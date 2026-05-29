# Hrot.MuscleCharacter.Animation.Fake

**Project path:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/Hrot.MuscleCharacter.Animation.Fake.csproj`
**Assembly:** `Hrot.MuscleCharacter.Animation.Fake`
**Target framework:** net8.0
**Date:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.MuscleCharacter.Animation.Fake` provides `FakeAnimationBackend`, a
deterministic, render-free implementation of `IAnimationBackend` used for unit
testing, integration testing, and simulation runs that do not require a real
graphics engine.

Key properties:

1. **Fully behavioral** — tracks per-entity slot playback, blend weights, aim
   state, and stance transitions without any Stride or rendering dependency.
2. **Deterministic** — given the same sequence of calls and `deltaTime` values,
   produces identical outputs; suitable for reproducible integration test scenarios.
3. **Synthetic footstep emission** — simulates footstep notify events based on
   horizontal velocity and a configurable stride length, matching the behavior of
   the real Stride backend at the notify-event level.
4. **Two construction modes** — minimal (smoke tests, no montage data) and
   behavioral (with per-class `CharacterAnimationBakedData` for full montage
   duration and marker lookup).
5. **JSON snapshot export** — `FakeAnimBackendSnapshotJson` can serialize the
   entire per-entity backend state to JSON for AAR (After-Action Review) pipelines.
6. **Diagnostic ImGui window** — `FakeAnimBackendWindow` (in `Windows/`) provides
   a real-time diagnostic overlay showing slot states, aim weights, and stance.

The design is governed by `DD-Fake_FakeAnimationBackend_v1_1.md`.

---

## Architecture

### State Storage Model

Unlike the ECS-backed real backend, `FakeAnimationBackend` uses a plain managed
dictionary to store per-entity state. This isolates entity state from ECS component
layout and makes the backend trivially safe to use in test scenarios that do not
run a full ECS world.

Two parallel dictionaries are maintained:

- `_handleSlots` — maps handle index → (Generation, EntityId)
- `_entityStates` — maps EntityId → `EntityBehavioralState`

Generation tracking provides the same stale-handle protection as the production
backend's `AnimationBackendHandle.Generation` check.

### Slot Playback

Each entity carries 8 `FakeSlotState` entries. On `PlayMontageOnSlot`:
- The slot's `IsActive`, `MontageHash`, `ElapsedSeconds`, `TotalDurationSeconds`,
  `BlendInTime`, `BlendOutTime`, and `PlayRate` are populated.
- `FiredNotifyMask` (64-bit) tracks which notify markers in this montage have
  already fired this playback, preventing double-emission.

On `Tick(deltaTime)`:
- Each active slot advances `ElapsedSeconds += deltaTime * PlayRate`.
- Blend weights are computed (blend-in from 0→1, blend-out from 1→0 near end).
- When `ElapsedSeconds >= TotalDurationSeconds` the slot enters blend-out and
  eventually deactivates, causing `IsAnySlotActive` to return false on the next
  check (triggering `AnimationStateReporterSystem` to emit `MontageEndedEvent`).
- Notify markers in the baked montage data are checked; those whose
  `TimeNormalized` falls within the elapsed range fire exactly once (masked by
  `FiredNotifyMask`) and are appended to `PendingNotifies`.

### Footstep Cadence

`FakeAnimationBackend` emits synthetic `Footstep` notifies based on locomotion
input:

- Minimum speed threshold: `0.3 m/s` (below this, no footsteps).
- Stride length: `0.9 m` — a footstep fires every 0.9 m of accumulated horizontal
  distance travelled.
- Foot alternation: `NextFootIndex` alternates 0 (left) / 1 (right).

Locomotion inputs are updated via `SetLocomotionVelocity(handle, vx, vz, vy, isGrounded)`.

### `FakeAnimBackendState` Component (ECS variant)

In addition to the dictionary-based `FakeAnimationBackend`, this project provides
the `FakeAnimBackendState` unmanaged ECS component (ComponentId = `GlobalComponentIds.FakeAnimBackendState`).
This Tier-1 component (~1 KB) stores the same state as an inline struct array
using C# 12 `[InlineArray]` types:

- `FakeSlotsBuffer` — `[InlineArray(8)]` of `FakeSlotState` (~224 bytes)
- `FakePendingNotifyBuffer` — `[InlineArray(16)]` of `RawNotifyEvent` (overflow
  is a hard assert per DD-Fake §6)
- `FakeAimState` — blend weight, target point/entity
- `FakeStanceState` — current stance, transition progress

This ECS component is used when the fake backend needs to store state directly in
an ECS entity rather than in the dictionary (e.g., for networked replication test
setups).

---

## ASCII Block Diagrams

### Diagram 1: Assembly Dependency Graph

```
+------------------------------------------+
|  Hrot.MuscleCharacter.Animation.Fake     |  net8.0 class library
+------------------------------------------+
   |        |        |        |
   |        |        |        +-- Hrot.SimHost
   |        |        |               (SimHost integration for DiagWindow)
   |        |        |
   |        |        +----------- Fdp.Presentation
   |        |                        (ImGui window base, JSON export utils)
   |        |
   |        +------------------- Fdp.Core
   |                                (Entity, ComponentId, DataPolicy)
   |
   +-- Hrot.MuscleCharacter.Animation
          (IAnimationBackend, CharacterAnimationBakedData,
           AnimationBackendHandle, RawNotifyEvent, PlayMontageParams,
           StopMontageParams, LookAtPointParams, LookAtEntityParams,
           ReleaseLookParams, AnimationBackendConfig, AnimationBackendMetrics)
```

### Diagram 2: FakeAnimationBackend Internal State

```
FakeAnimationBackend
  _handleSlots: Dictionary<uint, (uint Generation, uint EntityId)>
  _entityStates: Dictionary<uint, EntityBehavioralState>

EntityBehavioralState
  +-- Slots: FakeSlotState[8]
  |      +-- IsActive, ActiveMontage, ElapsedSeconds, TotalDurationSeconds
  |      +-- BlendInTime, BlendOutTime, PlayRate
  |      +-- CurrentSectionIndex, InBlendOutWindow, BlendWeight
  |      +-- FiredNotifyMask (64-bit)
  |
  +-- Aim: FakeAimState
  |      +-- IsActive, IsReleasing, BlendWeight, TargetX/Y/Z, Priority
  |
  +-- Stance: FakeStanceState
  |      +-- CurrentStance, TargetStance, IsTransitioning, TransitionProgress
  |
  +-- Locomotion: HorizontalVelX/Z, VerticalVelocity, IsGrounded
  +-- DistanceSinceLastFootstep, NextFootIndex
  +-- PendingNotifies: List<RawNotifyEvent>
```

---

## Key Types

### `FakeAnimationBackend` (root class)

Implements `IAnimationBackend`. Key behaviors:

| Method | Behavior |
|--------|----------|
| `RegisterEntity(entityId, characterDefHandle)` | Allocates handle index + generation; stores entity state; returns `AnimationBackendHandle` |
| `UnregisterEntity(handle)` | Validates generation; removes from both dictionaries |
| `TryResolve(handle, out state)` | Generation check; returns entity ID as `nint` |
| `PlayMontageOnSlot(handle, params)` | Activates the specified slot; if `_classData` present, looks up total duration from baked data |
| `StopMontageOnSlot(handle, params)` | Enters blend-out on specified slot (or all if SlotIndex=0xFF) |
| `SetAimTargetPoint/Entity` | Updates `FakeAimState`; sets blend-in progress |
| `ReleaseAim` | Sets `IsReleasing=true` on aim state |
| `RequestStanceChange` | Updates `FakeStanceState` target and transition duration |
| `Tick(deltaTime)` | Advances all slots, aim blend, stance blend, footstep distance; appends notifies |
| `DrainNotifies(handle, dest)` | Copies `PendingNotifies` to dest; clears the list; returns count |
| `IsAnySlotActive(handle)` | Returns true if any slot has `IsActive=true` |
| `SnapshotMetrics()` | Returns active entity count, total active slots, pending notify count |

**Construction modes:**
```csharp
// Minimal (no montage duration data — slots deactivate after 1.0 s default):
var backend = new FakeAnimationBackend();

// Behavioral (with baked montage data for accurate durations and marker firing):
var backend = new FakeAnimationBackend(classData);
```

### `FakeAnimBackendState` Component (`Hrot.MuscleCharacter.Animation.Fake.Components`)

