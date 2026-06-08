# BATCH-E: Breakpoint Toggle — Wire via NodeEdit native path

**Batch Number:** BATCH-E  
**Tasks:** Fix breakpoint toggle (root cause: `BlueprintEditorHostServices.Debug` never set; context menu path wrong)  
**Phase:** Slice-1 fix  
**Estimated Effort:** 2-4 hours  
**Priority:** HIGH

---

## 🚨 EXECUTION DIRECTIVE

**You are a coding agent. Your ONLY job is to implement this batch. Do NOT ask questions. Do NOT ask for permission. Do NOT stop until all tasks are done and tests pass. Edit files directly, build, run tests, write report. No interactive prompts.**

---
**Estimated Effort:** 2-4 hours  
**Priority:** HIGH  
**Dependencies:** Batches A/B/C/D committed (code exists, but context menu is dead code — this batch fixes it)

---

## 📋 Onboarding & Workflow

### Developer Instructions
The previous batch wired breakpoints through a custom `ICustomElementContextMenuProvider`, but that path only fires for `HoverKind.CustomElement` — never for regular node right-clicks. NodeEdit's native `NodeRenderer` already draws breakpoint markers + execution overlays via `view.Host.Debug` (`IDebugSession`), but Blueprint never sets `hostServices.Debug`.

This batch bridges `IBlueprintDebugSession` → NodeEdit `IDebugSession`, adds the "Toggle Breakpoint" menu item to NodeEdit's `CanvasRenderer`, and registers the command handler. Cleanup of the now-redundant custom renderers is **DEFERRED** until this works.

### Required Reading (IN ORDER)
1. **Architect's answer:** See the conversation context — architect confirmed: add menu item directly to `CanvasRenderer.cs` `HoverKind.Node` case, route through `IEditorCommands` pipeline, host registers handler.
2. **NodeEdit IDebugSession:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IDebugSession.cs` — the interface to implement.
3. **NodeEdit NodeRenderer (debug drawing):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/NodeRenderer.cs:192-242` — how NodeEdit draws breakpoints + execution overlays natively.
4. **BlueprintDocumentFactory:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` — where the adapter must be wired and the command registered.
5. **BlueprintEditorHostServices:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEditorHostServices.cs` — has `SetDebugSession()` (already exists, line 95).

### Source Code Location
- **NodeEdit library:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs`
- **Blueprint Editor:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`
- **Blueprint Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev/blueprint-dbg-1/reports/BATCH-E-REPORT.md`

---

## Context

NodeEdit's built-in `NodeRenderer.DrawStateOverlay()` (line 192) already draws:
- 🔴 Breakpoint markers (red circle on node header, line 237-242)
- 🟡 Gold pulsing outline on `CurrentlyExecutingNode` (line 204-215)
- 🟠 Orange afterglow on `RecentlyExecutedNodes` (line 217-220)

It reads `view.Host.Debug` (`IDebugSession`). Blueprint's `BlueprintEditorHostServices.Debug` property returns `_debug` which is **never set** — the factory never calls `SetDebugSession()`.

The architect confirmed: add "Toggle Breakpoint" to `CanvasRenderer.cs` `HoverKind.Node` as a first-class NodeEdit menu item, routable through `IEditorCommands`. `CommandCatalog.ToggleBreakpoint` already exists.

---

## 🎯 Batch Objectives

After this batch, right-clicking a node on the live blueprint canvas shows "Toggle Breakpoint (F9)", and clicking it actually sets/clears a breakpoint. NodeEdit's native `NodeRenderer` draws the red breakpoint marker + execution overlays automatically.

---

## ✅ Tasks

### Task 1: Add "Toggle Breakpoint" to NodeEdit CanvasRenderer (NodeEdit change)

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs` (UPDATE)  

**Description:** Add a "Toggle Breakpoint (F9)" menu item in the `HoverKind.Node` case of `DrawContextMenu`, after the existing "Add Comment" item and before the `break;`.

