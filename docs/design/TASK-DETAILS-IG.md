# IG Mock Implementation Tasks

**Version:** 1.0  
**Date:** 2026-02-14  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-IG.md](./DESIGN-IG.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document provides **detailed task breakdown** for implementing IG Mock components. Each task includes acceptance criteria, estimated effort, and dependencies.

**Total Effort:** ~14 developer-days (~3 weeks for 1 developer)

---

## Phase 1: Core Infrastructure (2 days)

### Task IG.1.1: Create Bagira.IG Project

**Goal:** Setup IG project structure with dependencies

**Steps:**
1. Create new project:
   ```bash
   dotnet new console -n Bagira.IG -f net8.0
   ```
2. Add to solution `IOS-IG-SimHost.sln`.
3. Location: `Bagira.IG/`

4. Add project references:
   - `FDP.Kernel` (ECS)
   - `ModuleHost.Core`
   - `ModuleHost.Network.Cyclone`
   - `FDP.Toolkit.Vis2D` (MapCanvas, Tools, Layers)
   - `FDP.Toolkit.Replication` (Network entity mapping)
   - `FDP.Toolkit.Lifecycle` (Entity lifecycle)
   - `FDP.Toolkit.NetworkSpawning` (Unified entity spawning)
   - `FDP.Toolkit.Time` (SlaveTimeController)
   - `Fdp.Toolkit.Geographic` (WGS84 transform)
   - `FDP.Toolkit.Tkb` (TKB database)
   - `Bagira.DDS.DataModel` (DDS types)
   - `Bagira.Map.Definitions` (TKB descriptors)
   - `Bagira.Map.Common` (Constants)
   
5. Add NuGet packages:
   - `Raylib-cs`
   - `rlImgui-cs` version `3.2.0`
   - `CycloneDDS.NET`
   - `NLog`

**Folder Structure:**
```
Bagira.IG/
  Components/       (ECS components)
  Systems/          (ECS systems)
  Tools/            (MapTool implementations)
  Translators/      (DDS → ECS)
  UI/               (ImGui panels)
  Adapters/         (Visualizers)
  Program.cs        (Main entry point)
```

**Acceptance Criteria:**
- ✅ Project compiles successfully
- ✅ All dependencies resolved
- ✅ Can run empty Raylib window (640x480)

**Estimated Effort:** 0.5 days

**Dependencies:** None

---

### Task IG.1.2: Setup MapCanvas with Camera Controls

**Goal:** Initialize MapCanvas with basic pan/zoom

**Steps:**
1. Create `IgApplication` class wrapping Raylib window
2. Initialize `MapCanvas` with`RaylibInputProvider`
3. Setup `MapCamera`:
   - Initial position: (5000, 5000) meters
   - Initial zoom: 0.5 (2 meters per pixel)
   - Zoom limits: 0.01 (100 m/px) to 5.0 (0.2 m/px)
4. Add pan controls:
   - Middle mouse drag
   - Arrow keys (10 m/s)
5. Add zoom controls:
   - Mouse wheel (1.2x per tick)
   - +/- keys
6. Add debug overlay showing:
   - Camera position
   - Zoom level
   - Cursor world coordinates

**Implementation:**
```csharp
public class IgApplication
{
    private MapCanvas _canvas;
    private MapCamera _camera;
    
    public void Initialize()
    {
        Raylib.InitWindow(1600, 900, "IG Mock");
        Raylib.SetTargetFPS(60);
        
        _canvas = new MapCanvas(new RaylibInputProvider());
        _camera = new MapCamera
        {
            Position = new Vector2(5000, 5000),
            Zoom = 0.5f
        };
        _canvas.Camera = _camera;
    }
    
    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            
            // Input
            HandleCameraInput(dt);
            
            // Update
            _canvas.Update(dt);
            
            // Render
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            _canvas.Render(new RenderContext { Camera = _camera, Zoom = _camera.Zoom });
            DrawDebugInfo();
            Raylib.EndDrawing();
        }
    }
    
    private void HandleCameraInput(float dt)
    {
        // Pan with arrow keys
        if (Raylib.IsKeyDown(KeyboardKey.Right))
            _camera.Position += Vector2.UnitX * 10 * dt;
        // ... other directions
        
        // Zoom with wheel
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
            _camera.Zoom *= MathF.Pow(1.2f, wheel);
    }
}
```

**Acceptance Criteria:**
- ✅ Window opens at 1600x900
- ✅ Camera pans with arrow keys and middle mouse
- ✅ Camera zooms with mouse wheel
- ✅ Debug overlay shows position, zoom, cursor coords
- ✅ No flickering or stuttering at 60 FPS

**Estimated Effort:** 0.5 days

**Dependencies:** IG.1.1

---

### Task IG.1.3: Integrate NetworkDemo Network Module

**Goal:** Set up DDS integration for IG using `CycloneNetworkModule`, following the NetworkDemo pattern.

> ⚠️ **Architecture notes:**
>
> 1. **Do NOT create a custom `IgNetworkModule`** that manually registers `CycloneIngressSystem`, `CycloneEgressSystem`, or `SmartEgressSystem`. These are **internal systems** owned by `CycloneNetworkModule`. Registering them manually alongside the module causes double-execution and conflicts. Provide your **Translator** list to the module constructor; the module installs all required systems itself.
>
> 2. **Time Sync requires a `TimePulseDescriptor` translator.** The `SlaveTimeController` listens on the FDP internal `FdpEventBus` for `TimePulse` events. Without a translator that bridges the DDS `TimePulse` topic to the local `FdpEventBus`, the `SlaveTimeController` will never receive updates and IG time will be frozen. Register an `AutoCycloneTranslator<TimePulseDescriptor>` (or equivalent `BlitEventTranslator`) in the translator list passed to `CycloneNetworkModule`.

**Steps:**
1. Follow `NetworkDemo.NetworkDemoApp` initialization sequence:
   - DDS participant creation
   - `NodeIdMapper` setup (`localDomain: 0, localInstance: 300` for IG)
   - `NetworkEntityMap` initialization
   - `SlaveTimeController` setup via `kernel.SetTimeController(...)`
2. Build the **translator list** for `CycloneNetworkModule`:
   - `EntityMasterTranslator` (DDS `EntityMaster` → ECS, triggers ELM construction)
   - `GeoSpatialTranslator` (DDS `GeoSpatial` → `NetworkReceivedState` for dead reckoning)
   - `EntityInfoTranslator` (DDS `EntityInfo` → ECS component)
   - **`AutoCycloneTranslator<TimePulseDescriptor>`** → bridges DDS time pulses to `FdpEventBus` for `SlaveTimeController` (**critical**)
   - Auto-translators via `ReplicationBootstrap.CreateAutoTranslators`
3. Instantiate and register `CycloneNetworkModule` (it installs ingress, egress, and gateway systems internally).
4. Set `SlaveTimeController` on the kernel after network module is registered.

**Implementation:**
```csharp
// In IG program/subsystem init (following NetworkDemoApp pattern):

var participant  = new DdsParticipant(domainId: 0);
var nodeMapper   = new NodeIdMapper(localDomain: 0, localInstance: 300); // IG instance ID
var entityMap    = new NetworkEntityMap();
var serialisation = new SerializationRegistry();
var topology     = new NetworkTopology { IsServer = false };
var idAllocator  = new DdsIdAllocator(participant, isServer: false); // Client-side allocator

var elm = new EntityLifecycleModule(tkb, peerNodeIds);
kernel.RegisterModule(elm);
kernel.RegisterModule(new ReplicationLogicModule());

// Build translator list — all network <-> ECS bridging goes here
var translators = new List<ITranslator>();
translators.Add(new EntityMasterTranslator(participant, entityMap, elm)); // triggers BeginConstruction
translators.Add(new GeoSpatialTranslator(participant, entityMap, geoTransform));
translators.Add(new EntityInfoTranslator(participant, entityMap));

// CRITICAL: Time Pulse translator — bridges DDS TimePulse → FdpEventBus
// Without this, SlaveTimeController never receives updates.
translators.Add(new AutoCycloneTranslator<TimePulseDescriptor>(participant, eventBus));

// Auto-translators for all [FdpDescriptor]-tagged types in Bagira.DDS.DataModel
var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(
    participant, typeof(IgProgram).Assembly, entityMap);
translators.AddRange(autoTranslators);

// Register CycloneNetworkModule — this installs CycloneIngressSystem, CycloneEgressSystem,
// SmartEgressSystem, NetworkGatewaySystem internally. Do NOT add them separately.
var networkModule = new CycloneNetworkModule(
    participant, nodeMapper, idAllocator, topology, elm,
    serialisation, translators, entityMap
);
kernel.RegisterModule(networkModule);

// Set up SlaveTimeController AFTER network module is ready
// (so the TimePulse DDS topic subscription exists when it starts)
var timeController = new SlaveTimeController(eventBus);
kernel.SetTimeController(timeController);
```

**Translators (corrected implementations):**
```csharp
public class EntityMasterTranslator : ITranslator
{
    private readonly NetworkEntityMap _entityMap;
    private readonly FdpEventBus _eventBus;

    public void OnReceived(EntityMaster sample, SampleInfo info, EntityRepository world)
    {
        if (info.InstanceState == InstanceState.Disposed)
        {
            // Delegate destruction to NetworkSpawningSystem
            _eventBus.Publish(new DestroyEntityCommand { NetworkId = sample.EntityId });
            return;
        }

        if (!_entityMap.Contains(sample.EntityId))
        {
            // New remote entity — publish SpawnEntityCommand
            // InitType = None: IG is a ghost replica, not an authority node
            _eventBus.Publish(new SpawnEntityCommand
            {
                NetworkId         = sample.EntityId,
                TkbType           = sample.TkbType,
                OwnerNodeId       = sample.OwnerNodeId,
                InitType          = ReliableInitType.None,
                InitialComponents = new List<object> { sample },
                RequestId         = Guid.Empty
            });
        }
        else
        {
            // Known entity — update component in-place
            if (_entityMap.TryGetEntity(sample.EntityId, out var entity))
                world.SetComponent(entity, sample);
        }
    }
}
```

**Acceptance Criteria:**
- ✅ DDS participant connects to domain 0
- ✅ `CycloneNetworkModule` registered with full translator list (not custom `IgNetworkModule`)
- ✅ No standalone `SmartEgressSystem` or `CycloneIngressSystem` registered outside the module
- ✅ `AutoCycloneTranslator<TimePulseDescriptor>` registered (time sync works)
- ✅ `kernel.SetTimeController(new SlaveTimeController(eventBus))` called after network module
- ✅ Receives `EntityMaster` from SimHost → `SpawnEntityCommand` published
- ✅ `NetworkSpawningSystem` processes `SpawnEntityCommand` and creates entities via ELM
- ✅ `NetworkEntityMap` correctly maps IDs

**Estimated Effort:** 0.75 days

**Dependencies:** IG.1.2, NS1 (FDP.Toolkit.NetworkSpawning complete)

---

### Task IG.1.3b: Register NetworkSpawningSystem in IG Kernel

**Goal:** Register `FDP.Toolkit.NetworkSpawning.NetworkSpawningSystem` (via a `SpawningModule` wrapper) so that `SpawnEntityCommand` and `DestroyEntityCommand` events published by `EntityMasterTranslator` are processed each ECS tick.

**Steps:**
1. Create `Bagira.IG.Modules.SpawningModule` wrapping `NetworkSpawningSystem` (same pattern as NetworkDemo):
```csharp
namespace Bagira.IG.Modules
{
    public class SpawningModule : IModule
    {
        private NetworkSpawningSystem _system;

        public void Initialize(ModuleHostKernel kernel)
        {
            var elm        = kernel.GetModule<EntityLifecycleModule>();
            var entityMap  = kernel.GetModule<NetworkEntityMap>();
            var tkbDb      = kernel.GetModule<TkbDatabase>();
            var eventBus   = kernel.GetService<FdpEventBus>();
            var idAlloc    = kernel.GetService<DdsIdAllocator>();

            // IG is a ghost node — localNodeId chosen as 300 (see NodeIdMapper)
            const int igNodeId = 300;

            _system = new NetworkSpawningSystem(
                tkbDb, elm, entityMap, idAlloc, eventBus, igNodeId,
                // DisTypeExtractor delegate: decouples Toolkit from Bagira.DDS.DataModel
                (object c, out ulong dis) => {
                    if (c is Bagira.BDC.SSTD.EntityMaster m) { dis = m.DisType; return true; }
                    dis = 0; return false;
                });
            kernel.RegisterSystem(_system);
        }

        public void Dispose() { }
    }
}
```
2. Register `SpawningModule` in `Program.cs` (before `CycloneNetworkModule`):
```csharp
kernel.RegisterModule(new SpawningModule());
```

**Acceptance Criteria:**
- ✅ `SpawningModule` registered in kernel
- ✅ `NetworkSpawningSystem` processes `SpawnEntityCommand` events each tick
- ✅ `DestroyEntityCommand` triggers entity teardown via ELM
- ✅ Integration test: SimHost spawns entity → IG receives EntityMaster DDS → SpawnEntityCommand → entity in IG ECS within 1 frame

**Estimated Effort:** 0.5 days

**Dependencies:** IG.1.3, NS1

---

### Task IG.1.4: Add EntityRenderLayer with Stub Visualizer

**Goal:** Render placeholder icons for network entities

**Steps:**
1. Create `StubVisualizerAdapter`:
   - Draw colored circles for entities
   - Red = unknown
   - Size = 10 pixels
2. Add `EntityRenderLayer` to MapCanvas
3. Create query: `With<EntityMasterComponent, SimTransform>`
4. Test with SimHost spawning 10 entities

> ⚠️ **Phase 0 Note (SimTransform):** `SimTransform` from `Fdp.Kernel` is the single canonical spatial component — do **not** redefine it locally in the IG project. All ECS queries, field accesses (`transform.Position`, `transform.Rotation`), and spawn calls (`new SimTransform { Position = ..., Rotation = ... }`) throughout this document are already correct as written. See BCS-P0-T1.

**Implementation:**
```csharp
public class StubVisualizerAdapter : IVisualizerAdapter
{
    public void Render(Entity entity, ISimulationView view, RenderContext ctx)
    {
        if (!view.TryGetComponent<SimTransform>(entity, out var transform))
            return;
        
        var screenPos = ctx.Camera.WorldToScreen(new Vector2(transform.Position.X, transform.Position.Y));
        Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 10, Color.Red);
        
        // Draw entity ID
        if (view.TryGetComponent<NetworkIdentity>(entity, out var netId))
        {
            string label = $"#{netId.NetworkId}";
            Raylib.DrawText(label, screenPos + Vector2.UnitY * 15, 10, Color.White);
        }
    }
    
    public Entity? PickEntity(Vector2 worldPos, ISimulationView view, EntityQuery query)
    {
        foreach (var entity in query.Entities)
        {
            var transform = view.GetComponentRO<SimTransform>(entity);
            float dist = Vector2.Distance(new Vector2(transform.Position.X, transform.Position.Y), worldPos);
            if (dist < 10) return entity;
        }
        return null;
    }
}
```

**Acceptance Criteria:**
- ✅ Entities from DDS appear as red circles
- ✅ Entity ID labels visible above icons
- ✅ Entities move smoothly (dead reckoning)
- ✅ 100+ entities render at 60 FPS

**Estimated Effort:** 0.5 days

**Dependencies:** IG.1.3


---

### Task IG.1.5: Create Bagira.IG.Tests Project

**Goal:** Setup unit test project.

**Steps:**
1. Create project:
   ```bash
   dotnet new mstest -n Bagira.IG.Tests -f net8.0
   ```
2. Location: `Bagira.IG.Tests/`
3. Add to solution `IOS-IG-SimHost.sln`.
4. Add reference to `Bagira.IG` project.

**Acceptance Criteria:**
- ✅ Test project created.
- ✅ Dependencies resolved.

**Estimated Effort:** 0.1 days

**Dependencies:** IG.1.1

---


## Phase 2: Basic Rendering (3 days)

### Task IG.2.1: Implement ResolvedStyle Component

**Goal:** Define ECS component for computed visual properties

**Steps:**
1. Create `Bagira.IG.Components.ResolvedStyle`:
   ```csharp
   public struct ResolvedStyle
   {
       public string TextureName;    // "tank_m1", "truck_cargo"
       public Color Tint;            // RGBA
       public string LabelText;      // Display name
       public ForceId Affiliation;   // Friend/Hostile/Neutral/Unknown
       public float DamageLevel;     // 0-100
       public bool ShowTrail;        // History trail enabled
       public bool ShowSensors;      // FOV sectors enabled
   }
   ```
2. Add default constructor with white tint, empty texture
3. Add unit test verifying struct size < 64 bytes (cache-friendly)

**Acceptance Criteria:**
- ✅ Component defined in `Bagira.IG.Components`
- ✅ Struct size verified < 64 bytes
- ✅ Default values set correctly

**Estimated Effort:** 0.25 days

**Dependencies:** None

---

### Task IG.2.2: Implement StyleResolutionSystem

**Goal:** Compute ResolvedStyle from TKB + network + user config

**Steps:**
1. Create `StyleResolutionSystem`:
   - Phase: `Simulation`
   - UpdateAfter: `NetworkIngressPhase`
2. Query: `With<EntityMasterComponent, SimTransform>`
3. Resolution layers:
   - **Layer 1 (TKB)**: Read `IgVisualDef` from TkbDatabase
   - **Layer 2 (Network)**: Apply `MapEntitySymbol` overrides (color, label, texture)
   - **Layer 3 (User)**: Apply `MapUserConfig` (force hostile, hide labels)
4. Handle damage integration:
   - Read `EntityDamage` descriptor if present
   - Map 0-100 damage to color gradient
5. Handle affiliation colors:
   - Friend: Blue (0, 100, 255, 255)
   - Hostile: Red (255, 0, 0, 255)
   - Neutral: Green (0, 255, 0, 255)
   - Unknown: White (255, 255, 255, 255)

**Implementation:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateAfter(typeof(NetworkIngressPhase))]
public class StyleResolutionSystem : ComponentSystem
{
    private readonly ITkbDatabase _tkb;
    private readonly MapUserConfig _userConfig;
    
