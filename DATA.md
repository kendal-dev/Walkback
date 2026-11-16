# DATA.md: the Gale site package

What the app reads from `Assets/StreamingAssets/Site/`, where each file comes from, the exact commands that make it, and the checks run on it. The raw NASA and USGS files are linked here and never committed (`data-pipeline/cache/` is ignored). Written by session B (data) of team MARSXR with Claude Code (AI); every step was run on 14 Nov 2026.

## The package (data contract: guion §22.7)

| File | What | Bytes | From |
|---|---|---|---|
| `site.json` | Name, center, 1000 m side, 513 heightmap, 257 grid, height range, habitat, CRS, one source per layer | 2 349 | `data-pipeline/sites/gale.json` + build |
| `heightmap.r16` | 513 x 513, uint16 little-endian, row 0 = north, height = min + v / 65535 x (max - min) | 526 338 | Elevation (see below) |
| `ortho.jpg` | Base photo, 4096 x 4096, 0.244 m/px, edge to edge of the square | 4 988 703 | HiRISE orthophoto, 25 cm |
| `site.json` `photoTint` | The colour multiplied onto the black-and-white photo: RGB 164, 124, 100, the median ground colour of Curiosity's Mastcam 360-degree mosaic at Nevado Sajama (504 807 ground pixels below the horizon, `data-pipeline/photo_tint.py`). The HiRISE orthophoto mosaic is a single red band; the app names the colour's source in the PHOTO legend | in site.json | PIA26696, NASA/JPL-Caltech/MSSS |
| `sand.png` | 257 x 257, 255 = sandy hollow, 0 = rock. **All 0 for now** (plan B, see decisions) | 143 | Calculated from the elevation |
| `stops.json` | Four Curiosity drill sites in walking order: Altadena, Valle de la Luna, Nevado Sajama, Campo Marte. Position, science (under 20 words), tasks with minutes, risk, a short tag | see report | Rover positions + NASA blog |
| `curiosity_traverse.json` | 85 rover positions inside the square, sols 4548 to 4914, `{sol, x, y}` | 4 160 (4 586 with Windows line ends) | MSL waypoints |
| `conditions.json` | Latest REMS report, season, daylight, surface radiation, the week's space weather; each with its date and source | see below | REMS feed, NASA DONKI, RAD |
| `mars_global.jpg` | The globe: equirectangular, left edge 180° W (trap 11), 4096 x 2048 | 2 550 912 | Viking MDIM 2.1 colour mosaic |
| `panorama_PIA26696.jpg` | Nevado Sajama, 360°, 4096 x 2048 | see below | MSL Mastcam mosaic PIA26696 |

Local coordinates: meters from the south-west corner, x east, y north. The square is centered on 4.807° S, 137.381° E; the projection is equirectangular on the Mars 2000 sphere (R = 3 396 190 m) with true scale at the center latitude.

The web page reads copies of this package in `walkback-web/web/data/` (`site.json`, `stops.json`, `heightmap.bin` = the same bytes as `heightmap.r16`, `ortho.jpg`), made by `node walkback-web/web/tools/build_data.mjs` from `Assets/StreamingAssets/Site/` (run `git lfs pull` first); the same script writes the plan B route and return map (`route.json`, `return_map.bin`, ESTIMATE). Do not edit those copies by hand: change this package and run the script again.

## Sources

