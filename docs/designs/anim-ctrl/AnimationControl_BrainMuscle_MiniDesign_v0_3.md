# Animation Control: Brain → Muscle — Mini Design (v0.3)

> **Status:** **Canonical architectural contract.** This document is the
> approved single-altitude statement of the Brain ↔ Muscle animation
> interface and the *entry point* for the animation architecture. It is
> not the implementation specification — the five detailed-design
> documents below carry that. New team members should read this
> document first to understand the shape of the system, then dive into
> whichever DD covers their area.
>
> **Implementation lives in five detailed-design documents:**
> - **DD-1 Muscle Character Runtime** — ECS systems, slot system,
>   `IAnimationBackend` abstraction, Stride backend, montage queue
>   advance, notify emission, capability gating.
> - **DD-2 Animation Replication** — DDS topics, QoS, intent/status
>   translators, side-buffer replication with `QueueVersion`
>   dirty-signaling, event translator pairs.
> - **DD-3 Engine Event Catalog** — catalog entries for the eight
>   notify events, `[AnimMarkerPicker]` / `[MontagePicker]` property
>   drawers, `BP2016` / `BP2017` validators, canonical
>   `AnimNotifyCategory` enum.
> - **DD-4 TKB Animation Descriptor** — `CharacterAnimationDefDto`
>   schema, `AnimationTkbTranslator` with hot-reload-aware cache,
>   editor `IAnimationTkbQueries`, `ANIM001`–`ANIM007` validators.
> - **DD-5 Blueprint Authoring Primitives** — nine new AiPrimitive
>   nodes (montage, stance, look-at, queue mutation, getters),
>   codegen with `[InlineArray]` Pattern A/B safety, `ANIM008`–
>   `ANIM012` validators, worked authoring example.
>
> **Sections superseded by DDs:**
> - **§7 (Topology)** — DD-1 §17 has the actual phase-ordering
>   specification including the architect's root-motion phase-ordering
>   caveats. The §7 here is the high-level statement only.
> - **§8 (TKB integration)** — DD-4 in full supersedes this. The §8
>   paragraph here is a placeholder pointing to DD-4 for the schema
>   and translator details.
>
> The rest of this document remains current as the architecture-altitude
> reference: §1 (problem shape), §2 (one-paragraph proposal), §3 (channel
> decomposition rationale), §4 (montage queue rationale), §5 (notify
> event family), §6 (authoring surfaces), §9 (capability gating bit
> assignment), §10 (end-to-end round-trip walkthrough), §11 (deliberate
> non-scope), §12 (implementation roadmap).
>
> **Audience:** Anyone new to the animation architecture (primary).
> Engine architect (historical sign-off record). Cross-team reviewers
> evaluating the architecture without needing the full DD detail.
>
> **Changes from v0.2:** Status block rewritten to reflect the
> document's role after the five DDs landed. Supersession notes added
> to the headers of §7 and §8. No body content changed; if you want
> the original v0.2 status block (which described the changes from
> v0.1), it's preserved in the project's revision history.

---

## 1. The shape of the problem

The Brain node runs BTrees, HSMs, and Blueprints. The Muscle node owns
physical execution. For a humanoid character, "physical execution" now
includes a skeletal mesh with a hardcoded animation state machine, montage
slots, an aim layer, and a stance system. The AI designer needs to:

1. **Trigger** discrete one-shot animations (reload, vault, melee swing,
   gesture, reaction) and know when they complete or are interrupted.
2. **Switch** the character's movement mode (standing / crouched / prone /
   etc.) and know when the transition blend is done.
3. **Aim** the character at a point or a target entity, concurrently with
   whatever else they're doing, and release the aim when done.
4. **React** to per-frame animation events (footstep, hit-window-open,
   generic notify markers) authored on the animation assets themselves.
5. **Chain** multiple animations into a seamless sequence — vault →
   land-roll → recover — without visible pops at the boundaries.

All of this fits the engine's existing Channel / CQRS Intent-Status / Engine
Event Catalog patterns, works with the visual authoring surfaces (BTree,
HSM, Blueprint Instance with `WhenNode`), and survives the future flip from
kinematics-drives-animation to root-motion-drives-kinematics.

