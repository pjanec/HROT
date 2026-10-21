<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-10-21 (first version — coordinator design, to be BUILT BY BACKEND after the user approves §5)
current-answer: §1 the picture · §2 classes · §3 sequences · §4 the formats and routes · §5 the decisions (leans for approval) · §6 phases and acceptance
stale-below: none
known-rot: none
known-conflict: R-3032 says a film frame is the scene ALONE — §5 D-5 (per-shot overlays) would amend it, and asks the user
related-designs:
  - DESIGN_Map_3D_Mode.md §6ab — OWNS the film camera this drives (FilmStudio, FilmDirector as an ICameraDriver, FilmCaptureLayer, the /film routes); this design adds WHO holds the script and WHEN the shot changes
  - RUNBOOK_Filming.md — OWNS film.py / film-events.py as built (CE-3289); D-9 makes them thin clients of this
  - PLAN_Feature_Films.md — OWNS WHAT to film (the clip list, the director's grammar §1a, the gaps); its clips.json is the seed of the takes here
  - blueprints/DESIGN_Time_Architecture.md — OWNS the clock (Continuous = wall-clock dt; Stepping = fixed dt) that D-1 rests on
  - DESIGN_Cgf_Scenario_Session_Slice.md — OWNS IScenarioSession (OpenForEdit, LoadedScenarioName) the runner loads through
  - DESIGN_Cluster_Load_Phase.md §9 — OWNS ScenarioLoadSequence (Idle first, then the load)
  - DESIGN_Remote_Map_Control.md — OWNS remote map commands over DDS (CMD_SET_VIEW); a network path, not a script path — not reused (§5)
  - designs/group-maneuvers/DESIGN_Demo_City_Showcase.md — OWNS the one-scenario showcase; its acts become takes here
-->

# Film director — play scripted camera takes, a playlist of scenarios, live or to film

🔒 **User, 2026-10-21:** *"It would be very useful to have the video filming instructions (what scenario, what scenes, what camera view,
what time.. everything necessary) so that i can run these on my machine in real time, without stepping, a pre-step to overnight filming …
some harness that follows the loaded scenario, waits for it being loaded, then following the instructions, then go to another scenario in
the list; also allowing each scenario to be replayed like that (with the camera cuts) individually; maybe it could be a feature of the
editor itself"* · *"i expect multiple versions of different scenarios"* · *"controlled via ai debug http of course"* · *"and also from some
ui panel for the user without ai debug http"* · *"it will be implemented by the backend"*.

**Words used here.** A **take** is one scenario's camera script: its shots, cuts, anchors and captions. A **reel** is a playlist of
takes. The **director** is the editor feature that plays them: on screen in real time, or frame by frame to a film.

## 1. The picture

```mermaid
graph TD
  subgraph inputs["files (git)"]
    TK["scenarios/&lt;name&gt;/takes/&lt;take&gt;.take.json"]
    RL["films/&lt;reel&gt;.reel.json"]
  end
  subgraph control["who drives it"]
    HTTP["ai-debug HTTP /director/* (and MCP)"]
    PNL["Director panel (editor UI)"]
  end
  subgraph editor["editor process (--mode editor) — NEW in green"]
    RUN["DirectorRunner"]:::new
    CLK["PacedClock"]:::new
    EVA["ShotEvaluator"]:::new
    ANC["AnchorBook"]:::new
    CIN["CinemaMode"]:::new
    SNK["CaptureSink"]:::new
    READY["ScenarioReadyProbe (extracted)"]:::moved
  end
  subgraph existing["existing — reused unchanged"]
    SEQ["ScenarioLoadSequence / IScenarioSession"]
    TIME["ITimeCommands.StepOneTick"]
    FS["FilmStudio.Direct / Capture"]
    FCL["FilmCaptureLayer → FilmDirector (ICameraDriver)"]
  end
  HTTP --> RUN
  PNL --> RUN
  TK --> RUN
  RL --> RUN
  RUN --> SEQ
  RUN --> READY
  RUN --> CLK --> TIME
  RUN --> ANC
  RUN --> EVA --> FS --> FCL
  RUN --> CIN
  RUN --> SNK --> FS
  classDef new fill:#d9f2d9,stroke:#2a7a2a
  classDef moved fill:#fff2cc,stroke:#a68a00
```

