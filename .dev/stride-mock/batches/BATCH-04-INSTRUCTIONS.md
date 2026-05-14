# BATCH-04 Instructions

**Workstream:** stride-mock  
**Batch:** BATCH-04  
**Tasks:** CA-01 (corrective) + SM-008  
**Effort estimate:** 4-8 hours

---

## Context

Read first (do not duplicate here; reference instead):
- `.dev/stride-mock/DESIGN.md` — §8 (FakeStrideApp)
- `.dev/stride-mock/TASK-DETAILS.md` — SM-008 section
- `.dev/stride-mock/DEBT-TRACKER.md` — DT-004, DT-005

BATCH-03 Review findings that this batch must address:
- **CA-01 (P2):** `StrideMockSubsystem.Update()` never calls `Camera.HandleInput()`. The
  SC_SM006_6 success condition requires it when `IsActiveMapOwner()` returns true.
- **DT-005 (must resolve):** SM-008 design spec says call `DemoTkbSetup.RegisterAll(tkb)`
  in `OnLoad()`. Do NOT call it — `HrotNodeBuilder.Build()` internally calls
  `HrotEnvironment.CreateTkb()` which calls `NedTkbCatalog.RegisterAll(tkb)`, registering
  `TkbEntityTypes.Tank_M1Abrams = 100` before `OnLoad()` runs. Calling
  `DemoTkbSetup.RegisterAll` a second time would throw
  `InvalidOperationException: Template with TkbType '100' already exists`. Only call
  `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb)` (IDs 1001-2003, no overlap).

---

## CA-01: Fix Camera.HandleInput in StrideMockSubsystem

### File to modify
`Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs`

### Change required

In `Update(float deltaTime)`, replace the commented-out block:

```csharp
// Before (agent left this as a comment):
if (!_headless && _isActiveMapOwner())
{
    // In embedded ClusterRunner mode there is no standalone RaylibInputProvider;
    // camera input is handled by the orchestrator's window controller.
    // Calling HandleInput here with a null-like provider would be a no-op,
    // so we intentionally leave it unhooked until SM-008 wires the window.
}
```

With the actual call:

```csharp
// After:
if (!_headless && _isActiveMapOwner())
    _core.Camera.HandleInput(new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider());
```

Add `using Fdp.Toolkit.Vis2D.Defaults;` to the using block of `StrideMockSubsystem.cs`.

`RaylibInputProvider` is in `FDP\Engine\Fdp.Presentation\Vis2D\Defaults\RaylibInputProvider.cs`
— the `Fdp.Presentation.csproj` reference already exists in `Hrot.StrideMock.csproj`.

### Headless safety

`RaylibInputProvider` reads from Raylib via `Raylib.GetMousePosition()`, etc. In headless
mode (unit tests), this would crash. The `!_headless` guard in the condition prevents the
call in test contexts. The existing `Update_HeadlessAfterInitialize_DoesNotThrow` test
in `StrideMockSubsystemTests.cs` must still pass unchanged.

### Test to add (in `StrideMockSubsystemTests.cs`)

No new unit test is possible for this specific behavior (requires Raylib window). The fix is
verified by code inspection. The existing SC_SM006_6 test (headless no-throw) is sufficient
to confirm the guard is in place.

---

## SM-008: Implement FakeStrideApp

