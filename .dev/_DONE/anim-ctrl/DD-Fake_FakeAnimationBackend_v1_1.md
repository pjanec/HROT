# DD-Fake — FakeAnimationBackend Implementation — Detailed Design (v1.1)

> **Status:** Architect-approved detailed design for the initial
> `IAnimationBackend` implementation: a fake/mock backend that
> processes animation state deterministically without any 3D
> rendering. Companion to the five approved animation control design
> documents (mini design v0.3, DD-1 through DD-5). This is an
> implementation choice for the Muscle Character node, not a contract
> change to the animation architecture.
> **Changes from v1.0:** All four §11 open questions resolved per
> architect review. §11 converted to Resolutions Summary for
> traceability. ComponentId block 220-249 allocated. Diagnostic window
> registration mechanism confirmed (`IWindowRegistrar` pattern). All
> design decisions locked in.
> **Audience:** Muscle Character implementation team (primary), engine
> architect (sign-off on `[ComponentId]` allocation and window
> registration pattern), AI editor team (informational — the fake is
> what your AI behaviors will run against during early development).
> **Scope:** The `FakeAnimationBackend` class implementing
> `IAnimationBackend` (DD-1 §3), the unmanaged ECS component
> `FakeAnimBackendState` that holds all per-entity working state, the
> tick algorithm, deterministic time advancement, synthetic footstep
> emission, the ImGui-based diagnostic window with JSON snapshot
> export to clipboard.
> **Out of scope:** The eventual Stride backend implementation (separate
> doc). Any visual rendering (the fake has none). The `IAnimationBackend`
> interface contract (DD-1 §3 — unchanged).
> **Reads alongside:** DD-1 §3 (`IAnimationBackend` interface), DD-1
> §10 (`AnimationRuntimeBridgeSystem` — the system that calls into the
> backend), DD-1 §11 (`NotifyEventEmitterSystem` — drains
> `RawNotifyEvent`s the fake produces), DD-1 §18
> (`AnimationStateReporterSystem` — synthesizes lifecycle events from
> backend state), DD-4 §2 (`MontageDefDto`, `NotifyMarkerDefDto` —
> the authored data the fake reads), DD-3 §2 (`AnimNotifyCategory` —
> the canonical discriminator for `RawNotifyEvent.Kind`).

---

## Table of contents

1. Design principles
2. The unmanaged `FakeAnimBackendState` component
3. The backend class — `FakeAnimationBackend`
4. The tick algorithm
5. Synthetic footstep emission
6. Hard-assert discipline
7. The diagnostic ImGui window
8. JSON snapshot export
9. AAR recording integration
10. What the fake intentionally doesn't do
11. Resolutions summary (from v1.0 review)

---

## 1. Design principles

Three principles shape every choice in this document.

**Principle 1: The fake is a best-effort approximation aiming to be close
to the real Stride backend.** It is not an authoritative oracle. The
goal is to unblock AI behavior development without requiring 3D
rendering, asset import, or Stride integration during early
development. Behaviors authored against the fake should behave
similarly enough against the real backend that AI work doesn't
regress when Stride lands — but cross-backend test parity is not
the design goal.

**Principle 2: All per-entity working state lives in an ECS component.**
This buys: entity inspector integration for free, AAR recording for
free, gizmo/scene-view possibilities for free, and an
already-existing diagnostic surface (the engine's standard
component-inspection tooling) for free. The backend class itself
becomes thin — a stateless processor reading/writing the component.

**Principle 3: Unmanaged (Tier 1) component storage.** The state fits
comfortably under 64 KB with fixed-capacity buffers, costs no GC,
records via the engine's raw-memory-copy fast path, and is
mechanically simpler than the alternatives.

A non-principle: performance. The fake is for development and
debugging. It will see at most a few dozen humanoid entities during
AI authoring sessions, not the hundreds the eventual production
backend will handle. Decisions optimize for clarity and
debuggability, not throughput.

## 2. The unmanaged `FakeAnimBackendState` component

The full state component. Tier 1 (unmanaged struct), stored in
`NativeChunkTable<FakeAnimBackendState>`. Total size ~1 KB per entity,
well within the 64 KB Tier 1 limit.

