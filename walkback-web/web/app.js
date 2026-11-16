// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Walkback web viewer. Reads web/data/ (copied from session B and C outputs by
// tools/build_data.mjs) and draws the 1 km² site. It never computes routes or
// return times itself: those numbers come from data/route.json and data/return_map.bin
// (the team's model when it exists; until then the app's plan B over the relief, marked
// ESTIMATE, the same numbers the headset shows). Derived here, for display only: the HEIGHT contours and the SLOPE
// layer (from the heights), the RETURN ON FOOT rings (from the model's minutes),
// and the "what if the tank ran low" overlay, which compares the model's litres
// in return_map.bin with a smaller tank (the same check as the headset's WHAT IF).

import * as THREE from "three";
import { OrbitControls } from "three/addons/OrbitControls.js";
import { CSS2DRenderer, CSS2DObject } from "three/addons/CSS2DRenderer.js";
import { setupXR } from "./xr.js";
import { addCuriosity, pickTraversePoint } from "./curiosity.js";

const STEEP_DEG = 20;
const VEX = 2; // vertical exaggeration, display only (slopes use real heights)
const LIFT = 2.5; // metres above ground for drawn lines

const $ = (id) => document.getElementById(id);
const fmtMin = (m) => (m >= 60 ? `${Math.floor(m / 60)} h ${String(Math.round(m % 60)).padStart(2, "0")}` : `${Math.round(m)}`);
const fmtMinUnit = (m) => (m >= 60 ? "" : " min");
const num = (v) => (v === null || v === undefined || v === "" ? NaN : Number(v));

async function fetchOk(url) {
  const r = await fetch(url, { cache: "no-cache" });
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  return r;
}
const getJSON = (u) => fetchOk(u).then((r) => r.json());
const getBin = (u) => fetchOk(u).then((r) => r.arrayBuffer());
const maybe = (p) => p.catch(() => null);
function loadImage(url) {
  return new Promise((res) => {
    const img = new Image();
    img.onload = () => res(img);
    img.onerror = () => res(null);
    img.src = url;
  });
}

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined) e.textContent = text;
  return e;
}

// ---------- route.json normalisation (format owned by session C) ----------
function normalizeRoute(r) {
  if (!r) return null;
  const raw = r.points || r.route || r.path || r.waypoints || [];
  const points = raw
    .map((p) => ({
      x: num(p.x),
      y: num(p.y),
      minutes: num(p.minutes ?? p.t ?? p.time ?? p.elapsedMinutes),
      o2: num(p.o2Percent ?? p.o2 ?? p.o2Pct ?? p.o2Remaining),
      stopId: p.stopId ?? p.stop ?? null,
    }))
    .filter((p) => Number.isFinite(p.x) && Number.isFinite(p.y));
  let pnr = r.pointOfNoReturn ?? r.pnr ?? r.noReturn ?? null;
  if (pnr) {
    pnr = { x: num(pnr.x), y: num(pnr.y), minutes: num(pnr.minutes ?? pnr.t), o2: num(pnr.o2Percent ?? pnr.o2) };
    if (!Number.isFinite(pnr.x) || !Number.isFinite(pnr.y)) pnr = null;
  }
  const verdict = String(r.verdict ?? r.result ?? "").toUpperCase();
  const viable = !pnr && verdict.includes("VIABLE") && !verdict.includes("NOT");
  const capacity = num(r.o2CapacityLiters ?? r.capacityLiters ?? r.tankLiters);
  const reserve = num(r.o2ReserveLiters ?? r.reserveLiters);
  return {
    points, pnr, verdict, viable,
    capacity: Number.isFinite(capacity) && capacity > 0 ? capacity : null,
    reserve: Number.isFinite(reserve) && reserve > 0 ? reserve : 0, // litres kept untouched, as in the headset
    estimate: r.estimate === true, // plan B over the relief, not the walking model yet
    used: num(r.usedLiters), // litres the whole plan needs (walking and work)
  };
}

