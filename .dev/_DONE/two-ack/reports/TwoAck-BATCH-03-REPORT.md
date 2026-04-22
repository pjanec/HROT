# TwoAck-BATCH-03 Report

**Batch:** TwoAck-BATCH-03  
**Date:** 2026-03-22  
**Status:** Complete

---

## 📊 Task Completion

| Task ID        | Status | Notes |
|----------------|--------|-------|
| CORRECTIVE-002 | ✅ Done | SimHost integration tests restored — 28/28 pass |
| CORRECTIVE-003 | ✅ Done | Runner integration tests green — 31/31 pass |
| DEBT-ARCH-001  | ✅ Done | `createEntityAckQueue` mandatory across all callsites |

---

## 🧪 Testing Results

**Unit Tests Passed:** 869 / 869 (IOS.Tests 310, DataModel.Tests 33, MapCommon.Tests 88, Runner.Tests 112, SimHost.Tests 326)  
**Integration Tests Passed:** 59 / 59 (SimHost.Integration 28, Runner.Integration 31)  
**Total:** 928 / 928 — zero failures

**Key Test Scenarios Verified:**
- [x] `EntityCreationFlowTests` — full IOS→SimHost→ACK round-trip now returns `StatusCode=0`
- [x] `NavComponentsPresenceTests` — all 4 nav-components tests unblocked
- [x] `MissionExecutionFlowTests` — entity creation helper returns terminal Success ACK
- [x] `PerformanceTests` — spawn burst acknowledges correctly under Two-ACK
- [x] `FirstSpawn_DoesNotExhaustIdPool` — InProgress ACK correctly bypassed
- [x] `EndToEnd_PlacementFlow_SpawnsAndDistributesEntity` — terminal ACK awaited
- [x] `EndToEnd_DirectCreationTool_SpawnsEntityInSimHost` — terminal ACK awaited
- [x] `EndToEnd_AreaAuthoring_PublishesOverlayAndIgReceivesPolyline` — terminal ACK awaited
- [x] All IOS unit tests pass with mandatory `createEntityAckQueue`

---

## 🗂️ Files Changed

