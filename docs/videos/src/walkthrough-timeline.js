import {
  createCursorController,
  createSceneController,
  runTimeline,
  setCaption,
  sleep,
} from "./walkthrough-driver.js";

const TIMES = {
  ist: 20 * 60,
  jst: 23 * 60 + 30,
  utc: 14 * 60 + 30,
  et: 10 * 60 + 30,
};

function fmtMins(m) {
  m = ((m % (24 * 60)) + 24 * 60) % (24 * 60);
  const h = Math.floor(m / 60);
  const min = m % 60;
  return String(h).padStart(2, "0") + ":" + String(min).padStart(2, "0");
}

function bumpLiveClock() {
  TIMES.ist += 1;
  TIMES.jst += 1;
  TIMES.utc += 1;
  TIMES.et += 1;
  for (const [key, el] of Object.entries({
    ist: document.querySelector('[data-zone="ist"]'),
    jst: document.querySelector('[data-zone="jst"]'),
    utc: document.querySelector('[data-zone="utc"]'),
    et: document.querySelector('[data-zone="et"]'),
  })) {
    if (el) el.textContent = fmtMins(TIMES[key]);
  }
  const flash = document.getElementById("live-flash");
  if (flash && flash.classList.contains("visible")) {
    flash.textContent =
      "Copied: " +
      fmtMins(TIMES.ist) +
      " IST | " +
      fmtMins(TIMES.jst) +
      " JST | " +
      fmtMins(TIMES.utc) +
      " UTC | " +
      fmtMins(TIMES.et) +
      " ET";
  }
  const echo = document.getElementById("live-echo");
  if (echo) echo.textContent = "Wed 07 Oct 2026 " + fmtMins(TIMES.ist);
}

function setMode(liveWin, mode) {
  liveWin.querySelectorAll(".mode").forEach((m) => {
    m.classList.toggle("on", m.dataset.mode === mode);
  });
}

const stage = document.getElementById("stage");
const scenes = createSceneController();
const cursor = createCursorController(stage);

scenes.init("scene-disclaimer");

let tickInterval = null;
function startTick() {
  if (tickInterval) clearInterval(tickInterval);
  tickInterval = setInterval(bumpLiveClock, 800);
}
function stopTick() {
  if (tickInterval) clearInterval(tickInterval);
  tickInterval = null;
}

/** Wall-clock length of the recording (ms). */
export const WALKTHROUGH_END_MS = 42_000;

runTimeline({
  endMs: WALKTHROUGH_END_MS,
  onFrame: (now) => {
    cursor.tickCursor(now);
  },
  onDone: () => {
    stopTick();
    window.__WALKTHROUGH_COMPLETE__ = true;
  },
  schedule: (at) => {
    at(0, () => {
      setCaption("UI mock-up — not an in-game capture", "WorldClock14 / TimeShift");
    });

    at(2800, () => {
      void scenes.showScene("scene-1").then(() => {
        setCaption("Live now", "Default mode — base zone, per-zone Copy, Copy for chat");
        startTick();
      });
    });

    at(5600, () => {
      void (async () => {
        const btn = document.getElementById("live-copy-btn");
        await cursor.clickEl(btn, () => {
          document.getElementById("live-flash").classList.remove("hidden");
          document.getElementById("live-flash").classList.add("visible");
        });
      })();
    });

    at(8200, () => {
      void (async () => {
        const win = document.getElementById("live-win");
        const custom = win.querySelector('[data-mode="custom"]');
        await cursor.clickEl(custom, () => {
          setMode(win, "custom");
          setCaption("Switching to Custom time", "Same window — typed time in the base zone");
        });
      })();
    });

    at(10_600, () => {
      stopTick();
      setCaption("Custom time", "Parsing, +1d day marker, DST note, copied line");
      void scenes.showScene("scene-2").then(() => {
        const field = document.getElementById("custom-field");
        field.textContent = "Wed 2";
        setTimeout(() => {
          field.textContent = "Wed 21";
        }, 220);
        setTimeout(() => {
          field.textContent = "Wed 21:00";
        }, 440);
      });
    });

    at(15_800, () => {
      void scenes.showScene("scene-3").then(() => {
        setCaption("Resilience", 'Bad zone id → error row; other zones still convert');
      });
    });

    at(20_400, () => {
      void (async () => {
        const settingsBtn = document.querySelector("#scene-3 .ghost");
        if (settingsBtn) {
          await cursor.clickEl(settingsBtn);
        }
        await scenes.showScene("scene-4");
        const win = document.getElementById("settings-win");
        win.style.transform = "translateY(0)";
        setCaption("TimeShift settings (/tzconfig)", "Zones, reorder, copied-line format, preview");
      })();
    });

    at(23_600, () => {
      void (async () => {
        const jstRow = document.querySelector('#settings-zones tr[data-row="jst"]');
        const cb = jstRow.querySelector(".cb");
        await cursor.clickEl(cb, () => {
          jstRow.classList.remove("row-disabled");
          cb.classList.remove("off");
          cb.textContent = "✓";
        });
      })();
    });

    at(26_200, () => {
      void (async () => {
        const win = document.getElementById("settings-win");
        const preview = document.getElementById("settings-preview");
        const { x, y } = cursor.getPosition();
        await cursor.moveTo(x, y - 90, 900);
        win.style.transform = "translateY(-140px)";
        await sleep(500);
        preview.textContent = "20:00 IST | 23:30 JST | 14:30 UTC | 10:30 ET";
        preview.classList.add("preview-highlight");
        setTimeout(() => preview.classList.remove("preview-highlight"), 700);
      })();
    });

    at(31_200, () => {
      void scenes.showScene("scene-5").then(() => {
        setCaption("/tz and /tzconfig", "Commands, help text, parse/DST log lines (mock /xllog)");
      });
    });

    at(33_400, () => {
      void (async () => {
        const cmd = document.querySelector("#scene-5 .cmd-list li:first-child code");
        if (cmd) await cursor.moveToEl(cmd.parentElement, 820);
      })();
    });
  },
});
