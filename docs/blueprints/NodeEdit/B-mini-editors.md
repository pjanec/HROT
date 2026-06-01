# B — Mini-Editors (Inline Pin Default Values)

The inline-default-editor system is what saves blueprint graphs from being
"Literal Node" hell. Get it right and the editor feels Unreal. Get it wrong
and users hate every constant they enter.

## B.1 The contract

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
    float MaxWidth,
    bool IsReadOnly,
    PinDefaultMetadata Metadata);

public sealed record PinDefaultMetadata(
    double? RangeMin,
    double? RangeMax,
    double? Step,
    string? Units,
    string? PickerSourceKey,
    string? PlaceholderText,
    bool ClampToRange);
```

### The committed/changed split

A `DragFloat` fires "value changed" every frame while you drag. We want one
undo entry per drag-gesture, not 60.

- Per-frame `Draw` returns `true, committed=false` for in-progress changes.
- View-model updates locally on each `true`.
- Only on `committed=true` does the editor emit a `SetPinDefault` command.

Commit triggers:
- DragFloat/DragInt: mouse release.
- Text-edit field: Enter or focus-out.
- Checkbox: every click (commit immediately).
- Combo: every selection.
- Color picker: every change (or only on close — implementation choice).

## B.2 Visual layout rules

- Editor width must be ≤ `MaxWidth` (computed from node's pin column width).
- If natural width exceeds MaxWidth (e.g., Vector3 in narrow node), fall
  back to button label `[ … ▾ ]` that opens wider popup.
- Numeric editors use monospace font for alignment.
- Right-edge of editor flush with pin column edge.
- Small `↶` reset glyph appears next to editor when value differs from
  default (V2).

## B.3 The full catalog

### bool — Checkbox

- ImGui `Checkbox`. ~18 px wide.
- Commits immediately on click.
- No drag interaction.

### int / long — DragInt

```
[ 42 ]
```

- ImGui `DragInt`. Width ~60 px default.
- Hover: cursor → ↔.
- Click+drag: continuous change. Speed = `step * dx_pixels`. Step from
  `Metadata.Step` or default = 1.
- Ctrl+drag: ×10 speed.
- Shift+drag: ÷10 speed.
- Alt+drag: ÷100 speed.
- Single click (no drag, ≤3 px, ≤150 ms): enter text-edit mode, all selected.
- Tab: focus next editor on same node.
- During drag: commit deferred.
- Right-click: menu — Reset to Default, Copy, Paste, Snap to Step, Set to Min,
  Set to Max.
- Range clamp if `RangeMin/Max` set.

### float / double — DragFloat

Same as DragInt, but with default step 0.01.

```
[ 3.14 ]
```

Suffix from `Metadata.Units`:
```
[ 3.14 m ]
[ 45.0 ° ]
[ 1.5 kg ]
```

### string — InputText

```
[ Hello world... ]
```

- Single-line text field.
- Commits on Enter or focus-out.
- Esc cancels (reverts to previous value).
- For multi-line strings: editor button `[ "Hello\\n..." ▾ ]` opens popup
  with multi-line `InputTextMultiline`.

Placeholder text from `Metadata.PlaceholderText` shown when empty.

### Enum (small, ≤ 8 values)

```
[ Strict ▾ ]
```

- ImGui `Combo`. Width ~120 px.
- Click drops menu.
- Commits on selection.

Enum source registered via `IEnumValueProvider`:
```csharp
public interface IEnumValueProvider
{
    IReadOnlyList<EnumValueEntry> GetValues(TypeKey enumType);
    int GetMaxInlineValues();        // 8 by default
}
```

### Enum (large, > 8) or [Flags]

```
[ Strict ▾ ]
```

- Button. Click opens generic picker (multi-select for [Flags]).

### Vector2

```
X[ 0.000 ] Y[ 0.000 ]
```

- Two DragFloat sub-editors.
- Tight horizontal spacing.
- Dragging on label "X" / "Y" also drags that component (extra hit target).
- Tab between components.
- Right-click: Copy XY, Paste XY, Set All To…

### Vector3

```
X[ 0.000 ] Y[ 0.000 ] Z[ 0.000 ]
```

Same as Vector2 with Z. Add "Normalize" to right-click menu.

If MaxWidth < ~200 px, collapse to:
```
[ XYZ: 0,0,0 ▾ ]
```
Click opens popup with three vertical DragFloats.

### Vector4

```
X[ 0.000 ] Y[ 0.000 ] Z[ 0.000 ] W[ 0.000 ]
```

Same pattern. Usually too wide for inline; defaults to button-opens-popup.

### Quaternion — Yaw/Pitch/Roll (degrees)

```
Y[ 0.0 ] P[ 0.0 ] R[ 0.0 ]   (yaw / pitch / roll in degrees)
```

- Internally converts to/from quaternion.
- Same drag/edit semantics as Vector3.

This matches Unreal's UX. Direct XYZW editing is hostile to authors.

### Color (Vector4 RGBA)

```
[██]    (color swatch, ~24×16 px)
```

- Click opens popup with `ImGui.ColorPicker4`.
- Popup also shows: hex input field `[ #FF8800 ]`, named-color dropdown,
  recent-colors strip (last 8 used, persisted globally).
- Right-click swatch: Copy hex, Paste hex, Reset, Set alpha to 1.

### Guid — Truncated button

```
[ 8f3a-…-c2e1 ▾ ]
```

- Shows first 4 chars + ellipsis + last 4 chars.
- Click opens picker (entity/asset, depending on context).

### Entity reference

```
[ #12,v3 ▾ ]
```

Or with name resolution:
```
[ Player_01 ▾ ]
```

- Format depends on host.
- Click opens entity picker.
- Right-click: "Pick from world" (if host supports gizmo-pick), Clear,
  Copy/Paste Reference.

### Asset reference

```
[ MyAsset ▾ ]
```

- Truncates if name too long.
- Optional tiny thumbnail on left if host provides.
- Click opens asset picker.
- Right-click: Find in Content Browser, Clear, Copy/Paste Reference.
- Drop target: accepts drags of compatible payloads. Green border highlight
  on drag-hover.

### Struct (composite)

```
[ Transform ▸ ]
```

Two modes:
- **Composite (default):** single pin with this button. Click opens popup
  with sub-field editors.
- **Split:** single pin replaced by N sub-pins. Right-click pin → "Split
  Struct Pin" toggles.

Recombine via right-click sub-pin → "Recombine Struct Pin".

### Array

```
[ [3 items] ▸ ]
```

Click opens popup:
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

- Element editor dispatched recursively by element type.
- `×` removes element.
- `+ Add` appends.
- Reorder via grip handle (V2).
- Right-click header: Clear, Sort, Paste from JSON.

### Wildcard / unknown type

```
[ — ]   (greyed, disabled)
```

- No editor rendered; tooltip explains "Type unresolved" or "No editor for X".

## B.4 Drag-float specifics (deeper)

This is the most-used editor; details matter.

- ImGui's `DragFloat` already does relative mouse capture (cursor stays put
  during drag visually). Use it; don't reinvent.
