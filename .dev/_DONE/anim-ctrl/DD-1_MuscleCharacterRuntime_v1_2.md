# DD-1 — Muscle Character Runtime — Detailed Design (v1.2)

> **Status:** Architect-approved detailed design for the Muscle Character
> node's animation runtime. Builds on the architect-approved
> `AnimationControl_BrainMuscle_MiniDesign_v0_2` ("v0.2 mini design") and is
> the first of five detailed-design documents splitting that mini design
> across implementation teams.
> **Changes from v1.1:** Cross-DD alignment pass per DD-5 §13 and
> architect authorization. The local `NotifyKind` enum declaration in
> §3 replaced with reference to the canonical `AnimNotifyCategory`
> declared in DD-3 §2. Values unchanged; the discriminator field on
> `RawNotifyEvent` now uses the canonical type for consistency across
> DD-1, DD-3, and DD-4. No other changes.
> **Audience:** Muscle Character implementation team (primary), engine
> architect (sign-off), AI editor team (informational — DD-5 consumes these
> contracts).
> **Scope:** Everything that runs on the Muscle node to execute the
> animation contract specified in the v0.2 mini design. Includes: the
> `IAnimationBackend` abstraction and its Stride implementation, the slot
> system, the dispatcher/executor systems for `AnimationChannel` and
> `LookAtChannel`, the montage queue advance mechanism, stance transition
> driving, notify event emission onto `FdpEventBus`, capability gating,
> and phase ordering with respect to existing Muscle systems.
> **Out of scope:** Network replication of channel components and
> side-buffers (DD-2). Engine Event Catalog registrations (DD-3). TKB
> descriptor schema and translator (DD-4 — but DD-1 references the runtime
> components the translator must inject). Blueprint authoring primitives
> (DD-5). UE-to-engine asset import pipeline. Future root-motion-drives-
> kinematics implementation (recorded as Muscle-side future work; channel
> contracts unchanged).
> **Reads alongside:** v0.2 mini design, `Architect's response`
> (`Animations-response-1`), `Predicate-Infrastructure-Capabilities.md`,
> `Blueprint_Subsystem_Runtime_Detailed_Design.md` (for channel dispatcher
> patterns).

---

## Table of contents

1. Design principles and constraints
2. System roster and phase placement
3. The `IAnimationBackend` abstraction
4. Slot system — production-complete design
5. ECS components introduced by DD-1
6. `AnimationDispatcherSystem`
7. `MontageQueueAdvanceSystem`
8. `LookAtDispatcherSystem`
9. `StanceTransitionSystem`
10. `AnimationRuntimeBridgeSystem` — the IAnimationBackend driver
11. `NotifyEventEmitterSystem` and the local FdpEventBus contract
12. Capability gating — where each check fires
13. Mid-action capability loss handling
14. Per-entity state and lifecycle
15. The Stride backend implementation (initial)
16. Designing for the proprietary backend swap
17. Phase ordering with existing Muscle systems
18. Future root-motion path — what changes here
19. Error handling and degenerate cases
20. Open questions for review

---

## 1. Design principles and constraints

Three load-bearing principles drive every choice in this document.

**Principle 1: The animation backend is swappable.** Stride is the initial
target; the proprietary engine is coming. The Muscle Character runtime is
designed against an `IAnimationBackend` abstraction (§3) and never reaches
into Stride APIs directly outside the `Stride*` implementation classes.
This is *thick* abstraction by architect's call (v0.2 §7) — the backend
interface is rich enough to express slotted playback, blend masks, notify
callbacks, and per-bone aim layering, even if the initial Stride
implementation has to synthesize some of those capabilities on top of
Stride's lower-level `IBlendTreeBuilder` API.

**Principle 2: Unreal-shaped vocabulary all the way down.** Animation
content is authored in UE; AI designers write `PlayMontage` /
`SetStance` / `LookAt`. The Muscle's internal vocabulary matches: slots,
montages, montage sections, blend in/out times, notify events. The
runtime doesn't impose Stride's `AnimationComponent.Play(name)` shape on
authors; it adapts to it inside the backend.

**Principle 3: Polling-friendly ECS, callback-friendly backend.** The
Muscle's ECS systems are polling per the engine's standard pattern. The
animation backend produces events (notify-fired, montage-ended) that must
be drained into ECS-visible state each tick. The bridge owns this
ECS-poll/backend-callback impedance match.

Three hard constraints from v0.2 and from architect feedback that are
non-negotiable in this design:

- The 96-byte `MaxChannelSizeBytes` budget on channel components. Anything
  that doesn't fit goes into a separate component.
- The `[InlineArray]` mutation hazard — every component touching an
  inline array uses Span-cast or Get→Mutate→`SetComponent`. Direct ref
  index-assignment is forbidden.
- Single-writer principle for spatial descriptors (`SimTransform`,
  `SimVelocity`). The current iteration does *not* break this (kinematics
  drives animation, not the reverse); future root-motion work will, but
  with the per-entity-class authority-routing path the architect
  identified.

## 2. System roster and phase placement

The Muscle Character node adds the following systems. Naming follows the
existing `XxxDispatcherSystem` / `XxxExecutorSystem` / `XxxSystem`
conventions used elsewhere in the engine.

| System | Phase | Reads | Writes |
|---|---|---|---|
| `AnimationDispatcherSystem` | `PreSimulation` | `AnimationChannel`, `ActorCapabilityState`, `CharacterAnimationDefRuntime` | `AnimationChannel.Status`, `AnimationChannel.DispatchedInstanceId`, `AnimationExecutorState` |
| `MontageQueueAdvanceSystem` | `Simulation` (early) | `AnimationMontageQueue`, `AnimationExecutorState` | `AnimationMontageQueueState`, `AnimationExecutorState` |
| `LookAtDispatcherSystem` | `PreSimulation` | `LookAtChannel`, `ActorCapabilityState` | `LookAtChannel.Status`, `LookAtChannel.DispatchedInstanceId`, `LookAtExecutorState` |
| `StanceTransitionSystem` | `PreSimulation` | `StanceIntent`, `StanceStatus`, `ActorCapabilityState` | `StanceStatus`, internal stance-transition montage requests |
| `AnimationRuntimeBridgeSystem` | `Simulation` (mid) | `AnimationExecutorState`, `LookAtExecutorState`, `StanceStatus`, `SimTransform`, `SimVelocity` | `AnimationBackendHandle` (per-entity), submits backend update calls |
| `NotifyEventEmitterSystem` | `PostSimulation` (early) | per-entity backend notify queue (drained from backend) | publishes onto Muscle's local `FdpEventBus` |
| `AnimationStateReporterSystem` | `PostSimulation` (late) | backend playback state | `AnimationChannel.Status` updates for natural completion, `AnimationMontageQueueState`, fires synthesized events (e.g. `MontageEndedEvent` on natural end) |
| `AnimationBackendCleanupSystem` | `PostSimulation` (late) | `PendingDestroy`, `CharacterAnimationDefRuntime` | calls `backend.UnregisterEntity`, clears `BackendHandle` |

