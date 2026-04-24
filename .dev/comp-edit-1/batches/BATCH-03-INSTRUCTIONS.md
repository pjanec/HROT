# BATCH-03: ComponentEditDrawer + ComponentEditWindow

**Batch Number:** BATCH-03
**Tasks:** TASK-CE07, TASK-CE08
**Phase:** Phase 3 — Component Edit Window (core rendering + window host)
**Estimated Effort:** 8-10 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (StructEdit extensions), BATCH-02 (picker attributes/interface + project refs)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
3. **Onboarding:** `.dev/comp-edit-1/ONBOARDING.md`
4. **Previous Review:** `.dev/comp-edit-1/reviews/BATCH-02-REVIEW.md`
5. **Design (Phase 3):** `.dev/comp-edit-1/DESIGN.md` — § "Phase 3: Component Edit Window"
6. **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` — TASK-CE07, TASK-CE08

### Source Code Reference Files

Study these BEFORE writing any code:

- **`ManagedWindow` base class:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs`
- **Volatile window precedent:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs`
  and the `FdpEntityWatchWindow` window class (find via search) for the `IsVolatile=true/ShowInMenu=false/IsOpen=true` pattern.
- **`ImGuiPropertyTree.Render`:** `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs`
  to understand the 2-column table setup (Borders|RowBg|Resizable|SizingFixedFit, columns
  "Property" WidthFixed 180f and "Value" WidthStretch).
- **`IEditSession`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/IEditSession.cs`
- **`EditRebuildState`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditRebuildState.cs`
- **`EditValidationException`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditValidationException.cs`
- **`ValidationResult`/`ValidationError`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/ValidationResult.cs`
  and `ValidationError.cs` — `ValidationError` is `record(string JsonPath, string Message)`.
- **`EditNode`, `EditDocument`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditDocument.cs`
  and `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNode.cs`
- **`IContainerBinding`, `IValueBinding`:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/`
- **`IComponentPickerContext`:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/IComponentPickerContext.cs` (BATCH-02)
- **`PickerAttributes.cs`:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/PickerAttributes.cs` (BATCH-02)
- **`IInspectableSession`:** `FDP/Engine/Fdp.Presentation/Abstractions/IInspectableSession.cs`
- **`WindowManager`:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`
  (has `RegisterWindow(ManagedWindow)`, `TryGetWindow(string, out ManagedWindow)`, `FocusWindow(string id)`)

### New Files to Create

All in `FDP/Engine/Fdp.Presentation/ImGui/Editing/` (folder already exists from BATCH-02):

- `ComponentEditDrawer.cs` (CE07)
- `ComponentEditWindow.cs` (CE08)

Tests go in `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/` (subfolder already exists from BATCH-02):

- `ComponentEditDrawerTests.cs` (CE07)
- `ComponentEditWindowTests.cs` (CE08)

### Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Incremental build — use after each task
dotnet build FDP/FDP.sln --no-restore

# Run Fdp.Presentation tests
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj

# Full solution build — required before report
dotnet build IOS-IG-SimHost.sln --no-restore

# Full solution tests — required before report
dotnet test IOS-IG-SimHost.sln
```

### Known pre-existing failure

`Fdp.Toolkit.Vis2D.Tests.Layers.EntityRenderLayerTests.EntityRenderLayer_HitTest_FindsClosest`
fails before and after this batch. Ignore it when counting regressions.

### Report Submission

When done: `.dev/comp-edit-1/reports/BATCH-03-REPORT.md`

---

## MANDATORY WORKFLOW

1. **TASK-CE07** (ComponentEditDrawer) → build → all CE07 tests pass.
2. **TASK-CE08** (ComponentEditWindow) → build → all CE07 + CE08 tests pass.
3. Full solution build succeeds. Full test suite passes (minus pre-existing failure).
4. Write and submit report.

Fix all compilation errors before moving to the next task.

---

## Context

`ComponentEditDrawer` is a pure ImGui renderer that walks an `EditDocument`/`EditNode` tree.
It has no ManagedWindow logic. `ComponentEditWindow` hosts the drawer inside a volatile floating
window and handles the session lifecycle (liveness guard, rebuild, commit/cancel).

CE07 tests are unit tests that create `EditDocument` objects directly via StructEdit APIs
and call `DrawEditNode`. They do NOT test ImGui rendering output — they test the logic around
input handling, picker context routing, and element deletion. Prefer testing the helper methods
and any non-rendering logic; for ImGui calls, use mocks or skip assertions on visual output.

CE08 tests also do not test actual ImGui rendering. They test the liveness guard, rebuild
delegation, dispose-on-cancel, validation-error retention, and the mid-frame disposal guard.
Use a mock `IEditSession` that lets you control `RebuildState` and throw `EditValidationException`.

---

## Tasks

---

### TASK-CE07: ComponentEditDrawer

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs` (NEW)
**Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §TASK-CE07
**Design:** `.dev/comp-edit-1/DESIGN.md` §3.2

