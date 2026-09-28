/* Auto LUT web control. Served by the plugin; no external scripts or stored credentials. */
(() => {
    'use strict';
    if (window.__autoLutWebInstalled) return;
    window.__autoLutWebInstalled = true;
    let connectionError = '';
    let button, state, pending = null, connected = false, busy = false, polling = false;
    function client() {
        const api = window.ApiClient;
        if (!api?.getCurrentUserId?.()) return null;
        // A separately selected remote server must not receive this plugin's requests.
        const target = new URL(api.getUrl('AutoLut/Web/State'), location.href);
        const script = document.getElementById('autolut-web-script');
        const local = new URL('./State', script.src);
        return target.href === local.href ? api : null;
    }
    function request(path, body) {
        const api = client();
        if (!api) return Promise.reject(new Error('请重新登录当前服务器'));
        return api.ajax({ url: api.getUrl('AutoLut/Web/' + path), type: body ? 'POST' : 'GET', dataType: 'json',
            ...(body ? { data: JSON.stringify(body), contentType: 'application/json' } : {}) });
    }
    function notify(message) {
        let notice = document.getElementById('autolut-web-notice');
        if (!notice) {
            notice = document.createElement('div'); notice.id = 'autolut-web-notice';
            notice.setAttribute('role', 'status'); notice.setAttribute('aria-live', 'polite');
            Object.assign(notice.style, { position: 'fixed', bottom: '22%', left: '10%', right: '10%',
                padding: '12px', background: '#151515ee', color: 'white', textAlign: 'center', zIndex: '10000', borderRadius: '8px', pointerEvents: 'none' });
            (document.fullscreenElement || document.body).appendChild(notice);
        }
        notice.textContent = message; notice.hidden = false;
        clearTimeout(notice.timer); notice.timer = setTimeout(() => { notice.hidden = true; }, 6000);
    }
    function render() {
        if (!button) return;
        button.disabled = busy || !connected || !state?.canToggle;
        const enabled = pending?.enabled ?? state?.enabled;
        button.textContent = busy ? 'LUT：切换中' : !connected && connectionError ? 'LUT：未连接' : !state ? 'LUT：连接中' : !state.canToggle ? 'LUT：不可用' : (enabled ? 'LUT：开' : 'LUT：关') + (pending ? '·待续播' : '');
        button.setAttribute('aria-pressed', String(!!enabled));
        button.title = (!connected ? (connectionError || '正在同步播放状态，按钮会保留；请稍后重试。') : pending ? '已记住选择，继续播放时应用。再次点击可取消。' : state?.reason) || '仅对此用户、设备和视频生效；切换会短暂缓冲。开启表示允许服务端调色，实际效果取决于处理条件。';
        button.setAttribute('aria-label', button.textContent + '。' + button.title);
        button.style.color = state?.canToggle && enabled ? '#54d4ff' : '#ffffff';
    }
    async function refresh() {
        if (polling || busy || !button?.isConnected || document.hidden) return;
        polling = true;
        try {
            state = await request('State'); connected = true; connectionError = '';
            if (pending && (pending.sessionId !== state.sessionId || pending.itemId !== state.itemId)) pending = null;
        } catch (error) {
            connectionError = error?.responseJSON?.error || (error?.status === 401
                ? '登录已失效，请重新登录当前服务器'
                : error?.status === 404 ? '未找到当前设备正在播放的视频，请重新播放后重试'
                : '播放状态暂时无法连接，正在重试');
            // Old playback-stop reports can temporarily clear NowPlayingItem during restart.
            // Keep the control and its last selection visible while waiting for fresh progress.
            connected = false;
        } finally { button.hidden = false; polling = false; render(); }
        const video = document.querySelector('video');
        if (connected && pending && !video?.paused && state.isPaused === false && state.canToggle) {
            const desired = pending.enabled; pending = null;
            if (desired !== state.enabled) await apply(desired);
        }
    }
    async function toggle(event) {
        event.preventDefault(); event.stopPropagation();
        if (busy || !connected || !state?.canToggle) return;
        const enabled = !(pending?.enabled ?? state.enabled);
        if (document.querySelector('video')?.paused || state.isPaused) {
            pending = enabled === state.enabled ? null : { enabled, itemId: state.itemId, sessionId: state.sessionId };
            render(); notify(pending ? '已记住 LUT 选择，继续播放时应用；当前保持暂停。' : '已取消待续播的 LUT 选择。');
            return;
        }
        pending = null;
        await apply(enabled);
    }
    function activePage() {
        // Jellyfin can cache multiple player pages with the same id after replay.
        return Array.from(document.querySelectorAll('#videoOsdPage')).find(page =>
            !page.hidden && !page.classList.contains('hide') && getComputedStyle(page).display !== 'none');
    }
    async function apply(enabled) {
        busy = true; render();
        try {
            const displayed = activePage()?.querySelector('.osdPositionText')?.textContent?.trim() || '';
            const parts = displayed.split(':');
            const seconds = parts.length >= 2 && parts.length <= 3 && parts.every(p => /^\d+$/.test(p))
                ? parts.reduce((sum, value) => sum * 60 + Number(value), 0) : null;
            const result = await request('Toggle', { sessionId: state.sessionId, itemId: state.itemId, enabled,
                ...(seconds === null ? {} : { positionTicks: seconds * 10000000 }) });
            state.enabled = result.enabled;
            notify(result.restarting ? '已请求' + (result.enabled ? '开启' : '关闭') + ' LUT，正在从当前播放位置重新播放…' : 'LUT 设置未改变');
        } catch (error) {
            notify(error?.responseJSON?.error || '切换未完成，请检查播放连接后重试');
        } finally {
            setTimeout(() => { busy = false; render(); refresh(); }, 2500);
        }
    }
    function mount() {
        const anchor = activePage()?.querySelector('.btnVideoOsdSettings');
        if (!anchor || !document.querySelector('video')) return;
        if (button?.isConnected && button.parentNode === anchor.parentNode) return;
        button?.remove();
        button = document.createElement('button'); button.type = 'button'; button.id = 'autolut-web-toggle';
        button.className = 'autoSize'; button.hidden = false;
        Object.assign(button.style, { border: '0', background: 'transparent', padding: '10px', font: 'inherit', whiteSpace: 'nowrap', cursor: 'pointer' });
        button.addEventListener('click', toggle);
        anchor.before(button); render(); refresh();
    }
    const observer = new MutationObserver(mount);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'hidden'] });
    document.addEventListener('playing', () => { if (pending) setTimeout(refresh, 200); }, true);
    window.addEventListener('hashchange', () => {
        if (!location.hash.startsWith('#/video')) { pending = null; render(); }
    });
    document.addEventListener('ended', () => { pending = null; render(); }, true);
    mount(); setInterval(() => { mount(); return refresh(); }, 2000);
})();
