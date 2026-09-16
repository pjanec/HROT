# Global Action System — Design Document

**Status:** Proposed
**Scope:** SimHost / EditorApplication / IG client / shared `Hrot.Common`
**Supersedes:** Marker-component activation (`ActiveRotationToolRequest`, `ActiveVertexEditRequest`, `ActiveRouteEditRequest`); `IMapTool` stack inside `MapCanvas`; ad-hoc string matching against `ContextActionTriggered.ActionName`.

---

## 1. Motivation

Gizmo activation is currently driven by a tangle of presentation-tier tools (`IMapTool` stack in `MapCanvas`), zero-byte ECS marker components, and string-based context-menu notifications. Three problems make the current state untenable:

1. **Component registry exhaustion.** Each new "active tool" demanded a zero-byte marker struct (e.g. `ActiveRotationToolRequest`). Every UI focus state burns one of the 256 component slots and fragments memory chunks, despite carrying no domain state.
2. **Magic strings on a critical boundary.** `ContextActionTriggered.ActionName` is a `string` because it multiplexes integer action IDs forwarded to ExCon with `"IG_*"` local commands. The simulation layer compares against literals like `"20"`, violating the project's no-magic-numbers rule and breaking compile-time safety on a contract that crosses the IG ↔ SimHost boundary.
3. **OCP violation in the input pipeline.** Every new tool meant editing a hard-coded switch in some activation system. The kernel pipeline cannot be extended without modification.

The goal of this document is a single, formalized **action system** that replaces all three mechanisms with a data-driven registry, dispatched via a strongly-typed FDP event, and shared across every subsystem (SimHost, Editor, future hosts) under a strict DRY contract.

## 2. Goals

- Eradicate magic strings and magic numbers from action routing — every action ID is a named symbolic constant in `Hrot.Common`.
- Provide one **shared** `GlobalActionRegistry` whose entries can be populated differently per subsystem, but whose contract, dispatch system, and event type are common to all.
- Add one strongly-typed unmanaged FDP event (`GlobalActionRequestedEvent`) that carries the action intent across the bus.
- Translate the legacy managed `ContextActionTriggered` (string-based, IG-originated) into the typed event at a single anti-corruption seam.
- Remove all gizmo activation marker components and the FSM stack inside `MapCanvas`.
- Preserve deterministic teardown: when a gizmo's FSM reaches a terminal state, it tears itself down via a closure passed at construction time.

## 3. Non-Goals

- Not changing the IG/ExCon wire format. `ContextActionTriggered` continues to arrive over DDS exactly as today; only its consumer changes.
- Not redesigning the gizmo FSMs themselves. `EntityRotatorGizmo`, `VertexEditGizmo`, `EntityPlacementGizmo`, `MeasureGizmo` etc. retain their existing `IEntityStatefulGizmo` / `IStatefulGizmo` contracts.
- Not unifying `DataDrivenGizmoSystem` and `GlobalGizmoManager`. The action handler chooses the correct lifecycle owner per action.
- Not addressing the `GizmoMap.Contracts` lexical namespace pollution noted in the previous review — that is tracked as a separate fast-follow ticket.

## 4. Architecture Overview

The pipeline is strictly unidirectional. UI emits intent; a translator promotes it into a typed domain event; a dispatcher routes it to a registered handler; the handler instantiates the FSM into the appropriate lifecycle owner.

```
 IG client                     SimHost / Editor (ECS, SystemPhase.Input)              Gizmo lifecycle
 ─────────                     ────────────────────────────────────────              ────────────────
 ContextMenu click
        │
        ▼
 ContextActionTriggered  ──►  ContextActionIngressSystem
 (managed, ActionName:str)         │  parse int, resolve Entity via NetworkEntityMap
                                   │
                                   ▼
                              GlobalActionRequestedEvent  ──►  GlobalActionDispatchSystem
                              (unmanaged: ActionId, Target)         │  registry.TryGetHandler
                                                                    │
                                                                    ▼
                                                              GlobalActionHandler(view, target)
                                                                    │
                                                          ┌─────────┴─────────┐
                                                          ▼                   ▼
                                                  DataDrivenGizmoSystem   GlobalGizmoManager
                                                  .ActivateGizmo(e, g)    .Register(id, g)
                                                  (entity-bound)          (canvas/global)
```

