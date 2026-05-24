# BATCH-24: Phase 1 -- Context Menu Decoupling & Marker Components

**Batch Number:** BATCH-24
**Phase:** Phase 1 of old-stuff-erradication.md
**Priority:** CRITICAL (architectural prerequisite for all subsequent eradication phases)
**Dependencies:** BATCH-23 complete (build must be green before you start)

---

## Overview

The previous developer introduced a **proxy hack** (`ExclusiveCaptureProxyTool`) that bypasses the data-driven ECS architecture. This batch enforces the correct architecture as described in Phase 1 of `.dev/gizmos-1/old-stuff-erradication.md`.

**The core principle**: context menus must NOT instantiate gizmos or push tools. They must ONLY mutate ECS state. The `DataDrivenGizmoSystem` observes state changes and activates/deactivates gizmos automatically.

**Relevant architecture docs (read them in order):**
1. `.dev/gizmos-1/old-stuff-erradication.md` -- lines 1-140 (Phase 1 description)
2. `.dev/gizmos-1/TASK-DETAIL.md` -- for background on the gizmo architecture
3. Previous review: `.dev/gizmos-1/reviews/BATCH-21-REVIEW.md` -- understand what was already done correctly

**Scope**: SimHost only. The Editor and CGF legacy `EntityRotationTool` is NOT touched in this batch (those are later phases).

---

## What the proxy hack looks like (DELETE this)

In `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs` there are two places where the context menu:
1. `new Hrot.SimHost.Gizmos.EntityRotatorGizmo(...)` -- imperative gizmo instantiation in UI
2. `_gizmoSystem.ActivateGizmo(ent, gizmo)` -- UI calls gizmo lifecycle management
3. `_map.PushTool(new Fdp.Toolkit.Vis2D.Gizmos.ExclusiveCaptureProxyTool(gizmo))` -- UI pushes canvas tool

This entire pattern must be replaced. The file `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/ExclusiveCaptureProxyTool.cs` must be deleted.

---

## MANDATORY WORKFLOW

Work through these tasks in order. Build and verify after EACH step. Do NOT skip ahead.

1. Task 1 -- Delete proxy hack --> build passes
2. Task 2 -- Add marker component + event --> build passes
3. Task 3 -- Enhance DataDrivenGizmoSystem --> all gizmo tests pass
4. Task 4 -- Create EntityRotatorGizmoDefinition --> build passes
5. Task 5 -- Create GizmoFocusInputBridge --> build passes
6. Task 6 -- Fix SimHostVisualization context menus --> build passes, rotate works
7. Task 7 -- Register, wire, test --> all tests pass

Write your report only after ALL tasks are complete and ALL tests pass.
Do not stop to ask permission for obvious things like running the tests or fixing compile errors.

---

## Task 1: Delete ExclusiveCaptureProxyTool

**Action:** Delete the file `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/ExclusiveCaptureProxyTool.cs` entirely.

Check the test file `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/` for any tests that reference `ExclusiveCaptureProxyTool` and delete those tests too (they test the wrong architecture).

After deletion, run `dotnet build IOS-IG-SimHost.sln` and fix every compile error that references the deleted type. The two callers are both in `SimHostVisualization.cs` -- comment out the failing lines with a `// TODO BATCH-24` note so the build passes. We fix them properly in Task 6.

---

## Task 2: Add ActiveRotationToolRequest and GizmoComponentActivatedEvent

### 2a. Create the marker component

**New file:** `Hrot/Subsystems/Hrot.SimHost/Gizmos/GizmoActivationMarkers.cs`

```csharp
namespace Hrot.SimHost.Gizmos
{
    // Zero-byte ECS marker component. Adding this to an entity signals to
    // DataDrivenGizmoSystem that the operator wants to interactively rotate
    // the entity. The system instantiates EntityRotatorGizmo automatically.
    // Removed by EntityRotatorGizmo.onRemove when the interaction ends.
    public struct ActiveRotationToolRequest { }
}
```

### 2b. Register the marker component

In `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`, find the static `RegisterSimComponents` method (or wherever `ComponentTypeRegistry` registrations happen for SimHost). Add:

```csharp
world.RegisterComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>();
```

This must happen before `GizmoRegistry` rules are registered (before `_gizmoRegistry` is built).

### 2c. Add GizmoComponentActivatedEvent

**Modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`

Add at the end of the file (after `GizmoKeyEvent`, EventId 8058):

```csharp
    /// <summary>
    /// Published when an ECS component that is part of a GizmoRegistry rule is added
    /// to an entity that already exists (i.e., the entity was not just constructed).
    /// DataDrivenGizmoSystem processes this to late-activate any matching gizmo rules.
    /// </summary>
    [EventId(8058)]
    public struct GizmoComponentActivatedEvent
    {
        /// <summary>The entity whose component mask may now satisfy a registered gizmo rule.</summary>
        public Entity Entity;
    }
```

Also register this event in `SimHostApp.cs` (wherever `world.RegisterEvent<...>()` calls are for gizmo events):

```csharp
world.RegisterEvent<GizmoComponentActivatedEvent>();
```

---

## Task 3: Enhance DataDrivenGizmoSystem

**Modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

### 3a. Process GizmoComponentActivatedEvent

In the `Execute` method, after the existing `ConstructionOrder` processing block (step 2), add step 2b:

```csharp
            // 2b. Late-activate gizmos for entities that gained components after construction.
            var activations = view.ReadEvents<GizmoComponentActivatedEvent>();
            foreach (ref readonly var evt in activations)
            {
                if (!view.IsAlive(evt.Entity)) continue;
                ref var header = ref repo.GetHeader(evt.Entity.Index);
                var rules = _registry.Rules;
                for (int r = 0; r < rules.Count; r++)
                {
                    var rule = rules[r];
                    if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                        continue;

                    // Skip if a gizmo instance from this rule already exists for this entity.
                    if (_activeGizmos.TryGetValue(evt.Entity, out var existing) &&
                        existing.Any(gi => gi.RuleIndex == rule.RuleIndex))
                        continue;

                    var instance = rule.Definition.CreateInstance(view, evt.Entity);

                    if (!_activeGizmos.TryGetValue(evt.Entity, out var list))
                    {
                        list = new List<CompiledGizmoInstance>();
                        _activeGizmos[evt.Entity] = list;
                        _entityList.Add(evt.Entity);
                    }

                    list.Add(new CompiledGizmoInstance
                    {
                        Instance   = instance,
                        Definition = rule.Definition,
                        RuleIndex  = rule.RuleIndex,
                    });

                    // Grant exclusive focus if the gizmo requests it.
                    if (instance.RequiresExclusiveFocus && _focusedGizmo == null)
                    {
                        _focusedGizmo = instance;
                        _focusedGizmo.SetFocus(true);
                    }
                }
            }
```

Note: `Any()` requires `using System.Linq;` -- add if missing.

### 3b. Add per-frame component-mask teardown scan

In the `Execute` method, add step 1b (between the `DestructionOrder` teardown and the `ConstructionOrder` processing) -- this tears down gizmo instances whose required components were removed from the entity:

```csharp
            // 1b. Tear down gizmos whose required-component mask is no longer satisfied.
            // This handles the case where a marker component (e.g. ActiveRotationToolRequest)
            // is removed by the gizmo's own onRemove callback.
            var entitiesToTeardown = new List<(Entity entity, int ruleIndex)>();
            foreach (var kvp in _activeGizmos)
            {
                Entity entity = kvp.Key;
                if (!view.IsAlive(entity)) continue;
                ref var header = ref repo.GetHeader(entity.Index);
                var instances = kvp.Value;
                for (int i = 0; i < instances.Count; i++)
                {
                    var gi = instances[i];
                    // Injected (on-demand) gizmos have RuleIndex == -1; skip them.
                    if (gi.RuleIndex < 0) continue;
                    var rule = _registry.Rules[gi.RuleIndex];
                    if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                        entitiesToTeardown.Add((entity, gi.RuleIndex));
                }
            }
            foreach (var (entity, ruleIndex) in entitiesToTeardown)
                TeardownGizmoByRule(entity, ruleIndex);
```

Add a private helper method `TeardownGizmoByRule`:

```csharp
        private void TeardownGizmoByRule(Entity entity, int ruleIndex)
        {
            if (!_activeGizmos.TryGetValue(entity, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].RuleIndex != ruleIndex) continue;
                var gizmo = list[i].Instance;
                if (_focusedGizmo == gizmo)
                {
                    _focusedGizmo.SetFocus(false);
                    _focusedGizmo = null;
                }
                gizmo.Dispose();
                list.RemoveAt(i);
            }
            if (list.Count == 0)
            {
                _activeGizmos.Remove(entity);
                _entityList.Remove(entity);
            }
        }
