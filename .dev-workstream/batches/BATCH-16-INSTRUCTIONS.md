# BATCH-16: Corrective-0 + BCS-P7-T7 + BCS-P7-T8 + BCS-P7-T9 (Phase 7 Completion)

**Batch Number:** BATCH-16  
**Tasks:**
- **Corrective-0 (P2):** `MilitaryAPC` template `BehaviorState.BrainTier` fix
- **BCS-P7-T7:** `ScenarioDirector` (entity spawning + initial state)
- **BCS-P7-T8:** `TelemetryReporterSystem` (console output)
- **BCS-P7-T9:** End-to-end 10-second integration test

**Phase:** Phase 7 — `Fdp.Examples.UrbanCombat` Demo App **(COMPLETION)**  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH — Phase 7 completion, project milestone  
**Dependencies:** BATCH-15 ✅ (modulo Corrective-0)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-15 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-15-REVIEW.md` — read Issue 1 carefully.
2. **DESIGN.md §9.1 (complete scenario), §9.5 (TelemetryReporterSystem format):** `FDP/Docs/projects/behavior-control/DESIGN.md`
3. **TASK-DETAIL.md §BCS-P7-T7, T8, T9:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
4. **`DemoTkbSetup.cs`** — read to understand the 5 entity types and their spawn parameters.
5. **`DemoEnvironmentSetup.cs`** — read the 5-node road graph topology (centre + N/S/E/W arms at ±100 m).
6. **`HeadlessDemoApp.cs`** — understand the current `Initialize()` + `Run()` structure before modifying.
7. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`

### Source Locations

| Area | Path |
|---|---|
| **Corrective-0** | `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs` ← MODIFY (1 line) |
| **Corrective-0 test** | `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/BlueprintTests.cs` ← ADD 1 test |
| **T7** | `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` ← CREATE |
| **T8** | `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs` ← CREATE |
| **T9** | `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs` ← CREATE |
| **HeadlessDemoApp wiring** | `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` ← MODIFY (wire T7, T8 into run loop) |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Examples/Fdp.Examples.UrbanCombat.Tests/
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-16-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **Corrective-0 first** — fix `DemoTkbSetup` line 125 + add BrainTier assertion test → all existing tests green ✅
2. T8 `TelemetryReporterSystem` (needed by T9 assertions) ✅
3. T7 `ScenarioDirector` ✅
4. Wire T7 + T8 into `HeadlessDemoApp.Run()` ✅
5. T9 integration test ✅
6. Full solution green ✅

---

## ✅ Tasks

### Task 0 (Corrective — P2): `MilitaryAPC` `BrainTier` fix

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs`

**Change:** Line ~125, inside `RegisterMilitaryAPC`:

```csharp
// BEFORE (wrong):
t.AddComponent(new BehaviorState { BrainTier = 2 });

// AFTER (correct):
t.AddComponent(new BehaviorState { BrainTier = BehaviorConstants.BrainTierHsm });
```

`BehaviorConstants.BrainTierHsm = 1`. This ensures `HsmTickSystem<BrainHsm128>` processes the APC (it filters on `BrainTier == BrainTierHsm`).

**New test (add to `BlueprintTests.cs`):**

```csharp
[Fact]
public void APC_Template_HasHsmBrainTier()
{
    var template = _app.Tkb.GetByType(2001)!;
    var e = _app.World.CreateEntity();
    template.ApplyTo(_app.World, e);
    var ds = _app.World.GetComponent<BehaviorState>(e);
    Assert.Equal(BehaviorConstants.BrainTierHsm, ds.BrainTier);  // must be 1, not 2
}
```

---

### Task 0b (Corrective — P2): Magic number sweep (CODE-STANDARDS §1)

**Background:** Read BATCH-15-REVIEW Issue 2 in full before starting.  
**Scope:** 3 files to modify in toolkits + 1 new file + 1 call-site sweep + 4 test assertion fixes.  
**Must be done before T7** — `ScenarioDirector` will use `UrbanCombatConstants` for spawn positions and `BehaviorConstants.SimTierCivilian/Tactical`.

#### Step 1 — Add constants to `BehaviorConstants.cs`

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`

