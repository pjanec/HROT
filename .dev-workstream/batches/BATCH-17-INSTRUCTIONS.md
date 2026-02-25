# BATCH-17: Post-Phase-7 Cleanup + DEBT-007 GCHandle Fix

**Batch Number:** BATCH-17  
**Tasks:**
- **Corrective-0 (P2):** DEBT-037 — `ScenarioDirector` banned API (`Quaternion.CreateFromYawPitchRoll` → `SimMath.FromYaw`)
- **Corrective-1 (P2):** DEBT-038 — `TelemetryReporterSystem` magic number (`EjectPassengersActionId = 3` → `BehaviorConstants.ActionIdEjectPassengers`)
- **Corrective-2 (P3):** DEBT-036 — `SpatialHashSystem` literal constants sweep
- **Feature (P2):** DEBT-007 **full resolution** — `GCHandle` pattern: `EntityRepository.UnmanagedHandle` → `HsmKernelBridge.WorldHandle` → full ECS access inside HSM action delegates; delete `ApcBrainOutputSystem`

**⚠️ The `ApcBrainOutputSystem` approach previously specified has been REJECTED by the architect.
See `DEBT-007-HSM-ANALYSIS.md` for the full explanation and the correct GCHandle solution.**

**Phase:** Post-Phase-7 stabilisation + DEBT-007 full resolution  
**Estimated Effort:** 5–7 hours  
**Priority:** HIGH — correct architectural fix for HSM ECS access  
**Dependencies:** BATCH-16 ✅

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **DEBT-007 Analysis (REVISED):** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEBT-007-HSM-ANALYSIS.md` — read the entire document. This explains WHY `ApcBrainOutputSystem` is wrong and exactly HOW to implement the `GCHandle` fix.
2. **BATCH-16 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-16-REVIEW.md`
3. **`HsmKernel.cs`:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs` — confirm `fixed (TContext* ctxPtr = &context)` at line 92 (root cause of the constraint).
4. **`HsmTickSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` — understand the current `HsmKernelBridge` and where `World.UnmanagedHandle` will be passed.
5. **`EntityRepository.cs`:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` — where `_selfHandle` and `UnmanagedHandle` are added.
6. **`ApcHsmSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs` — `CruisingStateIndex`, `DisabledStateIndex`, and which action names are registered.
7. **CODE-STANDARDS.md:** §1 (no magic numbers), §2 (banned API).

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
4. DEBT-007 Step A — `EntityRepository.UnmanagedHandle` property + `_selfHandle` (Kernel layer) ✅
5. DEBT-007 Step B — `HsmKernelBridge.WorldHandle` field (Behavior toolkit) ✅
6. DEBT-007 Step C — `ApcHsmActions` stubs → full ECS implementations ✅
7. DEBT-007 Step D — Delete `ApcBrainOutputSystem`; verify T9 still passes ✅
8. Write and pass 3 new HSM action tests ✅
9. Full solution: 0 errors, all tests green ✅

---

## ✅ Tasks

### Corrective-0 (P2 — DEBT-037): `ScenarioDirector` banned API

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` (line ~191)

```csharp
// BEFORE (banned):
tf.Rotation = Quaternion.CreateFromYawPitchRoll(yawRadians, 0f, 0f);

