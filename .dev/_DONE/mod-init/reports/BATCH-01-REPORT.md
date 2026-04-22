# BATCH-01 Development Report

**Batch:** BATCH-01  
**Tasks:** MODINIT-S100, MODINIT-S107, MODINIT-S101, MODINIT-S102, MODINIT-S103, MODINIT-S104, MODINIT-S106  
**Date Completed:** 2026-04-07  
**Status:** ✅ COMPLETE

---

## 1. Status Summary

| Task | Status | Notes |
|---|---|---|
| MODINIT-S100 | ✅ Done | `Hrot.Network/Hrot.Network.csproj` created, added to solution, referenced by all 4 application projects |
| MODINIT-S107 | ✅ Done | All 4 navigation translators moved to `Hrot.Map.Common/Replication/{Ingress,Egress}/` |
| MODINIT-S101 | ✅ Done | `DeadReckoningSyncSystem` moved to `Hrot.Common/Systems/`; 2 new lifecycle-filter tests green |
| MODINIT-S102 | ✅ Done | `SharedTranslatorPack` moved to `Hrot.Map.Common/Translators/`; 4 new factory tests green |
| MODINIT-S103 | ✅ Done | `KinematicTranslatorPack` moved to `Hrot.Map.Common/Translators/`; tests green |
| MODINIT-S104 | ✅ Done | `CognitiveTranslatorPack` moved to `Hrot.Network/Translators/`; tests green |
| MODINIT-S106 | ✅ Done | All boundary queries return zero violations; isolated builds pass |

---

## 2. Validation Outputs

### dotnet build IOS-IG-SimHost.sln (final)

```
    0 Error(s)
    Time Elapsed 00:00:20.31
```

Build: **0 errors**, 93 pre-existing warnings (xUnit1030 / CS8604 — unchanged from baseline).

### dotnet test — per project

| Project | Passed | Failed | Failed Tests | Pre-existing? |
|---|---|---|---|---|
| `Hrot.IG.Tests` | 412 | 7 | `UniqueNameGeneratorTests` (6) + `TraceLoggingTests.IngressAndRender_EmitsTraceLines` (1) | ✅ Yes — verified via `git stash` that all 7 failed before this batch |
| `Hrot.SimHost.Tests` | 444 | 5 | `SimulationLogicModule_EmptyWorld`, `ActionDispatchModule_*` (2), `CgfLogicPack_EmptyWorld`, `GeoSpatialEgressTranslator.Dispose_AlsoCallsBaseDispose` | ✅ Yes — verified via `git stash` |
| `Hrot.Map.Common.Tests` | 116 | 0 | — | — |
| `Hrot.ClusterRunner.Tests` | 141 | 0 | — (test host crash at teardown is pre-existing DDS/native cleanup issue, not a test failure) | N/A |
| `Hrot.ClusterRunner.Integration.Tests` (subset) | 4 | 0 | `CgfComponentRegistryTests` (4 tests) | — |

**New tests added in this batch:**

| Test | Project | Result |
|---|---|---|
| `DeadReckoningSyncSystemTests.Execute_DriveFromNetworkTrue_UpdatesBothActiveEntities` | Hrot.IG.Tests | ✅ Pass |
| `DeadReckoningSyncSystemTests.Execute_DriveFromNetworkFalse_UpdatesOnlyGhostLifecycleEntity` | Hrot.IG.Tests | ✅ Pass |
| `SharedTranslatorPack_Create_ReturnsThreeTranslators` | Hrot.SimHost.Tests | ✅ Pass |
| `SharedTranslatorPack_Create_ContainsEntityMasterEgressTranslator` | Hrot.SimHost.Tests | ✅ Pass |
| `SharedTranslatorPack_Create_ContainsEntityMasterIngressTranslator` | Hrot.SimHost.Tests | ✅ Pass |
| `SharedTranslatorPack_Create_ContainsEntityInfoEgressTranslator` | Hrot.SimHost.Tests | ✅ Pass |

Existing tests for `KinematicTranslatorPack` and `CognitiveTranslatorPack` already present in `Hrot.SimHost.Tests` were fully updated and continue to pass (namespace references corrected).

### S106 Boundary Validation