Add after the existing `BrainTierBTree` constant:

```csharp
/// <summary>
/// SimTier value for Tier-1 civilian entities, driven by <see cref="Systems.TrafficBrainSystem"/>.
/// </summary>
public const byte SimTierCivilian = 1;

/// <summary>
/// SimTier value for Tier-2 tactical entities driven by BTree or HSM brains.
/// </summary>
public const byte SimTierTactical = 2;
```

#### Step 2 — Add `EntityCollisionLayer` to `PhysicsConstants.cs`

**File:** Look up the actual path to `PhysicsConstants.cs` — it is in `FDP/Toolkits/FDP.Toolkit.Physics/`. Add:

```csharp
/// <summary>
/// CollisionLayer bitmask for all physical (non-bullet) entities.
/// Rays fired at layer mask <c>EntityCollisionLayer</c> will hit soldiers, vehicles, etc.
/// Distinct from <see cref="CombatConstants.BulletCollisionLayer"/> (bit 1).
/// </summary>
public const int EntityCollisionLayer = 1;
```

> Verify the file exists and is the canonical location for physics constants before adding. If `PhysicsConstants.cs` does not exist, check `CombatConstants.cs` — `BulletCollisionLayer = 2` is already there, so add `EntityCollisionLayer = 1` there instead (same class, neighboring constant).

