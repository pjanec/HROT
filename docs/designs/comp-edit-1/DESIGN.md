# Component Editor — Design

**Workstream:** `comp-edit-1`
**Status:** Planned

---

## Overview

Integrate the `StructEdit` library into the `Fdp.Presentation` Entity Inspector and Entity Watch
panel to enable **in-place editing of ECS component fields**. The result is a non-blocking,
floating editor window that opens when the operator double-clicks any component row in the
property table.

### Key design properties

- **Atomic ECS commits.** StructEdit clones the entire component on session open. "OK" writes back
  the whole, validated component via `IInspectableSession.SetComponent`. Partial overwrites are
  impossible.
- **No reflection in the render loop.** StructEdit runs its reflection pass once at session open and
  builds an immutable `EditDocument` tree. The ImGui renderer only reads/writes via
  `IValueBinding`, never via `MemberInfo`.
- **Non-blocking floating window.** The editor is a volatile `ManagedWindow` (not a modal popup).
  The map canvas and all other panels remain interactive, enabling asynchronous map-picking.
- **Sub-field scoping.** Double-clicking a specific field row in the read-only property table
  opens an editor scoped to that field's path (`EditScope.ForField`). Double-clicking the
  component header itself opens the whole-component scope as a fallback.
- **Single editor per component instance.** A deterministic window ID keyed on
  `entity.Index + entity.Generation + componentType.FullName` prevents duplicate edit buffers
  for the same component. Double-clicking a field that is already being edited brings the
  existing window to focus.
- **Entity liveness guard.** Every frame the window checks `session.IsAlive(entity)`. If the
  entity was destroyed, the window self-terminates and releases the cloned buffer.

---

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Edit window style | Floating volatile `ManagedWindow` | Matches `FdpEntityWatchWindow` pattern; allows async map-picking |
| Duplicate-prevention | Deterministic ID + `WindowManager.TryGetWindow` | Prevents two edit buffers for the same component |
| Array item nodes | StructEdit generates `EditNode` children for array elements | UI layer stays free of reflection; renderer is a pure dumb tree |
| Picker integration | `IComponentPickerContext` injection + `EditNodeMetadata.CustomAttributes` | Keeps spatial logic out of StructEdit and the render loop |
| `IsReadOnly` guard | Do not open editor on read-only sessions | `SimulationViewAdapter.IsReadOnly == true`; no writes allowed there |

---

## Phase 1: StructEdit Core Extensions

**Goal:** Extend StructEdit's data model and builder to natively generate the full `EditDocument`
tree down to leaf nodes inside arrays, and to surface domain-specific attributes through metadata.

### 1.1 `NestedMemberBinding`

Add `NestedMemberBinding` to `StructEdit.Core/Bindings/`. This binding wraps an existing
`IValueBinding` (e.g., an array element binding) and exposes one public field or property of
the parent value. When the parent is a value type (struct), `SetBoxed` must push the mutated
boxed struct back to the parent binding after setting the member — otherwise the copy-on-write
semantics of structs silently swallow the mutation.

**Scope:** `StructEdit.Core` only. No UI or application code changed.

### 1.2 `EditNodeMetadata.CustomAttributes`

Add a `CustomAttributes` property (`IReadOnlyList<Attribute>`) to `EditNodeMetadata`. Default to
`Array.Empty<Attribute>()`. Update `ReflectionEditDocumentBuilder.ReadMetadata` to harvest all
attributes from the field or property (not just the known set) and store them in the new list.
This allows domain-specific attributes (`[MapPickableEntity]`, `[MapPickableWorldLocation]`)
to flow through to the UI renderer without StructEdit knowing about them.

**Scope:** `EditNodeMetadata` record in `StructEdit.Core`; `ReadMetadata` method in
`StructEdit.Reflection`.

### 1.3 Array Element Node Generation in `ReflectionEditDocumentBuilder`

