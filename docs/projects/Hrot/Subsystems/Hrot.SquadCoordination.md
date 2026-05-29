# Hrot.SquadCoordination

**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md`
**Date:** 2026-05-30
**Implementation status:** Design phase — not yet implemented. All phases (P0–P6)
are pending. The pre-step gates (Utility AI P0–P6 and the 3D Cognitive Spatial
Awareness Promotion) must be green before implementation begins.

---

## Executive Overview

Squad Coordination is the Brain-resident layer that lifts individual AI agents
into a coordinated unit. It gives a commander entity three capabilities that do
not exist at the individual-agent tier:

| Capability | Where it lives |
|---|---|
| **Role assignment** (pointman / suppressor / flanker / sector) | This layer — primitives library |
| **Shared situational awareness** (pool what members perceive) | This layer — `SquadPerceptionMergeSystem` |
| **Coordinated maneuvers** (sequenced, phased group plays) | This layer — maneuver catalog |
| Fire allocation (who shoots whom) | Already built — Utility AI §10 |
| Formation movement (geometry, cover-hugging) | Muscle — not this layer |

The design insight that keeps this from being a pile of scripted set-pieces: every
maneuver — danger-area crossing, bounding overwatch, suppress-and-maneuver, hill-crest
hull-down rotation — decomposes into the **same five primitives** differing only in
configuration. The tank hill-crest doctrine (already built as `PlatoonHillAttack` /
`HullDownAttackRun`) is the proof: it is already all five, so the general engine
configured for infantry and configured for tanks is the same code.

Maneuver selection itself is a commander-tier `[UtilityDecision]` of a new kind,
`ManeuverSelect` — the identical Utility scoring core recursed one level up, so
the squad layer inherits the full Utility toolchain (editor, overlays, tuning
console, debug trace) for free.

---

## Five Primitives

All Brain-resident, all on the commander entity, all unit-type-agnostic.

| # | Primitive | Key type | Purpose |
|---|-----------|----------|---------|
| 1 | **Element partition** | `ElementPartitionPrimitive` | Split `UnitRoster` into N elements (moving vs. covering, firing pair vs. rest). Hysteresis (S-3): member holds its element unless the score gap is decisive. |
| 2 | **Tactical-feature reference** | `TacticalFeatureRef` | Handles on danger areas / features from the sensor (a street, a crest, a choke). Geometry stays in Muscle; the squad holds handles and ratings. |
| 3 | **Role / slot assignment** | `RoleSlotAssignmentPrimitive` | Assign elements to slots (exposed-firing, covering, crossing, suppressing, sector-of-fire) via the allocation matrix — the same machine as fire assignment, different payload. Re-run on phase changes. |
| 4 | **Phase sequencer with turn-taking** | `PhaseSequencer` | The squad-HSM substrate: which element holds which slot now, minimal-exposure dwell, rotate on completion, detect a broken rotation (veto) and recover. |
| 5 | **Exposed-slot rotation with burn/reuse** | `SlotRotation` | Track which exposure positions are used/burned so the unit never re-exposes in the same spot. Generalized from `HillAttackMutableState.BurnedSlotsMask` / `WaveUsedSlotsMask`. |

---

## Architecture

### 3.1 State on the Blackboard — `SquadCognitiveState`

All squad working state projects as a **single** `SquadCognitiveState` struct
onto the commander's `Blackboard1024`. A single contiguous offset claim; one
collision check rather than competing projections.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SquadCognitiveState   // single projection onto Blackboard1024 (= 1024 B)
{
    // --- maneuver scalars (16 B) ---
    public ushort ManeuverKind;        // catalog entry; 0 = none
    public ushort PhaseId;             // squad-HSM phase
    public uint   ActiveFeatureId;     // FNV-1a of navmesh polygon id (§5.2)
    public uint   PhaseEnteredTick;
    public uint   Flags;               // bit 0: missionOverride; bits 1..15 reserved

    // --- element partition (32 B) ---
    public ElementPartition Elements;  // [InlineArray(16)] byte + LastRepartitionTick + pad

    // --- slot assignment (96 B) ---
    public SlotAssignmentArray Slots;  // [InlineArray(12)] SlotState (8 B each)

    // --- role assignment (32 B) ---
    public RoleAssignmentArray Roles;  // [InlineArray(16)] RoleSlot (2 B each)

    // --- fire/threat assignment (256 B, migrated from ThreatMatrixAssignmentSystem) ---
    public AssignmentSlotArray Assignment;  // [InlineArray(16)] AssignmentSlot (16 B each)

    // --- shared-awareness sub-region (592 B) ---
    public SquadContactPool Contacts;       // capacity 16 contacts at 32 B each + 80 B headroom
}
```

