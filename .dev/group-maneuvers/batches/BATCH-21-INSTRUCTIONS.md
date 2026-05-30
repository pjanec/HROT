# BATCH-21 Instructions — Group Maneuvers Phase 1: Primitives Library

**Workstream:** group-maneuvers  
**Tasks:** TASK-SQD-P1-01 through TASK-SQD-P1-05  
**Depends on:** BATCH-20 (all committed, `a03a477f`)

---

## 0. Context

BATCH-20 landed all Phase-0 prerequisites:
- `AssignmentSlot` is 16 B; `AssignmentSlotArray` is 256 B.
- `SquadCognitiveState` is the 1024 B blackboard projection with sub-regions at pinned offsets.
- `DecisionKind.ManeuverSelect = 3` and the UT0151 analyzer are registered.
- `FakeDangerAreaProvider` fluent builder is ready.
- 14 new tests pass; no regressions.

Phase 1 implements the **five Brain-resident primitives** (§2 of
`Squad_Coordination_Design_v1_1.md`) as a pure-C# library. All are static classes
operating on `SquadCognitiveState` (or sub-structs) via `ref` parameters. No ECS dependencies
outside of tests (where `UtilityTestWorld` or plain struct instances suffice).

---

## 1. Pre-flight checks

Before writing any code:
1. Build the solution (`dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`).
   Zero warnings on new files — only pre-existing warnings are acceptable.
2. Verify baseline: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
   shows exactly the same pre-existing failures (ReplayBrowser.Export, Replication.IdAllocation,
   Navigation frustration watchdog). Record the baseline pass count.

---

## 2. Task P1-01 — Element partition primitive with hysteresis

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/ElementPartitionPrimitive.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

**Using directives needed:**
```csharp
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Squad;
```

### 2.1 MemberPartitionInput struct

```csharp
/// <summary>
/// Per-member per-element score inputs for the partition primitive.
/// Up to 4 element kinds (covering, bounding, overwatch, reserve).
/// </summary>
public struct MemberPartitionInput
{
    private float _s0, _s1, _s2, _s3;

    public MemberPartitionInput(float s0, float s1 = 0f, float s2 = 0f, float s3 = 0f)
    {
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
    }

    /// <summary>Score for element index <paramref name="i"/> (0..3).</summary>
    public float this[int i] =>
        i == 0 ? _s0 :
        i == 1 ? _s1 :
        i == 2 ? _s2 : _s3;
}
```

### 2.2 ElementPartitionPrimitive static class

```csharp
/// <summary>
/// Partitions squad members across N elements with hysteresis to prevent
/// disruptive mid-maneuver reshuffling (design §4.1).
/// </summary>
public static class ElementPartitionPrimitive
{
    /// <summary>
    /// Assigns each member to the highest-scoring element, subject to a
    /// decisive-gap hysteresis: a member stays in its current element unless
    /// the new winner's score exceeds the current element's score by at least
    /// <paramref name="decisiveGap"/>.
    /// </summary>
    /// <param name="state">Squad cognitive state to read/write.</param>
    /// <param name="inputs">Per-member element scores. Length must equal the squad roster size.</param>
    /// <param name="elementCount">Number of elements in use (2..4).</param>
    /// <param name="decisiveGap">
    ///   Minimum score advantage required to move a member to a new element
    ///   (anti-flip-flop; mirrors PostureSelect hysteresis in Utility §4.5).
    /// </param>
    /// <param name="repartitionsCount">
    ///   Number of members who actually changed element this call.
    /// </param>
    public static void Partition(
        ref SquadCognitiveState state,
        ReadOnlySpan<MemberPartitionInput> inputs,
        int elementCount,
        float decisiveGap,
        out int repartitionsCount)
```

**Implementation notes:**
- Iterate `inputs` (one per member). For each member `i`:
  - Find the element index `newBest` with the highest score among `[0, elementCount)`.
  - Read the current element `current = state.Elements.MemberElements[i]`.
  - Compute `inputs[i][newBest] - inputs[i][current]`.
  - If `newBest != current` AND the gap `> decisiveGap`, update the element index and
    increment `repartitionsCount`.
- Access `state.Elements.MemberElements[i]` via the InlineArray defensive-copy pattern:
  `MemoryMarshal.CreateSpan(ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16)[i]`
  (write path) to avoid the InlineArray defensive-copy trap.
