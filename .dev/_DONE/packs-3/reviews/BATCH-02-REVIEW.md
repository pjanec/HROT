# BATCH-02 Review — Editor Preview/Rewind, Urban Combat File Lifecycle & Zone Foundation

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Review Date:** 2026-04-05  
**Verdict:** ✅ APPROVED — with two notable FDP bug fixes confirmed

---

## Summary

All 4 tasks implemented and verified. Solution builds clean (0 errors). All new tests pass (U003
+ U004 integration, Z001 + Z002 unit). Three real FDP simulation bugs were correctly identified
and fixed as a necessary side-effect of making PACK3-U004 pass in the distributed cluster.

---

## Scope Check

| Task | Implemented | Verified |
|------|-------------|---------|
| PACK3-U003: `EditorPreviewAndSaveIntegrationTests` | ✅ | ✅ 14-step sequence passes in 183 ms |
| PACK3-U004: `UrbanCombatFileLifecycleTests` | ✅ | ✅ all 4 latches fire in 3 s |
| PACK3-Z001: `ZoneEnvironmentData` + `CarKinematicsSystem` refactor | ✅ | ✅ 2 unit tests + regressions |
| PACK3-Z002: DTOs + `HrotJsonOptions` | ✅ | ✅ 2 unit tests + 101/101 Hrot.Map.Common.Tests |

---

## Design Alignment

- **PACK3-Z002**: DTOs (`HrotScenarioEnvelopeDto`, `ScenarioHeaderDto`, `ZoneDefinitionDto`,
  `ZoneObstacleDto`) match the spec exactly — `RoadNetworkPath`, `TerrainDatabaseId`,
  `Obstacles` fields present; zero `[JsonPropertyName]` attributes (verified via grep). ✅
- **PACK3-Z001**: `ZoneEnvironmentData` struct in `FDP.Toolkit.CarKinem`, component ID 38 (within
  Toolkit expansion block 20–79). `CarKinematicsSystem.OnUpdate` reads with `default` fallback;
  never returns early on missing singleton. ✅
- **PACK3-U003**: All 14 spec steps present and tested. `[Collection("EditorOfflineTests")]`
  applied. `IDisposable.Dispose` deletes temp file. ✅
- **PACK3-U004**: Uses `UrbanCombatValidator` from BATCH-01 — shared validator validated. Temp
  staging dir properly cleaned on dispose. ✅

---

## FDP Bug Fixes Assessment

The developer found and fixed three FDP simulation bugs discovered during PACK3-U004. These are
correctness fixes that improve the overall reliability of the engine.

### Bug Fix 1 — Capability Bypass in Dispatcher Systems

**Files:** `WeaponDispatcherSystem.cs`, `LocomotionDispatcherSystem.cs`  
**Fix:** Removed `&& channel.Status == NodeStatus.Running` from capability guards.

**Assessment:** Correct fix. The intent of the guard is to prevent entities without the
required capability from firing/moving regardless of channel state. The `NodeStatus.Running`
check was incorrectly giving entities a free pass on first activation (when `Status == Inactive`).
This caused the APC passengers to fire immediately from their spawn position — corrupting
the Urban Combat narrative. Fix is minimal and non-breaking. ✅

### Bug Fix 2 — System Execution Order in Cluster Mode Flat Group

**File:** `BallisticsSystem.cs`  
**Fix:** Added `[UpdateAfter(typeof(HitResolutionSystem))]`.

**Assessment:** Correct fix. The developer's root-cause analysis (Kahn's topological sort
placing `HitResolutionSystem` after `BallisticsSystem` in the flat `_kernelGroup`) is accurate
and well-documented. The fix correctly establishes the dependency: `Ballistics` must run after
`HitResolution` has cleared the hit-result batch from the previous frame. The fix is scoped to
the `[UpdateAfter]` attribute and does not change any logic. ✅

**Note for next batch**: The developer's observation about `[UpdateAfter]`/`[UpdateBefore]`
discipline is valuable — record as P2 debt to audit other systems for missing ordering
attributes when used in the flat-group pattern.

---

## Test Quality Assessment

- **U003**: 14 sequential assertions test the exact Preview/Rewind lifecycle. Assertions check
  actual position values (`x == 100f`, `x == 999f`), not string existence. ✅
- **U004**: `Assert.True(success)` where `success` is set by validator returning `true` after all
  4 latches fire. The validator itself is already proven by BATCH-01 unit tests. ✅
- **Z001**: Tests assert that vehicle physics actually run (no skip) when singleton is absent.
  Tests with singleton assert navigation tick succeeds. ✅
