# BATCH-09 Instructions

**Batch:** BATCH-09  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-C001 · PACK2-R003  
**Branch:** main (append directly)

---

## Context

- `Hrot.Editor` currently is a class library with `Hrot.ScenarioEditor` and `FDP.Toolkit.DER` references; no composition root.
- C001 creates the offline All-In-One composition root (`Program.cs`) in `Hrot.Editor` — adds references to `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Orchestrator`, changes output type to Exe.
- The `EditorDependencyTests` constraint ("no Hrot.NED") still holds because `Program.cs` must NOT directly use any `Hrot.NED.*` types. `GetReferencedAssemblies()` only lists assemblies where the current assembly has direct type usage — not transitive deps. (Hrot.SimHost, Hrot.CGF, Hrot.Orchestrator all reference Hrot.NED internally, but as long as Program.cs doesn't use Hrot.NED types directly, the test passes.)
- R003 scaffolds `CgfHarness` and `EditorHarness` for integration tests, plus adds a shared-domain/mode constructor to `HrotRunnerHarness` for use by R006.
- `ModuleHostKernel(EntityRepository, EventAccumulator)` requires a time controller via `SetTimeController` before `Initialize()` can be called. Use `TimeControllerFactory.Create(bus, new TimeControllerConfig { Role = TimeRole.Standalone })`.
- `NetworkEntityMap` is a simple DDS-free `ConcurrentDictionary<long,Entity>` in `ModuleHost.Network.Cyclone.Services` — safe to instantiate without DDS.
- `DoctrineRegistry` is from `FDP.Toolkit.Behavior` — safe to instantiate without DDS.
- `ClusterSlave` is from `FDP.Toolkit.Orchestration` — safe to instantiate with just `nodeId + subsystemName`.

---

## Task A: PACK2-C001 — Assemble HROT Editor Composition Root

### A.1 — Update `Hrot.Editor/Hrot.Editor.csproj`

Add `<OutputType>Exe</OutputType>` and add project references to the three new dependencies. The existing references remain unchanged.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Hrot.Editor.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Hrot.ScenarioEditor\Hrot.ScenarioEditor.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.DER\FDP.Toolkit.DER.csproj" />
    <ProjectReference Include="..\Hrot.SimHost\Hrot.SimHost.csproj" />
    <ProjectReference Include="..\Hrot.CGF\Hrot.CGF.csproj" />
    <ProjectReference Include="..\Hrot.Orchestrator\Hrot.Orchestrator.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImgui-cs" Version="3.2.0" />
    <PackageReference Include="NLog" Version="5.2.8" />
  </ItemGroup>
</Project>
```

> **Verify:** `dotnet build Hrot.Editor` must produce 0 errors. The `EditorDependencyTests` must still pass — that test calls `GetReferencedAssemblies()` on `Hrot.Editor.dll`. Verify that `Hrot.NED` does NOT appear in that list (it should not, because `Program.cs` must not directly reference any `Hrot.NED.*` types).

### A.2 — Create `Hrot.Editor/Program.cs`

Create the offline All-In-One composition root. This file wires all modules and starts a Raylib window loop.

> **Critical:** Do NOT `using Hrot.NED;` or reference any type in the `Hrot.NED.*` namespace anywhere in this file. 
> **Critical:** `ModuleHost.Network.Cyclone.Services` contains `NetworkEntityMap` — check the namespace via the `using` declaration at the top of the file if needed.
> **Critical:** `FDP.Toolkit.Time.Controllers` contains `TimeControllerFactory` and `TimeControllerConfig`. Both come transitively via `Hrot.SimHost`.

```csharp
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Editor;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using ModuleHost.Network.Cyclone.Services;
using Raylib_cs;

// ── 1. ECS world ─────────────────────────────────────────────────────────────
var world       = new EntityRepository();
var accumulator = new EventAccumulator();
var kernel      = new ModuleHostKernel(world, accumulator);

// ── 2. Time controller (standalone — no DDS sync partner) ───────────────────
var timeCtrl = TimeControllerFactory.Create(
    world.Bus,
    new TimeControllerConfig { Role = TimeRole.Standalone });
kernel.SetTimeController(timeCtrl);

// ── 3. Shared services ────────────────────────────────────────────────────────
var entityMap        = new NetworkEntityMap();
var doctrineRegistry = new DoctrineRegistry();
var clusterSlave     = new ClusterSlave(nodeId: 0, subsystemName: "Editor", eventBus: world.Bus);
var fileService      = EditorBootstrap.CreateFileService();

// ── 4. Module registration (offline — no translator packs) ───────────────────
kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap));
kernel.RegisterModule(new OrchestrationLogicPack(clusterSlave));
kernel.RegisterModule(new ScenarioEditorModule(fileService));