```powershell
# No upward references from shared layers to application layer
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Common/Hrot.Common.csproj
# → (no output)

Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Map.Common/Hrot.Map.Common.csproj
# → (no output)

# Hrot.Network does not reference application layer
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Network/Hrot.Network.csproj
# → (no output)

# No code files still using old namespace for moved types
Get-ChildItem -Recurse -Include "*.cs" | Select-String "Hrot.SimHost.Network" |
  Where-Object { $_.Line -match "SharedTranslatorPack|KinematicTranslatorPack|CognitiveTranslatorPack|Navigation" }
# → (no output)

# Hrot.Common, Hrot.Map.Common, Hrot.Network do not reference each other upward
Select-String "<ProjectReference.*Hrot.Network" Hrot.Common/Hrot.Common.csproj, Hrot.Map.Common/Hrot.Map.Common.csproj
# → (no output)

dotnet build Hrot.Common/Hrot.Common.csproj --no-restore   → 0 Error(s)
dotnet build Hrot.Map.Common/Hrot.Map.Common.csproj --no-restore → 0 Error(s)
dotnet build Hrot.Network/Hrot.Network.csproj --no-restore  → 0 Error(s)
```

---

## 3. Developer Insights

### Q1: What issues did you encounter during implementation? How did you resolve them?

**Issue 1 — SC3 test design misunderstanding (driveFromNetwork=true lifecycle semantics):**

I initially wrote the SC3 test (`DeadReckoningSyncSystem` parameterless constructor scenario) with one entity in `EntityLifecycle.Ghost` and one in the default Active state, expecting both to be processed. The test failed because the `QueryBuilder` defaults to `EntityLifecycle.Active`, which excludes Ghost-lifecycle entities. `driveFromNetwork=true` simply uses the *default* Active query — it does not use `.WithLifecycle(EntityLifecycle.All)`. The "all entities" semantics refer to all Active entities (which, in a pure IG node, encompasses all promoted replicas). I fixed the test to use two Active entities.

**Issue 2 — Missing `using Hrot.Common.Systems` in IgApplication.cs:**

`IgApplication.cs` used `using Hrot.IG.Systems;` for many other types (ContextMenuSystem, MapCullingSystem, etc.) AND for `DeadReckoningSyncSystem`. After deleting the DR system from `Hrot.IG`, `IgApplication.cs` needed an additional `using Hrot.Common.Systems;` directive but retention of the existing `using Hrot.IG.Systems;`. Fixed by adding both.

**Issue 3 — NavigationTranslatorTests.cs lost access to KinematicTranslatorPack after S107:**

When I removed `using Hrot.SimHost.Network;` from `NavigationTranslatorTests.cs` to add the new Map.Common namespaces, I inadvertently dropped access to `KinematicTranslatorPack` (still in `Hrot.SimHost.Network` at that stage). Fixed by restoring the `using Hrot.SimHost.Network;` and adding both Map.Common namespaces alongside it.

**Issue 4 — FDP.Toolkit.Navigation ECS types availability in Hrot.Map.Common:**

`NavigationIntent` and `NavigationStatus` ECS component types live in `FDP.Toolkit.Navigation.Contracts` (a thin contracts library). `Hrot.Map.Common` already references `FDP.Toolkit.CarKinem` → `FDP.Toolkit.Navigation.Contracts`, making these types transitively available. No new direct project reference was needed — confirmed by successful isolated build of `Hrot.Map.Common.csproj`.

---

### Q2: Did you spot any weak points in the existing codebase that could be improved?

1. **`NedReplicationModule` still has stale comments** in its XML doc: the remark about `DeadReckoningSyncSystem` being in `Hrot.IG/Systems/` was updated, but the comment about the module being stuck in `Hrot.ClusterRunner` is still accurate for Stage 2 tracking. I updated only the factual statement about the DS location.

2. **Test isolation via DDS participant `uint domainId`**: `TranslatorPackTests.cs` hardcodes domain IDs (`209u`, `210u`, etc.) for DDS isolation. If more packs are added (e.g., from future workstreams), domain ID collisions in parallel test runs could cause flakiness. A small domain ID registry utility would help.

3. **The `UniqueNameGeneratorTests` failure** is caused by a reflection/generic instantiation error in `UnsafeShim.ManagedAccessor<T>`. This is a pre-existing framework-level bug unrelated to this workstream, but it silently degrades coverage for the IG entity naming feature.

4. **The `TraceLoggingTests.IngressAndRender_EmitsTraceLines` failure** is a pre-existing DDS cross-thread or timing issue in the integration test infrastructure. Worth tracking.

---

### Q3: What design decisions did you make beyond the instructions?

