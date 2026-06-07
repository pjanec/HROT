# BATCH-01: Utility AI — Phase 0 Prerequisites

**Batch Number:** BATCH-01  
**Tasks:** TASK-UAI-P0-01, TASK-UAI-P0-02, TASK-UAI-P0-03, TASK-UAI-P0-04, TASK-UAI-P0-05, TASK-UAI-P0-06, TASK-UAI-P0-07  
**Phase:** Phase 0 — Prerequisite bundle  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH (gates all Phase-1 work)  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — how to work with batches
2. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — see Phase 0 section (TASK-UAI-P0-01 through TASK-UAI-P0-07) for per-task success conditions
3. **Prerequisite Bundle Design:** `.dev/utility-ai/PREREQ_Phase0_Bundle.md` — full rationale for each item, spawn-site references, struct snippets
4. **Architecture (v1.2):** `.dev/utility-ai/Utility_AI_Design_v1_1.md` — §6.7 (prereq pointer), §8.1 (cap invariant), §10.1 (Blackboard projection)
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Source Code Locations

- **WeaponState:** `FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs` (lines 11–23)
- **WeaponState spawn site:** `FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs` (lines 67–79)
- **WeaponCapabilitiesDto:** `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/WeaponCapabilitiesDto.cs`
- **WeaponMountDto / WeaponSuiteDto:** `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/WeaponSuiteDto.cs`
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **PerceptionConstants:** `FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs`
- **TargetMemory / SensorContactList:** `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`
- **UnitRoster:** `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs`
- **Blackboard1024:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`
- **PartMetadata (precedent for child-entity pattern):** `FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs`

### Test Projects

- **P0.1, P0.2 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/` (existing project, `Fdp.Toolkits.Tests.csproj`)
- **P0.3 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Perception/PerceptionComponentTests.cs` (UPDATE existing tests; existing file uses `Assert.Equal(4, PerceptionConstants.MaxTrackedTargets)` — update to 16)
- **P0.4 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/` — new file `CommandHierarchy/UnitRosterTests.cs`
- **P0.5 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/` — new file `Behavior/Blackboard1024Tests.cs`
- **P0.6, P0.7 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/` — new folder `Utility/`, new files `UtilityTestWorldTests.cs` and `Phase0IntegrationTests.cs`

### Build and Test Commands

```bat
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build
```

Run tests frequently; all must pass before moving to the next task.

### Report Submission

