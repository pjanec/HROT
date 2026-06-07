# BATCH-28 Instructions — Phase 5 Part 2: Bounding Overwatch Maneuver (P5-02)

**Covers:** TASK-SQD-P5-02  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.2

---

## Context

P5-01 (`DangerAreaCrossingManeuver`) is committed. Same folder convention applies:
`FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/`

## Design

Bounding overwatch: two elements leapfrog. One element moves while the other covers. On each `BoundComplete` event the elements swap roles. This continues until an `Abort` event terminates the maneuver.

```
Phases:
  0  Element0Moving   - Element 0 moves toward next bound position; Element 1 covers.
                        BoundComplete → Element1Moving
                        Abort         → Aborted
  1  Element1Moving   - Element 1 moves; Element 0 covers.
                        BoundComplete → Element0Moving  (loops back to phase 0)
                        Abort         → Aborted
  2  Aborted          - Terminal phase.
```

```
Roles:
  RoleId 0 = Unassigned
  RoleId 1 = Moving   (executes bound)
  RoleId 2 = Covering (fires/overwatches)

Elements:
  Element 0 = first half of squad
  Element 1 = second half of squad
```

---

## Task 1: `BoundingOverwatchManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/BoundingOverwatchManeuver.cs`

Namespace: `Fdp.Toolkit.Squad.Maneuvers`

```csharp
/// <summary>
/// Configuration and role-swap logic for the 3-phase bounding-overwatch maneuver.
///
/// Phases:
///   0 Element0Moving  - Element 0 bounds; Element 1 covers (BoundComplete)
///   1 Element1Moving  - Element 1 bounds; Element 0 covers (BoundComplete)
///   2 Aborted         - Terminal phase (Abort event)
///
/// Roles:
///   RoleId 0 = Unassigned
///   RoleId 1 = Moving   (executing the bound)
///   RoleId 2 = Covering (fire/overwatch)
///
/// Elements:
///   Element 0 = first-half members
///   Element 1 = second-half members
/// </summary>
public static class BoundingOverwatchManeuver
{
    // Maneuver kind ID.
    public const ushort ManeuverKind = 2;

    // Phase IDs.
    public const ushort PhaseElement0Moving = 0;
    public const ushort PhaseElement1Moving = 1;
    public const ushort PhaseAborted        = 2;

    // Role IDs.
    public const byte RoleUnassigned = 0;
    public const byte RoleMoving     = 1;
    public const byte RoleCovering   = 2;

    // Element indices.
    public const byte ElementAlpha = 0;
    public const byte ElementBravo = 1;

    // --- Transition table ---

    /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
    public static PhaseTransitionEntry[] BuildTransitionTable() =>
        new PhaseTransitionEntry[]
        {
            new PhaseTransitionEntry { FromPhaseId = PhaseElement0Moving, EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhaseElement1Moving },
            new PhaseTransitionEntry { FromPhaseId = PhaseElement0Moving, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted        },
            new PhaseTransitionEntry { FromPhaseId = PhaseElement1Moving, EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhaseElement0Moving },
            new PhaseTransitionEntry { FromPhaseId = PhaseElement1Moving, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted        },
        };

    // --- Element partition ---

    /// <summary>
    /// Computes element partition inputs: first half → Element 0 (Alpha),
    /// second half → Element 1 (Bravo). Same heuristic as DangerAreaCrossingManeuver.
    /// </summary>
    public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
    {
        int half = Math.Max(1, memberCount / 2);
        for (int i = 0; i < memberCount; i++)
        {
            float alphaScore = i < half ? 1.0f : 0.1f;
            float bravoScore = i < half ? 0.1f : 1.0f;
            inputs[i] = new MemberPartitionInput(alphaScore, bravoScore);
        }
    }

    // --- Role assignment ---

    /// <summary>
    /// 4 candidates: 2 Moving slots + 2 Covering slots.
    /// Using 4 candidates ensures all 4 members get a role with maxFocusFire=1.
    /// </summary>
    public static readonly RoleSlotCandidate[] StandardCandidates =
        new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RoleMoving   },
            new RoleSlotCandidate { RoleId = RoleMoving   },
            new RoleSlotCandidate { RoleId = RoleCovering },
            new RoleSlotCandidate { RoleId = RoleCovering },
        };

    /// <summary>
    /// Builds a 4-column score matrix.
    /// Members of the moving element score high for the Moving candidates;
    /// members of the covering element score high for the Covering candidates.
    /// </summary>
    /// <param name="movingElement">
    /// Element index whose members should be assigned Moving (0 or 1).
    /// </param>
    public static void BuildRoleScoreMatrix(
        ref SquadCognitiveState state,
        int memberCount,
        byte movingElement,
        Span<float> scoreMatrix)
    {
        var membersSpan = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(
                ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

        // Columns: 0=Moving0, 1=Moving1, 2=Covering0, 3=Covering1
        for (int m = 0; m < memberCount; m++)
        {
            bool isMoving = membersSpan[m] == movingElement;
            scoreMatrix[m * 4 + 0] = isMoving ? 1.0f : 0.1f;  // Moving slot 0
            scoreMatrix[m * 4 + 1] = isMoving ? 1.0f : 0.1f;  // Moving slot 1
            scoreMatrix[m * 4 + 2] = isMoving ? 0.1f : 1.0f;  // Covering slot 0
            scoreMatrix[m * 4 + 3] = isMoving ? 0.1f : 1.0f;  // Covering slot 1
        }
    }

    /// <summary>
    /// Returns the moving element index for a given phase.
    /// Phase 0 → Element 0 moving; Phase 1 → Element 1 moving.
    /// </summary>
    public static byte GetMovingElement(ushort phaseId)
        => phaseId == PhaseElement1Moving ? ElementBravo : ElementAlpha;
}
```

