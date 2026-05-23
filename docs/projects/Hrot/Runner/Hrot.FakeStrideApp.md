# Hrot.FakeStrideApp

**Path**: `Hrot/Runner/Hrot.FakeStrideApp/`
**Assembly**: `Hrot.FakeStrideApp.exe`
**Target Framework**: net8.0
**Output Type**: Executable (Exe)
**Date**: 2026-05-23

---

## README Validation

**Status: Missing** — no `README.md` exists in the project folder or its subdirectories.
The project has no standalone README. Documentation is provided entirely by this file and
the XML doc-comments in the source.

---

## Executive Overview

`Hrot.FakeStrideApp` is a self-hosted, standalone windowed process that exercises the
`Hrot.StrideMock` subsystem in isolation. It acts as a *fake* Stride application: it
replaces the real Stride game-engine runtime with a lightweight 2-D Raylib window, yet
runs the same simulation core — kinematics, perception, combat, navigation, DDS
networking — that the real Stride node would run.

### What "Fake" Means in This Context

The Stride game engine is not referenced anywhere in this project or its direct
dependency `Hrot.StrideMock`. The project stands in for the full Stride runtime in the
following ways:

| Real Stride feature faked         | Replacement used                              |
|-----------------------------------|-----------------------------------------------|
| Stride game window / render loop  | Raylib window managed by `FdpApplication`     |
| Stride `SyncScript` base class    | `FakeStrideScript` abstract class             |
| Stride entity scene graph         | FDP ECS `EntityRepository` / `ISimulationView`|
| Stride input system               | `RaylibInputProvider`                         |
| Stride camera                     | `MapCamera` (flat-Earth 2-D pan/zoom)         |
| Stride visual effects             | Raylib primitive draw calls (circles, lines)  |
| Stride asset loading              | Not needed; geometry is procedural            |

### Why This Approach Exists

1. **GPU-free execution** — Raylib can run headless on CI machines without a display
   adapter; Stride cannot.
2. **Fast boot** — No asset compilation, shader compilation, or content pipeline.
3. **Portability** — The fake app can be run on any developer machine alongside the
   real Stride editor without conflict.
4. **Isolation testing** — Integration tests in `Hrot.FakeStrideApp.Tests` can verify
   the entire construction path (`FakeStrideApp` constructor, configuration defaults,
   type hierarchy) without launching a window.
5. **Reference implementation** — Serves as a living example of how to host a
   `StrideNodeBootstrapper` outside the multi-subsystem ClusterRunner shell.

### Primary Use Cases

- **Local development** — Run the Stride mock node as a standalone window during
  feature development when the full IOS-IG-SimHost cluster is not needed.
- **CI smoke test** — Verify type correctness and constructor safety without a GPU.
- **Debugging visualisation** — Observe entity positions and visual effects as 2-D
  Raylib primitives on a flat map while the simulation runs.
- **Stage 2 prototype target** — When the real Stride engine is eventually integrated,
  `FakeStrideScript` and `FakeStrideEntity`/`FakeStrideEffect` are swapped for their
  Stride-native counterparts with zero changes to the orchestration code.

---

## Architecture

### Overview

The project contains exactly three source files and is deliberately thin. All simulation
logic lives in `Hrot.StrideMock`; this project only wires it into the `FdpApplication`
lifecycle.

```
+================================================================+
|                     Hrot.FakeStrideApp.exe                     |
|                                                                |
|  Program.cs                                                    |
|    parse --domain / --node args                                |
|    new FakeStrideApp(appConfig, domainId, nodeId).Run()        |
|                                                                |
|  FakeStrideApp : FdpApplication                                |
|    OnLoad()    -- 7-step bootstrap sequence                    |
|    OnUpdate()  -- input + camera + script + tick               |
|    OnDrawWorld() -- 2-D entity/effect rendering                |
|    OnDrawUI()  -- ImGui splash overlay                         |
|    OnUnload()  -- dispose core + participant                   |
+================================================================+
```

### Inheritance Chain

```
System.IDisposable
    |
    +-- FdpApplication  (Fdp.Presentation.Raylib)
            |
            +-- FakeStrideApp  (Hrot.FakeStrideApp)
```