When done, submit your report to: `.dev/utility-ai/reports/BATCH-01-REPORT.md`  
If you have questions: `.dev/utility-ai/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 0 is the prerequisite bundle — six codebase changes that Utility AI Phase-1 runtime code depends on. None involve the Utility AI scoring logic itself; they're small, targeted additions to existing infrastructure. A seventh task (P0.07) is a gate integration test that validates all six together.

**Do not start Phase-1 code. This batch is solely the prerequisite bundle.**

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests.**

1. **P0.01:** Implement → Write tests → **ALL tests pass** ✅
2. **P0.02:** Implement → Write tests → **ALL tests pass** ✅
3. **P0.03:** Implement → Update/write tests → **ALL tests pass** ✅
4. **P0.04:** Implement → Write tests → **ALL tests pass** ✅
5. **P0.05:** Implement → Write tests → **ALL tests pass** ✅
6. **P0.06:** Implement → Write tests → **ALL tests pass** ✅
7. **P0.07:** Implement gate test → **ALL tests pass** ✅

**DO NOT** move to the next task until current task's tests are green.  
**DO NOT** stop and ask about obvious next steps. Work autonomously until all tests pass, then write the report.

---

## ✅ Tasks

### Task 1: `WeaponState.MaxAmmo` cache (TASK-UAI-P0-01)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs` — ADD field
- `FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs` — UPDATE spawn
- `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponStateTests.cs` — NEW test file (or extend existing)

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-01` for success conditions.
**Design:** `PREREQ_Phase0_Bundle.md` §P0.1.

**Requirements:**

Add `public int MaxAmmo;` to `WeaponState` **after** the existing three fields (append to avoid offset churn):

```csharp
public struct WeaponState
{
    public int   Ammo;
    public float CooldownSecondsRemaining;
    public float MuzzleVelocity;
    public int   MaxAmmo;  // NEW — cached from WeaponMountDto.InitialAmmunition at spawn
}
```

In `CombatTkbTranslator.Inject`, add `MaxAmmo = primary.InitialAmmunition` alongside `Ammo`:

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

`MaxAmmo` is **only written at spawn, never mutated by firing**. The `Ammo` field decrements; `MaxAmmo` stays constant.

**Tests Required (see SC-P0-01-1 through SC-P0-01-4 in TASK-DETAIL.md):**
- Assert `sizeof(WeaponState) == 16` (4 ints/floats × 4 bytes each)
- Test that a spawned `WeaponState` has `MaxAmmo == initialAmmunition`
- Test that firing (decrementing `Ammo`) leaves `MaxAmmo` unchanged
- Test that `default(WeaponState).MaxAmmo == 0` (safe default)

---

### Task 2: Multi-mount weapon entities (TASK-UAI-P0-02)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs` — ADD `WeaponMountInfo` struct
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — ADD `WeaponMountInfo` constant (use ID **216**)
- `FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs` — UPDATE to spawn child mount entities
- `FDP/Toolkits/Fdp.Toolkits/Combat/WeaponMountQuery.cs` — NEW static helper
- `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponMountTests.cs` — NEW test file

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-02`.  
**Design:** `PREREQ_Phase0_Bundle.md` §P0.2.

**Component definition — add to `CombatComponents.cs`:**

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.WeaponMountInfo)]
public struct WeaponMountInfo
{
    /// <summary>Index into WeaponSuiteDto.Mounts (0 = primary, already on owner entity).</summary>
    public int   MountIndex;
    /// <summary>TKB weapon GUID from mount.WeaponGuid.</summary>
    public ulong WeaponGuid;
    /// <summary>Effective range in metres, from WeaponCapabilitiesDto if present; else 0.</summary>
    public float EffectiveRange;
}
```

**GlobalComponentIds.cs — add at the bottom before the closing brace:**

```csharp
/// <summary><c>WeaponMountInfo</c> — identifies a weapon mount child entity; carries mount index, weapon GUID, and effective range.</summary>
public const int WeaponMountInfo = 216;
```

**Spawn pattern — in `CombatTkbTranslator.Inject`, update the weapon-state block:**

```csharp
var suite = template.GetDescriptor<WeaponSuiteDto>();
if (suite != null && suite.Mounts.Count > 0)
{
    var primary = suite.Mounts[0];
    // Primary mount: WeaponState stays on the owner entity (back-compat with actuators).
    if (repo.IsComponentTypeRegistered<WeaponState>() && !repo.HasComponent<WeaponState>(entity))
        repo.AddComponent(entity, new WeaponState
        {
            Ammo           = primary.InitialAmmunition,
            MaxAmmo        = primary.InitialAmmunition,
            MuzzleVelocity = primary.MuzzleVelocity > 0f ? primary.MuzzleVelocity : DefaultMuzzleVelocity
        });

    // Additional mounts (index 1+): each gets a child entity.
    if (repo.IsComponentTypeRegistered<WeaponMountInfo>() && repo.IsComponentTypeRegistered<PartMetadata>())
    {
        var caps = template.GetDescriptor<WeaponCapabilitiesDto>(); // may be null
        for (int i = 1; i < suite.Mounts.Count; i++)
        {
            var mount = suite.Mounts[i];
            var child = repo.CreateEntity();
            repo.AddComponent(child, new WeaponState
            {
                Ammo           = mount.InitialAmmunition,
                MaxAmmo        = mount.InitialAmmunition,
                MuzzleVelocity = mount.MuzzleVelocity > 0f ? mount.MuzzleVelocity : DefaultMuzzleVelocity
            });
            repo.AddComponent(child, new WeaponMountInfo
            {
                MountIndex     = i,
                WeaponGuid     = mount.WeaponGuid,
                EffectiveRange = caps?.EffectiveRange ?? 0f,
            });
            repo.AddComponent(child, new PartMetadata
            {
                ParentEntity      = entity,
                InstanceId        = i,
                DescriptorOrdinal = 0,
            });
        }
    }
}
```

