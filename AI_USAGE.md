# AI usage in Walkback

Team MARSXR used AI tools to build Walkback during the NASA International Space Apps Challenge 2026. This file says which tools, for what, and what the team did. It is also our acknowledgement of AI-written code, because the commits do not carry it (see "Commits" below).

## Tools

| Tool | Used for |
|---|---|
| **Claude Code** (Anthropic), models *Opus 5.5* (default) and *Fable 5.2* (critical pieces and audits) | Writing the app's code, the data pipeline, the automatic test and the build tools; reviewing and auditing the app; drafting documentation |
| **Codex** (OpenAI) | Parts of the web page in `walkback-web/web/`, each from one of the task prompts in `walkback-web/agentes/`: the Curiosity rover loader `curiosity.js` and `tools/prepare_curiosity.mjs` with its test page `tools/curiosity_test.html`; the "Enter VR" view `xr.js` with its test page `tools/xr_test.html`; it also added three three.js files under `vendor/three/addons/` (`loaders/GLTFLoader.js`, `utils/BufferGeometryUtils.js`, `webxr/VRButton.js`) |
| **Antigravity** (Google) | The web page's pre-publish check, `walkback-web/web/tools/check_web.mjs` (task prompt `walkback-web/agentes/03-check-web.md`) |
| **Gemini** (Google) and **Grok** (xAI) | A research round on candidate stops and data sources (prompts R8-1 and R8-2, see DATA.md, decision 5) |
| Image generation tool ("ImageGen") | An astronaut illustration and a discarded walk cycle for an earlier version of the presentation. Both were removed from the repository on 15 Nov 2026. The illustration is shown only at the local defense, from a file kept outside the repository, marked on the image and credited on the slide (see "Images, video and audio") |

## What the AI did