**Design Reference:** [DESIGN.md §8](../DESIGN.md#8-fakestrideapp-hrotfakestrideapp)  
**Task Reference:** [TASK-DETAILS.md SM-008](../TASK-DETAILS.md#sm-008--implement-fakestrideapp)

### Files to create / modify

| File | Action |
|------|--------|
| `Hrot\Runner\Hrot.FakeStrideApp\FakeStrideApp.cs` | Create |
| `Hrot\Runner\Hrot.FakeStrideApp\Program.cs` | Replace stub |
| `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\Hrot.FakeStrideApp.Tests.csproj` | Create (test project) |
| `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\FakeStrideAppTests.cs` | Create |

No `Hrot.FakeStrideApp.Tests` project exists yet. You must create and wire it.

### 1. `FakeStrideApp.cs`

```csharp
// Hrot\Runner\Hrot.FakeStrideApp\FakeStrideApp.cs
namespace Hrot.FakeStrideApp;

public sealed class FakeStrideApp : FdpApplication
{
    // See DESIGN.md §8.2 for the API contract
}
```

#### `OnLoad()` order (mandatory — do NOT reorder)

1. Read `domainId` and `nodeId` from constructor args (pass them in from Program.cs).
2. Create `DdsParticipant` via `HrotEnvironment.CreateParticipant(domainId)`.
3. Create `NedNetworkFactory(participant, new NetworkEntityMap(), HrotEnvironment.CreateGeoTransform(), new FdpEventBus(), nodeId, StrideNodeBootstrapper.Role)`.
4. Build `HrotNodeConfig`:
   ```csharp
   var config = new HrotNodeConfig
   {
       DomainId      = domainId,
       NodeId        = nodeId,
       Headless      = false,
       SubsystemName = "StrideMock",
       LocalTempRoot = Path.Combine(
           OrchestrationConstants.DefaultStagingDirectory,
           "nodes", $"node-{nodeId}"),
       LogDirectory  = Path.Combine(AppContext.BaseDirectory, "logs"),
   };
   ```
5. Create `StrideNodeBootstrapper _core = new StrideNodeBootstrapper()`.
6. Call `_core.BootstrapNode(config, StrideNodeBootstrapper.Role, networkFactory)`.
7. Extract and populate TKB **after** BootstrapNode:
   ```csharp
   var tkb = _core.Context.TkbDb;
   // NOTE: Do NOT call DemoTkbSetup.RegisterAll(tkb) here.
   // HrotNodeBuilder.Build() already calls HrotEnvironment.CreateTkb() which calls
   // NedTkbCatalog.RegisterAll(tkb), registering TkbEntityTypes.Tank_M1Abrams = 100.
   // Calling DemoTkbSetup.RegisterAll again would throw a duplicate-key exception.
   if (tkb != null)
       UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb); // IDs 1001-2003
   ```
8. Create `SyncFdpToStrideScript _script = new SyncFdpToStrideScript(_core)`.
9. Call `_script.Start()`.
10. Create `RaylibInputProvider _inputProvider = new RaylibInputProvider()`.

#### `OnUpdate(float dt)`

```csharp
_core.Camera.HandleInput(_inputProvider);  // always active (no tab switching)
_core.Camera.Update(dt);
_script.Update(dt);
_core.Tick(dt);
```

#### `OnDrawWorld()`

Draw logic identical to `StrideMockSubsystem.DrawWorld()` (without the headless guard
— FdpApplication only calls OnDrawWorld when the window is active):
1. `_core.Camera.BeginMode()`
2. Draw active entities as red circles (radius 5)
3. Draw effects: orange circles for Explosion, yellow lines for Tracer
4. `_core.Camera.EndMode()`

#### `OnDrawUI()`

Show ImGui splash window when `_script.CurrentStateMessage` is non-empty.

#### `OnUnload()`

```csharp
_core?.Dispose();
_core   = null;
_script = null;
```

Do NOT call `base.OnUnload()` — `FdpApplication.OnUnload()` disposes `Kernel` and `World`
which are owned by `StrideNodeBootstrapper`, not by `FakeStrideApp` directly.

### 2. `Program.cs` (replace stub)

```csharp
// Hrot\Runner\Hrot.FakeStrideApp\Program.cs
using Fdp.Presentation.Raylib;
using Hrot.FakeStrideApp;

// Default values; override with --domain <id> --node <id>
int domainId = 0;
int nodeId   = 700;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--domain") int.TryParse(args[i + 1], out domainId);
    if (args[i] == "--node")   int.TryParse(args[i + 1], out nodeId);
}

var appConfig = new ApplicationConfig
{
    WindowTitle = "FakeStrideApp — HROT Stride Mock",
    Width       = 1280,
    Height      = 720,
    TargetFPS   = 60,
};

using var app = new FakeStrideApp(appConfig, domainId, nodeId);
app.Run();
```

### 3. Test project

#### Hrot.FakeStrideApp.Tests.csproj

Create `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\Hrot.FakeStrideApp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.FakeStrideApp.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

Add the test project to `IOS-IG-SimHost.sln` (check if FDP.sln also needs it; FakeStrideApp
is in the Hrot subsystem, so IOS-IG-SimHost.sln is the primary target).

#### FakeStrideAppTests.cs

FakeStrideApp cannot be tested with a live Raylib window in unit tests. Focus on:
- Type/interface conformance
- Constructor does not throw
- Config defaults

```csharp
// SC_SM008_1: Type safety
[Fact]
public void FakeStrideApp_InheritsFromFdpApplication()
{
    Assert.True(typeof(Fdp.Presentation.Raylib.FdpApplication)
        .IsAssignableFrom(typeof(Hrot.FakeStrideApp.FakeStrideApp)));
}

// SC_SM008_1: Constructor does not throw with valid config
[Fact]
public void FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow()
{
    var config = new ApplicationConfig
    {
        WindowTitle = "Test", Width = 1280, Height = 720, TargetFPS = 60
    };
    var ex = Record.Exception(() =>
    {
        using var app = new FakeStrideApp(config, domainId: 0, nodeId: 700);
    });
    Assert.Null(ex);
}
```

Note: testing `OnLoad()` / `OnUpdate()` / `OnDrawWorld()` requires a Raylib window — skip
these in unit tests. The SC_SM008 integration success conditions (SC_SM008_2 through
SC_SM008_7) are verified by manual integration testing.

---

## Build & Test Targets

Run after implementation:

```
dotnet build "Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj"
dotnet test  "Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\Hrot.FakeStrideApp.Tests.csproj"
dotnet test  "Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj"
```

All 41 StrideMock.Tests must still pass after CA-01.

---

## Important Notes

1. **Do NOT reorder OnLoad steps** — step order is dictated by the 5 fragile init traps (see DESIGN.md §4.2). Specifically: DDS participant and network factory must be created before BootstrapNode, and TKB population must happen after BootstrapNode.

2. **DemoTkbSetup is not needed** — as explained in DT-005 and above. If you grep and find `DemoTkbSetup.RegisterAll` mentioned in TASK-DETAILS.md SM-008, that spec call is incorrect (pre-dates the discovery that NedTkbCatalog already registers TkbType 100 internally). Do NOT call it.

3. **Disposing correctly** — `StrideNodeBootstrapper.Dispose()` owns the kernel and world. Do NOT call `base.OnUnload()` after calling `_core.Dispose()`, as that would double-dispose those resources.

4. **FdpApplication constructor** — the base class requires an `ApplicationConfig` struct. Pass it through from Program.cs.

5. **CA-01 must be in the same commit** — the camera fix and FakeStrideApp are small enough to commit together.

---

## Report

Write the report to `.dev/stride-mock/reports/BATCH-04-REPORT.md`. Include:
- Files created / modified
- CA-01 fix description
- SM-008 implementation summary
- Test results (all tests that pass)
- Any deviations from spec and reasons
- DT-005 resolution note (confirm DemoTkbSetup.RegisterAll not needed)
- Suggested commit message

Resolve DT-005 in DEBT-TRACKER.md (mark RESOLVED) only after confirming that
`UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` registers successfully in
FakeStrideApp's OnLoad path (i.e., the FakeStrideApp.csproj builds and tests pass).
