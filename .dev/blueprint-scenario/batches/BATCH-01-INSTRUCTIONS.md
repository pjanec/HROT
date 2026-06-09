# BATCH-01: Core Unified Attach/Detach Seam (BSA-102)

**Batch Number:** BATCH-01  
**Tasks:** BSA-102 (Unified attach/detach seam in core, keyed by `BlueprintId`)  
**Phase:** Phase 1 — Core foundation  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Move the blueprint attach logic from the editor assembly into core (`Fdp.Toolkits`), keyed by runtime `int blueprintId` instead of the authoring `BlueprintAsset`. Add a `DetachFromEntity` method. The existing editor `BlueprintAttachService` becomes a thin forwarder.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Design Document:** `.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §3 (unified seam), §10 (module placement)
3. **Task Details:** `.dev/blueprint-scenario/TASK-DETAIL.md` — BSA-102 section
4. **Task Tracker:** `.dev/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **New core service (create):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Editor forwarder (modify):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs`
- **Partition allocator (read-only):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`
- **Blueprint registry:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`
- **Blackboard tier enum:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlackboardTier.cs`
- **Existing tests to reference:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintAttachServiceTests.cs`
- **Test fixture:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`
- **Test fake blueprints:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/FakeBlueprints.cs`

### Report Submission
**When done, submit your report to:**  
`.dev/blueprint-scenario/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/blueprint-scenario/questions/BATCH-01-QUESTIONS.md`

---

## Context

The current `BlueprintAttachService` lives in `Hrot.Blueprints.Editor.Runtime` and takes a `BlueprintAsset` (authoring type). CGF/genesis and mid-runtime events must NOT depend on the editor assembly. We are moving the attach logic into core `Fdp.Toolkits`, keyed by the runtime `int blueprintId` (+ `BlueprintRegistry`), so all consumers can call it without editor dependencies.

**Related Tasks:**
- [BSA-102](../TASK-DETAIL.md#bsa-102-unified-attachdetach-seam-in-core-keyed-by-blueprintid) — This batch

---

## 🎯 Batch Objectives

Create the core `BlueprintInstanceService` in `Fdp.Toolkits` with:
1. `AttachToEntity(world, registry, blueprintId, entity)` → `BlueprintAttachResult` (classified: Attached/AlreadyAttached/NotRegistered/NotInstanceKind/NoSlotAvailable)
2. `DetachFromEntity(world, blueprintId, entity)` → `bool` (frees slot, dense-compacts)

Then reduce the editor `BlueprintAttachService.AttachToEntity(..., asset, ...)` to a thin forwarder: compute `BlueprintIdHash.Compute(asset.AssetId)` → call core seam.

---

## ✅ Tasks

### Task 1: Create `BlueprintInstanceService` in core (NEW FILE)

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs` (NEW)  
**Namespace:** `Fdp.Toolkit.Blueprints`

**Description:** Move the attach logic from `Hrot.Blueprints.Editor.Runtime.BlueprintAttachService` into core, adapted to accept `int blueprintId` instead of `BlueprintAsset asset`.

**Requirements:**

1. **Move `BlueprintAttachStatus` enum and `BlueprintAttachResult` record** into this new file. They are currently in `Hrot.Blueprints.Editor.Runtime` namespace; the core file uses `Fdp.Toolkit.Blueprints` namespace. The existing types reference only `BlackboardTier` which is already in core at `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlackboardTier.cs`.

2. **`AttachToEntity(EntityRepository world, BlueprintRegistry registry, int blueprintId, Entity entity)`** — same logic as the current editor service, but takes `int blueprintId` instead of `BlueprintAsset`:
   - Guard: throw `ArgumentNullException` for null world/registry
   - `registry.TryGetById(blueprintId, out var def)` → if not found, return `NotRegistered`
   - Check `def.Kind != BlueprintDispatchKind.Instance` → return `NotInstanceKind`
   - Idempotent: scan all three tiers via `TryFindExistingTier` (same logic as current); if found, return `AlreadyAttached`
   - `ChooseTier(def.StateSize)` → tier
   - `EnsureTierComponent(world, entity, tier)` — add the right `BlueprintBlackboard*` component if missing
   - `GetTierMemoryAndMeta` → `Initialize` → `TryAttach` → `InitDefault` (same sequence as current)
   - Return `Attached` with tier on success, `NoSlotAvailable` on failure

3. **`DetachFromEntity(EntityRepository world, int blueprintId, Entity entity)`** → `bool`:
   - Scan all three tiers for the blueprintId (same `TryFindExistingTier`-style scan but using `HasInitializedSlot`)
   - If found: call `BlueprintBlackboardPartitions.TryDetach(memory, blueprintId)`, return `true`
   - If not found on any tier: return `false`

4. **All private helpers** (`TryFindExistingTier`, `HasInitializedSlot`, `EnsureTierComponent`, `GetTierMemoryAndMeta`, `ChooseTier`) should be copied from the editor service (they use only core types: `EntityRepository`, `BlueprintBlackboard*` components, `BlueprintBlackboardPartitions`).

