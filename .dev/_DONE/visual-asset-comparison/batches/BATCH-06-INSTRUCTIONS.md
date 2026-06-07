# BATCH-06: Visualization Layer (Slice C-6: C-21 through C-28)

**Batch Number:** BATCH-06
**Tasks:** TASK-C-22, TASK-C-21, TASK-C-23, TASK-C-24, TASK-C-25, TASK-C-26, TASK-C-27, TASK-C-28
**Slice:** C-6 (canvas annotations, summary panel, sidebar, exit mode, stale badge)
**Estimated Effort:** 20-26 hours
**Priority:** HIGH
**Dependencies:** BATCH-04 (C-16, C-17 done) + BATCH-05 (C-19 done)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design §6.1–6.9:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
   - §6.2 session state model
   - §6.3 summary panel + severity filter
   - §6.4 annotation renderer (colors, badges, dashed stroke)
   - §6.5 removed node sidebar-only rule
   - §6.6 sidebar layout
   - §6.7 Blackboard panel
   - §6.8 exit mode
   - §6.9 stale badge
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-21 through C-28
4. **Existing canvas renderer patterns:**
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/VariableBindingBadgeRenderer.cs`
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/HeatmapOverlayRenderer.cs`
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Renderers/VariableBindingBadgeRendererTests.cs`
   - `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Canvas/CustomRendererPassTests.cs`
5. **ICustomCanvasRenderer interface:**
   - `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomCanvasRenderer.cs`
6. **Existing panel pattern:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` (ManagedWindow pattern)
7. **Blackboard panel to modify:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
8. **ComparisonSessionState and registry:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs`

### Test Execution

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit to: `.dev\visual-asset-comparison\reports\BATCH-06-REPORT.md`

---

## KEY DESIGN CONSTRAINT: Rendering vs Logic Layer Tests

The `ImDrawListPtr` in `ICanvasRenderContext.DrawList` is a native pointer that is `default` (null) in unit test contexts. You CANNOT verify actual draw calls in unit tests.

Instead, use one of these two patterns:
1. **Record-then-draw pattern (RECOMMENDED):** The renderer accumulates a list of `AnnotationRecord` objects (what would be drawn, with node ID, severity, kind, position) in `_lastFrameAnnotations` before drawing. Tests inspect this list. The drawing code reads from the list and calls `DrawList.*` only after the list is built. Guard all draw calls with `if (ctx.DrawList.NativePtr == nint.Zero) return;`.
2. **Test-visible counter pattern:** The renderer exposes `internal int LastFrameAnnotationCount` and `internal int LastFrameSkippedCount`. Tests count instead of verify content.

Use **pattern 1** for C-21 since it is richer and reused by C-22's style map tests.

---

## Tasks

---

### TASK-C-22 — `ComparisonStyleMap`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-22`
**Design refs:** §6.4

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonStyleMap.cs`

**Implement a static class with two lookup methods:**

```csharp
public static class ComparisonStyleMap
{
    // Returns the RGBA color for a given severity string (case-insensitive).
    // Unknown severities return a neutral gray.
    public static Vector4 ColorForSeverity(string severity);

    // Returns the glyph string for a given kind string (case-insensitive).
    // Unknown kinds return "?".
    public static string GlyphForKind(string kind);
}
```

**Severity → color mapping (from §6.4):**

| Severity | Color |
|---|---|
| cosmetic | `new Vector4(0.5f, 0.5f, 0.5f, 0.6f)` — gray 60% |
| tuning | `new Vector4(0.3f, 0.5f, 1.0f, 1.0f)` — blue |
| feature | `new Vector4(0.2f, 0.8f, 0.2f, 1.0f)` — green |
| removal | `new Vector4(0.9f, 0.2f, 0.2f, 1.0f)` — red |
| behavior | `new Vector4(1.0f, 0.55f, 0.1f, 1.0f)` — orange |

Note: `intent_shift` uses the same color as `behavior` (orange).

**Kind → glyph mapping (from §6.4):**

| Kind | Glyph |
|---|---|
| node_added | "+" |
| node_removed | "-" |
| node_modified | "~" |
| variable_added | "+v" |
| variable_removed | "-v" |
| variable_renamed | ">>>" |
| variable_retyped | "[]" |
| connection_changed | "~>" |
| comment_changed | "\"" |
| intent_shift | "!!" |

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonStyleMapTests.cs`):**
- One `[Theory]` test with all 5 severity entries asserting exact RGBA values.
- One `[Theory]` test with all 10 kind entries asserting exact glyph strings.
- `ColorForSeverity("UNKNOWN")` returns gray (neutral fallback).
- `GlyphForKind("unknown_thing")` returns `"?"`.
- Case-insensitivity: `ColorForSeverity("BEHAVIOR")` == `ColorForSeverity("behavior")`.

