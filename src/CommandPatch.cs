using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoLut;

public static partial class CommandPatch
{
    public const string Bt709Parameters = "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709";
    public const string Smpte170mParameters = "setparams=color_primaries=smpte170m:color_trc=smpte170m:colorspace=smpte170m";
    public const string Smpte170mToBt709 = "format=yuv420p,colorspace=iall=smpte170m:all=bt709:format=yuv444p10:fast=0";
    [GeneratedRegex("(?:^|\\s)-vf\\s+\"(?<graph>[^\"]+)\"")]
    private static partial Regex Vf();
    [GeneratedRegex(@"^/[A-Za-z0-9_./-]+\.cube$")]
    private static partial Regex SafePath();
    [GeneratedRegex(@"(?:^|\s)-filter_hw_device\s+(?<device>[A-Za-z0-9_]+)(?:\s|$)")]
    private static partial Regex FilterDevice();

    public static bool TryApply(string command, string cube, out string changed, out string reason, bool assumeBt709 = false, bool convertSmpte170m = false, bool requireDeinterlace = false)
    {
        changed = command; reason = "unsupported-command";
        if (assumeBt709 && convertSmpte170m) return false;
        if (!SafePath().IsMatch(cube) || cube.Contains("..", StringComparison.Ordinal)) return false;
        if (command.Contains("-filter_complex", StringComparison.Ordinal) || command.Contains("-lavfi", StringComparison.Ordinal)) return false;
        var matches = Vf().Matches(command);
        if (matches.Count != 1) return false;
        var graph = matches[0].Groups["graph"];
        // Accept only the known simple Linux VAAPI decoder -> QSV encoder graph.
        const string tail = "hwmap=derive_device=qsv,format=qsv";
        if (!graph.Value.EndsWith(tail, StringComparison.Ordinal)
            || !Regex.IsMatch(command, @"(?:^|\s)-hwaccel\s+vaapi(?:\s|$)")
            || !Regex.IsMatch(command, @"(?:^|\s)-hwaccel_output_format\s+vaapi(?:\s|$)")
            || !Regex.IsMatch(command, @"(?:^|\s)-codec:v(?::0)?\s+h264_qsv(?:\s|$)|(?:^|\s)-c:v(?::0)?\s+h264_qsv(?:\s|$)")) return false;
        var prefix = graph.Value[..^tail.Length];
        if (prefix.Contains('[') || prefix.Contains(';') || prefix.Contains('"') || prefix.Contains('\\')) return false;
        var filters = prefix.TrimEnd(',').Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (filters.Any(f => !(f.StartsWith("scale_vaapi=", StringComparison.Ordinal) || f == "format=vaapi" || f == Bt709Parameters || f == Smpte170mParameters
            || f is "deinterlace_vaapi=rate=frame" or "deinterlace_vaapi=rate=field"))) return false;
        var deinterlacers = filters.Where(f => f.StartsWith("deinterlace_vaapi=", StringComparison.Ordinal)).ToArray();
        if (deinterlacers.Length != (requireDeinterlace ? 1 : 0)) return false;
        if (requireDeinterlace)
        {
            var deintIndex = Array.IndexOf(filters, deinterlacers[0]);
            var scaleIndex = Array.FindIndex(filters, f => f.StartsWith("scale_vaapi=", StringComparison.Ordinal));
            if (scaleIndex >= 0 && deintIndex > scaleIndex) return false;
        }
        if (convertSmpte170m || requireDeinterlace)
        {
            // Color-changing hardware scale options would invalidate the source declaration.
            if (filters.Where(f => f.StartsWith("scale_vaapi=", StringComparison.Ordinal)).Any(f =>
                !Regex.IsMatch(f, @"^scale_vaapi=(?:w=\d+:h=\d+:)?format=nv12:extra_hw_frames=\d+$"))) return false;
        }
        if (!convertSmpte170m && filters.Contains(Smpte170mParameters)) return false;
        if (convertSmpte170m)
        {
            // Keep source tags until the CPU primary/TRC/matrix transform is complete.
            prefix = Smpte170mParameters + "," + string.Join(",", filters.Where(f => f != Bt709Parameters && f != Smpte170mParameters));
            if (!prefix.EndsWith(',')) prefix += ",";
        }
        if (prefix.Contains("p010", StringComparison.Ordinal) || prefix.Contains("tonemap", StringComparison.Ordinal)) return false;
        var device = FilterDevice().Match(command);
        if (!device.Success) return false;
        var alias = device.Groups["device"].Value;
        if (!Regex.IsMatch(command, @"(?:^|\s)-init_hw_device\s+qsv=" + Regex.Escape(alias) + @"(?:@|:|\s)")) return false;
        // Keep float RGB precision; tetrahedral has optimized x86 SIMD paths in jellyfin-ffmpeg.
        var lut = $"hwdownload,format=nv12,format=gbrpf32le,lut3d=file={cube}:interp=tetrahedral,format=nv12,hwupload=extra_hw_frames=24,format=qsv";
        if (assumeBt709)
        {
            // Normalize frame metadata before any VAAPI scaling, then explicitly select
            // the same matrix for CPU YUV/RGB conversions as for the analysis frame.
            prefix = Bt709Parameters + "," + prefix;
            lut = $"hwdownload,format=nv12,{Bt709Parameters},scale=in_color_matrix=bt709,format=gbrpf32le,lut3d=file={cube}:interp=tetrahedral,scale=out_color_matrix=bt709,format=nv12,{Bt709Parameters},hwupload=extra_hw_frames=24,format=qsv";
        }
        if (convertSmpte170m)
            lut = $"hwdownload,format=nv12,{Smpte170mToBt709},scale=in_color_matrix=bt709,format=gbrpf32le,lut3d=file={cube}:interp=tetrahedral,scale=out_color_matrix=bt709,format=nv12,{Bt709Parameters},hwupload=extra_hw_frames=24,format=qsv";
        changed = command[..graph.Index] + prefix + lut + command[(graph.Index + graph.Length)..];
        reason = assumeBt709 ? "cpu-lut-qsv-assumed-bt709" : "cpu-lut-qsv";
        if (convertSmpte170m) reason += "-smpte170m-to-bt709";
        if (requireDeinterlace) reason += "-vaapi-deinterlace";
        return true;
    }
}
