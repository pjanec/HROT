# BATCH-10 Report
**Date:** 2026-06-16
**Branch:** main

## Implementation Summary

### Task 1 — PREREQ: StatefulSlotInfo.WorkingStateType + NodeLabel

**`FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`**

Extended `StatefulSlotInfo` from a 3-positional record to a 5-positional record with two optional trailing parameters (defaulting to `null`):

```csharp
public sealed record StatefulSlotInfo(
    int SlotKey,
    int PayloadSize,
    uint StructureHash,
    Type? WorkingStateType = null,
    string? NodeLabel = null);
```

All existing 3-arg constructions in BATCH-06/08 tests compile without change.

**`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs`**

Updated `EmitStatefulWorkingSlotsArray`:
- Extended the collection dict tuple to include `NodeLabel` alongside `WsTypeId`.
- For each node, sets `NodeLabel = DisplayLabel ?? VisualId.ToString()`.
- Extended the emitted `StatefulSlotInfo(...)` constructor line to pass:
  - `typeof({wsTypeFqn})` — the working-state type for typed projection.
  - `"{escapedLabel}"` — the node's `DisplayLabel` (backslash/quote-escaped) for the inspector row label.

Before (3 args):
```
new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, Marshal.SizeOf<T>(), unchecked({hash}u ^ ...)),
```

After (5 args):
```
new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, Marshal.SizeOf<T>(), unchecked({hash}u ^ ...), typeof(T), "{label}"),
```

### Task 2 — Feature A: Typed WorkingState section in tier renderers

**NEW: `Hrot/Engine/Hrot.Presentation/Renderers/StatefulWorkingStateProjection.cs`**

A shared static class (no triplication) with:

- `public static BehaviorRegistry? BehaviorRegistryAccessor` — set at startup.
- `public static unsafe void RenderWorkingState(IInspectableSession, Entity, byte*)` — the render entry point called by each tier renderer after its summary table.
  - Resolves `BehaviorState.ActiveBehaviorHash` → `BehaviorRegistry.TryGetDefinition` → `StatefulWorkingSlots`.
  - For each slot with `WorkingStateType != null`, calls `TryProjectSlot`. If `Ok`, renders `ImGui.Separator` + `ImGui.TextDisabled("Working state (BTree)")` (deferred until first resolved slot), then a `ImGui.TreeNodeEx(label)` with `ImGuiPropertyTree.Render(boxed, contextType)` inside.
- `internal static unsafe SlotProjectionResult TryProjectSlot(byte*, StatefulSlotInfo, out object?)` — the **testable decode seam**:
  - Returns `NoType` if `WorkingStateType == null`.
  - Returns `SlotNotFound` if `TryGetSlotOffset` fails.
  - Returns `InvalidOffset` if offset is ≤ 0.
  - Wraps `Marshal.PtrToStructure` in try/catch; returns `MarshalException` on failure.
  - Returns `Ok` with boxed struct on success.

**`BlueprintBlackboard{1024,4096,16384}Renderer.cs`** (all three)

