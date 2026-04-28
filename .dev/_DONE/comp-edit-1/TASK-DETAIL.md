# Component Editor — Task Detail

**Reference design:** [DESIGN.md](./DESIGN.md)

---

## Phase 1: StructEdit Core Extensions

---

### TASK-CE01: NestedMemberBinding

**Design Reference:** DESIGN.md § Phase 1 — 1.1 `NestedMemberBinding`

**Scope**

Create `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/NestedMemberBinding.cs`.
No other files in `StructEdit.Core` or `StructEdit.Reflection` are modified by this task.

**What is NOT included:**
- Changes to `ReflectionEditDocumentBuilder` (TASK-CE03).
- Any test project changes (tests live in TASK-CE03's verification).

**Constraints**

- Class must be `internal sealed` inside the `StructEdit.Core.Bindings` namespace.
- Implements `IValueBinding` exactly (no new interface members).
- `ValueType` is the member's field/property type (not the parent's type).
- `GetBoxed()`: get the parent value via `_parent.GetBoxed()`, then read the member from it.
  Return `null` if the parent value is `null`.
- `SetBoxed(value)`: get the parent value, mutate the member, **then if `_parent.ValueType.IsValueType`
  write the (now-mutated) boxed struct back to `_parent.SetBoxed(parentObj)`**. This is the
  critical correctness guarantee for structs in arrays.
- `TryGetSpan`: always returns `false`/default (no native memory access).
- Constructor: `(MemberInfo member, IValueBinding parent)`.  
  `member` may be `FieldInfo` or `PropertyInfo`.
  Both paths must be handled in `GetBoxed` and `SetBoxed`.
- No `markDirty` callback needed; the parent binding's `SetBoxed` propagates the dirty signal
  through the existing binding chain.

**Success Conditions**

- `T-CE01a` (struct mutation propagated): given a `DynamicArrayBinding` of `Vector3[]` with one
  element, wrap element 0's binding in a `NestedMemberBinding` for the `X` field. Call `SetBoxed(9f)`.
  Verify that `parent.GetBoxed()` returns a collection whose first element has `X == 9f`.
- `T-CE01b` (class mutation not propagated): given a `ManagedFieldBinding` holding a reference
  type with a `Name` property, wrap it in a `NestedMemberBinding` for `Name`. Call
  `SetBoxed("hello")`. Verify `GetBoxed()` returns `"hello"` and the parent object's `Name`
  field reflects the change (reference type: no re-push required).
- `T-CE01c` (null parent): `GetBoxed()` returns `null` without throwing when `_parent.GetBoxed()`
  is `null`.

---

### TASK-CE02: EditNodeMetadata.CustomAttributes

**Design Reference:** DESIGN.md § Phase 1 — 1.2 `EditNodeMetadata.CustomAttributes`

**Scope**

Two file changes:
1. `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNodeMetadata.cs` — add property.
2. `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs` —
   update `ReadMetadata` to harvest all attributes.

**What is NOT included:**
- Any UI or application code.
- Changes to the JSON serialiser (`StructEdit.Json`).

**Constraints**

- Property signature: `public IReadOnlyList<Attribute> CustomAttributes { get; init; }`
- Default value: `Array.Empty<Attribute>()` (do not allocate a list for the common case).
- `EditNodeMetadata.Empty` static must remain valid and have `CustomAttributes` equal to
  `Array.Empty<Attribute>()`.
- `ReadMetadata` must remain backward-compatible: if no attributes are present at all
  (neither the known set nor custom ones), it must still return `EditNodeMetadata.Empty`
  (the same singleton, not a new allocation).
- When only custom (non-known) attributes are present, a new `EditNodeMetadata` record must
  be returned with only `CustomAttributes` populated; all other properties remain at their
  default values.
- `GetCustomAttributes(false)` is the correct reflection call (non-inherited).

**Success Conditions**

- `T-CE02a` (known-only attributes): a field decorated with `[EditRange(0, 1)]` only.
  `ReadMetadata` returns a record with `Min == 0`, `Max == 1`, and `CustomAttributes.Count == 0`.