## 2. The proposal in one paragraph

Add two new channels — **`AnimationChannel`** and **`LookAtChannel`** —
each following the existing `LocomotionChannel` / `WeaponChannel` /
`InteractionChannel` pattern (32-byte `ActionParams` blob, 32-byte
`ActionState` blob, total channel under the 96-byte `MaxChannelSizeBytes`
budget; `ActiveAction` / `Status` / `ActionInstanceId` /
`DispatchedInstanceId` lifecycle; capability-gated dispatcher; Intent egress
translator on Brain; Status egress translator on Muscle). Stance is **not**
a channel — it's a plain CQRS descriptor pair (`StanceIntent` and
`StanceStatus`) following the `NavigationIntent` / `NavigationStatus`
precedent. For montage chaining, add a **side-buffer component pair** owned
by the entity: `AnimationMontageQueue` (Brain-authored, holds the
`[InlineArray]` of planned entries plus `Count` and `QueueVersion`) and
`AnimationMontageQueueState` (Muscle-authored, holds the executor's playback
progress). Add a family of typed **animation notify events** published by
the Muscle on its `FdpEventBus` and bridged to the Brain via
`INetworkEventTranslator`, registered in the `EngineEventCatalog` so they
become first-class triggers for Blueprint `WhenNode` Event Fired mode and
for `TransientEventPredicateDto`-based BTree reactivity. The Muscle
Character co-locates with the existing motion / perception / weapon Muscle
because all four subsystems share authority over the entity's spatial
descriptors.

## 3. The channel family

### 3.1 Two channels and a descriptor

- **`AnimationChannel`** carries one-shot completable commands (play
  montage). Lifetime: short. Arbitration: newer command preempts older
  (with crossfade blend-out). Success means "blended out cleanly to neutral
  or to the next queued montage."
- **`LookAtChannel`** carries continuous targeting overlay (aim at point /
  aim at entity / release). Lifetime: held until released. Arbitration:
  priority-weighted; designed to run concurrently with whatever montage is
  playing because aim-offset is an additive layer.
- **`StanceIntent` / `StanceStatus`** (not a channel) carries modal state
  (standing / crouched / prone). Brain writes `StanceIntent`; Muscle
  initiates the transition blend; Muscle writes `StanceStatus`
  (`Transitioning` / `Completed`) which replicates back. Authors who want
  to wait on the transition use `WhenNode(ValueChanged)` on `StanceStatus`
  — no channel dispatcher needed.

Concurrency between the three is intentional: an entity can simultaneously
be mid-reload-montage, transitioning to crouched, and aiming at a threat.
Each surface only worries about its own family.

### 3.2 Action surface

```
AnimationChannel:
  ActionIdPlayMontage            -- single montage; preempts current with crossfade
  ActionIdStopMontage            -- abort current with blend-out
  ActionIdPlayMontageQueue       -- start a fresh chain (preempts any current montage)
  ActionIdClearMontageQueue      -- drain entries after the currently-playing one
                                    (no ActionInstanceId bump)

LookAtChannel:
  ActionIdLookAtPoint            -- worldspace point
  ActionIdLookAtEntity           -- entity (resolved on Muscle via NetworkEntityMap)
  ActionIdReleaseLook            -- blend out

StanceIntent (component, no actions):
  TargetStance + BlendTime
```

Note: there is no `ActionIdEnqueueMontage`. Append is done by mutating the
`AnimationMontageQueue` side-buffer directly — see §4.

### 3.3 Channel component layout

Both channels follow the standard fixed-size shape:

- **`AnimationChannel`** — base tracking fields (`ActiveAction`, `Status`,
  `BehaviorInstanceId`, `ActionInstanceId`, `DispatchedInstanceId`) +
  32-byte `ActionParams` + 32-byte `ActionState`, total under the 96-byte
  `MaxChannelSizeBytes` budget.
- **`LookAtChannel`** — same shape.

`ActionParams` variants for `AnimationChannel` (each a struct ≤ 32 bytes,
unsafe-cast into the params blob):