```

### 3c. Fix CompiledGizmoInstance RuleIndex for injected gizmos

In the current `ActivateGizmo` method, injected gizmos are stored in `_injectedGizmos` (separate dictionary). They do NOT go into `_activeGizmos` and have no `RuleIndex`. The new rule-driven gizmos DO go into `_activeGizmos` with a valid `RuleIndex`. Make sure the per-frame scan (3b) correctly skips injected gizmos. Since rule-based gizmos use `_activeGizmos` (with valid RuleIndex >= 0) and injected gizmos use `_injectedGizmos`, the scan in 3b only touches `_activeGizmos` entries with `RuleIndex >= 0`, which is correct.

---

## Task 4: Create EntityRotatorGizmoDefinition

**New file:** `Hrot/Subsystems/Hrot.SimHost/Gizmos/EntityRotatorGizmoDefinition.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.SimHost.Gizmos
{
    // IGizmoDefinition for interactive entity rotation.
    // Activated by DataDrivenGizmoSystem when both SimTransform and
    // ActiveRotationToolRequest are present on the same entity.
    // The gizmo removes ActiveRotationToolRequest via its onRemove callback
    // to signal that the interaction is complete, which causes the system
    // to tear it down automatically on the next frame.
    public sealed class EntityRotatorGizmoDefinition : IGizmoDefinition
    {
        public Type[] RequiredComponents { get; } =
        {
            typeof(SimTransform),
            typeof(ActiveRotationToolRequest),
        };

        // Always visible while active (exclusive-focus gizmo; never filtered by policy).
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository
                ?? throw new ArgumentException(
                    $"{nameof(EntityRotatorGizmoDefinition)}.CreateInstance requires " +
                    $"direct EntityRepository access, not {view.GetType().Name}.");

            return new EntityRotatorGizmo(
                view,
                entity,
                onRemove: () =>
                {
                    if (repo.IsAlive(entity) && repo.HasComponent<ActiveRotationToolRequest>(entity))
                        repo.RemoveComponent<ActiveRotationToolRequest>(entity);
                });
        }
    }
}
```

**Note:** `AlwaysVisiblePolicy` must exist. Check `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/` for the type. If it is named differently (e.g., `AlwaysVisibleGizmoPolicy`), use the correct name. Do not create a duplicate.

---

## Task 5: Create GizmoFocusInputBridge

This is a GENERIC canvas tool that converts raw mouse/keyboard events into ECS events for any exclusive-focus gizmo. It has NO knowledge of `EntityRotatorGizmo` or any specific gizmo type.

**New file:** `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoFocusInputBridge.cs`

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    // Generic canvas tool that translates raw hardware events into ECS gizmo events
    // for the currently focused gizmo. Used as a temporary input bridge until Phase 5
    // of the eradication migrates all input routing to the ECS pipeline.
    //
    // Unlike ExclusiveCaptureProxyTool (deleted), this tool:
    //   - Does NOT know about any specific gizmo implementation.
    //   - Does NOT call gizmo methods directly.
    //   - Publishes strictly typed ECS events; DataDrivenGizmoSystem routes them.
    //
    // Lifecycle: pushed by the composition root when ActiveRotationToolRequest is added.
    // Pops itself when it receives a commit (left click) or cancel (right click / Escape).
    public sealed class GizmoFocusInputBridge : IMapTool
    {
        public string Name => "GizmoFocusBridge";

        private readonly FdpEventBus _bus;
        private readonly PickToken   _token;   // token for the focused entity
        private MapCanvas? _canvas;

        // focusEntity: the ECS entity that holds the marker component.
        public GizmoFocusInputBridge(FdpEventBus bus, Entity focusEntity)
        {
            _bus   = bus;
            _token = new PickToken { Target = focusEntity };
        }

        public void OnEnter(MapCanvas canvas) => _canvas = canvas;
        public void OnExit()                  => _canvas = null;
        public void Update(float dt)          { }
        public void Draw(RenderContext ctx)   { }

        // Hover and drag both publish drag-update events so the focused gizmo
        // can track the cursor position even without a button held.
        public bool HandleHover(Vector2 worldPos)
        {
            _bus.Publish(new GizmoDragUpdateEvent
            {
                Token    = _token,
                WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                Space    = CoordinateSpace.World,
            });
            return true;   // consume hover so nothing else reacts while gizmo is active
        }

        public bool HandlePress(Vector2 worldPos, MapMouseButton button) => true;

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            _bus.Publish(new GizmoDragUpdateEvent
            {
                Token    = _token,
                WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                Space    = CoordinateSpace.World,
            });
            return true;
        }

        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            var pos = new Vector3(worldPos.X, worldPos.Y, 0f);

            if (button == MapMouseButton.Left)
            {
                // Release = commit. isPressed=false signals left-release.
                _bus.Publish(new GizmoMouseEvent
                {
                    Token     = _token,
                    Button    = (global::Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton)(int)button,
                    IsPressed = false,
                    WorldPos  = pos,
                });
                _canvas?.PopTool();
                return true;
            }

            if (button == MapMouseButton.Right)
            {
                // Right press = cancel.
                _bus.Publish(new GizmoMouseEvent
                {
                    Token     = _token,
                    Button    = (global::Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton)(int)button,
                    IsPressed = true,
                    WorldPos  = pos,
                });
                _canvas?.PopTool();
                return true;
            }

            return false;
        }

        public bool HandleKeyPressed(MapKeyboardKey key)
        {
            _bus.Publish(new GizmoKeyEvent
            {
                Token     = _token,
                Key       = (global::Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey)(int)key,
                IsPressed = true,
            });

            if (key == MapKeyboardKey.Escape)
            {
                _canvas?.PopTool();
                return true;
            }

            return false;
        }
    }
}
```

