# DD-4 — TKB Animation Descriptor — Detailed Design (v1.2)

> **Status:** Architect-approved detailed design for the Transient
> Knowledge Base (TKB) integration that delivers character animation
> data from design-time JSON to runtime ECS components, and exposes
> that data to the editor for design-time validation and picker UI.
> Fourth of five detailed designs splitting the architect-approved
> v0.2 mini design across implementation teams.
> **Changes from v1.1:** Cross-DD alignment pass per DD-5 §13 and
> architect authorization. The local `NotifyMarkerKind` enum declaration
> in §2 replaced with reference to the canonical `AnimNotifyCategory`
> declared in DD-3 §2. Values unchanged; the discriminator field on
> `NotifyMarkerDefDto` now uses the canonical type for consistency
> across DD-1, DD-3, and DD-4. No other changes.
> **Audience:** Tools/pipeline team (primary), AI editor team (primary —
> consumes the design-time query API), Muscle Character implementation
> team (informational — consumes the runtime components), engine
> architect (sign-off).
> **Scope:** The `CharacterAnimationDefDto` descriptor schema, its
> `ITkbEntityTranslator` implementation (`AnimationTkbTranslator`),
> ghost-promotion component injection, montage and notify naming
> conventions, the editor-side `ITkbDatabase` query API for design-time
> consumption, validation rules the Blueprint compiler will use, and
> the hot-reload story.
> **Out of scope:** UE-to-engine asset import pipeline (DD-4 specifies
> the *boundary* — what import produces as TKB input — but not the
> import implementation itself). Runtime animation systems (DD-1).
> Network replication of the components this translator injects (DD-2).
> Engine Event Catalog registrations for notify events (DD-3 — but DD-4
> defines the marker-name → hash convention DD-3 consumes). Blueprint
> primitives that use the design-time query API (DD-5).
> **Reads alongside:** v0.2 mini design (§§5, 8, 9), DD-1 (§§3, 4, 5
> for the components this translator injects), architect's reply on
> v0.2 Q4.

---

## Table of contents

1. The data pipeline at a glance
2. The `CharacterAnimationDefDto` schema
3. Stable IDs — montages, stances, slots, notifies
4. The `AnimationTkbTranslator`
5. Design-time editor query API
6. Validation rules for the Blueprint compiler
7. Hot reload
8. Worked example — Sniper character class end to end
9. Resolutions summary (from v1.0 review)

---

## 1. The data pipeline at a glance

Four stages, three boundaries:

```
[UE author] → [Asset import pipeline] → [TKB design-time JSON] → [Translator at promotion] → [Runtime ECS]
            (out of DD-4 scope)        (DD-4 §2 schema)         (DD-4 §4 translator)        (DD-1 contracts)

                                         ↓ editor reads
                                       [ITkbDatabase query API]
                                         (DD-4 §5)
                                         ↓
                                       [Blueprint editor: pickers,
                                        validation; WhenNode filter UI]
```

The UE author creates montages, marks slots and notify markers on them
using UE editor conventions. The asset import pipeline produces, for
each montage, a small per-asset JSON record carrying the metadata that
matters at runtime (duration, sections, notifies, slot tag, root-motion
flag). These per-asset records are referenced from per-character-class
JSON authored by character designers — the `CharacterAnimationDefDto`
described in §2.

At promotion time, `AnimationTkbTranslator` reads
`CharacterAnimationDefDto` from the template, bakes it into
`CharacterAnimationDefRuntime` (and attaches the channel/queue/intent
components from DD-1 §5.1), and the Muscle Character runtime takes over.

At design time, the editor reads the same `CharacterAnimationDefDto` via
`ITkbDatabase` (§5) to populate pickers and run validation.

## 2. The `CharacterAnimationDefDto` schema

The TKB descriptor that drives everything else. Decorated with
`[TkbDescriptor("Anim.CharacterDef")]` so the source generator
registers it with `TkbDescriptorRegistry`.

