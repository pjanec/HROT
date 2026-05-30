# BATCH-22 Instructions

**Covers:** P2-00 (pre-flight), P2-01 (SquadPerceptionMergeSystem), P2-02 (SquadInputs)
**Prerequisites:** BATCH-21 committed (977c2d2d). All Phase 1 primitives in place.

---

## Context

Phase 2 brings shared situational awareness to the squad. Before any Phase 2 work begins, two
field renames left over from BATCH-20 must be applied (P2-00). Then we add the
`SquadPerceptionMergeSystem` (P2-01) and the two `SquadKnowsContact` / `SquadContactThreatLevel`
Utility input readers (P2-02).

---

## Task P2-00: Pre-flight renames + TargetMemory.ChangeEpoch

### 1. Rename `SquadCognitiveState._scalarPad` → `Flags`

File: `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`

Change:
```
private uint _scalarPad;
```
to:
```
/// <summary>Scalar flags. Bit 0 = mission-override active.</summary>
public uint Flags;
```

Keep the comment block above the Scalars region intact. The layout offset test in
`SquadCognitiveStateLayoutTests.cs` does not test individual scalar field names, so no
test update is needed.

### 2. Rename `SquadContact._pad` → `SourceMembersMask`

File: `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`

Inside `struct SquadContact`, change:
```
private ushort _pad;
```
to:
```
/// <summary>Bitmask of which squad members have reported this contact (bit i = member slot i).</summary>
public ushort SourceMembersMask;
```

Update the comment block above `SquadContact` to read:
```
// EntityId(8) + Position(12) + ThreatScore(4) + LastSeenTick(4) + Flags(2) + SourceMembersMask(2) = 32 bytes.
```

### 3. Repurpose `SquadContactPool._r0` as member-epoch checksum

File: `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`

Inside `struct SquadContactPool`, change:
```
private ulong _r0;
```
to:
```
/// <summary>XOR checksum of all subordinate TargetMemory.ChangeEpoch values
/// at the last merge tick. The merge system uses this to detect any structural
/// change in any member's TargetMemory without storing per-member values.</summary>
internal ulong _memberEpochChecksum;
```

Leave `_r1` through `_r8` unchanged.

Also update the layout comment above `SquadContactPool` to note:
```
// Count(4) + LastMergeTick(4) + _memberEpochChecksum(8) + _reserved(8*8=64) + Contacts(512) = 592 bytes.
```

### 4. Add `ChangeEpoch` to `TargetMemory`

File: `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`

Add a new field to `TargetMemory` AFTER the `Modalities` fixed array and BEFORE the
`AddOrUpdateTarget` mutation API:

```csharp
/// <summary>
/// Incremented each time a contact is added or evicted.
/// Does NOT increment on score updates to existing contacts.
/// Consumers (e.g. SquadPerceptionMergeSystem) XOR this value to detect
/// structural changes without iterating the table on every tick.
/// </summary>
public uint ChangeEpoch;
```

In `AddOrUpdateTarget`, bump `mem.ChangeEpoch++` in exactly two places:
1. When a new slot is allocated (`mem.Count++;`): add `mem.ChangeEpoch++;` immediately after.
2. When an eviction replaces the lowest-score slot (inside the `if (scoreBoost > lowestScore)`
   block): add `mem.ChangeEpoch++;` after writing the new slot data but before the sort.

Do NOT bump `ChangeEpoch` when updating an existing slot (the contact set did not change).

---

## Task P2-01: `SquadPerceptionMergeSystem`

### New file: `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadPerceptionMergeSystem.cs`

```
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.State;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Merges TargetMemory contacts from all squad subordinates into the commander's
    /// SquadCognitiveState.Contacts (SquadContactPool).
    /// <para>
    /// Runs on the Brain node. Call Run(...) once per world tick with the current tick
    /// and the desired merge interval. The method skips work when neither condition holds:
    ///   (a) currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks  (10 Hz cadence), OR
    ///   (b) XOR of all member TargetMemory.ChangeEpoch values changed since last merge
    ///       (event-driven forced re-merge on any structural perception change).
    /// </para>
    /// </summary>
    public static class SquadPerceptionMergeSystem { ... }
```

