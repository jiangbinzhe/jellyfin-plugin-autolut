using Jellyfin.Data.Enums;
using System.Text.Json;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
namespace Jellyfin.Plugin.AutoLut;

public static class PlaybackCompatibility
{
    // Respect advertised formats. Download profiles have no streaming HLS entry.
    // Clone before narrowing: shared profiles and unrelated requests stay unchanged.
    public static DeviceProfile? ForLut(DeviceProfile profile)
    {
        var copy = JsonSerializer.Deserialize<DeviceProfile>(JsonSerializer.Serialize(profile))!;
        var video = copy.TranscodingProfiles.Where(p => p.Type == DlnaProfileType.Video
            && p.Context == EncodingContext.Streaming && p.Protocol == MediaStreamProtocol.hls
            && p.VideoCodec.Split(',').Any(c => c.Trim().Equals("h264", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (video.Length == 0) return null;
        foreach (var p in video) p.VideoCodec = "h264";
        copy.TranscodingProfiles = copy.TranscodingProfiles.Where(p => p.Type != DlnaProfileType.Video).Concat(video).ToArray();
        return copy;
    }

    public static bool CanPrepareSubtitle(MediaSourceInfo source, int? index, DeviceProfile profile, bool burnIn)
    {
        var selected = index ?? source.DefaultSubtitleStreamIndex;
        if (selected is null or < 0) return true;
        var stream = source.MediaStreams.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.Index == selected);
        // Start with plain text. ASS/PGS and burn-in remain outside the supported graph.
        return !burnIn && stream != null && IsPlainText(stream.Codec)
            && profile.SubtitleProfiles.Any(p => (p.Method is SubtitleDeliveryMethod.External or SubtitleDeliveryMethod.Hls) && IsPlainText(p.Format));
    }

    public static bool CanBind(MediaSourceInfo source)
    {
        if (source.SupportsDirectPlay || source.SupportsDirectStream || !source.SupportsTranscoding
            || source.TranscodingSubProtocol != MediaStreamProtocol.hls || string.IsNullOrEmpty(source.TranscodingUrl)) return false;
        // The server makes the final delivery decision; preflight alone is insufficient.
        var selected = source.DefaultSubtitleStreamIndex;
        if (selected is null or < 0) return true;
        var stream = source.MediaStreams.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.Index == selected);
        return stream?.DeliveryMethod == SubtitleDeliveryMethod.Hls || (stream?.DeliveryMethod == SubtitleDeliveryMethod.External && !string.IsNullOrEmpty(stream.DeliveryUrl));
    }

    private static bool IsPlainText(string? codec) => codec?.ToLowerInvariant() is "srt" or "subrip" or "vtt" or "webvtt" or "ttml";
}
