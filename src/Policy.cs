using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AutoLut;

public static class Policy
{
    public static bool SelectedActor(Configuration c, Guid user, string device)
    {
        var users = Split(c.UserIds); var devices = Split(c.DeviceIds);
        return c.Enabled && users.Any(x => Guid.TryParse(x, out var id) && id == user)
            && (devices.Length == 0 || devices.Contains(device, StringComparer.Ordinal));
    }
    public static bool Selected(Configuration c, Guid user, string device, Guid item, IEnumerable<Guid>? libraries = null)
    {
        if (!SelectedActor(c, user, device)) return false;
        if (Split(c.ItemIds).Any(x => Guid.TryParse(x, out var id) && id == item)) return true;
        var selected = Split(c.LibraryIds).Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).ToHashSet();
        return libraries?.Any(selected.Contains) == true;
    }
    private static string[] Split(string text) => text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    public static object? Read(object? obj, string property) => obj?.GetType().GetProperty(property)?.GetValue(obj);
    public static bool Eligible(MediaSourceInfo source, Configuration c, out MediaStream? video)
    {
        video = source.MediaStreams.SingleOrDefaultSafe(s => s.Type == MediaStreamType.Video);
        if (video is null || source.RequiresOpening || source.IsInfiniteStream || source.Protocol.ToString() != "File"
            || !Path.IsPathRooted(source.Path) || !File.Exists(source.Path)) return false;
        // Unknown color metadata requires explicit administrator opt-in. Known non-709 is never overwritten.
        return video.BitDepth == 8 && video.Width > 0 && video.Height > 0
            && video.Width <= (c.Allow4kSdr ? 3840 : Math.Clamp(c.MaxWidth, 1, 1920))
            && video.Height <= (c.Allow4kSdr ? 2160 : Math.Clamp(c.MaxHeight, 1, 1080))
            && ((ColorAccepted(video.ColorPrimaries, c) && ColorAccepted(video.ColorTransfer, c) && ColorAccepted(video.ColorSpace, c))
                || c.EnableLegacySdr && IsSmpte170m(video) && video.Width % 2 == 0 && video.Height % 2 == 0)
            && (Read(video, "VideoRangeType")?.ToString() is null or "SDR"
                || NeedsBt709Assumption(video, c) && Read(video, "VideoRangeType")?.ToString() == "Unknown")
            && Read(video, "DvProfile") is null && Read(video, "DvLevel") is null
            && Read(video, "RpuPresentFlag") is not 1 && Read(video, "ElPresentFlag") is not 1
            && Read(video, "Hdr10PlusPresentFlag") is not true
            && (video.Rotation is null or 0)
            && (!video.IsInterlaced || c.EnableLegacySdr);
    }
    public static bool IsSmpte170m(MediaStream video) => video.ColorPrimaries == "smpte170m"
        && video.ColorTransfer == "smpte170m" && video.ColorSpace == "smpte170m";
    private static bool MissingColor(string? value) => string.IsNullOrWhiteSpace(value)
        || value is "unknown" or "unspecified";
    private static bool ColorAccepted(string? value, Configuration c) => value == "bt709"
        || c.AssumeUnspecifiedBt709 && MissingColor(value);
    public static bool NeedsBt709Assumption(MediaStream video, Configuration c) => c.AssumeUnspecifiedBt709
        && video.BitDepth == 8 && ColorAccepted(video.ColorPrimaries, c)
        && ColorAccepted(video.ColorTransfer, c) && ColorAccepted(video.ColorSpace, c)
        && (MissingColor(video.ColorPrimaries) || MissingColor(video.ColorTransfer) || MissingColor(video.ColorSpace));

    private static T? SingleOrDefaultSafe<T>(this IEnumerable<T> values, Func<T, bool> match) where T : class
    {
        var selected = values.Where(match).Take(2).ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }
}