#### Step 3 — Create `UrbanCombatConstants.cs`

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/UrbanCombatConstants.cs` ← **CREATE**

```csharp
namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Compile-time constants for the Urban Ambush scenario.
    /// Centralised here so a single edit propagates to all blueprint and spawn sites.
    /// See CODE-STANDARDS.md §1 (No magic numbers in production code).
    /// </summary>
    public static class UrbanCombatConstants
    {
        // ── Faction IDs (DESIGN.md §4.1) ─────────────────────────────────────────
        /// <summary>Neutral faction — civilians, environmental entities.</summary>
        public const byte FactionNeutral = 0;
        /// <summary>Blue force — friendly military (APC, infantry soldiers).</summary>
        public const byte FactionBlue    = 1;
        /// <summary>Red force — adversary (insurgents).</summary>
        public const byte FactionRed     = 2;

        // ── Collider radii (meters) ───────────────────────────────────────────────
        /// <summary>Collision radius for humanoid entities (soldiers, civilians, insurgents).</summary>
        public const float HumanoidColliderRadius = 0.4f;
        /// <summary>Collision radius for civilian car entities.</summary>
        public const float CarColliderRadius      = 2.0f;
        /// <summary>Collision radius for Military APC entities.</summary>
        public const float ApcColliderRadius      = 3.5f;

        // ── Health ────────────────────────────────────────────────────────────────
        /// <summary>Starting and maximum hit-points for the Military APC.</summary>
        public const float ApcMaxHealth     = 500f;
        /// <summary>Starting and maximum hit-points for infantry soldiers and insurgents.</summary>
        public const float SoldierMaxHealth = 100f;

        // ── Weapon stats: Rifle (InfantrySoldier) ────────────────────────────────
        /// <summary>Magazine capacity for the standard infantry rifle.</summary>
        public const int   RifleAmmo           = 30;
        /// <summary>Muzzle velocity of the standard infantry rifle (m/s).</summary>
        public const float RifleMuzzleVelocity = 800f;

        // ── Weapon stats: RPG (Insurgent) ─────────────────────────────────────────
        /// <summary>Single-round capacity of the insurgent RPG launcher.</summary>
        public const int   RpgAmmo           = 1;
        /// <summary>Projectile speed of the RPG round (m/s).</summary>
        public const float RpgMuzzleVelocity = 300f;

        // ── Perception ranges (meters) ────────────────────────────────────────────
        /// <summary>Vision range for civilian pedestrians.</summary>
        public const float CivilianVisionRange  = 30f;
        /// <summary>Hearing range for civilian pedestrians.</summary>
        public const float CivilianHearingRange = 100f;
        /// <summary>Vision range for military soldiers and insurgents.</summary>
        public const float SoldierVisionRange   = 150f;
        /// <summary>Hearing range for military soldiers and insurgents.</summary>
        public const float SoldierHearingRange  = 200f;
    }
}
```

#### Step 4 — Sweep `DemoTkbSetup.cs`

Replace every magic literal. Complete replacement table:

| Old literal | Replacement |
|---|---|
| `SimTier { Value = 1 }` (×2) | `SimTier { Value = BehaviorConstants.SimTierCivilian }` |
| `SimTier { Value = 2 }` (×3) | `SimTier { Value = BehaviorConstants.SimTierTactical }` |
| `BehaviorState { BrainTier = 2 }` | `BehaviorState { BrainTier = BehaviorConstants.BrainTierBTree }` *(also required for BTree entities — APC must still use `BrainTierHsm`, see Task 0a)* |
| `CollisionLayer = 1` (×5) | `CollisionLayer = PhysicsConstants.EntityCollisionLayer` *(or `CombatConstants.EntityCollisionLayer` — use wherever the constant was placed in Step 2)* |
| `Radius = 0.4f` (×3) | `Radius = UrbanCombatConstants.HumanoidColliderRadius` |
| `Radius = 2f` | `Radius = UrbanCombatConstants.CarColliderRadius` |
| `Radius = 3.5f` | `Radius = UrbanCombatConstants.ApcColliderRadius` |
| `Health { Current = 500f, Max = 500f }` | `Health { Current = UrbanCombatConstants.ApcMaxHealth, Max = UrbanCombatConstants.ApcMaxHealth }` |
| `HealthData { Current = 500f, Max = 500f }` | `HealthData { Current = UrbanCombatConstants.ApcMaxHealth, Max = UrbanCombatConstants.ApcMaxHealth }` |
| `Health { Current = 100f, Max = 100f }` (×2) | `Health { Current = UrbanCombatConstants.SoldierMaxHealth, Max = UrbanCombatConstants.SoldierMaxHealth }` |
| `HealthData { Current = 100f, Max = 100f }` (×2) | `HealthData { Current = UrbanCombatConstants.SoldierMaxHealth, Max = UrbanCombatConstants.SoldierMaxHealth }` |
| `Ammo = 30` | `Ammo = UrbanCombatConstants.RifleAmmo` |
| `MuzzleVelocity = 800f` | `MuzzleVelocity = UrbanCombatConstants.RifleMuzzleVelocity` |
| `Ammo = 1` | `Ammo = UrbanCombatConstants.RpgAmmo` |
| `MuzzleVelocity = 300f` | `MuzzleVelocity = UrbanCombatConstants.RpgMuzzleVelocity` |
| `VisionRange = 30f` | `VisionRange = UrbanCombatConstants.CivilianVisionRange` |
| `HearingRange = 100f` | `HearingRange = UrbanCombatConstants.CivilianHearingRange` |
| `VisionRange = 150f` (×2) | `VisionRange = UrbanCombatConstants.SoldierVisionRange` |
| `HearingRange = 200f` (×2) | `HearingRange = UrbanCombatConstants.SoldierHearingRange` |
| `private const byte FactionBlue = 1;` / `FactionRed = 2;` / `FactionNeutral = 0;` | **Remove** the private stubs; add `using Fdp.Examples.UrbanCombat;` and use `UrbanCombatConstants.FactionBlue` etc. |

After the sweep, run a final `grep` for any remaining numeric literals in `DemoTkbSetup.cs` that are not `0` (the "no value" identity, which is acceptable as a default struct initialiser):
```powershell
grep -n "[0-9]\+[f]\?\b" FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs
```
If any remain, add constants for them.

#### Step 5 — Fix `BlueprintTests.cs` test assertions

In `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/BlueprintTests.cs`:

```csharp
// Line 115 — was:  Assert.Equal(1, faction.FactionId);
Assert.Equal(UrbanCombatConstants.FactionBlue, faction.FactionId);

// Line 145 — was:  Assert.Equal(2, faction.FactionId);
Assert.Equal(UrbanCombatConstants.FactionRed, faction.FactionId);

