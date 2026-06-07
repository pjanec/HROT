# BATCH-27 Instructions — Phase 5 Part 1: Danger-Area Crossing Maneuver (P5-01)

**Covers:** TASK-SQD-P5-01  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.1

---

## Context

All Phase 0-4 primitives are in place:
- `ElementPartitionPrimitive` — partitions members into N elements
- `RoleSlotAssignmentPrimitive` — assigns RoleId per member using greedy matrix
- `PhaseSequencer` — phase state machine with event-driven + dwell-timeout transitions
- `SlotRotation` — rotating crossing lanes, burn/reuse semantics
- `TacticalFeatureHandles` — near/far side handles from danger area
- `SquadEventIngressSystem` — detects ammo decrease and navigation arrivals
- `SquadCognitiveState` — blackboard projection (1024B)

The squad states that are relevant:
- `state.PhaseId` — current HSM phase (ushort)
- `state.PhaseEnteredTick` — tick when phase was entered (uint)
- `state.ManeuverKind` — which maneuver is active (ushort); DangerAreaCrossing = 1
- `state.ActiveFeatureId` — the danger area feature entity ID (uint)
- `state.Flags` — bit 0 = MissionOverride, bits 8-9 = MovementMode
- `state.Roles` — `RoleAssignmentArray [InlineArray(16)] of RoleSlot { byte RoleId }`
- `state.Elements` — `ElementPartitionRecord { MemberElementIndexArray MemberElements }`
- `state.Slots` — `SlotAssignmentArray [InlineArray(12)] of SlotState`

Look at PhaseSequencer.cs for `PhaseTransitionEntry` struct and PhaseEventKind enum. PhaseEventKind values:
`ShotFired=0, DefiladeReached=1, FarSideReached=2, BoundComplete=3, VetoDetected=4, Abort=5`

---

## Task 1: `DangerAreaCrossingManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/DangerAreaCrossingManeuver.cs`

Create the folder if it doesn't exist.

Namespace: `Fdp.Toolkit.Squad.Maneuvers`

