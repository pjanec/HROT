# BF-BATCH-07 Report — Blueprint Inline Pin-Default-Value Editors

**Date:** 2026-06-06
**Branch:** `blueprint-integ-1`
**Status:** COMPLETE — all gates green

---

## Goal

Replace the no-op `NullPinDefaultValueEditorRegistry` with a real registry so that unconnected
common-typed input pins on the blueprint canvas display an inline ImGui editor widget.

---

## Changes Made

### 1. `Hrot.Blueprints.Compiler/Assets/GraphTypes.cs`
- Added `DefaultValue` (`string?`, `JsonIgnore` when null) to the `Pin` record.
- Carries per-pin default through in-memory projection rebuilds without bloating persisted JSON.

### 2. `Hrot.Blueprints.Compiler/Assets/Nodes.cs`
- Added `PinDefaults` (`Dictionary<string,string>?`, `JsonIgnore` when null) to the `Node` base class.
- Persists across save/reload; keyed by pin name; null/absent when no defaults are set.

### 3. `Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs`
- In `Rebuild()` slow-path (JSON-loaded `Pins:[]`): before creating a synthetic `Pin`, reads
  `assetNode.PinDefaults[pin.Name]` into `defaultVal` and sets `resolvedPin.DefaultValue`.

### 4. `Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs`
- `Default` property changed from hard-coded `null` to return a new `BlueprintPinDefaultValue`
  when `!pin.IsExec && pin.Direction == "In" && pin.DefaultValue != null`.
- Added `BlueprintPinDefaultValue : IPinDefaultValue`:
  - `ParseValue(typeId, rawValue)` — parses string to boxed CLR type for bool/int/float/double/byte/uint/string.
  - `FormatValue(value)` — converts boxed CLR back to invariant-culture string for persistence.

### 5. `Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`
- Added `case GraphCommand.SetPinDefault` branch in `Apply()` routing to `ApplySetPinDefault()`.
- `ApplySetPinDefault`:
  - Resolves pin → owning node.
  - Calls `EditService.RecordPropertyEdit` with apply/undo lambdas (full undo-redo support).
  - Updates `node.PinDefaults` via `SetPinDefaultOnNode` helper (null-value path removes the key).
  - Calls `_model.RebuildAndNotify()` so the canvas reflects the new default immediately.

### 6. `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`
- Added `using NodeEditor.UI.MiniEditors;`
- Changed `BlueprintTypeSystem` construction site (line ~122) from:
  ```
  var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
  ```
  to:
  ```
  var typeSystem = new BlueprintTypeSystem(PinDefaultValueEditorRegistry.CreateWithBuiltins());
  ```
- `CreateWithBuiltins()` pre-registers editors for bool, int, float, double, string,
  Vector2/3/4, Quaternion, Color, and Guid.

### 7. `Hrot.Blueprints.Tests/Host/BlueprintPinDefaultValueTests.cs` (NEW)
- 24 headless tests covering:
  - `ParseValue` round-trips for all supported types + fallback/bad-input cases.
  - `FormatValue` round-trips.
  - `BlueprintPinModel.Default` is non-null only for In-data pins with `DefaultValue` set;
    null for Out pins and Exec pins.
  - `SetPinDefault` writes to `node.PinDefaults`, survives a rebuild, and marks the document dirty.
  - Clearing a default (null value) removes the entry and collapses `PinDefaults` to null.

---

## Projection-Only Invariant

The `Pins:[]` invariant is preserved:
- `node.PinDefaults` is a separate dictionary on the node; it persists independently.
- `Pin.DefaultValue` is populated only during `Rebuild()` from `PinDefaults`; it is never
  serialized (JsonIgnore).
- No change to the pin-list serialization logic.

---

## Gate Results

| Gate | Result |
|------|--------|
| `dotnet build IOS-IG-SimHost.sln -c Debug` | **0 errors, 18 warnings (all pre-existing)** |
| Blueprints test failures | **7 / 7 pre-existing (no regressions)** |
| `BlueprintPinDefaultValueTests` (new) | **24 / 24 passed** |

---

## ImGui Visual Behavior

**NOTE: The inline editor widgets on the blueprint canvas require running-editor verification.**
The headless gate cannot exercise the ImGui widget draw loop. Specifically, the following
needs manual confirmation in the running editor:

- Unconnected input data pins (bool, int, float, string) show an inline editor control on the
  blueprint canvas node.
- Editing a value and committing it persists via `Ctrl+S` (projection-only save), survives
  reload, and the undo command (`Ctrl+Z`) correctly restores the previous value.
- Connected input pins do NOT show an editor widget (canvas `DrawInlineEditors` already
  gates on `connectedInputPins.Contains(p.Id)`).
- Exec and output pins do NOT show an editor widget.

---

## Files Modified

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BlueprintPinDefaultValueTests.cs` (new)
