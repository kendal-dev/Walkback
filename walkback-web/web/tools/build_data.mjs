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
// Usage (from the repo root): node web/tools/build_data.mjs
// Run "git lfs pull" first so the .r16 is the real file and not an LFS pointer.

import { existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync, copyFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const WEB = join(dirname(fileURLToPath(import.meta.url)), "..");
const REPO = join(WEB, "..");
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
take(siteSrc, "site.json", { required: true });
take(join(SITE, "heightmap.r16"), "heightmap.bin", { required: true, bytes: HM * HM * 2 });
take(join(SITE, "stops.json"), "stops.json", { required: true });
take(join(SITE, "ortho.jpg"), "ortho.jpg", { maxMB: 5 });
take(join(SITE, "curiosity_traverse.json"), "curiosity_traverse.json"); // [{sol, x, y}], rover positions (session B)
take(join(MODEL, "route.json"), "route.json");
take(join(MODEL, "return_map.bin"), "return_map.bin", { bytes: GRID * GRID * 8 });

if (problems.length) {
  console.error("build_data FAILED:\n  - " + problems.join("\n  - "));
  process.exit(1);
}

writeFileSync(join(OUT, "status.json"), JSON.stringify({ test: false, builtAt: new Date().toISOString(), copied }, null, 2));
console.log("web/data updated from:");
for (const [k, v] of Object.entries(copied)) console.log(`  ${v} -> web/data/${k}`);
