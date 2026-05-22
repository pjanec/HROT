# T-02 — Primitives

## Goal
Implement all primitive types (IDs, enums, IdGenerator, RectF).

## Project
`NodeEditor.Primitives`

## References
- `../kernel/00-primitives.md` — full inlined code, copy verbatim.

## Deliverables

One file per type, exactly as in the kernel doc:
- `NodeId.cs`, `PinId.cs`, `LinkId.cs`, `GraphId.cs`, `CommentId.cs`
- `RerouteRef.cs`
- `TypeKey.cs`, `NodeKindKey.cs`
- `Enums.cs` (multiple enums)
- `EditorKey.cs`
- `IdGenerator.cs`
- `RectF.cs`

## Implementation
Verbatim from the kernel file. No logic decisions needed.

## Acceptance
- All files compile.
- Add one smoke test in `NodeEditor.Core.Tests/Primitives/IdGeneratorTests.cs`:

```csharp
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Primitives;

public class IdGeneratorTests
{
    [Fact]
    public void Deterministic_SameInput_ReturnsSameGuid()
    {
        var a = IdGenerator.Deterministic("hello");
        var b = IdGenerator.Deterministic("hello");
        a.Should().Be(b);
    }

    [Fact]
    public void Deterministic_DifferentInput_ReturnsDifferentGuid()
    {
        var a = IdGenerator.Deterministic("hello");
        var b = IdGenerator.Deterministic("world");
        a.Should().NotBe(b);
    }

    [Fact]
    public void NewNodeId_ReturnsNonEmpty()
    {
        var id = IdGenerator.NewNodeId();
        id.Should().NotBe(NodeId.Empty);
    }
}
```

## Status
(fill in when complete)

---

# T-03 — Host Contract Interfaces

## Goal
Implement all host contract interfaces and their supporting record types.

## Project
`NodeEditor.Core`

## References
- `../kernel/01-interfaces.md` — full inlined code, copy verbatim.
- `../instructions/01-spec-brief.md` §3 for context.

## Deliverables

Files under `NodeEditor.Core/Interfaces/`:
- `IGraphModel.cs` (+ GraphKindDescriptor, GraphChangeNotification, GraphChangeKind)
- `INodeModel.cs`
- `IPinModel.cs` (+ IPinDefaultValue, PinDefaultMetadata)
- `ILinkModel.cs` (+ LinkStyle)
- `ICommentModel.cs`
- `ILinkValidator.cs` (+ LinkValidationResult, LinkValidity)
- `INodeCatalog.cs` (+ NodeCatalogEntry, PinSignature, etc.)
- `ITypeSystem.cs` (+ TypeDisplayInfo, IPinDefaultValueEditor, DefaultEditorContext)
- `IGraphCommandSink.cs` (+ GraphCommandResult)
- `IEditorHostServices.cs`
- `IPickerRegistry.cs` (+ IPickerSource, PickerLayout, PickerSelectionMode, QueryCost, IPickerRenderContext)
- `IClipboard.cs`
- `IIconProvider.cs` (+ IconHandle)
- `IDiagnosticsSink.cs` (+ DiagnosticSeverity)
- `IDebugSession.cs`
- `IInputSource.cs`
- `IEditorTheme.cs`

## Implementation
Verbatim from kernel file.

## Acceptance
- Compiles.
- No tests needed in this task (interfaces).

## Status
(fill in when complete)

---

# T-04 — Commands and Undo

## Goal
Implement the GraphCommand hierarchy, UndoStack, and CommandBuilder.

## Project
`NodeEditor.Core`

## References
- `../kernel/02-commands-and-undo.md` — full inlined code.
- `../instructions/01-spec-brief.md` §5.

## Deliverables

Files under `NodeEditor.Core/Commands/`:
- `GraphCommand.cs` (use the **revised form** with `AssignedId` fields)
- `UndoStack.cs`
- `CommandBuilder.cs`

## Implementation
Copy verbatim. Use the revised `AddNode` / `AddLink` / `AddComment` forms
with explicit `AssignedId` parameters. The plain forms in the spec are
illustrative; revised forms are authoritative.

## Acceptance
- Compiles.
- Add `NodeEditor.Core.Tests/Commands/UndoStackTests.cs` with these scenarios:
  - Apply → Undo restores state via a fake sink.
  - Undo then Redo re-applies.
  - Clear empties both stacks.
  - Max-entries trimming preserves most-recent entries.

