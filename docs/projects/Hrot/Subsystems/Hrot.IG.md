# Hrot.IG

**Project path:** `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj`
**Assembly:** `Hrot.IG`
**Root namespace:** `Hrot.IG`
**Target framework:** `net8.0`
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Subsystems/Hrot.IG/`. This document serves as the
primary architectural reference for the project.

---

## Executive Overview

`Hrot.IG` is the **Image Generator** (IG) node of the HROT military/combat simulation
system. In the IOS-IG-SimHost architecture, three major subsystems run as discrete
processes (or as embedded subsystems in a single orchestrated process):

| Subsystem  | Role                                                                 |
|------------|----------------------------------------------------------------------|
| IOS        | Instructor/Operator Station -- scenario control and exercise management |
| ExCon      | Exercise Control -- operator map console, command issuing             |
| SimHost    | Authoritative simulation engine -- owns entity state, physics, AI    |
| **IG**     | **Image Generator -- 2-D tactical map visualization**                |

The IG is a **visualization-only, ghost-only node**. It never spawns or mutates
entities authoritatively; all entity state arrives via DDS replication from SimHost.
The IG renders the simulation picture on a 2-D tactical map canvas (Raylib/ImGui),
visualises combat effects, maintains entity history trails, and accepts operator
input (entity selection, placement requests, route authoring, context menus) which
it forwards to SimHost or ExCon over DDS.

Key design invariants:

- **Read-only ECS**: no entity is created by the IG itself; ghost entities are created
  by `NetworkSpawningSystem` when SimHost publishes `EntityMaster` samples.
- **Ghost destruction**: when SimHost disposes an `EntityMaster`, `GhostDestructionSystem`
  tears down the local ECS entity.
- **Protocol neutrality**: all DDS traffic is hidden behind `IIgNetworkAdapter` and
  `ICommandGateway`; the application layer is fully testable without a live DDS participant.
- **Headless mode**: every Raylib/ImGui call is gated by `_headless` so the IG can run
  in cluster integration tests without a display.

---

## Architecture

### High-Level Structure

The IG is composed of four concentric layers:

1. **Network layer** -- CycloneDDS translators receive entity state, combat events, and
   operator commands via DDS topics. The `IIgNetworkAdapter` facade owns all DDS writers.

2. **ECS kernel** -- `ModuleHostKernel` ticks a set of `IEcsModule` / `IEcsModuleSystem`
   instances each frame. Modules are registered by `IgNodeBootstrapper` during startup.

3. **Rendering layer** -- `MapCanvas` (from `Fdp.Toolkit.Vis2D`) draws entities, overlays,
   and gizmos onto a Raylib render texture. `MapCamera` controls pan and zoom.

4. **UI layer** -- ImGui panels (`IgDebugPanel`, `EntityInspectorPanel`, `MiniExConPanel`,
   `WaypointEditorPanel`, `PerformanceOverlay`) are hosted inside `ManagedWindow`
   wrappers and drawn each frame between `rlImGui.Begin()` and `rlImGui.End()`.

### Bootstrapping Pipeline

`IgNodeBootstrapper` extends `SharedApplicationBootstrapper` and overrides seven
ordered phases that build the complete node topology:

```
Phase 1  BuildContext            -- HrotNodeBuilder: config, role, network factory, replication, translators
Phase 2  RegisterDomainComponents-- IgRoleComponentRegistry.RegisterAll() + TKB singleton
Phase 3  BuildSerializer        -- Fdp.Toolkit.Scenario.ScenarioSerializerBuilder
Phase 4a PopulateSystems        -- (empty; IG has no direct simulation ECS systems)
Phase 4b GetAdditionalModules   -- StyleResolution, MapCulling, MapLayer, HistoryTrail, EventEffect
Phase 5  BuildOrchestration     -- ClusterSlave + 2PC handlers (RR, Zone, Prefetch, Preview, Diagnostics)
Phase 6a RegisterSpawningPipeline -- GhostDestructionSystem + IgUnitHierarchyModule
Phase 6b RegisterNetworkTranslators -- IIgNetworkAdapter, DDS translators
```

### ECS Tick Phases

Each frame the `ModuleHostKernel` runs three sequential phases. The IG systems are
allocated as follows:

```
+------------------+-------+--------------------------------------------------+
| System                   | Phase          | Purpose                         |
+--------------------------+----------------+---------------------------------+
| NetworkSpawningSystem    | PostSimulation | Create/update/destroy ghosts    |
| GhostDestructionSystem   | PostSimulation | Tear down on EntityMaster DISPOSE|
| UnitHierarchySystem      | Simulation     | Commander-subordinate hierarchy  |
| HistoryRecordingSystem   | Simulation     | Record entity position samples  |
| EventToEffectSystem      | PostSimulation | Spawn explosion/tracer entities  |
| StyleResolutionSystem    | PostSimulation | Merge 3-layer style into ResolvedStyle|
| MapCullingSystem         | PostSimulation | Write CullingState per entity   |
| MapLayerAssignmentSystem | PostSimulation | Write MapDisplayComponent bitmask|
| VisualEffectCleanupSystem| PostSimulation | Age and destroy effect entities  |
+--------------------------+----------------+---------------------------------+
```

---

## ASCII Block Diagrams

### Diagram 1 -- IOS-IG-SimHost Node Topology

```
+------------------+     DDS      +------------------+     DDS      +------------------+
|      ExCon       |<------------>|     SimHost      |<------------>|        IG        |
| (operator map    |  Commands/   | (authoritative   |  Entity      | (visualization   |
|  console)        |  ACKs        |  simulation)     |  replication |  + input)        |
+------------------+              +------------------+              +------------------+
        |                                                                    |
        |    MapCommandRequest / MapCommandAck                               |
        +--------------------------------------------------------------------+
