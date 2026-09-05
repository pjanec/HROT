# Squad Coordination — Design v1.1

> **Changelog v1.0 → v1.1** (open questions resolved by architect review):
> - **S-1 → single `SquadCognitiveState` projection** with sub-regions (one offset claim); the 1024 B
>   block accommodates maneuver state + contact pool at the 16-member cap (§3.1).
> - **S-2 → `SquadPerceptionMergeSystem` decimated to ~10 Hz** + event-driven on significant contact
>   change (§4).
> - **S-3 → element-partition hysteresis** — a member holds its element unless the score gap is
>   decisive (§4.1, new).
> - **S-4 → maneuver selection is a commander-tier `[UtilityDecision]` (`ManeuverSelect`)** — the same
>   Utility core, one tier up, with mission orders able to force a maneuver (§8.0, new). Utility AI
>   recurses: agents score postures, the commander scores maneuvers, same machine.
> - **S-5 → hand-authored `FakeDangerAreaProvider` descriptors** per fixture; no navmesh simulation
>   (§11).

> **Status:** Detailed design. The destination of the squad-tactics thread.
> **Audience:** Implementation lead and reviewer.
> **Drives:** A Brain-resident squad coordination layer — role assignment, shared situational
> awareness, and coordinated maneuvers (urban + open-field + tank-platoon) — built on the commander
> entity's existing hierarchy/blackboard infrastructure and the tactical-intent pipeline, consuming
> the Utility AI allocation machinery and a new danger-area EQS sensor.
> **Depends on:** the **3D Cognitive Spatial Awareness Promotion** (committed pre-step — `TargetMemory`
> and EQS cover queries must be 3D for multi-level correctness); Utility AI (Architecture v1.1 — the
> allocation matrix, the leader/blackboard pattern, the two-level-authority veto model).
> **Out of scope:** Formation movement and cover-aware path *shaping* (hug-the-building, reshape in
> narrow passages) — that is **Muscle's** job; the squad layer only sets a movement-mode intent.
> Additional sensor types beyond danger-area (later, via the EQS precedent).

---

## 1. The shape of the problem

When agents stop acting as individuals and act as a unit, four capabilities appear. One we already
built; one belongs to Muscle; two are this design:

| Capability | Where it lives |
|---|---|
| **Fire allocation** (who shoots whom) | **Done** — Utility AI §10 (leader greedy assignment, member veto) |
| **Formation movement** (keep shape, reshape for terrain, hug cover) | **Muscle** — geometry; squad sets only a movement-mode intent |
| **Role assignment** (pointman / suppressor / flanker / sector) | **This design** |
| **Shared situational awareness** (pool what members perceive) | **This design** |
| **Coordinated maneuvers** (sequenced, phased group plays) | **This design** |

The unifying insight that keeps this from being a pile of scripted set-pieces: every maneuver — urban
danger-area crossing, open-field bounding overwatch, suppress-and-maneuver, the tank hill-crest
rotation — decomposes into the **same small set of primitives**. They differ only in configuration.

---

## 2. The five primitives

All Brain-resident, all on the commander entity, all unit-type-agnostic.

1. **Element partition** — split `UnitRoster` into N elements (firing pair vs. rest; moving vs.
   covering; crossing man vs. security). A scored partition reusing the Utility allocation matrix.
2. **Tactical-feature reference** — handles on danger areas / features from the danger-area sensor
   (§5): a street, a crest line, a choke. Geometry stays in Muscle; the squad holds handles + ratings.
3. **Role / slot assignment** — assign elements to slots (exposed-firing, covering, crossing,
   suppressing, assaulting, sector-of-fire) via the allocation matrix, **re-run on phase changes**.
   Same machine as fire assignment, different payload.
4. **Phase sequencer with turn-taking** — the squad HSM: which element holds which slot now,
   minimal-exposure dwell, rotate on completion, detect a broken rotation (a veto) and recover.
5. **Exposed-slot rotation with burn/reuse** — track which exposure positions are used/burned so the
   unit never re-exposes in the same spot (generalized from hill-attack's `BurnedSlotsMask` /
   `WaveUsedSlotsMask` bitmasks).

