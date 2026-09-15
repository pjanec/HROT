# BATCH-13: Corrective (BATCH-12 P2) + TASK-BB-1f-06 Read-Only Access Filtering

**Batch Number:** BATCH-13
**Tasks:** Corrective-P2 (BATCH-12 gaps), TASK-BB-1f-06
**Phase:** 1.5f — Validation, diagnostics, recovery
**Estimated Effort:** 4–6 hours
**Priority:** HIGH
**Dependencies:** BATCH-12 (approved)

---

## Onboarding

Read in this order before writing any code:

1. **BATCH-12 review:** `.dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-12-REVIEW.md`
   — understand exactly which P2 items need fixing
2. **Task detail:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` §TASK-BB-1f-06
3. **Design:** `.dev/_DONE/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` §9.6
   — `[BlackboardReadOnly]` access annotation makes an action safe (not a writer)

Key source files:
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs` — add schema field + `HasWritingAction` helper
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` — `BlackboardAccess` enum already present
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs` — add T7 test here
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — fix `SetCrossRegionWriteAllowed`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — fix `SetCrossRegionWriteAllowed`

Key invariants:
- `TreatWarningsAsErrors=true`; zero-warning build required in affected projects.
- Preserve all existing comments exactly. Minimize diffs.

### Build & test commands

```powershell
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build --logger "console;verbosity=minimal"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build --logger "console;verbosity=minimal"
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build --logger "console;verbosity=minimal"
```

### Baseline test counts (BATCH-12 approved)

| Project | Passing |
|---------|---------|
| `Hrot.BTree.Editor.Tests` | 275 |
| `Hrot.Hsm.Editor.Tests` | 228 |
| `Hrot.Editor.AiShared.Tests` | 379 |

### Report Submission

**When done:** `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-13-REPORT.md`
**Questions:** `.dev/_DONE/ai-hsm-btree-vis-edit/questions/BATCH-13-QUESTIONS.md`

---

## MANDATORY WORKFLOW

Complete tasks in order. ALL tests must pass before moving to the next task.

1. **Corrective-0** → fix + tests → ALL tests pass
2. **1f-06** → implement + tests → ALL tests pass

Run `dotnet build` after each task. Fix every compile error and test failure before proceeding.

---

## Task 0: Corrective — P2 items from BATCH-12 review

### 0a. Add diagnostic message content assertion

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs`

Add a new test — do NOT modify the existing T2 test:

- **`Validate_ConflictDiagnostic_MessageContainsVariableAndCompositeName`**

  Use the same setup as T2 (parallel composite, two alias bindings in different regions, variable
  named `"speed"`). After asserting the conflict diagnostic exists, additionally assert:
  ```csharp
  Assert.Contains("speed", conflict.Message, StringComparison.Ordinal);
  Assert.Contains("Parallel", conflict.Message, StringComparison.Ordinal);
  ```
  This protects the human-readable content of the diagnostic from accidental breakage.

### 0b. Guard SetCrossRegionWriteAllowed against no-op Changed fires

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`

Change `SetCrossRegionWriteAllowed` to only call `MarkDirty` when the value actually changes:

```csharp
public void SetCrossRegionWriteAllowed(string variableName, bool allowed)
{
    bool wasAllowed = _crossRegionAllowedVariables.Contains(variableName);
    if (wasAllowed == allowed) return;
    if (allowed) _crossRegionAllowedVariables.Add(variableName);
    else         _crossRegionAllowedVariables.Remove(variableName);
    MarkDirty();
}
```

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — same change.

**Test (new, in `BTreeCrossRegionAllowedTests.cs`):**

- **`SetCrossRegionWriteAllowed_DoesNotFireChanged_WhenValueUnchanged`**:
  Set `true`, reset the counter, set `true` again → `Changed` did NOT fire (count remains 0).
  Also: set `false` when already `false` → no fire.

---

## Task 1: TASK-BB-1f-06 — `[BlackboardReadOnly]`/`[BlackboardReadWrite]` access filtering

**Spec:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` §TASK-BB-1f-06; design §9.6.

### Context

`ActionSchemaEntry.Access` already holds the `BlackboardAccess` value read from the attribute.
The cross-region conflict validator (`HsmValidator.CheckBlackboardRegionConflicts`) currently
treats every aliased state as a potential writer regardless of its action's access annotation.
This task makes the validator skip states whose actions are all `[BlackboardReadOnly]`.

The conservative default (§9.6): **unknown or unannotated = `ReadWrite`**. A state is a writer
if ANY of its actions (OnEntry, OnExit, Activity, Timer) is non-ReadOnly or schema-unknown.

### 1.1 Add schema field to HsmValidator

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs`

