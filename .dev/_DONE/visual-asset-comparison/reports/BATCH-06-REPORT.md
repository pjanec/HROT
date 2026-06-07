# BATCH-06 REPORT — Visualization Layer (Slice C-6: C-21 through C-28)

**Date:** 2026-05-29
**Status:** COMPLETED
**Build:** 0 errors, 0 warnings
**Tests:** 527 passed, 0 failed (was 478 before batch)

---

## Summary Table

| Task | Status | New Files | Tests Added |
|------|--------|-----------|-------------|
| C-22 | Done | `Comparison/Rendering/ComparisonStyleMap.cs` | 8 (ComparisonStyleMapTests.cs) |
| C-21 | Done | `Comparison/Rendering/ComparisonAnnotationRenderer.cs` | 6 (ComparisonAnnotationRendererTests.cs) |
| C-25 | Done | (extension to C-21 file) | 2 (added to ComparisonAnnotationRendererTests.cs) |
| C-23 | Done | `Comparison/UI/ComparisonSummaryPanel.cs` | 5 (ComparisonSummaryPanelTests.cs) |
| C-24 | Done | `Comparison/UI/ComparisonSidebar.cs` | 4 (ComparisonSidebarTests.cs) |
| C-26 | Done | `Comparison/BlackboardComparisonDecorator.cs` | 5 (BlackboardAuthoringWindowComparisonTests.cs) |
| C-27 | Done | `Comparison/UI/ExitComparisonAction.cs` | 4 (ExitComparisonActionTests.cs) |
| C-28 | Done | `Comparison/StaleBadgeWatcher.cs` | 3 (StaleBadgeWatcherTests.cs) |

**Total new tests:** 49 (from 478 to 527)

---

## Files Created or Modified

### New Source Files
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonStyleMap.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonAnnotationRenderer.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSummaryPanel.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSidebar.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ExitComparisonAction.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonDecorator.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/StaleBadgeWatcher.cs`

### Modified Source Files
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonToolbarAction.cs` — added Exit button and [STALE] chip
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — added `_sessionRegistry` field and comparison decoration section
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs` — added ComparisonSummaryPanel + ComparisonSidebar
- `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` — registered ComparisonSummaryPanel, ComparisonSidebar, StaleBadgeWatcher
- `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` — added NodeEditor.Core + NodeEditor.Primitives references
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — added NodeEditor.Core + NodeEditor.Primitives references

### New Test Files
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonStyleMapTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonAnnotationRendererTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonSummaryPanelTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonSidebarTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardAuthoringWindowComparisonTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ExitComparisonActionTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/StaleBadgeWatcherTests.cs`

---

## Test Counts

| Metric | Value |
|--------|-------|
| Tests before batch | 478 |
| Tests after batch | 527 |
| Tests added | 49 |
| Tests failed | 0 |
| Build errors | 0 |

---

## Developer Insights

### Q1: NodeId type and ElementId translation

`NodeId` is a `readonly record struct(Guid Value)` in `NodeEditor.Primitives`. Translation from `ComparisonChange.ElementId` (a string) is done via `Guid.TryParse(elementId, out var guid)` followed by `new NodeId(guid)`. If the string is not a valid GUID, the node lookup is skipped. This approach is clean but means that any non-GUID element ID format (e.g., human-readable names) will silently produce no annotation.

### Q2: variable_renamed badge -- how nodes were scanned

`INodeModel` exposes only `Title` and `Subtitle`. There is no property bag exposed through the interface. Scanning was done against `Title.Contains(oldVarName)` and `Subtitle?.Contains(oldVarName)` with `OrdinalIgnoreCase`. This is a Phase 1 heuristic -- it will produce false positives if a node's title coincidentally contains the variable name as a substring (e.g., variable "Ammo" would match node title "AmmoCount"). A richer scan using a typed property bag would require host-side extension to `INodeModel`.

### Q3: Severity filter toggle UX location

The severity filter checkbox loop is implemented entirely in `ComparisonSummaryPanel.DrawClientArea()`. The base class `ManagedWindow` provides no severity filter at the base level -- it only manages window open/pin state, rendering framing (ImGui.Begin/End), and focus requests. All filter logic is in the panel subclass.

### Q4: BlackboardAuthoringWindow integration point

The existing per-row rendering is fully abstracted behind `VariablesPanelControl.DrawSingle(section)`. There is no accessible per-row loop in `BlackboardAuthoringWindow` itself. The integration adds:
1. A stored `_sessionRegistry` field (previously the registry was only accessed inside `_comparisonToolbar`).
2. A post-table comparison annotation section rendered after `_variablesControl.DrawSingle(section)` that iterates `vm.Variables` and calls `BlackboardComparisonDecorator.GetDecoration()` for each, showing color-coded entries for added/removed/retyped/renamed variables.

This is less integrated than per-row coloring but achieves the same information display without requiring changes to the `VariablesPanelControl` internal architecture.

### Q5: Edge cases and limitations (suggested debt items)

1. **DEBT: ElementId substring matching for variable_renamed** — Scanning Title/Subtitle with `Contains()` may produce false positives for short variable names (e.g., "HP" matches any title containing "HP"). A typed property bag on `INodeModel` would allow exact-match scanning. **Priority: P3**

2. **DEBT: connection_changed ElementId format not validated** — The renderer assumes `"{guidA}->{guidB}"` format. If the LLM uses a different separator or format, the split will fail silently. A stricter parser with logging would be safer. **Priority: P3**

3. **DEBT: BlackboardAuthoringWindow per-row decoration requires VariablesPanelControl refactor** — The current integration shows decorations in a separate section below the variables table. True per-row coloring (as specified in §6.7) requires `VariablesPanelControl` to accept an optional decoration callback or the decorator to be integrated into `BTreeHsmSchemaSource`. **Priority: P2**

4. **DEBT: ComparisonAnnotationRenderer has no Dispose** — The interface provides a default no-op `Dispose`, but if the renderer is ever attached to a canvas and the session registry is swapped, the active asset GUID may become stale. Should add explicit cleanup. **Priority: P3**

5. **DEBT: NodeEditor.Core now a dependency of Hrot.Editor.AiShared** — Adding NodeEditor.Core to `Hrot.Editor.AiShared.csproj` creates a new dependency that didn't exist before. The project previously had no graph model dependency. If the renderer should be graph-model-agnostic, it should be moved to an extension project (e.g., `Hrot.Editor.AiShared.NodeEditor`) rather than the base shared project. **Priority: P2**

---

## Build and Test Output

```
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:38.99

dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug --no-build
Passed!  - Failed:     0, Passed:   527, Skipped:     0, Total:   527, Duration: 9 s
```
