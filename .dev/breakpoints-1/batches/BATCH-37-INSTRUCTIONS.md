# BATCH-37: Corrective Task 0 + Phase P2T3 (Structural / Spatial / Lifecycle Trackers)

**Batch Number:** BATCH-37
**Tasks:** Corrective Task 0 (BATCH-36 fix), UBP-P2T3
**Phase:** P2 Universal substrate (completion)
**Estimated Effort:** 14-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-36 must be complete (DataBreakpointSystem + event path in place)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/breakpoints-1/DESIGN.md` — §6.8 (Structural/Spatial/Lifecycle paths)
3. **Task Definitions:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P2T3
4. **BATCH-36 Review:** `.dev/breakpoints-1/reviews/BATCH-36-REVIEW.md` — one fix required before P2T3
5. **Reference implementation (offline scanning patterns):**
   `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs`
   - `RunStructuralFrame(...)` — diff logic + `ComputeEffectivePresence` helper
   - `RunSpatialFrame(...)` — bounds tracking
   - `RunLifecycleScan(...)` — birth/death detection via `MaxEntityIndex` + `GetDestructionLog`
   Read these before implementing to understand the correct patterns.

### Source Code Locations
- **Breakpoints project:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/`
- **Tests project:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/`
- **SearchPredicateDto types:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`
  - `StructuralPredicateDto` — `ComponentType`, `ModificationType` (Added/Removed/AnyChange), `AuthorityRequirement`
  - `SpatialBoundingPredicateDto` — `Bounds` (BoundingBox2D), `TriggerEvent` (Entry/Exit/EntryOrExit), `PositionComponentType`, `PositionXPath`, `PositionYPath`
  - `LifecyclePredicateDto` — `IdentifierType`, `TargetValue`, `NameComponentType`, `NamePropertyPath`
- **EntityRepository spatial/structural helpers:**
  - `repo.MaxEntityIndex` — max entity index
  - `repo.GetComponentMask(int index)` — `ref BitMask512` for an entity by raw index
  - `repo.GetMetadata(int index)` — `ref EntityMetadataCold` with `IsActive` + `AuthorityMask`
  - `repo.GetEntityByIndex(int index)` — Entity from raw index
  - `repo.GetDestructionLog()` → `IReadOnlyList<Entity>` — entities destroyed this tick
  - `ComponentTypeRegistry.GetId(Type)` — resolve component type ID
  - `repo.HasAuthority(Entity, int componentId)` — authority check by type ID
  - `repo.HasComponentByTypeId(Entity, int)` — component presence check by type ID (if available; use mask directly otherwise)
  - `repo.GetComponentPointer(Entity, int)` — unsafe raw pointer for unmanaged component (used for position reading)
  - `repo.GetManagedComponentByTypeId(Entity, int)` — managed component by type ID