// ── 5. Kernel initialization ──────────────────────────────────────────────────
kernel.Initialize();

// ── 6. Editor application (IEditorLogic facade) ──────────────────────────────
var app   = new EditorApplication(fileService, world.Bus, world);
var files = new ScenarioBrowserPanel();
var tools = new EditorToolbarPanel();

// ── 7. Raylib window loop ─────────────────────────────────────────────────────
const int TargetFps     = 60;
const int WindowWidth   = 1280;
const int WindowHeight  = 720;
const string WindowTitle = "HROT Editor";

Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
Raylib.SetTargetFPS(TargetFps);

try
{
    while (!Raylib.WindowShouldClose())
    {
        float dt = Raylib.GetFrameTime();

        // Simulation tick
        kernel.Update(dt);

        // Rendering
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        // TODO: ImGui panels (Phase 7 — wired in a future batch)
        Raylib.EndDrawing();
    }
}
finally
{
    Raylib.CloseWindow();
    kernel.Dispose();
    world.Dispose();
}
```

> **If `ModuleHost.Network.Cyclone.Services` is not resolvable** as a using, check the actual namespace of `NetworkEntityMap` by reading the file. It may be a slightly different namespace. Adjust the using accordingly.

> **If `TimeControllerFactory` is not found** in `FDP.Toolkit.Time.Controllers`, check which assembly/namespace the factory is in. The test `Hrot.SimHost.Tests/SimHostCoreLogicPackTests.cs` or `SimHostApp.cs` may show the correct import.

> **If `kernel.Update(float)` does not exist** (only `kernel.Update()` with no args), replace it with `kernel.Update()` without the deltaTime argument.

### A.3 — Add build/smoke test in `Hrot.Editor.Tests/`

Create `Hrot.Editor.Tests/OfflineKernelBootTests.cs`:

```csharp
using System.IO;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Editor;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.SimHost;
using ModuleHost.Network.Cyclone.Services;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// PACK2-C001 smoke test: verifies the offline composition root
/// can be assembled and ticked headlessly without exception.
/// </summary>
public class OfflineKernelBootTests : IDisposable
{
    private readonly EntityRepository   _world;
    private readonly ModuleHostKernel   _kernel;

