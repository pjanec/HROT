# BATCH-15: BCS-P7-T2 TKB Rework + BCS-P7-T4 + BCS-P7-T5 + BCS-P7-T6

**Batch Number:** BATCH-15  
**Tasks:**
- **Task 0 (Corrective — P1):** Rework `EntityBlueprints` → `DemoTkbSetup` using `TkbTemplate` + `TkbDatabase`
- **BCS-P7-T4:** `TrafficBrainSystem` (Tier-1 hardcoded brain)
- **BCS-P7-T5:** Insurgent BTree nodes + `Ambush.json` authoring
- **BCS-P7-T6:** APC HSM authoring (`HsmBuilder` + action methods)

**Phase:** Phase 7 — `Fdp.Examples.UrbanCombat` Demo App (middle)  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH — Task 0 is P1 and unblocks T7 (ScenarioDirector)  
**Dependencies:** BATCH-14 ✅ (T1, T3 approved; T2 needs rework)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-14 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-14-REVIEW.md` — read Issue 1 in full before touching any code.
2. **`TankTemplate.cs` in `Fdp.Examples.NetworkDemo`:** `FDP/Examples/Fdp.Examples.NetworkDemo/Configuration/TankTemplate.cs` — this is the canonical TKB registration pattern to follow.
3. **`TkbTemplate.cs` (the interface):** `FDP/Common/FDP.Interfaces/Abstractions/TkbTemplate.cs` — read `AddComponent<T>()` and `ApplyTo()` API.
4. **`TkbDatabase.cs`:** `FDP/Toolkits/FDP.Toolkit.Tkb/TkbDatabase.cs` — `Register(TkbTemplate)`, `GetByType(long)`.
5. **TASK-DETAIL.md §BCS-P7-T2, T4, T5, T6:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
6. **DESIGN.md §9.1, §9.2, §9.4:** `FDP/Docs/projects/behavior-control/DESIGN.md`
7. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`

### Source Locations

| Area | Path |
|---|---|
| **Task 0 — rename/rework** | `FDP/Examples/Fdp.Examples.UrbanCombat/Blueprints/EntityBlueprints.cs` ← DELETE (or repurpose) |
| **Task 0 — new file** | `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs` ← CREATE |
| **Task 0 — HeadlessDemoApp wiring** | `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` ← MODIFY |
| **Task 0 — test rework** | `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/EntityBlueprintTests.cs` ← MODIFY (or rename) |
| **T4** | `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TrafficBrainSystem.cs` ← CREATE |
| **T5 nodes** | `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` ← CREATE |
| **T5 JSON** | `FDP/Examples/Fdp.Examples.UrbanCombat/Assets/Ambush.json` ← CREATE |
| **T6 HSM** | `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs` ← CREATE |
| **T6 actions** | `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs` ← CREATE |
| **Tests** | `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/` |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Examples/Fdp.Examples.UrbanCombat.Tests/
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-15-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **Task 0 first** — `DemoTkbSetup` registered + tests updated → build green ✅
2. T4 `TrafficBrainSystem` + tests ✅
3. T5 Insurgent BTree + JSON + tests ✅
4. T6 APC HSM + action methods + tests ✅
5. Full solution green ✅

---

## ✅ Tasks

### Task 0 (P1 Corrective): `DemoTkbSetup` — TKB Blueprint Registration (BCS-P7-T2)

**Issue:** Current `EntityBlueprints.cs` calls `world.AddComponent(...)` directly; it is not a TKB registration. **Delete or empty the file**; replace with `DemoTkbSetup`.

**Task Definition:** TASK-DETAIL.md §BCS-P7-T2 — file should be `Setup/DemoTkbSetup.cs`.

**Pattern to follow (from `TankTemplate.cs`):**
```csharp
public static class DemoTkbSetup
{
    public static void RegisterAll(ITkbDatabase tkb)
    {
        RegisterCivilianPedestrian(tkb);
        RegisterCivilianCar(tkb);
        RegisterMilitaryAPC(tkb);
        RegisterInfantrySoldier(tkb);
        RegisterInsurgent(tkb);
    }

    private static void RegisterCivilianPedestrian(ITkbDatabase tkb)
    {
        var t = new TkbTemplate("CivilianPedestrian", tkbType: 1001);
        t.AddComponent(new SimTransform());
        t.AddComponent(new SimVelocity());
        t.AddComponent(new SimTier { Value = 1 });
        t.AddComponent(new BehaviorState());
        t.AddComponent(new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
        t.AddComponent(new LocomotionChannel());
        t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });  // Phase 0 note: no Position/Forward
        t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
        t.AddComponent(new NavState());
        t.AddComponent(new PerceptionReceptor { VisionRange = 30f, HearingRange = 100f, FieldOfViewCos = 0f });
        t.AddComponent(new TargetMemory());
        t.AddComponent(new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
        tkb.Register(t);
    }
    // ... same pattern for the other 4
}
```

