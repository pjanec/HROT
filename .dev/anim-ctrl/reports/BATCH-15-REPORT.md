# BATCH-15 Report

**Batch:** BATCH-15
**Tasks:** ANC-P8-01, ANC-P8-02, ANC-P8-03
**Phase:** Phase 8 - Stride backend + smoke validation
**Status:** COMPLETE

---

## Summary

All three Phase 8 tasks implemented and verified. The `StrideAnimationBackend`
implements the full `IAnimationBackend` contract using internal Stride-namespace
types with no engine type leakage. The notify/transform mapping path is wired
and smoke-tested. The 31-test smoke suite validates boot, tick, handle lifecycle,
marker/notify drain, and transform update.

---

## Files Changed

### ANC-P8-01 — `StrideAnimationBackend` skeleton

**New files:**

- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride/Hrot.MuscleCharacter.Animation.Stride.csproj`
  - New library project; references `Hrot.MuscleCharacter.Animation` only.

- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride/StrideAnimationBackend.cs`
  - `StrideAnimationBackend` — sealed class implementing `IAnimationBackend`.
  - `PerEntityBlendTreeBuilder` — internal sealed class; simulates Stride's
    `IBlendTreeBuilder.BuildBlendTree()` callback without leaking engine types.
  - `SlotPlaybackState`, `AimLayerState`, `StanceTransitionState`, `StrideEntityTransform`
    — internal structs; all confined to the `.Stride` namespace.
  - Entry pool: `Entry[]` (256 slots) + `Stack<int>` free-index pool.
    Generation-safe: each unregister increments the generation so stale handles
    return false from `TryResolve`, `IndexOf`, and all mutating operations.

### ANC-P8-02 — Stride scene/transform + notify mapping

Same file (`StrideAnimationBackend.cs`):

- `MontageMarker` — public struct in the `.Stride` namespace. Carries
  `TimeSeconds`, `Kind`, `MarkerHash`, `PayloadFloat`, `PayloadUint`.
  Represents a keyframed clip marker baked at asset import time (DD-1 §15.4).

- `RegisterMontageMarkers(MontageAssetId, MontageMarker[])` — public method on
  `StrideAnimationBackend` (not on `IAnimationBackend`). Called by the asset
  bridge or tests to supply the per-montage marker schedule.

- `SetEntityTransform(handle, x, y, z, yaw)` — public method implementing
  Option A from DD-1 §15.3: the rendering bridge writes `SimTransform →
  StrideEntity.Transform` each tick. Stored in `Entry.Transform`; processed
  without crash even when handle is stale.

- Marker crossing logic in `AdvanceSlots()`: for each active slot, checks all
  registered markers; on crossing, pushes a `RawNotifyEvent` into the entity's
  `PerEntityBlendTreeBuilder.NotifyBuffer`. Each marker fires exactly once per
  play (bit tracked in `SlotPlaybackState.FiredMarkerMask`).

- `DrainNotifies(Span<RawNotifyEvent>)` (global) and
  `DrainNotifies(handle, Span<RawNotifyEvent>)` (per-entity) — same drain path
  the FakeAnimationBackend uses; clears the buffer after each drain.

### ANC-P8-03 — `StrideBackendSmokeTest` suite

**New files:**

- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride.Tests/Hrot.MuscleCharacter.Animation.Stride.Tests.csproj`
  - xunit 2.6.2 test project; references `Hrot.MuscleCharacter.Animation.Stride`.

- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride.Tests/StrideBackendSmokeTests.cs`
  - 31 tests in `StrideBackendSmokeTests` class (see test list below).

**Modified files:**

- `IOS-IG-SimHost.sln`
  - Added project declarations for `Hrot.MuscleCharacter.Animation.Stride`
    (GUID `{C6D7E8F9-A0B1-2345-6789-ABCDEF012345}`) and
    `Hrot.MuscleCharacter.Animation.Stride.Tests`
    (GUID `{D7E8F9A0-B1C2-3456-789A-BCDEF0123456}`).
  - Added both to `ProjectConfigurationPlatforms` and `NestedProjects`
    (parent folder `{9DCE0DFF-1A90-4579-AFBF-04768ED55A11}` — Subsystems).

---

## Tests Added

All 31 tests in `StrideBackendSmokeTests` (class in
`Hrot.MuscleCharacter.Animation.Stride.Tests`):

