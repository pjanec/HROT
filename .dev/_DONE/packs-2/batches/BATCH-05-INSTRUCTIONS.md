# BATCH-05 Instructions

**Batch:** BATCH-05  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-A001 · PACK2-E004 · PACK2-R002  
**Branch:** main (append directly)

---

## Context

- `Hrot.ScenarioEditor` is a standalone project that does NOT reference `Hrot.NED` or `CycloneDDS` directly.
- `Hrot.IG` references `Hrot.ScenarioEditor` (not vice versa).
- `FdpEventBus` is double-buffered: `PublishManaged<T>()` writes to the write buffer; events become readable after `SwapBuffers()`.
- `ModuleHostKernel.RegisterModule(module)` must be called BEFORE `Initialize()`. After Initialize, use `InstallModuleAsync()` for hot-plug.
- `ModuleHostKernel.GetRegisteredModuleNames()` returns the names of all modules registered before Initialize.
- `HrotEnvironment.CreateGeoTransform()` is in `Hrot.Map.Common` — returns WGS84 transform with Berlin origin.
- `GhostCreationSystem` is `FDP.Toolkit.Replication.Systems.GhostCreationSystem`, takes `NetworkEntityMap` in constructor.
- `ActuatorIntentsEgressPack` is in `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` (NOT in Map.Common).
- `CgfLogicPack` exists in `Hrot.CGF/CgfLogicPack.cs` and takes `(BehaviorRegistry, NetworkEntityMap, VehicleAPI? = null)`.
- `Hrot.ClusterRunner.csproj` already references `Hrot.Map.Common`, `Hrot.CGF`, `Hrot.SimHost`.

---

## Task A: PACK2-A001 — Define `WorldResetEvent` and Hook Selection State

### A.1 — Create `WorldResetEvent.cs`

**File:** `Hrot.ScenarioEditor/Events/WorldResetEvent.cs`

```csharp
namespace Hrot.ScenarioEditor.Events;

/// <summary>
/// Published synchronously before <c>EntityRepository.Clear()</c> when a world reset
/// is triggered (new scenario, load scenario).
/// Consumers flush any cached <see cref="Fdp.Kernel.Entity"/> handles immediately on receipt
/// to prevent stale-pointer access after the repository is wiped.
/// </summary>
public sealed class WorldResetEvent { }
```

### A.2 — Hook `StandardInteractionTool` to flush selection on `WorldResetEvent`

**File:** `Hrot.ScenarioEditor/Tools/StandardInteractionTool.cs`

The `StandardInteractionTool` already has:
- `_selection (DefaultSelectionState)` — stores selected entity handles
- `ClearAllSelections()` — private method that clears ECS SelectionState + DefaultSelectionState

Changes:
1. Add a new `public void FlushForWorldReset()` method that simply calls `ClearAllSelections()`.
   This is the synchronous notification hook (called directly by `ScenarioFileService` before `repo.Clear()`).
2. No FdpEventBus subscription required in the tool itself — the flush is called directly.

```csharp
/// <summary>
/// Clears all selection state in preparation for a world reset.
/// Called by <see cref="Hrot.ScenarioEditor.Services.ScenarioFileService"/> immediately
/// before <see cref="Fdp.Kernel.EntityRepository.Clear()"/> is invoked.
/// Must NOT access any ECS component after this call returns.
/// </summary>
public void FlushForWorldReset()
{
    ClearAllSelections();
}
```

### A.3 — Write tests for `WorldResetEvent` integration

**File:** `Hrot.ScenarioEditor.Tests/WorldResetTests.cs`

Write the following tests (namespace `Hrot.ScenarioEditor.Tests`):

