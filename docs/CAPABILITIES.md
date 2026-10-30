<!--STATUS
state: LIVE
updated: 2026-10-30 (§1a running, reviewing and filming — run modes, DAR / AAR, the branch, the timeline index, the film director, scenarios as tests; §1's recorder and filming bullets point there) · 2026-10-21 (first version — coordinator; built from OVERVIEW_Rules_And_Demos.md, the scenario descriptions and a code sweep)
current-answer: the whole file — WHAT THE ENGINE CAN DO, by chapter, each with the ONE scenario that shows it best
stale-below: none
known-rot: the ✅/⚠ marks are a 2026-10-21 snapshot of the live checks; the owning design's demo table wins
related-designs:
  - OVERVIEW_Rules_And_Demos.md — OWNS the rule-level index (every rule id → code → design §); this file is the reader's summary above it
  - OVERVIEW_Behaviour_Building_Blocks.md — OWNS the AI machinery (decision slots, sensors, EQS, library, groups, vehicles)
  - PLAN_Feature_Films.md — OWNS the clip list that films each chapter
  - DESIGN_Film_Production.md — OWNS the film process behind §1a's director bullets
  - DESIGN_Replay_Time_Axis_And_Review.md — OWNS the reviews (DAR, AAR) and the sim time axis behind §1a
  - blueprints/Architect_Question_108_Run_Mode_Picker.md — OWNS the run modes and the armed mode (§1a)
  - USAGES.md — OWNS what PEOPLE use the engine for and what it saves them (demo films, QA bug illustration, reviews, scenarios as tests …)
  - ../README.md — OWNS the engine architecture (ECS, cluster, replication, replay, tools)
-->

# What HROT can do

A short tour of the simulation's capabilities, one chapter per area. Each bullet is one capability; **▶** names the scenario that shows
it best (`scenarios/<name>/scenario.json`). Every scenario carries a `description` saying what happens and how to judge it, and most have
a live check: `python3 scripts/utility-demo-check.py --launch <name>`.

- **Marks:** ✅ the live check passes · ⚠ passes with a caveat · ⏸ parked, varies run to run · ⏳ not built.
- **Deeper:** the rules behind each bullet, with code and design links, are in [OVERVIEW_Rules_And_Demos.md](OVERVIEW_Rules_And_Demos.md).
  The machinery is in [OVERVIEW_Behaviour_Building_Blocks.md](OVERVIEW_Behaviour_Building_Blocks.md). The engine architecture is in the
  [README](../README.md).
- **Films:** [PLAN_Feature_Films.md](PLAN_Feature_Films.md) lists the clips that show each chapter.

## 1. The engine — in one screen *(depth: [README](../README.md))*

- **ECS core (FDP):** zero-allocation entities and components, a double-buffered event bus, phases, a module host. (README §2)
- **A distributed cluster:** separate processes for the simulation host, the AI (CGF), the image generator (IG), the instructor station,
  traffic, the editor and replay. They can run as one process (`--mode all`) or across machines. (README §3, §9)
- **Brain and muscle are split:** the AI decides (intent), the simulation acts and reports back (status).
- **Replication** over CycloneDDS with per-component translators, ownership transfer and dead reckoning, so every node sees the same
  world. (README §6)
- **Time control:** play, pause, step one frame, change the timescale. All nodes stay in step. (README §10)
- **AI authoring three ways:** behaviour trees, state machines (HSM) and blueprint visual scripts, with utility decisions and an
  environment query system (EQS). (README §4, §5, §7)
- **Flight recorder and Replay Browser:** record a run, then load, seek and step through it. (README §8) — what it is used for: §1a
- **Tools:** the scenario editor (author, run, step and save in one process); missions and the order of battle (ORBAT) in panels; HTTP
  diagnostics and an MCP server so an AI agent can drive the engine; **filming** — the film director, §1a
  ([RUNBOOK_Filming.md](RUNBOOK_Filming.md)).

## 1a. Running, reviewing and filming *(added `2026-10-30`; what people do with these: [USAGES.md](USAGES.md))*

- **Run modes, one armed mode for the whole cluster:** Preview (a dry run, nothing kept), Live (recorded), and the reviews AAR and DAR. The
  armed mode shows on every host the same. ([AQ108](blueprints/Architect_Question_108_Run_Mode_Picker.md)) ✅
- **Every Live run is recorded** (a setting, on by default), and the recording keeps its own copy of the scenario, so it opens years later
  as it ran. (CE-4090, CE-4091) ✅ — ⚠ the combat events are missing from a Live recording today (CE-4118)
- **Review during the run (DAR):** pause a live exercise, go back to show what happened, and resume live where it paused — one continuous
  recording. ([Replay Time Axis](DESIGN_Replay_Time_Axis_And_Review.md); CE-4104) ✅
- **Review after the run (AAR) and branch from any moment:** a new exercise starts live from the chosen moment; the original recording
  stays untouched. (CE-4104) ✅
- **Edits are never lost by accident:** one guarded way in and out of a scenario asks before a step that would lose edits, checking every
  node of the cluster. (CE-4093, CE-4101) ✅ · A start screen: nothing runs before a scenario is opened. (CE-4096) ✅
- **Every node sees the same cluster facts** — state, armed mode, readiness — and controls wait until the cluster is ready.
  ([DESIGN_Cluster_View](DESIGN_Cluster_View.md), CE-1436) ✅
- **The timeline index of a run:** every shot, hit, blast, spawn, death, behaviour change, order, report and decision, plus every unit's
  position, each with its sim time — a zoomable event timeline, seek to the next event, live or recorded.
  ([Director Timeline §9](DESIGN_Director_Timeline_Editor.md)) ✅
- **The film director**, the same in the editor and the Replay Browser: takes and reels; camera keys tied to events; speed ramps, skips and
  instant replays; subtitles, on-screen graphics and the map view per moment; a drafter that proposes a shot sequence for an event; stills
  and contact sheets; MP4 with sound rendered from a recording; promote a take back to its scenario.
  ([Film Production](DESIGN_Film_Production.md), [PLAN_Feature_Films §1c](PLAN_Feature_Films.md)) ⚠ first clip from a Live recording
  `2026-10-30`; gaps CE-4117, CE-4119
- **Scenarios are tests:** each scenario carries machine-checkable conditions; a verify run checks them and records the run; a night run
  covers all of them. ([DESIGN_Scenario_Regression](DESIGN_Scenario_Regression.md)) ✅ · Their documentation as stills per check ⏳ (the take
  builder is built, the rendering is not wired)
- **Players:** a player in an image generator takes over a unit; the server and two players play one scenario. ⚠ the acceptance run is open
  (CE-4080)

## 2. The world

- **Terrain with real height:** a height grid, bridges and road heights, a desert with ridges and wadis. ▶ `ua-hill-attack`
- **Navmesh** (Recast) baked per tile and fine enough for doorways, cached on disk. ▶ `tt-nav-los`
- **Buildings with interiors:** storeys, roofs, windows, and doors that open, close, lock, unlock and can be breached. Several templates
  (two- and three-storey houses, an office, a market hall). ▶ `bc-house-clear`
- **demo-city:** a generated city with a SUMO road network; residential, business and commercial quarters; a market square, a 400 m
  battle street and a landing square. ▶ `uc-urban-battle`
- **Weather and time of day:** a scenario start time; global weather plus weather regions that blend into each other; wind, four cloud
  layers, fog, rain; a real sun position from the map's geographic origin. The 3-D map draws the sky, the sun, the clouds and the fog.
  ▶ `ua-weather-regions` (no live check)
- **Weather changes what units sense:** night and fog shorten sight, thermal ignores night but not fog, wind and rain cover sound,
  parachutes drift on the wind. ⚠ No demo shows this yet.
- **Background traffic (SUMO):** a traffic node drives hundreds of cars and pedestrians, mirrored to every node. A shot, a blast or a hit
  turns the pedestrians and cars nearby into real frightened civilians. ▶ `demo-city` (a recipe scenario)
- **Movement:** human gait (turns on the spot, never steps off a ledge); wheeled and tracked vehicles; helicopters and planes on routes,
  taking off and landing; rope and parachute descent; formations; shared and personal routes. Vehicles never drive into buildings and
  step over a man who is down.
- **Visual effects:** muzzle flash, tracers, bursts, impact marks, wrecks, bodies, fire and smoke, gas clouds. **Sound** for the
  operator, carried over the same ranges the AI hears.
- **3-D map:** block figures with poses and gestures, vehicle and aircraft models, clothing by side, picking in 3-D.

## 3. Civilians and crowds

- **One civilian brain** with Routine, Alarmed, Panic, Cower and Casualty moods. Fear comes from heard shots and near misses, and it
  fades. ▶ `ua-civ-routine-alarm` ✅
- **Shelter:** a panicking civilian runs to a house, opens its door, goes in and hides. A locked house is skipped. ▶ `ua-civ-shelter` ✅
- **Cower** when there is no building to run to, then calm down. ▶ `ua-civ-cower` ✅
- **Crowds form by themselves** from people standing close. Onlookers gather on a ring round a casualty. ▶ `ua-civ-gathering` ⚠ live re-run pending
- **Contagion:** fear spreads through a crowd to people who heard nothing. ▶ `ua-civ-contagion` ✅
- **Protest:** placards and raised fists, seen the same on every node. ▶ `ua-civ-protest` ⚠ live not recorded
- **Provocation:** police who face a crowd, or strike it, make it angry. Each person has a hidden attitude, so only some of them riot.
  ▶ `ua-civ-provocation` ✅
- **Small things people do:** take out a phone on a pause, hold a stone ready to throw. (Shown inside the riot demos.)
- **Neutral people are never targets:** no unit aims at them, and they are not counted as threats. ▶ `ua-civ-neutral-gate` ✅

## 4. Armed people, riots and police

- **"Is he armed?" is judged by each observer.** It depends on range, light and optics, and on whether the weapon is in his hands, on
  his back or hidden. (`ua-armed-identify` ⏳ needs a toy-gun item.)
- **Combatants are engaged because of who they are; civilians only because of what they do.** Drawing a weapon is not hostile. Aiming
  it at the forces is. ▶ `ua-armed-draw-aim` ✅
- **Units remember who acted.** A man who aimed and hid his weapon again is still known to the patrol that saw it, and only to that
  patrol. ▶ `ua-armed-blend-back` ✅
- **No shot through bystanders:** a neutral person in the line of fire, or in the spread, holds the shot. ▶ `ua-armed-shield` ✅
- **Surrender:** hands up holds fire. A soldier lays down his weapons, and he can feint. ▶ `ua-armed-surrender` ✅ · `ua-civ-surrender` ✅
- **Riots:** rioters strike in melee, pick up stones and throw them, and throw a gas canister back. Each one turns Hostile only by his
  own act. ▶ `ua-civ-riot-stone` ✅ · `ua-civ-melee` ✅ · `ua-armed-gas-throwback` ✅
- **Police force levels** (Warn, NonLethal, Lethal) and the **escalation ladder**: warn first, then non-lethal force, lethal force
  only after a warning is ignored. Self-defence lifts the cap against that one man. ▶ `ua-civ-escalation` ✅ · `ua-civ-escalation-warn` ✅ ·
  `ua-armed-self-defence` ✅
- **Police line and baton charge:** the police hold a post, use non-lethal fire and batons, and break the riot. ▶ `ua-civ-police-line` ✅
- **Celebratory fire:** a wedding guest firing into the air is warned, not shot. He is shot once he aims at the police.
  ▶ `ua-armed-celebration` ✅
- **Suicide bomber:** the vest stays hidden until he sets it off, and casualties fall with distance from the blast. ▶ `ua-armed-bomber` ✅
- **Command-wire IED:** the trigger man fires a placed charge when a hostile vehicle he can see enters the kill zone. ▶ `uc-convoy-hit` ✅

## 5. The soldier: sensing, threat, posture, weapons, injury

- **Senses:** sight, radar, thermal and hearing, plus danger areas along his route. What he remembers fades when the contact is no longer
  seen. A loud sound carries far: a shot is heard where the shooter cannot be seen. ▶ `tt-heard-shot` (recipe)
- **Threat = danger × freshness.** Armed, visible and near contacts come first; a civilian counts 0. ▶ `ua-threat-ranking` ✅
- **Posture by utility:** advance and attack, take cover, suppress, flee, hold prone or hold, chosen from health, threat and cover,
  without flicker. ▶ `ua-posture` ✅ · the same tree decides the same on every host ▶ `ua-three-hosts` ✅
- **Cover by query (EQS):** take cover, fall back, flank, or find a firing position. A lost target is flanked or a new firing point is
  found. ▶ `ua-attack-approach` ✅
- **Peek and fire:** hide, show himself, aim if the target is seen, fire, hide again. A firing spot gets "burned" when used too much,
  and he bounds to the next cover. He reloads in cover. ▶ `bt-window-duel` ✅
- **Danger crossing:** he holds short of a road someone is watching and rushes across once it is clear. It is built both as a behaviour
  tree and as a blueprint. ▶ `ua-danger-crossing` ✅ (`-bp` for the blueprint)
- **Weapon choice per shot** from effectiveness, ammo and range band. ▶ `ua-weapon-choice` ✅
- **Injury:** ok, wounded (slower), incapacitated (cannot move or fire; bleeds out in 120 s; no longer a target) and dead.
  ▶ `ua-casualty-care` ✅
- **Under fire:** a soldier moving under fire runs crouched, drops prone at a near miss, stays down while pinned, and rushes on when the
  fire stops. ▶ `uf-alone-under-fire` ✅
- **Reflexes without orders:** a soldier with no task still reacts to fire by his type's standing orders (SOP); one stamped inert does
  not. A civilian ordered to stand still is frightened all the same. ▶ `df-lone-soldier-reflex` · `df-civilian-ordered`
- **Grenade or rifle:** against a man behind sandbags, the grenadier closes in and lobs a grenade while the rifleman fires.
  ▶ `ua-grenade-or-rifle` (in-process rail ✅, no live check)
- **Sentry:** a sentry watches his sector, notices a man creeping up behind him and turns to face him. ▶ `ld-sentry-surprise-360` ✅
- **One soldier, the whole kit:** a rifleman on a street with his own rules of engagement and drill, against three hostiles.
  ▶ `ua-universal-soldier` (in-process ✅; the live check is blocked by CE-2243)

## 6. The squad

- **The leader commands only through orders.** Phases move on when conditions are met: the covering men have fired, the movers are in
  cover.
- **React to contact:** assault, break contact, hold or rescue, chosen by utility. A far contact is closed by fire and movement.
  ▶ `uf-squad-under-fire` ✅
- **Fire and movement:** one half suppresses while the other rushes up to 15 m to cover, by halves or in buddy pairs.
  ▶ `ua-fire-and-movement` ✅ · `ua-fire-and-movement-pairs` ✅
- **Bounding overwatch:** one half moves while the other watches, and the watchers open fire at contact. ▶ `ua-bounding-overwatch` ✅
- **Fire distribution:** the leader spreads the squad's fire over the targets it has seen, at most two men per target.
  ▶ `ua-fire-distribution` ✅
- **Crossing a road:** security first, one pair at a time, far side secured, security last. Contact during it turns into a firefight.
  ▶ `ua-squad-danger-crossing` ✅ · `-contact` ✅
- **How to cross** (cross, bound or hold): open ground means bounding. ▶ `ua-squad-bound-open-ground` ✅
- **March:** the man nearest the goal leads, and laggards catch up. ▶ `ua-squad-march-loose` ✅
- **Casualty care:** a man goes down, covering fire starts, and the medic (never the leader) drags him to cover. Under heavy fire nobody
  goes until it slackens. ▶ `ua-casualty-care` ✅ · `-heavy` ✅
- **Succession:** a downed leader hands over command; the medic is skipped. ▶ `ua-casualty-care-leader` ✅
- **Carry:** only the incapacitated are carried. The carrier is slow, cannot fire, and drops the casualty alive if he is hit himself.
- **Embark and disembark** vehicles and helicopters. ▶ `uc-convoy-hit` ✅ · `ua-air-assault` ✅
- **Drills by recipe:** react to contact, suppress and flank, take cover, march in a wedge — small library scenarios that show one drill
  each. ▶ `tt-suppress-flank` · `tt-react-contact` (recipes, in-process rails ✅)
- **Standing orders (SOP):** the same fire makes one squad drop to cover and another march on through it, by their SOP. ▶ `sop-demo`

## 7. Platoon and command chain

- **Platoon march:** squads in columns, with one held back. ▶ `sl0-platoon-march` ✅
- **Platoon bound:** one squad supports while another moves to the objective. The platoon freezes both squads on contact and resumes when
  it is gone. ▶ `cc-platoon-bound` ✅
- **Platoon attack on an area,** objective by objective: base of fire 120 m off, a bound to a flank, fire lifted, the building cleared or
  assaulted through, then consolidate. A failed assault hands over to the reserve; a casualty calls the reserve. ▶ `pc-attack-dry` ✅
- **Platoon defence against a platoon attack,** AI on both sides. ▶ `pc-defend-house` ✅
- **Reports up the chain:** contact, SPOT (what was seen: vehicle, air defence, a mortar heard launching), SITREP, and requests for fire
  support or reinforcement. Nothing is sent without a report behind it.
- **The HQ answers:** air defence near the point → the strike plane on it first; otherwise the attack helicopter searches the point;
  otherwise the battery fires. A reinforcement request sends a free unit, never the one asking. ▶ `uc-request-chain-dry` ✅ ·
  `uc-reinforce-dry` ✅ · `uc-a10-zu23-dry` ✅
- **The flagship:** a convoy, an IED, an ambush round a corner, a spotter and a mortar, a ZU-23, an HQ with air and a reserve, and
  civilians, all in one battle on demo-city. ▶ `uc-urban-battle` ✅ — the IED, the dismount and the mortar now happen on screen; the air
  strike does not yet (CE-4032)

## 8. Buildings and close quarters

- **Doors:** a mover routes round a locked door and opens a closed one on the way. A locksmith unlocks a door. ▶ `bt-doors` ✅
- **Blast and fragments inside buildings:** fragments hit the body parts the stance exposes, so a prone man survives a grenade that
  wounds a standing one. A roof burst spares the floor below. ▶ `bt-grenade-posture` ✅ · `bt-mortar-roof` ✅
- **Throws and mortar rounds fly a real arc.** A door inside the lethal radius is breached.
- **Window duel:** each side fights from windows and cover points on each storey. ▶ `bt-window-duel` ✅
- **Room entry:** the squad stacks beside the door out of the window's view, breaches, and enters one man at a time to its points of
  domination. ▶ `bc-room-clear` ⚠ CE-2341 (about 1 run in 4 the entry team is lost)
- **House clearing:** a squad clears a house room by room from its front door, the fire teams taking turns, the civilians inside spared.
  ▶ `bc-house-clear` ✅
- **Defending a house:** defenders take window and roof posts and open fire as the enemy crosses their sector. ▶ `sq-defend-house` ⚠ CE-2304

## 9. Vehicles and armour

- **3-D armour hits:** the face that is hit comes from where the round arrives (a steep or top-attack round hits the roof). Each face
  has its own armour against each penetrator. A round over the roof misses. ▶ `ua-drone-shelter` ✅ (a roof hit)
- **The weapon picks the round:** the cheapest round that is enough for the target. Switching rounds costs a reload.
  ▶ `ua-tank-ammo` ⚠ soft targets survived; the causes are fixed but it was not re-run
- **Guided missiles:** command, fire-and-forget and IR guidance, top attack, minimum range, no firing on the move.
  ▶ `ua-javelin-lock` ✅
- **Weapon choice per shot:** the TOW at the tank, the 25 mm at the man. ▶ `ua-weapon-choice` ✅
- **Fight from cover:** a vehicle hides, shows itself, fires and withdraws. How long it stays exposed depends on the threat's aim time.
  It reverses back to the same hide, or scoots to a new one. ▶ `ua-tank-corner` ✅ · `ua-technical-scoot` ⏸ (CE-2232)
- **Stand-off:** a 25 mm IFV kills RPG men at 300 m and more without being touched. Up close, an RPG hits it. ▶ `ua-ifv-standoff` ✅
- **Hull-down platoon:** four tanks attack over a crest in alternating pairs, one round per exposure, then back down.
  ▶ `ua-hill-attack` ✅
- **Anti-tank infantry:** an RPG man moves from window to window; a TOW gunner holds his sight on the target for the whole flight.
  ▶ `ua-rpg-window` ✅
- **Convoy under attack:** a carrier hit with troops aboard pushes through. A stopped carrier unloads its squad, and the squad fights.
  ▶ `uc-convoy-hit-ifv` ✅ · `uc-convoy-hit` ✅

## 10. Drones, aircraft and air defence

- **FPV drones** search for a target, dive on it and hit the first thing in their path. A roof or deck shields what is under it.
  Vehicles hear the drone and hide under cover. ▶ `ua-drone-shelter` ✅
- **Air attack:** level bombing released on the weapon's cue, gun and rocket runs that lead the target, and a door-gun orbit that
  reverses when a gunner is shot. No weapon is used if its blast would catch a friend or a neutral. ▶ `ua-air-attack` ✅ · `ua-air-strike` ✅
- **Air defence:** SAMs and AA guns fire at the newest aircraft. Aircraft can be hit, and they fall. On a missile warning they drop
  decoys and break away; an IR seeker can be fooled. ▶ `ua-air-defence` ⚠ the flares and the re-attack are not shown
- **Air assault:** troops ride the helicopter, get out only once it lands, and it lifts off after the last man. ▶ `ua-air-assault` ✅
- **Air rescue:** a medic carries a downed pilot to a helicopter. ▶ `ua-air-rescue` ✅
- **Ejection:** a pilot ejects on his own when his aircraft drops to 20 % health or is wrecked. He lands, moves at least 100 m away and
  goes prone. ▶ `ua-eject` ⚠ this demo ORDERS the eject at 40 s; no demo yet shoots a plane down and lets the pilot eject by himself
- **Command chain with air:** the HQ sends the strike plane at nearby air defence first; otherwise the attack helicopter searches the
  point. ▶ `uc-a10-zu23-dry` ✅ · `uc-request-chain` 🟡

## 11. Indirect fire

- **Fire solutions:** a low arc at the lowest charge that clears the ground, else a high arc, else the mission is refused. Dispersion is
  deterministic.
- **The observer adjusts** from 800 m down to 50 m by watching one gun's bursts, then calls fire for effect.
- **Battery play:** time on target (every round arrives together), and converged, parallel or open sheaf.
- **Danger close:** a friend within 600 m refuses the mission unless it is ordered danger close.
- ▶ `ua-indirect-fire` ✅ shows all four as vignettes. A **spotter** picks his own target and runs the mission ▶ `uc-request-chain` 🟡

## 12. Weapon systems and airborne

- **Weapons are entities:** a sentry gun, an M2 that is set up and manned, a Javelin that is carried, a Kornet team. Kill the gunner and
  the gun goes silent; weapons can break. ▶ `ua-weapon-systems` ✅
- **Brain and muscle:** the behaviour names the target; the weapon picks mount, round and rules of engagement; the muscle slews and fires.
  A person fires only within 75° of where he faces, or turns first.
- **Fast rope:** eight men rope onto a roof in under 8 s and form a ring. ▶ `ua-fast-rope-roof` ✅
- **Parachute:** a stick of eight jumps from a release point upwind, lands near their points and assembles. ▶ `ua-airborne-drop` ✅
- **Falling hurts** above 6 m/s.

## 13. Built but not shown, and not built yet

| | what |
|---|---|
| **built, no demo** | weather and night in perception · a thermal sensor (no unit type carries one) · our vehicles driven by SUMO · ownership transfer · a pilot ejecting from a plane that was shot down · decoy flares you can see · navmesh rebake after a change · the Stride host (Windows only) |
| **not built** | structure damage (walls, collapse, bridges; only door breach exists) · temperament and stress breaks · knife attacks and closing fast · player fratricide · a toy-gun item (blocks `ua-armed-identify`) · team stretcher carry · a medic who stabilises · helicopter battle positions and pop-up attacks · radar SAMs · smoke · counter-battery fire · rappel · wind drift on rounds |
| **known wrong** | see [OVERVIEW_Rules_And_Demos.md §11](OVERVIEW_Rules_And_Demos.md#11-not-built-open-or-known-wrong) and [PLAN_Feature_Films.md §4](PLAN_Feature_Films.md) |
