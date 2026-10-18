<!--STATUS
state: LIVE
updated: 2026-10-19
build-state: BUILDING — DA-A ✅ BUILT 2026-10-19 (§8a, CE-2273) · DA-L ✅ BUILT 2026-10-19 (§13c, CE-2282; leans DA-26…DA-30 for the user) · READY-TO-BUILD — DA-1…DA-16 ✅ APPROVED 2026-10-18 (R-2035; DA-9 amended: ClearBuilding stays its own order, §10) · DA-17…DA-23 ✅ APPROVED 2026-10-18 (R-2036); DA-24 ✅ APPROVED (R-2038); DA-25 (§13b, the WorldSurvey sensor) amended by the user (R-2038: standing sensor, rooms derived by the sensor) — taken as approved as amended; R-2037 (the brain never reads world geometry) ruled; tracker CE-2273…CE-2285; backend frame docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Defend_And_Attack.md (BR-6…BR-11); nothing built
current-answer: §1 doctrine · §2 INVENTORY · §3–§5 the diagrams · §6 decisions · §7 edge cases · §8 slices · §9 demos · §10 presets · §11 composing the commander · §12 roles · §13 sectors · §14 sensors · §15 every behaviour · §16 the roof · §17 test scenarios
stale-below: ⛔ HISTORY at the end (the superseded synchronous §13b).
known-rot: §3 / §4 / §15 name PlatoonPicture and the report kinds PostsManned · Displacing · SniperContact · PhaseLine · Foothold · ObjectiveTaken — now CommandPicture (CC-1) and the closed PROGRESS / SPOT set (CB-43; DESIGN_Platoon_Attack_Defend_Clear PC-2/PC-4); DA-7's ROE lift on a squad does not reach its men today (RoeOf is per entity) — PC-6 / CE-2289; DA-9's "AttackPositionPlay ISquadPlay" is an HSM asset (PC-1). §17 `tq-urban-ambush-counterattack` is set in test-town, whose 6 buildings are SOLID prisms (no interior) — it cannot clear there; the platoon demos move it, `tq-military-attack-defended` and `tq-police-arrest-surprise` to the new terrain cqb-village (DESIGN_Platoon_Attack_Defend_Clear §12, PC-12).
known-conflict: none — the Building Clearing platoon play is RESOLVED as the ClearBuilding preset (§10, R-2035).
related-designs:
  - docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md — backend's answer to §13b for ITS sites (CE-2287): the home of each (the fire gates into the muscle's fire pipeline, the danger-area sight into its sensor, door approach and spawn level on the muscle), leans for the user
  - docs/DESIGN_World_Survey_Sensor.md — (backend, CE-3249) OWNS how DA-25's WorldSurvey is built: the road, the answer, the room derivation, the slices
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the concrete platoon HSMs PlatoonAttack / PlatoonDefend and the six squad plays with roles and the report contract; revises §3/§4 report names (PC-2) and needs PC-6 for DA-7
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — the concrete command machinery (CommandPicture, staff, OrderSubordinate) its platoon plays will run on
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS the clearing drill (rooms, entry, sectors, reports R-2031, EngagementArea, BuildingClearance, the platoon frame SL-0); this design is the ATTACK in front of it and the DEFENCE it meets
  - docs/DESIGN_Temperament_And_Stress.md — OWNS a man's will (RiskAcceptance, Obedience, Nerve, Resolve, stress, the break); this design adds his ABILITY (proficiency) beside it and never merges the two
  - docs/DESIGN_Peek_And_Fire.md — OWNS the expose / hide / relocate cycle (PeekAndFire, WindowDuel, FiringPositionMemory); defenders at windows and attackers behind cover run it, with proficiency setting its timings
  - docs/DESIGN_Building_Interiors.md — OWNS storeys, wall openings, window firing points (§3l), stairs and the roof slab; the defence's building posts are generated from them
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad drills (suppress-and-manoeuvre, fire and movement, bounding overwatch, danger crossing, casualty care) the attack's squads run
  - docs/designs/group-maneuvers/Squad_Coordination_Design_v1_1.md — OWNS the roster, roles and fire allocation inside a squad
  - docs/designs/hill-attack/DESIGN.md — the one built platoon commander (R-2022 shoot-and-duck, R-2023 never the same spot twice); its intent dispatch is the precedent for ordering squads
  - docs/blueprints/Architect_Question_85_Hit_Chance.md — OWNS the hit model's spread; DA-2 adds one shooter factor to it
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Defend_And_Attack.md — the backend frame: spread factor, acquisition by exposure, LMGs and roles on the wire, sniper / DMR / scope, BuildingOf(point), trenches later
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — OWNS where a man looks (LD-1…LD-3); the march watch sectors and EngagementArea.WatchBearing point the eyes once it lands
  - docs/DESIGN_Orders_Params_Entity_Classes.md — OWNS R-2003 (a sector is an area entity) and the Designate › Set sector gesture (§13.3, F10); §13a builds the two sector levels on it
  - docs/DESIGN_Decision_Layer.md — OWNS task · SOP · reaction and resume (§4.1, §4.9); §11's town movement is a task and the attack a reaction
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — the catalogue rows SQD-8 / SQD-11 (defend), D7 (trenches) and the building-post templates this design builds
-->

# Defending and attacking a position — from untrained insurgents to a skilled unit *(behaviors, `2026-10-18`)*

🔒 User, `2026-10-18`: *"I would like to design also behaviors for defending certain place including multi story buildings and
corresponding attack tactics. Defenders and attackers skill level needs to be settable from a group of untrained insurgents to a skilled
military unit - it migh drive the tactics chosen by their commanders. Defenders are usually prepared, using varoous cover spots outside
(behinds walls, obstacles, in dug-in holes..) as well as inside (behind windows - lean and peek might apply for observation, quick shot
while in fight), on the roof etc. Attackers need to advance, closing, using cover behind obstacles,busing short peek and shot, flanking
the building etc … Snipers migh help on both sides."* · *"This likely includes multi team coordination"* · *"The attack and building
clear might be connected in natural sequece i guess"*.

**The decision in one line (lean):** a per-man **`CombatProficiency`** (one 0–1 value, five named tiers) that sets how well he shoots,
how briefly he shows himself and how fast he reacts, and that **gates the plays his commander may choose**. On top of it go two
platoon plays: **`DefendPosition`**, which generates prepared posts outside, at windows on every storey and on the roof, with sectors,
a trigger line, alternates and a fall-back; and **`AttackPosition`**, which runs observe → support by fire → approach → isolate →
foothold → clear or assault through → consolidate. For a building, **the clearing already designed becomes the attack's last phases**.
Snipers are a two-man team behaviour used by both sides. Every hand-off between teams rides the networked squad reports (R-2031).

## 1. Doctrine — what real forces do *(US Army ATP 3-21.8 Infantry Platoon and Squad: the defence and the attack chapters; ATP 3-06.11 Combined Arms Operations in Urban Terrain; breaching fundamentals SOSRA: suppress, obscure, secure, reduce, assault; sniper employment, FM 3-22.10 / ATP 3-21.18)*

| | defence | attack |
|---|---|---|
| **the plan** | an engagement area (EA) where the enemy is to be killed · a trigger line where fire opens · each element a **sector** · **primary, alternate, supplementary** positions · an observation post (OP) forward · a reserve · a fall-back route | **isolate** the objective · a **support-by-fire** (SBF) element suppresses it · the **assault** element moves by covered routes, usually to a **flank** · **obscure** with smoke where it must cross open ground · gain a **foothold** · clear · **consolidate** and get ready for the counterattack |
| **buildings** | fight from **inside the room, set back** from the window, never on the sill · upper storeys for fields of fire, the ground floor held lightly · the roof for observation and snipers, but exposed to air and indirect fire · withdrawal routes planned before the fight | suppress **every window that sees the assault's route** · shift and then **lift** fire as the assault reaches the building, so as not to hit your own · enter at the least defended point, from the top when there is a way up · then the clearing drill (Building Clearing §1) |
| **fire control** | hold fire until the enemy is inside the EA (an ambush effect) · mutual support between positions · the final protective line when he closes | the SBF fires at known and likely posts, rate high at the assault's move, low otherwise · the assault fires only when it must |
| **snipers** | far back, deep in a room, few shots, then move · targets: leaders, machine-gunners, snipers, radio | the same from an overwatch with a long line of sight · counter-sniper: locate, then suppress with heavy fire, smoke, manoeuvre or a sniper |
| **untrained / irregulars** | occupy windows and roofs, open fire at first sight at any range, spray, shoot and move to another window, withdraw or blend in individually, no sectors and no reserve | mass rush on the nearest axis with covering spray, little use of cover or smoke, high casualties, breaks under fire |

## 2. INVENTORY *(2026-10-18 — codebase-memory CLI `search_graph` over Class / Method / Enum / Struct for `Storey|Floor`, `Roof`, `Window|Loophole|FiringPost|BuildingPost`, `Flank`, `Overwatch`, `Smoke`, `Skill|Proficien|Training|Marksman|Veteran`, `Trench|Foxhole|DugIn|Fortif|Bunker|Sandbag|Obstacle`, `Defen[cs]e|Defend|HoldPosition|Occupy`, `Sniper`, `.*Play` (16 squad plays); grep for `ISquadPlay`, `DefendArea`, `FallBack`; one read-only code sweep, nine questions, every answer opened at the line; the design corpus sweep for defence / attack / snipers / skill. `check_index_coverage` NOT run — CLI only)*

| need | exists | where | missing |
|---|---|---|---|
| a defend order | ⚠ `DefendArea` intent → infantry run `CombatPosture`, moving to the area's centre and holding within its radius; **no posts, no sectors** · `CommanderNodes` sends a hard-coded `DefendArea` with empty params (TODOs) | `Mappers/DefendAreaMapper.cs:48-49`; `Brains/CommanderNodes.cs:68-88` | the plan, the posts, the sectors, fire control (DA-4…DA-7) |
| squad plays | ✅ 16 `ISquadPlay`s on one runner — march, suppress-and-manoeuvre, fire and movement, bounding overwatch, danger crossing, hold, break contact, casualty care, embark / dismount, airborne, fast rope, battery fire mission; **none defends** | `Brains/*Behavior.cs`, `SquadManeuverRunner.cs` | `DefendPositionPlay`, `AttackPositionPlay` |
| a platoon commander | ✅ hill attack: a BTree of C# nodes ordering its tanks by `AssignTacticalIntentEvent` (origin Superior): `MoveToLocation`, then `HullDownAttack` in alternating waves; **no SBF element** (the idle half is just not ordered) | `HillAttackCommanderNodes.cs:164-170, 385-412, 531-537, 745-786` | squads as subordinates (SL-0, Building Clearing), a support element |
| storeys, stairs, roof | ✅ a building template holds storeys; walls are cut around openings, upper storeys become slabs, stairs ramps, a flat roof a walkable slab; the navmesh climbs the stair; **`house-2f`** (two storeys, windows, stairs, flat roof) in `bt-range`, placed twice | `Terrain/TerrainBuildingExpander.cs:65-143, 214-230`; `TerrainBuildings.cs:69-83`; `bt-range/buildings/house-2f.building.json`; `RecastNavmeshFactoryTests.cs:100-117, 166` | ⚠ house-2f has **no stair to the roof** (the expander allows one, `:122-128`) |
| windows | ✅ real OPENINGS: wall below the 0.9 m sill and above the 2.1 m head, nothing between — sight and rounds pass; a `glass` material exists, unused | `TerrainBuildingExpander.cs:34, 180-182, 226-227`; `materials.json:10` | — |
| window firing posts | ✅ `FindWindowFiringPosition`: a point on the inside face of each window at the lowest stance that clears the sill, filtered by seeing the target | `Spatial/Eqs/StarterTemplates.cs:32`; `TerrainCoverProvider.cs:122-129, 172-182` | a **set-back** post deep in the room (DA-6) |
| expose / hide / relocate | ✅ `PeekAndFire` + `WindowDuel` (window shape: prone under the sill, up at it, another window after 3 exposures; street shape: crouched behind cover, steps out, suppresses, relocates ≥ 8 m); exposure = max(`ExposeSeconds` 3 s, 2 × aim time 0.8 s) | `Brains/WindowDuelBehavior.cs:25-45`; `PeekAndFireNodes.cs:178, 233-234`; `AimAndFireExecutor.cs:819-821` | proficiency to set the timings (DA-2) |
| outside cover | ✅ static obstacles with a height: cover points stand ≥ 1.5 m, crouch ≥ 0.9 m, prone ≥ 0.45 m; `sandbags` and `earth-berm` materials; the shot and the "am I hidden" tests use the stance's eye and body points | `StaticObstacleDto.cs:15-28`; `TerrainCoverProvider.cs:70-73, 114-116`; `CheapLineOfSightTest.cs:101-131`; `materials.json:12-13` | — |
| dug-in holes, trenches | ⛔ none in code; D7 (authored polylines cut into height and navmesh) decided, not built | `DESIGN_Operations_Behaviour_Catalogue.md:243, 299, 316` | DA-8 |
| flank / retreat / fire points | ✅ `FindFlankingPosition` (side-on ring around the target) used by suppress-and-manoeuvre and `Flank` · `FindSafeRetreatPoint` (R-3004, always answers) · `FindOpenFiringPosition` · `FindCoverFromTarget` · `FindThrowingPosition` | `StarterTemplates.cs:11, 51, 75, 95`; `SuppressAndManeuverBehavior.cs:187` | `FindDefensivePosts`, `FindBuildingPosts`, `FindRoofPosts`, `FindOverwatchPosition` (catalogue rows only) |
| smoke | ✅ a Smoke warhead makes a cloud that passes 10 % of sight, blocks through not beside or over, thins at its edge; **TKB smoke grenade 6041** (8 m radius, 4 m high, 75 s) | `Combat/Systems/AreaEffectSystem.cs:64, 101-106`; `AreaEffectCloudTests.cs:130-153`; `MunitionTkbCatalog.cs:130, 304-306` | ⛔ **no loadout carries it, no behaviour throws it** (DA-12) · ⚠ the catalogue's "no smoke" row is stale |
| skill | ⛔ **none**: spread = mount `DispersionMils` × moving 2 × under fire 2 × stance (prone 0.5, crouched 0.75) × wounded arm 1.6; no shooter factor; deflection horizontal only | `Combat/HitModel.cs:45-58, 106-112` | DA-1, DA-2 |
| detection vs exposure | ⚠ seen the tick any ONE of 2–3 body points has a clear line; no exposure fraction, no acquisition time | `LosStrategies.cs:283-302, 438-440`; `ContactHysteresis.cs:8-10`; `VisualPerception.cs:115-131` | DA-3 |
| snipers | ⛔ no sniper or marksman weapon, no scope; the only optics are binoculars (×2 range, 10° FOV) | `Combat/HeldItems.cs:17, 20`; `VisualPerception.cs:63-68` | DA-13 |
| where a shot came from | ⛔ `NearMissEvent` carries the unit and the closest point, **not the shooter or the round's origin** | `Combat/Events/NearMissEvent.cs:16-26` | DA-14 (backend) |
| fire control | ✅ ROE `RoeFire { HoldFire, ReturnFire, FireAtWill }` + `ForceLevel` on the unit | `Behavior/Components/Roe.cs:9-35` | the trigger that lifts it (DA-7) |
| sectors | 🆕 designed: `EngagementArea` (per member, read by the target pick before the gate) | Building Clearing §7a (CB-4, CB-18) | — reuse |
| the platoon frame and reports | 🆕 designed: SL-0 (`Unit_InfantryPlatoon`, `CombatEffective`, a nested hierarchy run), RP-1 (`SquadReport` on the wire, `ReportOutbox`, `PlatoonPicture`) | Building Clearing §7 (`CE-2262`, `CE-2263`); backend BR-3 | — reuse; new report kinds (DA-15) |
| temperament | ✅ `CombatTemperament` (RiskAcceptance, Obedience) + designed Nerve, Resolve, stress, the break | AQ89; Temperament design | — reuse; kept apart from skill |
| retreat | ✅ `FallBack` (R-259), squad `BreakContact` | `SquadContactBehaviors.cs:191`; `FallBack.btree.json` | — reuse |

