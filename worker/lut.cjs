#!/usr/bin/env node
'use strict';
// Offline SDR PoC. Face detector is deliberately an external input.
const fs = require('node:fs');
const crypto = require('node:crypto');
const path = require('node:path');
const core = require('./core.cjs');
console.log = (...args) => console.error(...args);
const keys = ['temp', 'tint', 'sat', 'bright', 'contrast', 'highlight', 'shadow'];

function bake(params, size = 33) {
  if (!Number.isInteger(size) || size < 2 || size > 65) throw Error('LUT size must be 2..65');
  for (const key of keys) if (!Number.isFinite(params[key]) || Math.abs(params[key]) > 200)
    throw Error(`Invalid parameter: ${key}`);
  const data = core.generateLutData(size, params);
  if (data.some(v => !Number.isFinite(v) || v < 0 || v > 1)) throw Error('Invalid LUT output');
  return data;
}

function cube(data, size) {
  const rows = ['TITLE "User v6.7.5 SDR LUT"', `LUT_3D_SIZE ${size}`, 'DOMAIN_MIN 0 0 0', 'DOMAIN_MAX 1 1 1'];
  for (let i = 0; i < data.length; i += 3) rows.push(`${data[i]} ${data[i+1]} ${data[i+2]}`);
  return rows.join('\n') + '\n';
}

function analyze(rgba, width, height, faceBox) {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
      Math.max(width, height) !== 640 || rgba.length !== width * height * 4)
    throw Error('Supply one RGBA8 frame with longest side exactly 640 (original threshold calibration).');
  if (faceBox) {
    const { x1, y1, x2, y2 } = faceBox;
    if (![x1,y1,x2,y2].every(Number.isInteger) || x1 < 0 || y1 < 0 || x2 > width || y2 > height || x1 >= x2 || y1 >= y2)
      throw Error('Invalid faceBox coordinates in the resized frame');
  }
  const frame = core.sampleRgba(rgba, width, height, faceBox);
  if (!frame.pixels) return { status: 'keep_previous', reason: 'insufficient_skin_pixels' };
  const info = { ...core.sampleSkinStats(frame.pixels), full_hl_ratio: frame.full_hl_ratio_global,
    high_rb: frame.high_rb, high_pixels: frame.high_pixels, regionName: frame.regionName };
  const useExtreme = core.isExtremeDark(info);
  const hp = info.high_pixels, hrb = info.high_rb;
  const face1 = info.regionName === '人脸框' && hrb > 1.3 && hp > 200;
  const face2 = info.regionName === '人脸框' && hp > 5000 && hrb > 0.9;
  const center = info.regionName === '中央50%' && hp > 5000 && hp < 13000 && hrb > 0.9;
  const useHighMatch = !useExtreme && (info.regionName === '中央50%' ?
    center && core.isHighMatch(info) : face1 || face2 || core.isHighMatch(info));
  const params = core.predictParams(info, core.subsample(frame.pixels, core.CONFIG.maxSkinPixels), useExtreme, useHighMatch);
  return { status: 'ok', info, params, template: useExtreme ? 'extreme' : useHighMatch ? 'high_match' : 'standard' };
}

function main() {
  const [mode, input, output] = process.argv.slice(2);
  if (!['params', 'analyze'].includes(mode) || !input || !output)
    throw Error('Usage: node lut.cjs params params.json output.cube | analyze frame.json output.cube');
  // Refuse existing targets. Each analysis generation should have its own immutable name.
  if (fs.existsSync(output) || fs.existsSync(output + '.json')) throw Error('Output exists; choose a new generation filename.');
  const request = JSON.parse(fs.readFileSync(input, 'utf8'));
  const result = mode === 'params' ? { status: 'ok', params: request.params || request } :
    analyze(fs.readFileSync(path.resolve(path.dirname(input), request.rgba)), request.width, request.height, request.faceBox);
  if (result.status !== 'ok') { process.stdout.write(JSON.stringify(result) + '\n'); return; }
  const data = cube(bake(result.params), 33);
  result.sha256 = crypto.createHash('sha256').update(data).digest('hex');
  result.colorDomain = 'SDR RGB; browser canvas matching requires separate color-management validation';
  fs.writeFileSync(output, data, { flag: 'wx' });
  fs.writeFileSync(output + '.json', JSON.stringify(result, null, 2), { flag: 'wx' });
  process.stdout.write(JSON.stringify({ status: 'ok', output, sha256: result.sha256, params: result.params }) + '\n');
}
module.exports = { bake, cube, analyze };
if (require.main === module) { try { main(); } catch (e) { console.error(e.message); process.exitCode = 1; } }