- Bump `state.Elements.LastRepartitionTick` only when at least one member changed
  (`repartitionsCount > 0`). The tick value is NOT available to the primitive — callers
  pass it separately. **Do not accept a tick parameter here.** Instead, the caller bumps
  `state.Elements.LastRepartitionTick` after calling this method if `repartitionsCount > 0`.
  (See the test for the expected pattern.)
- Zero allocation. No LINQ. No managed heap.

---

## 3. Task P1-02 — Tactical-feature reference handles

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/TacticalFeatureHandles.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

**Using directives:**
```csharp
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
```

### 3.1 TacticalFeatureHandles static class

```csharp
/// <summary>
/// Primitives for acquiring and refreshing a reference to the active tactical feature
/// (danger area / navmesh polygon) that the squad is currently working (design §2 primitive 2,
/// §5.2).
/// </summary>
public static class TacticalFeatureHandles
{
    /// <summary>
    /// Sets the active feature reference in <paramref name="state"/> to
    /// <paramref name="featureId"/>. Idempotent — calling again with the same id
    /// is a no-op.
    /// </summary>
    public static void Acquire(ref SquadCognitiveState state, uint featureId)

    /// <summary>
    /// Searches <paramref name="descriptors"/> for a descriptor whose
    /// <see cref="DangerAreaDescriptor.FeatureId"/> matches <c>state.ActiveFeatureId</c>.
    /// Returns <c>true</c> and writes the match into <paramref name="descriptor"/>
    /// when found; <c>false</c> otherwise.
    /// Does NOT modify <c>state.ActiveFeatureId</c> on failure — the caller decides
    /// whether to abort the maneuver.
    /// </summary>
    public static bool TryRefresh(
        ref SquadCognitiveState state,
        ReadOnlySpan<DangerAreaDescriptor> descriptors,
        out DangerAreaDescriptor descriptor)
}
```

**Implementation notes:**
- `Acquire`: only writes `state.ActiveFeatureId = featureId` when `state.ActiveFeatureId != featureId`.
- `TryRefresh`: O(N) linear scan; N is the descriptor cap (small). Use `foreach (ref readonly var d in descriptors)`.
- Zero allocation.

---

## 4. Task P1-03 — Role / slot assignment primitive (allocation-matrix reuse)

This task requires:
1. Extracting the **greedy selection core** from `ThreatMatrixAssignmentSystem` into a new
   shared helper `GreedyMatrixAssigner`.
2. Refactoring `ThreatMatrixAssignmentSystem.Run()` to use `GreedyMatrixAssigner`.
3. Implementing `RoleSlotAssignmentPrimitive` that also uses `GreedyMatrixAssigner`.

### 4.1 New file: GreedyMatrixAssigner

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/GreedyMatrixAssigner.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

```csharp
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Greedy O(m*n) assignment over a pre-built score matrix.
    /// Shared by <see cref="Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem"/> and
    /// <see cref="RoleSlotAssignmentPrimitive"/>.
    /// </summary>
    public static unsafe class GreedyMatrixAssigner
    {
        /// <summary>
        /// Greedily assigns each of the <paramref name="memberCount"/> rows to the
        /// highest-scoring column in <paramref name="scoreMatrix"/>, subject to
        /// <paramref name="maxFocusFire"/> concurrent assignments per column.
        /// </summary>
        /// <param name="scoreMatrix">
        ///   Flat row-major matrix of size <c>memberCount * candidateCount</c>.
        ///   <c>scoreMatrix[m * candidateCount + c]</c> is the score of member <c>m</c>
        ///   for candidate <c>c</c>.
        /// </param>
        /// <param name="memberCount">Number of rows (squad members). Max 16.</param>
        /// <param name="candidateCount">Number of columns (candidates). Max 16.</param>
        /// <param name="maxFocusFire">
        ///   Maximum number of members that may be assigned to the same candidate.
        /// </param>
        /// <param name="assignments">
        ///   Output span of length <paramref name="memberCount"/>. Each entry is the
        ///   winning candidate index (0-based), or -1 when no acceptable candidate was
        ///   found for that member.
        /// </param>
        public static void Assign(
            ReadOnlySpan<float> scoreMatrix,
            int memberCount,
            int candidateCount,
            int maxFocusFire,
            Span<int> assignments)
```

