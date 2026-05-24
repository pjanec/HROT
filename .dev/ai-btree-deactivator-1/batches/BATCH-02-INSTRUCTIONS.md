# BATCH-02: Registry Contract Tests (P1 Fix) + Roslyn Generator Extension (Phase 2)

**Batch Number:** BATCH-02
**Tasks:** P1-FIX (TASK-EQL-002 contract tests), P2-FIX (TASK-EQL-001 contract tests), TASK-EQL-004
**Phase:** Phase 1 P1/P2 corrections + Phase 2 — Roslyn Generator
**Estimated Effort:** 10–14 hours
**Priority:** HIGH (P1 fix) + HIGH (Phase 2)
**Dependencies:** BATCH-01 (complete)

---

## Onboarding & Workflow

### Developer Instructions

This batch has two parts:

**Part A (P1/P2 fix):** Add the missing unit tests for TASK-EQL-001 and TASK-EQL-002 contract
conditions. These are small additions to existing test files in the FastBTree submodule.
Do this first — it is fast and unlocks the Phase 1 sign-off.

**Part B (TASK-EQL-004):** Extend `BTreeActionGenerator` to detect `[BTreeDeactivator]`
annotations and emit `registry.RegisterDeactivator(...)` calls in the generated
`FbtActionRegistrar.g.cs`. Write Roslyn compilation tests following the existing
`TkbDescriptorGeneratorTests.cs` pattern.

### Required Reading (IN ORDER)

1. **BATCH-01 Review:** `.dev/ai-btree-deactivator-1/reviews/BATCH-01-REVIEW.md` — understand
   what was done and what gaps need fixing.
2. **Design §2:** `.dev/ai-btree-deactivator-1/DESIGN.md` §2.1–§2.5 — full specification for
   the generator extension.
3. **Task Specification:** `.dev/ai-btree-deactivator-1/TASK-DETAIL.md` — TASK-EQL-002 T1–T5
   and TASK-EQL-004 T1–T5.
4. **Existing generator:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` —
   read the full file before modifying. Understand `BTreeMethodInfo`, `GroupEntry`, `Execute`,
   `GetMethodInfo`, `GenerateRegistrar`.
5. **Existing diagnostic codes:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedBhuDiagnostics.cs`
   — understand existing BHU-001/002/003 pattern before adding BHU-016/017.
6. **Generator test pattern:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDescriptorGeneratorTests.cs`
   — this is the canonical example for in-memory Roslyn compilation tests. Follow this pattern
   exactly for TASK-EQL-004 generator tests.
7. **Existing ActionRegistry tests:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/ActionRegistryTests.cs`
8. **Existing Attribute tests:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/AttributeTests.cs`

### Source Code Locations

**Part A:**
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/ActionRegistryTests.cs` — add 5 test methods
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/AttributeTests.cs` — add 4 test methods

**Part B:**
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` — extend
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedBhuDiagnostics.cs` — add BHU-016/017
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs` — **NEW** test file

### Build & Test Commands

```powershell
# Part A: FastBTree tests
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj

# Part B: FDP toolkit tests (includes BTreeActionGeneratorTests)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj

# Part B: verify FDP builds (generator runs during build)
dotnet build FDP\FDP.sln --no-restore
```

All commands run from `D:\WORK\IOS-IG-SimHost-FDP`.

### Report Submission

`.dev/ai-btree-deactivator-1/reports/BATCH-02-REPORT.md`

---

## Context

BATCH-01 delivered the Phase 1 infrastructure (delegate, attribute, ActionRegistry extension,
Interpreter delta-tracking). The review found one P1 gap (missing TASK-EQL-002 contract tests)
and one P2 gap (missing TASK-EQL-001 contract tests). These must be fixed before Phase 1 is
considered fully complete.

Phase 2 (TASK-EQL-004) extends the Roslyn incremental generator so that developers never need
to manually call `registry.RegisterDeactivator`. After BATCH-02, any method annotated with
`[BTreeDeactivator("...")]` in a compiled assembly will automatically have its registration
emitted in `FbtActionRegistrar.g.cs`.