| # | Test Name | Behavior Validated |
|---|-----------|-------------------|
| 1 | `Backend_Construction_Succeeds` | Backend can be constructed without crash |
| 2 | `Backend_Initialize_Succeeds` | `Initialize(config)` is safe; metrics returns 0 after init |
| 3 | `RegisterEntity_ReturnsValidHandle` | Handle has non-sentinel index, non-zero generation, IsValid==true |
| 4 | `TryResolve_WithValidHandle_ReturnsTrue` | Live handle resolves |
| 5 | `TryResolve_WithStaleHandle_ReturnsFalse` | Handle after unregister does not resolve |
| 6 | `UnregisterEntity_FollowedByReregister_BumpsGeneration` | Slot reuse bumps generation; old handle rejected |
| 7 | `UnregisterEntity_WithStaleHandle_IsNoop` | Double unregister does not crash |
| 8 | `MultipleEntities_AllResolveCorrectly` | Three concurrent entities all resolve, states are distinct |
| 9 | `SnapshotMetrics_ReflectsActiveEntityCount` | Metrics.ActiveEntityCount matches registered count |
| 10 | `PlayMontageOnSlot_Succeeds` | Call succeeds without crash |
| 11 | `PlayMontageOnSlot_MakesSlotActive` | `IsAnySlotActive` returns true after play |
| 12 | `Tick_DoesNotCrash_WithActiveSlot` | 30-tick loop with active slot is stable |
| 13 | `Tick_WithMultipleEntities_DoesNotCrash` | 60-tick loop with two entities is stable |
| 14 | `Slot_BecomesInactive_AfterNaturalDuration` | Slot auto-deactivates after default 1.0 s duration |
| 15 | `StopMontageOnSlot_ClearsActiveSlot` | Stop clears all active slots immediately |
| 16 | `PlayMontageOnSlot_WithStaleHandle_IsNoop` | Stale handle play does not crash |
| 17 | `MarkerNotify_IsNotFired_BeforeMarkerTime` | No event before marker time is crossed |
| 18 | `MarkerNotify_IsFired_AfterMarkerTime` | Event fires with correct Kind, MarkerHash, TimeSeconds, PayloadFloat, PayloadUint |
| 19 | `MarkerNotify_FiredOnce_NotDuplicated` | Marker fires exactly once per play, not on subsequent ticks |
| 20 | `GlobalDrainNotifies_AggregatesAcrossEntities` | Global drain returns events from all entities |
| 21 | `DrainNotifies_AfterDrain_IsEmpty` | Second drain after first returns 0 |
| 22 | `SetEntityTransform_DoesNotCrash` | Transform write + tick does not crash; handle still resolves |
| 23 | `SetEntityTransform_WithStaleHandle_IsNoop` | Stale handle transform write is safe |
| 24 | `Tick_WithTransformAndActiveSlot_DoesNotCrash` | Moving entity + active slot across 10 ticks is stable |
| 25 | `SetAimTargetPoint_Succeeds` | Aim-at-point call + tick is safe |
| 26 | `SetAimTargetEntity_Succeeds` | Aim-at-entity call + tick is safe |
| 27 | `ReleaseAim_AfterSetAim_DoesNotCrash` | Aim blend-in then release blend-out runs without crash |
| 28 | `RequestStanceChange_Succeeds` | Stance transitions from 0→1 over the requested blend duration |
| 29 | `GetCurrentStance_WithStaleHandle_ReturnsFalse` | Stale handle stance query returns false |
| 30 | `SnapshotMetrics_ReflectsActiveSlots` | Metrics.TotalActiveSlotsCount matches playing entities |
| 31 | `SnapshotMetrics_LastTickMs_IsNonnegative` | Tick timing metric is non-negative |

---

## Build / Test Command Summaries

### StrideAnimationBackend library

```
dotnet build Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride/Hrot.MuscleCharacter.Animation.Stride.csproj -c Debug
Build succeeded.
```

### Stride smoke test suite (ANC-P8-03)

```
dotnet test Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride.Tests/Hrot.MuscleCharacter.Animation.Stride.Tests.csproj --no-build --logger "console;verbosity=normal"
Total tests: 31
     Passed: 31
 Total time: 3.49 Seconds
```

### Full solution build

```
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
Build succeeded.
```

### Replication regression check

```
dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug --no-build
Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 309 ms
```

---

## Developer Insights

### 1. Issues encountered and how resolved