### How to Build and Test
```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2\
dotnet build IOS-IG-SimHost.sln -c Debug

# Run breakpoints tests
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

### Report Submission
**When done, submit your report to:**
`.dev/breakpoints-1/reports/BATCH-37-REPORT.md`

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective Task 0:** Fix the one gap from BATCH-36 → all 27 existing tests still pass + 1 new test
2. **UBP-P2T3:** Implement structural tracker, spatial tracker, lifecycle tracker → write tests → ALL pass

**DO NOT** move to UBP-P2T3 until Corrective Task 0 tests pass. Fix all failures immediately.

---

## Corrective Task 0 — Fix BATCH-36 Test Quality Gap

**Required reading:** `.dev/breakpoints-1/reviews/BATCH-36-REVIEW.md` — Issue 1

Add a zero-allocation assertion to `DataBreakpointSystem_NoBreakpoints_DoesNoWork` following the same
pattern used for `GateOff_Execute_ZeroAllocations` in `DebugSnapshotProviderTests`.

In `DataBreakpointSystemTests`, replace the existing `NoBreakpoints_DoesNoWork` test with:

```csharp
[Fact]
public void NoBreakpoints_DoesNoWork_ZeroAllocations()
{
    var (manager, system, repo) = Setup();

    // Warmup JIT.
    system.Execute(repo, 0f);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    long before = GC.GetAllocatedBytesForCurrentThread();
    const int Iterations = 10_000;
    for (int i = 0; i < Iterations; i++)
        system.Execute(repo, 0f);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.False(manager.IsPaused);
    Assert.Equal(0L, after - before);
}
```

Also fix the fully-qualified namespace reference in `DataBreakpointSystem.cs`:
- Add `using System.Collections.Generic;` at the top
- Remove `System.Collections.Generic.` prefix from `List<Entity>` inline

After the fix, confirm 28 total tests pass (27 existing + 1 renamed).

---

## Task UBP-P2T3 — Structural / Spatial / Lifecycle Scanners

**Design reference:** [DESIGN.md §6.8](../DESIGN.md#68-structural--spatial--lifecycle-paths)
**Task detail:** [TASK-DETAIL.md UBP-P2T3](../TASK-DETAIL.md#ubp-p2t3--structural--spatial--lifecycle-scanners)

### Overview

P2T3 adds three per-tick state-tracking scanner modes to `DataBreakpointManager` and
`DataBreakpointSystem`. These modes cannot be handled by the compiled-predicate path because
their hit detection requires cross-tick diffing of entity sets, not per-entity threshold evaluation.

The implementation mirrors `RecordingSearchService.RunStructuralFrame`,
`RunSpatialFrame`, and `RunLifecycleScan` — the same patterns already in production for
offline search. Read those methods before implementing.

### Manager extensions

**Add `HasStatefulTrackers` property and `EvaluateStatefulBreakpoints` method to `IDataBreakpointManager`:**

```csharp
/// <summary>
/// True when any structural, spatial, or lifecycle breakpoints are mounted.
/// Used by DataBreakpointSystem for a secondary gate check.
/// </summary>
bool HasStatefulTrackers { get; }

/// <summary>
/// Called once per tick by DataBreakpointSystem after compiled-predicate evaluation.
/// Evaluates all structural, spatial, and lifecycle trackers against the current repo state.
/// </summary>
void EvaluateStatefulBreakpoints(EntityRepository repo);
```

**Update `HasMountedDelegates`** to also return true when `HasStatefulTrackers` is true.

**In `DataBreakpointManager`, add three internal tracker dictionaries** (private, allocated on first use
when a relevant breakpoint is added):

```csharp
// Structural trackers: BreakpointId -> (Breakpoint, dto, set of entities known to have the component)
private readonly Dictionary<BreakpointId, (Breakpoint bp, StructuralPredicateDto dto, HashSet<Entity> knownSet)>
    _structuralTrackers = new();

// Spatial trackers: BreakpointId -> (Breakpoint, dto, set of entities currently inside the bounds)
private readonly Dictionary<BreakpointId, (Breakpoint bp, SpatialBoundingPredicateDto dto, HashSet<Entity> insideSet)>
    _spatialTrackers = new();

// Lifecycle trackers: BreakpointId -> (Breakpoint, dto, set of known-alive entities)
private readonly Dictionary<BreakpointId, (Breakpoint bp, LifecyclePredicateDto dto, HashSet<Entity> knownAlive)>
    _lifecycleTrackers = new();
```

**Extend `TryMountDelegate`** to populate the tracker dicts for structural/spatial/lifecycle condition types:
```csharp
case StructuralPredicateDto structuralDto:
    _structuralTrackers[id] = (bp, structuralDto, new HashSet<Entity>());
    break;

case SpatialBoundingPredicateDto spatialDto:
    _spatialTrackers[id] = (bp, spatialDto, new HashSet<Entity>());
    break;

case LifecyclePredicateDto lifecycleDto:
    _lifecycleTrackers[id] = (bp, lifecycleDto, new HashSet<Entity>());
    break;