```csharp
namespace Hrot.MuscleCharacter.Animation.Fake;

[ComponentId(GlobalComponentIds.FakeAnimBackendState)]  // ID = 240 (within allocated 220-249 animation block; see §11.1)
[DataPolicy(DataPolicy.NoSave)]                          // runtime state, not in scenarios
[StructLayout(LayoutKind.Sequential)]
public struct FakeAnimBackendState
{
    /// <summary>
    /// Matches AnimationBackendHandle.Generation. Detects stale handles
    /// across unregister/re-register cycles.
    /// </summary>
    public uint Generation;

    /// <summary>Total Tick() calls this entity has been part of.
    /// Useful for debugging.</summary>
    public long TotalTicks;

    /// <summary>Fixed table of 8 slots — DD-1's MaxSlots.</summary>
    public FakeSlotsBuffer Slots;

    public FakeAimState Aim;
    public FakeStanceState Stance;

    // --- Locomotion inputs (for footstep cadence + diagnostics) ---
    public float HorizontalSpeed;             // magnitude of LocalHorizontalVelocity
    public Vector2 LocalHorizontalVelocity;   // local-space, +X = forward
    public float VerticalVelocity;
    public byte IsGrounded;                   // bool-as-byte for deterministic layout
    public float DistanceSinceLastFootstep;
    public byte NextFootIndex;                // 0 = left, 1 = right; alternates

    // --- Pending notify ring (drained each tick by NotifyEventEmitterSystem) ---
    /// <summary>Number of live entries in PendingNotifies. Drained to 0
    /// each tick under normal operation.</summary>
    public byte PendingNotifyCount;

    /// <summary>Inline buffer of pending notify events. Mutate using
    /// Pattern A (Span-cast) or Pattern B (Get→Mutate→SetComponent) per
    /// DD-1 §4.3. Overflow is a hard assert (§6).</summary>
    public FakePendingNotifyBuffer PendingNotifies;
}

[InlineArray(8)]
public struct FakeSlotsBuffer
{
    private FakeSlotState _e0;
}

[InlineArray(16)]
public struct FakePendingNotifyBuffer
{
    private RawNotifyEvent _e0;
}

public struct FakeSlotState
{
    public byte IsActive;                     // bool-as-byte
    public MontageAssetId ActiveMontage;      // 0 = none (matches DD-1 convention)
    public float ElapsedSeconds;
    public float TotalDurationSeconds;
    public float BlendInTime;
    public float BlendOutTime;
    public float PlayRate;
    public byte CurrentSectionIndex;
    public byte InBlendOutWindow;             // bool-as-byte
    public float BlendWeight;                 // 0..1; semantic per DD-1 §3

    /// <summary>Bit i = notify i in this slot's active montage's
    /// Notifies list has already fired this play. ulong = 64 bits
    /// = max 64 notifies per montage (vastly more than any authored
    /// content needs).</summary>
    public ulong FiredNotifyMask;
}

public struct FakeAimState
{
    public byte IsActive;                     // bool-as-byte
    public Vector3 WorldAimPoint;             // current point (lerped toward Target each tick)
    public Vector3 TargetWorldAimPoint;       // requested point
    public float BlendInTime;
    public float BlendOutTime;
    public byte Priority;
    public float BlendWeight;                 // 0..1
    public byte IsReleasing;                  // bool-as-byte
}

public struct FakeStanceState
{
    public StanceId CurrentStance;
    public StanceId TargetStance;
    public byte IsTransitioning;              // bool-as-byte
    public float TransitionProgress;          // 0..1
    public float TransitionTotalSeconds;
}
```

### 2.1 Why bools-as-bytes

The Tier 1 chunk storage demands deterministic memory layout for the
flight recorder's raw-memory-copy and the schema validator's layout
hash. C#'s `bool` is 1 byte on the platforms we target, but the
language spec doesn't guarantee that. Using `byte` for boolean
flags removes the dependency and matches the existing codebase
convention (per the engine's other unmanaged components).

Constants: `0 = false`, `1 = true`. Anything else is undefined; the
backend never writes anything else.

### 2.2 Why the bitmask for fired notifies

A montage has a list of notify markers, each with a `TimeSeconds`
field. As elapsed time advances past each marker's time, that marker
fires *once* per play. We need to track which have fired.

Options considered:
- `List<int>` of remaining indices — needs managed storage.
- `[InlineArray(16)] int Remaining` + count — fixed unmanaged, but the
  "remove fired notify from list" pattern is awkward, and the size
  (16 × 4 = 64 bytes per slot) is large.
- `ulong FiredNotifyMask` — bit i set = notify i has fired. 8 bytes
  per slot. Iteration: `for each notify in def.Notifies, if !bit_set
  && elapsed >= TimeSeconds: fire, set bit`. Simple, small, allocation-
  free.

The bitmask wins. The 64-bit cap is wildly above any authored
content's notify count; if an exotic case ever exceeds 64 notifies on
one montage, that's a content-authoring concern, not a runtime concern.

### 2.3 Why `[InlineArray]` for the notify ring buffer

`fixed RawNotifyEvent[16]` is not legal in C# (`fixed` only allows
primitive types). The C# 12 `[InlineArray]` attribute is the
language-supported way to declare a fixed-capacity inline buffer of
a user-defined struct in an unmanaged context — same mechanism DD-1
§5.1 uses for `AnimationMontageQueue.Entries`.

The mutation hazard (DD-1 §4.3) applies: writes to the buffer must
use either Pattern A (Span-cast) or Pattern B (Get→Mutate→
SetComponent). Direct ref-index assignment silently fails. The
backend code in §3 follows Pattern A throughout.

Capacity 16 is comfortably oversized — a tick can produce at most
~9 notifies (8 slots × 1 notify-keyframe + 1 footstep) under
plausible conditions. Overflow is a hard assert (§6) because hitting
it indicates either a bug in the tick algorithm or that
`NotifyEventEmitterSystem` isn't running.

### 2.4 Size accounting

```
Header (Generation + TotalTicks):           12 bytes
Slots (8 × FakeSlotState ≈ 48 bytes each):  384 bytes
FakeAimState:                                48 bytes
FakeStanceState:                             16 bytes
Locomotion fields:                           ~24 bytes
PendingNotifyCount + buffer (16 × 32):       ~520 bytes
Padding to alignment:                        ~16 bytes
                                           ────────────
Total:                                       ≈ 1 KB
```

Well within Tier 1's 64 KB hard limit. 50 humanoid characters = ~50
KB of chunk memory for fake backend state. Insignificant.

## 3. The backend class — `FakeAnimationBackend`

