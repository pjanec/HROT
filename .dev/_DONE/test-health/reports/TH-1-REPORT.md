# TH-1 Report: Test-Health Pass — Hot Suites

**Date:** 2026-06-10
**Suites:** `FDP/Toolkits/Fdp.Toolkits.Tests` · `Hrot/Subsystems/Hrot.SimHost.Tests`
**Batch file:** `.dev/_DONE/test-health/batches/TH-1-INSTRUCTIONS.md`

---

## Result: FAST-GREEN ✓

Both suites pass with 0 failures under the stability filter, verified 2× each:

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
→ Passed! - Failed: 0, Passed: 1856, Skipped: 0  (run 1)
→ Passed! - Failed: 0, Passed: 1856, Skipped: 0  (run 2)

dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
→ Passed! - Failed: 0, Passed: 585, Skipped: 3   (run 1)
→ Passed! - Failed: 0, Passed: 585, Skipped: 3   (run 2)
```

---

## Summary Counts

| Suite | Fixed | Flaky | Broken | Total |
|-------|-------|-------|--------|-------|
| Fdp.Toolkits.Tests | 5 | 3 | 19 | 27 |
| Hrot.SimHost.Tests | 0 | 2 | 55 | 57 |
| **Total** | **5** | **5** | **74** | **84** |

---

## What Was Fixed (Cheap Deterministic)

**Root cause:** 5 test-only ECS components in `Fdp.Toolkits.Tests` had `[ComponentId(N)]` values colliding with production IDs in `GlobalComponentIds.cs`. The static `ComponentTypeRegistry` threw `InvalidOperationException: Component ID collision` when both the production and test types tried to register the same numeric ID.

**Collisions resolved:**

| Type | Old ID | Conflicted With | New ID |
|------|--------|-----------------|--------|
| `AuditCompB` (RegistryAuditTests.cs) | 201 | `ZoneEnvironmentData` (production) | 291 |
| `TestPhysicsCollider` (TestComponents.cs) | 212 | `INavmeshProvider` (production) | 292 |
| `NoSaveVelocity` (TestComponents.cs) | 214 | `EqsSolverGlobalState` (production) | 293 |
| `CachedSpeedComponent` (TestComponents.cs) | 215 | `IPathRegistry` (production) | 294 |
| `TestBallisticProjectile` (TestComponents.cs) | 211 | `ICoverProvider` (production) | 295 |

**Safe range selection:** Verified IDs 291–295 are unused across all `.cs` files in the repo, covering both production (`GlobalComponentIds.cs`) and all test/fake ranges (`NavFakeIds.cs`: 262–268, `GlobalComponentIds.cs` Squad: 262–264).

---

## Flaky Tests (5)

All 5 are order-dependent under the full-suite run due to shared static `ComponentTypeRegistry` state:

### Fdp.Toolkits.Tests (3)

1. **`FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup`** — Zero-alloc GC measurement is JIT-warmup sensitive; passes in isolation. Needs dedicated GC warmup or parallel-disable collection.

2. **`SC_GZ004_2_Register_UnregisteredComponent_Throws`** — Tests that `GetId(typeof(UnregisteredComp[248]))` returns -1 and triggers exception. Fails when another test in the same process registers a type with ComponentId(248) first, making `ComponentTypeRegistry` consider 248 "used". Passes in isolation.

3. **`SC_GZ022_2_Register_UnregisteredType_Throws`** — Same pattern with `UnregisteredComp[249]`.

### Hrot.SimHost.Tests (2)

4. **`SC_HA004_2_MuscleIngress_UnresolvedAreaEntity_WritesZeroTargetResponse`** — Passes in isolation and when run together with SC_HA004_1; fails in full suite when SC_HA004_1 (marked Broken) is skipped by the filter, leaving component state contamination from other tests.

5. **`SC_HA004_3_MuscleEgress_UnresolvedTargetEntity_SkippedInResponse`** — Same root cause.

---

## Broken Tests (74)

All 74 are deterministic failures. No test was silently weakened or marked Flaky to hide a real bug.

### Fdp.Toolkits.Tests — 19 Broken

**Real bugs (needs production fix):**
- `Float32_Roundtrip_Via_Persistence` — `GizmoSettingsPersistence.ParseValue` has `"Float32"` but `FormatValue` writes `"CsFloat32"` — type name mismatch
- `BicycleModel_NegativeSpeed_ClampsToZero` — `BicycleModel.Integrate` returns negative speed instead of clamping at zero
- `RotationToPitchRollDeg_*` (4 tests) — Sign convention inverted in `SimTransformBridgeSystem.RotationToPitchRollDeg`
- `RotationToHeadingDeg_DegenerateRotation_Returns0` — Degenerate case (pitch-down 90°) returns 90 instead of 0
- `MonitorSystem_*` (2 tests) — `IdAllocationMonitorSystem.Execute` resolves `_manager` on first call but skips event subscription (`OnLowWaterMark += HandleLowWaterMark`)
- `SC_GZ066_*` (2 tests) — `DataDrivenGizmoSystem` not routing events by GizmoTypeId
- `DIF_T*` (3 tests) — `ComponentDiffService.ComputeDiff` returns null
- `SR_T03_GreaterThan_FindsMultipleFrames` — `RecordingSearchService` GreaterThan returns 1 result instead of 3

**Stale tests (test assumptions no longer match production):**
- `ReplayModule_SeekToFrameAsync_IsOffMainThread` — Production deliberately returns `Task.CompletedTask` for ECS thread safety; test expects async off-thread
- `LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` — Test asserts `root/"scenario-alpha"` but production appends `"scenarios"` subdir
- 4 struct-size tests (`WeaponFireIntent`, `WeaponFireNotification`, `DetonationNotification`, `DamageAssessedEvent`) — struct layouts changed, test expected sizes are stale

**Missing registration:**
- `RoundTrip_MissionPlanQueue_PreservesPhaseData` — `MissionPlanQueue` not registered in test fixture

### Hrot.SimHost.Tests — 55 Broken

**Missing component registration (EditablePolyline / TkbDatabase / UnitSubordinate):**
- All `StagingEntityExtractorTests.*` (12 tests) — `EditablePolyline` managed component not registered in fixture; entire extractor test class broken
- `Serialize_ThenDeserialize_ReconstitutesHierarchy` — `UnitSubordinate` component not registered for deserialization
- `SetSingletonManaged_TkbDatabase_SameInstanceAfterRegisterAll` — Component type ID 45 not in SimHostComponentRegistry

**Registration ordering bug (`Call RegisterSystems before RegisterProviders`):**
- `InitializeEmbedded_DomainZero_UsesDomainZero`, `InitializeHeadless_NodeIdZero_*`, `InitializeHeadless_NodeIdTen_*`, `OnLoad_RegistersCycloneNetworkCleanupSystem`, `SimHost_Tick_DoesNotThrow` (5 tests) — `EngineBackedNavigationModule.RegisterProviders` called before `RegisterSystems`

**Logic pack system count drift:**
- `CgfLogicPack_EmptyWorld_*`, `CgfLogicPack_TwoGroup_*`, `CgfLogicPack_SingleGroup_*` (3 tests) — system added/removed without updating expected counts
- `SimHostCoreLogicPack_EmptyWorld_*` — same drift

**HillAttack BTree pipeline broken (9 tests):**
- `SC_HA015_*` (4 integration tests), `SC_HA008_1`, `SC_HA011_*` (3 tests), `SC_HA012_6b` — Various stages of the HillAttack BTree not functioning

**Other real bugs:**
- `UnitSubordinateTranslator*` (3 tests) — designation field type mismatch (Int vs String)
- `RequestAreaQuery_DistinctIds_AndFailsAtCapacity` — Duplicate RequestId in AreaQueryBatchData
- `PathfindingBatchData_DefaultCapacity_Is64` — DefaultCapacity value changed
- `HullDownAttackParams_Is40Bytes` — Struct size changed (56 vs 40)
- `SC_HA016_*` (3 tests) — ParsePlatoonHillAttackParams float precision / entity resolution issues
- `SC_HA012_6_IsWaveCompleted_NoRemove_*` — Removes entry when it shouldn't
- `MissionPlanTranslatorTests.*` (2 tests) — NullRef and behavior ID mismatch
- `SC_HA004_1_AreaQueryPipeline_*` — Solver doesn't mark result IsReady
- `EpisodeLoadClusterOpHandlerTests.StartEpisode_NullRepo_*` — Handler not participating
- `EditLoadClusterOpHandlerTests.*` (2 tests) — Missing scenario file (PrefetchFiles not called)
- `BranchedRecording_CapturesHistoricalStateAsKeyframe` — Branch recording pipeline broken
- `SC_GZ057_3`, `SC_GZ057_4` — Wrong primitive shape type / count
- `GZH011_2_UpdateAndDraw_*` — LayerControlGizmo event echo count wrong
- `SC_ER007_ValidActionName_*` — ContextActionIngressSystem not routing event
- `C013_ChildOverride_KeyAbsent_*` — Child allocation count mismatch

---

## Files Changed

### Production-adjacent (ComponentId renumbers, test data only):
- `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/TestComponents.cs` — 4 IDs renumbered
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Audit/RegistryAuditTests.cs` — 1 ID renumbered

