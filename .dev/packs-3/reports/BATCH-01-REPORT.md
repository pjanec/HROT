# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-16  
**Status:** Complete

---

## 📊 Task Completion

| Task ID     | Status | Notes |
|-------------|--------|-------|
| PACK3-C001  | ✅ Done | `CgfComponentRegistry` created; `CgfApplication` updated; 4 unit tests pass |
| PACK3-U001  | ✅ Done | `UrbanCombatValidator` created with TkbIdentity-based resolution; 3 unit tests pass |
| PACK3-U002  | ✅ Done | `UrbanCombatNewScenario` simplified; delegates `EvaluateTick` to validator |
| PACK3-N001  | ✅ Done | Canonical `NetworkGatewaySystem` in `FDP.Toolkit.Replication.Systems`; 3 unit tests pass |
| PACK3-N003  | ✅ Done | `CycloneNetworkModule` rewired to toolkit system via `using` alias |
| PACK3-N002  | ✅ Done | Deleted `ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs`, `Modules/NetworkGatewayModule.cs`, and orphaned test file |

---

## 🧪 Testing Results

**Unit Tests Passed:** 10 / 10

| Test Class | Project | Result |
|---|---|---|
| `CgfComponentRegistryTests` (4 tests) | `Hrot.ClusterRunner.Integration.Tests` | ✅ Pass |
| `UrbanCombatValidatorTests` (3 tests) | `Fdp.Examples.Scenarios.Tests` | ✅ Pass |
| `NetworkGatewaySystemTests` (3 tests) | `FDP.Toolkit.Replication.Tests` | ✅ Pass |

**Full solution build:** `Build succeeded. 0 Error(s)` (IOS-IG-SimHost.sln)

**Key Test Scenarios Verified:**
- ✅ `CgfComponentRegistry.RegisterAll` registers tier-1, tier-2 (cognitive + kinematic), and tier-3 (IG) components without throwing
- ✅ `UrbanCombatValidator` fires latches in sequence and returns `true` once the insurgent is killed
- ✅ `UrbanCombatValidator` throws `ScenarioFailureException` when tick > 600
- ✅ `NetworkGatewaySystem` ACKs immediately when no `PendingNetworkAck` component is present
- ✅ `NetworkGatewaySystem` ACKs immediately when topology reports zero peers
- ✅ `NetworkGatewaySystem` defers ACK until all peers respond via `ReceiveLifecycleStatus`

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three main issues arose:

1. **`IEntityCommandBuffer.Flush()` does not exist** — The test code initially used `.Flush(repo)` on the command buffer interface. The correct pattern is to cast to the concrete `EntityCommandBuffer` and call `Playback(repo)`. Additionally, `repo.Bus.SwapBuffers()` is needed after Playback to make events visible. Pattern discovered from `SubEntityTests.cs` and `SpawnSystemTests.cs`.

2. **`ConsumeEvents<T>()` returns `ReadOnlySpan<T>` — ref struct incompatible with `Assert.Contains`** — Replaced with `foreach` loops and `bool found` flag assertions. For zero-event assertions, used `Assert.Equal(0, span.Length)`.

3. **`UrbanCombatValidator` failing latch 4 after `DestroyEntity`** — After an entity is destroyed, `world.Query().With<TkbIdentity>().Build()` no longer returns it, so `insFound = false` and latch 4 was unreachable. Fixed by adding `_cachedApc` / `_cachedInsurgent` / `_apcEverFound` / `_insurgentEverFound` fields. After the query loop, if a previously found entity is missing from the query, the cached reference is used. `world.IsAlive(cachedEntity)` then correctly returns false, firing latch 4.

4. **INetworkTopology ambiguity** — Two `INetworkTopology` interfaces exist: `Fdp.Interfaces.INetworkTopology` (takes `long tkbType`) and `ModuleHost.Core.Network.Interfaces.INetworkTopology` (takes `ReliableInitType`). Resolved with `using INetworkTopology = Fdp.Interfaces.INetworkTopology;` alias in files that import both namespaces.

5. **`CycloneEgressSystem` reference lost after rewiring** — Removing `using ModuleHost.Network.Cyclone.Systems;` broke `CycloneEgressSystem`. Kept that `using` directive and added a separate alias `using NetworkGatewaySystem = FDP.Toolkit.Replication.Systems.NetworkGatewaySystem;` so both types resolve correctly.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `NetworkGatewayModule` (deleted) was a thin wrapper over `NetworkGatewaySystem` with a `Tick` method rather than `Execute`. The `Tick` vs `Execute` naming inconsistency between `IEcsModule` and `IEcsModuleSystem` is confusing and could benefit from unification.
- The two `INetworkTopology` interfaces with different `GetExpectedPeers` signatures (`long tkbType` vs `ReliableInitType`) create ongoing ambiguity. A migration to use only the Fdp.Interfaces version throughout would eliminate this.
- `EntityLifecycleModule.AcknowledgeConstruction` currently only publishes a `ConstructionAck` event; it has no guard against double-acknowledgement of the same entity. If a module calls it twice, two events are published.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **Latch 4 fix in `UrbanCombatValidator`**: Chose to cache entity handles rather than restructuring the latch logic or changing the test. Caching is the least-invasive option and preserves the TkbIdentity-resolution intent on normal ticks — the cache only activates when an entity is no longer queryable (i.e., destroyed). An alternative would have been to store entity handles from the first-ever discovery and never re-query, but that would undermine the serialisation-robustness design goal.

- **Deleted `NetworkGatewayModuleTests.cs`**: The test file tested the now-deleted `NetworkGatewayModule` and contained only smoke-tests (`Assert.True(true)`). Keeping it would have left orphaned tests. Coverage is provided by the new `NetworkGatewaySystemTests.cs` which is more behavioural.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- The destroyed-entity latch-4 case in `UrbanCombatValidator` (see Q1 above). Entity destruction removes entities from ECS queries mid-sequence, so cached references are required for any validator that checks `IsAlive` after an entity has been killed.
- `EmptyTopology.GetExpectedPeers` returning an empty enumerable causes `peerSet.Count == 0` in `NetworkGatewaySystem`, which takes the immediate-ACK path. This is separate from the "no `PendingNetworkAck` component" path — both result in immediate ACK but via different code branches. Both are tested.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `UrbanCombatValidator` builds a `Query().With<TkbIdentity>().Build()` every tick. Given the small entity count in the urban combat scenario this is negligible, but for scenarios with thousands of entities this could be expensive. The fix (caching) indirectly mitigates this slightly — once both actors are found they are cached and the loop will `break` immediately on all subsequent ticks.
- `NetworkGatewaySystem._pendingPeerAcks` and `_pendingStartFrame` use `Dictionary<Entity, ...>`. For hot-path scenarios with many simultaneous entity constructions this could be replaced with parallel arrays or pooled structures, but the existing design matches the original Cyclone clone.

---

## ⚠️ Outstanding Issues / Next Steps

- None. All BATCH-01 tasks are complete, all tests pass, and the solution builds cleanly.
- Suggested follow-up (out of scope for this batch): unify the two `INetworkTopology` interfaces and migrate `ModuleHost.Core.Network.Interfaces` callers to `Fdp.Interfaces`.