```

### Diagram 2 -- IgApplication Internal Structure

```
+---------------------------------------------------------------------+
|                          IgApplication                              |
|                                                                     |
|  +-------------------+     +-------------------+                   |
|  |   MapCanvas       |     |   MapCamera       |                   |
|  |  (Vis2D render)   |     |  (pan/zoom)       |                   |
|  +-------------------+     +-------------------+                   |
|                                                                     |
|  +----------------------------------------------------------+       |
|  |              ModuleHostKernel (ECS)                      |       |
|  |  +--------------+ +---------------+ +-----------------+ |       |
|  |  | StyleRes.    | | MapCulling    | | HistoryTrail    | |       |
|  |  | Module       | | Module        | | Module          | |       |
|  |  +--------------+ +---------------+ +-----------------+ |       |
|  |  +--------------+ +---------------+ +-----------------+ |       |
|  |  | MapLayer     | | EventEffect   | | Spawning        | |       |
|  |  | Module       | | Module        | | Module          | |       |
|  |  +--------------+ +---------------+ +-----------------+ |       |
|  +----------------------------------------------------------+       |
|                                                                     |
|  +----------------------------------------------------------+       |
|  |              ImGui UI Panels                             |       |
|  |  +------------+ +------------------+ +--------------+   |       |
|  |  | Debug Panel| | Entity Inspector | | Mini ExCon   |   |       |
|  |  +------------+ +------------------+ +--------------+   |       |
|  |  +------------------+ +---------------------------+      |       |
|  |  | Waypoint Editor  | | Performance Overlay       |      |       |
|  |  +------------------+ +---------------------------+      |       |
|  +----------------------------------------------------------+       |
|                                                                     |
|  +----------------------------------------------------------+       |
|  |          IIgNetworkAdapter (DDS facade)                  |       |
|  |  ICommandGateway  |  DDS readers  |  DDS writers         |       |
|  +----------------------------------------------------------+       |
+---------------------------------------------------------------------+
```

### Diagram 3 -- Style Resolution 3-Layer Merge

```
+--------------------+    Layer 1 (base)
|  TKB VisualData    |----> SymbolCode, ModelPath, ColorHex, MapShapeName
+--------------------+
        |
        v
+--------------------+    Layer 2 (network override)
| IgSymbolOverride   |----> affiliation tint, texture, label, trail flag
| (DDS MapEntitySymbol)
+--------------------+
        |
        v
+--------------------+    Layer 3 (operator, highest priority)
|   MapUserConfig    |----> ForceHostile, HideLabels
+--------------------+
        |
        v
+--------------------+
|  ResolvedStyle     |   written to ECS each PostSimulation tick
| (ECS component)    |   consumed by MapCanvas renderer + gizmo layer
+--------------------+
```

### Diagram 4 -- Ghost Entity Lifecycle

```
SimHost                    DDS                         IG
   |                        |                           |
   |  EntityMaster ALIVE    |                           |
   |----------------------->|                           |
   |                        |  NetworkSpawningSystem    |
   |                        |   SpawnEntityCommand      |
   |                        |-------------------------->|
   |                        |                    CreateEntity()
   |                        |                    AddComponents()
   |                        |                           |
   |  EntityMaster DISPOSE  |                           |
   |----------------------->|                           |
   |                        |  GhostDestructionSystem   |
   |                        |   DestroyEntityCommand    |
   |                        |-------------------------->|
   |                        |                    UnregisterNetworkId()
   |                        |                    DestroyEntity()
   |                        |                           |
```

### Diagram 5 -- Map Layer Bitmask Pipeline

```
+-------------------------+     per entity, time-sliced
| MapLayerAssignmentSystem|---> evaluates IsMember predicates
+-------------------------+          for each MapLayerDefinition
         |
         | writes MapDisplayComponent.LayerMask (uint bitmask)
         v
+---------------------------+
| MapCanvas.ActiveLayerMask |   operator-controlled visibility bitmask
| (bitwise AND at render)   |
+---------------------------+
         |
         | Only entities where (LayerMask & ActiveLayerMask) != 0 are drawn
         v
