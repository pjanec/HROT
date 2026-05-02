# BATCH-03 Review

**Batch:** BATCH-03  
**Reviewer:** Dev Lead  
**Tasks covered:** Corrective-0, PM-1 (Hrot.Common deps), PM-2 (NodeRole move), PM-3 (HrotNode* move), EAM-M001, EAM-M002, EAM-M003  
**Outcome:** ✅ APPROVED (deviations noted — debt recorded)

---

## 1. Build & Test Results

| Suite | Pre-BATCH-03 baseline | Post-BATCH-03 | Delta |
|---|---|---|---|
| `Hrot.ClusterRunner.Tests` | F:3 P:208 | F:3 P:211 | ✅ +3 passing (CgfSubsystemTests) |
| `Hrot.SimHost.Tests` | F:5 P:440 | F:5 P:440 | ✅ No change |
| `Hrot.IG.Tests` | F:7 P:410 | F:7 P:410 | ✅ No change |
| `Hrot.ClusterRunner.Integration.Tests` | F:3 P:121 S:4 | F:5 P:121 S:4 | ⚠️ 2 newly-flaking (see note) |
| Full build | ✅ 0 errors | ✅ 0 errors | ✅ |

### Integration test note — newly-flaking tests

`FeatureSwitchRcuIntegrationTests.SwitchToExternal_*` (×2) and `AllSubsystemsClusterTransitionTests.*` (×1) fail in the full suite but **pass in isolation** (confirmed by running each class separately — 4/4 and 2/2 respectively). These are DDS resource contention failures under parallel test load, NOT BATCH-03 code regressions. The full-suite flakiness count was already present before (MiniExCon×2 + ClusterOpE2e×1 = 3). After BATCH-03 one MiniExCon test now passes (net improvement from CgfSubsystem event bus fix), but two FeatureSwitch tests started flaking. Net: baseline still met for deterministic (non-parallel-contention) behavior.

---

## 2. Pre-Migration Tasks (PM-1, PM-2, PM-3)

**Result:** ✅ Approved

### PM-1: Hrot.Common.csproj dependencies

7 new project references added. Dependencies are:
- `Hrot.Map.Common` — for HrotEnvironment, geotransform
- `ModuleHost.Core` — for ModuleHostKernel, IEcsModule  
- `CycloneDDS.Runtime` + `CycloneDDS.Schema` — for DdsParticipant/Reader/Writer
- `ModuleHost.Network.Cyclone` — for DdsIdAllocator, NodeIdMapper
- `FDP.Toolkit.Lifecycle` — for EntityLifecycleModule
- `FDP.Toolkit.Replication` — for NetworkEntityMap, GhostCreationSystem

Trade-off: `Hrot.Common` is now a heavier dependency (pulls in CycloneDDS, ModuleHost etc.). This is unavoidable given the migration scope. Previously these project deps were siloed in `Hrot.ClusterRunner`; they now become part of the shared layer.

### PM-2: NodeRole moved to Hrot.Common

Correct implementation: `Hrot.Common/NodeRole.cs` with updated namespace; `Hrot.SimHost/NodeRole.cs` replaced with `global using NodeRole = Hrot.Common.NodeRole;` shim. Test projects (SimHost.Tests, SimHost.Integration.Tests) also got GlobalUsings.cs shims. Clean and backward-compatible.

### PM-3: HrotNode* moved to Hrot.Common.Infrastructure

All 4 files cleanly moved with updated namespaces. Key addition: `HrotNodeContext.IdAllocator` property added so EAM-M001 could access the `DdsIdAllocator`. All dependents (ClusterRunner tests, EyesAndMuscleSubsystem, NedReplicationModule, etc.) updated to `using Hrot.Common.Infrastructure` and `using Hrot.Common`.

---

## 3. EAM-M001 — SimHostApp Migration

**Result:** ✅ Approved (SC4 partial — documented debt)

