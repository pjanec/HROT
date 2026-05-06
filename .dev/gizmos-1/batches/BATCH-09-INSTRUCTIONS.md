# BATCH-09 — GZ021 Map Measure Tool Gizmo

**Workstream:** FDP Declarative Gizmo & Presentation Framework
**Task reference:** `.dev/gizmos-1/TASK-DETAIL.md`, `.dev/gizmos-1/TASK-TRACKER.md`

---

## Overview

This batch implements the **map measure tool gizmo** — the last remaining sub-item of
TASK-GZ021. The tool becomes activatable via gizmo settings (instead of only via the scenario
editor context menu), with configurable distance units.

The spatial grid gizmo is deferred to DEBT-TRACKER as it requires infrastructure changes to
expose `SpatialHashGrid` via a public service interface.

---

## Approach

The measure tool is a canvas-level concept (`IMapTool`). Since gizmo systems are ECS-bound and
can't push tools onto the canvas directly, a presentation-layer adapter class
(`MeasureToolGizmoAdapter`) bridges the two: it monitors the `GizmoSettingsRegistry` and
pushes/pops the `MeasureTool` on `MapCanvas` accordingly.

```
GizmoSettingsRegistry --[Active=true]--> MeasureToolGizmoAdapter.Update() --> canvas.PushTool(measureTool)
GizmoSettingsRegistry --[Active=false]--> MeasureToolGizmoAdapter.Update() --> canvas.PopTool()
```

---

## Key Reference Files (read before implementing)

- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` — for settings registration pattern
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoSettings.cs` — for settings key constant pattern
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureTool.cs` — existing tool to wrap
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureToolConstants.cs` — constants
- `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — `PushTool`, `PopTool`, `ActiveTool`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoSettingsRegistry.cs` — `Read`, `ComputeHash`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — where to wire the adapter (InitializeEcs + Update)

---

## GizmoSettingValue reminder

- `GizmoSettingValue.From(bool value)` — creates bool setting
- `GizmoSettingValue.From(int value)` — creates int setting  
- `.BoolValue` reads bool, `.IntValue` reads int
- `GizmoSettingsRegistry.ComputeHash(string key)` — FNV-1a hash of key string

---

## Files to create

### 1. `Hrot/Subsystems/Hrot.IG/Gizmos/MeasureToolGizmoSettings.cs`

```csharp
namespace Hrot.IG.Gizmos
{
    internal static class MeasureToolGizmoSettings
    {
        /// <summary>Bool: true = measure tool is active on the canvas.</summary>
        public const string Active = "MeasureTool.Active";

        /// <summary>Int: 0 = meters, 1 = kilometers.</summary>
        public const string Units = "MeasureTool.Units";

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(Active, GizmoSettingValue.From(false));
            settings.RegisterSetting(Units, GizmoSettingValue.From(0));
        }
    }
}
```

### 2. `Hrot/Subsystems/Hrot.IG/Gizmos/MeasureToolGizmoAdapter.cs`

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// Bridges the gizmo settings system and the MapCanvas tool stack.
    /// Pushes <see cref="MeasureTool"/> when the Active setting is true;
    /// pops it when the setting turns false.
    /// Call <see cref="Update"/> once per frame from IgApplication.
    /// </summary>
    internal sealed class MeasureToolGizmoAdapter
    {
        private readonly MapCanvas              _canvas;
        private readonly GizmoSettingsRegistry  _settings;
        private readonly MeasureTool            _tool;

        private readonly uint _activeHash;
        private readonly uint _unitsHash;

        private bool _wasActive;

        public MeasureToolGizmoAdapter(
            MapCanvas canvas,
            GizmoSettingsRegistry settings)
        {
            _canvas   = canvas   ?? throw new ArgumentNullException(nameof(canvas));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tool     = new MeasureTool();
            _activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            _unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);
        }

        /// <summary>
        /// Call once per frame (from IgApplication.Update) before canvas.Update().
        /// </summary>
        public void Update()
        {
            bool active = _settings.Read(_activeHash).BoolValue;

            if (active && !_wasActive)
            {
                // Sync units before pushing
                SyncUnits();
                _canvas.PushTool(_tool);
            }
            else if (!active && _wasActive)
            {
                // Only pop if our tool is the active one
                if (_canvas.ActiveTool == _tool)
                    _canvas.PopTool();
            }
            else if (active && _wasActive)
            {
                // Refresh units every frame in case they changed
                SyncUnits();
            }

            _wasActive = active;
        }

        private void SyncUnits()
        {
            int units = _settings.Read(_unitsHash).IntValue;
            _tool.DisplayUnits = units == 1 ? MeasureDisplayUnits.Kilometers : MeasureDisplayUnits.Meters;
        }
    }
}
```

---

## Files to modify

### 3. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureTool.cs`

Add `DisplayUnits` property and a `MeasureDisplayUnits` enum. Add it at the top of the file (before the class) or in a separate file if preferred:

```csharp
public enum MeasureDisplayUnits { Meters = 0, Kilometers = 1 }
```

Add to `MeasureTool`:
```csharp
/// <summary>Unit system for distance display. Default is meters.</summary>
public MeasureDisplayUnits DisplayUnits { get; set; } = MeasureDisplayUnits.Meters;
```

Modify the `Draw` method's label formatting to use units:
```csharp
// Replace:
string label = $"{distance:F1} m";

// With:
string label = DisplayUnits == MeasureDisplayUnits.Kilometers
    ? $"{distance / 1000f:F3} km"
    : $"{distance:F1} m";
```