```csharp
/// <summary>
/// Configuration and orchestration logic for the 5-phase danger-area crossing maneuver.
///
/// Phases:
///   0 SetSecurity     - security element occupies overwatch positions (DefiladeReached)
///   1 CrossElement    - first element crosses the danger area (FarSideReached)
///   2 FarSideCover    - first-across reassigned to covering role; signals ready (ShotFired)
///   3 CollapseSecurity- second element crosses, security follows (FarSideReached)
///   4 Reform          - terminal phase; all members on far side
///
/// Roles:
///   RoleId 0 = Unassigned
///   RoleId 1 = Crossing  (assigned to crossing element)
///   RoleId 2 = Security  (assigned to overwatch element)
///
/// Elements:
///   Element 0 = Crossing element
///   Element 1 = Security element
/// </summary>
public static class DangerAreaCrossingManeuver
{
    // Maneuver kind ID stored in state.ManeuverKind.
    public const ushort ManeuverKind = 1;

    // Phase IDs.
    public const ushort PhaseSetSecurity    = 0;
    public const ushort PhaseCrossElement   = 1;
    public const ushort PhaseFarSideCover   = 2;
    public const ushort PhaseCollapseSecurity = 3;
    public const ushort PhaseReform         = 4;

    // Role IDs.
    public const byte RoleUnassigned = 0;
    public const byte RoleCrossing   = 1;
    public const byte RoleSecurity   = 2;

    // Element indices.
    public const byte ElementCrossing = 0;
    public const byte ElementSecurity = 1;

    // --- Transition table ---

    /// <summary>
    /// Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.
    /// </summary>
    public static PhaseTransitionEntry[] BuildTransitionTable() =>
    [
        new PhaseTransitionEntry { FromPhaseId = PhaseSetSecurity,      EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseCrossElement   },
        new PhaseTransitionEntry { FromPhaseId = PhaseCrossElement,     EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseFarSideCover    },
        new PhaseTransitionEntry { FromPhaseId = PhaseFarSideCover,     EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseCollapseSecurity},
        new PhaseTransitionEntry { FromPhaseId = PhaseCollapseSecurity, EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseReform          },
    ];

    // --- Element partition ---

    /// <summary>
    /// Computes element partition inputs for the danger-area crossing scenario.
    ///
    /// Element 0 (Crossing): members in front half of the squad (lower member indices).
    /// Element 1 (Security): members in back half.
    ///
    /// This is a simple index-based heuristic for the starter pack; game-specific
    /// maneuvers should supply proper scoring from positional EQS data.
    /// </summary>
    public static void ComputePartitionInputs(
        int memberCount,
        Span<MemberPartitionInput> inputs)
    {
        int half = Math.Max(1, memberCount / 2);
        for (int i = 0; i < memberCount; i++)
        {
            // First half cross, second half provide security.
            float crossingScore  = i < half ? 1.0f : 0.1f;
            float securityScore  = i < half ? 0.1f : 1.0f;
            inputs[i] = new MemberPartitionInput(crossingScore, securityScore);
        }
    }

    // --- Role assignment ---

    /// <summary>
    /// Role candidates for <see cref="RoleSlotAssignmentPrimitive.AssignRoles"/>.
    /// Element 0 -> Crossing (RoleId 1); Element 1 -> Security (RoleId 2).
    /// </summary>
    public static readonly RoleSlotCandidate[] StandardCandidates =
    [
        new RoleSlotCandidate { RoleId = RoleCrossing },
        new RoleSlotCandidate { RoleId = RoleSecurity },
    ];

    /// <summary>
    /// Builds a score matrix (memberCount x 2) where crossing-element members score
    /// high for Crossing and security-element members score high for Security.
    /// </summary>
    public static void BuildRoleScoreMatrix(
        ref SquadCognitiveState state,
        int memberCount,
        Span<float> scoreMatrix)
    {
        var membersSpan = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(
                ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

        for (int m = 0; m < memberCount; m++)
        {
            byte elem = membersSpan[m];
            // Column 0 = Crossing, Column 1 = Security.
            scoreMatrix[m * 2 + 0] = (elem == ElementCrossing) ? 1.0f : 0.1f;
            scoreMatrix[m * 2 + 1] = (elem == ElementSecurity) ? 1.0f : 0.1f;
        }
    }

    /// <summary>
    /// Reassigns the first-across member (slot 0 winner in crossing element)
    /// to the Covering role on entering <see cref="PhaseFarSideCover"/>.
    /// Re-runs role assignment with a flipped matrix that gives the slot-0
    /// crossing member a high Security score.
    /// </summary>
    /// <param name="state">State to mutate.</param>
    /// <param name="memberCount">Roster member count.</param>
    /// <param name="firstAcrossSlot">
    ///   Roster index of the first member who crossed (emitting FarSideReached).
    ///   If -1, method is a no-op.
    /// </param>
    public static unsafe void ReassignFirstAcrossToCovering(
        ref SquadCognitiveState state,
        int memberCount,
        int firstAcrossSlot)
    {
        if (firstAcrossSlot < 0 || firstAcrossSlot >= memberCount) return;

        // Force member at firstAcrossSlot to Security role directly.
        var rolesSpan = MemoryMarshal.CreateSpan(
            ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);
        rolesSpan[firstAcrossSlot].RoleId = RoleSecurity;
    }
}
```

**Usings needed:**
```
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad.Primitives;
```

Check the exact `PhaseTransitionEntry` struct — it should have `FromPhaseId`, `ToPhaseId`, `EventKind` fields. Check in `PhaseSequencer.cs`.

If `PhaseTransitionEntry` uses C# 12 collection expression `[]` syntax, confirm it's available.
Otherwise use `new PhaseTransitionEntry[] { ... }`.

---

## Task 2: Integration test

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/DangerAreaCrossingManeuverTests.cs`

The test uses a fabricated fixture (no real navigation/physics). It manually drives state changes to simulate each phase transition.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Maneuvers
{
    public class DangerAreaCrossingManeuverTests
    {
        // ...
    }
}
```

### Setup

Create 4 members, each with:
- `Blackboard1024` (for squad state)
- `NavigationStatus`
- `Position` (from `Fdp.Modules.Geographic.Components`)
- `UnitSubordinate`

Commander with:
- `UnitRoster` (4 members added)
- `Blackboard1024`

Register all those component types.

Check if `Position` has a component ID in `GlobalComponentIds.cs`. It should be `GlobalComponentIds.GeoPosition`. If not, use a local component ID (define a local struct for the test).

Actually — the test probably shouldn't depend on `Position` from Geographic. The assertion "all members reach far side" can be done differently: just check that after the final phase all members have their roles set to Security (they crossed and were reassigned). 

