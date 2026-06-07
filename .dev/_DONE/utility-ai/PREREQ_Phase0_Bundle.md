# Phase-0 Prerequisite Bundle — Utility AI

> **Status:** Active. Supersedes `PREREQ_WeaponState_MaxAmmo_Cache.md` (kept for history).
> **Owner:** Phase-0 lead (most items touch engine surfaces outside the Utility layer).
> **Blocks:** All Utility AI work (Phase 1 onward) per the v1.2 design decisions.
> **Audience:** Implementer + reviewer.

This bundle replaces the original single-field `WeaponState.MaxAmmo` prerequisite. The 2026-05-28 design review surfaced six related codebase changes that the rest of the Utility AI design assumes; bundling them as Phase 0 keeps Phase-1 utility-layer code from inventing APIs it cannot rely on.

The six items are independent enough to ship in a single batch, owned together because they all expand existing engine surfaces in small, low-risk ways.

---

## P0.1 — `WeaponState.MaxAmmo` cache

### Problem

[`FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs:11-23`](FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs#L11-L23) defines `WeaponState`:

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.WeaponState)]
public struct WeaponState
{
    public int Ammo;                       // current rounds
    public float CooldownSecondsRemaining; // time until next shot
    public float MuzzleVelocity;           // m/s, set at init
}
```

Maximum capacity is **not** retained on the component. It exists upstream as `WeaponMountDto.InitialAmmunition` and is discarded after spawn. The Utility AI `AmmoFraction` reader has nothing to divide by.

### Fix

Add one field to `WeaponState`:

```csharp
public struct WeaponState
{
    public int   Ammo;
    public float CooldownSecondsRemaining;
    public float MuzzleVelocity;
    public int   MaxAmmo;                 // NEW — cached from WeaponMountDto.InitialAmmunition at spawn
}
```

### Spawn-site update

[`FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs:71-78`](FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs#L71-L78) currently writes:

```csharp
repo.AddComponent(entity, new WeaponState
{
    Ammo           = primary.InitialAmmunition,
    MuzzleVelocity = primary.MuzzleVelocity > 0f
        ? primary.MuzzleVelocity
        : DefaultMuzzleVelocity
});
```

Becomes:

```csharp
repo.AddComponent(entity, new WeaponState
{
    Ammo           = primary.InitialAmmunition,
    MaxAmmo        = primary.InitialAmmunition,   // NEW
    MuzzleVelocity = primary.MuzzleVelocity > 0f
        ? primary.MuzzleVelocity
        : DefaultMuzzleVelocity
});
```

Note: this spawn site (and the multi-mount expansion in P0.2 below) is the **only** code path that initializes `MaxAmmo`. `MaxAmmo` is never mutated after spawn; firing decrements `Ammo` only.

### Rejected alternative

Pass `maxAmmo` as a packed `InputParams` value to the reader. Rejected: the magic number drifts from the actual mount, reintroducing exactly the hardcoded-assumption coupling live-state reads exist to avoid.

### Safety

The `AmmoFraction` reader guards with `ws.MaxAmmo > 0`. A legacy `WeaponState` with `MaxAmmo == 0` reads as fully-gated (no ammo) rather than dividing by zero — forward-safe.

### Success conditions

- **P0.1-SC1:** `sizeof(WeaponState)` after the change is still natural-aligned (the new `int` adds 4 bytes; no padding regressions).
- **P0.1-SC2:** Spawn a weapon from a TKB mount with `InitialAmmunition = N`; assert `WeaponState.MaxAmmo == N`.
- **P0.1-SC3:** Fire until `Ammo == 0`; assert `MaxAmmo` is unchanged.
- **P0.1-SC4:** A `WeaponState` constructed without an explicit `MaxAmmo` (e.g. `default(WeaponState)`) reads `MaxAmmo == 0` and does not throw when divided by; downstream readers gate to zero utility.

---

## P0.2 — Multi-mount weapon entities

### Problem

The current `CombatTkbTranslator` only adds `WeaponState` for `suite.Mounts[0]` (the primary mount). Agents with multiple weapons therefore have only **one** `WeaponState` regardless of how many mounts the TKB defines, and there is no way for an AI scorer to enumerate the agent's other weapons. The Utility AI `WeaponSelectionDecision` (architecture §1.2, §6.4, starter pack §2) presupposes ranking multiple weapons; without per-mount entities the scorer has no candidate list.

### Fix

Promote each mount in `suite.Mounts` to a child entity carrying its own `WeaponState`, with parent linkage via the existing `PartMetadata` component pattern that already drives the EQS per-sensor dispatch (see [`PartMetadata.cs`](FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs)).

```csharp
public struct WeaponMountInfo
{
    public int     MountIndex;       // index into WeaponSuiteDto.Mounts
    public ulong   WeaponGuid;       // TKB weapon GUID (from mount.WeaponGuid)
    public float   EffectiveRange;   // copied from WeaponCapabilitiesDto.EffectiveRange (if present)
}
```

Spawn flow becomes:

```csharp
for (int i = 0; i < suite.Mounts.Count; i++)
{
    var mount = suite.Mounts[i];

    // Primary mount: keep WeaponState on the owner entity (back-compat with existing
    // weapon-actuator code that reads WeaponState off the owner).
    if (i == 0)
    {
        repo.AddComponent(entity, new WeaponState { ... });
        continue;
    }

    // Additional mounts: child entity with its own WeaponState + PartMetadata back-link.
    var mountEntity = repo.CreateEntity();
    repo.AddComponent(mountEntity, new WeaponState { Ammo = mount.InitialAmmunition, MaxAmmo = mount.InitialAmmunition, ... });
    repo.AddComponent(mountEntity, new WeaponMountInfo { MountIndex = i, WeaponGuid = mount.WeaponGuid, EffectiveRange = caps?.EffectiveRange ?? 0f });
    repo.AddComponent(mountEntity, new PartMetadata { ParentEntity = entity, InstanceId = i, DescriptorOrdinal = 0 });
}
```

### Rationale for keeping primary on the owner

Existing combat code (`AimAndFireExecutor`, weapon-channel actuator, etc.) reads `WeaponState` directly off the owner entity. Forcing the primary mount onto a child entity would touch every actuator and is out of scope for Phase 0. The Utility scorer treats "the owner's own `WeaponState`" as candidate-index `0` and child-mounts as candidates `1..N-1`.

### Enumeration helper

`WeaponMountInfo` is the marker that lets the scorer find mount children by walking `PartMetadata.ParentEntity == self`. A small read-only helper in `Fdp.Toolkit.Combat`:

```csharp
public static class WeaponMountQuery
{
    public static int EnumerateMounts(EntityRepository repo, Entity owner, Span<Entity> dest)
    {
        int count = 0;
        // include owner as candidate 0
        if (repo.HasComponent<WeaponState>(owner)) dest[count++] = owner;
        // include children carrying WeaponMountInfo
        // (concrete query API depends on EntityRepository surface; favor a tagged enumeration
        //  to keep allocations zero)
        return count;
    }
}
```

The exact enumeration API is to be aligned with `EntityRepository`'s existing query helpers at implementation time. The scorer must not allocate on the hot path.

### Success conditions

- **P0.2-SC1:** A TKB definition with `Mounts.Count == 3` produces, after spawn: one `WeaponState` on the owner entity and two child entities each with `WeaponState` + `WeaponMountInfo` + `PartMetadata`. Total `WeaponState` components = 3.
- **P0.2-SC2:** `WeaponMountQuery.EnumerateMounts(repo, owner, ...)` returns exactly 3 entities in stable index order; index 0 is the owner.
- **P0.2-SC3:** A TKB definition with `Mounts.Count == 1` produces the same outcome as before this change (one `WeaponState` on owner, no child mounts) — back-compat asserted.
- **P0.2-SC4:** Modifying `WeaponState.Ammo` on the owner does not affect the children, and vice versa (per-mount ammo accounting).

---

## P0.3 — `PerceptionConstants.MaxTrackedTargets` raised to 16

### Problem

[`FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs:11`](FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs#L11):

```csharp
public const int MaxTrackedTargets = 4;
```

The architecture §8.1 cap-invariant assumes the perception cap is greater than or equal to the Utility Top-N=16 (so threat ranking can be non-truncating over the perceived contact list). A perception cap of 4 makes the invariant trivially hold in the wrong direction — threat ranking sees at most 4 contacts ever, regardless of what's out there. The squad-coordination scorer evaluates (member × target), so with `UnitRoster.Capacity = 16` and only 4 targets, the squad routinely under-uses its members.

### Fix

```csharp
public const int MaxTrackedTargets = 16;
```

This is a const used to size `fixed` arrays in `TargetMemory` and `SensorContactList`. Raising it from 4 to 16 grows those structs:

- `TargetMemory`: from `4 + 4×(8 + 4 + 4 + 4 + 4 + 1) = 104 bytes` to `4 + 16×25 = 404 bytes`.
- `SensorContactList`: from `4 + 4×(8 + 4 + 1) = 56 bytes` to `4 + 16×13 = 212 bytes`.

Both are still well under any per-entity component budget. No `[InlineArray(N)]` literals are affected (the existing arrays are `fixed` and parameterized by the const).

### Architecture §8.1 invariant becomes

```csharp
// Threat ranking is non-truncating: perception cap matches the Utility Top-N cap.
// If MaxTrackedTargets ever exceeds TopN, the lowest-threat tail is silently dropped.
Debug.Assert(PerceptionConstants.MaxTrackedTargets <= UtilityConstants.TopN,
    $"Perception tracks more contacts ({PerceptionConstants.MaxTrackedTargets}) than the " +
    $"Utility scorer ranks ({UtilityConstants.TopN}). Raise UtilityConstants.TopN " +
    "or accept silent truncation of the lowest-threat contacts.");
