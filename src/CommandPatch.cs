using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoLut;

public static partial class CommandPatch
{
    [GeneratedRegex("(?:^|\\s)-vf\\s+\"(?<graph>[^\"]+)\"")]
    private static partial Regex Vf();
    [GeneratedRegex(@"^/[A-Za-z0-9_./-]+\.cube$")]
    private static partial Regex SafePath();
    [GeneratedRegex(@"(?:^|\s)-filter_hw_device\s+(?<device>[A-Za-z0-9_]+)(?:\s|$)")]
    private static partial Regex FilterDevice();

    public static bool TryApply(string command, string cube, out string changed, out string reason)
    {
        changed = command; reason = "unsupported-command";
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
        if (filters.Any(f => !(f.StartsWith("scale_vaapi=", StringComparison.Ordinal) || f == "format=vaapi" || f == "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709"))) return false;
        if (prefix.Contains("p010", StringComparison.Ordinal) || prefix.Contains("tonemap", StringComparison.Ordinal)) return false;
        var device = FilterDevice().Match(command);
        if (!device.Success) return false;
        var alias = device.Groups["device"].Value;
        if (!Regex.IsMatch(command, @"(?:^|\s)-init_hw_device\s+qsv=" + Regex.Escape(alias) + @"(?:@|:|\s)")) return false;
        var lut = $"hwdownload,format=nv12,format=gbrpf32le,lut3d=file={cube}:interp=trilinear,format=nv12,hwupload=extra_hw_frames=24,format=qsv";
        changed = command[..graph.Index] + prefix + lut + command[(graph.Index + graph.Length)..];
        reason = "cpu-lut-qsv";
        return true;
    }
}