The maneuver catalog (§8) is nothing but configurations of these five. **The tank hill-crest doctrine
is the proof**: it is already all five (wave partition, authored firing-line slots, creep-to-LOS slot
task, round-robin fire allocation, burned-slot rotation) — so an infantry danger-area cross and a tank
hull-down rotation are the *same engine* with different parameters.

---

## 3. Substrate — built on what exists

No new hierarchy or blackboard component. The squad layer reuses, unchanged:

- **Hierarchy:** `UnitRoster` (commander, capacity 16) + `UnitSubordinate.Commander` (back-pointer),
  maintained by `UnitHierarchySystem`.
- **Shared memory:** the commander's `Blackboard1024`, projected via `Unsafe.As` into squad state
  structs (the established `HillAttackMutableState` pattern; `[SharedAiHeavyAction]` auto-emits the
  projection). `[DataPolicy.NoSave]` — transient cognitive state, stripped from scenario JSON.
- **Authority rail:** `AssignTacticalIntentEvent` → `TacticalIntentResolutionSystem` →
  `ITacticalOrderMapper` → `BehaviorIngressSystem`. The commander publishes intents; subordinates
  resolve them into their own behaviors. This is exactly how `PlatoonHillAttack` commands its tanks;
  the squad layer adds new intent types and mappers, not a new pipeline.

### 3.1 Squad state on the blackboard

