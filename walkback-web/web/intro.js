// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Opening animation: the helmet visor boots, then opens on the page. CSS runs it and hides
// it by itself (so the page never stays covered); this file only skips it: on a second visit
// in the same tab session, when the system asks for less motion, or on a click or a key.
// Loaded in <head> without defer, so the choice is made before the first paint.
(() => {
  const root = document.documentElement;
  let seen = false;
  try { seen = sessionStorage.getItem("walkback-intro") === "seen"; } catch (e) { /* private mode */ }
  const still = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (seen || still) {
    root.classList.add("no-intro");
    return;
  }
  try { sessionStorage.setItem("walkback-intro", "seen"); } catch (e) { /* private mode */ }

  const events = ["keydown", "pointerdown", "wheel", "touchstart"];
  const off = () => events.forEach((t) => removeEventListener(t, skip));
  function skip() {
    root.classList.add("intro-skip");
    off();
  }
  events.forEach((t) => addEventListener(t, skip, { passive: true }));
  setTimeout(off, 2600); // once the visor is open, clicks and keys belong to the page again
})();
