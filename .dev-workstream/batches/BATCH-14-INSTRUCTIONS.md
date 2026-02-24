# BATCH-14: BATCH-13 Corrective + Phase 7 Start (BCS-P7-T1, T2, T3)

**Batch Number:** BATCH-14  
**Tasks:**
- **Corrective-0 (DEBT-035):** `DoctrineIngressSystem` partial-transition bug
- **BCS-P7-T1:** Project scaffold + `HeadlessDemoApp` shell
- **BCS-P7-T2:** TKB Blueprints (5 entity templates)
- **BCS-P7-T3:** `DemoEnvironmentSetup` (city intersection road graph)

**Phase:** Phase 7 — `Fdp.Examples.UrbanCombat` Demo App (start)  
**Estimated Effort:** 10–13 hours  
**Priority:** HIGH — first Phase 7 batch; Corrective-0 is P1 and must be done first  
**Dependencies:** BATCH-13 ✅ (modulo Corrective-0)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-13 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-13-REVIEW.md` — read Issue 1 carefully before touching any code.
2. **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — DEBT-035 only open item.
3. **DESIGN.md §9 (Demo Application):** `FDP/Docs/projects/behavior-control/DESIGN.md` — §9.1 (scenario), §9.2 (TKB blueprints), §9.3 (road graph), §9.5 (TelemetryReporterSystem output format).
4. **TASK-DETAIL.md §BCS-P7-T1, T2, T3:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — read all three sections in full.
5. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
6. **`DoctrineIngressSystem.cs`** — read the current (broken) code before fixing it.

### Source Locations

| Area | Path |
|---|---|
| **Corrective-0** | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs` |
| **Corrective-0 test** | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineIngressSystemTests.cs` |
| **New project** | `FDP/Examples/Fdp.Examples.UrbanCombat/` ← CREATE directory |
| **New project file** | `FDP/Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj` ← CREATE |
| **Demo shell** | `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` ← CREATE |
| **Program entry point** | `FDP/Examples/Fdp.Examples.UrbanCombat/Program.cs` ← CREATE |
| **Blueprints** | `FDP/Examples/Fdp.Examples.UrbanCombat/Blueprints/EntityBlueprints.cs` ← CREATE |
| **Environment** | `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoEnvironmentSetup.cs` ← CREATE |
| **Solution file** | `FDP/FDP.sln` ← add new project |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/    # Corrective-0 test
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-14-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **Corrective-0 first** — fix `DoctrineIngressSystem` + add test → all existing tests green ✅
2. Create demo project scaffold (T1) ✅
3. TKB Blueprints (T2) ✅
4. Road graph setup (T3) ✅
5. Full solution green ✅

---

## ✅ Tasks

### Task 0 (Corrective-0): `DoctrineIngressSystem` partial-transition fix (DEBT-035)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs`  
**Issue:** Read BATCH-13-REVIEW Issue 1 in full before coding.

**Root cause:** `DoctrineState` and `BrainBTreeState` writes happen BEFORE `ParseParams` is called inside the try/catch. A `ParseParams` failure leaves the entity with:
- New `ActiveDoctrineHash` (points to the new doctrine)
- Bumped `InstanceId` (channels have been preempted)
- Zeroed `BrainBTreeState`
- But an un-parsed / partially zeroed blackboard

This is a **partial doctrine transition** — the brain will try to run the new doctrine with all-zero blackboard parameters.

**Fix:** Restructure so that all ECS component writes happen **after** `ParseParams` succeeds. Because `ParseParams` writes directly into the live blackboard memory, use one of:

**Option A (preferred — stackalloc shadow, truly atomic):**

