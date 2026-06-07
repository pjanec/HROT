# DD-2 — Animation Channel & Side-Buffer Replication — Detailed Design (v1.1)

> **Status:** Architect-approved detailed design for the network
> replication of the animation Brain ↔ Muscle contract. Second of five
> detailed designs splitting the architect-approved v0.2 mini design
> across implementation teams.
> **Changes from v1.0:** All six §10 open questions resolved per
> architect review. §10.6 confirmed: humanoid entity migration is out
> of scope for v1; on transfer, the new Muscle instantiates a fresh
> backend and picks up from latest TransientLocal channel intents.
> §10 retained as Resolutions Summary for traceability. No open
> questions remain.
> **Audience:** Networking team (primary), Muscle Character implementation
> team (informational — replication translators bind to their
> components), engine architect (sign-off).
> **Scope:** DDS topic schemas, QoS choices, egress/ingress translator
> implementations, SmartEgress dirty-detection strategies for each
> component, side-buffer serialization (only `Count` entries shipped),
> `QueueVersion` dirty signaling, and the seven `INetworkEventTranslator`
> pairs. Ownership/authority routing is confirmed unchanged from existing
> humanoid-entity patterns.
> **Out of scope:** The components and events themselves (DD-1 §5 for
> components, DD-3 §3 for events). Local-only event handling (DD-3 §5.2
> covers FootstepEvent's exclusion). Asset import or TKB pipeline
> (DD-4). Blueprint authoring (DD-5).
> **Reads alongside:** v0.2 mini design (§4, §5), DD-1 §§5, 17, DD-3
> §§3, 5, 6, the engine's existing channel-replication patterns
> (`LocomotionChannel` / `WeaponChannel` egress translators).

---

## Table of contents

1. Replication scope at a glance
2. Channel intent/status replication
3. The Stance descriptor pair
4. Side-buffer replication — the novel piece
5. Event translators — the seven typed events
6. Topic naming and QoS table
7. Authority and ownership — what doesn't need change
8. Phase ordering and arrival ordering
9. Bandwidth budget and observability
10. Resolutions summary (from v1.0 review)

---

## 1. Replication scope at a glance

The animation contract introduces the following components and events
that cross the network. The table summarizes direction, mechanism, and
where each one's design lives.

| Item | Direction | Mechanism | Spec'd in |
|---|---|---|---|
| `AnimationChannel` (intent fields) | Brain → Muscle | Channel intent translator | DD-2 §2 |
| `AnimationChannel.Status` etc. | Muscle → Brain | Channel status translator | DD-2 §2 |
| `LookAtChannel` (intent) | Brain → Muscle | Channel intent translator | DD-2 §2 |
| `LookAtChannel.Status` | Muscle → Brain | Channel status translator | DD-2 §2 |
| `StanceIntent` | Brain → Muscle | Descriptor (intent) | DD-2 §3 |
| `StanceStatus` | Muscle → Brain | Descriptor (status) | DD-2 §3 |
| `AnimationMontageQueue` | Brain → Muscle | Side-buffer with version | DD-2 §4 |
| `AnimationMontageQueueState` | Muscle → Brain | Side-buffer with version | DD-2 §4 |
| `MontageStartedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `MontageEndedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `MontageSectionAdvancedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `StanceChangedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `HitWindowOpenedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `HitWindowClosedEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `AnimNotifyEvent` | Muscle → Brain | Event translator pair | DD-2 §5 |
| `FootstepEvent` | (excluded) | n/a — Muscle-local only | DD-3 §5.2 |
| (Internal Muscle state — `AnimationExecutorState`, `LookAtExecutorState`, `CharacterAnimationDefRuntime`) | (n/a) | Not replicated; Muscle-only | DD-1 §5.2 |

Eight components and seven events cross the network. The Muscle-internal
state components from DD-1 §5.2 are never replicated.

## 2. Channel intent/status replication

`AnimationChannel` and `LookAtChannel` follow the existing channel
replication pattern from `LocomotionChannel` and `WeaponChannel`.
Nothing genuinely new here; this section confirms the application of
the pattern.

### 2.1 The pattern, briefly

Each channel has:

- An **intent translator** (egress on Brain, ingress on Muscle) that
  replicates `ActiveAction`, `ActionInstanceId`, `BehaviorInstanceId`,
  `ActionParams`, and `ActionState`-flagged-for-replication if any.
- A **status translator** (egress on Muscle, ingress on Brain) that
  replicates `Status`, `DispatchedInstanceId`, and any
  status-side fields the Brain-side AI primitives need to observe.

The two translators use separate DDS topics (intent and status are
strictly directional). Both follow the standard SmartEgress
dirty-detection pattern: egress only publishes when its observed
inputs have changed since the last published sample.

### 2.2 `AnimationChannel` translators

```
AnimationChannelIntentEgress (Brain-side):
  Topic: hrot/anim/intent/AnimationChannel
  Trigger: change in (ActiveAction, ActionInstanceId, ActionParams[..],
                       BehaviorInstanceId)
  Payload: { Entity, ActiveAction, ActionInstanceId, BehaviorInstanceId,
             ActionParams[32B] }
  QoS: Reliable, TransientLocal

AnimationChannelIntentIngress (Muscle-side):
  Reads sample, writes to local ghost's AnimationChannel.
  Does not touch DispatchedInstanceId (Muscle-side state).

AnimationChannelStatusEgress (Muscle-side):
  Topic: hrot/anim/status/AnimationChannel
  Trigger: change in (Status, DispatchedInstanceId)
  Payload: { Entity, Status, DispatchedInstanceId }
  QoS: Reliable, TransientLocal

AnimationChannelStatusIngress (Brain-side):
  Writes Status and DispatchedInstanceId on the Brain's ghost.
  ActiveAction etc. are Brain-authored; ingress doesn't touch them.
```

`ActionState` is *not* replicated. It's executor working state owned by
the Muscle (e.g., currently-playing section index for the running
montage); the Brain has no use for it and shipping it would burn
bandwidth on a 32B blob per dirty tick. If a Brain-side primitive ever
needs progress data, the dedicated `AnimationMontageQueueState`
side-buffer (§4) carries the public-facing progress signals.

### 2.3 `LookAtChannel` translators

Same shape, different topic. Worth noting one detail: `LookAtChannel`
intents containing `ActionIdLookAtEntity` carry a `TargetEntity`
reference. Cross-node entity references serialize as the engine's
standard network entity ID, resolved on the Muscle side via
`NetworkEntityMap`. Same pattern as `WeaponChannel`'s aim-at-entity
parameter.

```
LookAtChannelIntentEgress / LookAtChannelIntentIngress
LookAtChannelStatusEgress / LookAtChannelStatusIngress

Topics:  hrot/anim/intent/LookAtChannel, hrot/anim/status/LookAtChannel
QoS:     Reliable, TransientLocal (both directions)
```

### 2.4 SmartEgress detail — channels are low-frequency

Channels in animation are *much* lower frequency than `LocomotionChannel`.
A character may play 5-20 montages per minute; aim targets change a
similar number of times. SmartEgress's per-tick dirty check is essentially
free for these channels — no need for any custom optimization.

The dirty signal: `ActionInstanceId != lastPublishedActionInstanceId`
combined with full-`ActionParams`-blob comparison. Both fast.

## 3. The Stance descriptor pair

`StanceIntent` and `StanceStatus` are plain descriptor pairs per
architect ruling (v0.2 Q3), mirroring `NavigationIntent` /
`NavigationStatus`. Standard descriptor replication pattern, no
animation-specific tweaks.

```
StanceIntentEgress (Brain-side):
  Topic: hrot/anim/StanceIntent
  Trigger: change in (TargetStance, BlendTime, Version)
  QoS: Reliable, TransientLocal

StanceIntentIngress (Muscle-side):
  Writes to local ghost's StanceIntent. Muscle's
  StanceTransitionSystem (DD-1 §9) compares Version vs.
  StanceStatus.AckVersion to detect new commands.

StanceStatusEgress (Muscle-side):
  Topic: hrot/anim/StanceStatus
  Trigger: change in (CurrentStance, Phase, TransitionProgress,
                      AckVersion)
  QoS: Reliable, TransientLocal

StanceStatusIngress (Brain-side):
  Writes Brain's ghost. Brain-side authors use
  WhenNode(ValueChanged) on StanceStatus to observe transitions.
```

`TransitionProgress` is a continuously-changing `float` during the
blend, which would normally cause continuous egress. Mitigation: the
SmartEgress for `StanceStatusEgress` ignores `TransitionProgress`
changes alone — it triggers only on `Phase` changes or
`CurrentStance`/`AckVersion` changes. Brain-side observers needing
mid-blend progress are rare (stance transitions are short — typically
0.3s); if a real use case emerges, the field can be added to the
trigger set.

## 4. Side-buffer replication — the novel piece

This is the only genuinely new replication pattern DD-2 introduces.
The montage queue's spec/progress split (DD-1 §5) requires two
side-buffer components to cross the network: `AnimationMontageQueue`
(Brain → Muscle) and `AnimationMontageQueueState` (Muscle → Brain).

### 4.1 The `QueueVersion` dirty signal

Both components carry a `QueueVersion`-like field that bumps on every
meaningful change:

- `AnimationMontageQueue.QueueVersion` — bumped by the Brain whenever
  the queue spec changes (entries added, removed, Count changed).
- `AnimationMontageQueueState.ObservedQueueVersion` — written by the
  Muscle executor when it has consumed the latest spec; this is
  Muscle-internal bookkeeping, *not* a network-relevant field (see
  §4.4).
- `AnimationMontageQueueState` itself doesn't have an explicit version;
  its meaningful changes are `CurrentEntryIndex` transitions and
  `EntryElapsedSeconds` updates.

For `AnimationMontageQueue`, the SmartEgress dirty signal is simply a
`QueueVersion` comparison. The egress does NOT diff `Entries` per-slot
on every tick (would be wasteful for a 128-byte inline array); it
trusts that any Brain-side mutation through the proper safe-mutation
patterns (DD-1 §4.3 Span-cast or Get→Mutate→`SetComponent`) bumps
`QueueVersion`. This is the load-bearing convention.

```
AnimationMontageQueueEgress (Brain-side):
  Topic: hrot/anim/MontageQueue
  Trigger: change in QueueVersion
  Payload: { Entity, Count, QueueVersion,
             Entries[Count]  // ONLY the live entries }
  QoS: Reliable, TransientLocal
```

### 4.2 Partial serialization — only live entries

The `Entries` inline array has capacity 8 but only `Count` entries are
live at any time. The egress serializes only the live entries (Count *
16 bytes each, max 128 bytes), not the full 8-slot array. The ingress
deserializes `Count` entries and writes them into the corresponding
positions of the receiving entity's inline array, then zeros the
unused tail entries (8 - Count).

The wire payload looks like:

```
QueueWirePayload {
  EntityId   target;       // 8 bytes (or whatever the engine uses)
  uint       queueVersion; // 4 bytes
  byte       count;        // 1 byte
  byte[3]    padding;      // 3 bytes for alignment
  MontageQueueEntry[count] entries;  // 16 * count bytes
}
```

Maximum payload at `Count = 8`: 12 + 8 * 16 = 140 bytes. Typical
payload at `Count = 2-3`: 12 + 48 = ~60 bytes. Acceptable for the
Reliable+TransientLocal topic.

The serializer/deserializer pair lives in `Hrot.Animation.Replication`
alongside the translator implementations.

### 4.3 `AnimationMontageQueueState` replication

Muscle → Brain direction. The state component is 16 bytes total and
should not be high-frequency. Trigger conditions:

```
AnimationMontageQueueStateEgress (Muscle-side):
  Topic: hrot/anim/MontageQueueState
  Trigger: change in (CurrentEntryIndex, InBlendOutWindow)
  Payload: { Entity, CurrentEntryIndex, InBlendOutWindow,
             EntryElapsedSeconds }
  QoS: Reliable, TransientLocal
```

`EntryElapsedSeconds` is included in the payload but does *not* drive
the dirty trigger — same pattern as `StanceStatus.TransitionProgress`
in §3. Egress fires on the structural changes (entry advance, blend-out
window entered) and ships the elapsed time as ride-along data. This
gives Brain-side observers a snapshot when they care (entry transition
points) without per-tick bandwidth.

### 4.4 `ObservedQueueVersion` is Muscle-internal

The `ObservedQueueVersion` field on `AnimationMontageQueueState` is
purely Muscle-side bookkeeping — it tracks "what QueueVersion has the
executor seen and consumed?" It's not replicated. The Brain doesn't
need to know it; the Muscle uses it locally to detect spec changes
between ticks.

If a Brain-side primitive ever wants to verify "did the Muscle see my
queue update?", it can compare its locally-cached `QueueVersion`
(from its own `AnimationMontageQueue` write) to the
`AnimationMontageQueueState.CurrentEntryIndex` advances arriving in
the status stream — implicit acknowledgement via observable effect.

### 4.5 Reasoning about lost samples on the queue

The queue is Reliable, so samples shouldn't be lost. But for paranoia:
what if a queue update sample is dropped (TransientLocal would re-send,
but consider a transient gap)?

- The Muscle's `MontageQueueAdvanceSystem` (DD-1 §7) only acts on the
  queue when `QueueVersion != ObservedQueueVersion`. If a sample is
  delayed, the executor simply doesn't see the new state until the
  resend arrives.
- If a sample is lost and TransientLocal recovery succeeds on the next
  publication (Brain bumps `QueueVersion` again for some reason), the
  Muscle catches up.
- If the Brain stops mutating the queue but a sample is lost in
  transit, TransientLocal late-joining recovery handles it. The
  durability classification matters: TransientLocal means a
  late-joining subscriber gets the latest sample. This is the right
  choice for the queue — the latest spec is always sufficient, no
  history needed.

The architect's previous resolution on QoS for channel-shaped data
(v0.2 §5) explicitly chose TransientLocal for this reason.

## 5. Event translators — the seven typed events

The seven cross-node events from DD-3 §5.2 each have an
`INetworkEventTranslator<E>` (egress on Muscle) and an
`INetworkEventIngressTranslator<E>` (ingress on Brain) pair. The
pattern is identical across all seven; this section specifies the
shape once and lists the topic/QoS table.

### 5.1 The pattern

```csharp
namespace Hrot.Animation.Replication;

internal sealed class MontageEndedEventEgress : INetworkEventTranslator<MontageEndedEvent>
{
    public DdsTopicDescriptor TopicDescriptor { get; } = new(
        TopicName: "hrot/anim/MontageEnded",
        TypeName: nameof(MontageEndedEvent),
        Reliability: Reliability.Reliable,
        Durability: Durability.Volatile,
        KeyField: nameof(MontageEndedEvent.Target));

    public void Serialize(in MontageEndedEvent ev, ref Span<byte> buf)
    {
        // Pack: Target (8B), MontageId (4B), ActionInstanceId (4B),
        //       QueueIndex (1B), EndReason (1B), padding (2B) = 20 bytes
        // Use the engine's standard binary writer.
    }
}

internal sealed class MontageEndedEventIngress : INetworkEventIngressTranslator<MontageEndedEvent>
{
    public DdsTopicDescriptor TopicDescriptor => /* same as egress */;

    public MontageEndedEvent Deserialize(ReadOnlySpan<byte> bytes)
    {
        // Symmetric unpack; publish onto Brain's local FdpEventBus
        // by the engine's standard ingress-router mechanism.
    }
}
```

Translators are stateless and shared across all entities. Registered
once at startup via the engine's translator-registration mechanism.

### 5.2 The seven pairs

| Event | Topic | Reliability | Durability | Wire Size (bytes) |
|---|---|---|---|---|
| `MontageStartedEvent` | `hrot/anim/MontageStarted` | Reliable | Volatile | 17 |
| `MontageEndedEvent` | `hrot/anim/MontageEnded` | Reliable | Volatile | 18 |
| `MontageSectionAdvancedEvent` | `hrot/anim/MontageSectionAdv` | Reliable | Volatile | 14 |
| `StanceChangedEvent` | `hrot/anim/StanceChanged` | Reliable | Volatile | 10 |
| `HitWindowOpenedEvent` | `hrot/anim/HitWindowOpened` | Reliable | Volatile | 13 |
| `HitWindowClosedEvent` | `hrot/anim/HitWindowClosed` | Reliable | Volatile | 13 |
| `AnimNotifyEvent` | `hrot/anim/AnimNotify` | Reliable | Volatile | 24 |

All keyed on `Target` (Entity ID) — DDS-level filtering for subscribers
that want to filter at the topic layer (rare; usually filtering happens
at the When-node level after ingress publishes to the local bus).

Wire sizes are approximate (struct field totals; actual serialization
may add header overhead). All are small — animation events are
low-bandwidth.

### 5.3 The local-only event — FootstepEvent

`FootstepEvent` has no translator pair. It's published onto the
Muscle's local `FdpEventBus` by `NotifyEventEmitterSystem` (DD-1 §11)
and never leaves the Muscle. DD-3 §5.2 confirmed the Brain-side
catalog excludes it; `BP2017` validator hard-errors any attempt to
subscribe cross-node.

For future readers: if FootstepEvent ever needs to reach the Brain
(some new use case), add a translator pair following the §5.1 pattern
and flip the catalog's `PropagatesAcrossNodes` flag. Strictly additive
change.

## 6. Topic naming and QoS table

Consolidated topic table for the whole animation contract. Topic names
use `hrot/anim/` prefix for grouping (subject to networking team's
prefix conventions per §10.1).

| Topic | Direction | Type | Reliability | Durability |
|---|---|---|---|---|
| `hrot/anim/intent/AnimationChannel` | B→M | Channel intent | Reliable | TransientLocal |
| `hrot/anim/status/AnimationChannel` | M→B | Channel status | Reliable | TransientLocal |
| `hrot/anim/intent/LookAtChannel` | B→M | Channel intent | Reliable | TransientLocal |
| `hrot/anim/status/LookAtChannel` | M→B | Channel status | Reliable | TransientLocal |
| `hrot/anim/StanceIntent` | B→M | Descriptor | Reliable | TransientLocal |
| `hrot/anim/StanceStatus` | M→B | Descriptor | Reliable | TransientLocal |
| `hrot/anim/MontageQueue` | B→M | Side-buffer | Reliable | TransientLocal |
| `hrot/anim/MontageQueueState` | M→B | Side-buffer | Reliable | TransientLocal |
| `hrot/anim/MontageStarted` | M→B | Event | Reliable | Volatile |
| `hrot/anim/MontageEnded` | M→B | Event | Reliable | Volatile |
| `hrot/anim/MontageSectionAdv` | M→B | Event | Reliable | Volatile |
| `hrot/anim/StanceChanged` | M→B | Event | Reliable | Volatile |
| `hrot/anim/HitWindowOpened` | M→B | Event | Reliable | Volatile |
| `hrot/anim/HitWindowClosed` | M→B | Event | Reliable | Volatile |
| `hrot/anim/AnimNotify` | M→B | Event | Reliable | Volatile |

15 topics total — 8 components/descriptors + 7 events. All Reliable
(no BestEffort anywhere in the animation pipeline per DD-3 §6
resolution). State-bearing topics are TransientLocal (late joiners get
latest state); event topics are Volatile (events are present-tense,
late joiners shouldn't replay historical events as if they're new).

### 6.1 Late-joiner semantics

A node joining the cluster mid-game (or recovering from a brief
disconnect) needs to see the latest authoritative state for each
entity. TransientLocal on the state-bearing topics handles this: the
node will receive the latest `AnimationChannel` intent, status,
queue, queue state, stance intent, and stance status for every
humanoid entity, on connection.

Events that fired *during* the disconnect are *not* replayed —
Volatile durability. This is intentional: if a Brain Blueprint missed
a `MontageEndedEvent` during a network gap, replaying it on reconnect
would cause unwanted behavior triggers far after the visible animation
finished. Better to lose the events and rely on state reconciliation:
the latest `AnimationChannel.Status = Success` arrives via
TransientLocal and a `WaitForChannel` unblocks. Authors writing
behaviors with strict event-required semantics need to design for this
(usually by checking `Status` as a backstop).

## 7. Authority and ownership — what doesn't need change

Per architect Q6/Q7 from v0.2 review, humanoid entities are owned by
the Muscle Character node co-located with motion / perception / weapon
Muscle. The existing `DescriptorOwnershipMap` and
`BrainMuscleOwnershipStrategy` already do per-entity-class ownership
routing.

For DD-2, this means:

- Channel intents (`AnimationChannel`, `LookAtChannel`,
  `StanceIntent`, `AnimationMontageQueue`) are Brain-owned — the Brain
  authors them, the Muscle reads its ghost copy.
- Channel statuses and Muscle-authored components (`Status`,
  `StanceStatus`, `AnimationMontageQueueState`) are Muscle-owned —
  the Muscle authors them, the Brain reads its ghost copy.
- No new authority bits or ownership maps needed. The animation
  components fit the existing humanoid ownership pattern naturally.

When the future root-motion flip lands (v0.2 architect Q7), humanoid
position descriptors move from Brain-ghost-of-Muscle-authored to
Muscle-authoritative. That's a routing-map change, not a translator
change — DD-2 doesn't need revision for that future work.

## 8. Phase ordering and arrival ordering

The Muscle has DD-1 dispatchers that read replicated intents in
`PreSimulation`. The egress translators write replicated state
*before* the Muscle simulation begins, so the dispatchers see
this-tick state.

The engine's standard phase ordering for this flow:

```
Brain tick t:
  PreSimulation:   AI logic writes channel intents, queue, etc.
  PostSimulation:  Intent egresses fire on dirty components.
                   DDS sends.

Muscle tick t+1:
  NetworkReceive:  Ingress translators write to local ghosts.
  PreSimulation:   DD-1 dispatchers run, see this-tick intents.
  Simulation:      AnimationRuntimeBridgeSystem submits to backend.
  PostSimulation:  Status egresses, event egresses, notify drains.

Brain tick t+2:
  NetworkReceive:  Status ingresses + event ingresses arrive.
  PreSimulation:   AI logic observes Status changes (WaitForChannel etc.)
                   and events (WhenNode firings).
```

Two-tick minimum round-trip from Brain intent to Brain observation of
result — same as every other channel in the engine.

### 8.1 Arrival-order independence

The state-bearing topics (channels, descriptors, side-buffers) are
"latest wins" semantics, so out-of-order DDS delivery doesn't cause
correctness problems. If sample N+1 arrives before sample N for the
same entity-and-topic, the engine's standard ingress mechanism keeps
the latest by sequence number; the older sample is discarded.

For events, the FdpEventBus consumer doesn't care about cross-event
ordering — each event is observed independently. If `MontageStartedEvent`
arrives after `MontageEndedEvent` for the same montage in rare
out-of-order delivery, both fire on the Brain bus, and any AI logic
should be tolerant of that. (In practice, Reliable+Volatile with a
single Muscle source means out-of-order is vanishingly rare.)

## 9. Bandwidth budget and observability

Order-of-magnitude estimate for a battle with 50 humanoid characters:

- Channel intents/statuses: ~5 montage commands per minute per
  character × 50 chars = 250 commands/min ÷ 60 = ~4 intent samples
  per second across all entities. Plus ~4 status samples per second.
  Each ~40 bytes. ~320 bytes/sec.
- Queue side-buffer: rare (only when queue is in use). Negligible.
- Stance intent/status: ~1 stance change per character per minute =
  ~1 sample/sec across all. Negligible.
- LookAt channel: highly variable; in active combat, perhaps 1-2
  changes per character per second = 50-100 samples/sec × 40 bytes =
  2-4 KB/sec.
- Events: most active during combat. `HitWindowOpened`/`Closed` paired,
  `MontageStarted`/`Ended` paired. Roughly 4 events × 50 chars × 0.2
  Hz typical = 40 events/sec × 20 bytes = 800 bytes/sec.

Aggregate: ~5-10 KB/sec for animation replication across 50 characters
in active combat. Negligible against the engine's network budget.

### 9.1 Observability — what to instrument

For each topic, instrument:

- Publish rate (samples/sec)
- Aggregate bandwidth (bytes/sec)
- Subscriber count (debug only)

For the channel topics specifically, instrument:

- Dirty-detection false-positive rate (egress fires but payload
  identical to last) — should be near zero with SmartEgress; spikes
  indicate a forgotten `=` comparison in the dirty check.
- Round-trip latency Brain-issue → Muscle-status-back — should be ~2
  ticks; higher indicates issues.

For the queue side-buffer specifically:

- `QueueVersion` bumps that produce no observable Muscle-side effect
  (Muscle's `ObservedQueueVersion` doesn't advance for some reason) —
  indicates a Brain-side mutation that the egress didn't pick up.

Existing engine instrumentation conventions apply; DD-2 doesn't invent
new mechanisms, just specifies what's worth watching.

## 10. Resolutions summary (from v1.0 review)

All six open questions from DD-2 v1.0 received architect rulings;
recorded here for traceability. No body sections needed structural
revision (most rulings confirmed v1.0's leanings); the resolution
status appears here.

### 10.1 ✅ Topic-name prefix convention

**Resolved:** `hrot/anim/` is an acceptable working assumption.
Implementation defers to the networking team during translator
registration to align with established delimiter and capitalization
conventions. No design-level change.

### 10.2 ✅ Partial-array serialization mechanism

**Resolved:** Hand-coding the partial serializer (ship `Count`
entries, max 128 bytes; zero the tail on deserialization) is
approved. Matches engine convention for other variable-length
side-buffers. Reflected in §4.2.

### 10.3 ✅ `LookAtChannel` precision

**Resolved:** Single-precision `Vector3` floats are adequate for
character combat aim targets. Revisit only if a verified long-range
aiming defect emerges.

### 10.4 ✅ `StanceStatus.TransitionProgress` in payload

**Resolved:** Keep in payload, exclude from dirty trigger. ~4 bytes
of structural-change overhead is acceptable; exact progress snapshot
on phase transition is a clean free benefit. Reflected in §3.

### 10.5 ✅ Event egress fan-out

**Resolved:** Standard DDS pub/sub handles multi-reader fan-out
natively. `INetworkEventTranslator` egress requires zero subscriber
awareness. No design-level change.

### 10.6 ✅ Authority handover during entity migration

**Resolved:** Active mid-animation humanoid entity migration is out
of scope for v1. If a humanoid migrates between Muscle nodes, the
new Muscle instantiates a fresh animation backend instance, receives
the latest `TransientLocal` channel intents (the existing
TransientLocal QoS handles this naturally), and begins execution
from that state. Visual continuity across the migration is
explicitly *not* guaranteed in v1 — the character may briefly snap
to neutral pose before the next replicated intent applies. If
verified-frame-perfect handover becomes a real requirement later,
it's a separate iteration touching Muscle-side migration handlers
(not DD-2's translator contract).

This resolution is recorded but does not affect any DD-2 design.
The TransientLocal QoS already chosen in §6 is what makes the v1
migration story work — a late-arriving Muscle gets the latest
channel intent on first connect.

---

**No residual open questions remain.** DD-2 is fully resolved and
approved for implementation.

---

## Summary

DD-2 v1.1 specifies the network replication of the animation Brain ↔
Muscle contract: four channel intent/status translator pairs
(`AnimationChannel`, `LookAtChannel` × intent/status), one descriptor
pair (`StanceIntent`/`StanceStatus`), one side-buffer pair
(`AnimationMontageQueue`/`AnimationMontageQueueState`), and seven event
translator pairs (the cross-node animation events from DD-3 §5.2). The
genuinely novel piece is the side-buffer replication with
`QueueVersion`-driven SmartEgress dirty-detection and partial-array
serialization (`Count` live entries only). Everything else applies the
engine's existing channel/descriptor/event-translator patterns.

All topics are Reliable. State-bearing topics are TransientLocal
(late-joiners get latest); event topics are Volatile (no replay).
Aggregate bandwidth for 50 humanoid characters in active combat: ~5-10
KB/sec. Authority routing requires no changes — humanoid entities fit
existing patterns. Humanoid entity migration is out of scope for v1
(§10.6); the TransientLocal QoS already chosen handles the supported
"fresh backend on new Muscle" case naturally.

All six open questions from v1.0 resolved per architect review.

Next: DD-5 (Blueprint primitives) — the last domino. With DD-1, DD-2,
DD-3, and DD-4 all approved, every contract DD-5 consumes is now
locked in.

---

*End of DD-2 v1.1. Architect-approved for implementation.*
