using System.Diagnostics;
using System.Text.Json;
using Jellyfin.Plugin.AutoLut;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

public static class ColorCompatibilityTests
{
    public static void PolicyChecks(Action<bool, string> check, MediaSourceInfo source, MediaStream video)
    {
        var c = new Configuration();
        check(!c.Allow4kSdr, "4K compatibility is disabled by default");
        check(!c.AssumeUnspecifiedBt709, "color assumption is disabled by default");
        video.ColorPrimaries = video.ColorTransfer = video.ColorSpace = null;
        check(!Policy.Eligible(source, c, out _), "untagged source remains excluded without opt-in");
        c.AssumeUnspecifiedBt709 = true;
        check(Policy.Eligible(source, c, out _) && Policy.NeedsBt709Assumption(video, c), "opt-in accepts untagged 8-bit video");
        check(video.ColorPrimaries is null && video.ColorTransfer is null && video.ColorSpace is null, "eligibility never mutates shared metadata");
        video.ColorSpace = "bt709";
        check(Policy.Eligible(source, c, out _), "partial BT709 tags can be completed");
        foreach (var transfer in new[] { "smpte2084", "arib-std-b67", "gamma22", "reserved" })
        {
            video.ColorTransfer = transfer;
            check(!Policy.Eligible(source, c, out _), "known transfer is not overwritten: " + transfer);
        }
        video.ColorTransfer = null;
        video.ColorPrimaries = "bt2020"; check(!Policy.Eligible(source, c, out _), "known BT2020 primaries excluded");
        video.ColorPrimaries = null; video.ColorSpace = "smpte170m";
        check(!Policy.Eligible(source, c, out _), "known BT601 matrix excluded");
        video.ColorSpace = "unknown"; video.ColorTransfer = "unspecified";
        check(Policy.Eligible(source, c, out _), "explicit unspecified metadata can be completed");
        video.BitDepth = 10; check(!Policy.Eligible(source, c, out _), "compatibility keeps 10-bit exclusion"); video.BitDepth = 8;
        video.DvProfile = 8; check(!Policy.Eligible(source, c, out _), "compatibility keeps Dolby Vision exclusion"); video.DvProfile = null;
        video.RpuPresentFlag = 1; check(!Policy.Eligible(source, c, out _), "orphan Dolby Vision RPU rejected"); video.RpuPresentFlag = null;
        video.ElPresentFlag = 1; check(!Policy.Eligible(source, c, out _), "orphan Dolby Vision enhancement layer rejected"); video.ElPresentFlag = null;
        video.Hdr10PlusPresentFlag = true; check(!Policy.Eligible(source, c, out _), "orphan HDR10+ metadata rejected"); video.Hdr10PlusPresentFlag = null;
        var width = video.Width;
        video.Width = 3840; check(!Policy.Eligible(source, c, out _), "compatibility keeps 4K exclusion"); video.Width = width;
        video.Rotation = 90; check(!Policy.Eligible(source, c, out _), "compatibility keeps rotation exclusion"); video.Rotation = null;
        video.IsInterlaced = true; check(!Policy.Eligible(source, c, out _), "compatibility keeps interlace exclusion"); video.IsInterlaced = false;
        video.ColorPrimaries = video.ColorTransfer = video.ColorSpace = "bt709";
        check(!Policy.NeedsBt709Assumption(video, c) && Policy.Eligible(source, c, out _), "explicit BT709 retains original pipeline");
        var originalWidth = video.Width; var originalHeight = video.Height;
        video.Width = 3840; video.Height = 2160;
        check(!Policy.Eligible(source, c, out _), "4K source excluded until enabled");
        c.Allow4kSdr = true; c.AssumeUnspecifiedBt709 = false;
        check(Policy.Eligible(source, c, out _), "4K UHD BT709 SDR accepted with opt-in");
        video.Width = 2560; video.Height = 1440;
        check(Policy.Eligible(source, c, out _), "1440p accepted within UHD cap");
        video.Width = 3840; video.Height = 2160;
        video.ColorPrimaries = video.ColorTransfer = video.ColorSpace = null;
        check(!Policy.Eligible(source, c, out _), "4K option alone cannot assume missing colors");
        c.AssumeUnspecifiedBt709 = true;
        check(Policy.Eligible(source, c, out _), "independent 4K and missing-color options compose");
        foreach (var size in new[] { (3841, 2160), (3840, 2161), (4096, 2160), (7680, 4320), (2160, 3840) })
        {
            video.Width = size.Item1; video.Height = size.Item2;
            check(!Policy.Eligible(source, c, out _), "resolution above UHD cap rejected: " + size);
        }
        c.MaxWidth = c.MaxHeight = 99999; video.Width = 7680; video.Height = 4320;
        check(!Policy.Eligible(source, c, out _), "hidden maximum fields cannot bypass UHD hard cap");
        video.Width = 3840; video.Height = 2160; video.BitDepth = 10;
        check(!Policy.Eligible(source, c, out _), "4K option cannot admit 10-bit SDR"); video.BitDepth = 8;
        video.ColorTransfer = "smpte2084";
        check(!Policy.Eligible(source, c, out _), "4K option cannot admit PQ HDR");
        video.ColorTransfer = "arib-std-b67";
        check(!Policy.Eligible(source, c, out _), "4K option cannot admit HLG HDR"); video.ColorTransfer = null;
        video.DvProfile = 8; check(!Policy.Eligible(source, c, out _), "4K option cannot admit Dolby Vision"); video.DvProfile = null;
        video.IsInterlaced = true; check(!Policy.Eligible(source, c, out _), "4K option cannot admit interlaced source"); video.IsInterlaced = false;
        c.Allow4kSdr = false;
        check(!Policy.Eligible(source, c, out _), "disabling 4K restores original input cap");
        video.Width = originalWidth; video.Height = originalHeight;
        video.ColorPrimaries = video.ColorTransfer = video.ColorSpace = "bt709";
    }