// Line 128 — was:  Assert.Equal(30, ws.Ammo);
Assert.Equal(UrbanCombatConstants.RifleAmmo, ws.Ammo);

// Line 141 — was:  Assert.Equal(1, ws.Ammo);
Assert.Equal(UrbanCombatConstants.RpgAmmo, ws.Ammo);
```

Add `using Fdp.Examples.UrbanCombat;` to the test file's using directives.

---

### Task 7: `ScenarioDirector` (BCS-P7-T7)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs`  
**Task Definition:** TASK-DETAIL.md §BCS-P7-T7 — read in full.  
**Design reference:** DESIGN.md §9.1 (spawn table).

**Class:**
```csharp
public class ScenarioDirector
{
    public ScenarioDirector(EntityRepository world, ITkbDatabase tkb, RoadNetworkBlob road) { ... }

    /// <summary>
    /// Spawns the full Urban Ambush cast at their initial positions and sets up
    /// embark state for the four soldiers in the APC.
    /// </summary>
    public void SetupAmbushScenario() { ... }
}
```

**Spawn manifest (DESIGN.md §9.1):**

| Count | TKB ID | Archetype | Spawn positions | Initial behavior | Notes |
|---|---|---|---|---|---|
| 5 | 1001 | CivilianPedestrian | Scattered around intersection (±30–50 m from centre) | `WanderCivil` | No embark |
| 3 | 1002 | CivilianCar | On road arms N/S/E | `WanderCivil` | No embark |
| 1 | 2001 | MilitaryAPC | South arm start `(0, -80, 0)`, heading north | `ConvoyEscort` | PassengerBuffer pre-filled |
| 4 | 2002 | InfantrySoldier | Same position as APC `(0, -80, 0)` | `InfantryCombat` | Embarked in APC |
| 1 | 2003 | Insurgent | Building corner `(60, 20, 0)` | `Ambush` | Hidden |

**Spawn pattern:**
```csharp
// For each entity:
var template = _tkb.GetByType(tkbTypeId);
var entity   = _world.CreateEntity();
template.ApplyTo(_world, entity);

// Set spawn position (Phase 0 adaptation — SimTransform, not VehicleState):
ref var tf = ref _world.GetComponentRW<SimTransform>(entity);
tf.Position = spawnPos;
tf.Rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);

// Assign initial behavior:
ref var behavior = ref _world.GetComponentRW<BehaviorState>(entity);
behavior.ActiveBehaviorHash = BehaviorIds.[BehaviorId];
unchecked { behavior.InstanceId++; }   // trigger ChannelArbitrationSystem preemption
behavior.BrainTier = BehaviorConstants.BrainTierBTree; // or Hsm/None depending on archetype
```

**Embark setup (4 soldiers in APC):**
After spawning all soldiers, use `EmbarkExecutor` directly (or manual buffer setup) to pre-fill `PassengerBuffer` on the APC and add `IsEmbarkedTag` + strip capabilities on each soldier:

```csharp
// Manual embark at spawn (no distance check needed — it's setup time):
ref var buffer = ref _world.GetComponentRW<PassengerBuffer>(apc);
foreach (var soldier in soldiers)
{
    buffer.Passengers[buffer.Count] = soldier;
    buffer.Count++;

    ref var caps = ref _world.GetComponentRW<ActorCapabilityState>(soldier);
    caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

    _world.AddComponent(soldier, new IsEmbarkedTag { VehicleEntity = apc });
}
```

**Tests:**
```csharp
[Fact] void ScenarioDirector_SpawnsExpectedEntityCount()
// After SetupAmbushScenario:
// Query all entities with SimTransform → count == 14
// (5 pedestrians + 3 cars + 1 APC + 4 soldiers + 1 insurgent)

[Fact] void ScenarioDirector_SoldiersAreEmbarked_Initially()
// Query entities with IsEmbarkedTag → count == 4

[Fact] void ScenarioDirector_InsurgentHasRedFaction()
// Query entity with Faction.FactionId == 2 → count == 1

[Fact] void ScenarioDirector_APC_HasFourPassengers_Initially()
// Query entity with PassengerBuffer → buffer.Count == 4
```