```

**Extend `UnmountDelegate`** to remove from all three tracker dicts.

**Implement `HasStatefulTrackers`:**
```csharp
public bool HasStatefulTrackers =>
    _structuralTrackers.Count > 0 || _spatialTrackers.Count > 0 || _lifecycleTrackers.Count > 0;
```

### `EvaluateStatefulBreakpoints` implementation

The method is called from `DataBreakpointSystem.Execute` once per tick.

**Do not call `OnHit` inside the iteration loops.** Collect hits first, then fire — the
same collect-then-fire pattern used in P2T1 to prevent rewind-mid-iteration errors.

```csharp
public void EvaluateStatefulBreakpoints(EntityRepository repo)
{
    var hits = new List<(Breakpoint bp, Entity entity)>(); // temp; reset per call

    EvaluateStructuralTrackers(repo, hits);
    EvaluateSpatialTrackers(repo, hits);
    EvaluateLifecycleTrackers(repo, hits);

    foreach (var (bp, entity) in hits)
        OnHit(bp, entity);
}
```

#### Structural tracker evaluation

Mirror `RecordingSearchService.RunStructuralFrame`:

```csharp
private void EvaluateStructuralTrackers(EntityRepository repo, List<(Breakpoint, Entity)> hits)
{
    if (_structuralTrackers.Count == 0) return;

    foreach (var (bpId, (bp, dto, knownSet)) in _structuralTrackers)
    {
        if (!bp.Enabled) continue;

        int typeId = ComponentTypeRegistry.GetId(dto.ComponentType);
        if (typeId < 0) continue;

        int maxIdx = repo.MaxEntityIndex;
        for (int i = 0; i <= maxIdx; i++)
        {
            ref var compMask = ref repo.GetComponentMask(i);
            ref var meta     = ref repo.GetMetadata(i);
            if (!meta.IsActive) continue;

            Entity entity = repo.GetEntityByIndex(i);
            if (entity.IsNull) continue;

            // Apply filter entity scoping if set.
            if (bp.FilterEntity is { } fe && fe != entity) continue;

            bool present = ComputeEffectivePresence(ref compMask, ref meta, typeId, dto.AuthorityRequirement);
            bool was     = knownSet.Contains(entity);

            if (present && !was)
            {
                knownSet.Add(entity);
                if (dto.ModificationType == StructuralModification.Added ||
                    dto.ModificationType == StructuralModification.AnyChange)
                    hits.Add((bp, entity));
            }
            else if (!present && was)
            {
                knownSet.Remove(entity);
                if (dto.ModificationType == StructuralModification.Removed ||
                    dto.ModificationType == StructuralModification.AnyChange)
                    hits.Add((bp, entity));
            }
        }

        // Remove destroyed entities from the known set.
        var destroyed = repo.GetDestructionLog();
        for (int i = 0; i < destroyed.Count; i++)
            knownSet.Remove(destroyed[i]);
    }
}

// Authority helper (same logic as RecordingSearchService.ComputeEffectivePresence).
private static bool ComputeEffectivePresence(
    ref BitMask512 componentMask,
    ref EntityMetadataCold meta,
    int typeId,
    AuthorityRequirement req) =>
    req switch
    {
        AuthorityRequirement.RequireAuthority =>
            componentMask.IsSet(typeId) && meta.AuthorityMask.IsSet(typeId),
        AuthorityRequirement.RequireGhost =>
            componentMask.IsSet(typeId) && !meta.AuthorityMask.IsSet(typeId),
        _ => componentMask.IsSet(typeId)
    };
