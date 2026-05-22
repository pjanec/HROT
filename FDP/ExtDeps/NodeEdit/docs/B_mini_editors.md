# Mini-Editors — Inline Pin Default Value Editors

## Overview

When an input data pin has no incoming connection and its type has a registered default-value editor, the editor renders the input control inline next to the pin label. These mini-editors are the most-touched UI elements in the entire editor — get them right and the editor feels fast; get them wrong and every constant feels like work.

This file specifies the contract, every built-in editor, the expression evaluator, and how editors interact with the inline pin rendering.

## Contract

```csharp
public interface IPinDefaultValueEditor
{
    /// Returns true if the value changed this frame.
    /// `committed` is true when the change should generate an undo entry
    /// (release of slider/drag, Enter pressed, focus lost).
    bool Draw(
        ref object? value,
        DefaultEditorContext ctx,
        out bool committed);
}

public readonly record struct DefaultEditorContext(
    PinId Pin,
    TypeKey Type,
    float MaxWidth,                  // editor must fit within this width
    bool IsReadOnly,                 // grey-out during connected state etc.
    PinDefaultMetadata Metadata);

public sealed record PinDefaultMetadata(
    double? RangeMin,
    double? RangeMax,
    double? Step,
    string? Units,                   // displayed as suffix, e.g. "m", "kg", "°"
    string? PickerSourceKey,         // for opening the generic picker
    string? PlaceholderText,         // for string inputs
    bool ClampToRange);
```

### Committed vs. changed

The committed/changed split matters enormously: a `DragFloat` fires "value changed" every frame while you drag. We want **one undo entry per drag-gesture**, not 60.

- The view-model updates locally on every `changed = true` call (the wire model sees the new value immediately).
- Only when `committed = true` is fired does the editor emit a `SetPinDefault` command to the host.

Committed events fire on:
- Mouse-release after drag.
- Enter pressed.
- Focus lost (clicked elsewhere).
- Type changed (e.g. typing a value in text-edit mode then pressing Enter).

## Registration

```csharp
public interface IPinDefaultValueEditorRegistry
{
    void Register(TypeKey type, IPinDefaultValueEditor editor);
    void RegisterFallback(IPinDefaultValueEditor editor);
    IPinDefaultValueEditor? GetEditor(TypeKey type);
}
```

The host calls `Register` for each type at startup. Built-in editors come with the library and are auto-registered to standard `TypeKey`s (`"System.Boolean"`, `"System.Int32"`, etc.). The host can override any built-in by registering a new editor under the same key.

For the project that integrates this editor (e.g., the Blueprint subsystem): register custom editors for `Entity`, `FixedString32`, asset reference types, channel-action enum dropdowns, etc. The mental model is identical to `StructEdit`'s `ComponentEditServiceBuilder.RegisterFieldEditor<T>()`.

## Built-in editors

A baseline registry shipped with the library. Each editor is ~10–30 LOC.

### bool

- Checkbox `[✓]`.
- Commits immediately on click.
- No drag interaction.
- Width: ~18 px.

### int / long

- `[ 42 ]` drag-int with horizontal `↔` cursor on hover.
- Wheel-drag for big steps.
- Single click → enter text-edit mode (value editable as text, all selected).
- Commits on release / Enter / focus-out.
- Clamps to `RangeMin`/`RangeMax` if set.
- `Ctrl+drag` = step × 10. `Shift+drag` = step ÷ 10. `Alt+drag` = step × 0.01 for fine work.
- Width: ~80 px.

### float / double

- `[ 3.14 ]` drag-float.
- Same as int, plus suffix for `Units` (`[ 3.14 m ]`).
- Right-click menu: `Set to 0`, `Reset to default`.
- Width: ~80 px.

### string

- `[ Hello world… ]` text field.
- Single-line. For multi-line types, opens external popup on click.
- Commits on Enter / focus-out. Esc cancels and reverts.
- `PlaceholderText` from metadata shown when empty.
- Width: stretches to fill `MaxWidth`.

### enum (small, ≤8 values)

- `[ Strict ▾ ]` combo box.
- Click drops a standard menu.
- Width: fits widest value name + padding.

### enum (large, >8 values, or [Flags])

- `[ Strict ▾ ]` button.
- Click opens the **generic picker** (see `C_generic_picker.md`) — fuzzy-searchable.
- For `[Flags]`, the picker is multi-select mode and shows the bit composition.

### Vector2