All squad working state projects onto the commander's `Blackboard1024` as a **single
`SquadCognitiveState` struct with sub-regions** (S-1) — maneuver state and the contact pool share one
projection, so there is exactly one offset claim and one collision-check rather than two competing
ones:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SquadCognitiveState   // the single projection onto Blackboard1024 (≤1024 B)
{
    // --- maneuver sub-region ---
    public ushort ManeuverKind;        // which catalog entry (§8)
    public ushort PhaseId;             // squad-HSM phase
    public uint   ActiveFeatureId;     // the danger area / feature being worked (§5)
    public ElementPartition Elements;  // member→element index (SoA, ≤16)
    public SlotAssignmentArray Slots;  // element→slot + per-slot state (rotation, burn)
    public RoleAssignmentArray Roles;  // member→role + assignment score
    public uint   PhaseEnteredTick;    // for dwell/timeout

    // --- shared-awareness sub-region (§4) ---
    public SquadContactPool Contacts;  // merged contacts, SoA, capacity-bounded, threat-sorted

    // … all SoA / fixed-capacity, [InlineArray] with span-cast access (the defensive-copy trap)
}
```

Sizing follows the hill-attack precedent (`HillAttackMutableState` is 120 B); the contact pool is the
variable cost, but at the hard 16-member `UnitRoster` cap the whole `SquadCognitiveState` fits the
1024 B block comfortably. **Offset-collision check** (carried from Utility §10.1): confirm no other
projected state claims the same `Blackboard1024` range on commander entities — now a single contiguous
claim to verify.

---

## 4. Shared situational awareness — the perception merge

This is the one genuinely new *mechanism* (the others reuse the allocation matrix). It is a **merge**,
not an allocation.

Each member perceives into its own (now-3D, post-promotion) `TargetMemory`. A
`SquadPerceptionMergeSystem` (Brain, on the commander) gathers members via `UnitRoster`, merges their
contacts by entity id into the **`SquadContactPool` sub-region of `SquadCognitiveState`** (§3.1),
keeping the freshest/closest/highest-threat sighting per contact. **Cadence (S-2):** the merge is
**decimated to the ~10 Hz cognitive cadence** (matching perception/EQS and the Utility re-score
rhythm), supplemented by event-driven re-merge on a significant contact change (a new contact, a
contact lost). The merged pool becomes:

- what the leader's **fire and role allocation read** (instead of just the commander's own perception);
- readable by **members** — "I know about a threat a squadmate sees but I can't, called out over the
  pool" — surfaced as a Utility consideration input (`SquadKnowsContact`).

Merge rules: dedupe by network-stable entity id; per contact keep max threat score and most-recent
3D position (3D matters — a contact seen on a bridge deck vs. the street below is *not* the same
sighting collapsed by 2D). Capacity bounded (≤ the contact cap); insertion-sorted by threat like
`TargetMemory`, so truncation drops least-threatening, consistent with the Utility cap invariant.

This is the squad analog of "EQS result as an input": members contribute perception; the pool is the
shared product; allocation and member decisions consume it.

### 4.1 Element-partition hysteresis (S-3)

Re-running the element partition (primitive 1) every phase could reshuffle members disruptively — an
agent yo-yoing between the moving and covering elements each bound would destroy maneuver cohesion. So
the partition carries **hysteresis**, exactly like the posture selector's anti-flip-flop bonus
(Utility §4.5): a member stays in its current element unless the score gap in favor of moving it is
**decisive**, not merely positive. Cohesion is preserved; re-partitioning happens only when the
tactical situation genuinely warrants it (a member lost, an element too depleted to hold its slot).

---

## 5. The danger-area sensor

A **new EQS query kind** — same sensor lifecycle, bespoke result schema (the 24-byte `EqsResult`
can't carry multi-handle features; confirmed with the architect).

### 5.1 Lifecycle (reuses EQS sensor infrastructure)

The commander owns the sensor as a **child entity** (`PartMetadata`: parent ref + `InstanceId`),
carrying the query config and its own result buffer; results route back over the compound
`ParentNetworkId` + `LocalChildIndex` key (the multi-sensor mechanism the architect described). The
commander can own several sensors (danger-area + others) as distinct children. Muscle does all the
geometry — identifying features along the squad's planned route/footprint — and the navmesh extension
that produces tactical features is the in-plan work this depends on.

### 5.2 Result schema (3D-native, lean)

Built on the architect's `DangerAreaDescriptor`, with the corrections we settled: **3D handles**,
**2.5D extent (OBB + Z band)**, and **no baked flanking-cover array** — cover comes from a separate
(now-3D) EQS cover query parameterized at maneuver time, not frozen at detection (§6).

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct DangerAreaDescriptor
{
    public uint    FeatureId;        // FNV-1a of core navmesh polygon id — stable across solver passes
    public float   ThreatRating;     // 0..1 (sets caution + role-bias weight)
    public ushort  Kind;             // OpenGround | StreetCrossing | Intersection | ChokePoint | CrestLine | … (extensible)
    public ushort  _pad;

    // 2.5D extent — OBB footprint + height band (multi-level: street vs. deck = two areas, same X/Y, disjoint Z)
    public Vector3 Center;
    public Vector2 ExtentsXY;        // half-width / half-length on the footprint
    public float   AngleRad;         // OBB orientation
    public float   ZFloor, ZCeiling; // the height band

    // Maneuver handles — 3D (height is tactically decisive: under-bridge vs. deck)
    public Vector3 NearSideHandle;   // where the crossing element forms up
    public Vector3 FarSideHandle;    // destination / where first-across sets up to cover
}
```

Tracking: the squad HSM keeps a `FeatureId → assignment` map on the blackboard; when the
`DangerAreaCognitiveBuffer` refreshes, an O(N) scan re-matches by `FeatureId` (the navmesh-derived id
is stable unless the navmesh is rebuilt), exactly as `TargetMemory` tracks contacts. Flanking cover is
**not** here — see §6.

### 5.3 Why cover is a separate query, not in the descriptor

Overwatch/flanking positions come from a standard (now-3D) **EQS cover query** fired by the squad HSM
when a maneuver needs them, parameterized by the danger area's extent and the *current* threat
direction. Three reasons: cover scoring is already EQS's job (concealment, LOS-to-area, reachability)
and shouldn't be reimplemented in the navmesh feature detector; the *right* cover depends on where the
threat actually is and which way the squad is crossing — known at maneuver time, not detection time;
and it keeps the architecture's division intact (sensor finds *features*, EQS scores *positions for a
purpose*), the same way Utility consumes EQS scores as inputs. This is the squad layer's one
dependency on the 3D EQS promotion.

