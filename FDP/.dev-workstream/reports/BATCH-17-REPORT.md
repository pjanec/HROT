# BATCH-17 Report

**Batch:** BATCH-17
**Date:** 2025-07-14
**Debts resolved:** DEBT-037, DEBT-038, DEBT-036, DEBT-007

---

## Checklist

- [x] **DEBT-037** resolved: `SimMath.FromYaw` in `ScenarioDirector.cs`.
- [x] **DEBT-038** resolved: `BehaviorConstants.ActionIdEjectPassengers`; `TelemetryReporterSystem` and `EjectPassengersExecutor` doc updated.
- [x] **DEBT-036** resolved: `SpatialHashConstants.cs`; `SpatialHashSystem.OnCreate()` uses named constants.
- [x] **DEBT-007 FULLY resolved:**
  - [x] `EntityRepository.UnmanagedHandle` property added; `_selfHandle` allocated in constructor, freed in `Dispose`.
  - [x] `HsmKernelBridge.WorldHandle : IntPtr` field added.
  - [x] `ApcHsmActions.Activity_Cruise` writes `ActionIdFollowRoute` to `LocomotionChannel`.
  - [x] `ApcHsmActions.OnEnter_Disabled` clears locomotion and writes `ActionIdEjectPassengers` to `InteractionChannel`.
  - [x] `ApcBrainOutputSystem` deleted; its wiring removed from `HeadlessDemoApp`.
  - [x] 3 new tests pass.
  - [x] T9 full-run milestone test still passes.
- [x] **Zero build errors; all tests green.**

---

## Q1 — Does T9 still pass after Step D (ApcBrainOutputSystem deletion)?

**Yes.** `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` passes after the deletion.

However, T9 did *not* pass immediately after Step D. It failed with:

```
"INTERACTION: EjectPassengers" milestone not found
```

**Root cause:** `ApcHsmActions.Activity_Cruise` and `OnEnter_Disabled` were not registered with `HsmActionDispatcher`. The dispatcher uses an `ActionTable` keyed by FNV-1a hash of action name. Without registration, the kernel silently skips both actions — `OnEnter_Disabled` never fired, so the `InteractionChannel` was never written, and the `TelemetryReporterSystem` never logged the milestone.

**Fix applied (not part of original steps, but required):**
1. Added `Fhsm.SourceGen` as an `OutputItemType="Analyzer"` project reference to `Fdp.Examples.UrbanCombat.csproj`. The source generator scans for `[HsmAction]`-decorated methods and emits `{AssemblyName}.Generated.HsmActionRegistrar.RegisterAll()`.
2. Added `[HsmAction]` attribute (from `Fhsm.Kernel.Attributes`) to both `Activity_Cruise` and `OnEnter_Disabled`.
3. Called `Fdp.Examples.UrbanCombat.Generated.HsmActionRegistrar.RegisterAll()` in `HeadlessDemoApp.Initialize()`.

After these three additions the build succeeded and T9 passed.

---

## Q2 — `GCHandleType.Normal` vs `GCHandleType.Pinned`

`GCHandleType.Normal` was used.

`Normal` inserts the object into the GC handle table and prevents it from being *collected*, but still allows the GC to *relocate* it during compaction. `GCHandle.ToIntPtr` returns the **table-slot index** (a stable integer), not the object's memory address. The action delegates recover the object via `GCHandle.FromIntPtr(handle).Target`, which reads the (possibly updated) internal pointer stored by the GC — so relocation is transparent.

`Pinned` would additionally lock the object's memory address, preventing compaction of the managed heap segment containing `EntityRepository`. Because `EntityRepository` is a large, long-lived object with many internal arrays, pinning it would create a persistent hole in the heap, increasing fragmentation and GC overhead for the lifetime of the simulation. Since we only need a stable *lookup key* (not a stable *address*), `Normal` is sufficient and correct.

---

## Q3 — Registration names in `ApcHsmSetup.Build()`