**Usings needed:**
```csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;
```

Also import `Fdp.Toolkit.Squad.State` namespace if `MemberElementIndexArray` lives there, or
`Fdp.Toolkit.Squad` if it's in the same namespace as `SquadCognitiveState`. Check before coding.

---

## Task 2: Tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BoundingOverwatchManeuverTests.cs`

Reuse the same helper pattern from `DangerAreaCrossingManeuverTests.cs`.

### SC-P5-02-1: At least 2 bound swaps (phase alternation)

Drive 4 BoundComplete events and verify the phase alternates correctly, ending back at Phase 0.

```csharp
[Fact]
public void BoundingOverwatch_PhaseAlternates_OnBoundComplete()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = BoundingOverwatchManeuver.PhaseElement0Moving;
    state.PhaseEnteredTick = 0;
    var table = BoundingOverwatchManeuver.BuildTransitionTable();
    var swapCount = 0;

    // 4 bounds: 0→1, 1→0, 0→1, 1→0
    var expected = new ushort[]
    {
        BoundingOverwatchManeuver.PhaseElement1Moving,
        BoundingOverwatchManeuver.PhaseElement0Moving,
        BoundingOverwatchManeuver.PhaseElement1Moving,
        BoundingOverwatchManeuver.PhaseElement0Moving,
    };

    for (int t = 1; t <= 4; t++)
    {
        bool transitioned = PhaseSequencer.Advance(ref state,
            new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.BoundComplete) }),
            table, currentTick: (uint)t, dwellTimeoutTicks: 100, recoveryPhaseId: 2);
        Assert.True(transitioned, $"Expected transition at tick {t}");
        Assert.Equal(expected[t - 1], state.PhaseId);
        swapCount++;
    }

    Assert.True(swapCount >= 2, "Expected at least 2 bound swaps");
}
```

### SC-P5-02-2: Abort transitions to terminal phase

```csharp
[Fact]
public void BoundingOverwatch_AbortEvent_TransitionsToAborted()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = BoundingOverwatchManeuver.PhaseElement0Moving;
    state.PhaseEnteredTick = 0;
    var table = BoundingOverwatchManeuver.BuildTransitionTable();

    bool transitioned = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.Abort) }),
        table, currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

    Assert.True(transitioned);
    Assert.Equal(BoundingOverwatchManeuver.PhaseAborted, state.PhaseId);
}
```

