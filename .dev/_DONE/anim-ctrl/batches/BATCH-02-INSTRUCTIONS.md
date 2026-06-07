# BATCH-02: Phase 1 — FakeAnimationBackend (Deterministic Render-Free Backend)

**Batch Number:** BATCH-02  
**Tasks:** ANC-P1-01, ANC-P1-02, ANC-P1-03, ANC-P1-04, ANC-P1-05, ANC-P1-06, ANC-P1-07, ANC-P1-08, ANC-P1-09, ANC-P1-10  
**Phase:** Phase 1 — FakeAnimationBackend  
**Estimated Effort:** 3–4 days  
**Priority:** HIGH (blocks Phase 2–3, but can proceed once Phase 0 ships)  
**Dependencies:** BATCH-01 (Phase 0 foundations must be complete)

---

## 📋 Batch Goal

Implement the `FakeAnimationBackend` — a deterministic, render-free implementation of the `IAnimationBackend` interface whose entire per-entity state lives in a single Tier-1 component. This backend drives all animation logic without dependency on Stride or external rendering engines, making it ideal for testing and diagnostics.

**Outputs:**
- `FakeAnimationBackend` implementation class
- All backend operations (slot management, aim control, stance transitions, notify handling)
- Comprehensive Layer-1 unit test suite (~18 tests)
- Diagnostic ImGui window with list/detail inspection
- JSON snapshot export for AAR integration

**Once complete,** Layer-2 system tests (Phase 3) can validate the full Muscle ECS pipeline against the deterministic backend.

---

## 🚀 Developer Onboarding

### Required Reading (IN ORDER)
1. **Batch Instructions:** This file — goals and structure.
2. **Phase 0 Report:** `.dev/anim-ctrl/reports/BATCH-01-REPORT.md` — Understand the Phase 0 contracts you'll use.
3. **Mini Design:** `.dev/anim-ctrl/AnimationControl_BrainMuscle_MiniDesign_v0_3.md` — Architecture and action flow.
4. **Task Details:** `.dev/anim-ctrl/TASK-DETAIL.md` (Phase 1 section) — Exact specifications for all 10 tasks.
5. **Design Document:** `.dev/anim-ctrl/DD-Fake_FakeAnimationBackend_v1_1.md` — Fake backend state structure, algorithms, and diagnostics.
6. **Test Spec:** `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` (§3: Layer-1 test suite) — Expected test cases and fixtures.

### Source Code Location
**Primary implementation:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/` — Main backend class and state
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/Windows/` — Diagnostic window