```csharp
namespace Hrot.MuscleCharacter.Animation.Tkb;

[TkbDescriptor("Anim.CharacterDef")]
public sealed record CharacterAnimationDefDto
{
    /// <summary>
    /// Slot definitions for this character class. Determines which slots
    /// exist in AnimationExecutorState and how the backend composes them.
    /// </summary>
    public required IReadOnlyList<SlotDefDto> Slots { get; init; }

    /// <summary>
    /// Montages this character class can play. The Blueprint editor's
    /// montage-picker dropdown is filtered to this list.
    /// </summary>
    public required IReadOnlyList<MontageDefDto> Montages { get; init; }

    /// <summary>
    /// Stances this character class supports (subset of the universal
    /// StanceId enum). Stance pickers in the editor are filtered to this.
    /// </summary>
    public required IReadOnlyList<StanceId> SupportedStances { get; init; }

    /// <summary>
    /// Stance transition table: which montage drives transitions between
    /// each stance pair. Missing entries mean the transition is direct
    /// (snap, no blend). Used by StanceTransitionSystem (DD-1 §9).
    /// </summary>
    public required IReadOnlyList<StanceTransitionDto> StanceTransitions { get; init; }

    /// <summary>
    /// Aim/look-at configuration. Null/absent means this character class
    /// doesn't support aim-offset (LookAtChannel commands will fail
    /// with CanAim capability check).
    /// </summary>
    public AimConfigDto? AimConfig { get; init; }

    /// <summary>
    /// Notify marker registry — maps marker names authored on montages
    /// to stable hashes used in AnimNotifyEvent.MarkerHash. Populated
    /// by the asset import pipeline; baked here for editor display.
    /// </summary>
    public required IReadOnlyList<NotifyMarkerDefDto> NotifyMarkers { get; init; }
}

public sealed record SlotDefDto
{
    /// <summary>Stable byte ID (0..255).</summary>
    public required byte SlotId { get; init; }

    /// <summary>Human-readable name for editor display ("FullBody", "UpperBody").</summary>
    public required string Name { get; init; }

    /// <summary>Bones included in this slot's blend mask. Bone names
    /// match the skeleton's hierarchy as exported from UE.</summary>
    public required IReadOnlyList<string> BoneMask { get; init; }

    /// <summary>Override or Additive compositing.</summary>
    public required SlotCompositingMode Mode { get; init; }

    /// <summary>Priority — higher wins on shared bones.</summary>
    public required int Priority { get; init; }
}

public enum SlotCompositingMode : byte { Override = 0, Additive = 1 }

public sealed record MontageDefDto
{
    /// <summary>Stable string name. Hashed to MontageAssetId (int) for
    /// runtime. See §3 for hashing convention.</summary>
    public required string Name { get; init; }

    /// <summary>Asset path or reference understood by the active backend
    /// (Stride: path to the imported AnimationClip; future proprietary
    /// backend: its own reference form).</summary>
    public required string AssetRef { get; init; }

    /// <summary>Which slot this montage plays on. Must reference a SlotId
    /// declared in Slots above.</summary>
    public required byte Slot { get; init; }

    /// <summary>Default blend-in time, used when a PlayMontage command
    /// doesn't override. Seconds.</summary>
    public required float DefaultBlendInTime { get; init; }

    /// <summary>Default blend-out time. Seconds.</summary>
    public required float DefaultBlendOutTime { get; init; }

    /// <summary>Total montage duration in seconds (informational; editor
    /// uses for display, runtime queries the backend for actual playback).</summary>
    public required float DurationSeconds { get; init; }

    /// <summary>Section names in order. Index in this list = section
    /// index used in PlayMontageParams.StartSectionIndex.</summary>
    public required IReadOnlyList<string> Sections { get; init; }

    /// <summary>Markers carried on this montage that will fire as notify
    /// events at runtime. Used by the editor to populate WhenNode's
    /// Event Fired filter dropdown for AnimNotifyEvent.</summary>
    public required IReadOnlyList<MontageNotifyRefDto> Notifies { get; init; }

    /// <summary>If true, this montage drives root-motion. Future-use
    /// flag; not yet read by DD-1's runtime.</summary>
    public bool UsesRootMotion { get; init; }

    /// <summary>If true, this montage is for stance transitions only
    /// and not exposed in the Blueprint editor's general PlayMontage
    /// picker. StanceTransitionDto references it directly.</summary>
    public bool IsStanceTransition { get; init; }
}

public sealed record MontageNotifyRefDto
{
    /// <summary>The marker's stable name (must appear in
    /// CharacterAnimationDefDto.NotifyMarkers).</summary>
    public required string MarkerName { get; init; }

    /// <summary>Time in seconds from montage start when the marker fires.
    /// Informational; the backend evaluates this at runtime.</summary>
    public required float TimeSeconds { get; init; }

    /// <summary>Optional payload values baked at import (e.g. footstep
    /// foot index, hit-window ID).</summary>
    public float PayloadFloat { get; init; }
    public byte PayloadByte { get; init; }
}

public sealed record StanceTransitionDto
{
    public required StanceId From { get; init; }
    public required StanceId To { get; init; }

    /// <summary>Name of the montage (must appear in Montages with
    /// IsStanceTransition = true) that plays this transition.</summary>
    public required string TransitionMontageName { get; init; }

    /// <summary>Default blend time for this transition. Seconds.</summary>
    public required float DefaultBlendTime { get; init; }
}

public sealed record AimConfigDto
{
    /// <summary>Maximum aim yaw range relative to character facing (deg).</summary>
    public required float MaxYawDegrees { get; init; }

    /// <summary>Maximum aim pitch range (deg, up/down symmetric).</summary>
    public required float MaxPitchDegrees { get; init; }

    /// <summary>Bone driving the aim direction (typically head or neck).</summary>
    public required string AimSourceBone { get; init; }
}

public sealed record NotifyMarkerDefDto
{
    /// <summary>Stable marker name as authored in UE.</summary>
    public required string Name { get; init; }

    /// <summary>Hash computed at import; stored here so editor doesn't
    /// recompute. See §3 for hashing convention.</summary>
    public required uint Hash { get; init; }

    /// <summary>Discriminates between generic markers and typed notifies.
    /// Drives which FdpEventBus event the Muscle publishes (DD-3 maps
    /// these to typed events; generic markers go to AnimNotifyEvent).
    /// Uses the canonical AnimNotifyCategory enum declared in DD-3 §2.</summary>
    public required AnimNotifyCategory Kind { get; init; }
}

// Note: The Kind discriminator uses AnimNotifyCategory (DD-3 §2) — the
// canonical enum unifying import-time classification (here), backend
// discrimination (DD-1 §3 RawNotifyEvent.Kind), and runtime event-catalog
// mapping (DD-3 §4). Only the marker-relevant values (Generic, Footstep,
// HitWindowOpened, HitWindowClosed) appear in DTO authoring; lifecycle
// values (MontageStarted etc.) are reserved in the enum for documentation
// but never set on a marker.
```

