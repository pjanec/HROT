# Hrot.MuscleCharacter.Animation.Stride

**Project path:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride/Hrot.MuscleCharacter.Animation.Stride.csproj`
**Assembly:** `Hrot.MuscleCharacter.Animation.Stride`
**Target framework:** net8.0
**Date:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.MuscleCharacter.Animation.Stride` provides `StrideAnimationBackend`, the
production implementation of `IAnimationBackend` that bridges the ECS animation
pipeline to the Stride game engine's blend tree and animation clip evaluators.

Key design principles (DD-1 §15–16):

1. **No Stride types leak into the public API.** All Stride engine types
   (`AnimationComponent`, `AnimationOperation`, `BlendTreeBuilder`) are
   encapsulated behind `StrideAnimationBackend`'s internal state. The public
   surface exposes only `IAnimationBackend` methods plus `MontageMarker`
   (for asset-import bridge setup).
2. **Per-entity `PerEntityBlendTreeBuilder`** drives blend weight computation
   each frame via the `BuildBlendTree()` callback, simulating
   `IBlendTreeBuilder.BuildBlendTree` as required by Stride's animation system.
3. **Marker-based notify emission** — montage markers are registered at startup
   via `RegisterMontageMarkers`; the backend fires `RawNotifyEvent` when the
   clip playhead crosses each marker's `TimeSeconds`.
4. **Entity transform mirroring** — world-space position/yaw per entity is
   updated via `SetEntityTransform` each tick (Option A from DD-1 §15.3) for
   root-motion and aim-target resolution.
5. **Smoke-tested** — `StrideBackendSmokeTest` validates registration,
   playback start/stop, notify emission, stance transitions, and aim
   acquisition without requiring a running Stride engine.

The design is governed by `DD-1_MuscleCharacterRuntime_v1_2.md` §15–16 and
`ANC-P8` tasks in the TASK-TRACKER.

---

## Architecture

### Blend Tree Integration

Stride's animation system calls `IBlendTreeBuilder.BuildBlendTree()` each frame
for every `AnimationComponent`. `StrideAnimationBackend` implements this interface
per entity via `PerEntityBlendTreeBuilder`. On each call:

1. All active slots compute a blend weight based on blend-in/out progress.
2. Each active slot pushes an `AnimationOperation` (Play + weight) to Stride's
   blend tree stack.
3. The aim layer overlays an additive blend operation if aim is active.

### State Management

`StrideAnimationBackend` maintains a generation-safe pool indexed by
`AnimationBackendHandle.Index`:

- Each slot carries `SlotPlaybackState`: elapsed time, duration, blend
  in/out progress, playback rate, fired marker bitmask.
- Each entity carries an aim layer (`AimLayerState`) and stance
  transition tracking (`StanceTransitionState`).
- Per-entity marker tables are pre-registered at startup by the asset
  import bridge calling `RegisterMontageMarkers`.

### Notify Emission

On each `Tick(deltaTime)` call per entity:
1. Each active slot advances `ElapsedSeconds += deltaTime * PlayRate`.
2. Registered `MontageMarker` entries for the slot's montage hash are
   scanned; markers whose `TimeSeconds` falls within the newly elapsed
   range and have not already fired (guarded by `FiredMarkerMask`) are
   appended to the entity's `NotifyBuffer`.
3. On `DrainNotifies(handle, dest)`, the buffered `RawNotifyEvent`
   records are moved to the caller's span.

---

## ASCII Block Diagrams

### Diagram 1: Assembly Dependency Graph

```
+---------------------------------------------------+
|  Hrot.MuscleCharacter.Animation.Stride            |  net8.0 class library
+---------------------------------------------------+
   |
   +-- Hrot.MuscleCharacter.Animation
          (IAnimationBackend, AnimationBackendHandle,
           AnimationBackendConfig, AnimationBackendMetrics,
           RawNotifyEvent, AnimNotifyCategory,
           PlayMontageParams, StopMontageParams,
           LookAtPointParams, LookAtEntityParams,
           ReleaseLookParams)

(Stride.Engine and Stride.Rendering are referenced at runtime only,
 not at compile time in the smoke-test configuration.)
```

### Diagram 2: Per-entity blend tree tick

```
ECS: AnimationRuntimeBridgeSystem.Execute()
  --> backend.Tick(deltaTime)
        |
        +-- for each registered entity:
              blendTreeBuilder.Advance(deltaTime)
                  |
                  +-- slot[i].ElapsedSeconds += deltaTime * PlayRate
                  +-- check FiredMarkerMask, fire new markers -> NotifyBuffer
                  +-- compute BlendWeight (blend-in / full / blend-out)
                  +-- if elapsed > duration: InBlendOut = true
                  +-- if blend-out complete: IsActive = false

ECS: NotifyEventEmitterSystem.Execute()
  --> backend.DrainNotifies(handle, buf)
        |
        +-- copy NotifyBuffer -> buf
        +-- clear NotifyBuffer
        +-- return count

ECS: AnimationStateReporterSystem.Execute()
  --> backend.IsAnySlotActive(handle)
        |
        +-- scan slot[0..7].IsActive
        +-- if none: report MontageEndedEvent(NaturalEnd)
```

---

## Key Types

### `StrideAnimationBackend` (public class)

Implements `IAnimationBackend`. Confines all Stride engine types internally.

