# Curve Editor in StructEdit — Integration Guide & Shared-Widget Plan v1.1

> **Status:** Conceptual integration guide, derived from the architect's review thread on the
> Runtime Tuning Console (resolution of open question **T-2**) and confirmed against the v236
> `StructEdit.Core` / `StructEdit.Reflection` / Presentation-layer interfaces.

> **Changelog v1.0 → v1.1** (resolves C-1…C-3):
> - **C-1 → `UtilityCurve` lives in the shared `Fdp.Toolkits` AI toolkit** (the `Fdp.Toolkit.Behavior`
>   namespaces, beside the other Utility runtime contracts), so it is visible to runtime, editor, and
>   presentation without a layering violation (§1 type note, §7).
> - **C-2 → piecewise points edit as an `EditNodeKind.DynamicArray` over a managed DTO in the
>   console, with an explicit translate-on-apply step** that converts the variable-length managed
>   array into the component's fixed-size blittable buffer at frame-top (§6.1, §7).
> - **C-3 → comparison overlay is an opt-in `CurveWidgetOptions` flag, off in the console** (§7).
> **Audience:** Implementation agent and human reviewer.
> **Drives:** A single visual curve-editing widget, built once for the Utility AI Editor, then
> exposed inside any `StructEdit` session — most importantly the Runtime Tuning Console's in-world
> `StructInspector`, upgrading it from raw `m,k,b,c` scalars (Slice 1) to the full visual editor
> (Slice 2).
> **Doesn't cover:** The curve *math* (architecture DD §5) or the Utility Editor's window chrome
> (Editor DD §5). This is purely the StructEdit plumbing that lets the same widget render in both
> places.

---

## 1. Why this exists

The Runtime Tuning Console (Tuning DD §4.2) renders a tunable group as a synthesized `EditDocument`.
For curve tunables, Slice 1 expands each curve into four scalar fields (`m,k,b,c`). That is fine for
proving the DDS tuning transport, but **balancing AI by typing mathematical scalars is not a viable
UX for designers** — only for mathematicians. Slice 2 must give the tuning console the same visual
curve editor (plot, draggable handles, piecewise points) that the Utility Editor makes its
centerpiece.

The wrong way to get there is to build the curve editor twice. The right way — and the one the v236
`StructEdit` extension points already support — is to **build the raw widget once, decoupled from any
host, then wrap it as a StructEdit custom field editor.** It then renders identically inside the
Utility Editor's `ManagedWindow` and inside the tuning console's `StructInspector` popups, with no
duplicated drawing code and a guarantee the two surfaces show the same numbers.

This guide is the conceptual recipe for that wrap.

---

## 2. The two-layer StructEdit model (what we're plugging into)

`StructEdit` separates *what a field is* from *how it's drawn*, across two assemblies:

- **`StructEdit.Core`** owns the document model: `IComponentEditService`, `IEditSession`,
  `EditDocument`, `EditNode`, the 20-value `EditNodeKind` vocabulary (which includes
  `EditNodeKind.Custom`), `IValueBinding`, and the **`ICustomFieldEditor`** plugin point that
  decides what `EditNode` a given CLR type produces. This layer is rendering-agnostic (net8, no
  ImGui).
- **The Presentation layer** owns the actual ImGui rendering: `ComponentEditDrawer` walks the
  `EditDocument` tree and draws each node, delegating per-CLR-type custom widgets to
  **`IImGuiFieldDrawer`** implementations (returns `true` if the value changed this frame).

The precedents to copy are already in the tree:

| Existing | Layer | What it does |
|---|---|---|
| `GuidFieldEditor`, `DateTimeFieldEditor` | Core (`ICustomFieldEditor`) | shape `Guid`/`DateTime` into a single custom node instead of a struct tree |
| `QuaternionEulerFieldDrawer` | Presentation (`IImGuiFieldDrawer`) | draws a quaternion as yaw/pitch/roll sliders |
| `MathFieldEditors` | Presentation (`IImGuiFieldDrawer`) | float/int/Vector drag inputs |
| `TypeFieldEditor` / `FilteredTypeComboFieldDrawer` | both | a `System.Type` field as a searchable dropdown |

The curve editor is `QuaternionEulerFieldDrawer`'s big sibling: a non-trivial type that should draw
as one rich widget, not an expandable tree of its scalars. So it needs **both** layers — a Core
editor to make it atomic, and a Presentation drawer to render it.

---

## 3. The five steps

### Step 1 — Define the curve as one CLR type

The widget needs a single type to bind to. This is the editor-side curve model already defined in
the architecture DD §5.3 and the Editor DD §3 (`ResponseCurveModel`): the `CurveKind` enum, the
`m,k,b,c` scalars, and the optional piecewise control-point array, in one struct/class.

