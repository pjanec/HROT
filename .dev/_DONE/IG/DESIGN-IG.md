# IG Mock Design

**Version:** 1.0 (Infrastructure Audit Complete)  
**Date:** 2026-02-14  
**Status:** Ready for Implementation

**⚠️ INFRASTRUCTURE AUDIT:** This document reflects comprehensive audit of existing FDP infrastructure. Components marked ✅ EXIST, components marked ❌ require NEW implementation.

**Parent Document**: [Overall Design](./DESIGN-OVERALL.md)

## Table of Contents

1. [Infrastructure Status Matrix](#1-infrastructure-status-matrix)
2. [Overview](#2-overview)
3. [Existing FDP Infrastructure (Reuse)](#3-existing-fdp-infrastructure-reuse)
4. [New Components (Implement)](#4-new-components-implement)
5. [System Architecture](#5-system-architecture)
6. [Tool System](#6-tool-system)
7. [Context Menu System](#7-context-menu-system)
8. [Rendering Pipeline](#8-rendering-pipeline)
9. [Implementation Plan](#9-implementation-plan)

---

## 1. Infrastructure Status Matrix

| Component | Status | Location | Purpose |
|-----------|--------|----------|---------|
| **Map Canvas** | ✅ EXISTS | `FDP.Toolkit.Vis2D.MapCanvas` | 2D map rendering, camera, layer management |
| **Tool System** | ✅ EXISTS | `FDP.Toolkit.Vis2D.IMapTool` | State-machine for interaction modes |
| **Standard Tools** | ✅ EXISTS | `FDP.Toolkit.Vis2D.Tools.*` | StandardInteractionTool, EntityDragTool, BoxSelectionTool, PointSequenceTool |
| **Render Layers** | ✅ EXISTS | `FDP.Toolkit.Vis2D.Layers.*` | EntityRenderLayer, DebugGizmoLayer |
| **Entity Lifecycle** | ✅ EXISTS | `FDP.Toolkit.Lifecycle.EntityLifecycleModule` | Constructing→Active→TearDown states |
| **Network Ingress** | ✅ EXISTS | `ModuleHost.Network.Cyclone.CycloneNetworkModule` (internal systems) | DDS subscription, entity creation from network |
| **Network Egress** | ✅ EXISTS | `ModuleHost.Network.Cyclone.CycloneNetworkModule` (internal systems) | Delta tracking, optimized publishing |
| **Network Entity Map** | ✅ EXISTS | `FDP.Toolkit.Replication.Services.NetworkEntityMap` | Network ID ↔ Local Entity mapping |
| **Dead Reckoning** | ✅ EXISTS | `Fdp.Examples.NetworkDemo.Systems.TransformSyncSystem` | Interpolate 10Hz network to 60Hz |
| **TKB Database** | ✅ EXISTS | `FDP.Toolkit.Tkb.TkbDatabase` | Entity templates with descriptors |
| **Geographic Transform** | ✅ EXISTS | `Fdp.Toolkit.Geographic.WGS84Transform` | WGS84 ↔ Cartesian |
| **Time Sync** | ✅ EXISTS | `FDP.Toolkit.Time.SlaveTimeController` | Follow SimHost time |
| **Recording/Replay** | ✅ EXISTS | `Fdp.Kernel.FlightRecorder.*` | Record/playback ECS state |
| **Command Gateway** | ✅ EXISTS (Shared) | `FDP.Toolkit.Commands.BdcCommandGateway` | Async RPC over DDS (CreateEntity, UpdateDescriptor) |
| **Network Spawning** | ❌ NEW (shared) | `FDP.Toolkit.NetworkSpawning.NetworkSpawningSystem` | Unified entity spawn/update/destroy via `SpawnEntityCommand` events |
| **Creation Tool** | ❌ NEW | `Hrot.IG.Tools.CreationTool` | Place entities/graphics on map |
| **Measure Tool** | ❌ NEW | `Hrot.IG.Tools.MeasureTool` | Distance/line-of-sight measurement |
| **Edit Tool** | ❌ NEW | `Hrot.IG.Tools.EditTool` | Vertex editing for overlays |
| **Style Resolution** | ❌ NEW | `Hrot.IG.Systems.StyleResolutionSystem` | TKB + network + user overrides → visual |
| **History Recording** | ❌ NEW | `Hrot.IG.Systems.HistoryRecordingSystem` | Record entity trails |
| **Visual Effects** | ❌ NEW | `Hrot.IG.Systems.EventToEffectSystem` | Spawn explosions/tracers from events |
| **Map Culling** | ❌ NEW | `Hrot.IG.Systems.MapCullingSystem` | Frustum culling + LOD |
| **Context Menu** | ❌ NEW | `Hrot.IG.UI.ContextMenuSystem` | Right-click menus from IOS |
| **IG Application** | ❌ NEW | `Hrot.IG.Program` | Main app shell, ImGui panels |

**Key Insight**: Map rendering, tools, and network infrastructure **FULLY EXISTS**. Focus on IG-specific systems (styling, history, effects, culling) and application shell.

---

## 2. Overview

### 2.1 Purpose

IG Mock is the **"Map Viewer & Editor"** for the simulation. It:
- **Visualizes Entities**: Renders tanks, units, overlays from DDS
- **Provides Interaction**: Tools for selection, dragging, creation, measurement
- **Acts as Editor**: Creates local visual overlays and ghosts before committing to SimHost
- **Follows Time**: Slave to SimHost clock for synchronized visualization
- **Dead Reckoning**: Interpolates 10Hz network updates to smooth 60Hz rendering

### 2.2 Design Principles

1. **Reuse Existing Infrastructure**: Vis2D for rendering, tools, layers; Replication for networking
2. **Ghosts Before Commits**: Local visual preview during creation/editing
3. **Three-Layer Styling**: TKB defaults + Network overrides + User config
4. **Stateless Rendering**: Rendering systems are "dumb", read from ECS components
5. **Tool State Machine**: IMapTool pattern for mode switching

### 2.3 Technology Stack

- **ECS**: FDP Kernel (Flecs-based)
- **Rendering**: Raylib + FDP.Toolkit.Vis2D (MapCanvas, Layers, Tools)
- **Networking**: ModuleHost.Network.Cyclone (CycloneDDS)
- **Commands**: FDP.Toolkit.Commands (Async RPC with correlation)
- **UI**: rlImGui (Raylib + ImGui.NET)
- **Language**: C# (.NET 8+)

### 2.4 Dependencies

**Critical Shared Components** (must be completed first):
- ✅ `Hrot.NED` - DDS types (Phase P2)
- ✅ `FDP.Toolkit.Commands` - RPC framework (Phase P4)
- ✅ `Hrot.Map.Definitions` - TKB descriptors (Phase P5)

---

## 3. Existing FDP Infrastructure (Reuse)

### 3.1 Map Canvas & Rendering (Vis2D Toolkit)

**✅ VERIFIED EXISTS** - Production-ready 2D map rendering

**Components:**
- `FDP.Toolkit.Vis2D.MapCanvas` - Main canvas with camera, layers, tools
- `FDP.Toolkit.Vis2D.Components.MapCamera` - Pan/zoom with screen↔world transforms
- `FDP.Toolkit.Vis2D.Layers.EntityRenderLayer` - Renders ECS entities via IVisualizerAdapter
- `FDP.Toolkit.Vis2D.Layers.DebugGizmoLayer` - Debug shapes, text labels

**MapCamera Features:**
```csharp
public class MapCamera
{
    public Vector2 Position { get; set; }      // World center
    public float Zoom { get; set; } = 1.0f;    // Pixels per meter
    public Rectangle ViewBounds { get; }        // Computed frustum
    
    public Vector2 ScreenToWorld(Vector2 screenPos);
    public Vector2 WorldToScreen(Vector2 worldPos);
}
```

**MapCanvas Architecture:**
```csharp
public class MapCanvas : IResourceProvider
{
    public MapCamera Camera { get; set; }
    public IMapTool? ActiveTool { get; }
    public uint ActiveLayerMask { get; set; }
    
    // Layer management
    public void AddLayer(IMapLayer layer);
    public Entity? PickTopmostEntity(Vector2 worldPos);
    
    // Tool state machine
    public void SwitchTool(IMapTool? tool);
    public void PushTool(IMapTool tool);
    public void PopTool();
    
    // Update & Render
    public void Update(float dt);
    public void Render(RenderContext ctx);
}
```

**EntityRenderLayer:**
```csharp
public class EntityRenderLayer : IMapLayer
{
    private readonly ISimulationView _view;
    private readonly EntityQuery _query;
    private readonly IVisualizerAdapter _adapter; // Strategy for drawing
    
    public void Render(RenderContext ctx)
    {
        foreach (var entity in _query.Entities)
        {
            if (!IsVisible(entity, ctx)) continue;
            _adapter.Render(entity, _view, ctx);
        }
    }
}
```

**Usage Pattern:**
```csharp
var canvas = new MapCanvas(new RaylibInputProvider());
canvas.Camera.Position = new Vector2(5000, 5000);
canvas.Camera.Zoom = 0.5f;

// Add layers
var bgLayer = new BackgroundTileLayer(tileProvider);
canvas.AddLayer(bgLayer);

var entityLayer = new EntityRenderLayer(world.Query(), visualizer);
canvas.AddLayer(entityLayer);

// Set tool
var selectionTool = new StandardInteractionTool(world, query, visualizer);
canvas.SwitchTool(selectionTool);

// Game loop
canvas.Update(dt);
canvas.Render(new RenderContext { Zoom = canvas.Camera.Zoom });
```

---

### 3.2 Tool System (Existing)

**✅ VERIFIED EXISTS** - State-machine pattern for interaction modes

**Built-in Tools:**

**StandardInteractionTool** - Default mode (click, drag, box select):
```csharp
public class StandardInteractionTool : IMapTool
{
    // Events
    public event Action<Entity, bool>? OnEntitySelectRequest; // Entity, AugmentSelection
    public event Action<List<Entity>>? OnRegionSelected;      // Box select result
    public event Action<Entity, Vector2>? OnEntityMoved;      // Drag result
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == SelectButton)
        {
            Entity hit = FindEntityAt(worldPos);
            if (hit != Entity.Null)
                OnEntitySelectRequest?.Invoke(hit, _shiftHeld);
        }
    }
}
```

**EntityDragTool** - Modal entity dragging:
```csharp
public class EntityDragTool : IMapTool
{
    private readonly Entity _target;
    public event Action<Entity, Vector2>? OnEntityMoved;
    
    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        _currentPos = worldPos;
        OnEntityMoved?.Invoke(_target, _currentPos);
        return true;
    }
}
```

**BoxSelectionTool** - Modal box selection:
```csharp
public class BoxSelectionTool : IMapTool
{
    public event Action<List<Entity>>? OnComplete;
    
    public void Draw(RenderContext ctx)
    {
        // Draw selection rectangle
        Raylib.DrawRectangleLines(..., Color.Yellow);
    }
}
```

**PointSequenceTool** - Modal polyline/polygon drawing:
```csharp
public class PointSequenceTool : IMapTool
{
    public event Action<List<Vector2>>? OnComplete;
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
            _points.Add(worldPos);
        else if (button == MouseButton.Right)
            Finish(); // Close polygon
    }
}
```

**IG will extend with:**
- `CreationTool` (entity placement + graphics drawing)
- `MeasureTool` (distance, line-of-sight)
- `EditTool` (vertex manipulation for overlays)

---

### 3.3 Network Replication (Existing)

**✅ VERIFIED EXISTS** - Full ingress/egress with lifecycle

**NetworkEntityMap:**
```csharp
public class NetworkEntityMap
{
    public Entity GetOrCreateEntity(int networkId);
    public void Register(int networkId, Entity entity);
    public bool TryGetEntity(int networkId, out Entity entity);
    public void Unregister(int networkId, int currentFrame); // Graveyard for late packets
}
```

> ⚠️ **Architecture note:** Do NOT register `SmartEgressSystem`, `CycloneIngressSystem`, or `CycloneEgressSystem` manually. They are private implementation details of `CycloneNetworkModule`. Provide your translators to the module constructor; the module installs all required systems itself.

```csharp
// CORRECT: CycloneNetworkModule owns all network systems internally.
var networkModule = new CycloneNetworkModule(
    participant, nodeMapper, idAllocator, topology, elm,
    serialisation, translators, entityMap
);
kernel.RegisterModule(networkModule);
```

**Dead Reckoning (TransformSyncSystem from NetworkDemo):**

```csharp
// Dead Reckoning: TransformSyncSystem from NetworkDemo
// IG's WorldPosTranslator converts WGS84 to Cartesian and writes it to NetworkPosition.
// TransformSyncSystem automatically lerps the visual SimTransform towards NetworkPosition.
registry.RegisterSystem(new TransformSyncSystem(driveFromNetwork: true));
```

---

### 3.4 Entity Lifecycle (Existing)

**✅ VERIFIED EXISTS** - State machine for entity construction

> ⚠️ **Architecture note:** IG does not own entity lifecycles. When `EntityMasterTranslator` receives a new entity, it publishes a `SpawnEntityCommand` to the `FdpEventBus`. The shared `NetworkSpawningSystem` handles the ECS instantiation.

```csharp
// In EntityMasterTranslator:
public void OnReceived(Hrot.DDS.EntityMaster sample, SampleInfo info, EntityRepository world)
{
    if (info.InstanceState == InstanceState.Disposed)
    {
        _eventBus.Publish(new DestroyEntityCommand { NetworkId = sample.EntityId });
        return;
    }

    if (!_entityMap.Contains(sample.EntityId))
    {
        // New remote entity — delegate to NetworkSpawningSystem
        _eventBus.Publish(new SpawnEntityCommand
        {
            NetworkId = sample.EntityId,
            TkbType = sample.TkbType,
            OwnerNodeId = sample.OwnerNodeId,
            InitType = ReliableInitType.None, // Ghost replica, no ACK handshake
            InitialComponents = new List<object> { sample }
        });
    }
}
```

---

### 3.5 Time Synchronization (Existing)

**✅ VERIFIED EXISTS** - IG uses SlaveTimeController

> ⚠️ **Time Sync requires a DDS → EventBus bridge.** `SlaveTimeController` does **not** read the DDS topic directly. It listens to `TimePulse` events on the internal `FdpEventBus`. Without a translator that bridges the DDS `TimePulse` / `TimePulseDescriptor` topic to the `FdpEventBus`, the controller will never receive updates and IG time will be permanently frozen.
>
> **Required:** Register `AutoCycloneTranslator<TimePulseDescriptor>` (or `BlitEventTranslator<TimePulseDescriptor>`) in the translator list passed to `CycloneNetworkModule` **before** setting the time controller. This bridges the DDS time signal → `FdpEventBus` → `SlaveTimeController`.

**SlaveTimeController:**
```csharp
public class SlaveTimeController : ITimeController
{
    // Listens to TimePulse events on FdpEventBus (NOT directly on DDS DataReader).
    private readonly FdpEventBus _eventBus;

    public void Update(float realtimeDt)
    {
        var pulses = _eventBus.ConsumeEvents<TimePulseEvent>();
        if (pulses.Any())
        {
            var latest = pulses.Last();
            _currentTime = latest.SimulationTime;
            _timeScale   = latest.TimeScale;
        }

        _currentTime += realtimeDt * _timeScale;
    }
}
```

**Usage:**
```csharp
// Step 1: Register TimePulseDescriptor translator in CycloneNetworkModule translators list
//         (before kernel.RegisterModule(networkModule))
translators.Add(new AutoCycloneTranslator<TimePulseDescriptor>(participant, eventBus));
// ...then register network module...
kernel.RegisterModule(networkModule);

// Step 2: Set time controller — must be called AFTER network module is registered
//         so the DDS subscription and EventBus bridge are active.
var timeController = new SlaveTimeController(eventBus);
kernel.SetTimeController(timeController);
```

---

### 3.6 Recording & Replay (Existing)

**✅ VERIFIED EXISTS** - Full deterministic replay

**AsyncRecorder:**
```csharp
public class AsyncRecorder : IDisposable
{
    public void RecordFrame(EntityRepository world, GlobalTime time);
    public void Dispose(); // Finalize file
}
```

**PlaybackController:**
```csharp
public class PlaybackController
{
    public int TotalFrames { get; }
    public GlobalTime CurrentTime { get; }
    
    public void SeekToFrame(EntityRepository world, int frame);
    public void StepForward(EntityRepository world);
}
```

**Usage (IG Debug Panel):**
```csharp
// Recording
_recorder = new AsyncRecorder("session.fdp");

// Playback
_playback = new PlaybackController("session.fdp");
_playback.SeekToFrame(world, 1500);

//# 3.7 Command Gateway (Shared Component - Reused)

**✅ VERIFIED EXISTS** - Implemented in FDP.Toolkit.Commands (Shared Phase P4)

**BdcCommandGateway** provides async/await RPC over DDS:

```csharp
public class BdcCommandGateway
{
    private readonly DdsCommandClient<CreateEntityRequest, CreateEntityAck> _createEntity;
    private readonly DdsCommandClient<UpdateEntityDescriptorRequest, UpdateEntityDescriptorAck> _updateDescriptor;
    private readonly DdsCommandClient<MissionControlRequest, MissionControlAck> _missionControl;
    
    public BdcCommandGateway(DomainParticipant participant)
    {
        _createEntity = new(participant, "CreateEntityRequest", "CreateEntityAck");
        _updateDescriptor = new(participant, "UpdateEntityDescriptorRequest", "UpdateEntityDescriptorAck");
        _missionControl = new(participant, "MissionControlRequest", "MissionControlAck");
    }
    
    public async Task<CreateEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000)
    {
        return await _createEntity.SendAsync(request, timeoutMs);
    }
    
    public async Task<UpdateEntityDescriptorAck> UpdateDescriptorAsync(UpdateEntityDescriptorRequest request, int timeoutMs = 5000)
    {
        return await _updateDescriptor.SendAsync(request, timeoutMs);
    }
    
    public async Task<MissionControlAck> MissionControlAsync(MissionControlRequest request, int timeoutMs = 5000)
    {
        return await _missionControl.SendAsync(request, timeoutMs);
    }
}
```

**DdsCommandClient<TReq, TAck>** (underlying implementation):
- Uses `TaskCompletionSource<TAck>` for async/await pattern
- Correlates requests/responses via `RequestId` (Guid)
- Handles timeouts (default 5 seconds)
- Thread-safe with `ConcurrentDictionary<Guid, TaskCompletionSource<TAck>>`

**Usage in IG:**
```csharp
// CreationTool
var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    TkbType = _selectedTkbType,
    Position = _geo.ToGeodetic(worldPos)
};

var ack = await _gateway.CreateEntityAsync(request);
if (ack.ErrorCode == 0)
    Console.WriteLine($"Entity created: {ack.NewEntityId}");
else
    Console.WriteLine($"Failed: {ack.ErrorText}");

// EditTool
var updateRequest = new UpdateEntityDescriptorRequest
{
    RequestId = Guid.NewGuid(),
    EntityId = entityId,
    DescriptorType = EDescriptorType.dtMapVisualOverlay,
    CurrentVersion = overlay.Version,
    Payload = modifiedOverlay
};

var updateAck = await _gateway.UpdateDescriptorAsync(updateRequest);
```

**Initialization:**
```csharp
public class IgApplication
{
    private BdcCommandGateway _gateway;
    
    public void Initialize()
    {
        var participant = new DdsParticipant(domainId: 0);
        _gateway = new BdcCommandGateway(participant);
        
        // Pass to tools
        var creationTool = new CreationTool(_gateway, _geo, tkbType, affiliation);
    }
}
```

**Error Handling:**
- Timeout: `TaskCanceledException` after 5 seconds
- Network failure: Exception from DDS layer
- Application error: Check `ack.ErrorCode` and `ack.ErrorText`

See [DESIGN-SHARED.md Section 4.2](./DESIGN-SHARED.md#42-fdptoolkitcommands-rpc-over-dds) for full implementation details.

---

## Resume live from replay
_playback.Dispose();
_timeController.SeedState(new GlobalTime { TotalTime = replayTime });
// Physics systems wake up, continue from replay state
```

---

## 4. New Components (Implement)

### 4.1 ECS Components

> ⚠️ **Phase 0 Note (SimTransform):** Use `SimTransform` from `Fdp.Kernel` — do **not** redefine it locally in the IG project. All field access patterns (`transform.Position`, `transform.Rotation`) are already correct as written. ECS queries (`With<SimTransform>()`) need no changes. See BCS-P0-T1.

> ⚠️ **Phase 0 Note (NetworkReceivedState):** Use `NetworkPosition` and `NetworkOrientation` from `FDP.Toolkit.Replication.Components` instead of defining a custom `NetworkReceivedState`. The `TransformSyncSystem` from `Fdp.Examples.NetworkDemo` automatically lerps `SimTransform` towards these network components.

**ResolvedStyle** (Computed visual properties):
```csharp
public struct ResolvedStyle
{
    public string TextureName;    // From TKB or network override
    public Color Tint;            // RGBA
    public string LabelText;      // Display name
    public ForceId Affiliation;   // Friend/Hostile/Neutral
    public float DamageLevel;     // 0-100
    public bool ShowTrail;        // History trail flag
}
```

**CullingState** (Visibility/LOD):
```csharp
public struct CullingState
{
    public bool IsVisible;        // In frustum
    public byte LodLevel;         // 0=Full detail, 1=Medium, 2=Icon only
    public bool IsAggregated;     // Hide children, show parent only
}
```

**MapLayerMask** (Filtering):
```csharp
public struct MapLayerMask
{
    public uint LayerBits;        // 32 layers (Background, Entities, Overlays, etc.)
}
```

**HistoryTrail** (Path recording):
```csharp
public struct HistoryTrail
{
    public NativeList<Vector3> Points;
    public int MaxPoints;
    public float SampleInterval; // Seconds between samples
    public double LastSampleTime;
}
```

**VisualEffectState** (Temporary animations):
```csharp
public struct VisualEffectState
{
    public EffectType Type;      // Explosion, Tracer, Smoke
    public float Duration;
    public float ElapsedTime;
    public Color Color;
    public float Scale;
}

public enum EffectType { Explosion, Tracer, Smoke, Flash }
```

**SelectionState** (UI highlighting):
```csharp
public struct SelectionState
{
    public bool IsSelected;
    public bool IsHovered;
    public bool IsPrimarySelection; // First selected entity
}
```

---

### 4.2 Systems

#### StyleResolutionSystem

**Purpose:** Merge TKB defaults + network overrides + user config → ResolvedStyle

**Architecture:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateAfter(typeof(NetworkIngressPhase))]
public class StyleResolutionSystem : ComponentSystem
{
    private readonly ITkbDatabase _tkb;
    private readonly MapUserConfig _userConfig; // Singleton
    
    protected override void OnUpdate()
    {
        // SimTransform from Fdp.Kernel — query is already correct.
        Entities.With<EntityMaster, SimTransform>().ForEach((entity, ref master, ref transform) =>
        {
            var template = _tkb.GetTemplate(master.TkbType);
            var defaultStyle = template.GetDescriptor<IgVisualDef>();
            
            Color tint = defaultStyle.Color;
            string texture = defaultStyle.SymbolTexture;
            string label = defaultStyle.Name;
            
            // Layer 2: Network overrides (MapEntitySymbol)
            if (World.TryGetManagedComponent<MapEntitySymbol>(entity, out var symbol))
            {
                if (symbol.ForceAffiliation.HasValue)
                    tint = GetAffiliationColor(symbol.ForceAffiliation.Value);
                if (!string.IsNullOrEmpty(symbol.TextureOverride))
                    texture = symbol.TextureOverride;
                if (!string.IsNullOrEmpty(symbol.LabelOverride))
                    label = symbol.LabelOverride;
            }
            
            // Layer 3: User config (e.g., "Show all as red")
            if (_userConfig.ForceHostile)
                tint = Color.Red;
            
            var damage = 0.0f;
            if (World.TryGetComponent<EntityDamage>(entity, out var dmg))
                damage = dmg.Damage;
            
            World.SetComponent(entity, new ResolvedStyle
            {
                TextureName = texture,
                Tint = tint,
                LabelText = label,
                DamageLevel = damage,
                ShowTrail = symbol?.ShowHistory ?? false
            });
        });
    }
}
```

---

#### MapCullingSystem

**Purpose:** Frustum culling + LOD based on zoom level

**Architecture:**
```csharp
[UpdateInPhase(SystemPhase.PreRender)]
public class MapCullingSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var camera = World.GetSingleton<MapCameraState>();
        var frustum = camera.ViewBounds;
        var zoom = camera.Zoom;
        
        // SimTransform from Fdp.Kernel — query and field access are already correct.
        Entities.With<SimTransform>().ForEach((entity, ref transform) =>
        {
            bool inFrustum = frustum.Contains(new Vector2(transform.Position.X, transform.Position.Y));
            
            byte lod = 0;
            if (zoom < 0.1f) lod = 2; // Far: icon only
            else if (zoom < 0.5f) lod = 1; // Medium: simplified
            
            World.SetComponent(entity, new CullingState
            {
                IsVisible = inFrustum,
                LodLevel = lod
            });
        });
    }
}
```

---

#### HistoryRecordingSystem

**Purpose:** Record entity trails for UAVs/ground units

**Architecture:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class HistoryRecordingSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var currentTime = World.GetSingleton<GlobalTime>().TotalTime;
        
        // SimTransform from Fdp.Kernel — query and field access are already correct.
        Entities.With<SimTransform, ResolvedStyle, HistoryTrail>().ForEach((entity, ref transform, ref style, ref trail) =>
        {
            if (!style.ShowTrail) return;
            
            double elapsed = currentTime - trail.LastSampleTime;
            if (elapsed >= trail.SampleInterval)
            {
                trail.Points.Add(transform.Position); // Correct: SimTransform.Position
                
                if (trail.Points.Length > trail.MaxPoints)
                    trail.Points.RemoveAt(0); // Circular buffer
                
                trail.LastSampleTime = currentTime;
            }
        });
    }
}
```

---

#### EventToEffectSystem

**Purpose:** Spawn visual effects from network events (explosions, tracers)

**Architecture:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class EventToEffectSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var fireEvents = World.ConsumeEvents<FireInteractionEvent>();
        
        foreach (var evt in fireEvents)
        {
            // Spawn explosion at target
            var explosion = World.CreateEntity();
            // ⚠️ Phase 0 Adaptation: Use World.AddComponent (not SetComponent) since the entity is newly created.
            //   SimTransform is from Fdp.Kernel — do not redefine locally.
            World.SetComponent(explosion, new SimTransform { Position = evt.TargetPosition });
            World.SetComponent(explosion, new VisualEffectState
            {
                Type = EffectType.Explosion,
                Duration = 2.0f,
                Color = Color.Orange,
                Scale = 5.0f
            });
            
            // Spawn tracer from shooter to target
            var tracer = World.CreateEntity();
            // ⚠️ Phase 0 Adaptation: Use World.AddComponent (not SetComponent) since the entity is newly created.
            //   SimTransform is from Fdp.Kernel — do not redefine locally.
            World.SetComponent(tracer, new SimTransform { Position = evt.ShooterPosition });
            World.SetComponent(tracer, new VisualEffectState
            {
                Type = EffectType.Tracer,
                Duration = 0.3f,
                Color = Color.Yellow
            });
        }
    }
}
```

---

#### VisualEffectCleanupSystem

**Purpose:** Remove expired effects

**Architecture:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class VisualEffectCleanupSystem : ComponentSystem
{
    protected override void OnUpdate(float dt)
    {
        Entities.With<VisualEffectState>().ForEach((entity, ref effect) =>
        {
            effect.ElapsedTime += dt;
            
            if (effect.ElapsedTime >= effect.Duration)
            {
                World.DeleteEntity(entity);
            }
        });
    }
}
```

---

### 4.3 DDS Translators

**EntityMasterTranslator** (DDS → ECS):

> ⚠️ **Architecture note — publish `SpawnEntityCommand`, do NOT call ELM directly:**
> `EntityMasterTranslator` is an ingress translator. It must NOT call `_lifecycle.BeginConstruction()` directly — that bypasses `NetworkSpawningSystem` and duplicates the construction logic. Instead, publish a `SpawnEntityCommand` with `InitType = ReliableInitType.None` (IG receives entities, it does not own them). `NetworkSpawningSystem` handles the full creation sequence on the next ECS tick.

```csharp
public class EntityMasterTranslator : ITranslator
{
    private readonly NetworkEntityMap _entityMap;
    private readonly FdpEventBus _eventBus;

    public void OnReceived(EntityMaster sample, SampleInfo info, EntityRepository world)
    {
        if (info.InstanceState == InstanceState.Disposed)
        {
            _eventBus.Publish(new DestroyEntityCommand { NetworkId = sample.EntityId });
            return;
        }

        if (!_entityMap.Contains(sample.EntityId))
        {
            // New remote entity — delegate full spawn to NetworkSpawningSystem
            _eventBus.Publish(new SpawnEntityCommand
            {
                NetworkId         = sample.EntityId,
                TkbType           = sample.TkbType,
                OwnerNodeId       = sample.OwnerNodeId,
                InitType          = ReliableInitType.None,  // IG is a ghost replica, not authority
                InitialComponents = new List<object> { sample },
                RequestId         = Guid.Empty
            });
        }
        else
        {
            // Existing entity — update its component
            if (_entityMap.TryGetEntity(sample.EntityId, out var entity))
                world.SetComponent(entity, sample);
        }
    }
}
```

**WorldPosTranslator** (DDS → ECS):
```csharp
public class WorldPosTranslator : ITranslator
{
    private readonly IGeographicTransform _geo;
    private readonly NetworkEntityMap _entityMap;
    
    public void OnReceived(Hrot.DDS.WorldPos sample, SampleInfo info, EntityRepository world)
    {
        if (!_entityMap.TryGetEntity(sample.EntityId, out var entity)) return;
        
        var cartesian = _geo.ToCartesian(sample.Pos.Latitude, sample.Pos.Longitude, sample.Pos.Altitude);
        
        // Write to Network components. TransformSyncSystem will lerp SimTransform to these values.
        world.SetComponent(entity, new NetworkPosition { Value = cartesian });
        
        // Convert heading to Quaternion
        float headingRad = sample.Rot.Heading * (MathF.PI / 180f);
        var rot = Quaternion.CreateFromYawPitchRoll(-headingRad, 0, 0);
        world.SetComponent(entity, new NetworkOrientation { Value = rot });
    }
}
```

---

### 4.4 Visualizer Adapter

**SstVisualizerAdapter** (connects to EntityRenderLayer):
```csharp
public class SstVisualizerAdapter : IVisualizerAdapter
{
    private readonly Texture2D[] _textures;
    
    public void Render(Entity entity, ISimulationView view, RenderContext ctx)
    {
        var style = view.GetComponentRO<ResolvedStyle>(entity);
        // SimTransform from Fdp.Kernel — field access transform.Position.X/Y is already correct.
        var transform = view.GetComponentRO<SimTransform>(entity);
        var culling = view.GetComponentRO<CullingState>(entity);
        
        if (!culling.IsVisible) return;
        
        var screenPos = ctx.Camera.WorldToScreen(new Vector2(transform.Position.X, transform.Position.Y)); // Correct: SimTransform.Position
        
        // Draw icon
        var texture = GetTexture(style.TextureName);
        float scale = culling.LodLevel == 2 ? 0.5f : 1.0f;
        Raylib.DrawTextureEx(texture, screenPos, 0, scale, style.Tint);
        
        // Draw label
        if (culling.LodLevel < 2)
            Raylib.DrawText(style.LabelText, screenPos + Vector2.UnitY * 20, 12, Color.White);
        
        // Draw damage bar
        if (style.DamageLevel > 0)
            DrawDamageBar(screenPos + Vector2.UnitY * 30, style.DamageLevel);
        
        // Draw selection highlight
        if (view.HasComponent<SelectionState>(entity))
        {
            var sel = view.GetComponentRO<SelectionState>(entity);
            if (sel.IsSelected)
                Raylib.DrawCircleLines((int)screenPos.X, (int)screenPos.Y, 20, Color.Yellow);
        }
        
        // Draw history trail
        if (style.ShowTrail && view.TryGetComponent<HistoryTrail>(entity, out var trail))
            DrawTrail(trail, ctx);
    }
    
    private void DrawDamageBar(Vector2 pos, float damage)
    {
        float barWidth = 30;
        float barHeight = 4;
        
        Color color = damage < 30 ? Color.Green : damage < 70 ? Color.Yellow : Color.Red;
        
        Raylib.DrawRectangle((int)pos.X, (int)pos.Y, (int)(barWidth * damage / 100), (int)barHeight, color);
        Raylib.DrawRectangleLines((int)pos.X, (int)pos.Y, (int)barWidth, (int)barHeight, Color.White);
    }
    
    private void DrawTrail(HistoryTrail trail, RenderContext ctx)
    {
        for (int i = 0; i < trail.Points.Length - 1; i++)
        {
            var p1 = ctx.Camera.WorldToScreen(new Vector2(trail.Points[i].X, trail.Points[i].Y));
            var p2 = ctx.Camera.WorldToScreen(new Vector2(trail.Points[i+1].X, trail.Points[i+1].Y));
            Raylib.DrawLineEx(p1, p2, 2.0f, new Color(0, 255, 255, 128));
        }
    }
}
```

---

## 6. Tool System

### 6.1 Creation Tool

**Purpose:** Unified tool for spawning entities and drawing graphics

**Architecture:**
```csharp
public class CreationTool : IMapTool
{
    public enum CreationMode { Entity, Polyline, Polygon, Circle }
    
    private CreationMode _mode;
    private long _selectedTkbType;
    private ForceId _affiliation;
    private List<Vector2> _ghostPoints = new();
    
    public event Action<CreateEntityRequest>? OnEntityCreated;
    public event Action<MapVisualOverlay>? OnOverlayCreated;
    
    public void OnEnter(MapCanvas canvas)
    {
        _canvas = canvas;
        _ghostPoints.Clear();
    }
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (_mode == CreationMode.Entity)
        {
            // Single click spawn
            var request = new CreateEntityRequest
            {
                RequestId = Guid.NewGuid(),
                EntityId = 0, // Will be allocated
                TkbType = _selectedTkbType,
                Position = _geo.ToGeodetic(worldPos),
                Affiliation = _affiliation
            };
            OnEntityCreated?.Invoke(request);
            _canvas.PopTool(); // Return to standard tool
        }
        else
        {
            // Multi-point drawing
            if (button == MouseButton.Left)
                _ghostPoints.Add(worldPos);
            else if (button == MouseButton.Right)
                FinishPolyline();
        }
        return true;
    }
    
    public void Draw(RenderContext ctx)
    {
        // Draw ghost preview
        if (_mode == CreationMode.Entity)
        {
            var mousePos = _canvas.Input.GetMousePosition();
            var worldPos = ctx.Camera.ScreenToWorld(mousePos);
            DrawEntityGhost(worldPos, ctx);
        }
        else
        {
            DrawPolylineGhost(_ghostPoints, ctx);
        }
    }
    
    private void FinishPolyline()
    {
        var overlay = new MapVisualOverlay
        {
            OverlayId = GenerateLocalId(),
            Type = _mode == CreationMode.Polyline ? OverlayType.Line : OverlayType.Area,
            Points = _ghostPoints.Select(p => _geo.ToGeodetic(p)).ToList()
        };
        OnOverlayCreated?.Invoke(overlay);
        _canvas.PopTool();
    }
}
```

---

### 6.2 Measure Tool

**Purpose:** Distance and line-of-sight measurement

**Architecture:**
```csharp
public class MeasureTool : IMapTool
{
    public enum MeasureMode { Distance, LineOfSight }
    
    private MeasureMode _mode;
    private Vector2? _startPoint;
    private Vector2 _currentPoint;
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            if (_startPoint == null)
                _startPoint = worldPos;
            else
            {
                FinishMeasurement(_startPoint.Value, worldPos);
                _startPoint = null;
            }
        }
        else if (button == MouseButton.Right)
        {
            _canvas.PopTool(); // Cancel
        }
        return true;
    }
    
    public bool HandleHover(Vector2 worldPos)
    {
        _currentPoint = worldPos;
        return false;
    }
    
    public void Draw(RenderContext ctx)
    {
        if (_startPoint == null) return;
        
        var start = ctx.Camera.WorldToScreen(_startPoint.Value);
        var end = ctx.Camera.WorldToScreen(_currentPoint);
        
        Raylib.DrawLineEx(start, end, 2.0f, Color.Cyan);
        
        float distance = Vector2.Distance(_startPoint.Value, _currentPoint);
        string label = $"{distance:F1} m";
        Raylib.DrawText(label, (start + end) / 2, 14, Color.White);
    }
    
    private void FinishMeasurement(Vector2 start, Vector2 end)
    {
        if (_mode == MeasureMode.Distance)
        {
            float distance = Vector2.Distance(start, end);
            LogInfo($"Distance: {distance:F2} m");
        }
        else
        {
            // Line of sight via terrain service
            var visible = _terrainService.IsVisible(start, end);
            LogInfo($"Line of sight: {(visible ? "CLEAR" : "BLOCKED")}");
        }
    }
}
```

---

### 6.3 Edit Tool

**Purpose:** Vertex manipulation for overlays

**Architecture:**
```csharp
public class EditTool : IMapTool
{
    private Entity _targetOverlay;
    private int _selectedVertexIndex = -1;
    private List<Vector2> _ghostPoints;
    
    public void OnEnter(MapCanvas canvas)
    {
        var overlay = World.GetManagedComponent<MapVisualOverlay>(_targetOverlay);
        _ghostPoints = overlay.Points.Select(p => _geo.ToCartesian(p)).ToList();
    }
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _selectedVertexIndex = FindNearestVertex(worldPos);
        }
        else if (button == MouseButton.Right)
        {
            CommitChanges();
            _canvas.PopTool();
        }
        return true;
    }
    
    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        if (_selectedVertexIndex >= 0)
        {
            _ghostPoints[_selectedVertexIndex] = worldPos;
            return true;
        }
        return false;
    }
    
    public void Draw(RenderContext ctx)
    {
        // Draw ghost polyline
        for (int i = 0; i < _ghostPoints.Count - 1; i++)
        {
            var p1 = ctx.Camera.WorldToScreen(_ghostPoints[i]);
            var p2 = ctx.Camera.WorldToScreen(_ghostPoints[i+1]);
            Raylib.DrawLineEx(p1, p2, 2.0f, Color.Yellow);
        }
        
        // Draw vertex handles
        for (int i = 0; i < _ghostPoints.Count; i++)
        {
            var p = ctx.Camera.WorldToScreen(_ghostPoints[i]);
            Color color = i == _selectedVertexIndex ? Color.Red : Color.White;
            Raylib.DrawCircle((int)p.X, (int)p.Y, 5, color);
        }
    }
    
    private void CommitChanges()
    {
        var request = new UpdateEntityDescriptorRequest
        {
            EntityId = _targetOverlay,
            DescriptorType = EDescriptorType.dtMapVisualOverlay,
            Payload = new MapVisualOverlay
            {
                Points = _ghostPoints.Select(p => _geo.ToGeodetic(p)).ToList()
            }
        };
        _commandGateway.UpdateDescriptorAsync(request);
    }
}
```

---

## 7. Context Menu System

**Purpose:** Display IOS-driven right-click menus

**Architecture:**
```csharp
public class ContextMenuSystem : ComponentSystem
{
    private readonly DdsCommandGateway _gateway;
    
    protected override void OnUpdate()
    {
        // Listen for ContextActionsUpdate from IOS
        var updates = World.ConsumeEvents<ContextActionsUpdate>();
        
        foreach (var update in updates)
        {
            if (_entityMap.TryGetEntity(update.EntityId, out var entity))
            {
                World.SetManagedComponent(entity, new ContextMenuState
                {
                    Actions = update.Actions.ToList()
                });
**Dependencies:** Shared P1-P2 (Data Model), P4 (Commands) complete

            }
        }
    }
}
```

**UI Rendering (ImGui):**
```csharp
public void DrawContextMenu(Entity entity)
{
    if (!World.TryGetManagedComponent<ContextMenuState>(entity, out var menu)) return;
    
    if (ImGui.BeginPopupContextVoid("EntityMenu"))
    {
        foreach (var action in menu.Actions)
        {
            if (ImGui.MenuItem(action.Label))
            {
                ExecuteContextAction(entity, action);
            }
        }
        ImGui.EndPopup();
    }
}

private void ExecuteContextAction(Entity entity, ContextAction action)
{
    if (action.ActionName.StartsWith("IG_"))
    {
        // Local action (e.g., IG_Lock_Camera)
        HandleLocalAction(entity, action);
    }
    else
    {
        // Remote action: send event to IOS
        var evt = new ContextActionTriggered
        {
            EntityId = GetNetworkId(entity),
            ActionName = action.ActionName
        };
        _eventBus.Publish(evt);
    }
}
```

---

## 8. Rendering Pipeline

**Layer Stack (Bottom to Top):**
1. **BackgroundTileLayer** - Map tiles
2. **GridLayer** - Coordinate grid
3. **OverlayRenderLayer** - MapVisualOverlay (areas, lines)
4. **EntityRenderLayer** - Entities via SstVisualizerAdapter
5. **EffectRenderLayer** - Explosions, tracers
6. **DebugGizmoLayer** - Debug shapes
7. **ActiveTool.Draw()** - Tool overlays (selection box, drag preview)

**Main Render Loop:**
```csharp
public void Render()
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);
    
    Raylib.BeginMode2D(GetRaylibCamera2D(_canvas.Camera));
    
    var ctx = new RenderContext
    {
        Camera = _canvas.Camera,
        Zoom = _canvas.Camera.Zoom,
        Time = _time.TotalTime
    };
    
    // Render layers
    foreach (var layer in _canvas.Layers)
    {
        if (_canvas.IsLayerVisible(layer))
            layer.Render(ctx);
    }
    
    // Render active tool overlay
    _canvas.ActiveTool?.Draw(ctx);
    
    Raylib.EndMode2D();
    
    // Render UI
    RenderImGui();
    
    Raylib.EndDrawing();
}
```

---

## 9. Critical Edge Cases & Mitigations

### 9.1 ImGui Input Blocking

**Issue:** ImGui panels overlay the Raylib map. Clicking an ImGui button might also trigger a `MapClickEvent` on the map underneath (click-through).

**Solution:**
- Before processing map input, check `ImGui.GetIO().WantCaptureMouse`
- If `true`, skip `MapCanvas.Update()` for that frame
- Prevents accidental map interactions while using UI

**Code Pattern:**
```csharp
public void Update(float dt)
{
    _world?.Update(dt);
    
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DARKGRAY);
    
    // Render map BEFORE ImGui to ensure correct layering
    if (!ImGui.GetIO().WantCaptureMouse)
    {
        _canvas?.Update(dt);  // Process map input only if ImGui not capturing
    }
    _canvas?.Render();
    
    rlImGui.Begin();
    DrawIgPanels();
    rlImGui.End();
    
    Raylib.EndDrawing();
}
```

### 9.2 Tool Preemption & Cleanup

**Issue:** If user is mid-interaction (e.g., 2nd click of 2-click measurement) and IOS changes the tool via `MapInteractionConfig`, the previous tool might leave ghost entities or corrupt state.

**Solution:**
- `ToolManager.SwitchTool()` MUST call `currentTool.OnExit()` before switching
- Each tool's `OnExit()` must clean up temporary entities (ghosts, rulers, selection boxes)
- Use `[Temp]` tag component for cleanup: `world.Query().With<TempGhost>().Delete()`

**Code Pattern:**
```csharp
public void SwitchTool(IMapTool newTool)
{
    if (_currentTool != null)
    {
        _currentTool.OnExit();  // Critical: clean up before switching
        _currentTool.Dispose();
    }
    
    _currentTool = newTool;
    _currentTool?.OnEnter();
}

// In MeasureTool.OnExit():
public override void OnExit()
{
    // Delete temporary ruler lines
    var tempEntities = _world.Query().With<TempMeasurement>().Build();
    foreach (var entity in tempEntities)
    {
        _world.DeleteEntity(entity);
    }
    
    _startPoint = null;
    _isWaitingForSecondClick = false;
}
```

### 9.3 Headless Camera Abstraction

**Issue:** Headless mode skips Raylib window creation, but some logic might call `Raylib.GetMousePosition()` or `Camera.ScreenToWorld()`, causing crashes or returning garbage.

**Solution:**
- Abstract camera operations behind `ICameraService` interface
- In headless mode, inject `HeadlessCamera` that returns mathematical projections without GPU calls
- Tools must check `IsHeadless` before accessing mouse input

**Code Pattern:**
```csharp
public interface ICameraService
{
    Vector2 ScreenToWorld(Vector2 screenPos);
    Vector2 WorldToScreen(Vector2 worldPos);
    Rectangle GetViewBounds();
}

public class HeadlessCamera : ICameraService
{
    private readonly Rectangle _virtualView;
    
    public HeadlessCamera(float width, float height)
    {
        _virtualView = new Rectangle(0, 0, width, height);
    }
    
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        // Return identity transform (screen = world for headless)
        return screenPos;
    }
    
    public Rectangle GetViewBounds() => _virtualView;
}

// In IgSubsystem.Initialize():
if (_config.Headless)
{
    _cameraService = new HeadlessCamera(1920, 1080);
}
else
{
    _cameraService = new RaylibCameraService(_canvas.Camera);
}
```

### 9.4 Late-Arriving Style Updates

**Issue:** If IG starts before IOS, it renders entities with default TKB styles. When IOS finally sends `MapInteractionConfig`, styles change abruptly (visual pop).

**Solution:**
- Not critical for mock, but production should interpolate style changes
- For mock: Acceptable to have instant style change
- Document in user guide: "Start IOS before IG for consistent initial styling"

### 9.5 Dead Reckoning Divergence

**Issue:** If network latency spikes, dead reckoning might extrapolate entity position far from truth, causing "rubber-banding" when truth update arrives.

**Solution:**
- Implement `MaxExtrapolationTime` (e.g., 500ms)
- If time since last truth update exceeds limit, freeze extrapolation
- Use exponential smoothing when truth update arrives to reduce snap

**Code Pattern:**
```csharp
if (timeSinceLastUpdate > MaxExtrapolationTime)
{
    // Freeze at last known position, don't extrapolate further
    return lastKnownPosition;
}

// When truth arrives
var error = truthPosition - deadReckonedPosition;
if (error.Length() > SnapThreshold)
{
    // Large error: blend over multiple frames
    smoothedPosition = Lerp(deadReckonedPosition, truthPosition, 0.3f);
}
else
{
    // Small error: snap immediately
    smoothedPosition = truthPosition;
}
```

---

## 10. Implementation Plan

### Phase 1: Core Infrastructure (2 days)
- **IG.1.1**: Create Hrot.IG project
- **IG.1.2**: Setup MapCanvas with camera controls
- **IG.1.3**: Integrate NetworkDemo network module
- **IG.1.4**: Add EntityRenderLayer with stub visualizer
**Dependencies:** Phase 1 complete, Shared P5 (TKB Definitions) complete


### Phase 2: Basic Rendering (3 days)
- **IG.2.1**: Implement ResolvedStyle component
- **IG.2.2**: Implement StyleResolutionSystem
- **IG.2.3**: Create SstVisualizerAdapter (icon, label, damage bar)
- **IG.2.4**: Add MapCullingSystem
- **IG.2.5**: Test: Render 100 entities from DDS
**Dependencies:** Phase 2 complete, BdcCommandGateway available

- **IG.3.1**: Integrate StandardInteractionTool
- **IG.3.2**: Add selection highlighting
- **IG.3.3**: Implement CreationTool (uses `_gateway.CreateEntityAsync()`)
- **IG.3.4**: Implement MeasureTool (distance)
- **IG.3.5**: Test: Create entity, send to SimHost via gatewayment)
**Dependencies:** Phase 3 complete

- **IG.4.1**: Implement HistoryRecordingSystem
- **IG.4.2**: Implement EventToEffectSystem
- **IG.4.3**: Add context menu system
- **IG.4.4**: Implement EditTool (uses `_gateway.UpdateDescriptorAsync()`
- **IG.4.1**: Implement HistoryRecordingSystem
- **IG.4.2**: Implement EventToEffectSystem
- **IG.4.3**: Add context menu system
**Dependencies:** Phase 4 complete

- **IG.5.1**: Create debug panel (time control, recording)
- **IG.5.2**: Add entity inspector panel
- **IG.5.3**: Add mini-IOS panel (spawner, TKB browser) (uses `_gateway`)
- **IG.5.4**: Add performance metrics overlay

**Total Effort:** 14 developer-days (~3 weeks)

**Critical Blocker:** IG cannot start Phase 3-5 until Shared P4 (FDP.Toolkit.Commands) is complete
- **IG.5.3**: Add mini-IOS panel (spawner, TKB browser)
- **IG.5.4**: Add performance metrics overlay

**Total Effort:** 14 developer-days (~3 weeks)

---

## 10. Embeddability Architecture

### 10.1 Overview

IG is designed to run in **two deployment modes**:
1. **Standalone Application** - Independent executable (`Hrot.IG.Standalone.exe`)
2. **Embedded Subsystem** - Library embedded in aggregated runner (`Hrot.ClusterRunner.exe`)

This dual-mode design enables:
- Independent map viewer development
- Integration into combined dashboard with IOS panels
- Headless rendering for automated testing
- Shared Raylib window with embedded ImGui context

**Reference:** See [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) for full aggregated application architecture.

### 10.2 ISubsystem Interface Implementation

**Interface:** `ISubsystem` (defined in `Hrot.ClusterRunner.Models.ISubsystem.cs`)

IG implements the standard subsystem interface:

```csharp
public class IgSubsystem : SubsystemBase
{
    private FdpWorld? _world;
    private DdsParticipant? _participant;
    private IgConfiguration _config;
    private MapCanvas? _canvas;
    private SubsystemStatusPublisher? _statusPublisher;
    
    public override string Name => "ig";
    
    // Lifecycle Methods
    public override void Initialize(object config)
    {
        _config = (IgConfiguration)config;
        
        // Initialize Raylib window (only if not headless)
        if (!_config.Headless)
        {
            Raylib.InitWindow(1920, 1080, "IG Mock");
            Raylib.SetTargetFPS(60);
            rlImGui.Setup(true);
        }
        
        // Create FDP World
        _world = new FdpWorld();
        
        // Add modules
        _world.AddModule<EntityLifecycleModule>();
        _world.AddModule<Vis2DModule>();
        
        // Create MapCanvas
        if (!_config.Headless)
        {
            _canvas = new MapCanvas(_world);
            _canvas.AddLayer(new EntityRenderLayer());
            _canvas.AddLayer(new DebugGizmoLayer());
        }
        
        Status = SubsystemStatus.Ready;
    }
    
    public override void ConnectToDomain(int domainId)
    {
        _participant = new DdsParticipant(domainId);
        var nodeMapper    = new NodeIdMapper(localDomain: domainId, localInstance: _config.NodeId);
        var topology      = new StaticNetworkTopology(_config.NodeId, Array.Empty<int>());
        var idAllocator   = new DdsIdAllocator(_participant, isServer: false);
        var serialisation = new SerializationRegistry();
        var elm           = _world.GetModule<EntityLifecycleModule>();

        var translators = new List<IDescriptorTranslator>
        {
            new EntityMasterTranslator(_participant, _entityMap, _eventBus),
            new WorldPosTranslator(_participant, _entityMap, _geoTransform),
            // CRITICAL: Bridge DDS TimePulse to the EventBus for SlaveTimeController
            new AutoCycloneTranslator<TimePulseDescriptor>(_participant, "TimePulse", 100, _entityMap)
        };
        
        var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(_participant, typeof(IgSubsystem).Assembly, _entityMap);
        translators.AddRange(autoTranslators);

        var networkModule = new CycloneNetworkModule(
            _participant, nodeMapper, idAllocator, topology, elm,
            serialisation, translators, _entityMap
        );
        _kernel.RegisterModule(networkModule);
        
        // Set SlaveTimeController AFTER network module (so TimePulse subscription exists)
        _kernel.SetTimeController(new SlaveTimeController(_eventBus));
        
        // Announce presence
        _statusPublisher = new SubsystemStatusPublisher(_participant, _config.NodeId, "ig");
        _statusPublisher.UpdateStatus(SubsystemStatus.Ready);
    }
    
    public override void Start()
    {
        Status = SubsystemStatus.Running;
    }
    
    public override void Update(float deltaTime)
    {
        // Update ECS
        _world?.Update(deltaTime);
        
        // Render (if not headless)
        if (!_config.Headless)
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKGRAY);
            
            _canvas?.Render();
            
            // Draw ImGui panels (IG panels + shared IOS panels if embedded)
            rlImGui.Begin();
            DrawIgPanels();
            rlImGui.End();
            
            Raylib.EndDrawing();
        }
    }
    
    // ... other ISubsystem methods
}
```

### 10.3 Raylib Window Ownership

**Critical Design Decision:** IG owns the main thread and Raylib window

**Rationale:**
- Raylib requires control of the main rendering loop
- IG's 60Hz update loop drives the entire application when embedded
- Other subsystems (IOS, SimHost) update within IG's frame loop

**Orchestrator Integration:**
```csharp
// In SubsystemOrchestrator.RunMainLoop()
if (HasIgSubsystem())
{
    // IG drives the loop
    while (!Raylib.WindowShouldClose())
    {
        float dt = Raylib.GetFrameTime();
        
        // Update all subsystems (IG updates its Raylib window internally)
        foreach (var subsystem in _subsystems)
            subsystem.Update(dt);
    }
}
else
{
    // Headless loop (fixed timestep)
    RunHeadlessLoop();
}
```

### 10.4 ImGui Context Sharing

**Embedded Mode:** IOS panels share IG's ImGui context

```csharp
public override void Update(float deltaTime)
{
    // ... Raylib rendering
    
    rlImGui.Begin();
    
    // IG's own panels
    DrawIgPanels();
    
    // If IOS is embedded, let it draw its panels too
    if (_embeddedIos != null)
    {
        _embeddedIos.DrawPanels();  // IOS draws into IG's ImGui context
    }
    
    rlImGui.End();
}
```

**Standalone Mode:** IG runs its own ImGui context independently

### 10.5 Refactoring Strategy

**Current Structure:**
```
Hrot.IG/
├── Program.cs
├── Tools/
│   ├── CreationTool.cs
│   └── ...
└── Systems/
    └── ...
```

**Refactored Structure:**
```
Hrot.IG/ (Library)
├── IgSubsystem.cs               ← NEW: ISubsystem implementation
├── IgConfiguration.cs            ← NEW: Configuration model
├── Tools/                        ← UNCHANGED
│   ├── CreationTool.cs
│   └── ...
└── Systems/                      ← UNCHANGED
    └── ...

Hrot.IG.Standalone/ (Executable)
└── Program.cs                    ← NEW: Thin wrapper
```

### 10.6 Configuration Model

```csharp
public class IgConfiguration
{
    public int NodeId { get; set; } = 2;
    public bool Headless { get; set; }
    public int WindowWidth { get; set; } = 1920;
    public int WindowHeight { get; set; } = 1080;
    public bool EnableRecording { get; set; }
    public string? MapConfigFile { get; set; }
}
```

### 10.7 Headless Mode Support

**When `Headless = true`:**
- **No Raylib Window**: Skip `InitWindow()`, `BeginDrawing()`, `EndDrawing()`
- **No Rendering**: Skip MapCanvas rendering
- **Logic Only**: Still run ECS systems for network ingress, dead reckoning
- **Metrics**: Collect FPS metrics from fixed timestep loop

**Use Case:** Automated testing verifying network synchronization without graphics

```csharp
public override void Update(float deltaTime)
{
    _world?.Update(deltaTime);
    
    if (!_config.Headless)
    {
        // Only render when NOT headless
        Raylib.BeginDrawing();
        _canvas?.Render();
        Raylib.EndDrawing();
    }
}
```

### 10.8 Waiting Room Integration

**Protocol:** IG announces presence via `SubsystemStatusAnnounce` topic

```csharp
public override void AnnounceReady()
{
    _statusPublisher?.UpdateStatus(SubsystemStatus.Ready);
}

public override async Task WaitForReady()
{
    // Wait for SimHost to be ready before starting
    var coordinator = new WaitingRoomCoordinator(_participant, _logger);
    await coordinator.WaitForPeersAsync(new[] { "simhost" }, timeoutSeconds: 30);
}
```

### 10.9 Deployment Modes

**Mode 1: Standalone IG**
```bash
Hrot.IG.Standalone.exe --domain 0 --node-id 2
```

**Mode 2: Embedded in Runner (Combined View)**
```bash
Hrot.ClusterRunner.exe --mode all --domain 0
# IG window shows map + IOS panels in ImGui docking layout
```

**Mode 3: Embedded in Runner (Headless Testing)**
```bash
Hrot.ClusterRunner.exe --mode ig --domain 0 --headless --script test.json
# IG runs network logic without Raylib window
```

### 10.10 Implementation Tasks

See [TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md) Phase R2:
- **R2.4**: Refactor IG to IgSubsystem Library (1.0d)
- **R2.5**: Create IG Standalone Program.cs (0.25d)
- **R2.6**: Test IG Embeddability (0.5d)

**Dependencies:**
- Runner Phase R1 complete (ISubsystem interface defined)
- IG Phases IG.1-IG.5 complete (all functionality implemented)

### 10.11 Testing Strategy

**Unit Tests:**
- `Test_IG_Initialize`: Verify window creation (non-headless)
- `Test_IG_InitializeHeadless`: Verify skips window creation
- `Test_IG_RaylibOwnership`: Verify IG owns main thread

**Integration Tests:**
- `Test_IG_Standalone`: Run standalone with Raylib window
- `Test_IG_Embedded`: Run via orchestrator
- `Test_IG_SharedImGui`: Verify IOS panels render in IG's context
- `Test_IG_Headless`: Run without Raylib, verify ECS still updates

**Verification:**
- Raylib window lifecycle correct in both modes
- ImGui context sharing works with IOS
- Headless mode maintains network synchronization
- FPS stable at 60Hz in all modes

---

## References

- [DESIGN-SHARED.md](./DESIGN-SHARED.md) - Infrastructure components
- [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) - SimHost architecture
- [TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md) - Detailed implementation tasks
- [TASK-TRACKER.md](./TASK-TRACKER.md) - Progress tracking
