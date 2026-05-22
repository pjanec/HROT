# BATCH-05: Find Bar, Comments/Reroutes, Action API

**Batch Number:** BATCH-05  
**Tasks:** TASK-P4-004, TASK-P4-005, TASK-P5-001  
**Phase:** Phase 4 (part 2) + Phase 5  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-04 (all completed)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes Phase 4 (Find Bar, Comments/Reroutes rendering) and implements Phase 5 (Action API).
All three tasks are in `NodeEditor.UI` and/or `NodeEditor.Core`.

T-18 requires small additions to `InteractionState.cs` (already in `src/NodeEditor.Core/View/`) —
this is one of the few cross-project touch-points; keep the additions minimal.

### Required Reading (IN ORDER)

1. **Ground Rules:** `.dev/final/instructions/00-START-HERE.md`
2. **Task Brief — Find + Comments:** `.dev/final/instructions/03-08-task-find-comments.md` (read full file)
3. **Task Brief — Action API:** `.dev/final/instructions/03-09-task-action-api.md` (read full file)
4. **Spec Brief Part 2 §21, §22–25, §26:** `.dev/final/instructions/01-spec-brief-part2.md`
5. **Find/Navigation Spec:** `.dev/final/specs/D1-to-D4-flows.md` §D.4
6. **Action API Spec:** `.dev/final/specs/D0-action-api.md`
7. **InteractionState (existing):** `src/NodeEditor.Core/View/InteractionState.cs` — read before modifying
8. **CommandCatalog (existing):** `src/NodeEditor.Core/CommandCatalog.cs` — all command ID constants
9. **IEditorCommands / IEditorIndicators (existing):** `src/NodeEditor.Core/Action/IEditorCommands.cs`, `IEditorIndicators.cs`

### Source Code Location

- **New UI files:** `src/NodeEditor.UI/Find/`, `src/NodeEditor.UI/Action/`
- **New Core files:** `src/NodeEditor.Core/Action/` (EditorCommandsImpl, EditorIndicatorsImpl)
- **Modified Core:** `src/NodeEditor.Core/View/InteractionState.cs` (T-18 additions only)
- **Modified UI Canvas:** `src/NodeEditor.UI/Canvas/CanvasRenderer.cs` (T-17: add FindBar overload; T-18: add reroutes renderer)

### Report Submission

**When done, submit your report to:** `.dev/final/reports/BATCH-05-REPORT.md`  
**Questions:** `.dev/final/questions/BATCH-05-QUESTIONS.md`

---

## Context

After this batch:
- Find bar (Ctrl+F) works above the canvas with live search, navigation (F3/Shift+F3), overlay.
- `FindResultsPanel` shows asset-scope search results.
- Comment boxes render correctly, can be dragged (with or without contents), resized, and renamed.
- Reroute waypoints render as small circles; can be dragged individually.
- `EditorCommandsImpl` and `EditorIndicatorsImpl` are implemented and wired to canvas operations.
- `BuiltinCommandHandlers` registers Undo, Redo, Delete, SelectAll, ZoomIn/Out, FrameAll, Find, etc.

---

## ✅ Tasks

### Task 1: Find Bar + Find-in-Asset Panel (TASK-P4-004)

**Task Definition:** See `.dev/final/instructions/03-08-task-find-comments.md` T-17 section  
**Spec:** `.dev/final/specs/D1-to-D4-flows.md` §D.4

**Files to create:**
```
src/NodeEditor.UI/Find/FindBar.cs
src/NodeEditor.UI/Find/FindResultsPanel.cs
src/NodeEditor.UI/Find/FindEngine.cs
src/NodeEditor.UI/Find/FindQuery.cs
src/NodeEditor.UI/Find/FindResult.cs
src/NodeEditor.UI/Find/FindScope.cs
```

**Files to modify:**
- `src/NodeEditor.UI/Canvas/CanvasRenderer.cs` — add overload `Render(GraphView view, FindBar? findBar)` that renders the find overlay (yellow outlines for matches, dim for non-matches, auto-center animation) when `findBar.IsVisible && findBar.Results.Count > 0`.

**Key requirements:**
- Bar layout: search input + scope dropdown + prev/next buttons + case/regex toggles + match count + close button
- Search is live (re-run `FindEngine.Search` on every query char change)
- `FindQueryParser.Parse(raw)` extracts type:/kind:/category:/var:/func:/error:/warning:/breakpoint:/watched: prefixes
- `FindEngine` uses FuzzyMatcher for free-text; `Regex.IsMatch` in regex mode
- ESC behavior: has text → clear; empty → close
- `FindResultsPanel`: groups results by graph, collapsible sections, click navigates

**Test note:** `FindQueryParser.Parse` is pure logic — add tests in `tests/NodeEditor.Core.Tests/Find/FindQueryParserTests.cs`:
- `Parse_FreeTextOnly` — no prefixes → just FreeText set
- `Parse_TypePrefix` — `"type:Vector3 foo"` → prefix `type`=`Vector3`, FreeText=`foo`
- `Parse_MultiplePrefix` — `"kind:branch error: foo"` → two prefixes + free text
- `Parse_Empty` → empty FreeText, no prefixes

