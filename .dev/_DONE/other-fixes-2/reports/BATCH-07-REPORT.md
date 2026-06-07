# BATCH-07 Report: Bookkeeping, Test Quality & Debt Tracker Fixes

**Batch:** BATCH-07  
**Tasks:** FIX2-015, FIX2-016, FIX2-017, FIX2-018, FIX2-019, FIX2-020, FIX2-021  
**Date:** 2026-06-04

---

## Task Status

| Task | Status | Summary |
|------|--------|---------|
| FIX2-015 | DONE | DEBT-TRACKER updates + `GetNodeHistory` added to `IBlueprintDebugSession` |
| FIX2-016 | DONE | blueprints-2 DEBT-TRACKER D-03 and D-04 marked RESOLVED |
| FIX2-017 | DONE | `CgfNoOpTimeController.IsPausedByDebugger` wired to real `_bpManager`; breakpoints-1 DEBT-TRACKER updated |
| FIX2-018 | DONE | Roslyn `Microsoft.CodeAnalysis.CSharp` package added to BTree test project; complex-tree round-trip test added |
| FIX2-019 | DONE | Two-file partial-write test added to `AtomicMultiFileWriterTests` |
| FIX2-020 | DONE | `ChildOrderDeterminismTests` switched from stub to production `FakeContainerModel` |
| FIX2-021 | DONE | `UtilityAssetLoader.Load()` implemented; `EmitAndRoundTrip` test uses loader-based round-trip |

---

## FIX2-015: Debt Tracker Updates (blueprints-1) + GetNodeHistory Interface Member

### Success Condition (defined before coding)
`IBlueprintDebugSession` declares `GetNodeHistory(Entity, int)`. All implementors compile.
`DEBT-TRACKER.md` in `.dev/blueprints-1/` reflects current resolution status for DEBT-003, DEBT-004, DEBT-018, DEBT-021, DEBT-022, DEBT-023.

### What Was Done
- Added `// DEBT-018 (deferred)` comment before namespace in `IBlueprintDebugSession.cs`.
- Added `// DEBT-003` comment near `BreakpointId` declaration.
- Added `IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100);`
  to the `// -- Inspection --` section with a `// DEBT-022` comment.
- Added stub implementations to `MockDebugSession`, `SpyDebugSession` (in
  `DebugWindowDrawUITests.cs`), and `CapturingDebugSession` (missed initially; fixed
  when the build reported `CS0535`).
- Updated `.dev/blueprints-1/DEBT-TRACKER.md`:
  - DEBT-003 → RESOLVED
  - DEBT-004 → RESOLVED
  - DEBT-018 → DEFERRED (reason: no ImGui context available in test/editor layer)
  - DEBT-021 → RESOLVED
  - DEBT-022 → RESOLVED
  - DEBT-023 → RESOLVED

### Files Changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/MockDebugSession.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/CapturingDebugSession.cs`
- `.dev/blueprints-1/DEBT-TRACKER.md`

---

## FIX2-016: Debt Tracker Updates (blueprints-2)

### Success Condition (defined before coding)
`.dev/blueprints-2/DEBT-TRACKER.md` has D-03 and D-04 marked RESOLVED with file references.

### What Was Done
- D-03 → RESOLVED: `HsmEmitter.cs` lines 38-61, state-machine skeleton generation confirmed.
- D-04 → RESOLVED: `ActionNames` ordering aligned with canonical declaration order.
- Updated `.dev/blueprints-2/DEBT-TRACKER.md` accordingly.

### Files Changed
- `.dev/blueprints-2/DEBT-TRACKER.md`

---

## FIX2-017: CgfNoOpTimeController.IsPausedByDebugger Wired to Real Manager

### Success Condition (defined before coding)
`CgfNoOpTimeController.IsPausedByDebugger` returns `_bpManager.IsPaused` (not a constant
`false`), while preserving the fact that `_bpManager` is created after the controller.
`.dev/breakpoints-1/DEBT-TRACKER.md` updated.

### What Was Done
`CgfNoOpTimeController` previously had `public bool IsPausedByDebugger => false;`.
A back-reference pattern was used to avoid the circular construction order:

```csharp
private IDataBreakpointManager? _bpManager;
public bool IsPausedByDebugger => _bpManager?.IsPaused ?? false;
public void SetManager(IDataBreakpointManager manager) => _bpManager = manager;
```