---

### TASK-C-21 — `ComparisonAnnotationRenderer`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-21`
**Design refs:** §6.4

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonAnnotationRenderer.cs`

**Implement `ICustomCanvasRenderer`:**

```csharp
public sealed class ComparisonAnnotationRenderer : ICustomCanvasRenderer
{
    public string Id => "comparison.annotations";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive => _sessionRegistry.GetSession(_assetId) != null;

    private readonly ComparisonSessionRegistry _sessionRegistry;
    private Guid _assetId;

    // Test-visible: records built during the last Render() call.
    internal IReadOnlyList<AnnotationRecord> LastFrameAnnotations => _lastFrameAnnotations;
    private readonly List<AnnotationRecord> _lastFrameAnnotations = new();

    public void SetActiveAsset(Guid assetId) { _assetId = assetId; }

    public void Render(ICanvasRenderContext ctx) { ... }
}

// A single annotation drawn for one change on one node.
public sealed record AnnotationRecord(
    string ElementId,
    string Severity,
    string Kind,
    string Glyph,
    System.Numerics.Vector4 Color,
    AnnotationPlacement Placement);

public enum AnnotationPlacement { NodeBadge, EdgeMidpoint, SurvivingEndpoint }
```

**Render algorithm:**
1. Get `ComparisonSessionState` from registry for the active asset; if null, clear `_lastFrameAnnotations` and return.
2. Clear `_lastFrameAnnotations`.
3. For each `ComparisonChange` in `Response.Changes`:
   - Skip if severity not in `EnabledSeverities`.
   - If `Kind == "connection_changed"`:
     - Determine endpoints from `ElementId` (split on `->` if present, or treat as single ID).
     - Apply `connection_changed` fallback: both found → `EdgeMidpoint`, one found → `SurvivingEndpoint`, none found → skip.
   - Else if `ElementId` is non-null:
     - Look up the node via `ctx.Graph.FindNode(...)`. If not found, skip.
     - Placement = `NodeBadge`.
   - Else: skip (no element to annotate).
   - Build an `AnnotationRecord` and add to `_lastFrameAnnotations`.
4. Guard: `if (ctx.DrawList.NativePtr == nint.Zero) return;` — early exit from drawing if no ImGui frame.
5. For each record in `_lastFrameAnnotations`, draw:
   - A dashed 2px outline 3px outside the node bounding box using `ctx.DrawList.AddRect(...)`.
   - A badge glyph using `ctx.DrawList.AddText(...)` at the upper-right corner.

**ElementId → NodeId translation:** The renderer must convert `ElementId` strings to `NodeId`. Since `NodeId` is a struct wrapping a GUID, try `Guid.TryParse(elementId, out var guid)`. If parse fails, skip.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonAnnotationRendererTests.cs`):**

Use the `FakeRenderContext` pattern from `CustomRendererPassTests.cs`. Create a fake graph with specific nodes.