- **`PlayMontageParams`** — `MontageId`, `BlendInTime`, `BlendOutTime`,
  `PlayRate`, `StartSectionIndex`, `LoopCount`, `Priority`, `Flags`.
- **`StopMontageParams`** — `BlendOutTime`, `Reason`.
- **`PlayMontageQueueParams`** — just the *trigger* and overall configuration
  (`InitialBlendInTime`, `Priority`, `Flags`). The actual queue entries
  live in the `AnimationMontageQueue` side-buffer component. The params
  blob is small because the heavy data is elsewhere.
- **`ClearMontageQueueParams`** — empty / reserved.

`ActionParams` variants for `LookAtChannel`:

- **`LookAtPointParams`** — `Vector3 WorldPoint`, `BlendInTime`, `Priority`.
- **`LookAtEntityParams`** — `Entity TargetEntity`, `Vector3 LocalOffset`,
  `BlendInTime`, `Priority`.
- **`ReleaseLookParams`** — `BlendOutTime`.

`ActionState` for `AnimationChannel` carries executor working state
(currently-playing montage id and section, accumulated blend progress) that
the Muscle dispatcher/executor uses. For the queue case, the per-entry
playback state lives in the separate `AnimationMontageQueueState`
component; the channel's own `ActionState` carries only what's needed for
the *outer* lifecycle.

## 4. Montage chaining — the side-buffer pair

### 4.1 The two components

```
AnimationMontageQueue              (Brain-authored, replicates Brain → Muscle)
  Count                : byte
  QueueVersion         : uint        -- bumped on any spec mutation
  Entries              : [InlineArray] of MontageQueueEntry (capacity N)

MontageQueueEntry
  MontageId            : int         -- stable ID from TKB animation set
  BlendIntoTime        : float       -- crossfade from previous entry
  PlayRate             : float
  StartSectionIndex    : byte
  Flags                : byte         -- e.g. bit 0: UsesRootMotion (future)

AnimationMontageQueueState         (Muscle-authored, replicates Muscle → Brain)
  CurrentEntryIndex    : byte         -- 0xFF = no entry active
  EntryElapsedSeconds  : float
  InBlendOutWindow     : bool
  ObservedQueueVersion : uint         -- the version the executor has seen
```

The inline-array capacity `N` (likely 4 or 8) is fixed at engine-build time;
chains longer than `N` are out of scope for the first iteration and would
require either upping the capacity or a separate spillover mechanism.

### 4.2 Lifecycle

**Starting a chain.** Brain wants vault → land-roll → recover. It:

1. Writes the three entries into `AnimationMontageQueue.Entries`, sets
   `Count = 3`, bumps `QueueVersion`.
2. Writes `ActionIdPlayMontageQueue` into `AnimationChannel.ActiveAction`
   with `PlayMontageQueueParams` (initial blend-in, priority).
3. Bumps `AnimationChannel.ActionInstanceId`.

The dispatcher sees the `ActionInstanceId` mismatch, calls `OnEnter` on the
queue executor. The executor reads `AnimationMontageQueue`, sets
`AnimationMontageQueueState.CurrentEntryIndex = 0`, starts playing entry 0
with the initial blend-in.

As the executor advances:

- When entry K reaches its blend-out window, set
  `InBlendOutWindow = true`, begin crossfading into entry K+1 (if any)
  using entry K+1's `BlendIntoTime`. Publish `MontageEndedEvent` for entry
  K with `EndReason = BlendedOutByNext` and `MontageStartedEvent` for
  entry K+1.
- Increment `CurrentEntryIndex`.
- When the last entry blends out completely, set
  `CurrentEntryIndex = 0xFF`, publish `MontageEndedEvent` for the last entry
  with `EndReason = NaturalEnd`, and write `AnimationChannel.Status = Success`.

**Appending a montage to a running chain.** No new channel command. The
Brain (or, more accurately, the AI primitive driving the chain) directly
mutates the `AnimationMontageQueue` side-buffer: appends a
`MontageQueueEntry`, increments `Count`, bumps `QueueVersion`. No
`ActionInstanceId` change. The executor observes the new entry on its next
tick (by noting `QueueVersion != ObservedQueueVersion`) and incorporates
it naturally when the current entry reaches blend-out.