    protected override void OnUpdate()
    {
        // SimTransform from Fdp.Kernel — query is already correct.
        Entities.With<EntityMasterComponent, SimTransform>().ForEach((entity, ref master, ref transform) =>
        {
            // Layer 1: TKB defaults
            var template = _tkb.GetTemplate(master.TkbType);
            var defaultVisual = template.GetDescriptor<IgVisualDef>();
            
            string texture = defaultVisual.SymbolTexture;
            Color tint = defaultVisual.Color;
            string label = defaultVisual.Name;
            ForceId affiliation = ForceId.Neutral;
            
            // Layer 2: Network overrides
            if (World.TryGetManagedComponent<MapEntitySymbol>(entity, out var symbol))
            {
                if (symbol.ForceAffiliation.HasValue)
                {
                    affiliation = symbol.ForceAffiliation.Value;
                    tint = GetAffiliationColor(affiliation);
                }
                if (!string.IsNullOrEmpty(symbol.TextureOverride))
                    texture = symbol.TextureOverride;
                if (!string.IsNullOrEmpty(symbol.LabelOverride))
                    label = symbol.LabelOverride;
            }
            
            // Layer 3: User config
            if (_userConfig.ForceHostile)
            {
                affiliation = ForceId.Hostile;
                tint = Color.Red;
            }
            if (_userConfig.HideLabels)
                label = "";
            
            // Damage integration
            float damage = 0;
            if (World.TryGetComponent<EntityDamageComponent>(entity, out var dmgComp))
                damage = dmgComp.Damage;
            
            World.SetComponent(entity, new ResolvedStyle
            {
                TextureName = texture,
                Tint = tint,
                LabelText = label,
                Affiliation = affiliation,
                DamageLevel = damage,
                ShowTrail = symbol?.ShowHistory ?? false
            });
        });
    }
    