Unmanaged ECS component for in-ECS state storage (alternative to dictionary mode).

| Field | Type | Description |
|-------|------|-------------|
| `Generation` | uint | Stale-handle detection counter |
| `TotalTicks` | long | Total Tick() calls this entity participated in |
| `Slots` | `FakeSlotsBuffer` (8 × `FakeSlotState`) | Per-slot inline array |
| `Aim` | `FakeAimState` | Aim overlay state |
| `Stance` | `FakeStanceState` | Stance transition state |
| `HorizontalSpeed` | float | Magnitude of horizontal velocity |
| `LocalHorizontalVelocity` | Vector2 | Local-space horizontal velocity (+X = forward) |
| `VerticalVelocity` | float | Vertical velocity |
| `IsGrounded` | byte | 1=grounded, 0=airborne |
| `DistanceSinceLastFootstep` | float | Accumulated distance since last footstep notify |
| `NextFootIndex` | byte | 0=left, 1=right |
| `PendingNotifyCount` | byte | Live entries in `PendingNotifies` |
| `PendingNotifies` | `FakePendingNotifyBuffer` (16 × `RawNotifyEvent`) | Pending notify ring; overflow is hard assert |

`[InlineArray]` types:
- `FakeSlotsBuffer` — `[InlineArray(8)]` of `FakeSlotState`
- `FakePendingNotifyBuffer` — `[InlineArray(16)]` of `RawNotifyEvent`

### `FakeSlotState` (`Hrot.MuscleCharacter.Animation.Fake.Components`)

| Field | Description |
|-------|-------------|
| `IsActive` (byte) | 0=inactive, 1=active |
| `ActiveMontage` (MontageAssetId) | Currently-playing montage hash |
| `ElapsedSeconds` | Playback position in seconds |
| `TotalDurationSeconds` | Full montage length in seconds |
| `BlendInTime` / `BlendOutTime` | Blend ramp durations |
| `PlayRate` | Speed multiplier |
| `CurrentSectionIndex` | Active section index within montage |
| `InBlendOutWindow` (byte) | 1 when slot is in the blend-out phase |
| `BlendWeight` | Current effective blend weight [0..1] |
| `FiredNotifyMask` (ulong) | Bit per notify marker; prevents double-fire |

### JSON Snapshot (`Hrot.MuscleCharacter.Animation.Fake.Diagnostics`)

`FakeAnimBackendSnapshotJson` serializes the complete per-entity backend state to
a JSON string. Used by `AnimationTkbTranslator` to emit AAR-compatible snapshots
at scenario end.

---

## Dependencies

```
Hrot.MuscleCharacter.Animation.Fake
  --> Hrot.MuscleCharacter.Animation   (IAnimationBackend and all contracts)
  --> Fdp.Core                         (Entity, ComponentId, DataPolicy)
  --> Fdp.Presentation                 (ImGui DiagWindow base, JSON export)
  --> Hrot.SimHost                     (SimHost integration for DiagWindow)
```

---

## Usage Patterns

### Using FakeAnimationBackend in an integration test

```csharp
// Build baked class data from a test TKB descriptor:
var dto = CharacterAnimationDefDto.BakeForTest(new[] { "Idle", "Run", "Fire" });
var bakedData = BakingUtils.BakeDef(dto);
var classData = new Dictionary<long, CharacterAnimationBakedData>
    { [dto.ClassId] = bakedData };

var backend = new FakeAnimationBackend(classData);
var cache = new BakedAnimationCache(hotReloadEvents: null);

// Wire into the Muscle animation module:
var module = new AnimationMuscleModule(backend, cache);
```

### Asserting on notify events in tests

```csharp
// After pumping the simulation for enough ticks:
var buf = new RawNotifyEvent[16];
int count = backend.DrainNotifies(entityHandle, buf.AsSpan());
Assert.Equal(1, count);
Assert.Equal(AnimNotifyCategory.Footstep, buf[0].Kind);
```

### Inspecting slot state

```csharp
// FakeAnimationBackend exposes test-helper GetSlotState:
var slot = backend.GetSlotStateForTest(entityHandle, slotIndex: 0);
Assert.True(slot.IsActive == 1);
Assert.Equal(expectedMontageHash, slot.ActiveMontage.Hash);
```