> ⚠️ Verify `BehaviorIds` constants from `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorIds.cs` before using them. The IDs defined there (e.g. `WanderCivil = 1001`, `ConvoyEscort = 2001`, `Ambush = 2003`) must be registered with the `BehaviorRegistry` in `HeadlessDemoApp.Initialize()` — add any missing registrations there.

---

### Task 8: `TelemetryReporterSystem` (BCS-P7-T8)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs`  
**Task Definition:** TASK-DETAIL.md §BCS-P7-T8. **Design reference:** DESIGN.md §9.5.

**System class:**
```csharp
[UpdateInGroup(typeof(ExportSystemGroup))]
public class TelemetryReporterSystem : ComponentSystem
```

**Output format** (`[FRAME NNNN] EVENT: ...`):

| Event | Trigger | Output |
|---|---|---|
| `BEHAVIOR ASSIGNED` | `BehaviorState.InstanceId` changes | `[FRAME 0001] BEHAVIOR ASSIGNED: entity {idx} → {behaviorName}` |
| `GUNFIRE` | `FireRequestEvent` on bus | `[FRAME NNNN] GUNFIRE: entity {shooter}` |
| `HIT` | `HitEvent` on bus | `[FRAME NNNN] HIT: target {target}, damage {dmg}` |
| `CAPABILITY LOST` | `CanMove` cleared (compare vs prev frame) | `[FRAME NNNN] CAPABILITY LOST: entity {idx} CanMove` |
| `HSM TRANSITION` | `BrainHsm128` state index changes | `[FRAME NNNN] HSM TRANSITION: entity {idx} → state {newIdx}` |
| `INTERACTION: EjectPassengers` | `InteractionChannel.ActiveAction == 3` | `[FRAME NNNN] INTERACTION: EjectPassengers on entity {idx}` |
| `FLEE` | `LocomotionChannel.ActiveAction == NavigationConstants.ActionIdFlee` | `[FRAME NNNN] FLEE: entity {idx}` |

**State tracking:** The system must track previous-frame values for `BehaviorState.InstanceId`, `BrainHsm128.State.ActiveLeafIds[0]`, and `ActorCapabilityState.Capabilities`. Use the same per-entity shadow pattern used elsewhere in the codebase (`PreviousCapabilities` exists already; may need additional shadow components or a `Dictionary<Entity, ...>` for the reporter — the latter is acceptable since this is a telemetry/debug-only system).

**Tests:**
```csharp
[Fact] void Telemetry_PrintsGunfireEvent_WhenFireRequestPublished()
// Redirect Console.Out to StringWriter.
// Publish FireRequestEvent via world.Bus.
// Run TelemetryReporterSystem.
// Assert output.Contains("GUNFIRE").

[Fact] void Telemetry_PrintsHitEvent_WhenHitEventPublished()
// Publish HitEvent. Run system. Assert output.Contains("HIT").

[Fact] void Telemetry_PrintsFleeEvent_WhenLocomotionChannelSetToFlee()
// Entity with LocomotionChannel.ActiveAction = NavigationConstants.ActionIdFlee.
// Run system. Assert output.Contains("FLEE").
```

---

### Task 9: End-to-End Integration Test (BCS-P7-T9)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs`  
**Task Definition:** TASK-DETAIL.md §BCS-P7-T9. **Design reference:** DESIGN.md §9.1.

**Test strategy:** Run the full 600-frame simulation via `HeadlessDemoApp`. Redirect `Console.Out` to a `StringWriter`. Assert that key scenario milestone strings appear in the output log.

The test must wire up:
1. `HeadlessDemoApp.Initialize()` — registers components, systems, TKB, behaviors.
2. `ScenarioDirector.SetupAmbushScenario()` — spawns all 14 entities.
3. `app.RunSimulation(600)` — 600 ticks at 1/60s = 10 seconds.
4. Assert log milestones (order-dependent).