Sketch of fake sink for tests:
```csharp
private sealed class FakeSink : IGraphCommandSink
{
    public List<GraphCommand> Log { get; } = new();
    public bool NextFails { get; set; }
    public GraphCommandResult Apply(GraphCommand command)
    {
        if (NextFails) return new GraphCommandResult(false, "forced");
        Log.Add(command);
        return new GraphCommandResult(true, null);
    }
}
```

## Status
(fill in when complete)

---

# T-05 — Fuzzy Matcher

## Goal
Implement the fuzzy matcher used by picker and My Blueprint search.

## Project
`NodeEditor.Core`

## References
- `../kernel/03-search-spatial-constants.md` (FuzzyMatcher section)
- `../specs/C-picker.md` §C.6 for tiered scoring rationale.

## Deliverables

- `NodeEditor.Core/Search/FuzzyMatcher.cs` (verbatim from kernel doc)
- `NodeEditor.Core.Tests/Search/FuzzyMatcherTests.cs`

## Tests to write

```csharp
[Theory]
[InlineData("",      "Multiply", 1)]        // empty query: match-all
[InlineData("mult",  "Multiply", 5000)]     // prefix
[InlineData("vm",    "VectorMultiply", 2500)] // camelCase
[InlineData("vec",   "VectorMultiply", 5000)] // prefix
[InlineData("ltp",   "VectorMultiply", 500)]  // fuzzy char-order
[InlineData("xyz",   "VectorMultiply", 0)]    // no match
public void Score_TierBehavior(string query, string candidate, int expectedMin) {
    var r = FuzzyMatcher.Score(query, candidate);
    if (expectedMin == 0) r.HasMatch.Should().BeFalse();
    else r.Score.Should().BeGreaterThanOrEqualTo(expectedMin - 100); // tolerance
}

[Fact]
public void ExactMatch_BeatsAllOthers() {
    var exact   = FuzzyMatcher.Score("multiply", "multiply");
    var prefix  = FuzzyMatcher.Score("multiply", "multiplyVector");
    exact.Score.Should().BeGreaterThan(prefix.Score);
}

[Fact]
public void Prefix_BeatsSubstring() {
    var prefix    = FuzzyMatcher.Score("mult", "multiply");
    var substring = FuzzyMatcher.Score("mult", "submultiplex");
    prefix.Score.Should().BeGreaterThan(substring.Score);
}

[Fact]
public void Keywords_ProvideMatch() {
    var noKey = FuzzyMatcher.Score("multiply", "Mul");
    var withKey = FuzzyMatcher.Score("multiply", "Mul", new[]{"multiply"});
    withKey.Score.Should().BeGreaterThan(noKey.Score);
}
```

## Acceptance
All tests green. Behavior matches spec tier order.

## Status
(fill in when complete)

---

# T-06 — Expression Evaluator

## Goal
Implement the safe whitelist expression evaluator for inline drag-float
text-edit.

## Project
`NodeEditor.Core`

## References
- `../kernel/03-search-spatial-constants.md` (ExpressionEvaluator section)
- `../specs/B-mini-editors.md` §B.6 for grammar.

## Deliverables

- `NodeEditor.Core/Expression/ExpressionEvaluator.cs` (verbatim)
- `NodeEditor.Core.Tests/Expression/ExpressionEvaluatorTests.cs`

## Tests to write

```csharp
[Theory]
[InlineData("1+1", 2.0)]
[InlineData("2*pi", Math.PI * 2)]
[InlineData("1/60", 1.0/60)]
[InlineData("45 deg", Math.PI/4)]
[InlineData("sin(pi/2)", 1.0)]
[InlineData("clamp(5, 0, 1)", 1.0)]
[InlineData("clamp(-5, 0, 1)", 0.0)]
[InlineData("1.5e-3", 0.0015)]
[InlineData("(1+2)*3", 9.0)]
[InlineData("2^3", 8.0)]
[InlineData("-5", -5.0)]
[InlineData("abs(-3.5)", 3.5)]
[InlineData("min(2,3)", 2.0)]
[InlineData("max(2,3)", 3.0)]
public void Eval_Success(string expr, double expected) {
    var r = ExpressionEvaluator.Evaluate(expr);
    r.Success.Should().BeTrue($"expr='{expr}' err='{r.Error}'");
    r.Value.Should().BeApproximately(expected, 1e-9);
}

[Theory]
[InlineData("")]
[InlineData("1+")]
[InlineData("(1")]
[InlineData("xyz")]
[InlineData("clamp(1)")]
[InlineData("System.IO.File")]
public void Eval_Failure(string expr) {
    var r = ExpressionEvaluator.Evaluate(expr);
    r.Success.Should().BeFalse();
}
```