- **Severity filter applied:** Session has cosmetic disabled. Response has one `cosmetic` change and one `behavior` change with valid ElementIds on the fake graph. After `Render()`, `LastFrameAnnotations.Count == 1` (only the behavior change).
- **Missing node skipped:** Change has `ElementId = "nonexistent-guid"` (valid GUID not in graph). After `Render()`, `LastFrameAnnotations` is empty.
- **connection_changed both endpoints exist:** Response has `connection_changed` with `ElementId = "{guidA}->{guidB}"` and both GUIDs are nodes in the fake graph. `LastFrameAnnotations[0].Placement == AnnotationPlacement.EdgeMidpoint`.
- **connection_changed one endpoint missing:** Only guidA is in the fake graph. `LastFrameAnnotations[0].Placement == AnnotationPlacement.SurvivingEndpoint`.
- **connection_changed neither endpoint:** Neither GUID in graph. `LastFrameAnnotations` is empty.
- **Null session — IsActive false:** No session set for the asset ID. `IsActive == false`.

---

### TASK-C-25 — Variable-Binding Badges

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-25`
**Design refs:** §6.4, §6.7

Extend `ComparisonAnnotationRenderer` (in the same file) to handle `variable_renamed` changes:

**Additional behavior in step 3 (above):**
- If `Kind == "variable_renamed"`:
  - Scan `ctx.Graph.Nodes` for nodes whose property values reference `OldValue` (the old variable name).
  - For each matching node, create an `AnnotationRecord` with kind `"variable_renamed"`, glyph `">>>"`, placement `NodeBadge`.

**Tests (extend `ComparisonAnnotationRendererTests.cs`):**

- **variable_renamed badge on matching nodes:** Fake graph has two nodes with a property value equal to `"AmmoCount"`. Response has `variable_renamed` with `OldValue="AmmoCount"`. After `Render()`, `LastFrameAnnotations.Count == 2` (one per node that references the variable).
- **variable_renamed no badge on non-matching nodes:** Same setup, but a third node with property value `"SomeOtherVar"`. Only the two AmmoCount nodes get badges.

**INodeModel property access:** Use `ctx.Graph.Nodes` and check node properties via a string scan of node display data. Since `INodeModel` has `Title` and `Subtitle`, check those. If the node model exposes a richer `Properties` bag (check the interface), scan that. If not, scanning `Title + Subtitle` is sufficient for Phase 1.

---

### TASK-C-23 — `ComparisonSummaryPanel`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-23`
**Design refs:** §6.3

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSummaryPanel.cs`

**Panel state model:**

```csharp
public sealed class ComparisonSummaryPanelState
{
    private readonly ComparisonSessionState _session;

    public string AssetName { get; }
    public bool HasMigrationNotice => _session.MigrationNotice != null;
    public string? MigrationNotice => _session.MigrationNotice;
    public string TopSummary => _session.Response.TopLevelSummary;
    public string? HumanSummary => _session.Response.HumanSummary;
    public IReadOnlySet<string> EnabledSeverities => _session.EnabledSeverities;

    public ComparisonSummaryPanelState(ComparisonSessionState session, string assetName)
    {
        _session = session;
        AssetName = assetName;
    }

    public void ToggleSeverity(string severity) => _session.ToggleSeverity(severity);
}
```

**ImGui panel class (extends existing `ManagedWindow` pattern):**

```csharp
public sealed class ComparisonSummaryPanel : ManagedWindow
{
    private readonly ComparisonSessionRegistry _registry;
    private Guid _activeAssetId;
    private string _activeAssetName = "";

    public ComparisonSummaryPanel(ComparisonSessionRegistry registry)
        : base("ai_comparison_summary", "Comparison Summary", "Analysis", WindowScope.PerspectiveBound)
    { ... }

    public void SetActiveAsset(Guid assetId, string assetName) { ... }

    protected override void DrawClientArea() { ... }
}
```

Register `ComparisonSummaryPanel` as a singleton in `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()` and `SharedAiWindowRegistrar.RegisterWindows()`.

**UI layout (from §6.3):**
1. `ImGui.Text(state.AssetName)` — title
2. If `HasMigrationNotice`: `ImGui.TextColored(yellow, "Migration: " + notice)`
3. `ImGui.TextWrapped(state.TopSummary)` — one-sentence summary
4. `ImGui.Separator()`
5. Scrollable region with `ImGui.TextWrapped(state.HumanSummary)` — full prose
6. `ImGui.Separator()`
7. Severity filter checkboxes — one per severity ("cosmetic", "tuning", "feature", "removal", "behavior"). Each is a `bool` driven by `EnabledSeverities.Contains(severity)`. Toggle calls `state.ToggleSeverity(severity)`.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonSummaryPanelTests.cs`):**

