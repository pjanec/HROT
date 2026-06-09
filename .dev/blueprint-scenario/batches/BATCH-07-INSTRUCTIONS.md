# BATCH-07: Entity Blueprints Authoring Panel (BSA-205)

**Batch Number:** BATCH-07  
**Tasks:** BSA-205 ("Entity Blueprints" authoring panel — staged diff, paused/running commit)  
**Phase:** Phase 4 — Editor UI  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** BSA-102 (core seam + CopyToLargerTier), BSA-301 (events for running commit)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Build the "Entity Blueprints" panel — a dedicated editor window for assigning/removing Instance blueprints on the selected entity. Uses a detached view-model (`EntityBlueprintsEditModel`) with headless ImGui-free logic for testability. Commits via the core seam (paused: synchronous with tier upgrade) or via BSA-301 events (running).

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §12.2 (panel design), §12.3 (commit — two timings), §2 (authoring invariant), §5 (tier pre-provisioning)
2. **Task Details:** `.dev/blueprint-scenario/TASK-DETAIL.md` — BSA-205 section
3. **Task Tracker:** `.dev/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **Edit model (NEW):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsEditModel.cs`
- **Panel (NEW):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsPanel.cs`
- **Pattern — editor window:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs`
- **Core seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Partition API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` (CopyToLargerTier, TryAttach, TryDetach, GetSlotCount, GetSlot)
- **BlueprintPickerSources:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs`
- **Events:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Events/BlueprintLifecycleEvents.cs`
- **Tier components:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs` (and 4096, 16384)
- **Registry:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`

### Report Submission
**When done, submit your report to:**  
`.dev/blueprint-scenario/reports/BATCH-07-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Create `EntityBlueprintsEditModel` + tests → **ALL tests pass** ✅
2. **Task 2:** Create `EntityBlueprintsPanel` (thin UI shell) → **Build + smoke test** ✅
3. **Task 3:** Register panel in editor → **Verify it opens** ✅

---

## Context

The Entity Inspector's per-tier renderers (BSA-204) show what's attached, but they're read-only and fragmented across three tier components. The "Entity Blueprints" panel is the **unified authoring surface** — it shows all blueprints across all tiers in one list, lets you stage adds/removes (Intent), shows a live diff vs. Reality, and commits mutations through the core seam.

---

## 🎯 Batch Objectives

1. `EntityBlueprintsEditModel` — headless, testable logic (Reality, Intent, Diff, Projection, BuildCommitPlan)
2. `EntityBlueprintsPanel` — ImGui window rendering the model
3. Registration in editor window system

---

## ✅ Tasks

### Task 1: Create `EntityBlueprintsEditModel` (headless view-model)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsEditModel.cs` (NEW)

All logic, no ImGui. Tests assert on this class.

**Types:**

```csharp
namespace Hrot.Blueprints.Editor.EntityBlueprints;

public enum UsageStatus { Ok, UpgradeNeeded, OverCeiling }

public readonly record struct Projection(
    int Slots, int Bytes, BlackboardTier Tier, UsageStatus Status);

public sealed class DiffResult
{
    public List<BlueprintAssignmentDto> Added { get; } = new();
    public List<BlueprintAssignmentDto> Removed { get; } = new();
}

public enum CommitTiming { Paused, Running }

public sealed class CommitPlan
{
    // Paused path:
    public BlackboardTier? UpgradeToTier { get; init; }
    public List<int> DetachBlueprintIds { get; } = new();
    public List<int> AttachBlueprintIds { get; } = new();

    // Running path:
    public List<RemoveInstanceBlueprintEvent> RemoveEvents { get; } = new();
    public List<AttachInstanceBlueprintEvent> AttachEvents { get; } = new();
}
```

**Model class:**

