# Test-Health — Architect Decisions (NEEDS-DECISION items)

Resolved 2026-07-12 by code-grounded analysis (human architect unavailable; verified against source, not just the diagnostics). Each ruling says which side is correct + the fix + confidence. Companion: `.dev/_DONE/test-health/diagnostics/*.md` (root causes), `TEST-HEALTH.md` (ledger).

## ⭐ Highest leverage

### D-1 · Nav phase-ordering — PRODUCTION BUG (fix bootstrapper) — HIGH
`EngineBackedNavigationModule.RegisterProviders` guards `_navmesh/_registry != null`; those are created by `RegisterSystems` (run in `Kernel.Initialize()`, Phase 7). `SimHostNodeBootstrapper.cs:294` calls `navModule.RegisterProviders(context.World)` in Phase 6a — **before** Initialize. The guard is a legitimate contract; the caller violates it. Nothing between RegisterModule and Initialize needs providers early.
**Fix:** move the `navModule.RegisterProviders(...)` call to **after `context.Kernel.Initialize()`**. **Clears 40+ tests** across `SimHostComponentRegistrationTests`, `SimHostTimeSyncTests`, and the ClusterRunner `SimHostSubsystemTests` cluster (and unblocks the secondary DDS-teardown crash, D-9).

## Fdp.Toolkits

### D-2 · Pitch-sign convention — PRODUCTION BUG (fix math) — MED-HIGH
`SimTransformBridgeSystem` documents "positive = nose up" (+ DIS/aerospace standard), but `SimMath.ToYawPitchRollDeg` returns nose-down-positive. The two consumers (`GeoSpatialEgressTranslator.cs:145`, `BdcWorldPosTranslator.cs:77`) write pitch to the wire **without compensating** — i.e. they currently emit wrong-signed pitch.
**Fix:** negate pitch in `SimMath` to match the documented nose-up-positive convention. **⚠ Wire-protocol change** — validate against any external DIS interop expectation before shipping (but the current value is non-standard, so this aligns to spec).

### D-3 · ComponentDiffService null-contract — TEST-STALE (fix tests) — MED
`DomDiffer.Diff` returns `null` for an unmodified tree; **all** callers already handle null as "no change" (`DiffToJournalConverter.Convert:30`, `UnknownsJournal`, `ReplayBrowser`). The established contract is null=no-change.
**Fix:** update DIF_T01/T04/T10 to expect `null` for identical trees (do **not** change the production contract callers depend on).

### D-4 · Clear production 1-liners — PRODUCTION BUG (fix) — HIGH
Unambiguous real bugs; fix production, keep tests:
- `BicycleModel.Integrate` — clamp `speed = max(0, speed)`.
- `GizmoSettingsPersistence.ParseValue` — match `"CsFloat32"` (not `"Float32"`).
- `IdAllocationMonitorSystem` — attach `+= HandleLowWaterMark` on the first-Execute path.
- `RecordingSearchService.GreaterThan` — returns 1 vs 3 for `>75` on {100,90,80,70,60}; fix the comparison.

### D-5 · Stale assertions / fixtures — TEST-STALE/FIXTURE (fix tests) — HIGH
- 4 combat struct `sizeof` constants — update to current sizes.
- `HarnessTransform/Position/Velocity/EntityInfo` component ids 202–205 — **renumber** (collide with `AreaQueryBatchData`/`EqsTargetPool`/`BlueprintBlackboard1024/4096`); use test-only 291–299 range.
- `LocalDiskStorageProvider` — assert `root/"scenarios"/...`.
- `ReplayModule.SeekToFrameAsync` — production is now synchronous by design; update the off-thread expectation.
- `MissionPlanQueue` — register it in the auto-serializer test fixture.
- 2 genuine flakies (GC zero-alloc, static `ComponentTypeRegistry` isolation) — trait `Flaky` with 3× evidence per README.

