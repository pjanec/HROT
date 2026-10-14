<!--STATUS
state: LIVE
updated: 2026-10-13 (§9 params box: defaults R-3001/R-3002, no variable kinds, fluent HSM binding · §5a who can ask EQS · §9 groups: commanders, squads, orders · §10 vehicles: passengers, formations · §6 / §6a refreshed)
current-answer: the whole file — an EXPLAINER (map of what exists), not a design; every box names its owning design. ⭐ READ IT BEFORE designing any behaviour, squad or vehicle work; when you MEASURE something it does not say, ADD it here (user, 2026-10-11: "I need the guide to serve every other future you so it does not need to rederive the stuff that is available")
stale-below: none
known-rot: §1–§8 colours are a 2026-10-06 snapshot (§5a, §9, §10 measured 2026-10-11) — re-check the tracker before relying on a red or amber box
related-designs:
  - DESIGN_Injury_States.md — OWNS the injury states (CE-2155) that §8a summarises
  - DESIGN_Orders_Params_Entity_Classes.md — OWNS orders, params DTOs and entity classes (rules built 2026-10-12/13; the §9 order box was refreshed 2026-10-13, P7)
  - DESIGN_Peek_And_Fire.md — OWNS the library's fight-from-cover node (§10, CE-3158) that CombatPosture's TakeCover runs.
  - REVIEW_Behaviour_Library_Genericity.md — OWNS the genericity verdict on these blocks (library vs demo-local vs legacy) and the leans L1–L7.
  - docs/TUTORIAL_Behaviour_Composition.md — the GUIDE to choosing between blocks (host, decision, starter, sensing) and composing them
  - docs/DESIGN_Decision_Layer.md — owns task/SOP/reaction slots, ROE, utility hosting in BTree/HSM/blueprint (§3, §4)
  - docs/DESIGN_Sensors_And_Doctrine.md — owns sensor kinds, sensor children, memory stage, threat = danger × freshness (§5–§7)
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — owns thermal / acoustic sensing and anonymous heard contacts
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns EQS generators, tests, contexts, templates (§19 as-built)
  - docs/DESIGN_Eqs_Consuming_Behaviours.md — owns the EQS tactics nodes (TakeCover / FallBack / Flank / FiringPosition)
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — owns the U1–U7 demo scenarios and the danger-area sensor
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — owns the GAP list: what modern-operations behaviours need that is not on this map yet
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — owns the squad-tactics build (§9 here is its measured starting point)
  - docs/designs/group-maneuvers/Squad_Coordination_Design_v1_1.md — owns WHAT the squad layer is (primitives, manoeuvre tables, merge)
  - docs/DESIGN_Entity_Interactions.md — owns embarkation (§7, §7.1 plan) — §10 here is its measured state
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — owns the urban tank tactics proposal (hide / expose / reverse, danger to me); §10b here is its measured ground
-->

# Behaviour building blocks — an overview

What a behaviour author can build from **on top of** the BTree / HSM / Blueprint engines. One map per layer;
every node in the C# boxes is ONE shared implementation usable from all three hosts (R-174).

**Legend** — 🟩 built · 🟨 partial / caveat · 🟥 not built

## 1. The stack

```mermaid
graph TD
  subgraph WHO["Who decides"]
    OP["Operator / Superior order"] --> GATE
    SOP["SOP (standing doctrine)"] --> GATE
    REACT["Reaction (Alert…Hit)"] --> GATE
    GATE{{"Entry gate<br/>ranks origins"}}
  end
  GATE --> TASK["Task slot"]
  GATE --> SOPS["SOP slot"]
  TASK --> HOSTS
  SOPS --> HOSTS
  HOSTS["Behaviour hosts<br/>BTree · HSM · Blueprint"] --> NODES
  subgraph NODES["Shared building blocks"]
    UT["Utility decisions"]
    TAC["EQS tactics"]
    SN["Sensor nodes"]
    ORD["SOP order node"]
  end
  UT --> INP["22 standard + 9 squad inputs"]
  INP --> MEM
  SN --> SENS
  TAC --> EQS
  subgraph PERC["Perception"]
    SENS["Sensors<br/>visual · acoustic · thermal · danger area"] --> MEM["TargetMemory<br/>contacts + freshness"]
    EQS["EQS queries<br/>find a good spot"]
  end
  ROE["ROE per unit"] -. enforced in weapon executor .-> HOSTS
```

*What the picture shows:* everything a behaviour reads flows up from perception; everything that starts a
behaviour flows down through ONE gate — no host has its own path.