- `[ X: 0.0 Y: 0.0 ]` — two compact drag-floats in a row.
- Tab moves between fields.
- Width: ~120 px.

### Vector3

- `[ X: 0.0 Y: 0.0 Z: 0.0 ]` — three compact drag-floats.
- Width: ~140 px.

### Vector4

- Four compact drag-floats.
- Width: ~160 px.

### Quaternion

- Renders as Yaw/Pitch/Roll degrees (matches the existing `QuaternionEulerFieldDrawer` pattern).
- Three drag-floats labelled `Y P R` in degrees.
- Internal storage stays as quaternion; conversion happens at the editor boundary.

### Color (Vec4 RGBA)

- `[██]` color swatch (~24 × 16 px).
- Click opens popup containing:
  - `ImGui.ColorPicker4` (from ImGui.NET).
  - Hex input field: `[ #FF8800 ]`.
  - Named-color dropdown (Red, Green, Blue, White, etc.).
  - Recent-colors strip (last 8 used colors, persisted globally).
- Right-click: `Copy hex`, `Paste hex`, `Reset`, `Set alpha to 1.0`.

### Guid

- `[ 8f3a-…-c2e1 ▾ ]` button.
- Click opens the generic picker for entity / asset reference selection.
- Shows truncated middle of the GUID.

### Entity (project-specific)

- `[ #12,v3 ▾ ]` button — index and generation.
- Click opens entity picker.
- Right-click: `Pick from world` if your map supports gizmo-pick.

### Asset Reference

- `[ MyAsset ▾ ]` button.
- Click opens asset picker.
- Shows asset name; truncates with ellipsis.
- **Drag-drop target:** accepts drops from external panels (asset browser, entity tree). When a compatible drag is in progress, the button highlights with a green border. Drop sets the value.

### Struct (composite)

- `[ struct ▸ ]` button.
- Click opens a popup with sub-editors for each field.
- Alternative: if the user used `Split Struct Pin`, no inline editor — sub-pins take over.

### Array

- `[ [3 items] ▸ ]` button.
- Click opens popup:

```
┌────────────────────────────────────┐
│ Array<float> [3]                   │
├────────────────────────────────────┤
│ [0]  [ 1.5 ]              [×]      │
│ [1]  [ 2.0 ]              [×]      │
│ [2]  [ 3.5 ]              [×]      │
│ [ + Add ]                          │
└────────────────────────────────────┘
```

- Each row: element's editor (recursively dispatched) + delete `×`.
- Reorder via grip handle on the left (V2).
- Bulk operations: right-click header → `Clear`, `Sort`, `Paste from JSON`.

### Wildcard (unresolved generic)

- `[ — ]` greyed.
- No editor; type must resolve first.

### Unknown type (fallback)

- `[ ? ]` greyed.
- Tooltip: `No editor registered for type {TypeKey}`.

## Drag-float behavior (the most-used editor)

Specifics for the workhorse:

- **Hover** anywhere on the value: cursor becomes a horizontal `↔`.
- **Click-and-hold + horizontal drag**: value changes continuously.
  - Step = `Metadata.Step` if set, else type-default (1 for int, 0.01 for float, 0.001 for unit-less small-range floats).
  - Speed: `step * dx_pixels`.
  - `Ctrl` held: `× 10`.
  - `Shift` held: `× 0.1`.
  - `Alt` held: `× 0.01` for fine work.
- **Cursor stays put visually during drag** — ImGui's `DragFloat` uses relative mouse capture. Pointer doesn't fly off-screen.
- **Single click without drag** (or click+release within ~150 ms and ≤3 px): enter text-edit mode. Current value becomes editable text, all selected. Type, Enter or click-elsewhere commits. Esc cancels.
- **Tab from one drag-float** jumps focus to the next pin's drag-float on the same node (great for filling in a Vector3).
- **During text-edit mode**, accept expressions (see Expression Evaluator below): `2*pi`, `1/60`, `45 deg`, `-3e-4`.
- **Right-click the field** → small menu:
  - `Reset to Default`
  - `Copy` / `Paste`
  - `Snap to Step`
  - `Set to Min` / `Set to Max` (only if range bounds set)

## Multi-field editors (Vector2/3/4)

Layout as three drag-floats with tiny labels inline:

```
 X[ 0.000 ] Y[ 0.000 ] Z[ 0.000 ]
```