```

(The old tautological `a <= b || b <= a` from v1.1 §8.1 is removed.)

### Success conditions

- **P0.3-SC1:** `PerceptionConstants.MaxTrackedTargets == 16`.
- **P0.3-SC2:** The existing perception test suite (`FDP/Toolkits/Fdp.Toolkits.Tests/Perception/`) passes unchanged after the raise. Any test that asserted "table fills at 4 entries" must be reviewed; if it asserted overflow behavior, update to 16.
- **P0.3-SC3:** Spawn 16 contacts, all visible to a single perceiver; `TargetMemory.Count == 16` and `ThreatScores` is sorted descending.

---

## P0.4 — `UnitRoster.Add` / `IndexOf` helpers

### Problem

[`FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs:26-48`](FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs#L26-L48) is a raw `fixed long[16]` + `fixed ushort[16]` struct with no API. The squad-assignment system writes per-member assignments by index, which requires both (a) inserting a subordinate into the roster and (b) looking up "which slot does this member occupy" so the assignment-state struct can be indexed in parallel. Both are zero-overhead helpers but they don't exist.

### Fix

Add two static helpers on `UnitRoster` (unmanaged-safe, zero-alloc):

```csharp
public unsafe struct UnitRoster
{
    // ... existing fields ...

    /// <summary>
    /// Append a subordinate to the roster. Returns the assigned slot index, or -1 if full.
    /// </summary>
    public static int Add(ref UnitRoster roster, long packedEntity, ushort designation = 0)
    {
        if (roster.Count >= Capacity) return -1;
        int slot = roster.Count++;
        roster.SubordinateEntities[slot] = packedEntity;
        roster.TacticalDesignations[slot] = designation;
        return slot;
    }

