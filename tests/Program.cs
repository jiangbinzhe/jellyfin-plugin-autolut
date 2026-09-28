using Jellyfin.Plugin.AutoLut;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS " + name); }
var user = Guid.NewGuid(); var item = Guid.NewGuid();
var config = new Configuration { Enabled = true, UserIds = user.ToString(), ItemIds = item.ToString() };
Check(Policy.Selected(config, user, "tv", item), "selected user and media");
Check(!Policy.Selected(config, Guid.NewGuid(), "tv", item), "other user excluded");
Check(!Policy.Selected(config, user, "tv", Guid.NewGuid()), "other media excluded");
config.DeviceIds = "tv";
Check(!Policy.Selected(config, user, "mobile", item), "device whitelist");
config.UserIds = "";
Check(!Policy.Selected(config, user, "tv", item), "empty whitelist fails closed");
config.UserIds = user.ToString(); config.Enabled = false;
Check(!Policy.Selected(config, user, "tv", item), "disabled policy");

const string cube = "/cache/autolut/test/lut.cube";
const string command = "-init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -filter_hw_device qs -hwaccel vaapi -hwaccel_output_format vaapi -i \"/media/file with spaces.mkv\" -codec:v:0 h264_qsv -vf \"scale_vaapi=w=1280:h=720:format=nv12:extra_hw_frames=24,hwmap=derive_device=qsv,format=qsv\" -codec:a copy -f hls \"/cache/transcodes/session.m3u8\"";
Check(CommandPatch.TryApply(command, cube, out var patched, out _), "known VAAPI to QSV command");
Check(patched.Contains("hwdownload,format=nv12,format=gbrpf32le,lut3d=file=" + cube), "LUT inserted in RGB domain");
Check(patched.Contains("hwupload=extra_hw_frames=24,format=qsv"), "QSV upload restored");
Check(patched.EndsWith("-codec:a copy -f hls \"/cache/transcodes/session.m3u8\""), "audio and cache path unchanged");
Check(!CommandPatch.TryApply(patched, cube, out _, out _), "cannot double apply");
foreach (var bad in new[] { command.Replace("h264_qsv", "copy"), command.Replace("nv12", "p010le"), command.Replace("scale_vaapi", "tonemap_vaapi"), command + " -filter_complex \"null\"", command.Replace("qsv=qs@va", "vaapi=qs:/dev/dri/renderD128"), command.Replace("-vf", "-filter:v"), command.Replace("scale_vaapi=", "subtitles=") })
    Check(!CommandPatch.TryApply(bad, cube, out var original, out _) && original == bad, "unsupported graph unchanged");
foreach (var bad in new[] { "/cache/evil'file.cube", "/cache/../evil.cube", "/cache/evil;file.cube", "relative.cube" })
    Check(!CommandPatch.TryApply(command, bad, out _, out _), "unsafe cube path rejected");
const string identity = "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n";
LutService.ValidateCube(identity); Check(true, "valid cube accepted");
foreach (var bad in new[] { identity.Replace("1 1 1", "NaN 1 1"), identity.Replace("1 1 1", "2 1 1"), identity + "LUT_3D_SIZE 2", identity + "DOMAIN_MIN -1 -1 -1", "LUT_3D_SIZE 2\n0 0 0" })
{ bool rejected = false; try { LutService.ValidateCube(bad); } catch (InvalidDataException) { rejected = true; } Check(rejected, "malformed cube rejected"); }

var temporary = Path.Combine(Path.GetTempPath(), "autolut-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    if (OperatingSystem.IsLinux())
    {
        var executable = Path.Combine(temporary, "node-fixture");
        File.WriteAllText(executable, "fixture");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        WorkerRuntime.EnsureExecutable(executable);
        Check(File.GetUnixFileMode(executable) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "GUI-extracted worker gains owner execute only");
    }
    var media = Path.Combine(temporary, "sample.mkv"); File.WriteAllText(media, "test");
    var stream = new MediaStream { Type = MediaStreamType.Video, Index = 0, BitDepth = 8, Width = 1280, Height = 720, ColorPrimaries = "bt709", ColorTransfer = "bt709", ColorSpace = "bt709" };
    var source = new MediaSourceInfo { Path = media, Protocol = MediaProtocol.File, MediaStreams = new[] { stream } };
    Check(Policy.Eligible(source, config, out _), "explicit BT709 SDR accepted");
    stream.BitDepth = 10; Check(!Policy.Eligible(source, config, out _), "10 bit excluded"); stream.BitDepth = 8;
    stream.ColorTransfer = "smpte2084"; Check(!Policy.Eligible(source, config, out _), "HDR excluded");
    stream.ColorTransfer = null; Check(!Policy.Eligible(source, config, out _), "unknown transfer excluded"); stream.ColorTransfer = "bt709";
    stream.Width = 3840; Check(!Policy.Eligible(source, config, out _), "4K excluded from CPU preview"); stream.Width = 1280;
    stream.IsInterlaced = true; Check(!Policy.Eligible(source, config, out _), "interlaced excluded"); stream.IsInterlaced = false;
    var input = Path.Combine(temporary, "input.cube"); File.WriteAllText(input, identity);
    config.Mode = "Fixed"; config.FixedCubePath = input; config.CacheDirectory = temporary + "/cache"; config.MaxSessions = 1;
    using var service = new LutService(NullLogger<LutService>.Instance);
    Check(service.TryReserve(config) && !service.TryReserve(config), "bounded session allocation");
    var plan = await service.Prepare(config, user, "tv", item, media, stream, 0, CancellationToken.None);
    Check(plan != null && File.ReadAllText(plan.CubePath) == identity, "immutable fixed LUT copy");
    File.WriteAllText(input, "changed"); Check(File.ReadAllText(plan!.CubePath) == identity, "source update cannot mutate active session");
    Check(service.Bind("session1", plan) && !service.Bind("session1", plan), "unique session binding");
    Check(service.Get("session1") == plan && service.Get("other") == null, "session isolation");
    service.Release(plan); Check(!File.Exists(plan.CubePath) && service.TryReserve(config), "failed playback releases files and reservation");
    var ffmpeg = Environment.GetEnvironmentVariable("AUTOLUT_TEST_FFMPEG");
    if (!string.IsNullOrEmpty(ffmpeg))
    {
        async Task Generate(string color)
        {
            var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=" + color + ":s=640x360:r=1", "-frames:v", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-y", media }) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!;
            var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            Check(process.ExitCode == 0, "synthetic frame generated " + color + " " + error);
        }
        config.Mode = "Automatic"; config.FfmpegPath = ffmpeg; stream.Width = 640; stream.Height = 360;
        await Generate("0xc18d73");
        var automatic = await service.Prepare(config, user, "tv", item, media, stream, 0, CancellationToken.None);
        Check(automatic != null, "real FFmpeg extraction and bundled Node analysis");
        LutService.ValidateCube(File.ReadAllText(automatic!.CubePath));
        Check(File.Exists(automatic.CubePath + ".json"), "automatic parameters and LUT hash recorded");
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(automatic.CubePath)!, "frame.rgba")), "raw frame removed after analysis");
        await Generate("blue");
        var noSkin = await service.Prepare(config, user, "tv", item, media, stream, 0, CancellationToken.None);
        Check(noSkin is null, "no-skin sample preserves ordinary playback");
    }
}
finally { Directory.Delete(temporary, true); }
Console.WriteLine($"PASS {count} assertions; Jellyfin API assembly {typeof(MediaBrowser.Controller.MediaEncoding.ITranscodeManager).Assembly.GetName().Version}");