| Method | Description |
|--------|-------------|
| `Initialize(config)` | Configures pool size and default blend times |
| `RegisterEntity(entityId, characterDefHandle)` | Allocates handle + `PerEntityBlendTreeBuilder`; links `AnimationComponent` if available |
| `UnregisterEntity(handle)` | Frees the entity's blend tree builder and disconnects from Stride's `AnimationComponent` |
| `PlayMontageOnSlot(handle, params)` | Activates the slot; looks up duration from registered montage clip; resets `FiredMarkerMask` |
| `StopMontageOnSlot(handle, params)` | Enters blend-out on the slot; or immediate stop if `BlendOutTime=0` |
| `SetAimTargetPoint/Entity(handle, params)` | Updates `AimLayerState` with new target and initiates blend-in |
| `ReleaseAim(handle, params)` | Sets aim layer to releasing state; begins blend-out |
| `RequestStanceChange(handle, stance, blendDuration)` | Updates `StanceTransitionState`; drives blend via the Stride skeleton override system |
| `Tick(deltaTime)` | Advances all per-entity builders; emits notifies; updates `BuildBlendTree` data |
| `DrainNotifies(handle, dest)` | Moves entity's `NotifyBuffer` → `dest`; returns count |
| `IsAnySlotActive(handle)` | True if any slot has `IsActive=true` |
| `SnapshotMetrics()` | Returns active entity count, active slot total, pending notify count, last tick time |
| `RegisterMontageMarkers(montageHash, markers[])` | Asset-import bridge: stores `MontageMarker[]` for a montage hash so the backend can fire notifies during playback |
| `SetEntityTransform(handle, x, y, z, yaw)` | Updates world-space entity position/orientation for root motion and aim resolution |

### Internal Types

All internal types are in the `Hrot.MuscleCharacter.Animation.Stride` namespace
and are not visible to callers of `IAnimationBackend`.

| Type | Description |
|------|-------------|
| `PerEntityBlendTreeBuilder` | Per-entity state: 8 `SlotPlaybackState`, `AimLayerState`, `StanceTransitionState`, `NotifyBuffer`; implements `BuildBlendTree()` |
| `SlotPlaybackState` | Slot: IsActive, MontageHash, ElapsedSeconds, DurationSeconds, PlayRate, BlendIn/OutTime, BlendWeight, InBlendOut, FiredMarkerMask (ulong) |
| `AimLayerState` | Aim: IsActive, IsReleasing, BlendWeight, BlendIn/OutTime, TargetX/Y/Z, Priority |
| `StanceTransitionState` | Stance: CurrentStance, TargetStance, IsTransitioning, TransitionProgress, TransitionTotalSeconds |
| `StrideEntityTransform` | World-space X/Y/Z + Yaw; updated each tick by `SetEntityTransform` |

### `MontageMarker` (public struct)

Keyframed marker supplied by the asset-import bridge at startup. Fields:

| Field | Description |
|-------|-------------|
| `TimeSeconds` | Clip time at which the marker fires |
| `Kind` (AnimNotifyCategory) | Notify category to emit |
| `MarkerHash` (uint) | Name hash for Generic notifies |
| `PayloadFloat` | Passed through to `RawNotifyEvent.PayloadFloat` |
| `PayloadUint` | Passed through to `RawNotifyEvent.PayloadUint` |

---

## Dependencies

```
Hrot.MuscleCharacter.Animation.Stride
  --> Hrot.MuscleCharacter.Animation   (IAnimationBackend and all contract types)
```

Stride engine assemblies (`Stride.Engine`, `Stride.Rendering`) are referenced
at runtime only via the host application's package references, not in this
project file. This allows the assembly to compile without a Stride SDK.

---

## Usage Patterns

### Asset-import bridge setup at Muscle node startup

```csharp
var backend = new StrideAnimationBackend();
backend.Initialize(new AnimationBackendConfig
{
    MaxEntities = 256,
    DefaultBlendInTime  = 0.2f,
    DefaultBlendOutTime = 0.3f,
    DefaultPlayRate     = 1.0f,
});

// For each montage clip imported from the asset pipeline:
backend.RegisterMontageMarkers(
    montageHash: StableIdHasher.ComputeMontageAssetId("Run_Fwd"),
    markers: new[]
    {
        new MontageMarker { TimeSeconds = 0.3f, Kind = AnimNotifyCategory.Footstep, PayloadUint = 0 },
        new MontageMarker { TimeSeconds = 0.7f, Kind = AnimNotifyCategory.Footstep, PayloadUint = 1 },
    });

// Wire into AnimationMuscleModule as the production backend:
var module = new AnimationMuscleModule(backend, bakedCache);
systemRegistry.RegisterModule(module);
```

### Updating entity transform for root motion

```csharp
// Called from the physics or transform system each tick before AnimationMuscleModule.Execute:
var t = entity.Transform.WorldMatrix;
backend.SetEntityTransform(handle, t.M41, t.M42, t.M43, yaw: MathF.Atan2(t.M31, t.M11));
```

---

## Test Projects

| Project | Description |
|---------|-------------|
| `Hrot.MuscleCharacter.Animation.Stride.Tests` | `StrideBackendSmokeTest` suite: validates registration, PlayMontageOnSlot, notify emission, DrainNotifies, stance transitions, and aim blend without a running Stride engine |