*What the picture shows that prose hides: the director adds no new camera and no new clock. It decides WHICH shot is held (through the
existing `FilmStudio` mailbox) and WHEN the sim steps (through the existing `StepOneTick`). The HTTP routes and the panel are two remotes
for the same runner.*

### 1a. Who calls what each frame — the module view

```mermaid
graph LR
  LOOP["EditorSubsystem.Update (main thread, once per frame)"] --> DRAIN["_debugApiJobQueue.DrainAll — /director/* routes land here"]
  LOOP --> TICK["DirectorRunner.Tick(wallDt) — NEW CALL, added after DrainAll"]:::new
  TICK --> STEP["ITimeCommands.StepOneTick ×n (n from PacedClock)"]
  TICK --> DIRECT["FilmStudio.Direct(shot for this sim time)"]
  CANVAS["MapCanvas.Update → FilmCaptureLayer.Update (every map host, AttachMapLayers)"] --> PLACE["FilmDirector.Place → MapCamera3D.Pose"]
  ALL["--mode all hosts (CGF, IG, SimHost)"] -.->|"no DirectorRunner in phase 1"| TICK
  classDef new fill:#d9f2d9,stroke:#2a7a2a
  linkStyle 5 stroke:#c00,stroke-dasharray:4
```

*The one new edge is `EditorSubsystem.Update → DirectorRunner.Tick`. ⛔ The red dashed edge is deliberate: `--mode all` hosts have no
runner in phase 1 (D-8), although every map host already has a `FilmCaptureLayer` and would obey a held shot.*

## 2. Classes

```mermaid
classDiagram
  class Take {
    +string Scenario
    +string Name
    +string Title
    +string Caption
    +double? EndAt
    +Dictionary~string,AnchorSpec~ Anchors
    +BakedAnchors Baked
    +List~ShotSpec~ Shots
  }
  class ShotSpec {
    +AtExpr At
    +double Seconds
    +CameraSpec Camera
    +CameraSpec? To
    +Ease Ease
    +double Speed
    +bool Replay
    +string? Caption
    +string[] Overlays
  }
  class CameraSpec {
    <<follow | over-toward | lookAt | eye-target>>
    +string? Follow
    +string? Over
    +string? Toward
    +Vector3? LookAt
    +float Distance, Yaw, Pitch, Lift, Back, Height, Ahead, Orbit
    +float? Fov
  }
  class BakedAnchors {
    +Dictionary~string,double~ Times
    +string ScenarioHash
    +string BuildSha
    +bool IsStaleFor(scenarioHash, buildSha)
  }
  class Reel {
    +string Name
    +List~ReelItem~ Items
    +bool Loop
    +double GapSeconds
  }
  class TakeStore {
    +IReadOnlyList~string~ TakesOf(scenario)
    +Take Load(scenario, take)
    +void Save(Take)
    +Reel LoadReel(name)
    +ValidationReport Validate(Take, EntityRepository)
  }
  class DirectorRunner {
    +DirectorState State
    +Play(PlayRequest)
    +Stop()
    +Next()
    +Tick(float wallDt)
    +DirectorStatus Status
  }
  class PacedClock {
    +double Speed
    +int StepsDue(float wallDt)
    +double RealtimeFactor
  }
  class ShotEvaluator {
    +FilmShot? Evaluate(ShotSpec, double simTime, EntityRepository)
  }
  class AnchorBook {
    +double? TimeOf(AtExpr)
    +void Observe(LiveCues)
    +IReadOnlyList~AnchorMismatch~ Mismatches
  }
  class LiveCues {
    <<reads in-process>>
    +shots, detonations, behaviour state, health, side, embark, position
  }
  class Scout {
    +ScoutReport Run(Take)
    +Take Draft(scenario, DraftRules)
  }
  class CinemaMode {
    +Enter()
    +Exit()
    +DrawCaption(string, CaptionStyle)
  }
  class CaptureSink {
    +CaptureOptions Options
    +Task Frame(double simTime)
  }
  class ScenarioReadyProbe {
    <<extracted from DebugApiHost.HandleScenarioLoad>>
    +bool IsReady(string scenario)
  }
  class DirectorPanel
  class DirectorRoutes
  class FilmStudio {
    <<existing>>
    +Direct(FilmShot)
    +Release()
    +Capture(FilmCaptureRequest)
  }
  class FilmShot { <<existing>> }
  class ITimeCommands { <<existing>> +StepOneTick() }
  class IScenarioSession { <<existing>> +OpenForEdit(name) }
  class EditorPreviewController { <<existing>> +EnterPreviewMode(startPaused) }

  Take "1" *-- "many" ShotSpec
  ShotSpec *-- CameraSpec
  Take *-- BakedAnchors
  Reel "1" o-- "many" Take : by scenario+take name
  TakeStore ..> Take
  TakeStore ..> Reel
  DirectorRunner --> TakeStore
  DirectorRunner --> PacedClock
  DirectorRunner --> ShotEvaluator
  DirectorRunner --> AnchorBook
  DirectorRunner --> CinemaMode
  DirectorRunner --> CaptureSink
  DirectorRunner --> ScenarioReadyProbe
  DirectorRunner --> IScenarioSession
  DirectorRunner --> EditorPreviewController
  PacedClock --> ITimeCommands
  AnchorBook --> LiveCues
  Scout --> DirectorRunner : runs a take with capture off and pacing off
  ShotEvaluator ..> FilmShot
  DirectorRunner --> FilmStudio
  CaptureSink --> FilmStudio
  DirectorPanel --> DirectorRunner
  DirectorRoutes --> DirectorRunner
  DirectorRoutes --> Scout
```

