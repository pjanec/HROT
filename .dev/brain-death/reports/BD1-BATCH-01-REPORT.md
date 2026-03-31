# BD1-BATCH-01 Report

**Batch:** BD1-BATCH-01 — Core Brain-Death Lifecycle  
**Tasks:** BD1-P1T0a, BD1-P1T0b, BD1-P1T1, BD1-P1T2, BD1-P1T3  
**Date:** 2026-03-19  
**Final Test Results:** FDP.Toolkit.Behavior.Tests — 71 passed, 0 failed | Hrot.SimHost.Tests — 225 passed, 0 failed

---

## ✅ Task Completion Summary

| Task | Description | Status |
|------|-------------|--------|
| BD1-P1T0a | DoctrineFinishedEvent + BTreeTickSystem publish | ✅ Done |
| BD1-P1T0b | ClearDoctrineEvent + DoctrineIngressSystem consume | ✅ Done |
| BD1-P1T1 | ChannelArbitrationSystem OnExit guarantee | ✅ Done |
| BD1-P1T2 | MissionDirectorSystem DoctrineFinished trigger + end-of-plan clear | ✅ Done |
| BD1-P1T3 | MissionControlRequestSystem CMD_ABORT_ALL ClearDoctrineEvent | ✅ Done |

---

## Files Changed

**New files:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DoctrineFinishedEvent.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearDoctrineEvent.cs`

**Modified production code:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs` — publishes `DoctrineFinishedEvent` once per terminal transition, guarded by `_publishedTerminalForInstanceId` dictionary keyed by `entity.Index`.
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs` — added `ConsumeManaged<ClearDoctrineEvent>()` loop that resets `ActiveDoctrineHash`, increments `InstanceId`, zeroes `BrainTier`, and defaults `BrainBTreeState.State`.
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` — replaced `channel = default` with `channel.ActiveAction = 0; unchecked { channel.ActionInstanceId++; }` for all three channel types.
- `FDP/Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs` — added `DoctrineFinished = 4` to `MissionTrigger` enum.
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` — added `case MissionTrigger.DoctrineFinished:` that reads from a per-frame `HashSet<int>` built at the top of `OnUpdate`; added `else` branch on plan exhaustion to publish `ClearDoctrineEvent`.
- `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` — added `ClearDoctrineEvent` publish in `CMD_ABORT_ALL` case; added `using FDP.Toolkit.Behavior.Events`.

**Modified test code:**
- `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/BTreeTickSystemTests.cs` — added 5 new `DoctrineFinishedEvent` tests; added `using FDP.Toolkit.Behavior.Events`.
- `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineIngressSystemTests.cs` — fixed broken `DoctrineInstanceId == 0u` assertion in `DoctrineIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction`; added 4 new `ClearDoctrineEvent` tests.
- `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/ChannelArbitrationTests.cs` — fixed `Arbitration_ClearsStaleChannel` assertions (`Status` and `DoctrineInstanceId` are no longer reset by selective-clear); added 4 new `ChannelClear_ShouldNotZeroActionInstanceId`, `NoPreemption_WhenDoctrineMatches`, `WeaponChannel_ReceivesOnExitSignal`, `InteractionChannel_ReceivesOnExitSignal` tests.
- `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/MissionDirectorSystemTests.cs` — added `using Fbt`, `using FDP.Toolkit.Behavior.Events`; registered `BrainBTreeState` in fixture; added 5 new `DoctrineFinishedTrigger` / `MissionComplete` tests.
- `Hrot.SimHost.Tests/MissionControlRequestSystemTests.cs` — registered `DoctrineState` and `BrainBTreeState` in `CreateWorld()`; added `using FDP.Toolkit.Behavior.Events`; added 3 new `AbortAll_*` tests.

---

## Developer Insights

### Q1: Issues encountered and how they were resolved

**Event bus buffer swap timing.** The ECS event bus uses a double-buffer pattern (`PublishManaged` writes to the write buffer; `ConsumeManaged` reads from the read buffer). In tests, `SwapBuffers()` must be called explicitly between the publishing side and the consuming side. This is straightforward for the `DoctrineIngress` and `MissionDirector` tests where we control both sides, but the `BTreeTickSystem` tests surface a subtle point: after `sys.Run()` the published `DoctrineFinishedEvent` is in the *write* buffer — the test must swap before consuming. Failing to do so silently returns an empty enumerable and the assertion `count == 1` fails with `0`. Added a `SwapBuffers()` call after each `Run()` call in all BTree event-checking tests.

**Broken pre-existing tests due to selective-clear.** The `ChannelArbitrationSystem` was already fixed (`channel.ActiveAction = 0; unchecked { channel.ActionInstanceId++; }`) but two tests still had assertions written for the old `channel = default` approach:
- `Arbitration_ClearsStaleChannel` was asserting `Status == NodeStatus.Failure` and `DoctrineInstanceId == 0u` (both zeroed by `= default`). After the selective-clear those fields are no longer touched.
- `DoctrineIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction` was asserting `DoctrineInstanceId == 0u`.
Both were corrected to match the new intended semantics. The test comments were updated to state that `DoctrineInstanceId` is deliberately preserved (it only changes when a dispatcher `OnEnter` stamps it).

**`MissionDirectorSystem` — DoctrineFinishedEvent consumption build pattern.** The spec suggested a per-entity lookup pattern. The implementation builds a `HashSet<int>` of entity indices at the top of `OnUpdate` (single pass over all `DoctrineFinishedEvent`s), then performs an `O(1)` `Contains` per entity in the query. This is cleaner than nested iteration and prevents double-consumption in the same frame.

### Q2: Weak points / tightly-coupled areas

**`DispatchedInstanceId` is the OnExit trigger — comment debt.** The contract that makes OnExit fire is `ActionInstanceId != DispatchedInstanceId`. After the channel-arbitration fix, `ActionInstanceId` is incremented but `DispatchedInstanceId` is left at its old value, so the dispatcher evaluates the mismatch and fires `OnExit`. This works but the logic is implicit and not documented at the call site in `ChannelArbitrationSystem`. A `// increment triggers dispatcher OnExit` comment was added inline.

