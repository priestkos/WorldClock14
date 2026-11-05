#!/usr/bin/env node
/**
 * Render docs/screenshots/src/*.html to PNG.
 * Requires: npm install (playwright) in repo root, or `npx playwright install chromium` once.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const srcDir = path.join(__dirname, "src");
const outDir = __dirname;

const pages = [
  { html: "01-main-live-now.html", png: "01-main-live-now.png" },
  { html: "02-main-custom-time.html", png: "02-main-custom-time.png" },
  { html: "03-main-warnings-errors.html", png: "03-main-warnings-errors.png" },
  { html: "04-settings.html", png: "04-settings.png" },
  { html: "05-commands-scene.html", png: "05-commands-scene.png" },
];

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });

let total = 0;
for (const entry of pages) {
  const htmlPath = path.join(srcDir, entry.html);
  await page.goto("file://" + htmlPath, { waitUntil: "load" });
  await page.waitForTimeout(80);
  await page.screenshot({
    path: path.join(outDir, entry.png),
    type: "png",
  });
  const size = fs.statSync(path.join(outDir, entry.png)).size;
  total += size;
  console.log(`${entry.png}\t${(size / 1024).toFixed(1)} KiB`);
}

await browser.close();
console.log(`Total PNG size: ${(total / 1024 / 1024).toFixed(2)} MiB`);
