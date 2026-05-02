# BATCH-12: Phase 6 Completion — HsmDamageBridgeSystem + EmbarkExecutor + EjectPassengersExecutor (BCS-P6-T2, T3)

**Batch Number:** BATCH-12  
**Tasks:** BCS-P6-T2 (`HsmDamageBridgeSystem`), BCS-P6-T3 (`EmbarkExecutor`, `EjectPassengersExecutor`, `PassengerBuffer`, `IsEmbarkedTag`)  
**Phase:** Phase 6 — FDP.Toolkit.Behavior (Advanced) — completion  
**Estimated Effort:** 9–12 hours  
**Priority:** HIGH — Phase 6 completion; Phase 7 (Demo App) depends on all of these  
**Dependencies:** BATCH-11 ✅

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-11 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-11-REVIEW.md`
2. **DESIGN.md §3.2 (Systems table) and §8.2 (Interaction Executors):** `FDP/Docs/projects/behavior-control/DESIGN.md`
3. **TASK-DETAIL.md §BCS-P6-T2 and §BCS-P6-T3:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — read both sections in full.
4. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
5. **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — particularly DEBT-033 (HealthCritical), DEBT-024 (OnExit entity-death gap).
6. **Existing code to read before writing:**
   - `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` — `ActorCapabilityState`, `ActorCapabilities`, `BrainHsm128`, `BrainHsm64`, `BehaviorState`
   - `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DamageSystem.cs` ← understand the capability stripping added in BATCH-11
   - `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` — understand the dispatcher lifecycle
   - CarKinem Core — confirm `NavState`, `VehicleState` field names: `FDP/Toolkits/FDP.Toolkit.CarKinem/Core/`

### Source Locations

| Area | Path |
|---|---|
| **New system** | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmDamageBridgeSystem.cs` ← CREATE |
| **New components** | `FDP/Toolkits/FDP.Toolkit.Behavior/Components/InteractionComponents.cs` ← CREATE |
| **New executors** | `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EmbarkExecutor.cs` ← CREATE |
| | `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` ← CREATE |
| **New tests** | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/HsmDamageBridgeSystemTests.cs` ← CREATE |
| | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/EmbarkExecutorTests.cs` ← CREATE |
| | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/EjectPassengersExecutorTests.cs` ← CREATE |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/    # must gain 9+ new tests
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-12-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. `InteractionComponents` (PassengerBuffer + IsEmbarkedTag) → build passes ✅
2. `HsmDamageBridgeSystem` + tests ✅
3. `EmbarkExecutor` + tests ✅
4. `EjectPassengersExecutor` + tests ✅
5. Full solution green ✅

---

## ✅ Tasks

### Task 1: `HsmDamageBridgeSystem` (BCS-P6-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmDamageBridgeSystem.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P6-T2 — read in full.  
**Design reference:** DESIGN.md §3.2 (`HsmDamageBridgeSystem` row).  
**Phase:** `SimulationSystemGroup`, `[UpdateBefore(typeof(HsmTickSystem<BrainHsm128>))]` — must run before HSM ticks so the mobility-loss event is available in the same frame.

**Goal:** Detect when `ActorCapabilityState.CanMove` is cleared on an entity that has `BrainHsm128` (or `BrainHsm64`); inject `HsmEvent(MobilityLost)` into the HSM's event queue.

**Approach for change detection:**

The system cannot hold per-entity state across frames (it is a stateless `ComponentSystem`). Two valid patterns:
- **Option A (preferred):** Add a shadow component `PreviousCapabilities` to track the last-frame capability bitmask. The bridge system compares `current.Capabilities` vs `previous.Capabilities` each frame; if `CanMove` was set and is now clear, inject the event.
- **Option B:** Only run on entities where `ActorCapabilityState.CanMove` is NOT set. Inject to any that also lack a `MobilityLostInjected` tag. Add the tag after injection. Remove the tag on re-enable (if capabilities are restored). This avoids a shadow component at the cost of a tag component.

Use **Option A** (shadow component). It is simpler to test and avoids tag proliferation.

**`PreviousCapabilities` component:**
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct PreviousCapabilities
{
    public ActorCapabilities Capabilities;
}
```
Add to `BehaviorComponents.cs`. Initialise to the entity's initial capabilities at spawn or on first frame.