// AFTER:
tf.Rotation = SimMath.FromYaw(yawRadians);
```

`SimMath` is in `Fdp.Kernel` — already imported. `Vector3` still requires `using System.Numerics;` so keep that directive.

**No new test needed** — T9 `UrbanAmbush_ApcMovesNorthward_BeforeAmbush` covers the orientation requirement.

---

### Corrective-1 (P2 — DEBT-038): `TelemetryReporterSystem` magic number

**Step A — `BehaviorConstants.cs`:** Add after `EventId_MobilityLost`:
```csharp
/// <summary>
/// Interaction action ID for the <see cref="Executors.EjectPassengersExecutor"/>.
/// Registered with <see cref="Systems.InteractionDispatcherSystem"/> at application startup.
/// Value must match the action ID used when registering the executor.
/// </summary>
public const ushort ActionIdEjectPassengers = 3;
```

**Step B — `TelemetryReporterSystem.cs`:** Remove `private const ushort EjectPassengersActionId = 3;` and replace the usage with `BehaviorConstants.ActionIdEjectPassengers`.

**Step C — `EjectPassengersExecutor.cs`:** Update doc comment line 10:
```csharp
/// Executor for the <c>EjectPassengers</c> interaction action
/// (<see cref="BehaviorConstants.ActionIdEjectPassengers"/> = 3).
```

---

### Corrective-2 (P3 — DEBT-036): `SpatialHashSystem` literal constants

Create `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashConstants.cs` (check if a constants file already exists first):

```csharp
namespace CarKinem.Spatial
{
    /// <summary>
    /// Compile-time parameters for <see cref="Systems.SpatialHashSystem"/> and <see cref="SpatialHashGrid"/>.
    /// See CODE-STANDARDS.md §1 (No magic numbers in production code).
    /// </summary>
    public static class SpatialHashConstants
    {
        /// <summary>Grid cell count along X axis. Width × CellSizeMeters = X coverage.</summary>
        public const int GridWidth  = 150;
        /// <summary>Grid cell count along Y axis. Height × CellSizeMeters = Y coverage.</summary>
        public const int GridHeight = 150;
        /// <summary>Cell edge length in meters.</summary>
        public const float CellSizeMeters = 5.0f;
        /// <summary>
        /// World-space X origin (bottom-left corner).
        /// Grid covers [OriginX, OriginX + GridWidth × CellSizeMeters] in X.
        /// Value: −GridWidth/2 × CellSizeMeters = −375 m, centring the grid on world origin.
        /// </summary>
        public const float OriginX = -375f;
        /// <summary>See <see cref="OriginX"/>. Grid covers [OriginY, OriginY + GridHeight × CellSizeMeters] in Y.</summary>
        public const float OriginY = -375f;
        /// <summary>Maximum entity capacity of the spatial hash (linked-list slot count).</summary>
        public const int MaxEntities = 100_000;
    }
}
```

Update `SpatialHashSystem.OnCreate()` to use these constants (replacing all five literals).

---

### Feature: DEBT-007 — GCHandle Pattern (Full HSM ECS Access)

**Read `DEBT-007-HSM-ANALYSIS.md` in full before writing any code.** The following is a summary of the three-step implementation. The analysis document contains the detailed rationale and exact code.

---

#### Step A — `EntityRepository.UnmanagedHandle` (Kernel layer)

**File:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs`

Add to the existing private field block (near top of the class):
```csharp
using System.Runtime.InteropServices;

// Inside EntityRepository class:

// Allocated once at construction; freed in Dispose.
// Provides an unmanaged IntPtr that HSM action delegates (via HsmKernelBridge.WorldHandle)
// use to recover this EntityRepository through the Fhsm.Kernel unmanaged constraint.
// See DEBT-007-HSM-ANALYSIS.md for full explanation.
private GCHandle _selfHandle;
```

In the constructor (after `Bus = new FdpEventBus();`):
```csharp
_selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
```

Add the public property (after the constructor):
```csharp
/// <summary>
/// Raw unmanaged handle to this repository. Valid for passing through
/// <c>unmanaged</c>-constrained contexts (e.g. <c>HsmKernelBridge</c>).
/// Recover via: <c>(EntityRepository)GCHandle.FromIntPtr(handle).Target!</c>
/// Remains valid until <see cref="Dispose"/> is called.
/// </summary>
public IntPtr UnmanagedHandle => GCHandle.ToIntPtr(_selfHandle);
```

In the `Dispose()` method (add before existing dispose logic):
```csharp
if (_selfHandle.IsAllocated)
    _selfHandle.Free();
```

> `GCHandleType.Normal` prevents the GC from moving the object (as opposed to `GCHandleType.Pinned` which prevents compaction — `Normal` is sufficient here since we only need a stable table entry, not a pinned memory address). The `GCHandle` allocates one slot in the GC handle table — a negligible fixed cost.

---

#### Step B — `HsmKernelBridge.WorldHandle` (Behavior toolkit)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs`

Update `HsmKernelBridge`:
```csharp
internal struct HsmKernelBridge
{
    public Entity Self;
    public IntPtr WorldHandle;   // ← IntPtr is unmanaged; holds GCHandle table index
}
```

