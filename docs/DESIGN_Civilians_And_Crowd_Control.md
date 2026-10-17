<!--STATUS
state: LIVE
updated: 2026-10-17 (behaviors: CV-13 gestures + props, CV-13a hands up holds fire, CV-14 security forces provoke — R-2024; §5b CIV-0 as-built; §5c demos) · 2026-10-16 (behaviors: CV-1 class-table rows, CV-4 dirty-mark, §5a measurements, §5 owners/ids + handoffs; backend: §7 detailed designs B-1…B-5; ui: §8 detailed designs U-1…U-4, CE-1162…CE-1165)
build-state: BUILDING (backend B-3/B-2 first) — the HOW is RULED too: CV-1…CV-12 approved as written 2026-10-16 (R-3013).
  SUPERSEDED: the programme's scope and order are RULED by the user 2026-10-16 (§0, R-4032…R-4036); the HOW is
  leans per decision (§4, CV-1…CV-12), awaiting the user. §5 is the slice plan; its backend and ui halves are handed off
  as FRAMES (§5, the receiving lane designs its details first, builds after the user rules the CV it builds), and the
  behaviors half is recorded as CE-2233…CE-2239. Written by the coordinator lane (cross-lane: most slices are
  behaviors-lane work, CV-6 is engine/backend work); the 2026-10-16 additions are the behaviors lane's.
