# BATCH-09 INSTRUCTIONS

**Assignee:** Coder Sub-agent (Developer role)
**Skill reference:** `.github/skills/developer/SKILL.md`
**Batch size estimate:** 14–20 hours
**Topic path:** `.dev/_DONE/ai-hsm-btree-vis-edit/`

---

## Onboarding

Read these documents before writing a single line:

- `docs/AI_DEV_GUIDE.md` — project conventions, test standards, emit discipline
- `.dev/_DONE/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` — the spec for everything in this batch
- `.dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-08-REVIEW.md` — what was reviewed and approved previously
- `.dev/_DONE/ai-hsm-btree-vis-edit/DEBT-TRACKER.md` — known tech debt; do not make things worse

**Previous batch context (BATCH-08, approved):**

- `BlackboardAliasBinding` record exists in `Hrot.Editor.AiShared/Blackboard/BlackboardAliasBinding.cs`
- `IBlackboardManagedAsset` extended with `GetAliasesFor`, `AddAlias`, `RemoveAlias`
- `BehaviorTreeAsset` and `HsmAsset` implement alias methods with cascade rename/remove
- `BlackboardAuthoringWindow` renders the alias UX (drop target, badge, remove menu)
- 773 tests pass. Build is clean.

---

## Tasks

### TASK-BB-1d-03 — BTree Orchestrator Emit

**Goal:** When a `BehaviorTreeAsset` has aliased sub-trees, emit a companion
`{AssetName}.Orchestrators.g.cs` file containing one `[BTreeAction]` static method per
aliased sub-tree. The method projects the master's variable slice via `Unsafe.As` (no copy)
and calls `{SubTreeName}.GetInterpreter().Tick(...)`.

**Spec:** BB §7.5, §4a.2.

**Where to create:**
- New file: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeOrchestratorEmitter.cs`
- Tests: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeOrchestratorEmitterTests.cs`

**Orchestrator file shape (from BB §7.5):**

```csharp
// HROT_EDITOR_GENERATED — managed by the AI editor; manual edits will be overwritten on next save.
// OwningAssetId: <asset.AssetId:D>
// OwningAssetName: <asset.Name>

using System.Runtime.CompilerServices;
using Hrot.FastBTree.Kernel;
<other usings>

namespace <asset.TargetNamespace>;

public static class <AssetClassName>_Orchestrators
{
    [BTreeAction(Name = "Orchestrate_<SubTreeName>")]
    public static NodeStatus Orchestrate_<SubTreeName>_Tick(
        ref <BlackboardTypeName> master,
        ref BehaviorTreeState state,
        ref BTreeContext ctx,
        int paramIndex)
    {
        ref var subBb = ref Unsafe.As<<DtoTypeName>, <DtoTypeName>>(ref master.<VariableName>);
        return <SubTreeName>.GetInterpreter().Tick(ref subBb, ref state, ref ctx);
    }

    // ... one method per unique (variableName, subTreeName) alias pair
}
```

**Implementation notes:**

1. Create `BTreeOrchestratorEmitter` as a `public static class` with a single static method
   `public static string? Emit(BehaviorTreeAsset asset)`. Return `null` when there are no
   aliases (caller skips write). **Do not** implement `IFluentCSharpEmitter<T>` — the
   orchestrators file has different nullable semantics.

2. Gather all aliases by iterating `asset.BlackboardVariables` and calling
   `asset.GetAliasesFor(v.Name)` for each variable. Skip variables with no aliases.

3. Per alias binding (`BlackboardAliasBinding`):
   - `binding.RequiringAssetName` → the `SubTreeName` identifier (sanitize with the same
     `SanitizeIdentifier` helper used by `BTreeFluentEmitter`).
   - `binding.DtoType.Name` → the DTO type simple name.
   - The variable's `v.Name` → `VariableName` in the `ref master.<VariableName>` expression.

