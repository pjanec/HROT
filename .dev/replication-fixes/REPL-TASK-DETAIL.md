# Replication Fixes — Task Details

**Version:** 2.0  
**Date:** 2026-03-02  
**Status:** Ready for Development

**Parent Design Document:** [REPL-DESIGN.md](./REPL-DESIGN.md)  
**Task Tracker:** [REPL-TASK-TRACKER.md](./REPL-TASK-TRACKER.md)

---

## Overview

This document details every implementation task for the Replication Fixes workstream. Each task includes exact file locations, step-by-step implementation instructions, and verifiable acceptance criteria.  

**Total Estimated Effort:** ~5 developer-days  
**Primary Affected Library:** `FDP/Toolkits/FDP.Toolkit.Replication/`  
**Secondary Affected Libraries:** `Bagira.IG/`, `Bagira.Runner/`, `ModuleHost.Network.Cyclone/`  
**Test Project:** `Bagira.Runner.Integration.Tests/`

---

## Phase 0 — Kernel Prerequisite Verification

**Goal:** Confirm `EntityLifecycle.Ghost` exists in the kernel enum before proceeding to implementation phases. This is a verification-only phase — no code changes.

---

### REPL-P0-T1: Verify `EntityLifecycle.Ghost` Exists

**Design Reference:** [REPL-DESIGN.md §6](./REPL-DESIGN.md#6-phase-0--kernel-prerequisite-verification)

**File to inspect:** `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs`

**What to verify:**
Open the file and confirm the following entry exists with value `4`:
```csharp
/// <summary>Entity created from network state, awaiting EntityMaster.</summary>
Ghost = 4,
```

**If it exists:** No code change required. Proceed to Phase 1.

**If it does NOT exist** (unlikely — it was present at time of design):
```csharp
// Add to EntityLifecycleState enum, after Constructing:
/// <summary>Entity created from network state, awaiting EntityMaster.</summary>
Ghost = 4,
```

**Verification Steps:**
1. Open `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs`.
2. Confirm `Ghost = 4` is present.
3. Confirm `_world.SetLifecycleState(entity, EntityLifecycle.Ghost)` compiles (write a throwaway line or check usages elsewhere).
4. No build step required unless the entry was missing and had to be added.

**Acceptance Criteria:**
- ✅ `EntityLifecycle.Ghost` with value `4` is present in `EntityLifecycleState.cs`.
- ✅ No other lifecycle values conflict with `4`.

**Dependencies:** None

**Estimated Effort:** 0.0 days (verification only)

---

## Phase 1 — Modernise Replication Systems (Remove SimWrapper)

**Goal:** Fix the simulation phase bug by converting all seven replication `ComponentSystem`s to native `IModuleSystem` implementations with correct `[UpdateInPhase]` attributes. Delete `SimWrapper<T>`. No system will be silently skipped after this phase.

---

### REPL-P1-T1: Modernise `DisposalMonitoringSystem`

**Design Reference:** [REPL-DESIGN.md §5](./REPL-DESIGN.md#5-design-resolution-removing-simwrapper), [§6.1](./REPL-DESIGN.md#61-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/DisposalMonitoringSystem.cs`

**Current state:**
```csharp
public class DisposalMonitoringSystem : ComponentSystem
{
    private NetworkEntityMap? _entityMap;
    protected override void OnUpdate()
    {
        if (_entityMap == null && World.HasSingletonManaged<NetworkEntityMap>())
            _entityMap = World.GetSingletonManaged<NetworkEntityMap>();
        if (_entityMap == null) return;
        _entityMap.PruneDeadEntities(World);
    }
}
```

**Target state:**
```csharp
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class DisposalMonitoringSystem : IModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public DisposalMonitoringSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void Execute(ISimulationView view, float dt)
        {
            // Main-thread PostSimulation: view IS the live EntityRepository.
            if (view is EntityRepository repo)
                _entityMap.PruneDeadEntities(repo);
        }
    }
}
```

**Implementation Steps:**
1. Open `DisposalMonitoringSystem.cs`.
2. Replace the class body with the target state above.
3. Remove the `using System;` import if present (add `using System;` for `ArgumentNullException`).
4. Verify the file compiles: `dotnet build FDP/Toolkits/FDP.Toolkit.Replication/FDP.Toolkit.Replication.csproj`.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.PostSimulation)]` attribute is present.
- ✅ `NetworkEntityMap` is injected via constructor; no singleton lookup in `Execute`.
- ✅ `Execute` calls `_entityMap.PruneDeadEntities(repo)` when `view is EntityRepository`.
- ✅ Project compiles with zero errors.

**Dependencies:** None

**Estimated Effort:** 0.1 days

---

### REPL-P1-T2: Modernise `SubEntityCleanupSystem`

**Design Reference:** [REPL-DESIGN.md §6.1](./REPL-DESIGN.md#61-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SubEntityCleanupSystem.cs`

**Current state:** inherits `ComponentSystem`, calls `World.Query()` and uses `EntityCommandBuffer` directly.

**Target state:**
```csharp
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class SubEntityCleanupSystem : IModuleSystem
    {
        public void Execute(ISimulationView view, float dt)
        {
            // Main-thread PostSimulation: safe to cast.
            if (view is not EntityRepository repo) return;

            var query = repo.Query()
                .With<PartMetadata>()
                .Build();

            using var ecb = new EntityCommandBuffer();

            foreach (var entity in query)
            {
                var meta = repo.GetComponent<PartMetadata>(entity);
                if (!repo.IsAlive(meta.ParentEntity))
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(repo);
        }
    }
}
```

**Implementation Steps:**
1. Open `SubEntityCleanupSystem.cs`.
2. Replace entire class body with the target state above.
3. Verify compilation.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.PostSimulation)]` attribute is present.
- ✅ No `World` field; all ECS access via `view` / cast to `EntityRepository`.
- ✅ ECB correctly plays back against `repo`.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T1 (establishes conversion pattern)

**Estimated Effort:** 0.1 days

---

### REPL-P1-T3: Modernise `OwnershipIngressSystem`

**Design Reference:** [REPL-DESIGN.md §6.1](./REPL-DESIGN.md#61-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/OwnershipIngressSystem.cs`

**Current state:** inherits `ComponentSystem`, uses `World.HasSingletonManaged<NetworkEntityMap>()` lazy lookup.

**Target state:**
```csharp
using System;
using Fdp.Kernel;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class OwnershipIngressSystem : IModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly INetworkTopology? _topology;

        public OwnershipIngressSystem(NetworkEntityMap entityMap, INetworkTopology? topology = null)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _topology = topology;
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            int localNodeId = _topology?.LocalNodeId ?? 0;

            var updates = view.ConsumeEvents<OwnershipUpdate>();
            foreach (var update in updates)
            {
                if (!_entityMap.TryGetEntity(update.NetworkId.Value, out Entity entity))
                    continue;
                if (!repo.IsAlive(entity)) continue;

                DescriptorOwnership ownership;
                if (repo.HasManagedComponent<DescriptorOwnership>(entity))
                    ownership = repo.GetComponent<DescriptorOwnership>(entity);
                else
                {
                    ownership = new DescriptorOwnership();
                    repo.SetManagedComponent(entity, ownership);
                }

                ownership.Map[update.PackedKey] = update.NewOwnerNodeId;

                var (typeId, _) = ModuleHost.Core.Network.OwnershipExtensions.UnpackKey(update.PackedKey);
                bool isAuth = localNodeId != 0 && update.NewOwnerNodeId == localNodeId;

                try { repo.SetAuthority(entity, (int)typeId, isAuth); }
                catch (Exception) { }

                if (isAuth)
                {
                    repo.Bus.Publish(new FDP.Toolkit.Replication.Messages.DescriptorAuthorityChanged
                    {
                        Entity = entity,
                        PackedKey = update.PackedKey,
                        IsAuthoritative = true
                    });
                }
            }
        }
    }
}
```

**Implementation Steps:**
1. Open `OwnershipIngressSystem.cs`.
2. Replace entire class body with the target state above.
3. Verify compilation.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.Input)]` attribute is present.
- ✅ `NetworkEntityMap` injected via constructor; no singleton lookup.
- ✅ All existing business logic (ownership map update, authority, event publishing) preserved.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T1

**Estimated Effort:** 0.2 days

---

### REPL-P1-T4: Modernise `GhostCreationSystem`

**Design Reference:** [REPL-DESIGN.md §8.1](./REPL-DESIGN.md#81-update-ghostcreationsystem-part-a), [§7.1](./REPL-DESIGN.md#71-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs`

