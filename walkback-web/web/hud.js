// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Helmet-HUD mirrors: the hero status chip and the O₂ bar in the verdict panel.
// Read-only: it copies what app.js already wrote into #verdict, #st-min and #st-o2 and computes nothing new,
// so the page never shows a number the route model did not produce.

const $ = (id) => document.getElementById(id);

/** Colour band for an O₂ percentage: ≥ 40 safe, ≥ 20 warn, below that danger. */
export function o2Level(pct) {
  return pct >= 40 ? "safe" : pct >= 20 ? "warn" : "danger";
}

/** What the verdict panel currently says. minutes is the panel's own text ("32 min"), o2 an integer percent. */
export function readStatus() {
  const box = $("verdict");
  const state = box.classList.contains("verdict-ok") ? "ok"
    : box.classList.contains("verdict-bad") ? "bad" : "pending";
  const m = $("st-min").textContent.trim();
  const o = parseInt($("st-o2").textContent, 10);
  return { state, minutes: m && m !== "–" ? m : null, o2: Number.isFinite(o) ? o : null };
}

export function render() {
  const { state, minutes, o2 } = readStatus();
  const chip = $("hud-chip");
  const bar = $("o2-bar");

  // #st-min is the whole walk, not the point of no return, so the "bad" chip carries no number.
  const text = state === "ok"
    ? ["Return path", minutes, o2 !== null && `${o2}% O₂`].filter(Boolean).join(" · ")
    : state === "bad" ? "No return on this route" : "Checking return path…";
  if (chip.textContent !== text) chip.textContent = text;
  chip.dataset.state = state;

  bar.hidden = o2 === null;
  if (o2 !== null) {
    bar.style.setProperty("--pct", Math.max(0, Math.min(100, o2)));
    // A route with a point of no return is never shown as safe, whatever O₂ is left at the end.
    bar.dataset.level = state === "bad" ? "danger" : o2Level(o2);
  }
}

render();
new MutationObserver(render).observe($("verdict"), {
  subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["class"],
});
