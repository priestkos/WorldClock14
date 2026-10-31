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
  { html: "01-main-live-now.html", png: "01-main-live-now.png", crop: "#shot" },
  { html: "02-main-custom-time.html", png: "02-main-custom-time.png", crop: "#shot" },
  { html: "03-main-warnings-errors.html", png: "03-main-warnings-errors.png", crop: "#shot" },
  { html: "04-settings.html", png: "04-settings.png", crop: "#shot" },
  { html: "05-commands-scene.html", png: "05-commands-scene.png", crop: "#shot" },
];

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ deviceScaleFactor: 1 });

let total = 0;
for (const page of pages) {
  const p = await context.newPage();
  const htmlPath = path.join(srcDir, page.html);
  await p.goto("file://" + htmlPath, { waitUntil: "load" });
  const el = p.locator(page.crop);
  await el.screenshot({ path: path.join(outDir, page.png) });
  await p.close();
  const size = fs.statSync(path.join(outDir, page.png)).size;
  total += size;
  console.log(`${page.png}\t${(size / 1024).toFixed(1)} KiB`);
}

await browser.close();
console.log(`Total PNG size: ${(total / 1024 / 1024).toFixed(2)} MiB`);