**Implementation:**
```csharp
        {
            // Stack-allocated focus-fire counter per candidate (max 16).
            int* focusCount = stackalloc int[candidateCount];
            for (int c = 0; c < candidateCount; c++)
                focusCount[c] = 0;

            for (int m = 0; m < memberCount; m++)
            {
                float best = -1f;
                int bestC  = -1;
                int rowBase = m * candidateCount;
                for (int c = 0; c < candidateCount; c++)
                {
                    if (focusCount[c] >= maxFocusFire) continue;
                    float s = scoreMatrix[rowBase + c];
                    if (s > best) { best = s; bestC = c; }
                }
                assignments[m] = best > 0f ? bestC : -1;
                if (bestC >= 0 && best > 0f)
                    focusCount[bestC]++;
            }
        }
```

### 4.2 Refactor ThreatMatrixAssignmentSystem.Run()

**File to modify:**
`FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs`

Add the using:
```csharp
using Fdp.Toolkit.Squad.Primitives;
```

Replace the inner loop of `Run()` so it:
1. Builds a flat float score matrix on the stack: `float* matrixBuf = stackalloc float[maxMembers * maxTargets]`
2. Fills `matrixBuf[memberIdx * maxTargets + tIdx]` by calling
   `UtilityScorer.Evaluate(repo, member, in def, target, ref tmpBuffer, null)` and reading
   `tmpBuffer.Count > 0 ? tmpBuffer.GetSpanRO()[0].Score : 0f`.
3. Calls `GreedyMatrixAssigner.Assign(new ReadOnlySpan<float>(matrixBuf, maxMembers * maxTargets), maxMembers, maxTargets, _maxFocusFireCount, assignmentsSpan)` where `assignmentsSpan` is a `Span<int>` from a stackalloc `int[maxMembers]`.
4. Writes back: for each member where `assignments[memberIdx] >= 0`, call
   `state.SetAssignment(memberIdx, (ulong)leaderMem.EntityIds[assignments[memberIdx]])` and
   set `state.GetSlot(memberIdx).AssignmentScore`.
5. Writes `FocusFireCount` using a second pass counting how many members share the same target.

**Do not change any public API — only the internal implementation of `Run()`.
All existing ThreatMatrixAssignment tests MUST still pass.**

### 4.3 New file: RoleSlotAssignmentPrimitive

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/RoleSlotAssignmentPrimitive.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

**Using directives:**
```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
```

#### RoleSlotCandidate struct

```csharp
/// <summary>
/// One role/slot candidate for the greedy assignment pass.
/// The score for a given (member, candidate) pair is supplied externally in the
/// <c>scoreMatrix</c> parameter.
/// </summary>
public struct RoleSlotCandidate
{
    /// <summary>Role identifier assigned when this candidate wins.</summary>
    public byte RoleId;
    private byte _pad;
}
```

#### RoleSlotAssignmentPrimitive static class

```csharp
/// <summary>
/// Assigns roles/slots to squad members using the same greedy matrix algorithm
/// as <see cref="Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem"/> (design §2 primitive 3).
/// Writes winning <see cref="RoleSlot.RoleId"/> values into
/// <c>state.Roles</c>. Re-run on every phase change.
/// </summary>
public static class RoleSlotAssignmentPrimitive
{
    /// <summary>
    /// Runs the greedy role assignment.
    /// </summary>
    /// <param name="state">Squad cognitive state to write roles into.</param>
    /// <param name="candidates">
    ///   Role candidates. Length must equal the number of columns in
    ///   <paramref name="scoreMatrix"/>.
    /// </param>
    /// <param name="scoreMatrix">
    ///   Caller-provided row-major score matrix of size
    ///   <c>memberCount * candidates.Length</c>.
    ///   The caller computes scores (e.g. from a <see cref="Fdp.Toolkit.Utility.UtilityDecisionDef"/>).
    /// </param>
    /// <param name="memberCount">
    ///   Number of members (rows) to assign. Must not exceed 16.
    /// </param>
    public static void AssignRoles(
        ref SquadCognitiveState state,
        ReadOnlySpan<RoleSlotCandidate> candidates,
        ReadOnlySpan<float> scoreMatrix,
        int memberCount)
```