```csharp
namespace Hrot.MuscleCharacter.Animation.Fake;

public sealed class FakeAnimationBackend : IAnimationBackend
{
    private EntityRepository _repo;
    private uint _nextGeneration = 1;
    private long _tickCount;

    // Handle table: maps AnimationBackendHandle.Index → Entity.
    // Per DD-1 §3's contract, handles are dense integer indices.
    private struct HandleSlot
    {
        public Entity Entity;
        public uint Generation;
        public byte InUse;        // bool-as-byte for consistency
    }
    private HandleSlot[] _handleSlots = new HandleSlot[256];
    private Stack<int> _freeHandleIndices = new();
    private int _nextFreshIndex;
}
```

This class is tiny — handle table plus a few fields. All per-entity
state lives in the `FakeAnimBackendState` component on each entity.

### 3.1 `Initialize`

```csharp
public void Initialize(AnimationBackendConfig config)
{
    _repo = config.EntityRepository;

    if (!_repo.IsComponentTypeRegistered<FakeAnimBackendState>())
        _repo.RegisterComponentType<FakeAnimBackendState>();
}
```

The component is registered idempotently. If a future Stride backend
ever runs side-by-side with the fake (e.g. for cross-backend
diagnostic comparison), each registers its own state component
independently.

### 3.2 `RegisterEntity` / `UnregisterEntity`

```csharp
public AnimationBackendHandle RegisterEntity(EntityId entity, in CharacterAnimationDefRuntime def)
{
    int idx;
    if (_freeHandleIndices.Count > 0)
        idx = _freeHandleIndices.Pop();
    else
    {
        if (_nextFreshIndex >= _handleSlots.Length)
            Array.Resize(ref _handleSlots, _handleSlots.Length * 2);
        idx = _nextFreshIndex++;
    }

    uint gen = _nextGeneration++;
    _handleSlots[idx] = new HandleSlot { Entity = entity, Generation = gen, InUse = 1 };

    // Initialize the state component.
    var state = default(FakeAnimBackendState);
    state.Generation = gen;
    state.Stance.CurrentStance = def.SupportedStances[0];
    state.Stance.TargetStance = state.Stance.CurrentStance;
    _repo.AddComponent(entity, state);

    return new AnimationBackendHandle { Index = idx, Generation = gen };
}

public void UnregisterEntity(AnimationBackendHandle handle)
{
    if (!TryResolve(handle, out var entity)) return;
    if (_repo.HasComponent<FakeAnimBackendState>(entity))
        _repo.RemoveComponent<FakeAnimBackendState>(entity);
    _handleSlots[handle.Index] = default;
    _freeHandleIndices.Push(handle.Index);
}

private bool TryResolve(AnimationBackendHandle h, out Entity entity)
{
    if (h.Index < 0 || h.Index >= _handleSlots.Length) { entity = default; return false; }
    var slot = _handleSlots[h.Index];
    if (slot.InUse == 0 || slot.Generation != h.Generation) { entity = default; return false; }
    entity = slot.Entity;
    return true;
}
```

### 3.3 Slot operations — montages

```csharp
public void PlayMontageOnSlot(AnimationBackendHandle h, SlotId slot,
                               MontageAssetId montage, float blendIn,
                               float playRate, byte startSection)
{
    if (!TryResolve(h, out var entity)) return;

    var def = _repo.GetComponentRO<CharacterAnimationDefRuntime>(entity);
    if (!def.TryGetMontageInfo(montage, out var info))
        return;  // unknown montage — dispatcher should have caught this

    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);

    // Pattern A — Span-cast for safe inline-array mutation.
    Span<FakeSlotState> slots = state.Slots;
    ref var s = ref slots[slot.Value];

    s.IsActive = 1;
    s.ActiveMontage = montage;
    s.ElapsedSeconds = 0;
    s.TotalDurationSeconds = info.DurationSeconds;
    s.BlendInTime = (blendIn >= 0) ? blendIn : info.DefaultBlendInTime;
    s.BlendOutTime = info.DefaultBlendOutTime;
    s.PlayRate = playRate;
    s.CurrentSectionIndex = startSection;
    s.InBlendOutWindow = 0;
    s.BlendWeight = 0;
    s.FiredNotifyMask = 0;
}

public void CrossfadeMontageOnSlot(AnimationBackendHandle h, SlotId slot,
                                    MontageAssetId next, float crossfade,
                                    float playRate, byte startSection)
{
    // Fake doesn't visually blend (no rendering). Crossfade is mechanically
    // identical to a Play with the crossfade time as the blend-in. The slot
    // immediately becomes the new montage's; the previous montage's
    // blend-out window finishes implicitly as the slot's record is replaced.
    PlayMontageOnSlot(h, slot, next, crossfade, playRate, startSection);
}

public void StopMontageOnSlot(AnimationBackendHandle h, SlotId slot, float blendOut)
{
    if (!TryResolve(h, out var entity)) return;

    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);
    Span<FakeSlotState> slots = state.Slots;
    ref var s = ref slots[slot.Value];

    if (s.IsActive == 0) return;

    // Force into blend-out: rewind elapsed so the next Tick(s) complete the
    // blend-out and end the montage naturally.
    s.BlendOutTime = blendOut;
    s.ElapsedSeconds = MathF.Max(s.ElapsedSeconds, s.TotalDurationSeconds - blendOut);
    s.InBlendOutWindow = 1;
}

public MontagePlaybackState QuerySlotState(AnimationBackendHandle h, SlotId slot)
{
    if (!TryResolve(h, out var entity)) return default;
    var state = _repo.GetComponentRO<FakeAnimBackendState>(entity);
    Span<FakeSlotState> slots = state.Slots;
    var s = slots[slot.Value];
    if (s.IsActive == 0) return default;
    return new MontagePlaybackState
    {
        ActiveMontage = s.ActiveMontage,
        ElapsedSeconds = s.ElapsedSeconds,
        TotalDurationSeconds = s.TotalDurationSeconds,
        CurrentSectionIndex = s.CurrentSectionIndex,
        InBlendOutWindow = s.InBlendOutWindow != 0,
        BlendWeight = s.BlendWeight,
    };
}
```

