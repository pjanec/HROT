# T-13 — UI: Built-in Pin Default-Value Editors (Mini-Editors)

## Goal
Implement all built-in inline editors that appear next to unconnected input data pins. They are dispatched via the host's `IPinDefaultValueEditorRegistry` interface, which is built up at startup with one entry per `TypeKey`.

## Project
`NodeEditor.UI`

## References

**Specs (full read required):**
- `../specs/B-mini-editors.md` — complete catalog with widget choices per type
- `../instructions/01-spec-brief.md` §9 (mini-editors intro)
- `../instructions/01-spec-brief-part2.md` §17 (mini-editor catalog)

**Kernel:**
- `../kernel/03-search-spatial-constants.md` — `ExpressionEvaluator` (used by drag-float)
- `../kernel/04-my-blueprint-and-rest.md` — `IPinDefaultValueEditorRegistry` interface

## Catalog (one editor class per type)

| TypeKey | Editor class | Widget |
|---|---|---|
| `bool` | `BoolPinEditor` | Checkbox |
| `int` | `IntPinEditor` | DragInt + expression evaluator |
| `float` | `FloatPinEditor` | DragFloat + expression evaluator |
| `string` | `StringPinEditor` | InputText (single line, expands on focus to InputTextMultiline) |
| `enum` (generic) | `EnumPinEditor` | Combo populated from `IEnumValueProvider.GetValues(typeKey)` |
| `Vector2`, `Vector3`, `Vector4` | `VectorPinEditor` (one class, dimension parameter) | N drag-float fields, packed horizontally |
| `Quaternion` | `QuaternionPinEditor` | 3 drag-float fields for yaw/pitch/roll in degrees, internally converts to/from quaternion |
| `Color` | `ColorPinEditor` | `ImGui.ColorEdit4` with alpha; small swatch chip |
| `Guid` | `GuidPinEditor` | Read-only text + "Pick" button (opens picker for the target type if known via metadata) |
| `Entity` | `EntityPinEditor` | Picker-driven (opens entity picker on click); displays entity label + small icon |
| `Asset` | `AssetPinEditor` | Picker-driven (asset picker, filtered by asset-type metadata if known); displays asset name + icon |
| `Struct` (composite) | `StructPinEditor` | Either a single foldout that recurses into child editors (Composite mode) or shows nothing inline and exposes children as separate "split" pins (Split mode, decided by metadata) |
| `Array<T>` | `ArrayPinEditor` | Foldout "Count: N", "+/−" buttons, then N child editors using the element type's editor |

## Deliverables

```
src/NodeEditor.UI/
    MiniEditors/
        IPinDefaultValueEditorRegistry.cs   // moved here from kernel/04 if not already in Core
        PinDefaultValueEditorRegistry.cs    // default registry impl with registration helpers
        BoolPinEditor.cs
        IntPinEditor.cs
        FloatPinEditor.cs
        StringPinEditor.cs
        EnumPinEditor.cs
        VectorPinEditor.cs
        QuaternionPinEditor.cs
        ColorPinEditor.cs
        GuidPinEditor.cs
        EntityPinEditor.cs
        AssetPinEditor.cs
        StructPinEditor.cs
        ArrayPinEditor.cs
        DragFloatWithExpression.cs          // shared helper widget for int/float/vector/quaternion
```

> If `IPinDefaultValueEditorRegistry` was declared in `kernel/04-my-blueprint-and-rest.md`, leave it in `NodeEditor.Core` and only add implementations here. Re-read that file to confirm.

## Implementation contract

Every editor implements:

```csharp
namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// One implementation per type key (or one polymorphic implementation registered
/// for several keys). Called by the canvas renderer for each unconnected input data pin.
/// </summary>
public interface IPinDefaultValueEditor
{
    /// <summary>Render the editor for one pin within the current ImGui context.</summary>
    /// <param name="ctx">Per-pin context (current value, pin metadata, command sink, services).</param>
    /// <returns>True if the user changed the value this frame; the canvas will dispatch a SetPinDefault command.</returns>
    bool Render(in PinDefaultEditorContext ctx, ref object? value);
}

/// <summary>
/// Bundle of inputs each mini-editor needs. Passed by readonly ref to avoid per-pin allocs.
/// </summary>
public readonly ref struct PinDefaultEditorContext
{
    public PinId PinId { get; init; }
    public TypeKey TypeKey { get; init; }
    public IGraphCommandSink Commands { get; init; }
    public IEditorHostServices Host { get; init; }
    public ITypeSystem TypeSystem { get; init; }
    public float WidthPx { get; init; }   // available width — renderer pre-sized
}
```

Note: `value` is the live value pulled from `IPinModel.GetDefaultValue(pinId)` by the renderer right before the call. The renderer compares the in/out values; if the editor returns true and the boxed value changed, the renderer dispatches:

```csharp
ctx.Commands.Dispatch(new SetPinDefaultValueCommand(ctx.PinId, value));
```

This avoids each editor needing to know about commands; they just mutate `value`.

> Some editors (notably picker-driven ones like Entity/Asset) cannot use this signature because the result comes from a popup on a later frame. Those editors call `ctx.Host.PickerRegistry.OpenPicker(...)` themselves and ignore the in/out value pattern.

## DragFloat with expression evaluator (shared helper)

```csharp
namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Drop-in replacement for ImGui.DragFloat that, on losing focus,
/// attempts to evaluate the typed text as an arithmetic expression (pi, sin(x), deg(x), etc.).
/// If parsing succeeds, the numeric result becomes the new value. If parsing fails,
/// the previous value is restored and a tooltip flags the parse error.
/// </summary>
public static class DragFloatWithExpression
{
    /// <summary>Render a single drag-float with expression-aware text editing.</summary>
    public static bool Render(string label, ref float value, float speed = 0.1f, string format = "%.3f");

    /// <summary>Render an int variant.</summary>
    public static bool Render(string label, ref int value, float speed = 1.0f);
}
```

Implementation pattern:

```csharp
// When the widget is being edited as text (DragXxx in edit mode):
//   on Enter or focus loss:
//     try { value = (float)ExpressionEvaluator.Evaluate(textBuffer); changed = true; }
//     catch { /* restore prior value, set a tooltip-flagged error */ }
// When the widget is being dragged (not in text-edit mode):
//   standard ImGui drag semantics.
```

The expression evaluator is in `NodeEditor.Core` (built in T-06). Use:

```csharp
double result = ExpressionEvaluator.Evaluate(text);
```

If it throws `ExpressionParseException`, treat the text as invalid and don't apply.

## Vector editor (representative example)

```csharp
namespace NodeEditor.UI.MiniEditors;

using System.Numerics;

/// <summary>Editor for Vector2 / Vector3 / Vector4. Pack drag-float fields side by side.</summary>
public sealed class VectorPinEditor : IPinDefaultValueEditor
{
    private readonly int _dimension; // 2, 3, or 4

    public VectorPinEditor(int dimension) { _dimension = dimension; }

    public bool Render(in PinDefaultEditorContext ctx, ref object? value)
    {
        // Decode boxed value into Vector4 working buffer.
        var v = Decode(value);
        bool changed = false;

        ImGui.PushItemWidth(ctx.WidthPx / _dimension - 4f);
        if (DragFloatWithExpression.Render("##x", ref v.X)) changed = true;
        ImGui.SameLine();
        if (DragFloatWithExpression.Render("##y", ref v.Y)) changed = true;
        if (_dimension >= 3)
        {
            ImGui.SameLine();
            if (DragFloatWithExpression.Render("##z", ref v.Z)) changed = true;
        }
        if (_dimension >= 4)
        {
            ImGui.SameLine();
            if (DragFloatWithExpression.Render("##w", ref v.W)) changed = true;
        }
        ImGui.PopItemWidth();

        if (changed) value = Encode(v);
        return changed;
    }

    private Vector4 Decode(object? v) => _dimension switch {
        2 => v is Vector2 v2 ? new Vector4(v2.X, v2.Y, 0, 0) : default,
        3 => v is Vector3 v3 ? new Vector4(v3.X, v3.Y, v3.Z, 0) : default,
        4 => v is Vector4 v4 ? v4 : default,
        _ => default
    };

    private object Encode(Vector4 v) => _dimension switch {
        2 => new Vector2(v.X, v.Y),
        3 => new Vector3(v.X, v.Y, v.Z),
        4 => v,
        _ => v
    };
}
```