    private Color GetAffiliationColor(ForceId affiliation) => affiliation switch
    {
        ForceId.Friend => new Color(0, 100, 255, 255),
        ForceId.Hostile => Color.Red,
        ForceId.Neutral => Color.Green,
        _ => Color.White
    };
}
```

**Unit Tests:**
1. Test default TKB styling
2. Test network override (MapEntitySymbol)
3. Test user config override (ForceHostile)
4. Test damage integration (0%, 50%, 100%)
5. Test missing components (graceful defaults)

**Acceptance Criteria:**
- ✅ System registered in Simulation phase
- ✅ All 3 layers correctly merge
- ✅ Affiliation colors match spec
- ✅ Damage scales correctly (0-100)
- ✅ Unit tests pass (>95% coverage)

**Estimated Effort:** 1.0 day

**Dependencies:** IG.2.1, Shared TKB extensions (Bagira.Map.Definitions)

---

### Task IG.2.3: Create SstVisualizerAdapter

**Goal:** Production-quality entity rendering

**Steps:**
1. Replace `StubVisualizerAdapter` with `SstVisualizerAdapter`
2. Implement features:
   - Load textures from `assets/symbols/` folder
   - Draw icon centered on entity position
   - Apply tint color
   - Draw label below icon (8-12 pt font)
   - Draw damage bar above icon (30x4 pixels, color-coded)
   - Draw selection highlight (yellow circle)
   - LOD support: Icon only when LodLevel=2
3. Add texture caching:
   - Dictionary<string, Texture2D>
   - Load on first use, cache for reuse
4. Handle missing textures:
   - Fallback to colored circle (affiliation color)

**Implementation:**
```csharp
public class SstVisualizerAdapter : IVisualizerAdapter
{
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    
    public void Render(Entity entity, ISimulationView view, RenderContext ctx)
    {
        var style = view.GetComponentRO<ResolvedStyle>(entity);
        var transform = view.GetComponentRO<SimTransform>(entity);
        
        if (!view.TryGetComponent<CullingState>(entity, out var culling) || !culling.IsVisible)
            return;
        
        var screenPos = ctx.Camera.WorldToScreen(new Vector2(transform.Position.X, transform.Position.Y));
        
        // Draw icon
        if (!string.IsNullOrEmpty(style.TextureName))
        {
            var texture = GetTexture(style.TextureName);
            float scale = culling.LodLevel == 2 ? 0.5f : 1.0f;
            var origin = new Vector2(texture.Width / 2, texture.Height / 2);
            Raylib.DrawTextureEx(texture, screenPos - origin, 0, scale, style.Tint);
        }
        else
        {
            // Fallback: colored circle
            Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 10, style.Tint);
        }
        
        // Draw label (skip if LOD=2)
        if (culling.LodLevel < 2 && !string.IsNullOrEmpty(style.LabelText))
        {
            var labelPos = screenPos + new Vector2(0, 20);
            Raylib.DrawText(style.LabelText, labelPos, 10, Color.White);
        }
        