**Implementation:**
- If `candidates.IsEmpty` or `memberCount == 0`, return immediately (no-op).
- Stackalloc `int[] assignments` of length `memberCount`.
- Call `GreedyMatrixAssigner.Assign(scoreMatrix, memberCount, candidates.Length, maxFocusFire: 1, assignments)`.
  Note: for role assignment, each role is typically assigned to one member (maxFocusFire=1 is the sensible default). Add a `maxFocusFire` parameter with default 1 if you like.
- Write back: for each member `i`, if `assignments[i] >= 0`, set
  `RolesSpan(ref state)[i].RoleId = candidates[assignments[i]].RoleId`.
- `RolesSpan` helper (private): `MemoryMarshal.CreateSpan(ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16)`.

**Important:** `SC-P1-03-3` says calling with `candidates.Length == 0` is a no-op — test this.

---

## 5. Task P1-04 — Phase sequencer with turn-taking (squad-HSM substrate)

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/PhaseSequencer.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

**Using directive:** `using Fdp.Toolkit.Squad;`

### 5.1 PhaseEventKind enum

```csharp
/// <summary>
/// Kinds of completion events that can drive squad-HSM phase transitions.
/// </summary>
public enum PhaseEventKind : byte
{
    ShotFired          = 0,
    DefiladeReached    = 1,
    FarSideReached     = 2,
    BoundComplete      = 3,
    VetoDetected       = 4,   // always routes to recovery phase, overrides other events
    Abort              = 5
}
```

### 5.2 PhaseEvent struct

```csharp
/// <summary>One squad phase completion event.</summary>
public struct PhaseEvent
{
    public PhaseEventKind Kind;
    public PhaseEvent(PhaseEventKind kind) { Kind = kind; }
}
```

### 5.3 PhaseTransitionEntry struct

```csharp
/// <summary>
/// One entry in a per-maneuver transition table:
/// when in <see cref="FromPhaseId"/> and <see cref="EventKind"/> fires,
/// transition to <see cref="ToPhaseId"/>.
/// </summary>
public struct PhaseTransitionEntry
{
    public ushort         FromPhaseId;
    public PhaseEventKind EventKind;
    private byte          _pad;
    public ushort         ToPhaseId;
}
```

### 5.4 PhaseSequencer static class

```csharp
/// <summary>
/// Drives the squad HSM: processes completion events and dwell-timeout against
/// a caller-supplied transition table, updating <see cref="SquadCognitiveState.PhaseId"/>
/// and <see cref="SquadCognitiveState.PhaseEnteredTick"/> (design §2 primitive 4, §9).
/// </summary>
public static class PhaseSequencer
{
    /// <summary>
    /// Advances the phase state machine.
    /// </summary>
    /// <param name="state">Squad cognitive state to read/write.</param>
    /// <param name="events">
    ///   Completion events for this tick, processed in span order.
    ///   <see cref="PhaseEventKind.VetoDetected"/> always overrides other events and
    ///   routes to <paramref name="recoveryPhaseId"/>.
    /// </param>
    /// <param name="table">Per-maneuver transition table.</param>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <param name="dwellTimeoutTicks">
    ///   If no completion event fires and
    ///   <c>currentTick - state.PhaseEnteredTick >= dwellTimeoutTicks</c>,
    ///   transition to <paramref name="recoveryPhaseId"/>.
    /// </param>
    /// <param name="recoveryPhaseId">
    ///   Phase id to transition to on veto or dwell-timeout.
    /// </param>
    /// <returns>
    ///   <c>true</c> if a phase transition occurred this call.
    /// </returns>
    public static bool Advance(
        ref SquadCognitiveState state,
        ReadOnlySpan<PhaseEvent> events,
        ReadOnlySpan<PhaseTransitionEntry> table,
        uint currentTick,
        uint dwellTimeoutTicks,
        ushort recoveryPhaseId)
```

**Implementation notes:**
- First scan `events` for `VetoDetected`. If found, transition immediately to `recoveryPhaseId` and return `true`. Do not process remaining events.
- Then scan `events` for the first event whose `(FromPhaseId, EventKind)` matches the current `state.PhaseId` in `table`. If found, transition to `table[i].ToPhaseId` and return `true`.
- If no matching event and `events` is non-empty but no match found, check dwell timeout next.
- If `events` is empty or no match: check `currentTick - state.PhaseEnteredTick >= dwellTimeoutTicks`; if so, transition to `recoveryPhaseId` and return `true`.
- On any transition: `state.PhaseId = newPhaseId; state.PhaseEnteredTick = currentTick`.
- Returns `false` when no transition occurred.
- Zero allocation.

