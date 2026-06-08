# BATCH-00: Cleanup — Delete dead `GraphEditorWindow`

**Batch Number:** BATCH-00
**Tasks:** Cleanup (delete dead `GraphEditorWindow`)
**Phase:** Prep/Cleanup
**Estimated Effort:** 1-2h
**Priority:** MEDIUM
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Simple cleanup batch: delete the dead `GraphEditorWindow` placeholder and all its references. This eliminates a false "no canvas exists" trap before the real debug UX work begins in Batch A.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE_claude.md` — How to work with batches
2. **Task Details:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch 0 section
3. **Onboarding:** `.dev/blueprint-dbg-1/ONBOARDING.md` — Full context

### Source Code Location
- **Primary Work Area:** `Hrot/Subsystems/Blueprints/`
- **Test Project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Report Submission
**When done, submit your report to:**
`.dev/blueprint-dbg-1/reports/BATCH-00-REPORT.md`

---

## Context

`GraphEditorWindow` is a dead placeholder (`ImGui.TextDisabled` + `TODO(D-BP-04)`), never registered in production (its registrar `BlueprintWindowRegistrar` returns `null` at `EditorSubsystem.cs:441-444`, AIE-015). It caused a false "no canvas exists" read. Removing it eliminates the trap before the real debug UX work.

The real canvas is `AiGraphCanvasWindow` which hosts the live blueprint editor.

---

## 🎯 Batch Objectives

Remove all traces of the dead `GraphEditorWindow` — the file itself, its tests, its registrar entry, and any remaining references. Keep the `Hrot.Blueprints.Editor.GraphEditor` *namespace* (used by live host services).

---

## ✅ Tasks

### Task 1: Delete `GraphEditorWindow.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` — DELETE ENTIRE FILE

### Task 2: Remove tests for `GraphEditorWindow`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs`

Delete the 4 `GraphEditorWindow_*` tests (constructor/title/selection/null-arg). If the file has **only** those 4 tests, delete the entire file; otherwise delete just those methods.

### Task 3: Remove registrar entry

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs:60`

Remove the `() => new GraphEditorWindow(...)` registration. Adjust any count/array accordingly.

### Task 4: Update registrar test

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs`

The `RegistersAllSevenWindows` test: drop `GraphEditorWindow` from the expected set (→ six windows) and rename if it asserts a count.

### Task 5: Verify nothing else references it

Run: `grep -r GraphEditorWindow` across the repo. Should return only references that are being deleted in this batch (or none after deletion). Report any unexpected references.

### Task 6 (conditional): Mark D-BP-04 superseded

If `.dev/breakpoints-1/DEBT-TRACKER.md` exists, mark **D-BP-04 SUPERSEDED**. If the file does not exist, note it and skip.

### Task 7: Retrofit old breakpoint menu populator if orphaned

Check `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintBreakpointMenuPopulator.cs` — if it was ONLY referenced by the deleted `GraphEditorWindow`, it is now orphaned. Delete it if no other references exist.

---

## 🧪 Testing Requirements

**Gates (all must pass):**
- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors, 0 warnings
- `Hrot.Blueprints.Tests` → **7 pre-existing failures, 0 new**
- `Hrot.Editor.AiShared.Tests` → all pass
- `EditorSubsystemBoot` → 10/10

**Important:** Editor must be CLOSED (DLL locks). Don't regenerate golden snapshots (`BLUEPRINT_REGENERATE_SNAPSHOTS` must NOT be set).

---

## 📊 Report Requirements

Fill every section per DEV-GUIDE_claude.md §4.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] GraphEditorWindow.cs deleted
- [ ] All GraphEditorWindow tests removed
- [ ] Registrar entry removed
- [ ] Registrar test updated
- [ ] No other references to GraphEditorWindow exist
- [ ] Build 0/0, Blueprints tests 7/0-new, AiShared tests pass, boot 10/10

---

## ⚠️ Common Pitfalls to Avoid

- **Do NOT delete the `GraphEditor` namespace** (`CommandHistory`, `GraphCommands`, `IGraphCommand`, `SelectionState`) — used by live host services.
- **Do NOT delete `BlueprintEditorWindowBase`** — base of all live windows.
- **Do NOT touch `BlueprintWindowRegistrar` beyond removing the one entry** — it's retired in production but still DI-registered; that's a separate cleanup out of scope.
- Report the full failure-set by name from Blueprints tests.

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch 0 section
- **Onboarding:** `.dev/blueprint-dbg-1/ONBOARDING.md`
