<!--STATUS
state: LIVE
updated: 2026-10-10 (§6a the LIBRARY line + promotion rule — R-256 L1; R-258 BTrees; R-259 one retreat)
current-answer: the whole file — an EXPLAINER (map of what exists), not a design; every box names its owning design
stale-below: none
known-rot: none known; built/partial/missing colours are a 2026-10-06 snapshot — re-check the tracker before relying on a red or amber box
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
  UT --> INP["21 standard inputs"]
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
    E1["HSM events<br/>Sensor.Acquired · Lost · Hit · NearMiss · AllClear …"]
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

## 6. Ready-made behaviours to reuse

```mermaid
graph TD
  subgraph TAC["EQS tactics (BTree)"]
    a1["TakeCover"] --- a2["FallBack"] --- a3["Flank"] --- a4["FiringPosition"]
  end
  subgraph POS["Posture & movement"]
    b1["CombatPosture<br/>BTree · HSM · Blueprint"]
    b2["DangerCrossing<br/>BTree · Blueprint"]
    b3["Sentry"]
  end
  subgraph SOPB["SOP"]
    c1["BasicInfantrySop<br/>Hit→TakeCover · HoldFire contact→FallBack<br/>contact→TakeCover · else Idle"]
  end
  subgraph BASIC["Basic orders"]
    d1["MoveToLocation · FollowRoute · JoinFormation<br/>Wander · FireAtTarget · Idle"]
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
    UT["Utility decisions: CombatPosture · AttackApproach · ThreatRanking · WeaponSelection"]
  end
  subgraph L2["② LIBRARY BEHAVIOURS — BTrees (R-258), assets an author assigns"]
    CP["CombatPosture"]
    TK["FallBack (THE retreat, R-259) · Flank · FiringPosition · TakeCover · Sentry · DangerCrossing"]
    SOP["BasicInfantrySop"]
    ORD["MoveToLocation · FollowRoute · JoinFormation · FireAtTarget · FireAtPoint · HoldStance"]
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