Simplify the integration test assertions to avoid dependency on `Position` component:
1. All 5 phases were entered (track via a list of phase IDs when Advance returned true)
2. Roles after Phase 2: at least one member has Security role (was first-across reassigned)
3. Slot rotation used 2 different lanes (SlotRotation output tracked)
4. After Phase 4 (Reform): state.PhaseId == 4

### Test SC-P5-01-1: All phases enter in sequence

```csharp
[Fact]
public void ManeuverRunsAllFivePhases_InOrder()
{
    // Arrange: 4-member squad in SetSecurity phase.
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));
    state.PhaseId = DangerAreaCrossingManeuver.PhaseSetSecurity;
    state.PhaseEnteredTick = 0;

    var table = DangerAreaCrossingManeuver.BuildTransitionTable();
    var phasesEntered = new List<ushort> { DangerAreaCrossingManeuver.PhaseSetSecurity };

    // Phase 0 → 1: DefiladeReached
    bool t01 = PhaseSequencer.Advance(ref state,
        [new PhaseEvent(PhaseEventKind.DefiladeReached)],
        table, currentTick: 1, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
    Assert.True(t01);
    Assert.Equal(DangerAreaCrossingManeuver.PhaseCrossElement, state.PhaseId);
    phasesEntered.Add(state.PhaseId);

    // Phase 1 → 2: FarSideReached
    bool t12 = PhaseSequencer.Advance(ref state,
        [new PhaseEvent(PhaseEventKind.FarSideReached)],
        table, currentTick: 2, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
    Assert.True(t12);
    Assert.Equal(DangerAreaCrossingManeuver.PhaseFarSideCover, state.PhaseId);
    phasesEntered.Add(state.PhaseId);

    // Phase 2 → 3: ShotFired
    bool t23 = PhaseSequencer.Advance(ref state,
        [new PhaseEvent(PhaseEventKind.ShotFired)],
        table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
    Assert.True(t23);
    Assert.Equal(DangerAreaCrossingManeuver.PhaseCollapseSecurity, state.PhaseId);
    phasesEntered.Add(state.PhaseId);

    // Phase 3 → 4: FarSideReached
    bool t34 = PhaseSequencer.Advance(ref state,
        [new PhaseEvent(PhaseEventKind.FarSideReached)],
        table, currentTick: 4, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
    Assert.True(t34);
    Assert.Equal(DangerAreaCrossingManeuver.PhaseReform, state.PhaseId);
    phasesEntered.Add(state.PhaseId);

    // Assert: all 5 phases entered in order.
    Assert.Equal(new ushort[] { 0, 1, 2, 3, 4 }, phasesEntered);
}
```

### Test SC-P5-01-2: Element partition splits squad correctly

```csharp
[Fact]
public void ElementPartition_SplitsSquad_IntoTwoElements()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    // Compute partition inputs.
    Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
    DangerAreaCrossingManeuver.ComputePartitionInputs(4, inputs);

    // Run element partition.
    ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 2,
                                        decisiveGap: 0f, out int repartitions);

    // Read element assignments.
    var elemSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<MemberElementIndexArray, byte>(
            ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

    // First 2 members = Element 0 (Crossing), last 2 = Element 1 (Security).
    Assert.Equal(DangerAreaCrossingManeuver.ElementCrossing, elemSpan[0]);
    Assert.Equal(DangerAreaCrossingManeuver.ElementCrossing, elemSpan[1]);
    Assert.Equal(DangerAreaCrossingManeuver.ElementSecurity, elemSpan[2]);
    Assert.Equal(DangerAreaCrossingManeuver.ElementSecurity, elemSpan[3]);
    Assert.True(repartitions > 0);
}
```

### Test SC-P5-01-3: Role assignment from element partition

```csharp
[Fact]
public void RoleAssignment_AssignsCrossingAndSecurityRoles()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    // Partition first.
    Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
    DangerAreaCrossingManeuver.ComputePartitionInputs(4, inputs);
    ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

    // Build score matrix and assign roles.
    Span<float> scoreMatrix = stackalloc float[4 * 2];
    DangerAreaCrossingManeuver.BuildRoleScoreMatrix(ref state, 4, scoreMatrix);
    RoleSlotAssignmentPrimitive.AssignRoles(ref state,
        DangerAreaCrossingManeuver.StandardCandidates, scoreMatrix, 4);

    // Read roles.
    var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

    // Members 0,1 -> Crossing; members 2,3 -> Security.
    Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing,  rolesSpan[0].RoleId);
    Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing,  rolesSpan[1].RoleId);
    Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity,  rolesSpan[2].RoleId);
    Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity,  rolesSpan[3].RoleId);
}
```