Test the state model directly:

- **AssetName shows session asset name:** Create a state with a known session, assert `AssetName` is correct.
- **HasMigrationNotice false when no notice:** Session has null MigrationNotice. `HasMigrationNotice == false`.
- **HasMigrationNotice true when notice present:** Session has non-null MigrationNotice. `HasMigrationNotice == true`, `MigrationNotice` equals the expected string.
- **TopSummary returns response TopLevelSummary:** Assert exact match.
- **ToggleSeverity delegates to session:** Call `ToggleSeverity("cosmetic")`. Session's `EnabledSeverities` now contains "cosmetic".

---

### TASK-C-24 — `ComparisonSidebar`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-24`
**Design refs:** §6.6

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSidebar.cs`

**Sidebar state model:**

```csharp
public sealed class ComparisonSidebarState
{
    private readonly ComparisonSessionState _session;
    private Action<string>? _onFocusNode;

    public IReadOnlyList<ComparisonChange> VisibleChanges =>
        _session.Response.Changes
            .Where(c => _session.EnabledSeverities.Contains(c.Severity))
            .ToList();

    public ComparisonSidebarState(ComparisonSessionState session, Action<string>? onFocusNode = null)
    {
        _session = session;
        _onFocusNode = onFocusNode;
    }

    // Called when user clicks a change entry.
    public void FocusChange(ComparisonChange change)
    {
        if (change.ElementId != null)
            _onFocusNode?.Invoke(change.ElementId);
    }
}
```

**ImGui panel class (extends `ManagedWindow`):**

```csharp
public sealed class ComparisonSidebar : ManagedWindow
{
    private readonly ComparisonSessionRegistry _registry;
    private Guid _activeAssetId;

    public ComparisonSidebar(ComparisonSessionRegistry registry)
        : base("ai_comparison_sidebar", "Comparison Changes", "Analysis", WindowScope.PerspectiveBound)
    { ... }

    protected override void DrawClientArea() { ... }
}
```

Register in DI and `SharedAiWindowRegistrar`.

**UI layout (from §6.6):**
1. No session → `ImGui.TextDisabled("No comparison active.")`
2. For each change in `VisibleChanges`:
   - `ImGui.Text("[{glyph}] {elementDescription}")` — glyph from `ComparisonStyleMap.GlyphForKind`
   - `ImGui.TextColored(ComparisonStyleMap.ColorForSeverity(severity), "{severity}")` on same row
   - `ImGui.TextWrapped(description)` — indented detail
   - Click on the row: call `FocusChange(change)` — which invokes the canvas-focus callback

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonSidebarTests.cs`):**

Test the state model:

- **VisibleChanges filters by enabled severities:** Session has behavior enabled, cosmetic disabled. Response has 2 behavior + 1 cosmetic change. `VisibleChanges.Count == 2`.
- **Severity toggle updates VisibleChanges:** Toggle cosmetic on → `VisibleChanges.Count == 3`. Toggle cosmetic off again → `VisibleChanges.Count == 2`.
- **FocusChange with non-null elementId invokes callback:** Click a change with ElementId "abc". Callback receives "abc".
- **FocusChange with null elementId does not invoke callback:** `intent_shift` change with null ElementId. No exception, callback NOT invoked.

---

### TASK-C-26 — Blackboard Variables Panel Integration

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-26`
**Design refs:** §6.7

**File to modify:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`

**Scope:** When the `ComparisonSessionRegistry` has an active session for the current blackboard asset, decorate variable rows with severity outlines and kind badges.