- `T-CE02b` (custom attribute only): a field decorated with `[MyCustomAttribute]` (any custom
  attribute). `ReadMetadata` returns a record with `CustomAttributes.Count == 1` and
  `CustomAttributes[0]` is the instance of `MyCustomAttribute`.
- `T-CE02c` (mixed): a field with both `[EditUnit("m/s")]` and `[MyCustomAttribute]`.
  `ReadMetadata` returns `Unit == "m/s"`, `CustomAttributes.Count == 1`.
- `T-CE02d` (Empty singleton unchanged): `EditNodeMetadata.Empty.CustomAttributes` is
  `Array.Empty<Attribute>()` (reference-equal to `Array.Empty<Attribute>()`).

---

### TASK-CE03: Array Element Node Generation

**Design Reference:** DESIGN.md § Phase 1 — 1.3 Array Element Node Generation

**Scope**

Modify `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs`:

- `BuildNode` gains two optional parameters: `IValueBinding? explicitBinding = null` and
  `IValueBinding? parentBinding = null`.
- Add private static method `BuildArrayElements(IEditBuffer, string, IContainerBinding,
  Type, IdAllocator, HashSet<Type>, providers, fieldEditors, context)`.
- Update the `DynamicArray`, `InlineArray`, and `FixedBuffer` `case` blocks to call
  `BuildArrayElements` and store the result as `children`.
- Update `BuildChildren` to accept and propagate `parentBinding`.
- Update `CreateLeafBinding` to accept `parentBinding` and return a `NestedMemberBinding`
  when the buffer is managed, `parentBinding != null`, and `nativeOffset < 0`.

**What is NOT included:**
- `BuildArrayElements` does not need to handle the `FixedBuffer` provider-override path for
  element nodes (providers operate at the buffer level, not per-element).
- `EditDocumentJsonSerializer` is unchanged; it already skips iterating children for array kinds
  when reading/writing JSON.

**Constraints**

- `BuildArrayElements`: loops `i` from `0` to `cb.Count - 1`. For each `i`, calls
  `cb.GetElementBinding(i)` and passes it as `explicitBinding` to `BuildNode`. This call
  uses `nativeOffset: -1, fi: null, pi: null` so the binding factory path falls through to
  `NestedMemberBinding` for managed element structs.
- For element types that are themselves structs/classes/records, the recursive `BuildChildren`
  call must receive the element binding as `parentBinding`, so that leaf fields inside the
  element are backed by `NestedMemberBinding`.
- `CreateLeafBinding`: when `fi == null && pi == null` but `parentBinding != null` (the
  element-level path is not reached from a field/property), return `null` — the caller
  (`BuildNode`) already passes `explicitBinding` in that case so `binding` is already set.
- Existing tests in `StructEdit.Tests` must continue to pass unchanged.
- After `session.MarkStructuralChange()` + `session.RebuildDocument()`: the rebuilt document
  must reflect the new element count; bindings for elements that existed before the rebuild
  must still function correctly (values preserved because the underlying buffer was not
  discarded).

**Success Conditions**

- `T-CE03a` (primitive array nodes): `float[]` field with 3 elements. After session open,
  `root.Children` for that field node has `Count == 3`. Each child has `Kind == Scalar`,
  `ClrType == typeof(float)`, and `Binding.GetBoxed()` returns the correct value.
- `T-CE03b` (struct array nodes): `Vector3[]` field with 2 elements. Each element child has
  `Kind == Struct` and 3 children (`X`, `Y`, `Z`). Calling `SetBoxed(9f)` on the `X` child
  of element 0 and then `session.Commit()` returns a component whose `field[0].X == 9f`.
- `T-CE03c` (List<T>): `List<int>` field with 2 elements. Element children have
  `Kind == Scalar`. After `container.Resize(3)` + `MarkStructuralChange` +
  `RebuildDocument`, the rebuilt root has 3 element children.
