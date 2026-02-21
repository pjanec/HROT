# FDP.Toolkit.NetworkSpawning Implementation Tasks

**Version:** 1.0  
**Date:** 2026-02-21  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-NetworkSpawning.md](./DESIGN-NetworkSpawning.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document provides **detailed task breakdown** for implementing the `FDP.Toolkit.NetworkSpawning` toolkit. This toolkit unifies entity creation across all distributed FDP nodes (SimHost, IG, NetworkDemo) by centralising the rigorous spawn sequence (TKB, network infra, ELM) into a single `NetworkSpawningSystem` driven by `SpawnEntityCommand` events.

**Total Effort:** ~4.0 developer-days

**Toolkit Location:** `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/`

**Key Insight:** All existing node-specific spawn helpers (`SpawnLocalEntities`, manual `CreateEntityRequestSystem`, manual `EntityMasterTranslator` construction) are replaced or simplified by this toolkit. The Toolkit has **zero dependency on `Bagira.DDS.DataModel`** — it is generic.

---

## Phase NS1: Library Creation (4 days)

### Task NS1.1: Create FDP.Toolkit.NetworkSpawning Project

**Goal:** Create the C# class library project with correct folder structure and project references.

**Steps:**

1. Create new class library:
   ```
   dotnet new classlib -n FDP.Toolkit.NetworkSpawning -f net8.0
   Location: FDP/Toolkits/FDP.Toolkit.NetworkSpawning/
   ```

2. Add to `FDP/FDP.sln`:
   ```
   dotnet sln FDP/FDP.sln add FDP/Toolkits/FDP.Toolkit.NetworkSpawning/FDP.Toolkit.NetworkSpawning.csproj
   ```

3. Add project references:
   ```xml
   <!-- FDP.Toolkit.NetworkSpawning.csproj -->
   <ItemGroup>
     <ProjectReference Include="..\..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
     <ProjectReference Include="..\FDP.Toolkit.Lifecycle\FDP.Toolkit.Lifecycle.csproj" />
     <ProjectReference Include="..\FDP.Toolkit.Replication\FDP.Toolkit.Replication.csproj" />
     <ProjectReference Include="..\FDP.Toolkit.Tkb\FDP.Toolkit.Tkb.csproj" />
     <ProjectReference Include="..\..\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
     <ProjectReference Include="..\..\ModuleHost\ModuleHost.Network.Cyclone\ModuleHost.Network.Cyclone.csproj" />
   </ItemGroup>
   ```

4. Create folder structure:
   ```
   FDP.Toolkit.NetworkSpawning/
     Events/
       SpawnEntityCommand.cs
       UpdateEntityCommand.cs
       DestroyEntityCommand.cs
     Systems/
       NetworkSpawningSystem.cs
     EntityComponentReflector.cs
     README.md
   ```

5. Create companion test project:
   ```
   dotnet new mstest -n FDP.Toolkit.NetworkSpawning.Tests -f net8.0
   Location: FDP/Toolkits/FDP.Toolkit.NetworkSpawning.Tests/
   dotnet sln FDP/FDP.sln add FDP/Toolkits/FDP.Toolkit.NetworkSpawning.Tests/...
   ```

**Acceptance Criteria:**
- ✅ Project compiles with zero errors
- ✅ Project added to FDP.sln
- ✅ All project references resolve
- ✅ Test project created and added to solution
- ✅ Folder structure in place

**Estimated Effort:** 0.25 days

**Dependencies:** None

---

### Task NS1.2: Define Spawn Command Events

**Goal:** Define the three event structs that constitute the Toolkit's public API.

**Implementation:**

Create `Events/SpawnEntityCommand.cs`:
```csharp
using System;
using System.Collections.Generic;
using ModuleHost.Core.Network.Interfaces;

namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to spawn an entity in the local ECS world.
    /// Published to FdpEventBus by any code that wants to create an entity.
    /// Consumed exclusively by <see cref="Systems.NetworkSpawningSystem"/>.
    ///
    /// Design: Decouples the "what to create" from the "how to set it up properly".
    /// All entity creation — local (AI/script) and network-triggered (DDS ingress) —
    /// flows through this command so that lifecycle, networking, and TKB setup are
    /// applied identically in all cases.
    /// </summary>
    public struct SpawnEntityCommand
    {
        /// <summary>
        /// Network entity ID. 0 = new entity (allocate fresh ID). Non-zero = replicate existing.
        /// </summary>
        public long NetworkId;

        /// <summary>TKB template type. Must be registered in TkbDatabase.</summary>
        public long TkbType;

        /// <summary>Node ID of the entity's owner/authority.</summary>
        public int OwnerNodeId;

        /// <summary>
        /// Lifecycle handshake mode.
        /// <list type="bullet">
        ///   <item><see cref="ReliableInitType.AllPeers"/>: Entity stays Constructing until all peers ACK.
        ///         Use on authority nodes (e.g. SimHost).</item>
        ///   <item><see cref="ReliableInitType.None"/>: No ACK required, entity transitions immediately.
        ///         Use for ghost/replica nodes (e.g. IG receiving from SimHost).</item>
        /// </list>
        /// </summary>
        public ReliableInitType InitType;

        /// <summary>
        /// Optional component overrides applied on top of TKB template defaults.
        /// Each item is an ECS component struct (e.g. EntityMaster, GeoSpatial, VehicleState).
        /// The runtime type of each item is used to call world.SetComponent dynamically.
        /// </summary>
        public List<object> InitialComponents;

        /// <summary>Optional correlation ID for request/ACK tracking.</summary>
        public Guid RequestId;
    }
}
```

Create `Events/UpdateEntityCommand.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to update one or more components on an existing network entity.
    /// Bridged from DDS <c>UpdateEntityDescriptorRequest</c> by the relevant ingress handler.
    /// </summary>
    public struct UpdateEntityCommand
    {
        /// <summary>Network entity ID (must be registered in NetworkEntityMap).</summary>
        public long NetworkId;

        /// <summary>Component instances to apply. Uses same reflective path as SpawnEntityCommand.</summary>
        public List<object> ComponentsToUpdate;

        /// <summary>Optional correlation ID.</summary>
        public Guid RequestId;
    }
}
```

Create `Events/DestroyEntityCommand.cs`:
```csharp
using System;

namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to destroy a network entity via proper ELM lifecycle teardown.
    /// Publish this event instead of calling elm.BeginDestruction() directly.
    /// </summary>
    public struct DestroyEntityCommand
    {
        /// <summary>Network entity ID (must be registered in NetworkEntityMap).</summary>
        public long NetworkId;

        /// <summary>Human-readable reason (logged for diagnostics).</summary>
        public string Reason;
    }
}
```

**Acceptance Criteria:**
- ✅ All three event structs compile
- ✅ XML documentation complete on all public members
- ✅ `SpawnEntityCommand` includes all fields described in [DESIGN-NetworkSpawning.md §4](./DESIGN-NetworkSpawning.md#4-events-spawn-commands)
- ✅ `ReliableInitType` reference resolves from `ModuleHost.Core.Network.Interfaces`

**Estimated Effort:** 0.5 days

**Dependencies:** NS1.1

---

### Task NS1.3: Implement EntityComponentReflector

**Goal:** Create a static helper that applies any component instance to an entity by its runtime type.

**Design Note:** The `EntityRepository` in `Fdp.Kernel` already contains an internal `SetComponent(Entity, Type, object)` overload used by the ImGui Inspector toolkit. `EntityComponentReflector` exposes this capability in a clean, documented way for use in slow-path operations only.

**Implementation:**

Create `EntityComponentReflector.cs`:
```csharp
using System;
using Fdp.Kernel;

namespace FDP.Toolkit.NetworkSpawning
{
    /// <summary>
    /// Utility for applying component instances to ECS entities using runtime type dispatch.
    /// Intended for "slow-path" operations only (entity spawn, descriptor update) —
    /// never use in tight per-frame physics loops.
    ///
    /// This enables callers to pass heterogeneous lists of component values
    /// (<see cref="Events.SpawnEntityCommand.InitialComponents"/>) without needing
    /// compile-time knowledge of each component type.
    /// </summary>
    public static class EntityComponentReflector
    {
        /// <summary>
        /// Calls <c>world.SetComponent(entity, component.GetType(), component)</c>.
        /// This creates the component if it does not exist, or overwrites the existing value.
        /// Null components are silently ignored.
        /// </summary>
        /// <param name="world">The entity repository.</param>
        /// <param name="entity">The target entity handle.</param>
        /// <param name="component">
        /// Any ECS component struct or class instance. Must not be null.
        /// The runtime type is used to dispatch to the correct typed SetComponent overload.
        /// </param>
        public static void SetComponent(EntityRepository world, Entity entity, object component)
        {
            if (component == null) return;
            world.SetComponent(entity, component.GetType(), component);
        }
    }
}
```

**Unit Tests** — `FDP.Toolkit.NetworkSpawning.Tests/EntityComponentReflectorTests.cs`:

```csharp
[TestClass]
public class EntityComponentReflectorTests
{
    [TestMethod]
    public void SetComponent_NewComponent_AddsSuccessfully()
    {
        // Arrange
        var world = FdpTestWorld.Create();
        var entity = world.CreateEntity();
        var comp = new TestComponentA { Value = 42 };

        // Act
        EntityComponentReflector.SetComponent(world, entity, comp);

        // Assert
        Assert.IsTrue(world.HasComponent<TestComponentA>(entity));
        Assert.AreEqual(42, world.GetComponent<TestComponentA>(entity).Value);
    }

    [TestMethod]
    public void SetComponent_ExistingComponent_Overwrites()
    {
        // Arrange
        var world = FdpTestWorld.Create();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponentA { Value = 1 });

        // Act
        EntityComponentReflector.SetComponent(world, entity, new TestComponentA { Value = 99 });

        // Assert
        Assert.AreEqual(99, world.GetComponent<TestComponentA>(entity).Value);
    }

    [TestMethod]
    public void SetComponent_NullComponent_DoesNotThrow()
    {
        // Arrange
        var world = FdpTestWorld.Create();
        var entity = world.CreateEntity();

        // Act + Assert (no exception)
        EntityComponentReflector.SetComponent(world, entity, null);
    }

    [TestMethod]
    public void SetComponent_MultipleTypes_AllApplied()
    {
        // Arrange
        var world = FdpTestWorld.Create();
        var entity = world.CreateEntity();
        var items = new List<object>
        {
            new TestComponentA { Value = 10 },
            new TestComponentB { Name  = "alpha" }
        };

        // Act
        foreach (var c in items)
            EntityComponentReflector.SetComponent(world, entity, c);

        // Assert
        Assert.AreEqual(10,      world.GetComponent<TestComponentA>(entity).Value);
        Assert.AreEqual("alpha", world.GetComponent<TestComponentB>(entity).Name);
    }
}
```

**Acceptance Criteria:**
- ✅ Adds a new component when the component does not yet exist
- ✅ Overwrites an existing component (uses `SetComponent`, not `AddComponent`)
- ✅ Null input is silently ignored (no exception)
- ✅ Multiple heterogeneous types handled correctly
- ✅ All four unit tests pass

**Estimated Effort:** 0.5 days

**Dependencies:** NS1.1

---

### Task NS1.4: Implement NetworkSpawningSystem — Spawn Path

**Goal:** Implement the `SpawnEntityCommand` processing logic that replaces all manual spawn helpers.

**Design Reference:** [DESIGN-NetworkSpawning.md §5](./DESIGN-NetworkSpawning.md#5-networkspawningsystem)

**Architecture Notes:**
- The system implements `IModuleSystem` (not `ComponentSystem`) to fit the Module Host pattern.
- ID allocation via `DdsIdAllocator.Allocate()` is used here synchronously. If the allocator requires async (block-buffered IDs), it is acceptable to pre-warm the buffer on startup so synchronous `Allocate()` does not block.
- The system sets the full network infra stack (`NetworkIdentity`, `NetworkOwnership`, `NetworkAuthority`, `NetworkSpawnRequest`, and optionally `PendingNetworkAck`) matching the exact sequence from `NetworkDemoApp.SpawnLocalEntities`.
- **CRITICAL:** `_elm.BeginConstruction(...)` is called LAST, after all components are set. Do NOT set `ConstructingTag` manually.

**Implementation:**

Create `Systems/NetworkSpawningSystem.cs`:
```csharp
using System;
using System.Collections.Generic;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Network.Cyclone;
using Fdp.Kernel;

namespace FDP.Toolkit.NetworkSpawning.Systems
{
    /// <summary>
    /// Centralised system for entity lifecycle management.
    /// Consumes <see cref="SpawnEntityCommand"/>, <see cref="UpdateEntityCommand"/>,
    /// and <see cref="DestroyEntityCommand"/> events from the FdpEventBus and performs
    /// the rigorous FDP entity setup (TKB, NetworkIdentity, NetworkOwnership,
    /// NetworkAuthority, NetworkSpawnRequest, PendingNetworkAck, ELM.BeginConstruction).
    ///
    /// CRITICAL invariants:
    /// - All component setup MUST happen before elm.BeginConstruction is called.
    /// - Do NOT set ConstructingTag manually; ELM manages lifecycle state internally.
    /// - PendingNetworkAck MUST be added before BeginConstruction for AllPeers init.
    /// </summary>
    public class NetworkSpawningSystem : IModuleSystem
    {
        private readonly TkbDatabase _tkb;
        private readonly EntityLifecycleModule _elm;
        private readonly NetworkEntityMap _entityMap;
        private readonly DdsIdAllocator _idAllocator;
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;
        private readonly DisTypeExtractor? _disTypeExtractor;

        public NetworkSpawningSystem(
            TkbDatabase tkb,
            EntityLifecycleModule elm,
            NetworkEntityMap entityMap,
            DdsIdAllocator idAllocator,
            FdpEventBus eventBus,
            int localNodeId,
            DisTypeExtractor? disTypeExtractor = null) // optional — null → DisType returns 0
        {
            _tkb             = tkb ?? throw new ArgumentNullException(nameof(tkb));
            _elm             = elm ?? throw new ArgumentNullException(nameof(elm));
            _entityMap       = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _idAllocator     = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _eventBus        = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId     = localNodeId;
            _disTypeExtractor = disTypeExtractor;
        }

        public void Execute(ISimulationView view, float dt)
        {
            var world = view.World;

            foreach (var cmd in _eventBus.ConsumeEvents<SpawnEntityCommand>())
                ExecuteSpawn(world, cmd);

            foreach (var cmd in _eventBus.ConsumeEvents<UpdateEntityCommand>())
                ExecuteUpdate(world, cmd);

            foreach (var cmd in _eventBus.ConsumeEvents<DestroyEntityCommand>())
                ExecuteDestroy(world, cmd);
        }

        // ── Spawn ─────────────────────────────────────────────────────────────────

        private void ExecuteSpawn(EntityRepository world, SpawnEntityCommand cmd)
        {
            // A. ID Management
            long netId = cmd.NetworkId;
            if (netId == 0)
                netId = _idAllocator.Allocate(); // Uses pre-warmed synchronous allocation

            // Guard: duplicate spawn
            if (_entityMap.TryGetEntity(netId, out _))
            {
                FdpLog.Warn($"[NetworkSpawning] Duplicate spawn for entity {netId} ignored.");
                return;
            }

            // B. Template Lookup
            if (!_tkb.TryGetByType(cmd.TkbType, out var template))
            {
                FdpLog.Error($"[NetworkSpawning] TkbType {cmd.TkbType} not found — spawn aborted.");
                return;
            }

            // C. Create ECS entity and apply TKB template defaults
            var entity = world.CreateEntity();
            template.ApplyTo(world, entity);

            // D. Apply caller-provided component overrides (Position, EntityMaster, etc.)
            if (cmd.InitialComponents != null)
                foreach (var comp in cmd.InitialComponents)
                    EntityComponentReflector.SetComponent(world, entity, comp);

            // E. Network infrastructure — exact sequence from NetworkDemoApp.SpawnLocalEntities
            world.SetComponent(entity, new NetworkIdentity { Value = netId });

            // Template already adds NetworkOwnership with default values; SET (overwrite).
            world.SetComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = cmd.OwnerNodeId,
                LocalNodeId    = _localNodeId
            });

            // NetworkAuthority: required for ReplayBridge compatibility
            world.AddComponent(entity, new NetworkAuthority(cmd.OwnerNodeId, _localNodeId));

            // NetworkSpawnRequest: signals NetworkGatewaySystem to replicate to peers
            world.AddComponent(entity, new NetworkSpawnRequest
            {
                DisType = ExtractDisType(cmd.InitialComponents),
                OwnerId = (ulong)cmd.OwnerNodeId
            });

            // F. Reliable Init: add PendingNetworkAck BEFORE BeginConstruction
            if (cmd.InitType != ReliableInitType.None)
            {
                world.AddComponent(entity, new PendingNetworkAck
                {
                    ExpectedType = cmd.InitType
                });
            }

            // G. Register in entity map (before BeginConstruction so gateway can find it)
            _entityMap.Register(netId, entity);

            // H. [CRITICAL] Begin ELM construction — LAST step.
            //    Fires ConstructionOrder → NetworkGatewaySystem → DDS replication.
            //    Do NOT call world.SetComponent(entity, new ConstructingTag()); ELM owns state.
            var cmdBuffer = world.GetCommandBuffer();
            _elm.BeginConstruction(entity, cmd.TkbType, world.GlobalVersion, cmdBuffer);

            FdpLog.Info(
                $"[NetworkSpawning] Spawned entity {netId} " +
                $"(TkbType={cmd.TkbType}, Owner={cmd.OwnerNodeId}, InitType={cmd.InitType})");
        }

        // ── Update ────────────────────────────────────────────────────────────────

        private void ExecuteUpdate(EntityRepository world, UpdateEntityCommand cmd)
        {
            if (!_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                FdpLog.Warn($"[NetworkSpawning] UpdateEntityCommand for unknown entity {cmd.NetworkId}.");
                return;
            }

            if (cmd.ComponentsToUpdate != null)
                foreach (var comp in cmd.ComponentsToUpdate)
                    EntityComponentReflector.SetComponent(world, entity, comp);
        }

        // ── Destroy ───────────────────────────────────────────────────────────────

        private void ExecuteDestroy(EntityRepository world, DestroyEntityCommand cmd)
        {
            if (!_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                FdpLog.Warn($"[NetworkSpawning] DestroyEntityCommand for unknown entity {cmd.NetworkId}.");
                return;
            }

            var cmdBuffer = world.GetCommandBuffer();
            _elm.BeginDestruction(entity, world.GlobalVersion, cmd.Reason, cmdBuffer);

            FdpLog.Info($"[NetworkSpawning] Destroyed entity {cmd.NetworkId} ({cmd.Reason})");
        }

        private ulong ExtractDisType(List<object> components)
        {
            // Delegate-based extraction: keeps the Toolkit free of Bagira.DDS.DataModel.
            // The DisTypeExtractor delegate is injected by the calling application.
            if (components == null || _disTypeExtractor == null) return 0;
            foreach (var comp in components)
                if (_disTypeExtractor(comp, out ulong dis))
                    return dis;
            return 0;
        }
    }
}
```

**Unit Tests** — Spawn path only (see NS1.5 for update/destroy):

```csharp
[TestClass]
public class NetworkSpawningSystemSpawnTests
{
    [TestMethod]
    public void SpawnEntityCommand_NewId_CreatesEntityAndRegisters()
    {
        // Arrange
        var world = FdpTestWorld.Create();
        var tkb   = BuildTkbWith(tkbType: 100, "TestTank");
        var elm   = new EntityLifecycleModule(tkb, peerIds: Array.Empty<int>());
        var map   = new NetworkEntityMap();
        var alloc = new StubIdAllocator(startId: 1000);
        var bus   = new FdpEventBus();

        var system = new NetworkSpawningSystem(tkb, elm, map, alloc, bus, localNodeId: 10);

        // Act
        bus.Publish(new SpawnEntityCommand
        {
            NetworkId  = 0, // allocate new
            TkbType    = 100,
            OwnerNodeId = 10,
            InitType   = ReliableInitType.AllPeers
        });
        system.Execute(new StubSimulationView(world), 0.016f);

        // Assert
        Assert.IsTrue(map.TryGetEntity(1000L, out var entity));
        Assert.IsTrue(world.HasComponent<NetworkIdentity>(entity));
        Assert.AreEqual(1000L, world.GetComponent<NetworkIdentity>(entity).Value);
        Assert.IsTrue(world.HasComponent<PendingNetworkAck>(entity));
    }

    [TestMethod]
    public void SpawnEntityCommand_NonZeroId_UsesProvidedId()
    {
        // Same setup, but NetworkId = 5555. Should use 5555, skip allocation.
        // Assert entityMap contains 5555.
    }

    [TestMethod]
    public void SpawnEntityCommand_DuplicateId_IsIgnored()
    {
        // Pre-register entity 999. Publish SpawnEntityCommand with NetworkId=999.
        // Second spawn should be ignored (warn log, no duplicate entity).
    }

    [TestMethod]
    public void SpawnEntityCommand_UnknownTkbType_IsAborted()
    {
        // TkbType = 99999 (not registered) → entity NOT created, no entity in map.
    }

    [TestMethod]
    public void SpawnEntityCommand_InitTypeNone_NoPendingAck()
    {
        // InitType = None → PendingNetworkAck NOT added to entity.
    }

    [TestMethod]
    public void SpawnEntityCommand_InitialComponents_OverrideTemplateDefaults()
    {
        // Template sets EntityMaster.Flags = 0.
        // InitialComponents contains EntityMaster { Flags = 7 }.
        // After spawn, entity.EntityMaster.Flags == 7.
    }
}
```

**Acceptance Criteria:**
- ✅ SpawnEntityCommand with `NetworkId = 0` allocates a new ID via `_idAllocator`
- ✅ SpawnEntityCommand with non-zero `NetworkId` uses provided ID (no allocation)
- ✅ Duplicate ID is silently warned and ignored (no second entity created)
- ✅ Unknown `TkbType` is logged as error and spawn is aborted
- ✅ `template.ApplyTo(world, entity)` is called before component overrides
- ✅ `NetworkIdentity`, `NetworkOwnership`, `NetworkAuthority`, `NetworkSpawnRequest` set
- ✅ `PendingNetworkAck` added when `InitType != None`
- ✅ `PendingNetworkAck` NOT added when `InitType == None`
- ✅ `_entityMap.Register` called before `elm.BeginConstruction`
- ✅ `elm.BeginConstruction` called as the last step
- ✅ `InitialComponents` overrides TKB template defaults
- ✅ All six unit tests pass

**Estimated Effort:** 1.0 day

**Dependencies:** NS1.1, NS1.2, NS1.3

---

### Task NS1.5: Implement NetworkSpawningSystem — Update & Destroy Paths

**Goal:** Implement `UpdateEntityCommand` and `DestroyEntityCommand` processing.

**Note:** The implementations can live in the same `NetworkSpawningSystem.cs` file from NS1.4. This task covers the unit tests and acceptance validation for those two paths.

**Unit Tests:**

```csharp
[TestClass]
public class NetworkSpawningSystemUpdateDestroyTests
{
    // ── Update ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void UpdateEntityCommand_KnownEntity_OverwritesComponent()
    {
        // Arrange: Spawn entity 200; entity has TestComponent { Value = 5 }.
        // Act: Publish UpdateEntityCommand { NetworkId=200, ComponentsToUpdate=[TestComponent{Value=99}] }
        // Assert: entity now has TestComponent { Value = 99 }
    }

    [TestMethod]
    public void UpdateEntityCommand_UnknownEntity_LogsWarning_NoException()
    {
        // Act: Publish UpdateEntityCommand for NetworkId=999 (never spawned).
        // Assert: No exception thrown; warn logged.
    }

    [TestMethod]
    public void UpdateEntityCommand_NullComponents_DoesNotThrow()
    {
        // Act: Publish UpdateEntityCommand { NetworkId=200, ComponentsToUpdate=null }
        // Assert: No exception.
    }

    // ── Destroy ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void DestroyEntityCommand_KnownEntity_CallsBeginDestruction()
    {
        // Arrange: Spawn entity 300; entity is Active.
        // Act: Publish DestroyEntityCommand { NetworkId=300, Reason="test" }.
        // Assert: elm.BeginDestruction was called; entity transitions to TearDown.
    }

    [TestMethod]
    public void DestroyEntityCommand_UnknownEntity_LogsWarning_NoException()
    {
        // Act: Publish DestroyEntityCommand for NetworkId=888 (never spawned).
        // Assert: No exception thrown; warn logged.
    }
}
```

**Acceptance Criteria:**
- ✅ `UpdateEntityCommand` for known entity overwrites specified components
- ✅ `UpdateEntityCommand` for unknown entity logs warning without throwing
- ✅ `UpdateEntityCommand` with null `ComponentsToUpdate` is safe
- ✅ `DestroyEntityCommand` for known entity calls `elm.BeginDestruction`
- ✅ `DestroyEntityCommand` for unknown entity logs warning without throwing
- ✅ All five unit tests pass

**Estimated Effort:** 0.5 days

**Dependencies:** NS1.4

---

### Task NS1.6: Write Integration Test

**Goal:** Verify that a complete spawn-update-destroy lifecycle works end-to-end through the Toolkit without a live DDS instance.

**Implementation:**

Create `FDP.Toolkit.NetworkSpawning.Tests/NetworkSpawningLifecycleTests.cs`:

```csharp
[TestClass]
public class NetworkSpawningLifecycleTests
{
    [TestMethod]
    public void FullLifecycle_SpawnUpdateDestroy_WorksCorrectly()
    {
        // 1. Setup: world, TKB with "TestTank" (TkbType=100), ELM, EntityMap, EventBus
        var world = FdpTestWorld.Create();
        var tkb   = BuildTkbWith(tkbType: 100, "TestTank");
        var elm   = new EntityLifecycleModule(tkb, peerIds: Array.Empty<int>());
        var map   = new NetworkEntityMap();
        var alloc = new StubIdAllocator(startId: 500);
        var bus   = new FdpEventBus();
        var sys   = new NetworkSpawningSystem(tkb, elm, map, alloc, bus, localNodeId: 1);

        // 2. Spawn
        bus.Publish(new SpawnEntityCommand
        {
            NetworkId  = 0,
            TkbType    = 100,
            OwnerNodeId = 1,
            InitType   = ReliableInitType.None,
            InitialComponents = new List<object>
            {
                new TestPositionComponent { X = 10.0f, Y = 20.0f }
            }
        });
        sys.Execute(new StubSimulationView(world), 0.016f);

        Assert.IsTrue(map.TryGetEntity(500L, out var entity));
        Assert.AreEqual(10.0f, world.GetComponent<TestPositionComponent>(entity).X);

        // 3. Update
        bus.Publish(new UpdateEntityCommand
        {
            NetworkId = 500,
            ComponentsToUpdate = new List<object>
            {
                new TestPositionComponent { X = 99.0f, Y = 88.0f }
            }
        });
        sys.Execute(new StubSimulationView(world), 0.016f);

        Assert.AreEqual(99.0f, world.GetComponent<TestPositionComponent>(entity).X);

        // 4. Destroy
        bus.Publish(new DestroyEntityCommand { NetworkId = 500, Reason = "test end" });
        sys.Execute(new StubSimulationView(world), 0.016f);

        // Entity in TearDown state (not yet disposed, ELM gracefulness applies)
        var lifecycle = world.GetComponent<LifecycleState>(entity);
        Assert.AreEqual(LifecyclePhase.TearDown, lifecycle.Phase);
    }
}
```

**Acceptance Criteria:**
- ✅ Spawn creates entity with correct components and ELM state
- ✅ Update overwrites specifically requested components (others unchanged)
- ✅ Destroy transitions entity to TearDown via ELM
- ✅ No dependencies on live DDS instance (all stubs)
- ✅ Integration test passes in CI

**Estimated Effort:** 1.0 day

**Dependencies:** NS1.4, NS1.5