Add an optional `IActionSchemaExporter?` field and inject it via constructor:

```csharp
private readonly IActionSchemaExporter? _schema;

public HsmValidator(IActionSchemaExporter? schema = null)
{
    _schema = schema;
}
```

The default `new HsmValidator()` constructor still works (schema = null → conservative behavior).

### 1.2 Change CheckBlackboardRegionConflicts from static to instance

`CheckBlackboardRegionConflicts` is currently `private static`. Because it now needs `_schema`,
change it to `private` (instance method). The call site at line ~34 already passes `this`
implicitly — just remove the `static` keyword from the method signature. No other changes to
the call site.

### 1.3 Add HasWritingAction helper

Add a new private method to `HsmValidator`:

```csharp
// Returns true if the state has at least one action that writes to the blackboard.
// Conservative: unknown schema, unknown FQN, or Unknown access -> treat as writer.
// A state with ONLY ReadOnly actions is safe to skip from conflict detection.
private bool HasWritingAction(StateNode state)
{
    if (_schema == null) return true;   // no schema -> conservative
    string?[] fqns = {
        state.OnEntryAction,
        state.OnExitAction,
        state.ActivityAction,
        state.TimerAction,
    };
    bool hasAnyAction = false;
    foreach (var fqn in fqns)
    {
        if (fqn == null) continue;
        hasAnyAction = true;
        var entry = _schema.Lookup(fqn);
        if (entry == null) return true;                              // unknown FQN -> conservative
        if (entry.Access != BlackboardAccess.ReadOnly) return true;  // non-ReadOnly -> writer
    }
    // If no action FQNs at all, the state has no actions -> cannot write -> safe
    return hasAnyAction;   // false when no FQNs; true when all known FQNs are non-ReadOnly
    // Note: a state with zero FQNs and an alias binding is unusual; we return false
    //       (no actions = no writes) so as not to produce a spurious conflict diagnostic.
}
```

**IMPORTANT:** The logic for `HasWritingAction` when `hasAnyAction == false` (state has no action
FQNs configured) should return `false` — a state with no actions cannot write anything.

Simplify the logic:

```csharp
private bool HasWritingAction(StateNode state)
{
    if (_schema == null) return true;
    string?[] fqns = {
        state.OnEntryAction,
        state.OnExitAction,
        state.ActivityAction,
        state.TimerAction,
    };
    foreach (var fqn in fqns)
    {
        if (fqn == null) continue;
        var entry = _schema.Lookup(fqn);
        if (entry == null) return true;                              // unknown -> conservative
        if (entry.Access != BlackboardAccess.ReadOnly) return true;  // non-ReadOnly -> writer
    }
    // Either no FQNs (zero actions) or all known FQNs are ReadOnly -> not a writer.
    return false;
}
```

### 1.4 Use HasWritingAction in CheckBlackboardRegionConflicts

In `CheckBlackboardRegionConflicts`, add the `HasWritingAction` filter immediately after the
`state.Parent.IsParallel` check:

```csharp
foreach (var binding in aliases)
{
    if (!stateById.TryGetValue(binding.RequiringElementId, out var state))
        continue;
    if (state.Parent == null || !state.Parent.IsParallel)
        continue;
    if (!HasWritingAction(state))        // NEW: skip read-only states
        continue;
    // ... rest of region bucket logic unchanged ...
}
```

### 1.5 Update HsmAssetValidator to pass the schema

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmAssetValidator.cs`

`HsmAssetValidator` holds a reference to `HsmValidator _inner`. Currently `_inner` is created
without a schema. To pass the schema, `HsmAssetValidator` needs to receive and store
`IActionSchemaExporter?`.

Option A (preferred): accept `IActionSchemaExporter?` in `HsmAssetValidator`'s constructor and
pass it to `new HsmValidator(schema)`:

```csharp
public HsmAssetValidator(IActionSchemaExporter? schema = null)
{
    _inner = new HsmValidator(schema);
}
```

Check `HsmAssetValidator`'s construction site in DI registration or factories — if it is
constructed without parameters (e.g., `new HsmAssetValidator()`), the default `null` schema
preserves the current conservative behavior.

### 1.6 Tests

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs`

Add 4 tests (append to the existing file):

- **`Validate_ReadOnlyActions_NoConflict`**: Setup as T2 (parallel composite, two children, aliases
  on same variable). Provide a stub `IActionSchemaExporter` that returns a `ReadOnly` access entry
  for any FQN. Assign the same FQN to `child0.ActivityAction` and `child1.ActivityAction`.
  Call `new HsmValidator(stubSchema).Validate(asset, bb)`.
  Assert: **no** `CrossRegionBlackboardConflict` (both states are pure readers).