```

**Note:** In live context, DO NOT call `repo.ClearDestructionLog()`. The recorder or another
system owns that responsibility. Only read the log here.

#### Spatial tracker evaluation

Mirror `RecordingSearchService.RunSpatialFrame`. Use `ReadPosition2D` which reads the position
via `repo.GetComponentPointer` + `Marshal.PtrToStructure` for unmanaged components, then extracts
the X/Y fields by name via reflection. This follows the exact same pattern as the replay browser.

```csharp
private void EvaluateSpatialTrackers(EntityRepository repo, List<(Breakpoint, Entity)> hits)
{
    if (_spatialTrackers.Count == 0) return;

    foreach (var (bpId, (bp, dto, insideSet)) in _spatialTrackers)
    {
        if (!bp.Enabled) continue;

        int maxIdx = repo.MaxEntityIndex;
        for (int i = 0; i <= maxIdx; i++)
        {
            ref var meta = ref repo.GetMetadata(i);
            if (!meta.IsActive) continue;

            Entity entity = repo.GetEntityByIndex(i);
            if (entity.IsNull) continue;
            if (bp.FilterEntity is { } fe && fe != entity) continue;

            // Read 2D position using the DTO's position component + field paths.
            Vector2 pos = ReadPosition2D(repo, entity, dto);
            bool isInside   = dto.Bounds.Contains(pos.X, pos.Y);
            bool wasInside  = insideSet.Contains(entity);

            if (isInside && !wasInside)
            {
                insideSet.Add(entity);
                if (dto.TriggerEvent == BoundaryEvent.Entry ||
                    dto.TriggerEvent == BoundaryEvent.EntryOrExit)
                    hits.Add((bp, entity));
            }
            else if (!isInside && wasInside)
            {
                insideSet.Remove(entity);
                if (dto.TriggerEvent == BoundaryEvent.Exit ||
                    dto.TriggerEvent == BoundaryEvent.EntryOrExit)
                    hits.Add((bp, entity));
            }
        }

        // Remove destroyed entities from inside-set.
        var destroyed = repo.GetDestructionLog();
        for (int i = 0; i < destroyed.Count; i++)
            insideSet.Remove(destroyed[i]);
    }
}
```

For `ReadPosition2D` and `ReadFloatField`, copy the implementation from
`RecordingSearchService` (it already exists in production). This is a private helper, not
a public method — copy it directly into `DataBreakpointManager.cs` rather than trying to reuse
the one in `RecordingSearchService` (which is internal to that class).

For `BoundingBox2D.Contains(float x, float y)`, look up the actual API on `BoundingBox2D` in
`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` (or nearby). Use whatever
method actually exists (may be `Contains`, `ContainsPoint`, or inline bounds check).

#### Lifecycle tracker evaluation

For birth detection: iterate `repo.MaxEntityIndex`, check `meta.IsActive`, check not in `knownAlive`,
evaluate the identifier, add to `knownAlive`, fire if matches.

For death detection: iterate `repo.GetDestructionLog()`, check if entity is in `knownAlive`,
remove, fire if matches.

Identifier evaluation (mirror `RecordingSearchService.MatchesLifecycleCriteria` or equivalent):
```csharp
private static bool MatchesLifecycleCriteria(EntityRepository repo, Entity entity, LifecyclePredicateDto dto)
{
    return dto.IdentifierType switch
    {
        EntityIdentifierType.EcsHandle =>
            entity.Index.ToString() == dto.TargetValue ||
            entity.ToString() == dto.TargetValue,

        EntityIdentifierType.NameSubstring =>
            dto.NameComponentType != null
                ? ReadEntityName(repo, entity, dto) is { } n &&
                  n.Contains(dto.TargetValue, StringComparison.OrdinalIgnoreCase)
                : entity.ToString().Contains(dto.TargetValue, StringComparison.OrdinalIgnoreCase),

        EntityIdentifierType.NetworkId =>
            false, // network-id lookup not available without network module injection; skip

        _ => false
    };
}