### Test SC-P5-01-4: First-across reassignment on Phase 2 entry

```csharp
[Fact]
public void ReassignFirstAcrossToCovering_ChangesRoleToSecurity()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    // Set member 0 to Crossing role initially.
    var rolesSpan = MemoryMarshal.CreateSpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);
    rolesSpan[0].RoleId = DangerAreaCrossingManeuver.RoleCrossing;
    rolesSpan[1].RoleId = DangerAreaCrossingManeuver.RoleCrossing;

    // Reassign member 0 (first-across) to Security.
    DangerAreaCrossingManeuver.ReassignFirstAcrossToCovering(ref state, 4, firstAcrossSlot: 0);

    Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity, rolesSpan[0].RoleId);
    Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing, rolesSpan[1].RoleId);  // unchanged
}
```

### Test SC-P5-01-5: Slot rotation tracks crossing lanes

```csharp
[Fact]
public void SlotRotation_TwoCrossers_UseDifferentLanes()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    // Use SlotRotation to allocate 2 crossing lanes for 2 members.
    int lane0 = SlotRotation.AllocateNextSlot(ref state, candidateCount: 2);
    int lane1 = SlotRotation.AllocateNextSlot(ref state, candidateCount: 2);

    Assert.NotEqual(lane0, lane1);
    Assert.True(lane0 >= 0 && lane0 < 2);
    Assert.True(lane1 >= 0 && lane1 < 2);
}
```

Note: Check the actual `SlotRotation` API by reading `SlotRotation.cs`. The method name/signature may differ. Adjust the test accordingly.

### Test SC-P5-01-6: No phase transition before event (dwell guard)

```csharp
[Fact]
public void PhaseSequencer_NoTransition_WhenNoEventAndDwellNotElapsed()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = DangerAreaCrossingManeuver.PhaseSetSecurity;
    state.PhaseEnteredTick = 0;
    var table = DangerAreaCrossingManeuver.BuildTransitionTable();

    bool t = PhaseSequencer.Advance(ref state,
        ReadOnlySpan<PhaseEvent>.Empty, table,
        currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 0);

    Assert.False(t);
    Assert.Equal(DangerAreaCrossingManeuver.PhaseSetSecurity, state.PhaseId);
}
```

### BuildFixture helper

```csharp
private static (EntityRepository repo, Entity commander, Entity[] members)
    BuildFixture(int memberCount)
{
    var repo = new EntityRepository();
    repo.RegisterComponent<UnitRoster>();
    repo.RegisterComponent<Blackboard1024>();
    repo.RegisterComponent<NavigationStatus>();
    repo.RegisterComponent<UnitSubordinate>();

    var commander = repo.CreateEntity();
    repo.AddComponent(commander, new UnitRoster());
    repo.AddComponent(commander, new Blackboard1024());

    var members = new Entity[memberCount];
    for (int i = 0; i < memberCount; i++)
    {
        members[i] = repo.CreateEntity();
        repo.AddComponent(members[i], new NavigationStatus());
        repo.AddComponent(members[i], new UnitSubordinate { Commander = commander });
        ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
        UnitRoster.Add(ref roster, (long)members[i].PackedValue);
    }
    return (repo, commander, members);
}
```

---

## Key lookups before coding

1. **`SlotRotation.cs`** — read the actual API (`AllocateNextSlot`, `AllocateSlot`, etc.) and adapt the test.
2. **`PhaseTransitionEntry`** — check if it's a `struct` with public fields or different names.
3. **`PhaseSequencer.cs`** — check if `PhaseEvent` constructor takes `PhaseEventKind`.
4. **`UnitRoster.Add`** — check how new members are added to the roster in tests.
5. **`MemberElementIndexArray`** — confirm it's an `[InlineArray(16)]` of `byte`.
6. **`RoleAssignmentArray`** — confirm it's an `[InlineArray(16)]` of `RoleSlot { byte RoleId }`.

---

## Checklist

- [ ] `DangerAreaCrossingManeuver.cs` compiles with 0 errors.
- [ ] `BuildTransitionTable()` returns correct 4-entry table.
- [ ] 6 tests pass (SC-P5-01-1 through SC-P5-01-6).
- [ ] All 86 pre-existing squad tests still pass.
- [ ] Total squad tests: 86 + 6 = 92 minimum.
- [ ] No new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/DangerAreaCrossingManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/DangerAreaCrossingManeuverTests.cs` |