```csharp
// Snapshot, attempt parse on snapshot, only commit if parse succeeds.
const int BlackboardSize = 128; // BrainBlackboard.MemorySize
Span<byte> shadow = stackalloc byte[BlackboardSize];

if (def.ParseParams != null && World.HasComponent<BrainBlackboard>(evt.Entity))
{
    // Copy current blackboard into shadow.
    ref readonly var bb = ref World.GetComponentRO<BrainBlackboard>(evt.Entity);
    unsafe
    {
        fixed (byte* src = &bb.Memory[0], dst = shadow)
            Buffer.MemoryCopy(src, dst, BlackboardSize, BlackboardSize);
    }

    bool ok = true;
    unsafe
    {
        fixed (byte* dst = shadow)
        {
            try   { def.ParseParams(evt.JsonParams, dst); }
            catch { ok = false; }
        }
    }
    if (!ok) continue;  // ParseParams failed — abort, entity stays on old doctrine.

    // Write shadow back to live blackboard.
    ref var bbW = ref World.GetComponentRW<BrainBlackboard>(evt.Entity);
    unsafe
    {
        fixed (byte* src = shadow, dst = &bbW.Memory[0])
            Buffer.MemoryCopy(src, dst, BlackboardSize, BlackboardSize);
    }
}
else if (def.ParseParams != null)
{
    // Entity has no blackboard component — skip if params are required.
    continue;
}

// ParseParams succeeded (or not required). Now commit doctrine transition.
ref var doctrine = ref World.GetComponentRW<DoctrineState>(evt.Entity);
doctrine.ActiveDoctrineHash = doctrineId;
unchecked { doctrine.InstanceId++; }
doctrine.BrainTier = def.BrainTier;
if (World.HasComponent<BrainBTreeState>(evt.Entity))
{
    ref var btState = ref World.GetComponentRW<BrainBTreeState>(evt.Entity);
    btState.State = default;
}
```

> **Check the actual `BrainBlackboard.MemorySize` constant** in `BehaviorComponents.cs` — do not hardcode 128. Use `BehaviorConstants.BlackboardByteSize` or `BrainBlackboard.MemorySize` if defined; otherwise count the `fixed byte Memory[N]` field size.

**Also fix:** Remove the duplicate `using System;` on line 2 (lines 1 and 2 are identical — one is redundant).

**New required test:**
```csharp
[Fact]
public void DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails()
// Entity: DoctrineState(ActiveDoctrineHash=OldId, InstanceId=0).
// Register new doctrine (NewId) with a ParseParams delegate that throws.
// Publish AssignDoctrineEvent(entity, "NewDoctrineName", "{}").
// Run DoctrineIngressSystem.
// Assert: doctrine.ActiveDoctrineHash == OldId (NOT switched to NewId).
// Assert: doctrine.InstanceId == 0 (NOT bumped).
```

---

### Task 1: Project Scaffold + `HeadlessDemoApp` Shell (BCS-P7-T1)

**Task Definition:** TASK-DETAIL.md §BCS-P7-T1 — read in full.  
**Design reference:** DESIGN.md §9 (full section).

**New project:** `FDP/Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj`

**References required** (verify exact project names from existing `.sln`):
```xml
<ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Behavior\FDP.Toolkit.Behavior.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Perception\FDP.Toolkit.Perception.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Navigation\FDP.Toolkit.Navigation.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Combat\FDP.Toolkit.Combat.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Physics\FDP.Toolkit.Physics.csproj" />
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.CarKinem\FDP.Toolkit.CarKinem.csproj" />
```

> ⚠️ Verify actual paths from `FDP.sln` before writing the `.csproj`. Use relative paths from the Examples directory.

**`Program.cs`:** Entry point — constructs the world, registers all systems, runs `HeadlessDemoApp.Run(world)`, exits.

**`HeadlessDemoApp.cs`:** Orchestrator class. Must have:

```csharp
public class HeadlessDemoApp
{
    private readonly EntityRepository _world;

    public HeadlessDemoApp(EntityRepository world) { _world = world; }

    /// <summary>
    /// Run the 600-frame (10-second at 60 Hz) Urban Ambush scenario.
    /// Prints structured telemetry via TelemetryReporterSystem.
    /// </summary>
    public void Run()
    {
        const float Dt = 1f / 60f;
        const int TotalFrames = 600;

        // 1. Register all components.
        // 2. Register and build all systems.
        // 3. Spawn scenario entities via ScenarioDirector (T7, placeholder for now).
        // 4. Simulation loop: for i=0..TotalFrames: _world.Tick(Dt);
        Console.WriteLine("[UrbanAmbush] Simulation complete.");
    }
}
```

