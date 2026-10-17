<!--STATUS
state: LIVE
updated: 2026-10-17 (backend: §7.10 B-13 pick-up designed, CE-3235) · 2026-10-17 (backend: §7.3c CE-3239 a cloud round bursts on the ground) · 2026-10-17 (behaviors: §5i CIV-4 part 1 — riot, the blow, the side flip, READY-TO-BUILD, CE-2238; §5h.2 CV-13a hands up holds fire built) · 2026-10-17 (backend: §7.8 B-7 Armament designed and BUILT, CE-3229) · 2026-10-17 (behaviors: §5h.1 AW-20 as built — a neutral person in the line or the spread holds the shot; §5f CE-2246 — contagion stopped at a crowd split by wandering, fixed in the demo) · 2026-10-17 (behaviors: §5h AW-24 the simple path stays simple, R-2029) · 2026-10-17 (behaviors: §5c the 13 armed-people demos, one per §5h branch) · 2026-10-17 (behaviors: §5h AW-22 civilian → combatant, AW-23 player fratricide, R-2028) · 2026-10-17 (behaviors: §5h AW-21 status vs conduct, R-2027) · 2026-10-17 (behaviors: §5h amended — AW-15…AW-20, R-2026; AW-1/AW-3 superseded) · 2026-10-17 (behaviors: §5h armed people AW-1…AW-14, R-2025) · 2026-10-17 (behaviors: §5f crowds — onlookers gather, fear spreads, CE-2237; §5g the protest and its gestures, CE-2240) · 2026-10-17 (behaviors: §5e CivilianHsm slice 2 panic/shelter/cower, CE-2236 + CE-2245 the stopped-move fix; §5d CivilianHsm slice 1, CE-2235; CV-13 gestures + props, CV-13a hands up holds fire, CV-14 security forces provoke — R-2024; §5b CIV-0 as-built; §5c demos) · 2026-10-16 (behaviors: CV-1 class-table rows, CV-4 dirty-mark, §5a measurements, §5 owners/ids + handoffs; backend: §7 detailed designs B-1…B-5; ui: §8 detailed designs U-1…U-4, CE-1162…CE-1165)
build-state: BUILDING (backend B-3/B-2 first) — the HOW is RULED too: CV-1…CV-12 approved as written 2026-10-16 (R-3013).
  SUPERSEDED: the programme's scope and order are RULED by the user 2026-10-16 (§0, R-4032…R-4036); the HOW is
  leans per decision (§4, CV-1…CV-12), awaiting the user. §5 is the slice plan; its backend and ui halves are handed off
  as FRAMES (§5, the receiving lane designs its details first, builds after the user rules the CV it builds), and the
  behaviors half is recorded as CE-2233…CE-2239. Written by the coordinator lane (cross-lane: most slices are
  behaviors-lane work, CV-6 is engine/backend work); the 2026-10-16 additions are the behaviors lane's.
