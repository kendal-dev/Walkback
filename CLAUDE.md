# Walkback — CLAUDE.md

**Walkback** — *The safest way to go far.* NASA Space Apps Challenge 2026, Santa Cruz (Bolivia) local event, team MARSXR. A mixed-reality tool for Meta Quest 3 that tells an astronaut on Mars, at every step, whether they can still make it back to the habitat. *Meta Quest 3 stands in for the helmet display.*

Deadlines: code freeze Sun 15/11 10:00 · submit 11:00 · the venue closes submissions at 12:00 · defense 14:00 (5 minutes of pure presentation).

## Read before writing anything

1. The handoff, from its first line: `C:\Users\kevin\Segundo_Cerebro\31_CARRERA\04_EVENTOS\SPACE-APPS-2026\space-apps-2026-traspaso.md`
2. The Saturday script, the main file: `...\space-apps-2026-guion-del-sabado.md` — its top notices, §22 entire (it rules), §13.6 and §6, then what your session's prompt names (§22.7).
3. Decisions: `...\space-apps-2026-continuidad.md` §4 (team) and §5 (decisions taken).
4. Log: `...\space-apps-2026-bitacora.md` §1 (AI use, with time) and §3 (organizer answers).

`...` = `C:\Users\kevin\Segundo_Cerebro\31_CARRERA\04_EVENTOS\SPACE-APPS-2026`. The vault has its own rules (`C:\Users\kevin\Segundo_Cerebro\CLAUDE.md`): one fact, one home; nothing is shortened (old text is marked 🗄️); every organizer answer carries a name and a date. Read them before editing the vault.

## Four rules

