// Written with Claude Code (AI), directed and tested by team MARSXR
//
// Writes the route and the return map the page draws, from the app's plan B over the relief
// (tools/walkback-model.cjs: a JavaScript port of ReturnFallback.cs, MissionFallback.cs and
// HudEstimate.cs). Every number that comes out of here is an ESTIMATE, exactly as in the app:
// the team's walking and oxygen model is not in yet.
//
//   Assets/StreamingAssets/Site/site.json, heightmap.r16, stops.json
//     -> route.json       { verdict, o2CapacityLiters, o2ReserveLiters, points: [{x, y, minutes, o2Percent, stopId?}] ... }
//     -> return_map.bin   257 x 257 pairs of float32 LE (minutes, litres), row 0 north; NaN = no way back
//
// It refuses to write when its numbers are not the ones the app's own test reports on Gale
// (1812 m, 43 min walking, 2 h 12 min of work, 181 L used, 285 L spare, 57.5 % red at 8 %).
// Called by build_data.mjs; alone: node walkback-web/web/tools/build_route.mjs

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const M = createRequire(import.meta.url)("./walkback-model.cjs");

/** The repository root: the first folder above this one that holds the app's site package. */
export function findRepo(from = HERE) {
  let dir = from;
  for (let i = 0; i < 8; i++) {
    if (existsSync(join(dir, "Assets", "StreamingAssets", "Site", "site.json"))) return dir;
    const up = dirname(dir);
    if (up === dir) break;
    dir = up;
  }
  throw new Error("Assets/StreamingAssets/Site/site.json not found above " + from);
}

const round = (v, d) => Number(v.toFixed(d));

