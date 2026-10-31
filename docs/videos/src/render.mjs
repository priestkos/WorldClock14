#!/usr/bin/env node
/**
 * Record docs/videos/src/walkthrough.html → mockup-walkthrough.mp4 + .gif
 * Requires: npm install && npx playwright install chromium (from repo root)
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { chromium } from "playwright";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const videosDir = path.join(__dirname, "..");
const htmlPath = path.join(__dirname, "walkthrough.html");
const mp4Out = path.join(videosDir, "mockup-walkthrough.mp4");
const gifOut = path.join(videosDir, "mockup-walkthrough.gif");
const tmpDir = path.join(__dirname, ".render-tmp");

function run(cmd, args, opts = {}) {
  const r = spawnSync(cmd, args, { stdio: "inherit", ...opts });
  if (r.status !== 0) {
    throw new Error(`${cmd} exited ${r.status}`);
  }
}

function rmrf(p) {
  fs.rmSync(p, { recursive: true, force: true });
}

rmrf(tmpDir);
fs.mkdirSync(tmpDir, { recursive: true });

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1280, height: 720 },
  recordVideo: {
    dir: tmpDir,
    size: { width: 1280, height: 720 },
  },
});

const page = await context.newPage();
await page.goto("file://" + htmlPath, { waitUntil: "load" });
// Fixed wall-clock wait: timeline is 38s; recording can throttle timers slightly.
await page.waitForTimeout(41_000);
if (!(await page.evaluate(() => window.__WALKTHROUGH_COMPLETE__ === true))) {
  throw new Error("Walkthrough did not finish (missing __WALKTHROUGH_COMPLETE__)");
}
// Let the last frame settle
await page.waitForTimeout(400);

const video = page.video();
await page.close();
await context.close();
await browser.close();

const webmPath = await video.path();
const rawMp4 = path.join(tmpDir, "raw.mp4");

run("ffmpeg", [
  "-y",
  "-i",
  webmPath,
  "-c:v",
  "libx264",
  "-pix_fmt",
  "yuv420p",
  "-crf",
  "28",
  "-preset",
  "medium",
  "-movflags",
  "+faststart",
  rawMp4,
]);

run("ffmpeg", [
  "-y",
  "-i",
  rawMp4,
  "-c:v",
  "libx264",
  "-pix_fmt",
  "yuv420p",
  "-crf",
  "28",
  "-preset",
  "medium",
  "-movflags",
  "+faststart",
  mp4Out,
]);

const palette = path.join(tmpDir, "palette.png");
run("ffmpeg", [
  "-y",
  "-i",
  mp4Out,
  "-vf",
  "fps=6,scale=800:-1:flags=lanczos,palettegen=stats_mode=diff",
  palette,
]);

run("ffmpeg", [
  "-y",
  "-i",
  mp4Out,
  "-i",
  palette,
  "-lavfi",
  "fps=6,scale=800:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3",
  "-loop",
  "0",
  gifOut,
]);

const mp4Size = fs.statSync(mp4Out).size;
const gifSize = fs.statSync(gifOut).size;
console.log(`\n${path.relative(process.cwd(), mp4Out)}\t${(mp4Size / 1024 / 1024).toFixed(2)} MiB`);
console.log(`${path.relative(process.cwd(), gifOut)}\t${(gifSize / 1024 / 1024).toFixed(2)} MiB`);

rmrf(tmpDir);

if (mp4Size > 10 * 1024 * 1024) {
  console.warn("Warning: MP4 exceeds 10 MiB target");
}
if (gifSize > 5 * 1024 * 1024) {
  console.warn("Warning: GIF exceeds 5 MiB target");
}
