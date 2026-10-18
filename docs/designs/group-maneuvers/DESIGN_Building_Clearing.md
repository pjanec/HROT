<!--STATUS
state: LIVE
updated: 2026-10-18 (approved; platoon layer, networked reports)
build-state: READY-TO-BUILD — CB-1…CB-18 APPROVED 2026-10-18 (R-2030, R-2031); slice SL-0 first (§7), the room slices wait on backend BR-1 · §5b report life and ageing: leans CB-31…CB-35 awaiting the user (2026-10-18)
current-answer: §3–§5 the diagrams (platoon level) · §5a monitoring (lean, for review) · §6 decisions (approved) · §6a casualties and edge cases · §7 slices · §7a building blocks (the whole list) · §8 demos (designed, the CQB house picture) — §1 doctrine, §2 measured ground
stale-below: nothing.
known-rot: ⛔ 2026-10-18 R-2037 — BR-1's "a read-only query seam the brains can call (like the door queries)" is overturned: the room graph is DERIVED by the survey sensor on the muscle from the building's current geometry — never authored or terrain-provided (R-2038) — and reaches the brain as a BuildingSurvey answer (docs/blueprints/batches/NOTE_Behaviors_To_Backend_Geometry_On_The_Muscle.md). (the first draft's one-squad "support team outside" CB-2 and squad-held room state CB-9 are SUPERSEDED — §⛔ HISTORY)
known-conflict: none — IsolateAndClear (CB-17/CB-18, PL-1) IS the ClearBuilding preset of AttackPositionPlay (docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §10, R-2035); the clearing stays its own order.
related-designs:
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Report_Ageing.md — the BR-3 amendment §5b needs (ObservedAt, Seq, the Status kind)
  - docs/DESIGN_Sensors_And_Doctrine.md — G10: the Reported modality §5b's reported contacts use (CB-33); OWNS TargetMemory's decay and forgetting
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad runner (`SquadManeuverRunner`, `ISquadPlay`) and every built drill; this is one more play on it (kind 5, the existing `StackAndRoomEntryManeuver` stub)
  - docs/designs/group-maneuvers/Squad_Coordination_Design_v1_1.md — §8.6 stack-and-room-entry: the original roles-and-sectors spec this design gives geometry
  - docs/DESIGN_Building_Interiors.md — (backend) OWNS buildings, panels, doors, the navmesh through doorways and the planned room/portal graph; its slice B-8 "room entry" IS this design
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS the civilian HSM and the armed-people rules (§5h AW-15…AW-24, CH-1) this design obeys indoors; CB-7's Comply state extends its §5e
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — names the scenario (SQD-5 room clearing, SQD-6 house search, SQD-8/9 defend / attack building) and the missing pieces (room graph, room state, `FindRoomStack`)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — (backend LD-1/LD-2) OWNS where a man looks; CB-4's sectors point the eyes once it lands
  - docs/DESIGN_Entity_Interactions.md — OWNS how a door command reaches the door's owner (OpenDoor / Breach)
  - docs/DESIGN_Orders_Params_Entity_Classes.md — R-2003: a sector is an area entity; the order on the wire is complete
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the machinery map (§9 squads: a new drill is an ISquadPlay, not a new node)
  - docs/DESIGN_Temperament_And_Stress.md — OWNS combatants' temperament (Nerve and Resolve added to AQ89's CombatTemperament beside RiskAcceptance and Obedience) and live stress; CB-28's Resolve lives there; the defenders' temperaments drive ts-house-defenders
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — the ATTACK in front of this clearing and the DEFENCE it meets (proficiency tiers, posts, snipers); proposes (DA-9) that IsolateAndClear becomes the later phases of its AttackPosition play
-->

# Building clearing — a squad clears a house, kills the armed, spares and controls the rest *(behaviors, `2026-10-18`)*

🔒 User, `2026-10-18`: *"I would like to add behaviors needed for military clearing houses where there might be armed insurgents inside
the rooms (need to be eliminated) while letting the civilians be, this is squad group behavior needing thorough design and knowledge of
real world military doctrines."*

🔒 User, `2026-10-18`, on the review: *"Platoon layer is what we need to exercise so the behaviors are created from that point of view already."* ·
*"Refusing civilians should be let be even if they protest, shout and insult unless they attack, then non lethal melee should be used (weapon butt hits for example for a soldier having other means) to stun them."* ·
*"Reuse knock down."* · *"Reports need to be networkable, nothing hidden inside single brain only."* ·
*"Search is likely an interaction channel, needing also some posture so ui can render an animation"* · *"Also civilians lying down covering their heads with their hands if frightened or ordered to comply."* ·
*"the mission us not always to kill"* · *"Also stun/flasglh grenades thrown to room"*

**The decision in one line (approved, R-2030 / R-2031):** a **platoon** play **`IsolateAndClear`** — the support squad isolates the
building, the assault squad clears it with the squad play **`ClearBuilding`** (its entry fire team clears by Battle Drill 6 while the
security fire team holds the foothold, and the two swap rooms), the reserve squad follows and relieves — on the existing runner, firing
under the rules already built (status combatants are shot, neutrals never, armed civilians only on their act), **controlling**
non-combatants (a civilian `Comply` state) and answering only an ATTACK with a rifle-butt knock-down. What a squad learns reaches the
platoon only as **typed reports on the wire** (3 s a hop, sent by the squad leader). First: the squad layer one echelon up (SL-0), and
backend's room graph.

## 1. Doctrine — what real forces do *(the basis; US Army Battle Drill 6 "Enter and Clear a Room", FM 3-21.8 / ATP 3-21.8; urban operations FM 3-06 / ATP 3-06.11; USMC MCWP 3-35.3)*

| phase | what is done | why |
|---|---|---|
| **isolate** | the support element seals the building — covers the exits and windows from outside, the assault element moves in | no one escapes or reinforces; the assault team is not shot in the back |
| **foothold** | enter at the best point (a door, a window, the roof — top-down is preferred because gravity and grenades favour the attacker), make the first room safe | the entry room is the base for everything after |
| **stack** | the team lines up on ONE side of the door, against the wall, #1 nearest the door; weapons alternate high/low and cover the danger areas | the doorway is the **fatal funnel** — nobody stands in it |
| **breach** | open it, force it (mechanical, ballistic, explosive) — or it is open | the door must not stop the entry halfway |
| **(grenade)** | **high intensity:** a fragmentation grenade before entry into a room thought to hold enemy and no non-combatants · **precision** (non-combatants likely, restrictive ROE): no fragmentation; a stun grenade, or none | precision is the default whenever civilians may be present |
| **enter** | #1 goes through and turns toward the open side (or the corner), #2 the opposite way, #3 opposite #2, #4 opposite #3 — each along the wall to his **point of domination** (≈ 1 m off the wall, a third into the room); a small room takes two | speed, surprise, violence of action — and four sectors that cover the whole room without crossing |
| **dominate** | each man clears his **sector** (the half / quarter of the room in front of him, never across a teammate) | no fratricide; every corner is covered |
| **engage** | **positive identification** before fire: hostile intent or a hostile act, or a combatant by status; controlled pairs | civilians and surrendering enemy are not targets |
| **control** | non-combatants are shouted at ("get down", "show me your hands"), put down, searched, held; a surrendering enemy is disarmed and detained | a hidden weapon is found on the search, not by a bullet |
| **clear** | each man calls "clear" (or "clear, one down", "clear, two civilians"); the team leader reports "room clear"; the room is **marked** | the squad knows which rooms are done |
| **next** | the team leader picks the next room (nearest uncleared, by the door that is covered), the team re-stacks; hallways and stairwells are danger areas cleared like rooms | the building is cleared in one continuous sweep, nothing left behind |
| **consolidate** | when the building is clear: casualties treated, non-combatants and detainees gathered and guarded, the building reported clear | the building can be handed over |

