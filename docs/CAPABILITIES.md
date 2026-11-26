<!--STATUS
state: LIVE
updated: 2026-11-24 (coordinator: §7 the LLM commander — the decision contract, the duel) · 2026-11-22 (behaviors: §5 perception checked against the truth — the pe-* suite) · 2026-11-22 (coordinator: §1b external presentation clients — the linked client, client-server multiplayer through the editor) · 2026-11-19 (coordinator: §1b sensor views + the AI thermal sensor on °C; §12a flame weapons; §9 rotors and smoke dischargers; §3 the civilian mood seen everywhere; §6 the corridor street fight; §9b the engineers answer; §16 the dragon) · 2026-11-18 (coordinator: §1b data for external renderers — thermal drivers, vehicle appearance, the shared look library; §8 glass windows and removing a baked building; §9 vehicle damage / hatches / lights; §16 fixed today and in progress) · 2026-11-17 (backend: §16 bows, birds, the rabbit hunt, the warrior built; outfit data built) · 2026-11-17 (behaviors: a civilian stops on the signal; §3a / §9 firing seats engage on their own) · 2026-11-17 (coordinator: §7 NATO symbols on every unit, §9b the back-out, §13 the status table, §14.2 symbols, §15 merged, §16 being built now) · 2026-11-17 (§9 the vehicle checkpoint and ramming) · 2026-11-16 (§3a animals, §7 the battalion assault, §9 motorcycles, §10 realistic FPV / radar SAM / anti-drone fire, §9b minefields 🚧, §15 the stochastic unit 🧪, §14.7 the terrain editor; §13 refreshed) · 2026-11-15 (§2b dynamic terrain — built, the remainders marked 📐) · 2026-11-14 (§2b dynamic terrain — designed and planned, the 📐 mark; §2a's tree felling and §13 point there) · 2026-11-10 (§2a terrains from real places and at large scale; §1a, §9, §13 and §14.7 — the week's new features, from the tracker and the status files) · 2026-11-06 (§14 authoring, live control and review — what a user can change and when, per mode; from the editing-UX inventory) · 2026-10-30 (§1a running, reviewing and filming — run modes, DAR / AAR, the branch, the timeline index, the film director, scenarios as tests; §1's recorder and filming bullets point there) · 2026-10-21 (first version — coordinator; built from OVERVIEW_Rules_And_Demos.md, the scenario descriptions and a code sweep)
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
  - UX/DESIGN_Editing_UX.md — OWNS the editing UX (inventory behind §14, the target design, the Details-tab review)
  - PLAN_Dynamic_Terrain.md — OWNS §2b's programme: the tasks (CE-4596…CE-4604), their order and gates; the designs DESIGN_Ground_Patches_And_Road_Blockage.md and DESIGN_Vehicle_Breaching.md
  - USAGES.md — OWNS what PEOPLE use the engine for and what it saves them (demo films, QA bug illustration, reviews, scenarios as tests …)
  - TECHNICAL_OVERVIEW.md — OWNS the engine architecture (ECS, cluster, replication, replay, tools); ../README.md is the product page
-->

# What HROT can do

A short tour of the simulation's capabilities, one chapter per area. Each bullet is one capability; **▶** names the scenario that shows
it best (`scenarios/<category>/<name>/scenario.json` — showcases, demos by area, tests; a plain name finds it, R-4185). Every scenario carries a `description` saying what happens and how to judge it, and most have
a live check: `python3 scripts/utility-demo-check.py --launch <name>`.

- **Marks:** ✅ the live check passes · ⚠ passes with a caveat · ⏸ parked, varies run to run · ⏳ not built · 🚧 **being built now** on a lane (the design is ruled and dispatched) · 🧪 **experimental** — built on a separate branch, not part of the product until the user takes it in · 📐 **designed and planned** — the
  design is ruled and its tasks are filed, the build has not started (it will come soon).
- **Deeper:** the rules behind each bullet, with code and design links, are in [OVERVIEW_Rules_And_Demos.md](OVERVIEW_Rules_And_Demos.md).
  The machinery is in [OVERVIEW_Behaviour_Building_Blocks.md](OVERVIEW_Behaviour_Building_Blocks.md). The engine architecture is in the
  [Technical overview](TECHNICAL_OVERVIEW.md).
- **Films:** [PLAN_Feature_Films.md](PLAN_Feature_Films.md) lists the clips that show each chapter.

## 1. The engine — in one screen *(depth: [Technical overview](TECHNICAL_OVERVIEW.md))*

- **ECS core (FDP):** zero-allocation entities and components, a double-buffered event bus, phases, a module host. (Technical overview §2)
- **A distributed cluster:** separate processes for the simulation host, the AI (CGF), the 2-D map, the instructor station,
  traffic, the editor and replay. They can run as one process (`--mode all`) or across machines. (Technical overview §3, §9)
- **Brain and muscle are split:** the AI decides (intent), the simulation acts and reports back (status).
- **Replication** over CycloneDDS with per-component translators, ownership transfer and dead reckoning, so every node sees the same
  world. (Technical overview §6)
- **Time control:** play, pause, step one frame, change the timescale. All nodes stay in step. (Technical overview §10)
- **AI authoring three ways:** behaviour trees, state machines (HSM) and blueprint visual scripts, with utility decisions and an
  environment query system (EQS). (Technical overview §4, §5)
- **Flight recorder and Replay Browser:** record a run, then load, seek and step through it. (Technical overview §8) — what it is used for: §1a
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
  ✅ *(2026-11-22, CE-11035)* the director's dip to black and its subtitle also reach a live client on the suggested camera (`Fade`, the caption's serial / position /
  alpha, the text on `FilmCaption`; [Look Rules §3.4](designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md)) — DPS `View` sending still to come (DD-022 P-8)
- **Scenarios are tests:** each scenario carries machine-checkable conditions; a verify run checks them and records the run; a night run
  covers all of them. ([DESIGN_Scenario_Regression](DESIGN_Scenario_Regression.md)) ✅ · Their documentation as stills per check ⏳ (the take
  builder is built, the rendering is not wired)
- **Players:** a player in an image generator takes over a unit; the server and two players play one scenario. ⚠ the acceptance run is open
  (CE-4080)
- *(added `2026-11-10`)* **The run mode switches at once** from one indicator button, which shows the mode the cluster IS in; the one **Save**
  button shows what Ctrl+S will do in that mode. ([AQ108 §0.A](blueprints/Architect_Question_108_Run_Mode_Picker.md); CE-3405…CE-3412) ✅
- **Run to …** a chosen sim time from Edit or Preview (CE-4316) ✅ · **Review** beside ▶ in Live (CE-4321) ✅ · **End** always works for an
  instructor or author (CE-4322) ✅ · **autosave** every 2 minutes while editing (CE-4099) ✅ · the start screen lists **recent** scenarios (CE-3414) ✅
- **Players are briefed** before a Live run and see a **summary** at its end. (CE-3416, CE-3418) ✅
- **Recordings collected and archived:** a finished exercise's recordings are gathered from every node into an archive, imported back, or
  deleted from it; one transfer at a time and never during a Live run. ([Recording Storage](DESIGN_Recording_Storage.md), CE-4330) 🟡 AAR
  placement on a different cluster: placed by node name then kind (built), the AAR's Start / Cancel question and the greyed *Go Live* when a
  file plays on another node's id wait for ui (CE-4331)
- **The merged review:** every station's recording opens as ONE world that plays; per unit, what its owner and each other station held
  (*By node*), a *Replica differs* lane, search across all stations, a per-station time offset. ([Review perspective §6](UX/DESIGN_Review_Perspective.md);
  CE-4354) ✅
- **Bookmarks and notes** on a run's timeline, kept beside the recording; the Replay Browser shows them. ([Bookmarks and notes](UX/DESIGN_Bookmarks_And_Notes.md);
  CE-4325, CE-1467) ✅

## 1b. Data for external renderers and thermal imagers *(added `2026-11-18`)*

Everything an outside image generator (a game engine, a physics-based thermal renderer, a DIS gateway) needs is SIMULATION STATE — recorded,
replicated to every node, renderer-agnostic — and HROT's own map reads the same state.

- ✅ *(2026-11-22)* **External presentation clients — the linked client** (CE-11018, CE-11026): an outside renderer (a game engine such as
  Unreal) joins a HROT node through a gateway node extension and presents the simulation. Every load waits for it; the director's clips,
  prepared on HROT's own 3-D map, are rendered by the client instead — its pictures and its sound — when one is linked
  ([DESIGN_Linked_Client_Director](designs/IG/DESIGN_Linked_Client_Director.md)).
- ✅ *(2026-11-22)* **Client-server multiplayer through the editor** (CE-11027, [Q114](blueprints/Architect_Question_114_Seats_On_One_Node.md),
  R-11010 / R-11011): the editor works as the all-in-one, networkless SERVER — simulation, AI and HROT's own deploy server in one process —
  and SEVERAL external presentation clients connect to it through the gateway, each trainee a seat of his own (up to 16 per node): he joins,
  picks a side and a unit, deploys through HROT's deploy flow, possesses it and leaves, his seat freed. ⚠ This is DIFFERENT from HROT's own
  Player: a HROT Player is a PEER NODE of the cluster (a DDS participant with its own ECS world); a linked client is a thin presentation
  client of one server node ([Player Join §12](DESIGN_Player_Join.md)).