**Replacing the chain with a different one.** Two-step pattern:

1. Brain writes `ActionIdClearMontageQueue` to drain entries *after* the
   currently-playing one. This does *not* bump `ActionInstanceId` — the
   executor observes `Count` shrinking on its next tick and discards the
   future entries. The currently-playing entry continues to its natural
   blend-out.
2. Brain then mutates `AnimationMontageQueue` to append the new entries
   (same mechanism as plain append). The executor, finding new entries
   when the current one reaches blend-out, crossfades into them.

This avoids the `ActionInstanceId` bump that would hard-preempt the
currently-playing montage and create a visible pop. Equivalently, if the
Brain *wants* a hard interrupt, it uses `ActionIdStopMontage` (or a new
`ActionIdPlayMontage` for a different single montage), which does bump
`ActionInstanceId` and triggers the normal preemption flow.

**Aborting a chain entirely.** `ActionIdStopMontage` on `AnimationChannel`,
which bumps `ActionInstanceId`. Dispatcher calls `OnExit` on the queue
executor; executor clears `CurrentEntryIndex`, blends to neutral, publishes
`MontageEndedEvent` with `EndReason = Interrupted`, and the channel's
`Status` goes to `Failure`.

### 4.3 The `[InlineArray]` mutation hazard

`AnimationMontageQueue.Entries` is a C# 12 `[InlineArray]` struct. The
direct-index-assignment-on-ref-component pattern is a silent footgun:

```csharp
// WRONG — silently drops the write
ref var q = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
q.Entries[q.Count] = newEntry;    // mutates a defensive copy, lost on scope exit
q.Count++;
```

The C# compiler emits an `ldobj` for the inline-array access, copying the
array to a temporary stack slot. The mutation modifies the temporary, not
the ECS chunk memory. Two safe patterns must be used everywhere:

```csharp
// PATTERN A — Span<T> cast for in-place mutation
ref var q = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
Span<MontageQueueEntry> entries = q.Entries;
entries[q.Count] = newEntry;
q.Count++;
q.QueueVersion++;

// PATTERN B — Get → mutate → SetComponent
var q = repo.GetComponentRO<AnimationMontageQueue>(entity);
Span<MontageQueueEntry> entries = q.Entries;
entries[q.Count] = newEntry;
q.Count++;
q.QueueVersion++;
repo.SetComponent(entity, q);
```

Every AI primitive that mutates `AnimationMontageQueue` must use one of
these patterns. The Blueprint compiler's codegen for queue-mutation primitives
will be code-reviewed against this — call out in the primitive's docstring
and in the Blueprint compiler tests.

## 5. Animation notify events

Three audiences for these events: Blueprint event graphs (existing
subscription mechanism), Blueprint `WhenNode` Event Fired mode
(auto-discovered via `EngineEventCatalog`), and BTree predicate
infrastructure (`TransientEventPredicateDto` over them, no compiler changes
needed).

Typed events for well-known notifies. Each has `Entity Target` as its first
field so the `EngineEventCatalog` entry declares
`TargetFieldName = "Target"` and `WhenNode` auto-filters to Self.

```
MontageStartedEvent         { Target, MontageId, ActionInstanceId, QueueIndex }
MontageEndedEvent           { Target, MontageId, ActionInstanceId, QueueIndex, EndReason }
MontageSectionAdvancedEvent { Target, MontageId, FromSectionIndex, ToSectionIndex }
FootstepEvent               { Target, WorldPosition, FootIndex, SurfaceTypeHint }
HitWindowOpenedEvent        { Target, MontageId, WindowId }
HitWindowClosedEvent        { Target, MontageId, WindowId }
StanceChangedEvent          { Target, PreviousStance, NewStance }
```

`QueueIndex` is `0xFF` when the montage was a single-shot `PlayMontage`
rather than part of a queue.

`MontageEndedEvent.EndReason`:
- `0 = NaturalEnd` — montage completed to its natural end
- `1 = Interrupted` — hard preempted by `ActionInstanceId` bump
- `2 = BlendedOutByNext` — crossfaded into next queue entry
- `3 = Failed` — couldn't start (capability missing, asset not found, etc.)

