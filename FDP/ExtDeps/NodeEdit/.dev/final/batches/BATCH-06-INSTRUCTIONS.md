# BATCH-06: Extended Demo Scenarios (S15–S33)

**Batch Number:** BATCH-06  
**Tasks:** TASK-P7-003  
**Phase:** Phase 7 — Polish Features  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-05 (all completed)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the final 19 demo scenarios (S15–S33) plus the fake-host extensions they
require. After this batch every feature in the editor design will have at least one end-to-end
exercise in the demo app.

**Work autonomously to completion. Do not stop and ask for permission to do routine things like
running the build, fixing warnings, or running the test suite. Fix every issue you find and only
stop when the Success Criteria below are fully met.**

### Required Reading (IN ORDER)

1. **Task Brief (full):** `.dev/final/instructions/03-12-task-extended-scenarios.md`
   — read **every section**, including per-scenario detail and Demo chrome additions.
2. **Existing Scenarios for pattern:** `src/NodeEditor.Demo/Scenarios/S13_DebugVizMock.cs`
   and `S03_BoxSelectAndDrag.cs` — study how `Build()` sets up pre-built graph state.
3. **Scenario base class:** `src/NodeEditor.Demo/Scenarios/Scenario.cs`
4. **FakeMyBlueprintModel:** `src/NodeEditor.Demo/FakeBlueprint/FakeMyBlueprintModel.cs`
5. **FakeCommandSink:** `src/NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs`
6. **DemoShell (full):** `src/NodeEditor.Demo/DemoShell.cs` — understand how scenarios are
   registered, how `ApplyScenario()` works, and where to hook multi-graph support.
7. **Bookmarks Core:** `src/NodeEditor.Core/Bookmarks/BookmarkStore.cs` — for S31.
8. **HotReload UI:** `src/NodeEditor.UI/HotReload/ChangeNotifier.cs`,
   `src/NodeEditor.UI/HotReload/ChangeBadgeRenderer.cs` — for S32.
9. **Action ToastQueue:** `src/NodeEditor.Core/Action/ToastQueue.cs` — for mock save/compile toasts.
10. **Specs (for context):**
    - `.dev/final/specs/D9-bookmarks.md` — S31
    - `.dev/final/specs/D10-hot-reload.md` — S32
    - `.dev/final/specs/D1-to-D4-flows.md` §D.4 — S28/S29 find prefixes
    - `.dev/final/specs/D8-comments-reroutes.md` — S26/S27

### Source Code Location

- **New scenario files:** `src/NodeEditor.Demo/Scenarios/` (S15_*.cs … S33_*.cs)
- **FakeBlueprint extensions:** `src/NodeEditor.Demo/FakeBlueprint/`
  - Modify `FakeMyBlueprintModel.cs` — add mutation methods
  - Modify `FakeCommandSink.cs` if any new `GraphCommand` subtypes need to be handled
  - **NEW:** `FakeGraphContainer.cs` — multi-graph asset abstraction for multi-tab scenarios
- **DemoShell:** `src/NodeEditor.Demo/DemoShell.cs` — register S15–S33, handle multi-graph, add FPS badge

### Report Submission