Every arrow is an event-bus or method call across a clear boundary. No component is mutated to express UI focus; no string is compared after the ingress seam.

## 5. Component Design

### 5.1 Symbolic action constants

A single shared constants class lives in `Hrot.Common.Constants` so that both the IG presentation layer (`ContextMenuProjectorGizmo`, JSON menu definitions) and every simulation host (`SimHost`, `EditorApplication`) reference the same identifiers without circular dependencies.

```csharp
namespace Hrot.Common.Constants
{
    public static class GlobalActionIds
    {
        // Entity-bound actions
        public const int CenterOnEntity = 1;
        public const int Delete         = 10;
        public const int Rotate         = 20;

        // Global / canvas-level actions
        public const int Measure        = 200;
        public const int PlaceEntity    = 201;
        public const int PlaceObstacle  = 202;
        // …
    }
}
```

Existing IDs from `Hrot.ExCon.Logic.ContextMenuActions` migrate here to unify the contract. The IG side (`ContextMenuProjectorGizmo`, JSON definitions) constructs menu items strictly via these constants — no integer literals at the call site.

### 5.2 `GlobalActionRequestedEvent`

The unmanaged FDP event that carries the typed intent across the local bus. `Target` is `Entity.Null` for canvas-level actions.

```csharp
namespace Hrot.Common.Events
{
    [EventId(8060)]
    public struct GlobalActionRequestedEvent
    {
        public int    ActionId;
        public Entity Target;
    }
}
```

`EventId(8060)` is chosen in the existing Gizmo/UI range; final ID confirmed at integration.

### 5.3 `GlobalActionRegistry`

The routing table. Maps action ID to a handler delegate. Lives in `Hrot.Common.Interactions` so all subsystems can see it. The registry instance is per-subsystem, but the type is shared.

```csharp
namespace Hrot.Common.Interactions
{
    public delegate void GlobalActionHandler(ISimulationView view, Entity target);

    public sealed class GlobalActionRegistry
    {
        private readonly Dictionary<int, GlobalActionHandler> _handlers = new();

        public void Register(int actionId, GlobalActionHandler handler)
        {
            if (!_handlers.TryAdd(actionId, handler))
                throw new InvalidOperationException(
                    $"ActionId {actionId} is already registered.");
        }

        public bool TryGetHandler(int actionId, out GlobalActionHandler handler)
            => _handlers.TryGetValue(actionId, out handler!);
    }
}
```

Each subsystem registers exactly the actions that make sense for it. SimHost may register `Rotate`, `Measure`, `PlaceEntity`; Editor may add authoring actions that do not exist on the operator console. Shared handlers (e.g. `Rotate`) are registered through a common composition helper to stay DRY.

### 5.4 `GlobalActionDispatchSystem`

A kernel system that drains `GlobalActionRequestedEvent` and invokes the registered handler. Runs in `SystemPhase.Input`. It is completely ignorant of what any action does — it never needs to be modified when new actions are added.

```csharp
namespace Hrot.Common.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class GlobalActionDispatchSystem : IEcsModuleSystem
    {
        private readonly GlobalActionRegistry _registry;

        public GlobalActionDispatchSystem(GlobalActionRegistry registry)
            => _registry = registry;

        public void Execute(ISimulationView view, float deltaTime)
        {
            foreach (ref readonly var evt in view.ReadEvents<GlobalActionRequestedEvent>())
            {
                if (_registry.TryGetHandler(evt.ActionId, out var handler))
                    handler(view, evt.Target);
            }
        }
    }
}
```

### 5.5 `ContextActionIngressSystem`

The anti-corruption layer between the IG-originated managed event and the typed domain event. Single responsibility: translate, do not dispatch. Runs before the dispatch system in the same phase.