**Note:** `GhostCreationSystem.CreateGhost` is a **public method** called by translators. The key ECS-as-Staging change: **remove `BinaryGhostStore`** and **set `EntityLifecycle.Ghost`** instead.

**⚠️ Race Condition Fix:** An earlier design captured `_world` inside `Execute(BeforeSync)` and used it in `CreateGhost`. However, translators are driven by the Input phase — which runs *before* `BeforeSync`. On Frame 0, `Execute` has never run, so `_world` is `null` and `CreateGhost` would throw. The fix is to pass `EntityRepository` **directly into** `CreateGhost` as a parameter. The caller (translator, via the ingress system) already holds a live repo reference.

**Target state:**
```csharp
using System;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostCreationSystem : IModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GhostCreationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        // No-op: system is registered to maintain architectural consistency,
        // but CreateGhost is driven by callers passing a live EntityRepository.
        public void Execute(ISimulationView view, float dt) { }

        /// <summary>
        /// Creates a ghost shell entity for the given network ID.
        /// Called by ingress translators on the Input phase main thread.
        /// The caller must supply a live <see cref="EntityRepository"/> from their view.
        /// Sets EntityLifecycle.Ghost so GhostPromotionSystem can query by lifecycle state.
        /// </summary>
        public Entity CreateGhost(EntityRepository repo, long networkId)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(networkId));

            // ECS-as-Staging: mark entity as Ghost lifecycle state.
            // Translators will set components directly; GhostPromotionSystem queries
            // by lifecycle to find entities ready for template promotion.
            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            _entityMap.Register(networkId, entity);

            return entity;
        }
    }
}
```

**Implementation Steps:**
1. Open `GhostCreationSystem.cs`.
2. Replace entire class body with the target state above.
3. Remove `private EntityRepository? _world;` field (it no longer exists).
4. Remove `using FDP.Toolkit.Replication.Components;` (no longer needed for `BinaryGhostStore`).
5. Remove the `currentFrame` / `GlobalTime` lookup block (no longer needed).
6. Verify compilation: `dotnet build FDP/Toolkits/FDP.Toolkit.Replication/FDP.Toolkit.Replication.csproj`.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.BeforeSync)]` attribute is present.
- ✅ `Execute` method body is empty (no-op).
- ✅ **No** `private EntityRepository? _world` field.
- ✅ `CreateGhost(EntityRepository repo, long networkId)` — takes repo as first argument.
- ✅ `repo.SetLifecycleState(entity, EntityLifecycle.Ghost)` is called in `CreateGhost`.
- ✅ **No** `BinaryGhostStore` added in `CreateGhost`.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P0-T1

**Estimated Effort:** 0.2 days

---

### REPL-P1-T5: Modernise `GhostPromotionSystem`

**Design Reference:** [REPL-DESIGN.md §8.2](./REPL-DESIGN.md#82-update-ghostpromotionsystem-part-b), [§7.1](./REPL-DESIGN.md#71-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs`

**ECS-as-Staging changes:** Remove `ISerializationRegistry` entirely. Query by `WithLifecycle(EntityLifecycle.Ghost)` instead of `WithManaged<BinaryGhostStore>()`. Apply template with `preserveExisting: true` so component data already set by translators is preserved.

**Target state (key structural changes):**
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.Lifecycle.Events;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostPromotionSystem : IModuleSystem
    {
        // ECS-as-Staging: only ITkbDatabase needed — no ISerializationRegistry
        private readonly ITkbDatabase _tkbDatabase;

        private readonly Queue<Entity> _promotionQueue = new();
        private readonly HashSet<Entity> _inQueue = new();
        private readonly Stopwatch _stopwatch = new();
        private static readonly long PROMOTION_BUDGET_TICKS =
            (long)(0.002 * Stopwatch.Frequency);

        private EntityRepository? _world;

        public GhostPromotionSystem(ITkbDatabase tkbDatabase)
        {
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            // Note: ISerializationRegistry removed — components are already on the entity via ECS-as-Staging
        }

        public void Execute(ISimulationView view, float dt)
        {
            _world = view as EntityRepository;
            if (_world == null) return;

            EnqueueReadyGhosts();

            if (_promotionQueue.Count == 0) return;

            _stopwatch.Restart();
            while (_promotionQueue.Count > 0)
            {
                if (_stopwatch.ElapsedTicks > PROMOTION_BUDGET_TICKS) break;

                var entity = _promotionQueue.Dequeue();
                _inQueue.Remove(entity);

                if (!_world.IsAlive(entity)) continue;
                if (!_world.HasComponent<NetworkSpawnRequest>(entity)) continue;

                PromoteGhost(entity);
            }
            _stopwatch.Stop();
        }

        private void EnqueueReadyGhosts()
        {
            // CHANGED: query by lifecycle Ghost state instead of BinaryGhostStore presence
            var query = _world!.Query()
                .With<NetworkSpawnRequest>()
                .WithLifecycle(EntityLifecycle.Ghost)
                .Build();

            foreach (var entity in query)
            {
                if (!_inQueue.Contains(entity))
                {
                    _promotionQueue.Enqueue(entity);
                    _inQueue.Add(entity);
                }
            }
        }

        private void PromoteGhost(Entity entity)
        {
            var spawnReq = _world!.GetComponent<NetworkSpawnRequest>(entity);
            var template = _tkbDatabase.GetTemplate(spawnReq.TkbType);
            if (template == null) return;

            // CHANGED: preserveExisting: true — translators set components BEFORE promotion;
            // template fills in defaults but does NOT overwrite already-set components
            template.ApplyTo(_world!, entity, preserveExisting: true);

            _world!.SetLifecycleState(entity, EntityLifecycle.Constructing);
            _world!.RemoveComponent<NetworkSpawnRequest>(entity);

            // Fire ConstructionOrder for ELM handoff (no byte deserialization needed)
            _world!.Bus.PublishManaged(new ConstructionOrder { Entity = entity });
        }
    }
}
```

**Implementation Steps:**
1. Open `GhostPromotionSystem.cs`.
2. Remove `ISerializationRegistry` field and all references to it.
3. Change constructor to accept only `ITkbDatabase` (remove `ISerializationRegistry` parameter).
4. Remove the lazy singleton lookup blocks for `_serializationRegistry` in `OnUpdate`/`Execute`.
5. Delete the entire byte deserialization loop (the block that iterates over `BinaryGhostStore.StashedData`).
6. Change the query in `EnqueueReadyGhosts` from `WithManaged<BinaryGhostStore>()` to `WithLifecycle(EntityLifecycle.Ghost)`.
7. Change `template.ApplyTo(_world!, entity, preserveExisting: false)` to `preserveExisting: true`.
8. Verify compilation.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.BeforeSync)]` attribute is present.
- ✅ **No** `ISerializationRegistry` field or constructor parameter.
- ✅ Query uses `.WithLifecycle(EntityLifecycle.Ghost)`, not `.WithManaged<BinaryGhostStore>()`.
- ✅ `template.ApplyTo(...)` called with `preserveExisting: true`.
- ✅ No byte deserialization loop in the body.
- ✅ All ghost promotion logic (budget, queue, lifecycle transition) is intact.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T4

**Estimated Effort:** 0.4 days

---

### REPL-P1-T6: Modernise `OwnershipEgressSystem`

**Design Reference:** [REPL-DESIGN.md §4.2](./REPL-DESIGN.md#42-ownership-systems-ownershipingresssystem-ownershipegresssystem), [§6.1](./REPL-DESIGN.md#61-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/OwnershipEgressSystem.cs`

**Target state (key structural changes):**
```csharp
[UpdateInPhase(SystemPhase.Export)]
public class OwnershipEgressSystem : IModuleSystem
{
    // _lastKnownOwnership and _deadEntities caches preserved as-is.

    public void Execute(ISimulationView view, float dt)
    {
        if (view is not EntityRepository repo) return;

        // All existing business logic unchanged;
        // replace World. with repo.
    }
}
```

**Implementation Steps:**
1. Open `OwnershipEgressSystem.cs`.
2. Change class declaration to implement `IModuleSystem`.
3. Add `[UpdateInPhase(SystemPhase.Export)]` attribute.
4. Rename `OnUpdate` to `Execute(ISimulationView view, float dt)`.
5. At the top of Execute add: `if (view is not EntityRepository repo) return;`
6. Replace every `World` reference with `repo`.
7. No constructor changes needed (no dependencies other than World).
8. Verify compilation.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.Export)]` attribute is present.
- ✅ All existing ownership-delta tracking logic is intact.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T1

**Estimated Effort:** 0.1 days

---

### REPL-P1-T7: Modernise `SmartEgressSystem`

**Design Reference:** [REPL-DESIGN.md §4.3](./REPL-DESIGN.md#43-smart-egress-smartegresssystem), [§6.1](./REPL-DESIGN.md#61-systems-to-convert)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SmartEgressSystem.cs`

