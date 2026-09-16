# BATCH-04: Asset Wiring, Window Shell, Picker Filtering

**Batch Number:** BATCH-04
**Tasks:** TASK-BB-1b-05, TASK-BB-1a-03, TASK-BB-1a-06
**Phase:** Phase 1.5b (wiring) + Phase 1.5a (remaining UI)
**Estimated Effort:** 12-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (all green)

---

## Onboarding & Workflow

### Developer Instructions

This batch delivers:
1. **Asset wiring (1b-05)** — add `IsBlackboardEditorManaged` flag to `BehaviorTreeAsset` and `HsmAsset`; add `SubElementKind.BlackboardVariable` to the reference catalog; track `ExpressionTargetField` references per variable
2. **Window shell (1a-03)** — `BlackboardAuthoringWindow` docked window, read-only Defined Variables list from reflected fields, header with layout kind and memory budget
3. **Picker filtering (1a-06)** — type-filter the `BlackboardFieldPickerAttribute` dropdown to variables matching the action's schema `DtoType`; canvas variable binding badge on action/condition/guard nodes

Do tasks in the order listed (1b-05 first; 1a-03 second; 1a-06 third). Each task may only build on prior tasks within this batch.

### Required Reading (IN ORDER)

1. **Workflow:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/_DONE/ai-hsm-btree-vis-edit/ONBOARDING.md`
3. **Task Details:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — TASK-BB-1b-05, TASK-BB-1a-03, TASK-BB-1a-06
4. **Design:**
   - §4.1 (window registration and layout)
   - §4.2 (variable glyph semantics)
   - §4.6 (layout-kind indicator)
   - §4.7 (memory budget indicator)
   - §11.2, §11.4, §11.5 (picker + canvas badge)
   - §11.6 (subtree nodes: no field badge)
5. **Existing code to read before coding:**
   - `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs`
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` — ManagedWindow pattern
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs` — window registration
   - `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs` — active asset access
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — asset model
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BlackboardFieldPickerAttribute.cs` — existing picker
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/ObserverGuardBadgeRenderer.cs` — canvas badge pattern
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardFieldClassifier.cs` — from Batch 02
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` — from Batch 02
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs` — from Batch 03

### Source Code Locations

- **1b-05:** `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs` (add enum value); `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` (add flag + variable list); HSM equivalent; reference catalog
- **1a-03:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAuthoringWindow.cs` (NEW); `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs` (register it)
- **1a-06:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BlackboardFieldPickerAttribute.cs` (type-filter logic); canvas badge renderer (NEW in `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/`)

### Report Submission

**When done, submit your report to:**
`.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-04-REPORT.md`

**If you have questions, create:**
`.dev/_DONE/ai-hsm-btree-vis-edit/questions/BATCH-04-QUESTIONS.md`

---

## Tasks

### TASK-BB-1b-05 — `BlackboardManaged` asset wiring + `BlackboardVariable` reference kind

**Spec:** TASK-DETAIL.md §TASK-BB-1b-05; design BB §3.1, §11.4, §14.1–14.3.

#### a) Add `SubElementKind.BlackboardVariable`

In `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs`, add:
```csharp
BlackboardVariable,
```
Do **not** remove or rename `BlackboardField` — it is already used by existing code.

Key: a `BlackboardVariable` reference is keyed as `"{AssetId:D}::{VariableName}"` (double colon delimiter), distinguishing it from asset-level references.

#### b) Add `IsBlackboardEditorManaged` flag to `BehaviorTreeAsset`

In `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`:
- Add `public bool IsBlackboardEditorManaged { get; set; }` property (default `false`)
- Add `public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _blackboardVariables;` backed by `private readonly List<BlackboardVariableEntry> _blackboardVariables = new();`
- Add `public void SetBlackboardVariables(IEnumerable<BlackboardVariableEntry> vars)` to replace the list (fires `Changed`)
- `BlackboardVariableEntry` is a record defined in the same file or a companion: `record BlackboardVariableEntry(string Name, Type FieldType, string? Comment)`

Define `BlackboardVariableEntry` in the shared project to avoid the BTree editor depending on it from AiShared. Either define it in `Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs` (shared, then reference from BTree) or define it directly in `BehaviorTreeAsset.cs` as a file-local type.