**Exact location:** After line ~728 (`CanvasCommands.AddCommentAroundSelection(view);`) and before the `break;` on line 730.

**Code to insert:**
```csharp
                ImGui.Separator();
                if (ImGui.MenuItem("Toggle Breakpoint", "F9"))
                {
                    if (!isHoveredSelected)
                        view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.ToggleBreakpoint);
                }
```

**Important:** `CommandCatalog` is in namespace `NodeEditor.Core` — verify the using is present (it should be — `CommandCatalog.GoToDefinition` is already used at line 667). `_editorCommands` is a field already available in `CanvasRenderer`.

---

### Task 2: Create BlueprintDebugToNodeEditAdapter (NEW FILE)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/BlueprintDebugToNodeEditAdapter.cs` (NEW FILE)

**Description:** Implements NodeEdit's `IDebugSession` (`NodeEditor.Core.Interfaces.IDebugSession`), wrapping `IBlueprintDebugSession` (`Hrot.Blueprints.Core.Debug.IBlueprintDebugSession`). This bridges the two debug session types so NodeEdit's `NodeRenderer` can read breakpoint/execution state.

**Constructor:** `(IBlueprintDebugSession session, Guid assetId, Guid graphId)`

**Interface mapping (every member must be implemented):**

| NodeEdit `IDebugSession` member | Implementation |
|---|---|
| `bool IsAttached` | `true` (session is always attached when non-null) |
| `bool IsPaused` | `_session.IsPaused` |
| `NodeId? CurrentlyExecutingNode` | From `_session.GetRecentNodeHistory(1)` — if count > 0, parse `NodeIdString` as Guid, return `new NodeId(guid)`. If paused, also check `PausedAt?.NodeId`. |
| `IReadOnlySet<NodeId> RecentlyExecutedNodes` | From `_session.GetRecentNodeHistory(10)`, parse each `NodeIdString` as Guid → `new NodeId(guid)`. Filter out `Guid.Empty`. Return as `HashSet<NodeId>`. |
| `IReadOnlySet<NodeId> Breakpoints` | From `_session.GetBreakpoints()`, filter where `bp.AssetId == _assetId && bp.GraphId == _graphId`, parse `bp.NodeId` (string, "D" format) as Guid → `new NodeId(guid)`. |
| `IReadOnlySet<PinId> WatchedPins` | From `_session.GetWatches()`, map watch pin IDs. **PinId** in NodeEdit is `new PinId(Guid)`. The watch's PinId format depends on how watches are stored — check `IBlueprintDebugSession.AddWatch` signature. If watch PinId is a string, parse as Guid. |
| `void ToggleBreakpoint(NodeId node)` | Look up if a breakpoint already exists for `_assetId`, `_graphId`, `node.Value.ToString("D")`. If yes → `_session.ClearBreakpoint(existing.Id)`. If no → `_session.SetBreakpoint(_assetId, _graphId, node.Value)`. |
| `void ToggleWatch(PinId pin)` | Check if already watched, call `_session.AddWatch(...)` or `_session.RemoveWatch(...)` accordingly. Match the existing `AddWatch`/`RemoveWatch` signatures. |
| `void Continue()` | `_session.Continue()` |
| `void StepOver()` | `_session.StepOver()` |
| `void StepInto()` | `_session.StepInto()` |
| `void StepOut()` | `_session.StepOut()` |
| `object? GetWatchValue(PinId pin)` | Return `null` for now (watch values are rendered by the WatchPanelWindow, not via this path). |
| `event Action? StateChanged` | Subscribe to `_session.OnNodeEnter` / `_session.OnPinValueChangedEvent` and invoke. Also invoke on relevant state changes. Use `+=` and `-=` to manage subscriptions. |

**Key types:**
- `NodeId` = `NodeEditor.Primitives.NodeId` (record struct wrapping `Guid Value`)
- `PinId` = `NodeEditor.Primitives.PinId` (record struct — check exact ctor, likely `new PinId(Guid)`)

---