+---------------------------+
|     Rendered entities     |
+---------------------------+
```

---

## Source Structure

### Root-level files

| File | Type | Description |
|------|------|-------------|
| `IgApplication.cs` | `class IgApplication` | Main application shell; owns Raylib window, MapCanvas, camera, kernel, ImGui panels |
| `IgSubsystem.cs` | `class IgSubsystem` | `ISubsystem` adapter used by the multi-subsystem orchestrator |
| `IgNodeBootstrapper.cs` | `class IgNodeBootstrapper` | Extends `SharedApplicationBootstrapper`; wires all ECS modules, DDS translators, and orchestration |
| `IgBootstrapperHelpers.cs` | (helpers) | `GhostDestructionSystem`, `IgUnitHierarchyModule` -- inner bootstrapper helpers |
| `IgRoleComponentRegistry.cs` | `static class IgRoleComponentRegistry` | Registers the full IG ECS component and event schema |
| `IgSequentialIdAllocator.cs` | `class IgSequentialIdAllocator` | Local sequential `INetworkIdAllocator` for ghost-only nodes |
| `IgEvents.cs` | (events) | Local managed event declarations for IG-specific bus events |
| `IgNetworkConstants.cs` | `static class IgNetworkConstants` | DDS domain, instance IDs, geographic origin defaults |
| `IgCameraConstants.cs` | `static class IgCameraConstants` | Camera initial state, zoom limits, pan speed |

### Namespace `Hrot.IG.Components`

| File | Type | Description |
|------|------|-------------|
| `HistoryTrail.cs` | `struct HistoryTrail` | Circular buffer of up to 64 XY world-space position samples per entity |
| `HistoryTrailConstants.cs` | `static class HistoryTrailConstants` | `MaxTrailPoints`, `DefaultSampleIntervalSeconds`, trail color constants |
| `VisualEffectState.cs` | `struct VisualEffectState` | Lifecycle and RGBA state for short-lived explosion/tracer effect entities |
| `VisualEffectStateConstants.cs` | `static class VisualEffectStateConstants` | Durations, scales, colors for explosion and tracer effects |
| `ContextMenuState.cs` | `class ContextMenuState` | Managed ECS component holding context-menu JSON and open/close state |
| `SelectionState.cs` | (stub) | Moved to `Hrot.Map.Common`; type remains in this namespace |
| `CullingState.cs` | (stub) | Moved to `Hrot.Map.Common`; type remains in this namespace |
| `CullingStateConstants.cs` | (stub) | Moved to `Hrot.Map.Common` |
| `ResolvedStyle.cs` | (stub) | Moved to `Hrot.Map.Common`; type remains in this namespace |
| `ResolvedStyleConstants.cs` | (stub) | Moved to `Hrot.Map.Common` |
| `MapOverlayStyle.cs` | (stub) | Moved to `Hrot.Map.Common` |
| `EditablePolyline.cs` | (stub) | Moved to `Hrot.Map.Common` |

### Namespace `Hrot.IG.Systems`

| File | Type | Description |
|------|------|-------------|
| `StyleResolutionSystem.cs` | `class StyleResolutionSystem` | PostSimulation: 3-layer style merge writes `ResolvedStyle` per entity |
| `MapCullingSystem.cs` | `class MapCullingSystem` | PostSimulation: viewport AABB test + LOD calculation writes `CullingState` |
| `MapLayerAssignmentSystem.cs` | `class MapLayerAssignmentSystem` | PostSimulation: time-sliced bitmask assignment writes `MapDisplayComponent` |
| `MapLayerRegistry.cs` | `static class MapLayerRegistry` | Ordered list of 5 `MapLayerDefinition` records with DIS-based predicates |
| `MapLayerDefinition.cs` | `record MapLayerDefinition` | Name, BitMask, and `IsMember` predicate for one map layer |
| `HistoryRecordingSystem.cs` | `class HistoryRecordingSystem` | Simulation: appends sampled XY positions to `HistoryTrail` circular buffer |
| `EventToEffectSystem.cs` | `class EventToEffectSystem` | PostSimulation: spawns explosion/tracer entities from combat events |
| `MapCommandController.cs` | `class MapCommandController` | Orchestrates tool activation (placement, authoring) and ACK routing to ExCon |
| `ContextMenuSystem.cs` | `class ContextMenuSystem` | PostSimulation: syncs `ContextMenuState` with `ContextActionsUpdate` events and input |
| `MapCameraViewport.cs` | `class MapCameraViewport` | POJO: world-space AABB + zoom; updated each frame from camera, read by culling system |
| `MapUserConfig.cs` | `class MapUserConfig` | POJO: operator overrides (ForceHostile, HideLabels, ContinuousDragUpdates) |
| `HrotEntityFilterFactory.cs` | `class HrotEntityFilterFactory` | Translates layer preset strings to `IEntityFilter` bitmask filters |
| `UniqueNameGenerator.cs` | `static class UniqueNameGenerator` | Scans ECS for highest numeric suffix to generate unique entity names |

### Namespace `Hrot.IG.Modules`

| File | Type | Description |
|------|------|-------------|
| `StyleResolutionModule.cs` | `class StyleResolutionModule` | `IEcsModule` wrapper for `StyleResolutionSystem` |
| `MapCullingModule.cs` | `class MapCullingModule` | `IEcsModule` wrapper for `MapCullingSystem` |
| `MapLayerModule.cs` | `class MapLayerModule` | `IEcsModule` wrapper for `MapLayerAssignmentSystem` |
| `HistoryTrailModule.cs` | `class HistoryTrailModule` | `IEcsModule` wrapper for `HistoryRecordingSystem` |
| `EventEffectModule.cs` | `class EventEffectModule` | `IEcsModule` wrapper for `EventToEffectSystem` + `VisualEffectCleanupSystem` |
| `SpawningModule.cs` | `class SpawningModule` | `IEcsModule` wrapper for `NetworkSpawningSystem` |
| `IgGroundClampingModule.cs` | `class IgGroundClampingModule` | Optional module: terrain ground-clamping pipeline (4 systems) |
| `Orchestration/IgZoneDummyHandler.cs` | `class IgZoneDummyHandler` | `IClusterStateHandler`: dummy ACK for `PrepareZone`/`CommitZone` |

### Namespace `Hrot.IG.Gizmos`

| File | Type | Description |
|------|------|-------------|
| `GizmoRegistrar.cs` | `static partial class GizmoRegistrar` | Aggregates all gizmo registrations from Common, AI.Behaviors, ScenarioEditor, and IG-local gizmos |
| `EffectPresentationGizmo.cs` | `class EffectPresentationGizmo` | `[GizmoProjector]`: draws explosion sphere or tracer line via `IDebugDrawBuilder` |
| `ProjectilePresentationGizmo.cs` | `class ProjectilePresentationGizmo` | `[GizmoProjector]`: draws yellow streak between previous and current projectile positions |
| `MeasureToolGizmoAdapter.cs` | `class MeasureToolGizmoAdapter` | Bridges `GizmoSettingsRegistry` and `GlobalGizmoManager` for the measure tool |
| `MeasureToolGizmoSettings.cs` | `static class MeasureToolGizmoSettings` | Setting keys and defaults for measure tool (`MeasureTool.Active`, `MeasureTool.Units`) |
| `IGCapabilitiesAnnounce.cs` | `partial struct IGCapabilitiesAnnounce` | DDS topic struct: advertises supported pipeline targets, layer masks, shape masks |
| `IGCapabilitiesPublisherSystem.cs` | `class IGCapabilitiesPublisherSystem` | PostSimulation: publishes `IGCapabilitiesAnnounce` exactly once on startup |
| `GlobalDebugSettingsPanel.cs` | `static class GlobalDebugSettingsPanel` | ImGui panel stub for `GlobalDebugSettings` singleton |

### Namespace `Hrot.IG.UI`

| File | Type | Description |
|------|------|-------------|
| `IgDebugPanel.cs` | `class IgDebugPanel` | ImGui: FPS, sim time, ForceHostile/HideLabels toggles |
| `DebugPanelState.cs` | `class DebugPanelState` | Logic state for the debug panel; wraps `MapUserConfig` |
| `EntityInspectorPanel.cs` | `class EntityInspectorPanel` | ImGui: entity ID, TKB type, position, affiliation, damage level |
| `EntityInspectorState.cs` | `class EntityInspectorState` | Reads ECS components into plain properties; testable without ImGui |
| `MiniExConPanel.cs` | `class MiniExConPanel` | ImGui: TKB type, affiliation, coordinates, Spawn button |
| `MiniExConPanelState.cs` | `class MiniExConPanelState` | Form state + `Submit()` logic for the Mini ExCon spawner |
| `MiniExConPanelConstants.cs` | `static class MiniExConPanelConstants` | `DefaultTkbType = 101` |
| `WaypointEditorPanel.cs` | `class WaypointEditorPanel` | ImGui: target speed + AI advice JSON for selected route waypoint |
| `PerformanceOverlay.cs` | `class PerformanceOverlay` | Translucent top-right overlay: FPS, frame time, entity counts; F3 toggle |
| `PerformanceMetrics.cs` | `class PerformanceMetrics` | ECS query: total entity count, visible entity count, FPS, frame time |
| `IgPanelColors.cs` | `static class IgPanelColors` | Push/Pop dark-green ImGui title-bar color theme |

### Namespace `Hrot.IG.Windows`

| File | Type | Description |
|------|------|-------------|
| `IgWindows.cs` | Multiple `ManagedWindow` subclasses | Wraps each UI panel as a perspective-bound `ManagedWindow` (docked in orchestrator window manager) |

### Namespace `Hrot.IG.Services`

| File | Type | Description |
|------|------|-------------|
| `IgCapabilitiesPublisher.cs` | `static class IgCapabilitiesPublisher` | One-shot startup service: publishes layer names and tool schemas to ExCon via `IIgNetworkAdapter` |

### Namespace `Hrot.IG.Translators`

| File | Type | Description |
|------|------|-------------|
| `PresentationTkbTranslator.cs` | `class PresentationTkbTranslator` | `ITkbEntityTranslator`: injects `VisualData` + `EntityInfo` from `VisualDefinitionDto` at spawn |

### Namespace `Hrot.IG.Abstractions`

| File | Type | Description |
|------|------|-------------|
| `IDdsWriter.cs` | `interface IDdsWriter<T>` | Thin abstraction over a DDS `DataWriter`; enables offline testing |

---

## Public API Reference

### `IgSubsystem`

```csharp
public sealed class IgSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar,
    Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
{
    // Properties
    public string Name { get; }                            // "IG"
    public Vector4 TitleBarColor { get; }                  // Forest green (0.08, 0.40, 0.08, 1)

    // Constructors
    public IgSubsystem();                                   // Headless / legacy path
    public IgSubsystem(INetworkFactory networkFactory);    // DDS-enabled path

    // ISubsystem
    public void Initialize(SubsystemConfig config);
    public void Update(float deltaTime);
    public void DrawWorld();                               // 2D map canvas + debug overlay
    public void DrawUI();                                  // ImGui panels
    public void Shutdown();

    // IMapCameraProvider
    public MapCameraView? GetCameraView();
    public void ApplyCameraView(MapCameraView view);

    // Additional public helpers
    public MapCamera? GetMapCamera();
    public GizmoExecutionController? GizmoController { get; }
}
```

### `IgApplication`

```csharp
public class IgApplication : IDisposable
{
    // Window constants
    public const int WindowWidth = 1600;
    public const int WindowHeight = 900;
    public const int TargetFps = 60;
    public const string WindowTitle = "IG Mock";

