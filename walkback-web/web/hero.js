// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Hero: the team's Mars globe (data/mars.glb) turning slowly, and a small rocket on a tilted
// orbit around it, built from three.js shapes and lit by the same sun, so it passes in front
// of Mars and hides behind it. "Zoom to Gale", all in 3D: the globe turns Gale crater to the
// front, the camera closes in and dives at an angle over it while a beacon rises from the crater,
// holds, and comes back the same way to how the scene was.
// The CSS planet and the SVG rocket stay as placeholders until the model is ready, and remain
// if it fails. The rocket is decoration, not mission data.

import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { OrbitControls } from "three/addons/OrbitControls.js";

const stage = document.getElementById("hero-stage");
const touch = window.matchMedia("(pointer: coarse)").matches;
const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

// Where Gale crater and the north pole sit on the model's texture (a cube map, so they were
// found on the image itself: Gale is the crater with a central mound on the dichotomy edge,
// south-west of Elysium; the north pole is the centre of the polar cap).
const GALE_UV = [0.762, 0.521];
const NORTH_UV = [0.625, 0.163];

// ---------- the rocket: hull, painted nose and band, window, three fins, nozzle and flame ----------
// Built 1 unit long along +Y (nozzle exit at y = 0, tip at y ≈ 0.97); scaled down by the caller.
function makeRocket() {
  const rocket = new THREE.Group();
  const hull = new THREE.MeshStandardMaterial({ color: 0xefebe5, metalness: 0.35, roughness: 0.36 });
  const paint = new THREE.MeshStandardMaterial({ color: 0xe8673c, metalness: 0.15, roughness: 0.42 });
  const metal = new THREE.MeshStandardMaterial({ color: 0x5c626a, metalness: 0.9, roughness: 0.28, side: THREE.DoubleSide });
  const lathe = (pts, mat) => new THREE.Mesh(new THREE.LatheGeometry(pts.map(([x, y]) => new THREE.Vector2(x, y)), 48), mat);

  rocket.add(lathe([[0, 0.06], [0.1, 0.06], [0.122, 0.12], [0.13, 0.32], [0.126, 0.54], [0.106, 0.67]], hull));
  rocket.add(lathe([[0.106, 0.67], [0.083, 0.78], [0.05, 0.88], [0.016, 0.95], [0, 0.968]], paint));
  const band = new THREE.Mesh(new THREE.CylinderGeometry(0.1312, 0.1312, 0.04, 48, 1, true), paint);
  band.position.y = 0.26;
  rocket.add(band);

  const glass = new THREE.MeshStandardMaterial({ color: 0x47d1ff, emissive: 0x16607c, emissiveIntensity: 1, metalness: 0.3, roughness: 0.08 });
  const win = new THREE.Mesh(new THREE.SphereGeometry(0.046, 24, 16), glass);
  win.scale.z = 0.45;
  win.position.set(0, 0.5, 0.117);
  const rim = new THREE.Mesh(new THREE.TorusGeometry(0.05, 0.01, 10, 32), metal);
  rim.position.set(0, 0.5, 0.121);
  rocket.add(win, rim);

  const fin = new THREE.Shape();
  fin.moveTo(0, 0); fin.lineTo(0.12, -0.08); fin.lineTo(0.14, -0.22); fin.lineTo(0, -0.14); fin.lineTo(0, 0);
  const finGeo = new THREE.ExtrudeGeometry(fin, { depth: 0.012, bevelEnabled: true, bevelThickness: 0.004, bevelSize: 0.004, bevelSegments: 1 });
  finGeo.translate(0, 0, -0.006);
  for (let i = 0; i < 3; i++) {
    const holder = new THREE.Group();
    holder.rotation.y = (i * Math.PI * 2) / 3 + Math.PI / 2;
    const f = new THREE.Mesh(finGeo, paint);
    f.position.set(0.112, 0.25, 0);
    holder.add(f);
    rocket.add(holder);
  }

  const bell = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.088, 0.07, 32, 1, true), metal);
  bell.position.y = 0.03;
  rocket.add(bell);

  const flameMat = (color, opacity) => new THREE.MeshBasicMaterial({
    color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false,
  });
  const cone = (radius, length, mat) => {
    const geo = new THREE.ConeGeometry(radius, length, 24, 1, true);
    geo.rotateX(Math.PI);
    geo.translate(0, -length / 2, 0);
    return new THREE.Mesh(geo, mat);
  };
  const outer = cone(0.075, 0.46, flameMat(0xff6a1f, 0.55));
  const inner = cone(0.042, 0.27, flameMat(0xfff0c0, 0.95));
  rocket.add(outer, inner);

  const g = document.createElement("canvas");
  g.width = g.height = 64;
  const gx = g.getContext("2d");
  const grad = gx.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, "rgba(255, 220, 150, 1)");
  grad.addColorStop(0.35, "rgba(255, 140, 60, .55)");
  grad.addColorStop(1, "rgba(255, 100, 40, 0)");
  gx.fillStyle = grad;
  gx.fillRect(0, 0, 64, 64);
  const glow = new THREE.Sprite(new THREE.SpriteMaterial({
    map: new THREE.CanvasTexture(g), blending: THREE.AdditiveBlending, depthWrite: false, transparent: true,
  }));
  glow.scale.setScalar(0.55);
  glow.position.y = -0.08;
  const light = new THREE.PointLight(0xff8a3c, 0.6, 0.6, 2);
  light.position.y = -0.1;
  rocket.add(glow, light);

  return { rocket, outer, inner, glow };
}