---

## 6. Task P1-05 — Exposed-slot rotation with burn/reuse

**File to create:**
`FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/SlotRotation.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives`

### 6.1 SlotRotationState struct

```csharp
/// <summary>
/// Compact bitmask state for tracking used and burned exposure slots.
/// 4 bytes. Supports up to 16 slots (ushort mask width).
/// </summary>
public struct SlotRotationState
{
    /// <summary>Bitmask of currently-in-use slots (bit i = slot i is occupied).</summary>
    public ushort UsedMask;
    /// <summary>Bitmask of permanently burned slots (bit i = slot i must not be reused).</summary>
    public ushort BurnedMask;
}
```

### 6.2 SlotRotation static class

```csharp
/// <summary>
/// Exposed-slot rotation primitive with burn/reuse tracking (design §2 primitive 5).
/// Generalizes <c>HillAttackMutableState.BurnedSlotsMask</c> /
/// <c>WaveUsedSlotsMask</c> from the hill-attack doctrine.
/// </summary>
public static class SlotRotation
{
    /// <summary>
    /// Acquires the next available (not burned, not in use) slot from
    /// <c>[0, totalSlots)</c>.
    /// </summary>
    /// <returns>The acquired slot index, or -1 when all slots are used or burned.</returns>
    public static int AcquireSlot(ref SlotRotationState rotation, int totalSlots)

    /// <summary>
    /// Releases a previously acquired slot, making it available for re-acquisition.
    /// A burned slot remains unavailable even after release (burn dominates).
    /// </summary>
    public static void ReleaseSlot(ref SlotRotationState rotation, int slotIndex)

    /// <summary>
    /// Permanently burns a slot so it will never be returned by
    /// <see cref="AcquireSlot"/> again, even after <see cref="ReleaseSlot"/> is called.
    /// </summary>
    public static void BurnSlot(ref SlotRotationState rotation, int slotIndex)
}
```

**Implementation:**
```
AcquireSlot: scan i from 0 to totalSlots-1; skip if (BurnedMask >> i) & 1 == 1 or
             (UsedMask >> i) & 1 == 1; set UsedMask bit; return i. Return -1 if none.
ReleaseSlot: clear UsedMask bit at slotIndex (do NOT touch BurnedMask).
BurnSlot:    set BurnedMask bit at slotIndex. Also clear UsedMask bit
             (burned slot is no longer "in use"; future AcquireSlot will skip it via BurnedMask check).
```

---

## 7. Tests

### 7.1 New test file: ElementPartitionPrimitiveTests.cs

**File path:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/ElementPartitionPrimitiveTests.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives.Tests`

Write the following 4 tests (matching SC-P1-01-1 through SC-P1-01-4):

**Test 1: `Partition_FavorsHighestScore_ElementAssigned`** (SC-P1-01-1)
- Create a `SquadCognitiveState` (default).
- Input: 3 members; member 0 scores `[0.9f, 0.2f, 0.1f]` (element 0 wins).
- Call `ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 3, decisiveGap: 0.15f, out int count)`.
- Assert member 0 is in element 0 and `count >= 1` (first partition from default = change).

**Test 2: `Partition_SmallGap_HysteresisHolds`** (SC-P1-01-2)
- Setup: put member 0 in element 0 first (use a decisive initial partition).
- Second call: member 0 scores `[0.5f, 0.55f, 0.0f]` — element 1 wins by only 0.05, below `decisiveGap=0.15`.
- Assert member 0 stays in element 0 and `count == 0`.

**Test 3: `Partition_DecisiveGap_MemberMoves`** (SC-P1-01-3)
- Setup same as test 2.
- Second call: member 0 scores `[0.3f, 0.6f, 0.0f]` — element 1 wins by 0.30, above `decisiveGap=0.15`.
- Assert member 0 moves to element 1 and `count == 1`.

**Test 4: `Partition_ZeroAllocs`** (SC-P1-01-4)
- Use `GC.GetTotalMemory(true)` before and after `10_000` partition calls.
- Assert allocated bytes == 0.

### 7.2 New test file: TacticalFeatureHandlesTests.cs

**File path:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/TacticalFeatureHandlesTests.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives.Tests`