These tests go in `NodeEditor.Core.Tests` (pure logic, no ImGui dependency).

---

### Task 2: Comments and Reroutes (TASK-P4-005)

**Task Definition:** See `.dev/final/instructions/03-08-task-find-comments.md` T-18 section

**Files to create:**
```
src/NodeEditor.UI/Canvas/CommentsRenderer.cs
src/NodeEditor.UI/Canvas/ReroutesRenderer.cs
```

**Files to modify:**
- `src/NodeEditor.Core/View/InteractionState.cs` — add these fields and reset them in `ResetToIdle()`:
  ```csharp
  public Dictionary<CommentId, Vector2> CommentDragOverridePositions { get; } = new();
  public CommentId? RenamingComment { get; set; }
  ```
- `src/NodeEditor.UI/Canvas/CanvasInput.cs` — add `DraggingComment`, `ResizingComment` handling (they should already have stubs from BATCH-03; fill in the logic per T-18 spec)

**Key requirements:**
- `CommentsRenderer`: draws comment box bodies + translucent fill, header strip with title, resize handles (8 handle dots at corners/edges)
- Selection outline on selected comments
- Inline rename: on double-click header → `ImGui.InputText` overlay
- `ReroutesRenderer`: draws small circles at each waypoint position; selected reroutes get a highlight ring
- **Drag with contents**: when `Mode = DraggingComment`, update `CommentDragOverridePositions` for comment AND `DragOverridePositions` for each `CommentDragContents` node
- On LMB-up during DraggingComment: flush with a batch command (`UpdateComment` + `MoveNodes`)
- Resize on LMB-up: dispatch `UpdateComment(id, newPosition, newSize)`
- Shift+drag = move comment alone; Alt+drag = move only contents

---

### Task 3: Action API (TASK-P5-001)

**Task Definition:** See `.dev/final/instructions/03-09-task-action-api.md`  
**Spec:** `.dev/final/specs/D0-action-api.md`

**Files to create:**
```
src/NodeEditor.Core/Action/EditorCommandsImpl.cs
src/NodeEditor.Core/Action/EditorIndicatorsImpl.cs
src/NodeEditor.Core/Action/CommandRegistration.cs
src/NodeEditor.Core/Action/ToastQueue.cs
src/NodeEditor.UI/Action/BuiltinCommandHandlers.cs
src/NodeEditor.UI/Action/CanvasCommands.cs
src/NodeEditor.UI/Action/EditCommands.cs
src/NodeEditor.UI/Action/ViewCommands.cs
```

**Key requirements:**
- `EditorCommandsImpl`: Dictionary-based registry, `Get`, `Invoke` (checks IsEnabled, catches exceptions), `Register`, `NotifyAvailabilityChanged`, `AvailabilityChanged` event
- `EditorIndicatorsImpl`: wraps `ToastQueue`, stores `EditorStatusSnapshot`, raises `Changed` on snapshot update
- `ToastQueue`: simple `Queue<EditorNotification>` with Enqueue/TryDequeue/Count
- `CommandRegistration`: ergonomic fluent builder for registering commands (see task brief)
- `BuiltinCommandHandlers.RegisterAll(...)`: registers Undo, Redo, Delete, SelectAll + zoom/find commands using `CommandCatalog` constants
- Check `IEditorCommands` and `IEditorIndicators` interfaces in Core for exact method signatures before implementing

**Test to add:** `tests/NodeEditor.Core.Tests/Action/EditorCommandsImplTests.cs`:
- `Register_Then_Get_ReturnsDescriptor`
- `Invoke_DisabledCommand_ReturnsFalse`
- `Invoke_UnknownCommand_ReturnsFalse`
- `Invoke_Succeeds_CallsAction`

---

## 🧪 Testing Requirements

| Test File | Tests |
|-----------|-------|
| `Find/FindQueryParserTests.cs` | 4 facts |
| `Action/EditorCommandsImplTests.cs` | 4 facts |

**All 59 previous tests must still pass.**

---

## 📊 Report Requirements

Submit `.dev/final/reports/BATCH-05-REPORT.md`:

```markdown
# BATCH-05 Report

## Tasks Completed
[TASK-P4-004 ✅/❌, TASK-P4-005 ✅/❌, TASK-P5-001 ✅/❌]

## Build & Test Results
[dotnet build: 0 errors 0 warnings; dotnet test: exact counts]

## Developer Insights
1. Issues encountered:
2. Weak points spotted:
3. Design decisions beyond spec:

## Files Created/Modified
[list]
```

---

## ✅ Success Criteria

- [ ] `dotnet build` exits 0, 0 errors, 0 warnings
- [ ] `dotnet test` exits 0, ≥67 tests pass (59 previous + 8 new)
- [ ] `FindBar.Draw()`, `FindEngine.Search()`, `FindQueryParser.Parse()` exist
- [ ] `InteractionState` has `CommentDragOverridePositions` and `RenamingComment`
- [ ] `EditorCommandsImpl.Invoke` and `EditorIndicatorsImpl.UpdateSnapshot` exist
- [ ] `BuiltinCommandHandlers.RegisterAll` exists
- [ ] `BATCH-05-REPORT.md` submitted
