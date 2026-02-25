# BATCH-16 Review

**Batch:** BATCH-16  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ⚠️ NEEDS FIX — two issues (§2 banned API + §1 magic number)

---

## Issues Found

### Issue 1: `Quaternion.CreateFromYawPitchRoll` used in `ScenarioDirector.cs` (P2 — CODE-STANDARDS §2)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` (line 191)

**Rule (CODE-STANDARDS.md §2):** `System.Numerics.Quaternion.CreateFromYawPitchRoll` is **banned in production code**. It uses a different coordinate convention (yaw around Y) than the FDP world (yaw around Z). Using it silently produces incorrect orientations.

```csharp
// Line 191 — BANNED
tf.Rotation = Quaternion.CreateFromYawPitchRoll(yawRadians, 0f, 0f);
```

**Fix:** Use `SimMath.FromYaw(yawRadians)` (in `Fdp.Kernel.SimMath`). For the APC heading north, `SimMath.FacingNorth` is directly available as a named constant.

```csharp
// Correct — general case:
tf.Rotation = SimMath.FromYaw(yawRadians);

// Correct — APC spawn (always facing north):
tf.Rotation = SimMath.FacingNorth;
```

Add `using Fdp.Kernel;` to `ScenarioDirector.cs` (it already imports it — `SimMath` is in `Fdp.Kernel`).

> **Effect of current bug:** APC is spawned facing wrong direction. The APC's forward vector is used by `CarKinematicsSystem` to move the vehicle. At `yawRadians = π/2`, `CreateFromYawPitchRoll` rotates around the Numerics Y axis (which is FDP's Z axis = up), producing a quaternion that rotates around +Z by +90°. `SimMath.FromYaw(π/2)` produces a quaternion for +Z rotation by π/2 = facing north (correct). These happen to be the **same quaternion** when the pitch and roll are both 0. However, for non-horizontal-only yaw calls where the convention differs in a meaningful way, the bug would bite. More critically, the **ban itself** must be enforced — the codereview rule is absolute regardless of whether this specific call happens to produce the right value. Fix it to use `SimMath.FromYaw`.

---

### Issue 2: `EjectPassengersActionId = 3` magic number in `TelemetryReporterSystem.cs` (P2 — CODE-STANDARDS §1)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs` (line 63)

```csharp
// Line 63 — magic number, no named constant exists in the toolkit
private const ushort EjectPassengersActionId = 3;
```

`EjectPassengersExecutor.cs` documents `"kind = 3"` in a comment but **no `InteractionConstants` class or named constant exists** that exports this value. The local private `const` hides the duplication rather than fixing it.

**Fix:** Add `ActionIdEjectPassengers = 3` to the **toolkit** (not the demo project), where the executor itself lives:

```csharp
// FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs — add:
/// <summary>
/// Interaction action ID for EjectPassengers.
/// Registered with <see cref="Systems.InteractionDispatcherSystem"/> by the host.
/// Must match the kind documented in <see cref="Executors.EjectPassengersExecutor"/>.
/// </summary>
public const ushort ActionIdEjectPassengers = 3;
```

Then in `TelemetryReporterSystem.cs` remove the private const and use:
```csharp
if (channel.ActiveAction == BehaviorConstants.ActionIdEjectPassengers)
```

Also update `EjectPassengersExecutor.cs` doc comment to reference the constant rather than the raw `3`.

---

## Confirmed Key Discoveries (Architectural)

### DEBT-007: Fully resolved? — **YES (partial)** for BTree nodes; **still deferred** for HSM action delegates

This is the most important question to address carefully.

**DEBT-007 was marked "Resolved" in BATCH-13**, where `HsmKernelBridge` was added so `FdpHsmContext` (the unmanaged HSM context struct) could carry `Entity Self` — satisfying the `unmanaged` constraint of `Fhsm.Kernel`. It was supposed to also thread `EntityRepository` into HSM actions.

**What BATCH-15 discovered (Q4 in that report):** The actual HSM action dispatch signature is `unsafe void Method(void* instance, void* context, HsmCommandWriter* writer)`. The `context` pointer points to an unmanaged struct. The `HsmKernelBridge` can carry `Entity Self` but **not** a managed `EntityRepository` reference (managed class — cannot go in unmanaged struct).

