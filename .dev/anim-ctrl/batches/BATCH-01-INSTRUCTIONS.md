# BATCH-01: Phase 0 Foundations & Shared Contracts

**Batch Number:** BATCH-01  
**Tasks:** ANC-P0-01, ANC-P0-02, ANC-P0-03, ANC-P0-04, ANC-P0-05, ANC-P0-06, ANC-P0-07, ANC-P0-08  
**Phase:** Phase 0 — Foundations & shared contracts  
**Estimated Effort:** 2–3 days  
**Priority:** CRITICAL (blocks all downstream phases)  
**Dependencies:** None (greenfield)

---

## 📋 Batch Goal

Land all foundational types, enums, and interfaces that downstream phases depend on:
- Animation capability bits and enums
- Component IDs and layout definitions
- Channel parameter/state structs
- ECS component definitions (replicated and internal)
- The `IAnimationBackend` interface contract
- Verification of design assumptions vs. real codebase

**Once complete,** all downstream phases (P1–P7) will compile against stable contracts.

---

## 🚀 Developer Onboarding

### Required Reading (IN ORDER)
1. **Batch Instructions:** This file — what you're doing and why.
2. **Mini Design:** `.dev/anim-ctrl/AnimationControl_BrainMuscle_MiniDesign_v0_3.md` — Architecture bird's-eye view, channel shapes, action IDs.
3. **Task Details:** `.dev/anim-ctrl/TASK-DETAIL.md` (Phase 0 section) — Exact definitions, success criteria per task.
4. **Design Documents:**
   - `DD-1_MuscleCharacterRuntime_v1_2.md` — `IAnimationBackend`, ECS systems, capability gating.
   - `DD-Fake_FakeAnimationBackend_v1_1.md` — Fake backend state structure.
   - `DD-3_EventCatalog_AnimationNotify_v1_3.md` — `AnimNotifyCategory` enum.
   - `DD-4_TKB_AnimationDescriptor_v1_2.md` — Component ID block and descriptor shape.
5. **Codebase References:** Section "Codebase grounding" in [TASK-DETAIL.md](./TASK-DETAIL.md) — verified existing types you build on top of.

### Source Code Location
**New types go into:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/` — Main implementation (create if needed)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Contracts/` — Shared contracts (`IAnimationBackend`, enums, etc.)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Components/` — ECS component definitions
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/` — Extension of `ActorCapabilities` (P0-02)

**Test projects:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/` (xUnit) — Unit tests for layouts, serialization, etc.
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/` — Fake backend impl (Phase 1)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake.Tests/` (xUnit) — Fake backend tests (Phase 1)

### Report Submission
When complete, write your findings to `.dev/anim-ctrl/reports/BATCH-01-REPORT.md` including:
- **Completed tasks:** Summary of what was implemented.
- **Codebase findings:** Output from ANC-P0-08 verification spike (confirmed real types, mismatches logged).
- **Test results:** All Phase 0 tests passing + line count.
- **Blockers / Questions:** Any issues preventing downstream compilation or integration.

If you hit blockers during development, create `.dev/anim-ctrl/questions/BATCH-01-QUESTIONS.md`.

---

## 📝 Task Breakdown

### ANC-P0-01 — `AnimNotifyCategory` canonical enum
Define the `byte` enum in `Hrot.MuscleCharacter.Animation.Contracts`:
```csharp
[Serializable]
public enum AnimNotifyCategory : byte
{
    Generic = 0,
    Footstep = 1,
    HitWindowOpened = 2,
    HitWindowClosed = 3,
}
```

**Success criteria (from TASK-DETAIL.md):**
- Enum compiles and fits in one byte.
- Unit test asserts all four values match DD-3 §2.
- Both `RawNotifyEvent.Kind` and `NotifyMarkerDefDto.Kind` reference this single enum (no duplicate local `NotifyKind`/`NotifyMarkerKind` compiles).

---

### ANC-P0-02 — `ActorCapabilities` animation bits
Add three new flags to the existing `ActorCapabilities` `[Flags]` enum in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`:
```csharp
CanPlayAnimations = 8,
CanChangeStance = 16,
CanAim = 32,
```

**Important:** Do **not** renumber existing bits (`CanMove=1`, `CanShoot=2`, `CanInteract=4`).

**Success criteria:**
- Enum still fits in a byte (`ActorCapabilities` is a `[Flags] byte` enum).
- Unit test verifies: existing bits are unchanged, new bits have values 8/16/32.
- Reference test (from the codebase) `ActorCapabilities_CanMove_Is_Bit0` still passes.

---

