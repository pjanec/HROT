<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../DESIGN_Film_Director.md — THE DESIGN this note frames (UML §1–§3, formats and routes §4, decisions §5, phases §6)
  - ../../PLAN_Feature_Films.md — what to film; docs/films/clips.json is the seed of the takes
-->

# NOTE — coordinator → backend: the film director (CE-4038)

🔒 **User, 2026-10-21:** *"filming instructions … so that i can run these on my machine in real time, without stepping, a pre-step to
overnight filming … follows the loaded scenario, waits for it being loaded, then following the instructions, then go to another scenario
in the list; also allowing each scenario to be replayed … maybe it could be a feature of the editor itself"* · *"controlled via ai debug
http of course"* · *"and also from some ui panel"* · *"it will be implemented by the backend"*.

⭐ **SUPERSEDED for building by [HANDOFF_Coordinator_To_Backend_Film_Director.md](HANDOFF_Coordinator_To_Backend_Film_Director.md)** — the user approved it (R-4062). This note is the history of how the design got there.

**The design:** [DESIGN_Film_Director.md](../../DESIGN_Film_Director.md). ~~Do not build until the user approves
§5.~~ It needs your measurements first: the per-frame cost with the panels hidden (D-7), and whether the master retires 2 steps in one frame
(D-1).

**Your five tips, and where each landed:**

| your tip | in the design |
|---|---|
| 1. camera moves within a shot, with easing | `ShotSpec.To` + `Ease`, evaluated per frame by `ShotEvaluator` (§2) |
| 2. faster rendering: skip the panels while filming | `CinemaMode` (D-7) — panels and gizmos off while the director runs; ⭐ measure the gain on entry |
| 3. automatic plans from the event list | `/director/draft` (D-10) — a draft take for a human to edit |
| 4. optional overlays per shot | `ShotSpec.Overlays`, opt-in (D-5) — ⚠ amends R-3032, the user decides |
| 5. titles and captions; no sound | drawn by `CinemaMode`, burned into captured frames on request (D-6); no audio — R-1004 plays sound only while possessed |

**The parts that matter most:**
- **D-1, paced stepping.** Real-time play runs the sim by fixed `StepOneTick`s paced to the wall clock, never in `Continuous` mode. The live
  show, the scout and the overnight film are then the same run, frame for frame, and a cut can land *before* an event.
- **D-9, one camera-math implementation.** `film.py`'s `_shot_for` moves into `ShotEvaluator`; `film.py` / `film-events.py` become clients
  of `/director/*`.
- **D-11, extract the readiness edge.** `HandleScenarioLoad`'s edge logic moves into a `ScenarioReadyProbe`, shared by the route and the
  runner. `LoadedScenarioName` is set when the load is SENT, so it cannot be the signal.
- ⭐ **"Save As" copies `takes/`**: the user expects several versions of a scenario, each with its own takes.

CE-4034 (the overnight batch runner) becomes phase 3 of this design: the batch is `POST /director/play {reel, capture}`.

## Second pass, same day — the user's rulings (R-4059, R-4060)

| | what changed in the design |
|---|---|
| **R-4059 sound** | sound in every map view behind the menu switch (CE-1137 already does 3-D; add the **2-D ear**), FPS always; `GET/POST /audio`; a take may silence a shot; **the films get a sound track**: the mixer logs every sound it starts while capturing, the engine renders the WAV offline (§4c) |
| **R-4060 layers** | overlays off by default in a take; a take or shot turns map layers on and off **as a transient override, never saved**; `GET/POST /map/layers` does exactly what a click in the Layers panel does, saved like one (§4b) |
| ~~instant replays live~~ | ⛔ **superseded by the third pass below** |
| **a reel is one movie** | several scenarios' takes, whole or part, in one film with transitions, chapter marks and one sound track (§4a) |

## Third pass, same day — R-4061

| | |
|---|---|
| ~~a LIVE run, not the preview~~ | ⛔ **reversed in the fourth pass below** |
| **no instant replays in slice 1** | a take's replay shots are skipped and named in the report |
| **later: replays from the recording** | the replay slice first measures record → playback fidelity (§3c questions); the in-memory bookmark is the fallback |

## Fourth pass, same day — R-4061 amended: the PREVIEW

🔒 *"live play from saved worls snapshot is even more untested feature so i would avoid it for now. we do not want instant replay in slice
1 so preview mode will do just fine."* ⇒ each reel item is **`OpenForEdit` (from the saved file) → `EnterPreviewMode(startPaused)` → paced
steps → `ExitPreviewMode`**. A re-run (the scout, "replay this take") is the preview's rewind. The first rail: two previews of one take,
stepped alike, cut at the same frames. Live runs return only with the later replay slice (§3b-i, §3c).