    /// <summary>
    /// Find the slot index of a subordinate. Returns -1 if not present.
    /// </summary>
    public static int IndexOf(ref UnitRoster roster, long packedEntity)
    {
        for (int i = 0; i < roster.Count; i++)
            if (roster.SubordinateEntities[i] == packedEntity) return i;
        return -1;
    }
}
```

Both methods take `ref` because the struct holds `fixed` arrays which only allow pointer-style access inside an `unsafe` context on a ref.

### Success conditions

- **P0.4-SC1:** `Add` returns sequential indices 0..15 for 16 subordinates; the 17th call returns -1 and leaves the roster unchanged.
- **P0.4-SC2:** `IndexOf` returns the correct slot for present entities and -1 for absent.
- **P0.4-SC3:** No managed allocations occur in either method (assert by running 10⁶ Add/IndexOf calls under a GC-allocation tracker).

---

## P0.5 — `Blackboard1024.Project<T>` helper

### Problem

The architecture §10.1 specifies projecting `ThreatMatrixAssignmentState` onto a commander's `Blackboard1024` via `Unsafe.As`. The existing code pattern at [`HillAttackCommanderNodes.cs:48`](Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs#L48):

```csharp
ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);
```

…works but is repeated verbatim everywhere a projection is needed. The starter pack and the assignment system would each repeat the `Unsafe.As<,>` chain. A one-line static wrapper removes the boilerplate and gives the projection a documented entry point.

### Fix

Add to [`Blackboard1024.cs`](FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs):

```csharp
public unsafe struct Blackboard1024
{
    public const int ByteSize = 1024;
    public fixed byte Memory[ByteSize];