Note on the Span-cast usage: even though the body assigns each field
explicitly (no in-place mutation of a partial struct), `Span<T>` is
used uniformly because (a) it's the established pattern in this
codebase and (b) future edits to add "read then conditionally update"
patterns won't require revisiting the access path.

### 3.4 Slot operations — locomotion, aim, stance

```csharp
public void UpdateLocomotionInputs(AnimationBackendHandle h, Vector2 hv,
                                    float vv, bool grounded)
{
    if (!TryResolve(h, out var entity)) return;
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);
    state.LocalHorizontalVelocity = hv;
    state.HorizontalSpeed = hv.Length();
    state.VerticalVelocity = vv;
    state.IsGrounded = grounded ? (byte)1 : (byte)0;
}

public void SetAimTarget(AnimationBackendHandle h, Vector3 worldAim,
                          float blendIn, byte priority)
{
    if (!TryResolve(h, out var entity)) return;
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);

    bool firstAcquire = state.Aim.IsActive == 0;
    state.Aim.IsActive = 1;
    state.Aim.IsReleasing = 0;
    state.Aim.TargetWorldAimPoint = worldAim;
    if (firstAcquire) state.Aim.WorldAimPoint = worldAim;
    state.Aim.BlendInTime = blendIn;
    state.Aim.Priority = priority;
}

public void ReleaseAim(AnimationBackendHandle h, float blendOut)
{
    if (!TryResolve(h, out var entity)) return;
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);
    state.Aim.IsReleasing = 1;
    state.Aim.BlendOutTime = blendOut;
}

public void RequestStanceChange(AnimationBackendHandle h, StanceId from,
                                 StanceId to, float blendTime)
{
    if (!TryResolve(h, out var entity)) return;
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);
    state.Stance.TargetStance = to;
    state.Stance.IsTransitioning = 1;
    state.Stance.TransitionProgress = 0;
    state.Stance.TransitionTotalSeconds = blendTime;
}

public StanceTransitionState QueryStanceTransition(AnimationBackendHandle h)
{
    if (!TryResolve(h, out var entity)) return default;
    var state = _repo.GetComponentRO<FakeAnimBackendState>(entity);
    return new StanceTransitionState
    {
        CurrentStance = state.Stance.CurrentStance,
        TargetStance = state.Stance.TargetStance,
        IsTransitioning = state.Stance.IsTransitioning != 0,
        TransitionProgress = state.Stance.TransitionProgress,
    };
}
```

### 3.5 Notify draining

```csharp
public int DrainNotifies(AnimationBackendHandle h, Span<RawNotifyEvent> dest)
{
    if (!TryResolve(h, out var entity)) return 0;
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);

    int n = Math.Min(state.PendingNotifyCount, dest.Length);
    Span<RawNotifyEvent> src = state.PendingNotifies;
    for (int i = 0; i < n; i++) dest[i] = src[i];

    // Shift remaining notifies (if dest was too small).
    int remaining = state.PendingNotifyCount - n;
    if (remaining > 0)
    {
        for (int i = 0; i < remaining; i++) src[i] = src[n + i];
    }
    state.PendingNotifyCount = (byte)remaining;
    return n;
}
```

`NotifyEventEmitterSystem` (DD-1 §11) calls this once per entity per
tick with a 16-element `Span`, draining the buffer fully under normal
operation. The "if dest was too small" path is defensive — in
practice the system's drain buffer matches the fake's capacity.

### 3.6 Metrics

```csharp
public AnimationBackendMetrics SnapshotMetrics()
{
    int active = 0;
    for (int i = 0; i < _nextFreshIndex; i++)
        if (_handleSlots[i].InUse != 0) active++;
    return new AnimationBackendMetrics
    {
        ActiveEntityCount = active,
        TotalTicks = _tickCount,
    };
}
```

## 4. The tick algorithm

`Tick(deltaSeconds)` is called once per simulation tick by
`AnimationRuntimeBridgeSystem` (DD-1 §10).

Per architect ruling (Q3 ack), the implementation uses an **ECS
query** over `FakeAnimBackendState` rather than iterating the handle
table directly. This fits the engine's standard system pattern and
gets free benefits from chunk-versioning + delta queries.

```csharp
public void Tick(float deltaSeconds)
{
    _tickCount++;

    // ECS query: every entity with both FakeAnimBackendState and
    // CharacterAnimationDefRuntime. The TKB translator (DD-4 §4)
    // ensures both components are present on humanoid entities.
    var query = _repo.CreateQuery()
        .With<FakeAnimBackendState>()
        .With<CharacterAnimationDefRuntime>()
        .Build();

    foreach (var entity in query)
    {
        TickEntity(entity, deltaSeconds);
    }
}

private void TickEntity(Entity entity, float dt)
{
    ref var state = ref _repo.GetComponentRW<FakeAnimBackendState>(entity);
    var def = _repo.GetComponentRO<CharacterAnimationDefRuntime>(entity);

    state.TotalTicks++;

    // Advance each active slot.
    Span<FakeSlotState> slots = state.Slots;
    for (int i = 0; i < slots.Length; i++)
    {
        if (slots[i].IsActive == 0) continue;
        AdvanceSlot(ref slots[i], def, ref state, dt);
    }

    // Advance aim, stance, footsteps.
    AdvanceAim(ref state.Aim, dt);
    AdvanceStance(ref state.Stance, dt);
    AdvanceFootsteps(ref state, dt);
}
```

