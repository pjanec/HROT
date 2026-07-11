# Test-Health Ledger

**Suites in scope:** `FDP/Toolkits/Fdp.Toolkits.Tests` · `Hrot/Subsystems/Hrot.SimHost.Tests`
**Baseline batch:** TH-1 (2026-06-10)
**TH-2 batch:** 2026-07-11

## Legend

| Bucket | Meaning |
|--------|---------|
| `Fixed` | Deterministic issue corrected in this batch — test passes unfiltered |
| `Flaky` | Intermittent / order-dependent — passes in isolation, fails in full suite run |
| `Environment` | Deterministic but environment-bound (locale, CRLF, GC timing, etc.) |
| `Broken` | Deterministic failure that is a real bug or stale test — not cheap to fix here |

---

## FDP/Toolkits/Fdp.Toolkits.Tests

### Fixed (cheap deterministic — ComponentId collisions)

| Test | Suite | Bucket | Reason | Resolution |
|------|-------|--------|--------|------------|
| All tests registering `AuditCompB` | Fdp.Toolkits.Tests | **Fixed** | ComponentId(201) collided with GlobalComponentIds.ZoneEnvironmentData=201 | Renumbered AuditCompB to 291 in RegistryAuditTests.cs |
| All tests registering `TestPhysicsCollider` | Fdp.Toolkits.Tests | **Fixed** | ComponentId(212) collided with GlobalComponentIds.INavmeshProvider=212 | Renumbered TestPhysicsCollider to 292 in TestComponents.cs |
| All tests registering `NoSaveVelocity` | Fdp.Toolkits.Tests | **Fixed** | ComponentId(214) collided with GlobalComponentIds.EqsSolverGlobalState=214 | Renumbered NoSaveVelocity to 293 in TestComponents.cs |
| All tests registering `CachedSpeedComponent` | Fdp.Toolkits.Tests | **Fixed** | ComponentId(215) collided with GlobalComponentIds.IPathRegistry=215 | Renumbered CachedSpeedComponent to 294 in TestComponents.cs |
| All tests registering `TestBallisticProjectile` | Fdp.Toolkits.Tests | **Fixed** | ComponentId(211) collided with GlobalComponentIds.ICoverProvider=211 | Renumbered TestBallisticProjectile to 295 in TestComponents.cs |

### Flaky

| Test | Suite | Bucket | Reason | Resolution/Target |
|------|-------|--------|--------|-------------------|
| `FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` | Fdp.Toolkits.Tests | **Flaky** | Zero-alloc GC measurement is JIT-warmup and test-ordering sensitive; passes in isolation | Needs dedicated `[Collection(DisableParallelization)]` or GC.Collect() warmup loop |
| `SC_GZ004_2_Register_UnregisteredComponent_Throws` | Fdp.Toolkits.Tests | **Flaky** | Order-dependent: passes in isolation, fails in full suite when static ComponentTypeRegistry already has `UnregisteredComp[248]` registered from a prior test | Requires test isolation (process-per-test or reset static state between tests) |
| `SC_GZ022_2_Register_UnregisteredType_Throws` | Fdp.Toolkits.Tests | **Flaky** | Order-dependent: passes in isolation, fails in full suite when static ComponentTypeRegistry already has `UnregisteredComp[249]` registered from a prior test | Requires test isolation (process-per-test or reset static state between tests) |

### Broken