current-answer: §0 the rulings · §2 the diagrams · §4 the decisions with leans · §5 the slice plan · §5a measure before CIV-2 · §7 backend detailed designs (B-1…B-5) · §8 ui detailed designs (U-1…U-4).
stale-below: nothing.
known-rot: none.
known-conflict: docs/TUTORIAL_Behaviour_Composition.md §2 (line 58) puts "thousands of trivial entities (traffic, crowds)"
  at Tier 0 (a plain C# system); the user's ruling D3 / R-263 makes civilians HSMs. This design follows R-263; the
  tutorial row is not reconciled yet. ⚠ AQ93 §8 P5 named a single "PanickedCivilian" type — SUPERSEDED here by CV-1
  (one civilian family whose panic is a MODE, not a type).
related-designs:
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — OWNS the civilian catalogue (CIV-1…CIV-7, ASY-5, SQD-13) and the rulings
    D3 (civilians = crowd agents + light HSM + crowd-level system), D4 (hidden truth + observable cues), D5 (melee = contact
    damage + knockdown, thrown objects reuse ThrowAt); this design is the BUILD of its P3 + P7 rows.
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — §8: SUMO pedestrians that panic are handed to THIS
    programme's civilians (CGF creates them, P4). That hookup is slice CIV-7 here, after everything else.
  - docs/DESIGN_Building_Interiors.md — owns doors (DoorState, DoorRules, DoorPassageSystem opening doors on its own) and
    building footprints/storeys that CV-7's shelter query and inside test read.
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns EQS templates and §19 terrain sight/cover; CV-7 adds an enterable-
    building generator and an inside test.
  - docs/DESIGN_Injury_States.md — owns ok/wounded/incapacitated/dead derived from Health (rule I1); CV-6 adds a STORED effect
    state for stun/irritant at full health, the case that design names as breaking I1.
  - docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — owns body parts and the civilian poses (HL-9).
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — owns separable weapons (PickUp/PutDown, FireWhileCarried) that CV-4's
    armed hostile civilian uses.
  - docs/DESIGN_Weapon_Layers.md — owns the weapon brain, weapon categories and the grenade throw (W-12e/f) that CV-6's
    non-lethal munitions and CV-5's ThrowAt reuse.
  - docs/DESIGN_Decision_Layer.md — §4.4 owns Roe (R-200); its "later" row (WeaponsTight needs identification) is what CV-8
    builds as an escalation-of-force ladder.
  - docs/DESIGN_Sensors_And_Doctrine.md — §9.4 perception budget (~500 units) that bounds CV-10; §11.2 G11 identification.
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — owns squads (UnitRoster ≤ 16) and SquadMarch formations; CV-5
    keeps crowds OUT of the squad layer (catalogue T3) and CV-8's police line uses its Line formation.
  - docs/DESIGN_Map_3D_Mode.md — §6q owns civilian outfits/children (CE-1099) the new types keep.
  - docs/DESIGN_Orders_Params_Entity_Classes.md — §2 owns EntityClassTable (R-279/R-280), the one class table CV-1's new
    civilian and security-force classes are added to.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — the backend half of §5 (B-1…B-5).
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md — the ui half of §5 (U-1…U-5).
  - docs/UX/UX_Feature_Entity_Symbology.md — OWNS the map symbol's colour (§3.1 the AffiliationColors fix = CE-125, §3.2 the one palette); U-2 builds §3.1.
  - docs/DESIGN_Visual_Effects.md — owns EffectLayer (lifespan visuals, the chaff cloud) that U-4 extends.
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Civilians_Findings.md — ui's four findings on §7 (F-1…F-4).
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians_Gestures.md — B-6, the replicated Gesture component (CV-13).
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians_Gestures.md — U-6, the gesture poses and the placard on a pole (CV-13).
-->

# DESIGN — **civilians and crowd control** *(the CIV programme)*

> **The one rule:** a civilian is ONE entity family whose behaviour is a **mode of one HSM** — routine, alarm, panic,
> shelter, casualty, protest, riot, armed hostility — and whose **side is what others can SEE**: neutral until it commits a
> hostile act. Security forces answer with an **escalation-of-force ladder** that starts below lethal.

## 0. What the user ruled *(2026-10-16)*

🔒 **User, verbatim:**
- *"P1 to P8, your leans accepted."* *(AQ93 §8)*
- *"Our civilian tkb typed are obsolete and old and need to be replaced by a completely new ones supporting the panic mode you just described, And not just that, probably many more behaviors we will need."*
- *"Note that the civilian can also be a threat to the soldiers, especially the hostile civilian, because their mood can change at runtime from neutral to hostile."*
- *"Best if all this civilian story could be solved independently on the sumo and before it, . as a separate programme."*
- *"I would carefully consider if we really need promotion zones. Likely the Sumo implementation already tries not to go over the pedestrians on a Crosswalk."*
- *"Panicking pedestrians should try hiding in a building if the doors can be opened, instead of flying away indefinitely. Inside a building, they can disappear after a certain time."*
- *"Let's please try writing a separate pedestrian design to be implemented in parallel with Sumo integration."*
- *"The civilians need to support rioting and attacking security forces. . Security forces need to support non-lethal weapons."*

| ruling | what |
|---|---|
| **R-4032** | AQ93 §8 P1–P8 accepted (the SUMO pedestrian handover). P1's promotion zones are re-examined in AQ93 §8 (lean: drop them) |
| **R-4033** | civilians are a **separate programme, independent of SUMO and built before its pedestrian hookup**; the old civilian TKB types are **replaced** by a new family |
| **R-4034** | a civilian's **attitude changes at runtime** (neutral → hostile); a hostile civilian is a threat soldiers may answer |
| **R-4035** | a panicking civilian **hides in a building whose door can be opened**, and **disappears** after a while inside — never flees indefinitely |
| **R-4036** | civilians **riot and attack security forces**; security forces have **non-lethal weapons** |
| **R-3013** | ✅ **CV-1…CV-12 leans approved as written** (§4) — 🔒 user, `2026-10-16`, after a plain-words walkthrough of all twelve: *"approved"* |
| **R-2024** | ✅ **CV-13, CV-13a, CV-14 approved as written** (§4) — gestures and drawn props, hands up holds fire, the security forces provoke a crowd |

🔒 **User, verbatim (`2026-10-17`):**
- *"Do we have special stances for demonstrating pedestrians? Like they might be raising hands or holding a poster On a pole. They might also need some behavior. I don't know exactly what, but they might sometims  respond to the security forces by switching to rioting."*
- *"approved, add CV-13 and CV-14 to the design"*

## 1. INVENTORY *(2026-10-16 — four read-only sweeps: codebase-memory CLI `search_graph` + grep + the design corpus; `check_index_coverage` NOT run)*

| area | measured | where |
|---|---|---|
| types | `CivilianPedestrian` 1001, `CivilianChild` 1003, `CivilianCar` 1002 — `BrainTier 0`, no `CombatPlatformDefDto` (no Health), no `CharacterAnimationDefDto` (no stances), 30 m vision + 100 m hearing; map keys 501–505 have NO template. `search_graph .*Civilian.*` = 74 nodes | `Hrot.Core/Tkb/UrbanCombatTkbCatalog.cs:141-176`, `MapDefinitions/TkbEntityTypes.cs:130-131` |
| replacement blast radius | 54 symbol lines in 12 files; 4 scenarios use `"TkbType": 1001` (`ua-threat-ranking`, `ua-attack-approach`, `tt-heard-shot`, `tt-posture`); ~9 test files use literal 1001 | grep |
| how a type is defined | code catalogues registered by `HrotEnvironment.RegisterBuiltInTkb` (`HrotEnvironment.cs:42-54`); one TKB per deployment (R-4018); a type's default behaviour = `BehaviorProfileDto.DefaultBehaviorHash`/`DefaultSop` (`BehaviorTkbTranslator.cs:82-118`); brain group granted only when `BrainTier != 0` (`HrotOwnershipGroups.cs:138`) | — |
| civilian AI | ⛔ none in production: `WanderCivil`/`PanicFlee` ids only (`BehaviorIds.cs:28-31`); `TrafficBrainSystem` example only; `FleeExecutor` legacy (R-259) | — |
| HSM cost | FastHSM kernel 15 ns/instance idle; ⚠ HROT calls `HsmKernel.Update` per entity inside `BrainTickSystem` (`HsmRunner.cs:189`), not batched; ⛔ no HROT per-entity brain cost measured | `FastHSM/benchmarks/BENCHMARK-RESULTS.md` |
| perception cost | vision 100 / 250 / 500 / 1000 units = 9.6 / 36 / 76 / 230 ms; every sensor every tick only up to ~500 units | `DESIGN_Sensors_And_Doctrine.md` §9.4 |
| side | `ForceId { Neutral, Friend, Hostile }` on `EntityInfo`, replicated (`dtEntityInfo`), owned by the CREATOR (R-160); set at birth only — no runtime writer except the generic attribute patch | `Fdp.Core/Components/ForceId.cs:10-20`, `EntityInfoEgressTranslator.cs:115-147` |
| who reads side | vision drops only the observer's own force (`VisionBroadphase.cs:79`) ⇒ soldiers SEE neutrals; ⛔ `ThreatDanger` (unarmed 0.3), `ThreatRankingDecision`, `TopThreat`, `AimAndFireExecutor.RoePermitsFire` never check the target's side; `TargetMemory` stores no side (reads live); `LineOfFire` fires through neutrals | `ThreatDanger.cs:18-33`, `AimAndFireExecutor.cs:35-47` |
| perceived vs true side | ⛔ none — every reader uses ground truth; G11 (identification) open; D4 approved (hidden truth + cues) | `DESIGN_Sensors_And_Doctrine.md` §11.2 |
| ROE | `Roe { HoldFire, ReturnFire, FireAtWill }` (R-200); no escalation ladder, no weapon-category tier | `Behavior/Components/Roe.cs:9-58` |
| doors | `DoorState {Open, Closed, Locked, Destroyed}` replicated (`dtDoorState`); `DoorAwareQueryFilter` (Locked = wall, Closed +10 m); ⭐ `DoorPassageSystem` opens a closed door for ANY path-following person (`DoorPassageSystem.cs:31-139`) | `Terrain/TerrainObjects.cs:16-31`, `NavDoorways.cs:99-168` |
| buildings | `TerrainWorld.Buildings` (footprint, storeys, `Solid`), `Doors[]`; ⛔ no inside test, no enterable-building EQS generator (`IndoorTest` is design only); only **bt-range** ships enterable houses | `TerrainWorld.cs:113-115`; catalogue §6 |
| self-delete | ⛔ no behaviour deletes itself; `DestroyEntityCommand` disposes on the wire only from the primary owner (`CycloneNetworkCleanupSystem.cs:74-120`) | — |
| weapons | `WarheadKind {Kinetic, HighExplosive, Fragmentation, ShapedCharge}`; damage is ONE scalar (`DamageAssessedEvent`, `DetonationEvents.cs:26-42`); `AreaEffectSystem` is a one-moment blast; smoke/fire are cosmetic; ⛔ no gas, stun, melee, non-lethal, persistent area effect | `WarheadDto.cs:9-19`, `AreaEffectSystem.cs:33-320` |
| groups | `UnitRoster` ≤ 16, `TargetMemory` 16 slots; SquadMarch formations File/Wedge/Line; ⛔ no cordon; ⛔ no crowd-level system (catalogue T3: a crowd is an uncommanded group, not a squad) | `UnitRoster.cs:42`, `PerceptionConstants.cs:11` |
| crowd navigation | dtCrowd `MaxAgents = 128`, Stride nodes only; SimHost persons move through `CarKinematicsSystem` `HumanGait` | `DotRecastDtCrowdProvider.cs:44` |
| side on the wire *(behaviors, 2026-10-16)* | ⚠ `EntityInfo` egress re-sends ONLY when `dtEntityInfo` is marked dirty (`SmartEgressUtil.ShouldPublish`) — a `ForceId` write that does not `MarkDirty` changes the creator's copy only (the `AX-015` rename failure); a receiver DOES apply an update to a live entity | `EntityInfoEgressTranslator.cs:51-53, 121`, `EntityInfoIngressTranslator.cs:177-193` |
| entity classes *(behaviors, 2026-10-16)* | `EntityClassTable` has `Person` (`3`) and `LandPerson` (`3.1`) but no civilian or security-force class — the picker folder, the 3-D model (`VisualFamilies`) and the behaviour lookup (`<Class><Order>`, R-278) all read this table (R-280) | `Hrot.Core/Tkb/EntityClassTable.cs:109, 174` |

## 2. Diagrams

### 2.1 Classes

```mermaid
classDiagram
  direction LR
  class CivilianTypes { <<NEW TKB family>> Adult Child Elderly variants, BrainTier Hsm, Health, Injury, BodyZones, stances, PersonCarrier, hearing + short vision }
  class CivilianHsm { <<NEW HSM asset>> Routine Alarmed Panic Shelter Cower Casualty Protest Riot ArmedHostile }
  class CivilianState { <<NEW component, brain>> Fear Anger Attitude CrowdId ShelterBuilding InsideSince }
  class HostileAct { <<NEW component, replicated>> SinceSimTime Kind }
  class EntityInfo { <<existing>> ForceId flips Neutral to Hostile on a hostile act }
  class Crowd { <<NEW bodiless entity>> Members Mood Density Focus Instigators }
  class CrowdSystem { <<NEW, CGF>> mood, contagion, gathering, focus, provocation by security forces CV-14 }
  class Gesture { <<NEW component, replicated, CV-13>> None HandsUp FistRaised HoldSign Waving Throwing }
  class FigurePose { <<existing, map 3-D>> arm override per gesture, placard drawn for HoldSign }
  class FindEnterableShelter { <<NEW EQS template>> door generator, InsideTest, door-aware PathCost }
  class InsideTest { <<NEW EQS test>> point in a non-solid footprint }
  class DoorPassageSystem { <<existing>> opens closed doors on the path }
  class DestroyEntityCommand { <<existing>> }
  class EffectHit { <<NEW>> DamageAssessedEvent grows an EffectKind }
  class EffectState { <<NEW component, stored>> Stunned Irritated KnockedDown Until }
  class AreaEffectCloud { <<NEW entity>> Kind TearGas Smoke Fire, Radius, Lifespan }
  class AreaEffectCloudSystem { <<NEW, SimHost>> applies effects inside, LOS transmittance }
  class Roe { <<existing, grows>> +ForceLevel Warn NonLethal Lethal }
  class AimAndFireExecutor { <<existing, grows>> target side filter, weapon by ForceLevel }
  class ThrowAt { <<NEW node, D5>> reuses the grenade arc }
  class MeleeAttack { <<NEW node, D5>> contact damage + knockdown }
  class WarheadDto { <<existing, grows>> +Blunt +Irritant +Stun }
  class Lifespan { <<existing>> }
  class EntityClassTable { <<existing, grows>> Civilian, SecurityForce classes }
  class SmartEgressUtil { <<existing>> MarkDirty dtEntityInfo }
  CivilianTypes --> EntityClassTable : one class per variant, R-280
  HostileAct ..> SmartEgressUtil : the flip marks the record dirty
  CivilianTypes --> CivilianHsm : DefaultBehaviorHash
  CivilianHsm ..> CivilianState
  CivilianHsm ..> FindEnterableShelter
  FindEnterableShelter ..> InsideTest
  CivilianHsm ..> DestroyEntityCommand : after the hide time
  CivilianHsm ..> ThrowAt
  CivilianHsm ..> MeleeAttack
  ThrowAt ..> HostileAct
  MeleeAttack ..> HostileAct
  HostileAct ..> EntityInfo : creator flips ForceId
  CrowdSystem --> Crowd
  CrowdSystem ..> CivilianState : mood, contagion
  WarheadDto ..> EffectHit
  EffectHit ..> EffectState
  AreaEffectCloud --> Lifespan
  AreaEffectCloudSystem ..> AreaEffectCloud
  AreaEffectCloudSystem ..> EffectState
  AimAndFireExecutor ..> Roe
  AimAndFireExecutor ..> EntityInfo : skips Neutral targets
  CivilianHsm ..> EffectState : cower, disperse
  CivilianHsm ..> DoorPassageSystem : moves through doors
  CivilianHsm ..> Gesture : writes, on CGF
  FigurePose ..> Gesture : reads, every map host
  AimAndFireExecutor ..> Gesture : HandsUp holds fire, CV-13a
  CrowdSystem ..> EffectHit : force used on a member, CV-14
  CrowdSystem ..> EntityClassTable : security forces near the crowd, CV-14
```

*What the picture shows that prose hid:* the side flip has ONE producer (a hostile act, made by the civilian's own brain on
CGF, which is also the creator that owns `EntityInfo`) and every existing side reader picks it up live, because
`TargetMemory` stores no side — on OTHER nodes only because the flip marks `dtEntityInfo` dirty (§1, the egress re-sends
nothing otherwise); and every non-lethal effect lands in ONE stored component (`EffectState`) that both the
civilian HSM and the body read — the I1 rule stays intact because health is never used to mean "stunned". ⭐ *(CV-13/CV-14, 2026-10-17)* — what a protester DOES
with his body is a third, separate carrier (`Gesture`), written by the same brain and read by the map and by the fire gate; and the
crowd's anger has TWO producers, instigators and the security forces themselves, so a police action can tip a crowd into a riot.

### 2.2 The civilian HSM

```mermaid
stateDiagram-v2
  [*] --> Routine
  Routine --> Alarmed : heard a shot or blast, saw a casualty
  Alarmed --> Routine : quiet for a while
  Alarmed --> Panic : fear over threshold
  Routine --> Protest : joined a gathering crowd
  Protest --> Riot : crowd anger over threshold
  Riot --> Protest : anger falls
  Riot --> ArmedHostile : picks up or draws a weapon
  Protest --> Panic : lethal fire nearby
  Riot --> Panic : fear beats anger
  Panic --> Shelter : an enterable building is reachable
  Panic --> Cower : under fire, no shelter reachable
  Cower --> Panic : fire stops
  Shelter --> Inside : through the door
  Inside --> [*] : hide time over, entity deleted
  Routine --> Casualty : wounded
  Panic --> Casualty : wounded
  Riot --> Casualty : wounded
  Riot --> Dispersing : gas, stun or baton round
  Dispersing --> Panic : still exposed
  Dispersing --> Routine : clear
```

*What the picture shows that prose hid:* panic never ends in open-ended running — every panic path drains into Shelter
(and deletion) or Cower (R-4035); and the only edges INTO violence go through the crowd (Protest → Riot) or a weapon, so a
lone frightened civilian never attacks.

### 2.3 Sequence — a shot, a panicking civilian, a building, gone

```mermaid
sequenceDiagram
  participant SH as SimHost (shooter)
  participant CB as CGF brain (civilian HSM)
  participant EQ as EQS FindEnterableShelter
  participant MV as SimHost mover
  participant D as Door entity (CGF owns)
  participant N as every node
  SH-->>CB: WeaponFire heard (hearing sensor), fear rises
  CB->>CB: Alarmed, then Panic
  CB->>EQ: nearest door of a non-solid building, not Locked, door-aware path
  EQ-->>CB: a point inside the footprint
  CB->>MV: NavigationIntent run upright to the point
  MV->>D: closed door on the path, Open command
  D-->>MV: Open
  MV-->>CB: arrived, InsideTest true
  CB->>CB: Inside, wait the hide time
  CB->>N: DestroyEntityCommand (CGF is the creator, so the delete goes out on the wire)
```

*What the picture shows that prose hid:* no new door logic — the mover already opens a closed door for any person on its
path, and a Locked door is a wall to the route planner, so "if the doors can be opened" is answered by the EXISTING planner;
and the delete works because CGF both runs the brain and created the entity.

### 2.4 Sequence — a riot meets a police line

```mermaid
sequenceDiagram
  participant CS as CGF CrowdSystem
  participant R as rioter HSM
  participant SH as SimHost
  participant P as police unit (Roe ForceLevel)
  CS->>R: crowd anger high, focus the police line
  R->>SH: ThrowAt stone (Blunt warhead, thrown arc)
  R->>R: HostileAct stamped, creator flips ForceId to Hostile
  SH-->>P: hit or NearMiss sensor
  P->>P: target is Hostile and unarmed, ForceLevel NonLethal allows baton round or gas
  P->>SH: fire a baton round or launch a tear gas canister
  SH->>SH: AreaEffectCloud spawns, applies Irritated to people inside each tick
  SH-->>R: EffectState Irritated
  R->>R: Dispersing, then Panic or Routine
  Note over P: lethal only when the target is armed and ForceLevel is Lethal
```

*What the picture shows that prose hid:* the police never decide on ground truth — they act on what the rioter DID
(the hostile act made it Hostile) and on what it CARRIES (armed or not), and the ladder is a property of the police unit's
Roe, not of the weapon.

### 2.5 Modules — who registers it, who calls it each frame

```mermaid
graph TD
  subgraph CGF["CGF (brain node, creator of civilians)"]
    BT[BrainTickSystem] --> HSM[CivilianHsm per civilian]
    CSY[CrowdSystem] -->|ModuleHost tick| CRW[Crowd entities]
    HSM --> EQS[EqsSolverSystem FindEnterableShelter]
    HSM --> DEL[DestroyEntityCommand]
    HSM --> FLIP[ForceId flip on HostileAct]
  end
  subgraph SimHost["SimHost (bodies)"]
    CK[CarKinematicsSystem HumanGait] --> DPS[DoorPassageSystem]
    FP[FireProcessing + Ballistics] --> HIT[hits with EffectKind]
    AEC[AreaEffectCloudSystem] --> EFS[EffectState]
  end
  subgraph Maps["map hosts"]
    MAP[3D map body layer: outfits, cower pose, casualty]
  end
  subgraph Stride["Stride nodes"]
    DT[dtCrowd 128 agents]
  end
  style DT fill:#f66,color:#000
```

*What the picture shows that prose hid:* the red box — dtCrowd exists only on Stride nodes and caps at 128 agents, so
civilians on SimHost move through `CarKinematicsSystem` like soldiers; the catalogue's "crowd agents (dtCrowd)" in D3 is
NOT the movement host here (CV-10). And everything that decides runs on CGF; SimHost only moves, hits and applies clouds.

## 3. Why — what the diagrams cannot carry

- **One family, modes not types.** AQ93 P5 proposed a `PanickedCivilian` type. A runtime mood change (R-4034) and a
  handover from SUMO both want the SAME entity to move between routine, panic and violence; a type per state would mean
  re-creating entities on every mood change. ⇒ superseded by CV-1.
- **Side = what others can see.** D4 (approved) forbids omniscient detection. Flipping `ForceId` only on an OBSERVABLE act
  gives disguise for free (a hostile-at-heart civilian reads Neutral until it acts) without building G11's identification
  layer first, and every existing side reader (`VisionBroadphase`, `FactionFilterTest`, drones, map tint) already reads it
  live.
- **The Neutral filter is a safety gate, not a feature.** Today nothing stops a soldier engaging a neutral — an unarmed
  civilian simply ranks at 0.3. Civilians with brains, in crowds, next to soldiers make that a live hazard the moment slice
  CIV-1 ships, so it ships first (AQ93 P6).

## 4. Decisions — ✅ RULED `2026-10-16` (R-3013): every lean below approved as written

| id | question | ⭐ lean | rejected — one line each | blast radius |
|---|---|---|---|---|
| **CV-1** | the new types | ONE civilian family in a new catalogue (`CivilianTkbCatalog`): Adult (m/f), Child, Elderly variants, NEW type ids (1001/1003 retired, not reused); `BrainTier = Hsm`, `DefaultBehaviorHash = CivilianHsm`, `CombatPlatformDefDto` (Health), `InjuryDefDto`, `BodyZonesDto` (HL-12), `CharacterAnimationDefDto` (stand/crouch/prone), `PersonCarrier` (can pick up a weapon or a stone), footsteps; outfits kept (CE-1099); `CivilianCar` stays until SUMO; ⭐ *(behaviors, 2026-10-16)* **a `Civilian` and a `SecurityForce` class in `EntityClassTable`** (R-280: the picker folder, the 3-D model and the behaviour lookup must agree on what the new types are) | a type per state (`PanickedCivilian`, `Rioter`…): re-create on every mood change · patching 1001: keeps ids whose meaning changes under live scenarios | 4 scenarios + ~12 code files + ~9 tests migrate; map keys 501–505 re-pointed |
| **CV-2** | sensors | hearing 100 m (shots, blasts, footsteps) + **short vision 30 m, Low priority band, 1 Hz refresh**; crowd awareness comes from the CrowdSystem, not per-person vision — a Neutral observer drops Neutral targets anyway (`VisionBroadphase.cs:79`) | 360° full vision: ~76 ms at 500 units · hearing only (AQ93 P5): a rioter could not see the police line it attacks | perception budget — measured in CIV-1's perf gate |
| **CV-3** | the behaviour host | **one `CivilianHsm`** per civilian (R-263), the states of §2.2; reads `CivilianState` (fear, anger, attitude), `EffectState` and crowd hints | BTree per civilian: R-263 rules HSM · Tier-0 C# system (tutorial §2): cannot express the modes | behaviors lane |
| **CV-4** | attitude and side | hidden attitude in `CivilianState` (brain only); a **hostile act** (throw, melee, fire, draw a weapon) stamps replicated `HostileAct` and the creator (CGF, owner of `EntityInfo`) flips `ForceId` Neutral → Hostile — sticky for the exercise in slice 1; ⭐ *(behaviors, 2026-10-16)* **the flip goes through ONE engine write path that also marks `dtEntityInfo` dirty** (§1: otherwise only CGF sees it), pinned by a two-node rail; an armed hostile civilian uses separable weapons (`PickUp`, `FireWhileCarried`) | flipping on attitude alone: omniscient detection (D4) · building G11 perceived-side first: large and not needed for this | `EntityInfo` writer on CGF; map symbol letter may stay stale (known limit) |
| **CV-5** | crowds | a **bodiless `Crowd` entity** (no `UnitRoster` — 16-member cap) and a CGF `CrowdSystem`: membership by proximity, mood (mean fear/anger), **contagion** of fear/anger by distance, gathering point, focus (a building, a police line), instigators (ASY-5) who raise anger; members read it through `CivilianState` | a squad per crowd: 16 members, and catalogue T3 says a crowd is uncommanded · per-person perception for contagion: civilians do not see each other | behaviors lane + one new system |
| **CV-6** | non-lethal effects | ⓐ `DamageAssessedEvent`/`EntityHitDamage` grow an **`EffectKind`** (Kinetic, Blunt, Irritant, Stun, Push) + magnitude; ⓑ a **stored `EffectState`** (Stunned, Irritated, KnockedDown, Until) on the victim — the case I1 names; ⓒ a persistent **`AreaEffectCloud`** entity (tear gas, smoke, later fire) on `Lifespan`, its system applying effects each tick and reducing LOS transmittance; ⓓ new `WarheadKind`s Blunt, Irritant, Stun | deriving stun from Health: breaks I1 · a cosmetic smoke effect: does not affect sight or people | engine (backend lane): damage path, AreaEffectSystem sibling, LOS strategy |
| **CV-7** | shelter and vanish | EQS **`FindEnterableShelter`**: generator over doors of non-solid buildings (not Locked in `DoorStates`), a point inside the footprint (**new `InsideTest`** = `PolygonMath.Contains` + storey), door-aware `PathCost`; the mover opens closed doors itself; inside ⇒ wait a **hide time** (scenario parameter, e.g. 60–180 s, randomised), then the brain issues `DestroyEntityCommand`; no reachable building ⇒ `FallBack` away, Cower under fire | open-ended fleeing (rejected by the user) · `PhysicalPresence.IsHidden` inside: keeps entities alive for nothing | EQS + one HSM state; test terrain bt-range |
| **CV-8** | escalation of force | `Roe` grows a **`ForceLevel`** {Warn, NonLethal, Lethal}; **ground-AI targeting skips Neutral targets** (threat ranking, `TopThreat`, `AimAndFire`) — the safety gate, first; a Hostile target is engaged with the weapons its `ForceLevel` allows; Lethal also needs the target armed or a lethal act; security-force SOP: Warn (an audible cue) → NonLethal → Lethal | weapon-per-target hard-coding: no doctrine knob · a full G11 identification model first: too big for the gate | `AimAndFireExecutor`, `ThreatRankingDecision`, `Roe` |
| **CV-9** | the weapons | slice 1 of non-lethal: **baton/melee** (D5 `MeleeAttack`), **baton round** (ballistic, Blunt), **tear gas grenade/launcher** (Indirect, Irritant cloud), **stun grenade** (thrown, Stun area); civilians' **stone** and **Molotov** (`ThrowAt`, Blunt / fire cloud); later: water cannon (a spray weapon), LRAD | all at once: the spray and acoustic weapons need new physics | TKB weapons + munitions |
| **CV-10** | scale | target **~300 active civilians per CGF** (perf gate in CIV-1, measured: brain tick per entity, perception, movement); movement through `CarKinematicsSystem` `HumanGait` on SimHost (dtCrowd is Stride-only, 128 cap); above the budget, the scenario spawns fewer — no brain LOD in slice 1 | thousands of brained civilians: unmeasured, perception alone tops out near 500 · dtCrowd: not on SimHost | measurement first |
| **CV-11** | security forces | a police/security unit family (shield, baton, baton-round launcher, gas launcher, stun grenades) and a **hold-the-line** behaviour on SquadMarch's `Line` formation; a proper cordon waits for CE-2124 (formations) | building a cordon formation now: CE-2124 is the owner | behaviors lane |
| **CV-13** *(2026-10-17, R-2024)* | gestures and props | ONE replicated **`Gesture`** byte per person {None, HandsUp, FistRaised, HoldSign, Waving, Throwing}: the civilian HSM writes it on CGF (as it writes `Cowering`); the map's `FigurePose` reads it as an ARM override (the way it draws carry arms today); **HoldSign draws a pole + placard in the hands — drawn only, no entity**; Throwing is the wind-up `ThrowAt` (CE-2238) sets | fold into `EffectState`: those are timed effects of a hit, a gesture is the person's own act · a real carried-item entity for the pole: heavy for a picture — later, if a pole must be dropped or used as a club · new stances: a stance is locomotion (stand / crouch / prone), a gesture is the arms over any stance | backend: the component + wire (B-6); ui: the poses + placard (U-6); behaviors: who writes it (CE-2240) |
| **CV-13a** *(2026-10-17, R-2024)* | hands up | a person showing **HandsUp is held like a neutral** — `AimAndFireExecutor.HoldOf` ⇒ a new `HoldSurrender`, the same gate CIV-0 built (§5b) — even when Hostile; it does NOT flip the side back | decoration only: a soldier shooting a surrendering man is exactly what a trainee is assessed on · flip Hostile → Neutral on hands up: the act stays recorded, and a feint must not erase it | `AimAndFireExecutor`, `ThreatDanger` (behaviors) |
| **CV-14** *(2026-10-17, R-2024)* | the security forces provoke | the `CrowdSystem` (CV-5) gets anger inputs beside the instigators: ① security forces (the `SecurityForce` class, CV-1) within ~30 m and FACING the crowd raise it slowly; ② force used on a member (baton, baton round, gas, an arrest — an `EffectHit` on him) SPIKES it in his neighbours; ③ the police withdrawing lets it decay. Each member has a **hidden threshold from his attitude** (CV-4), so only a FRACTION riots — the same non-lethal hit makes a calm crowd disperse and an angry one riot (the Riot / Dispersing edges of §2.2 both stay) | a fixed scripted switch: predictable, not a test of the trainee's choice · per-person vision of the police: the perception cost CV-2 already ruled out | `CrowdSystem` (behaviors, with CE-2237/CE-2241) |
| **CV-12** | the SUMO hookup | last slice (R-4033): AQ93 §8's handover creates a civilian of this family in **Panic** mode with the incident point — no special type | a SUMO-specific type: two civilian models | AQ93 §8 |

## 5. Slice plan *(backend + ui halves handed off as frames 2026-10-16; behaviors half recorded, not started)*

⭐ Handoffs: [backend B-1…B-5](blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md) ·
[ui U-1…U-5](blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md). Behaviors rows: `CE-2233`…`CE-2239` in the
[tracker](blueprints/Blueprint_Issues_Tracker.md).

| slice | content | owner | acceptance |
|---|---|---|---|
| **CIV-0** | **safety gate**: ground-AI targeting skips Neutral targets (CV-8 first half) | behaviors `CE-2233` | a soldier at FireAtWill next to a neutral civilian does not engage it; `ua-threat-ranking` migrates |
| **CIV-1** | the new civilian family (CV-1/CV-2) + its class rows, old types retired, 4 scenarios migrated; `CivilianHsm` Routine + Alarmed + Casualty; **perf gate** (CV-10) | backend B-2, B-4 · behaviors `CE-2235` · ui U-1 | 300 civilians on CGF within the frame budget, measured |
| **CIV-2** | Panic → Shelter → Inside → deleted, Cower (CV-7) on bt-range | behaviors `CE-2234` (§5a first), `CE-2236` | a shot near a civilian sends it through an openable door into a house and it is gone after the hide time; a locked house is skipped |
| **CIV-3** | crowds (CV-5): gathering, protest, contagion; ⭐ the protest's **gestures + placards** (CV-13) | behaviors `CE-2237`, `CE-2240` · backend B-6 · ui U-5 (optional), U-6 | onlookers gather at an incident; fear spreads through a crowd; a protest shows its gestures on every map |
| **CIV-4** | attitude + hostile acts (CV-4), `ThrowAt` + `MeleeAttack` (D5), riot (CV-3) | backend B-3 · behaviors `CE-2238` · ui U-2 | a rioter that throws a stone turns Hostile on every node; soldiers may now answer it |
| **CIV-5** | non-lethal (CV-6, CV-9), escalation ladder (CV-8), security forces (CV-11); ⭐ the security forces **provoke** (CV-14) and **hands up holds fire** (CV-13a) | backend B-1, B-5 · behaviors `CE-2239`, `CE-2241` · ui U-3, U-4 | a police line disperses a riot with gas and baton rounds without a single lethal shot; an armed rioter draws lethal fire; a pressing police line tips a FRACTION of a protest into a riot |
| **CIV-6** | water cannon, LRAD, Molotov fire cloud | backend | — |
| **CIV-7** | SUMO hookup (CV-12, AQ93 §8) | coordinator | a panicked SUMO pedestrian becomes a civilian in Panic and shelters |

## 5a. Measure before CIV-2 *(behaviors, 2026-10-16 — `CE-2234`; both unmeasured, both load-bearing)*

| # | question | why it decides | where to look |
|---|---|---|---|
| **M-1** | does CGF hold the authority the wire delete needs over a civilian it CREATED, while SimHost moves the body? | §2.3's last step: `CycloneNetworkCleanupSystem` disposes on the wire only when the deleting node `HasAuthority`; if SimHost is primary, "the brain deletes itself" leaves a ghost everywhere else | `CycloneNetworkCleanupSystem.cs:70-120`, R-160, the CGF creation path |
| **M-2** | does the route planner reach a point INSIDE a bt-range house footprint (through its door), and does `DoorPassageSystem` fire on that route? | CV-7's "a point inside the footprint" is only reachable if the navmesh / road graph enters the building; if not, the shelter target must be the door's inner side | `NavDoorways.cs`, `DoorPassageSystem.cs:78-122`, the bt-range terrain |

⭐ **Answered** *(behaviors, `CE-2234`, 2026-10-17)* — both YES, so CV-7 keeps its shape:

| # | answer | evidence |
|---|---|---|
| **M-1** | ✅ **a `DestroyEntityCommand` published on CGF removes a person CGF created on SimHost and on CGF** — no ownership move, no SimHost-side logic. The brain's self-delete is enough | `SplitAuthoritySpawnTests.CE2234_APersonCgfCreated_DeletedOnCgf_IsGoneOnSimHost` (two nodes, real DDS) — passes. ⚠ a measurement rail, not red-proved |
| **M-2** | ✅ **a path from outside reaches inside the house through its door; every doorway of the SHIPPED bt-range House A connects for infantry; a closed door on the route holds the walker and opens once, a locked one is a wall (replans or fails the move)** ⇒ the shelter target may be a point inside the footprint | `RecastNavmeshFactoryTests.Stage2_InsideTheHouse_…`, `Stage5c_ALockedDoorIsAWall_…`, `Stage5d_BtRangeHouseA_EveryDoorwayConnectsForInfantry`, `DoorPassageSystemTests.Stage5d_*` (4) — 7 / 0 |

## 5b. AS-BUILT — CIV-0, the safety gate *(behaviors, `CE-2233`, 2026-10-17)*

Built as CV-8's first half, at every layer a target passes (five — the last two found after the first three shipped) — measured, not assumed: danger 0 alone left the neutral as
`TopThreat`'s pick (it takes the ranking's top at score 0, the CE-2202 corpse case).