## 2. INVENTORY — what exists *(2026-10-18; three read-only sweeps, codebase-memory CLI `search_graph` (`.*Maneuver.*`, `.*(Room|Portal|Stair|Interior|Breach|Storey).*`, `.*Door.*`) + grep, and the design corpus; `check_index_coverage` NOT run — CLI only)*

| need | exists | where | missing |
|---|---|---|---|
| squad runner | ✅ `SquadManeuverRunner.Tick<TPlay,TParams>`, `ISquadPlay`, `MemberOrder` (Hold · AdvanceTo · Watch · Suppress · FireAtPoint · Stay …), gates, element partition, timeouts | `Brains/SquadManeuverRunner.cs:296`, `:333`; `Squad/Primitives/ElementPartitionPrimitive.cs:31`, `SquadGates.cs:31` | — |
| the stack drill | ⚠ a stub: `StackAndRoomEntryManeuver` (kind 5) — phases Stacking → Entering → Cleared, roles PointMan · BreachCover · Secondary ×2, **no geometry, no play** | `Squad/Maneuvers/StackAndRoomEntryManeuver.cs:24-73` | the play |
| buildings | ✅ wall panels with openings, storeys, stairs as ramps; `InsideTest` (which building) | `Terrain/TerrainBuildings.cs:35,69`; `Spatial/Eqs/EnterableShelter.cs:82` | ⛔ **no room, no portal graph, no "which room am I in"** — designed only (Building Interiors §198, B-8) |
| doors | ✅ door entities, `DoorState`, Open · Close · Lock · Unlock · **Breach (3 s)**, `MoveToDoor` / `OperateDoor` nodes, the mover opens a closed door, locked = a wall to the path | `Terrain/DoorCommands.cs:10,35`; `Terrain/DoorNodes.cs:60,109`; `CarKinem/Systems/DoorPassageSystem.cs:31` | — |
| walking inside | ✅ doorway polygons, multi-storey navmesh, stairs | `Navigation.Recast/NavDoorways.cs:16`; rail `RecastNavmeshFactoryTests.cs:100` | — |
| sight / fire inside | ✅ through openings, a closed door is opaque, penetration per panel | `Terrain/TerrainWorld.cs:418,535` | hearing ignores walls (`AcousticPerception.cs:21`) |
| interior EQS | ✅ window firing positions, cover per storey | `StarterTemplates.cs:32` | ⛔ stack positions, points of domination, next room |
| facing / sectors | ⛔ vision is 360°; "no facing control exists" | Squad Tactics §7.7.6; Look Direction LD-1…LD-3 (DESIGN) | a sector of responsibility |
| whom to shoot | ✅ the gate: `HoldNeutral` → `HoldSurrender` → `HoldConduct` (CH-1) → … → `HoldNeutralOnLine` (AW-20); `Armament.Judge` by status / known / conduct; indoors everything visible is identified (pistol 60 m by day) | `Combat/Executors/AimAndFireExecutor.cs:585-620`; `Combat/Armament.cs:265` | — (no new firing rule needed) |
| insurgents | ✅ type 2003 (status combatant, rifle + RPG); the covert insurgent (a civilian with a hidden pistol, draws, aims ⇒ Hostile) | `UrbanCombatTkbCatalog.cs:281`; Civilians §5h.7 | a room defender's posture |
| civilians indoors | ⚠ only violence frightens them: Alarmed crouches, Panic flees 20 m (through the soldiers), Inside stands then vanishes | `Brains/CivilianBehavior.cs:211-260`; `CivilianFearSystem.cs` | ⛔ **no reaction to soldiers, no compliance, no detain, no search** (Civilians §5h :1168, :1260) |
| grenades | ✅ `FireAtPoint` throws; fragments stopped by walls; **stun** (`WarheadKind.Stun` → `StunnedUntil`) | `Combat/Systems/AreaEffectSystem.cs:34,221` | ⛔ no military behaviour or the gate reads stun; indoor blast under-modelled (W-7v2) |
| shared squad memory | ✅ contacts pool, known combatants, claims (`CoverClaim`) | `SquadPerceptionMergeSystem.cs:32`; `KnownCombatants.cs:85`; `CoverClaim.cs:20` | room state (cleared / held) |
| test buildings | ✅ `house-2f` (10 × 8 m, 2 storeys, inner wall + hall door, front / back doors, stairs) placed twice in `bt-range` | `Recipes/Terrain/bt-range/buildings/house-2f.building.json` | a CQB house (hallway + 4 rooms) |
| a commander under a commander | ⚠ allowed (`UnitHierarchySystem` checks roster capacity only) but **never exercised** — 0 of the 20 shipped scenarios with a commander nest one | `Hrot.Common/Systems/UnitHierarchySystem.cs:112-160`; scenario sweep 2026-10-18 | a rail that runs it (SL-0) |
| the platoon knowing what its squads know | ⛔ the merge reads each member's own `TargetMemory`; a squad unit has none ⇒ a platoon's pool stays empty | `SquadPerceptionMergeSystem.cs:51` | the report step (CB-15) |
| the platoon hearing "under fire" / "a man down" | ⛔ both edges come from a member's Hit / `Health`; a squad unit has neither | `SquadCoordinationSystem.cs:73-74`, `SquadSensing.cs:131` | the report step |
| a wiped-out squad | ⛔ a unit has no `Health` ⇒ `Injury.IsInTheFight` is true with every man dead ⇒ the runner keeps ordering it; the platoon centroid counts it | `SquadManeuverRunner.cs:268`, `Injury.cs:65`, `UnitCentroidSystem.cs:33` | "combat effective" (SL-0) |
| a member down mid-drill | ✅ he gets no order; `SquadMemberDown` on the commander. ⚠ `BasicSquadSop` answers it with casualty care, which would pause a clearing mid-room | `SquadSensing.cs:126-143`; `BasicSquadSop.btree.json:612` | the play keeps its own casualties (§6a) |
| a report on the wire | ✅ the event-topic precedent: `BehaviorFaultReport` (origin node publishes once, every node takes it in) | `Hrot.Network.NED/BehaviorFaultMessages.cs`; `Translators/BehaviorFaultTranslators.cs` | the `SquadReport` topic (backend BR-3) |
| radio / comms | ⛔ none; Sensors G10 keeps a "Reported" modality open | `DESIGN_Sensors_And_Doctrine.md` G10 (searched `docs/`, `.dev/`; graph name pattern Radio / Comms / Communication: 0 classes) | the report step is its one seam |
| melee answer by a soldier | ⛔ only the rioter, the police line and the baton charge call `Melee.Strike` (Blunt, 3 s knock-down, 5 damage) | `Combat/Melee.cs:16-52` | the butt-stroke (CB-7) |
| HTTP: raw bus events | ⚠ `GET /events?type=` reads ONE ring of **500 events of every type** (`DiagnosticEventHistoryService.Capacity`), oldest overwritten — a firefight's shots and sensor events evict a report within seconds; payloads carry node-local `Entity` ids, not network ids | `Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs:16`; `DebugApiService.cs:837` | a report log of its own |
| HTTP: a log of one kind of event | ✅ the precedent: `ShotLog` (a ring per node, network ids at read time) served by `GET /combat/shots?last=&shooter=&target=` | `DebugApi/CombatReport.cs:17`; `DebugApiHost.cs:746` | — |
| HTTP: a squad's state | ✅ `GET /entities/{id}/squad`: members, assignments, contacts, the drill trace (`SquadDrillTrace`: phases, events, member orders) | `DebugApiService.Squad.cs:19-120`; `DebugApiHost.cs:1194` | the platoon's picture and the room record |
| an infantry PLATOON unit type | ⛔ only `Unit_InfantrySquad` (302, 1 leader + 9 riflemen, body-less, runs the squad SOP); platoons exist for tanks only (301, 303) | `MapDefinitions/TkbEntityTypes.cs:145-149`; `Tkb/BdcTkbCatalog.cs:734-750` | `Unit_InfantryPlatoon` (SL-0) |

