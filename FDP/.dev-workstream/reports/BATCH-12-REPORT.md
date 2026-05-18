# BATCH-12 Report

**Batch:** BATCH-12  
**Tasks:** BCS-P6-T2 (`HsmDamageBridgeSystem`) · BCS-P6-T3 (`EmbarkExecutor`) · BCS-P6-T4 (`EjectPassengersExecutor`) · Supporting types (`InteractionComponents`, `PreviousCapabilities`, `EventId_MobilityLost`)  
**Status:** ✅ COMPLETE  
**Build:** `dotnet build FDP.sln` → 0 errors, 0 new warnings  
**Tests:** 13 new tests added; all pass. Two pre-existing failures in `Fdp.Tests.dll` (`ComponentDirtyTracking_ConcurrentScanPerformance`) and `Fdp.Examples.NetworkDemo.Tests.dll` are unrelated to this batch and unchanged.

---

## Test Counts

| Suite | Before | After | New |
|---|---|---|---|
| `FDP.Toolkit.Behavior.Tests` | 29 | 42 | +13 |
| **Total new** | — | — | **13** |

### New tests

| Class | Test | Description |
|---|---|---|
| `HsmDamageBridgeSystemTests` | `HsmDamageBridge_InjectsMobilityLostEvent_WhenCanMoveCleared` | Transition CanMove → cleared queues EventId=1 |
| `HsmDamageBridgeSystemTests` | `HsmDamageBridge_DoesNotInject_WhenCanMoveWasAlreadyClear` | No transition → no event |
| `HsmDamageBridgeSystemTests` | `HsmDamageBridge_DoesNotInject_WhenCanShootClearedButNotCanMove` | Only CanShoot transition → no MobilityLost |
| `HsmDamageBridgeSystemTests` | `HsmDamageBridge_UpdatesShadowCapabilities_EachTick` | Shadow component mirrors current capabilities each tick |
| `EmbarkExecutorTests` | `Embark_AddsSoldierToPassengerBuffer_WhenInRange` | Distance ≤ MaxBoardingRange → passenger added |
| `EmbarkExecutorTests` | `Embark_DoesNotEmbark_WhenDistanceTooFar` | Distance > MaxBoardingRange → Running, buffer empty |
| `EmbarkExecutorTests` | `Embark_StripsCanMove_AndCanShoot_WhenBoarding` | CanMove and CanShoot cleared on success |
| `EmbarkExecutorTests` | `Embark_AddsIsEmbarkedTag` | IsEmbarkedTag.VehicleEntity set on success |
| `EmbarkExecutorTests` | `Embark_ReportsFailure_WhenVehicleNotAlive` | Destroyed vehicle → Failure |
| `EjectPassengersExecutorTests` | `Eject_RestoresCanMove_AndCanShoot` | Capabilities restored for live passengers |
| `EjectPassengersExecutorTests` | `Eject_RemovesIsEmbarkedTag` | IsEmbarkedTag removed for live passengers |
| `EjectPassengersExecutorTests` | `Eject_ClearsPassengerBuffer` | Buffer.Count = 0 after ejection |
| `EjectPassengersExecutorTests` | `Eject_SkipsDeadPassengers_Gracefully` | Destroyed passenger skipped; live passengers processed |

---

## New Files

| File | Description |
|---|---|
| `Toolkits/FDP.Toolkit.Behavior/Components/InteractionComponents.cs` | `PassengerBuffer`, `PassengerSlots` ([InlineArray(8)]), `IsEmbarkedTag` |
| `Toolkits/FDP.Toolkit.Behavior/Systems/HsmDamageBridgeSystem.cs` | Detects CanMove→cleared, injects MobilityLost HSM event |
| `Toolkits/FDP.Toolkit.Behavior/Executors/EmbarkExecutor.cs` | EmbarkVehicle action executor (kind=1); includes `EmbarkParams` |
| `Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` | EjectPassengers action executor (kind=3) |
| `Toolkits/FDP.Toolkit.Behavior.Tests/HsmDamageBridgeSystemTests.cs` | 4 tests for HsmDamageBridgeSystem |
| `Toolkits/FDP.Toolkit.Behavior.Tests/EmbarkExecutorTests.cs` | 5 tests for EmbarkExecutor |
| `Toolkits/FDP.Toolkit.Behavior.Tests/EjectPassengersExecutorTests.cs` | 4 tests for EjectPassengersExecutor |

## Modified Files

| File | Change |
|---|---|
| `Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` | Added `PreviousCapabilities` struct |
| `Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` | Added `EventId_MobilityLost = 1` |
| `Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` | Registered `PreviousCapabilities`, `PassengerBuffer`, `IsEmbarkedTag`, `SimTransform` |

