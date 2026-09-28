using System.Text;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Jellyfin.Plugin.AutoLut;

public sealed class WebStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<WebInjection>();
        next(app);
    };
}

public sealed class WebInjection(RequestDelegate next, IServerConfigurationManager server, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var prefix = server.GetNetworkConfiguration().BaseUrl.TrimEnd('/');
        var path = context.Request.Path.Value ?? "";
        var index = path.Equals(prefix + "/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals(prefix + "/web/", StringComparison.OrdinalIgnoreCase);
        if (!HttpMethods.IsGet(context.Request.Method) || !index || !configuration.HostWebClient()
            || Plugin.Instance?.Configuration.WebPlayerButton != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Only buffer the web entry document, never media/API responses. Let Jellyfin's
        // own static file middleware enforce file existence and normal routing first.
        var headers = new[] { "Accept-Encoding", "If-None-Match", "If-Modified-Since", "Range", "If-Range" };
        var saved = headers.ToDictionary(h => h, h => context.Request.Headers[h]);
        foreach (var h in headers) context.Request.Headers.Remove(h);
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            if (context.Response.StatusCode == 200 && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
                && !context.Response.Headers.ContainsKey("Content-Encoding") && bytes.Length <= 2 * 1024 * 1024)
            {
                var html = Encoding.UTF8.GetString(bytes);
                var insert = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (insert >= 0 && !html.Contains("id=\"autolut-web-script\"", StringComparison.Ordinal))
                {
                    // Relative URL supports a configured BaseUrl without inserting configuration into HTML.
                    html = html.Insert(insert, "<script id=\"autolut-web-script\" src=\"../AutoLut/Web/script.js?v=0.1.6\" defer></script>");
                    bytes = Encoding.UTF8.GetBytes(html);
                    context.Response.Headers.Remove("ETag");
                    context.Response.Headers.Remove("Last-Modified");
                    context.Response.Headers.Remove("Accept-Ranges");
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.ContentLength = bytes.Length;
                }
            }
            context.Response.Body = original;
            await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
            foreach (var (h, value) in saved) { if (value.Count == 0) context.Request.Headers.Remove(h); else context.Request.Headers[h] = value; }
        }
    }
}