| layer | as built | why here |
|---|---|---|
| danger | `ThreatDanger.Of` = 0 for a neutral (`ThreatDanger.IsNeutral`: `EntityInfo.ForceId == Neutral`, read live) | "the ONE function every threat reader calls" (Sensors §7.8a) — counts, the ranking, danger areas, the vehicle budget at once, as the dead (CE-3080) |
| pick | `EqsTacticsNodes.TopThreat`: the designated target, the kept/top threat and the live fallback must be `Engageable` (in the fight AND not neutral) | the one threat pick every fire / sensor / close step goes through (Entity Control §10) |
| fire | `AimAndFireExecutor.HoldOf` ⇒ `ActionReason.HoldNeutral` (label "hold: neutral", amber, weapon posture Ready) — on both the turret and the per-shot path | the last gate before an AI round, whoever picked the target (a squad assignment, an order); ⭐ an operator's (human's) fire does not pass here |
| edges | `ThreatEvaluationSystem`'s FirstThreat / AllClear count THREATS (`ThreatCount`: heard contacts + seen non-neutral ones), against the unit's count last frame — so a neutral turning Hostile is a FirstThreat | 📐 **found by the demo `ua-civ-neutral-gate`**: counted as CONTACTS, the first civilian seen took the one FirstThreat — `BasicInfantrySop` reacted, found nothing to engage, went idle — and the Hostile seen after it raised none: he never fought; and with civilians in view the memory never empties, so AllClear never came. ⛔ SUPERSEDED: `countBefore == 0 && mem.Count > 0`. Rail `SensorChangedEventTests.CE2233_…`, red-proved |
| posture | `StandardInputs.HaveLiveTarget` and `ThreatInSight` skip a neutral as they skip a corpse (CE-2120) | 📐 found by a sweep of every `IsOutOfTheFight` reader: these two gate the FIRING postures — without it a soldier who sees only a civilian picks Suppress with nothing to shoot. (`SquadInputs:270` reads the unit's own members and the EQS `AliveFilterTest` is not a threat gate — left alone) |

📐 **Live** *(2026-10-17, `utility-demo-check.py --launch ua-threat-ranking`, "Civilian Near" now Neutral)*: the ranking settles Armed Near
› Armed Far › Civilian 0, and stays so until both armed contacts are down. ⚠ Once every candidate scores 0 the ranking's debug WINNER
shows the civilian (the scorer returns its top at 0) — harmless, `TopThreat` filters it, but a reader of `/utility` should not take it as
a pick.

⚠ **Known limits of the gate:** ① an entity with NO `EntityInfo` is not neutral (Neutral is the enum's zero; unknown = engageable, as
`ThreatDanger` treats a target this node does not hold). ② An ORDER to attack a neutral is held by the weapon (no override yet — the
escalation ladder, `CE-2239`, decides whether an order may). ③ A grenade or an indirect round at a POINT is not gated by a neutral near
it (collateral is `CE-2239`'s). ④ Scenario sweep (2026-10-17): of 315 typed scenario entities only 4 are Neutral / unset — two vans and two
weather regions, none a combatant.

## 5c. Demo scenarios — one per feature, each a TEST *(behaviors, 2026-10-17; user: "a demo scenario for each civilian feature, with a clear success condition so it is usable as a test")*

⭐ **The rule:** a civilian slice is not done until its demo PASSES. Each demo is ① a `scenarios/<name>/scenario.json`, ② a live check
`python3 scripts/utility-demo-check.py --launch <name>` (a fresh cluster, asserting — the acceptance), and ③ an in-process twin in
`Hrot.ClusterRunner.Integration.Tests` (the CI gate; shipped-scenario twins live in `PostureScenarioTests`). A PASS line names what it
proves; ⭐ every demo also asserts the **precondition that makes the pass meaningful** (e.g. "he SEES the neutral" — otherwise "not
shot" could mean "not seen").

| slice | demo | terrain · cast | ⭐ PASS = all of | state |
|---|---|---|---|---|
| **CIV-0** `CE-2233` | **`ua-civ-neutral-gate`** | test-town · a rifleman (Friend) on a MISSION — CombatPosture → Sentry (MinDanger 0.5) → CombatPosture; an unarmed Neutral civilian; an ARMED Neutral nearest of all; a Hostile farther | ⓪ the cast stands where the scenario put it (else `CE-2242`, not CIV-0) · ① he remembers both neutrals · ② the Hostile is put out of the fight · ③ neither neutral loses health, sampled all run · ④ the neutrals rank at score 0 · ⑤ his fire stops, then no round for 8 s, and his weapon's target is never a neutral (sampled) · ⑥ the armed neutral's side flips to Hostile (`/attribute`, the owner's write path) ⇒ he is engaged, the civilian still unhurt | ✅ **live 3/3 `2026-10-17`** · twin `PostureScenarioTests.CE2233_CivNeutralGate_…` · 📐 it found the 5th CIV-0 layer (the threat edges, §5b) |
| CIV-0 | `ua-threat-ranking` *(migrated)* | test-town · its civilian is Neutral | the neutral civilian scores 0 and is never the ranking's winner | ✅ |
| **CIV-1** `CE-2235` | **`ua-civ-routine-alarm`** | test-town · 6 civilians of the new types (1101–1104) at their routine points; a soldier who fires a burst at a target 40 m from two of them, 150 m from the rest | ① all 6 start in `Routine` · ② within 2 s of the first shot, every civilian within hearing (100 m) is `Alarmed`, every one beyond stays `Routine` · ③ after the quiet time each `Alarmed` returns to `Routine` · ④ one wounded (Health set to 40) ⇒ `Casualty`, stops moving, and stays there | ⏳ with CE-2235 |
| CIV-1 | **`ua-civ-crowd-300`** *(perf gate, CV-10)* | test-town · 300 civilians, mixed variants, routine points spread over the town | ① all 300 run a behaviour · ② over 60 s, CGF frame time p95 ≤ the budget backend's `scripts/perf-civilians.sh` measures against (B-4) · ③ brain tick per civilian reported | ⏳ CE-2235 + B-4 |
| **CIV-2** `CE-2236` | **`ua-civ-shelter`** | bt-range · 4 civilians near House A (doors closed, not locked) and House B (locked); a burst fired 30 m away; hide time set short (20 s) for the test | ① each enters `Panic` · ② each reaches a point INSIDE House A's footprint (InsideTest), its door Open on every node · ③ none enters House B · ④ ~20 s after entering, each is gone on CGF, SimHost and IG | ⏳ |
| CIV-2 | **`ua-civ-cower`** | open ground (no building in reach) · 3 civilians; sustained fire nearby | ① each enters `Cower`, `EffectState.Cowering` true on every node, speed ≈ 0 · ② the fire stops ⇒ `Cowering` false and each leaves `Cower` | ⏳ |
| **CIV-3** `CE-2237` | **`ua-civ-gathering`** | test-town · 12 civilians within 80 m of an incident (a casualty on the street) | ① one `Crowd` entity forms holding ≥ 8 of them · ② their mean distance to the focus falls below 15 m within 60 s | ⏳ |
| CIV-3 | **`ua-civ-contagion`** | test-town · a crowd of 15 spread over 150 m; a blast at one end (heard by the near half only) | ① the near half panics · ② fear then reaches members who did NOT hear it (beyond 100 m): ≥ 3 of them panic via the crowd, not via hearing | ⏳ |
| **CIV-4** `CE-2238` | **`ua-civ-riot-stone`** | test-town · a protest crowd with an instigator, facing a soldier line (Friend, FireAtWill) | ① the crowd reaches `Riot` · ② a rioter's `ThrowAt` hits the line · ③ THAT rioter is Hostile on CGF, SimHost and IG within 2 s; all others stay Neutral · ④ no soldier hits a Neutral rioter, sampled all run | ⏳ |
| CIV-4 | **`ua-civ-melee`** | test-town · a rioter next to a policeman | ① a `MeleeAttack` knocks the policeman down (`EffectState.KnockedDown`) · ② the rioter is Hostile on every node | ⏳ |
| **CIV-5** `CE-2239` | **`ua-civ-police-line`** | test-town · a police squad (shields, batons, baton-round launcher, stun grenades; `ForceLevel = NonLethal`) against a riot | ① police fire ONLY baton rounds / stun grenades / batons — zero lethal rounds counted · ② ≥ 1 rioter knocked down or stunned · ③ the riot breaks up (`Dispersing`/`Panic`, the crowd's members leave) · ④ no death | ⏳ |
| CIV-5 | **`ua-civ-escalation`** | test-town · the police line; one rioter armed with a rifle who fires | ① lethal fire goes ONLY at the armed rioter · ② the unarmed hostile rioters still receive non-lethal only · ③ with `ForceLevel = Warn`, no round at all — the warning cue only | ⏳ |
| **CIV-3** `CE-2240` | **`ua-civ-protest`** | test-town · a protest crowd of 12 at a building, an instigator-free crowd, NO security forces | ① the crowd reaches `Protest` · ② on every node (CGF, SimHost, IG) members show `FistRaised` and `HoldSign` gestures (≥ 3 of each over the run) · ③ nobody turns Hostile and nobody enters `Riot` in 120 s — sampled all run | ⏳ |
| **CIV-5** `CE-2241` | **`ua-civ-provocation`** | test-town · the same protest; a police line (SecurityForce, NonLethal) that advances to 15 m, then uses batons on the front rank; a FIXED seed | ① before the police come, no riot · ② after the batons, ≥ 1 and < all members enter `Riot` (a fraction, not the whole crowd) · ③ the rioters are Hostile on every node, the rest stay Neutral · ④ the same members riot on every run (the seed) | ⏳ |
| **CIV-5** `CE-2241` | **`ua-civ-surrender`** | test-town · a rifleman (Friend); a Hostile armed rioter who drops his weapon and raises his hands mid-fight | ① he is engaged while armed and fighting · ② from the frame his `Gesture` is HandsUp, no round is fired at him for 10 s, his side stays Hostile · ③ hands down + armed again ⇒ he is engaged again | ⏳ |
| CIV-6 *(backend)* · CIV-7 *(coordinator)* | named by their owners | — | — | — |

⚠ **Timing is never a frame count in a live check** — the cluster boots paused and each node ticks on its own clock; every wait is
`wait_for(condition, timeout)` with the condition printed on failure, and every "never" is SAMPLED over the whole run (a minimum, not
an end state).

## 6. Known limits

- Only **bt-range** has enterable buildings; other terrains' buildings are solid prisms — civilians there can only Cower or fall back.
- Doors do not physically block the kinematic mover; a dtCrowd agent ignores doors (Building_Interiors §3j known limits).
- After a side flip, the SIDC symbol keeps its TKB affiliation letter and an `IgSymbolOverride` may keep its old style (map tint follows live).
- `TargetMemory` holds 16 contacts per unit — a soldier facing a 100-person riot remembers the nearest 16.

## 7. Backend detailed designs *(backend, 2026-10-16 — B-3 and B-2 first, as the handoff orders; CVs ruled 2026-10-16, R-3013 — ids CE-3219…CE-3223)*

⭐ Cross-lane: this section is the backend lane's, written into the behaviors-lane design because it owns the programme.
INVENTORY for this section: codebase-memory CLI `search_graph` (`.*(MarkDirty|SetForce|ForceId).*`, 50 hits read) + grep
over `Hrot/` `FDP/` for `ForceId =`, `MarkDirty`, `EcsPatchContext`, the TKB id constants; `check_index_coverage` is not
available through the CLI, so the "no runtime ForceId writer" claim rests on grep and the graph together.

### 7.1 B-3 — the side flip *(CV-4)*

⭐⭐ **Finding: the write path already exists.** `EcsPatchContext.Create(repo, entity)` (`EcsPatchContext.cs:96`) resolves the
world's component→descriptor map itself (Q59-E), gates a write on the component's authority bit (`:187-189`), and its
`FlushDirtyMarks()` marks EVERY descriptor covering the written component (`:160-164`). That is the AX-015 fix, used today
by the attribute patch. ⇒ B-3 is a five-line wrapper over it, not a new dirty mechanism, and FDP code never names
`dtEntityInfo` (the 2026-08-26 ruling: attributes are network-agnostic).

```mermaid
classDiagram
  direction LR
  class EntityAffiliation { <<NEW static, Fdp.Toolkits.Replication>> +Set(repo, entity, ForceId) AffiliationResult }
  class AffiliationResult { <<NEW enum>> Applied, Unchanged, NotOwner, NoEntityInfo }
  class EcsPatchContext { <<existing>> +Create(repo, entity) +CanWrite~T~() +GetUnmanagedComponent~T~() +FlushDirtyMarks() }
  class DescriptorOwnershipMap { <<existing, per world>> GetDescriptorsForComponentId }
  class EntityInfo { <<existing component>> ForceId }
  class EntityInfoEgressTranslator { <<existing>> publishes only when ShouldPublish = dirty }
  class HostileActHandler { <<behaviors CE-2238>> }
  HostileActHandler ..> EntityAffiliation : on CGF
  EntityAffiliation ..> EcsPatchContext
  EcsPatchContext ..> DescriptorOwnershipMap : which descriptors cover EntityInfo
  EcsPatchContext ..> EntityInfo : write ForceId
  EntityInfoEgressTranslator ..> EntityInfo : re-send once
```
*What the picture shows that prose hid:* nothing new sits between the behaviour and the wire except one call; the only
new box is the result enum, so a caller can tell "I am not the owner" from "nothing to do".

```mermaid
sequenceDiagram
  participant B as CGF behaviour (HostileAct)
  participant A as EntityAffiliation.Set
  participant P as EcsPatchContext
  participant E as EntityInfoEgress (CGF)
  participant S as SimHost EntityInfoIngress
  B->>A: Set(civilian, Hostile)
  A->>P: Create, CanWrite EntityInfo
  alt CGF owns EntityInfo (creator, R-160)
    P->>P: ForceId = Hostile, FlushDirtyMarks
    A-->>B: Applied
    E->>S: EntityInfo sample, ForceIdentifier Hostile
    S->>S: apply to the live entity (EntityInfoIngressTranslator.cs:177-193)
  else another node created it
    A-->>B: NotOwner
  end
```
*What the picture shows that prose hid:* the `NotOwner` branch — the flip is local only when the brain node is also the
creator.

| the design rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| egress re-sends only on dirty | ✅ `EntityInfoEgressTranslator.cs:121` (`ShouldPublish`) | ✅ §1 row "side on the wire"; AX-015 |
| one call marks every covering descriptor | ✅ `EcsPatchContext.cs:96, 160-164` | ✅ Q59-E (`EcsPatchContext.cs:31-43` cites it) |
| `EntityInfo` stays with the creator | ✅ `HrotOwnershipGroups.cs:19` (no group ⇒ creator) | ✅ R-160 |
| the brain node IS the creator of every civilian | ⛔ **not measured** — true for CGF-created entities; for scenario-loaded civilians the creator is whichever node loads them | ⛔ searched §5a, none found — ⇒ **M-3 below** |

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| B3-a the call | `EntityAffiliation.Set` over `EcsPatchContext.Create` | a `SetForce` that calls `SmartEgressUtil.MarkDirty(dtEntityInfo)`: FDP code naming a NED ordinal (Q59-E undid exactly that) · a bare `SetComponent`: AX-015 |
| B3-b not the owner | return `NotOwner`; the caller (the behaviour) may retry through the existing `UpdateEntityAttributeRequest` (`{"Affiliation": "Hostile"}`), which the owner applies (`UpdateEntityAttributeRequestSystem`) | a new cross-node "flip" message: the attribute request already routes to the owner and acks |
| B3-c rail | two-node rail: CGF flips, SimHost's `EntityInfo.ForceId` reads Hostile within one publish; red-proof by skipping `FlushDirtyMarks` | — |

**M-3 (measure before CIV-4):** which node creates a scenario-loaded civilian, and therefore owns its `EntityInfo`? If it is
not CGF, B3-b's request path is the normal case, not the fallback.

#### 7.1a AS-BUILT `CE-3221` *(2026-10-16)* — ⛔ SUPERSEDES the class diagram, sequence and B3-a/B3-b above

⭐⭐ **Nothing new was built: the write path already existed one layer up.** `EntityWriteRouter.For(repo).Write(entity, changes)`
(`Fdp.Toolkits/Replication/Attributes/EntityWriteRouter.cs`, AX-005b) applies a change locally through `EcsPatchContext` when this
node owns the component (`EntityWriteRoute.Direct`) and publishes the attribute request to the owner when it does not
(`Requested`) — **both** branches of §7.1's sequence, in one call. The side is the `Affiliation` attribute
(`AttributeIds.Affiliation`, value a `ForceIdentifier`). `EqsDistributedTests` T-DIS4 already flipped forces across hosts this way.
⇒ the drafted `EntityAffiliation` wrapper would have been a second implementation, and was deleted before commit.

| | |
|---|---|
| the call the behaviors lane's `HostileAct` handler makes | `EntityWriteRouter.For(world).Write(civilian, new[] { new EntityAttributeChange { AttributeId = AttributeIds.Affiliation, Value = AttributeValue.FromInt((int)ForceIdentifier.Opposing) } })` |
| **M-3, measured** | a scenario's entities are created by CGF (`CgfEpisodeLoadHandler`: "CGF-authoritative episode load"), so CGF owns their `EntityInfo` (R-160) and the flip is `Direct`; elsewhere the router sends the request |
| rail | `SplitAuthoritySpawnTests.CE3221_ASideFlipMadeOnCgf_ReachesSimHost` (IT-SA-6, domain 36): a CGF-created man flipped on CGF reads Hostile on SimHost. 🔴 Red-proved: with `EcsPatchContext.FlushDirtyMarks` emptied it fails |

### 7.2 B-2 — the civilian catalogue *(CV-1)*

```mermaid
classDiagram
  direction LR
  class CivilianTkbCatalog { <<NEW, Hrot.Core.Tkb>> RegisterAll(tkb) }
  class SecurityForceTkbCatalog { <<NEW, same file or sibling>> shield, baton, launcher men }
  class HrotEnvironment { <<existing>> RegisterBuiltInTkb — one line each }
  class UrbanCombatTkbCatalog { <<existing>> 1001 CivilianPedestrian, 1003 CivilianChild REMOVED; 1002 CivilianCar KEPT }
  class EntityClassTable { <<existing>> +Civilian 3.1.*.2 (HROT) +SecurityForce 3.1.*.3 (HROT) }
  class TkbTemplate { <<existing>> }
  class BehaviorProfileDto { <<existing>> BrainTier Hsm, DefaultBehaviorHash CivilianHsm }
  class CombatPlatformDefDto { <<existing>> Health }
  class InjuryDefDto { <<existing>> }
  class BodyZonesDto { <<existing, HL-12>> }
  class CharacterAnimationDefDto { <<existing>> stand crouch prone }
  class CarrierDefDto { <<existing>> PersonCarrier }
  class SensorCapabilitiesDto { <<existing>> hearing 100, vision 30 Low band }
  HrotEnvironment --> CivilianTkbCatalog
  HrotEnvironment --> SecurityForceTkbCatalog
  CivilianTkbCatalog --> TkbTemplate : Adult m/f, Child, Elderly
  TkbTemplate --> BehaviorProfileDto
  TkbTemplate --> CombatPlatformDefDto
  TkbTemplate --> InjuryDefDto
  TkbTemplate --> BodyZonesDto
  TkbTemplate --> CharacterAnimationDefDto
  TkbTemplate --> CarrierDefDto
  TkbTemplate --> SensorCapabilitiesDto
  CivilianTkbCatalog ..> EntityClassTable : DIS category 2 ⇒ Civilian
  SecurityForceTkbCatalog ..> EntityClassTable : DIS category 3 ⇒ SecurityForce
```
*What the picture shows that prose hid:* the class a type falls into is decided by its DIS mask, not by a name — so the
new catalogue must give each type a DIS category the table's new rows match, or the picker, the 3-D model and the
behaviour lookup (R-280) silently disagree.

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| B2-a ids | NEW block **1101–1104** (Adult m, Adult f, Child, Elderly) and **1201–1203** (security: shield, baton, launcher); 1001/1003 retired, never reused — measured free: the TKB id constants in 1000–1299 are only 1001–1003 | reuse 1001: live scenarios change meaning under them |
| B2-b DIS | Kind 3, Domain 1, Category **2** = Civilian (HROT), **3** = SecurityForce (HROT); ⚠ SISO-REF-010's own life-form category numbers not checked — "(HROT)" marks them ours, as `Infantry (HROT)` 3.1.*.1 already is | category 0 (today's civilians): falls only under `LandPerson`, no class of its own |
| B2-c migration | the 4 scenarios, ~9 tests and map keys 501–505 re-pointed in the same commit; a rail that no scenario/test names 1001/1003 outside HISTORY | a compatibility alias 1001→1101: two ids for one thing |
| B2-d the HSM name | `DefaultBehaviorHash` = the hash of `CivilianHsm`, which the behaviors lane registers (`CE-2235`); until then the type loads with no behaviour (Tier HSM, nothing bound) | waiting on CE-2235: blocks the perf gate B-4 needs |

#### 7.2a AS-BUILT `CE-3220` part 1 *(2026-10-16)* — the types; the retirement waits for CIV-0

| as built | deviation, and why |
|---|---|
| `CivilianTkbCatalog` (`Hrot.Core/Tkb/CivilianTkbCatalog.cs`), registered by `HrotEnvironment.RegisterBuiltInTkb`: 1101 adult male, 1102 adult female, 1103 child (1.25 m ⇒ drawn as a child), 1104 elderly; 1201 shield, 1202 baton, 1203 launcher; `EntityClassTable` rows `Civilian` 3.1.*.2 and `SecurityForce` 3.1.*.3 | — |
| ⛔ **1001 / 1003 NOT retired yet, the 4 scenarios NOT migrated** (B2-c deferred) | ⚠ the new civilians have health; until the neutral-target safety gate (`CE-2233`, CIV-0) lands, a soldier at FireAtWill may shoot them and the 4 scenarios' baselines would move. §3 already orders CIV-0 first. ⇒ **part 2** (retire + migrate ~25 sites incl. Stride tests with literal `1001L`, map key 501) follows CE-2233 |
| the child type is named `CivilianMinor` | 📐 its rail threw: a TKB template name is unique and the old `CivilianChild` (1003) is still registered until part 2 — the same name would have thrown at every host's start-up |
| `DefaultBehaviorHash` left empty | `CivilianHsm` does not exist until `CE-2235`; a brain tier with no default runs nothing (`BehaviorTkbTranslator` publishes no start) — the behaviors lane sets the hash when it registers the HSM |
| CV-2's "Low priority band, 1 Hz" not in the type | `SensorCapabilitiesDto` has no band/refresh field — it is a perception-budget setting (Sensors §9.4), the behaviors lane's `CE-2235` |
| the security forces carry no weapon | baton, shield, launchers are B-5 (`CE-3223`) |

Rails: `EntityClassTableTests.CE3220_TheCivilianFamily_AndTheSecurityForces_HaveTheirOwnClasses`; the type catalogue golden
(`EntityTypeCatalogTests`) gains the seven rows under `Life Form/Land/Civilian (HROT)` and `…/Security Force (HROT)`.

### 7.3 B-1 — non-lethal effects *(CV-6)*

⭐⭐ **Finding: the cloud's sight effect is an obstacle prism.** `TerrainMaterial` already carries `SightTransmittance`,
`ResistanceMmRhaPerMetre`, `SoundAttenuationDb` and `BlocksMovement` (`TerrainMaterials.cs:14-19`), and a `StaticObstacle`
entity (Material + `ObstacleShape`) becomes a terrain prism through the debounced rebake (`StaticObstacles.cs:17-35`,
`StaticObstacleBake.cs:51`, P-7a). ⇒ a gas or smoke cloud = an obstacle of material `smoke` (sight 0.15, resistance 0,
sound 0, `BlocksMovement = false`) — every sight solver sees it with no new LOS code.

```mermaid
classDiagram
  direction LR
  class EffectKind { <<NEW enum>> Kinetic Blunt Irritant Stun Push }
  class DamageAssessedEvent { <<existing, grows>> +EffectKind +EffectMagnitude }
  class EntityHitDamage { <<existing wire, grows>> +EffectKind +EffectMagnitude, appended }
  class HealthApplicationSystem { <<existing, grows>> applies Health AND EffectState on the owner }
  class EffectState { <<NEW component, Brain group beside Health>> Stunned Irritated KnockedDown, UntilSimTime }
  class EffectStateTranslators { <<NEW, dtEffectState>> the dtWeaponAmmo shape }
  class AreaEffectCloud { <<NEW component>> Kind Radius Magnitude }
  class AreaEffectCloudSystem { <<NEW, SimHost, sibling of AreaEffectSystem>> each tick: people inside get an EffectKind hit }
  class StaticObstacle { <<existing, P-7a>> Material smoke, ObstacleShape cylinder-ish box }
  class Lifespan { <<existing>> }
  class WarheadKind { <<existing, grows>> +Blunt +Irritant +Stun }
  class AreaEffectSystem { <<existing, one moment>> Irritant or Stun burst spawns a cloud }
  WarheadKind ..> AreaEffectSystem
  AreaEffectSystem ..> AreaEffectCloud : spawn on Irritant
  AreaEffectCloud --> StaticObstacle : sight
  AreaEffectCloud --> Lifespan : gone after
  AreaEffectCloudSystem ..> DamageAssessedEvent : EffectKind hit, 0 damage
  DamageAssessedEvent ..> EntityHitDamage : existing egress
  EntityHitDamage ..> HealthApplicationSystem
  HealthApplicationSystem ..> EffectState
  EffectState ..> EffectStateTranslators
```
*What the picture shows that prose hid:* the effect rides the EXISTING damage pipeline to the owner (one event, one wire
message, one applier) — so an effect lands on the node owning `Health`, the same rule damage already obeys; nothing new
crosses the wire except two appended fields and the stored state.

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| B1-a the effect on the wire | append `EffectKind` + `EffectMagnitude` to `DamageAssessedEvent` and `EntityHitDamage` (`FireInteractionMessages.cs:221`) | a second hit message: two appliers racing on one victim |
| B1-b the state | `EffectState` beside Health in the Brain group, written only by `HealthApplicationSystem`; KnockedDown sets the stance prone; Health is never read to mean stunned (I1) | derive from Health: breaks I1 |
| B1-c the cloud's sight | a `StaticObstacle` of material `smoke` | a new LOS strategy term: every solver would need it |
| B1-d the cloud's people effect | `AreaEffectCloudSystem` on the node that resolves area effects (SimHost), ticking at 1 Hz | per-frame: a riot's 100 people × N clouds for nothing |

| the design rests on | code | design |
|---|---|---|
| a material can let movement and rounds through | ✅ `TerrainMaterials.cs:14-19` | ✅ Building_Interiors §3c |
| an obstacle entity becomes a prism after a debounced rebake | ✅ `StaticObstacleBake.cs:37-51` | ✅ P-7a (Peek and fire) |
| Health is Brain-group, its applier on the owner | ✅ `HrotOwnershipGroups.cs:53`, `HealthApplicationSystem.cs:47` | ✅ CE-523 |
| a rebake per cloud is cheap enough | ⛔ **not measured** — a navmesh rebake for a non-blocking prism may be wasted work | ⛔ none ⇒ **M-4**: time one cloud's rebake on bt-range; if heavy, skip the navmesh for `BlocksMovement = false` prisms |

#### 7.3a AS-BUILT `CE-3219` part 1 *(2026-10-16)* — the effect on the hit, stored and replicated (B1-a, B1-b, ⓓ)

| as built | deviation, and why |
|---|---|
| `HitEffect` {Kinetic, Blunt, Irritant, Stun, Push} (named so: `Tkb.Domain.EffectKind` is the visual-effects enum) + `Effects.Apply` / `IsStunned` / `IsIrritated` / `IsKnockedDown` (`Fdp.Toolkits/Combat/Effects.cs`) | the "magnitude" is a DURATION (`EffectSeconds`): every effect here is "until when", so a second hit extends it (never shortens it) and nothing has to decay |
| `DamageAssessedEvent.Effect` (`HitEffect`) and the wire's `EntityHitDamage.EffectKind` (byte), each + `EffectSeconds`, APPENDED; both translators copy them; unset = Kinetic, so every existing sender reads as before | — |
| `EffectState` {StunnedUntil, IrritatedUntil, KnockedDownUntil} (component 397, NoScenario, Brain group beside Health), written only by `HealthApplicationSystem` on the Health owner; `dtEffectState` = 142 (`EntityEffectStateTranslators.cs`, the dtWeaponAmmo shape + CE-4007's delete-on-wipe), both packs, the group binding, the shared registry | times are SIM seconds, which the cluster keeps in step, so every node reads the same "is he still stunned" |
| `WarheadKind` += Blunt (4), Irritant (5), Stun (6) | nothing produces them yet: the area-effect cloud and the munitions are part 2 / B-5 |
| ⛔ KnockedDown does not set the stance | the stance is the body's (behaviors / Muscle); `Effects.IsKnockedDown` is the reader they use — no second writer of the stance |

Rails: `HealthApplicationSystemTests.CE3219_ABluntHit_StoresKnockedDown_…`, `EntityHitDamageIngressTranslatorTests.CE3219_TheEffectArrives_…`,
the descriptor-map lists (`TheDescriptorMapIsWiredTests`, `RoleGroupOwnershipStrategyTests`).
**Part 2a (built the same night):** `WarheadDto.EffectSeconds`; a `Blunt` round's direct hit carries `HitEffect.Blunt` for that long
(`DamageCalculationSystem`); a `Stun` burst gives every man its blast reaches a `HitEffect.Stun` hit with no damage, for `EffectSeconds`
× the blast fall-off × barrier × shadow (`AreaEffectSystem` — a wall between him and the flash spares him). Rails:
`DamageCalculationSystemTests.CE3219_ABluntRound_…`, `AreaEffectSystemTests.CE3219_AStunBurst_…`.

⏳ **Part 3 — the cloud (B1-c, B1-d, M-4). ⭐ Lean (re-measured `2026-10-17`): the cloud is a REPLICATED entity.** The node that
owns the detonation creates it through the normal creation path: a TKB "gas cloud" type carrying `StaticObstacle` (material `smoke`)
+ `ObstacleShape` + `Lifespan`. Every node then bakes it with the existing `StaticObstacleBakeSystem`. When the owner's
`LifespanSystem` (`Fdp.Toolkits/Time/LifespanSystem.cs`) removes it, the delete replicates. The owner alone pulses the irritation
(`AreaEffectCloudSystem`). It reuses creation, replication, the obstacle bake and its lifecycle ack. A late joiner and a recording
see the cloud as they see any entity. ⚠ Still to measure: M-4, the rebake cost per cloud.

| the lean rests on | code | design |
|---|---|---|
| one registration covers every host | ✅ the bake is made inside the shared pack (`EntityCreationPack.cs:115`) | ✅ P-7a §9 *(rule and ack both in the pack)* |
| a local derivation would miss nodes | ✅ `MunitionDetonation` ingress is on IG (`NedIgTranslators.cs:45`) and SimHost (`SimHostAuxiliaryTranslatorPack.cs:103`) only, not CGF | ⛔ searched, none found |
| a removed cloud leaves every node | ⛔ assumed: the owner's destroy replicates like any delete | ✅ creation unification §3 |

⛔ **SUPERSEDED (`2026-10-16`):** *"`DetonationNotification` reaches EVERY node … each node derives the same cloud … a registration on
every host, not a single pack line."* Both premises were wrong. CGF does not ingest detonations, and the bake is one line in the
shared pack. A derived cloud would also be missed by a late joiner.

### 7.4 B-4 — the perf-gate harness *(CV-10)*

One command (`scripts/perf-civilians.sh <count>`) runs the ClusterRunner `--mode all` headless on test-town with N civilians
spawned by a generated scenario, reads the scheduler's per-system `SystemProfileData` (`SystemScheduler.cs:21`) through `/diagnostics/architecture` (⚠ its system rows carrying the timing is not yet checked; the profiler panel reads the same data) over 60 s
of sim time on CGF and SimHost, and prints ms/frame for `BrainTickSystem`, the perception systems and `CarKinematicsSystem`
at 100 / 300 / 500. ⭐ It reads the existing diagnostics route — no new timing code. ⛔ Not built until B-2 gives it a type
with a brain.

### 7.5 B-5 — the weapons *(CV-9)*

Data on existing types, no new code beyond B-1: `WarheadKind` Blunt (baton round, stone), Irritant (gas grenade/launcher
round: spawns a cloud), Stun (stun grenade: one Stun hit in radius); munitions in `MunitionTkbCatalog`, launchers as
weapon mounts; the stone and Molotov as `ThrowAt` munitions on the grenade arc (W-12e). ⚠ Baton/melee is a behaviors
node (D5) whose hit goes through B-1-a's event — backend adds only the warhead.

#### 7.5a AS-BUILT `CE-3223` part 1 *(2026-10-16)* — the rounds whose effect exists, and the security loadouts

| built | where |
|---|---|
| three munitions with their own `WarheadDto`: **40 mm baton round** `6036` (Blunt, 4 s, damage 5) · **stun grenade** `6037` (Stun, injury radius 8 m, 8 s, time fuze 1.5 s) · **thrown stone** `6038` (Blunt, 2 s, damage 3); costs 2 / 5 / 0.1 | `MunitionTkbCatalog.cs` |
| loadouts: shield and baton men carry 2 stun grenades; the launcher man a baton-round launcher (20 rounds, 80 m/s, 50 m) and 2 stun grenades | `CivilianTkbCatalog.Security(…, mounts)` |
| rail `NonLethalMunitionTests` (3): each round resolves to its effect with no fragments (`ParameterResolver.Warhead`); the real `DamageCalculationSystem` / `AreaEffectSystem` over the built-in TKB knock down / stun; the security types load the rounds. Red-proved: the baton round set to Kinetic ⇒ 2 red | `Hrot.Core.Tests` |

⏳ **Not built, and why:** the gas grenade and gas launcher round wait for the cloud (§7.3, B-1 part 3). The Molotov waits too, since
its fire is the same kind of lasting area. The stone has no thrower yet: a civilian picks one up through `PersonCarrier`, which is
behaviors' CE-2236. The baton blow is behaviors' melee node (D5).

### 7.6 ui's findings F-1…F-4 — backend's answers *(2026-10-16; [findings](blueprints/batches/HANDOFF_Ui_To_Backend_Civilians_Findings.md))*

| # | finding | answer |
|---|---|---|
| F-1 | a cloud prism would draw as a solid block | ✅ agreed: ui owns the render skip (U4-b). The cloud itself is still not built (B-1 part 3) |
| F-2 | a second `EffectKind` | ✅ already done: the hit's enum is `HitEffect` (`Fdp.Toolkit.Combat/Effects.cs`) |
| F-3 | nothing carries "cowering" | ✅ built as asked: `EffectState.Cowering` (a byte, 0/1), on the wire in `EntityEffectState`. The CivilianHsm (behaviors) sets it through `Effects.SetCowering(repo, e, on)` on the brain owner, the same node that owns `dtEffectState`. ⚠ It is a mode, not a timed effect: it holds until the HSM clears it |
| F-4 | no elderly carrier | ✅ built as asked: the TKB descriptor `PersonDto` (`Body.Person`), holding `Age` (Adult / Child / Elderly) and `Sex` (Unspecified / Male / Female). It is on the four civilian types (male, female, minor, elderly), and the security types say Adult. No descriptor means an adult of unspecified sex, so soldiers need none. Read it with `template.GetDescriptor<PersonDto>()` |

⭐ Ruled `2026-10-16` (R-3013). Tracker: B-1 `CE-3219` · B-2 `CE-3220` · B-3 `CE-3221` · B-4 `CE-3222` · B-5 `CE-3223`.

## 8. UI detailed designs *(ui, 2026-10-16 — U-1…U-4 of the [ui handoff](blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md); ✅ the CVs are RULED (R-3013, 2026-10-16) ⇒ each builds once its backend item exists)*

**INVENTORY** *(2026-10-16; codebase-memory CLI `search_graph` for `.*EntityClassTable.*`, `.*VisualFamil.*`, `.*EffectState.*`,
`.*AreaEffect.*`, `.*IgSymbolOverride.*`, `.*Affiliation.*` + grep on every call site + the design corpus; `check_index_coverage` NOT run — CLI only)*:

| surface | exists | where |
|---|---|---|
| class rows | `Person` 3, `LandPerson` 3.1, `Infantry` 3.1.*.1 — **no Civilian / SecurityForce row** (B-2) | `EntityClassTable.cs:98-191` |
| class readers a new row reaches | picker folders (`DisNameTable` → `EntityTypeCatalog.CategoryOf`), picker icon (`VisualFamilies.IconName`), 3-D family (`VisualFamilies.FamilyOfClass`), 2-D outline (`EntityClassShapeLibrary.ProfileOfClass`), `WeaponKits`, behaviour lookup (`OrderResolver`, `TacticalIntentResolutionSystem`) | `DisNameTable.cs:24-38`, `EntityTypeCatalog.cs:56,87-91,131`, `VisualFamily.cs:51-89`, `EntityClassShapeLibrary.cs:21-42` |
| soldier vs civilian look | ⚠ **a hard-coded `DIS Category == 1`**, not the table | `VisualFamily.cs:104` `IsSoldier` |
| outfits, child | `Outfits.For(seed, type, soldier, child, own, side)`; child = shorter than 1.5 m; no elderly look | `Outfits.cs:91`, `EntityBodyLayer3D.cs:47,223,1204` |
| the 2-D side colour | 🔴 **no map host draws an SIDC**; the shared entity outline is **fixed cyan** on every host (CE-125, Symbology defect A) | `EntityPresentationGizmoShared.cs:272` |
| side palettes | **four** that disagree: `ResolvedStyleConstants` (authoritative, Symbology §3.2), `EntityPlacementGizmo.GetAffiliationColor`, `MilStd2525Renderer`, and the entity card's `SidePalette` | `EntityCardGizmos.cs:13-25` (the fourth, not in Symbology §1) |
| IG override | `StyleResolutionSystem`: live `ForceId` at Layer 1.5, then `IgSymbolOverride.StyleSetId` re-sets the affiliation at Layer 2 — **a flip is outranked**; its output `ResolvedStyle` feeds no renderer | `StyleResolutionSystem.cs:110-131` |
| poses | `FigurePose` from `LogicalStance` + `StanceStatus`; a lying wounded pose exists (`Downed`, C-2); **no cower pose; no `EffectState`** (B-1) | `EntityBodyLayer3D.cs:399-432`, `ShapeKit.cs:1163-1177`, `Carriage.cs:29,88` |
| 2-D state text | `StanceGizmo` — a text line under the entity, on every host | `StanceGizmo.cs:11-35` |
| clouds | **no `AreaEffectCloud`, no smoke obscurant**; `EffectLayer` (2-D + 3-D, every host) already draws a growing fading disc, wreck-smoke puffs and a chaff cloud thinning over its `Lifespan` | `EffectLayer.cs:15-25,141-216`, `MapInteractionPack.cs:485` |
| ⚠ B1-c's prism | every terrain prism is meshed whatever its material ⇒ a `smoke` obstacle draws as a **solid block** (3-D mesh and the 2-D terrain gizmo) | `TerrainWorldMesh.cs:48,128-133`, `StaticObstacles.cs:100-112`, `TerrainWorldGizmo.cs:68` |
| ⚠ a name | `EffectKind` already names the VISUAL-effect enum; B-1 draws a second `EffectKind` | `EffectDtos.cs:7` |

### 8.1 Classes

```mermaid
classDiagram
  direction LR
  class EntityClassTable { <<existing, B-2 adds rows>> Civilian 3.1.*.2 · SecurityForce 3.1.*.3 }
  class VisualFamilies { <<existing, grows>> +PersonKit(t) Civilian / Soldier / Security · IconName per class }
  class Outfits { <<existing, grows>> For(seed, type, kit, age, own, side) · + police look · + elderly look }
  class EntityBodyLayer3D { <<existing>> FigurePose reads EffectState first }
  class FigurePose { <<existing, grows>> KnockedDown ⇒ Downed · Cowering ⇒ Cower }
  class BlockFigure { <<existing, grows>> + Cower parts: crouched, forearms over the head }
  class EffectState { <<B-1 NEW>> Stunned Irritated KnockedDown + Cowering (asked of B-1) }
  class EffectStateGizmo { <<NEW, StanceGizmo's shape>> 2-D text: DOWN · STUNNED · GASSED · COWERING }
  class AffiliationColors { <<NEW, Symbology §3.1>> For(view, entity): ResolvedStyle tint, else live ForceId, else cyan }
  class ResolvedStyleConstants { <<existing, the one palette>> }
  class EntityPresentationGizmoShared { <<existing>> prim.Color = AffiliationColors.For }
  class SidePalette { <<existing, 4th palette>> redirects to ResolvedStyleConstants }
  class StyleResolutionSystem { <<existing, IG>> StyleSetId no longer outranks live ForceId }
  class EffectLayer { <<existing, grows>> + cloud: disc 2-D, puffs 3-D, fades on Lifespan }
  class AreaEffectCloud { <<B-1 NEW>> Kind Radius Magnitude }
  class TerrainWorldMesh { <<existing, grows>> skips see-through walk-through prisms }
  EntityClassTable <.. VisualFamilies : class of the type
  VisualFamilies <.. Outfits : kit
  EntityBodyLayer3D --> FigurePose
  FigurePose ..> EffectState
  FigurePose --> BlockFigure
  EffectStateGizmo ..> EffectState
  EntityPresentationGizmoShared ..> AffiliationColors
  AffiliationColors ..> ResolvedStyleConstants
  SidePalette ..> ResolvedStyleConstants
  EffectLayer ..> AreaEffectCloud
  TerrainWorldMesh ..> AreaEffectCloud : not drawn as terrain
```
*What the picture shows that prose hid:* every new box hangs off an EXISTING reader — the class table, the pose function,
`EffectLayer`, the shared outline — so the four items add one new class (`EffectStateGizmo`) and one helper (`AffiliationColors`,
already designed by Symbology §3.1); everything else grows.

### 8.2 Sequence — a civilian flips Hostile (U-2)

```mermaid
sequenceDiagram
  participant B as CGF behaviour (HostileAct)
  participant A as EntityAffiliation.Set (B-3)
  participant N as EntityInfo egress / ingress
  participant G as EntityPresentationGizmo (every host, each frame)
  participant C as AffiliationColors.For
  participant I as IG StyleResolutionSystem
  B->>A: Set(civilian, Hostile)
  A->>N: ForceId dirty, one sample
  N-->>G: the entity's EntityInfo.ForceId = Hostile
  G->>C: colour of this entity
  C-->>G: no ResolvedStyle here ⇒ the live ForceId's colour (red)
  N-->>I: same sample on the IG
  I->>I: Layer 1.5 live ForceId, then Layer 2 keeps texture and label, NOT the StyleSet colour
  I-->>G: ResolvedStyle tint red ⇒ the IG draws it red too
```
*What the picture shows that prose hid:* "within one update, no reload" costs nothing new on the wire — the gizmo reads the side
every frame; the only reasons it does not redraw today are the cyan constant and the IG override's precedence.

### 8.3 Modules — who registers it, who calls it each frame

```mermaid
graph TD
  MIP[MapInteractionPack.AttachMapLayers, every map host] --> BODY[EntityBodyLayer3D: pose, outfit]
  MIP --> EFX[EffectLayer: the cloud]
  MIP --> TER[TerrainLayer3D: TerrainWorldMesh on rebake]
  SGS[StatelessGizmoSystem, every host, each frame] --> EPG[EntityPresentationGizmo: the outline colour]
  SGS --> ESG[EffectStateGizmo NEW, auto-discovered like StanceGizmo]
  IGM[IG StyleResolutionModule, IG only] --> SRS[StyleResolutionSystem]
  PK[Add-Entity picker] --> DNT[DisNameTable folders, from the class table]
  B1[B-1 AreaEffectCloud + StaticObstacle smoke] -.->|today: meshed solid| TER
  classDef dead fill:#fdd,stroke:#c00
  CYAN[the outline colour is a constant: never reads the side]:::dead
  CYAN -.-> EPG
```
*Caption:* every surface is already called each frame on every map host; ⚠ the red box is why a flip shows nowhere in 2-D today,
and the dashed B-1 edge is the one path where a backend change would draw wrongly unless U-4 lands with it.

### 8.4 Decisions — leans *(the CVs above them are ruled, R-3013; these ui details follow the CV leans)*

| # | item | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **U1-a** | U-1 the soldier look | `VisualFamilies.PersonKit(t)` reads the CLASS (Infantry ⇒ soldier kit, SecurityForce ⇒ police kit, Civilian / other ⇒ clothes), replacing `IsSoldier`'s `Category == 1` | keep `Category == 1`: a police officer (category 3) draws in civilian clothes with no weapon |
| **U1-b** | U-1 elderly | ask B-2 for an explicit age/sex on the type (one small descriptor read by `Outfits`); child stays "under 1.5 m" | derive from the type NAME: two tables (R-280) · from height: an old man is not shorter |
| **U1-c** | U-1 picker | nothing to build: the folders come from the new rows (`DisNameTable`); a rail that a Civilian and a SecurityForce type each land in their folder with their icon | a hand-made civilian folder: a second table |
| **U2-a** | U-2 the 2-D colour | build Symbology §3.1 (CE-125): `AffiliationColors.For` — `ResolvedStyle` tint, else the live `ForceId` from `ResolvedStyleConstants`, else today's cyan; `SidePalette` redirects to the same constants | colour only flipped civilians: a second rule for one colour · keep the cyan: friend and hostile stay identical (defect A) |
| **U2-b** | U-2 the IG override | `IgSymbolOverride.StyleSetId` stops outranking a live `EntityInfo.ForceId` (it applies when no `EntityInfo` is present); its texture and label overrides keep winning | the override always wins: a flipped civilian stays neutral on the IG |
| **U2-c** | U-2 in 3-D | ⭐ **no clothes change** — a hostile civilian still LOOKS civilian (the user's "side is what others can see" is the 2-D symbol and the card); the card frame already follows live side | repaint the figure: makes a disguise visible to everyone |
| **U3-a** | U-3 pose source | `FigurePose` reads `EffectState` FIRST: KnockedDown ⇒ the existing `Downed` pose (C-2), Cowering ⇒ a new `Cower` block pose; else stance as today | per-state models: a second pose system · KnockedDown as plain prone (B1-b sets the stance prone): reads as a soldier taking cover |
| **U3-b** | U-3 cower's carrier | ask B-1 to add a `Cowering` bit to `EffectState`, written by the CivilianHsm (§2.1 already draws `CivilianHsm ..> EffectState : cower`) | a new replicated component for one bit · infer from stance + speed: a crouched soldier would cower |
| **U3-c** | U-3 in 2-D | `EffectStateGizmo`, `StanceGizmo`'s shape: one text line (DOWN / STUNNED / GASSED / COWERING) | an icon set: not designed anywhere yet |
| **U4-a** | U-4 the drawing | `EffectLayer` draws an `AreaEffectCloud`: a disc growing to `Radius` and fading over `Lifespan` in 2-D, the chaff cloud's puffs in 3-D (tear gas off-white, smoke grey) | a gizmo: `EffectLayer` is "no gizmos (U2)" and already owns lifespan visuals |
| **U4-b** | U-4 the prism | `TerrainWorldMesh` and `TerrainWorldGizmo` skip a prism whose material lets sight through (`SightTransmittance > 0`) and does not block movement — a volume effect, drawn by its own layer | let the terrain draw it: a gas cloud renders as a solid grey block |
| **U4-c** | the enum name | ask B-1 to name its enum `HitEffectKind` (or similar) | a second `EffectKind`: two meanings for one name across Fdp.Toolkits |

| the leans rest on | code — how it IS | design — how it was MEANT |
|---|---|---|
| a police officer draws as a civilian today | ✅ `VisualFamily.cs:104` | ✅ R-280 (one class table) says the class decides |
| the 2-D outline never reads the side | ✅ `EntityPresentationGizmoShared.cs:272` | ✅ Symbology §2 defect A, §3.1 the fix (CE-125) |
| the IG override outranks a live flip | ✅ `StyleResolutionSystem.cs:110-131` | ⛔ searched Symbology §3.8.9 / §0, nothing rules precedence against a live flip |
| who publishes `IgSymbolOverride.StyleSetId` (U2-b's blast radius) | ✅ **measured `2026-10-16`: two DELIBERATE producers** — the IG's mini-ExCon panel (`MiniExConPanelState.cs:118-120`, an operator picks the affiliation) and the external `MapEntitySymbol` topic (`MapEntitySymbolIngressTranslator.cs:78,105`) | ⛔ not searched — ⇒ 🔴 **U2-b's lean is WITHDRAWN**: an operator's override is an intent, so it keeps winning; a live flip shows wherever no override was set (§8.6) |
| a `smoke` prism would draw solid | ✅ `TerrainWorldMesh.cs:48,128-133` | ✅ B1-c (§7.3) — the clash is between two correct designs |
| nothing marks cowering on the wire | ✅ B-1's `EffectState` fields (§7.3) | ✅ §2.1 draws the edge — the field is missing, not the intent |

### 8.5 Items *(ids from the ui block)*

| id | item | needs | build when |
|---|---|---|---|
| `CE-1162` | U-1 class-driven person kit, police and elderly looks, picker rail | B-2 rows (`CE-3220`); U1-b's age field | B-2's rows exist |
| `CE-1163` | U-2 side colour on the 2-D outline (= CE-125's §3.1) + the IG override precedence | B-3 (`CE-3221`, built); U2-b measured | now (U2-a); U2-b after its measurement |
| `CE-1164` | U-3 `EffectState` poses (Downed, Cower) + `EffectStateGizmo` | B-1 `EffectState` with Cowering (`CE-3219`) | B-1 exists |
| `CE-1165` | U-4 the cloud in `EffectLayer` + terrain skips see-through prisms | B-1 `AreaEffectCloud` (`CE-3219`) | with B-1 |

U-5 (crowd overlay) stays LATER as the handoff leans, with no id until behaviors' `CE-2237` exists.
⚠ **Findings for the backend lane** (U1-b, U3-b, U4-b, U4-c) are sent in
[`HANDOFF_Ui_To_Backend_Civilians_Findings.md`](blueprints/batches/HANDOFF_Ui_To_Backend_Civilians_Findings.md).

### 8.6 AS-BUILT — U-1 and U-2 (`CE-1162`, `CE-1163`, `2026-10-16`, ui)

| decision | as built | deviation, and why |
|---|---|---|
| U1-a | `VisualFamilies.PersonKitOf(t)` walks the class chain: `SecurityForce` ⇒ `PersonKit.Security`, `Infantry` ⇒ `Soldier`, else `Civilian`; `TypeLook.Kit`; `IsSoldier` (the rifle) = a soldier, or an officer whose type has a weapon suite | ⚠ a security-force officer draws **unarmed** today: backend's `CivilianTkbCatalog` gives the 1201–1203 types no weapons yet (B-5) |
| U1-a (outfit) | `Outfits.For(…, kit)` — Security wears `Outfits.PoliceUniform` (navy), trousers darker, no camouflage whatever his side, the helmet of the uniformed | — |
| U1-b | ⏳ not built: no elderly look until a TKB age carrier exists (finding F-4) | — |
| U1-c | nothing to build: the picker folders come from backend's rows; backend's own `EntityTypeCatalogTests` rows cover it | — |
| U2-a | `AffiliationColors` (Hrot.Common): `For(view, entity)` = the `ResolvedStyle` tint, else the live `EntityInfo.ForceId` from `ResolvedStyleConstants`, else `NoSide` (the old cyan); `EntityPresentationGizmo` passes it to `DrawSemanticShape(…, colour)` on every host; `SidePalette` (the card) and `EntityPlacementGizmo`'s private palette now read the same table | ⚠ the card frame's colours change to the authoritative table (pure blue / red / green): Symbology §3.2 names that table, and two palettes would disagree |
| U2-b | 🔴 **withdrawn** — `IgSymbolOverride.StyleSetId` keeps winning on the IG (it is an operator's or the exercise's deliberate choice, §8.4's measured row) | the lean assumed nobody set it on purpose; measured, two producers do |
| U2-c | no 3-D change (as leaned) | — |

Rails: `EntityPresentationGizmoTests.CE1163_…` ×3 (the live side and its flip, a resolved tint wins, one palette) · `Map3DTests.CE1162_…`
(the kit from the class; police uniform on every side; civilians in clothes; every infantryman still a soldier).

**U-3 (`CE-1164`, `2026-10-16`) as built:** `Carriage.Of` returns `Downed` when `Effects.IsKnockedDown` (backend's `EffectState`, CE-3219) —
so a knocked-down man lies like a man down at full health and stands when it runs out (U3-a, through the carriage that already picks
`Downed` for the incapacitated, not a second pose path in `FigurePose`). `EffectStateGizmo` (Hrot.Common, auto-discovered, Labels layer)
writes `DOWN · STUNNED · GASSED` under him in 2-D (U3-c). ⏳ **Cower is not built**: `EffectState` has no Cowering yet (finding F-3).
Rail `Map3DTests.CE1164_…`, red-proved (both halves).

**U-4 (`CE-1165`) part built `2026-10-16` — U4-b, ahead of B-1's cloud:** `TerrainWorldMesh.IsVolumeEffect(prism)` (a material with
`SightTransmittance > 0` and `BlocksMovement = false`) keeps such a prism out of the mesh — so it neither renders as a block nor enters the
navmesh (it blocks nothing; this also answers backend's M-4 for walk-through prisms) — and `TerrainWorldGizmo` skips it in 2-D. Sight still
reads the prism (B1-c unchanged). ⚠ Cross-lane: `Fdp.Toolkits/Terrain/TerrainWorldMesh.cs` (backend's area), render/navmesh input only.
Today's see-through materials (glass, hedge, chain-link) all block movement ⇒ nothing existing changes. Rail
`TerrainWorldTests.CE1165_…`, red-proved. ⏳ U4-a (the cloud drawn in `EffectLayer`) waits on `AreaEffectCloud`.