- ✅ **Thermal drivers, never temperatures** — every node and an outside thermal imager compute an entity's heat from states and the time they
  began: the engine or body (off · idle · running; standing · moving, and since when), each weapon's last burst, fire as spots on a vehicle, a
  building or a terrain cell (fire burns health; damage starts fire), the time of death, the weather's "since", a thermal class per terrain
  material, IR-blocking smoke, and the warm patch a vehicle leaves where it idled. Our AI's thermal sensor reads the same calculation.
  ▶ ✅ `th-idling-tank` (an idling crewed tank seen at night, a parked empty one not) · ✅ `th-burning-wreck` (a late-joining node sees the wreck
  burning) · `th-vehicle-left-warm-spot` ([DESIGN_Thermal_Drivers](designs/thermal/DESIGN_Thermal_Drivers.md), CE-4646, CE-4657…CE-4664)
- ✅ **Vehicle appearance** — partial damage per part (hull, turret, gun, each track, engine; aircraft parts) as Intact · Damaged · Destroyed ·
  Burning, the kill kind (knocked out · burnt out · catastrophic), hatches / doors / ramps that open when the squad dismounts and a crewman with
  his head out, and lights under a light discipline (normal · convoy · blackout) with darkness and engine state.
  ▶ ✅ `va-tank-kill-kinds` · ✅ `uc-convoy-hit` (the APC's ramp) · ✅ `va-lights-night` ([DESIGN_Vehicle_Appearance_State](designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md), CE-4670)
- ✅ **One look library for every renderer** (`Hrot.Look`, no presentation dependency) — which effect a shot, a burst or an impact makes, how a
  person is dressed, how a vehicle looks; our map and any gateway call the same functions, so every renderer shows the same picture
  ([§7 of the same design](designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md), CE-4671)
- ✅ **The DIS mapping is written down** for a gateway — damage, fire, engine, lifeform health ([NED_DDS_Protocol](reference/NED_DDS_Protocol.md))
- ✅ *(2026-11-19)* **Rotors, engines, smoke dischargers, the full DIS mapping** — rotor and engine speed derived from the engine state; a tank's
  smoke dischargers fire a fan of screening grenades (smoke that blocks sight and infrared); the gateway table now covers lights, hatches, the kill
  kind and the effect → DIS detonation-result codes; our map draws the hatches and lights (CE-4670)
- ✅ *(2026-11-19)* **Night vision and a thermal view in our own map** — `F4` cycles normal · night vision · thermal: night vision on the moon and
  light sources (a cigarette's ember shows), a thermal picture solved from the same thermal drivers (warm engines, the patch a tank left, roads as
  asphalt), the observer's outlines. ▶ ✅ `sv-night-and-heat` · ✅ `th-vehicle-left-warm-spot` (CE-1984, CE-1985, CE-1989)
- ✅ *(2026-11-19)* **The AI thermal sensor sees by temperature contrast** (°C against its background, not an abstract heat number) — same
  results at 1, 4 and 8 threads (CE-1987, R-1058)
- ⏳ "pop smoke" when a missile is fired at a vehicle (behaviors, after the dischargers)

## 1c. Flame weapons *(added `2026-11-19` — [DESIGN_Flame_Weapons](designs/weapons/DESIGN_Flame_Weapons.md), CE-4635, CE-4637)*

- ✅ **The flamethrower** — a man-portable or vehicle-mounted jet: one ignition rule sets men, soft vehicles, roofs and grass alight; burning
  hurts in steps; the flame is heard as one roar; the jet is thrown into a bunker or a room. ▶ ✅ `fl-flamethrower-cone` · ✅ `fl-clear-the-bunker`
- ✅ **Rules of engagement hold it** — allowed for military forces by default, off in a police / civil scenario, never fired with a neutral in the
  flame. ▶ ✅ `fl-roe-holds`
- ✅ **Napalm** — a strike lays a burning ellipse on the ground that burns out and leaves a scorched mark. ▶ ✅ `fl-napalm-strike` · `fl-napalm-treeline`
- ✅ **Buildings burn** — a timber house catches and burns. ▶ `fl-timber-house`

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
- **Ambience layers — the war and the alarms around the scenario, by KIND** ✅ (CE-11034, R-11014, ui; slice 1; the editor picker and HROT's own mixer are slices 2–3, ⏳): an entity carrying `Ambience { Kind, Intensity, Radius, Until }` — a
  short dotted name (`battle.distant`, `alarm.airraid`, `event.drone` …, never a sample), positionless = heard everywhere, with a position = heard within its radius, ending at
  its sim time. A renderer maps the name to its own sound and an unknown child falls back to its parent (`alarm.airraid.preliminary` → `alarm.airraid` → `alarm`). A trigger, a
  behaviour or a director raises, updates (the same kind at the same place is ONE layer) or removes (intensity 0) one with the `SetAmbience` action; a late joiner hears what is
  sounding. Replicated (`dtAmbience`) for the gateway's DPS row. ▶ `ambience-air-raid` (judged by `scripts/scenario-regression.sh ambience-air-raid`).
  📄 [DESIGN_Ambience.md](designs/audio/DESIGN_Ambience.md)
- **Thermal sight sees engines and bodies by their heat** ✅ (CE-4658, CE-1987): a target is seen when its hottest part (an engine, a barrel, a body, a fire) differs enough in °C from the ground behind it — the same surface model the thermal view draws, so a sun-heated parked tank shows against cool grass but not against hot soil, and a man stays visible at dawn and dusk. An idling, crewed tank is seen at night, a parked cold one is not — `th-idling-tank`, `th-man-at-dawn`, `th-hull-in-the-sun`, `th-hull-on-hot-soil`; carried by the thermal observer (TKB 215: 4 K of contrast). 📄 [DESIGN_Thermal_Drivers.md §4e](designs/thermal/DESIGN_Thermal_Drivers.md)
- **Night-vision and thermal views of the 3-D map** ✅ (CE-1984, CE-1985, ui): F4 or View ▸ Sensor View cycles normal → night vision (sun, moon and starlight in lux, tube gain, grain, blooming lamps and fires) → thermal (every surface's °C from its material and the day's sun, sky and weather; engines hot at their end, men warm, fires saturating, the warm patch a vehicle left). A debugging view for an instructor: the °C under the pointer, a hovered unit's temperature and WHY (engine, barrel, fire, its signature against the selected observer's threshold), and corner brackets on every unit the selected observer's thermal sensor is hot enough to see — the AI's own rule. Judged by frame rails (`SensorViewFrameRail`) and its scene `sv-night-and-heat` (every heat and light source made by the simulation at night). 📄 [DESIGN_Sensor_View.md](designs/sensor-view/DESIGN_Sensor_View.md)
- **A destroyed vehicle burns, and its fire burns the men beside it** ✅ (CE-4660): fire is simulation state decided once on the Health owner (`FireState`), the same on every node and for a late joiner; a man inside the fire loses health each second and dies, one 40 m away is untouched; the 3-D flames read it — `th-burning-wreck`. ⏳ incendiary weapons, burning buildings and ground. 📄 [DESIGN_Thermal_Drivers.md §4g](designs/thermal/DESIGN_Thermal_Drivers.md)
- **The AI fires short bursts, and says so** ✅ (CE-11033, R-11012 / R-11013; Weapon Layers §5l): a rifleman fires 2–3 rounds inside 50 m or at a mover, 1–2 otherwise, a suppressing burst of 3–5 at a remembered spot, each round stamped with its burst (`BurstId` / `BurstRounds` / `BurstIndex` on the fire intent and notification); a player's rounds carry none. ▶ ✅ `rifle-bursts` (10 / 10) · ✅ the stamp crosses the brain → muscle and muscle → viewers DDS messages
- **A flamethrower sets men and soft vehicles alight** ✅ (CE-4635 FL-1, R-3111): the man-portable flamethrower (TKB 2022, its fuel 6069) throws no round — the jet is read off the weapon's burst record (`FlameJets`); a man in its cone burns after 0.3 s and burns on while the flame stays on him, a soft vehicle after 1.5 s; a wall or a hull stops it (an opening passes), an armoured hull never burns. ▶ ✅ `fl-flamethrower-cone` (7 / 7, under the parallel checks too) · ✅ napalm (FL-3), buildings (FL-4), the look (ui)
- **A flame tank burns men in a building through its opening, within the rules of engagement** ✅ (CE-3486 FL-2): the M67 (TKB 117) throws its flame INTO a window and lights who is inside on its line, never one behind the wall; no flame is released with a civilian anywhere in it (Protocol III, AW-20's rule run over the flame) nor where the ROE forbids incendiaries (`Roe.Incendiary`, down the chain; a police force's non-lethal cap holds it already). ▶ ✅ `fl-clear-the-bunker` (4 / 4) · ✅ `fl-roe-holds` (9 / 9, each hold beside a control)
- **A napalm strike burns men and leaves the ground burning** ✅ (CE-3487 FL-3): an A-10 with Mk 77 firebombs (TKB 410) lays an 80 × 25 m ellipse of fire along its run — every man inside is set alight at once, the ground burns 300 s (it burns whoever walks into it later) under a smoke cloud, and a burned patch is left; never released with a civilian inside the ellipse. ▶ ✅ `fl-napalm-strike` (7 / 7)
- **A timber house burns, and the men inside it** ✅ (CE-3488 FL-4): a flame jet held 2 s on a timber wall, or a napalm ellipse reaching the house, sets it alight — one fire over its footprint held on the building's damage record, burning its walls' time (600 s), and it burns whoever is inside even where the jet could not reach; brick, concrete and stone never burn. ▶ ✅ `fl-timber-house` (6 / 6, under the parallel checks too)
- **A dragon flies, lands and breathes fire** ⚠ (CE-4634 D-1, R-3112, fun pack): a rotorcraft on four legs (TKB 411) — takes off, flies a route, lands on its legs and takes off again; its breath (a flamethrower on its neck turret, from the mouth, 50 m) burns the men in front of it, never its own pillion; it is fireproof and an aircraft to radar. A player can fly it (*Drive*) or ride its firing pillion (the existing mechanisms). ▶ ✅ `dr-dragon-flight` (4 / 4) · ✅ `dr-dragon-breath` (8 / 8) · ⏳ its autonomous attack (CE-3490 design), its look and roar (ui)
- **A dragon (or any rotorcraft with a flamethrower) attacks on its own** ✅ (CE-3490, R-8012; [DESIGN_Breath_Attack.md](designs/air-attack/DESIGN_Breath_Attack.md) §7): ordered to attack a man it STRAFES — in at 60 m, down to 12 m and 10 m/s, breathing only inside its 50 m reach, over him, out and up, and back on a line 120° round, the line laid along the row of men it sees; ordered to strike a timber building it HOVERS 25 m off the nearest burnable wall and breathes on it 3 s; it never breathes on what cannot burn (brick, an armoured hull, an aircraft), nor under an ROE forbidding incendiaries, nor while a civilian is inside the flame it would throw. Scenarios ✅ `dr-dragon-attack` · `dr-dragon-village` · `dr-dragon-holds` (also under the parallel checks). ⭐ **The FLAME HELICOPTER** (TKB 412 "AH-64 (flame)", BA-9, R-8013): the AH-64 with a chin flame projector (60 m) and its Hydra pod — it breathes on men and rockets armour; scenario ✅ `ah-flame-attack`
- **Vehicles show their hatches, doors, ramps and lights to every renderer** ✅ (CE-4670 VA-S2 / VA-S3): a carrier's ramp opens as its squad dismounts and closes after (`uc-convoy-hit`); a crew's light discipline at night — head and tail lights when dark, the convoy marker, or blacked out (`va-lights-night`); an aircraft's beacon while its engine runs; each type's openings and light groups come from its class, overridable per type; replicated to every node and a late joiner. ⭐ A crewman can put his head out of his hatch (it stays open, he rides exposed) and a station can switch the light discipline — both as requests carried to the vehicle's owner; brake lights are derived by each renderer from deceleration (`VehicleLook.Lights`). ⚠ no scenario yet for head out / the switch: nothing ASKS for them until behaviors' SOP / orders do (rails only); ⏳ our 3D map drawing openings and lights (ui), the searchlight's aim, interior and landing lights (CE-3475). 📄 [DESIGN_Vehicle_Appearance_State.md §6a–§6b](designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md)
- **A vehicle's partial damage and how it was destroyed are simulation state every renderer reads alike** ✅ (CE-4670 VA-S1): a destroyed track, engine or gun is a part state by its zone's threshold, and a kill is KnockedOut / BurntOut / Catastrophic from the part the killing hit landed on and the type's stowage (a T-72's hull carousel throws its turret) — replicated, the same for a late joiner, drawn on our 3D map (`va-tank-kill-kinds`; `Hrot.Look.Vehicles.VehicleLook`)
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