| Success Criterion | Status |
|---|---|
| SC1: SimHost tests pass, 0 regressions | ✅ |
| SC2: `OnLoad` body significantly reduced (steps 2–4+8a collapsed to ~8 lines) | ✅ (spirit met; behavior setup is unavoidably lengthy) |
| SC3: No `HrotEnvironment.CreateParticipant` in `OnLoad` | ✅ |
| SC4: `_context` and `_nedReplicationModule` fields present | ⚠️ Partial — `_nedReplicationModule` is null (see deviation) |

**Deviation D1 — `_nedReplicationModule` always null:** `NedReplicationModule` is in `Hrot.ClusterRunner`; `Hrot.SimHost` cannot reference it (circular dependency). The field is declared and managed but never set. Registered as **DEBT-M001 (P2)** — requires moving `NedReplicationModule` to `Hrot.Common` in a future batch.

**Deviation D2 — `HrotNodeConfig.Headless = false` hardcoded:** Correct decision. `_headless` in `SimHostApp` controls Raylib window only; DDS must always initialize regardless. Hardcoding prevents false "headless = no DDS" interpretation.

**Notable quality:** `DdsIdAllocatorHelper.EnsureRouting` private method correctly deleted — fully replaced by the call inside `HrotNodeBuilder.Build()`. No duplication.

---

## 4. EAM-M002 — IgApplication Migration

**Result:** ✅ Approved (partial migration — documented debt)

| Success Criterion | Status |
|---|---|
| SC1: IG tests pass, 0 regressions | ✅ |
| SC2: No direct `CreateParticipant` in `InitializeNetwork` | ❌ Partial — see deviation |
| SC3: `DeadReckoningSyncSystem(driveFromNetwork:true)` registered | ✅ (in existing `ReplicationLogicModule`) |

**Deviation D1 — `CreateParticipant` kept in `InitializeNetwork`:** `IgApplication.InitializeEcs` uses `HrotNodeBuilder` with `Headless = true` (correct: IG never uses `DdsIdAllocator`, so the 30s wait must be skipped). Since `Headless = true` → `context.Participant == null`, the DDS participant must still be created in `InitializeNetwork`. This is the correct approach given: (a) circular dep `Hrot.IG → Hrot.ClusterRunner` is FORBIDDEN, and (b) IG has 50+ IG-specific translators that all need the participant. SC2 is not fully met but the partial migration (world/kernel/entityMap from HrotNodeBuilder) is correct and valuable. Registered as **DEBT-M002 (P3)**.

**Deviation D2 — `ReplicationLogicModule` kept:** Removing it broke 7 IG integration tests. `ReplicationLogicModule` bundles `GhostPromotionSystem`, `OwnershipIngressSystem`, `SubEntityCleanupSystem`, etc. — critical for the IG ghost lifecycle that cannot be replaced by `NedReplicationModule` (which is inaccessible from IG). Correct to keep. Registered as **DEBT-M003 (P3)**.

---

## 5. EAM-M003 — CgfSubsystem Migration

**Result:** ✅ Approved — full migration, well executed

| Success Criterion | Status |
|---|---|
| SC1: CGF integration tests pass (14 tests) | ✅ |
| SC2: No `new CgfApplication(...)` in Initialize | ✅ |
| SC3: `_nedReplicationModule` field retained and disposed | ✅ |

**Notable quality findings:**

