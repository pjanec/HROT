# BATCH-02 Report

**Batch:** BATCH-02 (BSA-101 + BSA-202)  
**Date:** 2026-06-09  
**Status:** ✅ COMPLETE

---

## Summary

Marked all three `BlueprintBlackboard{1024,4096,16384}` components `[DataPolicy(DataPolicy.NoSave)]`, created `BlueprintAssignmentDto` + `InitialBlueprintsIntent` with a unique `ComponentId` (187), fixed the compiler emitter to populate `BlueprintDefinition.AssetId`, and built `BlueprintStateTranslator : IEntityScenarioTranslator` registered in `HrotScenarioSerializerFactory`. All 4 tasks implemented with tests.

---

## Q1: What issues did you encounter? How did you resolve them?

1. **`GetCustomAttribute<T>()` not found on `Type`**. The `Fdp.Toolkits.Tests` project doesn't have implicit `System.Reflection` in its global usings. **Fix:** Added `using System.Reflection;` to `BlueprintBlackboardNoSaveTests.cs`.

2. **`EntityRepository.GetManagedComponent<T>()` doesn't exist**. Managed component reads use `ISimulationView.GetManagedComponentRO<T>()` via explicit interface implementation. **Fix:** Changed to `((ISimulationView)_repo).GetManagedComponentRO<T>(entity)` in both `BlueprintStateTranslatorTests.cs` and `GenesisIntentComponentsTests.cs`.

3. **`IGuidResolver` method names differ from stub**. The interface uses `Resolve(Entity)` and `Resolve(string)`, not `EntityToGuid`/`GuidToEntity`. **Fix:** Renamed methods in `StubGuidResolver`.

4. **`System.Text.Json` serializes null `Overrides` by default**. The DTO round-trip test expected null Overrides to be omitted. **Fix:** Added `JsonSerializerOptions` with `DefaultIgnoreCondition = WhenWritingNull`.

5. **`fixed` + generic `ref readonly` pointer extraction complexity**. Using a generic method with conditional type checks and `fixed` on ref readonly structs was fragile. **Fix:** Replaced with three explicit per-tier extraction methods (`ExtractTier1024`, `ExtractTier4096`, `ExtractTier16384`) following the established `BlueprintInstanceService.GetTierMemoryAndMeta` pattern.

6. **Partial CSharpEmitter.cs linter change**. The linter reverted `AssetId = new Guid(...)` line addition. **Fix:** Re-applied the edit.

---

## Q2: What design decisions did you make beyond the spec?

1. **`BlueprintRegistry?` is optional in the factory and translator**. Made `blueprintRegistry` parameter nullable with default `null` so callers without a BlueprintRegistry (CGF, SimHostNodeBootstrapper, ReplayBrowser, tests) continue to compile without changes. The translator handles `null` gracefully: legacy key black-holing works without registry; Extract emits `Guid.Empty` for assignments when registry is unavailable; `GetOutputDomKeys` always claims all 4 keys.

2. **Explicit per-tier extraction methods**. Used `ExtractTier1024/4096/16384` with direct `fixed (byte* memory = bb.Memory)` calls instead of a generic approach, following the `BlueprintInstanceService.GetTierMemoryAndMeta` pattern for clarity and safety.

3. **`InitialBlueprintsIntent` uses `HrotComponentIds` (byte-based)**. Followed the existing pattern for genesis intent components (IDs 177-187 in the Hrot application block), rather than adding to `GlobalComponentIds` (which is for toolkit-level components). This mirrors `InitialPassengersIntent`, `InitialUnitSubordinateIntent`, etc.

---

## Q3: Does `GetOutputDomKeys()` alone route legacy keys during deserialization, or did you need `CanTranslate` changes too? What did you find in `ScenarioSerializer.cs`?

**Answer:** `GetOutputDomKeys()` alone IS sufficient. Verified in `ScenarioSerializer.Deserialize` (lines 377-391):

```
foreach (var translator in _translators)
{
    translator.Inject(repo, entity, scenarioData, loadResolver);  // line 380
    foreach (var name in BuildConsumedNames(translator.GetConsumedComponentsMask()))
        translatorHandled.Add(name);
    foreach (var key in translator.GetOutputDomKeys())           // line 389
        translatorHandled.Add(key);
}
// Auto-serializer handles the rest (line 393-407):
// If a key is NOT in translatorHandled, FdpAutoSerializer tries to process it
// and throws InvalidOperationException for unknown component type names (line 401-403)
```

