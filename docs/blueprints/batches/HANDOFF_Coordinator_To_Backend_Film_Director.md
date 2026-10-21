<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole handoff
related-designs:
  - ../../DESIGN_Film_Director.md — THE DESIGN: build its UML (§1 module view, §2 classes, §3 sequences), formats and routes (§4), decisions (§5)
  - ../../PLAN_Feature_Films.md — what the takes film; ../../films/clips.json is the seed
  - ../../RUNBOOK_Filming.md — film.py as built (CE-3289), which becomes a client
-->

# HANDOFF — coordinator → backend: build the film director, slice 1 (CE-4038)

**Dispatched at `751d89899`** (re-dispatched under rule 1a: the first stamp `d4f406cd9` was never merged by backend; the amendment adds the 20 / 15 Hz step fallback to B0, B3 and the gates). ⛔ **Your scope is FROZEN at this sha.**
Documents that change after it are FYI only. If a later document invalidates an item, stop and report it; do not adapt.

🔒 **User, 2026-10-21:** *"lets write a note to backend to implement that"* (R-4062) · *"i need also the live mode with no saving screenshot
images, jus rendering on the screen"* · *"it will be implemented by the backend"* · *"clip can run on 15Hz if faster stepping does not work"* · *"(or 20)"*.

## What to build — the design's phases 1–3

⭐ **Build against the diagrams in [DESIGN_Film_Director.md](../../DESIGN_Film_Director.md); do not redraw them here.** Report the match
per design obligation ③: *"the design has N classes and M sequences; what I built matches, or deviates HERE because…"*. A deviation goes
back INTO the design (marked SUPERSEDED), not only into your report.