Update the per-entity tick code (lines ~102–106):
```csharp
// BEFORE:
var fdpContext = new FdpHsmContext { Self = entity, World = World };
var bridge     = new HsmKernelBridge { Self = fdpContext.Self };

// AFTER (FdpHsmContext no longer needed for bridge construction):
var bridge = new HsmKernelBridge
{
    Self        = entity,
    WorldHandle = World.UnmanagedHandle,  // one property read per entity per tick
};
```

`FdpHsmContext` (the struct with `EntityRepository World`) can now be removed from `HsmTickSystem.cs` since it is no longer used. Remove it along with its XML doc comment. If it was part of a public API surface and other code references it, mark it `[Obsolete]` first and remove in a follow-up.

---

#### Step C — `ApcHsmActions` — Full implementations

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs`

Replace the two stubs with full implementations:

```csharp
using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Navigation;

namespace Fdp.Examples.UrbanCombat.Brains
{
    public static unsafe class ApcHsmActions
    {
        /// <summary>
        /// Activity action for the <c>Cruising</c> state.
        /// Runs every tick while the APC is Cruising.
        /// Writes <see cref="NavigationConstants.ActionIdFollowRoute"/> to
        /// <see cref="LocomotionChannel"/> so the vehicle follows its road-graph route northward.
        /// </summary>
        public static void Activity_Cruise(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            ref var loco    = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
            var     doctrine = repo.GetComponent<DoctrineState>(bridge->Self);

            loco.ActiveAction       = NavigationConstants.ActionIdFollowRoute;
            loco.DoctrineInstanceId = doctrine.InstanceId;
        }

        /// <summary>
        /// OnEntry action for the <c>Disabled</c> state.
        /// Fires exactly once when the HSM transitions into Disabled (on <c>MobilityLost</c> event).
        /// Clears <see cref="LocomotionChannel"/> and writes
        /// <see cref="BehaviorConstants.ActionIdEjectPassengers"/> to <see cref="InteractionChannel"/>.
        /// </summary>
        public static void OnEnter_Disabled(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            var doctrine = repo.GetComponent<DoctrineState>(bridge->Self);

            // Stop movement
            ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
            loco.ActiveAction = 0;

            // Trigger passenger eject — fires exactly once on OnEntry
            if (repo.HasComponent<InteractionChannel>(bridge->Self))
            {
                ref var interact = ref repo.GetComponentRW<InteractionChannel>(bridge->Self);
                interact.ActiveAction       = BehaviorConstants.ActionIdEjectPassengers;
                interact.DoctrineInstanceId = doctrine.InstanceId;
                unchecked { interact.ActionInstanceId++; }
            }
        }
    }
}
```

> ⚠️ Before writing: verify the exact field names (`DoctrineInstanceId`, `ActionInstanceId`, `ActiveAction`) on `LocomotionChannel` and `InteractionChannel` from their actual struct definitions. The names above are inferred from `TrafficBrainSystem` and test helpers but must be confirmed.

---

#### Step D — Delete `ApcBrainOutputSystem`

**File to delete:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcBrainOutputSystem.cs`

Also remove its registration from `HeadlessDemoApp.RegisterSystems()`.

**Why:** With full HSM action delegates implemented, `ApcBrainOutputSystem` is redundant. Worse, if kept, it would race with the action delegates — both writing `LocomotionChannel` in the same frame. The HSM owns its output surface; the external system must not duplicate it.

> After deletion, run the full test suite. If T9 `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` still passes, the HSM delegates are correctly driving all channels.

---

## 🧪 Testing Requirements

### Corrective tests (no new)
- Corrective-0/1/2 have no new tests; existing tests cover the changed code paths.

### DEBT-007 tests — 3 new (add to `ApcBrainTests.cs` or `BlueprintTests.cs`)

