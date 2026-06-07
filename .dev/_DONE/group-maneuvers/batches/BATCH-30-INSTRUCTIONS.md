# BATCH-30 Instructions — Phase 5 Part 4: Hill-Crest Hull-Down Maneuver (P5-04)

**Covers:** TASK-SQD-P5-04  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §8.4

---

## Context

P5-01/P5-02/P5-03 are committed. Same folder convention:
`FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/`

Read `SuppressAndManeuverManeuver.cs` to match coding style.

## Design

Hill-crest hull-down: tanks advance to firing-line positions (expose), fire, then retire to
defilade. Wave-based element partition (2 at a time from a platoon). Burned-slot tracking
prevents tanks from re-exposing in the same slot.

```
Phases:
  0  Deploying     - Wave element advances to assigned firing-line slot (DefiladeReached = arrived at slot)
                     FarSideReached → Firing
                     Abort          → Aborted
  1  Firing        - Wave fires from hull-down positions.
                     ShotFired      → Retiring  (fired; now retire to defilade)
                     Abort          → Aborted
  2  Retiring      - Wave retires to defilade baseline.
                     DefiladeReached → Deploying  (next wave; loops back to phase 0)
                     Abort           → Aborted
  3  Aborted       - Terminal.
```

Wave slot management uses `SlotRotation`:
- `AcquireSlot` per wave member before deployment
- `ReleaseSlot` when wave retires
- `BurnSlot` when a tank is lost (should never re-expose in that slot)

```
Roles:
  RoleId 0 = Unassigned
  RoleId 1 = Deploying    (advance to firing slot)
  RoleId 2 = Covering     (hold at defilade, overwatch)

Elements:
  Element 0 = current wave (advancing)
  Element 1 = reserve (at defilade)
```

---

## Task 1: `HillCrestHullDownManeuver` static class

**New file:** `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/HillCrestHullDownManeuver.cs`

Namespace: `Fdp.Toolkit.Squad.Maneuvers`