---

## Batch Objectives

1. Close all P1 and P2 test gaps from BATCH-01.
2. Extend `BTreeActionGenerator` with deactivator detection and emission.
3. Add diagnostics `BHU-016` and `BHU-017` to `SharedBhuDiagnostics.cs`.
4. Write Roslyn generator unit tests covering T1–T5 from TASK-EQL-004.

---

## PART A — Contract Test Fixes

### Fix 1: TASK-EQL-002 contract tests (P1)

**File:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/ActionRegistryTests.cs` (MODIFY)

**Task Specification:** See TASK-DETAIL.md TASK-EQL-002 success conditions T1–T5.

Add 5 test methods to the existing `ActionRegistryTests` class (or create a new nested
class `DeactivatorTests` within the same file):

- **T1:** After `RegisterDeactivator("Foo", deleg)`, `TryGetDeactivator("Foo", out var d)`
  returns `true` AND `d` is the **same delegate instance** (reference equality, not just a
  non-null check).
- **T2:** `TryGetDeactivator("Missing", out _)` returns `false`.
- **T3:** `RegisterDeactivator(null, validDeleg)` throws `ArgumentNullException` with
  `paramName == "key"`.
- **T4:** `RegisterDeactivator("key", null)` throws `ArgumentNullException` with
  `paramName == "deactivator"` (or any non-null param name is acceptable; the throw itself
  is mandatory).
- **T5:** Registering same key twice — second registration wins. Assert that after registering
  `deleg1` then `deleg2` under the same key, `TryGetDeactivator` returns `deleg2`, not
  `deleg1`.

### Fix 2: TASK-EQL-001 contract tests (P2)

**File:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/AttributeTests.cs` (MODIFY)

**Task Specification:** See TASK-DETAIL.md TASK-EQL-001 success conditions T1–T4.

Add 4 test methods:

- **T1:** `typeof(NodeDeactivatorDelegate<TestBlackboard, MockContext>).Namespace == "Fbt"`.
- **T2:** `typeof(BTreeDeactivatorAttribute).Namespace == "Fbt"`.
- **T3:** `new BTreeDeactivatorAttribute("Foo.Bar").TargetAction == "Foo.Bar"`.
- **T4:** A lambda `(ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { }`
  can be assigned to `NodeDeactivatorDelegate<TestBlackboard, MockContext>` without a cast
  (compile-time check — the test just constructs the delegate, asserts it is non-null).

---

## PART B — TASK-EQL-004: BTreeActionGenerator deactivator detection and emission

**Design reference:** DESIGN.md §2.1–§2.5
**Task specification:** TASK-DETAIL.md TASK-EQL-004

### Step 1: Extend BTreeMethodInfo

Add two fields to `BTreeMethodInfo` (in `BTreeActionGenerator.cs`):

```csharp
public bool IsDeactivator { get; set; }
public string TargetAction { get; set; } = string.Empty;
```

### Step 2: Extend GroupEntry

Add a `Deactivators` list to `GroupEntry`:

```csharp
public List<BTreeMethodInfo> Deactivators { get; } = new List<BTreeMethodInfo>();
```

### Step 3: Extend GetMethodInfo to detect [BTreeDeactivatorAttribute]

At the start of `GetMethodInfo`, before checking `[BTreeActionAttribute]`, add detection for
`[BTreeDeactivatorAttribute]`:

```csharp
var deactivatorAttr = symbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "BTreeDeactivatorAttribute");
if (deactivatorAttr != null)
{
    // TargetAction is constructor arg[0]
    string target = deactivatorAttr.ConstructorArguments.Length > 0
        ? deactivatorAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty
        : string.Empty;

    // Resolve TBlackboard and TContext from the 4-param deactivator signature
    if (symbol.Parameters.Length != 4) return null; // only 4-param deactivators
    string tbType = symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    string tcType = symbol.Parameters[2].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return new BTreeMethodInfo
    {
        MethodName = symbol.Name,
        FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
        TBlackboardType = tbType,
        TContextType = tcType,
        IsDeactivator = true,
        TargetAction = target,
    };
}
```

