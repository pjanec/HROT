# BATCH-10 Instructions

**Topic:** `ai-hsm-btree-vis-edit`
**Estimated effort:** 16–22 hours
**Predecessor:** BATCH-09 (APPROVED — commit `8950b5b1` + `dbd3c8ff`)

---

## Onboarding

Read before writing any code:

1. **Design:** `.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` — §8 (Approach B field sync), §9.6 (BlackboardAccess annotations), §13 (failure modes A/B/C/D), §14 (infrastructure additions)
2. **Task detail:** `.dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — tasks 1f-07, 1e-01, 1e-02 and DEBT-06
3. **BATCH-09 review:** `.dev/ai-hsm-btree-vis-edit/reviews/BATCH-09-REVIEW.md`

Key invariants (AGENTS.md):
- `TreatWarningsAsErrors=true` in every project; xUnit analyzer rules are build errors (use `Assert.Empty`, `Assert.Single`, etc.)
- Preserve all existing comments exactly. Do not reflow or clean up unrelated code.
- Minimize diffs — only touch lines required for the functional change.
- No Unicode characters in comments or string literals.

### Previous batch summary (BATCH-09)

Delivered: `BTreeOrchestratorEmitter`, `HsmOrchestratorEmitter`, `HsmAsset.BlackboardTypeName`, `IBlackboardManagedAsset.RemoveVariables` + implementations on both concrete assets, `BlackboardDiagnosticCode`, unused-variable glyph + dimming in `BlackboardAuthoringWindow`, "Remove unused" toolbar action with confirmation modal.

Test baseline:
- `Hrot.BTree.Editor.Tests`: 221 passed
- `Hrot.Hsm.Editor.Tests`: 215 passed
- `Hrot.Editor.AiShared.Tests`: 365 passed
- Build: 0 errors, 0 warnings

Open debt coming into this batch:
- **DEBT-06 (P2):** Cross-asset alias bindings not cascade-invalidated when a variable is removed
- **DEBT-03 (P3):** Test stub redundancy (not in this batch — carry forward)
- **DEBT-04 (P3):** ImGui table column forward-only concern (not in this batch — carry forward)
- **DEBT-07 (P3):** `Unsafe.As<T,T>` layout risk (not in this batch — carry forward)

---

## Tasks

### Corrective (P2 Debt)

#### DEBT-06 — Stale alias binding surface

**Context:** When a variable is removed from a master asset A (`IBlackboardManagedAsset.RemoveVariables`), other assets B/C that hold `BlackboardAliasBinding` records pointing to A's requiring elements are not updated. Currently the bindings linger silently.

**Deliverable:** A lightweight validation surface — not automatic cascade removal (that requires catalog subscription, deferred) but a diagnostic visible in the Variables panel:

1. Add `void PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownAssetIds)` to `IBlackboardManagedAsset`. Signature rationale: the asset doesn't need the full catalog, just the set of currently-known asset IDs.
2. Implement in `BehaviorTreeAsset` and `HsmAsset`: for each binding list in `_aliases`, remove bindings whose `RequiringAssetId` is **not** in `knownAssetIds`. Call `MarkDirty()` once after all removals if any were removed; no-op otherwise.
3. In `BlackboardAuthoringWindow.DrawClientArea()`, obtain the known-asset IDs from the `IBlackboardManagedAsset.GetKnownSubAssetIds()` method (see step 4) and call `bbAsset.PruneStaleAliasBindings(...)` once per frame **before** building the view-model. (Yes, once per frame — the list is tiny; correctness > efficiency at this scale.)
4. Add `IReadOnlyCollection<Guid> GetKnownSubAssetIds()` to `IBlackboardManagedAsset`. Return the set of all `RequiringAssetId` GUIDs currently referenced in `_aliases`. This lets the window pass the "expected" IDs back as the known set. Since the window renders A's perspective (A is the master), it doesn't need a catalog — A provides what it knows. The prune is then effectively a self-consistency check: if A removes a variable and its aliases go, then the next draw will have no bindings left for that variable, so there is nothing to prune on A's side. The value is for when the catalog caller explicitly provides a smaller set.

**Alternate, simpler scope (preferred if the above feels circular):** Instead of the full interface method, just add in `BlackboardAuthoringWindow.DrawClientArea()` a pre-build step that calls a helper: iterate `bbAsset`'s alias entries and for each binding, surface a visible `!!` glyph and tooltip "Requiring asset not found in current session — binding may be stale." when `RequiringAssetId` is a GUID not returned by any loaded sub-asset reference in `bbAsset.AliasBindings`. For now the check is always true (no asset catalog integration yet) — defer the actual prune to when catalog access is wired. Add a `// TODO DEBT-06: prune when catalog available` comment.