        // Draw damage bar
        if (style.DamageLevel > 0)
        {
            var barPos = screenPos + new Vector2(-15, -25);
            DrawDamageBar(barPos, style.DamageLevel);
        }
        
        // Draw selection highlight
        if (view.TryGetComponent<SelectionState>(entity, out var sel) && sel.IsSelected)
        {
            Raylib.DrawCircleLines((int)screenPos.X, (int)screenPos.Y, 20, Color.Yellow);
        }
    }
    
    private void DrawDamageBar(Vector2 pos, float damage)
    {
        float barWidth = 30;
        float barHeight = 4;
        
        Color fillColor = damage < 30 ? Color.Green :
                          damage < 70 ? Color.Yellow :
                          Color.Red;
        
        Raylib.DrawRectangle((int)pos.X, (int)pos.Y, (int)(barWidth * damage / 100), (int)barHeight, fillColor);
        Raylib.DrawRectangleLines((int)pos.X, (int)pos.Y, (int)barWidth, (int)barHeight, Color.White);
    }
    
    private Texture2D GetTexture(string textureName)
    {
        if (_textureCache.TryGetValue(textureName, out var texture))
            return texture;
        
        string path = $"assets/symbols/{textureName}.png";
        if (File.Exists(path))
        {
            texture = Raylib.LoadTexture(path);
            _textureCache[textureName] = texture;
            return texture;
        }
        
        // Generate fallback texture (1x1 white pixel)
        return Texture2D.Default;
    }
    
    public Entity? PickEntity(Vector2 worldPos, ISimulationView view, EntityQuery query)
    {
        const float PICK_RADIUS = 15; // Pixels
        
        foreach (var entity in query.Entities)
        {
            if (!view.TryGetComponent<SimTransform>(entity, out var transform))
                continue;
            
            var entityPos2D = new Vector2(transform.Position.X, transform.Position.Y);
            float dist = Vector2.Distance(entityPos2D, worldPos);
            
            if (dist < PICK_RADIUS)
                return entity;
        }
        
        return null;
    }
}
```

**Test Assets:**
Create dummy textures in `assets/symbols/`:
- `tank_m1.png` (32x32, tank silhouette)
- `truck_cargo.png` (32x32, truck silhouette)
- `helicopter_ah64.png` (32x32, helicopter silhouette)

**Acceptance Criteria:**
- ✅ Icons render correctly from texture files
- ✅ Fallback to circle if texture missing
- ✅ Labels visible at LOD 0-1, hidden at LOD 2
- ✅ Damage bar renders with correct color gradient
- ✅ Selection highlight renders as yellow circle
- ✅ Texture cache prevents redundant loads
- ✅ 100+ entities render at 60 FPS

**Estimated Effort:** 1.0 day

**Dependencies:** IG.2.2

---

### Task IG.2.4: Add MapCullingSystem

**Goal:** Frustum culling and LOD based on zoom

**Steps:**
1. Create `MapCullingSystem`:
   - Phase: PreRender  
   - Query: `With<SimTransform>` (populated by MapCanvas)
3. Compute frustum from camera bounds
4. For each entity:
   - Check if position inside frustum
   - Compute LOD based on zoom:
     - Zoom < 0.1: LOD 2 (icon only)
     - Zoom < 0.5: LOD 1 (simplified)
     - Zoom >= 0.5: LOD 0 (full detail)
5. Set `CullingState` component

**Implementation:**
```csharp
[UpdateInPhase(SystemPhase.PreRender)]
public class MapCullingSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var camera = World.GetSingleton<MapCameraState>();
        var frustum = camera.ViewBounds; // Rectangle in world space
        var zoom = camera.Zoom;
        
        // SimTransform from Fdp.Kernel — query and field access are already correct.
        Entities.With<SimTransform>().ForEach((entity, ref transform) =>
            bool inFrustum = frustum.Contains(pos2D);
            
            byte lod = 0;
            if (zoom < 0.1f) lod = 2;
            else if (zoom < 0.5f) lod = 1;
            
            World.SetComponent(entity, new CullingState
            {
                IsVisible = inFrustum,
                LodLevel = lod,
                IsAggregated = false // TODO: Handle ORBAT aggregation
            });
        });
    }
}
```

**Unit Tests:**
1. Entity inside frustum → IsVisible=true
2. Entity outside frustum → IsVisible=false
3. Zoom=0.05 → LodLevel=2
4. Zoom=0.3 → LodLevel=1
5. Zoom=1.0 → LodLevel=0

**Acceptance Criteria:**
- ✅ Entities outside frustum not rendered
- ✅ LOD levels change correctly with zoom
- ✅ System runs in PreRender phase
- ✅ Unit tests pass

**Estimated Effort:** 0.5 days

**Dependencies:** IG.2.3

---

### Task IG.2.5: Integration Test - Render 100 Entities from DDS

**Goal:** End-to-end test of rendering pipeline

**Setup:**
1. Run SimHost Mock
2. SimHost spawns 100 entities at random positions (5km x 5km area)
3. SimHost publishes EntityMaster, GeoSpatial, EntityInfo

**Test Procedure:**
1. Launch IG Mock
2. Verify IG connects to DDS
3. Verify 100 entities appear on map
4. Pan camera to each quadrant
5. Zoom in/out (0.05x to 2.0x)
6. Measure FPS (should be 60)

**Acceptance Criteria:**
- ✅ All 100 entities visible
- ✅ Icons render with correct affiliation colors
- ✅ Labels visible at high zoom
- ✅ Damage bars visible (if SimHost sets EntityDamage)
- ✅ Culling works (entities outside frustum not rendered)
- ✅ LOD changes with zoom
- ✅ FPS remains 60

**Estimated Effort:** 0.25 days

**Dependencies:** IG.2.4, SimHost running

---

## Phase 3: Interaction Tools (3 days)

### Task IG.3.1: Integrate StandardInteractionTool

**Goal:** Reuse Vis2D StandardInteractionTool for selection

**Steps:**
1. Create `StandardInteractionTool` instance
2. Pass ECS query: `With<EntityMasterComponent, SimTransform>`
3. Pass `SstVisualizerAdapter` for picking
4. Subscribe to events:
   - `OnEntitySelectRequest` → Update `SelectionState` component
   - `OnRegionSelected` → Box selection
   - `OnEntityMoved` → Delegate to drag handling
5. Set as default tool in MapCanvas

**Implementation:**
```csharp
var selectionTool = new StandardInteractionTool(
    _world,
    _world.Query().With<EntityMasterComponent, SimTransform>().Build(),
    _visualizerAdapter
);

selectionTool.OnEntitySelectRequest += (entity, augment) =>
{
    if (!augment)
        ClearSelection();
    
    World.SetComponent(entity, new SelectionState { IsSelected = true });
};

selectionTool.OnRegionSelected += (entities) =>
{
    ClearSelection();
    foreach (var entity in entities)
        World.SetComponent(entity, new SelectionState { IsSelected = true });
};