**Sub-region sizes** (locked in TASK-SQD-P0-02):

| Sub-region | Size |
|---|---|
| Maneuver scalars | 16 B |
| `ElementPartition` | 32 B |
| `SlotAssignmentArray` (12 slots x 8 B) | 96 B |
| `RoleAssignmentArray` (16 members x 2 B) | 32 B |
| `AssignmentSlotArray` (16 x 16 B) | 256 B |
| `SquadContactPool` | 592 B (16 contacts x 32 B + 80 B headroom) |
| **Total** | **1024 B** |

`AssignmentSlot` was 64 B (Utility AI P1-07). TASK-SQD-P0-01 shrinks it to 16 B
(`long AssignedTargetHandle` + `float AssignmentScore` + `byte FocusFireCount` +
`byte Flags` + `ushort _pad`). The standalone `ThreatMatrixAssignmentState.Project()`
projection is removed; call sites read `SquadCognitiveState.Project(ref bb).Assignment`.

### Substrate Reuse

The squad layer adds **no new hierarchy or blackboard component**. It reuses:

```
UnitRoster (capacity 16) + UnitSubordinate.Commander
    maintained by UnitHierarchySystem

Commander Blackboard1024
    projected via Unsafe.As into SquadCognitiveState
    [DataPolicy.NoSave] -- transient cognitive state

AssignTacticalIntentEvent rail
    --> TacticalIntentResolutionSystem
    --> ITacticalOrderMapper  (new mappers: ForceManeuverMapper)
    --> BehaviorIngressSystem
```

This is exactly how `PlatoonHillAttack` commands its tanks today. The squad layer
adds new intent types and mappers — not a new pipeline.

### 4. Shared Situational Awareness — `SquadPerceptionMergeSystem`

Each member perceives into its own (3D) `TargetMemory`. The
`SquadPerceptionMergeSystem` gathers members via `UnitRoster`, deduplicates
contacts by network-stable entity ID, and writes the merged result into
`SquadCognitiveState.Contacts`.

Merge rules:
- Deduplicate by network-stable entity ID.
- Per contact: keep max threat score and most-recent 3D position (3D matters — a
  contact on a bridge deck vs. the street below is not the same sighting collapsed
  to 2D).
- Capacity-bounded (16 contacts); insertion-sorted by threat so truncation drops
  the least threatening (consistent with the Utility cap invariant).

**Cadence (S-2):** ~10 Hz (matching perception/EQS/Utility cadence) plus
event-driven re-merge on a significant contact change (new contact or contact lost).

The merged pool is readable by members as the `SquadKnowsContact` Utility input
reader — "I know about a threat a squadmate sees but I cannot."

**Element-partition hysteresis (S-3):** Re-partitioning every phase would reshuffle
members disruptively. A member holds its element unless the score gap favoring a
move is decisive — the same anti-flip-flop bonus as the Utility posture selector.

### 5. Danger-Area Sensor

A new EQS-shaped sensor with a bespoke result schema (the 24 B `EqsResult` cannot
carry multi-handle tactical features).