One generic catch-all for animator-authored markers that don't warrant a
typed event:

```
AnimNotifyEvent             { Target, MontageId, MarkerHash, PayloadFloat }
```

`MarkerHash` is the stable hash of the marker name authored in UE. A
TKB-attached lookup table on the editor side translates hashes back to
human-readable names for the When-node's event filter UI.

QoS: critical-correctness events (`MontageEndedEvent`, `HitWindowOpenedEvent`,
`StanceChangedEvent`) are Reliable. Cosmetic events (`FootstepEvent`,
generic `AnimNotifyEvent`) can be BestEffort.

Cross-node propagation: each event has an `INetworkEventTranslator`
egress on the Muscle and an ingress on the Brain that republishes onto the
Brain's local `FdpEventBus`, matching the `WeaponFire` / `EntityDamage`
precedent.

## 6. Authoring surfaces

### 6.1 Blueprint Instance with `WhenNode`

The primary authoring surface for character behavior. The designer drops:

- **`ChannelCommand(Animation/PlayMontage)`** with a montage picked from a
  TKB-driven dropdown filtered by the entity's animation set (see §8).
  Optionally wires to a downstream `WaitForChannel(AnimationChannel)` for
  blocking semantics.
- **`ChannelCommand(LookAt/LookAtEntity)`** with a target entity pin, paired
  later with `ChannelCommand(LookAt/ReleaseLook)`.
- For stance: a small **`SetStanceNode`** primitive that writes the
  `StanceIntent` component. (Not a channel command — see §3.1.)
- **`WhenNode(EventFired)`** on any of the registered notify events from §5
  for reactive responses. The catalog dropdown surfaces `MontageEndedEvent`,
  `FootstepEvent`, etc. automatically; no animation-specific compiler work
  needed.
- **`WhenNode(ValueChanged)`** on `AnimationChannel.Status` for "any time
  my current montage status changes" — useful for background reactions
  that don't fit the linear-exec-flow shape of `WaitForChannel`. Same
  pattern on `StanceStatus` gives stance-transition-complete semantics.

For chaining (§4.2), a higher-level primitive — e.g.
**`PlayMontageChainNode`** — accepts an array of montage IDs and emits the
correct side-buffer writes plus the `ActionIdPlayMontageQueue` command in
one go. Authors don't manipulate the side-buffer by hand for the common
case.

### 6.2 BTree and HSM

The existing AiPrimitive dispatch mechanism makes Animation and LookAt
channel commands usable as BTree action nodes and HSM action bodies
unchanged. Notify events are reachable from BTree reactivity
(`TransientEventPredicateDto`) and HSM transition guards (poll-based AiPrim
Condition) with no new primitives.

## 7. Topology and root-motion path

> **⚠ Superseded section — see DD-1 §17 for current truth.** This §7
> remains as the high-level architectural statement (humanoid Muscle
> co-location, future-root-motion phase-ordering hazards). The
> concrete phase-ordering specification with system-by-system slot
> assignments lives in DD-1 §17. If the two ever disagree, DD-1 wins.

Co-locate the Muscle Character with the existing motion / perception /
weapon Muscle. All four subsystems share authority over `SimTransform` and
`SimVelocity`; splitting them onto separate nodes would shatter the
single-writer principle for spatial descriptors and force constant
inter-node authority negotiation for the entity's physical state.

The future flip to root-motion-drives-kinematics is an additive change
orthogonal to the Brain↔Muscle contract. The architect has flagged two
specific Muscle-side issues that will need careful handling when that flip
lands (these are *not* blocking the current iteration but worth recording):

- **Double integration must be prevented.** `LinearKinematicsSystem`
  integrates `SimVelocity` into `SimTransform` for any entity carrying both
  that doesn't have a `VehicleState`. If humanoids carry velocity for
  momentum or jump arcs, the root-motion flip needs an explicit exclusion
  component (e.g., `SuppressLinearKinematics`) on root-motion-active
  entities so the linear integrator skips them while animation is driving.