The deactivator method must have 4 parameters (matching `NodeDeactivatorDelegate` signature).
Return null if the param count is wrong; the diagnostic BHU-016 is emitted during Execute.

### Step 4: Add BHU-016 and BHU-017 to SharedBhuDiagnostics.cs

```csharp
internal static readonly DiagnosticDescriptor BHU016_DeactivatorMissingTarget = new DiagnosticDescriptor(
    id: "BHU_016",
    title: "BTreeDeactivator missing or empty TargetAction",
    messageFormat: "Deactivator method ''{0}'' has an empty or missing TargetAction; skipping emission",
    category: "BTreeActionGenerator",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

internal static readonly DiagnosticDescriptor BHU017_DeactivatorUnknownTarget = new DiagnosticDescriptor(
    id: "BHU_017",
    title: "BTreeDeactivator TargetAction not found",
    messageFormat: "Deactivator method ''{0}'': TargetAction ''{1}'' does not match any [BTreeAction] or [BTreeCondition] method in this compilation",
    category: "BTreeActionGenerator",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

Add these to `SharedBhuDiagnostics` and reference them from `BTreeActionGenerator`.

### Step 5: Extend Execute to collect deactivators and validate

In `Execute`, after building `registrable`, `reusable`, and `sharedAiMethods`, also collect:

```csharp
var deactivators = new List<BTreeMethodInfo>();
foreach (var m in methods)
{
    if (m == null) continue;
    if (m.IsDeactivator) deactivators.Add(m);
}
```

Then, before calling `GenerateRegistrar`, resolve each deactivator's `TargetAction` to a
known method key and assign it to the appropriate group:

```csharp
foreach (var d in deactivators)
{
    // Validate: empty TargetAction -> BHU-016
    if (string.IsNullOrEmpty(d.TargetAction))
    {
        context.ReportDiagnostic(Diagnostic.Create(
            BHU016_DeactivatorMissingTarget, /* location */ null, d.MethodName));
        continue;
    }

    // Find the group matching this deactivator's (TBlackboard, TContext) pair
    var group = mergedGroups.FirstOrDefault(
        g => g.TBlackboardType == d.TBlackboardType && g.TContextType == d.TContextType);
    if (group == null) continue; // No matching group for this deactivator

    // Validate: TargetAction must match a known action or bridge key in this group
    bool knownAction = group.Direct.Any(a => a.FullQualifiedMethodName == d.TargetAction)
        || group.Bridges.Any(b => b.FullQualifiedMethodName + "@0" == d.TargetAction);
    if (!knownAction)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            BHU017_DeactivatorUnknownTarget, /* location */ null, d.MethodName, d.TargetAction));
        continue;
    }

    group.Deactivators.Add(d);
}
```

### Step 6: Extend GenerateRegistrar to emit RegisterDeactivator calls

In `GenerateRegistrar`, inside the `RegisterAll` method emission, after the SharedAi adapter
loop, add deactivator emission:

```csharp
foreach (var m in group.Deactivators)
{
    sb.AppendLine("            registry.RegisterDeactivator(\"" + m.TargetAction + "\", global::" + m.FullQualifiedMethodName + ");");
}
```

### Key convention for 3-param bridge deactivators (DESIGN.md §2.5)

A deactivator for a 3-param bridge action uses the `"{fullMethodName}@0"` compound key.
The `TargetAction` string in the attribute must already include the `@0` suffix (e.g.,
`"Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0"`). The
generator emits this key exactly as-is into `RegisterDeactivator`. The validation check in
Step 5 must also check against the `@0` form of bridge method names.

---

## Test Requirements

### Part A tests

Minimum 9 test methods total across `ActionRegistryTests.cs` (5) and `AttributeTests.cs` (4).

**Quality standards:**
- T1 in ActionRegistry: assert **reference equality** of the delegate instance, not just
  `Assert.NotNull`.
- T3/T4: use `Assert.Throws<ArgumentNullException>`.
- T5: register delegate1, then delegate2, assert `TryGetDeactivator` returns delegate2.

### Part B tests — BTreeActionGeneratorTests.cs

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs` (NEW)