5. **No reference to `Hrot.Blueprints.Core.Assets.BlueprintAsset`** — the core service takes `int blueprintId` only. The `BlueprintAsset` → `blueprintId` conversion remains in the editor forwarder.

**Existing class to study for the `TryFindExistingTier`/`HasInitializedSlot`/etc helper pattern:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs` (lines 155-237)

**Design Reference:** `BLUEPRINT-SCENARIO-DESIGN.md` §3 (unified seam), §10 (module placement — unified seam lives in `Fdp.Toolkits.Blueprints`)

---

### Task 2: Reduce editor `BlueprintAttachService` to forwarder (MODIFY)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs` (MODIFY)

**Description:** The existing `BlueprintAttachService.AttachToEntity(world, registry, asset, entity)` becomes a thin forwarder:

```csharp
public static BlueprintAttachResult AttachToEntity(
    EntityRepository world, BlueprintRegistry registry, BlueprintAsset asset, Entity entity)
{
    if (asset is null) throw new ArgumentNullException(nameof(asset));
    int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
    return BlueprintInstanceService.AttachToEntity(world, registry, blueprintId, entity);
}
```

**Requirements:**
- Keep the method signature identical (backward-compatible API)
- Remove all private helpers that were moved to core (`TryFindExistingTier`, `HasInitializedSlot`, `EnsureTierComponent`, `GetTierMemoryAndMeta`, `ChooseTier`)
- Remove the `BlueprintAttachStatus` enum and `BlueprintAttachResult` record (moved to core)
- Add `using Fdp.Toolkit.Blueprints;` if needed (the types now live there)
- The `BlueprintAttachStatus` and `BlueprintAttachResult` types in the editor namespace should become `[Obsolete]` type-forwarders or simply removed — **since they were internal/public API of the editor, keep backward-compat by adding `using` aliases or check if any editor code references them by qualified name.**

**Important:** Check for all usages of `Hrot.Blueprints.Editor.Runtime.BlueprintAttachStatus` and `Hrot.Blueprints.Editor.Runtime.BlueprintAttachResult` in the codebase. If any code uses these types by fully-qualified or namespace-imported name, they need to either:
- Reference the new core types (`Fdp.Toolkit.Blueprints.BlueprintAttachStatus`/`BlueprintAttachResult`)
- OR keep thin type-forwarders in the editor namespace (preferred: remove old types, update references)

**Design Reference:** `BLUEPRINT-SCENARIO-DESIGN.md` §10 — "Editor BlueprintAttachService → thin forwarder to the core seam"

---

## 🧪 Testing Requirements

