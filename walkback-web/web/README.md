# Walkback web page

Static page published to GitHub Pages by `.github/workflows/pages.yml` when `main` changes under `web/`.
Works offline: three.js, Satoshi and Inter are copied in `vendor/`.

## Update the data

```bash
git lfs pull
node web/tools/build_data.mjs
```

It reads `Assets/StreamingAssets/Site/` (session B) and `model-tests/out/` (session C) and writes
non-LFS copies into `web/data/` (`heightmap.r16` becomes `heightmap.bin`, same bytes).
Before the real data exists, `node web/tools/make_test_data.mjs` writes an invented test site (TODO).

Check that nothing in `web/` is in LFS: `git lfs ls-files | grep "web/"` must print nothing.

## route.json fields the page reads (session C)

`verdict` ("VIABLE" or anything else), `pointOfNoReturn` ({x, y, minutes, o2Percent} or null),
`o2CapacityLiters` (to turn return litres into % of the tank), and `points` [{x, y, minutes, o2Percent, stopId?}].
`return_map.bin`: 257 x 257 pairs of float32 LE (minutes, litres), row 0 north; NaN or negative = no way back.

## Local preview

```bash
python -m http.server 8137 --directory web
```