- `T-CE03d` (InlineArray): an `[InlineArray(4)]` struct field with `float` element type.
  `node.Children.Count == 4`. `CanResize == false`.
- `T-CE03e` (FixedBuffer): a `fixed float[8]` field. `node.Children.Count == 8`.
  `CanResize == false`.
- `T-CE03f` (empty array): `float[]` field is `null` or empty. Node children count is `0`.
  No exception thrown.

---

## Phase 2: Picker Infrastructure

---

### TASK-CE04: Picker Attributes

**Design Reference:** DESIGN.md § Phase 2 — 2.1 Picker Attributes

**Scope**

Create one new file in `Fdp.Presentation`:
`FDP/Engine/Fdp.Presentation/ImGui/Editing/PickerAttributes.cs`

**What is NOT included:**
- Implementation of `IComponentPickerContext` (TASK-CE05).
- Any rendering logic that reads these attributes.

**Constraints**

- Namespace: `Fdp.Presentation.Editing` (new sub-namespace, consistent with planned
  `ComponentEditWindow` location).
- `[MapPickableEntity]`:  
  `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`  
  Constructor: `(params string[] filterPresets)`. Property: `FilterPresets`.  
  Default (no args): `filterPresets` is empty array.
- `[MapPickableWorldLocation]`:  
  `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`  
  No constructor parameters.
- Both classes must be `public sealed` so ECS component authors in other assemblies can use them.

**Success Conditions**

- `T-CE04a`: a field decorated with `[MapPickableEntity("tanks", "infantry")]` has
  `attribute.FilterPresets` equal to `["tanks", "infantry"]`.
- `T-CE04b`: a field decorated with `[MapPickableEntity]` (no args) has
  `attribute.FilterPresets.Length == 0`.
- `T-CE04c`: a field decorated with `[MapPickableWorldLocation]` has the attribute present
  and the class compiles with `AttributeTargets.Field`.

---

### TASK-CE05: IComponentPickerContext

**Design Reference:** DESIGN.md § Phase 2 — 2.2 `IComponentPickerContext`

**Scope**

Create one new file:
`FDP/Engine/Fdp.Presentation/ImGui/Editing/IComponentPickerContext.cs`

**What is NOT included:**
- Any concrete implementation of `IComponentPickerContext` (lives in application-layer
  assemblies outside `Fdp.Presentation`).
- Rendering logic that calls into the interface.

**Constraints**

- Namespace: `Fdp.Presentation.Editing`.
- All members are `public` on the interface.
- `string jsonPath` is the `EditNode.JsonPath` of the node that owns the pending pick. Using
  the stable semantic path (e.g., `"$.Targets[2].Location"`) rather than the transient
  sequential `EditNodeId.Value` prevents dangling pick requests when the document is rebuilt
  after an array element is removed and the sequential IDs shift.
- Method signatures exactly:

```csharp
bool IsPickPendingFor(string jsonPath);

void RequestEntityPick(string jsonPath, string[]? filterPresets);
void RequestLocationPick(string jsonPath);

bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity);
bool TryConsumeLocationPick(string jsonPath, out Vector3 location);
```

- `Fdp.Core.Entity` and `System.Numerics.Vector3` are the types for picked results.
- The interface carries no state, no default implementations.

**Success Conditions**

- `T-CE05a`: a mock implementation can be created in a test class and all five methods can
  be invoked without compilation error.
- `T-CE05b`: calling `TryConsumeEntityPick` with a `jsonPath` for which no pick is pending
  returns `false` and `out Entity` is `default(Entity)` in the null/NOP implementation.

---

## Phase 3: Component Edit Window

---

### TASK-CE06: Add StructEdit Project References

**Design Reference:** DESIGN.md § Phase 3 — 3.1 Project Reference

**Scope**

Edit `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj` only.

**What is NOT included:**
- Changes to `Fdp.Presentation.Tests.csproj` (the test project already transitively pulls
  `Fdp.Presentation`; StructEdit types are available via the project reference chain).