| Test | Suite | Bucket | Reason | Resolution/Target |
|------|-------|--------|--------|-------------------|
| `RoundTrip_MissionPlanQueue_PreservesPhaseData` | Fdp.Toolkits.Tests | **Broken** | `MissionPlanQueue` not registered during `Deserialize` → InvalidOperationException | Investigate missing component registration in FdpAutoSerializer fixture |
| `SC_GZ066_2_StructUpdate_RoutesTo_MatchingGizmo` | Fdp.Toolkits.Tests | **Broken** | DataDrivenGizmoSystem not routing events by GizmoTypeId correctly (returns 0 instead of 1) | Real bug in DataDrivenGizmoSystem event routing |
| `SC_GZ066_5_MenuAction_RoutesTo_MatchingGizmo_Only` | Fdp.Toolkits.Tests | **Broken** | DataDrivenGizmoSystem not routing events by GizmoTypeId correctly (returns 0 instead of 1) | Real bug in DataDrivenGizmoSystem event routing |
| `Float32_Roundtrip_Via_Persistence` | Fdp.Toolkits.Tests | **Broken** | GizmoSettingsPersistence: `FormatValue` writes "CsFloat32" but `ParseValue` matches "Float32" — type name mismatch, always returns default 0 | Fix string constant in `ParseValue` to match `SettingType.CsFloat32.ToString()` |
| `ReplayModule_SeekToFrameAsync_IsOffMainThread` | Fdp.Toolkits.Tests | **Broken** | Production deliberately returns `Task.CompletedTask` (on-thread for ECS safety); test expects off-thread execution — stale test | Update test expectation to match current synchronous design |
| `LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` | Fdp.Toolkits.Tests | **Broken** | Test asserts `root/"scenario-alpha"` but production appends `OrchestrationConstants.ScenariosDirectoryName` ("scenarios") subdir | Fix test to expect `root/"scenarios"/"scenario-alpha"` |
| `DIF_T01_IdenticalObjects_IsModifiedFalse` | Fdp.Toolkits.Tests | **Broken** | `ComputeDiff` returns null — real bug in ComponentDiffService | Fix ComponentDiffService.ComputeDiff to return non-null DiffNode |
| `DIF_T04_NumericEpsilon_BelowEpsilonNotModified_AboveEpsilonModified` | Fdp.Toolkits.Tests | **Broken** | `ComputeDiff` returns null — real bug in ComponentDiffService | Fix ComponentDiffService.ComputeDiff |
| `DIF_T10_SameTree_DiffedTwice_NoModificationsSecondTime` | Fdp.Toolkits.Tests | **Broken** | `ComputeDiff` returns null — real bug in ComponentDiffService | Fix ComponentDiffService.ComputeDiff |
| `SR_T03_GreaterThan_FindsMultipleFrames` | Fdp.Toolkits.Tests | **Broken** | `ExecuteSearch` returns 1 frame instead of 3 for GreaterThan(75) on {100,90,80,70,60} — real bug in RecordingSearchService | Fix RecordingSearchService GreaterThan search logic |
| `MonitorSystem_PublishesRequest_WhenLowWaterMarkTriggers` | Fdp.Toolkits.Tests | **Broken** | In `IdAllocationMonitorSystem.Execute`, first call resolves `_manager` but skips `OnLowWaterMark` event subscription (event subscription is in later branch) | Fix IdAllocationMonitorSystem to attach event handler in first Execute call |
| `MonitorSystem_ProcessesResponse_AndAddsBlock` | Fdp.Toolkits.Tests | **Broken** | Same root cause as above — empty `requests` collection leads to index[0] out of range | Fix IdAllocationMonitorSystem event subscription |
| `WeaponFireIntent_IsUnmanaged_AndHasCorrectSize` | Fdp.Toolkits.Tests | **Broken** | sizeof(WeaponFireIntent)=24 vs expected 20 — struct layout changed after test was written | Update test to match actual struct size, or fix struct layout |
| `WeaponFireNotification_IsUnmanaged_AndHasCorrectSize` | Fdp.Toolkits.Tests | **Broken** | sizeof(WeaponFireNotification)=24 vs expected 20 — struct layout changed | Update test or fix struct |
| `DetonationNotification_IsUnmanaged_AndHasCorrectSize` | Fdp.Toolkits.Tests | **Broken** | sizeof(DetonationNotification)=32 vs expected 28 — struct layout changed | Update test or fix struct |
| `DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize` | Fdp.Toolkits.Tests | **Broken** | sizeof(DamageAssessedEvent)=16 vs expected 12 — struct layout changed | Update test or fix struct |
| `BicycleModel_NegativeSpeed_ClampsToZero` | Fdp.Toolkits.Tests | **Broken** | `BicycleModel.Integrate` returns -5 instead of 0 when speed=5, accel=-10, dt=1 — speed not clamped to zero | Fix BicycleModel.Integrate to clamp speed ≥ 0 |
| `RotationToHeadingDeg_DegenerateRotation_Returns0` | Fdp.Toolkits.Tests | **Broken** | Returns 90 instead of 0 for 90° pitch-down rotation — degenerate case not handled | Fix RotationToHeadingDeg degenerate case |
| `RotationToPitchRollDeg_NoseUp30_ReturnsPitchPositive30` | Fdp.Toolkits.Tests | **Broken** | Returns pitchDeg=-30 (sign inverted) instead of +30 — sign convention mismatch | Fix RotationToPitchRollDeg sign convention |
| `RotationToPitchRollDeg_NoseDown30_ReturnsPitchNegative30` | Fdp.Toolkits.Tests | **Broken** | Returns pitchDeg=+30 (sign inverted) instead of -30 — sign convention mismatch | Fix RotationToPitchRollDeg sign convention |
| `RotationToPitchRollDeg_Combined_PitchAndRollIndependent` | Fdp.Toolkits.Tests | **Broken** | pitchDeg=-20 instead of +20 — same sign convention mismatch | Fix RotationToPitchRollDeg sign convention |
| `RotationToPitchRollDeg_PitchedRotation_PitchDegNonZero` | Fdp.Toolkits.Tests | **Broken** | pitchDeg=-20 fails InRange(18,22) — same sign convention mismatch | Fix RotationToPitchRollDeg sign convention |