```csharp
public struct UtilityCurve            // the StructEdit target type
{
    public CurveKind Kind;
    public float M, K, B, C;
    public PiecewisePoint[]? Points;  // null unless Kind == PiecewiseLinear
}
```

`StructEdit` binds custom editors by CLR type, so this single type is what both layers key on.

### Step 2 — Build the raw ImGui widget, host-agnostic

Before touching any StructEdit interface, build the curve widget as a plain ImGui function with **no
StructEdit dependency**. This is the decoupling that lets the same code serve both hosts.

Contract:

```csharp
// Lives in a shared UI assembly both the Utility Editor and the tuning console reference.
public static class CurveWidget
{
    /// Draws plot + draggable handles + the m,k,b,c numeric fields (locked ones greyed per Kind,
    /// per Editor DD E-2). Optionally overlays a test-fixture input marker and a comparison curve.
    /// Returns true if the user changed anything this frame.
    public static bool Draw(string id, ref UtilityCurve curve, in CurveWidgetOptions opts);
}
```

This is exactly the widget the Editor DD §5 specifies as its centerpiece — built here as a
standalone function so it is **not** trapped inside the Utility Editor's window. It evaluates the
*actual runtime curve function* (architecture §5.3) so both hosts show what the runtime computes, no
preview-math drift.

### Step 3 — Wrap it as an `IImGuiFieldDrawer` (Presentation)

The drawer is the thin adapter from StructEdit's per-node render call to the raw widget:

```csharp
public sealed class UtilityCurveFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(UtilityCurve);

    public bool DrawInput(ref object value, EditNode node)
    {
        var curve = (UtilityCurve)value;
        bool changed = CurveWidget.Draw(node.JsonPath, ref curve, CurveWidgetOptions.Default);
        if (changed) value = curve;     // write back the boxed struct
        return changed;                 // StructEdit marks the session dirty on true
    }
}
```

Returning `true` is what flips `IEditSession.IsDirty`, which is what the tuning console's commit path
(Tuning DD §5.2) keys on to enqueue the change for frame-top application. The same `IsDirty` is what
the Utility Editor uses to know a save is needed.

### Step 4 — Make it atomic with an `ICustomFieldEditor` (Core)

Without this, StructEdit's reflection builder sees `UtilityCurve` as a struct and renders an
expandable tree of `Kind`/`M`/`K`/`B`/`C`/`Points` — defeating the point. The Core editor collapses
it to a single `EditNodeKind.Custom` node that the drawer then owns, exactly as `GuidFieldEditor`
collapses a `Guid`:

```csharp
public sealed class UtilityCurveFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(UtilityCurve);

    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath,
                                IValueBinding binding, EditNodeMetadata metadata)
        => new EditNode(id, name, jsonPath, EditNodeKind.Custom,
                        typeof(UtilityCurve), binding, children: null, metadata);
}
```

### Step 5 — Register both, per host

Two registrations, one per layer, wherever a host builds its edit service / drawer set:

```csharp
// Core service (document shaping) — both hosts:
var service = new ComponentEditServiceBuilder()
    .RegisterFieldEditor<UtilityCurve>(new UtilityCurveFieldEditor())
    .Build();

// Presentation drawers (rendering) — both hosts:
drawers[typeof(UtilityCurve)] = new UtilityCurveFieldDrawer();
```

For the **tuning console**, this is the only change needed to upgrade Slice 1 → Slice 2: the
synthesized tunable-group DTO already contains `UtilityCurve` fields (architecture §3.1 of the
Tuning DD expands curves into their params; here the field stays a `UtilityCurve` and the editor
renders it whole). Registering the two plugins on the console's `ComponentEditService` and
`ComponentEditDrawer` swaps the four greyed scalars for the visual widget with no change to the DDS
transport, the frame-top apply, or the recorded `TuningChangeEvent`.

For the **Utility Editor**, the same widget is also called directly inside its `ManagedWindow` card
inspector (Editor DD §5) — it doesn't strictly need the StructEdit wrap there, but using the same
`CurveWidget.Draw` call guarantees pixel-identical behavior across both surfaces.

---

## 4. Why the scalars stay (even after the widget lands)

Slice 2 does not delete the `m,k,b,c` fields — it renders them **beside** the plot, with kind-locked
params greyed (Editor DD E-2). Three reasons, all already settled:

1. **Pedagogy.** The numbers teach the `output = m·(x − b)^k + c` model; a designer learns which
   knobs each `CurveKind` exposes by watching them move as handles drag.
2. **Console ↔ editor parity.** The tuning console and the Utility Editor must show the *same*
   numbers for the same curve, so an operator tuning in-world and a designer authoring at desk speak
   one language. Shared widget + shared numeric fields guarantees it.
3. **No layout jump.** Keeping all four fields present (greyed when locked) stops the inspector pane
   reflowing when the `CurveKind` dropdown changes.