The schema is verbose but every field earns its place. The DTO is the
single source of truth for "what can this character do animation-wise"
across editor (picker filtering, validation) and runtime (translator
output).

## 3. Stable IDs — montages, stances, slots, notifies

Four kinds of IDs flow through the system; each needs a stability story.

### 3.1 Montage IDs

`MontageAssetId` (an `int`) is the runtime handle for a montage. It's
derived from the montage's stable string `Name` via deterministic hash:

```
MontageAssetId = (int) FNV1a64(montageDef.Name) & 0x7FFFFFFF;
```

Using 31 bits avoids sign issues and gives ~2.1B possible IDs; collision
risk is negligible at character-class scale (typical character class has
under 100 montages).

**Rename is a migration.** Renaming a montage in JSON changes its ID;
any Blueprint that referenced the old name breaks. The Blueprint
editor's validation (§6) catches this at compile time: "referenced
montage `Reload_Rifle_Old` not found in entity class `Sniper`."

If genuine renames are common, an alternative is to add a stable
`Id` field (GUID) to `MontageDefDto` alongside `Name`, with hashing
fallback for unset GUIDs. **Recommend deferring** until a real workflow
shows renames are painful.

### 3.2 Stance IDs

`StanceId` is a byte enum with universal values:

```csharp
public enum StanceId : byte
{
    Standing = 0,
    Crouched = 1,
    Prone = 2,
    // Add more as needed; max 256 stances total ever.
}
```

Enum values are stable by convention — new stances append, never
re-number. Editor pickers filter to the per-class `SupportedStances`
list.

### 3.3 Slot IDs

`SlotId.Value` is a byte declared per character class in `SlotDefDto`.
Slots are class-local — slot 100 in character class A and slot 100 in
character class B are unrelated. The standard slot layout suggested in
DD-1 §4.1 (Locomotion=0, FullBody=100, UpperBody=200, etc.) is a
*convention* across character classes for readability; the runtime
doesn't care if a class uses different numbering.

### 3.4 Notify marker hashes

`MarkerHash` (a `uint`) is computed from the marker's `Name` at asset
import:

```
markerHash = FNV1a32(markerName);
```

The hash is stored in `NotifyMarkerDefDto.Hash` for editor display (so
the editor can show "Hash 0x12345678 = `Footstep_Left`"). At runtime,
`AnimNotifyEvent.MarkerHash` carries the hash; the editor's reverse
lookup `hash → name` reads `NotifyMarkers` from the relevant class's
DTO.

If two characters in the same world use markers with the same name
(`Footstep_Left`), they collide on the same hash. That's fine — the
hash is the *identity* of the marker concept, and `AnimNotifyEvent`
carries the `Target` entity for disambiguation.

