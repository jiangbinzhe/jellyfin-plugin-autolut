// Real Jellyfin HTTP + WebSocket contract test. Only a disposable loopback server.
import { readFile, writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import assert from 'node:assert/strict';
const { values } = parseArgs({ options: {
    url: { type: 'string', default: 'http://127.0.0.1:18096' }, credentials: { type: 'string' },
    item: { type: 'string' }, report: { type: 'string' }, fixture: { type: 'string' }
} });
const base = new URL(values.url);
assert(base.protocol === 'http:' && ['127.0.0.1', '[::1]'].includes(base.hostname) && !base.username && !base.password);
assert(values.credentials && values.item && values.report && values.fixture);
const creds = JSON.parse(await readFile(values.credentials, 'utf8'));
const device = 'autolut-web-contract';
let authorization = `MediaBrowser Client="Jellyfin Web", Device="Synthetic", DeviceId="${device}", Version="12.1.0"`;
async function call(path, body, expected = 200, auth = authorization) {
    const response = await fetch(new URL(path, base), { method: body === undefined ? 'GET' : 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: auth }, body: body === undefined ? undefined : JSON.stringify(body) });
    assert.equal(response.status, expected, 'Unexpected HTTP status for ' + path.split('?')[0]);
    const text = await response.text(); return text ? JSON.parse(text) : null;
}
await call('/AutoLut/Web/State', undefined, 401, '');
const auth = await call('/Users/AuthenticateByName', { Username: creds.username, Pw: creds.password || '' });
authorization += `, Token="${auth.AccessToken}"`;
const checks = ['anonymous-state-rejected'];
const socketUrl = new URL('/socket', base); socketUrl.protocol = 'ws:';
socketUrl.searchParams.set('ApiKey', auth.AccessToken); socketUrl.searchParams.set('deviceId', device);
const socket = new WebSocket(socketUrl);
const commands = [];
socket.addEventListener('message', event => { const message = JSON.parse(event.data); if (message.MessageType === 'Play') commands.push(message.Data); });
await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('WebSocket connection timeout')), 5000);
    socket.addEventListener('open', () => { clearTimeout(timeout); resolve(); }, { once: true });
    socket.addEventListener('error', () => { clearTimeout(timeout); reject(new Error('WebSocket connection failed')); }, { once: true });
});
let active;
const item = values.item;
const fixture = JSON.parse(await readFile(values.fixture, 'utf8'));
async function playback() {
    return call(`/Items/${item}/PlaybackInfo`, { UserId: auth.User.Id, DeviceProfile: fixture, SubtitleStreamIndex: -1,
        StartTimeTicks: 20000000, MaxStreamingBitrate: 3000000 });
}
async function started(response) {
    active = response.PlaySessionId;
    await call('/Sessions/Playing', { ItemId: item, MediaSourceId: response.MediaSources[0].Id, PlaySessionId: active,
        PositionTicks: 20000000, CanSeek: true, IsPaused: false, AudioStreamIndex: 1, SubtitleStreamIndex: -1,
        PlayMethod: response.MediaSources[0].SupportsDirectPlay ? 'DirectPlay' : 'Transcode',
        NowPlayingQueue: [{ Id: item, PlaylistItemId: 'synthetic-queue-item' }] }, 204);
}
async function stopped() {
    if (active) await call('/Sessions/Playing/Stopped', { ItemId: item, PlaySessionId: active, PositionTicks: 20000000 }, 204);
    active = undefined;
}
async function command() {
    for (let n = 0; n < 30; n++) { if (commands.length) return commands.shift(); await new Promise(r => setTimeout(r, 100)); }
    throw new Error('Playback command not delivered to the owning WebSocket');
}
try {
    await call('/Sessions/Capabilities/Full', { PlayableMediaTypes: ['Video'], SupportsMediaControl: true }, 204);
    await call('/AutoLut/Web/State', undefined, 404);
    const initial = await playback(); assert.equal(initial.MediaSources[0].SupportsDirectPlay, false); await started(initial);
    let state = await call('/AutoLut/Web/State'); assert.equal(state.canToggle, true); assert.equal(state.enabled, true);
    checks.push('selected-library-enables-web-playback');
    await call('/AutoLut/Web/Toggle', { sessionId: 'not-owned', itemId: item, enabled: false }, 409);
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: '00000000000000000000000000000001', enabled: false }, 409);
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item }, 400);
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item, enabled: false, positionTicks: -1 }, 400);
    checks.push('foreign-session-stale-item-and-invalid-input-rejected');
    const other = authorization.replace(`DeviceId="${device}"`, 'DeviceId="autolut-other-device"');
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item, enabled: false }, 409, other);
    checks.push('other-device-cannot-toggle-session');
    const off = await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item, enabled: false, positionTicks: 40000000 });
    assert.equal(off.restarting, true);
    const cmd = await command();
    assert.equal(cmd.ItemIds[0].replaceAll('-', ''), item.replaceAll('-', ''));
    assert.equal(cmd.StartPositionTicks, 40000000); assert.equal(cmd.AudioStreamIndex, 1); assert.equal(cmd.SubtitleStreamIndex, -1);
    assert.equal(cmd.StartIndex, 0); assert.equal(cmd.MediaSourceId, initial.MediaSources[0].Id);
    checks.push('toggle-delivers-owned-replay-preserving-position-tracks-source');
    await stopped();
    const normal = await playback(); assert.equal(normal.MediaSources[0].SupportsDirectPlay, true); await started(normal);
    state = await call('/AutoLut/Web/State'); assert.equal(state.enabled, false);
    checks.push('off-bypasses-lut-and-restores-direct-play');
    await call('/Sessions/Playing/Progress', { ItemId: item, PlaySessionId: active, PositionTicks: 40000000, CanSeek: true, IsPaused: true }, 204);
    assert.equal((await call('/AutoLut/Web/State')).canToggle, true);
    assert.equal((await call('/AutoLut/Web/State')).isPaused, true);
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item, enabled: true }, 409);
    await call('/Sessions/Playing/Progress', { ItemId: item, PlaySessionId: active, PositionTicks: 40000000, CanSeek: true, IsPaused: false }, 204);
    checks.push('paused-playback-is-not-unexpectedly-resumed');
    await call('/AutoLut/Web/Toggle', { sessionId: state.sessionId, itemId: item, enabled: true, positionTicks: 40000000 });
    await command(); await stopped();
    const graded = await playback(); assert.equal(graded.MediaSources[0].SupportsDirectPlay, false); await started(graded);
    assert.equal((await call('/AutoLut/Web/State')).enabled, true);
    checks.push('on-restores-lut-preparation');
    const report = { plugin: '0.1.3.0', checks, success: true, nas_qsv_pixels: 'not-tested' };
    await writeFile(values.report, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} finally { await stopped(); socket.close(); }
