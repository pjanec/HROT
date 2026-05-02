# BATCH-15 Review

**Batch:** BATCH-15  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ⚠️ NEEDS FIX — two P2 issues (BrainTier mismatch + magic numbers)

---

## Issues Found

### Issue 1: `MilitaryAPC` template sets `BehaviorState { BrainTier = 2 }` (BTree tier) but carries `BrainHsm128` (P2)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs` (line 125)

**Problem:** The report acknowledges this in "Open Debt" item 2, but calls it a "pre-existing mismatch". It is **not pre-existing** — it was introduced in BATCH-14's `EntityBlueprints.cs` and carried forward unchanged into `DemoTkbSetup`. The bug:

```csharp
// Line 125 — WRONG
t.AddComponent(new BehaviorState { BrainTier = 2 });  // 2 = BrainTierBTree
```

`BehaviorConstants.BrainTierBTree = 2`, `BehaviorConstants.BrainTierHsm = 1` (confirmed by the test helper on line 358 of `BlueprintTests.cs`, which correctly uses `BrainTierHsm`).

**Effect:** Every APC entity spawned from the TKB template will have `BehaviorState.BrainTier = 2`. `HsmTickSystem<BrainHsm128>` filters on `BrainTierHsm (= 1)` — so it will **skip the APC**. The BTree tick system will attempt to drive the APC instead (matching `BrainTier=2`) but the APC has no `BrainBTreeState`, so nothing runs. The APC's state machine never executes.

**Fix:** Change line 125 to:
```csharp
t.AddComponent(new BehaviorState { BrainTier = BehaviorConstants.BrainTierHsm });
```

**Also add a test** verifying the stamped value is correct:
```csharp
[Fact] void APC_Template_HasHsmBrainTier()
// template.ApplyTo(world, e);
// var ds = world.GetComponent<BehaviorState>(e);
// Assert.Equal(BehaviorConstants.BrainTierHsm, ds.BrainTier);
```

---

### Issue 2: Magic numbers throughout `DemoTkbSetup.cs` — CODE-STANDARDS §1 violation (P2)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs`

**Rule (CODE-STANDARDS.md §1):** No magic numbers in production code. No exceptions.

Complete audit of violations (all in `DemoTkbSetup.cs`):

| Lines | Literal | What it represents | Target constant | Location |
|---|---|---|---|---|
| 62, 97 | `SimTier { Value = 1 }` | Civilian tier | `BehaviorConstants.SimTierCivilian` | `BehaviorConstants.cs` (add) |
| 124, 173, 238 | `SimTier { Value = 2 }` | Tactical tier | `BehaviorConstants.SimTierTactical` | `BehaviorConstants.cs` (add) |
| 125 | `BehaviorState { BrainTier = 2 }` | BTree tier (also P2 bug) | `BehaviorConstants.BrainTierBTree` | Already exists ✅ |
| 82, 106, 153, 219, 284 | `CollisionLayer = 1` | Entity physics layer | `PhysicsConstants.EntityCollisionLayer` | `FDP.Toolkit.Physics/PhysicsConstants.cs` (add) |
| 82, 219, 284 | `Radius = 0.4f` | Humanoid collider radius | `UrbanCombatConstants.HumanoidColliderRadius` | `UrbanCombatConstants.cs` (add in demo project) |
| 106 | `Radius = 2f` | Car collider radius | `UrbanCombatConstants.CarColliderRadius` | `UrbanCombatConstants.cs` |
| 153 | `Radius = 3.5f` | APC collider radius | `UrbanCombatConstants.ApcColliderRadius` | `UrbanCombatConstants.cs` |
| 150–151 | `500f` (health/healthdata) | APC max health | `UrbanCombatConstants.ApcMaxHealth` | `UrbanCombatConstants.cs` |
| 200–201, 265–266 | `100f` (health/healthdata) | Soldier max health | `UrbanCombatConstants.SoldierMaxHealth` | `UrbanCombatConstants.cs` |
| 204, 207 | `Ammo = 30, MuzzleVelocity = 800f` | Rifle stats | `UrbanCombatConstants.RifleAmmo/RifleMuzzleVelocity` | `UrbanCombatConstants.cs` |
| 269, 271 | `Ammo = 1, MuzzleVelocity = 300f` | RPG stats | `UrbanCombatConstants.RpgAmmo/RpgMuzzleVelocity` | `UrbanCombatConstants.cs` |
| 75, 76 | `VisionRange = 30f, HearingRange = 100f` | Civilian perception | `UrbanCombatConstants.CivVisionRange/HearingRange` | `UrbanCombatConstants.cs` |
| 211–213, 262–264 | `VisionRange = 150f, HearingRange = 200f` | Soldier perception | `UrbanCombatConstants.SoldierVisionRange/HearingRange` | `UrbanCombatConstants.cs` |