    /// <summary>
    /// Project the 1024-byte block as a typed mutable struct.
    /// T must be unmanaged and fit in 1024 bytes (asserted at use site, not here).
    /// Convention: each subsystem owns a disjoint byte range; do not share offsets without
    /// an explicit layout document.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T Project<T>(ref Blackboard1024 bb) where T : unmanaged
        => ref Unsafe.As<Blackboard1024, T>(ref bb);
}
```

The unmanaged constraint and `Unsafe.As` semantics give zero overhead and zero copies. Sizing assertions (e.g. `sizeof(ThreatMatrixAssignmentState) <= Blackboard1024.ByteSize`) live at the call site, not here, because `Project<T>` is generic.

### Existing usages keep working

Callers that already use the raw `Unsafe.As<Blackboard1024, ...>` form remain valid; the helper is purely additive.

### Success conditions

- **P0.5-SC1:** `Project<HillAttackMutableState>` returns a `ref` whose mutations are visible via the underlying `Blackboard1024.Memory` bytes (assert by writing through the projection and reading back via `fixed byte*`).
- **P0.5-SC2:** Calling `Project<T>` does not allocate (assert with allocation tracker).
- **P0.5-SC3:** Inlining is honored at JIT time (assert via release-build assembly inspection or, more practically, a microbenchmark that shows no method-call overhead vs. raw `Unsafe.As`).

---

## P0.6 — `UtilityTestWorld` helper

### Problem

The starter pack v1.1 references `TestRepository.CreateBrainOnly()` as if it existed. It does not. The existing test pattern (e.g. [`AimAndFireExecutorTests.cs:31`](FDP/Toolkits/Fdp.Toolkits.Tests/Combat/AimAndFireExecutorTests.cs#L31)) just does `_world = new EntityRepository();` directly. The Utility AI tests want a small, reusable Brain-only world setup that registers the AI-relevant component types and gives helpers for seeding agents, contacts, EQS sensors, and squads.

### Fix

Create a test scaffolding class in a new test-shared assembly (or `Hrot.AI.Tests` if a per-feature test project is preferred — implementation decides):

```csharp
internal sealed class UtilityTestWorld : IDisposable
{
    public readonly EntityRepository Repo;
    public uint Tick;

    public UtilityTestWorld()
    {
        Repo = new EntityRepository();
        // Component-type registration: every component the Utility readers touch
        // must be registered before AddComponent calls. Mirrors the explicit
        // registration pattern used in toolkit tests; no special factory needed.
        Repo.RegisterComponent<Health>();
        Repo.RegisterComponent<WeaponState>();
        Repo.RegisterComponent<WeaponMountInfo>();        // P0.2
        Repo.RegisterComponent<TargetMemory>();
        Repo.RegisterComponent<SensorContactList>();
        Repo.RegisterComponent<EqsSensor>();
        Repo.RegisterComponent<EqsCognitiveBuffer>();
        Repo.RegisterComponent<PartMetadata>();
        Repo.RegisterComponent<UnitRoster>();
        Repo.RegisterComponent<UnitSubordinate>();
        Repo.RegisterComponent<Blackboard1024>();
        Repo.RegisterComponent<Fdp.Toolkit.Geographic.Components.Position>();
        // Utility-layer components (Phase 1):
        Repo.RegisterComponent<UtilityResultBuffer>();
        Repo.RegisterComponent<UtilityDebugFlags>();
        Repo.RegisterComponent<UtilityTraceWorkingMemory1024>();
    }

