# BATCH-07 Instructions

**Batch:** BATCH-07  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-U004 · PACK2-F001  
**Branch:** main (append directly)

---

## Context

- `Hrot.ScenarioEditor` is the tool/rendering layer — NO `Hrot.NED` or CycloneDDS direct references.
- `Hrot.Editor` (new project) is the UI/application layer — also must NOT have a direct `Hrot.NED` dependency.
- `IEditorLogic` provides the facade interface between panels and business logic.
- `ScenarioFileService` (in `Hrot.ScenarioEditor/Services/`) already exists with `NewScenario`, `SaveScenario`, `LoadScenario`.
- `DerRepo`/`IDerRepo` is from `FDP.Toolkit.DER` — a non-ECS view repo used by ExCon panels.
- Panel test pattern: expose a `HandleXxxClick(IEditorLogic logic)` method that is the actual logic path; `DrawContent(IEditorLogic logic)` calls ImGui and delegates to it. Tests call `HandleXxxClick` directly without ImGui context.
- `ScenarioSerializerBuilder` does NOT require any translators (auto-serializer handles basic component types). `HrotEntityScenarioTranslator` referenced in the design docs does NOT exist yet — use no translators for F001 scaffolding.
- Add `Hrot.Editor` and `Hrot.Editor.Tests` to `IOS-IG-SimHost.sln` using `dotnet sln add`.

---

## Task A: PACK2-U004 — Scaffold Hrot.Editor Project

### A.1 — Create `Hrot.Editor/Hrot.Editor.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Hrot.Editor.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Hrot.ScenarioEditor\Hrot.ScenarioEditor.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.DER\FDP.Toolkit.DER.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImgui-cs" Version="3.2.0" />
    <PackageReference Include="ImGuiNET" Version="1.91.6.1" />
    <PackageReference Include="NLog" Version="5.2.8" />
  </ItemGroup>
</Project>
```

> **NOTE:** Check what `ImGuiNET` version is used in other projects before hardcoding. Look at `Hrot.IG.csproj` or `Hrot.ExCon.csproj` for the version in use. Use the same version.
>
> Also check if `FDP.Toolkit.DER.csproj` path is correct. It should be in `FDP/Toolkits/FDP.Toolkit.DER/`.

### A.2 — Create `Hrot.Editor/EditorTool.cs`

```csharp
namespace Hrot.Editor;

/// <summary>
/// Identifies the currently active interactive tool in the HROT Editor.
/// Used with <see cref="IEditorLogic.ActivateTool"/>.
/// </summary>
public enum EditorTool
{
    /// <summary>Standard selection + drag mode (default).</summary>
    Select,
    /// <summary>Entity placement / spawn mode (activates <c>CreationTool</c>).</summary>
    Spawn,
    /// <summary>Vertex edit mode for overlay shapes (activates <c>EditTool</c>).</summary>
    Edit,
    /// <summary>Route waypoint edit mode (activates <c>RouteEditTool</c>).</summary>
    Route,
    /// <summary>Measurement line mode (activates <c>MeasureTool</c>).</summary>
    Measure,
}
```

### A.3 — Create `Hrot.Editor/IEditorLogic.cs`

```csharp
using System.Collections.Generic;
using FDP.Toolkit.DER;

namespace Hrot.Editor;

/// <summary>
/// Application-level facade exposed to all HROT Editor UI panels.
/// Panels must only call methods on this interface — no direct access to
/// <c>FdpEventBus</c>, <c>EntityRepository</c>, <c>ScenarioEditorModule</c>,
/// or any DDS type is permitted in panel code.
/// </summary>
public interface IEditorLogic
{
    /// <summary>Clears the world and resets time to zero.</summary>
    void NewScenario();

    /// <summary>Serializes current world state to <paramref name="filePath"/>.</summary>
    void SaveScenario(string filePath);

    /// <summary>
    /// Clears the world, then deserializes entities from <paramref name="filePath"/>.
    /// </summary>
    void LoadScenario(string filePath);

    /// <summary>Activates the specified interactive tool.</summary>
    void ActivateTool(EditorTool tool);