## 3. Classes

```mermaid
classDiagram
  direction LR
  class IsolateAndClearPlay { <<NEW ISquadPlay, on the PLATOON commander>> Isolate Foothold Clear Relieve Consolidate }
  class IsolateAndClearParams { <<NEW>> Building · EntryDoor? · Intensity Precision|HighIntensity · Entry Ground|Roof }
  class ClearBuildingPlay { <<NEW ISquadPlay, kind 5, on a SQUAD commander>> Stack Breach Enter Dominate Control Clear Next · entry + security teams swap }
  class BuildingClearance { <<NEW, on the clearing echelon's commander>> per room Unknown Suspected Cleared Blocked · held people · built from RoomClear reports }
  class SquadReport { <<NEW event, on the wire BR-3>> From · To · Kind · Subject · Position · Count · SentAt }
  class ReportKind { <<NEW enum>> Contact UnderFire Casualty RoomClear RoomBlocked BuildingClear CombatIneffective Detained }
  class ReportOutbox { <<NEW, per squad commander>> queued reports, sent after ReportDelaySeconds by a leader in the fight }
  class PlatoonPicture { <<NEW, platoon commander>> contacts from Contact reports · each squad's last status · BuildingClearance }
  class CombatEffective { <<NEW, squad layer>> a unit member is in the fight while N of its men are }
  class RoomEntryPlan { <<NEW, pure>> stack side · entry order · points of domination · sectors · 2- or 4-man }
  class RoomGraph { <<BACKEND BR-1>> rooms · portals · corners · RoomOf(point) }
  class SquadManeuverRunner { <<existing>> Tick · Issue · gates · timeouts · Live(i) }
  class SectorFilter { <<NEW>> a member engages only inside his sector }
  class ControlOrder { <<NEW MemberOrder kind>> Control(person): cover, order down, search }
  class CivilianHsm { <<existing, + Comply>> }
  class Melee { <<existing>> Strike: Blunt, 3 s knock-down }
  class AimAndFireExecutor { <<existing gate, unchanged>> }
  IsolateAndClearPlay ..|> SquadManeuverRunner : a play on the platoon
  ClearBuildingPlay ..|> SquadManeuverRunner : a play on the squad
  IsolateAndClearPlay ..> ClearBuildingPlay : orders the assault squad
  IsolateAndClearPlay --> PlatoonPicture
  PlatoonPicture --> BuildingClearance
  ReportOutbox ..> SquadReport : publishes
  SquadReport ..> PlatoonPicture : taken in
  SquadReport --> ReportKind
  ClearBuildingPlay ..> ReportOutbox : RoomClear · Casualty · …
  SquadManeuverRunner ..> CombatEffective : Live(i) for a unit member
  ClearBuildingPlay ..> RoomEntryPlan
  RoomEntryPlan ..> RoomGraph
  ClearBuildingPlay ..> ControlOrder
  ControlOrder ..> CivilianHsm : he complies
  ControlOrder ..> Melee : he attacks ⇒ butt-stroke
  SectorFilter ..> AimAndFireExecutor : before the gate, never instead
```
*What the picture shows that prose hid:* the platoon never reads a squad's memory — it knows only what arrives as a `SquadReport`, and
the report is a wire event, so every node sees the same picture (R-2031); room progress lives with the PLATOON, so swapping the clearing
squad loses nothing; and the firing gate is still untouched — a soldier indoors obeys exactly the rules he obeys outside.

## 4. Sequences

```mermaid
sequenceDiagram
  participant P as platoon (IsolateAndClearPlay)
  participant S as support squad
  participant A as assault squad (ClearBuildingPlay)
  participant R as reserve squad
  P->>S: Isolate: Watch exits and windows (fire only into rooms not Cleared)
  P->>A: ClearBuilding (Building, EntryDoor, Intensity)
  P->>R: follow to the foothold, hold
  loop each room
    A->>A: entry team stacks, breaches, enters, dominates, controls, the security team holds the foothold
    A-->>P: SquadReport RoomClear (3 s later, by the squad leader)
    P->>P: BuildingClearance: mark it, tell S which rooms to stop covering
  end
  alt the assault squad reports CombatIneffective
    P->>R: ClearBuilding the rooms still Unknown or Suspected
    P->>A: hold the foothold and the casualty point
  end
  A-->>P: SquadReport BuildingClear
  P->>S: move in: Consolidate
  P->>P: casualties to the collection point, detainees guarded, report clear
```
*What the picture shows that prose hid:* the platoon acts on REPORTS — three seconds stale — and the relief of a broken squad is
just the same `ClearBuilding` order to another squad over a record the platoon already holds.

```mermaid
sequenceDiagram
  participant E as entry team #1-#4
  participant Q as security team
  participant C as squad commander
  participant O as ReportOutbox
  Note over E: #3 is hit in the doorway
  C->>Q: the next security man takes #3's place
  C->>E: re-stack, nobody waits in the doorway
  Note over E: later, #2 is hit inside the room
  C->>E: #1 and #4 widen their sectors, finish the room
  E-->>C: room clear
  C->>Q: carry #2 to the casualty point (the R-2012 medic)
  C->>O: Casualty (+ CombatIneffective when fewer than 4 can fight)
  O-->>O: after ReportDelaySeconds, publish on the wire
```
*What the picture shows that prose hid:* a casualty never stops a room — the room is finished first, the casualty moved after; the
squad's own `SquadMemberDown` reaction (casualty care) is held off while the play runs, and the platoon learns of it only by report.

## 5. Modules — who runs it each frame

```mermaid
graph TD
  CGF[CGF] --> BT[BrainTickSystem]
  BT --> PLT[platoon commander: IsolateAndClearPlay]
  BT --> SQD[squad commanders: ClearBuildingPlay / Isolate / follow]
  PLT -->|AssignTacticalIntentEvent| SQD
  SQD -->|AssignTacticalIntentEvent| MEM[members: AdvanceTo / Watch / OperateDoor / Control / FireAtPoint]
  MEM --> AF[AimAndFireExecutor: the gate, unchanged]
  SQD --> OUT[ReportOutboxSystem: due reports]
  OUT --> EG[SquadReport egress BR-3]
  EG --> WIRE((wire))
  WIRE --> IN[SquadReport ingress, EVERY node]
  IN --> PIC[PlatoonPicture on the platoon's brain node; the map on every node]
  BT --> CIV[CivilianHsm: + Comply]
  SH[SimHost] --> DOOR[DoorCommandSystem: the door's owner applies Open / Breach]
  LOAD[terrain load, every node] --> RG[RoomGraph BR-1]
  classDef gap fill:#ffd,stroke:#aa0
  RG:::gap
  EG:::gap
  IN:::gap
```
*Caption:* the yellow boxes are backend's; a report leaves the squad's node through the same event-topic path as a behaviour fault
(`BehaviorFaultReport`) and EVERY node takes it in — the platoon's brain builds its picture from it, the map can draw room marks
from it, a replay records it; nothing the platoon decides on is held in one brain only.

## 5a. Monitoring — reading the reports over HTTP *(lean, for review)*

**Decision (lean):** a **`SquadReportLog`** ring on every node — the `ShotLog` precedent — written by the report ingress (and by the
sender for its own reports), served by **`GET /squad/reports`**; and the platoon's **picture and room record** added to the existing
`GET /entities/{id}/squad`. ⭐ Because the reports are on the wire (R-2031), **every node's log holds the same reports** — the live check
asks CGF, SimHost and IG and compares them, which is the proof that nothing is hidden in one brain.