```csharp
/// <summary>
/// Configuration and slot-management logic for the hill-crest hull-down rotation maneuver.
///
/// Phases:
///   0 Deploying  - Wave advances to firing slots (FarSideReached -> Firing)
///   1 Firing     - Wave fires from hull-down (ShotFired -> Retiring)
///   2 Retiring   - Wave retires to defilade (DefiladeReached -> Deploying, next wave)
///   3 Aborted    - Terminal phase
///
/// Roles:
///   RoleId 0 = Unassigned
///   RoleId 1 = Deploying   (current wave, advancing)
///   RoleId 2 = Covering    (reserve wave, at defilade)
///
/// Elements:
///   Element 0 = current wave
///   Element 1 = reserve
///
/// Slot management uses <see cref="SlotRotation"/> with AcquireSlot + BurnSlot.
/// This matches the legacy HillAttackMutableState.WaveUsedSlotsMask + BurnedSlotsMask semantics.
/// </summary>
public static class HillCrestHullDownManeuver
{
    // Maneuver kind ID.
    public const ushort ManeuverKind = 4;

    // Phase IDs.
    public const ushort PhaseDeploying = 0;
    public const ushort PhaseFiring    = 1;
    public const ushort PhaseRetiring  = 2;
    public const ushort PhaseAborted   = 3;

    // Role IDs.
    public const byte RoleUnassigned = 0;
    public const byte RoleDeploying  = 1;
    public const byte RoleCovering   = 2;

    // Element indices.
    public const byte ElementWave    = 0;
    public const byte ElementReserve = 1;

    // --- Transition table ---

    /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
    public static PhaseTransitionEntry[] BuildTransitionTable() =>
        new PhaseTransitionEntry[]
        {
            new PhaseTransitionEntry { FromPhaseId = PhaseDeploying, EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseFiring    },
            new PhaseTransitionEntry { FromPhaseId = PhaseDeploying, EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
            new PhaseTransitionEntry { FromPhaseId = PhaseFiring,    EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseRetiring  },
            new PhaseTransitionEntry { FromPhaseId = PhaseFiring,    EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
            new PhaseTransitionEntry { FromPhaseId = PhaseRetiring,  EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseDeploying },
            new PhaseTransitionEntry { FromPhaseId = PhaseRetiring,  EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
        };

    // --- Element partition ---

    /// <summary>
    /// Computes element partition inputs for the current wave.
    /// Wave element members: first <paramref name="waveSize"/> members (by roster index).
    /// Remaining members: reserve element.
    /// </summary>
    public static void ComputePartitionInputs(
        int memberCount, int waveSize, Span<MemberPartitionInput> inputs)
    {
        for (int i = 0; i < memberCount; i++)
        {
            float waveScore    = i < waveSize ? 1.0f : 0.1f;
            float reserveScore = i < waveSize ? 0.1f : 1.0f;
            inputs[i] = new MemberPartitionInput(waveScore, reserveScore);
        }
    }

    // --- Role assignment ---

    /// <summary>
    /// 4 candidates: 2 Deploying slots + 2 Covering slots.
    /// Using 4 candidates supports up to 4 wave members with maxFocusFire=1.
    /// </summary>
    public static readonly RoleSlotCandidate[] StandardCandidates =
        new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RoleDeploying },
            new RoleSlotCandidate { RoleId = RoleDeploying },
            new RoleSlotCandidate { RoleId = RoleCovering  },
            new RoleSlotCandidate { RoleId = RoleCovering  },
        };

    /// <summary>
    /// Builds a 4-column score matrix.
    /// Wave element members score high for Deploying; reserve members score high for Covering.
    /// </summary>
    public static void BuildRoleScoreMatrix(
        ref SquadCognitiveState state,
        int memberCount,
        Span<float> scoreMatrix)
    {
        var membersSpan = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(
                ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

        // Columns: 0=Deploying0, 1=Deploying1, 2=Covering0, 3=Covering1
        for (int m = 0; m < memberCount; m++)
        {
            bool isWave = membersSpan[m] == ElementWave;
            scoreMatrix[m * 4 + 0] = isWave ? 1.0f : 0.1f;
            scoreMatrix[m * 4 + 1] = isWave ? 1.0f : 0.1f;
            scoreMatrix[m * 4 + 2] = isWave ? 0.1f : 1.0f;
            scoreMatrix[m * 4 + 3] = isWave ? 0.1f : 1.0f;
        }
    }

    // --- Slot allocation helpers (parity with legacy HillAttackMutableState) ---

    /// <summary>
    /// Computes the total number of slots from a firing-line segment length and spacing.
    /// Matches the legacy: <c>Math.Max(1, (int)(segLen / spacing))</c> capped at 16.
    /// </summary>
    public static int ComputeTotalSlots(float segmentLength, float spacing)
    {
        if (spacing <= 0f) spacing = 30f;
        int count = Math.Max(1, (int)(segmentLength / spacing));
        return count > 16 ? 16 : count;
    }
}
```

**Usings:** Same as other maneuver files.

---

## Task 2: Tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/HillCrestHullDownManeuverTests.cs`

### SC-P5-04-1: Full wave cycle (Deploying → Firing → Retiring → Deploying)

Verify the 3-phase cycle runs once:

```csharp
[Fact]
public void WaveCycle_DeployFireRetire_CyclesBackToDeploying()
{
    var state = default(SquadCognitiveState);
    state.PhaseId          = HillCrestHullDownManeuver.PhaseDeploying;
    state.PhaseEnteredTick = 0;
    var table = HillCrestHullDownManeuver.BuildTransitionTable();

    // Deploy → Firing (FarSideReached)
    bool t1 = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
        table, currentTick: 1, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
    Assert.True(t1);
    Assert.Equal(HillCrestHullDownManeuver.PhaseFiring, state.PhaseId);

    // Firing → Retiring (ShotFired)
    bool t2 = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.ShotFired) }),
        table, currentTick: 2, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
    Assert.True(t2);
    Assert.Equal(HillCrestHullDownManeuver.PhaseRetiring, state.PhaseId);

    // Retiring → Deploying (DefiladeReached = next wave cycles back)
    bool t3 = PhaseSequencer.Advance(ref state,
        new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.DefiladeReached) }),
        table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
    Assert.True(t3);
    Assert.Equal(HillCrestHullDownManeuver.PhaseDeploying, state.PhaseId);
}
```