```csharp
using System;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Defaults;
using Hrot.ScenarioEditor.Events;
using Hrot.ScenarioEditor.Tools;
using Xunit;

public class WorldResetTests
{
    [Fact]
    public void FlushForWorldReset_ClearsSelection()
    {
        // Arrange: create a world with one entity having SelectionState
        var world = new EntityRepository();
        world.RegisterComponent<SelectionState>();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        var selection = new DefaultSelectionState();
        // Construct StandardInteractionTool in stub mode (no real canvas)
        // Use the TestHook to drive selection
        var tool = new StandardInteractionTool(world, null!, null!, selection);
        tool.TestHook_SelectEntity(entity, augment: false);
        Assert.NotNull(selection.PrimarySelected);

        // Act
        tool.FlushForWorldReset();

        // Assert
        Assert.Null(selection.PrimarySelected);
        Assert.Empty(selection.SelectedEntities);
    }

    [Fact]
    public void WorldResetEvent_IsPlainClass()
    {
        // Ensure WorldResetEvent can be instantiated and is a reference type
        var evt = new WorldResetEvent();
        Assert.NotNull(evt);
    }
}
```

> **Note:** If `StandardInteractionTool` requires non-null `ISimulationQuery` or `IVisualizerAdapter` in its constructor, pass `null!` (the flush path does not invoke those). Adjust constructor arguments to match the actual signature (inspect the file before writing the test).

---

## Task B: PACK2-E004 — Wire Local Scenario File Operations in `ScenarioEditorModule`

### B.1 — Add `FDP.Toolkit.Scenario` reference to `Hrot.ScenarioEditor.csproj`

**File:** `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj`

Add to the `<ItemGroup>` with ProjectReferences:
```xml
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Scenario\FDP.Toolkit.Scenario.csproj" />
```

### B.2 — Create `ScenarioFileService.cs`

**File:** `Hrot.ScenarioEditor/Services/ScenarioFileService.cs`

This service provides `NewScenario`, `SaveScenario`, and `LoadScenario` operations. It accepts a `StandardInteractionTool` observer for synchronous flush before clear.

```csharp
using System;
using System.IO;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;
using FDP.Toolkit.Time;
using Hrot.ScenarioEditor.Events;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Services;

/// <summary>
/// Provides local scenario file operations: <see cref="NewScenario"/>,
/// <see cref="SaveScenario"/>, and <see cref="LoadScenario"/>.
///
/// <para>
/// All three operations that modify world state publish a <see cref="WorldResetEvent"/>
/// synchronously BEFORE calling <c>repo.Clear()</c> to let consumers (selection managers,
/// active tools) flush any cached <see cref="Entity"/> handles before the repository is wiped.
/// </para>
/// </summary>
public sealed class ScenarioFileService
{
    private static readonly string[] AcceptedSubsystemTypes =
    {
        "Hrot.Scenario",
        "Hrot.SimHost",
        "Hrot.CGF",
    };

    private readonly ScenarioSerializer _serializer;
    private Action? _worldResetObservers;

    public ScenarioFileService(ScenarioSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Register a synchronous callback that is invoked immediately before
    /// <c>repo.Clear()</c> in <see cref="NewScenario"/> and <see cref="LoadScenario"/>.
    /// Use this to flush cached entity handles.
    /// </summary>
    public void RegisterWorldResetObserver(Action callback)
    {
        _worldResetObservers += callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Fires all registered reset observers, then clears the repository.
    /// </summary>
    public void NewScenario(EntityRepository repo)
    {
        if (repo == null) throw new ArgumentNullException(nameof(repo));
        FireWorldReset();
        repo.Clear();
    }

    /// <summary>
    /// Serializes the repository state to a JSON file at <paramref name="filePath"/>.
    /// </summary>
    public void SaveScenario(EntityRepository repo, string filePath)
    {
        if (repo == null)     throw new ArgumentNullException(nameof(repo));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var header = new ScenarioHeader { SubsystemType = "Hrot.Scenario" };
        var dom    = _serializer.Serialize(repo, header);
        File.WriteAllText(filePath, dom.ToJsonString());
    }

    /// <summary>
    /// Loads a scenario from a JSON file into <paramref name="repo"/>.
    /// Fires reset observers and clears repo before deserializing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When the file's <c>SubsystemType</c> header is not recognized.
    /// </exception>
    public void LoadScenario(EntityRepository repo, string filePath)
    {
        if (repo == null)     throw new ArgumentNullException(nameof(repo));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var jsonText = File.ReadAllText(filePath);

        // Validate header before destructively clearing the repo.
        ValidateSubsystemType(jsonText);

        FireWorldReset();
        repo.Clear();

        _serializer.Deserialize(repo, jsonText);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void FireWorldReset()
    {
        _worldResetObservers?.Invoke();
    }

    private static void ValidateSubsystemType(string jsonText)
    {
        // Quick header peek: deserialize only enough to check SubsystemType.
        // We use System.Text.Json for a low-allocation peek.
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
        if (!doc.RootElement.TryGetProperty("Header", out var header))
            throw new InvalidOperationException(
                "[ScenarioFileService] File is missing the 'Header' section.");

        if (!header.TryGetProperty("SubsystemType", out var typeElem))
            throw new InvalidOperationException(
                "[ScenarioFileService] Header is missing 'SubsystemType'.");

        var subsystemType = typeElem.GetString() ?? string.Empty;
        if (Array.IndexOf(AcceptedSubsystemTypes, subsystemType) < 0)
            throw new InvalidOperationException(
                $"[ScenarioFileService] Unrecognized SubsystemType '{subsystemType}'. " +
                $"Accepted: {string.Join(", ", AcceptedSubsystemTypes)}.");
    }
}
```