1. The terrain and every number on screen come from real data with a source; nothing invented is shown as data.
2. Daniel directs and approves. Product ideas are the team's: what Claude proposes is marked as a proposal and Daniel decides.
3. Every AI use is logged (bitácora §1) and declared (`AI_USAGE.md` and the submission's AI field).
4. Say only what the app does.

## Who writes what (no two sessions write the same files)

| Session | Model · machine | Owns |
|---|---|---|
| A · the app | Fable 5.2 · Daniel's laptop | `Assets/` except B's and C's folders · `CLAUDE.md` |
| B · the data | Opus 5.5 · Daniel's laptop | `data-pipeline/` · `DATA.md` · `Assets/StreamingAssets/Site/` · `Assets/Walkback/Models3D/` |
| C · the model and the pitch | Fable 5.2 · Orlando's laptop | `Assets/Walkback/Model/` · `model-tests/` · `docs/` · `pitch/` · `video/` · `README.md` · `AI_USAGE.md` |
| D · the web | Claude Code · Josué's laptop | `web/` · `.github/workflows/pages.yml` |

The data contract (`site.json`, `heightmap.r16`, `stops.json`…), the model interface (`WalkModel`, `RoutePlanner`, `ReturnMap`, `Verdict`, `SuitFailure`, `Probe`, `Mission`, `GridMapping`) and each session's prompt: guion §22.7. Need something in another session's folder → ask Daniel. Check Claude usage at noon.

## Team

- **Daniel**: directs, compiles, tests in the headset, presents. **Only Daniel pushes.**
- **Orlando**: runs session C. When Daniel is away he approves Claude's permissions: yes to reading, compiling and testing inside the project; ask Daniel before deleting, pushing, installing or touching anything outside it. Handles the headset in the live demo.
- **Miranda**: the five-person tests (Sun 8:30) and photos.
- **Josué**: the web page (session D). His working doc: https://claude.ai/artifact/72XQbTwu35auhjCZe3MBbu

## Versions that work — do not update anything

Unity **6000.4.6f1** (the 2022.3 also installed is not used) · Meta XR SDK **209.0.0** · OpenXR **1.16.1** · URP **17.4.0** · Input System **1.19.0**. On the practice project: 72 fps locked on the Quest 3; first build ~11 min, later ones ~4 min.

## State on Saturday 14/11, 10:15

- **Project folder:** `C:\Users\kevin\walkback-NASA-Space-Apps`, created by Meta Quest Developer Hub (it opened in Unity at 10:07). `C:\Users\kevin\practica-spaceapps\Walkback` is a failed first attempt kept as a backup: don't use it. Nothing is copied from the Tupiza practice.
- **OpenXR step:** `Assets/Resources/OculusRuntimeSettings.asset` has `handSkeletonVersion: 1`. Confirm with Meta's Project Setup Tool on the first open.
- **Repository:** `github.com/kendal-dev/Walkback` already exists (Daniel created it: private, empty, description and topics set). Don't create it again. Plan in guion §13.6: `git init` · Unity `.gitignore` · `.gitattributes` with LFS for `*.r16 *.tif *.tiff *.glb *.fbx *.psd *.mp4` and the big site photos, then `web/** -filter -diff -merge` (GitHub Pages can't serve LFS) · `LICENSE` Apache 2.0 · `git remote add origin`. **Commit 1** = the project as the Developer Hub made it, without this file; **commit 2** = this `CLAUDE.md`. Branches: `main` (Daniel merges), `model` and `docs` (C), `web` (D). The repo goes public when Daniel says so.
- **Network:** the venue WiFi (5 GHz) doesn't reach `packages.unity.com` and reaches Meta's servers slowly. Missing packages → the offline kit `C:\Users\kevin\kit-sin-internet\` (its `LEEME.md` has the `manifest.json` lines) or the phone's hotspot.
- **Tools:** GDAL in `C:\Program Files\QGIS 3.44.15\bin` (not on PATH) · ffmpeg 9.0.1 · scrcpy 4.1 and its `adb` in `C:\Users\kevin\AppData\Local\Microsoft\WinGet\Packages\Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe\scrcpy-win64-v4.1\` · `gh` logged in as `kendal-dev`.
- **Laptop:** 16 GB of RAM for Unity plus several Claude sessions: close browsers and other apps before a build. Keep it plugged in (unplugging it closed Unity on 11/11).

## Status, piece by piece (update when a piece closes)

| Piece | Written | Compiles | Passes the test without headset | Seen in the Quest |
|---|---|---|---|---|
| Scene built by code + automatic test | ⬜ | ⬜ | ⬜ | ⬜ |
| Gale terrain and layers (height, slope, return) | ⬜ | ⬜ | ⬜ | ⬜ |
| Route, return map, stops and pillars (with C's model) | ⬜ | ⬜ | ⬜ | ⬜ |
| Panel: verdict, legend, conditions, stop cards | ⬜ | ⬜ | ⬜ | ⬜ |
| Pointer, table, globe and jump | ⬜ | ⬜ | ⬜ | ⬜ |
| Helmet HUD | ⬜ | ⬜ | ⬜ | ⬜ |
| Mission mode + MR ↔ VR button | ⬜ | ⬜ | ⬜ | ⬜ |

A piece is done when it compiles, passes the automatic test without the headset (with screenshots) and Daniel has seen it, in a screenshot or in the headset (guion §24).

## Milestones

H1 13:00 real Mars on the table · H2 16:30 the route with time, oxygen and verdict · H3 18:30 in the Quest · 20:00 P0 closed and installed · 20:00–23:30 mission mode · Sun 10:00 freeze, tag `v1.0-submission`, APK in Releases. The scene and its test were due at 9:30 and the project only opened at 10:07: if a milestone slips, propose the cut in guion §16.3 before going on.

## Code and commits

- Everything the jury sees: in English and readable on a projector (guion §18.1).
- First line of every script: `// Written with Claude Code (AI), directed and tested by team MARSXR`
- One commit per piece, message in English, with Claude's `Co-Authored-By` line.
- Test without the headset before asking for a build; build at milestones. With Unity open, the batchmode test can't run: say so.
- Unity's C# has no `PriorityQueue` (.NET Standard 2.1): use your own heap.
- Read SDK names from the installed packages, never from memory.
- No NASA logos. Nothing copied from the practice.
- The app writes its own log file (the Quest keeps about one minute of logcat); build *Development* so logcat shows its lines.
- The 21 traps already stepped on: guion §7. Product decisions, the walking and oxygen model (continuidad §5; 1 L/min of O₂ ≈ 1,157 BTU/hr, never 238), the priority list P0–P2 (guion §17.3, §18.2, §20, §21) and the places (§21.2): read them there, don't copy them here.

## When something fails

- Unity: `%LOCALAPPDATA%\Unity\Editor\Editor.log`. Quest: `adb logcat`. **Read logs yourself; never ask Daniel to paste them.**
- Headset to screen: scrcpy; one eye: `--crop=1600:1100:232:554`.
- Build times and what was seen in the headset → bitácora §2 and the table above.

## Talking to Daniel

Simple Rioplatense Spanish, conclusion first, numbered steps. After each piece, three lines: what you did, how you tested it, what's missing. Run `Get-Date` before writing any time. At night, remind him to sleep.