`FdpApplication` owns the Raylib window, the ImGui layer, and the main loop.
`FakeStrideApp` implements the four lifecycle hooks and owns the node identity.

### Lifecycle Phases

```
+------------------+     +-------------------+     +------------------+
|   Construction   |     |    Run() / Main   |     |   Shutdown       |
|                  |     |                   |     |                  |
| FakeStrideApp()  |---->| OnLoad()          |---->| OnUnload()       |
|  store domainId  |     |  1. DDS part.     |     |  core.Dispose()  |
|  store nodeId    |     |  2. Net factory   |     |  part.Dispose()  |
+------------------+     |  3. NodeConfig    |     +------------------+
                         |  4. BootstrapNode |
                         |  5. TKB populate  |
                         |  6. script.Start()|
                         |  7. InputProvider |
                         +-------------------+
                                  |
                         +--------v---------+
                         |  Frame loop      |
                         | OnUpdate(dt)     |
                         | OnDrawWorld()    |
                         | OnDrawUI()       |
                         +------------------+
```

### Component Interaction

```
+--------------------+       uses       +------------------------+
| FakeStrideApp      |----------------->| StrideNodeBootstrapper |
| (this project)     |                  | (Hrot.StrideMock)      |
|                    |                  |                        |
|  _core             |<-- exposes ----  |  .Context              |
|  _script           |<-- exposes ----  |  .Camera               |
|  _inputProvider    |                  |  .SimGroup             |
|  _participant      |                  |  .ProducerBuffer       |
+--------------------+                  +------------------------+
         |                                       |
         | owns                                  | owns
         v                                       v
+--------------------+              +------------------------+
| SyncFdpToStrideScript|            | HrotNodeContext        |
| (Hrot.StrideMock)   |            | .World (ECS)           |
|                     |            | .TkbDb                 |
|  ActiveEntities     |            | .ClusterSlave          |
|  ActiveEffects      |            | .SlaveTranslator       |
|  CurrentStateMessage|            +------------------------+
+--------------------+
         |
         | reads ECS
         v
+--------------------+       +--------------------+
| FakeStrideEntity   |       | FakeStrideEffect   |
| .Position (Vector3)|       | .Type (EffectType) |
| .Rotation (float)  |       | .Position          |
+--------------------+       | .TracerEnd         |
                             | .Scale             |
                             | .Alpha             |
                             +--------------------+
```

### OnLoad Initialization Order

The comment in `FakeStrideApp.cs` explicitly marks this order as mandatory (see also
`DESIGN.md §4.2` referenced in the XML doc-comment):

```
Step 1: HrotEnvironment.CreateParticipant()        -- DDS participant
Step 2: NedNetworkFactory()                        -- network factory + entity map
Step 3: HrotNodeConfig {}                          -- node identity + paths
Step 4: StrideNodeBootstrapper.BootstrapNode()     -- 7-phase bootstrap
Step 5: tkb.RegisterAll() / RegisterUrbanCombat()  -- TKB templates AFTER bootstrap
Step 6: SyncFdpToStrideScript.Start()              -- sync script init
Step 7: new RaylibInputProvider()                  -- after Raylib window is open
```

Violating this order causes null-reference failures in the bootstrap pipeline. In
particular, TKB must be populated after `BootstrapNode` because the catalog is created
inside the bootstrap pipeline, and `RaylibInputProvider` requires that the Raylib
window already be open (which `FdpApplication.InitializeWindow()` guarantees before
`OnLoad()` is called).

### Rendering Model

```
                  FdpApplication.Run()
                         |
          +---- OnDrawWorld() ----+---- OnDrawUI() ----+
          |                      |                     |
    Raylib 2-D mode        rlImGui Begin/End     ImGui.Begin/Text
          |                                            |
   foreach entity:                             "Cluster: LoadingLive"
     DrawCircleV (red, r=5)                    (splash only when non-operating)
          |
   foreach effect:
     Explosion -> DrawCircleV (orange, fading)
     Tracer    -> DrawLineV (yellow)
```

### Network Topology

