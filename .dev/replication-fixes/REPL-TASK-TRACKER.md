# Replication Fixes — Task Tracker

**Reference:** See [REPL-TASK-DETAIL.md](./REPL-TASK-DETAIL.md) for detailed task descriptions.  
**Design Document:** [REPL-DESIGN.md](./REPL-DESIGN.md)  
**Source Talk:** [design-talk.md](./design-talk.md)

---

## Overview

Fixes three interrelated issues in `FDP.Toolkit.Replication` and connected application layers:

| # | Issue | Root Cause |
|---|-------|------------|
| 1 | **Simulation Phase Bug** — All replication systems never execute | `SimWrapper<T>` hardcodes `SystemPhase.Simulation` which the kernel skips for global systems |
| 2 | **Zombie Entity Memory Leak** — `NetworkEntityMap` grows without bound | `DisposalMonitoringSystem` was never registered in `ReplicationLogicModule` |
| 3 | **Out-of-Order Descriptor Data Loss** — Translators silently drop data for unknown NetIDs | `return;` guard on unknown NetID + `SpawnEntityCommand` shortcut bypass Ghost pipeline |

**Architecture pivot:** ECS-as-Staging replaces the `BinaryGhostStore`/`ISerializationRegistry` byte-stashing pipeline. Ghost entities receive ECS components directly from translators; `GhostPromotionSystem` applies TKB templates with `preserveExisting: true`.

---

## Phase 0 — Kernel Prerequisite Verification

**Goal:** Verify `EntityLifecycle.Ghost` exists in the kernel enum. No code changes expected.