## 3. Classes

```mermaid
classDiagram
  direction LR
  class CombatProficiency { <<NEW component, beside CombatTemperament>> Proficiency 0-1 · Set }
  class ProficiencyOf { <<NEW reader>> SpreadFactor · ExposeFactor · ReactionDelay · BurstDiscipline · Tier }
  class ProficiencyTier { <<NEW enum>> Untrained · Militia · Regular · Veteran · Elite }
  class HitModel { <<EXISTING, + 1 factor>> Sigma x SpreadFactor }
  class PeekAndFire { <<EXISTING>> ExposeSeconds x ExposeFactor }
  class Repertoire { <<NEW pure>> tier to the plays and options a commander may use }
  class DefencePlan { <<NEW>> EA · trigger line · posts per element (primary, alternate, supplementary) · OP · reserve · fall-back }
  class DefendPositionPlay { <<NEW ISquadPlay, platoon and squad>> Occupy · Watch · Engage · Displace · FallBack }
  class AttackPositionPlay { <<NEW ISquadPlay, platoon>> Observe · SupportByFire · Approach · Isolate · Foothold · Clear or AssaultThrough · Consolidate }
  class ClearBuildingPlay { <<Building Clearing, squad>> }
  class IsolationPlan { <<Building Clearing>> }
  class EngagementArea { <<Building Clearing, component>> sectors per member }
  class PostTemplates { <<NEW EQS>> FindDefensivePosts · FindBuildingPosts · FindRoofPosts · FindOverwatchPosition }
  class FindWindowFiringPosition { <<EXISTING EQS>> }
  class SniperTeamBehavior { <<NEW library>> shooter + spotter · overwatch · few shots then move }
  class CounterSniperDrill { <<NEW squad reaction>> }
  class SquadReport { <<Building Clearing RP-1, + kinds>> PostsManned · Displacing · SniperContact · PhaseLine · Foothold · ObjectiveTaken }
  class PlatoonPicture { <<Building Clearing RP-1>> }
  class Roe { <<EXISTING>> RoeFire }
  CombatProficiency ..> ProficiencyOf
  ProficiencyOf ..> ProficiencyTier
  HitModel ..> ProficiencyOf : shooter
  PeekAndFire ..> ProficiencyOf : timings
  Repertoire ..> ProficiencyOf : the unit's tier
  DefendPositionPlay --> DefencePlan
  DefendPositionPlay ..> Repertoire
  DefencePlan ..> PostTemplates
  PostTemplates ..> FindWindowFiringPosition : reuses its window points
  DefendPositionPlay ..> EngagementArea : the sectors
  DefendPositionPlay ..> Roe : HoldFire until the trigger
  AttackPositionPlay ..> Repertoire
  AttackPositionPlay ..> IsolationPlan
  AttackPositionPlay ..> ClearBuildingPlay : the clear phase
  AttackPositionPlay ..> EngagementArea : shift and lift fire
  AttackPositionPlay --> PlatoonPicture
  SquadReport ..> PlatoonPicture
  SniperTeamBehavior ..> PostTemplates : FindOverwatchPosition
  CounterSniperDrill ..> SquadReport : SniperContact
```
*What the picture shows that prose hid:* skill reaches the world through only TWO existing places, the hit model's spread and peek's
timings, plus one new gate, the repertoire. Both platoon plays sit on the clearing design's platoon frame, its reports and its
`EngagementArea`, so sectors, shifting fire and "don't fire into rooms we hold" are one mechanism, not three. The building attack ENDS
in the existing `ClearBuildingPlay`, not a second clearing.

## 4. Sequences

### 4.1 Attack on a defended two-storey building (regular platoon)

```mermaid
sequenceDiagram
  participant PC as platoon commander (AttackPositionPlay)
  participant SBF as support squad
  participant AS as assault squad
  participant RS as reserve squad
  participant D as defenders (DefendPositionPlay)
  PC->>SBF: Observe, then SupportByFire from an overwatch line (EngagementArea = the windows facing the route)
  SBF-->>PC: Contact reports (posts seen at windows, storey)
  PC->>PC: axis = the side with the fewest known posts covering it (PlatoonPicture)
  PC->>SBF: suppress (high rate while AS moves)
  PC->>AS: Approach by bounding overwatch to the flank, ThrowSmoke at the open crossing
  D-->>D: posts in the EA fire, the flank posts displace to alternates
  AS-->>PC: PhaseLine (100 m, then 30 m)
  PC->>SBF: shift fire off the entry face, then lift at the breach
  PC->>AS: Isolate + Foothold at the entry point, then ClearBuilding (Building Clearing)
  AS-->>PC: RoomClear per room, Foothold, CombatIneffective if it runs short
  PC->>RS: relieve on CombatIneffective, or hold as the counterattack reserve
  AS-->>PC: BuildingClear
  PC->>PC: Consolidate = DefendPosition on the objective facing the expected counterattack
```
*What the picture shows that prose hid:* every phase change waits on a REPORT, not a clock. The 3 s report delay (R-2031) is the friction
that makes "lift fire" a real coordination risk. The reserve is a decision on a report, not a fixed phase.

### 4.2 Defence of the same building (militia vs regular, side by side)

```mermaid
sequenceDiagram
  participant DC as defending commander
  participant P as posts (window, roof, outside wall)
  participant OP as observation post
  DC->>DC: DefencePlan = posts from FindBuildingPosts / FindRoofPosts / FindDefensivePosts, filtered by the repertoire
  DC->>P: occupy (Regular: set back, sectors, RoeFire HoldFire)
  DC->>OP: forward OP (Regular only)
  OP-->>DC: Contact report (enemy approaching, outside the EA)
  alt Regular
    DC->>P: RoeFire FireAtWill when the PlatoonPicture puts an enemy inside the EA
  else Militia or Untrained
    P->>P: no trigger, fire at first sight at any range, at the sill, spray
  end
  P->>P: PeekAndFire (exposure x ExposeFactor), relocate to another window after N exposures
  P-->>DC: Casualty / Displacing reports
  DC->>P: displace to the alternate post (flanked, or element ineffective), or FallBack by the planned route
```
*What the picture shows that prose hid:* the tiers do not need two behaviours. The same play runs, and the repertoire turns off the OP, the
trigger line, the set-back and the sectors, which is exactly how irregulars differ.

## 5. Modules — who runs it each frame

```mermaid
graph TD
  TKB[TKB type: proficiency default] -->|creation| PRO[CombatProficiency]
  SCN[scenario: unit and per-man override] --> PRO
  BT[BrainTickSystem] --> PCMD[platoon commander, body-less: AttackPositionPlay or DefendPositionPlay]
  PCMD -->|AssignTacticalIntentEvent, Superior| SQ[squad commanders, body-less: their plays]
  SQ -->|member orders| MEN[soldiers: CombatPosture, PeekAndFire, SniperTeam]
  MEN -->|shots| HM[HitModel, every node that resolves fire]
  HM --> PRO
  SQ -->|SquadReport, wire| PIC[PlatoonPicture on the platoon's brain node]
  PIC --> PCMD
  EQS[EQS: post and overwatch templates] --> PCMD
  classDef new fill:#ffd,stroke:#aa0
  PCMD:::new
  PRO:::new
```
*Caption:* the only per-frame engine change is one factor in the hit model, read where fire is resolved; everything else runs on the brain
node inside plays the runner already ticks. ⚠ **Not reached today:** a platoon over infantry squads has never run (SL-0 makes it run first).