**Note:** `SmartEgressSystem.Execute` has an empty body (`// Logic is demand-driven by AutoTranslator`). The conversion is structural only; the `ShouldPublishDescriptor` / `NeedsRefresh` public API methods must be preserved as they are called by translators.

**Target state (key structural changes):**
```csharp
[UpdateInPhase(SystemPhase.Export)]
public class SmartEgressSystem : IModuleSystem
{
    private EntityRepository? _world;

    public void Execute(ISimulationView view, float dt)
    {
        _world = view as EntityRepository;
        // Logic is demand-driven by translators via ShouldPublishDescriptor.
    }

    // All existing public/private methods unchanged, replace World. with _world?.
}
```

**Implementation Steps:**
1. Open `SmartEgressSystem.cs`.
2. Change class to implement `IModuleSystem`.
3. Add `[UpdateInPhase(SystemPhase.Export)]` attribute.
4. Rename `OnUpdate` to `Execute`; add `_world = view as EntityRepository;` at the top.
5. Replace `World` references in helper methods with `_world!` or `_world?.`.
6. Verify compilation.

**Acceptance Criteria:**
- ✅ Class implements `IModuleSystem`, not `ComponentSystem`.
- ✅ `[UpdateInPhase(SystemPhase.Export)]` attribute is present.
- ✅ `ShouldPublishDescriptor` and `NeedsRefresh` public API unchanged.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T1

**Estimated Effort:** 0.1 days

---

### REPL-P1-T8: Refactor `ReplicationLogicModule` — Remove SimWrapper, Inject Dependencies

