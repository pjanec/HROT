# HROT — High-Realism Operations Technology

**A simulation engine for military training and analysis: forces that think, a world that breaks, and every run on the record.**

- **Computer-generated forces that fight like soldiers** — they react to fire, use cover, suppress, bound, break contact and break under
  stress, from the single rifleman up to a battalion.
- **A synthetic environment that changes under fire** — walls are breached, ground is cratered, buildings burn, minefields are cleared lane
  by lane, and every node of the exercise sees the same change at the same moment.
- **Your area of operations in minutes** — built from OpenStreetMap or imported from another simulator's terrain, with every house
  enterable, at 5 km scale.
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
| **Real places, fast** | A 5.5 × 5.5 km area with ≈ 1 800 enterable houses imports in seconds and loads in about 25 s. |
| **Review without stopping** | Pause a live exercise, rewind to show the trainee what happened, then carry on live — or branch a new exercise from any recorded moment. |
| **Echelons from team to battalion** | Higher echelons can run as aggregated units that fight as numbers and **deaggregate into individual soldiers and vehicles on contact**, keeping every identity. |
| **Deterministic and verifiable** | The same scenario gives the same result at 1, 4 or 8 threads, and every scenario states its expectations as machine-checked conditions. |
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
| ✅ **Buildings** | room entry from a stack, house clearing room by room with civilians spared, window and roof defence |
| ✅ **Standing orders** | per-unit SOPs and a character (risk, obedience, nerve, resolve, skill) that shape how the same order is carried out |

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
| ✅ **After-action review** | replay any recorded exercise, see the event timeline (every shot, hit, order and report), mark moments, compare what each station saw |
| ✅ **Go live from replay** | branch a new live exercise from any recorded moment ("what if he had turned left?"); the original record stays untouched |
| ✅ **Trainees and players** | briefed before a run, a summary at its end, a per-trainee debrief against the training standards |

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
| ✅ **DIS** | a written mapping of appearance, damage, fire, lights and detonation results for a DIS/HLA gateway |
| ✅ **A clean wire** | network messages carry only their own types, checked by an automated test — the simulation's internals can evolve without breaking the interface |
| ✅ **Automation** | every node answers an HTTP API — load, run, step, inspect any entity — so test harnesses and AI agents can drive the real product |

---

## Scale and determinism

| | |
|---|---|
| ✅ **Large areas** | 5.5 × 5.5 km, 1 m elevation, ≈ 1 800 enterable houses, 1 000 soldiers with vehicles and aircraft, loaded in about 25 s |
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
- [**Usages**](docs/USAGES.md) — what teams do with the engine: training review, branching, test automation, real-place terrain
- [**Technical overview**](docs/TECHNICAL_OVERVIEW.md) — the engine's architecture for integrators and developers