private static string? ReadEntityName(EntityRepository repo, Entity entity, LifecyclePredicateDto dto)
{
    // Use reflection to read dto.NamePropertyPath from dto.NameComponentType.
    // Same pattern as ReadFloatField in spatial tracker.
    int typeId = ComponentTypeRegistry.GetId(dto.NameComponentType!);
    if (typeId < 0) return null;
    // ... (use repo.GetComponentPointer / Marshal for unmanaged, or GetManagedComponentByTypeId for managed)
    return null; // return name string or null
}
```

### `DataBreakpointSystem` extension

Add a call to the stateful tracker evaluation after the event path:

```csharp
// Stateful trackers (structural / spatial / lifecycle)
if (_manager.HasStatefulTrackers)
    _manager.EvaluateStatefulBreakpoints(repo);
```

Note: `HasMountedDelegates` already gates the entire Execute body. But `HasStatefulTrackers` provides a
secondary check so we skip the `EvaluateStatefulBreakpoints` call if there are no trackers (avoids
the method call overhead).

---

## Tests for UBP-P2T3

Add class `DataBreakpointSystemStatefulTests` to the test project, annotated with
`[Collection("ComponentRegistry")]`.

### Test setup helpers needed

**Test components and events:**
```csharp
[ComponentId(210)]
internal struct WeaponState { public int Ammo; }