> **`ScenarioHeader`**: Check if it is a sealed class in `FDP.Toolkit.Scenario` with a `SubsystemType` property. If it has a different shape, adjust accordingly. Look at `ScenarioSerializerTests.cs` for example usage patterns.

### B.3 — Modify `ScenarioEditorModule.cs` to wire `ScenarioFileService`

**File:** `Hrot.ScenarioEditor/ScenarioEditorModule.cs`

Add `ScenarioFileService` as an optional injected service. The module is a thin facade that exposes the file ops.

Current state: `ScenarioEditorModule` is a stub with `Name`, `Policy`, and empty `RegisterSystems`/`Tick`.

Modify it to:
1. Accept an optional `ScenarioFileService?` in the constructor (nullable — the module is usable without file ops)
2. Expose `FileService` property for the ScenarioBrowserPanel (future Phase 3.D)

```csharp
// Add constructor parameter and private field:
private readonly ScenarioFileService? _fileService;

public ScenarioEditorModule(ScenarioFileService? fileService = null)
{
    _fileService = fileService;
}

/// <summary>
/// Exposes the file service for use by panels that trigger New/Save/Load operations.
/// <c>null</c> when no serializer was provided at construction time.
/// </summary>
public ScenarioFileService? FileService => _fileService;
```

### B.4 — Write tests for `ScenarioFileService`

**File:** `Hrot.ScenarioEditor.Tests/ScenarioFileServiceTests.cs`

Requirements:
- Test project already has `Hrot.ScenarioEditor.Tests.csproj` — add NuGet/project references to `FDP.Toolkit.Scenario` if not already transitively available.
- Use `System.IO.Path.GetTempFileName()` for temp file paths; delete in the test cleanup.

Write the following tests:

**Test 1 — Save/Load round-trip:**
- Build a minimal `ScenarioSerializer` using `ScenarioSerializerBuilder` with `SubsystemType = "Hrot.Scenario"` and no translators (auto-serializer only).
- Create a repo, register `SimTransform` (from `Fdp.Modules.Geographic`), create 2 entities with distinct positions.
- Call `fileService.SaveScenario(repo, tempPath)`.
- Create a fresh `EntityRepository`, call `fileService.LoadScenario(freshRepo, tempPath)`.
- Assert: `freshRepo` contains 2 entities; both have `SimTransform` components with matching positions.