Extend `BuildNode` in `ReflectionEditDocumentBuilder` to recursively generate `EditNode` children
for `DynamicArray`, `InlineArray`, and `FixedBuffer` node kinds, using `NestedMemberBinding`
(1.1) to build sub-nodes for complex struct elements:

- For each index `i` in `IContainerBinding.Count`, create a child `EditNode` named `[i]` by
  calling `BuildNode` recursively with the element's `IValueBinding` as the `explicitBinding`
  and the container binding as the `parentBinding`.
- When an element type is itself a struct/class/record, `BuildChildren` is called again,
  propagating the `parentBinding` so that leaf fields are backed by `NestedMemberBinding`
  instances rather than dead-end unmanaged offsets.
- After `MarkStructuralChange` + `RebuildDocument`, the rebuilt tree reflects the new element
  count without losing unsaved edits elsewhere in the buffer.

**Scope:** `StructEdit.Reflection` only. No UI or application code changed. `BuildNode` signature
gains two optional parameters (`explicitBinding`, `parentBinding`). Existing callers are unaffected
by default-null parameters.

---

## Phase 2: Picker Infrastructure

**Goal:** Define the abstraction layer that decouples the component editor from the application's
spatial and entity-selection mechanics.

### 2.1 Picker Attributes

Define two attributes in a suitable namespace inside `Fdp.Presentation` (or a shared layer
accessible to component types):

- `[MapPickableEntityAttribute(string[]? filterPresets = null)]` — marks a field whose value is
  an entity reference that should offer a "Pick Entity" button in the editor.
- `[MapPickableWorldLocationAttribute]` — marks a field whose value is a world coordinate
  (e.g., `Vector3`) that should offer a "Pick Map" button in the editor.

These attributes are placed on ECS component fields by the component author. StructEdit
Phase 1 (task 1.2) carries them opaquely through `EditNodeMetadata.CustomAttributes` to the
renderer.

### 2.2 `IComponentPickerContext`

Define an interface `IComponentPickerContext` in `Fdp.Presentation`. The interface is the
contract between the component editor window and the application's map / entity-selection
services:

```
bool IsPickPendingFor(string jsonPath)

void RequestEntityPick(string jsonPath, string[]? filterPresets)
void RequestLocationPick(string jsonPath)

bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity)
bool TryConsumeLocationPick(string jsonPath, out Vector3 location)
```

Requests are keyed on `EditNode.JsonPath` (e.g., `"$.Targets[2].Location"`) rather than the
transient sequential `EditNodeId.Value`. This makes pending picks survive a `RebuildDocument`
call — the stable semantic path uniquely identifies the field regardless of how the ID allocator
runs after an array element is removed.

The editor renderer calls `IsPickPendingFor` to display a `[Picking...]` status label and
`TryConsume*` to apply the result the frame after the pick resolves. Because the window is
non-blocking (Phase 3), the operator can freely interact with the map while the pick is pending.

Implementations of this interface live in the application layer (e.g., `Hrot.IG`, `Hrot.ExCon`).
The component editor holds only the interface reference.

---

## Phase 3: Component Edit Window

**Goal:** Implement the floating editor window and its ImGui renderer.

### 3.1 Project Reference

Add `ProjectReference` entries for `StructEdit.Core` and `StructEdit.Reflection` to
`Fdp.Presentation.csproj`. Both target `net8.0`, matching `Fdp.Presentation`. Neither has
further transitive dependencies beyond BCL types.

### 3.2 `ComponentEditDrawer`

Implement an `internal sealed class ComponentEditDrawer` in `Fdp.Presentation`. This class is
responsible for the recursive ImGui rendering of a `StructEdit` `EditDocument`. It has no
instance state that persists across frames other than a reference to the active `IEditSession`
and the `IComponentPickerContext`.

**Visual style:** identical to `ImGuiPropertyTree.Render`:

```
ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit

Column 0: "Property"  WidthFixed  = ImGuiPropertyTree.NameColWidth (180 f)
Column 1: "Value"     WidthStretch
```

**`DrawEditNode(EditNode node, IContainerBinding? parentContainer, int elementIndex)`** is the
single recursive method. It covers:

- `SelectionRoot` — invisible wrapper; iterate children directly.
- **Container nodes** (`Struct`, `Class`, `Record`, `DynamicArray`, `InlineArray`,
  `FixedBuffer`) — column 0: foldable `TreeNodeEx`; column 1: `[count]` for arrays plus
  `+ Add` button when `CanResize`. Children are drawn by recursing through `node.Children`
  (which StructEdit now generates for all array elements, per Phase 1 task 1.3).
- **Leaf nodes** (`Scalar`, `Boolean`, `String`, `Enum`, `Guid`, `DateTime`) — column 0:
  `Leaf | NoTreePushOnOpen` tree node; column 1: appropriate input control:
  - `float`/`int`/`double`: `InputFloat/InputInt/InputDouble` or `SliderFloat/SliderInt`
    when `Metadata.Min` and `Metadata.Max` are set.
  - `bool`: `Checkbox`.
  - `string`: `InputText`.
  - `Enum`: `Combo`.
  - `Guid`/`DateTime`: display read-only text (editable in v2).
- **Picker buttons** — inspected on every leaf node. If `Metadata.CustomAttributes` contains
  `[MapPickableEntityAttribute]` or `[MapPickableWorldLocationAttribute]`, append a `Pick Entity`
  or `Pick Map` button and poll `IComponentPickerContext` to consume the result. Picker buttons
  are suppressed when `_pickerCtx == null`.
- **Array element deletion** — when `parentContainer != null && CanResize`, render an `X` button
  that calls `RemoveElementAtIndex` (shift-down then `Resize(n-1)`) and triggers
  `MarkStructuralChange` + `RebuildDocument`.

**`DrawPrimitiveInput`** is a private helper that maps `(Type, ref object, EditNodeMetadata)`
to the correct ImGui control and returns `true` on change. Used by both leaf nodes and (via the
unified `DrawEditNode`) array element primitives — **no duplication**.

### 3.3 `ComponentEditWindow`

Implement `internal sealed class ComponentEditWindow : ManagedWindow` in `Fdp.Presentation`.

Constructor parameters:
- `string id` — deterministic window ID.
- `string title` — e.g., `"Edit Transform [42]"`.
- `string owningPerspective`
- `IEditSession session` — owns the cloned edit buffer.
- `Entity targetEntity`
- `Type componentType`
- `Func<IInspectableSession?> sessionGetter` — same pattern as `FdpEntityWatchWindow`.
- `IComponentPickerContext? pickerCtx` — optional; null disables picker buttons.

Constructor sets `IsVolatile = true`, `ShowInMenu = false`, `IsOpen = true`.

`DrawClientArea()` logic:

1. **Liveness guard**: call `sessionGetter()?.IsAlive(targetEntity)`. If null or false, call
   `CloseAndCleanup()` and return.
2. **Session rebuild**: if `session.RebuildState == EditRebuildState.RebuildRequired`, call
   `session.RebuildDocument()` before rendering.
3. **Property table**: delegate to `ComponentEditDrawer.DrawEditNode` for `session.Document.Root`.
4. **Error label**: show validation error message in red if non-null.
5. **OK button** (also `ImGuiKey.Enter`): call `session.Commit()`, then re-evaluate
   `sessionGetter()` and verify `IsAlive(targetEntity)` — the ECS repository may have been
   disposed or the entity destroyed between the frame start and the commit. Only if the
   re-evaluated session is non-null and alive call `SetComponent(targetEntity, componentType,
   newState)`. Call `CloseAndCleanup()` in all non-exception paths. Catch
   `EditValidationException`, set `_errorMessage`, and do **not** close the window so the
   user can correct the value.