**BATCH-16 Q4 confirms:** `ApcHsmActions.Activity_Cruise` and `OnEnter_Disabled` are **still stubs**. The full ECS write (e.g. `LocomotionChannel.ActiveAction = NavigationConstants.ActionIdFollowRoute`) cannot be emitted from an HSM action delegate because `EntityRepository` cannot be passed through the raw `void* context` pointer. The comments themselves say:
> *"Full EntityRepository access for HSM actions is deferred to a future wiring step (DEBT-007 partial — context struct exists, kernel path not yet threaded)."*

**Conclusion:** DEBT-007 is **partially resolved** for BTree nodes (where `BTreeContext` is a managed struct passed via generics, so `EntityRepository World` is available). It is **not resolved** for HSM action delegates, where the kernel constraint prevents managed references. `ApcHsmActions` delegates are stubs and the HSM never drives the APC's channels. The HSM correctly transitions states (test T6 proves that) but produces no locomotion/interaction outputs.

**Recommendation:** Reopen DEBT-007 as `🟡 Partial` with a P2 note clarifying the remaining gap: *HSM action delegates cannot access ECS world due to `unmanaged` context constraint; `ApcHsmActions` are stubs*. The fix requires either: (a) an `HsmCommandWriter` API that posts deferred ECS mutations, or (b) a bridge system that reads the current HSM state and writes channels externally. Option (b) is simpler — `ApcBrainOutputSystem` in `HeadlessDemoApp` is already a stub for exactly this.

---

### Structural fixes from T9 debugging — verified correct

**Q3 Defect 1 — `WeaponDispatcherSystem` ordering:** `[UpdateAfter(typeof(BTreeTickSystem))]` added. Correct and necessary. The canonical order `ChannelArb → BTree → WeaponDispatcher` must be deterministic since `HashSet` iteration is not. ✅

**Q3 Defect 2 — `SpatialHashGrid` negative-coordinate blind spot:** `OriginX`/`OriginY` fields added. `Add()` and `QueryNeighbors()` subtract origin before dividing. `SpatialHashSystem.OnCreate()` uses `originX: -375f, originY: -375f`. Backward-compatible (`Create(...)` defaults remain 0). ✅  
> Minor follow-up: `SpatialHashSystem.cs` line 24–25 still uses literals `150`, `150`, `5.0f`, `-375f`, `-375f`. These should be named constants. Low urgency (P3 — add to debt tracker).

**Q3 Defect 3 — `TrafficBrainSystem` `DoctrineInstanceId` stamp:** `channel.DoctrineInstanceId = doctrine.InstanceId` stamped when `HasComponent<DoctrineState>`. The existing T4 unit tests still pass (they create entities without `DoctrineState`). ✅

**Q5 `ChannelArbitrationSystem` contract clarification:** The system never *sets* `DoctrineInstanceId` — it only uses it as a staleness guard. The brain (BTree/HSM/TrafficBrainSystem) is responsible for stamping. This is now explicitly documented in the report. ✅

---

## Verified Correct: All Features

- ✅ **Corrective-0a** — `BrainTierHsm` on APC; `APC_Template_HasHsmBrainTier` test passes.
- ✅ **Corrective-0b** — `UrbanCombatConstants.cs` complete; `SimTierCivilian/Tactical` in `BehaviorConstants`; all call sites swept; 4 test assertions updated.
- ✅ **BCS-P7-T7** — `ScenarioDirector`: 14 entities; 4 soldiers pre-embarked; insurgent TargetMemory and civilian TargetMemory seeded at setup; 4 tests pass.
- ✅ **BCS-P7-T8** — `TelemetryReporterSystem`: 7 event types; shadow dictionaries; `Console.Out.WriteLine` (not `Console.WriteLine`); 3 tests pass.
- ✅ **BCS-P7-T9** — Full 600-frame run; all 7 milestones appear in log; APC northward test passes. Three systematic defects discovered and fixed during T9 (see above).
- ✅ **ExportSystemGroup** added to `StandardSystemGroups.cs`.
- ✅ **HeadlessDemoApp** fully wired: 4 system groups, all 20 systems registered, doctrine registration, `RunSimulation` real loop.

---

## DEBT-007 Status Update

| Component | DEBT-007 Status | Evidence |
|---|---|---|
| BTree nodes (`InsurgentNodes`) | ✅ **Resolved** — `BTreeContext.World` available | Confirmed BATCH-15 Q3 |
| HSM action delegates (`ApcHsmActions`) | 🟡 **Still deferred** — delegates are stubs | BATCH-15/16 Q4; `ApcHsmActions.cs` comments |

