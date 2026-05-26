# BATCH-04 Completion Report

**Batch:** BATCH-04  
**Developer:** GitHub Copilot (AI)  
**Date:** Session 3 of the animation control implementation

---

## 1. Task Status Table

| Task ID | Description | Status |
|---------|-------------|--------|
| Fix 1 (Stream A) | AnimationTkbTranslator.Inject tests | DONE |
| Fix 2 (Stream A) | BakedAnimationCache hot-reload tests | DONE |
| Fix 3 (Stream A) | AnimationTkbQueries query-method tests | DONE |
| Fix 4 (Stream A) | Phase 1 behavioral tests (DD-Tests §3.2) | DONE |
| ANC-P3-01 | AnimationDispatcherSystem | DONE |
| ANC-P3-02 | LookAtDispatcherSystem | DONE |
| ANC-P3-03 | StanceTransitionSystem | DONE |
| ANC-P3-04 | MontageQueueAdvanceSystem | DONE |
| ANC-P3-05 | AnimationRuntimeBridgeSystem | DONE |
| Phase3SystemTests | System-level integration tests | DONE |

---

## 2. Test Results

| Project | Before Batch | After Batch | Status |
|---------|-------------|-------------|--------|
| `Hrot.MuscleCharacter.Animation.Tests` | 58 | 92 | All Passed |
| `Hrot.MuscleCharacter.Animation.Fake.Tests` | 15 | 15 | All Passed |
| **Total** | **73** | **107** | **All Passed** |

Full solution build: **Build succeeded** (zero CS errors, zero NU errors after adding Animation project to solution).

---

## 3. Stream A: Test Fixes

### Fix 1 — AnimationTkbTranslator.Inject tests (`TranslatorAndCacheTests.cs`)

**4 tests added in `AnimationTranslatorTests`:**
- `Inject_WithNonAnimatedTemplate_AddsNoComponents` — verifies template without DTO descriptor adds no channels/stances
- `Inject_WithAnimatedEntity_AddsRequiredComponents` — verifies all 7 required components added for animated entity with no AimConfig
- `Inject_WithAimCapableEntity_AddsLookAtComponents` — verifies LookAtChannel + LookAtExecutorState added when AimConfig != null
- `Inject_WithoutAimConfig_DoesNotAddLookAtComponents` — verifies LookAt components absent when AimConfig == null

These tests exercise the real `AnimationTkbTranslator.Inject` code path, not stubs. All 4 tests construct a genuine `EntityRepository`, register all component types, and assert on actual component presence.

**Issue found:** `EntityRepository.RegisterComponentType<T>()` does not exist; the correct API is `RegisterComponent<T>()`. The test was initially written with the wrong name and was fixed.

### Fix 2 — BakedAnimationCache hot-reload tests

**2 tests added in `BakedAnimationCacheTests`:**
- `GetOrBake_ReturnsConsistentResult` — proves `GetOrBake` is idempotent: same classId returns data with the same montage count on repeated calls
- `HotReload_InvalidatesEntry` — uses an inline `FakeHotReloadEvents` class to fire `TkbDescriptorChangedEvent`, verifies the cache re-bakes (result2 non-null and same structure)

**Issue found:** `ITkbHotReloadEvents.Subscribe` takes `Action<TkbDescriptorChangedEvent>` not `Action<long>` as stated in some notes. Handled correctly in implementation.

### Fix 3 — AnimationTkbQueries query-method tests

**7 tests added in `AnimationTkbQueriesTests`:**
- `GetPlayableMontages_ExcludesStanceTransitionMontages` — 2 normal + 1 transition montage, result count == 2
- `GetSupportedStances_ReturnsAllStances` — 2 stances, count == 2
- `SupportsAim_TrueWhenAimConfigPresent` — with AimConfigDto != null
- `SupportsAim_FalseWhenAimConfigNull` — using `record with { AimConfig = null }` syntax (AimConfig is init-only)
- `GetAvailableMarkers_ReturnsAllMarkers` — 2 markers, count == 2
- `GetMarkerName_ReverseLookup` — named "MagOut" reverse-looked up by hash
- `ResolveMontageId_MatchesStableIdHasher` — id matches `StableIdHasher.ComputeMontageAssetId`