Follow the pattern from `TkbDescriptorGeneratorTests.cs` exactly:
- Use `CSharpCompilation.Create` with in-memory source strings.
- Run `CSharpGeneratorDriver.Create(new BTreeActionGenerator())`.
- Assert on the generated source content.

You must include attribute stubs matching production FQNs (`BTreeActionAttribute`,
`BTreeDeactivatorAttribute`, `IAIContext`, `BehaviorTreeState`, etc.) in the in-memory
compilation. The `TkbDescriptorGeneratorTests.cs` pattern shows how to include stub types.

Write exactly 5 test methods corresponding to TASK-EQL-004 T1–T5:

**T1:** Source with a 4-param `[BTreeAction]` method `Foo.Bar.Action_X` AND a 4-param
`[BTreeDeactivator("Foo.Bar.Action_X")]` method. Assert the generated source contains
`registry.RegisterDeactivator("Foo.Bar.Action_X", global::Foo.Bar.Deactivate_X)`.

**T2:** Source with a 3-param `[BTreeAction]` bridge method `Foo.Bar.Action_Y` AND a 4-param
`[BTreeDeactivator("Foo.Bar.Action_Y@0")]` method. Assert the generated source contains
`registry.RegisterDeactivator("Foo.Bar.Action_Y@0", global::Foo.Bar.Deactivate_Y)`.

**T3:** Source with a 4-param `[BTreeDeactivator("")]` method (empty target). Assert:
(a) the generated source does NOT contain `RegisterDeactivator` for this method;
(b) the run result diagnostics contain a warning with ID `BHU_016`.

**T4:** Source with `[BTreeDeactivator("Foo.Unknown")]` where `"Foo.Unknown"` matches no
`[BTreeAction]` in the compilation. Assert:
(a) no `RegisterDeactivator` call emitted;
(b) diagnostics contain `BHU_017`.

**T5 (regression):** Source with only `[BTreeAction]` methods and no `[BTreeDeactivator]`
methods. Assert the generated source is identical in structure to what was produced before
this batch (no `RegisterDeactivator` lines appear).

### Test-Driven Task Progression (MANDATORY WORKFLOW)

```
Part A (test fixes): Write and verify all 9 contract tests pass.
Part B Step 3-4 (detection + diagnostics): Implement → Write T3/T4 generator tests → ALL pass.
Part B Step 5-6 (emission): Implement → Write T1/T2/T5 generator tests → ALL pass.
Full suite: Run full FDP toolkit tests — all existing + new tests pass.
```

**DO NOT** move to the next sub-step until all tests from the current sub-step pass.

---

## Developer Insights Section (required in report)

1. **What issues were encountered?** (Roslyn compilation stubs, attribute detection, key
   resolution for bridge methods)
2. **What weak points did you spot?** (`BTreeMethodInfo` not being a record type;
   `GroupEntry.Deactivators` list not initialized in constructor; any incremental pipeline
   invalidation concerns)
3. **What design decisions did you make beyond the spec?** (method name for the diagnostic
   location resolution, how you structured the deactivator collection in `Execute`)
4. **Were there any gaps in DESIGN.md §2?** (especially around the `@0` suffix validation
   in the unknown-target check)

---

## Report Format

`.dev/ai-btree-deactivator-1/reports/BATCH-02-REPORT.md`:

```markdown
# BATCH-02 Report

## Summary

## Tasks Completed
- [x] P1 Fix: TASK-EQL-002 contract tests (ActionRegistryTests.cs)
- [x] P2 Fix: TASK-EQL-001 contract tests (AttributeTests.cs)
- [x] TASK-EQL-004 — BTreeActionGenerator deactivator detection and emission

## Test Results
[dotnet test output for Fbt.Tests.csproj]
[dotnet test output for Fdp.Toolkits.Tests.csproj]

## Files Changed

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond Spec
### Gaps Found in DESIGN.md
```
