# Walkback 1.1 (in progress, branch `v1.1`)

Version 1.0 is the one submitted to the NASA Space Apps Challenge 2026: the release `v1.0-submission` and `main`. Nothing here is in that release. Every number on screen is still the plan B estimate over the Gale relief, marked ESTIMATE.

Status: each item passes the automatic test without the headset (`Walkback > Run Auto Test`). **Not yet seen in the Quest 3.**

## New

- **Stop preview.** Point at a stop's pillar and the side board shows that stop's card, marked "PREVIEW · press the pillar to select it". The selected stop does not change and 360° PHOTO is off while previewing. Leaving the pillar brings the selected stop's card back.
- **Your own stops.** A short tap on bare ground (press and release within 0.45 s, without moving, not while the map is dragged) adds "My stop N" there. It goes where it adds the least straight-line distance to the loop habitat → stops → habitat, and the route, the verdict and the charts are planned again at once. Picked stops have a blue pillar, no science note, no photo and no tasks until the astronaut sets them. On ground steeper than 20° the verdict reads NO SAFE ROUTE. At most 4, never within 20 m of another pillar.
- **CLEAR** on the side board (C on the keyboard) takes the picked stops away and the data's plan comes back. The stop buttons keep the four Curiosity drill sites.
- **Minutes of work at your stops.** -5 MIN / +5 MIN on a picked stop's card, from 0 to 120 min. The line reads "Work: N min · your estimate", and time, oxygen and the verdict follow at once.

- **360° PHOTO is off on a picked stop's card** (there is no photo of that place).
- **Optional link to a NASA SUITS telemetry server (off by default).** The SUITS challenge's test server, TSS (github.com/SUITS-Techteam), answers UDP requests on port 14141 with the suit's telemetry as JSON (EVA.json). If the file `suits.json` is in the app's data folder (`{"host":"192.168.1.20","port":14141,"eva":1}`), or `-suits host[:port]` is on the command line, Walkback asks every 2 s for that suit's primary oxygen (`oxy_pri_storage`). The plan then starts with that much oxygen instead of a full tank, at most once every 10 s and only when it moved 1 % or more; the verdict card reads "SUITS TSS suit 1 · primary O2 85 % at 12:03 UTC (simulated) · ESTIMATE", and the WHAT IF button is off. TSS's numbers come from its own simulation, not from a real suit, and the app's oxygen model is still plan B. Both shapes of EVA.json are read (2026: suits under `eva1` / `eva2`; 2027: one suit). Tested only against a stand-in server on the laptop, never against a running TSS.

## To check in the headset

- With hands: a two-hand pinch to zoom must not add a stop (a right pinch released before the left one starts could count as a tap).
- The tap with the controller's trigger, on a map turned and zoomed.
- The preview while sweeping the ray over the pillars.

## Test

469 of 469 (1.0 had 424). The checks for 1.1 are in `AutoTest.UserStops.cs`, `AutoTest.Suits.cs` and the preview checks in `AutoTest.Panel.cs`; four screenshots `TestOutput/v11_*.png`.

## To check with a SUITS server

- Run TSS (it needs Linux or WSL), put `suits.json` with its address in the Quest's app folder (`/sdcard/Android/data/com.DefaultCompany.walkbackNASASpaceApps/files/`), and look for `SUITS TSS:` lines in `walkback.log`.