### ANC-P0-03 — `GlobalComponentIds` allocations (220–249)
Reserve a block of IDs in `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` for animation components. Specifically:
- Allocate IDs for all animation components introduced in P0-05, P0-06, and `FakeAnimBackendState` (=240).
- Document the 220–249 block boundary (e.g., `// Animation components: 220–249`).
- One ID per component type.

**Example structure** (exact IDs from TASK-DETAIL.md or component definitions):
```csharp
// Animation components: 220–249
AnimationChannel = 220,
LookAtChannel = 221,
StanceIntent = 222,
// ... other replicated components
CharacterAnimationDefRuntime = 238,
AnimationExecutorState = 239,
FakeAnimBackendState = 240,
```

**Success criteria:**
- `GlobalComponentIds` compiles without conflicts.
- Unit test: no duplicate IDs in 220–249 range.
- Unit test: `FakeAnimBackendState == 240`.

---

### ANC-P0-04 — Channel param/state structs + action-id constants
Define unmanaged parameter structs and action ID constants in `Hrot.MuscleCharacter.Animation.Contracts`:

**Parameter structs (all `unmanaged`, max 32 bytes each):**
- `PlayMontageParams` — Includes `MontageAssetId`, slot index, playback rate, etc.
- `StopMontageParams` — Slot index, blend-out duration.
- `PlayMontageQueueParams` — Montage ID, playback rate.
- `LookAtPointParams` — Point target, blend-in weight, etc.
- `LookAtEntityParams` — Entity reference, blend-in weight.
- `ReleaseLookParams` — Blend-out duration.

**Action ID constants** in `AnimationActionIds` and `LookAtActionIds`:
- `PlayMontage = 1`
- `StopMontage = 2`
- `PlayMontageChain = 3`
- `PlayMontageQueueAppend = 4`
- `ClearMontageQueue = 5` (if included; verify DD-1 §6.4)
- `SetLookAtPoint = 10`
- `SetLookAtEntity = 11`
- `ReleaseLookAt = 12`

**Success criteria:**
- All params structs `sizeof <= 32` (test with `Assert.True(sizeof(PlayMontageParams) <= 32)`).
- Round-trip test: write params to `fixed byte[32]` blob, read back, verify no corruption.
- `AnimationActionIds` constants resolve to their expected values (test at least one).

---

### ANC-P0-05 — Replicated/contractual components
Define replicated ECS components in `Hrot.MuscleCharacter.Animation.Components`:

**Components (all `[ComponentId]` from P0-03 block, `[DataPolicy(NoSave)]`):**
- `AnimationChannel` — Replicates animation playback intent.
- `LookAtChannel` — Replicates look-at target.
- `StanceIntent` — Desired stance.
- `StanceStatus` — Current stance + transition phase.
- `StanceTransitionPhase` enum — `Idle`, `Transitioning`, `Locked`.
- `StanceId` enum — Supported stances (from DD-4 §3.2).
- `AnimationMontageQueue` — Queued montages, `[InlineArray(8)]`, with entry count and version.
- `MontageQueueEntry` — Montage ID + playback params.
- `AnimationMontageQueueState` — Mirror of queue for replicated state synchronization.

**Layout from DD-1 §5.1, mini §3.3:**
- `AnimationChannel` ≤ 96 B (3 × `ushort`/`uint`/fields + 32 B params + 32 B state).
- `AnimationMontageQueue` ≤ 140 B (8 entries @ ~16 B + metadata).
- Stance components ≤ 16 B.

**Success criteria:**
- All layout tests pass (use `Assert.True(sizeof(AnimationChannel) <= 96)`).
- `[InlineArray]` mutation via Span-cast: write test that modifies a queue entry through a Span.
- Components compile with correct `[ComponentId]` attributes.

---

### ANC-P0-06 — Muscle-internal components
Define internal (non-replicated) ECS components in `Hrot.MuscleCharacter.Animation.Components`:

- `CharacterAnimationDefRuntime` — Handle into per-class baked data + `BackendHandle`, `StanceCount`, `SlotCount`.
- `AnimationExecutorState` — `[InlineArray]` slot table with `MaxSlots = 8`.
- `LookAtExecutorState` — Look-at runtime state.

**Important:** These are **not** replicated. Do **not** add `[NetworkingPolicy]` or register with any egress.

**Success criteria:**
- Compile + layout test: `MaxSlots == 8` and component total size reasonable.
- Component IDs assigned from P0-03 block.
- Not registered with any replication egress.

---

### ANC-P0-07 — `IAnimationBackend` interface + supporting types
Define the animation backend contract in `Hrot.MuscleCharacter.Animation.Contracts`:

**Core interface:**
```csharp
public interface IAnimationBackend
{
    AnimationBackendHandle RegisterEntity(/* params */);
    void UnregisterEntity(AnimationBackendHandle handle);
    bool TryResolve(AnimationBackendHandle handle, out /* state */);
    void PlayMontageOnSlot(/* params */);
    void StopMontageOnSlot(/* params */);
    void SetAimTarget(/* params */);
    void ReleaseAim(/* params */);
    void RequestStanceChange(/* params */);
    void Tick(/* params */);
    void DrainNotifies(/* params */);
    void SnapshotMetrics(/* output */);
}
```

**Supporting types** (all in `Hrot.MuscleCharacter.Animation.Contracts`):
- `AnimationBackendHandle` — Generation-counted entity handle.
- `SlotId` — Slot identifier (index 0–7).
- `MontageAssetId` — Stable ID from asset name hash (int).
- `MontagePlaybackState` — `Active`, `Blending`, `Inactive`.
- `StanceTransitionState` — Transition progress, source/target stances.
- `RawNotifyEvent` — Event with `Kind` = `AnimNotifyCategory`, marker hash, time.
- `AnimationBackendConfig` — Configuration for backend initialization.
- `AnimationBackendMetrics` — Performance counters.

**Constraint:** No Stride/3D-engine-specific types leak (DD-1 §16). Use only math types that are engine-agnostic.

**Success criteria:**
- Interface compiles in `Hrot.MuscleCharacter.Animation` without external dependencies.
- Mock implementation in a test satisfies all interface members.
- No reference to any Stride-specific type (e.g., `Entity`, `Stride.Core.Vector3`).

---

### ANC-P0-08 — Verification spike (dependency re-checks)
**This task is a research + documentation spike.** Review the "Codebase grounding ⚠" section in [TASK-DETAIL.md](./TASK-DETAIL.md) and verify:

1. **Real `DispatcherSystemBase<TChannel>` signature:**
   - Locate `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DispatcherSystemBase.cs`.
   - Confirm generic type parameter and base hooks (`OnInitialize`, `OnTick`, etc.).
   - Document findings in your report.

2. **Channel replication mechanism:**
   - DD-2 assumes "existing channel intent/status translator precedent."
   - `WeaponChannelTranslator` was deleted in `cgf-scn-2`.
   - Confirm the current channel-replication mechanism: is it SmartEgress? Descriptor egress?
   - Document what currently replicates `LocomotionChannel` / `WeaponChannel` / `InteractionChannel`.

3. **Diagnostic window host subsystem:**
   - DD-Fake §7.3 names `MuscleCharacterHostSubsystem`.
   - Actual host implementing `IWindowRegistrar` is `SimHostSubsystem`.
   - Confirm which subsystem should register the animation diagnostic window (P1-09).
   - Document the real class name.

4. **Component field naming:**
   - DD code uses `channel.ActionParams` / `ActionState`.
   - Real fields in `LocomotionChannel` are `Params` / `State`.
   - Confirm this applies to all channel types; update task descriptions if needed.

**Deliverable:**
- Append findings to your batch report (ANC-P0-08 section).
- For each confirmed mismatch, log an entry to [DEBT-TRACKER.md](./DEBT-TRACKER.md) with source, description, and target batch.
- DD-2 and DD-Fake tasks will reference confirmed real symbols (not design doc names).

**Success criteria:**
- All four points above investigated and documented.
- Confirmed items logged to DEBT-TRACKER (if any).
- No compilation errors in Phase 1 due to incorrect symbol names.

---

## ✅ Acceptance Criteria for Batch-01

1. **All 8 tasks completed:**
   - Enums, bits, IDs, structs, interfaces all compile.
   - Components registered with correct IDs (220–249).
   - Layout/round-trip tests pass.

2. **Unit tests:**
   - All Phase 0 tests pass (expect ~12–15 test cases).
   - <0.5 s total runtime.

3. **No downstream blockers:**
   - Phase 1+ code can compile against these types.
   - No missing or misnamed symbols.

4. **Design verification (ANC-P0-08):**
   - Codebase grounding findings documented.
   - Any mismatches logged to DEBT-TRACKER.

5. **Batch report submitted:**
   - `.dev/anim-ctrl/reports/BATCH-01-REPORT.md` includes task summary, test results, and findings.

---

## 🔗 Next Batch
Once this batch is approved, the next batch will cover **Phase 1 — FakeAnimationBackend** (ANC-P1-01 through ANC-P1-10), implementing a deterministic render-free backend with unit tests and diagnostics.