> If `SimTransform` is not available or not auto-serializable, use a simpler saveable component type from the codebase that IS registered for serialization (check `ScenarioSerializerTests.cs` to see what they use).

**Test 2 — NewScenario clears repo:**
- Create repo with 3 entities.
- Register an observer with `fileService.RegisterWorldResetObserver(() => observerCalled = true)`.
- Call `fileService.NewScenario(repo)`.
- Assert: `observerCalled == true` AND `repo.EntityCount == 0`.

**Test 3 — LoadScenario fires reset before populate:**
- Create a repo with 2 entities; save with `SaveScenario`.
- Create a target repo with 5 entities; register observer that records reset.
- Call `fileService.LoadScenario(targetRepo, savedPath)`.
- Assert: observer fired, final `targetRepo.EntityCount == 2` (loaded entities, not 5 original).

**Test 4 — Subsystem type mismatch throws:**
- Write a JSON string to a temp file with `"SubsystemType": "Hrot.OtherApp"`.
- Assert `InvalidOperationException` is thrown by `LoadScenario`.

**Test 5 — Cross-app compatibility (SimHost file accepted):**
- Write a JSON string with `"SubsystemType": "Hrot.SimHost"` and empty entities.
- Assert `LoadScenario` does NOT throw.

---

## Task C: PACK2-R002 — Complete `CgfSubsystem` Brain-Role Pack Installation

### C.1 — Add `PackRole` enum to `Hrot.Map.Common`

**File:** `Hrot.Map.Common/PackRole.cs`

```csharp
namespace Hrot.Map.Common;

/// <summary>
/// Tags a composite translator pack with its direction of data flow.
/// <see cref="Ingress"/> packs register DDS reader subscriptions only;
/// <see cref="Egress"/> packs register DDS writer publications only.
/// </summary>
public enum PackRole
{
    /// <summary>Pack subscribes to incoming DDS topics (reader only).</summary>
    Ingress,
    /// <summary>Pack publishes to outgoing DDS topics (writer only).</summary>
    Egress,
}
```

### C.2 — Add `PackRole` parameter to `EntityStatesIngressPack`

**File:** `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs`

Add `PackRole role` as the **first** constructor parameter. Add a validation guard asserting `role == PackRole.Ingress`:

```csharp
public EntityStatesIngressPack(
    PackRole role,
    DdsParticipant? participant,
    NetworkEntityMap entityMap,
    FdpEventBus eventBus,
    GhostCreationSystem ghostCreationSystem,
    IGeographicTransform geoTransform)
{
    if (role != PackRole.Ingress)
        throw new ArgumentException(
            $"EntityStatesIngressPack must be constructed with PackRole.Ingress, got {role}.",
            nameof(role));
    // ... rest of constructor unchanged
}
```

Update existing call sites:
- `Hrot.Map.Common.Tests` tests for `EntityStatesIngressPack` — add `PackRole.Ingress` as first arg.
- Any other callers (search `new EntityStatesIngressPack(` across the solution).

### C.3 — Add `PackRole` parameter to `ActuatorIntentsEgressPack`

**File:** `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs`

Add `PackRole role` as the **first** constructor parameter. Add validation guard asserting `role == PackRole.Egress`:

```csharp
public ActuatorIntentsEgressPack(
    PackRole role,
    DdsParticipant participant,
    NetworkEntityMap entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus eventBus)
{
    if (role != PackRole.Egress)
        throw new ArgumentException(
            $"ActuatorIntentsEgressPack must be constructed with PackRole.Egress, got {role}.",
            nameof(role));
    // ... rest of constructor unchanged
}
```

Update call sites:
- `Hrot.SimHost.Tests/ActuatorIntentsEgressPackTests.cs` — add `PackRole.Egress` as first arg.
- Any other callers (search `new ActuatorIntentsEgressPack(` across the solution).

### C.4 — Extend `CgfApplication` with simulation kernel and `Install()` API