**Important**: Check the cast from `MapMouseButton` (vis2d) to the gizmo interaction enum. Looking at `ExclusiveCaptureProxyTool` (now deleted), it used:
```csharp
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
```
The values match by design (see `MapMouseButton.cs` from BATCH-23 step 1). Use the same alias pattern if there is a name conflict.

---

## Task 6: Fix SimHostVisualization context menus

**Modify:** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`

There are TWO places that activate the rotation gizmo imperatively. Both must be changed.

### 6a. First occurrence (around line 200) -- "Rotate entity" in the entity inspector context menu

Find the block:
```csharp
                if (_repo!.HasComponent<SimTransform>(entity))
                    builder.AddItem("Rotate entity", () =>
                    {
                        if (_gizmoSystem == null || _map == null) return;
                        var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                            _repo!, entity,
                            onRemove: () => _gizmoSystem.DeactivateGizmo(entity));
                        _gizmoSystem.ActivateGizmo(entity, gizmo);
                        _map.PushTool(new Fdp.Toolkit.Vis2D.Gizmos.ExclusiveCaptureProxyTool(gizmo));
                    });
```

Replace with:
```csharp
                if (_repo!.HasComponent<SimTransform>(entity))
                    builder.AddItem("Rotate entity", () =>
                    {
                        if (_map == null) return;
                        // Data-driven activation: add marker component and publish event.
                        // DataDrivenGizmoSystem creates EntityRotatorGizmo via the registered
                        // EntityRotatorGizmoDefinition rule. The gizmo removes the marker on teardown.
                        if (!_repo!.HasComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>(entity))
                            _repo!.AddComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>(entity);
                        _repo!.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent
                        {
                            Entity = entity,
                        });
                        // Temporary input bridge: converts canvas events to ECS events until Phase 5.
                        _map.PushTool(new Fdp.Toolkit.Vis2D.Gizmos.GizmoFocusInputBridge(_repo!.Bus, entity));
                    });
```

### 6b. Second occurrence (around line 473) -- "Rotate" in the map right-click context menu via MapContextActionController

Find the block:
```csharp
                            rotateTool:     _ =>
                            {
                                if (_gizmoSystem == null || _map == null) return;
                                var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                                    _repo!, ent,
                                    onRemove: () => _gizmoSystem.DeactivateGizmo(ent));
                                _gizmoSystem.ActivateGizmo(ent, gizmo);
                                _map.PushTool(new Fdp.Toolkit.Vis2D.Gizmos.ExclusiveCaptureProxyTool(gizmo));
                            }
