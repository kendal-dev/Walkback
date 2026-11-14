// Walkback pitch: the three 3D scenes (Gale terrain, Mars globe, astronaut).
// Each scene renders only while its slide is on screen. Data comes from data/*.js (no fetch: works from file://).
(function () {
  const W = 1920, H = 1080;
  const AMBER = 0xf5b301, VERMILION = 0xd55e00, SKY = 0x56b4e9;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const OFFSET = (parseFloat(new URLSearchParams(location.search).get('at')) || 0) * 1000; // ?at=10 jumps the animations (for screenshots)

  function makeRenderer(canvas, w, h) {
    const r = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    r.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
    r.setSize(w, h, false);
    r.outputEncoding = THREE.sRGBEncoding;
    return r;
  }
  function texFromDataUrl(url, onload) {
    const t = new THREE.TextureLoader().load(url, onload);
    t.encoding = THREE.sRGBEncoding;
    t.anisotropy = 8;
    return t;
  }
  function label(text, color) {
    const c = document.createElement('canvas'); c.width = 1024; c.height = 192;
    const x = c.getContext('2d');
    x.font = '800 92px Inter, sans-serif';
    const w = Math.min(1000, x.measureText(text).width + 70);
    x.fillStyle = 'rgba(13,15,20,0.88)'; x.fillRect((1024 - w) / 2, 30, w, 132);
    x.fillStyle = color || '#f3efe8'; x.textAlign = 'center'; x.textBaseline = 'middle';
    x.fillText(text, 512, 98);
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(c), depthTest: false, transparent: true }));
    s.renderOrder = 10;
    return s;
  }
  function pillar(color, height, radius) {
    const g = new THREE.CylinderGeometry(radius, radius, height, 24, 1, true);
    g.translate(0, height / 2, 0);
    const m = new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
      uniforms: { c: { value: new THREE.Color(color) }, t: { value: 0 } },
      vertexShader: 'varying float vy; void main(){ vy = position.y; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }',
      fragmentShader: `uniform vec3 c; uniform float t; varying float vy;
        void main(){ float f = 1.0 - clamp(vy/${height.toFixed(1)},0.,1.); float p = 0.75 + 0.25*sin(t*2.0);
        gl_FragColor = vec4(c, pow(f,1.6)*0.85*p); }`,
    });
    return new THREE.Mesh(g, m);
  }

  // ---------- Gale terrain (slide 2) ----------
  function galeScene(canvas) {
    const G = window.GALE; if (!G) return null;
    const n = G.grid, side = G.side, step = side / (n - 1);
    const bin = atob(G.heights), q = new Uint16Array(n * n);
    for (let i = 0; i < n * n; i++) q[i] = bin.charCodeAt(2 * i) | (bin.charCodeAt(2 * i + 1) << 8);
    const hgt = (i) => G.hMin + (q[i] / 65535) * (G.hMax - G.hMin) - G.hMin;
    const hAt = (x, y) => { // local meters -> height (bilinear)
      const fc = Math.max(0, Math.min(n - 1.001, x / step)), fr = Math.max(0, Math.min(n - 1.001, (side - y) / step));
      const c = Math.floor(fc), r = Math.floor(fr), dc = fc - c, dr = fr - r;
      const a = hgt(r * n + c), b = hgt(r * n + c + 1), d = hgt((r + 1) * n + c), e = hgt((r + 1) * n + c + 1);
      return (a * (1 - dc) + b * dc) * (1 - dr) + (d * (1 - dc) + e * dc) * dr;
    };
    const P = (x, y, lift) => new THREE.Vector3(x - side / 2, hAt(x, y) + (lift || 0), side / 2 - y);

    const pos = new Float32Array(n * n * 3), uv = new Float32Array(n * n * 2), slope = new Float32Array(n * n);
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) {
      const i = r * n + c;
      pos[3 * i] = c * step - side / 2; pos[3 * i + 1] = hgt(i); pos[3 * i + 2] = r * step - side / 2;
      uv[2 * i] = c / (n - 1); uv[2 * i + 1] = 1 - r / (n - 1);
      const cl = Math.max(0, c - 1), cr = Math.min(n - 1, c + 1), ru = Math.max(0, r - 1), rd = Math.min(n - 1, r + 1);
      const gx = (hgt(r * n + cr) - hgt(r * n + cl)) / ((cr - cl) * step);
      const gy = (hgt(rd * n + c) - hgt(ru * n + c)) / ((rd - ru) * step);
      slope[i] = Math.atan(Math.hypot(gx, gy)) * 180 / Math.PI;
    }
    const idx = [];
    for (let r = 0; r < n - 1; r++) for (let c = 0; c < n - 1; c++) {
      const a = r * n + c, b = a + 1, d = a + n, e = d + 1; idx.push(a, d, b, b, d, e);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setAttribute('slope', new THREE.BufferAttribute(slope, 1));
    geo.setIndex(idx); geo.computeVertexNormals();

    const mat = new THREE.ShaderMaterial({
      uniforms: { map: { value: texFromDataUrl(G.ortho) }, lim: { value: G.slopeLimit }, show: { value: 0 } },
      vertexShader: `attribute float slope; varying vec2 vUv; varying float vS; varying vec3 vN; varying vec3 vP;
        void main(){ vUv = uv; vS = slope; vN = normal; vP = position; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
      fragmentShader: `uniform sampler2D map; uniform float lim; uniform float show; varying vec2 vUv; varying float vS; varying vec3 vN; varying vec3 vP;
        void main(){
          vec3 base = texture2D(map, vUv).rgb;
          base = pow(base, vec3(2.2));
          float light = 0.55 + 0.6*max(dot(normalize(vN), normalize(vec3(-0.5,0.9,0.3))), 0.0);
          float g = dot(base, vec3(0.333));
          vec3 col = g*light*vec3(1.35,0.78,0.52); // HiRISE red band is greyscale: tinted to Mars colour
          float over = step(lim, vS) * show;
          float hatch = step(0.5, fract((vP.x + vP.z) / 9.0));
          col = mix(col, vec3(0.80,0.12,0.04), over*(0.30 + 0.40*hatch));
          gl_FragColor = vec4(pow(col, vec3(1.0/2.2)), 1.0);
        }`,
    });
    const scene = new THREE.Scene();
    scene.add(new THREE.Mesh(geo, mat));

    // Straight line and safe route (to Nevado Sajama)
    const hab = G.habitat, stop = G.stops.find((s) => s.id === 'nevado-sajama') || G.stops[0];
    const straight = [];
    for (let k = 0; k <= 60; k++) { const t = k / 60; straight.push(P(hab.x + (stop.x - hab.x) * t, hab.y + (stop.y - hab.y) * t, 3)); }
    const sLine = new THREE.Line(new THREE.BufferGeometry().setFromPoints(straight),
      new THREE.LineDashedMaterial({ color: 0xffffff, dashSize: 10, gapSize: 7, depthTest: false, transparent: true }));
    sLine.computeLineDistances(); sLine.renderOrder = 5; scene.add(sLine);

    const routePts = (G.routes[stop.id] || []).map(([x, y]) => P(x, y, 3));
    const curve = routePts.length > 1 ? new THREE.CatmullRomCurve3(routePts) : null;
    let tube = null, tubeSegs = 0;
    if (curve) {
      tubeSegs = 400;
      tube = new THREE.Mesh(new THREE.TubeGeometry(curve, tubeSegs, 3.2, 8, false),
        new THREE.MeshBasicMaterial({ color: AMBER, depthTest: false, transparent: true }));
      tube.renderOrder = 6; tube.geometry.setDrawRange(0, 0); scene.add(tube);
    }
    // Curiosity's real path, faint sky-blue dots
    const trav = G.traverse.map(([x, y]) => P(x, y, 2));
    const dots = new THREE.Points(new THREE.BufferGeometry().setFromPoints(trav),
      new THREE.PointsMaterial({ color: SKY, size: 7, sizeAttenuation: true, transparent: true, opacity: 0.0, depthTest: false }));
    dots.renderOrder = 4; scene.add(dots);

    // Pillars and labels
    const pils = [];
    function addPin(x, y, name, color) {
      const p = pillar(color, 140, 5); p.position.copy(P(x, y, 0)); scene.add(p); pils.push(p);
      const l = label(name, color === AMBER ? '#f5b301' : '#f3efe8'); l.position.copy(P(x, y, 165)); l.scale.set(220, 41, 1); scene.add(l);
    }
    addPin(hab.x, hab.y, 'HÁBITAT', 0x3ad07a);
    addPin(stop.x, stop.y, stop.name, AMBER);

    const camera = new THREE.PerspectiveCamera(38, canvas.width / canvas.height, 1, 6000);
    const renderer = makeRenderer(canvas, canvas.width, canvas.height);
    const target = new THREE.Vector3(0, 60, 0);
    let t0 = 0;
    return {
      start() { t0 = performance.now() - OFFSET; },
      frame(now) {
        const t = (now - t0) / 1000;
        const ang = (reduced ? 0.5 : 0.5 + t * 0.05);
        const rad = 1050 - Math.min(t, 6) * 35;
        camera.position.set(Math.sin(ang) * rad, 560, Math.cos(ang) * rad);
        camera.lookAt(target);
        mat.uniforms.show.value = Math.min(1, Math.max(0, (t - 1.0) / 1.2));
        if (tube) { const k = Math.min(1, Math.max(0, (t - 2.4) / 3)); tube.geometry.setDrawRange(0, Math.floor(k * tubeSegs) * 8 * 6); }
        dots.material.opacity = Math.min(0.9, Math.max(0, (t - 6) / 1.5));
        pils.forEach((p) => (p.material.uniforms.t.value = t));
        renderer.render(scene, camera);
      },
    };
  }

  // ---------- Mars globe (slide 6) ----------
  function globeScene(canvas) {
    if (!window.GLOBE_TEX) return null;
    const scene = new THREE.Scene();
    const globe = new THREE.Group(); scene.add(globe);
    const mars = new THREE.Mesh(new THREE.SphereGeometry(1, 96, 64), new THREE.MeshStandardMaterial({ map: texFromDataUrl(window.GLOBE_TEX), roughness: 1, metalness: 0 }));
    globe.add(mars);
    // Atmosphere rim
    globe.add(new THREE.Mesh(new THREE.SphereGeometry(1.035, 64, 48), new THREE.ShaderMaterial({
      transparent: true, side: THREE.BackSide, blending: THREE.AdditiveBlending, depthWrite: false,
      vertexShader: 'varying vec3 n; void main(){ n = normalize(normalMatrix*normal); gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }',
      fragmentShader: 'varying vec3 n; void main(){ float i = pow(0.75 - dot(n, vec3(0,0,1.)), 3.0); gl_FragColor = vec4(1.0,0.55,0.3,1.0)*i; }',
    })));
    // Gale marker: lon 137.381 E, lat -4.807 (texture left edge 180 W)
    const lat = -4.807 * Math.PI / 180, phi = (137.381 + 180) / 360 * Math.PI * 2;
    const dir = new THREE.Vector3(-Math.cos(phi) * Math.cos(lat), Math.sin(lat), Math.sin(phi) * Math.cos(lat));
    const mark = new THREE.Mesh(new THREE.SphereGeometry(0.018, 16, 12), new THREE.MeshBasicMaterial({ color: AMBER }));
    mark.position.copy(dir.clone().multiplyScalar(1.005)); globe.add(mark);
    const ring = new THREE.Mesh(new THREE.RingGeometry(0.03, 0.04, 48), new THREE.MeshBasicMaterial({ color: AMBER, transparent: true, side: THREE.DoubleSide }));
    ring.position.copy(dir.clone().multiplyScalar(1.006)); ring.lookAt(dir.clone().multiplyScalar(2)); globe.add(ring);
    const beam = pillar(AMBER, 0.35, 0.006); beam.position.copy(dir); beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir); globe.add(beam);

    scene.add(new THREE.AmbientLight(0xffffff, 0.35));
    const sun = new THREE.DirectionalLight(0xffffff, 1.25); sun.position.set(-3, 1.2, 4); scene.add(sun);
    const camera = new THREE.PerspectiveCamera(30, canvas.width / canvas.height, 0.1, 50); camera.position.set(0, 0, 4.2);
    const renderer = makeRenderer(canvas, canvas.width, canvas.height);
    // Turn so that Gale ends facing the camera
    const faceY = Math.atan2(dir.x, dir.z);
    let t0 = 0;
    return {
      start() { t0 = performance.now() - OFFSET; },
      frame(now) {
        const t = (now - t0) / 1000;
        const spin = reduced ? 0 : Math.max(0, 1 - t / 7);
        globe.rotation.y = -faceY - spin * 2.6 + (reduced ? 0 : Math.sin(t * 0.25) * 0.08 * (1 - spin));
        globe.rotation.x = 0.08;
        const pulse = (t % 2) / 2; ring.scale.setScalar(1 + pulse * 2.2); ring.material.opacity = 1 - pulse;
        beam.material.uniforms.t.value = t;
        renderer.render(scene, camera);
      },
    };
  }

  // ---------- Astronaut (slide 7) ----------
  function astronautScene(canvas) {
    if (!window.ASTRONAUT_GLB || !THREE.GLTFLoader) return null;
    const scene = new THREE.Scene();
    if (window.SAJAMA_TEX) scene.background = texFromDataUrl(window.SAJAMA_TEX);
    scene.add(new THREE.HemisphereLight(0xffe2c4, 0x40281a, 0.9));
    const key = new THREE.DirectionalLight(0xfff0dd, 1.4); key.position.set(-3, 5, 4); scene.add(key);
    const rim = new THREE.DirectionalLight(0xf5b301, 1.1); rim.position.set(4, 3, -5); scene.add(rim);
    const holder = new THREE.Group(); scene.add(holder);
    const bin = atob(window.ASTRONAUT_GLB), buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
    const gl = new THREE.GLTFLoader();
    if (THREE.DRACOLoader && window.DRACO_DECODER_JS) {
      const draco = new THREE.DRACOLoader();
      draco.setDecoderConfig({ type: 'js' });
      draco._loadLibrary = () => Promise.resolve(window.DRACO_DECODER_JS); // no fetch: works from file://
      gl.setDRACOLoader(draco);
    }
    gl.parse(buf.buffer, '', (gltf) => {
      const m = gltf.scene;
      const box = new THREE.Box3().setFromObject(m), size = box.getSize(new THREE.Vector3());
      const s = 1.8 / size.y; m.scale.setScalar(s);
      const b2 = new THREE.Box3().setFromObject(m), c = b2.getCenter(new THREE.Vector3());
      m.position.set(-c.x, -b2.min.y, -c.z);
      holder.add(m);
    }, (err) => { console.error(err); canvas.closest('.scene-wrap')?.classList.add('fallback'); });
    // The light pillar he looks at, far away on the right
    const far = pillar(AMBER, 34, 0.55); far.position.set(-9, -1.5, -55); scene.add(far);
    holder.position.set(2.3, 0, 0);
    const ground = new THREE.Mesh(new THREE.CircleGeometry(1.2, 48), new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.3 }));
    ground.rotation.x = -Math.PI / 2; ground.position.set(2.3, 0.01, 0); scene.add(ground);

    const camera = new THREE.PerspectiveCamera(32, canvas.width / canvas.height, 0.1, 200);
    const renderer = makeRenderer(canvas, canvas.width, canvas.height);
    let t0 = 0;
    return {
      start() { t0 = performance.now() - OFFSET; },
      frame(now) {
        const t = (now - t0) / 1000;
        const k = reduced ? 1 : Math.min(t, 12) / 12;
        camera.position.set(0.9 - k * 0.6, 2.0, 9.4 - k * 1.2); // slow push-in from behind
        camera.lookAt(0.2, 0.9, -10);
        holder.rotation.y = Math.PI - 0.28; // back to us, looking at the pillar
        far.material.uniforms.t.value = t;
        renderer.render(scene, camera);
      },
    };
  }

  // ---------- wiring ----------
  const makers = { gale: galeScene, globe: globeScene, astronaut: astronautScene };
  const scenes = {};
  document.querySelectorAll('canvas[data-scene]').forEach((cv) => {
    cv.width = parseInt(cv.dataset.w, 10); cv.height = parseInt(cv.dataset.h, 10);
    try {
      const s = makers[cv.dataset.scene](cv);
      if (s) scenes[cv.dataset.scene] = { s, slide: cv.closest('.slide') };
      else cv.closest('.scene-wrap')?.classList.add('fallback');
    } catch (e) { console.error(e); cv.closest('.scene-wrap')?.classList.add('fallback'); }
  });
  let active = null;
  window.addEventListener('slidechange', (e) => {
    active = Object.values(scenes).find((x) => x.slide === e.detail.slide) || null;
    if (active) active.s.start();
  });
  (function loop(now) { if (active) active.s.frame(now); requestAnimationFrame(loop); })(performance.now());
})();