For this batch, the simulation loop, component registration, and system registration are stubs — the architecture must be correct but the actual actor spawning and TelemetryReporterSystem are not yet implemented (those come in T7/T8).

**Tests for T1:** BCS-P7-T1 success condition is integration-level; no isolated unit test required. Confirm: `dotnet build Fdp.Examples.UrbanCombat.csproj` succeeds.

---

### Task 2: TKB Blueprints (BCS-P7-T2)

**Task Definition:** TASK-DETAIL.md §BCS-P7-T2 — read in full.  
**Design reference:** DESIGN.md §9.2 (TKB Blueprints — all 5 entity templates).

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Blueprints/EntityBlueprints.cs`

Implement the 5 blueprint factory methods (static methods, each returning `Entity`):

| Blueprint | ID | Key Components |
|---|---|---|
| `CivilianPedestrian` | 1001 | SimTransform, SimVelocity, SimTier(1), DoctrineState, ActorCapabilityState(CanMove), LocomotionChannel, VehicleState, VehicleParams(Pedestrian), NavState, PerceptionReceptor(vision=30, hear=100), TargetMemory, PhysicsCollider(r=0.4, layer=1) |
| `CivilianCar` | 1002 | SimTransform, SimVelocity, SimTier(1), DoctrineState, ActorCapabilityState(CanMove), LocomotionChannel, VehicleState, VehicleParams(PersonalCar), NavState, PhysicsCollider(r=2, layer=1) |
| `MilitaryAPC` | 2001 | SimTransform, SimVelocity, SimTier(2), DoctrineState(BrainTier=2), BrainHsm128, BrainBlackboard, PreviousCapabilities, ActorCapabilityState(CanMove|CanInteract), LocomotionChannel, InteractionChannel, VehicleState, VehicleParams(Tank), NavState, Health(500), HealthData(500,500), PhysicsCollider(r=3.5, layer=1), PassengerBuffer, Faction(TeamId=1) |
| `InfantrySoldier` | 2002 | SimTransform, SimVelocity, SimTier(2), DoctrineState, BrainBTreeState, BrainBlackboard, PreviousCapabilities, ActorCapabilityState(CanMove|CanShoot), LocomotionChannel, WeaponChannel, InteractionChannel, VehicleState, VehicleParams(Pedestrian), NavState, Health(100), HealthData(100,100), WeaponState(ammo=30, rate=5Hz, range=200, damage=25), PerceptionReceptor(vision=150, hear=200), TargetMemory, PhysicsCollider(r=0.4, layer=1), Faction(TeamId=1) |
| `Insurgent` | 2003 | Same as InfantrySoldier but Faction(TeamId=2), WeaponState(ammo=1, range=300, damage=500, rate=0.1Hz) |

> All positions are set to `Vector3.Zero` in blueprints — actual spawn positions are set by `ScenarioDirector` (T7).

> **`PreviousCapabilities`** must be added to entities with `BrainHsm128`/`BrainHsm64` (required by `HsmDamageBridgeSystem`). DESIGN.md §9.2 does not list it explicitly because it was added in BATCH-12; include it for APC, InfantrySoldier, and Insurgent.

**Tests for T2:** One test verifying each blueprint creates an entity with all required components:
```csharp
[Fact] void Blueprint_CivilianPedestrian_HasAllRequiredComponents()
[Fact] void Blueprint_MilitaryAPC_HasAllRequiredComponents()
// (minimum 2 blueprint tests; the others are a bonus if time permits)
```

---

### Task 3: `DemoEnvironmentSetup` — Road Graph (BCS-P7-T3)

**Task Definition:** TASK-DETAIL.md §BCS-P7-T3 — read in full.  
**Design reference:** DESIGN.md §9.3 (road graph — 4-way intersection).

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoEnvironmentSetup.cs`

```csharp
public static class DemoEnvironmentSetup
{
    /// <summary>
    /// Creates a 4-way city intersection road graph:<br/>
    /// 5 nodes: center (0,0) + N (0,100) + S (0,-100) + E (100,0) + W (-100,0).<br/>
    /// 8 segments: 4 inbound + 4 outbound.<br/>
    /// Returns a <see cref="RoadNetworkBlob"/> ready for <see cref="CarKinematicsSystem"/>.
    /// </summary>
    public static RoadNetworkBlob CreateCityIntersection() { ... }
}
```