**Private consts `FactionBlue = 1` / `FactionRed = 2`** are named locally in both `EntityBlueprints.cs` (gutted) and `DemoTkbSetup.cs` — but they are duplicated private stubs. Since they encode domain-meaningful identity, they should be in `UrbanCombatConstants.cs` and referenced from there.

**Test violations** (`BlueprintTests.cs`) — §1 exemption does *not* apply because production constants will now exist:
- Line 115: `Assert.Equal(1, faction.FactionId)` → `Assert.Equal(UrbanCombatConstants.FactionBlue, faction.FactionId)`
- Line 145: `Assert.Equal(2, faction.FactionId)` → `Assert.Equal(UrbanCombatConstants.FactionRed, faction.FactionId)`
- Line 128: `Assert.Equal(30, ws.Ammo)` → `Assert.Equal(UrbanCombatConstants.RifleAmmo, ws.Ammo)`
- Line 141: `Assert.Equal(1, ws.Ammo)` → `Assert.Equal(UrbanCombatConstants.RpgAmmo, ws.Ammo)`

**Constants to add to existing toolkit files (require no new assemblies):**

```csharp
// BehaviorConstants.cs — add:
/// <summary>SimTier value for Tier-1 civilian entities (driven by TrafficBrainSystem).</summary>
public const byte SimTierCivilian = 1;
/// <summary>SimTier value for Tier-2 tactical entities (BTree or HSM brain).</summary>
public const byte SimTierTactical = 2;

// PhysicsConstants.cs (already exists) — add:
/// <summary>CollisionLayer bitmask bit for all physical entities. Distinct from BulletCollisionLayer (bit 1).</summary>
public const int EntityCollisionLayer = 1;
```

**New file to create in demo project:**

```csharp
// FDP/Examples/Fdp.Examples.UrbanCombat/UrbanCombatConstants.cs
public static class UrbanCombatConstants
{
    // Factions
    public const byte FactionNeutral = 0;
    public const byte FactionBlue    = 1;
    public const byte FactionRed     = 2;

    // Collider radii (meters)
    public const float HumanoidColliderRadius = 0.4f;
    public const float CarColliderRadius      = 2.0f;
    public const float ApcColliderRadius      = 3.5f;

    // Health
    public const float ApcMaxHealth     = 500f;
    public const float SoldierMaxHealth = 100f;

    // Weapon stats — Rifle
    public const int   RifleAmmo           = 30;
    public const float RifleMuzzleVelocity = 800f; // m/s

    // Weapon stats — RPG
    public const int   RpgAmmo           = 1;
    public const float RpgMuzzleVelocity = 300f; // m/s

    // Perception ranges (meters)
    public const float CivilianVisionRange  = 30f;
    public const float CivilianHearingRange = 100f;
    public const float SoldierVisionRange   = 150f;
    public const float SoldierHearingRange  = 200f;
}
```

---

## Verified Correct

**Task 0 — `DemoTkbSetup`:** Full TKB pattern implemented. `RegisterAll(ITkbDatabase)` → 5 `TkbTemplate` instances → `tkb.Register(t)`. ✅  
Tests drive `_app.Tkb.GetByType(id)` → `template.ApplyTo(world, entity)`. ✅  
Soldier ammo assertion (`ws.Ammo == 30`) and Insurgent ammo assertion (`ws.Ammo == 1`) prove the correct template values are stamped at spawn time. ✅