```csharp
public sealed class EntityBlueprintsEditModel
{
    private readonly EntityRepository _repo;
    private readonly BlueprintRegistry _registry;
    private readonly Entity _entity;

    // Public state
    public List<SlotSummary> Reality { get; } = new();
    public List<BlueprintAssignmentDto> Intent { get; } = new();
    public DiffResult Diff { get; } = new();
    public Projection Projection { get; private set; }

    public EntityBlueprintsEditModel(EntityRepository repo, BlueprintRegistry registry, Entity entity)
    {
        _repo = repo;
        _registry = registry;
        _entity = entity;
    }

    /// <summary>Scan all three tiers into Reality. Call every frame to keep live.</summary>
    public unsafe void RefreshReality()
    {
        Reality.Clear();
        if (_repo.HasComponent<BlueprintBlackboard1024>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(_entity);
            fixed (byte* mem = bb.Memory)
                BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
        if (_repo.HasComponent<BlueprintBlackboard4096>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard4096>(_entity);
            fixed (byte* mem = bb.Memory)
                BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
        if (_repo.HasComponent<BlueprintBlackboard16384>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard16384>(_entity);
            fixed (byte* mem = bb.Memory)
                BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
    }

    /// <summary>Stage an add. Does NOT mutate live memory.</summary>
    public void StageAdd(BlueprintAssignmentDto dto) => Intent.Add(dto);

    /// <summary>Stage a remove. Does NOT mutate live memory.</summary>
    public void StageRemove(BlueprintAssignmentDto dto) => Intent.Remove(dto);

    /// <summary>Clear all staged changes.</summary>
    public void RevertAll() => Intent.Clear();

    /// <summary>Compute Diff of Intent vs Reality.</summary>
    public DiffResult ComputeDiff()
    {
        var diff = new DiffResult();
        // Reality AssetIds as set
        var realityIds = new HashSet<Guid>(Reality.Select(s => s.AssetId));
        // Intent AssetIds as set
        var intentIds = new HashSet<Guid>(Intent.Select(d => d.AssetId));

        foreach (var dto in Intent)
        {
            if (!realityIds.Contains(dto.AssetId))
                diff.Added.Add(dto);
        }
        foreach (var slot in Reality)
        {
            if (!intentIds.Contains(slot.AssetId))
                diff.Removed.Add(new BlueprintAssignmentDto { AssetId = slot.AssetId });
        }
        return diff;
    }

    /// <summary>Compute projected usage after applying staged Intent.</summary>
    public Projection ComputeProjection()
    {
        // Total = Reality + staged adds - staged removes
        var realityIds = new HashSet<Guid>(Reality.Select(s => s.AssetId));
        var intentIds = new HashSet<Guid>(Intent.Select(d => d.AssetId));

        int totalSlots = Reality.Count;
        int totalBytes = Reality.Sum(s => s.PayloadSize);

        foreach (var dto in Intent)
        {
            if (!realityIds.Contains(dto.AssetId))
            {
                totalSlots++;
                // Look up def to get StateSize
                int bpId = BlueprintIdHash.Compute(dto.AssetId);
                if (_registry.TryGetById(bpId, out var def) && def != null)
                    totalBytes += def.StateSize;
            }
        }
        foreach (var slot in Reality)
        {
            if (!intentIds.Contains(slot.AssetId))
            {
                totalSlots--;
                totalBytes -= slot.PayloadSize;
            }
        }

        // Pick tier + status
        BlackboardTier tier = ChooseTierFromAggregate(totalSlots, totalBytes);
        UsageStatus status = UsageStatus.Ok;
        if (totalSlots > BlueprintBlackboard16384.MaxSlots || totalBytes > BlueprintBlackboard16384.PayloadSize)
        {
            tier = BlackboardTier.B16384;
            status = UsageStatus.OverCeiling;
        }
        else
        {
            // Determine current tier
            BlackboardTier currentTier = GetCurrentTier();
            if (tier > currentTier) status = UsageStatus.UpgradeNeeded;
        }

        return new Projection(totalSlots, totalBytes, tier, status);
    }

    /// <summary>Build the commit plan for paused or running timing.</summary>
    public CommitPlan BuildCommitPlan(CommitTiming timing)
    {
        var diff = ComputeDiff();
        var proj = ComputeProjection();
        var plan = new CommitPlan();

        if (timing == CommitTiming.Paused)
        {
            // Check if tier upgrade needed
            BlackboardTier currentTier = GetCurrentTier();
            if (proj.Tier > currentTier)
                plan.UpgradeToTier = proj.Tier;

            // Detaches
            foreach (var dto in diff.Removed)
                plan.DetachBlueprintIds.Add(BlueprintIdHash.Compute(dto.AssetId));

            // Attaches
            foreach (var dto in diff.Added)
                plan.AttachBlueprintIds.Add(BlueprintIdHash.Compute(dto.AssetId));
        }
        else
        {
            // Running — publish events (remove-before-add per BSA-301)
            foreach (var dto in diff.Removed)
                plan.RemoveEvents.Add(new RemoveInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(dto.AssetId),
                });

            foreach (var dto in diff.Added)
                plan.AttachEvents.Add(new AttachInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(dto.AssetId),
                });
        }

        return plan;
    }
}
```