```csharp
[Fact]
public void HsmAction_ActivityCruise_WritesFollowRoute_ToLocomotionChannel()
{
    // Arrange: entity with LocomotionChannel, DoctrineState, BrainHsm128 in Cruising state
    // Wire the GCHandle: bridge.WorldHandle = _app.World.UnmanagedHandle
    // Call: ApcHsmActions.Activity_Cruise(null, &bridge, null)
    // Assert: LocomotionChannel.ActiveAction == NavigationConstants.ActionIdFollowRoute
}

[Fact]
public void HsmAction_OnEnterDisabled_ClearsLocomotion_AndWritesEject()
{
    // Arrange: entity with LocomotionChannel (ActiveAction = ActionIdFollowRoute),
    //          InteractionChannel, DoctrineState
    //          bridge.WorldHandle = _app.World.UnmanagedHandle
    // Call: ApcHsmActions.OnEnter_Disabled(null, &bridge, null)
    // Assert: LocomotionChannel.ActiveAction == 0
    // Assert: InteractionChannel.ActiveAction == BehaviorConstants.ActionIdEjectPassengers
}

[Fact]
public void UnmanagedHandle_RecoveredTarget_IsSameInstance()
{
    // Arrange: var repo = new EntityRepository() (already initialized in _app)
    // Act: var handle = repo.UnmanagedHandle
    //      var recovered = (EntityRepository)GCHandle.FromIntPtr(handle).Target!
    // Assert: object.ReferenceEquals(repo, recovered)
    // (Proves the GCHandle round-trip is correct)
}
```

### Integration
- **T9 `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` must still pass after Step D (ApcBrainOutputSystem deletion).** This is the gate. If it fails, diagnose — the HSM delegates are not firing (likely the action delegates are not registered with the dispatcher; see **ApcHsmSetup.Build()** to verify registration names match).

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-17-REPORT.md`

**Q1:** After Step D (delete `ApcBrainOutputSystem`), does T9 still pass? If milestones are missing, which are missing and what is the root cause?

**Q2:** What `GCHandleType` did you use (`Normal` or `Pinned`) and why? What would go wrong with `Pinned`?

**Q3:** Are the HSM action delegate names in `ApcHsmActions.cs` (`"Activity_Cruise"`, `"OnEnter_Disabled"`) correctly registered in `ApcHsmSetup.Build()`? Show the registration calls.

**Q4:** Does `FdpHsmContext` (the old user-facing struct) still exist after your changes? If so, is it referenced anywhere?

**Q5:** Any surprises?

---

## 🎯 Success Criteria

- [ ] **DEBT-037** resolved: `SimMath.FromYaw` in `ScenarioDirector.cs`.
- [ ] **DEBT-038** resolved: `BehaviorConstants.ActionIdEjectPassengers`; `TelemetryReporterSystem` and `EjectPassengersExecutor` doc updated.
- [ ] **DEBT-036** resolved: `SpatialHashConstants.cs`; `SpatialHashSystem.OnCreate()` uses named constants.
- [ ] **DEBT-007 FULLY resolved:**
  - [ ] `EntityRepository.UnmanagedHandle` property added; `_selfHandle` allocated in constructor, freed in `Dispose`.
  - [ ] `HsmKernelBridge.WorldHandle : IntPtr` field added.
  - [ ] `ApcHsmActions.Activity_Cruise` writes `ActionIdFollowRoute` to `LocomotionChannel`.
  - [ ] `ApcHsmActions.OnEnter_Disabled` clears locomotion and writes `ActionIdEjectPassengers` to `InteractionChannel`.
  - [ ] `ApcBrainOutputSystem` deleted; its wiring removed from `HeadlessDemoApp`.
  - [ ] 3 new tests pass.
  - [ ] T9 full-run milestone test still passes.
- [ ] **Zero build errors; all tests green.**
- [ ] **Report submitted.**

---

## 📚 Reference Materials

- **DEBT-007 Analysis:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\DEBT-007-HSM-ANALYSIS.md` ← primary reference
- **FastHSM kernel:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs` (lines 91–92 — the `fixed` pin)
- **`HsmTickSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs`
- **`EntityRepository.cs`:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs`
- **`ApcHsmSetup.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
- **`ApcHsmActions.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs`
- **`BehaviorConstants.cs`:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **`ScenarioDirector.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs`
- **`TelemetryReporterSystem.cs`:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs`
- **`SpatialHashSystem.cs`:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs`
- **DEBT-TRACKER:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\.dev-workstream\guides\CODE-STANDARDS.md`