The simpler approach: define `BlackboardVariableEntry` in `Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs` as a public record — it belongs there since it is used by the window and the emitter.

#### c) Add the same to `HsmAsset` (find it in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/`)

Find the HSM asset model file and add the same `IsBlackboardEditorManaged` and `BlackboardVariables` surface.

#### d) Reference catalog tracking

When an action node's `ExpressionTargetField` is non-null and the asset has `IsBlackboardEditorManaged = true`, register the reference as `(SubElementKind.BlackboardVariable, "{AssetId}::{fieldName}")`. This is needed for TASK-BB-1b-03 (rename). The exact place to do this is wherever existing `ExpressionTargetField` references are registered (search for `BlackboardField` usage in the catalog/refactor service and add a parallel `BlackboardVariable` registration).

**Tests** (`BlackboardVariableWiringTests.cs` in `Hrot.BTree.Editor` tests or `Hrot.Editor.AiShared.Tests`):
- `BehaviorTreeAsset` with `IsBlackboardEditorManaged = true` + `SetBlackboardVariables([...])` → `BlackboardVariables` list correct
- `SetBlackboardVariables` fires `Changed` event
- `SubElementKind` has `BlackboardVariable` value
- `BlackboardVariableEntry` is a record with `Name`, `FieldType`, `Comment`

---

### TASK-BB-1a-03 — `BlackboardAuthoringWindow` shell (read-only mode)

**Spec:** TASK-DETAIL.md §TASK-BB-1a-03; design BB §4.1, §4.2, §4.6, §4.7.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAuthoringWindow.cs`

This window extends `ManagedWindow` and depends on `EditorSelectionStore` and `IActionSchemaExporter`.

**Constructor:**
```csharp
public BlackboardAuthoringWindow(
    EditorSelectionStore store,
    IActionSchemaExporter schemaExporter)
    : base("ai_blackboard_variables", "Blackboard Variables", "Authoring", WindowScope.PerspectiveBound)
```

**`DrawClientArea()` behavior:**

When `_store.ActiveAsset` is null:
```
ImGui.TextDisabled("Select an asset to begin.");
```

When `_store.ActiveAsset` is non-null but has no blackboard type (check how to get `BlackboardVariables`; if not `IsBlackboardEditorManaged` and there are no reflected fields, show an info message):
```
ImGui.TextDisabled("This asset has no blackboard.");
```

When the asset has blackboard variables:

**Header section** (§4.1, §4.6, §4.7):
- Show `LayoutKind: Sequential` (always Sequential for editor-managed; "Unknown" for hand-written assets)
- Show `Memory: X / 100 B` where X is computed via `BlackboardBinPacker.Pack(variables).TotalInlineBytes`

**Defined Variables section** (§4.1, §4.2):
- Section header: `"DEFINED VARIABLES"`
- For each variable, one row: `{glyph} {Name}  {TypeName}  ({ByteSize} B)`
  - Glyph: `◆` (editor-managed, referenced by at least one node) — for the shell, use `◆` for all editor-managed fields. Use `🔒` for read-only-passthrough fields. Hollow diamond `○` is for unreferenced fields (defer to TASK-BB-1f-03).
  - In the shell (read-only mode), no add/remove buttons yet; these come in TASK-BB-1b-02
  - Show sub-row comment if the variable has a non-null `Comment`: `"     -> {Comment}"` (dimmed)

**Internal view-model building:**

The window needs to derive a display list from the active asset. For this shell:
1. If the active asset has `BlackboardVariables` (i.e., it's `BehaviorTreeAsset` or `HsmAsset` with `IsBlackboardEditorManaged`), use `asset.BlackboardVariables` directly.
2. If the asset has no editor-managed variables, check if there's a reflected blackboard type. This requires the window to know the asset's blackboard struct type. For now, if there are no variables in the list and the asset isn't blackboard-managed, show "no blackboard".

**Important:** The window is read-only in this task. The `[+] Add variable...` button will be added in TASK-BB-1b-02. For now, render just the variable list.

**Registration:** In `SharedAiWindowRegistrar`:
- Add `BlackboardAuthoringWindow` as a constructor parameter
- Call `windowManager.RegisterWindow(_blackboardAuthoring)` in `RegisterWindows`

**Tests** (`BlackboardAuthoringWindowTests.cs`):
- When `ActiveAsset = null` → view-model is empty
- When `ActiveAsset` is a `BehaviorTreeAsset` with `IsBlackboardEditorManaged = true` and 3 variables → view-model has 3 entries in declaration order
- When `ActiveAsset` is a `BehaviorTreeAsset` with `IsBlackboardEditorManaged = false` → view-model shows the "no blackboard" state
- Memory budget: an asset with `int + bool` (5 bytes) → `TotalInlineBytes = 5` in the header
- Variable with non-null `Comment` → comment sub-row visible in view-model

Note: Do NOT try to test actual ImGui rendering (that requires a GPU/context). Test the view-model projection logic only. Extract view-model building to a testable static method or inner class.

---

### TASK-BB-1a-06 — Picker filtering by action `DtoType`

**Spec:** TASK-DETAIL.md §TASK-BB-1a-06; design BB §11.2, §11.5, §11.6, §10.5.

This task has two parts:
1. **Type-filter the dropdown** in `BlackboardFieldPickerAttribute` context
2. **Canvas badge** renderer showing bound variable name

#### Part 1: Type-filter the picker dropdown

Currently `BlackboardFieldPickerAttribute` is a marker attribute with no filtering logic.

The filtering context: when a StructEdit inspector renders a field decorated `[BlackboardFieldPickerAttribute]`, it shows a dropdown of available blackboard variable names. The filtering must restrict the dropdown to variables whose `FieldType` matches the containing action method's `DtoType` from `IActionSchemaExporter`.

Since this project uses StructEdit for inspector panels, and the actual dropdown rendering depends on how StructEdit invokes the attribute's logic, the approach here is:

**A. Extend `BlackboardFieldPickerAttribute`** with a helper that, given:
- The action's FQN (from the inspector context)
- The available blackboard variables (from the active asset)
- The `IActionSchemaExporter`

...returns the filtered list of variable names to offer in the dropdown.

```csharp
public static IReadOnlyList<string> GetCompatibleVariables(
    string actionFqn,
    IReadOnlyList<BlackboardVariableEntry> availableVariables,
    IActionSchemaExporter exporter);