The commander owns the sensor as a **child entity** (`PartMetadata` parent ref +
`InstanceId`). Muscle does all the geometry; the sensor reports the features Muscle
extracts.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct DangerAreaDescriptor
{
    public uint    FeatureId;        // FNV-1a of core navmesh polygon id
    public float   ThreatRating;     // 0..1
    public ushort  Kind;             // OpenGround|StreetCrossing|Intersection|ChokePoint|CrestLine|...
    public ushort  _pad;

    // 2.5D extent (OBB footprint + Z band)
    public Vector3 Center;
    public Vector2 ExtentsXY;        // half-width / half-length
    public float   AngleRad;         // OBB orientation
    public float   ZFloor, ZCeiling; // height band

    // 3D maneuver handles
    public Vector3 NearSideHandle;   // where the crossing element forms up
    public Vector3 FarSideHandle;    // destination / where first-across sets up to cover
}
```

Flanking/overwatch cover is **not** in the descriptor. It comes from a separate
3D EQS cover query fired by the squad HSM at maneuver time, parameterized by the
danger area's extent and the current threat direction. (Cover scoring is already
EQS's job; the right cover depends on where the threat actually is, known at
maneuver time, not detection time.)

Until the real navmesh tactical-feature extraction lands, a `FakeDangerAreaProvider`
supplies hand-authored descriptors per test fixture — mirroring `FakeNavmeshProvider`.

### 6. Authority — Two-Level-by-Weight

The squad assigns a member's role/slot by writing it to the blackboard; the member
reads it as a **high-weight consideration** in its own Utility decision.

- **Maneuver discipline = a much higher bias weight** than ordinary fire assignment.
  Members normally hold formation/role.
- **Self-preservation considerations can still zero the option** — a member about
  to die breaks off (the veto). Discipline is a very strong consideration, never
  an unvetoable order.
- The squad HSM **detects the broken rotation** and transitions to a recovery phase.

This keeps the whole system in one consistent scoring paradigm. The veto falls
out of the math, not a separate override protocol.

**Movement mode (§6.1):** "Hug the building / covered movement" is a squad
**posture bit** — a `MovementMode` intent which Muscle reads and turns into
cover-aware path shaping. The squad decides *when* to be in covered movement;
Muscle does the geometry. One enum on the intent; no squad-side pathing.

### 7. Three-Way Authoring

The five primitives are exposed as a library (Brain API), not buried in an
HSM-only framework. All three authoring forms call into them equally:

1. **Squad HSM on the commander** (preferred default). States = maneuver phases;
   transitions on completion events / vetoes / timeouts. FastHSM is the existing
   tool for this phased-stateful shape.
2. **Blueprint.** The `SquadState` Blueprint pattern (shared cross-peer variables,
   `callablePeers`) already exists as a recipe in `Hrot.AI.Behaviors`; it is the
   natural host for Blueprint-authored squad logic.
3. **Dedicated script.** The imperative form — how `PlatoonHillAttack` /
   `HullDownAttackRun` work today. Kept for parity and bespoke doctrine.

### 8.0 Maneuver Selection — `ManeuverSelect` (Commander-Tier Utility)

Which maneuver the squad runs is a **commander-tier `[UtilityDecision]` of kind
`ManeuverSelect`** — the exact same Utility scoring core recursed one level.
The commander scores candidates against squad-level considerations (squad strength
ratio, active danger-area threat rating and kind, contact-pool aggregates, ammo/health
rollups) and selects the highest. The result sets `SquadCognitiveState.ManeuverKind`.

Mission override: an explicit mission order bypasses the scorer and forces a specific
`ManeuverKind` (via `ForceManeuverMapper : ITacticalOrderMapper`). The scorer is the
autonomous default; orders win when present.

### 9. Event-Driven Rotation Engine

The phase sequencer (primitive 4) is driven by **completion events where
Muscle/weapon can signal; timers where they cannot** (hybrid):

- "shot fired" — Brain ordered it and hears the fire notification.
- "defilade / far-side reached" — Muscle locomotion-channel intent-success.
- "bound complete" — same.
- Timer fallback — only for phases with no available completion event.

`PlatoonHillAttack` proves all needed events exist. The timer is the fallback,
not the primary; exposure timing is responsive to reality, not a guess.

### 10. Debug Overlays

Extends the AI overlay substrate (`AiOverlayFlags.SquadAssignment`, already defined
in the Utility AI overlay set):

- Element membership (color per element), role/slot per member, active danger area
  (OBB + Z band extruded box — the 3D extent renders better than a flat box).
- Assignment lines: leader -> member -> assigned slot (solid) vs. what the member
  is actually doing (dashed) — divergence shows a veto with the dominant
  self-preservation consideration labeled.
- Squad-HSM phase + dwell timer; contact pool as merged markers distinct from
  per-member perception.

All gated by `DebugState.Flags`, budget-honored, layer-masked.

---

## Maneuver Catalog

Each entry is a configuration of the five primitives. The worked examples double
as integration tests (fabricated-world fixtures, the Utility starter-pack discipline).
Infantry-weighted per project priority; hill-crest is the cross-unit-type proof.

| ID | Maneuver | Unit types | Primitives exercised |
|----|----------|-----------|---------------------|
| 8.1 | **Danger-area crossing** | Infantry | Partition (crossing / security), slot rotation (crossing lanes), role re-assignment (first-across -> covering), phase sequencer |
| 8.2 | **Bounding overwatch** | Infantry (+ urban variant) | Two-element partition, slot flip per bound (Moving <-> Covering), completion-event driven transitions |
| 8.3 | **Suppress-and-maneuver** | Infantry | Partition (base-of-fire / assault), specialized slots (suppress, assault), danger-area anchor |
| 8.4 | **Hill-crest hull-down rotation** | Armor | Wave partition, authored firing-line + defilade-baseline handles, creep-to-LOS event-terminated task, round-robin fire allocation, burned/used-slot rotation — parity proof with `PlatoonHillAttack` |
| 8.5 | Covered-movement posture | All | Not a sequenced maneuver; sets `MovementMode` intent bit so Muscle hugs cover (§6.1) |
| 8.6a | **Stack-and-room-entry** | Infantry (urban) | Sector-assignment-heavy: stack on door, enter in sequence with assigned sectors of fire |
| 8.6b | **Travelling overwatch** | Armor / open-field | Element split without rotation: lead moves, trail overwatches at distance |

### 8.1 Danger-Area Crossing (canonical infantry case)

Squad HSM phases: **Set Security** -> **Cross Element** -> **Far-Side Cover
Established** -> **Collapse Security** -> **Reform**. The element partition splits
a crossing element from a security/overwatch element; the danger-area sensor
supplies near/far handles; an EQS cover query supplies overwatch positions; the
sequencer sends elements across one-or-a-pair at a time (exposed-slot rotation
across crossing lanes so not everyone uses the same line); first-across is
reassigned to the covering role (role re-assignment on phase transition).

### 8.2 Bounding Overwatch (open-field + urban)

Two elements; one `Moving` while the other `Covering` from a position with eyes
on the danger; they leapfrog. Squad HSM alternates which element holds which slot
each bound; role bias flips on each transition. Urban variant: building-to-building
/ corner-to-corner. Completion-event driven (bound complete -> swap).

### 8.3 Suppress-and-Maneuver

Element partition into a **base-of-fire** element (high suppress-role bias, hold
position, fire on the known threat) and an **assault** element (advance bias along
a Muscle-pathed flank). Generalizes the two-element pattern with specialized slots.

### 8.4 Hill-Crest Hull-Down Rotation (cross-unit-type proof)

The existing tank doctrine expressed as the engine's configuration: wave element
partition, authored firing-line + defilade-baseline segments, creep-to-LOS as the
event-terminated exposed-slot task (halt when target registers in `TargetMemory`
with threat > 0), round-robin fire allocation, burned/used-slot rotation. Parity
target: the general engine configured this way reproduces today's hill-attack
behavior — including the "resume-trap" avoidance.

---

## Planned Project Layout

Code for this system does not yet exist. The planned home for each component:

```
FDP/Toolkits/Fdp.Toolkits/Squad/
    State/
        SquadCognitiveState.cs       -- single projection onto Blackboard1024
    Primitives/
        ElementPartitionPrimitive.cs
        TacticalFeatureRef.cs
        RoleSlotAssignmentPrimitive.cs   -- calls ThreatMatrixAssignmentSystem adapter
        PhaseSequencer.cs
        SlotRotation.cs
    DangerArea/
        DangerAreaDescriptor.cs          -- 3D-native, 2.5D extent (OBB + Z band)
        DangerAreaSensor.cs              -- child component
        DangerAreaCognitiveBuffer.cs     -- child component (InlineArray<8>)
        DangerAreaRefreshSystem.cs
        Fake/
            FakeDangerAreaProvider.cs    -- hand-authored descriptors (mirrors FakeNavmeshProvider)
    Inputs/
        SquadInputs.cs                   -- SquadKnowsContact, SquadStrengthRatio,
                                         --   ActiveFeatureKindIs, AssignedRole, ...
    Mappers/
        ForceManeuverMapper.cs           -- ITacticalOrderMapper for mission override (§8.0)
    StarterPack/
        ManeuverSelectStarterDecision.cs