---

## 6. Authority — two-level-by-weight (carried from Utility)

The maneuver layer keeps the Utility authority model and extends it by *weight*, not by adding a
command channel:

- The squad assigns a member's role/slot by writing it to the blackboard; the member reads it (via
  `UnitSubordinate.Commander`) as a **high-weight consideration** in its own Utility decision.
- **Maneuver discipline = a much higher bias weight** than ordinary fire assignment, so members
  normally hold formation/role.
- But **self-preservation considerations can still zero the option** — a member about to die breaks
  off (the veto). Discipline is "a very strong consideration," never an unvetoable order.
- The squad HSM **detects the broken rotation** (a member vetoed / didn't reach its slot) and
  transitions to a recovery phase rather than assuming compliance.

This keeps the whole system in one consistent scoring paradigm and means the veto falls out of the
math, not a separate override protocol. (The tank doctrine's existing imperative intents are a special
case where discipline weight is effectively maximal; the general model subsumes it.)

### 6.1 Movement mode — the Muscle intent

"Hug the building / move covered vs. move fast" is **not** a maneuver; it's a squad **posture bit**
that biases a per-member `MovementMode` intent which Muscle reads and turns into cover-aware path
shaping. The squad decides *when* to be in covered movement (e.g. inside or approaching a danger
area); Muscle does the geometry. One enum on the intent; no squad-side pathing.

---

## 7. Three-way authoring

The five primitives are exposed as a **library** (Brain API), not buried in an HSM-only framework, so
all three authoring forms call into them equally — the same way the Utility scorer is a library
BTree/HSM/Blueprint all consume.

1. **Squad HSM on the commander — the preferred default.** States = maneuver phases; transitions on
   completion-events / vetoes / timeouts. This is where the catalog (§8) is authored and where the
   worked examples / integration tests live. FastHSM is the existing tool for exactly this
   phased-stateful shape.
2. **Blueprint.** For special cases wanting the visual/composite surface; the `SquadState` Blueprint
   pattern (shared cross-peer variables, `callablePeers`) already exists and is the natural host for
   Blueprint-authored squad logic.
3. **Dedicated script.** The imperative form — *how the tank hill-attack is implemented today*
   (`PlatoonHillAttack` / `HullDownAttackRun` commander+subordinate BTrees). Must stay supported, both
   for parity and because some bespoke doctrine is cleaner as direct code.

The phase sequencer, element partition, role/slot assignment, and rotation primitives are the API all
three call; the squad HSM is the recommended orchestration shell, not the only one.

---

## 8. The maneuver catalog (infantry-weighted; integration tests)

Each is a configuration of the five primitives. The worked examples double as integration tests
(fabricated-world fixtures, the Utility starter-pack discipline). **Infantry-weighted per the project
priority; hill-crest is the cross-unit-type proof, not the centerpiece.**

### 8.0 Maneuver selection — Utility AI, one tier up (S-4)

*Which* maneuver the squad runs is itself a **commander-tier `[UtilityDecision]` of a new kind,
`ManeuverSelect`** — the exact same Utility scoring core the agents use for posture, recursed one
level. The commander scores the candidate maneuvers (danger-area cross vs. bound vs.
suppress-and-maneuver vs. hold) against squad-level considerations — squad strength ratio, the active
danger area's threat rating and kind, member-state aggregates from the contact pool (§4), ammo/health
rollups — and selects the highest. The result sets `SquadCognitiveState.ManeuverKind`, which the squad
HSM (§7) then sequences.

This is a clean recursion and it means **there is no separate maneuver-selection mechanism to build**:
agents score *postures* (cover/flee/advance), the commander scores *maneuvers* (cross/bound/suppress),
both through the identical Utility core, both feeding the same blackboard + two-level-authority veto.
A `ManeuverSelect` decision is authored exactly like any other (`[UtilityDecision]`, the catalog, the
curve editor, the debug overlay) — the squad layer inherits the whole Utility toolchain for free.

**Mission override:** an explicit mission order bypasses the scorer and forces a specific
`ManeuverKind` (the same way the tank doctrine is dispatched imperatively today). The scorer is the
autonomous default; orders win when present. This mirrors the agent-tier model where an assigned target
strongly biases but mission can still command.

> Possible short follow-on: a `ManeuverSelect` starter-pack decision (scored cross vs. bound vs.
> suppress) as a worked example, paralleling the agent posture decision — useful but not required to
> build the engine, since `ManeuverSelect` is just another Utility decision kind.

### 8.1 Danger-area crossing (the canonical infantry case)

Squad HSM phases: **Set Security → Cross Element → Far-Side Cover Established → Collapse Security →
Reform.** Element partition splits a crossing element from a security/overwatch element; the
danger-area sensor supplies near/far handles; an EQS cover query supplies overwatch positions; the
sequencer sends elements across one/a-pair at a time (exposed-slot rotation across crossing lanes so
not everyone uses the same line); **first-across is reassigned to the covering role** (role
re-assignment on phase transition — the same matrix re-run). Last element crosses last.

### 8.2 Bounding overwatch (open-field + urban)

Two elements; one `Moving` while the other `Covering` from a position with eyes on the danger; they
leapfrog. Squad HSM alternates which element holds which slot each bound; role bias flips on each
transition. Urban variant: bounds are building-to-building/corner-to-corner; open-field: rushes
between cover. Completion-event driven (bound complete → swap).

### 8.3 Suppress-and-maneuver (base of fire + assault)

Element partition into a **base-of-fire** element (high suppress-role bias, hold position, fire on the
known threat) and an **assault** element (advance bias along a Muscle-pathed flank). The base
suppresses while the assault moves; the danger-area/threat reference anchors both. Generalizes the
two-element pattern with specialized slots.

### 8.4 Hill-crest hull-down rotation (the cross-unit proof)

The existing tank doctrine, expressed as the engine's configuration: **wave element partition** (2 at
a time if platoon > 3), **authored firing-line + defilade-baseline segments** (iteration-1 authored
lines; a future hull-down-finding sensor can supply the same handles later — the seam is the same
"handles in, source swappable" pattern), **creep-to-LOS as the event-terminated exposed-slot task**
(creep until target registers in `TargetMemory` with threat > 0 → halt → fire; abort on overshoot),
**round-robin or matrix fire allocation**, **burned/used-slot rotation** so tanks never re-expose in
the same spot. Parity target: the general engine configured this way reproduces today's hill-attack
behavior — including the "resume-trap" avoidance (actively scan during creep, don't rely on a cached
perception result).