**Class signature:**

```csharp
namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

internal sealed class ComponentEditDrawer
{
    private readonly IEditSession _session;
    private readonly IComponentPickerContext? _pickerCtx;

    internal ComponentEditDrawer(IEditSession session, IComponentPickerContext? pickerCtx)
    {
        _session   = session;
        _pickerCtx = pickerCtx;
    }

    public void DrawEditNode(EditNode node,
        IContainerBinding? parentContainer = null,
        int elementIndex = -1) { ... }

    private bool DrawPrimitiveInput(Type type, ref object value, EditNodeMetadata meta) { ... }

    private static void RemoveElementAtIndex(IContainerBinding container, int index) { ... }
}
```

**DrawEditNode implementation details:**

- `SelectionRoot` kind: skip the node's own rendering; recurse `node.Children` by calling
  `DrawEditNode(child)` for each.
- Container kinds (`Struct`, `Class`, `Record`, `DynamicArray`, `InlineArray`, `FixedBuffer`):
  - `ImGuiApi.TableNextRow(); ImGuiApi.TableSetColumnIndex(0);`
  - `ImGuiApi.PushID(node.Id.Value);`
  - `bool opened = ImGuiApi.TreeNodeEx(node.Name, ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);`
  - `ImGuiApi.TableSetColumnIndex(1);`
  - If `node.ContainerBinding != null`: `ImGuiApi.TextDisabled($"[{node.ContainerBinding.Count}]");`
    Then if `node.ContainerBinding.CanResize`:
    ```csharp
    ImGuiApi.SameLine(ImGuiApi.GetContentRegionAvail().X - 60);
    if (ImGuiApi.SmallButton("+ Add"))
    {
        node.ContainerBinding.Resize(node.ContainerBinding.Count + 1);
        _session.MarkStructuralChange();
        _session.RebuildDocument();
    }
    ```
  - If `opened`: foreach child → `DrawEditNode(child, node.ContainerBinding, i)` (with index).
    Then `ImGuiApi.TreePop();`.
  - `ImGuiApi.PopID();`
- Leaf kinds (`Scalar`, `Boolean`, `String`, `Enum`):
  - `ImGuiApi.TableNextRow(); ImGuiApi.TableSetColumnIndex(0);`
  - `ImGuiApi.PushID(node.Id.Value);`
  - `ImGuiApi.TreeNodeEx(node.Name, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);`
  - `ImGuiApi.TableSetColumnIndex(1);`
  - `ImGuiApi.SetNextItemWidth(-float.Epsilon);`
  - Get current value: `object value = node.Binding.GetBoxed() ?? GetDefaultForType(node.ClrType);`
  - `bool changed = DrawPrimitiveInput(node.ClrType, ref value, node.Metadata);`
  - If `changed`: `node.Binding.SetBoxed(value);`
  - Array element delete button: if `parentContainer != null && parentContainer.CanResize && elementIndex >= 0`:
    ```csharp
    ImGuiApi.SameLine();
    if (ImGuiApi.SmallButton("X"))
    {
        RemoveElementAtIndex(parentContainer, elementIndex);
        _session.MarkStructuralChange();
        _session.RebuildDocument();
    }
    ```
  - Picker rendering (after input, before PopID):
    - Check `node.Metadata.CustomAttributes` for `MapPickableEntityAttribute`:
      ```csharp
      var entityAttr = node.Metadata.CustomAttributes.OfType<MapPickableEntityAttribute>().FirstOrDefault();
      if (entityAttr != null && _pickerCtx != null)
      {
          ImGuiApi.SameLine();
          if (_pickerCtx.IsPickPendingFor(node.JsonPath))
              ImGuiApi.TextDisabled("[Picking...]");
          else if (ImGuiApi.Button($"Pick Entity##{node.Id.Value}"))
              _pickerCtx.RequestEntityPick(node.JsonPath, entityAttr.FilterPresets.Length > 0 ? entityAttr.FilterPresets : null);
          if (_pickerCtx.TryConsumeEntityPick(node.JsonPath, out var pickedEntity))
          {
              node.Binding.SetBoxed(pickedEntity);
              changed = true;
          }
      }
      ```
    - Check for `MapPickableWorldLocationAttribute` analogously (call `RequestLocationPick` / `TryConsumeLocationPick`).
  - `ImGuiApi.PopID();`
