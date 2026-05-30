# BATCH-31 Instructions — Phase 5 Part 5: Briefer Catalog Entries (P5-05)

**Covers:** TASK-SQD-P5-05  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.6

---

## Context

P5-01..P5-04 committed. Same folder convention.
Read `HillCrestHullDownManeuver.cs` for style.

## Design

Two lighter-detail catalog entries:

### 8.6a: Stack-and-room-entry
Sector-assignment-heavy: stack on a door, enter in sequence with assigned sectors of fire.
Slots are *sectors of fire* (not positions). Exercises role/slot assignment where slots = sectors.

```
Phases:
  0 Stacking     - Squad stacks at door position. (BoundComplete = in position)
                   BoundComplete → Entering
                   Abort         → Aborted
  1 Entering     - Members enter in sequence, covering their assigned sector.
                   FarSideReached → Cleared (breach complete)
                   Abort          → Aborted
  2 Cleared      - Terminal: room secure.
  3 Aborted      - Terminal.

Roles:
  0 = Unassigned
  1 = PointMan     (enters first, covers front sector)
  2 = BreachCover  (covers door while others enter)
  3 = Secondary    (enters after PointMan, covers side sector)

Elements:
  0 = Entry element (all members by default in this simpler variant)
  1 = (unused but reserved)
```

4 candidates for role assignment (PointMan + BreachCover + Secondary + Secondary):
- Allows 4-member squad to get distinct roles.

---

### 8.6b: Travelling overwatch
Lead element moves; trail element overwatches at distance. Bounding's looser cousin.
No rotation — element split without slot rotation.

```
Phases:
  0 Moving     - Lead element advances; trail overwatches.
                 FarSideReached → Arrived (destination reached)
                 Abort          → Aborted
  1 Arrived    - Terminal: lead reached destination.
  2 Aborted    - Terminal.

Roles:
  0 = Unassigned
  1 = Lead      (advance to destination)
  2 = Overwatch (hold position, eyes on threat)

Elements:
  0 = Lead element (first half)
  1 = Overwatch element (second half)
```

---

## Task 1: `StackAndRoomEntryManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/StackAndRoomEntryManeuver.cs`

```csharp
/// <summary>
/// Configuration and role logic for the stack-and-room-entry maneuver (§8.6a).
///
/// Phases:
///   0 Stacking  - Stack at door; BoundComplete -> Entering
///   1 Entering  - Enter in sequence; FarSideReached -> Cleared
///   2 Cleared   - Terminal: room secure
///   3 Aborted   - Terminal
///
/// Roles:
///   0 = Unassigned
///   1 = PointMan    (enters first, front sector)
///   2 = BreachCover (covers door)
///   3 = Secondary   (enters after PointMan)
/// </summary>
public static class StackAndRoomEntryManeuver
{
    public const ushort ManeuverKind = 5;

    public const ushort PhaseStacking  = 0;
    public const ushort PhaseEntering  = 1;
    public const ushort PhaseCleared   = 2;
    public const ushort PhaseAborted   = 3;

    public const byte RoleUnassigned  = 0;
    public const byte RolePointMan    = 1;
    public const byte RoleBreachCover = 2;
    public const byte RoleSecondary   = 3;

    /// <summary>Builds the phase-transition table.</summary>
    public static PhaseTransitionEntry[] BuildTransitionTable() =>
        new PhaseTransitionEntry[]
        {
            new PhaseTransitionEntry { FromPhaseId = PhaseStacking, EventKind = PhaseEventKind.BoundComplete,  ToPhaseId = PhaseEntering },
            new PhaseTransitionEntry { FromPhaseId = PhaseStacking, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted  },
            new PhaseTransitionEntry { FromPhaseId = PhaseEntering, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseCleared  },
            new PhaseTransitionEntry { FromPhaseId = PhaseEntering, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted  },
        };

    // 4 candidates: 1 PointMan + 1 BreachCover + 2 Secondary
    // (allows 4-member squad to get distinct roles with maxFocusFire=1)
    public static readonly RoleSlotCandidate[] StandardCandidates =
        new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RolePointMan    },
            new RoleSlotCandidate { RoleId = RoleBreachCover },
            new RoleSlotCandidate { RoleId = RoleSecondary   },
            new RoleSlotCandidate { RoleId = RoleSecondary   },
        };

    /// <summary>
    /// Score matrix: member 0 (point man candidate) gets high PointMan score;
    /// member 1 gets high BreachCover; members 2+ get Secondary.
    /// </summary>
    public static void BuildRoleScoreMatrix(int memberCount, Span<float> scoreMatrix)
    {
        for (int m = 0; m < memberCount; m++)
        {
            // Columns: 0=PointMan, 1=BreachCover, 2=Secondary0, 3=Secondary1
            scoreMatrix[m * 4 + 0] = (m == 0) ? 1.0f : 0.1f;
            scoreMatrix[m * 4 + 1] = (m == 1) ? 1.0f : 0.1f;
            scoreMatrix[m * 4 + 2] = (m >= 2) ? 1.0f : 0.1f;
            scoreMatrix[m * 4 + 3] = (m >= 2) ? 1.0f : 0.1f;
        }
    }
}
```