- **`Validate_MixedAccess_OneReadOnlyOneReadWrite_ProducesConflict`**: Same setup, but
  `child0.ActivityAction` → ReadOnly, `child1.ActivityAction` → ReadWrite (or Unknown).
  Assert: **one** `CrossRegionBlackboardConflict` (child1 is a writer).

- **`Validate_NullSchema_TreatsAllAsWriters`**: Same parallel setup, assign action FQNs to both
  children. Pass `new HsmValidator(schema: null)`. Assert: conflict IS produced (conservative).

- **`Validate_StateWithNoActions_NotAWriter`**: State has all four action FQNs null. It has an
  alias binding in region 0. Another state in region 1 with a ReadWrite action also has an alias.
  Assert: only the region-1 state counts as writer → no conflicting PAIR → no conflict diagnostic.

For the stub schema in these tests, create a minimal `StubActionSchemaExporter : IActionSchemaExporter`
inside the file (using `file sealed class` — fine here since it's used as local variables only):

```csharp
file sealed class StubActionSchemaExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, BlackboardAccess> _entries;

    public StubActionSchemaExporter(params (string Fqn, BlackboardAccess Access)[] entries)
    {
        _entries = entries.ToDictionary(e => e.Fqn, e => e.Access);
    }

    public ActionSchemaEntry? Lookup(string fqn)
    {
        if (!_entries.TryGetValue(fqn, out var access)) return null;
        return new ActionSchemaEntry(fqn, typeof(float), ActionHosting.HsmAction, access, null);
    }

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => 
        _entries.ToDictionary(kv => kv.Key, kv => 
            new ActionSchemaEntry(kv.Key, typeof(float), ActionHosting.HsmAction, kv.Value, null));

    public void Rebuild() { }
    public event Action? Changed { add { } remove { } }
}
```

Check `IActionSchemaExporter`'s interface members to ensure the stub implements all required
members. Only implement what's declared — do not guess.

---

## Testing Requirements

- Minimum new tests: 6 (1 corrective message test + 1 no-op Changed test + 4 for 1f-06)
- All tests must verify actual behavioral values (not just no-exception)
- Use `file sealed class` for stubs that are only used as local variables within methods (NOT in method signatures returning the type)

Expected test counts after BATCH-13:
- `Hrot.BTree.Editor.Tests`: 275 + 1 = **276**
- `Hrot.Hsm.Editor.Tests`: 228 + 5 = **233**
- `Hrot.Editor.AiShared.Tests`: 379 (unchanged)

---

## Quality Standards

**NOT ACCEPTABLE:**
- `Assert.NotNull` only (must check the value)
- Tests that only verify no exception thrown
- Skipping the `Validate_NullSchema_TreatsAllAsWriters` test (conservative behavior is critical)

**REQUIRED:**
- For the no-op Changed test: subscribe to `Changed`, call `SetCrossRegionWriteAllowed` with the
  same value twice, assert the event count is 0 on the second call.
- For 1f-06 tests: set action FQNs (`child0.ActivityAction = "Fhsm.Ai.TestActions.Move"`) before
  calling `Validate` — the `HasWritingAction` method reads these from the state directly.

---

## Report Requirements

In your report, address:

1. Did `HsmAssetValidator`'s construction site need changes? Where is it constructed?
2. Does `IActionSchemaExporter.All` return a dictionary or list? Check the actual interface.
3. Was the `StubActionSchemaExporter` stub complete? Which members did you need to implement?
4. Did you encounter any issues with the `file` keyword on the stub class? How did you handle it?

---

## Success Criteria

- [ ] T7 message content test added to `HsmValidatorBlackboardConflictTests`
- [ ] `SetCrossRegionWriteAllowed` no-op guard added to both `BehaviorTreeAsset` and `HsmAsset`
- [ ] No-op Changed test added to `BTreeCrossRegionAllowedTests`
- [ ] `HsmValidator` accepts optional `IActionSchemaExporter?` in constructor
- [ ] `HasWritingAction` helper skips `ReadOnly`-annotated states
- [ ] `CheckBlackboardRegionConflicts` uses `HasWritingAction` filter
- [ ] 4 new 1f-06 behavioral tests in `HsmValidatorBlackboardConflictTests`
- [ ] All 3 test suites pass: 0 failures
- [ ] Build: 0 errors, 0 warnings
- [ ] Report submitted