| # | item | design basis | accepted when |
|---|---|---|---|
| **B0** | ⭐ **first, before any director code:** a rail that two previews of one take, stepped alike, produce the same world at every cut. Use CE-202's method at film scale (a real demo scenario, 30 s) | D-1 claim row; §3b-i | the rail passes — at 60 Hz, and at 20 Hz and 15 Hz too (the fallback step rates, B3). ⛔ If it fails, STOP and report: paced real time rests on it |
| **B1** | `Take` / `Reel` / `TakeStore`: load, save, validate against the loaded world (each unknown name listed); `takes/` folders copied by "Save As" / new-from | §2, §4, §4a, D-2 | a take with a misspelt name is refused with the name; a copied scenario carries its takes |
| **B2** | `ShotEvaluator`: follow, over-toward, lookAt, eye-target, orbit, `to` + ease, fov. ⭐ **Port `film.py`'s `_shot_for`; do not write a second copy of it** | §2, D-9 | unit rails per camera kind; `film.py` drives `/director/*` (or `ShotEvaluator` through `/film/shot`) and keeps working |
| **B3** | `PacedClock` + `DirectorRunner`: open from the file → `ScenarioReadyProbe` → `EnterPreviewMode(startPaused)` → paced `StepOneTick`s → hold the shot each frame → `ExitPreviewMode` → next item, with a title card over each load. The new call `EditorSubsystem.Update → DirectorRunner.Tick` after `DrainAll` | §1a, §3a, D-1, D-11, D-17 | a reel of 3 takes from 3 scenarios plays unattended **live, on screen, saving nothing**; cuts land within one frame of the baked times; `realtimeFactor` reported. ⭐ **Measure** whether the master retires 2 steps in one frame. ⭐ **If not, and rendering is under 60 fps:** run the take at a coarser fixed step — **20 Hz, else 15 Hz** (`tickHz` in the take; send `StepTimeIntent { DeltaSeconds = 1/20 or 1/15 }`, no engine change), one step per rendered frame; live, scout and capture use the same rate; baked anchors record it |
| **B4** | `ScenarioReadyProbe`: **extract** `HandleScenarioLoad`'s edge logic; the route uses the same class | D-11 | the existing load rails stay green through the shared probe |
| **B5** | `/director/takes`, `/reels`, `/take/{s}/{t}` (GET, PUT), `/play`, `/stop`, `/next`, `/pause`, `/resume`, `/status`, each with a `DebugApiRouteDocs` entry (MCP) | §4 | `POST /director/play` without `capture` plays live; with `capture` it renders (B8) |
| **B6** | the **Director panel** (View › Director): reel or take, Play / Stop / Next, replay this take, prepare (scout), speed, a report row per item. Same runner methods as the routes | §4 panel | usable without HTTP |
| **B7** | `Scout` + `AnchorBook`: bake anchor times, verify them live, re-scout on a scenario hash or build change; the report names a missing beat. `/director/scout` | §2, D-3 | a take's every anchor is found, or the missing one is named |
| **B8** | capture: `CaptureSink` (one step per film frame, `speed` for slow motion), a reel → **one** MP4 with fades or title cards and a chapter per item | §3b, §4a, D-15 | a reel of several scenarios renders to one movie on the cloud VM (Xvfb) |
| **B9** | `CinemaMode`: panels and gizmos off while the director runs; captions drawn on screen and burned into frames on request | D-6, D-7 | ⭐ **measure** the per-frame time with panels off against today's ~0.8 s |
| **B10** | layers: `LayerOverride` (a take's or shot's `layers`, **transient, never written** to the settings registry) + `GET/POST /map/layers` (= the user's click, saved like one) | §4b, R-4060, D-13 | a take's layers never reach the user's settings file; the route and the panel agree |
| **B11** | sound: the **2-D ear** (view centre, half the visible width up), `GET/POST /audio`, a take's `sound` switch, and the film's **sound track** (the mixer logs each sound while capturing; the WAV is rendered offline and muxed) | §4c, R-4059, D-14 | a captured reel has sound in sync; FPS still always sounds |
| **B12** | seed: split `docs/films/clips.json` into `scenarios/<name>/takes/main.take.json` (55) and `films/features.reel.json` in CAPABILITIES chapter order | §6 seed | `/director/takes` lists them; the scout reports per take |

⛔ **Out of scope:** instant replays (R-4061: a take's replay shots are skipped and named in the report), `/director/draft` (phase 4), live
runs, `--mode all` hosts (D-8).
⭐ **R-106:** if a measurement breaks a lean (B0, B3's two steps per frame, B9's gain), stop THAT item, report it, and finish the rest.

## Gates — report these rows (the coordinator does not re-run them)

| # | report |
|---|---|
| 1 | per gate: the verbatim command · pass / fail / skip · the delta against the dispatch sha |
| 2 | ⭐ **T-1, the feature's own suites first:** `Hrot.Presentation.Tests` `FilmTests` + `FilmFrameRail` (the film camera you drive), then the editor's load rails (`DeterminismRails`, the `HandleScenarioLoad` readiness cases) through the extracted probe |
| 3 | every new rail named (B0–B11), each with an inverse-edit red-proof where it guards a rule (B0, B10's never-written, B11's FPS-always) |
| 4 | the integration row: one live run of `POST /director/play {reel}` in the editor, **and** one capture of the same reel on the cloud VM. The scout report of the 55 seeded takes, as a table: found / missing beat / name not found |
| 5 | the measurements: two steps per frame (yes/no), the `tickHz` chosen, per-frame time with panels on and off, the realtime factor on the cloud VM and on a desktop; ⚠ any seeded take whose beats change at 20 / 15 Hz versus 60 Hz (the scout table, per rate) |
| 6 | the build: the affected projects with `--no-restore`, the TEST projects built (the stale-binary trap); one full-solution build at the end |
| 7 | the working tree clean after the suites; `tracker-counts.py --check`, `rulings-check.py`, `design-digest.py --check`, `mermaid-check.mjs` on the design |
| 8 | every id allocated (`CE-3xxx`, `R-3xxx` from your blocks) and the design sections you updated to the as-built |
