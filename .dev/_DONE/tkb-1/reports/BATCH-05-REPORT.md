# BATCH-05 Report — Phase 6: ECS Translators and Wiring

**Workstream:** tkb-1  
**Batch:** 05  
**Tasks:** TKB-012, TKB-013, TKB-014  
**Status:** COMPLETE

---

## Files Created

| File | Description |
|------|-------------|
| `FDP/Engine/Fdp.Core/Abstractions/ITkbEntityTranslator.cs` | TKB-012: New interface |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs` | TKB-013: Reference translator implementation |
| `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Tkb/VehicleKinematicsTkbTranslatorTests.cs` | TKB-013: 7 unit tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TranslatorWiringTests.cs` | TKB-014: 3 wiring tests |

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/BlueprintApplicationSystem.cs` | Added `_translators` field and optional parameter; replaced stub comment with loop |
| `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs` | Added `_translators` field and optional parameter; replaced stub comment with loop |
| `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs` | Added `_translators` field before `onEntitySpawned`; replaced stub comment with loop |
| `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs` | Added `_translators` field and optional parameter; threaded to `BlueprintApplicationSystem` |
| `FDP/Toolkits/Fdp.Toolkits/Replication/ReplicationLogicModule.cs` | Added `_translators` field and optional parameter; threaded to `GhostPromotionSystem` |

---

## Test Results

### New tests (10 total)

**VehicleKinematicsTkbTranslatorTests** (7 tests):

```
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 41 ms
```

Tests:
1. `GetConsumedDescriptors_ReturnsVehicleParametersDto`
2. `Inject_WithAllComponentsRegistered_AddsVehicleParams`
3. `Inject_WithAllComponentsRegistered_AddsVehicleState`
4. `Inject_WithAllComponentsRegistered_AddsNavState`
5. `Inject_WithAllComponentsRegistered_AddsPhysicsCollider`
6. `Inject_TemplateWithoutVehicleParametersDto_AddsNoComponents`
7. `Inject_WorldWithoutVehicleParamsRegistered_DoesNotThrow`

**TranslatorWiringTests** (3 tests):

```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 69 ms
```

Tests:
1. `BlueprintApplicationSystem_WithTranslator_CallsInjectOnKnownTkbType`
2. `NetworkSpawningSystem_WithTranslator_CallsInjectOnSpawn`
3. `GhostPromotionSystem_WithEmptyTranslators_PromotesWithoutException`

### Backward compatibility

Existing tests untouched and all passing:

```
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19
  (GhostProtocolTests, BlueprintApplicationSystemTests, SpawnSystemTests, SubEntityTests)
```

### Tkb test suite (includes all new tests)

```
Passed!  - Failed: 0, Passed: 109, Skipped: 0, Total: 109
```

### Full regression

```
Failed!  - Failed: 59, Passed: 1144, Skipped: 0, Total: 1203
```

The 59 failing tests are pre-existing failures unrelated to this batch (confirmed by the
fact that `IdAllocationTests`, `AimAndFireExecutorTests`, `NavigationIntentBridgeSystemTests`,
and `FdpAutoSerializerFixedBufferTests` were already failing before batch-05 changes). All
tests in the domains touched by this batch pass.

### Build

```
Build succeeded.  0 Error(s)
```

---

## Deviations

None. Implementation follows the instructions exactly.

---

## P2/P3 Issues

- **D-002 (P2):** `WithHeavyMemory` / `Blackboard1024` restoration remains out of scope (noted in batch instructions).
- **D-003 (P2):** `UrbanAmbushIntegrationTests` remain failing — requires combat/HSM/AI component translators not part of TKB-013. The wiring in TKB-014 is the prerequisite but not sufficient on its own.
- **Pre-existing failures (59):** Not introduced by this batch. Examples: `IdAllocationTests`, `AimAndFireExecutorTests`, `NavigationIntentBridgeSystemTests`, `FdpAutoSerializerFixedBufferTests`.
