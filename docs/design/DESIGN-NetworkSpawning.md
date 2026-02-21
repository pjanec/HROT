# FDP.Toolkit.NetworkSpawning Design

**Version:** 1.0  
**Date:** 2026-02-21  
**Status:** Ready for Implementation

**⚠️ INFRASTRUCTURE AUDIT:** This document reflects the new `FDP.Toolkit.NetworkSpawning` toolkit design derived from the design talk. It sits on top of existing FDP infrastructure (ELM, TKB, Replication). Components marked ✅ EXIST are dependencies. The toolkit itself is marked ❌ NEW.

**Parent Document**: [TASK-TRACKER.md](./TASK-TRACKER.md)  
**Related Documents**: [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) | [DESIGN-IG.md](./DESIGN-IG.md)

## Table of Contents

1. [Motivation & Problem Statement](#1-motivation--problem-statement)
2. [Infrastructure Status Matrix](#2-infrastructure-status-matrix)
3. [Design Overview: Converging Flows](#3-design-overview-converging-flows)
4. [Events (Spawn Commands)](#4-events-spawn-commands)
5. [NetworkSpawningSystem](#5-networkspawningsystem)
6. [EntityComponentReflector](#6-entitycomponentreflector)
7. [DescriptorMapper (Application-Side)](#7-descriptormapper-application-side)
8. [Integration Patterns](#8-integration-patterns)
9. [Custom EntityMaster Support](#9-custom-entitymaster-support)
10. [Implementation Plan](#10-implementation-plan)

---

## 1. Motivation & Problem Statement

### 1.1 The Problem: Duplicated Spawning Logic

Currently, every FDP node (SimHost, IG, NetworkDemo) that needs to create entities must manually implement the same tedious boilerplate:

```csharp
// CURRENT: Every spawner must do this manually (duplicated in each project)
var entity = world.CreateEntity();
template.ApplyTo(world, entity);
world.SetComponent(entity, new NetworkIdentity { Value = netId });
entityMap.Register(netId, entity);
world.SetComponent(entity, new NetworkOwnership { ... });
world.AddComponent(entity, new NetworkAuthority(...));
world.AddComponent(entity, new NetworkSpawnRequest { ... });
world.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
elm.BeginConstruction(entity, tkbType, world.GlobalVersion, cmdBuffer);
```

This is the `SpawnLocalEntities` helper in NetworkDemo, the `CreateEntityRequestSystem` in the SimHost design, and the `EntityMasterTranslator` in the IG design. All three contain nearly the same boilerplate with subtle variations that can diverge over time.

### 1.2 The Problem: Two Code Paths for One Intent

Entity creation arrives from two sources but should result in identical setup:

| **Source** | **Trigger** | **Current Path** |
|---|---|---|
| Local logic (script, mission AI, physics init) | Direct programmatic call | Custom `SpawnLocalEntities` function |
| Network request (DDS `CreateEntityRequest`) | DDS ingress → EventBus | `CreateEntityRequestSystem` |
| Remote replication (DDS `EntityMaster`) | DDS → `EntityMasterTranslator` | Translator calls ELM directly |

All three paths eventually need the same rigorous lifecycle setup. Any inconsistency causes missed `PendingNetworkAck`, wrong ELM lifecycle state, or unreplicated entities.

### 1.3 The Solution

`FDP.Toolkit.NetworkSpawning` encapsulates the **what** (spawn configuration) separately from the **how** (the rigorous FDP setup sequence). Any code that wants to create an entity publishes a `SpawnEntityCommand` ECS event. A single centralized `NetworkSpawningSystem` processes all such commands identically.

---

## 2. Infrastructure Status Matrix

| Component | Status | Location | Purpose |
|-----------|--------|----------|---------|
| **Entity Lifecycle Module** | ✅ EXISTS | `FDP.Toolkit.Lifecycle.EntityLifecycleModule` | Constructing→Active→TearDown state machine |
| **TKB Database** | ✅ EXISTS | `FDP.Toolkit.Tkb.TkbDatabase` | Entity templates with default component sets |
| **Network Entity Map** | ✅ EXISTS | `FDP.Toolkit.Replication.Services.NetworkEntityMap` | Network ID ↔ Local Entity mapping |
| **Network Identity** | ✅ EXISTS | `FDP.Toolkit.Replication.Components.NetworkIdentity` | ECS component carrying network ID |
| **Network Ownership** | ✅ EXISTS | `ModuleHost.Core.Network.NetworkOwnership` | Primary/local node ID on entity |
| **Network Authority** | ✅ EXISTS | `FDP.Toolkit.Replication.Components.NetworkAuthority` | Replay/authority tracking |
| **Pending Network Ack** | ✅ EXISTS | `ModuleHost.Core.Network.Interfaces.PendingNetworkAck` | Blocks lifecycle until peers confirm |
| **NetworkSpawnRequest** | ✅ EXISTS | `FDP.Toolkit.Replication.Components.NetworkSpawnRequest` | Signal to Network Gateway to replicate |
| **ID Allocator** | ✅ EXISTS | `ModuleHost.Network.Cyclone.DdsIdAllocator` | Server-side entity ID allocation |
| **SpawnEntityCommand** | ❌ NEW | `FDP.Toolkit.NetworkSpawning.Events.SpawnEntityCommand` | Universal spawn intent event |
| **UpdateEntityCommand** | ❌ NEW | `FDP.Toolkit.NetworkSpawning.Events.UpdateEntityCommand` | Universal component update event |
| **DestroyEntityCommand** | ❌ NEW | `FDP.Toolkit.NetworkSpawning.Events.DestroyEntityCommand` | Universal entity destruction event |
| **NetworkSpawningSystem** | ❌ NEW | `FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem` | Centralised spawn/update/destroy processor |
| **EntityComponentReflector** | ❌ NEW | `FDP.Toolkit.NetworkSpawning.EntityComponentReflector` | Generic component-setting via reflection |

---

## 3. Design Overview: Converging Flows

The core idea is to funnel all entity creation requests — regardless of origin — through a single **command** object processed by a single **system**.

```
┌──────────────────────┐      SpawnEntityCommand         ┌───────────────────────────┐
│  Local Spawner Logic │ ──────────────────────────────► │                           │
│  (AI, Script, Test)  │                                  │   NetworkSpawningSystem   │
└──────────────────────┘                                  │                           │
                                                          │  1. ID allocation         │
┌──────────────────────┐      SpawnEntityCommand         │  2. TKB ApplyTo           │
│  DDS CreateEntity    │ ─── [translated by Handler] ──► │  3. Component overrides   │
│  Request Translator  │                                  │  4. NetworkIdentity       │
└──────────────────────┘                                  │  5. NetworkOwnership      │
                                                          │  6. NetworkAuthority      │
┌──────────────────────┐      SpawnEntityCommand         │  7. NetworkSpawnRequest   │
│  DDS EntityMaster     │ ─── [translated by Translator]► │  8. PendingNetworkAck     │
│  Ingress Translator  │                                  │  9. ELM BeginConstruction │
└──────────────────────┘                                  └───────────────────────────┘
```

**Key design properties:**
- **Same rigorous setup always**: No matter the source, every entity goes through steps 1–9 above.
- **`InitType` controls reliability**: Use `ReliableInitType.AllPeers` (SimHost: authority) or `ReliableInitType.None` (IG: ghost replica, no handshake required).
- **`NetworkId == 0` means allocate**: For new entities; non-zero means replicating an existing network entity.
- **Generic overrides via `List<object>`**: Initial components (position, entity master, etc.) are set as an override on top of TKB template defaults.

---

## 4. Events (Spawn Commands)

All events live in namespace `FDP.Toolkit.NetworkSpawning.Events`.

### 4.1 SpawnEntityCommand

```csharp
namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to spawn an entity locally.
    /// Can be generated by local logic (AI/script) or bridged from a DDS request/ingress translator.
    /// Published onto the FdpEventBus; consumed by NetworkSpawningSystem.
    /// </summary>
    public struct SpawnEntityCommand
    {
        /// <summary>
        /// 0 = request new ID allocation (local authority creates new entity).
        /// Non-zero = replicate an existing network entity (ghost/replica path).
        /// </summary>
        public long NetworkId;

        /// <summary>
        /// TKB template type to instantiate. Must match a registered TkbDatabase entry.
        /// </summary>
        public long TkbType;

        /// <summary>
        /// Node ID that owns/controls this entity.
        /// For SimHost-spawned entities: SimHost's local node ID.
        /// For network-replicated ghosts: the remote node's ID.
        /// </summary>
        public int OwnerNodeId;

        /// <summary>
        /// Reliability mode for the Entity Lifecycle Module handshake.
        /// AllPeers: Entity stays Constructing until all peers ACK (use on authority node).
        /// None:     Entity transitions immediately to Constructing state (use for ghost replicas).
        /// </summary>
        public ReliableInitType InitType;

        /// <summary>
        /// Optional list of ECS component instances to apply on top of TKB template defaults.
        /// Each item is an object whose runtime type is used by EntityComponentReflector
        /// to call world.SetComponent(entity, type, value).
        /// Typical contents: EntityMaster, GeoSpatial, EntityInfo, VehicleState, etc.
        /// </summary>
        public List<object> InitialComponents;

        /// <summary>
        /// Optional correlation ID for request tracking (used by gateway pattern).
        /// </summary>
        public Guid RequestId;
    }
}
```

### 4.2 UpdateEntityCommand

```csharp
/// <summary>
/// Universal command to update one or more components on an existing network entity.
/// Bridged from DDS UpdateEntityDescriptorRequest by the responsible translator.
/// </summary>
public struct UpdateEntityCommand
{
    /// <summary>
    /// The network entity ID to update. Must be registered in NetworkEntityMap.
    /// </summary>
    public long NetworkId;

    /// <summary>
    /// List of component instances to apply. Each item replaces the existing component.
    /// Uses the same List&lt;object&gt; / EntityComponentReflector pattern as SpawnEntityCommand.
    /// </summary>
    public List<object> ComponentsToUpdate;

    /// <summary>
    /// Optional correlation ID.
    /// </summary>
    public Guid RequestId;
}
```

### 4.3 DestroyEntityCommand

```csharp
/// <summary>
/// Universal command to destroy a network entity via proper ELM lifecycle teardown.
/// Bridged from DDS EntityMaster DISPOSE or a local destruction request.
/// </summary>
public struct DestroyEntityCommand
{
    /// <summary>
    /// The network entity ID to destroy. Looked up in NetworkEntityMap.
    /// </summary>
    public long NetworkId;

    /// <summary>
    /// Human-readable reason (for logging/diagnostics).
    /// </summary>
    public string Reason;
}
```

---

## 5. NetworkSpawningSystem

### 5.1 Purpose

`NetworkSpawningSystem` is a module system (implementing `IModuleSystem`) that runs in the `BeforeSync` phase. It:
1. Consumes `SpawnEntityCommand` events from the `FdpEventBus`.
2. Performs the full entity creation sequence (TKB, network infra, ELM).
3. Consumes `UpdateEntityCommand` events — applies component overrides to existing entities.
4. Consumes `DestroyEntityCommand` events — initiates proper ELM teardown.

### 5.2 System Registration

```csharp
// In any IModule's RegisterSystems():
registry.RegisterSystem(new NetworkSpawningSystem(
    tkbDatabase, elm, entityMap, idAllocator, localNodeId));
```

### 5.3 Implementation Sketch

```csharp
namespace FDP.Toolkit.NetworkSpawning.Systems
{
    using FDP.Toolkit.NetworkSpawning.Events;
    using FDP.Toolkit.Lifecycle;
    using FDP.Toolkit.Replication.Services;
    using FDP.Toolkit.Replication.Components;
    using FDP.Toolkit.Tkb;
    using ModuleHost.Core.Abstractions;
    using ModuleHost.Core.Network;
    using ModuleHost.Core.Network.Interfaces;
    using Fdp.Kernel;

    public class NetworkSpawningSystem : IModuleSystem
    {
        private readonly TkbDatabase _tkb;
        private readonly EntityLifecycleModule _elm;
        private readonly NetworkEntityMap _entityMap;
        private readonly DdsIdAllocator _idAllocator;
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;

        public NetworkSpawningSystem(
            TkbDatabase tkb,
            EntityLifecycleModule elm,
            NetworkEntityMap entityMap,
            DdsIdAllocator idAllocator,
            FdpEventBus eventBus,
            int localNodeId)
        { /* assign fields */ }

        public void Execute(ISimulationView view, float dt)
        {
            var world = view.World;

            // 1. Process Spawns
            foreach (var cmd in _eventBus.ConsumeEvents<SpawnEntityCommand>())
                ExecuteSpawn(world, cmd);

            // 2. Process Updates
            foreach (var cmd in _eventBus.ConsumeEvents<UpdateEntityCommand>())
                ExecuteUpdate(world, cmd);

            // 3. Process Destructions
            foreach (var cmd in _eventBus.ConsumeEvents<DestroyEntityCommand>())
                ExecuteDestroy(world, cmd);
        }

        // ── Spawn ──────────────────────────────────────────────────────────────────

        private void ExecuteSpawn(EntityRepository world, SpawnEntityCommand cmd)
        {
            // A. ID Management
            long netId = cmd.NetworkId;
            if (netId == 0)
                netId = _idAllocator.Allocate(); // sync path; or store pending async task

            // Guard: already known
            if (_entityMap.TryGetEntity(netId, out _))
            {
                FdpLog.Warn($"[NetworkSpawning] Entity {netId} already exists, ignoring duplicate spawn.");
                return;
            }

            // B. Template Lookup
            if (!_tkb.TryGetByType(cmd.TkbType, out var template))
            {
                FdpLog.Error($"[NetworkSpawning] TkbType {cmd.TkbType} not found — spawn aborted.");
                return;
            }

            // C. ECS entity + TKB defaults
            var entity = world.CreateEntity();
            template.ApplyTo(world, entity);

            // D. Apply caller-provided component overrides (on top of TKB defaults)
            if (cmd.InitialComponents != null)
                foreach (var comp in cmd.InitialComponents)
                    EntityComponentReflector.SetComponent(world, entity, comp);

            // E. Network infrastructure setup
            world.SetComponent(entity, new NetworkIdentity { Value = netId });
            world.SetComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = cmd.OwnerNodeId,
                LocalNodeId    = _localNodeId
            });
            world.AddComponent(entity, new NetworkAuthority(cmd.OwnerNodeId, _localNodeId));
            world.AddComponent(entity, new NetworkSpawnRequest
            {
                DisType = ExtractDisType(cmd.InitialComponents),
                OwnerId = (ulong)cmd.OwnerNodeId
            });

            // F. Reliable Init: PendingNetworkAck blocks Active transition until peers ACK
            if (cmd.InitType != ReliableInitType.None)
                world.AddComponent(entity, new PendingNetworkAck { ExpectedType = cmd.InitType });

            // G. Register in entity map
            _entityMap.Register(netId, entity);

            // H. Kick off ELM construction (fires ConstructionOrder → NetworkGatewaySystem → DDS)
            //    Do NOT set ConstructingTag manually — ELM manages lifecycle state internally.
            var cmdBuffer = world.GetCommandBuffer();
            _elm.BeginConstruction(entity, cmd.TkbType, world.GlobalVersion, cmdBuffer);

            FdpLog.Info($"[NetworkSpawning] Spawned entity {netId} (TkbType={cmd.TkbType}, Owner={cmd.OwnerNodeId})");
        }

        // ── Update ─────────────────────────────────────────────────────────────────

        private void ExecuteUpdate(EntityRepository world, UpdateEntityCommand cmd)
        {
            if (!_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                FdpLog.Warn($"[NetworkSpawning] Update for unknown entity {cmd.NetworkId}.");
                return;
            }

            if (cmd.ComponentsToUpdate != null)
                foreach (var comp in cmd.ComponentsToUpdate)
                    EntityComponentReflector.SetComponent(world, entity, comp);
        }

        // ── Destroy ────────────────────────────────────────────────────────────────

        private void ExecuteDestroy(EntityRepository world, DestroyEntityCommand cmd)
        {
            if (!_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                FdpLog.Warn($"[NetworkSpawning] Destroy for unknown entity {cmd.NetworkId}.");
                return;
            }

            var cmdBuffer = world.GetCommandBuffer();
            _elm.BeginDestruction(entity, world.GlobalVersion, cmd.Reason, cmdBuffer);

            FdpLog.Info($"[NetworkSpawning] Destroyed entity {cmd.NetworkId} ({cmd.Reason})");
        }

        private static ulong ExtractDisType(List<object> components)
        {
            if (components == null) return 0;
            foreach (var comp in components)
                if (comp is Bagira.BDC.SSTD.EntityMaster master)
                    return master.DisType;
            return 0;
        }
    }
}
```

---

## 6. EntityComponentReflector

A small static helper that applies a component instance to an entity by its runtime type. This is "slow-path" logic (creation/updates only, not per-frame), so reflection overhead is acceptable.

```csharp
namespace FDP.Toolkit.NetworkSpawning
{
    using Fdp.Kernel;

    public static class EntityComponentReflector
    {
        /// <summary>
        /// Calls world.SetComponent(entity, component.GetType(), component) dynamically.
        /// Allows callers to pass heterogeneous component lists without knowing types at compile time.
        /// Only for use in slow-path operations (entity spawn, update) — never in the tight physics loop.
        /// </summary>
        public static void SetComponent(EntityRepository world, Entity entity, object component)
        {
            if (component == null) return;
            var type = component.GetType();
            // EntityRepository already has an internal Object-parameterised SetComponent path
            // used by the ImGui toolkit; this call reaches the same path.
            world.SetComponent(entity, type, component);
        }
    }
}
```

---

## 7. DescriptorMapper (Application-Side)

`DescriptorMapper` is **not** part of the Toolkit library itself — it lives in the application project (SimHost, IG, NetworkDemo). The Toolkit deals in generic `List<object>`; the application is responsible for converting its domain-specific DDS types (`EntityDescriptorUnion`) to the component list before publishing `SpawnEntityCommand`.

**SimHost DescriptorMapper pattern:**

```csharp
// Bagira.SimHost.Util.DescriptorMapper  (SimHost project, not in Toolkit)
public static class DescriptorMapper
{
    public static List<object> MapToComponents(
        List<EntityDescriptorUnion> descriptors,
        WGS84Transform geoTransform)
    {
        var components = new List<object>();
        foreach (var desc in descriptors)
        {
            switch (desc._d)
            {
                case EDescriptorType.dtEntityMaster:
                    // Use Bagira.DDS.DataModel.EntityMaster directly (no wrapper)
                    components.Add(desc.EntityMaster);
                    break;

                case EDescriptorType.dtEntityInfo:
                    components.Add(desc.EntityInfo);
                    break;

                case EDescriptorType.dtGeoSpatial:
                    // Store raw descriptor for DDS replication
                    components.Add(desc.GeoSpatial);
                    // Convert to local Cartesian for CarKinem VehicleState
                    var cart = geoTransform.ToCartesian(desc.GeoSpatial.Pos);
                    components.Add(new VehicleState
                    {
                        Position   = new Vector2((float)cart.X, (float)cart.Y),
                        Forward    = HeadingToVector(desc.GeoSpatial.Rot.Heading),
                        Speed      = 0, SteerAngle = 0
                    });
                    break;

                default:
                    FdpLog.Warn($"[DescriptorMapper] Unhandled descriptor type: {desc._d}");
                    break;
            }
        }
        return components;
    }
}
```

**IG EntityMasterTranslator pattern (ingress → SpawnEntityCommand):**

```csharp
// In Bagira.IG.Translators.EntityMasterTranslator
public void OnReceived(EntityMaster sample, SampleInfo info, EntityRepository world)
{
    if (info.InstanceState == InstanceState.Disposed)
    {
        _eventBus.Publish(new DestroyEntityCommand
        {
            NetworkId = sample.EntityId,
            Reason    = "DDS EntityMaster DISPOSE"
        });
        return;
    }

    // Only spawn if not yet known
    if (_entityMap.TryGetEntity(sample.EntityId, out _)) return;

    _eventBus.Publish(new SpawnEntityCommand
    {
        NetworkId        = sample.EntityId,     // Non-zero: replicating a known network entity
        TkbType          = sample.TkbType,
        OwnerNodeId      = ResolveOwner(info),  // Remote node that sent this
        InitType         = ReliableInitType.None, // Ghost: no handshake needed; just spawn
        InitialComponents = new List<object> { sample } // EntityMaster as initial override
    });
}
```

---

## 8. Integration Patterns

### 8.1 SimHost (Authority Node)

SimHost processes `CreateEntityRequest` (DDS) and translates it to a `SpawnEntityCommand`. The toolkit handles all the rigorous setup.

```
DDS CreateEntityRequest
    → CreateEntityRequestTranslator (DDS → EventBus)
        → CreateEntityRequestSystem (consumes event, calls DescriptorMapper, publishes SpawnEntityCommand)
            → NetworkSpawningSystem (TKB + network infra + ELM — all handled here)
                → entity in ECS, NetworkGateway replicates to peers
```

**Benefits:** `CreateEntityRequestSystem` becomes very thin — no ECS creation code, no ELM calls.

### 8.2 IG (Replica Node)

IG receives `EntityMaster` DDS samples and translates them to `SpawnEntityCommand` events with `InitType = None` (ghost spawn, no ACK handshake).

```
DDS EntityMaster (from SimHost)
    → EntityMasterTranslator (DDS → SpawnEntityCommand on EventBus)
        → NetworkSpawningSystem (TKB + network infra + ELM.Constructing, no PendingAck)
            → ghost entity in ECS, rendered by Vis2D layer
```

### 8.3 NetworkDemo (Existing Example — Refactored)

NetworkDemo's `SpawnLocalEntities` helper is replaced by publishing `SpawnEntityCommand`:

```csharp
// OLD (NetworkDemoApp.cs SpawnLocalEntities):
// ~15 lines of manual world.CreateEntity + template.ApplyTo + SetComponent + ELM calls

// NEW:
_eventBus.Publish(new SpawnEntityCommand
{
    NetworkId        = 0, // Allocate new
    TkbType          = template.TkbType,
    OwnerNodeId      = instanceId,
    InitType         = ReliableInitType.AllPeers,
    InitialComponents = new List<object>
    {
        new NetworkSpawnRequest { DisType = 100, OwnerId = (ulong)instanceId },
        initialPosition // DemoPosition or equivalent
    }
});
```

---

## 9. Custom EntityMaster Support

The design supports domain-specific `EntityMaster` structs (e.g. `Bagira.BDC.SSTD.EntityMaster` with custom `Flags` field) without modification to the Toolkit:

1. Define your `EntityMaster` in your DataModel project (already done in `Bagira.DDS.DataModel`).
2. TKB template provides default `EntityMaster` values via `template.ApplyTo(...)`.
3. When creating via `SpawnEntityCommand`, include a new `EntityMaster` instance with custom fields in `InitialComponents`.
4. `EntityComponentReflector.SetComponent(world, entity, master)` overwrites the TKB default.

**Result:** The Toolkit is fully generic. Any struct type can be passed in `InitialComponents` and will be applied over the template default. The Toolkit has zero knowledge of `Bagira.DDS.DataModel` types.

---

## 10. Implementation Plan

### Phase NS1: FDP.Toolkit.NetworkSpawning Library

| Task | Description | Effort |
|------|-------------|--------|
| NS1.1 | Create `FDP.Toolkit.NetworkSpawning` project, set up folder structure | 0.25 days |
| NS1.2 | Define `SpawnEntityCommand`, `UpdateEntityCommand`, `DestroyEntityCommand` | 0.5 days |
| NS1.3 | Implement `EntityComponentReflector` (reflection-based `SetComponent`) | 0.5 days |
| NS1.4 | Implement `NetworkSpawningSystem` — spawn path | 1.0 day |
| NS1.5 | Implement `NetworkSpawningSystem` — update + destroy paths | 0.5 days |
| NS1.6 | Write unit tests (spawn, update, destroy, duplicate guard, unknown entity) | 1.0 day |

**Total:** ~4.0 days

### Phase NS2: NetworkDemo Integration (Validation)

| Task | Description | Effort |
|------|-------------|--------|
| NS2.1 | Add `FDP.Toolkit.NetworkSpawning` reference to `Fdp.Examples.NetworkDemo` | 0.1 days |
| NS2.2 | Refactor `SpawnLocalEntities` to publish `SpawnEntityCommand` | 0.5 days |
| NS2.3 | Register `NetworkSpawningSystem` in `NetworkDemoApp` module setup | 0.25 days |
| NS2.4 | Update `EntityMasterTranslator` (ingress) to publish `SpawnEntityCommand` | 0.5 days |
| NS2.5 | Run `LifecycleIntegrationTests` to confirm parity with old behaviour | 0.25 days |

**Total:** ~2.0 days

### Phase NS3: SimHost Integration

See [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) and [TASK-DETAILS-SIMHOST.md](./TASK-DETAILS-SIMHOST.md) Phase S2 for specific updates.

**Summary of SimHost changes:**
- Add `FDP.Toolkit.NetworkSpawning` project reference (S1.2)
- Implement `DescriptorMapper` (replaces manual `ApplyInitialDescriptors`, S2.5)
- Slim down `CreateEntityRequestSystem` to publish `SpawnEntityCommand` (S2.4)
- Register `NetworkSpawningSystem` in application shell (S5.1)

### Phase NS4: IG Integration

See [DESIGN-IG.md](./DESIGN-IG.md) and [TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md) Phase IG1 for specific updates.

**Summary of IG changes:**
- Add `FDP.Toolkit.NetworkSpawning` project reference (IG.1.1)
- Update `EntityMasterTranslator` to publish `SpawnEntityCommand` (IG.1.3)
- Register `NetworkSpawningSystem` in IG kernel setup (IG.1.3b)