**System logic:**
```
foreach entity with ActorCapabilityState + PreviousCapabilities + BrainHsm128:
    bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0
    bool canMoveNow   = (curr.Capabilities & ActorCapabilities.CanMove) != 0
    if (wasAbleToMove && !canMoveNow):
        world.GetHsmInstance<BrainHsm128>(entity).TryEnqueue(HsmEvent.MobilityLost)
    prev.Capabilities = curr.Capabilities   // update shadow
Repeat for BrainHsm64 entities.
```

> ⚠️ Check the actual API for `BrainHsm128`/`BrainHsm64` — look at `BehaviorComponents.cs` and `HsmTickSystem` to understand how to obtain the `HsmInstance` and call `TryEnqueue`. The field may store the instance directly as a value in the component, or the instance may be accessed via `HsmState`. Do NOT guess — read the code first.

**Tests (new file `HsmDamageBridgeSystemTests.cs`):**
```csharp
[Fact] void HsmDamageBridge_InjectsMobilityLostEvent_WhenCanMoveCleared()
// Entity: ActorCapabilityState(CanMove|CanShoot), PreviousCapabilities(same), BrainHsm128.
// First tick: capabilities unchanged → no event injected.
// Strip CanMove from ActorCapabilityState.
// Second tick: bridge detects CanMove was cleared → verifies HsmEventQueue/instance has MobilityLost queued.

[Fact] void HsmDamageBridge_DoesNotInject_WhenCanMoveWasAlreadyClear()
// Entity: CanMove NOT set from the start, PreviousCapabilities also No-CanMove.
// Run bridge. Assert: no event injected.

[Fact] void HsmDamageBridge_DoesNotInject_WhenCanShootClearedButNotCanMove()
// Entity: CanMove set, CanShoot cleared, PreviousCapabilities had both set.
// Run bridge. Assert: no MobilityLost event (only CanShoot changed, not CanMove).

[Fact] void HsmDamageBridge_UpdatesShadowCapabilities_EachTick()
// Run 2 ticks with unchanged capabilities.
// Assert: PreviousCapabilities.Capabilities == ActorCapabilityState.Capabilities after each tick.
```

---

### Task 2: `InteractionComponents` — `PassengerBuffer` + `IsEmbarkedTag` (BCS-P6-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Components/InteractionComponents.cs` ← NEW

```csharp
/// <summary>
/// Fixed passenger roster on a vehicle entity.
/// Managed by EmbarkExecutor (add) and EjectPassengersExecutor (clear).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PassengerBuffer
{
    public const int Capacity = 8;

    // Inline array of Entity handles for passengers.
    public PassengerSlots Passengers;   // [InlineArray(8)] Entity

    public int Count;
}

[System.Runtime.CompilerServices.InlineArray(8)]
public struct PassengerSlots { private Entity _element; }

/// <summary>
/// Tag component on a soldier entity who is currently embarked in a vehicle.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IsEmbarkedTag
{
    /// <summary>The vehicle entity this soldier is aboard.</summary>
    public Entity VehicleEntity;
}
```

---

### Task 3: `EmbarkExecutor` (BCS-P6-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EmbarkExecutor.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P6-T3.  

**This executor is registered to the `InteractionDispatcherSystem`** (action kind `EmbarkVehicle = 1`).

**`EmbarkParams` struct** (≤ 32 bytes, stored in `InteractionChannel.Params`):
```csharp
public struct EmbarkParams
{
    public Entity VehicleEntity;
    public float  MaxBoardingRange;  // metres — default 3.0f
}
```

**`OnEnter`:** Read `EmbarkParams` from channel. Nothing else.