// ---------------------------------------------------------------------------
async function main() {
  const [site, hmBuf, stops, routeRaw, retBuf, status, ortho, traverse] = await Promise.all([
    getJSON("data/site.json"),
    getBin("data/heightmap.bin"),
    maybe(getJSON("data/stops.json")),
    maybe(getJSON("data/route.json")),
    maybe(getBin("data/return_map.bin")),
    maybe(getJSON("data/status.json")),
    loadImage("data/ortho.jpg"),
    maybe(getJSON("data/curiosity_traverse.json")),
  ]);

  if (status?.test) $("test-badge").hidden = false;

  const S = site.sideMeters;
  const HMN = site.heightmapSize;
  const G = site.gridSize;
  const STEP = S / (G - 1);
  const minE = site.minElevation, maxE = site.maxElevation;
  if (hmBuf.byteLength !== HMN * HMN * 2) throw new Error("heightmap size does not match site.json");

  // Heights (metres), row 0 = north.
  const dv = new DataView(hmBuf);
  const elev = new Float32Array(HMN * HMN);
  for (let i = 0; i < elev.length; i++) elev[i] = minE + (dv.getUint16(i * 2, true) / 65535) * (maxE - minE);

  function heightAt(x, y) {
    const fc = Math.min(Math.max((x / S) * (HMN - 1), 0), HMN - 1);
    const fr = Math.min(Math.max(((S - y) / S) * (HMN - 1), 0), HMN - 1);
    const c0 = Math.floor(fc), r0 = Math.floor(fr);
    const c1 = Math.min(c0 + 1, HMN - 1), r1 = Math.min(r0 + 1, HMN - 1);
    const tc = fc - c0, tr = fr - r0;
    const a = elev[r0 * HMN + c0], b = elev[r0 * HMN + c1];
    const c = elev[r1 * HMN + c0], d = elev[r1 * HMN + c1];
    return (a * (1 - tc) + b * tc) * (1 - tr) + (c * (1 - tc) + d * tc) * tr;
  }

  // Grid heights and slope (degrees) on the 257 x 257 contract grid.
  const gh = new Float32Array(G * G);
  for (let r = 0; r < G; r++) for (let c = 0; c < G; c++) gh[r * G + c] = heightAt(c * STEP, S - r * STEP);
  const slope = new Float32Array(G * G);
  for (let r = 0; r < G; r++) {
    for (let c = 0; c < G; c++) {
      const cl = Math.max(c - 1, 0), cr = Math.min(c + 1, G - 1);
      const rn = Math.max(r - 1, 0), rs = Math.min(r + 1, G - 1);
      const gx = (gh[r * G + cr] - gh[r * G + cl]) / ((cr - cl) * STEP);
      const gy = (gh[rn * G + c] - gh[rs * G + c]) / ((rs - rn) * STEP);
      slope[r * G + c] = (Math.atan(Math.hypot(gx, gy)) * 180) / Math.PI;
    }
  }
  const slopeAt = (x, y) => {
    const c = Math.round(Math.min(Math.max(x / STEP, 0), G - 1));
    const r = Math.round(Math.min(Math.max((S - y) / STEP, 0), G - 1));
    return slope[r * G + c];
  };

  // Local site metres (x east, y north, from SW corner) -> three.js world.
  const toWorld = (x, y, lift = 0) => new THREE.Vector3(x - S / 2, (heightAt(x, y) - minE) * VEX + lift, S / 2 - y);

  // ---------- header info ----------
  $("legend-vex").textContent = `Heights ×${VEX}`;

  // ---------- renderer / scene ----------
  const viewer = $("viewer");
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
  renderer.setClearColor(0x000000, 0);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  viewer.prepend(renderer.domElement);
  const labels = new CSS2DRenderer();
  labels.domElement.className = "labels";
  viewer.appendChild(labels.domElement);

  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0x120d0b, 2600, 5200);
  const camera = new THREE.PerspectiveCamera(42, 1, 1, 8000);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.maxPolarAngle = Math.PI * 0.47;
  controls.minDistance = 120;
  controls.maxDistance = 2600;
  // Let the page scroll over the 3D view: wheel zooms only with Ctrl (trackpad
  // pinch also sends ctrlKey). Touch pinch is unaffected.
  viewer.addEventListener("wheel", (e) => { if (!e.ctrlKey) e.stopPropagation(); }, { capture: true });
  if (window.matchMedia("(pointer: coarse)").matches) {
    $("hint").innerHTML = "Drag to turn · Pinch to zoom · <b>Tap the ground</b>";
  }
  const midY = ((maxE - minE) * VEX) / 2;
  controls.target.set(0, midY * 0.5, 0);

  scene.add(new THREE.HemisphereLight(0xffeadb, 0x3b2318, 1.6));
  const sun = new THREE.DirectionalLight(0xfff0dd, 2.4);
  sun.position.set(-700, 900, 300);
  scene.add(sun);
  // Terrain and every marker live in one group: xr.js moves and rescales it to stand
  // the viewer at the habitat at real scale (desktop keeps it at the identity).
  const world = new THREE.Group();
  scene.add(world);

  // ---------- terrain mesh ----------
  const pos = new Float32Array(G * G * 3);
  const uv = new Float32Array(G * G * 2);
  for (let r = 0; r < G; r++) {
    for (let c = 0; c < G; c++) {
      const i = r * G + c;
      pos[i * 3] = c * STEP - S / 2;
      pos[i * 3 + 1] = (gh[i] - minE) * VEX;
      pos[i * 3 + 2] = r * STEP - S / 2;
      uv[i * 2] = c / (G - 1);
      uv[i * 2 + 1] = 1 - r / (G - 1);
    }
  }
  const idx = new Uint32Array((G - 1) * (G - 1) * 6);
  let k = 0;
  for (let r = 0; r < G - 1; r++) {
    for (let c = 0; c < G - 1; c++) {
      const a = r * G + c, b = a + 1, d = a + G, e = d + 1;
      idx[k++] = a; idx[k++] = d; idx[k++] = b;
      idx[k++] = b; idx[k++] = d; idx[k++] = e;
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
  geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
  geo.setIndex(new THREE.BufferAttribute(idx, 1));
  geo.computeVertexNormals();

  // ---------- map layers (the headset's MAP LAYER buttons, same colours) ----------
  // HEIGHT: the orthophoto when there is one, else a sand tint with contour lines.
  // SLOPE: under 10° blue, 10–20° orange, over 20° hatched as no-go (from the heights).
  // RETURN ON FOOT: the model's minutes back to the habitat (return_map.bin), with rings.
  const ret = retBuf && retBuf.byteLength === G * G * 8 ? new Float32Array(retBuf) : null;
  const T = Math.min(window.innerWidth > 700 ? 2048 : 1024, renderer.capabilities.maxTextureSize);
  const C = Math.min(T, 1024); // resolution of the per-pixel paints (contours, rings)
  const APP = {
    sandLo: [168, 116, 72], sandHi: [226, 190, 140],
    slopeLow: [88, 184, 232], slopeMid: [216, 152, 24],
    hatch: "rgba(120, 52, 16, 0.95)",
    nearHome: [96, 176, 128], farHome: [36, 84, 84], noWay: [44, 46, 52],
  };
  const lerp3 = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const niceStep = (raw, steps) => steps.find((s) => s >= raw) ?? steps[steps.length - 1];

  // Bilinear value of a G x G grid (row 0 north) under pixel (px, py) of an n x n paint.
  const bilinear = (grid, px, py, n) => {
    const fc = (px / (n - 1)) * (G - 1), fr = (py / (n - 1)) * (G - 1);
    const c0 = Math.floor(fc), r0 = Math.floor(fr);
    const c1 = Math.min(c0 + 1, G - 1), r1 = Math.min(r0 + 1, G - 1);
    const tc = fc - c0, tr = fr - r0;
    return (grid[r0 * G + c0] * (1 - tc) + grid[r0 * G + c1] * tc) * (1 - tr)
      + (grid[r1 * G + c0] * (1 - tc) + grid[r1 * G + c1] * tc) * tr;
  };
  // Paints an n x n canvas pixel by pixel: rgbAt(px, py) -> [r, g, b].
  const paint = (n, rgbAt) => {
    const cv = document.createElement("canvas");
    cv.width = cv.height = n;
    const ctx = cv.getContext("2d");
    const img = ctx.createImageData(n, n);
    for (let py = 0; py < n; py++) {
      for (let px = 0; px < n; px++) {
        const c = rgbAt(px, py), o = (py * n + px) * 4;
        img.data[o] = c[0]; img.data[o + 1] = c[1]; img.data[o + 2] = c[2]; img.data[o + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    return cv;
  };
  // Pixel is on a line when its band differs from the pixel to the left or above.
  const banded = (n, valueAt, step) => {
    const band = new Float64Array(n * n);
    for (let py = 0; py < n; py++) for (let px = 0; px < n; px++) band[py * n + px] = Math.floor(valueAt(px, py) / step);
    return (px, py) => {
      const b = band[py * n + px];
      return (px > 0 && band[py * n + px - 1] !== b) || (py > 0 && band[(py - 1) * n + px] !== b);
    };
  };
  const toTexture = (cv) => {
    const t = new THREE.CanvasTexture(cv);
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = renderer.capabilities.getMaxAnisotropy();
    return t;
  };

  const contourStep = niceStep((maxE - minE) / 12, [1, 2, 5, 10, 20, 25, 50, 100]);
  const retMin = ret ? new Float32Array(G * G) : null;
  let retMax = 0;
  if (ret) {
    for (let i = 0; i < G * G; i++) {
      const m = ret[i * 2];
      const ok = Number.isFinite(m) && m >= 0 && m < 1e6;
      retMin[i] = ok ? m : NaN;
      if (ok && m > retMax) retMax = m;
    }
  }
  const ringStep = niceStep(retMax / 10, [1, 2, 5, 10, 15, 20, 30, 60]);

  const painters = {
    height() {
      if (ortho) {
        const cv = document.createElement("canvas");
        cv.width = cv.height = T;
        cv.getContext("2d").drawImage(ortho, 0, 0, T, T);
        return cv;
      }
      const hAt = (px, py) => bilinear(gh, px, py, C);
      const onLine = banded(C, hAt, contourStep);
      return paint(C, (px, py) => {
        const c = lerp3(APP.sandLo, APP.sandHi, (hAt(px, py) - minE) / (maxE - minE || 1));
        return onLine(px, py) ? [c[0] * 0.45, c[1] * 0.42, c[2] * 0.4] : c;
      });
    },
    slope() {
      const base = paint(C, (px, py) => (bilinear(slope, px, py, C) < 10 ? APP.slopeLow : APP.slopeMid));
      const mask = document.createElement("canvas");
      mask.width = mask.height = C;
      const mx = mask.getContext("2d");
      const md = mx.createImageData(C, C);
      for (let py = 0; py < C; py++) for (let px = 0; px < C; px++) md.data[(py * C + px) * 4 + 3] = bilinear(slope, px, py, C) > STEEP_DEG ? 255 : 0;
      mx.putImageData(md, 0, 0);

      const cv = document.createElement("canvas");
      cv.width = cv.height = T;
      const cx = cv.getContext("2d");
      cx.drawImage(base, 0, 0, T, T);
      const st = document.createElement("canvas");
      st.width = st.height = T;
      const sx = st.getContext("2d");
      sx.strokeStyle = APP.hatch;
      sx.lineWidth = T / 220;
      sx.beginPath();
      for (let i = -T; i < T * 2; i += T / 90) { sx.moveTo(i, 0); sx.lineTo(i - T, T); }
      sx.stroke();
      sx.globalCompositeOperation = "destination-in";
      sx.drawImage(mask, 0, 0, T, T);
      cx.drawImage(st, 0, 0);
      return cv;
    },
    return() {
      const mAt = (px, py) => bilinear(retMin, px, py, C);
      const onRing = banded(C, (px, py) => { const m = mAt(px, py); return Number.isFinite(m) ? m : -1; }, ringStep);
      return paint(C, (px, py) => {
        const m = mAt(px, py);
        if (!Number.isFinite(m)) return APP.noWay;
        if (onRing(px, py)) return [236, 244, 240];
        return lerp3(APP.nearHome, APP.farHome, Math.min(m / (retMax || 1), 1));
      });
    },
  };
  const textures = {};
  const layerTexture = (name) => (textures[name] ??= toTexture(painters[name]()));

  const terrain = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ map: layerTexture("slope") }));
  world.add(terrain);

  // Layer buttons and the one-line key under them, worded like the headset's legend.
  {
    const KEYS = {
      height: ortho
        ? "<b>PHOTO</b> orbital photo of the square, draped on the relief"
        : `<b>HEIGHT</b> <i class="k-sand"></i> low to high · a contour line every ${contourStep} m`,
      slope: '<b>SLOPE</b> <span class="k-low">under 10°</span> · <span class="k-mid">10–20° with care</span> · <span class="k-high">over 20° no-go, hatched</span>',
      return: `<b>RETURN ON FOOT</b> <i class="k-return"></i> minutes back to the habitat · a ring every ${ringStep} min · <span class="k-noway">dark: no way back</span>`,
    };
    const bar = $("layer-bar");
    const key = $("layer-key");
    const buttons = [...bar.querySelectorAll("button[data-layer]")];
    if (ortho) bar.querySelector('[data-layer="height"]').textContent = "Photo";
    const returnBtn = bar.querySelector('[data-layer="return"]');
    if (!ret) { returnBtn.disabled = true; returnBtn.title = "The way-back map is not loaded yet"; }
    const show = (name) => {
      terrain.material.map = layerTexture(name);
      for (const b of buttons) b.setAttribute("aria-pressed", String(b.dataset.layer === name));
      key.innerHTML = KEYS[name];
    };
    for (const b of buttons) b.addEventListener("click", () => { if (!b.disabled) show(b.dataset.layer); });
    show("slope");
  }

  // Dark base under the tile so it reads as a block of ground.
  {
    const base = new THREE.Mesh(
      new THREE.BoxGeometry(S, 24, S),
      new THREE.MeshLambertMaterial({ color: 0x24272c }),
    );
    base.position.y = -12.5;
    world.add(base);
  }

  function label(text, cls, obj, y) {
    const div = el("div", `lbl ${cls}`, text);
    const l = new CSS2DObject(div);
    l.position.set(0, y, 0);
    obj.add(l);
    return l;
  }

  // ---------- habitat ----------
  const hab = site.habitat;
  {
    const g = new THREE.Group();
    g.position.copy(toWorld(hab.x, hab.y));
    const dome = new THREE.Mesh(
      new THREE.SphereGeometry(16, 32, 16, 0, Math.PI * 2, 0, Math.PI / 2),
      new THREE.MeshLambertMaterial({ color: 0xf4f1ec }),
    );
    g.add(dome);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(22, 2, 8, 48), new THREE.MeshBasicMaterial({ color: 0x3fda65 }));
    ring.rotation.x = Math.PI / 2;
    ring.position.y = 1;
    g.add(ring);
    // The headset marks the habitat with a green pillar.
    const pillar = new THREE.Mesh(new THREE.CylinderGeometry(3, 3, 62, 12), new THREE.MeshLambertMaterial({ color: 0x36c35a, emissive: 0x0d3a18 }));
    pillar.position.y = 31;
    g.add(pillar);
    label("Habitat", "lbl-habitat", g, 70);
    world.add(g);
  }

  // ---------- science stops ----------
  const stopById = new Map();
  for (const s of stops || []) {
    const x = num(s.x), y = num(s.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
    stopById.set(String(s.id), s);
    const g = new THREE.Group();
    g.position.copy(toWorld(x, y));
    // Stops are amber pillars, as in the headset.
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(2.2, 2.2, 40, 10), new THREE.MeshLambertMaterial({ color: 0xffb60b, emissive: 0x3a2600 }));
    pole.position.y = 20;
    g.add(pole);
    const head = new THREE.Mesh(new THREE.SphereGeometry(6, 20, 12), new THREE.MeshLambertMaterial({ color: 0xffb60b, emissive: 0x4a3000 }));
    head.position.y = 42;
    g.add(head);
    label(s.name || String(s.id), "lbl-stop", g, 54);
    world.add(g);
  }

  // ---------- route + verdict (numbers from session C) ----------
  const route = normalizeRoute(routeRaw);
  renderRoutePanel(route, stopById);
  if (route && route.points.length > 1) {
    let split = route.points.length - 1;
    if (route.pnr) {
      let best = Infinity;
      route.points.forEach((p, i) => {
        const d = Math.hypot(p.x - route.pnr.x, p.y - route.pnr.y);
        if (d < best) { best = d; split = i; }
      });
    }
    const drape = (pts) => {
      const out = [];
      for (let i = 1; i < pts.length; i++) {
        const a = pts[i - 1], b = pts[i];
        const n = Math.max(1, Math.ceil(Math.hypot(b.x - a.x, b.y - a.y) / 4));
        for (let j = i === 1 ? 0 : 1; j <= n; j++) {
          const t = j / n;
          out.push(toWorld(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, LIFT));
        }
      }
      return out;
    };
    const tube = (pts, color) => {
      if (pts.length < 2) return;
      const v = drape(pts);
      const curve = new THREE.CatmullRomCurve3(v, false, "centripetal");
      world.add(new THREE.Mesh(
        new THREE.TubeGeometry(curve, Math.min(v.length * 2, 4000), 2.4, 8, false),
        new THREE.MeshBasicMaterial({ color }),
      ));
    };
    tube(route.points.slice(0, split + 1), 0xffb60b); // the headset's amber route
    if (route.pnr) tube(route.points.slice(split), 0xf23829);
  }
  if (route?.pnr) {
    $("legend-pnr").hidden = false;
    const g = new THREE.Group();
    g.position.copy(toWorld(route.pnr.x, route.pnr.y));
    const m = new THREE.Mesh(new THREE.SphereGeometry(9, 20, 12), new THREE.MeshBasicMaterial({ color: 0xf23829 }));
    m.position.y = 10;
    g.add(m);
    label("Point of no return", "lbl-pnr", g, 24);
    world.add(g);
  }

  // ---------- tap: minutes and % O2 to get home (session C return map) ----------
  const capacity = route?.capacity ?? null;
  const tapMarker = new THREE.Group();
  {
    const pin = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 26, 8), new THREE.MeshBasicMaterial({ color: 0xffffff }));
    pin.position.y = 13;
    const ball = new THREE.Mesh(new THREE.SphereGeometry(5, 16, 10), new THREE.MeshBasicMaterial({ color: 0xffffff }));
    ball.position.y = 28;
    const disc = new THREE.Mesh(new THREE.RingGeometry(6, 9, 32), new THREE.MeshBasicMaterial({ color: 0xffffff, side: THREE.DoubleSide }));
    disc.rotation.x = -Math.PI / 2;
    disc.position.y = 1.5;
    tapMarker.add(pin, ball, disc);
    tapMarker.visible = false;
    world.add(tapMarker);
  }

  // ---------- what if the tank ran low (the headset's WHAT IF) ----------
  // Red where the litres the model says it takes to walk back are more than a smaller
  // tank still holds, or where there is no way back at all. Nothing is recomputed.
  let budget = null; // { pct, liters } while a what-if is on
  let lastTap = null;
  if (ret && capacity) {
    const mask = document.createElement("canvas");
    mask.width = mask.height = G;
    const mx = mask.getContext("2d");
    const md = mx.createImageData(G, G);
    const big = document.createElement("canvas");
    big.width = big.height = 1024;
    const bx = big.getContext("2d");
    const overlayTex = new THREE.CanvasTexture(big);
    overlayTex.colorSpace = THREE.SRGBColorSpace;
    const overlay = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({
      map: overlayTex, transparent: true, depthWrite: false,
      polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2,
    }));
    overlay.visible = false;
    overlay.renderOrder = 1;
    world.add(overlay);

    // Paints the mask for `liters` of oxygen to walk with; returns the share in red of the
    // ground that has a way back on foot (the headset's count: ground with no way back is not in it).
    const paint = (liters) => {
      let red = 0, reachable = 0;
      for (let i = 0; i < G * G; i++) {
        const m = ret[i * 2], l = ret[i * 2 + 1];
        const noWay = !(Number.isFinite(m) && m >= 0 && m < 1e6);
        const out = noWay || (Number.isFinite(l) && l > liters);
        if (!noWay) { reachable++; if (out) red++; }
        md.data[i * 4] = 255; md.data[i * 4 + 1] = 64; md.data[i * 4 + 2] = 56;
        md.data[i * 4 + 3] = out ? 160 : 0;
      }
      mx.putImageData(md, 0, 0);
      bx.clearRect(0, 0, big.width, big.height);
      bx.imageSmoothingEnabled = true;
      bx.drawImage(mask, 0, 0, big.width, big.height);
      overlayTex.needsUpdate = true;
      return reachable ? red / reachable : 0;
    };

    const box = $("whatif");
    const buttons = [...box.querySelectorAll("button[data-o2]")];
    const note = $("whatif-note");
    const choose = (btn) => {
      const pct = Number(btn.dataset.o2);
      for (const b of buttons) b.setAttribute("aria-pressed", String(b === btn));
      if (!(pct < 100)) {
        budget = null;
        overlay.visible = false;
        note.textContent = "As planned: the panel shows the whole walk with a full tank.";
      } else {
        // What is left to walk with: the tank at this level minus the reserve (the headset's rule).
        const tank = (capacity * pct) / 100;
        budget = { pct, liters: Math.max(0, tank - route.reserve) };
        const share = paint(budget.liters);
        overlay.visible = true;
        const kept = route.reserve ? `, ${Math.round(route.reserve)} L of it kept as reserve` : "";
        // The panel keeps the full-tank plan; say what the headset's verdict is for this tank.
        const short = Number.isFinite(route.used) && route.used > budget.liters;
        const plan = short
          ? ` The planned walk no longer fits (it needs ${Math.round(route.used)} L): the headset's verdict turns NOT VIABLE. The panel still shows the full-tank plan.`
          : " The panel still shows the full-tank plan.";
        note.textContent = (share > 0
          ? `With ${pct}% of the tank left (${Math.round(tank)} L${kept}), red is ground you could no longer walk back from: ${(share * 100).toFixed(1)}% of the ground that has a way back on foot.`
          : `With ${pct}% of the tank left (${Math.round(tank)} L${kept}), no ground turns red yet: you could still walk straight back from anywhere that has a way back.`) + plan;
      }
      $("legend-whatif").hidden = !overlay.visible;
      if (tapMarker.visible && lastTap) showTap(lastTap.x, lastTap.y);
    };
    for (const b of buttons) b.addEventListener("click", () => choose(b));
    box.hidden = false;
  }

  function showTap(x, y) {
    lastTap = { x, y };
    tapMarker.position.copy(toWorld(x, y));
    tapMarker.visible = true;
    const body = $("tap-body");
    body.replaceChildren();
    const sl = slopeAt(x, y);
    const slopeLine = sl > STEEP_DEG ? el("p", "tap-warn", `Too steep here: ${Math.round(sl)}°`) : "";
    if (!ret) {
      body.append(el("p", "tap-sub", "Way-back numbers are coming soon."), slopeLine);
    } else {
      const c = Math.round(Math.min(Math.max(x / STEP, 0), G - 1));
      const r = Math.round(Math.min(Math.max((S - y) / STEP, 0), G - 1));
      const i = r * G + c;
      const minutes = ret[i * 2], liters = ret[i * 2 + 1];
      const ok = Number.isFinite(minutes) && minutes >= 0 && minutes < 1e6;
      if (!ok) {
        body.append(el("p", "tap-bad", "No safe way back from here"), slopeLine);
      } else {
        const nums = el("div", "tap-nums");
        const a = el("div", "tap-big", fmtMin(minutes));
        a.append(el("small", "", fmtMinUnit(minutes).trim() || ""));
        nums.append(a);
        if (capacity && Number.isFinite(liters)) {
          const b = el("div", "tap-big", `${Math.round((liters / capacity) * 100)}`);
          b.append(el("small", "", "% O₂"));
          nums.append(b);
        }
        body.append(nums);
        if (budget && Number.isFinite(liters) && liters > budget.liters) {
          body.append(el("p", "tap-bad", `With ${budget.pct}% left, you could not walk back from here`));
        }
        if (!capacity && Number.isFinite(liters)) body.append(el("p", "tap-sub", `${liters.toFixed(1)} L of oxygen`));
        body.append(slopeLine);
      }
      // The return map is the app's plan B over the relief: every number on this card is an ESTIMATE.
      body.append(el("p", "tap-est", "Estimate · plan B over the relief"));
    }
    $("tap-card").hidden = false;
  }
  $("tap-close").addEventListener("click", () => {
    $("tap-card").hidden = true;
    tapMarker.visible = false;
  });

  const ray = new THREE.Raycaster();
  let down = null;
  const canvas = renderer.domElement;
  canvas.addEventListener("pointerdown", (e) => {
    down = { x: e.clientX, y: e.clientY, t: performance.now() };
    $("hint").classList.add("gone");
  });
  canvas.addEventListener("pointerup", (e) => {
    if (!down) return;
    const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y);
    const quick = performance.now() - down.t < 450;
    down = null;
    if (moved > 8 || !quick) return;
    const rect = canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
    ray.setFromCamera(ndc, camera);
    const hit = ray.intersectObject(terrain, false)[0];
    if (!hit) return;
    const x = hit.point.x + S / 2, y = S / 2 - hit.point.z;
    if (x < 0 || y < 0 || x > S || y > S) return;
    showTap(x, y);
  });

  // ---------- sizing + loop ----------
  function frame() {
    const w = viewer.clientWidth, h = viewer.clientHeight;
    renderer.setSize(w, h, false); // CSS keeps the canvas at 100% of the viewer
    labels.setSize(w, h);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  }
  frame();
  // First view: from the south-west, framed for the screen's shape.
  {
    const dist = camera.aspect < 1 ? 1100 / Math.pow(camera.aspect, 0.85) : 1150;
    const dir = new THREE.Vector3(-0.42, 0.68, 0.6).normalize();
    camera.position.copy(controls.target).addScaledVector(dir, Math.min(dist, controls.maxDistance));
    controls.update();
  }
  new ResizeObserver(frame).observe(viewer);

  renderer.setAnimationLoop(() => {
    // In the headset the viewer's own pose drives the camera; HTML labels can't be seen there.
    const inVR = renderer.xr.isPresenting;
    if (!inVR) controls.update();
    renderer.render(scene, camera);
    if (!inVR) labels.render(scene, camera);
  });
  $("loading").remove();

  // Enter VR: stand at the habitat in Gale at real scale (xr.js). The button lives in
  // the "In the headset" section; it says why when the browser has no WebXR.
  const xrSlot = $("xr-slot");
  if (xrSlot) {
    try {
      setupXR({ renderer, scene, camera, world, habitat: hab, stops, toWorld, vex: VEX, container: xrSlot });
    } catch (err) {
      console.warn("VR setup failed.", err);
    }
  }

  // Curiosity at one of its real positions inside the square (session B's traverse), if present.
  const roverAt = traverse ? pickTraversePoint(traverse, S) : null;
  if (roverAt) addCuriosity(world, toWorld, { x: roverAt.x, y: roverAt.y, vex: VEX });

  renderCredits(site.sources);
}