Note: `WeaponCapabilitiesDto.EffectiveRange` is per-suite, not per-mount. All non-primary mounts get the same `EffectiveRange` from the suite's capabilities descriptor if present.

**`WeaponMountQuery` helper — new file `FDP/Toolkits/Fdp.Toolkits/Combat/WeaponMountQuery.cs`:**

```csharp
namespace Fdp.Toolkit.Combat
{
    public static class WeaponMountQuery
    {
        /// <summary>
        /// Collects all weapon mount entities for <paramref name="owner"/> into <paramref name="dest"/>.
        /// Index 0 is always the owner entity itself (primary mount) if it carries WeaponState.
        /// Subsequent slots are child entities carrying WeaponMountInfo in MountIndex order.
        /// Returns the count written (min(dest.Length, total mounts)).
        /// Zero-alloc: iterates PartMetadata-bearing entities.
        /// </summary>
        public static int EnumerateMounts(EntityRepository repo, Entity owner, Span<Entity> dest)
        {
            if (dest.IsEmpty) return 0;
            int count = 0;
            // Candidate 0: the owner itself (primary mount).
            if (repo.HasComponent<WeaponState>(owner))
            {
                dest[count++] = owner;
                if (count >= dest.Length) return count;
            }
            // Candidates 1+: children whose PartMetadata.ParentEntity == owner.
            // Use a fixed-size stack buffer to collect then sort by MountIndex.
            Span<(int idx, Entity e)> scratch = stackalloc (int, Entity)[16];
            int scratchCount = 0;
            foreach (var e in repo.Query<WeaponMountInfo>())
            {
                ref readonly var pm = ref repo.GetComponentRO<PartMetadata>(e);
                if (!pm.ParentEntity.Equals(owner)) continue;
                ref readonly var mi = ref repo.GetComponentRO<WeaponMountInfo>(e);
                if (scratchCount < scratch.Length)
                    scratch[scratchCount++] = (mi.MountIndex, e);
            }
            // Sort by MountIndex (insertion sort — typically ≤4 mounts).
            for (int i = 1; i < scratchCount; i++)
            {
                var key = scratch[i];
                int j = i - 1;
                while (j >= 0 && scratch[j].idx > key.idx) { scratch[j + 1] = scratch[j]; j--; }
                scratch[j + 1] = key;
            }
            for (int i = 0; i < scratchCount && count < dest.Length; i++)
                dest[count++] = scratch[i].e;
            return count;
        }
    }
}
```

Note: `repo.Query<WeaponMountInfo>()` — use whatever query API `EntityRepository` exposes to iterate all entities carrying a given component type. Check the existing codebase patterns (e.g. `EqsResultUpdateSystem.cs` uses `view.Query().With<EqsSensor>().Build()`). Adapt if needed; the zero-alloc enumeration requirement applies.

**Tests (see SC-P0-02-1 through SC-P0-02-5):**
- 3-mount TKB definition → 3 WeaponState components (1 on owner, 2 children)
- `EnumerateMounts` returns count=3; dest[0]=owner; children in MountIndex order
- 1-mount definition → 1 WeaponState on owner, no children (back-compat)
- Mutating one mount's `Ammo` doesn't affect others
- `WeaponMountInfo.EffectiveRange` matches `WeaponCapabilitiesDto.EffectiveRange` when present; 0 when absent

**Important:** `WeaponMountInfo` has `[DataPolicy(DataPolicy.NoSave)]` is NOT applied — mount configuration should persist with scenarios. Check how similar components are declared and follow the same pattern.

---

