<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: §1 the B-0 baseline gate table (before any code changed) · §2 the build log B-1…B-9
stale-below: nothing
related-designs:
  - docs/DESIGN_Egress_Change_Detection.md §7 — the design this experiment builds (leans O-A…O-G approved)
  - docs/blueprints/RESUME_Backend_Large_Terrain_Perf.md §0 — the build plan B-0…B-9 and the ground rules
-->

# STATUS — backend: the state egress OFF the main thread (CE-3427 N-1, an experiment)

## 1. B-0 — the baseline, before any code changed (base `eea4a6d54`, cloud VM, 4 cores)

Built each test project (with restore — another lane's merge came in), then `dotnet test --no-build`, `HROT_HIDDEN_WINDOW=1`.

| suite | passed | failed | the reds (all pre-existing: this IS the base) |
|---|---|---|---|
| `Fdp.Network.Cyclone.Tests` | 102 | 0 | — |
| `Hrot.Network.NED.Tests` | 196 | 0 | — |
| `Hrot.SimHost.Tests` | 2 636 (6 skipped) | 9 | `ShippedTreesAreCSharp` ×6, `StrictNetworkSeparation` CE4208, `CgfLogicPack` CE454, `LoadPhaseChain` CE3345 (flaky, as the resume doc says) |
| `Hrot.ClusterRunner.Tests` | 411 | 6 | `CE1436_CgfShapedHost…`, `EverySystemHoldingEntities…`, the two `EverySystemStateFieldIsClassified`, `EveryListedWriter_ReadsTheReplayFlag`, ⚠ and `CE1446_TheKernelRunsItsSystemsInTheSharedBootstrappersOrder` (not in the resume doc's list of 5) |
| `Fdp.Toolkits.Tests` | 4 775 (1 skipped) | 4 | `S0_EveryNodeIdMember_IsANodeId`, `TakeCheckpointIntent_HasRequestIdAndLabel` (other lanes, known), `MissionDirector_DoesNotAdvance…` (order-flaky, known), `BinaryMerger…AllocatesZeroBytes` (allocation rail, ran beside a Roslyn workspace load) |
| `Hrot.ClusterRunner.Integration.Tests` | 301 (3 skipped) | 14 | `AllSubsystemsSpawnMovingVehicleTests.AllSubsystems_SpawnMovingVehicleViaGateway_EntityMovesOnPlayer`, `BlueprintScenarioIntegrationTests.Test3_DynamicSwap_ReplaceEvent_SwapsBlueprintOnEntity`, `CompositionSnapshots.TheHostCompositionIsFrozenTests.TheClusterNodesComposeWhatTheyComposedBefore`, `CompositionSnapshots.TheHostCompositionIsFrozenTests.TheEditorComposesWhatItComposedBefore`, `CompositionSnapshots.TheHostCompositionIsFrozenTests.TheFusedServerComposesWhatItComposedBefore`, `CompositionSnapshots.TheHostCompositionIsFrozenTests.TheStrideNodeComposesWhatItComposedBefore`, `Eqs.TheEditorsRecordingIsFilmedInTheReplayBrowserTests.CE3413_LiveEnded_CloseTheSession_LeavesTheClusterToIdle_TheExerciseKept`, `EventSerializationHelperTests.SerializeToJson_NoResolver_DoesNotThrow_AndProducesJson`, `GhostPromotionTests.OutOfOrder_GeoSpatialBeforeEntityMaster_PositionPreservedAfterPromotion`, `NetworkGatewayIntegrationTests.ReliableInitBarrier_EngagesAndReleases_WhenPeerReportsActiveOverWire`, `SpawnMovingVehicleWithGatewayIntegrationTests.Player_SpawnMovingVehicleViaGateway_EntityMovesOnPlayer`, `SplitAuthoritySpawnTests.CE3221_ASideFlipMadeOnCgf_ReachesSimHost`, `TheClusterAiDebugSurfaceAnswersTests.The_trace_arms_and_reads_the_CSharp_BTree_commander`, `TimeControlIntegrationTests.Stop_PausesTheClock_SoTheNextEditIsFrozen_CE3068` |

## 2. The build

| step | what landed | evidence |
|---|---|---|
| **B-1** | `EqsSolveWorkers` → `Fdp.ModuleHost.Threading.WorkerPool` (public, FDP), the one pool the EQS and the egress share | ⭐ Roslyn rename over stdio (`roslyn_preview_rename` → diff read → `roslyn_apply_rename`, `is_msbuild_workspace: true`; 7 references, 2 files + the file renamed), then the file moved; grep: no `EqsSolveWorkers` left in C#. `EqsModuleTests` green (in the 375-test SimHost egress run) |
| **B-2** | `IOffFrameEgress` (`BeginFrame` / `ScanState` / `ApplyDeferred`), `EgressPipeline`, `EgressPipelineLaunchSystem` with the NEW `[UpdateLastInPhase]`; every egress and cleanup system JOINS first (no join system — design §7a) | rail `The_launch_runs_last_in_Export_though_it_was_registered_first` |
| **B-3** | the replica: a full snapshotable `SyncFrom` + `CopyClockFrom` + `EgressSignals.CopyTo`; a held egress launches nothing | rail `A_held_egress_launches_no_scan…` |
| **B-4** | marked: `WorldPos`, `EntityLookState`, `NavigationStatus` + 29 pure state scans (Damage, DoorState, ObstacleShape, the 3 macro ones, TurretPose, WeaponTaskStatus, ManualDrive, PossessedBy, OperatorUseStatus, EffectState, Gesture, Custody, Lean, CombatProficiency, EngagementArea, Demeanour, SpawnLevel, AreaEffectCloud, PartDamage, HitMarks, Descent, Passengers, WeaponSystemState, ScenarioClock, TrafficDemand, ExerciseResult, MapRoute). ⏳ NOT yet: the 7 `EgressVisitGate` translators (their gate compares table chunk versions — whether a replica sync carries them is not measured), and every translator reading the bus, the `NetworkEntityMap`, smart egress or the epoch outside `OnFrame` (an explorer's classification of all 79 SimHost egress translators) | rail `Off_the_frame_every_state_sample_is_the_on_frame_sample…` |
| **B-5** | `WorldPos` off the frame: the shadow read from the replica, the writes (and a first-sight attach) queued, applied at the join | rail `CE3427_OffTheFrame_WorldPosSendsWhatItSendsOnTheFrame_AndTheShadowEndsTheSame` |
| **B-6** | the scan fans the translators over the `WorkerPool` (`FDP_EGRESS_WORKERS`, default CPUs / 4 ≤ 4), largest first; the replica's candidate index built once before the fan-out (`PrepareQueryIndex`) | ⏳ measured in B-8 |
| **B-7** | `GET` / `POST /diagnostics/egress {mode}`; the mode is taken ONCE per Export (the first join after a launch) | rails `Switching_every_few_frames…`, ⭐ `A_switch_requested_in_the_middle_of_Export_waits_for_the_next_join` — it found a real defect (every join re-read the request) |

**Red-proofs — each a compiling mutation, built and run (`scratchpad/mut.py`):**

| mutation | caught by |
|---|---|
| the `[UpdateLastInPhase]` edges dropped | `The_launch_runs_last…`, `Off_the_frame_every_state_sample…` |
| the cleanup does not join | `A_destroyed_entity_s_last_state_goes_out_before_its_delete…` |
| the launch ignores the hold | `A_held_egress_launches_no_scan…` |
| the systems read the REQUESTED mode | `A_switch_requested_in_the_middle_of_Export…` — ⚠ only once the rig had TWO egress systems (as production has six): with one, the mode is read once per system and the mutation survived. The rail was also what found the real defect (the launch re-taking the mode) |
| no fallback after a throwing scan | `A_scan_that_throws_puts_the_pipeline_back_on_the_frame` |
| the launch in the egress system (before the events) | `Off_the_frame…`, `Switching…`, `A_held…` |
| `ApplyDeferred` writes nothing | `CE3427_OffTheFrame_WorldPos…` |

**Gates after the build** (same base reds, nothing new): `Fdp.Network.Cyclone.Tests` 110/0 · `Hrot.Network.NED.Tests` 196/0 · `Hrot.SimHost.Tests` egress / translator / EQS classes 375/0 (1 skipped) · `Hrot.ClusterRunner.Tests` 411/6 — the same 6 as B-0; the new fields are classified (`DESIGN_System_State_Inventory.md`) and no longer listed.

## 3. B-9 — the system gates WITH IT ON (in progress)

| what | result |
|---|---|
| stress rail (2 000 frames, random switching, 6 translators over 2 workers) | ✅ green — no fault, every frame's state exactly once |
| ⚠ the full integration suite | aborts on a TEST-HOST CRASH at a random point (base: 318 of ~? ran; OFF: 154; ON: 65 — `[NS] DestroyEntityCommand for unknown network entity 1000` the last line) ⇒ the full-suite comparison is not usable; the replication classes are run as a filtered set instead |
| ⭐ a real finding, fixed | `TheEgressShadowExistsAtBirthTests.TheOwnerHasAuthorityOverItsOwnShadow` red ONLY with it on (deterministic, alone) — the deferred first-sight attach found the shadow already upserted by this node's own ingress and did not grant authority. Fixed in `ApplyDeferred`; the class 6/6 with it on |
| pre-existing, both modes | `Keymap.DeclareGesture` "concurrent update" (parallel tests building `MapGestureRouter.Shared`) — reds `SpawnMovingVehicle_PlayerReceivesPositionChangesWithinFewFrames`, `TheEgressQueryActuallyMatchesTheOwnedEntity` at random, OFF too |