**Test:**
```csharp
[Fact]
public void UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones()
{
    using var app = new HeadlessDemoApp();
    app.Initialize();

    using var output = new StringWriter();
    Console.SetOut(output);

    var director = new ScenarioDirector(app.World, app.Tkb, app.Road);
    director.SetupAmbushScenario();

    app.RunSimulation(600);

    var log = output.ToString();

    // Every milestone in order (Assert.Contains does not enforce order —
    // use IndexOf for strict ordering if needed):
    Assert.Contains("BEHAVIOR ASSIGNED", log);       // Frame 1 — initial behaviors applied
    Assert.Contains("GUNFIRE",           log);       // ~Frame 181 — insurgent fires
    Assert.Contains("HIT",               log);       // ~Frame 182 — APC hit
    Assert.Contains("CAPABILITY LOST",   log);       // ~Frame 182 — APC mobility lost
    Assert.Contains("HSM TRANSITION",    log);       // ~Frame 183 — APC enters Disabled state
    Assert.Contains("INTERACTION: EjectPassengers", log); // ~Frame 184
    Assert.Contains("FLEE",              log);       // ~Frame 185+ — civilians flee
}

[Fact]
public void UrbanAmbush_ApcMovesNorthward_BeforeAmbush()
{
    using var app = new HeadlessDemoApp();
    app.Initialize();

    var director = new ScenarioDirector(app.World, app.Tkb, app.Road);
    director.SetupAmbushScenario();

    // Run 100 frames — APC should have moved north from Y=-80 toward centre
    app.RunSimulation(100);

    // Find the APC entity (has BrainHsm128)
    var q = app.World.Query().With<SimTransform>().With<BrainHsm128>().Build();
    foreach (var e in q)
    {
        var tf = app.World.GetComponent<SimTransform>(e);
        Assert.True(tf.Position.Y > -90f, $"APC should have moved north; Y={tf.Position.Y}");
    }
}
```

> ⚠️ **T9 is the hardest test to pass** because it requires the full system pipeline to be correctly registered in `HeadlessDemoApp.Initialize()`. If T9 fails, diagnose via:
> 1. Check `TelemetryReporterSystem` is registered in the correct group (`ExportSystemGroup`).
> 2. Check behavior registrations match the `BehaviorIds` constants.
> 3. Check the APC's `BehaviorState.BrainTier` is `BrainTierHsm` (Corrective-0).
> 4. Check `HsmDamageBridgeSystem` and `DamageSystem` are registered so the MobilityLost chain fires.

**`HeadlessDemoApp` changes for T7/T8 wiring:**
- Add `RoadNetworkBlob Road { get; private set; }` property.
- In `Initialize()`: create the road blob via `DemoEnvironmentSetup.CreateCityIntersection()`, store as `Road`. Register `TelemetryReporterSystem`.
- In `RunSimulation(int frames)`: actual simulation loop (not a stub).

---

## 🧪 Testing Requirements

- **Minimum 9 new tests:**
  - Corrective-0: 1 (`APC_Template_HasHsmBrainTier`)
  - T7: 4 (entity count, embarked count, faction, APC passenger count)
  - T8: 3 (gunfire, hit, flee telemetry)
  - T9: 2 (full run milestones, APC northward movement)
- **All existing tests remain green.**
- **T9 must actually run the full 600-frame loop** — no mocks. If the loop has a bug, fix the underlying system, not the test.

---

## ⚠️ Quality Standards

**❗ Corrective-0 is the first thing to code.** All other work depends on the APC's BrainTier being correct.

**❗ `HeadlessDemoApp.RunSimulation(int)` must be a real loop** — `World.SetSimulationTime(frame * Dt); World.Tick();`. The BATCH-14 stub must be replaced with an actual loop before T9 can pass.