*What the class picture forces: the over-the-shoulder and orbit math lives ONCE, in `ShotEvaluator`. Today it lives in `film.py`
(`_shot_for`), outside the engine. D-9 moves it in, and `film.py` becomes a client.*

## 3. Sequences

### 3a. A reel, live on screen — the user's machine, real time

```mermaid
sequenceDiagram
  actor U as User (panel) or HTTP client
  participant R as DirectorRunner
  participant S as IScenarioSession + ScenarioLoadSequence
  participant P as ScenarioReadyProbe
  participant V as EditorPreviewController
  participant A as AnchorBook / Scout
  participant C as PacedClock → StepOneTick
  participant E as ShotEvaluator → FilmStudio
  U->>R: Play(reel)
  loop each reel item
    R->>S: OpenForEdit(scenario)
    loop each frame until ready
      R->>P: IsReady(scenario)?
    end
    R->>V: EnterPreviewMode(startPaused: true)
    R->>A: anchors baked and fresh?
    alt stale or missing
      A->>A: scout pass (stepped, no capture) — bakes times, reports missing beats
      R->>V: exit and re-enter preview (rewind to t=0)
    end
    R->>R: CinemaMode.Enter, title card
    loop each frame until the take ends
      R->>C: StepsDue(wallDt) — 0, 1 or 2 fixed 1/60 s steps
      R->>A: Observe live cues (verify baked times)
      R->>E: Evaluate(current shot, simTime) → Direct(shot)
    end
    R->>V: ExitPreviewMode (rewind)
  end
  R->>U: status: done, per-take report (missing beats, anchor mismatches, realtime factor)
```

*The decision the picture makes visible: the sim never plays in `Continuous` mode. The director paces fixed steps against the wall
clock, so the live show and the overnight film are the same run frame for frame (D-1).*

### 3b. The same take, to film (overnight) — and a replay shot