    public OfflineKernelBootTests()
    {
        _world = new EntityRepository();
        var accumulator    = new EventAccumulator();
        _kernel = new ModuleHostKernel(_world, accumulator);

        var timeCtrl = TimeControllerFactory.Create(
            _world.Bus,
            new TimeControllerConfig { Role = TimeRole.Standalone });
        _kernel.SetTimeController(timeCtrl);

        var entityMap        = new NetworkEntityMap();
        var doctrineRegistry = new DoctrineRegistry();
        var clusterSlave     = new ClusterSlave(nodeId: 0, subsystemName: "EditorTest");
        var fileService      = EditorBootstrap.CreateFileService();

        _kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
        _kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap));
        _kernel.RegisterModule(new OrchestrationLogicPack(clusterSlave));
        _kernel.RegisterModule(new ScenarioEditorModule(fileService));

        _kernel.Initialize();
    }

    public void Dispose()
    {
        _kernel.Dispose();
        _world.Dispose();
    }

    [Fact]
    public void OfflineCompositionRoot_Initializes_WithoutException()
    {
        // If we reach here, Initialize() did not throw.
        Assert.NotNull(_kernel);
    }

    [Fact]
    public void OfflineCompositionRoot_Ticks10Frames_WithoutException()
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < 10; i++)
        {
            // Use Update() or Update(dt) — check which overload exists on ModuleHostKernel.
            // If Update(float) exists, prefer it; otherwise use Update().
            _kernel.Update(dt);
        }
        Assert.True(true); // reached without exception
    }
}
```

> **Note on imports:** The same imports apply as in `Program.cs`. If `FDP.Toolkit.Time.Controllers` cannot be found via transitive refs from `Hrot.Editor`, add a direct `<ProjectReference>` to `FDP\Toolkits\FDP.Toolkit.Time\FDP.Toolkit.Time.csproj` in `Hrot.Editor.Tests.csproj`.

> **Note on `kernel.Update(float)` vs `kernel.Update()`:** Check `ModuleHostKernel` — if both overloads exist, prefer `Update(dt)`. If only `Update()` exists, use that.

> **Note on `EditorDependencyTests` regression:** Run it after A.1. If it fails (Hrot.NED appears), find which type in Program.cs or OfflineKernelBootTests.cs is from Hrot.NED and remove that direct usage.

---

## Task B: PACK2-R003 — Scaffold CgfHarness and EditorHarness

### B.1 — Modify `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`

Add a shared-domain, mode-filtered constructor. This is needed for R006 (`DistributedBrainMuscleIntegrationTests`) which pairs `CgfHarness(domainId)` with `HrotRunnerHarness(RunMode.SimHost, domainId)`.

The new constructor should:
1. Accept a `RunMode mode` and `int domainId` parameters.
2. Use the provided `domainId` instead of incrementing the counter.
3. Only start subsystems that match the given `RunMode` flags.
4. For `RunMode.SimHost`: start `OrchestratorSvc` (always) + `SimHost`.

Add the following constructor to `HrotRunnerHarness.cs`:

```csharp
/// <summary>
/// Creates a harness with a specific run mode and domain ID (for shared-domain tests).
/// Typically used alongside <see cref="CgfHarness(int)"/> for IT-4 tests.
/// </summary>
public HrotRunnerHarness(RunMode mode, int domainId)
{
    DomainId = domainId;

    OrchestratorSvc = new OrchestratorSubsystem();
    SimHost         = new SimHostSubsystem();
    Ig              = new IgSubsystem();
    ExCon           = new ExConSubsystem();

    // Always include Orchestrator; conditionally include other subsystems.
    var subsystems = new System.Collections.Generic.List<ISubsystem> { OrchestratorSvc };
    if (mode.HasFlag(RunMode.SimHost)) subsystems.Add(SimHost);
    if (mode.HasFlag(RunMode.IG))     subsystems.Add(Ig);
    if (mode.HasFlag(RunMode.ExCon))  subsystems.Add(ExCon);

    var options = new RunnerOptions { Headless = true, DomainId = domainId };
    Orchestrator = new SubsystemOrchestrator(subsystems, options);

    Orchestrator.Initialize();
    Warmup();
}
```

You will also need to add the following `using` at the top of the file (if not already present):
```csharp
using Hrot.ClusterRunner.Configuration;
using System.Collections.Generic;
```

> **Note:** `RunMode` is in `Hrot.ClusterRunner.Configuration` — it is already available since `HrotRunnerHarness` is in `Hrot.ClusterRunner.Integration.Tests` which references `Hrot.ClusterRunner`. Check if `using Hrot.ClusterRunner.Configuration;` is already at the top; if not, add it.

### B.2 — Create `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`

```csharp
using System;
using System.Threading;
using FDP.Framework.Runner;
using Hrot.ClusterRunner.Services;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Domain-isolated test harness wrapping <see cref="CgfSubsystem"/> for integration tests.
///
/// <para>Provides two construction modes:</para>
/// <list type="bullet">
///   <item>Auto-increment: uses same counter base as <see cref="HrotRunnerHarness"/> to avoid
///     domain conflicts (starting at 200 for CGF-only tests).</item>
///   <item>Shared-domain: <c>CgfHarness(int domainId)</c> — used with
///     <c>HrotRunnerHarness(RunMode, int domainId)</c> in IT-4 tests.</item>
/// </list>
/// </summary>
public sealed class CgfHarness : IDisposable
{
    private const int CgfDomainIdBase  = 200;
    private const int WarmupFrames     = 20;
    private const int PumpSleepMs      =  5;
    private const int PostWarmupSettle = 200;

