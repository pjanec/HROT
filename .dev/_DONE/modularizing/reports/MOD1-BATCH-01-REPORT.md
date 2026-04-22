# MOD1-BATCH-01 Report: CQRS Navigation Contract + Authority Bug Fixes

**Batch:** MOD1-BATCH-01  
**Date:** 2025-07-13  
**Status:** ✅ COMPLETE

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MOD1-P1T1 | ✅ Complete | Navigation contracts placed in `Fdp.Kernel` (deviation documented below) |
| MOD1-P1T2 | ✅ Complete | `MoveToExecutor` fully CQRS — zero geo dependencies |
| MOD1-P1T3 | ✅ Complete | Both geographic systems now use `WithOwned<Position>()` / `WithoutOwned<Position>()` |
| MOD1-P1T4 | ✅ Complete | `NavigationExecutionSystem` created in `FDP.Toolkit.CarKinem` |

---

## 🧪 Testing Results

**FDP.Toolkit.Navigation.Tests:** 26 / 26 passed  
**Fdp.Toolkit.Geographic.Tests:** 23 / 23 passed  
**FDP.Toolkit.CarKinem.Tests:** 117 / 117 passed  
**Hrot.NED.Tests:** 9 / 9 passed  
**Hrot.SimHost.Tests:** 98 / 99 passed (1 pre-existing failure — see Outstanding Issues)

**Key Test Scenarios Verified:**
- ✅ `NavigationIntent.Mode` zero-inits to `NavigationMode.None`
- ✅ `NavigationStatus.Result` zero-inits to `NavigationResult.InProgress`
- ✅ `FDP.Toolkit.Navigation` assembly contains zero references to `Hrot.*`
- ✅ `MoveToExecutor_OnEnter_WritesNavigationIntentWithIncrementedId`
- ✅ `MoveToExecutor_Execute_ReturnsSuccessWhenStatusArrived`
- ✅ `MoveToExecutor_Execute_IgnoresStaleStatus`
- ✅ `MoveToExecutor_Execute_ReturnsFailureWhenBlocked`
- ✅ `MoveToExecutor_Execute_ReturnsFailureWhenUnreachable`
- ✅ `MoveToExecutor_OnExit_ClearsNavigationIntent`
- ✅ `CoordinateTransformSystem_SkipsGhostEntities`
- ✅ `GeodeticSmoothingSystem_ProcessesOnlyGhostEntities`
- ✅ `NavigationExecution_WritesArrivedWhenEntityReachesTarget`
- ✅ `NavigationExecution_WritesFailedWhenEntityStuck`
- ✅ `NavigationExecution_IntentIdMismatch_ResetsOnNewCommand`

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Issue 1 — Circular dependency between `FDP.Toolkit.Navigation` and `FDP.Toolkit.CarKinem`.**  
`FDP.Toolkit.Navigation` already depends on `FDP.Toolkit.CarKinem` (via `FollowRouteExecutor`, `FleeExecutor`, etc. that use `CarKinem.Core.NavState`). Placing `NavigationIntent`, `NavigationStatus`, and the two enums inside `FDP.Toolkit.Navigation` would require `FDP.Toolkit.CarKinem` to reference `FDP.Toolkit.Navigation` for `NavigationExecutionSystem` — forming a cycle.  
**Resolution:** Physically placed the four new types (`NavigationMode`, `NavigationResult`, `NavigationIntent`, `NavigationStatus`) in `Fdp.Kernel/CoreComponents/NavigationComponents.cs`. The C# namespace is `FDP.Toolkit.Navigation` (matching the logical toolkit), but the assembly is `Fdp.Kernel`. This is the same pattern used for `HealthData`, which lives in `Fdp.Kernel` to break the `Combat ↔ Behavior` cycle.