**Root cause of remaining gap:** The `Fhsm.Kernel` dispatch signature `unsafe void(void* instance, void* context, HsmCommandWriter*)` requires the context to be unmanaged. `EntityRepository` is a managed class — it cannot occupy an unmanaged struct field. A bridge pattern is needed.

**Proposed resolution path (BATCH-17 or deferred):**
- **Option A (recommended):** Create `ApcBrainOutputSystem` — a `SimulationSystemGroup` system that queries all APC entities with `BrainHsm128`, reads the current state index, and writes the appropriate channel based on state (Cruising → `ActionIdFollowRoute`; Disabled → clear channel + `ActionIdEjectPassengers`). This decouples HSM state from channel writes entirely. The APC's `ApcHsmActions` stubs remain for forward-compatibility but the actual ECS mutations live in the external system. `ApcBrainOutputSystem.cs` already exists as a stub in the BATCH-16 file list.
- **Option B (heavyweight):** Extend `HsmCommandWriter` with an `EnqueueSetComponent<T>` capability (similar to `IEntityCommandBuffer`). Requires changes to `Fhsm.Kernel` internals.

---

## Verdict

**NEEDS FIX** — two P2 issues:
1. `Quaternion.CreateFromYawPitchRoll` (banned §2) → `SimMath.FromYaw` in `ScenarioDirector.cs`. One line.
2. `EjectPassengersActionId = 3` (§1 magic number) → `BehaviorConstants.ActionIdEjectPassengers`; add to `BehaviorConstants.cs`; update `TelemetryReporterSystem.cs`.

---

## 📝 Commit Message (approved content)

```
feat(BATCH-16): Phase 7 complete — ScenarioDirector + Telemetry + E2E integration

Corrective-0a: DemoTkbSetup APC BrainTier = BrainTierHsm + APC_Template_HasHsmBrainTier test
Corrective-0b: UrbanCombatConstants.cs; BehaviorConstants SimTierCivilian/Tactical;
  DemoTkbSetup full magic-number sweep; 4 BlueprintTest assertion updates

BCS-P7-T7 — ScenarioDirector
  14-entity spawn (5 ped, 3 car, 1 APC, 4 soldiers, 1 insurgent)
  EmbarkSoldiers: PassengerBuffer + IsEmbarkedTag + capability strip
  TargetMemory pre-seeded: insurgent→APC, civilian[0]→insurgent (for T9 milestones)
  BrainHsm128 pre-initialised on APC (StructureHash + CruisingStateIndex)
  +4 tests: entity count, embark count, red faction, APC passenger count

BCS-P7-T8 — TelemetryReporterSystem (ExportSystemGroup)
  7 milestones: DOCTRINE ASSIGNED, GUNFIRE, HIT, CAPABILITY LOST,
    HSM TRANSITION, INTERACTION: EjectPassengers, FLEE
  Shadow dicts: prevDoctrineInstanceId, prevHsmState, prevCapabilities
  Console.Out.WriteLine throughout (not Console.WriteLine) for StringWriter capture
  +3 tests: gunfire, hit, flee

BCS-P7-T9 — End-to-end (600 frames / 10 sec)
  All 7 milestones confirmed in run log; APC northward movement verified
  +2 tests

T9 defects fixed during integration:
  WeaponDispatcherSystem: [UpdateAfter(BTreeTickSystem)] — eliminates HashSet ordering race
  SpatialHashGrid: OriginX/OriginY fields; Add() and QueryNeighbors() subtract origin
  SpatialHashSystem.OnCreate: originX=-375f, originY=-375f (750×750m centred on world 0)
  TrafficBrainSystem: stamps channel.DoctrineInstanceId = doctrine.InstanceId

ExportSystemGroup added to StandardSystemGroups.cs
HeadlessDemoApp: full RegisterComponents/RegisterDoctrines/RegisterSystems/RunSimulation

Total new tests (BATCH-16): 9 + 9 previously passing = 26 green; 0 failures
DEBT-007: BTree path resolved; HSM action delegates still stubbed → see BATCH-17
```

---

**Next Batch:** BATCH-17 (if any remaining work) — ApcBrainOutputSystem + two P2 fixes + project wrap-up
