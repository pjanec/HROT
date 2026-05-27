# BATCH-12: Corrective Tests (BATCH-10 gaps) + Cross-Region Blackboard Conflict Validator

**Batch Number:** BATCH-12
**Tasks:** Corrective-P2 (BATCH-10 gaps), TASK-BB-1f-01, TASK-BB-1f-02
**Phase:** 1.5f — Validation, diagnostics, recovery
**Estimated Effort:** 14–18 hours
**Priority:** HIGH
**Dependencies:** BATCH-10 (approved), BATCH-11 (approved)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/ai-hsm-btree-vis-edit/ONBOARDING.md`
3. **Design:** `.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md`
   - §9.1–§9.4 (cross-region conflict rules, diagnostic, override, Approach A refusal)
   - §9.5 (Approach B sync-out conflicts)
   - §9.6 (read-only access is safe, `[BlackboardReadOnly/ReadWrite]` hint)
4. **Task Detail:** `.dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md`
   - Tasks: `TASK-BB-1f-01`, `TASK-BB-1f-02`
5. **BATCH-10 Review:** `.dev/ai-hsm-btree-vis-edit/reviews/BATCH-10-REVIEW.md` — issues you must fix
6. **BATCH-11 Review:** `.dev/ai-hsm-btree-vis-edit/reviews/BATCH-11-REVIEW.md`

### Source Code Locations

| Project | Path |
|---------|------|
| Shared editor (AiShared) | `Hrot/Editor/Hrot.Editor.AiShared/` |
| AiShared tests | `Hrot/Editor/Hrot.Editor.AiShared.Tests/` |
| BTree editor | `Hrot/Subsystems/AI/Hrot.BTree.Editor/` |
| BTree editor tests | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/` |
| HSM editor | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` |
| HSM editor tests | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/` |

### Key existing files for this batch

- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs` — Rule 7 (`CheckOutputLaneConflicts`) is the direct parallel for your new Rule 8; follow its pattern exactly.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnosticCode.cs` — add `CrossRegionBlackboardConflict` here.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — has `PruneStaleAliasBindings` and `GetKnownSubAssetIds` added in BATCH-10.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — same additions.
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — the interface.
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — the drop-target logic for 1f-02.
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDiagnosticCode.cs` — `UnusedVariable`, `VariableTypeNotFound` already present.

### Build & test commands

```powershell
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build --logger "console;verbosity=minimal"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build --logger "console;verbosity=minimal"
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build --logger "console;verbosity=minimal"
```

### Baseline test counts (BATCH-11 approved)

| Project | Passing |
|---------|---------|
| `Hrot.BTree.Editor.Tests` | 265 |
| `Hrot.Editor.AiShared.Tests` | 372 |
| `Hrot.Hsm.Editor.Tests` | 215 |

### Report Submission

**When done:** `.dev/ai-hsm-btree-vis-edit/reports/BATCH-12-REPORT.md`
**Questions:** `.dev/ai-hsm-btree-vis-edit/questions/BATCH-12-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Corrective** → Implement tests → **ALL tests pass** ✅
2. **1f-01** → Implement + tests → **ALL tests pass** ✅
3. **1f-02** → Implement + tests → **ALL tests pass** ✅

**DO NOT** move to the next task until all existing + current-task tests pass.

**DO NOT** stop and ask for permission to run tests, fix failures, or handle obvious next steps. Do it all.

---

## Context

BATCH-10 approved with two missing-test gaps (P2):
- `PruneStaleAliasBindings` tests for both concrete assets
- Concrete LoadState computation tests for both assets

BATCH-11 (1e-01 through 1e-05) complete. Phase 1.5e done.

This batch begins Phase 1.5f's cross-region conflict work. `HsmValidator` already detects `OutputLaneConflict` (parallel regions writing the same command lane — Rule 7). You extend it with Rule 8: parallel regions writing the same blackboard variable.

---

## ✅ Tasks

