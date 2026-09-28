using Jellyfin.Plugin.AutoLut;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

public static class WebTests
{
    public static void Run(Action<bool, string> check)
    {
        var owner = Guid.NewGuid();
        ClaimsPrincipal Identity(string client, string device = "phone", string version = "1.8.0", Guid? user = null, bool authenticated = true) =>
            new(new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", (user ?? owner).ToString()),
                new Claim("Jellyfin-DeviceId", device), new Claim("Jellyfin-Client", client),
                new Claim("Jellyfin-Version", version) }, authenticated ? "test" : null));
        SessionInfo Session(string client) => new(null!, NullLogger.Instance) { UserId = owner,
            DeviceId = "phone", Client = client, ApplicationVersion = "1.8.0",
            NowPlayingItem = new BaseItemDto { Id = Guid.NewGuid(), MediaType = MediaType.Video } };
        foreach (var client in new[] { "Jellyfin Web", "Jellyfin Mobile (iOS)", "Jellyfin iOS" })
        {
            var session = Session(client);
            check(WebSessionLookup.Find([session], Identity(client)) == session, "wrapper selects own session: " + client);
            check(WebSessionLookup.Find([session], Identity(client, user: Guid.NewGuid())) is null, "reject other user: " + client);
            check(WebSessionLookup.Find([session], Identity(client, device: "other")) is null, "reject other device: " + client);
            check(WebSessionLookup.Find([session], Identity("foreign")) is null, "reject other client: " + client);
            check(WebSessionLookup.Find([session], Identity(client, version: "other")) is null, "reject other version: " + client);
            check(WebSessionLookup.Find([session, Session(client)], Identity(client)) is null, "reject ambiguous session: " + client);
            check(WebSessionLookup.Find([session], Identity(client, authenticated: false)) is null, "reject anonymous identity: " + client);
            check(WebSessionLookup.Find([session], Identity(client, device: "")) is null, "reject missing device: " + client);
            session.NowPlayingItem = null!;
            check(WebSessionLookup.Find([session], Identity(client)) is null, "reject absent playback: " + client);
        }
        var lib = Guid.NewGuid(); var uid = Guid.NewGuid(); var item = Guid.NewGuid();
        var c = new Configuration { Enabled = true, UserIds = uid.ToString(), LibraryIds = lib.ToString() };
        check(Policy.Selected(c, uid, "web", item, [lib]), "library scope includes its movie or episode");
        check(!Policy.Selected(c, uid, "web", item, [Guid.NewGuid()]), "library scope excludes other libraries");
        check(!Policy.Selected(c, Guid.NewGuid(), "web", item, [lib]), "library scope still requires user permission");
        c.DeviceIds = "phone";
        check(!Policy.Selected(c, uid, "web", item, [lib]), "library scope still requires selected device");
        c.DeviceIds = ""; c.ItemIds = item.ToString();
        check(Policy.Selected(c, uid, "web", item), "explicit media is additive to library selection");
        c.ItemIds = ""; c.LibraryIds = "invalid";
        check(!Policy.Selected(c, uid, "web", item, [Guid.Empty]), "invalid library IDs do not grant access");
        c.LibraryIds = "";
        check(!Policy.Selected(c, uid, "web", item, [lib]), "empty library and item lists fail closed");
        var p = new WebPreferences(); var u = Guid.NewGuid(); var i = Guid.NewGuid();
        check(p.Enabled(u, "browser", i), "web preference defaults to admin policy");
        check(p.Set(u, "browser", i, false) && !p.Enabled(u, "browser", i), "web opt-out saved");
        check(p.Enabled(Guid.NewGuid(), "browser", i), "web opt-out isolates users");
        check(p.Enabled(u, "phone", i), "web opt-out isolates devices");
        check(p.Enabled(u, "browser", Guid.NewGuid()), "web opt-out isolates media");
        check(p.Set(u, "browser", i, true) && p.Enabled(u, "browser", i), "web opt-out can be cleared");
        for (var n = 0; n < 512; n++) p.Set(u, "browser", Guid.NewGuid(), false);
        check(!p.Set(u, "browser", i, false), "web preference store has bounded capacity");
        check(p.Set(u, "browser", i, true), "full preference store never prevents enable");
    }
}
