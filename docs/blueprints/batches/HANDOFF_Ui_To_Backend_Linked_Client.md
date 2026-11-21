<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - ../../designs/IG/DESIGN_Linked_Client_Director.md — ⭐ THE design you build (READY-TO-BUILD): §2a module view, §2b classes, §2c the reel sequence, §2d time, §3 items, §4 decisions; §2d-1 / §2e are BUILT (CE-11026)
  - ../Architect_Question_113_Linked_Client_For_The_Director.md — the rulings (R-11009, A–J), incl. the F refinement note
  - ../../DESIGN_Film_Director.md — OWNS IFrameSink, IDirectorSound, the runner and capture (B, E change what a sink is told)
  - ../../DESIGN_Director_Timeline_Editor.md — OWNS the render queue (§11u) a job goes through
  - ../DESIGN_Time_Architecture.md — OWNS the anchors (CE-11019)
  - ../../DESIGN_Cluster_Load_Phase.md — OWNS the load chain (CE-11025)
  - SimNetGateway docs/design/DD-022-Dps-Director-Rendering.md (branch ccr-458b1360-afzl8f) — the wire the gateway maps your types onto (§3e render topics, §3h the frozen fields) — FYI, not yours
-->

# HANDOFF — ui → `backend`: the director renders through the linked client (CE-11018) + four smaller items

**For:** the `backend` lane. **From:** ui (the linked-client programme). **Dispatched at `710b16f47`** (`origin/ui`); ⛔ scope FROZEN there —
later documents are FYI; one that invalidates a task ⇒ STOP that task, do the rest (R-106).

🔒 User, `2026-11-21`: *"Give the best and good fits to the backend line. Please, right handoff."* · the decisions: R-11008, R-11009 (`RULINGS.md`):
*"No external renderer, no Unreal. Otherwise approved."* — the concept is the **linked client**; HROT never knows what is on its other side.

