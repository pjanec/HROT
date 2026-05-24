# BATCH-07 Instructions — Gizmo Renderer Wiring & Entity Health Bar (GZ020 + GZ021 partial)

## Onboarding

**Design reference:** `.dev/gizmos-1/DESIGN.md` (§2.4, §7.1)
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` (GZ020, GZ021)

**Previous batch reviews:**
- `.dev/gizmos-1/reviews/BATCH-06-REVIEW.md` — Remote visualization foundation approved.

**What exists already:**
- Full gizmo primitive/buffer/settings/system stack in `FDP/Toolkits/Fdp.Toolkits/`
- `DebugGizmoLayer` in `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
- `DebugPrimitiveRenderer2D` in `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`
- `GizmoRegistry`, `DataDrivenGizmoSystem` in Fdp.Toolkits
- `GlobalDebugSettings` ECS singleton in `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs`
- `IgHealthState` component at `Hrot/Engine/Hrot.Core/Components/Map/IgHealthState.cs`
  - `public struct IgHealthState { public float Damage; }` — 0=healthy, 100=destroyed
  - ComponentId = 165
- `IgApplication` in `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
  - Has `private MapCanvas _canvas` and `private EntityRepository _world`
  - Has `_canvas.AddLayer(...)` calls in `InitializeEcs()` (around line 750) and another group ~line 1050-1110
  - The last existing layer add before `SwitchTool` is `new Hrot.IG.Layers.ZoneObstacleRenderLayer(_world)`

**Next available HrotComponentId:** 186 (check HrotComponentIds.cs to confirm 185=GlobalDebugSettings is the last)

---

## TASK-GZ020 — Local Gizmo Renderer Wiring in IgApplication

**Goal:** Wire the gizmo subsystem into IgApplication so gizmos are rendered on the 2D map canvas.

**Files to modify:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

### What to do:

1. **Add a `DebugPrimitiveBuffer` field** to `IgApplication`:
   ```csharp
   private DebugPrimitiveBuffer? _gizmoBuffer;
   ```
   Using namespace: `Fdp.Toolkit.Diagnostics.Gizmos`

2. **Add a `GizmoRegistry` field**:
   ```csharp
   private GizmoRegistry? _gizmoRegistry;
   ```
   Using namespace: `Fdp.Toolkit.Diagnostics.Gizmos`

3. **Initialize gizmo subsystem** in `InitializeEcs()`, just before the last existing layer additions (after line ~1104 where `ZoneObstacleRenderLayer` is added):
   ```csharp
   // --- Gizmo subsystem ---
   _gizmoBuffer   = new DebugPrimitiveBuffer(capacity: 4096);
   _gizmoRegistry = new GizmoRegistry();
   // DataDrivenGizmoSystem is registered in InitializeModules()
   var gizmoLayer = new DebugGizmoLayer(31, _gizmoBuffer, _world.Bus);
   _canvas.AddLayer(gizmoLayer);
   ```
   
   IMPORTANT: Read `DebugGizmoLayer`'s constructor signatures from the actual file before writing this. The backward-compatible constructor may be `(int layerBitIndex = 31)` (old) plus a new overload `(int layerBitIndex, DebugPrimitiveBuffer buffer, FdpEventBus eventBus)`.

4. **Register DataDrivenGizmoSystem in the kernel** — find where `_kernel` is set up (look for `_kernel = new ModuleHostKernel(...)` or `_kernel.RegisterSystem(...)`). Add:
   ```csharp
   // Register gizmo system — must happen before kernel.Initialize()
   if (_gizmoRegistry != null && _gizmoBuffer != null)
   {
       _kernel.RegisterSystem(new DataDrivenGizmoSystem(
           _gizmoRegistry,
           _gizmoBuffer,
           isSelectedPredicate: null)); // wired to GlobalDebugSettings in D-003 future work
   }
   ```
   
   IMPORTANT: Verify `DataDrivenGizmoSystem` constructor signature from actual file. It may be
   `(GizmoRegistry registry, IDebugDrawBuilder builder, Func<ISimulationView, Entity, bool>? isSelectedPredicate = null)`.

5. **Expose `GizmoRegistry`** for registration of concrete gizmos:
   ```csharp
   public GizmoRegistry? GizmoRegistry => _gizmoRegistry;
   ```
   This allows external code (concrete gizmo wiring) to call `_gizmoRegistry.Register(...)`.

6. **Export `DebugPrimitiveBuffer`** for diagnostics/testing:
   ```csharp
   internal DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer;
   ```

### Important checks before coding:

- Read `DebugGizmoLayer.cs` — verify constructor signature (does it accept `FdpEventBus` or `IFdpEventBus`?)
- Read `DataDrivenGizmoSystem.cs` — verify constructor signature
- Read `GizmoRegistry.cs` — verify `Register(IGizmoDefinition)` signature
- Check if `Hrot.IG.csproj` already references `Fdp.Toolkits` and `Fdp.Presentation` — if not, add them
- Check if `_world.Bus` is accessible as a `FdpEventBus` field

### Tests for GZ020

In `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmoRendererWiringTests.cs`:

- **SC-GZ020-1:** After headless `InitializeEmbedded`, `app.GizmoRegistry` is not null.
- **SC-GZ020-2:** After headless `InitializeEmbedded`, `app.GizmoBuffer` is not null.
- **SC-GZ020-3:** Calling `app.GizmoRegistry.Register(gizmoDef)` where `gizmoDef` requires component 165
  (IgHealthState) adds the definition to registry without throwing.

Test helper: use the existing headless pattern from `Hrot.IG.Tests` — look at how other Hrot.IG
integration tests initialize `IgApplication` with headless mode.

---

## TASK-GZ021 (partial) — Entity Health Bar Gizmo

**Goal:** Implement the entity health bar as a rendering-only `IGizmoDefinition + IStatefulGizmo`
that uses `IDebugDrawBuilder` to draw colored rectangles above entities.

**Target project:** `Hrot/Subsystems/Hrot.IG/`

### Settings

Create `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoSettings.cs`:
```csharp
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    // Keys are FNV-1a hashes of the strings below; computed via GizmoSettingsRegistry.ComputeHash().
    // These are pre-computed for documentation; the actual registration uses the string.
    public static class HealthBarGizmoSettings
    {
        public const string BarHeightKey = "HealthBar.BarHeight";
        public const string BarWidthKey  = "HealthBar.BarWidth";

        public static readonly GizmoSettingValue DefaultBarHeight = GizmoSettingValue.From(6f);  // pixels
        public static readonly GizmoSettingValue DefaultBarWidth  = GizmoSettingValue.From(40f); // pixels
    }
}
```

### IGizmoDefinition implementation

Create `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoDefinition.cs`:

```csharp
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;
using Hrot.Map.Definitions;