### 4.1 Slot advance

```csharp
private void AdvanceSlot(ref FakeSlotState s, CharacterAnimationDefRuntime def,
                          ref FakeAnimBackendState state, float dt)
{
    float prevElapsed = s.ElapsedSeconds;
    s.ElapsedSeconds += dt * s.PlayRate;

    // Compute blend weight.
    if (s.ElapsedSeconds < s.BlendInTime)
        s.BlendWeight = (s.BlendInTime > 0) ? s.ElapsedSeconds / s.BlendInTime : 1f;
    else if (s.ElapsedSeconds > s.TotalDurationSeconds - s.BlendOutTime)
    {
        float remaining = s.TotalDurationSeconds - s.ElapsedSeconds;
        s.BlendWeight = (s.BlendOutTime > 0) ? MathF.Max(0, remaining / s.BlendOutTime) : 0f;
        s.InBlendOutWindow = 1;
    }
    else
    {
        s.BlendWeight = 1f;
    }

    // Fire any notifies whose TimeSeconds was crossed this tick.
    if (def.TryGetMontageInfo(s.ActiveMontage, out var info))
    {
        for (int i = 0; i < info.Notifies.Count; i++)
        {
            ulong bit = 1UL << i;
            if ((s.FiredNotifyMask & bit) != 0) continue;     // already fired
            var notify = info.Notifies[i];
            if (s.ElapsedSeconds >= notify.TimeSeconds &&
                prevElapsed < notify.TimeSeconds)
            {
                EmitNotify(ref state, new RawNotifyEvent
                {
                    Montage = s.ActiveMontage,
                    MarkerHash = notify.MarkerHash,
                    Kind = notify.Kind,
                    PayloadFloat = notify.PayloadFloat,
                    PayloadByte = notify.PayloadByte,
                });
                s.FiredNotifyMask |= bit;
            }
        }
    }

    // End the montage if elapsed reached duration.
    if (s.ElapsedSeconds >= s.TotalDurationSeconds)
    {
        s.IsActive = 0;
        s.ActiveMontage = default;
        s.ElapsedSeconds = 0;
        s.TotalDurationSeconds = 0;
        s.BlendWeight = 0;
        s.InBlendOutWindow = 0;
        s.FiredNotifyMask = 0;
    }
}
```

### 4.2 Aim advance

```csharp
private void AdvanceAim(ref FakeAimState aim, float dt)
{
    if (aim.IsActive == 0) return;

    if (aim.IsReleasing != 0)
    {
        float step = (aim.BlendOutTime > 0) ? dt / aim.BlendOutTime : 1f;
        aim.BlendWeight = MathF.Max(0, aim.BlendWeight - step);
        if (aim.BlendWeight == 0)
        {
            aim.IsActive = 0;
            aim.IsReleasing = 0;
        }
    }
    else
    {
        float step = (aim.BlendInTime > 0) ? dt / aim.BlendInTime : 1f;
        aim.BlendWeight = MathF.Min(1, aim.BlendWeight + step);
        aim.WorldAimPoint = aim.TargetWorldAimPoint;  // fake doesn't smoothly interpolate
    }
}
```

The fake doesn't smoothly lerp `WorldAimPoint` toward
`TargetWorldAimPoint` — it snaps. A real backend with rendering would
interpolate. For AI behavior testing, only the
"is the aim acquired / released / still active" semantics matter,
which the fake reproduces.

### 4.3 Stance advance

```csharp
private void AdvanceStance(ref FakeStanceState stance, float dt)
{
    if (stance.IsTransitioning == 0) return;
    float step = (stance.TransitionTotalSeconds > 0)
        ? dt / stance.TransitionTotalSeconds
        : 1f;
    stance.TransitionProgress = MathF.Min(1, stance.TransitionProgress + step);
    if (stance.TransitionProgress >= 1)
    {
        stance.CurrentStance = stance.TargetStance;
        stance.IsTransitioning = 0;
        // StanceChangedEvent is synthesized by StanceTransitionSystem
        // (DD-1 §9) from observing this state transition, not by the
        // backend.
    }
}
```

### 4.4 Notify emission helper

```csharp
private static void EmitNotify(ref FakeAnimBackendState state, RawNotifyEvent ev)
{
    // §6: hard assert on overflow.
    if (state.PendingNotifyCount >= 16)
    {
        throw new InvalidOperationException(
            $"FakeAnimationBackend notify buffer overflow: " +
            $"attempting to queue 17th event in one drain cycle. " +
            $"PendingNotifyCount={state.PendingNotifyCount}, buffer capacity=16. " +
            $"This indicates either NotifyEventEmitterSystem is not draining " +
            $"each tick, or the tick algorithm produced more notifies than " +
            $"the mathematical maximum (8 slots × 1 keyframe + 1 footstep = 9). " +
            $"Investigate immediately.");
    }
    Span<RawNotifyEvent> buf = state.PendingNotifies;
    buf[state.PendingNotifyCount] = ev;
    state.PendingNotifyCount++;
}
```

## 5. Synthetic footstep emission

