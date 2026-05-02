# BD1-BATCH-02 Report

**Batch:** BD1-BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2026-03-19  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CORRECTIVE-0 | ✅ Complete | Both events converted to unmanaged structs; all call sites updated |
| CORRECTIVE-1 | ✅ Complete | Pruning added to BTreeTickSystem; TrackedEntityCount property exposed for test |
| CORRECTIVE-2 | ✅ Complete | AssignBehaviorHashEvent introduced; MissionDirectorSystem no longer writes BehaviorState directly |
| BD1-P2T1 | ✅ Complete | Brain-aware right-click handler with extracted testable method |

---

## 🧪 Testing Results

**FDP.Toolkit.Behavior.Tests:** 72 / 72 passed  
**Hrot.SimHost.Tests:** 229 / 229 passed

**Key Test Scenarios Verified:**

- [x] `BTreeTickSystemTests.BehaviorRoot_Success_PublishesBehaviorFinishedEvent` — unmanaged Publish/Consume
- [x] `BTreeTickSystemTests.BehaviorRoot_Success_PublishedOnlyOnce` — deduplication still works  
- [x] `BTreeTickSystemTests.DestroyedEntity_PrunedFromTerminalTrackingDictionary` — CORRECTIVE-1 pruning
- [x] `BehaviorIngressSystemTests.ClearBehaviorEvent_SetsBehaviorToNone` — unmanaged ClearBehaviorEvent
- [x] `BehaviorIngressSystemTests.ClearVsAssign_AreIndependent` — mixed event types in same frame
- [x] `MissionDirectorSystemTests.MissionDirector_AdvancesPhase_WhenTimerElapses` — CORRECTIVE-2 delegation pipeline
- [x] `MissionDirectorSystemTests.BehaviorFinishedTrigger_MultiPhase_SetsNextBehavior` — phase advance via event bus
- [x] `MissionDirectorSystemTests.MissionComplete_ViaBehaviorIngress_SetsBehaviorToNone` — end-to-end behavior clear
- [x] `MissionControlRequestSystemTests.AbortAll_PublishesClearBehaviorEvent` — unmanaged ClearBehaviorEvent from SimHost
- [x] `SimHostVisualizationTests.RightClick_BrainDead_CallsSetDestination` — brain-dead path
- [x] `SimHostVisualizationTests.ShiftRightClick_BrainDead_CallsAddWaypoint` — shift+click brain-dead
- [x] `SimHostVisualizationTests.RightClick_BrainActive_WritesMissionWithTrigger` — ReachedDestination trigger present

---

## 📝 Developer Insights

**Q1: What issues did you encounter during the unmanaged conversion? How were they resolved?**

The `Publish<T>()` / `Consume<T>()` API for unmanaged events requires the event type to be decorated with `[EventId(uniqueId)]`. This attribute was not on the original class-based events (which used `PublishManaged` and had no such requirement). The fix was:
1. Add `EventId_ClearBehavior = 3100` and `EventId_BehaviorFinished = 3101` constants to `BehaviorConstants.cs`.
2. Decorate each struct with `[EventId(BehaviorConstants.EventId_XxxYyy)]`.

A range of 3100–3199 was chosen for behavior behavior events — clearly between the existing Fire Interaction event (3001) and Perception events (4001–4003) and not conflicting with anything in use.

The second issue was test updates: `ConsumeManaged<T>()` returns `IReadOnlyList<T>` while `Consume<T>()` returns `ReadOnlySpan<T>`. This meant removing all `if (evt == null)` null-guards (structs cannot be null) and updating how `BehaviorFinishedEvent?` nullable struct are accessed via `.Value.Result` instead of `.Result`.

**Q2: Are there any edge cases with the Right-Click path determination?**

- **No `BehaviorState` component vs. zero hash**: Both treated as brain-dead. An entity spawned via `SpawnEntityLocal` (no `BehaviorState`) correctly goes to the muscle path; an entity that completed a mission and got brain-killed via `ClearBehaviorEvent` also goes to the muscle path because `ActiveBehaviorHash == BehaviorIds.None`.
- **Brain-active entity without `NetworkIdentity`**: If an entity has an active behavior but no `NetworkIdentity`, the handler returns early without calling either path. This is correct — such an entity cannot receive a DDS mission command.
- **Shift+click for brain-active entities**: Per spec, behaves identically to plain click (sends `CMD_REPLACE_MISSION`). This is a known limitation for a future increment.
- **Null `missionWriter`**: `missionWriter?.Write(...)` guards against a null writer so the method is safe to call in unit tests without a real DDS participant.

---

## ⚠️ Outstanding Issues / Next Steps

- CORRECTIVE-2 introduces a **one-frame delay** for behavior activation on phase advance: `MissionDirectorSystem` publishes `AssignBehaviorHashEvent` in the SimulationSystemGroup; `BehaviorIngressSystem` processes it in the next frame's InputSystemGroup. This is the correct event-driven architecture but means `BehaviorState.ActiveBehaviorHash` is one frame behind after a transition. `MissionAdapterSystem` (SimHost-side) still publishes an `AssignBehaviorEvent` for the same entity in the following MAS update cycle, providing the redundant write that keeps BrainTier aligned.
- The test for CORRECTIVE-2 (`MissionDirectorSystemTests`) now requires `BehaviorIngressSystem` in the test fixture and a `FlushBehaviorEvents()` helper between `_sys.Run()` and hash assertions. This is the correct way to verify the full delegation pipeline.
- Shift+right-click for brain-active entities (brain-active waypoint queuing) is deferred to a future increment as noted in the spec.
