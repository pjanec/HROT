# BS-1-BATCH-04 Report

**Workstream:** BS-1 (Brain / Muscle Node Separation)  
**Batch:** BS-1-BATCH-04  
**Status:** Complete

---

## Summary

All tech debt items (TD-6, TD-7, TD-8) and core tasks (BS1-T011 through BS1-T015) have been implemented and verified. All tests pass with no new regressions.

---

## Completed Items

### TD-6 — FireProcessingSystem Authority Gate

**Problem:** `FireProcessingSystem` was spawning bullets for every `WeaponFireIntent` regardless of whether the local node owned the shooter entity.

**Fix:** Added a `NetworkAuthority` component check in `FireProcessingSystem.OnUpdate`, mirroring the established pattern in `DamageSystem`. If the shooter entity has `NetworkAuthority` and `HasAuthority == false`, the event is silently skipped.

**Files modified:**
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` — added `NetworkAuthority` using + authority gate
- `FDP/Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs` — added `NetworkAuthority` component registration + two new tests: `FireProcessing_SkipsBullet_WhenShooterNotAuthoritative` and `FireProcessing_SpawnsBullet_WhenShooterIsAuthoritative`

---

### TD-7 — RaycastRequest.IgnoreEntity Documentation

**Problem:** The convention that `BallisticsSystem` populates `IgnoreEntity` with the shooter entity (carrier of the shooter's network ID) was undocumented, making `HitResolutionSystem`'s usage brittle.

**Fix:** Expanded the `<remarks>` section on `RaycastRequest.IgnoreEntity` in `PhysicsComponents.cs` to explicitly document the bullet-ray convention.

**Files modified:**
- `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs` — updated XML doc

---

### TD-8 — Physics Parallel Arrays Test

**Problem:** `HitResolutionSystem` assumes `batch.Hits[i]` always corresponds to `batch.Requests[i]`, but this contract had no test coverage.

**Fix:** Added `RaycastSolver_HitsIsParallelToRequests` test to `RaycastSolverSystemTests.cs`. It submits two requests (one hit, one miss) at specific indices and verifies the results are at the correct parallel positions.

**Files modified:**
- `FDP/Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs` — new test

---

### BS1-T011 — MunitionDetonationEgressTranslator

Translates `DetonationNotification` ECS events (from `HitResolutionSystem`) to `MunitionDetonation` DDS messages. Follows the same pattern as `WeaponFireNotificationEgressTranslator` (no authority check, ordinal 82).

**Files created:**
- `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs`
- `Hrot.SimHost.Tests/MunitionDetonationEgressTranslatorTests.cs` — 3 tests (single event, multiple detonations, empty bus)

---

### BS1-T012 — DamageAssessmentModule

Three new files:

1. **`DamageCalculationSystem`** — consumes `DetonationNotification`, checks `HasAuthority` over target entity, publishes `DamageAssessedEvent { TotalDamage = CombatConstants.DefaultBulletDamage }`. Does NOT mutate `Health` directly.

2. **`MunitionDetonationIngressTranslator`** — polls `MunitionDetonation` DDS, validates target entity via `NetworkEntityMap`, publishes `DetonationNotification` to local event bus.

3. **`DamageAssessmentModule`** — thin module class that registers `DamageCalculationSystem` into the `SimulationSystemGroup`.

**Files created:**
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Combat/Modules/DamageAssessmentModule.cs`
- `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs`
- `FDP/Toolkits/FDP.Toolkit.Combat.Tests/DamageCalculationSystemTests.cs` — 4 tests
- `Hrot.SimHost.Tests/MunitionDetonationIngressTranslatorTests.cs` — 2 tests

---

### BS1-T013 — DamageAssessedEgressTranslator

Translates `DamageAssessedEvent` ECS events to `EntityHitDamage` DDS messages (ordinal 83).

**Files created:**
- `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs`
- `Hrot.SimHost.Tests/DamageAssessedEgressTranslatorTests.cs` — 2 tests

---

### BS1-T014 — EntityHitDamageIngressTranslator + HealthApplicationSystem

1. **`EntityHitDamageIngressTranslator`** — polls `EntityHitDamage` DDS, validates via `NetworkEntityMap`, publishes `DamageAssessedEvent` on local bus.

2. **`HealthApplicationSystem`** — consumes `DamageAssessedEvent`, checks authority, decrements `Health.Current` (floored at 0), and strips `ActorCapabilities.CanMove | CanShoot` when HP reaches zero. Entity destruction is deferred.