| route | answers |
|---|---|
| `GET /squad/reports?unit=&to=&kind=&since=&last=` | each report: `seq` · `kind` · `from` / `to` (network ids + names) · `subject` (network id) · `room` (id + name) · `position` · `count` · `sentAt` · `receivedAt` (⇒ the delay is visible) · `originNode` |
| `GET /entities/{platoon}/squad` *(extended)* | + `picture`: per squad its last report kind, age, men in the fight, combat effective · + `clearance`: per room `Unknown / Suspected / Cleared / Blocked`, cleared by whom and when, the people held there |
| `GET /entities/{squad}/squad` *(unchanged shape)* | the drill trace already shows `ClearBuilding`'s phases, events and member orders (Stack · Breach · Enter · Dominate · Control · Clear) |

Rejected: **`GET /events?type=SquadReportNotification` alone** — the 500-event ring of every type evicts a report in seconds of a
fight, and its payloads hold local entity ids · **a report component on the commander** — a component holds the last one, not the
history, and would be the brain-local state R-2031 rules out.
*(Owner: behaviors, in `CE-2263` — the debug API has taken squad routes from this lane before, `/entities/{id}/squad` O-a.)*

## 5b. A report's life — who makes it, how it travels, how the brain reads it, and how it ages *(⭐ leans CB-31…CB-35, 2026-10-18)*

🔒 User, `2026-10-18`: *"how the reports from subordinates work, who generates them, how they are transported to the brain, how brain
reads them, if they are buffered somewhere Or can age, so the brain doesn't act on old no longer valid ones"*.

**What §3–§5a already fix (R-2031, CB-15):** a squad's knowledge reaches its platoon ONLY as typed `SquadReport`s, sent by the squad
leader in the fight after `ReportDelaySeconds` (3 s), as an EVENT topic every node takes in (the `BehaviorFaultReport` precedent), logged
on every node (`SquadReportLog`, `GET /squad/reports`). ⛔ **What they did NOT fix:** when a report stops being true. Measured
neighbours: a soldier's own memory already ages — threat scores decay 0.1 / s and an untracked entry is forgotten below 0.5
(`PerceptionConstants.cs:44, 74`; `ThreatEvaluationSystem.cs:16-26`); the sensors design keeps open a *"Reported"* modality that feeds
that same memory (Sensors & Doctrine G10).

```mermaid
sequenceDiagram
  participant SQ as squad commander (its play, its members' merged memory)
  participant OB as ReportOutbox (squad, brain)
  participant W as SquadReport topic (every node)
  participant IN as ingress + SquadReportLog (every node)
  participant PP as PlatoonPicture (platoon's brain node)
  participant PL as platoon play
  SQ->>OB: Contact / Casualty / RoomClear ... with ObservedAt = when the squad saw it
  OB->>OB: hold ReportDelaySeconds, the leader must be in the fight
  OB->>W: publish once, Seq + 1 per sender
  W->>IN: every node: append to the log (a ring, newest kept)
  IN->>PP: apply on the platoon's node: newer Seq wins, older dropped
  PP->>PP: contacts into the platoon's TargetMemory (decay, forget) · status per squad (age) · facts per room
  PL->>PP: read the picture, never the events
  Note over OB,PP: while engaged the squad re-sends its STATUS every 15 s, silence ages its status into Stale
```
*What the picture shows that prose hid:* the brain never reads a report as an event — events last one frame. It reads the **picture**,
the buffer the reports are folded into, and the picture is where ageing happens. Three kinds of content age three different ways.