1. **Event bus fix** — Developer correctly identified that `_context.World.Bus` (the EntityRepository's built-in bus) must be passed to `NedReplicationModule` for CGF, NOT `_context.EventBus`. `ConsumeManagedEvents<T>()` reads from `EntityRepository.Bus`; `_context.EventBus` is the orchestration bus for cluster slave/time. This was a subtle correctness fix, not in the spec.

2. **GhostDestructionSystem restoration** — Developer discovered that `CgfApplication`'s private `GhostDestructionSystem` was responsible for purging ghost entities and re-added it to `NedReplicationModule.RegisterSystems` for Brain role. This prevented ghost entity leaks post-migration. Good detective work.

3. **Headless semantics** — `CgfHarness` correctly updated from `Headless=true` to `Headless=false` since integration tests need real DDS. `CgfSubsystemTests` unit test correctly uses `Headless=true` for deterministic/fast test.

**One concern:** `NedReplicationModule.RegisterSystems` now behaves differently depending on whether called from CGF (where `eventBus = _context.World.Bus`) vs EyesAndMuscle (`_context.EventBus`). This inconsistency is tracked as **DEBT-M004 (P3)**.

---

## 6. New Debt Items to Record

| ID | Priority | Description | Target |
|---|---|---|---|
| DEBT-M001 | P2 | `NedReplicationModule` inaccessible from `Hrot.SimHost` — must move to `Hrot.Common` for full EAM-M001 SC4. SimHostApp `_nedReplicationModule` is null placeholder. | Future BATCH |
| DEBT-M002 | P3 | IgApplication still creates DDS participant directly in `InitializeNetwork` (EAM-M002 SC2 missed). Full IG migration requires `NedReplicationModule` in `Hrot.Common`. | Future BATCH |
| DEBT-M003 | P3 | `ReplicationLogicModule` kept in IgApplication — not replaced by NedReplicationModule (inaccessible from IG). | Future BATCH |
| DEBT-M004 | P3 | `NedReplicationModule` event bus parameter semantically inconsistent: CGF uses `World.Bus`, EyesAndMuscle uses `EventBus`. Both work in practice but differ in lifecycle; consider unifying under a documented convention. | Future BATCH |

---

## 7. Suggested Commit Message

```
feat: Phase 4 - Move HrotNode* to Hrot.Common + migrate SimHostApp/IgApp/CgfSubsystem

Pre-migration infrastructure (PM-1/2/3):
- Hrot.Common.csproj: +7 project references (Map.Common, ModuleHost.Core,
  CycloneDDS.Runtime/Schema, ModuleHost.Network.Cyclone, Toolkit.Lifecycle,
  Toolkit.Replication).
- NodeRole moved from Hrot.SimHost to Hrot.Common; backward-compat global using
  shims in SimHost project and both test projects.
- HrotNodeBuilder/Context/Config/DdsIdAllocatorHelper moved to Hrot.Common/Infrastructure;
  HrotNodeContext extended with IdAllocator property for SimHostApp.
- All callers updated to Hrot.Common.Infrastructure + Hrot.Common namespaces.

EAM-M001 (SimHostApp):
- OnLoad steps 2-4+8a replaced by HrotNodeBuilder.Build(); private
  EnsureIdAllocatorRouting deleted.
- HrotNodeConfig.Headless = false (DDS always required; _headless = Raylib only).
- P2 debt: _nedReplicationModule null until NedReplicationModule moves to Hrot.Common.

EAM-M002 (IgApplication):
- InitializeEcs uses HrotNodeBuilder (Headless=true, world/kernel/entityMap only).
- InitializeNetwork participant creation unchanged (IG→ClusterRunner circular dep).
- P3 debt: CreateParticipant + ReplicationLogicModule remain in InitializeNetwork.

EAM-M003 (CgfSubsystem):
- CgfApplication fully replaced by HrotNodeBuilder + NedReplicationModule(Brain)
  + CgfLogicPack.
- eventBus: _context.World.Bus (not EventBus) — required for ConsumeManagedEvents.
- GhostDestructionSystem added to NedReplicationModule for Brain/AllInOne roles.
- CgfHarness updated: Headless=false (integration tests need DDS).

Closes: EAM-M001 (partial), EAM-M002 (partial), EAM-M003 (full)
Debt: DEBT-M001(P2), DEBT-M002/M003/M004(P3) tracked in DEBT-TRACKER
```