**Simplest acceptable outcome:** A code comment `// DEBT-06: binding stale-check deferred to catalog wiring (see DEBT-TRACKER)` at the precise point in the code where cascade removal would happen, plus a method stub `PruneStaleAliasBindings` on both concrete assets that logs a debug message but does nothing. Mark DEBT-06 as PARTIALLY-ADDRESSED in DEBT-TRACKER.

Pick the simplest approach that makes the code honest about the gap.

**Tests:** One unit test per asset: `PruneStaleAliasBindings_RemovesBindings_ForUnknownAssets` — add two bindings from two different requiring-asset GUIDs, prune with a set containing only one of them, verify the other was removed.

---

### 1f-07 — Failure-state handling (States A/B/C/D)

**Spec:** BB §13.1–§13.4, §14.2 (BTree), §14.3 (HSM)

**Conceptual summary (read the spec carefully before coding):**

When the editor loads a Category 2 (editor-managed) file, four outcomes are possible:

| State | Cause | Panel behaviour | Save allowed? |
|-------|-------|-----------------|---------------|
| A | Clean | Fully functional | Yes |
| B | Span capture failed on one field | Read-only-passthrough for whole asset | Lossy save with warning |
| C | Struct parse failed entirely | Reflection-only display | No |
| D | Assembly did not compile; type not found | Show build error | No |

**Deliverables:**

1. **`BlackboardLoadState` enum** — add to `Hrot.Editor.AiShared/Blackboard/BlackboardLoadState.cs`:
   ```
   public enum BlackboardLoadState { Clean, SpanCaptureFailed, StructParseFailed, AssemblyFailed }
   ```
   (Names map A=Clean, B=SpanCaptureFailed, C=StructParseFailed, D=AssemblyFailed)

2. **`IBlackboardManagedAsset` additions** (minimal — only what the window needs):
   ```csharp
   BlackboardLoadState LoadState { get; }
   string? LoadDiagnosticMessage { get; }   // non-null for B/C/D
   ```

3. **`BehaviorTreeAsset` implementation:**
   - After the asset loads (parses source + reflects assembly), compute `LoadState`:
     - If the assembly type is `null` or couldn't be loaded → `AssemblyFailed`
     - Else if `BlackboardSourceTextParser.Parse(...)` returns `LocateResult.Found == false` → `StructParseFailed`
     - Else if any `FieldParseResult` in the parse result is missing a valid `VerbatimSpan` (i.e. the span offsets are invalid / span capture explicitly failed) → `SpanCaptureFailed`
     - Else → `Clean`
   - `LoadDiagnosticMessage`: null for `Clean`; a human-readable string for others (e.g. `"Struct declaration not found in source file."` for C, `"Build error: assembly load failed."` for D).

4. **`HsmAsset` implementation:** Same pattern.

5. **Save protection in `BlackboardDtoEmitter` (or wherever Save is triggered):**
   - If `LoadState == StructParseFailed` or `LoadState == AssemblyFailed` → throw `InvalidOperationException("Cannot save blackboard in load state {state}: {message}")`. Do not silently swallow.
   - If `LoadState == SpanCaptureFailed` → allowed but callers must opt-in with an explicit `allowLossySave: true` parameter. If `allowLossySave == false`, throw. The window will pass `true` only after showing the user a warning.

   Add `void Emit(IBlackboardManagedAsset asset, bool allowLossySave = false)` overload or augment the existing `Emit` method — whichever is cleaner without changing existing call sites.