- Unsupported / other kinds: column 0 leaf node with name, column 1 `TextDisabled(value?.ToString() ?? "null")`.

**DrawPrimitiveInput rules:**

```csharp
// float
if (type == typeof(float))
{
    float v = (float)value;
    bool ok = (meta.Min.HasValue && meta.Max.HasValue)
        ? ImGuiApi.SliderFloat("##v", ref v, (float)meta.Min.Value, (float)meta.Max.Value)
        : ImGuiApi.InputFloat("##v", ref v, 0f, 0f);
    if (ok) value = v;
    return ok;
}
// int: ImGuiApi.InputInt or SliderInt
// double: ImGuiApi.InputDouble
// long/ulong/short/ushort/byte/sbyte: InputInt with cast + type-bound clip
// bool: ImGuiApi.Checkbox
// string: ImGuiApi.InputText with 512-char buffer
// Enum: ImGuiApi.Combo from Enum.GetNames/GetValues
// all others: TextDisabled, return false
```

For `string`, ImGui.NET's `InputText` signature: `InputText(string label, ref string input, uint maxLength)`.

**RemoveElementAtIndex:**

```csharp
private static void RemoveElementAtIndex(IContainerBinding container, int index)
{
    // Shift elements down from index+1 to Count-1
    for (int i = index; i < container.Count - 1; i++)
    {
        var next = container.GetElementBinding(i + 1).GetBoxed();
        container.GetElementBinding(i).SetBoxed(next);
    }
    container.Resize(container.Count - 1);
}
```

**Note on ImGui context in tests:** Tests for CE07 cannot invoke actual ImGui rendering.
Test the non-ImGui logic only: picker routing, `RemoveElementAtIndex`, `DrawPrimitiveInput`'s
value mutation. Use the success conditions as a guide for what to verify without rendering.

**Success Conditions (T-CE07a through T-CE07f):**

- `T-CE07a` (float with range): verify that when `meta.Min = 0, meta.Max = 100`, the slider
  path is selected in `DrawPrimitiveInput`. Test this without ImGui by using a fake `EditNodeMetadata`
  and verifying the `SliderFloat` branch is chosen (e.g. by subclassing or by testing that the
  DrawPrimitiveInput return/value mutation behaves correctly with direct reflection).
  Acceptable alternative: verify `meta.Min.HasValue && meta.Max.HasValue` condition directly.
- `T-CE07b` (struct container): create an `IEditSession` for a struct with two float children
  (using `ComponentEditServiceBuilder().Build().Open(...)`). Verify `node.Children.Count == 2`.
  Do not call `DrawEditNode` directly (ImGui context unavailable in tests). Instead, verify
  the tree structure is correct and that `DrawEditNode` would traverse both children.
- `T-CE07c` (dynamic array element removal): create a mock `IContainerBinding` with `Count=3`,
  `CanResize=true`. Call `RemoveElementAtIndex(container, 1)` directly. Verify slot 1 now
  contains what was slot 2, and that `Resize(2)` was called.