- **Phase-ordering and `TransformSyncSystem` coexistence.** The animation
  solver's root-delta injection must run within `PostSimulation` so
  `SpatialHashSystem` sees the final position when rebuilding the grid for
  the next frame's `Simulation`. It must also avoid touching remote ghost
  entities whose `SimTransform` is being lerped by `TransformSyncSystem`
  toward `NetworkTransform` — a guard on local-authority is required, or
  the animation solver corrupts the replica's visual position.

Neither concern affects channel contracts. They're recorded here so the
Muscle Character implementation team has them on the radar from day one.

## 8. TKB integration

> **⚠ Superseded section — see DD-4 for current truth.** This §8 is the
> high-level statement of the TKB integration pattern. The
> `CharacterAnimationDefDto` schema, full translator implementation
> (`AnimationTkbTranslator` with hot-reload-aware
> `ConcurrentDictionary` cache), editor `IAnimationTkbQueries` API,
> and seven validation rules (ANIM001–ANIM007) all live in DD-4. If
> the two ever disagree, DD-4 wins.

Each humanoid entity class carries its set of supported animations in TKB
via a new descriptor following the standard pattern:

1. **Define the DTO.** A C# record `CharacterAnimationDefDto` listing the
   supported montage IDs (with per-montage metadata: slot, default
   blend-in/out times, whether it uses root motion), available stances,
   and look-at capability flags.
2. **Decorate with `[TkbDescriptor("Anim.CharacterDef")]`** so the source
   generator registers it with `TkbDescriptorRegistry` and the engine
   parses it from entity JSON.
3. **Implement `ITkbEntityTranslator`** (e.g., `AnimationTkbTranslator`)
   whose `GetConsumedDescriptors()` yields `CharacterAnimationDefDto`.
4. **In `Inject(repo, entity, template)`**, retrieve the descriptor via
   `template.GetDescriptor<CharacterAnimationDefDto>()` and attach the
   runtime ECS components — `AnimationChannel`, `LookAtChannel`,
   `StanceIntent`, `StanceStatus`, `AnimationMontageQueue`,
   `AnimationMontageQueueState`. Every insertion guarded by
   `repo.IsComponentTypeRegistered<T>()` per engine standards.

The same TKB database is consulted by the editor at design time: the
montage-picker dropdown in `ChannelCommand(Animation/PlayMontage)` and
`PlayMontageChainNode` filters its options by querying
`ITkbDatabase.GetDescriptor<CharacterAnimationDefDto>(entityClass)` for
the entity class the Blueprint is being authored against.

`MarkerHash` (§5) → human-readable name lookup is part of the same
descriptor or a sibling descriptor; design detail left for the detailed
design.

## 9. Capability gating

Three new bits added to `ActorCapabilities` (currently a `[Flags]` `byte`
with `CanMove = 1`, `CanShoot = 2`, `CanInteract = 4`):

```
CanPlayAnimations = 8
CanChangeStance   = 16
CanAim            = 32
```

Five bits then remain free within the existing byte footprint; expansion
to `ushort` later is trivial if the budget runs out.

Each channel/intent dispatcher reads `ActorCapabilityState` at
command-arrival time and forces failure if the relevant bit is missing:

- `AnimationDispatcherSystem` → `CanPlayAnimations`. Failure mode:
  `Status = Failure` immediately, no executor entered.
- `LookAtDispatcherSystem` → `CanAim`. Same failure mode.
- The system that consumes `StanceIntent` (Muscle-side) reads
  `CanChangeStance`. If missing, the `StanceIntent` write is ignored and
  `StanceStatus` is unchanged.

Damage / corpse / stunned states naturally strip these bits via existing
capability-state plumbing; no animation-specific code path needed.

## 10. End-to-end round-trip example

A concrete example tying it together — Brain plays a reload montage and
waits for it to end:

1. BTree action node ("Reload") writes `ActionIdPlayMontage` into
   `AnimationChannel.ActiveAction`, fills `ActionParams` with
   `PlayMontageParams { MontageId = Reload_Rifle, BlendIn = 0.1f, ... }`,
   bumps `ActionInstanceId`.