**Helper methods needed:**
- `GetCurrentTier()` — scan which tier component exists on entity (return highest)
- `ChooseTierFromAggregate(slots, bytes)` — same logic as BSA-203

**Tests (NEW file: `Hrot/.../Tests/Editor/EntityBlueprintsEditModelTests.cs`):**

Per TASK-DETAIL header rule 3, ALL tests assert on the model, not ImGui.

- **Test 1 — Reality:** Attach 2 blueprints → `RefreshReality()` → `model.Reality.Count == 2` with correct ids+names. **Also:** `RefreshReality` twice → Reality is the same (idempotent scan).

- **Test 2 — Diff staging:** Stage 1 add + 1 remove → `ComputeDiff()` → `Diff.Added.Count == 1 && Diff.Removed.Count == 1`. **Also:** the live slot table is byte-identical to before staging (assert no mutation until Apply). Compare `GetSlotCount` before and after staging.

- **Test 3 — Projection Ok:** Attach 3 small blueprints → `ComputeProjection()` → `Status == Ok`, `Tier == B1024`.

- **Test 4 — Projection UpgradeNeeded:** Attach 3, stage 2 more that push total > 928 bytes → `Status == UpgradeNeeded && Projection.Tier == B4096`.

- **Test 5 — Projection OverCeiling:** Stage 20 blueprints → `Status == OverCeiling`.

- **Test 6 — RevertAll:** Stage 2 adds → `RevertAll()` → `Intent.Count == 0`, `ComputeDiff()` has zero adds and zero removes.

- **Test 7 — Paused commit plan:** Sim paused, stage add + remove → `BuildCommitPlan(Paused)` → `plan.UpgradeToTier` correct, `plan.DetachBlueprintIds` contains remove, `plan.AttachBlueprintIds` contains add.

- **Test 8 — Paused commit + tier upgrade execution:** Sim paused, Apply adds that overflow current tier → after Apply the entity has exactly one (larger) `BlueprintBlackboard*` component, old tier component absent (`Assert.False(repo.HasComponent<OldTier>(e))`), every intended BlueprintId occupies exactly one slot (assert counts; no duplicates).

- **Test 9 — Running commit plan:** Sim running, stage same-size swap (remove X, add Y) → `BuildCommitPlan(Running)` → plan has 1 RemoveEvent then 1 AttachEvent. **Also:** the live blackboard is byte-identical during the planning frame (no mid-tick mutation — assert `GetSlotCount` unchanged after building the plan).

- **Test 10 — Invariant (§2):** Attach via model→Apply, run a preview, then `BlueprintStateTranslator.Extract` → produced DTOs contain exactly the assigned `AssetId`s and no `Overrides`/drift bytes (assert array equality + all `Overrides == null`).

---

### Task 2: Create `EntityBlueprintsPanel` (ImGui window)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsPanel.cs` (NEW)

Thin UI shell that renders the model from Task 1. Follow existing window patterns (e.g., `DebugPanelWindow.cs`).