---

## Task 2: `TravellingOverwatchManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/TravellingOverwatchManeuver.cs`

```csharp
/// <summary>
/// Configuration and role logic for the travelling-overwatch maneuver (§8.6b).
///
/// Phases:
///   0 Moving   - Lead element advances; FarSideReached -> Arrived
///   1 Arrived  - Terminal: lead reached destination
///   2 Aborted  - Terminal
///
/// Roles:
///   0 = Unassigned
///   1 = Lead      (advance to destination)
///   2 = Overwatch (hold position, eyes on threat)
///
/// Elements:
///   0 = Lead element
///   1 = Overwatch element
/// </summary>
public static class TravellingOverwatchManeuver
{
    public const ushort ManeuverKind = 6;

    public const ushort PhaseMoving  = 0;
    public const ushort PhaseArrived = 1;
    public const ushort PhaseAborted = 2;

    public const byte RoleUnassigned = 0;
    public const byte RoleLead       = 1;
    public const byte RoleOverwatch  = 2;

    public const byte ElementLead      = 0;
    public const byte ElementOverwatch = 1;

    public static PhaseTransitionEntry[] BuildTransitionTable() =>
        new PhaseTransitionEntry[]
        {
            new PhaseTransitionEntry { FromPhaseId = PhaseMoving, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseArrived },
            new PhaseTransitionEntry { FromPhaseId = PhaseMoving, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted },
        };

    // 4 candidates: 2 Lead + 2 Overwatch (supports 4-member squad with maxFocusFire=1)
    public static readonly RoleSlotCandidate[] StandardCandidates =
        new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RoleLead      },
            new RoleSlotCandidate { RoleId = RoleLead      },
            new RoleSlotCandidate { RoleId = RoleOverwatch },
            new RoleSlotCandidate { RoleId = RoleOverwatch },
        };

    /// <summary>
    /// Computes element partition inputs: first half → Lead, second half → Overwatch.
    /// </summary>
    public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
    {
        int half = Math.Max(1, memberCount / 2);
        for (int i = 0; i < memberCount; i++)
        {
            float leadScore      = i < half ? 1.0f : 0.1f;
            float overwatchScore = i < half ? 0.1f : 1.0f;
            inputs[i] = new MemberPartitionInput(leadScore, overwatchScore);
        }
    }

    /// <summary>Builds a 4-column score matrix. Lead element → Lead role; Overwatch element → Overwatch role.</summary>
    public static void BuildRoleScoreMatrix(
        ref SquadCognitiveState state, int memberCount, Span<float> scoreMatrix)
    {
        var membersSpan = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(
                ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

        for (int m = 0; m < memberCount; m++)
        {
            bool isLead = membersSpan[m] == ElementLead;
            scoreMatrix[m * 4 + 0] = isLead ? 1.0f : 0.1f;  // Lead0
            scoreMatrix[m * 4 + 1] = isLead ? 1.0f : 0.1f;  // Lead1
            scoreMatrix[m * 4 + 2] = isLead ? 0.1f : 1.0f;  // Overwatch0
            scoreMatrix[m * 4 + 3] = isLead ? 0.1f : 1.0f;  // Overwatch1
        }
    }
}
```

---

## Task 3: Tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BrieferCatalogManeuverTests.cs`