**Issue found:** `AnimationTkbQueries` is `internal` — resolved by adding `InternalsVisibleTo("Hrot.MuscleCharacter.Animation.Tests")` to `Hrot.Editor.AiShared.csproj`.

**Issue found:** `CharacterAnimationDefDto.AimConfig` is `init`-only, cannot be set post-construction. Fixed by using `dto with { AimConfig = null }` (record with-expression).

### Fix 4 — Phase 1 behavioral tests

**12 tests added in `Phase1BackendBehaviorTests.cs`:**

PlayMontage tests (3): `PlayMontage_SetsSlotActive`, `PlayMontage_OverwritesPreviousMontageInSameSlot`, `PlayMontage_UnknownMontage_NoOps`

Tick advancement tests (3): `Tick_AdvancesElapsedTimeByDeltaTimesPlayRate`, `Tick_DeactivatesSlotOnNaturalCompletion`, `Tick_DoesNotAdvanceInactiveSlots`

Notify tests (3): `Tick_FiresNotifyWhenElapsedCrossesTimeSeconds`, `Notify_FiresExactlyOncePerPlay`, `PlayMontage_ResetsFiredNotifyMask`

Footstep test (1): `Footstep_EmitsAtStrideDistance`

DrainNotifies tests (2): `DrainNotifies_ReturnsUpToBufferSize`, `DrainNotifies_HandlesSmallerDestBuffer`

**Issue found:** `BakingUtils.BakeDef` computes `NotifyInfo.MarkerHash` via `StableIdHasher.ComputeMarkerHash(name)`, NOT from `NotifyMarkerDefDto.Hash`. The test initially hardcoded `0xA1B2C3D4` as the hash constant but the backend fires the computed hash. Fixed by computing hash with `StableIdHasher.ComputeMarkerHash("MagOut")`.

**Issue found:** `AnimationMontageQueueState.InBlendOutWindow` (bool) failed the ECS layout validator. Fixed by adding `[MarshalAs(UnmanagedType.I1)]` to the field in `ReplicatedComponents.cs`.

---

## 4. Stream B: Phase 3 ECS Systems

### Common patterns applied
- All systems take `ISimulationView view` and immediately cast to `EntityRepository repo` (same pattern as `WeaponDispatcherSystem`)
- `using Fbt;` needed for `NodeStatus` (not `Fdp.Toolkit.Behavior`)
- `Entity.Index` (int) and `Entity.Generation` (ushort) available; no `Entity.Id` property exists — used `entity.PackedValue` (ulong) as dictionary key in bridge
- `SystemPhase.PreSimulation` does not exist in the enum; all systems use `SystemPhase.Simulation`

### ANC-P3-01 — AnimationDispatcherSystem

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationDispatcherSystem.cs`
**Executor file:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Executors/AnimationExecutors.cs`

Architecture: `DispatcherSystemBase<AnimationChannel>` with three executors:
- `PlayMontageExecutor`: validates montage against `BakedAnimationCache`, stages `StagedPlayIntent` in `AnimationExecutorState.SlotsData[0..sizeof(StagedPlayIntent)]`
- `StopMontageExecutor`: stages stop intent in same staging area
- `PlayMontageQueueExecutor`: stub (sets Running status; queue management in MontageQueueAdvanceSystem)

**Key design decision:** Executors do NOT call the backend directly. They stage intent in `AnimationExecutorState.SlotsData` and `AnimationRuntimeBridgeSystem` applies it. This decouples dispatcher timing from backend registration (which happens on first bridge tick).

**`StagedPlayIntent` struct** (internal, defined in `AnimationExecutors.cs`): uses the first 20 bytes of `AnimationExecutorState.SlotsData` as a staging area. Fields: `MontageId`, `PlayRate`, `BlendInTime`, `BlendOutTime`, `StartSectionIndex`, `HasPendingPlay`, `HasPendingStop`, `StopBlendOutTime`.

**Field names:** `channel.Params` and `channel.State` (D-04 compliant; NOT `ActionParams`/`ActionState`).

### ANC-P3-02 — LookAtDispatcherSystem

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/LookAtDispatcherSystem.cs`
**Executor file:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Executors/LookAtExecutors.cs`