Full implementation contract:

#### `Run` signature

```csharp
public static void Run(
    EntityRepository repo,
    Entity commander,
    uint currentTick,
    uint mergeIntervalTicks = 6)
```

`mergeIntervalTicks = 6` matches 10 Hz at 60 ticks/second. The default is a hint —
tests may pass a different value.

#### Algorithm

1. Guard: `repo.HasComponent<UnitRoster>(commander)` AND `repo.HasComponent<Blackboard1024>(commander)`.
   If either is absent, return immediately.

2. Project state: `ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));`

3. Compute the XOR epoch checksum across all subordinates:
   ```csharp
   ulong checksum = 0;
   ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
   for (int m = 0; m < roster.Count; m++)
   {
       var member = Entity.Unpack(roster.SubordinateEntities[m]);
       if (!repo.HasComponent<TargetMemory>(member)) continue;
       ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(member);
       checksum ^= mem.ChangeEpoch;
   }
   ```

4. Decide whether to merge:
   ```csharp
   bool epochChanged  = checksum != state.Contacts._memberEpochChecksum;
   bool dwellElapsed  = currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks;
   if (!epochChanged && !dwellElapsed) return;
   ```

5. Build new contact pool: zero out a local `SquadContactPool` on the stack, then
   iterate all subordinates and merge each subordinate's TargetMemory into it.

   For each member `m` (0-based index) with TargetMemory:
   - For each valid contact slot `k` (0 .. mem.Count-1):
     - Call the internal `MergeContact` helper (see below).
     - `sourceBit = (ushort)(1 << m)` (bit m of SourceMembersMask).

6. Re-sort the pool contacts descending by ThreatScore using insertion sort (the pool has at most 16 entries).

7. Write back:
   ```csharp
   state.Contacts.LastMergeTick       = currentTick;
   state.Contacts._memberEpochChecksum = checksum;
   state.Contacts.Count               = localPool.Count;
   // copy Contacts array via span
   var dst = MemoryMarshal.CreateSpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);
   var src = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref localPool.Contacts), 16);
   src.Slice(0, localPool.Count).CopyTo(dst);
   ```

#### `MergeContact` internal helper

```csharp
private static void MergeContact(
    ref SquadContactPool pool,
    long entityId,
    float posX, float posY, float posZ,
    float threatScore,
    uint lastSeenTick,
    byte modalities,
    ushort sourceMemberBit)
```

Logic:
- Obtain a `Span<SquadContact>` over pool.Contacts via `MemoryMarshal.CreateSpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16)`.
- Scan existing entries:
  - If found (entityId match): take max threatScore and position, OR modalities, OR SourceMembersMask;
    update LastSeenTick if newer. Return.
- Not found and `pool.Count < 16`: append at `pool.Count++`.
- Not found and pool full: find the lowest-ThreatScore entry; if `threatScore > lowestScore`,
  replace it. Otherwise do nothing (new contact loses the eviction race).
- Do NOT sort here — the sort happens in `Run` after all contacts are merged.

#### Notes

- All writes go through the Span pattern (InlineArray defensive-copy rule).
- The local `SquadContactPool` is a `SquadContactPool localPool = default;` on the stack.
  `SquadContactPool` is 592 bytes — this is safe on a Brain-thread stack.
- The method is zero-allocation: no heap use whatsoever.

---

## Task P2-01 — Tests

### New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadPerceptionMergeSystemTests.cs`

Use `xunit`. Namespace: `Fdp.Toolkit.Squad.Tests.Systems`.

**Helper setup**: create a utility `CreateSquadWorld(int memberCount)` that:
- Creates one `EntityRepository`.
- Spawns a commander with `UnitRoster + Blackboard1024 + SquadStateMarker`.
- Spawns `memberCount` subordinates each with `TargetMemory + UnitSubordinate` pointing at the commander.
- Registers each subordinate in the commander's roster via `UnitRoster.Add(...)`.
- Returns `(repo, commander, members[])`.

