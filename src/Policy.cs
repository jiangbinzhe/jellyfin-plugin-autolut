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
        // Unknown color metadata is intentionally not treated as SDR BT.709.
        return video.BitDepth == 8 && video.Width > 0 && video.Height > 0
            && video.Width <= Math.Clamp(c.MaxWidth, 1, 1920) && video.Height <= Math.Clamp(c.MaxHeight, 1, 1080)
            && video.ColorPrimaries == "bt709" && video.ColorTransfer == "bt709" && video.ColorSpace == "bt709"
            && (Read(video, "VideoRangeType")?.ToString() is null or "SDR")
            && Read(video, "DvProfile") is null
            && (video.Rotation is null or 0)
            && !video.IsInterlaced;
    }
    private static T? SingleOrDefaultSafe<T>(this IEnumerable<T> values, Func<T, bool> match) where T : class
    {
        var selected = values.Where(match).Take(2).ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }
}