| content | kinds | buffered in | ages how | ⇒ the brain must not act on it when |
|---|---|---|---|---|
| **contacts** | `Contact`, `UnderFire` (its source), `SniperContact` | ⭐ a `TargetMemory` on the PLATOON commander, as a *Reported* entry (G10): position + error radius + `ObservedAt` | the **existing** decay and forget (0.1 / s, forgotten below 0.5 unless re-reported); a later report on the same subject refreshes it; `Contact` with `Count = 0` (lost / destroyed) clears it | it was forgotten — and every reader (`TopContact`, the threat ranking, the axis choice) already weighs a faded entry less |
| **status** | ⭐ a new `Status` kind (men in the fight, combat effective, ammo low, phase) + `CombatIneffective` | `PlatoonPicture`, one row per squad | **age = now − ObservedAt**; `Fresh` < 2 heartbeats (30 s while engaged), then `Stale`, then `Silent` after 60 s — silence is itself information (§6a: *"the platoon sees the silence as the squad's status age"*) | it is `Stale` or `Silent`: the play treats the squad as unknown (no new tasks that rely on it; Regular+ sends the reserve's leader to check) |
| **facts** | `RoomClear`, `RoomBlocked`, `BuildingClear`, `Detained`, `Foothold` | `BuildingClearance` / the picture | **do not decay** — durable until CONTRADICTED: a later `Contact` or `UnderFire` located in a Cleared room sets it back to `Suspected` | it was contradicted |

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **CB-31** | the report's time | every report carries **`ObservedAt`** (when the squad saw it) beside `SentAt`; age is measured from `ObservedAt`, so the 3 s delay and the wire are counted in it | age from `SentAt`: a report held 3 s looks 3 s fresher than it is |
| **CB-32** | order and duplicates | a **`Seq`** per sender; for contacts and status the picture keeps the newest by `ObservedAt` and drops an older one that arrives late; facts are idempotent (a second `RoomClear` changes nothing) | trusting arrival order: the wire does not promise it across nodes |
| **CB-33** | reported contacts | they enter a **`TargetMemory` on the platoon commander** as a *Reported* entry (the G10 modality) with the reporter's error radius, and age by the existing decay / forget | a separate contact list in the picture: a second memory with its own forgetting rules, and no existing reader could use it |
| **CB-34** | status heartbeat | while engaged a squad re-sends **`Status` every 15 s** and at once on a change (a man down, effective lost); `Fresh` / `Stale` / `Silent` by age; not engaged: on change only | status only on change: a squad that went silent (all dead, radio-less) would look fine forever |
| **CB-35** | where the picture lives and how it survives | on the platoon's brain node, **rebuildable**: on an ownership handover or a late join the new owner replays its own node's `SquadReportLog` (every node holds the same log) and the heartbeat refreshes status within one period | replicate the picture as a component: a second copy of state the reports already carry (R-2031's rule is that the REPORTS are on the wire) |

⚠ **Changes to BR-3** (the `SquadReport` topic, backend, unstarted — `git merge-base` checked): the fields gain `ObservedAt` and `Seq`,
the kinds gain `Status` (with men in the fight, effective, ammo-low, phase in `Count` and a small flags field). Sent as
[`NOTE_Behaviors_To_Backend_Report_Ageing.md`](../../blueprints/batches/NOTE_Behaviors_To_Backend_Report_Ageing.md).

## 6. Decisions *(✅ APPROVED 2026-10-18 — R-2030 … R-2033)*

| id | decision | ✅ ruled | rejected — one line each |
|---|---|---|---|
| **CB-1** | where rooms come from | **backend derives a room graph at load from the wall panels** (rooms per storey, portals with their two rooms, corners, `RoomOf`) — ONE producer for clearing, sound v2 and blast W-7v2 | rooms authored per template: a second copy of the walls · a flood-fill in behaviors: every reader re-derives it |
| **CB-2** | the shape *(revised on review)* | ⭐ **a PLATOON**: the **support squad** isolates (Watch exits and windows from outside), the **assault squad** clears, the **reserve squad** follows to the foothold and relieves; inside the assault squad the **entry fire team** clears and the **security fire team** holds the foothold and hallway, guards the held, and takes the next room (they swap). A squad given `ClearBuilding` alone (no platoon) keeps both fire teams inside | ⛔ SUPERSEDED: one squad's second team isolating from outside — four men cannot seal a house AND clear it |
| **CB-3** | entry | **Battle Drill 6**: stack on one side, #1 nearest; points of domination; **two-man entry** under 12 m² or with 2–3 men | fixed "#1 left": wrong at a corner door · all to the centre: a crossfire |
| **CB-4** | sectors | **a TARGET filter now, the eyes later** (LD-1); before the gate, never instead | wait for LD-1 · none: two men fire across each other |
| **CB-5** | whom to shoot | **no new firing rule** — the gate's status / conduct / hands-up / neutral-in-line holds already are doctrine's PID | a CQB "shoot anyone armed": breaks AW-21 |
| **CB-6** | grenades | **Precision** (default): no fragmentation; a stun grenade only into a Suspected room · **HighIntensity**: fragmentation into a Suspected room with no non-combatant known — and never with a friend within the munition's lethal radius of the aim point | always one: kills the civilians · never: loses the high-intensity case |
| **CB-7** | the civilians *(ruled on review)* | **control**: a soldier covers and orders down; the civilian HSM's **`Comply`** (hands up, kneel, stay; wins over Panic while controlled). 🔒 **One who REFUSES is let be — even protesting, shouting, insulting — and only watched. One who ATTACKS gets a rifle-butt strike: `Melee.Strike`, the existing 3 s knock-down (reused, no new stun); then he is ordered down again; a second attack ⇒ detained.** A covert insurgent who draws commits his act (AW-16) | ignore them · shoot the non-compliant: not doctrine, breaks CH-1 · a new stun effect: the knock-down already gives the window |
| **CB-8** | the search | 3 s per complying person after "room clear"; a Hidden weapon ⇒ laid down, **detained** (out of the fight); none ⇒ **held** in place; v1 no escort | escort everyone: a drill of its own · no search: the covert one is never found |
| **CB-9** | room state *(revised on review)* | **`BuildingClearance` on the CLEARING ECHELON's commander** — the platoon when there is one (a relieving squad continues it), else the squad; built from `RoomClear` / `RoomBlocked` reports, so every node can rebuild it (R-2031) | ⛔ SUPERSEDED: brain-local on the squad — a relief would start over, and it hides state in one brain |
| **CB-10** | room order | nearest uncleared room by a portal from a Cleared one; hallways and stairs are rooms; bottom-up, `Entry: Roof` flips it | top-down always · a pre-planned route |
| **CB-11** | doors | **#1 is the breacher**: Open (1 s), locked ⇒ Breach (3 s); a door that cannot be opened or breached ⇒ the room **Blocked**, skipped, reported | a separate breacher · explosive breach (later) |
| **CB-12** | contact from an uncleared room | **limited penetration**: no entry under fire through the doorway; fire from beside it; enter after 3 s quiet — ⚙ refined by CB-22: the doorway is pied (`SliceThePie`) before entry | enter under fire · abort the building |
| **CB-13** | v2 | top-down by default · explosive breach · escort to a collection point · mouse-holing · two clearing squads on two floors | — *(swapping fire teams moved INTO v1 — CB-2)* |
| **CB-14** | the defenders | no new insurgent behaviour: `CombatPosture` in a room; the covert insurgent reuses B3 | a barricade behaviour now |
| **CB-15** | knowledge up the chain *(ruled on review)* | ⭐ **inside a squad: automatic, as today. Squad → platoon: typed `SquadReport`s** (Contact · UnderFire · Casualty · RoomClear · RoomBlocked · BuildingClear · CombatIneffective · Detained), sent by the **squad leader in the fight** after **`ReportDelaySeconds` (default 3 s, on the unit's type)**; silent during the leader-down lull. 🔒 **On the wire — every node takes it in, nothing hidden in one brain** (R-2031). The one seam a radio model (range, jamming, G10's Reported modality) plugs into later | automatic at the platoon: an omniscient reserve commit · a full radio net now: no reader needs it |
| **CB-16** | a squad as a member *(found measuring)* | **combat effective**: a unit member is in the fight while ≥ `MinEffective` (default 4, or half its roster) of its men are; the runner's `Live(i)` and the centroid read it | the unit's own `Health`: a squad has none, and a sum would hide a wiped-out team |
| **CB-17** | casualties | the room is finished first; §6a | stop the room for a casualty: the doorway is the fatal funnel |
| **CB-18** | the isolating squad's fire | it fires only into rooms not Cleared, and stops as rooms are reported clear (doctrine's "shift fire"); ⚠ the friend-in-the-line hold is backend's B-15 (open) — until it lands the support squad watches and fires only at an exit or a window of a room the platoon holds as Unknown | free fire at the building: fratricide through a window |
| **CB-19** | search, detain, restrain *(ruled 2026-10-18, R-2032)* | **interaction kinds** on `EntityInteractionRequest` (R-221): the soldier asks, the civilian's OWNER applies — **Search** (3 s: a Hidden weapon becomes visible, laid down), **Detain** (the replicated `Detained` flag), **Restrain** (hands tied) — so it works across nodes and every node sees the result | a brain-local timer: the result hidden in one brain · a direct write: the soldier's node may not own the civilian |
| **CB-20** | what ui draws *(R-2032)* | new **gesture** values on the replicated gesture byte: `Searching` (the searcher), `HandsOnHead` (complying, searched), `Restrained` (hands behind the back), `CoverHead` (fear); kneeling = the existing Crouched stance | a pose component per activity: a second channel beside the gesture ui would merge |
| **CB-21** | leaning *(R-2032)* | a replicated **`Lean` body state** (None · Left · Right): the eye point shifts ≈ 0.4 m sideways (he sees round the corner), his hit silhouette shrinks to what is exposed, ui draws the tilt; the weapon leads — `Aimed` while leaning; the eyes' direction is LD-1's | a stance value: stance is height, a man leans crouched too · no lean: corners taken fully exposed |
| **CB-22** | corners *(R-2032)* | **`SliceThePie`**: small steps on an arc outside a corner or a doorway, leaning, weapon first, until the space beyond is seen — always at hallway corners, junctions and stairs; at a doorway **under contact** (replaces CB-12's "fire from beside the door": pie the room, then enter); otherwise rooms keep Battle Drill 6's fast entry | pie every room: doctrine wants speed at the stack · never pie: the corners |
| **CB-23** | escort *(R-2032)* | v2 — but its pose is fixed now: a `Restrained` man walking beside a soldier who holds him (a "led" mode, the person-carrier pattern) | build it now: no demo needs it yet |
| **CB-24** | the civilians' poses *(ruled 2026-10-18, R-2032)* | **Cower** (frightened, no shelter): **Prone + `CoverHead`** (was Crouched) · **Comply** (ordered down): **Prone + `HandsOnHead`** · searched: stays Prone + `HandsOnHead`, the searcher Crouched + `Searching` · detained: Prone or kneeling + `Restrained`. ⭐ **`ThreatDanger.ShowsHandsUp` counts `HandsUp`, `HandsOnHead`, `Restrained` — never `CoverHead`**: covering the head is fear, not surrender | one "head" gesture: a frightened Hostile would read as surrendering · Cower kept crouched: people under fire drop flat |
| **CB-25** | the objective *(ruled 2026-10-18, R-2033)* | one order setting **`Objective: Eliminate \| Arrest`** + an optional **`CaptureTarget`** (named people to take alive), on `IsolateAndClear` and `ClearBuilding`; police (SecurityForce) units default to Arrest, soldiers to Eliminate — the same drill, a setting | a separate police drill: the whole play twice |
| **CB-26** | Arrest's force *(R-2033)* | **everyone is judged by conduct** (AW-21's path, insurgents included) and the escalation ladder (§5k) reaches status combatants: **challenge** ⇒ complies: control, search, restrain, detain · refuses or resists without aiming: **non-lethal** (the butt / baton knock-down now; taser / spray when G5 lands) · aims or fires: **lethal** (self-defence, built). A `CaptureTarget`: non-lethal preferred even armed; lethal only to save a teammate | Arrest = never fire: police fire when a life is threatened |
| **CB-27** | shouting *(R-2033)* | **speech is data** — G1's targeted, replicated `Utterance` (`CE-2249`, backend's component): Challenge · commands Down / Freeze / HandsUp / DropIt / ComeOut · Announce at the door · team calls Clear / ManDown. The TARGET's brain reads the commands addressed to him. A prerequisite of Arrest; Eliminate works without it (`Gesture.Warning`). ⭐ The ladder's brain-local `WarningLog` becomes a reader of utterances — R-2031 | shouting as ui decoration only: nobody could answer it |
| **CB-28** | the challenged answer *(R-2033)* | a civilian: Comply or refuse (CB-24) · an insurgent: a new **`SurrenderDecision`** — challenged AND covered by ≥ 2 guns ⇒ he surrenders if his **`Resolve`** allows (the existing `Surrender` behaviour, now Prone + `HandsOnHead`; a fanatic, Resolve 1, never), else fights or flees | no decision: an insurgent never gives up, an Arrest ends in a firefight |
| **CB-29** | capture and delivery *(R-2033)* | restrain ⇒ **escort** (CB-23, v1 for Arrest) ⇒ board a vehicle (the Embark / LoadCasualty pattern) ⇒ move to the delivery point ⇒ hand over; the mission succeeds when he is **delivered alive** | leave him in place: the capture mission never ends |
| **CB-30** | stun grenades *(R-2033; widens CB-6)* | **Arrest:** a flashbang before EVERY room not Cleared · **Eliminate + Precision:** a stun into a Suspected room, and always a stun (never fragmentation) when non-combatants are known · **Eliminate + HighIntensity:** fragmentation into a Suspected room with none known, a stun when one is. The drill **"bang out"**: #1 opens, #2 throws, the stack waits OUT of the doorway behind the wall for the bang (≈ 1.5 s fuze), enters within 1 s on it — the window is up to 8 s (the existing stun grenade 6037: 8 m, 8 s, scaled by falloff and walls, `AreaEffectSystem.cs:221`). No throw when a friend would get more than 0.5 s of stun at his position (the same falloff and barrier) | before every room always: wasted under Eliminate · never in Arrest: it is the police's main tool against having to shoot |

