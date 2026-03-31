# Replication Fixes Design

**Version:** 2.0  
**Date:** 2026-03-02  
**Status:** Ready for Implementation

**Source Talk:** [design-talk.md](./design-talk.md)  
**Task Details:** [REPL-TASK-DETAIL.md](./REPL-TASK-DETAIL.md)  
**Task Tracker:** [REPL-TASK-TRACKER.md](./REPL-TASK-TRACKER.md)

---

## Table of Contents

1. [Background & Problem Statement](#1-background--problem-statement)
2. [Issue 1 — Simulation Phase Bug (Silent Failure)](#2-issue-1--simulation-phase-bug-silent-failure)
3. [Issue 2 — Zombie Entity Memory Leak](#3-issue-2--zombie-entity-memory-leak)
4. [The Replication Systems — What They Actually Do](#4-the-replication-systems--what-they-actually-do)
5. [Design Resolution](#5-design-resolution)
6. [Phase 0 — Kernel Prerequisite Verification](#6-phase-0--kernel-prerequisite-verification)
7. [Phase 1 — Modernise Replication Systems (Remove SimWrapper)](#7-phase-1--modernise-replication-systems-remove-simwrapper)
8. [Phase 2 — ECS-as-Staging Architecture](#8-phase-2--ecs-as-staging-architecture)
9. [Phase 3 — Update App Wiring](#9-phase-3--update-app-wiring)
10. [Phase 4 — Integration Test Coverage](#10-phase-4--integration-test-coverage)
11. [Infrastructure Status Matrix](#11-infrastructure-status-matrix)

---

## 1. Background & Problem Statement

Two architectural bugs were identified via code analysis of the `FDP.Toolkit.Replication` toolkit:

| # | Issue | Class | Symptom |
|---|-------|-------|---------|
| 1 | **Simulation Phase Bug** | `ReplicationLogicModule` / `SimWrapper<T>` | All replication systems are registered with `SystemPhase.Simulation` but the kernel skips that phase → systems never run |
| 2 | **Zombie Entity Memory Leak** | `NetworkEntityMap` / `DisposalMonitoringSystem` | Cleanup system exists but is never registered → map grows without bound |

Both are **silent failures**: no crash, no compile error. The apps superficially work because the current IG `EntityMasterTranslator` takes a shortcut — firing `SpawnEntityCommand` through `NetworkSpawningSystem` rather than going through the Ghost pipeline. This shortcut, however, **violates BDC-SST protocol rules** (see §4.1) and must be replaced by the proper Ghost mechanism as part of this fix.

The resolution combines three independent improvements:
1. **Remove `SimWrapper`** — replication systems actually run (fixes the phase bug).
2. **Register `DisposalMonitoringSystem`** — map entries are pruned (fixes the zombie leak).
3. **ECS-as-Staging** — replace `BinaryGhostStore` / `ISerializationRegistry` with direct component application on Ghost entities. Translators write descriptor data directly to the entity; no byte serialization needed.

---

## 2. Issue 1 — Simulation Phase Bug (Silent Failure)

### 2.1 Root Cause

`ReplicationLogicModule.RegisterSystems` wraps every replication `ComponentSystem` in a private inner class `SimWrapper<T>`:

```csharp
// FDP/Toolkits/FDP.Toolkit.Replication/ReplicationLogicModule.cs
[UpdateInPhase(SystemPhase.Simulation)]       // ← THE BUG
private class SimWrapper<T> : IModuleSystem where T : ComponentSystem, new()
{
    ...
}
```

The `ModuleHostKernel` documents (and enforces) the rule that `SystemPhase.Simulation` is **strictly reserved for background worker threads** (modules running in `FastReplica` / `SlowBackground` policy). For global systems, the kernel's execution loop only runs these phases:

```csharp
// ModuleHost.Core/ModuleHostKernel.cs
private static readonly HashSet<SystemPhase> _validGlobalPhases = new()
{
    SystemPhase.Input,
    SystemPhase.BeforeSync,
    SystemPhase.PostSimulation,
    SystemPhase.Export
};
```

`RegisterGlobalSystem` already throws `InvalidOperationException` if called with a `Simulation`-phase system. The systems registered via `IModule.RegisterSystems`, which feeds the same global scheduler, are silently deposited into the `Simulation` bucket — a bucket the kernel never executes. **All six replication systems are dead on arrival.**

### 2.2 Why the Apps Currently Work

The IG `EntityMasterTranslator` (in `Hrot.IG`) bypasses the ghost pipeline entirely — it fires `SpawnEntityCommand` directly, which `NetworkSpawningSystem` handles. This workaround makes the demo work but is protocol-non-compliant (see §4.1). The FDP-internal `ModuleHost.Network.Cyclone.Translators.EntityMasterTranslator` creates "proxy entities" directly in the `Decode()` method, also bypassing the ghost pipeline.

**The replication systems are not dead legacy code.** They implement critical BDC-SST protocol requirements that the current shortcut cannot satisfy in all real-world scenarios.

### 2.3 Fix Strategy

Remove `SimWrapper<T>` entirely. Rewrite every replication system to implement `IModuleSystem` natively and tag it with the correct `[UpdateInPhase]` attribute that the kernel actually executes.

---

## 3. Issue 2 — Zombie Entity Memory Leak

### 3.1 Root Cause

`NetworkEntityMap` maintains two dictionaries that map between `long networkId` ↔ `Entity`:

```csharp
private readonly Dictionary<long, Entity> _netToEntity = new();
private readonly Dictionary<Entity, long> _entityToNet = new();
```

When an ECS entity is destroyed (e.g., from a `DestroyEntityCommand`), its `Entity` handle becomes invalid. The map has a cleanup method `PruneDeadEntities(EntityRepository repo)` that walks the dictionaries and removes stale entries.  

`DisposalMonitoringSystem` was written specifically to call this method each frame. However, it is **never registered** in `ReplicationLogicModule.RegisterSystems`:

```csharp
public void RegisterSystems(ISystemRegistry registry)
{
    registry.RegisterSystem(new SimWrapper<GhostCreationSystem>());
    registry.RegisterSystem(new SimWrapper<GhostPromotionSystem>());
    registry.RegisterSystem(new SimWrapper<OwnershipIngressSystem>());
    registry.RegisterSystem(new SimWrapper<OwnershipEgressSystem>());
    registry.RegisterSystem(new SimWrapper<SmartEgressSystem>());
    registry.RegisterSystem(new SimWrapper<SubEntityCleanupSystem>());
    // DisposalMonitoringSystem — MISSING
}
```

In a session with thousands of ephemeral entities (bullets, missiles, vehicles), this causes the map to accumulate stale entries indefinitely, eventually exhausting heap memory.

### 3.2 Additional Problem: Singleton Lookup Anti-Pattern

The current `DisposalMonitoringSystem` finds its `NetworkEntityMap` via a singleton query on the `World`:

```csharp
if (_entityMap == null && World.HasSingletonManaged<NetworkEntityMap>())
    _entityMap = World.GetSingletonManaged<NetworkEntityMap>();
```

This requires `NetworkEntityMap` to be globally registered as an ECS singleton, which creates tight coupling and makes the system hard to unit-test. It also silently does nothing if the singleton is absent.

### 3.3 Fix Strategy

1. Convert `DisposalMonitoringSystem` to `IModuleSystem` with constructor injection of `NetworkEntityMap`.  
2. Register it in `ReplicationLogicModule.RegisterSystems` tagged with `[UpdateInPhase(SystemPhase.PostSimulation)]`.  
3. Update `ReplicationLogicModule` constructor to accept `NetworkEntityMap`.

---

## 4. The Replication Systems — What They Actually Do

Before proceeding with the fix, it is critical to understand why these systems are **required** for BDC-SST compliance, even in production.

### 4.1 Ghost Creation & Promotion — BDC-SST Protocol Requirement

**BDC-SST does not guarantee `EntityMaster` arrives first.** On WANs, with UDP packet reordering, or for late-joining nodes, a `WorldPos` or `WeaponState` packet may arrive before the `EntityMaster` for that NetID.

**Current gap:** All IG ingress translators (e.g., `WorldPosTranslator.Decode`) call `EntityMap.TryGetEntity(netId, out entity)` and **silently return (drop data)** if the entity is unknown. This is protocol non-compliant — descriptors arriving before `EntityMaster` are irretrievably lost.

**The Ghost Pattern with ECS-as-Staging:**  
1. Any translator receives an unknown `NetID` → calls `GhostCreationSystem.CreateGhost(repo, netId)` to create a lightweight "ghost shell" entity with `EntityLifecycle.Ghost`. The calling ingress system (Input phase) passes its live `EntityRepository` directly; `GhostCreationSystem.Execute` is a no-op and holds no world reference, avoiding the Frame-0 race condition where translators run before `BeforeSync`.
2. The translator **immediately** applies its descriptor component to the ghost via the command buffer (`cmd.SetComponent(entity, new NetworkPosition {...})`).
3. When `EntityMasterTranslator` later receives the `EntityMaster`, it finds the existing ghost entity and adds a `NetworkSpawnRequest` component.
4. `GhostPromotionSystem` detects entities with `LifecycleState.Ghost` + `NetworkSpawnRequest`, applies the TKB template using `preserveExisting: true` (preserving descriptor data already on the entity), and transitions to `Constructing`.

### 4.2 Ownership Systems (`OwnershipIngressSystem`, `OwnershipEgressSystem`)

**Why required:** FDP supports *per-descriptor ownership transfer* (e.g., Node A owns the body, Node B takes over the turret). When a player enters a vehicle or a missile is handed off to a target-tracking node, ownership must transfer without deleting/recreating the entity. These systems consume `OwnershipUpdate` events and update the `DescriptorOwnership` component on the relevant entity.

### 4.3 Smart Egress (`SmartEgressSystem`)

**Why required:** Publishing all dirty descriptors for 10,000 entities at 60 Hz would collapse any network switch. `SmartEgressSystem` tracks `EgressPublicationState` per entity. Dirty descriptors publish immediately; un-dirty (unreliable) descriptors are refreshed at a throttled rate using a salted rolling window (~every 600 frames / 10 seconds), distributing the load evenly.

### 4.4 Sub-Entity Cleanup (`SubEntityCleanupSystem`)

**Why required:** Many TKB entities are composite — a Tank has a child Turret entity. When the network destroys the Tank root entity, the child Turret must also be destroyed to prevent invisible leaked "zombie turrets". This system queries all entities with `PartMetadata` components and destroys those whose parent is dead.

### 4.5 Disposal Monitoring (`DisposalMonitoringSystem`)

**Why required:** Prevents the `NetworkEntityMap` memory leak described in §3.

---

## 5. Design Resolution

### 5.1 Remove SimWrapper

All seven replication systems are converted from `ComponentSystem` to native `IModuleSystem` with correct `[UpdateInPhase]` attributes (details in §7.1). `SimWrapper<T>` is deleted.

### 5.2 Correct Phase Assignment

Each system is replication *infrastructure*, not simulation *logic*. It must run on the **main thread**:

| System | Phase | Rationale |
|--------|-------|-----------|
| `OwnershipIngressSystem` | `Input` | Reads incoming ownership updates before simulation logic |
| `GhostCreationSystem` | `BeforeSync` | Ghost entities are created during Input (by translators calling `CreateGhost(repo, netId)`); the system itself has a no-op `Execute` but is registered here for pipeline consistency |
| `GhostPromotionSystem` | `BeforeSync` | Template application completes before simulation logic |
| `SubEntityCleanupSystem` | `PostSimulation` | Cascade destroy after simulation has committed |
| `DisposalMonitoringSystem` | `PostSimulation` | Prune map after ECS destructions are committed |
| `OwnershipEgressSystem` | `Export` | Publish after simulation has settled |
| `SmartEgressSystem` | `Export` | Publish after simulation has settled |

### 5.3 Architectural Pivot — ECS-as-Staging

**Remove the `BinaryGhostStore` pipeline. Remove `ISerializationRegistry` from `ReplicationLogicModule`.**

| Old Approach (Binary Stashing) | New Approach (ECS-as-Staging) |
|---|---|
| Ghost entity created with `BinaryGhostStore` managed component | Ghost entity created with `EntityLifecycle.Ghost` and `NetworkIdentity` only |
| Translators serialise DDS structs to `byte[]`, stash in dict | Translators call `cmd.SetComponent(entity, <ECScomponent>)` directly |
| `GhostPromotionSystem` queries for `BinaryGhostStore` presence | `GhostPromotionSystem` queries with `.WithLifecycle(EntityLifecycle.Ghost)` |
| Template applied then bytes deserialised via `ISerializationRegistry` | Template applied with `preserveExisting: true`; existing components kept |
| `ReplicationLogicModule` needs `ISerializationRegistry` | `ReplicationLogicModule` only needs `NetworkEntityMap` + `ITkbDatabase` |

**Benefits:** Zero byte allocation per descriptor update. Ghost state visible in Inspector immediately. `ISerializationRegistry` removed from replication module's public API.

### 5.4 Translator Updates Required

**A. Non-master ingress translators** (`WorldPosTranslator`, `WorldPosTranslator`, `EntityInfoTranslator`, `EntityDamageTranslator`, `MapEntitySymbolTranslator`, `ContextActionsUpdateTranslator`):  
Change `return;` on unknown NetID to: `CreateGhost` + `SetComponent`.

**B. IG `EntityMasterTranslator`**:  
Stop firing `SpawnEntityCommand` (bypasses Ghost pipeline). Instead: create ghost if absent, add `NetworkSpawnRequest` to trigger `GhostPromotionSystem`.

**C. FDP-internal Cyclone `EntityMasterTranslator`** (`ModuleHost.Network.Cyclone`):  
Existing "proxy entity" creation already adds `NetworkSpawnRequest` but does **not** set `EntityLifecycle.Ghost`. Add one call to set the lifecycle so `GhostPromotionSystem` can query it by lifecycle state.

To give translators access to ghost creation, `GhostCreationSystem` is injected into translators that need it. Its public `CreateGhost(EntityRepository repo, long netId)` method creates the shell entity synchronously using the live repo passed in by the caller. The `Execute` body is intentionally empty — no world reference is cached, which avoids the Input-phase / BeforeSync race condition.

---

## 6. Phase 0 — Kernel Prerequisite Verification

**Goal:** Confirm `EntityLifecycle.Ghost` already exists in the kernel enum before proceeding.

Inspect `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs`. The current file already contains:

```csharp
/// <summary>Entity created from network state, awaiting EntityMaster.</summary>
Ghost = 4,
```

**No code change required.** The `Ghost` lifecycle state is already available. Phase 0 is a verification gate only.

---

## 7. Phase 1 — Modernise Replication Systems (Remove SimWrapper)

**Goal:** Fix the simulation phase bug. Convert all seven `ComponentSystem`s to native `IModuleSystem`. Delete `SimWrapper<T>`. Register the zombie-fix system.

### 7.1 Systems to Convert

| Task ID | System | From | To Phase |
|---------|--------|------|----------|
| REPL-P1-T1 | `DisposalMonitoringSystem` | `ComponentSystem` | `PostSimulation` |
| REPL-P1-T2 | `SubEntityCleanupSystem` | `ComponentSystem` | `PostSimulation` |
| REPL-P1-T3 | `OwnershipIngressSystem` | `ComponentSystem` | `Input` |
| REPL-P1-T4 | `GhostCreationSystem` | `ComponentSystem` | `BeforeSync` |
| REPL-P1-T5 | `GhostPromotionSystem` | `ComponentSystem` | `BeforeSync` |
| REPL-P1-T6 | `OwnershipEgressSystem` | `ComponentSystem` | `Export` |
| REPL-P1-T7 | `SmartEgressSystem` | `ComponentSystem` | `Export` |

### 7.2 Refactor `ReplicationLogicModule`

Remove `SimWrapper<T>`. Accept only `NetworkEntityMap` + `ITkbDatabase` via constructor (**no `ISerializationRegistry`**). Register all seven systems directly. Include the previously missing `DisposalMonitoringSystem`.

See [Task REPL-P1-T8](./REPL-TASK-DETAIL.md#repl-p1-t8-refactor-replicationlogicmodule).

---

## 8. Phase 2 — ECS-as-Staging Architecture

**Goal:** Replace the `BinaryGhostStore` byte-stashing pipeline with direct ECS component application.

### 8.1 Update `GhostCreationSystem` (Part A)

Remove `BinaryGhostStore` from ghost shell creation. Set `EntityLifecycle.Ghost` instead:

```csharp
// BEFORE (Binary Stashing)
World.AddComponent(entity, new BinaryGhostStore { FirstSeenFrame = currentFrame });

// AFTER (ECS-as-Staging)
_world.SetLifecycleState(entity, EntityLifecycle.Ghost);
```

See [Task REPL-P2-T1](./REPL-TASK-DETAIL.md#repl-p2-t1-update-ghostcreationsystem--ecs-as-staging-part-a).

### 8.2 Update `GhostPromotionSystem` (Part B)

Query by lifecycle state rather than `BinaryGhostStore` presence. Apply template with `preserveExisting: true`. Remove the entire `ISerializationRegistry` deserialization block:

```csharp
// BEFORE — queries by component presence, clobbers existing data, uses ISerializationRegistry
var query = World.Query()
    .With<NetworkSpawnRequest>()
    .WithManaged<BinaryGhostStore>()
    .Build();
// ... template.ApplyTo(World, entity, preserveExisting: false);
// ... ISerializationRegistry byte[] deserialization loop ...

// AFTER — queries by lifecycle, preserves all already-applied components
var query = _world!.Query()
    .With<NetworkSpawnRequest>()
    .WithLifecycle(EntityLifecycle.Ghost)
    .Build();
// ...
template.ApplyTo(_world!, entity, preserveExisting: true);
_world!.SetLifecycleState(entity, EntityLifecycle.Constructing);
// Fire ConstructionOrder for ELM handoff — no serialization registry needed
```

See [Task REPL-P2-T2](./REPL-TASK-DETAIL.md#repl-p2-t2-update-ghostpromotionsystem--ecs-as-staging-part-b).

### 8.3 Update IG `EntityMasterTranslator` (Part C)

Replace `SpawnEntityCommand` shortcut with Ghost + `NetworkSpawnRequest` pattern:

```csharp
// BEFORE — shortcut bypasses Ghost pipeline
if (!_entityMap.TryGetEntity(netId, out _))
    _eventBus.PublishManaged(new SpawnEntityCommand { NetworkId = netId, TkbType = ..., ... });

// AFTER — Ghost pipeline: create shell if absent, always add NetworkSpawnRequest
// repo is passed in by the Input-phase ingress system (view as EntityRepository)
if (!_entityMap.TryGetEntity(netId, out var entity))
    entity = _ghostCreationSystem.CreateGhost(repo, netId);

cmd.AddComponent(entity, new NetworkSpawnRequest
{
    TkbType = master.TkbType,
    DisType = master.DisType,
    OwnerNodeId = 0   // IG is a ghost replica — no authority
});
```

See [Task REPL-P2-T3](./REPL-TASK-DETAIL.md#repl-p2-t3-update-ig-entitymastertranslator--ecs-as-staging-part-c).

### 8.4 Update IG Ingress Translators — Ghost Fallback (Part D)

Change every non-master IG ingress translator from "drop on unknown NetID" to "create ghost + apply component":

```csharp
// BEFORE — data loss on out-of-order delivery
if (!EntityMap.TryGetEntity(netId, out var entity))
    return; // silently drops descriptor data

// AFTER — ghost fallback preserves data on entity immediately
// repo is passed in by the Input-phase ingress system (view as EntityRepository)
if (!EntityMap.TryGetEntity(netId, out var entity))
    entity = _ghostCreationSystem.CreateGhost(repo, netId);

cmd.SetComponent(entity, new NetworkPosition { Value = position });
```

Affected translators: `WorldPosTranslator`, `WorldPosTranslator`, `EntityInfoTranslator`, `EntityDamageTranslator`, `MapEntitySymbolTranslator`, `ContextActionsUpdateTranslator`.

See [Task REPL-P2-T4](./REPL-TASK-DETAIL.md#repl-p2-t4-update-ig-ingress-translators--ghost-fallback-part-d).

### 8.5 Update FDP-Internal Cyclone `EntityMasterTranslator` (Part E)

The `Decode()` method already creates an entity for unknown NetIDs and adds `NetworkSpawnRequest`. Add the missing `EntityLifecycle.Ghost` state so `GhostPromotionSystem` can query it:

```csharp
// In the "else" branch (new entity) in Decode():
var newEntity = repo.CreateEntity();
repo.SetLifecycleState(newEntity, EntityLifecycle.Ghost);  // ← ADD THIS
cmd.AddComponent(newEntity, new NetworkIdentity { Value = topic.EntityId });
cmd.AddComponent(newEntity, new NetworkSpawnRequest { ... });
```

See [Task REPL-P2-T5](./REPL-TASK-DETAIL.md#repl-p2-t5-update-fdp-internal-cyclone-entitymastertranslator--part-e).

---

## 9. Phase 3 — Update App Wiring

**Goal:** Update all application entry points. Key point: `ISerializationRegistry` is **no longer required** by `ReplicationLogicModule`.

### 9.1 Affected Files

| Task ID | File | Change |
|---------|------|--------|
| REPL-P3-T1 | `Hrot.IG/IgApplication.cs` | Pass `(_entityMap, tkb)` to `ReplicationLogicModule`; wire `GhostCreationSystem` instance into IG translators |
| REPL-P3-T2 | `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | Register `new ReplicationLogicModule(entityMap, tkbDb)` |
| REPL-P3-T3 | `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs` | Pass `(EntityMap, tkb)` |

### 9.2 GhostCreationSystem Wiring in IG

In `IgApplication.InitializeNetwork()`, the same `GhostCreationSystem` instance that is registered with the kernel (via `ReplicationLogicModule`) must also be passed to the IG ingress translators (Phase 2 changes). This requires extracting the `GhostCreationSystem` before constructing translators, or using a factory accessor on `ReplicationLogicModule`.

---

## 10. Phase 4 — Integration Test Coverage

**Goal:** Autonomous integration tests in `Hrot.ClusterRunner.Integration.Tests`. All use `HrotRunnerHarness` + `PumpUntil`.

| Task ID | Test | What It Verifies |
|---------|------|-----------------|
| REPL-P4-T1 | `ReplicationPhaseExecutionTests` | `DisposalMonitoringSystem` prunes map each frame (systems now run) |
| REPL-P4-T2 | `ZombieEntityMapTests` | `NetworkEntityMap` pruned after full lifecycle destroy on both SimHost and IG |
| REPL-P4-T3 | `SubEntityCascadeDestroyTests` | Child entities destroyed when parent is destroyed |
| REPL-P4-T4 | `GhostPromotionTests` | Out-of-order descriptors (WorldPos before EntityMaster) result in a fully promoted entity with preserved component data |

---

## 11. Infrastructure Status Matrix

| Component | Status | Location | Change |
|-----------|--------|----------|--------|
| `EntityLifecycle.Ghost` | ✅ EXISTS (value=4) | `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs` | Verification only — no code change |
| `BinaryGhostStore.cs` | ✅ EXISTS | `FDP/Toolkits/FDP.Toolkit.Replication/Components/BinaryGhostStore.cs` | No longer used by ghost pipeline (retained for backward compat) |
| `ISerializationRegistry` | ✅ EXISTS | `FDP/Common/FDP.Interfaces/Abstractions/ISerializationProvider.cs` | Removed from `ReplicationLogicModule` |
| `ReplicationLogicModule` | ✅ EXISTS — **MODIFY** | `FDP/Toolkits/FDP.Toolkit.Replication/ReplicationLogicModule.cs` | Remove SimWrapper; inject `NetworkEntityMap`+`ITkbDatabase`; register all 7 systems |
| `SimWrapper<T>` | ✅ EXISTS — **DELETE** | Inner class of `ReplicationLogicModule` | Deleted entirely |
| `DisposalMonitoringSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/DisposalMonitoringSystem.cs` | `IModuleSystem`, constructor inject, `PostSimulation` |
| `SubEntityCleanupSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/SubEntityCleanupSystem.cs` | `IModuleSystem`, `PostSimulation` |
| `OwnershipIngressSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/OwnershipIngressSystem.cs` | `IModuleSystem`, `Input` |
| `GhostCreationSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/GhostCreationSystem.cs` | Remove `BinaryGhostStore`; set `Ghost` lifecycle |
| `GhostPromotionSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/GhostPromotionSystem.cs` | Remove `ISerializationRegistry`; `preserveExisting: true`; query by Ghost lifecycle |
| `OwnershipEgressSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/OwnershipEgressSystem.cs` | `IModuleSystem`, `Export` |
| `SmartEgressSystem` | ✅ EXISTS — **MODIFY** | `...Replication/Systems/SmartEgressSystem.cs` | `IModuleSystem`, `Export` |
| IG `EntityMasterTranslator` | ✅ EXISTS — **MODIFY** | `Hrot.IG/Translators/EntityMasterTranslator.cs` | Replace `SpawnEntityCommand` with Ghost+`NetworkSpawnRequest` |
| IG ingress translators (6) | ✅ EXIST — **MODIFY** | `Hrot.IG/Translators/*.cs` | Ghost fallback instead of `return` on unknown NetID |
| Cyclone `EntityMasterTranslator` | ✅ EXISTS — **MODIFY** | `ModuleHost.Network.Cyclone/Translators/EntityMasterTranslator.cs` | Set `EntityLifecycle.Ghost` on new proxy entities |
| `IgApplication.cs` | ✅ EXISTS — **MODIFY** | `Hrot.IG/IgApplication.cs` | Pass `(_entityMap, tkb)` to `ReplicationLogicModule`; wire `GhostCreationSystem` into translators |
| `SimHostSubsystem.cs` | ✅ EXISTS — **MODIFY** | `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | Register `ReplicationLogicModule(entityMap, tkbDb)` |
| `NetworkDemoApp.cs` | ✅ EXISTS — **MODIFY** | `FDP/Examples/.../NetworkDemoApp.cs` | Pass `(EntityMap, tkb)` |
| New integration tests | ❌ NEW | `Hrot.ClusterRunner.Integration.Tests/` | REPL-P4-T1 through T4 |
