// Written with Claude Code (AI), directed and tested by team MARSXR
// Walkback pitch: the app's plan B, ported to JavaScript so the slides draw what the app draws.
// Port of Assets/Walkback/App/ReturnFallback.cs (minutes back to the habitat over the relief),
// TerrainLayers.SlopeDegrees, MissionFallback.ReachMinutes and the constants of HudEstimate.cs,
// as they are on main at eb8f7f8. Every number that comes out of here is an ESTIMATE (plan B),
// exactly as in the app. No DOM, no three.js: it runs in the browser and in Node (see check-model.js).
(function (root) {
  'use strict';

  // HudEstimate.cs / ReturnStandIn.cs / TerrainLayers.cs
  var TANK_LITERS = 497;
  var RESERVE_LITERS = 31;
  var FLAT_LITERS_PER_KM = 20;
  var FLAT_MINUTES_PER_KM = 19.3;
  var WALK_LITERS_PER_MINUTE = FLAT_LITERS_PER_KM / FLAT_MINUTES_PER_KM;
  var DESIGN_LITERS_PER_MINUTE = TANK_LITERS / 480;
  var SLOPE_LIMIT_DEGREES = 20;
  var TOBLER_K = 3.5, TOBLER_OFFSET = 0.05;
  var f = Math.fround; // the app computes in 32-bit floats

  /** window.GALE -> { n, side, spacing, heights: Float32Array (meters, row 0 = north), habitat, stops }. */
  function siteFromGale(G) {
    var n = G.grid, bin = typeof atob === 'function' ? atob(G.heights) : Buffer.from(G.heights, 'base64').toString('binary');
    if (bin.length !== n * n * 2) throw new Error('gale.js: heights has ' + bin.length + ' bytes, expected ' + n * n * 2);
    var heights = new Float32Array(n * n);
    for (var i = 0; i < n * n; i++) {
      var q = bin.charCodeAt(2 * i) | (bin.charCodeAt(2 * i + 1) << 8);
      heights[i] = G.hMin + (q / 65535) * (G.hMax - G.hMin);
    }
    return { n: n, side: G.side, spacing: G.side / (n - 1), heights: heights, habitat: G.habitat, stops: G.stops || [] };
  }

  /** TerrainLayers.SlopeDegrees: central differences, one-sided at the edges. */
  function slopeDegrees(site) {
    var n = site.n, sp = site.spacing, h = site.heights, slope = new Float32Array(n * n);
    for (var r = 0; r < n; r++) {
      var r0 = Math.max(r - 1, 0), r1 = Math.min(r + 1, n - 1);
      for (var c = 0; c < n; c++) {
        var c0 = Math.max(c - 1, 0), c1 = Math.min(c + 1, n - 1);
        var dzdx = (h[r * n + c1] - h[r * n + c0]) / ((c1 - c0) * sp);
        var dzdy = (h[r0 * n + c] - h[r1 * n + c]) / ((r1 - r0) * sp);
        slope[r * n + c] = Math.atan(Math.sqrt(dzdx * dzdx + dzdy * dzdy)) * 180 / Math.PI;
      }
    }
    return slope;
  }

  /** ReturnFallback.SlopeFactor: Tobler's hiking function normalised to 1 on flat ground. */
  function slopeFactor(s) {
    return Math.exp(-TOBLER_K * Math.abs(s + TOBLER_OFFSET) + TOBLER_K * TOBLER_OFFSET);
  }

  /** ReturnFallback.NodeOf: the node nearest to a point in local meters (x east, y north). */
  function nodeOf(site, x, y) {
    var n = site.n;
    var column = Math.min(n - 1, Math.max(0, Math.round(x / site.spacing)));
    var row = Math.min(n - 1, Math.max(0, Math.round((site.side - y) / site.spacing)));
    return row * n + column;
  }

  /**
   * ReturnFallback.MinutesTo: minutes to walk to (tx, ty) from every node. Dijkstra, 8 neighbours,
   * no node over 20 degrees, no step over 20 degrees. Infinity = no way on foot.
   * next (optional Int32Array n*n) receives, for each node, the node to step onto next (-1 = none).
   */
  function minutesTo(site, slope, tx, ty, next) {
    var n = site.n, sp = site.spacing, h = site.heights;
    var maxGrade = Math.tan(SLOPE_LIMIT_DEGREES * Math.PI / 180);
    var pace = FLAT_MINUTES_PER_KM / 1000;
    var minutes = new Float32Array(n * n).fill(Infinity);
    if (next) next.fill(-1);

    var start = nodeOf(site, tx, ty), sr = Math.floor(start / n), sc = start - sr * n;
    minutes[start] = Math.hypot(sc * sp - tx, (site.side - sr * sp) - ty) * pace;

    // Binary min-heap of (node, minutes); stale entries are skipped on pop.
    var cap = n * n, hn = new Int32Array(cap), hk = new Float32Array(cap), count = 0;
    function push(node, key) {
      if (count === cap) { cap *= 2; var a = new Int32Array(cap), b = new Float32Array(cap); a.set(hn); b.set(hk); hn = a; hk = b; }
      var i = count++;
      while (i > 0) { var p = (i - 1) >> 1; if (hk[p] <= key) break; hn[i] = hn[p]; hk[i] = hk[p]; i = p; }
      hn[i] = node; hk[i] = key;
    }
    push(start, minutes[start]);
    var diagonal = sp * Math.SQRT2;

    while (count > 0) {
      var u = hn[0], t = hk[0];
      count--;
      if (count > 0) {
        var ln = hn[count], lk = hk[count], i = 0;
        for (;;) {
          var ch = 2 * i + 1;
          if (ch >= count) break;
          if (ch + 1 < count && hk[ch + 1] < hk[ch]) ch++;
          if (hk[ch] >= lk) break;
          hn[i] = hn[ch]; hk[i] = hk[ch]; i = ch;
        }
        hn[i] = ln; hk[i] = lk;
      }
      if (t > minutes[u]) continue;
      var ur = Math.floor(u / n), uc = u - ur * n;
      for (var dr = -1; dr <= 1; dr++) {
        var vr = ur + dr;
        if (vr < 0 || vr >= n) continue;
        for (var dc = -1; dc <= 1; dc++) {
          if (dr === 0 && dc === 0) continue;
          var vc = uc + dc;
          if (vc < 0 || vc >= n) continue;
          var v = vr * n + vc;
          if (slope[v] > SLOPE_LIMIT_DEGREES) continue;            // no-go ground
          var run = dr !== 0 && dc !== 0 ? diagonal : sp;
          var grade = (h[u] - h[v]) / run;                        // the walk steps from v onto u
          if (Math.abs(grade) > maxGrade) continue;
          var arrival = f(t + run * pace / slopeFactor(grade));
          if (arrival < minutes[v]) { minutes[v] = arrival; if (next) next[v] = u; push(v, arrival); }
        }
      }
    }
    return minutes;
  }

  /** ReturnFallback.Minutes: minutes back to the habitat from every node. */
  function returnMinutes(site, slope, next) {
    return minutesTo(site, slope, site.habitat.x, site.habitat.y, next);
  }

  /** MissionFallback.ReachMinutes: minutes of walking the oxygen above the reserve covers with this much of a tank. */
  function reachMinutes(tankPercent) {
    return Math.max(0, (TANK_LITERS * tankPercent / 100 - RESERVE_LITERS) / WALK_LITERS_PER_MINUTE);
  }

  /**
   * What the test's "Demo, WHAT IF" notes count (AutoTest.Mission.cs): of the nodes that have a way back,
   * how many are farther than the oxygen covers. redPercent is over the REACHABLE nodes, like the app's note.
   */
  function whatIf(minutes, tankPercent) {
    var reach = reachMinutes(tankPercent), reachable = 0, beyond = 0, farthest = 0;
    for (var i = 0; i < minutes.length; i++) {
      var m = minutes[i];
      if (!isFinite(m)) continue;
      reachable++;
      if (m > farthest) farthest = m;
      if (m > reach) beyond++;
    }
    return { tankPercent: tankPercent, reachMinutes: reach, farthestMinutes: farthest, reachable: reachable,
      noWayBack: minutes.length - reachable, beyond: beyond, redPercent: reachable ? beyond / reachable * 100 : 0 };
  }

  /** The way from a point to the target of a minutesTo() call, as [x, y] local meters (follows next). */
  function pathFrom(site, next, x, y) {
    var n = site.n, node = nodeOf(site, x, y), pts = [[x, y]], guard = n * n;
    while (node >= 0 && guard-- > 0) {
      var r = Math.floor(node / n), c = node - r * n;
      pts.push([c * site.spacing, site.side - r * site.spacing]);
      node = next[node];
    }
    return pts;
  }

  // ------------------------------------------------------------------ MissionFallback.cs
  var SMOOTH_WINDOW = 30, SMOOTH_PASSES = 3;

  /** MissionFallback.Walkable: the four nodes around a point are all within the slope limit. */
  function walkable(site, slope, x, y) {
    var n = site.n, sp = site.spacing;
    var c0 = Math.min(Math.floor(Math.min(n - 1, Math.max(0, x / sp))), n - 2);
    var r0 = Math.min(Math.floor(Math.min(n - 1, Math.max(0, (site.side - y) / sp))), n - 2);
    var L = SLOPE_LIMIT_DEGREES;
    return slope[r0 * n + c0] <= L && slope[r0 * n + c0 + 1] <= L && slope[(r0 + 1) * n + c0] <= L && slope[(r0 + 1) * n + c0 + 1] <= L;
  }

  /** RouteGeometry.HeightAt (without subtracting the lowest point: only differences are used). */
  function heightAt(site, x, y) {
    var n = site.n, sp = site.spacing, h = site.heights;
    var gc = Math.min(n - 1, Math.max(0, x / sp)), gr = Math.min(n - 1, Math.max(0, (site.side - y) / sp));
    var c0 = Math.min(Math.floor(gc), n - 2), r0 = Math.min(Math.floor(gr), n - 2), fc = gc - c0, fr = gr - r0;
    var top = h[r0 * n + c0] + (h[r0 * n + c0 + 1] - h[r0 * n + c0]) * fc;
    var bottom = h[(r0 + 1) * n + c0] + (h[(r0 + 1) * n + c0 + 1] - h[(r0 + 1) * n + c0]) * fc;
    return top + (bottom - top) * fr;
  }

  /** MissionFallback.StraightMinutes: minutes along the straight line a -> b, or -1 when it is not walkable. */
  function straightMinutes(site, slope, a, b) {
    var meters = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (meters < 1e-6) return 0;
    if (!walkable(site, slope, a[0], a[1])) return -1;
    var maxGrade = Math.tan(SLOPE_LIMIT_DEGREES * Math.PI / 180), pace = FLAT_MINUTES_PER_KM / 1000;
    var steps = Math.max(1, Math.ceil(meters / (site.spacing / 4))), step = meters / steps;
    var previous = heightAt(site, a[0], a[1]), total = 0;
    for (var k = 1; k <= steps; k++) {
      var t = k / steps, x = a[0] + (b[0] - a[0]) * t, y = a[1] + (b[1] - a[1]) * t;
      if (!walkable(site, slope, x, y)) return -1;
      var height = heightAt(site, x, y), grade = (height - previous) / step;
      if (Math.abs(grade) > maxGrade) return -1;
      total += step * pace / slopeFactor(grade);
      previous = height;
    }
    return total;
  }

  /** MissionFallback.Smooth: straight lines where the grid's zigzag is not needed. Changes points and left in place. */
  function smooth(site, slope, points, left) {
    var shortcuts = 0;
    for (var pass = 0; pass < SMOOTH_PASSES; pass++) {
      var last = points.length - 1, kept = [points[0]], cost = [0], found = false, i = 0;
      while (i < last) {
        var best = i + 1, bestMinutes = Math.max(0, left[i] - left[i + 1]);
        for (var j = Math.min(last, i + SMOOTH_WINDOW); j > i + 1; j--) {
          var straight = straightMinutes(site, slope, points[i], points[j]);
          if (straight < 0) continue;
          if (straight > left[i] - left[j] + 1e-4) continue;
          best = j; bestMinutes = straight; shortcuts++; found = true;
          break;
        }
        kept.push(points[best]); cost.push(bestMinutes); i = best;
      }
      var total = 0, q;
      for (q = 1; q < cost.length; q++) total += cost[q];
      points.length = 0; left.length = 0;
      var walked = 0;
      for (q = 0; q < kept.length; q++) { walked += cost[q]; points.push(kept[q]); left.push(Math.max(0, total - walked)); }
      if (!found) break;
    }
    return shortcuts;
  }

  /**
   * MissionFallback.Make: habitat -> stops in order -> habitat over the relief, with the verdict.
   * stops: [{ name, x, y, tasks: [{ minutes }] }] (stops.json). tankPercent: 100, or less for a WHAT IF.
   * Returns { hasRoute, viable, path: [[x, y]...], legs, walkMeters, walkMinutes, workMinutes, totalMinutes,
   *           tankPercent, tankLiters, usedLiters, usedPercent, spareLiters, failsAtStop, failsOnArrival, failsAtName, home }.
   */
  function plan(site, slope, stops, tankPercent) {
    var n = site.n, hab = [site.habitat.x, site.habitat.y];
    var P = { hasRoute: true, brokenLeg: '', path: [hab], legs: [], walkMeters: 0, walkMinutes: 0, gridMeters: 0, gridMinutes: 0,
      shortcuts: 0, workMinutes: 0, tankPercent: tankPercent, tankLiters: TANK_LITERS * tankPercent / 100, usedLiters: 0, spareLiters: 0,
      failsAtStop: -1, failsOnArrival: false, failsAtName: '' };
    var homeNext = new Int32Array(n * n), home = minutesTo(site, slope, hab[0], hab[1], homeNext);
    P.home = home;
    var from = hab, fromName = 'Habitat', used = 0, next = new Int32Array(n * n);
    function taskMinutes(s) { var t = 0; (s.tasks || []).forEach(function (k) { t += k.minutes; }); return t; }

    for (var leg = 0; leg <= stops.length; leg++) {
      var lastLeg = leg === stops.length;
      var to = lastLeg ? hab : [stops[leg].x, stops[leg].y], toName = lastLeg ? 'Habitat' : stops[leg].name;
      var minutes = lastLeg ? home : minutesTo(site, slope, to[0], to[1], next), way = lastLeg ? homeNext : next;
      var node = nodeOf(site, from[0], from[1]), legMinutes = minutes[node];
      if (!isFinite(legMinutes)) { P.hasRoute = false; P.brokenLeg = fromName + ' -> ' + toName; break; }

      var legFirstPoint = P.path.length - 1, legStartMeters = P.walkMeters;
      var points = [from], left = [legMinutes], guard = n * n;
      while (node >= 0 && guard-- > 0) {
        var r = Math.floor(node / n), c = node - r * n;
        points.push([c * site.spacing, site.side - r * site.spacing]);
        left.push(minutes[node]);
        node = way[node];
      }
      points.push(to); left.push(0);
      for (var g = 1; g < points.length; g++) P.gridMeters += Math.hypot(points[g][0] - points[g - 1][0], points[g][1] - points[g - 1][1]);
      P.gridMinutes += legMinutes;

      P.shortcuts += smooth(site, slope, points, left);
      legMinutes = left[0];

      var gridStep = site.spacing * Math.SQRT2 + 0.001;
      for (var i = 1; i < points.length; i++) {
        var a = points[i - 1], b = points[i], meters = Math.hypot(b[0] - a[0], b[1] - a[1]);
        if (meters > gridStep) {
          var steps = Math.max(1, Math.ceil(meters / (site.spacing / 2)));
          for (var k = 2; k < steps; k += 2) P.path.push([a[0] + (b[0] - a[0]) * k / steps, a[1] + (b[1] - a[1]) * k / steps]);
        }
        P.path.push(b);
        P.walkMeters += meters;
      }
      P.walkMinutes += legMinutes;
      used += legMinutes * WALK_LITERS_PER_MINUTE;
      P.legs.push({ fromName: fromName, toName: toName, toStop: lastLeg ? -1 : leg, firstPoint: legFirstPoint, lastPoint: P.path.length - 1,
        meters: P.walkMeters - legStartMeters, minutes: legMinutes, workMinutesAtEnd: lastLeg ? 0 : taskMinutes(stops[leg]) });

      if (!lastLeg) {
        var backMinutes = home[nodeOf(site, to[0], to[1])];
        if (!isFinite(backMinutes)) { P.hasRoute = false; P.brokenLeg = toName + ' -> Habitat'; break; }
        var backLiters = backMinutes * WALK_LITERS_PER_MINUTE;
        if (P.failsAtStop < 0 && P.tankLiters - used - RESERVE_LITERS < backLiters) { P.failsAtStop = leg; P.failsOnArrival = true; P.failsAtName = toName; }
        var work = taskMinutes(stops[leg]);
        P.workMinutes += work;
        used += work * DESIGN_LITERS_PER_MINUTE;
        if (P.failsAtStop < 0 && P.tankLiters - used - RESERVE_LITERS < backLiters) { P.failsAtStop = leg; P.failsOnArrival = false; P.failsAtName = toName; }
      }
      from = to; fromName = toName;
    }
    P.usedLiters = used;
    P.usedPercent = used / TANK_LITERS * 100;
    P.spareLiters = P.tankLiters - used - RESERVE_LITERS;
    P.totalMinutes = P.walkMinutes + P.workMinutes;
    P.viable = P.hasRoute && P.failsAtStop < 0 && P.spareLiters >= 0;
    return P;
  }

  /** MissionFallback.Describe: the verdict card's headline and numbers line, in the app's own words. */
  function describe(P) {
    function mins(m) { m = Math.round(m); return m < 60 ? m + ' min' : Math.floor(m / 60) + ' h ' + (m % 60) + ' min'; }
    var note = P.tankPercent < 100 ? 'WHAT IF: tank at ' + Math.round(P.tankPercent) + ' %. ESTIMATE, not the walking model yet' : 'ESTIMATE: plan B over the relief, not the walking model yet';
    if (!P.hasRoute) return { headline: 'NO SAFE ROUTE', summary: 'No way under 20°: ' + P.brokenLeg, note: note };
    if (P.viable) return { headline: 'VIABLE', summary: mins(P.totalMinutes) + '  ·  O2 used ' + Math.round(P.usedPercent) + ' %  ·  spare ' + Math.round(P.spareLiters) + ' L', note: note };
    return { headline: 'NOT VIABLE', note: note, summary: P.failsAtStop >= 0
      ? (P.failsOnArrival ? 'No oxygen to walk back from ' + P.failsAtName : 'No oxygen to walk back after the work at ' + P.failsAtName)
      : 'The plan ends ' + Math.round(-P.spareLiters) + ' L into the reserve' };
  }

  /**
   * HudEstimate.AtStop(site, index, returnMinutes, plan): what the visor shows on arrival at a stop of the plan
   * (work done at the earlier stops only). stateName: 'covered' (green), 'tight' (amber), 'notCovered' (red), as HudEstimate.StateOf.
   */
  function visorAtStop(site, P, stopIndex) {
    var walk = 0, work = 0;
    for (var i = 0; i <= stopIndex; i++) { walk += P.legs[i].minutes; if (i < stopIndex) work += P.legs[i].workMinutesAtEnd; }
    var used = walk * WALK_LITERS_PER_MINUTE + work * DESIGN_LITERS_PER_MINUTE;
    var stop = site.stops[stopIndex], backMinutes = P.home[nodeOf(site, stop.x, stop.y)], backLiters = backMinutes * WALK_LITERS_PER_MINUTE;
    var above = P.tankLiters - used - RESERVE_LITERS, next = P.legs[stopIndex + 1];
    return { stopName: stop.name, elapsedMinutes: walk + work, oxygenLeftPercent: Math.max(0, (P.tankLiters - used) / TANK_LITERS * 100),
      returnMinutes: backMinutes, returnLiters: backLiters, turnBack: above < backLiters,
      marginMinutes: (above - backLiters) / WALK_LITERS_PER_MINUTE,
      stateName: above < backLiters ? 'notCovered' : (above < 2 * backLiters ? 'tight' : 'covered'),
      nextName: next.toName, nextMeters: next.meters, nextMinutes: next.minutes };
  }

  /** Height along the plan's path for the profile chart: [{ meters, height }] (height above the path's lowest point). */
  function profile(site, P) {
    var out = [], d = 0, lo = Infinity, i;
    for (i = 0; i < P.path.length; i++) {
      if (i > 0) d += Math.hypot(P.path[i][0] - P.path[i - 1][0], P.path[i][1] - P.path[i - 1][1]);
      var h = heightAt(site, P.path[i][0], P.path[i][1]);
      if (h < lo) lo = h;
      out.push({ meters: d, height: h });
    }
    for (i = 0; i < out.length; i++) out[i].height -= lo;
    return out;
  }

  var api = {
    TANK_LITERS: TANK_LITERS, RESERVE_LITERS: RESERVE_LITERS, FLAT_LITERS_PER_KM: FLAT_LITERS_PER_KM,
    FLAT_MINUTES_PER_KM: FLAT_MINUTES_PER_KM, WALK_LITERS_PER_MINUTE: WALK_LITERS_PER_MINUTE,
    DESIGN_LITERS_PER_MINUTE: DESIGN_LITERS_PER_MINUTE, SLOPE_LIMIT_DEGREES: SLOPE_LIMIT_DEGREES,
    siteFromGale: siteFromGale, slopeDegrees: slopeDegrees, slopeFactor: slopeFactor, nodeOf: nodeOf,
    minutesTo: minutesTo, returnMinutes: returnMinutes, reachMinutes: reachMinutes, whatIf: whatIf,
    pathFrom: pathFrom, heightAt: heightAt, walkable: walkable,
    straightMinutes: straightMinutes, plan: plan, describe: describe, profile: profile, visorAtStop: visorAtStop
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.WalkbackModel = api;
})(typeof window !== 'undefined' ? window : globalThis);