## 4. The `AnimationTkbTranslator`

Implements `ITkbEntityTranslator`. Runs during ghost promotion.

```csharp
namespace Hrot.MuscleCharacter.Animation.Tkb;

public sealed class AnimationTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<Type> GetConsumedDescriptors()
    {
        yield return typeof(CharacterAnimationDefDto);
    }

    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        var def = template.GetDescriptor<CharacterAnimationDefDto>();
        if (def is null) return;  // template doesn't have animation data; non-humanoid

        // --- Replicated/contractual components (DD-1 §5.1) ---

        if (repo.IsComponentTypeRegistered<AnimationChannel>())
            repo.AddComponent(entity, default(AnimationChannel));

        if (repo.IsComponentTypeRegistered<LookAtChannel>() && def.AimConfig is not null)
            repo.AddComponent(entity, default(LookAtChannel));

        if (repo.IsComponentTypeRegistered<StanceIntent>())
            repo.AddComponent(entity, new StanceIntent
            {
                TargetStance = def.SupportedStances[0],  // default to first declared stance
                Version = 0,
            });

        if (repo.IsComponentTypeRegistered<StanceStatus>())
            repo.AddComponent(entity, new StanceStatus
            {
                CurrentStance = def.SupportedStances[0],
                Phase = StanceTransitionPhase.Completed,
                AckVersion = 0,
            });

        if (repo.IsComponentTypeRegistered<AnimationMontageQueue>())
            repo.AddComponent(entity, default(AnimationMontageQueue));

        if (repo.IsComponentTypeRegistered<AnimationMontageQueueState>())
            repo.AddComponent(entity, new AnimationMontageQueueState
            {
                CurrentEntryIndex = 0xFF,
            });

        // --- Runtime-only components (DD-1 §5.2) ---

        if (repo.IsComponentTypeRegistered<CharacterAnimationDefRuntime>())
            repo.AddComponent(entity, BakeDef(def));

        if (repo.IsComponentTypeRegistered<AnimationExecutorState>())
            repo.AddComponent(entity, default(AnimationExecutorState));

        if (repo.IsComponentTypeRegistered<LookAtExecutorState>() && def.AimConfig is not null)
            repo.AddComponent(entity, default(LookAtExecutorState));
    }

    private static CharacterAnimationDefRuntime BakeDef(CharacterAnimationDefDto dto)
    {
        // Bake DTO into runtime-friendly form:
        //  - Hash montage names to MontageAssetId values
        //  - Build hash-keyed dictionaries for fast runtime lookup:
        //      Dictionary<MontageAssetId, MontageRuntimeInfo>
        //      Dictionary<(StanceId, StanceId), StanceTransitionRuntimeInfo>
        //  - Capture per-class slot table (sorted by priority)
        //  - Capture AimConfig snapshot
        // The CharacterAnimationDefRuntime component carries pointers/handles
        // into a per-class shared baked structure (since the data is
        // class-level not instance-level, no need to duplicate per entity).
        // Exact runtime data structure layout TBD with team but the
        // dictionary-of-baked-records pattern is standard.
        return new CharacterAnimationDefRuntime { /* baked handles */ };
    }
}
```

### 4.1 Per-class baked data — translator-owned cache

`CharacterAnimationDefRuntime` per entity is small (a handle to
per-class baked data). The baked data itself — the
`Dictionary<MontageAssetId, MontageRuntimeInfo>`, slot table, transition
table, aim config — is keyed by character class (TKB template ID) and
shared across all entities of that class.

Per §9.1 (architect-approved): the engine has no unified per-class
baked-data cache pattern, so `AnimationTkbTranslator` owns its own.
Concrete shape:

```csharp
public sealed class AnimationTkbTranslator : ITkbEntityTranslator, IDisposable
{
    // Keyed by stable class identifier (template ID or template-name hash;
    // exact key type matches how TKB identifies classes engine-wide).
    private readonly ConcurrentDictionary<long, CharacterAnimationBakedData> _cache = new();

    private readonly ITkbHotReloadEvents _hotReload;

    public AnimationTkbTranslator(ITkbHotReloadEvents hotReload)
    {
        _hotReload = hotReload;
        _hotReload.DescriptorChanged += OnDescriptorChanged;
    }

    private void OnDescriptorChanged(TkbDescriptorChangedEventArgs e)
    {
        // Invalidate per-class cache entries for changed templates.
        // Only react to changes affecting CharacterAnimationDefDto on
        // a class we've cached.
        if (e.DescriptorType != typeof(CharacterAnimationDefDto)) return;
        _cache.TryRemove(e.TemplateKey, out _);
    }

    public void Dispose()
    {
        _hotReload.DescriptorChanged -= OnDescriptorChanged;
    }

    // Inject() retrieves or builds the cached entry:
    private CharacterAnimationBakedData GetOrBake(long classKey, CharacterAnimationDefDto dto)
    {
        return _cache.GetOrAdd(classKey, _ => Bake(dto));
    }
}
```