## 6a. Casualties and edge cases *(approved)*

| case | behaviour |
|---|---|
| a man hit BEFORE entry | the entry stops; the next security man fills his place; re-stack — nobody stands in the doorway |
| a man hit INSIDE | the others widen their sectors, finish the room; then the security team carries him to the casualty point (the R-2012 medic, the existing carry) |
| the squad's `SquadMemberDown` reaction | held off while `ClearBuilding` runs (the play owns its casualties) |
| the entry team down to one man in the fight | the fire teams swap roles |
| fewer than `MinEffective` can fight | `CombatIneffective` report ⇒ the platoon sends the reserve to `ClearBuilding` what is left; the remnant holds the foothold and the casualty point |
| the squad leader falls | the existing succession and its 6 s lull (no reports in it); the team leader inside finishes the room |
| an insurgent raises his hands / is incapacitated | not shot (the existing gate); searched, detained (`Detained` report) |
| a civilian refuses | let be, watched — never forced, never shot for it (CB-7) |
| a civilian attacks | rifle-butt knock-down (`Melee.Strike`), ordered down again; a second attack ⇒ detained |
| a covert insurgent draws during the search | the covering man fires — the conduct rule frees him (AW-16) |
| fire from a room already Cleared | it goes back to Suspected; the security team engages; cleared again |
| an insurgent runs out | the support squad engages him (a combatant by status); a fleeing civilian is only controlled |
| a friend within the grenade's lethal radius of the aim point | no grenade |
| a door that cannot be opened or breached | the room Blocked, skipped, `RoomBlocked` report; the platoon decides (explosive breach later) |
| the report sender is down | nothing goes up until succession; the platoon sees the silence as the squad's status age |

## 7. Slices *(owners; behaviors ids `CE-2257`…`CE-2261`, `CE-2262`…`CE-2264`)*