    /// <summary>
    /// Publishes an <c>UpdateEntityCommand</c> for <paramref name="networkId"/>
    /// with the supplied component replacements.
    /// </summary>
    void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents);

    /// <summary>Read-only non-ECS view of the current entity set (for panels).</summary>
    IDerRepo View { get; }
}
```

### A.4 — Create `Hrot.Editor/EditorApplication.cs`

This is the `IEditorLogic` implementation used by panels. It delegates to `ScenarioFileService` for file operations and publishes FDP events for tool/command operations. `DerRepo` is held as an empty stub for now (future sync work).

```csharp
using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.DER;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// Implements <see cref="IEditorLogic"/> by delegating file operations to
/// <see cref="ScenarioFileService"/> and tool/command operations to
/// <see cref="FdpEventBus"/> events.
///
/// <para>
/// Panels must bind exclusively to <see cref="IEditorLogic"/> — no direct
/// field references to this class or any ECS/DDS types.
/// </para>
/// </summary>
public sealed class EditorApplication : IEditorLogic
{
    private readonly ScenarioFileService _fileService;
    private readonly FdpEventBus         _bus;
    private readonly EntityRepository   _world;
    private readonly DerRepo             _view = new(localNodeId: 0);

    public IDerRepo View => _view;

    public EditorApplication(
        ScenarioFileService fileService,
        FdpEventBus bus,
        EntityRepository world)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _world       = world       ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc/>
    public void NewScenario() => _fileService.NewScenario(_world);

    /// <inheritdoc/>
    public void SaveScenario(string filePath) => _fileService.SaveScenario(_world, filePath);

    /// <inheritdoc/>
    public void LoadScenario(string filePath) => _fileService.LoadScenario(_world, filePath);

    /// <inheritdoc/>
    public void ActivateTool(EditorTool tool)
    {
        // Publish an FDP-managed event that the active tool controller listens for.
        // The actual tool switch logic lives in ScenarioEditorModule / IgApplication.
        _bus.PublishManaged(new ActivateEditorToolEvent(tool));
    }

    /// <inheritdoc/>
    public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents)
    {
        if (updatedComponents == null) throw new ArgumentNullException(nameof(updatedComponents));
        _bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = networkId,
            UpdatedComponents  = updatedComponents,
        });
    }
}
```

> **IMPORTANT — UpdateEntityCommand:** Check if `UpdateEntityCommand` is already defined in `FDP.Toolkit.NetworkSpawning.Events` or `Hrot.Map.Common.Commands`. Look at how `CycloneEgressSystem` or `DestroyEntityCommandEgressTranslator` uses it. Do NOT create a new type; find and use the existing one. Check `Hrot.Map.Common/Commands/` or `FDP.Toolkit.NetworkSpawning/Events/`.

> **IMPORTANT — ActivateEditorToolEvent:** This event type does NOT exist yet. Create it in `Hrot.Editor/Events/ActivateEditorToolEvent.cs`:
> ```csharp
> namespace Hrot.Editor.Events;
> public sealed class ActivateEditorToolEvent
> {
>     public EditorTool Tool { get; init; }
>     public ActivateEditorToolEvent(EditorTool tool) { Tool = tool; }
> }
> ```

> The `CommitPropertyEdit` path uses `UpdateEntityCommand`. If `UpdateEntityCommand.UpdatedComponents` has a different property name (e.g. `Components`, `Patches`), adapt accordingly. Read the actual class definition before writing.

### A.5 — Create UI Panels (four files)

All panels follow the OrbatPanel pattern: expose testable `HandleXxxClick(IEditorLogic logic)` methods alongside `DrawContent(IEditorLogic logic)` that calls ImGui. Panels must NOT hold any field of type `FdpEventBus`, `EntityRepository`, `ScenarioEditorModule`, or DDS types.

#### `Hrot.Editor/UI/ScenarioBrowserPanel.cs`

```csharp
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Editor panel providing New / Save / Load file operations.
/// Delegates all actions to <see cref="IEditorLogic"/>; no direct bus or repo access.
/// </summary>
public sealed class ScenarioBrowserPanel
{
    private string _saveLoadPath = "scenario.json";

    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleNewClick(IEditorLogic logic) => logic.NewScenario();

    public void HandleSaveClick(IEditorLogic logic) => logic.SaveScenario(_saveLoadPath);