6. **Cancel button** (also `ImGuiKey.Escape`): call `CloseAndCleanup()`.

`CloseAndCleanup()`: `session.Dispose()`, `IsOpen = false`.

---

## Phase 4: Wiring — ComponentReflector Integration

**Goal:** Detect double-click events in the existing component property table and spawn or focus
the appropriate `ComponentEditWindow`.

### 4.1 `ComponentReflector` Double-Click Detection

Extend `ComponentReflector` with four optional injectable properties:

```csharp
public WindowManager? EditWindowManager { get; set; }
public Func<IInspectableSession?>? EditSessionGetter { get; set; }
public IComponentPickerContext? EditPickerContext { get; set; }
public string EditOwningPerspective { get; set; } = string.Empty;
```

Double-click detection happens at **two levels**:

**Level 1 — field row (scoped):** `ImGuiPropertyTree.Render` is extended with an `out string?
doubleClickedPath` parameter. Inside the render, after each row's `TreeNodeEx`/leaf call, if
`IsItemHovered() && IsMouseDoubleClicked(Left)`, the method sets `doubleClickedPath` to
`node.JsonPath` (e.g., `"$.Position.X"`) and returns. `ComponentReflector` reads this value
after each `Render` call.

**Level 2 — component header (whole-component fallback):** Immediately after the `CollapsingHeader`
call (while the header is still the "last item" for `ImGui.IsItemHovered`), check
`IsItemHovered && IsMouseDoubleClicked` to catch a click on the header itself.

When either level triggers and the standard guards pass (`!IsReadOnly`, `EditWindowManager != null`,
`EditSessionGetter != null`, `data != null`):
- Compute deterministic ID: `$"cedit_{e.Index}_{e.Generation}_{type.FullName}"`.
- If `EditWindowManager.TryGetWindow(id, out _)`: call `EditWindowManager.FocusWindow(id)`.
- Otherwise:
  - Level 1: `_editService.Open(data, type, EditScope.ForField(EditPath.Parse(doubleClickedPath)))`.
  - Level 2 (header): `_editService.Open(data, type)` (whole-component scope).
  - Instantiate `ComponentEditWindow`, call `EditWindowManager.RegisterWindow(...)`.

The `IComponentEditService` instance (`ComponentEditServiceBuilder().Build()`) is created once
as a private field of `ComponentReflector` — the service is stateless and safe to share.

### 4.2 Host Wiring

`EntityInspectorPanel` and `EntityWatchPanel` each own a `ComponentReflector` instance. The
host code that constructs these panels (subsystems in `Hrot`) must set the four injectable
properties when editing should be available. There is no change to the public API of
`EntityInspectorPanel` or `EntityWatchPanel`; the wiring is done via the existing property setters
exposed on `ComponentReflector`.

Because `ComponentReflector` is `internal` to `Fdp.Presentation`, host code accesses it
through a new `public ComponentReflector Reflector { get; }` property on `EntityInspectorPanel`
and `EntityWatchPanel` — thin read-only accessors to the already-private instances.

---

## Cross-Cutting Constraints

1. **No UI code in StructEdit.** StructEdit has no dependency on ImGui or any UI framework.
2. **No reflection in the ImGui render loop.** All field discovery happens at session open inside
   `ReflectionEditDocumentBuilder`. The renderer reads only `EditNode`, `IValueBinding`, and
   `EditNodeMetadata`.
3. **Single edit buffer per component.** Enforced via deterministic window ID in
   `WindowManager`.
4. **`IsReadOnly` gate.** The double-click handler in `ComponentReflector` checks
   `session.IsReadOnly` before opening any editor. Read-only sessions (e.g.,
   `SimulationViewAdapter`) never launch editors.
5. **Struct element mutation.** `NestedMemberBinding.SetBoxed` always writes the modified
   boxed struct back to its parent binding when `ValueType.IsValueType`, preserving the
   copy-on-write invariant of C# value types in boxed array slots.
