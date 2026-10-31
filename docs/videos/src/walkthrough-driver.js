/** Shared walkthrough helpers: cursor, clicks, scene fades, caption updates. */

export function easeInOutCubic(t) {
  const x = Math.max(0, Math.min(1, t));
  return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2;
}

export function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function centerOf(el, stage) {
  const sr = stage.getBoundingClientRect();
  const er = el.getBoundingClientRect();
  return {
    x: er.left - sr.left + er.width / 2,
    y: er.top - sr.top + er.height / 2,
  };
}

export function createCursorController(stage) {
  const layer = document.getElementById("cursor-layer");
  const pointer = document.getElementById("walkthrough-cursor");
  const ripples = document.getElementById("click-ripples");

  let x = 640;
  let y = 420;
  let moveAnim = null;

  function applyPosition() {
    pointer.style.transform = `translate(${x}px, ${y}px)`;
  }
  applyPosition();

  function tickCursor(now) {
    if (!moveAnim) return false;
    const elapsed = now - moveAnim.start;
    const p = Math.min(1, elapsed / moveAnim.duration);
    const e = easeInOutCubic(p);
    x = moveAnim.fromX + (moveAnim.toX - moveAnim.fromX) * e;
    y = moveAnim.fromY + (moveAnim.toY - moveAnim.fromY) * e;
    applyPosition();
    if (p >= 1) {
      moveAnim.resolve?.();
      moveAnim = null;
    }
    return moveAnim !== null;
  }

  function moveTo(nx, ny, duration = 680) {
    return new Promise((resolve) => {
      moveAnim = {
        fromX: x,
        fromY: y,
        toX: nx,
        toY: ny,
        start: performance.now(),
        duration,
        resolve,
      };
    });
  }

  function moveToEl(el, duration = 680) {
    const c = centerOf(el, stage);
    return moveTo(c.x, c.y, duration);
  }

  function ripple(atX, atY) {
    const ring = document.createElement("span");
    ring.className = "click-ripple";
    ring.style.left = `${atX}px`;
    ring.style.top = `${atY}px`;
    ripples.appendChild(ring);
    ring.addEventListener("animationend", () => ring.remove());
  }

  async function pressClick(el, onActivate) {
    pointer.classList.add("clicking");
    ripple(x, y);
    el?.classList.add("walkthrough-press");
    await sleep(90);
    pointer.classList.remove("clicking");
    el?.classList.remove("walkthrough-press");
    if (onActivate) onActivate();
    else el?.click?.();
    await sleep(140);
  }

  async function clickEl(el, onActivate) {
    await moveToEl(el);
    await pressClick(el, onActivate);
  }

  return {
    moveTo,
    moveToEl,
    clickEl,
    pressClick,
    tickCursor,
    getPosition: () => ({ x, y }),
  };
}

export function createSceneController() {
  const panels = [...document.querySelectorAll(".scene-panel")];
  let currentId = null;

  function showScene(id, { duration = 480 } = {}) {
    const next = document.getElementById(id);
    if (!next || currentId === id) return Promise.resolve();

    const prev = currentId ? document.getElementById(currentId) : null;
    currentId = id;

    next.classList.add("active");
    next.classList.remove("pre-enter");
    void next.offsetWidth;
    next.classList.remove("is-entering");

    if (prev && prev !== next) {
      prev.classList.add("is-leaving");
      prev.classList.remove("active");
      setTimeout(() => prev.classList.remove("is-leaving"), duration + 40);
    }

    return sleep(duration);
  }

  function init(firstId) {
    for (const p of panels) {
      p.classList.remove("active", "is-leaving", "is-entering");
      p.classList.add("pre-enter");
    }
    const first = document.getElementById(firstId);
    first.classList.remove("pre-enter");
    first.classList.add("active");
    currentId = firstId;
  }

  return { showScene, init };
}

export function setCaption(main, sub = "") {
  const root = document.getElementById("caption");
  root.classList.add("caption-updating");
  document.getElementById("caption-main").textContent = main;
  document.getElementById("caption-sub").textContent = sub;
  requestAnimationFrame(() => {
    root.classList.remove("caption-updating");
  });
}

export function runTimeline({ endMs, onFrame, onDone, schedule }) {
  const queue = [];
  const at = (ms, fn) => queue.push({ ms, fn });
  schedule(at);
  queue.sort((a, b) => a.ms - b.ms);
  let qi = 0;
  let startedAt = null;
  let done = false;

  function frame(now) {
    if (startedAt === null) startedAt = now;
    const t = now - startedAt;
    onFrame?.(now);

    while (qi < queue.length && queue[qi].ms <= t) {
      queue[qi].fn();
      qi++;
    }

    if (!done && t >= endMs && qi >= queue.length) {
      done = true;
      onDone?.();
      return;
    }
    requestAnimationFrame(frame);
  }

  requestAnimationFrame(frame);
}