    public void HandleLoadClick(IEditorLogic logic) => logic.LoadScenario(_saveLoadPath);

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        ImGui.InputText("Path", ref _saveLoadPath, 512);
        ImGui.Separator();
        if (ImGui.Button("New"))  HandleNewClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Save")) HandleSaveClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Load")) HandleLoadClick(logic);
    }
}
```

#### `Hrot.Editor/UI/EditorToolbarPanel.cs`

```csharp
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Editor toolbar panel for tool mode selection.
/// Delegates all tool activation to <see cref="IEditorLogic"/>.
/// </summary>
public sealed class EditorToolbarPanel
{
    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleSpawnClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Spawn);
    public void HandleSelectClick(IEditorLogic logic) => logic.ActivateTool(EditorTool.Select);
    public void HandleEditClick(IEditorLogic logic)   => logic.ActivateTool(EditorTool.Edit);
    public void HandleRouteClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Route);

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        ImGui.Text("Tools");
        ImGui.Separator();
        if (ImGui.Button("Select"))       HandleSelectClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Place Entity")) HandleSpawnClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Edit Shape"))   HandleEditClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Edit Route"))   HandleRouteClick(logic);
    }
}
```

#### `Hrot.Editor/UI/EntityPropertyInspector.cs`

```csharp
using System.Collections.Generic;
using FDP.Toolkit.DER;
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Panel that displays and edits properties of the currently selected entity.
/// Reads from <see cref="IEditorLogic.View"/>; commits via
/// <see cref="IEditorLogic.CommitPropertyEdit"/>.
/// </summary>
public sealed class EntityPropertyInspector
{
    private long _selectedNetworkId;

    // ── Testable handler ──────────────────────────────────────────────────────

    public void HandleCommitEdit(IEditorLogic logic, long networkId,
        IReadOnlyList<object> components)
    {
        logic.CommitPropertyEdit(networkId, components);
    }

    public void SetSelectedEntity(long networkId) { _selectedNetworkId = networkId; }

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        var entity = logic.View.GetEntity((int)_selectedNetworkId);
        if (entity == null)
        {
            ImGui.Text("No entity selected.");
            return;
        }

        ImGui.Text($"Entity ID: {entity.EntityId}");
        ImGui.Text($"Name: {entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>()?.Name ?? "(unknown)"}");
        // Property editing committed via HandleCommitEdit in response to user interaction.
    }
}
```

> **NOTE on EntityInfo:** This panel is a scaffold. If importing `Hrot.NED.Descriptors.EntityInfo` introduces a transitive NED dep into the project, comment it out and use a plain name display from `IDerEntity.Name` or similar property. Check `IDerEntity` first. Prefer using `IDerEntity` properties that don't require NED types. The key constraint is that `Hrot.Editor.csproj` must NOT have a `Hrot.NED` project reference.

#### `Hrot.Editor/UI/EditorOrbatPanel.cs`

```csharp
using System.Linq;
using FDP.Toolkit.DER;
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Panel that displays the entity hierarchy for the current scenario.
/// Reads from <see cref="IEditorLogic.View"/> exclusively.
/// </summary>
public sealed class EditorOrbatPanel
{
    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        var entities = logic.View.GetAllEntities().ToList();

        ImGui.Text($"Entities ({entities.Count})");
        ImGui.Separator();

        foreach (var entity in entities)
        {
            ImGui.Text($"• [{entity.EntityId}]");
        }
    }
}
```

### A.6 — Create `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Hrot.Editor\Hrot.Editor.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Moq" Version="4.20.72" />
  </ItemGroup>