// ---------- page sections ----------
function renderRoutePanel(route, stopById) {
  const box = $("verdict");
  const word = box.querySelector(".verdict-word");
  const text = box.querySelector(".verdict-text");
  if (!route || route.points.length < 2) {
    word.textContent = "Route pending";
    text.textContent = "The route has not been built for this map yet (tools/build_data.mjs).";
    return;
  }
  const last = route.points[route.points.length - 1];
  box.classList.remove("verdict-pending");
  $("verdict-estimate").hidden = !route.estimate;
  $("steps-estimate").hidden = !route.estimate;
  if (route.viable) {
    box.classList.add("verdict-ok");
    word.textContent = "VIABLE";
    text.textContent = Number.isFinite(last.o2)
      ? `The whole walk fits: back home with ${Math.round(last.o2)}% of the oxygen left.`
      : "The whole walk fits, with a safe way home at every step.";
  } else {
    box.classList.add("verdict-bad");
    word.textContent = "POINT OF NO RETURN";
    const p = route.pnr;
    text.textContent = p && Number.isFinite(p.minutes)
      ? `After minute ${Math.round(p.minutes)}${Number.isFinite(p.o2) ? ` (${Math.round(p.o2)}% O₂ left)` : ""} there is no safe way home. Turn back before it.`
      : "Somewhere on this route there is no safe way home. Turn back before the red part.";
  }

  const stopPts = route.points.filter((p) => p.stopId !== null && p.stopId !== undefined);
  $("kpis").hidden = false;
  $("st-min").textContent = Number.isFinite(last.minutes) ? `${fmtMin(last.minutes)}${fmtMinUnit(last.minutes)}` : "–";
  $("st-o2-label").textContent = route.viable ? "O₂ left at home" : "O₂ left at the end";
  $("st-o2").textContent = Number.isFinite(last.o2) ? `${Math.round(last.o2)}%` : "–";

  const ol = $("steps");
  ol.replaceChildren();
  const row = (dotText, dotCls, name, p) => {
    const li = el("li");
    li.append(el("span", `step-dot ${dotCls}`, dotText), el("span", "step-name", name));
    const nums = el("span", "step-nums");
    if (Number.isFinite(p.minutes)) {
      nums.append(el("b", "", `${fmtMin(p.minutes)}${fmtMinUnit(p.minutes)}`));
    }
    if (Number.isFinite(p.o2)) nums.append(document.createTextNode(` · ${Math.round(p.o2)}% O₂`));
    li.append(nums);
    ol.append(li);
  };
  row("H", "home", "Leave the habitat", route.points[0]);
  stopPts.forEach((p, i) => {
    const s = stopById.get(String(p.stopId));
    // The dot holds a short mark: the stop's id when it is one or two characters, else its order.
    const id = String(p.stopId);
    row(id.length <= 2 ? id : String(i + 1), "", s?.name ?? `Stop ${id}`, p);
  });
  if (route.pnr) row("!", "pnr", "Point of no return", route.pnr);
  row("H", "home", route.viable ? "Back at the habitat" : "End of route", last);
}

function renderCredits(sources) {
  const ul = $("credits-list");
  ul.replaceChildren();
  // Hide placeholder values so the footer stays clean until real credits arrive.
  const real = (v) => v && !/^(TODO|—|-)$/i.test(String(v).trim());
  const list = Array.isArray(sources) ? sources : [];
  for (const s of list) {
    if (/^none$/i.test(String(s.resolution ?? "").trim())) continue; // a layer the package says is not mapped: nothing to credit
    const li = el("li");
    li.append(el("strong", "", s.layer ?? "Layer"));
    const meta = [s.mission, s.instrument, s.resolution].filter(real).join(" · ");
    if (meta) li.append(el("span", "credit-meta", meta));
    if (real(s.credit)) li.append(el("span", "credit-meta", s.credit));
    if (meta || real(s.credit)) ul.append(li);
  }
  if (!ul.children.length) ul.append(el("li", "muted", "Data credits arrive with the real Gale Crater data."));
}

main().catch((err) => {
  console.error(err);
  const l = $("loading");
  if (l) {
    l.classList.add("error");
    l.textContent = `Could not load the terrain: ${err.message}`;
  }
});
