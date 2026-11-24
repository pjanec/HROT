# HROT — High-Realism Operations Technology

**A simulation engine for military training and analysis: forces that think, a world that breaks, and every run on the record.**

- **Computer-generated forces that fight like soldiers** — they react to fire, use cover, suppress, bound, break contact and break under
  stress, from the single rifleman up to a battalion — in open country and room by room inside a building.
- **A synthetic environment that changes under fire** — walls are breached, ground is cratered, buildings burn, minefields are cleared lane
  by lane, and every node of the exercise sees the same change at the same moment.
- **Your area of operations in minutes** — built from OpenStreetMap or imported from another simulator's terrain, with every house
  enterable.
- **Serious gaming built in** — trainees take the place of any soldier, crew or vehicle and fight alongside and against the AI, in the same
  world, on the same record.
- **Opposing forces that think for themselves** — a language model can take a commander's seat and plan, adapt and explain its tactics,
  while HROT's soldiers carry them out — a thinking enemy that never fights the same battle twice.
- **Every exercise recorded, reviewable and resumable** — review during the action, debrief after it, and go live again from any moment.
- **A brain that fits your world** — the AI runs on HROT's own perception, motion and navigation, or on yours: they are
  interchangeable reference implementations.
- **Proven, not promised** — every capability ships with scenarios the machine judges, run as regression tests.

> Status marks: ✅ built and proven by its scenarios · 🚧 in development · 🧪 experimental.
> The full, itemised list with the proving scenario for each line: [**docs/CAPABILITIES.md**](docs/CAPABILITIES.md).

---

## What HROT is

HROT is simulation **middleware**: the engine that runs the forces, the environment and the exercise, and serves them to the systems
around it — image generators, instructor stations, player stations, other simulators. It runs in a single process on one laptop for
authoring and demonstration, or distributed across many machines for a full exercise, with the same scenario and the same results.

HROT's own 2-D and 3-D maps are **engineering and instructor views**. The pictures your trainees see come from your **image generator**:
HROT publishes everything a renderer needs to show the battle convincingly (§ Integration).

---

## Why HROT stands out

| | |
|---|---|
| **AI that fights like soldiers** | Behaviour is driven by what each soldier perceives and how much stress he is under, not by scripts. A suppressed man goes to cover; a squad bounds from cover to cover; a defence that breaks rallies, reports and does not walk back into the ground it lost. |
| **A world that changes** | Breaches, craters, collapsing buildings, burning houses, felled trees and cleared minefield lanes change sight, fire and movement for every unit in the same tick, and survive save, replay and seek. |
| **Real places, on demand** | An area built from OpenStreetMap or imported from another simulator's terrain — heights, roads, woods, walls and every building enterable — ready for the AI to fight in. |
| **Trainees in the loop** | Any unit can be played: a trainee joins, picks a side and a unit, and takes it over in first person or over the shoulder — driving, gunning, leading — while the AI runs everyone else and takes the unit back when the trainee leaves. Human and AI units obey the same physics, the same damage and the same rules, and are reviewed the same way. |
| **Review without stopping** | Pause a live exercise, rewind to show the trainee what happened, then carry on live — or branch a new exercise from any recorded moment. |
| **Echelons from team to battalion** | Higher echelons can run as aggregated units that fight as numbers and **deaggregate into individual soldiers and vehicles on contact**, keeping every identity. |
| **Objective evaluation** | Exercises are scored by measurable objectives and standards, live and in the debrief, and every score traces back to the recorded moment that earned it. |
| **Deterministic and verifiable** | The same scenario gives the same result at 1, 4 or 8 threads, and every scenario states its expectations as machine-checked conditions. |
| **A thinking opposing force** 🧪 | A language model — local, on the exercise's own hardware — commands a platoon: it reads the situation as its radio reports it, decides, and HROT's soldiers execute with their own drills. The OPFOR adapts to what the trainees actually do and explains every decision for the debrief. |
| **A brain you can take with you** | The AI "brain" is separate from the world it acts in: perception, motion and navigation sit behind interfaces. HROT's own implementations are strong, but they are **reference implementations** — plug in your environment's sensors, vehicle dynamics or pathfinding and the same tactics run on top of them. |
| **Open to your systems** | A documented publish/subscribe wire, a written DIS mapping, and renderer-facing state including physics-based thermal drivers. |

---

## Capabilities