```

Replace with:
```csharp
                            rotateTool:     _ =>
                            {
                                if (_map == null) return;
                                if (!_repo!.HasComponent<SimTransform>(ent)) return;
                                if (!_repo!.HasComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>(ent))
                                    _repo!.AddComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>(ent);
                                _repo!.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent
                                {
                                    Entity = ent,
                                });
                                _map.PushTool(new Fdp.Toolkit.Vis2D.Gizmos.GizmoFocusInputBridge(_repo!.Bus, ent));
                            }
```

---

## Task 7: Register EntityRotatorGizmoDefinition and wire the event

### 7a. Register definition in SimHostApp

**Modify:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

Find where `_gizmoRegistry` is created and `DataDrivenGizmoSystem` is registered. Add registration of the new definition:

```csharp
_gizmoRegistry.Register(new Hrot.SimHost.Gizmos.EntityRotatorGizmoDefinition());
```

This must happen BEFORE `new DataDrivenGizmoSystem(_gizmoRegistry, ...)` is called.

### 7b. Register GizmoComponentActivatedEvent in SimHostApp

Find where other gizmo events are registered (e.g., `world.RegisterEvent<GizmoInteractionStartedEvent>()`). Add:

```csharp
world.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent>();
```

---

## Task 8: Tests

Write the following tests. Tests go in `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/` (check if this test project exists; if not, the correct location is `Hrot/Subsystems/Hrot.SimHost.Tests/`).

**Minimum 6 tests:**

### SC_ER001: ActiveRotationToolRequest is a zero-byte unmanaged struct
```csharp
[Fact]
public void SC_ER001_ActiveRotationToolRequest_IsZeroByteUnmanagedStruct()
{
    Assert.Equal(0, System.Runtime.CompilerServices.Unsafe.SizeOf<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>());
}
```

### SC_ER002: EntityRotatorGizmoDefinition requires SimTransform and ActiveRotationToolRequest
```csharp
[Fact]
public void SC_ER002_EntityRotatorGizmoDefinition_RequiresSimTransformAndMarker()
{
    var def = new Hrot.SimHost.Gizmos.EntityRotatorGizmoDefinition();
    Assert.Contains(typeof(SimTransform), def.RequiredComponents);
    Assert.Contains(typeof(Hrot.SimHost.Gizmos.ActiveRotationToolRequest), def.RequiredComponents);
    Assert.Equal(2, def.RequiredComponents.Length);
}
```

### SC_ER003: DataDrivenGizmoSystem activates gizmo on GizmoComponentActivatedEvent
Set up a repo with `SimTransform` and `ActiveRotationToolRequest`, register `EntityRotatorGizmoDefinition`, publish `GizmoComponentActivatedEvent`, run `Execute`, verify gizmo is active and has focus.

```csharp
[Fact]
public void SC_ER003_DataDrivenGizmoSystem_ActivatesGizmo_OnComponentActivatedEvent()
{
    var repo = new EntityRepository();
    repo.RegisterComponent<SimTransform>();
    repo.RegisterComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>();
    repo.RegisterEvent<GizmoComponentActivatedEvent>();
    repo.RegisterEvent<DestructionOrder>();
    repo.RegisterEvent<ConstructionOrder>();
    // Add all other events DataDrivenGizmoSystem reads...

    var registry = new GizmoRegistry();
    // Need to register RequiredMask -- GizmoRegistry.Register does this.
    registry.Register(new Hrot.SimHost.Gizmos.EntityRotatorGizmoDefinition());

    var buffer = new DebugPrimitiveBuffer();
    var system = new DataDrivenGizmoSystem(registry, buffer);

    var entity = repo.CreateEntity();
    repo.AddComponent<SimTransform>(entity);
    repo.AddComponent<Hrot.SimHost.Gizmos.ActiveRotationToolRequest>(entity);

    // Publish activation event, swap bus so Execute can ReadEvents.
    repo.Bus.Publish(new GizmoComponentActivatedEvent { Entity = entity });
    repo.Bus.SwapBuffers();

    system.Execute(repo, 0f);

    // Verify the gizmo system has a focused gizmo (exclusive focus was granted).
    // If DataDrivenGizmoSystem exposes a property like HasFocusedGizmo or FocusedEntity,
    // assert that. Otherwise verify indirectly: publishing a GizmoDragUpdateEvent routes
    // to the gizmo without error.
    Assert.True(true); // replace with a meaningful assertion once you see the API
}
```

Adapt this test based on what `DataDrivenGizmoSystem` exposes. The key behavior to verify is that the gizmo is instantiated and receives exclusive focus.

### SC_ER004: DataDrivenGizmoSystem tears down gizmo when marker is removed
After activation (like SC_ER003), call `repo.RemoveComponent<ActiveRotationToolRequest>(entity)` and run `Execute` again. Verify the gizmo is disposed.

### SC_ER005: GizmoFocusInputBridge publishes GizmoDragUpdateEvent on hover
```csharp
[Fact]
public void SC_ER005_GizmoFocusInputBridge_PublishDragUpdate_OnHover()
{
    var bus    = new FdpEventBus();
    bus.RegisterEvent<GizmoDragUpdateEvent>();
    var entity = new Entity(1, 0);
    var bridge = new Fdp.Toolkit.Vis2D.Gizmos.GizmoFocusInputBridge(bus, entity);

    bridge.HandleHover(new Vector2(10f, 20f));
    bus.SwapBuffers();

    var events = bus.ReadEvents<GizmoDragUpdateEvent>();
    Assert.Equal(1, events.Length);
    Assert.Equal(10f, events[0].WorldPos.X);
    Assert.Equal(20f, events[0].WorldPos.Y);
}
```

### SC_ER006: GizmoFocusInputBridge publishes GizmoMouseEvent left-release on left click
```csharp
[Fact]
public void SC_ER006_GizmoFocusInputBridge_PublishMouseEvent_OnLeftClick()
{
    var bus = new FdpEventBus();
    bus.RegisterEvent<GizmoMouseEvent>();
    var entity = new Entity(1, 0);
    var bridge = new Fdp.Toolkit.Vis2D.Gizmos.GizmoFocusInputBridge(bus, entity);

    bridge.HandleClick(new Vector2(5f, 5f), MapMouseButton.Left);
    bus.SwapBuffers();

    var events = bus.ReadEvents<GizmoMouseEvent>();
    Assert.Equal(1, events.Length);
    Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, events[0].Button);
    Assert.False(events[0].IsPressed);  // left-release signals commit
}
```

Run all tests in `Hrot/Subsystems/Hrot.SimHost.Tests/` and `FDP/Toolkits/Fdp.Toolkits.Tests/` and fix any failures before writing the report.

---

## Build Verification

Run this after all tasks are done:
```
dotnet build IOS-IG-SimHost.sln -nologo 2>&1 | Select-String "error CS|Build succeeded|FAILED"
```

Expected: `Build succeeded` with 0 errors.

Then run:
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --logger "console;verbosity=minimal" 2>&1 | Select-Object -Last 5
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --logger "console;verbosity=minimal" 2>&1 | Select-Object -Last 5
```

