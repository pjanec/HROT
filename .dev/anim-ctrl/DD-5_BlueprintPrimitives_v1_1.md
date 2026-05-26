# DD-5 — Blueprint Authoring Primitives — Detailed Design (v1.1)

> **Status:** Architect-approved detailed design for the Blueprint
> authoring nodes that let AI designers use the animation contract
> from BTrees, HSMs, and Blueprint Instances. Fifth and final of the
> detailed designs splitting the architect-approved v0.2 mini design
> across implementation teams. The entire five-part animation control
> design is now fully resolved and unblocked for implementation.
> **Changes from v1.0:** All five §14 open questions resolved per
> architect review. §14 converted to Resolutions Summary for
> traceability. Cross-DD cleanup batch (§13) authorized; implementation
> proceeds as DD-1 v1.2, DD-3 v1.3, DD-4 v1.2.
> **Audience:** AI editor team (primary — drawers, validators), Blueprint
> compiler team (primary — codegen, AiPrimitive dispatch), AI designers
> (informational — they use the result), engine architect (sign-off).
> **Scope:** Nine new Blueprint nodes (action nodes that write
> channels/intents/side-buffers, getter nodes for reading queue state),
> their drawers, codegen patterns, validator rules ANIM008-011, the
> `[MontagePicker]` property-drawer attribute extension, and how the
> nodes work uniformly across BTree action contexts, HSM action bodies,
> and Blueprint Instance graphs through the existing AiPrimitive
> dispatch. Explicitly *not* introducing new node kinds — every node
> here is an AiPrimitive consumable by all three subsystems.
> **Out of scope:** The Muscle-side runtime that consumes these
> primitives' outputs (DD-1). Network replication of the components
> they write (DD-2). The Engine Event Catalog and translators (DD-3).
> The TKB descriptor and query API they consume (DD-4). The existing
> `WhenNode` machinery (already approved in the When-Reactivity
> iteration); DD-5 only documents its use against the animation event
> catalog without modifying its mechanics.
> **Reads alongside:** DD-1 §§4, 5, 6 (channel mechanics, queue
> mechanics, `[InlineArray]` safe mutation patterns), DD-2 §4
> (side-buffer dirty signaling), DD-3 §§3, 4.1, 4.2, 6.1
> (event catalog, picker attributes, validator rules), DD-4 §§5, 6
> (TKB query API, validation rules ANIM001-007),
> `When_Reactivity_Iteration_Design_v2_2.md` (existing When-node
> behavior).

---

## Table of contents

1. The node roster — what DD-5 ships
2. What comes from where — cross-DD reference table
3. Action nodes — montages
4. Action nodes — stance
5. Action nodes — look-at
6. Getter nodes — reading queue state
7. The `[MontagePicker]` property-drawer attribute
8. Reuse — `WhenNode` and `WaitForChannel` patterns for animation
9. Codegen — the side-buffer-mutation safety story
10. Validator rules ANIM008–ANIM011
11. AiPrimitive dispatch and cross-subsystem reuse
12. Worked example — "Patrol, see threat, take cover" behavior
13. Cross-DD cleanup — the v1.2 alignment passes
14. Resolutions summary (from v1.0 review)

---

## 1. The node roster — what DD-5 ships

Nine new authoring nodes, plus one property-drawer attribute extension
and four new validator rules.

**Action nodes (7):**

- `PlayMontageNode` — fire a single one-shot montage
- `StopMontageNode` — abort the current montage with blend-out
- `PlayMontageChainNode` — fire a sequenced chain of montages
- `EnqueueMontageNode` — append one entry to a running chain
- `ClearMontageQueueNode` — drop future queue entries
- `SetStanceNode` — request stance transition
- `LookAtPointNode` / `LookAtEntityNode` / `ReleaseLookNode` — aim control

**Getter nodes (2):**

- `GetMontageQueueProgressNode` — read current entry index, elapsed time
- `GetCurrentStanceNode` — read `StanceStatus.CurrentStance`

**Drawer extension (1):**

- `[MontagePicker]` attribute — extends DD-3's `[AnimMarkerPicker]`
  pattern to `MontageId` fields on lifecycle events, so WhenNode
  payload-filter pickers show montage names

**Validator rules (4):**

- ANIM008 — `EnqueueMontageNode` without preceding chain start (warning)
- ANIM009 — `ReleaseLookNode` without preceding look-at acquire (warning)
- ANIM010 — Queue mutation nodes verified using `[InlineArray]`-safe
  codegen patterns (compiler self-check)
- ANIM011 — Cross-subsystem AiPrimitive validation: animation primitives
  used in inappropriate contexts (error)

No new node *kinds* in the editor sense — all nine are AiPrimitives,
the same kind that wraps `LocomotionChannel` and `WeaponChannel`
primitives today. This makes them usable as BTree actions, HSM action
bodies, and Blueprint Instance imperative nodes uniformly (per
`AI_Editor_Shared_Infrastructure.md` §AiPrimitive dispatch).

## 2. What comes from where — cross-DD reference table

Every concept DD-5 consumes traces back to one of the prior detailed
designs:

| Concept | DD-5 uses for | Source |
|---|---|---|
| `AnimationChannel` action IDs | Codegen for action nodes | DD-1 §6 |
| `ActionInstanceId` bump semantics | Codegen rules per action | DD-1 §6.1 |
| Slot routing of montages | Validation (chain same-slot) | DD-1 §4.2 |
| `[InlineArray]` safe mutation patterns | Codegen for queue nodes | DD-1 §4.3 |
| Queue side-buffer mutation conventions | Codegen for chain/enqueue/clear | DD-1 §6.4 |
| `LookAtChannel` action IDs | Codegen for look-at nodes | DD-1 §8 |
| `StanceIntent` Version bump | Codegen for SetStanceNode | DD-1 §9 |
| `AnimationMontageQueueState` field layout | Getter node codegen | DD-1 §5.1 |
| `QueueVersion` bump expectation | Codegen contract (DD-2 trust) | DD-2 §4.1 |
| Catalog entries for events | WhenNode integration | DD-3 §4.1 |
| `[AnimMarkerPicker]` attribute precedent | `[MontagePicker]` extension | DD-3 §3.3 |
| `BP2016`/`BP2017` validators | Defense in depth referenced | DD-3 §5.2, §6.1 |
| `IAnimationTkbQueries` query interface | Drawer picker population | DD-4 §5 |
| `MontageDefDto.Slot` for slot validation | Validator ANIM005 | DD-4 §2, §6 |
| ANIM001-007 validation rules | Codegen self-check + existing | DD-4 §6 |

DD-5 introduces nothing that isn't an application or extension of the
above.

## 3. Action nodes — montages

### 3.1 `PlayMontageNode`

Single-shot montage. Most common animation primitive.

**Drawer inputs:**

- `Montage` (montage picker dropdown, populated by
  `IAnimationTkbQueries.GetPlayableMontages(currentClass)` —
  excludes stance-transition montages per DD-4 §2)
- `BlendInTime` (float, default `-1f` = use montage's `DefaultBlendInTime`;
  the drawer shows this as "(TKB default)")
- `BlendOutTime` (float, default `-1f` = use montage's `DefaultBlendOutTime`;
  the drawer shows this as "(TKB default)")