6. **`BlackboardAuthoringWindow` rendering for non-Clean states:**
   - `AssemblyFailed`: replace the entire client area with `ImGui.TextColored(red, $"Cannot load blackboard. {asset.LoadDiagnosticMessage}")`.
   - `StructParseFailed`: show the reflected field names (from `IActionSchemaExporter` or reflection of `BlackboardTypeName`) read-only, plus a yellow warning: `"Source parse failed: {message}. Panel is read-only. Fix the source file to re-enable editing."`.
   - `SpanCaptureFailed`: same read-only display with a yellow warning; also show a `[ Save anyway (lossy) ]` button that calls emit with `allowLossySave: true` **after** an `ImGui.BeginPopupModal` confirm: `"Saving now will strip attributes and initializers from some fields. This cannot be undone. Continue?"`.

**Tests (`BlackboardLoadStateTests.cs` in `Hrot.Editor.AiShared.Tests/Blackboard/`):**

Use a stub `IBlackboardManagedAsset` implementation that exposes a settable `LoadState` and `LoadDiagnosticMessage`. The window tests should verify:
- T1: `Clean` → normal client-area render path (no error banner)
- T2: `AssemblyFailed` with a message → `TextColored` call with the message text (verify via `IMockImGui` or assert on a rendered string tracker)
- T3: `StructParseFailed` → read-only display present, edit controls absent
- T4: `SpanCaptureFailed` + reject lossy save → `InvalidOperationException` thrown from emit
- T5: `SpanCaptureFailed` + allow lossy save → emit succeeds (no throw)
- T6: Confirm that `BehaviorTreeAsset` sets `LoadState = Clean` when `BlackboardSourceTextParser.Parse` succeeds with all fields having valid spans
- T7: `BehaviorTreeAsset` sets `LoadState = StructParseFailed` when `LocateResult.Found == false`

Concrete `BehaviorTreeAsset` tests go in `Hrot.BTree.Editor.Tests/`. Concrete `HsmAsset` tests go in `Hrot.Hsm.Editor.Tests/`.

---

### 1e-01 — Inspector Parameter Synchronization sub-panel

**Spec:** BB §8.2, §11.6, §14.2 (BTree extension)

**Context:** When a Subtree node is selected in the BTree canvas, the Inspector should show a "PARAMETER SYNCHRONIZATION" section below the standard StructEdit fields. Each row in this section corresponds to one field of the sub-tree's DTO struct.

**Deliverables:**

1. **`SubtreeSyncBinding` record** — add to `Hrot.BTree.Editor/Model/SubtreeSyncBinding.cs`:
   ```csharp
   public sealed record SubtreeSyncBinding(
       string FieldName,
       string? MasterVariableName,  // null = (none)
       bool SyncIn,
       bool SyncOut);
   ```