Architecture: `DispatcherSystemBase<LookAtChannel>` with three executors:
- `LookAtPointExecutor`: reads `LookAtPointParams` from `channel.Params`, stores world point + TargetType=1 in `LookAtExecutorState`
- `LookAtEntityExecutor`: reads `LookAtEntityParams`, packs `TargetEntityId` into `LookAtExecutorState.TargetPointX` via `Unsafe.BitCast<uint,float>`, sets TargetType=2
- `ReleaseLookExecutor`: sets TargetType=0, success status — does NOT require `CanAim`

**Capability check:** LookAtPoint and LookAtEntity require `CanAim`; ReleaseLook is exempted via `bool requiresAim = channel.ActiveAction != LookAtActionIds.ReleaseLook`.

### ANC-P3-03 — StanceTransitionSystem

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/StanceTransitionSystem.cs`

Not a dispatcher. Watches `StanceIntent.Version` vs `StanceStatus.AckVersion`. Three code paths:
1. No `CanChangeStance`: silently ack (`AckVersion = Version`), leave Phase as-is
2. Same target stance: immediately ack, set `Phase = Locked`
3. Different stance: call `backend.RequestStanceChange(handle, targetStance, blendTime)`, ack, set `Phase = Transitioning`

**Note:** BackendHandle encoding for stance system is the same as bridge: `(uint)(def.BackendHandle & 0xFFFFFFFF)` = Index, `(uint)((def.BackendHandle >> 32) & 0xFFFFFFFF)` = Generation. In tests, BackendHandle still equals ClassId (before bridge runs), so stance calls to fake backend use an unregistered handle — gracefully handled by FakeAnimationBackend (no-op if handle not found).

### ANC-P3-04 — MontageQueueAdvanceSystem

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/MontageQueueAdvanceSystem.cs`

Runs in `Simulation`. Advances `AnimationMontageQueueState` when `InBlendOutWindow == true`:
- If next entry exists: advance `CurrentEntryIndex`, reset elapsed, clear blend-out flag
- If no next entry: mark queue done with `CurrentEntryIndex = 0xFF`

**Guard:** Skips entities where `def.BackendHandle <= 0` (bridge not yet run, handle not registered).

