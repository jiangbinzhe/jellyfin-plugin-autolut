using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoLut;

public sealed record SessionPlan(Guid UserId, string DeviceId, Guid ItemId, string MediaPath, string CubePath, bool AssumeBt709 = false, bool ConvertSmpte170m = false, bool Deinterlace = false);

public sealed class LutService(ILogger<LutService> logger) : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionPlan> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _analysis = new(1, 1);
    private int _reserved;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    // Bound pending/unclosed sessions. Never evict on a timer or HLS encoder stop.
    public bool TryReserve(Configuration c) => Reserve(Math.Clamp(c.MaxSessions, 1, 64));
    private bool Reserve(int max) { if (Interlocked.Increment(ref _reserved) <= max) return true; Interlocked.Decrement(ref _reserved); return false; }
    public void Release(SessionPlan? plan) { if (plan != null) DeletePlan(plan); Interlocked.Decrement(ref _reserved); }
    public bool Bind(string session, SessionPlan plan) => _sessions.TryAdd(session, plan);
    public SessionPlan? Get(string? session) => session != null && _sessions.TryGetValue(session, out var plan) ? plan : null;

    public bool Complete(string? session, Guid user, string device, Guid item)
    {
        var plan = Get(session);
        if (plan is null || plan.UserId != user || plan.DeviceId != device || plan.ItemId != item
            || !_sessions.TryRemove(new KeyValuePair<string, SessionPlan>(session!, plan))) return false;
        Interlocked.Decrement(ref _reserved);
        // FFmpeg may still be stopping. Keep immutable files until offline cache maintenance.
        logger.LogInformation("AutoLut closed playback session; reservation released");
        return true;
    }

    public async Task<SessionPlan?> Prepare(Configuration c, Guid user, string device, Guid item, string media, MediaStream video, long ticks, CancellationToken ct)
    {
        // ASCII path prevents FFmpeg filter parser escapes and shell metacharacters.
        if (!System.Text.RegularExpressions.Regex.IsMatch(c.CacheDirectory, @"^/[A-Za-z0-9_/-]+$") || c.CacheDirectory.Contains("..")) return null;
        var assumeBt709 = Policy.NeedsBt709Assumption(video, c);
        var convertSmpte170m = c.EnableLegacySdr && Policy.IsSmpte170m(video);
        var deinterlace = c.EnableLegacySdr && video.IsInterlaced;
        var dir = Path.Combine(c.CacheDirectory, _runId, Guid.NewGuid().ToString("N"));
        await _analysis.WaitAsync(ct).ConfigureAwait(false);
        bool ready = false;
        try
        {
            Directory.CreateDirectory(dir);
            var cube = Path.Combine(dir, "lut.cube");
            if (c.Mode == "Fixed")
            {
                if (!Path.IsPathRooted(c.FixedCubePath) || new FileInfo(c.FixedCubePath).Length > 8_000_000) return null;
                var content = await File.ReadAllTextAsync(c.FixedCubePath, ct).ConfigureAwait(false);
                ValidateCube(content);
                await File.WriteAllTextAsync(cube, content, ct).ConfigureAwait(false);
            }
            else if (c.Mode == "Automatic")
            {
                int width = video.Width!.Value, height = video.Height!.Value;
                double ratio = 640d / Math.Max(width, height);
                int w = (int)Math.Round(width * ratio), h = (int)Math.Round(height * ratio);
                var raw = Path.Combine(dir, "frame.rgba");
                var time = (Math.Max(0, ticks) / (double)TimeSpan.TicksPerSecond).ToString("0.###", CultureInfo.InvariantCulture);
                await Run(c.FfmpegPath, ["-nostdin", "-v", "error", "-threads", "1", "-filter_threads", "1", "-noautorotate", "-ss", time,
                    "-i", media, "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-vf", AnalysisFilter(w, h, assumeBt709, convertSmpte170m, deinterlace), "-pix_fmt", "rgba", "-f", "rawvideo", "-n", raw], c, ct).ConfigureAwait(false);
                if (new FileInfo(raw).Length != w * h * 4) throw new InvalidDataException("Unexpected frame dimensions");
                var request = Path.Combine(dir, "frame.json");
                await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new { rgba = "frame.rgba", width = w, height = h }), ct).ConfigureAwait(false);
                var home = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
                var node = Path.Combine(home, "runtime", "node");
                WorkerRuntime.EnsureExecutable(node);
                await Run(node, [Path.Combine(home, "worker", "lut.cjs"), "analyze", request, cube], c, ct).ConfigureAwait(false);
                File.Delete(raw); File.Delete(request);
                if (!File.Exists(cube)) { logger.LogInformation("AutoLut skipped: no usable skin sample for item {Item}", item); return null; }
            }
            else return null;
            var plan = new SessionPlan(user, device, item, media, cube, assumeBt709, convertSmpte170m, deinterlace);
            ready = true;
            if (convertSmpte170m || deinterlace) logger.LogInformation("AutoLut normalized SDR plan: SMPTE170M={Convert}, deinterlace={Deinterlace}", convertSmpte170m, deinterlace);
            if (assumeBt709) logger.LogInformation("AutoLut assumes missing color tags are BT.709 for item {Item}; source unchanged", item);
            logger.LogInformation("AutoLut prepared {Mode} LUT for item {Item}; scope is one playback session", c.Mode, item);
            return plan;
        }
        finally
        {
            _analysis.Release();
            // Only remove scratch files. A successful cube is owned by its session until process shutdown.
            foreach (var name in new[] { "frame.rgba", "frame.json" }) { try { File.Delete(Path.Combine(dir, name)); } catch (IOException) { } }
            if (!ready) DeletePlan(new SessionPlan(user, device, item, media, Path.Combine(dir, "lut.cube")));
        }
    }

    public static string AnalysisFilter(int width, int height, bool assumeBt709, bool convertSmpte170m = false, bool deinterlace = false)
    {
        // One CPU analysis frame; continuous playback keeps Jellyfin's VAAPI deinterlacer.
        var prefix = deinterlace ? "bwdif=mode=send_frame:parity=auto:deint=all," : "";
        if (convertSmpte170m) return prefix + $"{CommandPatch.Smpte170mToBt709},scale={width}:{height}:in_color_matrix=bt709";
        return prefix + (assumeBt709 ? $"{CommandPatch.Bt709Parameters},scale={width}:{height}:in_color_matrix=bt709" : $"scale={width}:{height}");
    }

    public static void ValidateCube(string text)
    {
        var size = 0; var nodes = 0;
        foreach (var original in text.Split('\n'))
        {
            var line = original.Split('#')[0].Trim();
            if (line.Length == 0 || line.StartsWith("TITLE ")) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "LUT_3D_SIZE") { if (size != 0 || parts.Length != 2 || !int.TryParse(parts[1], out size) || size < 2 || size > 65) throw new InvalidDataException("Invalid cube size"); continue; }
            if (parts[0] is "DOMAIN_MIN" or "DOMAIN_MAX")
            {
                var expected = parts[0] == "DOMAIN_MIN" ? 0d : 1d;
                if (parts.Length != 4 || parts.Skip(1).Any(p => !double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v != expected)) throw new InvalidDataException("Only normalized cube domain is supported");
                continue;
            }
            if (parts.Length != 3 || parts.Any(p => !double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v) || v < 0 || v > 1)) throw new InvalidDataException("Invalid cube node");
            nodes++;
        }
        if (size == 0 || nodes != size * size * size) throw new InvalidDataException("Incomplete cube");
    }

    private static async Task Run(string executable, string[] arguments, Configuration c, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(c.AnalysisTimeoutSeconds, 3, 60)));
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        // Drain both pipes concurrently without retaining unbounded child output or logging media paths.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); await Task.WhenAll(stdout, stderr).ConfigureAwait(false); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException($"LUT worker failed (exit {process.ExitCode})");
    }

    private static void DeletePlan(SessionPlan plan)
    {
        try { File.Delete(plan.CubePath); File.Delete(plan.CubePath + ".json"); Directory.Delete(Path.GetDirectoryName(plan.CubePath)!); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { /* No early file deletion: Jellyfin may still be shutting down FFmpeg jobs. */ _analysis.Dispose(); }
}