**When done, submit your report to:** `.dev/final/reports/BATCH-06-REPORT.md`  
**Questions (only for blocking architectural issues):** `.dev/final/questions/BATCH-06-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with a green build after each group:**

1. **Group A — Infrastructure (FakeBlueprint extensions + FakeGraphContainer):**
   Implement → Build clean ✅
2. **Group B — Scenarios S15–S21 (Blueprint authoring):**
   Implement → Build clean ✅
3. **Group C — Scenarios S22–S27 (Collapse/Expand + Multi-tab + Comments):**
   Implement → Build clean ✅
4. **Group D — Scenarios S28–S33 (Find + Bookmarks + Hot-reload + Big Graph):**
   Implement → Build clean ✅
5. **Group E — DemoShell wiring (register all 19 scenarios + chrome additions):**
   Implement → Build clean ✅ → Full test suite green ✅

**DO NOT** move to the next group until the current group compiles without warnings.

**Run after every group:**
```powershell
cd "d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\NodeEdit"
dotnet build NodeEditor.sln -v quiet
dotnet test  NodeEditor.sln --no-build -v quiet
```

---

## Context

After this batch:
- All 19 scenarios (S15–S33) are selectable from the scenario dropdown in `DemoShell`.
- Each scenario's `Description` property tells the user what to try interactively.
- Function/macro/variable/event/dispatcher authoring features are exercised via the fake host.
- Multi-tab graphs work for S25.
- Find bar is exercised with pre-built node sets (S28, S29).
- Bookmarks are exercised (S31).
- Hot-reload conflict flow is exercisable via a "Simulate External Modify" button (S32).
- Big-graph performance scenario with 500 nodes (S33).

---

## ✅ Tasks

### Task 1: FakeBlueprint Extensions

**Purpose:** Give the scenarios the infrastructure they need without fighting the existing
design.

#### 1a. Extend `FakeMyBlueprintModel`

**File:** `src/NodeEditor.Demo/FakeBlueprint/FakeMyBlueprintModel.cs` (MODIFY)

Add mutation methods (keeping all existing code intact):

```csharp
// Variables
public void AddVariable(string id, string name, Vector4 color, string? tooltip = null);
public void RemoveVariable(string id);
public void RenameVariable(string id, string newName);

// Functions
public void AddFunction(string id, string name);
public void RemoveFunction(string id);

// Macros (new section "macros")
public void AddMacro(string id, string name);

// Custom Events (extend "events" section — add canCreate=true)
public void AddCustomEvent(string id, string name);
public void RemoveCustomEvent(string id);

// Event Dispatchers (new section "dispatchers")
public void AddDispatcher(string id, string name);
```

Each method fires `NotifyChanged()` after mutating `_sections`.  
Add `"macros"` and `"dispatchers"` sections to `Sections` (at construction) with appropriate
`SortOrder`, `CanCreate`, and display names.

#### 1b. Create `FakeGraphContainer`

**File:** `src/NodeEditor.Demo/FakeBlueprint/FakeGraphContainer.cs` (NEW)

A lightweight container that tracks multiple named graphs for multi-tab scenarios:

```csharp
public sealed class FakeGraphContainer
{
    public IReadOnlyList<FakeGraphModel> Graphs { get; }
    public int ActiveIndex { get; private set; }
    public FakeGraphModel Active => Graphs[ActiveIndex];

    public FakeGraphContainer(params FakeGraphModel[] graphs);
    public void ActivateNext();
    public void ActivatePrev();
    public void Activate(int index);
}
```

#### 1c. Extend `Scenario` base class (optional overridable)

**File:** `src/NodeEditor.Demo/Scenarios/Scenario.cs` (MODIFY)

Add an optional override that scenarios can use to supply a multi-graph container:

```csharp
/// <summary>Override to supply multi-graph setup. Null means single-graph.</summary>
public virtual FakeGraphContainer? BuildMultiGraph(
    FakeNodeCatalog catalog,
    out FakeMyBlueprintModel myBlueprint) { myBlueprint = null!; return null; }