## 2. Who decides — task, SOP, reaction, ROE

```mermaid
graph LR
  subgraph RANK["Origin rank (higher wins)"]
    direction LR
    R1["SOP"] --> R2["Superior"] --> R3["Operator"]
  end
  subgraph URG["Reaction urgency (only higher replaces)"]
    direction LR
    U1["Alert"] --> U2["Contact"] --> U3["UnderFire"] --> U4["Hit"]
  end
  T["Task running"] -- "reaction arrives" --> P["Task PAUSED"]
  P -- "reaction ends" --> RES["Task RESUMES<br/>(not restarts)"]
  subgraph ROEBOX["ROE per unit"]
    F["Fire: HoldFire · ReturnFire · FireAtWill"]
    RE["Reactions: StayOnTask · React"]
  end
  subgraph SOPNODES["SOP authoring"]
    O1["SOP order node<br/>DoWhenIdle / React"]
    C1["Conditions<br/>SensedWithin · SensedFresh · InContact · RoeFire≥ / ≤"]
    E1["HSM events<br/>Sensor.Acquired · Lost · Hit · NearMiss · AllClear …<br/>Sensor.SquadContact · SquadUnderFire · SquadAllClear (on the leader)"]
  end
```

*Owner:* [DESIGN_Decision_Layer](DESIGN_Decision_Layer.md) §4 · R-199, R-200, R-203.

## 3. Utility decisions

```mermaid
graph TD
  subgraph DEC["Ready-made decisions (C#)"]
    D1["CombatPosture<br/>Advance · TakeCover · Suppress · Flee · Hold"]
    D2["AttackApproach<br/>Direct · Flank · FiringPosition"]
    D3["ThreatRanking"]
    D4["WeaponSelection"]
    D5["LeaderAssignment<br/>squad fire distribution"]
  end
  subgraph IN["Inputs (scored 0..1)"]
    I1["self: health · ammo · weapon ready · threat in sight"]
    I2["contact: danger · freshness · threat level · line of sight · health"]
    I3["EQS: top score · result count"]
    I4["weapon: range fit · effectiveness vs armour · rounds left"]
    I5["squad: knows contact · strength ratio · assigned role"]
  end
  IN --> DEC
  DEC --> BT["BTree: ChooseOption + IsOption(n)<br/>in Parallel + ObserverSelector"]
  DEC --> HSM["HSM: transitions guarded by IsOption"]
  DEC --> BP["Blueprint: ScoreDecision · ReadRankedResult"]
  ED["Utility editor"]:::missing
  classDef missing fill:#f8d7da,stroke:#c00
```

*Owner:* [DESIGN_Decision_Layer](DESIGN_Decision_Layer.md) §3 · R-201 (threat = danger × freshness), R-202.
🟥 The utility editor is a placeholder — new decisions are written in C#.

## 4. Sensors → memory

```mermaid
graph LR
  TKB["Unit type (TKB)<br/>vision / hearing range · sensor list · signatures"] --> CH["Sensor children<br/>one per sensor"]
  CH --> V["Visual<br/>range · FOV · terrain LOS"]:::built
  CH --> A["Acoustic<br/>anonymous point + radius + sound class"]:::built
  CH --> TH["Thermal<br/>heat signature filter"]:::partial
  CH --> DA["Danger area<br/>watched spots on a route"]:::built
  CH --> RA["Radar"]:::missing
  V --> MS["Memory stage<br/>debounce, merge"]
  A --> MS
  TH --> MS
  MS --> TM["TargetMemory<br/>id · position · Freshness · modality<br/>anonymous heard slots"]
  TM --> RD["Read at use time<br/>ThreatDanger: armed 1 · unarmed 0.3 · unknown 1"]
  BEH["Behaviour"] -- "retune · enable/disable · reset" --> CH
  BEH -- "SensorNodes: Sees · Read · ThreatsAtLeast<br/>BP: SpawnSensor · ReadSensorResult · When SensorResult" --> TM
  classDef built fill:#d4edda,stroke:#2a7
  classDef partial fill:#fff3cd,stroke:#c90
  classDef missing fill:#f8d7da,stroke:#c00
```

*Owner:* [Sensors & Doctrine](DESIGN_Sensors_And_Doctrine.md) §5–§7 · [Thermal & Acoustic](DESIGN_Thermal_And_Acoustic_Sensing.md).
🟨 Thermal: pipeline built, **no shipped unit has heat data**. 🟥 Radar: data shape only.