```

- Looks up `exporter.Lookup(actionFqn)` to get `ActionSchemaEntry.DtoType`
- Filters `availableVariables` to those where `v.FieldType == dtoType`
- Returns the filtered name list (empty = no compatible variables)

**B. Add `(no compatible variables)` display** — when `GetCompatibleVariables` returns empty and the current value is null, the picker should show `"(no compatible variables)"`. This is a string constant, not an actual variable name. Implement as `public const string NoCompatibleVariablesDisplay = "(no compatible variables)"` on `BlackboardFieldPickerAttribute`.

**Tests** (`BlackboardFieldPickerTests.cs` in `Hrot.BTree.Editor` tests):
- Action with `DtoType = MoveToLocationParams` and variables of types `[MoveToLocationParams, int, float]` → filtered list contains only `MoveToLocationParams` variables
- Action with no matching variables → result is empty
- Action FQN not in schema (`Lookup` returns null) → result is all variables (no filtering — safe default)
- `actionFqn = null` → returns all variables (safe default)

#### Part 2: Canvas variable binding badge renderer

**New file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/VariableBindingBadgeRenderer.cs`

Pattern: follow `ObserverGuardBadgeRenderer.cs` exactly. This renderer:

- Implements `ICustomCanvasRenderer`
- `Id = "btree.variable_binding_badges"`
- `Pass = CanvasRenderPass.AfterNodes` (on top of nodes)
- In `Render(ICanvasRenderContext ctx)`:
  - Skip if `ctx.IsLowZoom`
  - For each visible node that is an Action or Condition (NOT Subtree — see design BB §11.6):
    - Get the node's `ExpressionTargetField` from the `BTreeEditorNode`'s payload
    - If non-null: draw a small badge under the node showing `"{variableName}"`
    - If null: draw a small badge in red showing `"(unbound)"`
  - Subtree nodes and composite nodes: no badge

**Badge visual:**
- Bound: background `new Vector4(0.2f, 0.5f, 0.2f, 0.75f)` (green-ish), white text
- Unbound: background `new Vector4(0.6f, 0.15f, 0.15f, 0.75f)` (red), white text
- Position: bottom-center of the node's bounding box in screen space