## 2a. Terrains from real places, at large scale *(added `2026-11-10`; the programme: [PLAN_Large_Terrain](PLAN_Large_Terrain.md))*

- **Import a terrain made for another simulator:** its heights, every building with its storeys, doors and windows, its trees and its road
  network become an HROT terrain — every house enterable (open floors and a stair ramp), garages and sheds solid, pitched roofs. A 5.5 km
  square imports in ≈ 6 s and loads in ≈ 2 s. ([Terrain Import UTM §10–§12](DESIGN_Terrain_Import_UTM.md); CE-4368) ✅ run on real data
  `2026-11-10` ([report](blueprints/REPORT_Terrain_Import_Real_Runs.md))
- **A terrain whose import input is committed imports itself on first use:** a node that is asked for a terrain it does not have (e.g. `Stechovice`) imports it from `assets/terrain-input/<name>/` — heights, buildings, trees, roads AND the navmesh — once, under a lock, and caches the folder; a changed input re-imports (the old version is kept). 4-core VM: 289 s once, then 0 s. ([Terrain Import UTM §13](DESIGN_Terrain_Import_UTM.md); CE-11037; rails `SourceImportTests`) ✅
- **A terrain from any place on OpenStreetMap:** File ▸ New ▸ Terrain from OpenStreetMap… — search a place, draw the area on the map,
  preview what will be built (buildings by height, woods, water, walls, roads, the counts and a load verdict), import with 30 m Copernicus
  heights and a SUMO road network, open it in a new scenario. Also a command-line tool. ([Terrain from OSM](DESIGN_Terrain_From_OSM.md);
  CE-4373…CE-4385) ✅ live on Windows
- **Synthetic test terrains** with the same statistics as a real one, generated from a seed (`hrot-terrain-synth`). ([Terrain Synth](DESIGN_Terrain_Synth.md)) ✅
- **Large terrains in budget:** 5.5 × 5.5 km, 1 m heights, ≈ 1 800 houses, 1 000 soldiers — a warm load ≈ 25 s (≤ 30 s), the navmesh baked
  at import so the first open does not wait for it, ground sight lines over 3 km ten times faster, the 3-D view drawn in chunks with less
  detail far away, the 2-D map at that size. ([Scale Rig](DESIGN_Scale_Rig.md); CE-4363…CE-4367, CE-4393, CE-3423, CE-4376) ✅ — frame rates
  on a real GPU being measured (CE-4385, CE-4387)
- **UTM coordinates:** a terrain placed on the UTM grid (local metres near zero, grid north). (CE-4394) ✅ · every terrain on UTM and the
  tangent plane retired ([Geo Origin §6](DESIGN_Geo_Origin.md), R-4197; CE-4396) ⏳ building
- **Trees and woods:** a wood hides what is inside it (sight lost per metre among the trees, from the air too), forbids wheeled vehicles
  and slows tracked ones, and stops some rounds; a lone tree is a trunk that blocks. Drawn as trees near and a canopy far. (R-4190; CE-4369,
  CE-4377) ✅ — felling a lone tree ✅ (§2b)
- **Destructible buildings:** a blast or a penetrating hit breaches a wall — a hole to see and shoot through, or a passage to walk through;
  a heavy bomb close by collapses the whole building into rubble and kills the people inside. The navmesh follows in the background and
  only the paths through the changed tiles replan; breaches are saved, replicated and drawn on every host. (R-4188; CE-4391, CE-4392)
  ▶ `bd-destruction`
- **Glass in windows:** a window has a pane that sight passes unchanged and that muffles sound (20 dB); rounds crack it (two cracks kept,
  the third breaks it), a man firing out breaks it at once, a grenade's fragments or a big blast's pressure shatter it, and a breach takes it
  with the wall. A broken window lets sound into the building and is an open hole again; no navmesh rebake. Saved and replicated; the
  3-D map draws the pane, a crack star at each hit, an open hole with shards in the frame, a shard burst and the sound of breaking glass,
  the same on every map node, a late joiner included. (R-5068; CE-5032, CE-5033, CE-5034) ✅ `gw-shoot-the-window` · `gw-fire-out`

## 2b. Dynamic terrain — the ground and the walls change while the simulation runs *(✅ built `2026-11-14`…`15` by the `terrain` lane — the remainders marked 📐; the programme: [PLAN_Dynamic_Terrain](PLAN_Dynamic_Terrain.md), CE-4604)*

Everything in this chapter is **✅ built** (by the `terrain` lane, `2026-11-14`…`15`) unless marked 📐 — a named remainder with its owner.
Designs: [Ground patches and road blockage](DESIGN_Ground_Patches_And_Road_Blockage.md) · [Vehicle breaching](DESIGN_Vehicle_Breaching.md).
Pictures: [ground shaping and ditches](UX/ground-shaping-and-ditches.svg) · [vehicle breaching](UX/vehicle-breaching.svg).

- **Houses stand level on hills:** the importer levels the ground under every house at the house's lowest corner, cutting the uphill side
  with a gentle skirt, so no house is half buried and every door opens onto the ground. (P-1, CE-4403, R-4228; rails `ImporterTests.CE4403_*`, `HeightStoreTests.CE4403_*`) ✅ — 📐 still to come: a re-import kept as a new terrain version (D-4, awaits [Architect Question 112](blueprints/Architect_Question_112_Terrain_Versions_On_Reimport.md)); the hilly scale terrains re-imported (CE-4402)