**Test 1: `Acquire_WritesActiveFeatureId`** (SC-P1-02-1)
- Acquire featureId = 42u. Assert `state.ActiveFeatureId == 42u`.
- Acquire again with 42u. Assert still 42u (idempotent).

**Test 2: `TryRefresh_MatchingDescriptor_ReturnsTrue`** (SC-P1-02-2)
- Build 3 descriptors with featureIds 10, 20, 30.
- Acquire featureId=20.
- `TryRefresh` → assert `true` and `descriptor.FeatureId == 20u`.
- `TryRefresh` for featureId=99 (not acquired) → assert `false`.

**Test 3: `TryRefresh_EvictedDescriptor_ReturnsFalse_ActiveUnchanged`** (SC-P1-02-3)
- Acquire featureId=20. TryRefresh succeeds.
- Build a new span without featureId=20. TryRefresh → assert `false`.
- Assert `state.ActiveFeatureId == 20u` (unchanged by failure).

### 7.3 New test file: RoleSlotAssignmentPrimitiveTests.cs

**File path:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/RoleSlotAssignmentPrimitiveTests.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives.Tests`

**Test 1: `AssignRoles_GreedyAssignment_MatchesExpected`** (SC-P1-03-1)
- 4 members, 4 candidates (Pointman=0, Suppressor=1, Flanker=2, Sector=3).
- Score matrix (row=member, col=candidate):
  ```
  member 0: [0.9f, 0.1f, 0.2f, 0.1f]   -> wins Pointman
  member 1: [0.1f, 0.8f, 0.2f, 0.1f]   -> wins Suppressor
  member 2: [0.2f, 0.1f, 0.7f, 0.2f]   -> wins Flanker
  member 3: [0.1f, 0.1f, 0.1f, 0.6f]   -> wins Sector
  ```
- After `AssignRoles`, assert `state.Roles` element 0..3 have RoleId values 0,1,2,3 respectively.

**Test 2: `AssignRoles_PhaseChangeClearsAndReassigns`** (SC-P1-03-2)
- Run initial assignment. Bump `state.PhaseId++`. Re-run with a different score matrix.
- Assert `state.Roles` reflects the new assignment (not the old one).

**Test 3: `AssignRoles_EmptyCandidates_IsNoOp`** (SC-P1-03-3)
- Pre-fill `state.Roles[0].RoleId = 7`.
- Call `AssignRoles` with `candidates = Span<RoleSlotCandidate>.Empty`.
- Assert `state.Roles[0].RoleId == 7` (unchanged).

### 7.4 New test file: PhaseSequencerTests.cs

**File path:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/PhaseSequencerTests.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives.Tests`

**Test 1: `Advance_MatchingEvent_TransitionsPhase`** (SC-P1-04-1)
- Table: `{ FromPhase=0, FarSideReached -> ToPhase=1 }`.
- State at PhaseId=0, PhaseEnteredTick=0. CurrentTick=5.
- Feed `events = [PhaseEvent(FarSideReached)]`, dwellTimeoutTicks=100.
- Assert `Advance` returns `true`; `state.PhaseId == 1`; `state.PhaseEnteredTick == 5`.

**Test 2: `Advance_DwellTimeout_TransitionsToRecovery`** (SC-P1-04-2)
- Empty events. State at PhaseId=0, PhaseEnteredTick=0. CurrentTick=101.
- dwellTimeoutTicks=100, recoveryPhaseId=99.
- Assert `state.PhaseId == 99`.

**Test 3: `Advance_VetoDetected_OverridesOtherEvents`** (SC-P1-04-3)
- Table: `{ FromPhase=0, FarSideReached -> ToPhase=1 }`.
- State at PhaseId=0.
- Feed `events = [PhaseEvent(FarSideReached), PhaseEvent(VetoDetected)]`. RecoveryPhaseId=99.
- Assert `state.PhaseId == 99` (veto dominates FarSideReached).

### 7.5 New test file: SlotRotationTests.cs