/** Returns { route, returnMap: Buffer, numbers } from the site package in siteDir. Throws when a check fails. */
export function planB(siteDir) {
  const s = JSON.parse(readFileSync(join(siteDir, "site.json"), "utf8"));
  const stops = JSON.parse(readFileSync(join(siteDir, "stops.json"), "utf8"));
  const raw = readFileSync(join(siteDir, "heightmap.r16"));
  const HM = s.heightmapSize, n = s.gridSize;
  if (raw.length !== HM * HM * 2) throw new Error(`heightmap.r16 is ${raw.length} bytes, expected ${HM * HM * 2} (Git LFS pointer? run "git lfs pull")`);
  if ((HM - 1) % (n - 1) !== 0) throw new Error(`grid ${n} does not divide heightmap ${HM}`);

  // The model's grid: every (HM - 1) / (n - 1)-th sample of the heightmap, row 0 = north.
  const k = (HM - 1) / (n - 1);
  const heights = new Float32Array(n * n);
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      const q = raw.readUInt16LE((r * k * HM + c * k) * 2);
      heights[r * n + c] = s.minElevation + (q / 65535) * (s.maxElevation - s.minElevation);
    }
  }
  const site = { n, side: s.sideMeters, spacing: s.sideMeters / (n - 1), heights, habitat: s.habitat, stops };
  const slope = M.slopeDegrees(site);
  const P = M.plan(site, slope, stops, 100);
  const back = P.home;
  const w8 = M.whatIf(back, 8), w15 = M.whatIf(back, 15);

  // The same numbers the app's automatic test reports (AutoTest.Mission.cs, "Demo," notes).
  const mins = (m) => { m = Math.round(m); return m < 60 ? `${m} min` : `${Math.floor(m / 60)} h ${m % 60} min`; };
  const numbers = {
    walkMeters: Math.round(P.walkMeters), walk: mins(P.walkMinutes), work: mins(P.workMinutes), total: mins(P.totalMinutes),
    usedLiters: Math.round(P.usedLiters), usedPercent: Math.round(P.usedPercent), spareLiters: Math.round(P.spareLiters),
    viable: P.viable, redAt15: round(w15.redPercent, 1), redAt8: round(w8.redPercent, 1),
  };
  const want = { walkMeters: 1812, walk: "43 min", work: "2 h 12 min", total: "2 h 55 min", usedLiters: 181, usedPercent: 36, spareLiters: 285, viable: true, redAt15: 0 };
  const off = Object.keys(want).filter((key) => numbers[key] !== want[key]).map((key) => `${key}: ${numbers[key]} (the app: ${want[key]})`);
  if (Math.abs(numbers.redAt8 - 57.5) > 2) off.push(`redAt8: ${numbers.redAt8} (the app: 57.5)`);
  if (off.length) throw new Error("plan B numbers differ from the app's:\n    " + off.join("\n    "));

  // Route points with the clock and the tank at each one. Inside a leg the minutes are spread by distance.
  const points = [];
  let minutes = 0, used = 0;
  const push = (p, stopId) => {
    const o = { x: round(p[0], 1), y: round(p[1], 1), minutes: round(minutes, 2), o2Percent: round((M.TANK_LITERS - used) / M.TANK_LITERS * 100, 2) };
    if (stopId) o.stopId = stopId;
    points.push(o);
  };
  push(P.path[0]);
  for (const leg of P.legs) {
    let legMeters = 0;
    for (let i = leg.firstPoint + 1; i <= leg.lastPoint; i++) legMeters += Math.hypot(P.path[i][0] - P.path[i - 1][0], P.path[i][1] - P.path[i - 1][1]);
    const m0 = minutes, u0 = used;
    let walked = 0;
    for (let i = leg.firstPoint + 1; i <= leg.lastPoint; i++) {
      walked += Math.hypot(P.path[i][0] - P.path[i - 1][0], P.path[i][1] - P.path[i - 1][1]);
      const share = legMeters > 0 ? walked / legMeters : 1;
      minutes = m0 + leg.minutes * share;
      used = u0 + leg.minutes * share * M.WALK_LITERS_PER_MINUTE;
      push(P.path[i], i === leg.lastPoint && leg.toStop >= 0 ? stops[leg.toStop].id : null);
    }
    // The work at the stop: the next point leaves with the clock and the tank after it.
    minutes += leg.workMinutesAtEnd;
    used += leg.workMinutesAtEnd * M.DESIGN_LITERS_PER_MINUTE;
  }

  const route = {
    note: "ESTIMATE: plan B over the relief, not the team's walking model yet. Same numbers as the headset app.",
    estimate: true,
    source: "walkback-web/web/tools/build_route.mjs (port of the app's ReturnFallback, MissionFallback and HudEstimate)",
    verdict: "VIABLE",
    o2CapacityLiters: M.TANK_LITERS,
    o2ReserveLiters: M.RESERVE_LITERS,
    walkMeters: round(P.walkMeters, 0),
    walkMinutes: round(P.walkMinutes, 1),
    workMinutes: round(P.workMinutes, 1),
    totalMinutes: round(P.totalMinutes, 1),
    usedLiters: round(P.usedLiters, 1),
    spareLiters: round(P.spareLiters, 1),
    pointOfNoReturn: null,
    points,
  };

  const returnMap = Buffer.alloc(n * n * 8);
  for (let i = 0; i < n * n; i++) {
    const m = back[i], ok = Number.isFinite(m);
    returnMap.writeFloatLE(ok ? m : NaN, i * 8);
    returnMap.writeFloatLE(ok ? m * M.WALK_LITERS_PER_MINUTE : NaN, i * 8 + 4);
  }
  return { route, returnMap, numbers };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const repo = findRepo();
  const out = join(HERE, "..", "data");
  mkdirSync(out, { recursive: true });
  const { route, returnMap, numbers } = planB(join(repo, "Assets", "StreamingAssets", "Site"));
  writeFileSync(join(out, "route.json"), JSON.stringify(route, null, 1));
  writeFileSync(join(out, "return_map.bin"), returnMap);
  console.log("plan B (ESTIMATE):", JSON.stringify(numbers));
}
