# BATCH-14 Report

**Batch:** BATCH-14  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE — all success criteria met

---

## Summary

| Task | Status | Notes |
|---|---|---|
| Corrective-0 (DEBT-035) | ✅ | `BehaviorIngressSystem` was already correctly fixed in a prior partial run; `stackalloc` moved outside loop (CA2014); test already present. |
| BCS-P7-T1 Scaffold | ✅ | `Fdp.Examples.UrbanCombat` builds (0 errors); `HeadlessDemoApp.Run()` stub wired correctly. |
| BCS-P7-T2 Blueprints | ✅ | 5 blueprint methods; 4 component assertion tests (all pass). |
| BCS-P7-T3 Road Graph | ✅ | `DemoEnvironmentSetup.CreateCityIntersection()` returns 5 nodes + 8 segments; 4 geometry tests pass. |
| **Full solution** | ✅ | 0 errors, 0 test failures. |
| Behavior.Tests (all 50+1) | ✅ | 51 tests pass including DEBT-035 required test. |

---

## Q1: `BrainBlackboard` memory field size

The actual memory size is **128 bytes**, defined as `BehaviorConstants.BrainBlackboardByteSize = 128` in `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`.

`BrainBlackboard` declares `public fixed byte Memory[BehaviorConstants.BrainBlackboardByteSize]` — so the constant is authoritative and already eliminates any hardcoding. In `BehaviorIngressSystem.cs`, all buffer copy calls use `BehaviorConstants.BrainBlackboardByteSize` exclusively — **no literal `128` appears anywhere in the shadow-copy logic**.

Additionally, the `stackalloc` was moved **outside the `foreach` loop** (`Span<byte> shadow = stackalloc byte[BehaviorConstants.BrainBlackboardByteSize];` appears once before the loop), eliminating the CA2014 "potential stack overflow in loop" warning that was present in the prior version.

---

## Q2: `PreviousCapabilities` and DESIGN.md §9.2 reconciliation

`PreviousCapabilities` was added in BATCH-12 as a required companion component for `HsmDamageBridgeSystem`. It does **not** appear in DESIGN.md §9.2 (which predates BATCH-12), but is needed by any entity that carries an HSM or BTree brain. The reconciliation in `EntityBlueprints.cs`:

- **`MilitaryAPC`** — `BrainHsm128` entity: `PreviousCapabilities` added, initialized to `CanMove | CanInteract` (matching `ActorCapabilityState`).
- **`InfantrySoldier`** and **`Insurgent`** — `BrainBTreeState` entities: `PreviousCapabilities` added, initialized to `CanMove | CanShoot`.
- **`CivilianPedestrian`** and **`CivilianCar`** — Tier-1, no HSM/BTree: `PreviousCapabilities` **not** added (not needed).

`PreviousCapabilities` is already registered in the demo project's world via `HeadlessDemoApp.RegisterComponents()` — no new component type registration was required beyond what `HeadlessDemoApp` already does.

Similarly, `HealthData` (BATCH-13 addition required by `MissionDirectorSystem.HealthCritical`) was added to all three damageable blueprints (APC: 500/500, Soldier: 100/100, Insurgent: 100/100) and is also registered in `RegisterComponents()`.

---

## Q3: `RoadNetworkBlob` builder API

The road network builder is `CarKinem.Road.RoadNetworkBuilder`, found at `FDP/Toolkits/FDP.Toolkit.CarKiem/Road/RoadNetworkBuilder.cs`.

API used in `DemoEnvironmentSetup.CreateCityIntersection()`:
```csharp
var builder = new RoadNetworkBuilder();
builder.AddNode(Vector2 position);             // returns void; nodes indexed by insertion order
builder.AddSegment(startPos, startTangent, endPos, endTangent, startNodeIdx, endNodeIdx);
RoadNetworkBlob blob = builder.Build(gridCellSize, gridWidth, gridHeight);
```

`RoadNetworkBlob` is a struct containing:
- `NativeArray<RoadNode> Nodes` — each `RoadNode` has `Vector2 Position`
- `NativeArray<RoadSegment> Segments`
- Implements `IDisposable` (callers must dispose)

The 4-way intersection uses 5 nodes (centre + 4 arm endpoints) and 8 directed segments (1 inbound + 1 outbound per arm). Hermite tangent magnitudes are set to `EndpointDistance * 0.5f` (50 m) to produce smooth linear-like curves for straight roads.

---

## Q4: Design decisions and dependencies discovered during scaffold wiring

1. **`EntityRepository.Tick()` takes no arguments** — the stub in `HeadlessDemoApp.Run()` incorrectly called `World.Tick(Dt)`. Fixed to `World.SetSimulationTime(frame * Dt); World.Tick();`, matching the pattern used in `Fdp.Examples.BattleRoyale`.

2. **`SimTransform`, `SimVelocity`, `HealthData` live in `Fdp.Kernel`** — the test project needed `using Fdp.Kernel;` added explicitly (the UrbanCombat main project infers it via `ImplicitUsings`, but the test project does not).

3. **`VehiclePresets.GetPreset(VehicleClass)`** — the CarKinem toolkit exposes `VehiclePresets` as a static helper that returns a populated `VehicleParams` struct by vehicle class (`Pedestrian`, `PersonalCar`, `Tank`). The blueprints use this rather than constructing `VehicleParams` from scratch.

4. **No `WeaponFireRange` / `WeaponDamage` on `WeaponState`** — `WeaponState` only carries `Ammo`, `MuzzleVelocity`, and `CooldownTicksRemaining`. Range and damage are resolved at fire-time by `FireProcessingSystem` from projectile/ballistics data; they are not per-entity fields. Weapon stats documented in DESIGN.md §9.2 (range=200m, damage=25) are encoded as projectile parameters, not as `WeaponState` fields. Blueprint `WeaponState` values set `Ammo` and `MuzzleVelocity` only.

5. **`Fdp.Examples.UrbanCombat` uses `net8.0`** — aligned with `FDP.Toolkit.Behavior.Tests` and other toolkit test projects. The `Fdp.Examples.UrbanCombat.Tests` project also targets `net8.0`.

---

## Test Summary

| Project | Tests | Result |
|---|---|---|
| FDP.Toolkit.Behavior.Tests | 51 | ✅ all pass |
| Fdp.Examples.UrbanCombat.Tests | 8 | ✅ all pass |
| **Full FDP.sln** | **677 total, 2 skipped** | ✅ **0 failures** |

New tests added in this batch (minimum 6 required, 9 delivered):

| Test | Task |
|---|---|
| `BehaviorIngress_BehaviorStateUnchanged_WhenParseParamsFails` | Corrective-0 |
| `BehaviorIngress_DoesNotThrow_WhenParseParamsFails` | Corrective-0 (pre-existing from prior partial run) |
| `Blueprint_CivilianPedestrian_HasAllRequiredComponents` | T2 |
| `Blueprint_MilitaryAPC_HasAllRequiredComponents` | T2 |
| `Blueprint_InfantrySoldier_HasAllRequiredComponents` | T2 (bonus) |
| `Blueprint_Insurgent_HasAllRequiredComponents` | T2 (bonus) |
| `DemoEnvironment_Intersection_Has5Nodes` | T3 |
| `DemoEnvironment_Intersection_Has8Segments` | T3 |
| `DemoEnvironment_Intersection_CenterNodeAtOrigin` | T3 |
| `DemoEnvironment_Intersection_ArmEndpointsAt100m` | T3 (bonus) |
