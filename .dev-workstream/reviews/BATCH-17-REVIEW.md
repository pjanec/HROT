# BATCH-17 Review

**Batch:** BATCH-17  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ✅ APPROVED — clean, no issues found

---

## Summary

All four tasks completed correctly. Three correctives resolved long-standing CODE-STANDARDS
violations; the DEBT-007 GCHandle fix is architecturally sound and complete.
The developer discovered and resolved three non-trivial integration surprises without prompting.
26 → 30 tests, zero failures.

---

## Corrective-0 (DEBT-037) — `SimMath.FromYaw` ✅

`Quaternion.CreateFromYawPitchRoll` is gone from all production code in `Fdp.Examples.UrbanCombat`.
Confirmed by grep: zero results across the entire `FDP/` tree (the one remaining hit is a comment
inside `CarKinematicsSystem.cs` **explaining** why the toolkit avoids it — correct, not a
violation).

`SimMath.FromYaw` is used at the single call site in `ScenarioDirector.cs`. T9
`UrbanAmbush_ApcMovesNorthward_BeforeAmbush` still passes, confirming the orientation is correct.

---

## Corrective-1 (DEBT-038) — `BehaviorConstants.ActionIdEjectPassengers` ✅

- `BehaviorConstants.ActionIdEjectPassengers = 3` added. ✅
- `TelemetryReporterSystem` local const removed; toolkit constant used. ✅
- `EjectPassengersExecutor` doc comment updated to `<see cref="BehaviorConstants.ActionIdEjectPassengers"/>`. ✅

One observation: `BehaviorConstants.ActionIdEjectPassengers` is the only action-ID constant in
`BehaviorConstants`. The other dispatcher action IDs (`ActionIdFollowRoute`,
`ActionIdAimAndFire`, etc.) live in their respective toolkit constant files
(`NavigationConstants`, `CombatConstants`). This is consistent — `EjectPassengers` is a
Behavior-toolkit action so `BehaviorConstants` is the correct home. No issue.

---

## Corrective-2 (DEBT-036) — `SpatialHashConstants` ✅

`SpatialHashConstants.cs` created in `CarKinem.Spatial` namespace. Six named constants:
`GridWidth`, `GridHeight`, `CellSizeMeters`, `OriginX`, `OriginY`, `MaxEntities`.
`SpatialHashSystem.OnCreate()` uses all six. Named constants now document the arithmetic
(`OriginX = −GridWidth/2 × CellSizeMeters`). No magic numbers remain.

---

## DEBT-007 — GCHandle Full Resolution ✅

### Step A: `EntityRepository.UnmanagedHandle` (Kernel)

- `_selfHandle = GCHandle.Alloc(this, GCHandleType.Normal)` in constructor. ✅
- `public IntPtr UnmanagedHandle => GCHandle.ToIntPtr(_selfHandle)` — correct, simple property. ✅
- `Dispose()` line 1863: `if (_selfHandle.IsAllocated) _selfHandle.Free()` — guard is correct;
  prevents double-free if `Dispose()` is called twice (which is legal on `IDisposable`). ✅

`_disposed` field (pre-existing) is checked at the top of `Dispose()` — so even without the
`IsAllocated` guard, double-free would be prevented. The guard is defence-in-depth. ✅

`GCHandleType.Normal` is confirmed correct (see Q2 in the report: prevents collection, allows
relocation; `GCHandle.ToIntPtr` returns the stable table-slot index, not the memory address).

### Step B: `HsmKernelBridge` (Behavior toolkit)

- `HsmKernelBridge` promoted to `public` (required for cross-assembly use). ✅
- `WorldHandle : IntPtr` field added. ✅
- `FdpHsmContext` struct (with the dangling `EntityRepository World` field) removed entirely. ✅
- `HsmTickSystem<T>.OnUpdate()` builds `bridge` with `WorldHandle = World.UnmanagedHandle`. ✅

The doc comment on `HsmKernelBridge` is precise: explains the `IntPtr` is a GCHandle table index,
not a raw memory address, and gives the recovery pattern. ✅

### Step C: `ApcHsmActions` implementations

Both delegates fully implemented:

**`Activity_Cruise`:**
```csharp
loco.ActiveAction       = NavigationConstants.ActionIdFollowRoute;
loco.DoctrineInstanceId = doctrine.InstanceId;
```
Runs every tick while Cruising — correct for a continuous Activity. ✅  
No `HasComponent` guard on `LocomotionChannel` — correct, since this delegate is only registered
for APC entities which always have `LocomotionChannel` in the production scenario. ✅

**`OnEnter_Disabled`:**
```csharp
if (repo.HasComponent<LocomotionChannel>(bridge->Self))
    loco.ActiveAction = 0;

if (repo.HasComponent<InteractionChannel>(bridge->Self))
{
    interact.ActiveAction       = BehaviorConstants.ActionIdEjectPassengers;
    interact.DoctrineInstanceId = doctrine.InstanceId;
    unchecked { interact.ActionInstanceId++; }
}
```
Fires exactly once — it is an `OnEntry` action, not an Activity. ✅  
`HasComponent` guard — added to survive minimal test worlds without `LocomotionChannel`.
This guard is a no-op in production (all APC TKB templates have both channels). ✅  
`unchecked { interact.ActionInstanceId++ }` — correct pattern; matches `TrafficBrainSystem`
and `ScenarioDirector` to signal a new action instance to `ChannelArbitrationSystem`. ✅