The engine's existing capability-change reactor (`PreSimulation`) is
extended in this iteration to handle high→low transitions of
`CanPlayAnimations`, `CanAim`, and `CanChangeStance`. See §13. No new
system is introduced for this.

Three observations on this layout:

The **dispatchers run in `PreSimulation`** matching the existing channel
dispatcher pattern — they observe `ActionInstanceId`/`DispatchedInstanceId`
mismatches and set up executor state before the simulation tick that uses
that state.

The **`MontageQueueAdvanceSystem` runs early in `Simulation`** so the
executor state reflects this tick's queue advance before the
`AnimationRuntimeBridgeSystem` submits commands to the backend mid-tick.

The **`NotifyEventEmitterSystem` and `AnimationStateReporterSystem` run
in `PostSimulation`** so they drain the backend's post-update state
(notifies that fired this tick, montages that completed) and translate
them into bus events and ECS state changes. `AnimationStateReporterSystem`
runs *late* in `PostSimulation` so that its `Status = Success` writes
happen after `TransformSyncSystem` has done its work — preserving the
phase-ordering invariants the architect flagged in Q7 for the future
root-motion case (no contention for `SimTransform` writes in *this*
iteration, but the placement is right for that future too).

## 3. The `IAnimationBackend` abstraction

The thick backend interface. One implementation per 3D engine; Stride is
first, proprietary engine to follow. The interface is rich enough that
future backends won't need DD-1 to expand — only that DD-1's concepts
(slots, blend masks, notify callbacks, aim layering) are universally
expressible in any modern animation runtime, which is the bet.

```csharp
namespace Hrot.MuscleCharacter.Animation;

/// <summary>
/// The thick abstraction over a 3D engine's animation runtime.
/// One instance per Muscle Character node (not per entity).
/// All methods are called from the Muscle's main update thread;
/// implementations do not need to be thread-safe internally.
/// </summary>
public interface IAnimationBackend
{
    // --- Lifecycle ---

    /// <summary>Called once at Muscle node startup.</summary>
    void Initialize(AnimationBackendConfig config);

    /// <summary>Per-tick callback invoked by AnimationRuntimeBridgeSystem.
    /// The backend advances its internal animation time, evaluates blend
    /// trees, fires queued notifies into per-entity buffers.</summary>
    void Tick(float deltaSeconds);

    /// <summary>Called when an entity becomes Muscle-Character-relevant
    /// (ghost promotion + has CharacterAnimationDefRuntime). Backend
    /// creates its per-entity instance (e.g. Stride Entity +
    /// AnimationComponent).</summary>
    AnimationBackendHandle RegisterEntity(EntityId entity, in CharacterAnimationDefRuntime def);

    /// <summary>Called when an entity is destroyed or leaves relevance.</summary>
    void UnregisterEntity(AnimationBackendHandle handle);

    // --- Slot operations ---

    /// <summary>Start playing a montage on the specified slot.
    /// Crossfades with whatever the slot was doing.</summary>
    void PlayMontageOnSlot(
        AnimationBackendHandle handle,
        SlotId slot,
        MontageAssetId montage,
        float blendInSeconds,
        float playRate,
        byte startSectionIndex);

    /// <summary>Stop the current montage on the slot with blend-out.</summary>
    void StopMontageOnSlot(
        AnimationBackendHandle handle,
        SlotId slot,
        float blendOutSeconds);

    /// <summary>Crossfade-replace the current montage on the slot
    /// with a new one. Used by the queue advance to seamlessly chain.</summary>
    void CrossfadeMontageOnSlot(
        AnimationBackendHandle handle,
        SlotId slot,
        MontageAssetId nextMontage,
        float crossfadeSeconds,
        float playRate,
        byte startSectionIndex);

    /// <summary>Query playback state of the active montage on a slot.</summary>
    MontagePlaybackState QuerySlotState(AnimationBackendHandle handle, SlotId slot);

    // --- Locomotion blend ---

    /// <summary>Drive the locomotion blend space with current movement
    /// state. Called every Tick by the bridge with values derived from
    /// SimTransform/SimVelocity. The backend's locomotion state machine
    /// (or blend space) interprets these.</summary>
    void UpdateLocomotionInputs(
        AnimationBackendHandle handle,
        Vector2 horizontalVelocity,   // local-space, +X = forward
        float verticalVelocity,
        bool isGrounded);

    // --- Aim / Look-at layer ---

    /// <summary>Set the aim target for the additive aim layer.
    /// targetDirection is local-space forward of the head/spine.
    /// Pass blendInSeconds < 0 to release (uses configured release blend).</summary>
    void SetAimTarget(
        AnimationBackendHandle handle,
        Vector3 worldAimPoint,
        float blendInSeconds,
        byte priority);

    void ReleaseAim(AnimationBackendHandle handle, float blendOutSeconds);

    // --- Stance ---

    /// <summary>Request a stance change. Backend plays the appropriate
    /// stance-transition montage from the TKB-described transition table
    /// and ends in the new stance's locomotion state.</summary>
    void RequestStanceChange(
        AnimationBackendHandle handle,
        StanceId fromStance,
        StanceId toStance,
        float blendTimeSeconds);

    /// <summary>Query whether stance transition is still blending.</summary>
    StanceTransitionState QueryStanceTransition(AnimationBackendHandle handle);

    // --- Notify draining ---

    /// <summary>Drain notify events that fired this tick on this entity.
    /// Returns the number drained; events are written into the provided
    /// span. Called once per entity per tick by NotifyEventEmitterSystem.</summary>
    int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest);

    // --- Diagnostics ---

    /// <summary>Per-frame metrics (active entity count, total time spent
    /// in backend update, etc.) for the Muscle node's diagnostics dump.</summary>
    AnimationBackendMetrics SnapshotMetrics();
}
```

Supporting types:

```csharp
public readonly struct AnimationBackendHandle
{
    public readonly int Index;
    public readonly uint Generation;  // detects use-after-unregister
}

public readonly record struct SlotId(byte Value)
{
    public static readonly SlotId FullBody = new(0);
    public static readonly SlotId UpperBody = new(1);
    // Additional slots declared per-character in TKB; see §4.
}

public readonly record struct MontageAssetId(int Value);
public readonly record struct StanceId(byte Value);

public readonly struct MontagePlaybackState
{
    public MontageAssetId ActiveMontage;
    public float ElapsedSeconds;
    public float TotalDurationSeconds;
    public byte CurrentSectionIndex;
    public bool InBlendOutWindow;
    public float BlendWeight;          // 0..1 weight in the slot
}

public readonly struct StanceTransitionState
{
    public StanceId CurrentStance;
    public StanceId TargetStance;
    public bool IsTransitioning;
    public float TransitionProgress;   // 0..1
}

public readonly struct RawNotifyEvent
{
    public MontageAssetId Montage;
    public uint MarkerHash;
    public AnimNotifyCategory Kind;     // canonical enum declared in DD-3 §2
    public float PayloadFloat;
    public Vector3 PayloadVector;      // e.g. footstep world position
    public byte PayloadByte;           // e.g. foot index, surface hint
}

// Note: The Kind discriminator uses AnimNotifyCategory (DD-3 §2) — the
// canonical enum unifying backend discrimination (here), import-time
// marker classification (DD-4 §2 NotifyMarkerDefDto.Kind), and runtime
// event-catalog mapping (DD-3 §4). Lifecycle-event values (MontageStarted,
// MontageEnded, MontageSectionAdvanced, StanceChanged) are not used by
// RawNotifyEvent — those are synthesized from ECS state transitions by
// AnimationStateReporterSystem (§18), not drained from the backend.
```

