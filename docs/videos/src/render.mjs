#!/usr/bin/env node
/**
 * Record docs/videos/src/walkthrough.html → mockup-walkthrough.mp4 + .gif
 * Requires: npm install && npx playwright install chromium (from repo root)
 */
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { chromium } from "playwright";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const docsDir = path.join(__dirname, "../..");
const srcDir = __dirname;
const videosDir = path.join(__dirname, "..");
const mp4Out = path.join(videosDir, "mockup-walkthrough.mp4");
const gifOut = path.join(videosDir, "mockup-walkthrough.gif");
const tmpDir = path.join(__dirname, ".render-tmp");

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
};

function startStaticServer(root) {
  const server = http.createServer((req, res) => {
    const urlPath = decodeURIComponent((req.url || "/").split("?")[0]);
    const rel = urlPath === "/" ? "/walkthrough.html" : urlPath;
    const file = path.normalize(path.join(root, rel.replace(/^\//, "")));
    if (!file.startsWith(root)) {
      res.writeHead(403);
      res.end();
      return;
    }
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      res.writeHead(404);
      res.end();
      return;
    }
    const ext = path.extname(file);
    res.writeHead(200, { "Content-Type": MIME[ext] || "application/octet-stream" });
    fs.createReadStream(file).pipe(res);
  });
  return new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const { port } = server.address();
      resolve({ server, port });
    });
  });
}

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

const { server, port } = await startStaticServer(docsDir);
const pageUrl = `http://127.0.0.1:${port}/videos/src/walkthrough.html`;

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1280, height: 720 },
  recordVideo: {
    dir: tmpDir,
    size: { width: 1280, height: 720 },
  },
});

const page = await context.newPage();
await page.goto(pageUrl, { waitUntil: "load" });
// Fixed wall-clock wait: timeline ends at 42s; recording can throttle timers slightly.
await page.waitForTimeout(45_000);
if (!(await page.evaluate(() => window.__WALKTHROUGH_COMPLETE__ === true))) {
  throw new Error("Walkthrough did not finish (missing __WALKTHROUGH_COMPLETE__)");
}
// Let the last frame settle
await page.waitForTimeout(400);

const video = page.video();
await page.close();
await context.close();
await browser.close();
server.close();

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
  "fps=5,scale=640:-1:flags=lanczos,palettegen=stats_mode=diff:max_colors=128",
  palette,
]);

run("ffmpeg", [
  "-y",
  "-i",
  mp4Out,
  "-i",
  palette,
  "-lavfi",
  "fps=5,scale=640:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
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
