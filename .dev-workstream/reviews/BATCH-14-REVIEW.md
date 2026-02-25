# BATCH-14 Review

**Batch:** BATCH-14  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ⚠️ NEEDS FIX — T2 (Blueprints do not use TKB)

---

## Issues Found

### Issue 1: `EntityBlueprints` bypasses TKB entirely (P1)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Blueprints/EntityBlueprints.cs`

**Problem:** The five blueprint methods are manual `world.AddComponent(...)` factory functions — they do not use the `TkbTemplate` + `TkbDatabase` system at all. The project has `Id_CivilianPedestrian = 1001` etc. as constants, but these IDs are never registered with a `TkbDatabase` and the TKB is never used at spawn time.

The correct pattern established by `TankTemplate.cs` in `Fdp.Examples.NetworkDemo` is:

```csharp
// Pattern from TankTemplate.cs:
public static void Register(ITkbDatabase tkb)
{
    var tank = new TkbTemplate("CommandTank", tkbType: 100);
    tank.AddComponent(new SimTransform { ... });
    tank.AddComponent(new SimVelocity { ... });
    // ... all components via template ...
    tkb.Register(tank);
}
```

And spawn:
```csharp
var template = tkb.GetByType(100);
var entity = world.CreateEntity();
template.ApplyTo(world, entity);
```

The UrbanCombat blueprints must follow this pattern. The current implementation:
- Is not composable with the TKB spawn/lifecycle pipeline.
- Cannot be retrieved by type ID later (by `ScenarioDirector`, `TrafficBrainSystem`, etc.).
- Does not participate in `TkbDatabase.GetAll()` enumeration.
- Defeats the purpose of the TKB field on DESIGN.md §9.2 which references TKB type IDs explicitly.

**Fix required:**
1. Refactor `EntityBlueprints.cs` into a `UrbanCombatBlueprints` class (or keep `EntityBlueprints` name) with a single `Register(ITkbDatabase tkb)` static method.
2. Each of the 5 entity types becomes a `TkbTemplate` with `AddComponent<T>()` calls (matching the exact same component set as currently in BATCH-14, which is correct — only the registration mechanism changes).
3. The TKB type IDs `1001`–`2003` map to `TkbTemplate("CivilianPedestrian", 1001)` etc.
4. In `HeadlessDemoApp`, construct a `TkbDatabase`, call `EntityBlueprints.Register(tkb)`, and store it for use by `ScenarioDirector`.
5. Update blueprint tests: instead of calling `EntityBlueprints.CivilianPedestrian(world)`, resolve the template from `tkb.GetByType(1001)`, then `CreateEntity()` then `template.ApplyTo(world, entity)`.

**`FDP.Toolkit.Tkb` / `FDP.Interfaces` references** — verify these are already in `Fdp.Examples.UrbanCombat.csproj`. If not, add the project reference. The `ITkbDatabase` interface is in `FDP.Interfaces`. The `TkbDatabase` concrete class is in `FDP.Toolkit.Tkb` (separate). The demo project needs at minimum `FDP.Interfaces` for `ITkbDatabase`/`TkbTemplate`.

---

## Verified Correct

**Corrective-0 (DEBT-035):** `stackalloc` outside loop confirmed (CA2014 fix); `BehaviorConstants.BrainBlackboardByteSize` used throughout (no hardcoded 128). ✅

**BCS-P7-T1 Scaffold:** `HeadlessDemoApp.Run()` uses `World.SetSimulationTime` + `World.Tick()` (not `Tick(Dt)`) — correct pattern from `Fdp.Examples.BattleRoyale`. ✅

**BCS-P7-T3 Road Graph:** `RoadNetworkBuilder` API used correctly; 5 nodes + 8 directed segments; `NativeArray` disposed correctly via `IDisposable`. 4 geometry tests confirm topology. ✅

**DEBT-035 test:** `DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails` — asserts both `ActiveDoctrineHash == OldId` AND `InstanceId == 0` (neither was bumped). ✅

**Component additions vs DESIGN.md §9.2:** `PreviousCapabilities` and `HealthData` correctly back-ported to the three damageable blueprints. Well-documented in both XML comments and Q2. ✅

---

## Verdict

**NEEDS FIX** — T2 blueprints must be refactored to use `TkbTemplate` + `TkbDatabase`. All other tasks are clean. T1/T3/Corrective-0 are **not** re-reviewed — only T2 and the `HeadlessDemoApp` TKB wiring change.

---

**Required Actions (BATCH-15 Task 0):**
1. Refactor `EntityBlueprints.cs` → `Register(ITkbDatabase tkb)` pattern, 5 `TkbTemplate` registrations.
2. Add `FDP.Interfaces` (and/or `FDP.Toolkit.Tkb`) project reference to `Fdp.Examples.UrbanCombat.csproj` if not already present.
3. Wire `TkbDatabase` in `HeadlessDemoApp.RegisterComponents()`.
4. Update blueprint tests to use `tkb.GetByType(id)` + `template.ApplyTo(world, entity)`.
5. All 677+ tests remain green.

---

## 📝 Commit Message (for approved parts)

```
feat(BATCH-14): Corrective-0 + Phase 7 scaffold, road graph; blueprints need TKB rework

Corrective-0 (DEBT-035):
  DoctrineIngressSystem: stackalloc shadow moved outside foreach (CA2014 fix)
  ParseParams attempted on shadow before any DoctrineState write
  DoctrineState/BrainBTreeState mutated only on successful parse
  +1 test: DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails (InstanceId + hash unchanged)
  DEBT-008 now fully resolved via DEBT-035

BCS-P7-T1 — Fdp.Examples.UrbanCombat project scaffold
  Fdp.Examples.UrbanCombat.csproj (net8.0) with all toolkit ProjectReferences
  HeadlessDemoApp: World.SetSimulationTime + World.Tick(); 600-frame stub loop
  RegisterComponents(): all 25 component types pre-registered

BCS-P7-T3 — DemoEnvironmentSetup.CreateCityIntersection()
  RoadNetworkBuilder: 5 nodes (centre + 4 arms @ ±100 m) + 8 directed segments
  Hermite tangent magnitude = 50 m (smooth straight road approximation)
  +4 tests: node count, segment count, centre at origin, arms at 100 m

BCS-P7-T2 — EntityBlueprints (INCOMPLETE — TKB not used, see BATCH-15)

Tests: +9 new (1 Corrective-0, 4 blueprints, 4 road graph); FDP.sln 0 errors, 0 failures
```
