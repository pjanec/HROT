<!--STATUS
state: LIVE
build-state: BUILDING — ⭐ PW BUILT 2026-10-26 (§12f; CE-3331 closed) · P0 BUILT (§12g) · P1 core BUILT (§12h) · ⏸ P2 STOPPED — module-level declaration cannot work, lean per-system (§12i) · P3 BUILT (§12j) · was READY-TO-BUILD — ✅ every decision answered (R-3057…R-3063; §11f: the full checkpoint, D-A OVERTURNED — the review is ON the wire, §12 disposes every potential zombie at entry, at every seek and at exit). ✅ READY-TO-BUILD (2026-10-26): M1 / M4 / M5 answered (§7) — PW, then P0…P4 when scheduled. ⭐ PW (the wire, §8 rows 35–39) goes first: it also fixes CE-3331, a zombie hole in today's replay seeks and checkpoint restores. Principles ⭐ APPROVED 2026-10-26 (R-3057: R1…R12; R-3058: the axis switches at runtime; R-3059: no state change on a left click, any host; R-3060: the existing toolbar grown by two icons, the Replay Browser keeps its own controls; R-3061: the mode icon is an indicator)
updated: 2026-10-26 (§12b W1 revised: the sweep is change-driven — EntityRepository.ClearGeneration, a delta query otherwise; the per-frame sweep SUPERSEDED) · 2026-10-26 (M1 / M4 / M5 answered — W3: DDS acks exist, the .NET binding lacks wait_for_acks; W5: state is left in the reader during a replay, the exit discards it; M1: a one-flag reader fix; READY-TO-BUILD) · 2026-10-26 (§11f the user's answers; §12 the review on the wire — the zombie cleanup; §0, §3b, §3c, §4, R4, R6, §7, §8 amended; R-3062, R-3063; CE-3331) · 2026-10-26 (§11 — critical review: F1–F21 with file:line, D-A…D-H for the user, what is missing before READY-TO-BUILD) · 2026-10-26 (R-3061 — the mode icon is an INDICATOR whose click opens a menu (never an action), Edit and Preview get their own; §10 the timeline panel's zoom / pan reviewed against the built code, its interaction spec) · 2026-10-26 (R-3060 — §9 rewritten: ASCII wireframes, the existing time-control section grown by a mode icon and an axis icon, Stop = back to live in a review, the Replay Browser keeps its own controls, §9f the messages incl. ReplaySessionTopic, §9g what loading includes) · 2026-10-26 (R-3059 — the timeline's left click never changes state, seeking in its right-click menu; one UI for review, cluster replay and the Replay Browser; every control from any host; §9 wireframes, §9d who fills the timeline on each host) · 2026-10-26 (approved — R-3057, R-3058; §3a the state-machine additions as a table, §8 every system change, §9 the review UI on a live cluster host) · 2026-10-26 (first version — backend, from the user's request: the sim-time seek/step that skips unchanged pauses, its UI, and During Action Review)
current-answer: ⭐ §12f–§12j what is built (PW, P0, P1, P3) and P2's stop · §12f PW as built · §11f the answers + §12 the wire · §0 the decision · §3 UML (§3a states, §3b classes, §3c the review's sequence) · §5 decisions · §7 build order · §8 the system changes · §9 the UI · §11 the review's findings (each now folded into a row of §8)
stale-below: nothing
known-rot: §11d's D-A lean (wire-silent) is OVERTURNED — §12. §1's claim rows and §2's INVENTORY predate §11 / §12 (the wire and checkpoint rows are in §12c with their file:line). replay-and-modules §2.2 / §2.3 ("IG nodes … do not record and do not replay") are SUPERSEDED — every recording node replays (R-4010)
known-conflict: DESIGN_Director_Timeline_Editor.md TL-4 (time = frame index + wall ticks) and TE-19 (a live host never seeks) — this design ADDS a sim axis and makes a seek on a live host ENTER a review (R10); both stand until the user approves §5
related-designs:
  - DESIGN_Http_Control_Through_The_Cluster_Master.md — OWNS the replay routes and R-3056 (wall axis; the sim form deferred) that §5 R1–R4 extend
  - DESIGN_Checkpoint_And_Dry_Run_Restore.md — OWNS the dry run's RAM keyframe, AfterRestore, SnapTo (D5) and the branch copy (D18) that DAR's return reuses (R6)
  - designs/mgmt-1/DESIGN.md — OWNS §8.12 "Always Recording" (paused frames are kept on purpose), §8.10 replay gates, §8.11 the branch, §3 the operator state machine
  - designs/replay-and-modules/DESIGN.md — OWNS the replay isolation table (§2.1), "restore the world, re-derive the protocol" (§2.1d) and §5.7 (recording and replay modules exclusive — R8 relaxes it for a review)
  - DESIGN_System_State_Inventory.md — OWNS the K/A/B/C/S/X classes that decide which module state survives a review (R7)
  - DESIGN_Director_Timeline_Editor.md — OWNS the one timeline panel and its index (TL-4, TE-17, TE-19, §9b gap ① FrameSimTime) that R10 extends
  - DESIGN_Film_Director.md — §3c "instant replays" asks DAR's question ("can the recording be shown while the live run is paused, then the live world come back untouched?"); this answers it
  - DESIGN_Scenario_Regression.md — §10-1 H1b needs a sim-frame address through the product's control (R1/R3)
  - DESIGN_Replay_Browser_Debug_Http.md — OWNS the Replay Browser's seek forms and timeline over HTTP; the browser gets the same axis (R10)
  - blueprints/DESIGN_Time_Architecture.md — OWNS SnapTo (§12a) and the replay clock (§12: the playhead is the time controller's TotalTime)
  - blueprints/Architect_Question_91_Player_Nodes.md — §4-P OWNS the replay zero (R-4012), the entry seek round and the end-of-replay pause (P7)
-->
# The replay's time axis, and **During Action Review** (DAR)

🔒 **User, `2026-10-26`:** *"design the seek/step form that works with simulation time and skips the unchanged pauses including the UI for it in
the replay browser and not just there (also in the live cluster hosts) … resolve the question about how we choose what is the time axis we work
with? how we identify what is 'unchanged pause' to skip? What the time line will show in both modes? … having the design early might change what
the http api and data structures … look like now"* · DAR: *"Live simulation gets paused (and its state remembered so it can be restored to live
later). We switch to a temporary replay mode … At any point … return back to the live simulation, to where we paused it … When the DAR replay
reaches the paused live point, it is paused automatically."* · *"If any simulation state changed during the pause, it will be replayed as a
sudden change at the time of the unpause."* · *"statefull modules that does not use snapshot save/restore stays in memory … need to make sure
during DAR that do not respond to any messages replayed during DAR."*

## 0. The decision (leans — §5)

⭐ **ONE replay clock (wall), TWO views of it.** Every recorded frame already carries both clocks — wall ticks in its index, the run's
`GlobalTime` in its body — but only wall is cheap to read. ⇒ **P0, now:** the recorder writes the frame's SIM time and a HALTED / CHANGED
flag into the frame index (format v7). From that, each node builds a **`ReplayTimeMap`** (wall ↔ sim, with the paused stretches), the master
aggregates one for the cluster, and the **SIM axis** is the wall clock with every paused stretch collapsed to an instant: a seek in sim seconds
is a lookup, a sim-axis play jumps over the stretches, and whatever changed inside one lands at once — *"a sudden change at the time of the
unpause."* Nodes keep aligning on wall ticks (R-4012) — no second alignment rule.

⭐ **DAR is a REVIEW of the live run, in place:** `OperatingLive → LoadingReview → OperatingReview → UnloadingReview → OperatingLive`. Entry
pauses live on the synchronised barrier, captures each node's FULL checkpoint in RAM (every recorded type + the singletons — R6 as amended),
SUSPENDS the live recording and replays it up to the live point on the SIM axis — ON THE WIRE, with a dispose for every entity that ceases to
exist at each swap (§12). Exit applies the checkpoint with the checkpoint restore's semantics (`ResumeAfterRestore`), brings the clock back to
the live point, resumes the SAME recording — live, paused. Everything that is not world state does not run during the review, so it neither changes nor hears anything (R7).

## 1. Claim table — what the leans rest on

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| paused frames are recorded, on purpose | ✅ `RecorderTickSystem` is `EveryFrame` (`RecorderTickSystem.cs:17`), writes every frame (`:86-127`) | ✅ mgmt-1 §8.12 *"The AsyncRecorder does not go idle when simulation time is paused"*; invariant *"never assume a one-to-one mapping between simulation ticks and wall-clock time"* |
| the frame index holds wall ticks only | ✅ `FrameOuterHeader` = sizes, `Tick` (render counter), `FrameType`, `WallClockTicks` (`FrameOuterHeader.cs:11-16`); `FrameType` is Delta / Keyframe only | ✅ Director Timeline §9b gap ① leans to store `FrameSimTime[]` |
| the recorder HAS the sim time when it writes the header | ✅ it reads `GlobalTime` for the wall stamp in the same lines (`RecorderTickSystem.cs:86-88`) | — |
| a paused frame = recorded `DeltaTime == 0` | ✅ `GlobalTime.IsAdvancing => DeltaTime > 0` (`GlobalTime.cs:79`); master stepping writes 0 on non-step frames (`MasterSyncController.cs:625-630`) | ✅ R-126: the one source of "paused" is the clock |
| nodes align on wall ticks | ✅ seek payload = relative wall ticks + the master's anchor (`ClusterMaster.cs:1871`, `ReferenceReplayLoadHandler.cs:215-235`); playhead = anchor + `TotalTime` (`PlaybackTickSystem.cs:103`) | ✅ R-4012 one zero; R-3056 wall axis |
| nodes' sim clocks match only at step boundaries | ✅ slaves snap `TotalTime` to the step's target (`SlaveSyncController.cs:394-406`), else follow the synced wall clock | — ⇒ a second, sim-based alignment rule is rejected (R4) |
| live → replay today DESTROYS the live world | ✅ planned `OperatingLive → UnloadingLive → Idle → LoadingReplay` (`HrotStateGraph.cs:44-50`); Idle runs `WorldBoundaryReset.Clear` (deletes on the wire) | ✅ R-4019 entering Idle empties the world ⇒ DAR needs its own edge (R5) |
| a RAM keyframe + `AfterRestore` + `SnapTo` returns a world | ✅ the dry run: `ReferencePreviewHandler.cs:178-237`; clock `PreviewClockProcessManager.cs:39-55` | ✅ Checkpoint D14, D5, D4 |
| replayed EVENTS never reach live logic | ✅ the cluster replay leaves `PlaybackController.EventBus` null (`PlaybackSystem.cs:195-205`) | ✅ replay-and-modules §2.1c (state restore, not re-execution) |
| modules outside the toggle groups are NOT gated in a replay | 🔴 ✅ only the four toggle groups are switched (`ReferenceReplayLoadHandler.cs:342-348`); capability-walk modules keep ticking (`SharedApplicationBootstrapper.cs:262-264`) | ⛔ searched — no design names it; R7 closes it |
| a replay already pauses itself at a point | ✅ `ReplayProcessManager.cs:60-96` publishes `PauseTimeIntent` at the end | ✅ AQ91 P7 |
| the recording file can be read while its writer holds it | ⚠ `PlaybackController` opens `FileShare.Read` (`:72`), the writer `FileAccess.Write` (`AsyncRecorder.cs:127-132`); `RecordingSlice` opens `ReadWrite` (`:28`) and reads a live recording (checkpoint restore) | ⛔ unmeasured for `PlaybackController` — **measure first (§7 M1)** |
| SUMO (class X) cannot be restored, only frozen | ✅ `DESIGN_System_State_Inventory.md` §2c; SUMO does not step in replay (R-4027) | ✅ R-4027 |

## 2. INVENTORY *(codebase-memory `search_graph` + grep, `2026-10-26`; ⚠ `check_index_coverage` is not available through the CLI)*

| query | total | what it found |
|---|---|---|
| `search_graph name_pattern ".*Replay.*(Time\|Clock\|Controller\|Handler\|Module).*" label Class` | 12, `has_more: false` | `EcsRecordReplayController`, `ReferenceReplayLoadHandler`, `ReplayMasterModule`, `ReplayModule`, `ReplayTimelinePanel(+ViewModel)`, `ReplayTimelineWindow` (+ tests) — ⛔ no replay clock or time-map class exists |
| grep `FrameSimTime\|SimTime\b` over `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Timeline` | 0 | the timeline index keeps `FrameTicks[]` only (`TimelineIndex.cs:35`) |
| grep `DuringAction\|Review` states / ops over `Orchestration/Enums` | 0 | no review state or op |
| grep `ISystemStateful\|IStatefulSystem\|ISnapshotRestorable` | 0 | state classes are a document + rail (`EverySystemStateFieldIsClassifiedTests`), not an interface |
| the timelines (grep `SliderInt\|ITimelineCursor\|##OrcSeek`) | 4 | `ReplayTimelinePanel` (frame slider), `TimelinePanel` (Event Timeline, wall ruler), `ClusterScenarioPanel` `##OrcSeek` (wall seconds), Director `TakeTimeline` (sim seconds — a plan, not a scrubber) |

## 3. UML

### 3a. The cluster states — what DAR adds

```mermaid
stateDiagram-v2
  [*] --> Idle
  Idle --> LoadingLive
  LoadingLive --> OperatingLive
  OperatingLive --> UnloadingLive
  UnloadingLive --> Idle
  Idle --> LoadingReplay
  LoadingReplay --> OperatingReplay
  OperatingReplay --> UnloadingReplay
  OperatingReplay --> LoadingLive : branch (D18)
  UnloadingReplay --> Idle
  OperatingLive --> LoadingReview : NEW - pause, capture, suspend recorder
  LoadingReview --> OperatingReview
  OperatingReview --> UnloadingReview : NEW - return to live
  UnloadingReview --> OperatingLive : live, paused, at the live point
  state OperatingReview {
    [*] --> ReviewPaused
    ReviewPaused --> ReviewPlaying
    ReviewPlaying --> ReviewPaused : operator, or the live point reached
  }
```
*What the picture shows that prose hid:* a review never touches Idle, so nothing is deleted on the wire and no content reloads; and its ONLY
exit is back to live — a review that wants a replay or a stop goes through `OperatingLive` (the planner's BFS gives that path for free).

**The additions, exactly** *(both enum copies — `Fdp.Toolkit.Orchestration.ClusterState` and the DDS mirror `Hrot.NED.Descriptors.Orchestration.ClusterState` — stay
value-identical, as today)*:

| kind | added | today's neighbour it mirrors |
|---|---|---|
| states | `LoadingReview = 50`, `OperatingReview = 51`, `UnloadingReview = 52` | the Replay block 40 / 41 / 42 |
| sub-states of `OperatingReview` | `ReviewPaused` ⇄ `ReviewPlaying` — the cluster clock's pause, as in `OperatingLive`; ⭐ entering pauses; the live point pauses | `OperatingLive`'s paused / playing |
| edges (`HrotStateGraph`) | `OperatingLive → LoadingReview` · `LoadingReview → OperatingReview` · `OperatingReview → UnloadingReview` · `UnloadingReview → OperatingLive` | ⛔ no edge from a review to Idle, a replay or a branch |
| node ops (`NodeOpType`, both copies) | `PrepareReview` (on `LoadingReview`), `FinalizeReview` (on `UnloadingReview`) — chosen by the master's prepare-op mapping (`ClusterMaster.cs:1477-1486`) | `PrepareReplay` / `FinalizeReplay` |
| the request | the ordinary `TransitionState` to `OperatingReview` + `atSeconds \| atSimSeconds` (default: the live point); `TransitionState` to `OperatingLive` from a review = the return | R-3055: one request, the planner plans the way |
| refusals | a review only from `OperatingLive` (playing or paused); a second review while one runs is the same state (a seek) | the checkpoint restore's "only from OperatingLive" |
| the session's settings | `ReplayAxis` (Sim \| Wall, runtime — R-3058) and the live point (wall + sim) travel in the cluster status beside the state | `ReplayAnchorWallTicks`, `DurationSeconds` |
| what each state means on a node | `LoadingReview`: flush + `Suspend` the recorder, RAM keyframe, replay module over the open recording; `OperatingReview`: the replay's gates + `ReviewGate`; `UnloadingReview`: uninstall the replay module, rewind (`AfterRestore`), `Resume` the recorder | `LoadingReplay` / `OperatingReplay` / `UnloadingReplay` — ⛔ minus their Idle on either side |

### 3b. Classes — existing and new *(brought up to §8 `2026-10-26`)*

```mermaid
classDiagram
  direction LR
  class FrameOuterHeader { <<exists, grown v7>> +WallClockTicks +SimTime NEW +Flags NEW OperatorHalt Changed }
  class RecorderTickSystem { <<exists, grown>> +Suspend() +Resume() NEW - resume clears the destruction log, forces a keyframe }
  class PlaybackController { <<exists, grown>> +GetFrameMetadata(i) with SimTime and Flags · opens FileShare.ReadWrite }
  class ReplayTimeMap { <<NEW, Fdp.Toolkits>> +Segments +PausedStretches +SimAt(wall) +WallAt(sim, edge) +NextUnpause(wall) +Intersect(maps) }
  class ReplayClockProcessManager { <<NEW, master>> axis · rate per segment · non-pausing jump over an operator pause · pauses at the live point }
  class ReplaySessionTopic { <<NEW DDS, TransientLocal>> +Mode +ReviewId +Axis +Map +LivePoint +Checkpoints }
  class SetReplayAxisIntent { <<NEW intent>> any host to the master }
  class ReviewGuard { <<NEW, master>> refuses world-changing ops in a review or replay - ClusterStates.IsReplaying(s) }
  class ReferenceReviewHandler { <<NEW, every recording node>> PrepareReview · FinalizeReview · abandon on Idle }
  class ReferenceCheckpointHandler { <<exists>> the return semantics - ResumeAfterRestore }
  class WorldKeyframes { <<exists>> Capture with CaptureOptions.Recording · Apply · AfterRestore +ForceMarkAllDirty NEW }
  class EcsRecordReplayController { <<exists, grown>> +BeginReview() +EndReview() - a replay module over the OPEN recording }
  class ReviewGate { <<NEW, kernel>> suspends modules outside the toggle groups unless RunsDuringReplay }
  class CycloneNetworkCleanupSystem { <<exists, grown>> mark-and-sweep by network id - dispose the absent, forget the handed-over }
  class EgressHold { <<NEW>> no sample between a node's swap and the master's commit }
  class TimelinePanel { <<exists, grown>> axis · stretches · live point · session layer }
  class TimelineContextMenu { <<NEW>> the right-click seeks - a left click never changes state }
  class LiveTimelineSource { <<NEW, TE-17>> the live world's lanes, frozen at the live point }
  class ModeIndicator { <<NEW, toolbar>> Edit Preview Live Review Replay - a click opens a menu }
  RecorderTickSystem --> FrameOuterHeader : writes
  PlaybackController --> FrameOuterHeader : indexes
  ReplayTimeMap ..> PlaybackController : built from the index
  ReplayClockProcessManager --> ReplayTimeMap
  ReplayClockProcessManager --> ReplaySessionTopic : publishes
  SetReplayAxisIntent ..> ReplayClockProcessManager
  ReviewGuard ..> ReplaySessionTopic : reads the mode
  ReferenceReviewHandler --> WorldKeyframes : full checkpoint
  ReferenceReviewHandler ..> ReferenceCheckpointHandler : shares the return
  ReferenceReviewHandler --> EcsRecordReplayController
  ReferenceReviewHandler --> RecorderTickSystem : Suspend / Resume
  ReferenceReviewHandler --> ReviewGate
  ReferenceReviewHandler --> EgressHold
  CycloneNetworkCleanupSystem ..> EgressHold : disposes pass the hold
  TimelinePanel --> ReplaySessionTopic : the session layer
  TimelinePanel --> LiveTimelineSource
  TimelinePanel --> TimelineContextMenu
  ModeIndicator ..> ReplaySessionTopic : reads the mode
```
*What the picture shows that prose hid:* the review's return is the CHECKPOINT's existing machinery (`ReferenceCheckpointHandler`,
`WorldKeyframes`), not a new restore; the zombie cleanup is a grown EXISTING system (the cleanup's sweep) behind one new seam (the egress
hold); and every host's UI reads one topic.

### 3c. Sequence — enter and leave a review *(redrawn `2026-10-26` to §11f: full checkpoint, barrier pause, the wire — §12)*

```mermaid
sequenceDiagram
  actor Op as Operator (any host)
  participant M as ClusterMaster
  participant C as ReplayClockProcessManager
  participant N as each recording node (ReferenceReviewHandler)
  participant R as its recorder
  Op->>M: review intent {atSimSeconds?} - refused outside OperatingLive
  M->>N: barrier pause (SwitchToDeterministic) on a step with no slot run in flight - never a snap (F3)
  N-->>M: paused (every node)
  M->>N: PrepareReview - remember the live clock L
  N->>N: quiesce - drain ingress, finish in-flight protocols on the paused step (F6)
  N->>R: force the labelled keyframe review-n (D-C), flush, Suspend - the file stays open
  N->>N: capture the FULL checkpoint in RAM - every recorded type + the singletons + the unconsumed events (D-B, F1)
  N->>N: hold egress, freeze ingress, gates on (R7) - install ReplayModule over the open file, seek to T
  N->>N: the cleanup sweep disposes every id owned at L and absent at T, acks awaited (§12)
  N-->>M: ReplayPrepareResult (span, time map, live point)
  M->>N: CommitState(OperatingReview) - egress released, full republish
  M->>C: axis Sim, end = the live point
  Note over Op,R: play / pause / step / seek drive the replay clock - world-changing commands refused (D-E) - every seek retracts then publishes (§12)
  Op->>M: back-to-live intent (the ⏏ button, any host)
  M->>M: refuse new seeks, drain pending ones (F8)
  M->>N: FinalizeReview
  N->>N: hold egress - remove the ReplayModule and await it - apply the checkpoint - AfterRestore
  N->>N: the sweep disposes every id alive in the review and absent at L, acks awaited, readers drained
  N-->>M: done
  M->>N: the clock back to L as a checkpoint restore does it (SnapTo + ResumeAfterRestore, CE-3316) - wall and rate restored (F12, F19)
  M->>N: CommitState(OperatingLive) - live, paused - egress released, full republish, ingress resumes
  N->>R: Resume - destruction log cleared, keyframe forced (F11) - the same file, the same exercise
```
*What the picture shows that prose hid:* the live clock L is remembered by the MASTER, the world by each NODE; the pause is the synchronised
barrier, never a snap, so no slot is reset on the way in; the return is the CHECKPOINT's (slots resume, unconsumed events delivered); and both
swaps are wrapped in a hold-retract-release on the wire (§12). ⛔ SUPERSEDED: the first drawing paused with `SnapAndPause`, captured the dry
run's keyframe and returned with a plain `SnapTo` (F1, F3).

### 3d. Modules — who runs each frame during a review

```mermaid
graph TD
  K[ModuleHostKernel - every frame]
  K --> TG[toggle groups Input / Simulation / PostSimulation / NetworkLifecycle]
  K --> PB[PlaybackTickSystem - EveryFrame]
  K --> EG[egress translators - flood the replayed state]
  K --> CW[capability-walk modules - outside the groups]
  K --> RC[RecorderTickSystem]
  TG -. disabled in replay and review .-> X1[ ]
  CW -. TODAY not gated in a replay .-> X2[ ]
  CW -->|R7: ReviewGate - suspended unless declared| G[runs only if RunsDuringReplay]
  RC -. suspended in a review .-> X3[ ]
  classDef dead fill:#fdd,stroke:#c00;
  class X1,X2,X3 dead
```
*What the picture shows that prose hid:* the red edge — modules registered outside the four toggle groups keep running in a replay TODAY. For
a replay that is harmless only by luck; for a review it would let in-memory live state move. R7 is the fix, and it fixes replay too.

## 4. The time model — the answers to the user's questions

| question | answer |
|---|---|
| **what is a "paused stretch"** | ✅ *(amended, R-3063 — D-H)* a maximal run of consecutive frames recorded while the clock was halted by an OPERATOR (`HaltReason.PausedByOperator` in the frame's flags). ⛔ A stepped run's gaps between steps (`SteppingHeld`) are NOT stretches — they collapse with no mark (F14). ⭐ The clock's own word for it (R-126) — never inferred from wall gaps |
| **what is "unchanged"** | a paused stretch none of whose frames carries a change (the `Changed` flag: the recorder wrote a chunk, a destruction or an event besides `GlobalTime`). ⭐ It only decides the TIMELINE MARK (hollow vs filled) — on the sim axis EVERY stretch collapses, changed or not, and its changes apply at once at the unpause instant (the user's rule) |
| **what is sim time** | the recorded `GlobalTime.TotalTime` of the frame — the run's own clock, scaled (a 2× live run has 2 s of sim per wall second) |
| **how the axis is chosen** | it is a VIEW of one replay clock: `axis = Sim \| Wall`, held by the master per replay / review session and by the Replay Browser per viewer. Default **Sim** for a review and a replay once built (R2); `atSeconds` keeps meaning wall (R-3056); a sim form is `atSimSeconds` |
| **a sim seek** | `wall = map.WallAt(sim, Edge.AfterStretch)` → the ordinary wall seek (R-4012). A sim time that is a paused instant lands at the stretch's END, so its changes are shown |
| **a sim play** | the master's clock runs wall time at `rate × wall/sim` of the current segment (constant within a segment); at a stretch's start it `SnapTo`s the stretch's end — the playback applies the stretch's frames in one seek |
| **a sim step** | `+dt` of SIM → its wall time; never lands inside a stretch |
| **the timeline, Wall axis** | wall seconds after the zero; paused stretches shaded (filled = changed) |
| **the timeline, Sim axis** | sim seconds; each stretch drawn as a tick at its instant (hollow = unchanged, filled = changed); the review's live point a marker at the right end |

## 5. Decisions — ✅ APPROVED `2026-10-26` (R-3057)

🔒 **User, verbatim:** *"Leans approved."* · *"The sim/wall time line mode needs to be changeble at runtime - chznges the mode of thr timrline and seeking and playback (skipping/not skipping pauses)."* (R-3058 — R2 below as amended) · *"clicking timeline itself can not start review (accidental click) - timeline should allow right click menu having seek-related item(s)."* · *"replay in live cluster hosts (and replay browser) needs to share similar ui"* · *"control needs to work from any host (events/network commands to cluster master)"* · *"each host can have different timeline content (whatever the host knows)"* (R-3059 — R10 as amended, §9b–§9e) · R-3060: the §9 quote (*"no new toolbar, we extend the existing one (the time control one)."*)

| # | question | ⏳ lean | rejected — one line |
|---|---|---|---|
| **R1** | where does a frame's sim time live | ⭐ **the frame index (format v7)**: `FrameOuterHeader` + `double SimTime` + `byte Flags` (Halted, Changed) — written from the `GlobalTime` the recorder already reads; `PlaybackController` reads v6 too, building the same index by decoding each body's `GlobalTime` once at open | a `.timeline` sidecar from a decode pass — slow on first open, absent on cluster nodes · deriving pauses from wall gaps — wall runs on while paused (§8.12), so the gaps prove nothing |
| **R2** ✅ *(amended, R-3058)* | the default axis, who chooses, and when | ⭐ **switchable at RUNTIME**, and the switch changes all three at once: what the timeline draws, what a seek means and how playback runs (skip / do not skip paused stretches). In a cluster it is the SESSION's setting, held by the master (it drives the cluster's replay clock — §9b): any operator's switch changes it for all; in the Replay Browser it is the viewer's. ⭐ **default Sim**; a switch keeps the frame on screen (no seek) and only changes what comes next. `atSeconds` stays wall forever (R-3056); `atSimSeconds` is always sim, whatever the axis | change `atSeconds` to sim — silently moves every existing script's seeks · one global setting — two operators want different views |
| **R3** | how one cluster agrees on the map | each node builds `ReplayTimeMap` from its own index and returns it in `ReplayPrepareResult`; the master INTERSECTS the paused stretches (a stretch is the cluster's only where every recording node paused) and keeps the anchor node's sim times | the master reads files — it has none on a pure orchestrator · trust one node — a late joiner's map misses the start |
| **R4** ✅ *(amended, R-3063 — D-H, F14)* | how sim playback is driven | ⭐ **the wall clock stays the only replay clock**; the master's `ReplayClockProcessManager` sets its rate per segment and jumps over a stretch with a NEW non-pausing seek (no epoch bump) — ⛔ not `SnapTo` (= `SnapAndPause`, it would reset the kernel at every jump); ⭐ a stretch is an OPERATOR pause only (`HaltReason.PausedByOperator`), stepping gaps collapse with no mark — nodes are untouched | each node plays by its own sim clock — nodes' sim clocks agree only at step boundaries, a second alignment rule |
| **R5** | the review's states | NEW `LoadingReview`, `OperatingReview`, `UnloadingReview` (play / pause are sub-states, as in live); edges only `OperatingLive → LoadingReview → OperatingReview → UnloadingReview → OperatingLive`; the master refuses a review from any other state | reuse `OperatingReplay` with a flag — its exits (branch, Idle) are wrong for a review and every reader of the state would need the flag · route through Idle — R-4019 deletes the world |
| **R6** ✅ *(amended, R-3063 — D-B, F1, F3)* | how live comes back | ⭐ **the FULL checkpoint in RAM** per node — captured with the `Recording` options (every recorded type AND the singletons; ⛔ not the dry run's `NodeDryRun`, which drops both), on a barrier-paused quiet step; the return applies it with the CHECKPOINT's semantics (`AfterRestore` + `ModuleHostKernel.ResumeAfterRestore`, CE-3316: slots resume, unconsumed step events delivered) + `PreviewStateBracket`; the clock back to L as a checkpoint restore does it; wire: §12 | restore through the recording (B4) — forks a new segment (D8) and needs the open file flushed and indexed · a second world per node (sandbox, Replay-Browser style) — every reader, every host and the IG would have to switch worlds; it doubles memory |
| **R7** | what runs during a review (the user's "must not respond") | ⭐ the replay's gates (toggle groups off, ELM gated, world-state ingress frozen, no replayed events on the bus) **+ NEW `ReviewGate`**: during a replay AND a review the kernel suspends every module and system registered outside the toggle groups unless it declares `[RunsDuringReplay]` (playback, egress, presentation, recorder). Class S and X state (sessions, SUMO) is therefore frozen and deaf; A / B are re-derived by `AfterRestore` on exit; C is empty (in the ECS, restored by the keyframe). A rail: every module outside the groups declares, and the SUMO step does not run in a review | gate each module by hand — the gap §3d shows is exactly a module someone forgot · let them run and filter their inputs — every module would need its own filter |
| **R8** | the live recording during a review | ⭐ **suspended, not finalised**: no frame is written while the world is the replayed one; exit resumes the SAME file and exercise (one straight history, a wall gap the Sim axis never shows); the review's wall interval goes into `.meta.json` (`Reviews[]`) so the Wall axis can label it. ⚠ relaxes replay-and-modules §5.7 (recording and replay exclusive) for a review only: both modules installed, only the replay one running | finalise and branch on exit (D18) — a new exercise per review, history copied each time · keep recording during the review — writes replayed frames into the live history |
| **R9** | the live point and the end of the review | the live point = the last frame written before `Suspend` (wall + sim); the review's replay ends there; reaching it on play pauses (the existing end-of-replay pause, folded into `ReplayClockProcessManager`); a seek past it clamps to it | let the review play past the end — there is nothing recorded |
| **R10** ✅ *(amended, R-3059)* | the timeline UI — every host | ONE timeline (R-4068): axis toggle; stretches shaded (Wall) or ticks (Sim); checkpoint marks (they carry sim time already — `CheckpointMark`); in a review the live-point marker and a "back to live" button. ⭐ **A left click NEVER changes the state** (an accidental click must not start a review or move a replay): it selects an event. Seeking is in the **right-click menu** (§9b) — on a live host its items ENTER a review there (amends TE-19); in a review / replay the playhead handle drags. The Replay Browser and `ClusterScenarioPanel`'s slider use the same index and axis | a separate review timeline — two timelines for one recording |
| **R11** | HTTP | `POST /review {atSeconds \| atSimSeconds}`, `POST /review/exit`; `/replay/seek` and `/review/seek` take `atSimSeconds`; `POST /replay/axis {Sim \| Wall}`; `/replay/status` adds `simSeconds`, `axis`, `review: {livePoint: {atSeconds, simSeconds}}`; `/sim/play\|pause\|step\|timescale` drive the review clock as they drive the replay clock | a review as `POST /replay {review: true}` — one route with two exits |
| **R12** | when | ⭐ **P0 now** (R1: the header + the status's `simSeconds`), everything else when the user schedules it — P0 is the only part whose absence makes today's recordings worse later | build all now — the user asked for the design first |

## 6. HTTP and UI — the shape, for the API to stay compatible now

| surface | now (R-3056) | with this design |
|---|---|---|
| `/replay/status` | `sim.totalTime` = seconds after the zero (wall) | + `simSeconds` (the frame's recorded sim time) — ⭐ P0 · + `axis` · + `review` |
| `/replay/seek` | `atSeconds` (wall), `frame`, `frames`, `cause`, `next` | + `atSimSeconds`; `frames: ±n` on the Sim axis steps over stretches |
| `/sim/step` in a replay | `+dt` of the replay clock (wall) | `+dt` of the session's axis |
| new | — | `POST /review`, `POST /review/exit`, `POST /replay/axis` |
| timelines | frame slider (Replay Browser), wall ruler (Event Timeline), wall-seconds slider (cluster panel) | the one panel with the axis toggle, stretches, marks, the live point |

## 7. Build order — measure first *(re-cut `2026-10-26` to §8's phases, §11f and §12)*

| step | what (§8 rows) | accepted when |
|---|---|---|
| ~~M1~~ | ✅ answered, not a risk: Windows reads a file another process writes as long as both opens agree — the recorder already allows readers (`FileShare.Read`, `AsyncRecorder.cs:132`) and `RecordingSlice` already reads the live file that way (`FileShare.ReadWrite`, `:28`). Only `PlaybackController.cs:72` passes `FileShare.Read` — which on Windows means "no one else may be writing" and is refused while the recorder holds it. ⇒ one flag (row 43) + a rail that runs on Windows | — |
| ~~M4 · M5~~ | ✅ measured (§12c W5, W3): state is left in the reader during a replay ⇒ the exit discards it; acks exist in DDS and in `libddsc`, not in the .NET binding ⇒ a `WaitForAcks` to add | — |
| **PW** | ⭐ the wire, rows 35–39 — ⭐ **first, because it fixes a hole that exists TODAY** (CE-3331: ordinary replay seeks and checkpoint restores leave zombies) | §12e's rails, the CE-3331 rail red before and green after |
| **P0** | rows 1–6: the v7 header (`SimTime`, `Flags` with the `HaltReason`), v6 read by decoding; `/replay/status` `simSeconds` | a stepped live run's index shows the operator pauses only (D-H) without decoding (the CE-4053 recording); M2 |
| **P1** | rows 7–13, 31, 32, 34, 45: `ReplayTimeMap`, the master's map, `atSimSeconds`, the axis, `ReplayClockProcessManager` with the non-pausing jump (R4) | a sim seek lands every node on the same sim frame; a sim play crosses an operator pause without a kernel reset |
| **P2** | row 14: `ReviewGate` — fixes the replay gap too | every module outside the groups declares; nothing undeclared ticks in a replay |
| **P3** | rows 15–20, 40–44: the review states, `ReferenceReviewHandler`, the full checkpoint, the guard, recorder `Suspend` / `Resume`, `/review` routes; M3 | live → review → live identical (world + singletons + the next N steps = the straight run — R1's oracle); a failure during a review leaves a finalised recording and a restorable `review-<n>` |
| **P4** | rows 21–30, 33: the UI on every host | a right-click seek on a live host's timeline enters a review; a left click never changes state |

## 8. The system changes — every component, by phase *(✅ approved design; nothing built)*

| # | component (file) | change | phase |
|---|---|---|---|
| 1 | `FrameOuterHeader` (`Fdp.Core/FlightRecorder`) | v7: `+ double SimTime`, `+ byte Flags` (bit 0 `Halted`, bit 1 `Changed`); `FdpConfig.FORMAT_VERSION = 7` | P0 |
| 2 | `AsyncRecorder.CaptureFrame` / `CaptureKeyframe` | stamp `SimTime` and `Flags` from the `GlobalTime` it is handed; report whether the body held anything besides `GlobalTime` (chunks, destructions, events) → `Changed` | P0 |
| 3 | `PlaybackController` (+ `FrameMetadata`) | index `SimTime` + `Flags`; ⭐ read v6 too — build the same two fields by decoding each frame's `GlobalTime` once at open | P0 |
| 4 | `RecordingSlice`, `PlaybackSystem` (header readers) | the header 25 → 34 bytes (F17); v6 / v7 by the file's version | P0 |
| 5 | the D18 branch copy (`RecordingModule` history copy) | a branch of a v6 parent rewrites the copied headers to v7 — ⛔ never one file with two header sizes | P0 |
| 6 | `/replay/status` | `+ simSeconds` (the recorded sim time of the frame on screen) | P0 |
| 7 | `ReplayTimeMap` (NEW, `Fdp.Toolkits/Replay`) | segments (wall span, sim span, rate), paused stretches (wall span, changed?), `SimAt` / `WallAt(edge)` / `NextUnpause` / `Intersect` / `FromRecording(PlaybackController)` | P1 |
| 8 | `ReplayPrepareResult` + `ReplayConsensusAggregator` | carry each node's map; the master intersects the stretches (R3) | P1 |
| 9 | `ReplayClockProcessManager` (NEW, master; absorbs `ReplayProcessManager`) | holds `ReplayAxis` (R-3058) and the map; Sim axis: the clock's rate per segment, `SnapTo` over a stretch; pauses at the end / the live point; a switch keeps the frame | P1 |
| 10 | intents: `SeekReplayIntent` `+ SimSeconds`; NEW `SetReplayAxisIntent`; the cluster status `+ Axis`, `+ SimSeconds`, `+ LivePoint` (and their DDS mirrors in `OrchestrationMessages`) | the master turns a sim target into wall ticks — nodes unchanged | P1 |
| 11 | `TimelineIndex` + sidecar (schema 3) | `+ FrameSimTime[]`, `+ FrameFlags[]` — read from the frame index, not decoded | P1 |
| 12 | HTTP (`DebugApiService` + routes + `DebugApiRouteDocs` + `CapabilityManifest`, MCP catalog regenerated — R-1030) | `atSimSeconds` on `/replay/seek` and `/review/seek`; `POST /replay/axis`; status `axis` | P1 |
| 13 | Replay Browser: `FederatedReplayManager`, `IReplayBrowserControl` | the same map per node group; its axis is the viewer's; sim seeks / play through the map | P1 |
| 14 | `ReviewGate` (kernel) + `[RunsDuringReplay]` | in a replay AND a review, suspend every module / system outside the four toggle groups unless declared; rail: every one outside the groups declares | P2 |
| 15 | `ClusterState` + `NodeOpType` (both copies), `HrotStateGraph`, the master's prepare-op mapping, the review refusals | §3a | P3 |
| 16 | `ReferenceReviewHandler` (NEW, every ECS node; shares the capture / rewind of `ReferencePreviewHandler` — extracted, not copied) | `PrepareReview` / `FinalizeReview` (§3c): quiesce, forced `review-<n>`, FULL checkpoint (`Recording` options), gates before the module (F9); on Idle: abandon and finalise (D-C, F4) | P3 |
| 17 | `RecorderTickSystem` / `RecordingModule` | `Suspend()` / `Resume()` — flush, keep the file open, write nothing in between; `.meta.json` `Reviews[]` | P3 |
| 18 | `EcsRecordReplayController` + `PlaybackController` | `BeginReview` / `EndReview`: a `ReplayModule` over the OPEN recording (opened `FileShare.ReadWrite`, index frozen at the live point) — M1 first | P3 |
| 19 | the master's return | ✅ *(amended)* the clock back to L the way the checkpoint restore does it (`SnapTo` + `ResumeAfterRestore`, CE-3316), the wall clock and the rate restored (F12, F19); refuses new seeks and drains pending ones first (F8); live paused | P3 |
| 20 | `/review`, `/review/exit`, status `review: {livePoint}` | R11 | P3 |
| 21 | `ITimeTransportFacade` (+ `ClusterTimeTransportAdapter`, `EditorTimeTransportFacade`) | `+ Mode` (Live / Review / Replay), `+ Axis`, `SetAxis`, `EnterReview(t?)`, `ExitReview()`, `LivePoint`, `Seek(t)` (the timeline's menu); `Stop` in a review = back to live | P4 |
| 22 | `MainToolbarTimeControlSection` (the EXISTING time-control section) | ⭐ two glyphs only: the mode INDICATOR (✎ / ◐ / ● / ⟲ / ▶ — a click opens its menu, never an action, R-3061) before the three existing buttons and the axis icon (S / W) after the rate; ■ shows ⏏ "Back to live" in a review; the readout on the active axis (§9a) | P4 |
| 23 | `TimelinePanel` + `ITimelineCursor` (the ONE timeline, R-4068) | positions in the active axis; stretches shaded (Wall) or ticks (Sim); the live point; a left click selects, never seeks — a live host enters a review from the right-click menu (R-3059, amends TE-19) | P4 |
| 24 | `ClusterScenarioPanel` + `ClusterUiCache` | the seek slider on the active axis; the cache reads `ReplaySessionTopic` (axis, map, live point, checkpoints) | P4 |
| 25 | ⛔ withdrawn (R-3060) — no status-bar chip: the toolbar's mode icon says it | — | — |
| 26 | `DESIGN_Director_Timeline_Editor.md` (ui's) | TL-4 gains the sim axis; TE-19's live row becomes "the right-click menu enters a review" (R-3059) — amended by its owner when P4 is scheduled | P4 |
| 27 | `TimelineContextMenu` (NEW, beside `TimelinePanel`) + the panel's zoom / pan / time stamps on the active axis | the right-click items of §9b per mode; a left click selects, never seeks (R-3059) | P4 |
| 28 | `LiveTimelineSource` (NEW — TE-17's, not built) | the catalogue's indexed lanes evaluated on the live world each frame, appended; frozen at the live point for a review | P4 |
| 29 | the session layer (NEW, in `TimelinePanel`) | ruler, stretches, checkpoint marks, live point, playhead from the cluster status + `ReplayTimeMap` — identical on every host, the only layer on ExCon / Orchestrator | P4 |
| 30 | `TimelineLaneContext` | `+ Live` beside `Recording`, so a provider says where its lanes apply | P4 |
| 31 | `ClusterOpEgressTranslator` / `ClusterOpMasterTranslator` + `ClusterOpType` (both copies) | carry `SetReplayAxisIntent` and `SeekReplayIntent.SimSeconds` from any host (§9d) | P1 |
| 32 | `ReplaySessionTopic` (NEW DDS topic, TransientLocal) — the master publishes, every host's `ClusterUiCache` reads | the session layer (§9f) | P1 |
| 33 | `LiveTimelineSource` → `.timeline` sidecar at `FinalizeLive` | a just-finished exercise replays fully indexed (§9g) | P4 |
| 34 | `ReplayTimelinePanel` (the Replay Browser's own controls) | `+` the S / W axis icon (the viewer's) — nothing else (R-3060) | P1 |
| 35 | `CycloneNetworkCleanupSystem` | ⭐ §12 W1: mark-and-sweep by network id once per in-place world replacement (`ClearGeneration`), a delta query otherwise — an id it published and absent from the world ⇒ `DisposeAll`; present as a ghost ⇒ forget; subsumes the lightweight sweep (1b). ⭐ Fixes CE-3331 for ordinary replay seeks, checkpoint restores and the dry-run exit | **PW** |
| 36 | egress hold (`CycloneEgressSystem` + `SmartEgressSystem` + the cleanup's writers) | §12 W2: no sample between a node's world swap and the master's commit (disposes excepted); released on `CommitState` / the post-seek snap | PW |
| 37 | the disposing writers + CycloneDds.NET `DdsWriter` | §12 W3: `WaitForAcks(TimeSpan)` over `dds_wait_for_acks`, added to the binding (CycloneDds.NET); the node's prepare / seek result waits until its disposes are acknowledged (bounded) | PW |
| 38 | `AfterRestore.Run` | §12 W4: `SmartEgressUtil.ForceMarkAllDirty` — the survivors are republished after a checkpoint restore and the review's exit | PW |
| 39 | `CycloneNetworkIngressSystem` + the state translators | §12 W5: a STATE discard beside the events' `IDiscardableIngress`, run at the exit's release before ingress resumes | PW |
| 40 | `WorldKeyframes` capture for a review | `CaptureOptions.Recording` (every recorded type + singletons) — F1 | P3 |
| 41 | the master's review guard + `ClusterStates.IsReplaying(s)` | world-changing ops refused in `OperatingReview` / `OperatingReplay`, 409 on the debug API, the map hides its order actions, `/scenario/save` refused (D-E, F7); one predicate for the 25 state checks and 42 `IsReplayActive` readers (F20) | P3 |
| 42 | the master's review entry / exit | the barrier pause first, `PrepareReview` only after every node acked it (F5); every ECS node must participate or the review is refused (F10); the live point = the minimum of the nodes' last frame stamps, the end an exact clamp (F13); ■ asks "Stop the exercise?" for 3 s after a return (F15) | P3 |
| 43 | `PlaybackController` | opens with `FileShare.ReadWrite` (as `RecordingSlice:28`); the review indexes up to the last frame `Suspend` flushed (F16); a rail on Windows | P3 |
| 44 | the recorder on resume + the episode recorders | destruction log cleared, keyframe forced; episode recorders suspended with it (F11, W6) | P3 |
| 45 | `ReplaySessionTopic` | `+ ReviewId` (F18) | P1 |

## 9. The review UI on a live cluster host *(R-3059 as amended by R-3060)*

🔒 **User, `2026-10-26`:** *"no new toolbar, we extend the existing one (the time control one). there is too many buttons suggested, timeline
panel with well working zoom and pan with time stamps and right click menu handles the seeks, and the 3 already existing time control
buttons (play/pause, step, stop) should suffice then. Sim/wall should be a toggle icon, nothing big. toolbar has limited space. Same the
review/replay indicator - different colored switchable icon. Replay browser should keep its own specific time controls (no durian action
review there applicable). May STOP icon could change shape/color/tooltip to "back to live" while in DAR, so only a next press will stop
the live session. "live host never seeks" is supreseded by DAR."*

### 9a. The toolbar — the EXISTING time-control section, grown by two small icons *(R-3060, R-3061)*

🔒 **User, `2026-10-26`:** *"starting review at live point means the review is just about to end as it reached the live point. I think it
should be icon that open some kind of menu on click (offering to change mode maybe? only if there is anything meaningful to offer) but should
not do any immediate action, it is mainly an indicator or the live/replayy/review mopde, not an action button. If there is a preview mode, it
needs to have its own indicator icon as well."*

Today (`MainToolbarTimeControlSection`, registered by the editor and CGF — `ShellTimeControlToolbar`):

```
 … │  ⏸   ⏭   ■    12:31.4   1×  │ …
       play/pause · step · stop · time · rate
```

Grown — the same three buttons, the same readout and rate, ⭐ plus the **mode indicator** in front and the **axis icon** behind:

```
 EDIT      … │ (✎)  ▶   ⏭   ■    00:00.0   1×  [S] │ …     grey ✎ — indicator only, no menu
 PREVIEW   … │ (◐)  ⏸   ⏭   ■    00:14.2   1×  [S] │ …     violet ◐ — the dry run; ■ returns to edit; no menu
 LIVE      … │ (●)  ⏸   ⏭   ■    12:31.4   1×  [S] │ …     green ●  — click opens the LIVE menu
 REVIEW    … │ (⟲)  ▶   ⏭  (⏏)   07:04.0   2×  [S] │ …     amber ⟲  — click opens the REVIEW menu; ■ became green ⏏ Back to live
 REPLAY    … │ (▶)  ⏸   ⏭   ■    03:12.7   1×  [W] │ …     blue ▶   — click opens the REPLAY menu
```

⭐ **The mode indicator never acts on a click — it opens a menu, and only where the menu has something worth offering:**

```
 LIVE ● ─────────────────────────────┐   REVIEW ⟲ ───────────────────────────┐   REPLAY ▶ ────────────────────────────┐
 │ Live · ua-tank-ammo · 12:31.4      │   │ Review · 5:27 behind live          │   │ Replay · exercise 3f2a… · 18:40     │
 │ ─────────────────────────────────  │   │ ─────────────────────────────────  │   │ ─────────────────────────────────── │
 │ ⟲ Review the last 30 s             │   │ ⟲ Restart from 30 s before live    │   │ ⑂ Go live from here (a new branch)… │
 │ ⟲ Review the last 2 min            │   │ ⟲ Restart from 2 min before live   │   └─────────────────────────────────────┘
 │ ⟲ Review the last 10 min           │   │ ⟲ Restart from a checkpoint     ▸  │
 │ ⟲ Review from a checkpoint      ▸  │   │ ⏏ Back to live (12:31.4)           │
 │ ⟲ Review from the start            │   └────────────────────────────────────┘
 │ ─────────────────────────────────  │
 │ ⚑ Mark this moment                 │    ⚑ = take a checkpoint with an automatic label (R-3044 — a named
 └────────────────────────────────────┘      keyframe; no second "bookmark" mechanism), shown on the timeline
```

| mode | indicator | menu |
|---|---|---|
| Edit | grey ✎ | none — nothing runs |
| Preview (the dry run) | violet ◐ | none — ■ returns to edit (its one meaningful action is already a button) |
| Live | green ● | review the last 30 s / 2 min / 10 min, from a checkpoint ▸, from the start · ⚑ mark this moment (a checkpoint) |
| Review | amber ⟲ | restart from 30 s / 2 min before live, from a checkpoint ▸ · ⏏ back to live |
| Replay | blue ▶ | ⑂ go live from here — the existing branch (D18 / R-3047), behind a confirmation |
| none (Idle) | nothing | — |

| element | what it shows | click | tooltip |
|---|---|---|---|
| **mode indicator** (NEW, one glyph) | ✎ / ◐ / ● / ⟲ / ▶ by colour | opens its menu (above) — ⛔ never an immediate action | the mode; in a review the live point and the distance to it |
| ⏯ play / pause (exists) | | the cluster clock in live, the replay clock in a review / replay (skips paused stretches on S) | |
| ⏭ step (exists) | | `+dt` on the active axis — never lands inside a paused stretch on S | |
| ■ stop (exists) | ■ — in a review it CHANGES to ⏏ green **Back to live** | live: stop the run · review: back to live (so only a SECOND press, now in live, stops) · replay: leave it · preview: back to edit | "Stop the exercise" / "Back to live — 12:31.4, 5:27 ahead" / "Leave the replay" / "Back to edit" |
| time readout (exists) | the playhead on the active axis | | sim / wall; in a review "−5:27 to live" |
| rate (exists) | | | |
| **axis icon** (NEW, one glyph) | **S** = sim (pauses collapsed) · **W** = wall | toggles S ⇄ W for the SESSION (R-3058) — ruler, seek and playback at once | "Sim time — pauses skipped" / "Wall time — pauses shown" |

⛔ Withdrawn: the separate state chip, jump-to-start, seek-history back / forward, step back, "Review…" / "Back to live" buttons (first draft);
⛔ "click ● = review at the live point" (R-3060's first form — a review starting at its own end; R-3061).

### 9b. The timeline panel — zoom, pan, time stamps; the seeks are in its right-click menu

```
┌ Timeline ────────────────────────────────────────────────────── [S] ┐
│  05:30     05:40     05:50     06:00     06:10     06:20     06:30  │  ← time stamps on the active axis, re-labelled as you zoom
│ ──┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼──── │
│ Life      ·         ·                    ·                          │
│ Combat         x    x  x          ◯              ●      x           │  ◯ / ● a collapsed pause (S): unchanged / with edits
│ Tasks     ──────────────▬▬▬▬▬▬▬▬──────────                          │
│ Checkp.               ⚑ before-assault                              │
│                           ▼ playhead                          ┃ live│  ┃ the live point (review) — the right edge in live
└─────────────────────────────────────────────────────────────────────┘
   wheel = zoom at the cursor · drag on the ruler = pan · drag ▼ = scrub (review / replay only)
   left click = select the event (details, the unit) — ⛔ never seeks, never starts a review
```

Right-click menu, live (anything in the past):

```
┌────────────────────────────────────┐
│ ⟲ Review from 05:52                │
│ ⟲ Review from 05:52 and play       │
│ ⟲ Review from this event (hit)     │   ← only on an event
└────────────────────────────────────┘
```

Right-click menu, review / replay:

```
┌────────────────────────────────────┐
│ Seek here (06:02)                  │
│ Play from here                     │
│ Seek to this event (hit 06:02.4)   │   ← only on an event
│ ◀ Previous event in this lane      │
│ ▶ Next event in this lane          │
│ ── on a collapsed pause ─────────  │
│ Seek to before the pause's edits   │
│ Seek to after them (the unpause)   │
│ ───────────────────────────────    │
│ ⏏ Seek to the live point           │   ← review only
│ Copy the time                      │
└────────────────────────────────────┘
```

| axis | ruler | paused stretch | playback |
|---|---|---|---|
| **S** (sim) | sim seconds | one tick: ◯ unchanged, ● with edits (they appear at once, at the unpause) | skips it |
| **W** (wall) | wall seconds after the zero | a shaded span (darker = with edits) | plays through it (nothing moves) |

### 9c. The Replay Browser keeps its own controls

⭐ The Replay Browser has no live run, so no review: its own time controls (`ReplayTimelinePanel` — slider, step, play, rate, export) stay,
and gain only the **[S] / [W] axis icon** (the viewer's, no cluster — R-3058). Its timeline panel (`TimelinePanel`, already built there) is
the same panel as on the live hosts — the same zoom, pan, stamps and the review / replay right-click menu.

```
 Replay Browser — its own row (exists), + the axis icon:
 │ ⏮  ◀  ⏯  ▶  ⏭   frame 2 431 / 9 870   00:41.2 / 09:58.3   4×   [S] │  export …
```

⭐ **TE-19's "a live host never seeks" is SUPERSEDED** by the review (user, above): a live host seeks by entering a review.

### 9d. Every control from ANY host — an intent, the master decides

⭐ A control never acts on its own node: it publishes an INTENT on the node's bus; `ClusterOpEgressTranslator` (exists: `PauseTimeIntent`
`:55`, `TransitionStateIntent` `:91`, `SeekReplayIntent` `:162`) carries it as a `ClusterOpRequest` over DDS to the master; the master
validates, plans, drives; every host re-draws from what the master publishes. ⇒ CGF, an IG, SimHost, ExCon and the editor control a review
identically, and two operators always see one session.

```mermaid
sequenceDiagram
  actor A as Operator A (CGF)
  actor B as Operator B (ExCon)
  participant E as A's node: ClusterOpEgressTranslator
  participant M as ClusterMaster
  A->>E: click the axis icon S -> W (SetReplayAxisIntent)
  E->>M: ClusterOpRequest(SetReplayAxis, Wall) over DDS
  M->>M: the replay clock stops skipping paused stretches - the frame stays
  M-->>A: ReplaySessionTopic (axis W)
  M-->>B: ReplaySessionTopic (axis W)
  Note over A,B: both timelines and readouts re-draw on wall time - a seek now means wall
```
*What the picture shows that prose hid:* the axis changes how the MASTER drives the replay clock, so it is one setting the master owns and
publishes — two operators never see two playbacks of one replay.

### 9e. Who fills the timeline on each host — ⭐ each shows what IT knows

⭐ **Two layers.** The **session layer** is the cluster's and the SAME on every host: the ruler on the active axis, the paused stretches,
the checkpoint marks, the live point and the review's span, the playhead — all from the master's status and its `ReplayTimeMap`, never from a
local file. The **event lanes** are the host's own: its catalogue holds the lane providers of the modules IT hosts (R-4078 — *"the module that
owns the data registers its provider"*; R-4079 — lanes are definitions, never code), evaluated on the world IT holds.

```mermaid
graph TD
  M[ClusterMaster: ReplayTimeMap, checkpoints, live point, axis] -->|cluster status| SL[session layer: ruler, stretches, marks, live point, playhead]
  subgraph HOST[one host]
    CAT[TimelineLaneCatalog: the providers of the modules this host runs]
    LS[LiveTimelineSource: the catalogue's indexed lanes on the live world, each frame, appended]
    RS[RecordingTimelineSource: the same lanes over this host's own recording]
    MK[Marks providers: trace rings, director tracks]
    CAT --> LS
    CAT --> RS
    CAT --> MK
  end
  SL --> TP[TimelinePanel]
  LS -->|live and review| TP
  RS -->|replay, a cold start, the Replay Browser| TP
  MK --> TP
```
*What the picture shows that prose hid:* nothing crosses hosts except the session layer — so every host agrees on WHERE time is and how it is
drawn, and each shows only the events it can actually see; a host with no world (ExCon, Orchestrator) still has a correct, steerable timeline.

| mode | source of the event lanes | why |
|---|---|---|
| live | `LiveTimelineSource` (TE-17, NEW): the catalogue's indexed lanes evaluated on the live world each frame, appended | no file read, nothing to rebuild on entering a review |
| review | the SAME live source, frozen at the live point — its history IS the recording's up to there | the review shows what live showed |
| cluster replay | `RecordingTimelineSource` over the node's OWN recording (built; sidecar cached) | each node holds its own recording (R-4012) |
| Replay Browser | `RecordingTimelineSource` over the shown node's recording (built) | offline |
| a cold start (the host restarted mid-exercise) | `RecordingTimelineSource` for the frames before it started, then live | its recording holds them |

| host | what it knows ⇒ what its lanes show *(examples)* |
|---|---|
| editor (a one-node cluster) | everything — it owns the whole world |
| CGF | the brain lanes (journal, orders, reports, decisions — R-4076) of the units it owns; life / combat for every unit it holds |
| SimHost | the muscle / physics lanes of what it simulates; life / combat |
| IG | what it renders — life, effects, the camera / view lanes |
| ExCon, Orchestrator | ⭐ the session layer only (no world, no recording); ⏭ later, optionally, one node's lanes read over HTTP (`GET /timeline/events`, RB-6) |

⭐ A lane whose data a host does not hold is not offered there (its provider is not registered, or `Applies` is false) — ⛔ never an empty lane
that reads as "nothing happened". `TimelineLaneContext` gains `Live` beside `Recording`, so a provider can say where it applies.

### 9f. The messages — what is new on the wire

| message | kind / QoS | from → to | new or changed | carries |
|---|---|---|---|---|
| `TransitionStateIntent` → `ClusterOpRequest(TransitionState)` | command (exists) | any host → master | ⭐ no new type: targets `OperatingReview` (enter) / `OperatingLive` (back) | `atSeconds \| atSimSeconds` in the payload |
| `SeekReplayIntent` → `ClusterOpRequest(ReplaySeek)` | command (exists) | any host → master | changed: `+ SimSeconds` | a sim target; the master turns it into wall ticks |
| `SetReplayAxisIntent` → `ClusterOpRequest(SetReplayAxis)` | command | any host → master | ⭐ NEW intent + `ClusterOpType` value (both copies) | `Sim \| Wall` |
| `NodeOpCommand(PrepareReview / FinalizeReview)` | command (exists) | master → every node | NEW `NodeOpType` values (both copies) | the review's request id |
| `NodeOpStatus.ResultJson` for `PrepareReplay` / `PrepareReview` | reply (exists) | node → master | changed payload: `ReplayPrepareResult + TimeMap` (the node's segments and paused stretches, from its frame index) + the live point (review) | JSON |
| `ClusterStateTopic` | state, TransientLocal (exists) | master → all | changed: the three review values of `ClusterState` | |
| **`ReplaySessionTopic`** | ⭐ NEW state, Reliable + TransientLocal, KeepLast 1 (as `ClusterStateTopic`) | master → all | NEW | the SESSION LAYER: session / exercise id, the zero, the length, the axis, the cluster's `ReplayTimeMap` (segments, paused stretches with their changed flag), checkpoint marks (label, wall, sim), the live point and the review's span — JSON. Republished when the axis or the map changes |
| the clock | (exists — the cluster time pulse) | master → all | ⛔ unchanged | wall seconds after the zero; a host turns them into sim seconds with the session's map — ⛔ no per-frame sim message |

### 9g. When the timeline has content — what is inside "loading", what fills in after

```mermaid
sequenceDiagram
  participant M as ClusterMaster
  participant N as each ECS node
  participant T as that node's timeline
  participant X as a host with no world (ExCon)
  M->>N: PrepareReplay (LoadingReplay)
  N->>N: open the recording - the frame index (v7: sim time, pause flags, no decode)
  N->>N: open the timeline source - load the .timeline sidecar if present
  N-->>M: ReplayPrepareResult + TimeMap
  M->>M: intersect the maps - the session layer
  M-->>N: ReplaySessionTopic
  M-->>X: ReplaySessionTopic
  M->>N: CommitState(OperatingReplay), the entry seek
  T->>T: ruler, pauses, checkpoints, playhead at once - lanes from the sidecar at once
  N->>N: background scan only for lanes the sidecar lacks - they fill in, with progress
  X->>X: the session layer only - correct and steerable
```
*What the picture shows that prose hid:* "loading" holds only what is cheap — opening the file and its frame index, and the sidecar; the slow
part (scanning lanes) is never inside a cluster transition, and a host waits for nothing it cannot draw.

| question | answer |
|---|---|
| **how a host waits for the timeline's content from the master** | ⭐ it does not poll: `ReplaySessionTopic` is TransientLocal, so every host — a late joiner included — receives the newest sample; until a sample with the CURRENT session / exercise id (from `ClusterStateTopic`) arrives, the panel shows its frame with "waiting for the session…". ⚠ DDS orders no two topics, so the host matches by id, never by arrival order |
| **is the recording scan part of `LoadingReplay`** | ⭐ the CHEAP part is: each node opens its recording and its frame index (v7 — sim time and pause flags without decoding) and loads the `.timeline` sidecar — that is what `PrepareReplay` must do anyway to return its span and time map. ⛔ The lane SCAN is not: it can take minutes on a long recording and would hold the whole cluster's transition; it runs in the background afterwards and the lanes fill in with the progress the panel already shows (`RecordingTimelineSource.Progress`) |
| **so what shows right after the recording opens** | the session layer (ruler on the axis, pauses, checkpoints, playhead) at once on every host; the lanes at once wherever the sidecar exists, else filling in |
| **why the sidecar usually exists** | ⭐ NEW: at `FinalizeLive` each node writes the `.timeline` sidecar from its `LiveTimelineSource` (the lanes it already evaluated live) — so replaying a just-finished exercise opens fully indexed; only lanes added since are scanned |
| **a review** | ⛔ no scan at all: the live source's history up to the live point IS the timeline; the session sample adds the live point and the map |
| **the Replay Browser** | as today (its own process): open → sidecar → background scan; its session layer is local (its own map from its files) |

## 10. The timeline panel's zoom and pan — a critical review of what is built, and the interaction it needs

🔒 **User, `2026-10-26`:** *"Not sure how well the pan/zoom of the timeline is solved … these are crucial features so pls take a critical
look and use your experience from other sw to identify gaps and flaws there."*

📐 **Read: `FDP/Engine/Fdp.Presentation/ImGui/Panels/Timeline/TimelinePanel.cs` (236 lines) and `TimelineLayout.cs` (265 lines), ui's
CE-1302 R1-b.** ⭐ The foundations are good and stay: density bins when crowded / glyphs when sparse, clustering with counts, priority on
top, cost = pixels × lanes × log(events) (two binary searches a bin), zoom maths anchored at the cursor (`ZoomAbout`), an overview strip
with the window as a bracket. ⛔ What follows is what breaks the review use, or what every mature timeline has and this one lacks.

### 10a. Defects in the built code *(each measured — file:line)*

| # | defect | where | why it matters |
|---|---|---|---|
| T1 | 🔴 **the view resets to the WHOLE extent whenever the extent changes** — and a live source's extent grows every frame | `TimelinePanel.cs:70-74` | in live (and a review's source while live) no zoom or pan survives one frame — the panel is unusable for its main DAR job |
| T2 | 🔴 **a left click SEEKS** (release without drag) | `TimelinePanel.cs:196-199` | contradicts R-3059 (an accidental click must not move a replay or start a review) |
| T3 | left drag PANS on the lanes | `:191-195` | the same gesture is needed for the playhead scrub and a range selection; pan belongs elsewhere (10c) |
| T4 | zoom only with **Ctrl+wheel**, only while over a **lane row** — not over the ruler, the overview or empty space; the plain wheel scrolls the ImGui window | `:189-190` | the first thing every user tries (the wheel over the ruler) does the wrong thing |
| T5 | the wheel's MAGNITUDE is ignored (`MouseWheel > 0 ? 0.8 : 1.25`) | `:190` | a trackpad sends many small deltas per gesture ⇒ each one is a full 25 % step: a flick zooms 10× |
| T6 | no horizontal wheel (`io.MouseWheelH`) | — | trackpad two-finger sideways / Shift+wheel do nothing |
| T7 | the closest zoom is **0.1 s** (`minSpan = TicksPerSecond / 10`) | `:190` | a frame is 16.7 ms: individual frames, the shot-then-hit order, a step's effect cannot be inspected |
| T8 | ruler labels: steps under 1 s use `m:ss.f` — past one hour the HOURS vanish (`TimeSpan` `m` is 0-59); labels are not measured, so long ones overlap; no minor ticks; the step list ends at 2 h | `:164-166`, `TimelineLayout.cs:253-264` | a long exercise mislabels or crowds its own ruler |
| T9 | the overview only RE-CENTRES the bracket on the mouse; its edges cannot be dragged to zoom | `:144-149` | the cheapest way to zoom on a long recording is missing |
| T10 | `_dragging` is cleared only on a release over a lane row | `:196-200` | a drag released over the ruler leaves it set: the next click is ignored |
| T11 | the ruler, the overview and the lanes scroll together vertically | `:86-102` | with many lanes the ruler leaves the screen while you pan |
| T12 | the playhead can leave the window; nothing follows it during play, nothing points to it when off-screen | `:111-112` | press play, the cursor walks out of view |
| T13 | time is wall ticks only (TL-4) | `TimelineWindow` | the Sim axis (R-3058) needs the window, the ruler and `XOf` in the active axis |

### 10b. Gaps against mature timelines *(experience: Perfetto / Chrome tracing, DaVinci / Premiere, Audacity, Grafana live, Tacview-style debriefing)*

| gap | what users expect | for DAR |
|---|---|---|
| **follow modes** | live: the window stays pinned to the live edge ("tail") until the user pans away, then a `⇥ Live` chip re-pins it; playback: the window pages (or scrolls) to keep the playhead in view | ⭐ essential — live and review are this panel's main use |
| **fit / presets** | double-click the ruler = fit all; `F` fit, `0`…`3` = last 30 s / 2 min / 10 min / all; "zoom to selection" | the operator thinks "the last two minutes" |
| **range selection** | Shift+drag on the ruler selects a span showing its duration: zoom to it · loop it in a review · export it · review from its start | ⭐ loop a range is the classic debrief move |
| **hover readout** | a thin cursor line through the lanes with the time at the mouse (on the active axis) | the user sees what "Seek here" will pick before right-clicking |
| **snapping** | a seek or a range edge snaps to an event within a few pixels, and to a frame boundary | "seek to the shot" without pixel hunting |
| **keyboard** (panel focused only) | ←/→ one frame, Shift+←/→ prev / next event, Home / End, +/− zoom, Space play / pause | precise review; ⛔ never stolen from the map when the panel is not focused |
| **zoom history** | Backspace / mouse-back returns to the previous view | after a deep zoom, one key back |
| **off-screen playhead** | an arrow at the edge where the playhead is; click = bring it into view | |
| **unavailable zones** | shade before a node's first frame (a late joiner) and after the live point | the user sees why a seek clamps |
| **collapsed pauses on S** | a break mark on the ruler (a small zigzag) at each collapsed pause, the ◯ / ● tick as its hit target | the time on the ruler is continuous, the world is not |
| **an axis switch keeps the view** | S ⇄ W maps the visible window through the time map — the same FRAMES stay in view, not the same numbers | otherwise the switch throws the user elsewhere |
| **view state is the viewer's** | zoom / pan / follow are per host and per user; only the axis is the session's (R-3058) | two operators zoom independently |

### 10c. The interaction spec — the one mapping every host's timeline obeys

| input | over the ruler / overview | over the lanes |
|---|---|---|
| wheel | ⭐ zoom about the mouse — proportional to the delta (smooth on a trackpad) | scroll the lanes vertically (the ruler and overview stay — sticky) |
| Ctrl+wheel, pinch | zoom about the mouse | zoom about the mouse |
| Shift+wheel, horizontal wheel | pan | pan |
| left drag | ruler: pan · overview: move the bracket; its EDGES: zoom | ⛔ nothing on empty space (a drag never pans or seeks by accident) |
| Shift+left drag | select a range | select a range |
| middle drag | pan | pan |
| left click | — | select the event (details, the unit) — ⛔ never seeks (R-3059) |
| drag the playhead ▼ | scrub (review / replay) — seek on release | — |
| double-click | fit all (ruler) | zoom to the event's cluster |
| right click | the seek menu (§9b) | the seek menu, with the event under the mouse |

| zoom limits | |
|---|---|
| closest | ⭐ about 10 frames across the panel (≈ 0.17 s at 60 Hz) — every frame a distinct column (T7) |
| widest | the whole extent (the session's zero → the live point / the end) |
| step | `factor = 1.0015 ^ (−wheelDelta × 120)` — a notch ≈ 15 %, a trackpad smooth (T5) |

| ruler | |
|---|---|
| steps | 1·2·5 × 10ⁿ frames → 0.1 s … 1 s, 2, 5, 10, 15, 30 s, 1, 2, 5, 10, 15, 30 min, 1, 2, 6, 12 h (T8) |
| labels | measured, never overlapping (a label is skipped when it would collide); minor ticks unlabelled |
| format | by the step AND the extent: `h:mm:ss` once the extent passes an hour, `m:ss.f` below a second, `+frame` at the closest zoom; sim / wall per the axis, "−5:27" in a review |

### 10d. Where it is fixed

⭐ T1–T13 and 10b–10c are the timeline panel's — ui's component (`DESIGN_Director_Timeline_Editor.md`, CE-1302). Filed as **CE-3330** for
the ui lane (T1, T2 and T12 first: without them the panel cannot serve live or a review); this section is the spec it builds against, and
its owner folds it into that design's §2b when it does.

## 11. ⛔ Critical review `2026-10-26` — NOT READY TO BUILD: the gaps and flaws, with a lean each

🔒 **User, `2026-10-26`:** *"yes please take a critical look and find gaps and flaws."* — two independent passes (backend's, and an
unanchored reviewer's that had not seen backend's conclusions); every finding below is checked against code (file:line). ⚠ They overturn
parts of R4, R6 and §3c — marked; the user decides D-A…D-H.

### 11a. Blockers — as designed, the return is NOT "exactly where we paused"

| # | flaw | evidence | ⏳ lean (fix) |
|---|---|---|---|
| **F1** | the return's keyframe is the DRY RUN's: it omits every singleton and every `NoPreview` type — `PositionGeodetic`, `DomainMissionPlan`, `SubordinateOrders`, `PausedTask`, `PendingAuthorityGrants`, `Lifespan`, … come back EMPTY; versioned singletons keep the last REPLAYED value (`SoundTimeline` resumes its numbering in the past — its guard only rejects values newer than the clock, `SoundEmissionSystem.cs:47-55`) | `WorldKeyframes.cs:55` (`NodeDryRun = Snapshotable, Singletons: false`); `Apply` → `repo.Clear()` | ⭐ capture with `CaptureOptions.Recording` (every recorded type AND the singletons) — the review replaces singletons, so it must put them back; rail: live → review → live leaves the world identical, singletons included. ⚠ R6 amended |
| **F2** | what the review puts ON THE WIRE is never taken back: replayed entities (alive in the past, dead at the live point) keep their newest DDS samples, TransientLocal hands them to late joiners, other nodes rebuild them as zombie ghosts; historic samples win over the live owner's until it republishes | a restore deletes nothing (`CycloneNetworkCleanupSystem.cs:52-57`: only `WorldEpoch.Ended`); ingress keeps the newest sample (`CycloneNetworkIngressSystem.cs:92-104`) | ⛔ ~~D-A: a review is WIRE-SILENT~~ — **overturned by the user (§11f)**: the review is on the wire. ⭐ **Fix: §12** — a dispose per id absent from the new world at every swap (entry, seek, exit), held egress between the swap and the master's commit, a full republish on release |
| **F3** | the pause and the return's snap DESTROY live kernel state: `SnapAndPause` bumps the timeline epoch ⇒ the kernel restarts the event accumulator, resets every slow module to "never ran" and DISCARDS in-flight async results — at entry (before the capture!) and again at exit | `MasterSyncController.cs:517-519`; `ModuleHostKernel.cs:570-592` | ⭐ enter with the BARRIER pause (`SwitchToDeterministic`) on a step with no slot run in flight (as the recorder defers checkpoints, CE-3316), never a snap; exit with CHECKPOINT semantics — `ResumeAfterRestore` (slots resume, unconsumed step events delivered, D2), not the dry run's. ⚠ §3c amended |
| **F4** | a failure during a review strands nodes: a node loss → `Degraded` / Idle, a master loss → every node Idle; the replay module stays installed (`IsReplayActive` true), the recorder is never finalised, and the NEXT live load is claimed as a *branch from a replay* — copying replayed history into a new exercise | `ClusterMaster.cs:711-742`; `ClusterSlave.cs:641-692`; `ReferenceReplayLoadHandler.cs:169,178,264-289` | ⭐ **D-C**: the review handler reacts to Idle — removes the replay module, FINALISES the recorder (the live point in `.meta.json`), drops the RAM keyframe. ⭐ Entry forces a labelled keyframe at the live point (`review-<n>`, R-3044), so a lost review is recoverable by the existing checkpoint restore (B4) |

### 11b. Major

| # | flaw | evidence | ⏳ lean |
|---|---|---|---|
| F5 | the nodes may capture DIFFERENT frames: `PrepareReview` (node-op topic) and the pause (time topic) are unordered | DDS orders no two topics | the master sends `PrepareReview` only after the barrier pause has landed on every node (all acks) |
| F6 | messages in flight at the live point: world EVENTS are dropped while a node restores (shots, hits, mission control); command-like samples on state topics (ownership hand-overs, attribute writes) land on the live world AFTER the return | `CycloneNetworkIngressSystem.cs:99-101`; `DeferredTakeOwnershipIngressTranslator`, `OwnershipUpdateTranslator` | a QUIESCE round at entry: each node drains its ingress and finishes in-flight protocols on the paused step before it captures; frozen ingress during the review and the exit's reader drain keep anything later from landing (§12 W5) |
| F7 | world-changing commands are NOT refused in a review: `/entities/spawn`, `/component`, `/order`, `/possess`, `/ownership/transfer`, `/missions/*/run`, `/scenario/save` (would save the REPLAYED world); map orders wired once (`MapInteractionPack.cs:348`); ExCon has no gate | `DebugApiHost.cs:618-1562` | **D-E**: ONE guard — the master refuses world-changing ops in `OperatingReview` / `OperatingReplay`; the debug API answers 409 *"a review shows the past — return to live first"*; the map hides its order actions; `/scenario/save` refused |
| F8 | the exit's order is unsafe: a seek still in flight snaps the live clock back into history (`ReplaySeekProcessManager.cs:59`); `PlaybackTickSystem` runs one more frame after the module's removal is requested and can overwrite the restored world; any seek pauses even live (`:49`) | `ClusterMaster.cs:1859` (no state check on seeks) | the master refuses new seeks and drains pending ones before `UnloadingReview`; the node awaits the module's removal, then applies the keyframe; seeks refused outside a replay / review |
| F9 | entry installs the replay module BEFORE its gates are on (prepare vs commit): for a frame `PlaybackTickSystem` runs over the LIVE world, its anchor unset — it can seek or `SoftClear` | `ReferenceReplayLoadHandler.cs:186-205` vs `298-309`; `PlaybackTickSystem.cs:100-110,197-206` | gates first, module second; `Abort` after a partial prepare is a full rollback (apply the keyframe, resume the recorder) |
| F10 | a node without the review handler answers `Success, IsParticipating=false` and silently STAYS LIVE (recording, ingress open) | `ClusterSlave.cs:505-515` | the master requires every ECS node's participation (by capability) or refuses the review |
| F11 | resuming the recorder writes everything destroyed DURING the review (every seek, `SoftClear`, the exit's apply) into the first live delta — a later replay of the exercise would destroy live entities; episode recorders keep recording replayed frames | `AsyncRecorder.cs:201,240`; `EcsRecordReplayController.cs:237-251` | on resume: clear the destruction log and force a keyframe (D4 / D8); suspend the episode recorders too |
| F12 | the wall clock across the return: the master's virtual wall after `SnapTo(L)` is below the last recorded stamp, so `Math.Max` stamps a flat run of frames (in stepped mode for steps worth the whole review); slaves stamp physical time — master and slaves disagree about the gap | `RecorderTickSystem.cs:88`; `MasterSyncController.cs:357`; `SlaveSyncController.cs:65,452` | the return restores the master's wall to its last recorded stamp (only sim time is snapped); the review's span goes into `Reviews[]`; the gap rule is one: the time map skips the review span on every node |
| F13 | the live point is found by DURATION: an open recording has `Duration = 0` (written on Dispose) ⇒ `TotalFrames / 60`; the end pause is a barrier with 0.2 s lookahead — at 4× it overshoots past the 2 s grace and the node Listens (`SoftClear`) | `EcsRecordReplayController.cs:121-131`; `TimeConfig.cs:75`; `PlaybackController.cs:41` | the live point = the MINIMUM over the nodes of their last frame's wall stamp; the end is an exact clamp in `ReplayClockProcessManager` (never a duration timer) |
| F14 | 🔴 **in a STEPPED recording every gap between steps is `dt == 0`** — the time map would hold one "pause" per step (tens of thousands), and R4's jump-by-`SnapTo` would pause and reset the epoch at every step | design §1; `MasterSyncController.cs:171-172` (`SnapTo` = `SnapAndPause`) | ⭐ **D-H**: the frame's flag records the clock's `HaltReason` (`PausedByOperator` vs `SteppingHeld`, `HaltReason.cs:44`); only OPERATOR pauses are stretches (marks, skipping); stepping gaps collapse silently; the sim-axis jump is a NEW non-pausing seek (no epoch bump). ⚠ R1 / R4 amended |
| F15 | ■ changes to ⏏ under the cursor: a double press across the state change returns to live and then STOPS the exercise | §9a | for 3 s after a return ■ asks "Stop the exercise?" (or ignores the press); the tooltip says so |

### 11c. Minor

| # | flaw | ⏳ lean |
|---|---|---|
| F16 | M1 is now answered by the code: the writer opens `FileShare.Read`, the reader `FileShare.Read` ⇒ on Windows the review's reader cannot open the live file (`AsyncRecorder.cs:131-132`, `PlaybackController.cs:72`); on Linux .NET only advises — a Linux test passes falsely. The index also adds a frame before its payload is complete (`:158-171`) | the reader opens `FileShare.ReadWrite` (as `RecordingSlice:28`); `Suspend` waits for writes; measure on WINDOWS |
| F17 | v7 header: 25 → 34 bytes (not "26 longer"); decoding a v6 recording at open is slow — it contradicts §9g's "loading holds only what is cheap" | §8 row 1 corrected; a v6 recording gets its time map from the background scan (the timeline fills in), not inside `PrepareReplay` |
| F18 | `ReplaySessionTopic` matched by exercise / session id cannot tell a review from live (same exercise, same session) | `+ ReviewId` (the master mints one per review) |
| F19 | the review's playback rate carries into live | the return restores the live rate |
| F20 | 25 checks of `ClusterState.OperatingReplay` in 14 files and 42 `IsReplayActive` readers need a decision each (the toolbar is DISABLED in a review by `ClusterTimeTransportAdapter.cs:76-80`; the editor's Stop does nothing outside a preview, `EditorTimeTransportFacade.cs:173`; …) | one predicate `ClusterStates.IsReplaying(s)` (replay or review) for the shared behaviour, explicit review cases where they differ; a rail lists every check |
| F21 | the `Changed` flag may be set on EVERY paused frame (singletons, ghost writes) | measure on the CE-4053 recording before relying on ◯ / ● · ✅ **MEASURED `2026-10-26` (M2): yes — every frame** (a v7 editor run of demo-city: 417 / 417 changed, all 64 frames of an operator pause included; the CE-4053 recording is v6 and carries no flags). ⇒ ◯ / ● cannot be drawn from it yet — CE-3332 |

### 11d. Decisions for the user *(leans)*

| # | question | ⏳ lean |
|---|---|---|
| **D-A** | does a review publish on the wire? | ~~no — wire-silent~~ ⛔ **overturned (§11f): YES — §12** |
| **D-B** | the return's semantics | ⭐ the checkpoint's (`ResumeAfterRestore`, the recorded types + singletons, the unconsumed events), entered on a quiet barrier-paused step |
| **D-C** | a node or the master lost during a review | ⭐ the review is abandoned; every node finalises its recording at the live point; the live run is recoverable with the existing checkpoint restore from the forced `review-<n>` keyframe — not automatic |
| **D-D** | a node joining during a review | ⭐ held: it joins after the return (catch-up already skips a replay) |
| **D-E** | world-changing commands during a review | ⭐ refused at the master + 409 on the debug API + hidden on the map |
| **D-F** | who may start / end a review | any operator; the master serialises; no lock — every host shows ⟲ |
| **D-G** | a node whose recording does not cover the reviewed moment (a late joiner) | ⭐ shows nothing there (the timeline shades it) — wire-silence means no other node's flood fills it |
| **D-H** | what is a pause in a stepped run | ⭐ only an OPERATOR pause (`HaltReason.PausedByOperator`); stepping gaps collapse with no mark |

### 11e. What is still missing before `READY-TO-BUILD`

1. ✅ the user's answers (§11f); R4, R6, §0, §3c, §4, §8 amended; §12 the wire;
2. the class diagram (§3b) brought up to §8: `ReplaySessionTopic`, `SetReplayAxisIntent`, `LiveTimelineSource`, `TimelineContextMenu`, the mode indicator, the quiesce round, `ClusterStates.IsReplaying`;
3. ✅ §3c redrawn (on the wire, §12);
4. §7 build order re-cut to §8's phases (M1 on Windows first);
5. measurements: ✅ M1, M4, M5 answered (§7); M2 (the `Changed` flag on CE-4053's recording) and M3 (capture + apply time of a `Recording` keyframe on `uc-urban-battle`) are measured inside P0 / P3 — neither blocks the design;
6. the rails named per phase: live → review → live identical (world + singletons + the next N steps' trajectory = the straight run — R1's oracle reused); the wire rails of §12e; a failure during a review leaves a finalised recording and a restorable `review-<n>`.

### 11f. ✅ The user's answers `2026-10-26` (R-3062, R-3063)

🔒 **User, verbatim:** *"approved with following notes: Of course full checkpoint necessary. Replay can not be left off the network; replay
needs to show on external IGs or HLA/DIS gateway (there is one, just not part of this repo yet). We need to resolve the zombie cleanup when
entering the review (to get rid of the live entities) as well when exiting it (to get rid of the review entities). 
Dispose sample for each such potential zombie is mandatory."* · *"every node that recorded something DOES replay and our node called IG (not the external IGs) is no
exception"*

| # | answer |
|---|---|
| **D-A** | ⛔ **OVERTURNED — a review IS on the wire** (external IGs, the HLA / DIS gateway). Every potential zombie gets a DISPOSE at entry and at exit — ⭐ §12 (R-3062) |
| **D-B** | ✅ the FULL checkpoint: every recorded type + the singletons, the unconsumed events, `ResumeAfterRestore` (R-3063) |
| **D-C … D-H** | ✅ as leaned (R-3063): abandon + finalise + a forced `review-<n>` keyframe · joiners held · world-changing commands refused · any operator · a node without the moment shows nothing · only an operator pause is a pause |

## 12. The review ON THE WIRE — every potential zombie disposed *(R-3062)*

🔒 the quote above. ⭐ The external consumers — an IG outside this repo, the HLA / DIS gateway — see ONLY DDS. A world that is replaced in
place (a review's entry, every seek, the review's exit) must therefore say on the wire which entities ceased to exist: **a DISPOSE per
network id that was published and is absent from the new world.** ⭐ Our own IG node is NOT such a consumer — it records and replays its own
recording like every recording node (R-4010, Q91 L5; ⛔ replay-and-modules §2.2 / §2.3 say otherwise and are superseded).

**INVENTORY** *(`2026-10-26`; ⚠ `check_index_coverage` is not available through the CLI)*: `search_graph(name_pattern=".*(Cleanup|Disposal|Dispose|Egress)System.*", label=Class)` → **14**; the ONE that writes DDS disposes is `CycloneNetworkCleanupSystem` (the egress systems under the W2 hold: `CycloneEgressSystem`, `SmartEgressSystem`, `OwnershipEgressSystem`, `PeerLifecycleStatusEgressSystem`, `SelectionEgressSystem`, the gizmo transport's). · `search_graph(".*(NetworkId|EntityId)Allocator.*")` → 0, so grep: `INetworkIdAllocator` / `IRestorableIdAllocator` (`DdsIdAllocator`, `SequentialIdAllocator`, `IgSequentialIdAllocator`). · `search_code("zombie")` → 128 matches / 66 files: replay-and-modules §3.10.4, mgmt-1 §8.10, dds-to-ecs §6.1, replication-fixes Issue 2 — the four §12d answers. · grep: `ForceMarkAllDirty` → 2 callers, both `PlaybackTickSystem`; `DestroyEntity` in the ownership / replication paths → none (a hand-over never destroys).

### 12a. Sequence — retract, then publish

```mermaid
sequenceDiagram
  participant M as ClusterMaster
  participant N as a recording node
  participant CL as its CycloneNetworkCleanupSystem
  participant EG as its egress translators
  participant X as external IG / HLA-DIS gateway
  Note over M,X: ENTRY - the world goes from the live point L to the review target T
  M->>N: PrepareReview (after every node acked the barrier pause)
  N->>EG: HOLD - no sample is written until the commit
  N->>N: full checkpoint of L in RAM, then the world becomes T
  CL->>CL: sweep - every id it published, looked up in the new world
  CL->>X: DISPOSE each id owned at L and absent at T
  N->>N: wait until the disposes are acknowledged
  N-->>M: prepared
  M->>N: CommitState(OperatingReview)
  N->>EG: RELEASE - a full republish of T
  EG->>X: the review's world
  Note over M,X: EVERY SEEK in the review (and in an ordinary replay) is the same hold, sweep, ack, commit, republish
  Note over M,X: EXIT - the world goes from the review frame R back to L
  M->>N: FinalizeReview
  N->>EG: HOLD
  N->>N: apply the checkpoint of L, ResumeAfterRestore
  CL->>X: DISPOSE each id alive at R and absent at L
  N->>N: wait for the acks, then DROP whatever its readers hold
  N-->>M: done
  M->>N: CommitState(OperatingLive) - live, paused
  N->>EG: RELEASE - a full republish of L, ingress resumes
```
*What the picture shows that the prose hid:* the dispose is NOT a signal any replacer has to remember — every in-place replacement goes
through `EntityRepository.Clear()`, which moves `ClearGeneration`, and the sweep runs on THAT, so entry, exit, every seek, a checkpoint
restore and the dry run's exit all get it for free *(as built — §12b)*. And the HOLD between a node's swap and the master's
commit is what keeps one node's retraction from landing AFTER another node's publication of the same id.

### 12b. The rule — one sweep, four cases

⭐⭐ **As built — CHANGE-DRIVEN, never per frame** *(user, `2026-10-26`: "Zombie cleanup is checked every frame for every entity. Are you serious?")*:

| when | what `CycloneNetworkCleanupSystem` looks at | cost |
|---|---|---|
| ⭐ the world was replaced in place — `EntityRepository.ClearGeneration` moved *(every keyframe apply goes through `Clear()`: a seek, a checkpoint / dry-run restore, a review's entry / exit; also a `GlobalVersion` that went backwards)* | ONE **mark-and-sweep by network id** — the table below | O(networked entities), once per replacement |
| an ordinary frame | `QueryDelta` over `NetworkIdentity`+`NetworkAuthority` since the last run: new owned ids tracked, a handed-over id forgotten | O(chunks) when nothing changed — unchanged chunks are skipped |
| an ordinary end | its `DestructionOrder` (step 2); a lightweight round (LR-2 — no lifecycle): only the LIVE ROUNDS are checked | O(events) · O(live rounds) |

⛔ **SUPERSEDED (this design's first build, `1b35571e7`):** the sweep ran EVERY frame over every networked entity — a dictionary rebuilt and diffed
per frame to avoid naming a signal that the engine already had. ⚠ What the change-driven form gives up, stated: an entity destroyed on an
ORDINARY frame with neither a `DestructionOrder` nor a world replacement is not retracted — the same as before PW (the ELM's timeout paths,
`EntityLifecycleModule.cs:410-456`, destroy entities still under construction).

The sweep — a **mark-and-sweep by network id** over the ids this node has published:

| an id this node published | in the world now? | ⭐ action | why |
|---|---|---|---|
| owned here | yes, owned | keep | its republish follows the commit |
| owned here | yes, as a GHOST (another node owns it in this world) | **forget — no dispose** | the owner publishes it; an ownership hand-over flips authority in place (no destroy in the ownership path — `OwnershipUpdateTranslator`, `DeferredTakeoverSystem`) |
| owned here | **absent** | ⭐⭐ **DISPOSE on every translator** (`DisposeAll`) | the potential zombie |
| — (new) | yes, owned | track and publish | `EntityMaster`'s once-per-id set forgets an id on its dispose (`EntityMasterEgressTranslator.cs:140`) and on a restore (`:60`), so it is published again |

⭐ **Network ids are never reused within one world** (the id authority's high-water mark only moves forward — `IRestorableIdAllocator`) ⇒
the same id at L and at T is the same entity, and the diff is exact. ⭐ LR-2's lightweight-round check (step 1b) stays — only the live
rounds are visited *(⛔ SUPERSEDED: "the sweep absorbs it" — true only of a per-frame sweep)* — and the world boundary keeps its own path (`Ended` ⇒ dispose everything, unchanged).

### 12c. The ordering rules

| # | rule | what breaks without it | ⭐ evidence |
|---|---|---|---|
| **W1** | the sweep above, once per in-place world replacement, on every node *(⛔ SUPERSEDED: "every frame")* | 🔴 **TODAY a backward seek, a checkpoint restore or a dry-run exit disposes NOTHING**: the cleanup disposes only on a `DestructionOrder` event or a world boundary, and a seek's keyframe is a `repo.Clear()` with no events — the vanished entities stay on the wire, TransientLocal hands them to late joiners (CE-3331) | `CycloneNetworkCleanupSystem.cs:54-124`; `PlaybackSystem.cs:52-58`; `AfterRestore.Run` (`WorldKeyframes.cs:157`): "nothing is deleted on the wire" |
| **W2** | egress HELD from a node's world swap until the master's commit (entry: `CommitState(OperatingReview)`; exit: `CommitState(OperatingLive)`; a seek: the master's post-seek snap, sent only after every node acked) — the cleanup's disposes go out during the hold | a node whose world lacks an id (a late joiner — D-G: its world is EMPTY there, so it disposes everything it owns at L) retracts an id that its owner AT T is publishing; if the publication lands first, the gateway deletes a live entity | DDS orders no two writers |
| **W3** | a node reports prepared / seek-done only after its disposes are ACKNOWLEDGED by the matched readers (bounded; a timeout is reported in the result, not fatal) | W2's barrier holds only if the disposes have ARRIVED before anyone publishes | 📐 **M5 measured:** acks are built into DDS — a RELIABLE writer's samples (and disposes) are acknowledged by every matched reader (RTPS ACKNACK), and the spec's `DataWriter::wait_for_acknowledgments` is Cyclone's `dds_wait_for_acks(writer, timeout)` — ✅ exported by the shipped `libddsc.so` (`nm -D`). ⛔ The .NET binding (`CycloneDDS.NET` 0.3.2) does NOT import it (its P/Invoke list has no `wait_for_acks`). ⇒ ⭐ add `DdsWriter.WaitForAcks(TimeSpan)` to CycloneDds.NET (the user's own repo) and bump the package. ⛔ A local P/Invoke is not clean: 📐 reflection over the binding shows no public writer handle (only `DdsParticipant.HandleWrapper`, internal). ⚠ Only a RELIABLE writer is covered — a best-effort topic has no acks; W3 names the reliable ones (the entity-master and state topics). ⭐ **WHO waits:** the node's review / seek handler, between its sweep's disposes and its "ready" to the master — and the master's commit already waits for every node's ready, so the whole cluster's republication waits for every retraction. ⭐ **WHEN it decides anything:** only when one node retracts an id that ANOTHER node publishes in the new world — a node with no recording at the reviewed moment (D-G: a late joiner, or a gap) that was later given an entity whose owner then was someone else. Without the ack the race needs that dispose to be delayed past the ready → master → commit round trip (a lost packet repaired late); the cost when it hits is high — the gateway deletes a live entity and `EntityMaster` is published once, so it stays deleted. ⇒ ⭐ insurance for a rare case: PW is built without it and W3 lands when the binding has `WaitForAcks` (CE-3000 ④); bounded (≈1 s) so a dead external reader cannot stall a seek |
| **W4** | on release: a FULL republish — `SmartEgressUtil.ForceMarkAllDirty` (already done on a seek, `PlaybackTickSystem.cs:175,183`) ⭐ **and in `AfterRestore`** | ⛔ the exit and a checkpoint restore do NOT force it today ⇒ the gateway keeps showing the REVIEW's state for every entity that survives, and live comes back PAUSED, so nothing changes to repair it | `grep ForceMarkAllDirty` — two callers, both in `PlaybackTickSystem` |
| **W5** | 🔒 *(user, `2026-10-26`: "We will be sending, but ignoring incoming traffic during the replay, because in our world every ecs node is reconstructing its own state from his own recording.")* during a replay / review a recording node SENDS but takes nothing from the wire into its world; ⭐ at the exit's release it also DISCARDS the world STATE its readers hold (not only the events), then ingress resumes | 📐 **M4 measured:** while a replay restores, ingress DISCARDS world EVENTS but LEAVES world STATE in DDS (the join rule, R-4007 — `CycloneNetworkIngressSystem.cs:92-104`). ⇒ at the exit the readers still hold, per instance, the newest REVIEW-era sample or the entry's DISPOSE; the first poll after the return would write review state over the restored ghosts, or tear down a ghost alive at L, before the owners' republish lands | the join rule keeps state on purpose (a joiner wants the TransientLocal state); the exit wants the opposite — the node already HAS L from its own checkpoint. ⇒ a state-discard beside `IDiscardableIngress` (today the events' only) |
| **W6** | the recorder on resume: its destruction log cleared and a keyframe forced (F11) | the review's swaps are written into the first live delta | `AsyncRecorder.cs:201,240` |

### 12d. Rejected — one line each

- **a wire-silent review** (the D-A lean) — 🔒 overturned: the external IGs and the HLA / DIS gateway must show the review.
- **retract everything, republish everything** at each swap — every entity deleted and re-created on the gateway (a new HLA object, the IG's effects and smoothing lost) where the diff keeps the survivors continuous.
- **clear the tracking on a seek** (replay-and-modules §3.10.4) — that is exactly what forgets the vanished ids WITHOUT disposing them; its "mass dispose on every seek" premise was never the code (the cleanup disposes only on events).
- **the local ghost purge on a seek** (mgmt-1 §8.10 "heavy seek") — reaches only nodes that run the op; an external consumer sees only DDS.
- **a dispose raised by a seek signal** — every path that replaces a world (seek, keyframe, review, dry run, checkpoint) would have to raise it; the frame sweep needs none.

### 12e. Rails

- live → review → live: the set of DDS instances alive on a test reader equals the live world's owned ids at L, before and after; every id alive only in the review was disposed (a reader that saw the review asserts `NotAliveDisposed`).
- an ordinary replay's backward seek: an entity created after the target is disposed on the wire (CE-3331 — red today).
- a checkpoint restore leaves no entity of the abandoned future on the wire, and its survivors are republished (W4).
- a late joiner's retraction never deletes an entity its owner at T publishes (W2 + W3): the reader sees it alive after the commit.

### 12f. ✅ As built — PW `2026-10-26` (CE-3331 closed; rows 35, 36, 38 built; row 37 waits on CE-3000 ④; row 39 moved to P3)

```mermaid
classDiagram
  direction LR
  class CycloneNetworkCleanupSystem { <<exists, grown>> mark-and-sweep on ClearGeneration · delta otherwise · Sweeps · SweptDisposes · ForgottenHandedOver }
  class EgressHold { <<NEW Transient world singleton, id 477>> Hold(world, reason) · Release(world) · IsHeld(view) - lapses after 3 s }
  class CycloneEgressSystem { <<exists>> skips its scan while held }
  class IClusterStateUpdateObserver { <<NEW, the IIdleResetHandler shape>> OnClusterStateUpdate(state, masterGone) }
  class ClusterSlave { <<exists>> notifies every observer on each ClusterStateUpdateEvent }
  class ReferenceReplayLoadHandler { <<exists>> NodeReplaySeek: HoldEgress before the swap, release on the next broadcast }
  class ReferenceCheckpointHandler { <<exists>> RestoreSnapshot commit: hold before PutBack, release on the next broadcast }
  class IRecordReplayController { <<exists>> HoldEgress(reason) · ReleaseEgress() - default no-ops }
  class AfterRestore { <<exists>> + SmartEgressUtil.ForceMarkAllDirty (W4) }
  ClusterSlave --> IClusterStateUpdateObserver : each broadcast
  IClusterStateUpdateObserver <|.. ReferenceReplayLoadHandler
  IClusterStateUpdateObserver <|.. ReferenceCheckpointHandler
  ReferenceReplayLoadHandler --> IRecordReplayController : hold / release
  IRecordReplayController ..> EgressHold : EcsRecordReplayController
  ReferenceCheckpointHandler --> EgressHold
  CycloneEgressSystem ..> EgressHold : reads
  CycloneNetworkCleanupSystem ..> EgressHold : ignores - disposes pass
```
*What the picture shows that prose hid:* the hold is world state nobody wires (the `WorldEpoch` pattern), and its RELEASE rides an
existing broadcast — the master already publishes the cluster state when a seek or restore round completes on every node
(`BroadcastClusterStateOnComplete`), and a node commits before it answers (`ClusterSlave` runs `Commit` before `NodeOpCompletedEvent`), so
the first broadcast after a node's answer is the master's commit.

| row | as built | deviation from §12 — and why |
|---|---|---|
| 35 W1 | `CycloneNetworkCleanupSystem`: ⭐ *(revised `2026-10-26` — see §12b: the sweep runs when `EntityRepository.ClearGeneration` moves, a `QueryDelta` otherwise; LR-2's live-round check is BACK)* on a sweep, `_present` = this world's networked ids (owned or not); a tracked id absent ⇒ `DisposeAll`, present as a ghost ⇒ forgotten; owned ⇒ tracked, its handle refreshed. ⭐ An entity in `TearDown` counts as ABSENT, so whichever sees it first (the sweep or its `DestructionOrder`) retracts it once and it is never re-tracked while it lingers for acks. ⛔ ~~The LR-2 lightweight special case is gone~~ — back: without a per-frame sweep a burst round needs its own check | ⚠ the TearDown rule is new: a keyframe restores entities under the SAME handles (`WorldKeyframesTests.AfterRestore_RebuildsTheEntityMap`) and a replay seek moves no `WorldEpoch`, so "retracted" could not be keyed on the handle or the epoch. ⚠ A path that publishes a `DestructionOrder` without `TearDown` (the gateway's reliable-init timeout, `NetworkGatewaySystem.cs:247`) may dispose twice — a repeated DDS dispose, harmless |
| 36 W2 | `EgressHold` (Transient world singleton, `GlobalComponentIds.EgressHold = 477`); `CycloneEgressSystem` returns before its scan while held; a seek holds in `ReferenceReplayLoadHandler` (via `IRecordReplayController.HoldEgress`) BEFORE the swap, a checkpoint restore holds in `ReferenceCheckpointHandler.Commit`; both release on the next cluster-state broadcast (`IClusterStateUpdateObserver`, called by `ClusterSlave.FollowClusterSession`) | ⚠ the release is "the next broadcast after this node answered", not a message naming the round: a broadcast from an unrelated round landing in that window releases early (rare; W3 is the real guarantee). ⭐ Bounded: a hold lapses after 3 s with a warning (`EgressHold.Lapsed`) so a lost broadcast never silences a node. ⚠ id 477 is from the coordinator / new-lane block (backend's 434–449 is full — the `AirFollower` precedent); the backend block question is open with the user |
| 37 W3 | ⏸ not built — CE-3000 ④ (`WaitForAcks` in CycloneDds.NET). The seam: `IRecordReplayController.HoldEgress` / the handlers' release are where the ack wait goes | — |
| 38 W4 | `AfterRestore.Run` ends with `SmartEgressUtil.ForceMarkAllDirty` — a checkpoint restore and a dry-run exit republish everything | — |
| 39 W5 | ⏭ **moved to P3** — its only caller is the review's exit; the state discard needs a take-and-drop on every WORLD-STATE translator (`TranslatorClass.WorldState` is the default of ~170 translators, `IDiscardableIngress` is implemented by 5 event translators) | built with its caller rather than ahead of it |

**Rails** (`Hrot.SimHost.Tests/CycloneNetworkCleanupSystemTests` — the feature's suite): `CE3331_AWorldReplacedInPlace_DisposesWhatItLacks_AndKeepsTheSurvivors` (red-proved on the old system), `AHandOver_IsForgotten_NeverDisposed`, `ATornDownEntity_IsDisposedOnce_AndASeekThatBringsItBack_TracksItAgain`; `Fdp.Toolkits.Tests/Replay/WorldKeyframesTests.AfterRestore_MarksEveryDescriptorDirty_SoTheRestoredWorldIsRepublished`; `Orchestration/ReferenceHandlerTests.W2_A_replay_seek_holds_egress_until_the_masters_commit_broadcast`; `Fdp.Network.Cyclone.Tests/TheEgressHoldTests` (new — no egress-system suite existed). ⭐ For the coordinator's H2 wire check (NOTE §4): assert on `CycloneNetworkCleanupSystem.SweptDisposes` per node, or a DDS reader on the `EntityMaster` topic (every entity's lifetime instance).

### 12g. ✅ As built — P0 `2026-10-26` (rows 1–6)

| row | as built | deviation — and why |
|---|---|---|
| 1 | `FrameOuterHeader` v7 = v6 + `double SimTime` + `byte Flags` (34 bytes); `FrameFlags` { `Halted` = 1, `Changed` = 2, `Unknown` = 0x80 }; `FdpConfig.FORMAT_VERSION = 7`, `MIN_READABLE_FORMAT_VERSION = 6`; `FrameOuterHeader.SizeFor(version)` / `Read(bytes, version)` | ⚠ **`Halted` is the clock's `IsHalted`, not a `HaltReason`** (D-H asked for the reason). Measured in code: on a node an operator pause and a gap between steps are the SAME clock state (slave `Stepping`, nothing in flight — `HaltReasonResolver` tells them apart only on the master, by outstanding step acks). ⇒ the distinction is drawn by `ReplayTimeMap` from the frame PATTERN (a flat run that follows a one-frame step that followed a flat run is a step gap — §12h) |
| 2 | `AsyncRecorder` stamps both on the main thread before dispatch (`StampIndex`): sim from the world's `GlobalTime` singleton; `Changed` = `RecorderSystem.LastFrameChanged` (destructions, event streams or component chunks written; singletons excluded; a keyframe always) | — |
| 3 | `PlaybackController` indexes `SimTime` + `Flags` (`FrameMetadata`); a v6 file opens, indexes and seeks — its frames read `SimTime = NaN`, `Flags = Unknown` | ⚠ **v6 is NOT decoded at open** (F17: slow, and against §9g's "loading holds only what is cheap") — a v6 recording has no sim axis (`ReplayTimeMap.Empty`); decoding it in the background scan is left for the timeline index (row 11) |
| 4 | `PlaybackSystem`, `RecordingSlice` read by the file's version | — |
| 5 | `RecordingSlice.CopyFrames` (D18's branch copy) writes the CURRENT format: the global header's version and every frame header converted (a v6 frame → v7 with `Unknown`) | — |
| 6 | `/replay/status.simSeconds` — the recorded sim of the frame on screen (`IRecordReplayController.CurrentReplaySimSeconds`: the world's `GlobalTime` while the kernel's clock push is suspended in a replay) → `ReferenceReplayLoadHandler.ReplaySimSeconds` → `ReplayPosition.SimSeconds` | — |

**Rails** (`Fdp.Core.Tests/PlaybackControllerTests`): `V7_TheFrameIndexCarriesTheRecordedSimTimeAndTheFlags`, `V6_ARecordingInTheOldFormat_StillOpensIndexesAndSeeks`, `V6_ABranchCopyOfAnOldRecording_IsWrittenInTheCurrentFormat`. Three rails that pinned `FORMAT_VERSION == 6` (`QueryTests`, `WcrBatch01Tests`, `RecorderSystemTests`) now assert the v6 break is in (`>= 6`); the header-written rail compares with the constant.

### 12h. ✅ As built — P1 `2026-10-26` (rows 7–10, 12, 13 partly, 31, 34 / 11, 32, 45 not yet)

```mermaid
sequenceDiagram
  participant H as any host (HTTP / UI)
  participant M as ClusterMaster
  participant C as ReplayClockProcessManager
  participant K as the master clock
  participant N as every node
  N-->>M: ReplayPrepareResult + its ReplayTimeMap (from its v7 index)
  M->>M: ReplayConsensusAggregator: Intersect(maps, earliest-first node leads)
  H->>M: SeekReplayIntent {TargetSimSeconds} or TransitionState {TargetSimSeconds}
  M->>M: RelativeTicksForSim = map.WallAt(sim, AfterStretch) - zero
  M->>N: NodeReplaySeek (wall ticks - nodes unchanged)
  H->>M: SetReplayAxisIntent (SetReplayAxis = 22 on the wire)
  M->>N: cluster-state broadcast carries ReplayAxis
  loop each master tick, OperatingReplay, Sim axis, clock running
    C->>C: map.NextUnpause(zero + playhead)
    C->>K: JumpTo(end) - JumpTimeIntent, no pause, no epoch move
    K->>N: SwitchTimeModeEvent Continuous with the new sim time
  end
```
*What the picture shows that the prose hid:* nodes never learn about sim time. Every sim request becomes the wall seek they already
perform, and every jump becomes the resume they already follow. The only new node-side fact is the axis, and it rides an existing broadcast.

| row | as built | deviation — and why |
|---|---|---|
| 7 | `ReplayTimeMap` — piecewise-linear keypoints (O(n) slope-window fit, ≤ 0.5 ms sim error), operator `Stretches`, `SimAt`, `WallAt(sim, Edge)`, `NextUnpause`, `Intersect`, `FromRecording` | ⚠ **in `Fdp.Core.FlightRecorder`, not `Fdp.Toolkits/Replay`**: it crosses `IRecordReplayController` (Fdp.Core). ⚠ **D-H drawn from the frame pattern** (§12g): a flat run that follows a ≤ 2-frame step that followed a flat run is a step gap. ⚠ Sim matches use the tolerance (10 × 0.1 ≠ 1.0) |
| 8 | `ReplayPrepareResult.TimeMap` (each node's `ReplayModule.TimeMap`, via `IRecordReplayController.ActiveReplayTimeMap`); `ReplayConsensusAggregator` intersects, the earliest-starting node's sim times; `ClusterMaster.ReplayTimeMap` | — |
| 9 | `ReplayClockProcessManager` (master): on the Sim axis, while `OperatingReplay` and the clock runs, a playhead inside a flat run → `ITimeCommands.JumpTo(end)` → `JumpTimeIntent` → `MasterSyncController.JumpWhileRunning` (no pause, no `TimelineEpoch` move, slaves follow a Continuous `SwitchTimeModeEvent`) | ⚠ **not yet**: the per-segment RATE (a run recorded at 2× plays its sim at 2×) and absorbing `ReplayProcessManager`'s end pause — both still with `ReplayProcessManager` |
| 10 | `SeekReplayIntent.TargetSimSeconds`, `TransitionStateIntent.TargetSimSeconds` (wins over wall); the master resolves (`RelativeTicksForSim`), refuses a sim seek on a replay with no sim axis (v6); `SetReplayAxisIntent` + `ReplayAxis {Sim, Wall}` (R2: Sim by default); the axis on `ClusterStateTopic.ReplayAxis` → `ClusterStateUpdateEvent` → `ClusterSlave.ReplayAxis` / `ClusterUiCache.ReplayAxis` | ⚠ **the axis rides the cluster-state broadcast, not a new `ReplaySessionTopic`** (row 32): every node already follows that broadcast, so the axis is the same on every host with no new topic; the session layer's map / live point / checkpoints are P4's (ui) need and get the topic then |
| 12 | HTTP: `atSimSeconds` on `POST /replay` and `/replay/seek`; `POST /replay/axis {axis}`; `/replay/status` `axis` (+ `simSeconds`, P0); route docs; MCP catalog regenerated (`set_replay_axis`; `seek_replay` forwards every form; `enter_replay` forwards `atSimSeconds`, `path(s)`) | — |
| 13 | Replay Browser: its group's map (`TimeMap`, the same intersection), `atSimSeconds` seeks, `simSeconds` in its position, its OWN axis (the viewer's, R2) | ⏭ the timeline / S-W icon is row 34 (ui) |
| 31 | `ClusterOpType.SetReplayAxis = 22` (both enums, R-42); `SeekReplayPayloadDto` / `SetReplayAxisPayloadDto` with ONE `From` / `Decode` each, used by the egress, the master's translator, its request adapter and the cluster panel (the panel's own parser deleted); the cluster-state decoding is ONE `OrchestrationObserverTranslator.StateOf` for both DDS readers | — |
| 11, 32, 34, 45 | ⏭ not built — the timeline index columns (11), `ReplaySessionTopic` (32, 45) and the Replay Browser's S / W icon (34) are the UI session layer's | — |

**Rails**: `ReplayTimeMapTests` ×7 (Toolkits); `ClusterMasterSeekTests.CE3329_*` ×4 (the aggregate's map over JSON, a sim seek resolved / refused without a sim axis, the axis broadcast, the jump manager — once per stretch, never on Wall / paused / live, never a snap); `MasterSyncControllerTests.CE3329_*` ×2 (a jump keeps the clock running and the epoch still; the intent path); `TranslatorDtoTests` (seek + axis cross the wire whole; the transition's sim target); `TheClusterControlRoutesTests` ×2 (sim seek / entry carry the target; `/replay/axis` and the status).

### 12i. ⛔ HISTORY — P2 stopped `2026-10-26` (the census below is still true; the lean is SUPERSEDED by §12l — the user chose the replay flag)

📐 **The census** (read-only sweep: `search_graph(".*Module$", Class)` → 104 total over two pages, grep `IEcsModule` → 40 production
module classes, 30 registered, 10 dead; `RegisterGlobalSystem(` → **79** production call sites in 17 files):

| finding | evidence | consequence for row 14 |
|---|---|---|
| modules are MIXED: `NedReplicationModule` / `BdcReplicationModule` carry the egress (must run — the review is ON the wire, R-3062) beside `DeadReckoningSyncSystem` (writes `SimTransform`, `DeadReckoningSyncSystem.cs:115`) and the ownership systems (recompute, reclaim, hand-over, `GhostCreationSystem.cs:66`) | `NedReplicationModule.cs:443-477` | ⛔ a per-MODULE attribute cannot say "egress yes, dead reckoning no" ⇒ the declaration must be per SYSTEM |
| 79 `RegisterGlobalSystem` sites outside the groups (EntityCreation.RegisterOn, GenesisTail, the IG / CGF / editor apps, StrideNodeShell, NED translators …) | grep | the gate must cover kernel systems, not only modules |
| world-writing modules tick in a replay TODAY: `StrideMuscleModule` (the whole Stride muscle tier, `StrideMuscleModules.cs:260-268`), `TrafficSteppingModule` (SUMO), `EqsModule`, both navigation modules, `AnimationMuscleModule` (stance / montage writes), `GeographicModule` (`GeodeticSmoothingSystem.cs:53` writes `Position`), `NetworkSpawningSystem` (creates entities), `EntityLifecycleModule`'s `BlueprintApplicationSystem` (its `LifecycleSystem` is already replay-gated), `EditorSystemsModule` | the census table (11 SUSPEND) | the gap §3d names is real and wider than drawn |
| UNSURE without a measurement: `IgGroundClampingModule` (writes the recorded `SimTransform` Z), `HistoryTrailModule` (appends to a recorded trail), `GizmoInteractionModule` (the map's order dispatcher), `AnimationMuscleModule`'s pose bridge (display?), and whether `[TickMode(WhenTimeAdvances)]` modules tick at all while the kernel's clock push is suspended | — | ⏭ measure in a live replay before classifying |
| the Replay Browser has no kernel (`ReplayBrowserSubsystem.cs:352`) | — | nothing to gate there |

⛔ ~~**Lean for the user**~~ *(SUPERSEDED by §12l)* (R-3057 R7 amended): ⭐ **declare per SYSTEM** — `[RunsDuringReplay]` on `IEcsModuleSystem` types (egress, playback, recorder, presentation, diagnostics), and the kernel's gate skips every other system registered outside the four toggle groups while a replay or review is open; a module's `Tick` follows its systems' majority only where it has no systems. A census rail lists every production system outside the groups with its decision (the shape of `EverySystemStateFieldIsClassifiedTests`). Blast radius: every host's replay; what would change the lean: the TickMode measurement showing most of them already idle in a replay.
⇒ ⛔ **not built in this batch.** ⚠ P3's review therefore runs with today's replay gates — the four groups, the ELM, frozen ingress; the modules above still tick during a review (the same as in an ordinary replay today).

### 12j. ✅ As built — P3 `2026-10-26` (rows 15–20, 40–43; 44 partly)

| row | as built | deviation — and why |
|---|---|---|
| 15 | `LoadingReview = 50`, `OperatingReview = 51`, `UnloadingReview = 52` and `NodeOpType.PrepareReview = 33`, `FinalizeReview = 34` in BOTH enums (R-42); `HrotStateGraph` edges `OperatingLive → LoadingReview → OperatingReview → UnloadingReview → OperatingLive`; the prepare-op mapping; the master refuses a review unless live (⛔ the planner would LOAD a live run from Idle to reach it), a review asked during a review is a seek | — |
| 16 | `ReferenceReviewHandler` (every ECS host — `EcsHostClusterHandlers.Compose`): `PrepareReview` = wait for the barrier pause (F5) → the forced `review-<n>` keyframe (D-C) → `SuspendForReview` → the FULL checkpoint (`CaptureOptions.Recording`, F1 — red-proved: `NodeDryRun` brings the singleton back replayed) → egress hold → the replay through the SAME `IRecordReplayController.PrepareReplayAsync`; Commit = `ReferenceReplayLoadHandler.EnterReplayGates` (the ONE gate set, extracted, F9). `FinalizeReview` = teardown awaited (F8) → Commit: hold, `ReferenceCheckpointHandler.PutBack` (the checkpoint's return: `AfterRestore` + republish, the ELM, `ResumeAfterRestore`), `LeaveReplayGates`, `ResumeAfterReview`. Idle during a review ⇒ abandon (D-C, F4) | ⚠ ⏭ not built: the quiesce round (F6), W3 acks, W5 the readers' state discard, F10 "every ECS node must participate" (every ECS host composes the handler, so it does), F15's ■ confirm (UI) |
| 17 | `RecorderTickSystem.Suspend / Resume(atSimSeconds)` (resume: waits until the clock stands at L — §12k —, then the destruction log cleared, a keyframe forced — F11, W6); `RecordingModule.SuspendForReview / ResumeAfterReview / PendingCheckpoints` | ⚠ `.meta.json` `Reviews[]` and the episode recorders' suspension not built |
| 18 | ⚠ **no `BeginReview / EndReview`**: the review uses the controller's ordinary `PrepareReplayAsync` / `TeardownReplayAsync` — the live exercise's file IS the recording to replay | one path, not two |
| 19 | `ReviewProcessManager` (master, before `ClusterMaster`): the barrier `Pause` at entry (never a snap, F3), L + the rate remembered when the pause lands, on `OperatingLive` after a review: `SnapTo(L)` + `SetTimeScale(rate)` (F19) | ⚠ F8's "refuse new seeks, drain pending ones" and F12's wall restore not built |
| 20 | `POST /review {atSeconds? | atSimSeconds?}` (default: the live point), `POST /review/exit`, `POST /review/seek` (= `/replay/seek`); `/replay/status` `review`; MCP `enter_review` / `exit_review` / `seek_review` | the live point is `/replay/status.durationSeconds` in a review (the review's aggregate ends at the EARLIEST last frame, F13) |
| 41 | `ClusterStates.IsReplaying` / `IsReview` (F20) used by the end pause, the jump manager, seeks; the guard: `DebugApiHost.WorldChangingRoutes` (24 routes) answer **409** while a recording is shown, the master refuses `SaveScenario` | ✅ the map's order actions hidden (ui, `2026-10-26`, CE-1363): the unit menu (orders, Designate, Possess, Embark — and the key shortcuts, which read the same menu), the RTS tool and the Delete key read the world's `ReplayState` each frame; Add Entity greys out ("a recording is shown"); the build-time `MapInteractionContext.IsReplayActive` is removed (R-3064 — it left the editor and CGF offering orders in a review); F20's census of the 25 state checks / 42 `IsReplayActive` readers not done |
| 42 | the entry is the replay's seek round (`NoteReplayEntry` sees `OperatingReview`, `_reviewEntryWallTicks` = `long.MaxValue` → the live point) | the barrier-ack ordering is node-side (each handler waits for its own pause), not a master round |
| 43 | `PlaybackController` opens `FileShare.ReadWrite` (M1) | ⚠ the Windows rail is not written — this container is Linux, where the flag is advisory |

**Rails**: `ReferenceHandlerTests.CE3329_Live_review_live_leaves_the_world_identical_and_the_recording_resumable` (a real `RecordingModule`: the labelled keyframe written, nothing written during the review, world + singleton + destroyed review-only entity back, the same file grows after) — red-proved; `…_A_review_never_entered_ignores_idle_…`; `ClusterMasterReviewTests` ×4 (only from live, the prepare + the live-point seek, the save refused, the review clock: pause / L / snap / rate); `TheClusterControlRoutesTests` ×2 (`/review` and exit, every guarded route exists). ⭐ The scenario-scale oracle (live → review → live = the straight run) is the coordinator's H5 (`NOTE_Coordinator_To_Backend_Review_Harness` §1).

### 12k. 📐 Measured live `2026-10-26` — the editor, demo-city (`scratchpad/run-dar.sh`)

| step | measured |
|---|---|
| live 30 s with a 6 s operator pause | the v7 index: the pause is ONE flat run (62 frames at sim 15.57 s); M2 — `Changed` on every frame (CE-3332) |
| `POST /review` (no moment) | `OperatingReview`, `simSeconds` 31.366 = the live point, `durationSeconds` 38.28 (the live point after the zero) |
| `POST /entities/spawn` in the review | **409** "the cluster shows a recording" |
| `POST /review/seek {atSimSeconds: 10}` | `simSeconds` 9.895 (the frame on or before; the map's wall → nearest frame) |
| `POST /replay/axis {Wall}` | `axis: Wall` in every status |
| `POST /review/exit` | `OperatingLive`, paused, `simTime` 31.365524 (= L to the frame), 549 entities (as at L); play goes on |
| the live recording afterwards | 🔴 **runs 1–2: three review-era frames (sim 10.43 s) written after the return** — the kernel's clock push resumes at the node's commit, the master's snap to L lands a frame later, and the first frame carries L in the RESTORED singleton (so "the clock is at L once" proved nothing). ✅ **Fixed** (`RecorderTickSystem.Resume(atSimSeconds)`: the resumed recorder writes only after the clock STANDS at L for 3 frames — the return snaps and pauses there; rail extended, red-proved). Run 3: no review frame; the pause before the review and the paused return are ONE flat run at 31.366 s |

⏭ Not measured: M3 (the capture + apply time of a `Recording` keyframe on `uc-urban-battle`) and a five-process review (the coordinator's H5 runs live → review → live at scenario scale).

### 12l. ✅ As built — P2 `2026-10-26`: ONE replay flag in the world; every world writer reads it

🔒 User, `2026-10-26`: *"Cannot every system simply check if there is a replay or not and behave according to that?"* → *"P2 using the
replay flag approved."* ⛔ SUPERSEDES row 14's `ReviewGate` + `[RunsDuringReplay]` and §12i's per-system-attribute lean.

```mermaid
classDiagram
  class ReplayState {
    <<new, world singleton, Transient, id 478>>
    -volatile bool active
    +Ensure(world)
    +Enter(world)
    +Leave(world)
    +IsActive(view) bool
  }
  class EcsRecordReplayController {
    <<exists, grown>>
    ctor: Ensure
    PrepareReplayAsync: Enter
    TeardownReplayAsync: Leave
  }
  class ReplayBrowserSubsystem {
    <<exists, grown>>
    PrepareRepo: Enter on every world it binds
  }
  class EntityRepositorySync {
    <<exists, grown>>
    SyncFrom: borrows ReplayState like WorldEpoch
  }
  class WorldWriter {
    <<every system that writes world state>>
    Execute: if IsActive(view) return
  }
  EcsRecordReplayController --> ReplayState : sets
  ReplayBrowserSubsystem --> ReplayState : sets
  EntityRepositorySync --> ReplayState : snapshot sees the live flag
  WorldWriter ..> ReplayState : reads
```
*What the picture shows that the prose hid:* nothing is WIRED into a system any more — the writer reads the world it is handed, so a
host cannot forget a gate; and a background (snapshot) module reads the SAME flag object, borrowed by `SyncFrom`.

| rule | why |
|---|---|
| the flag is set by what INSTALLS a replay (the controller every ECS host builds; the Replay Browser for its worlds) | ⛔ SUPERSEDED: six injected `Func<bool>? IsReplayActive` predicates wired in five host roots — unset meant "live", silently |
| Transient, created once on the composing thread; prepare only flips a volatile field | a keyframe never carries it (a seek, a review's return keep it); no singleton is written off the frame thread |
| borrowed into snapshots (`EntityRepository.Sync.cs`, beside `WorldEpoch`) | 📐 the census: `EqsModule`, `NavigationSolverModule` run on a snapshot — without it their check read "live" forever |
| a writer stands down, a reader / publisher does not | the review is ON the wire (R-3062): egress, cleanup, presentation, recorder, playback, diagnostics keep running |
| a LISTENING replay (the moment is outside this node's recording, R-4012 — `PlaybackTickSystem` sets the bit on every span change) still smooths ghosts: `DeadReckoningSyncSystem` and `GeodeticSmoothingSystem` read `IsRestoring`, every other writer `IsActive` | a listening node's world IS the owners' flood from the wire — without the smoothers its ghosts would stop moving; on a RESTORING node they would overwrite the recorded poses |
| the Stride pre-kernel bracket: Bullet does not step, motors and the reverse sync do not run | they write Bullet's poses over the recorded `SimTransform` (`StridePhysicsBracket.RunPreKernelStep`) |

⭐ **The gated writers** (57 — R-3064) — one line at the top of `Execute`. ⚠ The table is a RAIL: `EveryReplayWriterIsGatedTests` reads it and fails when a
listed file no longer calls `ReplayState.IsActive`.

<!--REPLAY-WRITERS-->
| system | file |
|---|---|
| `AnimationCapabilityChangeReactorSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationCapabilityChangeReactorSystem.cs` |
| `AnimationDispatcherSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationDispatcherSystem.cs` |
| `AnimationStateReporterSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationStateReporterSystem.cs` |
| `AreaEffectSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/AreaEffectSystem.cs` |
| `BallisticsSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs` |
| `BlueprintApplicationSystem` | `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/BlueprintApplicationSystem.cs` |
| `BlueprintEventIngressSystem` | `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintEventIngressSystem.cs` |
| `BlueprintMaterializationSystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/BlueprintMaterializationSystem.cs` |
| `BrainHandOverSystem` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BrainHandOverSystem.cs` |
| `CombatTraceSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/CombatTraceSystem.cs` |
| `CountermeasureSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/CountermeasureSystem.cs` |
| `CreateEntityRequestSystem` | `Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs` |
| `CrowdAgentUpdateSystem` | `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs` |
| `DamageCalculationSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/DamageCalculationSystem.cs` |
| `DeadReckoningSyncSystem` | `Hrot/Engine/Hrot.Core/Systems/Common/DeadReckoningSyncSystem.cs` |
| `DeleteEntityRequestSystem` | `Hrot/Engine/Hrot.Common/Systems/DeleteEntityRequestSystem.cs` |
| `EditorPerceptionSetupSystem` | `Hrot/Subsystems/Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs` |
| `EditorZoneAuthoringSystem` | `Hrot/Subsystems/Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs` |
| `EffectSpawnSystem` | `Hrot/Engine/Hrot.Presentation/Effects/EffectSystems.cs` |
| `EngineBackedPathResponseSystem` | `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedPathResponseSystem.cs` |
| `EqsSolverSystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` |
| `FireProcessingSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/FireProcessingSystem.cs` |
| `FormationTargetSystem` | `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/FormationTargetSystem.cs` |
| `GenesisMaterializationSystem` | `Hrot/Engine/Hrot.EcsNode/Systems/GenesisMaterializationSystem.cs` |
| `GeodeticSmoothingSystem` | `FDP/Toolkits/Fdp.Toolkits/Geographic/Systems/GeodeticSmoothingSystem.cs` |
| `GhostPromotionSystem` | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs` |
| `GuidedRoundSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/GuidedRoundSystem.cs` |
| `HitResolutionSystem` | `FDP/Toolkits/Fdp.Toolkits/Physics/Systems/HitResolutionSystem.cs` |
| `InitialBrainMaterializationSystem` | `Hrot/Engine/Hrot.EcsNode/Systems/InitialBrainMaterializationSystem.cs` |
| `LifecycleSystem` | `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/LifecycleSystem.cs` |
| `LifespanSystem` | `FDP/Toolkits/Fdp.Toolkits/Time/LifespanSystem.cs` |
| `LightweightRoundIdentitySystem` | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/LightweightRoundIdentitySystem.cs` |
| `LinearKinematicsSystem` | `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/LinearKinematicsSystem.cs` |
| `LocalAuthorityYieldSystem` | `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs` |
| `LookAtDispatcherSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/LookAtDispatcherSystem.cs` |
| `MontageQueueAdvanceSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/MontageQueueAdvanceSystem.cs` |
| `NavigationExecutionSystem` | `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs` |
| `NavigationIntentBridgeSystem` | `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs` |
| `NetworkSpawningSystem` | `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs` |
| `NotifyEventEmitterSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/NotifyEventEmitterSystem.cs` |
| `OwnershipIngressSystem` | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/OwnershipIngressSystem.cs` |
| `OwnershipRecomputeSystem` | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/OwnershipRecomputeSystem.cs` |
| `OwnershipTransferInitiationSystem` | `Hrot/Network/Hrot.Network.NED/Systems/OwnershipTransferInitiationSystem.cs` |
| `PartialOwnerReclaimSystem` | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/PartialOwnerReclaimSystem.cs` |
| `PathfindingResultMaterializationSystem` | `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingResultMaterializationSystem.cs` |
| `PathfindingSolverSystem` | `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs` |
| `PersonalRouteAuthoringSystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs` |
| `RaycastSolverSystem` | `FDP/Toolkits/Fdp.Toolkits/Physics/Systems/RaycastSolverSystem.cs` |
| `RouteState` | `Stride/Hrot.Stride.Core/VehicleNavigationIntentSystem.cs` |
| `RouteTrajectorySyncSystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/Routing/RouteTrajectorySyncSystem.cs` |
| `SeatPresetSystem` | `Hrot/Engine/Hrot.Common/EntityCreation/SeatPresets.cs` |
| `SpawnLevelSystem` | `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/SpawnLevelSystem.cs` |
| `StanceTransitionSystem` | `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/StanceTransitionSystem.cs` |
| `StridePhysicsBracket` | `Stride/Hrot.Stride.Core/StridePhysicsBracket.cs` |
| `VehicleCommandSystem` | `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/VehicleCommandSystem.cs` |
| `WeaponTaskDispatcherSystem` | `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/WeaponTaskDispatcherSystem.cs` |
| `WreckSpawnSystem` | `Hrot/Engine/Hrot.Presentation/Effects/EffectSystems.cs` |
<!--/REPLAY-WRITERS-->

⭐ **Kept running on purpose:** Cyclone ingress / egress / cleanup, `SmartEgressSystem`, `GhostCreationSystem` (no-op), `DisposalMonitoringSystem`,
`SpatialHashSystem` and `CoordinateTransformSystem` (derived), `AnimationRuntimeBridgeSystem` (the pose shown), presentation / gizmo / map,
the recorder and playback, diagnostics, `TrafficSteppingModule` (no world access; SUMO stalls without the mirror's input).
⏭ **UNSURE, not gated, to measure in a live replay:** `NetworkGatewaySystem` (acks a replayed `ConstructionOrder`?), `SubEntityCleanupSystem`,
`StaticObstacleBakeSystem`, `TerrainQueryResolutionSystem` (`IgGroundClampingModule`), `ResumeAndDrainSystem`, `BehaviorDiagnosticsModule`,
`GizmoInteractionModule`, `ContextMenuSystem`, `MapLayerAssignmentSystem`.

### 12m. ✅ As built `2026-10-26` — rows 21, 32, 45: ONE time facade on every host; the session layer on the wire

🔒 User: *"One facade."* · *"What the play has to do with UI?"* (the dry run is the cluster's, not the editor's) · *"in the editor, I need the
determinism, because this is used for regression checks of scenarios."* ⛔ SUPERSEDES row 21's "in EVERY implementation" — there is now ONE.

```mermaid
classDiagram
  class ITimeTransportFacade {
    <<exists, grown>>
    Mode, Axis, LivePoint
    SetAxis(), EnterReview(t?), ExitReview(), Seek(t)
  }
  class ClusterTimeTransportAdapter {
    <<exists, grown: THE facade on every host>>
    Play from edit = DryRunIntents.Enter(running)
    Stop: preview to edit, review to live, else idle
  }
  class ClusterReplayControls {
    <<new>>
    the session members, written once
  }
  class ReplaySessionIntents {
    <<new>>
    the one construction: review, seek, axis
  }
  class DryRunIntents {
    <<new>>
    the one construction: enter / exit the dry run
  }
  class EditorPreviewController {
    <<exists, now a cluster client>>
    publishes DryRunIntents, reads the cluster state
  }
  class EditorTimeTransportFacade {
    <<deleted>>
  }
  class ReplaySessionProcessManager {
    <<new, master>>
    builds ReplaySession, republishes on change
  }
  ClusterTimeTransportAdapter ..|> ITimeTransportFacade
  ClusterTimeTransportAdapter --> ClusterReplayControls
  ClusterReplayControls --> ReplaySessionIntents
  ClusterTimeTransportAdapter --> DryRunIntents
  EditorPreviewController --> DryRunIntents
  ReplaySessionProcessManager --> ClusterTimeTransportAdapter : ReplaySessionTopic, LivePoint
```
*What the picture shows that the prose hid:* the editor has no time path of its own any more — its toolbar, status bar, timeline, debug
API and the film director (the regression verifier) all hold the SAME adapter instance, and the HTTP routes and the facade build the SAME
intents. The editor differs only in where its master lives (in-process, on the same orchestration bus — T3).

| rule | as built |
|---|---|
| Play from edit = the cluster's dry run, running | `DryRunIntents.Enter(startPaused: false)` (`TimeMode` "Continuous"); the master resumes it when `OperatingPreview` lands — `GlobalContextProcessManager`, the same "run when it lands" path a live start uses |
| Step from edit = the dry run, paused at its first frame | `DryRunIntents.Enter(startPaused: true)` — unchanged meaning |
| Stop | preview → edit (the master puts the clock back: `PreviewClockProcessManager`, CE-3299 D5 — CE-3156's local put-back is gone) · review → live (R-3060) · live / replay → idle · ⛔ not offered in edit |
| the editor's `IPreviewController` | a cluster client: enter / exit publish `DryRunIntents`; "in preview" = `OperatingPreview`; `ForgetPreview` (CE-1025) still drops the shared handler's capture |
| `POST /sim/step` from edit | waits for the cluster to run the dry run (≤ 20 s, the publishing routes' contract), then ticks |
| the session layer | `ReplaySessionProcessManager` (master) → `ReplaySessionTopic` (Reliable, TransientLocal, KeepLast 1) → `NodeOpSlaveTranslator` / `OrchestrationObserverTranslator` → `ClusterUiCache.ReplaySession` and the facade's `LivePoint`; checkpoint marks ride the prepare round (`ReplayPrepareResult.Checkpoints`, union by label); `ReviewId` minted at a review's entry (F18) |
| determinism (R-3035 / R-3038) | unchanged by construction: stepping was ALREADY a bus intent (`ITimeCommands.StepBy`) on both facades; the dry run reaches the same handler instance (D14). ⭐ Proof: the director's verify on three scenarios before / after — §12m measured below |

📐 **Measured — the regression path unchanged** (`--mode editor --headless --verify ua-civ-melee,ua-tank-ammo,bt-grenade-posture`, each a fresh
process; the before run on the binaries without the change, golden of `ua-civ-melee` captured on them):

| scenario | before | after (one facade, the editor's Play/Stop through the cluster) |
|---|---|---|
| `ua-civ-melee` | 4/4 conditions; golden differs ONLY in `CreationRequestId` (CE-3334: a fresh Guid per run — no golden repeats) | identical |
| `ua-tank-ammo` | "soft-target round at the technical" held at 51.0667 s; kinetic round at 8.9333 s; the same 54 values against the stored golden | identical |
| `bt-grenade-posture` | "the standing man is hurt" FAILS; the same 6 values | identical |

⚠ The first after-run failed every load: an edit had put `_editorLogic?.Update()` inside a comment (the editor's scenario session stopped
reading the cluster state). Caught by this very check, fixed before commit.

