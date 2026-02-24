# BATCH-11: BATCH-10 Fixes + LinearKinematicsSystem + Phase 6 Start (BCS-P6-T1)

**Batch Number:** BATCH-11  
**Tasks:** BATCH-10 Corrective (Issues 1–3), DEBT-032 (`LinearKinematicsSystem`), BCS-P6-T1 (`MissionPlanQueue` + `MissionDirectorSystem`)  
**Phase:** Corrective + Phase 6 — FDP.Toolkit.Behavior (Advanced)  
**Estimated Effort:** 9–12 hours  
**Priority:** HIGH — `LinearKinematicsSystem` unblocks bullet movement; corrective unblocks Phase 5 completion  
**Dependencies:** BATCH-10 (review pending fixes)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-10 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-10-REVIEW.md` — read all Issues 1–3 before touching any code.
2. **DESIGN.md §2.3 and §10:** `FDP/Docs/projects/behavior-control/DESIGN.md` — lines 57–72 (LinearKinematicsSystem spec) and lines 469–510 (frame execution pipeline / system ordering).
3. **DESIGN.md §8.1:** lines 354–367 (MissionPlanQueue + MissionDirectorSystem).
4. **TASK-DETAIL.md §BCS-P6-T1:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — full BCS-P6-T1 section.
5. **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — DEBT-032.
6. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
7. **Existing Combat systems (understand before fixing):**
   - `FDP/Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs`
   - `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`
   - `FDP/Toolkits/FDP.Toolkit.Behavior/Components/ChannelComponents.cs` — verify `ActorCapabilityState` struct

### Source Locations

| Area | Path |
|---|---|
| **Corrective — Ballistics phase fix** | `FDP/Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs` |
| **Corrective — Damage fix + test** | `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` |
| **Corrective — Damage test** | `FDP/Toolkits/FDP.Toolkit.Combat.Tests/DamageSystemTests.cs` |
| **New system — LinearKinematics** | `FDP/Toolkits/FDP.Toolkit.Physics/Systems/LinearKinematicsSystem.cs` ← CREATE |
| **New tests — LinearKinematics** | `FDP/Toolkits/FDP.Toolkit.Physics.Tests/LinearKinematicsSystemTests.cs` ← CREATE |
| **New components — Mission** | `FDP/Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs` ← CREATE |
| **New system — MissionDirector** | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` ← CREATE |
| **New tests — Mission** | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/MissionDirectorSystemTests.cs` ← CREATE |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Combat.Tests/    # +1 new test (capability stripping)
dotnet test Toolkits/FDP.Toolkit.Physics.Tests/   # +5 new tests (LinearKinematics)
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/  # +4 new tests (MissionDirector)
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-11-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Corrective 1 — fix `BallisticsSystem` group/ordering → all existing tests green ✅
2. Corrective 2 — fix `DamageSystem` group → all existing tests green ✅
3. Corrective 3 — add `ActorCapabilityState` stripping + test → new test passes ✅
4. `LinearKinematicsSystem` — implement + tests ✅ — uncomment `[UpdateAfter]` in `BallisticsSystem`
5. `MissionComponents` + `MissionDirectorSystem` + tests ✅
6. Full solution green before submitting ✅

---

## ✅ Tasks

### Task 0a (Corrective): `BallisticsSystem` — wrong system group

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs`

**Fix:** Change `[UpdateInGroup(typeof(SimulationSystemGroup))]` to `[UpdateInGroup(typeof(PostSimulationSystemGroup))]`.

The `LinearKinematicsSystem` runs in `PostSimulation` and must run **before** `BallisticsSystem`. Leave the `[UpdateAfter(typeof(LinearKinematicsSystem))]` attribute commented out for now — you will uncomment it in Task 1 once `LinearKinematicsSystem` exists.

The execution order in `PostSimulation` will be:
```
LinearKinematicsSystem  →  BallisticsSystem  →  SpatialHashSystem
```

---

### Task 0b (Corrective): `DamageSystem` — wrong system group

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`

**Fix:**
1. Change `[UpdateInGroup(typeof(InputSystemGroup))]` to `[UpdateInGroup(typeof(SimulationSystemGroup))]`.
2. Remove `[UpdateAfter(typeof(HitResolutionSystem))]` — `HitResolutionSystem` is in `Input`; cross-group `UpdateAfter` is unsupported by the FDP kernel and will silently have no effect (or error). `HitEvent`s published in `Input` are available to `Simulation` systems via the bus swap.

Verify existing 5 `DamageSystemTests` still pass.

---

### Task 0c (Corrective): `DamageSystem` — missing `ActorCapabilityState` stripping

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`

