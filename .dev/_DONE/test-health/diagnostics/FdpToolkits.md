# FdpToolkits Diagnostic — 2026-07-11

**Suite:** `FDP/Toolkits/Fdp.Toolkits.Tests`
**Run command:** `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --nologo -v q` (no BLUEPRINT_REGENERATE_SNAPSHOTS)
**Result:** Failed: 24 (quiet mode) + 5 additional BehaviorIngress failures observed in verbose run (order-dependent, rooted in new ComponentId(204) collision)

---

## TH-1 Ledger Cross-Check

All 19 "Broken" entries from TH-1 are still failing — none have been fixed since TH-1.
The 5 "Fixed" (ComponentId collision renumbers) and 3 "Flaky" entries are unchanged.

**New failures NOT in TH-1 (found this run):**

| Test | Bucket | Root Cause |
|------|--------|------------|
| `HardReload_RepublishesAssignBehaviorEvent` | B | ComponentId(204) collision — see cluster below |
| `HardReload_SameSize_PreservesWorkingState` | B | ComponentId(204) collision |
| `HardReload_GrowsWorkingState_NoNeighborCorruption` | B | ComponentId(204) collision |
| `Assign_UpgradesTierSynchronously_BeforeFirstTick` | B | ComponentId(204) collision |
| `Assign_ProvisionsWorstCaseReachableStatefulNodes` | B | ComponentId(204) collision |

These appear intermittently (test-order dependent) because the static ComponentTypeRegistry is shared across test classes. When `FdpRecordingHarness` tests run before `BehaviorIngress*` tests, `HarnessTransform[ComponentId(204)]` is registered first; then `BlueprintBlackboard1024[ComponentId(204)]` (via `GlobalComponentIds.BlueprintBlackboard1024=204`) triggers an ID collision exception.

---

## Full Failure Table