### D-6 · DataDrivenGizmo gen-0 routing — TEST-STALE (verify then fix tests) — MED
`FindGizmo` intentionally handles `Generation==0` via an index-only lookup (events carrying only an index); `entity.IsNull` rejects only the true null sentinel. Production looks correct.
**Fix:** update SC_GZ066_2/5 to the gen-0 lookup contract — but **verify the exact test-setup failure first** (this contradicts TH-1's "real bug" read, so confirm before committing).

## StrideMock / Presentation

### D-7 · NedReplicationModule `NodeRole.None` — PRODUCTION (make it skip) — MED
The ctor throws `ArgumentException` for `NodeRole.None`; headless/test nodes legitimately bootstrap with `None`. `None` = "no role" ⇒ no replication is the correct semantics, not an error.
**Fix:** `WithReplication` **skips (no-ops) for `NodeRole.None`** rather than throw. Fixes the 9 `SharedApplicationBootstrapperTests`. (Product robustness improvement, not a test hack.)

### D-8 · Presentation `ctx.Resources` NRE (28) — FIXTURE, known fix exists — HIGH
Fix exists on branch `test-fixing`/`eebd7d9e` but that commit bundles junk. **Cleanly extract only** `Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs` + `Vis2D/Layers/DebugGizmoLayer.cs` (null-guard `ctx.Resources`), review the 84-line `DebugGizmoLayer` change, and set `Resources` in the test `MakeCtx()`. + `SharedApplicationBootstrapper` `BuildContext` added to the reflection test's expected-abstract list.

## ClusterRunner (beyond D-1)

### D-9 · DDS teardown crash — PRODUCTION (defensive) — MED
Secondary to D-1 (bad reader state after the nav exception aborts the suite). Add a try/catch around `dds_take` in `HrotRunnerHarness.Dispose()` so teardown can't kill the test host.

### D-10 · EditorHarness BarrierPending (8) — FIXTURE — HIGH
`PumpFrames` no-ops in BarrierPending (deltaTime=0). Inject `TimeConfig{LookaheadWallTicks=0}` into the harness.

## Scenarios

### D-11 · Event-2030 (`RaycastRequestEvent`) — FIXTURE, NOT a prod defect — HIGH
Real `HeadlessDemoApp` registers it; two test scenarios' `Configure()` don't. **Fix:** register `RaycastRequestEvent`/`RaycastResultEvent` in `BallisticsAndHitScenario.Configure` + `UrbanCombatNewScenario.Configure`. **⚠ Verify `RegisterEvent` is idempotent** (else register in the test harness instead, to avoid a double-register throw via `HeadlessDemoApp`).

### D-12 · SensorGrid (4) — FIXTURE — MED
A squad-coordination refactor moved `ThreatEvaluationSystem` to read `ActiveSensorTracks`; the scenario fixture is missing `SensorTrackDebounceSystem` + `ActiveSensorTracksUpdateSystem`. **Fix:** add them to the scenario pipeline.

### D-13 · DistributedTank (7) + ComponentDamage (5) — REAL REGRESSIONS, need a debug batch — n/a
Confirmed genuine behavior regressions, not stale tests:
- DistributedTank: ELM zero-participant auto-promote doesn't advance `_brainHull` to `Active` (regression from BATCH-02 EntityIndex hot/cold rewrite `7c35badb`).
- ComponentDamage: phase-1 baseline already fails at tick 15 (wrong health / `CanMove=false`) before detonation.
**Ruling:** do **not** guess a fix — these need a focused runtime-tracing batch. Prioritize DistributedTank (has a suspected regressing commit).

## SimHost correction

### D-14 · C013 ORBAT-dedup — TEST-STALE (restore fix + rename) — HIGH
Production **intentionally** skips children with no override entry on scenario load (explicit comment: "Prevent duplicate ORBAT entities on scenario load…"). The TH-3 agent's fix was correct; the lead over-reverted it. **Fix:** restore `Assert.Single(cmds)` / `NetworkId=5555` / `LastAllocatedId=0`, drop the `Broken` trait, and **rename** the test (e.g. `C013_ChildOverride_KeyAbsent_ChildSkipped_OnScenarioLoad`) since "AllocatorCalledForChild" now misdescribes the intended behavior.

---

## Execution grouping
- **Auto-fixable now (decided, low-risk):** D-3, D-4, D-5, D-10, D-11, D-12(fixture), D-14, D-8(after clean extract), D-6(after verify), D-7. Batch by project; test-only or clear 1-line prod fixes.
- **Prod fixes needing a validation note:** D-1 (reorder — big win, verify boot), D-2 (wire-protocol sign), D-9 (defensive).
- **Needs runtime-tracing batch:** D-13.
