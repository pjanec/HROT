<!--STATUS
state: LIVE
build-state: DESIGN — ⭐ APPROVED 2026-10-23 (R-4071: "looks good!") with three amendments the same day: anchors ARE the existing conditions (R-4072, TE-3 rewritten), the timeline is a central panel of its own (R-4073, TE-17), the recording's events are indexed and cached for a zoomable timeline (TE-4 rewritten, TE-18…TE-20). TE-3b approved (R-4074). TE-21 approved (R-4075): the Trace Timeline and this timeline are ONE panel. TE-22 approved 2026-10-25 (R-4076): the Behaviour group's four lanes over the brain's recorded histories (ui's handoff, BV-59). ⭐ HANDED to the ui lane: batches/HANDOFF_Coordinator_To_Ui_Director_Timeline.md
updated: 2026-10-26 (§2c — the review timeline: the sim axis, a left click only selects, the director's gestures merged with DAR §10c — R-4087) · 2026-10-26 (§9c — R-4078 one lane seam, the editor's ITraceLaneProvider moved to the shared toolkit; R-4079 lanes are registered definitions, the shipped ones too; TL-3 and the hard-coded lanes SUPERSEDED) · 2026-10-26 (§9b — the coordinator's review of R1-a…c: accepted, three gaps for R2 / R1-d, two questions for the user) · 2026-10-25 (TE-22 approved, R-4076 — the Behaviour lanes Changes / Orders / Reports / Decisions; the exercise group "Orders" renamed Tasks) · 2026-10-23 (TE-21 approved, R-4075) · 2026-10-23 (third pass — TE-3b approved, R-4074; TE-21 added after finding the designed, unbuilt Trace Timeline; handed to ui) · 2026-10-23 (second pass — the approval and three amendments: §0, §2a, §2b, §4c, §5, §6, §7, §8) · 2026-10-23 (first version — coordinator, from the user's request and their answers the same day)
current-answer: ⭐ §2e THE PANEL AND TOOLBAR ON A CLUSTER HOST — live / review / replay (ui, 2026-10-26, READY-TO-BUILD) · §10 R2-T — THE DIRECTOR'S TRACKS: the build plan (ui, 2026-10-26, DESIGN) · §2c THE INTERACTION RULES AFTER DAR (R-4087 — supersedes §2/§2a/§2b gestures) · §9c LANES REGISTERED (R-4078, R-4079) + §9d ITS AS-BUILT · §9 R1 BUILD PLAN + §9a R1-a…c AS BUILT (2026-10-26, measured: D1 the browser seeks through FederatedReplayManager, D2/D3 the panel lives in Fdp.Presentation — TL-1…TL-7, slices R1-a…e) · §0 the rulings and leans · §1 the picture · §2 the workspace · §2a the timeline on every host · §2b the event index · §3 classes · §4 sequences · §5 the take format v2 · §6 phases · §7 claim table · §8 INVENTORY
stale-below: none
known-rot: §2 Markers "double-click jumps there", §2b "drag to pan; drag on the ruler to zoom to a range", §2a's Replay Browser "a click in the past seeks", §9a's built "click seeks" and TL-4 "time is frame index + wall ticks" — SUPERSEDED by §2c (R-4087, R-3059) · gap ① (§9b: the builder decodes FrameSimTime) — SUPERSEDED: the v7 frame index carries the sim time (DAR P0, §8 rows 1–3, 11) · TE-19's and §2a's "a live host never seeks" — SUPERSEDED by R-3060 (the review, DESIGN_Replay_Time_Axis_And_Review §9) · §9c's `LaneProjection` is the model's `ResultProjectionDto`, and its Life lanes are structural (not `LifecyclePredicateDto`) — §9d · §9a's lane enum, extractors and `TimelineLane` mentions are SUPERSEDED by §9c/§9d · §9's TL-3 (ITimelineRowProvider + adapter) and the hard-coded lanes of §9 / §9a (TimelineLane, TimelineLaneGroup, the three extractors) are SUPERSEDED by §9c (R-4078, R-4079) · §1, §3 and §4b draw ReplayDirectorHost seeking through ReplayBrowserContext.SeekToFrame — overtaken by D1 the same way: it binds to the panel's ITimelineCursor (§9b ②) · §2a's ReplayBrowserTransportAdapter → ReplayBrowserContext.SeekToFrame and "TimelinePanel (NEW, Hrot.Presentation)" are overtaken by §9 D1 / D3 (TL-1, TL-2) · TE-4's first wording (a scout per take, AnchorBook watching the sandbox) and TE-3's first wording (three new anchor kinds) are SUPERSEDED the same day by the amended rows; nothing else quotes them
known-conflict: none
related-designs:
  - DESIGN_Replay_Time_Axis_And_Review.md — OWNS the SIM axis (FrameSimTime in the frame index, paused stretches), the review (DAR) a live host's right-click seek enters, and §10 the panel's defects and input map (CE-3330); amended here in §2c (R-4087)
  - DESIGN_Replay_Browser_Debug_Http.md — GET /timeline/lanes, /timeline/events, POST /timeline/lanes over this design's index (RB-6), and seek to the next event in a lane
  - docs/DESIGN_Unit_Camera.md — the follow camera (orbit → shoulder → eyes) is a candidate shot type for the camera track
  - DESIGN_Film_Director.md — OWNS the take and reel formats, DirectorRunner, the stepping rule (R-3038), capture and the sound track; this design adds the EDITOR, the recording source, keys, tracks and promote. Its §3e / D-18 ("final films later from a recording") is the path this design builds
  - designs/replay-browser-2/DESIGN.md — OWNS the Replay Browser, its sandbox world (ReplayBrowserContext), its timeline panel and the headless RecordingSearchService whose frame-step loop the recording scout reuses (TE-4)
  - DESIGN_Scenario_Regression.md — OWNS the Verify mode and checks.json (the take anchor grammar extended); TE-10 adds still keys captured as regression artefacts
  - DESIGN_Agent_Shared_View.md — OWNS the /replay/* routes over one editor recording (R-1029) and the rule "a route only when no existing route answers" that TE-15 follows
  - DESIGN_Map_3D_Mode.md — OWNS the film camera (FilmStudio, FilmDirector, FilmCaptureLayer) on every map host and the 2-D ⇄ 3-D switch; §6b's finding "ReplayBrowser sets no TKB" is ruled here (R-4069)
  - DESIGN_Subsystem_Composition_Unification.md — OWNS "one catalogue per node" (§4.1ae.1, HrotNodeBuilder step 9) that TE-12 extends to the Replay Browser
  - designs/audio/DESIGN_FPS_Audio.md — OWNS the spatializer and the ear; TE-11 adds the rule for sound at a playback rate other than 1×
  - DESIGN_Terrain_Combat_Tuning.md — OWNS the map layers (§5e) a View track switches, through the film's transient override (R-4060)
  - blueprints/AI_Editor_Shared_Infrastructure.md — §15 OWNS the Trace Timeline (designed, a 50-line shell today: TraceTimelineWindow + ITraceLaneProvider); TE-21 makes it and this timeline ONE panel
  - DESIGN_Behaviour_Debugging.md — OWNS the ui lane's debugging workflow (CE-1270); the timeline's unit swimlanes and trace lanes serve it (TE-21)
  - DESIGN_Entity_Brain_View.md — OWNS the brain journal; its Trace Timeline lane (BV-30, deferred) becomes the timeline's Behaviour lane
  - Predicate-Infrastructure-Capabilities.md — OWNS the condition model this design's anchors ARE (SearchPredicateDto: DTOs, compiler, StructEdit editor and drawers, JSON) — R-4072. ⚠ its §6 row "enter the polygon" is a BOX (BoundingBox2D) today
  - designs/breakpoints-1/DESIGN.md — OWNS the data breakpoints that evaluate the same predicates LIVE and their editor (DataBreakpointManagerPanel / PredicateBuilderState)
  - blueprints/DESIGN_Time_Architecture.md — OWNS cluster replay and its seek (ClusterOpType.ReplaySeek, §11) and the transport facade (ITimeTransportFacade) the timeline's transport row drives (TE-19)
  - designs/mgmt-1/DESIGN.md — OWNS the exercise archive (`exercises/<id>/` with the .fdp files and Orchestrator.json, §12) that TE-13 keeps films' recordings in
-->

# Director timeline editor — cut films in the Replay Browser, from one recording

🔒 **User, 2026-10-23:** *"I would like to talk about how to edit director's cuts (existing feature to filming clips) and whether we
could use such an editor in the replay browser where the exercise is recorded and its time line is known and we could easily jump to
desired time and position a camera to suitable position and angle by flying it in 3d map and then record it as new camera keyframe in
the clip, selecting optional type if cut (camera flies smoothly to new location or jumps immediayely with some darkening/undarkening)"*
· *"Also including skipping unimportant part of the exercise, adding subtitles to the time line, adding switching 2d and 3d maps and
some tactical map graphics, composing reels from takes."*

🔒 **User, 2026-10-23 (on the proposal):**
- *"scouting (offline recording search) can still fill the time line so we can easily see where time to jump to."*
- *"takes can be recording specific but be also used for usual scenarios as now so it can be used with live scenarios"*
- *"maybe the camera keyframes could be time-relative to some known events (like this entity fired at this target, or ammo exploded in this area, or entity crossed this phase line/entered this area...) so that if the scenario execution timing changes later the take prescription could still hold."*
- *"some of camera key frames from such a prescription) could become automatic screenshots added to the overnight regression tests as an artifact documenting the test run."*
- *"replay browser of course need to load same type database as other hosts."*
- *"films can be made from single recording only (coming from editor usually)."*
- *"Audio layer in replay browser would be nice (just not idea how to handle faster or slower that real time modes)."*
- *"cool pls record it to a design document."*
- On the five decisions: *"extend Take with a source: yes, but allow the take produced on a recording to be used with live scenario"* (with *"storing takes for replay on different place next to the recordings and promote to scenario-take only as explicit operation?"*) · *"sounds correct, you decide"* (camera keys) · *"One shared timeline panel.: for sure. also "unifying and sharing" is desired wherever to avoid duplications"* · *"sure film only; tactiocal graphics in scenario is different story"*

🔒 **User, 2026-10-26:** *"we need to unify and share, not duplicate and fix with adapters. we can move the editor code to some shared place. what are built-in lanes, timeline should be extensible, not hardcoded."*

🔒 **User, 2026-10-23:** *"TE-21 approved"*

🔒 **User, 2026-10-23 (third pass):** *"TE-3b approved, write the handoff to ui lane; tell them about the whole idea we are solving (pointing to designs); they are designong behavior debuggoing with lots of ui tasks so that they can include this one in ther plans"*

🔒 **User, 2026-10-23 (second pass):** *"looks good!"* · *"these should the already existing conditions editable using already existing conditions editor, ple reuse do not invent anything new, if something missing (area/line picking or drawing) we will extend"* · *"timeline is usefull regardless of director cut window and i guess it should become one of the most important central pieces of the replay browser as separate panel allowing for navigation.  (needs thinking how other hosts should use it, if it is part of director cut panel only there)"* · *"of course the recording scout needs to read the recorded events and cache them for fast timeline rendering - this part is important, there could be thousands of events and entities  etc so it needs smart choices to make it usable -and zoomable without clutter etc. - use your experience from other products"*

**Words used here.** A **key** is a camera keyframe: a time, a camera pose, and how the camera gets there. The **program** is the
film as cut (skips, speeds, cuts applied); the **source** is the raw recording or scenario run. The **pilot camera** is the one you fly
to author; the **program camera** is the one the film shows.

## 0. Rulings and leans

| # | | |
|---|---|---|
| ✅ **R-4067** | **A take names its source: a scenario (as today) or a recording. A film is made from ONE recording.** | user: decision 1 "yes"; "films can be made from single recording only" |
| ✅ **R-4068** | **One timeline panel and one runner on every host that plays a take: the editor and the Replay Browser. Unify and share; no second implementation.** | user: decision 3 |
| ✅ **R-4069** | **The Replay Browser loads the same type database (TKB) as every other host.** | user |
| ✅ **R-4070** | **Film graphics are film-only: stored in the take, drawn by the film, never written to a recording or a scenario.** Tactical graphics in a scenario are a separate subject | user: decision 5 |
| ✅ **R-4071** | **The director timeline editor is APPROVED** — the leans of this table, as amended below | user: *"looks good!"* |
| ✅ **R-4072** | **An anchor IS an existing condition — a `SearchPredicateDto`, edited in the existing StructEdit predicate editor. Nothing new is invented; what is missing (a polygon area, a line, picking or drawing them on the map) is an EXTENSION of that model and editor** | user, second pass |
| ✅ **R-4073** | **The timeline is a panel of its own — the Replay Browser's central navigation — useful without the director; the director adds its tracks to it. The recording's events are read once and cached so the timeline draws fast** | user, second pass |
| ⭐ **TE-2** *(decided, delegated)* | **The camera key is the unit.** A key = `at` + camera + transition in + fov + optional still. Today's `shots[]` read as keys (a shot → a cut key; its `to` → a fly key at the shot's end), so every seeded take keeps working | user: *"you decide"* |
| ⭐ TE-1 | **A recording's takes live beside the recording** (`exercises/<id>/takes/`). **Promote** is an explicit operation that copies a take to `scenarios/<s>/takes/`, drops `source`, and re-expresses each fixed-time key relative to the nearest marker before it (listed for you to confirm). ⭐ The reverse needs no operation: a scenario take plays on any recording of that scenario | rejected: a take with no source used everywhere — an unpromoted recording take would then sit in the scenario folder with recording-only times |
| ⭐ TE-3 *(amended, R-4072)* | **Keys are anchored to conditions.** `at` stays the anchor expression (`"blue-0.5"`); an anchor is **the n-th time a `SearchPredicateDto` becomes true** — stored as the predicate's own JSON (`$type` discriminator, as the breakpoint clipboard and the search presets already save it), edited by the StructEdit predicate editor (`ReplaySearchPanel` and `DataBreakpointManagerPanel` host it today). **Enter / leave an area exist** (`SpatialBoundingPredicateDto`, `BoundaryEvent.Entry / Exit`, a map-picked box). ⭐ **Extensions, of that model only:** a polygon area and a line crossing (with a side) as spatial predicates, and drawing them on the map beside the existing box picker (`BoundingBoxFieldDrawer` → `ISpatialPickerContext`) | rejected: new anchor kinds `cross` / `enter` / `leave` (the first wording) — a parallel condition vocabulary |
| ✅ TE-3b — **R-4074** | **The nine existing anchor shorthands** (`shot`, `blast`, `hit`, `near`, `task`, `side`, `health`, `embark`, `gesture` — `Take.cs:129`, also used by `checks.json`) **are read as shorthands that LOWER to predicates**, so one evaluator answers every anchor and check. A shorthand that does not lower is a missing predicate to add, not a kind to keep. ⛔ Each lowering is measured in R0 ⑦ before it is built | keep the nine as a second vocabulary: two evaluators (AnchorBook and the compiler) that can disagree |
| ⭐ TE-4 *(amended)* | **The scout is the recording's INDEX, built once per recording, not per take** (§2b). It runs **automatically the first time a recording is opened** in any host, in the background, filling the timeline as it goes, and is cached beside the recording; every later open reads the cache (milliseconds). A take's anchors are QUERIES over the index; a predicate the index cannot answer (a new area drawn later) runs one targeted pass of the search service, narrowed by the index's position track, and its hits are added to the cache | rejected: scouting on every open (minutes per open on a long recording) · scouting per take (the same scan repeated for every take) |
| ⭐ TE-5 | **The runner moves to the shared assembly.** `DirectorRunner` and `DirectorWindow` move from `Hrot.Editor/Director` to `Hrot.Presentation/Map3D/Director` (their usings are already only director, window-manager and ImGui types). A new `ReplayDirectorHost : IDirectorHost` plays a recording; `IDirectorHost` gains `Seek` / `CanSeek` (default not supported, like `WriteCheckpoint`) | rejected: a replay copy of the runner — R-4068 |
| ⭐ TE-6 | **The program is one pure function:** `TakeTimeline.ProgramAt(filmTime)` → source time, camera pose, fade, subtitles, view, graphics, audio rate. The panel's scrub, the runner, capture and the regression stills all call it | rejected: the panel computing its own preview — it would drift from the film |
| ⭐ TE-7 | **Skips and speeds are the Time track**: segments kept at a speed (0.25× … 8×); a gap is crossed by a cut, a dip, or a fast-forward with an on-screen clock (*"+12 min"*). On a recording a skip is a seek. ⚠ On a scenario source a skip still steps one step per frame (R-3038), so it costs wall time: it shows a card, and long skips are why films come from recordings | — |
| ⭐ TE-8 | **Transitions: cut · fly (a smooth path through consecutive fly keys, eased) · dip (to black or a colour and back, two durations).** Crossfade later: it needs two renders | — |
| ⭐ TE-9 | **Graphics track** (R-4070): arrow, ring on a unit, route line, area, label, each with in/out and fade, drawn by a film overlay layer that reuses the tactical-graphic drawing (⛔ which class draws it: measure in R0) | — |
| ⭐ TE-10 | **Still keys become regression artefacts.** A key with `"still": "<name>"` is captured as a PNG by the Verify run (`DESIGN_Scenario_Regression.md`) into its report folder. An artefact that documents the run, ⛔ not a gate (no image comparison) | rejected: a separate screenshot list — the camera would be authored twice |
| ⭐ TE-11 | **Sound at any rate, one rule live and offline:** an event sound (shot, blast) starts at its film time at natural pitch; a continuous sound (engine) plays at natural pitch; above 2× only detonations sound, ducked; paused is silent. The offline track (`SoundTrack.RenderWav`) already maps each cue to film time, so it follows the same rule | rejected: resampling the mix (chipmunk / drone) · silence at every rate but 1× |
| ⭐ TE-12 | **The TKB (R-4069) is made the way every node makes it:** `HrotEnvironment.CreateTkb()` set as the world singleton (`HrotNodeBuilder.cs:262`), filled by the same knowledge-base load step from the recording's scenario (named in the exercise's `Orchestrator.json`) | rejected: a Replay Browser-only loader — a second catalogue path (CE-4013 D4/D5 removed four) |
| ⭐ TE-13 | **A film's recording is kept in the exercise archive layout** (`exercises/<id>/` with its `.fdp`, `Orchestrator.json` and the archived scenario). An editor run writes `/tmp/FDP_Temp/exercises/<id>/`; **"Keep for films"** copies it into the archive, and its takes live there | ⛔ whether an editor run is archived today: measure in R0 |
| ⭐ TE-14 | **Undo/redo is a snapshot of the take per edit**, kept by one of the four existing undo stacks (Blueprints `CommandHistory`, the two `GizmoUndoStack`s, NodeEditor `UndoStack`). ⛔ No fifth | — |
| ⭐ TE-15 | **HTTP** (R-1029's rule: a route only when none answers): the existing `/director/take`, `/director/play` and `/director/scout` take `recording=` beside `scenario=`; one new `POST /director/promote`. The MCP catalog is regenerated in the same commit (R-1030) | — |
| ⭐ TE-17 *(R-4073)* | **One Timeline panel, shared by every host** (`Hrot.Presentation`), over an `ITimelineSource`: the **recording** source (the index; seekable) and the **live** source (the same extractors run on the live world each frame, appended; history only). It **replaces** the Replay Browser's scrubber row (`ReplayTimelinePanel` keeps its export options). The director window becomes bin + inspector; its tracks are **contributed** to the Timeline when a take is open (§2a) | rejected: the timeline inside the director window (the user: useful without it) · a second scrubber beside the old one |
| ⭐ TE-18 | **Draw cost follows pixels, not events** (§2b): a level-of-detail pyramid per lane (counts, top priority, one representative per bucket) like an audio editor's peak file or a map's tiles; density strips when crowded, glyphs when sparse, clusters with a count, priority on top | rejected: drawing every event (thousands per lane per pixel when zoomed out) |
| ⭐ TE-19 *(⛔ its "editor and live cluster: `CanSeek = false`" SUPERSEDED `2026-10-26` by R-3060 — a live host seeks by entering a review, [DESIGN_Replay_Time_Axis_And_Review §9](DESIGN_Replay_Time_Axis_And_Review.md))* | **One transport for every host:** `ITimeTransportFacade` gains `CanSeek` + `Seek(t)`. Replay Browser: a new adapter over `ReplayBrowserContext`; cluster replay: the existing `ClusterOpType.ReplaySeek` round; editor and live cluster: `CanSeek = false` (R-3034: never rewind) — the past is a history to read | rejected: the timeline calling each host's seek itself |
| ⭐ TE-20 | **The event list reads the same index.** The shared Event Browser (`EventBrowserPanel`) shows the index around the playhead in a recording, so the list and the timeline cannot disagree | rejected: a second scan for the list |
| ✅ TE-21 — **R-4075** | **The Trace Timeline IS this timeline.** `AI_Editor_Shared_Infrastructure.md` §15 designed a shared, swim-lane timeline for the selected unit (lanes registered by `ITraceLaneProvider`, scrub in replay, click jumps, hover tooltip, right-click *Set Breakpoint Here*, a search box that dims); it is built only as a shell (`TraceTimelineWindow`, 50 lines, *"No trace data."*), and its first lane was deferred (BV-30). ⇒ **ONE panel:** `TimelinePanel` is §15 grown — the exercise lanes from the index, plus a pinned unit's swimlane whose rows are its trace lanes; **the row seam is the existing `ITraceLaneProvider`, generalised** (the director's tracks and search hits register through it); the `ITimelineTrackProvider` of the second pass is WITHDRAWN. `TraceTimelineWindow` becomes a host of the shared panel in the AI perspectives | rejected: two timelines (an exercise one and a trace one) — the same time axis, scrub and seek twice |
| ✅ TE-22 — **R-4076** | **The Behaviour lane group reads the brain's recorded histories** (🔒 user, `2026-10-25`: *"approve all 5"*; asked by ui, [handoff](blueprints/batches/HANDOFF_Ui_To_Coordinator_Timeline_Behaviour_History.md), BV-59). ① **four lanes**, one per source — **Changes** (`BrainJournal`, the entry's kind as glyph), **Orders** (`CommandOrderHistory`, sent and received, the why in the hover), **Reports** (`CommandReportHistory`, in and out, with fate), **Decisions** (a `UtilityDecisionLog` winner switch) · ② the index **diffs each ring frame to frame and emits only NEW entries**, keyed by the entry's own counter (`BrainJournal.Seq`; a history entry's time + counterpart), so a ring's eviction never loses an event the index took · ③ a Brain-tab row's *Seek here* calls the timeline's `Seek(time, unit)` (TE-19's transport); absent live · ④ the four lanes live on the **pinned unit's swimlane** (TE-21); Orders / Reports only on a commander · ⑤ *"where he changes his mind"* is the existing `BrainJournalBreak.Condition` in the ONE condition editor (R-4072), its hits on the Behaviour lanes. ⭐ The exercise-level group formerly called **Orders** (a unit's `MissionPlan` task states) is renamed **Tasks** — 🔒 user: *"Missions"* clashes with the mission-plan concept; two "Orders" would clash with ①. Built by ui with R1 (CE-1302) | rejected: one mixed Behaviour lane (a commander's traffic drowns his own changes) · re-reading every ring every frame (one entry emitted 32 times) · the tab driving `PlaybackController` (a second seek path) · every unit's lanes at once · a timeline-only filter |
| ⭐ TE-16 | **Reels are unchanged**; an item may name `{recording, take}`. The reel composer is the bin plus a sequence row in the same panel (reorder, trim, transition per item) | — |

## 1. The picture — who calls what each frame

```mermaid
graph TD
  subgraph RB["Replay Browser (Hrot.ReplayBrowser)"]
    RBU["ReplayBrowserSubsystem.Update"]
    RDH["ReplayDirectorHost (NEW)"]:::new
    CTX["ReplayBrowserContext (sandbox, SeekToFrame)"]
  end
  subgraph ED["Editor (Hrot.Editor)"]
    EDU["EditorSubsystem.Update"]
    EDH["EditorDirectorHost (exists)"]
  end
  subgraph SH["shared (Hrot.Presentation / Map3D / Director)"]
    RUN["DirectorRunner (MOVED)"]:::moved
    WIN["DirectorWindow: bin + inspector (MOVED)"]:::moved
    TLP["TimelinePanel (NEW, R-4073)"]:::new
    TL["TakeTimeline.ProgramAt (grown)"]:::moved
    AB["AnchorBook (grown: cross, enter, leave)"]:::moved
    FG["FilmGraphicsLayer (NEW)"]:::new
  end
  subgraph EX["existing, unchanged"]
    FS["FilmStudio → FilmCaptureLayer (every map host)"]
    RSS["RecordingSearchService frame loop"]
    TKB["HrotEnvironment.CreateTkb + KB load step"]
  end
  RBU -->|"Tick, NEW edge"| RUN
  EDU -->|Tick| RUN
  RUN --> RDH --> CTX
  RUN --> EDH
  RUN --> TL
  RUN --> AB
  RUN --> FS
  RUN --> FG
  WIN --> RUN
  WIN -->|"contributes tracks"| TLP
  AB -.->|"targeted search, 4e"| RSS
  RB -->|"NEW: R-4069"| TKB
  classDef new fill:#d9f2d9,stroke:#2a7a2a
  classDef moved fill:#fff2cc,stroke:#a68a00
```

*What the picture shows that prose hid: there is still ONE runner and ONE camera path. The only new frame edge is the Replay Browser
ticking the runner; everything a film needs below the runner already exists on every map host. The dotted edge is a reuse of the
search service's stepping loop, not of its predicates.*

## 2. The workspace

![Director workspace](img/director-timeline-workspace.svg)

| track | holds | key operations |
|---|---|---|
| **Markers** | the scout's events + bookmarks (TE-4) | snap target; double-click jumps there |
| **Time** | kept segments, their speed, how each gap is crossed (TE-7) | ripple delete, speed, skip style |
| **Camera** | keys (TE-2) with their transition in (TE-8) | **K** adds a key from the pilot camera; update from view; go to key; tie to the selected unit (stored relative, so it follows a moving vehicle); path preview in 3-D |
| **Subtitles** | caption · title card · unit label · exercise-clock stamp; position; fade | the old per-shot `caption` reads as one item |
| **View** | 2-D ⇄ 3-D (the existing animated switch) and layer presets | through the transient override (R-4060) |
| **Graphics** | arrow, ring, route, area, label (TE-9) | drawn in world space, in/out, fade |
| **Audio** | on/off and gain per segment; cues at film time (TE-11) | music and voice-over later |

**Practical must-haves** (from Unreal Sequencer, Resolve, and the replay editors of racing and shooter games): pilot ⇄ program camera on
one key · keys tied to a unit · J/K/L shuttle, frame step, I/O · snapping · copy/paste/nudge keys · lock/mute/solo a track · undo/redo
and autosave (TE-14) · 16:9 safe frame, letterbox, thirds · film length and exercise clock · **shot templates from a marker**
(right-click a shot → over the shooter 0.5 s before, then the target, then the result; reuses D-10's draft rules) · a key whose unit is
gone holds its last pose · validation lists missing units and a stale recording.

## 2a. The timeline — a panel of its own, on every host (R-4073, TE-17, TE-19)

```mermaid
graph TD
  subgraph HOSTS["hosts"]
    RBH["Replay Browser"]
    EDH2["Editor"]
    CLH["CGF / SimHost / IG (map hosts)"]
  end
  TLP2["TimelinePanel (NEW, Hrot.Presentation)"]:::new
  subgraph SRC["ITimelineSource (NEW)"]
    RSRC["RecordingTimelineSource: the index file, complete, seekable"]:::new
    LSRC["LiveTimelineSource: the same extractors on the live world, appended"]:::new
  end
  subgraph TRK["ITraceLaneProvider (exists, generalised — TE-21): extra rows"]
    TRC["BTree / HSM / Utility trace lanes (exist) + the Brain lane (BV-30)"]
    DIR["Director: Time, Camera, Subtitles, View, Graphics, Audio"]
    SRCH["Search results (ReplaySearchPanel)"]
    BKM["Bookmarks + back/forward history (PlaybackHistoryTracker)"]
  end
  subgraph TR["ITimeTransportFacade (exists) + CanSeek / Seek (grown)"]
    RBA["ReplayBrowserTransportAdapter (NEW) → ReplayBrowserContext.SeekToFrame"]:::new
    CTA["ClusterTimeTransportAdapter (exists) → ClusterOpType.ReplaySeek in a cluster replay"]
    ETF["EditorTimeTransportFacade (exists), CanSeek = false"]
  end
  RBH --> TLP2
  EDH2 --> TLP2
  CLH --> TLP2
  TLP2 --> RSRC
  TLP2 --> LSRC
  TLP2 --> TRK
  TLP2 --> TR
  EVB["EventBrowserPanel (exists, shared)"] -->|"reads the same source (TE-20)"| SRC
  classDef new fill:#d9f2d9,stroke:#2a7a2a
```

*What the picture shows that prose hid: the timeline knows nothing about any host. A host chooses a source and a transport; the
director, the search, the bookmarks and a unit's trace lanes are only extra rows (TE-21). That is what lets the Replay Browser make it central while the editor and
the cluster hosts show the same panel.*

| host · mode | source | seek | a click in the past |
|---|---|---|---|
| ⭐ **Replay Browser** | recording (the index) | ✅ local sandbox | seeks |
| **a node in a cluster REPLAY** | recording (its own `.fdp`, the same index) | ✅ through the master's `ReplaySeek` round | seeks the cluster |
| **editor preview, a live cluster** | live (appended) | ⛔ never rewind (R-3034) | shows the event; "Open in Replay Browser" when the run was recorded |
| **editor, a director take playing** | live + the take's tracks | ⛔ | the director's own controls (Play / Next) |

## 2b. The event index — read once, cached, drawn by pixels (TE-4, TE-18, TE-20)

**What goes in.** One background pass over the recording, on the search service's own isolated context (replay-browser-2's isolation
invariant: the GUI's sandbox is never touched, so a background thread is safe here):

| lane group | lanes | read from |
|---|---|---|
| **Combat** | shots, hits, detonations | recorded transient events (the event scanner, `IEventScannerCompiler`) — ⛔ which event types are recorded: R0 ② |
| **Life** | spawned, killed / destroyed | lifecycle (`GetDestructionLog`) and `Health` crossing zero |
| **Behaviour** *(TE-22)* | **Changes** · **Orders** · **Reports** · **Decisions** — per pinned unit; Orders / Reports on a commander only | `BrainJournal` (new `Seq`) · `CommandOrderHistory` · `CommandReportHistory` (newest entry changed; key = time + counterpart) · `UtilityDecisionLog` (winner switch) — all recorded (CE-1319); `BehaviorState` changes for a unit with no brain |
| **Movement** | area entries / exits, line crossings | each entity's **position track** (below) against the scenario's areas and lines |
| **Tasks** *(was "Orders", renamed by TE-22)* | a unit's mission-plan tasks: active, done, failed, skipped | `MissionPlan.Tasks[].State` (`eTaskState`) changes |
| *rows added by providers* | search hits, bookmarks, the director's tracks | §2a |

**What the cache holds** — a sidecar beside the recording (`<recording>.timeline`), invalidated by its header (the recording's size,
its last frame's ticks, the index schema version):

| table | per row | why |
|---|---|---|
| **events** | frame, lane, subject id, other id, x, y, one value | the glyphs, the tooltips, the queries — columns, so a lane is one contiguous array |
| **entities** | network id, name, type, side, spawn and despawn frame | names and side colours without opening the recording |
| **position track** | per entity, one sample per second, quantised | a NEW area or line is answered from the cache, then refined by a short local scan from the nearest keyframe (≤ 1 s) — no full rescan |
| **pyramid** | per lane, per level (each level halves the resolution): count, top priority, one representative event | drawing costs pixels × lanes, never events |

📐 Sizes are estimates to measure in R0 ⑧: an hour at 60 Hz is 216 000 frames; 100 000 events × ~24 bytes ≈ 2.4 MB; 1 000 entities ×
3 600 position samples × 8 bytes ≈ 29 MB (moving entities only, delta-coded, is much less).

**How it draws without clutter** — what works in Perfetto/Tracy (profilers), audio editors (peak files), video editors (marker
lanes), Grafana (annotations) and game replay timelines (kill markers):

| rule | |
|---|---|
| **density when crowded, glyphs when sparse** | more than ~1 event per pixel ⇒ a density strip (log-scaled intensity, side colour); fewer ⇒ an icon per event |
| **cluster, never overlap** | glyphs closer than their width merge into one with a count (*×37*); its tooltip lists them grouped by kind |
| **priority on top** | death > detonation > hit > shot > behaviour > movement; a cluster shows its highest |
| **collapsible lane groups** | Combat, Life, Behaviour, Movement, Tasks; collapsed, a group is ONE density strip |
| **focus, not filter** | "selected unit" dims every other event instead of hiding it, so context stays; a pinned unit gets its own swimlane (alive span, its shots, hits taken, behaviour bars, like a Gantt row) |
| **the overview strip** | the whole recording always visible above, with the zoomed window as a bracket you drag |
| **zoom about the cursor** | Ctrl+wheel; drag to pan; drag on the ruler to zoom to a range; the ruler's ticks adapt (hours … frames) with the exercise clock |
| **jump, don't hunt** | next / previous event in the lane under the cursor; a search box filters lanes from the index, not the file |
| **progressive** | while scanning, the scanned part is drawn and the rest shaded; the timeline is usable from the first second |

📐 **Budgets** (to hold in R1): the timeline draws in < 1 ms per frame at any zoom; a cached index opens in < 200 ms for an hour;
the first scan's rate is measured in R0 ⑧ — it bounds how soon the first open is useful, not whether.

## 2c. The interaction rules after During Action Review *(R-4087, coordinator, `2026-10-26`)*

🔒 **User, `2026-10-26`:** *"yes write the addendum note, and approved"* · R-3059: *"clicking timeline itself can not start review (accidental
click)"*. ⭐ **The input map is [DAR §10c](DESIGN_Replay_Time_Axis_And_Review.md)** (wheel, drags, double-click, right-click; zoom limits;
the ruler) and DAR §10b (follow, fit, range, hover, snapping, zoom history) — for EVERY host, the Replay Browser included (only its
toolbar is its own, R-3060). This section owns only what this design adds to it. Dispatched to ui:
[NOTE_Coordinator_To_Ui_Review_Timeline_Addendum](blueprints/batches/NOTE_Coordinator_To_Ui_Review_Timeline_Addendum.md) beside backend's
[NOTE_Backend_To_Ui_Review_Timeline](blueprints/batches/NOTE_Backend_To_Ui_Review_Timeline.md).

| # | rule | supersedes |
|---|---|---|
| 0 | **TL-4 grown: time has two axes.** A frame keeps its wall ticks AND its sim time (the v7 frame index — DAR P0); positions, the ruler and seeks are on the session's active axis (S / W, R-3058) | TL-4 (wall only); gap ① (decode `GlobalTime` in the builder) |
| 1 | **a left click only SELECTS** (an event, a key); seeking is the right-click menu and, in a review / replay / the Replay Browser, the playhead drag | §2a "a click in the past seeks", §9a "click seeks" |
| 2 | **director tracks take a left drag**: on a Time / Camera track a click selects a key or segment and a drag moves it — an edit of the TAKE (TE-14 undo), never of the world | — (DAR §10c's "nothing on empty space" still holds on empty space) |
| 3 | **one keymap** (panel focused): Space · ←/→ frame · Shift+←/→ event · Home / End · +/− · **K adds a camera key** (TE-2) · J / L slower / faster · I / O range in / out; ⛔ on a LIVE host the arrows move the selection, never seek | §2 Camera row's J/K/L shuttle (K is the key) |
| 4 | ⛔ no gesture seeks by itself: double-click = zoom to the cluster, a ruler drag = pan, Shift+drag = a range | §2 Markers "double-click jumps there", §2b "drag on the ruler to zoom to a range" |
| 5 | **the right-click menu is the mode's seek items + what the row under the mouse contributes** (*Set breakpoint here* — TE-21, *Add key here*, *Add bookmark*) through its provider — ⛔ no switch on the row's kind | — |
| 6 | **a selected event selects its unit through the ONE selection** (`SelectionRequestSystem`, as `POST /selection` — R-4083) | — |
| 7 | **`LiveTimelineSource` runs `TimelineIndexBuilder`'s per-frame step** on the live world (the same `IndexedLaneDefinition`s) — a live index equals the index rebuilt from its recording, lane by lane | — |
| 8 | **the end-of-run sidecar is written after the recorder closed the file**, by `TimelineSidecar` (TL-6's header holds the byte size) | — |
| 9 | ⚠ **"Loop range" in a review / replay is the master's clock**, not the panel's — absent until backend's `ReplayClockProcessManager` carries it (the Replay Browser may loop locally) | — |
| 10 | **a take's keys stay in SIM seconds**; on the Wall axis drawn through the session's `ReplayTimeMap`; never inside a paused stretch | — |
| 11 | **events inside one collapsed pause** (Sim axis) draw as one cluster with a count at the pause's tick | — |

## 2d. CE-3330 — the panel's zoom, pan and menu, as built *(ui, `2026-10-26`; build-state: BUILT — the parts that need backend's data D wait, below)*

```mermaid
classDiagram
  direction LR
  class TimelinePanel { <<exists, reworked>> input map DAR §10c · sticky ruler + overview, lanes in a child · menu · hover line · range · keys }
  class TimelineView { <<NEW, pure>> Window · Extent · Follow · Range · History · OnExtent() · Zoom() · Pan() · Fit() · Last(span) · KeepInView(now) · Back() }
  class TimelineRuler { <<NEW, pure>> Ticks(window, width, extent, measure) major + minor, measured labels, h:mm:ss / m:ss.f / +frame }
  class TimelineLayout { <<exists>> Marks · ZoomAbout · Pan · Clamp · Bracket · Snap (NEW) · NearestEvent (NEW) }
  class TimelineMenuItem { <<NEW, record>> Label · Run · Enabled }
  class TimelineMenuContext { <<NEW, record>> LaneId · Ticks · Frame · Event? }
  class ITimelineLaneProvider { <<exists>> + MenuItems(context) default empty — a row contributes (addendum 4) }
  class ITimelineCursor { <<exists>> Extent · Now · CanSeek · Seek + IsPlaying · TogglePlay (default members) }
  class FederatedReplayCursor { <<exists>> + play / pause through the host's transport }
  class EventTimelineWindow { <<exists>> + onSelect, provider lookup }
  class ReplayBrowserSubsystem { <<exists>> binds onSelect to SelectionChangeRequest (RB-7's path) }
  TimelinePanel --> TimelineView
  TimelinePanel --> TimelineRuler
  TimelinePanel --> TimelineLayout
  TimelinePanel --> ITimelineCursor
  TimelinePanel ..> TimelineMenuItem : the mode's seek items + the row's
  ITimelineLaneProvider ..> TimelineMenuItem : MenuItems(TimelineMenuContext)
  FederatedReplayCursor ..|> ITimelineCursor
  EventTimelineWindow --> TimelinePanel
  ReplayBrowserSubsystem --> EventTimelineWindow
```
```mermaid
graph TD
  EDU["EditorSubsystem.Update, after the kernel tick — _node.StepTimeline() (the editor runs its own frame)"] --> LTS["HrotNodeContext.Timeline — ONE LiveTimelineSource per node context (editor included)"]
  LTS --> FS["TimelineFrameScanner.Step (the lanes the catalogue registers)"]
  BLD["TimelineIndexBuilder (background, a recording)"] --> FS
  RBU["ReplayBrowserSubsystem.SyncTimelineSource"] --> BLD
  WIN["EventTimelineWindow.DrawClientArea (editor_event_timeline / rb_event_timeline)"] --> PAN["TimelinePanel.Draw"]
  API["GET /timeline/lanes, /timeline/events"] --> LTS
  NT["HrotNodeContext.Tick — the ONE node frame (CGF / SimHost / IG / Traffic / Stride), after afterKernel, before the swap"] --> LTS
  NAPI["a node's GET /timeline/* (EcsNodeDebugProvider.For → TimelineAccess)"] --> LTS
  SC["the end-of-run sidecar (row 33)"]:::dead -.->|"backend's, after FinalizeLive"| BLD
  classDef dead stroke:#c33,stroke-dasharray:4 3,color:#c33
```
*What the picture shows that prose hid: ONE per-frame step under both sources — so "live lanes = recorded lanes" holds by construction — and
the dead edge: the sidecar at the end of a live run is backend's. ⭐ The cluster nodes' live source sits in the node frame itself, so no host can lack it (one catalogue, `ShippedTimelineLanes.Catalog()`, for the editor, the Replay Browser and every node).*

*What the picture shows that prose hid: every rule of DAR §10 that is not drawing — follow, fit, presets, history, the zoom limits, the ruler's
steps and labels, snapping — is in two PURE classes (`TimelineView`, `TimelineRuler`), so the panel only maps input to their calls and
they are tested without ImGui; and the menu is one list the mode and the row under the mouse both add to, so no lane kind is named in the panel.*

```mermaid
sequenceDiagram
  actor U as user
  participant P as TimelinePanel
  participant V as TimelineView
  participant C as ITimelineCursor
  participant H as host (onSelect)
  U->>P: left click on a mark
  P->>H: onSelect(event) — the ONE selection (SelectionChangeRequest), never a seek
  U->>P: right click
  P->>P: menu = mode items (Seek here, Seek to event, prev / next, Play from here, Copy time) + the row's provider items
  U->>P: Seek here
  P->>C: Seek(snapped ticks)
  loop each frame
    P->>V: OnExtent(from, to) — tail follows the live edge, else the window is KEPT (T1)
    P->>V: KeepInView(now) while playing — page when the playhead leaves (T12)
  end
```

| DAR §10 / R-4087 | as built | rail (`TimelineInteractionTests` unless named) |
|---|---|---|
| T1 view reset | `TimelineView.OnExtent`: the first extent fits, a fitted view stays fitted, a view at the end TAILS (span kept), any other is clamped; a window shown before the first extent is kept | `CE3330_T1_*` ×2 (+ `TimelinePanelFrameRail`, which caught the "shown before" case) |
| T2 / T3 click seeks, drag pans | a left click SELECTS the nearest event (`OnSelect`); a left drag on empty lanes does nothing; seeks are the menu, the ▼ scrub (on release) and the keys | `CE3330_TheMenu_*` (a seek only when an item runs); RB `CE3330_ATimelineEvent_SelectsItsUnit_NeverSeeks` |
| T4 / T5 / T6 wheel | the wheel over the ruler / overview zooms about the mouse by `1.0015^(−Δ×120)`; over the lanes it scrolls them (Ctrl zooms); Shift+wheel, the horizontal wheel and a middle drag pan | `CE3330_T5_T7_*` |
| T7 closest zoom | `MinSpanFrames` = 10 frames at the index's median frame spacing | `CE3330_T5_T7_*` |
| T8 ruler | `TimelineRuler`: 0.01 s … 12 h steps, five minors per major, labels measured and skipped on collision, `h:mm:ss` past an hour, `+frame` at the closest zoom | `CE3330_T8_*` |
| T9 overview edges | the bracket's edges drag to zoom (resize cursor), its inside moves it, a click outside jumps it | — (ImGui input; read) |
| T10 stuck drag | a release anywhere ends the drag | — (read) |
| T11 sticky ruler | the lanes draw in a child window; the overview and the ruler stay | `TimelinePanelFrameRail` draws it |
| T12 follow | `KeepInView` pages the window (playhead 10 % in) while `ITimelineCursor.IsPlaying`; an arrow at the edge where it left, click = reveal | `CE3330_T12_*` |
| §10b fit · presets · range · history · hover · snapping · keys | F / double-click the ruler = fit; 0–3 = 30 s / 2 min / 10 min / all; Shift+drag = a range (duration shown; I / O set it; Esc clears); Backspace = back; a hover line with its time; seeks snap to an event within 7 px, else a frame; Space / ←→ / Shift+←→ / Home / End / + − — ⭐ arrows move the SELECTION where the cursor cannot seek (live) | `CE3330_FitPresetsRangeAndHistory`, `CE3330_Snapping_*` |
| §9b menu + addendum 4 | `TimelinePanel.MenuItems` (pure): Seek here / Play from here / Seek to this event / previous · next in the lane where `CanSeek`; zoom / clear the range; Copy the time — then `TimelineLaneCatalog.MenuItems` (the row's provider, `ITimelineLaneProvider.MenuItems`) | `CE3330_TheMenu_*`, `CE3330_TheRowUnderTheMouse_*` |
| addendum 5 selection | `EventTimelineWindow(onSelect, rowMenu)`; the Replay Browser binds `SelectTimelineEvent` → `SelectionChangeRequest` (RB-7) and play / pause to its transport | RB `CE3330_*` |
| ⭐ **addendum 6 / DAR row 28 — the live source** | `TimelineFrameScanner` = the builder's per-frame step, extracted; `TimelineIndexBuilder` runs it per played frame, `LiveTimelineSource` per live sim frame (`GlobalTime.FrameNumber` advancing; a paused world adds nothing; a new world / a rewind / a changed catalogue starts afresh; a snapshot at most every 0.25 s). `TimelineLaneContext.Live` (row 30) — the shipped indexed lanes apply when `Indexing` | `TimelineIndexTests.CE3330_ALiveIndex_EqualsTheIndexRebuiltFromItsRecording_LaneByLane`, `CE3330_TheLiveSource_*` |
| 🔴→⭐ **found by the parity rail** | on a LIVE world the recorder clears the destruction log INSIDE the tick, before the live step reads it ⇒ the Left lane missed every death. `StructuralScanner` now also reports a tracked entity that is no longer alive (once; playback unchanged — its log removed it first) | the parity rail (red-proved: without the sweep the death is missing) |
| ⭐ **R1-e — the editor hosts the panel** | `editor_event_timeline` (closed by default; Window menu) over its NODE CONTEXT's `LiveTimelineSource` (`_node.Timeline` / `TimelineLanes`, the access `EcsNodeDebugProvider.TimelineOf` — the same owner as every cluster node; the editor calls `StepTimeline()` from its own frame) with a `LiveTimelineCursor` (extent = the index, never seeks, Space through the editor's transport); a clicked event → `SetSelection2D` (the one selection); `GET /timeline/lanes \| events` read the live index (`DebugApiService.AttachTimeline`) | the live run below |
| ⭐ **the cluster nodes' live timeline** (the module diagram's former dead edge) | `HrotNodeContext.Timeline` (a `LiveTimelineSource` over `ShippedTimelineLanes.Catalog()`, built on first use) stepped inside `HrotNodeContext.Tick` after the host's post-kernel work and before the swap ⇒ CGF, SimHost, IG, Traffic and Stride all index by construction; `EcsNodeDebugProvider.For` serves it (`timeline.read`). The editor and the Replay Browser compose the same catalogue. ⚠ **cost, measured (1000 units, Release):** 1.7 → **≈ 1.0 ms / frame busy, ≈ 0.5 ms idle** after CE-1361 (ring lanes skip a byte-identical ring and read it in place; the structural lanes track by index + generation, no `IsAlive` sweep); the rest is the per-unit walk; a publish is O(1) — the snapshot SHARES the scanner's grow-only buffers up to its count (`SharedList`), never a copy (CE-1361 ②). ⭐ ALWAYS ON by user decision (`2026-10-26`); filling a node's index on first use from its recording is CE-1362 | `HrotNodeBuilderTests.CE3330_Node_*` ×2, `TimelineIndexTests.CE3330_Node_TheShippedCatalog_*`, RB `CE3330_Node_*` |
| ⚠ **one frame late** | the live step runs after the kernel tick, when the bus holds the PREVIOUS tick's events: a live event is one frame (≈ 16 ms) later than the recording files it | — |
| ⭐ **T13 / R10 — the Sim axis** (backend P0/P1: `ReplayTimeMap`, `FrameMetadata.SimTime` / `Flags`) | `TimelineAxis` (pure): Wall = the identity; `Sim(map)` projects the index's frame ticks, the extent, "now" and every seek through the map (`AxisCursor` — the host keeps seeking by wall ticks, TL-2); a pause collapses to one instant (its events cluster there — row 11) and a seek to it lands on its LAST frame (`Edge.AfterStretch` — what changed is shown); `Sim` of a v6 recording is Wall. The panel draws a pause as a shaded span on Wall and a zigzag + ◯ / ● (● = changed) on the ruler on Sim; an axis switch maps the window and range through wall time (`TimelineView.Remap`) so the same FRAMES stay in view. The Replay Browser's transport row has the **S / W** toggle (its `Axis`, disabled without sim time) | `CE3330_T13_OnTheSimAxis_APauseCollapses_ASeekLandsAtItsEnd`, `CE3330_T13_TheCursorOnTheSimAxis_AndASwitchKeepsTheFramesInView` |
| ⭐ **was "waits on backend's data D" — UNBLOCKED `2026-10-26`** (backend `5aa14d2b0` one facade, `0784f356c` `ReplaySessionTopic`) | the session layer, the review menu items, the toolbar's mode / axis icons, the slider on the axis — designed in **§2e**; Loop range still waits on backend §12n | — |
| ⏳ not in this slice | addendum 1 / 9 (director tracks — R2 has none yet), K / J / L (no rate in the cursor) | — |

## 2e. The panel and the toolbar on a cluster host — live, review, replay *(ui, `2026-10-26`; build-state: BUILT (E1–E4, `2026-10-26`) — coordinator note B6–B9 on backend's facade `5aa14d2b0` + `ReplaySessionTopic` `0784f356c`)*

⭐ Spec: [DAR §9a / §9b / §9e](DESIGN_Replay_Time_Axis_And_Review.md) (the toolbar, the menu, the two layers) and §12m (the ONE facade,
as built). This section is HOW the ui half is built on it. INVENTORY (graph + grep, `2026-10-26`): `MainToolbarTimeControlSection`
(the toolbar's time section, one per host) · `ClusterTimeTransportAdapter` (THE facade, every host) + `ClusterReplayControls` /
`ReplaySessionIntents` (the one construction of the intents) · `ReplaySession` (the session layer, `ReplaySessionUpdateEvent`) ·
`CheckpointMark` (`Fdp.Core`) · `TimelinePanel` / `ITimelineCursor` / `AxisCursor` / `TimelineAxis` (§2d) · `LiveTimelineCursor` (§2d —
REPLACED here) · `HrotNodeContext.StepTimeline` (§2d) · `ClusterStates.IsReplaying` / `IsReview` · `ClusterScenarioPanel` (the slider) ·
`GlobalTime.TotalWallTicks` (the frame-locked wall clock). ⚠ The graph was indexed before the backend merge (`ReplaySession` absent there,
present in grep).

```mermaid
classDiagram
  class ITimelineCursor { <<exists, grown>> +Mode TimelineMode +LivePoint long? +Checkpoints IReadOnlyList~CheckpointMark~ +CanReview bool +EnterReview(wallTicks) }
  class TimelineMode { <<NEW enum>> None Live Review Replay }
  class ClusterTimelineCursor { <<NEW, Hrot.Presentation>> facade, session, live source · wall ticks in, the facade's axis out }
  class LiveTimelineCursor { <<DELETED>> its live behaviour is ClusterTimelineCursor's Live mode }
  class ITimeTransportFacade { <<exists, backend's; +2 members, said>> +Session ReplaySession? +EnterReviewAtSim(simSeconds) }
  class ClusterReplayControls { <<exists>> +EnterReviewAtSim — ReplaySessionIntents.EnterReview(simSeconds) }
  class TimelinePanel { <<exists, grown>> menu by Mode · draws checkpoints and the live point }
  class MainToolbarTimeControlSection { <<exists, grown>> +ModeGlyph +ModeMenu (pure) +StopFace +stop guard F15 +axis icon }
  class LiveTimelineSource { <<exists, fixed>> frame wall = GlobalTime.TotalWallTicks · frame sim = GlobalTime.TotalTime }
  class TimelineIndex { <<exists, grown>> +FrameSim SharedList~double~ }
  class HrotNodeContext { <<exists, grown>> StepTimeline: frozen in a review / replay, afresh on entering live }
  ClusterTimelineCursor ..|> ITimelineCursor
  ClusterTimelineCursor --> ITimeTransportFacade : Mode, Axis, Seek, EnterReviewAtSim, LivePoint, Session
  ClusterTimelineCursor --> LiveTimelineSource : extent, frame wall and sim (live)
  ITimeTransportFacade <|.. ClusterTimeTransportAdapter
  ClusterTimeTransportAdapter --> ClusterReplayControls
  TimelinePanel --> ITimelineCursor
  MainToolbarTimeControlSection --> ITimeTransportFacade
  HrotNodeContext --> LiveTimelineSource
```
*What the picture shows that prose hid:* the panel and the toolbar reach the cluster through the ONE facade — no second seek or review path
(R-3059 "one path"); the only new type is a cursor, and it REPLACES one. Fdp.Presentation never sees the facade: the cursor is the seam.

```mermaid
sequenceDiagram
  actor U as Operator
  participant P as TimelinePanel
  participant C as ClusterTimelineCursor
  participant F as ITimeTransportFacade
  participant M as ClusterMaster
  participant N as HrotNodeContext (each node)
  Note over P,N: LIVE — the index grows, the ruler on the session axis (Sim from the live index's own frame sim)
  U->>P: right-click a past event, "Review from this event"
  P->>C: EnterReview(event wall ticks)
  C->>F: EnterReviewAtSim(the frame's sim seconds)
  F->>M: TransitionStateIntent(OperatingReview, sim target)
  M-->>N: OperatingReview — StepTimeline freezes the index (its history IS the run's up to the live point)
  M-->>C: ReplaySessionTopic (map, checkpoints, live point)
  Note over P,C: REVIEW — Mode Review and CanSeek, so the menu seeks, the live point and checkpoints drawn
  U->>P: "Seek to the live point"
  P->>C: Seek(live point wall ticks)
  C->>F: Seek(t on the session axis)
  U->>P: toolbar ⏏ Back to live
  P->>F: Stop() = ExitReview (R-3060), then for 3 s a second ■ is ignored (F15)
  M-->>N: OperatingLive — the index steps on from the live point (same run, no reset)
```
*What the picture shows that prose hid:* entering a review from LIVE has no session yet (no map, no wall zero on the host) — so the entry
goes by SIM seconds, which the master accepts on either axis ("the sim target wins", `ReplaySessionIntents`); inside the review every seek
goes by the session axis with the session's own zero and map.

```mermaid
graph TD
  NT["HrotNodeContext.Tick / the editor's frame"] --> ST["StepTimeline: OperatingReview / OperatingReplay ⇒ no step · entering OperatingLive from edit / preview ⇒ Reset"]
  ST --> LTS["LiveTimelineSource (wall = GlobalTime.TotalWallTicks, sim = GlobalTime.TotalTime)"]
  ED["EditorSubsystem: editor_event_timeline"] --> PAN["TimelinePanel + ClusterTimelineCursor"]
  TB["MainToolbarTimeControlSection (editor, CGF — ShellTimeControlToolbar)"] --> FAC["ITimeTransportFacade = ClusterTimeTransportAdapter"]
  PAN --> FAC
  FAC --> BUS["orchestration bus → ClusterOpEgressTranslator → master"]
  RS["the cluster replay's own recording → RecordingTimelineSource"]:::dead -.->|"not yet: in Replay the panel shows the session layer only"| PAN
  classDef dead stroke:#c33,stroke-dasharray:4 3,color:#c33
```
*What the picture shows that prose hid:* the freeze is in the node frame, so EVERY host's index survives a review (a review rewinds the world:
the frame counter going back would otherwise reset it); and a cluster REPLAY has no lane source on these hosts yet — its index would be the
live run's, which is a different exercise, so the panel shows only the session layer there (red edge).

| ⭐ decision | why — the one fact |
|---|---|
| **the host cursor keeps WALL ticks** (`AxisCursor` converts the Sim axis back first, TL-2) and converts to the facade's axis itself | one conversion point; the panel and its rails are unchanged |
| **entering a review from LIVE goes by SIM** (`EnterReviewAtSim`, NEW on the facade — a backend file, a two-line cross-lane edit, said in the commit) | in live the host has no session: no map, no wall zero; the master takes a sim target on either axis |
| **the live index stamps frames with `GlobalTime.TotalWallTicks`** (was `DateTime.UtcNow`) and records `GlobalTime.TotalTime` | `GlobalTime.cs:56`: *"the single source of truth for wall-clock time within a frame … including the flight recorder"* — the live index then shares the recording's and the session's clock exactly |
| **`StepTimeline` freezes in a review / replay and starts afresh on entering live** (the node's `LocalClusterState`) | DAR §9e: *"review — the SAME live source, frozen at the live point"*; afresh at live ⇒ index frame 0 = the run's first frame (the wall zero in live) |
| **in REPLAY the panel draws the session layer only** | the live index is the previous run's; the replay's own `RecordingTimelineSource` is a separate slice (DAR §9e row "cluster replay") |
| **F15: for 3 s after a return to live, ■ is ignored, and its tooltip says why** | the design's own fix; no modal |
| ⛔ rejected: a seek path in the panel beside the facade | R-3059 one path |
| ⛔ rejected: converting live wall → wall-after-zero for the entry | the host has no zero in live; a guessed zero seeks the wrong frame |
| ⏭ deferred, one line each: "Review from here **and play**" (needs a play once the review lands — no seam yet) · "Seek to before / after the pause's edits" (needs an `Edge` on the facade seek) · ⚑ "Mark this moment" (no checkpoint intent found) · replay "⑂ Go live from here" (D18, behind its own confirmation) · "Loop range" (backend §12n, building) | |

**Build slices (each with its rails, red-proved):** **E1** — the live source's clock (`TotalWallTicks`, frame sim, `TimelineIndex.FrameSim`)
+ `StepTimeline` freeze / reset · **E2** — `TimelineMode` + the cursor members, `ClusterTimelineCursor`, the panel's menu by mode and the
session marks, the editor wired (`LiveTimelineCursor` deleted), the facade's `Session` + `EnterReviewAtSim` · **E3** — the toolbar: mode
glyph + menu, ⏏ + F15, the axis icon · **E4** — `ClusterScenarioPanel`'s slider on the active axis (row 24).

#### 2e-E2 as built *(ui, `2026-10-26` — slice E2 BUILT; E1/E3 per their own commits; E4 open)*

| built | where | deviation from the diagrams above, and why |
|---|---|---|
| `TimelineMode` + `ITimelineCursor.Mode / LivePoint / Checkpoints / CanReview / EnterReview` (defaults: None / null / empty / false / no-op) | `Fdp.Presentation/ImGui/Panels/Timeline/ITimelineCursor.cs` | none. `FederatedReplayCursor` keeps the defaults (the Replay Browser has no review, DAR §9c) |
| `AxisCursor` forwards them: `LivePoint` and each checkpoint's `WallTicks` converted to the axis (checkpoints projected once per list the host hands out), `EnterReview` converted back to wall | `TimelineAxis.cs` | none |
| menu by `Mode` (`TimelinePanel.MenuItems`, still pure): Live + `CanReview` ⇒ "⟲ Review from \<time\>" + "⟲ Review from this event (\<lane\>)" and no seek item; Review ⇒ the seek items + "⏏ Seek to the live point" (when `LivePoint` is known); Replay ⇒ the seek items only | `TimelinePanel.cs` | none. The deferred items of the table above are NOT built |
| session marks: each checkpoint a `IconsFontAwesome6.Flag` on the ruler (label in the tooltip), the live point a vertical line labelled "live" over the lanes; off-screen ones not drawn | `TimelinePanel.DrawRuler` / `DrawOverlays`, `IconsFontAwesome6.Flag` | none |
| `ClusterTimelineCursor : ITimelineCursor` `(Func<ITimeTransportFacade?>, Func<ITimelineSource?>)` | `Hrot.Presentation/Adapters/` (beside `ClusterTimeTransportAdapter`, the facade's implementation; `Facades/` holds interfaces and the intents builder) | **"Now" in a review / replay is `Session.ZeroWallTicks + facade.TotalTime·TPS`, not `TimeMap.WallAt(TotalTime)`.** 📐 Measured: the cluster clock the facade reads is, after a seek, *wall seconds after the session's zero* (`ReferenceReplayLoadHandler` overwrites `RestoredTime.TotalTime` with exactly that; `ReplaySeekProcessManager` snaps the master clock to it; `ReplayClockProcessManager` reads it back as `anchor + TotalTime`) — so it is not sim seconds and a `WallAt` would double-convert on a paused session. Clamped to the extent. `TimeMap` is returned whatever the facade's axis (the Wall axis shades the pauses from it); `Axis` is `Sim` only when the facade's axis is Sim |
| `LiveTimelineCursor` deleted; the editor window's source func and the cursor's are ONE func, `null` while the facade's Mode is Replay | `EditorSubsystem.cs` (`editor_event_timeline`) | none |
| the live map is `ReplayTimeMap.FromFrames(FrameTicks, FrameSim, 0)` per index SNAPSHOT (cached by instance) | `ClusterTimelineCursor.TimeMap` | the snapshot is republished at the source's throttle (not per frame), so the O(n) rebuild is the same cost class as `TimelineAxis.Project`'s |
| rails | `Hrot.Presentation.Tests/ClusterTimelineCursorTests.cs` (mode, extent per mode, seek on both axes, review entry, live point / checkpoints, axis, play) · `Fdp.Presentation.Tests/…/TimelineInteractionTests.cs` `CE3330_E2_*` (menu by mode, `AxisCursor` session layer) · `Hrot.Presentation.Tests/Panels/TimelinePanelFrameRail.cs` `CE3330_E2_TheSessionLayer_…` | |


```mermaid
classDiagram
  class Take { <<exists, grown>> +Source Source +List~CameraKey~ Keys +List~Segment~ Segments +List~Subtitle~ Subtitles +List~ViewChange~ Views +List~FilmGraphic~ Graphics +shots read as keys }
  class Source { <<NEW>> +string? Scenario +string? Recording +string ScenarioHash }
  class CameraKey { <<NEW>> +AtExpr At +CameraSpec Camera +Transition In +float? Fov +string? Still }
  class Transition { <<NEW>> Cut · Fly(seconds, Ease) · Dip(out, in, colour) }
  class Segment { <<NEW>> +AtExpr From +AtExpr To +double Speed +GapStyle Gap }
  class Subtitle { <<NEW>> +AtExpr At +AtExpr Until +string Text +SubtitleStyle Style }
  class ViewChange { <<NEW>> +AtExpr At +bool? Map3D +LayerChange Layers }
  class FilmGraphic { <<NEW>> +AtExpr At +AtExpr Until +GraphicKind Kind +points or unit +Style }
  class CameraSpec { <<exists>> follow · over-toward · lookAt · eye-target }
  class AtExpr { <<exists>> }
  class AnchorSpec { <<exists, grown>> +When SearchPredicateDto +N · nine shorthands lower to predicates (TE-3b) }
  class AnchorBook { <<exists, grown>> +Observe(world, units, t) +TimeOf(anchor) +Markers() }
  class TakeTimeline { <<exists, grown>> +ProgramAt(filmTime) ProgramFrame +FilmLength }
  class ProgramFrame { <<NEW>> +double SourceTime +FilmShot Pose +float Fade +subtitles +view +graphics +double AudioRate }
  class TakeStore { <<exists, grown>> +TakesOf(recording) +Promote(take, scenario) PromoteReport }
  class DirectorRunner { <<exists, MOVED>> +Play() +Tick() +Seek(filmTime) }
  class IDirectorHost { <<exists, grown>> +bool CanSeek +Seek(sourceTime) }
  class EditorDirectorHost { <<exists>> CanSeek = false }
  class ReplayDirectorHost { <<NEW>> over ReplayBrowserContext }
  class ReplayBrowserContext { <<exists>> +SeekToFrame(i) }
  class RecordingSearchService { <<exists>> frame-step loop }
  class DirectorWindow { <<exists, MOVED, grown>> bin · inspector · the director's track provider }
  class TimelinePanel { <<NEW, R-4073>> lanes · overview · zoom · transport row }
  class ITimelineSource { <<NEW>> +Extent +Lanes +Pyramid(lane, level) +Events(lane, range) +Progress }
  class RecordingTimelineSource { <<NEW>> over the index file }
  class LiveTimelineSource { <<NEW>> the extractors on the live world, appended }
  class TimelineIndexBuilder { <<NEW>> one pass, its own context; writes the sidecar }
  class EventExtractors { <<NEW>> shot · hit · blast · death · behaviour · movement · orders }
  class ITraceLaneProvider { <<exists, generalised — TE-21>> +Lanes +rows for a range }
  class TraceTimelineWindow { <<exists, a shell>> hosts TimelinePanel in the AI perspectives }
  class ITimeTransportFacade { <<exists, grown>> +bool CanSeek +Seek(t) }
  class SearchPredicateDto { <<exists — R-4072>> the anchor's condition }
  class PredicateCompiler { <<exists>> }
  class FilmGraphicsLayer { <<NEW>> draws ProgramFrame.graphics }
  class FilmStudio { <<exists>> +Direct(FilmShot) +Capture() }
  class ShotEvaluator { <<exists>> +Evaluate(spec, t, world) FilmShot }
  class UndoStack { <<exists — one of four, TE-14>> }

  Take *-- Source
  Take "1" *-- "many" CameraKey
  Take *-- Segment
  Take *-- Subtitle
  Take *-- ViewChange
  Take *-- FilmGraphic
  CameraKey *-- CameraSpec
  CameraKey *-- Transition
  CameraKey ..> AtExpr
  AtExpr ..> AnchorBook : resolved by
  AnchorBook ..> AnchorSpec
  TakeTimeline ..> Take
  TakeTimeline ..> ShotEvaluator
  TakeTimeline --> ProgramFrame
  DirectorRunner --> TakeTimeline
  DirectorRunner --> IDirectorHost
  DirectorRunner --> FilmStudio
  DirectorRunner --> FilmGraphicsLayer
  EditorDirectorHost ..|> IDirectorHost
  ReplayDirectorHost ..|> IDirectorHost
  ReplayDirectorHost --> ReplayBrowserContext
  DirectorWindow --> DirectorRunner
  DirectorWindow --> TakeStore
  DirectorWindow --> UndoStack
  DirectorWindow ..|> ITraceLaneProvider
  TraceTimelineWindow --> TimelinePanel
  TimelinePanel --> ITimelineSource
  TimelinePanel --> ITraceLaneProvider
  TimelinePanel --> ITimeTransportFacade
  RecordingTimelineSource ..|> ITimelineSource
  LiveTimelineSource ..|> ITimelineSource
  TimelineIndexBuilder --> EventExtractors
  LiveTimelineSource --> EventExtractors
  TimelineIndexBuilder ..> RecordingSearchService : steps with its loop
  AnchorSpec ..> SearchPredicateDto : IS one (R-4072)
  AnchorBook ..> RecordingTimelineSource : answers from the index first
  AnchorBook ..> PredicateCompiler
```

*What the class picture forces: every new type is DATA in the take, plus one host and one layer. The pose maths stays in
`ShotEvaluator`, the anchors in `AnchorBook`, the timing in `TakeTimeline` — each grows, none is copied.*

## 4. Sequences

### 4a. Author a key — fly, press K

```mermaid
sequenceDiagram
  actor U as User
  participant P as TimelinePanel
  participant C as pilot camera (MapCamera3D)
  participant S as selection
  participant T as Take + UndoStack
  participant R as DirectorRunner
  U->>P: scrub to a marker (seek)
  P->>R: Seek(filmTime)
  U->>C: fly to a pose
  U->>P: K
  P->>S: a unit selected?
  alt a unit is selected
    P->>P: pose relative to it (distance, yaw, pitch → follow / over)
  else
    P->>P: pose in the world (eye, target)
  end
  P->>P: at = nearest marker before the playhead + offset (TE-3)
  P->>T: push snapshot, insert key (transition: last used)
  P->>R: ProgramAt(now) — the program camera shows the new key
```

### 4b. One program frame, from a recording

```mermaid
sequenceDiagram
  participant B as ReplayBrowserSubsystem.Update
  participant R as DirectorRunner.Tick
  participant L as TakeTimeline.ProgramAt
  participant H as ReplayDirectorHost
  participant X as ReplayBrowserContext
  participant F as FilmStudio + FilmGraphicsLayer
  B->>R: Tick()
  R->>L: ProgramAt(filmTime)
  L-->>R: source time, pose, fade, subtitles, view, graphics, audio rate
  alt the source time is not the next frame (skip, a seek, slow motion)
    R->>H: Seek(sourceTime)
    H->>X: SeekToFrame(i) — keyframe + at most 59 deltas
  else
    R->>H: StepBy(dt × speed)
  end
  R->>F: Direct(pose) · draw fade, subtitles, graphics · apply view
  Note over R: capture mode: the same frame goes to the PNG sink, as built
```

### 4c. Open a recording — the index, built once, then cached (TE-4)

```mermaid
sequenceDiagram
  actor U as User
  participant H as Replay Browser (any host opening a recording)
  participant S as RecordingTimelineSource
  participant B as TimelineIndexBuilder (background, its own context)
  participant T as TimelinePanel
  U->>H: open recording
  H->>S: Open(recording)
  alt the sidecar exists and its header matches
    S-->>T: complete index (milliseconds)
  else first open, or the recording changed
    S->>B: Build(recording)
    loop every recorded frame, on the search service's loop
      B->>B: EventExtractors → events, entities, position track
      B-->>S: a chunk every ~N frames (pyramid levels updated)
      S-->>T: Progress (drawn up to here, the rest shaded)
    end
    B->>S: write the sidecar
  end
  U->>T: zoom, pan, click
  T->>S: Pyramid(lane, level) for the visible range (pixels × lanes)
  T->>H: Seek(t) through ITimeTransportFacade
```

*What the sequence shows: the expensive pass happens once per recording; every take, search lane and later open reuses it. The GUI's
sandbox is never touched while it runs.*

### 4e. A take's anchor that the index cannot answer yet

```mermaid
sequenceDiagram
  participant A as AnchorBook
  participant S as RecordingTimelineSource
  participant Q as RecordingSearchService (targeted)
  A->>S: TimeOf(n-th time the predicate is true)
  alt an indexed lane answers it (a shot, a death)
    S-->>A: time
  else a spatial predicate on a new area or line
    S-->>A: candidate seconds from the position track
    A->>Q: ExecuteSearch(predicate) on those windows only (keyframe + ≤ 1 s each)
    Q-->>A: exact frame
    A->>S: add the hits to the cache
  end
```

### 4d. Promote a recording take to a scenario take

```mermaid
sequenceDiagram
  actor U as User
  participant P as TimelinePanel
  participant T as TakeStore
  U->>P: Promote "main" to scenario ua-civ-shelter
  P->>T: Promote(take, scenario)
  T->>T: the recording's scenario hash = the scenario's? else refuse
  T->>T: each fixed-time key → nearest marker before it + offset
  T-->>P: report: re-anchored keys, keys left fixed, missing units
  U->>P: confirm
  P->>T: write scenarios/ua-civ-shelter/takes/main.take.json (no source)
```

## 5. The take format, v2

```json
{
  "source": {"recording": "7f3a…", "scenarioHash": "…"},
  "anchors": {"fire": {"on": "shot", "who": "Tank 1", "n": 1},
              "blue": {"n": 1, "when": {"$type": "SpatialBounding", "TriggerEvent": "Entry",
                                        "Bounds": {"Min": [180, 40], "Max": [260, 90]}, "…": "the predicate's own JSON"}}},
  "segments": [{"from": 0, "to": "blue-5", "speed": 1},
               {"from": "fire-1", "to": "fire+3", "speed": 0.25, "gap": "dip"}],
  "keys": [
    {"at": "blue-5", "lookAt": [105, 75, 1], "distance": 60, "pitch": -30, "in": "cut"},
    {"at": "blue-0.5", "over": "Tank 1", "toward": "Enemy", "in": {"fly": 2, "ease": "inOut"}, "still": "crossing"},
    {"at": "fire+1", "follow": "Enemy", "distance": 12, "in": {"dip": [0.4, 0.4]}}
  ],
  "subtitles": [{"at": "blue-4", "until": "blue", "text": "The platoon crosses phase line BLUE"}],
  "views": [{"at": 0, "map": "2d", "layers": {"on": ["Units.Cards"]}}, {"at": "blue-5", "map": "3d", "layers": {"off": ["Units.Cards"]}}],
  "graphics": [{"at": "fire-1", "until": "fire+3", "kind": "ring", "who": "Enemy"}]
}
```

A scenario take is the same with `source` absent. ⭐ `shots[]` is still read; the editor writes `keys[]`. ⭐ `"on": "shot"` is a shorthand that lowers to a predicate (TE-3b); `when` holds any predicate, written by the existing editor (R-4072). A line crossing is the extension TE-3 names; until it exists, an area entry is the box above.

## 6. Phases and acceptance

| phase | builds | accepted when |
|---|---|---|
| **R0** *(measure)* | ① a recording plays back faithfully (types, effects, deaths, poses, doors) · ② which events a recording holds (shots, hits, blasts as recorded transient events? `ShotLog` / `DetonationLog` live beside the live world only) · ③ the TKB from the recording's scenario (TE-12) · ④ sound sources in replay · ⑤ is an editor run archived (TE-13) · ⑥ which class draws tactical graphics (TE-9) · ⑦ each of the nine anchor shorthands as a predicate (TE-3b) · ⑧ the scan rate of the search service's loop and the index's size on a real hour | a report per item, each with its file:line; the TKB loaded in the Replay Browser |
| **R1** ⭐ *(the timeline first — useful without the director, R-4073)* | `TimelineIndexBuilder` + extractors + the sidecar · `ITimelineSource` (recording, live) · `TimelinePanel` with the rules of §2b · `ITimeTransportFacade.CanSeek / Seek` and the Replay Browser adapter · the Event Browser on the index | an hour's recording opens with its timeline filling progressively; the second open is instant; zoomed out it draws densities, zoomed in glyphs, < 1 ms per frame; a click seeks |
| **R2** | format v2 + `shots[]` as keys · `ProgramAt` · move the runner and window (TE-5) · `ReplayDirectorHost` · the director's tracks (Time, Camera) on the Timeline, K, cut / fly / dip · undo · anchors as predicates in the existing editor (R-4072) | a recording take plays in the Replay Browser with skips, slow motion and the three transitions; the same take's keys play in the editor |
| **R2b** | Subtitles and View tracks · promote · the predicate extensions (polygon area, line crossing, drawing them on the map) | a take authored on a recording, promoted, plays on a fresh editor run of its scenario with every condition-anchored key on its event |
| **R3** | Graphics track + `FilmGraphicsLayer` · shot templates · audio at any rate (TE-11) · still keys in Verify (TE-10) | a regression run writes a PNG per still key; slow motion sounds as TE-11 says |
| **R4** | reel composer · render queue · HTTP (TE-15) + MCP catalog | a reel of two recording takes renders to one movie with sound |

## 7. Claim table — what the leans rest on

| claim | code — how it IS | design — how it was MEANT |
|---|---|---|
| a recording plays back faithfully | ⛔ not tried (user) | ⛔ `DESIGN_Film_Director.md` §3e: *"that slice starts by measuring both"* ⇒ R0 ① |
| the Replay Browser sets no TKB | ✅ no `ITkbDatabase` in `ReplayBrowserSubsystem.cs`; the node builder sets it at `HrotNodeBuilder.cs:262` | ✅ `DESIGN_Map_3D_Mode.md` (§6b, the finding) · ruled R-4069 |
| the runner can move to the shared assembly | ✅ `DirectorRunner.cs` usings: `Hrot.UI.Common.Map3D(.Director)` only; `DirectorWindow.cs`: + `Fdp.Presentation.WindowManager`, ImGui | ✅ R-4068 |
| the host seam takes a replay host | ✅ `IDirectorHost` (`DirectorHost.cs:13`): World, StepBy, SimTime, Hold, Pause…; optional members already default to NotSupported (`WriteCheckpoint`, `:53`) | ✅ `DESIGN_Film_Director.md` §8.1 (every editor dependency behind an interface) |
| every map host carries the film camera, the Replay Browser too | ✅ `MapInteractionPack.cs:539` (`FilmCaptureLayer`, every host) | ✅ `DESIGN_Map_3D_Mode.md` §6ab, §6b |
| the audio layer is on every map host | ✅ `MapInteractionPack.cs:533` (`AudioLayer.For`, given the host's TKB) | ✅ `designs/audio/DESIGN_FPS_Audio.md`; R-4059 |
| a seek is cheap | ✅ a keyframe every 60 frames (`RecorderTickSystem.cs:21`) | ✅ replay-browser-2 §4.2 |
| a headless frame loop over a recording exists, isolated from the GUI | ✅ `RecordingSearchService` | ✅ replay-browser-2 §1 isolation invariants, §6.3 |
| anchors already carry events + offsets | ✅ `AtExpr`, `AnchorSpec.Kinds` (shot, blast, hit, near, task, side, health, embark, gesture — `Take.cs:129`) | ✅ `DESIGN_Film_Director.md` §4, `DESIGN_Scenario_Regression.md` (checks.json reuses it) |
| a recording names its scenario | ✅ an archived exercise's `Orchestrator.json` carries the scenario id (`AssetInventoryProcessManager.cs:265`) · ⛔ an editor run's temp folder: not measured | ✅ mgmt-1 §12 · ⛔ R0 ⑤ |
| the anchors can be resolved from a recording | ⛔ `AnchorBook` reads `ShotLog` / `DetonationLog`, kept beside the LIVE world (`DESIGN_Film_Director.md` §8.3) — in a replay sandbox: not measured | ⛔ R0 ② — ⚠ load-bearing for the Combat lanes; the index reads RECORDED events (the event scanner), which is why it is the source and not AnchorBook's logs |
| the existing condition model is one, shared, editable and serialisable | ✅ `SearchPredicateDto` (`SearchPredicateDto.cs`, `$type` discriminators) used by data breakpoints (`Hrot.Diagnostics.Breakpoints`), Replay Browser search, `/replay/search`, BTree/HSM/Blueprint breakpoint menus; StructEdit editor hosted by `ReplaySearchPanel` and `DataBreakpointManagerPanel` | ✅ `Predicate-Infrastructure-Capabilities.md` §1–§5 · R-4072 |
| enter / leave an area exists; a polygon and a line do not | ✅ `SpatialBoundingPredicateDto` + `BoundaryEvent { Entry, Exit, EntryOrExit }` over a `BoundingBox2D` (`SearchPredicateDto.cs:161-183`), picked with `[MapPickableBoundingBox]` · ⛔ no polygon, no line predicate found (graph + grep; coverage not checkable) | ⚠ the capabilities doc §6 calls the box "the polygon" — it is a box |
| the predicates evaluate both on a recording and live | ✅ `RecordingSearchService` (recording) · `DataBreakpointManager` (live, per tick) | ✅ capabilities doc §3.3 |
| no event index or cache of a recording exists | ✅ `PlaybackController` keeps only an in-memory FRAME index (`_frameIndex`: offsets, wall ticks, keyframes — `PlaybackController.cs:30-47, 354`) · ⛔ searched graph + grep for an index / cache / summary file: none (coverage not checkable) | ⛔ searched `docs/`: none |
| a cluster replay can seek; the transport facade cannot yet | ✅ `ClusterOpType.ReplaySeek` / `NodeReplaySeek` (`OrchestrationMessages.cs:34, 74`), `ClusterMaster.Replay.cs` · `ITimeTransportFacade` has no seek (`ITimeTransportFacade.cs:11-47`) | ✅ `DESIGN_Time_Architecture.md` §11 |
| a timeline for one unit was already designed, not built | ✅ `TraceTimelineWindow.cs` (50 lines, draws *"No trace data."*), `ITraceLaneProvider` + three providers (BTree, HSM, Utility) | ✅ `AI_Editor_Shared_Infrastructure.md` §15 · BV-30 deferred (`DESIGN_Entity_Brain_View.md`) ⇒ TE-21 |
| a background index pass is safe | ✅ the search and export services build their own `ReplayBrowserContext`; export already runs `LongRunning` | ✅ replay-browser-2 §1 "Isolation invariants" |
| sound at other rates | ✅ `SoundTrack.RenderWav` maps each cue to the film timeline (§8.3) | ⛔ no design for live playback at ≠ 1× — TE-11 is new |

## 9. R1 — the build plan, refined by measurement *(ui, `2026-10-26`; build-state: READY-TO-BUILD for R1-a…R1-c)*

**INVENTORY** *(`scripts/cbm.sh search_graph` name_pattern `.*(Timeline|TraceLane|TimeTransport|EventBrowser).*` label Class → **31**,
label Interface → **2**)*: the timeline-shaped types that exist are `ReplayTimelinePanel` + `ReplayTimelineWindow` (Fdp.Presentation —
the browser's transport and slider), `TraceTimelineWindow` (a shell) + `ITraceLaneProvider` with `BTree/Hsm/UtilityTraceLaneProvider`
(descriptors only), `EventBrowserPanel` (+ two `FdpEventBrowserWindow`s), `ITimeTransportFacade` with `EditorTimeTransportFacade` and
`ClusterTimeTransportAdapter`; the rest are SUMO's `ActivityTimeline*` (unrelated). ⛔ No index, builder, source or event-lane type exists.

📐 **Measured before building** (a read-only survey of the code + grep; ⚠ `check_index_coverage` is not reachable through the CLI, so an
"absent" here is absent in the searched paths):

| finding | file:line | what it changes |
|---|---|---|
| **D1** the Replay Browser's scrubber does NOT seek through `ReplayBrowserContext.SeekToFrame`: it drives `FederatedReplayManager` (`SetBaseWallTicks` + `SeekAll`/`StepForwardAll`) | `ReplayTimelinePanel.cs:539-543`, `FederatedReplayManager.cs:125-183`; `ReplayBrowserSubsystem.cs:835` | §2a's `ReplayBrowserTransportAdapter → SeekToFrame` is wrong: the adapter fronts the MANAGER (TL-2) |
| **D2** `ITraceLaneProvider` lives in `Hrot.Editor.AiShared` and only DESCRIBES lanes (id, name, levels) — no rows | `Hrot.Editor.AiShared/Debug/ITraceLaneProvider.cs` | the row seam cannot live there (TL-3) |
| **D3** `Hrot.ReplayBrowser` does not reference `Hrot.Presentation` (it references `Fdp.Presentation`, `Fdp.Toolkits`, `Hrot.Core`, `Hrot.CGF`, `Hrot.SimHost`) | `Hrot.ReplayBrowser.csproj` | ⛔ the panel cannot live in `Hrot.Presentation` as §2a says — it lives in `Fdp.Presentation`, beside today's `ReplayTimelinePanel` (TL-1) |
| the combat events a recording carries — `WeaponFireNotification`, `HitEvent`, `DetonationNotification`, `DamageAssessedEvent` — are `Fdp.Toolkits` types without `NoReplay`; `ShotLog` / `DetonationLog` are per-world side stores, not recorded | `Combat/Events/WeaponFireEvents.cs:94`, `Combat/HitEvent.cs:19`, `Combat/DetonationNotification.cs:24`; `Combat/ShotLog.cs:68` | R0 ②: the Combat lanes come from the recorded EVENTS (TL-5) |
| the behaviour histories are recorded components: `BrainJournal` (24, `Seq`), `CommandOrderHistory` / `CommandReportHistory` (32 each), `UtilityDecisionLog` | `BrainJournal.cs:133`, `CommandHistory.cs:61,95`, `UtilityDecisionLog.cs:63` | TE-22's lanes read them frame to frame |
| `RecordingSearchService`'s loop returns finished lists only (no chunks, no progress) | `RecordingSearchService.cs:39,85` | the builder runs its OWN loop, on its own repository — the same isolation invariant |

| # | ⭐ decision (lean) | rejected |
|---|---|---|
| **TL-1** | the index, its builder and the sources in **`Fdp.Toolkits/ReplayBrowser/Timeline`**; the panel and its pure layout in **`Fdp.Presentation/ImGui/Panels/Timeline`** — every host references both | `Hrot.Presentation` (§2a) — the Replay Browser cannot see it (D3) |
| **TL-2** | the panel seeks through **`ITimelineCursor { Extent, Now, CanSeek, Seek(ticks) }`**, a one-method adapter each host binds to ITS OWN transport: the Replay Browser to `FederatedReplayManager` (D1), the Hrot hosts to `ITimeTransportFacade` grown `CanSeek / Seek` (R1-e) — ⛔ not a second transport, no state of its own | the panel calling `ITimeTransportFacade` — not visible from `Fdp.Presentation`; `SeekToFrame` — not the browser's path |
| **TL-3** | a row seam **`ITimelineRowProvider`** in `Fdp.Presentation` (rows for a time range); `ITraceLaneProvider`'s providers get an adapter when the Trace Timeline hosts the panel (R1-e, TE-21) | generalising `ITraceLaneProvider` in place — an editor assembly the browser cannot reference |
| **TL-4** | time is **frame index + the recording's wall-clock ticks** (`PlaybackController.GetFrameMetadata`): events keep their frame; the index keeps one frame → ticks table; seek is by ticks | sim seconds — a recording's frames carry wall ticks, and the browser's manager seeks by them |
| **TL-5** | R1's lanes: **Life** (a network id appearing / leaving), **Combat** (shots = `WeaponFireNotification`, hits = `HitEvent`, blasts = `DetonationNotification`), **Behaviour** per unit (Changes = `BrainJournal.Seq` advancing, Orders / Reports = the history's newest entry changing, Decisions = a `UtilityDecisionLog` winner switch). ⏳ **Movement** and **Tasks** wait (they need the scenario's areas and mission plans — R2b) | every §2b lane at once — the two need data the index does not hold yet |
| **TL-6** | the sidecar **`<recording>.timeline`**: a header (magic, schema version, the recording's byte size, its last frame's ticks) then the tables; a header mismatch ⇒ rebuild | a database — one file beside the recording is what §2b asks |
| **TL-7** | the builder runs on a **background task with its own `PlaybackController` + repository + bus**; it publishes immutable snapshots every ~N frames (progressive, §2b) — the GUI's sandbox is never touched | stepping the GUI's sandbox — the isolation invariant forbids it |

```mermaid
classDiagram
  direction LR
  class TimelineIndex { <<NEW, Fdp.Toolkits>> Lanes · FrameTicks[] · Events(lane, from, to) · Count(lane, from, to) · Before(lane, frame) · FrameAt(ticks) }
  class TimelineEventSink { <<NEW, as built>> per-lane lists · Snapshot() }
  class FederatedReplayCursor { <<NEW, as built, Fdp.Presentation>> }
  class EventTimelineWindow { <<NEW, as built>> U-obs-5 dump }
  class TimelineEvent { <<NEW, struct>> Frame · Lane · Subject · Other · X · Y · Value }
  class TimelineLane { <<NEW, enum>> Spawn Destroyed Shot Hit Blast · Change Order Report Decision }
  class TimelineIndexBuilder { <<NEW>> Build(path, onChunk, ct) — own PlaybackController + repo + bus }
  class ITimelineExtractor { <<NEW>> Frame(repo, bus, frame, sink) }
  class LifeExtractor { <<NEW>> }
  class CombatExtractor { <<NEW>> }
  class BehaviourExtractor { <<NEW>> BrainJournal · order/report histories · UtilityDecisionLog }
  class TimelineSidecar { <<NEW>> Write(path, index) · TryRead(path) header check }
  class ITimelineSource { <<NEW>> Index (snapshot) · Progress · IsComplete }
  class RecordingTimelineSource { <<NEW>> Open(path): sidecar or background build }
  class TimelineLayout { <<NEW, pure, Fdp.Presentation>> Marks(index, lane, window, pixels) density or glyphs, clusters, priority }
  class TimelinePanel { <<NEW, Fdp.Presentation>> lanes · overview · zoom about cursor · click seeks }
  class ITimelineCursor { <<NEW>> Extent · Now · CanSeek · Seek(ticks) }
  class ITimelineRowProvider { <<NEW>> rows for a range — TE-21 }
  class PlaybackController { <<exists>> }
  class FederatedReplayManager { <<exists — the browser's seek, D1>> }
  class ReplayTimelinePanel { <<exists — the browser's transport + slider>> }
  TimelineIndexBuilder --> PlaybackController : its own
  TimelineIndexBuilder --> ITimelineExtractor
  LifeExtractor ..|> ITimelineExtractor
  CombatExtractor ..|> ITimelineExtractor
  BehaviourExtractor ..|> ITimelineExtractor
  TimelineIndexBuilder --> TimelineIndex
  TimelineIndex *-- TimelineEvent
  RecordingTimelineSource ..|> ITimelineSource
  RecordingTimelineSource --> TimelineSidecar
  RecordingTimelineSource --> TimelineIndexBuilder
  TimelinePanel --> ITimelineSource
  TimelinePanel --> TimelineLayout
  TimelinePanel --> ITimelineCursor
  TimelinePanel --> ITimelineRowProvider
  FederatedReplayCursor ..|> ITimelineCursor
  FederatedReplayCursor --> FederatedReplayManager : SetBaseWallTicks
  ITimelineExtractor ..> TimelineEventSink : adds to
  EventTimelineWindow --> TimelinePanel
  TimelinePanel ..> ReplayTimelinePanel : sits beside it, the transport row stays there
```
*Caption:* what the picture shows that prose hid — nothing in the index or the panel knows a host: the browser binds a cursor to its
manager and a source to its file; the Hrot hosts bind the same two to their facade and the live world (R1-e). The pure `TimelineLayout`
is where §2b's drawing rules live, so they are tested without ImGui.

```mermaid
graph TD
  RB["Hrot.ReplayBrowser — ReplayBrowserSubsystem.Update (each frame)"] --> TP["TimelinePanel.Draw"]
  RB -->|"on open"| RTS["RecordingTimelineSource.Open(path)"]
  RTS -->|"sidecar matches"| IDX["TimelineIndex"]
  RTS -->|"else: Task.Run"| BLD["TimelineIndexBuilder (own PlaybackController, own repo)"]
  BLD -->|"snapshot every N frames"| RTS
  TP --> LAY["TimelineLayout (pure)"]
  TP -->|"click"| CUR["ITimelineCursor → FederatedReplayManager.SetBaseWallTicks + SeekAll"]
  ED["Editor / CGF / SimHost / IG"]:::later -.->|"R1-e: LiveTimelineSource + ITimeTransportFacade"| TP
  classDef later stroke:#c33,stroke-dasharray:4 3,color:#c33
```
*Caption:* the dead edges — the editor and the cluster hosts do not show the panel in R1-a…c; they need a live source and the grown
facade (R1-e). The builder never runs on the frame thread.

| slice | builds | accepted when |
|---|---|---|
| **R1-a** | `TimelineIndex`, `TimelineEvent`, the three extractors, `TimelineIndexBuilder`, `TimelineSidecar`, `RecordingTimelineSource` | a recorded run's shots, hits, blasts, spawns, deaths and a commander's orders come out of one pass in frame order; the second open reads the sidecar; a changed recording rebuilds |
| **R1-b** | `TimelineLayout` (density / glyph / cluster / priority, zoom-about-cursor maths) + `TimelinePanel` + `ITimelineCursor` | §2b's rules as rails on the pure layout; < 1 ms per draw on an hour's index |
| **R1-c** | the Replay Browser hosts the panel over its manager; R0 ⑧ (scan rate, index size) measured on a real recording | §6 R1's acceptance in the browser |
| **R1-d** | the pinned unit's swimlane + TE-22's acceptance (the `hill-attack` commander's Orders lane, hover why, click seeks) | TE-22 |
| **R1-e** | `LiveTimelineSource`, `ITimeTransportFacade.CanSeek / Seek`, the Trace Timeline hosting the panel (TE-21), the Event Browser on the index | the editor shows the same panel live |

### 9a. R1-a as built *(ui, `2026-10-26`)*

Built in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Timeline/` as the class diagram above draws it; rails `TimelineIndexTests` (Fdp.Toolkits.Tests).

| what | as built | deviates from the plan |
|---|---|---|
| `TimelineIndex` | per lane a sorted `TimelineEvent[]`, `FrameTicks`, `ScannedFrames` / `TotalFrames`; `Events` / `Count` are binary searches; `FrameAt(ticks)` | ⚠ no `Entities` table — every event carries its ids (network id, else `-(index+1)`), so nothing needs one yet |
| `TimelineEventSink` | the builder's growing per-lane lists; `Snapshot` copies them into an immutable index | new — the `sink` the extractor signature names |
| `LifeExtractor` | a set diff of NETWORKED entities per frame; X / Y where it appeared or was last seen | ⚠ entities without a network id (rounds) are not indexed — they would drown the lane. Frame 0 reports every unit present as Spawned |
| `CombatExtractor` | Shot = a fired `WeaponFireNotification` (a refusal is not a shot; value = rounds), Hit = `HitEvent`, Blast = `DetonationNotification` (value = damage) | — |
| `BehaviourExtractor` | Change per new `BrainJournal` entry (value = kind); Order / Report: one event per NEW history entry, found by the previous newest entry's key (time + counterpart, TE-22 ②) — two orders in a frame are two events — plus the same order REPEATED, which the history counts rather than lists; Decision when a slot's `SwitchCount` rises (other = winner, value = decision id) | the first frame a unit is seen is its baseline (its rings hold the past) |
| `TimelineIndexBuilder.Build` | its own `PlaybackController` + repository (+ the repository's bus), `RecordingSearchService.RegisterAllComponents`; a snapshot every `chunkFrames` (default 2000) | — |
| `TimelineSidecar` | `<recording>.timeline`: magic, schema version, the recording's **byte size and last-write time**, then the tables; written via a temp file, only when complete | ⚠ TL-6 said "its last frame's ticks": reading those needs the recording opened; size + write time is checked without opening it |
| `RecordingTimelineSource` | `Open(path)`: the sidecar if it matches, else a `Task.Run` build publishing snapshots into a volatile field, then the sidecar; `Dispose` cancels | — |

**R1-b / R1-c as built** *(same day)* — in `Fdp.Presentation/ImGui/Panels/Timeline/` + `Windows/Timeline/`; rails `TimelineLayoutTests`
(Fdp.Presentation.Tests) and `CE1302_TheEventTimeline_*` (Hrot.ReplayBrowser.Tests):

| what | as built | deviates from the plan |
|---|---|---|
| `TimelineLayout` (pure) | `Marks(index, lanes, window, width)`: more events than pixels ⇒ contiguous density bins (frame edges, log-scaled intensity), else glyphs clustered within 7 px across the row's lanes (a k-way merge), each mark carrying its top-priority lane and frame range; `ZoomAbout` / `Pan` / `Clamp` / `Bracket` / `RulerStep`; `MarkAt` + `Describe` (the tooltip, events counted per lane) | ⭐ the bin edges are cached per window, so the rows of one draw share them — 📐 **0.59 ms per draw** for 9 rows × 1600 px over an hour (216 000 frames, 100 000 events), Debug build; before the cache 1.5 ms |
| `TimelinePanel` | overview strip + bracket (drag moves it), ruler, Life / Combat / Behaviour groups (collapsed = one row), unscanned part shaded, "now" line; Ctrl+wheel zooms about the mouse, drag pans, click seeks, hover lists the mark | ⏳ not yet: jump to next / previous event, the search box, focus-dimming (R1-d with the pinned unit) |
| `ITimelineCursor` + `FederatedReplayCursor` | Extent / Now / CanSeek / Seek over `FederatedReplayManager`; a seek subtracts the node's offset and calls `SetBaseWallTicks` (which seeks every node) | — |
| `EventTimelineWindow` + `TimelinePanelViewModel` | the window (U-obs-5: declared, dumps scanned / total frames, window, now, per-lane counts in window and in total) | ⚠ named *Event* Timeline: "Replay Timeline" is already the browser's transport window |
| Replay Browser hosting | `rb_event_timeline` registered beside `rb_timeline`; `SyncTimelineSource()` opens the primary node's recording each update when it changes (sidecar or background build), disposed on shutdown | — |

📐 **Seen in a real ImGui frame** (`TimelinePanelFrameRail`, Hrot.Presentation.Tests — Xvfb + software GL, a synthetic ten-minute
firefight; screenshots to `HROT_TIMELINE_SHOTS`): the first render found three defects the pure-layout rails could not — ① `ImGui.Text*`
take a printf FORMAT, so *"Indexing… 50%"* read garbage after its `%` (⇒ all panel text goes through `TextUnformatted`); ② the editor font
has no `–` / `…` (drawn as `?`); ③ `×N` count labels overprinted each other where clusters sit closer than the label's width (⇒ a label is
drawn only where it fits before the next mark; the overview strip draws marks only). `TimelinePanel.Show(window)` was added so a host
(and R1-d's jump) can set the zoom.

📐 **Measured on a real recording (R0 ⑧):** a component type from an assembly the process never LOADED stops the playback
(`PlaybackSystem.cs:332`, "Component type ID 238 not found" — `CharacterAnimationDefRuntime`, `Hrot.MuscleCharacter.Animation`):
`RecordingSearchService.RegisterAllComponents` sees loaded assemblies only. ⚠ The Replay Browser's own sandbox registers the same way
(`ReplayBrowserContext.cs:68`), so the index fails exactly where the browser would; `RecordingTimelineSource` now keeps the part scanned,
sets `Error`, and the panel says why — ⛔ never a faulted task and an endless "Indexing…".

📐 **R0 ⑧ measured** (`CE1301_R0_8_*`, a 216 MB / 14 245-frame / 596 s recording, editor assemblies loaded): one pass **46 s = 309 frames/s,
12.9× real time**; sidecar **115 kB**, reopened in **1.1 ms** (budget 200 ms). ⚠ Its frames 0–2 carry relative wall ticks (0.26–0.58 s) and frame 3
absolute ones — the mixed clock `6052a75a1` fixed in `RecorderTickSystem` the same day; a recording made before it shows its span squashed
(the browser's own scrubber too). ⛔ No timeline workaround: the cure is a fresh recording.

⏳ **Of §2b's cache tables only `events` is built.** `entities` (names, side) waits for R1-d's swimlane, which is its first reader; the
`position track` for R2b (Movement); the `pyramid` is replaced in R1-b by a binary search per pixel column (`Count(lane, from, to)`),
which already costs pixels × lanes × log(events) — a pyramid only if R1-b's < 1 ms budget is missed.

### 9b. Coordinator review of R1-a…c *(coordinator, `2026-10-26` — merged `ui@42f1ff7b5`)*

⭐ **Verdict: it is the timeline the design asked for, and it is measured.** One background pass on its own playback (12.9× real
time), a sidecar reopened in 1.1 ms, drawing by pixels in 0.59 ms, density / glyph / cluster / priority, zoom about the cursor, progress
shading, a failed build reported, clicks that seek. 🔒 The deviations of §9 / §9a are ACCEPTED as argued: D1 / TL-2 (seek through the
browser's manager, a per-host cursor), D3 / TL-1 (the panel in `Fdp.Presentation`), TL-6 (size + write time), the pyramid replaced by
a per-column count while under budget.

| # | gap — for the slice named | why it matters | lean |
|---|---|---|---|
| ① | **sim time per frame** — the index keeps `FrameTicks` (wall clock) only; `FrameMetadata` carries `Tick` and `WallClockTicks`, no sim time (`PlaybackController.cs:426-433`) | a take is written in SIM seconds (`AtExpr`, `TakeTimeline`), and so are `checks.json`'s windows; a recorded run that paused has wall gaps that sim time does not | **R2:** the builder also stores `FrameSimTime[]` from `GlobalTime.TotalTime` as it plays (it already holds the repository) |
| ② | **the director host's seek** — §1 / §3 / §4b say `ReplayBrowserContext.SeekToFrame` (known-rot) | D1: the browser seeks through `FederatedReplayManager` | **R2:** `ReplayDirectorHost` binds to the same `ITimelineCursor` as the panel — one seek path |
| ③ | **the TKB in the Replay Browser (R-4069)** is in §6's R1 and the handoff's R1 row, but in no slice of §9 | the swimlane's names, types and sides (R1-d) and the 3-D bodies need it | **R1-d** |
| ④ | the recording-replay defect found in R0 ⑧ had no tracker row | the browser itself cannot open such a recording | filed: **CE-4050** (replay harness) |

| ❓ answered by the user, `2026-10-26` | |
|---|---|
| **Q-a — TL-3 against R-4075** | 🔒 *"we need to unify and share, not duplicate and fix with adapters. we can move the editor code to some shared place."* ⇒ **R-4078**: ⛔ no `ITimelineRowProvider`, ⛔ no adapter — the editor's `ITraceLaneProvider` (+ `TraceLaneDescriptor`, `TraceLevel`) MOVES to the shared toolkit and becomes the ONE lane seam (§9c). TL-3 is SUPERSEDED |
| **Q-b — the extractors against R-4077** | 🔒 *"what are built-in lanes, timeline should be extensible, not hardcoded."* ⇒ **R-4079**: there are no built-in lanes — every lane is a REGISTERED definition; the ones shipped are definitions like any other. The `TimelineLane` / `TimelineLaneGroup` enums and the three hand-written extractors are SUPERSEDED (§9c) |

### 9c. Lanes are registered, never hard-coded *(R-4078, R-4079 — coordinator, `2026-10-26`)*

```mermaid
classDiagram
  direction LR
  class ITimelineLaneProvider { <<MOVED + generalised: today's ITraceLaneProvider>> +Lanes +Applies(context) +Marks(laneId, range) }
  class TimelineLaneDescriptor { <<MOVED: today's TraceLaneDescriptor>> +string Id +DisplayName +string Group +int Priority +Glyph +TraceLevel Levels }
  class TraceLevel { <<MOVED>> }
  class IndexedLaneDefinition { <<NEW, data>> +TimelineLaneDescriptor Lane +SearchPredicateDto When +LaneProjection Payload }
  class LaneProjection { <<NEW, data>> +OtherPath +PositionXPath +PositionYPath +ValuePath }
  class TimelineLaneCatalog { <<NEW>> +Register(provider) +Indexed() +ForContext(context) }
  class SearchPredicateDto { <<exists, EXTENDED — R-4077>> +RingEntryPredicateDto (one result per NEW entry of a recorded ring — TE-22 ②) }
  class IEventScannerCompiler { <<exists, EXTENDED>> results carry the projected payload }
  class PredicateCompiler { <<exists>> }
  class TimelineIndexBuilder { <<exists, reworked>> compiles every indexed lane once, evaluates all in ONE pass }
  class TimelineIndex { <<exists, reworked>> events by lane ID (string), not an enum }
  class TimelineSidecar { <<exists, reworked>> per lane: id + definition hash ⇒ a new lane is an incremental pass }
  class TimelinePanel { <<exists>> rows from the catalogue }
  class BTreeTraceLaneProvider { <<exists>> }
  class HsmTraceLaneProvider { <<exists>> }
  class UtilityTraceLaneProvider { <<exists>> }
  class CombatLanes { <<NEW: shipped DATA, registered by the combat toolkit>> Shot · Hit · Blast }
  class BehaviourLanes { <<NEW: shipped DATA, registered by the behaviour toolkit>> Changes · Orders · Reports · Decisions }
  class LifeLanes { <<NEW: shipped DATA>> Spawned · Left — LifecyclePredicateDto }
  class DirectorTracks { <<R2>> Camera · Time · Subtitles … }
  class UserLanes { <<NEW>> a predicate from the existing editor, saved like a search preset }
  ITimelineLaneProvider *-- TimelineLaneDescriptor
  TimelineLaneDescriptor --> TraceLevel
  IndexedLaneDefinition --> TimelineLaneDescriptor
  IndexedLaneDefinition --> SearchPredicateDto
  IndexedLaneDefinition --> LaneProjection
  TimelineLaneCatalog o-- ITimelineLaneProvider
  TimelineLaneCatalog o-- IndexedLaneDefinition
  TimelineIndexBuilder --> TimelineLaneCatalog
  TimelineIndexBuilder --> IEventScannerCompiler
  TimelineIndexBuilder --> PredicateCompiler
  TimelineIndexBuilder --> TimelineIndex
  TimelineIndex ..> TimelineSidecar
  TimelinePanel --> TimelineLaneCatalog
  TimelinePanel --> TimelineIndex
  BTreeTraceLaneProvider ..|> ITimelineLaneProvider
  HsmTraceLaneProvider ..|> ITimelineLaneProvider
  UtilityTraceLaneProvider ..|> ITimelineLaneProvider
  DirectorTracks ..|> ITimelineLaneProvider
  CombatLanes ..> IndexedLaneDefinition
  BehaviourLanes ..> IndexedLaneDefinition
  LifeLanes ..> IndexedLaneDefinition
  UserLanes ..> IndexedLaneDefinition
```

*What the picture shows that prose hid: ONE lane seam and ONE condition model. A lane either comes from the recording's index (a
predicate + which fields to keep) or is drawn by its provider for a range (live trace rings, the director's tracks, search hits) — and
both are registered the same way §15.2 already registers the trace lanes. Nothing in the index, the builder or the panel names a lane.*

| | how | measured basis |
|---|---|---|
| **the move** | `ITraceLaneProvider`, `TraceLaneDescriptor`, `TraceLevel` go from `Hrot.Editor.AiShared` to `Fdp.Toolkits` (`ReplayBrowser/Timeline`), which the index, the panel (`Fdp.Presentation`), the Replay Browser and the editor all reference. `AssetKind Kind` becomes `Applies(context)` — the asset kind is one key of the context. A rename, if any, by Roslyn (CLAUDE.md ②) | 7 files use it (`ITraceLaneProvider.cs`, `TraceTimelineWindow.cs`, three providers, two test files); `Fdp.Presentation` and `Hrot.Editor.AiShared` both reference `Fdp.Toolkits` |
| **an indexed lane** | a descriptor + a `SearchPredicateDto` (*when*) + a projection (*what to keep*: the counterpart, the point, the value) | R-4077; the scanner returns frame + entity + a message only today (`IEventScannerCompiler.cs:14-19`) ⇒ the projection is an EXTENSION of the model |
| **the shipped lanes as data** | Spawned / Left = `LifecyclePredicateDto` (exists) · Shot / Hit / Blast = `TransientEventPredicateDto` on `WeaponFireNotification` (fired) / `HitEvent` / `DetonationNotification` + projections · Changes / Orders / Reports / Decisions (R-4076) = the NEW `RingEntryPredicateDto` (one result per new ring entry, keyed by the entry's own counter — TE-22 ②) | today's `LifeExtractor`, `CombatExtractor`, `BehaviourExtractor` say exactly what each lane is (`TimelineExtractors.cs:64-121`) — they become their definitions' tests |
| **who registers them** | the module that owns the data — the combat toolkit its lanes, the behaviour toolkit its four, the trace editors theirs — as §15.2 already does | §15.2 *"Subsystems register their categories"* |
| **a user's lane** | built in the existing predicate editor (R-4072), saved like a search preset; the next open indexes it | the presets' JSON round-trip (capabilities doc §5) |
| **the cache** | events keyed by lane id; the sidecar keeps each lane's definition hash ⇒ a new or changed lane is ONE incremental pass for that lane, never a rebuild of the rest | — |
| **groups, priority, glyph** | descriptor fields, not enums | `TimelineLaneGroup` (3 values) and the priority order were enums (`TimelineIndex.cs:10, 33`) |

⛔ **Superseded by this section:** TL-3 (`ITimelineRowProvider` + an adapter); TL-5's lane list as code; `TimelineLane` /
`TimelineLaneGroup` (29 production references) and the three extractors as the index's source of events.

### 9d. §9c as built *(ui, `2026-10-26` — steer [STEER_Coordinator_To_Ui_Timeline_Lanes_Unified](blueprints/batches/STEER_Coordinator_To_Ui_Timeline_Lanes_Unified.md))*

**INVENTORY** *(run before building)*:
```
scripts/cbm.sh search_graph name_pattern=".*(TraceLane|TimelineLane|LaneProvider|LaneDescriptor).*" → 31 rows: ITraceLaneProvider + TraceLaneDescriptor
   (Hrot.Editor.AiShared/Debug), BTree / Hsm / UtilityTraceLaneProvider and their tests — no other lane seam
scripts/cbm.sh search_graph name_pattern=".*PredicateDto$" label=Class → 13, has_more false (grep adds BehaviourStepPredicateDto, newer than the index)
scripts/cbm.sh search_graph name_pattern=".*(Scanner|PredicateCompiler).*" label=Interface → 2: IEventScannerCompiler, IPredicateCompiler
   label=Class → EventScannerCompiler, PredicateCompiler (+ test doubles), DataBreakpointManager.CompiledEventScanner (the live breakpoints' own)
scripts/cbm.sh search_graph name_pattern=".*(History|Journal|DecisionLog)$" label=Class → UtilityDecisionLog (+ unrelated); the rings
   BrainJournal / CommandOrderHistory / CommandReportHistory are structs ([InlineArray] fields), read by grep
roslyn_find_references ITraceLaneProvider (Hrot.Editor.AiShared.csproj, is_msbuild_workspace true) → 6, = grep's 6 code sites
check_index_coverage is not available through the CLI ⇒ these totals are not proof of completeness
```

```mermaid
sequenceDiagram
  participant RB as ReplayBrowserSubsystem
  participant Cat as TimelineLaneCatalog
  participant Src as RecordingTimelineSource
  participant Car as TimelineSidecar
  participant Bld as TimelineIndexBuilder
  participant Cmp as TimelineLaneCompiler
  participant Pan as EventTimelineWindow / TimelinePanel
  RB->>Cat: Register(Replication, Combat, Behaviour lanes) at construction
  RB->>Src: Open(path, Cat.Indexed(Recording), compiler) when the primary recording or Cat.Version changes
  Src->>Car: TryRead(path)
  Car-->>Src: cached index + each lane's definition hash
  Src->>Src: reuse lanes whose hash matches, scan the rest
  Src->>Bld: Build(path, missing lanes) on a background task
  Bld->>Cmp: Compile(each lane) → event / structural / ring / rising-edge scan
  loop each played frame
    Bld->>Bld: every lane's scan → SearchResultDto (+ payload) → TimelineEvent(lane id)
  end
  Bld-->>Src: snapshots, merged with the reused lanes
  Src->>Car: Write(index, hashes)
  Pan->>Cat: Lanes(Recording) every frame — groups, names, colours, priorities
  Pan->>Src: Index (the newest snapshot)
```
*What it shows that prose hid: the only place lanes are NAMED is each toolkit's provider; the host lists providers, the index and the panel
iterate the catalogue. A lane added later costs one pass over the recording for THAT lane — the sidecar's per-lane hash decides.*

```mermaid
graph TD
  REP["Fdp.Toolkits/Replication — ReplicationTimelineLanes (Spawned, Left)"] --> CAT["TimelineLaneCatalog"]
  COM["Fdp.Toolkits/Combat — CombatTimelineLanes (Shot, Hit, Blast)"] --> CAT
  BEH["Fdp.Toolkits/Behavior — BehaviourTimelineLanes (Changes, Orders, Reports, Decisions)"] --> CAT
  USR["a user's IndexedLaneDefinition — catalog.Register(definition)"] --> CAT
  RBS["ReplayBrowserSubsystem.Update → SyncTimelineSource (each frame)"] --> CAT
  RBS --> SRC["RecordingTimelineSource → TimelineIndexBuilder (background)"]
  WIN["EventTimelineWindow.DrawClientArea (each frame)"] --> PAN["TimelinePanel.Draw(source, cursor, catalog.Lanes)"]
  TT["Editor: TraceTimelineWindow.RegisterProvider(BTree/Hsm/Utility)"]:::dead -.->|"a shell: draws 'No trace data' — R1-e hosts the panel"| PAN
  UI["a predicate editor for user lanes"]:::dead -.->|"not built: the API exists, no UI yet"| USR
  classDef dead stroke:#c33,stroke-dasharray:4 3,color:#c33
```
*The dead edges (red): the editor's Trace Timeline still registers its providers into a shell that draws nothing (R1-e makes it host the
panel), and nothing yet lets a user BUILD a lane — `TimelineLaneCatalog.Register(IndexedLaneDefinition)` is the seam the existing
predicate editor will call.*

| | as built | deviates from §9c |
|---|---|---|
| **① the move** | `ITimelineLaneProvider`, `TimelineLaneDescriptor` (renamed by Roslyn over stdio, 6 + 6 files), `TraceLevel` now in `Fdp.Toolkits/ReplayBrowser/Timeline`; the descriptor gained `Group`, `Priority`, `Color`, `Glyph` (defaulted, so the trace providers' 3-argument constructions stand); `AssetKind Kind` → `Applies(TimelineLaneContext)` (`AssetKind` + `Recording`); `Indexed` and `Marks(laneId, range, into)` are default interface members | — |
| **an indexed lane** | `IndexedLaneDefinition(descriptor, SearchPredicateDto, ResultProjectionDto?)` | ⚠ no separate `LaneProjection`: the projection is the predicate model's own `ResultProjectionDto` (R-4077 — one model), and search results carry it too (`SearchResultDto.Payload`) |
| **③ the model, extended** | `ResultProjectionDto` (subject / other / x / y / value paths, read off the match or off a component of the matched entity) + `CompiledProjection` (getters by the model's `PropertyEvaluator.BuildGetter`); `IEventScannerCompiler.CompileScanner(predicate, projection)` — one result per event; `RingEntryPredicateDto` (component, `[InlineArray]` field, count, key fields, optional filter) + `RingEntryScanner` (keys mixed without boxing; the first frame an entity is seen is its baseline); the Replay Search evaluates ring predicates too, and both use ONE `StructuralScanner` | ⚠ **behaviour fix**: an `Added`-only structural search no longer reports a destroyed entity as "Lost" (it did, regardless of the predicate) |
| **② shipped lanes as data** | Spawned / Left = **`StructuralPredicateDto`** on `NetworkIdentity` (gain / loss or destruction; X/Y from `SimTransform`) · Shot (fired: `Refusal == None`) / Hit / Blast = `TransientEventPredicateDto` + projections · Changes / Orders / Reports / Decisions = `RingEntryPredicateDto` (Orders' key includes `Repeats`, so a repeated order is news; Decisions keep slots whose `SwitchCount > 0`) | ⚠ Life lanes are structural, not `LifecyclePredicateDto`: measured, that predicate matches ONE named / numbered entity (`MatchesLifecycleCriteria` treats a network id as an entity index) — it cannot say "every unit". ⚠ "Destroyed" is named **Left** (a unit also leaves when its id is removed). ⚠ Reports' counterpart is the SENDER (`FromNetId`) — a projection reads one field, it has no "the other end" condition |
| **the compiler** | `TimelineLaneCompiler(IEventScannerCompiler, IPredicateCompiler?)`, passed in by the host | ⚠ `Fdp.Toolkits` does not reference `StructEdit.Reflection`, so the host supplies the edit service, as it does for the Replay Search |
| **④ the cache** | sidecar schema 2: per lane its id, a definition hash (SHA-256 of the id + the predicate's polymorphic JSON + the projection) and its events; `RecordingTimelineSource` reuses matching lanes and scans only the rest (`ScannedLanes`); the panel shows reused lanes at once | an unserialisable predicate hashes fresh — rebuilt every open rather than reused wrongly |
| **the extractors' knowledge** | `TimelineExtractors.cs` deleted; every rule it held is now asserted against the DEFINITIONS (`TimelineIndexTests`): a refused shot is not a shot, two orders in a frame are two events, a repeat is one more, the pre-recording order is a baseline, a death keeps its last position | — |
| **⑤ user lanes** | `TimelineLaneCatalog.Register(IndexedLaneDefinition)` + per-lane hashing ⇒ a user lane indexes like a shipped one, incrementally (rail: "who was hit") | ⏳ no editor UI to author one yet (dead edge above) |

## 10. R2-T — the director's tracks on the panel: the build plan *(ui, `2026-10-26`; build-state: DESIGN — dispatched by [NOTE_Coordinator_To_Ui_After_Review_Timeline](blueprints/batches/NOTE_Coordinator_To_Ui_After_Review_Timeline.md) item A1)*

**Scope** — the part of R2 (§6) that puts a take on the timeline: format v2 `keys[]` / `segments[]` (§5), the **Time** and **Camera**
tracks drawn and edited on the ONE panel (§2c rows 2, 3, 10), **K** / **J** / **L**, undo (TE-14). ⛔ Not here: TE-5 (the runner's move)
and `ReplayDirectorHost` — a take from a RECORDING plays once those land (§6 R2's acceptance); here a take plays on the editor's live run.

**INVENTORY** (`2026-10-26`, graph re-indexed — `search_graph` `.*Timeline.*` Interface → 3 / Class → 34, `.*(Director|Take|Shot|Reel).*`
Interface → 4 / Class, `.*Undo.*` → 2 interfaces, 2 stacks; `search_code` `ProgramAt|ReplayDirectorHost|CameraKey` → 0): the panel
(`TimelinePanel`, `TimelineView`, `TimelineLayout`, `TimelineAxis`, `ITimelineCursor`, `TimelineMenuItem`) draws only index EVENTS — ⛔ no
editable item, no track seam; the take (`Take`, `ShotSpec`, `CameraSpec`, `AtExpr`, `TakeJson`, `TakeTimeline.Plan`) has only `shots[]`;
`ProgramAt`, `keys[]`, `segments[]`, `ReplayDirectorHost` do not exist; the undo stacks are `GizmoUndoStack` (Fdp.Toolkits — a record per
edit), its GizmoMap twin, NodeEditor's `UndoStack` (graph commands only) and Blueprints' `CommandHistory` (not reachable from Presentation).

| the plan rests on | code — how it is | design basis |
|---|---|---|
| the panel cannot reference the take (FDP below Hrot) | ✅ `TimelinePanel` in `Fdp.Presentation`; `Take` in `Hrot.Presentation/Map3D/Director` | ✅ §9 D2/D3 (the panel lives in Fdp.Presentation) |
| the panel's rows are the index's lanes only | ✅ `TimelinePanel.Draw` → `DrawRow(index, lanes…)` | ✅ §2a: a track is a row of the SAME panel (R-4073) |
| a key is the unit; old `shots[]` read as keys | ✅ `ShotSpec` (`At`, `Seconds`, `Camera`, `To` = a fly) | ✅ TE-2 |
| keys stay in SIM seconds; drawn through the session's map on Wall | ✅ `ITimelineCursor.TimeMap`, `TimelineAxis.IsSim` | ✅ §2c row 10 |
| an edit is undoable by an EXISTING stack | ✅ `GizmoUndoStack.Push(IGizmoUndoRecord)` (Fdp.Toolkits, reachable) | ✅ TE-14 ("no fifth") |
| a drag never moves the world | — | ✅ §2c row 2 |

```mermaid
classDiagram
  direction LR
  class TimelinePanel { <<exists, grows>> Tracks : Func~IReadOnlyList~ITimelineTrack~~ · Draw(source, cursor, lanes) unchanged · tracks above the lanes }
  class TrackLayout { <<NEW, static, pure>> AxisTicks / SimSeconds · SpanOf · HitTest · SimAtX · OfferKey }
  class TrackGesture { <<NEW, pure>> Press · Update · Release — select / preview / commit once }
  class ITimelineTrack { <<NEW, Fdp.Presentation>> Id · Label · Items · Select(id) · Move(id, toSimSeconds, commit) · Key(char, atSimSeconds) bool · MenuItems(id?) }
  class TimelineTrackItem { <<NEW, record>> Id · From · To? (sim s) · Label · Kind (key / segment) · Selected }
  class DirectorTracks { <<NEW, Hrot.Presentation/Map3D/Director>> Time + Camera tracks over the edited take }
  class TakeEditor { <<NEW>> Take (current) · Apply(edit) · Undo / Redo via GizmoUndoStack · Dirty · Save → TakeStore }
  class TakeEditRecord { <<NEW>> IGizmoUndoRecord: before / after take snapshots (TE-14) }
  class Take { <<exists, grows>> + Keys · + Segments (format v2) }
  class CameraKey { <<NEW, record>> At · Camera (CameraSpec) · In (cut / fly(s, ease) / dip(a, b)) · Fov? · Still? }
  class TimeSegment { <<NEW, record>> From · To · Speed · Gap (cut / dip) }
  class TakeJson { <<exists, grows>> reads keys[] / segments[]; shots[] read AS keys (TE-2); writes v2 }
  class TakeTimeline { <<exists, grows>> + ProgramAt(simTime) → key, blend, fade }
  class GizmoUndoStack { <<exists, Fdp.Toolkits>> }
  TimelinePanel --> ITimelineTrack : draws, hit-tests, drags
  TimelinePanel --> TrackLayout : places items on the active axis
  TimelinePanel --> TrackGesture : one per panel
  TrackGesture ..> ITimelineTrack : Select / Move(preview) / Move(commit)
  DirectorTracks ..|> ITimelineTrack
  DirectorTracks --> TakeEditor
  TakeEditor --> Take
  TakeEditor --> GizmoUndoStack : Push(TakeEditRecord)
  Take *-- CameraKey
  Take *-- TimeSegment
  TakeJson ..> Take
  TakeTimeline ..> CameraKey
```
*What the picture shows that prose hid:* the panel learns one small interface and nothing about takes; the take's editor owns undo and
saving, so a drag on the panel and an edit in the Director window are the SAME edit path.

```mermaid
sequenceDiagram
  actor U as User
  participant P as TimelinePanel
  participant D as DirectorTracks
  participant E as TakeEditor
  participant S as GizmoUndoStack
  participant R as DirectorRunner
  U->>P: press on a Camera key, drag
  P->>D: Select(item) then Move(item, simSeconds, commit:false) each frame (preview only)
  U->>P: release
  P->>D: Move(item, final, commit:true) once
  D->>E: Apply(MoveKey(id, at))
  E->>S: Push(TakeEditRecord(before, after))
  E-->>R: the runner reads the edited take on its next ProgramAt
  Note over P,E: the world is never touched — an edit of the TAKE (§2c row 2)
  U->>P: K (panel focused)
  P->>D: Key(K, playhead sim s)
  D->>E: Apply(AddKey(pilot camera pose, at, in: last used))
```

| # | slice | builds | accepted when |
|---|---|---|---|
| **T1** ✅ BUILT `2026-10-26` | format v2 | `CameraKey`, `TimeSegment`, `Take.Keys` / `Segments`; `TakeJson` reads `keys[]` / `segments[]`, reads `shots[]` AS keys (a shot → a cut key at `at`; a shot with `to` → a fly key at its end), writes v2; `TakeTimeline.ProgramAt` | every seeded take loads and lays out as before (55 takes); a v2 file round-trips |
| **T2** ✅ BUILT `2026-10-26` | the track seam | `ITimelineTrack` + `TimelineTrackItem`; the panel draws tracks above the lanes, hit-tests, selects on a click, drags (preview each frame, commit on release); keys on the active axis through the time map | a frame rail: a drag moves the item, a click only selects, empty space still does nothing |
| **T3** | the director's tracks | `DirectorTracks` (Time = segments, Camera = keys), `TakeEditor` + `TakeEditRecord` on `GizmoUndoStack`, save through `TakeStore`; **K** adds a key from the pilot camera (relative to a selected unit, TE-2); **J** / **L** the rate | a key moved on the panel is where the saved file says; undo puts it back; K adds one at the playhead |
| **T4** ✅ BUILT `2026-10-26` | playing keys | `DirectorRunner` plays `ProgramAt`: cut, fly (eased), dip (TE-8) | a take plays from the panel with its keys where the take's file says (note A1's acceptance) |

**T2 as built** — `ITimelineTrack` + `TimelineTrackItem` (`Id`, `From`, `To?` in SIM seconds, `Label`, `Kind` key / segment, `Selected`) in
`Fdp.Presentation/ImGui/Panels/Timeline/`; the panel takes `Tracks` (a `Func<IReadOnlyList<ITimelineTrack>>?` PROPERTY — `Draw`'s signature is
unchanged, every existing host untouched) and draws them as rows at the top of the lanes child, so the "now" line, range and pause shading cover them.
The rules are two pure classes: `TrackLayout` (sim seconds ⇄ axis ticks ⇄ x on BOTH axes — Sim: seconds × ticks; Wall: `ReplayTimeMap.WallAt` / `SimAt`,
so a key inside a collapsed pause draws at the pause's wall END and a drop there reads back as the pause's instant; no map ⇒ not drawable, the row's
tooltip says so) and `TrackGesture` (press on an item selects, a drag past 3 px previews `Move(commit:false)` each frame with the grab offset kept, the
release — wherever — commits ONCE; a press on empty space starts nothing). A drop snaps to the nearest frame of the axis-projected index. Right-click
adds `MenuItems(itemId)` (null on empty space) in front of the mode's items. **K / J / L** are offered through `TrackLayout.OfferKey` to the track holding
the selected item, before the panel's keymap (which has no K/J/L; every existing key unchanged). **Deviations from the diagram above (corrected):**
`Items` takes no axis argument (the panel converts, §2c row 10); `Move` carries `commit`; `MenuItems` takes the item id, not a context; `Tracks` is a
property, not a `Draw` parameter; two helper classes were added. Rails: `TimelineInteractionTests.CE1364_T2_*` ×6 (placement on both axes through a
pause, frame snap, click-only-selects, drag preview + one commit, empty space, K to the selected track) and the frame rail
`CE1364_T2_ATrackWithTwoKeys_*` (injected `AddMousePosEvent` / `AddMouseButtonEvent`; precedent `tools/ui-probe/synthetic-input`). ⚠ Not built:
event-snapping within 7 px on a drop (frame snap only).

**T1 as built** — `KeyTransition` (Cut / Fly(s, ease) / Dip(out, in)), `CameraKey`, `TimeSegment`, `Take.Keys` / `Segments`; `CameraKeys.Of(take)`
is the ONE place a take's keys come from (its own `keys[]`, else its `shots[]` per TE-2); `TakeJson` reads / writes v2 and the camera read /
write is now one helper shared by shots and keys (`ParseCamera` / `WriteCamera`); `TakeTimeline.ProgramAt` reuses `ShotEvaluator.Eased` (no
second easing). Rails `FilmDirectorTests.TE2_*` ×5, `T1_ProgramAt_*` — each red-proved; the 55 seeded takes read unchanged and write back v1.

**T4 as built** *(ui, `2026-10-26`)* — `DirectorRunner.DirectUpcoming` now holds the camera `ShotEvaluator.Evaluate(ProgramFrame)` returns for
`TakeTimeline.ProgramAt(CameraKeys.Played(take, plan), anchors, t)` — `Played` is `Of` with each shot's start taken from `Plan`'s layout (a shot
listed after one still running starts LATER than its `at`; measured in `scenarios/pc-defend-house/takes/main.take.json`, whose third shot's anchor
precedes its second's end — keyed at the raw `at`, that seeded take would have filmed differently, so `Played` re-times it; same per-shot helper as `Of`); ⛔ there is no second shot-playing path (a v1 take is read AS keys, TE-2). `Plan`'s
shots stay, but only as the **stretches the film runs in** (captions, layers, speed, capture, the end) — and a take with `keys[]` and no `shots[]`
gets one stretch per placed key (`TakeTimeline.Stretches`), so a v2 take runs at all. The dip's black is the same frame's `Fade`, carried by one new
mailbox value `FilmStudio.Fade` (set by the runner, cleared by `Release`), drawn over the live picture by `EditorCinema` and over every captured scene
frame in `Film.cs`'s capture. Deviations, each SUPERSEDING the T1 text:
- **⛔ SUPERSEDED (T1): "a fly key blends from the key before it over its seconds AFTER its time."** A fly **arrives AT its key's time**, over the
  seconds before it (clamped to start no earlier than the key it leaves) — TE-8's "how the camera gets TO a key", the dip's out-window shape, and the only
  reading under which `CameraKeys.Of`'s "fly key at the shot's end" reproduces a shot's `to`. T1's `ProgramAt` rail was moved to match (`T1_ProgramAt_*`).
- `ProgramFrame.Key` is the key flown INTO while a fly is under way; new `Since` = sim seconds since the key the camera leaves (an orbit's clock).
- A fly between cameras of **different kinds** (a follow into a look-at) has nothing to blend: the leaving camera stays, the arriving one cuts in at the
  fly's end. Same-kind cameras blend their numbers with the arithmetic `ShotEvaluator.At` already used.
- Gap hold unchanged: in a gap the camera is the NEXT stretch's at its start; the black is of NOW.
Rails `FilmDirectorTests.CE1364_T4_*` ×4 (fly, dip, keys-only layout, and ⭐ all 55 seeded takes + a hand-made one play the SAME shots through the key path
as through the old per-shot path, 0.25 s samples) and `DirectorRunnerTests.CE1364_T4_TheRunner_PlaysTheKeys_*`.

⚠ **OPEN before T2 — the live panel has no sim axis** *(measured `2026-10-26`)*: the editor's live index records only WALL ticks
(`LiveTimelineSource.cs:61` → `TimelineFrameScanner.Step(…, wallTicks, …)`) and its cursor carries no time map
(`EditorSubsystem.cs:5285`, `new LiveTimelineCursor(...)` with no `timeMap`) ⇒ a key in SIM seconds (§2c row 10) has nothing to be placed
through on a live host. ⭐ Lean **T-L5**: the live scanner records each frame's sim time beside its wall ticks (it already steps the world
every frame) and the live cursor exposes `ReplayTimeMap.FromFrames` over them — the SAME map a recording's v7 frame index gives, one node's
own frames; ⛔ not a second map owner: when backend's `ReplaySessionTopic` (note B, item 6) lands, the session's map replaces it on the
cursor. Rejected: wait for B (blocks A1, which the note says nothing blocks) · place keys on wall ticks (breaks §2c row 10 the moment a
pause collapses).

**Leans** (ask before building any that the user overturns): **T-L1** the seam is a NEW `ITimelineTrack`, ⛔ not a lane provider (lanes are
READ from an index; a track's items are EDITED — two different lifetimes) · **T-L2** undo on `GizmoUndoStack` (its `IEntityCommandBuffer`
argument is unused by a take record — a wart, not a fifth stack) · **T-L3** one `TakeEditor` per open take, shared by the panel and the
Director window (one edit path) · **T-L4** T1 keeps reading `shots[]` and WRITES v2 only when a take is edited (seeded takes stay as they are
until touched).

## 8. INVENTORY

```
scripts/cbm.sh search_graph name_pattern=".*(Director|Replay|Film|Take|Timeline|Cinema|FrameSink|ShotWorld|Playback).*" label=Interface
   → total 7, has_more false: ICinema, IDirectorHost, IDirectorSound, IFrameSink, IShotWorld, IRecordReplayController, IReplayRestoreGated
scripts/cbm.sh search_graph name_pattern=".*" label=Class file_pattern="**/Director/**"   → total 94, has_more false
   → shared (Hrot.Presentation/Map3D/Director): Take, ShotSpec, CameraSpec, AnchorSpec, AtExpr, BakedAnchors, LayerChange, Reel, ReelItem,
     ReelWaiting, TakeJson, TakeStore, TakeTimeline, TimelineShot, ShotEvaluator, WorldUnits, AnchorBook, AnchorMismatch, PacedClock
   → editor (Hrot.Editor/Director): DirectorRunner, PlayRequest, CaptureOptions, ItemReport, CutSnapshot, VerifyOptions, DirectorWindow,
     EditorDirectorHost, EditorCinema, EditorLayerOverride, FilmFrameSink, EditorDirectorSound, ScenarioReadyProbe, ScenarioChecks,
     ScenarioVerification, CommandLineVerify, FreshVerifyJobs, EditorVerifyWorld
scripts/cbm.sh search_graph name_pattern=".*(Director|Take|Reel|Timeline|Replay).*" label=Class → total 114 (first 83 read): ReplayBrowserSubsystem,
   ReplayBrowserContext, FederatedReplayManager, ReplaySearchPanel, ReplayModule, RecordingModule, EcsRecordReplayController, … 
   ReplayTimelinePanel (Fdp.Presentation) — the Replay Browser's transport; the director panel REUSES its scrub/transport, not a second one
scripts/cbm.sh search_graph name_pattern=".*(Undo|Redo|CommandHistory|EditHistory).*" label=Class
   → four undo stacks: Blueprints CommandHistory, Fdp.Toolkits GizmoUndoStack, GizmoMap GizmoUndoStack, NodeEditor UndoStack (TE-14)
scripts/cbm.sh search_graph name_pattern=".*(Condition|Predicate|Trigger|Zone|Area|PhaseLine|Region).*" label=Interface → 7: IAreaOutline,
   IDangerAreaProvider, IPredicateCompiler, ISearchPredicateRegistry, IZoneAuthoringController, IZoneRowRenderer, IZoneTileLoader
scripts/cbm.sh search_graph name_pattern=".*(PredicateDto|ConditionDto|TriggerDto|Trigger|MissionCondition|…Crossing|Enter).*" label=Class
   → the SearchPredicateDto family (Compound, PropertyMatch, Numeric, String, Enum, TransientEvent, Lifecycle, SpatialBounding, Structural,
     BehaviorParam, TraceBufferScan, BlueprintVariable, ExternalHitTag) · MissionTrigger (string-typed: TimerElapsed, HealthCritical,
     BehaviorFinished, UnderAttack — no area or line) · no line-crossing or polygon condition
scripts/cbm.sh search_graph name_pattern=".*(Condition|Predicate|Trigger).*(Editor|Panel|Window|Drawer|Inspector|Widget).*" label=Class
   → PredicateValueFieldDrawer, PredicateValueFieldEditor; hosts found by grep: ReplaySearchPanel, DataBreakpointManagerPanel (+ PredicateBuilderState)
grep "SearchPredicateDto" → 31 production files: breakpoints (8), debug API (5), BTree/HSM/Blueprint breakpoint menus, compiler, Replay Browser
cluster seek: ClusterOpType.ReplaySeek, NodeReplaySeek, ClusterMaster.Replay, ReplaySeekProcessManager · transports: ITimeTransportFacade,
   EditorTimeTransportFacade, ClusterTimeTransportAdapter, MainToolbarTimeControlSection, ShellTimeControlToolbar
⛔ MISSED in the first two passes, found 2026-10-23 from the ui lane's DESIGN_Behaviour_Debugging.md: TraceTimelineWindow + ITraceLaneProvider
   (+ BTree / HSM / Utility lane providers) — the first query's 114 results were read only to 83 (truncated) — a coverage stop, not a fit
   stop, would have caught it. ⇒ TE-21
docs: AI_Editor_Shared_Infrastructure.md §15 · DESIGN_Behaviour_Debugging.md · DESIGN_Entity_Brain_View.md BV-30 · Predicate-Infrastructure-Capabilities.md · designs/breakpoints-1/DESIGN.md · DESIGN_Time_Architecture.md §11 ·
   DESIGN_Film_Director.md (§3e, D-18) · designs/replay-browser-2/DESIGN.md · DESIGN_Agent_Shared_View.md (R-1029) · DESIGN_Scenario_Regression.md ·
   DESIGN_Map_3D_Mode.md §6b · searched docs/ for "director editor", "camera keyframe", "timeline editor", "film graphics" → none found
check_index_coverage is not available through the CLI ⇒ these totals are not proof of completeness
```

*Reused as is:* `FilmStudio`, `FilmCaptureLayer`, `ShotEvaluator`, `CameraSpec`, `AtExpr`, `ReplayBrowserContext`, `RecordingSearchService`,
the reel format, capture and the sound track. *Grown:* `Take`, `AnchorSpec`, `AnchorBook`, `TakeTimeline`, `TakeStore`, `IDirectorHost`,
`DirectorWindow` (into the timeline panel). *New:* `ReplayDirectorHost`, `FilmGraphicsLayer`, the key/segment/subtitle/view/graphic records.
