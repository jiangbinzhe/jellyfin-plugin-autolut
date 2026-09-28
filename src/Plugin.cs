using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AutoLut;

public sealed class Plugin : BasePlugin<Configuration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public override string Name => "Auto LUT (Preview)";
    public override Guid Id => Guid.Parse("70a81c22-b61d-4bdd-bd86-2048e0b29a5a");
    public override string Description => "Session-scoped SDR automatic LUT. Jellyfin 12.1 preview.";
    public IEnumerable<PluginPageInfo> GetPages() => new[] { new PluginPageInfo {
        Name = "AutoLut", EmbeddedResourcePath = "Jellyfin.Plugin.AutoLut.Configuration.html" } };
}

public sealed class Configuration : BasePluginConfiguration
{
    public bool Enabled { get; set; }
    public bool Allow4kSdr { get; set; }
    public bool AssumeUnspecifiedBt709 { get; set; }
    public bool WebPlayerButton { get; set; } = true;
    public string UserIds { get; set; } = "";
    public string DeviceIds { get; set; } = "";
    public string ItemIds { get; set; } = "";
    public string LibraryIds { get; set; } = "";
    public string Mode { get; set; } = "Automatic";
    public string FixedCubePath { get; set; } = "";
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public int MaxSessions { get; set; } = 8;
    public int AnalysisTimeoutSeconds { get; set; } = 20;
    public string CacheDirectory { get; set; } = "/cache/autolut";
    public string FfmpegPath { get; set; } = "/usr/lib/jellyfin-ffmpeg/ffmpeg";
}