---

## Hrot/Subsystems/Hrot.SimHost.Tests

### Fixed (TH-2, 2026-07-11 — Hill-Attack 14 failing tests)

| Test | Suite | Bucket | Classification | Root Cause | Resolution |
|------|-------|--------|----------------|------------|------------|
| `HullDownAttackParams_Is40Bytes` | Hrot.SimHost.Tests | **Fixed** | A (Stale Test) | struct grew from 40→56 bytes (added `TargetNetworkId` long + `MaxRounds/RoundsFired/LastObservedAmmo` int fields; Sequential layout pads to largest alignment=8) | Updated assertion to `Assert.Equal(56, ...)` |
| `SC_HA008_1_AimAndFireSpecific_WritesWeaponChannel_AndIncrementsId` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | `Action_AimAndFireSpecific` returns Failure early if entity lacks `WeaponState` component; test fixture did not add it | Added `WeaponState{Ammo=10}` component and set `MaxRounds=1, LastObservedAmmo=-1` in params |
| `SC_HA011_4_IsAreaQueryResolved_ReturnsFailure_WhenReadyWithZeroTargets` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Test wrote to `batch.Results[0]` but `AreaQueryBatchHelper` uses XOR-hash slot formula; slot ≠ 0 | Computed correct slot with XOR formula before writing test data |
| `SC_HA011_5_IsAreaQueryResolved_ReturnsSuccess_AndDoesNotClearRequestId` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Same wrong-slot root cause as SC_HA011_4 | Same fix |
| `SC_HA012_6b_IsWaveCompleted_RunComplete_WhenHashNoLongerMatchesAfterStarted` | Hrot.SimHost.Tests | **Fixed** | C (Production Bug — fixed) | `Condition_IsWaveCompleted` in the run-complete path (`HasStartedRun==1`, `BehaviorHash cleared`) called `SwapRemove` without releasing `BaselineReservedMask`, leaving the slot permanently reserved | Added `s.BaselineReservedMask &= (ushort)~(1 << s.ReturnBaselineSlotIndex[i])` before `SwapRemove` in `HillAttackCommanderNodes.cs` |
| `SC_HA016_1_ParsePlatoonHillAttackParams_DeserializesFromJson` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | `PickableGeoPoint` has `[JsonConverter(typeof(PickableGeoPointArrayConverter))]` requiring `[lat, lon]` arrays; test used `{"x":0,"y":0}` object format causing parse failure and zeroed params | Updated JSON to array format: `{x,y}` → `[y, x]` (Cartesian fallback maps `Longitude→X, Latitude→Y`) |
| `SC_HA016_3_ParsePlatoonHillAttackParams_ResolvesTargetArea_WhenValid` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Same wrong JSON format as SC_HA016_1 causing parse failure → `Entity.Null` returned | Same JSON format fix |
| `SC_HA004_1_AreaQueryPipeline_BrainRequestReachesBack_WithTargets` | Hrot.SimHost.Tests | **Fixed** | B+C (Fixture Gap + Production Bug) | Two bugs: (1) `AddArea` helper in test didn't add `SimTransform` so solver guard `!HasComponent<SimTransform>` fired, publishing empty result; (2) `AreaQueryBrainIngressTranslator.ProcessBatch` used simple modulo slot formula inconsistent with `AreaQueryBatchHelper.ComputeSlot` (XOR hash), so brain ingress wrote to wrong slot | (1) Added `SimTransform` in `AddArea` in `AreaQueryTranslatorTests.cs`; (2) Fixed slot formula in `AreaQueryTranslators.cs` to use XOR hash matching `AreaQueryBatchHelper.ComputeSlot` |
| `SC_HA015_1_FullEndToEnd_CommanderFinishes_WhenAreaIsEmpty` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Two issues: (1) JSON used `{"x":y}` object format → parse failure → `Entity.Null` target → wrong BTree path; (2) `TickOnce` does 3 SwapBuffers/tick so `BehaviorFinishedEvent` from btreeTick gets destroyed by the T-B→T-C swap pair before test can read it | (1) Fixed JSON to array format; (2) Added `TickBTreeOnly` helper that skips EQS pipeline; used it for Tick 2 where no new EQS request is submitted |
| `SC_HA015_2_DispatchWaveWithTargets_AssignsUniqueSlots_ViaBTreeSystem` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Same JSON format issue; additionally `AssignTacticalIntentEvent`s published by BTree in Tick 2 were destroyed by extra SwapBuffers in `TickOnce` before test could read them | Fixed JSON; used `TickBTreeOnly` for Tick 2 to preserve dispatch events |
| `SC_HA015_3_IsWaveCompleted_BurnsSlotOfKilledTank_ViaBTreeSystem` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) + C (Production Bug — fixed) | JSON format prevented params from parsing → TargetAreaEntity=null → no EQS dispatch; additionally the production `BaselineReservedMask` bug (SC_HA012_6b) caused Tick 4 to fail | Fixed JSON; production bug fixed (see SC_HA012_6b) |
| `SC_HA015_4_DispatchWaveWithTargets_AssignsTargetsRoundRobin_ViaBTreeSystem` | Hrot.SimHost.Tests | **Fixed** | B (Fixture Gap) | Same JSON format + TickOnce buffer destruction as SC_HA015_2 | Same fixes as SC_HA015_2 |