    private static int _domainCounter = CgfDomainIdBase - 1;

    public int          DomainId { get; }
    public CgfSubsystem CgfSvc   { get; }

    // ── Auto-increment constructor ────────────────────────────────────────────

    /// <summary>
    /// Creates a new harness assigned a unique domain ID from the internal counter.
    /// Two independently created instances always get different IDs.
    /// </summary>
    public CgfHarness()
        : this(Interlocked.Increment(ref _domainCounter))
    {
    }

    // ── Shared-domain constructor ─────────────────────────────────────────────

    /// <summary>
    /// Creates a harness using the specified domain ID (shared with another harness).
    /// Used in <c>DistributedBrainMuscleIntegrationTests</c> (IT-4) to pair with
    /// a <see cref="HrotRunnerHarness"/> on the same loopback domain.
    /// </summary>
    public CgfHarness(int domainId)
    {
        DomainId = domainId;
        CgfSvc   = new CgfSubsystem();
        CgfSvc.Initialize(new SubsystemConfig
        {
            DomainId = domainId,
            Headless = true,
        });

        Warmup();
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances <paramref name="frames"/> simulation frames (5 ms sleep between each).
    /// </summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> returns <c>true</c>
    /// or <paramref name="timeoutMs"/> milliseconds have elapsed.
    /// Returns <c>true</c> if the condition was met before timeout.
    /// </summary>
    public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        if (condition()) return true;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        CgfSvc.Shutdown();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Warmup()
    {
        for (int i = 0; i < WarmupFrames; i++)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
        }
        Thread.Sleep(PostWarmupSettle);
    }
}
```

### B.3 — Create `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

The `EditorHarness` is fully offline (no DDS). It wires up `SimHostCoreLogicPack`, `CgfLogicPack`, and `ScenarioEditorModule` under a `ModuleHostKernel`.

> **Important:** `Hrot.Editor` (containing `EditorApplication`, `IEditorLogic`) is NOT referenced by `Hrot.ClusterRunner.Integration.Tests`. `EditorHarness` exposes `Kernel`, `Repo`, `Bus` — tests publish commands directly to `Bus`. There is no `Editor` (`IEditorLogic`) property here to avoid adding Raylib as a test dependency.
>
> If tests need `IEditorLogic`, they can call `new EditorApplication(fileService, harness.Bus, harness.Repo)` directly in the test code after adding `Hrot.Editor` reference to the test project (add it in BATCH-11 if needed for R005).

```csharp
using System;
using System.Threading;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.SimHost;
using ModuleHost.Core;
using ModuleHost.Network.Cyclone.Services;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Offline (no DDS) test harness for editor integration tests.
/// Instantiates <see cref="ModuleHostKernel"/> with the three local packs:
/// <see cref="SimHostCoreLogicPack"/>, <see cref="CgfLogicPack"/>,
/// and <see cref="ScenarioEditorModule"/>.
///
/// <para>No CycloneDDS domain is allocated.</para>
/// </summary>
public sealed class EditorHarness : IDisposable
{
    private const int PumpSleepMs = 5;

    public EntityRepository  Repo   { get; }
    public FdpEventBus        Bus    { get; }
    public ModuleHostKernel   Kernel { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public EditorHarness()
    {
        Repo   = new EntityRepository();
        Bus    = Repo.Bus;

        var accumulator = new EventAccumulator();
        Kernel = new ModuleHostKernel(Repo, accumulator);

        // Standalone time controller — no network sync.
        var timeCtrl = TimeControllerFactory.Create(
            Bus,
            new TimeControllerConfig { Role = TimeRole.Standalone });
        Kernel.SetTimeController(timeCtrl);

        var entityMap        = new NetworkEntityMap();
        var doctrineRegistry = new DoctrineRegistry();
        var clusterSlave     = new ClusterSlave(nodeId: 0, subsystemName: "EditorHarness");

        Kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
        Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap));
        Kernel.RegisterModule(new ScenarioEditorModule());

        Kernel.Initialize();
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>Advances <paramref name="frames"/> simulation frames.</summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Kernel.Update(PumpSleepMs / 1000f);
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> returns <c>true</c>
    /// or <paramref name="timeoutMs"/> milliseconds have elapsed.
    /// </summary>
    public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        if (condition()) return true;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Kernel.Update(PumpSleepMs / 1000f);
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Kernel.Dispose();
        Repo.Dispose();
    }
}
```