    public static async Task PixelChecks(Action<bool, string> check, string ffmpeg, string temp, string command, string cube)
    {
        check(CommandPatch.TryApply(command, cube, out var patched, out var reason, true), "compatibility patches validated QSV graph");
        check(reason == "cpu-lut-qsv-assumed-bt709", "applied log distinguishes assumed color");
        check(patched.Contains(CommandPatch.Bt709Parameters + ",scale_vaapi"), "assumption precedes VAAPI scaling");
        check(!CommandPatch.TryApply(command.Replace("nv12", "p010le"), cube, out _, out _, true), "assumption cannot admit 10-bit graph");
        var graph = patched.Split("hwdownload,")[1].Split(",hwupload=")[0];
        async Task<byte[]> Run(string executable, params string[] args)
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            using var output = new MemoryStream();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.BaseStream.CopyToAsync(output);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("Color test failed: " + await error);
            await error;
            return output.ToArray();
        }
        async Task<byte[]> Pixels(string filter, string format = "nv12") => await Run(ffmpeg,
            "-nostdin", "-v", "error", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc2=s=64x36:r=3", "-frames:v", "3",
            "-vf", filter, "-pix_fmt", format, "-c:v", "rawvideo", "-threads:v", "1", "-f", "rawvideo", "pipe:1");
        var actual = await Pixels(graph);
        var reference = await Pixels("scale=in_color_matrix=bt709,format=gbrpf32le,scale=out_color_matrix=bt709,format=nv12");
        var wrongMatrix = await Pixels("scale=in_color_matrix=bt601,format=gbrpf32le,scale=out_color_matrix=bt709,format=nv12");
        check(actual.Length == 64 * 36 * 3 / 2 * 3, "assumed BT709 graph emits every frame");
        check(actual.Zip(reference, (a,b) => Math.Abs(a-b)).Max() <= 1, "untagged identity LUT matches explicit BT709 reference");
        check(actual.Zip(wrongMatrix, (a,b) => Math.Abs(a-b)).Max() > 5, "color test detects wrong BT601 input interpretation");
        var uhd = await Run(ffmpeg, "-nostdin", "-v", "error", "-filter_threads", "2", "-f", "lavfi", "-i", "testsrc2=s=3840x2160:r=1",
            "-frames:v", "1", "-vf", graph, "-pix_fmt", "nv12", "-c:v", "rawvideo", "-threads:v", "1", "-f", "rawvideo", "pipe:1");
        check(uhd.Length == 3840 * 2160 * 3 / 2 && uhd.Max() - uhd.Min() > 100, "actual UHD frame passes the assumed-BT709 CPU LUT graph");
        var analysis = await Pixels(LutService.AnalysisFilter(32, 18, true), "rgba");
        var analysisReference = await Pixels("scale=32:18:in_color_matrix=bt709", "rgba");
        check(analysis.SequenceEqual(analysisReference), "analysis reads untagged pixels with the BT709 matrix");
        var encoded = Path.Combine(temp, "assumed-color.mkv");
        await Run(ffmpeg, "-nostdin", "-v", "error", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc2=s=64x36:r=3", "-frames:v", "3", "-vf", graph, "-c:v", "libx264", "-threads:v", "1", "-y", encoded);
        using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe"), "-v", "error", "-show_entries", "stream=color_space,color_transfer,color_primaries", "-of", "json", encoded));
        var tags = probe.RootElement.GetProperty("streams")[0];
        check(new[] { "color_space", "color_transfer", "color_primaries" }.All(k => tags.GetProperty(k).GetString() == "bt709"), "software encoded result retains all BT709 output tags");

        var c = new Configuration { Mode = "Fixed", FixedCubePath = cube, CacheDirectory = Path.Combine(temp, "assumed-cache"), AssumeUnspecifiedBt709 = true };
        var video = new MediaStream { Type = MediaStreamType.Video, BitDepth = 8, Width = 64, Height = 36 };
        using var service = new LutService(NullLogger<LutService>.Instance);
        var plan = await service.Prepare(c, Guid.NewGuid(), "test", Guid.NewGuid(), encoded, video, 0, CancellationToken.None);
        c.AssumeUnspecifiedBt709 = false;
        check(plan?.AssumeBt709 == true, "color assumption frozen in fixed-LUT session after configuration changes");
    }
}