- Each component is an independent drag-float editor.
- **Drag on the X/Y/Z label itself** also drags that component — extra hit target, big QoL.
- Right-click any component → menu has: `Copy XYZ`, `Paste XYZ`, `Set All To…`, `Normalize` (Vec3 only).
- If `MaxWidth` too small for all three inline, collapse to a button `[ XYZ ▾ ]` that opens a popup with the three drag-floats vertically stacked.

## Inline rendering rules

How editors appear on a node's pin column:

```
◯ Multiplier      [ 2.50 ]
◯ Enabled         [✓]
◯ Mode            [ Strict ▾ ]
◯ Position        X[ 0.0 ] Y[ 0.0 ] Z[ 0.0 ]
◯ Target Entity   [ Player_01 ▾ ]
◯ Color           [██]
◯ Settings        [ Settings ▸ ]
```

Layout rules:

- Total editor width must be ≤ `MaxWidth` (computed from node's pin column width).
- If a type's natural width exceeds `MaxWidth` (Vector3 in a narrow node, for example), fall back to a button label that opens a wider popup.
- Editors use **monospace font** for numeric fields. Critical for visual alignment when nodes have multiple float pins stacked.
- Right-edge of the editor is flush with the node's pin-column edge so stacked editors form a clean column.

**A small `↶` glyph** appears next to the editor when value differs from the type's default; clicking resets. (Polish, V2.)

**Pin-column budget per node side:** ~180 px for label + editor combined. Editors that need more space fall back to button-opens-popup.

## When wire connects to the pin

The inline editor is **hidden** and replaced by italic grey text: `← wired`.

This makes it obvious *why* the editor disappeared. When the wire disconnects, the editor reappears with the last-edited default value.

## ImGui ID stability

Each editor uses `ImGui.PushID(pin.Id.Value.GetHashCode())` so the editor maintains its focus state across frames. This is the same pattern your `StructEdit` infrastructure already uses with `EditNodeId.Value` — zero-allocation stable IDs.

## Drag interaction handling

When focus is on a child widget (text-edit mode), the node's drag handler must ignore drags. The interaction state machine (see `A_canvas_interactions.md` §A.1) checks `ImGui.IsAnyItemActive()` or `ImGui.IsWindowHovered()` and suppresses canvas drag handling for the frame.

This is how every ImGui-based editor handles widget vs canvas input conflict: ImGui owns input that hits widgets; canvas logic runs only when ImGui says no widget is active.

## Expression evaluator (for drag-float text-edit mode)

A tiny expression evaluator for the float / int text-edit mode. Hugely useful — Unreal has this and users love it.

### Grammar

| Element | Examples |
|---|---|
| Numbers | `42`, `3.14`, `1.5e-3`, `-7` |
| Operators | `+`, `-`, `*`, `/`, `%`, `^` (power), `(`, `)` |
| Constants | `pi`, `tau`, `e` |
| Functions | `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `sqrt`, `abs`, `floor`, `ceil`, `round`, `min(a,b)`, `max(a,b)`, `clamp(v,min,max)` |
| Unit suffixes | `deg` (multiplies by π/180), `rad` (no-op) |

### Examples

```
2*pi              → 6.283185...
1/60              → 0.016666...
45 deg            → 0.785398... (= 45° in radians)
sin(pi/4)         → 0.707106...
clamp(5,0,3)      → 3
sqrt(2)           → 1.414213...
1.5e-3            → 0.0015
```

### Error handling

On error (parse fail or division by zero):
- The field shakes briefly (CSS-style ~150 ms horizontal jitter).
- Shows error in a small red tooltip for 2 seconds.
- Reverts to the previous value.

### Implementation

Recursive-descent parser, ~150 LOC. Returns either a `double` or an error message. Whitelist-only — no eval of arbitrary code.

## Per-frame performance

Inline editors are only drawn for **visible nodes with unconnected input pins**. With viewport virtualization, only the few hundred pins actually on screen render editors. No performance concern at scale.

Per-editor draw cost: <0.1 ms for typical types (bool, int, float). Color picker (when popup open) is the most expensive but only one is ever active at a time.

## Future built-in editors (V2+)

These should be added to the library over time:

- `Rect` / `Bounds` — composite of position + size.
- `Curve` — small curve editor for animation curves.
- `LayerMask` — bitmask of layer names.
- `KeyCode` — keyboard key picker.
- `Texture/Sprite preview` — small thumbnail for asset references.

Hosts can implement any of these themselves via the standard registration API.
