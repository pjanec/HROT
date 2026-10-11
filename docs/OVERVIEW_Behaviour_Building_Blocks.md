<!--STATUS
state: LIVE
updated: 2026-10-11 (§5a who can ask EQS · §9 groups: commanders, squads, orders · §10 vehicles: passengers, formations · §6 / §6a refreshed)
current-answer: the whole file — an EXPLAINER (map of what exists), not a design; every box names its owning design. ⭐ READ IT BEFORE designing any behaviour, squad or vehicle work; when you MEASURE something it does not say, ADD it here (user, 2026-10-11: "I need the guide to serve every other future you so it does not need to rederive the stuff that is available")
stale-below: none
known-rot: §1–§8 colours are a 2026-10-06 snapshot (§5a, §9, §10 measured 2026-10-11) — re-check the tracker before relying on a red or amber box
related-designs:
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
    c2["BasicSquadSop (leader)<br/>SquadUnderFire / SquadContact → ReactToContact"]
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
    FP["FireAtPoint · DoorNodes · DangerArea · Sensor · SOP nodes"]
    UT["Utility decisions: CombatPosture · AttackApproach · ThreatRanking · WeaponSelection · SquadContactResponse<br/>ChooseOption (re-scores) · ChooseOptionOnce (keeps its first winner)"]
  end
  subgraph L2["② LIBRARY BEHAVIOURS — BTrees (R-258), assets an author assigns"]
    CP["CombatPosture"]
    TK["FallBack (THE retreat, R-259) · Flank · FiringPosition · TakeCover · Sentry · DangerCrossing"]
    SOP["BasicInfantrySop · BasicSquadSop (the leader, §9)"]
    ORD["MoveToLocation · AdvanceTo · FollowRoute · FireAtTarget · FireAtPoint · TrackTarget · HoldStance · DoorLocksmith (JoinFormation: broken, §10)"]
    SQ["Library/Squad: BoundingOverwatch · SuppressAndManeuver · ReactToContact · BreakContact · SquadHold (on the commander, §9)"]
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
| the unit's commander (R-272) | ✅ `CE-2125`: a body-less entity — a TKB COMPOSITE type (e.g. `Unit_InfantrySquad` 302) gets `UnitEntity` on every node; the perception grid and the EQS area search skip it (`PhysicalPresence.IsHidden`, the one check R-271's hidden passenger will join); its owner (CGF) places it at its LIVE members' centroid (`UnitCentroidSystem`, CE-514). It runs the squad SOP and drills; the leader soldier is a member designated `SquadLeader` with his own soldier SOP |
| the leader soldier falls | ✅ `CE-2125`: the first live `TeamLeader`, else the first live member, becomes `SquadLeader` (CGF-local); the commander is SILENT for its type's `CommandDisruptionSeconds` (6 s — the squad runner sends no orders, members keep theirs), then re-sends every order; `SquadLeaderDown` on the commander — `BasicSquadSop` reacts with `ReactToContact`, and a running `ReactToContact` holds and decides again |
| a commander is a soldier or a body-less entity | both exist: `ua-fire-distribution`'s Leader is a rifleman; `hill-attack-close`'s platoon has no body |

*Owners:* [Squad_Coordination_Design_v1_1](designs/group-maneuvers/Squad_Coordination_Design_v1_1.md) (what) ·
[DESIGN_Squad_Wiring](designs/group-maneuvers/DESIGN_Squad_Wiring.md) (where it runs) ·
[DESIGN_Squad_Tactics](designs/group-maneuvers/DESIGN_Squad_Tactics.md) (the runner, react to contact, the march).

## 10. Vehicles — passengers and formations *(measured 2026-10-11)*

| mechanism | state | detail |
|---|---|---|
| **eject everyone** (`EjectPassengersExecutor`) | 🟨 runs on CGF + Editor only; NOT replicated — SimHost and viewers never see the soldiers leave | triggered only by the example APC HSM or a blueprint `EjectPassengers` node |
| **board** (`EmbarkExecutor`) | 🟥 constructed only by its unit test; no host registers it | — |
| **one soldier gets out** | 🟥 only the editor's authoring path (`EditorCargoSystem`) | — |
| what "inside" means today | 🟥 only CanMove / CanShoot stripped: not carried, not hidden, still seen and shot | `IsEmbarkedTag { VehicleEntity }` exists on the passenger; nothing reads it |
| ⭐ the plan (user-approved) | I-2a one applier → I-2b `PassengerBuffer` replicated, passenger side derived on CGF and SimHost → I-2c inside = carried, hidden, unseen, dies with the vehicle → I-2d AI nodes + the squad dismount | [`DESIGN_Entity_Interactions.md`](DESIGN_Entity_Interactions.md) §7.1 · `CE-3110` |
| **formations** (`FormationController` / `FormationFollower` / `FormationTargetSystem`, CarKinem) | 🟥 for AI: works only in the standalone CarKinem example. `JoinFormation`'s params are empty, `VehicleAPI` is never built in production, nothing carries a join from CGF to SimHost; followers steer straight at a slot with no path finding | proper formations = their own programme, `CE-2124` |
| ⭐ squad formation **for now** | a `SquadMarch` play: members ordered to slot points behind the lead, falling back to single file on the lead's breadcrumbs | [`DESIGN_Squad_Tactics.md`](designs/group-maneuvers/DESIGN_Squad_Tactics.md) §6.1 |