### 8.5 Covered-movement posture (Muscle intent, not a maneuver)

Not a sequenced maneuver — a squad posture that sets the `MovementMode` intent to "covered" so Muscle
hugs cover (§6.1). Listed for completeness; it's a one-bit decision, not an HSM.

### 8.6 Briefer catalog entries (cover the remaining primitives)

- **Stack-and-room-entry** (urban) — sector-assignment-heavy: stack on a door, enter in sequence with
  assigned sectors of fire (first-man-left, second-right). Exercises role/slot assignment where slots
  are *sectors of fire*. Lighter detail in v1.
- **Travelling overwatch** (tank/open-field) — lead element moves, trail element overwatches at
  distance; bounding's looser cousin. Exercises **element-split without rotation**. Lighter detail.

These two are included so the catalog exercises every primitive: rotation (8.1/8.2/8.4),
role/sector assignment (8.1/8.3/8.6a), element split (8.3/8.6b), turn-taking sequencing (8.1/8.2/8.4),
burn/reuse (8.1/8.4).

---

## 9. The event-driven rotation engine

The sequencer (primitive 4) is driven by **completion events where Muscle/weapon can signal, timers
where they can't** (the hybrid you specified):

- "shot fired" — Brain ordered it and hears the fire notification.
- "defilade/far-side reached" — Muscle locomotion-channel intent-success.
- "bound complete" — same.
- timer fallback — only for phases with no available completion event.