_canvas.SwitchTool(selectionTool);
```

**Acceptance Criteria:**
- ✅ Click entity → selection highlight appears
- ✅ Shift+Click → multi-select
- ✅ Box select (Ctrl+Drag) → selects multiple
- ✅ Click background → deselects all
- ✅ Drag entity (Left+Drag) → todo IG.3.2

**Estimated Effort:** 0.5 days

**Dependencies:** IG.2.5

---

### Task IG.3.2: Add Selection Highlighting

**Goal:** Visual feedback for selected entities

**Steps:**
1. Create `SelectionRenderSystem`:
   - Phase: PostRender
   - Query: `With<SelectionState, SimTransform>`
2. For selected entities:
   - Draw yellow circle outline (radius 25 px)
   - Draw green fill circle for primary selection (first selected)
3. Update `SstVisualizerAdapter.Render()` to check `SelectionState`

**Implementation:**
```csharp
[UpdateInPhase(SystemPhase.PostRender)]
public class SelectionRenderSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var camera = World.GetSingleton<MapCameraState>();
        
        // SimTransform from Fdp.Kernel — query and field access are already correct.
        Entities.With<SelectionState, SimTransform>().ForEach((entity, ref sel, ref transform) =>
        {
            if (!sel.IsSelected) return;
            
            var screenPos = camera.WorldToScreen(new Vector2(transform.Position.X, transform.Position.Y));
            
            Color fillColor = sel.IsPrimarySelection ? new Color(0, 255, 0, 50) : new Color(255, 255, 0, 0);
            Color outlineColor = sel.IsPrimarySelection ? Color.Green : Color.Yellow;
            
            if (fillColor.A > 0)
                Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 25, fillColor);
            
            Raylib.DrawCircleLines((int)screenPos.X, (int)screenPos.Y, 25, outlineColor);
        });
    }
}
```

**Acceptance Criteria:**
- ✅ Selected entities show yellow outline
- ✅ Primary selection shows green fill
- ✅ Multi-select shows consistent highlighting
- ✅ Deselect clears highlights

**Estimated Effort:** 0.5 days

**Dependencies:** IG.3.1

---

### Task IG.3.3: Implement CreationTool - Entity Placement

**Goal:** Tool for spawning entities from TKB

**Steps:**
1. Create `CreationTool`:
   - Modes: Entity, Polyline, Polygon
2. Add TKB browser UI (ImGui):
   - Search box
   - Filtered list of entity types
   - Affiliation dropdown (Friend/Hostile/Neutral)
3. Entity mode:
   - Click to place
   - Show ghost icon at cursor
   - Send `CreateEntityRequest` to SimHost via `BdcCommandGateway`
4. Handle async response:
   - Success → log "Entity created with ID X"
   - Failure → show error message

**Implementation:**
```csharp
public class CreationTool : IMapTool
{
    public enum Mode { Entity, Polyline, Polygon }
    
    private Mode _mode;
    private long _selectedTkbType;
    private ForceId _affiliation;
    private readonly BdcCommandGateway _gateway;
    private readonly IGeographicTransform _geo;
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (_mode != Mode.Entity) return false;
        if (button != MouseButton.Left) return false;
        
        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            EntityId = 0, // Allocated by SimHost
            TkbType = _selectedTkbType,
            Position = _geo.ToGeodetic(worldPos),
            Affiliation = _affiliation
        };
        
        _ = _gateway.CreateEntityAsync(request).ContinueWith(task =>
        {
            if (task.Result.ErrorCode == 0)
                Console.WriteLine($"Entity created: {task.Result.NewEntityId}");
            else
                Console.WriteLine($"Failed: {task.Result.ErrorText}");
        });
        
        _canvas.PopTool(); // Return to standard tool
        return true;
    }
    
    public void Draw(RenderContext ctx)
    {
        if (_mode != Mode.Entity) return;
        
        var mousePos = _canvas.Input.GetMousePosition();
        var worldPos = ctx.Camera.ScreenToWorld(mousePos);
        
        // Draw ghost preview
        DrawEntityGhost(worldPos, ctx);
    }
    
    private void DrawEntityGhost(Vector2 worldPos, RenderContext ctx)
    {
        var screenPos = ctx.Camera.WorldToScreen(worldPos);
        
        // Ghosted icon (semi-transparent)
        Color ghostColor = GetAffiliationColor(_affiliation);
        ghostColor.A = 128;
        
        Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 15, ghostColor);
        Raylib.DrawText(_selectedTkbType.ToString(), screenPos + Vector2.UnitY * 20, 10, Color.White);
    }
}
```

**ImGui Panel:**
```csharp
public void DrawSpawnerPanel()
{
    if (!ImGui.Begin("Entity Spawner")) return;
    
    ImGui.InputText("Search", ref _searchText, 64);
    
    if (ImGui.BeginCombo("Type", _selectedTypeName))
    {
        foreach (var type in _filteredTypes)
        {
            bool selected = type.TkbType == _selectedTkbType;
            if (ImGui.Selectable(type.Name, selected))
            {
                _selectedTkbType = type.TkbType;
                _selectedTypeName = type.Name;
            }
        }
        ImGui.EndCombo();
    }
    
    int affil = (int)_affiliation;
    ImGui.Combo("Affiliation", ref affil, "Friend\0Hostile\0Neutral\0Unknown\0");
    _affiliation = (ForceId)affil;
    
    if (ImGui.Button("Activate Placement Tool"))
    {
        var tool = new CreationTool(_gateway, _geo, _selectedTkbType, _affiliation);
        _canvas.PushTool(tool);
    }
    
    ImGui.End();
}
```

**Acceptance Criteria:**
- ✅ UI panel shows TKB types
- ✅ Click "Activate" switches to CreationTool
- ✅ Cursor shows ghost icon
- ✅ Click sends CreateEntityRequest
- ✅ SimHost responds with success/failure
- ✅ Entity appears on map after creation

**Estimated Effort:** 1.5 days

**Dependencies:** IG.3.2, FDP.Toolkit.Commands (Shared)

---

### Task IG.3.4: Implement MeasureTool - Distance

**Goal:** Distance measurement tool

**Steps:**
1. Create `MeasureTool`:
   - Mode: Distance (future: LineOfSight)
2. Workflow:
   - Click to set start point
   - Hover shows preview line
   - Click to set end point
   - Log distance, return to standard tool
3. Draw overlay:
   - Line from start to end (cyan, 2px)
   - Text label at midpoint showing distance in meters

**Implementation:**
```csharp
public class MeasureTool : IMapTool
{
    private Vector2? _startPoint;
    private Vector2 _currentPoint;
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            if (_startPoint == null)
            {
                _startPoint = worldPos;
            }
            else
            {
                float distance = Vector2.Distance(_startPoint.Value, worldPos);
                Console.WriteLine($"Distance: {distance:F2} m");
                _canvas.PopTool();
            }
            return true;
        }
        else if (button == MouseButton.Right)
        {
            _canvas.PopTool(); // Cancel
            return true;
        }
        return false;
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
        var midpoint = (start + end) / 2;
        Raylib.DrawText(label, midpoint, 14, Color.White);
    }
}
```

**Acceptance Criteria:**
- ✅ Click sets start point
- ✅ Hover shows preview line
- ✅ Click sets end point, logs distance
- ✅ Right-click cancels
- ✅ Distance calculation accurate within 0.1m

**Estimated Effort:** 0.5 days

**Dependencies:** IG.3.3

---

### Task IG.3.5: Integration Test - Create Entity

**Goal:** End-to-end test of creation workflow

**Test Procedure:**
1. Launch SimHost + IG
2. Open Entity Spawner panel in IG
3. Select "Tank M1 Abrams"
4. Set affiliation "Hostile"
5. Click "Activate Placement Tool"
6. Click on map at (5500, 5500)
7. Verify:
   - IG sends CreateEntityRequest
   - SimHost responds with success
   - New entity appears on map within 1 second
   - Entity has hostile affiliation (red icon)

**Acceptance Criteria:**
- ✅ Full workflow completes successfully
- ✅ Entity ID allocated by SimHost
- ✅ Entity visible on IG within 1 second
- ✅ Affiliation color correct

**Estimated Effort:** 0.5 days (includes bug fixing)

**Dependencies:** IG.3.4, SimHost running

---

## Phase 4: Advanced Features (4 days)

### Task IG.4.1: Implement HistoryRecordingSystem

**Goal:** Record entity trails for visualization

**Steps:**
1. Create `HistoryTrail` component:
   ```csharp
   public struct HistoryTrail
   {
       public NativeList<Vector3> Points;
       public int MaxPoints;
       public float SampleInterval;
       public double LastSampleTime;
   }
   ```
2. Create `HistoryRecordingSystem`:
   - Phase: Simulation
   - Query: `With<SimTransform, ResolvedStyle, HistoryTrail>`
3. Logic:
   - If `ResolvedStyle.ShowTrail == true`:
     - Sample position every `SampleInterval` seconds
     - Add to `Points` list
     - Remove oldest if exceeds `MaxPoints`
4. Add `HistoryTrailRenderer` to `SstVisualizerAdapter`:
   - Draw polyline connecting points
   - Color: Cyan with 50% alpha
   - Line width: 2px

**Implementation:**
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
            if (elapsed < trail.SampleInterval) return;
            
            trail.Points.Add(transform.Position);
            
            if (trail.Points.Length > trail.MaxPoints)
                trail.Points.RemoveAt(0);
            
            trail.LastSampleTime = currentTime;
        });
    }
}
```