**❗ System registration order** in `HeadlessDemoApp.Initialize()`:
- Input group: `BehaviorIngressSystem`
- Simulation group: `TrafficBrainSystem`, `BTreeTickSystem`, `HsmTickSystem<BrainHsm128>`, `HsmDamageBridgeSystem`, `ChannelArbitrationSystem`, `LocomotionDispatcherSystem`, `WeaponDispatcherSystem`, `InteractionDispatcherSystem`, `MissionDirectorSystem`, `FireProcessingSystem`, `DamageSystem`, `VisionBroadphaseSystem`, `ThreatEvaluationSystem`, `AudioPerceptionSystem`
- PostSimulation group: `BallisticsSystem`, `LinearKinematicsSystem`, `SpatialHashSystem`, `CarKinematicsSystem`
- Export group: `TelemetryReporterSystem`

> Verify actual group type names from `Fdp.Kernel.StandardSystemGroups` before registering. Confirm that `PostSimulationSystemGroup` was added in BATCH-11.

**❗ Behavior registration in `HeadlessDemoApp.Initialize()`:**

Register behaviors with `BehaviorRegistry` using the correct `BehaviorIds` constants and matching `BehaviorDefinition` specs (BrainTier, HSM blob for APC, BTree interpreters built from JSON for soldiers and insurgent):

```csharp
// HSM behavior for APC:
_behaviorRegistry.Register(BehaviorIds.ConvoyEscort, "ConvoyEscort", new BehaviorDefinition
{
    Name          = "ConvoyEscort",
    BrainTier     = BehaviorConstants.BrainTierHsm,
    HsmDefinition = ApcHsmSetup.Build(),
});

// BTree behaviors (soldiers, civilians, insurgent):
// Use TreeCompiler.CompileFromJson + ActionRegistry pattern (see T5 tests)
```

**❗ `ScenarioDirector` must use `BehaviorIds` constants** — no raw integer literals for behavior IDs.

**❗ `TelemetryReporterSystem` console writes** — use `Console.Out.WriteLine(...)` not `Console.WriteLine(...)` directly, so the `StringWriter` redirect in T9 captures everything.

**❗ Dispose `RoadNetworkBlob`** — it contains `NativeArray`. `HeadlessDemoApp.Dispose()` must call `Road.Dispose()` if `Road` is not null.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-16-REPORT.md`

**Q1:** What system group names does `HeadlessDemoApp.Initialize()` use? Are they the same types used in `[UpdateInGroup]` attributes (e.g. `ExportSystemGroup`), and if not, how does the registration differ?

**Q2:** How did you register BTree interpreters (for soldiers and insurgents) with the `BehaviorRegistry`? Did the `BehaviorDefinition` take a pre-built `Interpreter<BrainBlackboard, BTreeContext>` or a `FbtBlob`?

**Q3:** For T9, which milestones in the expected log actually appeared in the 600-frame run? Were any missing, and if so, what was the root cause?

**Q4:** How does `TelemetryReporterSystem` detect HSM state transitions — shadow dictionary, shadow component, or event bus?

**Q5:** Any surprises?

---

## 🎯 Success Criteria

- [ ] **Corrective-0** — `BrainTierHsm` on APC template; `APC_Template_HasHsmBrainTier` test passes.
- [ ] **BCS-P7-T7** — `ScenarioDirector.SetupAmbushScenario()` spawns 14 entities, 4 embarked, 1 red faction, APC with 4 passengers; 4 tests pass.
- [ ] **BCS-P7-T8** — `TelemetryReporterSystem` in `ExportSystemGroup`; 3 unit tests pass.
- [ ] **BCS-P7-T9** — Full 600-frame simulation runs; 7 milestone strings in log + APC northward movement test; 2 tests pass.
- [ ] **Full solution: 0 errors.**
- [ ] **All tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **BATCH-15 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-15-REVIEW.md`
- **TASK-DETAIL §T7, T8, T9:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
- **DESIGN.md §9.1, §9.5:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **`DemoTkbSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs`
- **`DemoEnvironmentSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoEnvironmentSetup.cs`
- **`HeadlessDemoApp.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`
- **`BehaviorIds.cs`:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorIds.cs`
- **`ApcHsmSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
- **`InsurgentNodes.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs`
- **`StandardSystemGroups.cs`:** `FDP/Kernel/Fdp.Kernel/` (system group types)
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