2. `AnimationChannelEgressTranslator` on the Brain detects the
   `ActionInstanceId` change (SmartEgress dirty-flag), publishes
   `AnimationIntent` DDS sample (Reliable, TransientLocal).
3. `AnimationIntentIngressTranslator` on Muscle Character receives,
   writes the local ghost's `AnimationChannel`.
4. `AnimationDispatcherSystem` notices the
   `ActionInstanceId`/`DispatchedInstanceId` mismatch, checks
   `ActorCapabilityState.CanPlayAnimations`, calls `OnExit` on any
   outgoing montage executor and `OnEnter` on the new one. The executor
   calls into the Stride/Unreal animation runtime to enqueue the montage
   onto the configured slot.
5. Animation runtime fires `MontageStartedEvent` immediately (with
   `ActionInstanceId` from the channel command, `QueueIndex = 0xFF`).
6. As the montage plays, the animation runtime fires AnimNotifies →
   Muscle publishes typed notify events (and/or generic `AnimNotifyEvent`)
   onto the local bus.
7. Notify-event translators egress to DDS → ingress on Brain → republish
   onto Brain's bus.
8. On Brain: a `WhenNode(EventFired, MontageEndedEvent)` in the
   Blueprint Instance (filtered to Self via `TargetFieldName`) fires when
   the montage ends. Alternatively, a `WaitForChannel(AnimationChannel)`
   in the BTree returns Success when the dispatcher writes
   `Status = Success` (which is itself replicated back via the
   `AnimationStatus` egress).
9. When the montage's blend-out completes, the Muscle's executor calls
   `OnExit`, writes `AnimationChannel.Status = Success`. Status egress
   replicates back. Brain's `WaitForChannel` sees Success; BTree node
   returns Success; control flow continues to the next BTree node.

## 11. What this proposal does *not* address (out of scope, future work)

- The animation runtime itself — Muscle-side detailed design.
- Asset import pipeline from UE.
- Per-frame animation progress reporting (a "montage is 47% complete"
  gauge for designers). Status is binary Running / Success / Failure for
  now. If progress feedback is needed later, the existing
  `AnimationMontageQueueState.EntryElapsedSeconds` already carries it for
  the queue case and can be exposed via a Blueprint accessor.
- Designer-facing mid-montage section control beyond `StartSectionIndex`.
  If "advance to next section at next break" becomes a real need, it
  slots in as a new action on `AnimationChannel` later.
- Cross-entity animation synchronization (e.g., synchronized takedown
  animations on two entities). The current design treats each entity's
  channels independently. Synchronized animations are a known future
  topic and would likely require a separate coordination component
  spanning the two entities.
- Chains longer than the inline-array capacity `N`. Spillover into a
  managed list or per-entity heap allocation can be added if real
  use cases need it.

## 12. Implementation roadmap (rough)

Suggested order, all behind a feature flag until the full path lands:

1. **TKB descriptor + translator** (§8) — wires up the data path before
   anything else needs it.
2. **`ActorCapabilities` bit additions** (§9) — additive, low-risk.
3. **`StanceIntent` / `StanceStatus` descriptor pair** (§3.1) — simpler
   than the channels, good first integration with Muscle Character.
4. **`AnimationChannel` with `ActionIdPlayMontage` / `ActionIdStopMontage`
   only** — no queue yet. Validates the channel pipeline end-to-end.
5. **`LookAtChannel`** — parallel to (4), independent.
6. **Engine Event Catalog entries + notify event translators** (§5).
   Enables `WhenNode(EventFired)` authoring against animation events.
7. **`AnimationMontageQueue` / `AnimationMontageQueueState` side-buffer
   pair + queue actions** (§4) — the chaining feature.
8. **Higher-level Blueprint primitives** (`PlayMontageChainNode`,
   `SetStanceNode`) — ergonomic wrappers over the raw mechanism.

Each step independently shippable; the queue feature (7) is the largest
single piece and depends on having the basic channel (4) working first.

---

*End of v0.3. The detailed-design promotion is complete — five DDs
ship the implementation specifications, all architect-approved. This
document remains as the canonical architectural-altitude reference
and entry point.*