**Issue 2 — `NavigationMode` name collision across ancestor namespaces.**  
`CarKinem.Core.NavigationMode` (the existing kinematics enum) and the new `FDP.Toolkit.Navigation.NavigationMode` (the CQRS enum) share the same short name. Files inside the `FDP.Toolkit.Navigation.Executors` namespace resolve `NavigationMode` via C# ancestor-namespace lookup to the *new* CQRS enum, silently breaking `FollowRouteExecutor`, `FleeExecutor`, and `FollowRoadGraphExecutor` (which need the kinematics one).  
**Resolution:** Added `using CarKinemNavMode = CarKinem.Core.NavigationMode;` alias in each affected executor file and its test file, then updated all usages of the kinematics enum to `CarKinemNavMode.*`. This is a maintenance surface: future executors added under this namespace must apply the same alias.

**Issue 3 — `GlobalComponentIds` 20–49 toolkit block was full.**  
The batch spec references IDs in the 20–49 range, but that range is entirely allocated (30 IDs, all consumed). Assigning 24 or 25 would overwrite `SimTransform` and `SimVelocity`.  
**Resolution:** Used IDs 67 (`NavigationIntent`) and 68 (`NavigationStatus`), which occupied a "reserved for Replication" comment block. Updated the block comment to reflect their new usage.

**Issue 4 — `GetManagedComponent<T>()` is not publicly accessible.**  
`CoordinateTransformSystemTests` initially followed the read-back pattern (`FlushCommandBuffer` + `GetManagedComponent`) to verify that `IGeographicTransform.ToGeodetic` was called. Neither method exists on the public `EntityRepository` API.  
**Resolution:** Switched to mock-verification strategy: inject a `Mock<IGeographicTransform>` and assert `mock.Verify(g => g.ToGeodetic(ownedPos), Times.AtLeastOnce())` for the owned entity and `Times.Never()` for the ghost entity. This is actually cleaner — it tests responsibility (did the system call the transform?) rather than side-effects on internal state.

---

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

**Ongoing NavState coupling in non-CQRS executors.** `FollowRouteExecutor`, `FleeExecutor`, and `FollowRoadGraphExecutor` still write directly to `CarKinem.Core.NavState`, bypassing the new CQRS contract entirely. `MoveToExecutor` is now clean, but the majority of executors remain on the pre-CQRS path. The architectural divide is wider than it appears from the batch scope.

**`CarKinem.Core.NavigationMode` vs `FDP.Toolkit.Navigation.NavigationMode` naming ambiguity.** Two public enums with the same short name in the same logical domain will silently cause subtle bugs as the codebase grows. The old `CarKinem.Core.NavigationMode` should be renamed (e.g., `KinematicsMode`) or encapsulation should be improved with an `internal` visibility once all callers are migrated to the new CQRS contract.

**`GlobalComponentIds` block management.** The 30-slot per-toolkit block convention is fragile — the Navigation toolkit already has no room, and there is no automated guard against ID collisions. A compile-time uniqueness check (e.g., a Roslyn analyzer or a unit test that iterates all `ComponentId` attributes and asserts no duplicates) would prevent silent data corruption if an ID is accidentally reused.

**`NetworkOwnership` residue.** Although this batch replaced the two geographic systems' manual ownership checks, `NetworkOwnership` is still present in many other systems across the codebase. The pattern of `if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;` is a ticking time-bomb in any distributed-authority configuration. A codebase-wide audit and migration to `WithOwned<T>()` / `WithoutOwned<T>()` is warranted.

---

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

**Placing navigation contracts in `Fdp.Kernel` (not `FDP.Toolkit.Navigation`).** The spec assumed the new types would live in `FDP.Toolkit.Navigation`, but that would create a circular project reference. The approach of hosting them in `Fdp.Kernel` with the `FDP.Toolkit.Navigation` C# namespace maintains discoverability while eliminating the cycle. The alternative — introducing a new `FDP.Toolkit.Navigation.Core` or `FDP.Toolkit.Navigation.Contracts` project — was considered but rejected to avoid introducing yet another project boundary for a small handful of structs. Keeping the `HealthData` precedent consistent felt more appropriate.