Both must show all tests passing.

---

## What NOT to touch

- Do NOT modify `GizmoInteractionProxyTool.cs` (used by `DebugGizmoLayer` for pick-based drag; different from the deleted `ExclusiveCaptureProxyTool`)
- Do NOT touch `CgfSubsystem.cs` or `EditorSubsystem.cs` rotation code (those are later phases)
- Do NOT touch any IgApplication code
- Do NOT touch `EntityRotationTool.cs` (legacy tool for Editor/CGF; left for later phases)

---

## Report Submission

**When done, submit your report to:**
`.dev/gizmos-1/reports/BATCH-24-REPORT.md`

**If you have questions, create:**
`.dev/gizmos-1/questions/BATCH-24-QUESTIONS.md`

---

## Developer Insights (for report)

**Q1:** What was the most difficult part of modifying `DataDrivenGizmoSystem` to handle component additions? Were there any ECS event registration gotchas?

**Q2:** Did the per-frame component-mask teardown scan (task 3b) cause any test interference? How did you handle it?

**Q3:** What design decisions did you make in `GizmoFocusInputBridge` beyond the spec? What alternatives did you consider?

**Q4:** Did you spot any existing tests that were testing the deleted `ExclusiveCaptureProxyTool` behavior that needed to be removed?

**Q5:** Are there any edge cases in the rotation flow (e.g., user deletes entity while rotating) that the implementation handles? Which ones does it not yet handle?
