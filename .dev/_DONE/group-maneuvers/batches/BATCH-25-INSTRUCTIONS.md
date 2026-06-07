# BATCH-25 Instructions — Phase 4 Part 1: Member Considerations, Veto Detection, MovementMode

**Covers:** TASK-SQD-P4-01, TASK-SQD-P4-02, TASK-SQD-P4-04  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §6, §9

---

## Context

All Phase 0-3 work is committed. Key new additions from earlier batches:
- `SquadCognitiveState.Flags` — bit 0 = MissionOverride, bits 8-9 = **reserved for this batch** (MovementMode)
- `state.Roles` (RoleAssignmentArray, 16 × RoleSlot{RoleId: byte, _pad: byte})
- `state.Slots` (SlotAssignmentArray, 12 × SlotState{ElementIndex, SlotKind, Flags, LastTransitionTick})
- `state.Elements.MemberElements` (MemberElementIndexArray, 16 × byte) — member-to-element index

---

## Task 1 (P4-01): `AssignedRole` + `AssignedSlot` Utility input readers

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs`

### 1a. New `SquadInputIds` constants

Compute FNV-1a-16 (same algorithm as previous IDs):
- `AssignedRole` — FNV-1a-32("AssignedRole") & 0xFFFF
- `AssignedSlot` — FNV-1a-32("AssignedSlot") & 0xFFFF

Document the constants with FNV-1a provenance comments.

### 1b. Reader: `AssignedRole(in UtilityInputCtx ctx)`

**ctx.Self = member entity** (NOT commander; these are member-side readers).

```
[UtilityInput("AssignedRole")]
public static float AssignedRole(in UtilityInputCtx ctx)
```

Algorithm:
1. Resolve commander: `ctx.Self` must have `UnitSubordinate`; get `sub.Commander`.
2. Default safe: return 0f if no `UnitSubordinate`, or commander is `Entity.Null`, or commander has no `Blackboard1024`, or commander has no `UnitRoster`.
3. Get `state = SquadCognitiveState.Project(blackboard)`.
4. Find member's roster index: scan `roster.SubordinateEntities[m]` for `(long)ctx.Self.PackedValue`; return 0f if not found.
5. Get `targetRoleId = (byte)(ctx.Params.BlueprintId & 0xFF)` (caller encodes the expected RoleId byte into `Params.BlueprintId`).
6. Read role span via `MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16)`.
7. Return 1f if `roleSpan[memberIndex].RoleId == targetRoleId`, else 0f.

### 1c. Reader: `AssignedSlot(in UtilityInputCtx ctx)`

```
[UtilityInput("AssignedSlot")]
public static float AssignedSlot(in UtilityInputCtx ctx)
```

Algorithm:
1. Same commander resolution as `AssignedRole` (steps 1-4).
2. Get member's element index: `memberElementIndex = state.Elements.MemberElements[memberIndex]`.
3. Get `targetSlotKind = (byte)(ctx.Params.BlueprintId & 0xFF)`.
4. Read slot span via `MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<SlotAssignmentArray, SlotState>(ref Unsafe.AsRef(in state.Slots)), 12)`.
5. Scan slots: return 1f if ANY slot has `slot.ElementIndex == memberElementIndex && slot.SlotKind == targetSlotKind`. Return 0f otherwise.

### 1d. Register both in `RegisterAll()`

---

## Task 2 (P4-02): `SquadVetoDetectionSystem`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadVetoDetectionSystem.cs`

```
namespace Fdp.Toolkit.Squad.Systems
```

```csharp
/// <summary>
/// Detects when a squad member's active behavior diverges from the squad leader's
/// assignment for more than <see cref="_vetoConfirmTicks"/> consecutive ticks,
/// then emits a <see cref="PhaseEvent"/> with kind <see cref="PhaseEventKind.VetoDetected"/>.
/// </summary>
/// <remarks>
/// Veto is emitted into a caller-provided event list (zero allocation on the hot path).
/// Hysteresis: a single-tick divergence does NOT trigger a veto; the divergence must
/// persist for at least <see cref="_vetoConfirmTicks"/> consecutive ticks.
/// </remarks>
public sealed class SquadVetoDetectionSystem
```

Fields:
```csharp
    private readonly uint _vetoConfirmTicks;
    // Per-member divergence counter (roster-slot indexed, max 16 members).
    private VetoCounterArray _vetoCounters;

    [InlineArray(16)]
    private struct VetoCounterArray
    {
#pragma warning disable CS0169
        private byte _element;
#pragma warning restore CS0169
    }
```

Constructor: `public SquadVetoDetectionSystem(uint vetoConfirmTicks = 3)`