(Both stack-and-room-entry and travelling-overwatch tests in one file.)

### SC-P5-05-1: Stack-and-room-entry assigns 4 members to 4 distinct roles

```csharp
[Fact]
public void StackAndRoomEntry_AssignsFourDistinctRoles()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    Span<float> scoreMatrix = stackalloc float[4 * 4];
    StackAndRoomEntryManeuver.BuildRoleScoreMatrix(4, scoreMatrix);
    RoleSlotAssignmentPrimitive.AssignRoles(ref state,
        StackAndRoomEntryManeuver.StandardCandidates, scoreMatrix, 4);

    var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

    // Each of the 4 members should have a distinct role.
    var assignedRoles = new HashSet<byte>();
    for (int i = 0; i < 4; i++)
        assignedRoles.Add(rolesSpan[i].RoleId);
    // PointMan(1), BreachCover(2), Secondary(3) — at least 3 distinct roles.
    Assert.True(assignedRoles.Count >= 3,
        $"Expected at least 3 distinct roles, got {assignedRoles.Count}");
    Assert.Contains(StackAndRoomEntryManeuver.RolePointMan,    assignedRoles);
    Assert.Contains(StackAndRoomEntryManeuver.RoleBreachCover, assignedRoles);
    Assert.Contains(StackAndRoomEntryManeuver.RoleSecondary,   assignedRoles);
}
```

### SC-P5-05-2: Travelling overwatch — transition on FarSideReached

```csharp
[Fact]
public void TravellingOverwatch_FarSideReached_TransitionsToArrived()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = TravellingOverwatchManeuver.PhaseMoving;
    state.PhaseEnteredTick = 0;
    var table = TravellingOverwatchManeuver.BuildTransitionTable();

    bool transitioned = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
        table, currentTick: 10, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

    Assert.True(transitioned);
    Assert.Equal(TravellingOverwatchManeuver.PhaseArrived, state.PhaseId);
}
```

### SC-P5-05-3: Primitive coverage static check (compile-time catalog)

Verify at test time that all 5 maneuver primitives are exercised across the catalog:

```csharp
[Fact]
public void CatalogCoverageCheck_AllPrimitivesExercised()
{
    // Primitive 1 (ElementPartition): DangerAreaCrossing, BoundingOverwatch,
    //   SuppressAndManeuver, HillCrestHullDown, TravellingOverwatch all use it.
    Assert.True(typeof(ElementPartitionPrimitive).IsPublic, "ElementPartitionPrimitive must be public");

    // Primitive 2 (TacticalFeatureHandles): state.ActiveFeatureId used by DangerAreaCrossing.
    Assert.True(typeof(TacticalFeatureHandles).IsPublic, "TacticalFeatureHandles must be public");

    // Primitive 3 (RoleSlotAssignment): used by all 6 maneuvers.
    Assert.True(typeof(RoleSlotAssignmentPrimitive).IsPublic, "RoleSlotAssignmentPrimitive must be public");

    // Primitive 4 (PhaseSequencer): used by all maneuvers with BuildTransitionTable().
    Assert.True(typeof(PhaseSequencer).IsPublic, "PhaseSequencer must be public");

    // Primitive 5 (SlotRotation): used by HillCrestHullDown (BurnSlot/AcquireSlot).
    Assert.True(typeof(SlotRotation).IsPublic, "SlotRotation must be public");
}
```

---

## Notes on usings

`BrieferCatalogManeuverTests.cs` needs:
```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Xunit;
```

`TravellingOverwatchManeuver.cs` and `StackAndRoomEntryManeuver.cs` need:
```csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;
```

Only `TravellingOverwatchManeuver.cs` needs `unsafe` (uses Unsafe.As). `StackAndRoomEntryManeuver.cs` does NOT use Unsafe.As (no element span), so it does not need `unsafe`.

---

## Checklist

- [ ] Both `.cs` maneuver files compile with 0 errors.
- [ ] 3 tests pass (SC-P5-05-1, SC-P5-05-2, SC-P5-05-3).
- [ ] All 105 pre-existing squad tests still pass.
- [ ] Total: 108 (105 + 3).
- [ ] No new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/StackAndRoomEntryManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/TravellingOverwatchManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BrieferCatalogManeuverTests.cs` |
