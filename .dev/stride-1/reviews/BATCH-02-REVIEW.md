# BATCH-02 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
External fixed-timestep host-loop driver (`StrideHostLoopDriver`) + `StrideHrotGame` (T5), and the headless `editor_stride` composition skeleton `EditorStrideSubsystem` (T6). Verified independently: read all new source, ran the tests (Core 51/51, Animation 4/4, Game.Tests 5/5), confirmed our commits never touched `Hrot.StrideMock`.

## Verification performed
- Read `StrideHostLoopDriver.cs`: correct double-accumulator, `MaxTicksPerFrame` spiral-of-death cap, leftover carry-over. Tests assert real deterministic tick counts (exact-multiple, irregular-frames-summing-to-N, cap, partial-step-carry, callback-always-fixed-dt) — genuinely behavioral.
- Read `EditorStrideSubsystem.cs`: real composition — `OfflineNetworkFactory`, separate `_orchestrationBus` (distinct instance, registries applied), `CgfLogicPack` + `SimHostCoreLogicPack` (with an explicit P1-seam comment), `NetworkSpawningSystem(localNodeId:0)`, real TKB translators, in-process `ClusterSlave`+`OrchestrationLogicPack`, `ClusterMaster` with empty `Mandatory`, per-frame `OrchestrationBus.SwapBuffers()`+`ClusterMaster.Tick()`. Not a stub.
- Read `EditorStrideSubsystemTests.cs`: the owned-from-birth test drives the **real** spawn pipeline (enqueue `EntityCreationRequest{OwnerAppInstanceId=0}` → `CreateEntityRequestSystem` → `SpawnEntityCommand` → `NetworkSpawningSystem` stamps the authority bit). `HasAuthority<SimTransform>` + `WithOwned<SimTransform>` count==1 are asserted against the real repository — the system sets authority, not the test. Separate-bus and Standby (`ClusterState.Idle`) assertions are real.
- Ran the three Stride test projects myself; counts match the report.
- `git diff 6bb3153d..HEAD -- Hrot/Subsystems/Hrot.StrideMock/` is empty → the 10 `Hrot.StrideMock.Tests` failures are byte-for-byte pre-existing, not caused by this work.

## Issues Found
No blocking issues.

## Test Quality
Strong. T5 driver tests use a binary-exact `fixedDt` (1/32f) to make tick-count assertions exact — a deliberate, sound choice (documented). T6 tests exercise the real spawn/authority/orchestration paths headlessly. The 3-frame spawn latency is understood and documented.

## Notes carried forward
- **P1 (STR-P1-T5) must introduce the togglable groups.** `EditorStrideSubsystem` currently registers sim/post-sim systems flat (no `TogglableSimulationGroup`/`TogglablePostSimulationGroup`). P1's `BulletReverseSyncSystem` must live in a `TogglablePostSimulationGroup`, and P5 replay passes that group to `ReferenceReplayLoadHandler`. Recorded as STR-D5.
- **P1 replaces `SimHostCoreLogicPack`** (FDP integrators) with `StrideKinematicsModule` at the marked seam (`EditorStrideSubsystem.cs:207-211`).
- **`StrideHrotGame` GPU path remains unverified** (needs a GraphicsDevice). This is the genuine T8 (BATCH-03) end-to-end smoke obligation — together with STR-D4 (real asset-compile proof via booting the app).
- Minimal TKB (`TestUnit`) is P0-only; P1+ needs the real TKB/scenario template chain.

## Verdict
APPROVED. Proceed to BATCH-03 (STR-P0-T7 `StrideVisualBindingSystem` + procedural fallback, STR-P0-T8 end-to-end spawn+render smoke — which also discharges STR-D4 and the `StrideHrotGame` GPU obligation).

## Commit Message
```
feat(stride): external host loop + EditorStrideSubsystem composition skeleton (BATCH-02)

Completes STR-P0-T5, STR-P0-T6
- StrideHostLoopDriver (pure, GPU-free): double accumulator, fixed-dt clock, MaxTicksPerFrame cap
- StrideHrotGame: Stride.Engine.Game subclass; external loop via Tick(); throttler disabled
  (WindowMinimumUpdateRate=0); SDL2 events drained inside Tick()
- EditorStrideSubsystem: Mode-1 headless composition — OfflineNetworkFactory, CgfLogicPack +
  SimHostCoreLogicPack (P0 movement stub, P1 seam marked), NetworkSpawningSystem(localNodeId=0),
  separate orchestration bus, in-process ClusterSlave/ClusterMaster (empty Mandatory → Standby)
- HrotStrideApp.Game.Tests: 5 integration tests (boot, separate-bus, Standby latch,
  owned-from-birth spawn via real NetworkSpawningSystem, 60-frame stability)
Tests: 60 (51 Core incl. 14 host-loop, 4 Animation, 5 Game). Pre-existing 10 SharedApplicationBootstrapper
  failures in Hrot.StrideMock.Tests are unrelated (source untouched since baseline).
```
