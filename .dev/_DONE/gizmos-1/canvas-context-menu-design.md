# Canvas & Entity Context Menus — Design Document

## 1. Context

The terminal supports right-click context menus on two surfaces:

1. Networked entities on the 2D map and in the Entity Inspector panel.
2. Empty map-space ("canvas") on the 2D map.

The entity flow already follows a clean preemptive projection pipeline. The empty-space flow does not — it currently uses a `GizmoContextMenuRequestedEvent` that bridges the raw input layer to the application shells' ImGui rendering loops. This document specifies the target architecture that eliminates that hack and unifies both flows under a single source of truth.

## 2. Problem with the Current Empty-Space Flow

`DebugGizmoLayer` is a domain-ignorant 2D projection and hit-testing surface. When the operator right-clicks an entity, it finds a pre-projected `ContextMenuBinding` primitive in the gizmo frame, resolves the JSON via the `StringInternMap`, and hands it to `ContextMenuAdapter` — zero round-trips, zero domain knowledge in the layer.

Empty space has no entity, therefore no binding. The current implementation short-circuits: `DebugGizmoLayer` packages the screen position and a pick token into `GizmoContextMenuRequestedEvent`, publishes it onto the interaction bus, and the application shells (`IgApplication`, `EditorSubsystem`, `CgfSubsystem`, `SimHostVisualization`) read it during their `Update` phase, stash deferred state in fields like `_pendingContextMenuEntity` and `_openContextMenuThisFrame`, and trigger hardcoded `ImGui.OpenPopup` calls during `DrawUI` via `SharedContextMenuPopulator`.

This is a hack for four concrete reasons:

- **Presentation coordinates leak into a semantic event bus.** `Vector2 ScreenPos` rides alongside the `PickToken` on a bus that is supposed to carry domain intents.
- **The UI talks to itself through the bus.** Both the publisher (`DebugGizmoLayer`) and the consumers (application shells) live in the presentation tier. The interaction bus is being used as a glorified callback delegate to bridge two halves of the UI.
- **Detached deferred state.** Because ImGui demands popup calls happen during the render phase, shells stash booleans in `Update` and read them in `DrawUI`. This split state machine is fragile and easy to desync.
- **Hardcoded fallback menus.** The fallback path reaches into `SharedContextMenuPopulator.PopulateEmptyMapMenu` and builds C# ImGui menus inline, completely outside the data-driven JSON pipeline that entities use.

The architectural defense for the hack is that `DebugGizmoLayer` stays ignorant of ImGui and domain logic. That goal is correct; the chosen mechanism is wrong.

## 3. Goals

- **One pipeline, one pattern.** Entities and empty space resolve through identical preemptive projection.
- **Single Source of Truth in the ECS domain.** Both the 2D map and the Entity Inspector consume the same per-entity / per-canvas menu definition through their own adapters; neither knows about the other.
- **Zero-latency UI.** No network round-trip and no event-bus relay between right-click and popup. Hit-test → JSON → popup.
- **Zero per-frame allocations on the hot path.** JSON is rebuilt only when domain state changes; the `StringInternMap` deduplicates strings via FNV-1a hashing.
- **Strict layer purity.** `DebugGizmoLayer` performs only spatial intersection. Gizmos do not know about ImGui. The Entity Inspector does not know about gizmos.
- **Eradicate `GizmoContextMenuRequestedEvent`.** The event, its registration, the shell read loops, and all hardcoded shell popups are deleted.

## 4. Architecture Overview

```
ECS Domain (SSOT)                   Adapters (Presentation)         Surfaces
─────────────────                   ────────────────────────        ────────
ContextMenuState (per entity)  ──►  JsonEntityContextMenuHandler ─► Entity Inspector (ImGui)
                               └──► EntityContextMenuGizmo ──┐
                                                              ├──►  DebugGizmoLayer ─► ContextMenuAdapter
CanvasContextMenuState         ───► CanvasContextMenuGizmo ──┘       (2D map ImGui popup)
  (managed singleton)
```

Domain systems running in `SystemPhase.Simulation` evaluate the world and write the menu JSON into ECS state. From there, two independent adapters serve two independent surfaces. Neither adapter touches the other surface's code, and neither surface knows the other exists.

The action return path is a single channel: when any surface fires a menu item, `GizmoMenuActionEvent` carries an integer `ActionId` over DDS; on the backend it becomes a `ContextActionTriggered` event consumed by domain dispatch systems.

## 5. Components

### 5.1 SSOT — ECS State

**`ContextMenuState`** *(existing, unchanged)* — managed component attached per entity. Holds `MenuJson : string`. Already populated by existing entity menu generation.