The earlier v1.1 declared a local `NotifyKind` enum here; v1.2 aliases
to the canonical `AnimNotifyCategory` declared in DD-3 §2 for
cross-DD consistency. Values are identical.

## 4. Slot system — production-complete design

Slots are how multiple montages and the locomotion baseline compose on
one skeleton. The architect's call for production-complete: arbitrary
slot count per character, blend masks declared in TKB, configurable
priorities.

### 4.1 Slot taxonomy

A *slot* is a named layer in the per-entity animation graph. Each slot
has:

- **Stable ID** (`SlotId`, a `byte`) — declared in TKB for each character class
- **Blend mask** — which bones the slot affects (e.g. UpperBody mask = spine
  upward, FullBody mask = all bones, Hands mask = wrist-and-below)
- **Compositing mode** — Override (replaces lower-priority slots on its
  masked bones) or Additive (adds onto lower-priority slots)
- **Priority** — int, declared in TKB; higher priority composites on top of
  lower

Standard slots declared by convention in TKB for humanoids:

| Slot | Mask | Mode | Priority |
|---|---|---|---|
| Locomotion | Full body | Override (baseline) | 0 |
| FullBody | Full body | Override | 100 |
| UpperBody | Spine + arms + head | Override | 200 |
| Hands | Wrists + fingers | Override | 300 |
| AimAdditive | Spine + neck + head (pose offsets) | Additive | 400 |

Locomotion is special — it's the always-on baseline that the
`UpdateLocomotionInputs` backend call drives. Other slots overlay on top.

A vault montage targets FullBody (mask = full body, priority 100) →
overrides the locomotion baseline entirely while playing. A reload
montage targets UpperBody → leaves the legs running locomotion. An aim
overlay targets AimAdditive → applies on top of everything.

### 4.2 Slot selection per montage

Each montage asset declares which slot it plays on. This is content-author
metadata (set in UE when authoring the montage) and carried through asset
import. Stored in TKB alongside the montage ID as part of
`CharacterAnimationDefDto.Montages[i].Slot`.

When the AI says `PlayMontage(Reload_Rifle)`, the dispatcher looks up
`Reload_Rifle.Slot` from the TKB runtime descriptor and routes to that
slot. The AI author doesn't pick slots; the content author does.

### 4.3 Slot conflict resolution

Two montages requesting the same slot at the same time: the newer wins.
The dispatcher calls `CrossfadeMontageOnSlot` so the old montage blends
out as the new one blends in.

Two montages targeting *different but overlapping* slots: both play; the
higher-priority slot wins on the bones they share via the blend mask
composition.

Concretely: a FullBody vault (priority 100) and a UpperBody wave
(priority 200) both playing — the wave plays on the arms/spine, the vault
plays on the legs. This is what AAA engines do; the slot/blend-mask
machinery makes it natural.

### 4.4 Per-channel slot bookkeeping

`AnimationChannel` plays on one slot at a time (the dispatcher routes the
montage to its declared slot). But the *channel* doesn't track which slot
— it tracks one active montage command (or queue) and trusts the
dispatcher to route. The per-slot state lives in `AnimationExecutorState`
indexed by slot, plus the backend's own per-slot state.

This raises a question: if the channel runs one command at a time but
slots are parallel, how does an AI play a reload (UpperBody) and a wave
(also UpperBody) simultaneously? Answer: it doesn't. The channel is
*one* command stream. A second `PlayMontage` from the same channel
preempts the first regardless of slots, because the channel is the AI's
single conceptual handle to "what montage am I currently telling this
character to play."

If we ever need parallel montages from the same AI (running reload while
also playing a gesture), the right answer is multiple channels —
`AnimationChannel.UpperBody`, `AnimationChannel.Lower` — but that's
explicitly out of scope for v1.

## 5. ECS components introduced by DD-1

Restating v0.2's components with concrete shapes and adding the
DD-1-internal components.

### 5.1 Replicated/contractual components (defined in v0.2 mini design)

These are the components in the Brain↔Muscle contract; DD-2 covers their
replication.

- **`AnimationChannel`** — base tracking + 32B `ActionParams` + 32B
  `ActionState`, ≤96B total.
- **`LookAtChannel`** — same shape.
- **`StanceIntent`** — `{ StanceId TargetStance; float BlendTime; uint Version; }`
  (16 bytes).
- **`StanceStatus`** — `{ StanceId CurrentStance; StanceTransitionPhase Phase;
  float TransitionProgress; uint AckVersion; }` (16 bytes).
- **`AnimationMontageQueue`** — `{ byte Count; uint QueueVersion;
  [InlineArray N=8] Entries; }`. N=8 is the v1 cap. `Entries` is
  `MontageQueueEntry[8]`, each entry ≤16 bytes, so total ≤140 bytes.
- **`AnimationMontageQueueState`** — `{ byte CurrentEntryIndex;
  float EntryElapsedSeconds; bool InBlendOutWindow; uint
  ObservedQueueVersion; }` (16 bytes).

### 5.2 Muscle-internal components (new in DD-1, not replicated)

These live only on the Muscle and are not part of the Brain↔Muscle
contract. DD-2 won't touch them.

```
CharacterAnimationDefRuntime  // injected by AnimationTkbTranslator (DD-4)
  AnimationBackendHandle BackendHandle    // populated by AnimationRuntimeBridgeSystem on first tick
  byte                   StanceCount
  byte                   SlotCount
  // Pointers/handles to TKB-side per-class tables for montages,
  // stance-transition montages, slot definitions. Exact form TBD in
  // DD-4 — DD-1 only needs a handle to read them.

AnimationExecutorState                    // per-slot executor state, Muscle-internal
  // Fixed-size table indexed by SlotId.Value, capped at MaxSlots=8 for v1.
  // Per-slot record:
  fixed Slot[MaxSlots] {
    MontageAssetId  ActiveMontage          // 0 = none
    uint            ActiveActionInstanceId // correlates back to channel command that started this
    byte            ActiveQueueIndex       // 0xFF if not from a queue, else queue entry index
    byte            CurrentSectionIndex
    float           ElapsedSeconds
    bool            InBlendOutWindow
  }

LookAtExecutorState                       // Muscle-internal
  byte             ActiveAction            // 0=None, 1=Point, 2=Entity
  Vector3          CachedWorldPoint        // for Point mode, or computed-this-tick for Entity mode
  EntityId         TargetEntity            // for Entity mode
  Vector3          LocalOffset
  byte             Priority
  float            BlendWeight             // current weight 0..1
  uint             ActiveActionInstanceId
```