So the widget is plot + handles + the four fields + (optionally) the fixture marker and the
comparison overlay — one composite, used verbatim in both hosts.

---

## 5. Slice plan

| Slice | Tuning console curve UX | How |
|---|---|---|
| **1** | Four `m,k,b,c` scalar fields (greyed when locked) | default StructEdit scalar rendering of the expanded curve params; proves DDS transport + frame-top apply + recorded `TuningChangeEvent` |
| **2** | Full visual widget (plot, handles, piecewise, fixture marker) | build `CurveWidget` for the Utility Editor, then register `UtilityCurveFieldEditor` + `UtilityCurveFieldDrawer` on the console's StructEdit session per Step 5 |

The dependency order is deliberate: the Utility Editor's curve widget (Editor DD §5) is the
long-pole artifact; once it exists as the host-agnostic `CurveWidget`, the tuning-console upgrade is
two registrations. Build it once, for the editor, and the console inherits it.

---

## 6. Piecewise points: managed edit, translate on apply (C-2)

A non-piecewise curve is five fixed values (`Kind` + `m,k,b,c`) and fits a blittable DTO trivially.
A `PiecewiseLinear` curve carries a **variable-length** point array, which does **not** fit the
native blittable tuning buffer the console otherwise uses. The resolution has two halves:

- **Editing side (console UI).** A curve-bearing tunable group is hosted as a **managed DTO** (the
  `ManagedObjectEditBuffer` / boxed-struct path the memory classifier already selects for
  non-blittable types). StructEdit renders the points natively as an `EditNodeKind.DynamicArray`
  (add/remove rows), and `EditDocumentJsonSerializer` serializes the variable-length array as part of
  the committed `EditDocument` JSON — no special handling.
- **Apply side (sim node, frame top).** When the resulting `TuningChangeEvent` JSON arrives at the
  simulation node during the frame-top apply phase (Tuning DD §5.2), the apply logic **deserializes
  and translates** the variable-length managed array into the component's **fixed-size blittable
  representation** — writing the points into the native fixed buffer / `[InlineArray]` the runtime
  `UtilityCurve` actually uses, clamping to the buffer's capacity. This translate-on-apply is the
  bridge between "managed array is convenient to edit and transport" and "the live component is
  unmanaged and fixed-size."

So the managed/blittable boundary is crossed exactly once, at apply time, on the authoritative node —
not in the UI, not on the wire. The DDS payload is JSON throughout (Tuning DD §4.1), so the transport
is identical to the scalar case; only the apply handler for curve tunables gains the array→fixed-buffer
copy. Capacity overflow (more authored points than the fixed buffer holds) is a clamp-with-warning at
apply, surfaced like any other rejected tuning commit (Tuning DD §5.3).

---

## 7. What stays unchanged

- **DDS transport.** The widget edits a `UtilityCurve` inside the same synthesized DTO; commits ride
  the same `GizmoInteractionBatch` path (Tuning DD §4.1). The transport never sees the difference
  between a scalar edit and a widget edit — both arrive as a JSON-committed `EditDocument`.
- **Determinism.** A widget-driven change is still enqueued at frame top and recorded as a
  `TuningChangeEvent` (Tuning DD §5.2/§5.4). The richer UI changes nothing about the recorded-input
  discipline.
- **The Core/Presentation split.** We add two plugin classes; we do not modify `StructEdit` itself —
  the same constraint the Blueprint editor honored ("we add drawers; we don't extend StructEdit").

---

## 8. Resolved questions

- **C-1. `UtilityCurve` location — RESOLVED.** Shared `Fdp.Toolkits` AI toolkit (the
  `Fdp.Toolkit.Behavior` namespaces, beside the other Utility runtime contracts). StructEdit binds
  custom editors by CLR type, so the type must be visible to the simulation runtime, the editor, and
  the presentation layer simultaneously; the shared toolkit is the one assembly all three already
  reference, avoiding a layering violation.
- **C-2. Piecewise points in the tuning DTO — RESOLVED.** Managed DTO + `EditNodeKind.DynamicArray`
  on the edit side; translate the variable-length array into the fixed-size blittable component buffer
  at frame-top apply. (§6)
- **C-3. Comparison overlay in the console — RESOLVED.** Opt-in `CurveWidgetOptions.ShowComparisonOverlay`,
  default `false`. The console has no baseline "old version," so it passes the widget options with the
  overlay off; the Utility Editor turns it on for comparison mode (Editor DD §5.5).

---

*End of Curve Editor in StructEdit guide v1.1. Resolves Tuning DD open question T-2 (visual widget,
not scalars-forever) and depends on the Utility Editor DD §5 curve widget as the shared artifact.
The two StructEdit plugin classes follow the `GuidFieldEditor` / `QuaternionEulerFieldDrawer`
precedents exactly.*
