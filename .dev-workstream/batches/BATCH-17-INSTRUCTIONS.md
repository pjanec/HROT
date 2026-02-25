# BATCH-17: Post-Phase-7 Cleanup + DEBT-007 HSM Bridge

**Batch Number:** BATCH-17  
**Tasks:**
- **Corrective-0 (P2):** DEBT-037 — `ScenarioDirector` banned API (`Quaternion.CreateFromYawPitchRoll` → `SimMath.FromYaw`)
- **Corrective-1 (P2):** DEBT-038 — `TelemetryReporterSystem` magic number (`EjectPassengersActionId = 3` → `BehaviorConstants.ActionIdEjectPassengers`)
- **Corrective-2 (P3):** DEBT-036 — `SpatialHashSystem` literal constants sweep
- **Feature:** DEBT-007 partial resolution — `ApcBrainOutputSystem` bridge (HSM state → ECS channel writes)

**Phase:** Post-Phase-7 stabilisation  
**Estimated Effort:** 3–5 hours  
**Priority:** MEDIUM — two P2 standard violations + one open architectural stub  
**Dependencies:** BATCH-16 ✅

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-16 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-16-REVIEW.md` — all three issues + DEBT-007 analysis.
2. **DEBT-TRACKER:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — DEBT-036, 037, 038 entries.
3. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\.dev-workstream\guides\CODE-STANDARDS.md` — §1 (no magic numbers), §2 (banned API).
4. **`SimMath.cs`:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs` — verify `FromYaw`, `FacingNorth` API.
5. **`ApcHsmActions.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs` — understand the stub context.
6. **`ApcHsmSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs` — `CruisingStateIndex`, `DisabledStateIndex` constants.

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-17-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Corrective-0 (DEBT-037) — `SimMath.FromYaw` → build green ✅
2. Corrective-1 (DEBT-038) — `BehaviorConstants.ActionIdEjectPassengers` → build green ✅
3. Corrective-2 (DEBT-036) — `SpatialHashConstants` → build green ✅
4. `ApcBrainOutputSystem` feature + test ✅
5. Full solution: all existing tests still green ✅

---

## ✅ Tasks

### Corrective-0 (P2 — DEBT-037): `ScenarioDirector` banned API

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` (line ~191)

**Change:**
```csharp
// BEFORE (banned):
tf.Rotation = Quaternion.CreateFromYawPitchRoll(yawRadians, 0f, 0f);

// AFTER:
tf.Rotation = SimMath.FromYaw(yawRadians);
```

`SimMath` is in `Fdp.Kernel` — already imported by `ScenarioDirector.cs`. No new using needed.

Remove the `using System.Numerics;` directive from `ScenarioDirector.cs` **if** it is now only used for the banned call. Check that `Vector3` still needs it (it does — `Vector3` is `System.Numerics`).

**No new test needed** — existing T7 tests cover spawn counting and embark state. The geometry is verified by the T9 `UrbanAmbush_ApcMovesNorthward_BeforeAmbush` test (APC must move north, which requires correct orientation).

---

### Corrective-1 (P2 — DEBT-038): `TelemetryReporterSystem` magic number

**Step A — Add constant to `BehaviorConstants.cs`:**

File: `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`

Add after `EventId_MobilityLost`:
```csharp
/// <summary>
/// Interaction action ID for the <see cref="Executors.EjectPassengersExecutor"/>.
/// Registered with <see cref="Systems.InteractionDispatcherSystem"/> at application startup.
/// Value must match the action ID used when registering the executor.
/// </summary>
public const ushort ActionIdEjectPassengers = 3;
```

**Step B — Update `TelemetryReporterSystem.cs`:**

File: `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs`

Remove the private const:
```csharp
// DELETE this line:
private const ushort EjectPassengersActionId = 3;
```

Replace the usage:
```csharp
// BEFORE:
if (channel.ActiveAction == EjectPassengersActionId)

// AFTER:
if (channel.ActiveAction == BehaviorConstants.ActionIdEjectPassengers)
```

Add `using FDP.Toolkit.Behavior;` to the using directives (may already be present).

**Step C — Update `EjectPassengersExecutor.cs` doc comment:**

File: `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` (line 10)

```csharp
// BEFORE:
/// Executor for the <c>EjectPassengers</c> interaction action (kind = 3).

// AFTER:
/// Executor for the <c>EjectPassengers</c> interaction action
/// (<see cref="BehaviorConstants.ActionIdEjectPassengers"/> = 3).
```

---

### Corrective-2 (P3 — DEBT-036): `SpatialHashSystem` literal constants