- Changes to any `Hrot.*` project files.

**Constraints**

- Add two `<ProjectReference>` entries to the existing `<ItemGroup>`:
  ```xml
  <ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Core\StructEdit.Core.csproj" />
  <ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Reflection\StructEdit.Reflection.csproj" />
  ```
- Both paths are relative to `FDP/Engine/Fdp.Presentation/`.
- Do not change any other element in the `.csproj`.
- After adding the references, `dotnet build FDP/FDP.sln --no-restore` must succeed with no
  new errors.

**Success Conditions**

- `T-CE06a`: `dotnet build` of `FDP.sln` succeeds after the reference is added.
- `T-CE06b`: `using StructEdit.Core;` compiles in a new `.cs` file inside `Fdp.Presentation`.
- `T-CE06c`: `using StructEdit.Reflection;` compiles in a new `.cs` file inside
  `Fdp.Presentation`.

---

### TASK-CE07: ComponentEditDrawer

**Design Reference:** DESIGN.md § Phase 3 — 3.2 `ComponentEditDrawer`

**Scope**

Create `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs`.

**What is NOT included:**
- The `ComponentEditWindow` that hosts the drawer (TASK-CE08).
- Picker attribute definitions (TASK-CE04/CE05).

**Constraints**

- Class: `internal sealed class ComponentEditDrawer`.
- Namespace: `Fdp.Presentation.Editing`.
- Constructor: `(IEditSession session, IComponentPickerContext? pickerCtx)`.
- The single public method `DrawEditNode` takes
  `(EditNode node, IContainerBinding? parentContainer = null, int elementIndex = -1)`.

**DrawEditNode rules:**

- `SelectionRoot`: skip drawing; iterate `node.Children` directly.
- Container kinds (`Struct`, `Class`, `Record`, `DynamicArray`, `InlineArray`, `FixedBuffer`):
  - Column 0: `ImGui.TreeNodeEx` with `SpanAvailWidth | DefaultOpen`.
  - Column 1: for container-binding nodes show `TextDisabled("[N]")`; when `CanResize`, append
    `"+ Add"` button aligned to the right of column 1 (`GetContentRegionAvail().X - 60`).
  - When `opened`, iterate `node.Children` with `DrawEditNode(child, currentContainer, i)`.
  - Call `TreePop()`.
- Leaf kinds (`Scalar`, `Boolean`, `String`, `Enum`):
  - Column 0: `Leaf | NoTreePushOnOpen | SpanAvailWidth`.
  - Column 1: `SetNextItemWidth(-float.Epsilon)` then call `DrawPrimitiveInput`.
  - After the input control, if `Metadata.CustomAttributes` contains
    `MapPickableEntityAttribute`: call picker rendering logic.
  - After the input control, if `Metadata.CustomAttributes` contains
    `MapPickableWorldLocationAttribute`: call picker rendering logic.
- `Guid`, `DateTime`, `Unsupported`, others: column 1 shows `TextDisabled(value.ToString())`.
- Array element deletion: when `parentContainer != null && parentContainer.CanResize && elementIndex >= 0`,
  render an `X` button after the value input. On click: `RemoveElementAtIndex(parentContainer,
  elementIndex)` then `_session.MarkStructuralChange()` + `_session.RebuildDocument()`.
- `ImGui.PushID(node.Id.Value)` / `PopID()` wraps column 1 rendering for each node.

**DrawPrimitiveInput rules** (`private bool DrawPrimitiveInput(Type type, ref object value, EditNodeMetadata meta)`):

- `float`: `InputFloat` or `SliderFloat` when both `meta.Min` and `meta.Max` are set. Step: 0.
- `int`: `InputInt` or `SliderInt` when range set.
- `double`: `InputDouble`.
- `long`/`ulong`/`short`/`ushort`/`byte`/`sbyte`: `InputInt` with explicit cast (no data loss
  within normal ranges; clip to type bounds on assignment).