```
+-------------------+     DDS domain     +-------------------+
| FakeStrideApp     |<==================>|  SimHost / ExCon  |
| node-700 (default)|   CycloneDDS       |  (other nodes)    |
+-------------------+                    +-------------------+
         |
  NED translators
  (NedNetworkFactory)
         |
  ECS world (EntityRepository)
         |
  SyncFdpToStrideScript
         |
  Raylib draw calls
```

---

## Source Structure

### Namespace: `Hrot.FakeStrideApp`

All production source files are in the project root. No sub-namespaces are used.

```
Hrot/Runner/Hrot.FakeStrideApp/
    Program.cs              Top-level program entry point (no namespace, top-level statements)
    FakeStrideApp.cs        Main application class
    Hrot.FakeStrideApp.Tests/
        FakeStrideAppTests.cs   xUnit integration smoke tests
```

#### `Program.cs` — Entry Point

Top-level statements (no enclosing class or namespace). Responsibilities:

- Parse `--domain <id>` and `--node <id>` command-line arguments.
- Construct an `ApplicationConfig` with the default window parameters.
- Create and `Run()` a `FakeStrideApp` instance.

Default values if no arguments are provided: `domainId=0`, `nodeId=700`.

#### `FakeStrideApp.cs` — Main Application Class

Namespace: `Hrot.FakeStrideApp`
Class: `FakeStrideApp` (`sealed`)
Base: `FdpApplication` (`Fdp.Presentation.Raylib`)

#### `FakeStrideAppTests.cs` — Tests

Namespace: `Hrot.FakeStrideApp.Tests`
Class: `FakeStrideAppTests` (`sealed`)
Framework: xUnit

Compiled as a separate project (`Hrot.FakeStrideApp.Tests.csproj`); excluded from the
main project via `<Compile Remove="Hrot.FakeStrideApp.Tests\**" />` in the csproj.

---

## Public API Reference

### `FakeStrideApp` class

```
namespace Hrot.FakeStrideApp
public sealed class FakeStrideApp : FdpApplication
```

Implements the `FdpApplication` lifecycle hooks to host a Stride mock node with a live
Raylib window.

#### Constructors

| Signature | Description |
|-----------|-------------|
| `FakeStrideApp(ApplicationConfig config, int domainId, int nodeId)` | Stores configuration and node identity. Does NOT call `OnLoad()`; the base class calls it inside `Run()`. |

**Parameters**:
- `config` — Raylib window configuration (title, size, FPS target).
- `domainId` — CycloneDDS domain identifier. Default: `0`.
- `nodeId` — HROT node identifier. Default: `700`.

#### Protected Override Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `OnLoad` | `protected override void OnLoad()` | Executes the mandatory 7-step bootstrap sequence. Sets up DDS participant, network factory, node config, `StrideNodeBootstrapper`, TKB catalog, sync script, and input provider. |
| `OnUpdate` | `protected override void OnUpdate(float dt)` | Each frame: processes camera input, updates camera, calls `script.Update(dt)`, then `core.Tick(dt)`. Guards against null fields. |
| `OnDrawWorld` | `protected override void OnDrawWorld()` | Renders entities as red circles (radius 5) and effects as orange expanding circles (explosions) or yellow lines (tracers) using Raylib 2-D primitives inside the camera mode. |
| `OnDrawUI` | `protected override void OnDrawUI()` | Renders an ImGui overlay with `CurrentStateMessage` when the cluster is in a non-operating state (e.g. loading). |
| `OnUnload` | `protected override void OnUnload()` | Disposes `StrideNodeBootstrapper` and DDS participant. Sets fields to null. Does NOT call `base.OnUnload()` to avoid double-disposing the ECS world. |

#### Private Fields

| Field | Type | Description |
|-------|------|-------------|
| `_domainId` | `int` | CycloneDDS domain identifier (constructor parameter). |
| `_nodeId` | `int` | HROT node identifier (constructor parameter). |
| `_participant` | `DdsParticipant?` | CycloneDDS participant; null until `OnLoad`, null after `OnUnload`. |
| `_core` | `StrideNodeBootstrapper?` | Bootstrapped node core; null until `OnLoad`, null after `OnUnload`. |
| `_script` | `SyncFdpToStrideScript?` | ECS-to-Raylib sync script; null until `OnLoad`, null after `OnUnload`. |
| `_inputProvider` | `RaylibInputProvider?` | Raylib input adapter; null until `OnLoad`. |