2. **`BehaviorTreeAsset` model additions:**
   - `private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _syncBindings = new();` — keyed by node visual ID (only Subtree nodes have entries)
   - `public IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid nodeVisualId)` — returns empty list if no entry
   - `public void SetSyncBinding(Guid nodeVisualId, SubtreeSyncBinding binding)` — upserts the binding for the given field name (matching on `FieldName`), calls `MarkDirty()`
   - `public void ClearSyncBindings(Guid nodeVisualId)` — removes all bindings for the node, calls `MarkDirty()` if any existed
   - These are **not** persisted to the layout method yet (that is 1e-03's job); they are session-state for this batch.

3. **`InspectorWindow` extension:**
   - The current `DrawClientArea()` is a shell. Extend it to detect when `_store.ActiveSubSelection` is a `BTreeNodeSelection` **and** `_store.ActiveAsset` is a `BehaviorTreeAsset`.
   - Look up the node via `btreeAsset.FindNode(selection.VisualId)`.
   - If the node's `KernelType == NodeType.Subtree` and `node.Subtree != null`, render the **PARAMETER SYNCHRONIZATION** section:
     - Header: `ImGui.SeparatorText("PARAMETER SYNCHRONIZATION")`
     - If `node.Subtree.IsResolved == false` → `ImGui.TextDisabled("Subtree not resolved -- sync unavailable.")`; done.
     - If resolved: enumerate the sub-tree's DTO fields (see §4 below for how to get them).
     - For each field, render one row: field name (display only), then the "Bound to" dropdown (1e-02), then checkboxes Sync In / Sync Out.
   - If not a Subtree node, fall through to the existing rendering (no change).

4. **Getting sub-tree DTO fields:** The `InspectorWindow` needs to resolve the sub-tree's asset from a registry. For now, the window can receive an optional `Func<Guid, BehaviorTreeAsset?>` delegate in its constructor named `subAssetResolver`. This is the minimum coupling needed. In production the container wires this; in tests it is a simple lambda.

   When the delegate is null or returns null, show `ImGui.TextDisabled("Sub-tree asset not available in current session.")`.

   When the sub-asset is available, get its `_blackboardVariables` via `subAsset.BlackboardVariables` (use the existing `IReadOnlyList<BlackboardVariableEntry> BlackboardVariables` property). Each `BlackboardVariableEntry.Name` + `.TypeName` defines one row.

5. **Render each row** (column layout):
   - Column 0 (width 40%): field name, plain text
   - Column 1 (width 35%): Bound-to dropdown (1e-02 adds the dropdown body; for 1e-01 render `ImGui.TextDisabled("(none)")` as a placeholder)
   - Column 2 (width 12.5%): `☑↓` / `☐↓` Sync In checkbox (`##syncin_{nodeVisualId}_{fieldName}`)
   - Column 3 (width 12.5%): `☑↑` / `☐↑` Sync Out checkbox (`##syncout_{nodeVisualId}_{fieldName}`)
   - Checkbox state reads from `btreeAsset.GetSyncBindings(nodeVisualId)` for the matching field.
   - On checkbox change: call `btreeAsset.SetSyncBinding(nodeVisualId, updatedBinding)`.

**Tests (`BTreeSubtreeSyncPanelTests.cs` in `Hrot.BTree.Editor.Tests/Inspector/`):**

Use real `BehaviorTreeAsset` instances where possible:
- T1: No sub-asset resolver → `TextDisabled("Sub-tree asset not available...")` rendered
- T2: Resolved sub-asset with 3 variables → 3 rows rendered (check field names appear in output)
- T3: Unresolved subtree node → `TextDisabled("Subtree not resolved...")` rendered
- T4: Non-subtree node selected → Parameter Sync section absent from render
- T5: `SetSyncBinding` then `GetSyncBindings` → binding round-trips correctly
- T6: `ClearSyncBindings` → returns empty list afterward

Tests T5/T6 are pure model tests; T1–T4 require a minimal ImGui test renderer or a record-based mock. Follow the test patterns already established for `BlackboardAuthoringWindowTests.cs` in `AiShared.Tests`.

---

### 1e-02 — "Bound to" dropdown with type filtering

**Spec:** BB §8.2, §8.5

**Dependencies:** 1e-01 (this is the companion to 1e-01; implement together)

**Deliverables:**

1. **Type-filtered variable lookup** — add a helper in `BehaviorTreeAsset` (or in the window logic):
   ```csharp
   public IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName)
   ```
   Returns all `_blackboardVariables` entries whose `TypeName` equals `typeName` (exact match, case-sensitive). No coercion.

2. **Dropdown rendering** — replace the `ImGui.TextDisabled("(none)")` placeholder from 1e-01 with a real dropdown:
   - Collect candidates: `masterAsset.GetVariablesOfType(field.TypeName)` where `masterAsset` is the asset being inspected (the master BTree asset, `_store.ActiveAsset` cast to `BehaviorTreeAsset`).
   - Build the item list: `["(none)", ...candidate names]`.
   - Use `ImGui.BeginCombo("##bound_{nodeVisualId}_{fieldName}", currentSelection)` where `currentSelection` is the `MasterVariableName` from the existing binding (or `"(none)"` if null).
   - On selection change: call `btreeAsset.SetSyncBinding(nodeVisualId, binding with updated MasterVariableName)`.

3. **Empty state:** If `GetVariablesOfType(fieldType)` returns an empty list, show the combo with only `"(none)"` and a tooltip `"No master variables of type {typeName} exist. Add one in the Variables panel."`.

**Tests (`BTreeBoundToDropdownTests.cs` in `Hrot.BTree.Editor.Tests/Inspector/`):**

- T1: Master has two `int` vars and one `float` var; field type is `int` → dropdown shows only the two `int` vars
- T2: No master vars of matching type → dropdown shows only `(none)` with tooltip text
- T3: Selecting a variable from the dropdown calls `SetSyncBinding` with the correct `MasterVariableName`
- T4: Selecting `(none)` → `SetSyncBinding` called with `MasterVariableName = null`
- T5: Type matching is case-sensitive: `"Int32"` field does NOT match `"int"` master variable

---

## Mandatory Workflow: Test-Driven Task Progression

Follow this exactly for each task:

1. **Read** the relevant spec section and task detail fully before writing any implementation code.
2. **Write the test file first** (or at minimum the test signatures). Verify it compiles but all tests fail with `NotImplementedException` or `null ref`.
3. **Implement** the production code until tests pass.
4. **Run the test suite** for the affected project(s) before moving to the next task. Do not carry forward red tests.
5. **Check for xUnit analyzer violations** — `xUnit2013` (no `Assert.True(n > 0)`, use `Assert.NotEmpty`), `xUnit2002`/`xUnit2006` (no `Assert.True(false)`, use `Assert.Fail`). These are build errors.

---

## Developer Insights Required in Report

The report **must** answer all of these:

1. **Issues encountered:** What was harder than expected? Any spec ambiguities that required interpretation?
2. **Weak points spotted:** Any fragile patterns, missing validations, or code smells observed in the surrounding codebase (not introduced by this batch)?
3. **Design decisions made beyond the spec:** Any non-trivial choices made where the spec was silent?
4. **DEBT-06 approach chosen:** Which of the three DEBT-06 approaches did you take and why?
5. **Load-state detection specifics (1f-07):** How does `BehaviorTreeAsset` currently detect the source file load state? Is there an explicit `Load()` method or is it lazy? If lazy, where was the detection logic attached?

---

## Report Format

Write the completion report to: `.dev/ai-hsm-btree-vis-edit/reports/BATCH-10-REPORT.md`

Structure:

```
# BATCH-10 Report

## Summary
One paragraph.

## Tasks Completed
Table: Task ID | Deliverable | Tests Written | Tests Passing

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions

## DEBT-06 Approach Taken
Explain which approach and why.

## 1f-07 Load-State Detection Mechanism
Explain the attachment point.

## Test Counts
Project | Before | After
```

---

## Success Criteria

Before declaring the batch done:

- [ ] `dotnet build IOS-IG-SimHost.sln` exits 0 with 0 errors and 0 warnings
- [ ] `Hrot.BTree.Editor.Tests`: all tests pass, count >= 221 (new tests added for 1e-01, 1e-02)
- [ ] `Hrot.Hsm.Editor.Tests`: all tests pass, count >= 215 (new tests added for 1f-07 HSM side)
- [ ] `Hrot.Editor.AiShared.Tests`: all tests pass, count >= 365 (new tests for 1f-07 window states)
- [ ] DEBT-06 is addressed at the level you chose; `DEBT-TRACKER.md` updated to reflect status
- [ ] No xUnit2013 / xUnit2002 / xUnit2006 violations
- [ ] `IBlackboardManagedAsset` additions are minimal and backward-compatible (all 5 existing stubs still compile without changes)

---

## File Checklist (expected new/modified files)

### New files
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardLoadState.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/SubtreeSyncBinding.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Inspector/BTreeSubtreeSyncPanelTests.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Inspector/BTreeBoundToDropdownTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardLoadStateTests.cs`

### Modified files
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — add `LoadState`, `LoadDiagnosticMessage`, `PruneStaleAliasBindings`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — `LoadState` impl, sync binding model, `GetVariablesOfType`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — `LoadState` impl
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` — extend `DrawClientArea`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` — add `allowLossySave` guard
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAuthoringWindow.cs` — state-aware banners + `PruneStaleAliasBindings` call
- `DEBT-TRACKER.md` — update DEBT-06 status

### Do NOT modify
- Any existing test file beyond adding new test methods (do not rename or restructure existing tests)
- `BTreeFacets.cs` — facet struct extension is NOT in scope for this batch
- Any file outside the `Hrot/` tree