### Task 3: Wire adapter in BlueprintDocumentFactory

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` (UPDATE)

**Description:** After creating `hostServices` (line 197) and wiring the context menu provider (lines 200-203), add:

```csharp
        // Bridge IBlueprintDebugSession → NodeEdit IDebugSession so NodeRenderer
        // natively draws breakpoint markers and execution overlays.
        if (debugSession != null)
        {
            var adapter = new BlueprintDebugToNodeEditAdapter(debugSession, bpAsset.AssetId, graph.Id);
            hostServices.SetDebugSession(adapter);
        }
```

Also register the `editor.toggle-breakpoint` command handler after `BuiltinCommandHandlers.RegisterAll` (line 220):

```csharp
        // Register debug commands (Toggle Breakpoint etc.)
        commands.Register(
            new EditorCommandDescriptor(
                CommandCatalog.ToggleBreakpoint,
                "Toggle Breakpoint", "Debug",
                "Toggles a breakpoint on the selected node.",
                null,
                new KeyBinding(EditorKey.F9, KeyModifiers.None),
                IsEnabled: () => view.Selection.Nodes.Any()),
            _ =>
            {
                var dbg = hostServices.Debug;
                if (dbg == null) return;
                foreach (var nodeId in view.Selection.Nodes)
                    dbg.ToggleBreakpoint(nodeId);
            });
```

**Required usings (add at top of file):**
```csharp
using NodeEditor.Core;           // CommandCatalog
using NodeEditor.Core.Action;    // EditorCommandDescriptor, EditorCommandContext
```

---

### Task 4: Remove dead context menu provider wiring (CLEANUP — partial)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` (UPDATE)

**Description:** Remove or comment out lines 200-203 that create and set the `BlueprintBreakpointContextMenuProvider`, since it's dead code (the `ICustomElementContextMenuProvider` path never fires for regular nodes). Add a comment noting it's superseded by the native NodeEdit path.

```csharp
        // SUPERSEDED: BlueprintBreakpointContextMenuProvider was dead code — the
        // ICustomElementContextMenuProvider path only fires for custom-rendered
        // elements, not regular node right-clicks. Toggle Breakpoint is now wired
        // through NodeEdit's CanvasRenderer (HoverKind.Node) + IEditorCommands.
        // Old wiring (removed):
        // if (debugSession != null)
        //     hostServices.SetBreakpointContextMenu(
        //         new BlueprintBreakpointContextMenuProvider(
        //             debugSession, bpAsset.AssetId, graph.Id));
```

---

## 🧪 Testing Requirements

**CRITICAL: You MUST run ALL tests before submitting.** See Success Criteria below.

### Test 1: Adapter unit tests (5-7 tests)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BlueprintDebugToNodeEditAdapterTests.cs`

**Test specifications (implement exactly these):**

1. **`ToggleBreakpoint_Sets_WhenNotAlreadySet`** — Create adapter with a `CapturingDebugSession`. Call `adapter.ToggleBreakpoint(new NodeId(testNodeGuid))`. Assert `session.GetBreakpoints()` contains a breakpoint for that node.

2. **`ToggleBreakpoint_Clears_WhenAlreadySet`** — Pre-register a breakpoint via `session.SetBreakpoint(...)`. Call `adapter.ToggleBreakpoint(nodeId)`. Assert breakpoint is removed from `session.GetBreakpoints()`.

3. **`Breakpoints_ReturnsCorrectSet`** — Set breakpoints on two nodes in the matching asset/graph, plus one in a different asset. Assert `adapter.Breakpoints` contains only the two matching NodeIds.

4. **`IsPaused_DelegatesToSession`** — Assert adapter reports `IsPaused == false` initially. Then simulate pause (set `CapturingDebugSession.IsPaused = true` if possible, or use a real `BlueprintDebugSession`).

5. **`Continue_StepOver_StepInto_StepOut_Delegate`** — Call each method on adapter, verify the corresponding session method was invoked (use a spy/mock or check side effects).