| order | slice | owner | what | needs |
|---|---|---|---|---|
| 1 | **SL-0** `CE-2262` | behaviors | **the squad layer one echelon up**: `CombatEffective` (CB-16) read by `Live(i)` and the centroid; the `Unit_InfantryPlatoon` composite type (three squads, body-less, runs the commander SOP); a rail that runs a platoon ordering a squad commander a drill (`SquadMarch`) on a nested-hierarchy scenario — the first time one is exercised | — |
| 2 | **RP-1** `CE-2263` | behaviors | **the report step** (CB-15): `SquadReport` + `ReportKind`, `ReportOutbox` (delay, leader in the fight, silent in the lull), `PlatoonPicture` taken in from the wire; Contact / UnderFire / Casualty / CombatIneffective first; the monitoring (§5a: `SquadReportLog`, `GET /squad/reports`, the platoon's picture on `/entities/{id}/squad`) | backend BR-3 |
| 3 | **BC-1** `CE-2257` | behaviors | one room, one squad: `RoomEntryPlan` + entry / security fire teams, Stack · Breach · Enter · Dominate · Clear, `SectorFilter`; demo `bc-room-clear` | BR-1 |
| 4 | **BC-2** `CE-2258` | behaviors | civilians: `Control`, `Comply`, search, held / detained, the refusal rule and the butt-stroke (CB-7); demo `bc-room-civilians` | BC-1 |
| 5 | **BC-3** `CE-2259` | behaviors | the building, one squad: `BuildingClearance`, next room, hallways, stairs, the fire teams swapping, RoomClear / RoomBlocked reports; demo `bc-house-clear` | BC-1, RP-1 |
| 6 | **PL-1** `CE-2264` | behaviors | **the platoon** `IsolateAndClear`: isolate (CB-18), foothold, clear, relieve on `CombatIneffective`, consolidate; `BuildingClearance` on the platoon; demo `bc-platoon-house` | BC-3, SL-0, RP-1 |
| 7 | **BC-4** `CE-2260` | behaviors | grenades by `Intensity` (CB-6); demo `bc-house-high-intensity` | BC-3, BR-2 |
| 8 | **BC-5** `CE-2261` | behaviors | contact and casualties (CB-12, §6a); demo `bc-house-defended` | BC-3 |
| 0 | **CP-1** `CE-2265` | behaviors | the civilians' poses (CB-24): Cower Prone + `CoverHead`; the four gesture values; `ShowsHandsUp` counts HandsOnHead / Restrained, never CoverHead — buildable now | — |
| — | **BR-1** … **BR-5** | backend | the room graph + a CQB test house · stun holds every class · the `SquadReport` topic · the Search / Detain / Restrain interactions · the `Lean` body state | [handoff 1](../../blueprints/batches/HANDOFF_Behaviors_To_Backend_Building_Clearing.md) (BR-1, BR-2) · [handoff 2](../../blueprints/batches/HANDOFF_Behaviors_To_Backend_Building_Clearing_Reports.md) (BR-3) · [handoff 3](../../blueprints/batches/HANDOFF_Behaviors_To_Backend_Building_Clearing_Interactions.md) (BR-4, BR-5) |

## 7a. Building blocks — the whole list *(2026-10-18; ✅ reuse as is · ⚙ extend · 🆕 new; measured where marked)*

### Engine and wire *(backend)*
| block | kind | what | slice |
|---|---|---|---|
| `RoomGraph` + `RoomOf` + the CQB house | 🆕 | rooms, portals (doors, gaps, stairs) with their two rooms, corners, area; the §8 house in `bt-range` | BR-1 |
| stun holds every class | ⚙ | `Effects.IsStunned` stops the fire gate (`HoldStunned`) and the mover for any person — today only civilian code reads it | BR-2 |
| `SquadReport` topic | 🆕 | the event topic on the `BehaviorFaultReport` precedent | BR-3 |
| interactions `Search` · `Detain` · `Restrain` | 🆕 | `EInteractionKind` cases applied by the civilian's owner (CB-19): reveal + lay down a Hidden weapon, the replicated `Detained` flag, hands tied | BR-4 |
| `Lean` body state | 🆕 | replicated None · Left · Right; the eye point offset for perception, the exposed silhouette for hits (CB-21) | BR-5 |

### Squad layer *(FDP `Squad` toolkit, behaviors)*
| block | kind | what | slice |
|---|---|---|---|
| `CombatEffective` | 🆕 | a unit member is in the fight while ≥ `MinEffective` of its men are; read by `SquadManeuverRunner.Live(i)` and `UnitCentroidSystem` | SL-0 |
| `Unit_InfantryPlatoon` | 🆕 | the composite type (3 squads, body-less, commander SOP) — none exists | SL-0 |
| `SquadReportNotification` | 🆕 event | the local event the egress drains and the ingress raises on every node | RP-1 |
| `ReportOutbox` + `ReportOutboxSystem` | 🆕 | per squad commander: queued reports, sent by the leader in the fight after `ReportDelaySeconds`; silent in the leader-down lull | RP-1 |
| `PlatoonPicture` | 🆕 | on the platoon: contacts from Contact reports, each squad's last report + age + men in the fight | RP-1 |
| `SensorChange.SubordinateContact` · `SubordinateUnderFire` · `SubordinateIneffective` | 🆕 events (18–20) | raised on the platoon commander when such a report ARRIVES — so the platoon SOP / HSM reacts as a squad's does to `SquadContact` | RP-1 |
| `SquadReportLog` + `GET /squad/reports` + the platoon block on `/entities/{id}/squad` | 🆕 | §5a | RP-1 |
| `SquadPerceptionMergeSystem` | ✅ | unchanged inside a squad (automatic, CB-15) | — |
| `SquadSensing.Succession` · `MemberDown` · `SquadLeaderDown` | ✅ | the leader's lull also silences reports | — |

### The runner's member orders *(`MemberOrderKind`, measured: Hold · AdvanceTo · Disembark · Suppress · CarryCasualty · Watch · Embark · FireAtPoint · Stay)*
| order | kind | what it starts on the member | slice |
|---|---|---|---|
| `Watch` · `AdvanceTo` · `Hold` · `CarryCasualty` · `FireAtPoint` · `Suppress` | ✅ | stack points, foothold, isolating watch points, grenades (stun 6037 / M67 6001 exist as munitions), casualties | — |
| `OperateDoor` (9) | 🆕 | the library door behaviour on the shared `DoorNodes` (MoveToDoor · OperateDoor Open / Breach — `DoorLocksmith` is built from them) | BC-1 |
| `Enter` (10) | 🆕 | `EnterRoom`: along the wall to a point of domination WITH a sector; never stops in the doorway | BC-1 |
| `Control` (11) | 🆕 | `ControlPerson` | BC-2 |
| `Search` (12) | 🆕 | `SearchPerson` | BC-2 |

### Member behaviours *(library, behaviors)*
| behaviour | kind | what | slice |
|---|---|---|---|
| `EnterRoom {Point, Sector}` | 🆕 | move to the point, then hold and engage only in the sector; Success when the sector is empty, down or complying | BC-1 |
| `ControlPerson {Target}` | 🆕 | cover him, order him down (a `Gesture` now, G1 speech later); he complies ⇒ Success; refuses ⇒ watch only (CB-7); attacks ⇒ the butt-stroke | BC-2 |
| `SearchPerson {Target}` | 🆕 | go to a complying man, Crouched + `Searching`, send the `Search` interaction (3 s); a Hidden weapon found ⇒ `Detain` (+ `Restrain`); none ⇒ `Held` (CB-19) | BC-2 |
| `SliceThePie {Corner}` | 🆕 | the arc of small steps outside a corner or doorway, leaning, `Aimed`, until the space beyond is seen (CB-22) | BC-3 (corners), BC-5 (doorway under contact) |
| `MeleeResponse` (SOP reaction) | 🆕 | struck in melee by an unarmed man in reach (`Hit` from a Blunt `DamageAssessedEvent` whose `Source` is in reach) ⇒ one `Melee.Strike` back (the existing 3 s knock-down), then the order resumes — any soldier, not only indoors | BC-2 |
| `SurrenderDecision` + `Resolve` | 🆕 | the insurgent's answer to a challenge (CB-28) | CE-2267 |
| `Escort {Detainee, To}` + delivery | 🆕 | walk a restrained man beside the escort ("led" mode), board, deliver, hand over (CB-29) | CE-2268 |
| `CombatPosture` · `CasualtyCare` (the R-2012 medic) · `CarryCasualty` · `Surrender` · `StationWeapon` (B3 draw) | ✅ | the defenders, casualties, a surrendering insurgent, the covert one's draw | — |

### Squad and platoon plays *(behaviors)*
| play | kind | what | slice |
|---|---|---|---|
| `ClearBuildingPlay` (kind 5) | 🆕 over the existing stub `StackAndRoomEntryManeuver` | Stack · Breach · (Grenade) · Enter · Dominate · Control · Search · Clear · Next · Consolidate; entry + security fire teams swapping; bump-up; casualties per §6a; runs with ROE `StayOnTask` so the squad SOP's `SquadMemberDown` reaction does not pause it (✅ `RoeReactions.StayOnTask` refuses every reaction — `SopSlotTests.CE2078_…`) | BC-1, BC-3, BC-5 |
| `IsolateBuildingPlay` | 🆕 | the support squad: `Watch` on watch points that cover every exit and window face from outside; engagement limited to rooms not Cleared (CB-18) | PL-1 |
| `IsolateAndClearPlay` | 🆕 on the platoon | Isolate · Foothold · Clear · Relieve (on `SubordinateIneffective`) · Consolidate | PL-1 |
| the reserve | ✅ | `SquadMarch` to the foothold, then `SquadHold` | — |

### Pure planners and state *(behaviors)*
| block | kind | what | slice |
|---|---|---|---|
| `RoomEntryPlan` | 🆕 | stack side, entry order, points of domination, sectors, 2- or 4-man — recomputed from the live members (bump-up) | BC-1 |
| `RoomOrder` | 🆕 | the next room: nearest uncleared by a portal from a Cleared one; hallways and stairs as rooms; bottom-up / Roof top-down | BC-3 |
| `IsolationPlan` | 🆕 | watch points outside covering each exit and window face, out of each other's line | PL-1 |
| `EngagementArea` | 🆕 component, read by the target pick BEFORE the gate | allowed arcs / polygons per member — ONE mechanism for the room sector (CB-4) and the support squad's "rooms not Cleared" (CB-18); never a new firing rule | BC-1, PL-1 |
| `BuildingClearance` | 🆕 | per room Unknown / Suspected / Cleared / Blocked, the held and detained, the casualty point; on the clearing echelon's commander, rebuilt from reports | BC-3, PL-1 |
| `GrenadeCheck` | 🆕 | Objective × Intensity × room state × non-combatants known; the friend check by the area effect's own falloff and walls (CB-30); "bang out" timing | BC-4 |
| `Objective` / `CaptureTarget` + conduct-for-everyone | 🆕 | the order setting (CB-25), the gate's conduct path and the ladder for status combatants under Arrest (CB-26) | CE-2266 |
| `Utterance` readers | 🆕 | the challenge and the commands sent by `ControlPerson` / the ladder; the target's brain reads those addressed to him (CB-27) | CE-2249 (+ backend G1) |

### The civilians *(the civilian HSM, behaviors)*
| block | kind | what | slice |
|---|---|---|---|
| `Comply` state | 🆕 | entered when a forces person controls him (`Covering.By` + the control order) and fear ≥ 0.3 or attitude ≤ 0; **Prone + `HandsOnHead`**, stays; wins over Panic while controlled (CB-24) | BC-2 |
| `Cower` pose | ⚙ | **Prone + `CoverHead`** instead of Crouched (CB-24) | CE-2265 |
| gesture values `Searching` · `HandsOnHead` · `Restrained` · `CoverHead` | 🆕 | appended to `GestureKind` (the wire is the byte); `ThreatDanger.ShowsHandsUp` counts HandsUp / HandsOnHead / Restrained, never CoverHead (CB-24) | CE-2265 |
| refusal | 🆕 (a guard, no state) | the rest stay in their state — protest, shout, insult — never forced | BC-2 |
| the attack | ✅ | the existing `Riot` state (fist on a forces person in reach) — what the soldier's `MeleeResponse` answers | — |
| `Detained` | 🆕 flag | out of the fight (like a laid-down surrender): not searched again, guarded | BC-2 |
| the covert insurgent | ✅ | B3 draw + CO-1 aiming ⇒ Hostile; the gate frees itself | — |

### Demos and checks
| block | kind | slice |
|---|---|---|
| the 7 scenarios of §8 + their twins + `utility-demo-check.py` runners | 🆕 | each slice ships its own |
| ⛔ **to measure:** how a scenario incapacitates men at a scripted moment (the relief demo) · whether a rifleman's loadout carries M67 / stun grenades or the scenario must give them (`PersonHands.Give`) · what a failed breach leaves (`DoorState`) for `RoomBlocked` | — | before the slice that needs it |

**Count:** 5 backend blocks · 9 squad-layer · 4 new member orders · 5 member behaviours · 3 plays · 6 planners / state · 5 civilian (+ 4 gesture values) ·
3 new sensor events + 1 new local event + 1 wire topic — and the firing gate, the doors, the casualty care, the melee strike, the
munitions and the squad runner are all reused.

## 8. Demos *(each with an HTTP live check and an in-process twin, as every drill)*

![The CQB test house](img/cqb-house.svg)

*The house BR-1 is asked for, as proposed here (backend may move walls; the demos need only its topology):* a 14 × 10 m single storey —
a **hallway** (a danger area cleared as a room) with the front door (W) and back door (E); rooms **A** and **B** north, **C** south (36 m²),
**D** south (11 m² ⇒ two-man entry), and the closet **E** reached only through D; side doors N into A and S into C, so the support
squad has four exits to watch. Placed in `bt-range` on open ground, 60 m from the existing `house-2f`.

### 8.1 The ladder of demos

| demo | slice | cast | must end with *(the live check, read over HTTP)* |
|---|---|---|---|
| `bc-room-clear` | BC-1 | one squad at the front door of the hallway; 2 insurgents (`CombatPosture`) in room A's far corners | ① nobody stands in a doorway longer than 1 s (positions vs the room graph) · ② each entry man reaches his point of domination (drill trace) · ③ both insurgents down, no friend hit (`/combat/shots`) · ④ `RoomClear` A in `/squad/reports` |
| `bc-room-civilians` | BC-2 | the same squad; room C: 1 insurgent, a civilian who complies, one who refuses and shouts (`Attitude` +0.8, no `Instigator`), one who attacks (`Attitude` +1, `Instigator`), a covert insurgent (hidden pistol, B3 draw on the search) | ① the insurgent down · ② the complier `Comply`, searched, `held`, unhurt · ③ the refuser standing, never struck, never shot · ④ the attacker knocked down by ONE blunt blow from a soldier (`Melee.Strike`, Blunt) then held · ⑤ the covert one `Detained` (report) — or shot only after `WeaponPosture` Aimed at a soldier |
| `bc-house-clear` | BC-3 | one squad, the whole house; insurgents in A and E (the closet), civilians in B and C | ① a `RoomClear` for hallway, A, B, C, D, E — each ONCE, each from a room reachable from a Cleared one (E after D) · ② the two fire teams swap at least once (drill trace) · ③ every insurgent down, every civilian unhurt and held · ④ `BuildingClear` last |
| ⭐ `bc-platoon-house` | PL-1 | **a platoon**: a body-less `Unit_InfantryPlatoon` commander over three `Unit_InfantrySquad`s (support · assault · reserve), the same house and people as `bc-house-clear` | ① the support squad on four watch points covering the four exits before the assault squad reaches the door · ② the same `RoomClear` sequence as above · ③ ⭐ **each `RoomClear` arrives 3 s (± 1 frame) after the drill trace's "room clear"** (`receivedAt − ` the trace time) · ④ ⭐ **the report log is identical on CGF, SimHost and IG** · ⑤ the platoon's `clearance` = all Cleared · ⑥ no support-squad round crosses a window of a room the platoon then held as Cleared (`/combat/shots` crossings × the report times) · ⑦ the support squad enters only after `BuildingClear` |
| `bc-platoon-house-relief` | PL-1 | the same, with three assault-squad men made incapacitated by the scenario script after room B | ① `Casualty` ×3 then `CombatIneffective` from the assault squad · ② the reserve is ordered `ClearBuilding` within 1 s of the report arriving · ③ the reserve clears only C, D, E — no room cleared twice · ④ the casualties carried to the casualty point after a room is clear, never mid-room |
| `bc-house-high-intensity` | BC-4 | one squad, `Intensity: HighIntensity`, no civilians, insurgents heard in A and D | a fragmentation grenade before A and D (Suspected); none before B, C, E (Unknown); none with a friend inside its lethal radius |
| `bc-house-defended` | BC-5 | an insurgent in A firing through its open door at the stack in the hallway | no entry until 3 s of quiet; the man hit in the doorway replaced from the security team; the room still cleared |
| `bc-police-arrest` | CE-2266 | a police team (Arrest) at the CQB house: 2 insurgents (one Resolve 0.2, one 1.0), 2 civilians | a flashbang before every room; a challenge (`Utterance`) to every person; the low-Resolve insurgent surrenders and is detained ALIVE; the fanatic is shot only after he aims; the civilians comply, unhurt |
| `bc-capture-hvt` | CE-2268 | a platoon (Eliminate) with `CaptureTarget` = one named insurgent among three, a truck at the rally point | the two others engaged as usual; the target taken alive (non-lethal only), restrained, escorted to the truck, delivered; success = delivered alive |

⚠ **To measure before the relief demo is built** (`⛔ assumed`): how a scenario incapacitates men at a scripted moment. A timed scenario
task, a health-setting HTTP command or a test-only system are the candidates; the twin can set `Health` directly either way.

### 8.2 What every live check also asserts
- **Determinism, the drill way:** the twin and the live run agree on the room ORDER (not on exact times).
- **No civilian is ever the target of a round** (`/combat/shots` target class), except a round fired after that man's own lethal act.
- **The report log is the record:** a check never reads a brain component to decide pass or fail — it reads `/squad/reports`, the
  room record and the shots, all of which exist on every node.

## ⛔ HISTORY

- *(2026-10-18, first draft — SUPERSEDED on review the same day)* CB-2 was *"two elements: the assault team (4, the first fire team)
  clears; the support team (the rest) isolates — Watch on each exterior door and the window sides from outside — and moves in at
  Consolidate; fewer than 6 men: no support team."* CB-9 kept `BuildingClearance` brain-local on the squad commander. CB-13 listed two
  teams leapfrogging as v2. The four questions of §9 were answered: the leans approved, CB-7 refined (refusal ⇒ let be; attack ⇒
  butt-stroke knock-down).