// The surface point (from the globe's centre, in the globe's own frame) of the vertex whose UV
// is closest to `uv`. Call while the globe group has no rotation.
function pointAtUV(mesh, uv) {
  const pos = mesh.geometry.attributes.position, tex = mesh.geometry.attributes.uv;
  let best = -1, bestD = Infinity;
  for (let i = 0; i < tex.count; i++) {
    const du = tex.getX(i) - uv[0], dv = tex.getY(i) - uv[1];
    const d = du * du + dv * dv;
    if (d < bestD) { bestD = d; best = i; }
  }
  return new THREE.Vector3().fromBufferAttribute(pos, best).applyMatrix4(mesh.matrixWorld);
}

// A pillar of light with pulsing rings, standing on the surface along local +Y (the app marks
// the landing site on its globe the same way).
function makeBeacon() {
  const beacon = new THREE.Group();
  const add = (color, opacity) => new THREE.MeshBasicMaterial({
    color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
  });
  const H = 0.2;
  const core = new THREE.Mesh(new THREE.CylinderGeometry(0.0035, 0.0035, H, 12, 1, true).translate(0, H / 2, 0), add(0xe9fbff, 0.95));
  const halo = new THREE.Mesh(new THREE.CylinderGeometry(0.004, 0.016, H, 16, 1, true).translate(0, H / 2, 0), add(0x47d1ff, 0.35));
  const g = document.createElement("canvas");
  g.width = g.height = 64;
  const gx = g.getContext("2d");
  const grad = gx.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, "rgba(220, 248, 255, 1)");
  grad.addColorStop(0.4, "rgba(71, 209, 255, .5)");
  grad.addColorStop(1, "rgba(71, 209, 255, 0)");
  gx.fillStyle = grad;
  gx.fillRect(0, 0, 64, 64);
  const foot = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(g), blending: THREE.AdditiveBlending, depthWrite: false, transparent: true }));
  foot.scale.setScalar(0.07);
  const rings = [0, 1, 2].map(() => {
    const m = new THREE.Mesh(new THREE.RingGeometry(0.02, 0.024, 64).rotateX(-Math.PI / 2), add(0x47d1ff, 0.8));
    m.position.y = 0.002;
    beacon.add(m);
    return m;
  });
  beacon.add(core, halo, foot);
  beacon.userData = { rings, core, halo, foot };
  return beacon;
}