```csharp
namespace Hrot.Common.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(GlobalActionDispatchSystem))]
    public sealed class ContextActionIngressSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public ContextActionIngressSystem(NetworkEntityMap entityMap)
            => _entityMap = entityMap;

        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            foreach (var evt in view.ReadManagedEvents<ContextActionTriggered>())
            {
                // Silently drop non-numeric local commands such as "IG_Center".
                if (!int.TryParse(evt.ActionName, out int actionId))
                    continue;

                Entity target = Entity.Null;
                if (evt.EntityNetworkId != 0)
                    _entityMap.TryGetEntity(evt.EntityNetworkId, out target);

                cmd.PublishEvent(new GlobalActionRequestedEvent
                {
                    ActionId = actionId,
                    Target   = target,
                });
            }
        }
    }
}
```

`int.TryParse` is the deliberate filter for IG-only string commands — they do not belong on the simulation bus and are dropped here without logging or exceptions.

### 5.6 Composition root

Each subsystem populates the registry at startup. Handlers decide which lifecycle owner (`DataDrivenGizmoSystem` for entity-bound FSMs, `GlobalGizmoManager` for canvas-level FSMs) receives the gizmo, and pass the appropriate teardown closure.

```csharp
var actionRegistry = new GlobalActionRegistry();

// Entity-bound: rotate the targeted entity.
actionRegistry.Register(GlobalActionIds.Rotate, (view, target) =>
{
    if (target.IsNull) return;

    // Cleanly tear down any previous tool injected for this entity.
    _dataDrivenGizmoSystem.DeactivateGizmo(target);

    var gizmo = new EntityRotatorGizmo(
        view,
        target,
        onRemove: () => _dataDrivenGizmoSystem.DeactivateGizmo(target));

    _dataDrivenGizmoSystem.ActivateGizmo(target, gizmo);
});

// Canvas-level: free measurement.
actionRegistry.Register(GlobalActionIds.Measure, (view, target) =>
{
    long id = GlobalGizmoManager.NewId();
    var gizmo = new MeasureGizmo(
        onRemove: () => _globalGizmoManager.Unregister(id));

    _globalGizmoManager.Register(id, gizmo);
});
```

When a new tool is added, the FDP kernel systems (`ContextActionIngressSystem`, `GlobalActionDispatchSystem`) require zero changes — only a new constant in `GlobalActionIds` and a new `Register(...)` call at the composition root. This is the OCP guarantee the design exists to deliver.

## 6. Lifecycle and Teardown

Every gizmo receives an `onRemove` closure at construction. When the gizmo's internal FSM reaches a terminal state (commit or cancel), or when it processes a structural event indicating the target is gone, it invokes the closure. The closure unregisters the gizmo from its lifecycle owner, which in turn releases the `InputCaptureBinding` if exclusive focus was held.

This is intentionally symmetric with activation: the registry handler is the only place that knows which lifecycle owner is involved, and the closure captures that knowledge. Neither the dispatch system nor the gizmo itself needs to know.

If the entity targeted by an entity-bound gizmo is destroyed mid-interaction, the `DataDrivenGizmoSystem` observes the entity drop and disposes the gizmo deterministically on the next frame, exactly as it does today for definition-driven rules.

## 7. Why Not Marker Components

Marker components were considered and rejected. The data-oriented argument for them is strong in isolation: declarative activation, deterministic teardown via mask evaluation, and natural lifecycle safety on entity destruction. But applied to *every* transient UI focus state, they:

- consume slots in the strict 256-component-type registry for state that carries no domain data,
- fragment ECS chunks for purely ephemeral UI focus,
- and leak presentation concerns into the structural component schema that the simulation reasons about.

The FDP event + registry approach preserves the data-oriented properties that matter — typed intent on the bus, deterministic teardown, decoupled instantiation — without paying the registry cost. The lifecycle safety of mask-driven activation is recovered through the gizmo's `onRemove` closure plus `DataDrivenGizmoSystem`'s existing entity-destruction handling.