### SC-P5-04-2: Burn/reuse semantics — 2 burns over 6 slots leave 4 usable

This is the parity test: same semantics as `HillAttackMutableState.BurnedSlotsMask`.

```csharp
[Fact]
public void SlotRotation_2Burns_Over6Slots_Leave4Usable_InOrder()
{
    var rotation = default(SlotRotationState);
    const int totalSlots = 6;

    // Burn slots 0 and 2 (simulates 2 tank losses).
    SlotRotation.BurnSlot(ref rotation, 0);
    SlotRotation.BurnSlot(ref rotation, 2);

    // Remaining slots in order: 1, 3, 4, 5.
    int s1 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
    int s2 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
    int s3 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
    int s4 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
    int s5 = SlotRotation.AcquireSlot(ref rotation, totalSlots);  // should be -1

    Assert.Equal(1, s1);
    Assert.Equal(3, s2);
    Assert.Equal(4, s3);
    Assert.Equal(5, s4);
    Assert.Equal(-1, s5);  // all usable slots exhausted
}
```

### SC-P5-04-3: Resume-trap — live NavigationStatus checked per tick (not cached)

Simulates the "resume-trap" scenario: if `NavigationStatus.Result` is set mid-tick,
`SquadEventIngressSystem` detects it on the NEXT call — it reads current state, not cached.

```csharp
[Fact]
public void SquadEventIngressSystem_DetectsFarSideReached_FromLiveNavStatus()
{
    var (repo, commander, members) = BuildFixture(memberCount: 2);
    var system = new SquadEventIngressSystem { FarSideIntentId = 77u };
    var events = new List<PhaseEvent>();

    // First tick: ammo snapshot; nav not arrived.
    system.Run(repo, commander, events);
    Assert.Empty(events);

    // Simulate "arrived mid-tick" by setting Result AFTER the first tick.
    ref var ns = ref repo.GetComponentRW<NavigationStatus>(members[0]);
    ns.Result   = NavigationResult.Arrived;
    ns.IntentId = 77u;

    // Second tick: system reads live state and detects arrival.
    events.Clear();
    system.Run(repo, commander, events);
    Assert.Contains(events, e => e.Kind == PhaseEventKind.FarSideReached);
}
```

### SC-P5-04-4: ComputeTotalSlots matches legacy formula

```csharp
[Fact]
public void ComputeTotalSlots_MatchesLegacyFormula()
{
    // Legacy: Math.Max(1, (int)(segLen / spacing)), capped at 16.
    // 150m / 30m = 5 slots.
    Assert.Equal(5, HillCrestHullDownManeuver.ComputeTotalSlots(150f, 30f));
    // 0m → at least 1.
    Assert.Equal(1, HillCrestHullDownManeuver.ComputeTotalSlots(0f, 30f));
    // 500m / 30m = 16 (capped).
    Assert.Equal(16, HillCrestHullDownManeuver.ComputeTotalSlots(500f, 30f));
    // Default spacing when 0 supplied: treats as 30m.
    Assert.Equal(5, HillCrestHullDownManeuver.ComputeTotalSlots(150f, 0f));
}
```

### BuildFixture helper

Copy from existing test files. Must register `NavigationStatus`, `WeaponState`, and `UnitSubordinate` for the SC-P5-04-3 test. The helper can be the same as in `SquadEventIngressSystemTests.cs`.

Also include `using Fdp.Toolkit.Squad.Systems;` and `using Fdp.Toolkit.Navigation;` for the ingress test.

Also `using System.Collections.Generic;` for `List<PhaseEvent>`.

---

## Checklist

- [ ] `HillCrestHullDownManeuver.cs` compiles with 0 errors.
- [ ] 4 tests pass (SC-P5-04-1 through SC-P5-04-4).
- [ ] All 101 pre-existing squad tests still pass.
- [ ] Total squad tests: 105 minimum (101 + 4).
- [ ] No new warnings.

## File summary

| Action | File |
|---|---|
| CREATE | `FDP/Toolkits/Fdp.Toolkits/Squad/Maneuvers/HillCrestHullDownManeuver.cs` |
| CREATE | `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Maneuvers/HillCrestHullDownManeuverTests.cs` |