**Test SC-P2-01-1: ThreeDistinctContacts_MergeToThree**

Setup: 3 members, each sees a distinct contact (entity ids 100, 200, 300) with scores 0.5, 0.3, 0.4.
`TargetMemory.AddOrUpdateTarget` is called on each member.
Call `SquadPerceptionMergeSystem.Run(repo, commander, tick: 10, mergeIntervalTicks: 1)`.

Assert: `state.Contacts.Count == 3`.
Assert: For the contact with entityId 100, `SourceMembersMask` has exactly bit 0 set.
Assert: `SourceMembersMask` for entityId 200 has exactly bit 1 set; for 300 exactly bit 2 set.

**Test SC-P2-01-2: TwoMembersSeeSameContact_MaxThreatAndBothBitsSet**

Setup: 3 members. Member 0 sees entity 100 with score 0.7 at position (1f, 2f, 3f).
Member 1 sees entity 100 with score 0.4 at position (4f, 5f, 6f). Member 2 sees nothing.
Set member 0's tick to 20 and member 1's tick to 30 (member 1 more recent).

Call `Run(repo, commander, tick: 10, mergeIntervalTicks: 1)`.

Assert: `state.Contacts.Count == 1`.
Assert: `contacts[0].ThreatScore == 0.7f` (max).
Assert: `contacts[0].SourceMembersMask` has bits 0 and 1 set (== 0b_0011).
Assert: `contacts[0].LastSeenTick == 30` (most recent).

**Test SC-P2-01-3: CadenceGate_SkipsRunWhenIntervalNotElapsed**

Setup: 2 members, member 0 sees entity 100. Call `Run(repo, commander, tick: 10, mergeIntervalTicks: 6)`.
Assert `state.Contacts.Count == 1`. (First run always proceeds since LastMergeTick == 0.)

Now call `Run(repo, commander, tick: 12, mergeIntervalTicks: 6)` without changing any TargetMemory.
(tick delta = 2 < 6, epoch unchanged.)
Assert `state.Contacts.LastMergeTick == 10` (not updated — skipped).

Call `Run(repo, commander, tick: 16, mergeIntervalTicks: 6)`.
(tick delta = 6 >= 6, should run.)
Assert `state.Contacts.LastMergeTick == 16`.

**Test SC-P2-01-4: EventDriven_ForcedRemergeOnEpochChange**

Setup: 2 members, both start with empty TargetMemory. First call:
`Run(repo, commander, tick: 10, mergeIntervalTicks: 100)`.
Assert `state.Contacts.Count == 0` and `state.Contacts.LastMergeTick == 10`.

Then add a contact to member 1 via `TargetMemory.AddOrUpdateTarget(...)` (this bumps ChangeEpoch).
Call `Run(repo, commander, tick: 11, mergeIntervalTicks: 100)`.
(Only 1 tick elapsed, far below 100 interval — but epoch changed.)

Assert `state.Contacts.Count == 1` and `state.Contacts.LastMergeTick == 11`.

**Test SC-P2-01-5: CapacityEviction_SeventeenthContactEvictsLowest**

Setup: 1 member. Fill it with 16 distinct contacts (entity ids 1..16) with scores 0.1, 0.2, ..., 1.6.
(Use `AddOrUpdateTarget` in a loop.)
Call `Run(repo, commander, tick: 1, mergeIntervalTicks: 1)`.
Assert `state.Contacts.Count == 16`.

Add a 17th contact to the member (entity id 100, score 0.05 — lower than all 16).
Call `Run(repo, commander, tick: 2, mergeIntervalTicks: 1)`.
Assert `state.Contacts.Count == 16` (pool is full; 17th was rejected — score too low to evict).

