# Onboarding — Component Editor (`comp-edit-1`)

Welcome to the `comp-edit-1` workstream. This guide orients a developer who is new to
this area of the codebase.

---

## What is Being Built

An **interactive component editor** for the FDP Entity Inspector and Entity Watch panels.
When the operator double-clicks a component row in the read-only property table, a floating,
non-blocking editor window opens. The editor uses the `StructEdit` library to clone the full
component state, render it as an editable two-column table, and write the result atomically
back to the ECS only when the operator clicks OK.

The feature spans three layers:

1. **StructEdit** (`FDP/ExtDeps/StructEdit/`) — extended to generate a complete tree of
   `EditNode` objects for array elements, and to carry domain-specific field attributes
   through to the UI.
2. **`Fdp.Presentation`** (`FDP/Engine/Fdp.Presentation/`) — a new `ComponentEditDrawer`
   (ImGui renderer) and `ComponentEditWindow` (volatile ManagedWindow) are added, plus
   lightweight picker infrastructure.
3. **Wiring** — `ComponentReflector` gains double-click detection; `EntityInspectorPanel`
   and `EntityWatchPanel` expose their `Reflector` so host subsystems can inject the
   `WindowManager` and optional picker context.

---

## Planning Artifacts

| File | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architecture, phases, constraints, decisions |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Folder Layout

### StructEdit (the edit-session library)

```
FDP/ExtDeps/StructEdit/src/
  StructEdit.Core/
    Bindings/            <- IValueBinding implementations; add NestedMemberBinding here
    Attributes/          <- EditRangeAttribute, EditUnitAttribute, etc.
    EditNodeMetadata.cs  <- Add CustomAttributes property here
    IEditSession.cs      <- Session API (MarkStructuralChange, RebuildDocument, Commit)
    IContainerBinding.cs <- Array operations (Resize, GetElementBinding, CanResize)
  StructEdit.Reflection/
    ReflectionEditDocumentBuilder.cs  <- Extend to generate array element nodes
```

### Fdp.Presentation (the UI layer)

```
FDP/Engine/Fdp.Presentation/
  Fdp.Presentation.csproj      <- Add StructEdit.Core + StructEdit.Reflection references
  ImGui/
    Editing/                   <- NEW sub-folder for all editor-specific types
      PickerAttributes.cs      <- [MapPickableEntity], [MapPickableWorldLocation]
      IComponentPickerContext.cs
      ComponentEditDrawer.cs   <- Recursive ImGui renderer
      ComponentEditWindow.cs   <- Volatile ManagedWindow
    Utils/
      ComponentReflector.cs    <- Add double-click trigger + injectable properties
      ImGuiPropertyTree.cs     <- Read-only tree (unchanged; used for visual style reference)
    Panels/
      EntityInspectorPanel.cs  <- Add public Reflector property
      EntityWatchPanel.cs      <- Add public Reflector property
    WindowManager/
      ManagedWindow.cs         <- Base class (IsVolatile, ShowInMenu pattern)
      WindowManager.cs         <- TryGetWindow, FocusWindow, RegisterWindow
```

### Tests

```
FDP/Engine/Fdp.Presentation.Tests/
  ImGui/
    Editing/                   <- NEW; place ComponentEditDrawer and ComponentEditWindow tests
    EntityInspectorPanelTests.cs  <- Must remain passing after TASK-CE10

FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/
    <- Existing tests (must pass after CE01/CE02/CE03)
    <- Add new test classes for Phase 1 tasks here
```

---

## Build and Run Tests

Build the whole solution:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore
```

Build only FDP (faster for Phase 1/2/3 work):

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/FDP.sln --no-restore
```

Run StructEdit tests:

```powershell
dotnet test FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/StructEdit.Tests.csproj
```

Run Fdp.Presentation tests:

```powershell
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj
```

Run all tests:

```powershell
dotnet test IOS-IG-SimHost.sln
```

---

## Development Workflow

Read `.github/skills/developer/SKILL.md` to understand the batch-based development workflow
used in this project. Tasks are grouped into batches, implemented following the spec in
[TASK-DETAIL.md](./TASK-DETAIL.md), and reported via a batch report before review.

Key points for this workstream:

- Implement **Phase 1 first** — all UI work in Phases 3 and 4 depends on the extended
  StructEdit data model.
- **Phase 2 can proceed in parallel with Phase 1** if working on separate branches.
- **Phase 3 depends on Phase 1 and Phase 2** (the drawer reads `CustomAttributes` and
  requires array element nodes).
- **Phase 4 depends on Phase 3** (registers `ComponentEditWindow`).
- Every task has explicit success conditions expressed as unit tests. Write the tests and
  make them pass before marking a task done.
- The double-click trigger (TASK-CE09) tests require ImGui context; use the existing
  `xunit.runner.json` (no parallel execution) pattern in `Fdp.Presentation.Tests`.
