# BATCH-07 Review

**Batch:** BATCH-07  
**Tasks:** PACK2-U004, PACK2-F001  
**Reviewer:** GitHub Copilot (dev-lead)  
**Decision:** ✅ APPROVED

---

## Build Verification

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln --no-incremental` | ✅ 0 errors, 336 warnings (all pre-existing) |

---

## Test Verification

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.Editor.Tests` | — (new) | 8 pass | +8 |
| Other suites | unchanged | unchanged | 0 |

`EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency` ✅ passes.

---

## Scope Check

### PACK2-U004 — Scaffold Hrot.Editor ✅
- `Hrot.Editor/Hrot.Editor.csproj` — no `Hrot.NED` reference, NED-free chain verified
- `EditorTool.cs` enum — Select, Spawn, Edit, Route, Measure
- `IEditorLogic.cs` interface — NewScenario, SaveScenario, LoadScenario, ActivateTool, CommitPropertyEdit, View
- `EditorApplication.cs` — implements IEditorLogic; delegates to ScenarioFileService + FdpEventBus events
- `ActivateEditorToolEvent.cs` in `Hrot.Editor/Events/`
- 4 panels: ScenarioBrowserPanel, EditorToolbarPanel, EntityPropertyInspector, EditorOrbatPanel
- `Hrot.Editor.Tests/` project with 8 tests covering all panel handlers + bootstrap + dependency check
- Both projects added to `IOS-IG-SimHost.sln`

### PACK2-F001 — Serializer Bootstrap ✅
- `EditorBootstrap.CreateFileService()` — builds `ScenarioSerializer("Hrot.Scenario")` with no custom translators
- `EditorBootstrapTests.CreateFileService_ReturnsNonNullService` — confirms instantiation

---

## Quality Notes

- `UpdateEntityCommand.ComponentsToUpdate` (actual property name) vs `UpdatedComponents` (instructions assumption) — correctly handled
- ImGuiNET version conflict resolved cleanly (flows transitively)
- `IDerEntity.Name` doesn't exist → EntityPropertyInspector uses `EntityId + TkbType` display (NED-free, correct)
- `EditorApplication.CommitPropertyEdit` correctly converts `IReadOnlyList<object>` to `List<object>` for UpdateEntityCommand

---

## Suggested Commit Message

```
feat(packs-2): PACK2-U004 + PACK2-F001 -- Hrot.Editor scaffold + serializer bootstrap

U004: New Hrot.Editor project: IEditorLogic + EditorApplication + 4 panels
      (ScenarioBrowserPanel, EditorToolbarPanel, EntityPropertyInspector, EditorOrbatPanel)
      + ActivateEditorToolEvent + EditorTool enum; no NED dependency confirmed
F001: EditorBootstrap.CreateFileService() wires ScenarioSerializerBuilder

Tests: 8/8 Hrot.Editor.Tests (incl. NED dependency check)
```