current-answer: §0 the rulings · §2 the diagrams · §4 the decisions with leans · §5 the slice plan · §5a measure before CIV-2 · §5b CIV-0 as-built · §5c demos · §5d CIV-1 CivilianHsm as-built · §5e CIV-2 panic, shelter, cower as-built · §5f CIV-3 crowds (CE-2237) · §5g the protest (CE-2240) · §5h armed people (R-2025) · §7 backend detailed designs (B-1…B-5) · §8 ui detailed designs (U-1…U-4).
stale-below: nothing.
known-rot: §4 CV-4's "draw a weapon" as a hostile act — amended in place (R-2026, §5h AW-16).
known-conflict: ⭐ RESOLVED 2026-10-17 (CE-2235) — docs/TUTORIAL_Behaviour_Composition.md §2 put "crowds" at Tier 0; it now says
  Tier 0 is for entities that only move (traffic) and civilians run one CivilianHsm (R-263). ⚠ AQ93 §8 P5 named a single "PanickedCivilian" type — SUPERSEDED here by CV-1
  (one civilian family whose panic is a MODE, not a type).
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians_Gestures.md — the B-6 frame §7.7 answers.
  - docs/DESIGN_Node_Roles_And_Policies.md — §3.5 (R-3015): every node receives the same event data (detonations included); it owns who receives, §7.3a here owns the cloud.
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
    armed hostile civilian uses; §5h adds the pick-up rifle/pistol and charge types, the laid-down flag and more stations per person.
  - docs/DESIGN_Weapon_Layers.md — owns the weapon brain, WeaponChoice and W-13 weapon posture; §5h's ladder reads that posture and adds the hands mount.
  - docs/DESIGN_Entity_Interactions.md — §7.4/§7.5 own the carry stations (R-2009, R-2017) that §5h extends to Hands / Back / Hidden.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — the backend frame for §5h.
  - docs/DESIGN_Weapon_Layers.md — owns the weapon brain, weapon categories and the grenade throw (W-12e/f) that CV-6's
    non-lethal munitions and CV-5's ThrowAt reuse.
  - docs/DESIGN_Decision_Layer.md — §4.4 owns Roe (R-200); its "later" row (WeaponsTight needs identification) is what CV-8
    builds as an escalation-of-force ladder.
  - docs/DESIGN_Sensors_And_Doctrine.md — §9.4 perception budget (~500 units) that bounds CV-10; §11.2 G11 identification; §7.8a "armed = 1", which §5h AW-1 refines (one Armament rule).
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
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_CivilianHsm_Landed.md — CE-2235 landed; B-4 (the perf harness) may go.
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
| **R-2029** | ✅ **AW-24** (§5h) — the simple path stays simple: one status lookup for combatants, the conduct path never entered without civilians (a counter rail), the pistol and the hands mount bounded, appearance defaulted from the weapon category |
| **R-2028** | ✅ **AW-22, AW-23** (§5h) — a lethal hostile act makes a civilian a combatant, known per unit (the witnesses and their squad); a player who kills his friends is flipped Hostile and engaged as an enemy combatant, the instructor told |
| **R-2027** | ✅ **AW-21** (§5h) — status for combatants (an enemy combatant is engaged as today, unless out of the fight), conduct (AW-15…AW-20) for civilians of any side and the neutral or unknown |
| **R-2026** | ✅ **the armed-people review, AW-15…AW-20** (§5h) — armed judged per observer by appearance and identification range; drawing is not a hostile act, aiming and firing at the forces are; direction of fire; self-defence over the force level; lethality by reach (knives, Molotovs, vehicles); a neutral in the line holds the shot. AW-1 and AW-3 superseded |
| **R-2025** | ✅ **armed people AW-1…AW-14** (§5h) — armed = a visible usable lethal weapon; issued weapons built-in (a pistol too), handed-over ones entities; hands, carry stations with a hidden one, held items; stones and canisters picked up and thrown; posture grades the ladder; insurgents and charges |

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
  Cower --> Alarmed : fear falls below panic
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
lone frightened civilian never attacks. ⚠ **As built (`CE-2236`, §5e):** Cower's exit is **Alarmed** once fear falls below
`PanicAt` — ⛔ SUPERSEDED: *"Cower → Panic : fire stops"*, which re-asks the shelter query that just answered none and loops
Panic ⇄ Cower while he is still frightened.

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
| **CV-4** | attitude and side | hidden attitude in `CivilianState` (brain only); a **hostile act** (throw, melee, fire, ~~draw a weapon~~ — ⭐ *amended 2026-10-17, R-2026 §5h AW-16: drawing makes him armed, intent unknown; the act is aiming at or firing at the forces, attacking, a vehicle driven at them*) stamps replicated `HostileAct` and the creator (CGF, owner of `EntityInfo`) flips `ForceId` Neutral → Hostile — sticky for the exercise in slice 1; ⭐ *(behaviors, 2026-10-16)* **the flip goes through ONE engine write path that also marks `dtEntityInfo` dirty** (§1: otherwise only CGF sees it), pinned by a two-node rail; an armed hostile civilian uses separable weapons (`PickUp`, `FireWhileCarried`) | flipping on attitude alone: omniscient detection (D4) · building G11 perceived-side first: large and not needed for this | `EntityInfo` writer on CGF; map symbol letter may stay stale (known limit) |
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
| **CIV-1** `CE-2235` | **`ua-civ-routine-alarm`** | test-town (⚠ 0–400 m; keep off the Pond) · 6 civilians of the new types (1101–1104) at their homes; a rifleman fighting a Hostile, two civilians 35–40 m from him, four 150+ m from both shooters | ① all 6 start in `Routine` (from the type's default), and the far ones walk · ② within 2.5 s of the first shot both near ones are `Alarmed`, no far one ever is (sampled) · ③ after the quiet time each `Alarmed` returns to `Routine` · ④ one wounded (Health set to 40 on CGF, the owner) ⇒ `Casualty`, stops moving, and stays there | ✅ **live 11/11 `2026-10-17`** (alarmed 1.7 s after the first shot; 4/4 far ones walked) · run 1 found its own cast off the navmesh — test-town is 0–400 m, two stood in the Pond (fixed) · twin `PostureScenarioTests.CE2235_CivRoutineAlarm_…` |
| CIV-1 | **`ua-civ-crowd-300`** *(perf gate, CV-10)* | test-town · 300 civilians, mixed variants, routine points spread over the town | ① all 300 run a behaviour · ② over 60 s, CGF frame time p95 ≤ the budget backend's `scripts/perf-civilians.sh` measures against (B-4) · ③ brain tick per civilian reported | ⏳ CE-2235 + B-4 |
| **CIV-2** `CE-2236` | **`ua-civ-shelter`** | bt-range · 4 civilians near House A (doors closed, not locked) and House B (locked); a burst fired 30 m away; hide time set short (20 s) for the test | ① each enters `Panic` · ② each reaches a point INSIDE House A's footprint (InsideTest), its door Open on every node · ③ none enters House B · ④ ~20 s after entering, each is gone on CGF, SimHost and IG | ✅ **live 15/15 `2026-10-17`** (re-run after the check's fix: all 4 in through A's front door, hid 19.3–21.0 s, gone on CGF, SimHost and IG) · ⛔ SUPERSEDED: 14/15, the check's 0.25 s poll missing a few-frame `Panic` · twin `PostureScenarioTests.CE2236_CivShelter_…` ✅ (samples every frame, so it asserts `Panic` itself; door Open on all three nodes) |
| CIV-2 | **`ua-civ-cower`** | open ground (no building in reach) · 3 civilians; sustained fire nearby | ① each enters `Cower`, `EffectState.Cowering` true on every node, speed ≈ 0 · ② the fire stops ⇒ `Cowering` false and each leaves `Cower` | ✅ live 11/11 `2026-10-17` · twin `PostureScenarioTests.CE2236_CivCower_…` ✅ (also asserts each passed through `Panic` and left Cower for Alarmed, never back to Panic) |
| **CIV-3** `CE-2237` | **`ua-civ-gathering`** | test-town · a hurt civilian (health 40) at the crossroads (200, 200); 12 calm civilians 30–60 m up the four streets | ⓪ the hurt one is a `Casualty` · ① every onlooker enters `Gather` · ② their mean distance to him falls below 15 m within 60 s · ③ one crowd holds ≥ 8 of them · ④ every one arrives and stands on a ring 2.5–11 m round him | ⏳ live run 1: 5/6 — ② at 25 s; ④ was measured while the 60 m ones still walked (check fixed: waits for all to arrive) · twin `PostureScenarioTests.CE2237_CivGathering_…` ✅ (12/12 Gather, 4.0–11.0 m, one crowd, mean < 15 m at 24.5 s) |
| CIV-3 | **`ua-civ-contagion`** | test-town · 15 civilians 8 m apart along the street, 30–142 m from a firefight (a rifleman and a hostile) at its west end · ⚠ *deviation:* small-arms fire, not a blast — no scenario-scriptable blast exists; the hearing edge (100 m) splits the crowd the same way | ⓪ ONE crowd · ① ≥ 5 hear it (`HeardAt`) · ⓪ ≥ 4 never hear anything · ② ≥ 3 of those who never heard panic, from the crowd | ✅ live 3 of 3 in a row after `CE-2246` (§5f: the crowd split by wandering; the demo's civilians wander 3 m) · twin `PostureScenarioTests.CE2237_CivContagion_…` ✅ (samples `HeardAt` and `Panic` every frame) |
| **CIV-4** `CE-2238` | **`ua-civ-riot-stone`** | test-town · a protest crowd with an instigator, facing a soldier line (Friend, FireAtWill) | ① the crowd reaches `Riot` · ② a rioter's `ThrowAt` hits the line · ③ THAT rioter is Hostile on CGF, SimHost and IG within 2 s; all others stay Neutral · ④ no soldier hits a Neutral rioter, sampled all run | ⏳ |
| CIV-4 | **`ua-civ-melee`** | test-town · a rioter next to a policeman | ① a `MeleeAttack` knocks the policeman down (`EffectState.KnockedDown`) · ② the rioter is Hostile on every node | ⏳ |
| **CIV-5** `CE-2239` | **`ua-civ-police-line`** | test-town · a police squad (shields, batons, baton-round launcher, stun grenades; `ForceLevel = NonLethal`) against a riot | ① police fire ONLY baton rounds / stun grenades / batons — zero lethal rounds counted · ② ≥ 1 rioter knocked down or stunned · ③ the riot breaks up (`Dispersing`/`Panic`, the crowd's members leave) · ④ no death | ⏳ |
| CIV-5 | **`ua-civ-escalation`** | test-town · the police line; one rioter armed with a rifle who fires | ① lethal fire goes ONLY at the armed rioter · ② the unarmed hostile rioters still receive non-lethal only · ③ with `ForceLevel = Warn`, no round at all — the warning cue only | ⏳ |
| **CIV-3** `CE-2240` | **`ua-civ-protest`** | test-town · 12 civilians told to protest at (224, 265), the street in front of the building at 230–290 × 230–300; NO security forces | ① every one reaches `Protest`, standing as one crowd (≥ 10) · ② on every node (CGF, SimHost, IG) members show `FistRaised` and `HoldSign` gestures (≥ 3 of each over the run) · ③ nobody turns Hostile and nobody enters `Riot` in 120 s — sampled all run | ⏳ · twin `PostureScenarioTests.CE2240_CivProtest_…` |
| **CIV-5** `CE-2241` | **`ua-civ-provocation`** | test-town · the same protest; a police line (SecurityForce, NonLethal) that advances to 15 m, then uses batons on the front rank; a FIXED seed | ① before the police come, no riot · ② after the batons, ≥ 1 and < all members enter `Riot` (a fraction, not the whole crowd) · ③ the rioters are Hostile on every node, the rest stay Neutral · ④ the same members riot on every run (the seed) | ⏳ |
| **CIV-5** `CE-2241` | **`ua-civ-surrender`** | test-town · a rifleman (Friend); a Hostile armed rioter who drops his weapon and raises his hands mid-fight | ① he is engaged while armed and fighting · ② from the frame his `Gesture` is HandsUp, no round is fired at him for 10 s, his side stays Hostile · ③ hands down + armed again ⇒ he is engaged again | ⏳ |
| **ARMED** §5h AW-21 · needs B-7 | **`ua-armed-status`** | test-town · a rifleman (Friend) vs an enemy rifleman AT EASE, 300 m, at night | ⓪ he is remembered · ① he is engaged · ② his danger and the ranking are identical to today's `ua-threat-ranking` — ⭐ the GUARD RAIL: green from the first armed-people commit on (the military fight is untouched) | ⏳ |
| ARMED AW-15 · B-7 | **`ua-armed-identify`** | test-town · 3 civilians holding a phone, a pistol, a toy gun, each at 20 m and at 150 m; two observers, one with binoculars | ① at 20 m the pistol and the toy gun are identified lethal · ② at 150 m both are unidentified without binoculars and identified with them · ③ the phone is never lethal, at any range | ⏳ |
| ARMED AW-16 · CE-2239 | **`ua-armed-draw-aim`** | test-town · a covert insurgent (Civilian, a pistol Hidden) near a patrol; he draws, waits 10 s, then aims | ① after the draw: a warning, side still Neutral, no round (sampled) · ② after the aim: side Hostile on every node and he is engaged | ⏳ |
| ARMED AW-17 · CE-2239 | **`ua-armed-celebration`** | test-town · a wedding crowd; one man fires into the air, later at the patrol | ① fire into the air: a warning, no lethal round (sampled) · ② fire at the patrol: lethal | ⏳ |
| ARMED AW-18 · CE-2239 | **`ua-armed-self-defence`** | test-town · a NonLethal police line; one rioter aims a rifle at it | ① lethal fire at THAT rioter only · ② every other rioter still gets non-lethal force only | ⏳ |
| ARMED AW-19 · CE-2239 | **`ua-armed-knife`** | test-town · a man with a knife closing on a checkpoint from 15 m; a second throws a Molotov | ① a warning while he is beyond reach · ② lethal once he is within reach or closing fast · ③ the Molotov thrower reads lethal | ⏳ |
| ARMED AW-20 · CE-2239 | **`ua-armed-shield`** | test-town · a rifleman 60 m from an insurgent who walks east along the line, then 20 m out of it; six civilians standing in one row 0.45 m BESIDE the line (outside a 0.3 m body, inside the rifle's spread) — ⚠ *as built:* not "from inside a crowd": a civilian ON the line hides the target (no aimed shot is ever ready), and two rows are pushed ~1 m apart by pedestrian spacing | ① the rifleman holds ('hold: neutral on line') while a civilian is in the spread · ② he fires once clear and the insurgent goes down (out of the fight) · ③ zero neutral casualties (sampled) | ✅ live 5/5 · twin `PostureScenarioTests.AW20_ArmedShield_…` ✅ (every frame: held 2870 frames, no round left with a body on the line) |
| ARMED AW-5, CV-13a · B-10, CE-2241 | **`ua-armed-surrender`** | test-town · an enemy soldier lays his weapon down and raises his hands; later re-arms and aims | ① no round while laid down (sampled) · ② engaged again once armed and aiming | ⏳ |
| ARMED AW-2, AW-21 · B-7, B-8 | **`ua-armed-stones`** | test-town · stone-throwers (Hostile civilians) against a police line | ① non-lethal force only — zero lethal rounds counted · ② the throwers read unarmed on every node | ⏳ |
| ARMED AW-22 · CE-2238, B-16 | **`ua-armed-blend-back`** | test-town · a civilian fires at patrol A, hides his rifle, walks into a crowd; patrol B (another squad) arrives later | ① patrol A engages him on status when his line is clear · ② patrol B treats him as a civilian (warns, never fires first) | ⏳ |
| ARMED AW-8, AW-9 · B-13, B-14 | **`ua-armed-gas-throwback`** | test-town · police gas a riot; a rioter picks the canister up and throws it back | ① the cloud stays where it was picked up and drifts · ② a new cloud rises where it lands · ③ the rioter is irritated while he holds it | ⏳ |
| ARMED AW-23 · B-15, CE-2238 | **`ua-player-fratricide`** | test-town · a first-person player in a squad shoots two friends; a second run, one accidental wound | ① flipped Hostile on every node · ② the friendly AI engage him · ③ the instructor event names both victims · ④ the one-wound run flags nothing | ⏳ |
| ARMED AW-12 · B-11, CE-2238 | **`ua-armed-bomber`** | test-town · a civilian with a Hidden charge walks into a crowd and sets it off | ① never armed to any observer before the blast (sampled) · ② the casualties match the warhead's numbers | ⏳ |
| CIV-6 *(backend)* · CIV-7 *(coordinator)* | named by their owners | — | — | — |

⚠ **Timing is never a frame count in a live check** — the cluster boots paused and each node ticks on its own clock; every wait is
`wait_for(condition, timeout)` with the condition printed on failure, and every "never" is SAMPLED over the whole run (a minimum, not
an end state).

## 5d. CIV-1 behaviour — `CivilianHsm` slice 1: Routine, Alarmed, Casualty *(behaviors, `CE-2235`, 2026-10-17)*

INVENTORY *(codebase-memory CLI `search_graph` + grep, one Explore sweep; `check_index_coverage` not available through the CLI)*: hand-built
HSMs are `[HsmDefinition(Curated)]` graphs (`CuratedMachines.cs`, `ClassOrderBehaviors.cs`) bound through `HsmFluentBindingExtensions`;
hearing reaches a brain as `SoundContactEvent` → `TargetMemory.HearContact` (`ThreatEvaluationSystem.cs:69`, `PerceptionComponents.cs:282`)
— ⚠ a sound near an IDENTIFIED contact only refreshes that slot, so memory cannot say "heard now"; moves are `LocomotionMoveTo.Issue/Stop`,
stances `StanceRequest.Set`, wounds `Injury.StateOf` (fraction below the type's `WoundedBelow`, 0.6); no civilian AI, no fear component
existed (§1).

```mermaid
classDiagram
  direction LR
  class CivilianState { <<NEW, Fdp.Toolkits, brain-local, id 399>> Fear Anger Attitude Mode Home LastFrightAt }
  class CivilianMind { <<NEW>> Ensure SetMode FearOf ModeOf }
  class CivilianFearSystem { <<NEW, CGF Simulation>> heard sounds + near miss/hit raise fear, then the fade }
  class CivilianFear { <<NEW>> Impulse(class, distance) DecayPerSecond }
  class CivilianBehavior { <<NEW, Hrot.AI.Behaviors>> CivilianHsm graph, Resolve, Routine Alarmed Casualty, guards }
  class CivilianParamsJsonDto { <<NEW, Hrot.Core>> WanderRadius WalkSpeed Pause AlarmAt CalmBelow }
  class CivilianTkbCatalog { <<existing, backend>> 1101-1104 DefaultBehaviorHash = CivilianHsm }
  class SoundContactEvent { <<existing>> Observer X Y SourceClass }
  class SensorChangedEvent { <<existing>> NearMiss Hit }
  class LocomotionMoveTo { <<existing>> Issue Stop Status }
  class StanceRequest { <<existing>> Set }
  class Injury { <<existing>> StateOf }
  class SimRng { <<existing>> FromSim }
  CivilianFearSystem ..> SoundContactEvent : reads, grouped by listener
  CivilianFearSystem ..> SensorChangedEvent : reads
  CivilianFearSystem ..> CivilianFear
  CivilianFearSystem --> CivilianState : writes Fear
  CivilianBehavior ..> CivilianMind : Ensure, SetMode, FearOf
  CivilianMind --> CivilianState
  CivilianBehavior ..> LocomotionMoveTo
  CivilianBehavior ..> StanceRequest
  CivilianBehavior ..> Injury : Wounded guard
  CivilianBehavior ..> SimRng : wander spot, linger time
  CivilianBehavior ..> CivilianParamsJsonDto : resolver
  CivilianTkbCatalog ..> CivilianBehavior : starts it by default
```
*What the picture shows that prose hid:* fear has ONE writer (the system) and the HSM only READS it — so the crowd's contagion
(`CE-2237`) is a second writer of the same field, not a second fear model; and the mode is written by the HSM's own state, so a reader
that is not the HSM never needs its metadata.

```mermaid
stateDiagram-v2
  [*] --> Routine
  Routine --> Alarmed : Fear >= AlarmAt (0.3)
  Alarmed --> Routine : Fear < CalmBelow (0.1)
  Routine --> Casualty : wounded (priority)
  Alarmed --> Casualty : wounded (priority)
```
*What the picture shows that prose hid:* the slice-1 subset of §2.2 — Casualty has no exit, and the wounded edge outranks the fear edge
from both states, so a frightened wounded man lies down instead of crouching.

```mermaid
sequenceDiagram
  participant SH as SimHost (shooter)
  participant SN as CGF hearing sensor
  participant FS as CivilianFearSystem
  participant CS as CivilianState
  participant HSM as CivilianHsm (BrainTickSystem)
  SH-->>SN: weapon fire, a SmallArms sound
  SN->>FS: SoundContactEvent (Observer = the civilian)
  FS->>CS: Fear += Impulse(class, distance)
  HSM->>CS: polled guard reads Fear >= AlarmAt
  HSM->>HSM: Routine exit (stop), Alarmed (crouch)
  loop every frame, nothing new heard
    FS->>CS: Fear -= 0.05 per s
  end
  HSM->>HSM: Fear < CalmBelow, Alarmed exit (stand), Routine (walk on)
```
*What the picture shows that prose hid:* the alarm's length is the FADE, not a timer in the machine — a burst that frightened him more
keeps him down longer, and a second shot while he is alarmed extends it without any transition.

```mermaid
graph TD
  subgraph CGF["CGF (brain node)"]
    TE[CgfThreatEvaluationSystem] --> FS[CivilianFearSystem]
    FS --> BT[BrainTickSystem]
    BT --> HSM[CivilianHsm per civilian]
    TKB[BehaviorTkbTranslator] -->|DefaultBehaviorHash| HSM
    REG[CognitiveComponentRegistry] -.registers.- CS[CivilianState]
  end
  subgraph SimHost
    MV[CarKinematicsSystem HumanGait] 
  end
  HSM -->|LocomotionChannel MoveTo| MV
  subgraph Other["SimHost, IG, maps"]
    NONE[CivilianState absent: brain-local]
  end
  style NONE fill:#f66,color:#000
```
*What the picture shows that prose hid:* `CgfLogicPack` puts `CivilianFearSystem` in its Simulation list right after threat evaluation
and before the cognitive runtime (the brain tick), so a fright is read the same frame; the red box — no other node has `CivilianState`,
so anything another node needs (the cower pose, the side) travels in its own replicated component, never here.

| decision | ⭐ as built | rejected — one line each |
|---|---|---|
| who integrates fear | ⭐ one CGF system, one pass over the frame's `SoundContactEvent`s | a node in the HSM: civilians × events per frame (300 × a burst's samples) · reading `TargetMemory`: it cannot tell "heard now" for an identified contact (§5d inventory) |
| where fear lives | ⭐ `CivilianState` (id 399), brain-local, registered with the cognitive components, never saved | in the HSM block: the crowd (CE-2237) and a live check could not read it |
| who adds it | ⭐ the HSM, on its first tick (`CivilianMind.Ensure`, home = where he stands) | the TKB translator: any person may be given `CivilianHsm`, and only a running one needs a mind |
| the mode | ⭐ each state's activity writes `CivilianState.Mode` | the HSM trace's leaf names: the trace must be armed per entity and the leaves are ids |
| wounded | ⭐ `Injury.StateOf != Ok` (the type's thresholds: below 60 %) | "health below max": a scratch would make a casualty |
| the routine | ⭐ wander about home (radius 12 m, 1.4 m/s, linger 3–10 s, `SimRng` per man and leg) | authored routine points: no demo needs them yet — a later round-out |

⚠ **Known limits (slice 1):** ① *seeing* a casualty does not frighten (§2.2's "saw a casualty") — a Neutral observer drops Neutral
targets in vision (`VisionBroadphase.cs:79`), so the crowd hint (`CE-2237`) carries it. ② Fear's impulses and fade are constants of
`CivilianFear`, the same for every man — attitude-scaled nerve comes with `CE-2238`. ③ A casualty stays one; a casualty's rescue is not
civilian behaviour.

## 5e. CIV-2 behaviour — Panic, Shelter, Inside, Cower *(behaviors, `CE-2236`, 2026-10-17)*

INVENTORY *(codebase-memory CLI `search_graph` + grep; `check_index_coverage` not available through the CLI)*: EQS templates are
`[EqsTemplate]` static classes (`VehicleTemplates.cs` — ⚠ the existing **`FindShelter`** finds OVERHEAD cover on a grid for the drone
shelter, V8b: it does not know doors or footprints, so it cannot answer "a house I can walk into"); point generators derive from
`PointPatternGenerator` (`PointPatternGenerators.cs`); a behaviour owns a sensor through `PostureNodes.Keep/Drop`; doors are
`TerrainWorld.Doors`/`Panels` with live state in `DoorStates.Of` and the mover opens a closed door itself (`DoorPassageSystem`, §5a M-2);
a person is deleted on every node by `DestroyEntityCommand` (§5a M-1); cowering is `Effects.SetCowering` (backend F-3, §7).

```mermaid
classDiagram
  direction LR
  class CivilianBehavior { <<grows>> + Panic Shelter Inside Cower, guards Panicked BelowPanic ShelterFound NoShelter InsideNow ShelterFailed }
  class PanicState { <<NEW, in the block>> Sensor Since Answered Found Moving Arrived Failed Deleted Shelter HideUntil }
  class CivilianState { <<grows>> + FrightFrom HasFrightFrom }
  class CivilianFearSystem { <<grows>> records where the frightening sound came from }
  class FindEnterableShelter { <<NEW EQS template, Fdp.Toolkits>> }
  class EnterableDoorGenerator { <<NEW>> one point InsetMetres inside per exterior ground door not Locked }
  class InsideTest { <<NEW EQS filter>> IsInside(world, p) — a non-solid footprint }
  class NavmeshReachableTest { <<existing>> a Locked door is a wall }
  class PathCostScoreTest { <<existing>> }
  class PostureNodes { <<existing>> Keep Drop }
  class DoorStates { <<existing>> Of }
  class DoorPassageSystem { <<existing, SimHost>> opens a closed door on the path }
  class Effects { <<existing, backend F-3>> SetCowering }
  class DestroyEntityCommand { <<existing>> }
  CivilianBehavior --> PanicState
  CivilianBehavior ..> PostureNodes : Keep the shelter sensor in Panic
  PostureNodes ..> FindEnterableShelter
  FindEnterableShelter --> EnterableDoorGenerator
  FindEnterableShelter --> InsideTest
  FindEnterableShelter --> NavmeshReachableTest
  FindEnterableShelter --> PathCostScoreTest
  EnterableDoorGenerator ..> DoorStates : skips Locked
  CivilianBehavior ..> InsideTest : IsIndoors on the run in
  CivilianBehavior ..> Effects : Cower sets, its exit clears
  CivilianBehavior ..> DestroyEntityCommand : Inside, after the hide time
  CivilianFearSystem --> CivilianState : FrightFrom
  CivilianBehavior ..> CivilianState : Panic flees from FrightFrom
  DoorPassageSystem ..> DoorStates : Open
```
*What the picture shows that prose hid:* ONE new template of three new pieces, the rest reused — the door rule ("if the doors can be
opened") lives in TWO existing places and nowhere new: the generator skips a Locked door, and the reachability test treats it as a wall;
nothing in the HSM knows doors, and `InsideTest.IsInside` is the same question for the query and for the run in.

```mermaid
stateDiagram-v2
  [*] --> Routine
  Routine --> Alarmed : Fear >= AlarmAt (0.3)
  Alarmed --> Routine : Fear < CalmBelow (0.1)
  Alarmed --> Panic : Fear >= PanicAt (0.6)
  Panic --> Shelter : the query found a way in
  Panic --> Cower : none, or no answer in 3 s
  Shelter --> Inside : arrived, or inside the footprint
  Shelter --> Cower : the way failed
  Cower --> Alarmed : Fear < PanicAt
  Inside --> [*] : hide time over, deleted
  Routine --> Casualty : wounded (priority)
  Alarmed --> Casualty : wounded (priority)
  Panic --> Casualty : wounded (priority)
  Shelter --> Casualty : wounded (priority)
  Cower --> Casualty : wounded (priority)
```
*What the picture shows that prose hid:* every panic drains — into Inside (and deletion) or Cower, never open-ended running (R-4035);
Cower's exit is Alarmed, not Panic (§2.2 SUPERSEDED there): re-panicking would re-ask a query that just answered none; Inside alone has
no wounded edge, because a man inside is about to vanish and nothing outside can hurt him.

```mermaid
sequenceDiagram
  participant FS as CivilianFearSystem
  participant HSM as CivilianHsm (CGF)
  participant EQ as EQS FindEnterableShelter
  participant MV as SimHost mover
  participant D as DoorPassageSystem
  participant N as every node
  FS->>FS: heard shots, Fear past PanicAt, FrightFrom = the sound
  HSM->>HSM: Alarmed, then Panic: stand, flee 20 m from FrightFrom
  HSM->>EQ: Keep sensor (search 80 m)
  EQ-->>HSM: best point inside, reachable through a door not Locked
  HSM->>MV: MoveTo the point at 3.5 m/s (Shelter)
  MV->>D: a closed door on the path
  D-->>MV: Open
  MV-->>HSM: arrived (or IsIndoors)
  HSM->>HSM: Inside, hide time drawn in [HideMin, HideMax]
  HSM->>N: DestroyEntityCommand, once
```
*What the picture shows that prose hid:* the flee and the query run TOGETHER — he is already running away while EQS answers, and the
answer replaces the flee's move; a query that never answers (no terrain, a node without EQS) costs ShelterWaitSeconds and ends in
Cower, not a stuck Panic.

```mermaid
graph TD
  subgraph CGF["CGF (brain node)"]
    FS[CivilianFearSystem] --> BT[BrainTickSystem]
    BT --> HSM[CivilianHsm]
    HSM -->|Keep| EQS[EqsSolverSystem FindEnterableShelter]
    EQS -->|EqsCognitiveBuffer| HSM
    HSM -->|SetCowering| ES[EffectState]
    HSM -->|DestroyEntityCommand| SP[NetworkSpawning]
  end
  subgraph SimHost
    MV[CarKinematicsSystem + DoorPassageSystem]
    PR[EngineBackedPathResponseSystem]
  end
  HSM -->|NavigationIntent PathToPoint| MV
  PR -->|refuses a path for a stopped move| MV
  ES -->|dtEffectState| ALL[SimHost, IG, maps: Cower pose]
  SP -->|delete on the wire| ALL
```
*What the picture shows that prose hid:* the one cross-lane fix of this slice sits on SimHost — `EngineBackedPathResponseSystem` drove
EVERY reachable path answer, so a path planned for a move that was then STOPPED revived it (measured: a casualty crawling 2 m along
the walk he was shot out of, CIV-1's ④); it now refuses one whose intent is the explicit stop (`CE-2245`).

| decision | ⭐ as built | rejected — one line each |
|---|---|---|
| where he hides | ⭐ a NEW template `FindEnterableShelter`: a point 2 m inside each exterior ground-storey door not Locked, indoors, reachable, nearest | the existing `FindShelter`: overhead cover on a grid, blind to doors and footprints · the door CENTRE: on the threshold, half outside |
| the lock rule | ⭐ the generator skips Locked (`DoorStates`) and reachability treats it as a wall — both existing | a door check in the HSM: a third place for one rule |
| the run | ⭐ flee 20 m from `FrightFrom` while the query runs, then MoveTo the answer at 3.5 m/s, upright | wait still for the answer: a man frozen under fire · `FallBack`: a soldier's node, needs a threat contact he does not have |
| no answer | ⭐ Cower after `ShelterWaitSeconds` (3 s) | wait forever: no terrain ⇒ a stuck Panic |
| inside | ⭐ hide `[HideMin, HideMax]` (60–180 s, `SimRng`), then ONE `DestroyEntityCommand` | `PhysicalPresence.IsHidden`: keeps him alive for nothing (CV-7) |
| Cower's exit | ⭐ Alarmed once Fear < PanicAt | §2.2's Cower → Panic: re-asks a query that answered none, loops Panic ⇄ Cower |
| the stopped-move fix | ⭐ `EngineBackedPathResponseSystem` skips a path whose entity's intent is `None` (cross-lane, backend file; the handle stays registered) | correlate request ids: PlanRoute already asks with intent `None` and documents that it "does not start corridor-following" — the same rule fixes it |

⚠ **Known limits (slice 2):** ① only bt-range's houses are enterable (§6); on test-town every panic ends in Cower. ② The shelter is the
first answer — a door locked AFTER the answer makes the run fail and he cowers, he does not re-query. ③ `Inside` deletes him from
`SimulationTime`; a paused sim holds him inside. ④ Upper storeys are not candidates (ground floor only).

## 5f. CIV-3 behaviour — crowds: onlookers gather, fear spreads *(behaviors, `CE-2237`, 2026-10-17; build-state: READY-TO-BUILD)*

INVENTORY *(codebase-memory CLI, re-indexed 2026-10-17 after the backend/ui merges; `check_index_coverage` not available through the CLI)*:
`search_graph .*Crowd.* Class` = **18**, every one a dtCrowd MOVEMENT agent (`CrowdAgent`, `CrowdMotorIntent`, `IDtCrowdProvider`,
`CrowdAgentUpdateSystem`…, Stride-only and capped at 128, §2.5's red box) — none is a group of civilians; `.*(Gather|Onlook|Incident|Contagion|Mood).* Class`
= **0**. A brain-local entity that is never saved already exists as a pattern: `EqsChildSensor.Ensure` (`repo.CreateEntity` +
`DerivedParts.MarkNotSaved`, CE-3045). `CivilianState` already carries `Anger` and `Attitude` (unused), §2.1 already names a `CrowdId` on it.
Component id **400** is free on `behaviors`, `backend` and `ui` (grep of `GlobalComponentIds.cs` on all three, 2026-10-17).

⭐ **Scope** *(this slice)*: the crowd itself (membership, mood, focus), onlookers gathering at an incident, and fear spreading through a
crowd. ⛔ Moved out, each to the slice that first has a consumer: **Protest** and its gestures → `CE-2240`; **anger and instigators** (ASY-5)
→ `CE-2238` (anger has no reader before Riot); the security forces' provocation → `CE-2241`. `Crowd.MeanAnger` is computed now so those
slices only add producers.

```mermaid
classDiagram
  direction LR
  class Crowd { <<NEW component, Fdp.Toolkits/Civilians, id 400, brain-local, never saved>> Id Count Centroid MeanFear MeanAnger Focus HasFocus FormedAt }
  class CrowdSystem { <<NEW, CGF Simulation>> incidents, attraction, membership, mood, contagion }
  class CrowdRules { <<NEW static>> LinkMetres MinMembers AttractMetres RingMetres ContagionMetres ContagionShare ContagionRate }
  class CivilianState { <<grows>> + CrowdId Focus HasFocus HeardAt }
  class CivilianMode { <<grows>> + Gather = 12 }
  class CivilianBehavior { <<grows>> + Gather state, guards Attracted NotAttracted }
  class GatherState { <<NEW, in the block>> Moving Arrived Target FocusAt }
  class CivilianFearSystem { <<grows>> stamps HeardAt }
  class Injury { <<existing>> StateOf }
  class DerivedParts { <<existing>> MarkNotSaved }
  class LocomotionMoveTo { <<existing>> Issue Stop Status }
  CrowdSystem ..> CrowdRules
  CrowdSystem ..> Injury : an incident is a person not Ok
  CrowdSystem --> CivilianState : writes CrowdId, Focus, Fear (contagion)
  CrowdSystem --> Crowd : creates, updates, destroys
  CrowdSystem ..> DerivedParts : a crowd entity is never saved
  CivilianBehavior --> GatherState
  CivilianBehavior ..> CivilianState : reads Focus
  CivilianBehavior ..> LocomotionMoveTo : walks to a ring point
  CivilianFearSystem --> CivilianState : HeardAt
```
*What the picture shows that prose hid:* fear still has ONE model — contagion is a second WRITER of the same `Fear` field (§5d's caption
predicted it), so the HSM's thresholds, the fade and the panic path are untouched; and the crowd is DERIVED every frame from where the
civilians stand, so a crowd entity is a read-out with an id, never a second place membership is decided.

```mermaid
stateDiagram-v2
  [*] --> Routine
  Routine --> Gather : an incident within AttractMetres (Focus set)
  Gather --> Routine : the focus is gone
  Gather --> Alarmed : Fear >= AlarmAt
  Routine --> Alarmed : Fear >= AlarmAt
  Gather --> Casualty : wounded (priority)
```
*What the picture shows that prose hid:* gathering is entered only from Routine — a frightened man never walks TOWARDS an incident — and
fear outranks curiosity from both states; the rest of §5e's machine is unchanged.

```mermaid
sequenceDiagram
  participant CS as CrowdSystem (CGF)
  participant M as a calm civilian's CivilianState
  participant HSM as his CivilianHsm
  participant MV as SimHost mover
  CS->>CS: incidents = persons whose Injury is not Ok
  CS->>M: Focus = the nearest incident within 80 m (Routine or Gather only)
  HSM->>M: polled guard reads HasFocus
  HSM->>HSM: Routine exit (stop), Gather
  HSM->>MV: MoveTo a point on a ring 4 to 9 m round the focus, walking
  loop every frame
    CS->>CS: link civilians within LinkMetres, components of MinMembers or more
    CS->>M: CrowdId of his component (kept from the last frame by majority)
    CS->>CS: Crowd entity per id: centroid, mean fear and anger, focus
  end
```
*What the picture shows that prose hid:* nobody tells a civilian to join a crowd — each walks to the SAME focus, and walking there is what
makes them one crowd; the id survives because a component inherits the id most of its members already had.

```mermaid
sequenceDiagram
  participant FS as CivilianFearSystem
  participant CS as CrowdSystem
  participant A as a member who heard the fire
  participant B as a member 110 m off, beyond hearing
  FS->>A: Fear up (heard), HeardAt
  A->>A: Alarmed, then Panic
  CS->>B: same crowd, within ContagionMetres of a frightened member
  CS->>B: Fear rises toward 0.9 x that member's fear, at a rate falling with distance, FrightFrom copied
  B->>B: Panic, flees from where the others flee from
```
*What the picture shows that prose hid:* contagion can never RAISE a crowd's peak fear — each hop carries at most 0.9 of its source — so a
wave dies out after a few hops and two frightened neighbours cannot hold each other frightened forever (each fades below the other).

```mermaid
graph TD
  subgraph CGF["CGF (brain node)"]
    TE[CgfThreatEvaluationSystem] --> FS[CivilianFearSystem]
    FS --> CS[CrowdSystem]
    CS --> BT[BrainTickSystem]
    BT --> HSM[CivilianHsm per civilian]
    CS -.creates.-> CR[Crowd entities, local, never saved]
  end
  subgraph Other["SimHost, IG, maps"]
    NONE[no Crowd: brain-local; U-5's overlay needs a descriptor first]
  end
  style NONE fill:#f66,color:#000
```
*What the picture shows that prose hid:* `CgfLogicPack` runs the crowd after the fear system and before the brain tick, so a contagion step
and the guard that reads it land in the same frame; the red box — no other node holds a crowd, so ui's optional overlay (U-5) needs a
replicated descriptor first, a backend item that does not exist yet.

| decision | ⭐ lean (this lane's detail under CV-5) | rejected — one line each |
|---|---|---|
| membership | ⭐ connected components of civilians within `LinkMetres` (15 m), ≥ `MinMembers` (3), recomputed every frame (300 civilians ≈ 45 000 pair checks) | a crowd assigned by the scenario: onlookers form crowds nobody authored · `UnitRoster`: 16 members, and a crowd is uncommanded (T3) |
| identity | ⭐ a component keeps the id most of its members had last frame; a new id otherwise; an empty id's entity is destroyed | a fresh id every frame: a reader could not follow a crowd |
| the crowd entity | ⭐ CGF-local `repo.CreateEntity` + `Crowd` + `DerivedParts.MarkNotSaved`, no `NetworkIdentity` — the `EqsChildSensor` pattern | a replicated entity: no reader on another node yet (U-5 is optional, LATER) · a singleton table: CV-5 rules an entity |
| what draws onlookers | ⭐ a PERSON whose `Injury.StateOf` is not Ok within `AttractMetres` (80 m), read from ground truth on CGF — CV-2's "crowd awareness comes from the CrowdSystem" | per-person vision: a Neutral observer drops Neutral targets (`VisionBroadphase.cs:79`) and costs CV-2's budget |
| how they gather | ⭐ walk to a point on a ring 4–9 m round the focus, on their own bearing (`SimRng` per man) and stand | everyone to the focus point: they stack on the casualty |
| contagion | ⭐ from a same-crowd member within `ContagionMetres` (20 m) showing fear (Alarmed, Panic, Shelter, Cower): `Fear += ContagionRate·(1 − ½·d/R)·dt·(0.9·Fear_src − Fear)` when positive; `FrightFrom` copied from the strongest source | an impulse per sighting: two cowering neighbours re-frighten each other forever · the crowd's mean: one panicking man in 300 changes nothing, a wave never starts |
| how a check tells hearing from contagion | ⭐ `CivilianState.HeardAt` — the fear system stamps the last time he HEARD violence | a contagion counter: says nothing about whether he also heard it |

⭐ **Demos** (§5c): `ua-civ-gathering` and `ua-civ-contagion`, each with a live check and a twin. ⚠ **Known limits:** ① incidents are ground
truth within 80 m — no line of sight; ② a crowd is one CGF's civilians (two CGF nodes ⇒ two crowds where one stands); ③ an onlooker does
not turn to face the focus (no facing request exists for a standing person); ④ an onlooker stays as long as the incident does — a casualty
is never rescued (§5d ③), so the crowd stays; ⑤ *(`CE-2246`, measured)* **a crowd held together only by its 15 m link breaks as its members
wander, and contagion stops at the break** — see below.

⭐ **`CE-2246` — why contagion reached the crowd in-process but not live** *(behaviors, 2026-10-17, measured)*. A per-second live trace
(fear, mode, crowd, x of all 15) showed it: at t ≈ 12 s civilian 11 had wandered 17 m from civilian 10, past `LinkMetres`, so 11–15 were
a SECOND crowd while 10 cowered at fear 0.84; they re-linked at t ≈ 20 s, by which time the wave had faded (10 → 0.84, 11 → 0.67, 12 → 0.59,
just under `PanicAt`). At 8 m spacing, two neighbours on the default 12 m wander drift up to 32 m apart, so the line held only by luck —
the twin was lucky, the live run not. The candidates the row named first (a shorter fight, the check's poll) were measured too: the
hearers peaked at 0.97–1.00 live as in-process, and the poll was ~4 s a sweep (3 HTTP calls per civilian), fixed but not the cause.
⭐ **Fix — in the DEMO, not the rule:** the scenario gives its 15 civilians `WanderRadius` 3 (`CivilianHsm` override), so neighbours stay
within 8 + 2 × 3 = 14 m < 15 m — a crowd standing in the street, which is what the demo means. ⛔ The rule is unchanged: contagion within
one crowd is CV-5's, and a crowd that walks apart SHOULD stop passing fear. The check now sweeps all 15 under one perspective switch
(~1.7 s) and prints the same per-second trace as the twin. Live after the fix: PASS 3 of 3 in a row (4 of 6 who heard nothing caught it in the first).

## 5g. CIV-3 behaviour — the protest and its gestures *(behaviors, `CE-2240`, 2026-10-17; build-state: READY-TO-BUILD)*

INVENTORY *(codebase-memory CLI + grep, 2026-10-17)*: backend's B-6 is built (§7.7: `Gesture` 398, `Gestures.Set/Of`, `dtGesture` 143,
Brain group, so the behaviour on CGF owns it); ui's U-6 is built (`CE-1166`: `EntityBodyLayer3D.Gestured` draws `Gestures.Of` on every map
host, the placard for `HoldSign`). A params DTO carries a point as a `Vector3` (`CombatPostureParamsJsonDto.Objective`). ⛔ No facing
request exists for a standing person (grep `Facing|LookAt|TurnTo` over the behaviour layer: none).

```mermaid
classDiagram
  direction LR
  class CivilianBehavior { <<grows>> + Protest state, guards Protesting }
  class ProtestState { <<NEW, in the block>> Moving NextMillAt Spot NextGestureAt Sign }
  class CivilianParams { <<grows>> + ProtestAt HasProtest }
  class CivilianParamsJsonDto { <<grows>> + ProtestAt (Vector3), Protest (bool) }
  class Gestures { <<existing, backend B-6>> Set Of }
  class LocomotionMoveTo { <<existing>> Issue Stop Status }
  class CrowdSystem { <<existing, CE-2237>> the protesters ringing one point are one crowd }
  CivilianBehavior --> ProtestState
  CivilianBehavior ..> CivilianParams : ProtestAt
  CivilianBehavior ..> Gestures : writes FistRaised, HoldSign, None on exit
  CivilianBehavior ..> LocomotionMoveTo : to a spot on the protest ring, then milling
```
*What the picture shows that prose hid:* a protest is a PARAMETER of the one civilian machine (where to protest), not a new type or a
crowd order — and the crowd it forms is the same derived crowd as onlookers', so anger and provocation (`CE-2238`, `CE-2241`) attach to
a crowd that already exists.

```mermaid
stateDiagram-v2
  [*] --> Routine
  Routine --> Protest : he has a protest point
  Protest --> Panic : Fear >= PanicAt
  Protest --> Casualty : wounded (priority)
```
*What the picture shows that prose hid:* a protester skips Alarmed — a shot alone does not send a committed protester home, only fear past
PANIC does (§2.2's "Protest → Panic : lethal fire nearby"); Riot (`CE-2238`) is the next edge out of Protest.

```mermaid
sequenceDiagram
  participant HSM as CivilianHsm (CGF)
  participant MV as SimHost mover
  participant G as Gesture (dtGesture)
  participant N as every map host
  HSM->>MV: MoveTo his spot on the protest ring (8 to 15 m out), walking
  loop every 8 to 15 s
    HSM->>MV: a step to a new spot within 3 m, slowly
  end
  alt a sign carrier (40 per cent, per man)
    HSM->>G: HoldSign, the whole protest
  else
    loop
      HSM->>G: FistRaised for 3 to 6 s, then None for 2 to 5 s
    end
  end
  G-->>N: dtGesture, drawn as arms and a placard
  HSM->>G: None, on leaving Protest
```
*What the picture shows that prose hid:* the brain is the gesture's only writer and every map just draws the replicated byte, so "a
protest shows its gestures on every map" needs no new wire; and leaving the state clears it, so a panicking protester never runs with
his fist up.

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| who protests | ⭐ a civilian whose params name a point (`Protest: true`, `ProtestAt`) — scenario-authored, the same CivilianHsm | a protester TYPE: re-create on every mood change (CV-1) · a crowd order: a crowd is uncommanded (T3) |
| where he stands | ⭐ on a ring 8–15 m round the point, his own bearing (the onlookers' `RingPoint`, wider), stepping within 3 m every 8–15 s at 0.8 m/s | standing still: a frozen crowd reads as a picture, not a protest |
| his gesture | ⭐ 40 % carry a sign the whole time (`HoldSign`), the rest raise a fist 3–6 s then rest 2–5 s — per man, `SimRng` | everyone the same: a drilled unit, not a crowd |
| leaving | ⭐ `None` on exit; Panic only past `PanicAt` | Alarmed from Protest: one shot would end every protest |

⚠ **Known limits:** ① protesters do not FACE the point (no facing request for a standing man — §5f ③); ② anger stays 0 until `CE-2238`/`CE-2241`
add producers; ③ a protest point inside a solid building leaves its ring spots unreachable on that side — author it in the street.

## 5h. Armed people — what a man holds, what others can see *(behaviors, 2026-10-17; build-state: READY-TO-BUILD; ✅ RULED R-2025, amended R-2026, R-2027, R-2028, R-2029)*

🔒 **User, verbatim (`2026-10-17`, one exchange, each step approved in turn):**
- *"Both concepts sound reasonable. Just the condition for armed or unarmed need to be a little bit more complex."*
- *"picking the stones is likely very common way of getting a stone. Also picking a landed gas grenade and throwing it back should be possible."*
- *"Is there any threat level evaluated based on whether the weapon is drawn or just at ease?"* → *"Yes sounds good."*
- *"Is the bomb attached also considered a carried weapon? Bomb can be hidden."* → *"Approved."*
- *"The hand mount should be usable also for normal civilian stuff, not just weapons, like a mobile phone or maybe a binocular."* · *"we should not make it a separate entity, because there could be many soldiers who just carry it but never use it. The same like with their ordinary main weapon."* → *"approved, write it into the design"*
- After a critical review against real incidents (misidentification, legally armed civilians, celebratory fire, self-defence, knives and
  Molotovs, crowds as shields): *"approved, fold 1-6 into the design with your leans"* ⇒ **R-2026**, AW-15…AW-20 below; AW-1 and AW-3 are
  SUPERSEDED (their old text kept in their rows, struck through).
- On whether the complex rules are for civilians only (*"for soldiers and military is much simpler, because every enemy soldier who is
  armed, no matter where his weapon is drawn, unless he surrenders, needs to be considered hostile"*): *"approved, add AW-21 to the
  design"* ⇒ **R-2027**, AW-21.
- On how a civilian turns into a combatant (the lean: a lethal act, known per unit): *"Yes please."* And: *"in first-person shooter mode, if
  the player starts to kill his friends, he should be marked as enemy combatant and treated as such."* ⇒ **R-2028**, AW-22 and AW-23.
- *"I would like to prevent overcomplicated decisions where they are not necessary."* → *"yes, add AW-24 to the design"* ⇒ **R-2029**, AW-24.

INVENTORY *(codebase-memory MCP `search_graph` + grep, 2026-10-17; `check_index_coverage` not run)*:

| area | measured | where |
|---|---|---|
| "armed" today | ONE test: a `WeaponState` on the target or a controlled weapon system, a dispenser-only mount excepted; armed = danger 1, unarmed 0.3 | `ThreatDanger.cs:30-33`, `WeaponSystems.WeaponOf:182-186`, `Countermeasures.DispenserOnly` (5 readers) |
| who reads `WeaponState` as armed-ness | **27** production reads in 17 files (figure, utility inputs, squad inputs, threat danger…) | grep `HasComponent<WeaponState>` |
| issued weapons | BUILT-IN mounts, type data (rifle, RPG); several per type, the weapon brain ranks them per target | `UrbanCombatTkbCatalog.cs:203, 256-279` (Insurgent 2003: RPG + rifle), `WeaponChoice.Choose`; W-A, W-F |
| separable weapons | weapon-system ENTITIES 600–609 (HMGs, minigun, Javelin, NLAW, LAW, Kornet, TOW, sentry, mortar); one-man launchers fire while carried; stowed / deploying / deployed / packing | `TkbEntityTypes.cs:27-38`, `BdcTkbCatalog.cs:264`, `WeaponSystemComponents.cs:45`; R-2016 |
| picking up | the carry station takes a downed man or a portable weapon system — nothing else | `Embarkation.cs:121-160`, `CarrierDefDto.cs:43-47` (ONE station per person) |
| a thrown grenade on the ground | a time-fuzed round LANDS and lies there (`WarheadRound.Landed`) until its fuze runs out; grenades cross the wire as lightweight entities, simulated only on SimHost | `CombatComponents.cs:146-149`; R-271 (ui) |
| several rounds on one mount | `AmmoLoadout`: up to 4 munition types with counts, one loaded, a switch time | `AmmoLoadout.cs` (M8) |
| the gas cloud | BUILT (backend `CE-3219` part 3): a CGF-owned area entity 7202 that drifts on the wind, Irritant hits once a second | §7.3a part 3; R-3014 |
| weapon posture | at ease / ready / aimed + the aimed-at target + the active mount, weapon brain's, replicated `dtWeaponPosture` — read ONLY by the 3-D figure | `WeaponPosture.cs`; W-13, R-272 (backend) |
| the stone round | 6038 Blunt — ⚠ its own warhead without `Indirect`, so it would fly straight; 6037 (stun) the same | `MunitionTkbCatalog.cs:269-273` |
| civilians | no mount, no `CanShoot`; a hidden `Attitude` (unused) | `CivilianTkbCatalog.cs:62-65`, `CivilianState.Attitude` |
| ⛔ none exist | held items (phone, binoculars), a pistol, a hands mount, round pick-up, stone sources, explosive charges, surrender / laid-down weapons, a suspicion sensor | graph + grep: 0 production hits; catalogue lines 191, 237 ("the carrier and trigger 🟥") |

```mermaid
classDiagram
  direction LR
  class Armament { <<NEW static, Fdp.Toolkits/Combat>> Judge(view, observer, man) IdentifiedLethal or Unidentified or Nothing · LethalInHand(view, man) truth }
  class Appearance { <<NEW TKB data per item>> LooksLike Rifle Pistol LongObject SmallObject None · Size m · Lethality Reach }
  class Identification { <<NEW>> range from Size x light x optics (binoculars, night sight) }
  class NeutralInLine { <<NEW, beside LineOfFire.BlockedByFriendly>> a neutral on the line or in the spread holds the shot }
  class ThreatDanger { <<existing>> armed = 1 now via Armament }
  class WeaponOf { <<existing>> the primary weapon }
  class DispenserOnly { <<existing>> }
  class BuiltInMounts { <<existing type data, grows>> rifle, RPG · + pistol · + the HANDS mount }
  class LaidDown { <<NEW flag on a built-in mount, replicated>> surrendered: cannot fire, reads unarmed }
  class HandsLoadout { <<existing AmmoLoadout on the hands mount>> stones, a picked-up canister or grenade }
  class CarryStations { <<existing seats, grows>> Hands · Back · Hidden (Concealed) }
  class WeaponSystemEntity { <<existing>> + pick-up Rifle, Pistol · + ExplosiveCharge }
  class HeldItem { <<NEW replicated byte, the Gesture shape>> None Weapon Throwable Phone Binoculars }
  class PocketItems { <<NEW type data>> phone, binoculars — never entities }
  class WeaponPosture { <<existing, replicated>> AtEase Ready Aimed · Target · ActiveMount }
  class LandedRound { <<existing>> a round lying where it landed }
  class RoundPickUp { <<NEW, SimHost>> request from the brain · validate · remove · give }
  class StoneSource { <<NEW area kind>> unlimited stones }
  class AreaEffectCloud { <<existing, CE-3219>> + Source: sits on its canister while it vents }
  class EscalationLadder { <<CE-2239>> reads posture: at ease Warn · ready NonLethal · aimed Lethal }
  class SuspicionSensor { <<D4, later>> observable cues, never ground truth }
  Armament ..> WeaponOf
  Armament ..> Appearance : what it looks like, not what it is
  Armament ..> Identification : per observer
  Armament ..> DispenserOnly
  Armament ..> LaidDown
  Armament ..> CarryStations : a Hidden station is invisible
  Armament ..> HandsLoadout : lethal round in hand
  ThreatDanger ..> Armament
  BuiltInMounts --> LaidDown
  BuiltInMounts --> HandsLoadout : the hands mount
  CarryStations --> WeaponSystemEntity
  HeldItem ..> WeaponPosture : Weapon means ActiveMount
  HeldItem ..> PocketItems
  RoundPickUp ..> LandedRound
  RoundPickUp ..> StoneSource
  RoundPickUp ..> HandsLoadout
  AreaEffectCloud ..> LandedRound : Source
  EscalationLadder ..> WeaponPosture
  EscalationLadder ..> Armament
  EscalationLadder ..> NeutralInLine : before any shot
  SuspicionSensor ..> HeldItem : phoning is a cue
```
*What the picture shows that prose hid:* every source of danger — a built-in rifle, a carried launcher, a stone in the hand, a hidden vest —
ends in ONE function, so the 27 readers change their call, not their meaning; ⭐ *(R-2026)* that function judges what the OBSERVER can
identify (an item's `Appearance` at his range, light and optics), never what the man really holds — a phone at 150 m is "something", a
toy gun is a rifle; and the picture has TWO kinds of object only: TYPE DATA for
what a man is issued (rifle, pistol, hands, phone, binoculars — free until used) and ENTITIES for what changes hands (a picked-up rifle, a
launcher, a charge) or flies (rounds).

```mermaid
stateDiagram-v2
  [*] --> Ground : lying there (a weapon entity, a landed round, a stone source)
  Ground --> Hands : pick up
  Hands --> Back : stow (visible)
  Hands --> Hidden : conceal
  Back --> Hands : draw
  Hidden --> Hands : draw (armed, intent unknown)
  Hands --> Ground : put down, or lay down (surrender)
  Hands --> Flight : throw (a round off the hands mount)
  Flight --> Ground : lands
  Hidden --> Burst : a worn charge goes off
  Hands --> Burst : a held grenade goes off
```
*What the picture shows that prose hid:* concealment is a PLACE, not a property of the thing — the same pistol is visible in the hands or on
the belt and invisible under the coat; and "drawing" is the one edge that turns a hidden lethal weapon into a visible one — ⭐ *(R-2026)* which makes him
ARMED, INTENT UNKNOWN, not Hostile: the hostile act is pointing it at the forces, firing at them, or attacking (AW-16). ⛔ SUPERSEDED: "which
is why it is the hostile act (CV-4)".

```mermaid
graph TD
  S{AW-21: an enemy COMBATANT - Hostile and not of the Civilian class?} --> ST[engaged on STATUS - armed, a full threat, wherever his weapon is]
  ST --> OUT{out of the fight? laid down and hands up, or wounded out}
  OUT --> NOT[not a target]
  OUT --> NL
  S --> Q
  Q{what can THIS observer identify on him, at his range, light and optics?} --> A[an item that LOOKS lethal - a rifle, a pistol, a toy gun, a knife within reach]
  Q --> U[something he cannot identify - a long object, a small object in the hand]
  Q --> D[only items that look non-lethal - stones, a canister]
  Q --> E[nothing visible - a Hidden station, a vest]
  A --> ID[IDENTIFIED LETHAL - armed]
  U --> UNK[UNIDENTIFIED - warn and confirm, never fire on this alone]
  D --> UN[UNARMED - non-lethal force only]
  E --> UN2[UNARMED and neutral until he acts - only D4's suspicion sensor may flag him]
  ID --> I{his intent}
  I --> AE[weapon at ease, or just drawn: intent unknown - Warn, demand]
  I --> RD[weapon ready and an ignored warning: NonLethal, then Lethal]
  I --> HA[a HOSTILE ACT: aimed at us, fired at us or near us, attacking, a vehicle driven at us: Lethal]
  I --> AIR[fired into the air: intent unknown - Warn]
  HA --> SD[self-defence: Lethal even above the unit's ForceLevel]
  RD --> NL{a neutral in the line or the spread?}
  SD --> NL
  NL --> HOLD[hold the shot]
  NL --> FIRE[fire]
```
*What the picture shows that prose hid:* ⭐ *(R-2027)* the first question is STATUS — an enemy combatant is engaged as today, by
status, and everything below it is for civilians of any side and the neutral or unknown; the second question is what the OBSERVER can identify, so the same man is "armed" to a soldier 20 m
away and "carrying something" to one 200 m away in the dark — and a toy gun is engaged like a rifle, which is the training outcome
(R-2026); armed is never enough to fire — intent decides, and only a hostile act (or self-defence, which overrides the force level) gives
Lethal; every shot first asks whether a neutral is in the line or the spread. Hands up is NOT a branch — a man with his hands up is still
armed if he holds a weapon, and fire is held by CV-13a (a feint must not erase the threat); and posture and intent grade only the security
forces' LADDER, never the general danger, so a military fight (an at-ease enemy rifleman is a full threat) is unchanged.

```mermaid
sequenceDiagram
  participant P as police (CGF brain)
  participant SH as SimHost (rounds)
  participant CG as CGF (cloud owner)
  participant R as rioter (CGF brain)
  P->>SH: fire the gas launcher (WeaponFireIntent)
  SH->>SH: the canister lands, lies there venting (a landed round)
  SH->>CG: cloud creation request, Source = the canister
  CG->>CG: the cloud sits on its canister, Irritant hits once a second
  R->>SH: pick up the round (RoundPickUp request, in reach)
  SH->>SH: still lying there and not spent? remove it from the ground
  SH-->>R: the canister is in his hands loadout, its burn left
  CG->>CG: the source is gone: the cloud drifts free on the wind and fades
  R->>SH: throw it (FireAtPoint on the hands mount, the throw arc)
  SH->>CG: it lands again: a new cloud for the burn left
```
*What the picture shows that prose hid:* nothing new happens to the cloud — its source simply moves, and the existing creation request and
wind drift do the rest; the pick-up is SimHost's to grant, because SimHost alone simulates rounds (R-271), so two men grabbing one canister
cannot both get it.

```mermaid
sequenceDiagram
  participant I as covert insurgent (CivilianHsm, CGF)
  participant N as every node
  participant S as soldier (weapon brain)
  Note over I: Routine, Hostile at heart, a pistol in his Hidden station
  N-->>S: a neutral unarmed civilian (side Neutral, nothing visible)
  I->>I: his trigger (an order, a time, a target in reach): ArmedHostile
  I->>N: draw: the pistol to Hands, HeldItem Weapon, posture Ready
  N-->>S: identified lethal (in range), intent unknown: Warn, demand
  I->>N: aims at the soldier: the hostile act, side flips to Hostile (EntityWriteRouter)
  N-->>S: armed, Hostile, posture Aimed at him
  S->>S: the ladder allows Lethal (self-defence), no neutral in the line: fire
```
*What the picture shows that prose hid:* there is no "insurgent" detector anywhere — the soldier reacts only to what crossed the wire (side,
posture, what is in the man's hands), exactly as for anyone else; ⭐ *(R-2026)* the draw alone buys the insurgent a warning, not a bullet —
the soldier fires when the man AIMS, which is the moment real ROE allows it. ⛔ SUPERSEDED: the side flipped on the draw.

```mermaid
graph TD
  subgraph CGF["CGF (brains, cloud owner)"]
    HSM[CivilianHsm / soldier behaviours: draw, stow, conceal, pick up, throw, lay down, detonate]
    WB[weapon brain: Armament, ladder, WeaponChoice]
    CL[AreaEffectCloudSystem: the cloud sits on its source]
  end
  subgraph SimHost
    FP[FireProcessingSystem + the throw arc]
    RP[RoundPickUp: grant or refuse]
  end
  subgraph Every["every node"]
    REP[HeldItem, LaidDown, carry stations, WeaponPosture: replicated]
    MAP[3-D figure: item in hand, laid-down weapon, bending]
  end
  HSM -->|WeaponFireIntent| FP
  HSM -->|pick-up request| RP
  RP -->|granted| HSM
  WB --> REP
  REP --> MAP
  style RP fill:#f66,color:#000
```
*What the picture shows that prose hid:* the red box is the only NEW cross-node exchange — everything else rides wires that exist (fire
intents, posture, entity creation); and every node can answer `IsArmed` itself, because everything it reads is replicated.

| id | ✅ ruled (R-2025) | rejected — one line each |
|---|---|---|
| ~~**AW-1**~~ | ⛔ **SUPERSEDED by AW-15 (R-2026)** — *was:* **armed = an observer can see a usable LETHAL weapon on him**: a built-in lethal mount not laid down · a lethal carried weapon in Hands or Back · a lethal round in the hands loadout. ONE function (`Armament`), called by the 27 readers through `WeaponOf` | "any `WeaponState` = armed": a stone-thrower justifies lethal force, a concealed rifle is seen by all |
| **AW-2** | **non-lethal only arms nobody** (stones, canister, baton-round launcher, stun grenades, dispensers) | — |
| **AW-3** | **a Hidden station is invisible** · ⛔ **the rest SUPERSEDED by AW-16 (R-2026)** — *was:* "drawing a lethal weapon from it is the hostile act (CV-4)" | concealment through the side flip alone: `WeaponOf` reads ground truth |
| **AW-4** | **issued weapons stay built-in type data — the rifle AND a new PISTOL mount**; the weapon brain switches by itself (`WeaponChoice`) | an entity per issued weapon: W-A — "an entity per rifle would multiply entities and wire for nothing" |
| **AW-5** | **surrender = a LAID-DOWN flag** on every built-in mount (cannot fire, reads unarmed); **handover** later: laying a weapon down spawns a pick-up weapon entity and empties the mount — only for the weapon actually picked up | hands up = unarmed: a feint erases the threat (CV-13a holds fire without changing what he is) |
| **AW-6** | **pick-up rifle and pistol weapon-system types** (one man, fire while carried) for anything that changes hands — a covert insurgent's, a dropped soldier's | — |
| **AW-7** | **every person gets a built-in HANDS mount** (non-lethal by default, an `AmmoLoadout` of up to 4 kinds, empty at start); a throw is `FireAtPoint` on it, on the throw arc (6038 and 6037 get `Indirect`) | a stone pouch entity: an entity per rioter for nothing visible · a mountless throw: a second firing path on SimHost |
| **AW-8** | **stones are PICKED UP**: from a stone-source AREA (unlimited, 1.5 s bend, up to 5) or a landed throwable (a stone stays 60 s after landing, a canister, a grenade) via a RoundPickUp request SimHost grants | every stone an entity: hundreds per riot on the wire · stones from terrain material: no terrain stores loose stones |
| **AW-9** | **a gas canister can be thrown back**: the cloud sits on its canister while it vents; picked up, the cloud drifts free; thrown, a new cloud for the burn left. A picked-up LIVE grenade is a lethal round in hand ⇒ armed | — |
| **AW-10** | **posture grades the security forces' LADDER (CV-8), not general danger**: armed at ease ⇒ Warn · ready ⇒ NonLethal, Lethal after an ignored warning · aimed at us or fired ⇒ Lethal; "aimed at me" in threat RANKING later, with its own demo | posture in the general danger score: changes every military scenario's ranking and the Sentry threshold |
| **AW-11** | **insurgents**: OPEN = the existing combatant type (unchanged); COVERT = a civilian, Hostile at heart, a pick-up weapon Hidden, `ArmedHostile` reusing the soldiers' fighting behaviours | a covert insurgent TYPE: a type per role (CV-1) |
| **AW-12** | **an explosive CHARGE is one weapon-system type in three placements**: worn Hidden (suicide vest — never "drawn", only D4's suspicion sensor may flag him, a search reveals it), placed (IED, its trigger holder controls it), a vehicle station (VBIED); it bursts at its controller's position | a bomber type with a built-in charge: cannot be planted or put in a car |
| **AW-13** | **worn = carried in a HIDDEN station** — a person gets a few stations (Hands, Back, Hidden) instead of one; concealment belongs to the STATION | a separate "worn" concept: a second mechanism for one idea |
| **AW-15** *(R-2026)* | **armed is judged PER OBSERVER, by what he can IDENTIFY**: every item carries an `Appearance` (looks like Rifle / Pistol / LongObject / SmallObject / None, a size, its lethality and reach) and an identification range from its size × light × optics (binoculars, night sights lengthen it). Three answers: **identified lethal** · **unidentified** (warn and confirm, never fire on it alone) · **nothing**. A toy or replica gun LOOKS like a rifle and may be engaged — the training outcome | ground truth of what he holds: no misidentification can ever happen, and those are the cases a trainee is assessed on (the 2007 Baghdad cameras, toy-gun shootings, the 2005 London man taken for a bomber) |
| **AW-16** *(R-2026)* | **drawing is NOT a hostile act**: drawn ⇒ armed, intent unknown (Warn, demand). The **hostile act** — and the side flip (CV-4, amended) — is pointing at the forces, firing at them, attacking, or driving a vehicle at them | the draw as the act: in many theatres households, farmers, guards and plain-clothes police carry openly — it punishes the normal |
| **AW-17** *(R-2026)* | **direction matters**: fired AT us, or rounds landing near us (`NearMiss`) ⇒ Lethal; fired into the air ⇒ intent unknown (Warn) | "he fired ⇒ Lethal": celebratory fire at weddings and funerals has caused tragic engagements |
| **AW-18** *(R-2026)* | **self-defence overrides the unit's `ForceLevel`**: a weapon aimed at us, or fire at us, allows Lethal even to a NonLethal police line | the cap always binding: no ROE card denies self-defence |
| **AW-19** *(R-2026)* | **lethality is an item's REACH, not "firearms and explosives"**: a knife or machete is lethal within its reach (~2 m, and an attacker closes 7 m in under 2 s), a Molotov is lethal, a stone non-lethal; a **vehicle driven at people** is a hostile act | firearms only: knife attacks at checkpoints and Molotovs (deadly force under most ROE) read as unarmed |
| **AW-20** *(R-2026)* | **a neutral in the line of fire or within the weapon's spread HOLDS the shot** — beside `LineOfFire.BlockedByFriendly`, which today holds only for the shooter's own force (`LineOfFire.cs:71-81`, measured) | collateral left for later: an armed man in a crowd is lethal-permitted the moment he is identified (human shields, fire from inside protests) |
| **AW-21** *(R-2027)* | **STATUS for combatants, CONDUCT for civilians**: an enemy COMBATANT (Hostile and not of the `Civilian` class — soldiers, the open `Insurgent`, enemy crews) is engaged on STATUS: armed by status, a full threat wherever his weapon is, unless out of the fight (laid down and hands up, AW-5; wounded out, `ThreatDanger.IsOutOfTheFight`). AW-15…AW-20 apply ONLY to the `Civilian` class (of any side, a Hostile-flipped rioter included) and to neutral or unknown targets — so the military fight, its ranking and the Sentry threshold are unchanged. ⚠ Simplification: the law targets a civilian who takes part only "for such time" as he does; the flip is sticky for the exercise (CV-4) | per-observer judgement for combatants too: an unseen enemy rifleman becomes "unidentified, warn first", which no military ROE does · Hostile = status target: a rioter who threw one stone becomes as targetable as an enemy rifleman |
| **AW-22** *(R-2028)* | **a LETHAL hostile act makes a civilian a COMBATANT; a non-lethal one only makes him Hostile.** Lethal = fires at the forces, a lethal attack (a knife, a Molotov), plants or sets off a charge — the `HostileAct` stamp carries its kind (CV-4). ⭐ **Combatant status is KNOWLEDGE PER UNIT**: the units that saw or heard the act, and their squad through the shared contacts (`SquadPerceptionMergeSystem`), mark that contact a known combatant (one byte per `TargetMemory` slot) and engage him on status (AW-21) until he is out of the fight; everyone else sees a civilian and applies the conduct rules — so a man who fires, hides his rifle and blends back is still a target to the patrol that watched him, and a civilian to a fresh one. The side flip stays global (CV-4); "for such time" stays simplified (sticky for the exercise) | any hostile act ⇒ combatant: one stone makes a rifleman's target · a global combatant fact: every soldier knows who fired, and hiding cannot be represented · never a combatant: a man who just shot at you gets a warning each time he re-aims |
| **AW-23** *(R-2028)* | **a PLAYER who kills his friends is marked an enemy combatant and treated as one.** A unit held by a station (`Possession`, first person) whose rounds or bursts put a FRIEND out of the fight — ⭐ *lean:* one friend killed or incapacitated, or two wounded within 60 s (a single wound may be an accident; deliberate fratricide repeats) — has his side flipped Friend → Hostile (`EntityWriteRouter`); being of a combatant class, AW-21 then makes him a status target on every node, and every friendly AI engages him. The instructor gets an event (who, whom, when) for the after-action review and may reset it. ⚠ Needs the shooter on the damage: SimHost knows it at the hit (`BallisticProjectile.Shooter` via `HitEvent.BulletEntity`), but `DamageAssessedEvent` does not carry it today | no rule: a trainee shooting his squad is ignored by it · the first friendly hit: real fratricide by accident happens and must not end the exercise · AI units: they never fire through a friend (`LineOfFire.BlockedByFriendly`), so only an operator can do this |
| **AW-24** *(R-2029)* | **THE SIMPLE PATH STAYS SIMPLE.** A soldier against an enemy combatant pays ONE class-and-side lookup (AW-21) and an empty neutral-in-line check (AW-20); identification, intent and the ladder are never reached, and the ladder runs only for a unit with a graded force level (security forces). Gated, not hoped: ① `ua-armed-status` (§5c) stays green from the first armed-people commit on, with every soldier rail's expectations unchanged (backend note 2); ② a COUNTER rail — in a scenario with no `Civilian`-class entity the conduct path is entered ZERO times. Three military touches, each bounded: the **pistol** only on the types that should carry it, accepted by re-running the soldier rails (`ua-threat-ranking`, `ua-posture`, `ua-universal-soldier`, `bt-window-duel`); the **hands mount** never chosen as a weapon while empty or non-lethal (a rail); the **neutral-in-line hold** is the one new rule a soldier obeys, and only near neutrals. An item's **appearance defaults from its weapon category** — only odd items (a toy gun, a phone, a disguised weapon) set it by hand | one rule for everyone: a pure military fight pays for civilian judgement it never needs, and a future change could route soldiers through it unnoticed |
| **AW-14** | **the hands hold everyday things too**: a replicated HELD-ITEM byte (None, Weapon, Throwable, Phone, Binoculars; "Weapon" means `WeaponPosture.ActiveMount`, never a copy); phone and binoculars are POCKET ITEMS, type data, never entities; binoculars lengthen vision and narrow it while held; a phone is a D4 cue and an IED's remote trigger | phones and binoculars as entities: the pistol's cost times every civilian |

⭐ **Owners and order.** Backend (frame [HANDOFF_Behaviors_To_Backend_Armed_People.md](blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md)):
`Armament` + the 27 readers routed (AW-1/2), `LaidDown` (AW-5), stations per person (AW-13), `HeldItem` + its wire (AW-14), the hands and
pistol mounts and pocket items on person types (AW-4/7/14), pick-up rifle/pistol and the charge types (AW-6/12), `Indirect` on 6037/6038,
RoundPickUp on SimHost + stone sources + landed stones lingering (AW-8), the cloud's source (AW-9). Behaviors: `CE-2238` (draw / stow /
conceal / pick up / throw / detonate, the covert trigger, riot), `CE-2239` (the ladder's posture rows), `CE-2241` (hands up). Ui: the held
item, the laid-down weapon and the bending pose on the figure.

⭐ **R-2026 / R-2027 owners:** backend — `Appearance` on every item's TKB data, `Armament.Judge` per observer with the identification range,
and the status rule for combatants in front of it (B-7 is amended, [NOTE](blueprints/batches/NOTE_Behaviors_To_Backend_Armed_People_Amended.md),
[NOTE 2](blueprints/batches/NOTE_Behaviors_To_Backend_Armed_People_Status.md)); behaviors — the intent rows, self-defence and the
neutral-in-line hold in the ladder and the weapon brain's hold (`CE-2239`), the hostile act moved from the draw to the aim/fire/attack
(`CE-2238`).

### 5h.1 AS-BUILT — AW-20, a neutral in the line or the spread holds the shot *(behaviors, 2026-10-17)*

⭐ **Built as the class diagram's `NeutralInLine` box says — beside `LineOfFire.BlockedByFriendly`, at its four callers.**
`LineOfFire.BlockedByNeutral(world, shooter, target | point, σ)`: a living PERSON (`PhysicsColliderReaders.IsPerson`) whom
`ThreatDanger.IsNeutral` calls neutral (the CE-2233 test, reused), other than the target, whose collider — widened by σ (`HitModel.Sigma`,
the shooter's spread) × his distance along the line — the line crosses; a held round is `ActionReason.HoldNeutralOnLine` (posture stays Aimed).

| caller | as built |
|---|---|
| `AimAndFireExecutor` aimed path | after the friendly hold, same shape (Running, nothing spent) |
| `AimAndFireExecutor.FireBlind` | the point form, after the friendly hold |
| `FireAtPointExecutor.ShootAtPoint` | only when `obeyRoe` (the AI's) — an operator decides himself, as for the neutral target (CE-2233); an indirect warhead is exempt |
| `WeaponTaskDispatcherSystem.Engage` (turrets) | entity or point form; an indirect warhead is exempt |

| ⚠ deviation / detail | why |
|---|---|
| a neutral is a PERSON, never just `ForceId.Neutral` | `Neutral` is the enum's ZERO: walls, wrecks, dropped weapons would hold every shot |
| a NEUTRAL shooter is never held by it | the neutrals are his friends — `BlockedByFriendly` already holds him |
| ⭐ it holds EVERY AI shooter, the insurgent too | the design names no shooter class; one rule is the simple path (AW-24). An insurgent who fires through a crowd on purpose is a later per-unit knob, recorded under the known limits below |

Rails `AimAndFireExecutorTests.AW20_…` (2) — each red under its mutant (the executor's hold removed; the spread term removed). Demo
`ua-armed-shield` (§5c), live check + twin `PostureScenarioTests.AW20_ArmedShield_…`.

📐 **Measured building the demo:** a civilian standing ON the line never reaches this hold — he hides the target (vision's planar
sight test uses the same 0.3 m collider), so no aimed shot is ever ready (the twin: 4098 on-line frames, reasons only None / Aiming). ⇒ the
hold's own case is the NEAR MISS: a neutral beside the line, inside the spread. Pedestrians also keep ~1 m from each other, so a crowd
packed closer than that spreads out.

### 5h.2 AS-BUILT — CV-13a, hands up holds fire *(behaviors, `CE-2241` ①, 2026-10-17)*

⭐ **Built exactly where CV-13a's owner column puts it, beside the CE-2233 neutral gate:** `ThreatDanger.ShowsHandsUp(view, target)`
(`Gestures.Of` == HandsUp, read live) ⇒ `ThreatDanger.Of` = 0 and `AimAndFireExecutor.HoldOf` ⇒ `ActionReason.HoldSurrender`
("hold: hands up", posture Aimed — covered, never fired). Any side, a Hostile included; ⛔ the side never flips and nothing is disarmed
(AW-5); the hands coming down is read the next frame (a feint). An operator's fire is not held (`HoldOf` is the AI's).
Rail `AimAndFireExecutorTests.CV13a_…`, red under each mutant (the `HoldOf` arm removed; the danger-0 arm removed).
⏳ **Demo deferred to after `CE-2238` part 1 (§5i):** a live hands-up needs someone who RAISES them — the scenario format cannot set a
`Gesture` (`DataPolicy.NoScenario`) and the debug API edits only a component that exists. ⭐ Lean: a hostile rioter (§5i) raises his hands
when a forces person aims at him past `PanicAt` — the `ua-armed-surrender` hands-up half then runs on it.

⚠ **Known limits (ruled later, not now):** ⓪ *(AW-20 as built)* an insurgent holds for neutrals like a soldier — human shields used
on purpose need a per-unit "ruthless" setting; ① one item in Hands at a time; ② a dead bomber's charge stays live only once a dead-man setting
exists; ③ a search that reveals a Hidden station is the checkpoint slice (SQD-13); ④ the D4 suspicion sensor is its own item; ⑤ *(from the
review, each its own later item)* Friend → Hostile and disguise (insider attacks, stolen uniforms); a killed soldier's weapon left to pick
up; the bomber's hit-initiated vest, handler trigger and secondary devices; a vehicle ladder (signs, flares, warning and disabling shots);
the warning-shot step and time to comply; snatching a soldier's weapon.

## 5i. CIV-4 behaviour, part 1 — the riot, the blow, the side flip *(behaviors, `CE-2238` part 1, 2026-10-17; build-state: READY-TO-BUILD)*

⭐ **Scope.** The protest's next edge (§2.2 Protest → Riot), the riot's one act that needs no backend item — the **melee blow** (D5) — and
the **hostile act** it is, which flips the rioter's side (CV-4 as amended by AW-16). Throwing (`ThrowAt`) waits on backend B-8 / B-13
(the hands mount, stones); the act's replicated KIND waits on B-16 — this slice stamps the act brain-locally and B-16 swaps the stamp for
the replicated component later. Demo `ua-civ-melee` (§5c).

**INVENTORY** *(2026-10-17)*: `search_graph name_pattern=".*(Melee|HostileAct|Instigat|Riot|Anger).*" label=Method` ⇒ 89 hits, **none** a
melee, a hostile act or an anger producer (danger-area and test names); grep `Melee|HostileAct|Instigat|\.Anger\b` ⇒ animation montage ids
only, and `CrowdSystem.cs:195` READS `Anger` (the crowd's mean). ⇒ nothing to reuse but the pieces below. ⚠ `check_index_coverage` not run.
Reused, measured: `EntityWriteRouter.For(world).Write(… Affiliation …)` (§7.1a — the flip, Direct on CGF for a scenario cast) ·
`DamageAssessedEvent{Effect = Blunt, EffectSeconds}` on the publishing node's bus, applied only where Health is claimed
(`HealthApplicationSystem.cs:77`) — CGF for a scenario cast; `AreaEffectCloudSystem.Pulse` is the CGF-side precedent · `Effects.IsKnockedDown`
· `Gestures.Set` · `LocomotionMoveTo` · the protest ring (`CivilianBehavior.Protest`).

```mermaid
classDiagram
  direction LR
  class CivilianHsm { <<existing, grows>> + Riot state · Protest ⇄ Riot on his anger }
  class CivilianState { <<existing>> Anger · + HostileActAt (brain-local stamp until B-16) }
  class CivilianParams { <<existing, grows>> + Instigator · RiotAt }
  class CrowdSystem { <<existing, grows>> ⑤ anger: an instigator raises his crowd's anger near him; anger fades }
  class Melee { <<NEW static, Fdp.Toolkits/Combat>> Strike(world, attacker, victim) a Blunt DamageAssessedEvent }
  class HostileActs { <<NEW static, Fdp.Toolkits/Civilians>> Commit(world, actor) stamp + flip once }
  class EntityWriteRouter { <<existing>> Affiliation write, Direct or Requested }
  class HealthApplicationSystem { <<existing>> damage + EffectState.KnockedDownUntil on the Health owner }
  class Gestures { <<existing>> FistRaised while rioting }
  CivilianHsm ..> Melee : within reach, on cooldown
  CivilianHsm ..> HostileActs : the blow is the act
  CivilianHsm ..> Gestures
  CrowdSystem ..> CivilianState : Anger
  CivilianHsm ..> CivilianState : reads Anger
  HostileActs ..> EntityWriteRouter
  HostileActs ..> CivilianState : HostileActAt
  Melee ..> HealthApplicationSystem : DamageAssessedEvent
```
*What the picture shows that prose hid:* the slice adds TWO statics and grows three existing types; the blow and the flip ride paths
that already reach every node (the damage chain, the attribute write) — nothing new crosses the wire.

```mermaid
stateDiagram-v2
  Protest --> Riot : his anger reaches RiotAt
  Riot --> Protest : his anger falls below half of RiotAt
  Riot --> Panic : fear past PanicAt
  Riot --> Casualty : wounded
  state Riot {
    [*] --> Mill
    Mill --> Close : a forces person within RiotReach
    Close --> Strike : within MeleeReach
    Strike --> Close : cooldown
    Close --> Mill : none in reach
  }
```
*What the picture shows that prose hid:* the riot is the protest's crowd turned on the nearest forces person; the blow is an inner
loop, not a state of its own, so leaving Riot (fear, a wound, anger gone) always stops the walking and the gesture in one deactivator.

```mermaid
sequenceDiagram
  participant CS as CGF CrowdSystem
  participant R as rioter's CivilianHsm (CGF)
  participant M as Melee
  participant H as HealthApplicationSystem (CGF, Health owner)
  participant W as EntityWriteRouter
  participant N as SimHost, IG
  CS->>R: anger raised by the instigator beside him
  R->>R: Protest to Riot, FistRaised, walks at the nearest forces person
  R->>M: within 1.5 m, cooldown over: Strike(victim)
  M->>H: DamageAssessedEvent Blunt, 5 hp, 3 s
  H->>H: Health down, EffectState KnockedDownUntil
  H-->>N: dtEffectState, Health replicated
  R->>W: HostileActs.Commit: Affiliation Opposing (once)
  W-->>N: dtEntityInfo, the rioter reads Hostile everywhere
```
*What the picture shows that prose hid:* both writes happen on CGF and leave through EXISTING egress — the knock-down through
`dtEffectState`, the flip through the dirty-marked `EntityInfo` (§7.1a's rail) — so "on every node" needs no new code.

```mermaid
graph TD
  CGF[CGF node] --> CLP[CgfLogicPack]
  CLP --> CFS[CivilianFearSystem]
  CLP --> CRS[CrowdSystem: + anger]
  CGF --> BT[BrainTickSystem]
  BT --> HSM[CivilianHsm: Riot]
  HSM --> MEL[Melee.Strike]
  HSM --> HA[HostileActs.Commit]
  MEL --> BUS[CGF bus: DamageAssessedEvent]
  BUS --> HAS[HealthApplicationSystem, Health owner]
  HA --> EWR[EntityWriteRouter]
```
*What the picture shows that prose hid:* no new system is registered — `CgfLogicPackTests`' count is unchanged; the riot runs inside
the brain tick and the crowd system that already run every frame on CGF.

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| where anger comes from (slice 1) | ⭐ an **instigator** (`Instigator` param; his own anger 1) raises the anger of his crowd's members within `ContagionMetres` toward 1 at `InstigateRate` 0.15/s, faster the nearer; anger fades 0.02/s; the security forces' provocation is CV-14 / `CE-2241` | anger contagion between every member: a wave nobody started · the crowd's mean as the trigger: one instigator in 12 never tips it |
| when he riots | ⭐ HIS anger ≥ `RiotAt` (default 0.6); back to Protest below half of it (hysteresis); only a protester riots | the crowd's mean: the front rank and the back rank riot together |
| who he goes for | ⭐ the nearest living, standing PERSON of the forces — `Friend`, not Neutral, not of the `Civilian` class — within `RiotReach` 30 m | anyone: a rioter beating a bystander is a different act (later) · the crowd's focus point: not every protest point is the police line |
| the blow | ⭐ `Melee.Strike`: within `MeleeReach` 1.5 m, every `MeleeCooldown` 2 s, a `DamageAssessedEvent{5 hp, Blunt, 3 s}` on the CGF bus — applied by the Health owner (D5); a knocked-down victim is not struck again until he is up | a melee "round" through the ballistic path: a fist has no muzzle · damage on SimHost: CGF is where the brain decides and, for a scenario cast, where Health is claimed |
| the hostile act | ⭐ the FIRST blow commits it: `HostileActs.Commit` flips his side to Hostile (Opposing) through `EntityWriteRouter` and stamps `CivilianState.HostileActAt` — once, sticky (CV-4); its KIND (B-16) is added when backend lands it | flip on entering Riot: AW-16 — anger is not an act · a new replicated component now: B-16 owns it |
| a hostile rioter and the soldiers | ⭐ unchanged rules: he is Hostile and unarmed (danger 0.3); the AI may engage him — the force LEVEL that keeps it non-lethal is A6 (`Roe.ForceLevel`, `CE-2239`) | special-casing rioters in the threat rules: the ladder's job |

⚠ **Known limits:** ① a blow at a victim whose Health another node claims is dropped there (`HealthApplicationSystem`'s gate) — fine for
scenario casts (CGF claims them), a limit for a SimHost-created soldier; ② no stone, no Molotov, no knife yet (B-8, B-13, AW-19); ③ the
hostile act carries no kind yet (B-16) — AW-22's lethal/non-lethal split waits on it; ④ a rioter does not avoid being shot — the force
level (A6) is what makes the police answer non-lethally.

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

⏳ **Part 3 — the cloud (B1-c, B1-d, M-4). ✅ RULED `2026-10-17` (R-3014): the cloud is a REPLICATED entity.** ⚠ The obstacle-prism SIGHT mechanism below is SUPERSEDED by §7.3b (the cloud drifts on the wind) — the replicated entity stands. 🔒 User: *"Claud being a special area entity approved."* The node that
owns the detonation creates it through the normal creation path: a TKB "gas cloud" type carrying `StaticObstacle` (material `smoke`)
+ `ObstacleShape` + `Lifespan`. Every node then bakes it with the existing `StaticObstacleBakeSystem`. When the owner's
`LifespanSystem` (`Fdp.Toolkits/Time/LifespanSystem.cs`) removes it, the delete replicates. The owner alone pulses the irritation
(`AreaEffectCloudSystem`). It reuses creation, replication, the obstacle bake and its lifecycle ack. A late joiner and a recording
see the cloud as they see any entity. ⚠ Still to measure: M-4, the rebake cost per cloud.

| the lean rests on | code | design |
|---|---|---|
| one registration covers every host | ✅ the bake is made inside the shared pack (`EntityCreationPack.cs:115`) | ✅ P-7a §9 *(rule and ack both in the pack)* |
| a local derivation needs every node to see every detonation | ⚠ corrected `2026-10-17`: CGF DOES get them (it builds `SimHostAuxiliaryTranslatorPack` too, `CgfSubsystem.cs:1103`; the Brain-only branch, CE-2176). But they arrive through host-shaped lists with role exclusions, which the user ruled must be unified: CE-1148, [Node Roles §4.5](DESIGN_Node_Roles_And_Policies.md) | ✅ R-3015 |
| a removed cloud leaves every node | ⛔ assumed: the owner's destroy replicates like any delete | ✅ creation unification §3 |

⛔ **SUPERSEDED (`2026-10-16`):** *"`DetonationNotification` reaches EVERY node … each node derives the same cloud … a registration on
every host, not a single pack line."* The second premise was wrong: the bake is one line in the shared pack. A derived cloud would also be missed by a late joiner.

#### 7.3b Part 3 DESIGN — the cloud *(`CE-3219`, backend, `2026-10-17`; build-state: BUILT — as-built in §7.3c)*

🔒 User, `2026-10-17`: *"do the cloud next. Btw the ground wind could move the cloud i guess."* ⇒ the cloud DRIFTS. ⛔ That retires
§7.3's B1-c (a `smoke` obstacle prism baked into the terrain): measured, `StaticObstacleBakeSystem` waits for its obstacle set to be
quiet for 0.5 s before baking (`StaticObstacleBake.cs`, `DebounceSeconds`), so a cloud that moves every tick is never re-baked; and a
prism is a wall to the navmesh and to `SurfaceZ` (nothing but the renderer skips a see-through prism — `TerrainWorldMesh.IsVolumeEffect`
is its only reader), so people would walk round gas, or stand on it. ⭐ Instead the cloud is a LIVE OVERLAY at the world-query seam,
the way door states already are (R-219): every sight query is asked through `IWorldQuery` (`WorldQuery.Of`, `TerrainWorldLosStrategy`,
`TerrainLosService`), and the query multiplies in the clouds the line crosses.

```mermaid
classDiagram
  direction LR
  class AreaEffectCloud { <<NEW component, PerInstanceValue>> Kind TearGas Smoke · Radius · Height · SightTransmittance · IrritantSeconds · BornAtSimSeconds · Seconds }
  class AreaEffectClouds { <<NEW static>> Of(view) the live cloud table · Request(repo, at, cloud) · TkbType 7202 }
  class CloudTable { <<NEW, the DoorStates shape>> per thread, per view; Transmittance(from, to) }
  class TerrainWorldQuery { <<existing, grows>> + Clouds: SightBlocked, QuerySight, Trace(Sight) multiply them in }
  class WorldQuery { <<existing>> Of(view) passes the view's clouds }
  class AreaEffectSystem { <<existing>> an Irritant or Smoke burst requests a cloud }
  class EntityCreationRequest { <<existing, MOVED to Fdp.Toolkits, CE-3228>> on the FDP bus, owner 0 = the system picks }
  class CreateEntityRequestSystem { <<existing>> the default processor creates it, or the request is forwarded there }
  class AreaEffectCloudSystem { <<NEW, Interactions capability, owner only>> once a second per cloud: drift by WindAt x elapsed, Irritant hits to people · destroy at the end }
  class EntityAreaEffectCloud { <<NEW wire dtAreaEffectCloud 144>> creator to every node, no group (like dtObstacleShape) }
  class EffectLayer { <<existing, ui U4-a>> draws Radius, fades on BornAtSimSeconds + Seconds }
  AreaEffectSystem ..> AreaEffectClouds : Request
  AreaEffectClouds ..> EntityCreationRequest : publishes on the FDP bus
  EntityCreationRequest ..> CreateEntityRequestSystem
  CreateEntityRequestSystem ..> AreaEffectCloud : InitialComponents
  AreaEffectCloud ..> EntityAreaEffectCloud
  AreaEffectCloudSystem ..> AreaEffectCloud
  AreaEffectClouds ..> CloudTable
  WorldQuery ..> TerrainWorldQuery
  TerrainWorldQuery --> CloudTable
  EffectLayer ..> AreaEffectCloud
```
*What the picture shows that prose hid:* nothing the cloud does touches the terrain world, so a moving cloud costs no bake on any node; the
only new wire data is the cloud's own description, once (its position rides the entity's ordinary transform).

```mermaid
sequenceDiagram
  participant SH as SimHost (resolves the burst)
  participant CG as CGF (the default processor)
  participant C as cluster (every node)
  participant Q as any sight query
  SH->>SH: AreaEffectSystem: a TearGas / Smoke burst publishes EntityCreationRequest(type 7202, owner 0) on the bus
  SH->>CG: not the default processor: the forwarder sends the request (CreateEntityRequest on DDS)
  CG->>CG: CreateEntityRequestSystem creates it, owner = CGF, a bare type is granted to nobody (R-175)
  CG->>C: EntityMaster + dtAreaEffectCloud (Kind, Radius, born, seconds)
  loop once a second per cloud, owner only
    CG->>CG: move the cloud by WindAt(position) x the time since its last step
    CG->>CG: Irritant hit to every PERSON inside (DamageAssessedEvent, 0 damage, to the owner of his Health)
    CG->>C: the transform (ordinary position replication)
  end
  Q->>Q: WorldQuery.Of(view): SightBlocked multiplies terrain x clouds crossed
  CG->>C: at BornAt + Seconds the owner destroys it (the delete replicates)
```

```mermaid
graph TD
  PACK[shared pack: every node] --> CLOUD[AreaEffectCloudSystem: acts only on clouds it has authority over]
  PACK --> PAIR[dtAreaEffectCloud egress + ingress]
  AES[AreaEffectSystem: the node that resolves the burst] --> REQ[EntityCreationRequest on the FDP bus, owner 0]
  REQ --> CERS[CreateEntityRequestSystem: the default processor creates it; elsewhere the forwarder sends it there]
  WQ[WorldQuery.Of / TerrainWorldLosStrategy / TerrainLosService] --> TABLE[AreaEffectClouds.Of view]
  LS[LifespanSystem]:::dead
  classDef dead fill:#f88
```
*Caption:* the red box is deliberately NOT used — `LifespanSystem` destroys on every node that runs it, so a replicated cloud carrying
`Lifespan` would be deleted locally on each replica as well as by its owner; the cloud keeps its own born/seconds and only the owner ends it.

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| sight | a live overlay at the world-query seam (the `DoorStates` shape), multiplied into `SightBlocked`, `QuerySight` and `Trace(Sight)` | a baked `smoke` prism: never re-baked while drifting, and a wall to the navmesh and `SurfaceZ` |
| drift | the owner steps the cloud ONCE A SECOND by `WeatherField.WindAt(position) × the time since its last step` (🔒 user: *"not every tick"*); no weather ⇒ it stays | every tick: a position sample per tick per cloud on the wire · a fixed breeze (VE W3's): ignores the scenario's weather |
| lifetime | `BornAtSimSeconds` + `Seconds` on `AreaEffectCloud`; the owner destroys it | `Lifespan`: destroyed on every node (above) |
| creation | ⭐ THE entity-creation request on the FDP bus (`AreaEffectClouds.Request` → `EntityCreationRequest`, owner 0 = the system picks: the default processor creates it, then the role-group grants) — `CE-3228`, Entity Creation §3.4d (🔒 user: *"whoever wants to create an entity simply puts a request in the form of FDP bus event, and some system will create the entity"* · *"Isn't the creation request coming from the network exactly the same one … Do we need some new FDP request? I do not think so."*) | a toolkit event + a Hrot spawn system: two steps · publishing `SpawnEntityCommand` directly (with a `ThisNode` owner): the ORDER level, which skips the routing and the load-balanced grants (Authoring Surface G1) — built first, reverted the same day · a new FDP request type: two types for one intent (user) |
| who irritates | the owner, once a second, an Irritant hit (0 damage) to each PERSON inside (`PhysicsColliderReaders.IsPerson`, the one person check) through the existing `DamageAssessedEvent` → owner of `Health` path | every node: N copies of each hit · every `Health` holder: gasses vehicles |

| the design rests on | code | design |
|---|---|---|
| every sight query goes through one seam | ✅ `WorldQuery.cs:19` and `LosStrategies.cs:160` build it through `TerrainWorldQuery.ForView`; the EQS `TerrainLosService` takes that `IWorldQuery` (`ILosService.cs:31`; its `:39` bare-terrain form is for tests and tools) | ✅ World Query Seam WQ-A |
| a moving prism is never re-baked | ✅ `StaticObstacleBake.cs` (signature must be quiet `DebounceSeconds`) | — |
| the wind is readable at a point | ✅ `WeatherField.WindAt(world, point)` (`WeatherField.cs:249`) | ✅ Weather §4 (CE-3216) |
| a creator-owned per-instance descriptor needs no group | ✅ `NedOwnershipGroupBinding.cs:102` (dtObstacleShape) | ✅ P-7a §9 |
| `LifespanSystem` destroys on every node | ✅ `LifespanSystem.cs` (no authority check) | — |

#### 7.3c AS-BUILT `CE-3219` part 3 *(2026-10-17)* — the cloud, built as §7.3b draws it

| built | where |
|---|---|
| `AreaEffectCloud` (component 401, `NoScenario`; ⚠ 399 went to behaviors' `CivilianState`) · `CloudKind` · `CloudTable` / `AreaEffectClouds.Of` (the `DoorStates` shape: per thread, the last table handed back while unchanged; null when there are none) · `AreaEffectClouds.Request` / `TkbType` 7202 | `Fdp.Toolkits/Combat/AreaEffectClouds.cs` |
| sight: dense for two thirds of its life, then thinning linearly to clear (`TransmittanceAt`); clouds crossed MULTIPLY (two 0.6 clouds block, one does not); `Trace(Sight)` lists each as a `"cloud"` crossing | `TerrainWorldQuery.SightBlocked` / `Trace`, `ForView` |
| the owner's system, once a second per cloud (`StepSeconds`; the first step at once): drift by `WeatherField.WindAt(position) × elapsed`, an Irritant hit (0 damage, `IrritantSeconds`) to every living, not-hidden PERSON inside (`PhysicsColliderReaders.IsPerson`); end at `BornAt + Seconds` (checked each tick) — ⭐ a replicated cloud through the ONE teardown path, a `DestroyEntityCommand` published once (Entity Creation §3.4c), so every replica goes too; 📐 the first build destroyed the owner's copy directly and the replicas stayed (measured by the cross-node rail once CGF owned it); an offline cloud (no network identity) is destroyed directly | `Combat/Systems/AreaEffectCloudSystem.cs` |
| the burst asks: `WarheadDto` grows `CloudRadiusM` / `CloudHeightM` / `CloudSeconds`, `WarheadKind.Smoke`, `HasCloud`; tear gas 0.4 transmittance, smoke 0.1; a remote detonation never asks | `AreaEffectSystem.RequestCloud` |
| creation: ⭐ `CE-3228` — the entity-creation request on the FDP bus, owner 0: the default processor (CGF) creates it — on any other node the forwarder sends the request there (Entity Creation §3.4d). TKB 7202 "Area effect cloud" registered by Hrot (`AreaEffectCloudTkb`, bare, hidden from the palette) ⇒ no role group applies and nothing is granted away (R-175, `RoleGroupOwnershipStrategyTests.ATacticalSymbol_GrantsNothing`): the creator owns its position too, so the owner's drift replicates. The owner's system is in the `Interactions` capability (every node) | `AreaEffectClouds.cs`, `AreaEffectSystem.RequestCloud`, `AreaEffectCloudTkb.cs`, `CoreInfrastructureCapabilities.cs` |
| wire: `dtAreaEffectCloud` 144, topic `EntityAreaEffectCloud` (reliable, transient-local), no ownership group, in the shared translator pack | `EntityAreaEffectCloudTranslators.cs`, `SharedTranslatorPack.cs` |
| munitions (B-5's gas grenade): tear-gas grenade `6039` (6 m, 60 s, time fuze 1.5 s) · 40 mm tear-gas canister `6040` (8 m, 90 s) · smoke grenade `6041` (8 m, 4 m high, 75 s); the security launcher man carries 2 tear-gas grenades (mount 2) | `MunitionTkbCatalog.cs`, `CivilianTkbCatalog.cs` |

Rails: `AreaEffectCloudTests` (10: sight through / beside / over / thinning, two clouds multiply, drift once a second, no weather, irritation
once a second inside only, people not vehicles, smoke irritates nobody, ends at its time, a cloud this node does not own is left alone, the
burst publishes the creation request) · `NonLethalMunitionTests.CE3219_…` (each cloud round over the built-in TKB requests its cloud) ·
`BodyPartReplicationTests.CE3219_…` (every field across the wire) · `EntityOrdersAcrossNodesTests.CE3219_…` (asked for on SimHost ⇒ forwarded,
created and owned by the default processor CGF ⇒ on SimHost and the IG as replicas ⇒ gone everywhere at its time).

⛔ **SUPERSEDED `2026-10-17` (same day, before any push), twice:** ① the first build had an `AreaEffectCloudRequest` toolkit event (5011)
and a Hrot `AreaEffectCloudSpawnSystem` feeding the creation-request queue, and drifted every tick — rejected by the user (two steps,
every-tick drift); event id 5011 was never pushed. ② the second published the `SpawnEntityCommand` ORDER with a new `ThisNode` (−1) owner —
rejected: the order skips the routing and the load-balanced grants, and *"no valid node ID should be 0"* makes owner 0 free to mean "the
system picks". Reverted; the cloud asks with THE creation request (`CE-3228`).

⚠ **Limits, stated:** a scenario with no `TerrainWorld` has no world query, so its clouds do not block sight (every sight caller already
needs a terrain). The canister is data only: no launcher loads it yet (which round a launcher fires is the weapon brain's choice —
behaviors). The drift is written by the cloud's primary owner. ⭐ A cloud's TKB type is bare, so no role group applies (R-175) and its
position is never granted away — the default processor that creates it drifts it. ⚠ Not railed across nodes: the cross-node rail checks
creation, ownership and the end, not the drift (`AreaEffectCloudTests` rails the drift on one node). ⭐ A cloud asked for on a node that
is not the default processor crosses the wire inside the forwarded request (`dtAreaEffectCloud` in the request's descriptor union —
Entity Creation §3.4d ③); it is created `AllPeers` there (the request's `InitType` does not travel).

⛔⛔ **Defect found and fixed `2026-10-17` — `CE-3239`: a cloud round never burst on the GROUND.** 📐 Measured in the B-13 round-lifecycle
sweep: `FireProcessingSystem` set a round's `Area` bit only for `WarheadDto.HasAreaEffect` (blast or fragments), and only an `Area` round
bursts when the flight stops it — so a canister was destroyed where it landed (`BallisticsSystem`'s stopped-round grace) and a gas grenade that
landed before its 1.5 s fuze lay 8 ticks and vanished. ⚠ The rails above published the burst by hand, so none saw it. ⭐ Now
`WarheadRound.BitsOf` (`Combat/Components/CombatComponents.cs`) is the one derivation of a fired round's bits and sets `Area` for `HasCloud`
too; the burst asks for the cloud through `AreaEffectSystem.RequestCloud` as drawn, and is still not assessed as a blast. Rails
`WarheadFlightTests.CE3239_…` (the fuzed grenade bursts on its fuze where it lies; the canister where it lands) and
`NonLethalMunitionTests.CE3239_…` (every built-in cloud round flies as an area round). ⚠ A gas burst publishes a `DetonationNotification`
like any area round — the effects layer draws what it draws for one (ui's to decide).

#### 7.3d Gas as a DANGER AREA — and what people do about it *(`CE-3219` part 4; approved `2026-10-17`; build-state: DESIGN)*

🔒 User, `2026-10-17`: *"avoidance via brain yes if sensor can find a cover point and the person can still see (usually not if hit by tear
gas) so the gas is something like a dangerous area to be avoided by not stepping into and rather taking cover farther from its current or
future expected location"* · *"Danger area kind for gas seems okay."* · *"Behaviors accepted."*

📐 INVENTORY (`search_graph name_pattern=".*(DangerAlongRoute|DangerZone|DangerArea|AvoidArea|Hazard)[A-Za-z]*$"`, Class: 29, Interface: 1,
`has_more:false`): the danger-area machinery EXISTS — `IDangerAreaProvider`, `DangerAreaSensorSystem`, `DangerAlongRouteClassifier`,
`DangerAreaKind` (*"Extensible: new kinds may be added"*), `DangerAreaKindsOf.Wants` (stops at `CrestLine`), `DangerAreaCrossingManeuver`,
behaviors' `DangerAreaNodes`. ⇒ gas is a new KIND in it, never a second danger mechanism. ⚠ That machinery is built for areas to CROSS
(a bound from `NearSideHandle` to `FarSideHandle`); gas is an area NOT to enter — the kind is what lets a behaviour choose differently.

```mermaid
classDiagram
  direction LR
  class DangerAreaKind { <<existing enum, grows>> + GasCloud }
  class DangerAreaKindsOf { <<existing>> Wants: + GasCloud bit }
  class DangerAlongRouteClassifier { <<existing, grows>> + a cloud crossing the route corridor, now OR where the wind takes it within T s }
  class AreaEffectClouds { <<existing>> Of(view) · + Predicted(cloud, position, wind, T) }
  class DangerAreaDescriptor { <<existing>> Center · ExtentsXY · NearSideHandle FarSideHandle = detour points }
  class EffectState { <<existing>> Irritated: the person cannot see }
  class GasReactions { <<behaviors lane>> avoid · escape blind · mask drill · crowd scatter }
  DangerAlongRouteClassifier ..> AreaEffectClouds
  DangerAlongRouteClassifier ..> DangerAreaDescriptor : Kind GasCloud
  GasReactions ..> DangerAreaDescriptor
  GasReactions ..> EffectState
```
*What the picture shows that prose hid:* the backend adds ONE kind and one predicted footprint; every reaction reads data that already
replicates (the danger-area result, `EffectState`). No navmesh change.

| situation | the expected behaviour *(behaviors lane)* | reads |
|---|---|---|
| outside, gas on or near the route | do not step in; detour or wait; take cover upwind / crosswind, beyond where the cloud will be in 10–20 s | the `GasCloud` danger area (its footprint already includes the drift) |
| caught inside, no mask | eyes shut: NO cover search — move blind out of the cloud, upwind and away from its centre; seek cover only once sight is back | `EffectState` Irritated · the wind |
| masked (riot police, prepared troops) | a masking drill (~8 s), then continue; not irritated, narrower vision | a gas-mask TKB entry *(backend, later)* |
| a crowd | break and flee away from the cloud and the launcher; panic spreads | the danger area · the CivilianHsm |
| smoke (not gas) | cover, not danger: cross open ground behind it; the enemy fires blind into it | sight only (already built) |

| decision | ⭐ lean | rejected — one line each |
|---|---|---|
| where avoidance lives | a `DangerAreaKind.GasCloud` reported by the existing danger-area sensor | a new EQS "outside the cloud" test: a second danger mechanism · a navmesh cost: a rebake each time a cloud moves |
| the footprint | the cloud now ∪ its centre moved by `WindAt × T` (T ≈ 15 s), radius + a margin | the current cylinder only: the cloud drifts onto the chosen cover |
| a gassed man | no cover search; escape blind | a normal cover search: he cannot see (user) |

⏳ Backend half (`CE-3227`): the kind, the predicted footprint, the classifier. Behaviors half: the reactions — 📨 [frame](blueprints/batches/HANDOFF_Backend_To_Behaviors_Gas_Reactions.md).

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

✅ **The gas grenade, the gas canister and a smoke grenade are BUILT** with the cloud (§7.3c, `2026-10-17`). ⏳ The Molotov still waits:
its fire is the same kind of lasting area, with a burn effect no cloud has yet. The stone has no thrower yet: a civilian picks one up through `PersonCarrier`, which is
behaviors' CE-2236. The baton blow is behaviors' melee node (D5).

### 7.7 B-6 — the gesture *(CV-13, R-2024; `CE-3224`; [frame](blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians_Gestures.md))* — ✅ BUILT `2026-10-17`

```mermaid
classDiagram
  class Gesture { <<NEW component 398>> GestureKind Kind }
  class Gestures { <<NEW>> Set(repo, unit, kind) · Of(view, unit) }
  class EntityGesture { <<NEW wire, dtGesture 143>> EntityId · byte Kind }
  class EntityGestureEgressTranslator { <<NEW, shared pack>> on change, from the dtGesture owner }
  class EntityGestureIngressTranslator { <<NEW, shared pack>> skips the recorded owner }
  class CivilianHsm { <<behaviors, CE-2240>> }
  class AimAndFireExecutor { <<behaviors, CE-2241>> hands up holds fire }
  class FigurePose { <<ui>> }
  CivilianHsm ..> Gestures : Set
  AimAndFireExecutor ..> Gestures : Of
  FigurePose ..> Gestures : Of
  Gestures ..> Gesture
  EntityGestureEgressTranslator ..> EntityGesture
  EntityGestureIngressTranslator ..> Gesture
```
*The frame's fences hold:* not in `EffectState` (that is what was done TO a man), not a stance (locomotion), no prop entity. Brain group
(the behaviour that chooses it owns it); recorded like `EffectState` (`NoScenario` only — it reaches the replay, never a scenario
file). Absent ⇒ None, so a unit that never gestures costs nothing. `None` after a gesture is a change and is published.

### 7.9 B-8 — the HANDS mount, and stones and stun grenades on the arc *(AW-7, AW-24 — R-2025, R-2029; `CE-3230`; backend, `2026-10-17`; build-state: BUILT — as-built in §7.9a)*

INVENTORY *(grep + reads at `backend@58d6e7a5c`; codebase-memory CLI as in §7.8)*:

| question | measured | where |
|---|---|---|
| person types | every TKB type whose `VehicleParametersDto.VehicleClass` is `Pedestrian`: the urban catalog (civilian pedestrian/child 1001/1003, infantry, grenadier, mortar team, insurgent, the AT men), the civilian catalog (the family + security forces), the BDC builder's infantry, and any TKB FILE the knowledge-base step loads | `UrbanCombatTkbCatalog`, `CivilianTkbCatalog.Person`, `BdcTkbBuilder:541`, `KnowledgeBaseLoadStep` |
| one place every TKB passes through | `HrotEnvironment.RegisterBuiltInTkb` (with a post-pass precedent: `RouteTkbExtensions.ApplyRoutePlanToBlueprint`) and `KnowledgeBaseLoadStep` (file TKB) | `HrotEnvironment.cs:41`, `KnowledgeBaseLoadStep.cs:70+` |
| how a mount becomes state | `CombatTkbTranslator`: mount 0 ⇒ `WeaponState` on the OWNER, mount 1+ ⇒ a child; an `AmmoLoadout` only when the TKB mount lists one | `CombatTkbTranslator.cs:86-140` |
| a civilian today | NO suite ⇒ no `WeaponState` ⇒ the hands mount would be his mount 0, on him | `CivilianTkbCatalog.cs:62` |
| readers that take "a `WeaponState` on the unit" as "he has a weapon" | `PersonBody.PoseOf` (figure armed), `WeaponSystems.WeaponOf`, `Armament.ArmedByStatus`, `Armament.LethalInHand` (`FiresLethal` of a mount naming no round = lethal), the weapon-selection candidates (`UtilityScorer`) | §7.8 table |
| throwing at a point from a LOADOUT | ⛔ `FireAtPointExecutor` sends the mount's own round (`AmmoGuid = 0`), never spends the loaded type, and checks `Indirect` on the mount's own round — a loadout is ignored on this path today | `FireAtPointExecutor.cs:235-260` |
| the hand grenade today | the M67 is thrown as an `Indirect` round on a `Grenade` mount — the arc already works | `UrbanCombatTkbCatalog.cs:223`, `BallisticAim.IsIndirect` |
| replicated per-mount ammo | `WeaponAmmo` (`dtWeaponAmmo`), the breech round of mounts 0…3 | `WeaponAmmo.cs` (`MaxMounts` 4) |

```mermaid
classDiagram
  direction LR
  class WeaponMountDto { <<existing, + Hands bool>> Category Grenade · no AmmoGuid · empty Loadout }
  class PersonHands { <<NEW static, Fdp.Toolkits/Combat>> Mount() the DTO · EnsureOnPersons(db) · IsHands(mount) · HandsOnly(view, unit) · Give(world, owner, ammo, n) · Rounds(world, owner, ammo) · HoldsLethal(world, owner) · MountIndex(world, owner) }
  class CombatTkbTranslator { <<existing>> a Hands mount gets WeaponState 0/0 + an EMPTY AmmoLoadout }
  class AmmoLoadout { <<existing>> up to 4 kinds; Give fills a free slot }
  class FireAtPointExecutor { <<existing, fixed>> sends + spends the LOADED round; Indirect from it; an empty hands slot reloads the next kind at once }
  class Armament { <<CE-3229>> hands judged by what it holds }
  class UtilityScorer { <<existing>> never a hands candidate unless it holds a lethal round }
  class PersonBody { <<existing>> armed pose: not for the hands }
  class WeaponOf { <<existing>> a hands-only unit is unarmed }
  class HrotEnvironment { <<existing>> RegisterBuiltInTkb → EnsureOnPersons }
  class KnowledgeBaseLoadStep { <<existing>> a file TKB → EnsureOnPersons }
  HrotEnvironment ..> PersonHands
  KnowledgeBaseLoadStep ..> PersonHands
  PersonHands ..> WeaponMountDto
  CombatTkbTranslator ..> PersonHands : IsHands
  PersonHands ..> AmmoLoadout
  FireAtPointExecutor ..> AmmoLoadout
  Armament ..> PersonHands
  UtilityScorer ..> PersonHands
  PersonBody ..> PersonHands
  WeaponOf ..> PersonHands
```
*What the picture shows that prose hid:* the hands is an ordinary mount with one flag, so it rides every existing path (the translator, the
loadout, `FireAtPoint`, the ammo mirror, the wire) — and the ONLY new behaviour is in the five readers that would otherwise read an empty
hand as a weapon. The fix to `FireAtPoint` is not hands-specific: a loadout thrown at a point was simply never wired.

```mermaid
sequenceDiagram
  participant B as rioter brain (CGF)
  participant H as PersonHands
  participant F as FireAtPointExecutor
  participant S as SimHost FireProcessing
  B->>H: Give(stone, 3) — B-13's grant, a test today
  H->>H: free slot ← stone ×3, Loaded = it, WeaponState.Ammo += 3
  B->>F: FireAtPoint(point, mount = hands)
  F->>F: loaded = stone (Indirect) ⇒ the arc, no friendly or neutral line hold for an arcing round
  F->>S: WeaponFireIntent { AmmoGuid = stone, AtPoint }
  F->>F: Magazine.Spend + SpendLoaded ⇒ 2 left
  Note over F: a slot emptied ⇒ the next kind with rounds is loaded at once (a hand has no breech)
```
*What the picture shows that prose hid:* the round that flies, the round that is spent and the round whose warhead decides "arc or
straight" are now the SAME round — before, a loadout thrown at a point flew its mount's default and spent nothing of the loadout.

| id | decision *(backend lean, inside AW-7 / AW-24)* | rejected — one line each |
|---|---|---|
| **H-1** | **the hands is a TKB mount with `Hands = true`, appended LAST by one pass** `PersonHands.EnsureOnPersons(db)` over every `Pedestrian` type, idempotent — run by `RegisterBuiltInTkb` and after a FILE TKB loads; a type already ending in a hands mount is left alone | a call in every person template: misses the BDC builder's and every file TKB · a synthesized mount the TKB never names: `MountOf(index)` would return null and every suite reader would disagree with the runtime |
| **H-2** | **it starts empty**: `Category = Grenade`, no `AmmoGuid`, no `Loadout` entries; the translator gives a hands mount `WeaponState` 0/0 and an EMPTY `AmmoLoadout` (`Count = 0`) that `Give` fills (up to 4 kinds, AW-7) | a fixed 4-kind loadout (stone, stun, gas, grenade): a picked-up type outside the list could not be held |
| **H-3** | **a hands-only unit has NO weapon** (`HandsOnly`: his mount 0 is the hands — exactly the dispenser rule CE-2188): `WeaponOf` ⇒ null, `ArmedByStatus` false, the figure's armed pose false. `LethalInHand` and `Judge` read the hands by WHAT IT HOLDS: empty ⇒ nothing; stones ⇒ a small object, not lethal; a grenade ⇒ lethal (AW-9) | a hands-only civilian read as armed by every "has a WeaponState" reader — the figure would carry a rifle |
| **H-4** | **never CHOSEN as a weapon unless it holds a lethal round** (AW-24): the weapon-selection candidates skip it; an explicit `FireAtPoint` / `AimAndFire` on its index (behaviors' throw) is not affected | rely on "0 ammo scores 0": stones in a soldier's hand have ammo |
| **H-5** | **`FireAtPoint` throws the LOADED round**: it sends it on the intent, spends its count, and takes `Indirect` / the line-of-fire exemption from its warhead; a hands slot that empties loads the next kind with rounds at once (no switch time) | a hands-only fire path: the frame's fence, "no second firing path" |
| **H-6** | **6037 (stun) and 6038 (stone) get `Indirect`** — thrown on the arc like the M67; the security forces' stun grenades arc too | leave them straight: a stone would fly like a bullet (AW-7) |
| ⚠ **H-7** | the hands index is the type's mount COUNT; every built-in person has ≤ 3 weapon mounts, so it is ≤ 3 and `dtWeaponAmmo` (4 mounts) carries what it holds to every node. A rail fails if a person type ever has more | widen `WeaponAmmo` now: no type needs it |

#### 7.9a AS-BUILT `CE-3230` *(2026-10-17)* — built as §7.9 draws it

| what | where |
|---|---|
| `PersonHands` (`Mount`, `EnsureOnPersons`, `IsHands`, `MountIndex`, `HandsOnly`, `MountEntity`, `Give`, `Rounds`, `ReadyNext`, `Holding`, `HoldsLethal`) | `FDP/Toolkits/Fdp.Toolkits/Combat/PersonHands.cs` |
| `WeaponMountDto.Hands` (appended) | `Tkb/Domain/WeaponSuiteDto.cs` |
| the pass, run twice: the built-in catalogue and a file TKB | `HrotEnvironment.RegisterBuiltInTkb`, `KnowledgeBaseLoadStep.PrepareAsync` |
| an empty `AmmoLoadout` for a hands mount | `CombatTkbTranslator.AddLoadout` |
| H-3 readers: `WeaponOf`, `PrimaryMount`, `Armament.ArmedByStatus` / `LethalInHand` / the conduct path (`OfHands`), `PersonBody.PoseOf` | as named |
| H-4: the weapon-selection candidates (self and children) | `UtilityScorer` |
| H-5: `FireAtPoint` sends, spends and judges the LOADED round (`ShootAtPoint`'s friendly / neutral exemptions read that round's warhead) | `FireAtPointExecutor` |
| H-6: `Indirect` on 6037, 6038 | `MunitionTkbCatalog` |
| rails: `PersonHandsTests` (5) · `StarterPackIntegrationTests.CE3230_ARiflemanWithStonesInHisHands_StillFiresHisRifle` · `NonLethalMunitionTests.CE3230_EveryBuiltInPerson_…` (every person ends with one hands mount at index ≤ 3; 6037 / 6038 arc) | toolkit tests · `Hrot.Core.Tests` |

⚠ **Known limits.** ① Replicas see the hands' content only through `dtWeaponAmmo`'s breech round (the loaded kind) — enough for
`Armament.Judge`; the full loadout stays on the brain's node. ② A civilian's hands ARE his mount 0, so `FireAtPoint` on mount 0 throws from
his hands even when he also carries a weapon system (B-11): the throw names the hands' index, a carried weapon its controlled index — B-11
decides whether a carried weapon should take mount 0 first. ③ `Give` is the only writer: the pick-up grant (B-13) calls it.

### 7.6 ui's findings F-1…F-4 — backend's answers *(2026-10-16; [findings](blueprints/batches/HANDOFF_Ui_To_Backend_Civilians_Findings.md))*

| # | finding | answer |
|---|---|---|
| F-1 | a cloud prism would draw as a solid block | ✅ agreed: ui owns the render skip (U4-b). The cloud itself is still not built (B-1 part 3) |
| F-2 | a second `EffectKind` | ✅ already done: the hit's enum is `HitEffect` (`Fdp.Toolkit.Combat/Effects.cs`) |
| F-3 | nothing carries "cowering" | ✅ built as asked: `EffectState.Cowering` (a byte, 0/1), on the wire in `EntityEffectState`. The CivilianHsm (behaviors) sets it through `Effects.SetCowering(repo, e, on)` on the brain owner, the same node that owns `dtEffectState`. ⚠ It is a mode, not a timed effect: it holds until the HSM clears it |
| F-4 | no elderly carrier | ✅ built as asked: the TKB descriptor `PersonDto` (`Body.Person`), holding `Age` (Adult / Child / Elderly) and `Sex` (Unspecified / Male / Female). It is on the four civilian types (male, female, minor, elderly), and the security types say Adult. No descriptor means an adult of unspecified sex, so soldiers need none. Read it with `template.GetDescriptor<PersonDto>()` |

⭐ Ruled `2026-10-16` (R-3013). Tracker: B-1 `CE-3219` · B-2 `CE-3220` · B-3 `CE-3221` · B-4 `CE-3222` · B-5 `CE-3223`.

### 7.8 B-7 — `Armament`, the one armed rule *(AW-15, AW-19, AW-21, AW-24 — R-2026, R-2027, R-2029; `CE-3229`; backend, `2026-10-17`; build-state: BUILT — as-built in §7.8a)*

INVENTORY *(codebase-memory CLI re-indexed at `backend@d84808237`, + grep `HasComponent<WeaponState>` and `WeaponOf(`; `check_index_coverage` is
not reachable through the CLI)*. ⭐ **Behaviors' "27 readers" are 27 READS, and only TWO of them mean "armed".** The rest ask whether a mount
can fire, or how much ammunition it holds, and keep their test:

| the read means | sites | routed? |
|---|---|---|
| ⭐ **is the TARGET armed** (to this observer) | `ThreatDanger.Of:32` — and through it every threat reader (`TopThreat`, ranking, the Sentry, utility inputs, EQS) | ✅ → `Armament.Judge` |
| ⭐ **am I armed** (my own strength) | `ThreatDanger.OwnStrength:243` | ✅ → `Armament.LethalInHand` |
| does he HOLD a weapon (the figure's pose) | `PersonBody.PoseOf:69` | ⛔ unchanged — a police launcher is held too; the figure draws what he holds, not what an observer concludes |
| can this mount fire / what is its ammo | `FireAtPointExecutor` ×3, `AimAndFireExecutor` ×5, `FireProcessingSystem`, `WeaponChoice` ×2, `HiddenTargetThrow`, `WeaponMountQuery`, `WeaponAmmoMirrorSystem`, `UtilityScorer` ×2 (weapon selection), `IndirectFireBehaviors`, `SquadGates` ×2, `SquadInputs.SquadAmmoRollup`, `SquadEventIngressSystem` (shot fired), `StandardInputs` ×4 and `PeekAndFireNodes` / `VehicleFightFromCoverNodes` (via `WeaponOf`: own ammo), `DebugApiService.Orders` (ammo row) | ⛔ unchanged |
| the dispenser and the translator themselves | `Countermeasures.DispenserOnly`, `CombatTkbTranslator` (stamps `WeaponState`) | ⛔ unchanged |

```mermaid
classDiagram
  direction LR
  class Armament { <<NEW static, Fdp.Toolkits/Combat>> Judge(view, observer, man) ArmedJudgement · Judge(world, observer, man, seeing) · IsCombatant(view, man) · ArmedByStatus(view, man) · LethalInHand(view, man) · IsCivilianClass(dis) · ConductJudgements counter }
  class ArmedJudgement { <<NEW enum>> Nothing · Unidentified · IdentifiedLethal }
  class SeeingConditions { <<NEW struct>> RelativeLight · VisibilityM · Optics · Of(world, observer, man) }
  class Identification { <<NEW static>> RangeM(sizeM, seeing) = 300 x size x light x optics, capped by visibility }
  class ItemAppearanceDto { <<NEW TKB, appended to WeaponMountDto>> Look · SizeM · Lethal? · ReachM }
  class ItemAppearances { <<NEW static>> Of(world, mount) resolved, defaulted from Category }
  class ItemLook { <<NEW enum>> None Rifle Pistol LongObject SmallObject }
  class ThreatDanger { <<existing>> Of: status or Judge · OwnStrength: LethalInHand }
  class WeaponOf { <<existing, unchanged>> }
  class DispenserOnly { <<existing, unchanged>> }
  class WeatherField { <<existing, CE-3216>> At(world, p).RelativeLight · VisibilityM }
  class EntityClassTable { <<existing, Hrot.Core>> Civilian = 3.1.*.2 }
  ThreatDanger ..> Armament
  Armament ..> WeaponOf : ArmedByStatus = today's test
  Armament ..> DispenserOnly
  Armament ..> ItemAppearances : conduct path only
  Armament ..> Identification
  Identification ..> SeeingConditions
  SeeingConditions ..> WeatherField
  ItemAppearances ..> ItemAppearanceDto
  ItemAppearanceDto ..> ItemLook
  Armament ..> EntityClassTable : the same mask, a rail keeps them equal
```
*What the picture shows that prose hid:* the status branch and the conduct branch share NOTHING below `Armament` — `ArmedByStatus` is
today's three-line test moved, untouched, and only the conduct path reaches the TKB appearance, the weather and the identification range;
so a soldier against an enemy combatant pays one DIS-type read and one side read (AW-24), and the counter proves it.

```mermaid
sequenceDiagram
  participant R as a threat reader (TopThreat, Sentry, ranking)
  participant TD as ThreatDanger.Of(view, self, target)
  participant A as Armament.Judge
  participant W as WeatherField / ItemAppearances
  R->>TD: danger of target to self
  TD->>TD: out of the fight? 0 · Neutral? 0 (CIV-0, unchanged)
  TD->>A: Judge(view, self, target)
  A->>A: IsCombatant (not 3.1.*.2, side not Neutral)?
  alt combatant (AW-21) — the simple path
    A-->>TD: ArmedByStatus ? IdentifiedLethal : Nothing
  else Civilian class, or a neutral or unknown non-civilian (conduct)
    A->>A: ConductJudgements++
    A->>W: seeing at the man (light, visibility) · his items' appearances
    A->>A: per visible item: in its identification range? looks lethal? within its reach?
    A-->>TD: the strongest of IdentifiedLethal · Unidentified · Nothing
  end
  TD-->>R: IdentifiedLethal ⇒ 1 (or ToMe for a vehicle) · else Unarmed 0.3
```
*What the picture shows that prose hid:* a Neutral still returns 0 BEFORE the rule (CIV-0) — so today the conduct path is reached only by
a Hostile-flipped civilian (or a non-civilian with no side); and `Unidentified` gives the same danger as unarmed, because danger drives
engagement and "never fire on it alone" (AW-15). The ladder (`CE-2239`, behaviors) reads `Judge` directly to warn.

```mermaid
graph TD
  subgraph AnyNode["every node that runs a brain (CGF, SimHost twins, tests)"]
    BR[weapon brain, utility inputs, EQS TopThreat, Sentry] --> TD[ThreatDanger.Of / OwnStrength]
    TD --> AR[Armament - a pure function, no system]
    LAD[CE-2239 ladder - behaviors, later] -.-> AR
  end
  AR --> REP[reads replicated data only: EntityInfo, the DIS header, WeaponState, controlled weapons, TkbIdentity]
  style LAD stroke-dasharray: 5 5
```
*What the picture shows that prose hid:* there is no system to register and no host on which it "never ticks" — `Armament` is called
inside the readers' own frames, on whatever node runs them, and everything it reads is replicated (so every node gives the same answer
for the same observer). The dashed reader is not built yet.

| id | decision *(backend lean, inside the ruled AW rows)* | rejected — one line each |
|---|---|---|
| **A-1** | `Armament` is a static in `Fdp.Toolkits/Combat` beside `WeaponOf` / `DispenserOnly`; the class test is the DIS mask `3.1.*.2` held as a toolkit constant, and a rail asserts it equals `EntityClassTable.Default`'s "Civilian" | reference `Hrot.Core`'s table from the toolkit: the toolkit cannot depend on Hrot |
| **A-2** | **combatant = not Civilian class and side not Neutral** (an entity with no `EntityInfo` is a combatant, as `ThreatDanger` reads it today); ⇒ `ArmedByStatus` = today's test exactly (a weapon not only a dispenser, or a working controlled weapon) | AW-2's non-lethal rule for combatants too: a baton-launcher police unit is SecurityForce class, and AW-24 says the status branch is today's |
| **A-3** | **the items** a man shows are his template's built-in mounts and his controlled weapon systems' mount 0; each has an `ItemAppearanceDto` (named so beside `Fdp.Core.EntityAppearance`, the body colour) on its `WeaponMountDto` — **null ⇒ defaulted from `Category`** (AW-24): SmallArms / MachineGun / Atgm / Sam ⇒ Rifle 1.0 m; Grenade (a thrown item: a grenade, a stone) ⇒ SmallObject 0.15 m; Countermeasure and the vehicle-borne categories ⇒ none; its `Lethal` defaults to "any round it fires is not Blunt / Irritant / Stun / Smoke" | a separate appearance table per type: a second place to keep in step with the mounts |
| **A-4** | **identification range = 300 × size(m) × light × optics, capped by the visibility at the man** — light = 1 at `RelativeLight` ≥ 0.3 (day), falling to 0.1 at night; optics = 1 until B-12's binoculars (`Judge` takes an explicit `SeeingConditions` for them); ⭐ a world with no World Environment (every scenario that predates the clock) reads a CLEAR DAY — the default clock's 06:00 UTC would put a test world without a geo origin at sunrise. ⇒ by day a pistol (0.2 m) is identified at 60 m, a rifle at 300 m; at night a pistol at 6 m; binoculars ×4 identify the pistol at 240 m | Johnson criteria per sensor: the sensors have no optics model yet (CE-3216 S2), and one constant is checkable against the acceptance rows |
| **A-5** | **per item**: hidden ⇒ skipped (B-11's Hidden station); beyond its range ⇒ Unidentified; identified and lethal-looking ⇒ IdentifiedLethal, **but a REACH item (`ReachM` > 0, a knife) only within its reach**, else Unidentified (warn); identified non-lethal ⇒ Nothing. The man's answer is the strongest item's | a knife lethal at any range: AW-19 says within its reach |
| **A-6** | **`Unidentified` reads `ThreatDanger.Unarmed`** (0.3); only `IdentifiedLethal` is danger 1 | Unidentified = armed: AW-15 "never fire on this alone", and danger feeds the Sentry's fire threshold |
| **A-7** | **`OwnStrength` reads `LethalInHand`** (truth: a lethal round on a built-in mount or a working controlled weapon) — a soldier reads exactly as before; a unit with only non-lethal rounds is unarmed to its own strength (AW-2) | own strength by status: a stone-thrower's strength would equal a rifleman's |
| **A-8** | **the counter** `Armament.ConductJudgements` (a process-wide `Interlocked` count of conduct-path entries) is the AW-24 rail's probe; it costs one atomic add, only on the conduct path | a debug flag: the rail must run on production code |

#### 7.8a AS-BUILT `CE-3229` *(2026-10-17)* — built as §7.8 draws it

| what | where |
|---|---|
| `Armament` (`Judge` ×2, `IsCombatant`, `ArmedByStatus`, `LethalInHand`, `IsCivilianClass`, `OfItem`, `ConductJudgements`), `ArmedJudgement`, `SeeingConditions`, `Identification`, `ItemAppearance` + `ItemAppearances` | `FDP/Toolkits/Fdp.Toolkits/Combat/Armament.cs` |
| `ItemAppearanceDto` + `ItemLook`, appended to `WeaponMountDto.Appearance` (null = by category) | `Tkb/Domain/WeaponSuiteDto.cs` |
| `ThreatDanger.Of` → `Judge` (A-6); `OwnStrength` → `LethalInHand` (A-7) | `Perception/ThreatDanger.cs` |
| rails: `ArmamentTests` (9, a non-parallel collection because the counter is process-wide) · `EntityClassTableTests.CE3229_…` (the mask equals the table's "Civilian") | toolkit tests · `Hrot.Core.Tests` |

⚠ Not yet reachable, by design: a pistol, the hands mount, Hidden stations, binoculars and laid-down weapons are B-8…B-12 — `Judge` sees
today's items (built-in mounts and controlled weapon systems) and takes optics through `SeeingConditions`. The live `ua-armed-status`
scenario (§5c) does not exist yet; its in-process twin is `ArmamentTests.CE3229_AnEnemyRiflemanAtNight_…` (status at 300 m at night,
danger 1, the counter unmoved).

### 7.10 B-13 — picking a round up, stone sources, a stone that lies *(AW-8, AW-9 — R-2025; `CE-3235`; backend, `2026-10-17`; build-state: BUILT — as-built in §7.10a)*

INVENTORY *(grep + reads at `backend@397502d14`; codebase-memory CLI `search_graph` `.*PickUp.*` / `.*Pickup.*` (30: team-carry `PickUp*`, the
pickup truck — no round pick-up), `.*InteractionKind.*` (1: `EInteractionKind`), `.*StoneSource.*` (0), `.*Zone.*` / `.*Region.*` (Class))*:

| question | measured | where |
|---|---|---|
| what ends a round that stops | a NON-area round: frozen at the stop, destroyed after `StoppedRoundGraceTicks` (8); an area round on an impact fuze bursts after `ContactHitWaitTicks`; on a time fuze it is marked `Landed` and waits for its fuze | `BallisticsSystem.cs:93-122` |
| `Landed` today | only an area round on a time fuze; the `Landed` branch always DETONATES when `FuzeRemaining` runs out | `BallisticsSystem.cs:93-101, 226-229` |
| a round that hits a unit | goes to `TearDown` and is destroyed — it does not fall | `HitResolutionSystem.cs:181-188` |
| who sees a round | only a munition with a `MunitionVisualDto` becomes a LIGHTWEIGHT replicated entity (`TkbIdentity` + `WorldPos`, no lifecycle; the master's dispose removes the replicas). ⛔ Stone, stun grenade, tear-gas grenade, canister and smoke have NO look ⇒ the brain's node never sees them | `FireProcessingSystem.MarkIfVisible:396`, `EntityMasterIngressTranslator.cs:214-227`, `MunitionTkbCatalog.BuiltInLooks` |
| lying, on a viewer | a replica is lying when its speed < 0.2 m/s | `ProjectileLayer.cs:150` |
| asking the owner of another entity | ⭐ I-1's ONE interaction transport: a codec per kind (event ↔ payload case + who owns the target); egress sends what this node does not own, ingress publishes it remote; "a new interaction kind adds its owner-side system" to the `Interactions` capability (every node) | `InteractionTransport.cs` (`InteractionCodecs.All`, 8 kinds), `CoreInfrastructureCapabilities.Interactions` |
| who owns a brain / a round | `PossessionSystem.OwnsBrain` (authority over `BehaviorState`); a round is created by `FireProcessingSystem` on the node that runs `CombatModule` (SimHost) | `Possession.cs:268`, `CombatModule.cs:37-45` |
| what the hands hold, and where | `PersonHands` (B-8): the `AmmoLoadout` on the hands mount, written by `Give` on the node that runs the brain; ⛔ not on the wire (only the breech round, `WeaponAmmo`) | §7.9a |
| a fire intent's fuze | ⛔ none: `FireProcessingSystem` takes `FuzeDelayS` from the warhead; the intent wire (`WeaponFireRequest`) carries `Charge`, `HighArc`, `RaisedAt` | `FireInteractionMessages.cs:32-59` |
| an authored AREA of its own kind | ⭐ prior art: `TerrainZone` 8804 and `WeatherRegion` 8811 — an area entity (`EditablePolyline` + `SimTransform`) born as its OWN type, replicated, found by `WeatherField.TryOutline` | `TkbEntityTypes.cs:104`, `WeatherField.cs:200-243`, `PlacementToolRegistry.cs:49` |

```mermaid
classDiagram
  direction LR
  class HandItemDto { <<NEW TKB descriptor on a munition>> LiesSeconds · present = a hand may pick one up }
  class WarheadRound { <<existing bits, + Lingers 32>> BitsOf sets Lingers + the lying time }
  class BallisticsSystem { <<existing>> a stopped LINGERING round lies Landed for LiesSeconds, then goes without a burst }
  class MunitionVisualDto { <<existing>> a look on every throwable ⇒ it replicates }
  class RoundPickUp { <<NEW static, Fdp.Toolkits/Combat>> ReachM 1.5 · IsLying(world, round) · CanAsk(world, man, round) · Ask(world, man, round) · OwnsRound(world, round) }
  class PickUpRoundEvent { <<NEW event>> Round · Man · IsRemote }
  class PickUpGrantEvent { <<NEW event>> Man · Ammo · Rounds · FuzeSeconds · IsRemote }
  class RoundPickUpSystem { <<NEW, Interactions capability>> owner of the round: check, grant, destroy · owner of the brain: Give }
  class PersonHands { <<CE-3230, + live round>> Give · HeldFuze(world, man) }
  class HeldFuze { <<NEW component on the hands mount>> Ammo · Remaining }
  class HandsFuzeSystem { <<NEW, Interactions capability>> burns a held live round; at 0 it is thrown at his feet }
  class WeaponFireIntent { <<existing, + FuzeSeconds>> 0 = the warhead's own }
  class ItemSourceDto { <<NEW TKB descriptor>> Ammo · UpTo }
  class ItemSources { <<NEW static>> At(world, point) · Refill(world, man) }
  class StoneSource { <<NEW TKB type 8812, an area>> carries ItemSourceDto stone x5 }
  class InteractionCodecs { <<existing>> + PickUpRound (9) · PickUpGrant (10) }
  HandItemDto ..> WarheadRound : BitsOf
  WarheadRound ..> BallisticsSystem
  RoundPickUp ..> PickUpRoundEvent : publishes
  InteractionCodecs ..> PickUpRoundEvent
  InteractionCodecs ..> PickUpGrantEvent
  RoundPickUpSystem ..> PickUpRoundEvent : reads where it owns the round
  RoundPickUpSystem ..> PickUpGrantEvent : publishes · reads where it owns the brain
  RoundPickUpSystem ..> PersonHands : Give
  PersonHands *-- HeldFuze
  HandsFuzeSystem ..> HeldFuze
  HandsFuzeSystem ..> WeaponFireIntent : cook-off
  StoneSource *-- ItemSourceDto
  ItemSources ..> ItemSourceDto
  ItemSources ..> PersonHands : Give
```

*What the picture shows that prose hid:* **no new transport, no new component on the wire for the pick-up** — the request and the grant are two
cases of I-1's one interaction message, both handled by ONE system that every node runs (each half gates itself on what it owns). The only
new replicated state is the munition looks (so the brain can SEE a lying stone) and one field on the fire request (the fuze left).

```mermaid
sequenceDiagram
  participant B as brain node (CGF) — man's brain + hands
  participant T as I-1 transport
  participant S as round owner (SimHost)
  participant V as every viewer
  Note over S: a stone stops: frozen 8 ticks, then Landed for LiesSeconds (60 s)
  S->>V: the stone replicates (it has a look) — lying = speed 0
  B->>B: behaviour: CanAsk (lying, in reach, room in the hands) ⇒ Ask
  B->>T: PickUpRoundEvent (round not owned here ⇒ sent)
  T->>S: kind 9, round + man as network ids
  S->>S: RoundPickUpSystem: alive · Landed · not Spent · Throwable · man alive · within 1.5 m
  S->>S: grant: PickUpGrantEvent (ammo, 1, fuze left) · destroy the round (a second ask finds it gone — one winner)
  S->>V: the master's dispose removes the replicas
  S->>T: PickUpGrantEvent (man not owned here ⇒ sent)
  T->>B: kind 10
  B->>B: RoundPickUpSystem: PersonHands.Give, a live round keeps its fuze (HeldFuze)
  Note over B: thrown: FireAtPoint sends the intent with FuzeSeconds = what is left · or the fuze runs out in hand ⇒ thrown at his own feet
```

```mermaid
graph TD
  subgraph every node
    I[Interactions capability] --> R[RoundPickUpSystem]
    I --> H[HandsFuzeSystem]
  end
  subgraph SimHost
    C[CombatModule] --> F[FireProcessingSystem]
    C --> BS[BallisticsSystem]
  end
  R -. "owner of the round: grant + destroy (SimHost)" .-> BS
  R -. "owner of the brain: Give (CGF)" .-> P[PersonHands]
  H -. "cook-off intent" .-> F
  X[ItemSources.Refill]:::lib -. "called by the behaviour on the brain node" .-> P
  classDef lib fill:#eee,stroke:#999
```

*Caption:* the two halves of `RoundPickUpSystem` tick on DIFFERENT nodes — the check on the round's owner, the `Give` on the brain's. Both are
in the `Interactions` capability, which every node builds, so in a one-node world (the in-process rails) both run in one frame-chain.
`ItemSources.Refill` is a library call, no system: a stone source is unlimited, so there is nothing to arbitrate.

| id | decision *(lean, backend's own — no CV is reopened)* | rejected — one line each |
|---|---|---|
| **PU-1** | **a lying round is the round itself, kept**: `HandItemDto.LiesSeconds` (stone 60) ⇒ `BitsOf` sets `WarheadRound.Lingers` and the lying time; a stopped lingering round lies `Landed` after the 8-tick grace and goes WITHOUT a burst when its time is up (the `Landed` branch detonates only an area round) | a new "loose item" entity: a second representation of the same thing, and a hand-off at the moment it stops |
| **PU-2** | **what a hand may pick up is data**: a `HandItemDto` (`Munition.HandItem`) on the MUNITION type — stone, stun grenade, M67, tear-gas grenade, canister, smoke | a flag on `WarheadDto`: the M67's warhead comes from the reference library, not the TKB (`ParameterResolver.Warhead:283`) · every `Indirect` round: a mortar bomb is indirect · by category: a round has no weapon category |
| **PU-3** | **a throwable has a LOOK** (`MunitionVisualDto`, the existing grenade shape: a stone a grey lump 8 cm, a canister a can) ⇒ it replicates as a lightweight round, so the brain's node sees it lying; no ui change (the grenade shape is drawn already) | a pick-up list replicated by SimHost: a second channel for what the round entity already is |
| **PU-4** | **request and grant are two I-1 kinds** (9 `PickUpRound`: target = the round, its owner applies · 10 `PickUpGrant`: target = the man, his brain's owner applies); **one winner by construction** — the owner destroys the round on the first valid ask, a later ask finds it gone; no "denied" message (the asker sees the round gone and his hands unchanged) | a reply topic: I-1 has none and the target's replicated state is the reply · a lock on the round: two writers |
| **PU-5** | **the owner checks what it can see**: round alive, `Landed`, not `Spent`, `Throwable`; man alive; within **1.5 m** (embark's reach). ⚠ It cannot see his hands (not on the wire) ⇒ the ASKER checks room first (`CanAsk`); a grant that finds the hands full is dropped (stated limit) | the owner checks the hands: it holds a stale copy |
| **PU-6** | **a live round keeps its fuze**: the grant carries the fuze left; the hands hold it (`HeldFuze`, one live round at a time); thrown, the intent's new `FuzeSeconds` flies it with what is left; run out in hand, it is thrown at his own feet (the ONE firing path) | a fresh fuze on the throw: a cooked grenade is the throwback's whole risk · an in-hand burst published by the brain node: a second detonation path |
| **PU-7** | **a stone source is an AREA of its own type** (8812, "Stone Source", like Weather Region) whose TKB template carries `ItemSourceDto { Ammo = stone, UpTo = 5 }`; `ItemSources.Refill(world, man)` tops his hands up to `UpTo` when he stands inside one, locally on the brain's node (unlimited ⇒ nothing to arbitrate); the 1.5 s bend is the behaviour's | a flag on the tactical area: every area reader would have to skip it (Terrain Zones §2.1) · stones from the terrain material: no terrain stores loose stones (AW-8) |

⚠ **Known limits, stated before the build:** a round that HITS a man ends (it does not fall at his feet) · a stone thrown onto a roof lies on
the roof · the canister bursts where it lands (CE-3239) until B-14 makes it vent, so it is not yet pickable · a grant that finds the hands full
is lost · one live round in the hands at a time.



#### 7.10a AS-BUILT `CE-3235` *(2026-10-17)* — built as §7.10 draws it

| built | where |
|---|---|
| `HandItemDto` (`Munition.HandItem`, `LiesSeconds`) · `ItemSourceDto` (`Area.ItemSource`, `Ammo`, `UpTo` 5) | `Fdp.Toolkits/Tkb/Domain/HandItemDto.cs` |
| `WarheadRound.Lingers` (32) and `BitsOf(warhead, hand, out fuze)` — an inert hand item's lying time rides as its fuze; `CombatTkb.HandItemOf` | `CombatComponents.cs`, `ArmorModel.cs` (CombatTkb) |
| a stopped LINGERING round lies `Landed` after the 8-tick grace; the `Landed` branch bursts only a non-lingering round | `BallisticsSystem.cs` |
| `PickUpRoundEvent` (5011) · `PickUpGrantEvent` (5012) · `HeldFuze` (component 402, `NoScenario`) · `RoundPickUp` (`OwnsRound` = this node holds its `BallisticProjectile` · `IsLying` · `IsHandItem` · `InReach` 1.5 m / ±2 m · `CanAsk` · `Ask` · `TryGrant` · `Receive` · `FuzeLeft` · `Thrown`) | `Combat/RoundPickUp.cs` |
| `RoundPickUpSystem` (the flier grants and DESTROYS the round — the master's dispose removes the replicas; the brain owner receives) · `HandsFuzeSystem` (a held live round burns; at 0 it is thrown at his feet, 0.5 m ahead, with a 0.05 s fuze, through `WeaponFireIntent`) — both in the `Interactions` capability | `Combat/Systems/RoundPickUpSystem.cs`, `CoreInfrastructureCapabilities.Interactions` |
| `PersonHands.HasRoom` · `PersonHands.Take` | `PersonHands.cs` |
| `WeaponFireIntent.FuzeSeconds` + the request wire (`WeaponFireRequest.FuzeSeconds`, appended); `FireProcessingSystem` flies a time-fuzed round with it; `FireAtPointExecutor` sends a held live round's fuze left and drops the `HeldFuze` once thrown | `WeaponFireEvents.cs`, `FireInteractionMessages.cs`, the two SimHost fire translators, `FireAtPointExecutor.cs` |
| interaction kinds 9 `PickUpRound` / 10 `PickUpGrant` + payload cases; codecs (`OwnsRound`, `PossessionSystem.OwnsBrain`); events registered on every host | `InteractionMessages.cs`, `InteractionTransport.cs`, `HrotSharedComponentRegistry.cs` |
| built-in data: hand items stone (lies 60 s), M67, stun, tear-gas grenade, canister, smoke — each with a look (grenade shape) | `MunitionTkbCatalog.HandItems`, `BuiltInLooks` |
| `ItemSources.At` / `Refill` (the area found by `WeatherField.TryOutline`, even-odd inside test) · the **Stone Source** type 8812 (`ItemSourceDto` stone ×5) · its placement tool (ui's `PlacementToolRegistry`, one row — a cross-lane edit, named in the commit) | `Combat/ItemSources.cs`, `TkbEntityTypes.cs`, `BdcTkbCatalog.cs`, `PlacementToolRegistry.cs` |

Rails: `RoundPickUpTests` (7: lingers vs bursts · a stone lies its minute then goes without a burst · ask → grant → hands · two asks, one winner ·
out of reach / still flying / not a hand item refused · a live grenade cooks off at his feet · a held round thrown with its fuze left) ·
`NonLethalMunitionTests.CE3235_…` (2: the built-in hand items are seen, a stone lies a minute · a man in a Stone Source is topped up to 5) ·
`InteractionTransportTests.CE3235_…` (the ask and the grant cross as their union cases) · ⭐ integration
`EntityOrdersAcrossNodesTests.CE3235_AStoneLyingOnSimHost_IsPickedUpByABrainOnCgf_Once` (SimHost's lying stone seen on CGF, asked twice,
granted once, gone everywhere, one stone in his hands).

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

### 8.6 AS-BUILT — U-1 and U-2 (`CE-1162`, `CE-1163`, `2026-10-16`, ui; elderly + cower + B-5 arming `2026-10-17`)

| decision | as built | deviation, and why |
|---|---|---|
| U1-a | `VisualFamilies.PersonKitOf(t)` walks the class chain: `SecurityForce` ⇒ `PersonKit.Security`, `Infantry` ⇒ `Soldier`, else `Civilian`; `TypeLook.Kit`; `IsSoldier` (the rifle) = a soldier, or an officer whose type has a **held** weapon — a mount whose `Category` is not `Grenade` | ⚠ **corrected `2026-10-17`** after backend's B-5 loadouts: the shield and baton men carry only stun grenades (thrown, not held) ⇒ unarmed; the launcher man's baton launcher (`SmallArms`) ⇒ armed. ⛔ SUPERSEDED: *"an officer whose type has a weapon suite"* — it armed every officer once B-5 landed |
| U1-a (outfit) | `Outfits.For(…, kit)` — Security wears `Outfits.PoliceUniform` (navy), trousers darker, no camouflage whatever his side, the helmet of the uniformed | — |
| U1-b | ✅ `2026-10-17`: `TypeLook.Elderly` = a person whose type's `PersonDto.Age` is `Elderly` (backend F-4); `Outfits.For(…, kit, elderly)` dresses an elderly civilian adult in grey/white hair (a man often balding), a muted top, trousers or a long skirt — never shorts | — (⛔ SUPERSEDED: *"not built until a TKB age carrier exists"*) |
| U1-c | nothing to build: the picker folders come from backend's rows; backend's own `EntityTypeCatalogTests` rows cover it | — |
| U2-a | `AffiliationColors` (Hrot.Common): `For(view, entity)` = the `ResolvedStyle` tint, else the live `EntityInfo.ForceId` from `ResolvedStyleConstants`, else `NoSide` (the old cyan); `EntityPresentationGizmo` passes it to `DrawSemanticShape(…, colour)` on every host; `SidePalette` (the card) and `EntityPlacementGizmo`'s private palette now read the same table | ⚠ the card frame's colours change to the authoritative table (pure blue / red / green): Symbology §3.2 names that table, and two palettes would disagree |
| U2-b | 🔴 **withdrawn** — `IgSymbolOverride.StyleSetId` keeps winning on the IG (it is an operator's or the exercise's deliberate choice, §8.4's measured row) | the lean assumed nobody set it on purpose; measured, two producers do |
| U2-c | no 3-D change (as leaned) | — |

Rails: `EntityPresentationGizmoTests.CE1163_…` ×3 (the live side and its flip, a resolved tint wins, one palette) · `Map3DTests.CE1162_…`
(the kit from the class; police uniform on every side; civilians in clothes; every infantryman still a soldier; a grenade-only officer
unarmed, the launcher man armed) and `CE1162_AnElderlyType_…` (grey hair, no shorts, on 40 seeds).

**U-3 (`CE-1164`, `2026-10-16`) as built:** `Carriage.Of` returns `Downed` when `Effects.IsKnockedDown` (backend's `EffectState`, CE-3219) —
so a knocked-down man lies like a man down at full health and stands when it runs out (U3-a, through the carriage that already picks
`Downed` for the incapacitated, not a second pose path in `FigurePose`). `EffectStateGizmo` (Hrot.Common, auto-discovered, Labels layer)
writes `DOWN · STUNNED · GASSED · COWERING` under him in 2-D (U3-c). ✅ **Cower built `2026-10-17`** on backend's F-3
(`EffectState.Cowering`): `EntityBodyLayer3D.Cowers(world, e, soldier)` (an unarmed man with `Effects.IsCowering`) makes `FigurePose`
return `BlockFigure.Cower` — the crouched body with both forearms over the head — after the death fall and before the stance; a soldier
never cowers in the drawing; the knock-down (a carriage) still wins. Rails `Map3DTests.CE1164_AKnockedDownMan_…` and
`CE1164_ACoweringCivilian_…`, red-proved.

**U-4 (`CE-1165`) part built `2026-10-16` — U4-b, ahead of B-1's cloud:** `TerrainWorldMesh.IsVolumeEffect(prism)` (a material with
`SightTransmittance > 0` and `BlocksMovement = false`) keeps such a prism out of the mesh — so it neither renders as a block nor enters the
navmesh (it blocks nothing; this also answers backend's M-4 for walk-through prisms) — and `TerrainWorldGizmo` skips it in 2-D. Sight still
reads the prism (B1-c unchanged). ⚠ Cross-lane: `Fdp.Toolkits/Terrain/TerrainWorldMesh.cs` (backend's area), render/navmesh input only.
Today's see-through materials (glass, hedge, chain-link) all block movement ⇒ nothing existing changes. Rail
`TerrainWorldTests.CE1165_…`, red-proved. ⏳ U4-a (the cloud drawn in `EffectLayer`) waits on `AreaEffectCloud`.


### 8.7 U-6 — gestures and the placard *(ui, `2026-10-17`; frame [HANDOFF_Behaviors_To_Ui_Civilians_Gestures.md](blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians_Gestures.md), CV-13 R-2024; item `CE-1166`)*

**INVENTORY** *(2026-10-17; codebase-memory CLI `search_graph` `.*Gesture.*` (40 results) and `.*Placard.*|.*Sign.*Pole.*` (0) + grep
`Arms(` in `ShapeKit.cs`; `check_index_coverage` NOT run — CLI only)*:

| surface | exists | where |
|---|---|---|
| a body-gesture anything | ⛔ **none** — all 40 `Gesture` hits are EDITOR input gestures (variable edit, watch pinning) | — |
| backend's `Gesture` component (B-6) | ⏳ **not built** at `ui@21a0e3498` (frame dispatched to backend the same day) | — |
| arm overrides on a figure | `BlockFigure.CarrierArms` (C-1), `HandleArms` (T-b3), `AtWeapon` (N-2), `Shouldered` (N-4), `Cower` (U-3) — each replaces the four arm parts at `TorsoIndex+2…+5` and slings a soldier's rifle at `WeaponIndex` | `ShapeKit.cs:1055,1081,1104`, `ShapeKit.cs:873` |
| where an override is applied | `EntityBodyLayer3D.Burdened` (standing only; carriage arms) after `FigurePose` | `EntityBodyLayer3D.cs:660-680` |
| 2-D state text | `StanceGizmo`; `EffectStateGizmo` (U-3c, DOWN · STUNNED · GASSED · COWERING) | `EffectStateGizmo.cs` |

```mermaid
classDiagram
  direction LR
  class Gesture { <<backend B-6, NOT YET BUILT>> None HandsUp FistRaised HoldSign Waving Throwing }
  class ArmGesture { <<NEW, ui>> the same six names, byte for byte }
  class BlockFigure { <<existing, grows>> +GestureArms(parts, ArmGesture, crouched, soldier) KitPart[] ; +Placard KitPart[] }
  class EntityBodyLayer3D { <<existing, grows>> Gestured(world, e, parts) after Burdened }
  class EffectStateGizmo { <<existing, grows>> + HANDS UP / PROTEST text }
  class CarrierArms { <<existing>> the precedent: replace the four arm parts }
  EntityBodyLayer3D --> BlockFigure : GestureArms
  EntityBodyLayer3D ..> Gesture : reads (when B-6 lands)
  Gesture ..> ArmGesture : cast, a rail pins the names
  BlockFigure ..> CarrierArms : same shape
  EffectStateGizmo ..> Gesture : reads (when B-6 lands)
```
*Caption:* what the picture shows that prose hid — the drawing vocabulary (`ArmGesture`) is ui's own and is built now; the ONLY edge that
waits on backend is the read of `Gesture`, and a rail pins the two enums' names so the cast cannot drift.

```mermaid
sequenceDiagram
  participant L as EntityBodyLayer3D (every map host, each frame)
  participant F as FigurePose
  participant B as Burdened
  participant G as Gestured
  participant K as BlockFigure
  L->>F: parts for the stance (dead fall, cower win first)
  L->>B: carriage arms (a carrier does not gesture)
  L->>G: free, standing or crouched, alive, not cowering
  G->>G: ArmGesture from Gesture (None draws nothing new)
  G->>K: GestureArms(parts, g, crouched, soldier)
  K-->>L: the four arm parts replaced, rifle slung, HoldSign adds the pole and the board
```

```mermaid
graph TD
  MIP[MapInteractionPack, every map host] --> BODY[EntityBodyLayer3D.Draw each frame]
  BODY --> FP[FigurePose]
  BODY --> BUR[Burdened]
  BODY --> GES[Gestured NEW]
  SGS[StatelessGizmoSystem, every host] --> ESG[EffectStateGizmo]
  B6[backend B-6 Gesture component]:::wait -.->|not built: Gestured reads None| GES
  B6 -.-> ESG
  classDef wait fill:#ffd,stroke:#aa0
```
*Caption:* both readers are already called each frame on every map host; the yellow box is the one input that does not exist yet — until it
does, `Gestured` sees `None` and the figure is unchanged.

| # | decision | lean | rejected |
|---|---|---|---|
| **U6-a** | where the override applies | `Gestured` right after `Burdened`: standing AND crouched (the shoulders follow `CrouchDrop`); NOT prone, dead, cowering, knocked down or carrying (those already own the arms — the frame's "EffectState wins") | inside `FigurePose`: it returns before carriage, so a carrier would wave · prone too: a prone man's arms are under him |
| **U6-b** | the vocabulary | ui's `ArmGesture` enum with backend's six names; the wiring is a byte cast and a rail asserts `Enum.GetNames` equal | switch on backend's type directly: blocks the whole build on B-6 · a pose per stance: six × three tables |
| **U6-c** | the shapes | HandsUp — both arms straight up, open · FistRaised — right arm up, the left hanging · HoldSign — both hands up on a pole in front of the face, a board above the head · Waving — right arm up and out, the forearm tilted · Throwing — the wind-up: right hand back above the shoulder, left arm forward; all static (the brain cycles them, the frame says so) | animated waving: the brain toggles it, and an animation per gesture is a second clock |
| **U6-d** | the placard | two appended parts (a `Metal`-role pole and a `Canvas`-role board), appended AFTER every indexed part so no index moves | a prop entity: fenced off by the frame |
| **U6-e** | 2-D | `EffectStateGizmo` adds `HANDS UP` (HandsUp) and `PROTEST` (FistRaised, HoldSign, Waving, Throwing) to its one line | a second gizmo: two lines under one man |
| **U6-f** | a soldier | the gesture still draws (a surrendering soldier raises his hands); his rifle is slung like a carrier's | skip soldiers: CV-13a's surrender is about armed men |

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| an arm override replaces four parts at `TorsoIndex+2` | ✅ `ShapeKit.cs:1081-1094` (`CarrierArms`) | ✅ §8.4 U3-a names the arm-override precedent |
| a carrier never reaches `Gestured` with free arms | ✅ `Burdened` sets carrier arms first (`EntityBodyLayer3D.cs:666`) | ✅ frame: "EffectState wins over a gesture" (cowering / down) |
| appending parts moves no index | ✅ `HeadIndex 5`, `TorsoIndex 4`, `WeaponIndex 10` are all below the arm block's end | ⛔ searched, none found |
| backend's enum names are the frame's six | ⛔ **assumed until B-6 lands** — the names rail fails the build the day they differ | ✅ CV-13 §4 lists the six |

#### 8.7a AS-BUILT — the poses, then the wiring on B-6 (`CE-1166`, `2026-10-17`, ui)

| decision | as built | still to do |
|---|---|---|
| U6-b | `BlockFigure.ArmGesture` — the six names of CV-13; ✅ `2026-10-17` the byte cast from backend's `GestureKind` (B-6, `CE-3224`) and the names rail `CE1166_TheDrawingsGestures_AreBackendsGestures_ByteForByte` | — |
| U6-c | `BlockFigure.GestureArms(parts, gesture, crouched, soldier)` — the five shapes as leaned, static; only the four arm parts at `TorsoIndex+2…+5` change; a soldier's rifle slung (the carrier's sling, shifted by the crouch) | — |
| U6-d | `BlockFigure.Placard(drop)` — the pole (`Metal`) and the board (`Canvas`), appended after every indexed part (`PlacardParts` = 2) | — |
| U6-a | ✅ `2026-10-17` — `EntityBodyLayer3D.Gestured(world, e, soldier, carriage, parts)` after `Burdened` and the shouldered launcher: `Gestures.Of` → `GestureArms` for a FREE (carriage `Free`, wounded included), living, not-cowering man, standing or crouched | the `ua-civ-protest` look on a live cluster (behaviors' CE-2240 writes it) |
| U6-e | ✅ `2026-10-17` — `EffectStateGizmo.Text` adds `HANDS UP` / `PROTEST` (`GestureWord`) after the effects; ⚠ **deviation:** a `[GizmoProjector]` runs only for entities that HAVE its component, so a protester with no `EffectState` never reached the effect gizmo ⇒ a small `GestureGizmo` (`[GizmoProjector(typeof(Gesture))]`) draws the SAME line when there is no `EffectState` (never two lines; one text rule) | — |

No deviation from §8.7's leans. Rails `Map3DTests.CE1166_EachGesture_ChangesOnlyTheArms` (×5, standing and crouched, the placard above the
head, the soldier's rifle slung) and `CE1166_TheShapes_ReadAsTheGesture` (hands up = two hands above the head, a fist = one, the wind-up's
hand behind the shoulder), red-proved with three switches (no override · hands up drawn as a fist · no placard). ⭐ `2026-10-17`, on
B-6: `CE1166_TheDrawingsGestures_AreBackendsGestures_ByteForByte` (red-proved by an extra enum member) and
`CE1166_TheReplicatedGesture_DrivesTheArms_AndThe2DLine` (red-proved: no `Gestured`, no word).