**File path:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/SlotRotationTests.cs`

**Namespace:** `Fdp.Toolkit.Squad.Primitives.Tests`

**Test 1: `AcquireSlot_8Slots_ReturnsSequentialThenMinusOne`** (SC-P1-05-1)
- Fresh `SlotRotationState rot = default`.
- Call `AcquireSlot` 8 times with `totalSlots=8`. Assert returns 0,1,2,3,4,5,6,7.
- 9th call returns -1.

**Test 2: `BurnThenRelease_SlotRemainsUnavailable`** (SC-P1-05-2)
- `BurnSlot(rot, 3)`. Then `ReleaseSlot(rot, 3)`.
- `AcquireSlot(rot, totalSlots=8)` should never return 3 across 8 sequential calls.
- Verify slot 3 is not returned (e.g., acquire slots 0..7 except 3; slot 3 skipped).

**Test 3: `AllSlotsBurned_AcquireReturnsMinusOne`** (SC-P1-05-3)
- Burn all 4 slots in a `totalSlots=4` rotation.
- `AcquireSlot` returns -1.

---

## 8. Compilation and Test Verification

After implementing:

1. Build: `dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
   — must be clean (no errors, no new warnings).

2. Run squad primitives tests:
   `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Squad"`
   — expected: all new tests pass + the 14 Phase-0 tests continue to pass.

3. Run ThreatMatrix tests (regression check for the GreedyMatrixAssigner refactor):
   `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~ThreatMatrix|FullyQualifiedName~StarterPack|FullyQualifiedName~StandardInput"`
   — all must pass (zero regressions from the refactor).

4. Full suite (to confirm pre-existing failures unchanged):
   `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
   — new pass count = baseline + (number of new tests); failure count unchanged or lower.

---

## 9. Success Conditions Summary

| SC | Description |
|----|-------------|
| SC-P1-01-1 | Member assigned to highest-scoring element |
| SC-P1-01-2 | Hysteresis holds on marginal gap |
| SC-P1-01-3 | Member moves on decisive gap; repartitionsCount==1 |
| SC-P1-01-4 | Zero allocs on 10^4 Partition calls |
| SC-P1-02-1 | Acquire writes ActiveFeatureId; idempotent |
| SC-P1-02-2 | TryRefresh returns true only for matching featureId |
| SC-P1-02-3 | Eviction → false; ActiveFeatureId unchanged |
| SC-P1-03-1 | 4-member 4-candidate greedy matches expected assignment |
| SC-P1-03-2 | Re-run after phase change overwrites assignment |
| SC-P1-03-3 | Empty candidates is no-op |
| SC-P1-04-1 | Matching event → phase transition + tick bump |
| SC-P1-04-2 | Dwell timeout → recovery phase |
| SC-P1-04-3 | VetoDetected overrides other events |
| SC-P1-05-1 | Sequential acquisition 0..7; 9th returns -1 |
| SC-P1-05-2 | Burn then release keeps slot unavailable |
| SC-P1-05-3 | All-burned → -1 |

---

## 10. Important Notes for the Developer

1. **InlineArray defensive-copy pattern**: When writing to `state.Elements.MemberElements[i]`,
   you CANNOT write `state.Elements.MemberElements[i] = value` directly on a ref because the
   InlineArray indexer returns a copy. Use:
   ```csharp
   MemoryMarshal.CreateSpan(
       ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16)[i] = value;
   ```
   Similarly for `state.Roles` writes.

2. **RoleSlot access pattern**: Same issue — `state.Roles[i].RoleId = x` will silently no-op.
   Use the `MemoryMarshal.CreateSpan` pattern to get a writable `Span<RoleSlot>`.

3. **GreedyMatrixAssigner in tests**: For P1-03 test, construct the `scoreMatrix` as a flat
   `float[]` with `new float[] { 0.9f, 0.1f, 0.2f, 0.1f, ... }` in row-major order.

4. **ThreatMatrixAssignmentSystem refactor**: The refactored `Run()` must produce
   byte-identical results to the original for the existing 16 ThreatMatrix tests. A pre-built
   matrix approach is semantically equivalent — verify this by running the tests.

5. **No Unicode in comments**: Use plain ASCII in comments and string literals.

6. **AGENTS.md constraint**: Preserve all existing comments exactly. Do not rewrite comments
   in files you modify.