`AnimationExecutorState` is large (fixed `MaxSlots=8` × ~24B = ~192B per
entity). Acceptable because it's local-only and not part of any channel
budget; budgeted against the Muscle node's per-entity memory footprint
which has plenty of headroom for character entities.

### 5.3 Component injection sequence

Done by `AnimationTkbTranslator` (DD-4) during ghost promotion:

1. Insert `AnimationChannel`, `LookAtChannel`, `StanceIntent`,
   `StanceStatus` (the replicated channels/descriptors).
2. Insert `AnimationMontageQueue`, `AnimationMontageQueueState` (the
   replicated side-buffer pair).
3. Insert `CharacterAnimationDefRuntime` with TKB-resolved data, leaving
   `BackendHandle` zeroed.
4. Insert `AnimationExecutorState`, `LookAtExecutorState` (Muscle-only).

`AnimationRuntimeBridgeSystem` on its first tick observes any entity
with `CharacterAnimationDefRuntime.BackendHandle.Generation == 0` and
calls `backend.RegisterEntity(...)`, populating `BackendHandle`.

## 6. `AnimationDispatcherSystem`

Inherits from the standard `DispatcherSystemBase<AnimationChannel>` (the
existing pattern from `LocomotionDispatcherSystem`,
`WeaponDispatcherSystem`, etc.). Runs in `PreSimulation`.

### 6.1 Per-entity tick algorithm

For each entity carrying `AnimationChannel`:

```
1. Read AnimationChannel.ActionInstanceId, DispatchedInstanceId.
2. If ActionInstanceId == DispatchedInstanceId: nothing to do this tick.
3. Else: a new command arrived. Process it:
   a. If Status was Running on a previous action: call OnExit handling
      for that action's slot (covered by AnimationStateReporterSystem
      typically; dispatcher just marks).
   b. Read ActiveAction. Dispatch on it:
       - ActionIdPlayMontage          → ProcessPlayMontage(...)
       - ActionIdStopMontage          → ProcessStopMontage(...)
       - ActionIdPlayMontageQueue     → ProcessPlayMontageQueue(...)
   c. Set DispatchedInstanceId = ActionInstanceId.

Note: there is no `ActionIdClearMontageQueue` channel command. Queue
truncation is direct mutation of the `AnimationMontageQueue` side-buffer
by the Brain-side AI primitive (write down `Count`, bump `QueueVersion`).
`MontageQueueAdvanceSystem` (§7) observes the version change and respects
the truncated queue naturally. See §20.1 for the architectural rationale.
```

### 6.2 `ProcessPlayMontage`

```
1. Read PlayMontageParams from ActionParams blob.
2. Check ActorCapabilityState.CanPlayAnimations:
   - If missing: Status = Failure; return.
3. Look up the montage in CharacterAnimationDefRuntime to find its slot.
   - If not found in this character class's allowed set:
     Status = Failure; emit MontageEndedEvent { EndReason = Failed }; return.
4. Compute the effective blend-in time (params override, fall back to
   montage default).
5. Mark AnimationExecutorState.Slot[slot] with the new command's
   ActiveActionInstanceId, zero out ElapsedSeconds, ActiveMontage,
   InBlendOutWindow.
6. The actual backend.PlayMontageOnSlot call is deferred to
   AnimationRuntimeBridgeSystem this tick. The dispatcher just stages
   the intent in AnimationExecutorState.
7. Status = Running.
```

Note: the dispatcher *doesn't* call the backend directly. It writes
executor state; `AnimationRuntimeBridgeSystem` reads executor state and
calls the backend. This keeps the backend call site centralized and
makes the dispatcher cheap (no backend access cost in `PreSimulation`).

### 6.3 `ProcessPlayMontageQueue`

```
1. Read PlayMontageQueueParams from ActionParams blob.
2. Capability check (same as PlayMontage).
3. Read AnimationMontageQueue.Entries[0] (the first queue entry — must
   exist if Count > 0; if Count == 0 it's a malformed command:
   Status = Failure, return).
4. Look up Entries[0].MontageId in CharacterAnimationDefRuntime, find
   its slot. Subsequent queue entries may target the same slot or
   different slots — they're routed per-entry by their montage def. For
   v1 we restrict queue entries to share a slot (validator check in
   DD-5); cross-slot chains are out of scope.
5. Mark AnimationExecutorState.Slot[slot] with ActiveActionInstanceId
   from the channel + ActiveQueueIndex = 0.
6. Reset AnimationMontageQueueState: CurrentEntryIndex = 0,
   EntryElapsedSeconds = 0, InBlendOutWindow = false,
   ObservedQueueVersion = AnimationMontageQueue.QueueVersion.
7. Status = Running. (Will stay Running until the queue completes, see
   AnimationStateReporterSystem §11.)
```

### 6.4 Queue truncation — no dispatcher involvement

Per §20.1 (architect-approved Option B), there is no
`ActionIdClearMontageQueue` channel command and no corresponding
dispatcher path. Queue truncation is direct side-buffer mutation:

```
Brain-side AI primitive ("Clear Future Queue Entries"):
  1. Read AnimationMontageQueue and AnimationMontageQueueState.
  2. newCount = AnimationMontageQueueState.CurrentEntryIndex + 1
                (preserve the currently-playing entry only).
  3. Mutate AnimationMontageQueue via Span-cast or Get→Mutate→SetComponent:
     - Count = newCount
     - QueueVersion++
  4. Replication carries the mutation to the Muscle.

Muscle-side (no dispatcher action):
  MontageQueueAdvanceSystem (§7) observes QueueVersion != ObservedQueueVersion
  on its next tick and respects the truncated queue. Currently-playing entry
  continues to its natural blend-out; no next entry to crossfade to means
  the queue ends after the current entry.
```

No `ActionInstanceId` bump. No `AnimationChannel.Status` change.
`AnimationChannel.ActiveAction` remains `ActionIdPlayMontageQueue` and
`Status` remains `Running` until the currently-playing entry completes,
at which point `AnimationStateReporterSystem` (§18) detects the empty
queue tail and transitions `Status = Success`.

### 6.5 `ProcessStopMontage`

```
1. Find the slot currently running this channel's command (as in 6.4).
2. Read StopMontageParams.BlendOutTime.
3. Mark AnimationExecutorState.Slot[slot] to request stop with blend-out
   (a flag the bridge reads to call backend.StopMontageOnSlot).
4. Status = Failure if Reason indicated forced abort, else Status will
   transition to Success when blend-out completes (handled by
   AnimationStateReporterSystem).
```