`[HsmAction]` attribute on both delegates. ✅  
`Fhsm.SourceGen` added as analyzer reference. ✅  
`HsmActionRegistrar.RegisterAll()` called in `HeadlessDemoApp.Initialize()`. ✅

### Step D: `ApcBrainOutputSystem` deletion

File confirmed absent. Wiring removed from `HeadlessDemoApp`. T9 still passes after deletion —
confirms HSM delegates are correctly driving both channels. ✅

---

## Tests

### Before / After

| Batch | New tests | Total |
|---|---|---|
| BATCH-16 | 9 | 26 |
| BATCH-17 | 4 | **30** |

*(4 new: 3 in `ApcBrainTests.cs` + `HsmKernelBridge_WorldHandle_RoundTrip` in `HsmTickSystemTests.cs`)*

### Quality

**T1 — `UnmanagedHandle_RecoveredTarget_IsSameInstance`:** Uses `object.ReferenceEquals`. Correct — this proves same managed object, not just equal value. ✅

**T2 — `HsmAction_ActivityCruise_WritesFollowRoute_ToLocomotionChannel`:** Calls delegate directly with a constructed `HsmKernelBridge`. Clean, minimal, deterministic. Passes `null` for `instance` and `writer` — correct since neither delegate reads those in these implementations. ✅

**T3 — `HsmAction_OnEnterDisabled_ClearsLocomotion_AndWritesEject`:** Two-component assertion (locomotion = 0, interact = EjectPassengers). Tight, correct. ✅

**`HsmKernelBridge_WorldHandle_RoundTrip_RecoversSameInstance`** (toolkit test): Uses `Assert.Same` — correct equivalent of `ReferenceEquals`. Validates the exact round-trip pattern used by every HSM action delegate in production. ✅

One observation (P3, not blocking): T2 could additionally verify `LocomotionChannel.DoctrineInstanceId == 1` (matching the `DoctrineState.InstanceId = 1` set in Arrange). The current assertion only checks `ActiveAction`. Not a defect, but the invariant is undocumented. Log as minor gap — add a comment, no new test needed.

---

## Discovered Surprises — correctly handled

**Surprise 1: Action dispatch not automatic** — HSM delegates must be explicitly registered with
`HsmActionDispatcher` (via hash). The `[HsmAction]` + `Fhsm.SourceGen` + `RegisterAll()` path
is the correct FDP-way; the developer found it and applied it correctly. The BATCH-17 instructions
did not specify this step — confirmed as a documentation gap in the batch instructions, not a
code error. Added to lessons-learned for future HSM batch instructions.

**Surprise 2: `HsmKernelBridge` visibility** — making it `public` to allow cross-assembly
pointer casts is correct. Alternatively, the bridge could remain `internal` and an
`InternalsVisibleTo` attribute added — but `public` is simpler and the struct is already
documented as a toolkit-level integration point. Acceptable. ✅

**Surprise 3: `HasComponent` guard in `OnEnter_Disabled`** — correct defensive pattern
matching the established style in `EjectPassengersExecutor` and unit-test helpers. ✅

---

## Zero-issue Verdict

No code issues, no test issues, no standards violations. All four tasks complete.

**DEBT-007, DEBT-036, DEBT-037, DEBT-038 fully resolved.**

---

## Commit Message (approved)

```
fix(BATCH-17): resolve DEBT-007/036/037/038 — GCHandle HSM bridge + code standards

DEBT-037: ScenarioDirector.cs — Quaternion.CreateFromYawPitchRoll → SimMath.FromYaw

DEBT-038: BehaviorConstants.ActionIdEjectPassengers = 3 (toolkit constant);
  TelemetryReporterSystem private const removed; EjectPassengersExecutor doc updated

DEBT-036: SpatialHashConstants.cs (new) — GridWidth/Height/CellSizeMeters/OriginX/Y/MaxEntities;
  SpatialHashSystem.OnCreate uses named constants

DEBT-007 (full resolution — GCHandle pattern):
  EntityRepository: _selfHandle (GCHandle.Normal, ctor alloc, Dispose free), UnmanagedHandle : IntPtr
  HsmKernelBridge: public + WorldHandle : IntPtr; FdpHsmContext removed
  HsmTickSystem<T>: bridge.WorldHandle = World.UnmanagedHandle (one read/entity/tick)
  ApcHsmActions: Activity_Cruise and OnEnter_Disabled fully implemented (not stubs)
    + [HsmAction] attributes; Fhsm.SourceGen analyzer ref; RegisterAll() in HeadlessDemoApp
  ApcBrainOutputSystem: DELETED (HSM delegates own full output surface)
  +4 tests: UnmanagedHandle round-trip, Activity_Cruise write, OnEnter_Disabled clear+eject,
            HsmKernelBridge WorldHandle recovery

Total tests: 30 / 30 pass. Zero build errors.
```