## 5. EQS — "find me a good spot"

```mermaid
graph TD
  subgraph Q["A query = template"]
    G["Generator<br/>candidate points"] --> TS["Tests<br/>filter + score"] --> R["Ranked results<br/>top 16"]
    CX["Contexts<br/>Self · Target · Leader · heard Point"] --> G
    CX --> TS
  end
  subgraph GEN["Generators"]
    g1["Cover points (terrain)"]:::built
    g2["Donut · Grid"]:::built
    g3["Entities in radius / area"]:::built
    g4["Cone · Offset · Navmesh samples"]:::partial
  end
  subgraph TST["Tests"]
    t1["Line of sight (cheap)"]:::built
    t2["Threat exposure · Distance · Flank bearing · Height · Path cost"]:::built
    t3["Force · Alive filters"]:::built
    t4["Reachability · Accurate LOS"]:::partial
  end
  subgraph TPL["Shipped templates"]
    p1["FindCoverFromTarget"]
    p2["FindSafeRetreatPoint"]
    p3["FindOpenFiringPosition"]
    p4["FindFlankingPosition"]
    p5["FindThreatsInView"]
    p6["EntitiesOfForceInArea"]
  end
  GEN --> Q
  TST --> Q
  Q --> TPL
  TPL --> USE["Used via<br/>BTree/HSM: MaintainEqsSensor · WaitForSensor<br/>BP: SpawnEqsSensor · ReadEqsResult · When EqsResult"]
  USE --> SOLV["Solved on SimHost<br/>identical queries solved once"]
  classDef built fill:#d4edda,stroke:#2a7
  classDef partial fill:#fff3cd,stroke:#c90
```

*Owner:* [EQS design](designs/eqs-2/EQS_Design_v1.3_final.md) §19 · [EQS-consuming behaviours](DESIGN_Eqs_Consuming_Behaviours.md).
🟨 Amber = built, but no shipped template uses it yet.

## 5a. Who can ask EQS, and what a query measures from *(measured 2026-10-11)*