After `_bpManager = new DataBreakpointManager(...)`, the call
`bpTimeAdapter.SetManager(_bpManager)` is added so the reference is wired at
construction time.

Updated `.dev/breakpoints-1/DEBT-TRACKER.md`:
- D-BP-01 → RESOLVED
- D-BP-02 → OPEN / deferred (no HrotNodeBuilder API to enumerate internal component
  registrations; requires future extension)
- D-BP-04 → OPEN / deferred (canvas placeholder not yet implemented; requires ImGui
  context)

### Files Changed
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- `.dev/breakpoints-1/DEBT-TRACKER.md`

---

## FIX2-018: Roslyn-Validated Round-Trip Test for BTreeFluentEmitter

### Success Condition (defined before coding)
`Hrot.BTree.Editor.Tests` has a test that emits a multi-level tree, parses the result
with `CSharpSyntaxTree.ParseText`, and asserts zero syntax diagnostics.
`Microsoft.CodeAnalysis.CSharp 4.8.0` is referenced in the test `.csproj`.

### What Was Done
- Added `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />`
  to `Hrot.BTree.Editor.Tests.csproj`.
- Added `using Microsoft.CodeAnalysis.CSharp;` to `BTreeFluentEmitterTests.cs`.
- Added new test class `BTreeFluentEmitterRoslynTests` with test
  `EmitComposite_ComplexTreeWithPill_ProducesValidCSharp` that:
  - Builds: Root → Sequence[with Inverter pill] → [Action1, Selector → [Action2, Action3]]
  - Emits via `BTreeFluentEmitter`
  - Parses with `CSharpSyntaxTree.ParseText`
  - Asserts `diagnostics.Length == 0` (no syntax errors)

### Files Changed
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeFluentEmitterTests.cs`

---

## FIX2-019: AtomicMultiFileWriter Partial-Write Test

### Success Condition (defined before coding)
A test confirms that when the second of two files fails to write, `result.Success ==
false`, the first file appears in `result.SuccessfullyWritten`, and the second does not.

### What Was Done
Added `Write_TwoFiles_FirstSucceeds_SecondFails_PartialSuccessfullyWritten` to
`AtomicMultiFileWriterTests.cs`. The test:
- Creates a temp directory via `Directory.CreateTempSubdirectory`.
- Uses `SortedDictionary<string, string>(StringComparer.Ordinal)` for deterministic
  iteration order so `a_output.txt` is always processed before `b_blockdir`.
- Pre-creates `b_blockdir` as a directory to force a write failure on the second path.
- Asserts `result.Success == false`, first path in `SuccessfullyWritten`, second path
  not in `SuccessfullyWritten`, and `result.FailureReason != null`.

### Files Changed
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/AtomicMultiFileWriterTests.cs`

---

## FIX2-020: ChildOrderDeterminismTests Uses Production FakeContainerModel

### Success Condition (defined before coding)
`ChildOrderDeterminismTests.cs` references `NodeEditor.Demo.FakeBlueprint.FakeContainerModel`
(from the demo project), the private stub inner class is removed, and all four tests still pass.

### What Was Done
- Added `<ProjectReference Include="..\..\src\NodeEditor.Demo\NodeEditor.Demo.csproj" />`
  to `NodeEditor.Core.Tests.csproj`.
- Removed `using NodeEditor.Core.Interfaces;` (no longer needed).
- Added `using NodeEditor.Demo.FakeBlueprint;`.
- Removed the private `FakeContainerModel` stub class (32 lines).
- Updated all 4 test constructors to use the production constructor:
  `new FakeContainerModel(IdGenerator.NewNodeId(), "Container", Vector2.Zero)`.

### Files Changed
- `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj`
- `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Serialization/ChildOrderDeterminismTests.cs`

---

## FIX2-021: UtilityAssetLoader.Load() Implementation + Round-Trip Test

### Success Condition (defined before coding)
`UtilityAssetLoader.Load()` parses `.Option(`, `.CandidateOption(`, and `.Consider(`
lines. `EmitAndRoundTrip_UtilityDecisionAsset_StructuralEquality` round-trips a two-
consideration asset through emit + file write + loader, asserting full structural
equality without AST re-sorting.

### What Was Done

**UtilityAssetLoader.cs** — added full parsing logic to `Load()`:
- State variable `OptionModel? currentOption` tracks the current option being parsed.
- `.Option(id, ScoringMode.XXX)` lines: `ParseOptionLine` extracts `OptionId` and
  `ScoringMode`; option is added to `asset.Options` and becomes `currentOption`.
