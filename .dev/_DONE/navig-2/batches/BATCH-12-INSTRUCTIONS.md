# BATCH-12 Implementation Instructions

## Scope
- **NAV-P6-T6**: Wire `EngineBackedNavigationModule` into `SimHostNodeBootstrapper`
- **NAV-P7-T1**: Create `FakeNavigationInspectorWindow` (4-tab ManagedWindow, headless-guarded)
- **NAV-P6-T7**: Engine-backed mode detection in the diagnostic window

**Current test count**: 255 passing (after BATCH-11, commit `dada7a3d`)  
**Target**: ≥ 255 passing (these tasks are UI/wiring with no new unit tests required)

---

## Context

### Workspace root
`d:\Work\IOS-IG-SimHost-FDP-2`

### Key assemblies
- `FDP/Toolkits/Fdp.Toolkits` — navigation fakes + providers (namespace `Fdp.Toolkit.Navigation.*`)
- `Hrot/Subsystems/Hrot.SimHost` — SimHost host, references `Fdp.Presentation` and `Fdp.Toolkits`
- `Fdp.Presentation.WindowManager` — has `ManagedWindow`, `WindowManager`, `WindowScope`

### Existing files to read before implementing
1. `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs` — add nav module registration
2. `Hrot/Subsystems/Hrot.SimHost/Windows/SimHostWindows.cs` — `ManagedWindow` usage pattern
3. `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavigationModule.cs` — what was created in BATCH-11
4. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavigationFakesModule.cs` — the fake module pattern
5. `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/Windows/FakeAnimBackendInspectorWindow.cs` — precedent window pattern
6. `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — to understand how windows are registered

---

## Task 1: NAV-P6-T6 — Wire `EngineBackedNavigationModule` into SimHostNodeBootstrapper

### File to modify
`Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs`

### Change needed
Add `using Fdp.Toolkit.Navigation.EngineBacked;` at the top.

In the `RegisterSpawningPipeline` method, after the `EqsModule` registration line:
```csharp
context.Kernel.RegisterModule(new EqsModule());
```

Add:
```csharp
// Register engine-backed navigation providers (road-graph + direct-line stubs).
var navModule = new EngineBackedNavigationModule(
    RoadNetwork ?? default(CarKinem.Road.RoadNetworkBlob),
    CoreLogicPack!.TrajectoryPool);
context.Kernel.RegisterModule(navModule);
navModule.RegisterProviders(context.World);
```

**IMPORTANT**: `RoadNetwork` is populated in `PopulateSystems` which runs before `RegisterSpawningPipeline`, so it will be non-null after road network loading. Use `RoadNetwork ?? default(...)` as defensive null handling.

Also check: does `Hrot.SimHost.csproj` already reference `Fdp.Toolkits`? If not, add the reference. (It likely already does since `SimHostCoreLogicPack` references `GroundKinematicsModule` from toolkits.)

---

## Task 2: NAV-P7-T1 — FakeNavigationInspectorWindow

### File to create
`Hrot/Subsystems/Hrot.SimHost/Windows/FakeNavigationInspectorWindow.cs`

### Implementation
The window is a `ManagedWindow` subclass. It has 4 tabs (Navmesh, Crowd, Volumetric, Paths). Headless-guarded means it's only registered when NOT in headless mode.

Follow the `SimHostControlsWindow` pattern from `SimHostWindows.cs`.