**Files created:**
- `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs`
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Combat.Tests/HealthApplicationSystemTests.cs` — 4 tests
- `Hrot.SimHost.Tests/EntityHitDamageIngressTranslatorTests.cs` — 2 tests

---

### BS1-T015 — EntityDamageEgressTranslator

Tracks dirty `Health` components and publishes `EntityDamage` DDS messages. Change detection uses a `Dictionary<long, float>` cache of last-published `Health.Current` per network entity ID. Derives the DDS `Damage` field (0–100 scale) from `(1 - Current/Max) × 100`. Registered in `SimHostApp.cs`.

**Files created:**
- `Hrot.Map.Common/Replication/Egress/EntityDamageEgressTranslator.cs`
- `Hrot.SimHost.Tests/EntityDamageEgressTranslatorTests.cs` — 3 integration tests

**Files modified:**
- `Hrot.Map.Common/Hrot.Map.Common.csproj` — added `FDP.Toolkit.Combat.Contracts` project reference (needed for `Health` component)
- `Hrot.SimHost/SimHostApp.cs` — registered `EntityDamageEgressTranslator` in egress list

---

## Test Results

| Project | Total | Passed | Failed |
|---|---|---|---|
| FDP.Toolkit.Combat.Tests | 49 | 49 | 0 |
| FDP.Toolkit.Physics.Tests | 25 | 25 | 0 |
| Hrot.SimHost.Tests (non-integration) | 341 | 341 | 0 |

---

## Design Decisions

### HealthData mirror omitted in HealthApplicationSystem
The spec says to "update the `HealthData` mirror" in `HealthApplicationSystem`. However, `HealthData` was eradicated in the BUG2 workstream (`BUG2-BATCH-02`) as architectural debt. The system only updates `Health.Current` directly (matching what the post-BUG2 `DamageSystem` does). No deviation from functional intent.

### DamageAssessmentModule location
Spec suggested `FDP/Toolkits/FDP.Toolkit.Combat/Modules/` or `Hrot.SimHost/Modules/`. The module was placed in `FDP.Toolkit.Combat/Modules/` because `DamageCalculationSystem` resides there. The `MunitionDetonationIngressTranslator` (which depends on DDS infrastructure) lives in `Hrot.SimHost/Network/Ingress/` as specified.

### EntityDamageEgressTranslator change detection
`SmartEgressUtil` (used by other egress translators) tracks "published once per ordinal" — it has no mechanism to detect component value changes. A `Dictionary<long, float>` cache was used instead to compare `Health.Current` against the last-published value. This is the same pattern that `WorldPosEgressTranslator` uses with its `NetworkTransform` shadow component, adapted to avoid adding a new ECS component.

### Ordinal assignments
- Ordinal 82 used for both `MunitionDetonationEgressTranslator` and `MunitionDetonationIngressTranslator` (both address the same topic). This follows the existing convention where egress and ingress translators for the same topic share an ordinal (e.g., ordinal 80 for `WeaponFireRequest`).
- Ordinal 83 for both `DamageAssessedEgressTranslator` and `EntityHitDamageIngressTranslator` (`EntityHitDamage` topic).

---

## Design Gaps / Edge Cases Not Covered

1. **`DamageAssessmentModule` not registered** — `DamageAssessmentModule` is defined but not yet wired into `NodeBootstrapper`/`SimHostApp`. That is deferred to BS1-T016/T017 (not in this batch's scope). The module's `RegisterSystems` method is ready to use.

2. **New translators not role-gated in SimHostApp** — BS1-T017 (translator registration) is a separate task not in this batch. Only `EntityDamageEgressTranslator` (BS1-T015, which explicitly requires SimHostApp registration) has been wired. The others will be registered in BS1-T017.

3. **`DamageCalculationSystem` uses `DefaultBulletDamage` regardless of bullet type** — Per spec (POC scope). Armor penetration curves are deferred.

4. **`HealthApplicationSystem` does not destroy entities at 0 HP** — Per spec ("entity destruction is deferred"). Only capabilities are stripped.

5. **`EntityDamageEgressTranslator` cache is never pruned for dead entities** — The `Dispose(long)` method removes from cache when an entity dies, which is called by `CycloneNetworkCleanupSystem`. But if the system is not used in a topology where cleanup runs, the cache can grow unboundedly. This is consistent with existing translator disposal patterns.

---

## Temporary Hacks / Deviations

None. All implementations follow established patterns and specs cleanly.
