# MOD1-BATCH-07 Report

**Batch:** MOD1-BATCH-07  
**Developer:** GitHub Copilot  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CT-MOD1-M | ✅ Complete | Reverted FastBTree submodule: deleted `BTreeActionNode.cs`, restored `IAIContext.cs` stubs |
| CT-MOD1-K | ✅ Complete | Replaced abstract base classes with static helpers; added no-op IAIContext stubs to `BTreeContext` |
| CT-MOD1-L | ✅ Complete | `LosRequestBatchingSystem` now implements both `ComponentSystem` and `IModuleSystem`; all 4 systems registered via `ISystemRegistry` |
| MOD1-P7T1 | ✅ Complete | `EClampingMode` wire enum + `GroundClampingOverride` DDS struct in `Hrot.NED.Descriptors`; engine-side `EClampingMode` in `Fdp.Modules.Geographic` |
| MOD1-P7T2 | ✅ Complete | `GroundClampingConfig`, `GroundClampingState`, `TerrainQueryBatchData` + supporting request/result structs; IDs 77–79 allocated in `GlobalComponentIds` |
| MOD1-P7T3 | ✅ Complete | `ITerrainProvider` interface created; `GroundClampingOverrideTranslator` ingress-only translator (ordinal 66) registered in `IgApplication` |
| MOD1-P7T4 | ✅ Complete | All 4 execution systems created: `TerrainQueryInitializationSystem`, `TerrainQuerySubmitSystem`, `TerrainQuerySolverSystem`, `TerrainQueryResolutionSystem` |
| MOD1-P7T5 | ✅ Complete | `IgGroundClampingModule` packages all 4 systems; `TransformSyncSystem` lerps `CurrentZOffset` and applies to visual Z; `IgApplication.InstallGroundClamping(ITerrainProvider)` bootstrapper added |

---

## 🧪 Testing Results

**All new tests passed; no regressions in any previously-passing suite.**

| Suite | Passed | Total | New Tests |
|-------|--------|-------|-----------|
| `Fdp.Toolkit.Geographic.Tests` | 23 | 23 | +19 (from 4 baseline to 23) |
| `Hrot.IG.Tests` | 300 | 300 | +4 (TransformSync Z-offset, IgGroundClampingModule) |
| `FDP.Toolkit.Behavior.Tests` | 53 | 53 | — |
| `FDP.Toolkit.Physics.Tests` | 21 | 21 | — |
| `FDP.Toolkit.Navigation.Tests` | 32 | 32 | — |
| `FDP.Toolkit.Perception.Tests` | 25 | 25 | — |

**New test scenarios (P7 tasks):**
- [x] `GroundClampingConfigTests` — 6 truth-table cases for `IsClampingActive` (ForceOn/Off/Default × grounded/ungrounded)
- [x] `TerrainQueryBatchDataTests` — native-array alloc/dispose lifecycle
- [x] `TerrainQuerySubmitSystemTests` — ForceOff skip, Default-non-grounded skip, ForceOn adds request, no-singleton no-throw
- [x] `TerrainQueryResolutionSystemTests` — jump-rejection (|16−10|>5 rejected), acceptance (|13−10|≤5 accepted), first-frame bootstrap (LastValidIgAltitude=0 accepts any hit), missed-hit ignored, no-singleton no-throw
- [x] `TerrainQueryPipelineIntegrationTests` — 3-frame `FlatEarthTerrainProvider` integration: `TargetZOffset` converges to 3.0 m
- [x] `IgGroundClampingModuleTests` — 4 systems registered, metadata valid
- [x] `TransformSyncSystemGroundClampingTests` — Z-offset applied when `GroundClampingState` present; Z unmodified when absent

---

## 📝 Developer Insights

**Q1: For CT-MOD1-M, confirming everything builds after reverting the submodule files — did leaving the stubs as no-ops cause any unexpected side effects?**

No unexpected side effects. The 4 stubs added to `BTreeContext` (`RequestRaycast`, `GetRaycastResult`, `RequestPath`, `GetPathResult`) return default values (`-1` for request IDs, `default` for result structs). Because no production code path in this codebase activates the BTree subsystem during an actual physics or navigation query — those paths use the ECS batch singletons via `RaycastBatchHelper` and `PathfindingBatchHelper` now — the stubs are dead code in practice. All 53 Behavior toolkit tests and 106 combined physics/navigation/behavior tests pass with the stubs in place. If the BTree library is later extended to call back into `IAIContext`, the stubs will need real implementations, but that is separate future work.