## 7. `MontageQueueAdvanceSystem`

Runs early in `Simulation`. For each entity with an active queue
command (detected by `AnimationMontageQueueState.CurrentEntryIndex !=
0xFF`):

```
1. Read AnimationMontageQueueState and AnimationMontageQueue.
2. Detect if QueueVersion != ObservedQueueVersion:
   - The Brain (or some Muscle process) has mutated the queue. The
     mutation is one of:
       (a) Appended new entries (Count grew, QueueVersion bumped)
       (b) Cleared future entries (Count shrunk, QueueVersion bumped)
   - Either way, just update ObservedQueueVersion = QueueVersion and
     proceed. The advance logic in step 3 below naturally handles both.
3. Compute whether the current entry has entered its blend-out window:
   - Query backend.QuerySlotState(handle, slot) (cheap; cached per
     tick).
   - If state.InBlendOutWindow && CurrentEntryIndex + 1 < Count:
     - There IS a next entry. Schedule a crossfade to it (write to
       AnimationExecutorState.Slot[slot] requesting the crossfade; the
       bridge will call backend.CrossfadeMontageOnSlot this tick).
     - Increment AnimationMontageQueueState.CurrentEntryIndex.
     - Emit pending notify-event-style markers via AnimationExecutorState
       (to be picked up by AnimationStateReporterSystem and published
       as MontageEndedEvent{EndReason=BlendedOutByNext} for the old
       entry and MontageStartedEvent for the new).
   - If state.InBlendOutWindow && CurrentEntryIndex + 1 >= Count:
     - No next entry. Queue is ending naturally. Leave the current
       entry to play out; AnimationStateReporterSystem will detect
       completion in PostSimulation and write Status = Success.
4. Update AnimationMontageQueueState.EntryElapsedSeconds from
   state.ElapsedSeconds.
```

### 7.1 The "QueueVersion-bumped mid-blend-out" case

Edge case worth being explicit about: the executor is already in
`InBlendOutWindow` for entry K, has already committed to crossfading to
entry K+1, and *then* the Brain bumps `QueueVersion` (e.g. by clearing
future entries). What happens?

Resolution: once the executor has called `CrossfadeMontageOnSlot` for
entry K→K+1, the crossfade is committed at the backend level. The
crossfade completes; entry K+1 plays to its natural blend-out. At *that*
point, the executor re-evaluates against the (possibly updated) queue.
If by then the queue has been further mutated, the new state is honored.

This means there's a 1-frame race where a Clear issued during the
blend-out of K won't prevent K+1 from playing — K+1 starts playing then
plays out without further entries. This is a documented and acceptable
limitation; precise mid-blend-out cancellation requires
`ActionIdStopMontage` (which is a preemption).

## 8. `LookAtDispatcherSystem`

Same `DispatcherSystemBase` shape as `AnimationDispatcherSystem`. Per
tick, per entity carrying `LookAtChannel`:

```
1. Read ActionInstanceId / DispatchedInstanceId. If equal, skip.
2. Dispatch on ActiveAction:
   - ActionIdLookAtPoint:
       * Capability check CanAim.
       * Copy LookAtPointParams.WorldPoint into LookAtExecutorState.
         CachedWorldPoint, ActiveAction = 1 (Point), Priority,
         BlendWeight=0 (will ramp up).
   - ActionIdLookAtEntity:
       * Capability check CanAim.
       * Read LookAtEntityParams.TargetEntity, LocalOffset, Priority.
         Store into LookAtExecutorState. ActiveAction = 2 (Entity).
       * Each tick, the bridge resolves Entity → world position via
         repo.GetComponentRO<SimTransform>(target) + LocalOffset.
   - ActionIdReleaseLook:
       * No capability check needed (release always allowed).
       * Set LookAtExecutorState.ActiveAction = 0, store BlendOutTime.
3. DispatchedInstanceId = ActionInstanceId.
4. Status = Running (or Success after blend-out completes for Release,
   reported by AnimationStateReporterSystem).
```

The backend's `SetAimTarget` is called per tick by
`AnimationRuntimeBridgeSystem` with the current resolved world point —
this keeps Entity-mode look-at tracking moving targets naturally.

If the target entity dies or becomes unreachable, the bridge falls back
to the last known position and the dispatcher writes
`Status = Failure` on the look-at channel. This is the
`view.IsAlive(target)` check style mirrored from the EQS sensor pattern.

## 9. `StanceTransitionSystem`

Not a channel dispatcher — `StanceIntent`/`StanceStatus` is a plain CQRS
descriptor pair per architect's Q3. The system:

```
For each entity carrying StanceIntent + StanceStatus:
  1. If StanceIntent.Version == StanceStatus.AckVersion: nothing to do.
  2. Else: a new stance request has arrived.
     a. Capability check CanChangeStance.
        - If missing: AckVersion = Version, leave CurrentStance
          unchanged (silently ignore).
     b. Read StanceIntent.TargetStance.
     c. If TargetStance == StanceStatus.CurrentStance: nothing to
        animate. Set AckVersion = Version, Phase = Completed.
     d. Else: call backend.RequestStanceChange(handle, CurrentStance,
        TargetStance, StanceIntent.BlendTime). Set
        StanceStatus.Phase = Transitioning, TransitionProgress = 0.
        Set AckVersion = Version.

  3. If StanceStatus.Phase == Transitioning:
     a. Read backend.QueryStanceTransition(handle).
     b. Update StanceStatus.TransitionProgress.
     c. If transition state reports done:
        - StanceStatus.CurrentStance = StanceIntent.TargetStance
        - StanceStatus.Phase = Completed
        - Emit a pending StanceChangedEvent (drained by
          AnimationStateReporterSystem into FdpEventBus).
```

The backend's `RequestStanceChange` implementation selects the
appropriate stance-transition montage from the TKB-described per-class
transition table (`CharacterAnimationDefRuntime` has handles to it) and
plays it on a designated stance-transition slot (could be FullBody with
high priority, or a dedicated Stance slot; backend's choice within its
slot system).

## 10. `AnimationRuntimeBridgeSystem`

The system that actually talks to the backend. Runs mid-`Simulation`,
after dispatchers and after `MontageQueueAdvanceSystem` so it has the
latest staged commands.

Per entity (with `CharacterAnimationDefRuntime`):