4. `BlackboardTypeName` comes from `asset.BlackboardTypeName`. `ContextTypeName` from
   `asset.ContextTypeName`. Both are already on the model.

5. Use the four-line header block:
   ```
   // HROT_EDITOR_GENERATED — managed by the AI editor; manual edits will be overwritten on next save.
   // Auto-generated orchestrator actions for aliased sub-trees.
   // OwningAssetId: {asset.AssetId:D}
   // OwningAssetName: {asset.Name}
   ```
   Use `FluentCSharpEmitterBase.BuildHeader(asset.AssetId)` for line 1 (the standard marker
   line), then append lines 2–4 manually.

6. Collect usings deterministically:
   - Always: `System.Runtime.CompilerServices` (for `Unsafe`), `Hrot.FastBTree.Kernel`
     (for `BTreeAction`, `NodeStatus`, `BehaviorTreeState`, `BTreeContext`)
   - Per DTO type: if the type's namespace differs from the asset's `TargetNamespace`, include
     it. Use `FluentCSharpEmitterBase.SortUsings(namespaces)` for deterministic ordering.

7. If two alias bindings use the same `RequiringAssetName` for the same variable (two separate
   element bindings from the same sub-tree), emit only one orchestrator method for that
   `(variableName, subTreeName)` pair. Use a `HashSet<(string, string)>` to deduplicate.

8. The emitter is **stateless** and **pure** (no file I/O). File write is the caller's concern.
   A helper `WriteOrchestratorFile(BehaviorTreeAsset asset, string? sidecarContent)` may live
   in the same file or in the existing `FluentCSharpEmitterBase` as a static utility. It uses
   `FluentCSharpEmitterBase.WriteAtomic(path, content)` where
   `path = Path.ChangeExtension(asset.SourceFilePath, null) + ".Orchestrators.g.cs"`.
   When `sidecarContent` is null, do nothing (do not delete the existing file; deletion is
   a separate concern not covered in this batch).

**Tests to write** (`BTreeOrchestratorEmitterTests.cs`):

- `Emit_ReturnsNull_WhenNoAliases` — asset with no aliases → `null`
- `Emit_ContainsOrchestratorMethod_ForAlias` — asset with one alias on variable `SharedFire`
  pointing to sub-tree `Shoot_BT` → emitted string contains
  `[BTreeAction(Name = "Orchestrate_Shoot_BT")]` and
  `Orchestrate_Shoot_BT_Tick` and `ref master.SharedFire`
- `Emit_Deduplicates_SameSubTreeTwoBindings` — two alias bindings for same sub-tree/variable
  → only one method emitted
- `Emit_ContainsTwoMethods_ForTwoDistinctSubTrees` — two different aliasing sub-trees on the
  same variable → two distinct methods in the output
- `Emit_OutputIsDeterministic` — same asset, two calls → identical strings
- `Emit_StartsWithEditorGeneratedMarker` — output starts with
  `FluentCSharpEmitterBase.EditorGeneratedMarker`

For tests, build a minimal `BehaviorTreeAsset` in memory (use the existing constructor), add
alias bindings via `AddAlias`, and assert on the string output. No disk I/O in unit tests.

---

### TASK-BB-1d-04 — HSM Orchestrator Emit

**Goal:** Same projection pattern for `HsmAsset` — emit `{AssetName}.Orchestrators.g.cs`
when the HSM master has aliased sub-BTrees. The HSM action attribute is `[HsmAction]`; the
tick API differs slightly.

**Spec:** BB §7.5, §14.3.
**Dependencies:** TASK-BB-1d-03 (same structural pattern).

**Where to create:**
- New file: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmOrchestratorEmitter.cs`
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmOrchestratorEmitterTests.cs`

**HSM-specific differences from BTree:**

1. Attribute: `[HsmAction(Name = "Orchestrate_<SubTreeName>")]`.
   Using: `Fhsm.Kernel.Attributes` (provides `HsmAction`).

