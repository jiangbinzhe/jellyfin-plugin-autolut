using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoLut;

public sealed class PlaybackFilter(ILibraryManager library, IUserManager users, IMediaSourceManager sources, LutService luts, ILogger<PlaybackFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        SessionPlan? plan = null;
        bool reserved = false;
        var c = Plugin.Instance?.Configuration;
        if (c?.Enabled == true && context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "MediaInfo", ActionName: "GetPostedPlaybackInfo" }
            && context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var claim = context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value;
                var device = context.HttpContext.User.FindFirst("Jellyfin-DeviceId")?.Value ?? "";
                if (Guid.TryParse(claim, out var userId) && context.ActionArguments.TryGetValue("itemId", out var id) && id is Guid itemId
                    && Policy.Selected(c, userId, device, itemId))
                {
                    context.ActionArguments.TryGetValue("playbackInfoDto", out var body);
                    object? Value(string key, string property) => (context.ActionArguments.TryGetValue(key, out var value) ? value : null) ?? Policy.Read(body, property);
                    var requestedUser = Value("userId", "UserId");
                    var user = users.GetUserById(userId);
                    // Never override another user's request or grant transcoding permission.
                    if (user != null && (requestedUser is null || requestedUser is Guid uid && uid == userId)
                        && user.HasPermission(PermissionKind.EnableMediaPlayback)
                        && user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding)
                        && Policy.Read(body, "DeviceProfile") != null
                        && Value("liveStreamId", "LiveStreamId") is null)
                    {
                        var item = library.GetItemById<BaseItem>(itemId, user);
                        var media = item is null ? [] : sources.GetStaticMediaSources(item, false, user);
                        // Multiple editions/source switching is deliberately excluded in v0.1.
                        if (media.Count == 1 && Policy.Eligible(media[0], c, out var video)
                            && (Value("mediaSourceId", "MediaSourceId") is not string requestedSource || requestedSource == media[0].Id)
                            && (Value("subtitleStreamIndex", "SubtitleStreamIndex") is int sub ? sub < 0 : media[0].DefaultSubtitleStreamIndex is null or < 0)
                            && luts.TryReserve(c))
                        {
                            reserved = true;
                            var ticks = Value("startTimeTicks", "StartTimeTicks") is long t ? t : 0;
                            plan = await luts.Prepare(c, userId, device, itemId, media[0].Path, video!, ticks, context.HttpContext.RequestAborted).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning("AutoLut preparation skipped ({Error}); preserving normal playback", ex.GetType().Name);
            }
            catch (OperationCanceledException) { if (reserved) luts.Release(plan); throw; }
        }
        if (plan is null)
        {
            if (reserved) luts.Release(null);
            await next().ConfigureAwait(false);
            return;
        }
        // The query arguments take precedence over DTO values in Jellyfin 12.1.
        context.ActionArguments["enableDirectPlay"] = false;
        context.ActionArguments["enableDirectStream"] = false;
        context.ActionArguments["enableTranscoding"] = true;
        context.ActionArguments["allowVideoStreamCopy"] = false;
        bool bound = false;
        try
        {
            var executed = await next().ConfigureAwait(false);
            if (executed.Exception is null && executed.Result is ObjectResult { Value: PlaybackInfoResponse response }
                && response.ErrorCode is null && !string.IsNullOrEmpty(response.PlaySessionId)
                && response.MediaSources.Any(s => !string.IsNullOrEmpty(s.TranscodingUrl)))
                bound = luts.Bind(response.PlaySessionId, plan);
        }
        finally { if (!bound) luts.Release(plan); }
    }
}
