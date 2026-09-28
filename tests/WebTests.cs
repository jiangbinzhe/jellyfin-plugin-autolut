using Jellyfin.Plugin.AutoLut;

public static class WebTests
{
    public static void Run(Action<bool, string> check)
    {
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
