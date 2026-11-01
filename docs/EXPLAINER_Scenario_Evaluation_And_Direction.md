<!--STATUS
state: LIVE
updated: 2026-11-01 (parts 1–2, diagrams added — coordinator)
current-answer: the whole file — an EXPLAINER of the design, not the design itself
stale-below: nothing
known-rot: none — ⚠ describes the DESIGN (nothing here is built yet); the decisions and their ids live in DESIGN_Scenario_Rules.md
known-conflict: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — OWNS every decision explained here (SR-…, T1–T10); this file only explains them with examples
  - docs/DESIGN_Scenario_Entity_Keys.md — the stable entity GUIDs references rest on
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the behaviours, orders and command chain objectives and triggers use
-->

# EXPLAINER — **scenario evaluation and direction** *(objectives, triggers, …)*

> For a reader who wants to understand HOW it works, with examples. The decisions and their reasons are in
> [`DESIGN_Scenario_Rules.md`](DESIGN_Scenario_Rules.md); ⚠ none of this is built yet.

**The three roles, in one line each:**

| | does | example |
|---|---|---|
| **objective** | OBSERVES and REPORTS — *did it succeed, how well* | *"Hold the bridge for 10 minutes"* |
| **trigger** | REACTS — *when something happens, do something* | *"when the bridge is held, send the counter-attack"* |
| **mission plan** | DIRECTS one unit — *its ordered task list* | *"drive to the bridge, then hold it"* |

```mermaid
graph LR
    W["the simulated world<br/>units · areas · clock"]
    O["OBJECTIVES<br/>observe + report"]
    T["TRIGGERS<br/>watch + react"]
    M["MISSION PLANS / orders<br/>direct one unit"]
    W -- "read" --> O
    O -- "status" --> T
    T -- "orders · insert macros · messages" --> M
    M -- "the units act" --> W
    O -- "status" --> R["evaluation result<br/>IOS · HUD · NED · debrief"]
```

*Caption:* the loop — objectives READ the world and publish statuses; triggers READ statuses and ACT through orders and macros; units carry
the orders out and change the world, which the objectives read again.

---

## 1. The objective

### 1.1 What it is

An ordinary **entity** (usually created by a scenario macro) with four parts:

| part | in the example *"Hold the bridge"* |
|---|---|
| label + position | `EntityInfo` (title *"Hold the bridge"*, side Blue) · `SimTransform` at the bridge — only for drawing it on the map |
| its LINKS (`ObjectiveLinks`) | `Subject` = **Blue Platoon** (*whose* objective it is) · `Parent` = **Blue mission** (the objective it rolls up into) |
| its TASK slot = the evaluating behaviour | `HoldArea` with params `{area: "@Bridge", minUnits: 4, maxEnemy: 0, holdSeconds: 600, common: {deadline: "10:30", points: 100, weight: 40}}` |
| its RESULT (`ObjectiveStatus`, replicated to every node) | state · progress · value / target · score · points · when · reason |


```mermaid
classDiagram
    direction LR
    class ObjectiveEntity { <<an ordinary entity>> }
    class EntityInfo { title "Hold the bridge" · side Blue }
    class SimTransform { position at the bridge · drawing only }
    class ObjectiveLinks { Subject = Blue Platoon · Parent = Blue mission }
    class TaskSlot { behaviour HoldArea · params area minUnits maxEnemy holdSeconds deadline weight }
    class ObjectiveStatus { <<replicated>> State · Progress · Value Target · Score · Points · ChangedAt · Reason }
    ObjectiveEntity *-- EntityInfo
    ObjectiveEntity *-- SimTransform
    ObjectiveEntity *-- ObjectiveLinks
    ObjectiveEntity *-- TaskSlot
    ObjectiveEntity *-- ObjectiveStatus
```

It runs on the **Brain node** (which owns it), in the normal brain tick — only while simulation time advances.

### 1.2 Where its data comes from — it only READS the world, through references

| it needs | from |
|---|---|
| the area polygon | `@Bridge` — the `RefKey` of an area entity → its `EditablePolyline` |
| *our* units | `Subject` = Blue Platoon → its members down the command chain (`UnitSubordinate`) |
| who is inside | each member's `SimTransform` tested against the polygon (the existing area test) |
| enemies inside | entities in the polygon whose side (`ForceId`) is Hostile |
| time | `ScenarioClock` — the deadline and the hold timer |

### 1.3 What its behaviour does each tick (`HoldArea`, a small state machine)

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Active : activeWhen holds (or none)
    Active --> Achieved : held long enough
    Active --> Failed : the deadline passed