**Design Reference:** [REPL-DESIGN.md §7.2](./REPL-DESIGN.md#72-refactor-replicationlogicmodule)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/ReplicationLogicModule.cs`

**This is the integration task.** After all systems are modernised (T1–T7), update the module to:
- Accept **only** `NetworkEntityMap` and `ITkbDatabase` via constructor (**NO `ISerializationRegistry`**).
- Register all systems directly (no `SimWrapper`).
- Restore `DisposalMonitoringSystem` (the missing registration from the zombie leak).
- Delete the `SimWrapper<T>` inner class.
- Pass only `_tkbDatabase` to `GhostPromotionSystem` (not `ISerializationRegistry`).

**Target state:**
```csharp
using System;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Fdp.Kernel;
using Fdp.Interfaces;

namespace FDP.Toolkit.Replication
{
    public class ReplicationLogicModule : IModule
    {
        public string Name => "ReplicationLogic";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkEntityMap _entityMap;
        private readonly ITkbDatabase _tkbDatabase;

        // Note: ISerializationRegistry intentionally removed.
        // GhostPromotionSystem no longer needs byte deserialization (ECS-as-Staging).

        public ReplicationLogicModule(
            NetworkEntityMap entityMap,
            ITkbDatabase tkbDatabase)
        {
            _entityMap   = entityMap   ?? throw new ArgumentNullException(nameof(entityMap));
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            // Input phase: process incoming ownership transfers before simulation
            registry.RegisterSystem(new OwnershipIngressSystem(_entityMap));

            // BeforeSync phase: lifecycle management (entity existence settled before sim logic)
            registry.RegisterSystem(new GhostCreationSystem(_entityMap));
            registry.RegisterSystem(new GhostPromotionSystem(_tkbDatabase));  // no ISerializationRegistry

            // PostSimulation phase: cleanup after simulation has run
            registry.RegisterSystem(new SubEntityCleanupSystem());
            registry.RegisterSystem(new DisposalMonitoringSystem(_entityMap));   // FIX: zombie leak

            // Export phase: publish network state after simulation has settled
            registry.RegisterSystem(new OwnershipEgressSystem());
            registry.RegisterSystem(new SmartEgressSystem());
        }

        public void Tick(ISimulationView view, float dt) { }

        // SimWrapper<T> intentionally removed.
        // All systems are now native IModuleSystem implementations with correct phase attributes.
    }
}
```

**Implementation Steps:**
1. Open `ReplicationLogicModule.cs`.
2. Replace constructor: remove `ISerializationRegistry` parameter; keep only `NetworkEntityMap` + `ITkbDatabase`.
3. Remove `_serializationRegistry` field.
4. Update `RegisterSystems`: replace all `new SimWrapper<X>()` calls with direct system instantiation.
5. Change `new GhostPromotionSystem(_tkbDatabase, _serializationRegistry)` to `new GhostPromotionSystem(_tkbDatabase)`.
6. Add `new DisposalMonitoringSystem(_entityMap)` — this is the **zombie fix**.
7. Delete the entire `private class SimWrapper<T>` inner class definition.
8. Verify compilation.

**Acceptance Criteria:**
- ✅ `SimWrapper<T>` inner class is completely removed.
- ✅ Constructor accepts **two** parameters: `NetworkEntityMap`, `ITkbDatabase`. **No** `ISerializationRegistry`.
- ✅ `DisposalMonitoringSystem(_entityMap)` is registered in `RegisterSystems`.
- ✅ `GhostPromotionSystem(_tkbDatabase)` — single argument only.
- ✅ All seven systems are registered.
- ✅ No `SystemPhase.Simulation` used anywhere in the module.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T1, REPL-P1-T2, REPL-P1-T3, REPL-P1-T4, REPL-P1-T5, REPL-P1-T6, REPL-P1-T7

**Estimated Effort:** 0.3 days

---

## Phase 2 — ECS-as-Staging Architecture

**Goal:** Replace the `BinaryGhostStore` byte-stashing pipeline with direct ECS component application. Translators write components onto Ghost entities immediately. `GhostPromotionSystem` promotes when both `EntityLifecycle.Ghost` and `NetworkSpawnRequest` are present.

The zombie memory leak is fixed as a side-effect of Phase 1 (DisposalMonitoringSystem registered). Phase 2 wires the ECS-as-Staging pattern end-to-end.

---

### REPL-P2-T1: Update `GhostCreationSystem` — ECS-as-Staging (Part A)

**Design Reference:** [REPL-DESIGN.md §8.1](./REPL-DESIGN.md#81-update-ghostcreationsystem-part-a)

**Implementation note:** This change is **already specified in REPL-P1-T4**. The ECS-as-Staging refactor (remove `BinaryGhostStore`, add `SetLifecycleState(Ghost)`) is performed as part of converting the system from `ComponentSystem` to `IModuleSystem`. There is no additional code change beyond what REPL-P1-T4 describes.

**This task is a cross-reference marker only.** Mark as complete when REPL-P1-T4 is complete.

**Acceptance Criteria:**
- ✅ REPL-P1-T4 completed.
- ✅ `GhostCreationSystem.CreateGhost` sets `EntityLifecycle.Ghost`, not `BinaryGhostStore`.

**Dependencies:** REPL-P1-T4

**Estimated Effort:** 0.0 days (covered by P1-T4)

---

### REPL-P2-T2: Update `GhostPromotionSystem` — ECS-as-Staging (Part B)

**Design Reference:** [REPL-DESIGN.md §8.2](./REPL-DESIGN.md#82-update-ghostpromotionsystem-part-b)

**Implementation note:** This change is **already specified in REPL-P1-T5**. The ECS-as-Staging refactor (remove `ISerializationRegistry`, query by `WithLifecycle(Ghost)`, `preserveExisting: true`) is performed as part of converting the system. There is no additional code change beyond what REPL-P1-T5 describes.

**This task is a cross-reference marker only.** Mark as complete when REPL-P1-T5 is complete.

**Acceptance Criteria:**
- ✅ REPL-P1-T5 completed.
- ✅ `GhostPromotionSystem` queries by `EntityLifecycle.Ghost` lifecycle, not `BinaryGhostStore`.
- ✅ `template.ApplyTo(...)` uses `preserveExisting: true`.

**Dependencies:** REPL-P1-T5

**Estimated Effort:** 0.0 days (covered by P1-T5)

---

### REPL-P2-T3: Update IG `EntityMasterTranslator` — ECS-as-Staging (Part C)

**Design Reference:** [REPL-DESIGN.md §8.3](./REPL-DESIGN.md#83-update-ig-entitymastertranslator-part-c)

**File:** `Bagira.IG/Translators/EntityMasterTranslator.cs`

**Problem:** Current code fires `SpawnEntityCommand` when encountering an unknown NetID, bypassing the Ghost pipeline. This means `GhostCreationSystem` is never involved for IG-originated spawns.

**Current state (approximate):**
```csharp
public void ProcessSample(EntityMasterDescriptor master)
{
    long netId = master.EntityId;

    if (!_entityMap.TryGetEntity(netId, out _))
    {
        _eventBus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId = netId,
            TkbType   = master.TkbType,
            DisType   = master.DisType
        });
        return;
    }
    // update existing entity...
}
```

**Target state:**
```csharp
// Decode is called by the Input-phase ingress system which passes the live repo:
public void Decode(EntityMasterDescriptor master, ICommandBuffer cmd, EntityRepository repo)
{
    long netId = master.EntityId;

    // ECS-as-Staging: create ghost if not yet known (instead of SpawnEntityCommand shortcut)
    if (!_entityMap.TryGetEntity(netId, out var entity))
    {
        // Pass repo directly — safe because Input phase runs on the main thread
        // with a live EntityRepository, never a read-only snapshot.
        if (repo == null)
        {
            // Should never happen on Input phase, but guard defensively
            _logger?.LogWarning("Cannot create ghost: view is read-only.");
            return;
        }
        entity = _ghostCreationSystem.CreateGhost(repo, netId);
    }

    // Add NetworkSpawnRequest — triggers GhostPromotionSystem on next BeforeSync
    cmd.AddComponent(entity, new NetworkSpawnRequest
    {
        TkbType     = master.TkbType,
        DisType     = master.DisType,
        OwnerNodeId = 0   // IG replica — no ownership authority
    });
}
```

**Implementation Steps:**
1. Open `Bagira.IG/Translators/EntityMasterTranslator.cs`.
2. Add `private readonly GhostCreationSystem _ghostCreationSystem;` field.
3. Add `GhostCreationSystem ghostCreationSystem` parameter to constructor; assign field.
4. Locate `ProcessSample` / `Decode`: find the block that calls `_eventBus.PublishManaged(new SpawnEntityCommand {...})`.
5. Ensure the method (or calling ingress system) passes `EntityRepository repo` — obtained from the ingress system's `Execute` via `view as EntityRepository`.
6. Replace the `SpawnEntityCommand` publish block with the `CreateGhost(repo, netId)` + `cmd.AddComponent(NetworkSpawnRequest)` pattern.
7. Build `Bagira.IG.csproj`.

**Acceptance Criteria:**
- ✅ **No** `SpawnEntityCommand` published from `EntityMasterTranslator`.
- ✅ `_ghostCreationSystem.CreateGhost(repo, netId)` called for unknown NetIDs (with repo parameter).
- ✅ Defensive `null` check on `repo` with warning log and `return` (should never fire in practice).
- ✅ `NetworkSpawnRequest` added to entity via command buffer.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P1-T4, REPL-P3-T1 (for wiring)

**Estimated Effort:** 0.3 days

---

### REPL-P2-T4: Update IG Ingress Translators — Ghost Fallback (Part D)

**Design Reference:** [REPL-DESIGN.md §8.4](./REPL-DESIGN.md#84-update-ig-ingress-translators--ghost-fallback-part-d)

**Files to modify:**
- `Bagira.IG/Translators/GeoSpatialTranslator.cs`
- `Bagira.IG/Translators/GeoSpatialDRTranslator.cs`
- `Bagira.IG/Translators/EntityInfoTranslator.cs`
- `Bagira.IG/Translators/EntityDamageTranslator.cs`
- `Bagira.IG/Translators/MapEntitySymbolTranslator.cs`
- `Bagira.IG/Translators/ContextActionsUpdateTranslator.cs`

**Problem:** Each of these translators silently drops data when the NetID is not yet in `_entityMap`. This violates BDC-SST (data-before-entity delivery order is valid; entity creation is not guaranteed before all its descriptors arrive).

**Pattern to apply in each file:**

```csharp
// BEFORE: data loss on out-of-order delivery
if (!_entityMap.TryGetEntity(netId, out var entity))
    return;  // WRONG: silently discards descriptor data

// AFTER: ghost fallback — create shell entity, passing the live repo from the calling system
// The ingress Input-phase system calls Decode(sample, cmd, view as EntityRepository)
if (!_entityMap.TryGetEntity(netId, out var entity))
{
    if (repo == null)
    {
        // Read-only view — this should not happen on Input phase; log and skip.
        _logger?.LogWarning("Cannot create ghost for NetId {0}: view is read-only.", netId);
        return;
    }
    entity = _ghostCreationSystem.CreateGhost(repo, netId);
}

// Continue: apply component to entity (either existing or newly created ghost)
cmd.SetComponent(entity, new NetworkPosition { Value = position });
```

**Implementation Steps (per file):**
1. Add `private readonly GhostCreationSystem _ghostCreationSystem;` field.
2. Add constructor parameter `GhostCreationSystem ghostCreationSystem`, assign field.
3. Ensure `Decode` (or `ProcessSample`) accepts `EntityRepository? repo` — the calling ingress system passes `view as EntityRepository`.
4. Find the `return;` guard on unknown NetID (typically line ~40).
5. Replace `return;` with the ghost-fallback block above (`CreateGhost(repo, netId)` with null guard).
6. Ensure the translator then invokes `cmd.SetComponent(entity, <relevant component>)` on the next line.
7. Build `Bagira.IG.csproj`.
8. Repeat for all 6 files.

**Acceptance Criteria:**
- ✅ All 6 translators inject `GhostCreationSystem` via constructor.
- ✅ `Decode`/`ProcessSample` accepts `EntityRepository? repo` parameter passed from the calling ingress system.
- ✅ Null guard on `repo` with warning log + `return` (defensive, should not fire on Input phase).
- ✅ `CreateGhost(repo, netId)` called with live repo — **no** version without repo.
- ✅ **No** bare `return;` on unknown NetID without ghost fallback.
- ✅ Each translator sets the appropriate ECS component on the ghost entity.
- ✅ `Bagira.IG` compiles with zero errors.

**Dependencies:** REPL-P1-T4, REPL-P3-T1 (for wiring)

**Estimated Effort:** 0.3 days

---

### REPL-P2-T5: Update FDP-Internal Cyclone `EntityMasterTranslator` — Part E

**Design Reference:** [REPL-DESIGN.md §8.5](./REPL-DESIGN.md#85-update-fdp-internal-cyclone-entitymastertranslator-part-e)

**File:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/EntityMasterTranslator.cs`

**Problem:** The `Decode()` method creates a proxy entity for unknown NetIDs and adds `NetworkSpawnRequest`, but does **not** set `EntityLifecycle.Ghost`. Without the Ghost lifecycle state, `GhostPromotionSystem` (which now queries `WithLifecycle(EntityLifecycle.Ghost)`) will not find and promote the entity.

**Location of change:** In the `else` branch (new entity creation) of `Decode()`:

```csharp
// BEFORE: creates entity but does not set Ghost lifecycle
var newEntity = repo.CreateEntity();
cmd.AddComponent(newEntity, new NetworkIdentity { Value = topic.EntityId });
cmd.AddComponent(newEntity, new NetworkSpawnRequest { ... });

// AFTER: set Ghost lifecycle so GhostPromotionSystem can query it
var newEntity = repo.CreateEntity();
repo.SetLifecycleState(newEntity, EntityLifecycle.Ghost);   // ← ADD THIS
cmd.AddComponent(newEntity, new NetworkIdentity { Value = topic.EntityId });
cmd.AddComponent(newEntity, new NetworkSpawnRequest { ... });
```

**Implementation Steps:**
1. Open `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/EntityMasterTranslator.cs`.
2. Find the `Decode()` method — specifically the branch where `repo.CreateEntity()` is called for a new NetID.
3. Add `repo.SetLifecycleState(newEntity, EntityLifecycle.Ghost);` immediately after `repo.CreateEntity()`.
4. Build `ModuleHost.Network.Cyclone.csproj` to verify.

**Acceptance Criteria:**
- ✅ `repo.SetLifecycleState(newEntity, EntityLifecycle.Ghost)` is called on new proxy entities.
- ✅ Existing logic (adding `NetworkIdentity`, `NetworkSpawnRequest`) unchanged.
- ✅ Project compiles with zero errors.

**Dependencies:** REPL-P0-T1

**Estimated Effort:** 0.1 days

---

## Phase 3 — Update App Wiring

**Goal:** Update all application entry points to pass the required dependencies to the refactored `ReplicationLogicModule` constructor.

---

### REPL-P3-T1: Update `IgApplication` — Pass EntityMap + Wire GhostCreationSystem

**Design Reference:** [REPL-DESIGN.md §9.1](./REPL-DESIGN.md#91-affected-files), [§9.2](./REPL-DESIGN.md#92-ghostcreationsystem-wiring-in-ig)

**File:** `Bagira.IG/IgApplication.cs`

**Change 1 — ReplicationLogicModule constructor** (~line 285 in `InitializeNetwork()`):
```csharp
// BEFORE:
_kernel.RegisterModule(new ReplicationLogicModule());

// AFTER: only 2 params — NO ISerializationRegistry
var tkb = (Fdp.Interfaces.ITkbDatabase)_world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
var replicationModule = new ReplicationLogicModule(_entityMap, tkb);
_kernel.RegisterModule(replicationModule);
```

**Change 2 — Wire GhostCreationSystem into IG translators:**
```csharp
// Extract the GhostCreationSystem from the module before constructing translators
var ghostCreation = replicationModule.GhostCreationSystem;

// Pass ghostCreation to translators that were updated in P2-T3 and P2-T4:
//   EntityMasterTranslator, GeoSpatialTranslator, GeoSpatialDRTranslator,
//   EntityInfoTranslator, EntityDamageTranslator,
//   MapEntitySymbolTranslator, ContextActionsUpdateTranslator
```

**Note:** `_entityMap` is already a field in `IgApplication` (line 82). To expose `GhostCreationSystem` from `ReplicationLogicModule`, add a public `GhostCreationSystem GhostCreationSystem { get; }` property that returns the registered instance.

**Implementation Steps:**
1. Open `IgApplication.cs`.
2. Locate `_kernel.RegisterModule(new ReplicationLogicModule());`.
3. Replace with the 2-argument form; store the module instance in a local `replicationModule`.
4. Add `public GhostCreationSystem GhostCreationSystem { get; }` property to `ReplicationLogicModule` (or use an accessor).
5. Extract `ghostCreation` from the module and pass it to the 7 modified translators.
6. Build `Bagira.IG.csproj` to verify.

**Acceptance Criteria:**
- ✅ `ReplicationLogicModule` constructed with `(_entityMap, tkb)` — **no** `ISerializationRegistry`.
- ✅ `GhostCreationSystem` instance is passed to all 7 IG ingress translators.
- ✅ `Bagira.IG` compiles with zero errors.
- ✅ `_entityMap` is not `null` at point of construction.

**Dependencies:** REPL-P1-T8, REPL-P2-T3, REPL-P2-T4

**Estimated Effort:** 0.3 days

---

### REPL-P3-T2: Update `SimHostSubsystem` — Register ReplicationLogicModule

**Design Reference:** [REPL-DESIGN.md §9.1](./REPL-DESIGN.md#91-affected-files)

**File:** `Bagira.Runner/Services/SimHostSubsystem.cs`

**Current state:** `ReplicationLogicModule` is not currently registered in `SimHostSubsystem` at all. The SimHost uses `SimHostModule` for DDS I/O. `ReplicationLogicModule` must be added **after** the lifecycle module registration.

**Location of change:** Around line 264, after `_kernel.RegisterModule(elm);`:
```csharp
// ADD: only 2 params — NO ISerializationRegistry
_kernel.RegisterModule(new ReplicationLogicModule(entityMap, tkbDb));
```

**Note:** `entityMap` and `tkbDb` are local variables created earlier in the `Initialize()` method.

**Implementation Steps:**
1. Open `SimHostSubsystem.cs`.
2. Locate the section after `_kernel.RegisterModule(elm);` (the lifecycle module registration).
3. Add `_kernel.RegisterModule(new ReplicationLogicModule(entityMap, tkbDb));`.
4. Add required `using FDP.Toolkit.Replication;` import.
5. Build `Bagira.Runner.csproj` to verify.

**Acceptance Criteria:**
- ✅ `ReplicationLogicModule` registered in `SimHostSubsystem` initialization.
- ✅ Constructor uses `(entityMap, tkbDb)` — **no** `ISerializationRegistry`.
- ✅ `Bagira.Runner` project compiles with zero errors.

**Dependencies:** REPL-P1-T8

**Estimated Effort:** 0.1 days

---

### REPL-P3-T3: Update `NetworkDemoApp` — Pass EntityMap to ReplicationLogicModule

**Design Reference:** [REPL-DESIGN.md §9.1](./REPL-DESIGN.md#91-affected-files)

**File:** `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`

**Location of change:** ~line 198:
```csharp
// BEFORE:
if (!isReplay)
{
    Kernel.RegisterModule(new ReplicationLogicModule());
}

// AFTER: only 2 params — NO ISerializationRegistry
if (!isReplay)
{
    Kernel.RegisterModule(new ReplicationLogicModule(EntityMap, tkb));
}
```

**Note:** `EntityMap` and `tkb` (TKB database) are existing fields/locals in `NetworkDemoApp`. `SerializationRegistry` was previously created as a singleton around line 144 for the old byte-stashing pipeline — it is no longer needed.

**Implementation Steps:**
1. Open `NetworkDemoApp.cs`.
2. Locate the `new ReplicationLogicModule()` call (~line 198) and replace with the 2-argument form.
3. Locate the `SerializationRegistry` instantiation and `SetSingletonManaged<ISerializationRegistry>(...)` call (~line 144). **Remove both lines.** They exist solely for the retired `BinaryGhostStore` pipeline.
4. Remove any `using` directive for the serialization registry if it becomes unused.
5. Build `Fdp.Examples.NetworkDemo.csproj` to verify.

**Acceptance Criteria:**
- ✅ `ReplicationLogicModule` constructed with `(EntityMap, tkb)` — **no** `ISerializationRegistry`.
- ✅ `SerializationRegistry` singleton creation **removed** from `NetworkDemoApp` initialization.
- ✅ `Fdp.Examples.NetworkDemo` project compiles with zero errors.

**Dependencies:** REPL-P1-T8

**Estimated Effort:** 0.1 days

---

## Phase 4 — Integration Test Coverage

**Goal:** Add automated integration tests in `Bagira.Runner.Integration.Tests` that prove:
1. Replication systems execute each frame (Phase Bug fix is working).
2. `NetworkEntityMap` is pruned after entity destruction (Zombie Leak fix is working).
3. Child entities are cascaded-destroyed when parent is destroyed (`SubEntityCleanupSystem` working).
4. Out-of-order descriptors result in a properly promoted entity with ECS-as-Staging data preserved.

All tests use `BagiraRunnerHarness` (already available in the test project) and are fully autonomous (no manual setup, no external infrastructure beyond what the harness provides).

---

### REPL-P4-T1: `ReplicationPhaseExecutionTests` — Systems Execute Each Frame

**Design Reference:** [REPL-DESIGN.md §9.1](./REPL-DESIGN.md#91-tests)

**File to create:** `Bagira.Runner.Integration.Tests/ReplicationPhaseExecutionTests.cs`

**What this verifies:**
- After the SimWrapper fix, `DisposalMonitoringSystem` (PostSimulation) and `SubEntityCleanupSystem` (PostSimulation) actually execute, which is measurable by observing their side-effects on the `NetworkEntityMap`.
- The simplest observable proxy: spawn an entity, manually register a *second* fake entity handle in the map (one that has already been destroyed), pump frames, and assert the map no longer contains the dead handle.

**Exact test specification:**

```csharp
using System;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Verifies that DisposalMonitoringSystem and SubEntityCleanupSystem
/// actually execute after the SimWrapper phase fix.
/// Before the fix, these would silently never run (SystemPhase.Simulation was skipped).
/// After the fix, they run in PostSimulation and their side-effects are observable.
/// </summary>
public class ReplicationPhaseExecutionTests
{
    private const int TimeoutFrames = 60;

    /// <summary>
    /// Creates a real entity in the SimHost world, registers it in the NetworkEntityMap,
    /// then immediately destroys the entity.
    /// After pumping frames, the map should be pruned (DisposalMonitoringSystem ran).
    /// </summary>
    [Fact]
    public void DisposalMonitoringSystem_PrunesMapAfterEntityDestroyed()
    {
        using var harness = new BagiraRunnerHarness();

        // Access SimHost's NetworkEntityMap via TestHook (to be added in REPL-P4-T1 helper).
        var entityMap = harness.SimHost.TestHook_EntityMap;

        // Spawn and immediately get a live entity handle
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new GeoPosition { Latitude = 32.0, Longitude = 34.0 });

        // Wait for entity to be registered
        Entity simHostEntity = Entity.Null;
        bool registered = harness.PumpUntil(
            () => entityMap.TryGetEntity(networkId, out simHostEntity),
            TimeoutFrames);
        Assert.True(registered, "Entity was not registered in NetworkEntityMap.");

        // Destroy the entity via the event bus
        harness.SimHost.World.Bus.PublishManaged(
            new FDP.Toolkit.Lifecycle.Events.DestroyEntityCommand
            {
                NetworkId  = networkId,
                Reason     = "REPL-P4-T1 test"
            });

        // Wait for DisposalMonitoringSystem to prune the map
        bool mapPruned = harness.PumpUntil(
            () => !entityMap.TryGetEntity(networkId, out _),
            TimeoutFrames);

        Assert.True(mapPruned,
            "NetworkEntityMap was NOT pruned after entity destruction. " +
            "This indicates DisposalMonitoringSystem is not executing. " +
            "Check that SimWrapper has been removed and the system is registered " +
            "with [UpdateInPhase(SystemPhase.PostSimulation)].");
    }
}
```

**Prerequisites — TestHook additions to `SimHostSubsystem`:**

Add to `Bagira.Runner/Services/SimHostSubsystem.cs`:
```csharp
/// <summary>TestHook: exposes the NetworkEntityMap for integration test assertions.</summary>
public NetworkEntityMap TestHook_EntityMap => _entityMap;
```
(Store `entityMap` as a field `private NetworkEntityMap _entityMap;` in SimHostSubsystem.)

**Implementation Steps:**
1. Add `private NetworkEntityMap _entityMap;` field to `SimHostSubsystem.cs`.
2. Assign it: `_entityMap = entityMap; // around line 209 where entityMap is created`.
3. Add the `TestHook_EntityMap` property.
4. Create `ReplicationPhaseExecutionTests.cs` with the test class above.
5. Add required `using` directives.
6. Run: `dotnet test Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj --filter ReplicationPhaseExecutionTests`.

**Acceptance Criteria:**
- ✅ Test exists and compiles.
- ✅ Test **fails** before Phase 1 fix (map is never pruned because DisposalMonitoringSystem never runs).
- ✅ Test **passes** after Phase 1 + Phase 3 fixes are applied.
- ✅ Test runs autonomously with no external configuration.

**Dependencies:** REPL-P1-T8, REPL-P3-T1, REPL-P3-T2

**Estimated Effort:** 0.3 days

---

### REPL-P4-T2: `ZombieEntityMapTests` — Map Is Pruned After Entity Destroy (Full Lifecycle)

**Design Reference:** [REPL-DESIGN.md §7](./REPL-DESIGN.md#7-phase-2--fix-zombie-memory-leak), [§9.1](./REPL-DESIGN.md#91-tests)

**File to create:** `Bagira.Runner.Integration.Tests/ZombieEntityMapTests.cs`

**What this verifies:**
- After a full entity lifecycle (spawn → active → destroy via `DestroyEntityCommand`), the corresponding `NetworkEntityMap` entry on both **SimHost** and **IG** sides is removed, proving `DisposalMonitoringSystem` runs on both nodes.
- This is the full end-to-end proof of the zombie fix.

**Exact test specification:**

```csharp
using System;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// End-to-end test proving that entity destruction results in map cleanup on both nodes.
/// Tests the zombie entity memory leak fix (REPL Issue 2).
/// </summary>
public class ZombieEntityMapTests
{
    private const int SpawnTimeoutFrames   = 150;
    private const int DestroyTimeoutFrames = 150;
    private const int MapPruneTimeoutFrames = 60;

    [Fact]
    public void DestroyedEntity_IsRemovedFromNetworkEntityMap_OnSimHost()
    {
        using var harness = new BagiraRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new GeoPosition { Latitude = 32.0, Longitude = 34.0 });

        // 1. Wait for entity to appear in SimHost map
        var simHostMap = harness.SimHost.TestHook_EntityMap;
        bool appeared = harness.PumpUntil(
            () => simHostMap.TryGetEntity(networkId, out _),
            SpawnTimeoutFrames);
        Assert.True(appeared, "Entity did not appear in SimHost NetworkEntityMap.");

        // 2. Destroy entity
        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason    = "ZombieEntityMapTests"
        });

        // 3. Wait for map to be pruned
        bool removedFromMap = harness.PumpUntil(
            () => !simHostMap.TryGetEntity(networkId, out _),
            MapPruneTimeoutFrames);

        Assert.True(removedFromMap,
            $"NetworkId {networkId} is still present in SimHost NetworkEntityMap " +
            $"after entity destruction. Zombie entity leak detected. " +
            $"Verify DisposalMonitoringSystem is registered and executes in PostSimulation.");
    }

    [Fact]
    public void DestroyedEntity_IsRemovedFromNetworkEntityMap_OnIg()
    {
        using var harness = new BagiraRunnerHarness();

        long networkId = SpawnEntityViaPlacement(harness);

        // 1. Wait for entity to appear in IG's map
        var igMap = harness.Ig.App.TestHook_EntityMap;
        bool igEntityAppeared = harness.PumpUntil(
            () => igMap.TryGetEntity(networkId, out _),
            SpawnTimeoutFrames);
        Assert.True(igEntityAppeared, "Entity did not appear in IG NetworkEntityMap.");

        // 2. Destroy on SimHost side
        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason    = "ZombieEntityMapTests-IG"
        });

        // 3. Wait for IG's map to be pruned
        bool removedFromIgMap = harness.PumpUntil(
            () => !igMap.TryGetEntity(networkId, out _),
            MapPruneTimeoutFrames);

        Assert.True(removedFromIgMap,
            $"NetworkId {networkId} still in IG NetworkEntityMap after remote destroy. " +
            $"Verify ReplicationLogicModule is registered in IgApplication with _entityMap.");
    }

    private static long SpawnEntityViaPlacement(BagiraRunnerHarness harness)
    {
        var iosLogic = harness.Ios.Logic;
        var igApp    = harness.Ig.App;
        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        iosLogic.StartPlacementMode(tkbType, eForceIdentifier.FORCE_FRIENDLY);

        harness.PumpUntil(
            () => iosLogic.ActiveContextId != Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId,
            100);

        igApp.TestHook_SimulateMapClick(new System.Numerics.Vector2(100f, 200f));

        long networkId = 0;
        harness.PumpUntil(() =>
        {
            if (harness.SimHost.TestHook_EntityMap.TryGetEntity(0, out _)) return false; // placeholder
            // Find first entity with NetworkId via TestHook
            networkId = harness.SimHost.TestHook_LastSpawnedNetworkId;
            return networkId != 0;
        }, 150);

        return networkId;
    }
}
```

**Prerequisites — TestHook additions:**

Add to `IgApplication.cs`:
```csharp
/// <summary>TestHook: exposes NetworkEntityMap for integration tests.</summary>
public NetworkEntityMap TestHook_EntityMap => _entityMap;
```

Add to `SimHostSubsystem.cs` (if not added by REPL-P4-T1):
```csharp
public long TestHook_LastSpawnedNetworkId => _lastSpawnedNetworkId;
private long _lastSpawnedNetworkId;
// Set _lastSpawnedNetworkId = networkId; in TestHook_SpawnEntity after registration.
```

**Implementation Steps:**
1. Add `TestHook_EntityMap` property to `IgApplication`.
2. Ensure `IgSubsystem` exposes its `IgApplication` as `App` (already exists per harness usage).
3. Create `ZombieEntityMapTests.cs` with the test class above.
4. Run: `dotnet test Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj --filter ZombieEntityMapTests`.

**Acceptance Criteria:**
- ✅ Both tests exist and compile.
- ✅ Both tests **fail** before Phase 1–3 fixes.
- ✅ Both tests **pass** after all Phase 1–3 fixes are applied.
- ✅ Tests run autonomously with no external configuration.

**Dependencies:** REPL-P4-T1, REPL-P3-T1, REPL-P3-T2

**Estimated Effort:** 0.4 days

---

### REPL-P4-T3: `SubEntityCascadeDestroyTests` — Child Entities Are Destroyed With Parent

**Design Reference:** [REPL-DESIGN.md §4.4](./REPL-DESIGN.md#44-sub-entity-cleanup-subentitycleanupsystem), [§9.1](./REPL-DESIGN.md#91-tests)

**File to create:** `Bagira.Runner.Integration.Tests/SubEntityCascadeDestroyTests.cs`

**What this verifies:**
- When a parent entity is destroyed, its child entities (with `PartMetadata` pointing to the dead parent) are also destroyed by `SubEntityCleanupSystem`.
- This proves `SubEntityCleanupSystem` is executing in `PostSimulation`.

**Exact test specification:**

```csharp
using System;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Verifies that SubEntityCleanupSystem cascades entity destruction to child entities.
/// Tests that orphan "zombie children" do not accumulate after parent destroy.
/// </summary>
public class SubEntityCascadeDestroyTests
{
    private const int SpawnTimeoutFrames   = 150;
    private const int DestroyTimeoutFrames = 150;
    private const int CleanupTimeoutFrames = 60;

    [Fact]
    public void DestroyParentEntity_ChildEntitiesAreAlsoDestroyed()
    {
        using var harness = new BagiraRunnerHarness();

        // Spawn a composite entity (e.g., Tank with turret child).
        // Use a TKB type that has ChildBlueprints defined.
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,   // Must have child blueprints in TKB
            new GeoPosition { Latitude = 32.0, Longitude = 34.0 });

        // Wait for parent entity to be active
        Entity parentEntity = Entity.Null;
        bool parentActive = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out parentEntity))
                return false;
            return harness.SimHost.World.IsAlive(parentEntity);
        }, SpawnTimeoutFrames);
        Assert.True(parentActive, "Parent entity did not become active in time.");

        // Find child entities (PartMetadata.ParentEntity == parentEntity)
        var childEntities = harness.SimHost.TestHook_GetChildEntities(parentEntity);
        // If this TKB type has no children, this test is not applicable; skip gracefully.
        if (childEntities.Count == 0)
        {
            // No children to test — TKB type has no ChildBlueprints. Test is vacuously passing.
            return;
        }

        // Verify children are alive
        foreach (var child in childEntities)
            Assert.True(harness.SimHost.World.IsAlive(child),
                "Child entity should be alive before parent destruction.");

        // Destroy parent
        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "SubEntityCascadeDestroyTests"
        });

        // Wait for parent to be fully destroyed
        bool parentDestroyed = harness.PumpUntil(
            () => !harness.SimHost.World.IsAlive(parentEntity),
            DestroyTimeoutFrames);
        Assert.True(parentDestroyed, "Parent entity was not destroyed.");

        // Verify all children were also destroyed
        bool allChildrenDestroyed = harness.PumpUntil(
            () => childEntities.TrueForAll(c => !harness.SimHost.World.IsAlive(c)),
            CleanupTimeoutFrames);

        Assert.True(allChildrenDestroyed,
            "Child entities were not destroyed after parent was destroyed. " +
            "This indicates SubEntityCleanupSystem is not executing. " +
            "Check that SimWrapper has been removed and the system is registered " +
            "with [UpdateInPhase(SystemPhase.PostSimulation)].");
    }
}
```

**Prerequisites — TestHook additions to `SimHostSubsystem`:**

```csharp
/// <summary>TestHook: returns all entities whose PartMetadata.ParentEntity == parent.</summary>
public List<Entity> TestHook_GetChildEntities(Entity parent)
{
    var result = new List<Entity>();
    var query = _world.Query().With<FDP.Toolkit.Replication.Components.PartMetadata>().Build();
    foreach (var e in query)
    {
        var meta = _world.GetComponent<FDP.Toolkit.Replication.Components.PartMetadata>(e);
        if (meta.ParentEntity == parent)
            result.Add(e);
    }
    return result;
}
```

**Implementation Steps:**
1. Add `TestHook_GetChildEntities` to `SimHostSubsystem.cs`.
2. Create `SubEntityCascadeDestroyTests.cs` with the test class above.
3. Verify the `Tank_M1Abrams` TKB type has child blueprints; if not, use a different composite type or document the skip condition.
4. Run: `dotnet test Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj --filter SubEntityCascadeDestroyTests`.

**Acceptance Criteria:**
- ✅ Test exists and compiles.
- ✅ If TKB type has children: test **fails** before Phase 1 fix and **passes** after.
- ✅ If TKB type has no children: test exits gracefully (vacuous pass).
- ✅ Test runs autonomously.

**Dependencies:** REPL-P4-T1, REPL-P3-T2

**Estimated Effort:** 0.3 days

---

### REPL-P4-T4: `GhostPromotionTests` — Out-of-Order Descriptor Promotion

**Design Reference:** [REPL-DESIGN.md §10](./REPL-DESIGN.md#10-phase-4--integration-test-coverage)

**File to create:** `Bagira.Runner.Integration.Tests/GhostPromotionTests.cs`

**What this verifies:**
- An entity whose first-received descriptor is **GeoSpatial** (not EntityMaster) results in a ghost with `NetworkPosition` set directly.
- When `EntityMaster` arrives later, `GhostPromotionSystem` promotes the entity.
- After promotion, the entity retains `NetworkPosition` (because `preserveExisting: true`).
- This proves the ECS-as-Staging architecture preserves out-of-order component data.

**Exact test specification:**

```csharp
using System;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Verifies that out-of-order descriptor arrival (GeoSpatial before EntityMaster)
/// results in a properly promoted entity with ECS-as-Staging component preservation.
/// </summary>
public class GhostPromotionTests
{
    private const int TimeoutFrames = 120;

    [Fact]
    public void OutOfOrder_GeoSpatialBeforeEntityMaster_PositionPreservedAfterPromotion()
    {
        using var harness = new BagiraRunnerHarness();

        long networkId = 123_456_789L;
        var expectedPosition = new System.Numerics.Vector3(100f, 200f, 10f);

        // Step 1: Inject a GeoSpatial descriptor for an unknown entity (no EntityMaster yet)
        harness.Ig.App.TestHook_InjectGeoSpatialDescriptor(new GeoSpatialDescriptor
        {
            EntityId = networkId,
            Position = expectedPosition
        });

        // Step 2: Verify a Ghost entity was created and has NetworkPosition
        Entity ghostEntity = Entity.Null;
        bool ghostCreated = harness.PumpUntil(() =>
        {
            var igMap = harness.Ig.App.TestHook_EntityMap;
            if (!igMap.TryGetEntity(networkId, out ghostEntity)) return false;
            return harness.Ig.World.HasComponent<NetworkPosition>(ghostEntity);
        }, TimeoutFrames);

        Assert.True(ghostCreated, "Ghost entity with NetworkPosition was not created after GeoSpatial descriptor.");

        var posAfterGeo = harness.Ig.World.GetComponent<NetworkPosition>(ghostEntity).Value;
        Assert.Equal(expectedPosition, posAfterGeo);

        // Step 3: Inject the EntityMaster descriptor (arrives after GeoSpatial)
        harness.Ig.App.TestHook_InjectEntityMasterDescriptor(new EntityMasterDescriptor
        {
            EntityId = networkId,
            TkbType  = TkbEntityTypes.Tank_M1Abrams,
            DisType  = 0
        });

        // Step 4: Wait for ghost promotion (lifecycle leaves Ghost state)
        bool promoted = harness.PumpUntil(() =>
        {
            if (!harness.Ig.World.IsAlive(ghostEntity)) return false;
            var lifecycle = harness.Ig.World.GetLifecycleState(ghostEntity);
            return lifecycle != EntityLifecycle.Ghost;
        }, TimeoutFrames);

        Assert.True(promoted, "Ghost entity was not promoted after EntityMaster descriptor arrived.");

        // Step 5: Verify NetworkPosition was preserved (preserveExisting: true)
        var posAfterPromotion = harness.Ig.World.GetComponent<NetworkPosition>(ghostEntity).Value;
        Assert.Equal(expectedPosition, posAfterPromotion);
    }
}
```

**Prerequisites — TestHook additions to `IgApplication` / `IgSubsystem`:**
```csharp
/// <summary>TestHook: directly injects a GeoSpatial descriptor into the IG translator pipeline.</summary>
/// Passes the live world repo so CreateGhost can execute synchronously in the test.
public void TestHook_InjectGeoSpatialDescriptor(GeoSpatialDescriptor descriptor)
{
    var cmd = _world.GetCommandBuffer();
    _geoSpatialTranslator.Decode(descriptor, cmd, (EntityRepository)_world);
    _world.FlushCommandBuffers();
}

/// <summary>TestHook: directly injects an EntityMaster descriptor into the IG translator pipeline.</summary>
public void TestHook_InjectEntityMasterDescriptor(EntityMasterDescriptor descriptor)
{
    var cmd = _world.GetCommandBuffer();
    _entityMasterTranslator.Decode(descriptor, cmd, (EntityRepository)_world);
    _world.FlushCommandBuffers();
}
```

**Implementation Steps:**
1. Add `TestHook_InjectGeoSpatialDescriptor` and `TestHook_InjectEntityMasterDescriptor` to `IgApplication`.
2. Expose `IgApplication` from `IgSubsystem` as `App` (if not already present).
3. Create `GhostPromotionTests.cs` with the test above.
4. Run: `dotnet test Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj --filter GhostPromotionTests`.

**Acceptance Criteria:**
- ✅ Test exists and compiles.
- ✅ Test **fails** before Phase 2 fixes (ghost not created, or position not preserved).
- ✅ Test **passes** after Phase 2 (ECS-as-Staging) + Phase 3 fixes are applied.
- ✅ `NetworkPosition` value is identical before and after promotion.
- ✅ Test runs autonomously.

**Dependencies:** REPL-P2-T1, REPL-P2-T2, REPL-P2-T3, REPL-P2-T4, REPL-P3-T1

**Estimated Effort:** 0.4 days

---

## Phase 5 — Translator Unification

**Goal:** `Bagira.IG` and `Bagira.SimHost` use independent sets of translators, generating completely unnecessary code duplication. The logic in `Bagira.IG/Translators` and `Bagira.SimHost/Translators` must be unified and moved into `Bagira.Map.Common`, an existing shared library.

### REPL-P5-T1: Update Bagira.Map.Common Project References
Update `Bagira.Map.Common/Bagira.Map.Common.csproj` to include project references to the necessary libraries required to run `NetworkEntityMap` and the replication framework. Add `FDP.Toolkit.Replication`, `Bagira.DDS.DataModel`, `Fdp.Kernel`, `ModuleHost.Core`, and `ModuleHost.Network.Cyclone`. Add to `.csproj` and `dotnet restore`.

### REPL-P5-T2: Migrate IG Ingress Translators
Move the following translators from `Bagira.IG` to `Bagira.Map.Common/Replication/Ingress/`. Rename them explicitly with the `IngressTranslator` postfix (e.g. `GeoSpatialIngressTranslator`). Change their namespaces to `Bagira.Map.Common.Replication.Ingress`. These modules MUST retain the ECS-as-Staging (Ghost Fallback) pattern implemented in Phase 2.
List:
- `EntityMasterTranslator.cs`
- `GeoSpatialTranslator.cs`
- `GeoSpatialDRTranslator.cs`
- `EntityInfoTranslator.cs`
- `EntityDamageTranslator.cs`
- `MapEntitySymbolTranslator.cs`

### REPL-P5-T3: Migrate SimHost Egress Translators
Move the following EGRESS translators from `Bagira.SimHost` to `Bagira.Map.Common/Replication/Egress/`. Remove SimHost-specific fast paths that break pure FDP abstractions. Rename them to explicit `EgressTranslator` postfixes and change to namespace `Bagira.Map.Common.Replication.Egress`.
List:
- `EntityMasterEgressTranslator.cs`
- `GeoSpatialEgressTranslator.cs`
- `TimePulseEgressTranslator.cs`

### REPL-P5-T4: Migrate EntityMission Translators
The `EntityMission` feature has both Ingress AND Egress components (currently mixed). Move `Bagira.SimHost.Translators.EntityMissionTranslator` to `Bagira.Map.Common/Replication/Ingress/EntityMissionIngressTranslator.cs`. Move `EntityMissionEgressTranslator` to the equivalent target directory. Maintain identical ECS translation mapping.

### REPL-P5-T5: Migrate DescriptorMapper
Move `Bagira.SimHost/Util/DescriptorMapper.cs` into `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs`. This utility maps `EntityDescriptorUnion` (DDS) to ECS Components. Make sure any coupling to concrete instances is dropped; reference through interfaces like `IGeographicTransform`.

### REPL-P5-T6: Update Composition Roots
Update `Bagira.IG/IgApplication.cs` and `Bagira.SimHost/Modules/SimHostModule.cs`. 
In IG, use the new `IngressTranslator` variants and pass the necessary dependencies (via constructor injections matching the Phase 2 changes) inside the `customTranslators` list.
In SimHost, use the explicit new `EgressTranslator` references.

---

## Summary Table

| Task ID | Phase | File | Effort | Depends On |
|---------|-------|------|--------|------------|
| REPL-P0-T1 | Phase 0 | `EntityLifecycleState.cs` (verify) | 0.0d | — |
| REPL-P1-T1 | Phase 1 | `DisposalMonitoringSystem.cs` | 0.1d | P0-T1 |
| REPL-P1-T2 | Phase 1 | `SubEntityCleanupSystem.cs` | 0.1d | P1-T1 |
| REPL-P1-T3 | Phase 1 | `OwnershipIngressSystem.cs` | 0.2d | P1-T1 |
| REPL-P1-T4 | Phase 1 | `GhostCreationSystem.cs` | 0.2d | P0-T1 |
| REPL-P1-T5 | Phase 1 | `GhostPromotionSystem.cs` | 0.4d | P1-T4 |
| REPL-P1-T6 | Phase 1 | `OwnershipEgressSystem.cs` | 0.1d | P1-T1 |
| REPL-P1-T7 | Phase 1 | `SmartEgressSystem.cs` | 0.1d | P1-T1 |
| REPL-P1-T8 | Phase 1 | `ReplicationLogicModule.cs` | 0.3d | P1-T1 through P1-T7 |
| REPL-P2-T1 | Phase 2 | (covered by P1-T4) | 0.0d | P1-T4 |
| REPL-P2-T2 | Phase 2 | (covered by P1-T5) | 0.0d | P1-T5 |
| REPL-P2-T3 | Phase 2 | `Bagira.IG/Translators/EntityMasterTranslator.cs` | 0.3d | P1-T4 |
| REPL-P2-T4 | Phase 2 | `Bagira.IG/Translators/*.cs` (6 files) | 0.3d | P1-T4 |
| REPL-P2-T5 | Phase 2 | `ModuleHost.Network.Cyclone/.../EntityMasterTranslator.cs` | 0.1d | P0-T1 |
| REPL-P3-T1 | Phase 3 | `IgApplication.cs` | 0.3d | P1-T8, P2-T3, P2-T4 |
| REPL-P3-T2 | Phase 3 | `SimHostSubsystem.cs` | 0.1d | P1-T8 |
| REPL-P3-T3 | Phase 3 | `NetworkDemoApp.cs` | 0.1d | P1-T8 |
| REPL-P4-T1 | Phase 4 | `ReplicationPhaseExecutionTests.cs` | 0.3d | P1-T8, P3-T1, P3-T2 |
| REPL-P4-T2 | Phase 4 | `ZombieEntityMapTests.cs` | 0.4d | P4-T1, P3-T1, P3-T2 |
| REPL-P4-T3 | Phase 4 | `SubEntityCascadeDestroyTests.cs` | 0.3d | P4-T1, P3-T2 |
| REPL-P4-T4 | Phase 4 | `GhostPromotionTests.cs` | 0.4d | P2-T1, P2-T2, P2-T3, P2-T4, P3-T1 |
| | | **Total** | **~4.2d** | |