## 6. Decisions *(✅ approved 2026-10-18, R-2035 — 🔒 "Perfect."; DA-9 as amended in §10)*

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-1** | what skill is | **one `CombatProficiency` 0–1 per man, beside `CombatTemperament`**, five named tiers as presets: Untrained 0.1 · Militia 0.35 · Regular 0.6 · Veteran 0.8 · Elite 0.95. Set like temperament: a TKB type default (insurgent Militia, soldier Regular, police Regular), a **unit-level setting that fills every member not authored**, a per-man scenario override; unset reads Regular | many separate skills (marksmanship, fieldcraft, tactics): five sliders per man is unauthorable · folding it into temperament: will and ability are different (a brave novice, a careful expert) |
| **DA-2** | what skill changes in a man | through **`ProficiencyOf`**, each **exactly 1.0 / 0 at Regular** so every existing rail and demo is unchanged: spread × (Untrained 2.5 · Militia 1.6 · Regular 1.0 · Veteran 0.8 · Elite 0.65, linear between) · exposure time × (1.8 … 1.0 … 0.7) · reaction delay before the first shot at a new contact (+1.2 s … 0 … 0) · burst discipline (Untrained empties the magazine, Militia long bursts, Regular+ aimed / controlled) · cover discipline (below Regular: stays at the sill, does not change window after N exposures) | skill on the weapon: a rifle is not trained · range-dependent skill: the spread is already angular, so error grows with range |
| **DA-3** | detection vs exposure | ⭐ **acquisition time by exposure** (backend frame): a contact is acquired after a delay that grows with range and with how little of the man shows (body points visible of those his stance has) and shrinks with the observer's proficiency; **0 for a fully exposed man within 100 m** ⇒ close-range rails unchanged. Without it, a short peek and a set-back window post protect only against being HIT, never against being SEEN | keep instant detection: makes the peek, the set-back and the sniper's hide meaningless for sight · a full silhouette-area model: costlier, same behaviour |
| **DA-4** | the defence plan | **`DefencePlan` computed when the order is given**: the EA (the order's polygon, or the area in front of the position along the threat axis) · the threat axis (the order's, else from the picture) · per element **primary + alternate + supplementary** posts · an OP forward · a reserve (one squad of three) · a fall-back route (`FindSafeRetreatPoint` from the position). Re-planned only on a new axis (supplementary posts face it) | a fixed plan authored per scenario: works once · re-plan every frame: posts dither |
| **DA-5** | where the posts come from | **new EQS templates**, scored for: sees into the EA, cover from the threat axis, ≥ 8 m from the next post, **mutual support** (sees a neighbour's front): `FindDefensivePosts` (outside cover, walls, sandbags, berms), `FindBuildingPosts` (per storey, windows facing the EA, upper storey preferred, ground floor lightly held), `FindRoofPosts` (roof edge, prone, only if the roof is reachable). ⭐ An **authored fighting position** in the scenario (a point + facing) is preferred over a generated one | only authored posts: a defence needs hand-placing in every scenario · only generated: the scenario author cannot say "the MG goes here" |
| **DA-6** | fighting from a window | Regular+ fight from a **set-back post** 1.5–2.5 m inside the room on the line through the window (sees a narrower arc, is far harder to see, needs DA-3 to matter); below Regular **at the sill** (today's `FindWindowFiringPosition`). Observation is a lean / peek at the window frame (BR-5); a quick shot is `PeekAndFire`'s window shape with DA-2 timings, **relocating to another window after N exposures** (R-2023's rule for infantry) | always at the sill: the doctrinal error irregulars make, wrong for regulars · always set back: untrained should be easier to kill |
| **DA-7** | fire control in defence | Regular+ start at **`RoeFire.HoldFire`** with the EA as their `EngagementArea`; the commander lifts to `FireAtWill` when the picture puts an enemy inside the EA (or a post is fired upon: `ReturnFire` already allows it). Below Regular: `FireAtWill` from the start, no EA | a new "trigger" mechanism: ROE already holds and lifts fire, networked |
| **DA-8** | dug-in holes and trenches | ⭐ **phase 2, backend frame**: a `FightingPosition` world object (foxhole, trench section) that lowers its occupant's body points by its depth and joins the navmesh. ⭐ **Phase 1 uses what exists**: sandbag walls and earth berms as static obstacles (cover height already ruled by the stance thresholds) | build trenches first: the whole of D7 (height-grid carve, navmesh) for the same behaviour a 1 m berm gives |
| **DA-9** | the attack and the clearing | ⭐ **ONE platoon play `AttackPosition`**: Observe → SupportByFire → Approach → Isolate → Foothold → Clear (building: `ClearBuildingPlay` on the assault squad) or AssaultThrough (open position: suppress-and-manoeuvre's assault) → Consolidate (= `DefendPosition` on the objective). **`IsolateAndClear` (PL-1) becomes this play** — a clear with no opposition is the same play with an empty approach. ⚠ Changes the approved clearing design's platoon section: everything else in it stays | two plays chained: both would own "isolate" and the hand-off between them |
| **DA-10** | choosing the axis | the commander picks the **assault position on the side of the objective covered by the fewest KNOWN posts** (picture), reached by a covered route: v1 = `FindFlankingPosition` around the objective with `ThreatExposureTest` against the known posts, then bounding overwatch between cover. A path-exposure route planner is v2 | always frontal: the irregular's choice, kept for Untrained · a full route planner first: v1 already flanks |
| **DA-11** | support by fire, shift and lift | the SBF squad watches and suppresses the **windows / posts that see the assault's route** (its `EngagementArea`); on the assault's **PhaseLine** reports it shifts off the entry face, at **Foothold** it lifts to depth (the upper storey, the roof, the next building). Fire into a room the attackers hold is refused (CB-18, same component) | fixed timings: friendly fire whenever the assault is slow · no shifting: the assault walks into its own SBF |
| **DA-12** | smoke | smoke grenade 6041 into **Regular+ loadouts** (one or two per man); a `ThrowSmoke` member order at a point between the element and the known posts; the attack uses it at **open crossings** (the danger-crossing drill's open stretch) and on the **breach face**; a defender uses it to **break contact** | smoke only by the commander: a squad crossing a street would wait for a report round trip |
| **DA-13** | snipers | a **sniper team** (shooter + spotter) behaviour for both sides: `FindOverwatchPosition` (300–800 m, long line of sight to the objective, set back inside a building or behind cover) · targets in order: snipers, MG gunners, leaders, window posts · **few shots, then relocate** (R-2023) · the spotter reports `Contact`s up. ⚠ Needs from backend / TKB: a **sniper rifle** (≈ 0.3 mil spread) and a **designated-marksman rifle** (≈ 1 mil), a **scope** on the mount (×4 range, narrow FOV: the binocular rule on a weapon) | a sniper as a better rifleman: without the hide and the relocation he dies to the first SBF |
| **DA-14** | counter-sniper | a squad taking **near misses from an unseen long-range shooter** reports `SniperContact` (the bearing) and goes to cover; the commander answers by tier: Regular+ suppress the suspected building with the SBF, smoke, and send the own sniper or manoeuvre; below Regular, spray in the general direction. ⚠ Needs the **round's origin on `NearMissEvent`** (backend: the ballistics knows it) | guess the direction from the squad's contacts: an unseen sniper is not a contact |
| **DA-15** | coordination across teams | **only orders down and reports up** on the existing paths: `AssignTacticalIntentEvent` (Superior) to squads, `SquadReport` (R-2031) to the platoon; new kinds **`PostsManned · Displacing · SniperContact · PhaseLine · Foothold · ObjectiveTaken`** beside the clearing's. Both sides' commanders decide from their `PlatoonPicture`, never from the other squads' brains | a shared platoon blackboard: R-2031 forbids it ("nothing hidden inside single brain only") |
| **DA-16** | what the tier lets a commander do (the repertoire) | **Untrained**: defend = occupy the nearest windows and roof, no plan · attack = frontal rush with spray, no smoke. **Militia**: + a base of fire and a rush, posts at the sill, individual withdrawal. **Regular**: the full plans (sectors, trigger, OP, alternates; SBF, flank, bounding, smoke, shift / lift). **Veteran**: + reserve counterattack, set-back posts everywhere, a DMR. **Elite**: + multiple assault axes, roof entry when there is a way up (fast rope exists), a sniper team, smoke on every breach. The commander's tier is his unit's median man | the commander's own skill only: a skilled officer with untrained men cannot run a regular's plan |

## 7. Edge cases

| case | what happens |
|---|---|
| the defenders fall back UPWARD while the building is being cleared | rooms they leave are `Suspected` again for the clearing (Building Clearing `BuildingClearance`); the stair is a danger area; a defender who is cornered upstairs fights or surrenders by Resolve (CB-28) |
| the attack comes from an unexpected side | defenders re-plan on the new axis: they move to **supplementary** posts; untrained just turn and shoot from where they are |
| the SBF loses line of sight (smoke, its own losses) | it reports, and the commander holds the assault at the last phase line until fire is re-established or another squad takes over the SBF |
| the assault squad is shot below combat effective in the approach | `CombatIneffective` report; the reserve takes the assault (Regular+), else the attack is broken off by `BreakContact` |
| a defender sniper kills the attacking platoon leader | succession (Squad Tactics §7.2) at squad level; at platoon level the senior squad leader's commander takes the play, reported |
| ammo low on the SBF | the rate drops (the Ammo input already damps Suppress); a `CombatIneffective` with reason ammo when empty |
| civilians in the building | the clearing's civilian rules apply in the clear phase; the SBF fires only at seen combatants (ROE `Status`) |
| both sides untrained | no plans, no smoke, both spray: the fight is decided by numbers and cover, which is the point of the demo |
| a roof with no stair (house-2f) | `FindRoofPosts` returns nothing (not reachable); an Elite attacker may still fast-rope onto it |
| the defence breaks (stress) | men break individually (TS-6), and the post's element reports `Displacing`; the commander orders the fall-back if more than half are gone |

## 8. Slices *(behaviors `CE-2273`…`CE-2285`; backend BR-6…BR-11 in the handoff)*

| order | slice | owner | what | needs |
|---|---|---|---|---|
| 1 | **DA-A** `CE-2273` | behaviors | `CombatProficiency` + `ProficiencyOf` + tiers + unit fill + translator key (DA-1); neutral rail: Regular reads exactly today | — |
| 2 | **DA-B** (backend BR-6, BR-7) | backend (frame) | the spread factor in `HitModel` (DA-2) · acquisition time by exposure (DA-3) · the round's origin on `NearMissEvent` (DA-14) | DA-A |
| 3 | **DA-C** `CE-2274` | behaviors | the DA-2 timings in `PeekAndFire` (exposure, relocation, cover discipline, reaction delay, bursts) | DA-A |
| 4 | **DA-D** `CE-2275` | behaviors | `DefencePlan` + `FindDefensivePosts` / `FindBuildingPosts` / `FindRoofPosts` + set-back posts + `DefendPositionPlay` at **squad** level, sectors via `EngagementArea`, HoldFire and its lift (DA-4…DA-7); `DefendArea` maps to it | DA-C, Clearing BC-1 (`EngagementArea`) |
| 5 | **DA-E** `CE-2276` | behaviors | platoon defence: OP, reserve, alternates, displacement, fall-back, storeys; the report kinds (DA-15) | DA-D, SL-0, RP-1 |
| 6 | **DA-F** `CE-2277` | behaviors | `AttackPositionPlay` (DA-9…DA-11) absorbing PL-1: SBF, axis, approach, shift and lift, isolate, foothold, clear / assault through, consolidate | DA-E, Clearing BC-3 |
| 7 | **DA-G** `CE-2278` | behaviors + TKB | smoke loadout and `ThrowSmoke` (DA-12) | DA-F |
| 8 | **DA-H** `CE-2279` | behaviors + backend / TKB | sniper and DMR weapons, the scope, `FindOverwatchPosition`, `SniperTeamBehavior`, the counter-sniper drill (DA-13, DA-14) | DA-B, DA-F |
| 9 | **DA-I** `CE-2280` | behaviors | the repertoire gate in both commanders (DA-16) + the tier ladder of demos (§9) | DA-F…DA-H |
| — | **DA-J** (backend BR-11) | backend (frame, later) | `FightingPosition` holes and trenches (DA-8, D7) | — |
| 2a | **DA-K** `CE-2281` | behaviors + backend / TKB | roles (DA-19): the new `TacticalDesignation` values + fire team index, the element split by team, the MG first into the base of fire; the infantry LMGs and their carriers (TKB) | DA-A |
| 3a | **DA-L** `CE-2282` | behaviors | `EngagementArea` with `Allow` / `NoFire` in the five target picks and the two gates (DA-20) — ⭐ ONE build shared with Building Clearing BC-1 | — |
| 3b | **DA-M** `CE-2283` | behaviors + backend | the new sensor changes (DA-21); building / storey / room of a point from a PointSurvey answer (R-2037) | DA-L, DA-P |
| 6a | **DA-N** `CE-2284` | behaviors | `UrbanMovement`, `PlatoonSop`, the `ReactToFireFromBuilding` preset with clear / bypass (DA-17, DA-18) | DA-F, DA-M |
| 1a | **DA-O** `CE-2285` | behaviors (FDP Terrain, cross-lane) | `roof` as rings + `house-3f` with the roof stair house and parapet (DA-22) | — |
| 0 | **DA-P** (backend BR-12) | backend | the `WorldSurvey` sensor: VisibilityFan · BuildingSurvey · PointSurvey, solved on the muscle (DA-25, R-2037) | — |
| 0a | **DA-Q** `CE-2286` | behaviors | move behaviors' brain-side geometry reads onto WorldSurvey / EQS answers (§13b table) + the conformance rail | DA-P |

### 8a. DA-A in detail — `CombatProficiency`, its three sources and its one reader *(`CE-2273`, behaviors, 2026-10-19)*

**INVENTORY** (codebase-memory CLI `search_graph`, Class / Struct / Enum / Method, `.*(Proficien|Skill|Training|Veteran|Marksman).*` →
**0**; `.*Temperament.*` → `CombatTemperament`, `TemperamentOf`, `CombatTemperamentTranslator`; grep for the registration sites:
`HrotSharedComponentRegistry.cs:194` (every node), `HrotScenarioSerializerFactory.cs:26`; the TKB projection precedent
`BehaviorTkbTranslator` (ROE defaults). `check_index_coverage` NOT run — CLI only.)

```mermaid
classDiagram
  direction LR
  class CombatProficiency { <<NEW component 415, beside CombatTemperament>> Authored · Unit · TypeDefault · Set bits }
  class ProficiencyOf { <<NEW reader, the ONLY one>> Value · Tier · SpreadFactor · ExposeFactor · ReactionDelaySeconds · Bursts · KeepsCoverDiscipline }
  class ProficiencyTier { <<NEW enum>> Untrained · Militia · Regular · Veteran · Elite }
  class ProficiencyFillSystem { <<NEW, brain node>> a man's Unit = the nearest commander's Authored }
  class CombatProficiencyTranslator { <<NEW scenario key Proficiency>> a number or a tier name, Authored only }
  class BehaviorProfileDto { <<EXISTING TKB, + DefaultProficiency>> }
  class BehaviorTkbTranslator { <<EXISTING, + TypeDefault>> }
  class CombatTemperament { <<EXISTING, unchanged>> will, not ability }
  class UnitSubordinate { <<EXISTING>> Commander }
  class HitModel { <<backend BR-6>> Sigma x SpreadFactor }
  BehaviorProfileDto ..> BehaviorTkbTranslator
  BehaviorTkbTranslator --> CombatProficiency : TypeDefault at spawn
  CombatProficiencyTranslator --> CombatProficiency : Authored, man or unit
  ProficiencyFillSystem ..> UnitSubordinate : walks up, at most 8
  ProficiencyFillSystem --> CombatProficiency : Unit on each man
  ProficiencyOf ..> CombatProficiency : the man's own only
  ProficiencyOf ..> ProficiencyTier
  HitModel ..> ProficiencyOf
```
*What the picture shows that prose hid:* the reader looks at the man's OWN component only — never up the chain — so the hit model
(backend BR-6, on whichever node resolves the shot) needs nothing but the man. The unit's setting reaches him by the fill system
WRITING his `Unit` field, which is why "fill" in DA-1 is a writer and not a chain walk like `RoeOf` (CE-2289).

```mermaid
sequenceDiagram
  participant TKB as BehaviorTkbTranslator (spawn)
  participant SCN as CombatProficiencyTranslator (scenario load)
  participant FILL as ProficiencyFillSystem (each frame, owned men)
  participant MAN as the man's CombatProficiency
  participant RD as ProficiencyOf
  TKB->>MAN: TypeDefault = the type's DefaultProficiency (insurgent 0.35), if it has one
  SCN->>MAN: Authored = 0.8 (a per-man override), if authored
  SCN->>FILL: the squad entity has Authored = 0.1 (the unit setting)
  FILL->>MAN: Unit = 0.1 (the nearest commander with Authored)
  RD->>MAN: Authored if set, else Unit, else TypeDefault, else Regular 0.6
```
*What the picture shows that prose hid:* the three sources never overwrite each other — each has its own field and bit, and the
precedence lives in ONE place, the reader. A unit setting changed later (a scenario edit, a future order) reaches the men on the next
frame; a man moved to another squad takes the new squad's.

| ⭐ decision | lean | rejected — one line each |
|---|---|---|
| the component's shape | three values with their own `Set` bits (`Authored`, `Unit`, `TypeDefault`) — 0 is a legitimate proficiency, so "unset" is a bit (the `CombatTemperament` rule) | one value overwritten by each source: a later TKB projection or fill would clobber an authored override |
| the unit fill | `ProficiencyFillSystem` (CgfLogicPack, gated on authority, after `UnitCentroidSystem`): each owned man with a `UnitSubordinate` gets `Unit` = the nearest commander's `Authored` (≤ 8 levels), cleared when none | a chain read in `ProficiencyOf` (the `RoeOf` shape): the hit model would need the commander entity on the node that resolves the shot |
| the factors | exactly 1.0 / 0 at Regular (0.6) and with no component; linear between the five presets (DA-2's tables) | — |
| the scenario key | `"Proficiency"`: a number 0–1 or a tier name (`"Militia"`), on a man or a unit; only `Authored` is saved | a separate `"ProficiencyTier"` key: two spellings for one value |
| where the TKB default lives | `BehaviorProfileDto.DefaultProficiency` (0 = none ⇒ Regular); insurgent 505 = Militia 0.35 | a new descriptor: one float |

⚠ Not in DA-A: the wire (BR-6 decides whether the component replicates or the hit model runs on the brain node) · the repertoire's
"commander's tier = his unit's median man" (DA-16, `CE-2280`).

**As built (2026-10-19, `CE-2273`)** — as drawn above: `CombatProficiency` (component 415, registered on every node beside
`CombatTemperament`), `ProficiencyOf` (`Value`, `Tier`, `SpreadFactor`, `ExposeFactor`, `ReactionDelaySeconds`, `Bursts`,
`KeepsCoverDiscipline`), `ProficiencyFillSystem` (CgfLogicPack, after the staff), `BehaviorProfileDto.DefaultProficiency` projected by
`BehaviorTkbTranslator`, `CombatProficiencyTranslator` (scenario key `"Proficiency"`). Deviations:

| what | as built | why |
|---|---|---|
| the in-between factors DA-2 left open | exposure × 1.8 · **1.4** · 1.0 · **0.85** · 0.7; reaction delay 1.2 · **0.6** · 0 · 0 · 0 s (Untrained … Elite) | DA-2 named the ends and Regular; the middle values keep each line monotonic and linear between presets |
| the fill's reach | a man is filled only where his brain is owned (gated on `BehaviorState` authority on a cluster) | the component is written on its owner; a brain-less body keeps its own or its type's value |
| the insurgent type default | ⚠ **not set anywhere yet**: type 505 (Insurgent) has no TKB catalog entry to carry it | the field and its projection are built and railed; the first insurgent TKB entry sets `DefaultProficiency = 0.35` |

Rails: `CombatProficiencyTests` (neutral at Regular / none / unregistered — bit-exact; the presets and the line; precedence; the unit
fill, nearest wins, taken back; the TKB projection under an authored value) · `CombatProficiencyTranslatorTests` (number or tier
name, only the authored value saved) · `CgfLogicPackTests` (+1 system). Red-proof: the fill writing nothing reddens the fill rail.

## 9. Demos *(each with an HTTP live check and an in-process twin; `bt-range` and its two `house-2f`, a roof stair added for the roof demos)*

| demo | cast | must end with |
|---|---|---|
| `dp-squad-wall` | a regular squad behind sandbag walls defending a road, an enemy squad walking in | posts spread ≥ 8 m with overlapping arcs (posture trace) · nobody fires before the first enemy crosses the trigger line (`/combat/shots` times × positions) · then all fire within 1 s |
| `dp-insurgents-house` | six militia in `house-2f`, two on the upper storey, one on the roof, a regular squad approaching | they open fire at first sight (> 300 m), at the sill, and change window after their exposures · their hit rate is below the regulars' (`/combat/shots` hits / shots per side) |
| `ap-squad-vs-wall` | a regular squad attacking two men behind a wall | one element suppresses while the other flanks (`FindFlankingPosition`), then assaults; the wall's men are hit from the side |
| ⭐ `ap-platoon-house` | a regular platoon (support, assault, reserve) against eight regular defenders in `house-2f` | the full §4.1 sequence in the report log: `Contact` → `PhaseLine` ×2 → `Foothold` → `RoomClear` per room → `BuildingClear` → `PostsManned` (consolidate) · ⭐ **no SBF round crosses a window of a room the attackers then held** · the report log identical on CGF, SimHost and IG |
| `ap-untrained-rush` | a militia platoon attacking two regular squads dug behind berms | a frontal rush with no smoke; most attackers down before 50 m; the survivors break (`StressBreak`) |
| `ap-elite-smoke` | an elite platoon against militia in `house-2f` | smoke on the open crossing and the breach face, a second axis from the roof by fast rope, few attacker casualties |
| `sn-sniper-duel` | a sniper team on each side, 500 m apart, a regular squad with each | each sniper fires ≤ 3 shots per position then relocates · the squad under sniper fire reports `SniperContact` with a bearing within 15° of the true one and suppresses that building |
| `tier-ladder` | the same house, the same attack, run five times with both sides at each tier | the attacker's casualties fall and the time to `BuildingClear` falls as the tier rises (a table in the run report) |

## 10. One play, presets — the clearing stays its own ORDER *(✅ ruled 2026-10-18, R-2035)*

🔒 User, `2026-10-18`: *"Perfect. The building clearing might stay as separate, but in the way as you said, it is the same play, just with different presets."*

| preset (the order a person gives) | phases it runs | who uses it | default settings |
|---|---|---|---|
| **`ClearBuilding`** | Isolate → Foothold → Clear → Consolidate · the Approach runs only if the building fires | police, soldiers searching a building nobody defends | police: `Objective: Arrest`, `ForceLevel: NonLethal`, ROE `ReturnFire` · soldiers: `Eliminate` |
| **`AttackBuilding`** | Observe → SupportByFire → Approach → Isolate → Foothold → Clear → Consolidate | a building known to be defended | `Eliminate`, `FireAtWill` inside the objective |
| **`AttackPosition`** | Observe → SupportByFire → Approach → AssaultThrough → Consolidate | an open position: a wall line, a trench, a compound | `Eliminate` |
| **`ReactToFireFromBuilding`** | *(started by the SOP, never by a person)* the squad in contact IS the support by fire → Approach → Isolate → Foothold → Clear → Consolidate, or **Bypass** (DA-18) | a unit moving through town shot at from a building | inherits the paused task's ROE and objective |

⇒ Building Clearing's `IsolateAndClear` (CB-17/CB-18, PL-1 `CE-2264`) **is** the `ClearBuilding` preset of `AttackPositionPlay`.
Everything else that design ruled (the room drill, entry, sectors, reports, arrest, stun, casualties) is unchanged.

## 11. Composing the commander — moving through town, attacked, clearing, then carrying on

🔒 User, `2026-10-18`, on §11–§17: *"Approved"* (R-2036). *(✅ approved R-2036 — DA-17, DA-18)*

**What exists (measured 2026-10-18):**

| piece | does | where |
|---|---|---|
| **task · SOP · reaction** | a body-less squad commander runs an SOP (`BasicSquadSop`); a `SensorChangedEvent` wakes it; a reaction (origin Reaction, an urgency) **pauses** the task and the task **resumes in place** when the reaction ends; at most one paused task; any new order drops the paused one | `BrainTickSystem.cs:220-246, 445-454`; `BehaviorIngressSystem.cs:258-265, 298-320, 405-418, 459-485`; Decision Layer §4.1, §4.9 (R-199, `CE-2081`) |
| **a mission** | `MissionPlanQueue`: up to 8 phases, each a behaviour + a trigger (TimerElapsed · UnderAttack · HealthCritical · BehaviorFinished); a reaction's end does NOT advance the phase; a faulted phase halts the plan | `MissionComponents.cs:42-62, 140-170`; `MissionDirectorSystem.cs:102-103, 126-146, 211-229` |
| **moving** | `SquadMarch` (file, wedge, line), travelling / bounding overwatch; a danger-area sensor along the route (street crossing ≤ 40 m, **intersection** = a node of ≥ 3 roads, open ground ≥ 20 m) ⇒ `DangerAreaAhead` ⇒ `SquadDangerCrossing` | `SquadMarchBehavior.cs:34-100, 208-213`; `DangerAlongRouteClassifier.cs:3-37, 250`; `SquadDangerCrossingBehavior.cs:44-228` |
| **react to contact** | squad: 3 s return fire and hold, then decide once: Assault · BreakContact · Hold · Rescue; ⛔ **no building case** — the contact is a 2-D point | `SquadContactBehaviors.cs:279-520`; `SquadContactResponseDecision.cs:35-51` |
| **looking at windows** | ⛔ nothing: `Watch` = go there and hold, no facing; vision is 360° for every soldier until LD-1…LD-3 (look direction, designed) | `SquadManeuverRunner.cs:50-53`; Look Direction §1.1–§1.3 |

```mermaid
stateDiagram-v2
  direction LR
  state "TASK: UrbanMovement (route legs in params)" as Move
  state "REACTION: ReactToFireFromBuilding" as React
  state "decide (platoon, on the reports)" as Decide
  state "AttackBuilding phases" as Attack
  state "Bypass: suppress, smoke, cross" as Bypass
  state "Consolidate" as Cons
  [*] --> Move
  Move --> React : squad SOP reports FiredOnFromStructure, platoon SOP reacts (urgency UnderFire)
  React --> Decide : the squad in contact holds and returns fire (existing ReactToContact first 3 s)
  Decide --> Attack : the building dominates the rest of the route, or the task says clear
  Decide --> Bypass : off the route and the tier may bypass (Regular+)
  Attack --> Cons : BuildingClear report
  Bypass --> Move : past the building
  Cons --> Move : reaction ends, the task RESUMES at its current leg
```
*What the picture shows that prose hid:* the switch is **not** authored into the movement behaviour. The movement is the platoon's
TASK, the attack is a REACTION its SOP starts, and the existing gate pauses and resumes the task. ⚠ The one measured catch: when the
platoon orders its squads into the attack, **that new order drops each squad's own paused march leg** (`BehaviorIngressSystem.cs:417`).
So the platoon's resumed task re-issues the legs to the squads from where they now stand. Squads never resume on their own.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-17** | the town movement | a platoon task **`UrbanMovement`**: the route as legs in the play's own params *(not a `MissionPlanQueue` — Command Chain §7b)*; one squad leads in travelling overwatch, one bounds behind it, one is the reserve; each squad marches in a file along the building lines, each man given a **watch sector** (front, left windows, right windows, rear, upper storeys) by his place in the file (§13); intersections and open stretches go through the existing danger-area drill | one big "patrol" behaviour holding every reaction: the user's own SOP ruling (Decision Layer §4) forbids embedding reactions in tasks |
| **DA-18** | after fire from a building | the platoon SOP decides once from its picture: **clear** when the building is within 50 m of the remaining route, or the task says "secure the route" · **bypass** (suppress + smoke + cross out of its arcs) when it is off the route and the tier is Regular+ · Untrained and Militia always assault it head-on. Then the reaction ends and the task resumes | always clear: a unit would spend the day on every window that fires once · always bypass: leaves an enemy behind the column |

## 12. Roles in the squad *(✅ approved R-2036 — DA-19)*

**Measured:** roles exist as **`TacticalDesignation`** — `Commander · SquadLeader · Wingman · Support · TeamLeader · Medic`
(`Fdp.Core/CommandHierarchy/TacticalDesignation.cs:8-20`; TeamLeader and Medic are CGF-local, not on the NED wire enum), stored on
`UnitRoster.TacticalDesignations[]` and `UnitSubordinate.Designation`, set by the TKB composition (squad = 1 SquadLeader + 9 Wingman,
`BdcTkbCatalog.cs:748-749`) or the scenario (`"Designation": "Medic"`). **Read by only two things:** leader succession
(`SquadSensing.cs:92-170`) and casualty care's medic pick (`CasualtyCareBehavior.cs:262-265`). ⛔ **The element split and fire
allocation ignore roles** — the base of fire is "the first half of the roster" (`SuppressAndManeuverManeuver.cs:66-74`). ⛔ **No
infantry machine gun exists**: the only MachineGun-category weapons are a tank coax, crew-served M2 / DShK, M134, a sentry gun and
the ZU-23; no infantry type carries one; `M249` is only a ballistic reference row.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-19** | roles | **append** `AutomaticRifleman · MachineGunner · Grenadier · Marksman · AntiTank` to `TacticalDesignation`, plus a **fire team index** (A / B) per member — the doctrinal 9-man squad: a leader + two teams of four (team leader, automatic rifleman, grenadier, rifleman). Readers: the element split goes **by fire team**, not by roster halves · the base of fire and the SBF take the AR / MG first · a defence puts the MG on the post with the longest view down the EA (its final protective line) · the clearing stack never puts the AR first · the sniper team is two Marksmen · a casualty's role is taken over (the next rifleman picks up the AR's role). ⚠ Needs from backend / TKB: an **infantry LMG** (M249-class, and a PKM-class for the other side) and a soldier type carrying it; the wire enum gains the new values | a role component separate from the designation: two stores of one fact · roles inferred from the weapon carried: a rifleman who picks up the MG would need re-inferring every frame, and the medic carries a rifle |

### 12a. BR-8 as built *(backend, `CE-3253`, 2026-10-19 — cross-lane fold by backend)*

| piece | as built | where | rail |
|---|---|---|---|
| the roles | `TacticalDesignation` gains `AutomaticRifleman 7 · MachineGunner 8 · Grenadier 9 · Marksman 10 · AntiTank 11` (appended) | `Fdp.Core/CommandHierarchy/TacticalDesignation.cs` | — |
| on the wire | `eTacticalDesignation` carries EVERY value by the same name and value — ⭐ `TeamLeader 5` and `Medic 6` too (⛔ before, they crossed as raw numbers outside the enum) | `Hrot.Network.NED/GenericDescriptors.cs` | `TacticalDesignationMapperTests.CE3253_*` (every member, name + value) · `EntityInfoTranslatorTests.CE3253_*` (a MachineGunner arrives as one) |
| from a scenario | by name (`"designation": "MachineGunner"`) — the existing `UnitSubordinateTranslator` parse, nothing new | — | `UnitSubordinateTranslatorTests.CE3253_*` |
| the guns | `InfantryLmg` mount (mount 0, `MachineGun` category, belt, 1 in 5 traces, the rifleman's aim error): an **M249-class** (5.56, 200 belt, 750 rpm, 800 m) and a **PKM-class** (the 7.62 ball, 100 box, 650 rpm, 1000 m) | `NedTkbCatalog.InfantryLmg` | `NedTkbBuilderCombatTests.CE3253_*` |
| the men | `Infantry_AutomaticRifleman` **207** (US, faction 1) and `Infantry_MachineGunnerPkm` **208** (Russia, faction 2): the rifleman's body, sensor, injury, carrier and brain with the LMG | `TkbEntityTypes`, `NedTkbCatalog.MachineGunner` (BdcTkbCatalog.cs) | the same |
| ⛔ not here | the fire-team index, the element split by team, the MG first into the base of fire, role takeover (behaviors' `CE-2281`) · the squad's composition (still 1 SquadLeader + 9 Wingman — behaviors decides where the AR / MG go) · the grenadier's launcher and the AT gunner's weapon (no new weapon: the role only) | | |

### 12b. BR-9 as built — the sniper rifle, the DMR, the scope *(backend, `CE-3254`, 2026-10-19 — cross-lane fold by backend; DA-13)*

| piece | as built | where | rail |
|---|---|---|---|
| the scope (data) | `WeaponMountDto.ScopeMagnification` (0 / 1 = none) + `ScopeFovDegrees` (appended), mapped from the builder's `WeaponMount` | `WeaponSuiteDto.cs`, `SimCombatDef.cs`, `BdcTkbBuilder.cs` | `NedTkbBuilderCombatTests.CE3254_*` |
| the scope in use | ⭐ **the binocular rule on a weapon**: `HeldItems.ScopeUp` — AIMED (`WeaponPostureKind.Aimed`) with an own mount (`ActiveMount` < 32) that has a scope ⇒ `VisualPerception.Optics` multiplies the range and narrows the cone (binoculars first: one or the other); `HeldItems.IdentificationOptics` returns the magnification. Lowered (Ready / Idle) ⇒ the naked eye | `HeldItems.cs`, `VisualPerception.cs` | `HeldItemsTests.CE3254_*` (500 → 2000 m: a man at 600 m becomes reachable; ×4 identification) |
| the rifles | `ScopedRifle` mount (small arms, the 7.62 ball, ×4 scope, 1.5 s aim): **M24-class** sniper 0.3 mil, 5 rounds, 800 m, 4° · **M110-class** DMR 1 mil, 20 rounds, 600 m, 6° · **SVD-class** DMR 1 mil, 10 rounds, 600 m, 6° | `NedTkbCatalog.ScopedRifle` (BdcTkbCatalog.cs) | the same |
| the men | `Infantry_Sniper` **209** (US) · `Infantry_Marksman` **210** (US) · `Infantry_MarksmanSvd` **211** (Russia, faction 2) — the rifleman's body with the scoped rifle; the role is `TacticalDesignation.Marksman` (BR-8) | `TkbEntityTypes`, `NedTkbCatalog.MachineGunner` | the same |
| ⛔ not here | the sniper team (hide, few shots, relocate, the spotter — behaviors' `CE-2279`) · a spread factor for a better shooter (BR-6 — ✅ built since, §12c) | | |

### 12d. BR-7 as built — acquisition time by exposure *(backend, `CE-3260`, 2026-10-19 — the engine of DA-3; its design lives in [Sensors and Doctrine §5.7](../../DESIGN_Sensors_And_Doctrine.md), not here)*

`delay = 2 s × (max(0, range − 100 m) / 400 m + (1 − clear points / his stance's)) × the observer's factor` (Untrained 1.8 · Militia 1.4 ·
Regular 1.0 · Veteran 0.85 · Elite 0.7, through `ProficiencyOf`). A fully exposed man within 100 m: 0, as before. One of three points at
300 m: ≈ 2.3 s. Fully exposed at 300 m: 1 s. A hide longer than the lost threshold (20 ticks) starts the dwell over. ⭐ Only SIGHT waits
(vision, and thermal, which shares the sight test); a radar contact does not. ⇒ Your short peek and the set-back window post now protect
from being SEEN, not only from being hit (DA-3's point).

### 12c. BR-6 as built — the shooter's proficiency in the spread *(backend, `CE-3259`, 2026-10-19 — cross-lane fold by backend; DA-2)*

| piece | as built | where | rail |
|---|---|---|---|
| the factor | `HitModel.Sigma` × `ProficiencyOf.SpreadFactor(body)` — Untrained 2.5 · Militia 1.6 · Regular 1.0 · Veteran 0.8 · Elite 0.65, linear between; a man with no component and an authored Regular are **bit-identical** to before. `body` is `HitModel.BodyOf`: a man's own, a carried weapon system's carrier, a station weapon's host (a vehicle reads Regular) | `Combat/HitModel.cs` | `HitModelTests.CE3259_*` |
| ⭐ **who can read it** | ⚠ found while building: `CombatProficiency` was registered on every node but NOT replicated — the muscle, which resolves the shot (`FireProcessingSystem`), held only the TYPE default it projected at spawn; an authored "this squad is elite" never reached the spread. ⇒ **replicated**: Brain group, `dtCombatProficiency` (150), all three sources and the set bits (the precedence stays in `ProficiencyOf`, the one reader). BR-7's acquisition reads it on the muscle too | `EntityCombatProficiencyTranslators.cs`, `HrotOwnershipGroups`, `NedOwnershipGroupBinding` | `BodyPartReplicationTests.CE3259_*`, both descriptor-map rails |

### 12c. DA-K — what is built, what is blocked *(`CE-2281`, behaviors, 2026-10-19)*

**Measured** (a read-only sweep, 2026-10-19): only `EntityInfo.CommanderId` and `TacticalDesignation` cross the wire
(`GenericDescriptors.cs:164-185`); every node builds its OWN roster from `CmdAssignSubordinate` (`UnitHierarchySystem.cs:138-150`) or
on load (`GenesisMaterializationSystem.cs:206-219`); `UnitSubordinateTranslator` is not extraction-safe, so the brain node gets a
man's `UnitSubordinate` from the WIRE. Every squad play partitions on its first tick only, by roster halves or by who reported
(`SquadManeuverRunner.cs:346-353`). Succession rewrites the successor's designation locally and never marks it for the wire
(`SquadSensing.cs:116-118`); and a second `CmdAssignSubordinate` for the same commander appends the man again
(`UnitHierarchySystem.cs:117-122`, its class comment says so). A man's machine gun is his TKB TYPE's mount 0 (207 / 208), not an item.

```mermaid
classDiagram
  direction LR
  class SquadRoles { <<NEW, the ONE role reader>> Of(world, roster, slot) · CarriesSupportWeapon(role) }
  class SquadSensing { <<EXISTING, routed>> succession }
  class CasualtyCare { <<EXISTING, routed>> the medic pick }
  class SuppressAndManeuverPlay { <<EXISTING, + roles>> the AR / MG join the base of fire }
  class TacticalDesignation { <<EXISTING + BR-8>> AutomaticRifleman · MachineGunner · Grenadier · Marksman · AntiTank }
  class FireTeam { <<NOT BUILT: no carrier on the wire>> }
  class RoleTakeover { <<NOT BUILT: needs a weapon to pick up>> }
  SquadSensing ..> SquadRoles
  CasualtyCare ..> SquadRoles
  SuppressAndManeuverPlay ..> SquadRoles
  SquadRoles ..> TacticalDesignation
```
*What the picture shows that prose hid:* two of the three copies of "his role is his component's, else the roster's" already existed
(succession, the medic pick); the third reader would have been a third copy — `SquadRoles` is the one, and the two blocked boxes are
blocked for reasons outside this lane.

| piece | state | ⭐ lean for the blocked ones |
|---|---|---|
| the role reader | ✅ `SquadRoles.Of` (succession and the medic pick routed through it) | — |
| the AR / MG first into the base of fire | ✅ `SuppressAndManeuverPlay.Start`: after the split by reporters (or halves), every AR / MG goes to the base of fire, if a live man is left to assault | — |
| ⛔ the element split BY FIRE TEAM | **blocked**: a fire-team index has no carrier — a scenario value would never reach the brain node (it gets `UnitSubordinate` from the wire) | ⭐ a `FireTeam` byte beside the designation, on `UnitSubordinate`, the scenario key and **`EntityInfo`** (one more wire field, backend) — then one helper replaces the plays' halves |
| ⛔ role takeover on a casualty | **blocked**: the AR's gun is his TYPE's mount, nothing can pick it up; re-labelling a rifleman would send a man with no MG to the base of fire as one | ⭐ wait for a weapon pick-up design (a held item from a body, `ItemSources`); and make succession's designation change reach the wire first (mark it dirty, and fix the duplicate append) — both backend |

⚠ Neither blocked part is decided by this lane alone: the user rules on the fire-team carrier and on weapon pick-up.

## 13. Sectors — how they are assigned and how a man keeps to his *(✅ approved R-2036 — DA-20)*

**Measured:** ⛔ **no sector, arc or engagement-area restriction exists anywhere** (only a turret's traverse limit). A target is chosen
in **five places**: `EqsTacticsNodes.TopThreat` (`:322-348`, an ordered target first, else the `ThreatRankingDecision` utility),
`TopAim` (`:373-388`, a heard point), `PeekAndFireNodes.LockTarget` (`:527-535`), the squad's `ThreatMatrixAssignmentSystem`
(greedy, focus cap 2), and each play's `SquadManeuverRunner.TopContact` (`:703-708`). The **last check before a shot** is
`AimAndFireExecutor` (body twist → friendly on the line → neutral on the line → muzzle blocked → ROE, `:243-281`), plus
`FireAtPointExecutor.ShootAtPoint` (`:237-247`).

```mermaid
classDiagram
  direction LR
  class EngagementArea { <<NEW component on the member>> Allow shapes (arc or polygon) · NoFire shapes · WatchBearing · SetBy order id }
  class EngagementAreas { <<NEW pure>> Permits(member, point) · InWatch(member, point) }
  class TopThreat { <<EXISTING>> }
  class LockTarget { <<EXISTING PeekAndFire>> }
  class ThreatMatrixAssignmentSystem { <<EXISTING>> }
  class TopContact { <<EXISTING runner>> }
  class AimAndFireExecutor { <<EXISTING gate, + 1 check>> }
  class FireAtPointExecutor { <<EXISTING, + 1 check>> }
  class OrderParams { <<EXISTING order JSON>> carries the sector }
  OrderParams ..> EngagementArea : the member's own brain writes it from its order
  TopThreat ..> EngagementAreas : drop contacts outside Allow
  LockTarget ..> EngagementAreas
  ThreatMatrixAssignmentSystem ..> EngagementAreas : assign only inside each member's Allow
  TopContact ..> EngagementAreas : the squad's union
  AimAndFireExecutor ..> EngagementAreas : refuse a shot ending in NoFire
  FireAtPointExecutor ..> EngagementAreas
```
*What the picture shows that prose hid:* the sector is **two filters with different jobs**. `Allow` decides **what he CHOOSES** (in
the five target picks); `NoFire` decides **what he may NEVER hit** (in the gate). Only the second is a safety rule, and it is the one
that makes shifting and lifting fire safe.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-20** | sectors | one **`EngagementArea`** per member — the component Building Clearing already designed (CB-4, CB-18), now with two lists: **`Allow`** (arcs from his post: left and right bearing and a max range, or a polygon such as a building face's windows) and **`NoFire`** (polygons: rooms the friends hold, the assault element's area). **Who assigns:** the play of his commander writes the sector into the ORDER it gives him — the defence plan per post, the SBF its window faces, the clearing its room sectors, the march each man's watch sector by file place — and the member's own brain stamps the component from the order, so it crosses nodes with the order (R-2031: nothing hidden in one brain). **Shift and lift** = the platoon sends the SBF squad a new order with moved shapes. **How he uses it:** contacts outside `Allow` are not chosen, **except** one that hit or near-missed him in the last 5 s within 50 m (self-defence, as `ReturnFire` already reads); a shot whose line ends inside `NoFire` is **refused at the gate, always**. Below Regular: no `Allow` (they fire at anything); `NoFire` still applies, since it is the friends' safety. Once LD-1 lands, `WatchBearing` is also where he looks | a sector as a new firing rule only in the gate: he would keep choosing a target he may not shoot and freeze · a sector per behaviour: five places would each grow their own |

### 13a. Sector shapes, who finds them, who gets them — two levels *(✅ DA-24 approved 2026-10-18, R-2038; refines DA-20)*

**Measured:** ⭐ **a sector is already ruled to be an AREA ENTITY** (R-2003, user *"Sector as area for sure"*; Orders design F10): a
tactical graphic, a polygon (`EditablePolyline : IAreaOutline`, vertices relative to the entity's position,
`Hrot.Core/Components/Map/EditablePolyline.cs:20-29`), replicated as `MapVisualOverlay` (`Hrot.Network.NED/MapDescriptors.cs:60`),
picked in an order as an `EntityRef` with `[MapPickableEntity("tactical_graphics")]`. An operator can already give a unit one:
**Designate › Set sector…** (Orders §13.3, built, `CE-2149`). ⛔ Nothing reads it as a sector yet. Doctrine: the platoon leader gives
each squad its sector (left and right limits, depth); the squad leader gives each team and man his part of it, overlapping the
neighbour's.

```mermaid
classDiagram
  direction LR
  class SquadSector { <<AREA ENTITY, existing kind, R-2003>> polygon · owner unit · Allow or NoFire }
  class MemberArc { <<NEW value, in the member order>> origin · left bearing · right bearing · min and max range · storey band · relative (to the route) or fixed }
  class SectorPlanner { <<NEW pure>> SplitArea(area, squads) · ForMembers(squad sector, posts, roles) · Moving(file places) }
  class PlatoonPlay { <<AttackPosition, DefendPosition, UrbanMovement>> }
  class SquadPlay { <<SquadDefend, SupportByFire, Approach, march, clearing>> }
  class EngagementArea { <<NEW component, member and squad commander>> Allow · WatchBearing }
  class NoFireAreas { <<NEW pure>> NoFire areas owned by any unit in his chain of command }
  class RoomEntryPlan { <<Building Clearing>> room sectors }
  PlatoonPlay ..> SectorPlanner : SplitArea, when it plans or shifts
  PlatoonPlay --> SquadSector : creates one per squad, or uses the operator's
  PlatoonPlay ..> SquadPlay : the order carries the SquadSector ref
  SquadPlay ..> SectorPlanner : ForMembers, when it gives member orders
  SquadPlay ..> RoomEntryPlan : inside a room
  SquadPlay ..> MemberArc : one per member order
  MemberArc ..> EngagementArea : the member's brain stamps it
  SquadSector ..> EngagementArea : the squad commander's Allow
  NoFireAreas ..> SquadSector : the NoFire ones
```
*What the picture shows that prose hid:* the two levels use **two different shapes for a reason**. The squad's sector is a **polygon
entity**, so it is on the map, on the wire and can be drawn by a person. A man's part is a cheap **arc in his order**, so it can be
redone every time someone is hit or moves, without creating entities. **NoFire areas are never copied down:** the gate looks them up
through the man's chain of command, so one write by the platoon reaches every man on every node.

```mermaid
sequenceDiagram
  participant PL as platoon play
  participant SP as SectorPlanner
  participant SQ as squad play
  participant M as member brain
  participant G as target pick + gate
  PL->>SP: SplitArea(the EA or the objective or the route corridor, squads, their places)
  SP-->>PL: one polygon per squad (10 percent overlap with the neighbour)
  PL->>PL: create the SquadSector area entities (or take the operator's)
  PL->>SQ: order (SquadDefend / SupportByFire / march leg) + SquadSector ref
  SQ->>SP: ForMembers(the sector, each man's post, roles)
  SP-->>SQ: one MemberArc per man (MG on the long axis, team A left, team B right)
  SQ->>M: member order + MemberArc
  M->>M: EngagementArea.Allow = the arc
  G->>G: choose only inside Allow (self-defence excepted) · refuse any shot ending in a NoFire area of his chain
  Note over SQ: a man down or moved ⇒ ForMembers again, the neighbours widen
  Note over PL: shift or lift fire ⇒ move the SquadSector polygon, or add a NoFire area
```
*What the picture shows that prose hid:* the planner is asked **only at order time and on change** (a plan, a phase, a casualty, a
displacement, a shift of fire) — never per frame. A sector change is one order, which is why it crosses nodes.

| who | finds | from | when |
|---|---|---|---|
| **platoon commander** (`DefendPosition`) | one polygon per squad on the line: the EA split by bearing from the position's centre, each 10 % wider than its share (mutual support); the reserve gets none until committed | the order's EA, or the area in front of the position along the threat axis | the plan; a new threat axis (supplementary sectors) |
| **platoon commander** (`AttackPosition`) | the SBF squad: a strip in front of the objective's faces that see the assault's route (their windows); the assault squad: the entry face + a lateral boundary; a **NoFire** area around the assault squad's live men, and per room it holds | the objective building (a BuildingSurvey answer from the muscle, R-2037), the chosen axis | each phase; each `PhaseLine` / `Foothold` / `RoomClear` report |
| **platoon commander** (`UrbanMovement`) | relative sectors: lead squad the corridor ahead, the following squad the two building faces of the street with an upper-storey band, the rear squad behind | the route leg | each leg |
| **squad commander** (`SquadDefend`, `SupportByFire`) | each man's arc: the bearings from HIS post to the edges of the squad polygon, split between neighbours with ~10° overlap; the MG on the longest axis (its final protective line); team A the left half, team B the right | the squad sector, the posts, the roles (DA-19) | the order; a casualty (the neighbours widen); a displacement |
| **squad commander** (march) | by place in the file: point man front 90°, then alternating left and right building faces, every second man the upper storeys, the last man the rear | the file order | each leg; a casualty |
| **squad commander** (clearing) | the room sectors (`RoomEntryPlan`, Building Clearing CB-4) | the room graph | each room |
| **the operator** | any squad sector, by **Designate › Set sector** (built) | — | ⭐ an operator's sector wins over the planner's |

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-24** | the two levels and their shapes | **squad level = an AREA ENTITY polygon** (R-2003), created by the platoon play (or drawn by the operator), owned by the platoon, read by the squad commander's `EngagementArea`, `TopContact` and `ThreatMatrixAssignment` · **member level = an ARC in the member order** (origin, left and right bearing, range band, storey band; fixed, or relative to the route while moving), computed by the squad play with `SectorPlanner`, stamped by the member's brain · **NoFire = area entities owned by a unit**, honoured by every man whose chain of command includes that unit (looked up, never copied) · by tier: Untrained none · Militia squad level only · Regular+ both levels. v1 arcs come from geometry only; v2 clips each arc to what the man can actually see from his post (EQS line-of-sight samples) | member sectors as area entities: ten map objects per squad, recreated at every casualty · squad sectors as arcs: an operator could not draw one, and it would not be on the map · NoFire copied into every order: one missed copy is a friendly-fire bug |

### 13c. DA-L in detail — the mechanism *(`CE-2282`, behaviors, 2026-10-19 — leans refining the approved DA-20 / DA-24, for the user)*

**INVENTORY** (a read-only sweep of every site, 2026-10-19; codebase-memory CLI earlier found no `Sector` / `EngagementArea` /
`WatchBearing` / `FireArc` type — grep agrees; `check_index_coverage` NOT run, CLI only):

| need | measured | where |
|---|---|---|
| ⛔ ONE choke point for "what to shoot" | **none**. `TopThreat` serves BOTH hiding (TakeCover, FallBack, posture sensors) and fire (`PostureNodes.Fire`); `LockTarget` picks for PeekAndFire / VehicleFightFromCover; `ThreatMatrixAssignment` assigns per member; `TopContact` feeds the plays' Suppress orders | `EqsTacticsNodes.cs:322,223,235`; `PostureNodes.cs:443`; `PeekAndFireNodes.cs:527`; `ThreatMatrixAssignmentSystem.cs:105`; `SquadManeuverRunner.cs:693` |
| the gates | aimed: `AimAndFireExecutor.HoldOf` (:585, shared by the aimed and the turret path) and friendly / neutral on the line (:253, :261); blind `FireBlind` (:438-442); point `ShootAtPoint` (:257-263); the hidden-target throw (`HiddenTargetThrow.TryThrow`) | `AimAndFireExecutor.cs`, `FireAtPointExecutor.cs`, `HiddenTargetThrow.cs` |
| ⛔ who shot at me | nothing: `Hit` and `NearMiss` are timestamps in `RecentSenses`; `NearMissEvent` has the round's closest point but no shooter (the projectile's `Shooter` is not copied) | `RecentSenses.cs`; `NearMissEvent.cs:16-28`; `BallisticsSystem.cs:345-370` |
| an area | `EditablePolyline` (Hrot.Core) on every node, read from the toolkit as `IAreaOutline` through `WeatherField.TryOutline`; `PolygonMath.Contains` | `IAreaOutline.cs`; `WeatherField.cs:236`; `PolygonMath.cs:29` |
| a network id → entity | `NetworkIdResolver.ResolveNetworkId` (the ONE resolver) | `Replication/Services/NetworkIdResolver.cs:105` |
| the operator's sector | ⛔ `Designate › Set sector` writes a station-local remembered field, not an order field — no AI effect | `OrderDesignations.cs:24-112` |

```mermaid
classDiagram
  direction LR
  class EngagementArea { <<NEW component 417, on a man or a commander>> Arc origin left right range · AllowArea net id · WatchBearing · NoFire x4 net ids · SetBy }
  class SetEngagementAreaEvent { <<NEW event 3107, the SetRoeEvent twin>> replaces the set · all empty withdraws }
  class EngagementAreaSystem { <<NEW, Input, after ReportingRequirementsSystem>> origin-gated · a replica left to its owner }
  class EngagementAreas { <<NEW pure reader, the ONLY one>> Allows(view, man, point) · AllowsUnit(view, commander, point) · IsNoFire(view, shooter, point) · FirstAllowed }
  class ProficiencyOf { <<CE-2273>> Tier }
  class RecentSenses { <<EXISTING>> Hit · NearMiss times }
  class IAreaOutline { <<EXISTING>> the polygon of an area entity }
  class NetworkIdResolver { <<EXISTING>> }
  class PostureNodes_Fire { <<EXISTING, + Allow>> the top threat, else the first allowed one }
  class LockTarget { <<EXISTING, + Allow>> }
  class ThreatMatrixAssignment { <<EXISTING, + Allow>> a pair outside the man's Allow is not assigned }
  class TopContact { <<EXISTING, unchanged: it also moves the squad>> }
  class SquadSuppress { <<EXISTING runner order + Suppress fire step, + Allow>> }
  class AimAndFire_and_FireAtPoint { <<EXISTING gates, + NoFire>> HoldNoFireArea }
  SetEngagementAreaEvent ..> EngagementAreaSystem
  EngagementAreaSystem --> EngagementArea
  EngagementAreas ..> EngagementArea : the man's, and his commanders' NoFire
  EngagementAreas ..> ProficiencyOf : below Regular no member Allow, Untrained none
  EngagementAreas ..> RecentSenses : self-defence
  EngagementAreas ..> IAreaOutline
  EngagementAreas ..> NetworkIdResolver
  PostureNodes_Fire ..> EngagementAreas
  LockTarget ..> EngagementAreas
  ThreatMatrixAssignment ..> EngagementAreas
  SquadSuppress ..> EngagementAreas
  AimAndFire_and_FireAtPoint ..> EngagementAreas
```
*What the picture shows that prose hid:* with no single choke point, `Allow` must sit in each place that chooses what to
SHOOT — and never in `TopThreat` or `TopContact`, which also decide what to HIDE from and where to MOVE (a man hides from every threat, he shoots only inside
his sector). `NoFire` is one check at every gate. Everything reads one pure class; nothing else interprets the component.

```mermaid
sequenceDiagram
  participant PLAY as commander's play (later slices)
  participant BUS as the bus (and the wire, kind 4 later)
  participant SYS as EngagementAreaSystem (the man's brain node)
  participant PICK as a fire pick (posture, LockTarget, matrix, TopContact)
  participant GATE as the firing gate
  PLAY->>BUS: SetEngagementAreaEvent(man, arc, origin Superior) beside his order
  BUS->>SYS: origin-gated, replaces the set
  SYS->>SYS: EngagementArea on the man
  PICK->>PICK: the top threat outside Allow and no self-defence: take the first remembered one inside, or hold fire
  PLAY->>BUS: SetEngagementAreaEvent(platoon commander, NoFire = the assault's area)
  GATE->>GATE: the shot ends in a NoFire area of his chain: HoldNoFireArea, always (self-defence does not lift it)
```
*What the picture shows that prose hid:* the sector travels the way ROE and reporting do (CC-2's shape, R-2044), not inside every
order's params — so no play's params change and a shift of fire is one event.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-26** | how a sector reaches the man | ⭐ **the ROE twin** `SetEngagementAreaEvent` beside the order (origin-gated, replace, all-empty withdraws), the shape CC-2 (R-2044) chose for reporting; across nodes on the tactical-intent road like kinds 2 and 3 (a kind 4 — asked of backend) | inside every order's params (DA-20's first wording): each play's DTO grows a sector, and CB-42 lost to CC-2 for the same reason |
| **DA-27** | where `Allow` applies | ⭐ the FIRE picks: the posture fire step (falls back to the first remembered, engageable, allowed contact, else holds fire), `LockTarget`, the threat matrix (a pair outside the man's `Allow` is not assigned), the runner's **Suppress order** (the squad commander's `Allow`: outside it the member holds) and the **Suppress behaviour's fire step** (the man's own). **Not** `TopThreat` nor `TopContact` — both also decide where to HIDE and MOVE (break contact, flank, cover) | in `TopThreat`: a man would stop hiding from a threat outside his sector · only at the gate: DA-20's "he would freeze" |
| **DA-28** | self-defence | ⭐ a contact within **50 m** is allowed while the man has a `Hit` or `NearMiss` within **5 s** — the timestamps `ReturnFire` already reads; the shooter is not known today (DA-14's round origin, backend, will tighten it to "the one who shot at me") | wait for the round's origin: no sector would work in a fight until then |
| **DA-29** | tiers (DA-24) | ⭐ a man's own `Allow` counts from Regular up; a commander's (the squad level) from Militia up; Untrained none — read through `ProficiencyOf.Tier`. `NoFire` for everyone, always | the tier gate inside every pick: four copies |
| **DA-30** | shapes and ids | ⭐ `Allow` = an arc (origin, left / right compass bearing, min / max range) and / or an area entity (network id, the `PirArea` precedent), the union; `NoFire` = up to 4 area entities on the unit that owns them, looked up through the man's chain (`UnitSubordinate.Commander`, ≤ 8); compass bearings: 0 = north (+Y), 90 = east (+X) | entity handles in the component: not meaningful on another node, and the wire carries network ids |

⚠ **Not in this slice:** who WRITES sectors (the plays: `SquadDefend` `CE-2275`, the SBF, the clearing `CE-2257`, the march) and the
`SectorPlanner` (§13a) · the wire (kind 4, backend) — until it lands a superior on another node than the man's brain sets a sector with no
effect, exactly as CC-2 did before kind 3 · `WatchBearing` pointing the eyes (LD-3, `CE-2297`) · a commander's `NoFire` read on a man's
brain node in another process needs the commander's component there (the CE-2289 ROE question; backend's answer for ROE was to
replicate it).

**As built (2026-10-19, `CE-2282`)** — as drawn above, with DA-27 as corrected (the Suppress order and step, not `TopContact`):
`EngagementArea` (component 417, `CognitiveComponentRegistry`), `SetEngagementAreaEvent` (event 3107) + `EngagementAreaSystem`
(`MissionControlModule`, after `ReportingRequirementsSystem`), `EngagementAreas` (`Allows`, `AllowsUnit`, `IsNoFire`, `InArc`),
`EqsTacticsNodes.FirstAllowedRemembered`; the picks in `PostureNodes.Fire`, `PeekAndFireNodes.LockTarget`,
`ThreatMatrixAssignmentSystem`, `SquadManeuverRunner.Suppress`, `SuppressNodes.SuppressContact`; the gates in
`AimAndFireExecutor.HoldOf` + `FireBlind`, `FireAtPointExecutor.ShootAtPoint` (`obeyRoe`), `HiddenTargetThrow.TryThrow`, reason
`ActionReason.HoldNoFireArea`. Rails: `EngagementAreaTests` (6, four red-proofed) · `CgfLogicPackTests`, `MissionControlModuleTests`
(+1 input system). Backend asked for the kind 4, the commander's component on the men's nodes, and DA-14's round origin
([note](../../blueprints/batches/NOTE_Behaviors_To_Backend_Sectors.md)). ⚠ Nothing WRITES a sector yet — the plays that do are the next
slices (`CE-2275` SquadDefend, `CE-2257` the clearing, the march), so no demo shows it until then.

### 13b. Where the planner runs — the brain ASKS the muscle, then decides *(✅ rule R-2037; DA-25 the survey sensor, amended by the user 2026-10-18 — R-2038)*

🔒 User, `2026-10-18`, on DA-25: *"sensors on the simhost (perception role) might keep working and providing the data on the background and keep delivering the results automatically so The brain just reads them without re-asking and without waiting"* · *"Reasking every time is also valid if it goes with diffetent imputs."* · *"The room graph is probably something also the EQS query or the sensor can create on its own based on the building geometry. It shouldn't be something the scenario or terrain needs to provide. That way we could apply the behavior to any building without prior knowledge."*

⭐ **Measured — this is how EQS sensors already work:** a live sensor is **re-solved continuously** on a Perception node by `EqsSolverSystem` (Muscle tier, driven at 10 Hz; every live sensor ordered by band, then oldest result; a counted budget, `EqsSolverSystem.cs:15-25, 67-74`); each answer carries its `RefreshTick` (`EqsDdsTopics.cs:130-132`); the Brain picks the least-loaded Perception node per sensor (R-179, `EqsDdsTopics.cs:50-52`); **re-asking with new inputs** is `EqsChildSensor.Refresh(view, child, config)`, which bumps the epoch so only answers for the new inputs count (`EqsChildSensor.cs:168-190`). ⇒ `WorldSurvey` reuses exactly that: a **standing** sensor, not a request.

🔒 User, `2026-10-18`: *"The line of sight must always be a sensor or raycast query which goes to the muscle and returns back as a result.
 It is nothing that is calculatable immediately on the brain It always needs to go to muscle and back."* — because smoke,
destruction and doors change the world and *"only the muscle knows the current state of the environment"*. *"The final decisions, of
course, can stay on brain."* ⛔ The first version of this section (a synchronous planner reading `IWorldQuery` on the brain) is
SUPERSEDED — see HISTORY.

**What the planner needs, split by the rule:**

| the planner needs | kind | ⇒ where it comes from |
|---|---|---|
| the plan: EA, axis, which squad where, known enemy posts, roles, tier | brain data | the play itself — immediate |
| splitting a polygon by bearing, an arc around a post, the overlap | pure math, no world | immediate, on the brain |
| the posts (walkable, in cover, seeing the EA) | world | EQS, solved on the muscle (exists) |
| what each post can SEE across its arc (walls, windows, smoke, rubble) | world | 🆕 a **visibility fan** asked of the muscle |
| the building faces, windows, storeys, rooms, doors as they are NOW | world | 🆕 a **building survey** from the muscle — ⭐ it DERIVES the rooms itself from the building's current walls and openings (any building, no authored or pre-baked rooms) |
| which building / storey / room a point is in; heights and roof surfaces | world | 🆕 a **point survey** asked of the muscle (replaces BR-10's direct `BuildingOf`) |

```mermaid
sequenceDiagram
  participant C as commander play (brain)
  participant S as WorldSurvey child sensor (brain side)
  participant M as Perception node (SimHost: live world, smoke, doors, damage)
  participant P as SectorPlanner (pure, brain)
  C->>S: create once: posts (EQS template) + building survey + visibility fan per post
  S->>M: sensor config on the wire (the EQS road: owner, epoch)
  Note over C: only the FIRST answer is waited for, the squads keep their current orders
  loop standing sensor, re-solved in the background (10 Hz scheduler, banded, oldest first)
    M->>M: solve against the CURRENT world (rooms derived from the current walls)
    M-->>S: answer on the wire with its RefreshTick
  end
  S-->>C: SensorChange SurveyReady on the first answer and when it changes
  C->>P: Split(the latest answer, the plan) whenever the play needs it
  P-->>C: squad polygons + member arcs (pure, immediate)
  C->>C: create or move the SquadSector areas, send the orders that changed
  Note over C: NEW INPUTS (a new post, a new area) = Refresh(child, config): a new epoch, wait for its first answer
```
*What the picture shows that prose hid:* the wait happens **once per input set**, not per decision. After the first answer the sensor keeps
solving in the background and the brain just reads the latest; it re-asks only when the INPUTS change. The decision over the answer is
immediate and stays on the brain.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-25** | how the brain gets geometry | ⭐ **one `WorldSurvey` sensor in the EQS family** (backend frame): a child sensor of any unit (the same lifecycle, epoch and config / answer translators as EQS — `EqsChildSensor`), solved on the muscle against the live world (`Trace` with the sight purpose: walls, openings, doors, clouds, damage), its answer applied into a cognitive buffer on the brain (the danger-area sensor's shape) and announced by `SensorChange.SurveyReady`. **Standing** (R-2038): created once, re-solved in the background on its band, the brain reads the latest answer; new inputs go through `Refresh`. Three question shapes: **VisibilityFan** (≤ 12 origins, an arc each, eye height, range, ≤ 24 rays → per ray the free distance and what stopped it) · **BuildingSurvey** (an area or building → storeys, faces, window and door openings, and **the room graph it derives itself** from those walls and openings — rooms, portals, corners — so any building works with no prior knowledge; cached per building and re-derived when that building is damaged) · **PointSurvey** (points → building, storey, room, indoors, ground and surface heights). The answer carries its `RefreshTick`; a stale answer (older than its band's period × 3) is reported as stale, never silently used. ⭐ The SectorPlanner is a pure function over the latest answer | a raycast batch per ray: the existing `RaycastRequestEvent` solver casts against entity circles (`RaycastSolverSystem.cs:18-24`), ⛔ not measured whether it sees walls and smoke, and a fan would be hundreds of loose events · a new EQS generator only: an EQS answer is ranked points, not a visibility profile or a building's openings · the brain's terrain copy (the superseded lean): breaks the rule |

**Existing code that breaks the rule — found while measuring this** *(grep for `WorldQuery.Of`, `TerrainWorld`, `SightBlocked`,
`SegmentBlocked`, `InsideTest` in code that runs on the brain; codebase-memory CLI not used for this sweep — a text sweep, so the list is
what the pattern matched, not proof of completeness)*:

| where | reads on the brain | kind | owner |
|---|---|---|---|
| `IndirectFireBehaviors.Sees` (`:612-618`) | the observer sees the burst — `SightBlocked` | **line of sight** | behaviors |
| `VehicleFightFromCoverNodes.CanSeeMe` (`:652-663`) | can the remembered threat see me — `SightBlocked` | **line of sight** | behaviors |
| `CivilianBehavior.IsIndoors` (`:758-763`) | inside a building — `TerrainWorld` + `InsideTest` | **building geometry** | behaviors |
| `AirborneDrill` rope point / ring spots (`AirborneDrillBehaviors.cs:222-677`) | roof and ground surfaces | **building geometry**, heights | behaviors |
| `IndirectFireBehaviors` aim heights + `FireSolution.For` / `ClearsDensely` (`:88, 143, 307, 644`; `FireSolution.cs:82, 137`) | ground height, does the arc clear the terrain | heights, **geometry** | behaviors (call) / backend (`FireSolution`) |
| air behaviours: `GroundHeightAt` (`AirAttackBehaviors.cs:543, 1057`, `AirOrderBehaviors.cs:45`, `AirHolds.cs:110`) | ground height under an aircraft or a target | heights | behaviors |
| `DangerAreaSensorSystem.Rate` (brain half, `Squad/Systems/DangerAreaSensorSystem.cs`) | does the remembered contact see the area — `TerrainWorld.SegmentBlocked` | **line of sight** | backend (`CE-3072`) |
| `AimAndFireExecutor` (Brain executor set, `CgfLogicPack.cs:164-167`) → `LineOfFire.MuzzleMasked` (`AimAndFireExecutor.cs:268-275`; `LineOfFire.cs:67-83`, `TerrainPenetration.Carry`) | is the muzzle masked by a wall | **geometry** | backend |
| ⭐ *full sweep, 2026-10-18 (one read-only agent, every hit traced to its CGF registration):* `AirborneDrillBehaviors.Plan` (`:406-437`), the stick-drift wind (`:410` → `WeatherField.GroundZ`), `SquadInputs.CasualtyExposure` (`SquadInputs.cs:114-122` → `SightBlocked`) | landing ground, wind profile, sight to a casualty | heights, **sight** | behaviors |
| ⭐ *full sweep:* `FireAtPointExecutor.cs:121` (`FireSolution.For`), `AimAndFireExecutor.ReleaseCue` / `ReleaseAt` (`:637-653`), `DoorNodes.ApproachPoint` (`DoorNodes.cs:172-182`), `NetworkSpawningSystem.cs:217-220` (`ResolveLevel`), `AreaEffectCloudSystem.cs:65` and `Armament.Judge` → `WeatherField` ground height | arc / charge choice, bomb release, which side of a door, spawn level, cloud drift, fog visibility | **geometry**, heights | backend |

⚠ `AimPoint.Middle` itself reads stance heights only (no terrain) — corrected by the full sweep. ⚠ The editor builds the same Brain pack (`EditorSubsystem.cs:1627`), so every site above also runs offline. ⭐ The brain's terrain is for drawing the map (user, 2026-10-18).

⚠ Stated fairly: today's brain-side `SightBlocked` already multiplies in smoke clouds and doors (`TerrainWorldQuery.cs:69-76`) — but
only by mirroring state the muscle owns, and with no destruction at all; that mirroring is exactly what R-2037 forbids. ⭐ The fix is
tracked as `CE-2286` (behaviors' sites) and in the backend note; a conformance rail (an allow-list that may only shrink) keeps new
code from adding a brain-side geometry read.

## 14. New sensors and sensor changes *(✅ approved R-2036 — DA-21)*

**Measured:** a near miss and a hit carry **no shooter** (`NearMissEvent.cs:5-23`; `ThreatEvaluationSystem.cs:205`). ⭐ What gives
the shooter's position is the **gunshot SOUND**: `SoundEmissionSystem.cs:54-64` records it and it arrives as an anonymous
`SoundContactEvent` with an error radius (`SoundContactEvent.cs:8-30`), merged into memory (`TargetMemory.HearContact`). ⛔ Nothing
maps a position to a building. Existing squad changes: `SquadContact 11 · UnderFire 12 · AllClear 13 · LeaderDown 14 · MemberDown 15`,
plus `DangerAreaAhead` (an SOP condition).

| new | kind | raised when | read by |
|---|---|---|---|
| **`FiredOnFromStructure`** | squad `SensorChange` | a seen shooter, or a gunshot sound contact whose error circle mostly lies inside a building footprint, during `UnderFire`; carries building id + storey (+ the window when the room graph BR-1 places it) | the squad SOP (report up), the platoon SOP (DA-18) |
| **`SniperFire`** | member and squad change | near misses or hits with no seen shooter and the gunshot heard beyond 250 m (or not heard) | the counter-sniper reaction (DA-14) |
| **`ContactInEngagementArea`** | squad change, once per contact | a hostile contact enters the post's EA | the defence's fire-control lift (DA-7) |
| **`ContactOutsideSector`** (flanked) | member change | a hostile contact within 50 m outside his `Allow`, behind or beside him | the defender's displace to an alternate post (DA-4) |
| **`PositionUntenable`** | member change | ≥ 3 near misses or a friend down at the same post within 10 s | displace (DA-4) |
| — the defence's trigger line crossing | *(not a sensor)* | — | an EA test on the picture |
| `BuildingOf(point)` | ⛔ not a brain query (R-2037): a **PointSurvey** answer from the muscle (§13b) | — | `FiredOnFromStructure`, the axis choice — one round trip before the change is raised |

⚠ **DA-14's backend need is downgraded:** the round's origin on `NearMissEvent` is no longer required for v1, because the gunshot
sound already carries the shooter's position. It is kept as an option if sound proves too coarse at sniper ranges (⛔ not measured:
the audible range of a rifle shot in `SoundEmissionSystem`).

## 15. Every behaviour, what it does, how they work together

```mermaid
graph TD
  subgraph platoon[platoon commander, body-less]
    UM[UrbanMovement task]
    PSOP[PlatoonSop]
    APP[AttackPositionPlay presets]
    DPP[DefendPositionPlay]
  end
  subgraph squad[squad commanders, body-less]
    SSOP[BasicSquadSop]
    MARCH[SquadMarch / overwatch / DangerCrossing]
    RTC[ReactToContact]
    SBF[SupportByFirePlay]
    APR[Approach: BoundingOverwatch / FireAndMovement]
    ISO[IsolateBuildingPlay]
    CLR[ClearBuildingPlay]
    SDEF[SquadDefendPlay]
    CSN[CounterSniper]
  end
  subgraph men[soldiers]
    POST[OccupyPost / Displace]
    PEEK[PeekAndFire / WindowDuel]
    CP[CombatPosture]
    SNP[SniperTeam]
    SMK[ThrowSmoke]
    ROOM[clearing member behaviours]
  end
  UM -->|legs| MARCH
  SSOP -->|reaction| RTC
  RTC -->|FiredOnFromStructure report| PSOP
  PSOP -->|reaction pauses UM| APP
  APP -->|orders| SBF
  APP -->|orders| APR
  APP -->|orders| ISO
  APP -->|orders| CLR
  APP -->|Consolidate| DPP
  DPP -->|orders| SDEF
  SSOP -->|SniperFire| CSN
  SBF --> PEEK
  APR --> CP
  APR --> SMK
  CLR --> ROOM
  SDEF --> POST
  POST --> PEEK
  classDef new fill:#ffd,stroke:#aa0
  UM:::new
  PSOP:::new
  APP:::new
  DPP:::new
  SBF:::new
  SDEF:::new
  CSN:::new
  POST:::new
  SNP:::new
  SMK:::new
```
*Caption:* arrows down are ORDERS (`AssignTacticalIntentEvent` from a Superior, or a member order); every arrow up is a networked
`SquadReport` (R-2031). Yellow is new. Nothing talks sideways: two squads coordinate only through their platoon.

| behaviour | level | 🆕 / ✅ | what it does | started by | hands off to |
|---|---|---|---|---|---|
| `UrbanMovement` | platoon task | 🆕 | the route as legs; lead squad, follow squad, reserve; each squad marches with watch sectors | a person, the mission | `SquadMarch` per squad; paused by `PlatoonSop` |
| `PlatoonSop` | platoon SOP | 🆕 | answers squad reports: `FiredOnFromStructure` → clear or bypass (DA-18); `CombatIneffective` → relief; `SniperContact` → counter-sniper support | always running | `AttackPositionPlay` as a reaction |
| `AttackPositionPlay` | platoon | 🆕 | the presets of §10, phase by phase on reports | an order, or `PlatoonSop` | `SupportByFirePlay`, Approach, `IsolateBuildingPlay`, `ClearBuildingPlay`, `DefendPositionPlay` |
| `DefendPositionPlay` | platoon (and squad) | 🆕 | the `DefencePlan`: posts, sectors, OP, reserve, fall-back; fire control | an order, or Consolidate | `SquadDefendPlay` per squad |
| `SquadDefendPlay` | squad | 🆕 | gives each man his post and sector, lifts hold-fire on `ContactInEngagementArea`, displaces on reports | the platoon | `OccupyPost` per man |
| `SupportByFirePlay` | squad | 🆕 | occupies the SBF line, suppresses the window faces in its `Allow`, rate high while the assault moves, shifts and lifts on new orders | the platoon | members' `PeekAndFire` / `FireAtPoint` |
| `SquadMarch`, travelling / bounding overwatch, `SquadDangerCrossing` | squad | ✅ | movement in formation, bounds, the crossing drill at intersections and open stretches | `UrbanMovement` legs, the squad SOP | — |
| `ReactToContact` | squad reaction | ✅ + building case | first 3 s: return fire and hold; ⚙ now also raises `FiredOnFromStructure` when the contact is in a building | `BasicSquadSop` | the platoon via the report |
| Approach (`BoundingOverwatch`, `FireAndMovement`) | squad | ✅ + smoke | closes on the assault position by bounds, throws smoke at open stretches | the platoon | — |
| `IsolateBuildingPlay`, `ClearBuildingPlay` | squad | 🆕 (Building Clearing) | exits watched; the room-by-room drill | the platoon | the clearing member behaviours |
| `CounterSniper` | squad reaction | 🆕 | cover, report `SniperContact` with the bearing, suppress the suspected building (Regular+) | `SniperFire` | — |
| `CasualtyCare`, `BreakContact`, `FallBack` | squad / member | ✅ | as built | SOPs | — |
| `OccupyPost` / `Displace` | member | 🆕 | go to the post by a covered path, take its stance, face the sector, hold fire until lifted; move to the alternate on `ContactOutsideSector` / `PositionUntenable` | `SquadDefendPlay` | `PeekAndFire` at the post |
| `PeekAndFire` / `WindowDuel` | member | ✅ + proficiency | expose, fire, hide, relocate after N exposures | posts, SBF | — |
| `CombatPosture` | member | ✅ | the per-man posture decision (now damped by temperament, TS-5) | default | — |
| `SniperTeam` | member pair | 🆕 | overwatch hide, target priority, few shots then move, spotter reports | an order | — |
| `ThrowSmoke` | member order | 🆕 | throws grenade 6041 at a point | squad plays | — |
| `Surrender`, `SearchPerson`, `Detain`, … | member | ✅ / 🆕 (Clearing) | arrest and capture | the clearing drill | — |

## 16. The roof — a floor with a stair house *(✅ approved R-2036 — DA-22)*

🔒 User, `2026-10-18`: *"A small little house on top of the roof with a door that lead to the roof. The roof is then basically just
another floor with the hole for stairs and a small house on top of that."*

**Measured** (`TerrainBuildingExpander.cs`): walls are free segments **per storey**, not tied to the outline (`:91-102`); a storey's
floor is any set of rings (`:107`); a stair may go up to `storeys.Count` (`:122-128`); doors are allowed on any storey (`:156-243`).
⛔ But the **roof is either the full footprint at the top height or `"none"`** (`:139-141`) — no stair hole, no partial roof.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **DA-22** | how the roof is built | exactly the user's picture: the roof **is the top storey** — its floor rings leave the stairwell open, its only walls are the **stair house** (≈ 3 × 3 m, a door onto the roof) and a **1 m parapet** around the edge (low cover for a roof post, ruled by the existing stance thresholds) — and the template's `roof` accepts **rings** (like `floor`), here the stair house's footprint. One small expander change (FDP Terrain, a cross-lane edit said in the commit). New template **`house-3f`** in `bt-range`: two storeys + the roof storey, its stair from the ground to the roof | the untested workaround (`roof: "none"` + a wall-less extra storey as the stair house's lid): makes a phantom storey every reader would count |

## 17. Test scenarios *(✅ approved R-2036 — DA-23, each with an HTTP live check and an in-process twin)*

| scenario | the people and their settings | what happens | must end with |
|---|---|---|---|
| ⭐ **`tq-police-arrest-surprise`** | police platoon (Regular, `ClearBuilding`, `Arrest`, `NonLethal`, ROE `ReturnFire`, stun grenades) · 3 suspects in `house-3f`, **not defending** (Militia, idle in rooms, `Resolve` 0.2, one with a hidden pistol), 2 civilians | no fire ⇒ the Approach phase is **skipped**; isolate, a quiet foothold at the back door, rooms cleared with shouts and stun; the pistol man draws and is knocked down (non-lethal melee, R-2030 ruling) | every suspect `Detained` and `Restrained` · the pistol found by the search and laid down · **zero lethal rounds** (`/combat/shots`) · the report log `Foothold → RoomClear … → BuildingClear` with no `Contact` before the foothold |
| ⭐ **`tq-military-attack-defended`** | regular platoon (`AttackBuilding`, `Eliminate`, smoke) · 8 insurgents (Militia) running `DefendPosition` in `house-3f`: window posts on both storeys, 2 on the roof behind the parapet, an MG at the upper corner window, a sniper (Marksman) in a second building 300 m off | Observe → SBF on the window faces → the axis from the side with fewest known posts → bounds + smoke over the open street → foothold → clear top-down after the roof is suppressed | `BuildingClear` · defenders dead or surrendered by `Resolve` · `SniperFire` raised and the sniper's building suppressed · **no SBF round into a room the attackers held** · reports identical on CGF, SimHost, IG |
| ⭐ **`tq-urban-ambush-counterattack`** | regular platoon on `UrbanMovement` through `test-town` (3 legs, one intersection) · 4 insurgents (Militia, `Resolve` 0.9 two of them, 0.2 the others) in a building beside leg 2, `DefendPosition` preset **Ambush** (hold fire until the column is inside the EA) | the column crosses the intersection by the drill, is fired on, the squad in contact returns fire, reports `FiredOnFromStructure`; the platoon reacts with `ReactToFireFromBuilding` (the building is beside the route ⇒ clear); clear with `Eliminate`: the two who refuse and keep fighting are killed, the two who surrender are detained; consolidate; **the movement resumes at leg 2** | the trace: `UrbanMovement` paused → reaction → `BuildingClear` → `UrbanMovement` resumed at the same leg (`/entities/{id}` task state) · every non-surrendering insurgent dead, every surrendering one detained, none killed after surrendering · the column reaches leg 3's end |
| `tq-ambush-bypass` | the same, but the ambush building is 120 m off the route | `Bypass`: suppress + smoke + cross | no squad enters the building; the movement resumes |
| `tq-tier-ladder` | `tq-military-attack-defended` five times, both sides at each tier | — | attacker losses and time-to-clear fall as the tier rises |

## ⛔ HISTORY

- **2026-10-18, §13b first version (SUPERSEDED the same day by R-2037):** the SectorPlanner as a synchronous function on the brain reading building faces and line of sight through `IWorldQuery` (CGF loads the terrain). ⛔ The user ruled that the brain never reads world geometry: smoke, destruction and doors change it and only the muscle knows the current state. The rest of that version (posts from EQS, the answer carried by the area entity and the order) still holds.