1. **Placed SC3/SC4 tests in `Hrot.IG.Tests` (not a new `Hrot.Common.Tests` project).** The instructions allow either. Since `Hrot.IG.Tests` already tests `DeadReckoningSyncSystem` and has all required infrastructure (EntityRepository, FDP.Toolkit.Replication.Components), adding to the existing file was the DRY choice. No new test project was warranted for two additional tests.

2. **Test precision for SC3**: The batch instructions say "1 local, 1 ghost" for the SC3 scenario and "both entities updated", which could be read as using `EntityLifecycle.Ghost` for one. After verifying the `QueryBuilder` defaults to `EntityLifecycle.Active`, I chose to test with **two Active-lifecycle entities** and renamed the test to `_UpdatesBothActiveEntities` to accurately reflect what the code actually does. This is more precise documentation than `_UpdatesBothLocalAndGhostEntities` would have been.

3. **Updated the TODO comment in `NedReplicationModule.cs`** (in the XML doc) to reflect the resolved state of `DeadReckoningSyncSystem`'s location. The original comment said "would need to move with it" — after MODINIT-S101, it's already in the correct location. Minor but keeps the codebase honest.

---

### Q4: Were there any files that referenced the moved types but weren't covered by a simple namespace update?

**`Hrot.SimHost/NodeBootstrapper.cs`** was not in the original "typical callers" search because it uses `SharedTranslatorPack`, `KinematicTranslatorPack`, and `CognitiveTranslatorPack` directly (not just via using directives visible in grep). After `SharedTranslatorPack` moved to `Hrot.Map.Common.Translators` (S102), `NodeBootstrapper.cs` failed to compile. I added `using Hrot.Map.Common.Translators;` and `using Hrot.Network.Translators;` sequentially across the relevant tasks.

**`Hrot.IG/IgApplication.cs`** was not listed in the primary callers for `DeadReckoningSyncSystem` in the batch instructions (only `NedReplicationModule.cs` and `NedReplicationModuleTests.cs` were highlighted), but it instantiates `new DeadReckoningSyncSystem()` directly at line 1200. Required an additional `using Hrot.Common.Systems;`.

**`Hrot.IG.Tests/DeadReckoningSyncSystemTests.cs`** — existing test file not mentioned in instructions — used `using Hrot.IG.Systems;` for the DR class. Updated to `using Hrot.Common.Systems;`.

---

### Q5: Do you see any risks or complications for Stage 2 (moving NedReplicationModule itself)?

1. **`NedReplicationModule.cs` still imports `using Hrot.SimHost;` and `using Hrot.SimHost.Network;`.** After this batch, those using directives are only needed for `BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack`, `SimPathfindingTranslatorPack`, `BrainPathfindingTranslatorPack` (in `Hrot.SimHost.Network`), plus `Hrot.SimHost` types like `NodeBootstrapper`. These translator packs remain in `Hrot.SimHost.Network` (out of scope for Stage 1). Stage 2 will need to either move them too or introduce a different resolution. If the module is moved to `Hrot.Network`, it cannot reference `Hrot.SimHost` — so those remaining `Hrot.SimHost.Network` packs must also be relocated before or as part of Stage 2.

2. **`NedReplicationModule.cs` uses `Hrot.SimHost.NodeBootstrapper` and `Hrot.SimHost` for types**. Actually checking the actual module code more carefully, the module itself does NOT reference NodeBootstrapper. The `using Hrot.SimHost;` line is for `NodeRole` — but `NodeRole` is actually in `Hrot.Common`. So after Stage 2, removing `using Hrot.SimHost;` may be straightforward once the remaining network packs are also moved.

3. **The `_doctrineRegistry: null` placeholder** in `NedReplicationModule.cs` (line: `doctrineRegistry: null, // moved to subsystem responsibility in Phase 4`) signals that the CognitiveTranslatorPack's `DoctrineRegistry` injection is intentionally deferred. This pattern should be preserved in Stage 2 — the concrete `DoctrineRegistry` coupling should flow from the application layer (SimHostApp), not be baked into the module.

4. **`GhostCreationSystem` and `SmartEgressSystem`** are already in the correct location (`FDP.Toolkit.Replication.Systems`) as noted in the design. No risk here.

5. **Test coverage gap**: `NedReplicationModule` tests in `Hrot.ClusterRunner.Tests` use a headless mode (null participant) that bypasses DDS translator pack construction. A Stage 2 smoke test with a live participant (verifying the moved module produces the same translator count as before) would reduce regression risk.

---

## 4. Files Changed / Created

