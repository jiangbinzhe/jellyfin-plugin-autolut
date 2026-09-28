using System.ComponentModel.DataAnnotations;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AutoLut;

public sealed class WebToggleRequest
{
    [Required] public string SessionId { get; set; } = "";
    public Guid ItemId { get; set; }
    [Required] public bool? Enabled { get; set; }
    [Range(0, long.MaxValue)] public long? PositionTicks { get; set; }
}

[ApiController]
[Route("AutoLut/Web")]
public sealed class WebController(ISessionManager sessions, IUserManager users, ILibraryManager library,
    IMediaSourceManager sources, ISyncPlayManager syncPlay, WebPreferences preferences) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("script.js")]
    public IActionResult Script()
    {
        Response.Headers.CacheControl = "no-store";
        return File(typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.AutoLut.WebPlayer.js")!, "application/javascript; charset=utf-8");
    }

    private SessionInfo? OwnSession() => WebSessionLookup.Find(sessions.Sessions, User);

    private string? Unavailable(SessionInfo session)
    {
        var c = Plugin.Instance!.Configuration;
        if (!c.WebPlayerButton) return "管理员已关闭网页按钮";
        if (!Policy.SelectedActor(c, session.UserId, session.DeviceId)) return "需要管理员启用插件并选择此用户、设备和媒体";
        var user = users.GetUserById(session.UserId);
        if (user is null || !user.HasPermission(PermissionKind.EnableMediaPlayback) || !user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding)) return "当前用户没有视频转码权限";
        if (!session.SupportsRemoteControl) return "播放控制连接尚未就绪，请稍后重试";
        if (syncPlay.IsUserActive(session.UserId)) return "同步播放期间暂不支持切换";
        if (!session.PlayState.CanSeek || !string.IsNullOrEmpty(session.PlayState.LiveStreamId)) return "此播放不支持重新定位";
        var item = library.GetItemById<BaseItem>(session.NowPlayingItem.Id, user);
        if (item is null || !Policy.Selected(c, session.UserId, session.DeviceId, item.Id, library.GetCollectionFolders(item).Select(f => f.Id))) return "此媒体或所在媒体库未被管理员选中";
        var media = sources.GetStaticMediaSources(item, false, user);
        var maximum = c.Allow4kSdr ? "3840×2160（4K UHD）" : "1920×1080";
        if (media.Count != 1 || !Policy.Eligible(media[0], c, out _))
            return $"当前仅支持单视频源、8-bit SDR、最高 {maximum}；请检查色彩标记及隔行兼容选项。HDR、旋转及未支持的色彩组合会跳过";
        return null;
    }

    [Authorize]
    [HttpGet("State")]
    public IActionResult State()
    {
        Response.Headers.CacheControl = "no-store";
        var session = OwnSession();
        if (session is null) return NotFound(new { error = "未找到当前设备正在播放的视频，请重新播放后重试" });
        var reason = Unavailable(session);
        return Ok(new { sessionId = session.Id, itemId = session.NowPlayingItem.Id,
            enabled = preferences.Enabled(session.UserId, session.DeviceId, session.NowPlayingItem.Id),
            canToggle = reason is null, isPaused = session.PlayState.IsPaused, reason });
    }

    [Authorize]
    [HttpPost("Toggle")]
    public async Task<IActionResult> Toggle([FromBody] WebToggleRequest request)
    {
        // Serialize preference + command delivery, and reject stale/repeated UI commands.
        if (!await preferences.Changes.WaitAsync(0, HttpContext.RequestAborted).ConfigureAwait(false)) return Conflict(new { error = "正在切换，请稍后重试" });
        try
        {
            var session = OwnSession();
            if (session is null || session.Id != request.SessionId || session.NowPlayingItem.Id != request.ItemId) return Conflict(new { error = "播放已改变，请重新操作" });
            var reason = Unavailable(session);
            if (reason != null) return Conflict(new { error = reason });
            if (session.PlayState.IsPaused) return Conflict(new { error = "暂停时请在网页按钮选择，继续播放后会应用" });
            var enabled = request.Enabled!.Value;
            var before = preferences.Enabled(session.UserId, session.DeviceId, request.ItemId);
            if (before == enabled) return Ok(new { enabled, restarting = false });
            var queue = session.NowPlayingQueue.Select(q => q.Id).ToArray();
            if (queue.Length == 0) queue = [request.ItemId];
            if (queue.Count(id => id == request.ItemId) != 1 || queue.Length > 200) return Conflict(new { error = "当前播放队列不支持安全重播" });
            if (!preferences.Set(session.UserId, session.DeviceId, request.ItemId, enabled)) return StatusCode(429, new { error = "临时设置已达上限，请联系管理员" });
            try
            {
                await sessions.SendPlayCommand(session.Id, session.Id, new PlayRequest {
                    ItemIds = queue, StartIndex = Array.IndexOf(queue, request.ItemId), PlayCommand = PlayCommand.PlayNow,
                    StartPositionTicks = Math.Clamp(request.PositionTicks ?? session.PlayState.PositionTicks ?? 0, 0, Math.Max(0, (session.NowPlayingItem.RunTimeTicks ?? long.MaxValue) - 1)),
                    AudioStreamIndex = session.PlayState.AudioStreamIndex, SubtitleStreamIndex = session.PlayState.SubtitleStreamIndex,
                    MediaSourceId = session.PlayState.MediaSourceId
                }, HttpContext.RequestAborted).ConfigureAwait(false);
            }
            catch
            {
                preferences.Set(session.UserId, session.DeviceId, request.ItemId, before);
                return StatusCode(503, new { error = "未能发送重播请求，设置已还原" });
            }
            // Delivery is not proof that FFmpeg has applied LUT. The UI says requested, not applied.
            return Ok(new { enabled, restarting = true });
        }
        finally { preferences.Changes.Release(); }
    }
}
