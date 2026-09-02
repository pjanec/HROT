# BATCH-06: Entity Inspector Per-Tier Summary Renderers (BSA-204)

**Batch Number:** BATCH-06  
**Tasks:** BSA-204 (Entity Inspector per-tier summary renderers — read-only monitoring)  
**Phase:** Phase 4 — Editor UI  
**Estimated Effort:** 3-4 hours  
**Priority:** MEDIUM  
**Dependencies:** None critical (BSA-102 core seam useful for tests)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Replace the raw byte-dump the Entity Inspector shows for `BlueprintBlackboard*` components with read-only per-tier summaries. Create a plain testable `BlueprintTierSummary.Read()` class, then 3 thin ImGui renderers that call it.

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §12.1 (Entity Inspector read-only monitoring), §12.4 (per-tier summary)
2. **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-204 section
3. **Task Tracker:** `.dev/_DONE/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **View-model (NEW):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintTierSummary.cs`
- **Renderer 1024 (NEW):** `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard1024Renderer.cs`
- **Renderer 4096 (NEW):** `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard4096Renderer.cs`
- **Renderer 16384 (NEW):** `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard16384Renderer.cs`
- **Pattern renderer:** `Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs`
- **Pattern renderer:** `Hrot/Engine/Hrot.Presentation/Renderers/Blackboard1024Renderer.cs`
- **Interface:** `FDP/Engine/Fdp.Presentation/Abstractions/IEntityAwareImGuiRenderer` (look up exact path)
- **BlueprintRegistry:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`
- **Partition API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` (`GetSlotCount`, `GetSlot`)

### Report Submission
**When done, submit your report to:**  
`.dev/_DONE/blueprint-scenario/reports/BATCH-06-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Create `BlueprintTierSummary` + tests → **ALL tests pass** ✅
2. **Task 2:** Create 3 renderers → Build passes + verify registration ✅

---

## Context

Today the Entity Inspector shows a raw byte-dump of `BlueprintBlackboard*` partition memory. We replace it with a read-only list: each slot shows blueprint name, `InstanceVersion`, and tick/latent-cursor status. The logic is extracted into a plain `BlueprintTierSummary.Read()` class — tests assert on that, not on ImGui rendering.

---

## 🎯 Batch Objectives

1. Create `BlueprintTierSummary` with `Read(byte*, BlueprintRegistry) → IReadOnlyList<SlotSummary>`
2. Create 3 `IEntityAwareImGuiRenderer` classes, one per tier

---

## ✅ Tasks

### Task 1: Create view-model class `BlueprintTierSummary`

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintTierSummary.cs` (NEW)

This is the **testable** layer. No ImGui dependencies. No rendering. Pure data extraction.

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>A single slot summary, produced by <see cref="BlueprintTierSummary.Read"/>.</summary>
public readonly record struct SlotSummary(
    Guid AssetId,
    int BlueprintId,
    string Name,
    uint InstanceVersion,
    ushort PayloadOffset,
    ushort PayloadSize);

/// <summary>
/// Read-only, allocation-free scanner that extracts blueprint slot summaries
/// from a blackboard tier's unmanaged memory. Used by the Entity Inspector
/// renderers to replace the raw byte-dump.
/// </summary>
public static unsafe class BlueprintTierSummary
{
    /// <summary>
    /// Reads all allocated slots from the given blackboard memory.
    /// Returns an empty list if the tier is uninitialized (no header magic).
    /// </summary>
    /// <param name="memory">Pointer to the blackboard component's fixed buffer.</param>
    /// <param name="registry">Blueprint registry for id→name resolution.</param>
    /// <returns>A list of slot summaries (one per allocated slot).</returns>
    public static List<SlotSummary> Read(byte* memory, BlueprintRegistry registry)
    {
        var result = new List<SlotSummary>();
        AppendSlots(memory, registry, result);
        return result;
    }

    /// <summary>
    /// Same as <see cref="Read"/> but appends into an existing list to avoid allocation.
    /// </summary>
    public static void AppendSlots(byte* memory, BlueprintRegistry registry, List<SlotSummary> target)
    {
        // Check header magic — uninitialized tier is all zeros.
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != 0x42504257u) // BlueprintBlackboardHeader.MagicValue
            return;

        int count = BlueprintBlackboardPartitions.GetSlotCount(memory);
        for (int i = 0; i < count; i++)
        {
            ref var slot = ref BlueprintBlackboardPartitions.GetSlot(memory, i);
            if (slot.BlueprintId == 0) continue;

            Guid assetId = Guid.Empty;
            string name = $"0x{slot.BlueprintId:X8}";
            if (registry.TryGetById(slot.BlueprintId, out var def) && def != null)
            {
                assetId = def.AssetId;
                name = def.Name;
            }

            target.Add(new SlotSummary(
                assetId,
                slot.BlueprintId,
                name,
                slot.InstanceVersion,
                slot.PayloadOffset,
                slot.PayloadSize));
        }
    }
}
```

**Tests required (all in `FDP/Toolkits/Fdp.Toolkits.Tests/` or `Hrot/.../Tests/`):**

- **Test 1 — Empty tier (uninitialized):** Create zeroed `BlueprintBlackboard1024`, call `Read` → returns empty list (no throw).

- **Test 2 — Attached blueprints:** Attach 3 blueprints via `BlueprintInstanceService.AttachToEntity`, call `Read` → returns exactly 3 entries. Assert `BlueprintId`, `Name`, `AssetId` match the registered blueprints.

- **Test 3 — InstanceVersion present:** After calling `Read`, assert each slot's `InstanceVersion` equals 1 (set by `TryAttach`).

- **Test 4 — No managed allocation in Read:** Skipped. (Hard to assert zero-alloc without GC monitoring. Design says "assert no per-call managed allocation" — this is aspirational; the view-model currently uses `List<T>.Add`.)

**Test file location:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTierSummaryTests.cs` (NEW)