This pattern is local to the translator — no engine-level mechanism
required. Hot-reload safety is achieved by the cache invalidation on
`DescriptorChanged`; the next entity of that class promoted after the
reload triggers a re-bake on demand. Entities already promoted before
the reload are updated by the hot-reload consequences described in §7.

### 4.2 Translator ordering and dependencies

`AnimationTkbTranslator` only injects components — it doesn't read state
written by other translators, doesn't write state other translators
depend on. So ordering with respect to other translators doesn't matter.
(If TKB's existing translator-ordering mechanism requires a declared
order, declare `AnimationTkbTranslator` as having no dependencies.)

## 5. Design-time editor query API

The editor needs efficient queries against `CharacterAnimationDefDto`
data, indexed by character class. The standard `ITkbDatabase` API
already supports `GetDescriptor<T>(entityClass)`; DD-4 adds a small
service layer on top for animation-specific queries.

```csharp
namespace Hrot.Editor.AiShared.Catalog;

public interface IAnimationTkbQueries
{
    /// <summary>All montages available to this entity class, excluding
    /// stance-transition montages (those are hidden from the general
    /// PlayMontage picker).</summary>
    IReadOnlyList<MontageDefDto> GetPlayableMontages(string entityClass);

    /// <summary>Look up a montage by name. Returns null if not in the
    /// class's def.</summary>
    MontageDefDto? GetMontage(string entityClass, string montageName);

    /// <summary>Stances supported by this class.</summary>
    IReadOnlyList<StanceId> GetSupportedStances(string entityClass);

    /// <summary>Whether the class supports aim/look-at.</summary>
    bool SupportsAim(string entityClass);

    /// <summary>All notify markers usable by this class (union over all
    /// its montages' Notifies). Used by WhenNode's AnimNotifyEvent
    /// filter UI to populate the marker dropdown.</summary>
    IReadOnlyList<NotifyMarkerDefDto> GetAvailableMarkers(string entityClass);

    /// <summary>Reverse lookup hash → name for editor display.</summary>
    string? GetMarkerName(string entityClass, uint hash);

    /// <summary>Resolve a montage name to its runtime MontageAssetId.
    /// Used by the Blueprint compiler when generating PlayMontageParams
    /// from a montage-picker selection.</summary>
    int ResolveMontageId(string entityClass, string montageName);
}

internal sealed class AnimationTkbQueries : IAnimationTkbQueries
{
    private readonly ITkbDatabase _db;

    // Implementation reads CharacterAnimationDefDto via _db.GetDescriptor
    // for the given entity class and filters/projects per query.
    // Caches per (entityClass, query) results aggressively since TKB
    // data is immutable between hot-reload events.
}
```

The Blueprint editor's `ChannelCommand(Animation/PlayMontage)` node
drawer (in DD-5) queries this service to populate its montage picker.
The `WhenNode` drawer (Event Fired mode) queries
`GetAvailableMarkers` when the picker has selected `AnimNotifyEvent` to
filter the marker name dropdown.

### 5.1 Entity class context — where does it come from?

The editor needs to know "which entity class is the currently-edited
Blueprint targeting?" so it can filter pickers. Two cases:

- **Class Blueprint** — authored against a specific class; the class
  is in the Blueprint's header/metadata.
- **Instance Blueprint** — same.

The shared AI editor infrastructure already tracks "currently-edited
target class" (consumed by Blackboard picker, component picker, etc.).
`IAnimationTkbQueries` reuses that context. No new editor wiring.

## 6. Validation rules for the Blueprint compiler

The Blueprint compiler validates animation references at compile time.
Validation runs after the Blueprint's class context is resolved.

Rules:

**ANIM001** — Montage referenced by `PlayMontageNode` (or
`PlayMontageChainNode` per DD-5) must exist in the entity class's
`Montages` list. Error if not.

**ANIM002** — Stance referenced by `SetStanceNode` must be in
`SupportedStances`. Error if not.

**ANIM003** — `LookAt*` node used on an entity class with no
`AimConfig` declared. Error.