- `bool`: `Checkbox`.
- `string`: `InputText` with 512-char buffer.
- `Enum`: `Combo` built from `Enum.GetNames(type)` and `Enum.GetValues(type)`.
- All others: `TextDisabled(value?.ToString() ?? "null")`, return `false`.

**Picker rendering rules** (called from leaf rendering after the input control):

```
ImGui.SameLine();
if (pickerCtx.IsPickPendingFor(node.JsonPath)):
    ImGui.TextDisabled("[Picking...]")
else:
    if (ImGui.Button("Pick Entity##nodeId")): pickerCtx.RequestEntityPick(node.JsonPath, filterPresets)
if (pickerCtx.TryConsumeEntityPick(node.JsonPath, out picked)):
    binding.SetBoxed(picked)
    changed = true
```

(analogous for world location).

**RemoveElementAtIndex** is a `private static` helper that shifts elements down and then
calls `container.Resize(container.Count - 1)`.

**Success Conditions**

- `T-CE07a` (float with range): an `EditNode` with `Kind == Scalar`, `ClrType == float`,
  `Metadata.Min = 0, Max = 100` renders a slider in column 1. Dragging updates the node's
  `Binding` value.
- `T-CE07b` (struct container): a struct node with two float children. Column 0 shows a tree
  node that expands. Children appear as leaf rows in the same table.
- `T-CE07c` (dynamic array): a `DynamicArray` node with 3 int children (from Phase 1
  TASK-CE03). All 3 children are rendered as rows. `+ Add` button appears. Clicking `X` on
  child 1 shifts element 2 into slot 1 and removes the last row.
- `T-CE07d` (enum combo): an `EditNode` with `Kind == Enum` renders a combo box listing the
  enum's names. Selecting a different name updates the binding.
- `T-CE07e` (picker button, no context): when `pickerCtx == null`, no `Pick *` button appears.
- `T-CE07f` (picker button, pending): when `pickerCtx.IsPickPendingFor(nodeId) == true`,
  `[Picking...]` text appears instead of the button.

---

### TASK-CE08: ComponentEditWindow

**Design Reference:** DESIGN.md § Phase 3 — 3.3 `ComponentEditWindow`

**Scope**

Create `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs`.

**What is NOT included:**
- The `DrawEditNode` renderer (TASK-CE07).
- Changes to `ComponentReflector` (TASK-CE09).

**Constraints**

- Class: `internal sealed class ComponentEditWindow : ManagedWindow`.
- Namespace: `Fdp.Presentation.Editing`.
- Constructor sets `IsVolatile = true`, `ShowInMenu = false`, `IsOpen = true`.
- Constructor signature:

```csharp
internal ComponentEditWindow(
    string id,
    string title,
    string owningPerspective,
    IEditSession session,
    Entity targetEntity,
    Type componentType,
    Func<IInspectableSession?> sessionGetter,
    IComponentPickerContext? pickerCtx = null)
    : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
```

- `DrawClientArea()` must:
  1. Call `sessionGetter()`. If result is `null` or `!result.IsAlive(targetEntity)`:
     call `CloseAndCleanup()` and return immediately.
  2. If `_session.RebuildState == EditRebuildState.RebuildRequired`: call
     `_session.RebuildDocument()` before rendering.
  3. Begin the 2-column table with flags and column setup matching `ImGuiPropertyTree.Render`:
     `Borders | RowBg | Resizable | SizingFixedFit`, columns `"Property"` (WidthFixed 180f)
     and `"Value"` (WidthStretch).
  4. Call `_drawer.DrawEditNode(_session.Document.Root)`.
  5. End the table.
  6. If `_errorMessage != null`: `ImGui.TextColored(red, _errorMessage)`.
  7. `ImGui.Separator()`.
  8. OK button / `ImGuiKey.Enter`:
     - Call `_session.Commit()`.
     - Re-evaluate: `var liveSession = _sessionGetter();`
     - If `liveSession != null && liveSession.IsAlive(_targetEntity)`:
       call `liveSession.SetComponent(_targetEntity, _componentType, newState)`.
     - `CloseAndCleanup()`.
     - On `EditValidationException`: set `_errorMessage` from `ex.Result.Errors[0].Message`
       (or similar; do not crash). Do **not** call `CloseAndCleanup()` on validation failure
       so the user can correct the value.
  9. `ImGui.SameLine()`.
  10. Cancel button / `ImGuiKey.Escape`: `CloseAndCleanup()`.