```
1. If BackendHandle.Generation == 0:
   - Call backend.RegisterEntity(entity, def) → populate BackendHandle.

2. Pump locomotion inputs:
   - Read SimTransform, SimVelocity from this entity.
   - Convert to local-space horizontalVelocity (rotate by inverse
     facing) + verticalVelocity + grounded flag (from
     CharacterMovementState or similar — depends on existing kinematics
     system).
   - Call backend.UpdateLocomotionInputs(handle, hv, vv, grounded).

3. Apply staged montage commands (from AnimationExecutorState):
   - For each slot with a pending command flag:
     - PlayMontage: backend.PlayMontageOnSlot(handle, slot, montage,
       blendIn, playRate, startSection); clear the flag.
     - Crossfade (from queue advance): backend.CrossfadeMontageOnSlot(...).
     - Stop: backend.StopMontageOnSlot(handle, slot, blendOut).

4. Apply look-at state:
   - If LookAtExecutorState.ActiveAction != 0:
     - For Point: use CachedWorldPoint directly.
     - For Entity:
       * If view.IsAlive(TargetEntity) && entity has SimTransform:
         resolvedPoint = targetTransform.Position +
           targetTransform.Rotation * LocalOffset.
       * Else: keep CachedWorldPoint (last known), signal failure
         pending (LookAtDispatcher writes Status = Failure next tick).
     - backend.SetAimTarget(handle, resolvedPoint, blendInSeconds,
       priority).
   - Else if released-this-tick flag: backend.ReleaseAim(handle,
     blendOutSeconds).

5. (Stance transition is driven by StanceTransitionSystem which calls
    backend.RequestStanceChange directly; bridge does nothing extra.)
```

After the per-entity loop, `AnimationRuntimeBridgeSystem` calls
`backend.Tick(deltaSeconds)` once. The backend advances its internal
animation time, evaluates blend trees, fires notifies into per-entity
buffers. The next two systems drain that state.

## 11. `NotifyEventEmitterSystem` and the local FdpEventBus contract

Runs early in `PostSimulation`, after `AnimationRuntimeBridgeSystem`'s
backend tick.

Per entity:

```
1. Allocate a small stack-buffer Span<RawNotifyEvent>(16).
2. count = backend.DrainNotifies(handle, buf).
3. For i in 0..count-1:
   - raw = buf[i].
   - Translate raw.Kind into the typed FdpEventBus event:
     * Footstep → Publish FootstepEvent { Target=entity,
                  WorldPosition=raw.PayloadVector, FootIndex=raw.PayloadByte, ... }
     * HitWindowOpened → Publish HitWindowOpenedEvent { ... }
     * Generic → Publish AnimNotifyEvent { Target=entity,
                 MontageId=raw.Montage, MarkerHash=raw.MarkerHash,
                 PayloadFloat=raw.PayloadFloat }
     * MontageStarted/Ended/SectionAdvanced are NOT published here —
       these are synthesized by AnimationStateReporterSystem based on
       ECS state transitions (more deterministic than relying on the
       backend to emit them).
4. If count > 0 and buf was full, loop with another drain call to handle
   high-notify-density frames.
```

The published events are on the Muscle's *local* `FdpEventBus`. DD-2
covers the `INetworkEventTranslator` egress for cross-node propagation.

### 11.1 Why synthesize MontageStarted/Ended in ECS rather than from backend?

The backend's "montage ended" callback fires when *it* decides the
montage is done, which depends on backend-internal blend timing. For
consistency, we synthesize these events from ECS state transitions in
`AnimationStateReporterSystem` (next section) — the executor state is
the source of truth for "did the montage finish in our model."

The backend can still emit these via `DrainNotifies` if it wants, but
`NotifyEventEmitterSystem` discards `MontageStarted`/`MontageEnded`/
`MontageSectionAdvanced` from the drain and lets the ECS-side
synthesis handle them. This is a deliberate redundancy-avoidance
choice; revisit if profiling shows backend-driven events would be
materially cheaper.

## 12. Capability gating — where each check fires

Per architect Q5, three new bits in `ActorCapabilities`. Gating checks
live at command-arrival time in each dispatcher (§§6, 8, 9 above):

- `CanPlayAnimations` → checked in `AnimationDispatcherSystem`
  `ProcessPlayMontage` / `ProcessPlayMontageQueue`. On absence,
  immediately set `Status = Failure` and skip executor setup.
- `CanAim` → checked in `LookAtDispatcherSystem` for
  `ActionIdLookAtPoint`/`ActionIdLookAtEntity` (not for Release; release
  always allowed even if blinded).
- `CanChangeStance` → checked in `StanceTransitionSystem` per §9. On
  absence, silently ack the version without changing stance.

Capability checks read `ActorCapabilityState`; the existing capability
plumbing on the Muscle keeps that component up to date based on damage,
stuns, environmental factors.

## 13. Mid-action capability loss handling

A character is playing a reload montage when they get stunned —
`CanPlayAnimations` is stripped mid-execution. The dispatchers only
check capability at command-arrival time, so a separate mechanism must
catch high→low transitions.

Per §20.6/§20.7 architectural rulings: the engine already has a
capability-change reactor system that forces stops on `CanMove` and
`CanShoot` loss, and the `PreviousCapabilities` component is already
tracked engine-wide. **This iteration extends the existing reactor;
it does not introduce a new system.**

The added reactor logic (placed alongside the existing
`CanMove`/`CanShoot` handlers):

```
For each entity whose PreviousCapabilities differs from
ActorCapabilityState.Capabilities:

  - If CanPlayAnimations bit transitioned high→low:
    * For every slot in AnimationExecutorState.Slot[] with an active
      montage: stage a forced-stop with short blend-out (e.g. 0.1s,
      configurable per-character).
    * Set AnimationChannel.Status = Failure.
    * Bump DispatchedInstanceId so the next command from Brain isn't
      ignored as a duplicate.
    * Clear AnimationMontageQueueState (queue is cancelled).

  - If CanAim bit transitioned high→low:
    * Stage ReleaseAim with short blend-out on LookAtExecutorState.
    * Set LookAtChannel.Status = Failure.

  - If CanChangeStance bit transitioned high→low while in
    StanceStatus.Phase = Transitioning:
    * Leave the in-flight transition to complete naturally — better
      than snapping mid-blend. Subsequent stance requests are silently
      ignored while capability is absent (per §9's
      StanceTransitionSystem capability check).
```

This logic runs in the existing reactor's `PreSimulation` phase slot,
before the DD-1 dispatchers, so the dispatchers see consistent
state. No new system added.

## 14. Per-entity state and lifecycle

Entity lifecycle from the Muscle Character runtime's perspective:

1. **Ghost promotion** — `AnimationTkbTranslator` (DD-4) injects all the
   replicated and Muscle-internal components.
   `CharacterAnimationDefRuntime.BackendHandle` is zeroed.
2. **First tick visible** — `AnimationRuntimeBridgeSystem` notices
   `BackendHandle.Generation == 0`, calls `backend.RegisterEntity`,
   populates `BackendHandle`. Backend allocates its per-entity instance
   (e.g. Stride `AnimationComponent`).
3. **Steady state** — channels and intents drive backend operations
   through the dispatcher/bridge pipeline.