namespace Hrot.IG.Gizmos
{
    public sealed class HealthBarGizmoDefinition : IGizmoDefinition
    {
        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmoDefinition(GizmoSettingsRegistry settings)
        {
            _settings = settings;
        }

        // Requires IgHealthState component (ComponentId 165)
        public int[] RequiredComponents => new[] { HrotComponentIds.IgHealthState };

        // Always visible (rendering-only gizmo, no selection dependency)
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IStatefulGizmo CreateInstance() => new HealthBarGizmoInstance(_settings);
    }
}
```

### IStatefulGizmo implementation

Create `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoInstance.cs`:

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Components;

namespace Hrot.IG.Gizmos
{
    internal sealed class HealthBarGizmoInstance : IStatefulGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmoInstance(GizmoSettingsRegistry settings)
        {
            _settings = settings;
        }

        public void OnInitialize(ISimulationView view, Entity entity) { }

        public void UpdateAndDraw(ISimulationView view, Entity entity, IDebugDrawBuilder draw, bool isSelected)
        {
            if (!view.HasComponent<IgHealthState>(entity)) return;

            ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
            float damage   = health.Damage;
            float healthPct = 1f - (damage / 100f);

            // Read settings for bar dimensions
            float barWidth  = _settings.Read(GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarWidthKey)).AsFloat;
            float barHeight = _settings.Read(GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarHeightKey)).AsFloat;

            // Color: green=healthy, yellow=damaged, red=critical
            Rgba32 color = healthPct >= 0.66f ? Rgba32.Green
                         : healthPct >= 0.33f ? Rgba32.Yellow
                         : Rgba32.Red;

            // Draw as entity-local badge text showing health percentage
            // (The gizmo framework does not yet have a Box2D draw call for the 2D bar;
            //  use DrawEntityBadge as a placeholder rich-text indicator instead)
            var text = new Fdp.Core.FixedString32($"{(int)(healthPct * 100)}%");
            draw.DrawEntityBadge(entity, text);
        }

        public void OnTeardown(ISimulationView view, Entity entity) { }
    }
}
```