Per your Q1 confirmation, the fake emits synthetic `FootstepEvent`s
when the character is moving. Cadence is distance-based (matches how
real footstep cadence works — faster movement = more footsteps per
second).

```csharp
namespace Hrot.MuscleCharacter.Animation.Fake;

public static class FakeBackendConstants
{
    /// <summary>Below this horizontal speed (m/s), no footsteps emit.
    /// Standing or slow shuffle.</summary>
    public const float MinFootstepSpeed = 0.3f;

    /// <summary>Distance (m) between consecutive footsteps. Real human
    /// stride is ~0.7-1.2m depending on speed; the fake uses a constant
    /// for simplicity.</summary>
    public const float FootstepStrideMeters = 0.9f;
}

private void AdvanceFootsteps(ref FakeAnimBackendState state, float dt)
{
    if (state.IsGrounded == 0 ||
        state.HorizontalSpeed < FakeBackendConstants.MinFootstepSpeed)
    {
        // Optional: bleed off accumulated distance so a long stop doesn't
        // produce a footstep on the first step after standing still.
        state.DistanceSinceLastFootstep = 0;
        return;
    }

    state.DistanceSinceLastFootstep += state.HorizontalSpeed * dt;
    if (state.DistanceSinceLastFootstep >= FakeBackendConstants.FootstepStrideMeters)
    {
        EmitNotify(ref state, new RawNotifyEvent
        {
            Kind = AnimNotifyCategory.Footstep,
            PayloadByte = state.NextFootIndex,
            // PayloadVector intentionally left zero — the fake doesn't
            // know the entity's world position. NotifyEventEmitterSystem
            // fills in WorldPosition from the entity's SimTransform when
            // translating to the typed FootstepEvent.
        });
        state.NextFootIndex = (byte)(1 - state.NextFootIndex);
        state.DistanceSinceLastFootstep = 0;
    }
}
```

Note on the `PayloadVector` zero: DD-1's `RawNotifyEvent` carries a
`Vector3 PayloadVector` field that the typed `FootstepEvent`
(DD-3 §3.2) maps to `WorldPosition`. The fake doesn't know the
entity's world position (that's in `SimTransform`, not the fake's
state). `NotifyEventEmitterSystem` is the one that publishes the typed
event and has access to `SimTransform` via the entity reference;
filling `WorldPosition` from `SimTransform.Position` there closes the
loop. This is a small change to DD-1 §11 — flagged in §11.

## 6. Hard-assert discipline

Per your decision, the fake asserts hard on conditions that indicate a
bug. This catches problems during development before they
silently produce wrong AAR recordings or wrong behaviors.

Cases that hard-assert (throw `InvalidOperationException` with a
detailed message):

- **Notify buffer overflow** (§4.4 `EmitNotify`) — 17th notify in one
  tick. Mathematical maximum is 9; hitting 17 means a bug.
- **Stale handle access** — handled by `TryResolve` returning false;
  no assert here because stale-handle-after-unregister is a legitimate
  race that the engine's standard handle pattern handles gracefully.
- **Slot index out of range** — would already throw via Span bounds
  check; explicit assert is redundant.

Assert messages are detailed, naming the invariant violated and a
hypothesis about what went wrong. The developer who hits the assert
should have most of the bug investigation done by reading the message.

The assertions are present in all builds, not just debug. The cost is
trivial (a single branch on each `EmitNotify`), and silent corruption
in a debug-only assert path is worse than the cost.

## 7. The diagnostic ImGui window

A standalone ImGui window registered with the engine's window manager.
Opt-in (off by default; developer toggles via the standard
window-menu). Has two views:

### 7.1 List view

Table of all entities currently registered with the fake backend.
Columns:

| Column | Source | Notes |
|---|---|---|
| Entity ID | handle table | hex format |
| TKB Class | `EntityInfo.ClassName` or similar | from existing engine convention |
| Active Slots | count of `FakeSlotState.IsActive != 0` | "3/8" format |
| Aim | `FakeAimState.IsActive`, releasing/holding | brief text |
| Stance | `FakeStanceState.CurrentStance`, transition state | enum + arrow if transitioning |
| Speed | `HorizontalSpeed` | m/s, 1 decimal |
| Last Tick | `TotalTicks` | for sanity check |

Click an entity row → switch to detail view for that entity.

### 7.2 Detail view (per entity)

Per-slot panel:

- Slot index + slot name (from `CharacterAnimationDefRuntime.SlotTable`)
- Active montage name (resolved from `MontageAssetId` → name via the
  same hash-to-name lookup the editor uses, DD-4 §3.4)
- Progress bar: elapsed / total
- Blend weight (numeric + small bar)
- In-blend-out flag
- Fired notifies bitmask (visualized as a row of LEDs: lit = fired,
  unlit = pending; hover for marker name)

Aim panel:

- IsActive, IsReleasing
- Target world point (vector formatted)
- Current world point
- Blend weight

Stance panel:

- Current stance (enum)
- Target stance (only if transitioning)
- Transition progress bar

Locomotion panel:

- Horizontal speed
- Local velocity vector
- Vertical velocity
- Grounded flag
- Distance since last footstep
- Next foot index

Pending notifies queue:

- Read-only list of currently-queued `RawNotifyEvent`s
- Should usually be empty (drained each tick); if non-empty, that's a
  diagnostic clue

**"Copy JSON Snapshot to Clipboard" button at the top of the detail
view.** Produces the JSON described in §8.

### 7.3 Window registration via `IWindowRegistrar`