4. **Entity destruction** — when the entity is removed from the Muscle's
   ECS world: per §20.5 (architect-approved), the engine uses an
   explicit `PendingDestroy` tag-component pattern. A dedicated
   `AnimationBackendCleanupSystem` watches for entities carrying both
   `PendingDestroy` and `CharacterAnimationDefRuntime`, calls
   `backend.UnregisterEntity(handle)`, and clears `BackendHandle`. This
   runs one tick before the engine reaps the ECS chunk memory, giving
   the backend a clean opportunity to release per-entity state (Stride
   entities, blend-tree-builder instances, notify buffers).

   Phase placement: `PostSimulation`, after `NotifyEventEmitterSystem`
   has drained any final notifies for entities about to be destroyed,
   but before the engine's chunk-reaper phase.

## 15. The Stride backend implementation (initial)

The first implementation of `IAnimationBackend`. Will be replaced with
the proprietary engine's backend later; the interface contract above is
what survives.

### 15.1 Stride's animation model — recap

Stride's per-entity animation lives in `AnimationComponent`, which by
default plays animation clips via name and the engine's internal blend
list. For non-trivial slot/blend-mask use, Stride supports replacing
the default behavior with an `IBlendTreeBuilder`:

```csharp
animationComponent.BlendTreeBuilder = customBuilder;
```

The builder's `BuildBlendTree(FastList<AnimationOperation>)` method is
called by Stride's animation processor each tick; the builder pushes
operations (push clip, blend two, blend additive, pop) describing the
frame's pose composition.

This is exactly the seam we need. The Stride `IAnimationBackend`
implementation creates one custom `IBlendTreeBuilder` per registered
entity that knows about our slots, montages, and aim layer, and
constructs the blend tree from the per-entity executor state each frame.

### 15.2 `StrideAnimationBackend` structure

```csharp
internal sealed class StrideAnimationBackend : IAnimationBackend
{
    // The Stride Game/SceneSystem reference, set in Initialize.
    private SceneSystem _sceneSystem;
    private ContentManager _content;

    // Per-handle entry pool. Generation-counted for safety.
    private struct Entry
    {
        public Stride.Engine.Entity StrideEntity;     // backing Stride entity carrying AnimationComponent
        public AnimationComponent Anim;
        public PerEntityBlendTreeBuilder Builder;     // implements IBlendTreeBuilder
        public List<RawNotifyEvent> NotifyBuffer;      // drained by DrainNotifies
        public uint Generation;
        public bool InUse;
    }
    private Entry[] _entries;
    private Stack<int> _freeIndices;

    // Montage asset cache (MontageAssetId → AnimationClip + metadata).
    private Dictionary<MontageAssetId, MontageAsset> _montageCache;

    // Implementation of every IAnimationBackend method here.
    // PlayMontageOnSlot writes into the entry's Builder; Builder
    // synthesizes the blend tree next frame.
}

internal sealed class PerEntityBlendTreeBuilder : IBlendTreeBuilder
{
    // Per-slot active clip evaluators.
    // Locomotion blend space evaluator.
    // Aim layer additive evaluator.
    // Methods that mutate slot state (called by StrideAnimationBackend).
    // BuildBlendTree implementation that composes the final tree.
}
```

### 15.3 Per-entity Stride entities — where do they live?

Each humanoid entity in the engine's ECS needs a backing Stride entity
carrying an `AnimationComponent` plus skeletal model. The Stride backend
manages a parallel Stride scene graph populated by `RegisterEntity`.

This raises a question for the team: how does the Stride scene get its
positions updated? Options:

(A) The bridge writes `SimTransform → strideEntity.Transform` every
    tick after the bridge's `backend.Tick`. Simple, explicit, costs
    a copy per character per tick.
(B) The Stride entity is parented to a transform that the existing
    rendering bridge updates. Probably already happens for ghost-side
    rendering of the character.

(B) is the right architectural answer if the rendering bridge already
exists; (A) is the fallback if not. The Muscle Character runtime doesn't
care which — its `IAnimationBackend` doesn't care about world
transforms (animation produces local-space bone poses; final world
transform composition is the rendering layer's job).

### 15.4 Stride notify mapping

Stride doesn't have first-class AnimNotify (that's a UE concept).
Notifies are baked into the montage assets at import time (DD-4 / asset
import scope) as keyframed events attached to clips. The Stride backend
exposes a callback registration on each `AnimationClipEvaluator` that
fires when the clip's playhead crosses a keyframed marker; the callback
pushes a `RawNotifyEvent` into the per-entry `NotifyBuffer`.

Asset import details (how UE AnimNotifies become Stride clip markers)
are out of scope for DD-1 but the bridge's contract is: Stride emits
notify callbacks, backend translates to `RawNotifyEvent`, ECS side
drains them.

## 16. Designing for the proprietary backend swap

The `IAnimationBackend` interface is the entire contract. Three things
DD-1 does to keep the swap mechanical:

**No Stride types leak.** Public types referenced in the interface
(`AnimationBackendHandle`, `SlotId`, `MontageAssetId`, `RawNotifyEvent`,
etc.) are engine-defined, not Stride-defined. The Stride backend
translates between them internally.

**No backend-internal concepts leak.** Stride's `IBlendTreeBuilder`,
`AnimationClipEvaluator`, `AnimationOperation` — all confined to the
`Hrot.MuscleCharacter.Animation.Stride` namespace. The proprietary
backend will have its own internal concepts (presumably more
Unreal-like — graph-based AnimGraph, anim state machines), and those
will also live in its namespace.

**The interface is rich enough to express what we need from any
modern backend.** Specifically, the interface assumes any backend can
support: slotted playback with blend masks, per-slot crossfades,
additive aim layering, notify keyframes, and locomotion inputs. These
are universal enough to be safe assumptions. If a future backend can't
do additive aim layering, the look-at feature degrades on that
backend; doesn't break the whole runtime.

## 17. Phase ordering with existing Muscle systems

The Muscle Character node also runs the existing motion, perception,
weapon systems. DD-1's systems insert into the existing ordering as
follows. (Confirm with the engine architect — this is the proposed
ordering, not a verified-against-existing-codebase ordering.)

```
PreSimulation:
  ... existing perception input gathering ...
  ... existing capability-change reactor (now extended for animation per §13) ...
  AnimationDispatcherSystem                 (DD-1, §6)
  LookAtDispatcherSystem                    (DD-1, §8)
  StanceTransitionSystem                    (DD-1, §9)
  ... other existing dispatchers (LocomotionDispatcherSystem, WeaponDispatcherSystem) ...

Simulation:
  MontageQueueAdvanceSystem                 (DD-1, §7)
  ... existing simulation systems ...
  AnimationRuntimeBridgeSystem              (DD-1, §10) — backend.Tick called here
  ... rest of simulation ...

PostSimulation:
  ... existing kinematics integration (LinearKinematicsSystem etc.) ...
  ... existing perception/event production ...
  SpatialHashSystem                         (existing)
  NotifyEventEmitterSystem                  (DD-1, §11)
  AnimationStateReporterSystem              (DD-1, §18)
  AnimationBackendCleanupSystem             (DD-1, §14 — PendingDestroy watch)
  TransformSyncSystem                       (existing)
```

The key invariants:

- Backend tick (in `AnimationRuntimeBridgeSystem`) must run *after*
  inputs are pumped to the backend but *before* notify draining and
  state reporting.
- Notify draining must run *after* backend tick (notifies fire during
  tick).
- `AnimationStateReporterSystem` must run *after* the backend tick (so
  it can observe final playback state for natural-completion detection)
  but *before* network egress translators (DD-2) so their dirty-detection
  picks up the updated `Status`.

## 18. `AnimationStateReporterSystem`

Mentioned but not yet detailed. Runs late in `PostSimulation`.

Per entity, per slot in `AnimationExecutorState`:

```
1. If slot has an active montage:
   a. Query backend.QuerySlotState(handle, slot).
   b. If state.ActiveMontage == 0 (backend says nothing playing) but
      executor still thinks something is playing:
      * The backend ended the montage. This is a natural completion or
        forced-stop completion.
      * Determine which:
        - If executor's pending-stop flag was set: forced. EndReason = Interrupted (or Forced).
        - Else: natural. EndReason = NaturalEnd.
      * Publish MontageEndedEvent { Target, MontageId, ActionInstanceId,
                                    QueueIndex, EndReason }.
      * If this montage was part of a queue (executor's
        ActiveQueueIndex != 0xFF):
        - Check if CurrentEntryIndex+1 >= Count: queue is fully done.
          * Clear AnimationMontageQueueState (CurrentEntryIndex = 0xFF).
          * Set AnimationChannel.Status = Success.
        - Else: queue advance system should have already scheduled the
          next entry; just clear this slot's record.
      * Else (single PlayMontage, not a queue):
        - Set AnimationChannel.Status = Success.
   c. Else if state shows a new montage that we don't have recorded:
      * The bridge started a montage that's now visible to the backend.
        Synthesize MontageStartedEvent.
      * Update executor record.
   d. Else if state.CurrentSectionIndex changed since last tick:
      * Synthesize MontageSectionAdvancedEvent.

2. For LookAtChannel: if release was staged and the aim has fully
   blended out (BlendWeight == 0), set LookAtChannel.Status = Success.
```

This system is the central source of truth for "what events did the
montage system produce this tick." Synthesizing here rather than relying
on the backend gives us deterministic event semantics that don't depend
on backend timing details.

## 19. Future root-motion path — what changes here

When the engine adopts root-motion-drives-kinematics (architect Q7):

1. The `IAnimationBackend` interface adds a method:
   `Vector3 ExtractRootMotionDelta(handle)` returning the
   local-space root-bone translation for this tick.
2. A new system `RootMotionApplicatorSystem` reads
   `ExtractRootMotionDelta` for entities flagged as root-motion-active
   (`UsesRootMotion` capability or a per-entity flag) and writes the
   delta into `SimTransform` directly. Adds a
   `SuppressLinearKinematics` component to those entities so
   `LinearKinematicsSystem` skips them (architect Q7 specifically
   flagged this).
3. Phase ordering: `RootMotionApplicatorSystem` runs in `PostSimulation`
   *after* `AnimationRuntimeBridgeSystem` (so the backend tick has
   produced the delta) and *after* existing kinematics for non-RM
   entities, but *before* `SpatialHashSystem` rebuild. It also includes
   the `IsLocalAuthoritativeOnly` guard (architect Q7) so it doesn't
   touch remote ghosts being lerped by `TransformSyncSystem`.

None of this requires DD-1's current contracts to change. It's a
strict addition — DD-1's systems and components survive the flip
unchanged.

## 20. Resolutions summary (from v1.0 review)

All seven open questions from DD-1 v1.0 received architect rulings;
recorded here for traceability. v1.1 incorporates each resolution
into the body sections referenced.

### 20.1 ✅ `ActionIdClearMontageQueue` mechanism

**Resolved:** Option B approved. Clear is not a channel command. Queue
truncation is direct side-buffer mutation by the Brain-side AI
primitive (write down `Count`, bump `QueueVersion`). The channel's
action surface is `ActionIdPlayMontage`, `ActionIdStopMontage`,
`ActionIdPlayMontageQueue` only. Reflected in §6.1 and §6.4.

### 20.2 ✅ Slot count cap

**Resolved:** `MaxSlots = 8` approved. Per-entity
`AnimationExecutorState` size ~192 bytes is acceptable as Muscle-only
non-replicated state. Reflected in §5.2.

### 20.3 ✅ Queue capacity `N`

**Resolved:** `AnimationMontageQueue.Entries` capacity `N = 8` approved.
Total component size ~140 bytes fits within engine side-buffer limits.
Reflected in §5.1.

### 20.4 ✅ Backend tick threading

**Resolved (informationally):** parallelization is the backend's internal
concern; DD-1 doesn't constrain it. Stride's animation processor may
already parallelize; the proprietary backend can make its own choice.

### 20.5 ✅ Entity-destruction cleanup mechanism

**Resolved:** Use the engine's `PendingDestroy` tag-component pattern. A
dedicated `AnimationBackendCleanupSystem` watches for this tag plus
`CharacterAnimationDefRuntime` and calls `backend.UnregisterEntity` one
tick before chunk reaping. Reflected in §14, §17 phase ordering, and
the §2 system roster.

### 20.6 ✅ `CapabilityChangeReactorSystem` — extend existing

**Resolved:** Do not create a new system. The engine already has a
capability-change reactor handling `CanMove`/`CanShoot` loss; this
iteration extends it with `CanPlayAnimations`, `CanAim`, and
`CanChangeStance` handlers. Reflected in §13 and §17 phase ordering.

### 20.7 ✅ `PreviousCapabilities` tracking

**Resolved:** The engine already tracks `PreviousCapabilities` engine-wide;
DD-1 relies on it for high→low transition detection. No additive work.
Reflected in §13.

---

**No residual open questions remain.** DD-1 is fully resolved and
approved for implementation.

---

## Summary

DD-1 specifies the Muscle Character node's animation runtime against
the v0.2 contract: seven new ECS systems (one dispatcher per channel, a
queue advancer, a backend bridge, an event emitter, a state reporter,
and a backend cleanup system), plus extension of the engine's existing
capability-change reactor for animation-related capability loss. The
`IAnimationBackend` thick abstraction confines all 3D-engine specifics
to its implementation namespace, enabling mechanical swap from Stride
to the proprietary engine when ready. The production-complete slot
system supports TKB-declared blend masks, configurable priorities, and
both Override and Additive compositing — enabling concurrent montages
on overlapping slots (the AAA pattern). Future
root-motion-drives-kinematics is an additive change that doesn't
disturb DD-1's contracts.

All seven open questions from DD-1 v1.0 resolved per architect review
(see §20 Resolutions Summary).

Next detailed designs to write: DD-3 (Event Catalog) and DD-4 (TKB) are
independent of DD-1 and can proceed in parallel. DD-2 (replication) and
DD-5 (Blueprint primitives) depend on DD-1's contracts and should follow.

---

*End of DD-1 v1.1. Architect-approved for implementation.*
