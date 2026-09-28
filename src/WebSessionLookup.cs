using System.Security.Claims;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.AutoLut;

public static class WebSessionLookup
{
    // Web wrappers advertise their own client name. Match the authenticated identity,
    // never fall back to another session of the same user or a hard-coded client name.
    public static SessionInfo? Find(IEnumerable<SessionInfo> sessions, ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirst("Jellyfin-UserId")?.Value, out var user)) return null;
        var device = principal.FindFirst("Jellyfin-DeviceId")?.Value;
        var client = principal.FindFirst("Jellyfin-Client")?.Value;
        var version = principal.FindFirst("Jellyfin-Version")?.Value;
        if (string.IsNullOrEmpty(device) || string.IsNullOrEmpty(client) || string.IsNullOrEmpty(version)) return null;
        var matches = sessions.Where(s => s.UserId == user && s.DeviceId == device
            && s.Client == client && s.ApplicationVersion == version && s.IsActive
            && s.NowPlayingItem?.MediaType.ToString() == "Video").Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