**`CanvasContextMenuState`** *(new)* — managed singleton. Holds `MenuJson : string`. Path: `Hrot/Engine/Hrot.IG/Components/CanvasContextMenuState.cs`.

```csharp
namespace Hrot.IG.Components
{
    /// <summary>
    /// Managed singleton acting as the Single Source of Truth for the
    /// empty-map-space context menu.
    /// </summary>
    public sealed class CanvasContextMenuState
    {
        public string MenuJson { get; set; } = string.Empty;
    }
}
```

### 5.2 Domain Update Systems

The entity menu update system already exists and is not changed.

**`CanvasMenuUpdateSystem`** *(new)* — runs in `SystemPhase.Simulation`, evaluates domain rules, serializes the JSON, and writes it into `CanvasContextMenuState`. The system caches its serialized JSON internally and only rebuilds when the relevant state hash changes. The exact same JSON written every frame is fine: the `StringInternMap`'s FNV-1a hash deduplicates and only allocates on a true cache miss.

For the initial cut, the canvas menu contains a single item: **Measurement Tool**, dispatched via the existing `GlobalActionIds.Measure` global action. Subsystem-specific items (e.g. *Place Entity* for the editor) can be added later without changing the architecture.

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class CanvasMenuUpdateSystem : IEcsModuleSystem
{
    private string _cachedJson = string.Empty;

    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;

        if (string.IsNullOrEmpty(_cachedJson))
        {
            var items = new List<ContextMenuItemDto>
            {
                new() { Id = GlobalActionIds.Measure, Label = "Measurement Tool", Icon = "measure" }
            };
            _cachedJson = JsonSerializer.Serialize(items, Options);
        }

        repo.SetSingletonManaged(new CanvasContextMenuState { MenuJson = _cachedJson });
    }
}
```

The system is registered in each subsystem's composition root (`EditorSubsystem`, `IgApplication`, `CgfSubsystem`) so each subsystem can install its own variant if needed.

### 5.3 Projection Gizmos (Adapters for the 2D Map)

These are the only adapters that bridge ECS state to the gizmo buffer. They are pure read-only, allocation-free per-frame projectors. They have **no knowledge of ImGui**, no domain logic, no JSON construction — they read state and emit `ContextMenuBinding` meta-primitives.

**`EntityContextMenuGizmo`** — `[GizmoProjector(typeof(NetworkIdentity), typeof(ContextMenuState))]`, runs per entity, projects a binding keyed by `NetworkIdentity.Value`.

**`CanvasContextMenuGizmo`** — `[GizmoProjector]`, global stateless, projects a single binding keyed by the well-known canvas anchor `CanvasAnchorId = -1L`.

```csharp
[GizmoProjector]
public sealed class CanvasContextMenuGizmo : IGlobalStatelessGizmo
{
    public const long CanvasAnchorId = -1L;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (!view.HasSingletonManaged<CanvasContextMenuState>()) return;
        var state = view.GetSingletonManaged<CanvasContextMenuState>();
        if (string.IsNullOrEmpty(state.MenuJson)) return;
        draw.DrawContextMenuBinding(CanvasAnchorId, state.MenuJson);
    }
}
```

### 5.4 Entity Inspector Adapter

The Entity Inspector keeps its existing `JsonEntityContextMenuHandler`, which reads `ContextMenuState.MenuJson` directly off the entity and walks the JSON to build the ImGui tree. It does **not** read from the gizmo buffer. This is deliberate — the gizmo buffer is a transient, write-only projection surface; using it as a read source for application UI panels would couple domain state to the graphics pipeline and reverse the unidirectional flow.

Both surfaces consume the same `ContextMenuState.MenuJson`, so the menus are identical by construction.

### 5.5 Hit-Testing in `DebugGizmoLayer`

`HandleRightClick` resolves the click to an entity network ID, an anchor index, or — as a fallback for empty space — `CanvasContextMenuGizmo.CanvasAnchorId`. It then performs a single dictionary lookup against the bindings collected from the current frame, resolves the JSON via `_buffer.InternMap.TryResolve`, and schedules the popup via `ContextMenuAdapter`.

```csharp
private bool HandleRightClick(Vector2 worldPos)
{
    var frame = _buffer!.GetFrame();
    var menuBindings = new Dictionary<long, uint>();
    foreach (ref readonly var prim in frame)
        if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
            menuBindings[prim.InspNetworkId] = prim.StringHash;

    var best = FindTopmostInteractivePrimitive(worldPos);
    long entityId = best?.InspNetworkId
                  ?? best?.AnchorIndex
                  ?? CanvasContextMenuGizmo.CanvasAnchorId;

    if (entityId != 0 && menuBindings.TryGetValue(entityId, out uint menuHash))
    {
        string? json = _buffer!.InternMap.TryResolve(menuHash);
        if (json != null)
        {
            _contextMenuAdapter.Schedule(entityId, json);
            return true;
        }
    }
    return false;
}
```

There is no fallback event emission. There is no presentation-tier branching for "entity vs empty space". The empty-space case is just the entity case with `entityId = -1L`.

### 5.6 Action Dispatch Flow

When the operator clicks a menu item on either surface, the popup adapter publishes `GizmoMenuActionEvent` carrying the integer `ActionId`. This event egresses over DDS and is unpacked on the backend as `ContextActionTriggered`. `ContextActionIngressSystem` resolves the network ID through `NetworkEntityMap`; for the canvas (`-1L`), the lookup misses and `Entity.Null` is forwarded to `GlobalActionDispatchSystem`, which routes canvas-level actions like the Measurement Tool to handlers expecting an empty target. No special-casing of the canvas exists in the dispatch layer.

## 6. Removal of the Hack

The following deletions are part of this change:

- **Delete** `GizmoContextMenuRequestedEvent` from `Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoInteractionEvents`.
- **Delete** the `_interactionBus.Register<GizmoContextMenuRequestedEvent>()` line in `GizmoInteractionModule.cs`.
- **Delete** the `_interactionBus.Read<GizmoContextMenuRequestedEvent>()` loops in `EditorSubsystem.Update`, `IgApplication.Update`, `CgfSubsystem.Update`, and `SimHostVisualization.Update`.
- **Delete** the deferred state fields (`_pendingContextMenuEntity`, `_openContextMenuThisFrame`) in those shells.
- **Delete** the hardcoded ImGui popup blocks `##editor_map_ctx`, `##ig_map_ctx`, `##cgf_map_ctx`, `##simhost_map_ctx` in their respective `DrawUI` methods.
- **Delete** the empty-map-space populator entry point in `SharedContextMenuPopulator` once no shell calls it.