```mermaid
sequenceDiagram
  participant B as film-batch / HTTP client
  participant R as DirectorRunner
  participant C as StepOneTick
  participant K as CaptureSink → FilmStudio.Capture
  B->>R: Play(take, capture: {dir, fps 30, size, captions: burn})
  loop each film frame
    R->>R: shot for simTime (or the replay shot's own time)
    R->>K: Frame(simTime) — waits for the drawn PNG
    R->>C: StepOneTick × (60 / fps × shot.speed)
  end
  Note over R: replay shot reached (phase 1): finish the pass, then a SECOND deterministic pass from load to the replay's start, capture only the replay frames, splice by frame number
  R->>B: frames + manifest (frame → shot, simTime) — ffmpeg joins and cross-fades
```

## 4. Formats and routes

**A take** — `scenarios/<scenario>/takes/<take>.take.json` (`main` is the default take). The shot keys are `clips.json`'s, which are
`film.py`'s plan keys plus `to`, `ease`, `speed`, `replay`, `caption`, `overlays`.

```json
{
  "title": "Panic, then shelter",
  "caption": "Gunfire panics the civilians; they run to the open house, open its door and hide inside",
  "anchors": {"fire": {"on": "shot", "n": 1}, "door": {"on": "near", "who": "Civilian 1", "point": [105, 100], "within": 3}},
  "baked": {"times": {"fire": 2.35, "door": 14.9}, "scenarioHash": "…", "buildSha": "…"},
  "endAt": "door+5",
  "shots": [
    {"at": "fire-2", "seconds": 3, "lookAt": [105, 75, 1], "distance": 55, "yaw": 90, "pitch": -22},
    {"at": "fire-0.3", "seconds": 3, "follow": "Civilian 2", "distance": 9, "yaw": 60, "pitch": -8, "to": {"distance": 6}, "ease": "inOut"}
  ]
}
```

**A reel** — `films/<reel>.reel.json`: `{"items": [{"scenario": "ua-civ-shelter", "take": "main"}, …], "loop": false, "gapSeconds": 1}`.

**Several versions of one scenario** (🔒 *"multiple version of different scenarios"*): a variant is its own scenario folder
(`ua-eject` · `ua-shootdown-eject`), so it carries its own `takes/`. One scenario can hold several takes (`main`, `short`, `wide`). A reel
names `scenario + take`. ⭐ **"Save As" and "new from" must copy `takes/`** with the scenario.

**HTTP** — on the editor's ai-debug host, main-thread jobs like every other route, each with a `DebugApiRouteDocs` entry (so MCP gets
them):

| route | does |
|---|---|
| `GET /director/takes?scenario=` · `GET /director/reels` | list |
| `GET` / `PUT /director/take/{scenario}/{take}` | read / save a take (authoring by an agent) |
| `POST /director/play` `{scenario, take}` or `{reel}` + `{speed?, loop?, capture?: {dir, fps, size, supersample, captions}}` | start |
| `POST /director/stop` · `/next` · `/pause` · `/resume` | transport |
| `GET /director/status` | state, reel item, shot index, sim time, realtime factor, missing beats, anchor mismatches |
| `POST /director/scout` `{scenario, take}` | bake the anchors; the report doubles as a scenario health check |
| `POST /director/draft` `{scenario, rules?}` | an automatic take from the scout's events (backend tip 3, D-10) |
| the existing `/film/shot`, `/film/capture` | unchanged — a held shot still works by hand |

**The panel** — *View › Director*: pick a reel or a scenario's take · Play / Stop / Next · *Replay this take* · Prepare (scout) ·
speed · a row per item with its last report (✅ / ⚠ mismatch / ⛔ beat missing). It calls the same `DirectorRunner` methods the routes
call.

## 5. Decisions — leans for the user's approval