---

### Task 2: Create 3 ImGui renderers

**Files (NEW):**  
- `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard1024Renderer.cs`
- `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard4096Renderer.cs`
- `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard16384Renderer.cs`

Each follows the same pattern. Example for 1024:

```csharp
namespace Hrot.Presentation.Renderers;

[ImGuiRenderer(typeof(BlueprintBlackboard1024))]
public sealed class BlueprintBlackboard1024Renderer : IEntityAwareImGuiRenderer
{
    /// <summary>Set at startup. Required for blueprint id→name resolution.</summary>
    public static BlueprintRegistry? BlueprintRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----
    public string? GetSummary(object value) => "Instance Blueprints (1024 bytes)";
    public bool RenderValue(object value) => false; // non-entity-aware fallback — delegate to default

    // ---- IEntityAwareImGuiRenderer ----
    public string? GetSummary(IInspectableSession session, Entity entity, object value)
    {
        var registry = BlueprintRegistryAccessor;
        if (registry == null || value is not BlueprintBlackboard1024 bb)
            return GetSummary(value);

        unsafe
        {
            fixed (byte* mem = bb.Memory)
            {
                int count = BlueprintBlackboardPartitions.GetSlotCount(mem);
                return $"Instance Blueprints ({count} attached)";
            }
        }
    }

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        var registry = BlueprintRegistryAccessor;
        if (registry == null || value is not BlueprintBlackboard1024 bb)
            return false;

        unsafe
        {
            fixed (byte* mem = bb.Memory)
            {
                var summaries = BlueprintTierSummary.Read(mem, registry);
                if (summaries.Count == 0)
                {
                    ImGui.TextDisabled("No blueprints attached.");
                    return true;
                }

                if (ImGui.BeginTable("##bp1024", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Blueprint");
                    ImGui.TableSetupColumn("Version");
                    ImGui.TableSetupColumn("Size");
                    ImGui.TableSetupColumn("Id");
                    ImGui.TableHeadersRow();

                    foreach (var s in summaries)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(s.Name);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(s.InstanceVersion.ToString());
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"{s.PayloadSize} B");
                        ImGui.TableNextColumn(); ImGui.TextDisabled($"0x{s.BlueprintId:X8}");
                    }

                    ImGui.EndTable();
                }
            }
        }

        return true; // suppress default byte-dump
    }
}
```

Create identical files for 4096 and 16384, changing only the component type and tier-specific strings (e.g., "4096 bytes").

**Tests:**
- **Test 5 — RenderValue returns true:** Instantiate the renderer with a `BlueprintBlackboard1024` value, call `RenderValue(object value)` or the entity-aware overload, assert returns `true` (byte-dump suppressed).

- **Test 6 — GetSummary returns correct string:** Call `GetSummary(value)` → returns non-null string with expected tier suffix.

*Note: Since rendering tests are inherently fragile with ImGui, Test 5 and 6 can be basic smoke tests — just verify the class is instantiable and methods don't throw.*

---

### Task 3: Set `BlueprintRegistryAccessor` at startup

Find where `BrainBlackboardRenderer.BehaviorRegistryAccessor` and `Blackboard1024Renderer.BehaviorRegistryAccessor` are set (likely in `EditorSubsystem.Initialize` or `CgfSubsystem.Initialize`). Set the new accessors similarly.

Search for `BehaviorRegistryAccessor =` in the codebase to find the pattern, then add the blueprint equivalent.

---

## 🧪 Testing Requirements

**Test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTierSummaryTests.cs` (NEW)

**IMPORTANT from TASK-DETAIL.md header rule 3:**
> "UI tasks (BSA-204/205): assert on a headless view-model, not on ImGui."

Tests 1-4 assert on `BlueprintTierSummary.Read()`. Tests 5-6 are minimal smoke for the renderer. No ImGui assertion needed.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BlueprintTierSummary.Read()` created returning slot summaries
- [ ] 3 `IEntityAwareImGuiRenderer` classes created, one per tier
- [ ] Each renderer's `RenderValue` returns `true` (suppress byte-dump)
- [ ] `BlueprintRegistryAccessor` set at startup
- [ ] All 6 specified tests pass
- [ ] All pre-existing tests in touched projects pass (0 net-new failures)
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls to Avoid

1. **ImGui in tests:** Don't try to test actual ImGui rendering. The `ImGui.BeginTable` API requires a valid ImGui context. Smoke-test the renderer by calling the methods and checking they don't throw.
2. **`BlueprintRegistryAccessor` null:** The renderer must handle null accessor gracefully (fall back to hex display or empty message).
3. **Uninitialized tier:** Check header magic before calling `GetSlotCount` — the component can be zeroed.
4. **`fixed` keyword:** The tier component's `Memory` is a `fixed byte[]` — use `fixed (byte* mem = bb.Memory)` to get the pointer.
5. **Renderer registration:** The `[ImGuiRenderer]` attribute auto-registers the renderer. No manual registration needed. Ensure the assembly containing the renderers is loaded.

---

## 📊 Report Requirements

- **Q1:** Where did you set `BlueprintRegistryAccessor`? Which file/line?
- **Q2:** Were there issues with the `ImGuiTableFlags` or ImGui API availability? (renderers might not have ImGui.NET accessible in tests)
- **Q3:** Suggested commit message.