---

### Task 0: Corrective — Missing tests from BATCH-10

**BATCH-10 review identified two P2 test gaps. Fix them first before any new feature work.**

#### 0a. `PruneStaleAliasBindings` tests

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreePruneStaleBindingsTests.cs` (NEW)

Write 3 tests against the real `BehaviorTreeAsset`:

- **`PruneStaleAliasBindings_RemovesBindings_ForUnknownRequiringAsset`**
  Setup: create an asset; add two alias bindings to the same variable but from two different `RequiringAssetId` GUIDs. Call `PruneStaleAliasBindings` with a collection containing only one of the two GUIDs. Assert: `GetAliasesFor(variableName)` now returns exactly one binding (the one whose `RequiringAssetId` was in the set). Assert: `Changed` event was fired once.

- **`PruneStaleAliasBindings_NoOp_WhenAllKnownAssetIds`**
  Setup: same two bindings. Prune with both GUIDs present. Assert: both bindings remain. Assert: `Changed` event was NOT fired.

- **`GetKnownSubAssetIds_ReturnsAllDistinctRequiringIds`**
  Setup: add aliases to two variables, each from a different `RequiringAssetId`. Assert: `GetKnownSubAssetIds()` returns exactly 2 distinct GUIDs containing both IDs.

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Blackboard/HsmPruneStaleBindingsTests.cs` (NEW)

Write the same 3 tests against real `HsmAsset`. Use `HsmAsset.AddAlias` / `GetAliasesFor` / `Changed`.

#### 0b. Concrete LoadState computation tests

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreeAssetLoadStateTests.cs` (NEW)

Write 4 tests using `BehaviorTreeAsset.SetLoadDiagnostic`:

- **`LoadState_DefaultsToClean`** — freshly constructed asset has `LoadState == BlackboardLoadState.Clean` and `LoadDiagnosticMessage == null`.
- **`SetLoadDiagnostic_SetsClean`** — after `SetLoadDiagnostic(Clean, null)`, `LoadState == Clean` and `LoadDiagnosticMessage == null`.
- **`SetLoadDiagnostic_SetsStructParseFailed`** — after `SetLoadDiagnostic(StructParseFailed, "Parse error")`, `LoadState == StructParseFailed` and `LoadDiagnosticMessage == "Parse error"`.
- **`SetLoadDiagnostic_SetsAssemblyFailed`** — after `SetLoadDiagnostic(AssemblyFailed, "Build failed")`, `LoadState == AssemblyFailed` and message matches.

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Blackboard/HsmAssetLoadStateTests.cs` (NEW)

Same 4 tests against `HsmAsset`.

---

### Task 1: TASK-BB-1f-01 — Cross-region blackboard conflict validator

**Spec:** BB §9.1–§9.2, §9.5, §9.6; TASK-DETAIL.md `TASK-BB-1f-01`

#### 1.1 Add `CrossRegionBlackboardConflict` to `HsmDiagnosticCode`

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnosticCode.cs`

Add a new enum value after `OutputLaneConflict`:

```csharp
// Two sub-trees in different parallel regions of the same composite both write
// to the same master blackboard variable (Approach A alias, Approach B sync-out,
// or both). The writes are concurrent and non-deterministic.
CrossRegionBlackboardConflict,
```

#### 1.2 Implement Rule 8 in `HsmValidator`

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs`

The `HsmValidator.Validate(HsmAsset asset)` method must accept an additional optional parameter for the asset's blackboard:

```csharp
public IReadOnlyList<HsmDiagnostic> Validate(HsmAsset asset,
    IBlackboardManagedAsset? blackboard = null)
```

Add `CheckBlackboardRegionConflicts` to the validation call chain **only when `blackboard != null`**:

```csharp
if (blackboard != null)
    CheckBlackboardRegionConflicts(asset, blackboard, diagnostics);
```

**Implement `CheckBlackboardRegionConflicts`:**