## Acceptance
All tests green.

## Status

---

# T-07 — Spatial Index

## Goal
Implement the uniform-grid spatial index.

## Project
`NodeEditor.Core`

## References
- `../kernel/03-search-spatial-constants.md` (SpatialIndex section)

## Deliverables

- `NodeEditor.Core/Spatial/SpatialIndex.cs` (verbatim)
- `NodeEditor.Core.Tests/Spatial/SpatialIndexTests.cs`

## Tests to write

```csharp
[Fact]
public void Insert_Then_QueryPoint_FindsNode() {
    var idx = new SpatialIndex();
    var id = NodeId.NewId();
    idx.Insert(id, new RectF(new Vector2(10, 10), new Vector2(50, 50)));
    idx.QueryPoint(new Vector2(30, 30)).Should().Contain(id);
}

[Fact]
public void Insert_Then_QueryPoint_OutsideMisses() {
    var idx = new SpatialIndex();
    var id = NodeId.NewId();
    idx.Insert(id, new RectF(new Vector2(10, 10), new Vector2(50, 50)));
    idx.QueryPoint(new Vector2(100, 100)).Should().BeEmpty();
}

[Fact]
public void Query_Area_FindsIntersecting() {
    var idx = new SpatialIndex();
    var id1 = NodeId.NewId();
    var id2 = NodeId.NewId();
    idx.Insert(id1, new RectF(new Vector2(0, 0), new Vector2(100, 100)));
    idx.Insert(id2, new RectF(new Vector2(200, 200), new Vector2(50, 50)));

    var found = idx.Query(new RectF(new Vector2(50, 50), new Vector2(100, 100))).ToList();
    found.Should().Contain(id1);
    found.Should().NotContain(id2);
}

[Fact]
public void QueryFullyEnclosed_ExcludesPartial() {
    var idx = new SpatialIndex();
    var id = NodeId.NewId();
    idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(100, 100)));

    var fully = idx.QueryFullyEnclosed(new RectF(new Vector2(-10, -10), new Vector2(200, 200)));
    fully.Should().Contain(id);

    var partial = idx.QueryFullyEnclosed(new RectF(new Vector2(50, 50), new Vector2(100, 100)));
    partial.Should().NotContain(id);
}

[Fact]
public void Remove_Works() {
    var idx = new SpatialIndex();
    var id = NodeId.NewId();
    idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(50, 50)));
    idx.Remove(id).Should().BeTrue();
    idx.QueryPoint(new Vector2(25, 25)).Should().BeEmpty();
}

[Fact]
public void Insert_LargeRect_CoversManyCells() {
    var idx = new SpatialIndex(cellSize: 100);
    var id = NodeId.NewId();
    idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(500, 500)));

    // Point within the big rect, far corner.
    idx.QueryPoint(new Vector2(450, 450)).Should().Contain(id);
}
```

## Acceptance
All tests green.

## Status

---

# T-08 — Timing / Theme / Catalog Constants

## Goal
Drop in the constant tables.

## Project
`NodeEditor.Core`

## References
- `../kernel/03-search-spatial-constants.md` (TimingConstants, DefaultTypeColors, DefaultTheme)
- `../kernel/04-my-blueprint-and-rest.md` (IMyBlueprintModel, IEnumValueProvider, IGraphSearchProvider, IPinDefaultValueEditorRegistry, IDetailsViewProvider)
- Also `../kernel/03-search-spatial-constants.md` for `CommandCatalog` constants.

## Deliverables

- `NodeEditor.Core/TimingConstants.cs`
- `NodeEditor.Core/DefaultTypeColors.cs`
- `NodeEditor.Core/DefaultTheme.cs`
- `NodeEditor.Core/CommandCatalog.cs`
- `NodeEditor.Core/Interfaces/IMyBlueprintModel.cs`
- `NodeEditor.Core/Interfaces/IEnumValueProvider.cs`
- `NodeEditor.Core/Interfaces/IGraphSearchProvider.cs`
- `NodeEditor.Core/Interfaces/IPinDefaultValueEditorRegistry.cs`
- `NodeEditor.Core/Interfaces/IDetailsViewProvider.cs`
- `NodeEditor.Core/Action/IEditorCommands.cs` (+ supporting records)
- `NodeEditor.Core/Action/IEditorIndicators.cs` (+ supporting records)

All verbatim from the kernel files.

## Acceptance
- Compiles.
- One sanity test verifying `DefaultTypeColors.GetColor("System.Single")` returns expected color, `DefaultTypeColors.ExecColor` is white, and an unknown key returns the fallback.

## Status