---

**Q2: During CT-MOD1-K, how did the transition from mocked services to direct `EntityRepository` Singletons change the structure of your BTree node unit tests?**

The change eliminated service-interface mocks entirely. Previously, `PhysicsQueryActionNode` and `PathfindingActionNode` inherited from a (nonexistent) `BTreeActionNode` base and held injected `IRaycastService` / `IPathfindingService` fields, so tests needed to construct mock service objects and inject them. With the new static-helper approach (`RaycastBatchHelper`, `PathfindingBatchHelper`), the action-node objects are plain POCOs that hold only the entity's index/generation, and the helpers interact directly with `EntityRepository.GetSingleton<RaycastBatchData>()`. Unit tests now:

1. Create a bare `EntityRepository`.
2. Call `world.SetSingleton(new RaycastBatchData { ... })` to seed the batch.
3. Populate `batch.Requests[i]` and read `batch.Hits[i]` directly.

This is simpler and more honest — the tests exercise the *actual* memory path the runtime uses, not a mock boundary. The tradeoff is that tests require `AllowUnsafeBlocks` to construct `NativeArray<T>`.

---

**Q3: For CT-MOD1-L, what was the minimal path to allow `LosRequestBatchingSystem` to conform to `ISystemRegistry` without breaking its internal ECS update logic?**

`ISystemRegistry.RegisterSystem<T>` requires `T : IModuleSystem`. `LosRequestBatchingSystem` extended `ComponentSystem` (the ECS base for main-thread per-frame systems), which does not implement `IModuleSystem`. The minimal fix was dual-interface: keep the `ComponentSystem` base class intact (preserving `OnUpdate`, `World`, and `Bus` access for any existing `SystemGroup` callers) and add explicit `IModuleSystem` implementation with a single bridge method:

```csharp
void IModuleSystem.Execute(ISimulationView view, float deltaTime)
{
    var events = view.ConsumeEvents<LosCheckRequestEvent>();
    var cmd    = view.GetCommandBuffer();
    foreach (ref readonly var e in events)
        cmd.PublishEvent(new RaycastBatchEntry { ... });
}
```

This approach requires no changes to the internal `OnUpdate` path, no changes to `CombatModule` or any existing `SystemGroup`, and adds exactly one new interface + one method to the class. The 4 systems in `AutonomousPerceptionModule` are now all registered via `registry.RegisterSystem(...)` without any public field exposure.

---

**Q4: The three-phase clamping pipeline (P7T4) is highly time-sensitive. Were you able to prove via integration tests that the Resolution phase precisely applies interpolations within the same frame?**

Yes. `TerrainQueryPipelineIntegrationTests.Pipeline_TargetOffsetConverges_After3Frames` drives all four systems in the correct phase order (Init → Submit → Solver → Resolution) within a single `TickOnce()` call, with command-buffer playback at the end of each tick. After frame 1 the `FlatEarthTerrainProvider` stub (constant height = 5 m, entity simZ = 2 m) produces `TargetZOffset = 3 m`; frames 2 and 3 confirm idempotence (the value remains 3 m since simZ and terrain height are unchanged). The key safety property — that the resolution system's `cmd.SetComponent` call is visible *in the same tick* after `ecb.Playback` — is demonstrated by reading the component value immediately after the tick helper returns.

---

**Q5: Did bridging the `GroundClampingOverrideTranslator` (P7T3) expose any deserialization complexities mapping the DDS enum to the engine-side enum?**

No deserialization complexity. Both the wire enum (`Hrot.NED.Descriptors.EClampingMode`) and the engine enum (`Fdp.Modules.Geographic.EClampingMode`) are `byte`-backed and share identical ordinal values (0 = Default/CLAMP_DEFAULT, 1 = ForceOn/CLAMP_FORCE_ON, 2 = ForceOff/CLAMP_FORCE_OFF). The mapping is a direct cast: `(IgEClampingMode)(int)sample.Data.Mode`. No lookup table or switch statement is required.