> **`ITkbDatabase` is in `FDP.Interfaces`**. The project already references it (if not, add `<ProjectReference>` to `FDP.Interfaces.csproj`). `TkbTemplate` is in the same assembly.

> **`TkbDatabase` concrete class is in `FDP.Toolkit.Tkb`**. Tests use `TkbDatabase` directly. The main project uses `ITkbDatabase` only.

**Spawn pattern** (for `ScenarioDirector` in BATCH-16 and for tests): 
```csharp
var template = tkb.GetByType(2001);     // MilitaryAPC
var entity   = world.CreateEntity();
template.ApplyTo(world, entity);        // adds all components from the template
// Then set spawn-specific fields:
ref var tf = ref world.GetComponentRW<SimTransform>(entity);
tf.Position = new Vector3(50f, 50f, 0f);
```

**Add `PreviousCapabilities` and `HealthData`** to the damageable entity templates (APC, InfantrySoldier, Insurgent) — these are not in DESIGN.md §9.2 but are required by BATCH-12/13 systems. Use `t.AddComponent(new PreviousCapabilities { Capabilities = ... })`.

> ⚠️ **Check `VehicleState` field names** in the current codebase before adding it — the TASK-DETAIL adaptation quote says `VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 }`. Verify actual field names in `CarKinem.Core.VehicleState`.

**HeadlessDemoApp changes:**
- Construct `TkbDatabase _tkb = new()` field.
- In `RegisterComponents()` (or equivalent init), call `DemoTkbSetup.RegisterAll(_tkb)` after world component registration.
- Expose `_tkb` so `ScenarioDirector` (BATCH-16) can use `_tkb.GetByType(...)`.

**Test rework** — rename/replace existing blueprint tests:
```csharp
[Fact] void TkbSetup_RegistersAllFiveTemplates()
// var tkb = new TkbDatabase();
// DemoTkbSetup.RegisterAll(tkb);
// Assert.NotNull(tkb.GetByType(1001));
// Assert.NotNull(tkb.GetByType(2001));
// ... (all 5)

[Fact] void APC_Template_HasPassengerBuffer()
// DemoTkbSetup.RegisterAll(tkb);
// var template = tkb.GetByType(2001);
// var world = new EntityRepository(); (register PassengerBuffer)
// var entity = world.CreateEntity();
// template.ApplyTo(world, entity);
// Assert.True(world.HasComponent<PassengerBuffer>(entity));

[Fact] void Soldier_Template_HasWeaponState()
// Same pattern — GetByType(2002), ApplyTo, HasComponent<WeaponState>

[Fact] void Insurgent_Template_HasWeaponState_WithExpectedAmmo()
// GetByType(2003), ApplyTo, Assert ammo == 1 (RPG)
```

---

### Task 4: `TrafficBrainSystem` (BCS-P7-T4)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TrafficBrainSystem.cs`  
**Task Definition:** TASK-DETAIL.md §BCS-P7-T4 — read in full.  
**Design reference:** DESIGN.md §9.1.

**System class:**
```
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ChannelArbitrationSystem))]
public class TrafficBrainSystem : ComponentSystem
```

**Logic:** For each entity with `SimTier { Value == 1 }` + `LocomotionChannel` + `ActorCapabilityState(CanMove)`:
- If `TargetMemory.ThreatCount > 0` (entity perceives a threat): write `LocomotionChannel.ActiveAction = CombatActions.Flee` (or `NavigationActions.WanderRandom` with high urgency — check the existing action kind enums).
- Else: write `LocomotionChannel.ActiveAction = NavigationActions.WanderRandom` (or equivalent); set destination to a random waypoint on the road graph.

> ⚠️ Look up the **actual `NavigationActions` action ID constants** from `FDP.Toolkit.Navigation` before writing. Also find how `MoveToExecutor` / `WanderExecutor` is triggered — is it an action kind on `LocomotionChannel`, or a `NavState` write? Look at existing tests to understand the channel contract.

**Tests:**
```csharp
[Fact] void TrafficBrain_SetsFlee_WhenThreatDetected()
// Entity: SimTier=1, LocomotionChannel, ActorCapabilityState(CanMove), TargetMemory{ThreatCount=1}.
// Run TrafficBrainSystem.
// Assert: LocomotionChannel.ActiveAction == [Flee action id].

[Fact] void TrafficBrain_SetsWander_WhenIdle()
// Entity: SimTier=1, TargetMemory{ThreatCount=0}.
// Run TrafficBrainSystem.
// Assert: LocomotionChannel.ActiveAction == [Wander action id].

[Fact] void TrafficBrain_IgnorestTier2Entities()
// Entity: SimTier=2. Run TrafficBrainSystem.
// Assert: LocomotionChannel.ActiveAction unchanged (== 0).
```