| # | question | ⭐ lean | rejected, one line each |
|---|---|---|---|
| **D-1** | how real time plays | ⭐ **paced stepping**: the runner steps fixed 1/60 s ticks as the wall clock allows (at most 2 per frame), so the run is identical to the stepped film and the scout | `Continuous` play: dt from the wall clock (`MasterSyncController.cs:177-186`), so cuts land late and the AI differs run to run |
| **D-2** | where takes live | ⭐ **beside the scenario**, `takes/*.take.json` — a variant carries its own | in `scenario.json`: presentation in the replicated world document, and the R-260 camera entity is adopted once at load · one central manifest: variants lose their takes |
| **D-3** | when a cut happens | ⭐ **baked anchor times** from a scout pass (exact under D-1), **verified live** by in-process cues; a take is re-scouted when the scenario hash or build changes | live cues alone: cannot cut BEFORE an event (`fire-0.5`), which is the director's main move |
| **D-4** | replay shots | ⭐ phase 1: **film only** (a second deterministic pass, spliced); live play skips them. Phase 2: mid-run snapshot and restore, **after** a rail proves restore-and-re-step reproduces the run | a live replay now: the preview snapshot is taken at t=0 only, and a mid-run restore of brains and path requests is unmeasured |
| **D-5** | overlays (fire lines, unit labels) per shot — backend tip 4 | ⭐ **opt-in per shot, default none**, from the existing gizmo layers. ⚠ **Amends R-3032** (*"the scene alone"*): yours to approve | always none: some beats (fire distribution, who saw whom) need a line to be understood |
| **D-6** | titles and captions — tip 5 | ⭐ **drawn by `CinemaMode`**: on screen live, and burned into captured frames when asked. One drawing for both | `ffmpeg drawtext` only: the live show would have none. ⚠ No sound is captured (FPS audio plays only while possessed, R-1004) |
| **D-7** | rendering speed — tip 2 | ⭐ **`CinemaMode` hides the panels and gizmos** while directing: the map fills the window. Measure the per-frame gain on entry | keep the panels: ~0.8 s per film frame, mostly the panels |
| **D-8** | which hosts | ⭐ **the editor** (`--mode editor`), the runbook's preferred host | `--mode all` now: one perspective on screen, real-time cuts overshoot; later, if a clip must prove the wire |
| **D-9** | `film.py` / `film-events.py` | ⭐ **thin clients** of `/director/*`: one camera-math implementation | keep both: two copies of the over-the-shoulder math that rot apart |
| **D-10** | automatic plans — tip 3 | ⭐ `/director/draft` writes a take from the scout's events (a shot fired → over the shooter 0.5 s before, then the target), **for a human to edit** | fully automatic films: the grammar (PLAN §1a) needs judgement the rules lack |
| **D-11** | "is it loaded?" | ⭐ **extract** `HandleScenarioLoad`'s measured edge logic into `ScenarioReadyProbe`; the route and the runner share it | a new "loaded" event: none exists, and the edge logic is already measured. `LoadedScenarioName` is set when the load is SENT (`EditorScenarioSession.cs:82`), not when it is built |
| **D-12** | moving the camera | ⭐ through `FilmStudio` (the director's `ICameraDriver`) only | writing the scenario's camera entity: it would be saved into the scenario (`DESIGN_Map_3D_Mode.md` §6i) · `DESIGN_Remote_Map_Control` `CMD_SET_VIEW`: an IG-only DDS path |

### 5a. Claim table — what the leans rest on

| claim | code — how it IS | design — how it was MEANT |
|---|---|---|
| a held shot follows its unit every frame, paused or not | ✅ `Film.cs` `FilmDirector.Place` (resolves `Follow` per frame) | ✅ `DESIGN_Map_3D_Mode.md` §6ab |
| every map host already carries the film layer | ✅ `MapInteractionPack.cs:553`; attached by Editor, CGF, IG, SimHost, ReplayBrowser | ✅ §6ab |
| real-time play takes dt from the wall clock | ✅ `MasterSyncController.cs:177-186`; only `Step` uses `fixedDelta` (`:349`) | ✅ `DESIGN_Time_Architecture.md` |
| stepped editor runs are identical | ⚠ CE-202 measured 600 steps identical (`DESIGN_Subsystem_Composition_Unification.md`), and that doc admits its first claim was vacuous | ✅ the runbook §0 measured frame-exact cuts in the editor |
| the editor's step path | ✅ `EditorTimeTransportFacade.Step` → `ITimeCommands.StepOneTick`, the same as `/sim/step` | ✅ AS-11 |
| one step per frame at most on the master | ✅ `/sim/step` comment CE-105 (`DebugApiHost.cs`) | — ⇒ D-1 caps at 2 per frame and **measures** it; if the master retires only 1, real time needs ≥ 60 fps rendering, and the realtime factor says so |
| no "loaded" event exists | ✅ searched; readiness is the edge poll in `HandleScenarioLoad` | ⛔ searched `docs/`, none found |
| a mid-run snapshot restore re-steps identically | ⛔ **not measured** — the reason D-4 waits for a rail | ⛔ none found |

## 6. Phases and acceptance — for backend

| phase | builds | accepted when |
|---|---|---|
| **1** | `Take`/`Reel`/`TakeStore` + validation (unknown names listed) · `ShotEvaluator` (follow, over-toward, lookAt, orbit, `to` + ease, fov) · `PacedClock` · `DirectorRunner` (load → ready → preview → play → exit → next) · `ScenarioReadyProbe` extracted · `/director/play|stop|next|status|takes|reels` · the panel | a reel of 3 takes plays unattended in the editor; each take's cuts land within one frame of its baked times; `realtimeFactor` reported |
| **2** | `Scout` + `AnchorBook` (bake, live verify, staleness) · `/director/scout` · the report | the scout of a take finds every anchor or names the missing one; a changed scenario is re-scouted |
| **3** | `CaptureSink` (+ `speed`, replay by second pass) · `CinemaMode` (panels off, captions) · `film.py` as a client | the overnight run films `clips.json`'s ready clips from a reel; the measured per-frame time with panels off |
| **4** | `/director/draft` · overlays (if D-5 approved) · live replay (if the snapshot rail passes) | — |

⭐ **Seed data:** `docs/films/clips.json` splits into one `main.take.json` per scenario (55 takes), and a `films/features.reel.json` lists
them in CAPABILITIES chapter order.

## 7. INVENTORY — what exists, measured before designing

```
scripts/cbm.sh search_graph name_pattern=".*(Film|Director|Playlist|Showcase|Cinematic).*" label=Class  → total 15, has_more false
    → MissionDirectorSystem, UrbanCombat.ScenarioDirector, Showcase* (an FDP example), EvacDirector (SUMO): none a camera director.
    ⚠ FilmStudio / FilmDirector / FilmCaptureLayer are NOT in the graph (it predates CE-3289); found by grep in Hrot.Presentation/Map3D/Film.cs
scripts/cbm.sh search_graph name_pattern=".*Camera.*" label=Class  → total 24: MapCamera3D, CameraPose, MapCamera (2-D), MapCameraLayer,
    MapCameraTkb, MapCameraView(port), FpsCameraDriver, CameraBookmarkStore (Stride, saved poses), Stride camera controllers
scripts/cbm.sh search_graph name_pattern=".*(ScenarioSession|ScenarioLoad|Preview).*" label=Class  → total 53: EditorScenarioSession,
    ScenarioLoadSequence, ScenarioLoadStep, Reference*LoadHandler, EditorPreviewController, PreviewStateBracket, PreviewParticipants, …
corpus sweep (docs/ then .dev/): "playlist", "cinematic", "camera path", "kiosk", "presentation mode" → none found
check_index_coverage is not available through the CLI ⇒ these totals are not proof of completeness
```

*Reused as is:* `FilmStudio`, `FilmDirector`, `FilmCaptureLayer`, `ITimeCommands`, `IScenarioSession`, `ScenarioLoadSequence`,
`EditorPreviewController`, the debug job queue. *Prior art:* `CameraBookmarkStore` (Stride) saves poses, not timed shots.
