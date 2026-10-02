# BF-BATCH-0607-FIX: Corrective Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06
**Sub-agent:** Sonnet 4.6

---

## Summary

Two incomplete delivered batches corrected:
- **FIX-A (BATCH-07):** Unset In-data pins now show inline editors (type-zero default) when the canvas has a registered editor for that type.
- **FIX-B (BATCH-06):** `ChannelCommandNodeDrawer` added so designers can select ChannelType + ActionId via a Combo, triggering param-pin projection via `NodePinSchema.ChannelCommandPins`.

---

## FIX-A: Inline editors on unset pins

### Root cause
`BlueprintPinModel.Default` (line 46) gated on `pin.DefaultValue != null`. A fresh unconnected value pin (arithmetic operand, etc.) had `DefaultValue == null` and therefore returned `null`, causing `NodeRenderer.DrawInlineEditors` to skip it entirely.

### Change: condition
The new condition for exposing `Default` on an unconnected In-data pin:

```
if (!pin.IsExec && pin.Direction == "In")
{
    if (pin.DefaultValue != null)
        Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, pin.DefaultValue);
    else if (editorRegistry != null && editorRegistry.GetEditor(new TypeKey(pin.TypeRef.TypeId)) != null)
        Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, rawValue: null);
}
```

- Legacy two-arg ctor (`new BlueprintPinModel(pin, nodeId)`) preserves old behavior (null when unset).
- New three-arg ctor (`new BlueprintPinModel(pin, nodeId, editorRegistry)`) enables zero-value default when the type has a registered editor.
- Connected-pin hiding is already done by `NodeRenderer.DrawInlineEditors` via `!connectedInputPins.Contains(p.Id)` — not reimplemented here.

### Change: `BlueprintPinDefaultValue.ParseValue` null/empty handling
`ParseValue` now accepts `string?` and returns type-zero when `rawValue` is null or empty:
- `System.Int32` → `(object)0`
- `System.Single` → `(object)0f`
- `System.Boolean` → `(object)false`
- `System.String` → `(object)""`
- Unknown type → `null` (no widget)

### Wiring in production
`BlueprintDocumentFactory.Build()` now creates `editorRegistry = PinDefaultValueEditorRegistry.CreateWithBuiltins()` **before** `BlueprintGraphModel`, passing it as the new `editorRegistry` parameter so all projected pins in the running editor show type-zero editors on first use.

---

## FIX-B: ChannelCommandNodeDrawer

### Root cause
No `ChannelCommandNodeDrawer` was registered. Without a drawer, the "node edit session" returned null for ChannelCommandNode, so no Combo appeared in the side panel, `ChannelType`/`ActionId` stayed empty, `NodePinSchema.ChannelCommandPins` fell back to exec-only, and the node title showed "Command: " (blank ActionId).

### New file
`Hrot.Blueprints.Editor/NodeDrawers/ChannelCommandNodeDrawer.cs`

Pattern is exactly `FunctionCallNodeDrawer`:
- `ChannelCommandNodeDrawer : IBlueprintNodeDrawer` — ctor takes `IChannelCommandCatalog` + `IEditService`
- `ChannelCommandNodeSession : INodeEditSession` — `Draw()` renders an `ImGui.Combo` over catalog entries
- Label per entry: `"{ShortChannelType} / {ActionId}"` (e.g. `"LocomotionChannel / MoveTo"`)
- On selection: `_node.ChannelType = LastSegment(entry.ChannelTypeFqn)` (short name, matches compiler convention) and `_node.ActionId = entry.Name`, then `MarkChanged()` → `IEditService.MarkDirty`
- `internal void SelectActionForTest(int catalogIndex)` test hook (InternalsVisibleTo Hrot.Blueprints.Tests)

### Critical convention
`ChannelType` stores the **short class name** (`"LocomotionChannel"`, not the FQN). `NodePinSchema.ChannelCommandPins` matches via `LastSegment(e.ChannelTypeFqn) == cc.ChannelType` (line ~528). Stage2_Validate uses the same convention.

### Registration
`BlueprintEditorBootstrap.CreateNodeDrawerRegistry()` now registers:

```csharp
registry.Register(typeof(ChannelCommandNode),
    new ChannelCommandNodeDrawer(channelCatalog, editService));
```

(after the FunctionCallNode registration, passing the already-threaded `channelCatalog` and `editService`).

---

## Files changed

| File | Change |
|------|--------|
| `Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs` | FIX-A: new 3-arg ctor; null rawValue → type-zero in ParseValue |
| `Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs` | FIX-A: optional `editorRegistry` param; thread to BlueprintPinModel |
| `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | FIX-A: create registry before graph model, share instance |
| `Hrot.Blueprints.Editor/NodeDrawers/ChannelCommandNodeDrawer.cs` | FIX-B: new file (drawer + session) |
| `Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` | FIX-B: register ChannelCommandNodeDrawer |
| `Hrot.Blueprints.Tests/Host/BlueprintPinDefaultZeroTests.cs` | New tests: Fix-A |
| `Hrot.Blueprints.Tests/Editor/ChannelCommandNodeDrawerTests.cs` | New tests: Fix-B |

---

## Build & test results

### `dotnet build IOS-IG-SimHost.sln -c Debug`
- **0 errors / 0 new warnings** (18 pre-existing warnings, all xUnit2013 analyzer or CS0618 obsolete)

### `dotnet test Hrot.Blueprints.Tests -c Debug`
- **Total: 1445** (27 new tests added)
- **Passed: 1430 / Failed: 7 / Skipped: 8**
- 0 new failures

**Final failure set (7 pre-existing, unchanged):**
1. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
2. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource`
3. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`
4. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`
5. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
6. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot`
7. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`

### `dotnet test Hrot.Editor.AiShared.Tests -c Debug`
- **Total: 832 / Passed: 831 / Failed: 1**
- 1 pre-existing failure: `AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` (file-system temp cleanup test, unrelated to blueprint changes, confirmed unchanged file)

### `EditorSubsystemBoot` (Hrot.ClusterRunner.Integration.Tests)
- **10/10 passed**

---

## Visual verification caveat

The headless tests confirm:
- Fix-A: unconnected Int/Float/Bool In-data pins yield non-null `Default` (type-zero) when registry provided; connected/Out/Exec/unsupported types yield null.
- Fix-B: `SelectActionForTest(0)` (MoveTo) sets `ChannelType="LocomotionChannel"` + `ActionId="MoveTo"`, and `NodePinSchema.GetCanonicalPins` then projects at least one data-IN param pin.

**Visual/ImGui behavior (editor shows inline editor widget for unset pins; ChannelCommand Combo selects action + param pins appear + title updates) requires running-editor verification by the user.**

---

## Deviations from instructions

- The `IEditService` interface only exposes `MarkDirty()` (no `RecordPropertyEdit`). `ChannelCommandNodeSession` follows the same pattern as `FunctionCallNodeSession`: applies mutations directly + calls `MarkDirty`. Full undo history (via `EditService.RecordPropertyEdit`) would require promoting `RecordPropertyEdit` to the interface — deferred as the instructions say "model on FunctionCallNodeDrawer" which uses the same approach.
- No running-editor DLL lock encountered.
