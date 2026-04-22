# MOD1-BATCH-10 Report

**Batch:** MOD1-BATCH-10  
**Developer:** GitHub Copilot  
**Date:** 2026-03-16  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| DB-MOD1-03 | ✅ Complete | Added `HasAuthority` to `NetworkOwnership`; fixed `CycloneNetworkCleanupSystem` |
| DB-MOD1-07 | ✅ Complete | Gated all 3 `EntityMasterEgressTranslatorTests` with `[Trait("Category","Integration")]` |
| DB-MOD1-10 | ✅ Complete | Fixed 9 unguarded `DdsParticipant` creations in `EntityMissionTranslatorTests.cs` |
| DB-MOD1-21 | ✅ Complete | Confirmed zero `Hrot.*` refs in `TestMetricsCollector`; documented in `MOD1-DESIGN.md §3.9.4` |
| MOD1-P6T4 | ✅ Already delivered | `PhysicsQueryActionNode` (as `RaycastBatchHelper`), `Action_QueryRaycast`, all 3 tests present |
| MOD1-P6T5 | ✅ Already delivered | `PathfindingActionNode` (as `PathfindingBatchHelper`), `Action_PlanRoute`, all 3 tests present |
| MOD1-P6T6 | ✅ Already delivered | `AutonomousPerceptionModule` + `PhysicsQueryModule` exist with correct policies and tests |
| MOD1-P6T7 | ✅ Already delivered | `NavigationSolverModule` + `PathfindingSolverSystem` exist with all 3 specified tests |
| MOD1-P6T8 | ✅ Complete | All 4 packs exist; `NodeBootstrapper` wiring confirmed; 12 new tests added to `TranslatorPackTests.cs` |

---

## 🧪 Testing Results

**Hrot.SimHost.Tests:** 182 / 182 passed (+12 new tests vs. baseline 170)  
**ModuleHost.Network.Cyclone.Tests:** 47 / 47 passed  
**FDP.Toolkit.Physics.Tests:** 21 / 21 passed  
**FDP.Toolkit.Navigation.Tests:** 31 / 31 passed  
**FDP.Toolkit.Behavior.Tests:** 53 / 53 passed  
**FDP.Toolkit.Perception.Tests:** all passed  
**Hrot.IG.Tests:** 304 / 304 passed  
**Hrot.SimHost.Integration.Tests:** 28 / 28 passed (when run in isolation)  
**Hrot.ClusterRunner.Integration.Tests:** 31 / 31 passed

Pre-existing flaky failures (not caused by batch changes, pass when run in isolation):
- `ModuleHost.Core.Tests.HonestSodGdbTests.GdbModule_InstallAndUninstall_UsesDoubleBufferProvider` — thread-timing-sensitive GDB test
- `ModuleHost.Core.Tests.ResilienceIntegrationTests.Resilience_*` tests — isolation-sensitive, pass solo
- `Hrot.SimHost.Integration.Tests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` — domain 0 collision when `Hrot.IG.Tests` runs concurrently (inherent to DDS domain sharing; passes in isolation)
- `Fdp.Tests.ComponentDirtyTrackingTests.ComponentDirtyTracking_ConcurrentScanPerformance` — timing-based performance test

**Key Test Scenarios Verified:**
- [x] `CycloneNetworkCleanupSystem` correctly tracks owned entities using `HasAuthority`
- [x] `EntityMasterEgressTranslatorTests` gated so unit test runs skip them (DDS-daemon-free)
- [x] DDS participant cleanup in `EntityMissionTranslatorTests` — 9 tests now properly dispose via `using var`
- [x] `TestMetricsCollector` confirmed to have zero `Hrot.*` references
- [x] All 4 Phase 6 translator packs create the correct number and types of translators
- [x] `NodeBootstrapper.BuildTranslators(NodeRole.AllInOne)` includes all 4 new packs
- [x] `NodeBootstrapper.BuildTranslators(NodeRole.Brain)` excludes Sim-side perception/pathfinding packs
- [x] `NodeBootstrapper.BuildTranslators(NodeRole.Perception)` includes SimPerceptionTranslatorPack
- [x] `NodeBootstrapper.BuildTranslators(NodeRole.NavigationSolver)` includes SimPathfindingTranslatorPack
- [x] `PhysicsQueryActionNode` tests: WritesToBatch, ReturnsMatchingHit, ReturnsDefaultForUnresolvedId
- [x] `PathfindingActionNode` tests: WritesToBatch, ReturnsRouteHandleWhenResolved, ReturnsDefaultWhilePending
- [x] `PathfindingSolverSystem` tests: WritesRouteHandle, WritesUnreachable
- [x] `NavigationSolverModule_RegistersPathfindingSystem`
- [x] `AutonomousPerceptionModule_RegistersAllPerceptionSystems`
- [x] `PhysicsQueryModule_RegistersRaycastAndHitSystems`

---

## 📝 Developer Insights

**Q1: For DB-MOD1-03 — how many systems were still reading `PrimaryOwnerId` directly? Were any cases genuinely intentional?**

**Production systems changed:** 1  
— `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs`: replaced `if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;` with `if (!ownership.HasAuthority) continue;`.  
  Fix required adding `HasAuthority => PrimaryOwnerId == LocalNodeId` to the `NetworkOwnership` struct in `NetworkComponents.cs` (the `NetworkAuthority` struct in `FDP.Toolkit.Replication` already had this property; `NetworkOwnership` in `ModuleHost.Core.Network` did not).