- [x] **REPL-P0-T1** Verify `EntityLifecycle.Ghost = 4` in `EntityLifecycleState.cs` [details](./REPL-TASK-DETAIL.md#repl-p0-t1-verify-entitylifecycleghost-exists)

---

## Phase 1 — Modernise Replication Systems (Remove SimWrapper)

**Goal:** Convert all seven replication systems from `ComponentSystem` to native `IModuleSystem` with correct phase attributes. Delete `SimWrapper<T>`. Key ECS-as-Staging changes applied in T4 and T5.

- [x] **REPL-P1-T1** Modernise `DisposalMonitoringSystem` → `[PostSimulation]` [details](./REPL-TASK-DETAIL.md#repl-p1-t1-modernise-disposalmonitoringsystem)
- [x] **REPL-P1-T2** Modernise `SubEntityCleanupSystem` → `[PostSimulation]` [details](./REPL-TASK-DETAIL.md#repl-p1-t2-modernise-subentitycleanupsystem)
- [x] **REPL-P1-T3** Modernise `OwnershipIngressSystem` → `[Input]` [details](./REPL-TASK-DETAIL.md#repl-p1-t3-modernise-ownershipingresssystem)
- [x] **REPL-P1-T4** Modernise `GhostCreationSystem` → `[BeforeSync]` + remove `BinaryGhostStore` + set `Ghost` lifecycle [details](./REPL-TASK-DETAIL.md#repl-p1-t4-modernise-ghostcreationsystem)
- [x] **REPL-P1-T5** Modernise `GhostPromotionSystem` → `[BeforeSync]` + remove `ISerializationRegistry` + `WithLifecycle(Ghost)` + `preserveExisting: true` [details](./REPL-TASK-DETAIL.md#repl-p1-t5-modernise-ghostpromotionsystem)
- [x] **REPL-P1-T6** Modernise `OwnershipEgressSystem` → `[Export]` [details](./REPL-TASK-DETAIL.md#repl-p1-t6-modernise-ownershipegresssystem)
- [x] **REPL-P1-T7** Modernise `SmartEgressSystem` → `[Export]` [details](./REPL-TASK-DETAIL.md#repl-p1-t7-modernise-smartegresssystem)
- [x] **REPL-P1-T8** Refactor `ReplicationLogicModule` — remove `SimWrapper`, 2-param constructor `(NetworkEntityMap, ITkbDatabase)`, register `DisposalMonitoringSystem` [details](./REPL-TASK-DETAIL.md#repl-p1-t8-refactor-replicationlogicmodule--remove-simwrapper-inject-dependencies)

---

## Phase 2 — ECS-as-Staging Architecture

**Goal:** Extend Ghost pipeline to translators. Remove `SpawnEntityCommand` shortcut. Add ghost fallback to all non-master ingress translators. Set `EntityLifecycle.Ghost` in FDP-internal Cyclone translator.

- [x] **REPL-P2-T1** *(covered by P1-T4)* `GhostCreationSystem` sets Ghost lifecycle [details](./REPL-TASK-DETAIL.md#repl-p2-t1-update-ghostcreationsystem--ecs-as-staging-part-a)
- [x] **REPL-P2-T2** *(covered by P1-T5)* `GhostPromotionSystem` queries Ghost lifecycle, `preserveExisting: true` [details](./REPL-TASK-DETAIL.md#repl-p2-t2-update-ghostpromotionsystem--ecs-as-staging-part-b)
- [x] **REPL-P2-T3** Update IG `EntityMasterTranslator` — replace `SpawnEntityCommand` with `CreateGhost` + `NetworkSpawnRequest` [details](./REPL-TASK-DETAIL.md#repl-p2-t3-update-ig-entitymastertranslator--ecs-as-staging-part-c)
- [x] **REPL-P2-T4** Update 6 IG ingress translators — ghost fallback instead of `return;` on unknown NetID [details](./REPL-TASK-DETAIL.md#repl-p2-t4-update-ig-ingress-translators--ghost-fallback-part-d)
- [x] **REPL-P2-T5** Update FDP-internal Cyclone `EntityMasterTranslator` — set `EntityLifecycle.Ghost` on new proxy entities [details](./REPL-TASK-DETAIL.md#repl-p2-t5-update-fdp-internal-cyclone-entitymastertranslator--part-e)

---

## Phase 3 — Update App Wiring

**Goal:** Pass `(NetworkEntityMap, ITkbDatabase)` to `ReplicationLogicModule` in all entry points. Wire `GhostCreationSystem` into IG translators. **No `ISerializationRegistry` needed.**

- [x] **REPL-P3-T1** Update `IgApplication` — pass `(_entityMap, tkb)` + wire `GhostCreationSystem` into translators [details](./REPL-TASK-DETAIL.md#repl-p3-t1-update-igapplication--pass-entitymap--wire-ghostcreationsystem)
- [x] **REPL-P3-T2** Update `SimHostSubsystem` — register `ReplicationLogicModule(entityMap, tkbDb)` [details](./REPL-TASK-DETAIL.md#repl-p3-t2-update-simhostsubsystem--register-replicationlogicmodule)
- [x] **REPL-P3-T3** Update `NetworkDemoApp` — pass `(EntityMap, tkb)` [details](./REPL-TASK-DETAIL.md#repl-p3-t3-update-networkdemoapp--pass-entitymap-to-replicationlogicmodule)

---

## Phase 4 — Integration Test Coverage

**Goal:** Add autonomous integration tests in `Hrot.ClusterRunner.Integration.Tests` that verify all three fixes end-to-end.

- [x] **REPL-P4-T1** `ReplicationPhaseExecutionTests` — `DisposalMonitoringSystem` prunes map within 60 frames [details](./REPL-TASK-DETAIL.md#repl-p4-t1-replicationphaseexecutiontests--systems-execute-each-frame)
- [x] **REPL-P4-T2** `ZombieEntityMapTests` — destroyed entity removed from `NetworkEntityMap` on both SimHost and IG [details](./REPL-TASK-DETAIL.md#repl-p4-t2-zombieentitymaptests--map-is-pruned-after-entity-destroy-full-lifecycle)
- [x] **REPL-P4-T3** `SubEntityCascadeDestroyTests` — child entities destroyed when parent is destroyed [details](./REPL-TASK-DETAIL.md#repl-p4-t3-subentitycascadedestroyests--child-entities-are-destroyed-with-parent)
- [x] **REPL-P4-T4** `GhostPromotionTests` — out-of-order WorldPos-before-EntityMaster results in promoted entity with preserved `NetworkPosition` [details](./REPL-TASK-DETAIL.md#repl-p4-t4-ghostpromotiontests--out-of-order-descriptor-promotion)

---

## Phase 5 — Translator Unification

**Goal:** Migrate and unify descriptor translators from `Hrot.IG` and `Hrot.SimHost` into a shared `Hrot.Map.Common` library. Apply ECS-as-Staging pattern consistently and organize them into Ingress and Egress folders.

- [x] **REPL-P5-T1** Update `Hrot.Map.Common` project references [details](./REPL-TASK-DETAIL.md#repl-p5-t1-update-hrotmapcommon-project-references)
- [x] **REPL-P5-T2** Migrate IG Ingress Translators to `Hrot.Map.Common.Replication.Ingress` [details](./REPL-TASK-DETAIL.md#repl-p5-t2-migrate-ig-ingress-translators)
- [x] **REPL-P5-T3** Migrate SimHost Egress Translators to `Hrot.Map.Common.Replication.Egress` [details](./REPL-TASK-DETAIL.md#repl-p5-t3-migrate-simhost-egress-translators)
- [x] **REPL-P5-T4** Migrate EntityMission Translators [details](./REPL-TASK-DETAIL.md#repl-p5-t4-migrate-entitymission-translators)
- [x] **REPL-P5-T5** Migrate `DescriptorMapper` to `Hrot.Map.Common.Replication.Utils` [details](./REPL-TASK-DETAIL.md#repl-p5-t5-migrate-descriptormapper)
- [x] **REPL-P5-T6** Update composition roots in IG and SimHost to use new shared translators [details](./REPL-TASK-DETAIL.md#repl-p5-t6-update-composition-roots)

---

## Affected Files Quick Reference

| File | Change Type |
|------|-------------|
| `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs` | VERIFY (no change expected) |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/DisposalMonitoringSystem.cs` | MODIFY |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SubEntityCleanupSystem.cs` | MODIFY |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/OwnershipIngressSystem.cs` | MODIFY |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs` | MODIFY (remove BinaryGhostStore, add Ghost lifecycle) |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs` | MODIFY (remove ISerializationRegistry, Ghost lifecycle query, preserveExisting: true) |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/OwnershipEgressSystem.cs` | MODIFY |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SmartEgressSystem.cs` | MODIFY |
| `FDP/Toolkits/FDP.Toolkit.Replication/ReplicationLogicModule.cs` | MODIFY (2-param constructor, no ISerializationRegistry) |
| `Hrot.IG/Translators/EntityMasterTranslator.cs` | MODIFY (replace SpawnEntityCommand with Ghost pipeline) |
| `Hrot.IG/Translators/WorldPosTranslator.cs` | MODIFY (ghost fallback) |
| `Hrot.IG/Translators/WorldPosTranslator.cs` | MODIFY (ghost fallback) |
| `Hrot.IG/Translators/EntityInfoTranslator.cs` | MODIFY (ghost fallback) |
| `Hrot.IG/Translators/EntityDamageTranslator.cs` | MODIFY (ghost fallback) |
| `Hrot.IG/Translators/MapEntitySymbolTranslator.cs` | MODIFY (ghost fallback) |
| `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs` | MODIFY (ghost fallback) |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/EntityMasterTranslator.cs` | MODIFY (set Ghost lifecycle on new proxy entities) |
| `Hrot.IG/IgApplication.cs` | MODIFY (2-param ReplicationLogicModule, wire GhostCreationSystem) |
| `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | MODIFY (register ReplicationLogicModule) |
| `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs` | MODIFY (2-param ReplicationLogicModule) |
| `Hrot.ClusterRunner.Integration.Tests/ReplicationPhaseExecutionTests.cs` | NEW |
| `Hrot.ClusterRunner.Integration.Tests/ZombieEntityMapTests.cs` | NEW |
| `Hrot.ClusterRunner.Integration.Tests/SubEntityCascadeDestroyTests.cs` | NEW |
| `Hrot.ClusterRunner.Integration.Tests/GhostPromotionTests.cs` | NEW |
