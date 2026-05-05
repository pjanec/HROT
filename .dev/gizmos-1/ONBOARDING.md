# Onboarding — FDP Declarative Gizmo & Presentation Framework

## Project Overview

This workstream implements a **runtime-extensible declarative visualization and interaction
framework** for the FDP/HROT engine. It serves as both the developer debug visualization toolbox
and the production map UI rendering/interaction engine.

The core idea: gizmo logic runs on the authoritative simulation node, emits a stream of 64-byte
backend-neutral `DebugPrimitive` structs, and those structs are rendered locally (Raylib 2D) or
transported remotely over CycloneDDS. The presentation layer is a "dumb terminal" that only renders
— it contains no gizmo logic.

Key architectural properties:
- **Zero allocation on hot paths** — all primitives are blittable 64-byte structs in a pre-allocated
  buffer; no `new` on the per-frame path.
- **O(K) execution** — systems iterate only the K entities that have active gizmos; entity count
  does not affect cost.
- **Open-Closed** — adding a new gizmo type never modifies the orchestrator (`DataDrivenGizmoSystem`).
- **Dual purpose** — the same pipeline handles both debug overlays and production UI/map rendering.

---

## Planning Artifacts

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Architecture, phased breakdown, decisions |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specs with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Technical debt log |

---

## Folder Layout

### New code being created

| Location | Namespace | Contents |
|----------|-----------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/` | `Fdp.Toolkit.Diagnostics.Gizmos` | Primitive data model, gizmo contracts, registry, ECS systems, settings, events |
| `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/` | `Fdp.Toolkit.Vis2D.Gizmos` | Raylib renderer, `GizmoInteractionProxyTool`, rich text renderer |
| `Hrot/Subsystems/Hrot.IG/Gizmos/` | `Hrot.IG.Gizmos` | `GlobalDebugSettings`, concrete gizmo implementations |

### Existing code being extended

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` | Wire renderer and hit-testing (GZ013) |

### Key existing types to understand first

- `FDP/Engine/Fdp.Core/` — `Entity`, `BitMask256`, `ISimulationView`, `EntityRepository`,
  `FdpEventBus`, `FixedString32/64`, `NativeChunkTable`
- `FDP/Engine/Fdp.ModuleHost/Abstractions/` — `IEcsModuleSystem`, `SystemPhase`
- `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Events/` — `ConstructionOrder`, `DestructionOrder`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/` — `AssignBehaviorEvent` (managed!), `ClearBehaviorEvent`
- `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/IMapTool.cs` — `IMapTool` interface
- `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — `MapCanvas.PushTool/PopTool`
- `Hrot/Engine/Hrot.Core/Components/Map/SelectionState.cs` — `IsSelected`, `IsPrimarySelection`

---

## Build and Run

Build the engine:
```
cd FDP
dotnet build FDP.sln
```

Build the full solution (engine + HROT + examples):
```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
```

Run tests (the new gizmo tests should be in `Fdp.Toolkits.Tests`):
```
dotnet test FDP\Engine\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --nologo
```

Or all tests:
```
dotnet test IOS-IG-SimHost.sln --nologo
```

---

## Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` for the batch-based development workflow used in this
project. All implementation is done in batches with instructions, report, and review phases.

### Key conventions relevant to this workstream

1. **Explicit `[FieldOffset]` for all `[StructLayout(Explicit)]` structs** — every field must have
   a declared offset; no implicit padding.
2. **`[EventId(n)]` must not collide** — check all existing `[EventId]` usages before choosing
   IDs. Reserved range 8050–8059 for gizmo events (verify before use).
3. **`AssignBehaviorEvent` is managed** — it is a class, not a struct. Read it with
   `view.ReadManagedEvents<AssignBehaviorEvent>()`, NOT `view.ReadEvents<T>()`.
4. **`Color32` is in `Hrot.IG.Components`** — do not reference it from `Fdp.Toolkits`. Use the
   new `Rgba32` type defined in `Fdp.Toolkit.Diagnostics.Gizmos`.
5. **`ComponentTypeRegistry.GetId(Type)` returns -1 for unregistered types** — always guard with
   a null check or assertion when building `BitMask256` from component types.
6. **`GlobalDebugSettings` component ID** — pick an unused ID from the 160–199 application range.
   Check `GlobalComponentIds` in the codebase first.