## Registry default population

```csharp
namespace NodeEditor.UI.MiniEditors;

/// <summary>Default implementation of the pin-editor registry. Looks up by TypeKey.</summary>
public sealed class PinDefaultValueEditorRegistry : IPinDefaultValueEditorRegistry
{
    private readonly Dictionary<TypeKey, IPinDefaultValueEditor> _byType = new();

    public void Register(TypeKey type, IPinDefaultValueEditor editor) => _byType[type] = editor;

    public IPinDefaultValueEditor? Get(TypeKey type) =>
        _byType.TryGetValue(type, out var ed) ? ed : null;

    /// <summary>Wire up all built-in primitive editors. Hosts call this once at startup.</summary>
    public static PinDefaultValueEditorRegistry CreateWithBuiltins()
    {
        var r = new PinDefaultValueEditorRegistry();
        r.Register(TypeKey.Of("bool"),       new BoolPinEditor());
        r.Register(TypeKey.Of("int"),        new IntPinEditor());
        r.Register(TypeKey.Of("float"),      new FloatPinEditor());
        r.Register(TypeKey.Of("string"),     new StringPinEditor());
        r.Register(TypeKey.Of("Vector2"),    new VectorPinEditor(2));
        r.Register(TypeKey.Of("Vector3"),    new VectorPinEditor(3));
        r.Register(TypeKey.Of("Vector4"),    new VectorPinEditor(4));
        r.Register(TypeKey.Of("Quaternion"), new QuaternionPinEditor());
        r.Register(TypeKey.Of("Color"),      new ColorPinEditor());
        r.Register(TypeKey.Of("Guid"),       new GuidPinEditor());
        // Entity / Asset / Enum / Struct / Array editors are usually polymorphic
        // and registered by the host since they consult host metadata.
        return r;
    }
}
```

## Spec-conformance reminders

- Quaternion: store quaternion in the model, but expose **yaw/pitch/roll degrees** to the user. Convert each frame via `Quaternion.CreateFromYawPitchRoll(radians)`. Wrap angles to (-180, 180].
- Color: clamp inputs to [0,1] and pass-through to model unchanged (color space conversion is the host's responsibility).
- String: single-line by default. On focus, expand to a multi-line InputText if the value contains '\n'.
- Enum: combo entries come from `IEnumValueProvider.GetValues(typeKey)` — the host knows the enum's value list. Selected entry is the current value; if the live value is not in the list, show "(invalid: N)" and keep the underlying value untouched until the user picks.
- Struct (composite mode): use ImGui.TreeNodeEx with the pin name as the header. Inside, render one child editor per field. Struct (split mode): no inline editor — the canvas renders each field as its own pin (handled by the canvas renderer's pin enumeration, not here).
- Array: header line shows count, [+] button appends one default element, [−] button removes the last; per-element editors are children with a small [×] button to remove that specific index.

## Acceptance

- All editors compile.
- The demo app (T-21) includes a scenario showing one of each: a node with bool, int, float, string, Vector3, Color, enum, and array<int> input pins, all editable.
- For text-input editors: pressing Enter, Tab, or clicking away commits the value. Pressing Escape reverts.
- For drag editors: dragging changes value continuously; typing a number replaces; typing `pi*2` evaluates on commit.
- No tests in this layer (UI is exercised through the demo).

## Estimated Size
~800 LOC across all editors plus helpers.

## Status
Pending.