**T4 — `TrafficBrainSystem`:** Query on `SimTier + LocomotionChannel + ActorCapabilityState`. Tier-1 filter guards entry. `HasComponent<TargetMemory>()` guard before reading (correct — civilian cars don't have TargetMemory). ✅  
Test 3 (`TrafficBrain_IgnoresTier2Entities`) asserts `channel.ActiveAction == 0` — clean proof the guard fires. ✅

**T5 — `InsurgentNodes` + `Ambush.json`:** Correct delegate signature `NodeLogicDelegate<BrainBlackboard, BTreeContext>` confirmed from Q3 source discovery. All three nodes use `ctx.World` for ECS access (no ambient state). ✅  
Test approach (inline JSON string + `TreeCompiler.CompileFromJson`) is better than file-path resolution for CI stability. ✅  
Test 2 (`AimsAtTarget`) correctly asserts `channel.ActiveAction == CombatConstants.ActionIdAimAndFire` (= 1). ✅

**T6 — `ApcHsmSetup` + `ApcHsmActions`:** Full compile pipeline (`HsmNormalizer → HsmGraphValidator → HsmFlattener → HsmEmitter`) with errors-count check is correct. ✅  
`CruisingStateIndex = 1`, `DisabledStateIndex = 2` are public constants enabling self-documenting tests. ✅  
Test 3 (`TransitionsToDisabled`) injects `Reserved1 = EventId_MobilityLost` — matches how `HsmDamageBridgeSystem` enqueues events. ✅  
`ApcHsmActions` stubs are correctly documented as deferred (DEBT-007 HSM context threading), references to intended future writes preserved as comments. ✅

**Q3 / Q4 discoveries documented:** BTree delegate signature, HsmBuilder pipeline, HSM state index BFS order — all accurately reported and correctly handled in code. ✅

---

## Verdict

**NEEDS FIX** — two P2 issues:
1. `BrainTier = 2` (magic number + wrong value) on APC template → must be `BehaviorConstants.BrainTierHsm`. One line + one test.
2. Comprehensive CODE-STANDARDS §1 violations in `DemoTkbSetup.cs` (13 literal clusters). Requires creating `UrbanCombatConstants.cs`, adding `SimTierCivilian`/`SimTierTactical` to `BehaviorConstants.cs`, adding `EntityCollisionLayer` to `PhysicsConstants.cs`, and updating all call sites in `DemoTkbSetup.cs` and four test assertions in `BlueprintTests.cs`.

---

## 📝 Commit Message (for approved content)

```
feat(BATCH-15): TKB blueprints + TrafficBrainSystem + Ambush BTree + APC HSM

Task 0 (T2 corrective): DemoTkbSetup.RegisterAll(ITkbDatabase)
  5 TkbTemplate registrations (1001–2003) via AddComponent<T>(); tkb.Register(t)
  EntityBlueprints.cs gutted; ID constants kept with [Obsolete]
  HeadlessDemoApp: TkbDatabase _tkb field + ITkbDatabase Tkb property
  +3 csproj refs: FDP.Interfaces, FDP.Toolkit.Tkb, Fhsm.Compiler
  +4 tests: all-five lookup, APC PassengerBuffer, Soldier ammo, Insurgent ammo+faction

BCS-P7-T4 — TrafficBrainSystem (SimulationSystemGroup, UpdateBefore ChannelArbitration)
  Tier-1 filter; HasComponent<TargetMemory> guard; writes MoveTo(1) / Flee(2)
  +3 tests: flee threat, move idle, ignore Tier-2

BCS-P7-T5 — InsurgentNodes + Ambush.json
  NodeLogicDelegate<BrainBlackboard, BTreeContext> signature (from source)
  Condition_HasTarget: ctx.World.GetComponent<TargetMemory>.Count > 0
  Action_AimAndFire: writes CombatConstants.ActionIdAimAndFire to WeaponChannel
  Action_HoldPosition: returns Running (fallback)
  JSON: Selector → Sequence/HoldPosition (inline in test; Assets/Ambush.json file also present)
  CombatConstants.ActionIdAimAndFire = 1 added to CombatConstants.cs
  +2 tests: hold-position (no target), aim-and-fire (target present)

BCS-P7-T6 — ApcHsmSetup + ApcHsmActions (ConvoyEscort_HSM)
  Full compile pipeline: Build → Normalize → Validate → Flatten → Emit
  Events: MobilityLost (EventId=1); States: Cruising(initial)→Disabled(on MobilityLost)
  CruisingStateIndex=1, DisabledStateIndex=2 (public constants for tests)
  ApcHsmActions: Activity_Cruise + OnEnter_Disabled stubs (DEBT-007 threading pending)
  +3 tests: build (StateCount=3), initial Cruising, transition to Disabled

Discovered facts: HsmBuilder→StateMachineGraph pipeline (not direct blob); BTree delegate
  is NodeLogicDelegate<TBlackboard,TContext>; TargetMemory field is Count not ThreatCount

To fix in BATCH-16 Corrective-0a: BehaviorState { BrainTier = 2 } → BrainTierHsm + APC_Template_HasHsmBrainTier test
To fix in BATCH-16 Corrective-0b: UrbanCombatConstants.cs + SimTierCivilian/Tactical + EntityCollisionLayer + sweep DemoTkbSetup.cs + update 4 test assertions
```

---

**Next Batch:** BATCH-16 — Two P2 correctIves (BrainTier fix + magic number sweep) + BCS-P7-T7 (ScenarioDirector) + BCS-P7-T8 (TelemetryReporterSystem) + BCS-P7-T9 (End-to-end integration test)