- `T-CE07d` (enum combo): verify that `DrawPrimitiveInput` returns the list of names from
  `Enum.GetNames(enumType)` — test by creating a fake metadata and verifying the combo is
  reachable (check by testing the branching logic, not ImGui output).
- `T-CE07e` (picker null): when `pickerCtx == null`, no picker method is called. Verify by
  constructing a drawer with `pickerCtx=null` and confirming no NullReferenceException.
- `T-CE07f` (picker pending): when `pickerCtx.IsPickPendingFor(path) == true`, the
  `[Picking...]` branch is taken. Test via a mock `IComponentPickerContext` that returns `true`
  for `IsPickPendingFor`.

---

### TASK-CE08: ComponentEditWindow

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs` (NEW)
**Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §TASK-CE08
**Design:** `.dev/comp-edit-1/DESIGN.md` §3.3

**Class signature:**

```csharp
namespace Fdp.Presentation.Editing;

using Fdp.Core;
using Fdp.Presentation.WindowManager;
using ImGuiApi = ImGuiNET.ImGui;
using StructEdit.Core;

internal sealed class ComponentEditWindow : ManagedWindow
{
    private readonly IEditSession _session;
    private readonly Entity _targetEntity;
    private readonly Type _componentType;
    private readonly Func<IInspectableSession?> _sessionGetter;
    private readonly ComponentEditDrawer _drawer;
    private string? _errorMessage;

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
    {
        _session       = session;
        _targetEntity  = targetEntity;
        _componentType = componentType;
        _sessionGetter = sessionGetter;
        _drawer        = new ComponentEditDrawer(session, pickerCtx);

        IsVolatile  = true;
        ShowInMenu  = false;
        IsOpen      = true;
    }

    protected override void DrawClientArea() { ... }