    public Entity SpawnAgent(float health01, float ammo01, int initialAmmunition = 30) { ... }
    public Entity SpawnWeaponMount(Entity owner, int mountIndex, float effRange,
                                   float ammo01, int initialAmmunition) { ... }
    public void   SeedContact(Entity self, Entity contact, float distanceM,
                              float threatBoost, float contactHealth01, bool hasLos) { ... }
    public Entity SpawnEqsSensor(Entity owner, uint blueprintId, float topScore, int count) { ... }
    public Entity SpawnLeader() { ... }
    public Entity SpawnSquadMember(Entity leader, float health01, float ammo01) { ... }

    public void Dispose() => Repo.Dispose();
}
```

The exact `RegisterComponent` API depends on `EntityRepository`'s current surface (the helper just lists what the real API offers — check at implementation time).

### Why one shared helper

Each Utility AI integration test would otherwise re-register components and re-write seeding code; one shared helper keeps the tests focused on the behavior under test and prevents drift between tests (a single source of truth for "what seeded state looks like").

### Success conditions

- **P0.6-SC1:** `new UtilityTestWorld()` succeeds without exceptions on a clean process; `Dispose()` releases without leaks.
- **P0.6-SC2:** `SpawnAgent(1f, 1f)` returns an entity carrying `Health`, `WeaponState` (with `MaxAmmo`), `TargetMemory`, `EqsCognitiveBuffer`, `UtilityResultBuffer`, and `UtilityDebugFlags`.
- **P0.6-SC3:** `SpawnWeaponMount` creates a child entity with `WeaponState` + `WeaponMountInfo` + `PartMetadata.ParentEntity == owner`.
- **P0.6-SC4:** `SeedContact` calls real `TargetMemory.AddOrUpdateTarget` (no test-only shortcut for the public path); the contact appears in slot 0 with a `ThreatScore` matching the boost.
- **P0.6-SC5:** `SpawnEqsSensor` creates a child entity with `EqsSensor` (carrying the supplied `BlueprintId`) + `EqsCognitiveBuffer` (seeded via `GetSpanRW()`) + `PartMetadata.ParentEntity == owner`. The seeded Top-K is visible through the child's `EqsCognitiveBuffer`.

---

## Phase-0 exit gate

All six items must be in `main` and tested green before Phase 1 begins. Concretely:

1. **P0.1** `WeaponState.MaxAmmo` populated at spawn; legacy default safe.
2. **P0.2** Multi-mount weapons enumerable; ammo accounting per-mount.
3. **P0.3** Perception cap = 16; threat ranking provably non-truncating.
4. **P0.4** `UnitRoster.Add`/`IndexOf` available and zero-alloc.
5. **P0.5** `Blackboard1024.Project<T>` available and inlined.
6. **P0.6** `UtilityTestWorld` helper available to Phase-1 tests.

A single passing integration test in `Hrot.AI.Tests` (or wherever the helper lands) that exercises all six in a Brain-only world is the gate; the test instantiates `UtilityTestWorld`, spawns a multi-mount agent, populates a leader's `Blackboard1024` via `Project<T>`, adds 16 contacts to its `TargetMemory`, and reads back state through each new API.

---

## Secondary beneficiaries (outside Utility AI scope)

- **Runtime tuning overlay** — perception/channel overlay can show `Ammo / MaxAmmo` as a bar without re-deriving max (P0.1).
- **Any ammo HUD or telemetry** — same reason (P0.1).
- **Squad UI** — leader→member→target lines can read assignment directly through `Project<T>` (P0.5).
- **Replay browser** — multi-mount loadouts are now first-class entities, indexable per-mount (P0.2).

---

*End of Phase-0 prerequisite bundle. Resolved on 2026-05-28 from the original `PREREQ_WeaponState_MaxAmmo_Cache.md` plus the v236 codebase-review discrepancies.*
