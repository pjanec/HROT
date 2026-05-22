# BATCH-01 Report

## Tasks Completed

| Task | Status | Notes |
|------|--------|-------|
| TASK-P0-001 — Solution Scaffolding | ✅ | .sln + 6 .csproj files, Directory.Build.props, demo placeholder |
| TASK-P1-001 — Primitives | ✅ | All 12 files verbatim from kernel/00-primitives.md |
| TASK-P1-002 — Host Contract Interfaces | ✅ | All 17 interface files verbatim from kernel/01-interfaces.md |
| TASK-P1-003 — Commands and Undo Stack | ✅ | GraphCommand.cs, UndoStack.cs, CommandBuilder.cs verbatim |
| TASK-P1-004 — Fuzzy Matcher | ✅ | FuzzyMatcher.cs verbatim from kernel/03 |
| TASK-P1-005 — Expression Evaluator | ✅ | ExpressionEvaluator.cs verbatim from kernel/03 |
| TASK-P1-006 — Spatial Index | ✅ | SpatialIndex.cs verbatim from kernel/03 |
| TASK-P1-007 — Timing/Theme/Catalog Constants | ✅ | TimingConstants, DefaultTypeColors, DefaultTheme, CommandCatalog + kernel/04 interfaces |

## Test Results

```
Test summary: total: 43; failed: 0; succeeded: 43; skipped: 0; duration: 1,2s
Build succeeded with 1 warning(s) in 1,4s
```

(The 1 warning is from `NodeEditor.UI.Tests` having no test classes yet — not a code issue.)

### Test breakdown

| Test File | Count | Status |
|-----------|-------|--------|
| `Primitives/IdGeneratorTests.cs` | 3 facts | ✅ |
| `Commands/UndoStackTests.cs` | 4 facts | ✅ |
| `Search/FuzzyMatcherTests.cs` | 6 theory cases + 3 facts | ✅ |
| `Expression/ExpressionEvaluatorTests.cs` | 14 theory (success) + 6 theory (failure) | ✅ |
| `Spatial/SpatialIndexTests.cs` | 6 facts | ✅ |
| `Constants/DefaultTypeColorsTests.cs` | 1 fact (3 assertions) | ✅ |

## Developer Insights

### 1. Issues Encountered

**CS1591 / XML doc warnings as errors:**  
`TreatWarningsAsErrors=true` combined with `GenerateDocumentationFile=true` caused build failures because the kernel files don't include XML doc comments on every enum member (e.g. all 50+ `EditorKey` values, `PinId.Empty`, etc.). Resolution: added `<NoWarn>1591</NoWarn>` to `Directory.Build.props`. This is a design trade-off: either the kernel must document every enum value, or the project must suppress CS1591. Logged for review.

**`System.Action` shadowed by `NodeEditor.Core.Action` namespace:**  
Files inside `NodeEditor.Core.Action` namespace (IEditorIndicators, IEditorCommands) and files in sibling namespaces that use `Action` encountered CS0118 (`Action is a namespace, not a type`). C# name resolution finds `NodeEditor.Core.Action` before `System.Action` from global usings. Resolution: qualified the occurrences as `System.Action` and `System.Action<T>`. This is an expected pitfall when naming a namespace the same as a BCL delegate type.

### 2. Weak Points Spotted

- **`NodeEditor.Core.Action` namespace vs. `System.Action` collision** will recur every time a new interface in that namespace (or in `NodeEditor.Core.Interfaces` that can see the child namespace) uses `Action`. Future contributors must remember to fully qualify.

- **`FuzzyMatcher` fuzzy char-order test tolerance:** The test for `"ltp" / "VectorMultiply"` uses `expectedMin - 100` tolerance because the char-order scoring can be near 500 for short spread. This is acceptable but the tolerance makes the test less precise.

- **`UndoStack.TrimToMax` rebuild cost:** The current implementation rebuilds the stack by converting to array and back (`O(n)`) every time any push exceeds `maxEntries`. For typical UI (max 256 entries) this is negligible, but for large batched operations it could be a minor hotspot.

### 3. Design Decisions Beyond Spec

- Added `<NoWarn>1591</NoWarn>` to `Directory.Build.props` — was the minimal change to keep both `TreatWarningsAsErrors=true` and verbatim kernel copy intact.
- All `System.Action` / `System.Action<T>` qualified to resolve namespace shadow — minimal fixup, no API changes.

## Files Created

### Solution / Config
- `NodeEditor.sln`
- `Directory.Build.props`

### `src/NodeEditor.Primitives/`
- `NodeEditor.Primitives.csproj`
- `NodeId.cs`
- `PinId.cs`
- `LinkId.cs`
- `GraphId.cs`
- `CommentId.cs`
- `RerouteRef.cs`
- `TypeKey.cs`
- `NodeKindKey.cs`
- `Enums.cs`
- `EditorKey.cs`
- `IdGenerator.cs`
- `RectF.cs`

### `src/NodeEditor.Core/`
- `NodeEditor.Core.csproj`
- `TimingConstants.cs`
- `DefaultTypeColors.cs`
- `DefaultTheme.cs`
- `CommandCatalog.cs`
- `Commands/GraphCommand.cs`
- `Commands/UndoStack.cs`
- `Commands/CommandBuilder.cs`
- `Interfaces/IGraphModel.cs`
- `Interfaces/INodeModel.cs`
- `Interfaces/IPinModel.cs`
- `Interfaces/ILinkModel.cs`
- `Interfaces/ICommentModel.cs`
- `Interfaces/ILinkValidator.cs`
- `Interfaces/INodeCatalog.cs`
- `Interfaces/ITypeSystem.cs`
- `Interfaces/IGraphCommandSink.cs`
- `Interfaces/IEditorHostServices.cs`
- `Interfaces/IPickerRegistry.cs`
- `Interfaces/IClipboard.cs`
- `Interfaces/IIconProvider.cs`
- `Interfaces/IDiagnosticsSink.cs`
- `Interfaces/IDebugSession.cs`
- `Interfaces/IInputSource.cs`
- `Interfaces/IEditorTheme.cs`
- `Interfaces/IMyBlueprintModel.cs`
- `Interfaces/IEnumValueProvider.cs`
- `Interfaces/IGraphSearchProvider.cs`
- `Interfaces/IPinDefaultValueEditorRegistry.cs`
- `Interfaces/IDetailsViewProvider.cs`
- `Action/IEditorCommands.cs`
- `Action/IEditorIndicators.cs`
- `Search/FuzzyMatcher.cs`
- `Spatial/SpatialIndex.cs`
- `Expression/ExpressionEvaluator.cs`

### `src/NodeEditor.UI/`
- `NodeEditor.UI.csproj`

### `src/NodeEditor.Demo/`
- `NodeEditor.Demo.csproj`
- `Program.cs`

### `tests/NodeEditor.Core.Tests/`
- `NodeEditor.Core.Tests.csproj`
- `Primitives/IdGeneratorTests.cs`
- `Commands/UndoStackTests.cs`
- `Search/FuzzyMatcherTests.cs`
- `Expression/ExpressionEvaluatorTests.cs`
- `Spatial/SpatialIndexTests.cs`
- `Constants/DefaultTypeColorsTests.cs`

### `tests/NodeEditor.UI.Tests/`
- `NodeEditor.UI.Tests.csproj`