- `.CandidateOption(ScoringMode.XXX)` lines: `ParseCandidateOptionLine`; option added
  but no `currentOption` update (candidate options are not top-level).
- `.Consider(In.InputName(context), weight, curveExpr)` lines: `ParseConsiderationLine`
  extracts `InputName`, `InputContext`, `Weight`, and `ResponseCurveModel`; appended to
  `currentOption.Considerations`.

Private helpers added:
- `ParseOptionLine` / `ParseCandidateOptionLine` / `ParseScoringMode`
- `ParseConsiderationLine` — depth-balanced paren walking to find In.XXX(args) context,
  then splits remaining args
- `SplitArgsAtDepthZero` — splits text on depth-0 commas, stopping at depth-0 `)`
- `StripOuterSuffix` — strips trailing `;` then excess closing parens
- `ParseCurveExpression` — handles `Curve.Linear`, `Curve.InverseLinear`, and all
  preset names, plus `new ResponseCurve(CurveKind.XXX, slope:, exponent:, xShift:)`
- `ParseLabeledCurveFloat` — extracts named float arguments

**UtilityFluentEmitterTests.cs** — replaced the Roslyn-AST-based
`EmitAndRoundTrip_UtilityDecisionAsset_StructuralEquality` body:
- Emits to a temp `.cs` file.
- Calls `UtilityAssetLoader.Load(tempPath)`.
- Asserts `loaded.Options.Count == 1`, `OptionId == 1`, `Mode == WeightedProduct`,
  `Considerations.Count == 2`.
- Asserts consideration[0] = HealthFraction / Self / 0.8f / InverseLinear
  (VisualId "aaa" sorts first).
- Asserts consideration[1] = ThreatRange / Target / 1.2f / Linear
  (VisualId "bbb" sorts second).
- Added `using System.IO;` and `using Hrot.Utility.Editor.Loading;`.

### Files Changed
- `Hrot/Editor/Hrot.Utility.Editor/Loading/UtilityAssetLoader.cs`
- `Hrot/Editor/Hrot.Utility.Editor.Tests/UtilityFluentEmitterTests.cs`

---

## Test Results

| Test Project | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| `Hrot.Blueprints.Tests` (no AllocationFree) | 894 | 886 | 0 | 8 |
| `Hrot.BTree.Editor.Tests` | 320 | 320 | 0 | 0 |
| `Hrot.Utility.Editor.Tests` | 142 | 142 | 0 | 0 |
| `NodeEditor.Core.Tests` | 181 | 181 | 0 | 0 |
| `Hrot.Editor.AiShared.Tests` | 545 | 545 | 0 | 0 |
| **Total** | **2082** | **2074** | **0** | **8** |

All 8 skipped tests were pre-existing Phase 3 / Phase 5 scope skips unrelated to this batch.

---

## Debt Tracker Changes

| File | Entry | Before | After |
|---|---|---|---|
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-003 | OPEN | RESOLVED |
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-004 | OPEN | RESOLVED |
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-018 | OPEN | DEFERRED |
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-021 | OPEN | RESOLVED |
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-022 | OPEN | RESOLVED |
| `.dev/blueprints-1/DEBT-TRACKER.md` | DEBT-023 | OPEN | RESOLVED |
| `.dev/blueprints-2/DEBT-TRACKER.md` | D-03 | OPEN | RESOLVED |
| `.dev/blueprints-2/DEBT-TRACKER.md` | D-04 | OPEN | RESOLVED |
| `.dev/breakpoints-1/DEBT-TRACKER.md` | D-BP-01 | OPEN | RESOLVED |
| `.dev/breakpoints-1/DEBT-TRACKER.md` | D-BP-02 | OPEN | DEFERRED |
| `.dev/breakpoints-1/DEBT-TRACKER.md` | D-BP-04 | OPEN | DEFERRED |

---

## Issues Encountered

1. **CapturingDebugSession missed** — initial implementation of FIX2-015 added
   `GetNodeHistory` to `MockDebugSession` and `SpyDebugSession` but missed
   `CapturingDebugSession`. Caught by the first build and fixed immediately.

2. **UtilityAssetLoader.cs corruption** — a large `replace_string_in_file` operation
   merged new and old code without proper closing braces, producing uncompilable output.
   Fixed by identifying the malformed section and replacing the corrupted tail (from the
   `.Consider` branch through `}`) with correctly structured code.
