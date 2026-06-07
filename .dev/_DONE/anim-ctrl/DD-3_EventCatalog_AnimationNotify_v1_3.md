# DD-3 — Engine Event Catalog: Animation Notify Entries — Detailed Design (v1.3)

> **Status:** Architect-approved detailed design for the Engine Event
> Catalog registrations and cross-node network propagation of animation
> notify events. Third of five detailed designs splitting the
> architect-approved v0.2 mini design across implementation teams.
> **Changes from v1.2:** Cross-DD alignment pass per DD-5 §13 and
> architect authorization. `[MontagePicker]` attribute (defined in
> DD-5 §7) added to the `MontageId` fields on all events that carry
> them (§3.1 lifecycle events, §3.2 backend-drained events). New §3.4
> introduces the attribute reference. No other changes; event IDs,
> payload layouts, QoS, and translator specifications unchanged.
> **Audience:** Blueprint editor team (consumes catalog entries for
> `WhenNode` Event Fired mode), networking team (consumes translator
> specifications for cross-node DDS propagation), Muscle Character
> implementation team (informational — DD-1 publishes the events,
> DD-3 specifies how they're catalogued and shipped), engine architect
> (sign-off).
> **Scope:** The eight animation notify event types' Engine Event
> Catalog entries (display names, `TargetFieldName` for Self-filtering,
> filterable payload fields), the `INetworkEventTranslator` egress and
> ingress pairs for cross-node propagation, the per-event QoS policy,
> and the canonical `AnimNotifyCategory` enum that unifies
> import-time classification (DD-4) with runtime emission (DD-1) with
> catalog registration (DD-3).
> **Out of scope:** The animation runtime that publishes these events
> on the local Muscle `FdpEventBus` (DD-1 §11). The TKB descriptor that
> classifies markers by category (DD-4 §2). The Blueprint authoring
> nodes that consume the catalog entries via `WhenNode` (DD-5). The
> existing `EngineEventCatalog` mechanism itself (Blueprint subsystem
> docs); DD-3 registers entries into it, doesn't redesign it.
> **Reads alongside:** v0.2 mini design (§5), DD-1 §§3, 11, 18 (event
> emission), DD-4 §§2, 3.4 (`NotifyMarkerKind`, marker hashing),
> Blueprint subsystem runtime docs (event-graph subscription mechanism,
> `EngineEventCatalog` shape, `TransientEventPredicateDto`).

---

## Table of contents

1. The event family
2. The canonical `AnimNotifyCategory` enum
3. The eight event type definitions (with engine-mandatory attributes)
4. Engine Event Catalog entries
5. Cross-node propagation — `INetworkEventTranslator` pairs
6. QoS policy table (and the `BP2016` BestEffort guard)
7. Synthesized vs. backend-emitted — what comes from where
8. `WhenNode` filter UX implications
9. Resolutions summary (from v1.0 review)
10. Flaw resolutions from the v1.0 review

---

## 1. The event family

DD-1 §11 specified that the Muscle Character node publishes typed
animation events onto its local `FdpEventBus` either by synthesis from
ECS state transitions (`AnimationStateReporterSystem`) or by draining
from the backend (`NotifyEventEmitterSystem` translating
`RawNotifyEvent`). DD-3 is concerned with everything *after* local
publication:

1. How each event is described to the editor for `WhenNode(EventFired)`
   authoring — its Engine Event Catalog entry.
2. How each event reaches the Brain across the network — its
   `INetworkEventTranslator` egress/ingress pair (or its lack thereof,
   for Muscle-local-only events).
3. What QoS settles each event uses on DDS.

The event family is closed at eight: three lifecycle events
(`MontageStartedEvent`, `MontageEndedEvent`, `MontageSectionAdvancedEvent`),
three "what's happening on this frame" events (`FootstepEvent`,
`HitWindowOpenedEvent`, `HitWindowClosedEvent`), one stance event
(`StanceChangedEvent`), and one generic catch-all (`AnimNotifyEvent`).
Additional typed events can be added later by repeating the DD-3 pattern;
no mechanism work needed.

## 2. The canonical `AnimNotifyCategory` enum

DD-1 §3 declared `NotifyKind` as part of the backend interface (what the
backend emits as a `RawNotifyEvent` discriminator). DD-4 §2 declared
`NotifyMarkerKind` in the descriptor schema (how a marker is classified
at import time). These have the same logical domain — footstep,
hit-window-open, etc. — and must agree on values.

DD-3 declares the canonical enum and the others reference it:

```csharp
namespace Hrot.Animation.Events;

/// <summary>
/// Canonical classification of animation notify markers. Drives which
/// typed FdpEventBus event the Muscle publishes for a given marker
/// (Footstep markers become FootstepEvent, etc.), and is mirrored in
/// DD-4's marker descriptor classification at import time and in DD-1's
/// backend RawNotifyEvent discriminator.
///
/// New categories require a typed event class registered in this
/// namespace, a corresponding entry in DD-3's catalog (§4), and a
/// translator pair (§5).
/// </summary>
public enum AnimNotifyCategory : byte
{
    Generic = 0,            // → AnimNotifyEvent (catch-all)
    Footstep = 1,           // → FootstepEvent
    HitWindowOpened = 2,    // → HitWindowOpenedEvent
    HitWindowClosed = 3,    // → HitWindowClosedEvent

    // Lifecycle events are NOT marker categories — they're synthesized
    // from ECS state transitions by AnimationStateReporterSystem (DD-1
    // §18) without going through the backend's notify draining. Values
    // reserved here for documentation only:
    //   MontageStarted, MontageEnded, MontageSectionAdvanced
    //   StanceChanged
    // These do not appear in marker classifications (DD-4's
    // NotifyMarkerDefDto.Kind).
}
```

`DD-1.NotifyKind` and `DD-4.NotifyMarkerKind` are aliased to or replaced
by `AnimNotifyCategory` in their respective v1.1+ revisions, with byte
values aligned.

## 3. The eight event type definitions

Restated from v0.2 §5 and DD-1 §11, with the exact field layouts DD-3
commits to. All events live in `Hrot.Animation.Events`. All have
`Entity Target` as the first field for Self-filtering (§4).

**Engine-mandatory attributes.** Per engine convention (every event type
used with `FdpEventBus` requires it), each event carries:

- `[EventId(int)]` — unique ID validated at registration time by
  `EventTypeRegistry`. Reserved ID block for animation events is 8000–8099
  (pending §9.7 confirmation with engine architect).
- `[DataPolicy(DataPolicy.NoRecord)]` — prevents these transient bus
  events from flooding the Flight Recorder. Matches the pattern of
  `ClusterStateChangedEvent` and other transient bus events.

### 3.1 Lifecycle events (synthesized by `AnimationStateReporterSystem`)

```csharp
[EventId(8001)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct MontageStartedEvent
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;
    public readonly uint ActionInstanceId;   // correlates to issuing channel command
    public readonly byte QueueIndex;          // 0xFF = single-shot PlayMontage, else 0..N-1
}

[EventId(8002)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct MontageEndedEvent
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;
    public readonly uint ActionInstanceId;
    public readonly byte QueueIndex;          // 0xFF = single-shot
    public readonly MontageEndReason EndReason;
}

public enum MontageEndReason : byte
{
    NaturalEnd = 0,
    Interrupted = 1,
    BlendedOutByNext = 2,
    Failed = 3,
}

[EventId(8003)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct MontageSectionAdvancedEvent
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;
    public readonly byte FromSectionIndex;
    public readonly byte ToSectionIndex;
}

[EventId(8004)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct StanceChangedEvent
{
    public readonly Entity Target;
    public readonly StanceId PreviousStance;
    public readonly StanceId NewStance;
}
```

### 3.2 Backend-drained events (from `RawNotifyEvent` via `NotifyEventEmitterSystem`)

```csharp
[EventId(8010)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct FootstepEvent
{
    public readonly Entity Target;
    public readonly Vector3 WorldPosition;
    public readonly byte FootIndex;           // 0=left, 1=right
    public readonly byte SurfaceTypeHint;     // resolved by Muscle physics surface
}

[EventId(8011)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct HitWindowOpenedEvent
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;
    public readonly byte WindowId;            // melee-attack hit-window id
}

[EventId(8012)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct HitWindowClosedEvent
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;
    public readonly byte WindowId;
}

[EventId(8013)]
[DataPolicy(DataPolicy.NoRecord)]
public readonly struct AnimNotifyEvent          // generic catch-all
{
    public readonly Entity Target;

    [MontagePicker]
    public readonly int MontageId;

    /// <summary>
    /// Stable hash of the marker name authored on the montage. The
    /// [AnimMarkerPicker] attribute drives the Blueprint property
    /// drawer to render this as a marker-name dropdown sourced from
    /// IAnimationTkbQueries.GetAvailableMarkers(currentClass) rather
    /// than as a raw numeric input. The drawer resolves the picked
    /// name to a hash at compile time (DD-4 §3.4 hashing).
    /// </summary>
    [AnimMarkerPicker]
    public readonly uint MarkerHash;

    public readonly float PayloadFloat;
}
```

### 3.3 The `[AnimMarkerPicker]` attribute

A property-drawer attribute mirroring the existing pattern of
`[HsmEventPicker]` and `[MapPickableEntity]`. Recognized by the
Blueprint editor's property drawer dispatch, which substitutes the
standard `uint` numeric input with the marker-name dropdown.

```csharp
namespace Hrot.Animation.Events;

/// <summary>
/// Marks a uint field as a stable hash of an animation marker name.
/// The Blueprint property drawer renders it as a string dropdown
/// populated by IAnimationTkbQueries.GetAvailableMarkers for the
/// Blueprint's current target entity class. At compile time, the
/// designer's picked name is hashed via the DD-4 §3.4 convention and
/// stored as the literal uint hash in the lowered code.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class AnimMarkerPickerAttribute : Attribute
{
}
```

The drawer's class-context source is the same context the WhenNode
already consumes (Blueprint's target entity class — DD-4 §5.1). When
the WhenNode payload filter UI is rendering inputs for the chosen event,
each field's attributes select the drawer; the `[AnimMarkerPicker]`
attribute on `AnimNotifyEvent.MarkerHash` triggers the marker dropdown.

### 3.4 The `[MontagePicker]` attribute (added in v1.3 per DD-5 §7)

Companion attribute to `[AnimMarkerPicker]`, applied to the `MontageId`
fields on `MontageStartedEvent`, `MontageEndedEvent`,
`MontageSectionAdvancedEvent`, `HitWindowOpenedEvent`,
`HitWindowClosedEvent`, and `AnimNotifyEvent` (§3.1 and §3.2). Same
drawer-dispatch pattern: the property drawer substitutes the
standard `int` input with a montage-name dropdown populated by
`IAnimationTkbQueries.GetPlayableMontages(currentClass)`. The
designer picks "Reload_Rifle"; the drawer resolves to the FNV-1a hash
(DD-4 §3.1) at compile time and stores the literal int hash in the
lowered code.

The attribute itself is defined in DD-5 §7 alongside the
`PlayMontageNode` drawer where it's also used. DD-3 references it on
the events because the WhenNode payload-filter drawers need it on
this side of the integration. No DD-3 source code lives in DD-3 for
this attribute — it's a DD-5-owned editor extension that DD-3 events
reference via the attribute marker.

Without this attribute, designers authoring a `WhenNode(EventFired,
MontageEndedEvent)` with a `MontageId equals X` filter would have to
type the int hash directly — practically impossible without a
calculator. With the attribute, they pick "Reload_Rifle" from a
dropdown.

## 4. Engine Event Catalog entries

Each of the eight events gets an Engine Event Catalog registration so
`WhenNode(EventFired)` and `TransientEventPredicateDto` can match
against it.

Entries are declared via the existing `[EngineEvent]` attribute (or
equivalent registration mechanism — exact API per the Blueprint
subsystem's catalog implementation). The pattern, illustrated for
`MontageEndedEvent`:

```csharp
[EngineEvent(
    DisplayName = "Montage Ended",
    Category = "Animation/Lifecycle",
    TargetFieldName = nameof(MontageEndedEvent.Target),
    QoS = EventQoS.Reliable)]
public readonly struct MontageEndedEvent { ... }
```

`TargetFieldName` declares the field that identifies which entity the
event is "about." `WhenNode` Event Fired mode uses this for automatic
Self-filtering: the compiled predicate matches only events where
`event.Target == self`.

`Category` groups events in the WhenNode dropdown UI for designer
discoverability — animation events all live under `Animation/*`.

### 4.1 Full registration table

| Event | Display Name | Category | TargetField | Filterable Fields |
|---|---|---|---|---|
| `MontageStartedEvent` | Montage Started | `Animation/Lifecycle` | `Target` | `MontageId`, `QueueIndex` |
| `MontageEndedEvent` | Montage Ended | `Animation/Lifecycle` | `Target` | `MontageId`, `QueueIndex`, `EndReason` |
| `MontageSectionAdvancedEvent` | Montage Section Advanced | `Animation/Lifecycle` | `Target` | `MontageId`, `ToSectionIndex` |
| `StanceChangedEvent` | Stance Changed | `Animation/Lifecycle` | `Target` | `PreviousStance`, `NewStance` |
| `FootstepEvent` | Footstep | `Animation/Notify` | `Target` | `FootIndex`, `SurfaceTypeHint` |
| `HitWindowOpenedEvent` | Hit Window Opened | `Animation/Notify` | `Target` | `MontageId`, `WindowId` |
| `HitWindowClosedEvent` | Hit Window Closed | `Animation/Notify` | `Target` | `MontageId`, `WindowId` |
| `AnimNotifyEvent` | Anim Notify (Generic) | `Animation/Notify` | `Target` | `MontageId`, `MarkerHash` |

`Filterable Fields` defines which payload properties appear as filter
inputs in the `WhenNode` Event Fired drawer. The fields are chosen for
designer usefulness — `ActionInstanceId` on lifecycle events is
filterable in principle but rarely useful (designers don't know the
runtime ID values), so it's omitted. If filtering by a non-listed field
becomes a real need, append to the registration.

### 4.2 `MarkerHash` filtering — picker integration via attribute

`AnimNotifyEvent`'s `MarkerHash` is a `uint`. Designers don't want to
type `0xA1B2C3D4` — they want to pick `"MagOut"` from a dropdown.

The integration is *explicit*, not implicit: the `MarkerHash` field on
`AnimNotifyEvent` carries the `[AnimMarkerPicker]` attribute (§3.3).
The Blueprint editor's property drawer dispatch recognizes this
attribute and substitutes the standard `uint` numeric input with a
marker-name dropdown populated by
`IAnimationTkbQueries.GetAvailableMarkers(entityClass)` (DD-4 §5).
The compiler resolves the picked name to a hash at compile time (DD-4
§3.4 hashing) and emits `event.MarkerHash == 0xA1B2C3D4` in the lowered
predicate.

This follows the same pattern as `[HsmEventPicker]` and
`[MapPickableEntity]` elsewhere in the engine — drawer dispatch sees the
attribute, picks the custom drawer, no `WhenNode`-specific or compiler-
specific awareness of animation needed.

### 4.3 Filterable fields require existing drawer types

The other filterable fields in §4.1 (`MontageId`, `EndReason`, `FootIndex`,
`WindowId`, `PreviousStance`, `NewStance`) use standard property drawers:

- `MontageId` (int) — same picker concern as `MarkerHash`. Future iteration:
  add `[MontagePicker]` attribute to the `MontageId` fields on
  `MontageStartedEvent`, `MontageEndedEvent`, etc., driving a dropdown
  sourced from `IAnimationTkbQueries.GetPlayableMontages`. For v1, the
  field is plain int and designers pick from a typed-int drawer until
  the attribute is added.
- `EndReason` / `PreviousStance` / `NewStance` (enums) — standard enum
  drawer.
- `FootIndex` / `WindowId` (byte) — standard numeric drawer.

The `[MontagePicker]` attribute is a small follow-up; not blocking v1
since the typed int drawer is usable even if less ergonomic.

### 4.3 Editor-side cosmetic data

`Animation/Lifecycle` and `Animation/Notify` categories should sort
alphabetically together in the WhenNode dropdown. Per editor convention,
the leading category prefix is used for grouping; the existing event
catalog UI handles this.

Icons: lifecycle events use a generic "animation" icon (timeline-with-
dot); notify events use a generic "marker" icon (flag). Existing editor
icon conventions apply; if new icons are needed, they're a cosmetic
follow-up not blocking this DD.

## 5. Cross-node propagation — `INetworkEventTranslator` pairs

Animation events fire on the Muscle and need to reach the Brain (and
the IG for visual ghosts, though the IG case is mostly already covered
by the rendering pipeline — DD-3 focuses on the Brain consumer). Each
event in §3 has a translator pair per the existing pattern
(`WeaponFire`/`EntityDamage`).

### 5.1 The pattern

For each event `E`:

```csharp
// Muscle-side egress (publishes to DDS when event hits local bus)
internal sealed class EEgressTranslator : INetworkEventTranslator<E>
{
    public DdsTopicDescriptor TopicDescriptor => ...;   // per §5.3 below
    public byte[] Serialize(in E ev) { ... }
}

// Brain-side ingress (receives DDS samples, republishes to local bus)
internal sealed class EIngressTranslator : INetworkEventIngressTranslator<E>
{
    public DdsTopicDescriptor TopicDescriptor => ...;
    public E Deserialize(ReadOnlySpan<byte> bytes) { ... }
}
```

Both registered with the engine's existing translator-registration
mechanism. Translators are pure — no side effects, no state — so they're
shared across all entities.

### 5.2 The eight translator pairs

| Event | Cross-Node? | Brain-Side Consumer? | Notes |
|---|---|---|---|
| `MontageStartedEvent` | Yes | Yes | AI may react to montage start; WhenNode filterable. |
| `MontageEndedEvent` | Yes | Yes | Critical — drives `WaitForChannel(AnimationChannel)` indirectly + WhenNode reactions. |
| `MontageSectionAdvancedEvent` | Yes | Yes | Rare but legitimate use (mid-montage AI hooks). |
| `StanceChangedEvent` | Yes | Yes | AI may react to stance changes; doubles as the natural way to detect `StanceStatus.Phase` completion. |
| `FootstepEvent` | **No (Muscle-local only)** | No | Cosmetic — drives footstep audio on Muscle; Brain has no use case. |
| `HitWindowOpenedEvent` | Yes | Yes | Combat-critical — Brain weapon/damage logic may key off it. |
| `HitWindowClosedEvent` | Yes | Yes | Same. |
| `AnimNotifyEvent` (generic) | Yes | Yes | Generic catch-all; designers may attach arbitrary AI reactions. |

`FootstepEvent` is the only Muscle-local-only event in the family. It
fires frequently (every footfall × number of characters) and has no
known Brain consumer. Shipping it would be wasted bandwidth.

**FootstepEvent is excluded from the Brain-side Engine Event Catalog
entirely** (architect's ruling on the v1.0 §9.1 question; see §9.1
Resolutions Summary). It's registered only in the Muscle-side catalog,
where it remains available for any future Muscle-side Blueprint
subsystem. Brain-side `WhenNode(EventFired)` does not list it in the
dropdown — there's nothing to silently fail against.

For defense in depth against any other event that may be marked
`LocalOnly` in the future, the Blueprint compiler enforces validator
rule **`BP2017`**: a hard compile error when a Brain-targeted Blueprint
attempts to subscribe to an event whose catalog entry has
`PropagatesAcrossNodes = false`. Wording:

> "Cross-node reactivity attempted on local-only event: event '{X}' is
> registered with `PropagatesAcrossNodes = false` and will never reach
> this Blueprint's execution node. Move the subscriber to a node where
> this event is locally published, or wrap the data in a cross-node
> typed event."

This rule provides certainty that no future `LocalOnly` event creates a
silent-stall hazard regardless of catalog UX choices.

### 5.3 DDS topic shape

Per family. Each event family gets its own DDS topic to enable
independent QoS settings and reader/writer pairs:

```
Topic name                      Type
hrot/anim/MontageStarted        MontageStartedEvent (serialized)
hrot/anim/MontageEnded          MontageEndedEvent
hrot/anim/MontageSectionAdv     MontageSectionAdvancedEvent
hrot/anim/StanceChanged         StanceChangedEvent
hrot/anim/HitWindowOpened       HitWindowOpenedEvent
hrot/anim/HitWindowClosed       HitWindowClosedEvent
hrot/anim/AnimNotify            AnimNotifyEvent
```

(No topic for `FootstepEvent` — local-only.)

Topic key (the DDS key field for filtering): the `Target` entity ID, so
subscribers on the Brain can DDS-filter to "events for entities this
Brain cares about" if multiple Muscle nodes are publishing into the same
DDS domain. Exact key encoding matches existing patterns for
entity-keyed event topics.

## 6. QoS policy table

QoS per event, following the precedent of v0.2 §5 and the existing
`WeaponFire`/`EntityDamage` patterns.

| Event | Reliability | Durability | Rationale |
|---|---|---|---|
| `MontageStartedEvent` | Reliable | Volatile | AI must observe every start; loss breaks `WaitForChannel`. |
| `MontageEndedEvent` | Reliable | Volatile | Same — loss leaves a behavior stuck waiting. |
| `MontageSectionAdvancedEvent` | Reliable | Volatile | Lower-frequency; cheap to make reliable. |
| `StanceChangedEvent` | Reliable | Volatile | Affects gameplay-relevant state observable from Brain. |
| `FootstepEvent` | (n/a — local-only) | (n/a) | Doesn't cross network. |
| `HitWindowOpenedEvent` | Reliable | Volatile | Combat-correctness — missing the window means missing the hit. |
| `HitWindowClosedEvent` | Reliable | Volatile | Same. |
| `AnimNotifyEvent` (generic) | **Reliable** | Volatile | Edge-triggered animation marker, not high-frequency telemetry. Designers will inevitably wire AI logic to specific markers (e.g., `BeginCover`); BestEffort drops would cause permanent agent stalls indistinguishable from When-node bugs. Reliable is the safer default. |

Volatile durability across the board — late-joining subscribers don't
need historical events. Animation events are present-tense by nature.

### 6.1 The BestEffort wiring guard (`BP2016`)

Even with `AnimNotifyEvent` Reliable by default, any future BestEffort
event added to this catalog (or any other event catalog the WhenNode
sees) creates the same designer footgun: drop a When-node, wire to
behavior, suffer silent UDP-drop stalls.

Defense in depth: extend the Blueprint compiler's WhenNode validator
(Phase M1 in the When-node iteration design) with rule **`BP2016`** —
warning-level diagnostic emitted when a `WhenNode(EventFired)`
references any event whose catalog QoS is `BestEffort`. Wording:

> "Reactive guard on BestEffort event: this When-node may miss occurrences
> if the network drops the underlying UDP packet. Consider promoting the
> event to Reliable in its catalog entry, or restructure the dependent
> behavior to tolerate missed firings."

The diagnostic surfaces in the editor's standard validation pane. It
doesn't block compilation (warning, not error) because some legitimate
patterns *do* tolerate missed firings (cosmetic-only reactions). But
it ensures every BestEffort-wired When-node was an explicit choice, not
an oversight.

## 7. Synthesized vs. backend-emitted — what comes from where

A clarification useful for both teams (Muscle implementation and
Blueprint authors). The eight events fall into two emission paths:

**Synthesized from ECS state transitions** (by
`AnimationStateReporterSystem` in DD-1 §18):
- `MontageStartedEvent`
- `MontageEndedEvent`
- `MontageSectionAdvancedEvent`
- `StanceChangedEvent`

These are derived from observed state changes in
`AnimationExecutorState` / `AnimationMontageQueueState` / `StanceStatus`.
They don't depend on the backend emitting anything via `DrainNotifies` —
the backend may report montage-ended via callback but `NotifyEventEmitterSystem`
discards lifecycle events from the drain and lets the ECS-side
synthesis own them (DD-1 §11.1 / §18 rationale).

**Drained from backend `RawNotifyEvent`** (by `NotifyEventEmitterSystem`
in DD-1 §11):
- `FootstepEvent`
- `HitWindowOpenedEvent`
- `HitWindowClosedEvent`
- `AnimNotifyEvent` (generic)

These reflect markers authored on the montage assets themselves — only
the backend knows when the playhead crosses them. The
`RawNotifyEvent.Kind` discriminator (a value from `AnimNotifyCategory`)
determines which typed event the emitter publishes.

This split is invariant under backend swap. The proprietary backend
will produce `RawNotifyEvent`s the same way Stride does, and the
synthesis path runs on ECS state which is backend-independent.

## 8. `WhenNode` filter UX implications

A few specifics about how the catalog entries translate into editor
experience:

### 8.1 Designer's perspective for a common case

A designer authors: "when this character's reload montage ends, log it
and consider taking cover."

Steps in the editor:
1. Drop `WhenNode` in Event Fired mode.
2. From the event dropdown, pick `Animation/Lifecycle → Montage Ended`.
3. Self-filtering is automatic (TargetFieldName = "Target" wired up by
   the catalog entry).
4. Optional payload filter: `MontageId equals` → picker shows entity
   class's montages → designer picks `Reload_Rifle`.
5. Optional payload filter: `EndReason equals` → enum dropdown → pick
   `NaturalEnd` (ignore `Interrupted`/`Failed` for this branch).
6. Wire the exec output to the response logic.

Underneath, the compiler emits a polling block over
`view.ReadEvents<MontageEndedEvent>()` filtered by
`ev.Target == self && ev.MontageId == 0x... && ev.EndReason ==
MontageEndReason.NaturalEnd`. Standard When-node Event Fired lowering;
no animation-specific compiler work.

### 8.2 The generic AnimNotifyEvent + MarkerHash dropdown

The generic-notify case is the only one with a content-aware payload
field (MarkerHash). The picker integration with
`IAnimationTkbQueries.GetAvailableMarkers` (§4.2) makes this usable.
Without that integration, designers would have to remember/lookup the
hash, which is unworkable.

When the entity class isn't yet locked in for the Blueprint being
authored (a rare case — most Blueprints declare their target class),
the picker falls back to showing all markers from all known classes
with class-prefixed names. Editor concern, not contract concern.

### 8.3 FootstepEvent absent from the Brain-side dropdown

Per the Option B ruling locked in §5.2, `FootstepEvent` does not appear
in any Brain-side `WhenNode(EventFired)` dropdown. The catalog
registration carries `PropagatesAcrossNodes = false`, and the catalog
registration is scoped to the Muscle-side only. The Brain editor
simply never sees the event listed.

If a designer obtains the event type some other way (e.g., copying a
fully-qualified type name into a programmatic Blueprint construction)
and wires a When-node against it on the Brain side, `BP2017`
(§6.1's sibling rule from §5.2) raises a hard compile error before
codegen.

The combination — catalog scoping + `PropagatesAcrossNodes` flag
exclusion + `BP2017` validator — provides triple-redundant protection
against the silent-stall hazard the v1.0 review flagged.

## 9. Resolutions summary (from v1.0 review)

The v1.0 review surfaced four integration-gap flaws with the Blueprint
editor and When-node validator boundary, plus six prior open questions.
All resolved in v1.1; status recorded here for traceability.

### 9.1 ✅ FootstepEvent visibility (was: Option A vs. Option B)

**Resolved: Option B.** FootstepEvent is excluded from the Brain-side
Engine Event Catalog entirely. Registered only in the Muscle-side
catalog (forward-compatible with future Muscle-side Blueprint
subsystems). Brain-side designers never see it as a dropdown option;
no silent stall risk possible. Defense-in-depth: `BP2017` validator
rule (§5.2) hard-errors any attempt to wire a Brain-targeted Blueprint
to any `PropagatesAcrossNodes = false` event regardless of UX. See
§5.2, §8.3.

### 9.2 ✅ Catalog registration API

**Resolved (informationally):** specifics defer to the existing
`EngineEventCatalog` mechanism. The contract DD-3 declares (display
name, category, target field, filterable fields, QoS, optional
`PropagatesAcrossNodes`) fits whatever the existing catalog accepts.

### 9.3 ✅ MontageId filter for non-class-bound Blueprints

**Resolved:** standard `int` drawer is the fallback for Blueprints
without a locked target class. Class-bound Blueprints get the
`[MontagePicker]` enhancement (§4.3) as a small follow-up; not
blocking v1.

### 9.4 ✅ Topic-name conventions

**Resolved (deferred to networking team):** §5.3's `hrot/anim/*`
prefix is illustrative; networking team adapts to existing conventions
during DD-2 implementation. No DD-3 design-level decision pending.

### 9.5 ✅ `MontageEndedEvent.EndReason = Failed` topic split

**Resolved: keep unified.** EndReason is a filterable field; consumers
who care only about Failed can filter at the When-node level. Simpler
than maintaining a separate topic.

### 9.6 ✅ `AnimNotifyCategory` enum location

**Resolved:** stays in `Hrot.Animation.Events` alongside the event
types it categorizes. DD-1 and DD-4 reference it from there in their
next minor revisions (DD-1 v1.2 aliases `NotifyKind`, DD-4 v1.2
aliases `NotifyMarkerKind`).

### 9.7 ✅ Reserved `[EventId]` block for animation events

**Resolved.** The engine architect officially allocates the **8000-8099**
event ID block to animation events. The 8001-8013 assignments proposed
in §3 are confirmed and locked in. Future animation events extend
within this block; coordinate with the architect when nearing the
upper end of the range.

---

## 10. Flaw resolutions from the v1.0 review

The Blueprint-editor / When-node reviewer flagged four integration
gaps. Each addressed in v1.1; recorded here for traceability.

### 10.1 ✅ Flaw 1: `AnimNotifyEvent` BestEffort footgun

**Resolved with both fixes (defense in depth):**

- `AnimNotifyEvent` QoS promoted to **Reliable** (§6). Animation
  notifies are edge-triggered events authored at montage keyframes —
  not high-frequency telemetry. The original "BestEffort by default,
  promote when needed" framing put the burden on every individual
  designer to know which notifies are AI-critical; that was wrong.
  Reliable is the safer default.
- Validator rule `BP2016` added to the When-node compiler (§6.1) to
  warn on any future BestEffort-marked event wired to a When-node.
  General protection beyond this catalog.

### 10.2 ✅ Flaw 2: FootstepEvent local-only visibility

**Resolved:** Option B locked in (§5.2, §8.3). FootstepEvent excluded
from Brain-side catalog entirely; `BP2017` validator hard-errors any
cross-node `LocalOnly` subscription attempt. See §9.1.

### 10.3 ✅ Flaw 3: `MarkerHash` picker UI assumption

**Resolved:** `[AnimMarkerPicker]` attribute added to
`AnimNotifyEvent.MarkerHash` (§3.2, §3.3, §4.2). Mirrors the existing
`[HsmEventPicker]` / `[MapPickableEntity]` pattern — drawer dispatch
sees the attribute and substitutes the marker-name dropdown for the
default integer input. No When-node or compiler animation-awareness
needed.

### 10.4 ✅ Flaw 4: Missing `[EventId]` and `[DataPolicy]` attributes

**Resolved:** Every event type in §3 now carries `[EventId(80xx)]`
and `[DataPolicy(DataPolicy.NoRecord)]`. Reserved ID block 8000-8099
proposed; final allocation pending §9.7 architect confirmation.

---

**No residual open questions remain.** All v1.0 questions, reviewer-
flagged flaws, and the §9.7 `[EventId]` block allocation are resolved.

---

## Summary

DD-3 v1.2 specifies the Engine Event Catalog registrations and
cross-node propagation for the eight animation notify events. Each
event type carries the engine-mandatory `[EventId(80xx)]` and
`[DataPolicy(DataPolicy.NoRecord)]` attributes, using the
architect-allocated 8000-8099 reserved block (8001-8013 in current
use). Catalog entries declare display names, categories, target-field
self-filtering, and filterable payload fields. Seven of eight events
cross the network via `INetworkEventTranslator` pairs with
Reliable+Volatile QoS; `FootstepEvent` is excluded from the Brain-side
catalog entirely. A canonical `AnimNotifyCategory` enum unifies DD-1's
backend discriminator, DD-4's marker classification, and DD-3's typed
event mapping. `MarkerHash` filtering uses an explicit
`[AnimMarkerPicker]` property-drawer attribute integrating with DD-4's
`IAnimationTkbQueries.GetAvailableMarkers`. `AnimNotifyEvent` QoS is
Reliable to prevent silent-stall hazards, with `BP2016` validator
warning providing defense in depth against any future BestEffort
events. `BP2017` validator rule hard-errors on cross-node subscription
to any `LocalOnly` event.

All v1.0 open questions and v1.1 reviewer-flagged flaws resolved per
architect review.

Next: DD-2 (Replication) and DD-5 (Blueprint primitives) — both
unblocked. DD-5's `WhenNode(EventFired, AnimNotifyEvent)` integration
consumes the marker-name picker (§3.3, §4.2) and the `BP2016` / `BP2017`
validator rules.

Cross-DD follow-up: DD-1 and DD-4 minor revisions (v1.2) will alias
their local `NotifyKind` / `NotifyMarkerKind` declarations to the
canonical `AnimNotifyCategory` declared here.

---

*End of DD-3 v1.2. Architect-approved for implementation.*