Hrot/Subsystems/Hrot.AI.Brain/Squad/
    SquadPerceptionMergeSystem.cs        -- perception merge (10 Hz + event-driven)
    CommanderUtilityTickSystem.cs        -- commander-tier ManeuverSelect scoring
    SquadVetoDetectionSystem.cs          -- broken-rotation detection
    SquadEventIngressSystem.cs           -- hybrid event/timer rotation engine
    SquadMovementModeBroadcastSystem.cs  -- squad posture bit -> Muscle MovementMode

Hrot/Subsystems/Hrot.AI.Behaviors/Squad/Maneuvers/
    DangerAreaCrossingManeuver.cs        -- §8.1
    BoundingOverwatchManeuver.cs         -- §8.2
    SuppressAndManeuverManeuver.cs       -- §8.3
    HillCrestHullDownManeuver.cs         -- §8.4 (parity with HillAttackCommanderNodes)
    StackAndRoomEntryManeuver.cs         -- §8.6a
    TravellingOverwatchManeuver.cs       -- §8.6b

Hrot/Diagnostics/Hrot.Diagnostics.Overlays/
    SquadCoordinationOverlaySource.cs    -- §10 overlay (extends SquadAssignmentOverlaySource)
```

The `SquadState` / `SquadAwareEngagement` Blueprint recipes already exist in
`Hrot.AI.Behaviors/Blueprints/Recipes/` as the authorable-surface prototype (see
`Hrot.AI.Behaviors` doc, Blueprint Recipes section).

---

## Existing Infrastructure Reused

| Component | Location |
|---|---|
| `UnitRoster` (commander; capacity 16) | `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs` |
| `UnitSubordinate` (back-pointer) | `FDP/Engine/Fdp.Core/CommandHierarchy/UnitSubordinate.cs` |
| `UnitHierarchySystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/UnitHierarchySystem.cs` |
| `Blackboard1024` + `Project<T>()` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` |
| `AssignTacticalIntentEvent` rail | `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs` |
| `TacticalIntentResolutionSystem` | `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs` |
| `ITacticalOrderMapper` | `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs` |
| `TargetMemory` (3D after promotion) | `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` |
| `EqsSensor`, `EqsCognitiveBuffer`, `EqsResult` (32 B, 3D) | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` |
| `PartMetadata` (child-entity routing) | `FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs` |
| `HillAttackMutableState` (template precedent) | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs` |
| `HillAttackCommanderNodes` (parity target for 8.4) | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` |
| `FakeNavmeshProvider` (pattern precedent) | `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs` |
| Utility AI scoring core + readers | `FDP/Toolkits/Fdp.Toolkits/Utility/` |
| `ThreatMatrixAssignmentSystem` (shrunk + migrated in P0-01) | `FDP/Toolkits/Fdp.Toolkits/Utility/Group/` |

---

## Pre-Step: TargetMemory 3D Reconciliation

After the 3D Cognitive Spatial Awareness Promotion merges, Utility AI's
`TargetMemory` readers must be updated before squad work begins (see
`.dev/group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md`).

The 3D promotion widens `TargetMemory` to carry altitude (Z); Utility's readers
were written against the 2D struct. The widening is additive (Z is appended;
X/Y unchanged), so the fix is:

- Replace any `new Vector3(x, 0f, y)` position reconstruction with the contact's
  real Z from `TargetMemory`.
- Replace 2D distance computations in readers with `Vector3.Distance` (3D).
- Scalar readers (threat score, health) need no change.

Gate: flat-terrain Utility regression must be bit-or-tolerance-identical to
pre-step behavior; a multi-level fixture must show that `DistanceToContext` now
distinguishes altitude-separated contacts.

---

## Dependencies

### Hard Prerequisites (must be green before Phase 0)

| Prerequisite | Why needed |
|---|---|
| Utility AI Phases 0–6 | Allocation matrix, two-level-authority veto, `[UtilityDecision]` source gen, `ManeuverSelect` kind extension, Utility input catalog |
| 3D Cognitive Spatial Awareness Promotion | `TargetMemory` and EQS cover query must be 3D-native for multi-level correctness (§4, §5.3) |
| Step 1.5 TargetMemory 3D Reconciliation | Utility readers must consume real Z before squad perception merge is valid |

### Soft Prerequisite

| Prerequisite | Notes |
|---|---|
| Navmesh tactical-feature extraction | Produces `DangerAreaDescriptor` values for the sensor. Until it lands, `FakeDangerAreaProvider` supplies hand-authored descriptors per test fixture — squad phases 0–6 proceed against fabricated features. |

### Project-Level Dependencies (planned)

| Project | Role |
|---|---|
| `Fdp.Toolkits` | `ThreatMatrixAssignmentSystem`, `Blackboard1024`, `TargetMemory`, `EqsComponents`, `UnitRoster`, Utility scoring core |
| `Fdp.Core` | `Entity`, `EntityRepository` |
| `Hrot.Core` | Entity type constants |
| `Hrot.AI.Behaviors` | Maneuver HSM shells, Blueprint recipe prototypes |
| `Hrot.CGF` | `TacticalIntentResolutionSystem`, `BehaviorIngressSystem` |
| `Hrot.Diagnostics.Overlays` | `SquadCoordinationOverlaySource` consumer |

---

## Implementation Phases

| Phase | Goal | Status |
|---|---|---|
| P0 | State layout: shrink `AssignmentSlot`, define `SquadCognitiveState`, `ManeuverSelect` kind, `FakeDangerAreaProvider` scaffolding | Not started |
| P1 | Primitives library: `ElementPartition`, `TacticalFeatureRef`, `RoleSlotAssignment`, `PhaseSequencer`, `SlotRotation` | Not started |
| P2 | Shared awareness: `SquadPerceptionMergeSystem`, `SquadKnowsContact` input reader, `DangerAreaSensor` + `DangerAreaCognitiveBuffer` | Not started |
| P3 | Maneuver selection: commander-tier Utility scorer pipeline, squad-level considerations, mission-override mapper, `ManeuverSelect` starter-pack worked example | Not started |
| P4 | Authority + rotation engine: `AssignedRole`/`AssignedSlot` member considerations, veto detection, hybrid event/timer rotation, `MovementMode` intent | Not started |
| P5 | Maneuver catalog: 8.1 danger-area crossing, 8.2 bounding overwatch, 8.3 suppress-and-maneuver, 8.4 hill-crest parity, 8.6 briefer entries | Not started |
| P6 | Three-way authoring shells: squad HSM, Blueprint host, dedicated-script parity with `PlatoonHillAttack` | Not started |

---

## Design Documents

| Document | Location |
|---|---|
| Architecture (primary) | `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` |
| TargetMemory 3D pre-step | `.dev/group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md` |
| Task tracker | `.dev/group-maneuvers/TASK-TRACKER.md` |
| Task detail (per-task specs, success conditions) | `.dev/group-maneuvers/TASK-DETAIL.md` |
| Onboarding | `.dev/group-maneuvers/ONBOARDING.md` |
| Debt tracker | `.dev/group-maneuvers/DEBT-TRACKER.md` |
| Utility AI architecture (scoring core) | `.dev/utility-ai/Utility_AI_Design_v1_1.md` |
| Utility editor / `ManeuverSelect` authoring | `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` |
| AI overlays (`SquadAssignmentOverlaySource`) | `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` |