- **Roads lie flat across a slope:** at import every road levels the ground across its own width (the grade along it kept, junctions
  levelled, bridges left alone); the SUMO road network is re-elevated to match, so SUMO's cars, the drawn road and our vehicles agree. Houses
  next to a road are tolerated — the ground ramps from the road's edge to the façade. (P-1, R-4231, R-4234; rails `ImporterTests.CE4403_ARoad*`, `CE4403_HousesByTheRoad*`, `CE4403_ABridgeOverAValley_*`) ✅
- **A house placed while authoring gets its own level pad,** on every node the same, and *Save terrain as* keeps every pad. (P-3, CE-4597; rails `TerrainWorldMergeTests.CE4597_*`, `TerrainBakeTests.CE4597_*`) ✅
- **Shape the ground while authoring a scenario:** draw an area and level it, raise a hill, lower a hollow or make a crater; roads stay
  level and keep working (SUMO's network follows after saving); a warning when a road gets too steep. (P-5, CE-4592) — ✅ built: the Level,
  Raise and Lower tools (the newest shape wins, houses stay on their ground; rails `TerrainWorldMergeTests.CE4592_*`), the Details read-out
  with the grade warning (`GroundEffectTests`), roads re-levelled under a shape (`TerrainWorldMergeTests.CE4592_AHillOverARoad_*`) and SUMO's
  network re-elevated in the saved overlay (`OverlayCompilerTests.CE4592_*`); the Crater mode stays greyed (a drawn crater is V-14)
- **Ditches and foxholes:** draw a ditch along a line or click a foxhole into place; a man in it is hidden by the ground, the AI finds it as
  cover and defending squads take their posts in foxholes; an anti-tank ditch stops every vehicle while infantry crosses. (P-6, CE-4593) — ✅ built:
  the ditch, anti-tank ditch and foxhole tools, every ditch closed to vehicles, cover along the banks and round a foxhole (rails
  `TerrainWorldMergeTests.CE4593_*`), the Details panel, a ditch stopping at a road (`CE4593_ADitchAcrossARoad_StopsAtTheRoad`); 📐 still to
  come: a defending squad taking its posts in foxholes (V-29 — the behaviors lane, R-5031)
- **Craters:** a bomb or a shell leaves a hole in the ground on every node at once — the ground height, sight and fire change in the same
  moment; vehicles route round it, infantry crosses it, and after the gases clear its rim is cover. (P-4, CE-4591, R-4232, R-4233) — ✅ built:
  the crater record from the warhead's charge, the cratered ground for height, sight and fire the same tick on every node (a standalone brain
  included, over the wire), the navmesh closed to vehicles round it, a crater across a road cuts it (rails `CraterTests`, `CraterBakeTests`,
  `EntityGroundDamageTranslatorTests`, `BridgeClosureTests.CE4591_*`); saved with the scenario, and its rim is cover once the gases clear; and drawn with its scorch for as long as it exists
- **Blocked roads are blocked for everyone:** a roadblock, a wreck or a crater on a road makes SUMO's cars swerve round it or re-route, and
  our own road-following vehicles re-plan; clearing it re-opens the road. (P-2, CE-4590; rails `BridgeClosureTests.CE4590_*`, `CE4591_*`) ✅
- **Every wall stops a vehicle:** garden walls, fences and hedges hold every vehicle — AI or a player's — like a building's wall does; a low
  wall is met, never climbed onto; and avoiding a man on the road never swerves a vehicle into a wall. (B-1, CE-4598, CE-6014, R-4237,
  R-4239; rails `CarKinematicsSystemTests.CE4598_*`, `CE6014_*`; scenario `roadblock-blast`) ✅
- **A player's tank breaks through weak barriers:** a player driving a tank goes through fences, hedges, garden walls and sandbags (an IFV
  through the lighter ones; wheeled vehicles stop); a tank bumps a building wall open for the infantry without driving in; the driver sees
  and hears the break, or a "too strong" cue. The AI never breaks a wall by accident. (B-1…B-3, CE-4598…CE-4600, R-4236…R-4240, R-4243;
  rails `CE4600_*`, `AudioTests.CE4600_*`; a barrage on the showcase town measured — Breaching §7.17) ✅
- **Blasts breach garden walls too:** a charge, a shell or a bomb beside a garden wall, a fence or a hedge breaches it like a house wall —
  sight and fire pass at once, every node draws the gap, and a gap cut to the ground is walkable after the bake. (B-2, CE-4599, R-4236; rails
  `CE4599_*`) ✅
- **Every breach is a way in for the AI** — a gap by a blast, a tank or the bulldozer is a gap infantry walks through and the AI uses as a way
  into a building. (B-5, CE-4602; rail `BreachEntryTests`) ✅
- **Lone trees can be felled:** a bomb or a shell fells the lone trees round it, and a player driving a tank or an IFV fells one by ramming or
  pushing it; sight and fire pass at once on every node, the tree disappears from every view and the way opens; saved with the scenario.
  (B-6, CE-4603, R-4241; rails `TreeFellingTests`, `TreeFellingBakeTests`) ✅
- **The bulldozer clears on order:** point at a roadblock, a wall or a lone tree and the bulldozer pushes it away, cuts a gap or fells it.
  (B-4, CE-4601, R-4242; rails `ClearObstacleBehaviorTests.CE4601_*`) ✅ — the order and the cut; 📐 the map's "clear here" gesture
  ⚠ scenario `tt-dozer-breach` (R-4277): a house wall opens, but a FREE wall (a garden wall, a fence row) is not yet opened for infantry by the ordered cut in a live run — red, engine defect filed in its `$doc`
- **A real-map (OSM) bridge is a bridge:** an imported road tagged `bridge` becomes a deck from bank to bank at the banks' height — men, vehicles, the navmesh
  and SUMO's cars cross on it, the valley stays under it, and it collapses like an authored bridge (CE-5057 / CE-11063, `docs/DESIGN_Terrain_OSM_Bridges.md`;
  rails `ImporterTests.CE5057_*`; ▶ `te-osm-bridge` — 3 of 3 pass) ✅ on a synthetic net · ⏳ Stechovice's Vltava bridges wait for its regenerated road net
- **A laid bridge carries men and vehicles over a river, and can be blown:** a laid bridge (an assault or a floating span) is terrain on every
  node — the navmesh, the floor, sight and fire hold its deck — and road for our road-following vehicles while it stands; a charge on the deck
  drops it, whoever was on the span goes into the river, and the river is a barrier again. (BL-S1/S2, CE-4692, R-4299; rails
  `StaticObstacleBakeTests.CE4692_*`, `BridgeClosureTests.CE4692_*`, `ObstacleDamageTests.CE4692_*`, `FeatureKindsTests.CE4692_*`; scenarios
  `bl-placed-bridge`, `bl-placed-bridge-absent`, `bl-blow-the-bridge` — 3 of 3 pass) ✅ — the engineer who lays and recovers it is BL-S3 (backend, CE-4693)
- **An engineer lays and recovers a bridge on order:** `LayBridge {NearBank, FarBank}` to an assault bridge layer (TKB 118, an M104 Wolverine: 26 m deck over
  ≤ 24 m, 5 min) or a floating-bridge engineer (TKB 119: a ribbon bridge up to 100 m, 20 min); a gap wider than its span, or a bank in the water, is REFUSED
  with its reason on the engineer (`BridgeCarried.Refusal`); the deck is centred on the water gap, built after the laying seconds, baked and Open; the
  engineer backs off the approach; `RecoverBridge` takes it back (600 s) and the river is a barrier again; a bridge blown while being recovered is lost.
  Nothing on the ground wades into a river (men, animals, vehicles). (BL-S3/S4, CE-4693; rails `BridgeLayingBehaviorTests`, `BridgeLayerTkbTests`,
  `StaticObstacleBakeTests.CE4696_*`, `CarKinematicsSystemTests.CE4695_*`; scenarios `bl-too-wide`, `bl-recover`, `bl-assault-crossing`,
  `bl-floating-bridge` and the end-to-end `bl-river-crossing` judged by objectives — 5 of 5 pass) ✅ — ⚠ a vehicle cannot yet line up onto a narrow deck from
  an angle: a column crosses from the bridge's axis, one at a time (CE-4697)