---

### `FakeStrideAppTests` class (test project)

```
namespace Hrot.FakeStrideApp.Tests
public sealed class FakeStrideAppTests
```

xUnit test class covering construction-time contracts.

#### Test Methods

| Method | Test ID | Description |
|--------|---------|-------------|
| `FakeStrideApp_InheritsFromFdpApplication` | SC_SM008_1 | Asserts `typeof(FdpApplication).IsAssignableFrom(typeof(FakeStrideApp))` via reflection. |
| `FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow` | SC_SM008_1 | Constructs a `FakeStrideApp` with valid config; verifies no exception is thrown. Does not call `Run()`. |
| `FakeStrideApp_DefaultConfig_HasExpectedValues` | SC_SM008_1 | Validates that the default window spec (1280x720, 60 fps, title) matches documented values. |

All three tests carry the same scenario ID `SC_SM008_1`, grouping them as a single
type-conformance specification check.

---

## Dependencies

### Project References

| Project | Path | Purpose |
|---------|------|---------|
| `Hrot.StrideMock` | `Hrot/Subsystems/Hrot.StrideMock/` | Provides `StrideNodeBootstrapper`, `SyncFdpToStrideScript`, `FakeStrideScript`, `FakeStrideEntity`, `FakeStrideEffect`. Core simulation node logic. |
| `Fdp.Presentation` | `FDP/Engine/Fdp.Presentation/` | Provides `FdpApplication`, `ApplicationConfig`, `RaylibInputProvider`. Raylib rendering shell and lifecycle. |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/` | Provides `NedNetworkFactory`. Wires CycloneDDS NED (Network Entity Data) translators to the ECS world. |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Raylib-cs` | 7.0.2 | C# binding for the Raylib 2-D/3-D game library. Provides window management, 2-D draw calls, and input. **Not a Stride package.** |
| `rlImGui-cs` | 3.2.0 | Raylib + ImGui integration layer. Provides `rlImGui.Begin/End()` that wraps Dear ImGui rendering into the Raylib frame. **Not a Stride package.** |

### Transitive Dependencies (notable, via project references)

| Package / Project | Origin | Notes |
|-------------------|--------|-------|
| `CycloneDDS.Runtime` | `Hrot.Network.NED` | DDS publish/subscribe middleware. `DdsParticipant`, sender tracking. |
| `Fdp.Core` | `Fdp.Presentation` | ECS `EntityRepository`, `Entity`, `FdpEventBus`. |
| `Fdp.ModuleHost` | `Fdp.Presentation` | `ModuleHostKernel`, scheduling pipeline. |
| `Fdp.Toolkit.*` | `Hrot.StrideMock` | Orchestration, replication, Vis2D, TKB, scenario utilities. |
| `Hrot.Common.Infrastructure` | `Hrot.StrideMock` | `HrotEnvironment`, `HrotNodeConfig`, `HrotNodeContext`. |
| `Hrot.IG.Components` | `Hrot.StrideMock` | `SimTransform`, `VisualEffectState`, `TracerTarget`, `EffectType`. |
| `Hrot.Map.Common` | `Hrot.StrideMock` | `MapCamera`, `NetworkEntityMap`, `GeoTransform`. |
| `ImGuiNET` | `rlImGui-cs` | Dear ImGui .NET bindings used for the splash overlay. |

### No Stride Packages

Despite the name, this project contains **zero references to any Stride NuGet package**.
The project name signals intent: it is the fake that stands in for a future real Stride
integration. All simulation physics and entity logic are provided by the FDP engine
(`Fdp.Core`, `Fdp.ModuleHost`, `Fdp.Toolkit.*`), not by Stride.

---

## Usage Examples

### Example 1: Running the Application from the Command Line