Key method:
```csharp
/// <param name="repo">Active ECS repository.</param>
/// <param name="commander">Commander entity (must have UnitRoster and Blackboard1024).</param>
/// <param name="expectedHashByRole">
/// Mapping from RoleId (byte, 0-based) to the expected BehaviorState.ActiveBehaviorHash.
/// Index = RoleId; 0 = unassigned (always no-veto).
/// The caller provides this at the maneuver level so the system stays generic.
/// </param>
/// <param name="vetoEvents">
/// Output list; receives one <see cref="PhaseEvent"/> per vetoDetected member per call.
/// Caller must pre-clear if desired.
/// </param>
public void Run(
    EntityRepository repo,
    Entity commander,
    ReadOnlySpan<int> expectedHashByRole,
    IList<(int memberSlot, PhaseEvent evt)> vetoEvents)
```

Algorithm:
1. Guard: commander has `UnitRoster` and `Blackboard1024`.
2. Project `SquadCognitiveState`.
3. For each member `m` in `roster`:
   a. Get `roleId = roleSpan[m].RoleId`. If `roleId == 0` (unassigned), reset counter, skip.
   b. If `roleId >= expectedHashByRole.Length`, reset counter, skip.
   c. Get expected hash = `expectedHashByRole[roleId]`.
   d. Get member's `BehaviorState.ActiveBehaviorHash` (default safe: if no `BehaviorState`, reset counter, skip).
   e. If actual != expected: increment `_vetoCounters[m]`. If `_vetoCounters[m] >= _vetoConfirmTicks`, add `(m, new PhaseEvent(PhaseEventKind.VetoDetected))` to `vetoEvents`.
   f. Else (actual == expected): reset `_vetoCounters[m] = 0`.

---

## Task 3 (P4-04): `MovementMode` + `SquadMovementModeBroadcastSystem`

### 3a. `MovementMode` enum + constant

**In `SquadCognitiveState.cs`** (add near the top, before `SquadCognitiveState` struct), add:

```csharp
/// <summary>
/// Squad movement mode broadcast to members by <see cref="SquadMovementModeBroadcastSystem"/>.
/// </summary>
public enum MovementMode : byte
{
    Default = 0,
    Covered = 1,
    Fast    = 2
}
```

Add constants to a new or existing file for squad flag bits:

In `SquadCognitiveState` (or a companion static class), document the flag allocation:
```csharp
// Flags bit layout:
// bit 0  = MissionOverrideBit (set by Phase 3)
// bit 1  = (reserved)
// ...
// bits 8-9 = MovementMode (0=Default, 1=Covered, 2=Fast)
```

In the `SquadMovementModeBroadcastSystem` use:
```csharp
private const uint MovementModeMask  = 0x0300u;  // bits 8-9
private const int  MovementModeShift = 8;
```

### 3b. `MovementModeIntent` component

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Components/MovementModeIntentComponent.cs`

First, add a new `GlobalComponentIds` entry for it. Check `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — the Squad block (256-299) already has 256/257/258. Add:

```csharp
/// <summary><c>MovementModeIntent</c> — per-member movement mode intent broadcast by the squad (Squad toolkit).</summary>
public const int MovementModeIntent = 259;
```

Then create the component:
```csharp
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MovementModeIntent)]
    public struct MovementModeIntent
    {
        public MovementMode Mode;
    }
}
```

### 3c. `SquadMovementModeBroadcastSystem`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadMovementModeBroadcastSystem.cs`

```csharp
/// <summary>
/// Reads bits 8-9 of <see cref="SquadCognitiveState.Flags"/> from the commander's
/// blackboard and broadcasts the resulting <see cref="MovementMode"/> to all members
/// by writing their <see cref="MovementModeIntent"/> component.
/// </summary>
public static class SquadMovementModeBroadcastSystem
{
    private const uint MovementModeMask  = 0x0300u;
    private const int  MovementModeShift = 8;

    public static void Run(EntityRepository repo, Entity commander)
    {
        if (!repo.HasComponent<UnitRoster>(commander)) return;
        if (!repo.HasComponent<Blackboard1024>(commander)) return;

        ref readonly var state = ref SquadCognitiveState.Project(
            ref repo.GetComponentRW<Blackboard1024>(commander));
        var mode = (MovementMode)((state.Flags & MovementModeMask) >> MovementModeShift);

        ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
        for (int m = 0; m < roster.Count; m++)
        {
            var member = new Entity((ulong)roster.SubordinateEntities[m]);
            if (!repo.HasComponent<MovementModeIntent>(member)) continue;
            repo.GetComponentRW<MovementModeIntent>(member).Mode = mode;
        }
    }
}
```

---

## Task 4: Tests

### 4a. `SquadInputsP4Tests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP4Tests.cs`

Call `SquadInputs.RegisterAll()` in constructor.