    private void CloseAndCleanup() { ... }
}
```

**DrawClientArea implementation:**

```csharp
protected override void DrawClientArea()
{
    // 1. Liveness guard
    var liveCheck = _sessionGetter();
    if (liveCheck == null || !liveCheck.IsAlive(_targetEntity))
    {
        CloseAndCleanup();
        return;
    }

    // 2. Rebuild if needed
    if (_session.RebuildState == EditRebuildState.RebuildRequired)
        _session.RebuildDocument();

    // 3. Begin 2-column table
    if (ImGuiApi.BeginTable("##cedit", 2,
        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
        ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
    {
        ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
        ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // 4. Draw node tree
        _drawer.DrawEditNode(_session.Document.Root);

        // 5. End table
        ImGuiApi.EndTable();
    }

    // 6. Validation error banner
    if (_errorMessage != null)
        ImGuiApi.TextColored(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f), _errorMessage);

    // 7. Separator
    ImGuiApi.Separator();

    // 8. OK button
    if (ImGuiApi.Button("OK") || ImGuiApi.IsKeyPressed(ImGuiKey.Enter))
    {
        try
        {
            object newState = _session.Commit();
            var ls = _sessionGetter();
            if (ls != null && ls.IsAlive(_targetEntity))
                ls.SetComponent(_targetEntity, _componentType, newState);
            CloseAndCleanup();
        }
        catch (EditValidationException ex)
        {
            _errorMessage = ex.Result.Errors.Count > 0
                ? ex.Result.Errors[0].Message
                : "Validation failed.";
            // Do NOT close on validation failure.
        }
    }

    // 9. Same line + Cancel button
    ImGuiApi.SameLine();
    if (ImGuiApi.Button("Cancel") || ImGuiApi.IsKeyPressed(ImGuiKey.Escape))
        CloseAndCleanup();
}

private void CloseAndCleanup()
{
    _session.Dispose();
    IsOpen = false;
}
```

**Note on test approach:** `DrawClientArea` calls ImGui APIs that require a context. Tests must
NOT call `DrawClientArea` directly. Instead, use a `TestableComponentEditWindow` internal
subclass (or test-accessible methods) that exposes the logic without the ImGui calls.

The recommended approach: extract the testable logic into `internal` methods that tests call
directly. The tests listed in the success conditions only check:
- Constructor side-effects (`IsVolatile`, `ShowInMenu`)
- `CloseAndCleanup()` — call it directly
- `_errorMessage` state after a mocked commit throws `EditValidationException`
- Liveness guard logic — extract as internal helper or test via the existing `IsOpen`/`IsVolatile` flags

For the liveness guard (T-CE08b, T-CE08c, T-CE08d, T-CE08g), you can expose an
`internal void ExecuteDrawLogic(IInspectableSession? overrideSession = null)` method that
contains the `DrawClientArea` logic without the ImGui table/button rendering — or simply check
the state changes (IsOpen = false after CloseAndCleanup) by calling `CloseAndCleanup` directly.

Do not over-engineer the test infrastructure. Most tests verify that the right state transitions
happen; they do not need to simulate a full ImGui frame.

**Success Conditions (T-CE08a through T-CE08g):**

- `T-CE08a`: after `new ComponentEditWindow(...)`, `IsVolatile == true` and `ShowInMenu == false`.
- `T-CE08b`: when `sessionGetter` returns a session where `IsAlive(entity) == false`, calling
  `CloseAndCleanup()` (or the extracted liveness guard) sets `IsOpen = false` without throwing.
- `T-CE08c`: when `sessionGetter` returns `null`, same as T-CE08b.
- `T-CE08d`: verify that `EditRebuildState.RebuildRequired` → `RebuildDocument()` is called
  before any other action. Use a mock `IEditSession` that tracks call order.
- `T-CE08e`: after calling `CloseAndCleanup()` directly, `_session.Dispose()` was called once
  and `IsOpen == false`.
- `T-CE08f`: when a mock `IEditSession.Commit()` throws `EditValidationException`, the window
  stays open and `_errorMessage` is set to the error message.
- `T-CE08g`: when `_sessionGetter()` returns `null` at the moment the OK logic executes after
  commit returns, `SetComponent` is NOT called and no exception is thrown.

For T-CE08d, T-CE08f, T-CE08g: create a `FakeEditSession : IEditSession` test double that
lets you control `RebuildState` and throw from `Commit()`.

---

## Testing Requirements

- **Minimum new tests:** 13 (6 CE07 + 7 CE08)
- All pre-existing tests must continue to pass (except the known pre-existing failure)
- Tests must verify real behavior: not just "no exception" (except where noted above)
- Use `FakeEditSession` / `NopPickerContext` / mock `IInspectableSession` test doubles
- If a test truly cannot be written without a full ImGui context, document why in a
  `// T-CE07x: [SKIPPED — requires ImGui context]` comment and compensate with a structural assertion

---

## Quality Standards

- `ComponentEditDrawer` must be `internal sealed`
- `ComponentEditWindow` must be `internal sealed`
- `CloseAndCleanup` must be private
- No magic numbers except the `180f` column width (matches `ImGuiPropertyTree.NameColWidth`)
- The drawer must not access `_session` directly for ImGui calls — it only reads `node.Binding`
- Follow the established code style in `Fdp.Presentation`

---

## Report Requirements

Submit `.dev/comp-edit-1/reports/BATCH-03-REPORT.md` covering:

**Q1:** Any issues encountered? How resolved?

**Q2:** Was the test approach for CE08 (no-ImGui-context) workable? Any design suggestions?

**Q3:** Any deviations from the spec? Justify each.

**Q4:** Suggested commit message.

---

## Success Criteria

This batch is DONE when:

- [ ] `ComponentEditDrawer.cs` created, all CE07 tests pass
- [ ] `ComponentEditWindow.cs` created, all CE08 tests pass
- [ ] All pre-existing tests still pass (minus the known failure)
- [ ] `dotnet build FDP/FDP.sln --no-restore` exits with 0 errors
- [ ] `dotnet test IOS-IG-SimHost.sln` exits with 0 new failures
- [ ] Report submitted

---

## Reference

- **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §§ CE07, CE08
- **Design:** `.dev/comp-edit-1/DESIGN.md` §§ 3.2, 3.3
- **Editing folder (already exists):** `FDP/Engine/Fdp.Presentation/ImGui/Editing/`
- **Test folder (already exists):** `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/`
- **ManagedWindow base:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs`
- **StructEdit session APIs:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/`
