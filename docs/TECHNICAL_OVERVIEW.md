<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file
stale-below: nothing
known-rot: none — ⚠ kept deliberately GENERIC (user, 2026-11-21: "needs to stay on a more generic level"): no type, class or project names, no
  counts that change. A fact that needs a name belongs in the owning design under docs/, not here.
related-designs:
  - ../README.md — the product page (what it does, for a buyer); this file is how it is built, for an integrator
  - CAPABILITIES.md — every capability, its status and its proving scenario; links into §2, §3, §4, §6, §8, §10 here
  - OVERVIEW_Behaviour_Building_Blocks.md — the behaviour machinery in depth (decisions, sensors, spatial queries, groups, vehicles)
  - reference/NED_DDS_Protocol.md — the wire protocol and the DIS mapping
  - RUNBOOK_Cluster_Debugging_Over_Http.md — running and inspecting a live cluster
-->

# HROT — technical overview

How HROT is built, for integrators and engineers evaluating it. What it can do is on the [product page](../README.md) and, item by item, in
[CAPABILITIES.md](CAPABILITIES.md). This page stays at the level of concepts; the designs under `docs/` carry the detail.

---

## 1. The shape

HROT has two layers:

| layer | what it is |
|---|---|
| **The engine** | a general-purpose, data-oriented simulation core: entities and components, scheduling, events, recording, distribution, time. It knows nothing of soldiers or vehicles |
| **The application** | the military simulation on top: soldiers, vehicles, weapons, effects, terrain, the AI, the tools |

The work of an exercise is split into **nodes**, each with one or more **roles**:

| role | does |
|---|---|
| **Brain** | decides: behaviours, orders, missions, memory of the enemy |
| **Body (ground physics)** | carries out: movement, combat, ballistics, damage |
| **Perception** | answers what each entity can see and hear |
| **Navigation** | answers routes and places to move to |
| **Traffic** | civilian road traffic |
| **Map** | the 2-D engineering and instructor map |