**IMPORTANT:** Check the `GizmoSettingValue` struct's actual property names — the session notes say
it uses `Type`/`BoolValue`/`FloatValue`/`IntValue` rather than `.Kind`/`.AsBool`/`.AsFloat`. Read
`GizmoSettingValue.cs` before writing the `Read(hash).AsFloat` calls and adapt accordingly.

### Register the health bar gizmo

Create `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`:

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    // Registers all concrete gizmo definitions with the GizmoRegistry.
    // Call once after IgApplication.Initialize().
    public static class GizmoRegistrar
    {
        public static void Register(GizmoRegistry registry, GizmoSettingsRegistry settings)
        {
            // Register HealthBar settings defaults
            uint heightKey = GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarHeightKey);
            uint widthKey  = GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarWidthKey);
            settings.RegisterSetting(HealthBarGizmoSettings.BarHeightKey, HealthBarGizmoSettings.DefaultBarHeight);
            settings.RegisterSetting(HealthBarGizmoSettings.BarWidthKey,  HealthBarGizmoSettings.DefaultBarWidth);

            // Register the gizmo definition
            registry.Register(new HealthBarGizmoDefinition(settings));
        }
    }
}
```

### Tests for health bar gizmo

In `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HealthBarGizmoTests.cs`:

- **SC-GZ021-HB-1:** `HealthBarGizmoDefinition.RequiredComponents` contains `HrotComponentIds.IgHealthState`.
- **SC-GZ021-HB-2:** `HealthBarGizmoDefinition.VisibilityPolicy == AlwaysVisiblePolicy.Instance`.
- **SC-GZ021-HB-3:** With `IgHealthState { Damage = 0 }` (full health), `UpdateAndDraw` calls at
  least one draw method on `IDebugDrawBuilder` (use a capturing mock or stub).
- **SC-GZ021-HB-4:** `HealthBarGizmoInstance` implements `IStatefulGizmo` — `OnInitialize` and
  `OnTeardown` do not throw.
- **SC-GZ021-HB-5:** `GizmoRegistrar.Register(registry, settings)` registers the health bar height
  and width settings (assert `settings.IsRegistered(hash)` for both keys).

For SC-GZ021-HB-3: create a simple `CapturingDrawBuilder` that implements `IDebugDrawBuilder` and
records calls to `DrawEntityBadge`. Verify `DrawEntityBadge` was called.

---

## Critical: read these files BEFORE writing any code

1. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Engine\Fdp.Presentation\Vis2D\Layers\DebugGizmoLayer.cs`
   - Constructor signatures (backward-compat + test-injection overload)

2. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Systems\DataDrivenGizmoSystem.cs`
   - Constructor: parameters, types

3. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\GizmoRegistry.cs`
   - `Register` method signature

4. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Settings\GizmoSettingsRegistry.cs`
   - `RegisterSetting(string key, GizmoSettingValue default)` signature
   - `ComputeHash(string)` — is it `static`?
   - `IsRegistered(uint)` — does it exist? (may be `internal`)
   - `Read(uint)` return type, property names

5. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Settings\GizmoSettingValue.cs`
   - Exact property names for float reading (`AsFloat` or `FloatValue`?)
   - Factory: `From(float)` or `FromFloat(float)`?

6. `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\IDebugDrawBuilder.cs`
   - `DrawEntityBadge` signature

7. `d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.IG\IgApplication.cs`
   - Lines 740-760: first `AddLayer` group
   - Lines 1040-1130: second `AddLayer` group (canonical place to add gizmo layer)
   - How `_world.Bus` is typed/named (FdpEventBus or similar)
   - Where kernel systems are registered (search for `RegisterSystem` or `kernel.Register`)

8. `d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.IG.Tests\Gizmos\GizmosRemoteVisualizationTests.cs`
   - Pattern for creating IgApplication in headless tests

---

## Verification

From `d:\Work\IOS-IG-SimHost-FDP-2`:

```
dotnet build Hrot\Subsystems\Hrot.IG\Hrot.IG.csproj --nologo
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmo"
```

## Write report

Write report to: `d:\Work\IOS-IG-SimHost-FDP-2\.dev\gizmos-1\reports\BATCH-07-REPORT.md`

Include: build status, test pass counts per task, deviations, issues encountered.

## Return to dev lead

Return: build status, test count, any deviations or blockers.