    // Initialisation
    public void InitializeEmbedded(
        bool headless,
        int? domainIdOverride,
        int nodeIdOverride,
        INetworkFactory? networkFactory);

    // Per-frame
    public void Update(float deltaTime);
    public void DrawWorld();
    public void DrawUI();

    // Camera access
    public MapCamera? GetMapCamera();

    // Gizmo access
    internal GizmoExecutionController GizmoController { get; }
    internal Func<bool> IsActiveMapOwner { set; }

    // Optional module installation
    public void InstallGroundClamping(ITerrainProvider terrainProvider);

    // Test hooks (internal)
    internal void TestHook_SetCommandGateway(ICommandGateway gateway);
    internal void TestHook_SetSpawnCommandSink(Action<SpawnEntityCommand> sink);

    // IDisposable
    public void Dispose();
}
```

### `IgNodeBootstrapper`

```csharp
internal sealed class IgNodeBootstrapper : SharedApplicationBootstrapper
{
    // Post-bootstrap public state
    public bool NetworkEnabled { get; }
    public IIgNetworkAdapter? NetworkAdapter { get; }
    public ICommandGateway? CommandGateway { get; }
    public FdpEventBus? OrchestrationBus { get; }
    public NodeOpSlaveTranslator? IgSlaveTranslator { get; }