```

Only S25 needs this. Most scenarios ignore it.

---

### Task 2: Scenario Files S15–S33

**Full per-scenario specification:** See `.dev/final/instructions/03-12-task-extended-scenarios.md`,
section "Per-scenario detail".

**Files to create (19 files in `src/NodeEditor.Demo/Scenarios/`):**

| File | Scenario |
|------|----------|
| `S15_VariablesGetSet.cs` | Variable creation + Get/Set drag |
| `S16_PromoteToVariable.cs` | RMB pin → Promote to Variable |
| `S17_CustomEvent.cs` | Create event with params |
| `S18_FunctionAuthoring.cs` | Navigate into function body |
| `S19_MultipleReturnNodes.cs` | Function with multiple Returns |
| `S20_MacroWithWildcards.cs` | Wildcard macro resolution |
| `S21_EventDispatcher.cs` | Create dispatcher + Call/Bind/Unbind |
| `S22_CollapseToFunction.cs` | Ctrl+E → collapse selection → function |
| `S23_CollapseToMacro.cs` | Latent node → macro requirement |
| `S24_ExpandNode.cs` | RMB function call → Expand inline |
| `S25_MultiTab.cs` | 3 graphs, Ctrl+Tab switching |
| `S26_Comments.cs` | Comment create/drag/rename/recolor/resize |
| `S27_NestedComments.cs` | 3 nested comments + z-order |
| `S28_FindInGraph.cs` | Ctrl+F with prefixes, F3 cycling |
| `S29_FindInAsset.cs` | Ctrl+Shift+F across graphs |
| `S30_GoToDefinition.cs` | F12 navigation |
| `S31_Bookmarks.cs` | Ctrl+Shift+1..9 set + Ctrl+1..9 jump |
| `S32_HotReloadConflict.cs` | Dirty + external reload → blocking toast |
| `S33_BigGraph.cs` | 500 nodes + perf check |

**Implementation rules for each scenario:**

1. **`Name` property** — format `"NN — Short Name"` (matches existing S01–S13 convention).
2. **`Description` property** — concise instruction telling the user what to try interactively
   (this shows in the toolbar next to the scenario picker).
3. **`Build()` method** — constructs the pre-built graph state described in the task brief.
   Use `AddNode()` and `LinkNodes()` helpers inherited from `Scenario`. For comments, call
   `graph.AddComment(...)` directly (pattern from `FakeGraphModel`).
4. **No heavy logic in Build** — the scenario is a setup fixture. The user interacts manually
   to exercise the feature. The description tells them what to do.
5. **Zero ImGui imports** — `Build()` runs before any rendering; do not call ImGui from `Build()`.

**Key implementation notes per scenario group:**

**S15–S21 (Blueprint authoring):**  
Pre-populate `FakeMyBlueprintModel` by calling the new mutation methods added in Task 1a.
For example, S15 should call `mbModel.AddVariable("var.health", "Health", ...)` so the My
Blueprint panel shows the right state. These scenarios receive the model from `FakeHostServices`;
you may need to expose it via the scenario's `Build` override or a separate `Setup` method.

Design decision: add an optional `Setup(FakeMyBlueprintModel mbModel)` virtual to `Scenario`
so scenarios that need to pre-configure the My Blueprint panel can do so. `DemoShell` calls this
after constructing the host but before calling `Build()`.

```csharp
/// <summary>Optional pre-build configuration step for the fake host services.</summary>
public virtual void Setup(FakeMyBlueprintModel mbModel) { }
```

**S25 (Multi-Tab):**  
Override `BuildMultiGraph` (added in Task 1c) to return a `FakeGraphContainer` with 3 graphs.
`DemoShell` detects when a scenario returns a non-null container and switches to a multi-graph
mode (tab bar at the top of the canvas). See Task 3 for DemoShell changes.

**S26–S27 (Comments):**  
Call `graph.AddComment(IdGenerator.NewCommentId(), text, pos, size, color, true)` in `Build()`.
`IdGenerator.NewCommentId()` is in `src/NodeEditor.Primitives/IdGenerator.cs`.

**S28 (Find in Graph):**  
Create a graph with at least:
- 5 `Math.Multiply` nodes
- 3 nodes with error state (`NodeState.Error`)
- 2 nodes with breakpoint state (`NodeState.Breakpoint`)
- 5+ `Flow.Branch` nodes
Use `FakeNodeModel.State` to set `NodeState.Error | NodeState.Breakpoint` where needed.
`FakeNodeModel` is in `src/NodeEditor.Demo/FakeBlueprint/FakeNodeModel.cs`.

**S33 (Big Graph):**  
Use the helper code exactly as shown in the task brief (`.dev/final/instructions/03-12-task-extended-scenarios.md`,
section "S33 — Big Graph (performance)"). Call `TryAddCompatibleLink` which should:
1. Find a random output pin on node `a`.
2. Find a random input pin on node `b`.
3. Call `LinkNodes()` wrapped in a try/catch that swallows `InvalidOperationException` (pin
   already has a wire). Skip silently on failure.

---

### Task 3: DemoShell Wiring

**File:** `src/NodeEditor.Demo/DemoShell.cs` (MODIFY)

#### 3a. Register all 19 new scenarios in the constructor

```csharp
// S14 (bookmarks scenario from P7-001) — if not already registered
_scenarios.Add(new S15_VariablesGetSet());
// ... through ...
_scenarios.Add(new S33_BigGraph());
```

Add them after the existing `_scenarios.Add(new S13_DebugVizMock())` line.

#### 3b. Update `ApplyScenario()` to support multi-graph and Setup

```csharp
private void ApplyScenario(int index)
{
    // ... existing reset code ...

    var scenario = _scenarios[index];

    // Optional per-scenario My Blueprint setup
    scenario.Setup(_host.MyBlueprint_);  // new virtual method

    // Check for multi-graph scenario
    var container = scenario.BuildMultiGraph(_host.NodeCatalog_, out var customMbModel);
    if (container is not null)
    {
        _graphContainer = container;
        _graph = container.Active;
        // re-create host bound to active graph
        _host = new FakeHostServices(_graph);
        if (customMbModel is not null) _host.OverrideMyBlueprint(customMbModel);
        _view = CreateView();
    }
    else
    {
        _graphContainer = null;
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _host  = new FakeHostServices(_graph);
        _view  = CreateView();
        scenario.Build(_view, _graph, _host.CommandSink_, _host.NodeCatalog_);
    }

    // ... rest of existing panel/command wiring ...
}
```

Keep the design pragmatic — if `FakeHostServices` doesn't have `OverrideMyBlueprint`, add it:
```csharp
public void OverrideMyBlueprint(FakeMyBlueprintModel model) => MyBlueprint_ = model;
```

#### 3c. Multi-graph tab bar

When `_graphContainer is not null`, render a tab bar **inside** the Canvas window before calling
`_canvas.Render(...)`:

```csharp
if (_graphContainer is not null)
{
    if (ImGui.BeginTabBar("##graphs"))
    {
        for (int i = 0; i < _graphContainer.Graphs.Count; i++)
        {
            bool isActive = i == _graphContainer.ActiveIndex;
            if (ImGui.BeginTabItem(_graphContainer.Graphs[i].DisplayName))
            {
                if (!isActive) _graphContainer.Activate(i);
                ImGui.EndTabItem();
            }
        }
        ImGui.EndTabBar();
    }
}
```

Also handle keyboard shortcuts Ctrl+Tab / Ctrl+Shift+Tab in `Frame()` or `HotkeyDispatcher`.

#### 3d. Demo chrome additions (per task brief)

Add to `DrawMenuBar()` or `DrawStatusBar()` as appropriate:

1. **FPS badge** — In the status bar, right-side, display `$"FPS: {1.0/elapsedSeconds:F0}"`.
   Store `elapsedSeconds` from `Frame(double elapsedSeconds)` in a field. Guard against
   division by zero with `Math.Max(elapsedSeconds, 0.001)`.

2. **Save (mock) + Compile (mock)** — In the File menu, add two items:
   ```csharp
   if (ImGui.MenuItem("Save (mock)", "Ctrl+S"))
       _host.ToastQueue_.Enqueue("Saved (no-op in demo)", ToastKind.Info, 2.0f);
   if (ImGui.MenuItem("Compile (mock)", "F7"))
       _host.ToastQueue_.Enqueue("Compiled (no-op in demo)", ToastKind.Success, 2.0f);
   ```
   `ToastQueue` is in `src/NodeEditor.Core/Action/ToastQueue.cs`; expose it through
   `FakeHostServices` if not already present.

3. **Make Dirty button** — Next to the scenario picker in the menu bar:
   ```csharp
   ImGui.SameLine();
   if (ImGui.SmallButton("Make Dirty"))
       _view.Undo.SetDirty();   // if Undo has SetDirty; else call an Undo.AddDummyCommand
   ```
   Check what `UndoStack` exposes (`src/NodeEditor.Core/Commands/UndoStack.cs`) and choose
   the simplest way to flip the dirty flag.

---

## 🧪 Testing Requirements

These are **visual demo scenarios**. There are no new unit tests required in
`tests/NodeEditor.Core.Tests/` or `tests/NodeEditor.UI.Tests/` for this batch.

However, the existing test suite **must stay green**. Run after every group:

```powershell
dotnet test "d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\NodeEdit\NodeEditor.sln" --no-build -v quiet
```

Expected baseline: **67 tests passing** (63 Core + 4 UI).

The quality bar for scenarios is:
- Each scenario **compiles** without warnings.
- Each scenario **appears in the dropdown** when the demo runs.
- The `Description` property tells the user exactly what to do for each scenario.
- S33 `Build()` actually creates 500 nodes (verify with a `Debug.Assert` or a check in the
  Build method: `System.Diagnostics.Debug.Assert(graph.Nodes.Count == 500);`).
- The FPS badge is visible in the status bar.

---

## ⚠️ Quality Standards

**❗ BUILD MUST STAY AT ZERO WARNINGS.**  
CS0168, CS0219, CS8600 and similar "unused variable" / "possible null" warnings are not
acceptable. Treat warnings as errors: if `dotnet build` shows any warning count > 0, fix them
before writing the report.

**❗ DO NOT BREAK EXISTING TESTS.**  
Check `dotnet test` output before writing the report.

**❗ KEEP SCENARIOS FOCUSED.**  
Each scenario file is ~30–80 LOC. If a scenario exceeds 120 LOC it is too complex — simplify
the pre-built graph.

**❗ MATCH EXISTING CODE STYLE.**  
- Use `var` for local declarations.  
- XML `/// <summary>` doc comments on the class.  
- Follow the exact naming convention: `SNN_PascalCaseName`.