**Test files:** 
- Move/port tests from `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintAttachServiceTests.cs`
- Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintInstanceServiceTests.cs` (core seam tests)
- Update `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintAttachServiceTests.cs` (editor forwarder regression tests)

### Tests for the core `BlueprintInstanceService` (in `BlueprintInstanceServiceTests.cs`):

**SC1 — Fresh attach allocates slot + runs InitDefault:**
```csharp
[Fact]
public void AttachToEntity_FreshEntity_AllocatesSlot_And_RunsInitDefault()
{
    using var world = NewWorldWithTierComponents();
    var registry = new BlueprintRegistry();
    CounterDemoBlueprint.Register(registry);
    int bpId = CounterDemoBlueprint.BlueprintId;
    var entity = world.CreateEntity();
    var result = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);
    Assert.Equal(BlueprintAttachStatus.Attached, result.Status);
    // Verify InitDefault ran: Count field == 0
    Assert.Equal(0, ReadCount(world, entity));
}
```

**SC2 — Idempotent re-attach returns AlreadyAttached:**
```csharp
[Fact]
public void AttachToEntity_SecondCall_ReturnsAlreadyAttached()
{
    // Setup, attach, attach again...
    var second = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);
    Assert.Equal(BlueprintAttachStatus.AlreadyAttached, second.Status);
    Assert.Equal(1, SlotCount(world, entity)); // exactly one slot
}
```

**SC3 — Unregistered id returns NotRegistered:**
```csharp
[Fact]
public void AttachToEntity_UnregisteredId_ReturnsNotRegistered()
{
    int unknownId = 0xDEADBEEF;
    var result = BlueprintInstanceService.AttachToEntity(world, registry, unknownId, entity);
    Assert.Equal(BlueprintAttachStatus.NotRegistered, result.Status);
}
```

**SC4 — Non-Instance kind returns NotInstanceKind:**
```csharp
[Fact]
public void AttachToEntity_LibraryKind_ReturnsNotInstanceKind()
{
    // Register a Library-kind blueprint under the same id
    registry.RegisterLibrary(bpId, "TestLib");
    var result = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);
    Assert.Equal(BlueprintAttachStatus.NotInstanceKind, result.Status);
}
```

**SC5 — Detach frees slot and dense-compacts:**
```csharp
[Fact]
public void DetachFromEntity_FreesSlot_And_DenseCompacts()
{
    // Attach blueprints A, B, C (all small, same tier)
    // Assert slot count == 3
    // Detach B
    bool detached = BlueprintInstanceService.DetachFromEntity(world, bpIdB, entity);
    Assert.True(detached);
    // Assert slot count == 2
    // Assert A and C still present (their BlueprintIds still resolve via TryGetSlotOffset)
    Assert.True(HasSlot(world, entity, bpIdA));
    Assert.False(HasSlot(world, entity, bpIdB));
    Assert.True(HasSlot(world, entity, bpIdC));
}
```

**SC6 — Detach of absent id returns false (no throw):**
```csharp
[Fact]
public void DetachFromEntity_AbsentId_ReturnsFalse()
{
    bool detached = BlueprintInstanceService.DetachFromEntity(world, 0xDEADBEEF, entity);
    Assert.False(detached);
}
```

**SC7 — Attach→tick via core seam (end-to-end with BlueprintTestFixture):**
```csharp
[Theory]
[InlineData(1)]
[InlineData(5)]
public void AttachToEntity_ThenTick_CounterAdvances(int frames)
{
    using var fixture = new BlueprintTestFixture();
    CounterDemoBlueprint.Register(fixture.Registry);
    var entity = fixture.World.CreateEntity();
    var result = BlueprintInstanceService.AttachToEntity(
        fixture.World, fixture.Registry, CounterDemoBlueprint.BlueprintId, entity);
    Assert.Equal(BlueprintAttachStatus.Attached, result.Status);
    for (int i = 0; i < frames; i++)
        fixture.TickFrame(0.016f);
    Assert.Equal(frames, ReadCount(fixture.World, entity));
}
```

### Tests for the editor forwarder (`BlueprintAttachServiceTests.cs` — update existing):

**SC8 — Editor forwarder produces identical result to core seam:**
```csharp
[Fact]
public void Forwarder_ProducesSameResult_AsCoreSeam()
{
    // Call both the editor forwarder (with asset) and the core seam (with id)
    // Assert both results have the same Status and Tier
}
```

### Test quality rules:
- **Every test must drive the real production path** — no mocks for the unit under test.
- **Assert on concrete values** (Status enum values, slot counts, BlueprintIds), not on strings or logs.
- **Use existing helpers:** `CounterDemoBlueprint`, `BlueprintTestFixture`, `BlueprintRuntimeWiring.RegisterTierComponents`, `NewWorldWithTierComponents()`.
- **Helpful helper to extract:** `HasSlot(EntityRepository world, Entity entity, int blueprintId)` → scan tiers via `TryGetSlotOffset`.
- **Do NOT regenerate snapshots** (`BLUEPRINT_REGENERATE_SNAPSHOTS` must be unset).

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs` created with `AttachToEntity` and `DetachFromEntity`
- [ ] `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs` reduced to thin forwarder
- [ ] No reference from `Fdp.Toolkits` to `Hrot.Blueprints.Editor` (verify: `dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` succeeds without editor dependency)
- [ ] All 8 specified tests pass (SC1–SC8)
- [ ] All pre-existing tests in `Hrot.Blueprints.Tests` still pass (0 net-new failures)
- [ ] Build: 0 errors across the solution
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

1. **Don't forget `NoSlotAvailable` path** — when `TryAttach` returns false, the result must be `NoSlotAvailable`, not a crash.
2. **Don't remove `BlueprintAttachStatus`/`BlueprintAttachResult` without checking references** — search the codebase for usages first.
3. **`BlueprintRuntimeWiring.RegisterTierComponents`** is in the editor assembly — don't move it; tests in `Hrot.Blueprints.Tests` can still use it since they reference the editor assembly.
4. **The `CounterDemoBlueprint.BlueprintId`** is a public const int — use it, don't recompute.
5. **Dense-compact verification:** after detaching B from [A,B,C], the slot count must be 2, not 3 with a hole. `GetSlotCount` returns the accurate count after `TryDetach` (which dense-compacts).

---

## 📊 Report Requirements

**Focus on Developer Insights:**
- **Q1:** What issues did you encounter? How did you resolve them?
- **Q2:** What design decisions did you make beyond the spec? What alternatives did you consider?
- **Q3:** What edge cases did you discover that weren't in the instructions?
- **Q4:** Any callers of `BlueprintAttachStatus`/`BlueprintAttachResult` by fully-qualified name that needed updating? List them.
- **Q5:** Suggested commit message for this batch.

---

## 📚 Reference Materials
- **Design:** `.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §3, §10
- **Task Details:** `.dev/blueprint-scenario/TASK-DETAIL.md` — BSA-102
- **Task Tracker:** `.dev/blueprint-scenario/TASK-TRACKER.md`
- **Core partition API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`
- **Existing editor service:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs`
- **Existing tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintAttachServiceTests.cs`