- **Z002**: Round-trip test verifies `Zones["urban_combat_zone"].Obstacles[0].X == 50`. Case
  insensitivity test verifies PascalCase JSON deserialises correctly. ✅

---

## Pre-existing Test Failures

The developer reported 6 pre-existing failures in `Hrot.ClusterRunner.Integration.Tests` and
`Hrot.ClusterRunner.Tests`. These are confirmed unrelated to BATCH-02 (DDS timeout,
timing-sensitive time-mode tests, replay executor exit code). Record as P3 debt for future
investigation.

---

## Issues Found During Review

### P2 (record in DEBT-TRACKER) 
1. **System ordering audit**: `[UpdateAfter]`/`[UpdateBefore]` attributes should be audited
   across all FDP systems to ensure correctness when instantiated in a flat `_kernelGroup`.
   The flat-group pattern is used in cluster mode (`SimHostApp`) — undocumented ordering
   dependencies are latent bugs.

### P3 (record in DEBT-TRACKER)
2. **Pre-existing integration test failures** (6 tests): DDS timeout, time-mode timing,
   replay executor — tracked separately, investigation deferred.
3. **`ZoneEnvironmentData` placed in `FDP.Toolkit.CarKinem`**: The spec suggested
   `FDP.Toolkit.Geographic` or `Fdp.Kernel.Environment`. Current location in `CarKinem` is
   acceptable (CarKinem depends on Geographic, not the other way around) but the struct is
   conceptually geographic rather than kinematic. Low impact; can be moved in a future cleanup.

---

## Debt Tracker Entries

| Priority | Description | Source |
|----------|-------------|--------|
| P2 | FDP system ordering audit: all systems should have explicit `[UpdateAfter]`/`[UpdateBefore]` where cluster-mode flat `_kernelGroup` changes topological sort order vs standalone mode. Regression hazard for any multi-system interaction. | BATCH-02 (BallisticsSystem bug) |
| P3 | Pre-existing integration test failures: `SwitchToExternal_SpawnCommand`, `AllSubsystems_TransitionToOperatingLive`, `RecordAndReplaySeek`, and 3 time-mode timing tests. Investigate in a future maintenance batch. | BATCH-02 developer report |
| P3 | `ZoneEnvironmentData` struct lives in `FDP.Toolkit.CarKinem` but is conceptually geographic. Move to `FDP.Toolkit.Geographic` in a future cleanup when scope allows. | BATCH-02 review |

---

## Suggested Git Commit Messages

**FDP submodule commit:**
```
feat(packs-3): PACK3-Z001 ZoneEnvironmentData + CarKinematicsSystem refactor + FDP bug fixes

- Add ZoneEnvironmentData ECS singleton struct (ComponentId 38)
- Refactor CarKinematicsSystem to read ZoneEnvironmentData singleton (remove ctor param)
- Update all call sites (GroundKinematicsModule, HeadlessCarKinemApp, example scenarios, etc.)
- Fix WeaponDispatcherSystem/LocomotionDispatcherSystem capability bypass on first activation
- Fix BallisticsSystem [UpdateAfter(HitResolutionSystem)] execution order in flat kernelGroup

Tests: CarKinemTests +2, UrbanCombatFileLifecycleTests passes (was blocked by all 3 bugs)
```

**Parent repo commit:**
```
feat(packs-3): PACK3-U003/U004/Z002 + BATCH-02 tracking

- Add EditorPreviewAndSaveIntegrationTests (14-step Preview/Rewind lifecycle)
- Add UrbanCombatFileLifecycleTests (Orchestrator+SimHost+CGF, 4-latch validation)
- Add TargetMemoryTranslator, PassengerBufferTranslator, WeaponChannelTranslator for U004
- Add HrotScenarioEnvelopeDto, ScenarioHeaderDto, ZoneDefinitionDto, ZoneObstacleDto, HrotSerializerOptions
- Update FDP submodule ref (ZoneEnvironmentData + BallisticsSystem + dispatcher fixes)
- Update TASK-TRACKER.md (U003, U004, Z001, Z002 done)

Tests: all new tests pass; 101/101 Hrot.Map.Common.Tests
```

---

## Next Actions

- ✅ Update TASK-TRACKER.md: PACK3-U003, PACK3-U004, PACK3-Z001, PACK3-Z002
- ✅ Add P2/P3 debt entries to DEBT-TRACKER.md
- ✅ Commit FDP submodule and parent repo
- ➡️ Create BATCH-03: PACK3-Z003, PACK3-Z004, PACK3-Z005, PACK3-Z006