**Context:** DESIGN.md §6.4 and TASK-DETAIL.md §BCS-P5-T5: *"if Health.Current <= 0, strip `CanMove` and `CanShoot` from `ActorCapabilityState`"*. This is required for `HsmDamageBridgeSystem` (Phase 6) to detect mobility loss.

**Fix:** In the lethal-hit block (before or alongside `World.DestroyEntity(evt.HitEntity)`):
```csharp
// Strip capabilities before destroying — HsmDamageBridgeSystem reads this.
if (World.HasComponent<ActorCapabilityState>(evt.HitEntity))
{
    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
    caps.Flags &= ~(ActorCapabilityFlags.CanMove | ActorCapabilityFlags.CanShoot);
}
World.DestroyEntity(evt.HitEntity);
```

> **Check the actual field/flag names in `ActorCapabilityState`** — look at `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs`. The struct may use `Flags` (bitmask int) or individual `bool` fields. Adapt accordingly.

**New test (add to `DamageSystemTests.cs`):**
```csharp
[Fact]
public void Damage_StripsCapabilities_OnLethalHit()
// Entity with Health(20f, Max=100f) + ActorCapabilityState(CanMove|CanShoot). Damage=25f (lethal).
// Snapshot capabilities BEFORE running system (entity alive, readable).
// Run system.
// Assert: CanMove == false, CanShoot == false.
// Note: entity may be destroyed after stripping — snapshot the values before destruction or
//       redesign so stripping happens in a separate frame before destruction.
// If entity is already destroyed, assert on the snapshotted value.
```

> **Implementation note on ordering:** In the current implementation, `World.GetComponentRW` fails on a destroyed entity. Strip capabilities first (`caps.Flags &= ~...`), then `World.DestroyEntity`. The test should snapshot `caps` struct value from the world BEFORE running the system, then run, then verify the entity is dead AND check that CanMove/CanShoot are cleared — OR, restructure the test to assert mid-frame by not destroying the entity (use sub-lethal damage that nevertheless triggers stripping).

Accept whichever test approach correctly validates the "capabilities stripped" behaviour without false positives.

---

### Task 1: `LinearKinematicsSystem` (DEBT-032)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/LinearKinematicsSystem.cs` ← NEW

**Spec:** DESIGN.md §2.3 (lines 57–72). This is the canonical definition — implement it exactly.

```csharp
/// <summary>
/// Advances the position of any entity that has <see cref="SimTransform"/> and
/// <see cref="SimVelocity"/> but NOT <see cref="VehicleState"/>.
/// Covers: bullets, pedestrians (future), projectiles, drift objects.
/// Vehicles are handled by <c>CarKinematicsSystem</c>.
///
/// Execution phase: PostSimulation, after BallisticsSystem (which snapshots
/// PreviousPosition before movement), before SpatialHashSystem (which needs
/// updated positions for the next frame's grid).
/// </summary>
[UpdateInGroup(typeof(PostSimulationSystemGroup))]
[UpdateAfter(typeof(BallisticsSystem))]
[UpdateBefore(typeof(SpatialHashSystem))]
public class LinearKinematicsSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        float dt = DeltaTime;

        var query = World.Query()
            .With<SimTransform>()
            .With<SimVelocity>()
            .Without<VehicleState>()
            .Build();

        // Parallel integration: tf.Position += vel.Linear * dt
        // Angular integration (tf.Rotation += ω·dt) is intentionally omitted for
        // the current use cases (bullets travel straight, rotation tracking deferred).
        query.ForEachParallel(entity =>
        {
            ref var tf  = ref World.GetComponentRW<SimTransform>(entity);
            ref readonly var vel = ref World.GetComponentRO<SimVelocity>(entity);
            tf.Position += vel.Linear * dt;
        });
    }
}
```

**After implementing:** uncomment `// [UpdateAfter(typeof(LinearKinematicsSystem))]` in `BallisticsSystem.cs`.

**Tests (new file `LinearKinematicsSystemTests.cs`):**

```csharp
[Fact] void LinearKinematics_AdvancesPosition_ByVelocityTimesDeltaTime()
// Entity: SimTransform{(0,0,0)}, SimVelocity{Linear=(10,0,0)}.
// Run system with dt=1.0f.
// Assert: Position == (10, 0, 0).

[Fact] void LinearKinematics_DoesNotMove_EntityWithVehicleState()
// Entity: SimTransform{(0,0,0)}, SimVelocity{(10,0,0)}, VehicleState present.
// Run system with dt=1.0f.
// Assert: Position == (0, 0, 0) — excluded from query.

[Fact] void LinearKinematics_DoesNotMove_EntityWithoutSimVelocity()
// Entity: SimTransform{(5,5,0)} only (static building). No SimVelocity.
// Run system.
// Assert: Position unchanged at (5, 5, 0).

[Fact] void LinearKinematics_MovesMultipleEntities_Independently()
// Entity A: velocity=(1,0,0). Entity B: velocity=(0,2,0). dt=1.0f.
// Assert: A.Position==(1,0,0), B.Position==(0,2,0).

[Fact] void LinearKinematics_ZeroVelocity_PositionUnchanged()
// Entity: SimVelocity{Linear=Zero}. Any initial position.
// Assert: Position unchanged after run.
```