```csharp
// Default: domain 0, node 700
Hrot.FakeStrideApp.exe

// Custom domain and node
Hrot.FakeStrideApp.exe --domain 1 --node 710

// The window title will always be:
// "FakeStrideApp -- HROT Stride Mock"
```

The application opens a 1280x720 Raylib window at 60 FPS, joins the specified DDS
domain as node `nodeId`, and begins processing the simulation loop. Red circles
represent live entities on the flat map; orange expanding circles represent explosions;
yellow lines represent tracer rounds.

Close the window or press the OS close button to trigger a clean shutdown via
`OnUnload()`.

### Example 2: Embedding in an Integration Test (No Window)

The test project demonstrates how to verify construction-time invariants without ever
opening a window. The key insight is that `FdpApplication.OnLoad()` is only called
inside `Run()`, so constructing `FakeStrideApp` and immediately disposing it is safe:

```csharp
using Fdp.Presentation.Raylib;
using Hrot.FakeStrideApp;
using Xunit;

public sealed class FakeStrideAppTests
{
    // Verify type hierarchy without opening a window.
    [Fact]
    public void FakeStrideApp_InheritsFromFdpApplication()
    {
        Assert.True(typeof(FdpApplication)
            .IsAssignableFrom(typeof(FakeStrideApp)));
    }

    // Verify constructor does not throw with a valid config.
    [Fact]
    public void FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow()
    {
        var config = new ApplicationConfig
        {
            WindowTitle = "Test",
            Width       = 1280,
            Height      = 720,
            TargetFPS   = 60,
        };
        var ex = Record.Exception(() =>
        {
            using var app = new FakeStrideApp(config, domainId: 0, nodeId: 700);
        });
        Assert.Null(ex);
    }
}
```

This pattern is suitable for CI pipelines without a GPU or display server because the
`FakeStrideApp` constructor performs no Raylib initialization; that happens inside
`Run()` via `FdpApplication.InitializeWindow()`.

### Example 3: Hosting FakeStrideApp Programmatically

If you need to run the fake Stride node programmatically (e.g. from a test harness that
controls the lifecycle), replicate the same pattern as `Program.cs`:

```csharp
using Fdp.Presentation.Raylib;
using Hrot.FakeStrideApp;

int domainId = 2;
int nodeId   = 750;

var appConfig = new ApplicationConfig
{
    WindowTitle = "FakeStrideApp - Custom",
    Width       = 1920,
    Height      = 1080,
    TargetFPS   = 30,
    PersistenceEnabled = false,  // disable imgui.ini saving in CI
};

using var app = new FakeStrideApp(appConfig, domainId, nodeId);
app.Run();  // blocks until the window is closed
```

Note that `app.Quit()` (inherited from `FdpApplication`) can be called from another
thread to signal the main loop to exit cleanly at the end of the current frame.

### Example 4: Accessing the Simulation State During a Running Frame

Within custom code that is called from a derived class or from a composition that holds
a reference to the script, the `SyncFdpToStrideScript` exposes live entity state:

```csharp
// Assuming you hold a reference to the running script (e.g. via reflection in a test):
SyncFdpToStrideScript script = /* obtained from _core or injected */;

// Current cluster state (reflects ClusterSlave.LocalStateIdForTest).
ClusterState state = script.CurrentClusterState;

// Iterate live entities.
foreach (FakeStrideEntity entity in script.ActiveEntities)
{
    Console.WriteLine($"  pos=({entity.Position.X:F1}, {entity.Position.Y:F1})  " +
                      $"yaw={entity.Rotation:F3} rad");
}

// Iterate live visual effects.
foreach (FakeStrideEffect effect in script.ActiveEffects)
{
    if (effect.Type == EffectType.Explosion)
        Console.WriteLine($"  explosion at ({effect.Position.X:F1}, {effect.Position.Y:F1})  " +
                          $"alpha={effect.Alpha:F2}");
}
```

---

## Best Practices

### 1. Never Call `base.OnUnload()` in `FakeStrideApp`