### The individual soldier
| | |
|---|---|
| ✅ **Senses** | sight, hearing (a shot is heard where the shooter cannot be seen), thermal by temperature contrast, radar; memory that ages; night, fog and weather shorten what he senses |
| ✅ **Threat and posture** | contacts ranked by danger and freshness; advance, take cover, suppress, fall back or hold, chosen without flicker; peek-and-fire from windows and corners |
| ✅ **Cover** | cover and firing positions found by environment queries; a suppressing soldier fights from cover his rounds can reach, never from the open |
| ✅ **Under fire** | runs crouched, drops at a near miss, stays down while pinned; stress and nerve per man — militia break, regulars hold, a cornered man surrenders |
| ✅ **Weapons** | weapon and round chosen per shot by effectiveness and range; anti-armour weapons kept for vehicles; aim time and spread per weapon |
| ✅ **Wounds** | damage by body part; wounded, incapacitated, bleeding out; casualty care and evacuation under covering fire |
| ✅ **Rules of engagement** | combatants engaged for who they are, civilians only for what they do; no shot through bystanders; surrender and hands-up honoured |

### Squad and platoon tactics
| | |
|---|---|
| ✅ **Drills** | react to contact, fire and movement by halves or pairs, bounding overwatch, danger-area crossing, fire distribution, break contact cover to cover |
| ✅ **After the fight** | rally, report (LACE, SPOT), remember the ground the enemy covers, never assault frontally a force it broke from without superiority |
| ✅ **Command** | the leader commands through orders; succession when a leader falls; reports and requests travel up the chain |
| ✅ **Platoon and company** | platoon march, bound, attack objective by objective (base of fire, flank, assault, consolidate), defence; a company assault under fire with mortars and smoke, a reserve that is committed when the attack stalls |
| ✅ **The HQ answers** | fire support and reinforcement requests answered with the right asset — air defence first suppressed, then air, helicopter or artillery |
| ✅ **Standing orders** | per-unit SOPs and a character (risk, obedience, nerve, resolve, skill) that shape how the same order is carried out |

### Close quarters and the fight in built-up areas
| | |
|---|---|
| ✅ **House clearing** | a squad clears a house room by room from its front door, the fire teams taking turns, the civilians inside spared |
| 🚧 **Room entry** | the team stacks beside the door out of the window's view, breaches it and enters one man at a time to his point of domination (being hardened: the entry team is still lost in some runs) |
| ✅ **Doors** | doors open, close, lock and are breached; a mover routes round a locked door and opens a closed one on his way; a locksmith unlocks one |
| ✅ **Fighting from a building** | window duels storey by storey; defenders take window and roof posts and open fire as the enemy enters their sector (🚧 being hardened) |
| ✅ **Windows** | a glass pane cracks and then breaks under fire; a man firing out through a closed window shatters it; blast and fragments break panes |
| ✅ **Grenades and blast indoors** | throws fly a real arc; fragments hit the body parts the stance exposes, so a prone man survives what wounds a standing one; a roof burst spares the floor below; a door inside the blast is breached |
| ✅ **Buildings to fight in** | every house enterable, with storeys, stairs, roofs, doors and windows — imported with the terrain or built from templates |

### Higher echelons — aggregated units 🧪
| | |
|---|---|
| **One entity per platoon, company or battalion** | its men and vehicles kept as slots (health, ammunition, stress); the same orders, reports and SITREPs as physical units, at a fraction of the cost |
| **Probabilistic movement, detection and combat** | killer–victim tables generated from the real weapon and armour models |
| **Deaggregation on contact** | a unit splits into real soldiers and vehicles when the fight comes close — keeping every identity — and merges back later |

### Vehicles and armour
| | |
|---|---|
| ✅ **Armour** | the face hit depends on where the round arrives (top attack meets the roof); armour per face per penetrator |
| ✅ **Damage and kills** | damage per part — tracks, engine, turret, gun — giving mobility and firepower kills; a kill is knocked out, burnt out or catastrophic |
| ✅ **Tactics** | fight from cover (hide, expose, fire, withdraw), hull-down attack over a crest, shoot and scoot, stand-off against anti-tank teams |
| ✅ **Guided weapons** | command, fire-and-forget and IR guidance, top attack, minimum range; smoke dischargers that screen sight and infrared |
| ✅ **Convoys and checkpoints** | a convoy that pushes through or dismounts under attack; a checkpoint that ladders a car by its time to stand-off (warning shot, disabling fire, lethal fire) |
| ✅ **Crews and lights** | hatches, ramps and doors that open as the squad dismounts; light discipline (normal, convoy, blackout) by SOP and darkness |
| ✅ **Breaching** | a tank breaks fences and walls; a bulldozer clears roadblocks; wrecks and craters re-route traffic |