    // Hook for additional system registration (gizmos, event history, etc.)
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }
}
```

### `IgRoleComponentRegistry`

```csharp
public static class IgRoleComponentRegistry
{
    public static void RegisterAll(EntityRepository world);
}
```

Registers: `ResolvedStyle`, `CullingState`, `SelectionState`, `VehicleParams`,
`IgHealthState`, `PerceptionReceptor`, `TargetMemory`, `WeaponState`, `Health`,
`PhysicsCollider`, `HistoryTrail`, `VisualEffectState`, `TracerTarget`,
`ContextMenuState` (managed), `EditablePolyline` (managed), `MapOverlayStyle`,
`MapDisplayComponent`, `EntityInfo`, `GroundClampingConfig`, `GroundClampingState`,
plus all events from `MissionComponentRegistry`, `RouteComponentRegistry`,
`ZoneComponentRegistry`, and `GizmoComponentActivatedEvent`.

### `IgNetworkConstants`

```csharp
public static class IgNetworkConstants
{
    public const int DdsDomain = 0;
    public const int InstanceId = 300;         // DDS instance ID for the IG process
    public const int LocalNodeId = 1;          // NodeIdMapper-assigned internal ID
    public const int MapGroupId = 1;

    public const double GeoOriginLatDeg   = 52.52;    // Berlin area default
    public const double GeoOriginLonDeg   = 13.405;
    public const double GeoOriginAltMeters = 0.0;
}
```

### `IgCameraConstants`

```csharp
public static class IgCameraConstants
{
    public const float InitialPositionX         = 5000f;
    public const float InitialPositionY         = 5000f;
    public const float InitialZoom              = 0.5f;   // 2 m/px
    public const float MinZoom                  = 0.01f;  // 100 m/px
    public const float MaxZoom                  = 5.0f;   // 0.2 m/px
    public const float ZoomFactor               = 1.2f;
    public const float ZoomSpeedPerTick         = 0.2f;
    public const float ArrowKeyPanSpeedMetersPerSecond = 10f;
}
```

### `MapUserConfig`

```csharp
public class MapUserConfig
{
    public bool ForceHostile { get; set; }
    public bool HideLabels { get; set; }
    public bool ContinuousDragUpdates { get; set; }
}
```

### `MapCameraViewport`

```csharp
public class MapCameraViewport
{
    public float WorldMinX { get; set; }
    public float WorldMinY { get; set; }
    public float WorldMaxX { get; set; }
    public float WorldMaxY { get; set; }
    public float Zoom { get; set; }
    public bool Contains(float x, float y);
}
```

### `StyleResolutionSystem`

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class StyleResolutionSystem : IEcsModuleSystem
{
    public StyleResolutionSystem(MapUserConfig userConfig, long localNodeId = 0);
    public void Execute(ISimulationView view, float deltaTime);
}
```

### `MapCullingSystem`

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class MapCullingSystem : IEcsModuleSystem
{
    public MapCullingSystem(MapCameraViewport viewport);
    public void Execute(ISimulationView view, float deltaTime);
}
```

### `MapLayerRegistry`

```csharp
public static class MapLayerRegistry
{
    public const uint GroundUnitsBit      = 1u << 0;
    public const uint AirUnitsBit         = 1u << 1;
    public const uint VehiclesBit         = 1u << 2;
    public const uint TacticalGraphicsBit = 1u << 3;
    public const uint RoadGraphsBit       = 1u << 4;

    public static readonly IReadOnlyList<MapLayerDefinition> All;
    // Layers: "units_ground", "units_air", "vehicles", "tactical_graphics", "road_graphs"
}
```

### `MapLayerDefinition`

```csharp
public record MapLayerDefinition(
    string Name,
    uint   BitMask,
    Func<Entity, DISEntityType, ISimulationView, bool> IsMember);