**ANIM004** — `WhenNode(EventFired, AnimNotifyEvent)` with a
`MarkerName` filter that doesn't match any marker in
`GetAvailableMarkers(class)`. Warning (the marker may exist on a
runtime-attached montage from another source, but for the Blueprint's
known class, it's suspicious).

**ANIM005** — `PlayMontageChainNode` chain entries that don't all share
the same slot (per DD-1 §6.3). Error.

**ANIM006** — Stance transition declared in `StanceTransitions` that
references a non-existent transition montage name. Error. (Validation
of the DTO itself at TKB-load time, before any Blueprint compiles
against it.)

**ANIM007** — Notify marker referenced in `MontageDefDto.Notifies` that
doesn't exist in `CharacterAnimationDefDto.NotifyMarkers`. Error.
(DTO-level validation.)

The DTO-level validations (ANIM006, ANIM007) run during TKB load
itself, gated by the source-generator-produced validator hooks if
present.

## 7. Hot reload

`CharacterAnimationDefDto` changes during development — montages added,
renamed, removed; stances added; notify markers added. The engine's
existing hot-reload infrastructure for TKB descriptors emits
`DescriptorChanged` events; `AnimationTkbTranslator` subscribes
(§4.1) and invalidates its per-class cache on relevant changes. DD-4
specifies the *consequences* of that invalidation.

When the TKB hot-reload pipeline detects a changed
`CharacterAnimationDefDto` for a class:

1. **Re-bake the per-class data** in the translator's cache (§4.1) for
   the changed class.
2. **Update existing entities of that class.** The
   `CharacterAnimationDefRuntime` component's handle is updated to
   point at the new baked data; the components themselves don't need
   reconstruction.
3. **Validate currently-active state.** If an entity is currently
   playing a montage that was *removed* by the reload, its slot's
   `ActiveMontage` references an ID that no longer resolves.
   `AnimationStateReporterSystem` detects this on the next tick
   (the backend says nothing is playing, since the underlying asset
   may have been unloaded) and fires `MontageEndedEvent { EndReason =
   Failed }`, sets `Status = Failure`.
4. **Re-validate active Blueprints in the editor.** If a Blueprint
   was open referencing a now-removed montage, the editor's
   ANIM001 validation fires retroactively and surfaces a red
   error pill on the affected node.

If an asset's actual binary content changes (montage re-imported with
different keyframes), that's an asset-system reload concern handled by
the backend's own asset hot-reload — the `MontageAssetId` is unchanged,
the backing animation data is swapped under the runtime. Out of scope
here.

## 8. Worked example — Sniper character class end to end

A complete walk-through to make the schema concrete.

### 8.1 The JSON

```jsonc
// content/characters/sniper.tkb.json (excerpt)
{
  "Anim.CharacterDef": {
    "Slots": [
      { "SlotId": 0,   "Name": "Locomotion", "BoneMask": ["root"],   "Mode": "Override", "Priority": 0 },
      { "SlotId": 100, "Name": "FullBody",   "BoneMask": ["root"],   "Mode": "Override", "Priority": 100 },
      { "SlotId": 200, "Name": "UpperBody",  "BoneMask": ["spine"],  "Mode": "Override", "Priority": 200 },
      { "SlotId": 400, "Name": "AimAdditive","BoneMask": ["spine"],  "Mode": "Additive", "Priority": 400 }
    ],
    "Montages": [
      {
        "Name": "Reload_Rifle",
        "AssetRef": "Animations/Sniper/Reload_Rifle.clip",
        "Slot": 200,
        "DefaultBlendInTime": 0.1,
        "DefaultBlendOutTime": 0.2,
        "DurationSeconds": 3.4,
        "Sections": ["Start", "Insert", "Close"],
        "Notifies": [
          { "MarkerName": "MagOut",  "TimeSeconds": 0.8, "PayloadByte": 0 },
          { "MarkerName": "MagIn",   "TimeSeconds": 2.1, "PayloadByte": 0 }
        ]
      },
      {
        "Name": "Vault_Low",
        "AssetRef": "Animations/Sniper/Vault_Low.clip",
        "Slot": 100,
        "DefaultBlendInTime": 0.1,
        "DefaultBlendOutTime": 0.15,
        "DurationSeconds": 1.2,
        "Sections": ["Approach", "Vault", "Land"],
        "Notifies": [
          { "MarkerName": "Footstep_Left",  "TimeSeconds": 0.9, "PayloadByte": 0 }
        ]
      },
      {
        "Name": "Trans_StandToCrouch",
        "AssetRef": "Animations/Sniper/Trans_StandToCrouch.clip",
        "Slot": 100,
        "DefaultBlendInTime": 0.1,
        "DefaultBlendOutTime": 0.1,
        "DurationSeconds": 0.5,
        "Sections": [],
        "Notifies": [],
        "IsStanceTransition": true
      }
      // ... etc
    ],
    "SupportedStances": [0, 1],   // Standing, Crouched
    "StanceTransitions": [
      { "From": 0, "To": 1, "TransitionMontageName": "Trans_StandToCrouch", "DefaultBlendTime": 0.3 },
      { "From": 1, "To": 0, "TransitionMontageName": "Trans_CrouchToStand", "DefaultBlendTime": 0.3 }
    ],
    "AimConfig": {
      "MaxYawDegrees": 90,
      "MaxPitchDegrees": 70,
      "AimSourceBone": "head"
    },
    "NotifyMarkers": [
      { "Name": "MagOut",         "Hash": 0xA1B2C3D4, "Kind": "Generic"  },
      { "Name": "MagIn",          "Hash": 0xB1C2D3E4, "Kind": "Generic"  },
      { "Name": "Footstep_Left",  "Hash": 0xC1D2E3F4, "Kind": "Footstep" },
      { "Name": "Footstep_Right", "Hash": 0xD1E2F3A4, "Kind": "Footstep" }
    ]
  }
}
```