```


```mermaid
sequenceDiagram
    participant BT as brain tick (Brain node)
    participant HA as HoldArea (on the objective)
    participant W as world
    participant S as ObjectiveStatus (replicated)
    BT->>HA: tick (sim time advanced by dt)
    HA->>W: Subject Blue Platoon - members via the command chain
    HA->>W: @Bridge - polygon · members and hostiles inside · clock
    HA->>HA: ours 5 ≥ 4, enemy 0 ≤ 0 ⇒ held += dt (312 s)
    HA->>S: Active · "5 / 4 units, held 312 of 600 s"
    Note over HA,S: later — held reaches 600 s ⇒ Achieved, Reason "held 600 s, 5 Blue / 0 Red"
```

```
Active, every tick:
    ours   = members of Subject inside the area, alive
    enemy  = hostile units inside the area
    if ours >= 4 and enemy <= 0:  held += dt   else: held = 0        (held lives in its working state)
    status.Progress = "5 / 4 units, held 312 of 600 s"
    held >= 600          -> Achieved   (Reason "held 600 s, 5 Blue / 0 Red")
    clock > deadline     -> Failed     (Reason "deadline 10:30 passed, held only 140 s")
```

⭐ It **never changes the world** — it only writes its own status; every state change is journaled (so the debrief can say why).

### 1.4 How others use it — nobody calls it, everyone READS its status

```mermaid
graph LR
    BP["Blue Platoon<br/>(members, positions)"]
    HB["Hold the bridge<br/>HoldArea"]
    BM["Blue mission<br/>Composite WeightedMean"]
    EX["Exercise result"]
    BR["@Bridge area"]
    HB -- "Subject" --> BP
    HB -- "reads polygon" --> BR
    HB -- "Parent" --> BM
    BM -- "Parent" --> EX
    BM -. "reads children's status" .-> HB
```

*Caption:* the solid arrows are LINKS the objective holds; the dotted arrow is the parent READING the child's status. A trigger, the
instructor station, the trainee's HUD and any NED frontend read the same replicated status.

- **The parent** *"Blue mission"* (a `Composite`, behaviour `WeightedMean`) finds its children by asking *"which objectives have `Parent` = me?"*,
  reads their statuses (*Hold the bridge* Achieved = 1.0 × weight 40, …) and writes its own score.
- ⭐ **In one sentence:** an objective is a small brain that OBSERVES (its subject, its areas, the clock) and REPORTS (its own status).
  Steering belongs to triggers and mission plans.

---

## 2. The trigger

### 2.1 What it is

Also an ordinary **entity** — a SUB-entity of what it concerns (`Subject`; shown nested under it) — with its own brain running the `Trigger`
behaviour in ITS task slot (⛔ never on the unit it steers: that unit's task slot belongs to its mission plan or orders).

| part | in the example *"Counter-attack"* |
|---|---|
| label | *"Counter-attack when the bridge is held"* |
| `Subject` | **Red HQ** (whose reaction it is) — or the World Environment entity for a scenario-wide trigger |
| its TASK slot = the `Trigger` behaviour with params | `when`: the status of `@HoldTheBridge` becomes **Active** · `delay`: 120 s · `once` · `do`: ① `InsertMacro(forces/RedQRF at @VillageEdge)` ② `Order(@RedQRF, Attack, area @Bridge)` ③ `Message(IOS, "Red counter-attack launched")` |
| its RESULT (`ObjectiveStatus`, not scored) | state · `FireCount` · `LastFired` · reason |

### 2.2 Where its data comes from

| it needs | from |
|---|---|
| its CONDITION | the replicated `ObjectiveStatus` of the objective it watches (`@HoldTheBridge`) — ⭐ conditions ARE objective statuses; a condition you do not want scored is an objective with `scored: false` (e.g. *"Red reaches the ridge"* = a `ReachArea` objective, not scored) |
| time | `ScenarioClock` (the delay, the cooldown) |
| the targets of its actions | `RefKey`s (`@RedQRF`, `@VillageEdge`, `@Bridge`) resolved in the nearest scope |

### 2.3 What its behaviour does (the `Trigger` state machine — rules T1–T10)

```mermaid
stateDiagram-v2
    [*] --> Disarmed
    Disarmed --> Armed : activeWhen holds (or none)
    Armed --> Holding : the condition rises (false to true)
    Holding --> Armed : drops before holdFor
    Holding --> Waiting : held for holdFor
    Waiting --> Acting : delay elapsed
    Acting --> Acting : current action Success, next one starts
    Acting --> Fired : last action Success
    Acting --> Fired : an action Failure (stops, Reason written)
    Fired --> Cooldown : repeat
    Cooldown --> Armed : cooldown over and condition false again
    Fired --> Done : once
    Armed --> Disarmed : activeWhen stops or deadline passes
