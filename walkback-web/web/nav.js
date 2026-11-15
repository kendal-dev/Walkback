// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Top bar: marks the section on screen with aria-current="true", and on phones turns the
// links into a menu behind a button. Without JavaScript the links stay visible as a plain row.
(() => {
  const header = document.querySelector(".nav");
  const links = document.getElementById("nav-links");
  const toggle = header && header.querySelector(".nav-toggle");
  if (!header || !links || !toggle) return;

  document.documentElement.classList.add("js-nav");
  // Turn the menu's fade on only after the closed state has been drawn once, so it never
  // fades out on page load. The timeout covers tabs that draw no frames in the background.
  const animate = () => header.classList.add("nav-anim");
  requestAnimationFrame(() => requestAnimationFrame(animate));
  setTimeout(animate, 500);

  const setOpen = (open) => {
    header.classList.toggle("is-open", open);
    toggle.setAttribute("aria-expanded", String(open));
    toggle.setAttribute("aria-label", open ? "Close menu" : "Open menu");
  };
  toggle.addEventListener("click", () => setOpen(!header.classList.contains("is-open")));
  links.addEventListener("click", (e) => { if (e.target.closest("a")) setOpen(false); });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && header.classList.contains("is-open")) { setOpen(false); toggle.focus(); }
  });
  document.addEventListener("click", (e) => { if (!header.contains(e.target)) setOpen(false); });

  // Section on screen: the last one whose top has passed a line just under the bar.
  const anchors = [...links.querySelectorAll('a[href^="#"]')];
  const sections = anchors.map((a) => document.querySelector(a.getAttribute("href")));
  let ticking = false;
  const mark = () => {
    ticking = false;
    const line = header.offsetHeight + window.innerHeight * 0.25;
    let current = -1;
    sections.forEach((s, i) => { if (s && s.getBoundingClientRect().top <= line) current = i; });
    // At the very bottom the last section wins even if it is short.
    if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2) current = sections.length - 1;
    anchors.forEach((a, i) => {
      if (i === current) a.setAttribute("aria-current", "true");
      else a.removeAttribute("aria-current");
    });
  };
  const onScroll = () => { if (!ticking) { ticking = true; requestAnimationFrame(mark); } };
  window.addEventListener("scroll", onScroll, { passive: true });
  window.addEventListener("resize", onScroll);
  mark();
})();