**Test projects:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake.Tests/` (xUnit) — Layer-1 unit tests for all operations

**Integration points:**
- Uses `IAnimationBackend` interface from Phase 0
- Uses `FakeAnimBackendState` component (allocated ID=240)
- Uses `AnimNotifyCategory` enum for event kinds
- Registers diagnostic window with `IWindowRegistrar` on **`SimHostSubsystem`** (confirmed real host, see DEBT-TRACKER D-02)

### Debt-Tracker Items to Address
- **D-02:** Confirm diagnostic window registers to `SimHostSubsystem` (not non-existent `MuscleCharacterHostSubsystem`)
- **D-03:** Verify `SimHostSubsystem` implements `IWindowRegistrar` interface

### Report Submission
When complete, write findings to `.dev/anim-ctrl/reports/BATCH-02-REPORT.md` including:
- **Completed tasks:** Status of all 10 tasks
- **Test results:** Layer-1 test count + pass rate + runtime
- **Code metrics:** LOC per task
- **Diagnostic window:** Verification of registration + manual inspection walkthrough
- **Blockers:** Any issues preventing Phase 2 compilation or integration
- **Debt items:** Record any issues (D-02/D-03 status, new findings)

If you need clarification, create `.dev/anim-ctrl/questions/BATCH-02-QUESTIONS.md`.

---

## 📝 Task Breakdown & Success Criteria

### ANC-P1-01 — `FakeAnimBackendState` component + sub-structs
**File:** `Hrot.MuscleCharacter.Animation.Fake/Components/FakeAnimBackendState.cs`  
**Reference:** DD-Fake §2 (§2.1–2.4)

**Define the unmanaged Tier-1 component:**
```csharp
[ComponentId(240)]
[DataPolicy(NoSave)]
public struct FakeAnimBackendState : IComponentData
{
    // Per §2.1–2.4: Handle table, slot buffer, aim state, stance state, notify buffer, etc.
}
```

**Sub-structures (all unmanaged):**
- `FakeSlotState` — Montage ID, playback position, blend weight, active flag
- `FakeAimState` — Target position/entity, blend weight, is-entity flag
- `FakeStanceState` — Current stance ID, target stance, transition progress
- `[InlineArray(8)]` `FakeSlotsBuffer` — Slot table
- `[InlineArray(16)]` `FakePendingNotifyBuffer` — Pending notify events
- `ulong FiredNotifyMask` — Track which keyframes already fired per slot

**Layout:** ~1 KB total (deterministic, fixed-size), <64 KB.

**Success criteria:**
- Component compiles with `[ComponentId(240)]` and `[DataPolicy(NoSave)]`
- Size test: ≈1 KB, deterministic layout
- Sub-structs are `unmanaged` (no managed references)

---

### ANC-P1-02 — Backend scaffold: `Initialize`, handle table, Register/Unregister
**File:** `Hrot.MuscleCharacter.Animation.Fake/FakeAnimationBackend.cs`  
**Reference:** DD-Fake §3, §3.1, §3.2

**Implement the backend class:**
```csharp
public class FakeAnimationBackend : IAnimationBackend
{
    // Generation-safe handle slots (§3.1)
    // Initialize() — one-time setup (idempotent component registration, etc.)
    // RegisterEntity() — allocates a generation-safe handle for an entity
    // UnregisterEntity() — frees the handle slot
    // TryResolve() — validates handle generation, returns state by ref if valid
}
```

**Key behaviors:**
- **Idempotent registration:** Can call `Initialize()` multiple times safely
- **Generation counting:** Handles are `(uint slotIndex, uint generation)` to catch use-after-free
- **Initial stance:** On register, set `CurrentStance = def.SupportedStances[0]`

**Success criteria:**
- Unit test: `RegisterEntity` returns valid handle
- Unit test: `TryResolve` with stale handle (after unregister) returns `false`
- Unit test: Re-registering same entity bumps generation

---

### ANC-P1-03 — Slot operations
**File:** `Hrot.MuscleCharacter.Animation.Fake/Operations/SlotOperations.cs`  
**Reference:** DD-Fake §3.3

**Implement per DD-Fake §3.3:**
- `PlayMontageOnSlot(handle, slotId, montageId, ...)` — Writes active montage; resets blend weight, elapsed time, notify mask
- `CrossfadeMontageOnSlot(...)` — Equivalent to Play (no separate blending logic in fake backend)
- `StopMontageOnSlot(handle, slotId, blendOutDuration)` — Forces blend-out phase
- `QuerySlotState(handle, slotId, out FakeSlotState)` — Reads current slot

**Use Span-cast mutation throughout** (convert `fixed byte[]` to `Span<T>` for in-place updates, per DD-Fake §2.3).

**Success criteria:**
- Unit test: `PlayMontage_SetsSlotActive` — slot marked active after play
- Unit test: `PlayMontage_OverwritesPreviousMontageInSameSlot` — existing slot content cleared
- Unit test: `PlayMontage_UnknownMontage_NoOps` — invalid montage ID silently does nothing (no crash)
- All pass per DD-Tests §3.2

---

### ANC-P1-04 — Locomotion / aim / stance operations
**File:** `Hrot.MuscleCharacter.Animation.Fake/Operations/AimAndStanceOperations.cs`  
**Reference:** DD-Fake §3.4

**Implement:**
- `UpdateLocomotionInputs(handle, velocity, isAirborne)` — Stores locomotion state for footstep cadence
- `SetAimTarget(handle, point/entity, blendInWeight)` — First-acquire behavior: sets aim state, begins blend-in
- `ReleaseAim(handle, blendOutDuration)` — Stages blend-out
- `RequestStanceChange(handle, targetStanceId, blendDuration)` — Initiates stance transition
- `QueryStanceTransition(handle, out StanceTransitionState)` — Reads transition progress

**Aim first-acquire:** When transitioning from inactive→active, snap blend weight to blend-in weight (DD-Fake §3.4).

**Success criteria:**
- Unit test: `SetAimTarget_ActivatesAimWithBlendInWeight` — aim marked active, blend set correctly
- Unit test: `RequestStanceChange_StartsTransition` — stance marked transitioning, progress reset
- Per DD-Tests §3.2 aim/stance test cases

---

### ANC-P1-05 — Notify drain + hard-assert + metrics
**File:** `Hrot.MuscleCharacter.Animation.Fake/Operations/NotifyHandling.cs`  
**Reference:** DD-Fake §3.5, §4.4, §3.6, §6

**Implement:**
- `DrainNotifies(Span<RawNotifyEvent> dest)` — Copies pending notifies to dest; shifts remainder if smaller than pending; returns count copied
- `EmitNotify(event)` — Backend-internal method to add to pending buffer; throws `InvalidOperationException` on 17th (capacity guard)
- `SnapshotMetrics(out AnimationBackendMetrics)` — Returns counters (e.g., total ticks, total notifies emitted)

**Behavior:**
- If `dest.Length < PendingNotifyCount`, copy only what fits; remainder stays buffered for next drain
- Overflow (17th notify) is fatal: throw `InvalidOperationException` with diagnostic info

**Success criteria:**
- Unit test: `DrainNotifies_TransfersAllPendingToDest` — full buffer copies correctly
- Unit test: `DrainNotifies_HandlesSmallerDestBuffer` — overflow handled; remainder preserved
- Unit test: `EmitNotify_OverflowThrowsInvalidOperationException` — 17th throws
- Per DD-Tests §3.2

---

### ANC-P1-06 — Tick algorithm (slot/aim/stance advance)
**File:** `Hrot.MuscleCharacter.Animation.Fake/Tick/TickAlgorithm.cs`  
**Reference:** DD-Fake §4, §4.1–4.3

**Implement per §4.1–4.3:**

**Slot advance logic:**
- Increment `ElapsedSeconds` by `deltaTime * PlayRate`
- Check notify keyframes: `if ElapsedSeconds crosses a keyframe, set bit in FiredNotifyMask and call EmitNotify`
- **Notify fires exactly once per play:** Mask prevents re-fire (PP-Tests: `Notify_FiresExactlyOncePerPlay`)
- Detect natural end: `if ElapsedSeconds >= MontageLength, mark inactive`
- Update blend weight: Linear ramp from `BlendInWeight` to 1.0 over blend-in duration

**Aim advance logic:**
- Ramp `BlendWeight` toward target (1.0 for active, 0.0 for releasing)
- Set inactive when blend fully released

**Stance advance logic:**
- Increment transition progress linearly
- Detect completion: `if Progress >= TransitionDuration, mark complete`

**Execution:**
- Call `AdvanceSlot` per active slot
- Call `AdvanceAim` if aim active or releasing
- Call `AdvanceStance` if transitioning

**Success criteria:**
- Unit test: `Tick_AdvancesElapsedTimeByDeltaTimesPlayRate` — time advances correctly
- Unit test: `Tick_DeactivatesSlotOnNaturalCompletion` — slot auto-deactivates
- Unit test: `Tick_FiresNotifyWhenElapsedCrossesTimeSeconds` — notify emits at keyframe
- Unit test: `Notify_FiresExactlyOncePerPlay` — mask prevents double-fire
- Unit test: `PlayMontage_ResetsFiredNotifyMask` — new play resets mask
- Unit test: `Tick_RampsAimBlendWeight` — blend progresses
- Unit test: `Tick_CompletesStanceTransition` — transition finishes
- All per DD-Tests §3.2

---

### ANC-P1-07 — Synthetic footstep emission
**File:** `Hrot.MuscleCharacter.Animation.Fake/Tick/FootstepEmission.cs`  
**Reference:** DD-Fake §5

**Implement footstep cadence:**
- `FakeBackendConstants`:
  - `MinFootstepSpeed = 0.3` m/s (threshold below which no footsteps)
  - `FootstepStrideMeters = 0.9` m per step
- `AdvanceFootsteps(deltaTime, velocity)` — Track cumulative distance; emit on stride boundary
- Alternate feet (L, R, L, R, ...)
- **Payload:** Left at zero; enriched downstream by `NotifyEventEmitterSystem` with world position

**Behavior:**
- No emission when stationary (`speed < MinFootstepSpeed`)
- No emission when airborne (`IsAirborne=true`)

**Success criteria:**
- Unit test: `Footstep_EmitsAtStrideDistance` — emits every 0.9 m
- Unit test: `Footstep_AlternatesFeet` — L/R/L/R pattern
- Unit test: `Footstep_NoEmissionWhenStill` — below speed threshold
- Unit test: `Footstep_NoEmissionWhenAirborne` — airborne flag suppresses
- Per DD-Tests §3.2

---

### ANC-P1-08 — Layer-1 unit test suite
**File:** `Hrot.MuscleCharacter.Animation.Fake.Tests/FakeBackendOperationsTests.cs`  
**Reference:** DD-Tests §3 (§3.1–3.3)

**Create comprehensive test fixture:**
```csharp
public class FakeBackendOperationsFixture : IDisposable
{
    // Per DD-Tests §3.1: Bootstrap fake backend + minimal test data
    // Exposes backend, component registry, entity manager for tests
}
```

**Test cases (~18 total):**
- All tests from ANC-P1-02 through ANC-P1-07 above
- Additional edge cases: invalid handles, out-of-bounds slot IDs, zero deltaTime, etc.

**Execution & Reporting:**
- xUnit framework
- Each test independently runnable
- On failure, dump `FakeAnimBackendState` as JSON for diagnostics
- Runtime <0.5 s total

**Success criteria:**
- All ~18 tests pass
- <0.5 s total runtime
- Coverage: all public backend methods + all tick algorithms

---

### ANC-P1-09 — Diagnostic ImGui window
**File:** `Hrot.MuscleCharacter.Animation.Fake/Windows/FakeAnimBackendInspectorWindow.cs`  
**Reference:** DD-Fake §7 (§7.1–7.3), DEBT-TRACKER D-02/D-03

**Implement ImGui-based diagnostic window:**
```csharp
public class FakeAnimBackendInspectorWindow : IDiagnosticWindow
{
    // List view: entity id, backend handle, active slots, aim active, stance, metrics
    // Detail view: per-entity deep dive (slot details, aim state, notify buffer, etc.)
    // Metrics: tick count, notify count, footstep count, etc.
}
```

**Registration:**
- Register via `IWindowRegistrar.Register()` on **`SimHostSubsystem`** (per BATCH-01 verification)
- Headless-safe: do not attempt to register window when running headless
- Label: "Animation Backend Inspector" (or similar)

**Features (from DD-Fake §7.1–7.3):**
- List all tracked entities with backend handles
- Select an entity to show detail view
- Display slot table (active slots, montage IDs, blend weights, elapsed time)
- Display aim state (target, blend weight, active flag)
- Display stance (current, target, transition progress)
- Display pending notify buffer (upcoming/fired)
- Display metrics snapshot

**Success criteria:**
- Window compiles + registers to `SimHostSubsystem`
- Unit test (style: `Hrot.Editor.AiShared.Tests/Windows`):
  - In non-headless: registration succeeds
  - In headless: registration is skipped / never attempted
- Manual inspection: launch diagnostics window, verify entity list + deep dive work (manual UI verification is acceptable)

---

### ANC-P1-10 — JSON snapshot export + AAR integration
**File:** `Hrot.MuscleCharacter.Animation.Fake/Diagnostics/FakeAnimBackendSnapshotJson.cs`  
**Reference:** DD-Fake §8, §9

**Implement snapshot serializer:**
```csharp
public static class FakeAnimBackendSnapshotJson
{
    public static string Serialize(FakeAnimBackendState state, /* name-resolution context */)
    {
        // Outputs JSON with:
        // - Slots array: {id, montageId, montageIdResolved (name), elapsedSeconds, blendWeight, ...}
        // - Aim: {targetPoint, blendWeight, isActive, ...}
        // - Stance: {current, target, transitionProgress, ...}
        // - Metrics: {tickCount, totalNotifies, ...}
        // - NotifyBuffer: [{kind, markerHash, markerHashResolved (name), timeSeconds, ...}]
    }
}
```

**Name resolution:**
- Map `MontageAssetId` → montage name (use stable-hash from P2-02 or test data)
- Map `MarkerHash` → marker name (FNV1a32 hash → name)
- Use a provided context / fake test-data registry

**Integration:**
- "Copy JSON" button in diagnostic window (ImGui) — copies to clipboard
- Leverages Tier-1 recorder fast-path (`AAR` = Tier-1 snapshot + metadata)

**Success criteria:**
- Serializer test: known state → JSON with expected slot/aim/stance/notify fields
- Names resolve correctly (at least one known montage/marker pair)
- Output JSON is valid and parseable

---

## ✅ Acceptance Criteria for Batch-02

1. **All 10 tasks completed:**
   - Fake backend fully functional
   - All component definitions compile
   - All operations implemented per spec

2. **Layer-1 unit test suite:**
   - ~18 tests pass
   - <0.5 s total runtime
   - All test cases from DD-Tests §3 covered

3. **Diagnostic window:**
   - Registers to `SimHostSubsystem` (DEBT-TRACKER D-02 verified)
   - List/detail inspection works
   - Headless-safe

4. **JSON export:**
   - Serializer produces valid JSON
   - Name resolution works
   - Integrates with diagnostic window

5. **No blockers for Phase 2–3:**
   - All phase dependencies (`IAnimationBackend` + supporting types from P0) verified
   - Ready for TKB descriptor → ECS injection (Phase 2)
   - Ready for Layer-2 system tests (Phase 3)

6. **Batch report submitted:**
   - `.dev/anim-ctrl/reports/BATCH-02-REPORT.md`
   - Task-by-task summary
   - Test results + metrics
   - Debt-tracker verification (D-02/D-03)
   - Any new blockers logged

---

## 🔗 Next Batch

Once BATCH-02 is approved, the next batch will cover **Phase 2 — TKB Animation Descriptor** (ANC-P2-01 through ANC-P2-08), implementing the design-time JSON → runtime component injection pipeline and editor query API.