</Project>
```

> Check version numbers used in other test projects to ensure consistency. Look at `Hrot.ScenarioEditor.Tests.csproj` for reference. 

### A.7 — Create panel tests

**File:** `Hrot.Editor.Tests/ScenarioBrowserPanelTests.cs`

```csharp
using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class ScenarioBrowserPanelTests
{
    [Fact]
    public void HandleNewClick_CallsNewScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleNewClick(mock.Object);
        mock.Verify(l => l.NewScenario(), Times.Once);
    }

    [Fact]
    public void HandleSaveClick_CallsSaveScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleSaveClick(mock.Object);
        mock.Verify(l => l.SaveScenario(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void HandleLoadClick_CallsLoadScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleLoadClick(mock.Object);
        mock.Verify(l => l.LoadScenario(It.IsAny<string>()), Times.Once);
    }
}
```

**File:** `Hrot.Editor.Tests/EditorToolbarPanelTests.cs`

```csharp
using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class EditorToolbarPanelTests
{
    [Fact]
    public void HandleSpawnClick_ActivatesSpawnTool()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new EditorToolbarPanel();
        panel.HandleSpawnClick(mock.Object);
        mock.Verify(l => l.ActivateTool(EditorTool.Spawn), Times.Once);
    }

    [Fact]
    public void HandleSelectClick_ActivatesSelectTool()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new EditorToolbarPanel();
        panel.HandleSelectClick(mock.Object);
        mock.Verify(l => l.ActivateTool(EditorTool.Select), Times.Once);
    }
}
```

**File:** `Hrot.Editor.Tests/EntityPropertyInspectorTests.cs`

```csharp
using System.Collections.Generic;
using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class EntityPropertyInspectorTests
{
    [Fact]
    public void HandleCommitEdit_CallsCommitPropertyEdit()
    {
        var mock       = new Mock<IEditorLogic>();
        var panel      = new EntityPropertyInspector();
        var components = new List<object> { "SomeComponent" };
        panel.HandleCommitEdit(mock.Object, networkId: 42L, components: components);
        mock.Verify(l => l.CommitPropertyEdit(42L, components), Times.Once);
    }
}
```

### A.8 — Compile-time dependency check test

**File:** `Hrot.Editor.Tests/EditorDependencyTests.cs`

```csharp
using System.Linq;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Verifies PACK2-U004 constraint: Hrot.Editor has no transitive dependency on Hrot.NED.
/// </summary>
public class EditorDependencyTests
{
    [Fact]
    public void HrotEditor_HasNoTransitiveNedDependency()
    {
        var assemblyNames = typeof(Hrot.Editor.IEditorLogic).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Hrot.NED", assemblyNames);
    }
}
```

### A.9 — Add Hrot.Editor and Hrot.Editor.Tests to the solution

Run from the workspace root:
```
dotnet sln IOS-IG-SimHost.sln add Hrot.Editor/Hrot.Editor.csproj
dotnet sln IOS-IG-SimHost.sln add Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```

---

## Task B: PACK2-F001 — Instantiate the Purified Serializer in the Editor Bootstrap

### B.1 — Create `Hrot.Editor/EditorBootstrap.cs`

This class provides the `ScenarioFileService` construction, isolating the composition from `EditorApplication`. This is the scaffold for the composition root (full `Program.cs` wiring is Phase 5/C001).

```csharp
using FDP.Toolkit.Scenario;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// Static factory for constructing the Editor's core services.
/// The full composition root (Raylib window, module kernel) lives in <c>Program.cs</c>
/// and is wired up in Phase 5 (PACK2-C001).
/// </summary>
public static class EditorBootstrap
{
    /// <summary>
    /// Builds a <see cref="ScenarioFileService"/> with an auto-serializer
    /// configured for <c>"Hrot.Scenario"</c> subsystem type.
    /// </summary>
    public static ScenarioFileService CreateFileService()
    {
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario")
            // No custom translators yet; FdpAutoSerializer handles all registered component types.
            .Build();

        return new ScenarioFileService(serializer);
    }
}
```

### B.2 — Add a bootstrap test

**File:** `Hrot.Editor.Tests/EditorBootstrapTests.cs`

```csharp
using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests;

public class EditorBootstrapTests
{
    [Fact]
    public void CreateFileService_ReturnsNonNullService()
    {
        var service = EditorBootstrap.CreateFileService();
        Assert.NotNull(service);
    }
}
```

---

## Verification Checklist

1. **Build:** `dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**
2. **Tests:**
   - `dotnet test Hrot.Editor.Tests --no-build` → all pass (min. 6+ tests)
3. **No-NED check:** `EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency` passes.
4. **ScenarioEditor tests unchanged:** `dotnet test Hrot.ScenarioEditor.Tests --no-build` → still 14/14

---

## Report Format

1. **Task completion table** (sub-tasks A.1–A.9 and B.1–B.2).
2. **Q1:** Was `UpdateEntityCommand` found in `Hrot.Map.Common.Commands` or `FDP.Toolkit.NetworkSpawning.Events`? What namespace was used in `EditorApplication`?
3. **Q2:** Was `ImGuiNET` available as a transitive dependency of `Hrot.ScenarioEditor`, or did it need to be added to `Hrot.Editor.csproj` directly?
4. **Q3:** Did `EntityPropertyInspector` need to import `Hrot.NED.Descriptors`? If yes, how was the constraint satisfied (DerRepo ID-only display, or something else)?
5. **Q4:** What was the `IDerEntity` interface shape — does it expose `EntityId`, `Name`? Give the actual accessible properties found.
6. **Test counts table** (project, before, after, delta).