```csharp
using System;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Navigation.Fake;

namespace Hrot.SimHost.Windows;

/// <summary>
/// NAV-P7-T1: Four-tab ImGui diagnostic window for fake navigation backends.
/// Registered via SimHostNodeBootstrapper in non-headless mode.
/// Detects active provider type at draw time (NAV-P6-T7).
/// </summary>
internal sealed class FakeNavigationInspectorWindow : ManagedWindow
{
    private readonly Func<EntityRepository?> _repoGetter;

    public FakeNavigationInspectorWindow(Func<EntityRepository?> repoGetter)
        : base("fake_nav_inspector", "Fake Navigation Backends", "Navigation", WindowScope.Global)
    {
        _repoGetter = repoGetter;
    }

    protected override void DrawClientArea()
    {
        var repo = _repoGetter();
        if (repo == null)
        {
            ImGui.TextDisabled("No world available.");
            return;
        }

        DrawHeader(repo);

        if (ImGui.BeginTabBar("nav_tabs"))
        {
            if (ImGui.BeginTabItem("Navmesh"))  { DrawNavmeshTab(repo);   ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Crowd"))    { DrawCrowdTab(repo);     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Volumetric")){ DrawVolumetricTab(repo); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Paths"))    { DrawPathsTab(repo);     ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHeader(EntityRepository repo)
    {
        // Detect active backend
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        string backendLabel = navmesh switch
        {
            EngineBackedNavmeshProvider => "Backend: EngineBacked (road graph + direct-line)",
            FakeNavmeshProvider         => "Backend: FakeNavmeshProvider + FakeDtCrowdProvider + FakeVolumetricPathProvider",
            null                         => "Backend: none (no providers registered)",
            _                            => $"Backend: {navmesh.GetType().Name}",
        };
        ImGui.TextDisabled(backendLabel);
        ImGui.Separator();
    }

    private void DrawNavmeshTab(EntityRepository repo)
    {
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        if (navmesh is EngineBackedNavmeshProvider)
        {
            ImGui.TextDisabled("No navmesh layers loaded — direct-line provider in use.");
            ImGui.TextDisabled("All IsWalkable queries return true.");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Navmesh controls are not available in engine-backed mode.");
            return;
        }
        if (navmesh is FakeNavmeshProvider fakeNav)
        {
            ImGui.Text("FakeNavmeshProvider active.");
            ImGui.TextDisabled("(Detailed polygon tree not yet implemented — NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No navmesh provider registered.");
    }

    private void DrawCrowdTab(EntityRepository repo)
    {
        var crowd = repo.GetSingletonManaged<IDtCrowdProvider>();
        if (crowd is EngineBackedDtCrowdProvider)
        {
            ImGui.TextDisabled("Crowd avoidance disabled — stub provider in use.");
            ImGui.TextDisabled("Humanoids move via LinearKinematicsSystem.");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Crowd controls not available in engine-backed mode.");
            return;
        }
        if (crowd is FakeDtCrowdProvider fakeCrowd)
        {
            ImGui.Text("FakeDtCrowdProvider active.");
            ImGui.TextDisabled("(Agent list not yet implemented — NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No crowd provider registered.");
    }

    private void DrawVolumetricTab(EntityRepository repo)
    {
        var vol = repo.GetSingletonManaged<IVolumetricPathProvider>();
        if (vol is EngineBackedVolumetricPathProvider)
        {
            ImGui.TextDisabled("Volumetric path provider: direct-line stub.");
            ImGui.TextDisabled("All IsFlyable queries return true.");
            return;
        }
        if (vol is FakeVolumetricPathProvider fakeVol)
        {
            ImGui.Text("FakeVolumetricPathProvider active.");
            ImGui.TextDisabled("(No-fly zone list not yet implemented — NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No volumetric path provider registered.");
    }

    private void DrawPathsTab(EntityRepository repo)
    {
        var pathReg = repo.GetSingletonManaged<IPathRegistry>();
        if (pathReg == null)
        {
            ImGui.TextDisabled("No path registry registered.");
            return;
        }
        ImGui.Text($"Path registry: {pathReg.GetType().Name}");
        ImGui.TextDisabled("(Path pool table not yet implemented — NAV-P7-T1 Phase 2)");
    }
}
```

**IMPORTANT notes**:
- Check how `ImGui` is used in other window files (e.g., `SimHostWindows.cs`, `SimHostVisualization.cs`). It's likely `ImGuiNET.ImGui` — use the same import.
- `WindowScope.Global` — verify this is a valid scope value (look at `WindowScope` enum in `Fdp.Presentation.WindowManager`). The `FakeAnimBackendInspectorWindow` uses `WindowScope.PerspectiveBound` — may need to use the same or `Global`. Check both options.
- `GetSingletonManaged<IDtCrowdProvider>()` — `IDtCrowdProvider` is not registered as a managed singleton by `EngineBackedNavigationModule.RegisterProviders` (the subagent intentionally left it unregistered since it may not have a `[ComponentId]`). Check what IS actually registered. Look at `EngineBackedNavigationModule.RegisterProviders` to see which singletons are set. Only `INavmeshProvider` and `IPathRegistry` were registered. Adjust the window to only read registered singletons.
- The actual import for `ImGuiNET` needs to match what's used in the project.

---

## Task 3: NAV-P6-T7 — Register window from bootstrapper

### File to modify
`Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs`

After the nav module registration (from Task 1), also register the window:

```csharp
// Register diagnostic window in non-headless mode.
// The Func<EntityRepository?> getter is captured from context.
if (!HeadlessMode.IsHeadless)
{
    // Windows are registered in ApplicationSystemsRegistrar callback
    // (or via the existing SimHostSubsystem window registration path).
    // For now, store the window reference for later registration.
    // See SimHostApp.cs for the window manager hookup pattern.
}
```

Actually, the window registration may need to follow the existing pattern in `SimHostApp.cs`. Rather than registering from `SimHostNodeBootstrapper` directly, check how other windows (like `SimHostControlsWindow`) are registered in `SimHostApp.cs`. Mirror that pattern to add `FakeNavigationInspectorWindow`.

**Alternative approach**: If `SimHostNodeBootstrapper.ApplicationSystemsRegistrar` is the hook for additional systems, use that. Or look at how `SimHostSubsystem` registers windows and add the nav window there.

**Read `SimHostApp.cs` and `SimHostSubsystem.cs` to find the right registration point.**

---

## Build and test

```powershell
# Build the full solution
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-Object -Last 40

# Run unit tests (should still be 255+)
cd FDP\Toolkits
dotnet test Fdp.Toolkits.Tests 2>&1 | Select-Object -Last 10
```

## Validation checklist
- [ ] `dotnet build` for `Hrot.SimHost` succeeds (0 errors)
- [ ] `FakeNavigationInspectorWindow` compiles with correct `ManagedWindow` base class
- [ ] `SimHostNodeBootstrapper.RegisterSpawningPipeline` registers `EngineBackedNavigationModule` + calls `RegisterProviders`
- [ ] 255 navigation unit tests still pass (no regressions)
- [ ] Window class has `DrawNavmeshTab` that detects `EngineBackedNavmeshProvider` vs `FakeNavmeshProvider` at draw time (NAV-P6-T7 requirement)
- [ ] Window is headless-guarded (not registered in headless/test builds)

## Report back
When done, report:
1. Build status (0 errors required)
2. Test count (should still be ≥ 255)
3. Files created/modified
4. Any compile errors encountered and how fixed
5. The exact pattern used to register the window (which file, which method)