**`MissionDirectorSystem` directly mutates `DoctrineState` for phase transitions.** For the `DoctrineFinished` trigger the system now delegates teardown via `ClearDoctrineEvent` (single ownership in `DoctrineIngressSystem`). However, for all other triggers (TimerElapsed, ReachedDestination, etc.) `MissionDirectorSystem` still directly writes `doctrine.ActiveDoctrineHash` and `doctrine.InstanceId`. This dual-write pattern will become problematic if other systems need to hook into phase transitions. The spec doesn't require fixing this now, but it's worth noting as DEBT.

**`BTreeTickSystem._publishedTerminalForInstanceId` never shrinks.** The dictionary accumulates one entry per entity index that has ever reached a terminal doctrine, and is never pruned when entities are destroyed. For long-running simulations with high entity churn this will leak memory. A solution would be to observe entity destruction events or make the guard lazy by storing `(entity.Index, InstanceId)` pairs in a `HashSet<(int, uint)>` and clearing old entries on each new doctrine assignment. Not addressed in this batch per scope.

### Q3: Design decisions beyond the instructions

**`MissionDirectorSystem` — single-pass HashSet vs per-entity ConsumeManaged.** The spec showed pseudo-code with `HasDoctrineFinishedEvent(entity, out var result)` suggesting a helper. I chose a single pre-pass into a `HashSet<int>` at the top of `OnUpdate` rather than calling `ConsumeManaged` inside the entity loop (which would either re-consume on each entity or require draining the bus into a temporary list first). The HashSet approach is O(events + entities) instead of O(events × entities) and matches how the system already handles the query loop.

**`ClearDoctrineEvent` — published unconditionally from `CMD_ABORT_ALL` even if entity has no `DoctrineState`.** The spec says "the ingress system will guard against missing `DoctrineState`". Publishing unconditionally is correct because it keeps `MissionControlRequestSystem` decoupled from the cognitive layer's component layout — it sends a signal and `DoctrineIngressSystem` decides whether to act. The alternative (checking `HasComponent<DoctrineState>` in the request system) would couple the network command path to ECS component knowledge.

**Pre-existing failing tests corrected without a corrective batch.** Two tests with wrong assertions were updated in-place rather than raised as a corrective issue. The root cause was that the tests were written expecting `channel = default` (full reset) before the `ActionInstanceId++` fix landed. Updating the assertions to reflect the documented correct behavior is the right long-term outcome.

### Q4: Edge cases around zero-allocation ClearDoctrineEvent publish

The `ClearDoctrineEvent` is a managed class instance. The `PublishManaged` path allocates one object per publish (no pooling). In `CMD_ABORT_ALL` this happens once per network command, so it's not a hot path. However, `MissionDirectorSystem` can publish one per entity per frame when a single-phase doctrine completes. For large simulations with many simultaneous plan completions this will generate GC pressure. A future improvement would be a pool-backed `ClearDoctrineEvent`, but this is consistent with the existing `AssignDoctrineEvent` and `DoctrineFinishedEvent` patterns.

One edge case: if an entity gets `DoctrineFinishedEvent` published for it while `CurrentPhase >= PhaseCount` (plan already exhausted on a prior tick), the `MissionDirectorSystem` skips via `if (queue.CurrentPhase >= queue.PhaseCount) continue;` before the event is ever checked. The `DoctrineFinishedEvent` is silently discarded (consumed but not acted upon). This is correct because the entity is already brain-dead from the prior `ClearDoctrineEvent`.

### Q5: Performance observations in BTreeTickSystem

The `_publishedTerminalForInstanceId` dictionary lookup (`TryGetValue` + conditional `[]= `) adds two dictionary operations per entity with a registered doctrine each frame. This is negligible at typical simulation scales (hundreds of entities) but worth monitoring if entity counts reach thousands. The existing `Dictionary<int, uint>` uses value types for both key and value, so no GC pressure per lookup.

The `Array.Empty<float>()` / `Array.Empty<int>()` used for `BTreeContext._floatParams` / `_intParams` is correct — `Array.Empty<T>()` returns a cached shared instance (no allocation). The `BTreeContext` struct itself is stack-allocated which is the intended zero-allocation design.