- **App code.** Every C# script in `Assets/Walkback/` was written with Claude Code. Several Claude Code sessions worked in parallel, each on its own files: the app (scene, terrain and layers, panel, pointer and table, globe and jump, helmet HUD, mission mode), the data, and the documentation. A session was also assigned the team's walking and oxygen model; that model is not in the repository, and the app's numbers come from a stand-in (see the last section).
- **Data pipeline.** The scripts in `data-pipeline/` and the site package in `Assets/StreamingAssets/Site/` were made by a Claude Code session: downloading the NASA and USGS windows, building the heightmap and photo, placing the stops, and running the checks listed in DATA.md (for example, DEM slope against Curiosity's tilt sensor).
- **Audits.** Several review rounds with the Fable 5.2 model read the app, wrote patches with their test checks and wrote reports; the team chose which patches to apply. Parts of the app (the stand-in route and verdict, mission mode, the live space-weather line) were first written as such patches. Some rounds split their work between parallel sub-agents of the same model. The rounds are listed at the end of this file.
- **Research.** Gemini and Grok proposed candidate stops (Altadena came from that round). Every position was then checked against Curiosity's waypoints and every line against the NASA Curiosity blog; drill numbers the research gave that could not be found in the blog posts were not used (DATA.md, decision 5).
- **Documentation.** DATA.md, this file, README.md and NOTICE were drafted with Claude Code and reviewed by the team. The texts of the submission form, the outline of the demo for NASA, the script of the defense and the answers prepared for the jury's questions were also drafted with Claude Code; the team decides what is sent and what is said.

## What the team did

- **Directed the work:** chose the problem and the product, approved the site and the stops, and split the work between sessions.
- **Decided:** every product idea the AI proposed was marked as a proposal; the team accepted or rejected it.
- **Tested in the headset:** built the app, ran it on the Meta Quest 3 and reported what worked and what did not (see the status table in README.md).
- **Reviewed the data and its sources** before approving them for the screen.
- **Prepared the presentation and the defense** (the presentation's adaptation was AI-assisted, as its own README says; see also "Images, video and audio").

## How the AI use is marked in the repository

- **First line of every script written with AI:** `// Written with <tool> (AI), directed and tested by team MARSXR`, where the tool named depends on the file: **Claude Code** for every script of the app and most of the web page, **Codex** or **Antigravity** for the web files listed for them in the table above. The three.js files in `walkback-web/web/vendor/three/` are three.js's own code (MIT, see its `LICENSE` there); the three that Codex added carry the Codex line too.
- **`CLAUDE.md`** at the root is the instruction file the Claude Code sessions read: rules, who writes what, status, decisions. It is kept public as a record of how the AI was directed.
- **This file** and the AI field of the submission form.

## Commits

The commits carry no AI co-author line: the team removed it from the history on 14 Nov 2026 and decided to declare the AI use here, in the first line of each script and in the submission form instead. All code in this repository should be read as AI-assisted unless stated otherwise.

## Images, video and audio

- **The app:** no AI-generated images, video or audio. Every image in the app is NASA / USGS data (see NOTICE). The app's sounds are generated by code at run time (`AudioClip.Create` in `HelmetHud.cs`, `Pointer.cs`, `PanelButton.cs`, `GlobeOpening.cs`); there are no sound files.
- **The presentation for the local defense:** its code and texts were written with Claude Code, directed by the team, and it is not in this repository (the team removed it on 15 Nov 2026; earlier versions remain in the git history). The demo sent to NASA is a 30-second video of the app recorded from the headset, with no AI-generated image. An earlier version of the slides used an AI-generated astronaut illustration and a discarded AI-generated walk cycle; they remain only in the git history. **Local defense only (Daniel's decision, 15 Nov 2026):** the slides shown at the local defense include that AI-generated astronaut illustration, marked "Ilustración hecha con IA" on the image itself and credited on the slide.

## What the AI did not decide

The walking and oxygen numbers on screen are a stand-in ("plan B") until the team's walking model lands, and the app labels every one of them "ESTIMATE: plan B over the relief, not the walking model yet". The AI did not choose the mission constants on its own: they are listed in NOTICE, which also says which of their sources are not written in this repository yet.

## Audit rounds with the Fable 5.2 model (log)

Times are local (Bolivia, UTC-4) and are the times the team's working notes (`CLAUDE.md`) give for applying each round. The app's Claude Code session (Opus 5.5 by default) applied the patches, ran the automatic test and built the app; the count is that test's.

| Round | Applied | What the AI wrote | Test after it |
|---|---|---|---|
| Earlier rounds | 14 Nov, 15:20 to 18:02 | The stand-in route, return map and verdict ("plan B"), mission mode, the visor's compass and the board's charts, and a first set of fixes | 388 of 388 (18:02) |
| R12 | 14 Nov, 21:10 | Patches 0023-0028: the NEXT STOP and ROOM VIEW signs in mission mode, an error behind the fade curtain no longer leaves the headset black, the menu button read three ways | 396 of 396 |
| R13 | 14 Nov, 21:46 | Patches 0029-0035: a stop card that ran over its box, the table brought in front of the user after the mission, a RECENTER sign, larger board text, new test checks | 403 of 403 |
| R14 | 14 Nov, 23:07 | Patches 0037, 0038 and 0040 (the arrival card under the visor, END OF ROUTE, checks for the sources board); 0039 was applied and reverted; one patch was kept on hold. Also a guide to read the headset's log and a demo script | 408 of 408 |
| R15 | 14 Nov, by 23:41 | Patches 0041-0043, 0045, 0046 and 0048: the WHAT IF no longer starts during a fade, checks for the numbers said in the defense, lower table pillars, truer empty-state texts. Run with four parallel sub-agents | 414 of 414 |
| R16 | 15 Nov, 00:35 | Patches 0049-0051: the app waits for the head pose before placing the table, ESTIMATE instead of PLACEHOLDER, the 360° PHOTO button's name. Also a review of the presentation | 416 of 416 |
| R17 | 15 Nov, 01:20 | No code. Corrected drafts of README.md, AI_USAGE.md and NOTICE, of the demo outline and form texts for NASA, and of the team's morning checklist | 416 of 416 |
| R18 | 15 Nov, 01:35 | Patches 0052 and 0053: the live space-weather line from NASA DONKI and its README text. Also phrases for the defense | 424 of 424 |
| R19 | 15 Nov, 02:25 (the new presentation reached `main`) | The design of the new presentation; `walkback-model.js`, a JavaScript port of the app's stand-in model, with `tools/check-model.js` (it compares the port with the app's numbers); the slide texts in Spanish and English. An Opus 5.5 session built the pages from that design | App unchanged (424 of 424); the port's check: 18 of 18 |
| R20 | 15 Nov, from 02:36 | No app feature. Seven parallel sub-agents wrote the headset checklist, the release steps and notes, the final texts of the submission form, a review of the presentation (run in a headless browser), the demo and rehearsal plan, the protocol for the test with five people, and a review of these documents. Applied: the documentation fixes F7-A and F7-C; the sources of the plan B constants in NOTICE and DATA.md, after the four cited NASA documents were opened and read; the presentation's fixes (the closing on the Gale terrain without NASA's 3D astronaut, whose texture carries the NASA insignia; where the data comes from on slide 6; today and the next step on the closing; the 7-page English PDF for NASA). No code patch: the one kept on hold was not needed, because the headset measured 72 fps on Mars. The app's session ran two sub-agents of its own: one fixed the presentation from that review, one opened the cited NASA documents. Another Claude Code session rewrote part of the slide text at 03:13, and the team chose how to merge both | App unchanged (424 of 424) |

## After the submission (15 Nov)

These changes reached `main` after the submission and touch the web page, its publishing workflow, `.gitattributes` and the README; the app's code is unchanged. Hashes are from `git log`.

- **The landing page (PRs 3 and 4).** PR 3 (merge `2dc73d1`, commit `fc929d8`) added the web page in `walkback-web/`: the page in `walkback-web/web/`, its tools, the vendored three.js and fonts, and the task prompts in `walkback-web/agentes/`. PR 4 (merge `f4e213a`, commit `f6cb515`) updated the landing page (new `hud.js`, `intro.js`, `nav.js`, logo and pictures) and added landing screenshots to README.md. The files carried their header lines from the first commit: Claude Code for `index.html`, `style.css`, `app.js`, `hero.js`, `sky.js` and the data tools; Codex and Antigravity for the files listed for them in the table at the top.
- **Real Gale data on the web (`4e005c9`).** The page's map had drawn an invented test site; it now draws the app's own site package (HiRISE relief and photo, the four Curiosity drill sites, the rover's path) and the app's plan B over the relief, marked ESTIMATE as in the headset. `tools/build_route.mjs` computes the route and the return map with `tools/walkback-model.cjs`, a JavaScript port of the app's plan B (`ReturnFallback.cs`, `MissionFallback.cs`, `HudEstimate.cs`; its header says it is the port made for the presentation in round R19). `build_route.mjs` refuses to write unless its numbers are the ones the app's own test reports on Gale: 1812 m, 43 min walking, 2 h 12 min of work, 181 L used, 285 L spare, and 57.5 % of the ground with a way back on foot in red with the tank at 8 % (all ESTIMATE). Both files carry the Claude Code header.
- **Fixes from the review of that change (`ece5905`).** The WHAT IF note says the planned walk no longer fits and the headset's verdict turns NOT VIABLE; the 57.5 % check became exact; `build_data.mjs` computes plan B before writing anything and writes a smaller photo; the hero chip reads "Whole mission"; `.gitattributes` keeps `walkback-web/web/` out of Git LFS. The repository does not say which tool ran the review.
- **PR 5 (merge `9c7400f`)** brought `4e005c9` and `ece5905` to `main`.
- **GitHub Pages (`17bc9c2`).** The publishing workflow moved from `walkback-web/.github/workflows/pages.yml` to the root `.github/workflows/pages.yml`, which publishes `walkback-web/web/`; its header names Claude Code.
- **The prompts for the external agents.** `walkback-web/agentes/*.md` (in Spanish) are the prompts used to direct the external agents: `00-LEEME.md` is the index, `01-curiosity.md` the Curiosity rover, `02-webxr.md` the "Enter VR" view, `03-check-web.md` the pre-publish check, and `04-revision.md` an independent read-only review of the page. The index names several possible agents; the headers show only Codex and Antigravity, and the repository does not say which tool ran the read-only review.
- **AI tools in this work,** from the files' header lines: Claude Code, Codex and Antigravity. The team directed and merged the work (PRs 3, 4 and 5).

## Version 1.1 (branch `v1.1`, from 17 Nov)

Work on the next version, kept on the branch `v1.1` so that `main`, the release `v1.0-submission` and the web page stay as they were judged. Written with Claude Code (Opus 5.5), directed by the team; each piece passed the automatic test without the headset. None of it has been seen in the headset yet. What changed is listed in CHANGELOG-v1.1.md.

- **Stop preview** (`97c4bc0`): pointing at a stop's pillar shows its card as a preview.
- **The astronaut's own stops** (`7db0fdd`): a tap on the map adds a stop and the plan B route, verdict and charts are planned again at once.
- **Minutes of work at those stops** (`4bead4d`): -5 / +5 on the card; the minutes are the astronaut's estimate and the card says so.
- **360° PHOTO off on picked stops, test screenshots, and an optional SUITS telemetry link** (off unless set up): Claude Code read the public README, `EVA.json` and `server.c` of the SUITS tech team's TSS repositories on GitHub to learn the request format and field names, and wrote a client for them; no TSS code was copied. Tested against a stand-in server on the laptop only.
- **REMOVE, the ground card and planning on a worker thread** (Claude Code, Opus 5.5; directed by the team, checked by the automatic test).
- **The point of no return on the map** (Claude Code, Opus 5.5; the team's rule from the verdict, applied point by point; checked by the automatic test).
- **Picked stops kept between sessions, and the visor's distance to the point of no return** (Claude Code, Opus 5.5; checked by the automatic test).
- **Picked stops and the point of no return on the visor's and the wrist's maps, a mission test with a picked stop, and the README note for the branch** (Claude Code, Opus 5.5).
- **The WHAT IF sign for hands, the stop card's reach line and CLEAR's confirmation** (Claude Code, Opus 5.5; checked by the automatic test).
- **The time at Gale (MarsClock)**: Claude Code wrote the Mars24 steps from the published algorithm, checked them first in a Python script against the REMS feed the data session had downloaded, then in C# with the automatic test (NOTICE section 8).
- **EARLIER / LATER for picked stops (order kept in my_stops.json) and today's season from Mars24** (Claude Code, Opus 5.5; checked by the automatic test).