### Task 3: Raise `MaxTrackedTargets` to 16 (TASK-UAI-P0-03)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs` — change `4` to `16`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Perception/PerceptionComponentTests.cs` — UPDATE existing tests

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-03`.  
**Design:** `PREREQ_Phase0_Bundle.md` §P0.3.

**One-line change:**
```csharp
public const int MaxTrackedTargets = 16;   // was 4
```

**⚠️ CRITICAL: update the existing test `MaxTrackedTargets_ConstantValueIsFour`** in `PerceptionComponentTests.cs` line 29. Rename it to `MaxTrackedTargets_ConstantValueIsSixteen` and update the assertion to `Assert.Equal(16, ...)`.

**⚠️ CRITICAL: update the eviction test `AddOrUpdateTarget_WhenTableFull_EvictsLowestScoringSlot`** (currently fills 4 entries). It uses `MaxTrackedTargets` as the capacity, so the fill loop must be updated to insert 16 entries (not hardcoded 4), then attempt an eviction. The sort behaviour is unchanged; only the number of fill iterations changes.

**New tests to add (see SC-P0-03-3 through SC-P0-03-5):**
- Fill 16 contacts; assert `Count == 16` and `ThreatScores[0] >= ThreatScores[1]` (sorted descending)
- Add a 17th with higher score than the lowest; verify eviction and `Count` stays 16
- Add a 17th with lower score than all existing; verify it was rejected and the table is unchanged

---

### Task 4: `UnitRoster.Add` / `IndexOf` helpers (TASK-UAI-P0-04)

**File:**
- `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs` — ADD two static methods
- `FDP/Toolkits/Fdp.Toolkits.Tests/CommandHierarchy/UnitRosterTests.cs` — NEW test file

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-04`.  
**Design:** `PREREQ_Phase0_Bundle.md` §P0.4.

**Add these two methods to `UnitRoster`:**

```csharp
/// <summary>
/// Append a subordinate. Returns the 0-based slot index, or -1 if the roster is full.
/// Does not throw on overflow.
/// </summary>
public static unsafe int Add(ref UnitRoster roster, long packedEntity, ushort designation = 0)
{
    if (roster.Count >= Capacity) return -1;
    int slot = roster.Count++;
    roster.SubordinateEntities[slot]  = packedEntity;
    roster.TacticalDesignations[slot] = designation;
    return slot;
}

/// <summary>
/// Returns the slot index of a subordinate, or -1 if not present.
/// </summary>
public static unsafe int IndexOf(ref UnitRoster roster, long packedEntity)
{
    for (int i = 0; i < roster.Count; i++)
        if (roster.SubordinateEntities[i] == packedEntity) return i;
    return -1;
}
```

Both methods are `static` and take `ref UnitRoster` because the struct contains `fixed` arrays that require unsafe pointer access. Mark the struct's methods `unsafe` — or the containing file already has `unsafe` from the `fixed` declarations.

**Tests (see SC-P0-04-1 through SC-P0-04-3):**
- Fill 16 entries → slots 0..15 returned; 17th call returns -1 without mutating Count
- `IndexOf` returns correct slot for present packedEntity, -1 for absent
- After `Add(e)` → `IndexOf(e)` returns the same slot index returned by `Add`
- Edge case: empty roster, `IndexOf` returns -1; after `Add`, `IndexOf` finds it

**No allocation constraint:** the methods are pure struct manipulation; there's nothing to allocate. Verify with a static assert or simply by inspection.

---

### Task 5: `Blackboard1024.Project<T>` helper (TASK-UAI-P0-05)