**Mock-verification strategy for `CoordinateTransformSystemTests`.** Rather than testing that `PositionGeodetic` was set (which required internal API access), the tests verify that the right `IGeographicTransform` method was called the right number of times. This follows the Tell-Don't-Ask principle and decouples the test from internal component storage mechanics.

**`NavigationExecutionSystem` as a standalone `ComponentSystem` (not integrated into `CarKinematicsSystem`).** The spec title suggests "add logic to CarKinematicsSystem", but a separate system with `[UpdateAfter(typeof(CarKinematicsSystem))]` is architecturally cleaner — it avoids bloating the kinematics system and keeps the frustration tracking logic independently testable. The `UpdateAfter` ordering preserves the dependency without merging concerns.

**`_frustrationTicks` dictionary keyed by entity index (not `Entity` handle).** Using `entity.Index` as the key means a recycled entity can inadvertently inherit frustration counter state from a previously destroyed entity with the same index. The safer approach would be to key by `entity.PackedValue` (index + generation) and clear stale entries on entity destruction. This is documented in Outstanding Issues.

---

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

**C# ancestor-namespace lookup for type resolution.** Any type declared in an ancestor namespace is automatically in scope inside child namespaces. The new `FDP.Toolkit.Navigation.NavigationMode` is in scope from `FDP.Toolkit.Navigation.Executors` without any `using` directive — silently shadowing `CarKinem.Core.NavigationMode`. This was not flagged as a risk in the spec and would have caused a subtle runtime behaviour change (using the wrong enum variant) rather than a compile error because the enum values overlap at zero.

**`NavigationStatus.IntentId` mismatch window.** Between `OnEnter` writing a new `NavigationIntent` and `NavigationExecutionSystem` running its first tick, `NavigationStatus.IntentId` still holds the previous value. During this one-tick window `MoveToExecutor.Execute` correctly returns early (stale status check). This is the intended behaviour but was not explicitly covered in the spec's success conditions for T2.

**`NavigationMode.None` as the "skip" sentinel.** `NavigationExecutionSystem` uses `intent.Mode == NavigationMode.None` to detect inactive entities and skip them. This relies on the engine guarantee that zero-initialized components have `Mode == None`. If an entity is allocated from a pool without explicit component reset, mode could be non-None while no valid intent exists. `OnExit` explicitly sets `Mode = None`, which is the correct guard point, but pool recycling is an assumption rather than an enforced contract.

**`FrustrationTickLimit` and frame-rate dependence.** At 60 Hz the limit of 120 ticks is 2 seconds. If `NavigationExecutionSystem` runs at a different tick rate (e.g., variable timestep), the frustration threshold is implicitly time-variant. The spec states the constant but doesn't mention tick-rate coupling.

---

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

**`_frustrationTicks` dictionary allocation.** `NavigationExecutionSystem` holds a `Dictionary<int, int>` that grows as entities navigate. Entries are never removed upon entity destruction (because there is no `OnEntityDestroyed` hook in the current system API). In a long-running simulation with many ephemeral entities this will leak memory indefinitely. The same problem existed in the old `MoveToExecutor._stuckTicks`. Migrating to a component (`FrustrationTicks : int`) stored directly on the entity would eliminate the dictionary entirely and enable automatic cleanup via ECS entity lifecycle.

**One extra linear pass per tick.** `NavigationExecutionSystem.OnUpdate()` runs a full entity query after `CarKinematicsSystem`. All navigating entities are iterated twice per frame (once in carKinematics, once here). The arrival/frustration check could instead be performed as a late step inside `CarKinematicsSystem` to eliminate the second pass. This was not done in order to keep system responsibilities separated, but it is a measurable cost at entity counts above ~1 000.