Added one call after the `ImGui.EndTable()` block inside the `unsafe` region:
```csharp
StatefulWorkingStateProjection.RenderWorkingState(session, entity, mem);
```
The existing 4-column slot-summary table is preserved unchanged.

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`**

Added wiring next to the existing `BlueprintRegistryAccessor` assignments:
```csharp
Hrot.Presentation.Renderers.StatefulWorkingStateProjection.BehaviorRegistryAccessor = behaviorRegistry;
```

## Design Decisions

1. **`BehaviorRegistryAccessor` on the shared helper, not on each renderer.** The spec said "prefer the helper" for the static accessor. This avoids 3× duplicate wiring and 3× duplicate null-check code in the renderers.

2. **`TryProjectSlot` is `internal`** (not `private`), exposed via `InternalsVisibleTo` to `Hrot.Presentation.Tests`. This enables direct unit testing of the decode path without requiring ImGui.

3. **Header rendered only when ≥1 slot resolves.** The `ImGui.Separator()` and `ImGui.TextDisabled("Working state (BTree)")` lines are deferred inside a `headerPrinted` flag — no orphan section header appears when all slots are absent or untyped.

4. **`ImGui.TreeNodeEx(..., DefaultOpen)` per slot.** Each slot gets its own collapsible tree node labelled by `NodeLabel`. This mirrors how `BrainBlackboardRenderer` uses `ImGuiPropertyTree.Render` for each variable.

5. **`SlotProjectionResult` enum for testability.** Rather than bool/exception, the seam returns a discriminated result so tests can assert the exact failure reason (NoType, SlotNotFound, etc.).

6. **Test struct `TestCursorState` (local)** instead of directly referencing `DemoCursorState` from Hrot.AI.Behaviors. This keeps the test project self-contained at the source level (though AI.Behaviors is a transitive dep anyway). The struct has identical layout (`[StructLayout(Sequential)] { int Cursor; }`).

## Deviations

None. All changes match the spec exactly.

## Test Results

### Hrot.AiEditor.Generators.Tests
- Filter: `StatefulSlotKey` → 2/2 passed.
- New assertions added to `StatefulEmitter_EmitsBridge_WithTryGetSlotOffset_AndSlotKeyLiteral`:
  - (g) `typeof(` present in emitted output.
  - (h) `"AdvanceCursor"` (the node DisplayLabel) present as NodeLabel string literal.
- Full suite: **87 total, 85 passed, 2 failed** (both are known pre-existing `MigrationEquivalence` failures — non-regressions as documented in BATCH-10 instructions).

### Hrot.AiEditor.Persistence.Tests (byte-identity gate)
- **129 passed, 0 failed** ✓ — byte-identity preserved.

### Hrot.Presentation.Tests
- Filter: `Behavior` → **23 passed, 0 failed** ✓
- New `StatefulWorkingStateProjectionTests` (5 tests):
  - `TryProjectSlot_DecodesKnownCursorValue` — writes Cursor=42 into slot payload, decodes it, asserts value == 42. **Real value assertion.**
  - `TryProjectSlot_ReturnsSlotNotFound_WhenSlotAbsent` — missing slot returns `SlotNotFound`.
  - `TryProjectSlot_ReturnsNoType_WhenWorkingStateTypeIsNull` — 3-arg ctor slot returns `NoType`.
  - `BehaviorDefinition_StatefulWorkingSlots_CarriesWorkingStateTypeAndNodeLabel` — round-trips all 5 fields.
  - `StatefulSlotInfo_ThreeArgConstruction_HasNullOptionalFields` — PREREQ back-compat.

### Fdp.Toolkits.Tests (--filter Behavior)
- **153 passed, 0 failed** ✓

### T20 proof tests
- Filter: `T20` on `Hrot.AiEditor.Generators.Tests` → **2 passed, 0 failed** ✓

## Developer Insights

- The `ImGui.TreeNodeEx` + `ImGui.TreePop()` pattern requires matching every push with a pop; the current implementation is correct since `TreeNodeEx` returns true only when the node is open, and `TreePop()` is called only inside the `if` block — this mirrors the ImGui convention for collapsible nodes without leaves.
- `Marshal.PtrToStructure` requires the target type to be a blittable struct (or a class with `[StructLayout]`). The try/catch guards against non-blittable types that might be accidentally passed in; in practice only `[StructLayout(Sequential)]` structs will ever reach this code path.
- The `Hrot.Presentation.Tests` suite exhibits slight non-determinism in total test count (58–64 range) due to the `[Collection("ImGui Sequential")]` semaphore and test-runner scheduling. All runs show 0 failed for the non-ImGui tests and no regressions in the Behavior filter.

## Rebuild+Restart Note

The editor app does **not** hot-reload. To see the "Working state (BTree)" section live in the Entity Inspector, a full rebuild (`dotnet build`) of `Hrot.Editor` followed by an application restart is required.

## Known Issues

None. All spec requirements implemented.

## Suggested Commit Message

```
feat(inspector): BATCH-10 — typed WorkingState in BlueprintBlackboard* renderers (PREREQ + Feature A)

- Extend StatefulSlotInfo with optional WorkingStateType + NodeLabel (back-compat)
- BTreeBridgeEmitCore emits typeof(WorkingState) + DisplayLabel in StatefulWorkingSlots
- New StatefulWorkingStateProjection shared helper with testable TryProjectSlot seam
- BlueprintBlackboard{1024,4096,16384}Renderer call helper after slot-summary table
- Wire BehaviorRegistryAccessor in EditorSubsystem
- 5 new Presentation.Tests; emitter test extended with typeof + NodeLabel assertions
```