Change the 17th contact's score to 5.0 (higher than all 16) by calling `AddOrUpdateTarget` again
with a new entity id 101 and score 5.0. Call `Run(repo, commander, tick: 3, mergeIntervalTicks: 1)`.
Assert `state.Contacts.Count == 16` and the top contact has `ThreatScore >= 5.0f` (entity 101
evicted the previous lowest).

---

## Task P2-02: `SquadInputs.cs`

### New file: `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs`

Namespace: `Fdp.Toolkit.Utility`. Mirrors the shape of `StandardInputs.cs`.

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.State;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// FNV-1a-16 identifiers for squad-tier Utility AI input readers.
    /// </summary>
    public static class SquadInputIds
    {
        // FNV-1a-32("SquadKnowsContact") & 0xFFFF
        public const ushort SquadKnowsContact      = 0xXXXX; // compute at implementation time
        // FNV-1a-32("SquadContactThreatLevel") & 0xFFFF
        public const ushort SquadContactThreatLevel = 0xXXXX; // compute at implementation time
    }

    /// <summary>
    /// Squad-tier Utility AI input readers.
    /// Register all readers via <see cref="RegisterAll"/>.
    /// </summary>
    public static unsafe class SquadInputs
    {
        public static void RegisterAll(UtilityInputRegistry registry)
        {
            UtilityInputRegistry.Register<SquadKnowsContact>(registry);
            UtilityInputRegistry.Register<SquadContactThreatLevel>(registry);
        }

        [UtilityInput("SquadKnowsContact")]
        public static float SquadKnowsContact(in UtilityInputCtx ctx) { ... }

        [UtilityInput("SquadContactThreatLevel")]
        public static float SquadContactThreatLevel(in UtilityInputCtx ctx) { ... }
    }
}
```

Compute the FNV-1a-16 constants at implementation time using the same algorithm as
`StandardInputIds` (Fnv1a32 then mask to 16 bits). Verify they do not collide with any
existing entries in `StandardInputIds`.

#### `SquadKnowsContact` contract

`Context = Candidate` (a candidate contact entity).

1. If no `UnitSubordinate` on `ctx.Self`, return `0f`.
2. Read `sub.Commander`. If `== Entity.Null`, return `0f`.
3. If commander has no `Blackboard1024`, return `0f`.
4. Project: `ref var bb = ref Unsafe.AsRef(in ctx.Repo.GetComponentRO<Blackboard1024>(commander));`
   `ref readonly var state = ref SquadCognitiveState.Project(ref bb);` (reinterpret-cast; read-only use).
5. Get the candidate packed id: `long candidateId = ctx.Candidate.PackedValue;`
6. Walk `state.Contacts.Count` entries via a read-only span over `state.Contacts.Contacts`
   (same `MemoryMarshal.CreateReadOnlySpan / Unsafe.As` pattern as elsewhere in the codebase).
7. Return `1f` if any slot's `EntityId == candidateId`, else `0f`.

#### `SquadContactThreatLevel` contract

Same walkup as above (steps 1–4). Then:
5. Walk the contact pool; if the candidate is found, return `contacts[i].ThreatScore` (already in [0,∞];
   normalize by clamping to [0, 1] if needed — check whether other threat-score readers clamp;
   match that behavior). If not found, return `0f`.

Looking at `StandardInputs.ContactThreatLevel` for the normalization precedent: it returns
`Math.Clamp(score / PerceptionConstants.MaxThreatScore, 0f, 1f)`. Apply the same normalization
using `PerceptionConstants.MaxThreatScore` for consistency.

---

## Task P2-02 — Tests

### New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsTests.cs`

Namespace: `Fdp.Toolkit.Squad.Tests.Inputs`.

Setup helper: reuse or inline the `CreateSquadWorld` helper from P2-01 tests (or copy it into a
shared test utility class `SquadTestHelper`).

After creating the world, populate `state.Contacts` by calling
`SquadPerceptionMergeSystem.Run(...)` with a single-merge interval (so the pool is fresh).

**Test SC-P2-02-1: SquadKnowsContact_ReturnOneWhenCommanderPoolHasContact**