After removal, no presentation code emits or consumes that event, and no shell renders a hardcoded fallback menu.

## 7. Composition Roots

Each subsystem registers `CanvasMenuUpdateSystem` as a global system and lets the Roslyn analyzer auto-register the gizmo via `[GizmoProjector]`:

```csharp
// In EditorSubsystem.Initialize (and IgApplication, CgfSubsystem similarly)
_kernel.RegisterGlobalSystem(new CanvasMenuUpdateSystem());
// CanvasContextMenuGizmo and EntityContextMenuGizmo are picked up automatically
// by the [GizmoProjector] analyzer registration.
```

Subsystem-specific menu content (e.g. editor-only items) is achieved by giving each subsystem its own variant of `CanvasMenuUpdateSystem`, not by injecting providers into the gizmo. The gizmo stays universal.

## 8. Why This Holds Together

- **One pattern.** Entities and canvas resolve through the same hit-test → binding → JSON → popup path.
- **Inspector and Map cannot diverge.** They read the same `ContextMenuState.MenuJson` (entities) or the same `CanvasContextMenuState.MenuJson` (canvas). There is no second source.
- **Gizmo buffer is write-only for adapters.** The Inspector does not read it; the gizmo does not feed the Inspector. This was a tempting shortcut and is explicitly rejected — the gizmo buffer is a transient projection of ECS state, not a data source for application panels.
- **Layers stay pure.** `DebugGizmoLayer` does only spatial work. Projection gizmos do only state-read + binding-emit. The Inspector handler does only ImGui tree construction. Domain systems own all business rules.
- **No reactive request-response over the bus.** All UI rendering is driven by state that already exists in the frame; the action bus only carries semantic intents in one direction.

## 9. Open Items

- **Subsystem-specific canvas menu content.** Initial cut is Measurement Tool only. When IG, Editor, and CGF need divergent menus, each provides its own `CanvasMenuUpdateSystem` variant; the architecture does not change.
- **Dynamic canvas menu states.** When `CanvasMenuUpdateSystem` needs to react to runtime state (e.g. disable Measurement Tool while a rubber-band is active), it adds a state-hash check around the JSON rebuild — same caching pattern as entity menu generation. No new components required.
- **Multi-entity selection menus.** Out of scope here; the Inspector's `PopulateMenu(IReadOnlyCollection<Entity>, ...)` overload remains unimplemented for the binding-based path until requirements are pinned down.