## 8. Why Not Activate Directly From the Ingress Handler

An earlier iteration had the context-action ingress system instantiate gizmos directly. This was rejected because it collapses two responsibilities into one seam:

- `ContextActionTriggered` is a **notification** — "the operator clicked this menu item." It is not, semantically, a gizmo lifecycle command.
- The ingress system would then need to know which tools are entity-bound, which are canvas-level, and how to construct each one.

Splitting into ingress (translate to typed event) and dispatch (route via registry) preserves a clean pipeline: notification → typed intent → factory. Each system has one reason to change, and the registry is the only place that grows when new tools are added.

## 9. Migration Plan

The migration proceeds in six phases. Each phase has a clear pass/fail condition and can be merged independently as long as the ordering is preserved.

### Phase 1 — Contract unification

Establish compile-time-safe action identifiers before any wiring changes.

- Create `Hrot.Common.Constants.GlobalActionIds` with integer constants for every existing action.
- Migrate all IDs currently scattered across `Hrot.ExCon.Logic.ContextMenuActions` and JSON menu definitions to reference the new constants.
- Update `ContextMenuProjectorGizmo` and every JSON context-menu definition to construct `ContextMenuItemDto` items via the symbolic constants. **Pass condition:** no integer literal appears in any menu construction site.

### Phase 2 — Event and dispatch pipeline

Land the typed event, registry, and kernel systems. No tools are migrated yet.

- Define `GlobalActionRequestedEvent` (`[EventId(8060)]`) in `Hrot.Common.Events`.
- Implement `GlobalActionRegistry` in `Hrot.Common.Interactions`.
- Implement `GlobalActionDispatchSystem` in `Hrot.Common.Systems`, registered in `SystemPhase.Input`.
- Implement `ContextActionIngressSystem` in the same phase with `[UpdateBefore(typeof(GlobalActionDispatchSystem))]`. **Pass condition:** the systems are registered in SimHost and Editor module hosts; the registry is empty but functional; no behaviour change yet.

### Phase 3 — Eradicate marker components

Drop the activation markers and migrate the entity-bound tools they used to gate.

- Physically delete `ActiveRotationToolRequest`, `ActiveVertexEditRequest`, `ActiveRouteEditRequest` and any references.
- Delete the gizmo *definitions* that depended on them (e.g. the `RequiredComponents` arrays referencing those markers).
- Register handlers in the composition root for `Rotate`, vertex edit, and route edit. Each handler instantiates the corresponding `IEntityStatefulGizmo` and calls `DataDrivenGizmoSystem.ActivateGizmo(target, gizmo)` with an `onRemove` closure that calls `DeactivateGizmo(target)`.
- Delete the legacy `EntityRotationTool`, `EditTool`, `RouteEditTool`. **Pass condition:** the marker structs are absent from the repository, the affected actions still work end-to-end, and no `MapCanvas.PushTool` call remains for these flows.

### Phase 4 — Migrate global authoring and picking

Apply the same pattern to canvas-level interactions that operate without a target entity.

- Register handlers for `Measure`, `PlaceEntity`, `PlaceObstacle`, etc. Each handler obtains a transient ID from `GlobalGizmoManager.NewId()`, constructs the `IStatefulGizmo` (e.g. `MeasureGizmo`, `EntityPlacementGizmo`, `FdpLocationPickerGizmo`, `EntityPickerGizmo`), and registers it via `GlobalGizmoManager.Register(id, gizmo)` with an unregistering `onRemove` closure.
- Confirm each global gizmo declares `RequiresExclusiveFocus = true` so the manager emits `InputCaptureBinding` to the terminal. **Pass condition:** the legacy presentation-tier tools are gone; async pickers (`CanvasMapPickAdapter` and friends) drive their flow via gizmo registration rather than modal tool pushes.

## 11. Summary

One typed event. One registry. One dispatch system. One ingress translator. Zero marker components, zero magic strings, zero canvas-level tool stack. New tools cost one constant and one `Register(...)` call.