// A thin glow just outside the planet's edge: brighter where you look across the atmosphere.
function makeAtmosphere(radius) {
  return new THREE.Mesh(
    new THREE.SphereGeometry(radius, 64, 32),
    new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.BackSide,
      uniforms: { glow: { value: new THREE.Color(0xff9a6a) } },
      vertexShader: `
        varying vec3 vN; varying vec3 vV;
        void main() {
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz);
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader: `
        uniform vec3 glow; varying vec3 vN; varying vec3 vV;
        void main() {
          float f = pow(1.0 - abs(dot(vN, vV)), 4.0);
          gl_FragColor = vec4(glow * f * 1.1, f);
        }`,
    }),
  );
}

const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

async function start() {
  const gltf = await new GLTFLoader().loadAsync("data/mars.glb");
  const mars = gltf.scene;

  // Centre and scale the globe, whatever units the file uses.
  const sphere = new THREE.Box3().setFromObject(mars).getBoundingSphere(new THREE.Sphere());
  mars.position.sub(sphere.center);
  const globe = new THREE.Group();
  globe.add(mars);
  globe.scale.setScalar(1 / sphere.radius);

  // Gale's direction and the north pole in the globe's frame (before any rotation), and the
  // globe orientation that puts Gale facing the camera with north up.
  globe.updateMatrixWorld(true);
  let surface = null;
  mars.traverse((o) => { if (!surface && o.isMesh && o.geometry.attributes.uv) surface = o; });
  let galeQuat = null, galeDir = null, galeR = 0.577;
  if (surface) {
    const gp = pointAtUV(surface, GALE_UV);
    galeR = gp.length();
    const f = gp.clone().normalize();
    galeDir = f;
    const n = pointAtUV(surface, NORTH_UV).normalize();
    const u = n.clone().addScaledVector(f, -n.dot(f)).normalize();
    const r = new THREE.Vector3().crossVectors(u, f).normalize();
    galeQuat = new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(r, u, f).invert());
  }
  globe.rotation.z = THREE.MathUtils.degToRad(25); // Mars axial tilt

  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setClearColor(0x000000, 0);
  renderer.domElement.setAttribute("aria-hidden", "true");

  const scene = new THREE.Scene();
  scene.add(globe);
  scene.add(new THREE.AmbientLight(0xffe6d8, 0.35));
  const sun = new THREE.DirectionalLight(0xfff2e6, 3.2);
  sun.position.set(-3, 1.5, 2.5);
  scene.add(sun);
  const rim = new THREE.DirectionalLight(0xff7a45, 1.2);
  rim.position.set(3, -1, -2);
  scene.add(rim);
  scene.add(makeAtmosphere(galeR * 1.045));

  // The beacon on Gale turns with the globe (a pivot copies its rotation; the globe itself is scaled).
  const pivot = new THREE.Group();
  scene.add(pivot);
  const beacon = makeBeacon();
  beacon.visible = false;
  if (galeDir) {
    beacon.position.copy(galeDir).multiplyScalar(galeR * 0.998);
    beacon.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), galeDir);
    pivot.add(beacon);
  }
  const setBeacon = (k) => { // 0 = hidden, 1 = full height
    beacon.visible = k > 0.01;
    const { core, halo, foot } = beacon.userData;
    core.scale.y = halo.scale.y = Math.max(k, 0.001);
    foot.material.opacity = k;
  };

  // ---------- the rocket's orbit: tilted, so it crosses in front of Mars and hides behind it ----------
  // Mars' visible radius here is about 0.58 (the bounding sphere above wraps the model's box).
  const R = 0.9;
  const orbit = new THREE.Group();
  orbit.rotation.set(0.4, 0, -0.42);
  scene.add(orbit);

  const ringPts = [];
  for (let i = 0; i <= 160; i++) {
    const a = (i / 160) * Math.PI * 2;
    ringPts.push(new THREE.Vector3(R * Math.cos(a), 0, R * Math.sin(a)));
  }
  const ring = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints(ringPts),
    new THREE.LineDashedMaterial({ color: 0xffd2b8, transparent: true, opacity: 0.3, dashSize: 0.025, gapSize: 0.035 }),
  );
  ring.computeLineDistances();
  orbit.add(ring);

  const TRAIL = 56, STEP = 0.016;
  const trailPos = new Float32Array(TRAIL * 3);
  const trailCol = new Float32Array(TRAIL * 4);
  for (let k = 0; k < TRAIL; k++) {
    const t = k / (TRAIL - 1);
    trailCol.set([1 - 0.72 * t, 0.72 + 0.1 * t, 0.25 + 0.75 * t, 0.85 * (1 - t) ** 1.6], k * 4);
  }
  const trailGeo = new THREE.BufferGeometry();
  trailGeo.setAttribute("position", new THREE.BufferAttribute(trailPos, 3));
  trailGeo.setAttribute("color", new THREE.BufferAttribute(trailCol, 4));
  const trail = new THREE.Line(trailGeo, new THREE.LineBasicMaterial({
    vertexColors: true, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
  }));
  orbit.add(trail);

  const { rocket, outer, inner, glow } = makeRocket();
  rocket.scale.setScalar(0.24);
  const carrier = new THREE.Group(); // places and points the rocket; the rocket itself rolls inside
  carrier.add(rocket);
  orbit.add(carrier);

  // Everything in the orbit can fade (used while zooming to Gale).
  const fadeable = [];
  orbit.traverse((o) => {
    if (!o.material) return;
    for (const m of [].concat(o.material)) {
      fadeable.push([m, m.opacity, m.transparent]);
    }
  });
  const setOrbitFade = (k) => {
    orbit.visible = k > 0.01;
    for (const [m, base, wasTransparent] of fadeable) {
      m.transparent = wasTransparent || k < 1;
      m.opacity = base * k;
    }
  };

  const UP = new THREE.Vector3(0, 1, 0);
  const tangent = new THREE.Vector3();
  let theta = 2.2; // where it starts: in front of Mars, lower left
  const placeRocket = () => {
    carrier.position.set(R * Math.cos(theta), 0, R * Math.sin(theta));
    tangent.set(-Math.sin(theta), 0, Math.cos(theta));
    carrier.quaternion.setFromUnitVectors(UP, tangent);
    for (let k = 0; k < TRAIL; k++) {
      const a = theta - 0.05 - k * STEP;
      trailPos[k * 3] = R * Math.cos(a);
      trailPos[k * 3 + 1] = 0;
      trailPos[k * 3 + 2] = R * Math.sin(a);
    }
    trailGeo.attributes.position.needsUpdate = true;
  };
  placeRocket();

  const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 100);
  const HOME = new THREE.Vector3(0, 0, 4.2);
  camera.position.copy(HOME);

  // Desktop: drag to turn the globe. Phones: no controls, so the page still scrolls.
  let controls = null;
  if (!touch) {
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableZoom = false;
    controls.enablePan = false;
    controls.enableDamping = true;
    renderer.domElement.style.cursor = "grab";
  }

  stage.append(renderer.domElement);
  // Size the drawing and the camera's aspect to the stage. 30° across the smaller side, so a
  // non-square box keeps Mars' framing instead of cutting it at the sides.
  const resize = () => {
    const w = Math.max(stage.clientWidth, 1), h = Math.max(stage.clientHeight || stage.clientWidth, 1);
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.fov = camera.aspect < 1
      ? THREE.MathUtils.radToDeg(2 * Math.atan(Math.tan(THREE.MathUtils.degToRad(15)) / camera.aspect))
      : 30;
    camera.updateProjectionMatrix();
  };
  resize();
  new ResizeObserver(resize).observe(stage);

  let visible = true;
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(stage);

  // ---------- Zoom to Gale, then back (all 3D, inside the hero) ----------
  // Mars always stays whole inside its box (no square cut): it grows to ~84 % of the box,
  // 1. Gale turns to the front, 2. the camera swings below the planet so the crater sits
  // high on the globe and the beacon stands up from it, 3. a hold with the crater's name,
  // 4. the same, reversed.
  const FILL = 0.84;
  const CLOSE = galeR / (FILL * Math.tan(THREE.MathUtils.degToRad(15)));
  const SWING = THREE.MathUtils.degToRad(26); // how far below the planet the camera swings
  const ORIGIN = new THREE.Vector3(0, 0, 0);
  const NEAR_P = new THREE.Vector3(0, 0, CLOSE);
  const hud = document.getElementById("gale-hud");
  const label = document.getElementById("gale-label");
  const button = document.getElementById("gale-btn");
  let tween = null; // { p, dur, step, done }: p goes 0 → 1
  let busy = false;
  const speed = 1;
  const look = new THREE.Vector3();

  const run = (dur, step) => new Promise((done) => { tween = { p: 0, dur, step, done }; });
  const hold = (ms) => run(ms, () => {});
  // Turn the globe and move the camera in a straight line; fade the orbit; raise the beacon.
  const turnAndMove = (fromQ, toQ, fromP, toP, fadeFrom, fadeTo, beaconFrom, beaconTo) => (k) => {
    const e = ease(k);
    if (fromQ !== toQ) globe.quaternion.slerpQuaternions(fromQ, toQ, e);
    camera.position.lerpVectors(fromP, toP, e);
    camera.lookAt(ORIGIN);
    setOrbitFade(fadeFrom + (fadeTo - fadeFrom) * e);
    setBeacon(beaconFrom + (beaconTo - beaconFrom) * e);
  };
  // Swing the camera on a circle around Mars (same distance, so it never grows past its box),
  // looking a little above the centre so Gale and the beacon sit in the middle of the view.
  const swing = (fromA, toA, beaconFrom, beaconTo) => (k) => {
    const e = ease(k);
    const a = fromA + (toA - fromA) * e;
    camera.position.set(0, -CLOSE * Math.sin(a), CLOSE * Math.cos(a));
    camera.lookAt(look.set(0, galeR * 0.14 * (a / SWING), 0));
    setBeacon(beaconFrom + (beaconTo - beaconFrom) * e);
  };

  async function zoomToGale() {
    if (busy || !galeQuat) return;
    busy = true;
    if (button) button.disabled = true;
    if (controls) controls.enabled = false;
    const homeQ = globe.quaternion.clone();
    const homeP = camera.position.clone();

    await run(2200, turnAndMove(homeQ, galeQuat, homeP, NEAR_P, 1, 0, 0, 0.35));
    hud?.classList.add("is-on");
    await run(2600, swing(0, SWING, 0.35, 1));
    label?.classList.add("is-on");
    await hold(2400);
    label?.classList.remove("is-on");
    hud?.classList.remove("is-on");
    await run(2200, swing(SWING, 0, 1, 0.35));
    await run(2200, turnAndMove(galeQuat, homeQ, NEAR_P, homeP, 0, 1, 0.35, 0));

    if (controls) controls.enabled = true;
    if (button) button.disabled = false;
    busy = false;
  }

  if (button && galeQuat) {
    button.hidden = false;
    button.addEventListener("click", zoomToGale);
  }

  const clock = new THREE.Clock();
  let lastNow = performance.now();
  renderer.setAnimationLoop(() => {
    if (!visible && !busy) return;
    const dt = Math.min(clock.getDelta(), 0.1);
    // The zoom runs on the wall clock (not on frames drawn), so slow devices take the same time.
    const now = performance.now();
    const realMs = Math.min(now - lastNow, 250);
    lastNow = now;
    if (tween) {
      tween.p = Math.min(tween.p + (realMs * speed) / tween.dur, 1);
      const k = tween.p;
      tween.step(k);
      if (k >= 1) { const d = tween.done; tween = null; d(); }
    }
    pivot.quaternion.copy(globe.quaternion);
    if (beacon.visible) { // rings spread out from the crater and fade, one after the other
      const now = performance.now() / 1000;
      beacon.userData.rings.forEach((ring, i) => {
        const p = (now * 0.6 + i / 3) % 1;
        ring.scale.setScalar(1 + p * 4);
        ring.material.opacity = 0.8 * (1 - p) * Math.min(beacon.userData.foot.material.opacity * 1.5, 1);
      });
    }
    if (!still) {
      if (!busy) globe.rotateY(dt * 0.12);
      theta += dt * 0.32; // one orbit in about 20 s
      rocket.rotation.y += dt * 0.9;
      placeRocket();
      outer.scale.set(1, 0.85 + Math.random() * 0.3, 1);
      inner.scale.set(1, 0.9 + Math.random() * 0.2, 1);
      glow.material.opacity = 0.75 + Math.random() * 0.25;
    }
    if (!busy) controls?.update();
    renderer.render(scene, camera);
  });
  stage.classList.add("has-model");

  // Play it once by itself, after the opening animation, if the hero is still on screen.
  if (!still && button && !button.hidden) {
    const introPlayed = !document.documentElement.classList.contains("no-intro");
    const at = (introPlayed ? 4600 : 1600) - performance.now();
    setTimeout(() => { if (visible && window.scrollY < 200) zoomToGale(); }, Math.max(at, 900));
  }
}

start().catch((err) => console.warn("Mars model not loaded:", err));
