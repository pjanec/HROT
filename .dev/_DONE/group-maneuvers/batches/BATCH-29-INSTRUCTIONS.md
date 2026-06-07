# BATCH-29 Instructions — Phase 5 Part 3: Suppress-and-Maneuver (P5-03)

**Covers:** TASK-SQD-P5-03  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.3

---

## Context

P5-01 and P5-02 are committed. Same folder convention:
`FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/`

Read `DangerAreaCrossingManeuver.cs` and `BoundingOverwatchManeuver.cs` before coding —
match their style exactly.

---

## Design

Suppress-and-maneuver: base-of-fire element suppresses while assault element flanks.

```
Phases:
  0  Suppressing   - BaseOfFire element fires; Assault element advances to flank position.
                     FarSideReached → AssaultComplete (assault reached flank)
                     Abort          → Aborted
  1  AssaultComplete - Assault reached flank; base consolidates. Terminal phase.
  2  Aborted        - Terminal phase (Abort event).
```

```
Roles:
  RoleId 0 = Unassigned
  RoleId 1 = BaseOfFire  (hold position, suppress target)
  RoleId 2 = Assault     (advance along Muscle-pathed flank)

Elements:
  Element 0 = BaseOfFire element
  Element 1 = Assault element
```

---

## Task 1: `SuppressAndManeuverManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/SuppressAndManeuverManeuver.cs`

Namespace: `Fdp.Toolkit.Squad.Maneuvers`

```csharp
/// <summary>
/// Configuration and role logic for the suppress-and-maneuver (base-of-fire + assault).
///
/// Phases:
///   0 Suppressing     - BaseOfFire suppresses; Assault flanks (FarSideReached)
///   1 AssaultComplete - Terminal: assault reached flank
///   2 Aborted         - Terminal (Abort event)
///
/// Roles:
///   RoleId 0 = Unassigned
///   RoleId 1 = BaseOfFire  (hold, suppress)
///   RoleId 2 = Assault     (advance to flank)
///
/// Elements:
///   Element 0 = Base-of-fire element (first half)
///   Element 1 = Assault element (second half)
/// </summary>
public static class SuppressAndManeuverManeuver
{
    // Maneuver kind ID.
    public const ushort ManeuverKind = 3;

    // Phase IDs.
    public const ushort PhaseSuppressing    = 0;
    public const ushort PhaseAssaultComplete = 1;
    public const ushort PhaseAborted        = 2;

    // Role IDs.
    public const byte RoleUnassigned = 0;
    public const byte RoleBaseOfFire = 1;
    public const byte RoleAssault    = 2;

    // Element indices.
    public const byte ElementBaseOfFire = 0;
    public const byte ElementAssault    = 1;

    // --- Transition table ---

    /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
    public static PhaseTransitionEntry[] BuildTransitionTable() =>
        new PhaseTransitionEntry[]
        {
            new PhaseTransitionEntry { FromPhaseId = PhaseSuppressing, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseAssaultComplete },
            new PhaseTransitionEntry { FromPhaseId = PhaseSuppressing, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted         },
        };

    // --- Element partition ---

    /// <summary>
    /// Computes element partition inputs: first half → BaseOfFire element,
    /// second half → Assault element.
    /// </summary>
    public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
    {
        int half = Math.Max(1, memberCount / 2);
        for (int i = 0; i < memberCount; i++)
        {
            float baseScore    = i < half ? 1.0f : 0.1f;
            float assaultScore = i < half ? 0.1f : 1.0f;
            inputs[i] = new MemberPartitionInput(baseScore, assaultScore);
        }
    }

    // --- Role assignment ---

    /// <summary>
    /// 4 candidates: 2 BaseOfFire slots + 2 Assault slots.
    /// Using 4 candidates ensures all 4 members get a role with maxFocusFire=1.
    /// </summary>
    public static readonly RoleSlotCandidate[] StandardCandidates =
        new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RoleBaseOfFire },
            new RoleSlotCandidate { RoleId = RoleBaseOfFire },
            new RoleSlotCandidate { RoleId = RoleAssault    },
            new RoleSlotCandidate { RoleId = RoleAssault    },
        };

    /// <summary>
    /// Builds a 4-column score matrix.
    /// BaseOfFire element members score high for BaseOfFire candidates;
    /// Assault element members score high for Assault candidates.
    /// </summary>
    public static void BuildRoleScoreMatrix(
        ref SquadCognitiveState state,
        int memberCount,
        Span<float> scoreMatrix)
    {
        var membersSpan = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(
                ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

        // Columns: 0=BaseOfFire0, 1=BaseOfFire1, 2=Assault0, 3=Assault1
        for (int m = 0; m < memberCount; m++)
        {
            bool isBase = membersSpan[m] == ElementBaseOfFire;
            scoreMatrix[m * 4 + 0] = isBase ? 1.0f : 0.1f;  // BaseOfFire slot 0
            scoreMatrix[m * 4 + 1] = isBase ? 1.0f : 0.1f;  // BaseOfFire slot 1
            scoreMatrix[m * 4 + 2] = isBase ? 0.1f : 1.0f;  // Assault slot 0
            scoreMatrix[m * 4 + 3] = isBase ? 0.1f : 1.0f;  // Assault slot 1
        }
    }
}
```