| Layer | Product | Link | Credit |
|---|---|---|---|
| Elevation (in use) | MRO HiRISE Gale Crater DEM 1 m, `Gale_DEM_SMG_1m` (NASA Mars Trek) | `https://trek.nasa.gov/mars/trekarcgis/rest/services/Gale_DEM_SMG_1m/ImageServer` | NASA/JPL Mars Trek; HiRISE NASA/JPL/University of Arizona |
| Elevation (reference, guion §5.1) | MSL Gale Merged DEM 1m v3 | `https://planetarymaps.usgs.gov/mosaic/Mars/MSL/MSL_Gale_DEM_Mosaic_1m_v3.tif` (3.87 GB) | Parker and Calef (2016), USGS Astrogeology |
| Base photo | MSL Gale Merged Orthophoto Mosaic 25cm v3 | `https://planetarymaps.usgs.gov/mosaic/Mars/MSL/MSL_Gale_Orthophoto_Mosaic_25cm_v3.tif` (24.4 GB) | Calef and Parker (2016), USGS Astrogeology; HiRISE NASA/JPL/University of Arizona |
| Curiosity positions | MSL waypoints (one point per drive) | `https://mars.nasa.gov/mmgis-maps/MSL/Layers/json/MSL_waypoints.json` | NASA/JPL-Caltech |
| Stop science | Curiosity blog: sols 4568-4569 (Altadena, 11 Jun 2025), 4695-4701 (13 Nov 2025), 4716-4722 (18 Nov 2025), 4893-4899 and 4900-4907 (Campo Marte, May 2026). Read on 14 Nov 2026 15:30 before use | `science.nasa.gov/blog/` | NASA/JPL-Caltech |
| Stop task times | Proposed by the team. Miller et al. (2017), *Operational Assessment of Apollo Lunar Surface Extravehicular Activity*, NASA/TP-2017-219457, table 30 gives the Apollo statistics for station tasks (median 9 to 11 min, quartiles 4 to 25 min), not times for these tasks | `https://ntrs.nasa.gov/citations/20170007261` | NASA |
| Plan B constants (pace, oxygen, tank, reserve, slope limit) | See NOTICE, section 3: which numbers have a published figure to compare with, which are derived, and which come only from the team's notes | | see NOTICE |
| Weather | Curiosity REMS weather feed (the data CAB publishes) | `https://mars.nasa.gov/rss/api/?feed=weather&category=msl&feedtype=json` | NASA/JPL-Caltech/CAB (CSIC-INTA) |
| Space weather | NASA DONKI: SEP, GST, CME for the week | `https://api.nasa.gov/DONKI/SEP?startDate=2026-11-07&endDate=2026-11-14&api_key=DEMO_KEY` (same for GST, CME). Since 15 Nov (audit R18, patch 0052) the app also asks the SEP endpoint once at start for the last 7 days (`DEMO_KEY`, 4 s limit); the card then says "(live)" with the time, otherwise "(stored in the app)" | NASA CCMC |
| Surface radiation | 0.64 mSv/day (dose equivalent), Curiosity RAD | Hassler et al. (2014), *Science* 343 | NASA/JPL-Caltech/SwRI |
| Globe (in use) | Viking MDIM 2.1 colour mosaic, 1 km/px, 21339 x 10670 (36 708 552 bytes, matches the server) | guion §5.1, row 4 | NASA/JPL/USGS |
| Globe (plan B, used 12:10-12:23) | Mars image texture, from Viking images processed at the USGS, 1440 x 720 | `https://assets.science.nasa.gov/content/dam/science/cds/3d/resources/image/mars/Mars.jpg` | NASA/JPL-Caltech |
| Nevado Sajama panorama | PIA26696, Mastcam 360°, sols 4714-4741 (14.0 MB) | `https://assets.science.nasa.gov/content/dam/science/psd/photojournal/pia/pia26/pia26696/PIA26696.jpg` | NASA/JPL-Caltech/MSSS |

## How it is built

GDAL 3.13.3 from QGIS (`C:\Program Files\QGIS 3.44.15\bin`, not on PATH). In Git Bash, prefix GDAL calls with `MSYS_NO_PATHCONV=1`. Only the window of the square is read from the servers, never the whole file.

1. **Elevation, Mars Trek** (4.2 MB, 64 s): one `exportImage` request for the window at 1 m.
   `curl -o data-pipeline/cache/marstrek/gale_dem_window.tif "https://trek.nasa.gov/mars/trekarcgis/rest/services/Gale_DEM_SMG_1m/ImageServer/exportImage?bbox=137.37253,-4.81544,137.38947,-4.79856&bboxSR=104905&size=1004,1001&format=tiff&pixelType=F32&f=image"`
2. **Elevation, USGS** (reference; 1036 x 1028 px, 2.76 MB, finished on the second try at 12:51 after a 54 KB/s and 5 KB/s network). The file is stored in strips one row high, LZW: the square needs about 1030 full rows of 33 km, ~80 MB.
   `gdal_translate -srcwin 14706 39638 1036 1028 /vsicurl/https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/Mars/MSL/MSL_Gale_DEM_Mosaic_1m_v3.tif data-pipeline/cache/usgs/gale_dem_window.tif`
   A 60-row strip across the stops was read (`strip_rows40192_60.tif`, 0.24 MB) and is used for the alignment check.
3. **Base photo** (16.2 MB): the same window at 25 cm, 4026 x 4012 px.
   `gdal_translate -projwin 8142714 -284432 8143720.5 -285435 /vsicurl/https://asc-pds-services.s3.us-west-2.amazonaws.com/mosaic/Mars/MSL/MSL_Gale_Orthophoto_Mosaic_25cm_v3.tif data-pipeline/cache/ortho/gale_ortho_window.tif`
