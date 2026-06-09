# BATCH-05: SharedAiAction Lifecycle Nodes (BSA-302)

**Batch Number:** BATCH-05  
**Tasks:** BSA-302 (`[SharedAiAction]` `BlueprintLifecycleLibrary` node(s) publishing BSA-301 events)  
**Phase:** Phase 3 — Dynamic / mid-runtime assignment  
**Estimated Effort:** 3-4 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-04 (BSA-301 — events + ingress system)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Create `[SharedAiAction]` static methods that blueprint graphs can use to attach/remove/replace Instance blueprints at runtime. These methods publish the BSA-301 events to `world.Bus` — the actual attach/detach happens in the next frame's Input phase (one-frame latency).

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §8 (action-node access)
2. **Task Details:** `.dev/blueprint-scenario/TASK-DETAIL.md` — BSA-302 section
3. **Task Tracker:** `.dev/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **Action library (NEW):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Actions/BlueprintLifecycleLibrary.cs`
- **Pattern — SharedAiAction:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Demo/DemoEnumAction.cs` (lines 107-157)
- **InlineActionLowering:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InlineActionLowering.cs`
- **Events (publish to):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Events/BlueprintLifecycleEvents.cs`
- **Core seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **FdpEventBus:** `repo.Bus.Publish<T>()` where `T : unmanaged`

### Report Submission
**When done, submit your report to:**  
`.dev/blueprint-scenario/reports/BATCH-05-REPORT.md`

---

## Context

Blueprint graphs need action nodes that let a running blueprint attach/remove/replace OTHER Instance blueprints on entities at runtime. The mechanism is:
1. Action node publishes a BSA-301 event to `world.Bus`
2. `BlueprintEventIngressSystem` (Input phase) consumes the event next frame
3. Attach/detach happens via the BSA-102 core seam

This uses the proven `[SharedAiAction]` pattern — the compiler's `InlineActionLowering` (line 33) emits `global::{ActionFqn}(ref __p_N, self, world)`.

---

## 🎯 Batch Objectives

Create `BlueprintLifecycleLibrary` with 3 `[SharedAiAction]` methods: Attach, Remove, Replace. Each publishes the corresponding BSA-301 event.

---

## ✅ Tasks

### Task 1: Create DTO structs and BlackboardSlot

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Actions/BlueprintLifecycleLibrary.cs` (NEW)

The `[SharedAiAction]` attribute requires:
- A **DTO struct** (e.g., `AttachInstanceBlueprintParams`) — blittable, fields become data-IN pins in the node editor
- A **BlackboardSlot struct** (e.g., `BlueprintInstanceSlot`) — wrapper whose `Params` field type matches the DTO
- A **static method** with signature `(ref Dto, Entity self, EntityRepository world) → NodeStatus`

Study `DemoEnumAction.cs` lines 90-157 for the complete pattern, then mirror:

```csharp
namespace Fdp.Toolkit.Blueprints.Actions;

// ── DTOs (become data-IN pins) ─────────────────────────────────────────

/// <summary>Params for the AttachInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AttachInstanceBlueprintParams
{
    /// <summary>The runtime BlueprintId to attach. Use BlueprintIdHash.Compute(assetId).</summary>
    public int BlueprintId;
    
    /// <summary>Optional target entity. Defaults to self (0 = target self).</summary>
    public long TargetEntityPacked; // 0 means self
}

/// <summary>Params for the RemoveInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RemoveInstanceBlueprintParams
{
    public int BlueprintId;
    public long TargetEntityPacked;
}

/// <summary>Params for the ReplaceInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ReplaceInstanceBlueprintParams
{
    public int OldBlueprintId;
    public int NewBlueprintId;
    public long TargetEntityPacked;
}

// ── BlackboardSlot (required by [SharedAiAction] attribute) ────────────

[StructLayout(LayoutKind.Sequential)]
public struct BlueprintInstanceSlot
{
    // This field type MUST match the ref param type of each [SharedAiAction] method.
    // The ActionSchemaExporter validates this match.
    // All three DTOs have the same layout pattern but different semantics.
    // Use the largest DTO (Replace) for the slot since it covers all fields.
    public ReplaceInstanceBlueprintParams Params;
}

// ── Action library ─────────────────────────────────────────────────────

/// <summary>
/// <c>[SharedAiAction]</c> methods for runtime blueprint lifecycle operations.
/// Each publishes a BSA-301 event to <c>world.Bus</c>; the actual attach/detach
/// happens in the next frame's Input phase via <c>BlueprintEventIngressSystem</c>.
/// </summary>
public static class BlueprintLifecycleLibrary
{
    [SharedAiAction(typeof(BlueprintInstanceSlot), nameof(BlueprintInstanceSlot.Params))]
    public static NodeStatus AttachInstanceBlueprint(
        ref AttachInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        world.Bus.Publish(new AttachInstanceBlueprintEvent
        {
            Entity = ResolveTarget(dto.TargetEntityPacked, self),
            BlueprintId = dto.BlueprintId,
        });
        return NodeStatus.Success;
    }

    [SharedAiAction(typeof(BlueprintInstanceSlot), nameof(BlueprintInstanceSlot.Params))]
    public static NodeStatus RemoveInstanceBlueprint(
        ref RemoveInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        world.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = ResolveTarget(dto.TargetEntityPacked, self),
            BlueprintId = dto.BlueprintId,
        });
        return NodeStatus.Success;
    }

    [SharedAiAction(typeof(BlueprintInstanceSlot), nameof(BlueprintInstanceSlot.Params))]
    public static NodeStatus ReplaceInstanceBlueprint(
        ref ReplaceInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        var target = ResolveTarget(dto.TargetEntityPacked, self);
        world.Bus.Publish(new ReplaceInstanceBlueprintEvent
        {
            Entity = target,
            OldBlueprintId = dto.OldBlueprintId,
            NewBlueprintId = dto.NewBlueprintId,
        });
        return NodeStatus.Success;
    }

    private static Entity ResolveTarget(long packed, Entity self)
        => packed == 0 ? self : new Entity(packed);
}
```