- **Lone trees are felled** by a player's tank, a blast or the bulldozer; AI vehicles drive round them. (B-6, CE-4603, R-4241; rails `TreeFellingTests`, `TreeFellingBakeTests`) ✅
- **Everything is recorded:** every change is replicated to every node, saved with the scenario, restored on seek and replay, the same at
  any thread count. (rails `BuildingDamageSaveTests`, `CraterTests`, the translators `EntityGroundDamageTranslatorTests` / `EntityBuildingDamageTranslatorTests`;
  `stepped-hash.py --threads 1,4,8` equal on `bt-mortar-roof` and `roadblock-blast`) ✅

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
- **What a person wears comes from the TKB** (a type's wardrobe: garments, headwear, hair, colours; seeded variety inside its lists), with Middle-East dress — thobe, abaya, shemagh and agal, skullcap, hijab, niqab, chest rig, face wrap — and the Native American garments (buckskin, breechcloth, headband with feathers, braids, face paint). Drawn on the block figure; verified by frame rails (`PersonOutfitsRail`), not yet by a scenario. ▶ `civ-middle-east-market` ⏳ not yet built
- **Afghan people and the Native American warrior's look** — Afghan civilians (man, woman, boy, girl, elderly man, elderly woman) and fighters (rifle, RPG, machine gunner) in perahan tunban, waistcoat, pakol, lungee turban, patu and the chadari; the warrior with black hair and a braid each side, headband and feathers, beaded yoke, bone breastplate, armbands, necklace. Seeded inside each type's own lists. ▶ `civ-afghan-village` ✅ (the look itself is held by the frame rails `PersonOutfitsRail`)

## 3a. Animals *(added `2026-11-16`; ⚠ built by the `backend` and `ui` lanes, the batch still finishing — [DESIGN_Animals](designs/animals/DESIGN_Animals.md), CE-4623)*

- **Nine species** — cattle, sheep, horses, dogs, cats, hens, pigs, goats and birds — each with its own type, size, gait and sounds. They graze
  round their herd's spot, rest at night, grow uneasy, and flee from shots, vehicles and blasts. They are frightened by the SAME fear model as
  civilians, from every side.
- **Herds and flocks:** a herd moves and flees together; birds lift off as a flock and settle elsewhere. A frightened big animal breaks
  through a small fence (a wall or hedge holds); horses and goats jump low fences.
- **Species behaviours:** a guard dog barks at a stranger and bites one who comes close; a horse can be ridden, and its rider can fire from
  the saddle.
- **Animal areas:** draw an area, say what lives there and how many, and the animals are placed when the run starts.
- **Sounds that carry:** calls, alarm calls and barks are heard at their own ranges, and the soldiers' hearing picks them up.
- ▶ `farm-under-fire` ✅ (8 of 8 checks: a tank panics the herd, it flees, a cow breaks through the fence…) · feature scenarios (R-4277, `scripts/scenario-regression.sh <name>`): `ua-animal-guard-dog` ✅ 5/5 · `ua-animal-horse-rider` ✅ (the rider engages on his own from the saddle — CE-3454, no scripted order) · `ua-animal-flock-flush` ✅ 6/6 · `ua-animal-herd-road` ✅ 6/6 (⚠ the herd clears the road by fear; vehicles never yield to animals, CE-3457) · `ua-animal-village-fight` ✅ 5/5 · ⏳ a cat jumping onto a sill

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
- **What he believes is checked against the truth** (CE-2474 / CE-2476 / CE-2477, behaviors `2026-11-22`): one shot heard by several men is ONE
  enemy; a shot from a man he sees is that man; two shooters 30 m apart are two; a burst is never an enemy; however long he listens, a lone ear
  is never surer than it can be. Judged by the `perceives` check (missed · phantom · duplicate against the real enemies) ▶ `pe-one-shot-four-ears` ✅
  `pe-seen-shooter` ✅ `pe-two-shooters` ✅ `pe-shelled` ✅ · `pe-house-shot` ✅ (a shot seen fired is that man's, whatever way its sound came — R-2144, CE-2490/2491) · `pe-steps-round-corner` ✅ · `pe-seen-then-hidden` ✅ · `pe-wall-and-door` ✅ (the first wavefront through a thin wall; a man heard walking is one contact, CE-2492) · ⏳ two nodes (P8).
  📄 [Sensing §8.6e](DESIGN_Thermal_And_Acoustic_Sensing.md)
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
- **Revive:** one action puts a dead, incapacitated or wounded man back in the fight — full health, wounds / stun / his own fire cleared, move and fire
  back as his type allows; the Unit form's heart-pulse button beside Strength, `POST /entities/{id}/revive`, or a scenario's `revive` action; any node,
  answered by the owner of his Health (CE-11061, [Injury States §9](DESIGN_Injury_States.md)). Typing a Strength number sets only the number (R-11021).
  ▶ `ua-revive` ✅
- **Under fire:** a soldier moving under fire runs crouched, drops prone at a near miss, stays down while pinned, and rushes on when the
  fire stops. ▶ `uf-alone-under-fire` ✅ · `rf-pinned-in-open` ⚠ he goes prone, but never fires and walks toward the house instead of crawling to the wreck (CE-3464)
- ⚠ **Found by the new scenario checks (`2026-11-16`, regression suite item 7):** militia never break under fire (`ts-fire-discipline`, CE-3461) · the Waverer never breaks (`ts-house-defenders`, CE-3462) · an area's guard is outgunned before he fires first (`ia-area-defend`, CE-3463) · defenders never grenade a man hugging their wall (`rf-wall-hugger`, CE-3465). The regulars, the Coward and the Fanatic, the Shooter behave as designed ✅
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
- **After a broken contact** (CE-2466): the squad remembers the ground each enemy covers (its side's `UnderFire` knowledge, which ages), rallies and
  reports (LACE + SPOT), never assaults frontally a force it broke from without 3 : 1, stays out when no longer fit, holds short of covered ground
  and bounds along a route priced round it. ▶ `uf-corner-fight` ✅
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

- *(added `2026-11-17`)* **NATO symbols on the map for every unit** — squad, platoon, company and battalion, real or stochastic: the symbol
  is worked out from what the unit is made of (riflemen ⇒ infantry, tanks ⇒ armour), in its side's frame, drawn in the unit card's left cell or
  alone at the unit's centre with one echelon per zoom band, and in the ORBAT tree; one layer switch turns it on or off. A man or a vehicle never
  gets one. (agg-ui, CE-8608…CE-8611, [DESIGN_Unit_Symbols](UX/DESIGN_Unit_Symbols.md); rails `UnitSymbolDeriverTests`, `SymbolRendererFrameRail`) — ⚠ seen in the
  stochastic-unit scenarios; not yet checked by eye on an ordinary squad scenario
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

- *(added `2026-11-16`)* **A company assault under fire,** suppression first: the base of fire takes a covered building on the line of
  fire and opens up; mortars fire a sustained mission and a smoke screen; the assault crosses under smoke and is ordered forward again if its
  approach fails; a spent assault is relieved; IFVs fire on reported posts they cannot see; a drone team scouts and strikes; phase lines have
  deadlines. Three versions of the battle: the plan works, it stalls (the reserve is committed), or the defence collapses.
  ▶ `cc-battalion` ✅ · `cc-battalion-stall` ✅ · `cc-battalion-collapse` ✅ ([DESIGN_Assault_Under_Fire](designs/group-maneuvers/DESIGN_Assault_Under_Fire.md), CE-2436)

- 🧪 *(2026-11-22 … 24)* **An LLM in a commander's seat — opposing-force tactics from a language model** ([DESIGN_LLM_Commander](designs/llm-commander/DESIGN_LLM_Commander.md); CE-4676, CE-4679, CE-4680, CE-4681, CE-4683, CE-4684):
  - **The seat:** a platoon commander's orders come from a MIND — scripted, an outside agent over HTTP, or a language model HROT calls itself (an Ollama-style chat server) — while his squads fight with their own AI. ⛔ No ground truth: the mind gets only what was reported to him.
  - **What he is told:** a mission brief (task, purpose, constraints, time), his squads' orders and progress, the objectives his side may see, the enemy as reported (size, activity, equipment, age, confidence), and per named place what sees it, its cover, vehicle reach and walking minutes — places by name, never coordinates; ≈ 1 500–3 500 tokens per call.
  - **What he decides:** ONE schema-checked decision per cycle — assessment, intent, orders (the platoon plays or squad orders, each with start / complete / abort conditions), up to three contingencies HROT carries out on its own between decisions, and when to decide next. A bad decision is repaired once, else the squads CONTINUE; every prompt, answer and applied order is logged beside the recording.
  - **The clock:** held while the model thinks (development), or running on with stale decisions refused (continuous, 📐 L-4).
  - **Proven so far** with a small, fast model (Haiku at the lowest effort, standing in for a local 7–8 B model): it beat Red's defended house (`llm-platoon-attack` ✅ 6/6), two LLM commanders fought each other (`llm-platoon-duel` ✅ 10/10), and a company commander led three AGGREGATED platoons that split into squads on contact, still commanding them after the split (`llm-company-aggregated` ✅ — CE-4685, CE-4686): the company hears its platoons' sightings relayed up with their source and age, sees each platoon's role in a running play, and its base of fire stands at the support position and fires on the objective even after its platoon splits. ⏳ A real local model on a laptop GPU (L-3b) is next.

## 8. Buildings and close quarters

- ✅ *(2026-11-18)* **Glass windows** — a window has a pane: rounds crack it, then break it, and a soldier firing out through a closed window
  shatters it with his first shot; blast and nearby fragments break panes too. ▶ ✅ `gw-shoot-the-window` · ✅ `gw-fire-out`. ⏳ the glass drawn
  on our map (ui, CE-5034) ([DESIGN_Glass_Windows](DESIGN_Glass_Windows.md))
- 🚧 *(2026-11-18)* **Editing baked-in buildings** — removing a building that came with the terrain is built (the merge, CE-5027); ⏳ its doors and
  damage following, making an imported block enterable / adding a door, the toolbar tools and "Save terrain as" (CE-5028…CE-5031)
  ([DESIGN_Terrain_Base_Edits](DESIGN_Terrain_Base_Edits.md))

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

- ✅ *(2026-11-18)* **A vehicle shows its damage** part by part (a destroyed track, a jammed turret, a burning engine deck), knows HOW it was
  killed (a thrown turret is catastrophic), opens its rear ramp for a dismount, and drives blacked out at night under its SOP — see §1b.
  ▶ ✅ `va-tank-kill-kinds` · ✅ `uc-convoy-hit` · ✅ `va-lights-night`
- ✅ *(2026-11-18)* **A move that cannot finish now says so** — an unreachable target fails instead of freezing the unit; a disabled vehicle
  stops running its route; a bike starting from a standstill no longer reports "blocked"; **vehicles slow and yield for animals** on the road
  (CE-3458, CE-9023, CE-3452, CE-3457)

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
- *(added `2026-11-10`)* **Shoot and scoot by the numbers:** an exposure lasts long enough to fire once; each gun declares a real aim time
  and spread (M256 0.3, 2A46 0.4, M242 0.5 mil — estimates); a firing point is never inside the weapon's minimum range; the vehicle turns
  to bring a limited-arc mount onto the target. (R-2086; CE-2430, CE-2431, CE-2432) ▶ `ua-technical-scoot`
- **Possessed weapons fire:** a player in a technical's heavy machine gun hits people; a possessed ZU-23 fires. (CE-1299, CE-2317) ✅

- *(added `2026-11-16`)* **Motorcycles:** bikes ride off-road by surface (open ground, scrub, forest), carry a rider and a pillion who
  fires while riding, and run three plays — a drive-by, a parked bomb on a timer, and a swarm raid from all sides. A **grey-zone dash** past
  drones: riders listen for them (riding dulls their hearing), break hard when one dives, and dismount at the edge.
  ⏳ no live demo yet ([DESIGN_Motorcycle](designs/vehicle-tactics/DESIGN_Motorcycle.md), CE-4621)
  ▶ feature scenarios (R-4277): `ua-moto-drive-by` ✅ 6/6 · `ua-moto-drive-by-grenade` ✅ 6/6 · `ua-moto-parked-bomb` ✅ 9/9 (⚠ a road start can stall, CE-3452 — the play retries) · `ua-moto-swarm-raid` ✅ 11/11 (the pillions engage the post on their own within 60 m, CE-3454 / R-2136) · ⏳ the grey-zone dash, no scenario yet ([DESIGN_Motorcycle](designs/vehicle-tactics/DESIGN_Motorcycle.md), CE-4621)
- *(added `2026-11-17`)* **A vehicle checkpoint:** a squad mans it on an approach road — a stopper at the stop line, two searchers, a machine gun
  and the rest behind at a stand-off worked out from the expected bomb. It judges each vehicle by its TIME to the stand-off: a car that slows on the
  signal is routine; one that ignores it is a threat and is laddered — a warning shot, disabling fire at the engine, then lethal fire, skipping a rung
  when time is short. A stopped car's people are told out, hands up, and they and the car (its boot) are searched, then let go — or held if something
  is found; a released car boards its people and drives on, waved through. A civilian car on any errand STOPS ON ITS OWN at the signal (a car on a ram never
  does). ▶ `cp-civilian-stops` ✅ (its route runs straight through the checkpoint: it stops, is searched, drives on) · `cp-routine-car` ✅ · `cp-vbied` ✅ (a car bomb that will not stop is hit and stopped short) · `cp-compliant-bomber` ✅ (a bomber
  who stops and sets it off as the searchers come — the price of a search) · `cp-moto-attack` ✅ (two men on one bike: a threat, no engine rung)
  ([DESIGN_Checkpoint_And_Ramming](designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md), CE-4622)
- *(added `2026-11-17`)* **Vehicles hurt the people they hit,** by speed and weight; a ramming car does not swerve for its target, and its ram is a
  lethal act once it is in its last 50 m, where it drives the straight line — through a weak barrier, not round it. ▶ `cp-ram-barrier` ✅ (through four barrels, then fired on)

- 🚧 *(added `2026-11-17`)* **Road convoys:** a column of vehicles that never splits — every follower drives the LEAD's actual path at a spacing
  measured along it, never passes the vehicle ahead, stops with it, and the lead slows when the tail falls back. Off the road net it drives our own
  route (off-road → the road in its lane → off-road); SUMO's cars treat the whole column as one obstacle, so none cuts in. An attack or a new order
  breaks the column at once and each vehicle's own drill takes over. ▶ `cv-offroad-join-leave` ✅ · ⏳ `cv-red-lights`, `cv-no-cut-in`,
  `cv-blocked-road`, `cv-ambush`, `cv-mode-1-to-2` · ✅ in SUMO traffic the escort has its beacon and siren (red lights ignored at the front; a queue at a red holds it until green)
  ([DESIGN_Road_Convoy](designs/vehicle-tactics/DESIGN_Road_Convoy.md), CE-4631)

## 9b. Minefields *(🚧 being built now by the `terrain`, `behaviors` and `ui` lanes, `2026-11-16` — [DESIGN_Minefields](designs/minefields/DESIGN_Minefields.md), CE-4624)*

- 🚧 **A minefield is one object:** an outline, a mine type (anti-personnel, anti-tank or mixed), a spacing and a row direction. Thousands of
  mines are worked out from it, the same on every computer, never stored one by one.
- 🚧 **Stepping on a mine:** a man sets off an anti-personnel mine; an anti-tank mine needs a vehicle's weight and hits it from below. A mine
  goes off once. ▶ `mf-strike-and-halt` ✅ (a patrol strikes an unknown field: one blast, a man hurt, the patrol halts, the cells round it suspected) · *(`2026-11-17`)* ⭐ each man now BACKS OUT along his own footprints (MR-5, behaviors — the breadcrumbs are terrain's; ⏳ the scenario's re-run with the back-out is the lane's next report) · `mf-at-vs-vehicle` ❌ (a man crosses an AT field unharmed and a tank sets one off and stops — but the burst does not hurt the tank and hurts the man 35 m off: the AT mine's effects, terrain)
- 🚧 **Each side knows only what it has learned:** a strike makes the area round it SUSPECTED; engineers sweep it slowly and mark the cells
  clear or mined; a marked field is known to everyone who sees it; your own fields are known from the start. The knowledge is saved, replayed
  and shared by the whole side. ▶ `mf-recon-and-mark` ✅ (an engineer traces an unknown field on swept-clear cells only and marks it) · ⏳ `mf-side-knowledge-replay` (waits on the knowledge save entry)
- 🚧 **Routes avoid what the side knows,** and go through only when there is no other way — then the squad halts and calls the engineers.
  ▶ `mf-no-way-round` ✅ (a march halts short of a known field, never crosses it, calls the engineers) · ⏳ `mf-route-around` (waits on route pricing)
- 🚧 **Clearing a lane:** a mine-clearing tank with a plough or flail, or a line charge, cuts a marked lane; the company's breach play covers
  it with fire and smoke, and the squads cross in single file. ▶ ⏳ `mf-clear-lane-and-cross` (waits on the swath clear)

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

- *(added `2026-11-16`)* **A realistic FPV pilot:** the drone dives on what its camera saw a moment ago, with the operator's delay, so a
  vehicle that turns hard at the last second can make it miss — though rarely by more than the warhead's wounding radius. ▶ `ua-drone-shelter` ✅
- *(added `2026-11-16`)* **Shooting drones down:** an anti-drone shotgun, machine guns, cannon, air defence, and any rifle at close range;
  the gunners stand and fire, everyone else takes cover. Drones can be made immune to jamming with one switch. (shown in `cc-battalion`)
- *(added `2026-11-15`)* **A radar SAM site:** a search radar finds aircraft, a tracking radar guides the missiles; aircraft get a radar
  warning when they are painted. ▶ `sam-site` ✅ (3 of 3, the same at 1, 4 and 8 threads) ([DESIGN_Radar_SAM](designs/air-attack/DESIGN_Radar_SAM.md))

- *(added `2026-11-17`, `air` lane — [DESIGN_Air_Combat](designs/air-attack/DESIGN_Air_Combat.md), CE-4647)* **Jets that hold their altitude and
  never fly into the ground:** a cruise leg holds one altitude over the hills; an automatic ground-collision recovery (Auto-GCAS) pulls up from a
  dive, inverted or not, and turns away from a ridge too steep to climb. ▶ `ac-cruise-altitude` ✅ · `ac-gcas-ridge` ✅ · `ac-gcas-inverted` ✅ ·
  `ac-gcas-steep-ridge` ✅ · `ac-map-edge` ✅
- *(added `2026-11-17`)* **Missiles that can be beaten:** a missile boosts, coasts and slows; a pilot drags it out of energy far out and beams it with
  flares or chaff near, or breaks hard at the last moment; a radar missile loses a low, beaming target. A late pilot is hit. ▶ `ac-ir-defeated` ✅ ·
  `ac-ir-hits-late` ✅ · `ac-two-missiles` ✅ · `ac-radar-chaff-beam` ✅ · `ac-sam-dodge` ✅ (a jet in transit beams the SAMs) · `ac-sam-dodge-route` ✅
  (the same on a route — it resumes the route where it was) · `ac-attack-evades` ✅
- *(added `2026-11-17`)* **Dogfights:** an F-16 and a MiG-29 merge, then fight with missiles (only inside the weapon's zone, one at a time) and the
  gun (it leads a turning target); out of weapons or hurt they leave. Skill decides: an Ace beats a Rookie. A combat air patrol finds bandits by radar
  and splits them between the wingmen. Lapses break a stalemate. ▶ `ac-dogfight-1v1` ✅ · `ac-skill-decides` ✅ · `ac-guns-drone` ✅ ·
  `ac-guns-turning` ✅ · `ac-head-on-shot` ✅ · `ac-winchester` ✅ · `ac-cap` ✅ · `ac-stalemate-lapse` ✅ · `ac-dogfight-2v2` ✅ (a gun hit, two
  kills; rounds now meet aircraft beyond a small terrain) · on a `--mode all` cluster the jets fight (merge, all missiles, a turning fight) ⚠ with no
  kill in 311 s — two Veterans out of missiles trade head-on passes (see STATUS, CE-10006) — 21 / 21 `ac-*` pass (`2026-11-17`)

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
| **built, no demo** | weather and night in perception · our vehicles driven by SUMO · ownership transfer · a pilot ejecting from a plane that was shot down · decoy flares you can see · the Stride host (Windows only) |
| **being built** | 🚧 minefields, §9b · 🚧 road convoys (§9, 5 of 6 scenarios) · 🚧 air combat — the 2v2 gun hits and the cluster run (§10) · 🧪 the stochastic unit, §15 (experimental, merged) · 🚧 the items in §16 |
| **designed, not built** | 📐 knowledge of other places per side (a destroyed bridge, a cleared building — the minefield built the shared machinery) · 📐 the fire / flamethrower / napalm, weapon stoppages, physical devices in the loop, thermal drivers beyond the engine / body state, the last burst and the wreck fire (death time) |
| **not built** | the engineer that LAYS a bridge (BL-S3, CE-4693 — the laid bridge itself is built, §dynamic terrain) · bridge damage (walls and whole-building collapse are built, §2a) · knife attacks and closing fast · player fratricide · a toy-gun item (blocks `ua-armed-identify`) · team stretcher carry · a medic who stabilises · helicopter battle positions and pop-up attacks · smoke · counter-battery fire · rappel · wind drift on rounds |
| **known wrong** | see [OVERVIEW_Rules_And_Demos.md §11](OVERVIEW_Rules_And_Demos.md#11-not-built-open-or-known-wrong) and [PLAN_Feature_Films.md §4](PLAN_Feature_Films.md) |

## 14. Authoring, live control and review — what a user can change, and when *(added `2026-11-06`)*

One editing surface serves three modes: **Edit** (building a scenario offline), **Live** (an instructor steering a running exercise)
and **Review** (a recording: replay, after-action review, a review during the run). It is not tied to one program: the editor and the
AI station (CGF) show the same editing, and what is offered for a unit depends on what that unit has, not on which station shows it
(rulings R-4118, R-4119). Selecting anything shows its detail in one **Details** panel, in tabs.

- **Marks:** ✅ built and usable · 🟡 partial (raw fields, type-level only, or some modes) · ⚙ built, reachable only over HTTP / MCP or a
  file · ⏳ not built. **Modes:** ● offered · ○ view only · – not offered.
- **Where it goes next:** the target design for editing — [UX/DESIGN_Editing_UX.md](UX/DESIGN_Editing_UX.md) (step 1, the inventory
  behind this chapter, is written; the target layout follows).

### 14.1 Scenarios

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Start a new scenario from a recipe (the recipe fixes the terrain and the starting units), open, save, save as, save as recipe | ● | ● | – | ✅ File menu; in Live, Save takes a checkpoint |
| Open with a preview: picture, description, terrain, unit count; *Open and go live* | ● | ● | ● | ✅ open picker |
| Scenario parameters (name, type, default) and a **Run Setup** per run (variant, overrides) | ● | ● | – | ✅ |
| Picture and description of a scenario | – | – | – | 🟡 shown, but written outside the editor (the thumbnail script, the file) |
| Change the terrain of an existing scenario | – | – | – | ⏳ |
| Scenario checks (*scenarios as tests*: what must happen, verified against a recorded golden) | ○ | – | – | ⚙ a file per scenario, run from the director's Verify tab or the command line |

### 14.2 Units and force structure

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Place units from a thumbnail gallery by side (friendly, hostile, neutral) — click after click | ● | ● | – | ✅ |
| Move by dragging, turn with the rotate tool, delete | ● | ● | – | ✅ — a change to a unit another station runs is sent to it |
| Change a placed unit's type | – | – | – | ⏳ delete and place again |
| Side, name, health, ammunition | ● | ● | ○ | 🟡 only through the raw component fields |
| Copy, paste, duplicate; edit many selected units at once | – | – | – | ⏳ |
| Order of battle: who commands whom (drag in the ORBAT tree); the structure and scenario views | ● | ● | ○ | ✅ |
| New empty group or headquarters, rename an echelon | – | – | – | ⏳ groups come only from placing a composite type |
| Passengers: embark and disembark | ● | ● | – | ✅ unit menu, ORBAT drop |
| Crew and seat presets, weapon loadout | ○ | – | – | 🟡 set by the unit's type, not per scenario |
| Starting posture (e.g. prone) | – | – | – | ⏳ not saved |
| Heal, damage, resupply, repair a unit | – | – | – | ⏳ the menu items are reserved, no handler |
| Symbol style per unit | – | – | – | 🟡 the NATO symbol is derived from the unit's TYPE (a type can name its role); on / off per layer, not per unit (§7) |
| **Compositions (macros):** save a selection, insert by reference with arguments and a preview, bind fields to parameters | ● | ● | – | ✅ |

### 14.3 AI behaviour, missions and orders

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Give a unit its task, standing procedure (SOP) and rules of engagement, with the behaviour's parameters | ● | ● | ○ | ✅ Details ▸ Brain |
| Character: risk, obedience, nerve, resolve, combat skill | ● | ● | ○ | ✅ Details ▸ Brain ▸ Settings |
| AI skill per side (−2 … +2) | – | ● | – | 🟡 Live only (Game master) or by a rule |
| See why a unit does what it does: doing, why, knows, squad, orders and reports | ○ | ○ | ○ | ✅ Details ▸ Brain |
| Mission plan: a list of tasks, each with its behaviour, parameters and a trigger | ● | ● | ○ | 🟡 trigger typed as text; no phases or phase lines |
| Orders from the map: move, engage, stop, a form with options; designate targets and areas; RTS command mode | – | ● | – | ✅ |
| Squad drills and formations | – | ● | – | 🟡 squad march only; joining a formation is broken |
| Take direct control of a unit (first person, over the shoulder) | – | ● | – | ✅ |
| Draw routes (per-waypoint kind, height, hold), areas, zones | ● | ● | – | ✅ |
| Behaviour graphs (behaviour trees, state machines, blueprint scripts) with hot reload into running units | ● | ● | ○ | ✅ |
| Utility weights and curves, environment queries (EQS) | ○ | ○ | ○ | ⏳ viewable, written in code |
| Debug: breakpoints, pause and step, watch and edit variables | ● | ● | ○ | ✅ |

### 14.4 World and environment

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Scenario start time and time zone offset | ● | ● | ○ | ✅ Details ▸ Scenario (click empty map) |
| Global weather; regional weather drawn as an area | ● | ● | ○ | 🟡 raw fields |
| Terrain zones, obstacles (car, sandbag wall, concrete block, crate), door states | ● | – | – | ✅ / 🟡 doors raw |
| Buildings and interiors | – | – | – | ⚙ terrain files |
| A house's level pad on a slope; ground shapes (level, raise, lower, crater an area); ditches and foxholes | ● | – | – | 📐 designed (§2b; P-3, P-5, P-6) |
| Order the bulldozer to clear a roadblock, a wall or a tree at a point | ● | ● | – | 📐 designed (§2b; B-4) |
| Traffic and pedestrians (density, seed, panic) | – | – | – | ⚙ scenario file only |
| Civilians, protesters and police (placed, given orders and attitude) | ● | ● | – | 🟡 no crowd-level settings |
| Gameplay for players: playable units, deploy roles and kits, spawn areas, respawn delay | ● | – | – | 🟡 kits and spawn facing in raw fields |
| Sides and their relations | – | – | – | ⏳ three fixed sides |
| Minefields, air defence sites, fire lines, freehand drawings, sound cues | – | – | – | ⏳ |

### 14.5 Rules, injects and running the exercise

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Triggers: *when* (with hold, delay, cooldown, repeat) → *do* (orders, missions, messages, objectives, end exercise, insert a composition, AI skill, reporting) | ● | 🟡 | ○ | ✅ Rules window |
| Objectives and measures (take, hold, minimum men, deadline) and the exercise result | ● | ● | ○ | ✅ (`sr-objectives-march` 10/10, CE-3460 closed) |
| Injects: fire a trigger now, disarm, re-arm, run any action by hand (Game master) | – | ● | – | ✅ |
| Rule actions on the world (weather, time of day, traffic, sound) | – | – | – | 🟡 sound: `SetAmbience` (CE-11034); weather, time of day, traffic ⏳ |
| Run modes: Preview, Live, review during the run, after-action review — chosen in one indicator | ● | ● | ● | ✅ |
| Play, pause, step, time scale, stop | ● | ● | ● | ✅ |
| Checkpoint; go live from any moment of a recording (a branch) | – | ● | ● | ✅ |
| Players join, pick a side and a unit, deploy | – | ● | – | ✅ (full join → leave → rejoin cycle under test) |
| See whether a change sent to another station was applied or refused | – | 🟡 | – | 🟡 rules only |
| Undo | 🟡 | 🟡 | – | 🟡 parameters, rules, the AI section, graphs — not unit edits |

### 14.6 Review and filming

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Timeline: seek, play from here, previous / next event, loop a range; click an event to see it in Details | – | ● | ● | ✅ |
| Replay Browser: step, scrub, search by condition, compare components, combine several stations' recordings, export | – | – | ● | ✅ separate windows |
| Bookmarks, notes and annotations on a recording | – | – | – | ⏳ designed (bookmarks) |
| Film director: takes, shots, anchors on events, reels across recordings, render queue, stills | ● | ● | ● | ✅ |

### 14.7 Added since `2026-11-06`

| what you can do | Edit | Live | Review | status |
|---|:-:|:-:|:-:|---|
| Scenarios filed in category folders (showcases · demos by area · tests); a plain name still opens one | ● | ● | ● | ✅ CE-4360, CE-4361 · the picker's folder order ⏳ CE-4397 |
| Scenario presets: a run's parameters saved and applied in place | ● | ● | – | ✅ CE-4317, CE-4326 |
| A scenario's problems in one window, counted on the toolbar, a click goes to each | ● | ○ | – | ✅ CE-4318 |
| *+ Add task* offers only the tasks that fit the unit, in one tree picker | ● | ● | – | ✅ CE-4350, CE-4359 |
| Rules: a gallery to add one, actions read as sentences, *Add rule about this unit…* from the map | ● | ● | – | ✅ CE-4307, CE-4308 |
| Component inspector: nested structs, fixed arrays and per-field editing in one table | ● | ● | ○ | ✅ CE-3374…CE-3404 |
| Create a terrain from OpenStreetMap (§2a) | ● | – | – | ✅ CE-4375, CE-4381 |
| The seat and hat you hold, on the status bar; the menus grey by seat | ● | ● | ● | ✅ CE-4329, CE-4279 |
| Keyboard shortcuts routed to the focused window, listed under View ▸ Settings | ● | ● | ● | ✅ CE-4282, CE-4337 |
| A node extension (a folder of plug-ins every node loads) | ⚙ | ⚙ | ⚙ | ✅ CE-4293 |
| Run the editor and its frame tests with no window on screen (`HROT_HIDDEN_WINDOW=1`) | ⚙ | ⚙ | ⚙ | ✅ CE-4386 |
| *(2026-11-16)* The terrain editor, the user-friendly way: draw walls, fences, hedges, ditches and berms that follow the slope; place a ROW of obstacles as one group; delete or edit several placed objects at once in one undo step | ● | – | – | ✅ CE-4594, CE-5008, CE-5015, CE-5017 ([DESIGN_Terrain_Editor_UX](DESIGN_Terrain_Editor_UX.md)) |
| *(2026-11-16)* Animal areas: what lives there, how many | ● | – | – | ⚠ CE-4623 (the form binds to a temporary component until backend lands) |

## 15. The stochastic unit — whole units fought as numbers *(🧪 experimental, `2026-11-16` — ⭐ MERGED into `coordinator` `2026-11-17` (24 / 24 aggregate scenarios on its branch; the ordinary scenarios checked unchanged); ⏳ a platoon splitting into its SQUADS rather than 30 men is being built — three `exp/agg-*` lanes — [DESIGN_Stochastic_Units_And_Resolution](designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md) §23, CE-4616)*

- 🧪 **One entity for a whole platoon, company or battalion,** its men and vehicles kept as slots (health, ammunition, stress), so a large
  battle costs a fraction of one fought man by man. The SAME orders, reports and SITREPs as physical units.
- 🧪 **Movement, sensing and combat as probabilities:** the unit moves as a shape across the terrain at its slowest member's pace, detects
  by a chance that grows with exposure, and fights with killer–victim tables generated from the real weapon and armour models.
- 🧪 **Changing resolution mid-run:** a stochastic unit can split into real soldiers and vehicles when the fight comes close — keeping every
  identity — and merge back later.

## 16. Being built now *(added `2026-11-17` — every item judged by its scenarios when it lands)*

- ✅ / 🚧 **What a person wears comes from the catalogue** — BUILT and merged `2026-11-17` (the data by backend, the drawing by ui): see §3 —
  Middle-East dress, the Native American warrior's look (worn by backend's warrior 2016 — ONE wardrobe, `OutfitCatalog.NativeWarrior`) and ⭐ the **Afghan people** (civilians 1111–1116, fighters 2019–2021 in perahan tunban,
  waistcoat, pakol, lungee, patu, chadari) ▶ `civ-afghan-village` ✅. ⏳ still to land: `civ-middle-east-market`, `me-fighters-ambush`
  ([DESIGN_Person_Outfits](designs/person-looks/DESIGN_Person_Outfits.md) §10, CE-4648)
- ✅ **Bows and crossbows** — fired on a low arc, SILENT to every AI ear (the same man's rifle stays loud), stuck where they land and in a body,
  shot from horseback; an archery target scores by ring. ▶ ✅ `bw-arrow-arc` · `bw-unheard` · `bw-crossbow` · `bw-stuck-wall` · `bw-stuck-body` ·
  `bw-archery-contest` · `bw-mounted-archers` (7 / 7). ⚠ a team total over the archers reads 0 under the parallel brain phase (CE-3468); the stuck
  arrow's look is ui's ([DESIGN_Bows_And_Crossbows](designs/weapons/DESIGN_Bows_And_Crossbows.md) §10, CE-4649)
- ✅ **Birds you can shoot** — a lone bird falls; a round through a flock brings one down and the rest flee; **ducks**, hunted with the shotgun.
  An explicit order may now fire at an animal (R-3101). ▶ ✅ `an-duck-hunt` ([DESIGN_Animals](designs/animals/DESIGN_Animals.md) §9b)
- ✅ **The rabbit and the hunt** — a rabbit freezes, then bolts in zigzags for the wood; hunting dogs flush it, hunters shoot game only (never a
  person or a dog). ▶ ✅ `an-rabbit-bolts` · ✅ `scenarios/fun/hunting/rabbit-hunt` ([DESIGN_Animals](designs/animals/DESIGN_Animals.md) §9b)
- ✅ *(the fun pack)* **The Native American warrior** — a bow and a thrown tomahawk, on foot or on horseback; a mounted war party CIRCLES its
  target at a gallop, shooting. ▶ ✅ `scenarios/fun/western/war-party` (6 / 6) · ✅ `archer-ambush` (4 / 4). ⚠ every mounted-rider scenario aborts
  under the strict read check (CE-3470) — it passes in the ordinary run ([DESIGN_Bows_And_Crossbows](designs/weapons/DESIGN_Bows_And_Crossbows.md) §10, CE-4650)
- ✅ *(2026-11-18)* **The threaded editor's scoring** — an archery contest's team total and best archer are right with the parallel brains on
  (CE-3468; `bw-archery-contest` gains the two rows)
- 🚧 *(2026-11-18, top priority — behaviors)* **Infantry stop moving after a scenario reload** (the crowd's slots were never released, CE-4665) and
  **the street fight fought in the open** — squads bunched and standing in the street, suppressing with no ammo, dead men still acting
  (CE-4666…CE-4669); decided: a sound heard through your own window is where to LOOK, not an enemy there (D-8), the base of fire's two exits (D-7)
- ✅ *(2026-11-19)* **The civilian's mood is seen on every node** — fear and anger travel as one mood shared with animals (the face, the mood
  dot, an animal's alert look); civilians and animals now keep fear, home and group in the SAME components (CE-4628, CE-2250)
- 🚧 *(2026-11-19)* **The street fight on open ground** — the new `wide-corridor` terrain and `pc-corridor-fight` (a platoon attack across a
  ~30 m corridor between houses) judge it: a man going to his post under fire now reaches cover before he breaks, the base of fire spreads into
  covered posts, a man without a post keeps 5 m from the next, a squad on the move spreads across its line; the 26 scenarios those plays touch
  re-checked with no new failure (D-11…D-13, CE-4666)
- 🚧 *(2026-11-19)* **Minefields: the engineers answer** — an HQ sends engineers to a squad halted by a minefield; they trace and mark it and the
  clearer cuts a lane (▶ ✅ `mf-engineers-answer`; ⏳ `mf-clear-lane-and-cross` — the whole chain — and `mf-knowledge-survives`, waiting on the
  replay seek fix CE-5041)
- 🎉 *(2026-11-19, the fun pack)* **The dragon** — it flies, lands, roars and breathes fire (the flame weapons' jet on a neck turret); its look is
  built; it attacks on its own — strafing passes at men and soft vehicles, a hover at a burnable building (CE-3490); the same pattern flies the flame helicopter, TKB 412 "AH-64 (flame)" ([DESIGN_Dragon](designs/fun/DESIGN_Dragon.md), CE-4634; [DESIGN_Breath_Attack](designs/air-attack/DESIGN_Breath_Attack.md))