### SC-P5-02-3: Role assignment — never >2 members in Moving role simultaneously

For a 4-member squad, after each bound assignment, at most 2 members should have RoleMoving.

```csharp
[Fact]
public void RoleAssignment_AtMost2Members_HaveMovingRole()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    // Partition the squad.
    Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
    BoundingOverwatchManeuver.ComputePartitionInputs(4, inputs);
    ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

    // Assign roles with Element 0 moving.
    Span<float> scoreMatrix = stackalloc float[4 * 4];
    BoundingOverwatchManeuver.BuildRoleScoreMatrix(ref state, 4,
        BoundingOverwatchManeuver.ElementAlpha, scoreMatrix);
    RoleSlotAssignmentPrimitive.AssignRoles(ref state,
        BoundingOverwatchManeuver.StandardCandidates, scoreMatrix, 4);

    // Count Moving roles.
    var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);
    int movingCount = 0;
    for (int i = 0; i < 4; i++)
        if (rolesSpan[i].RoleId == BoundingOverwatchManeuver.RoleMoving)
            movingCount++;

    Assert.True(movingCount <= 2, $"Expected ≤ 2 moving, got {movingCount}");
    Assert.True(movingCount >= 1, "Expected at least 1 moving member");
}
```

### SC-P5-02-4: Role swap on phase transition (Element 0 cover after swap)

```csharp
[Fact]
public void RoleAssignment_AfterSwap_Element0MembersGetCoveringRole()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
    BoundingOverwatchManeuver.ComputePartitionInputs(4, inputs);
    ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

    // After Phase 0→1 swap: Element 1 moves (Bravo).
    Span<float> scoreMatrix = stackalloc float[4 * 4];
    BoundingOverwatchManeuver.BuildRoleScoreMatrix(ref state, 4,
        BoundingOverwatchManeuver.ElementBravo, scoreMatrix);
    RoleSlotAssignmentPrimitive.AssignRoles(ref state,
        BoundingOverwatchManeuver.StandardCandidates, scoreMatrix, 4);

    var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

    // Element 0 members (0,1) should be Covering; Element 1 members (2,3) should be Moving.
    Assert.Equal(BoundingOverwatchManeuver.RoleCovering, rolesSpan[0].RoleId);
    Assert.Equal(BoundingOverwatchManeuver.RoleCovering, rolesSpan[1].RoleId);
    Assert.Equal(BoundingOverwatchManeuver.RoleMoving,   rolesSpan[2].RoleId);
    Assert.Equal(BoundingOverwatchManeuver.RoleMoving,   rolesSpan[3].RoleId);
}
```

### SC-P5-02-5: GetMovingElement returns correct element per phase

```csharp
[Fact]
public void GetMovingElement_ReturnsCorrectElement_ForEachPhase()
{
    Assert.Equal(BoundingOverwatchManeuver.ElementAlpha,
        BoundingOverwatchManeuver.GetMovingElement(BoundingOverwatchManeuver.PhaseElement0Moving));
    Assert.Equal(BoundingOverwatchManeuver.ElementBravo,
        BoundingOverwatchManeuver.GetMovingElement(BoundingOverwatchManeuver.PhaseElement1Moving));
}
```

### BuildFixture helper (reuse from DangerAreaCrossing pattern)

Same as previous test — check the existing test file to copy the exact helper.

---

## Key lookups before coding

1. Read `DangerAreaCrossingManeuver.cs` to match code style.
2. Read `DangerAreaCrossingManeuverTests.cs` for the exact `BuildFixture` pattern to reuse.
3. Verify `MemberElementIndexArray` and `RoleAssignmentArray` import paths.

---

## Checklist

- [ ] `BoundingOverwatchManeuver.cs` compiles with 0 errors.
- [ ] 5 tests pass (SC-P5-02-1 through SC-P5-02-5).
- [ ] All 92 pre-existing squad tests still pass.
- [ ] Total squad tests: 97 minimum (92 + 5).
- [ ] No new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/BoundingOverwatchManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/BoundingOverwatchManeuverTests.cs` |
