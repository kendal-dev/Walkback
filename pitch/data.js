// Walkback pitch: every number shown on the slides lives here.
// Update this file as the app evolves; the slides read it on load.
// A value of null shows as "—" on the slide and is listed as pending in the notes view (key N).
// Source of each value: the app (TestOutput / Quest log) or DATA.md of the project.

window.WALKBACK = {
  // Slide 2: straight line vs safe route, habitat -> Nevado Sajama.
  // Fill from the app once the walking model is wired (session A/C). Today the panel shows TEST VALUES.
  straightMinutes: null,      // minutes walking the straight line (ignores slope)
  safeMinutes: null,          // minutes on the safe route (never over 20°)
  slopeLimitDeg: 20,          // NASA slope limit used by the planner

  // Slide 4 / live demo: verdict line (real values once the model is wired).
  o2UsedPct: null,
  marginToReturn: null,       // e.g. "5 h 40 min"
  reserveMinutes: 30,         // suit-failure reserve, never touched

  // Slide 5: validation. Only show the Apollo number if docs/SOURCES.md confirms it.
  btuConfirmed: false,        // set true when docs/SOURCES.md has the link for 1,200 BTU/hr
  btuPerHour: 1200,
  fps: 72,                    // measured on Quest 3 (practice app, 14 Nov 05:50); re-measure with Walkback
  testers: null,              // number of people in the Sunday test (target 5)
  testerScore: null,          // average ease score 1-7
  rockLimitM: null,           // the one figure the map cannot see (1 m or 4 m): session B confirms

  // From DATA.md (14 Nov 2026, real).
  site: {
    name: "Gale crater boxwork",
    center: "4.807° S, 137.381° E",
    sideMeters: 1000,
    gridMeters: 3.9,
    slopeShares: { under10: 53, from10to20: 28, over20: 19 }, // % of the square
    roverSlopeMedian: 8.8,    // median slope along Curiosity's path, degrees
    roverPositions: 85,
    roverSols: "4548–4914",
    stopsApartMeters: 10,
  },
  conditions: {
    sol: 4995, earthDate: "2026-08-25", solsOld: 78,
    air: "-71 / -5 °C", ground: "-84 / 4 °C", pressurePa: 777,
    radiation: "0.64 mSv/day",
  },
};