**HistoryTrailRenderer (in SstVisualizerAdapter):**
```csharp
private void DrawHistoryTrail(HistoryTrail trail, RenderContext ctx)
{
    if (trail.Points.Length < 2) return;
    
    for (int i = 0; i < trail.Points.Length - 1; i++)
    {
        var p1 = ctx.Camera.WorldToScreen(new Vector2(trail.Points[i].X, trail.Points[i].Y));
        var p2 = ctx.Camera.WorldToScreen(new Vector2(trail.Points[i+1].X, trail.Points[i+1].Y));
        
        Color trailColor = new Color(0, 255, 255, 128);
        Raylib.DrawLineEx(p1, p2, 2.0f, trailColor);
    }
}
```

**Unit Tests:**
1. EnableTrail → points recorded
2. DisableTrail → recording stops
3. MaxPoints limit enforced
4. SampleInterval respected

**Acceptance Criteria:**
- ✅ Entities with ShowTrail=true record trails
- ✅ Trails render as cyan polylines
- ✅ MaxPoints limit enforced (circular buffer)
- ✅ Sample interval prevents excessive sampling
- ✅ Unit tests pass

**Estimated Effort:** 1.0 day

**Dependencies:** IG.3.5

---

### Task IG.4.2: Implement EventToEffectSystem

**Goal:** Spawn visual effects from network events

**Steps:**
1. Register event types in ECS:
   - `FireInteractionEvent`
2. Create `VisualEffectState` component:
   ```csharp
   public struct VisualEffectState
   {
       public EffectType Type; // Explosion, Tracer
       public float Duration;
       public float ElapsedTime;
       public Color Color;
       public float Scale;
   }
   ```
3. Create `EventToEffectSystem`:
   - Phase: Simulation
   - Consume `FireInteractionEvent`
   - Spawn explosion entity at target
   - Spawn tracer entity from shooter to target
4. Create `VisualEffectCleanupSystem`:
   - Phase: Simulation
   - Query: `With<VisualEffectState>`
   - Delete entities where `ElapsedTime >= Duration`
5. Add `EffectRenderer` to render pipeline:
   - Explosion: Orange circle expanding over 2 seconds
   - Tracer: Yellow line fading over 0.3 seconds

**Implementation:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class EventToEffectSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var fireEvents = World.ConsumeEvents<FireInteractionEvent>();
        
        foreach (var evt in fireEvents)
        {
            // Spawn explosion
            var explosion = World.CreateEntity();
            // ⚠️ Phase 0 Adaptation: Use World.AddComponent (not SetComponent) since the entity is newly created.
            World.SetComponent(explosion, new SimTransform { Position = evt.TargetPosition });
            World.SetComponent(explosion, new VisualEffectState
            {
                Type = EffectType.Explosion,
                Duration = 2.0f,
                ElapsedTime = 0,
                Color = Color.Orange,
                Scale = 5.0f
            });
            
            // Spawn tracer
            var tracer = World.CreateEntity();
            // ⚠️ Phase 0 Adaptation: Use World.AddComponent (not SetComponent) since the entity is newly created.
            World.SetComponent(tracer, new SimTransform { Position = evt.ShooterPosition });
            World.SetComponent(tracer, new TracerTarget { EndPos = evt.TargetPosition });
            World.SetComponent(tracer, new VisualEffectState
            {
                Type = EffectType.Tracer,
                Duration = 0.3f,
                ElapsedTime = 0,
                Color = Color.Yellow,
                Scale = 1.0f
            });
        }
    }
}

[UpdateInPhase(SystemPhase.Simulation)]
public class VisualEffectCleanupSystem : ComponentSystem
{
    protected override void OnUpdate(float dt)
    {
        Entities.With<VisualEffectState>().ForEach((entity, ref effect) =>
        {
            effect.ElapsedTime += dt;
            
            if (effect.ElapsedTime >= effect.Duration)
                World.DeleteEntity(entity);
        });
    }
}
```

**EffectRenderer:**
```csharp
public void RenderEffects(RenderContext ctx)
{
    // SimTransform from Fdp.Kernel — query and field access are already correct.
    Entities.With<VisualEffectState, SimTransform>().ForEach((entity, ref effect, ref transform) =>
    {
        var screenPos = ctx.Camera.WorldToScreen(new Vector2(transform.Position.X, transform.Position.Y));
        
        float alpha = 1.0f - (effect.ElapsedTime / effect.Duration);
        Color fadeColor = new Color(effect.Color.R, effect.Color.G, effect.Color.B, (byte)(255 * alpha));
        
        if (effect.Type == EffectType.Explosion)
        {
            float radius = effect.Scale * (1.0f + effect.ElapsedTime / effect.Duration);
            Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, (int)radius, fadeColor);
        }
        else if (effect.Type == EffectType.Tracer)
        {
            var tracerTarget = World.GetComponent<TracerTarget>(entity);
            var endScreen = ctx.Camera.WorldToScreen(new Vector2(tracerTarget.EndPos.X, tracerTarget.EndPos.Y));
            Raylib.DrawLineEx(screenPos, endScreen, 2.0f, fadeColor);
        }
    });
}
```

**Acceptance Criteria:**
- ✅ FireInteractionEvent spawns explosion + tracer
- ✅ Explosion expands over 2 seconds, fades out
- ✅ Tracer draws line, fades over 0.3 seconds
- ✅ Effects auto-delete after duration
- ✅ No memory leaks (verify with profiler)

**Estimated Effort:** 1.5 days

**Dependencies:** IG.4.1

---

### Task IG.4.3: Add Context Menu System

**Goal:** Display IOS-driven right-click menus

**Steps:**
1. Listen for `ContextActionsUpdate` events from IOS
2. Store menu data in `ContextMenuState` managed component
3. Implement right-click detection in `StandardInteractionTool`
4. Render ImGui popup menu
5. On menu item click:
   - If action starts with "IG_": execute locally (e.g., IG_Lock_Camera)
   - Else: send `ContextActionTriggered` event to IOS

**Implementation:**
```csharp
public class ContextMenuSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var updates = World.ConsumeEvents<ContextActionsUpdate>();
        
        foreach (var update in updates)
        {
            if (_entityMap.TryGetEntity(update.EntityId, out var entity))
            {
                World.SetManagedComponent(entity, new ContextMenuState
                {
                    Actions = update.Actions.ToList(),
                    LastUpdateTime = World.GetSingleton<GlobalTime>().TotalTime
                });
            }
        }
    }
}
```

**ImGui Rendering:**
```csharp
public void HandleRightClick(Entity entity)
{
    if (!World.TryGetManagedComponent<ContextMenuState>(entity, out var menu))
        return;
    
    ImGui.OpenPopup($"ContextMenu_{entity.Index}");
    
    if (ImGui.BeginPopup($"ContextMenu_{entity.Index}"))
    {
        foreach (var action in menu.Actions)
        {
            if (ImGui.MenuItem(action.Label))
            {
                ExecuteAction(entity, action);
            }
        }
        ImGui.EndPopup();
    }
}