### Test Trait annotations added:
- `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/FdpAutoSerializerFixedBufferTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/StatelessGizmoSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Replay/ReplayModuleTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Orchestration/ReferenceHandlerTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/ComponentDiffServiceTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/RecordingSearchServiceTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Replication/IdAllocationTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/CombatComponentTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Algorithms/BicycleModelTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Geographic/SimTransformBridgeSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/DangerAreaProviderTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackDtosTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/PathfindingBatchDataTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryBatchDataTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryTranslatorTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/UnitSubordinateTranslatorTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/StagingEntityExtractorTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/SimHostCoreLogicPackTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/ContextActionRotateHandlerTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/SimHostEntityPresentationGizmoTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/MissionPlanTranslatorTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/EpisodeLoadClusterOpHandlerTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/EditLoadClusterOpHandlerTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/FullBranchPipelineTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/SimHostComponentRegistrationTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/SimHostTimeSyncTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/HierarchySerializationIntegrationTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/CreateEntityRequestSystemTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackIntegrationTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackNodeTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/TkbDatabaseSingletonTests.cs`

### New documentation:
- `.dev/_DONE/test-health/TEST-HEALTH.md` — full ledger
- `.dev/_DONE/test-health/README.md` — filter command + convention
- `.dev/_DONE/test-health/reports/TH-1-REPORT.md` — this report