**Project:** `LinearKinematicsSystem` belongs in `FDP.Toolkit.Physics` (as noted in DESIGN.md §2.3 comment `// FDP.Toolkit.Physics or Fdp.Kernel`). Use `FDP.Toolkit.Physics` — it's already set up and has `SimulationSystemGroup`/`PostSimulationSystemGroup` references.

---

### Task 2: `MissionPlanQueue` + `MissionDirectorSystem` (BCS-P6-T1)

**Task Definition:** TASK-DETAIL.md §BCS-P6-T1 — read in full.  
**Design reference:** DESIGN.md §8.1 (lines 354–367).

#### `MissionComponents.cs`

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs` ← NEW

```csharp
public enum MissionTrigger : byte
{
    TimerElapsed       = 0,
    ReachedDestination = 1,
    UnderAttack        = 2,
    HealthCritical     = 3,
}

[StructLayout(LayoutKind.Sequential)]
public struct MissionPhase
{
    /// <summary>Doctrine to activate when this phase becomes current.</summary>
    public int   DoctrineId;
    /// <summary>Condition that must be met to advance to the next phase.</summary>
    public MissionTrigger Trigger;
    /// <summary>
    /// Trigger parameter interpretation depends on Trigger type:
    /// - TimerElapsed: duration in seconds (float, reinterpreted from uint bits)
    /// - ReachedDestination: arrival radius in metres
    /// - HealthCritical: threshold fraction [0..1] (float bits)
    /// - UnderAttack: unused (0)
    /// </summary>
    public float TriggerParam;
}

/// <summary>
/// Fixed queue of up to 8 mission phases. CurrentPhase is the index into Phases
/// of the active phase. Phases[CurrentPhase].DoctrineId is loaded into DoctrineState
/// when the phase starts.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MissionPlanQueue
{
    public const int MaxPhases = 8;
    public fixed byte _phases[MaxPhases * 8]; // sizeof(MissionPhase) must be <= 8 bytes; verify
    public byte CurrentPhase;
    public byte PhaseCount;
    public float PhaseElapsedSeconds;   // accumulator for TimerElapsed trigger
}
```

> ⚠️ `sizeof(MissionPhase)` must fit the fixed buffer. `MissionPhase` has `int DoctrineId` (4) + `MissionTrigger` byte (1) + 3 pad + `float TriggerParam` (4) = 12 bytes. Adjust `_phases` buffer to `MaxPhases * 12` or use a Sequential layout verified by a unit test. Alternatively use a simple `fixed MissionPhase[MaxPhases]` if the compiler allows fixed buffers of unmanaged types (C# 7.3+).

**Preferred approach:** Use `fixed MissionPhase Phases[8]` directly (C# 7.3 allows this for unmanaged structs).

#### `MissionDirectorSystem.cs`

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` ← NEW  
**Phase:** `SimulationSystemGroup`, before `ChannelArbitrationSystem`.

**Logic per entity with `MissionPlanQueue` + `DoctrineState`:**

1. If `queue.CurrentPhase >= queue.PhaseCount`: nothing to do (mission complete).
2. Retrieve current phase: `ref var phase = ref queue.Phases[queue.CurrentPhase]`.
3. Evaluate trigger:
   - `TimerElapsed`: `queue.PhaseElapsedSeconds += deltaTime`. If `>= phase.TriggerParam` → trigger fires.
   - `ReachedDestination`: check `NavState.HasArrived == 1` (entity must also have `NavState`).
   - `UnderAttack`: check `TargetMemory` has any entry with ThreatScore > 0 (entity must have `TargetMemory`).
   - `HealthCritical`: check `Health.Current / Health.Max <= phase.TriggerParam` (entity must have `Health`).
4. If trigger fires:
   - `queue.CurrentPhase++`
   - `queue.PhaseElapsedSeconds = 0f`
   - If `queue.CurrentPhase < queue.PhaseCount`: load next phase's doctrine into `DoctrineState.InstanceId++`, `DoctrineState.ActiveDoctrineId = queue.Phases[queue.CurrentPhase].DoctrineId`. This causes `ChannelArbitrationSystem` to preempt the old action channels naturally.
5. Write back `queue` and `doctrineState`.

**Tests (new file `MissionDirectorSystemTests.cs`):**

