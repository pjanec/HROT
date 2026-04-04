# BATCH-07 Report

**Batch:** BATCH-07  
**Tasks:** PACK2-U004 · PACK2-F001  
**Date:** 2026-04-04  
**Developer:** GitHub Copilot

---

## Task Completion Table

| Sub-task | Description | Status |
|----------|-------------|--------|
| A.1 | Create `Hrot.Editor/Hrot.Editor.csproj` | ✅ Done |
| A.2 | Create `Hrot.Editor/EditorTool.cs` | ✅ Done |
| A.3 | Create `Hrot.Editor/IEditorLogic.cs` | ✅ Done |
| A.4 | Create `Hrot.Editor/EditorApplication.cs` + `Events/ActivateEditorToolEvent.cs` | ✅ Done |
| A.5 | Create 4 UI panels (ScenarioBrowserPanel, EditorToolbarPanel, EntityPropertyInspector, EditorOrbatPanel) | ✅ Done |
| A.6 | Create `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` | ✅ Done |
| A.7 | Create panel tests (ScenarioBrowserPanelTests, EditorToolbarPanelTests, EntityPropertyInspectorTests) | ✅ Done |
| A.8 | Create `EditorDependencyTests.cs` (no-NED compile-time check) | ✅ Done |
| A.9 | Add both projects to `IOS-IG-SimHost.sln` | ✅ Done |
| B.1 | Create `Hrot.Editor/EditorBootstrap.cs` | ✅ Done |
| B.2 | Create `Hrot.Editor.Tests/EditorBootstrapTests.cs` | ✅ Done |

---

## Q1 — UpdateEntityCommand location

`UpdateEntityCommand` was found in **`FDP.Toolkit.NetworkSpawning.Events`** (namespace `FDP.Toolkit.NetworkSpawning.Events`).

The actual class definition differs from the instructions:
- Instructions showed `UpdatedComponents` → actual field name is **`ComponentsToUpdate`** (`List<object>`)
- Instructions showed `IReadOnlyList<object>` in the command → actual is `List<object>`

Adaptation in `EditorApplication.CommitPropertyEdit`:
```csharp
_bus.PublishManaged(new UpdateEntityCommand
{
    NetworkId          = networkId,
    ComponentsToUpdate = new List<object>(updatedComponents),
});
```

---

## Q2 — ImGuiNET dependency

`ImGui.NET` (the NuGet package providing the `ImGuiNET` namespace) was **NOT** needed as an explicit direct reference.  
It is provided **transitively** through `rlImgui-cs 3.2.0`, which declares a dependency on `ImGui.NET >= 1.91.6.1`.

Initially an explicit `<PackageReference Include="ImGui.NET" Version="1.91.0.1" />` was added (matching `FDP.Toolkit.ImGui`), but this caused a **NU1605 package downgrade error** because `rlImgui-cs 3.2.0` requires `>= 1.91.6.1`. The explicit reference was removed; `rlImgui-cs` resolves to `1.91.6.1` and `ImGuiNET` types are fully available in panel code.

---

## Q3 — EntityPropertyInspector and Hrot.NED

`EntityPropertyInspector` does **NOT** import `Hrot.NED.Descriptors.EntityInfo`.

The instructions' draft used `entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>()?.Name`, which would have introduced a transitive NED dependency. Since `IDerEntity` has no `Name` property (see Q4), and using `EntityInfo` would violate the no-NED constraint, the panel was implemented using only `IDerEntity`-level members:

```csharp
ImGui.Text($"Entity ID: {entity.EntityId}");
ImGui.Text($"TKB Type: {entity.TkbType}");
```

The `EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency` test confirms the constraint is satisfied.

---

## Q4 — IDerEntity interface shape

Located at `FDP/Toolkits/FDP.Toolkit.DER/IDerEntity.cs`. Accessible properties and methods:

| Member | Type | Notes |
|--------|------|-------|
| `EntityId` | `int` | Network entity ID |
| `TkbType` | `long` | TKB entity type ID |
| `GetDescriptor<T>(int partId = 0)` | `T?` | Returns descriptor or default |
| `SetDescriptor<T>(T descriptor, int partId = 0)` | `void` | Replaces existing |
| `HasDescriptor<T>(int partId = 0)` | `bool` | Presence check |
| `GetAllDescriptorTypes()` | `IEnumerable<Type>` | All attached descriptor types |
| `GetAllRawDescriptors()` | `IEnumerable<(Type, int, object)>` | All raw descriptors with type+partId |

**No `Name` property exists on `IDerEntity`.**

---

## Test Counts Table

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| Hrot.Editor.Tests | 0 (new) | 8 | +8 |
| Hrot.ScenarioEditor.Tests | 14 | 14 | 0 |

### Hrot.Editor.Tests breakdown (8 tests, all passed):
- `ScenarioBrowserPanelTests.HandleNewClick_CallsNewScenario`
- `ScenarioBrowserPanelTests.HandleSaveClick_CallsSaveScenario`
- `ScenarioBrowserPanelTests.HandleLoadClick_CallsLoadScenario`
- `EditorToolbarPanelTests.HandleSpawnClick_ActivatesSpawnTool`
- `EditorToolbarPanelTests.HandleSelectClick_ActivatesSelectTool`
- `EntityPropertyInspectorTests.HandleCommitEdit_CallsCommitPropertyEdit`
- `EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency`
- `EditorBootstrapTests.CreateFileService_ReturnsNonNullService`

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln --no-incremental -v quiet
  0 Error(s)
  336 Warning(s)  (pre-existing xUnit1030 warnings in TimeControlIntegrationTests)
Time Elapsed: 00:00:28
```

**Result: SUCCESS — 0 errors.**

---

## Deviations from Instructions

| Item | Instructed | Actual | Reason |
|------|-----------|--------|--------|
| `UpdateEntityCommand.UpdatedComponents` | `UpdatedComponents` (IReadOnlyList) | **`ComponentsToUpdate`** (List\<object\>) | Actual class field name differs; adapted with `new List<object>(updatedComponents)` |
| `ImGuiNET` package version | `ImGuiNET 1.91.6.1` (explicit) | **Transitive via rlImgui-cs** (no direct ref) | Explicit 1.91.0.1 caused NU1605 downgrade error; removed; comes transitively at 1.91.6.1 |
| `EntityPropertyInspector` name display | `entity.GetDescriptor<EntityInfo>()?.Name` | **`entity.EntityId` + `entity.TkbType`** | `IDerEntity` has no Name; EntityInfo would require Hrot.NED (banned) |
| Test NuGet versions | xunit 2.9.3, TestSdk 17.14.1, runner 2.8.2 | **xunit 2.5.3, TestSdk 17.8.0, runner 2.5.3** | Matched existing test projects for version consistency |

---

## Files Created

| File | Type |
|------|------|
| `Hrot.Editor/Hrot.Editor.csproj` | Project file |
| `Hrot.Editor/EditorTool.cs` | Enum |
| `Hrot.Editor/IEditorLogic.cs` | Interface |
| `Hrot.Editor/EditorApplication.cs` | Implementation |
| `Hrot.Editor/EditorBootstrap.cs` | Static factory |
| `Hrot.Editor/Events/ActivateEditorToolEvent.cs` | Event type |
| `Hrot.Editor/UI/ScenarioBrowserPanel.cs` | UI panel |
| `Hrot.Editor/UI/EditorToolbarPanel.cs` | UI panel |
| `Hrot.Editor/UI/EntityPropertyInspector.cs` | UI panel |
| `Hrot.Editor/UI/EditorOrbatPanel.cs` | UI panel |
| `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` | Test project file |
| `Hrot.Editor.Tests/ScenarioBrowserPanelTests.cs` | Tests |
| `Hrot.Editor.Tests/EditorToolbarPanelTests.cs` | Tests |
| `Hrot.Editor.Tests/EntityPropertyInspectorTests.cs` | Tests |
| `Hrot.Editor.Tests/EditorDependencyTests.cs` | Constraint test |
| `Hrot.Editor.Tests/EditorBootstrapTests.cs` | Tests |

**Modified:** `IOS-IG-SimHost.sln` (added Hrot.Editor and Hrot.Editor.Tests)