**IMPORTANT**: Check the exact line in `MeasureTool.Draw()` before editing — the label formatting is inside the method body. Only change the label line; leave all other draw code untouched.

### 4. `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`

Add `MeasureToolGizmoSettings.Register(settings)` call inside the existing `Register` static method.

### 5. `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Add field:
```csharp
private MeasureToolGizmoAdapter? _measureToolGizmoAdapter;
```

In `InitializeEcs()` (after `_gizmoRegistry` and canvas layer creation), add:
```csharp
_measureToolGizmoAdapter = new MeasureToolGizmoAdapter(_canvas, gizmoSettingsRegistry);
```

Note: `gizmoSettingsRegistry` must be the `GizmoSettingsRegistry` instance. Check how
`GizmoRegistrar.Register` is called to find where the registry is declared.

In `Update(float dt)`, before `_canvas.Update(dt)` call, add:
```csharp
_measureToolGizmoAdapter?.Update();
```

**IMPORTANT**: Read the actual `InitializeEcs()` and `Update()` method bodies before editing.
Find the exact location by searching for the existing BATCH-07 wiring code (gizmoBuffer, gizmoRegistry lines).

---

## Tests to write

Test file: `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/MeasureToolGizmoTests.cs`

### Test: SC-GZ021-MT-1 — Adapter pushes tool when Active becomes true

```
Given: adapter with canvas + settings (Active=false initially)
When: set Active=true, call adapter.Update()
Then: canvas.ActiveTool is the MeasureTool instance
```

### Test: SC-GZ021-MT-2 — Adapter pops tool when Active becomes false

```
Given: adapter with Active=true (tool already pushed)
When: set Active=false, call adapter.Update()
Then: canvas.ActiveTool is null (or original tool if one existed)
```

### Test: SC-GZ021-MT-3 — Units sync: km selected -> tool.DisplayUnits = Kilometers

```
Given: adapter, settings with Units=1 (km)
When: set Active=true, call adapter.Update()
Then: _tool.DisplayUnits == MeasureDisplayUnits.Kilometers
```

### Test: SC-GZ021-MT-4 — Settings registered: Active and Units in GizmoSettingsRegistry

```
Given: fresh GizmoSettingsRegistry
When: MeasureToolGizmoSettings.Register(settings) called
Then: EnumerateAll() contains "MeasureTool.Active" and "MeasureTool.Units"
```

### Test: SC-GZ021-MT-5 — MeasureTool label in km: distance 1500m -> "1.500 km"

```
Given: MeasureTool with DisplayUnits=Kilometers, LastMeasuredDistanceMeters=1500f
When: verify label format (can use TestHook or manual compute)
Then: formatted string is "1.500 km"
```

### Test canvas for adapter tests

`MapCanvas` requires an `IInputProvider`. Use test constructor:
```csharp
var canvas = new MapCanvas(input: null); // RaylibInputProvider is default, may fail in headless
```

If `MapCanvas` constructor fails without a real input provider, create a simple `FakeInputProvider : IInputProvider` that returns default values. Check `IInputProvider` interface in `FDP/Engine/Fdp.Presentation/Vis2D/`.

If `MapCanvas` uses Raylib calls that fail in test, create a `TestMapCanvas` subclass that overrides `PushTool`/`PopTool`/`ActiveTool` tracking without actual Raylib. Actually, look at how `MeasureTool` tests in the codebase test push/pop behavior (check `MapCanvas` tests if they exist).

Alternative: test via `_tool.IsMeasuring` state is not directly testable without pushing. Instead:
- Test `canvas.ActiveTool == _tool` after push (if MapCanvas works headlessly)
- Or test `_tool.DisplayUnits` value which doesn't need canvas

Adapt based on what actually compiles and runs.

---

## DEBT-TRACKER update

Add entry D-005 to `.dev/gizmos-1/DEBT-TRACKER.md`:

```
## D-005: Spatial grid global gizmo deferred

**Area**: GZ021 concrete gizmos
**Description**: The spatial grid global gizmo requires `SpatialHashGrid` (from `CarKinem.Spatial`,
internal to `PerceptionModule`) to be exposed via a public service interface (ISpatialGridView or
similar). This interface does not currently exist. Implementing it requires FDP changes and a new
service registration in the perception module, which is scope for a separate workstream.
**Impact**: Low — spatial grid visualization is a diagnostic aid, not a correctness requirement.
**Mitigation**: None; gizmo not implemented.
```

---

## Build and test commands

```
dotnet build Hrot\Engine\Hrot.Presentation\Hrot.Presentation.csproj --nologo
dotnet build Hrot\Subsystems\Hrot.IG\Hrot.IG.csproj --nologo
dotnet build Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --nologo
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmo"
```

Target: 0 build errors, 5 new tests pass, 33+ total.

---

## Deliverables

1. `MeasureToolGizmoSettings.cs` (new)
2. `MeasureToolGizmoAdapter.cs` (new)
3. `MeasureTool.cs` (modified — DisplayUnits property + enum + label)
4. `GizmoRegistrar.cs` (modified — add MeasureToolGizmoSettings.Register)
5. `IgApplication.cs` (modified — adapter field + construction + Update call)
6. `MeasureToolGizmoTests.cs` (new tests)
7. `.dev/gizmos-1/DEBT-TRACKER.md` (D-005 entry added)
8. `BATCH-09-REPORT.md` in `.dev/gizmos-1/reports/`