---

### Task 5: Insurgent BTree Nodes + Ambush.json (BCS-P7-T5)

**Files:**
- `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs`
- `FDP/Examples/Fdp.Examples.UrbanCombat/Assets/Ambush.json`

**Task Definition:** TASK-DETAIL.md §BCS-P7-T5. **Design reference:** DESIGN.md §9.4.

**Node implementations (`InsurgentNodes.cs`):**
```csharp
public static class InsurgentNodes
{
    /// <summary>
    /// Condition: returns Success if TargetMemory has at least one live threat entry.
    /// Used as: root → Selector → [Condition_HasTarget → Action_AimAndFire]
    ///                                                  → HoldPosition
    /// </summary>
    public static NodeStatus Condition_HasTarget(
        Entity entity, ref BrainBlackboard bb, FdpHsmContext ctx) { ... }
    // Read ctx.World.GetComponent<TargetMemory>(ctx.Self)
    // Return Success if ThreatCount > 0, else Failure

    /// <summary>
    /// Action: writes WeaponChannel.ActiveAction = CombatActions.AimAndFire (action kind = 1).
    /// Returns Running until target is destroyed or moves out of range.
    /// </summary>
    public static NodeStatus Action_AimAndFire(
        Entity entity, ref BrainBlackboard bb, FdpHsmContext ctx) { ... }
    // Check target still alive via TargetMemory[0]; write WeaponChannel.ActiveAction
}
```

> Note: `FdpHsmContext.World` is now available (DEBT-007 resolved in BATCH-13). Nodes must use `ctx.World` not static ambient for ECS access. However — **check the actual BTree node delegate signature** in `FDP.Toolkit.Behavior` or `Fbt` library. The delegate type should be something like `Func<BTreeContext, NodeStatus>` or a custom delegate. Read `BTreeTickSystem` or existing BTree node examples before writing.

**`Ambush.json`:** FastBTree JSON authoring. Selector with two branches:
```json
{
  "name": "Ambush_BT",
  "root": {
    "type": "Selector",
    "children": [
      {
        "type": "Sequence",
        "children": [
          { "type": "Condition", "node": "HasTarget" },
          { "type": "Action", "node": "AimAndFire" }
        ]
      },
      { "type": "Action", "node": "HoldPosition" }
    ]
  }
}
```

> ⚠️ Read an **existing `.json` BTree file** in the repo to get the exact schema before writing `Ambush.json`. Look in `Examples/` or `Assets/` directories.

**Tests:**
```csharp
[Fact] void Ambush_BT_HoldPosition_WhenNoTarget()
// Entity: BrainBTreeState, BrainBlackboard, TargetMemory{ThreatCount=0}.
// Load Ambush.json, tick BTreeTickSystem once.
// Assert: WeaponChannel.ActiveAction == 0 (no aim-and-fire dispatched).

[Fact] void Ambush_BT_AimsAtTarget_WhenTargetPresent()
// Entity: TargetMemory{ThreatCount=1}.
// Tick BTreeTickSystem.
// Assert: WeaponChannel.ActiveAction == CombatActions.AimAndFire.
```

---

### Task 6: APC HSM Authoring (BCS-P7-T6)

**Files:**
- `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
- `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs`

**Task Definition:** TASK-DETAIL.md §BCS-P7-T6. **Design reference:** DESIGN.md §9.4.

**HSM states:** `Cruising` (initial) → on `MobilityLost` (EventId=`BehaviorConstants.EventId_MobilityLost`) → `Disabled`.

> **`HsmBuilder` API** — before writing, read the `Fhsm.Compiler` library or look at an existing HSM authoring example in the codebase (search for `HsmBuilder` in `FDP/`). The API may differ from the design talk description.

**`ApcHsmSetup.cs`:**
```csharp
public static class ApcHsmSetup
{
    public static HsmDefinitionBlob Build()
    {
        var builder = new HsmBuilder();
        // State: Cruising (initial, depth=0)
        builder.AddState("Cruising", isInitial: true);
        builder.SetActivityDelegate("Cruising", ApcHsmActions.Activity_Cruise);
        // State: Disabled (depth=0)
        builder.AddState("Disabled");
        builder.SetOnEnterDelegate("Disabled", ApcHsmActions.OnEnter_Disabled);
        // Transition: Cruising → Disabled on MobilityLost
        builder.AddTransition("Cruising", "Disabled",
            eventId: BehaviorConstants.EventId_MobilityLost);
        return builder.Build();
    }
}
```

**`ApcHsmActions.cs`:**
```csharp
public static class ApcHsmActions
{
    /// <summary>
    /// Activity for the Cruising state. Writes convoy escort locomotion intent each tick.
    /// Uses ctx.World to write LocomotionChannel.
    /// </summary>
    public static void Activity_Cruise(FdpHsmContext ctx) { ... }