private void ExecuteAction(Entity entity, ContextAction action)
{
    if (action.ActionName.StartsWith("IG_"))
    {
        // Local action
        switch (action.ActionName)
        {
            case "IG_Lock_Camera":
                _cameraLockTarget = entity;
                break;
            case "IG_Center":
                CenterCameraOn(entity);
                break;
        }
    }
    else
    {
        // Remote action: send to IOS
        var evt = new ContextActionTriggered
        {
            EntityId = GetNetworkId(entity),
            ActionName = action.ActionName
        };
        _eventBus.Publish(evt);
    }
}
```

**Acceptance Criteria:**
- ✅ Right-click on entity opens context menu
- ✅ Menu items from IOS displayed
- ✅ Local actions (IG_*) execute immediately
- ✅ Remote actions send events to IOS
- ✅ Menu closes after selection

**Estimated Effort:** 1.0 day

**Dependencies:** IG.4.2

---

### Task IG.4.4: Implement EditTool - Vertex Manipulation

**Goal:** Edit overlay geometry (areas, lines)

**Steps:**
1. Create `EditTool`:
   - Initialize with target overlay entity
   - Load points from `MapVisualOverlay` descriptor
   - Convert to Cartesian for editing
2. Workflow:
   - Display vertex handles (white circles, 5px radius)
   - Click handle to select (red highlight)
   - Drag to move vertex
   - Ctrl+Click on segment to insert vertex
   - Alt+Click on vertex to delete
   - Right-click to commit changes
3. Commit:
   - Convert points back to geodetic
   - Send `UpdateEntityDescriptorRequest` with new overlay

**Implementation:**
```csharp
public class EditTool : IMapTool
{
    private readonly Entity _targetOverlay;
    private List<Vector2> _ghostPoints;
    private int _selectedVertexIndex = -1;
    private readonly IGeographicTransform _geo;
    private readonly BdcCommandGateway _gateway;
    