Hill-attack proves all the needed events exist. The engine prefers the event; the timer is the
fallback so exposure timing is responsive to reality, not a guess. The squad HSM transition guards are
these events (and the veto/abort signals), so the maneuver advances when the world says the slot task
is done — minimal-exposure dwell falls out naturally.

---

## 10. Debug & overlays

Reuses the AI overlay substrate (Runtime Tuning & Overlays DD) — the squad coordination overlay is a
`SquadAssignmentOverlaySource` already sketched there, extended for maneuvers:

- Element membership (color per element), role/slot per member, the active danger area (its OBB + Z
  band as an extruded box — the 3D extent the promotion enables renders better than a flat box).
- Assignment lines: leader→member→assigned-slot (solid) vs. what the member is actually doing
  (dashed) — divergence shows a veto, with the dominant self-preservation consideration labeled (the
  same "why did it pick this" trace from Utility §9).
- Squad-HSM phase + dwell timer; the contact pool (§4) as merged markers distinct from per-member
  perception.

All gated by `DebugState.Flags`, budget-honored, layer-masked, Map2D-defaulted for the text-heavy
parts — consistent with the overlay DD.

---

## 11. Dependencies & sequencing

- **Hard pre-step: the 3D Cognitive Spatial Awareness Promotion.** `TargetMemory` and the EQS cover
  query must be 3D, or the squad layer is multi-level-blind (§4, §5.3). This is the one dependency that
  gates correctness, accepted deliberately.
- **Utility AI** must exist (the allocation matrix, the two-level-authority veto). Squad role/fire/slot
  assignment *is* the Utility matrix with different payloads.
- **Navmesh tactical-feature extraction** (in plan) produces the danger-area descriptors; the sensor
  is the contract (§5). Until it lands, the sensor can be faked (a `FakeDangerAreaProvider`, mirroring
  `FakeNavmeshProvider`) so the squad layer and its integration tests proceed against fabricated
  features.
- **Within the squad work:** primitives library → perception merge → danger-area sensor (+ fake) →
  squad HSM shell → catalog (infantry first: 8.1, 8.2, 8.3; then 8.4 hill-crest parity; then 8.6
  briefer). Authoring-form support (HSM/Blueprint/script) follows from the library being a clean API.

---

## 12. Resolved questions (architect review)

- **S-1. Blackboard layout — RESOLVED: single `SquadCognitiveState` projection with sub-regions**
  (§3.1). One contiguous offset claim, one collision-check; the 1024 B block fits maneuver state +
  contact pool at the 16-member cap, the pool being the variable cost.
- **S-2. Merge cadence — RESOLVED: ~10 Hz decimated** + event-driven on significant contact change
  (§4), matching perception/EQS/Utility cadence.
- **S-3. Partition stability — RESOLVED: hysteresis** (§4.1) — a member holds its element unless the
  score gap is decisive, like the posture selector's anti-flip-flop bonus.
- **S-4. Maneuver selection — RESOLVED: commander-tier `ManeuverSelect` Utility decision** (§8.0) —
  the same Utility core one tier up, mission orders able to force a maneuver. No new selection
  mechanism; the squad inherits the whole Utility toolchain. (Optional short follow-on: a
  `ManeuverSelect` starter-pack decision.)
- **S-5. Fake provider — RESOLVED: hand-authored descriptors** per fixture (§11), no navmesh
  simulation; sufficient for deterministic maneuver integration tests until the real extraction lands.

---

*End of Squad Coordination design v1.1. Depends on the 3D Cognitive Spatial Awareness Promotion (hard
pre-step) and Utility AI (the allocation matrix + two-level-authority veto). Five primitives, the
danger-area EQS sensor (3D-native, cover via separate EQS query), maneuver selection as a
commander-tier Utility decision, three-way authoring with the squad HSM as preferred shell,
infantry-weighted catalog as integration tests, hill-crest as the cross-unit proof. Formation movement
and cover-aware path shaping remain Muscle's.*
