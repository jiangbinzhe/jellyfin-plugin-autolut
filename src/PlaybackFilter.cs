using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoLut;

public sealed class PlaybackFilter(ILibraryManager library, IUserManager users, IMediaSourceManager sources, IUserDataManager userData, LutService luts, WebPreferences webPreferences, ILogger<PlaybackFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // A playback-stop report is terminal. HLS stop-encoding can mean seek or track switch
        // and must NOT release the plan: the same PlaySessionId can start FFmpeg again.
        if (context.ActionDescriptor is ControllerActionDescriptor { ControllerName: "Playstate", ActionName: "ReportPlaybackStopped" }
            && context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var completed = await next().ConfigureAwait(false);
            if (completed.Exception is null && !completed.Canceled
                && completed.Result is not IStatusCodeActionResult { StatusCode: >= 400 }
                && context.ActionArguments.TryGetValue("playbackStopInfo", out var stop) && stop is PlaybackStopInfo info
                && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var owner))
                luts.Complete(info.PlaySessionId, owner, context.HttpContext.User.FindFirst("Jellyfin-DeviceId")?.Value ?? "", info.ItemId);
            return;
        }
        DeviceProfile? preparedProfile = null;
        object? preparedBody = null;
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
                    && Policy.SelectedActor(c, userId, device)
                    && webPreferences.Enabled(userId, device, itemId))
                {
                    context.ActionArguments.TryGetValue("playbackInfoDto", out var body);
                    object? Value(string key, string property) => (context.ActionArguments.TryGetValue(key, out var value) ? value : null) ?? Policy.Read(body, property);
                    var requestedUser = Value("userId", "UserId");
                    var user = users.GetUserById(userId);
                    // Never override another user's request or grant transcoding permission.
                    if (user != null && (requestedUser is null || requestedUser is Guid uid && uid == userId)
                        && user.HasPermission(PermissionKind.EnableMediaPlayback)
                        && user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding)
                        && Policy.Read(body, "DeviceProfile") is DeviceProfile profile
                        && PlaybackCompatibility.ForLut(profile) is DeviceProfile compatible
                        && Value("liveStreamId", "LiveStreamId") is null)
                    {
                        var item = library.GetItemById<BaseItem>(itemId, user);
                        var media = item is null ? [] : sources.GetStaticMediaSources(item, false, user);
                        // Multiple editions/source switching is deliberately excluded in v0.1.
                        if (item != null && Policy.Selected(c, userId, device, itemId, library.GetCollectionFolders(item).Select(f => f.Id))
                            && media.Count == 1 && Policy.Eligible(media[0], c, out var video)
                            && (Value("mediaSourceId", "MediaSourceId") is not string requestedSource || requestedSource == media[0].Id)
                            && PlaybackCompatibility.CanPrepareSubtitle(media[0], Value("subtitleStreamIndex", "SubtitleStreamIndex") as int?, compatible,
                                Policy.Read(body, "AlwaysBurnInSubtitleWhenTranscoding") is true)
                            && luts.TryReserve(c))
                        {
                            reserved = true;
                            preparedProfile = compatible;
                            preparedBody = body;
                            var ticks = Value("startTimeTicks", "StartTimeTicks") is long t ? Math.Max(0, t) : Math.Max(0, (userData.GetUserData(user, item!)?.PlaybackPositionTicks ?? 0));
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
        // Preserve bitrate, audio, start position and subtitle fields on the per-request DTO.
        preparedBody!.GetType().GetProperty("DeviceProfile")!.SetValue(preparedBody, preparedProfile);
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
                && response.MediaSources.Count == 1 && PlaybackCompatibility.CanBind(response.MediaSources[0]))
                bound = luts.Bind(response.PlaySessionId, plan);
        }
        finally { if (!bound) luts.Release(plan); }
    }
}