**Usings:** Same as `BoundingOverwatchManeuver.cs`.

---

## Task 2: Tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/SuppressAndManeuverManeuverTests.cs`

### SC-P5-03-1: FarSideReached → AssaultComplete transition

```csharp
[Fact]
public void Suppressing_FarSideReached_TransitionsToAssaultComplete()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
    state.PhaseEnteredTick = 0;
    var table = SuppressAndManeuverManeuver.BuildTransitionTable();

    bool transitioned = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
        table, currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

    Assert.True(transitioned);
    Assert.Equal(SuppressAndManeuverManeuver.PhaseAssaultComplete, state.PhaseId);
}
```

### SC-P5-03-2: Abort → Aborted transition

```csharp
[Fact]
public void Suppressing_AbortEvent_TransitionsToAborted()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
    state.PhaseEnteredTick = 0;
    var table = SuppressAndManeuverManeuver.BuildTransitionTable();

    bool transitioned = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.Abort) }),
        table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

    Assert.True(transitioned);
    Assert.Equal(SuppressAndManeuverManeuver.PhaseAborted, state.PhaseId);
}
```

### SC-P5-03-3: Role assignment — BaseOfFire and Assault roles correctly split

```csharp
[Fact]
public void RoleAssignment_SplitsBaseOfFireAndAssault_Correctly()
{
    var (repo, commander, members) = BuildFixture(memberCount: 4);
    ref var state = ref SquadCognitiveState.Project(
        ref repo.GetComponentRW<Blackboard1024>(commander));

    Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
    SuppressAndManeuverManeuver.ComputePartitionInputs(4, inputs);
    ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

    Span<float> scoreMatrix = stackalloc float[4 * 4];
    SuppressAndManeuverManeuver.BuildRoleScoreMatrix(ref state, 4, scoreMatrix);
    RoleSlotAssignmentPrimitive.AssignRoles(ref state,
        SuppressAndManeuverManeuver.StandardCandidates, scoreMatrix, 4);

    var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

    // Members 0,1 → BaseOfFire; members 2,3 → Assault.
    Assert.Equal(SuppressAndManeuverManeuver.RoleBaseOfFire, rolesSpan[0].RoleId);
    Assert.Equal(SuppressAndManeuverManeuver.RoleBaseOfFire, rolesSpan[1].RoleId);
    Assert.Equal(SuppressAndManeuverManeuver.RoleAssault,    rolesSpan[2].RoleId);
    Assert.Equal(SuppressAndManeuverManeuver.RoleAssault,    rolesSpan[3].RoleId);
}
```

### SC-P5-03-4: Timer fallback (suppression dwell) transitions to recovery

```csharp
[Fact]
public void PhaseSequencer_DwellTimeout_TransitionsToRecovery_WhileSuppressing()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
    state.PhaseEnteredTick = 0;
    var table = SuppressAndManeuverManeuver.BuildTransitionTable();

    // No event, dwell elapsed.
    bool transitioned = PhaseSequencer.Advance(ref state,
        ReadOnlySpan<PhaseEvent>.Empty, table,
        currentTick: 200, dwellTimeoutTicks: 100, recoveryPhaseId: SuppressAndManeuverManeuver.PhaseAborted);

    Assert.True(transitioned);
    Assert.Equal(SuppressAndManeuverManeuver.PhaseAborted, state.PhaseId);
}
```

### BuildFixture helper

Copy exactly from `DangerAreaCrossingManeuverTests.cs` (same registrations and setup).

---

## Checklist

- [ ] `SuppressAndManeuverManeuver.cs` compiles with 0 errors.
- [ ] 4 tests pass (SC-P5-03-1 through SC-P5-03-4).
- [ ] All 97 pre-existing squad tests still pass.
- [ ] Total squad tests: 101 minimum (97 + 4).
- [ ] No new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/SuppressAndManeuverManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/SuppressAndManeuverManeuverTests.cs` |