**Changes to `BlackboardAuthoringWindow`:**
1. In the table row rendering loop, check if there is a session for the current asset.
2. For each variable (field), find matching `ComparisonChange` entries where `ElementId == fieldName` (or `ElementDescription` contains the field name).
3. Apply decorations:
   - `variable_added`: label text colored green + "+" prefix
   - `variable_removed`: append "(removed)" in red at end of row
   - `variable_retyped`: type column colored blue
   - `variable_renamed`: field name column colored yellow + `>>>` suffix

**Important:** The `BlackboardAuthoringWindow` already has `ComparisonSessionRegistry` injected (from BATCH-05). Use it directly.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/BlackboardAuthoringWindowComparisonTests.cs`):**

Test via the state model (extract a `BlackboardComparisonDecorator` helper class to avoid testing ImGui):

```csharp
public static class BlackboardComparisonDecorator
{
    // Returns decoration info for a given field name from the active session.
    public static FieldDecoration GetDecoration(
        string fieldName, ComparisonSessionState? session);
}

public sealed record FieldDecoration(
    bool IsAdded,    // variable_added
    bool IsRemoved,  // variable_removed
    bool IsRetyped,  // variable_retyped
    bool IsRenamed,  // variable_renamed
    string? OldName, // for renamed: the old name
    string? NewType  // for retyped: the new type
);
```

Tests:
- **variable_added:** Session has `variable_added` for "AmmoCount". `GetDecoration("AmmoCount", session).IsAdded == true`.
- **variable_removed:** Session has `variable_removed` for "OldField". `IsRemoved == true`.
- **variable_retyped with new type:** Session has `variable_retyped` for "Health", `NewValue = "float"`. `IsRetyped == true`, `NewType == "float"`.
- **variable_renamed with old name:** Session has `variable_renamed` where `ElementId = "BurstShotsRemaining"`, `OldValue = "AmmoCount"`. `GetDecoration("BurstShotsRemaining", session).IsRenamed == true`, `OldName == "AmmoCount"`.
- **No session:** `GetDecoration("anything", null)` returns `FieldDecoration(false, false, false, false, null, null)`.

---

### TASK-C-27 — Exit Comparison Mode

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-27`
**Design refs:** §6.8

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ExitComparisonAction.cs`

**Simple service:**

```csharp
public sealed class ExitComparisonAction
{
    private readonly ComparisonSessionRegistry _registry;

    public ExitComparisonAction(ComparisonSessionRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    // Clears the comparison session for the given asset.
    // After this call, the annotation renderer's IsActive is false,
    // and all panels show their "no session" state.
    public void Exit(Guid assetId) => _registry.ClearSession(assetId);
}
```

**Toolbar wiring:** Extend `ComparisonToolbarAction.Render()` to show a "Exit Comparison" button when a session is active for the current asset. When clicked, calls `ExitComparisonAction.Exit(activeAssetId)`.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ExitComparisonActionTests.cs`):**

- **Exit clears session from registry:** Set a session, call `Exit(assetId)`. Registry returns null for that assetId.
- **Exit on asset with no session:** Call `Exit` with an assetId that has no session. No exception.
- **Asset content unchanged after exit:** Set session, call exit. The asset files on disk are not modified (this is trivially true since `Exit` only touches the in-memory registry — assert via a boolean "no file write occurred" by verifying the registry is the only thing touched).
- **Annotation renderer IsActive false after exit:** Create renderer, set its active asset, set session in registry, assert `IsActive == true`. Then call `Exit`, assert `IsActive == false`.

---

### TASK-C-28 — Stale Comparison Badge

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-28`
**Design refs:** §6.2, §6.9

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/StaleBadgeWatcher.cs`

**Watcher class:**

```csharp
public sealed class StaleBadgeWatcher
{
    private readonly ComparisonSessionRegistry _registry;

    public StaleBadgeWatcher(ComparisonSessionRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    // Call this when any asset is saved.
    // If the asset has an active comparison session, marks it stale.
    public void OnAssetSaved(Guid assetId)
    {
        var session = _registry.GetSession(assetId);
        session?.MarkStale();
    }
}
```

