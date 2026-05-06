# BATCH-10 Report

**Batch:** BATCH-10  
**Tasks:** TASK-GZ040, TASK-GZ022, TASK-GZ023, TASK-GZ024  
**Status:** COMPLETE — all success criteria PASS; solution builds with 0 errors

---

## Summary

All four tasks were implemented and verified.  The full build (`IOS-IG-SimHost.sln`) succeeds with 0 errors.  
All 117 gizmo/stateless/stringintern tests in `Fdp.Toolkits.Tests` pass.  
466 of 470 tests in `Hrot.IG.Tests` pass; the 4 failures (CS011_*) are pre-existing `EntityInfoTranslatorTests` failures unrelated to this batch.

---

## TASK-GZ040 — StringInternMap concurrency fix

**File changed:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs`

Replaced `Dictionary<uint, string>` with `ConcurrentDictionary<uint, string>`.  Changed `TryAdd`/`TryGetValue` calls to lock-free equivalents.  Removed all manual locking.

**Test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/StringInternMapConcurrencyTests.cs`

| Criterion | Result |
|-----------|--------|
| SC-GZ040-2: Concurrent Intern calls on same string return equal IDs | PASS |
| SC-GZ040-3: Concurrent Intern calls on distinct strings return distinct IDs | PASS |
| SC-GZ040-5: Concurrent Lookup after Intern never throws | PASS |

---

## TASK-GZ022 — IStatelessGizmo contract + StatelessGizmoSystem

**New files:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatelessGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StatelessGizmoRegistry.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs`

**Test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/StatelessGizmoSystemTests.cs`

| Criterion | Result |
|-----------|--------|
| SC-GZ022-1: Register stores rule; Execute calls Draw for matching entity | PASS |
| SC-GZ022-2: Entity without all required components is skipped | PASS |
| SC-GZ022-3: Inactive entity is skipped | PASS |
| SC-GZ022-4: Multiple gizmos registered; all are called for matching entity | PASS |
| SC-GZ022-5: Visibility policy false suppresses Draw | PASS |
| SC-GZ022-6: Visibility policy true allows Draw | PASS |
| SC-GZ022-7: Register with unregistered component type throws InvalidOperationException | PASS |
| SC-GZ022-8: StatelessGizmoSystem is decorated with [UpdateInPhase(PostSimulation)] | PASS |

---

## TASK-GZ023 — Migrate pure-projector gizmos to IStatelessGizmo

**New gizmo files (Hrot.Common):**
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmoSettings.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmoSettings.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/VisibilityConeGizmo.cs`

**New gizmo files (Hrot.AI.Behaviors):**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmoSettings.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmo.cs`

**Updated:**
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` — delegates to generated `RegisterAll` methods
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — adds `StatelessGizmoRegistry`, `StatelessGizmoSystem`, calls updated registrar

**Deleted (old Definition/Instance/Settings tuples):**
- `Hrot.IG/Gizmos/HealthBarGizmoInstance.cs`, `HealthBarGizmoDefinition.cs`, `HealthBarGizmoSettings.cs`
- `Hrot.IG/Gizmos/EntityRotationGizmoInstance.cs`, `EntityRotationGizmoDefinition.cs`, `EntityRotationGizmoSettings.cs`
- `Hrot.IG/Gizmos/VisibilityConeGizmoInstance.cs`, `VisibilityConeGizmoDefinition.cs`
- `Hrot.IG/Gizmos/HillAttackGizmoInstance.cs`, `HillAttackGizmoDefinition.cs`, `HillAttackGizmoSettings.cs`

**Test files updated:**
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HealthBarGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/EntityRotationGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/VisibilityConeGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HillAttackGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmoRendererWiringTests.cs`

