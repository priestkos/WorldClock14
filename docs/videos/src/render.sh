#!/usr/bin/env bash
# Regenerate README mock-up walkthrough video + GIF (from repo root).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$ROOT"
npm install
npx playwright install chromium
node docs/videos/src/render.mjs