2. `HsmAsset` does not have a `BlackboardTypeName` property (unlike `BehaviorTreeAsset`).
   Add a `BlackboardTypeName` property to `HsmAsset`:
   ```csharp
   public string BlackboardTypeName { get; set; }
   ```
   Default value: `$"{SanitizeIdentifier(Name)}_Blackboard"` (computed lazily in the
   constructor, same naming convention as BTree). Initialize in the constructor where the
   other string properties are set. The property is `string` not `string?` — it always has a
   value.

3. The tick call for a sub-BTree inside HSM uses `BehaviorTreeState` and `BTreeContext` (from
   `Hrot.FastBTree.Kernel`), same as BTree — the sub-tree is always a BTree even when the
   master is an HSM. The method signature mirrors §7.5:
   ```csharp
   [HsmAction(Name = "Orchestrate_<SubTreeName>")]
   public static NodeStatus Orchestrate_<SubTreeName>_Tick(
       ref <BlackboardTypeName> master,
       ref BehaviorTreeState state,
       ref BTreeContext ctx,
       int paramIndex)
   {
       ref var subBb = ref Unsafe.As<<DtoTypeName>, <DtoTypeName>>(ref master.<VariableName>);
       return <SubTreeName>.GetInterpreter().Tick(ref subBb, ref state, ref ctx);
   }
   ```

4. The class name suffix is `_Orchestrators` (same as BTree). The namespace comes from
   `asset.TargetNamespace`.

5. All other rules from TASK-BB-1d-03 (deduplication, null return, determinism, write helper)
   apply unchanged to the HSM emitter.

**Tests to write** (`HsmOrchestratorEmitterTests.cs`):

- `Emit_ReturnsNull_WhenNoAliases`
- `Emit_ContainsOrchestratorMethod_ForAlias` — attribute is `[HsmAction(...)]` not `[BTreeAction(...)]`
- `Emit_OutputIsDeterministic`
- `BlackboardTypeName_DefaultsToAssetNamePlusBlackboard` — `HsmAsset` with name `GuardPatrol_HSM`
  → `BlackboardTypeName` == `"GuardPatrol_HSM_Blackboard"`

---

### TASK-BB-1f-03 — Unused-Variable Diagnostic + Glyph

**Goal:** Reference-count each variable via the existing `CountNodesReferencingVariable` method.
Zero references → `○` hollow-diamond glyph, dimmed text, and an Info-level
`UnusedVariable` diagnostic. Also surface `VariableTypeNotFound` when a variable's
`FieldType` is `null` after a schema rebuild dropped the type.

**Spec:** BB §12.1–§12.3.

**Step 1 — Add `BlackboardDiagnosticCode` enum.**

New file: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDiagnosticCode.cs`

```csharp
namespace Hrot.Editor.AiShared.Blackboard;

// Diagnostic codes for blackboard authoring validators.
// See Blackboard_Authoring_Detailed_Design.md section 12.
public enum BlackboardDiagnosticCode
{
    // A variable has zero references from any node. Candidate for removal. (Info level)
    UnusedVariable,

    // A variable's FieldType could not be resolved after a schema rebuild.
    // The variable is preserved verbatim; authoring is suspended for this field. (Warning level)
    VariableTypeNotFound,
}
```

No other diagnostic codes yet — the rest (cross-region, inline memory exceeded, etc.) are in
future tasks. Do not add codes for tasks not in this batch.

**Step 2 — Add `IsUnused` to `VariableViewModel`.**

In `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`, the
`VariableViewModel` record currently looks like:

```csharp
public record VariableViewModel(
    string Name,
    string TypeDisplayName,
    int ByteSize,
    Type FieldType,
    string? Comment,
    IReadOnlyList<(string AssetName, Guid AssetId, Guid ElementId)> AliasedBy);
```

Add one more property:
```csharp
public record VariableViewModel(
    string Name,
    string TypeDisplayName,
    int ByteSize,
    Type FieldType,
    string? Comment,
    IReadOnlyList<(string AssetName, Guid AssetId, Guid ElementId)> AliasedBy,
    bool IsUnused);