```

The example, on a timeline:

```mermaid
sequenceDiagram
    participant HB as Hold the bridge (objective)
    participant CA as Counter-attack (trigger)
    participant EC as entity creation
    participant RC as Red's command chain
    participant IOS as instructor station
    Note over HB: 09:41:10 — Blue's 4th man reaches the bridge
    HB->>HB: Pending to Active
    CA->>HB: reads the status, sees the edge
    CA->>CA: Armed to Waiting (delay 120 s)
    Note over CA: 09:43:10 — the delay elapsed
    CA->>EC: InsertMacro RedQRF at @VillageEdge (12 creation requests)
    Note over CA: InsertMacro is Running — the next action waits
    EC-->>CA: EntityConstructionCompleted x12 (every node acked)
    Note over CA: InsertMacro Success
    CA->>RC: Order @RedQRF Attack @Bridge (AssignTacticalIntent)
    RC-->>CA: intent accepted — Order Success
    CA->>IOS: Message "Red counter-attack launched"
    CA->>CA: Fired to Done · Reason written · journaled
```

| rule | meaning |
|---|---|
| ⭐ the EDGE | it fires when the condition BECOMES true — not every tick while it is true |
| `holdFor` vs `delay` | `holdFor` = the condition must STAY true that long (debounce); `delay` = fire that long AFTER the edge, even if it drops |
| `once` / `repeat` | `repeat` re-arms only after the condition went false again, plus `cooldown` |
| time | simulation time only — paused ⇒ nothing fires |
| several in one tick | each is independent; up to one tick of latency; where order matters, CHAIN — a trigger's own `Fired` is a condition another can watch |
| actions | run in their listed order, each WAITED for — the next starts only when the previous succeeded; a failure stops the list and is written into `Reason` |
| the instructor | can fire, disarm or re-arm it by hand (journaled with who did it) |

### 2.5 What `do` is — a plain list of the SAME actions every behaviour uses (SR-93)

```mermaid
graph LR
    P["trigger params (JSON)<br/>do: InsertMacro · Order · Message"]
    TB["Trigger behaviour<br/>runs the list as a sequence"]
    BT["any BTree / HSM<br/>e.g. a squad leader"]
    DR["a director"]
    V["ONE action vocabulary<br/>[SharedAiAction]s with an explicit target<br/>InsertMacro · Order · MissionCommand · Message …"]
    E["existing machinery<br/>entity creation · AssignTacticalIntent · messages"]
    P --> TB
    TB --> V
    BT --> V
    DR --> V
    V --> E
```

*Caption:* the verbs are not trigger-only handlers — they are ordinary shared actions that say WHO they act on, so a trigger, a squad
leader's tree or a director all call the same `InsertMacro`. Each one reports `Running` until it is really done (InsertMacro: until every
entity it asked for is constructed and acked on all nodes), so the list can wait for it. The `do` list itself is still just data — nothing is
compiled per scenario, and an unknown action or bad argument is refused when the scenario loads. `when` can be an objective's status OR the
scenario clock directly (`scenarioTime >= 09:30`).

```mermaid
sequenceDiagram
    participant C as caller (trigger, tree or director)
    participant IM as InsertMacro (shared action)
    participant EC as entity creation
    participant LM as lifecycle module (every node)
    C->>IM: tick (first time)
    IM->>EC: one creation request per macro entity, ids kept in its working state
    IM-->>C: Running
    LM-->>IM: EntityConstructionCompleted (RequestId = ours)
    C->>IM: tick
    IM-->>C: Running (some still pending)
    LM-->>IM: the last one completed
    C->>IM: tick
    IM-->>C: Success (or Failure, with the refusing module's reason)
```

*Caption:* what "waits for it to complete" means in practice — the action, not the caller, knows when it is done, so every caller gets the
same behaviour for free.

⛔ **Not yet buildable** — three things to measure first: binding an action from data at runtime (today bindings are generated code), where
InsertMacro's variable arguments live (an action's params must be a fixed-size struct), and moving the action catalogue from the editor into
shared runtime code so a cluster node can validate the list. See [SR-93](DESIGN_Scenario_Rules.md).

### 2.4 How it fits with the others

```mermaid
graph LR
    HB["Hold the bridge<br/>(objective)"]
    CA["Counter-attack<br/>(trigger, Subject Red HQ)"]
    QRF["RedQRF<br/>(inserted macro)"]
    MP["RedQRF HQ<br/>mission plan / orders"]
    CA -. "reads status" .-> HB
    CA -- "InsertMacro" --> QRF
    CA -- "Order Attack" --> MP
```

*Caption:* the trigger READS objectives and ACTS through the existing mechanisms — macro insertion (ordinary entity creation) and orders down
the command chain (which a unit's mission plan or brain then executes). ⭐ **In one sentence:** a trigger is a small brain that WATCHES statuses and,
on an edge, RUNS a list of actions.
