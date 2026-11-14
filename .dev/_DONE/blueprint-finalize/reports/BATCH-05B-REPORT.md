# BATCH-05B Report — Math node-palette entries

## Implementation Summary

### Task 1 — `BlueprintMathPaletteEntries.cs`
New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/BlueprintMathPaletteEntries.cs`

Static `All()` yields **50 `NodeKindDescriptor`** instances, one per `BlueprintMath` public method.
Each descriptor:
- has a unique `Kind` in the form `"Math.<Method>"` (e.g. `"Math.AddInt"`, `"Math.Dot"`),
- has a friendly `DisplayName` (mirrors FakeNodeCatalog where applicable, e.g. `"Int + Int"`, `"Dot Product"`, `"Clamp (Float)"`),
- is assigned one of five categories from the nested `Categories` class,
- has `CreateInstance = () => new FunctionCallNode { Id = Guid.NewGuid(), TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath", MethodName = "<Method>", IsPure = true }`.

**Full function set covered (all 50 BlueprintMath methods):**

| Category | Kind | Method |
|---|---|---|
| Math | Math.Add … Math.Cos | Add, Subtract, Multiply, Divide, Modulo, Abs, Negate, Min, Max, Clamp, Lerp, Floor, Ceil, Round, Sqrt, Pow, Sin, Cos |
| Math/Int | Math.AddInt … Math.ClampInt | AddInt, SubInt, MulInt, DivInt, ModInt, AbsInt, NegateInt, MinInt, MaxInt, ClampInt |
| Math/Compare | Math.GreaterThan … Math.LessThanInt | GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, ApproxEquals, EqualsInt, GreaterThanInt, LessThanInt |
| Math/Bool | Math.And … Math.Xor | And, Or, Not, Xor |
| Math/Vector | Math.AddVec … Math.Distance | AddVec, SubVec, MulVecScalar, Dot, Cross, Normalize, Length, Distance |

Pattern: mirrors `BlueprintNodePaletteEntries.Make<TNode>` but uses a dedicated `MakeMath(kind, displayName, category, tooltip, methodName)` helper that creates `FunctionCallNode` instead of `new TNode()`.

### Task 2 — Registration
`BlueprintEditorBootstrap.CreatePaletteRegistry()` (line 72):
```csharp
// BATCH-05B: register BlueprintMath function-call presets (Math/* categories).
foreach (var descriptor in BlueprintMathPaletteEntries.All())
    registry.Register(descriptor);
```
Appended immediately after the `BlueprintNodePaletteEntries.All()` loop. Ordering is deterministic (declaration order in `All()`).

### Task 3 — Tests
New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintMathPaletteEntriesTests.cs`
38 headless tests covering:
- Descriptor collection shape (uniqueness, Kind prefix, non-empty DisplayName, valid category).
- `CreateInstance()` correctness: returns `FunctionCallNode` with correct `TargetTypeId`, `MethodName`, `IsPure = true`, fresh `Id` per call.
- Category assignments for 13 representative kinds (theory).
- Pin projection (`NodePinSchema.GetCanonicalPins`) for AddInt, Add (float), Clamp, Dot — verifying count, names, directions, CLR types.
- Palette registry contains all math kinds after bootstrap registration.
- `BlueprintNodeCatalog` includes all math kinds; no overwrite of built-in kinds.

## Design Decisions

1. **Dedicated `MakeMath` helper (not `Make<TNode>`)** — `Make<TNode>` requires a `where TNode : Node, new()` constraint and wires `CreateInstance = () => new TNode { Id = Guid.NewGuid() }`. Since all math descriptors share the same node type (`FunctionCallNode`) but differ in `MethodName`, a separate helper with an explicit `methodName` parameter is cleaner and avoids abuse of the generic factory.

2. **Separate `Categories` nested class** — Keeps the Math categories orthogonal to the existing `BlueprintNodePaletteEntries.Categories` constants (which cover `FlowControl`, `Variables`, etc.). The two constant sets deliberately do not overlap, matching how FakeNodeCatalog separates "Math/*" from "Flow Control" etc.

3. **`TargetTypeId` constant** — Stored as `private const string TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath"` to avoid repetition and provide a single point to update if the namespace ever changes.

4. **No `IrOp_PureCall` round-trip test** — The spec marked this as "optional". The existing Stage5 golden IR tests cover PureCall scheduling via `LibraryMath` and `MathUtils` assets; adding another that boots the full compiler pipeline against a hand-built asset would duplicate coverage already exercised by `GoldenIrTests.Schedule_ProducesExpectedIr("LibraryMath")`. The pin-projection tests prove the picker→node→pins chain, which is what BATCH-05B actually adds.

## Deviations

None. All 50 BlueprintMath methods are included. Category assignments match the FakeNodeCatalog pattern.

## Registration Site

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`
**Method:** `CreatePaletteRegistry()`, lines 69-77 (after `BlueprintNodePaletteEntries.All()` loop).

## Pin-Projection Proof

`NodePinSchema.GetCanonicalPins` exercises `FunctionCallPins → ResolveMethod` via reflection for each math node. Test results:

| Kind | Pins | Verified |
|---|---|---|
| Math.AddInt | 2 data-IN (`a:int`, `b:int`) + 1 data-OUT (`Return:int`), no exec | ✓ |
| Math.Add (float) | 2 data-IN (`a:float`, `b:float`) + 1 data-OUT (`Return:float`), no exec | ✓ |
| Math.Clamp | 3 data-IN (`value`, `min`, `max` all `System.Single`) + 1 data-OUT (`Return:float`), no exec | ✓ |
| Math.Dot | 2 data-IN (`a:Vector3`, `b:Vector3`) + 1 data-OUT (`Return:System.Single`), no exec | ✓ |

`ResolveMethod` first-match is unambiguous for all 50 methods because BlueprintMath has no overloads.

## Test Results

### New tests — `BlueprintMathPaletteEntriesTests`
```
Total tests: 38
     Passed: 38
 Total time: ~1.5 s
```

### Existing palette/catalog tests — `BlueprintNodeCatalogTests`
```
Total tests: 19
     Passed: 19
```

### Full `Hrot.Blueprints.Tests`
```
Failed:     7
Passed:  1357
Skipped:    8
Total:   1372
Duration:  ~30 s
```

**Failing tests (subset of pre-existing 7 — no new failures, no golden changed):**

| Test | Pre-existing? | Classification |
|---|---|---|
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` | Yes | Golden snapshot mismatch (unrelated to BATCH-05B) |
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` | Yes | Golden snapshot mismatch (unrelated) |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Yes | Golden snapshot mismatch (unrelated) |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Yes | Golden snapshot mismatch (unrelated) |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | Yes | Golden snapshot mismatch (unrelated) |
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Yes | Pre-existing assertion failure (unrelated) |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Yes | Allocation tracking (unrelated) |

Zero new failures.

### `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot`
```
Total tests: 10
     Passed: 10
 Total time: ~3.1 s
```

## Developer Insights

- `NodeKindRegistry.TryGet` looks up by `Kind` string only (not by node CLR type), so multiple descriptors for the same CLR type (here: all 50 FunctionCallNode variants) are fully supported — each has a distinct `Kind` key.
- The picker dispatch in `NodePinSchema.GetCanonicalPins` (Pass 1) checks the registry by `node.GetType().Name` ("FunctionCallNode") then falls to the built-in switch which calls `FunctionCallPinsDispatch`. Since the math nodes have no `TargetGraphId`, they always go through `FunctionCallPins → ResolveMethod`. The registry pass (Pass 1) returns early only if the matched descriptor's `CreateInstance().Pins` is non-empty — math descriptors return empty `Pins` on the blank `FunctionCallNode`, so the fallback to the built-in table always fires, which is correct and consistent with all other palette entries.
- 50 math entries increases the palette by ~130% (from ~38 to ~88 entries). No performance concern at this scale; all operations are O(n) over the palette list.

## Known Issues

None.

## Suggested Commit Message

```
feat(blueprint-palette): surface BlueprintMath functions as Math/* node-picker presets (BATCH-05B)
```