### Flaky

| Test | Suite | Bucket | Reason | Resolution/Target |
|------|-------|--------|--------|-------------------|
| `SC_HA004_2_MuscleIngress_UnresolvedAreaEntity_WritesZeroTargetResponse` | Hrot.SimHost.Tests | **Flaky** | Order-dependent: passes in isolation, fails in full suite due to ComponentTypeRegistry contamination from SC_HA004_1 | Requires test isolation or fixture ordering |
| `SC_HA004_3_MuscleEgress_UnresolvedTargetEntity_SkippedInResponse` | Hrot.SimHost.Tests | **Flaky** | Order-dependent: passes in isolation, fails in full suite due to ComponentTypeRegistry contamination from SC_HA004_1 | Requires test isolation or fixture ordering |

### Broken

| Test | Suite | Bucket | Reason | Resolution/Target |
|------|-------|--------|--------|-------------------|
| `RequestAreaQuery_DistinctIds_AndFailsAtCapacity` | Hrot.SimHost.Tests | **Broken** | C-REPORT: `AreaQueryBatchHelper.RequestAreaQuery` generates ID as `((long)entity.Index << 32) \| repo.GlobalVersion`; same entity in same frame yields same `GlobalVersion` → duplicate IDs; also never returns -1 for over-capacity. Changing the ID scheme (include `sourceNodeId`, use per-entity counter, etc.) is non-trivial and may affect distributed routing assumptions | Lead decision needed: add `sourceNodeId` to ID hash, or switch to a per-entity monotonic counter |
| `SC_HA016_2_ParsePlatoonHillAttackParams_ComputesAttackDir_Perpendicular` | Hrot.SimHost.Tests | **Broken** | C-REPORT (spec discrepancy): Test expects AttackDir = left-hand perpendicular of firing line. Production `ParsePlatoonHillAttackParams` computes `AttackDir = normalize(firingCenter - baselineCenter)` (baseline→firing direction). Both are reasonable interpretations; cannot determine which is correct without spec clarification | Lead decision needed: is AttackDir the approach vector (baseline→firing) or the lateral spread vector (perp to firing line)? |
| `Inject_WithValidGuid_WritesInitialUnitSubordinateIntent` | Hrot.SimHost.Tests | **Broken** | JSON designation field: test passes Number but translator expects String | Fix designation type in UnitSubordinateTranslator (int vs string) |
| `Inject_WithUnresolvableGuid_WritesIntentWithZeroNetworkId` | Hrot.SimHost.Tests | **Broken** | Same designation type mismatch as above | Fix designation type in UnitSubordinateTranslator |
| `Extract_WithCommander_ProducesCommanderGuidAndDesignation` | Hrot.SimHost.Tests | **Broken** | designation extracted as String but JSON expects Int32 | Fix designation type in UnitSubordinateTranslator |
| `PathfindingBatchData_DefaultCapacity_Is64` | Hrot.SimHost.Tests | **Broken** | PathfindingBatchData.DefaultCapacity != 64 — constant changed without updating test | Update test constant or restore DefaultCapacity to 64 |
| `C013_ChildOverride_KeyAbsent_AllocatorCalledForChild` | Hrot.SimHost.Tests | **Broken** | AllocateId() called 1 time instead of expected 2 — child allocation logic broken | Investigate CreateEntityRequestSystem child override allocation |
| `StartEpisode_NullRepo_WhenParticipating_Throws` | Hrot.SimHost.Tests | **Broken** | Handler not participating when scenario file exists — PrefetchFiles/participation logic broken | Investigate EpisodeLoadClusterOpHandler.PrepareAsync |
| `GZH011_2_UpdateAndDraw_WithEditing_PublishesOnce_NoDuplicateEcho` | Hrot.SimHost.Tests | **Broken** | Expected 1 event but got 0/2 — LayerControlGizmo echo suppression logic broken | Investigate LayerControlGizmo UpdateAndDraw |
| `SC_ER007_ValidActionName_KnownEntity_PublishesGlobalActionRequestedEvent` | Hrot.SimHost.Tests | **Broken** | Expected 1 GlobalActionRequestedEvent but got 0 — ContextActionIngressSystem routing broken | Investigate ContextActionIngressSystem.Execute |
| `Extract_EntityWithActiveMissionPlan_ReturnsMissionPlanDomObject` | Hrot.SimHost.Tests | **Broken** | NullReferenceException — MissionPlanTranslator.Extract returns null | Investigate MissionPlanTranslator |
| `Inject_WithExtractedDom_RestoresActivePlanAndQueue` | Hrot.SimHost.Tests | **Broken** | BehaviorId restored as TimerElapsed instead of BehaviorFinished — phase behavior mismatch | Investigate MissionPlanTranslator.Inject phase behavior |
| `SimHostCoreLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException` | Hrot.SimHost.Tests | **Broken** | System count mismatch — system added/removed without updating test count | Update expected system count in test |
| `Extract_WithBehaviorRemapper_ReplacesNetworkIdInBehaviorParams` | Hrot.SimHost.Tests | **Broken** | Component type ID 183 not registered — missing component registration in fixture | Add missing component registration to StagingEntityExtractor test fixture |
| `Extract_TranslatorConsumedComponent_IsExcludedFromInitialComponents` | Hrot.SimHost.Tests | **Broken** | Component type ID 28 not registered — missing component in fixture | Add missing component registration to StagingEntityExtractor test fixture |
| `Extract_ChildWithNetworkIdentity_CarriesPreAllocatedIdToOverrides` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `SC_GZ057_4_Draw_WithVehicleParams_EmitsNonZeroDimensions` | Hrot.SimHost.Tests | **Broken** | Expected 3 primitives but got 8 — SimHostEntityPresentationGizmo emitting extra primitives | Investigate SimHostEntityPresentationGizmo.Draw with VehicleParams |
| `SC_GZ057_3_Draw_EmitsSemanticShapeWithMatchingAnchorIndex` | Hrot.SimHost.Tests | **Broken** | Expected SemanticShape primitive but got Box2D — wrong shape type emitted | Investigate SimHostEntityPresentationGizmo.Draw |
| `CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly` | Hrot.SimHost.Tests | **Broken** | System count mismatch (Expected 3, Actual 2) — system added/removed from CgfLogicPack | Update expected count in test |
| `CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException` | Hrot.SimHost.Tests | **Broken** | System count mismatch (Expected 3, Actual 2) — same root cause | Update expected count in test |
| `CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup` | Hrot.SimHost.Tests | **Broken** | System count mismatch (Expected 21, Actual 20) — system removed without updating test | Update expected count in test |
| `SC_HA004_1_AreaQueryPipeline_BrainRequestReachesBack_WithTargets` | Hrot.SimHost.Tests | **Broken** | Solver does not mark result IsReady — AreaQuerySolverSystem pipeline not completing | Investigate AreaQuerySolverSystem |
| `LoadExistingScenario_SpawnsCorrectEntityCount` | Hrot.SimHost.Tests | **Broken** | No scenario file found — PrefetchFiles not called before LoadingEdit | Fix test to call PrefetchFiles first, or mock scenario file loading |
| `Commit_DoesNotBlockLongerThan50ms` | Hrot.SimHost.Tests | **Broken** | No scenario file found — same as above | Fix test setup |
| `BranchedRecording_CapturesHistoricalStateAsKeyframe` | Hrot.SimHost.Tests | **Broken** | BranchedRecording pipeline fails — likely real bug in branch recording or keyframe capture | Investigate FullBranchPipeline |
| `InitializeEmbedded_DomainZero_UsesDomainZero` | Hrot.SimHost.Tests | **Broken** | `Call RegisterSystems before RegisterProviders` — ordering bug in EngineBackedNavigationModule | Fix EngineBackedNavigationModule.RegisterProviders ordering |
| `InitializeHeadless_NodeIdZero_FallsBackToLegacyConstant` | Hrot.SimHost.Tests | **Broken** | `Call RegisterSystems before RegisterProviders` — same ordering bug | Fix EngineBackedNavigationModule.RegisterProviders ordering |
| `InitializeHeadless_NodeIdTen_ResolvedToTen` | Hrot.SimHost.Tests | **Broken** | `Call RegisterSystems before RegisterProviders` — same ordering bug | Fix EngineBackedNavigationModule.RegisterProviders ordering |
| `OnLoad_RegistersCycloneNetworkCleanupSystem` | Hrot.SimHost.Tests | **Broken** | `Call RegisterSystems before RegisterProviders` — same ordering bug | Fix EngineBackedNavigationModule.RegisterProviders ordering |
| `SimHost_Tick_DoesNotThrow` | Hrot.SimHost.Tests | **Broken** | `Call RegisterSystems before RegisterProviders` — same ordering bug | Fix EngineBackedNavigationModule.RegisterProviders ordering |
| `Serialize_ThenDeserialize_ReconstitutesHierarchy` | Hrot.SimHost.Tests | **Broken** | Unknown component type 'UnitSubordinate' on deserialization — component not registered in fixture | Add UnitSubordinate component registration to HierarchySerializationIntegrationTests |
| `Extract_EntityWithPartMetadata_IsFilteredOutFromResults` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_InitialPassengersIntent_RemapsPassengerNetworkIdsViaOldToNewMap` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_EntityWithoutNetworkIdentity_NoExceptionReturnsSingleRequest` | Hrot.SimHost.Tests | **Broken** | Assert.Contains failure — StagingEntityExtractor single-entity without NetworkIdentity path broken | Investigate StagingEntityExtractor Pass 1 logic |
| `SC_HA012_6_IsWaveCompleted_NoRemove_WhenHasNotStartedYet` | Hrot.SimHost.Tests | **Broken** | IsWaveCompleted incorrectly removes entry when HasStartedRun==0 | Fix IsWaveCompleted entry removal guard |
| `SetSingletonManaged_TkbDatabase_SameInstanceAfterRegisterAll` | Hrot.SimHost.Tests | **Broken** | Component type ID 45 not registered — TkbDatabase not in SimHostComponentRegistry | Add TkbDatabase component registration to SimHostComponentRegistry |
| `Extract_SingleRootEntity_ReturnsSingleRequestWithCorrectTkbType` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_EntityWithNetworkIdentity_SetsPreAllocatedNetworkId` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_WithChildEntity_PopulatesChildComponentOverrides` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_InitialPassengersIntent_PreservesUnknownNetworkId` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_InitialUnitSubordinateIntent_PreservesUnknownCommanderNetworkId` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_InitialUnitSubordinateIntent_RemapsCommanderNetworkIdViaOldToNewMap` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |
| `Extract_ChildEntity_InitialPassengersIntent_NetworkIdIsRemapped` | Hrot.SimHost.Tests | **Broken** | EditablePolyline managed component not registered — missing in fixture | Add RegisterManagedComponent<EditablePolyline>() to StagingEntityExtractor fixture |

---

## Summary Counts

| Suite | Fixed | Flaky | Environment | Broken | Total Marked/Fixed |
|-------|-------|-------|-------------|--------|-------------------|
| Fdp.Toolkits.Tests | 5 (TH-1: ComponentId collision renumbers) | 3 | 0 | 19 | 27 |
| Hrot.SimHost.Tests | 12 (TH-2: 12 Hill-Attack tests) | 2 | 0 | 43 | 57 |
| **Total** | **17** | **5** | **0** | **62** | **84** |

### TH-2 C-REPORT Summary (lead decision required)

| Test | Production Issue | Proposed Fix Options |
|------|-----------------|---------------------|
| `RequestAreaQuery_DistinctIds_AndFailsAtCapacity` | ID = `(entity.Index << 32) \| GlobalVersion`; same entity+frame always yields same ID; no capacity-exceeded return | Option A: include `sourceNodeId` in ID hash; Option B: per-entity monotonic counter in `AreaQueryBatchData` |
| `SC_HA016_2_ParsePlatoonHillAttackParams_ComputesAttackDir_Perpendicular` | `AttackDir = normalize(firingCenter − baselineCenter)` (approach vector); test expects left-hand perpendicular of firing line | Clarify spec: approach vector (current code) or lateral spread vector (test expectation) |