## 0. Start
`git fetch origin ui backend coordinator`; on `backend`, merge `origin/ui` at `710b16f47` (it carries the design, CE-11026's built registry and load
step, and CE-11027 S12-1's `StationId`); restore the test projects you run; push `chore: started linked client at <sha>`. Read
[DESIGN_Linked_Client_Director.md](../../designs/IG/DESIGN_Linked_Client_Director.md) END TO END and [Q113](../Architect_Question_113_Linked_Client_For_The_Director.md)
§0 + §2 (R-129) — ⭐ the F refinement note under Q113's table BINDS: the pose comes from the map's `FilmCaptureLayer` path (`Film.cs:296-320`), and a
2-D shot is refused for a linked client.

## 1. What already exists — extend it, never a second one

| exists (built `2026-11-21`) | where | your use |
|---|---|---|
| `ILinkedClient` with `Name`, `BeginWorld(Guid)`, `IsWorldReady(Guid, out failure, out step)` | `Hrot/Engine/Hrot.Core/Infrastructure/LinkedClients.cs` | ⭐ ADD the frame / job members (§2b) to THIS interface. ⛔ do not change the world members — the gateway's `IClientWorldGate` mirrors them one to one |
| `LinkedClients` registry (`Offer` / `Withdraw` / `Current`) on `HrotNodeContext.LinkedClients` | same file; `HrotNodeContext.cs` | the director finds the client here |
| `LinkedClientLoadStep`, `LoadPart.LinkedClient` in every ECS host's chain | `Hrot/Engine/Hrot.Core/Services/LoadPhase/` | CE-11025 reuses it for a replay open |
| `StationId` | `FDP/Engine/Fdp.Core/World/StationId.cs` | FYI — ⛔ ui is building CE-11027 S12-2 in `Hrot.Gameplay` and the possession code: do not edit those |

## 2. The tasks — in this order (the first is the best fit; the rest are independent)

| # | id | task (the design row) | done when |
|---|---|---|---|
| 1 | [CE-11018](../Blueprint_Issues_Tracker.md) | §3 rows A + B + F: `FilmFrame` / `FilmJob` / `FilmTrack` (Hrot.Core) and `ILinkedClient`'s frame members; `IFrameSink.Begin(job)` / `End` (default no-op); ONE `RoutingFrameSink` built in `DirectorPack.Attach` choosing at `Begin` (`frames: auto \| map`; cards always the map's); the frame carries the film camera (pose from the `FilmCaptureLayer` path, vertical FOV), the film rate and the step; a faulted frame fails the job with its reason; a 2-D shot routed to a linked client is refused with a reason | rails: a fake linked client receives every frame of a reel with the exact pose the map would have used; `auto` with no client = today's map frames, unchanged (the existing director suites green); a card never reaches the client; a fault fails the job; a 2-D shot is refused; one sink for the runner, stills and the render queue (a forwarding rail on the constructed pack — the silent-default rule) |
| 2 | [CE-11019](../Blueprint_Issues_Tracker.md) + [CE-11022](../Blueprint_Issues_Tracker.md) | F-H1: `MasterSyncController.SetTimeScale` while Continuous publishes a Continuous re-anchor (`:196-225`); then F-H2: the time docs describe the anchor model, not the pulse/PLL (`FDP.Toolkit.Time.md`, `time-ctrl-unif` §4.6, `MasterSyncController.cs:153`'s comment, the unused `TimeConfig` PLL fields) | a running scale change keeps every slave on the master's time (⭐ `SimTimeSyncIntegrationTests` — the row-8 integration suite, run in isolation if the full suite is flaky); the docs fold into `DESIGN_Time_Architecture.md` |
| 3 | [CE-11025](../Blueprint_Issues_Tracker.md) | F-H3 (your load phase): a replay open makes the recording's terrain resident through the chain's SHARED parts (Terrain with the recording's name and overlay hash → Map3D → the linked client), the measured gap in the row (`TransitionPlanner.cs:140-157`, `EcsRecordReplayController.cs:387-409`, `LoadPhaseChain.cs:160`) | a start-screen recording of another terrain replays over ITS terrain; the map and the linked client step run; a recording of the resident terrain loads nothing new |
| 4 | [CE-11023](../Blueprint_Issues_Tracker.md) | the editor's director gets the editor's recording opener (`ScenarioDoor.OpenRecording`), reusing `DirectorRunner`'s scenario-door shape; the fixed 30 s wall (`DirectorRunner.LoadTimeoutSeconds`) replaced by "no limit while the load phase makes progress; fail on the load's error state or a stall" — measure the two "to measure" points in the row first | a render job naming another recording plays in the editor; a large terrain past 30 s still loads; a failed open fails the job with the reason |
| 5 | [CE-11024](../Blueprint_Issues_Tracker.md) | `EffectCause` gains `Kind` (entry / exit / stop) and `Shooter` from `RoundImpactNotification` (`:18, :33`) — ⚠ a RECORDED component: append, unmanaged, fixed before its first recording (CLAUDE.md "NEW COMPONENTS") | an impact's effect entity carries both, live and after a replay seek |

Rows stay their ui ids (rule 6: they are yours for this batch); ⭐ any NEW row comes from YOUR block.

## 3. The contract the gateway will map — ⭐ your FIRST commit after the start marker

`NOTE_Backend_To_Ui_Linked_Client_Frames.md`: the exact signatures of `FilmFrame`, `FilmJob`, `FilmTrack` and the new `ILinkedClient` members as you
build them. The gateway (SimNetGateway DD-022 §3e/§3h) maps them onto `RenderJob` / `FrameRequest` / `FrameDone` / `RenderJobResult`, so the frame
must be able to say what `FrameRequest` says: job id, frame index, scene or silence, step, sim time, film rate, position, orientation, vertical FOV,
width, height, supersample, file, subtitle (text, position, alpha), the overlay graphics (ring / arrow / route / area / label, world points), layers
on / off; and a result per frame (written / failed + reason) and per job (the track file and its sample total, which the encode checks within 1024
samples — CE-11021, not yours).

## 4. Gates — every task (CLAUDE.md "THE THREE TEST TIERS", rule 8)
T-1 first (the director suites, the film / capture rails, the render-queue rails, the load-phase chain rails); every new rail red-proved by a compiling
mutation; display rails under `xvfb-run -a` (they skip silently otherwise); the integration suite per cross-cutting change (CE-11019:
`SimTimeSyncIntegrationTests`; CE-11025: the replay-open integration); 0 B per steady frame on any per-frame path; invariant output (R-4221); a
director feature scenario through `scripts/scenario-regression.sh` where one exists (R-4277), else name the scenario you would add.

## 5. The report — `STATUS_Backend_Linked_Client.md`
Per task: built / stopped (and why), the design section folded (obligation ⑤), every lane decision with what would change it, the gate-report
contract (rule 8: verbatim commands, counts, reds confirmed at base with the sha), ids allocated.