4. **Curiosity positions** (0.67 MB): `curl -o data-pipeline/cache/waypoints/MSL_waypoints.json https://mars.nasa.gov/mmgis-maps/MSL/Layers/json/MSL_waypoints.json`
5. **The package** (2 s):
   `python -I data-pipeline/build_site.py --site data-pipeline/sites/gale.json --dem-source marstrek --dem data-pipeline/cache/marstrek/gale_dem_window.tif --ortho data-pipeline/cache/ortho/gale_ortho_window.tif --waypoints data-pipeline/cache/waypoints/MSL_waypoints.json --habitat 514.8,777.0 --sand none --out Assets/StreamingAssets/Site --work data-pipeline/cache/work --report data-pipeline/reports/gale_controls.json`
   It warps the DEM to the 513 points (`average`), the photo to 4096 px (`cubic`, linear stretch 0.5-99.5 %), puts the stops at the rover's positions and writes the controls report. The same program serves any other site: a new `data-pipeline/sites/<site>.json` and its two windows.
6. **Conditions** (1.7 MB feed, 8 s; DONKI 37 KB): download the weather feed and the three DONKI answers into `data-pipeline/cache/rems/` (`msl_weather.json`, `donki_sep.json`, `donki_gst.json`, `donki_cme.json`), then
   `python -I data-pipeline/build_conditions.py data-pipeline/cache/rems 5073 2026-11-07 2026-11-14 "2026-11-14 16:06 UTC" Assets/StreamingAssets/Site/conditions.json`
   Today's sol, 5073, is the rover's latest position in the waypoints file. The CAB page of guion §5.1 (`cab.inta-csic.es/rems/en/`) no longer exists: on 14 Nov it redirects to a 2019 news item, also in a browser.
7. **Globe**: the MDIM 2.1 mosaic resized to 4096 x 2048 (Lanczos, JPEG quality 90) as `mars_global.jpg`; Olympus Mons at 134° W and Hellas at 68° E confirm the left edge is 180° W. The download needed three tries (connection reset, then no DNS for the USGS server), resumed with `curl -C -`.
8. **Panorama**: `python -I data-pipeline/process_panorama.py data-pipeline/cache/panorama/PIA26696.jpg Assets/StreamingAssets/Site/panorama_PIA26696.jpg 0.255` (the horizon sits at 25.5 % of the mosaic height, from the top), then step 5 again with `--panorama nevado-sajama=PIA26696="NASA/JPL-Caltech/MSSS, Mastcam mosaic PIA26696" --extra-sources globe_mdim,panorama_sajama`.
9. **Checks**: `python -I data-pipeline/check_alignment.py <dem 1 m local> <usgs strip 1 m local>` and `python -I data-pipeline/preview_package.py Assets/StreamingAssets/Site <out prefix>` (photo, hillshade and slope side by side).

## 3D models (`Assets/Walkback/Models3D/`, NASA 3D Resources)

NASA 3D Resources offers its models as free to download and use and points to the NASA Images and Media Usage Guidelines (`nasa.gov/nasa-brand-center/images-and-media`) for the terms. The astronaut's page names DigitalSpace Corporation as its source and states no licence of its own (read on 15 Nov 2026). The astronaut's texture carries the NASA insignia and a United States flag: do not show the model with that texture (the team uses no NASA logo).

| File | Triangles | Bytes | Page | Note |
|---|---|---|---|---|
| `Astronaut.glb` | 6 381 (1 mesh, 4 textures) | 762 988 | `science.nasa.gov/3d-resources/astronaut/` | Light enough for the Quest |
| `Perseverance.glb` | 199 521 (68 meshes, 44 textures) | 4 987 176 | `science.nasa.gov/3d-resources/mars-2020-perseverance-rover/` | For Jezero (guion §21.2); heavy: one at a time |
| Curiosity (dirty), not in `Assets/` | | 7 253 380 (zip) | `science.nasa.gov/3d-resources/curiosity-rover-msl/` | The zip holds only a Blender file (`.blend`, 10.7 MB): Unity needs Blender installed to open it. Kept in `data-pipeline/cache/models3d/` |

Both `.glb` need the glTFast package in Unity (offline kit, guion §5.5); without it Unity keeps them as plain files.

## Checks (14 Nov 2026, 11:56; full numbers in `data-pipeline/reports/gale_controls.json`)