Yes, the names match exactly. From `ApcHsmSetup.Build()` (lines 61–62):

```csharp
.RegisterAction("Activity_Cruise")
.RegisterAction("OnEnter_Disabled");
```

These strings are hashed with FNV-1a at compile-time by the `HsmFlattener` and baked into the `HsmDefinitionBlob`. At runtime the `HsmActionDispatcher` maps the same hash to a `delegate*` function pointer. Because the `[HsmAction]`-decorated method names in `ApcHsmActions.cs` are exactly `Activity_Cruise` and `OnEnter_Disabled`, the source generator produces hash entries that align with the blob — the kernel can dispatch each action correctly.

---

## Q4 — Does `FdpHsmContext` still exist?

No. `FdpHsmContext` (the struct with `EntityRepository World`) was removed entirely from `HsmTickSystem.cs`. There are no remaining references in production code or tests.

The one test that referenced it — `FdpHsmContext_ExposesWorldAccess` in `HsmTickSystemTests.cs` — was replaced with `HsmKernelBridge_WorldHandle_RoundTrip_RecoversSameInstance`, which validates the GCHandle round-trip through `HsmKernelBridge.WorldHandle`.

---

## Q5 — Surprises

**1. Action dispatch not automatic.**
The instruction steps described the GCHandle plumbing (Steps A–D) but did not explicitly mention that the action delegates also need to be *registered* with `HsmActionDispatcher`. The FastHSM kernel calls delegates by hash ID; without a registration entry the kernel silently no-ops. This required three additional changes: `Fhsm.SourceGen` analyzer reference, `[HsmAction]` attributes, and the `RegisterAll()` call.

**2. `HsmKernelBridge` visibility.**
`HsmKernelBridge` was `internal`, but action delegates in `Fdp.Examples.UrbanCombat` (a separate assembly) need to cast `void* context` to `HsmKernelBridge*`. The struct was changed to `public` to allow this cross-assembly access.

**3. `OnEnter_Disabled` needs `HasComponent` guard on `LocomotionChannel`.**
The original Step C code in the instructions called `GetComponentRW<LocomotionChannel>` unconditionally. `BlueprintTests.ApcHsm_TransitionsToDisabled_OnMobilityLostEvent` constructs a minimal world without registering `LocomotionChannel`, so the unconditional call threw `InvalidOperationException`. The fix — add `if (repo.HasComponent<LocomotionChannel>(bridge->Self))` guard before the write, matching the existing `InteractionChannel` guard pattern — was applied. In the full scenario all APC entities have `LocomotionChannel` (stamped by the TKB template), so the guard is a no-op in production but keeps unit tests with minimal worlds working.

---

## Files Changed

| File | Change |
|---|---|
| `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` | `SimMath.FromYaw` (DEBT-037) |
| `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` | Added `ActionIdEjectPassengers = 3` (DEBT-038) |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs` | Removed local const; use `BehaviorConstants.ActionIdEjectPassengers` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` | Doc comment uses `<see cref>` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashConstants.cs` | **NEW** — compile-time constants (DEBT-036) |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` | `OnCreate()` uses `SpatialHashConstants.*` |
| `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` | `_selfHandle`, `UnmanagedHandle`, ctor alloc, Dispose free (DEBT-007 Step A) |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` | `HsmKernelBridge` public + `WorldHandle`; `FdpHsmContext` removed (Step B) |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs` | Full implementations + `[HsmAction]` (Step C) |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcBrainOutputSystem.cs` | **DELETED** (Step D) |
| `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` | `ApcBrainOutputSystem` unregistered; `RegisterAll()` added |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj` | `Fhsm.SourceGen` analyzer reference added |
| `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/ApcBrainTests.cs` | **NEW** — 3 DEBT-007 unit tests |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/HsmTickSystemTests.cs` | `FdpHsmContext` test replaced with `WorldHandle` round-trip test |