The routing works as designed:
- During `Inject`, every translator gets called with ALL scenario data keys; it self-filters
- After all translators run, `GetOutputDomKeys()` populates `translatorHandled`
- Any key NOT in `translatorHandled` falls through to `FdpAutoSerializer`, which throws
- By claiming `"BlueprintBlackboard1024"`/`"4096"`/`"16384"` in `GetOutputDomKeys()`, these legacy keys are added to `translatorHandled` and never reach the auto-serializer
- `CanTranslate` is only checked during EXTRACT (save path), never during INJECT (load path)

No `CanTranslate` changes were needed.

---

## Q4: List all callers of `HrotScenarioSerializerFactory.Build()` that you updated.

Since `blueprintRegistry` is optional (defaults to `null`), only one caller needed updating:

| Caller | File | Updated? |
|--------|------|----------|
| `EditorSubsystem.cs:690` | `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | ✅ Yes — passes `_blueprintRegistry` |
| `CgfSubsystem.cs:404` | `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | ❌ Uses default `null` |
| `SimHostNodeBootstrapper.cs:175` | `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs` | ❌ Uses default `null` |
| `StrideNodeBootstrapper.cs:228` | `Hrot/Subsystems/Hrot.StrideMock/StrideNodeBootstrapper.cs` | ❌ Uses default `null` |
| `EditorBootstrap.cs:37` | `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | ❌ Uses default `null` |
| `ReplayBrowserSubsystem.cs:142` | `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` | ❌ Uses default `null` |
| `HierarchySerializationIntegrationTests.cs:86` | `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/` | ❌ Uses default `null` |
| `UrbanCombatFileLifecycleTests.cs:182` | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` | ❌ Uses default `null` |
| `SharedApplicationBootstrapperTests.cs:85` | `Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/` | ❌ Uses default `null` |

All 9 callers compile and work. The `EditorSubsystem` is the only production caller with a `BlueprintRegistry` available at serializer-build time.

---

## Q5: What value did you assign as `GlobalComponentIds.InitialBlueprintsIntent`?

Assigned `HrotComponentIds.InitialBlueprintsIntent = 187` — the next available byte after `CanvasContextMenuState = 186` in the genesis intent components block (177–187). This is consistent with the existing `HrotComponentIds` byte-based allocation scheme.

---

## Q6: Did the golden emit snapshots need updating? List the files changed.

Yes. Three Instance golden snapshot files were updated to include the new `AssetId` line:

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/InstanceCounter.cs.txt`
   - Added: `AssetId = new Guid("00000002-0000-0000-0000-000000000001"),`
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/HealthRegen.cs.txt`
   - Added: `AssetId = new Guid("00000003-0000-0000-0000-000000000001"),`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/DoorActor.cs.txt`
   - Added: `AssetId = new Guid("00000006-0000-0000-0000-000000000001"),`

This is the only snapshot regeneration allowed per the instructions (Test 6).

---

## Q7: Suggested commit message

```
feat: BSA-101 NoSave blackboard + BSA-202 BlueprintStateTranslator + AssetId emit fix

- Mark BlueprintBlackboard{1024,4096,16384} [DataPolicy(DataPolicy.NoSave)]
- Create BlueprintAssignmentDto (Fdp.Toolkit.Blueprints) + InitialBlueprintsIntent
  ([Transient], HrotComponentIds.InitialBlueprintsIntent = 187)
- Fix CSharpEmitter to populate BlueprintDefinition.AssetId from asset.AssetId
- Create BlueprintStateTranslator : IEntityScenarioTranslator
  - Extract: scan all tiers, emit BlueprintAssignments array of AssetIds
  - Inject: parse assignments → set InitialBlueprintsIntent
  - Legacy keys (BlueprintBlackboard1024/4096/16384): black-holed via GetOutputDomKeys
- Register translator in HrotScenarioSerializerFactory.Build()
  (BlueprintRegistry? param, defaults null for non-editor callers)