```csharp
[Fact] void MissionDirector_AdvancesPhase_WhenTimerElapses()
// Phase 0: DoctrineA, Trigger=TimerElapsed(0.5s).
// Run at 60Hz (dt=1/60). After 31 ticks (≥0.5s elapsed):
// Assert: DoctrineState.ActiveDoctrineId == DoctrineB (phase 1's doctrine).
// Assert: queue.CurrentPhase == 1.

[Fact] void MissionDirector_DoesNotAdvance_WhenTimerNotElapsed()
// Same setup. Run 10 ticks (≈0.167s < 0.5s).
// Assert: DoctrineState still DoctrineA. CurrentPhase still 0.

[Fact] void MissionDirector_AdvancesPhase_WhenReachedDestination()
// Phase trigger = ReachedDestination. NavState.HasArrived = 0 initially.
// Run 1 tick → no advance. Set NavState.HasArrived=1. Run 1 more tick → advance.
// Assert: CurrentPhase == 1.

[Fact] void MissionDirector_StopsAtEndOfQueue()
// 2-phase queue. Force both phases to advance.
// Assert: third advance attempt → CurrentPhase stays at 2, no crash, no index-out-of-bounds.
```

---

## 🧪 Testing Requirements

- **Minimum 10 new tests:** 1 corrective (capability stripping) + 5 LinearKinematics + 4 MissionDirector.
- **All 27 existing `FDP.Toolkit.Combat.Tests` remain green.**
- **`LinearKinematics_DoesNotMove_EntityWithVehicleState` is the key exclusion test** — proves the `Without<VehicleState>()` query filter works and bullets won't compete with vehicles.

---

## ⚠️ Quality Standards

**❗ `BallisticsSystem` must be in `PostSimulationSystemGroup`.** Verify by searching for `SimulationSystemGroup` in the file — there must be none.

**❗ `DamageSystem` must be in `SimulationSystemGroup`.** Verify by searching for `InputSystemGroup` in the file — there must be none.

**❗ `ActorCapabilityState` field names** — look them up in `BehaviorComponents.cs` before coding. Do not guess the field names. The test failure should be your first signal if they're wrong.

**❗ `LinearKinematicsSystem` excludes `VehicleState` entities** — the `Without<VehicleState>()` clause is not optional. Vehicles moving via this system instead of `CarKinematicsSystem` would bypass the bicycle model entirely.

**❗ `MissionPlanQueue` fixed buffer** — use `fixed MissionPhase Phases[8]` (C# 7.3 unmanaged fixed buffer). Verify `sizeof(MissionPhase)` in a unit test if unsure.

**❗ No raw literals for max phases or dt in production code** — use `MissionPlanQueue.MaxPhases`.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-11-REPORT.md`

**Q1:** Having implemented `LinearKinematicsSystem` with `Without<VehicleState>()`, confirm: do any vehicle entities accidentally match this query in your test setup? How did you verify exclusion?

**Q2:** For the `ActorCapabilityState` stripping in `DamageSystem` — if the entity is destroyed in the same frame, can a subsequent system still read the capability state? Or is it undefined? How did you write the test to avoid testing against undefined behavior?

**Q3:** `MissionDirectorSystem` increments `DoctrineState.InstanceId` to trigger preemption. Does the current `ChannelArbitrationSystem` implementation read `DoctrineState.InstanceId` or compare it differently? Confirm the preemption mechanism matches.

**Q4:** Any design decisions or edge cases beyond the spec?

---

## 🎯 Success Criteria

- [ ] **Corrective 0a** — `BallisticsSystem` in `PostSimulationSystemGroup`; `[UpdateAfter(LinearKinematicsSystem)]` commented in. Existing 6 Ballistics tests pass.
- [ ] **Corrective 0b** — `DamageSystem` in `SimulationSystemGroup`; cross-group `[UpdateAfter]` removed. Existing 5 Damage tests pass.
- [ ] **Corrective 0c** — `ActorCapabilityState` stripped on lethal hit. +1 new test passes.
- [ ] **DEBT-032** — `LinearKinematicsSystem` in `FDP.Toolkit.Physics`. 5 new tests pass. `BallisticsSystem` `[UpdateAfter]` uncommented.
- [ ] **BCS-P6-T1** — `MissionPlanQueue`, `MissionDirectorSystem`. 4 new tests pass.
- [ ] **Full solution: 0 errors.**
- [ ] **All tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **BATCH-10 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-10-REVIEW.md`
- **DESIGN.md §2.3 + §10:** `FDP/Docs/projects/behavior-control/DESIGN.md`
- **DESIGN.md §8.1:** same file, lines 354–367
- **TASK-DETAIL.md §BCS-P6-T1:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md`
- **BehaviorComponents.cs:** `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` — verify `ActorCapabilityState` field names
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