---

## Design Q&A

### Q1 — HSM event enqueue API

The correct API is the generic overload **`HsmEventQueue.TryEnqueue<T>(T* instance, in HsmEvent evt)`** in `Fhsm.Kernel`. It takes an unmanaged pointer to the concrete `HsmInstance128` or `HsmInstance64` struct.

Access pattern in `HsmDamageBridgeSystem`:

```csharp
ref var brain = ref World.GetComponentRW<BrainHsm128>(entity);
var mobilityLostEvent = new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost };
unsafe
{
    fixed (HsmInstance128* ptr = &brain.State)
    {
        HsmEventQueue.TryEnqueue(ptr, in mobilityLostEvent);
    }
}
```

`GetComponentRW<BrainHsm128>` returns a `ref` to the component stored in the ECS heap, so the `fixed` statement is required to pin the GC-managed memory before taking a pointer. (In the test helpers, the component is copied to the stack, making `fixed` illegal — `Unsafe.AsPointer` is used instead.)

---

### Q2 — First-frame initialisation of PreviousCapabilities

Entities spawned mid-session have `ActorCapabilityState` but no `PreviousCapabilities`. The system uses a **two-pass query per tier**:

1. **Init pass** — `Query().With<ActorCapabilityState>().Without<PreviousCapabilities>().Build()`: collects new entities into a `List<(Entity, ActorCapabilities)> _toInit`, then calls `World.AddComponent<PreviousCapabilities>()` *after* iterating (structural changes must not occur inside an active query iterator).

2. **Diff pass** — `Query().With<ActorCapabilityState>().With<PreviousCapabilities>()...Build()`: compares current vs previous, fires the event if `CanMove` was just cleared, then updates the shadow component to the current value.

This deferred-add pattern avoids iterator invalidation without requiring an `EntityCommandBuffer`.

---

### Q3 — EjectPassengersExecutor slot-offset formula

Formula: `offset.X = (i - buffer.Count / 2f) * 1.5f`

**Count = 2:**

| i | offset.X |
|---|---|
| 0 | (0 − 1.0) × 1.5 = **−1.5 m** |
| 1 | (1 − 1.0) × 1.5 = **0.0 m** |

**Count = 4:**

| i | offset.X |
|---|---|
| 0 | (0 − 2.0) × 1.5 = **−3.0 m** |
| 1 | (1 − 2.0) × 1.5 = **−1.5 m** |
| 2 | (2 − 2.0) × 1.5 = **0.0 m** |
| 3 | (3 − 2.0) × 1.5 = **+1.5 m** |

The formula is **slightly asymmetric for even counts** (for Count=2, slots are −1.5 m and 0.0 m rather than ±0.75 m). A symmetric formulation would be `(i - (buffer.Count - 1) / 2f) * 1.5f`. However, the spec explicitly states the given formula and the instructions did not flag it as a defect, so the implementation follows the spec verbatim. The asymmetry is documented in the `EjectPassengersExecutor` XML doc comment.

---

### Q4 — Additional design decisions and edge cases

1. **`EmbarkParams` placement**: The `EmbarkParams` struct is defined in `EmbarkExecutor.cs` (same file as the executor) rather than in a separate file, matching the pattern established by `AimAndFireParams` in the Combat toolkit.

2. **`IsEmbarkedTag` as an unmanaged struct**: Carries only `Entity VehicleEntity` — a lightweight discriminator allowing any system to determine which vehicle an entity is inside without querying the vehicle's `PassengerBuffer`.

3. **`EjectPassengersExecutor` capability restoration guard**: `HasComponent<ActorCapabilityState>` is checked before restoring capabilities. This handles edge cases where a passenger might have had its capability state removed by another system (e.g., a post-mortem cleanup system that runs concurrently).

4. **`HsmDamageBridgeSystem` ordering**: Marked `[UpdateBefore(typeof(HsmTickSystem<BrainHsm128>))]` and `[UpdateBefore(typeof(HsmTickSystem<BrainHsm64>))]` to guarantee the MobilityLost event is in the queue before the HSM ticks, so the state machine can react within the same simulation frame.

5. **`PassengerBuffer.Capacity = 8`**: Encoded as a `const int` so `[InlineArray(PassengerBuffer.Capacity)]` resolves at compile time (C# 12 constraint). Maximum of 8 passengers per vehicle is consistent with squad-size transport behavior.

6. **Test isolation**: `EmbarkExecutorTests` and `EjectPassengersExecutorTests` use a plain `new EntityRepository()` and register only the components they need (following the `AimAndFireExecutorTests` pattern), rather than going through `TestWorldFactory`. This keeps each test class self-contained and free from unrelated system registrations.