Build ECS world with:
- Commander: `UnitRoster`, `Blackboard1024`
- Members: `UnitSubordinate` (Commander field set), `BehaviorState`
- Link members via `UnitRoster.Add` and `UnitSubordinate.Commander`

**SC-P4-01-1:** Member 0 has `state.Roles[0].RoleId == 2` (Suppressor).
- `AssignedRole(Params.BlueprintId = 2)` → 1f.
- `AssignedRole(Params.BlueprintId = 3)` (Flanker) → 0f.

**SC-P4-01-2:** Member with no `UnitSubordinate` (not in squad) → `AssignedRole` returns 0f.

**SC-P4-01-3:** Member 0's element = 1. `state.Slots[0] = {ElementIndex=1, SlotKind=2}`.
- `AssignedSlot(Params.BlueprintId = 2)` → 1f.
- `AssignedSlot(Params.BlueprintId = 3)` → 0f.

---

### 4b. `SquadVetoDetectionSystemTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadVetoDetectionSystemTests.cs`

**SC-P4-02-1:** Member assigned Role 1 (Engage), expected hash = 42. Member has `BehaviorState.ActiveBehaviorHash = 99` (Flee) for 3+ ticks. Should trigger `VetoDetected` after tick 3.
- Tick 1: run → no veto (count=1 < 3).
- Tick 2: run → no veto (count=2 < 3).
- Tick 3: run → `VetoDetected` emitted with memberSlot=0.

**SC-P4-02-2:** Single-tick divergence does NOT trigger veto.
- Tick 1: diverge (count=1).
- Tick 2: align (hash matches expected) → count resets to 0.
- No veto ever emitted.

**SC-P4-02-3:** Member with no `BehaviorState` — skip without veto, counter resets.

---

### 4c. `SquadMovementModeBroadcastSystemTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadMovementModeBroadcastSystemTests.cs`

**SC-P4-04-1:** `state.Flags |= 0x0100u` (bits 8-9 = 1 = Covered). Run system.
- All 2 roster members with `MovementModeIntent` get `Mode = Covered`.

**SC-P4-04-2:** Clear bits 8-9 (`state.Flags &= ~0x0300u`). Run system.
- All members get `Mode = Default`.

**SC-P4-04-3:** Member without `MovementModeIntent` component is unaffected (no exception).

---

## Component registration in tests

All used components must be registered:
- `repo.RegisterComponent<BehaviorState>()`
- `repo.RegisterComponent<MovementModeIntent>()`
- `repo.RegisterComponent<UnitSubordinate>()`
- `repo.RegisterComponent<UnitRoster>()`
- `repo.RegisterComponent<Blackboard1024>()`

---

## InlineArray read pattern for Roles and Slots

For reading `RoleAssignmentArray` (InlineArray(16)):
```csharp
var roleSpan = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
        ref Unsafe.AsRef(in state.Roles)), 16);
```

For reading `SlotAssignmentArray` (InlineArray(12)):
```csharp
var slotSpan = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<SlotAssignmentArray, SlotState>(
        ref Unsafe.AsRef(in state.Slots)), 12);
```

For reading `MemberElementIndexArray` (InlineArray(16)):
```csharp
var elemSpan = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<MemberElementIndexArray, byte>(
        ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
```

---

## `UnitSubordinate.Commander` linkage in tests

The test must set `UnitSubordinate.Commander` to the commander entity AND add the member to the roster:
```csharp
var member = repo.CreateEntity();
repo.AddComponent(member, new UnitSubordinate { Commander = commander });
ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
UnitRoster.Add(ref roster, (long)member.PackedValue);
```

---

## Checklist

Before submitting report:

- [ ] Two new `SquadInputIds` constants with correct FNV-1a values.
- [ ] `AssignedRole` and `AssignedSlot` readers registered in `RegisterAll()`.
- [ ] `MovementMode` enum in `SquadCognitiveState.cs`.
- [ ] `MovementModeIntent` component with `GlobalComponentIds.MovementModeIntent = 259`.
- [ ] `SquadVetoDetectionSystem` hysteresis correct (counter resets on alignment).
- [ ] `SquadMovementModeBroadcastSystem` reads bits 8-9 correctly.
- [ ] All SC tests pass: SC-P4-01-1..3, SC-P4-02-1..3, SC-P4-04-1..3.
- [ ] `SquadCognitiveStateLayoutTests` still pass (no struct size changes).
- [ ] Build: 0 errors, 0 new warnings.
- [ ] Total new tests >= 9.

## File summary

| Action | File |
|---|---|
| MODIFY | `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` |
| MODIFY | `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` |
| MODIFY | `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Components/MovementModeIntentComponent.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadVetoDetectionSystem.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadMovementModeBroadcastSystem.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP4Tests.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadVetoDetectionSystemTests.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadMovementModeBroadcastSystemTests.cs` |