**`Execute`:**
1. Read `EmbarkParams` from `channel.Params`.
2. Guard: if `!world.IsAlive(p.VehicleEntity)` → `channel.Status = Failure; return`.
3. Distance check: `Vector3.Distance(world.GetComponent<SimTransform>(entity).Position, world.GetComponent<SimTransform>(p.VehicleEntity).Position)`. If > `p.MaxBoardingRange` → `channel.Status = Running` (still approaching — locomotion must already be moving the entity).
4. Check `PassengerBuffer.Count < PassengerBuffer.Capacity` — if full → `channel.Status = Failure; return`.
5. Add to buffer: `buffer.Passengers[buffer.Count++] = entity`.
6. Strip capabilities: `caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot)`.
7. Add `IsEmbarkedTag { VehicleEntity = p.VehicleEntity }` to soldier entity.
8. `channel.Status = Success`.

**`OnExit`:** No-op (embark is complete; EjectPassengersExecutor handles the reverse).

**Tests (new file `EmbarkExecutorTests.cs`):**
```csharp
[Fact] void Embark_AddsSoldierToPassengerBuffer_WhenInRange()
// Soldier at (0,0,0), vehicle at (1,0,0), MaxBoardingRange=3f → in range → embark.
// Assert: PassengerBuffer.Count == 1, buffer[0] == soldierEntity.

[Fact] void Embark_DoesNotEmbark_WhenDistanceTooFar()
// Soldier at (0,0,0), vehicle at (100,0,0), MaxBoardingRange=3f.
// Assert: PassengerBuffer.Count == 0, channel.Status == Running.

[Fact] void Embark_StripsCanMove_AndCanShoot_WhenBoarding()
// After successful embark → Assert: CanMove == false, CanShoot == false.

[Fact] void Embark_AddsIsEmbarkedTag()
// After successful embark → Assert: world.HasComponent<IsEmbarkedTag>(soldier) == true.
// Assert: IsEmbarkedTag.VehicleEntity == vehicleEntity.

[Fact] void Embark_ReportsFailure_WhenVehicleNotAlive()
// Destroy vehicle after setting params → Execute → Assert: channel.Status == Failure.
```

---

### Task 4: `EjectPassengersExecutor` (BCS-P6-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P6-T3.

**This executor is registered to the `InteractionDispatcherSystem`** (action kind `EjectPassengers = 3`).

**No params struct** — the ejector operates on the vehicle entity's own `PassengerBuffer`.

**`OnEnter`:** No-op.

**`Execute`:**
1. Get `ref var buffer = ref world.GetComponentRW<PassengerBuffer>(entity)` (vehicle entity).
2. Get vehicle position: `Vector3 vehiclePos = world.GetComponent<SimTransform>(entity).Position`.
3. For `i = 0 .. buffer.Count - 1`:
   - `Entity passenger = buffer.Passengers[i]`
   - Guard: `if (!world.IsAlive(passenger)) continue` (passenger may have died while embarked).
   - Set spawn position: `Vector3 offset = new Vector3((i - buffer.Count / 2f) * 1.5f, -4f, 0f)`. Write to `SimTransform.Position`.

     > **Phase 0 Adaptation (from TASK-DETAIL):** Use `SimTransform` for all position reads/writes. The slot offset formula places passengers on the side of the vehicle (negative Y = to the side in ENU).

   - Restore capabilities: if entity has `ActorCapabilityState`, add `CanMove | CanShoot` back.
   - Remove `IsEmbarkedTag` if present: `world.RemoveComponent<IsEmbarkedTag>(passenger)`.
4. Clear the buffer: `buffer.Count = 0`.
5. `channel.Status = Success`.

**`OnExit`:** No-op.

**Tests (new file `EjectPassengersExecutorTests.cs`):**
```csharp
[Fact] void Eject_RestoresCanMove_AndCanShoot()
// Soldier embarked on vehicle (IsEmbarkedTag set, CanMove|CanShoot stripped).
// Run EjectPassengersExecutor on vehicle.
// Assert: soldier has CanMove == true, CanShoot == true.

[Fact] void Eject_RemovesIsEmbarkedTag()
// Assert: world.HasComponent<IsEmbarkedTag>(soldier) == false after eject.

[Fact] void Eject_ClearsPassengerBuffer()
// Vehicle with 2 passengers. Run eject.
// Assert: buffer.Count == 0.

[Fact] void Eject_SkipsDeadPassengers_Gracefully()
// Soldier entity in buffer is destroyed before eject runs.
// Assert: no crash. Remaining live passengers processed correctly.
```