**Issue:** `0xDEAD_u` is not a valid C# numeric literal (`_` before a type suffix
is invalid). Changed to `0xDEADu`.

**Issue:** `Assert.Equal(float, float, precision: int)` overload does not exist
in xunit 2.6.2. Changed to exact float equality (`Assert.Equal(0.1f, actual)`),
which is safe since the `RawNotifyEvent` fields are set directly without
arithmetic transformation.

### 2. Weak points in Stride integration boundaries

- **Slot assignment is hardcoded to slot 0.** In full production the slot comes
  from the montage asset metadata (`montageInfo.Slot`). The smoke backend has no
  asset data, so all `PlayMontageOnSlot` calls land in slot 0. This is explicitly
  documented in the code. Addressing it requires wiring the asset bridge
  (deferred to a later batch, not in scope here per "smoke-level integration" rule).

- **Marker cap of 64 per montage** (bit mask width). Sufficient for any realistic
  asset. If a montage has more than 64 markers, excess are silently ignored.
  Document in a future debt item if needed.

- **No actual Stride SDK integration.** This project has no Stride NuGet
  dependency. `PerEntityBlendTreeBuilder` simulates `IBlendTreeBuilder` state
  transitions in pure C#. The real integration would add a Stride NuGet package
  and swap the simulation for actual `FastList<AnimationOperation>` push calls.
  The seam is exactly the `BuildBlendTree()` internal method.

### 3. Design decisions beyond spec

- **`SetEntityTransform` stored in `Entry.Transform` but not plumbed into
  `BuildBlendTree`.** The spec says "transform resolution is the rendering
  layer's job"; the backend stores the value so it is available for future
  look-at world-space resolution without changing the interface.

- **`Initialize()` is a public non-interface method** (matching FakeAnimationBackend
  convention). The interface does not declare it; leaving it out of the interface
  allows each backend to have its own initialization contract.

- **Per-entity notify buffer in `PerEntityBlendTreeBuilder`** (not a global ring
  buffer). Matches the FakeAnimationBackend pattern and keeps entity isolation
  clean for the per-entity drain overload.

### 4. Edge cases discovered

- **Stale handle after pool slot reuse:** Verified that generation bump on
  re-use causes the old handle to fail `IndexOf`, making all mutating calls
  no-ops. Test `UnregisterEntity_FollowedByReregister_BumpsGeneration` covers
  the full sequence.

- **Marker fired across a tick boundary:** The FiredMarkerMask uses `ulong` bits.
  If `prevElapsed < marker.TimeSeconds <= newElapsed`, the marker fires exactly
  once regardless of tick size. Verified by `MarkerNotify_FiredOnce_NotDuplicated`.

- **Natural slot completion race:** If a montage completes and markers fire in the
  same tick, the markers are still buffered before the slot is set inactive. Order
  preserved: advance elapsed → check markers → check completion.

### 5. Suggested commit message

```
BATCH-15: Phase 8 Part 1 - StrideAnimationBackend skeleton + smoke suite

ANC-P8-01: StrideAnimationBackend implementing full IAnimationBackend contract
- Generation-safe entry pool (256 slots, Stack<int> free list)
- PerEntityBlendTreeBuilder (internal, simulates Stride IBlendTreeBuilder)
- All IAnimationBackend members: register/unregister, play/stop, aim, stance, tick, drain, metrics
- No Stride engine types leak past Hrot.MuscleCharacter.Animation.Stride namespace

ANC-P8-02: Stride scene/transform + notify mapping
- MontageMarker public struct for keyframed clip marker schedule
- RegisterMontageMarkers() seam for asset bridge to supply per-montage markers
- SetEntityTransform() Option-A bridge (SimTransform -> StrideEntity.Transform)
- Marker crossing check in AdvanceSlots(): pushes RawNotifyEvent per crossing
- Per-entity DrainNotifies + global DrainNotifies matching existing contract

ANC-P8-03: StrideBackendSmokeTest suite (31 tests, all passing)
- Boot + initialize smoke
- Handle lifecycle (register, unregister, stale, re-use generation bump)
- Slot play/tick/natural-completion/stop progression
- Marker/notify: before-time no-fire, after-time fires, fires-once, global drain
- Transform mapping: update + tick no-crash, stale handle is noop
- Aim + stance smoke

Results: 31 smoke tests passing; 42 replication tests passing; solution build clean
```
