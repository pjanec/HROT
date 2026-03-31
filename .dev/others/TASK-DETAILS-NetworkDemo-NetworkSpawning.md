# NetworkDemo NetworkSpawning Integration Tasks

**Version:** 1.0  
**Date:** 2026-02-21  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-NetworkSpawning.md](./DESIGN-NetworkSpawning.md) | [TASK-DETAILS-NetworkSpawning.md](./TASK-DETAILS-NetworkSpawning.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document covers the integration of `FDP.Toolkit.NetworkSpawning` into the `Fdp.Examples.NetworkDemo` project. The NetworkDemo serves as the **validation vehicle** for the Toolkit: by refactoring its existing `SpawnLocalEntities` helper and `EntityMasterTranslator` ingress logic to use the new Toolkit, we verify that the Toolkit works correctly before applying the same patterns to SimHost and IG.

**Goal:** All existing `LifecycleIntegrationTests` must continue to pass after refactoring.

**Reference Project:** `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`

**Total Effort:** ~2.0 developer-days

---

## Phase NS2: NetworkDemo Integration (2 days)

### Task NS2.1: Add FDP.Toolkit.NetworkSpawning Reference

**Goal:** Add the new Toolkit as a project reference to `Fdp.Examples.NetworkDemo`.

**Steps:**

1. Add project reference to `Fdp.Examples.NetworkDemo.csproj`:
   ```xml
   <ProjectReference Include="..\..\..\Toolkits\FDP.Toolkit.NetworkSpawning\FDP.Toolkit.NetworkSpawning.csproj" />
   ```

2. Add using directives to `NetworkDemoApp.cs`:
   ```csharp
   using FDP.Toolkit.NetworkSpawning.Events;
   using FDP.Toolkit.NetworkSpawning.Systems;
   ```

3. Build the solution and confirm zero new errors.

**Acceptance Criteria:**
- ✅ `Fdp.Examples.NetworkDemo.csproj` references `FDP.Toolkit.NetworkSpawning`
- ✅ `dotnet build FDP/FDP.sln` succeeds with zero errors
- ✅ No circular project references introduced

**Estimated Effort:** 0.1 days

**Dependencies:** NS1.6 (Toolkit fully implemented)

---

### Task NS2.2: Register NetworkSpawningSystem in NetworkDemoApp

**Goal:** Add `NetworkSpawningSystem` to the module/system registration in `NetworkDemoApp.InitializeAsync`.

**Context:** In `NetworkDemoApp`, systems are registered inside named modules (e.g., `GameLogicModule`). `NetworkSpawningSystem` can be registered either as a standalone `IModuleSystem` inside a new thin `SpawningModule`, or directly via a wrapper if the kernel supports it.

**Implementation:**

In `NetworkDemoApp.InitializeAsync`, after the ELM and Replication modules are registered (around section "C. Application Modules"), add:

```csharp
// Register NetworkSpawningSystem from FDP.Toolkit.NetworkSpawning
// Must be registered AFTER elm and BEFORE modules that publish SpawnEntityCommand events.
var spawningSystem = new NetworkSpawningSystem(
    tkb,
    elm,
    EntityMap,
    idAllocator,           // May be null in tests; guard if needed
    eventBus,
    localInternalId,
    // DisTypeExtractor: extracts DIS type from EntityMaster without coupling Toolkit to BDC
    (object c, out ulong dis) => {
        if (c is Hrot.NED.Descriptors.EntityMaster m) { dis = m.DisType; return true; }
        dis = 0; return false;
    }
);

// Wrap in a minimal IModule (like other systems that have no module logic):
Kernel.RegisterModule(new SpawningModule(spawningSystem));
```

Create `Modules/SpawningModule.cs` (thin wrapper):
```csharp
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.NetworkSpawning.Systems;

namespace Fdp.Examples.NetworkDemo.Modules
{
    /// <summary>
    /// Thin module wrapper hosting <see cref="NetworkSpawningSystem"/>.
    /// Runs synchronously in the main simulation loop.
    /// </summary>
    public class SpawningModule : IModule
    {
        public string Name => "NetworkSpawning";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem _system;

        public SpawningModule(NetworkSpawningSystem system)
            => _system = system;

        public void RegisterSystems(ISystemRegistry registry)
            => registry.RegisterSystem(_system);

        public void Tick(ISimulationView view, float dt) { }
    }
}
```

**Acceptance Criteria:**
- ✅ `SpawningModule` compiles and registered in `Kernel`
- ✅ `NetworkSpawningSystem` is executed every frame
- ✅ `SpawningModule` appears in kernel module list (debug print)
- ✅ No double-registration of ELM or other systems

**Estimated Effort:** 0.25 days

**Dependencies:** NS2.1

---

### Task NS2.3: Refactor SpawnLocalEntities to Use SpawnEntityCommand

**Goal:** Replace the manual ~20-line `SpawnLocalEntities` method body with publishing a `SpawnEntityCommand` event. The Toolkit's `NetworkSpawningSystem` handles the rest.

**Reference Code:** `NetworkDemoApp.cs` lines 444–504 (`SpawnLocalEntities` static method).

**Before (current implementation):**
```csharp
static void SpawnLocalEntities(EntityRepository world, TkbDatabase tkb, int instanceId,
    int localInternalId, NetworkEntityMap entityMap, EntityLifecycleModule elm, IEntityCommandBuffer cmd)
{
    if (tkb.TryGetByName("CommandTank", out var template))
    {
        for (int i = 0; i < 1; i++)
        {
            var entity = world.CreateEntity();
            world.SetLifecycleState(entity, EntityLifecycle.Constructing); // ← incorrect direct tag
            template.ApplyTo(world, entity);
            var netId = (long)instanceId * 1000 + entity.Index;
            world.SetComponent(entity, new NetworkIdentity { Value = netId });
            entityMap.Register(netId, entity);
            world.SetComponent(entity, new NetworkOwnership { PrimaryOwnerId = localInternalId, LocalNodeId = localInternalId });
            world.AddComponent(entity, new NetworkAuthority(localInternalId, localInternalId));
            world.AddComponent(entity, new NetworkSpawnRequest { DisType = 100, OwnerId = (ulong)localInternalId });
            world.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });
            elm.BeginConstruction(entity, template.TkbType, world.GlobalVersion, cmd);
            world.SetComponent(entity, new DemoPosition { Value = new Vector3(...) });
            world.SetComponent(entity, new NetworkPosition { Value = Vector3.Zero });
            world.AddComponent(entity, new EntityType { Name = "Tank", TypeId = 1 });
        }
    }
}
```

**After (new implementation):**
```csharp
// In NetworkDemoApp.InitializeAsync, replace the SpawnLocalEntities call:
if (autoSpawn && !isReplay)
{
    SpawnLocalEntities(eventBus, tkb, instanceId, localInternalId);
}

// Replace SpawnLocalEntities method:
private void SpawnLocalEntities(FdpEventBus eventBus, TkbDatabase tkb,
    int instanceId, int localInternalId)
{
    if (!tkb.TryGetByName("CommandTank", out var template))
    {
        FdpLog<NetworkDemoApp>.Warn("[SpawnLocal] CommandTank template not found.");
        return;
    }

    // Pre-compute a deterministic network ID (same logic as before for test compatibility).
    // NetworkId = instanceId * 1000 + 0 (single tank per node).
    // NOTE: If NetworkId == 0 is desired to trigger allocation, use 0 here instead.
    long netId = (long)instanceId * 1000;

    eventBus.Publish(new SpawnEntityCommand
    {
        NetworkId         = netId,   // deterministic; use 0 to allocate dynamically
        TkbType           = template.TkbType,
        OwnerNodeId       = localInternalId,
        InitType          = ReliableInitType.AllPeers,
        InitialComponents = new List<object>
        {
            // Position override (on top of TKB default)
            new DemoPosition
            {
                Value = new Vector3(
                    Random.Shared.Next(-50, 50),
                    Random.Shared.Next(-50, 50),
                    0)
            },
            new NetworkPosition  { Value = Vector3.Zero },
            new EntityType        { Name = "Tank", TypeId = 1 },
            // DisType/OwnerId carried via NetworkSpawnRequest in Toolkit automatically for owner.
        }
    });

    FdpLog<NetworkDemoApp>.Info($"[SPAWN] Published SpawnEntityCommand for TkbType={template.TkbType}");
}
```

> ⚠️ **Architecture note:** The old `SpawnLocalEntities` received a `IEntityCommandBuffer cmd` parameter and called `cmd.Playback(World)` after the call site. The new version uses `FdpEventBus.Publish` — the `NetworkSpawningSystem` (registered in NS2.2) processes the command on the next frame. Remove the `cmd.Playback(World)` call at the call site.

**Acceptance Criteria:**
- ✅ `SpawnLocalEntities` no longer calls `world.CreateEntity()` directly
- ✅ No manual `elm.BeginConstruction` call in `SpawnLocalEntities`
- ✅ No manual `PendingNetworkAck` setup in `SpawnLocalEntities`
- ✅ No `world.SetLifecycleState(entity, EntityLifecycle.Constructing)` call (was incorrect)
- ✅ `SpawnEntityCommand` published to `eventBus`
- ✅ Entities still appear on both nodes after spawn (same observable behaviour as before)

**Estimated Effort:** 0.5 days

**Dependencies:** NS2.2

---

### Task NS2.4: Update EntityMasterTranslator Ingress to Use SpawnEntityCommand

**Goal:** The `EntityMasterTranslator` (or equivalent ingress translator) currently creates entities directly in the ECS when it receives `EntityMaster` DDS samples. Update it to publish `SpawnEntityCommand` instead.

**Context:** NetworkDemo uses `ReplicationBootstrap.CreateAutoTranslators` for auto-generated translators. The `EntityMaster` type (`NetworkVelocity` in NetworkDemo's descriptor scheme) uses an auto-translator. However, any manual ingress translator that calls `entityMap.GetOrCreate(...)` or `elm.BeginConstruction(...)` directly must be updated.

> **Note:** If NetworkDemo currently relies entirely on `AutoCycloneTranslator` for `EntityMaster`-equivalent ingress and that translator already calls `elm.BeginConstruction` via the ELM module integration, this task may require only verifying that the auto-translator path is consistent. Check whether `ReplicationBootstrap` auto-translators publish to EventBus or call ELM directly, and bridge via `SpawnEntityCommand` if they call ELM directly.

**Steps:**

1. Identify which translators in NetworkDemo handle new entity creation on the ingress (receiving) side.
   - Check `FastGeodeticTranslator`, `OwnershipUpdateTranslator`, and any auto-translators.

2. For any translator that calls `elm.BeginConstruction(...)` or `entityMap.GetOrCreate(...)` for a **new** entity, refactor it:

```csharp
// BEFORE (direct ELM call):
var entity = _entityMap.GetOrCreate(sample.EntityId, world);
if (!world.HasComponent<SomeComponent>(entity))
    _elm.BeginConstruction(entity, sample.TkbType, world.GlobalVersion, cmdBuffer);
world.SetComponent(entity, sample);

// AFTER (via SpawnEntityCommand):
if (!_entityMap.TryGetEntity(sample.EntityId, out _))
{
    _eventBus.Publish(new SpawnEntityCommand
    {
        NetworkId        = sample.EntityId,
        TkbType          = LookupTkbType(sample),
        OwnerNodeId      = ResolveOwner(info),
        InitType         = ReliableInitType.None, // Ghost: no ACK required
        InitialComponents = new List<object> { sample }
    });
}
else
{
    // Entity known: just update the component
    _eventBus.Publish(new UpdateEntityCommand
    {
        NetworkId          = sample.EntityId,
        ComponentsToUpdate = new List<object> { sample }
    });
}
```

3. For `InstanceState.Disposed` (entity deletion):
```csharp
if (info.InstanceState == InstanceState.Disposed)
{
    _eventBus.Publish(new DestroyEntityCommand
    {
        NetworkId = sample.EntityId,
        Reason    = "DDS sample DISPOSE"
    });
    return;
}
```

**Acceptance Criteria:**
- ✅ Ingress translator no longer calls `elm.BeginConstruction` directly
- ✅ Ingress translator no longer calls `entityMap.GetOrCreate` for new entities
- ✅ `SpawnEntityCommand` published for new entities with `InitType = ReliableInitType.None`
- ✅ `UpdateEntityCommand` published for existing entities (component update only)
- ✅ `DestroyEntityCommand` published for DISPOSE samples
- ✅ Existing dead-reckoning (TransformSyncSystem) still receives updates (NetworkReceivedState still set)

**Estimated Effort:** 0.5 days

**Dependencies:** NS2.2

---

### Task NS2.5: Validate with Existing Integration Tests

**Goal:** Confirm that all existing `LifecycleIntegrationTests` still pass after the refactoring.

**Reference Tests:** `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/DdsIntegrationTests.cs`

**Steps:**

1. Run the existing lifecycle integration tests:
   ```powershell
   dotnet test FDP/Examples/Fdp.Examples.NetworkDemo.Tests/Fdp.Examples.NetworkDemo.Tests.csproj `
     --filter "FullyQualifiedName~LifecycleIntegration" --logger "console;verbosity=normal"
   ```

2. Confirm all tests pass. If any fail, diagnose and fix the refactoring. Common failure causes:
   - `SpawnEntityCommand` published but `NetworkSpawningSystem` not yet executed on that frame → check module ordering.
   - `NetworkId = instanceId * 1000` (deterministic) conflicts with allocator state → verify ID guard.
   - Missing `InitialComponents` entry that was previously set in `SpawnLocalEntities` → add to list.
   - `PendingNetworkAck` mismatch causing timeout → check `InitType = ReliableInitType.AllPeers` is set.

3. Run the full FDP test suite to check for regressions:
   ```powershell
   dotnet test FDP/FDP.sln --filter "Category!=Integration" --logger "console;verbosity=minimal"
   ```

**Acceptance Criteria:**
- ✅ All `LifecycleIntegration` tests pass (same result as before refactoring)
- ✅ No regressions in other FDP unit/integration tests
- ✅ NetworkDemo can be started manually with two nodes: entities appear on both sides
- ✅ Entity lifecycle progression: Constructing → Active visible in debug output
- ✅ Entity count in `NetworkEntityMap` matches expected (1 per node)

**Estimated Effort:** 0.25 days

**Dependencies:** NS2.3, NS2.4