**Important notes:**
- `NodeStatus` is from `Fbt.Kernel` namespace (`Fbt.NodeStatus` or `Fhsm.Kernel.Data.NodeStatus`). Check which one the `SharedAiAction` lowering expects.
- The `SharedAiActionAttribute` constructor takes `(Type slotType, string paramsFieldName)`. The `slotType` must be a struct with a field named `paramsFieldName` whose type matches the `ref` DTO param.
- **BUT** — the design doc §8 shows a simpler form: just `[SharedAiAction]` without parameters. Check the attribute definition: `SharedAiActionAttribute` may have a parameterless constructor variant. If it does, use it. If not, use the `(typeof(Slot), "Params")` form as shown.

**⚠️ Verify the attribute signature first:** Find `SharedAiActionAttribute` in the codebase and confirm which constructor overloads exist. The design says plain `[SharedAiAction]` — verify this is valid.

**Tests (in a new test file):**
- **Test 1 — Method signature:** Reflection-verify each method is static, returns `NodeStatus`, has `(ref Dto, Entity, EntityRepository)` params, has `[SharedAiAction]` attribute.
- **Test 2 — Attach publishes correct event:** Call `AttachInstanceBlueprint(ref dto, self, world)`, read `world.Bus.Read<AttachInstanceBlueprintEvent>()`, assert Entity and BlueprintId match.
- **Test 3 — Remove publishes correct event:** Same pattern for Remove.
- **Test 4 — Replace publishes correct event:** Same pattern for Replace.
- **Test 5 — Target resolution:** Call with `TargetEntityPacked = 0` → event targets self. Call with specific packed value → event targets that entity.
- **Test 6 — Integration end-to-end:** Attach a blueprint via the action method, tick the ingress system (simulating next frame's Input phase), verify the blueprint is actually attached to the entity. This tests the full pipeline: action → event → ingress → core seam.

---

### Task 2: (Optional) Verify editor palette discovery

The `ActionSchemaExporter` reflects all loaded assemblies for `[SharedAiAction]` methods. If the assembly containing `BlueprintLifecycleLibrary` is loaded in the editor, the nodes should automatically appear in the blueprint action palette.

**Verify:** Build the project, check if `BlueprintNodePaletteEntries.cs` or the `ActionSchemaExporter` picks up the new action FQNs. If manual registration is needed, add entries following the existing pattern.

Search for how `DemoSharedActions.AlertNearbyUnits` appears in the palette to understand the registration mechanism.

---

## 🧪 Testing Requirements

**Test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintLifecycleLibraryTests.cs` (NEW)

**Test setup:**
```csharp
var repo = new EntityRepository();
// Register components...
var entity = repo.CreateEntity();
// Register test blueprints in a BlueprintRegistry...
```

For Test 6 (integration), use `BlueprintTestFixture` to get a full sim loop:
```csharp
using var fixture = new BlueprintTestFixture();
// Register blueprints, call action method, tick ingress system, verify attach
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BlueprintLifecycleLibrary` created with 3 `[SharedAiAction]` methods
- [ ] All 6 specified tests pass
- [ ] Actions discovered and appear in the blueprint editor action palette (verify via test or manual check)
- [ ] All pre-existing blueprint tests pass (0 net-new failures)
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls to Avoid

1. **`SharedAiActionAttribute` constructor** — verify whether it takes `(Type, string)` or also has a parameterless form. The design says plain `[SharedAiAction]`, but the demo code uses `[SharedAiAction(typeof(DemoBlackboardSlot), nameof(DemoBlackboardSlot.Params))]`.
2. **`NodeStatus` namespace** — the return type must match what `InlineActionLowering` expects. Check the lowering code to see which `NodeStatus` it imports.
3. **Blittable DTOs** — the DTO structs must be blittable (no reference-type fields). Use `int` for BlueprintId, `long` for packed entity references.
4. **Entity resolution** — `new Entity(packed)` from `long` packed value. Check if this constructor exists on `Entity`.
5. **One-frame latency** — the action publishes an event but the attach happens NEXT frame. Test 6 must tick the ingress system after publishing to verify the effect.

---

## 📊 Report Requirements

- **Q1:** Which `SharedAiActionAttribute` constructor form did you use? Why?
- **Q2:** Did the actions auto-discover in the editor palette, or did you need manual registration?
- **Q3:** What `NodeStatus` type did you use? (FQN)
- **Q4:** How did you handle entity target resolution from `long` packed value?
- **Q5:** Suggested commit message.