---

## Broken Tests Flagged for Follow-Up

**High priority (real production bugs):**
1. `EngineBackedNavigationModule` — RegisterSystems/RegisterProviders ordering bug (5 SimHost tests blocked)
2. `IdAllocationMonitorSystem` — event subscription skipped on first Execute call
3. `BicycleModel.Integrate` — negative speed not clamped to zero
4. `SimTransformBridgeSystem.RotationToPitchRollDeg` — sign convention inverted
5. `GizmoSettingsPersistence.ParseValue` — "Float32" vs "CsFloat32" type name mismatch
6. `ComponentDiffService.ComputeDiff` — returns null (3 tests broken)
7. `RecordingSearchService.ExecuteSearch` — GreaterThan returns 1 result instead of 3

**Medium priority (stale tests, easy to update):**
- 4 combat struct-size tests — sizes changed, update expected values
- System-count tests in CgfLogicPackTests / SimHostCoreLogicPackTests — counts changed

**Infrastructure (missing registrations):**
- `StagingEntityExtractorTests` — `EditablePolyline` managed component missing from fixture (12 tests blocked)
- `HierarchySerializationIntegrationTests` — `UnitSubordinate` missing from fixture

**Complex pipeline investigations:**
- HillAttack BTree pipeline (9 tests) — multiple interconnected BTree node logic failures
- `AreaQuerySolverSystem` — solver not marking result IsReady
