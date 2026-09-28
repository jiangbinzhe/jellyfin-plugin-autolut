import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../src/WebPlayer.js', import.meta.url), 'utf8');
const settle = async () => { for (let n = 0; n < 5; n++) await new Promise(setImmediate); };
function harness() {
    const h = { state: { sessionId: 'session-a', itemId: 'item-a', enabled: true, canToggle: true, isPaused: false }, posts: [], timers: [], intervals: [], fail: false };
    const events = {};
    const element = () => ({ style: {}, hidden: false, isConnected: true, attrs: {},
        setAttribute(k, v) { this.attrs[k] = v; }, addEventListener(k, v) { this[k] = v; }, appendChild() {}, remove() { this.isConnected = false; } });
    const video = { paused: false }; h.video = video;
    const parent = {}; const anchor = { parentNode: parent, before(b) { b.parentNode = parent; h.button = b; } };
    const script = { src: 'http://localhost:18096/jellyfin/AutoLut/Web/script.js?v=0.1.3' };
    const api = { getCurrentUserId: () => 'test-user', getUrl: path => 'http://localhost:18096/jellyfin/' + path,
        ajax: async req => {
            if (req.type === 'POST') { const body = JSON.parse(req.data); h.posts.push(body); h.state.enabled = body.enabled; return { enabled: body.enabled, restarting: true }; }
            if (h.fail) throw new Error('Transient playback transition');
            return structuredClone(h.state);
        } };
    const document = { hidden: false, body: element(), fullscreenElement: null,
        getElementById: id => id === 'autolut-web-script' ? script : null,
        querySelector: selector => selector.includes('btnVideoOsdSettings') ? anchor : selector === 'video' ? video : selector.includes('osdPositionText') ? { textContent: '1:23' } : null,
        createElement: element, addEventListener: (name, fn) => { events[name] = fn; } };
    const location = { href: 'http://localhost:18096/jellyfin/web/index.html', hash: '#/video' };
    h.leave = () => { location.hash = '#/home'; events.hashchange(); };
    const context = vm.createContext({ window: { ApiClient: api, addEventListener: (name, fn) => { events[name] = fn; } }, document, location,
        URL, MutationObserver: class { observe() {} }, setInterval: fn => { h.intervals.push(fn); },
        setTimeout: fn => { h.timers.push(fn); return h.timers.length; }, clearTimeout() {} });
    vm.runInContext(source, context);
    h.refresh = async () => { await h.intervals[0](); await settle(); };
    h.click = async () => { await h.button.click({ preventDefault() {}, stopPropagation() {} }); await settle(); };
    h.finishTimers = async () => { const timers = h.timers.splice(0); for (const fn of timers) fn(); await settle(); };
    h.resume = async () => { video.paused = false; h.state.isPaused = false; events.playing(); await h.finishTimers(); };
    return h;
}
test('button survives a temporary missing playback state and reconnects', async () => {
    const h = harness(); await settle(); assert.equal(h.button.hidden, false);
    h.fail = true; await h.refresh(); assert.equal(h.button.hidden, false); assert.equal(h.button.disabled, true);
    h.fail = false; h.state.enabled = false; await h.refresh();
    assert.equal(h.button.hidden, false); assert.equal(h.button.disabled, false); assert.equal(h.button.textContent, 'LUT：关'); assert.equal(h.button.style.color, '#ffffff');
});
test('paused choice never sends replay until playback resumes', async () => {
    const h = harness(); h.video.paused = true; h.state.isPaused = true; await settle(); await h.refresh();
    await h.click(); assert.equal(h.posts.length, 0); assert.match(h.button.textContent, /关.*待续播/); assert.equal(h.video.paused, true);
    await h.refresh(); assert.equal(h.posts.length, 0); assert.match(h.button.textContent, /待续播/);
    await h.resume(); assert.equal(h.posts.length, 1); assert.equal(h.posts[0].enabled, false); assert.equal(h.posts[0].positionTicks, 830000000);
    await h.finishTimers(); await h.refresh(); assert.equal(h.posts.length, 1); assert.equal(h.button.textContent, 'LUT：关');
});
test('pending choice can be cancelled and cannot cross a playback-session change', async () => {
    const h = harness(); h.video.paused = true; h.state.isPaused = true; await settle(); await h.refresh();
    await h.click(); await h.click(); assert.equal(h.posts.length, 0); assert.equal(h.button.textContent, 'LUT：开');
    await h.click(); h.state.sessionId = 'session-b'; await h.refresh(); await h.resume();
    assert.equal(h.posts.length, 0); assert.equal(h.button.textContent, 'LUT：开');
});
test('disallowed state cannot send a toggle and configured BaseUrl remains in API URLs', async () => {
    const h = harness(); h.state.canToggle = false; await settle(); await h.refresh(); await h.click();
    assert.equal(h.posts.length, 0); assert.equal(h.button.disabled, true); assert.equal(h.button.textContent, 'LUT：不可用');
});

test('leaving the player cancels a pending paused choice', async () => {
    const h = harness(); h.video.paused = true; h.state.isPaused = true; await settle(); await h.refresh();
    await h.click(); h.leave(); await h.resume();
    assert.equal(h.posts.length, 0); assert.equal(h.button.textContent, 'LUT：开');
});