### Air and air defence
| | |
|---|---|
| ✅ **Helicopters** | air assault (troops dismount only on landing), air rescue, gun and rocket runs, door-gun orbits |
| ✅ **Jets** | terrain-safe flight with automatic ground-collision recovery; dogfights with missiles and guns where pilot skill decides; combat air patrol |
| ✅ **Missiles that can be beaten** | energy-limited missiles, flares and chaff, beaming, a late break; missile and radar warning |
| ✅ **Air defence** | AA guns and a radar SAM site (search and tracking radar); the HQ suppresses air defence before sending aircraft |
| ✅ **Drones** | FPV drones with the operator's delay, which a hard-turning vehicle can defeat; counter-drone fire; vehicles that hear a drone and hide |
| ✅ **Airborne** | rope and parachute descent drifting on the wind; pilot ejection |

### Fires, obstacles and effects
| | |
|---|---|
| ✅ **Indirect fire** | fire solutions with low and high arcs, observer adjustment, time on target, sheaf control, danger-close rules |
| ✅ **Minefields** | anti-personnel and anti-tank fields as one object; each side knows only what it has learned; strike, halt and back out; engineers trace and mark; a clearer cuts a lane · 🚧 the full strike-to-crossing chain |
| ✅ **Incendiary weapons** | man-portable and vehicle flamethrowers, napalm, burning buildings — all within the rules of engagement and switched off in a police scenario |
| ✅ **Obscurants** | smoke and tear gas as clouds that block sight (and infrared where the munition does) |
| ✅ **Terrain shaping** | ditches, anti-tank ditches and foxholes that the AI uses as cover; craters whose rim becomes cover |

### Civilians and the asymmetric fight
| | |
|---|---|
| ✅ **Pattern of life** | civilians with routines who take shelter, cower or flee when fighting starts; fear that spreads through a crowd; background road traffic and pedestrians; livestock and guard dogs that react to the fight |
| ✅ **Crowds and riots** | crowds that form, protest and, when provoked, riot — stones, melee, a gas canister thrown back |
| ✅ **Police** | force levels and the escalation ladder (warn, non-lethal, lethal), police lines and baton charges, self-defence |
| ✅ **Hostile intent** | "is he armed?" judged per observer; a man who aimed and hid his weapon is remembered; celebratory fire, suicide bombers, command-wire IEDs, vehicle-borne threats |
| ✅ **Regional dress** | people dressed from a catalogue of garments per region and role, with seeded variety |

### Sensors and the environment
| | |
|---|---|
| ✅ **Real terrain** | from OpenStreetMap (search a place, draw the area, preview, import with elevation and a road network) or from another simulator's terrain; on the UTM grid |
| ✅ **Built-up areas** | buildings with storeys, doors that open, lock and are breached, glass windows that crack and break; terrain editing (remove an imported building, make one enterable) |
| ✅ **Weather and light** | weather regions, wind, clouds, fog and rain, a real sun and moon from date and position; they change what units sense |
| ✅ **Thermal data** | engines, barrels, bodies, fires and the warm patch a vehicle leaves — all simulation state a physics-based thermal renderer can use |
| ✅ **Sensor views** | night vision and thermal views on the instructor's map, from the same thermal model the AI sensors use |

---

## Running an exercise — the instructor's view