- `PlayRate` (float, default 1.0)
- `StartSection` (section dropdown, populated from the picked
  montage's `MontageDefDto.Sections` list; default 0)
- `LoopCount` (byte, default 1)
- `Priority` (byte, default 0)
- `InterruptCurrent` (bool, default true — sets `PlayMontageParams.Flags`
  bit 1)

**Codegen (illustrative):**

```csharp
// Generated for PlayMontageNode { Montage = "Reload_Rifle", BlendIn = 0.1f, ... }
{
    ref var __ch = ref world.GetComponentRW<AnimationChannel>(self);
    __ch.ActiveAction = AnimationActionIds.PlayMontage;
    unsafe
    {
        fixed (byte* __paramSlot = __ch.Params)
        {
            *(PlayMontageParams*)__paramSlot = new PlayMontageParams
            {
                MontageId = 0x{hash},   // resolved at compile time from picked name
                BlendInTime = 0.1f,
                BlendOutTime = 0.0f,
                PlayRate = 1.0f,
                StartSectionIndex = 0,
                LoopCount = 1,
                Priority = 0,
                Flags = 0x02,           // InterruptCurrent
            };
        }
    }
    __ch.ActionInstanceId++;
}
```

Matches the existing `ChannelCommandNode` codegen pattern from
`Blueprint_Subsystem_Compiler_Detailed_Design.md`. The
`ActionInstanceId` bump signals the new command to the
`AnimationDispatcherSystem`.

**Validation:**

- ANIM001 — picked montage exists in the entity class's TKB
  `Montages` list (compile error).
- ANIM003 — if the entity class has no `AimConfig` and the montage's
  TKB `Slot` happens to be the aim layer, error. (Unusual case;
  authored content normally wouldn't have this.)

### 3.2 `StopMontageNode`

**Drawer inputs:**

- `BlendOutTime` (float, default 0.1)
- `Reason` (enum: `Normal`, `Forced`; default `Normal`)

**Codegen:**

```csharp
{
    ref var __ch = ref world.GetComponentRW<AnimationChannel>(self);
    __ch.ActiveAction = AnimationActionIds.StopMontage;
    unsafe
    {
        fixed (byte* __paramSlot = __ch.Params)
        {
            *(StopMontageParams*)__paramSlot = new StopMontageParams
            {
                BlendOutTime = 0.1f,
                Reason = 0,
            };
        }
    }
    __ch.ActionInstanceId++;
}
```

### 3.3 `PlayMontageChainNode`

The most complex action node. Atomically:

1. Mutates the `AnimationMontageQueue` side-buffer with the chain
   entries.
2. Bumps `AnimationMontageQueue.QueueVersion`.
3. Writes `ActionIdPlayMontageQueue` to `AnimationChannel.ActiveAction`.
4. Writes overall configuration into `PlayMontageQueueParams`.
5. Bumps `AnimationChannel.ActionInstanceId`.

**Drawer inputs:**

- `Entries` (array, 1..8): each entry is a montage picker + per-entry
  `BlendIntoTime` (float), `PlayRate` (float), `StartSection` (byte).
- `InitialBlendInTime` (float, applies to entry 0)
- `Priority` (byte)

**Codegen — the safety-critical bit:**

```csharp
{
    // 1. Side-buffer mutation (Span-cast safe pattern per DD-1 §4.3)
    {
        ref var __q = ref world.GetComponentRW<AnimationMontageQueue>(self);
        Span<MontageQueueEntry> __entries = __q.Entries;
        __entries[0] = new MontageQueueEntry { MontageId = 0x{h0}, ... };
        __entries[1] = new MontageQueueEntry { MontageId = 0x{h1}, ... };
        __entries[2] = new MontageQueueEntry { MontageId = 0x{h2}, ... };
        __q.Count = 3;
        __q.QueueVersion++;
    }

    // 2. Channel command (separate component, separate ref scope)
    {
        ref var __ch = ref world.GetComponentRW<AnimationChannel>(self);
        __ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
        unsafe
        {
            fixed (byte* __paramSlot = __ch.Params)
            {
                *(PlayMontageQueueParams*)__paramSlot = new PlayMontageQueueParams
                {
                    InitialBlendInTime = 0.1f,
                    Priority = 0,
                    Flags = 0,
                };
            }
        }
        __ch.ActionInstanceId++;
    }
}
```

The `Span<MontageQueueEntry>` cast is the load-bearing safety pattern.
Direct `__q.Entries[0] = ...` would silently land in a defensive copy
per DD-1 §4.3.

ANIM010 (§10) validates that the codegen always uses Span-cast or
Get→Mutate→`SetComponent` — never bare-ref index assignment. The
compiler's test suite will include a dedicated test for this.

**Validation:**

- ANIM001 on every entry's picked montage.
- ANIM005 — all chain entries' montages must share the same slot
  (DD-1 §6.3 restriction). Error if not.
- ANIM012 (NEW) — chain length > 8 (the inline-array capacity).
  Compile error.

### 3.4 `EnqueueMontageNode`

Single-entry append to a running queue. No `ActionInstanceId` bump
(per DD-1 §6.4 — this is queue mutation only, not a new command).

**Drawer inputs:**

- `Montage` (picker; must be same slot as chain in progress —
  designer responsibility, not enforced at compile time since chain
  context isn't always knowable statically)
- `BlendIntoTime` (float)
- `PlayRate` (float, default 1.0)
- `StartSection` (byte, default 0)

**Codegen:**

```csharp
{
    ref var __q = ref world.GetComponentRW<AnimationMontageQueue>(self);
    if (__q.Count < 8)   // capacity guard
    {
        Span<MontageQueueEntry> __entries = __q.Entries;
        __entries[__q.Count] = new MontageQueueEntry
        {
            MontageId = 0x{hash},
            BlendIntoTime = 0.2f,
            PlayRate = 1.0f,
            StartSectionIndex = 0,
            Flags = 0,
        };
        __q.Count++;
        __q.QueueVersion++;
    }
    // else: silent no-op at capacity (logged as warning at runtime
    //       via DebugProbe so designers can investigate during testing)
}
```

No `AnimationChannel` write at all. The Muscle's
`MontageQueueAdvanceSystem` (DD-1 §7) observes the `QueueVersion` bump
on its next tick and consumes the new entry naturally.

**Validation:**

- ANIM001 on picked montage.
- ANIM008 — warning when the static control flow shows no preceding
  chain-start node executed on this entity. Implementation: simple
  data-flow check at the Blueprint scope; doesn't try to be smart
  across event-graph boundaries. False-negative cases acceptable
  (warning, not error).

### 3.5 `ClearMontageQueueNode`

Truncates future queue entries, leaves currently-playing entry alone.
No `ActionInstanceId` bump.

**Drawer inputs:** none.

**Codegen:**

```csharp
{
    ref var __q = ref world.GetComponentRW<AnimationMontageQueue>(self);
    ref var __qs = ref world.GetComponentRO<AnimationMontageQueueState>(self);
    if (__qs.CurrentEntryIndex != 0xFF)
    {
        // Truncate to currently-playing entry only.
        byte __newCount = (byte)(__qs.CurrentEntryIndex + 1);
        if (__q.Count > __newCount)
        {
            // Span-cast to safely zero the tail entries.
            Span<MontageQueueEntry> __entries = __q.Entries;
            for (int __i = __newCount; __i < __q.Count; __i++)
                __entries[__i] = default;
            __q.Count = __newCount;
            __q.QueueVersion++;
        }
    }
    // else: no queue currently active; no-op.
}
```

The zeroing of tail entries isn't strictly needed (the side-buffer
serializer in DD-2 §4.2 only ships `Count` entries) but it makes the
component state cleaner for debugging and recording.

## 4. Action nodes — stance

### 4.1 `SetStanceNode`

Plain descriptor write, not a channel command. Bumps `StanceIntent.Version`
so the Muscle's `StanceTransitionSystem` (DD-1 §9) sees the new request.

**Drawer inputs:**

- `TargetStance` (enum dropdown filtered by
  `IAnimationTkbQueries.GetSupportedStances(currentClass)` —
  unsupported stances greyed out)
- `BlendTime` (float, default `-1f` = use TKB stance-transition's
  `DefaultBlendTime`; the drawer shows this as "(TKB default)"))

**Codegen:**

```csharp
{
    ref var __si = ref world.GetComponentRW<StanceIntent>(self);
    __si.TargetStance = (StanceId)1;   // Crouched
    __si.BlendTime = -1f;              // -1f = use TKB default; drawer shows "(TKB default)"
    __si.Version++;
}
```

**Validation:**

- ANIM002 — picked stance in `SupportedStances`. Compile error if not.

## 5. Action nodes — look-at

### 5.1 `LookAtPointNode`

Channel command. Standard pattern.

**Drawer inputs:**

- `WorldPoint` (Vector3, may be a Blueprint variable or hardcoded)
- `BlendInTime` (float, default 0.1)
- `Priority` (byte, default 0)

**Codegen:**

```csharp
{
    ref var __ch = ref world.GetComponentRW<LookAtChannel>(self);
    __ch.ActiveAction = LookAtActionIds.LookAtPoint;
    unsafe
    {
        fixed (byte* __paramSlot = __ch.Params)
        {
            *(LookAtPointParams*)__paramSlot = new LookAtPointParams
            {
                WorldPoint = __sourcePoint,   // from input pin
                BlendInTime = 0.1f,
                Priority = 0,
            };
        }
    }
    __ch.ActionInstanceId++;
}
```

**Validation:**

- ANIM003 — entity class has `AimConfig` declared. Compile error if
  not.

### 5.2 `LookAtEntityNode`

Same shape; entity pin instead of vector. `LocalOffset` parameter
exposed as a Vector3 input.

**Codegen:**

```csharp
{
    ref var __ch = ref world.GetComponentRW<LookAtChannel>(self);
    __ch.ActiveAction = LookAtActionIds.LookAtEntity;
    unsafe
    {
        fixed (byte* __paramSlot = __ch.Params)
        {
            *(LookAtEntityParams*)__paramSlot = new LookAtEntityParams
            {
                TargetEntity = __sourceEntity,   // from input pin
                LocalOffset = new Vector3(0, 1.5f, 0),  // chest-height by default
                BlendInTime = 0.1f,
                Priority = 0,
            };
        }
    }
    __ch.ActionInstanceId++;
}
```

### 5.3 `ReleaseLookNode`

**Drawer inputs:**

- `BlendOutTime` (float, default 0.2)

**Codegen:**

```csharp
{
    ref var __ch = ref world.GetComponentRW<LookAtChannel>(self);
    __ch.ActiveAction = LookAtActionIds.ReleaseLook;
    unsafe
    {
        fixed (byte* __paramSlot = __ch.Params)
        {
            *(ReleaseLookParams*)__paramSlot = new ReleaseLookParams
            {
                BlendOutTime = 0.2f,
            };
        }
    }
    __ch.ActionInstanceId++;
}
```

**Validation:**

- ANIM009 — warning when the static control flow shows no preceding
  look-at acquire node. Same shape as ANIM008; warning, not error.

## 6. Getter nodes — reading queue state

For Blueprints that want to introspect the queue rather than wait for
events.

### 6.1 `GetMontageQueueProgressNode`

**Drawer inputs:** none.

**Drawer outputs:**

- `IsActive` (bool — true if `CurrentEntryIndex != 0xFF`)
- `CurrentEntryIndex` (byte — 0..7 when active, 0xFF when not)
- `ElapsedSeconds` (float)
- `InBlendOutWindow` (bool)

**Codegen:**

```csharp
{
    ref readonly var __qs = ref world.GetComponentRO<AnimationMontageQueueState>(self);
    __outIsActive = __qs.CurrentEntryIndex != 0xFF;
    __outCurrentEntryIndex = __qs.CurrentEntryIndex;
    __outElapsedSeconds = __qs.EntryElapsedSeconds;
    __outInBlendOutWindow = __qs.InBlendOutWindow;
}
```

No `ActionInstanceId` reference — this is pure read.

### 6.2 `GetCurrentStanceNode`

**Drawer outputs:**

- `Stance` (StanceId enum)
- `IsTransitioning` (bool — true if `Phase == Transitioning`)
- `TransitionProgress` (float, 0..1)

**Codegen:**

```csharp
{
    ref readonly var __ss = ref world.GetComponentRO<StanceStatus>(self);
    __outStance = __ss.CurrentStance;
    __outIsTransitioning = __ss.Phase == StanceTransitionPhase.Transitioning;
    __outTransitionProgress = __ss.TransitionProgress;
}
```

## 7. The `[MontagePicker]` property-drawer attribute

DD-3 §3.3 introduced `[AnimMarkerPicker]` on `AnimNotifyEvent.MarkerHash`
to drive a marker-name dropdown in property drawers. DD-5 extends the
pattern to `MontageId` fields on lifecycle events
(`MontageStartedEvent`, `MontageEndedEvent`,
`MontageSectionAdvancedEvent`, `HitWindowOpenedEvent`,
`HitWindowClosedEvent`), so When-node payload filters can pick by
montage name rather than typing a hash.

```csharp
namespace Hrot.Animation.Events;

/// <summary>
/// Marks an int field as a stable hash of a montage name. Blueprint
/// property drawer substitutes the standard integer input with a
/// montage-name dropdown populated by
/// IAnimationTkbQueries.GetPlayableMontages for the Blueprint's
/// current target entity class. Drawer resolves picked name to a
/// hash at compile time (DD-4 §3.4).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class MontagePickerAttribute : Attribute { }
```

Application:

```csharp
[EventId(8002)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct MontageEndedEvent
{
    public readonly Entity Target;

    [MontagePicker]              // NEW: drawer renders as montage dropdown
    public readonly int MontageId;

    public readonly uint ActionInstanceId;
    public readonly byte QueueIndex;
    public readonly MontageEndReason EndReason;
}
```

DD-3's event type definitions will need a minor revision (v1.3) to
add this attribute to the relevant `MontageId` fields. Cross-DD
follow-up captured in §13.

Same drawer dispatch mechanism as `[AnimMarkerPicker]`; same pattern as
`[HsmEventPicker]` and `[MapPickableEntity]` elsewhere in the engine.

## 8. Reuse — `WhenNode` and `WaitForChannel` patterns for animation

DD-5 doesn't introduce new mechanism for reactive listening; the
existing `WhenNode` (from the When-Reactivity iteration) and
`WaitForChannel` primitives already handle everything authors need.
This section documents the common patterns so designers know the
recipes.

### 8.1 `WaitForChannel(AnimationChannel)` — block until completion

Standard channel-wait pattern. Used after a `PlayMontageNode` to
block until the montage finishes (or fails).

```
[PlayMontageNode: Reload_Rifle]
    ↓ exec
[WaitForChannel: AnimationChannel]
    ↓ Success branch
[ReadyToFireNode] (or whatever comes next)
    ↓ Failure branch
[HandleInterruption]
```

For `PlayMontageChainNode`, the same `WaitForChannel` waits for the
*whole* chain — `Status = Success` fires only when the last entry
blends out (DD-1 §6.3).

### 8.2 `WhenNode(EventFired, MontageEndedEvent)` — react without blocking

For background reactions that don't fit linear exec flow:

```
[WhenNode: EventFired, MontageEndedEvent, MontageId == Reload_Rifle,
           EndReason == NaturalEnd]
    ↓ on fire
[LogReloadCompletedNode]
```

Self-filtering automatic via DD-3 §4.1 `TargetFieldName`.

### 8.3 `WhenNode(ValueChanged, AnimationChannel.Status)` — observe status changes

For higher-level patterns like "any time my current animation gets
interrupted, do X":

```
[WhenNode: ValueChanged, AnimationChannel.Status, NewValue == Failure]
    ↓ on fire
[ReplanCoverNode]
```

### 8.4 `WhenNode(ValueChanged, StanceStatus.Phase)` — observe stance transitions

```
[WhenNode: ValueChanged, StanceStatus.Phase, NewValue == Completed]
    ↓ on fire
[BeginSnipingFromCrouchNode]
```

This is the architect-approved replacement (v0.2 Q3) for "wait for
stance change" — no channel needed because Stance is a descriptor pair.

### 8.5 `WhenNode(EventFired, AnimNotifyEvent)` — react to generic markers

Uses the `[AnimMarkerPicker]` drawer (DD-3 §3.3) for the
`MarkerHash` filter:

```
[WhenNode: EventFired, AnimNotifyEvent, MarkerHash == "MagOut"]
    ↓ on fire
[PlayMagEjectSoundNode]
```

The picker dropdown is populated by
`IAnimationTkbQueries.GetAvailableMarkers(currentClass)`.

## 9. Codegen — the side-buffer-mutation safety story

The single highest-risk codegen path in DD-5 is the side-buffer
mutation in `PlayMontageChainNode`, `EnqueueMontageNode`, and
`ClearMontageQueueNode`. The `[InlineArray]` mutation hazard (DD-1
§4.3) is silent — wrong codegen produces no compile or runtime error,
just silently lost writes.

### 9.1 The required pattern

Every side-buffer write must follow one of two patterns:

**Pattern A (preferred for write-only):** Get a `ref var` to the
component, then immediately Span-cast its inline array:

```csharp
ref var __q = ref world.GetComponentRW<AnimationMontageQueue>(self);
Span<MontageQueueEntry> __entries = __q.Entries;
__entries[i] = newEntry;
__q.Count++;
__q.QueueVersion++;
```

**Pattern B (when you need to read first):** Get RO, copy, mutate, write
back:

```csharp
var __q = world.GetComponentRO<AnimationMontageQueue>(self);
Span<MontageQueueEntry> __entries = __q.Entries;
__entries[i] = newEntry;
__q.Count++;
__q.QueueVersion++;
world.SetComponent(self, __q);
```

### 9.2 What codegen must NOT emit

```csharp
// FORBIDDEN — silent write-loss
ref var __q = ref world.GetComponentRW<AnimationMontageQueue>(self);
__q.Entries[i] = newEntry;   // C# emits ldobj; mutates defensive copy
__q.Count++;                  // this works (Count is scalar)
__q.QueueVersion++;           // this works
// ... but Entries[i] write was lost.
```

### 9.3 ANIM010 — codegen self-check

The Blueprint compiler's emit phase for queue-mutation primitives
includes a static assertion that the generated AST follows Pattern A or
Pattern B. If a refactoring of the codegen template accidentally
produces direct ref indexer assignment on an `[InlineArray]` field,
ANIM010 fires as a compiler-internal error (not a designer-facing
ANIM error — this is the compiler self-checking its own output).

Implementation: the codegen template strings for these three nodes are
versioned and reviewed; a unit test in the compiler test suite verifies
that the emitted code for a representative chain mutation passes
Pattern A or Pattern B's recognized AST shape. New code paths require
the test to be extended.

### 9.4 Test coverage

The DD-5 implementation includes integration tests that:

1. Build a Blueprint with a `PlayMontageChainNode` with 3 entries.
2. Compile and execute it.
3. After execution, read `AnimationMontageQueue` and assert
   `Count == 3` and `Entries[0..2]` carry the expected montage IDs.

If the codegen accidentally regresses to direct-index assignment, the
test sees `Entries[0..2]` all zero (writes lost) and fails. This is
the regression guard for the silent-failure mode.

## 10. Validator rules ANIM008–ANIM011

Existing rules (ANIM001-007) declared in DD-4 §6 cover montage existence,
stance support, aim config, marker existence, slot consistency, and
DTO-level validation. DD-5 adds four more:

### ANIM008 — Enqueue without chain

**Severity:** Warning

**Trigger:** Static control-flow analysis of the Blueprint scope shows
an `EnqueueMontageNode` reached without a preceding
`PlayMontageChainNode`. The analysis is scope-local (within a single
graph); doesn't try to reason across event-graph boundaries.

**Message:** "Enqueue Montage executed without a preceding Play
Montage Chain in this graph. The enqueue will silently no-op at
runtime if no queue is active."

**Rationale:** Helps designers catch the common mistake of using
Enqueue when they meant `PlayMontageNode`. Doesn't block compilation
because cross-graph chain starts are legitimate (an event handler
starts the chain, the Tick graph enqueues more entries).

### ANIM009 — Release without acquire

**Severity:** Warning

**Trigger:** Same control-flow shape — `ReleaseLookNode` without
preceding `LookAtPointNode` or `LookAtEntityNode`.

**Message:** "Release Look executed without a preceding Look At in
this graph. The release will succeed harmlessly at runtime if no aim
is active."

### ANIM010 — Codegen self-check for safe mutation

**Severity:** Internal compiler error (not designer-facing)

**Trigger:** Generated code for queue-mutation primitives fails the
Pattern A/B AST recognition.

**Message:** Compiler bug — fix codegen template.

### ANIM011 — Cross-subsystem AiPrimitive validation

**Severity:** Error

**Trigger:** An animation primitive used in an inappropriate context.
Examples:

- `WaitForChannel(AnimationChannel)` in a Blueprint that's not on a
  humanoid entity class (the class doesn't carry `AnimationChannel`).
- `SetStanceNode` in a Blueprint targeting an entity class with empty
  `SupportedStances` (a non-humanoid class somehow tried to use the
  animation set).

**Message:** "Animation primitive '{X}' used in a context where its
target component is not present on the entity class '{class}'."

This is the catch-all that ensures TKB declarations align with
Blueprint authoring; if a designer mis-types the target class or
forgets to add an entity class to the animation pipeline, this surfaces
clearly.

### Validator rule consolidation

Full list across DD-4 and DD-5, for reference:

| Rule | Severity | Source | Concern |
|---|---|---|---|
| ANIM001 | Error | DD-4 §6 | Montage exists in entity class |
| ANIM002 | Error | DD-4 §6 | Stance in `SupportedStances` |
| ANIM003 | Error | DD-4 §6 | `AimConfig` present for look-at |
| ANIM004 | Warning | DD-4 §6 | Marker exists in entity class |
| ANIM005 | Error | DD-4 §6 | Chain entries share slot |
| ANIM006 | Error | DD-4 §6 | DTO stance-transition montage exists |
| ANIM007 | Error | DD-4 §6 | DTO notify marker exists |
| ANIM008 | Warning | DD-5 §10 | Enqueue without chain start |
| ANIM009 | Warning | DD-5 §10 | Release without acquire |
| ANIM010 | Internal | DD-5 §10 | Codegen self-check |
| ANIM011 | Error | DD-5 §10 | Cross-subsystem context check |
| ANIM012 | Error | DD-5 §3.3 | Chain length > 8 |
| BP2016 | Warning | DD-3 §6.1 | When-node on BestEffort event |
| BP2017 | Error | DD-3 §5.2 | When-node on LocalOnly event from Brain |

Twelve animation-specific rules plus two When-node rules cooperatively.

## 11. AiPrimitive dispatch and cross-subsystem reuse

All nine action nodes (PlayMontage, StopMontage, PlayMontageChain,
Enqueue, ClearQueue, SetStance, LookAtPoint, LookAtEntity,
ReleaseLook) are registered as **AiPrimitives** — the engine's shared
authoring concept that makes a single primitive usable in three
contexts:

1. **As a BTree action node** — the BTree subsystem's
   `AiPrimitiveAction` wraps it. The primitive runs once per BTree
   tick when reached.
2. **As an HSM action body** — the HSM subsystem's `OnEnter` /
   `OnTick` / `OnExit` handlers wrap it. The primitive runs at the
   appropriate state-machine transition or per tick.
3. **As a Blueprint Instance imperative node** — the Blueprint
   compiler emits it inline in the Tick graph or event graph.

This is the same AiPrimitive dispatch that wraps existing primitives
like `MoveToPositionNode` (LocomotionChannel command) and
`FireWeaponNode` (WeaponChannel command). DD-5 introduces nothing new
in the dispatch layer; it just provides the new primitive
implementations.

The getter nodes (`GetMontageQueueProgressNode`,
`GetCurrentStanceNode`) are also AiPrimitives — usable as condition-side
helpers in BTree conditions and HSM transition guards in addition to
Blueprint expressions.

## 12. Worked example — "Patrol, see threat, take cover" behavior

Concrete designer-facing example to validate the design.

### 12.1 The behavior in English

A sniper character patrols a perimeter. When the character sees a
threat, they should:

1. Stop patrolling
2. Aim at the threat
3. Crouch
4. Wait for the stance transition
5. Fire

If the threat is lost during this sequence, abort and return to patrol.

### 12.2 The Blueprint Instance authoring

```
Tick graph:
  [Sequence]
    ├── [Branch: PerceptionState.HasVisibleThreat]
    │      ├── (true): [Goto: EngageGraph]
    │      └── (false): [Patrol behavior tree call]
    └── [Continue]

EngageGraph (sub-flow, called by goto):
  [Sequence]
    ├── [StopMontageNode]                                     ← stop any patrol-anim
    ├── [LookAtEntityNode: TargetEntity = ThreatEntity]
    ├── [SetStanceNode: TargetStance = Crouched]
    ├── [WhenNode: ValueChanged, StanceStatus.Phase == Completed]
    │      └── [PlayMontageNode: Aim_Sniper]
    │              └── [WaitForChannel: AnimationChannel]
    │                       └── [FireWeaponNode]
    └── [End]

ThreatLostHandler (event-graph or background WhenNode):
  [WhenNode: ValueChanged, PerceptionState.HasVisibleThreat == false]
    └── [Sequence]
          ├── [ReleaseLookNode]
          ├── [SetStanceNode: TargetStance = Standing]
          └── [Goto: PatrolGraph]
```

### 12.3 What runs

When threat appears:

1. `StopMontageNode` writes `ActionIdStopMontage` to `AnimationChannel`.
   The Muscle's dispatcher (DD-1 §6) calls executor `OnExit`, montage
   blends out.
2. `LookAtEntityNode` writes `ActionIdLookAtEntity` to `LookAtChannel`
   with `TargetEntity = ThreatEntity`. Muscle bridge resolves the
   entity's position each tick and aims the head/spine via additive
   layer (DD-1 §10).
3. `SetStanceNode` writes `StanceIntent { TargetStance = Crouched,
   Version++ }`. Muscle's `StanceTransitionSystem` (DD-1 §9) plays the
   `Trans_StandToCrouch` montage from TKB's transition table on the
   stance slot. The `Aim_Additive` layer continues running in parallel
   because it's on a different slot (DD-1 §4.3).
4. `WhenNode` watches `StanceStatus.Phase`. When transition completes,
   `Phase` flips from `Transitioning` to `Completed`, the WhenNode
   fires. Underneath, the compiler emits a polling check per
   When-node v2.2.
5. `PlayMontageNode(Aim_Sniper)` writes
   `ActionIdPlayMontage(MontageId=hash("Aim_Sniper"))`. Muscle plays
   the montage on the FullBody slot (overriding the crouched
   locomotion baseline since FullBody slot has higher priority per
   DD-1 §4.1). Aim layer still continues — different slot.
6. `WaitForChannel(AnimationChannel)` polls
   `AnimationChannel.Status`. When `Aim_Sniper`'s blend-out completes
   and Status flips to Success, the wait resolves.
7. `FireWeaponNode` writes to `WeaponChannel` — standard weapon
   pattern, outside DD-5 scope.

Meanwhile, the threat-lost background WhenNode is also evaluated each
tick by the runtime. If threat disappears:

- `ReleaseLookNode` writes `ActionIdReleaseLook` — aim blends out.
- `SetStanceNode(Standing)` triggers reverse stance transition.
- Goto returns to patrol.

### 12.4 What the AI designer never sees

- Slot management — handled by TKB declarations and Muscle backend.
- `ActionInstanceId` bumps — handled by codegen.
- Side-buffer mutation safety — handled by codegen following
  Pattern A/B.
- Replication — handled by DD-2 translators.
- Event catalog wiring — handled by DD-3 registrations.
- Cross-node propagation — handled by DDS.

Authoring is high-level: "play this montage, wait for it, react to
this event." All the infrastructure carries the weight.

## 13. Cross-DD cleanup — the v1.2 alignment passes

With DD-5 introduced, two prior DDs need minor revision to fully align:

**DD-1 v1.2** — alias `NotifyKind` (the `IAnimationBackend`'s
`RawNotifyEvent` discriminator) to `AnimNotifyCategory` declared in
DD-3 §2. Mechanical edit; no contract change.

**DD-3 v1.3** — add `[MontagePicker]` (DD-5 §7) to the `MontageId`
fields on `MontageStartedEvent`, `MontageEndedEvent`,
`MontageSectionAdvancedEvent`, `HitWindowOpenedEvent`,
`HitWindowClosedEvent`. Small additive edit; doesn't change wire
formats or event IDs.

**DD-4 v1.2** — alias `NotifyMarkerKind` (in `NotifyMarkerDefDto.Kind`)
to `AnimNotifyCategory`. Mechanical edit.

I'll batch these three revisions together after DD-5 ships. They're
purely terminology and attribute-addition; no architectural
implications.

## 14. Resolutions summary (from v1.0 review)

All five open questions from DD-5 v1.0 received architect rulings.
Where rulings confirmed v1.0 leanings, no body section needed
revision; the resolution status is recorded here for traceability.
Where they triggered material changes, the relevant body section is
updated.

### 14.1 ✅ ANIM008/ANIM009 data-flow analysis scope

**Resolved:** Conservative, per-graph-only analysis approved. Full
inter-graph data-flow analysis rejected as too expensive and false-
positive-prone for warning-level diagnostics. Reflected (already) in
§10 ANIM008 / ANIM009 specifications.

### 14.2 ✅ Enqueue at capacity

**Resolved:** `DebugProbe` warning on silent no-op when queue is at
capacity. Visible in editor debug mode; no hard crash in shipped
games. Reflected (already) in §3.4 codegen comment.

### 14.3 ✅ Getter node ergonomics

**Resolved:** Multi-output getter nodes approved.
`GetMontageQueueProgressNode` exposes 4 output pins as designed;
`GetCurrentStanceNode` exposes 3. Reduces graph node density vs.
the alternative split-into-many-single-purpose-getters approach.
Reflected (already) in §6.

### 14.4 ✅ `BlendInTime = 0` semantics

**Resolved:** `-1f` sentinel for "use TKB default" approved. `0f`
legitimately means instant blend; `-1f` is the explicit
default-marker. All blend-time inputs across the action nodes use
this convention. The drawer's float input accepts `-1f` and shows
it as "(TKB default)" in the visual representation; designers don't
type `-1` directly unless they know the convention.

This is a refinement to the drawer behavior, not a contract change.
The codegen for `0f` and `-1f` differs at the input-resolution stage
(before the params struct is populated): `-1f` is replaced with the
montage's `DefaultBlendInTime` / `DefaultBlendOutTime` from TKB.

### 14.5 ✅ `PlayMontageChainNode` drawer ergonomics

**Resolved:** Custom property drawer required. The existing array
drawers won't gracefully handle add/remove, reorder, and per-entry
sub-drawer expansion in the visual density `PlayMontageChainNode`
needs.

**Action item:** Implementation team files a formal ticket with the
editor team to implement the custom drawer. The ticket is a
dependency for DD-5's editor delivery; the runtime side
(codegen, validators, AiPrimitive dispatch) can proceed in parallel.

---

**No residual open questions remain.** DD-5 is fully resolved and
approved. The cross-DD cleanup batch (§13: DD-1 v1.2, DD-3 v1.3,
DD-4 v1.2) is architect-authorized for batched application.

---

## Summary

DD-5 v1.1 specifies the nine Blueprint authoring nodes that complete
the AI designer's surface for the animation contract: seven action
nodes (`PlayMontageNode`, `StopMontageNode`, `PlayMontageChainNode`,
`EnqueueMontageNode`, `ClearMontageQueueNode`, `SetStanceNode`,
`LookAtPointNode`/`LookAtEntityNode`/`ReleaseLookNode`) and two
getter nodes (`GetMontageQueueProgressNode`, `GetCurrentStanceNode`).
All nine are AiPrimitives — usable uniformly across BTree, HSM, and
Blueprint Instance contexts via existing dispatch mechanism. No new
node *kinds* introduced; this is the standard authoring extension.

The codegen for queue-mutation nodes strictly follows the
`[InlineArray]` Pattern A/B safety conventions from DD-1 §4.3, with
ANIM010 compiler-internal validation guarding against regressions.
Five new validator rules (ANIM008-ANIM012) extend DD-4's seven for
authoring-surface concerns. The `[MontagePicker]` property-drawer
attribute extends DD-3's `[AnimMarkerPicker]` precedent to lifecycle
events' `MontageId` fields. Reactive authoring uses existing
`WhenNode` infrastructure with the animation event catalog
established in DD-3. The `-1f` sentinel marks "use TKB default" for
blend-time inputs; `0f` legitimately means instant blend.

A worked example in §12 demonstrates the full author surface in
context — a sniper character that engages a threat through stance
change, look-at acquisition, montage-driven aim, and weapon fire,
with parallel threat-lost handling — entirely in visual Blueprint
authoring without touching any animation runtime code.

All five v1.0 open questions resolved per architect review. The
three small cross-DD v1.2/v1.3 alignment passes (§13) are
architect-authorized for batched application immediately after DD-5
finalization.

**With DD-5 approved, all five detailed designs are complete. Total
implementation surface: one canonical enum, eight ECS components
(four replicated + four Muscle-internal), eight cross-node-relevant
event types, one TKB descriptor, one editor query interface, fifteen
DDS topics, seven new translator pairs, nine new Blueprint authoring
nodes, two property-drawer attributes, fourteen validator rules, and
seven new Muscle-side ECS systems plus one capability-reactor
extension. All built on the engine's existing patterns with one
genuinely novel mechanism (side-buffer replication with `QueueVersion`
dirty-signaling).**

---

*End of DD-5 v1.1. The complete five-part animation control design
is architect-approved and unblocked for implementation.*