[ComponentId(211)]
internal struct Position2D { public float X; public float Y; }
```

**Factory method** (similar to `DataBreakpointSystemTests.Setup()`): create a fresh
`DataBreakpointManager` with real compilers, `DataBreakpointSystem`, and `EntityRepository`.

### Test 1: `StructuralPredicate_FiresOnComponentAdded`

1. Create entity E (alive, no WeaponState initially).
2. Register `StructuralPredicateDto(WeaponState, Added)` breakpoint.
3. Call `system.Execute(repo, 0f)` — no hit (E has no WeaponState).
4. Add `WeaponState` component to E.
5. Call `system.Execute(repo, 0f)` again.
6. Assert: `manager.IsPaused == true`, hit event fired for entity E.

### Test 2: `StructuralPredicate_DoesNotFireOnDwelling`

1. Create entity E with WeaponState already added BEFORE the breakpoint is registered.
2. Register `StructuralPredicateDto(WeaponState, Added)` breakpoint.
3. Call `system.Execute(repo, 0f)`.
4. Assert: `manager.IsPaused == false` — E was already in the initial snapshot, no transition detected.

### Test 3: `SpatialPredicate_FiresOnEntry_NotOnDwelling`

1. Create entity E with `Position2D { X=10, Y=10 }`.
2. Register spatial breakpoint: `BoundingBox2D(minX=0, minY=0, maxX=20, maxY=20)`, `Entry`.
3. First `Execute`: E is outside (X=10 is inside? Actually yes; need to start E outside).
   Start E at `{ X=100, Y=100 }` (outside bounds).
4. Call `system.Execute` → no hit (outside).
5. Move E to `{ X=10, Y=10 }` (inside bounds).
6. Call `system.Execute` → hit (entry).
7. Call `system.Execute` again (still inside) → no new hit (dwelling).
8. Assert: exactly 1 hit total.

### Test 4: `LifecyclePredicate_FiresOnBirth_AndOnDeath`

1. Register `LifecyclePredicateDto(NameSubstring, "Tank")` breakpoint.
2. Create entity E with a name component containing "Tank".
   (If no name component is available, use `EntityIdentifierType.EcsHandle` and match by index string.)
3. First `Execute` after creation: hit (birth detected).
4. Assert hit count = 1.
5. Destroy E: `repo.DestroyEntity(E)`.
6. Call `Execute` again: hit (death detected via destruction log).
7. Assert hit count = 2.

**Note on name component:** If testing with `NameSubstring` requires a name component type that's
complex to set up, use `EntityIdentifierType.EcsHandle` and `TargetValue = entity.Index.ToString()`
for a simpler test. The NameSubstring path can be covered by a second test once the name component
is established.

### Test 5: `AuthorityRequirement_RequireAuthority_FiltersGhostMutations`

1. Create entity E.
2. Register `StructuralPredicateDto(WeaponState, Added, RequireAuthority)` breakpoint.
3. Add `WeaponState` to E but with no authority (`repo.SetAuthority<WeaponState>(E, false)` or
   simply add without granting authority — check if component defaults to ghost or authoritative).
4. Call `Execute` → should NOT fire (ghost, authority requirement not met).
5. Grant authority: `repo.SetAuthority<WeaponState>(E, true)` or add a second entity with authority.
6. Assert the ghost entity never triggered a hit.

**Note:** Check how authority is assigned by default when `AddComponent` is called. If components
are authoritative by default, the test must explicitly clear authority. Look at `repo.SetAuthority` API.

---

## Quality Standards

**Test Quality:**
- Each test has a clear positive AND negative case where specified by TASK-DETAIL.
- Tests verify actual state (IsPaused, hit count, entity identity) not just "no exception".
- The `SpatialPredicate_FiresOnEntry_NotOnDwelling` test MUST call Execute 3+ times to prove that
  dwelling inside the bounds does NOT produce multiple hits.

**Code Quality:**
- `EvaluateStatefulBreakpoints` MUST use collect-then-fire (same as component predicate path).
- Do NOT clear `repo.GetDestructionLog()` — only read it.
- Position reading via reflection is acceptable (matches existing production pattern).
- `HasStatefulTrackers` must be a fast O(1) count check.

---

## Success Criteria

- [ ] Corrective Task 0 fix applied; 28 total tests pass (27 + 1 renamed)
- [ ] `HasStatefulTrackers` property on `IDataBreakpointManager` and `DataBreakpointManager`
- [ ] `EvaluateStatefulBreakpoints(EntityRepository)` on both interface and implementation
- [ ] Structural tracker: diff logic + `ComputeEffectivePresence` helper
- [ ] Spatial tracker: position reading + bounds entry/exit tracking
- [ ] Lifecycle tracker: birth detection + death detection via destruction log
- [ ] `DataBreakpointSystem.Execute` calls `EvaluateStatefulBreakpoints` when `HasStatefulTrackers`
- [ ] All 4+ P2T3 tests pass (StructuralPredicate_FiresOnComponentAdded, SpatialPredicate_FiresOnEntry_NotOnDwelling, LifecyclePredicate_FiresOnBirth_AndOnDeath, AuthorityRequirement_RequireAuthority_FiltersGhostMutations)
- [ ] Full solution builds with 0 errors
- [ ] Report submitted at `.dev/breakpoints-1/reports/BATCH-37-REPORT.md`

---

## Developer Insights (required in report)

**Q1:** How did the offline replay browser's scanning pattern translate to the live per-tick context?
What differences did you discover (e.g., destruction log clearing, version tracking)?

**Q2:** What was the implementation approach for position reading in the spatial tracker?
Did `BoundingBox2D` have a `Contains` method or did you do an inline bounds check?

**Q3:** What approach did you use for lifecycle birth detection (iteration vs. dedicated log)?
Why?

**Q4:** Any edge cases or unexpected behaviors discovered during testing?

---

## Reference Materials
- **Design §6.8:** `.dev/breakpoints-1/DESIGN.md`
- **Task spec:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P2T3
- **BATCH-36 review:** `.dev/breakpoints-1/reviews/BATCH-36-REVIEW.md`
- **Offline structural/spatial/lifecycle scanners:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs` (lines ~337-450 for RunStructuralFrame/RunSpatialFrame)
- **RecordingSearchService position reading:** same file, look for `ReadPosition2D` and `ReadFloatField`
- **BoundingBox2D definition:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` or nearby
- **EntityMetadataCold structure:** `FDP/Engine/Fdp.Core/EntityIndex.cs` (look for `IsActive`, `AuthorityMask`)
- **Code standards:** `.github/skills/CODE-STANDARDS.md`