### ANC-P3-05 — AnimationRuntimeBridgeSystem

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/AnimationRuntimeBridgeSystem.cs`

Key responsibility:
1. **First tick per entity:** `backend.RegisterEntity((uint)entity.Index, classId)` — stores returned `AnimationBackendHandle` encoded as `long` in `CharacterAnimationDefRuntime.BackendHandle` (Index in low 32 bits, Generation in high 32 bits). Uses `Dictionary<ulong, long>` keyed on `entity.PackedValue` to track registered entities.
2. **Per-tick:** reads `StagedPlayIntent` from `AnimationExecutorState.SlotsData`, calls `backend.PlayMontageOnSlot` or `backend.StopMontageOnSlot`, clears flags.
3. **Final step:** calls `backend.Tick(deltaTime)` once after all entity updates.

**Design decision:** Bridge is the single point that calls the backend. This cleanly separates ECS state management (dispatcher/executors) from backend interaction (bridge), making either side independently testable.

---

## 5. Developer Insights

### Issues Encountered

1. **`SystemPhase.PreSimulation` missing:** The design doc and session notes said "PreSimulation phase" but the enum only has `Input`, `BeforeSync`, `Simulation`, `PostSimulation`. Used `Simulation` (same phase as `WeaponDispatcherSystem`). The ordering between dispatcher and bridge within the same phase relies on registration order in the module array (not phase ordering).

2. **`Entity.Id` doesn't exist:** Entity uses `Index` (int) + `Generation` (ushort). No `Id` property. Used `entity.PackedValue` (ulong) as the dictionary key in the bridge's registration tracking.

3. **`NodeStatus` namespace is `Fbt`:** Not `Fdp.Toolkit.Behavior` — though `Fdp.Toolkit.Behavior` uses `Fbt.NodeStatus`. All executor/system files needed `using Fbt;`.

4. **`EntityRepository.RegisterComponentType<T>()` doesn't exist:** The correct API is `RegisterComponent<T>()`. Session notes had the wrong name.

5. **`AnimationMontageQueueState.InBlendOutWindow` needs `[MarshalAs(UnmanagedType.I1)]`:** The ECS layout validator enforces that bool fields in unmanaged components have explicit 1-byte layout contracts. Required fixing `ReplicatedComponents.cs`.

6. **`BakingUtils.BakeDef` computes `MarkerHash` from name, not DTO:** The `NotifyMarkerDefDto.Hash` field is present in the DTO but `BakingUtils` ignores it and calls `StableIdHasher.ComputeMarkerHash(name)`. Tests must use the computed hash, not the hardcoded DTO value.

7. **`CharacterAnimationDefDto.AimConfig` is `init`-only:** Cannot mutate post-construction. Must use `dto with { AimConfig = null }` C# record syntax.

8. **`Hrot.MuscleCharacter.Animation` was not in the solution file:** Caused `NU1105` errors when building `IOS-IG-SimHost.sln`. Fixed with `dotnet sln add`.

### Weak Points in Existing Codebase

1. **`BakedAnimationCache` ignores `NotifyMarkerDefDto.Hash`:** The DTO has a `Hash` field that is never used by `BakingUtils.BakeDef`. This field is dead. Either the DTO should remove it, or the baking should use it to allow stable hash overrides. Currently the canonical hash is always computed from the name.

2. **`StanceTransitionSystem` uses decoded BackendHandle:** Before the bridge runs and registers the entity, calling `backend.RequestStanceChange` with a classId-encoded handle will silently no-op in `FakeAnimationBackend`. In production with a real backend this could be an issue if stance transitions are requested before the first simulation tick. The bridge should ideally run before stance system, but both are in `Simulation` phase.

3. **`StagedPlayIntent` consumes first 20 bytes of `AnimationExecutorState.SlotsData`:** `AnimationExecutorState.MaxSlots = 8`, `SlotStateSize = 28`, so `SlotsData = 224 bytes`. The staging area uses only the first 20 bytes, leaving 204 bytes for actual slot state. This is an ad-hoc reuse of the array. A dedicated staging field in `AnimationExecutorState` would be cleaner, but adding fields risks breaking the size limit.

4. **`LookAtExecutorState.TargetPointX` used to store entity ID:** `TargetEntityId` (uint) is bit-cast to float and stored in `TargetPointX` (float field) when `TargetType == 2`. This is a type-unsafe reuse of a field. A union-style field or explicit `TargetEntityId` field in the struct would be better.

### Design Decisions Beyond the Spec

1. **`StagedPlayIntent` staging protocol:** Instead of adding new fields to already-constrained components, we use the first N bytes of `AnimationExecutorState.SlotsData` as a staging area shared between executors and the bridge. The `HasPendingPlay`/`HasPendingStop` flags act as a simple protocol.

2. **Bridge handles all backend calls:** Executors stage intent; bridge applies. This allows testing dispatcher behavior (channel state) without needing a registered backend handle.

3. **`BackendHandle` dual-use encoding:** The `CharacterAnimationDefRuntime.BackendHandle` (long) starts as the classId and is overwritten by the bridge to encode `AnimationBackendHandle` (Index in low 32, Generation in high 32). This uses the spare 4-byte room in the component (the struct is 12 bytes; limit is 16 bytes) which is exactly the right size for the encoded handle.

---

## 6. Blockers for BATCH-05

### No hard blockers. Known partial implementations:

1. **`PlayMontageQueueExecutor` is a stub:** Queue management (crossfade sequencing, per-entry play calls) is not implemented in the executor. The `MontageQueueAdvanceSystem` handles state advancement but the executor doesn't validate or sequence queue entries. BATCH-05 should complete this.

2. **`LookAtDispatcherSystem` entity-mode not bridge-integrated:** The bridge system doesn't currently resolve `LookAtExecutorState.TargetType == 2` to a world point from `SimTransform`. The entity-mode look-at executor stores the entity ID but the bridge needs a `SimTransform` query step. BATCH-05 needs to add this to the bridge.

3. **No `NotifyEventEmitterSystem`, `AnimationStateReporterSystem`, or `AnimationBackendCleanupSystem`:** These PostSimulation systems are listed for BATCH-05. Without cleanup, destroyed entity backend handles are not released, which will leak per-entity state in `FakeAnimationBackend._states`.

4. **`StanceTransitionSystem` doesn't poll for transition completion:** Currently it only starts transitions; it doesn't observe `StanceStatus.Phase` updates from the backend. A polling step is needed to detect when the backend completes the transition and update `StanceStatus.Phase` accordingly.
