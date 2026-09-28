using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoLut;

public sealed class Registration : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Internal service contract is deliberately pinned; a future server must not silently opt in.
        var version = typeof(ITranscodeManager).Assembly.GetName().Version;
        if (version is null || version.Major != 12 || version.Minor != 1) return;
        var descriptor = services.LastOrDefault(s => s.ServiceType == typeof(ITranscodeManager));
        if (descriptor is null || descriptor.Lifetime != ServiceLifetime.Singleton || descriptor.ImplementationType?.FullName != "MediaBrowser.MediaEncoding.Transcoding.TranscodeManager") return;
        services.Remove(descriptor);
        services.AddSingleton<LutService>();
        services.AddScoped<PlaybackFilter>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<PlaybackFilter>());
        services.AddSingleton<ITranscodeManager>(sp => new LutTranscodeManager(
            (ITranscodeManager)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType),
            sp.GetRequiredService<LutService>(), sp.GetRequiredService<ILogger<LutTranscodeManager>>()));
    }
}