| Check | Result |
|---|---|
| Mars Trek DEM against the USGS DEM, on the 60-row strip across the stops | Same place: best match at 2 m east, 1 m north (under one grid cell). Heights: Mars Trek is **17.9 m higher**; the difference varies 0.98 m (std) cell to cell |
| DEM against Curiosity's own elevation, 85 positions | Mars Trek - rover = 17.13 m mean, 1.04 m std. The USGS strip matches the rover within 0.3 m at both stops |
| The step from the hollow to the ridge (NASA: 10 m apart, 2 m higher) | USGS 2.80 m · Mars Trek 2.41 m · rover 2.71 m |
| Slope along the rover's path (a rover drove there) | Median 8.8°, 90 % under 13.1°, max 16.7° |
| Slope over the square | 53 % under 10°, 28 % 10-20°, 19 % over 20° (real scarps: the mesa to the west and the Mount Sharp slopes to the east, both visible in the photo) |
| **DEM slope against Curiosity's own tilt sensor**, 85 positions (independent of any orbital DEM; `check_tilt.py`, 14 Nov 13:35) | **Mars Trek: 2.0° mean difference, r = 0.77** (DEM slope at those positions: max 21°; rover tilt: median 8.3°, max 17.5°). The full USGS v3 window (downloaded 12:51): 6.2° mean difference, r = 0.49, max 44.5°: noisier at 1 m. Over the whole square the two DEMs differ by 1.59 m (std) after removing the 17.4 m offset |
| DEM height against the rover's elevation, full USGS window | USGS - rover = -0.01 m mean, 0.14 m std. Probably not independent: the rover's localized elevation is likely tied to this same DEM |
| Heightmap quantization | Under 1.74 mm |
| **Does the 1 m DEM show the boxwork hollows?** | **No.** Hollows from the DEM (more than 0.5 m below the 20 m mean) cover 3.2 % of the grid and are no darker in the photo (116.5 vs 117.8). At 1 m only 6.3 % of the dark ground in the photo is a DEM hollow (3.2 % by chance). The USGS strip does no better (r = 0.03 against the photo, Mars Trek 0.15) |
| Unity automatic test with this package (session A's test, 11:56) | 215 of 217 (at 11:56; the whole test passed 424 of 424 on 15 Nov 01:33). Every site check passes (contract, r16, sources, panel naming mission, instrument and resolution, pillars on the ground). The two failures are session A's: the panel data line wraps out of its one-line box, and the habitat pillar has no collider |

## Decisions (proposed by Claude; Daniel decides)

1. **Elevation from Mars Trek, kept on the data** (first plan B of guion §5.1 and §5.6, because the USGS server was slow; once the USGS window arrived at 12:51, Mars Trek stayed because its slopes match Curiosity's tilt sensor three times better: 2.0° against 6.2°). Its absolute heights read 17 m higher than the USGS DEM and the rover; slopes, routes and times do not change with that offset. To use the USGS DEM anyway: step 5 with `--dem-source usgs --dem data-pipeline/cache/usgs/gale_dem_window.tif`.
2. **No sand map** (plan B of guion §22.5): rock everywhere (η 1.2) and the sand source in `site.json` says the 1 m elevation misses the hollows (no legend in the app shows it). `--sand dem` writes the DEM hollows instead.
3. **Habitat at x 514.8, y 777.0**: on Curiosity's own path (its sol 4580 position), 330 m north of Valle de la Luna and Nevado Sajama (Altadena is 200 m from it, Campo Marte 716 m), 5° slope. *Proposal.*
4. **Stops at the rover's positions** during each drill campaign: Valle de la Luna at the sol 4687 position (parked to sol 4709; drilled sols 4689-4695), Nevado Sajama at the sol 4711 position (parked to sol 4741; drilled sol 4718). They are 10 m apart.
5. **Two more drill stops (14 Nov 15:35), for variety and a real distance:** **Altadena**, Curiosity's 43rd drill hole (the blog: "the 42 drill sites that came before"), at the sol 4560 position (parked to sol 4571; hole imaged sol 4564), 200 m from the habitat; and **Campo Marte**, in the layered sulfate unit just above the boxwork, named by NASA after a red sandstone feature in Bolivia, at the sol 4893 position (parked to sol 4912; hole imaged sol 4897), 716 m from the habitat and 77 m higher. Its risk line is the blog's: next door, the Atacama block came up with the drill. Altadena came from Daniel's research round (Gemini and Grok, prompts R8-1 and R8-2), Campo Marte from a Fable 5.2 audit; positions checked against the waypoints, every line against the blog. The drill number 44 that the research gave is used for Valle de la Luna ("Curiosity's 44th drill hole" on screen): the NASA Curiosity blog of 13 Nov 2025 says the rover "drilled its 44th hole on Mars". The number 47 is not used: not found in the posts read. Task minutes reuse the drill set of Valle de la Luna. *Proposal.*

## Not made (left out of v1.0)

`thermal.png`: the research found the THEMIS quantitative tile (`THEMIS_TI_Mosaic_Quant_30S120E_100mpp.cub`, 2.36 GB, Astropedia), but on 14 Nov 15:33 the Astropedia server did not answer in 2 minutes; at 100 m the square is only 10 x 10 pixels, context only (guion §5.1) · glTFast in Unity for the two `.glb` models. Valle de la Luna has no official 360° panorama (guion §5.1, row 3b): the app says "No panorama for this stop yet".