**File:** `Hrot.CGF/CgfApplication.cs`

Add the following to `CgfApplication`:

**New private fields (after existing fields):**
```csharp
// ── Simulation kernel (lazy-initialized on first Tick after Install calls) ──
private readonly EntityRepository _simWorld;
private readonly ModuleHostKernel _simKernel;
private bool _simInitialized;
```

**Constructor changes:**
After the `_timeKernel.Initialize()` call, add:
```csharp
_simWorld  = new EntityRepository();
_simKernel = new ModuleHostKernel(_simWorld, new EventAccumulator());
// Note: _simKernel.Initialize() is deferred until first Tick()
// so callers can call Install() between construction and first tick.
```

**New `Install()` method:**
```csharp
/// <summary>
/// Registers an <see cref="IEcsModule"/> with the CGF simulation kernel.
/// Must be called BEFORE <see cref="Tick"/> is first invoked.
/// Ownership of the module transfers to this application.
/// </summary>
/// <exception cref="InvalidOperationException">If called after the first Tick.</exception>
public void Install(IEcsModule module)
{
    if (module == null) throw new ArgumentNullException(nameof(module));
    if (_simInitialized)
        throw new InvalidOperationException(
            $"[CgfApplication] Cannot Install module '{module.Name}' after Tick() has been called.");
    _simKernel.RegisterModule(module);
}
```

**New introspection property:**
```csharp
/// <summary>
/// Returns the names of modules registered via <see cref="Install"/>.
/// Used by unit tests to assert pack composition.
/// </summary>
public IReadOnlyList<string> InstalledModuleNames => _simKernel.GetRegisteredModuleNames();
```

**Modify `Tick()`:** At the start of `Tick()`, add lazy-init:
```csharp
// Lazy-initialize the simulation kernel on the first tick.
if (!_simInitialized)
{
    _simKernel.Initialize();
    _simInitialized = true;
}
```

Add the simKernel update at the end of `Tick()` (after `_eventBus.SwapBuffers()`):
```csharp
_simKernel.Update();
```

**Modify `Dispose()`:** Add disposal of the sim kernel before `_participant.Dispose()`:
```csharp
_simKernel.Dispose();
_simWorld.Dispose();
```

> **Required imports** to add (check which are already present in the using list):
> - `using ModuleHost.Core;` (for ModuleHostKernel)
> - `using Fdp.Kernel;` (for EntityRepository, EventAccumulator)
> - `using System.Collections.Generic;` (for IReadOnlyList)
> - `using ModuleHost.Core.Abstractions;` (for IEcsModule)

Also expose the participant and event bus as `internal` properties so `CgfSubsystem` can use them when constructing packs (without adding a public API):

```csharp
/// <summary>Internal accessor for subsystem wiring.</summary>
internal DdsParticipant Participant => _participant;

/// <summary>Internal accessor for subsystem wiring.</summary>
internal FdpEventBus EventBus => _eventBus;
```

### C.5 — Extend `CgfSubsystem.Initialize` to install packs

**File:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs`

```csharp
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost.Translators;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Modules.Geographic;
using FDP.Framework.Runner;

namespace Hrot.ClusterRunner.Services;

public sealed class CgfSubsystem : ISubsystem
{
    private CgfApplication? _app;

    public string Name => "CGF";
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

    public void Initialize(SubsystemConfig config)
    {
        _app = new CgfApplication(config.DomainId, nodeId: config.NodeId != 0 ? config.NodeId : 400);

        // ── Brain-role pack installation (PACK2-R002) ─────────────────────────
        var behaviorRegistry   = new BehaviorRegistry();
        var entityMap          = new NetworkEntityMap();
        var geoTransform       = HrotEnvironment.CreateGeoTransform();
        var ghostCreation      = new GhostCreationSystem(entityMap);

        _app.Install(new CgfLogicPack(behaviorRegistry, entityMap));
        _app.Install(new EntityStatesIngressPack(
            PackRole.Ingress,
            _app.Participant,
            entityMap,
            _app.EventBus,
            ghostCreation,
            geoTransform));
        _app.Install(new ActuatorIntentsEgressPack(
            PackRole.Egress,
            _app.Participant,
            entityMap,
            geoTransform,
            _app.EventBus));
    }