---

## 📊 Report Requirements

**Submit your report to:** `.dev/final/reports/BATCH-06-REPORT.md`

Include:

1. **Files Created / Modified** — table with file path and brief purpose.
2. **Build & Test Results** — paste the last few lines of `dotnet build` and `dotnet test` output.
3. **Developer Insights:**
   - **Q1:** What issues did you encounter during implementation? How did you resolve them?
   - **Q2:** Did you spot any weak points or rough edges in the existing FakeBlueprint / DemoShell
     code? What would you improve?
   - **Q3:** What design decisions did you make beyond the instructions? What alternatives did
     you consider?
   - **Q4:** Which scenarios required the most infrastructure change and why?
   - **Q5:** Are there any performance or UX concerns you noticed that aren't captured in a task?
4. **Suggested commit message** (one line, imperative, ≤72 chars).

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] `FakeMyBlueprintModel` has mutation methods (AddVariable, AddFunction, AddMacro, AddCustomEvent, AddDispatcher, etc.)
- [ ] `FakeGraphContainer` created and functional
- [ ] All 19 scenario files exist in `src/NodeEditor.Demo/Scenarios/`
- [ ] All 19 scenarios registered in `DemoShell._scenarios`
- [ ] S25 multi-tab works (tab bar shows 3 graph tabs, Ctrl+Tab cycles)
- [ ] S33 generates exactly 500 nodes
- [ ] FPS badge visible in the status bar
- [ ] Save(mock) and Compile(mock) menu items present and emit toasts
- [ ] Make Dirty button present next to scenario picker
- [ ] `dotnet build NodeEditor.sln -v quiet` → **0 Warning(s), 0 Error(s)**
- [ ] `dotnet test NodeEditor.sln --no-build -v quiet` → **all tests pass (67 expected)**
- [ ] Report submitted to `.dev/final/reports/BATCH-06-REPORT.md`

---

## 📚 Reference Materials

- **Task Brief (normative):** `.dev/final/instructions/03-12-task-extended-scenarios.md`
- **Existing scenario patterns:** `src/NodeEditor.Demo/Scenarios/S13_DebugVizMock.cs`
- **FakeGraphModel API:** `src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs`
- **FakeNodeModel:** `src/NodeEditor.Demo/FakeBlueprint/FakeNodeModel.cs`
- **IdGenerator:** `src/NodeEditor.Primitives/IdGenerator.cs`
- **NodeState enum:** `src/NodeEditor.Primitives/Enums.cs`
- **BookmarkStore:** `src/NodeEditor.Core/Bookmarks/BookmarkStore.cs`
- **ToastQueue:** `src/NodeEditor.Core/Action/ToastQueue.cs`
- **UndoStack:** `src/NodeEditor.Core/Commands/UndoStack.cs`
- **ChangeNotifier:** `src/NodeEditor.UI/HotReload/ChangeNotifier.cs`