| # | Test | Class | A/B/C | Root Cause File:Line | Proposed Fix | Disposition |
|---|------|-------|-------|---------------------|--------------|-------------|
| 1 | `LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` | `ReferenceHandlerTests` | **A** | `ReferenceHandlerTests.cs:35` — asserts `root/"scenario-alpha"` but production prepends `OrchestrationConstants.ScenariosDirectoryName` (`"scenarios"`) subdir, yielding `root/"scenarios"/"scenario-alpha"` | Change assertion to `Path.Combine(root, "scenarios", "scenario-alpha")` | **SAFE-AUTO-FIX** |
| 2 | `WeaponFireIntent_IsUnmanaged_AndHasCorrectSize` | `CombatComponentTests` | **A** | `CombatComponentTests.cs:105` — expected 20, actual 24; struct grew (likely added a field or changed field type) | Update assertion constant to 24 | **SAFE-AUTO-FIX** |
| 3 | `WeaponFireNotification_IsUnmanaged_AndHasCorrectSize` | `CombatComponentTests` | **A** | `CombatComponentTests.cs:120` — expected 20, actual 24; same struct drift | Update assertion constant to 24 | **SAFE-AUTO-FIX** |
| 4 | `DetonationNotification_IsUnmanaged_AndHasCorrectSize` | `CombatComponentTests` | **A** | `CombatComponentTests.cs:149` — expected 28, actual 32; struct grew | Update assertion constant to 32 | **SAFE-AUTO-FIX** |
| 5 | `DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize` | `CombatComponentTests` | **A** | `CombatComponentTests.cs:164` — expected 12, actual 16; struct grew | Update assertion constant to 16 | **SAFE-AUTO-FIX** |
| 6 | `ReplayModule_SeekToFrameAsync_IsOffMainThread` | `ReplayModuleTests` | **A** | `ReplayModuleTests.cs:190` — production deliberately returns `Task.CompletedTask` (synchronous for ECS safety); test asserts off-thread execution ("SeekToFrameAsync completed synchronously; it must run on a background thread.") | Remove or invert assertion to accept synchronous completion | **SAFE-AUTO-FIX** |
| 7 | `FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` | `DangerAreaProviderTests` | **Flaky** | Zero-alloc GC measurement is JIT/ordering sensitive; passes in isolation. Already documented in TH-1. | Needs `[Collection(DisableParallelization)]` + GC warmup loop | **NEEDS-DECISION** |
| 8 | `SC_GZ004_2_Register_UnregisteredComponent_Throws` | `GizmoRegistryTests` | **Flaky** | Order-dependent: static `ComponentTypeRegistry` already has `UnregisteredComp[248]` registered from prior test. Already documented in TH-1. | Requires test isolation / static reset | **NEEDS-DECISION** |
| 9 | `RotationToPitchRollDeg_NoseUp30_ReturnsPitchPositive30` | `SimTransformBridgeSystemTests` | **C** | `SimTransformBridgeSystem.cs:41-43` — calls `SimMath.ToYawPitchRollDeg` which uses `pitch = Asin(2*(W*Y - Z*X))`. SimMath comments say +pitch = nose DOWN (Y-axis in right-hand ENU = left-body = pitch-down). Bridge doc says +pitch = nose UP. The raw extraction returns inverted sign vs what callers expect. | Apply `pitchDeg = -p;` in `RotationToPitchRollDeg` in `SimTransformBridgeSystem.cs:42`. | **NEEDS-DECISION** |
| 10 | `RotationToPitchRollDeg_NoseDown30_ReturnsPitchNegative30` | `SimTransformBridgeSystemTests` | **C** | Same as #9 — sign inverted: returns +30 for NoseDown scenario | Same fix as #9 | **NEEDS-DECISION** |
| 11 | `RotationToPitchRollDeg_Combined_PitchAndRollIndependent` | `SimTransformBridgeSystemTests` | **C** | Same sign inversion | Same fix as #9 | **NEEDS-DECISION** |
| 12 | `RotationToPitchRollDeg_PitchedRotation_PitchDegNonZero` | `SimTransformBridgeSystemTests` | **C** | Same sign inversion | Same fix as #9 | **NEEDS-DECISION** |
| 13 | `RotationToHeadingDeg_DegenerateRotation_Returns0` | `SimTransformBridgeSystemTests` | **C** | `SimTransformBridgeSystem.cs:25-26` — returns 90 instead of 0 for 90°-pitch-down rotation (gimbal lock). `RotationToHeadingDeg` computes heading from raw `ToYawPitchRollDeg` yaw; at gimbal lock (sinp≥1) Asin clamps but yaw extraction via Atan2 still gives 0 → heading = 90-0 = 90. | Detect gimbal lock (`|sinp|>=1`) and return compass heading as `(90-yaw+360)%360` clamped to previous-known or 0 fallback | **NEEDS-DECISION** |
| 14 | `BicycleModel_NegativeSpeed_ClampsToZero` | `BicycleModelTests` | **C** | `BicycleModel.cs:33` — `state.Speed += accel * dt;` — no lower clamp. When speed=5, accel=-10, dt=1, result=-5. | Add `if (state.Speed < 0f) state.Speed = 0f;` after line 33 in `BicycleModel.cs` | **SAFE-AUTO-FIX** |
| 15 | `DIF_T01_IdenticalObjects_IsModifiedFalse` | `ComponentDiffServiceTests` | **C** | `ComponentDiffService.cs:16-19` — delegates to `CoreDiff.DomDiffer.Diff(...)` which returns `null` when trees are **identical** (by design: "Returns null when the two trees are structurally identical — no modified nodes"). Test calls `Assert.NotNull(root)` — but root IS null for identical trees. The contract is inconsistent. | Option A: Have `ComputeDiff` return a non-null `DiffObject` with `IsModified=false` when core returns null. Option B: Change tests to accept null as "not modified" | **NEEDS-DECISION** |
| 16 | `DIF_T04_NumericEpsilon_BelowEpsilonNotModified_AboveEpsilonModified` | `ComponentDiffServiceTests` | **C** | Same root cause as #15 — when below epsilon, Diff returns null, test uses `r1!.IsModified` → NullReferenceException | Same fix as #15 | **NEEDS-DECISION** |
| 17 | `DIF_T10_SameTree_DiffedTwice_NoModificationsSecondTime` | `ComponentDiffServiceTests` | **C** | Same root cause as #15 — second diff of identical trees returns null, test dereferences null | Same fix as #15 | **NEEDS-DECISION** |
| 18 | `RoundTrip_MissionPlanQueue_PreservesPhaseData` | `FdpAutoSerializerFixedBufferTests` | **C** | `FdpAutoSerializerFixedBufferTests.cs` — `MissionPlanQueue` component not registered during `Deserialize` → `InvalidOperationException`. FdpAutoSerializer fixture lacks the component in its registered set. | Register `MissionPlanQueue` in the auto-serializer test fixture (or in `FdpAutoSerializer`'s component registry scan) | **NEEDS-DECISION** |
| 19 | `SC_GZ066_2_StructUpdate_RoutesTo_MatchingGizmo` | `DataDrivenGizmoSystemRoutingTests` | **C** | Two bugs: (a) `GizmosSystemTests.cs:946` — `interactionBus.PublishManaged(new GizmoStructUpdateEvent{...})` but `GizmoStructUpdateEvent` is never `Register<>`-ed on `interactionBus` in `CreateRoutingFixture` (only unmanaged events are registered); (b) `DataDrivenGizmoSystem.cs:542` — `if (entity.IsNull) return null` fires before the generation=0 index-scan path (`entity.IsNull` is true when `Generation==0`, so events using `new Entity(index,0)` always hit early-return null) | Fix (a): add `interactionBus.RegisterManaged<GizmoStructUpdateEvent>()` in fixture; Fix (b): move the `IsNull` guard to only check `Index < 0` for events that legitimately use gen=0, or store gen-0 entries separately | **NEEDS-DECISION** |
| 20 | `SC_GZ066_5_MenuAction_RoutesTo_MatchingGizmo_Only` | `DataDrivenGizmoSystemRoutingTests` | **C** | Bug (b) from #19: `FindGizmo` called with `new Entity((int)evt.AnchorId, 0)` — generation=0 makes `IsNull=true` → early return null before index-scan path. `GizmoMenuActionEvent` IS registered on bus so the event arrives; the routing fails in `FindGizmo`. | Fix the `IsNull` guard in `DataDrivenGizmoSystem.cs:542` to allow gen=0 entities through to the index-scan path | **NEEDS-DECISION** |
| 21 | `Float32_Roundtrip_Via_Persistence` | `GizmoSettingsPersistenceTests` | **C** | `GizmoSettingsPersistence.cs:68` — `FormatValue` emits `"CsFloat32"` (from `SettingType.CsFloat32.ToString()`), but `ParseValue` (line 76) matches `"Float32"` — type name mismatch; always returns default 0. | Change `ParseValue` case from `"Float32"` to `"CsFloat32"` in `GizmoSettingsPersistence.cs:76` | **SAFE-AUTO-FIX** |
| 22 | `MonitorSystem_PublishesRequest_WhenLowWaterMarkTriggers` | `IdAllocationTests` | **C** | `IdAllocationMonitorSystem.cs:28-38` — on **first** `Execute` call (when `_clientId == ""`), manager is resolved via `repo.HasSingletonManaged<BlockIdManager>()` but `HandleLowWaterMark` is NOT subscribed (`_manager.OnLowWaterMark += HandleLowWaterMark` only executes in the `else` branch at line 46-51). Event handler is never attached on first-call path, so `OnLowWaterMark` fires silently with no request published. | In the first-call branch (line 34-37), add `_manager.OnLowWaterMark += HandleLowWaterMark;` after resolving the manager | **SAFE-AUTO-FIX** |
| 23 | `MonitorSystem_ProcessesResponse_AndAddsBlock` | `IdAllocationTests` | **C** | Same root cause as #22 — `requests` list is empty (no request was published) so `requests[0]` throws `ArgumentOutOfRangeException` | Same fix as #22 | **SAFE-AUTO-FIX** |
| 24 | `SR_T03_GreaterThan_FindsMultipleFrames` | `RecordingSearchServiceTests` | **C** | `RecordingSearchServiceTests.cs:87` — expects 3 frames for `GreaterThan(75)` on values `{100,90,80,70,60}` (frames 100,90,80 > 75). Returns 1. Bug in `RecordingSearchService.ExecuteSearch` GreaterThan predicate implementation | Investigate `RecordingSearchService.cs` GreaterThan scan logic (likely stops at first match instead of collecting all) | **NEEDS-DECISION** |
| 25–29 | `HardReload_RepublishesAssignBehaviorEvent`, `HardReload_SameSize_PreservesWorkingState`, `HardReload_GrowsWorkingState_NoNeighborCorruption`, `Assign_UpgradesTierSynchronously_BeforeFirstTick`, `Assign_ProvisionsWorstCaseReachableStatefulNodes` | `BehaviorIngress*Tests` | **B** | `FdpRecordingHarness.cs:23` — `HarnessTransform` uses `[ComponentId(204)]`; `GlobalComponentIds.BlueprintBlackboard1024 = 204` was added in a later batch (BATCH-03C). When recording-harness tests run first, ID 204 is claimed; `BehaviorIngress*` tests then fail trying to register `BlueprintBlackboard1024` with the same ID. | Renumber `HarnessTransform` to an unused ID (e.g. 296) in `FdpRecordingHarness.cs:23`. Also audit `HarnessPosition[202]`, `HarnessVelocity[203]`, `HarnessEntityInfo[205]` against `GlobalComponentIds.AreaQueryBatchData=202`, `EqsTargetPool=203`, `BlueprintBlackboard4096=205`. | **SAFE-AUTO-FIX** |

---

## TH-1 Ledger Status

| TH-1 Entry | Current Status | Notes |
|------------|---------------|-------|
| `LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` (Broken) | Still failing | TH-1 entry accurate |
| `SC_GZ066_2_StructUpdate_RoutesTo_MatchingGizmo` (Broken) | Still failing | Deeper root cause found: also `entity.IsNull` guard in `DataDrivenGizmoSystem` |
| `SC_GZ066_5_MenuAction_RoutesTo_MatchingGizmo_Only` (Broken) | Still failing | Root cause now identified: `entity.IsNull` with gen=0 |
| `Float32_Roundtrip_Via_Persistence` (Broken) | Still failing | TH-1 entry accurate |
| `ReplayModule_SeekToFrameAsync_IsOffMainThread` (Broken) | Still failing | TH-1 entry accurate (design is deliberate synchronous) |
| `DIF_T01`, `DIF_T04`, `DIF_T10` (Broken) | Still failing | TH-1 entry accurate |
| `RoundTrip_MissionPlanQueue_PreservesPhaseData` (Broken) | Still failing | TH-1 entry accurate |
| `MonitorSystem_*` (Broken x2) | Still failing | TH-1 entry accurate; exact bug confirmed in source |
| `WeaponFire*`, `Detonation*`, `DamageAssessed*` struct sizes (Broken x4) | Still failing | TH-1 entry accurate |
| `BicycleModel_NegativeSpeed_ClampsToZero` (Broken) | Still failing | TH-1 entry accurate |
| `RotationToPitchRollDeg_*`, `RotationToHeadingDeg_*` (Broken x5) | Still failing | TH-1 entry accurate |
| `SR_T03_GreaterThan_FindsMultipleFrames` (Broken) | Still failing | TH-1 entry accurate |
| `FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` (Flaky) | Still flaky | TH-1 entry accurate |
| `SC_GZ004_2_Register_UnregisteredComponent_Throws` (Flaky) | Still flaky | TH-1 entry accurate |

---

## Shared Root Cause Clusters

### Cluster 1 — ComponentId collision (IDs 202–205): SAFE-AUTO-FIX
`FdpRecordingHarness.cs` reserves IDs 202-205 for `HarnessPosition`, `HarnessVelocity`, `HarnessTransform`, `HarnessEntityInfo`. Since TH-1, global IDs 202-205 were all claimed: `AreaQueryBatchData=202`, `EqsTargetPool=203`, `BlueprintBlackboard1024=204`, `BlueprintBlackboard4096=205`. The test comment says "IDs 202–205 reserved for this file" but the reservation was never updated. **All 4 harness components need renumbering to 296–299** (or similar unused range). Affects: `BehaviorIngress*` tests (5) in verbose run; may cascade to recording-harness tests if run in other order.

### Cluster 2 — Pitch/Roll sign convention (SimMath vs SimTransformBridgeSystem): NEEDS-DECISION
`SimMath.ToYawPitchRollDeg` defines +pitch = nose DOWN (its doc comment: "+90° = straight down"); `RotationToPitchRollDeg` promises +pitch = nose UP. The bridge must negate `p` before returning. 4 tests share this root cause; fix is 1-line. Decision needed: confirm nose-up = positive is the product convention.

### Cluster 3 — ComponentDiffService null-return contract: NEEDS-DECISION
`DomDiffer.Diff` returns null for identical trees. `ComponentDiffService.ComputeDiff` propagates null rather than wrapping it in a non-modified DiffObject. 3 tests share this root cause. Fix options: wrap in callee (ComponentDiffService returns a sentinel non-modified node) or fix callers (tests accept null = no modification). Shared product impact: all callers of `ComputeDiff` must handle null.

### Cluster 4 — IdAllocationMonitorSystem event subscription on first call: SAFE-AUTO-FIX
Single 1-line insertion in `IdAllocationMonitorSystem.cs:37`. Affects 2 tests.

### Cluster 5 — DataDrivenGizmoSystem entity.IsNull guard: NEEDS-DECISION
`FindGizmo` returns null for `Generation==0` entities (line 542) which blocks routing for all events (`GizmoStructUpdateEvent`, `GizmoMenuActionEvent`) that construct `new Entity(index, 0)`. Production fix requires deciding whether `Index >= 0 && Generation == 0` should be treated as a valid non-null lookup key for index-only events.

---

## Summary Counts

| Category | Count |
|----------|-------|
| Total failing tests (quiet run) | 24 |
| Additional order-dependent failures (verbose) | 5 |
| **A (Stale Test)** | 6 (LocalDiskStorageProvider, 4 struct sizes, ReplayModule async) |
| **B (Fixture Gap)** | 5 (BehaviorIngress* — ComponentId collision, new since TH-1) |
| **Flaky** | 2 (FakeDangerArea, SC_GZ004_2) |
| **C (Real Production Bug)** | 16 |
| SAFE-AUTO-FIX | 9 (LocalDiskStorage, 4 struct sizes, ReplayModule, BicycleModel clamp, GizmoSettingsPersistence type name, IdAllocationMonitor subscription, HarnessTransform IDs) |
| NEEDS-DECISION | 13 (pitch sign, degenerate heading, DataDrivenGizmo routing, ComponentDiffService null contract, MissionPlanQueue registration, RecordingSearchService GreaterThan, FakeDangerArea GC, SC_GZ004_2 static isolation) |