- `CloseAndCleanup()`: `_session.Dispose()`, `IsOpen = false`.
- `_drawer` is a `ComponentEditDrawer` created in the constructor.

**Success Conditions**

- `T-CE08a` (volatile/menu flags): after construction, `IsVolatile == true` and
  `ShowInMenu == false`.
- `T-CE08b` (liveness guard): when `sessionGetter` returns a session where
  `session.IsAlive(entity) == false`, `DrawClientArea` calls `IsOpen = false` (verified by
  inspecting the field via the internal test accessor) without throwing.
- `T-CE08c` (liveness guard null): when `sessionGetter` returns `null`, same result as above.
- `T-CE08d` (rebuild delegation): when `_session.RebuildState == RebuildRequired` at the start
  of `DrawClientArea`, `_session.RebuildDocument()` is called before any drawing occurs (verify
  by tracking call order on a mock session).
- `T-CE08e` (CloseAndCleanup after cancel): verify `session.Dispose()` is called and
  `IsOpen == false` after the Cancel path executes.
- `T-CE08f` (validation error retained): after an `EditValidationException` is caught, `IsOpen`
  remains `true` and `_errorMessage` is non-null.
- `T-CE08g` (mid-frame session disposal): when `_sessionGetter()` returns `null` at the moment
  the OK button is pressed (session disposed between liveness check and commit), `SetComponent`
  is NOT called and `CloseAndCleanup()` is called without throwing.

---

## Phase 4: Wiring

---

### TASK-CE09: ComponentReflector Double-Click Integration

**Design Reference:** DESIGN.md § Phase 4 — 4.1 `ComponentReflector` Double-Click Detection

**Scope**

Two file modifications:
1. `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`
2. `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs` — extend `Render` with an
   `out string? doubleClickedPath` parameter to expose field-row double-click events.

**What is NOT included:**
- Changes to `EntityInspectorPanel` or `EntityWatchPanel` (TASK-CE10).

**Constraints**

- Add three `public` nullable properties to `ComponentReflector`:
  ```csharp
  public WindowManager? EditWindowManager { get; set; }
  public Func<IInspectableSession?>? EditSessionGetter { get; set; }
  public IComponentPickerContext? EditPickerContext { get; set; }
  public string EditOwningPerspective { get; set; } = string.Empty;
  ```
- Add a `private readonly IComponentEditService _editService` field, initialised via
  `new ComponentEditServiceBuilder().Build()` in the constructor (or as a field initialiser).
- Extend `ImGuiPropertyTree.Render` (or add an overload) with `out string? doubleClickedPath`.
  Inside the render, after each leaf/node `TreeNodeEx` call, check
  `IsItemHovered() && IsMouseDoubleClicked(Left)` and set `doubleClickedPath = node.JsonPath`
  if true, then return early from that row. `doubleClickedPath` is `null` when no row was
  double-clicked during that frame.
- In `DrawComponents`, double-click detection works at two levels:

```csharp
// Level 1: field-row double-click captured from ImGuiPropertyTree.Render:
// ImGuiPropertyTree.Render(data, contextType: type, out string? doubleClickedPath);

// Level 2: component header double-click
// bool open = ImGuiApi.CollapsingHeader(label);   <- existing line
bool headerDoubleClicked = ImGuiApi.IsItemHovered()
    && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left);

if (!session.IsReadOnly
    && EditWindowManager != null
    && EditSessionGetter != null
    && data != null
    && (doubleClickedPath != null || headerDoubleClicked))
{
    string winId = $"cedit_{e.Index}_{e.Generation}_{type.FullName}";
    if (EditWindowManager.TryGetWindow(winId, out _))
    {
        EditWindowManager.FocusWindow(winId);
    }
    else
    {
        EditScope scope = doubleClickedPath != null
            ? EditScope.ForField(EditPath.Parse(doubleClickedPath))
            : EditScope.WholeComponent;
        var editSession = _editService.Open(data, type, scope);
        string title = $"Edit {type.Name} [{e.Index}]";
        EditWindowManager.RegisterWindow(new ComponentEditWindow(
            winId, title, EditOwningPerspective, editSession,
            e, type, EditSessionGetter, EditPickerContext));
    }
}
```
- The `_editService` field must be created once (not per `DrawComponents` call) — the service
  is stateless.
- When `data == null` for a component, skip the double-click check (no data to clone).
- The Level 2 (header) check must appear **after** `CollapsingHeader` and **before** any
  other ImGui call in the same loop iteration so that `IsItemHovered` still refers to the
  header item.
- `OwningPerspective` passed to `ComponentEditWindow` must match the perspective of the
  host window; pass `EditOwningPerspective` in the constructor.

**Success Conditions**

- `T-CE09a` (no-op when read-only): when `session.IsReadOnly == true`, even if all other
  conditions are met, no window is registered in `EditWindowManager`.
- `T-CE09b` (no-op when manager null): when `EditWindowManager == null`, no exception is thrown
  (guard check passes cleanly).
- `T-CE09c` (window registered on header double-click): when session is writable,
  `EditWindowManager` is set, and the header `ImGui.IsMouseDoubleClicked` returns `true`
  (mocked/overridden in test), a window is registered with whole-component scope.
- `T-CE09d` (focus, not duplicate): when the same component is double-clicked a second time
  (window already registered), `FocusWindow` is called and no second window is registered.
- `T-CE09e` (deterministic ID format uses FullName): verify that for `Entity(Index=3,
  Generation=2)` and component type `MyNs.SimTransform` (with `FullName ==
  "MyNs.SimTransform"`), the window ID is `"cedit_3_2_MyNs.SimTransform"`.
- `T-CE09f` (scoped open on field-row double-click): when `ImGuiPropertyTree.Render` returns
  `doubleClickedPath == "$.Position.X"`, the edit session is opened with
  `EditScope.ForField(EditPath.Parse("$.Position.X"))`, not whole-component scope.

---

### TASK-CE10: Host Wiring (EntityInspectorPanel + EntityWatchPanel)

**Design Reference:** DESIGN.md § Phase 4 — 4.2 Host Wiring

**Scope**

Two file modifications:
1. `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
2. `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs`

**What is NOT included:**
- Any `Hrot.*` subsystem files. Those are application-layer wiring that is out of scope for
  this workstream; the injectable properties on `ComponentReflector` are enough for host teams
  to wire up.

**Constraints**

- Expose `ComponentReflector` as a `public` property on both panel classes:
  ```csharp
  // EntityInspectorPanel
  public ComponentReflector Reflector => _reflector;

  // EntityWatchPanel
  public ComponentReflector Reflector => _reflector;
  ```
- Do NOT change the constructor signature of either panel class.
- Do NOT move or rename the existing `_reflector` field.
- The property must be `public` so that host subsystems (in `Hrot`) can set
  `panel.Reflector.EditWindowManager = ...` etc. from outside `Fdp.Presentation`.

**Success Conditions**

- `T-CE10a`: `new EntityInspectorPanel().Reflector` returns a non-null `ComponentReflector`.
- `T-CE10b`: `new EntityWatchPanel(someEntity).Reflector` returns a non-null `ComponentReflector`.
- `T-CE10c`: setting `panel.Reflector.EditWindowManager = mockManager` from an external
  assembly compiles without errors (verifies `ComponentReflector` is accessible and the
  property is truly public-facing).
- `T-CE10d`: existing `EntityInspectorPanelTests` continue to pass unchanged (no regressions
  from adding the `Reflector` property).