---

## 🧪 Testing Requirements

- **Minimum 13 new tests:** 4 HsmDamageBridge + 5 Embark + 4 Eject.
- **All 29 existing `FDP.Toolkit.Behavior.Tests` remain green.**
- **No mocking of `EntityRepository`** — real world with real components.
- **`HsmDamageBridge_DoesNotInject_WhenCanMoveWasAlreadyClear` is mandatory** — proves the shadow component check fires only on the *transition*, not on states.
- **`Embark_DoesNotEmbark_WhenDistanceTooFar` is mandatory** — proves the distance guard.

---

## ⚠️ Quality Standards

**❗ Read `BrainHsm128`/`BrainHsm64` API before writing `HsmDamageBridgeSystem`** — the exact method to enqueue an HSM event is not guessed. Look at `HsmTickSystem` to see how the instance is accessed and what API it exposes. If `TryEnqueue` is not available, document in Q1 what the actual API is.

**❗ `EmbarkExecutor` and `EjectPassengersExecutor` must use `SimTransform` for all position reads and writes** — no `VehicleState.Position` references anywhere.

**❗ `PreviousCapabilities` component** — requires registration in any test world that uses `HsmDamageBridgeSystem`. Add to the existing test world factories if needed.

**❗ `PassengerSlots` and `IsEmbarkedTag`** — check that `IsEmbarkedTag` is an unmanaged struct. `Entity` is a value type (Index + Generation as ints) so it is blittable. `PassengerSlots [InlineArray(8)]` is blittable.

**❗ Dead passenger guard in `EjectPassengersExecutor`** — `IsAlive` check before restoring capabilities. A passenger killed by `DamageSystem` in the same frame will have no component storage; `GetComponentRW` on a dead entity throws in `FDP_PARANOID_MODE`.

**❗ No raw literals in production code** — `PassengerBuffer.Capacity`, `MaxBoardingRange` from `EmbarkParams`.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-12-REPORT.md`

**Q1:** How did you access `BrainHsm128` / `BrainHsm64` to enqueue `MobilityLost`? What is the exact API (`TryEnqueue`, `Push`, etc.)? Did you need to get the component as a `ref` or was it accessible via a static pool?

**Q2:** `HsmDamageBridgeSystem` uses a per-entity shadow component (`PreviousCapabilities`). How did you initialise it for entities that had `ActorCapabilityState` but no `PreviousCapabilities` yet (i.e., the first frame after a new entity is spawned)?

**Q3:** In `EjectPassengersExecutor`, the passenger slot offset formula `(i - buffer.Count / 2f) * 1.5f` places soldiers relative to the vehicle. Is this formula correct for 0–7 passengers? Show the computed offsets for Count=2, Count=4. If the formula has an issue, describe what you changed and why.

**Q4:** Any design decisions or edge cases beyond the spec?

---

## 🎯 Success Criteria

- [ ] `InteractionComponents` — `PassengerBuffer` (Capacity=8, InlineArray) + `IsEmbarkedTag`; both unmanaged structs.
- [ ] `HsmDamageBridgeSystem` — detects `CanMove` cleared, injects `MobilityLost` into HSM; 4 tests pass.
- [ ] `EmbarkExecutor` — distance check, buffer add, capability strip, tag add; 5 tests pass.
- [ ] `EjectPassengersExecutor` — buffer clear, capability restore, tag remove, dead-passenger guard; 4 tests pass.
- [ ] Full solution: 0 errors.
- [ ] All tests green.
- [ ] Report submitted.

---

## 📚 Reference Materials

- **BATCH-11 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-11-REVIEW.md`
- **TASK-DETAIL.md §BCS-P6-T2, T3:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
- **DESIGN.md §3.2 + §8.2:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **BehaviorComponents.cs:** `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs`
- **HsmTickSystem:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` — understand HSM instance access
- **ChannelArbitrationSystem:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