    /// <summary>
    /// OnEnter for the Disabled state. Clears LocomotionChannel.ActiveAction
    /// and signals loss of mobility.
    /// </summary>
    public static void OnEnter_Disabled(FdpHsmContext ctx) { ... }
}
```

> ⚠️ Read the **actual `HsmBuilder` API** from the codebase or `Fhsm.Compiler` before writing. The state machine may use different method names, and delegates may have a different signature than shown above. Check `HsmTickSystem` for how delegates are invoked to understand the expected signature.

**Tests:**
```csharp
[Fact] void ApcHsm_Builds_WithoutException()
// ApcHsmSetup.Build() → Assert.NotNull(blob).

[Fact] void ApcHsm_InitialState_IsCruising()
// Create HsmInstance128. Initialize with blob via HsmKernel.
// Assert: current state index == [Cruising state index].

[Fact] void ApcHsm_TransitionsToDisabled_OnMobilityLostEvent()
// Initialize HsmInstance128.
// Enqueue MobilityLost event (EventId = BehaviorConstants.EventId_MobilityLost).
// Run one HsmKernel tick.
// Assert: current state index == [Disabled state index].
```

---

## 🧪 Testing Requirements

- **Minimum 12 new tests:**
  - Task 0: 4 TKB registration tests
  - T4: 3 TrafficBrainSystem tests
  - T5: 2 Ambush BTree tests
  - T6: 3 APC HSM tests
- **All existing 677+ tests remain green.**
- **No raw `world.AddComponent(...)` in blueprint code** — all component additions go through `TkbTemplate.AddComponent<T>()`.

---

## ⚠️ Quality Standards

**❗ Task 0 is P1:** Do not start T4 until `DemoTkbSetup` compiles and blueprint tests pass.

**❗ Read existing examples before writing new code for T5 and T6:**
- Find an existing `.json` BTree file in the repo before writing `Ambush.json`
- Find `HsmBuilder` usage before writing `ApcHsmSetup`
- Find BTree node delegate signature before writing `InsurgentNodes`

**❗ `FdpHsmContext.World`** is available (DEBT-007, BATCH-13). Use it in BTree node delegates and HSM action delegates. Do NOT use static/thread-local ECS access.

**❗ `VehicleState` field names** — verify before using `Speed`, `SteerAngle`, `Accel`. The TASK-DETAIL.md adaptation quote lists these but the actual field names must be confirmed in source.

**❗ TKB type IDs are `long`** (`TkbTemplate.TkbType` is `long`). The blueprint IDs `1001–2003` are fine as `long`.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-15-REPORT.md`

**Q1:** For Task 0 — did `Fdp.Examples.UrbanCombat.csproj` already reference `FDP.Interfaces`? Did you need to add `FDP.Toolkit.Tkb` for the `TkbDatabase` in tests?

**Q2:** For T4 `TrafficBrainSystem` — what are the actual action kind integer values for Flee and Wander in your project? Where are these constants defined?

**Q3:** For T5 — what is the actual BTree node delegate signature (`Func<...>` / `delegate` / interface)? Show the exact type used by `BTreeTickSystem`.

**Q4:** For T6 — what is the `HsmBuilder` API? Show the actual method names used to add states, set delegates, and add transitions. If it differs significantly from the spec above, describe the delta.

**Q5:** Any surprises?

---

## 🎯 Success Criteria

- [ ] **`EntityBlueprints.cs`** deleted or repurposed; not the active blueprint registration.
- [ ] **`DemoTkbSetup.RegisterAll(ITkbDatabase tkb)`** registers 5 templates by TKB type ID; 4 tests pass.
- [ ] **`HeadlessDemoApp`** constructs and holds `TkbDatabase`; calls `DemoTkbSetup.RegisterAll`.
- [ ] **T4** `TrafficBrainSystem` — 3 tests pass.
- [ ] **T5** `Ambush.json` + `InsurgentNodes` — 2 tests pass.
- [ ] **T6** `ApcHsmSetup` + `ApcHsmActions` — 3 tests pass.
- [ ] **Full solution: 0 errors; all tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **BATCH-14 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-14-REVIEW.md`
- **TankTemplate (canonical pattern):** `FDP/Examples/Fdp.Examples.NetworkDemo/Configuration/TankTemplate.cs`
- **TkbTemplate API:** `FDP/Common/FDP.Interfaces/Abstractions/TkbTemplate.cs`
- **TkbDatabase:** `FDP/Toolkits/FDP.Toolkit.Tkb/TkbDatabase.cs`
- **TASK-DETAIL §T2, T4, T5, T6:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
- **DESIGN.md §9.1, §9.2, §9.4:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