- During drag, the visual updates every frame but `committed=false`.
- On mouse release: `committed=true` once.
- During text-edit mode:
  - Field accepts expressions (see B.6).
  - All text selected on entry.
  - Enter commits; focus-out commits.
  - Esc cancels.
- Wheel-over-field while not in text-edit: scrolls value by step.

### Step defaults by type

| Type | Default step |
|---|---|
| int | 1 |
| long | 1 |
| float | 0.01 |
| double | 0.01 |
| Vector2/3/4 components | 0.01 |
| Quaternion (degrees) | 0.5 |

If `Metadata.Step` is set, override the default.

### Range bounds

If `RangeMin/Max` set:
- During drag, clamp to range.
- If `ClampToRange` is false, allow text-edit to exceed range (some types
  have soft limits).
- "Set to Min" / "Set to Max" menu items appear only if bounds set.

## B.5 Multi-field editor layout

Width-aware:
1. Try full inline: `X[…] Y[…] Z[…]`.
2. If too wide, try compact: `[X Y Z]` with smaller fields.
3. If still too wide, button: `[ XYZ ▾ ]` opening popup.

Decision per-frame based on available width.

## B.6 Expression evaluator

Used by DragFloat/DragInt text-edit mode. Whitelist:

### Operators (binary)
`+ - * / % ^`

### Operators (unary)
`-` (negation)

### Constants
`pi`, `tau`, `e`

### Functions
`sin cos tan asin acos atan sqrt abs floor ceil round min max clamp deg rad`

- `deg(x)` converts radians to degrees.
- `rad(x)` converts degrees to radians.

### Suffixes
- `45 deg` — interpreted as degrees, converted to radians.
- `1.5 rad` — no-op.

### Scientific notation
`1.5e-3`, `2e10`

### Implementation

Shunting-yard or recursive-descent parser. ~150 LOC. Returns either value
or error message.

### On parse error
- Field shakes ~150 ms (horizontal jitter).
- Red tooltip with error message for 2 s.
- Revert to previous value.

### Examples

| Input | Result |
|---|---|
| `2 * pi` | 6.283… |
| `1/60` | 0.01667 |
| `45 deg` | 0.7854 (rad) |
| `sin(pi/2)` | 1.0 |
| `clamp(x, 0, 1)` | (error — no variables; x undefined) |
| `1.5e-3` | 0.0015 |

## B.7 How editors register

```csharp
public interface IPinDefaultValueEditorRegistry
{
    void Register(TypeKey type, IPinDefaultValueEditor editor);
    void RegisterFallback(IPinDefaultValueEditor editor);
    IPinDefaultValueEditor? GetEditor(TypeKey type);
}
```

Host calls `Register` at startup. Built-in editors auto-register at editor
init time. Host can override any built-in by registering for the same
TypeKey.

Special TypeKeys for the built-ins:
- `"System.Boolean"`
- `"System.Int32"`, `"System.Int64"`
- `"System.Single"`, `"System.Double"`
- `"System.String"`
- `"System.Numerics.Vector2"` etc.
- `"System.Numerics.Quaternion"`
- `"NodeEditor.Color"` (the editor library's own color type wrapper)
- `"System.Guid"`

## B.8 How editors appear on nodes

Input data pin column on the node:
```
◯ Multiplier      [ 2.50 ]
◯ Enabled         [✓]
◯ Mode            [ Strict ▾ ]
◯ Position        X[ 0.0 ] Y[ 0.0 ] Z[ 0.0 ]
◯ Target Entity   [ Player_01 ▾ ]
◯ Color           [██]
◯ Settings        [ Settings ▸ ]
```

Editor to right of pin label. When wire connects, replace editor with
italic gray `← wired`.

Width budget per node-side: ~180 px (label + editor combined).

## B.9 ImGui ID stability

Each editor must use stable ImGui IDs:
```csharp
ImGui.PushID(pin.Id.Value.GetHashCode());
// ... editor draws ...
ImGui.PopID();
```

Prevents focus loss across frames when items shift position slightly.

## B.10 Performance

Editors only drawn for visible nodes' unconnected input pins. With spatial-
index virtualization, only a few hundred per frame max. No issue.