    public void OnEnter(MapCanvas canvas)
    {
        var overlay = World.GetManagedComponent<MapVisualOverlay>(_targetOverlay);
        _ghostPoints = overlay.Points.Select(p => _geo.ToCartesian(p)).ToList();
    }
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _selectedVertexIndex = FindNearestVertex(worldPos, threshold: 15);
            return true;
        }
        else if (button == MouseButton.Right)
        {
            CommitChanges();
            _canvas.PopTool();
            return true;
        }
        return false;
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
        DrawPolyline(_ghostPoints, ctx, Color.Yellow);
        
        // Draw vertex handles
        for (int i = 0; i < _ghostPoints.Count; i++)
        {
            var screenPos = ctx.Camera.WorldToScreen(_ghostPoints[i]);
            Color color = i == _selectedVertexIndex ? Color.Red : Color.White;
            Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 5, color);
        }
    }
    
    private int FindNearestVertex(Vector2 worldPos, float threshold)
    {
        float minDist = threshold;
        int nearestIndex = -1;
        
        for (int i = 0; i < _ghostPoints.Count; i++)
        {
            float dist = Vector2.Distance(_ghostPoints[i], worldPos);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = i;
            }
        }
        
        return nearestIndex;
    }
    
    private async void CommitChanges()
    {
        var overlay = World.GetManagedComponent<MapVisualOverlay>(_targetOverlay);
        overlay.Points = _ghostPoints.Select(p => _geo.ToGeodetic(p)).ToList();
        
        var request = new UpdateEntityDescriptorRequest
        {
            EntityId = GetNetworkId(_targetOverlay),
            DescriptorType = EDescriptorType.dtMapVisualOverlay,
            CurrentVersion = overlay.Version,
            Payload = overlay
        };
        
        var ack = await _gateway.UpdateDescriptorAsync(request);
        if (ack.ErrorCode != 0)
            Console.WriteLine($"Edit failed: {ack.ErrorText}");
    }
}
```

**Acceptance Criteria:**
- ✅ Vertex handles visible
- ✅ Click selects vertex (red highlight)
- ✅ Drag moves vertex
- ✅ Right-click commits changes
- ✅ SimHost receives update request
- ✅ Overlay updates across all clients

**Estimated Effort:** 1.5 days (includes vertex insert/delete)

**Dependencies:** IG.4.3

---

### Task IG.4.5: Integration Test - Advanced Features

**Goal:** End-to-end test of history trails, effects, editing

**Test Procedure:**
1. Launch SimHost + IG
2. Spawn tank entity
3. Send mission to tank (move 500m)
4. Enable history trail via IOS (MapEntitySymbol.ShowHistory = true)
5. Verify trail renders
6. Trigger combat event (SimHost publishes FireInteractionEvent)
7. Verify explosion + tracer appear
8. Create overlay (polyline)
9. Right-click overlay, select "Edit"
10. Move vertices, commit
11. Verify overlay updates

**Acceptance Criteria:**
- ✅ History trail renders for moving entity
- ✅ Explosion appears on fire event
- ✅ Tracer draws from shooter to target
- ✅ Overlay editing works
- ✅ Changes propagate across network

**Estimated Effort:** 1.5 days (includes debugging)

**Dependencies:** IG.4.4

---

## Phase 5: UI & Polish (2 days)

### Task IG.5.1: Create Debug Panel - Time Control & Recording

**Goal:** ImGui panel for time control and recording

**Steps:**
1. Create `IgDebugPanel`:
   - Time info: Sim time, FPS, time scale
   - Time controls: Pause, Play, Step (if master mode)
   - Recording: File path input, Record/Stop buttons, status
   - Playback: Load file, scrub slider, Resume Live button
2. Integrate with `SlaveTimeController`:
   - Display master time
   - Show time scale from TimePulse
3. Add recording support:
   - `AsyncRecorder` for live session
   - `PlaybackController` for replay

**Implementation:**
```csharp
public void DrawDebugPanel()
{
    if (!ImGui.Begin("Debug Panel")) return;
    
    // Time info
    var time = _world.GetSingleton<GlobalTime>();
    ImGui.Text($"Sim Time: {TimeSpan.FromSeconds(time.TotalTime):hh\\:mm\\:ss\\.ff}");
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.Text($"Time Scale: {_timeController.TimeScale:F2}x");
    
    ImGui.Separator();
    
    // Recording
    ImGui.InputText("File", ref _recordingPath, 256);
    
    if (_recorder == null)
    {
        if (ImGui.Button("Record"))
        {
            _recorder = new AsyncRecorder(_recordingPath);
        }
    }
    else
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1, 0, 0, 1));
        if (ImGui.Button("Stop Recording"))
        {
            _recorder.Dispose();
            _recorder = null;
        }
        ImGui.PopStyleColor();
        ImGui.Text($"Recording: {_recorder.FramesRecorded} frames");
    }
    
    ImGui.Separator();
    
    // Playback
    if (_playback != null)
    {
        int frame = _playback.CurrentFrame;
        if (ImGui.SliderInt("Frame", ref frame, 0, _playback.TotalFrames - 1))
        {
            _playback.SeekToFrame(_world, frame);
        }
        
        if (ImGui.Button("< Prev"))
            _playback.StepBackward(_world);
        ImGui.SameLine();
        if (ImGui.Button("Next >"))
            _playback.StepForward(_world);
        
        ImGui.Separator();
        if (ImGui.Button("Resume Live Simulation"))
        {
            var replayTime = _playback.CurrentTime;
            _playback.Dispose();
            _playback = null;
            _timeController.SeedState(new GlobalTime { TotalTime = replayTime.TotalTime });
        }
    }
    else
    {
        if (ImGui.Button("Load Replay"))
        {
            _playback = new PlaybackController(_recordingPath);
        }
    }
    
    ImGui.End();
}
```

**Acceptance Criteria:**
- ✅ Panel shows sim time, FPS, time scale
- ✅ Record button starts recording
- ✅ Stop button saves file
- ✅ Load replay opens playback controller
- ✅ Scrub slider seeks frames
- ✅ Resume Live restores live simulation

**Estimated Effort:** 1.0 day

**Dependencies:** IG.4.5

---

### Task IG.5.2: Add Entity Inspector Panel

**Goal:** Show detailed entity properties

**Steps:**
1. Create `EntityInspectorPanel`:
   - Show selected entity info
   - EntityMaster: TkbType, DisType
   - `SimTransform`: Position, Heading (from `Fdp.Kernel` — do not redefine locally)
   - ResolvedStyle: Texture, Affiliation, Damage
   - EntityInfo: Name, Commander, Subordinates
   - EntityMission: Active task, mission plan
2. Add edit capabilities:
   - Change name (inline text edit)
   - Change affiliation (dropdown)
   - Teleport (button → location picker)

**Implementation:**
```csharp
public void DrawEntityInspector()
{
    if (!ImGui.Begin("Entity Inspector")) return;
    
    var selected = GetFirstSelectedEntity();
    if (selected == Entity.Null)
    {
        ImGui.Text("No entity selected");
        ImGui.End();
        return;
    }
    
    // EntityMaster
    if (_world.TryGetComponent<EntityMasterComponent>(selected, out var master))
    {
        ImGui.Text($"TKB Type: {master.TkbType}");
        ImGui.Text($"DIS Type: {master.DisType}");
    }
    
    // SimTransform (Fdp.Kernel)
    if (_world.TryGetComponent<SimTransform>(selected, out var transform))
    {
        var geo = _geoTransform.ToGeodetic(transform.Position);
        ImGui.Text($"Position:");
        ImGui.Text($"  Lat: {geo.Latitude:F6}");
        ImGui.Text($"  Lon: {geo.Longitude:F6}");
        ImGui.Text($"  Alt: {geo.Altitude:F2} m");
    }
    
    // ResolvedStyle
    if (_world.TryGetComponent<ResolvedStyle>(selected, out var style))
    {
        ImGui.Text($"Affiliation: {style.Affiliation}");
        ImGui.Text($"Damage: {style.DamageLevel:F1}%");
    }
    
    // EntityInfo
    if (_world.TryGetManagedComponent<EntityInfo>(selected, out var info))
    {
        ImGui.InputText("Name", ref info.Name, 64);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            // Send update request
            UpdateEntityInfo(selected, info);
        }
    }
    
    ImGui.Separator();
    
    if (ImGui.Button("Teleport"))
    {
        var tool = new LocationPickerTool((newPos) =>
        {
            TeleportEntity(selected, newPos);
        });
        _canvas.PushTool(tool);
    }
    
    ImGui.End();
}
```

**Acceptance Criteria:**
- ✅ Panel shows entity properties
- ✅ Name edit sends update request
- ✅ Teleport button activates location picker
- ✅ Panel updates when selection changes

**Estimated Effort:** 0.5 days

**Dependencies:** IG.5.1

---

### Task IG.5.3: Add Mini-IOS Panel - Spawner & TKB Browser

**Goal:** Lite version of IOS controls for standalone IG

**Steps:**
1. Create `MiniIosPanel`:
   - TKB Browser (searchable list)
   - Affiliation dropdown
   - "Activate Placement Tool" button
   - Mission presets (Move, Patrol, Wait)
2. Reuse CreationTool from IG.3.3

**Implementation:**
```csharp
public void DrawMiniIosPanel()
{
    if (!ImGui.Begin("Mini IOS")) return;
    
    ImGui.Text("Entity Spawner");
    ImGui.Separator();
    
    ImGui.InputText("Search", ref _searchText, 64);
    
    if (ImGui.BeginCombo("Type", _selectedTypeName))
    {
        foreach (var type in GetFilteredTypes(_searchText))
        {
            if (ImGui.Selectable(type.Name, type.TkbType == _selectedTkbType))
            {
                _selectedTkbType = type.TkbType;
                _selectedTypeName = type.Name;
            }
        }
        ImGui.EndCombo();
    }
    
    int affil = (int)_affiliation;
    ImGui.Combo("Affiliation", ref affil, "Friend\0Hostile\0Neutral\0Unknown\0");
    _affiliation = (ForceId)affil;
    
    if (ImGui.Button("Activate Placement Tool"))
    {
        var tool = new CreationTool(_gateway, _geo, _selectedTkbType, _affiliation);
        tool.Mode = CreationTool.CreationMode.Entity;
        _canvas.PushTool(tool);
    }
    
    ImGui.Separator();
    ImGui.Text("Quick Actions");
    
    if (ImGui.Button("Spawn 10 Tanks"))
    {
        SpawnMultipleEntities(_selectedTkbType, 10, _affiliation);
    }
    
    ImGui.End();
}
```

**Acceptance Criteria:**
- ✅ Panel shows TKB browser
- ✅ Search filters types
- ✅ Placement tool activated on button click
- ✅ Quick spawn creates multiple entities

**Estimated Effort:** 0.5 days

**Dependencies:** IG.5.2

---

### Task IG.5.4: Add Performance Metrics Overlay

**Goal:** Real-time performance monitoring

**Steps:**
1. Create `PerformanceOverlay`:
   - FPS (current, min, max, avg)
   - Frame time (ms)
   - Entity count
   - Visible entity count (after culling)
   - Network stats (packets/sec, bytes/sec)
   - Memory usage (managed heap)
2. Render as transparent overlay (top-right corner)
3. Toggle with F3 key

**Implementation:**
```csharp
public void DrawPerformanceOverlay()
{
    if (!_showPerformance) return;
    
    ImGui.SetNextWindowPos(new Vector2(Raylib.GetScreenWidth() - 250, 10));
    ImGui.SetNextWindowSize(new Vector2(240, 200));
    ImGui.SetNextWindowBgAlpha(0.8f);
    
    ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | 
                              ImGuiWindowFlags.NoMove | 
                              ImGuiWindowFlags.NoSavedSettings;
    
    if (!ImGui.Begin("Performance", flags)) return;
    
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.Text($"Frame Time: {_frameTime:F2} ms");
    ImGui.Text($"Entities: {_world.EntityCount}");
    ImGui.Text($"Visible: {_visibleEntityCount}");
    ImGui.Text($"Network:");
    ImGui.Text($"  Packets/s: {_networkStats.PacketsPerSecond}");
    ImGui.Text($"  Bytes/s: {_networkStats.BytesPerSecond}");
    ImGui.Text($"Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
    
    ImGui.End();
}
```

**Acceptance Criteria:**
- ✅ Overlay shows FPS, frame time
- ✅ Entity counts accurate
- ✅ Network stats update in real-time
- ✅ F3 toggles visibility

**Estimated Effort:** 0.5 days

**Dependencies:** IG.5.3

---

## Summary

**Total Tasks:** 23  
**Total Effort:** 14 developer-days (~3 weeks)

**Critical Path:**
1. Core Infrastructure (2d)
2. Basic Rendering (3d)
3. Interaction Tools (3d)
4. Advanced Features (4d)
5. UI & Polish (2d)

**Risk Mitigation:**
- Phase 1-2 rely heavily on existing infrastructure (low risk)
- Phase 3-4 involve new tools and systems (medium risk)
- Phase 5 is polish and can be deferred if schedule slips

**Next Steps:**
1. Complete Shared components (Bagira.DDS.DataModel, FDP.Toolkit.Commands)
2. Start Phase 1 implementation
3. Run integration tests after each phase
