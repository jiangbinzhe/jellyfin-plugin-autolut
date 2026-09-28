using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AutoLut;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

internal static class MobileTests
{
    public static void Run(Action<bool, string> check)
    {
        var original = new DeviceProfile {
            TranscodingProfiles = [
                new() { Type = DlnaProfileType.Video, Context = EncodingContext.Streaming, Protocol = MediaStreamProtocol.hls,
                    Container = "mp4", VideoCodec = "av1,hevc,h264,mpeg4", AudioCodec = "aac,ac3", EnableSubtitlesInManifest = true, MinSegments = 2 },
                new() { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, VideoCodec = "vc1" },
                new() { Type = DlnaProfileType.Video, Context = EncodingContext.Static, Protocol = MediaStreamProtocol.http, VideoCodec = "h264" }
            ],
            SubtitleProfiles = [new() { Format = "vtt", Method = SubtitleDeliveryMethod.Hls }]
        };
        var compatible = PlaybackCompatibility.ForLut(original)!;
        check(compatible.TranscodingProfiles.Length == 1 && compatible.TranscodingProfiles[0].VideoCodec == "h264", "Swiftfin negotiated codec limited to advertised H264");
        check(original.TranscodingProfiles[0].VideoCodec == "av1,hevc,h264,mpeg4", "client profile not mutated");
        var t = compatible.TranscodingProfiles[0];
        check(t.Container == "mp4" && t.AudioCodec == "aac,ac3" && t.EnableSubtitlesInManifest && t.MinSegments == 2, "fMP4 audio and HLS subtitle capabilities preserved");
        check(PlaybackCompatibility.ForLut(new DeviceProfile { TranscodingProfiles = [original.TranscodingProfiles[2]] }) is null, "download/static profile bypasses LUT");
        check(PlaybackCompatibility.ForLut(new DeviceProfile { TranscodingProfiles = [original.TranscodingProfiles[1]] }) is null, "client lacking H264 HLS bypasses LUT");

        var subtitle = new MediaStream { Type = MediaStreamType.Subtitle, Index = 3, Codec = "subrip" };
        var source = new MediaSourceInfo { MediaStreams = [subtitle], DefaultSubtitleStreamIndex = 3, SupportsDirectPlay = false,
            SupportsDirectStream = false, SupportsTranscoding = true, TranscodingSubProtocol = MediaStreamProtocol.hls, TranscodingUrl = "/test/master.m3u8" };
        check(PlaybackCompatibility.CanPrepareSubtitle(source, null, compatible, false), "Swiftfin native text subtitle accepted for negotiation");
        check(!PlaybackCompatibility.CanPrepareSubtitle(source, 3, compatible, true), "explicit subtitle burn-in bypasses LUT");
        check(PlaybackCompatibility.CanPrepareSubtitle(source, -1, compatible, true), "explicit subtitles off overrides default");
        subtitle.Codec = "pgssub";
        check(!PlaybackCompatibility.CanPrepareSubtitle(source, 3, compatible, false), "image subtitle excluded");
        subtitle.Codec = "subrip";
        subtitle.DeliveryMethod = SubtitleDeliveryMethod.Encode;
        check(!PlaybackCompatibility.CanBind(source), "negotiated burn-in cannot bind LUT");
        subtitle.DeliveryMethod = SubtitleDeliveryMethod.Hls;
        check(PlaybackCompatibility.CanBind(source), "HLS subtitle playlist can coexist with LUT");
        subtitle.DeliveryMethod = SubtitleDeliveryMethod.External;
        check(!PlaybackCompatibility.CanBind(source), "external subtitle requires delivery URL");
        subtitle.DeliveryUrl = "/test/subtitles.vtt";
        check(PlaybackCompatibility.CanBind(source), "external subtitle URL preserved");
        source.SupportsDirectPlay = true;
        check(!PlaybackCompatibility.CanBind(source), "direct play response cannot masquerade as graded transcode");
    }
}