| fact | where |
|---|---|
| ⭐ **ANY networked unit can own a query** — a soldier, a vehicle, a squad leader, even a leader with no body: a query is a child sensor of its owner (`EqsChildSensor.Ensure(view, owner, site, config)`) | `EqsChildSensor.cs:57` |
| a query measures from **slot 0** when set, else its owner (or the owner's parent) — so a body-less leader can measure from one of its members | `EqsContext.Self`, `EqsContext.cs:20-30` |
| **slot 1 / slot 2** are what it is aimed at: an entity, or a POINT (`ContextPoint1` + `Point1Bit`, e.g. a heard shot) — the owner need not have seen the entity | `EqsContext.cs:41-75`; `EqsTacticsNodes.cs:362-364` |
| it is **solved on SimHost** (the navmesh and terrain live there); the answer comes back to the owner's node | `EqsSensorConfigEgress/IngressTranslator.cs` |
| ⛔ **the AI node (CGF) has no navmesh** — a point computed there can land off the walkable area, and a move to a point more than 2 m from it FAILS (the soldier then retries forever). ⇒ **ask the terrain through EQS**, never compute a destination in the brain and hope | `DotRecastNavmeshProvider.cs:43,428-431`; `PostureNodes.cs:242-247` |

## 6. Ready-made behaviours to reuse

```mermaid
graph TD
  subgraph TAC["EQS tactics (BTree)"]
    a1["TakeCover"] --- a2["FallBack"] --- a3["Flank"] --- a4["FiringPosition"]
  end
  subgraph POS["Posture & movement"]
    b1["CombatPosture<br/>C#-built BTree (HSM / blueprint copies retired, R-266)"]
    b2["DangerCrossing<br/>BTree · Blueprint"]
    b3["Sentry"]
  end
  subgraph SOPB["SOP"]
    c1["BasicInfantrySop<br/>Hit→TakeCover · HoldFire contact→FallBack<br/>contact→TakeCover · else Idle"]
    c2["BasicSquadSop (commander)<br/>under fire while mounted → SquadDismount<br/>SquadUnderFire / SquadContact → ReactToContact"]
  end
  subgraph BASIC["Basic orders"]
    d1["MoveToLocation · AdvanceTo · FollowRoute · FireAtTarget · FireAtPoint<br/>TrackTarget · HoldStance · DoorLocksmith · Wander · Idle<br/>JoinFormation ⛔ broken — §10"]
  end
  subgraph LEARN["Learning material"]
    e1["BTree tutorials T01–T40"]
    e2["HSM showcases · feature-demo blueprints"]
  end
  c1 --> a1
  c1 --> a2
  b1 --> a3
  b1 --> a4
```

*What the picture shows:* the SOP and the posture decision are themselves built FROM the tactics — the same
composition an author would do.

## 6a. The library — what an author may assign, and how a block gets in *(R-256 L1)*

```mermaid
graph TD
  subgraph L1["① CAPABILITY NODES — shared C#, one per concept (R-174)"]
    PS["PostureNodes: Sensors · Fire (aimed, ONE step) · Advance · Hold · HoldProne"]
    PF["PeekAndFireNodes: fight from cover (CE-3158)"]
    ET["EqsTacticsNodes: FallBack · Flank · FiringPosition · TakeCover (hide only)"]
    FP["FireAtPoint · DoorNodes · EmbarkNodes (Embark, Disembark) · DangerArea · Sensor · SOP nodes"]
    UT["Utility decisions: CombatPosture · AttackApproach · ThreatRanking · WeaponSelection · SquadContactResponse<br/>ChooseOption (re-scores) · ChooseOptionOnce (keeps its first winner)"]
  end
  subgraph L2["② LIBRARY BEHAVIOURS — BTrees (R-258), assets an author assigns"]
    CP["CombatPosture"]
    TK["FallBack (THE retreat, R-259) · Flank · FiringPosition · TakeCover · Sentry · DangerCrossing"]
    SOP["BasicInfantrySop · BasicSquadSop (the leader, §9)"]
    ORD["MoveToLocation · AdvanceTo · FollowRoute · FireAtTarget · FireAtPoint · TrackTarget · HoldStance · DoorLocksmith · Embark · Disembark (JoinFormation: broken, §10)"]
    SQ["Library/Squad: BoundingOverwatch · SuppressAndManeuver · ReactToContact · BreakContact · SquadHold · SquadDismount (on the commander, §9)"]
  end
  subgraph L3["③ NOT LIBRARY — never offered as a building block"]
    DM["demo-local: hill attack (PlatoonHillAttack, HullDownAttackRun, SlotOps) · WindowDuel · insurgent"]
    LG["legacy: HideInCover_BT/_v2 · EqsCombatNodes · EqsLifecycleNodes · FleeExecutor (R-259)"]
    LR["learning: Authoring T01–T40 · showcases · blueprint feature witnesses · Demo_* stubs"]
  end
  CP --> PS
  CP --> PF
  CP --> ET
  CP --> UT
  TK --> ET
  SOP --> TK
  DM -.->|may use| L1
```

*What the picture shows that prose hid: a library behaviour is built only from ① nodes; ③ may use ① but nothing in ② may
use ③. CombatPosture's TakeCover option now runs the fight-from-cover node (`PeekAndFire`), so cover means "fire from it"
(Decision Layer §3.1).*

**Promotion rule — a block enters ② only when ALL four hold** *(R-256 L1)*:

| | rule | why |
|---|---|---|
| (a) | **no scenario constant** — only tunable defaults (a zero param = the default) | a constant from the demo it was born in makes it that demo's behaviour |
| (b) | **states its unit scope** — infantry / vehicle / any | e.g. `PeekAndFire` is infantry (stance or step peek) |
| (c) | **has a rail outside its originating demo** | the feature's own suite, not the scenario that motivated it |
| (d) | **is the ONE implementation of its concept** (ruling 9, R-174) | a second retreat, a second cover node is clutter — `FleeExecutor` stayed out for this (R-259) |

**Form** *(R-258)*: a library behaviour is a **BTree** (JSON or C#-built). A blueprint or HSM copy of a library behaviour is
kept only where it is not redundant or that host fits better; the three-host proof stays a DEMO (`ua-three-hosts`).

## 7. Demos — which building blocks each one exercises

```mermaid
graph LR
  S1["sop-demo"] --> K1["SOP · task · ROE"]
  S2["tt-take-cover"] --> K2["EQS cover · preview rewind"]
  S3["tt-posture · U1 ua-posture"] --> K3["CombatPosture"]
  S4["tt-heard-shot"] --> K4["Acoustic sensing"]
  S5["U2 ua-threat-ranking"] --> K5["ThreatRanking"]
  S6["U3 ua-three-hosts"] --> K6["One decision, three hosts"]
  S7["U4 ua-attack-approach"] --> K7["AttackApproach"]
  S8["U5 ua-weapon-choice"] --> K8["WeaponSelection"]
  S9["U6 ua-fire-distribution"] --> K9["LeaderAssignment"]
  S10["ua-danger-crossing"] --> K10["Danger-area sensor"]
  S11["U7 squad manoeuvre"]:::missing --> K11["ManeuverSelect"]:::missing
  classDef missing fill:#f8d7da,stroke:#c00
```

*Owner:* [DESIGN_Utility_AI_Demo_Scenarios](DESIGN_Utility_AI_Demo_Scenarios.md) §4 · runbook [RUNBOOK_Utility_AI_Demos](RUNBOOK_Utility_AI_Demos.md).
🟥 U7 waits on CE-507 decisions.

## 8. Gaps to know before authoring

```mermaid
graph TD
  G1["No utility editor<br/>decisions are C#"]:::missing
  G2["No heat data on shipped units<br/>thermal never fires"]:::partial
  G3["BTree / BP sensor nodes have no heard-point pin<br/>only C# tactics aim at a sound"]:::partial
  G4["Hurt unit on open ground: HoldProne<br/>stop + return fire — CE-3090"]
  G5["BTree inspector can't bind a stateful node's<br/>working state — CE-2099"]:::partial
  G6["HSM canvas: regions, 'On re-entry' history, Add Transition<br/>only on the ui branch"]:::partial
  classDef partial fill:#fff3cd,stroke:#c90
  classDef missing fill:#f8d7da,stroke:#c00
```

## 8a. Hurt, down, dead — what a behaviour can rely on *(CE-2155, measured and built 2026-10-13)*

| question | ✅ answer | where |
|---|---|---|
| the states | ok · wounded · incapacitated · dead, DERIVED from `Health` and the type's `Gen.Injury` band (no stored state, no topic) | `Combat/Injury.cs` — `Injury.StateOf` |
| who has a band | types that declare `Gen.Injury`: BDC rifleman, the four UrbanCombat soldier types. Everything else: ok until dead | [`DESIGN_Injury_States.md`](DESIGN_Injury_States.md) §8 |
| what "down" does | no move, no fire (stripped in `CombatLife.ApplyHitCapabilities`, enforced by the weapon / locomotion dispatchers); bleeds to death on the `Health` owner (`BleedOutSystem`) | §8 |
| is he a threat / a target | no — `ThreatDanger.IsOutOfTheFight` (danger 0; `HaveLiveTarget`, `ThreatInSight`, the squad strength sum) and the EQS `AliveFilterTest` all skip him. A blast still hits him | §8 ① |
| ⚠ the "wounded" a decision reads today | still the `HealthFraction` input (CE-3090's curves), not the state — the state's speed / aim effects are slice I-b | §6 I5 |

## 9. Groups — commanders, squads and orders *(measured 2026-10-11)*

```mermaid
graph TD
  H["UnitRoster (commander, up to 16) · UnitSubordinate.Commander (member)<br/>kept by UnitHierarchySystem · set in the scenario or the ORBAT"]:::built
  ORD["a commander orders a member: AssignTacticalIntentEvent (origin Superior)"]:::built
  TIR["TacticalIntentResolutionSystem: a mapper, or the intent id IS a behaviour name"]:::built
  MT["the member's TASK slot — its own SOP may still react (pauses the order unless ROE StayOnTask)"]:::built
  H --> ORD --> TIR --> MT
  MRG["SquadPerceptionMergeSystem → SquadCognitiveState.Contacts on the commander<br/>16 pooled contacts + who reported each · same frame a member's memory changes"]:::built
  FIRE["ThreatMatrixAssignmentSystem — who fires at whom (members pull it via IsAssignedTarget)"]:::built
  MRG --> FIRE
  MAN["6 manoeuvre TABLES: BoundingOverwatch · DangerAreaCrossing · SuppressAndManeuver · HillCrestHullDown · StackAndRoomEntry · TravellingOverwatch<br/>+ SquadHsmShell · partition · roles · slot rotation"]:::partial
  RUN["SquadManeuverRunner (CE-2123): one runner, a play per table — BoundingOverwatch · SuppressAndManeuver run on the commander"]:::built
  MAN --> RUN
  RUN --> ORD
  DEAD["no production caller: SquadEventIngressSystem · SquadVetoDetectionSystem · CommanderUtilityTickSystem (ManeuverSelect) · SquadMovementModeBroadcastSystem"]:::missing
  BP["Blueprint palette: Partition Elements · Assign Roles · Advance Phase · Acquire Slot — no shipped blueprint uses them"]:::partial
  classDef built fill:#d4edda,stroke:#2a7
  classDef partial fill:#fff3cd,stroke:#c90
  classDef missing fill:#f8d7da,stroke:#c00
```

*What the picture shows that prose hid: a squad is a COMMANDER entity plus orders — members only ever run their own behaviours
(usually `CombatPosture`) with an order's parameters; the squad's shared knowledge lives on the commander and members do NOT read
it (only through utility inputs no shipped decision uses) ⇒ a soldier knows only what it has seen itself.*

| need | how, today |
|---|---|
| make a squad | give members `UnitSubordinate.Commander` (scenario `CommanderGuid`, ORBAT drag-and-drop); the commander gets `UnitRoster` + `SquadCognitiveState` automatically |
| order a member | publish `AssignTacticalIntentEvent { Entity = member, IntentId = <behaviour name>, JsonParams, Origin = Superior }` (see `BoundingOverwatchBehavior.Order`) |
| run a squad drill | order the COMMANDER a Library/Squad behaviour (`BoundingOverwatch`, `SuppressAndManeuver`); a new drill = an `ISquadPlay` over its table on `SquadManeuverRunner` (`Brains/SquadManeuverRunner.cs`) — not a new node |
| move a member UNDER CONTACT | order `AdvanceTo { Objective, Speed, ArrivalRadius }` (Library/Infantry): it goes, firing at what it sees, and succeeds on arrival. ⛔ NOT `CombatPosture` with an objective — 📐 measured: with an enemy in sight it picks Suppress (healthy) or HoldProne (half health) and stays put; it is the HOLD order (`DESIGN_Squad_Tactics.md` §3.6) |
| know a member arrived | poll its position (the arrival events of `SquadEventIngressSystem` are dead — they key on intent ids the per-entity counter breaks) |
| what the squad knows | `SquadCognitiveState.Contacts` on the commander (entity, position, threat, last seen, `SourceMembersMask`, heard flag) — CGF only, not saved, not replicated |
| ask the terrain for the squad | the commander owns an EQS query (§5a), slot 0 = the member to measure from |
| commander-level reaction ("the squad is under fire") | ✅ (CE-2123 S2) `SquadCoordinationSystem` publishes `SensorChangedEvent{Unit = commander}` with `SquadContact` (pool 0 → >0) · `SquadUnderFire` (a member's or the leader's Hit / NearMiss) · `SquadAllClear` (pool → 0); the leader's SOP, `RecentSenses` and HSM events (`Sensor.SquadContact` …) take them exactly as a soldier's own. Give the leader the SOP `BasicSquadSop` ⇒ contact pauses its task with `ReactToContact` |
| what a squad does on contact | `ReactToContact` (Library/Squad): every member holds and returns fire for `DecideAfterSeconds`, the leader decides ONCE (`SquadContactResponseDecision` over `SquadStrengthRatio` / `SquadEnemyStrengthRatio`): assault (`SuppressAndManeuver`), `BreakContact` (its own `FindSafeRetreatPoint` query, element by element) or `SquadHold`; a drill that cannot start falls back to holding until clear. When it ends the paused task resumes — the runner re-takes the squad state another drill used (S2-G) |
| the unit's commander (R-272 (behaviors)) | ✅ `CE-2125`: a body-less entity — a TKB COMPOSITE type (e.g. `Unit_InfantrySquad` 302) gets `UnitEntity` on every node; the perception grid and the EQS area search skip it (`PhysicalPresence.IsHidden`, the one hidden check — since I-2c it also hides an enclosed passenger and guards the collider hash, the blast and the line-of-sight occluders); its owner (CGF) places it at its LIVE members' centroid (`UnitCentroidSystem`, CE-514). It never boards and carries nobody (`Embarkation.TryEmbark` refuses either way — its members board). It runs the squad SOP and drills; the leader soldier is a member designated `SquadLeader` with his own soldier SOP |
| the leader soldier falls | ✅ `CE-2125`: the first live `TeamLeader`, else the first live member, becomes `SquadLeader` (CGF-local); the commander is SILENT for its type's `CommandDisruptionSeconds` (6 s — the squad runner sends no orders, members keep theirs), then re-sends every order; `SquadLeaderDown` on the commander — `BasicSquadSop` reacts with `ReactToContact`, and a running `ReactToContact` holds and decides again |
| a commander is a soldier or a body-less entity | both exist: `ua-fire-distribution`'s Leader is a rifleman; `hill-attack-close`'s platoon has no body |

*Owners:* [Squad_Coordination_Design_v1_1](designs/group-maneuvers/Squad_Coordination_Design_v1_1.md) (what) ·
[DESIGN_Squad_Wiring](designs/group-maneuvers/DESIGN_Squad_Wiring.md) (where it runs) ·
[DESIGN_Squad_Tactics](designs/group-maneuvers/DESIGN_Squad_Tactics.md) (the runner, react to contact, the march).

> ⭐ **Orders, params DTOs and entity classes — BUILT (2026-10-12/13); owner: [DESIGN_Orders_Params_Entity_Classes.md](DESIGN_Orders_Params_Entity_Classes.md).**
> - one params DTO per behaviour: `[Behavior(Params = …)]`, or an asset's `ParamsDtoTypeId`; `NoParams` when it takes nothing (§1 P1, §5b);
> - flat JSON, case-insensitive through the one shared `FdpJsonOptionsRegistry.DefaultRelaxed`, enums as strings; DTOs are plain public FIELDS (§1 P2, §13.1 F12, §13.6);
> - every DTO carries `[ParamsDto]` (a `[Dto]` in Fdp.Core recording its origin), read by one `ParamsDtoRegistry`; a `.params.json` asset generates one (§13.7, §12); a mandatory field is `[Required]` (§13.1 F5, §13.11);
> - orders: `<Class><Order>` by the entity's DIS class (§8), one generic menu / RTS / HTTP path (§9), `GET /orders` lists them (§13.11); per-unit designations (target, point, route, area) fill a missing `[Required]` field on the sender (§13.3);
> - defaults *(R-3001/R-3002, CE-3194)*: a DTO's own defaults are its field INITIALISERS (+ a parameterless constructor) — never an attribute; an asset (BTree / HSM / blueprint) overrides single fields in `ParamsDefaultJson`; the bake is `new T()` → the asset's overrides → the order's JSON (`ParamsDefaults`, §13.10a);
> - no variable kinds *(CE-3193)*: a blueprint names its DTO and its Parameters are the DTO's fields (no declared Parameters); a BTree / HSM variable has no `Role` / `Scope` — working state is the variable a binding names as its `WorkingStateTargetField` (§13.10 ⑦);
> - a hand-written (fluent C#) HSM binds params and working state by selector, `state.Activity(bb => bb.Order, bb => bb.State, Run)`, and every HSM action reads its OWN block, so a hosted HSM is correct (CE-3163, [DESIGN_Behavior_Action_Binding](blueprints/DESIGN_Behavior_Action_Binding.md) §5.7);
> - the floating order form is built (P5b, CE-1059); editor panels show `Params`' fields and pick the DTO (CE-1061); not yet: editing an asset's defaults in the editor (CE-1061 U5b, ui) and the params-asset editor (B4b, postponed).

## 10. Vehicles — passengers and formations *(measured 2026-10-11; passengers updated 2026-10-12)*

| mechanism | state | detail |
|---|---|---|
| ⭐ **board / get out / eject** | ✅ `2026-10-12` (CE-3110 I-2a/I-2b): ONE rules class `Embarkation` and ONE applier `EmbarkationSystem`, which runs only where the vehicle's passenger list (`dtPassengerBuffer`, Brain group) is owned; the AI executors (`EmbarkExecutor`, `EjectPassengersExecutor`) raise commands that the interaction transport carries to that owner. The list replicates; `EmbarkedStateSystem` derives the passenger side on every node | [`DESIGN_Entity_Interactions.md`](DESIGN_Entity_Interactions.md) §7.1b, §7.1d |
| ⭐ where it runs | ✅ every node that can own a live entity: the cross-role `CoreInfrastructureCapabilities.Interactions` (doors + embarkation) in every host's plan — never a host or a role pack | §7.1d |
| ⭐ what "inside" means | ✅ `2026-10-12` (I-2c, R-271): CARRIED (the passenger's position owner copies the vehicle's transform; kinematics skips an embarked body), HIDDEN when the cabin is enclosed (`PhysicalPresence.IsHidden` — the perception grid, the EQS area search, the collider hash for bullets / near misses / radius search / avoidance, the blast and line-of-sight occluders all ask it), DIES when the vehicle is destroyed (a lethal damage event on the node owning its Health). An OPEN cabin type (TKB `VehicleParametersDto.PassengerExposure = Open` → `OpenPassengerCabin`) carries without hiding. CanMove / CanShoot are stripped aboard; boarding is not "mobility lost" | §7.2, §7.2a |
| ⚠ a vehicle feeling a hit *(measured 2026-10-12, CE-1056)* | a unit's `Hit` edge comes from `ThreatEvaluationSystem`, which walks units WITH a `TargetMemory` (`ThreatEvaluationSystem.cs:76`); a vehicle type without sensors (the APC 2001) has none ⇒ no Hit edge, no `SquadUnderFire` for the squad it carries. `tt-convoy-ambush` authors an empty `TargetMemory` on the APC | Squad Tactics §5a.1 |
| ⭐ the plan (user-approved) | I-2a ✅ → I-2b ✅ → I-2c ✅ → I-2d ✅ `2026-10-12` (`CE-1056`): the AI asks — `Embark` (walks to the boarding point beside the hull, then the embark action) / `Disembark` (one passenger, interaction action 2, `DisembarkExecutor`), library behaviours of the same names; the squad `SquadDismount` drill (everyone out, done when nobody is aboard); a CARRIER's hit / near miss puts the squad it carries under fire, and `BasicSquadSop`'s first row is "under fire while mounted → SquadDismount" | §7.1 / §7.3 · Squad Tactics §5a · `CE-3110` · `CE-1056` |
| **formations** (`FormationController` / `FormationFollower` / `FormationTargetSystem`, CarKinem) | 🟥 for AI: works only in the standalone CarKinem example. `JoinFormation`'s params are empty, `VehicleAPI` is never built in production, nothing carries a join from CGF to SimHost; followers steer straight at a slot with no path finding | proper formations = their own programme, `CE-2124` |
| ⭐ squad formation **for now** | ✅ BUILT `2026-10-12`: Library/Squad `SquadMarch` (File / Wedge / Line) — the point man nearest the objective leads, the others keep slots on it and fall back to file on his trail; live on `tt-squad-march` | [`DESIGN_Squad_Tactics.md`](designs/group-maneuvers/DESIGN_Squad_Tactics.md) §6.1, §6.1a |

## 10b. Vehicles in combat — what a tank can do today *(measured 2026-10-14)*

| need | ✅ / 🟨 / 🟥 | where |
|---|---|---|
| back up | 🟨 `MoveTo` with `ReverseAllowed` reverses only while the point is BEHIND the hull; only `HullDownAttackRun` sets it; no AI reverse rail; no pivot turn | `CarKinematicsSystem.cs:329-335`, `HillAttackTankNodes.cs:493` |
| aim independently of the hull, keep the gun on a target while hidden | ✅ turret part + `TrackTarget` | `TurretSlew.cs`, `TrackTargetExecutor.cs` |
| armour by facing, AI weapon effectiveness | ✅ one `ArmorModel` (R-212) | `ArmorModel.cs`, `StandardInputs.cs:398` |
| know an enemy is dangerous TO ME (an RPG man vs a rifleman) | 🟥 `ThreatDanger.Of` = armed 1 / unarmed 0.3, `self` unused | `ThreatDanger.cs:21-28` |
| a hide point / firing point for a hull | 🟥 cover points sit 0.75 m off walls (man-sized), exposure tested at the soldier eye | `TerrainCoverProvider.cs:22-23,70` |
| one exposure: creep until seen → fire → reverse | 🟨 demo-local `HullDownAttackRun` (hill attack) | `HillAttackTankNodes.cs:592-603` |
| ⚠ where a round lands | rounds are real flying entities (straight, ≈ 2 s life); a hit from straight above counts as the FRONT; no top face; a vehicle is hit as a 2-D circle at any height; no guidance, lock or min range | `ArmorModel.cs:29-38`, `CombatConstants.cs:80`, `RaycastSolverSystem.cs:157` |
| hide from a drone | 🟨 a vertical `SightBlocked` already sees slabs / decks / roofs (test-town garage deck, highway-rest overpass fit a vehicle); no EQS test asks; no drone exists | `TerrainWorld.cs:390-412` |
| ⚠ weapons and numbers | tanks: main gun + APFSDS only; the RPG `Insurgent` has 1 rocket, no rifle; RPG 300 mm cannot hurt an M1's front (side ≈ 57 of 3000 HP) | `BdcTkbCatalog.cs`, `UrbanCombatTkbCatalog.cs:81-89,214-229` |

*Owner of the next step:* [DESIGN_Urban_Armour_Tactics](designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md) §4 (V1–V7 approved 2026-10-14; round 2 M1–M7, V8 and decisions D-A…D-H open).