| Criterion | Result |
|-----------|--------|
| SC-GZ021-HB-1: HealthBarGizmo implements IStatelessGizmo | PASS |
| SC-GZ021-HB-2: HealthBarGizmo.Draw renders a bar scaled to health fraction | PASS |
| SC-GZ021-HB-3: HealthBarGizmoSettings keys are registered via GizmoSettingsRegistry | PASS |
| SC-GZ021-HB-4: HealthBarGizmo is decorated with [GizmoProjector(typeof(IgHealthState))] | PASS |
| SC-GZ021-HB-5: HealthBarGizmo registered via GizmoRegistrar.Register appears in StatelessGizmoRegistry | PASS |
| SC-GZ021-ROT-1: EntityRotationGizmo implements IStatelessGizmo | PASS |
| SC-GZ021-ROT-2: EntityRotationGizmo.Draw renders a direction arrow | PASS |
| SC-GZ021-ROT-3: EntityRotationGizmo is decorated with [GizmoProjector(typeof(SimTransform))] | PASS |
| SC-GZ021-ROT-4: EntityRotationGizmo registered via GizmoRegistrar.Register appears in StatelessGizmoRegistry | PASS |
| SC-GZ021-VIS-1: VisibilityConeGizmo implements IStatelessGizmo | PASS |
| SC-GZ021-VIS-2: VisibilityConeGizmo.Draw renders a cone for PerceptionReceptor | PASS |
| SC-GZ021-VIS-3: VisibilityConeGizmo is decorated with [GizmoProjector(typeof(SimTransform), typeof(PerceptionReceptor))] | PASS |
| SC-GZ021-HA-1: HillAttackGizmo implements IStatelessGizmo | PASS |
| SC-GZ021-HA-2: HillAttackGizmo.Draw renders slot markers when ShowSlots is true | PASS |
| SC-GZ021-HA-3: HillAttackGizmo.Draw draws nothing when ShowSlots is false | PASS |
| SC-GZ021-HA-4: HillAttackGizmoSettings keys are registered via GizmoSettingsRegistry | PASS |
| SC-GZ021-HA-5: HillAttackGizmo is decorated with [GizmoProjector(typeof(BrainBlackboard), typeof(BehaviorState), typeof(SimTransform))] | PASS |
| SC-GZ021-HA-6: HillAttackGizmo registered via GizmoRegistrar.Register appears in StatelessGizmoRegistry | PASS |

**Wiring tests:**

| Criterion | Result |
|-----------|--------|
| SC-GZ020-1: GizmoRegistry.Register is called for at least one gizmo definition | PASS |
| SC-GZ020-2: StatelessGizmoRegistry exists and has at least one rule after Register | PASS |
| SC-GZ020-3: StatelessGizmoRegistry.Register is called with correct component types | PASS |

---

## TASK-GZ024 — GizmoProjectorAttribute + Roslyn source generator

**New files:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoProjectorAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs`

**Generated at build time (by generator):**
- `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(...)` — registers HealthBarGizmo, EntityRotationGizmo, VisibilityConeGizmo
- `Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(...)` — registers HillAttackGizmo

**Test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoRegistrarGeneratorTests.cs`

| Criterion | Result |
|-----------|--------|
| SC-GZ024-1: [GizmoProjector] class implementing IStatelessGizmo appears as statelessRegistry.Register(...) in generated output | PASS |
| SC-GZ024-2: [GizmoProjector] class with GizmoSettingsRegistry constructor uses new T(settings) in generated call | PASS |
| SC-GZ024-5: [GizmoProjector] class NOT implementing IStatelessGizmo triggers FDP_002 warning and is excluded from output | PASS |

---

## Files Changed (complete list)

### New files
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatelessGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StatelessGizmoRegistry.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoProjectorAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/StringInternMapConcurrencyTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/StatelessGizmoSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoRegistrarGeneratorTests.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmoSettings.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmoSettings.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityRotationGizmo.cs`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/VisibilityConeGizmo.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmoSettings.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/HillAttackGizmo.cs`

### Modified files
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs`
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- `Hrot/Engine/Hrot.Common/Hrot.Common.csproj`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HealthBarGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/EntityRotationGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/VisibilityConeGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HillAttackGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmoRendererWiringTests.cs`

### Deleted files
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoInstance.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoDefinition.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoSettings.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoInstance.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoDefinition.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoSettings.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/VisibilityConeGizmoInstance.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/VisibilityConeGizmoDefinition.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoInstance.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoDefinition.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoSettings.cs`

---

## Test Run Results

```
Fdp.Toolkits.Tests (filter: Gizmo|StringIntern|Stateless):  Passed: 117, Failed: 0
Hrot.IG.Tests (full suite):                                  Passed: 466, Failed: 4 (pre-existing EntityInfoTranslator failures, CS011_*)
```