Setup: 3 members. Member 0 sees entity 100.
Run merge so pool has entity 100.
Member 1 does NOT have entity 100 in its own TargetMemory.

Build a `UtilityInputCtx` with `Self = member1`, `Candidate = entity(100)`.
Assert `SquadInputs.SquadKnowsContact(ctx) == 1f`.

**Test SC-P2-02-2: SquadKnowsContact_ReturnZeroWhenContactNotInPool**

Same world. Build ctx with `Candidate = entity(999)` (not in pool).
Assert `SquadInputs.SquadKnowsContact(ctx) == 0f`.

**Test SC-P2-02-3: SquadKnowsContact_ReturnZeroForNonSquadMember**

Create a standalone entity with no `UnitSubordinate` component.
Build ctx with `Self = standaloneEntity`, `Candidate = entity(100)`.
Assert `SquadInputs.SquadKnowsContact(ctx) == 0f`.

**Test SC-P2-02-4: SquadContactThreatLevel_MatchesPoolScore**

Setup: member 0 sees entity 100 with score 0.5f (use `AddOrUpdateTarget(score 0.5f)`).
Run merge. Build ctx with `Candidate = entity(100)`.
Assert `SquadInputs.SquadContactThreatLevel(ctx)` is within `1e-5f` of
`Math.Clamp(0.5f / PerceptionConstants.MaxThreatScore, 0f, 1f)`.

---

## Code location summary

| File | Action |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` | Rename `_scalarPad`→`Flags`, `SquadContact._pad`→`SourceMembersMask`, `_r0`→`_memberEpochChecksum` |
| `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` | Add `ChangeEpoch` field + bump in `AddOrUpdateTarget` |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadPerceptionMergeSystem.cs` | NEW — merge system |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` | NEW — two input readers + IDs |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadPerceptionMergeSystemTests.cs` | NEW — 5 tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsTests.cs` | NEW — 4 tests |

---

## Build and test instructions

1. `dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` — must be warning-free.
2. `dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — must be warning-free.
3. Run new tests only:
   ```
   dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj \
     --filter "FullyQualifiedName~SquadPerceptionMergeSystem|FullyQualifiedName~SquadInputs"
   ```
   All 9 new tests must pass.
4. Run regression guard for existing squad + ThreatMatrix tests:
   ```
   dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj \
     --filter "FullyQualifiedName~Squad|FullyQualifiedName~ThreatMatrix|FullyQualifiedName~StarterPack"
   ```
   All previously passing tests must still pass.

---

## Success Conditions

| ID | Requirement |
|----|-------------|
| SC-P2-00-1 | `SquadCognitiveState.Flags` is `public uint`; layout test suite passes |
| SC-P2-00-2 | `SquadContact.SourceMembersMask` is `public ushort`; layout test suite passes |
| SC-P2-00-3 | `TargetMemory.ChangeEpoch` increments on new-slot-allocation and eviction; does NOT increment on score update |
| SC-P2-01-1 | Three distinct contacts merge to Count==3 with correct 1-bit SourceMembersMask per entry |
| SC-P2-01-2 | Two members seeing same contact: Count==1, ThreatScore==max, SourceMembersMask has both bits, LastSeenTick==most-recent |
| SC-P2-01-3 | System skips merge within cadence interval when epoch unchanged; runs at interval boundary |
| SC-P2-01-4 | Epoch change forces re-merge before cadence interval elapses |
| SC-P2-01-5 | 17th contact is evicted correctly (rejected if lowest-scorer, replaces if highest) |
| SC-P2-02-1 | `SquadKnowsContact` returns 1f for a contact present in the commander's pool |
| SC-P2-02-2 | `SquadKnowsContact` returns 0f for a contact absent from the pool |
| SC-P2-02-3 | `SquadKnowsContact` returns 0f for an entity with no UnitSubordinate component |
| SC-P2-02-4 | `SquadContactThreatLevel` returns the normalized pool threat score |

Total new tests: **9** (5 merge system + 4 inputs).