Alongside them run the **orchestrator** (the exercise's lifecycle), the **editor** (author, run and step a scenario in one process), the
**player** and the **replay browser**. A 3-D view and any external image generator are consumers of the same replicated state.

⭐ **Any combination of roles runs in one process** (development, a laptop exercise) **or spread across machines** (a full exercise); the
nodes find each other on the network with no central configuration.

---

## 2. The simulation core

| ⭐ property | what it gives |
|---|---|
| **Entity-component data** | an entity is a handle; its state is a set of small components stored by type in contiguous memory — fast to iterate, cheap to copy, trivial to record |
| **Plain-data state** | state that is recorded is fixed-size data with no references, so recording, snapshots and replay copy bytes rather than objects |
| **No allocation on the frame path** | the steady frame allocates nothing, so the garbage collector never interrupts the simulation; checked by tests |
| **Deterministic parallel simulation** | per-entity work runs in parallel: each entity writes only itself, reaches others by commands applied at the next step, and reads the state as it stood at the step's start. The result is identical at 1, 4 or 8 threads — and machine checks catch any code that breaks the rule |
| **Events** | a buffered event bus: what is published in one step is seen by every consumer in the next, in a fixed order |
| **Modules** | features plug in as modules that register their systems and the phase each runs in; a node is a composition of modules |
| **Type knowledge base** | every entity type — a rifleman, a tank, a munition — is data: its parts, weapons, sensors, signatures and behaviours, authored once and shared by every node |
| **Culture-neutral** | every number in every log, file and API is written the same on every machine, whatever its language settings |

---

## 3. Distributed nodes

- **The brain and the body are separate.** The brain publishes *intent* (move here, engage that); the body publishes *status* (where it is,
  what it hit). Either can run on its own node, and neither needs the other's internals.
- **Authority is per component, not per entity.** One node owns an entity's movement, another its decisions, another its perception
  results; each writes only what it owns and everyone else sees a replica. Ownership can be handed over while the exercise runs.
- **Late joiners catch up.** A node that joins mid-exercise receives the current state of everything — a burning wreck, a breached wall, a
  cleared lane — and is in step from its first frame.
- **Every node sees the same world change at the same step**: a breach, a crater or a fire changes sight, fire and movement everywhere at
  once.

---

## 4. The AI

| ⭐ | |
|---|---|
| **Three ways to author behaviour** | behaviour trees, hierarchical state machines and visual scripts — compiled to fast native code, combinable, and swapped into a running exercise without a restart |
| **Who decides what** | a unit's task, its standing operating procedures, its reactions and the rules of engagement are separate layers; the reaction to fire never needs the task to foresee it |
| **Utility decisions** | choices (which cover, which target, when to bound) are scored from inputs the author can see and tune |
| **Sensors and memory** | units act on what they have perceived and remember, never on ground truth |
| **Spatial queries** | "find me a good spot" — cover, a firing position, a flank — asked by any unit, a squad leader included |
| **Groups** | commanders, orders down the chain, reports up it, the squad's shared picture of the enemy, manoeuvres, succession when a leader falls |
| **The person underneath** | stress, the break, wounds, idle life and moving under fire are built into every soldier, so a behaviour does not re-implement them |
| **Aggregated units** 🧪 | a platoon, company or battalion as one entity that keeps its men as slots and deaggregates on contact |
| **Seeing why** | a unit's current decision, its inputs and the history of its choices can be inspected live and in replay |

---

## 5. The separable brain — perception, motion and navigation are replaceable

The brain reaches the world only through **three seams**: perception (what can I see and hear?), motion (move me there, this way) and
navigation (how do I get there, where may I stand?). HROT ships strong **reference implementations** of all three — line-of-sight and
signature-based perception, physics-based motion for men and vehicles, navigation meshes and road networks — but they are references, not
requirements.

⇒ The same behaviours run against **another engine's perception, physics or pathfinding**: an existing simulator's terrain service, a
physics engine already in use, or a sensor model a customer must keep.

---

## 6. The network and the wire

- **Publish/subscribe over DDS** (CycloneDDS): peer-to-peer discovery, no broker, quality of service per topic.
- **The wire is a separate data model.** The messages have their own types — even their own enumerations — and are converted at the edge,
  so the simulation can evolve without breaking a partner's integration. A machine check keeps internal types off the wire.
- **What is sent is chosen per topic**: periodic, on change, or reliable once — so a burst of fire costs one message, not one per round.
- **Documented** for partners: the protocol reference and a written mapping to DIS for a DIS/HLA gateway
  ([NED_DDS_Protocol.md](reference/NED_DDS_Protocol.md)).

---

## 7. Terrain and the environment

- **Real places**: built from OpenStreetMap (heights, roads, woods, buildings) or imported from another simulator's terrain, with every
  building enterable — storeys, stairs, doors and windows. Procedural terrain for training areas that do not exist.
- **Editable**: remove an imported building, make one enterable, add obstacles.
- **A world that changes**: walls breach, ground craters, buildings burn and collapse, trees fall, smoke drifts, minefields are cleared lane
  by lane — and every change is replicated, saved and replayed.
- **Environment**: time of day, light, weather; thermal signatures from physical drivers (engine running, barrel firing, ground left warm),
  computed on every node rather than sent.

---

## 8. Recording and replay

- **Everything is recorded**: periodic keyframes plus the changes in between, and every event.
- **Seek, step and scrub** to any moment; inspect any entity's state and any unit's decisions there.
- **Review during the action**, without stopping the exercise; **debrief after it**.
- **Go live from any moment**: branch the recording and continue the exercise from there, as a new run.
- **Schema-checked**: a recording carries the layout of what it holds, so one made by a different build is recognised rather than misread.

---

## 9. Exercise control

The orchestrator takes the whole cluster through its lifecycle — load, start, pause, stop, save — as **one coordinated transaction**
(prepare on every node, then commit), so no node is ever left on a different scenario or a half-applied change. It tracks which nodes are
alive and what each one serves.

---

## 10. Time

- **One clock for the exercise**: every node agrees on simulation time; clocks are synchronised over the network.
- **Modes**: real time, faster or slower than real time, paused, and single-step.
- **The simulation is paced by simulation time only**, never by the wall clock — which is what makes runs repeatable and replay exact.

---

## 11. Tools

| tool | for |
|---|---|
| **Scenario editor** | place forces, terrain edits and objectives; author missions and the order of battle; run, pause, step and save in one process |
| **Behaviour editors** | visual editors for behaviour trees, state machines and visual scripts, with live debugging and breakpoints |
| **2-D and 3-D maps** | engineering and instructor views of the live or replayed exercise (production imagery comes from an external image generator) |
| **Inspectors** | entities, components, events, decisions, profiling |
| **Replay browser** | load, seek, review and branch recordings |
| **HTTP API** | everything the editor can do, scriptable: author, run, query and step — used for automation and test harnesses |

---

## 12. Scenarios and verification

- **Every capability ships with feature scenarios** — one per situation it must handle. Each states its expectation twice: in words for
  people, and as **objectives the machine checks**. They run as a regression suite; a feature is done only when its scenarios pass.
- **Determinism is tested**: the same scenario stepped at different thread counts must produce the same world, bit for bit.
- **Parallel-safety checks**: undeclared cross-entity reads and writes off their owning thread fail the tests that exercise them.

---

## 13. Technology

.NET 8 on Windows and Linux · C# throughout, with source generators for the hot paths · CycloneDDS · Dear ImGui tools · navigation meshes
and a physics engine as reference implementations behind the seams of §5.