> **Note on `kernel.Update(float)` vs `kernel.Update()`:** If `ModuleHostKernel` doesn't have `Update(float)`, use `Update()` in both harnesses.

> **Note on `ScenarioEditorModule()`:** The constructor signature is `ScenarioEditorModule(ScenarioFileService? fileService = null)` — so calling without args is valid.

> **Note on `OrchestrationLogicPack` in EditorHarness:** The spec says 3 packs (SimHostCore + CgfLogic + ScenarioEditor). `OrchestrationLogicPack` is intentionally excluded from the offline headless harness (it requires DDS event bus for ClusterSlave heartbeat in production; in tests we skip it).

### B.4 — Write smoke tests for the harnesses

Add tests to `Hrot.ClusterRunner.Integration.Tests/` to verify the harnesses work:

Create `Hrot.ClusterRunner.Integration.Tests/HarnessSmoke.Tests.cs`:

```csharp
using System.Threading;
using Hrot.ClusterRunner.Configuration;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Smoke tests for PACK2-R003 harness scaffolding.
/// </summary>
public class HarnessSmokTests
{
    [Fact]
    public void EditorHarness_Initializes_WithoutException()
    {
        using var h = new EditorHarness();
        Assert.NotNull(h.Repo);
        Assert.NotNull(h.Bus);
        Assert.NotNull(h.Kernel);
    }

    [Fact]
    public void EditorHarness_PumpFrames_WithoutException()
    {
        using var h = new EditorHarness();
        h.PumpFrames(5);
        Assert.True(true);
    }

    [Fact]
    public void CgfHarness_TwoInstances_HaveDifferentDomainIds()
    {
        using var h1 = new CgfHarness();
        using var h2 = new CgfHarness();
        Assert.NotEqual(h1.DomainId, h2.DomainId);
    }

    [Fact]
    public void CgfHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        using var h = new CgfHarness(domainId: 150);
        Assert.Equal(150, h.DomainId);
    }

    [Fact]
    public void HrotRunnerHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        // Use a distinct domain (250) to avoid clashes with the auto-counter test run.
        using var h = new HrotRunnerHarness(RunMode.SimHost, domainId: 250);
        Assert.Equal(250, h.DomainId);
    }
}
```

> **CgfHarness smoke tests DO start a real CycloneDDS participant** (CgfSubsystem calls CgfApplication which creates a DdsDomainParticipant). The domain IDs (starting at 200) must not conflict with HrotRunnerHarness auto-counter (starting at 100). If tests fail due to DDS domain initialization, check if there are port conflicts; try different domain IDs above 240.

---

## Testing Summary

| Suite | Expected delta | Details |
|-------|---------------|---------|
| `Hrot.Editor.Tests` | +2 tests | `OfflineKernelBootTests` (init + 10 frames) |
| `Hrot.ClusterRunner.Integration.Tests` | +5 smoke tests | `HarnessSmokTests` |
| `Hrot.ScenarioEditor.Tests` | 0 (14/14) | No regressions |

---

## Build & Verify Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build Hrot.Editor/Hrot.Editor.csproj --no-restore
dotnet build Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-restore
dotnet build Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-restore

# Test (non-DDS first)
dotnet test Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build
# Editor dependency test must still pass (no Hrot.NED in assembly refs)

# Integration tests (require CycloneDDS)
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "HarnessSmokTests"
```

---

## Report

Submit your report to `.dev/packs-2/reports/BATCH-09-REPORT.md`.

Include:
1. Build result for `Hrot.Editor` (0 errors required).
2. Whether `EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency` still passes.
3. Final test counts for `Hrot.Editor.Tests` and `Hrot.ClusterRunner.Integration.Tests`.
4. Any deviations from instructions (e.g., `kernel.Update()` vs `Update(float)`, namespace differences).
5. Whether CgfHarness smoke tests pass (these require DDS; note if skipped in CI environment).