    public void Update(float deltaTime)
    {
        _app?.Tick();
    }

    public void DrawWorld() { }
    public void DrawUI()    { }

    public void Shutdown()
    {
        _app?.Dispose();
        _app = null;
    }
}
```

> **Note on `HrotEnvironment`:** It's in namespace `Hrot.Map.Common`. Add `using Hrot.Map.Common;` if not already present.

### C.6 — Write unit test for `CgfSubsystem` pack composition

**File:** `Hrot.ClusterRunner.Tests/CgfSubsystemTests.cs`

> First check if `Hrot.ClusterRunner.Tests/` exists and has a .csproj referencing Hrot.ClusterRunner. Look at existing test files in that folder for patterns. If the project does not exist, create it matching the pattern of `Hrot.SimHost.Tests.csproj`.

```csharp
using Hrot.ClusterRunner.Services;
using FDP.Framework.Runner;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

public class CgfSubsystemTests : IDisposable
{
    private readonly CgfSubsystem _sut = new();

    public void Dispose() => _sut.Shutdown();

    [Fact]
    public void Initialize_InstallsThreePacks()
    {
        // Use a unique domain to avoid participant conflicts with other tests.
        var config = new SubsystemConfig { DomainId = 199 };

        _sut.Initialize(config);

        // Access the internal CgfApplication via reflection (or via an internal TestHook)
        var appField = typeof(CgfSubsystem)
            .GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var app = appField!.GetValue(_sut);

        var namesProp = app!.GetType().GetProperty("InstalledModuleNames");
        var names = (System.Collections.Generic.IReadOnlyList<string>)namesProp!.GetValue(app)!;

        Assert.Contains("CgfLogicPack",             names);
        Assert.Contains("EntityStatesIngress",       names);
        Assert.Contains("ActuatorIntentsEgress",     names);
        Assert.Equal(3, names.Count);
    }
}
```

> **Alternative:** If `Hrot.ClusterRunner` already has `InternalsVisibleTo` for the test project, you can add a `TestHook_App` internal property to `CgfSubsystem` to avoid reflection. Prefer reflection-free approach if InternalsVisibleTo is set.

---

## Verification Checklist

After completing all tasks, verify:

1. **Build:** `dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**
2. **Tests:**
   - `dotnet test Hrot.ScenarioEditor.Tests --no-build` → all pass (at minimum previous 7 + 3 new A001/E004 tests)
   - `dotnet test Hrot.ClusterRunner.Tests --no-build` → new CgfSubsystem test passes
   - `dotnet test Hrot.Map.Common.Tests --no-build` → 99/99 (no regressions)
   - `dotnet test Hrot.SimHost.Tests --no-build` → no regressions (ActuatorIntentsEgressPackTests updated)
3. **No NED leak check:** `Hrot.ScenarioEditor.csproj` must still have no reference to `Hrot.NED` or `CycloneDDS.*` (check `dotnet list package --include-transitive` for `Hrot.ScenarioEditor`)

---

## Report Format

Provide the following at the end:

1. **Q1:** Which test used for round-trip in E004 tests — what component/type was used for entities? Was `SimTransform` auto-serializable?
2. **Q2:** Was `ScenarioHeader` a class or record in `FDP.Toolkit.Scenario`? What were its required properties?
3. **Q3:** Did `CgfApplication.Install()` need any additional `using` directives beyond those listed? List them.
4. **Q4:** Were there any unexpected callers of `EntityStatesIngressPack` or `ActuatorIntentsEgressPack` constructors that required updating?
5. **Test counts** (full table: project, before, after, delta).