The algorithm mirrors `CheckOutputLaneConflicts` (Rule 7) but operates on blackboard variables instead of output lanes.

Conceptually:
1. For each variable in `blackboard.BlackboardVariables`, determine which `RegionIndex` values write to it.
2. A variable is "written" in a region if the `HsmAsset`'s alias bindings record a `RequiringElementId` that is in a `StateNode` child with that `RegionIndex`, **and** the parent of that child is a parallel composite (`IsParallel == true`).
3. For Approach A: scan `blackboard.GetAliasesFor(v.Name)` for each variable; for each `BlackboardAliasBinding`, look up the `RequiringElementId` in the asset's `AllStates` to find the state and its `RegionIndex`. A state is in a region if its `Parent.IsParallel == true`.
4. For Approach B sync-out: scan `asset.AllStates` for states that host Subtree nodes (we'll look for states with the metadata registered via `RecordSubtreeNodeMeta` on `BehaviorTreeAsset`) — but since `HsmAsset` doesn't track sync bindings directly, **scope the implementation to Approach A aliases only** for this task. Leave a `// TODO TASK-BB-1f-01: add Approach B sync-out scan` comment for the Approach B path.
5. If any two alias bindings for the same variable land in different regions of the **same** parallel composite (same `RequiringAssetId` where the parent `StateNode.IsParallel == true` and the two bindings have different `RegionIndex`), emit `CrossRegionBlackboardConflict`.

**Detailed implementation:**

```csharp
private static void CheckBlackboardRegionConflicts(
    HsmAsset asset,
    IBlackboardManagedAsset blackboard,
    List<HsmDiagnostic> out_)
{
    // Build a map: StateNode.StableId -> StateNode for fast lookup.
    var stateById = asset.AllStates.ToDictionary(s => s.StableId);

    foreach (var variable in blackboard.BlackboardVariables)
    {
        var aliases = blackboard.GetAliasesFor(variable.Name);
        if (aliases.Count < 2) continue;   // Need at least 2 to conflict.

        // For each alias, find the parallel-region index (if any).
        // A binding is "in region R of parallel P" if:
        //   - stateById[RequiringElementId] exists AND
        //   - that state's Parent is a parallel composite (IsParallel) AND
        //   - RegionIndex is the child's RegionIndex.
        var regionsByCompositeId =
            new Dictionary<Guid, List<(int RegionIndex, BlackboardAliasBinding Binding)>>();

        foreach (var binding in aliases)
        {
            if (!stateById.TryGetValue(binding.RequiringElementId, out var state))
                continue;
            if (state.Parent == null || !state.Parent.IsParallel)
                continue;   // Not in a parallel composite — no conflict risk.

            var compositeId = state.Parent.StableId;
            if (!regionsByCompositeId.TryGetValue(compositeId, out var list))
            {
                list = new();
                regionsByCompositeId[compositeId] = list;
            }
            list.Add((state.RegionIndex, binding));
        }

        // Check each parallel composite for multi-region writers.
        foreach (var (compositeId, regionList) in regionsByCompositeId)
        {
            if (regionList.Count < 2) continue;

            // Check all pairs for distinct region indices.
            for (int i = 0; i < regionList.Count; i++)
            for (int j = i + 1; j < regionList.Count; j++)
            {
                if (regionList[i].RegionIndex != regionList[j].RegionIndex)
                {
                    var composite = stateById[compositeId];
                    out_.Add(new HsmDiagnostic(
                        HsmDiagnosticCode.CrossRegionBlackboardConflict,
                        HsmDiagnosticSeverity.Warning,
                        $"Variable '{variable.Name}' is written by sub-trees in regions " +
                        $"{regionList[i].RegionIndex} and {regionList[j].RegionIndex} of " +
                        $"parallel composite '{composite.Name}' — concurrent writes are " +
                        $"non-deterministic.",
                        new[] { composite.StableId }));
                    goto nextVariable;   // One diagnostic per variable is enough.
                }
            }
            nextVariable:;
        }
    }
}
```

#### 1.3 Update `HsmAssetValidator` to pass the blackboard

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmAssetValidator.cs`

The `HsmAssetValidator.Validate(IEditableAsset asset)` currently casts to `HsmAsset` and calls `_inner.Validate(hsmAsset)`. Update:

```csharp
var blackboard = hsmAsset as IBlackboardManagedAsset;  // null if not wired yet
var raw = _inner.Validate(hsmAsset, blackboard);
```

`HsmAsset` implements `IBlackboardManagedAsset` (added in BATCH-10), so this cast succeeds for real HSM assets. The `blackboard != null` guard in `HsmValidator` means the rule is simply silent for non-blackboard assets.

Add `using Hrot.Editor.AiShared.Blackboard;` to the `HsmAssetValidator.cs` file.

#### 1.4 Surface `CrossRegionBlackboardConflict` in `BlackboardDiagnosticCode`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDiagnosticCode.cs`

Add a third value so the panel can style it distinctly:

```csharp
/// <summary>Two sub-trees in different parallel regions write to the same variable.</summary>
CrossRegionConflict,
```

(This is the AiShared-level code; `HsmDiagnosticCode.CrossRegionBlackboardConflict` is the HSM-level code. They don't need to match by name. The `HsmAssetValidator` maps between them — see Step 1.3 for the mapping update needed in `MapSeverity`… actually severity mapping is fine; just make sure the diagnostic code string comes through unchanged.)

#### Tests for Task 1

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs` (NEW)

Write 6 unit tests using real `HsmAsset` + stub `IBlackboardManagedAsset`:

Use the `StubBbAsset` pattern (see existing `RemoveVariablesTests.cs` or `AliasMutableAsset` pattern in AiShared tests). The stub needs:
- `BlackboardVariables` — settable list
- `GetAliasesFor(name)` — returns bindings from a dictionary

Setup helpers: build an `HsmAsset` with a parallel composite state having two child states, each with a distinct `RegionIndex`. The child states' `StableId` values become the `RequiringElementId` for aliases.

- **T1: `Validate_NoBlackboard_ProducesNoConflictDiagnostic`** — call `Validate(asset)` with `blackboard = null`. Assert: no `CrossRegionBlackboardConflict` in result.

- **T2: `Validate_ParallelRegionWriteSameVariable_ProducesConflict`** — two aliases on the same variable, `RequiringElementId` = state in region 0 and state in region 1 of the same parallel composite. Assert: exactly one `CrossRegionBlackboardConflict` with severity Warning and composite's `StableId`.

- **T3: `Validate_ParallelRegionWriteDifferentVariables_NoConflict`** — two aliases, but on *different* variables, one per region. Assert: no `CrossRegionBlackboardConflict`.

- **T4: `Validate_SameRegionTwoAliases_NoConflict`** — two alias bindings for the same variable, both pointing to states in *region 0* of the same parallel composite. Assert: no `CrossRegionBlackboardConflict` (same region, no race).

- **T5: `Validate_SequentialStates_NoConflict`** — two aliases for the same variable, but the binding states are NOT inside a parallel composite (`Parent.IsParallel == false`). Assert: no `CrossRegionBlackboardConflict`.

- **T6: `Validate_OnlyOneAlias_NoConflict`** — a variable with only one alias binding (regardless of region). Assert: no `CrossRegionBlackboardConflict`.

---

### Task 2: TASK-BB-1f-02 — Drop-target validator (refuse unsafe cross-region alias)

**Spec:** BB §9.4, §7.7; TASK-DETAIL.md `TASK-BB-1f-02`

#### 2.1 Add `AllowCrossRegionWrites` flag to `IBlackboardManagedAsset` and implementations

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs`

Add a default interface method:

```csharp
/// <summary>
/// Returns true if the variable has been explicitly permitted to accept
/// concurrent writes from different parallel regions (BB §9.4 override).
/// Default: false (refuse cross-region aliases by default).
/// </summary>
bool IsCrossRegionWriteAllowed(string variableName) => false;

/// <summary>
/// Marks (or clears) the explicit cross-region write permission for a variable.
/// </summary>
void SetCrossRegionWriteAllowed(string variableName, bool allowed) { }
```

Implement concretely in `BehaviorTreeAsset` and `HsmAsset`:

```csharp
// In BehaviorTreeAsset (and HsmAsset similarly):
private readonly HashSet<string> _crossRegionAllowedVariables = new();

public bool IsCrossRegionWriteAllowed(string variableName) =>
    _crossRegionAllowedVariables.Contains(variableName);

public void SetCrossRegionWriteAllowed(string variableName, bool allowed)
{
    if (allowed)
        _crossRegionAllowedVariables.Add(variableName);
    else
        _crossRegionAllowedVariables.Remove(variableName);
    MarkDirty();
}
```

**Note:** Persistence of this flag to the layout method is TASK-BB-1f-05 (next batch). For now, the flag is session-only.

#### 2.2 Add `IsCrossRegionAliasUnsafe` validation helper

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAliasDropValidator.cs` (NEW)

```csharp
namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Validates whether an alias drop would create an unsafe cross-region write
/// conflict, per BB §9.4.
/// </summary>
public static class BlackboardAliasDropValidator
{
    /// <summary>
    /// Returns true if adding the given alias binding to the given variable
    /// would introduce a cross-region write conflict (BB §9.4).
    /// The check is HSM-specific: BTree assets have no parallel regions.
    /// </summary>
    public static bool WouldCreateCrossRegionConflict(
        IBlackboardManagedAsset asset,
        string variableName,
        BlackboardAliasBinding newBinding,
        IReadOnlyDictionary<Guid, int>? regionIndexByStateId)
    {
        // Fast exit: if the asset explicitly allows cross-region writes, never block.
        if (asset.IsCrossRegionWriteAllowed(variableName)) return false;

        // Fast exit: no region map means no parallel structure.
        if (regionIndexByStateId == null || regionIndexByStateId.Count == 0) return false;

        // Fast exit: new binding's element is not in any parallel region.
        if (!regionIndexByStateId.TryGetValue(newBinding.RequiringElementId, out int newRegion))
            return false;

        // Check if any existing alias binding on this variable is in a DIFFERENT region
        // of the same parallel composite.
        var existing = asset.GetAliasesFor(variableName);
        foreach (var b in existing)
        {
            if (b.RequiringAssetId != newBinding.RequiringAssetId) continue; // different asset
            if (!regionIndexByStateId.TryGetValue(b.RequiringElementId, out int existingRegion))
                continue;
            if (existingRegion != newRegion) return true; // Conflict!
        }
        return false;
    }
}
```

The `regionIndexByStateId` dictionary maps `StateNode.StableId → RegionIndex`. The window builds this lazily from the HSM asset when performing HSM-backed drop validation. For BTree assets, pass `null` (no parallel regions).

#### 2.3 Wire the validator into `BlackboardAuthoringWindow`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`

In the drag-drop handling code (where alias drops are processed, `DrawClientArea` drop logic):

1. Before calling `bbAsset.AddAlias(variableName, newBinding)`, call `BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(bbAsset, variableName, newBinding, regionMap)`.

2. For the region map: the window already holds a reference to `bbAsset`. If `bbAsset` is also an `HsmAsset` (cast via `_store.ActiveAsset as HsmAsset`), build the region map from `hsmAsset.AllStates` where `s.Parent?.IsParallel == true`:
   ```csharp
   var regionMap = hsmAsset?.AllStates
       .Where(s => s.Parent?.IsParallel == true)
       .ToDictionary(s => s.StableId, s => s.RegionIndex);
   ```
   For non-HSM assets, pass `null`.

3. When `WouldCreateCrossRegionConflict` returns `true`:
   - Do NOT add the alias.
   - Set a flag `_dropRejectedCrossRegion = true` and store the variable name.
   - In `DrawClientArea`, render an `ImGui.TextColored(red, "Cannot alias: ...")` popup/tooltip.

4. Add the "Allow concurrent writes" toggle to the variable's `⋮` menu (the existing `DrawVariableContextMenu` call, or where the context menu is rendered):
   - Menu item: `"Allow concurrent writes"` — when checked, calls `bbAsset.SetCrossRegionWriteAllowed(variableName, true)`; unchecked clears it.
   - The menu item label includes `" (unsafe)"` when the variable currently has cross-region aliases.

**Minimal implementation note:** The window's current drag-drop plumbing may not have a full alias-drop handler yet (this is editor UI that was scaffold-in-place through the batches). If a complete drag-drop handler is not present, add a stub drop-validation helper that is called from the conceptual drop-completion site, document where it would fire, and write the tests against the model-level `BlackboardAliasDropValidator` directly. Do not invent ImGui drop behavior that wasn't previously scaffolded — validate the model layer and leave the UI wiring as a `// TODO` comment at the right callsite.

#### Tests for Task 2

**File:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAliasDropValidatorTests.cs` (NEW)

Write 7 unit tests against `BlackboardAliasDropValidator.WouldCreateCrossRegionConflict`:

- **T1: `Returns_False_WhenNoRegionMap`** — `regionIndexByStateId = null`. Assert: returns false (no conflict possible without region info).
- **T2: `Returns_False_WhenEmptyRegionMap`** — empty dictionary. Assert: false.
- **T3: `Returns_False_WhenNewBindingNotInAnyRegion`** — `newBinding.RequiringElementId` not in region map. Assert: false.
- **T4: `Returns_False_WhenNoExistingAliases_InSameAsset`** — no existing aliases for the variable. Assert: false.
- **T5: `Returns_True_WhenExistingAlias_InDifferentRegion`** — existing alias in region 0, new binding in region 1, same `RequiringAssetId`. Assert: true (conflict).
- **T6: `Returns_False_WhenExistingAlias_InSameRegion`** — existing alias in region 1, new binding in region 1. Assert: false (same region, safe).
- **T7: `Returns_False_WhenCrossRegionWriteAllowed`** — same conflict scenario as T5 but `IsCrossRegionWriteAllowed` returns true. Assert: false (override suppresses check).

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreeCrossRegionAllowedTests.cs` (NEW)

Write 3 model tests for `BehaviorTreeAsset.SetCrossRegionWriteAllowed` / `IsCrossRegionWriteAllowed`:

- `SetCrossRegionWriteAllowed_True_AllowsVariable`
- `SetCrossRegionWriteAllowed_False_AfterTrue_DisallowsVariable`
- `SetCrossRegionWriteAllowed_FiresChanged`

---

## 🧪 Testing Requirements

**Minimum new tests:** 30+ across all projects.

**Quality bar:**
- Corrective tests (Task 0): test actual `BehaviorTreeAsset` and `HsmAsset` behavior — not stubs.
- Task 1 tests: use a real `HsmAsset` with properly-configured parallel composite state (parent with `IsParallel = true` and children with different `RegionIndex` values). Do not mock the state model.
- Task 2 tests: test `BlackboardAliasDropValidator` directly (pure function, easy to unit-test).
- All tests must verify ACTUAL behavior values (counts, return values, event fires), not just "no exception thrown."

## ⚠️ Quality Standards

**❗ xUnit analyzer rules are build errors.** Use `Assert.Empty`, `Assert.Single`, `Assert.True`, `Assert.False` correctly. TreatWarningsAsErrors=true.

**❗ DO NOT modify** any passing test. Do not rename existing types or methods. Minimal diffs only.

**❗ After every task**, run all three test suites and confirm 0 failures before proceeding.

**❗ Check `HsmAsset` constructor signature** before writing tests — use the actual constructor, not a guessed one. Inspect `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` first.

---

## 📊 Report Requirements

When done, submit `.dev/ai-hsm-btree-vis-edit/reports/BATCH-12-REPORT.md` answering:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** Were any of the corrective-task implementations simpler or more complex than expected?

**Suggested commit message** (fill in the test counts):

```
feat: BATCH-10 corrective tests + cross-region blackboard conflict validator

Corrective (BATCH-10 P2 gaps):
- BTreePruneStaleBindingsTests, HsmPruneStaleBindingsTests: 3 tests each
- BTreeAssetLoadStateTests, HsmAssetLoadStateTests: 4 tests each

TASK-BB-1f-01 (cross-region blackboard conflict):
- HsmDiagnosticCode.CrossRegionBlackboardConflict added
- HsmValidator Rule 8: CheckBlackboardRegionConflicts (Approach A aliases)
- HsmAssetValidator wires blackboard to validator
- Tests: HsmValidatorBlackboardConflictTests (6 tests)

TASK-BB-1f-02 (drop-target validator):
- IBlackboardManagedAsset: IsCrossRegionWriteAllowed / SetCrossRegionWriteAllowed
- BlackboardAliasDropValidator (pure function, no ImGui)
- BTree/HsmAsset concrete implementations
- Tests: BlackboardAliasDropValidatorTests (7), BTreeCrossRegionAllowedTests (3)

Tests: BTree +X, AiShared +Y, HSM +Z
Build: 0 errors, 0 warnings
```

---

## 🎯 Success Criteria

- [ ] Task 0 corrective tests all pass — PruneStale (3+3), LoadState (4+4)
- [ ] `HsmDiagnosticCode.CrossRegionBlackboardConflict` added
- [ ] `HsmValidator.Validate(asset, blackboard?)` accepts optional blackboard
- [ ] Rule 8 fires when two aliases for the same variable land in different parallel regions
- [ ] Rule 8 does NOT fire for same-region, sequential, or single-alias cases
- [ ] `BlackboardAliasDropValidator.WouldCreateCrossRegionConflict` implemented as pure function
- [ ] `IsCrossRegionWriteAllowed` / `SetCrossRegionWriteAllowed` on both concrete assets
- [ ] All 3 test suites pass: 0 failures
- [ ] Build: 0 errors, 0 warnings
- [ ] Report submitted

---

## ⚠️ Common Pitfalls

- **`HsmAsset` parallel state setup in tests:** You need a state with `IsParallel = true` and children with `RegionIndex` set. Study the existing `HsmAssetValidatorTests` and `HsmValidator` tests to see how states are constructed. Use `HsmAsset.AddState`, setting `IsParallel`, `RegionIndex`, and linking `Parent`.
- **`BlackboardAliasBinding` constructor:** Check the actual constructor in `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAliasBinding.cs` before writing test setup code.
- **`goto nextVariable`:** The goto label approach used in the pseudocode above is a valid C# pattern inside the foreach. Alternatively, use a boolean flag + `break`. Either is fine.
- **`using` directive for `IBlackboardManagedAsset` in `HsmValidator.cs`:** Add `using Hrot.Editor.AiShared.Blackboard;` — check there are no circular project references before adding.

---

## 📚 Reference Materials

- **Design:** `.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` §9.1–9.4
- **Task Detail:** `.dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — TASK-BB-1f-01, TASK-BB-1f-02
- **Existing Rule 7 pattern:** `HsmValidator.CheckOutputLaneConflicts` (line ~165)
- **BATCH-10 review:** `.dev/ai-hsm-btree-vis-edit/reviews/BATCH-10-REVIEW.md`
- **Baseline for PruneStale tests:** `BehaviorTreeAsset.PruneStaleAliasBindings` (line ~323), `HsmAsset.PruneStaleAliasBindings` (line ~196)
