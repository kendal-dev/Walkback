# Walkback web page

Static page (three.js, Satoshi and Inter are copied in `vendor/`, so it works offline).
It draws the same square kilometre of Gale crater, the same four stops and the same plan as the headset app.

## Update the data

From the repository root:

```bash
git lfs pull
node walkback-web/web/tools/build_data.mjs
```

It reads the app's site package in `Assets/StreamingAssets/Site/` and writes non-LFS copies into
`walkback-web/web/data/` (`heightmap.r16` becomes `heightmap.bin`, same bytes).

The route and the return map:

- if `model-tests/out/route.json` and `return_map.bin` exist (the team's walking model), they are copied;
- if not, `tools/build_route.mjs` computes them with the app's plan B over the relief
  (`tools/walkback-model.cjs`, a JavaScript port of `ReturnFallback.cs`, `MissionFallback.cs` and `HudEstimate.cs`).
  The page then shows ESTIMATE, as the headset does. The script stops if its numbers are not the app's
  (1812 m, 43 min walking, 2 h 12 min of work, 181 L used, 285 L spare, 57.5 % red with the tank at 8 %).

`node walkback-web/web/tools/make_test_data.mjs` writes an invented test site (the page then shows a "Test data" badge).
Never publish the page with it.

## route.json fields the page reads

`verdict` ("VIABLE" or anything else), `pointOfNoReturn` ({x, y, minutes, o2Percent} or null),
`o2CapacityLiters` (to turn return litres into % of the tank), `o2ReserveLiters` (litres kept untouched in the
"what if the tank ran low" check, as in the headset), `estimate` (true = show the ESTIMATE line),
and `points` [{x, y, minutes, o2Percent, stopId?}].
`return_map.bin`: 257 x 257 pairs of float32 LE (minutes, litres), row 0 north; NaN or negative = no way back.

## Local preview

```bash
python -m http.server 8138 --directory walkback-web/web
```