```

**Step 3 — Populate `IsUnused` in `BuildViewModel`.**

In the `BuildViewModel` method where variable view models are built (the `foreach (var v in rawVars)` loop), compute:
```csharp
bool isUnused = bbAsset.CountNodesReferencingVariable(v.Name) == 0;
```

Pass `isUnused` as the new `IsUnused` argument when constructing `VariableViewModel`.

Note: `CountNodesReferencingVariable` already exists on `IBlackboardManagedAsset`.

**Step 4 — Render the `○` glyph in `DrawClientArea`.**

In `DrawClientArea`, the row-drawing loop currently renders a glyph before the variable name.
The existing glyph logic shows `◆` for normal variables (filled diamond). Add a branch:

```
if (row.IsUnused)
    // render "o " dimmed (hollow diamond — ASCII approximation; see note below)
else
    // render "* " or the current filled diamond
```

**Glyph implementation note:** ImGui doesn't have a hollow-diamond Unicode glyph in its
default font atlas. Use ASCII `o` followed by a space as the hollow-diamond stand-in.
Render the entire row (glyph + name + type + size) at 60% alpha when `IsUnused` is true,
using `ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.6f)` / `PopStyleVar()`.

Tooltip for unused rows (add to the existing hover tooltip logic):
`"Not referenced by any node -- consider removing."`

**Tests to write:**

File: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/UnusedVariableDiagnosticTests.cs`

- `BuildViewModel_SetsIsUnused_WhenZeroReferences` — stub asset returns `0` from
  `CountNodesReferencingVariable("x")`, variable `x` in asset → `VariableViewModel.IsUnused == true`
- `BuildViewModel_ClearsIsUnused_WhenOneReference` — stub returns `1` → `IsUnused == false`
- `BuildViewModel_MultipleVars_OnlyUnusedOnesMarked` — three vars; two with refs, one without →
  exactly one marked unused

Use an `internal sealed class` stub (not `file`-scoped, per the pattern established in
BATCH-08) implementing `IBlackboardManagedAsset` for all test stubs.

---

### TASK-BB-1f-04 — "Remove Unused" Toolbar Action

**Goal:** One-click batch removal of all zero-reference variables behind a confirmation modal.
Single batch command, single Changed event fire.

**Spec:** BB §12.4, §12.6.
**Dependencies:** TASK-BB-1f-03 (IsUnused flag in ViewModel).

**Implementation:**

1. In `DrawClientArea`, add a `[ Remove unused ]` button in the panel header — placed
   immediately before (or after) the `[+] Add variable` button — **only when at least one
   variable has `IsUnused == true`**.

2. When the button is clicked, show an `ImGui.OpenPopup("confirm_remove_unused")` popup
   (use a modal-popup pattern consistent with the existing "confirm remove" popup already
   in `BlackboardAuthoringWindow`).

3. The confirmation popup shows:
   ```
   Remove N unused variables?
   This will free X bytes from the blackboard.
   This cannot be undone.
   [ Remove ]   [ Cancel ]
   ```
   `N` = count of unused vars; `X` = sum of their `ByteSize`.

4. On confirm: collect all variable names where `IsUnused == true` from the current ViewModel.
   Remove them **all from the asset in a single batch** — call `bbAsset.RemoveVariable(name)`
   for each in order, but fire `Changed` **only once** after all removals. The safest way is to
   call `RemoveVariable` for each but suppress firing until done:
   - **If `BehaviorTreeAsset.RemoveVariable` (and `HsmAsset.RemoveVariable`) fire `Changed`
     internally, add an internal batch-remove method** to `IBlackboardManagedAsset`:
     ```csharp
     void RemoveVariables(IReadOnlyList<string> names);
     ```
     Default implementation: remove each name, fire `Changed` once at the end.
     Implement on both `BehaviorTreeAsset` and `HsmAsset`.
   - If the existing `RemoveVariable` does NOT auto-fire, call it in a loop and fire `Changed`
     manually once. Check the existing code before deciding.