**Toolbar chip:** Extend `ComparisonToolbarAction.Render()` to show a "Stale" warning chip/label when `session.IsStale == true`. The chip should use `ImGui.TextColored(orange, "[STALE]")`. No separate widget file needed for the chip.

**DI registration:** Register `StaleBadgeWatcher` as a singleton in `SharedAiEditorServiceCollectionExtensions`.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/StaleBadgeWatcherTests.cs`):**

- **OnAssetSaved marks session stale:** Set session for assetId. Call `OnAssetSaved(assetId)`. `registry.GetSession(assetId)!.IsStale == true`.
- **OnAssetSaved on asset without session:** Call `OnAssetSaved` with unknown assetId. No exception.
- **Re-apply response resets stale:** Mark stale, then call `registry.SetSession(new session)`. New session has `IsStale == false` (fresh constructor).

---

## Mandatory Workflow

1. **TASK-C-22:** Implement + test `ComparisonStyleMap` → all tests pass ✅
2. **TASK-C-21:** Implement + test `ComparisonAnnotationRenderer` (including severity filter and connection_changed logic) → all tests pass ✅
3. **TASK-C-25:** Extend annotation renderer with `variable_renamed` badge logic + tests ✅
4. **TASK-C-23:** Implement `ComparisonSummaryPanel` state model + panel + register in DI → tests pass ✅
5. **TASK-C-24:** Implement `ComparisonSidebar` state model + panel + register in DI → tests pass ✅
6. **TASK-C-26:** Add `BlackboardComparisonDecorator` + integrate into `BlackboardAuthoringWindow` → tests pass ✅
7. **TASK-C-27:** Implement `ExitComparisonAction` + wire into `ComparisonToolbarAction` → tests pass ✅
8. **TASK-C-28:** Implement `StaleBadgeWatcher` + register + wire chip → tests pass ✅
9. Full solution build: 0 errors ✅

---

## Developer Insights (Answer in Report)

**Q1:** For C-21, what is the NodeId type? Is it a GUID, an int, or a string? How did you translate `ComparisonChange.ElementId` (a string) to a `NodeId` for the `FindNode()` lookup?

**Q2:** For C-25 (variable_renamed badge), how did you scan nodes for bindings to the old variable name? Did `INodeModel` expose a property bag, or did you scan `Title`/`Subtitle`?

**Q3:** For C-23 and C-24, did `ManagedWindow` provide the severity filter toggle UX at the base class level, or did you implement it entirely in `DrawClientArea`?

**Q4:** For C-26, where exactly in `BlackboardAuthoringWindow` did you integrate the decorator? Are there existing per-row rendering loops, or did you add new ones?

**Q5:** List any edge cases or limitations noted. Suggest them as debt items.

---

## Success Criteria

- [ ] TASK-C-22: `ComparisonStyleMap` with ~15 tests (severity + kind tables + fallbacks + case insensitivity)
- [ ] TASK-C-21: `ComparisonAnnotationRenderer` with 6 tests (severity filter, missing node, connection_changed 3 variants, null session)
- [ ] TASK-C-25: 2 additional tests for variable_renamed badge
- [ ] TASK-C-23: `ComparisonSummaryPanel` + state model with 5 tests
- [ ] TASK-C-24: `ComparisonSidebar` + state model with 4 tests
- [ ] TASK-C-26: `BlackboardComparisonDecorator` + integration with 5 tests
- [ ] TASK-C-27: `ExitComparisonAction` + toolbar wiring with 4 tests
- [ ] TASK-C-28: `StaleBadgeWatcher` + DI registration with 3 tests
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` passes
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-06-REPORT.md`

---

## Reference Materials

- **Design §6.1–6.9:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
- **Task details C-21 through C-28:** `.dev\visual-asset-comparison\TASK-DETAILS.md`
- **ICustomCanvasRenderer:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomCanvasRenderer.cs`
- **FakeRenderContext example:** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Canvas/CustomRendererPassTests.cs`
- **Existing session state:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs`
- **DI extension:** `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`
- **ComparisonToolbarAction to extend:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonToolbarAction.cs`
