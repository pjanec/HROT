# IOS Mock Design

**Version:** 1.0  
**Date:** 2026-02-14  
**Status:** Ready for Implementation

**⚠️ INFRASTRUCTURE AUDIT:** This document reflects comprehensive audit of existing FDP infrastructure. Components marked ✅ EXIST, components marked ❌ require NEW implementation.

**Parent Document**: [Overall Design](./DESIGN-OVERALL.md)

## Table of Contents

1. [Infrastructure Status Matrix](#1-infrastructure-status-matrix)
2. [Overview](#2-overview)
3. [Existing Infrastructure (Reuse)](#3-existing-infrastructure-reuse)
4. [New Components (Implement)](#4-new-components-implement)
5. [Architecture Design](#5-architecture-design)
6. [UI Layout](#6-ui-layout)
7. [Implementation Plan](#7-implementation-plan)

---

## 1. Infrastructure Status Matrix

| Component | Status | Location | Purpose |
|-----------|--------|----------|---------|
| **CycloneDDS C# API** | ✅ EXISTS | `ModuleHost.Network.Cyclone` | Raw DDS readers/writers, QoS management |
| **Data Model** | ✅ EXISTS | `Bagira.DDS.DataModel` | All DDS structs from IDL (EntityMaster, GeoSpatial, etc.) |
| **TKB Service** | ✅ EXISTS | `FDP.Toolkit.Tkb.TkbDatabase` | Entity type definitions |
| **Commands Toolkit** | ✅ EXISTS (Shared) | `FDP.Toolkit.Commands.*` | RPC-over-DDS with correlation IDs |
| **DER (Dynamic Entity Repository)** | ✅ EXISTS (Shared P3) | `FDP.Toolkit.DER.*` | Non-ECS entity access (dictionary-based) |
| **Mission Editor Service** | ❌ NEW | `Bagira.IOS.Services.MissionEditorService` | Optimistic locking for mission editing |
| **Context Menu Logic** | ❌ NEW | `Bagira.IOS.Logic.ContextMenuLogic` | Push-based menu generation |
| **Transaction Manager** | ❌ NEW | `Bagira.IOS.Services.RequestTransactionManager` | Track request/ack correlation |
| **IOS UI Panels** | ❌ NEW | `Bagira.IOS.Panels.*` | ImGui panels for all IOS functions |
| **IOS Application** | ❌ NEW | `Bagira.IOS.Program` | Main app shell, configuration |

**Key Insight**: Raw DDS API, data model, and DER toolkit **FULLY EXIST** (see SHARED components). IOS is pure DDS client (no ECS). Focus on IOS services, UI panels, and DDS integration.

---

## 2. Overview

### 2.1 Purpose

IOS Mock is the **"Command & Control Dashboard"** for the simulation. It:
- **Commands the Map**: Controls IG configuration (layers, tools, styling)
- **Manages Entities**: Creates, edits, and commands simulation entities
- **Plans Missions**: Edits mission plans and sends control commands
- **Visualizes ORBAT**: Shows hierarchical organization chart
- **Provides Context Menus**: Pushes menu definitions to IG proactively
- **Validates Protocol**: Acts as "black box" to prove BDC SST compliance

### 2.2 Design Philosophy: "The Brain"

**Critical Architectural Decision**: IOS is **NOT** based on FDP ECS.

**Rationale:**
1. **Real-world IOS** will likely be legacy C++/Qt/Java system, not FDP-based
2. **Protocol Validation**: Proves that BDC SST works with external systems
3. **Simplicity**: Avoids overhead of full simulation engine for control-only interface
4. **Lightweight**: Uses raw CycloneDDS C# API directly

**Technology:**
- **DDS Access**: `ModuleHost.Network.Cyclone` (DdsReader/DdsWriter)
- **Entity Access**: `FDP.Toolkit.DER` (NEW - Dynamic Entity Repository)
- **UI Framework**: ImGui.NET (via rlImGui)
- **Data Management**: Dictionary-based caches, no ECS
- **JSON**: Newtonsoft.Json for configuration merge patching

### 2.3 Dependencies

**Critical Shared Components** (must be completed first):
- ✅ `Bagira.DDS.DataModel` - DDS types (SHARED Phase P2)
- ✅ `FDP.Toolkit.Commands` - RPC framework (SHARED Phase P4)
- ✅ `Bagira.Map.Definitions` - TKB descriptors (SHARED Phase P5)
- ✅ `FDP.Toolkit.DER` - Non-ECS entity repository (SHARED Phase P3)

---

## 3. Existing Infrastructure (Reuse)

### 3.1 CycloneDDS C# API

**✅ VERIFIED EXISTS** - Production-ready DDS bindings

**Location:** `ModuleHost.Network.Cyclone`

**Key Classes:**
```csharp
// DDS Participant (Domain connection)
public class DdsParticipant : IDisposable
{
    public DdsParticipant(int domainId);
}

// DDS Reader (Subscription)
public class DdsReader<T> : IDisposable where T : class
{
    public DdsReader(DdsParticipant participant, string topicName);
    public LoanedSamples<T> Take(); // Zero-copy reading
    public LoanedSamples<T> Read(); // Non-destructive read
}

// DDS Writer (Publication)
public class DdsWriter<T> : IDisposable where T : class
{
    public DdsWriter(DdsParticipant participant, string topicName);
    public void Write(T data);
    public void Dispose(T data); // Dispose instance (lifecycle)
}
```

**IOS Usage Pattern:**
```csharp
// Initialize
var participant = new DdsParticipant(domainId: 0);
var configWriter = new DdsWriter<MapInteractionConfig>(participant, "MapInteractionConfig");
var clickReader = new DdsReader<MapClickEvent>(participant, "MapClickEvent");

// In Update Loop
using var samples = clickReader.Take();
foreach (var sample in samples)
{
    if (sample.IsValid)
        ProcessClick(sample.Data);
}

// Send Command
configWriter.Write(new MapInteractionConfig { ... });
```

### 3.2 Data Model (BDC SST Structs)

**✅ VERIFIED EXISTS** - Complete IDL → C# structs

**Location:** `Bagira.DDS.DataModel`

**Key Topics for IOS:**

**Input (Subscribe):**
- `MapClickEvent` - User clicked on map
- `SelectionChangedEvent` - Selection changed on IG
- `DragEvent` - User dragging entity/overlay
- `ContextActionInvoked` - User clicked context menu item
- `MapConfigStatus` - IG reporting current config state
- `EntityMaster` - Entity lifecycle
- `EntityInfo` - Entity metadata, ORBAT hierarchy
- `EntityMission` - Mission plans and execution state
- `GeoSpatial` / `GeoSpatialDR` - Entity position/orientation
- `MapVisualOverlay` - Tactical graphics/overlays
- `MapEntitySymbol` - Visual overrides
- `CreateEntityAck` - Response to entity creation
- `UpdateEntityDescriptorAck` - Response to updates

**Output (Publish):**
- `MapInteractionConfig` - Configure IG behavior
- `ContextActionsUpdate` - Push context menu definitions
- `MapCommandRequest` - Imperative commands (pan, zoom)
- `CreateEntityRequest` - Request entity creation
- `UpdateEntityDescriptorRequest` - Request entity updates
- `MissionControlRequest` - Mission control commands

### 3.3 TKB Service

**✅ VERIFIED EXISTS** - Entity type database

**Location:** `FDP.Toolkit.Tkb.TkbDatabase`

**IOS Usage:**
- Populate entity picker/spawner UI
- Display entity type names and metadata
- Filter by category for UI organization

```csharp
var tkb = new TkbDatabase();
var allTypes = tkb.GetAll();

// UI: Entity Picker
foreach (var def in allTypes)
{
    var master = def.GetDescriptor<TkbMasterDef>();
    if (ImGui.Selectable(master.Name))
        selectedType = def.TkbId;
}
```

### 3.4 Commands Toolkit

**✅ VERIFIED EXISTS (Shared P4)** - Async RPC framework

**Location:** `FDP.Toolkit.Commands.*`

**Already Designed for Shared Components** - see DESIGN-SHARED.md

### 3.5 DER Toolkit

**✅ VERIFIED EXISTS (Shared P3)** - Non-ECS entity repository

**Location:** `FDP.Toolkit.DER.*`

**Already Designed for Shared Components** - see DESIGN-SHARED.md and TASK-DETAILS-SHARED.md Phase P3

**IOS Usage Pattern:**
```csharp
// Create repository
var repo = new DerRepo();

// Listen for entity lifecycle
repo.EntityCreated += (entity) => Console.WriteLine($"Entity {entity.EntityId} created");
repo.EntityDeleted += (entity) => Console.WriteLine($"Entity {entity.EntityId} deleted");

// Query entities
var entity = repo.GetEntity(entityId);
var allEntities = repo.GetAllEntities();

// Work with descriptors
var geoDesc = entity.GetDescriptor<GeoSpatialDescriptor>();
if (geoDesc != null)
{
    Console.WriteLine($"Position: {geoDesc.Position}");
}
```

**IOS Usage Pattern:**
```csharp
// High-level async API
var response = await commandGateway.CreateEntityAsync(new CreateEntityRequest
{
    TkbType = 100,
    Position = new GeoPosition { Latitude = 45.0, Longitude = 14.0 },
    Owner = simHostNodeId
});

if (response.ErrorCode == 0)
    Console.WriteLine($"Created entity {response.NewEntityId}");
```

---

## 4. New Components (Implement)

### 4.1 DDS-to-DER Integration Layer

**❌ NEW** - IOS-specific DDS ingress/egress

**Purpose**: Translate between DDS topics and DER descriptors for IOS

**Rationale**: DER toolkit (from SHARED P3) is generic. IOS needs translators to map BDC SST topics ↔ DER descriptors.

**Architecture:**

```
┌─────────────────────────────────────────────────────────┐
│ IOS Application (ImGui Panels)                          │
│ ┌─────────────┐  ┌──────────────┐  ┌─────────────────┐ │
│ │ ORBAT Panel │  │ Mission Panel│  │ Selection Panel │ │
│ └──────┬──────┘  └──────┬───────┘  └────────┬────────┘ │
│        │                 │                    │          │
│        └─────────────────┴────────────────────┘          │
│                          │                               │
│                    ┌─────▼─────┐                         │
│                    │ IosLogic  │                         │
│                    └─────┬─────┘                         │
│                          │                               │
│        ┌─────────────────┴─────────────────┐            │
│        │   DerRepo (from SHARED P3)         │            │
│        └─────────────────┬─────────────────┘            │
│                          ▲                               │
│                          │                               │
│        ┌─────────────────┴─────────────────┐            │
│        │   DDS Translators (IOS-specific)   │            │
│        │  - EntityMasterTranslator          │            │
│        │  - GeoSpatialTranslator            │            │
│        │  - EntityInfoTranslator            │            │
│        │  - EntityMissionTranslator         │            │
│        └─────────────────┬─────────────────┘            │
│                          │                               │
└──────────────────────────┼───────────────────────────────┘
                           │
        ┌──────────────────┴──────────────────┐
        │         CycloneDDS Layer            │
        │  ┌─────────────┐  ┌──────────────┐ │
        │  │ DdsReader<T>│  │ DdsWriter<T> │ │
        │  └─────────────┘  └──────────────┘ │
        └─────────────────────────────────────┘
```

**DER Descriptors for BDC SST:**

```csharp
// IOS defines custom descriptors for DDS types
namespace Bagira.IOS.Descriptors
{
    /// <summary>
    /// Main repository for entity access
    /// </summary>
    public interface IDerRepo : IDisposable
    {
        // Lifecycle
        void Poll();  // Read network, update cache
        void Flush(); // Write dirty changes to network
        
        // Entity Access
        IDerEntity? GetEntity(long entityId);
        IEnumerable<IDerEntity> GetAllEntities();
        
        // Factory
        IDerEntity CreateLocalEntity(long entityId, long tkbType);
        
        // Events
        event Action<IDerEntity> EntityDiscovered;
        event Action<IDerEntity> EntityLost;
        
        // Identity
        int LocalNodeId { get; }
    }
    
    /// <summary>
    /// Represents a single entity
    /// </summary>
    public interface IDerEntity
    {
        long EntityId { get; }
        bool IsOwned { get; }
        
        // Descriptor Access
        IDerDescriptor<T>? GetDescriptor<T>() where T : class, new();
        IDerDescriptor<T> GetOrCreateDescriptor<T>() where T : class, new();
        bool HasDescriptor<T>();
    }
    
    /// <summary>
    /// Wrapper for a single descriptor
    /// </summary>
    public interface IDerDescriptor<T> where T : class
    {
        T Data { get; set; }
        bool IsOwned { get; }
        bool IsValid { get; } // Have we received data?
        
        void Write(); // Mark dirty
        void ApplyJsonPatch(string jsonPatch); // For config merging
        
        event Action<T> Updated;
    }
}
```

**DDS Translators:**

```csharp
// EntityInfo DDS → DER Translator
public class EntityInfoTranslator
{
    private readonly IDerRepo _repo;
    private readonly DdsReader<EntityInfo> _reader;
    
    public void Poll()
    {
        using var samples = _reader.Take();
        foreach (var sample in samples)
        {
            if (!sample.IsValid) continue;
            
            var entity = _repo.GetEntity(sample.Data.EntityId);
            if (entity == null) continue;
            
            // Map DDS type → DER descriptor
            entity.SetDescriptor(new EntityInfoDescriptor
            {
                EntityId = sample.Data.EntityId,
                Name = sample.Data.Name,
                CommanderId = sample.Data.CommanderId,
                ForceId = sample.Data.ForceId
            });
        }
    }
}

// GeoSpatial DDS → DER Translator  
public class GeoSpatialTranslator
{
    private readonly IDerRepo _repo;
    private readonly DdsReader<GeoSpatial> _reader;
    
    public void Poll()
    {
        using var samples = _reader.Take();
        foreach (var sample in samples)
        {
            if (!sample.IsValid) continue;
            
            var entity = _repo.GetEntity(sample.Data.EntityId);
            if (entity == null) continue;
            
            entity.SetDescriptor(new GeoSpatialDescriptor
            {
                EntityId = sample.Data.EntityId,
                Position = sample.Data.Pos,
                Orientation = sample.Data.Orient
            });
        }
    }
}

// EntityMaster DDS → DER Lifecycle Translator
pub class EntityMasterTranslator
{
    private readonly IDerRepo _repo;
    private readonly DdsReader<EntityMaster> _reader;
    
    public void Poll()
    {
        using var samples = _reader.Take();
        foreach (var sample in samples)
        {
            if (sample.Info.InstanceState == InstanceState.Disposed)
            {
                _repo.DeleteEntity(sample.Data.EntityId);
            }
            else if (sample.IsValid)
            {
                if (_repo.GetEntity(sample.Data.EntityId) == null)
                {
                    _repo.CreateEntity(sample.Data.EntityId, sample.Data.TkbType);
                }
            }
        }
    }
}
```

### 4.2 JSON Merge Patch Helper

**❌ NEW** - RFC 7396 merge patch for `MapInteractionConfig`

**Purpose**: Apply partial JSON updates to config

**Implementation:**

```csharp
public static class JsonMergePatch
{
    public static void ApplyPatch<T>(T target, string jsonPatch)
    {
        var jObject = JObject.FromObject(target);
        var jPatch = JObject.Parse(jsonPatch);
        
        jObject.Merge(jPatch, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });
        
        JsonConvert.PopulateObject(jObject.ToString(), target);
    }
}
```

### 4.3 Mission Editor Service

**❌ NEW** - Handles optimistic locking for mission editing

**Purpose**: Manage single-view mission editing with conflict detection

**Interface:**

```csharp
namespace Bagira.IOS.Services
{
    public interface IMissionEditorService
    {
        /// <summary>
        /// Get current mission snapshot and version
        /// </summary>
        (MissionPlan Plan, long Version) GetMissionSnapshot(long entityId);
        
        /// <summary>
        /// Attempt to commit mission changes
        /// </summary>
        Task<MissionCommitResult> CommitMissionAsync(
            long entityId, 
            MissionPlan newPlan, 
            long baseVersion);
        
        /// <summary>
        /// Send jump/abort command (no version check)
        /// </summary>
        void SendControlCommand(
            long entityId, 
            eMissionCommandType type, 
            Guid taskId);
    }
    
    public class MissionCommitResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public long NewVersion { get; set; }
    }
}
```

**Implementation:**

```csharp
public class MissionEditorService : IMissionEditorService
{
    private readonly IDerRepo _repo;
    private readonly DdsWriter<MissionControlRequest> _requestWriter;
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _pendingCommits = new();
    
    public (MissionPlan, long) GetMissionSnapshot(long entityId)
    {
        var entity = _repo.GetEntity(entityId);
        if (entity == null) return (null, 0);
        
        var mission = entity.GetDescriptor<EntityMission>();
        var lock = entity.GetDescriptor<DescriptorOptimisticLock>();
        
        return (mission?.Data.Plan, lock?.GetVersion(DescriptorType.Mission) ?? 0);
    }
    
    public async Task<MissionCommitResult> CommitMissionAsync(
        long entityId, 
        MissionPlan newPlan, 
        long baseVersion)
    {
        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<bool>();
        _pendingCommits[requestId] = tcs;
        
        // Send request
        _requestWriter.Write(new MissionControlRequest
        {
            RequestId = requestId,
            TargetEntityId = entityId,
            BaseVersion = baseVersion,
            Payload = new MissionCommandPayload
            {
                Type = CMD_REPLACE_MISSION,
                FullMissionData = newPlan
            }
        });
        
        // Wait for ack (with timeout)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await tcs.Task.WaitAsync(cts.Token);
            return new MissionCommitResult { Success = true };
        }
        catch (TimeoutException)
        {
            return new MissionCommitResult 
            { 
                Success = false, 
                ErrorMessage = "Timeout waiting for response" 
            };
        }
    }
    
    // Called from main Poll() loop when MissionControlAck received
    public void OnAckReceived(MissionControlAck ack)
    {
        if (_pendingCommits.Remove(ack.RequestId, out var tcs))
        {
            if (ack.ErrorCode == 0)
                tcs.SetResult(true);
            else
                tcs.SetException(new Exception(ack.ErrorMessage));
        }
    }
}
```

### 4.4 Context Menu Logic

**❌ NEW** - Implements proactive menu pushing

**Purpose**: Generate context menu definitions based on selection and strategy

**Interface:**

```csharp
namespace Bagira.IOS.Logic
{
    public interface IContextMenuLogic
    {
        /// <summary>
        /// Handle selection changed event, push appropriate menu
        /// </summary>
        void OnSelectionChanged(SelectionChangedEvent evt);
        
        /// <summary>
        /// Handle invoked action from IG
        /// </summary>
        void OnActionInvoked(ContextActionInvoked evt);
        
        /// <summary>
        /// Set current menu strategy
        /// </summary>
        void SetStrategy(MenuStrategy strategy);
    }
    
    public enum MenuStrategy
    {
        Standard,
        Admin,
        DamageControl,
        Logistics
    }
}
```

**Implementation:**

```csharp
public class ContextMenuLogic : IContextMenuLogic
{
    private readonly IDerRepo _repo;
    private readonly DdsWriter<ContextActionsUpdate> _menuWriter;
    private MenuStrategy _currentStrategy = MenuStrategy.Standard;
    
    public void OnSelectionChanged(SelectionChangedEvent evt)
    {
        if (evt.SelectedEntityIds.Count == 0) return;
        
        // Get info about selected entities
        var menuItems = new List<ContextMenuItem>();
        
        foreach (var id in evt.SelectedEntityIds)
        {
            var entity = _repo.GetEntity(id);
            if (entity == null) continue;
            
            var info = entity.GetDescriptor<EntityInfo>()?.Data;
            if (info == null) continue;
            
            // Build menu based on strategy and entity state
            switch (_currentStrategy)
            {
                case MenuStrategy.Standard:
                    menuItems.Add(new ContextMenuItem
                    {
                        ActionId = "center_camera",
                        Label = "Center on Entity",
                        IconName = "camera"
                    });
                    menuItems.Add(new ContextMenuItem
                    {
                        ActionId = "edit_properties",
                        Label = "Properties...",
                        IconName = "edit"
                    });
                    break;
                    
                case MenuStrategy.Admin:
                    menuItems.Add(new ContextMenuItem
                    {
                        ActionId = "admin_delete",
                        Label = "DELETE",
                        IconName = "delete",
                        Style = "destructive"
                    });
                    menuItems.Add(new ContextMenuItem
                    {
                        ActionId = "admin_teleport",
                        Label = "Teleport...",
                        IconName = "move"
                    });
                    break;
                    
                case MenuStrategy.DamageControl:
                    // Check if entity is damaged
                    var damage = entity.GetDescriptor<EntityDamage>()?.Data;
                    if (damage != null && damage.Damage > 0)
                    {
                        menuItems.Add(new ContextMenuItem
                        {
                            ActionId = "repair",
                            Label = "Repair Unit",
                            IconName = "wrench"
                        });
                    }
                    break;
            }
        }
        
        // Serialize to JSON
        string menuJson = JsonConvert.SerializeObject(new { items = menuItems });
        
        // Push to IG
        _menuWriter.Write(new ContextActionsUpdate
        {
            MapGroupId = evt.MapGroupId,
            ForSelection = evt.SelectedEntityIds,
            MenuDefinitionJson = menuJson
        });
    }
    
    public void OnActionInvoked(ContextActionInvoked evt)
    {
        // Handle action based on ActionId
        switch (evt.ActionId)
        {
            case "center_camera":
                // Send MapCommandRequest
                break;
            case "edit_properties":
                // Open inspector panel
                break;
            case "admin_delete":
                // Send DestructionOrder
                break;
            // etc...
        }
    }
}
```

### 4.5 Transaction Manager

**❌ NEW** - Tracks request/response correlation

**Purpose**: Monitor outstanding requests, handle timeouts

**Interface:**

```csharp
namespace Bagira.IOS.Services
{
    public interface IRequestTransactionManager
    {
        /// <summary>
        /// Register outgoing request
        /// </summary>
        void TrackRequest(Guid requestId, string description);
        
        /// <summary>
        /// Mark request as completed
        /// </summary>
        void CompleteRequest(Guid requestId, bool success, string message = null);
        
        /// <summary>
        /// Get pending requests for UI display
        /// </summary>
        IEnumerable<PendingRequest> GetPendingRequests();
        
        /// <summary>
        /// Check for timeouts
        /// </summary>
        void CheckTimeouts();
    }
    
    public class PendingRequest
    {
        public Guid RequestId { get; set; }
        public string Description { get; set; }
        public DateTime SentTime { get; set; }
        public double AgeMs => (DateTime.Now - SentTime).TotalMilliseconds;
        public bool IsTimedOut => AgeMs > 5000;
    }
}
```

**Implementation:**

```csharp
public class RequestTransactionManager : IRequestTransactionManager
{
    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private const double TimeoutMs = 5000;
    
    public void TrackRequest(Guid requestId, string description)
    {
        _pending[requestId] = new PendingRequest
        {
            RequestId = requestId,
            Description = description,
            SentTime = DateTime.Now
        };
    }
    
    public void CompleteRequest(Guid requestId, bool success, string message)
    {
        if (_pending.Remove(requestId, out var req))
        {
            // Log to UI
            Console.WriteLine($"[{(success ? "OK" : "FAIL")}] {req.Description} ({req.AgeMs:F0}ms)");
            if (!success && message != null)
                Console.WriteLine($"  Error: {message}");
        }
    }
    
    public void CheckTimeouts()
    {
        var now = DateTime.Now;
        var timedOut = _pending.Values
            .Where(r => (now - r.SentTime).TotalMilliseconds > TimeoutMs)
            .ToList();
        
        foreach (var req in timedOut)
        {
            CompleteRequest(req.RequestId, false, "Timeout");
        }
    }
}
```

---

## 5. Architecture Design

### 5.1 Overall Structure

```
┌───────────────────────────────────────────────────────────────┐
│ Bagira.IOS.Program (Console App)                             │
│ ┌─────────────────────────────────────────────────────────┐  │
│ │ IosMock (IMockSubsystem)                                 │  │
│ │  ┌───────────────────────────────────────────────────┐  │  │
│ │  │ IosLogic (State Container)                         │  │  │
│ │  │  - DerRepo                                         │  │  │
│ │  │  - MissionEditorService                            │  │  │
│ │  │  - ContextMenuLogic                                │  │  │
│ │  │  - RequestTransactionManager                       │  │  │
│ │  │  - TkbService                                       │  │  │
│ │  └───────────────────────────────────────────────────┘  │  │
│ │                                                           │  │
│ │  ┌───────────────────────────────────────────────────┐  │  │
│ │  │ UI Panels (ImGui)                                  │  │  │
│ │  │  - ConfigPanel                                     │  │  │
│ │  │  - OrbatPanel                                      │  │  │
│ │  │  - MissionPanel                                    │  │  │
│ │  │  - InteractionPanel                                │  │  │
│ │  │  - SpawnerPanel                                    │  │  │
│ │  │  - InspectorPanel                                  │  │  │
│ │  └───────────────────────────────────────────────────┘  │  │
│ └─────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────┘
```

### 5.2 Data Flow

**Ingress (Network → UI):**
```
DDS Topics
   ↓
DerRepo.Poll()
   ↓
TopicHandlers dispatch to DerEntity
   ↓
DerDescriptor.Updated event fires
   ↓
UI Panels refresh (ImGui next frame)
```

**Egress (UI → Network):**
```
ImGui Button Click
   ↓
Panel calls IosLogic method
   ↓
IosLogic.SendXxxRequest()
   ↓
DdsWriter.Write()
   ↓
RequestTransactionManager.TrackRequest()
```

**Ack Processing:**
```
DerRepo.Poll() receives Ack
   ↓
RequestTransactionManager.CompleteRequest()
   ↓
Log updated in UI
```

### 5.3 Main Loop

```csharp
public class IosMock : IMockSubsystem
{
    private IosLogic _logic;
    
    public void Update(float dt)
    {
        // 1. Network Ingress
        _logic.Repo.Poll();
        
        // 2. Check timeouts
        _logic.TransactionManager.CheckTimeouts();
        
        // 3. Network Egress
        _logic.Repo.Flush();
    }
    
    public void DrawUI()
    {
        // Main menu bar
        if (ImGui.BeginMainMenuBar())
        {
            ImGui.Text($"IOS (Node {_logic.Repo.LocalNodeId})");
            if (ImGui.Button("EXIT")) Environment.Exit(0);
            ImGui.EndMainMenuBar();
        }
        
        // Dockspace
        ImGui.DockSpaceOverViewport();
        
        // Panels
        _configPanel.Draw(_logic);
        _orbatPanel.Draw(_logic);
        _missionPanel.Draw(_logic);
        _interactionPanel.Draw(_logic);
        _spawnerPanel.Draw(_logic);
        _inspectorPanel.Draw(_logic);
    }
}
```

---

## 6. UI Layout

### 6.1 Layout Diagram (ASCII Art)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ [≡] IOS MOCK - CONTROLLER (Node ID: 10)                              [_][□][X] │
├─────────────────┬───────────────────────┬───────────────────────────────────┤
│ A. ORBAT TREE   │ B. MAP CONFIG         │ D. ENTITY SPAWNER                 │
│                 │                       │                                   │
│ [Filter...]     │  Tool: [Navigation▼]  │  [Units][Graphics][Measures]      │
│ ▼ TaskForce 1   │  □ Satellite Layer    │  Search: [t-72         ]    (×)   │
│   ▼ Plt 1 (HQ)  │  ☑ Tactical Graphics  │                                   │
│     • Tank#1    │  □ Air Units          │  ┌─────────────────────────────┐  │
│     • Tank#2    │  □ Grid               │  │ [🚗] T-72B3 Main Battle Tank│  │
│     • Tank#3    │                       │  │      Type:100 | Russia      │  │
│   ▶ Plt 2       │  Icon Scale: [1.0   ] │  └─────────────────────────────┘  │
│   • Supply      │  Sel Color:  [Green▼] │  ┌─────────────────────────────┐  │
│                 │                       │  │ [🚗] T-72M1 (Export)        │  │
│  [New Unit...]  │  [SEND CONFIG PATCH]  │  │      Type:105 | Generic     │  │
│                 │                       │  └─────────────────────────────┘  │
│                 │                       │                                   │
│                 │                       │  Affiliation: ⦿Friend ○Hostile    │
│                 │                       │  Mode: [Shared (SimHost)▼]        │
│                 │                       │  [ACTIVATE PLACEMENT TOOL]        │
├─────────────────┼───────────────────────┼───────────────────────────────────┤
│                 │ C. SEL & MISSION      │ E. CONTEXT MENU LOGIC             │
│                 │                       │                                   │
│                 │  Selected: Tank#1     │  Strategy: [Standard        ▼]    │
│                 │  ID: 5000002          │  ○ Admin (Delete/Teleport)        │
│                 │  Owner: SimHost(1)    │  ○ Damaged (Repair)               │
│                 │                       │  ○ Logistics (Refuel)             │
│                 │  Mission:             │                                   │
│                 │  ▶ 1. Move WP_A       │  Menu Items (auto-generated):     │
│                 │  ⏸ 2. Wait 30s        │  • Center on Entity               │
│                 │  ⏹ 3. Patrol Area     │  • Properties...                  │
│                 │                       │  • Assign Mission                 │
│                 │  [JUMP][ABORT]        │                                   │
│                 │  [UPLOAD NEW MISSION] │  Last Action: center_camera       │
│                 │                       │  From IG: Map-1 (14:02:33)        │
├─────────────────┴───────────────────────┴───────────────────────────────────┤
│ F. DATA MONITOR & LOGS                                                      │
│                                                                             │
│ ┌─Time──┬─Topic────────────────┬─Details──────────────────────────────────┐ │
│ │ 14:01 │ RX MapClickEvent     │ Pos:45.12,12.33 | Ctx:A7F2... [✓VALID] │ │
│ │ 14:01 │ TX CreateEntityReq   │ Type:T-72 | To:SimHost                  │ │
│ │ 14:02 │ RX CreateEntityAck   │ ✓Success | NewID:5000005 (120ms)       │ │
│ │ 14:03 │ RX EntityMaster      │ ID:5000005 | State:Alive                │ │
│ │ 14:05 │ TX MissionControlReq │ CMD_JUMP | Entity:5000002               │ │
│ │ 14:05 │ RX MissionControlAck │ ⚠Timeout (5000ms)                       │ │
│ └───────┴──────────────────────┴──────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 Panel Descriptions

#### A. ORBAT Hierarchy (Tree View)
- **Purpose**: Visualize command structure
- **Data**: Reads `EntityInfo.CommanderId`
- **Interaction**: Click node → IOS sends `CMD_SET_SELECTION` to IG
- **Features**:
  - Recursive tree rendering
  - Status indicators (mission state)
  - Filter/search
  - Right-click context menu

#### B. Map Configuration
- **Purpose**: Control IG behavior
- **Mechanism**: JSON Merge Patch via `MapInteractionConfig`
- **Features**:
  - Tool selector (generates new Context ID)
  - Layer visibility toggles
  - Global style settings
  - "Send Config Patch" button

#### C. Selection & Mission
- **Purpose**: Inspect and control selected entity
- **Data**: Reads `EntityMission`, `EntityInfo`
- **Features**:
  - Display current mission tasks
  - Highlight active task
  - "Jump to Task" buttons
  - "Abort Mission" button
  - "Upload New Mission" (with conflict detection)

#### D. Entity Spawner
- **Purpose**: Create new entities
- **Data**: Uses `TkbService` to list types
- **Workflow**:
  1. User selects type (T-72)
  2. Click "Activate Placement Tool"
  3. IOS sends Config, waits for `MapClickEvent`
  4. On click received, sends `CreateEntityRequest`
  5. On ack, entity appears in ORBAT

#### E. Context Menu Logic
- **Purpose**: Test proactive menu pushing
- **Mechanism**: Strategy selector + automatic push
- **Workflow**:
  1. User selects strategy (Admin/Standard/...)
  2. When `SelectionChangedEvent` arrives
  3. IOS generates menu based on strategy
  4. IOS sends `ContextActionsUpdate`
  5. Log shows when IG invokes action

#### F. Data Monitor & Logs
- **Purpose**: Transparency and debugging
- **Features**:
  - Scrolling log of all DDS messages
  - Color-coded by type (RX=blue, TX=green)
  - Validation (Context ID matching)
  - Timeout indicators
  - Pending requests table

### 6.3 Additional Panels

**Inspector Panel** (Optional):
- Shows raw descriptor data for selected entity
- Tabs for each descriptor type
- JSON viewer for complex fields
- Property grid for editing

**Diagnostics Panel**:
- Network statistics (bandwidth, message rates)
- DER cache statistics (entity count, descriptor count)
- Transaction manager status
- Performance metrics

---

## 7. Critical Edge Cases & Mitigations

### 7.1 Late Join Synchronization

**Issue:** When IOS starts after IG, the `MapInteractionConfig` topic (Volatile QoS) might be missed. IOS UI controls (checkboxes/dropdowns) might default to wrong state.

**Solution:**
- On startup, IOS MUST read `MapConfigStatus` (Transient Local) from IG to hydrate UI state
- Block UI interaction until synchronization complete
- Show "Synchronizing..." indicator during initial load

**Code Pattern:**
```csharp
public async Task SynchronizeWithIg(int timeoutMs = 5000)
{
    var statusReader = new DdsReader<MapConfigStatus>(_participant, "MapConfigStatus");
    var stopwatch = Stopwatch.StartNew();
    
    _logger.LogInformation("Waiting for IG MapConfigStatus...");
    
    while (stopwatch.ElapsedMilliseconds < timeoutMs)
    {
        using var samples = statusReader.Take();
        var status = samples.FirstOrDefault(s => s.IsValid);
        
        if (status != null)
        {
            // Hydrate UI state from IG's current config
            var config = JsonConvert.DeserializeObject<MapConfig>(status.Data.CurrentSettingsJson);
            
            _uiState.SelectedTool = config.Tool;
            _uiState.VisibleLayers = config.View.Layers;
            _uiState.ActiveStyle = config.View.StylePreset;
            
            _logger.LogInformation("Synchronized with IG");
            _isSynchronized = true;
            return;
        }
        
        await Task.Delay(100);
    }
    
    _logger.LogWarning("IG synchronization timeout, using defaults");
    _isSynchronized = true;  // Allow interaction anyway
}

// In UI code:
if (!_isSynchronized)
{
    ImGui.Text("Synchronizing with IG...");
    return;
}

// Normal UI rendering
```

### 7.2 JSON Merge Patch Array Semantics

**Issue:** RFC 7396 treats arrays as atomic (REPLACE), not incremental (APPEND). Sending `{"layers": ["NewLayer"]}` disables all other layers.

**Solution:**
- Use `Dictionary<string, bool>` for layers in `MapInteractionConfig.ConfigurationJson`
- NOT `List<string>`
- Enables granular toggling: `{"view": {"layers": {"Units": false}}}` only affects Units layer

**Correct Structure:**
```json
{
  "view": {
    "layers": {
      "Terrain": true,
      "Units": true,
      "Overlays": false
    }
  },
  "tool": "Selection"
}
```

**Wrong Structure (DON'T USE):**
```json
{
  "view": {
    "layers": ["Terrain", "Units"]  // ❌ Sending patch will replace entire array
  }
}
```

### 7.3 DER Type Safety

**Issue:** `DerRepo` stores descriptors as `object` or `IDerDescriptor`. If SimHost updates schema (e.g., adds field to `EntityInfo`), IOS might crash during cast.

**Solution:**
- `FDP.Toolkit.DER` should enforce version checking in `SetDescriptor<T>()`
- Add `SchemaVersion` field to all descriptor structs
- Log warning and skip if version mismatch, don't crash

**Code Pattern:**
```csharp
public void SetDescriptor<T>(T descriptor) where T : class
{
    var typeName = typeof(T).Name;
    
    // Check if descriptor has version field
    var versionProp = typeof(T).GetProperty("SchemaVersion");
    if (versionProp != null)
    {
        var version = (int)versionProp.GetValue(descriptor);
        var expectedVersion = GetExpectedVersion<T>();
        
        if (version != expectedVersion)
        {
            _logger.LogWarning($"Schema version mismatch for {typeName}: expected {expectedVersion}, got {version}");
            // Store anyway, but flag as potentially incompatible
        }
    }
    
    _descriptors[typeName] = descriptor;
}

public T? GetDescriptor<T>() where T : class
{
    if (_descriptors.TryGetValue(typeof(T).Name, out var obj))
    {
        try
        {
            return (T)obj;
        }
        catch (InvalidCastException)
        {
            _logger.LogError($"Failed to cast descriptor to {typeof(T).Name}");
            return null;
        }
    }
    return null;
}
```

### 7.4 Optimistic Lock Conflict Handling

**Issue:** Two IOS instances edit the same mission simultaneously. Both read version 5, edit, and try to write version 6.

**Solution:**
- Second writer receives `UpdateDescriptorAck.Status=CONFLICT`
- IOS must notify user: "Mission modified by another user. Refresh and retry?"
- Show diff between local changes and server state

**Code Pattern:**
```csharp
private async Task<bool> CommitMissionAsync(int entityId, MissionPlan plan)
{
    var ack = await _commandGateway.UpdateEntityDescriptorAsync(new UpdateDescriptorRequest
    {
        EntityId = entityId,
        DescriptorType = "MissionPlan",
        Version = plan.Version,
        Json = JsonConvert.SerializeObject(plan)
    });
    
    if (ack.Status == AckStatus.SUCCESS)
    {
        return true;
    }
    else if (ack.Status == AckStatus.CONFLICT)
    {
        // Fetch latest version from server
        var latestPlan = await FetchLatestMission(entityId);
        
        // Show conflict dialog
        ShowConflictDialog(plan, latestPlan);
        return false;
    }
    else
    {
        _logger.LogError($"Mission commit failed: {ack.ErrorMessage}");
        return false;
    }
}
```

### 7.5 Context Menu Lifecycle Management

**Issue:** IOS pushes context menu definitions to IG. If IOS disconnects, stale menus remain on IG.

**Solution:**
- Context menu descriptors should have TTL (Time To Live)
- IG expires menus after 30s if no refresh from IOS
- IOS sends heartbeat updates every 10s for active menus

**Not Critical for Mock:** Document that restarting IG clears menus.

---

## 8. Implementation Plan

### 7.1 Phase P6: IOS Services (2 days)

**Duration**: 2 days  
**Dependencies**: SHARED P3 (DER), SHARED P4 (Commands)

**Tasks:**
1. Implement `RequestTransactionManager`
2. Implement `MissionEditorService`
3. Implement `ContextMenuLogic`
4. Write service tests

**Deliverables:**
- `Bagira.IOS.Services.dll`
- Service tests passing

### 7.2 Phase P7: IOS UI Panels (4 days)

**Duration**: 4 days  
**Dependencies**: P6 (Services), SHARED P3 (DER)

**Tasks:**
1. Implement `ConfigPanel`
2. Implement `OrbatPanel` (recursive tree)
3. Implement `MissionPanel`
4. Implement `InteractionPanel` (event log)
5. Implement `SpawnerPanel` (TKB browser)
6. Test all panels with mock data

**Deliverables:**
- `Bagira.IOS.Panels.dll`
- All panels functional

### 7.3 Phase P8: IOS Application Shell (2 days)

**Duration**: 2 days  
**Dependencies**: P7 (UI Panels)

**Tasks:**
1. Implement `IosMock` class
2. Implement CLI argument parsing
3. Implement main loop with DER Poll/Flush
4. Create DDS-to-DER translators
5. Test standalone IOS
6. Test IOS + IG integration
7. Test IOS + SimHost integration

**Deliverables:**
- `Bagira.IOS.exe`
- DDS Translators (EntityMaster, EntityInfo, GeoSpatial, etc.)
- Standalone IOS functional
- Integration tests passing

### 7.4 Testing Strategy

**Unit Tests:**
- Services: Mission editor, transaction manager
- Panels: Logic only (no ImGui rendering)

**Integration Tests:**
- IOS → IG: Config changes, context menus
- IOS → SimHost: Entity creation, mission control
- IOS ← IG: Click events, selection changes
- IOS ← SimHost: Acks, state updates

**Manual Tests:**
- Full workflow: Spawn platoon, assign mission, monitor execution
- Conflict detection: Multiple IOS instances editing same mission
- Timeout handling: Disconnect SimHost, verify UI shows timeouts

---

## 8. Technical Notes

### 8.1 No ECS in IOS

**Critical**: IOS does NOT use FDP Kernel or ECS. It is a pure dictionary-based application using raw DDS.

**Rationale**:
- Validates that BDC SST works with external systems
- Simpler codebase for control-only interface
- Demonstrates non-FDP client integration

### 8.2 JSON Configuration

**MapInteractionConfig** structure (example):
```json
{
  "mapGroupId": 0,
  "activeContextId": "a7f23e45-...",
  "interaction": {
    "activeTool": "PLACEMENT",
    "toolConfig": {
      "entityType": 100
    }
  },
  "view": {
    "layers": {
      "satellite": true,
      "tactical_graphics": true,
      "air": false
    },
    "declutterThreshold": 0.5
  }
}
```

**Merge Patch Example**:
```json
{
  "view": {
    "layers": {
      "air": true
    }
  }
}
```
Result: Only `air` layer changed, others preserved.

### 8.3 Context ID Workflow

```
IOS: User clicks "Place Tank" button
  → Generate new Guid: a7f23e45-1234-5678-...
  → Store in _activeContextId
  → Send MapInteractionConfig with contextId=a7f23e45...

IG: Receives config
  → Activates PlacementTool
  → Stores contextId internally

IG: User clicks map
  → Sends MapClickEvent with contextId=a7f23e45...

IOS: Receives MapClickEvent
  → if (event.contextId == _activeContextId):
      ✓ VALID - Process click, send CreateEntityRequest
    else:
      ⚠ STALE - Ignore (user changed tools)
```

### 8.4 Optimistic Locking for Missions

```
IOS: User opens mission editor for Tank#1
  → Read EntityMission.Plan (tasks list)
  → Read DescriptorOptimisticLock.Version (e.g., version=5)
  → Show in UI

IOS: User edits mission (adds task)
  → Edit local copy only

IOS: User clicks "Upload"
  → Send MissionControlRequest:
      - CMD_REPLACE_MISSION
      - BaseVersion = 5
      - NewPlan = edited tasks

SimHost: Receives request
  → Check current version (still 5?)
    - YES: Apply changes, increment to version=6, send Success Ack
    - NO (now 7): Reject with Version Conflict, send Error Ack

IOS: Receives Ack
  → if (Success):
      ✓ Mission updated
    else:
      ⚠ Show warning: "Mission was modified by another user. Reload?"
      → Reload: Re-read from DER
```

---

## 9. Dependencies

### Project References

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- DDS & Data Model -->
    <ProjectReference Include="..\ModuleHost.Network.Cyclone\ModuleHost.Network.Cyclone.csproj" />
    <ProjectReference Include="..\Bagira.DDS.DataModel\Bagira.DDS.DataModel.csproj" />
    
    <!-- Shared Toolkits -->
    <ProjectReference Include="..\FDP.Toolkit.DER\FDP.Toolkit.DER.csproj" />
    <ProjectReference Include="..\FDP.Toolkit.Commands\FDP.Toolkit.Commands.csproj" />
    
    <!-- Shared Definitions -->
    <ProjectReference Include="..\Bagira.Map.Definitions\Bagira.Map.Definitions.csproj" />
    
    <!-- UI -->
    <PackageReference Include="ImGui.NET" Version="1.89.9" />
    <PackageReference Include="rlImGui-cs" Version="1.0.0" />
    
    <!-- JSON -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

---

## 10. Success Criteria

### Functional Requirements

- ✅ IOS can control IG configuration (layers, tools)
- ✅ IOS can spawn entities via SimHost
- ✅ IOS can visualize ORBAT hierarchy
- ✅ IOS can edit missions with conflict detection
- ✅ IOS can push context menus proactively
- ✅ IOS can monitor all network traffic
- ✅ IOS handles timeouts gracefully

### Technical Requirements

- ✅ No ECS dependencies (pure DDS client)
- ✅ JSON merge patching working correctly
- ✅ Context ID validation working
- ✅ Optimistic locking preventing conflicts
- ✅ All panels responsive (<16ms UI update)
- ✅ Network overhead minimal (<1MB/s idle)

### Integration Requirements

- ✅ Works standalone (mock mode)
- ✅ Works with real IG
- ✅ Works with real SimHost
- ✅ Works with multiple IOS instances
- ✅ Handles IG/SimHost disconnection gracefully

---

## 8. Embeddability Architecture

### 8.1 Overview

IOS is designed to run in **two deployment modes**:
1. **Standalone Application** - Independent executable (`Bagira.IOS.Standalone.exe`)
2. **Embedded Subsystem** - Library embedded in aggregated runner (`Bagira.Runner.exe`)

This dual-mode design enables:
- Independent C&C dashboard development
- Integration into combined view with IG map
- Headless automated testing
- ImGui context sharing with IG

**Reference:** See [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) for full aggregated application architecture.

### 8.2 ISubsystem Interface Implementation

**Interface:** `ISubsystem` (defined in `Bagira.Runner.Models.ISubsystem.cs`)

IOS implements the standard subsystem interface:

```csharp
public class IosSubsystem : SubsystemBase
{
    private DdsParticipant? _participant;
    private IosConfiguration _config;
    private IDerRepo? _derRepo;
    private BdcCommandGateway? _commandGateway;
    private SubsystemStatusPublisher? _statusPublisher;
    
    // Services
    private MissionEditorService? _missionEditor;
    private ContextMenuLogic? _contextMenuLogic;
    private RequestTransactionManager? _transactionManager;
    
    public override string Name => "ios";
    
    // Lifecycle Methods
    public override void Initialize(object config)
    {
        _config = (IosConfiguration)config;
        
        // IOS does NOT use ECS - pure DDS client
        // Initialize DER repository (dictionary-based entity storage)
        _derRepo = new DerRepo();
        
        // Initialize services
        _missionEditor = new MissionEditorService();
        _contextMenuLogic = new ContextMenuLogic();
        _transactionManager = new RequestTransactionManager();
        
        // If standalone, initialize ImGui window
        if (_config.Standalone && !_config.Headless)
        {
            Raylib.InitWindow(1280, 720, "IOS Mock");
            rlImGui.Setup(true);
        }
        
        Status = SubsystemStatus.Ready;
    }
    
    public override void ConnectToDomain(int domainId)
    {
        _participant = new DdsParticipant(domainId);
        
        // Create DDS readers/writers
        var entityReader = new DdsReader<EntityMaster>(_participant, "EntityMaster");
        var configWriter = new DdsWriter<MapInteractionConfig>(_participant, "MapInteractionConfig");
        
        // Initialize command gateway
        _commandGateway = new BdcCommandGateway(_participant);
        
        // Start DDS → DER ingress loop
        StartDdsIngress(entityReader);
        
        // Announce presence
        _statusPublisher = new SubsystemStatusPublisher(_participant, _config.NodeId, "ios");
        _statusPublisher.UpdateStatus(SubsystemStatus.Ready);
    }
    
    public override void Start()
    {
        Status = SubsystemStatus.Running;
    }
    
    public override void Update(float deltaTime)
    {
        // Update DER from DDS samples
        UpdateDerFromDds();
        
        // Update services
        _transactionManager?.Update();
        
        // Draw ImGui panels
        if (!_config.Headless)
        {
            if (_config.Standalone)
            {
                // Standalone: Manage own Raylib window
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.DARKGRAY);
                
                rlImGui.Begin();
                DrawIosPanels();
                rlImGui.End();
                
                Raylib.EndDrawing();
            }
            else
            {
                // Embedded: Just draw panels (IG owns window)
                DrawIosPanels();
            }
        }
    }
    
    public void DrawIosPanels()
    {
        // Called by IG when embedded, or by Update() when standalone
        ImGui.Begin("ORBAT Tree");
        // ... panel code
        ImGui.End();
        
        ImGui.Begin("Mission Editor");
        // ... panel code
        ImGui.End();
        
        // ... other panels
    }
    
    // ... other ISubsystem methods
}
```

### 8.3 Window Ownership Models

**Standalone Mode:** IOS owns its own Raylib window
```csharp
if (_config.Standalone)
{
    Raylib.InitWindow(1280, 720, "IOS Mock");
    rlImGui.Setup(true);
    
    // In Update(): Manage full Raylib frame
    Raylib.BeginDrawing();
    rlImGui.Begin();
    DrawIosPanels();
    rlImGui.End();
    Raylib.EndDrawing();
}
```

**Embedded Mode:** IOS shares IG's Raylib window
```csharp
if (!_config.Standalone)
{
    // IG calls IosSubsystem.DrawIosPanels() within its ImGui context
    // IOS does NOT call BeginDrawing/EndDrawing
}
```

### 8.4 Refactoring Strategy

**Current Structure:**
```
Bagira.IOS/
├── Program.cs
├── Services/
│   ├── MissionEditorService.cs
│   └── ...
└── Panels/
    └── ...
```

**Refactored Structure:**
```
Bagira.IOS/ (Library)
├── IosSubsystem.cs               ← NEW: ISubsystem implementation
├── IosConfiguration.cs            ← NEW: Configuration model
├── Services/                      ← UNCHANGED
│   ├── MissionEditorService.cs
│   └── ...
└── Panels/                        ← UNCHANGED
    └── ...

Bagira.IOS.Standalone/ (Executable)
└── Program.cs                     ← NEW: Thin wrapper
```

### 8.5 Configuration Model

```csharp
public class IosConfiguration
{
    public int NodeId { get; set; } = 3;
    public bool Headless { get; set; }
    public bool Standalone { get; set; } = true;  // False when embedded
    public string? ConfigFile { get; set; }
}
```

### 8.6 DER Repository Integration

**DER Repo:** Non-ECS entity storage (uses `FDP.Toolkit.DER`)

```csharp
private void StartDdsIngress(DdsReader<EntityMaster> reader)
{
    Task.Run(() => 
    {
        while (_running)
        {
            using var samples = reader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                
                // Get or create DER entity
                var entity = _derRepo.GetOrCreate(sample.Data.EntityId);
                
                // Update descriptor
                entity.SetDescriptor<EntityMaster>(sample.Data);
            }
        }
    });
}
```

**Key Difference from ECS:**
- IOS uses `IDerRepo` (dictionary-based) instead of FDP ECS World
- No components/systems - just raw DDS data storage
- Proves protocol works with non-ECS systems

### 8.7 Headless Mode Support

**When `Headless = true`:**
- **No Window**: Skip Raylib window creation
- **No ImGui**: Skip all panel rendering
- **Logic Only**: Still run DDS readers, services, command gateway
- **Metrics**: Expose performance counters for testing

**Use Case:** Automated testing of command sending and latency measurement

```csharp
public override void Update(float deltaTime)
{
    // Always update DER and services
    UpdateDerFromDds();
    _transactionManager?.Update();
    
    // Only render if NOT headless
    if (!_config.Headless)
    {
        DrawIosPanels();
    }
}
```

### 8.8 Waiting Room Integration

**Protocol:** IOS waits for SimHost and IG to be ready

```csharp
public override async Task WaitForReady()
{
    var coordinator = new WaitingRoomCoordinator(_participant, _logger);
    
    // Wait for both SimHost and IG
    await coordinator.WaitForPeersAsync(
        new[] { "simhost", "ig" }, 
        timeoutSeconds: 30
    );
    
    _statusPublisher?.UpdateStatus(SubsystemStatus.Ready);
}
```

### 8.9 ImGui Context Sharing

**Embedded Mode Strategy:**

IG owns the ImGui context, IOS just draws panels:

```csharp
// In IgSubsystem.Update()
rlImGui.Begin();

// IG's panels
DrawIgToolbar();
DrawIgLayerPanel();

// IOS's panels (if embedded)
if (_embeddedIos != null)
{
    _embeddedIos.DrawIosPanels();  // IOS draws into IG's context
}

rlImGui.End();
```

**Docking Layout:**
- Map canvas on left (IG)
- Dockable panels on right (IOS)
- Shared menu bar at top

### 8.10 Deployment Modes

**Mode 1: Standalone IOS**
```bash
Bagira.IOS.Standalone.exe --domain 0 --node-id 3
# Own window, separate from IG
```

**Mode 2: Embedded in Runner (Combined View)**
```bash
Bagira.Runner.exe --mode all --domain 0
# IOS panels dock within IG's window
```

**Mode 3: Embedded in Runner (Headless Testing)**
```bash
Bagira.Runner.exe --mode ios --domain 0 --headless --script test.json
# IOS runs commands without UI, measures latency
```

### 8.11 Implementation Tasks

See [TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md) Phase R2:
- **R2.7**: Refactor IOS to IosSubsystem Library (1.0d)
- **R2.8**: Create IOS Standalone Program.cs (0.25d)
- **R2.9**: Test IOS Embeddability (0.5d)

**Dependencies:**
- Runner Phase R1 complete (ISubsystem interface defined)
- IOS Phases IOS-P6 to IOS-P8 complete (all functionality implemented)
- FDP.Toolkit.DER complete (SHARED Phase P3)

### 8.12 Testing Strategy

**Unit Tests:**
- `Test_IOS_Initialize`: Verify DER repo creation
- `Test_IOS_StandaloneWindow`: Verify creates own Raylib window
- `Test_IOS_EmbeddedMode`: Verify skips window creation when embedded
- `Test_IOS_Headless`: Verify no UI when headless

**Integration Tests:**
- `Test_IOS_Standalone`: Run with own window
- `Test_IOS_EmbeddedInIG`: Verify panels render in IG's context
- `Test_IOS_CommandLatency`: Measure CreateEntity request→ack latency
- `Test_IOS_DerIngress`: Verify DDS → DER updates

**Verification:**
- No ECS dependencies (pure DDS client)
- Window ownership correct in both modes
- ImGui context sharing works
- Headless mode functional for automated testing

---

**END OF IOS MOCK DESIGN DOCUMENT**