The dual-enum pattern (separate wire and engine enums) is valuable here even though the values happen to match: it decouples the DDS IDL schema from the IG engine's internal naming convention, so a future IDL change (e.g. adding `CLAMP_TERRAIN_ADAPTIVE = 3`) does not automatically leak into engine code paths and can be handled at the translator boundary.

---

## 🏗️ Architecture Notes

### Ground Clamping Module Structure

```
DDS wire (Hrot.NED.Descriptors)
  └─ GroundClampingOverride { EntityId, EClampingMode }
       │
       ▼ GroundClampingOverrideTranslator (ordinal 66)
       │
       ▼ ECS component: GroundClampingConfig { Mode, BaseRequiresClamping }

ITerrainProvider (Fdp.Modules.Geographic)
  └─ QueryBatch(requests, count, results) → engine terrain adapter

Phase pipeline (all in Fdp.Modules.Geographic.Systems):
  Input:          TerrainQueryInitializationSystem  (resets batch count)
  Input:          TerrainQuerySubmitSystem          (writes batch from clamped entities)
  Simulation:     TerrainQuerySolverSystem          (calls ITerrainProvider.QueryBatch)
  PostSimulation: TerrainQueryResolutionSystem      (updates GroundClampingState with jump-rejection)

TransformSyncSystem (Fdp.Examples.NetworkDemo.Systems):
  PostSimulation:  lerps CurrentZOffset → TargetZOffset
                   applies CurrentZOffset to visual Z of SimTransform
```

### Files Created / Modified

| File | Change |
|------|--------|
| `Hrot.NED/SimDescriptors.cs` | `EClampingMode` enum + `GroundClampingOverride` struct added |
| `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` | IDs 77–79 allocated (GroundClampingConfig, GroundClampingState, TerrainQueryBatchData) |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/EClampingMode.cs` | NEW — engine-side enum |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/ITerrainProvider.cs` | NEW — terrain query abstraction |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GroundClampingConfig.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GroundClampingState.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/TerrainQueryBatchData.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/TerrainQueryInitializationSystem.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/TerrainQuerySubmitSystem.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/TerrainQuerySolverSystem.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/TerrainQueryResolutionSystem.cs` | NEW |
| `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs` | +4 no-op IAIContext stubs |
| `FDP/Toolkits/FDP.Toolkit.Physics/BTreeNodes/PhysicsQueryActionNode.cs` | Replaced with `RaycastBatchHelper` static class |
| `FDP/Toolkits/FDP.Toolkit.Physics/BTreeNodes/Action_QueryRaycast.cs` | Replaced with plain POCO |
| `FDP/Toolkits/FDP.Toolkit.Navigation/BTreeNodes/PathfindingActionNode.cs` | Replaced with `PathfindingBatchHelper` static class |
| `FDP/Toolkits/FDP.Toolkit.Navigation/BTreeNodes/Action_PlanRoute.cs` | Replaced with plain POCO |
| `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs` | +`IModuleSystem` explicit interface |
| `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs` | Removed public field; all 4 systems registered via registry |
| `FDP/Toolkits/FDP.Toolkit.Perception.Tests/AutonomousPerceptionModuleTests.cs` | Updated assertions |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/TransformSyncSystem.cs` | Z-offset block added to `SyncRemoteEntities` |
| `Hrot.IG/Translators/GroundClampingOverrideTranslator.cs` | NEW |
| `Hrot.IG/Modules/IgGroundClampingModule.cs` | NEW |
| `Hrot.IG/IgApplication.cs` | Component registrations + `InstallGroundClamping` method + translator registration |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/Fdp.Toolkit.Geographic.Tests.csproj` | +AllowUnsafeBlocks, +Fdp.Kernel reference |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/GroundClampingConfigTests.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/TerrainQueryBatchDataTests.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/Systems/TerrainQuerySubmitSystemTests.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/Systems/TerrainQueryResolutionSystemTests.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/Systems/TerrainQueryPipelineIntegrationTests.cs` | NEW |
| `Hrot.IG.Tests/IgGroundClampingModuleTests.cs` | NEW |
| `Hrot.IG.Tests/TransformSyncSystemGroundClampingTests.cs` | NEW |