- Register InitialBlueprintsIntent in GenesisIntentRegistry
- Update 3 Instance golden emit snapshots (expected AssetId addition)
- 12 tests: reflection, serialization exclusion, DTO round-trip, intent round-trip,
  AssetId emit verification, extract round-trip, inject→intent, legacy black-hole,
  GetOutputDomKeys, CanTranslate, registry AssetId cross-check

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

---

## Test Results

### New tests (all passing)

| Test # | File | Test Name | Status |
|--------|------|-----------|--------|
| 1 | `Fdp.Toolkits.Tests/Scenario/BlueprintBlackboardNoSaveTests.cs` | `BlueprintBlackboard1024_HasDataPolicyNoSave` | ✅ |
| 1 | same | `BlueprintBlackboard4096_HasDataPolicyNoSave` | ✅ |
| 1 | same | `BlueprintBlackboard16384_HasDataPolicyNoSave` | ✅ |
| 2 | same | `Serialization_ExcludesBlueprintBlackboard1024` | ✅ |
| 3 | `Fdp.Toolkits.Tests/Scenario/BlueprintAssignmentDtoTests.cs` | `Dto_RoundTrip_WithNullOverrides_OmitsOverridesKey` | ✅ |
| 3 | same | `Dto_RoundTrip_WithPopulatedOverrides_PreservesValues` | ✅ |
| 4 | `Hrot.SimHost.Tests/GenesisIntentComponentsTests.cs` | `InitialBlueprintsIntent_RegisterManagedComponent_DoesNotThrow` | ✅ |
| 4 | same | `InitialBlueprintsIntent_ComponentTypeRegistry_ReturnsCorrectType` | ✅ |
| 4 | same | `InitialBlueprintsIntent_HasTransientDataPolicy` | ✅ |
| 4 | same | `InitialBlueprintsIntent_RoundTrip_SetThenGet_ReturnsSameData` | ✅ |
| 5 | `Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/InstanceEmitGoldenTests.cs` | `Instance_EmitContainsAssetId` | ✅ |
| 5 | same | `Instance_EmitAssetId_MatchesAssetGuid` | ✅ |
| 6 | same | `Instance_EmitMatchesGoldenSource(InstanceCounter)` | ✅ |
| 6 | same | `Instance_EmitMatchesGoldenSource(HealthRegen)` | ✅ |
| 6 | same | `Instance_EmitMatchesGoldenSource(DoorActor)` | ✅ |
| 7 | `Hrot.SimHost.Tests/BlueprintStateTranslatorTests.cs` | `Extract_TwoBlueprintsAttached_ReturnsCorrectAssignments` | ✅ |
| 8 | same | `Inject_WithAssignmentsData_SetsInitialBlueprintsIntent` | ✅ |
| 8 | same | `Inject_WithMultipleAssignments_SetsAllEntries` | ✅ |
| 9 | same | `Inject_LegacyBlackboardKey_DoesNotThrow` | ✅ |
| 9 | same | `Inject_LegacyBlackboardKey_DoesNotAddAnyBlackboardComponent` | ✅ |
| 10 | same | `GetOutputDomKeys_ReturnsAllFourKeys` | ✅ |
| 11 | same | `CanTranslate_EntityWithBlackboard1024_ReturnsTrue` | ✅ |
| 11 | same | `CanTranslate_EntityWithoutBlackboard_ReturnsFalse` | ✅ |
| 11 | same | `CanTranslate_EntityWithBlackboard4096_ReturnsTrue` | ✅ |
| 12 | same | `AssetId_RegisteredInstanceBlueprint_HasNonEmptyAssetId` | ✅ |

### Pre-existing failures (all projects, confirmed unrelated)

| Project | Pass | Fail | Pre-existing |
|---------|------|------|--------------|
| `Hrot.Blueprints.Tests` | 1683 | 8 | ✅ All compiler golden/PDB/ALC/perf (same as BATCH-01 baseline) |
| `Hrot.SimHost.Tests` | 582 | 43 | ✅ Various — HillAttack, Checkpoint, SimHost init, etc. (none reference BlueprintStateTranslator or NoSave) |
| `Fdp.Toolkits.Tests` | 1839 | 33 | ✅ Navigation, Combat, ReplayBrowser, Gizmos (none related to blueprints) |

**0 net-new failures in all touched projects.**

### Build commands used

```bash
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-restore --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-restore --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-restore --no-build
```