**File:**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` — ADD method to `Blackboard1024`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Blackboard1024Tests.cs` — NEW test file

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-05`.  
**Design:** `PREREQ_Phase0_Bundle.md` §P0.5.

**Add to `Blackboard1024`:**

```csharp
/// <summary>
/// Project the 1024-byte memory block as a reference to an unmanaged struct T.
/// T must fit within <see cref="ByteSize"/> bytes (assert at call site, not here).
/// Convention: each subsystem projects at a disjoint byte offset.
/// </summary>
[System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
public static unsafe ref T Project<T>(ref Blackboard1024 bb) where T : unmanaged
    => ref System.Runtime.CompilerServices.Unsafe.As<Blackboard1024, T>(ref bb);
```

Add `using System.Runtime.CompilerServices;` to the file if not already present.

**Tests (see SC-P0-05-1 through SC-P0-05-3):**
- Write through `Project<MyState>(ref bb)` → read back the same values through `bb.Memory[0..sizeof(MyState)]` — confirm aliasing
- Write a struct with a known byte pattern via `Project`, then read via a second `Project` call → same values
- `Project` call on two different struct types at offset 0 verifies mutual aliasing works both ways

Define a small `private struct TestState { public int A; public int B; }` in the test class for this.

---

### Task 6: `UtilityTestWorld` helper (TASK-UAI-P0-06)

**Files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — NEW
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorldTests.cs` — NEW

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-06`.  
**Design:** `PREREQ_Phase0_Bundle.md` §P0.6; `Utility_AI_StarterPack_Examples_v1_1.md` §0.

**What to build:**

A `UtilityTestWorld : IDisposable` helper class in `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/` (note: this is a *test project* class, not production code). It wraps `new EntityRepository()`, registers the AI-relevant component types, and provides convenience methods.

**Required public surface (minimum; implement all):**
- `EntityRepository Repo` — the underlying ECS world
- `uint Tick` — monotonically increasing tick counter for `AddOrUpdateTarget` calls
- `Entity SpawnAgent(float health01, float ammo01, int initialAmmunition = 30)` — creates entity with Health + WeaponState (with MaxAmmo) + Position + TargetMemory + (Phase 1: UtilityResultBuffer etc.)
- `Entity SpawnWeaponMount(Entity owner, int mountIndex, ulong weaponGuid, float effRange, float ammo01, int initialAmmunition)` — creates child entity with WeaponState + WeaponMountInfo + PartMetadata
- `void SetWeaponAmmo(Entity owner, int mountIndex, float ammo01)` — sets `WeaponState.Ammo` via mount resolution
- `void SeedContact(Entity self, Entity contact, float distanceM, float threatBoost, float contactHealth01, bool hasLos)` — calls real `TargetMemory.AddOrUpdateTarget`
- `Entity SpawnEqsSensor(Entity owner, uint blueprintId, float topScore, int count, int instanceId)` — creates child sensor entity with `EqsSensor` + seeded `EqsCognitiveBuffer` + `PartMetadata`
- `Entity SpawnLeader()` — creates entity with UnitRoster + Blackboard1024 + TargetMemory + Position
- `Entity SpawnSquadMember(Entity leader, float health01, float ammo01, bool asLauncher = false)` — calls `SpawnAgent`, links to leader via `UnitSubordinate`, calls `UnitRoster.Add`
- `long AssignmentFor(Entity leader, Entity member)` — reads `ThreatMatrixAssignmentState` from leader's blackboard via `Blackboard1024.Project<T>` + `UnitRoster.IndexOf`; returns the assigned target packed value
- `static uint Fnv1a32(string name)` — computes 32-bit FNV-1a hash matching the source-generator formula (basis `2166136261u`, prime `16777619u`, no truncation for 32-bit form)

**⚠️ For Phase 0, only register Phase-0 components** (Health, WeaponState, WeaponMountInfo, TargetMemory, SensorContactList, EqsSensor, EqsCognitiveBuffer, PartMetadata, UnitRoster, UnitSubordinate, Blackboard1024, and Position from `Fdp.Toolkit.Geographic.Components`). **Do not reference Utility AI components** (UtilityResultBuffer etc.) — those are Phase-1 additions.

**⚠️ `ThreatMatrixAssignmentState`** does not exist yet. For Phase 0, `AssignmentFor` should return a sentinel (`-1L`) and be documented as `// placeholder — ThreatMatrixAssignmentState defined in Phase 1`. This is acceptable because P0.07's gate test only validates the infrastructure, not the assignment state.

**⚠️ `SpawnEqsSensor`** must seed `EqsCognitiveBuffer` via `buf.GetSpanRW()` — the [InlineArray] span-cast discipline from architecture §8.2. Do NOT use the direct indexer `buf.Results[i] = ...` (silent write loss). See `EqsComponents.cs` `GetSpanRW()` for the pattern.

**Component registration** — use the `RegisterComponent<T>()` public API on `EntityRepository` (see `AimAndFireExecutorTests.cs:31-37` for the pattern). Register every component type the helper touches before any `AddComponent` call.

**Position component** — use `Fdp.Toolkit.Geographic.Components.Position` (in `Fdp.Toolkits/Geographic/Components/Position.cs`, `ComponentId = GlobalComponentIds.GeoPosition = 48`). The `Value` field is `System.Numerics.Vector3`.

**Tests (see SC-P0-06-1 through SC-P0-06-6):**
- `new UtilityTestWorld()` succeeds; `Dispose()` cleans up
- `SpawnAgent(1f, 1f)` → entity carries Health, WeaponState (MaxAmmo=30, Ammo=30), Position (zero), TargetMemory
- `SpawnWeaponMount(owner, 1, ...)` → child entity with WeaponMountInfo.MountIndex==1, PartMetadata.ParentEntity==owner
- `SeedContact` calls `TargetMemory.AddOrUpdateTarget`; contact lands in slot 0 with the supplied threatBoost as `ThreatScores[0]`
- `SpawnEqsSensor(self, blueprintId, topScore, 2, 0)` → child with EqsSensor.BlueprintId==blueprintId, EqsCognitiveBuffer.Count==2, GetSpanRO()[0].Score==topScore
- `Fnv1a32("CoverQuery")` produces a stable, non-zero uint (pin the exact value in the test comment)

---

### Task 7: Phase-0 integration test gate (TASK-UAI-P0-07)

**File:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Phase0IntegrationTests.cs` — NEW

**Task Detail:** See `TASK-DETAIL.md#task-uai-p0-07`.  
**Design:** `PREREQ_Phase0_Bundle.md` "Phase-0 exit gate".

**One xUnit test** named `Phase0_Bundle_Integration` that exercises all six prerequisite items in sequence. It must not use any Phase-1 utility scorer.

**What the test must validate:**

```csharp
[Fact]
public unsafe void Phase0_Bundle_Integration()
{
    using var w = new UtilityTestWorld();

    // ── P0.1 / P0.2: Multi-mount agent ─────────────────────────────────────
    var agent = w.SpawnAgent(health01: 1f, ammo01: 1f, initialAmmunition: 30);
    var mount1 = w.SpawnWeaponMount(agent, mountIndex: 1, weaponGuid: 0xABCDEF01, effRange: 300f, ammo01: 0.5f, initialAmmunition: 20);
    var mount2 = w.SpawnWeaponMount(agent, mountIndex: 2, weaponGuid: 0xABCDEF02, effRange: 600f, ammo01: 1.0f, initialAmmunition: 4);

    // 3 WeaponState components total
    Assert.True(w.Repo.HasComponent<WeaponState>(agent));
    Assert.True(w.Repo.HasComponent<WeaponState>(mount1));
    Assert.True(w.Repo.HasComponent<WeaponState>(mount2));

    // WeaponMountInfo on children only
    Assert.False(w.Repo.HasComponent<WeaponMountInfo>(agent));
    Assert.True(w.Repo.HasComponent<WeaponMountInfo>(mount1));
    Assert.Equal(1, w.Repo.GetComponentRO<WeaponMountInfo>(mount1).MountIndex);
    Assert.Equal(2, w.Repo.GetComponentRO<WeaponMountInfo>(mount2).MountIndex);

    // PartMetadata back-links
    Assert.Equal(agent, w.Repo.GetComponentRO<PartMetadata>(mount1).ParentEntity);
    Assert.Equal(agent, w.Repo.GetComponentRO<PartMetadata>(mount2).ParentEntity);

    // MaxAmmo cached correctly (P0.1)
    Assert.Equal(30, w.Repo.GetComponentRO<WeaponState>(agent).MaxAmmo);
    Assert.Equal(20, w.Repo.GetComponentRO<WeaponState>(mount1).MaxAmmo);

    // Independent ammo mutation (P0.2)
    w.SetWeaponAmmo(agent, mountIndex: 0, ammo01: 0f);
    Assert.Equal(0, w.Repo.GetComponentRO<WeaponState>(agent).Ammo);
    Assert.Equal(10, w.Repo.GetComponentRO<WeaponState>(mount1).Ammo); // 50% of 20

    // ── P0.3: Perception cap = 16 ────────────────────────────────────────────
    Assert.Equal(16, PerceptionConstants.MaxTrackedTargets);
    var contacts = new Entity[16];
    for (int i = 0; i < 16; i++)
    {
        contacts[i] = w.Repo.CreateEntity();
        w.SeedContact(agent, contacts[i], distanceM: 10f + i, threatBoost: (float)(i + 1),
                      contactHealth01: 1f, hasLos: true);
    }
    Assert.Equal(16, w.Repo.GetComponentRO<TargetMemory>(agent).Count);

    // ── P0.4: UnitRoster helpers ──────────────────────────────────────────────
    var leader = w.SpawnLeader();
    var m1 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
    var m2 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
    {
        ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(leader);
        int slot1 = UnitRoster.IndexOf(ref roster, (long)m1.PackedValue);
        int slot2 = UnitRoster.IndexOf(ref roster, (long)m2.PackedValue);
        Assert.True(slot1 >= 0, "m1 should be in roster");
        Assert.True(slot2 >= 0, "m2 should be in roster");
        Assert.NotEqual(slot1, slot2);
    }

    // ── P0.5: Blackboard1024.Project<T> ─────────────────────────────────────
    {
        ref var bb = ref w.Repo.GetComponentRW<Blackboard1024>(leader);
        ref var proj = ref Blackboard1024.Project<TestProjectionStruct>(ref bb);
        proj.Value = 42;
        // Re-read via projection must see the mutation
        ref var reread = ref Blackboard1024.Project<TestProjectionStruct>(ref bb);
        Assert.Equal(42, reread.Value);
    }

    // ── P0.6: EQS sensor child entities ──────────────────────────────────────
    var coverSensor = w.SpawnEqsSensor(agent, Fnv1a32("CoverQuery"), topScore: 0.85f, count: 3, instanceId: 0);
    Assert.True(w.Repo.HasComponent<EqsSensor>(coverSensor));
    Assert.Equal(Fnv1a32("CoverQuery"), w.Repo.GetComponentRO<EqsSensor>(coverSensor).BlueprintId);
    Assert.Equal(3, w.Repo.GetComponentRO<EqsCognitiveBuffer>(coverSensor).Count);
    Assert.Equal(0.85f, w.Repo.GetComponentRO<EqsCognitiveBuffer>(coverSensor).GetSpanRO()[0].Score,
                 precision: 3);

    // ── Gate: all above pass means Phase-0 is complete ──────────────────────
}

// Small struct for Blackboard projection test
private struct TestProjectionStruct { public int Value; }
private static uint Fnv1a32(string name) => UtilityTestWorld.Fnv1a32(name);
```

`Entity.PackedValue` is `ulong`; `UnitRoster.SubordinateEntities` stores `long`. Cast: `(long)entity.PackedValue` when storing, `roster.SubordinateEntities[i] == (long)e.PackedValue` when comparing.

---

## 🧪 Testing Requirements

**Minimum new tests:** 30 (across all 7 tasks combined)

**Distribution:**
- P0.01: ≥ 4 tests
- P0.02: ≥ 5 tests
- P0.03: ≥ 3 new/updated tests
- P0.04: ≥ 4 tests
- P0.05: ≥ 3 tests
- P0.06: ≥ 6 tests
- P0.07: 1 integration test (but exercises everything)

**Test quality:**
- Every test must verify **actual values** (struct field values, component counts, slot indices) — not just "no exception thrown"
- `SpawnEqsSensor` test must assert `GetSpanRO()[0].Score` equals the seeded `topScore` — proving the [InlineArray] span discipline is used correctly
- Eviction test for `TargetMemory` must fill all 16 slots before testing eviction
- `Blackboard1024.Project<T>` test must verify mutation through the projection is visible via re-projection (proves aliasing, not just successful compilation)

---

## ⚠️ Common Pitfalls

**1. Entity identity — `ulong` vs `long`** — `Entity.PackedValue` returns a **`ulong`** (`((ulong)Generation << 32) | (uint)Index`), but `UnitRoster.SubordinateEntities` is declared as `fixed long[Capacity]`. Store as `(long)entity.PackedValue` and compare as `roster.SubordinateEntities[i] == (long)entity.PackedValue`. The cast is safe (bit-pattern identical for non-negative indices). The same pattern is used in `HillAttackMutableState.ActiveEntityPacked[8]` (also `fixed long[]` storing `Entity.PackedValue`). Make `Add` / `IndexOf` take `long packedEntity` (consistent with the existing field type) — callers cast when passing `entity.PackedValue`.

**2. WeaponMountInfo not registered** — the translator guard `repo.IsComponentTypeRegistered<WeaponMountInfo>()` means tests **must** call `w.Repo.RegisterComponent<WeaponMountInfo>()` before the child entities can be spawned. The `UtilityTestWorld` constructor must include this.

**3. [InlineArray] span trap** — `EqsCognitiveBuffer.Results` is `[InlineArray(16)]`. Direct indexing (`buf.Results[i] = ...`) silently loses writes. Always use `buf.GetSpanRW()` for writes. Verify this in the `SpawnEqsSensor` implementation.

**4. PerceptionComponentTests eviction test update** — the test currently calls `AddOrUpdateTarget` 4 times explicitly. After raising to 16, you must call it 16 times to fill the table before testing eviction. Use a loop. Update the test; don't delete it.

**5. `GetComponentRO<T>` vs `GetComponentRW<T>`** — read-only vs read-write refs. Use `RO` for read-only access (assertions), `RW` when mutating. Wrong choice causes CS-level issues or subtle aliasing bugs.

**6. `WeaponMountQuery.EnumerateMounts` query API** — check how `EntityRepository` allows iterating entities with a specific component. The existing `EqsResultUpdateSystem.cs` uses `view.Query().With<EqsSensor>().Build()`. Use the same pattern. If `EntityRepository` doesn't have a direct `Query<T>()` method (vs `ISimulationView`), look at how the HillAttack system reads `UnitRoster` entities — adapt accordingly.

---

## 📊 Report Requirements

Submit to `.dev/utility-ai/reports/BATCH-01-REPORT.md`. Answer:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you find the `EntityRepository` query API for iterating entities by component type? What exact method did you use in `WeaponMountQuery`?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the instructions?

**Q5:** Is the `Entity.PackedValue` (or equivalent) API consistent with what HillAttack code uses? Note the exact property/cast you used.

**Q6:** Suggested git commit message for this batch.

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] **P0.01** `WeaponState.MaxAmmo` field added; spawn site writes it; 4+ tests green
- [ ] **P0.02** `WeaponMountInfo` component + `WeaponMountQuery` helper; translator spawns child entities; 5+ tests green
- [ ] **P0.03** `MaxTrackedTargets = 16`; existing perception tests updated; 3+ new tests covering 16-slot behaviour; all green
- [ ] **P0.04** `UnitRoster.Add` / `IndexOf` methods added; 4+ tests green
- [ ] **P0.05** `Blackboard1024.Project<T>` method added; 3+ tests green
- [ ] **P0.06** `UtilityTestWorld` helper complete with all listed methods; 6+ tests green
- [ ] **P0.07** Gate integration test passes
- [ ] `dotnet build IOS-IG-SimHost.sln` — zero errors, zero new warnings
- [ ] `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj` — all tests pass

---

## 📚 Reference Materials

- **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — Phase 0, all 7 tasks
- **Prereq Bundle Design:** `.dev/utility-ai/PREREQ_Phase0_Bundle.md` — full rationale + code snippets
- **Architecture §8.1:** cap invariant assertion (asserted in Phase 1)
- **Architecture §10.1:** `Blackboard1024.Project<T>` usage context
- **Existing precedent — Blackboard projection:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` lines 47–48
- **Existing precedent — spawn pattern:** `FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs`
- **Existing precedent — child entity pattern:** `FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs`
- **Existing precedent — EQS span discipline:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` (`GetSpanRW`)
- **Existing test example:** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/AimAndFireExecutorTests.cs` (EntityRepository setup pattern)
- **Entity struct:** `FDP/Engine/Fdp.Core/` — check for `PackedValue` or equivalent