`FdpApplication.OnUnload()` disposes `Kernel` and `World`, which in this configuration
are owned by `StrideNodeBootstrapper`, not by `FakeStrideApp`. The correct cleanup path
is `_core.Dispose()`, which the bootstrapper performs transitively. Calling
`base.OnUnload()` would cause a double-dispose of the ECS world.

### 2. Respect the OnLoad Step Order

The 7-step bootstrap order in `OnLoad()` is mandatory. The specific constraints are:

- The DDS participant must exist before `NedNetworkFactory` is constructed (step 2
  needs step 1's participant).
- `BootstrapNode` must complete before TKB templates are registered (step 5 needs the
  catalog created in step 4).
- `RaylibInputProvider` must be constructed after the Raylib window is open (the window
  opens inside `FdpApplication.InitializeWindow()` before `OnLoad()` is called, so
  step 7 is safe anywhere inside `OnLoad()`).

### 3. Guard Against Null Fields in Render Methods

`OnUpdate`, `OnDrawWorld`, and `OnDrawUI` all check their nullable fields for null
before use. This prevents crashes if the application is somehow ticked before
`OnLoad()` completes or after `OnUnload()` runs. Maintain this guard pattern in any
code that extends or replaces these methods.

### 4. Avoid Adding Rendering Logic to StrideNodeBootstrapper

The design mandates that `StrideNodeBootstrapper` contains zero Raylib, ImGui, or
camera provider references. All rendering belongs in `FakeStrideApp.OnDrawWorld()` and
`FakeStrideApp.OnDrawUI()`. This separation ensures the bootstrapper remains portable
to the real Stride stage.

### 5. Use the ApplicationConfig Defaults for New Instances

The production `Program.cs` hard-codes `1280x720` at `60 FPS`. Tests that construct
`FakeStrideApp` should use the same values to guarantee that the configuration
validation tests pass without modification.

### 6. TKB Population Is Application-Layer Responsibility

Both `DemoTkbSetup.RegisterAll` and `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates`
are called in `OnLoad`. If you add new scenario templates, add them here after step 5.
Do not modify `StrideNodeBootstrapper` to add application-specific templates; the
bootstrapper is shared by both `FakeStrideApp` and `StrideMockSubsystem`.

### 7. Sender Tracking for Diagnostics

After creating the DDS participant, `EnableSenderTracking` is called with
`AppDomainId` and `AppInstanceId` set to `domainId` and `nodeId`. This is required for
DDS diagnostics tools (e.g. `ddsmon`) to attribute published samples to this process.
Always call `EnableSenderTracking` immediately after participant creation.

---

## Related Projects

### Direct Dependencies

| Project | Role |
|---------|------|
| [Hrot.StrideMock](../../Hrot/Hrot.StrideMock.md) | Provides the entire simulation core: `StrideNodeBootstrapper`, `SyncFdpToStrideScript`, `FakeStrideScript`, `FakeStrideEntity`, `FakeStrideEffect`. All simulation logic lives here. |
| [Fdp.Presentation](../../../../FDP/Engine/Fdp.Presentation.md) | Provides `FdpApplication` (the base class), `ApplicationConfig`, `RaylibInputProvider`. The Raylib + ImGui rendering shell. |
| [Hrot.Network.NED](../../Hrot/Network/Hrot.Network.NED.md) | NED (Network Entity Data) protocol translators. `NedNetworkFactory` wires DDS topics to ECS components. |

### Sibling Runners

| Project | Description |
|---------|-------------|
| `Hrot.ClusterRunner` | Full multi-subsystem runner that hosts `StrideMockSubsystem` (and others) inside a multi-tab Raylib window. `FakeStrideApp` is the single-subsystem equivalent. |
| `Hrot.ClusterRunner.Tests` | Unit tests for the cluster runner. |
| `Hrot.ClusterRunner.Integration.Tests` | Integration tests that spin up a real DDS domain. |

### Subsystems That Share the Same Core

| Project | Relationship |
|---------|-------------|
| `Hrot.StrideMock` (`StrideMockSubsystem`) | Uses the same `StrideNodeBootstrapper` + `SyncFdpToStrideScript` pair, but wraps them as an `ISubsystem` tab inside `ClusterRunner` rather than as a standalone process. The rendering code in `OnDrawWorld` in `FakeStrideApp` is the standalone equivalent of `StrideMockSubsystem`'s render panel. |

### Simulation Core

| Project | Role |
|---------|------|
| `Hrot.SimHost` | The simulation host subsystem — runs the authoritative ECS world on a separate node. `FakeStrideApp` connects to `SimHost` via DDS to receive entity state. |
| `Hrot.IG` (Image Generator) | Produces `SimTransform`, `VisualEffectState`, `TracerTarget` ECS components that `SyncFdpToStrideScript` reads. |
| `Fdp.Examples.Scenarios.Integrated` | Contains `UrbanCombatNewScenario`, whose TKB templates are registered in step 5 of `OnLoad`. |

---

## Architecture Decision Record

### ADR-1: Standalone Process vs Subsystem Tab

**Decision**: Provide both a standalone executable (`Hrot.FakeStrideApp`) and a
subsystem tab (`StrideMockSubsystem` inside `ClusterRunner`).

**Rationale**: The standalone executable is faster to boot and easier to debug in
isolation. The subsystem tab is needed for cluster-level integration testing where all
nodes run in a single process. Both share the same simulation core (`StrideNodeBootstrapper`),
so the duplication is minimal (only the rendering shell differs).

### ADR-2: No Real Stride Dependency

**Decision**: Use zero Stride NuGet packages in both `Hrot.FakeStrideApp` and
`Hrot.StrideMock`.

**Rationale**: Stride has a complex content pipeline and requires GPU resources. Keeping
the mock Stride-free allows it to run in CI, on Linux, and on machines without a
discrete GPU. The `FakeStrideScript` / `FakeStrideEntity` / `FakeStrideEffect` API
surface is designed to be interface-compatible with future Stride types so that Stage 2
integration requires only a type swap, not an architectural change.

### ADR-3: Two-Pass Differential Sync in SyncFdpToStrideScript

**Decision**: Use a destruction pass followed by a creation/update pass each frame
rather than event-driven lifecycle callbacks.

**Rationale**: `PlaybackSystem` during replay and seek blasts raw ECS memory without
firing lifecycle events. A two-pass approach based on `EntityRepository.IsAlive()` is
correct for all cluster states (live, replay, seek) without any event subscriptions.

---

## Appendix: FdpApplication Lifecycle (Inherited)

The following is a condensed view of `FdpApplication.Run()` for reference:

```csharp
public void Run()
{
    InitializeWindow();   // Raylib.InitWindow + rlImGui.Setup
    OnLoad();             // -- derived class initializes simulation
    while (!WindowShouldClose() && !_shouldQuit)
    {
        float dt = Raylib.GetFrameTime();
        OnUpdate(dt);
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGray);
        OnDrawWorld();    // -- 2-D/3-D scene
        rlImGui.Begin();
        OnDrawUI();       // -- ImGui overlays
        rlImGui.End();
        Raylib.EndDrawing();
    }
    OnUnload();           // -- derived class disposes simulation
    ShutdownWindow();     // Raylib.CloseWindow
}
```

The `Quit()` method sets `_shouldQuit = true`, which causes the loop to exit cleanly at
the end of the current frame and then call `OnUnload()`. This is the recommended way to
stop the application from non-UI code.

---

## Appendix: Visual Effect Rendering Details

| Effect Type | Raylib Primitive | Visual |
|-------------|-----------------|--------|
| `EffectType.Explosion` | `DrawCircleV` | Orange circle. Radius grows from 5 to 13 as `alpha` decreases from 1 to 0. Alpha channel fades in step with `effect.Alpha * 255`. |
| `EffectType.Tracer` | `DrawLineV` | Yellow line from `effect.Position` to `effect.TracerEnd` (XY plane, Z ignored). |
| Live entity (no effect) | `DrawCircleV` | Solid red circle, fixed radius 5, at `entity.Position.XY`. |

All rendering is in camera-transformed 2-D space (`_core.Camera.BeginMode()` /
`_core.Camera.EndMode()`). The `MapCamera` provides flat-Earth pan, zoom, and rotation
using standard Raylib 2-D camera transforms.