**Step A — Add constants.** Decide the best location:
- If a `SpatialHashConstants.cs` (or `CarKinemConstants.cs`) already exists in `FDP.Toolkit.CarKinem` → add there.
- If not → create `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashConstants.cs`.

```csharp
namespace CarKinem.Spatial
{
    /// <summary>
    /// Compile-time parameters for <see cref="Systems.SpatialHashSystem"/>
    /// and <see cref="SpatialHashGrid"/>.
    /// See CODE-STANDARDS.md §1 (No magic numbers in production code).
    /// </summary>
    public static class SpatialHashConstants
    {
        /// <summary>Number of cells along each axis. Width × Height × CellSizeMeters = world coverage.</summary>
        public const int GridWidth  = 150;
        /// <summary>See <see cref="GridWidth"/>.</summary>
        public const int GridHeight = 150;
        /// <summary>Cell edge length in meters.</summary>
        public const float CellSizeMeters = 5.0f;
        /// <summary>
        /// World-space X origin (bottom-left corner). Grid covers
        /// [OriginX, OriginX + GridWidth × CellSizeMeters] in X.
        /// </summary>
        public const float OriginX = -375f;  // -GridWidth/2 * CellSizeMeters
        /// <summary>See <see cref="OriginX"/>.</summary>
        public const float OriginY = -375f;
        /// <summary>Maximum entity capacity of the grid (linked-list slots).</summary>
        public const int MaxEntities = 100_000;
    }
}
```

**Step B — Update `SpatialHashSystem.cs`:**

```csharp
_grid = SpatialHashGrid.Create(
    SpatialHashConstants.GridWidth,
    SpatialHashConstants.GridHeight,
    SpatialHashConstants.CellSizeMeters,
    SpatialHashConstants.MaxEntities,
    Allocator.Persistent,
    originX: SpatialHashConstants.OriginX,
    originY: SpatialHashConstants.OriginY);
```

---

### Feature: `ApcBrainOutputSystem` — DEBT-007 HSM Bridge

**Context:** The `ApcHsmActions.Activity_Cruise` and `OnEnter_Disabled` delegates are stubs because `EntityRepository` cannot be passed through the `void* context` HSM dispatch pointer. This bridge system reads the APC's current HSM state index and writes the appropriate channels externally — decoupling HSM state transitions from ECS mutations.

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcBrainOutputSystem.cs`

> This file was listed as created in the BATCH-16 report with only `unsafe void OnUpdate()`. Verify the current content before rewriting.

**Design:**
```csharp
/// <summary>
/// Bridge that reads the current HSM state of the Military APC and translates it into
/// ECS channel writes. This decouples the HSM's raw-pointer action dispatch from ECS access,
/// resolving the DEBT-007 architectural gap for APC entities.
///
/// <para>Runs in <see cref="SimulationSystemGroup"/> after <see cref="HsmTickSystem{TBrain}"/>
/// (so state transitions have already occurred this frame) and before
/// <see cref="ChannelArbitrationSystem"/> (because it writes <em>directly</em> to channels
/// without going through the channel arbitration protocol — it is the brain output,
/// not a channel request).</para>
///
/// <para>State → channel mapping:</para>
/// <list type="table">
///   <item><c>CruisingStateIndex</c> → <see cref="LocomotionChannel.ActiveAction"/> =
///         <see cref="NavigationConstants.ActionIdFollowRoute"/>.</item>
///   <item><c>DisabledStateIndex</c> → <see cref="LocomotionChannel.ActiveAction"/> = 0;
///         <see cref="InteractionChannel.ActiveAction"/> = <see cref="BehaviorConstants.ActionIdEjectPassengers"/>
///         (once, on the first frame in Disabled — use shadow to avoid repeated eject).</item>
/// </list>
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HsmTickSystem<BrainHsm128>))]
[UpdateBefore(typeof(ChannelArbitrationSystem))]
public class ApcBrainOutputSystem : ComponentSystem
{
    // Shadow: tracks the previous HSM state per entity to detect first-frame-in-Disabled.
    private readonly Dictionary<int, ushort> _prevState = new();