```

### `HrotEntityFilterFactory`

```csharp
public sealed class HrotEntityFilterFactory : IEntityFilterFactory
{
    public HrotEntityFilterFactory(EntityRepository world);
    public IEntityFilter CreateFilter(string[] filterPresets);
}
```

### `MapCommandController`

```csharp
public class MapCommandController
{
    public const long StatusFinished     = 0L;
    public const long StatusIntermediate = 1L;
    public const long StatusCancelled    = 2L;
    // ... (construction via DI in IgApplication)
}
```

### `HistoryTrail` Component

```csharp
[ComponentId(GlobalComponentIds.HistoryTrail)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HistoryTrail
{
    public int   Count;
    public int   Head;
    public float SampleInterval;
    public float ElapsedSinceSample;

    public void AddPoint(float x, float y);
    public void GetPoint(int logicalIndex, out float x, out float y);
}
```

### `VisualEffectState` Component

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.VisualEffectState)]
public struct VisualEffectState
{
    public EffectType Type;
    public float Duration;
    public float ElapsedTime;
    public byte ColorR, ColorG, ColorB, ColorA;
    public float Scale;

    public readonly bool IsExpired { get; }
    public readonly float Alpha { get; }
}
```

### `HistoryTrailConstants`

```csharp
public static class HistoryTrailConstants
{
    public const int   MaxTrailPoints             = 64;
    public const float DefaultSampleIntervalSeconds = 0.5f;
    public const byte  TrailColorR = 0;
    public const byte  TrailColorG = 255;
    public const byte  TrailColorB = 255;
    public const byte  TrailColorA = 128;
    public const float TrailLineWidthPx = 2.0f;
}
```

### `VisualEffectStateConstants`

```csharp
public static class VisualEffectStateConstants
{
    public const float ExplosionDurationSeconds = 2.0f;
    public const float ExplosionInitialScale    = 5.0f;
    public const byte  ExplosionColorR = 255, ExplosionColorG = 165, ExplosionColorB = 0, ExplosionColorA = 255;
    public const float TracerDurationSeconds    = 0.3f;
    public const float TracerScale              = 1.0f;
    public const byte  TracerColorR = 255, TracerColorG = 255, TracerColorB = 0, TracerColorA = 255;
    public const float EffectLineWidthPx        = 2.0f;
}
```

### `IgCapabilitiesPublisher`

```csharp
public static class IgCapabilitiesPublisher
{
    public static void Publish(IIgNetworkAdapter? adapter, int mapId);
}
```

### `PresentationTkbTranslator`

```csharp
public sealed class PresentationTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<Type> GetConsumedDescriptors();
    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
}
```

### `IDdsWriter<T>`

```csharp
public interface IDdsWriter<T>
{
    void Write(T sample);
}
```

### UI State Classes

```csharp
public class DebugPanelState
{
    public DebugPanelState(MapUserConfig config);
    public bool ForceHostile { get; set; }
    public bool HideLabels { get; set; }
    public void ToggleForceHostile();
    public void ToggleHideLabels();
    public double CurrentSimTime { get; set; }
    public long CurrentWallTicks { get; set; }
}

public class EntityInspectorState
{
    public bool HasSelection { get; }
    public Entity InspectedEntity { get; }
    public int EntityId { get; }
    public long TkbType { get; }
    public float PositionX, PositionY, PositionZ { get; }
    public ForceId Affiliation { get; }
    public float DamageLevel { get; }
    public void Refresh(ISimulationView view, Entity entity);
}

public class PerformanceMetrics
{
    public int TotalEntityCount { get; }
    public int VisibleEntityCount { get; }
    public int Fps { get; }
    public float FrameTimeMs { get; }
    public void Snapshot(ISimulationView view, int fps, float frameTimeMs);
}
```

### Module Classes

All modules implement `IEcsModule`:

| Module | Constructor | Policy |
|--------|-------------|--------|
| `StyleResolutionModule` | `(MapUserConfig, long localNodeId=0)` | Synchronous |
| `MapCullingModule` | `(MapCameraViewport)` | Synchronous |
| `MapLayerModule` | `()` | Synchronous |
| `HistoryTrailModule` | `()` | Synchronous |
| `EventEffectModule` | `()` | Synchronous |
| `SpawningModule` | `(NetworkSpawningSystem)` | Synchronous |
| `IgGroundClampingModule` | `(ITerrainProvider)` | Synchronous |

### `IgZoneDummyHandler`

```csharp
public sealed class IgZoneDummyHandler : IClusterStateHandler
{
    public IgZoneDummyHandler(long localNodeId = 0);
    public bool CanHandle(NodeOpType operation);      // PrepareZone | CommitZone
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo);
}
```

### Window Classes (`Hrot.IG.Windows`)

All internal `ManagedWindow` subclasses with scope `WindowScope.PerspectiveBound`:

| Class | Key | Title |
|-------|-----|-------|
| `IgDebugWindow` | `"ig_debug"` | `"IG Debug"` |
| `IgEntityPropertiesWindow` | `"ig_entity_properties"` | `"IG Entity Properties"` |
| `IgWaypointEditorWindow` | `"ig_waypoint_editor"` | `"Waypoint Editor"` |
| `IgMiniExConWindow` | `"ig_mini_excon"` | `"Mini ExCon"` |
| `IgPerformanceWindow` | `"ig_performance"` | `"Performance"` |

---

## Dependencies

### Project References

| Project | Purpose |
|---------|---------|
| `Hrot.Common` | `HrotNodeConfig`, `ISubsystem`, `SharedApplicationBootstrapper`, `HrotNodeBuilder`, `HrotNodeContext`, `ClusterSlave`, orchestration infrastructure |
| `Hrot.Core` | `INetworkFactory`, `IIgNetworkAdapter`, `ICommandGateway`, `IIgTranslators`, network topology abstractions |
| `Hrot.Presentation` | `ManagedWindow`, window manager facades, ImGui window hosting |
| `Hrot.AI.Behaviors` | AI behavior gizmo registrations |
| `Hrot.Network.NED` | `DebugPrimitivesIngressTranslator`, NED gizmo interaction translators |
| `Fdp.Core` | `EntityRepository`, `FdpEventBus`, `Entity`, `SimTransform`, core ECS types |
| `Fdp.Presentation` | FDP panel base classes, `EntityInspectorPanel`, `EventBrowserPanel`, `RepositoryAdapter` |
| `Fdp.Toolkits` | `StyleResolutionSystem`, `Vis2D` rendering toolkit, `Replication`, `Lifecycle`, `Combat`, `Perception`, `Physics`, `Diagnostics.Gizmos`, `Orchestration`, `Time`, `NetworkSpawning`, `Scenario`, `Spatial`, `Behavior` |
| `Fdp.Toolkits.Analyzers` | Source generator for `[GizmoProjector]` attribute (analyzer-only, no assembly ref) |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Raylib-cs` | 7.0.2 | Window creation, 2D rendering, keyboard/mouse input, FPS queries |
| `rlImgui-cs` | 3.2.0 | ImGui integration layer for Raylib; `rlImGui.Begin/End` context |
| `NLog` | 5.2.8 | Structured logging via `FdpLog<T>` wrappers |

### InternalsVisibleTo

```xml
<InternalsVisibleTo Include="Hrot.IG.Tests" />
<InternalsVisibleTo Include="Hrot.ClusterRunner.Integration.Tests" />
<InternalsVisibleTo Include="Hrot.ClusterRunner.Tests" />
```

---

## Usage Examples

### Example 1 -- Embedding IG as an `ISubsystem` in the orchestrated host

```csharp
// Composition root (e.g. Program.cs or the orchestrated host entry point)
using Hrot.IG;
using Hrot.Core.Network;

// Build a network factory from the DDS/NED configuration.
INetworkFactory networkFactory = NedNetworkFactory.Create(config);

// Construct and register the IG subsystem.
var igSubsystem = new IgSubsystem(networkFactory);

var orchestrator = new SubsystemOrchestrator();
orchestrator.Register(igSubsystem);

// The orchestrator calls Initialize -> loop { Update, DrawWorld, DrawUI } -> Shutdown
// IgSubsystem.Initialize calls IgApplication.InitializeEmbedded internally.
orchestrator.Run(new SubsystemConfig
{
    Headless     = false,
    DomainId     = 0,
    NodeId       = IgNetworkConstants.InstanceId,
    IsActiveMapOwner = () => true
});
```

### Example 2 -- Running IG in headless mode for integration tests

```csharp
using Hrot.IG;
using Hrot.Core.Network;

// Headless mode: no Raylib window, no ImGui; only ECS + DDS are initialised.
var ig = new IgSubsystem(NullNetworkFactory.Instance);

ig.Initialize(new SubsystemConfig { Headless = true, DomainId = 99 });

// Advance 10 frames at 60 Hz.
float dt = 1f / 60f;
for (int i = 0; i < 10; i++)
    ig.Update(dt);

// Read back ECS state directly via the internal App hook.
var app = ig.App;   // IgApplication (InternalsVisibleTo Hrot.IG.Tests)
// ... assertions against app state ...

ig.Shutdown();
```

### Example 3 -- Querying resolved entity style from a test

```csharp
using Hrot.IG.Systems;
using Hrot.Map.Common.Components;
using Fdp.Core;

// Construct the system under test in isolation (no kernel required).
var config = new MapUserConfig { ForceHostile = true };
var system = new StyleResolutionSystem(config, localNodeId: 0);

// Build a minimal entity repository with the required components registered.
var world = new EntityRepository();
world.RegisterComponent<NetworkIdentity>();
world.RegisterComponent<SimTransform>();
world.RegisterComponent<ResolvedStyle>();

var entity = world.CreateEntity();
world.AddComponent(entity, new NetworkIdentity { Value = 42 });
world.AddComponent(entity, new SimTransform
{
    Position = new System.Numerics.Vector3(100f, 200f, 0f)
});

// Execute the system for one tick.
system.Execute(world, deltaTime: 0.016f);

// Assert ForceHostile Layer-3 override is applied.
ref readonly var style = ref world.GetComponentRO<ResolvedStyle>(entity);
System.Diagnostics.Debug.Assert(style.Affiliation == ForceId.Hostile);
```

### Example 4 -- Installing optional ground clamping

```csharp
using Hrot.IG;
using Hrot.IG.Modules;
using Fdp.Modules.Geographic;

// After IgApplication.InitializeEmbedded() returns, install ground clamping
// by providing a terrain provider. The module wires four systems into the kernel.
ITerrainProvider terrainProvider = new MyHeightMapTerrainProvider("terrain_data/");
igApplication.InstallGroundClamping(terrainProvider);
```

### Example 5 -- Registering a custom map layer

```csharp
// Extend the static layer registry for a new entity class (e.g. "sensors").
// Note: MapLayerRegistry.All is a sealed list; in practice this would require
// a custom MapLayerModule with an injected layer list.

var customLayers = new List<MapLayerDefinition>(MapLayerRegistry.All)
{
    new MapLayerDefinition(
        Name:     "sensors",
        BitMask:  1u << 5,
        IsMember: (entity, dis, view) =>
            view.HasComponent<SensorComponent>(entity))
};

// Inject the custom list into the module.
var module = new MapLayerAssignmentSystem(customLayers.AsReadOnly());
kernel.RegisterModule(new MapLayerModule(module));
```

---

## Key Design Patterns

### Ghost-Only ECS Node

The IG never calls `EntityRepository.CreateEntity()` from authoritative code paths.
All entities are created by `NetworkSpawningSystem` when `SpawnEntityCommand` events
arrive from DDS replication, and destroyed by `GhostDestructionSystem` when
`DestroyEntityCommand` events arrive. This ensures the ECS world on the IG is always
a read-consistent mirror of the SimHost world.

When an operator places a new entity through the IG map canvas, the IG sends a
`CreateEntityRequest` to SimHost over DDS. SimHost creates the authoritative entity
and replicates it back; the IG receives it through the normal ghost-creation path.

### 3-Layer Style Merge

`StyleResolutionSystem` evaluates three priority layers each `PostSimulation` tick:

1. **TKB default** (lowest): `VisualData` attached at spawn, derived from the TKB
   template's `VisualDefinitionDto` by `PresentationTkbTranslator`.
2. **Network override**: `IgSymbolOverride` populated by `MapEntitySymbol` DDS
   translator at runtime; allows ExCon to override symbols per entity.
3. **Operator config** (highest): `MapUserConfig.ForceHostile` and `HideLabels`;
   cannot be suppressed by data.

The merged result is written into the `ResolvedStyle` ECS component and consumed
by the Vis2D renderer and gizmo layer without any further protocol dependency.

### Viewport-Driven Culling

`MapCameraViewport` is a plain C# object updated by `IgApplication` each frame
from the camera's world-space corner projections. `MapCullingSystem` reads it
at `PostSimulation` time and writes a `CullingState` per entity. The renderer
skips all draw calls for entities with `CullingState.IsVisible == false`,
eliminating wasted GPU work for off-screen entities.

LOD levels are also set here using the viewport's `Zoom` value:

| Zoom threshold | LOD level | Description |
|---------------|-----------|-------------|
| `< LodIconOnlyZoomThreshold` | 2 | Icon-only; no labels or details |
| `< LodSimplifiedZoomThreshold` | 1 | Simplified rendering |
| `>= LodSimplifiedZoomThreshold` | 0 | Full rendering |

### Time-Sliced Layer Assignment

`MapLayerAssignmentSystem` spreads the bitmask evaluation workload across frames
using an `IteratorState` that limits processing to a 1 ms budget per frame.
After a full scan it waits `RescanIntervalSeconds = 3.0` seconds before beginning
another pass. This keeps the per-frame cost bounded at the expense of slight
latency in picking up entity class changes.

### Protocol-Neutral Gizmo Registration

`GizmoRegistrar.Register()` aggregates registrations from four sources:
`Hrot.Common.Diagnostics.Gizmos`, `Hrot.AI.Behaviors.Gizmos`,
`Hrot.ScenarioEditor.Gizmos`, and IG-local gizmos. The source generator for
`[GizmoProjector]` emits a `partial` class method for each decorated gizmo class.
This allows new gizmos to be added by decorating a class without manually editing
the registrar.

### Managed-Window Panel Hosting

Each ImGui panel has a corresponding `ManagedWindow` subclass in `Hrot.IG.Windows`.
These windows have scope `WindowScope.PerspectiveBound`, meaning the window manager
shows or hides them according to which subsystem has the active perspective. This
supports seamless switching between IG, SimHost, and ExCon perspectives in the
orchestrated single-process configuration.

---

## Best Practices

**Do not call `EntityRepository.CreateEntity()` in IG production code.** Entities
must arrive via `NetworkSpawningSystem` / `GhostDestructionSystem`. Local-only
effect entities (explosions, tracers) are created via the command buffer inside
`EventToEffectSystem` and carry no `EntityMaster`, but these are the only
sanctioned exception.

**Always gate rendering code on `_headless`.** Any code path that calls a Raylib
or ImGui API must be skipped in headless mode. Failing to do so causes
`AccessViolationException` or `InvalidOperationException` in test environments
without a display.

**Read `MapCameraViewport` from the singleton, not from `MapCamera` directly,
inside ECS systems.** The viewport object is updated from the render thread before
the kernel ticks; accessing `MapCamera` from an ECS system would introduce a
thread-safety hazard.

**Prefer `cmd.SetComponent()` over direct `repo.SetComponent()` in systems.**
Using the command buffer serialises writes and avoids aliasing with the current
frame's read queries. `StyleResolutionSystem` uses `repo.SetComponent()` only when
it detects direct repository access and has verified no concurrent reads are
in flight.

**Keep `MapUserConfig` mutations on the main render/UI thread.** `MapUserConfig`
is read each simulation tick and written from ImGui callbacks (also on the main
thread), so no synchronisation is needed. Do not access it from background tasks.

**Zero-allocations on hot paths.** All `Execute()` methods in this project are
annotated with the intent to allocate nothing. Use `ref readonly` for component
access, avoid LINQ, and use pre-built ECS queries.

**Use `IgNetworkConstants` and `IgCameraConstants` for all magic numbers** rather
than embedding literal values. This ensures that configuration changes propagate
to all call sites without a search-and-replace.

**When adding a new map layer**, add a `const uint` bit allocation to
`MapLayerRegistry` and append a `MapLayerDefinition` to `MapLayerRegistry.All`.
Also add the new layer name to the `IgCapabilitiesPublisher.BuildLayerTreeJson()`
set so the ExCon's layer control UI reflects it automatically.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.Common` | Shared node infrastructure: `ISubsystem`, `SharedApplicationBootstrapper`, `HrotNodeConfig`, `ClusterSlave`, `MapCamera`, `MapCanvas`, orchestration buses |
| `Hrot.Core` | Protocol abstractions: `INetworkFactory`, `IIgNetworkAdapter`, `ICommandGateway`; used to decouple IG from DDS specifics |
| `Hrot.Presentation` | Window manager, `ManagedWindow`, shared ImGui hosting infrastructure |
| `Hrot.Network.NED` | NED (Network Entity Descriptor) protocol implementation; provides `DebugPrimitivesIngressTranslator` and gizmo interaction translators |
| `Hrot.AI.Behaviors` | AI behavior gizmo registrations; `GizmoRegistrar` aggregates them |
| `Hrot.Map.Common` | Shared map types: `MapCamera`, `MapCanvas`, `MapCameraView`; also hosts `ResolvedStyle`, `CullingState`, `SelectionState` after their move from `Hrot.IG.Components` |
| `Hrot.ScenarioEditor` | Route and zone gizmos; `RouteWaypointGizmo`, `MeasureGizmo`; gizmos registered by `GizmoRegistrar` |
| `Hrot.SimHost` | Authoritative simulation node; source of all `EntityMaster` DDS samples that the IG receives |
| `Hrot.ExCon` | Exercise control node; sends `ContextActionsUpdate`, `MapCommandRequest`, and overlay configs; receives `MapCommandAck` and `IGCapabilitiesAnnounce` |
| `Fdp.Core` | FDP ECS kernel: `EntityRepository`, `FdpEventBus`, component/event registration |
| `Fdp.Toolkit.Vis2D` | 2D map canvas, `MapDisplayComponent`, `IEntityFilterFactory`, layer rendering pipeline |
| `Fdp.Toolkit.Diagnostics.Gizmos` | Gizmo framework: `GizmoRegistry`, `GlobalGizmoManager`, `DebugPrimitiveBuffer`, `GizmoSettingsRegistry`, `[GizmoProjector]` |
| `Fdp.Toolkit.Replication` | Entity replication: `NetworkSpawningSystem`, `NetworkEntityMap`, `NetworkIdentity` |
| `Hrot.IG.Tests` | Unit and integration test project (InternalsVisibleTo); tests `StyleResolutionSystem`, `MapCullingSystem`, `HistoryRecordingSystem`, `MapCommandController`, and UI state classes |
| `Hrot.ClusterRunner.Integration.Tests` | End-to-end cluster test harness (InternalsVisibleTo) |
| `Hrot.ClusterRunner.Tests` | Cluster runner unit tests (InternalsVisibleTo) |