**Tests for T3:**
```csharp
[Fact] void DemoEnvironment_Intersection_Has5Nodes()
// CreateCityIntersection().Nodes.Length == 5

[Fact] void DemoEnvironment_Intersection_Has8Segments()
// CreateCityIntersection().Segments.Length == 8

[Fact] void DemoEnvironment_Intersection_CenterNodeAtOrigin()
// Node 0 position == (0, 0) (or whichever is the center)
```

---

## 🧪 Testing Requirements

- **Minimum 6 new tests:** 1 Corrective-0 (DoctrineIngress unchanged-state) + 2 blueprint component assertions + 3 road graph geometry.
- **All 50 existing `FDP.Toolkit.Behavior.Tests` remain green** (incl. DEBT-008 test from BATCH-13).
- **`Fdp.Examples.UrbanCombat` builds with 0 errors.**

---

## ⚠️ Quality Standards

**❗ Corrective-0 is P1 — must be done before any T1/T2/T3 work.** Submit only when all existing tests still pass.

**❗ The stackalloc shadow approach in Corrective-0** — check `BrainBlackboard.MemorySize` / field size before writing the buffer copy. Do not hardcode `128`.

**❗ Blueprint component list** — treat DESIGN.md §9.2 as the canonical list. Add `PreviousCapabilities` to Hsm/BTree entities (not in design doc, required by `HsmDamageBridgeSystem`). Add `HealthData` alongside `Health` for damageable entities (not in design doc, required by `MissionDirectorSystem.HealthCritical`, added in BATCH-13).

**❗ `RoadNetworkBlob`** — look at `CarKinematicsSystem` usage and the existing road graph builder in `FDP.Toolkit.CarKinem` before implementing. The builder API is likely `RoadNetworkBuilder` or similar. Find it in `FDP/Toolkits/FDP.Toolkit.CarKiem/` before writing `DemoEnvironmentSetup.cs`.

**❗ `HeadlessDemoApp.Run()` stub** — the simulation loop exists in stub form. The system pipeline and component registrations must be architecturally correct (no TODOs for project references, no wrong assembly references) even if actor simulation hasn't been wired yet.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-14-REPORT.md`

**Q1:** For Corrective-0 — what is the actual `BrainBlackboard` memory field size? Did you use `BehaviorConstants.BlackboardByteSize`, a field-level `sizeof`, or another approach to avoid hardcoding?

**Q2:** For T2 Blueprints — did `PreviousCapabilities` require registering a new component type in the demo project's world? How did you reconcile the DESIGN.md §9.2 blueprint lists (which predate BATCH-12) with the new components added in BATCH-11/12/13?

**Q3:** For T3 Road Graph — what is the `RoadNetworkBlob` builder API? Did you find a `RoadNetworkBuilder` class or another factory?

**Q4:** Any design decisions or dependencies discovered while wiring up the project scaffold?

---

## 🎯 Success Criteria

- [ ] **Corrective-0** — `DoctrineIngressSystem` reordered; DoctrineState writes after ParseParams; duplicate `using System` removed; test `DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails` passes.
- [ ] **BCS-P7-T1** — `Fdp.Examples.UrbanCombat` project builds (0 errors); `HeadlessDemoApp.Run()` exists with correct stub architecture.
- [ ] **BCS-P7-T2** — `EntityBlueprints` has 5 methods; minimum 2 blueprint component tests pass.
- [ ] **BCS-P7-T3** — `DemoEnvironmentSetup.CreateCityIntersection()` returns 5 nodes + 8 segments; 3 geometry tests pass.
- [ ] **Full solution: 0 errors.**
- [ ] **All tests green (including all 50 existing Behavior.Tests).**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **BATCH-13 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-13-REVIEW.md`
- **TASK-DETAIL.md §BCS-P7-T1, T2, T3:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
- **DESIGN.md §9:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **DoctrineIngressSystem.cs:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs`
- **BrainBlackboard / BehaviorConstants:** `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs`; `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **RoadNetworkBlob builder:** `FDP/Toolkits/FDP.Toolkit.CarKinem/` — find the road network construction API before writing T3
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