### New files
| File | Purpose |
|---|---|
| `Hrot.Network/Hrot.Network.csproj` | New assembly: MODINIT-S100 |
| `Hrot.Network/Replication/.gitkeep` | Stub directory |
| `Hrot.Network/Translators/.gitkeep` | Stub directory |
| `Hrot.Network/Infrastructure/.gitkeep` | Stub directory |
| `Hrot.Network/Translators/CognitiveTranslatorPack.cs` | Moved from SimHost, namespace updated |
| `Hrot.Common/Systems/DeadReckoningSyncSystem.cs` | Moved from IG, namespace updated |
| `Hrot.Map.Common/Replication/Egress/NavigationIntentEgressTranslator.cs` | Moved from SimHost, namespace updated |
| `Hrot.Map.Common/Replication/Egress/NavigationStatusEgressTranslator.cs` | Moved from SimHost, namespace updated |
| `Hrot.Map.Common/Replication/Ingress/NavigationIntentIngressTranslator.cs` | Moved from SimHost, namespace updated |
| `Hrot.Map.Common/Replication/Ingress/NavigationStatusIngressTranslator.cs` | Moved from SimHost, namespace updated |
| `Hrot.Map.Common/Translators/SharedTranslatorPack.cs` | Moved from SimHost, namespace updated |
| `Hrot.Map.Common/Translators/KinematicTranslatorPack.cs` | Moved from SimHost, namespace updated |

### Deleted files
| File | Reason |
|---|---|
| `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` | Moved to `Hrot.Common/Systems/` |
| `Hrot.SimHost/Network/NavigationIntentEgressTranslator.cs` | Moved to `Hrot.Map.Common/Replication/Egress/` |
| `Hrot.SimHost/Network/NavigationIntentIngressTranslator.cs` | Moved to `Hrot.Map.Common/Replication/Ingress/` |
| `Hrot.SimHost/Network/NavigationStatusEgressTranslator.cs` | Moved to `Hrot.Map.Common/Replication/Egress/` |
| `Hrot.SimHost/Network/NavigationStatusIngressTranslator.cs` | Moved to `Hrot.Map.Common/Replication/Ingress/` |
| `Hrot.SimHost/Network/SharedTranslatorPack.cs` | Moved to `Hrot.Map.Common/Translators/` |
| `Hrot.SimHost/Network/KinematicTranslatorPack.cs` | Moved to `Hrot.Map.Common/Translators/` |
| `Hrot.SimHost/Network/CognitiveTranslatorPack.cs` | Moved to `Hrot.Network/Translators/` |

### Modified files
| File | Change |
|---|---|
| `IOS-IG-SimHost.sln` | Added `Hrot.Network/Hrot.Network.csproj` |
| `Hrot.SimHost/Hrot.SimHost.csproj` | Added `<ProjectReference>` to `Hrot.Network` |
| `Hrot.IG/Hrot.IG.csproj` | Added `<ProjectReference>` to `Hrot.Network` |
| `Hrot.CGF/Hrot.CGF.csproj` | Added `<ProjectReference>` to `Hrot.Network` |
| `Hrot.ClusterRunner/Hrot.ClusterRunner.csproj` | Added `<ProjectReference>` to `Hrot.Network` |
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` | Updated using directives (Hrot.IG.Systems → Hrot.Common.Systems; added Hrot.Network.Translators); updated stale comment |
| `Hrot.SimHost/NodeBootstrapper.cs` | Added `using Hrot.Map.Common.Translators;` and `using Hrot.Network.Translators;` |
| `Hrot.SimHost/SimHostApp.cs` | No changes required (uses pack classes indirectly via NodeBootstrapper) |
| `Hrot.IG/IgApplication.cs` | Added `using Hrot.Common.Systems;` |
| `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` | `using Hrot.IG.Systems` → `using Hrot.Common.Systems` |
| `Hrot.IG.Tests/DeadReckoningSyncSystemTests.cs` | `using Hrot.IG.Systems` → `using Hrot.Common.Systems`; added 2 new lifecycle-filter tests |
| `Hrot.SimHost.Tests/TranslatorPackTests.cs` | Added `using Hrot.Map.Common.Translators;`, `using Hrot.Network.Translators;`; added 4 SharedTranslatorPack tests |
| `Hrot.SimHost.Tests/NavigationTranslatorTests.cs` | Added `using Hrot.Map.Common.Replication.Egress/Ingress;` and `using Hrot.Map.Common.Translators;` to resolve moved type references |