5. After removal, the ViewModel rebuild triggers automatically via the `Changed` event
   (existing subscriber chain). No manual ViewModel rebuild needed.

**Tests to write:**

File: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/RemoveUnusedVariablesTests.cs`

- `RemoveVariables_RemovesAllNamedVariables` — asset has vars `a`, `b`, `c`; call
  `RemoveVariables(["a","c"])` → `BlackboardVariables` contains only `b`
- `RemoveVariables_FiresChangedExactlyOnce` — subscribe to `Changed`; call `RemoveVariables`
  on a list of 3 vars → `Changed` fires exactly 1 time
- `RemoveVariables_EmptyList_NoChange` — call with empty list → `Changed` fires 0 times,
  variable list unchanged
- `RemoveVariables_RemovesAliasKeys` — var `x` has alias bindings; remove `x` via
  `RemoveVariables` → `GetAliasesFor("x")` returns empty after removal

Test on both `BehaviorTreeAsset` and `HsmAsset` instances.

---

## Test-Driven Task Progression (MANDATORY)

For each task, follow this exact sequence:

1. **Write the test file first.** All tests fail (not compile — the types/methods don't exist
   yet). This is expected.
2. **Write the minimum implementation** to make the tests compile.
3. **Run the tests.** Iterate until green.
4. **Run the full suite** (`dotnet test`) from the repo root. Must still be 773+ tests, 0 failed.
5. **Do not proceed to the next task** until the current task's tests are green and the full
   suite passes.

---

## Developer Insights Required in Report

Answer all four questions in your report:

1. **What issues did you encounter?** Any compiler errors, design ambiguities, unexpected
   API gaps.
2. **What weak points did you spot in the codebase?** Note things that are fragile but not
   in scope to fix.
3. **What design decisions did you make beyond the spec?** Especially for anything not
   explicitly stated.
4. **What is still rough or needs follow-up?** List anything you did that is a temporary
   workaround.

---

## Report Format

Write your completion report to:
`.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-09-REPORT.md`

Structure:

```markdown
# BATCH-09 REPORT

## Task Status
| Task     | Status  | Tests Added | Notes |
|----------|---------|-------------|-------|
| 1d-03    | Done    | N           |       |
| 1d-04    | Done    | N           |       |
| 1f-03    | Done    | N           |       |
| 1f-04    | Done    | N           |       |

## Full test suite result
773+ passed, 0 failed (state count here)

## Developer Insights
### Issues encountered
### Weak points spotted
### Design decisions beyond the spec
### What is still rough
```

---

## Success Criteria

- [ ] `BTreeOrchestratorEmitter.Emit` returns `null` for no aliases; returns correct C# for
  aliases with correct method name, attribute, `ref master.<VariableName>`, and
  `Unsafe.As<...>`.
- [ ] `HsmOrchestratorEmitter.Emit` mirrors BTree with `[HsmAction]`.
- [ ] `HsmAsset.BlackboardTypeName` property exists and defaults correctly.
- [ ] `VariableViewModel.IsUnused` set correctly from `CountNodesReferencingVariable`.
- [ ] `BlackboardDiagnosticCode` enum exists with `UnusedVariable` and `VariableTypeNotFound`.
- [ ] "Remove unused" button appears only when unused vars exist; confirmation dialog shows
  count + freed bytes; `Changed` fires once; all named vars removed.
- [ ] `IBlackboardManagedAsset.RemoveVariables(IReadOnlyList<string>)` method exists and
  implemented on both `BehaviorTreeAsset` and `HsmAsset`.
- [ ] All new tests pass. No existing tests broken.
- [ ] `dotnet test` from repo root: 0 failures.
- [ ] `dotnet build` from repo root: 0 errors, 0 warnings introduced.