**FDP/Examples (demo apps) — documented but not changed:**  
Seven systems in `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/` still read `.PrimaryOwnerId` directly: `TransformSyncSystem`, `ReplayBridgeSystem`, `RefactoredPlayerInputSystem`, `PhysicsSystem`, `CombatInputSystem`, `CombatFeedbackSystem`, `ChatSystem`. These are example/demo applications, not production `Hrot.*` or `FDP.Toolkit.*` code, so they are out of scope for this production audit. They should migrate to `.HasAuthority` in a dedicated examples cleanup pass.

**Were any cases genuinely intentional?**  
The `OwnershipExtensions.OwnsDescriptorKey` helper in `NetworkComponents.cs` (lines 101, 132) directly compares `PrimaryOwnerId`  — this IS the correct location (it implements the HasAuthority semantics), not a `WithOwned<T>()` candidate.  
The `NetworkSpawningSystem.cs` write (`PrimaryOwnerId = cmd.OwnerNodeId`) is a legitimate ingress-write during entity spawning from a network command, consistent with the rule "only the replication ingress translator may write it."

---

**Q2: For P6T4/P6T5 — did `BTreeContext` still have the raycast/pathfinding stubs? What is the current state of `IAIContext`?**

`BTreeContext.cs` still implements `IAIContext.RequestRaycast`, `IAIContext.GetRaycastResult`, `IAIContext.RequestPath`, and `IAIContext.GetPathResult` as no-op stubs (returning `-1`/`default`). This is **correct and intentional**: `IAIContext` is defined in `Fbt.Kernel` (a third-party submodule that must not be modified), so `BTreeContext` MUST provide concrete implementations of the interface contract. Making them no-ops satisfies the interface without creating the circular dependency that wiring them to `RaycastBatchData` or `PathfindingBatchData` would introduce.

The concrete access to raycast/pathfinding batch data was moved to:
- `FDP/Toolkits/FDP.Toolkit.Physics/BTreeNodes/RaycastBatchHelper.cs` — static helper (analogous to `PhysicsQueryActionNode` from the design spec, adapted to the FDP delegate-based BTree pattern)
- `FDP/Toolkits/FDP.Toolkit.Navigation/BTreeNodes/PathfindingBatchHelper.cs` — static helper (analogous to `PathfindingActionNode`)

Concrete action nodes (`Action_QueryRaycast`, `Action_PlanRoute`) delegate to these helpers, keeping `FDP.Toolkit.Behavior` free of Physics and Navigation assembly references (confirmed via `dotnet build` with no new `RaycastBatchData`/`PathfindingBatchData` references in `FDP.Toolkit.Behavior.csproj`).

---

**Q3: For P6T8 — how did AllInOne handle both Brain and Sim translator packs? Were there ordering concerns?**

`NodeBootstrapper.BuildTranslators(NodeRole.AllInOne, ...)` appends both Brain-side and Sim-side packs to the same translator list. The conditional blocks are additive — no deduplication is needed because:

1. **Different DDS topics:** `SensorConfigEgressTranslator` publishes to `SensorConfig` (Brain→Sim); `SensorConfigIngressTranslator` subscribes to the same topic (Sim side). In AllInOne mode both are present. Internally the egress translator reads ECS state and writes to DDS memory, while the ingress translator reads from DDS memory and writes to ECS. In-process, these operate on the same ECS singletons, making the round-trip instantaneous with no network I/O overhead.
2. **No topic subscription conflict:** Both Brain and Sim ingress translators subscribe to their respective topics using different readers (different DDS reader objects, same topic). CycloneDDS allows multiple readers per topic.
3. **No ordering requirement:** Ingress and egress translators are driven by the `CycloneNetworkModule` poll loop, which calls `PollIngress` and `ScanAndPublish` in sequence. Within the same frame, the egress from the Brain side writes the DDS sample that the Sim ingress will pick up on the next frame — this one-frame lag is by design and consistent with the existing non-AllInOne deployment model.

---

**Q4: Were there any new circular dependency risks discovered?**

None. Verified:
- `FDP.Toolkit.Behavior.csproj` has zero references to `FDP.Toolkit.Physics` or `FDP.Toolkit.Navigation` ✅
- All four new translator packs are in `Hrot.SimHost.Network` (Hrot domain, may reference FDP toolkits) ✅
- `NavigationSolverModule` is in `FDP.Toolkit.Navigation` and references `FDP.Toolkit.Behavior` (correct, one-way) ✅
- `AutonomousPerceptionModule` is in `FDP.Toolkit.Perception` which references `Fdp.Kernel` only ✅
- `PhysicsQueryModule` is in `FDP.Toolkit.Physics` which references `FDP.Toolkit.Perception` (for `HitResolutionSystem` events) but not the reverse ✅

---

## ⚠️ Outstanding Issues / Next Steps

- **FDP/Examples PrimaryOwnerId residue** (DB-MOD1-03 follow-up): Seven demo-app systems still read `.PrimaryOwnerId` directly. Should be cleaned up in a dedicated examples maintenance pass, not a production batch.
- **Pre-existing flaky tests**: `ModuleHost.Core.Tests` resilience tests and `Hrot.SimHost.Integration.Tests.DomainIsolation` remain flaky under heavy parallel load due to thread-timing and DDS domain-0 sharing. Root-cause fix would require either serializing affected test classes with `[Collection]` or migrating domain-0 tests to a unique domain ID.
- **DB-MOD1-02** (pending): `GlobalComponentIds` 20–49 block full — automated uniqueness guard still needed.