### 8.2 What the editor shows

When the AI designer opens a Blueprint targeting `Sniper`:

- `PlayMontageNode`'s montage dropdown shows `Reload_Rifle`, `Vault_Low`
  (and any other non-transition montages). `Trans_StandToCrouch` is
  hidden — it's flagged `IsStanceTransition`.
- `SetStanceNode`'s stance dropdown shows `Standing`, `Crouched`. `Prone`
  is greyed out (not in `SupportedStances`).
- A `LookAtPointNode` is allowed (AimConfig present).
- `WhenNode(EventFired, AnimNotifyEvent)` — its MarkerName filter
  dropdown shows `MagOut`, `MagIn`, `Footstep_Left`, `Footstep_Right`.
- `WhenNode(EventFired, FootstepEvent)` — typed event, no marker
  filter needed; just listens for any `FootstepEvent` for Self.

### 8.3 What the translator produces

On ghost promotion of a Sniper entity:

- `AnimationChannel` (default-initialized)
- `LookAtChannel` (default-initialized, present because AimConfig is)
- `StanceIntent { TargetStance = Standing, Version = 0 }`
- `StanceStatus { CurrentStance = Standing, Phase = Completed, AckVersion = 0 }`
- `AnimationMontageQueue` (default — Count = 0)
- `AnimationMontageQueueState { CurrentEntryIndex = 0xFF }`
- `CharacterAnimationDefRuntime` (handle into Sniper-class baked data)
- `AnimationExecutorState` (default)
- `LookAtExecutorState` (default)

The Sniper-class baked data, built once and cached:

```
Montages: {
  hash("Reload_Rifle"): MontageRuntimeInfo { Slot = 200, AssetRef = ..., Duration = 3.4, ... },
  hash("Vault_Low"):    MontageRuntimeInfo { Slot = 100, AssetRef = ..., Duration = 1.2, ... },
  hash("Trans_StandToCrouch"): MontageRuntimeInfo { Slot = 100, ..., IsStanceTransition = true }
}
Stances: { 0, 1 }
Transitions: {
  (0, 1): { TransitionMontageId = hash("Trans_StandToCrouch"), BlendTime = 0.3 },
  (1, 0): { TransitionMontageId = hash("Trans_CrouchToStand"), BlendTime = 0.3 }
}
AimConfig: { MaxYaw = 90, MaxPitch = 70, AimSourceBone = "head" }
SlotTable: [Slot 0, Slot 100, Slot 200, Slot 400]   sorted by priority
```

### 8.4 What happens at runtime

Brain says "play Reload_Rifle":

1. `PlayMontageNode` (DD-5) writes `ActionIdPlayMontage` into
   `AnimationChannel.ActiveAction`, fills `ActionParams` with
   `PlayMontageParams { MontageId = hash("Reload_Rifle"), BlendInTime = 0.1, ... }`.
2. Replication carries to Muscle.
3. `AnimationDispatcherSystem` (DD-1 §6) looks up the montage in
   the baked `Montages` dict, finds `Slot = 200`, capability-checks,
   writes executor state.
4. `AnimationRuntimeBridgeSystem` calls
   `backend.PlayMontageOnSlot(handle, slot=200, montage=hash, blendIn=0.1, ...)`.