    protected override unsafe void OnUpdate()
    {
        var q = World.Query()
            .With<BrainHsm128>()
            .With<LocomotionChannel>()
            .With<DoctrineState>()
            .Build();

        foreach (var entity in q)
        {
            var brain    = World.GetComponent<BrainHsm128>(entity);
            ushort state = brain.State.ActiveLeafIds[0];
            int key      = entity.Index;

            _prevState.TryGetValue(key, out ushort prev);

            ref var loco = ref World.GetComponentRW<LocomotionChannel>(entity);
            var doctrine = World.GetComponent<DoctrineState>(entity);

            if (state == ApcHsmSetup.CruisingStateIndex)
            {
                // Write locomotion intent: follow the road graph northward.
                loco.ActiveAction      = NavigationConstants.ActionIdFollowRoute;
                loco.DoctrineInstanceId = doctrine.InstanceId;
            }
            else if (state == ApcHsmSetup.DisabledStateIndex)
            {
                // Stop locomotion.
                loco.ActiveAction = 0;

                // On first frame entering Disabled: trigger eject.
                if (prev != ApcHsmSetup.DisabledStateIndex
                    && World.HasComponent<InteractionChannel>(entity))
                {
                    ref var interact = ref World.GetComponentRW<InteractionChannel>(entity);
                    interact.ActiveAction      = BehaviorConstants.ActionIdEjectPassengers;
                    interact.DoctrineInstanceId = doctrine.InstanceId;
                    unchecked { interact.ActionInstanceId++; }
                }
            }

            _prevState[key] = state;
        }
    }
}
```

> ⚠️ Verify the actual `LocomotionChannel` and `InteractionChannel` field names (`DoctrineInstanceId`, `ActionInstanceId`) against the actual component definitions before writing. These were inferred from `TrafficBrainSystem` and test helpers.

**Tests:**
```csharp
[Fact] void ApcBrainOutput_WritesFollowRoute_WhenCruising()
// Entity: BrainHsm128 (CruisingStateIndex), DoctrineState (BrainTierHsm), LocomotionChannel.
// Run ApcBrainOutputSystem.
// Assert: LocomotionChannel.ActiveAction == NavigationConstants.ActionIdFollowRoute.

[Fact] void ApcBrainOutput_ClearsLocomotion_WhenDisabled()
// Entity: BrainHsm128 (DisabledStateIndex), LocomotionChannel{ActiveAction = ActionIdFollowRoute}.
// Run ApcBrainOutputSystem.
// Assert: LocomotionChannel.ActiveAction == 0.

[Fact] void ApcBrainOutput_WritesEjectPassengers_OnFirstFrameInDisabled()
// Entity: transition Cruising → Disabled (two Run() calls with state change between them).
// Assert: InteractionChannel.ActiveAction == BehaviorConstants.ActionIdEjectPassengers on second run.
// Assert: InteractionChannel.ActiveAction == ActionIdEjectPassengers only on FIRST Disabled frame
//         (third run with state still Disabled → no second eject written — prev == Disabled).
```

**Wire into `HeadlessDemoApp.RegisterSystems()`** — register `ApcBrainOutputSystem` in the `SimulationSystemGroup` after HSM tick but before channel arbitration. The T9 integration test should still pass with this system active.

---

## 🧪 Testing Requirements

- **Corrective-0:** No new test. Existing T9 APC northward movement test covers orientation.
- **Corrective-1:** No new test. Existing T8 `INTERACTION: EjectPassengers` telemetry test covers the path.
- **Corrective-2:** No new test. Build-time constant coverage is sufficient.
- **`ApcBrainOutputSystem`:** 3 new tests (see above).
- **All existing 26+ UrbanCombat tests + full FDP.sln suite must remain green.**

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-17-REPORT.md`

**Q1:** After adding `ApcBrainOutputSystem`, does the T9 `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` test still pass? Were any milestone timings affected?

**Q2:** Does `LocomotionChannel.DoctrineInstanceId` actually exist on the struct? If the field name is different, what is it?

**Q3:** With `ApcBrainOutputSystem` active, does the APC actually move northward further in T9? (Check APC position at frame 100 vs previous run.)

**Q4:** Any surprises?

---

## 🎯 Success Criteria

- [ ] **DEBT-037** resolved: `SimMath.FromYaw` in `ScenarioDirector.cs`; no `Quaternion.CreateFromYawPitchRoll` in any production file in `Fdp.Examples.UrbanCombat`.
- [ ] **DEBT-038** resolved: `BehaviorConstants.ActionIdEjectPassengers = 3` exists; `TelemetryReporterSystem` and `EjectPassengersExecutor` doc updated.
- [ ] **DEBT-036** resolved: `SpatialHashConstants.cs` created; `SpatialHashSystem.OnCreate()` uses named constants.
- [ ] **DEBT-007** progress: `ApcBrainOutputSystem` implemented and registered; 3 tests pass; APC moves northward (T9 movement test still passes).
- [ ] **Zero build errors; all tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **BATCH-16 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-16-REVIEW.md`
- **DEBT-TRACKER:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\.dev-workstream\guides\CODE-STANDARDS.md`
- **`SimMath.cs`:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs`
- **`BehaviorConstants.cs`:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **`ApcHsmSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
- **`ScenarioDirector.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs`
- **`TelemetryReporterSystem.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs`
- **`SpatialHashSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs`