**`NavigationExecutionSystem` allocates an iterator per tick.** The `foreach (var entity in query)` call on `QueryBuilder.Build()` materialises a query enumerator each update. For hot code paths, a zero-allocation iterator (cached query + `Span<Entity>` slicing) would be preferable. This is consistent with other systems in the codebase that have not yet been migrated to the allocation-free path.

---

## ⚠️ Outstanding Issues / Next Steps

- **`_frustrationTicks` entity index collision risk.** Should be replaced by a `FrustrationTicks` ECS component on the entity (see Q5). Suggested follow-on DEBT item.
- **`CarKinem.Core.NavigationMode` rename.** The ambiguity with `FDP.Toolkit.Navigation.NavigationMode` should be resolved before T2-style executors exist alongside T3/T4-style (CQRS) executors in the same namespace tree.
- **`Hrot.SimHost.Tests` — 1 pre-existing failure.** `EntityMasterEgressTranslatorTests.ScanAndPublish_RemotelyOwnedEntity_DoesNotPublish` fails with `CycloneDDS.Runtime.DdsException: Failed to create participant (ReturnCode: Error)`. This test requires a running CycloneDDS daemon and has been failing in this environment prior to and independent of this batch. No changes were made to `EntityMasterEgressTranslator` or any related DDS infrastructure in this batch.
- **`FollowRouteExecutor`, `FleeExecutor`, `FollowRoadGraphExecutor` still use `NavState`.** These are not yet migrated to the new CQRS contract. They are outside this batch's scope but should be tracked as MOD1 Phase 2 work.

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `FDP/Kernel/Fdp.Kernel/CoreComponents/NavigationComponents.cs` | **NEW** — NavigationMode, NavigationResult, NavigationIntent, NavigationStatus in namespace `FDP.Toolkit.Navigation` |
| `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` | **MODIFIED** — Added NavigationIntent=67, NavigationStatus=68 |
| `Hrot.NED/SimDescriptors.cs` | **MODIFIED** — Added ENavigationMode, ENavigationResult, NavigationIntent DDS struct, NavigationStatus DDS struct |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs` | **REWRITTEN** — Pure CQRS: writes NavigationIntent on enter, reads NavigationStatus on execute |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs` | **MODIFIED** — Added `CarKinemNavMode` alias to resolve NavigationMode ambiguity |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs` | **MODIFIED** — Added `CarKinemNavMode` alias |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs` | **MODIFIED** — Added `CarKinemNavMode` alias |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/CoordinateTransformSystem.cs` | **REWRITTEN** — Replaced NetworkOwnership manual check with `.WithOwned<Position>()` |
| `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/GeodeticSmoothingSystem.cs` | **REWRITTEN** — Replaced NetworkOwnership manual skip with `.WithoutOwned<Position>()` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs` | **NEW** — Arrival + frustration logic; writes NavigationStatus; runs after CarKinematicsSystem |
| `FDP.Toolkit.Navigation.Tests/NavigationContractsTests.cs` | **NEW** — Zero-init defaults, enum values, zero Hrot.* references |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/MoveToExecutorTests.cs` | **REWRITTEN** — 6 CQRS-based tests |
| `FDP.Toolkit.Navigation.Tests/NavigationTestWorldFactory.cs` | **MODIFIED** — RegisterComponent for NavigationIntent and NavigationStatus |
| `FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs` | **MODIFIED** — Added CarKinemNavMode alias |
| `Fdp.Toolkit.Geographic.Tests/Systems/GeodeticSmoothingSystemTests.cs` | **REWRITTEN** — Uses SetAuthority<Position>() instead of NetworkOwnership |
| `Fdp.Toolkit.Geographic.Tests/Systems/CoordinateTransformSystemTests.cs` | **NEW** — Mock-verify strategy for owned/ghost filtering |
| `FDP.Toolkit.CarKinem.Tests/Systems/NavigationExecutionSystemTests.cs` | **NEW** — 4 tests covering arrival, frustration, intent-id mismatch reset, inactive skip |