The badge needs the `BTreeCanvasHost` or equivalent to get the editor nodes. Inject `BehaviorTreeAsset` or `EditorSelectionStore` as a dependency.

**Tests** (`VariableBindingBadgeRendererTests.cs`):
- Node with `Action.ExpressionTargetField = "myVar"` → `LastRenderBadgeCount == 1`
- Node with `Action.ExpressionTargetField = null` → `LastRenderBadgeCount == 1` (unbound badge)
- Subtree node → `LastRenderBadgeCount == 0` (no badge)
- Condition node with binding → `LastRenderBadgeCount == 1`
- `ctx.IsLowZoom = true` → `LastRenderBadgeCount == 0` (skipped)

---

## Mandatory Workflow

For each task in order:
1. Read the spec in TASK-DETAIL.md and the referenced design sections
2. Explore all referenced existing code before writing anything new
3. Write tests first (failing)
4. Implement to make tests pass
5. Run: `dotnet build IOS-IG-SimHost.sln -v minimal` + `dotnet test` on the affected test project
6. Move to next task only when current task's tests pass

---

## Testing Requirements

- `BlackboardVariableWiringTests.cs` — TASK-BB-1b-05
- `BlackboardAuthoringWindowTests.cs` — TASK-BB-1a-03 (view-model only, no ImGui)
- `BlackboardFieldPickerTests.cs` — TASK-BB-1a-06 Part 1
- `VariableBindingBadgeRendererTests.cs` — TASK-BB-1a-06 Part 2
- Minimum 25 new tests total across the new files
- Tests must exercise the real logic, not just "non-null" assertions

---

## Notes

- The `BlackboardAuthoringWindow` tests must NOT depend on ImGui. Extract the view-model building to a testable internal method.
- `BlackboardVariableEntry` must be defined in the shared project (`Hrot.Editor.AiShared`) so the window can use it without a dependency on `Hrot.BTree.Editor`
- For the canvas badge renderer: `ctx.VisibleNodes` gives the visible node IDs. Use `ctx.Graph.FindNode(id)` to get the node. Check `node.Kind.Id` against `BTreeKinds.Action` and `BTreeKinds.Condition`.
- For the canvas badge position: `var screenPos = ctx.Viewport.GraphToScreen(node.Position + new Vector2(node.Width / 2, node.Height))` — the node's bottom-center. Check the ObserverGuardBadgeRenderer for the exact `DrawBadge` helper pattern (size, `ImGui.DrawList.AddRectFilled` + `ImGui.DrawList.AddText`).
- `BehaviorTreeAsset` is in `Hrot.BTree.Editor` which already references `Hrot.Editor.AiShared`, so adding `BlackboardVariableEntry` to `AiShared` is safe.

---

## Report Requirements

Submit `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-04-REPORT.md`:

```markdown
# BATCH-04 Report

## Tasks Completed
- [ ] TASK-BB-1b-05 (asset wiring + SubElementKind.BlackboardVariable)
- [ ] TASK-BB-1a-03 (BlackboardAuthoringWindow shell)
- [ ] TASK-BB-1a-06 (picker filtering + canvas badge)

## Test Results
[dotnet test summary]

## Files Changed / Created

## Developer Insights

**Q1:** How did you handle the view-model extraction for the window tests?
**Q2:** Any issues with the canvas badge renderer?
**Q3:** What is the HSM asset model file name and structure?
**Q4:** Anything unexpected?
**Q5:** Suggested git commit message?
```

---

## Success Criteria

- [ ] `SubElementKind.BlackboardVariable` exists alongside (not replacing) `BlackboardField`
- [ ] `BehaviorTreeAsset.IsBlackboardEditorManaged` + `BlackboardVariables` + `SetBlackboardVariables` working
- [ ] Same on `HsmAsset`
- [ ] `BlackboardVariableEntry` record in `Hrot.Editor.AiShared`
- [ ] `BlackboardAuthoringWindow` registered in `SharedAiWindowRegistrar`, window renders variable list with correct glyphs and memory budget header
- [ ] `BlackboardFieldPickerAttribute.GetCompatibleVariables` filters by action DtoType
- [ ] `VariableBindingBadgeRenderer` draws bound/unbound badges on Action/Condition nodes only
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds
- [ ] All tests pass
- [ ] Report submitted