6. **`CurrentlyExecutingNode_FromHistory`** — If `CapturingDebugSession` supports recording node history, simulate an `OnNodeEnter` event and verify `adapter.CurrentlyExecutingNode` returns the correct NodeId.

7. **`IsAttached_ReturnsTrue`** — Adapter with non-null session → `IsAttached == true`.

### Test 2: Factory wiring test (1-2 tests)

Add to `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BlueprintDocumentFactoryTests.cs`:

8. **`Build_WithDebugSession_SetsHostDebug`** — Call `BlueprintDocumentFactory.Build(fileAsset, bundle, debugSession: capturingSession)`. Assert `ctx.View.Host.Debug` is not null. Verify it's the adapter type.

### Test 3: Register command handler test (1 test)

9. **`ToggleBreakpoint_Command_Registered_And_Invokable`** — Build with debug session. Get `ctx.Commands` (cast to `EditorCommandsImpl`). Find the registered command for `CommandCatalog.ToggleBreakpoint`. Assert `IsEnabled()` returns true (selection has nodes). Invoke the command action. Assert `CapturingDebugSession.SetBreakpoint` was called for the selected node.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] CanvasRenderer has "Toggle Breakpoint (F9)" in node context menu
- [ ] `BlueprintDebugToNodeEditAdapter` implements all `IDebugSession` members
- [ ] Adapter wired in `BlueprintDocumentFactory.Build()` via `SetDebugSession`
- [ ] `editor.toggle-breakpoint` command registered and invokable
- [ ] Dead `BlueprintBreakpointContextMenuProvider` wiring removed from factory
- [ ] **`dotnet build IOS-IG-SimHost.sln -c Debug` passes with 0 errors, 0 warnings**
- [ ] **`Hrot.Blueprints.Tests` — all existing tests pass; 0 new failures; 7 pre-existing failures unchanged**
- [ ] **New adapter tests (7+ scenarios) all pass**
- [ ] Report submitted

**Pre-existing failures (7):** `AllocationFreeTests`, `WhenNodePerfTests`, `AiPrimitiveEmitGolden` (MoveToAndFire/HasVisibleTarget `*_GeneratedSource_Snapshot`), `LibraryEmitGolden`, `LibraryMath/MoveToAndFire *_GeneratedSource_Snapshot`, `ConditionSummaryAttachmentTests.EqsResult`

---

## ⚠️ Common Pitfalls to Avoid

- **Don't skip the CanvasRenderer change** — without it, the menu item never appears even if everything else is wired.
- **NodeId vs Guid:** `NodeId` is a `record struct NodeId(Guid Value)`. Use `new NodeId(guid)`, not `new NodeId { Value = guid }`.
- **PinId:** Check the exact constructor in `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/PinId.cs`.
- **`_editorCommands` field:** Verify this field exists in `CanvasRenderer`. If it's null (e.g., headless mode), the `?.Invoke` handles it gracefully.
- **DO NOT delete the custom renderer files** (`BlueprintBreakpointGutterRenderer`, `BlueprintRuntimeOverlayRenderer`, `BlueprintBreakpointContextMenuProvider`) — cleanup is deferred. Just stop wiring the context menu provider.
- **Run tests with editor CLOSED** — DLLs are locked while the editor is running.

---

## 📚 Reference Materials
- **NodeEdit IDebugSession:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IDebugSession.cs`
- **NodeEdit NodeRenderer (debug drawing):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/NodeRenderer.cs:192-242`
- **NodeEdit CanvasRenderer (context menu):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs:649-730`
- **CommandCatalog:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/CommandCatalog.cs:74` (`ToggleBreakpoint` already exists)
- **IEditorCommands / EditorCommandDescriptor:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs:26-34`
- **EditorCommandsImpl.Register:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/EditorCommandsImpl.cs:44`
- **BlueprintEditorHostServices:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEditorHostServices.cs:95`
- **CapturingDebugSession (test double):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/CapturingDebugSession.cs`
- **Existing factory tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BlueprintDocumentFactoryTests.cs`