Core structure:
```csharp
public sealed class EntityBlueprintsPanel
{
    private readonly EntityBlueprintsEditModel _model;
    private bool _isRunning; // true = sim running

    public void DrawUI()
    {
        // Header: target entity, sim state, current tier
        // Projected Usage bar (yellow if upgrade needed, red + disable Apply if OverCeiling)
        // [+ Add Blueprint...] button → BlueprintPickerSources filtered to Instance
        // Table: Name | Status (Active/Removed/Added) | Size | Action buttons
        // Footer: [Apply] [Revert All]
        
        if (ImGui.Button("Apply"))
        {
            var timing = _isRunning ? CommitTiming.Running : CommitTiming.Paused;
            var plan = _model.BuildCommitPlan(timing);
            ExecuteCommitPlan(plan, timing);
            _model.RevertAll();
        }
    }

    private void ExecuteCommitPlan(CommitPlan plan, CommitTiming timing)
    {
        if (timing == CommitTiming.Paused)
        {
            // Tier upgrade first
            if (plan.UpgradeToTier.HasValue)
            {
                UpgradeTier(plan.UpgradeToTier.Value);
            }
            // Detaches then attaches (remove-before-add)
            foreach (int bpId in plan.DetachBlueprintIds)
                BlueprintInstanceService.DetachFromEntity(...);
            foreach (int bpId in plan.AttachBlueprintIds)
                BlueprintInstanceService.AttachToEntity(...);
        }
        else
        {
            // Publish events (BSA-301 will apply next Input phase, removes-before-adds)
            foreach (var evt in plan.RemoveEvents)
                world.Bus.Publish(evt);
            foreach (var evt in plan.AttachEvents)
                world.Bus.Publish(evt);
        }
    }

    private unsafe void UpgradeTier(BlackboardTier newTier)
    {
        // 1. Get memory from old tier
        // 2. Add new tier component
        // 3. CopyToLargerTier(old, new)
        // 4. Remove old tier component (CRITICAL — else double-tick)
        // Study BlueprintMaintenanceSystem for the exact pattern
    }
}
```

**+ Add Blueprint… button:** Use `BlueprintPickerSources` to show Instance-only blueprints. Look at how existing pickers work (e.g., in `BlueprintDocumentFactory.cs`).

---

### Task 3: Register the panel

Find where editor windows are registered (likely `EditorSubsystem.cs` or `BlueprintWindowRegistrar.cs`). Register `EntityBlueprintsPanel` so it appears in the Window menu or toolbar. Study how `DebugPanelWindow` or `BlueprintVariablesWindow` is registered.

---

## 🧪 Testing Requirements

**Test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EntityBlueprintsEditModelTests.cs` (NEW)

Per TASK-DETAIL header rule 3: assert on view-model, not ImGui. Tests 1-10 above.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `EntityBlueprintsEditModel` created with Reality/Intent/Diff/Projection/CommitPlan
- [ ] `EntityBlueprintsPanel` renders the model via ImGui
- [ ] Panel registered in editor window system
- [ ] Paused commit: tier upgrade via CopyToLargerTier, old tier removed
- [ ] Running commit: publishes BSA-301 events (removes-before-adds)
- [ ] + Add Blueprint… uses existing BlueprintPickerSources
- [ ] All 10 specified tests pass
- [ ] 0 net-new failures
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls to Avoid

1. **Don't mutate live memory during staging** — Intent is local. Apply is the only mutation point.
2. **Tier upgrade must remove old tier** — leaving both tiers attached makes `BlueprintTickSystem` tick blueprints twice and `Extract` emit duplicates.
3. **Add button must not be inside `DrawUI` mutation** — apply commit at a frame-safe point, not mid-DrawUI.
4. **BlueprintPickerSources only shows Instance kind** — filter appropriately.
5. **`CopyToLargerTier` handles all data migration** — slot table, payload bytes, free list. You just need to call it correctly then remove old tier.

---

## 📊 Report Requirements

- **Q1:** How did you register the panel in the editor? What menu/shortcut opens it?
- **Q2:** Where is the tier upgrade logic? Did you reuse `BlueprintMaintenanceSystem`'s approach?
- **Q3:** How did you integrate `BlueprintPickerSources` for the +Add button?
- **Q4:** Suggested commit message.