Per architect ruling on §11.2, the diagnostic window is registered
through the engine's standard `IWindowRegistrar` pattern. The host
subsystem (the Muscle Character node's host subsystem, analogous to
`SimHostSubsystem`) implements `IWindowRegistrar`. During its
`RegisterWindows(IWindowManager)` callback, it instantiates the
fake-backend inspector window and calls
`windowManager.RegisterWindow(...)`:

```csharp
namespace Hrot.MuscleCharacter.Animation.Fake.Diagnostics;

public sealed class FakeAnimBackendInspectorWindow : IDiagnosticWindow
{
    public string Title => "Fake Animation Backend";
    public string MenuCategory => "Animation";
    public bool IsVisible { get; set; }

    private Entity _selectedEntity;
    private bool _visible;
    private readonly EntityRepository _repo;
    private readonly ITkbDatabase _tkb;        // for hash → name lookups

    public FakeAnimBackendInspectorWindow(EntityRepository repo, ITkbDatabase tkb)
    {
        _repo = repo;
        _tkb = tkb;
    }

    public void Draw(IDiagnosticContext ctx)
    {
        if (!IsVisible) return;
        if (ImGui.Begin(Title, ref _visible))
        {
            if (_selectedEntity == default)
                DrawListView(ctx);
            else
                DrawDetailView(ctx, _selectedEntity);
        }
        ImGui.End();
    }

    // ... DrawListView, DrawDetailView, JSON snapshot button
}

// Registration: the Muscle Character host subsystem
public sealed class MuscleCharacterHostSubsystem : ISubsystem, IWindowRegistrar
{
    public void RegisterWindows(IWindowManager windowManager)
    {
        // Headless guard — only register UI in non-headless builds, per
        // the engine convention noted in Hrot-project-docs.txt.
        if (!_headless)
        {
            var window = new FakeAnimBackendInspectorWindow(_repo, _tkb);
            windowManager.RegisterWindow(window);
        }
    }
    // ... other ISubsystem methods
}
```

The category string `"Animation"` groups the fake-backend window
alongside any other future animation-related diagnostic windows in
the engine's dev tools menu.

The window's visibility is toggled by the developer through the
standard window-menu pattern; default is hidden.

## 8. JSON snapshot export

The "Copy JSON Snapshot to Clipboard" button serializes the entity's
full fake-backend state into JSON and copies it to the clipboard.
Designed for pasting into bug reports and Slack threads.

The JSON structure:

```jsonc
{
  "snapshot_time": "2026-05-26T14:32:17.123Z",
  "entity_id": "0x000A_0001",
  "tkb_class": "Sniper",
  "fake_backend": {
    "generation": 17,
    "total_ticks": 4823,
    "slots": [
      {
        "index": 0,
        "slot_name": "Locomotion",
        "is_active": false
      },
      {
        "index": 1,
        "slot_name": "FullBody",
        "is_active": true,
        "active_montage": { "id": "0xA1B2C3D4", "name": "Vault_Low" },
        "elapsed_seconds": 0.42,
        "total_duration_seconds": 1.2,
        "blend_in_time": 0.1,
        "blend_out_time": 0.15,
        "play_rate": 1.0,
        "current_section_index": 1,
        "current_section_name": "Vault",
        "in_blend_out_window": false,
        "blend_weight": 1.0,
        "fired_notifies": [
          { "index": 0, "name": "Footstep_Left", "time_seconds": 0.3 }
        ],
        "pending_notifies": [
          { "index": 1, "name": "Land", "time_seconds": 1.0 }
        ]
      }
      // ... slot indices 2-7 omitted if inactive
    ],
    "aim": {
      "is_active": true,
      "is_releasing": false,
      "target_world_point": [123.4, 0.5, -67.8],
      "current_world_point": [123.4, 0.5, -67.8],
      "blend_in_time": 0.1,
      "blend_out_time": 0.2,
      "priority": 0,
      "blend_weight": 1.0
    },
    "stance": {
      "current": "Crouched",
      "target": "Crouched",
      "is_transitioning": false,
      "transition_progress": 0.0
    },
    "locomotion": {
      "horizontal_speed_mps": 2.3,
      "local_velocity": [2.3, 0.0],
      "vertical_velocity": 0.0,
      "is_grounded": true,
      "distance_since_last_footstep_m": 0.55,
      "next_foot_index": 1
    }
  },
  "related_state": {
    // Convenience inclusion of the related ECS components from DD-1
    // for context. Optional; the fake's state alone is the primary payload.
    "animation_channel": { "active_action": "PlayMontage", "status": "Running", ... },
    "animation_montage_queue": { "count": 0 },
    "stance_intent": { "target_stance": "Crouched", "version": 3 },
    "stance_status": { "current_stance": "Crouched", "phase": "Completed", ... }
  }
}
```

Implementation: the engine's `FdpAutoSerializer` already serializes
unmanaged components for AAR recording. A JSON-sink variant or a
custom small writer produces the human-readable form, with name
resolution for `MontageAssetId` and `MarkerHash` via the same TKB
lookup the editor uses (DD-4 §5).

The "related_state" section is convenience — pulls the contractual
components from DD-1 so the snapshot is self-contained for bug
reports. Optional in the v1 implementation; can be added later if
useful.

## 9. AAR recording integration

`FakeAnimBackendState` is a Tier 1 unmanaged component with
`[ComponentId]` and `[DataPolicy(DataPolicy.NoSave)]`. By engine
convention:

- The flight recorder captures it via the standard raw-memory-copy
  fast path (no reflection).