| File | Change |
|------|--------|
| `Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | Added `SstRequestFinalizationSystem` field; wired into constructor and `Tick()` after `ActivateConstructingEntities()`; `CreateEntity()` uses `TryGetTerminalAck`; added `TryGetTerminalAck` to `StubAckSink` |
| `Hrot.SimHost.Integration.Tests/Infrastructure/MockIOSClient.cs` | `WaitForAckAsync` now calls `TryGetTerminalAck` in both poll and final-check paths |
| `Hrot.ClusterRunner.Integration.Tests/MiniIosIntegrationTests.cs` | `TryTakeCreateAck` skips `InProgress` (returns `false` to let `PumpUntil` retry) |
| `Hrot.ClusterRunner.Integration.Tests/MapPlacementIntegrationTests.cs` | Same `TryTakeCreateAck` fix |
| `Hrot.ClusterRunner.Integration.Tests/AreaAuthoringIntegrationTests.cs` | Same `TryTakeCreateAck` fix |
| `Hrot.ExCon/IosLogic.cs` | `createEntityAckQueue` made mandatory (no `= null` default); field changed to non-nullable; null guard removed from `ProcessEntityCreationAcks`; XML doc updated |
| `Hrot.ExCon.Tests/IosLogicTests.cs` | Both `CreateSut` and `CreateSutWithCommandWriter` pass `new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>()` |
| `Hrot.ExCon.Tests/IosMockTests.cs` | `CreateSut` passes `new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>()` |
| `Hrot.ExCon.Tests/WorkflowTests.cs` | `WorkflowFixture` passes `new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>()` |
| `Hrot.ExCon.Tests/MultiIosIntegrationTests.cs` | `MultiIosFactory.CreateClients` passes `new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>()` |
| `Hrot.ExCon.Tests/IntegrationTests.cs` | `IntegrationFactory.Create` passes `new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>()` |

---

## 💡 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The BATCH-02 review mentioned 17 + 1 = 18 failing tests. On investigation, the runner failure count was actually 3 (not 1): `MiniIosIntegrationTests`, `MapPlacementIntegrationTests`, and `AreaAuthoringIntegrationTests` each contained their own private `TryTakeCreateAck` helper that was not Two-ACK-aware. The fix (discard InProgress ACKs and return `false` to allow `PumpUntil` to retry) was applied uniformly to all three files.

The SimHost integration fix required two coordinated changes: wiring `SstRequestFinalizationSystem` into the test harness's `Tick()` loop (after `ActivateConstructingEntities()` so the Active lifecycle is observable) AND updating `WaitForAckAsync` / `TryGetAck` to return only terminal ACKs. Neither change alone was sufficient — without the finalization system, Success ACKs were never emitted; without `TryGetTerminalAck`, the first InProgress ACK was returned immediately.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The `TryTakeCreateAck` helper is copy-pasted across three separate test files (`MiniIosIntegrationTests`, `MapPlacementIntegrationTests`, `AreaAuthoringIntegrationTests`) with identical signatures. A shared `RunnerTestHelpers` static class (or base class fixture) would prevent this kind of silent skew. Any future protocol change would currently require updating three independent files.

Similarly, `SimHostInstance.CreateEntity()` returned the raw first ACK from `TryGetAck`. The method is now semantically correct (terminal ACK via `TryGetTerminalAck`), but it's worth noting that the method returns a `CreateUpdateDeleteEntityAck` struct — callers that were only interested in `EntityId` were incidentally relying on the fact that the InProgress ACK also carries it. Moving to terminal-only makes the contract explicit and removes ambiguity.

**Q3: What design decisions did you make beyond the instructions? How did you resolve them?**

The batch instructions specified fixing `WaitForAckAsync` in `MockIOSClient` and patching `MiniIosIntegrationTests`. After running all tests, I discovered the same pattern existed in two additional test files. Rather than leaving them broken, I applied the same Three-line fix uniformly — this is the spirit of CORRECTIVE-003.

For `StubAckSink`, I chose to add a new `TryGetTerminalAck(Guid)` method alongside the existing `TryGetAck(Guid)` rather than changing `TryGetAck`'s semantics. This preserves backward compatibility for any internal code that legitimately needs to detect the InProgress ACK (e.g., a hypothetical future monitor verifying Phase-1 latency).

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

The test harness's `CreateEntity()` method was implicitly broken by the Two-ACK change — it called `TryGetAck` (returning InProgress) and the returned ACK's `StatusCode=1` caused `Assert.Equal(0, ack.StatusCode)` failures in `MissionExecutionFlowTests`. Updating `CreateEntity()` to call `TryGetTerminalAck` fixed this without changing the method's public signature.

The `ActivateConstructingEntities()` → `_finalizationSystem.Execute()` ordering is critical: entities must be Active before the system fires, otherwise the Success ACK is never sent. Running `_finalizationSystem.Execute()` outside the `if (RequestSource.HasPendingRequests)` block ensures it also processes entities spawned in previous ticks (relevant for multi-tick scenarios or future tests without the force-activation shortcut).

**Q5: Are there any performance concerns or optimization opportunities?**

`StubAckSink.TryGetTerminalAck` iterates a `List<T>` linearly. For the test harness (< 100 ACKs per test) this is unimportant. In a theoretical production sink that accumulates many ACKs, a lookup-by-requestId dictionary would be more efficient — but the stub is test-only code and should remain simple.

`SstRequestFinalizationSystem` executes unconditionally every tick (it short-circuits at `if (_tracked.Count == 0) return`). In the test harness this adds negligible overhead since the tracked set is tiny and cleared immediately on the same spawn tick.
