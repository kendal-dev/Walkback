// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Copies what the web page needs from the Unity project into web/data/, in formats
// that are NOT tracked by Git LFS (GitHub Pages does not serve LFS files).
// It only READS the sources; it never writes into Assets/ or model-tests/.
//
//   Assets/StreamingAssets/Site/site.json      -> web/data/site.json
//   Assets/StreamingAssets/Site/heightmap.r16  -> web/data/heightmap.bin  (same bytes)
//   Assets/StreamingAssets/Site/stops.json     -> web/data/stops.json
//   Assets/StreamingAssets/Site/ortho.jpg      -> web/data/ortho.jpg      (optional)
//   model-tests/out/route.json                 -> web/data/route.json     (session C)
//   model-tests/out/return_map.bin             -> web/data/return_map.bin (session C)
//
// When model-tests/out/ does not exist, route.json and return_map.bin are computed from the
// app's plan B (tools/build_route.mjs), labelled ESTIMATE like in the headset.
//
// Usage (from the repo root): node walkback-web/web/tools/build_data.mjs
// Run "git lfs pull" first so the .r16 is the real file and not an LFS pointer.

import { existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync, copyFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { findRepo, planB } from "./build_route.mjs";

const WEB = join(dirname(fileURLToPath(import.meta.url)), "..");
const REPO = findRepo(); // the page lives in walkback-web/web/, the app's data at the repository root
const OUT = join(WEB, "data");
const SITE = join(REPO, "Assets", "StreamingAssets", "Site");
const MODEL = join(REPO, "model-tests", "out");

const HM = 513, GRID = 257;
const problems = [];
const copied = {};

function isLfsPointer(path) {
  const head = readFileSync(path).subarray(0, 64).toString("latin1");
  return head.startsWith("version https://git-lfs.github.com/spec/");
}

function take(src, dst, { required = false, bytes = null, maxMB = null } = {}) {
  const outPath = join(OUT, dst);
  if (!existsSync(src)) {
    if (required) problems.push(`missing ${relative(REPO, src)}`);
    else console.warn(`warn: missing ${relative(REPO, src)} (page will show it as pending)`);
    if (existsSync(outPath)) rmSync(outPath); // never leave test data next to real data
    return false;
  }
  if (isLfsPointer(src)) {
    problems.push(`${relative(REPO, src)} is a Git LFS pointer: run "git lfs pull"`);
    return false;
  }
  const size = statSync(src).size;
  if (bytes !== null && size !== bytes) {
    problems.push(`${relative(REPO, src)} is ${size} bytes, expected ${bytes}`);
    return false;
  }
  if (maxMB !== null && size > maxMB * 1024 * 1024) {
    console.warn(`warn: ${relative(REPO, src)} is ${(size / 1048576).toFixed(1)} MB (> ${maxMB} MB, slow on phones)`);
  }
  copyFileSync(src, outPath);
  copied[dst] = relative(REPO, src).replaceAll("\\", "/");
  return true;
}

mkdirSync(OUT, { recursive: true });

// site.json: check the contract before copying.
const siteSrc = join(SITE, "site.json");
if (existsSync(siteSrc)) {
  const s = JSON.parse(readFileSync(siteSrc, "utf8"));
  for (const k of ["name", "sideMeters", "heightmapSize", "gridSize", "minElevation", "maxElevation", "habitat", "sources"]) {
    if (s[k] === undefined) problems.push(`site.json has no "${k}"`);
  }
  if (s.heightmapSize !== HM) problems.push(`site.json heightmapSize is ${s.heightmapSize}, expected ${HM}`);
  if (s.gridSize !== GRID) problems.push(`site.json gridSize is ${s.gridSize}, expected ${GRID}`);
}
// The route and the return map come first, so a failure leaves web/data as it was:
// the team's model when it has written both files (session C), else the app's plan B
// over the relief (ESTIMATE), computed here from the same site package.
const modelRoute = join(MODEL, "route.json"), modelReturn = join(MODEL, "return_map.bin");
const hasRoute = existsSync(modelRoute), hasReturn = existsSync(modelReturn);
let model = "model-tests/out", plan = null;
if (hasRoute !== hasReturn) {
  problems.push(`model-tests/out has only ${hasRoute ? "route.json" : "return_map.bin"}: both files are needed`);
} else if (hasRoute) {
  if (statSync(modelReturn).size !== GRID * GRID * 8) problems.push(`model-tests/out/return_map.bin is not ${GRID * GRID * 8} bytes`);
} else {
  model = "plan B over the relief (ESTIMATE), tools/build_route.mjs";
  try {
    plan = planB(SITE);
  } catch (err) {
    problems.push(err.message);
  }
}
if (problems.length) {
  console.error("build_data FAILED (web/data not changed):\n  - " + problems.join("\n  - "));
  process.exit(1);
}

take(siteSrc, "site.json", { required: true });
take(join(SITE, "heightmap.r16"), "heightmap.bin", { required: true, bytes: HM * HM * 2 });
take(join(SITE, "stops.json"), "stops.json", { required: true });
// The photo: the page draws it on a canvas of 2048 px at most, so a 2048 px copy is enough
// (ffmpeg, about 1 MB); without ffmpeg the app's 4096 px file is copied as it is.
{
  const src = join(SITE, "ortho.jpg"), dst = join(OUT, "ortho.jpg");
  const run = existsSync(src) && !isLfsPointer(src)
    ? spawnSync("ffmpeg", ["-y", "-loglevel", "error", "-i", src, "-vf", "scale=2048:2048:flags=lanczos", "-q:v", "3", dst])
    : null;
  if (run && run.status === 0) copied["ortho.jpg"] = "Assets/StreamingAssets/Site/ortho.jpg (resized to 2048 px with ffmpeg)";
  else {
    if (run) console.warn("warn: ffmpeg not available, copying the full-size photo (slow on phones)");
    take(src, "ortho.jpg", { maxMB: 1.5 });
  }
}
take(join(SITE, "curiosity_traverse.json"), "curiosity_traverse.json"); // [{sol, x, y}], rover positions (session B)
if (plan) {
  writeFileSync(join(OUT, "route.json"), JSON.stringify(plan.route, null, 1));
  writeFileSync(join(OUT, "return_map.bin"), plan.returnMap);
  copied["route.json"] = copied["return_map.bin"] = model;
  console.log("plan B (ESTIMATE):", JSON.stringify(plan.numbers));
} else {
  take(modelRoute, "route.json", { required: true });
  take(modelReturn, "return_map.bin", { required: true, bytes: GRID * GRID * 8 });
}

if (problems.length) {
  console.error("build_data FAILED:\n  - " + problems.join("\n  - "));
  process.exit(1);
}

writeFileSync(join(OUT, "status.json"), JSON.stringify({ test: false, model, builtAt: new Date().toISOString(), copied }, null, 2));
console.log("web/data updated from:");
for (const [k, v] of Object.entries(copied)) console.log(`  ${v} -> web/data/${k}`);
