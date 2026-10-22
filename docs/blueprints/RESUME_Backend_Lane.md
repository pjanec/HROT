<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: ⭐⭐⭐⭐⭐⭐⭐⭐⭐ §000000000000 (2026-10-22) — the CHECKPOINT programme (CE-3299): B2 + R-3039 S4/S1/S3/S2 BUILT; B3 BUILT too (2026-10-22, design §1g); B4 BUILT too (design §1h); D1 BUILT (§6b); D7 BUILT (§1j); D17 BUILT (§1i); NEXT = B5 (the R1 oracle — ask the user first); CE-3313 BUILT; open: CE-3311, CE-3312. ⛔ §00000000000 below is DONE — history. ⭐⭐⭐⭐⭐⭐⭐⭐ §00000000000 (2026-10-21) — the user's approvals built: CE-2185 armour, CE-3222 gated green, CE-2326 backend half; CE-3177 is behaviors'. Next: the audit's PARTLY rows. ⛔ §0000000000 below is DONE — history. ⭐⭐⭐⭐⭐⭐⭐ §0000000000 (2026-10-20 overnight) — the overnight list DONE (CE-2321, audit, CE-1206, CE-3200, CE-3158, CE-3288); next: merge, then the audit's PARTLY rows. ⛔ §000000000 below is DONE — history. ⭐⭐⭐⭐⭐⭐ §000000000 (2026-10-20) — Q95 (sound signatures + lossless hearing) BUILT (CE-3282…CE-3285), CE-3279 (EQS timeouts) BUILT, EQS breaker 2 s; NEXT: merge behaviors, then their Scenario Description handoff, then CE-3286 (vehicles stuck in building corners). ⛔ §00000000 below (Q94) is DONE — history. ⭐⭐⭐⭐⭐ §00000000 (2026-10-19 evening) — CE-2304 (interior move stall) DONE, Q94 C DONE (CE-3270), D DONE (CE-3271), B DONE (CE-3272) — Q94 B–D complete; A-5…A-7 DEFERRED (R-3022) (A-1…A-4 BUILT, CE-3269). ⛔ §0000000 below (the platoon programme) is DONE — history. ⭐⭐⭐⭐ §0000000 (2026-10-18/19) — the PLATOON PROGRAMME (behaviors' NOTE_Behaviors_To_Backend_Platoon_Programme.md): BR-13 (CE-3251), BR-12 WorldSurvey (CE-3249), BR-3 SquadReport (CE-3250), LD-1/LD-2 (CE-3252, Look Direction §3.2b), BR-8 (CE-3253), BR-9 (CE-3254), BR-2 (CE-3255), BR-4 custody (CE-3256), BR-5 lean (CE-3257), CE-3258 built; CE-2287 designed as Q94 (waits on the user); BR-6 (CE-3259), BR-7 (CE-3260) built — the queue is done; then CE-3261…CE-3263 (Sectors, near-miss origin, formation slots) and ui's G1–G3 (CE-3264…CE-3266, people display on the wire); BR-6/BR-7 blocked on behaviors' CE-2273. ⛔ §000000 below is history (the armed-people queue it names was finished by another backend session). Earlier: §000000 (2026-10-17) — the queue is batches/HANDOFF_Backend_Continuation_2026_10_17.md: armed people B-7…B-16 (CE-3229…CE-3238) first. §00000 (2026-10-17) — CE-3228 + the gas cloud landed. §0000 (2026-10-16) — a NEW backend session holds the lane: batches/HANDOFF_Backend_Continuation_2026_10_16.md is the queue (CE-3216 weather S1-0 first). ⛔ §000 (2026-10-15) below is history. Earlier: ⭐⭐ §000 (2026-10-15) — a NEW backend session holds the lane: batches/HANDOFF_Backend_Continuation_2026_10_15.md is the queue. ⛔ The older §00 — NOW (2026-10-10): the BEHAVIOUR LIBRARY programme (review R-258/R-256, squad-aware PeekAndFire R-257, CE-3158 ready to build). §0 (2026-10-06 utility demos) and §1 below are HISTORY for this lane's current work.
stale-below: §0 (2026-10-06) and §1 (2026-10-03) — history for this lane's current work
related-designs:
  - docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md — the CURRENT programme (CE-3299): decisions D1–D18 (§5), measurements (§6), rails (§7), build order B0–B5 (§8).
  - docs/DESIGN_System_State_Inventory.md — R-3039: every system field classified (K/A/B/C/S/X), S1 (§6), S2 (§7), S3 (§8) as built.
  - docs/DESIGN_Entity_Control.md — the entity-control design (orders, RTS, FPS possession, manual drive); S1-S3 built (§9-§12).
  - docs/REVIEW_Behaviour_Library_Genericity.md — the CURRENT programme's verdict + decisions (§5) and retreat concepts (§6).
  - docs/DESIGN_Peek_And_Fire.md §10 — the approved generalisation (G1–G8, R-257) and §10.7 CoverClaim; CE-3158 builds it.
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the programme's owning design (push-only ownership, S1–S8, §5.7.1 live matrix, §5.9/§5.10 deferred designs).
  - docs/DESIGN_Subsystem_Composition_Unification.md — B5 (not started) gates CE-3006 / CE-524 / CE-513.
-->
# RESUME — backend lane

⚠ A STATE doc: verify every line against git before acting on it.

## 000000000000. NOW (`2026-10-22`) — the CHECKPOINT programme: R-3039 S1–S4 built, B3 next *(🔒 user: "Yes" to starting B3)*

⭐ **Start here.** `backend` is clean and pushed at `31f690dac` (+ this doc). ⛔ Read `RULINGS.md` first (RULE ZERO).
⚠ **Merge first:** `origin/behaviors` (`421c32cbf`, 53 commits ahead) and `origin/ui` (`88a60d1c8`, 16 ahead) are NOT merged —
then `dotnet restore` the test projects you run (CLAUDE.md, third stale-binary trap). The behaviors merge closes `CE-3306`.

**The programme.** [`CE-3299`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) (restore a checkpoint and continue) —
owning design [**DESIGN_Checkpoint_And_Dry_Run_Restore.md**](https://github.com/pjanec/HROT/blob/backend/docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md): §5 decisions,
§8 build order. Its precondition is `R-3039` (*a system's memory lives in ECS components; a checkpoint is a plain ECS snapshot*),
owned by [**DESIGN_System_State_Inventory.md**](https://github.com/pjanec/HROT/blob/backend/docs/DESIGN_System_State_Inventory.md).

### Done (all on `backend`)

| what | commit | where it is written |
|---|---|---|
| **B2** — the dry run in ONE handler (D14) | earlier, see the design | Checkpoint design §8 |
| **R-3039 S4** — every field a system / module / translator carries between frames is classified (K/A/B/C/S/X); rail `EverySystemStateFieldIsClassifiedTests` (C/X may only shrink — ratchet `PendingMoves` = 7, all SUMO X rows) | `d9e91cbe1` | Inventory §1–§5 |
| **R-3039 S1** — per-entity memory moved onto the entity: squad sense (`SquadMemberSense` 445 / `SquadUnitSense` 446), danger-area edges, the five dispatchers' `DispatchedAction`, `PersonalRouteRef.FollowPending`, animation registration (`ClassOf`), `TrafficShadowFollow` 447 — each with a *restore into a fresh system* rail | `c57376c39` | Inventory §6; [lanes note](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/batches/NOTE_Backend_To_Lanes_R3039_System_Memory_Frames.md) |
| **R-3039 S3** — ONE restore signal: `AfterRestore.Run` → `WorldEpoch.Restored()`; every class-B cache clears on `WorldEpoch.Moved` (8 animation egress translators, mission/route/overlay translators, entity-request systems, `TrafficPanicSystem`); `LifecycleSystem` re-arms the ELM on a RESTORE (not a load); new class **S** (session state a restore leaves); rail `EveryProtocolField_IsReDerivedAtAWorldReplacement` | `f368a55cf` | Inventory §8; lanes note addendum |
| **R-3039 S2 routes** — `RouteTrajectorySyncSystem` re-compiles every restored route under its cached id (`TrajectoryPoolManager.RegisterTrajectoryAt` / `EnsureIdsAbove`) | `f368a55cf` | Inventory §7b |
| **R-3039 S2 solved paths** — `PathTrace` carries `Exact` + `Interpolation` (in the padding); new `TrajectoryRestoreSystem` (first Simulation system of `GroundKinematicsModule`) re-registers each mover's path from its exact trace under its handle, re-plans a thinned one, lifts `NavigationHandleAllocator.EnsureAbove`. 📐 measured: max path 12 points over 4 scenarios, so nearly every path is exact | `086be0890` (measure), `31f690dac` (build) | Inventory §7c / §7c-bis |

📐 Last gates (S2 §7c): Toolkits 4033/0 · SimHost 1857/0 (after the system-count fix) · ClusterRunner 308/0 · Traffic 60/0 · Editor 534/0 ·
`Hrot.Network.NED.Tests` 149/0. ⚠ `Hrot.NED.Tests` and `Hrot.Animation.Integration.Tests` were never restored in this container — not run.

### ⏭ Next — in the user's accepted order

| # | item | ⭐ the starting facts |
|---|---|---|
| **1** | ✅ **B3 BUILT `2026-10-22`** ([design §1g](https://github.com/pjanec/HROT/blob/backend/docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md)) — and B2 closed on a cluster (CE-3306 merged). ⛔ the cell below is the PLAN it was built from. **B3** — checkpoints as labelled recorder keyframes (`R-3044`) | Design §8 B3 + D3 (the master names wall time T ahead on the shared zero; each node's recorder writes keyframe X at its first frame with `TotalWallTicks ≥ T`) + D2 (the keyframe carries the UNCONSUMED step window, as `ReferenceCheckpointHandler` flushes it today). Build: `RecorderTickSystem` takes a *keyframe at T, label X* request → label → frame index in the recording's `.meta.json`; `CheckpointIOWorker`'s `'FDPC'` capture retires; `CollectCheckpoint` exports keyframe X as a one-frame recording; measure **M5** (a forced keyframe's cost on its frame). ⛔ Not started — no code written. Inventory first: `RecorderTickSystem`, `ReferenceCheckpointHandler`, `CheckpointIOWorker`, the master's `TakeCheckpoint` / `CollectCheckpoint` |
| 2 | ✅ **B4 BUILT `2026-10-22`** ([design §1h](https://github.com/pjanec/HROT/blob/backend/docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md)) — a two-phase restore round (stage, then restore), the replay branch and the restore both record a master-minted exercise whose first frames are the parent's (CE-3300 closed). ⛔ the cell below is the PLAN. **B4** — `RestoreCheckpoint X` = pause → every node seeks to keyframe X → `AfterRestore` → a new recording segment (D8) → live paused; + **D18** (`R-3047`, `CE-3300`): the master mints the branch exercise id, each node copies its history up to T, `FileMode.CreateNew` | clock = `ITimeCommands.SnapTo` (D5) |
| 3 | ✅ **D1 BUILT `2026-10-22`** (the nine SIM-STATE types recorded, design §6b) · ✅ **D7 BUILT** (every registrable type declares its policy, §1j) · ✅ **D17 BUILT** (the checkpointing mode, §1i) · ⏳ B5. ⛔ the cell below is the PLAN. **D1 / D7 / D17 + B5** — the 9 SIM-STATE `NoReplay` types made recordable (§6b); singletons honour `DataPolicy` + a rail; the recorder's checkpointing mode (`R-3045`); the R1 oracle live | R1 is red until every violator is fixed — each red names the next one |

### ⏳ Open, not on the critical path

| id | what | lean / state |
|---|---|---|
| SUMO X rows (7) | traffic's own memory at a restore | ⏳ **waits on the user** — lean: a restore restarts traffic (Inventory §2c) |
| [`CE-3312`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | a replay branch never snaps the cluster clock or unfreezes time on a real cluster (the master drops `LiveBranchResult`; nothing aggregates `PrepareLive`) | lean: a decode case + a `PrepareLive` aggregator, as B4 did for the restore |
| [`CE-3311`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | a test asserts the IG role gate R-4011 retired | lean: invert + Roslyn rename |
| [`CE-3309`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | a managed component replays as one 16 384-entity chunk, allocated anew every frame | engine item; filed, not scheduled |
| [`CE-3307`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | the Stride assemblies are walked by neither state rail | small |
| [`CE-3308`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | `Networked_Locomotion_BrainSeesFootstepEvents` red — pre-existing at `d9e91cbe1` | open |
| [`CE-3306`](https://github.com/pjanec/HROT/blob/backend/docs/blueprints/Blueprint_Issues_Tracker.md) | `TakeCoverScenarioTests` red — fixed on `behaviors` | close after the merge |

📌 Ids: backend tracker next free **`CE-3310`**, rulings next free **`R-3049`**, component ids **448–449** free in backend's block
(450–459 is traffic's).

## 00000000000. NOW (`2026-10-21`) — the user's approvals, built *(🔒 user: "Those 3 approved. 3177 approved")*

⭐ **Start here.** `backend` is clean and pushed; ui merged at `cac7dda48`, behaviors had not moved.

| what | where it is written |
|---|---|
| **`CE-2185`** aircraft armour faces — UH-60 8/6/4/4, A-10 25/25/10/10, AH-64 20/15/8/8 (R-3028) | [Air Ops §7c-i](../designs/air-attack/DESIGN_Air_Operations.md) |
| **`CE-3222`** the civilian ceiling ≤ 100 ms per node at 300 (R-3029): `perf-civilians.py --gate` **PASS** (CGF 51.2 · SimHost 40.3 · IG 12.8) — closed | [Civilians §7.4a](../DESIGN_Civilians_And_Crowd_Control.md) |
| **`CE-2326`** backend half (R-3030): `TargetMemory.SeenStance`, `BodyStance` on the fire request, the muscle's `AimPoint.ForBodyAt`; backend's own AQ89 A blind round uses it. ⏭ behaviors switches `PeekAndFireNodes` | [Peek and Fire §11.6a/b](../DESIGN_Peek_And_Fire.md); [note](batches/NOTE_Backend_To_Behaviors_Blind_Burst_Body.md) |
| **`CE-3177`** approved (R-3031) — behaviors' posture tree | [AQ89](Architect_Question_89_Suppress_And_Close.md); [note](batches/NOTE_Backend_To_Behaviors_Approvals_2026_10_21.md) |

| **`CE-3289`** FILMING the 3-D map — a director camera + a scene-only capture (R-3032); ⭐ `--mode editor` preferred (frame-exact stepped cuts, ~0.8 s/frame) | [**RUNBOOK_Filming.md**](../RUNBOOK_Filming.md); design [Map 3D Mode §6ab](../DESIGN_Map_3D_Mode.md) |
| behaviors' clearing notes: A/C reviewed + kept; **`CE-2335`** measured — one writer, no split, the re-send lean withdrawn | [note](batches/NOTE_Backend_To_Behaviors_Clearing_Notes_Taken.md); [Building Interiors §3j](../DESIGN_Building_Interiors.md) |

⏭ **Next:** the audit's PARTLY rows (§0000000000's table, item 2). ⏳ Waiting on the user: watching the editor film (CE-3289 stays open until then).

## 0000000000. NOW (`2026-10-20`, overnight) — the user's overnight list is DONE *(🔒 user: "yes fix CE-2321 the same way, and audit the stale rows. Jets ok, sumo not heard ok, do 1206, 3200 (if still needed, after fixing the vehicle turning into the walls), 3158, do that autonomously overnight")*

⭐ **Start here.** `backend` is clean and pushed. ⛔ Read `RULINGS.md` first (RULE ZERO). Merged up to: behaviors `8178efe67`, ui at the
`2026-10-20` evening tip (both have moved since — merge first).

### Done (all on `backend`)

| what | commit | where it is written |
|---|---|---|
| **`CE-3286`** a hull never enters a building, backs out of a wall (R-3025) — live in both shoot-and-scoot demos | `18bd6ed48`…`3fc536e33` | [Motion Domains §5f](../designs/motion-models/DESIGN_Motion_Domains.md) |
| **`CE-3287`** a wreck never moves, a parked vehicle is never pushed (R-3026) | `b526772d9` | §5g |
| **`CE-3281`** the built-in catalogue is heard; jets as `Rotor` and silent SUMO cars **confirmed by the user** | `f5150575f`, `86dc14dc6` | [Sensing §8.6](../DESIGN_Thermal_And_Acoustic_Sensing.md) |
| **`CE-2321`** a man down (dead / incapacitated) is never pushed nor turned (R-3027); 4/4 mutants caught | `ed1195df1` | Motion Domains §5h |
| **`CE-3200`** the scale-out DDS path request carries `TurnRadius` | `86dc14dc6` | [Navigation v2 §5.2b](../designs/navig-2/Navigation_Design_v2_0.md) |
| **stale-row audit** — 24 backend rows closed, 18 statuses corrected (each closure cites its commits) | `c16498fef` | the tracker |
| **`CE-3158`** was already BUILT (G-1…G-8, `2026-10-10`) — closed by the audit | — | Peek and Fire §10.9 |
| **`CE-1206`** NOT reproduced live: a possessed ZU-23 technical fires on a press (100 → 96). Closed, reopen with the HUD state | `33181a965` | tracker row; [runbook §6.1](../RUNBOOK_Cluster_Debugging_Over_Http.md) (the 3-D station overwrites low-`seq` HTTP frames) |
| **`CE-3288`** the scenario DESCRIPTION survives load → save → merge (behaviors' handoff) | `5d4041258` | [Distributed Scenario Persistence §4d](../DESIGN_Distributed_Scenario_Persistence.md); note to behaviors |

### ⏭ Next

| # | item | ⚠ |
|---|---|---|
| 1 | merge `behaviors` and `ui` | ⚠ restore the test projects after the merge (CLAUDE.md, third stale-binary trap) |
| 2 | the audit's PARTLY rows, by value: `CE-3105` (replan when the navmesh geometry changes) · `CE-3009` (6 animation translators still gate on entity authority) · `CE-3081` (ChokePoint / CrestLine danger areas) · `CE-3223` (Molotov) · `CE-3109` I-3 (the interaction executor base) | each row carries its audit note |

### ⏳ Waiting on the user

| | lean |
|---|---|
| `CE-2185` the A-10 / AH-64 / UH-60 armour faces | — numbers to be given |
| `CE-3222` the civilian performance budget (ms per frame at 500) | rule a budget; the harness is built |

## 000000000. NOW (`2026-10-20`) — Q95 and CE-3279 done; next: the scenario description, then the stuck vehicles *(🔒 user: "write resumption doc so i can continue in other session")*

⭐ **Start here.** `backend` is clean and pushed. ⚠ `behaviors` has moved since this lane merged it (`912d35866` merged up to `96e18de8e`;
`origin/behaviors` was at `f36b5b2cb`) — **merge it first** (it carries the next handoff). ⛔ Read `RULINGS.md` first (RULE ZERO).

### Done this session (all on `backend`)

| what | commit | where it is written |
|---|---|---|
| **S1 / `CE-3282`** — the lossless sound ring: every shot / burst in `SoundEmissionRing` (4096 entries, 30 s, a managed singleton BORROWED by the snapshot, read by a per-slot seqlock); each hearing sensor reads everything after its own `AcousticCursor` | `279220eb3` | [Sensing §8.3](../DESIGN_Thermal_And_Acoustic_Sensing.md) |
| **S3 / `CE-3283`** — a heard sound's family (`Flags` bits 13–15) and TKB type (a ring token in `EqsResult.SignatureToken`, the struct stays 32 B), by the ear's `ProficiencyOf.Tier` (below Regular the class, Regular + family, Veteran + type); on `SoundContactEvent` and `AudioTargetDetected` | `2abbb0c89` | Sensing §8.4 |
| **S4 / `CE-3284`** — real carries: 60 mm 4 km · 81 mm 5 km (and the urban `MortarTeam`) · 120 mm 7 km · 105 mm 12 km · M109 18 km | `2abbb0c89` | Sensing §8.5 (sources) |
| **S6 / `CE-3285`** — `MovingBand` / `FiringBand` / `DetonationBand` authored; nothing reads them (WQ-E Q3) | `2abbb0c89` | Sensing §8.5 |
| **S5** — the distance rule in Sensing §8.2 and Terrain World; ruling `R-3024` (Q95 approved) | `2abbb0c89` | — |
| **S2 / `CE-3279`** — the EQS timeouts, measured live then fixed (user: *"Yes a b and c"*): (a) the Bepu terrain index attached in `TerrainResidency` and asked by the point queries; (b) one navmesh search per sensor (`INavmeshProvider.PathCosts`); (c) path and exposure COUNT their work (`IEqsCountedWork`). A solve 1.8–2.4 s → ~320 ms | `6cab61d7b` `11e6ce9b5` `e0cf95cf1` | [Sensors §5.3 + O5](../DESIGN_Sensors_And_Doctrine.md), [World Query Seam §5a](../DESIGN_World_Query_Seam.md) |
| the EQS breaker **400 ms → 2 s** (`EqsModule.MaxRunMs`, user: *"make it 2 s"*): a timed-out run is not stopped (a zombie blocks the next, CE-3066), so the cap only threw away late answers and opened the circuit | this commit | Sensors O5 |
| **`CE-3286`** recorded (user report): a tank / technical peeking round a building corner drives into it and stays — NOT measured | `5f5081f78` | the tracker row |
| notes to behaviors: [signatures built](batches/NOTE_Backend_To_Behaviors_Sound_Signatures_Built.md) (their readers — E1 — are theirs now) · [EQS timeouts fixed](batches/NOTE_Backend_To_Behaviors_Eqs_Timeouts_Fixed.md) (path costs can shift slightly; their golden red) | | |

### ⏭ The queue, in order

| # | item | how to start | ⚠ watch |
|---|---|---|---|
| **1** | **merge `behaviors`** | `git fetch origin behaviors && git merge origin/behaviors` | — |
| **2** | ⭐ **the scenario DESCRIPTION** — behaviors' FRAME [`HANDOFF_Behaviors_To_Backend_Scenario_Description.md`](batches/HANDOFF_Behaviors_To_Backend_Scenario_Description.md) (on `behaviors` until merged): every scenario header carries `description` ("What: … · Judge: …") that survives editor load → save → cluster merge. Backend designs the details first (UML into [Distributed Scenario Persistence §4](../DESIGN_Distributed_Scenario_Persistence.md)), then builds | the traps the handoff measured: both save sites build a FRESH header (`HrotScenarioSaveHandler.cs:98`, `ScenarioFileService.cs:104`) and the merge rebuilds it (`ScenarioMergeCore.cs:160-162`); the terrain name survives only because the loader stamps it resident (`TerrainResidency.cs:300`) — the description needs the same, NOT tied to the terrain (a scenario may have none); leans D1–D6 in the handoff; the 88 texts are behaviors' (D5) |
| **3** | **`CE-3286`** — vehicles stuck in a building corner (shoot-and-scoot) | measure first, on behaviors' shoot-and-scoot scenarios (`ua-technical-scoot`, `ua-tank-corner` family — find them under `scenarios/`): is it the route (navmesh clearance for a hull) or the chosen hide / firing point (`FindVehicleHide` / `FindVehicleFiringPoint` too near a wall)? Lead: `CE-2198` ([Motion Domains §5b](../designs/motion-models/DESIGN_Motion_Domains.md)) stops the step into a wall and nothing backs off | 🔒 user's order: ① never drive into the building (preferred) ② reverse a little and re-plan (V5 reverse exists, [Exposure Cycle §5](../designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md)) |

### ⏳ Waiting on the user

| | lean |
|---|---|
| `CE-3281` — the built-in military catalogue (M1, T-72, Bradley, HMMWV, aircraft, persons 200–211) is SILENT to AI ears | yes: author `SignaturesDto` on them (data, like the urban catalogue) |
| the EQS budget: 150 000 units ≈ 450 ms on this debug VM (~3 µs a unit) | leave it — deterministic counts, a release build is several times faster; lower it only if timeouts show on a release machine |

### ⚠ Findings for others (not backend's to fix)

| | |
|---|---|
| `EqsFlatTerrainGoldenTests.RegisteredTemplates_AllHaveAGoldenScenario` red — six window/roof templates from behaviors' `CE-2275` have no golden scenario | told behaviors |
| `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` — intermittent in the full SimHost suite, green alone | known |

### ⭐ Tooling that cost time this session

| ⚠ | |
|---|---|
| **the codebase-memory graph is NOT indexed** in a fresh cloud session | `/opt/codebase-memory-mcp/codebase-memory-mcp cli index_repository '{"repo_path":"/home/user/HROT"}'` |
| **Roslyn over stdio: `roslyn_preview_rename` works, `roslyn_apply_rename` returns a bare "An error occurred"** (with `token` + `approval` + `projectPath`) | read the preview's diff and apply exactly that diff; grep the old name afterwards. Script: the stdio client pattern in `CLAUDE.md` §②a |
| **`pkill -f <pattern>` inside a Bash call kills that call's own shell** when the pattern is in its command line (exit 144) | kill by pid |
| **a live EQS profile** — temporary Stopwatch timers in `EqsModule.Tick` / `EqsSolverSystem` (per template) and the expensive tests, run `uc-request-chain` through `scripts/utility-demo-check.py`'s `launch` / `load` / `play`, grep `[EQS-PROF]` in `/tmp/utility-demo-cluster-8111.log`; revert with `git checkout` before committing | the probe REFUSES a runner older than a source file — rebuild `Hrot/Runner/Hrot.ClusterRunner` first; a stale-runner refusal leaves the OLD log in place (do not read its counts) |
| **`--no-build` after a FAILED build runs the stale binary green** | check `Build succeeded` before reading any test count |
| **IDL: TKB enum members share ONE scope** in the generated DDS IDL — a new enum may not repeat `Unknown`, `SmallArms`, … from another | the codegen error names the collision |

## 00000000. NOW (`2026-10-19`, evening) — finish Q94 (R-3018): A-5…A-7, then C, D, B *(🔒 user: "pls merge behaviors and write resumtion document so i can continue in another session")*

⭐ **Start here.** `backend` is clean and pushed; behaviors is merged up to `73812700a` (CE-2275 D-2e). ⛔ Read `RULINGS.md` first
(RULE ZERO), then **[Architect Question 94](Architect_Question_94_Brain_Geometry_Sites.md) §0a (the ruling), §4 (A as RULED), §4.1
(A as BUILT), §5 (C and D carriers)** — that one document is the whole programme.

**What R-3018 rules** (🔒 user: *"Approved. Move fire check to muscle. Anyway brain should be sending just fire requeat and can update ammo
based on fire notification (and we would need to add refused flag to the notification)."*) — every geometry gate of a shot runs on the
MUSCLE; the brain only asks; the muscle fires or REFUSES; the brain's ammo follows the answers.

### Done this session (all on `backend`, all red-proofed)

| what | commit | ⭐ |
|---|---|---|
| A-1 the carrier — `RequestId` on intent / request, `RequestId` + `Refusal` + `Fired` on the notification, both DDS topics appended | `2c8c18370` | size rails 70 / 81 B |
| A-2 the muscle answers every request (`CannotFire`), echoes the id | `0d83a5ff3` | |
| A-3/A-4 `Combat/ShotLedger.cs` — ask reserves, the fired answer spends, a refusal holds 1 s with its reason, 2 s timeout; the muscle refuses a MASKED direct round (unit or point); the brain's two `MuzzleMasked` calls gone | `05a12d58f` | tracker [`CE-3269`](Blueprint_Issues_Tracker.md) (Q94 A); design [Q94 §4.1](Architect_Question_94_Brain_Geometry_Sites.md) + [BS-1 §5.4c](../designs/brain-split/BS-1-DESIGN.md) note |
| behaviors told the one timing change (`Masked` arrives a frame or two after the ask) | `cacf88953` | [note](batches/NOTE_Backend_To_Behaviors_Fire_Refusal.md) |
| earlier today: S2 weather in perception (R-3017, `CE-3216`), FireTeam byte (R-3019), `CE-3220` part 2, sectors / Hold-stay rulings recorded (R-3020 = R-2054, R-3021 = R-2055) | — | |

### ⏭ The queue, in order

⭐ **REORDERED `2026-10-19` (R-3022):** 🔒 user *"yes, postpone them and start CE-2304 and the points C,B,D, leaving A-5…A-7 deferred until destruction is designed or until suitable opportunity comes"*. ⇒ the order is now **CE-2304** (behaviors' [interior move stall note](batches/NOTE_Behaviors_To_Backend_Interior_Move_Stall.md)) → **C** → **D** → **B** (design first). Rows 1–4 below (A-5…A-7) are ⏸ DEFERRED — kept for when they resume ([Q94 §4.1](Architect_Question_94_Brain_Geometry_Sites.md) names the trigger).

| # | item | how to start | ⚠ watch |
|---|---|---|---|
| **1** | **A-5 — the indirect solve moves to the muscle** | `FireAtPointExecutor.cs` ~:112–148 calls `FireSolution.For(WorldQuery.Of(world)…)` on the BRAIN and puts `Charge` / `HighArc` on the intent. Ruled: the brain sends the point + its ARC PREFERENCE and `Charge = 0` ("solve there"); `FireProcessingSystem` runs `FireSolution.For` with the live terrain and refuses `TooClose` / `Masked` / `TooFar`. ⇒ the intent needs the arc preference (append a byte, or reuse `HighArc` as "prefer high" when `Charge == 0` — decide, size rail). The laying (`Lay`, turret slew) still needs an arc BEFORE the shot: lay with the terrain-free solution (`FireSolution.For(null, …)`), let the muscle's solve decide the charge. The executor's `!solution.Ok ⇒ Failure` becomes "the refusal ⇒ Failure" (read `ShotLedger.Held` → `HoldReasonOf`; an indirect refusal FAILS the action, not holds it — §4 A-5) | tests that move (`AimAndFireExecutorTests`): `IF1_*` ×2 and `P2_ASelfPropelledGun` assert the brain's charge; re-check the `M4_*` set §4 lists |
| **2** | **A-6 — the bomb release moves to the muscle** | brain sites: `AimAndFireExecutor.ReleaseCue` / `ReleaseAt` (~:632–665, `GroundHeightAt`) and `FireAtPointExecutor.cs` ~:162. Ruled: the brain asks when ITS terrain-free cue (the replicated height) says "about now"; the muscle releases at its true cue (`BombBallistics.ReleaseNow`, live ground) or refuses `Expired` after 3 s. ⇒ the muscle must HOLD the request across frames (a small pending-release list in `FireProcessingSystem`, keyed by `RequestId`) — the one new piece of state | `CE2173_ABombAtAPoint_WaitsForTheReleaseCue`, `CE2180_AnIntentReadNFramesLate_*`, `A6_TheCue_SaysNow_*`, `A6_TheReleaseProfile_*`, `AirAttackNodesTests.B6_ALevelBombRun_HitsThePoint` (asserts the release point within ±1 frame) |
| **3** | **A-7 — the battery plan stays terrain-free** | `Brains/IndirectFireBehaviors.cs` `SolutionFor` (~:82–90, plan ~:261, sheaf ~:307) ⇒ `FireSolution.For(null, …)`; a masked gun learns it from its refusal | behaviors' file — say it in the commit (cross-lane) |
| **4** | fold A-5…A-7 into [Q94 §4.1](Architect_Question_94_Brain_Geometry_Sites.md), tick [`CE-3269`](Blueprint_Issues_Tracker.md) | — | |
| **5** | ✅ **BUILT `2026-10-19` as [`CE-3270`](Blueprint_Issues_Tracker.md) — no new mode, `RouteEntityId` under `PathToPoint` names the door ([Q94 §5.1a](Architect_Question_94_Brain_Geometry_Sites.md)).** ⛔ the lean below is SUPERSEDED. **C — the door approach** ([Q94 §5.1](Architect_Question_94_Brain_Geometry_Sites.md)) | C-1 `NavigationMode.ApproachDoor` (appended) + the door's network id in `NavigationIntent.RouteEntityId` (already on the wire, `SimDescriptors.cs:745`); C-2 `NavigationDestination.Of` resolves it with `DoorNodes.ApproachPoint` moved there | allocate a CE id from the 3xxx block (next free: see the tracker's id-block table) |
| **6** | ✅ **BUILT `2026-10-19` as [`CE-3271`](Blueprint_Issues_Tracker.md) — one path: the request stays (`Resolved`), resolved where `SimTransform` authority is held ([Q94 §5.2a](Architect_Question_94_Brain_Geometry_Sites.md)).** **D — a spawn's ground level** ([Q94 §5.2](Architect_Question_94_Brain_Geometry_Sites.md)) | D-1 a one-shot `SpawnLevelRequest { Mode, Level }` in the Muscle ownership group, a `SpawnLevelSystem` on the muscle; D-2 the IG's remote placement carries the height (a found defect) | component id from backend's block **436–449** (434/435 used; R-2056) |
| **7** | ✅ **BUILT `2026-10-19` as [`CE-3272`](Blueprint_Issues_Tracker.md) — the solver marks `SeenFrom` per area from `EqsContextPoints` carried on `EqsSensorConfig`; no epoch bump ([Q94 §5.3](Architect_Question_94_Brain_Geometry_Sites.md)).** ⛔ the row's WorldSurvey idea was not used (a standing ~1 s answer with no per-contact shape). **B — the danger-area contact sight** ([Q94 §2 B](Architect_Question_94_Brain_Geometry_Sites.md)) | not designed beyond the lean (the `WorldSurvey` sensor fits it, [DESIGN_World_Survey_Sensor](../DESIGN_World_Survey_Sensor.md)) | design first (UML in Q94) |

⏳ **Waiting on others:** `CE-3222` (ms budget — another session's) · `LD-4` (waits on behaviors' LD-3 rest + LD-5).

### ⭐ Tooling that cost time this session — read before building

| ⚠ | |
|---|---|
| **gate scripts: `set -o pipefail`** and test only after `BUILD-OK` of the **TEST** project | a `dotnet build … \| grep` swallowed a failed build and `--no-build` ran the stale binary green |
| **a brain-only test world has no muscle** | after A-3 an executor test must ANSWER its asks: `TestMuscle.FireAll(world)` (`Fdp.Toolkits.Tests/Combat/TestMuscle.cs`) or a harness `AnswerFired()` (`AirAttackNodesTests`). A burst that stalls in a test = the reservation cap (`ShotLedger.MaxPending` 8 / `Ammo − pending`) with nobody answering |
| **mutants must COMPILE** | `if (true)` / `if (false)` fail the build (warnings are errors — unreachable code) ⇒ use a non-constant form (`x == None \|\| x != None`, `* 1000f`). A `BUILD-FAIL` mutant proves nothing |
| **don't edit source while a mutant run or a build is going** | stage edits in the scratchpad and copy them in after |
| **known reds** | Editor `NoNewInlineNetworkIdLookupAppears` · SimHost `BranchedRecording_CapturesHistoricalStateAsKeyframe` (flaky) · Toolkits `Partition_ZeroAllocs` (flaky). Stride tests cannot run on Linux (build only) |
| **twins for anything that fires** | `ClusterRunner.Integration.Tests` filter `CE3172_OperatorFire\|CE3136_P8_WindowDuel\|CE3088_U6\|CE2106_ARifleman\|CE2073_AnArmedRifleman\|CE1078_APress` — 6/6 in ~1.5 min |
| **codebase-memory** | the MCP server does not connect here; use the CLI (`/opt/codebase-memory-mcp/codebase-memory-mcp cli search_graph …`, project `home-user-HROT`); `check_index_coverage` is NOT in the CLI — say so in any exhaustive claim |

## 0000000. NOW (`2026-10-18`) — the platoon programme *(🔒 user: "Merge behaviours branch, find the new handoff for you and start working on it autonomously overnight")*

⭐ **The queue is behaviors' [`NOTE_Behaviors_To_Backend_Platoon_Programme.md`](batches/NOTE_Behaviors_To_Backend_Platoon_Programme.md) §1**
(order: BR-12 → BR-3 → BR-13 → LD-1/LD-2 → BR-8 → BR-6/BR-7 → BR-9 → BR-2 / BR-4 / BR-5 → CE-2287). Built today:
[`CE-3251`](Blueprint_Issues_Tracker.md) BR-13 (ROE to the brain's node + the replica's ROE follows — Entity Control §2.3a) ·
[`CE-3249`](Blueprint_Issues_Tracker.md) BR-12 WorldSurvey ([design + as-built](../DESIGN_World_Survey_Sensor.md)) ·
[`CE-3250`](Blueprint_Issues_Tracker.md) BR-3 SquadReport (Building Clearing §5d "BR-3 as built"). Answer to behaviors:
[`NOTE_Backend_To_Behaviors_Platoon_Programme_Built.md`](batches/NOTE_Backend_To_Behaviors_Platoon_Programme_Built.md).
⭐ **Built `2026-10-19`: [`CE-3252`](Blueprint_Issues_Tracker.md) LD-1/LD-2** — as-built
[Look Direction §3.2b](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) (G1–G6 closed + G7 found: the look-at egress sent a raw
entity index). ⭐ **At the behaviors merges:** BR-3 rebased onto their `CommandReport` (`SquadReportNotification`, one vocabulary); the
survey ids moved to 410–412 (they took 407–409); `SetReportingEvent` forwarding was built twice in parallel (mine at the merge, their `CE-2299`) — behaviors kept MINE at their merge `78e4bd546` and dropped theirs except the cluster rail `EntityOrdersAcrossNodesTests.CE2299`.
⭐ **Built `2026-10-19`, later:** [`CE-3253`](Blueprint_Issues_Tracker.md) BR-8 (LMGs + roles on the wire) ·
[`CE-3254`](Blueprint_Issues_Tracker.md) BR-9 (sniper, DMR, the scope) · [`CE-3255`](Blueprint_Issues_Tracker.md) BR-2 (stun holds
every class) · [`CE-3256`](Blueprint_Issues_Tracker.md) BR-4 (custody: ONE `Custody` interaction kind with a verb — [Entity Interactions
§7.6](../DESIGN_Entity_Interactions.md)). ⛔ BR-6/BR-7 wait on behaviors' `CE-2273` (`ProficiencyOf`, not on `behaviors` yet).
[`CE-3257`](Blueprint_Issues_Tracker.md) BR-5 (the lean — [Hit Location §13](../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md);
NEW `TerrainWorldLosStrategy.EyeOf`, the one eye position). ⭐ **CE-2287** (backend's brain-side geometry, R-2037): designed as [Architect Question 94](Architect_Question_94_Brain_Geometry_Sites.md)
— A fire gates into the muscle's fire pipeline · B danger-area contact sight into its muscle sensor · C door approach · D spawn level ·
F the CGF-owned doors / clouds premise; ⏳ **waiting on the user**. E built as [`CE-3258`](Blueprint_Issues_Tracker.md) (+ a cloud
never drifted under a scenario wind — fixed). ⭐ **BR-6 [`CE-3259`](Blueprint_Issues_Tracker.md)** (proficiency in the spread +
`CombatProficiency` replicated, `dtCombatProficiency` 150) and **BR-7 [`CE-3260`](Blueprint_Issues_Tracker.md)** (acquisition time by
exposure — [Sensors §5.7](../DESIGN_Sensors_And_Doctrine.md)) built once behaviors' `CE-2273` landed. ⚠ Component ids collided three times with
behaviors (CONVERGED after a ping-pong: Lean **416**, EngagementArea 417 — both lanes) — [NOTE](batches/NOTE_Backend_To_All_Lanes_Component_Id_Space.md) proposes per-lane component-id
blocks (the mask is 512 bits; ~85 ids left), ⏳ the user decides. ⭐ **The platoon queue is DONE** except Q94's A–D/F. ⭐ Since: [`CE-3261`](Blueprint_Issues_Tracker.md) (Sectors ①② — kind 4 `SetEngagementArea` forwarding + `EngagementArea` replicated, dt 151) · [`CE-3262`](Blueprint_Issues_Tracker.md) (near-miss carries the shooter's origin) · [`CE-3263`](Blueprint_Issues_Tracker.md) (formation slots updated in place; succession re-publishes) · ⭐ ui's people display G1–G3: [`CE-3264`](Blueprint_Issues_Tracker.md) `Utterance` (434, dt 152) · [`CE-3265`](Blueprint_Issues_Tracker.md) `Demeanour` (435, dt 153) · [`CE-3266`](Blueprint_Issues_Tracker.md) `StrikeNotification` + `MeleeStrike` (dt 154) — [Civilians §7.18](../DESIGN_Civilians_And_Crowd_Control.md); ⭐ [`CE-3246`](Blueprint_Issues_Tracker.md) — a hit reaches the `Health` owner from ANY node (the `EntityHitDamage` pair in the shared pack, `SourceNodeId` appended — [BS-1 §6.4a](../designs/brain-split/BS-1-DESIGN.md)). ⭐ [`CE-3248`](Blueprint_Issues_Tracker.md) — a hand throw is no gunshot (`PersonHands.Threw`, five readers). ⭐ [`CE-3267`](Blueprint_Issues_Tracker.md) — a module snapshot carries the frame's clock (BR-7's dwell never passed beyond 100 m on SimHost; found live by behaviors). ⭐ [`CE-3268`](Blueprint_Issues_Tracker.md) — `EngagementArea` registered on every node (SimHost crashed on the first sector order). ✅ Verified LIVE: behaviors' `ld-sentry-surprise` with A at 113 m passes every step. ⭐ [`CE-3247`](Blueprint_Issues_Tracker.md) — thrown blades (hatchet / spear / star, 6044–6046; shapes Axe/Spear/Star — ui told they draw nothing yet). ⭐ [`CE-2244`](Blueprint_Issues_Tracker.md) — no sight while this node's terrain is still loading (`TerrainWorldSource.MarkPending` from the load step's `BeforePrepare` to commit/abort; the LOS strategy judges nothing visible — [Cluster Load Phase](../DESIGN_Cluster_Load_Phase.md)). ⭐ [`CE-3220`](Blueprint_Issues_Tracker.md) part 2 — 1001/1003 retired, 5 shipped scenarios migrated (neutral civilians ⇒ 1101 + `Idle`; unarmed hostiles/decoy ⇒ new 2008 UnarmedFighter, since AW-21 makes a civilian of any side no target) (behaviors told: [note](batches/NOTE_Backend_To_Behaviors_Civilian_Types_Retired.md); [Civilians §7.2b](../DESIGN_Civilians_And_Crowd_Control.md)). ⭐ [`CE-3216`](Blueprint_Issues_Tracker.md) S2 (weather + night in perception) BUILT (R-3017, [Weather §8.5](../designs/weather/DESIGN_Weather_And_Time.md)). ⭐ R-3019 the FireTeam byte BUILT. ⭐ R-3018 Q94 A-1…A-4 BUILT as [`CE-3269`](Blueprint_Issues_Tracker.md) (the muscle refuses a masked / unfireable round; the brain's `ShotLedger` spends only what the muscle says flew — [Q94 §4.1](Architect_Question_94_Brain_Geometry_Sites.md)); ⏳ A-5 indirect, A-6 bomb, A-7 battery plan next. ⏳ R-3018 (Q94 C–D): C needs a carrier (the channel params and the intent flags are full), D needs the level to reach the muscle; B not started. ⏳ Open backend rows: CE-3222 (other session's), Q94 A–D/F and Sectors ④ wait on the user; LD-4 waits on behaviors' LD-3 rest + LD-5. ⏳ Sectors ④ (the fire-team byte) waits on the user. ⚠ The other armed-people
backend session finished CE-3229…CE-3248 on 2026-10-17/18; its open rows were CE-3246, CE-3247, CE-3248, CE-2244 — all FIXED 2026-10-19; CE-3222 remains.

## 000000. NOW (`2026-10-17`, later) — the queue is [`batches/HANDOFF_Backend_Continuation_2026_10_17.md`](batches/HANDOFF_Backend_Continuation_2026_10_17.md) *(🔒 user: "write a handoff for your future self to continue in what you are doing as well as the new armed civilian stuff")*

⭐ **Next:** behaviors' ARMED PEOPLE (Civilians §5h, R-2025…R-2029) — B-7 `Armament` first (status for combatants, then per-observer
identification; the counter rail), then B-8, B-13, B-10, B-9, B-11, B-12, B-14, B-15, B-16 — filed as **CE-3229…CE-3238**. Then CE-3227,
CE-3222. §00000 below is what landed earlier today.

## 00000. NOW (`2026-10-17`) — CE-3228 + the gas cloud landed on `backend` *(🔒 user: "follow HANDOFF_Backend_Takeover_CE3228_Cloud.md. push to backend branch when in consistent state")*

⭐ **Built (from the takeover of `wip/backend-ce3228-cloud`):** [`CE-3228`](Blueprint_Issues_Tracker.md) — ONE creation request on the
FDP bus (`EntityCreationRequest` moved to `Fdp.Toolkit.NetworkSpawning.Events`; the queue singleton gone; every local author publishes;
`ScenarioSpawnAdapter`'s four affordances end in one request mapping; the editor is node 1; closes `CE-3226`) — design
[Entity Creation §3.4d](../DESIGN_Entity_Creation_Unification.md) (as-built at its end) · [`CE-3219`](Blueprint_Issues_Tracker.md) part 3 —
the gas / smoke cloud (component **401**, `dtAreaEffectCloud` 144, TKB 7202), asked for with that request, owned by the default processor —
design [Civilians §7.3b/§7.3c](../DESIGN_Civilians_And_Crowd_Control.md). Sent: behaviors the
[gas reactions frame](batches/HANDOFF_Backend_To_Behaviors_Gas_Reactions.md), ui the [cloud note](batches/NOTE_Backend_To_Ui_AreaEffectCloud.md).
**Next:** [`CE-3227`](Blueprint_Issues_Tracker.md) (gas as a danger area) · [`CE-3222`](Blueprint_Issues_Tracker.md) B-4 the civilian
perf gate (unblocked: behaviors' `CivilianHsm` landed) · `CE-3220` part 2 · the live `ua-civ-neutral-gate` re-check (CE-2242 + CE-3225) ·
CE-3216 S2 (from §0000).

## 0000. NOW (`2026-10-16`) — a new backend session holds the lane *(🔒 user: "pls write handoff to another session (also backend) so i can continue there")*

⭐ **The queue is [`batches/HANDOFF_Backend_Continuation_2026_10_16.md`](batches/HANDOFF_Backend_Continuation_2026_10_16.md)** — ① CE-3216
S1-0 (the weather programme's first slice: a positionless World Environment entity with the scenario clock, two-node rail FIRST),
② CE-3216 S1, ③ CE-3217 for ui. Built `2026-10-16`: CE-2210 (the ejected pilot flies) · CE-2217 (a vehicle part's face is the armour arc) ·
CE-2218 (the muzzle-line hold, `Masked`) · CE-2209 (routes without a transform) · CE-2222 (missile turn rate as data; the Javelin
top-attacks from 150 m). Designed: [Weather and Time](../designs/weather/DESIGN_Weather_And_Time.md) (R-3012, W-8).
⭐ **Progress (`2026-10-16`, this session):** ① **CE-3216 S1-0 ✅** and ② **S1 ✅** (backend half) — the World Environment (8810) with
the scenario clock and the global weather, Weather Regions (8811), `WeatherField`, the `Wind` singleton retired; as built in the design §6,
report [REPORT_Backend_Weather_And_Time_S1](batches/REPORT_Backend_Weather_And_Time_S1.md). ③ the editor half + CE-3217 → ui:
[HANDOFF_Backend_To_Ui_Weather_And_Scenario_Time](batches/HANDOFF_Backend_To_Ui_Weather_And_Scenario_Time.md). **Next:** S2 (perception:
vision by visibility and light, thermal crossover, hearing by wind) — not started; a runtime weather edit from a non-owning host.
④ **`coordinator@0eb432b0f` merged** (`5252dab54`, the Player-node programme, Q91 / CE-4000…4011) with three merge fixes: CE-4007's
delete-on-wipe on the clock/weather egress, route docs + MCP tools for `/cluster/roster|transition|cancel`, and the shared-JSON-options
rail (coordinator's transfer code + backend's `AudioSettings`). ⑤ **TM-023 fixed** — the 6 long-red `Fdp.ModuleHost` convoy tests were stale (async modules must never convoy); 218 / 0.

## 000. NOW (`2026-10-15`) — a new backend session holds the lane *(🔒 user: the CE-3203 session is ON HOLD until it finishes)*

⭐ **The queue is [`batches/HANDOFF_Backend_Continuation_2026_10_15.md`](batches/HANDOFF_Backend_Continuation_2026_10_15.md)** — ① CE-2180
(the bomb lands 20–27 m long: release latency between the brain and the SimHost), ② CE-3203 C4 (is a carried weapon struck?), ③ the CE-3203
leans waiting on the user. Last built: CE-3203 (one fragment law for people, hulls and weapons; weapons break per type) at `84c27344e`;
CE-1110 answered (already built in `c313ba0d7`). ⚠ §00 below is the 2026-10-10 programme — history for the current work.
⭐ **Progress (continuation session, `2026-10-15`):** ① **CE-2180 ✅ BUILT** — the release time crosses (`RaisedAt`), the SimHost steps the
carrier back and flies the bomb forward in cue steps; live `ua-air-strike` burst **3.9 m** (was 20–27 m), all five checks PASS (Air Ops §8b).
② **CE-3203 C4 ✅ measured** — a carried Javelin IS struck (Weapon Systems §5b). ③ C1–C5 APPROVED (R-3008). ④ **CE-3183 ✅ FIXED** (`bcf073b2e`): the SimHost suite's rotating OOMs were the kernel's mapping cap — undisposed test worlds rooted by `EntityRepository`'s strong self-handle; the handle is weak now ⇒ SimHost.Tests 1406/1 (the 1 = behaviors' CE2079 rail). ⑤ **Autonomous rows (user: "go ahead autonomously")** — CE-3148 ✅ (static obstacles carry DIS kind 5 / the car kind 1, drawn as a cultural-feature box, not magenta) · CE-3189 ✅ (already fixed `8ee002bcb`) · CE-3166 ✅ (Presentation reds re-pointed) · CE-3165 ✅ (closed) · CE-3114 ✅ (nav tile cache prunes) · CE-3008 ✅ (capability loss aborts the montage in the Muscle's state, R-180) · CE-3138 ✅ (the cross-host danger-area rail: its Abrams read a head-on T-72 at 0.50 since CE-2165 V1 — unit now a rifleman). ⑦ **Behaviors handoffs (merged `behaviors@351697b62`):** [Air targets](batches/HANDOFF_Behaviors_To_Backend_Air_Targets.md) AT1–AT4 ✅ (`e59408ee5`, `c99d22e44` — rounds on their own bit; weapon systems skipped; a killed flyer in a landing meets the ground; UH-60s get health + a brain; the lead on the firing node) · [Munition cost](batches/HANDOFF_Behaviors_To_Backend_Munition_Cost.md) MC1 ✅ (`c99d22e44`). Proposals to the user: armour faces for A-10 / AH-64 / UH-60 ([Air Ops §7c-i](../designs/air-attack/DESIGN_Air_Operations.md)); a real collider for a deployed weapon system. CE-3198 gate LIFTED by the user — next. ⑥ **CE-3180 → [Architect Question 92](Architect_Question_92_Per_World_Component_Registry.md)** (one component table per world, a frozen production catalogue, a duplicate-id rail; slice S1 can land first) — WAITING on the user's answers; findings filed CE-3204/3205/3206. ⛔ **CE-3198 (NLAW overfly) is PARKED** — the user's gate: queue it after the drone attack (behaviors appends the go). Not started: CE-3180 (fixture component ids — a cross-lane sweep), CE-3200 (deferred by its row). ⚠ The full ClusterRunner integration suite never finished in one run (killed at 30 min); the EQS subset is 30/30 after CE-3138. Found: the far
technical's ④ fails 3 of 4 live runs (AE5 holes a 100-health technical into a mobility kill at ~125 m) — reported, not changed; the whole
SimHost suite is red at base too (CE-3183 broadened).

⭐ **`2026-10-15` (later):** CE-3207 ✅ (`2e3f4d441`, scenario saves carry no runtime diagnostics) · CE-3205 ✅ (`645eb2886`, the module host registers a module's component in ITS world) · CE-3206 ✅ (`c05dd2723`, id 212 is the `INavmeshProvider` slot alone) · CE-3119 ✅ (`f96b1c295`, the exercise archive carries the scenario as it was at the start — mgmt-1 §13.3a) · CE-3086 ✅ (`e41882ee0`, Terrain Height H4: basic-desert ridge + wadi on a grid, §5d). CE-3158 was already built (`fa0cf54a0`); its row stays open on the live "fires from cover" check. Not built, need a nod: CE-3200 (a wire field). `Fdp.ModuleHost.Tests` carries 6 known reds (TM-023).
⭐ **Air Defence handoff (behaviors, CE-2187…CE-2190) ✅ DONE `2026-10-15`** — [report](batches/REPORT_Backend_Air_Defence.md): the IR seeker (leads its target), proximity fuze, `TargetDomain`, MANPADS 613/614 + gunners 202/203, flares / chaff (`CountermeasureSystem`, `SimRng` moved to `Fdp.Toolkits`), the missile warning across nodes (dt 133/134), the ZU-23-2 (615, technical 110). Behaviors' half is CE-2191.

## 00. NOW (`2026-10-10`) — the behaviour LIBRARY: generic, squad-aware *(this session is the ACTIVE backend session; 🔒 user: "the other one is on hold")*

| what | where | state |
|---|---|---|
| **the review** — is the behaviour library generic? inventory (88 node classes), overlap clusters, PeekAndFire audit | [`REVIEW_Behaviour_Library_Genericity.md`](../REVIEW_Behaviour_Library_Genericity.md) §1–§6 | ✅ decided: **R-256** (L1–L4 + L5 accepted) · **R-258** (the library is BTREES — C#-built allowed; blueprint/HSM copies go *only where redundant and not the best host*; the three-host DEMO stays, on the library implementation) |
| **PeekAndFire generalised, squad-aware** — G1 peek from the point · G2 honest Failure · G3 friendly line + ROE on bursts · G4 locked target · G5 `CoverClaim` · G6 `MayExpose` seam · G7 `SimRng` fix · G8 infantry scope | [`DESIGN_Peek_And_Fire.md`](../DESIGN_Peek_And_Fire.md) §10 (+ §10.7 CoverClaim) | ✅ **APPROVED R-257** · **`CE-3158` READY-TO-BUILD** (slices G-1…G-6, each red-proved) · ⛔ **user said "not implementing yet"** before approving — build only when told |
| the universal-soldier demo | `scenarios/ua-universal-soldier` · rail `PostureScenarioTests.CE3094_*` · [`TUTORIAL_Universal_Soldier.md`](../TUTORIAL_Universal_Soldier.md) | rail LOCKED (hidden >4000 f, leg 2, his rounds reach SimHost, a hostile hurt, defensive once wounded, ≥2 m moved); he still dies in cover ⇒ `CE-3157` (closed by CE-3158 slice G-6) |
| done this run | `CE-3092` (mobility kill = TKB opt-in) · `CE-3095` (fire events Reliable/KeepAll) · AQ85 hit chance (built by the other session, R-216) | ✅ |
| synced with ui | merged `ui@7648a2ff6` at `3ac4baefc` (world-query seam CE-1035, terrain height CE-1034, 3-D map). ⚠ ui took R-248…R-255 ⇒ our library ruling is **R-258**. No CE-3158 file touched; the ui rules binding it (G3 friendly test joins `Trace(Fire)` with WQ-F · `ClaimRadius` 3-D · `CoverClaim` in the shared registry · brain names the target only) are in [`DESIGN_Peek_And_Fire.md`](../DESIGN_Peek_And_Fire.md) §10.8 | ✅ |
| ⭐ **CE-3158 BUILT `2026-10-10`** | G-1 `a3b0e6e42` · G-2/3/4+L5 `ade8eaf2b` · G-5/G-7 `5390ed046` · G-6 `fa0cf54a0` (+ rails `051bcc251`); R-259 L7 approved (`FallBack` the one retreat). As-built + deviations: [`DESIGN_Peek_And_Fire.md`](../DESIGN_Peek_And_Fire.md) §10.9 | ✅ SimHost 1198/0 (+3 skip), posture scenarios 8/8 + CE3094, generators 398/398. ⚠ CE-3157 still OPEN: the soldier now dies in a ~48 s **Flee** before TakeCover matters ⇒ **`CE-3159` is next** (measure: `CE-3097` speed vs `FallBack`'s point) |
| ⭐ **overnight `2026-10-10`** | **CE-3097 REFUTED** (asked speed = real speed; harness frame ≈ 6.5 ms sim) — the real limit is `InfantrySoldier.MaxSpeedFwd 2.0` · **CE-3159 root-caused** ⇒ [`Architect_Question_88`](Architect_Question_88_Soldier_Speed_And_Retreat_Under_Fire.md) · **L1 done** (overview §6a) · **L4 STOPPED** (premise half-false; lean in the review §5) · **R-258 measured: nothing retired** (review §7) · G8 flags + claim gizmo built (`a04aa6d1a`, `7b6a2da86`, goldens `9547ad716`) | ⏳ **WAITING ON THE USER:** AQ88 A/B/C · L4 lean (palette contracts) · the three-host TakeCover drift lean (review §7) |
| ⭐ **operations catalogue `2026-10-11`** (`CE-3160`) | [`DESIGN_Operations_Behaviour_Catalogue.md`](../DESIGN_Operations_Behaviour_Catalogue.md) — the user's modern-operations request recorded, behaviours categorised (IND/SQD/VEH/AIR/ASY/CIV/MED), blocks / sensors / EQS / engine support per gap, roadmap P0–P7 | 📋 design only; decisions D1–D8 await the user (lean D8: P0 enablers + P1 convoy ambush first) |
| ⭐ **grenade scenario end to end `2026-10-11`** (WL-4 + AQ89; other lanes on hold) | [`DESIGN_Weapon_Layers.md`](../DESIGN_Weapon_Layers.md) §5f · [`AQ89`](Architect_Question_89_Suppress_And_Close.md) §4 (R-269) · `scenarios/ua-grenade-or-rifle` · cluster rail `GrenadeOrRifleScenarioTests` | WL-4 mechanics `8de11a79d`; the cluster run found Suppress idling at a hidden enemy ⇒ AQ89 blind suppression + `CombatTemperament` + a willing close inside Suppress. ✅ cluster rail GREEN (closes under blind fire, throws, hurt behind the sandbags). Rifleman blind suppression asserted too (4 rounds into the sandbags, UnderFire stamped on SimHost). Open: a TKB per-type temperament when a second profile is needed |
| ⭐ **entity control `2026-10-11`** (possession FPS / RTS / menu orders / AI player over HTTP; R-270) | [`DESIGN_Entity_Control.md`](../DESIGN_Entity_Control.md) §2 decisions, §7 slices | DESIGN written; C-1..C-12 (R-270) and M-1..M-10 (R-271 (backend)) approved; all messaging is FDP events, the network only translates — every slice railed first in the networkless editor. ✅ S1 BUILT (`CE-3167` + `CE-3168`: orders over HTTP + sitrep + side lock + MCP group Z, the `/entities/command` fix; the intent egress now on every node). ✅ S2 attack BUILT (`CE-3170`, design §10: `DesignatedTarget` read first by `SquadAssignment.TargetOf` + `TopThreat`; `UntilTargetDown`; the advance hunts; HTTP `attack`); found `CE-3177` (a hidden target is suppressed, not closed on — a decision-scoring change for the behaviors lane). ✅ S2 menu orders BUILT (`CE-3169`, design §11: `MenuOrders` on every live map host; the context ingress CGF and IG never had; IG egress + entity-map singleton; picker cancel; no orders in replay); filed `CE-3178` (relayed menu action on a windowed node) and `CE-3179` (ReplayBrowser runs no ingress). ⚠ The behaviors lane is on squad behaviours (`CE-2123`): stay out of `Squad/**`, `SquadManeuverRunner`, `group-maneuvers/*`; their `R-271` (passengers) will need the `(behaviors)` tag at merge. ✅ S3 BUILT (`CE-3171`, design §8a/§12: View › RTS Command Mode — LMB select, RMB move/attack, Esc). Filed `CE-3180` (fixtures reuse production component ids). ✅ S4a-S4c BUILT (design §8b/§13): `BehaviorOrigin.Possession` (pauses, refuses reactions, its release resumes), manual drive (`ManualDrive` + `KinematicsMode.Manual` + `ManualMotion`), `PossessionSystem` + `OperatorControl` + HTTP `possess`/`control` — railed in the networkless editor. ⚠ Component 359 was allocated twice (ui's `WeaponPosture`): backend's ids moved to 360-362 at the ui merge; new ones 363/364. ✅ S4d operator fire + S4e cluster plumbing BUILT (§13.1/§13.2; `CE-3172`/`CE-3173` closed; a live 4-node rail). ✅ S5 BUILT (`CE-3174`, §8d/§13.3): `PossessionStation` follows `PossessedBy` into first person on every map host, `FpsCameraDriver` sends the control frames, HUD with `WeaponAmmo` (365, `dtWeaponAmmo` 130), "Possess (FPS)" (action 255); (V) screenshot by `Map3DFrameRail.CE3174_V_*` under Xvfb. ✅ Follow-ups closed `2026-10-11`: `CE-3175` (no key press reached a backend gizmo — the canvas drained the queue; Esc cancelled nothing), `CE-3176` (a pending MoveTo drove straight at its goal), `CE-3178` (a relayed menu order ran again on every windowed node holding the unit), `CE-3179` (replay menu clicks never dispatched), `CE-3181` (M-5 navmesh check — the provider WAS reachable). Open: `CE-3180` (fixture component ids — measured 236 / 42 collide with production; a per-test-assembly rail, cross-lane sweep) and `CE-3177` (the behaviors lane's). ⇒ the entity-control programme's own slices S1-S5 are DONE. Filed `CE-3181` (no navmesh check on manual walking). Defects found: `CE-3175` (key queue), `CE-3176` (None driven as Direct) |
| open findings | ~~`CE-3097`~~ refuted (see overnight row) · the `DefendAreaMapper` routes to tree-less `InfantryCombat`/`ConvoyEscort` (L4 — file + measure first) · `Hrot.NED.Tests` does not compile (pre-existing, unrelated) | ⏳ |

**Waiting on the user:** [`AQ88`](Architect_Question_88_Soldier_Speed_And_Retreat_Under_Fire.md) A/B/C (soldier speed, crouched run, retreat hides from the shooter) · the L4 palette lean · the three-host TakeCover drift lean (R-258) · `CE-507` D2/D3 (who runs squad manoeuvres) · the operations catalogue D1–D8 (`CE-3160`). *(L7 approved = R-259.)*
**Work order when told to build:** L1 (library list + promotion rule in the Overview) → retire redundant blueprint/HSM library copies (corpus check per item) → **CE-3158** G-1…G-6 → L4 registry / L5 `FireAtTarget` routing.

**Traps met this run:** the graph goes stale after a big merge — re-index with the CLI (`codebase-memory-mcp cli index_repository --repo_path <root> --mode moderate`; the MCP call times out at 60 s) · two cluster test processes started together share a DDS domain and corrupt each other (a rifleman "dead at f0") · a `;` inside a Mermaid sequence `Note` breaks the parse · ruling ids collide across lanes — fetch `ui`/`behaviors` RULINGS before allocating (`ui` took R-254 while this lane held it; ours became R-256).

## 0. NOW (`2026-10-06`) — the utility AI demo programme

- **Owning design:** [`DESIGN_Utility_AI_Demo_Scenarios.md`](../DESIGN_Utility_AI_Demo_Scenarios.md) — §4 the seven scenarios U1–U7, §6
  the G-items, §8 the approved leans (R-209), §9 armour (CE-3071 ✅), §10 the danger sensor (CE-3072/3078/3079/3080 ✅), §11 G8+G3
  (fire distribution + `/squad`), §12 G7 (weapon mounts). Runbook: [`RUNBOOK_Utility_AI_Demos.md`](../RUNBOOK_Utility_AI_Demos.md).
- **Built:** U1 `ua-posture`, U2 `ua-threat-ranking` (live + in-process, CE-3085) · `ua-danger-crossing(-bp)` · G8 `CE-3088`, G3
  `CE-3087`, G7 `CE-3089` (code + rails; U6 `ua-fire-distribution` / U5 `ua-weapon-choice` scenarios + checks — see the tracker rows
  for the live result).
- **Behaviors lane (P2):** G4 `CE-3082`, G5 `CE-3083`, G6 `CE-3084` dispatched in
  [`HANDOFF_Utility_Demo_P2_Behaviors.md`](batches/HANDOFF_Utility_Demo_P2_Behaviors.md) — ⭐ its §6 SYNC is the channel; read the
  newest entries first. The backend then builds U3 `ua-three-hosts` / U4 `ua-attack-approach` against their assets.
- **Next on backend:** G10 `CE-3086` (desert ridge + wadi — only U7 needs it) · U7 waits on CE-507 D2/D3 (user decisions) ·
  CE-3081 (more danger kinds, not approved) · open reds CE-3076 / CE-3077.
- **Id block:** `CE-3000`–`CE-3999`, next free per the tracker header (`CE-3090` at this writing).

## 1. Where it stands (`2026-10-03`)

- ⭐ **Terrain-world slice 1 BUILT** ([`DESIGN_Terrain_World.md`](../DESIGN_Terrain_World.md) §8, report [`REPORT_Terrain_World_Slice1.md`](batches/REPORT_Terrain_World_Slice1.md)): GeoJSON world on every ECS node, map layer, movement Z (W8), DotRecast navmesh baked per terrain + solver on SimHost (W6, `CE-3006` closed), Z-up navigation (`CE-3011` closed), 3-D LOS with posture eye heights (W5). Open: `CE-3010` (Stride animation, Windows), `CE-3029` (vehicle follower cuts corners) (`CE-3017`, `CE-3018`, `CE-3024`, `CE-3025`, `CE-3026` — MoveTo is a `PathToPoint` intent planned on the vehicle side on every host, `CE-3027` — vehicle bake 1.8 m, `CE-3028` — `/world/info` live — closed `2026-10-03`).
- ⭐ **Terrain EQS slice BUILT** (`CE-3030`, [`EQS_Design_v1.3_final.md`](../designs/eqs-2/EQS_Design_v1.3_final.md) §19): terrain sight + cover, child-sensor self, new generators/tests, starter templates `FindOpenFiringPosition` / `FindFlankingPosition` / `FindSafeRetreatPoint` / `FindThreatsInView`. ⚠ No behaviour consumes the four new templates yet — the natural next step is a behaviours-lane consumer (BTree / blueprint) and a live run. Live-run scenario: `scenarios/tt-nav-los` (copy into `/tmp/FDP_Temp/shared/scenarios/` — the runtime NAS root).
- ⭐ **Asset management BUILT** (user put it in scope `2026-10-03`): [`DESIGN_Asset_Management.md`](../DESIGN_Asset_Management.md) §8 increments A→B→C (`CE-3019`/`CE-3020`/`CE-3021`), §10 the terrain deltas; plan [`PLAN_Asset_Management_Build.md`](PLAN_Asset_Management_Build.md).
- Backend id block **next free `CE-3022`**.

### ⛔ HISTORY — `2026-10-02`

- Branch **`backend`**. Last batch: [`HANDOFF_Ownership_Remaining_Work.md`](batches/HANDOFF_Ownership_Remaining_Work.md) → report [`REPORT_Ownership_Remaining_Work.md`](batches/REPORT_Ownership_Remaining_Work.md).
- Ownership programme: S1–S8 built; live matrix §5.7.1 **E1–E8 all ✅** (E8 = the multi-process crash reclaim, +10.2 s, no message).
- Done this batch: `CE-3003` (debug writes ask the owner), `CE-3004` (CGF polls mission acks), `CE-516` (editor uses the injected offline factory), `CE-518`'s 11 unit reds (all stale tests).
- Backend id block `CE-3000`–`CE-3999`, **next free `CE-3029`**. Every `behaviors` merge conflicts on the id-block table: keep their behaviors row and our backend row.

## 2. Waiting on the user

| id | question | lean |
|---|---|---|
| `CE-3017` | the editor (≡ CGF solo) declares `NavigationSolver` but composes no solver | add the SimHost `NavigationSolver` capability to `EditorCapabilities.BuildDefault` + `AttachNavmesh`; keep the order rails green |
| `CE-3018` | spatial grids fixed at [0,1000)² / [-750,750)² | size from `TerrainWorld.Bounds` via a holder-style generation swap; today a loud warning at commit |
| `CE-3007` | after a crash an IG creator reclaims Muscle/Perception descriptors it has no components for ⇒ nothing publishes them, other nodes keep stale samples | to be handled later (user `2026-10-03`) |
| `CE-524` | where the `NavState` write belongs | `CE-3006` composed the solver in-process (write live and correct); for the split node: the writer goes under MuscleGround and the scale-out hop reuses `PathRequestBatch`/`PathResponseBatch` (design §5.9) |
| `CE-513 (backend)` | ✅ DONE `2026-10-03` (Q80 §5) | — |
| `CE-3008` | Muscle clears the Brain's montage queue on capability loss | dormant; read the abort from the Muscle's queue state |
| `CE-3009` | animation egress translators gate on entity authority | prerequisite for composing animation replication across nodes |
| `CE-3010` | no host composes the Muscle animation pipeline ⇒ Brain montages/look-ats never play (Stride included) | compose `AnimationMuscleModule` over `StrideAnimationBackend` in editor_stride; cross-node needs `CE-3009` |
| `CE-518` (rest) | the whole `Hrot.ClusterRunner.Integration.Tests` run is order-dependent | gate by class `--filter` until someone isolates the shared state |

## 3. Tooling notes that cost time

- Multi-process cluster: `HROT_E2E_PORTS="SimHost=8102,Scenario=8101,IG=8103" python3 scripts/ownership-e2e.py …` (runbook §1.2 launch; use a non-zero `-d` domain).
- `ddsmonitor` is not preinstalled: `dotnet tool install --global cyclonedds.net.ddsmonitor`, run `~/.dotnet/tools/ddsmonitor` with `DOTNET_ROOT=/usr/local/dotnet`. A SIGINT leaves the JSON array unterminated: parse it sample by sample.
- `scripts/find.sh` reported 0/0 for a `\b…|…\b` pattern that plain grep matched in 40 files: do not trust its zero for alternations.