5. Stride's per-entity blend-tree builder picks up the new slot
   state on next backend tick.
6. At 0.8s, the `MagOut` notify fires on the backend; drained to
   `RawNotifyEvent { Kind = Generic, MarkerHash = 0xA1B2C3D4, ... }`.
7. `NotifyEventEmitterSystem` (DD-1 §11) publishes
   `AnimNotifyEvent { Target, MontageId = hash("Reload_Rifle"),
                        MarkerHash = 0xA1B2C3D4, PayloadFloat = 0 }`.
8. A `WhenNode(EventFired, AnimNotifyEvent, MarkerName="MagOut")` in
   the Blueprint sees its filter match (compiled to compare
   `event.MarkerHash == 0xA1B2C3D4`) and fires its downstream exec.

End-to-end the pipeline is concrete; nothing is hand-waved.

## 9. Resolutions summary (from v1.0 review)

All six open questions from DD-4 v1.0 received architect rulings;
recorded here for traceability. v1.1 incorporates each resolution into
the body sections referenced.

### 9.1 ✅ Per-class baked data caching mechanism

**Resolved:** The engine has no unified per-class baked-data cache
pattern. `AnimationTkbTranslator` owns its own thread-safe
`ConcurrentDictionary<long, CharacterAnimationBakedData>` cache and
subscribes to the engine's TKB hot-reload events
(`ITkbHotReloadEvents.DescriptorChanged`) to invalidate entries when
class definitions change. Reflected in §4.1.

### 9.2 ✅ GUID-stable montage IDs

**Resolved:** Defer. The 31-bit FNV-1a hash of the montage name is
sufficient for v1. Revisit only if content authors report severe
rename-induced breakage. Reflected (already) in §3.1.

### 9.3 ✅ Notify marker hash collisions across character classes

**Resolved:** No cross-class fallback needed. The AI editor
infrastructure always has correct entity-class context for the
currently-edited Blueprint, so the per-class `GetMarkerName(hash)`
query always resolves correctly within scope. Cross-class hash
collisions are runtime-irrelevant (events carry `Target` entity) and
editor-irrelevant (always per-class scoped). Reflected (already) in
§3.4.

### 9.4 ✅ Asset import → TKB JSON boundary contract

**Resolved:** Delegation confirmed. The DD-4-side schema (§2) is the
contract; the asset import team owns producing it. File a formal
ticket for that team referencing this section. Action item for DD-4
implementer, not a design change.

### 9.5 ✅ Backend-specific `AssetRef` shape

**Resolved:** Option 1 (opaque string) approved. Don't over-engineer
a tagged union for a backend that doesn't exist yet. When the
proprietary backend arrives, manage transition with a one-time JSON
migration script if both backends need to coexist. Reflected
(already) in §2; §9.5 of v1.0 recommended deferring and the architect
confirmed.

### 9.6 ✅ Editor query API location

**Resolved:** Place `IAnimationTkbQueries` in
`Hrot.Editor.AiShared.Catalog`, alongside other catalog tools in the
shared AI editor infrastructure. Not in
`Hrot.MuscleCharacter.Animation.Editor`. Reflected in §5.

---

**No residual open questions remain.** DD-4 is fully resolved and
approved for implementation.

---

## Summary

DD-4 specifies the data pipeline from design-time JSON to runtime
ECS components for character animation. `CharacterAnimationDefDto`
holds the per-character-class schema covering slots, montages, stances,
stance transitions, aim configuration, and notify markers.
`AnimationTkbTranslator` injects the components defined in DD-1 §5 at
ghost promotion, maintains its own thread-safe `ConcurrentDictionary`
cache of per-class baked data, and subscribes to TKB hot-reload events
for cache invalidation. `IAnimationTkbQueries` (in
`Hrot.Editor.AiShared.Catalog`) exposes the same data to the editor
for picker filtering, validation, and `WhenNode` marker dropdowns.
Seven validation rules (ANIM001–ANIM007) catch animation reference
errors at Blueprint compile time. Hot reload is handled via the
engine's existing TKB hot-reload events with documented consequences
for in-flight animations on outdated entities.

All six open questions from v1.0 resolved per architect review (see §9
Resolutions Summary).

Next: DD-2 (Replication) and DD-5 (Blueprint primitives) can proceed
with DD-1 and DD-4 contracts both locked. DD-3 (Event Catalog) consumes
DD-4's marker-name/hash convention (now defined).

---

*End of DD-4 v1.1. Architect-approved for implementation.*
