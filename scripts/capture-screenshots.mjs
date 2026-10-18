#!/usr/bin/env node
// Renders screenshots/*.html into PNGs via headless Chrome.
//   node scripts/capture-screenshots.mjs
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, rmSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dir = join(root, 'screenshots');
mkdirSync(dir, { recursive: true });

const chrome =
  process.env.CHROME_PATH ||
  ['google-chrome', 'google-chrome-stable', 'chromium', 'chromium-browser']
    .map((c) => spawnSync('which', [c], { encoding: 'utf8' }).stdout.trim())
    .find(Boolean);

if (!chrome) {
  console.error('No Chrome/Chromium found. Set CHROME_PATH.');
  process.exit(1);
}

const scenes = readdirSync(dir)
  .filter((f) => f.endsWith('.html') && !f.startsWith('_'))
  .sort();

const profile = mkdtempSync(join(tmpdir(), 'wc14-chrome-'));

try {
  for (const html of scenes) {
    const name = html.replace(/\.html$/, '');
    const out = join(dir, `${name}.png`);
    const url = pathToFileURL(join(dir, html)).href;
    const args = [
      '--headless=new',
      '--disable-gpu',
      '--no-sandbox',
      '--disable-dev-shm-usage',
      '--hide-scrollbars',
      '--force-device-scale-factor=2',
      `--user-data-dir=${profile}`,
      `--window-size=${name === 'settings' ? 640 : 480},${name === 'settings' ? 900 : 560}`,
      `--screenshot=${out}`,
      url,
    ];
    const r = spawnSync(chrome, args, { encoding: 'utf8', timeout: 45_000 });
    if (r.error || r.status !== 0) {
      console.error(r.stderr || r.stdout || r.error || `chrome failed for ${html}`);
      process.exit(r.status || 1);
    }
    cropPng(out);
    const kb = (statSync(out).size / 1024).toFixed(1);
    console.log(`wrote ${name}.png (${kb} KiB)`);
  }
} finally {
  rmSync(profile, { recursive: true, force: true });
}

/** Trim the near-black page chrome so README shots sit tight around the window. */
function cropPng(path) {
  const r = spawnSync('python3', ['-c', `
from PIL import Image
im = Image.open(${JSON.stringify(path)}).convert('RGB')
bg = (8, 10, 15)
px = im.load(); w, h = im.size
minx, miny, maxx, maxy = w, h, 0, 0
for y in range(h):
  for x in range(w):
    r, g, b = px[x, y]
    if abs(r-bg[0]) >= 8 or abs(g-bg[1]) >= 8 or abs(b-bg[2]) >= 8:
      minx = min(minx, x); miny = min(miny, y); maxx = max(maxx, x); maxy = max(maxy, y)
pad = 24
im.crop((max(0,minx-pad), max(0,miny-pad), min(w,maxx+1+pad), min(h,maxy+1+pad))).save(${JSON.stringify(path)}, optimize=True)
`], { encoding: 'utf8' });
  if (r.status !== 0) {
    console.error(r.stderr || 'crop failed');
    process.exit(r.status || 1);
  }
}