- The schema validator hashes its layout and validates recordings
  against the current binary.
- `DataPolicy.NoSave` excludes it from scenario save files — it's
  runtime state, not authoring data.
- Recording is on by default; the policy doesn't suppress it.

When a developer opens a recording in the replay browser, the
entity inspector shows `FakeAnimBackendState` alongside every other
component, with its fields drilled down. The diagnostic window
described in §7 also works on replayed states (reading from the
playback world rather than the live world), giving the same
inspection experience for post-mortem debugging.

`ComponentDiffService` (engine's existing diff tool) can diff the
component between recordings, surfacing animation backend
non-determinism if it ever happens.

## 10. What the fake intentionally doesn't do

For clarity, explicit non-features:

- **No skeletal pose evaluation.** No bones, no IK, no blend space
  computation. The fake produces *state*, not *poses*.
- **No rendering.** Obviously.
- **No real crossfade.** `CrossfadeMontageOnSlot` is mechanically
  identical to `PlayMontageOnSlot`. A visual difference would only
  matter if there were rendering.
- **No aim-point interpolation.** Aim point snaps to target; only the
  blend-weight ramps. Sufficient for AI behavior testing of
  "is aim active / acquired."
- **No locomotion blend-space output.** `UpdateLocomotionInputs` is
  stored for footstep cadence and diagnostics but doesn't drive a
  visible blend.
- **No content load.** Montage assets are looked up by
  `MontageAssetId` from the TKB-baked
  `CharacterAnimationDefRuntime` — the fake reads the same authored
  data the real backend will. No assets are loaded, no clips
  evaluated.
- **No threading.** All `IAnimationBackend` methods are called from
  the main simulation thread per DD-1 §3 contract; the fake doesn't
  need synchronization.
- **No save-to-disk.** Per `DataPolicy.NoSave`. The fake's state is
  runtime-only; scenario saves don't include it.

## 11. Resolutions summary (from v1.0 review)

All four open questions from DD-Fake v1.0 received architect
rulings. Where rulings confirmed v1.0 leanings, no body section
needed revision; the resolution status is recorded here for
traceability. Where they triggered material changes, the relevant
body section is updated.

### 11.1 ✅ `[ComponentId]` block allocation for animation components

**Resolved.** The architect officially allocates the **220-249**
block from the reserved 200-255 range for all animation
components, including `FakeAnimBackendState`, the future
`StrideAnimBackendState`, and DD-1's contractual components
(`AnimationChannel`, `LookAtChannel`, `StanceIntent`, `StanceStatus`,
`AnimationMontageQueue`, `AnimationMontageQueueState`,
`CharacterAnimationDefRuntime`, `AnimationExecutorState`,
`LookAtExecutorState`).

Specific assignments are made by the implementer at implementation
time within the 220-249 block. This DD pins `FakeAnimBackendState`
at ID 240 (§2); other components claim their IDs as they're added to
`GlobalComponentIds.cs`. Thirty IDs are comfortably more than the
~11 currently known plus the future Stride state, leaving headroom.

### 11.2 ✅ Diagnostic window registration mechanism

**Resolved.** Use the engine's `IWindowRegistrar` pattern. The host
subsystem (analogous to `SimHostSubsystem`) implements
`IWindowRegistrar`. Its `RegisterWindows(IWindowManager)` callback
instantiates the diagnostic window and registers it via
`windowManager.RegisterWindow(...)`. Reflected in §7.3.

### 11.3 ✅ `RawNotifyEvent.PayloadVector` zero-from-fake convention

**Resolved.** The fake leaves `PayloadVector = 0` on footstep events;
`NotifyEventEmitterSystem` (DD-1 §11) enriches the typed
`FootstepEvent` with `WorldPosition` from the entity's
`SimTransform`. The architect confirms this enrichment fits the
emitter's existing role of translating `RawNotifyEvent` to typed
events with `Target = self`. Reflected (already) in §5.

This is a small implicit clarification to DD-1 §11's responsibility
description, not a contract change.

### 11.4 ✅ Strict-oracle stance — relaxed?

**Resolved.** The fake backend is strictly a best-effort
approximation, not an authoritative oracle. Cross-backend test
parity between the fake and the future Stride or proprietary
backends is **not** a goal or requirement. Reflected in §1
Principle 1 and §10.

---

**No residual open questions remain.** DD-Fake is fully resolved and
approved for implementation.

---

## Summary

DD-Fake v1.1 specifies the initial implementation of
`IAnimationBackend`: a fake backend that processes animation state
deterministically without 3D rendering. All per-entity working state
lives in a single Tier 1 unmanaged ECS component
(`FakeAnimBackendState`, ~1 KB) using `[InlineArray]` for slot table
and notify buffer, with a 64-bit bitmask per slot tracking fired
notifies. The backend class is thin — mostly handle-table
bookkeeping; the tick algorithm reads/writes the state component
via an ECS query. Synthetic footstep events emit at distance-based
cadence when grounded and moving. Hard-assert on notify buffer
overflow catches algorithmic bugs immediately. An ImGui diagnostic
window registered via the `IWindowRegistrar` pattern, with list +
detail views and clipboard JSON snapshot export, provides
developer-facing introspection. AAR recording integration is free
because the state is a regular Tier 1 component.

All four v1.0 open questions resolved per architect review.
ComponentId block 220-249 allocated for all animation components.
Best-effort-approximation stance confirmed (no cross-backend test
parity goals).

---

*End of DD-Fake v1.1. Architect-approved for implementation.*