| | |
|---|---|
| ✅ **Run modes** | Preview (a dry run), Live (recorded) and Review, armed once for the whole cluster and shown on every station |
| ✅ **Control** | play, pause, step and time scale, with every node in step; give orders from the map; take direct control of any unit in first person |
| ✅ **Injects and rules** | triggers (when … then …), objectives and measures, injects fired by hand, the exercise result |
| ✅ **Review during the action** | pause a live exercise, rewind to show what happened, and resume live where it paused — one continuous record |
| ✅ **After-action review** | replay any recorded exercise, from any point of view (the instructor's free camera, any trainee's or unit's own view), see the event timeline (every shot, hit, order and report), mark moments, compare what each station saw |
| ✅ **Go live from replay** | branch a new live exercise from any recorded moment ("what if he had turned left?"); the original record stays untouched |
| ✅ **Game master** | change the AI's skill per side, fire triggers, disarm or re-arm a unit, or run any action by hand while the exercise runs |
| ✅ **Trainees and players** | briefed before a run, a summary at its end |

---

## Serious gaming — trainees in the loop

HROT is a serious-gaming engine as well as a CGF: the same simulation that runs the computer-generated forces lets people fight in it.

| | |
|---|---|
| ✅ **Play any unit** | take direct control of a soldier, a crew position or a vehicle, in first person or over the shoulder; the AI hands the unit over and takes it back when the trainee releases it |
| ✅ **Join, deploy, rejoin** | a trainee joins a running exercise, picks a side and a unit and deploys; leaving and rejoining is supported |
| ✅ **Client-server multiplayer** | one HROT server — the editor, running simulation, AI and deployment in one process — and several external presentation clients (a game engine such as Unreal) connected to it, each trainee with a seat of his own |
| 🚧 **Peer multiplayer** | HROT's own player stations as full nodes of a distributed exercise (the acceptance run is open) |
| ✅ **Crew the weapons** | man a vehicle's heavy machine gun or an anti-aircraft gun; the rounds are the simulation's own ballistics and damage |
| ✅ **Drive the vehicles** | a trainee's tank breaks through fences, hedges, garden walls and sandbags and fells lone trees — and the change is seen by everyone |
| 🚧 **Set up the game** | playable units, deploy roles and kits, spawn areas and respawn delay per scenario (authoring of kits still basic) |
| ✅ **Mixed teams** | a trainee who takes over a squad leader commands his AI squad from an order wheel; trainees fight AI opponents who see, hear and react to them exactly as to any other unit |
| ✅ **One record for all** | a trainee's actions are recorded with everyone else's — every shot, hit and order — for the review and the score, and replayed from any point of view — the instructor's free camera or the trainee's own view; any moment can be played again live |

---

## A thinking opposing force — LLM commanders 🧪

Scripted opponents get learned: by the third run, trainees know where the enemy will be. HROT lets a **language model take a commander's
seat**, so the opposing force reads the situation, makes a plan, changes it when the plan meets the trainees, and never fights the same battle
twice — while HROT's own soldiers carry out its orders with their drills, cover and fire discipline.

| | |
|---|---|
| 🧪 **A commander, not a puppeteer** | the model commands a platoon: it chooses the play (attack, defend, bound, support by fire) and the orders for its squads; HROT's squads and soldiers execute, react and report |
| 🧪 **Fog of war by construction** | the model knows only what its radio has reported — the enemy's size, activity, equipment, how old the report is and how sure — never the simulation's ground truth |
| 🧪 **Real tactical reasoning** | each decision states its assessment, the enemy's likely course of action, the main risk and the commander's intent, then the orders — with conditions, abort triggers and pre-authorised contingencies that HROT carries out on its own between decisions |
| 🧪 **Terrain the way a commander sees it** | named places, each with what can see it, its cover, whether vehicles can reach it and how long it takes to get there — the model chooses between options, it never does geometry |
| 🧪 **Explained for the debrief** | every prompt, every decision and every order applied is recorded beside the exercise: the after-action review can show *why* the enemy did what it did |
| 🧪 **Your model, your hardware** | the model runs locally (an Ollama-compatible server; a 7–8 B model on a single laptop GPU is the target) — no exercise data leaves the site |
| 🧪 **From platoon to company — over aggregated units** | the model can command a company whose platoons run as aggregated units — fought as numbers, cheap enough for a large enemy force — that split into squads and soldiers with full drills on contact; the commander keeps commanding them through the split |
| 🧪 **Side against side** | two commanders can fight each other — an LLM-commanded Blue against an LLM-commanded Red — for wargaming and course-of-action analysis |
| 🧪 **A tester that never runs out of ideas** | an LLM commander gives plausible orders nobody scripted — the fastest way we have found to expose where the forces' AI breaks |

> Status: a platoon commander against a defended house, a two-sided duel and a company over aggregated platoons are proven with a small,
> fast model; a local model on a laptop GPU is the next step. Design: [docs/designs/llm-commander/DESIGN_LLM_Commander.md](docs/designs/llm-commander/DESIGN_LLM_Commander.md).

---

## Mission planning

| | |
|---|---|
| ✅ **Missions per unit** | a mission is a list of tasks — each a behaviour with its parameters and the trigger that starts it — for any unit or echelon |
| ✅ **Orders through the chain** | a commander's orders flow down to his subordinates, who carry them out by their own drills and SOPs; reports flow back up |
| ✅ **Control measures in the plan** | objectives taken in turn, a base of fire and an assault, phase lines with deadlines, a reserve committed when the plan stalls — shown in a company attack that runs three ways: it works, it stalls, the defence collapses |
| ✅ **Rehearse the plan** | Preview runs the plan dry — nothing recorded — and *Run to* jumps to any time in it |
| ✅ **Compare courses of action** | branch the same exercise from one moment into alternatives and replay each |
| ✅ **Orders on the map** | move, engage and stop orders, designated targets and areas, a command mode for many units at once |
| 🚧 **A graphical plan editor** | phases and phase lines drawn on the map and edited in the plan (today the plan is a task list) |

---

## Objective evaluation and scoring

The exercise is judged by **measurable conditions**, not by an observer's impression — the same machinery that verifies the engine's own
scenarios.

| | |
|---|---|
| ✅ **Objectives and measures** | take, hold, keep a minimum of men, by a deadline — tracked live, and the **exercise result** computed from them |
| ✅ **Rules that react** | triggers (*when* … with hold, delay, cooldown and repeat, *then* … orders, messages, objectives, end of exercise) drive the exercise and its scoring |
| ✅ **The instructor's rules view** | every objective, measure and rule with its live state, the same data each trainee's display reads |
| 🚧 **Per-trainee standards** | each training standard GO / NO-GO for each trainee against his group's mean, what went well and what to work on, and his own moments with a jump into the recording |
| ✅ **Evidence for every judgement** | the event timeline indexes every shot, hit, casualty, order, report and decision with its time — each score traces back to the moment that earned it |
| ✅ **Repeatable** | deterministic runs mean the same plan scores the same, so a difference in result is a difference in decisions |

---

## Authoring

| | |
|---|---|
| ✅ **Scenario editor** | place units from a gallery, build the order of battle, draw routes, areas and zones, set weather and start time, save and run in one tool |
| ✅ **Missions and orders** | tasks with parameters and triggers, SOPs and rules of engagement per unit, compositions reused across scenarios |
| ✅ **Behaviour authoring** | behaviour trees, state machines and visual blueprint scripts, with breakpoints, stepping and live variable watch — and **changed in a running exercise without a restart** |
| ✅ **Data-driven** | every entity type — weapons, vehicles, ammunition, sensors, signatures, dress — is data in a type catalogue, not code |
| ✅ **See why** | for any unit: what it is doing, why, what it knows, its squad, its orders and reports |

---

## Integration

| | |
|---|---|
| ✅ **A separable brain** | decision (the brain) and execution (the muscle) are split: the brain issues intents and reads back status, and perception, motion and navigation are reached through interfaces — so the behaviours can run against another simulation's sensors, physics and pathfinding, with HROT's own as the reference |
| ✅ **Distributed by design** | simulation, AI, instructor, image generator and player stations as separate nodes over a publish/subscribe network (DDS), or all in one process; ownership hand-over and dead reckoning |
| ✅ **Image-generator ready** | everything a renderer needs is simulation state: poses, turret angles, damage per part and kill kind, hatches and lights, rotors and engines, effects with their causes, fire and thermal drivers — read alike by every renderer through one shared rule library |
| ✅ **External presentation clients** | an outside renderer links to a HROT node through a gateway and presents the simulation; the built-in director's prepared clips are rendered by it, pictures and sound |
| ✅ **DIS** | a written mapping of appearance, damage, fire, lights and detonation results for a DIS/HLA gateway |
| ✅ **A clean wire** | network messages carry only their own types, checked by an automated test — the simulation's internals can evolve without breaking the interface |
| ✅ **Automation** | every node answers an HTTP API — load, run, step, inspect any entity — so test harnesses and AI agents can drive the real product |

---

## Scale and determinism

| | |
|---|---|
| ✅ **Large areas, large forces** | town-sized built-up areas and battalion-size forces, with aggregated units for the higher echelons; the limits depend on the hardware and on the perception and navigation implementation in use |
| ✅ **Multithreaded** | the simulation runs in parallel per entity, with machine checks that keep it safe |
| ✅ **Deterministic** | the same scenario, the same result, at any thread count — the basis for repeatable training and analysis |

---

## Quality you can trust

| | |
|---|---|
| ✅ **Scenarios are tests** | each scenario states what must happen as machine-checked conditions and a recorded golden; a nightly run verifies them all |
| ✅ **Recordings as evidence** | a misbehaving run is a recording anyone can open, replay and inspect — no cluster needed |
| ✅ **See the wire** | a network capture shows exactly what crossed between nodes |

---

## Under the hood

.NET 8 on Windows and Linux · a cache-friendly entity-component core with no allocation on the frame path · CycloneDDS
publish/subscribe · deterministic parallel simulation.

## Learn more

- [**Capabilities**](docs/CAPABILITIES.md) — every capability, its status and the scenario that proves it
- [**Technical overview**](docs/TECHNICAL_OVERVIEW.md) — the engine's architecture for integrators and developers
