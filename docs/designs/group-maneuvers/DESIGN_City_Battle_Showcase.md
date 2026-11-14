<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — every decision APPROVED by the user in chat (2026-11-13, §10 D-1…D-17, D-R); F-0 (fix the flaky behaviours) first
updated: 2026-11-13 (rev 9: merge — rev 4: §11 the brains items split into buildable rows from measurement (CE-7021…7034); CIV-3 found built) · (rev 8: §4c the skeleton and its variants, CE-6010 / CE-6011 built; §4a platoons of six squads + keys) · (rev 7: §4b lethality settings, CE-6008 built) · (rev 6: §4a the force macros, CE-6009 built) · (rev 5: §11 O-2 CE-6005 built — Peek and Fire §9.10) · (rev 4: §11 O-1 CE-6003 built — as built in Peek and Fire §9.9) · (rev 3: §9 points at the as-built parallel rule, §11 every item a tracker row in Area S, W/B lanes)
current-answer: the whole file — §2 the picture · §3 phases (overlapping) · §3a momentum · §3b the air gate · §3c civilians · §4 forces · §4a the force macros (as built) · §4b lethality settings (as built) · §4c the skeleton + variants (as built) · §5 the breach drill · §6 classes · §7 sequences · §8 who runs what · §9 the parallel rules · §11 the work · §12 testing
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Demo_City_Showcase.md — OWNS the demo-city showcase: a scripted timetable of ~50-entity ACTS (every feature seen once); this one is the opposite axis — scale and autonomy. Its gaps G-1…G-7 still apply to the features both use
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — OWNS the urban battle ladder and the platoon/squad plays this battle's platoons run unchanged
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — OWNS the echelon pattern (orders down as AssignTacticalIntentEvent, reports up); the company commander (§6) is one more level of it
  - docs/DESIGN_Scale_Rig.md — OWNS the scale budget this battle meets (R-4192: 1 000 men + 50 vehicles + 10 aircraft, ≤ 10 ms sim frame) and its measurements
  - docs/DESIGN_Parallel_Simulation.md — OWNS how the simulation reaches that budget; §9 here is the contract every new system of this battle keeps so it can run in parallel
  - docs/DESIGN_Terrain_Synth.md — OWNS the synthetic terrain generator; §11 T-1 adds an authored TOWN PLAN input to it
  - docs/DESIGN_Scenario_Macros.md — OWNS macros; §11 M-1 adds the company / platoon macros the order of battle and the waves are made of
  - docs/designs/weather/DESIGN_Weather_And_Time.md — OWNS visibility; the 5 km fog is a scenario setting of it (§8 S2 already shortens every sensor)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the air assault, the air defence and the strike aircraft used in phase 3
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — OWNS mortars, smoke and the observer chain the recon quadcopter joins
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS room / building clearing the platoons use in phase 4
  - docs/blueprints/RESUME_Coordinator_Destructible_Buildings.md — OWNS the breach / collapse path; the obstacle removal (§11 O-1) shares its scaled distance (BreachRule), not its rows
  - docs/DESIGN_Peek_And_Fire.md — OWNS static obstacles as terrain (§9, R-242…R-244); §9.9 is the AS-BUILT obstacle removal (CE-6003): the blast law, the Health owner's teardown, the blade's seam
-->

# DESIGN — the city battle showcase: ~1 000 men taking and holding a town, decided by their own commanders

## 0. Why

🔒 User, `2026-11-13`: *"i wanted to discuss new scenario demonstrating lots of entities (minimum 1000 soldiers, plus ground vechiles and aircraft)
that are visibly fighting in some suitable terrain, best if they are not scripted much, using their (and their commander's) brain to take decisions
… top level stuff (take over a city, defend a city) can be defined by some scripted approach like mission plans with triggers etc, lower level best
coming from automatic commander entities having their squads and squads having their teams."* — and: *"if it needs some new behaviors to look
realistic or simply good, come with suggestions"*.

## 1. INVENTORY *(`2026-11-13`; three read-only sweeps over `docs/`, `.dev/`, `scenarios/` and the code + direct reads)*

Queries: `scripts/cbm.sh search_graph` `.*(Commander|Platoon|Squad|Company|Echelon|Maneuver|Manoeuvre|Objective|Garrison|Occupy).*` ·
`.*(Weapon|Mortar|Missile|Rocket|Drone|Helicopter|Aircraft|Flight|Turret|Vehicle|Embark|Damage|Destruct|Fortif|Sandbag|AntiAir).*` ·
`.*(Navmesh|NavMesh|Building|Osm|Terrain|Lod|Partition).*` (Class) + grep. Re-run for this document: `.*(Commander|Platoon|Squad|Company|Echelon|Maneuver|Manoeuvre|Objective|Garrison|Occupy).*`
(Class) ⇒ **total 183**, none a company-level commander; `.*(Bulldozer|Dozer|Engineer|Obstacle|Wreck|Jammer|Quadcopter).*` (Class) ⇒ **total 23** —
the obstacle bake / placement / avoidance classes (`StaticObstacleBakeSystem`, `ObstacleTkbTranslator`, `OrcaObstacle` …) and `WreckSpawnSystem`;
⛔ no bulldozer, engineer, jammer or quadcopter class. ⚠ The ai-debug MCP was down: **no live run** confirmed anything below.

| capability | state | evidence |
|---|---|---|
| echelons HQ → platoon → squad → soldier | ✅ built | `PlatoonAttackBehavior.cs:208`, `PlatoonDefendBehavior.cs:198`, `SupportCommandBehavior.cs:69` (the HQ routes support only) |
| **a commander above platoon** | ⛔ **missing** | no company type, no company play (graph + grep); only prose in `DESIGN_Building_Clearing.md` |
| fire teams | data only | `UnitSubordinate.FireTeam` (`Fdp.Core/CommandHierarchy/UnitSubordinate.cs:29`); 0 readers in `Hrot.AI.Behaviors` |
| squad / platoon plays (assault, base of fire, bound, defend posts, fall back, clear room / building) | ✅ built | Urban Battle ladder; `pc-attack-dry` 12/12, `pc-defend-house` |
| scripted top: objectives, triggers that order units, waves (`InsertMacro`) | ✅ built | Scenario Rules S1 / S2 (§00.8–9) |
| mortar + observer, smoke | ✅ mortar · ⛔ smoke rounds | `ua-indirect-fire`; smoke CE-2278 designed |
| AT rockets, guided missiles, armour damage | ✅ built | `ua-rpg-window`, `ua-javelin-lock`, `ArmorModel.cs:27` |
| FPV drone (search, dive), vehicles shelter from it | ✅ built | `DroneImpactSystem.cs:28`, `ua-drone-shelter` |
| bomber / recon quadcopter, drone team, jammer | ⛔ missing | — |
| air defence vs aircraft; helicopter insertion on a DRAWN route | ✅ built | `AirDefenceBehaviors.cs:155`, `RotorcraftModel.cs:233`, `ua-air-assault` |
| wall breach, building collapse (terrain change, sight / fire / paths follow, replicated) | ✅ built | `BreachRule`, `BuildingDamageSystem.cs:49`, `AreaEffectSystem.cs:426` |
| static obstacles (concrete block 8808, car 8806, sandbag wall 8807) — baked as terrain prisms | ✅ built · ⛔ **cannot be destroyed or removed** | `StaticObstacles.cs` (CE-3136 P-7a) |
| a destroyed vehicle as an obstacle | ⛔ missing | a wreck is a presentation effect (`WreckSpawnSystem`, `EffectSystems.cs:406`) |
| bulldozer / engineering vehicle | ⛔ missing | the vehicle catalogue (`TkbEntityTypes.cs`) has none |
| weather visibility shortening every sensor | ✅ built | `PerceptionWeather.cs` (CE-3216 S2), `WeatherConditions.VisibilityM` |
| water surface blocking movement | ⛔ **missing** | `TerrainSurfaceType.Water` is read only for placement (`DESIGN_Terrain_Import_UTM.md` §9.1: "no movement cost, no sight effect") |
| synthetic terrain generator (roads, plots, buildings, heights; deterministic, in git as code) | ✅ built · ⛔ no authored layout | `DESIGN_Terrain_Synth.md`, `scripts/terrain-scale.py` |
| scale at 1 000 men | ⚠ runs, slow | W16: SimHost 37 ms, CGF 86 ms (Scale Rig §7); the speed is the parallel programme's (user: *"assume there is enough speed"*) |
| a scenario with more than one AI platoon per side | ⛔ none found | largest: `perf-stress` 992 men, squads only, no command above |

## 2. The picture — the battle at a glance

![The battle at a glance](city-battle-overview.svg)

*What the picture shows that prose hid:* the battle is THREE bands the attack must cross — the open fields (where both sides' drones make a dead
zone), the ring road (the defenders' main line, roadblocked), the old town (the inner ring, the town hall) with the river BEHIND it from the attacker's side, the hill and the reserve beyond. Each phase line is a
scripted trigger; everything between two lines is a company commander's problem. The air assault skips the bands and lands on the objective.

## 3. The phases — the scripted top, OVERLAPPING, and nothing below it

🔒 User, `2026-11-13`: *"it is a showcase scenario, for visual effect, showing lots of entities doing something interesting, not just sitting in
cover/firing from it and never advancing. It needs to be dynamic."* ⇒ ⭐ the phases OVERLAP: at any minute at least two areas of the town are busy.

```mermaid
gantt
  title The battle's timeline (minutes, ≈ 40 min) — at least two things at once
  dateFormat m
  axisFormat %M
  section Town
  civilians panic, flee, shelter               :civ, 0, 12m
  section Shaping
  drones up, mortars register, recon finds AA  :sh, 0, 8m
  section Ground vs air defence
  kill the hill's AA (mortars, ATGM, FPV)      :aa, 4, 12m
  section A Coy (north)
  cross the fields, breach PL B                :a1, 5, 10m
  grind to PL C                                :a2, 15, 15m
  section B Coy (south)
  cross the fields, breach PL B                :b1, 8, 10m
  grind to PL C                                :b2, 18, 14m
  section Air
  air gate opens → strikes, then insertion     :air, 16, 8m
  section Defender
  outposts withdraw, shoot and move            :d1, 5, 12m
  local counterattacks                         :d2, 12, 18m
  reserve counterattack over the bridges       :d3, 28, 8m
  section Waves
  2nd wave (macro)                             :w, 20, 10m
```
*What the picture shows that prose hid:* no phase waits for another to FINISH — the companies, the air-defence fight, the civilians and the defenders'
counterattacks run side by side; the timings are the TRIGGERS' fallbacks (§3a), the real start is whichever condition comes first.

| phase | starts when (trigger — the earlier of the two) | the battalion script orders (intent only) | what the AI then does by itself |
|---|---|---|---|
| **0 alarm** | t = 0 | — | civilians panic at the first shots: run from the shooting, hide in any building whose door opens, vanish inside; the fear spreads street by street (§3c) |
| **1 shaping** | t = 0 | drone teams on; mortars register on PL A; **find and kill the hill's air defence** | recon quadcopters find targets for the mortars, FPVs hunt in the fields — the dead zone forms; the AA fight starts (§3b) |
| **2 breach** | t = 5 min (A Coy) / 8 min (B Coy) | A Coy, B Coy: attack to PL B (ring road), **deadline 12 min** | mounted dash to the edge, dismount, carriers overwatch; outposts withdraw; roadblocks removed (§5) |
| **3 air** | **the air gate opens** (§3b) — never on a timer | strikes on what still holds; then the air assault platoon inserts at the town hall | UH-60s fly a route away from any AA still reported; the landed platoon holds the town hall |
| **4 urban grind** | a company reaches PL B | that company: attack to PL C, deadline 15 min · the 2nd wave macro enters | block-by-block; the defenders shoot and move, counterattack locally |
| **5 counterattack** | the attacker holds the square, or t = 28 min | defender reserve: counterattack the square | the reserve crosses the bridges; the attackers defend what they took |
| **6 end** | every objective held ≥ 3 min · or one side < 30 % · or t = 45 min | end, score | — |
| ✚ **pressure** | any sector with no contact for 3 min | its company: advance to the next line | keeps a quiet sector from going quiet |

⭐ The script writes **intent only** (sector, objective, phase line, deadline, fire plan, wave, end) through the built objectives and triggers (S1 / S2 +
`InsertMacro`); ⛔ it never moves a platoon.

## 3a. Momentum — what makes them MOVE *(the review, `2026-11-13`; 🔒 user: "definitely add those")*

⛔ The flaw this section fixes: every built incentive points to COVER — defenders hold posts, a soldier under fire picks Take cover / Hold, the drone
dead zone punishes the open. Without a reason to move, base of fire versus a house trades fire forever.

| mechanism | who | what it does |
|---|---|---|
| **tempo deadline → escalate** | company commander (attack) | every order carries a deadline; at 50 % of it with no progress: smoke + fires + commit the reserve; at 100 %: switch axis or bypass; ⛔ never "wait" |
| **shoot and move** | defending squad / team | after N bursts or when a position is spotted (fire received): relocate through back yards, houses, roofs to the next post — the posts are a SET per squad, not one |
| **outposts withdraw under fire** | defender outpost | at contact with a superior force: fire, then fall back along a planned route to the main line (CE-3361's fall-back, must be seen live first) |
| **technicals shoot and scoot** | defender vehicle | dash to a firing point, a burst, back into cover, next point (`ua-technical-scoot`, paused CE-2232 — fix first) |
| **local counterattack** | company commander (defence) | a lost block with a weak attacker in it: the company reserve retakes it (CC-3, promoted to must-have) |
| **suppression wears off; leaders rally** | squad leader | a pinned squad gets up again: suppression decays, the leader orders the bound; ⚠ a stress model that can pin a squad FOREVER is unmeasured — a rail first |
| **morale and rout** (AQ101 E-3 / E-4) | platoon / squad | a broken platoon runs back through the streets — the most dynamic picture a battle makes, and how a position ends without everyone dying. ⭐ promoted to MUST-HAVE · ✅ RULED `2026-11-14` (R-4227, [AQ101 §R](../../blueprints/Architect_Question_101_Shelling_Fear_And_Shelled_Areas.md)): stress is morale; a burst is fire (CE-4584); a broken man counts as lost to the platoon's fall-back and abort (CE-7015); the shelled area CE-4583 |
| **casualty evacuation** | squad | a casualty is dragged / carried back (the carry exists, Map 3-D held figures) — losses become movement |
| **waves** | battalion script | the 2nd wave (macro) keeps the attack fed |
| **pressure trigger** | battalion script | a sector quiet for 3 min gets "advance to the next line" (§3) |

⭐ **"Dynamic" is MEASURED, not judged** — the full run's pass condition (§12): the share of men who moved > 50 m in every 5 minutes, contacts per
minute, each phase line reached by its deadline.

## 3b. The air gate — no aircraft into a live air defence *(🔒 user, `2026-11-13`)*

🔒 *"air attack with lots of anti aircraft missiles on the ground is a suicide mission as well … the defense needs killing first before extremely
expensive aircraft is let flying there, no?"* — ⭐ yes: the air defence is killed FROM THE GROUND first; aircraft fly only when the gate opens.

```mermaid
sequenceDiagram
  participant R as Recon quadcopter / observers
  participant HQ as SupportCommand (attacker HQ)
  participant M as Mortars · Javelin teams · FPV teams
  participant A as Aircraft (A-10, AH-64, UH-60)
  R->>HQ: spot reports: ZU-23 at the hill, a Stinger team at the church tower
  HQ->>HQ: AIR THREAT PICTURE: known AA within reach of the target or the route?
  HQ->>M: suppress / destroy the AA (fire missions, drone strikes)
  M-->>HQ: BDA — ZU-23 destroyed · the Stinger team not seen for 5 min
  A->>HQ: a company's air request (strike, insertion)
  alt a known AA still live within reach
    HQ-->>A: refused — air threat · the request becomes an AA-kill task
  else the gate is open
    HQ->>A: approved — route planned away from any AA still reported
  end
```
*What the picture shows that prose hid:* the gate lives in ONE place — the HQ that already routes every support request (`SupportCommandBehavior`) —
so no aircraft and no company has to remember it; and a refused air request is not lost, it turns into the ground task that opens the gate.
⚠ A HIDDEN MANPADS team (never reported) is the residual risk — the possible shoot-down is the drama (Demo City Showcase G-1, the pilot ejecting).

## 3c. Civilians — the town is alive when the attack starts, and then it PANICS *(🔒 user, `2026-11-13`)*

⭐ **As built `2026-11-13` (showcase-brains, CE-7030 / CE-7031):** watching and boldness are civilian params — `WatchMaxMetres` (fighting heard 150 m … it ⇒ Watch: stop, look, `FilmShare` 0.4 film), `BoldShare` (a bold man watches down to 50 m and is alarmed at twice the fear); **0 = off** (lane default R-7003: watching and boldness are opt-in civilian params, so no existing civilian scenario changes) — the showcase's civilian macro sets them. ⚠ A watcher does not turn to face the fighting (no facing request for a standing man).

🔒 *"civilians panicking and evacuating when attack starts is a good idea, add it please"* — then: *"evacuation can hardly be organized under attack"* ·
*"its sheer panic i guess"* · *"people close to the warfight should simply start running away or trying to hide"* · *"exactly people are curious and start fleeing only if the danger is close (and even then some cracy filmers stay)"*. ⇒ ⭐ **no organised evacuation**: sheer panic — run from the shooting, hide, vanish (R-4035: a panicking civilian hides in a
building whose door can be opened and disappears after a while inside; never flees indefinitely).

| who | what | state |
|---|---|---|
| ≈ 200 civilians in the west belt and the old town (+ a few cars) | **close to the fighting** (shots, explosions near them): **panic** — run AWAY from the danger, or hide in the nearest building whose door opens, disappear inside after a while · **further away**: alarmed (stop, look, keep away), until the fighting comes close | ✅ built (`CivilianHsm` CIV-1 Alarmed, CIV-2 panic / shelter, Civilians §5d–§5e) |
| civilians who hear the fighting **far away** | **curious**: stop and watch, some walk to a corner / a balcony with a view and film with a phone — until the danger comes close (their fear crosses the alarm threshold), then panic | ⛔ new: a **Watch** mode in `CivilianHsm`, entered from Routine when violence is heard but fear stays below `AlarmAt` (today a distant fight leaves him in Routine; curiosity is drawn only by a casualty within 80 m, CIV-3) |
| **a few "crazy filmers"** (≈ 5 %) | stay and film even close to the fighting — flee only when hurt or when an explosion lands very close | ⛔ new: a per-civilian **boldness** trait (a scenario-set share) that raises his alarm threshold; ⚠ a phone held up at 150 m can be mistaken for a weapon by a soldier (`ua-armed-identify`) — the engine holds fire on a Neutral (AW-20), so it is tension, not a massacre |
| a crowd in a street or the square | **fear spreads**: one panics, the people around him panic — the panic travels through the town as a wave, ahead of the fighting | ✅ CIV-3 BUILT (`CrowdSystem.cs:400-421`, measured 2026-11-13 — the Civilians design §5f still says ready-to-build) — ⭐ the one civilian piece to build |
| a driver near the fighting | panics: stops, leaves the car, runs (the abandoned car stays where it stopped — street clutter, not an obstacle) | ✅ BUILT `2026-11-13` (CE-7011): `TrafficPanicSystem` gives a SUMO car up as it gives a HIT car up (K7 / AQ93 §8.7 G4: our stopped car, its driver out in panic) when a shot or a burst is within `DriverPanicMetres` 25 m — no damage, at most 2 cars a frame, each once (lane default R-7005) |
| a civilian in a soldier's line of fire | the shot is held (AW-20) | ✅ built |

## 4. The forces *(≈ 1 050 men, ≈ 50 vehicles, ≈ 10 aircraft — the Scale Rig budget, R-4192; more later if the speed allows)*

| | attacker (≈ 600) | defender (≈ 450) |
|---|---|---|
| manoeuvre | 2 mechanised companies (each: HQ + 3 platoons + 4 IFVs + 2 tanks) · 1 air assault platoon · a 2nd-wave company (macro, phase 4) | 3 companies in the buildings (each: HQ + 3 platoons, technicals) · 1 reserve company (counterattack) |
| support | mortar section · Javelin teams · 2 drone teams · 1 jammer vehicle · 1 engineer squad + 1 **bulldozer** · on call: A-10, AH-64 | mortar section · RPG / NLAW teams · 2 drone teams · ZU-23 + Stinger / Igla on the hill |
| air | 4 UH-60 (insertion), A-10, AH-64 — **held until the air gate opens** (§3b) | — (ground counterattack instead) |
| positions | — | **prepared from the start** by the scenario: sandbag posts, roadblocks (blocks + cars), MG nests, occupied buildings; no "prepare" behaviour |
| fire teams | **groups inside a squad** (squad leader addresses team A / team B); no team brain | same |
| company structure | HQ + 3 platoons + a **company support element** that holds the attachments (tanks, engineers, bulldozer, drone team) — ⚠ a commander's picture holds **6 subordinates** (`CommandPicture.MaxSubordinates`, `CommandComponents.cs:116`, sized for a platoon); a company with every attachment as a direct subordinate would overflow it | same (the reserve platoon, technicals and the AT teams in the support element) |
| civilians | — | ≈ 200 in the west belt and the old town + a few cars (§3c) |

Visibility: **5 km fog**, daylight only. ⭐ **Lethality is a set of scenario settings** — drone count and battery, weapon accuracy, armour —
tuned once so the field crossing costs, but does not erase, a company (§3a).

⭐⭐ **Recording is ON — always** (🔒 user: *"recording is a mandatory feature as this shows the performance of the system WHILE recording is ON"*): the
showcase demonstrates the engine's speed WITH the recorder running; the budget (R-4192) is met with it on. ⚠ The recorder cost 18.7 ms at this size
(Scale Rig §5, Evaluation Cost §11) — a requirement on the parallel / performance programme, not an optional extra.

⭐ **Repeatable, and not always the same** — the parallel programme's **deterministic mode** (R-4207) makes a seed repeat exactly (the best
seeds are the ones filmed); **scenario variants** (the existing `$variant` / variant forms, Scenario Macros §17) vary what each run starts from:
the defender layout, the roadblock set, where the air defence and the MANPADS teams stand, the civilians' density.

## 4a. The force macros — as built *(`CE-6009`, showcase-world, `2026-11-13`)*

```mermaid
graph TD
  MC["macros/city-battle/mech-company (side)"] --> P1["rifle-platoon instance '1st Platoon' (Args.side ← side)"]
  MC --> P2["rifle-platoon '2nd Platoon'"]
  MC --> P3["rifle-platoon '3rd Platoon'"]
  MC --> SUP["support element: Tank Section 301 → 2 × M1 · 4 × Bradley"]
  MC -.->|"⏳ EMPTY until CE-7002"| CC["company commander"]
  P1 --> PL["platoon 305 @Platoon (name)"] --> SQ["6 × squad 302 (CE-6010; was 3)"] --> MEN["6 men each: leader, MG, grenadier 2004, 3 riflemen 2002"]
  DC["macros/city-battle/defender-company (side = Hostile)"] --> MP["3 × militia-platoon (2003 fighters)"]
  DC --> TEC["2 × technical 104"]
  TRG["a Trigger: InsertMacro (2nd wave, reserve)"] -->|"EntityCreationRequest → expander → ELM"| P1
```
*What the picture shows that prose hid:* every unit of the battle is one of FOUR content files on the existing machinery — the company is
three NESTED platoon instances whose `side` arg is bound to the company's (`MacroInstance.Args.side ← side`, measured: it reaches every man), and
the company commander's place is deliberately empty. No code.

| decision | why |
|---|---|
| **kit = which macro** (`rifle-platoon` 2002 soldiers · `militia-platoon` 2003 fighters); **side** and **name** are params; the **entry point** is the instance's anchor (`at` of `InsertMacro`, or where the scenario places it) | ⭐ params bind FLAT fields only (§4.0 of Scenario Macros); a TKB type per role as a param is not a proven binding — a second file is |
| **no company commander entity** — the platoons are top-level until CE-7002 | ⛔ a placeholder would be a platoon (305) commanding platoons: its plays (`PlatoonAttack`…) expect squads — wrong orders, not an empty slot. CE-7002 adds its type and sets the platoons' `UnitSubordinate` in these files |
| **the support element holds the attachments** (a tank section 301 for the tanks; the IFVs top-level) | `CommandPicture.MaxSubordinates = 6` (§4): the company commander will see 3 platoons + the tank section + room |
| ⛔ SUPERSEDED by CE-6010 (§4c): ~~three squads abreast, ≈ 22 entities a platoon~~ — now SIX squads of six in two rows of three, 15 m apart, platoons 60 m apart; each platoon takes a `key` (its RefKey) and a company passes `p1..p3` down | ≈ 43 entities a platoon, ≈ 139 a mechanised company (`CityBattleMacrosTests`) |

⚠ **Found, filed `CE-6015`:** the command hierarchy (`UnitSubordinate`) does NOT reach SimHost or the Player for ANY platoon — a scenario-loaded
one included (📐 `pc-attack-dry` in the in-process twin at 8 s: 15 of 15 wired on CGF, 0 on SimHost and the Player). The brains run on CGF, so
the battle plays; what reads the hierarchy elsewhere (the command display on the Player, `DESIGN_Command_Display.md` §7) sees none.
Rails: `Hrot.SimHost.Tests.CityBattleMacrosTests` (M1–M3) · `Hrot.ClusterRunner.Integration.Tests.MacroPlatoonScenarioTests` on
`scenarios/showcases/city-battle/features/macro-platoon`.

## 4b. Lethality as scenario settings — as built *(`CE-6008`, showcase-world, `2026-11-13`)*

```mermaid
graph TD
  T["a scenario Trigger (ScenarioTime 0): SetSkillAmplifier {side, tiers −2…+2}"] -->|"CGF: SkillVerbs → World Environment"| A["SkillAmplifier (per side)"]
  A -->|"ProficiencyFillSystem, every man with a brain"| P["CombatProficiency.SideTiers (replicated)"]
  P -->|"the node resolving the shot"| S["HitModel.Sigma × ProficiencyOf.SpreadFactor (2.5 … 1.0 … 0.65)"]
  S --> H["hits per round"]
  D["drone count"] -->|"content: entities / macros / variants"| C["the scenario file"]
  B["drone battery: FlightParams.EnduranceSeconds"]:::gap -.->|"TKB only (NoScenario) — CE-6016"| X["no scenario knob"]
  R["armour: CombatPlatformDef (TKB)"]:::gap -.->|"TKB only; R-4018: a scenario names no TKB — CE-6016"| X
  classDef gap stroke:#c00,stroke-dasharray:4
```
*What the picture shows that prose hid:* of the four knobs, ONE (accuracy) already IS a scenario setting end to end and one (drone count) is just
content; the other two have NO path from a scenario file today — the dashed edges.

| knob | as built | why |
|---|---|---|
| **accuracy** | ✅ a 0 s trigger with `SetSkillAmplifier` per side — no code. Feature `features/lethality-accuracy`: two identical lanes, Friend +2 vs Hostile −2 | ⭐ the existing mechanism (SR-100); it moves a man's proficiency by preset steps, so the spread goes ×0.65 … ×2.5 and every other proficiency effect (exposure, delay) follows |
| **drone count** | ✅ content — the number of drone entities a scenario / macro / variant places | nothing to expose |
| **battery, armour** | ⛔ NOT exposed — filed `CE-6016` | both live only in the TKB (`FlightParams` is `NoScenario`; `CombatTkb.PlatformOf`); a scenario-level scale needs a NEW recorded-unmanaged World Environment component (lean: `LethalitySettings { DroneEnduranceScale, ArmourScale }`) — a new id in `GlobalComponentIds.cs`, which the parallel programme is editing ⇒ held for CE-6012's merge |

Rail: `Hrot.ClusterRunner.Integration.Tests.LethalityAccuracyScenarioTests`.

## 4c. The skeleton and its variants — as built *(`CE-6010`, `CE-6011`, showcase-world, `2026-11-13`)*

```mermaid
graph TD
  GEN["scripts/city_battle_scenario.py (+ city_battle_macros.py)"] --> SCN["scenarios/showcases/city-battle/city-battle/scenario.json"]
  SCN --> ATK["attacker: A Coy, B Coy = mech-company (p1..p3 = A1..A3 / B1..B3) · air-assault rifle-platoon + 4 UH-60 · A-10, AH-64 parked · mortars, drones, Javelins, engineers + bulldozer, HQ + jammer HMMWVs"]
  SCN --> DEF["defender: 1/2/3 Coy + reserve = defender-company (D11.. / DR1..) — Positions: 3 places each · hill: mortars, ZU-23, 2 Stinger, 2 Igla — Positions"]
  SCN --> RB["3 × roadblock macro on the ring-road crossings — Presence 0.6 each"]
  SCN --> CIV["200 civilians on the west belt + old town streets — Presence 0.7 each (Civilian 1 always)"]
  SCN --> TOP["intent only: PL A–D, 2 sectors, 3 objectives, 11 triggers"]
  TOP -->|"t = 0: Order '@D11' PlatoonDefend at '@D11' (where the variant put it)"| DEF
  TOP -->|"5 / 8 min: Order '@A1..3' / '@B1..3' PlatoonAttack PL B"| ATK
  TOP -->|"reached PL B OR 15 / 18 min: PlatoonAttack PL C"| ATK
  TOP -->|"20 min: InsertMacro mech-company (W1..W3) + Order"| ATK
  TOP -->|"town hall held OR 28 min: reserve PlatoonAttack the square · held OR 45 min: EndExercise"| DEF
```
*What the picture shows that prose hid:* every order reaches a platoon by a KEY the company macro passes down (`p1` → `MacroInstance.Args.key` → the
platoon's `RefKey`) — a GUID of a macro child cannot be named in the file (the load refuses it, measured) — and the defenders defend WHERE THEY STAND
(`Position: "@D11"`), so a variant that moves a company needs no second trigger.

| decision | why |
|---|---|
| ⭐ **EVERY place in ONE table, named by role** (`ANCHORS` in `scripts/city_battle_scenario.py`: `attack-A`, `defence-1`, `roadblock-N`, `square`, `hill`, `wave-entry` …; `ALTERNATIVES`, `PHASE_LINES`, `SECTORS`, `CIVILIAN_AREAS` beside it) | ⏳ the town is being REDRAWN (radial roads, no ring road — coordinator, `2026-11-13`): re-anchoring the battle edits that block only, then regenerates. ⚠ The current anchors are the OLD town's (ring-road crossings) |
| **its own leaf folder** `showcases/city-battle/city-battle/` (not `showcases/city-battle/scenario.json`) | ⛔ measured: a folder holding a `scenario.json` HIDES the `features/` beside it — `ScenarioFolderIndex` never enters a scenario folder, so `macro-platoon` stopped resolving |
| **a platoon is SIX squads of six** (36 men; was three) | the order of battle at ≈ 1 050 needs it — the squad stays at six (a squad leader's `CommandPicture` holds 6, `CommandComponents.cs:116`); six squads is the platoon's own limit |
| **the scripted top: "the earlier of two"** = `Compound Or [ObjectiveStatus Achieved, ScenarioTime]` | the existing predicates express it (no new mechanism); ⚠ "one side < 30 %" and "a quiet sector for 3 min" (pressure) have no predicate — CE-6018 |
| **the roadblock is a macro** (`city-battle/roadblock`: 2 blocks, 2 cars, 2 MG nests behind sandbags, an AT team) | one `Presence` roll keeps or drops a crossing's WHOLE roadblock |
| **recording** = the cluster default (`ClusterMaster.RecordLiveRuns = true`) | ⛔ a scenario cannot declare it — it is a run setting (Scale Rig Q-3); the rail asserts it is ON and the files grow |

📐 **Measured** (in-process, four hosts in one thread, Debug, 4-core VM, nothing else running): 6 661 entities on CGF (1 840 of them doors), 274 + 462
soldiers + 200 civilians, 32 vehicles, 14 aircraft (8 FPVs); OperatingLive 9.5 s after boot; every node complete at sim ≈ 32 s; **≈ 370 ms per frame**
(all four hosts) in the steady minute; recording 6.5 MB (SimHost) + 10.2 MB (CGF) per minute. ⚠ 844 soldiers with the 2nd wave, not 1 050.
Rails: `CityBattleSkeletonTests` (CE-6010, CE-6011 twin) · `Hrot.SimHost.Tests.CityBattleVariantsTests` (V1, V2).

## 5. The roadblock breach drill — removal is mandatory

![The breach drill](city-battle-breach-drill.svg)

*What the picture shows that prose hid:* a roadblock is only ever passed by **removing** it (🔒 user: *"there is nothing like breach through a road
block - removal mandatory (even as disappearing)"*) — the drill around it exists only to get the remover there alive. Three removers, one effect:
the obstacle's prism leaves the terrain world. The bypass (infantry through breached walls) is the alternative ROUTE, not a removal.

## 6. Classes

```mermaid
classDiagram
  direction LR
  class BattalionScript {
    <<existing: objectives + triggers + InsertMacro>>
    phase lines · sectors · fire plan · waves · end
  }
  class CompanyCommander {
    <<NEW, one entity per company, own brain>>
    CompanyAttack HSM · CompanyDefend HSM
    reads its platoons from the static copy
    orders platoons · requests fires, smoke, bulldozer, drones
  }
  class PlatoonPlays { <<existing>> PlatoonAttack · PlatoonDefend · BasicPlatoonSop }
  class SquadPlays { <<existing>> SquadDefend · ClearBuilding · BoundingOverwatch … }
  class Soldier { <<existing>> CombatPosture utility · peek and fire · stress }
  class SupportCommand { <<existing HQ>> routes FireSupport · Reinforce · + AIR GATE (NEW): the air threat picture refuses air until the AA is killed }
  class Civilian { <<existing CivilianHsm>> panic · flee · shelter · vanish inside · + fear spreads (CIV-3, BUILT — CrowdSystem) }
  class DroneTeam { <<NEW>> operator soldier · drone stock · battery cycle · picks targets from the company picture }
  class Quadcopter { <<NEW kinds>> FPV (existing) · Bomber (drop) · Recon (spots) }
  class Jammer { <<NEW>> a vehicle / post: a bubble that breaks drone links }
  class Bulldozer { <<NEW vehicle>> shape kit + armour profile · ClearObstacle behaviour }
  class Obstacle { <<existing StaticObstacle>> + the existing Health (CE-6003) · destroyed at 0 }
  class WreckObstacle { <<NEW>> a heavy vehicle's hulk becomes an Obstacle }
  class UnitMacros { <<NEW content>> company · platoon macros (side, kit, entry) }
  BattalionScript --> CompanyCommander : orders (events)
  CompanyCommander --> PlatoonPlays : orders (AssignTacticalIntentEvent)
  PlatoonPlays --> SquadPlays
  SquadPlays --> Soldier
  CompanyCommander ..> SupportCommand : requests
  CompanyCommander ..> Bulldozer : clear obstacle
  CompanyCommander ..> DroneTeam : search sector
  DroneTeam --> Quadcopter : launches
  Jammer ..> Quadcopter : breaks link
  Bulldozer ..> Obstacle : removes
  WreckObstacle --|> Obstacle
  BattalionScript ..> UnitMacros : InsertMacro (waves)
  SupportCommand ..> CompanyCommander : air refused → AA-kill task
```
*What the picture shows that prose hid:* only ONE new echelon (the company) sits in the existing chain; everything else new is a leaf the company
commander calls (bulldozer, drone team) or a world rule (obstacle health, wrecks). The platoon, squad and soldier levels are reused unchanged.

## 6a. The company as built *(showcase-brains, `2026-11-13`; CE-7021…CE-7024, CE-7005 — obligation ⑤)*

```mermaid
classDiagram
  direction LR
  class Unit_InfantryCompany {
    <<TKB 306, NEW>>
    3 x Unit_InfantryPlatoon · echelon Company
    no standing orders
  }
  class CompanyAttackBehavior {
    <<NEW HSM CompanyAttack>>
    CPlan · CSupport · CAssault · CNext · CConsolidate · CWon
    CBreaking · CBroke · watch: CRelieve · CAbort
    Tempo(): 0.5 x fires · 1 x reserve · 1.5 x bypass
  }
  class CompanyDefendBehavior {
    <<NEW HSM CompanyDefend>>
    KPlan · KHold · KFallBack · watch: KReinforce
  }
  class PlatoonAttackBehavior { <<existing>> }
  class PlatoonDefendBehavior { <<existing>> Ready entry now LineReady: reports PROGRESS Set up }
  class CommandOrders { <<existing>> Assign · Stands · StillRunning }
  class ReportQueue { <<existing>> REQUEST FireSupport up }
  Unit_InfantryCompany ..> CompanyAttackBehavior : runs
  Unit_InfantryCompany ..> CompanyDefendBehavior : runs
  CompanyAttackBehavior --> CommandOrders : PlatoonAttack · PlatoonDefend
  CompanyDefendBehavior --> CommandOrders : PlatoonDefend
  CompanyAttackBehavior --> ReportQueue : tempo fires
  CommandOrders ..> PlatoonAttackBehavior
  CommandOrders ..> PlatoonDefendBehavior
```
*What the picture shows that prose hid:* the company adds NO new order type below it — it speaks only the two platoon plays that already exist, and
the one change below it is that a platoon's defence now reports Set when its line is ready (the base of fire's signal; a defence never ends, so the
task-end Set never came).

| as built | deviation from §6 / the handoff, and why |
|---|---|
| roles by slot: 0 assault, 1 base of fire, 2 reserve, 3+ keep their own task | — |
| objectives = four fixed params `Objective1…4` | ⭐ lane default R-7000: the intent payload is a flat struct like every platoon play's |
| the company has NO standing orders | ⭐ lane default R-7000 (the handoff said `BasicCompanySop`): its work is its task; the platoons' SOP reacts below it |
| the base of fire 250 m short of the objective, the reserve 400 m; each next objective measured from the previous one | — |
| tempo: ½ deadline ⇒ REQUEST FireSupport on the objective · 1× ⇒ the reserve attacks beside the assault · 1.5× ⇒ bypass (never the last) | the smoke round comes with CE-7025; until then the request is plain fire support |
| defend: the reserve goes in FIRST; the company falls back only when half the blocks fail with the reserve already in (the block it reinforced counts as held) | ⭐ measured on the bench: with two blocks one failure is already "half", so the first rule fell back before the reserve could act |
| ⭐ CE-7004 — the LOCAL COUNTERATTACK (`CompanyDefend` region 1, `KCounter`, checked before `KReinforce`): a block LOST (its platoon gone or `TaskFailed`) with the reserve free and ≥ 70 % (Green / Amber) ⇒ the reserve `PlatoonAttack`s the block's point; its Set ⇒ it defends the block; once per defence. A block merely failing (Black, falling back) is reinforced as before | ⭐ lane default R-7008: "lost" (gone / gave it up) ≠ "failing" (Black / falling back), so the handoff's counterattack and the existing reinforcement each keep their trigger |
| ⭐ CE-7007: shoot and move is opt-in below the company (`ShootAndMoveSeconds`, 0 = off) and on by default at 25 s in `CompanyDefend` — the company hands it to its platoons, they to their squads | ⭐ lane default R-7002: shoot and move is opt-in below the company, so every existing defence scenario keeps its behaviour |
| the puppet bench (CE-7022) = the drills' existing one-frame `Platoon` fixture with its members as platoons (`SquadManeuverTests.Company.cs`) | no new harness was needed: the fixture never cared what echelon its members were |

## 6b. Smoke from the guns as built *(showcase-brains, `2026-11-13`; CE-7025, CE-7003 — obligation ⑤)*

```mermaid
sequenceDiagram
  participant C as CompanyAttack (tempo)
  participant HQ as SupportCommand
  participant B as ArtilleryUnitStrike (battery)
  participant G as gun: FireAtPoint executor
  C->>HQ: REQUEST FireSupport · Equipment Smoke flag (half the deadline)
  HQ->>B: Strike · Smoke true (never the helicopter: smoke is the guns' only)
  B->>G: FireAtPoint · round 1 (per gun, the member order)
  G->>G: AmmoChoice.IndexOfKind(Smoke) · LoadIndex = one reload (Reloading)
  G-->>G: fires the smoke bomb: an AreaEffectCloud where it lands (existing)
```
*What the picture shows that prose hid:* nothing NEW carries the round on the wire — the request rides the report's existing equipment byte
(`ElementEquipment.Smoke`), the gun's params the last padding byte of its 32-byte channel params; the gun switches rounds with the weapon brain's
own one-reload rule, so a mission that does not ask for smoke reloads HE the same way.

| as built | why |
|---|---|
| smoke bombs 60 / 81 / 120 mm = TKB 6047 / 6048 / 6049: cloud 15 / 20 / 25 m, 90 s, the HE charges | Indirect Fire IF-5's round, starter values |
| each mortar mount carries a loadout: its HE first, then a third as many smoke bombs | a plain mission still fires entry 0 (HE) — unchanged |
| `StrikeOrder.Smoke` → battery `Round` → `MemberOrder.Round` → `FireAtPoint` `round` → the executor loads it | one field per hop, the existing chain |
| a smoke REQUEST is the `ElementEquipment.Smoke` flag | ⭐ lane default R-7001: a request's `Count` is already 1 in production, so it could not carry the round |

## 6c. The bulldozer's `ClearObstacle` as built *(showcase-brains, `2026-11-13`; CE-7012 — obligation ⑤)*

```mermaid
sequenceDiagram
  participant D as ClearObstacle (dozer brain, parallel phase)
  participant L as LocomotionChannel (its own)
  participant Bus as bus → batch buffer
  participant O as obstacle owner: HealthApplication + ObstacleRemoval (CE-6003)
  D->>D: Face = ObstacleDamage.NearestPoint(box, dozer)
  alt farther than ContactMetres (6 m)
    D->>L: MoveTo StandOffMetres (4 m) in front of the face
  else blade on it
    D->>Bus: ObstacleDamage.Damage(Max / PushSeconds × dt) each tick
    Bus-->>O: DamageAssessedEvent (next tick)
    O->>O: Health → 0 ⇒ DestroyEntityCommand ⇒ rebake (existing)
  end
  D-->>D: obstacle gone (or Health ≤ 0) ⇒ Success
```
*What the picture shows that prose hid:* the dozer never writes the obstacle — the push is the damage EVENT, which inside the phase rides the batch's
buffer (`FdpEventBus.Publish` → `BatchContext.Commands`), so CE-6003's owner, removal and rebake are reused by construction (R-4212).

| as built | why |
|---|---|
| `ClearObstacle` params `Obstacle` (link), `Speed` (0 = 4), `PushSeconds` (0 = 10) | ⭐ lane default R-7004: ANY obstacle in 10 s of contact (the push is the obstacle's `Health.Max / 10` per second) — the handoff's frame |
| contact = hull centre within 6 m of the nearest face; the drive stops 4 m short | half a dozer + the blade; no new shape kit for the dozer |
| `StaticObstacle` + `ObstacleShape` added to `BrainReads` | declared stable reads — nobody writes them in the phase |
| ⏳ the company ORDERING it after smoke (CE-7003's rest) | not built here — CE-7003 |

## 7. Sequences

```mermaid
sequenceDiagram
  participant C as CompanyCommander
  participant P1 as Platoon 1 (base of fire)
  participant HQ as SupportCommand
  participant B as Bulldozer
  participant O as Obstacle
  participant T as Terrain world (every node)
  C->>C: the lead platoon reports blocked at a roadblock (read from the static copy)
  C->>P1: Order: suppress the covering positions
  C->>HQ: FireSupport: smoke on the street
  C->>B: Order: ClearObstacle(roadblock)
  B->>O: drives up, blade down, pushes N s (or the engineers' charge / tank HE drain its Health)
  O->>T: destroyed: the obstacle bake rebakes without it — sight, fire, vehicle paths open on every node
  C->>C: the street is open (paths through) — order the vehicles forward
```

```mermaid
sequenceDiagram
  participant D as DroneTeam (operator)
  participant Q as Quadcopter
  participant J as Jammer
  participant M as Mortar (via observer chain)
  D->>D: pick a target from the company picture (static copy), within battery + stock
  D->>Q: launch (FPV dive · Bomber drop · Recon spot)
  alt inside a jammer bubble
    J-->>Q: link lost: falls / returns
  else Recon
    Q->>M: spot report → the existing observer adjust + fire for effect
  else FPV / Bomber
    Q->>Q: strike (the existing drone impact)
  end
  D->>D: recharge / next drone
```

## 8. Who runs what each frame

```mermaid
graph TD
  CGF["CGF node (brains)"] --> BT["BrainTickSystem (existing): every brain incl. CompanyCommander, DroneTeam operator, Bulldozer driver"]
  BT -->|"orders = events / command buffer"| TIR["TacticalIntentResolutionSystem (existing)"]
  SIM["SimHost (muscle)"] --> OHS["AreaEffectSystem + HealthApplicationSystem (existing): blasts + blade push drain an obstacle's Health"]
  OHS -->|"Health ≤ 0 → destroy (CE-6003)"| REM["StaticObstacleBakeSystem (existing): the set changed → rebake world + cover + navmesh tiles"]
  SIM --> WRK["WreckObstacleSystem (BUILT CE-6005, HealthApplication capability): a destroyed heavy vehicle → obstacle entity (ELM, multi-frame)"]
  SIM --> DRN["DroneImpactSystem (existing) + bomber drop (NEW)"]
  SIM --> JAM["JammerSystem (NEW): drones inside a bubble lose link"]
  SCN["Scenario rules (existing triggers)"] -->|"InsertMacro → creation requests → ELM (multi-frame, acked)"| WAVE["2nd wave / reserve"]
```
*What the picture shows that prose hid:* every NEW system is a SimHost or CGF system in an EXISTING scheduled place — none needs a new host or a
private loop; and every cross-entity effect (an order, a removal, a new wreck, a wave) leaves its system as an event, a command-buffer write or an
ELM creation — the shapes §9 requires. ✅ Which node destroys a dead obstacle: the node that owns its Health (`HealthOwnership.AppliesHere`), through the one teardown (`DestroyEntityCommand`) — CE-6003, [Peek and Fire §9.9](../../DESIGN_Peek_And_Fire.md); the bake runs on every node.

## 9. The parallel contract — the AS-BUILT rule binds every new system *(user, 2026-11-13; superseded my own paraphrase the same day)*

⭐ Not restated here — two copies rot apart. The rule is the parallel programme's as built:

- **[`DESIGN_Parallel_Simulation.md`](../../DESIGN_Parallel_Simulation.md) §18.5–§18.8** — the per-entity parallel phases every brain, squad, staff,
  stress, callout, threat, weapon and movement step runs in; any new behaviour runs there too.
- **`.claude/CLAUDE.md` "BEHAVIOUR CODE RUNS IN PARALLEL"** — the nine *never / instead* rows (another entity is written only by a command that
  lands NEXT tick, R-4212 · a cross-entity read is declared and served from the tick-start view, R-1050 · a component is added WITH its final value ·
  follow-ups through `BatchContext.Later` · no static / shared scratch or shared `Random` · no lazy registration mid-tick · managed components
  replaced, never mutated · no locks · sim time only) and the **three machine checks** (§12).
- 🔒 User, `2026-11-13` (R-1050): *"reading other entity's state is always from a world static copy, never from live - the reader declares what component it reads, system adds this to the copy list"*.
- ⭐ **Where this battle meets it:** the company commander reads its platoons, the drone operator its targets and the jammer the drones — all
  declared reads; an order, a removal, a new wreck and a wave all leave as a command / an event / an ELM creation; drone and tie-break randomness
  is seeded per entity.
- ⭐ **Where the code is written** (user, `2026-11-13`): the WORLD lane works from `ui` — its seams are identical there, and it is terrain, data,
  content and two tiny owner-only systems; the machine checks run on them later (CE-6012). The BRAINS lane — new behaviour code — works from
  `exp/threaded-build`, where the checks exist (CLAUDE.md).

## 10. Decisions — ✅ APPROVED by the user, `2026-11-13` (chat)

| # | decision | 🔒 the user |
|---|---|---|
| D-1 | **terrain: a hand-made synthetic town** — an authored town plan the synthetic generator fills (§11 T-1), then adjusted by hand; ⛔ no OSM auto-import | *"no towns generated by automatic OSM import - produces garbage often, synthetic terrain better - can take inspiration from real but needs to be hand made"* |
| D-2 | speed is the parallel programme's; this design assumes it | *"assume there is enough speed"* |
| D-3 | positions prepared from the start (scenario-placed) | *"positions should be prepared from the beginning"* |
| D-4 | the scripted top = intent only (§3) | *"top level stuff … scripted approach like mission plans with triggers"* |
| D-5 | the new behaviours of §11 | *"feature/behavior extensions otherwise approved"* · *"drone stuff approved"* |
| D-6 | roadblock: breach drill + bypass; **removal mandatory**, at least as disappearing; obstacles + wrecks get removal; a bulldozer | *"breach + bypass ok"* · *"removal manadatory (even as disappearing)"* · *"pls plan for adding the bulldozer vehicle including its 3d model (no need to look armored, just it should stand some shots as armored one would)"* · *"no blade tank necessary"* |
| D-7 | daylight only; visibility 5 km fog | *"daylight only"* · *"add a a bit of fog to limit visibility (5km maybe?)"* |
| D-8 | ≈ 1 050 men / 50 vehicles / 10 aircraft; more later | *"entity numbers ok, later we could add if performance is good"* |
| D-9 | fire teams = groups inside a squad, no team brain | (explained in chat: a fire team is part of a squad; the platoon commander drives squads) |
| D-10 | defenders: ground counterattack; drones on BOTH sides (the dead zone is wanted) | *"defenders get ground counterattack; and they have attack quadrocopters (both sides do nowadays of course, which might create a no-ones dead zone and stop the attack from any side)"* |
| D-11 | the parallel contract (§9) binds every new system | *"note the building would need to follow restricition to not go against the parallization"* |
| D-12 | never test correctness on 1 000 men (§12) | *"i can;t imagine testing on full 1k men scenarios that would take ages"* |

| D-R | ✅ **water is impassable to ground units** (the navmesh excludes `Water` surfaces; vehicles path around) — the river forces the bridges | *"river lean approved"* |
| D-13 | ✅ **dynamic, not static**: overlapping phases, tempo deadlines and escalation, active defence, morale / rout, casualty evacuation, pressure triggers, the dynamism gate (§3, §3a, §12) | *"It needs to be dynamic."* · *"good feedback! definitely add those"* |
| D-14 | ✅ **civilians panic** when the attack starts — sheer panic, flight and shelter, **no organised evacuation** (§3c) | *"civilians panicking and evacuating when attack starts is a good idea, add it please"* · *"evacuation can hardly be organized under attack"* · *"its sheer panic i guess"* · *"people close to the warfight should simply start running away or trying to hide"* · *"people are curious and start fleeing only if the danger is close (and even then some cracy filmers stay)"* |
| D-15 | ✅ **the air gate**: the air defence is killed from the ground first; aircraft fly only when the gate opens (§3b) | *"the defense needs killing first before extremely expensive aircraft is let flying there, no?"* |
| D-16 | ✅ **deterministic mode + scenario variants**: a good seed repeats exactly; variants keep runs different | *"repeatibility - determinism mode ok. scenario variants (existing feature!)can help to avoid having everything same every time"* |
| D-17 | ✅ **recording ON, always** — the showcase shows the speed WITH recording | *"recording is a mandatory feature as this shows the performance of the system WHILE recording is ON."* |

## 11. The work — every item a tracker row *(🔒 user, 2026-11-13: "all needs to be recorded in traceable tasks")*

⭐ Two lanes on two VMs (the user, 2026-11-13). **W = `showcase-world`**, made from `ui` (ids `CE-6000…`,
rulings `R-6000…`): the terrain, the world rules, the bulldozer, the scenario and its content. **B = `showcase-brains`**, made from `exp/threaded-build` (ids `CE-7000…`,
`R-7000…`): every new behaviour. ⭐ The rows live in the tracker's **Area S** ([tracker](https://github.com/pjanec/HROT/blob/ui/docs/blueprints/Blueprint_Issues_Tracker.md)); the W lane's frames are in
[`HANDOFF_Ui_To_Showcase_World.md`](../../blueprints/batches/HANDOFF_Ui_To_Showcase_World.md). The B lane's frames are in [`HANDOFF_Ui_To_Showcase_Brains.md`](../../blueprints/batches/HANDOFF_Ui_To_Showcase_Brains.md). ⭐ Order inside W: CE-6003 and CE-6006 first —
B's CE-7012 (the bulldozer's ClearObstacle) stands on them.

| id | row | what | reuses | lane |
|---|---|---|---|---|
| T-1 | **CE-6001** ⛔ *(user `2026-11-14`: no more authored-town work — the town is an OSM import; CE-6020 withdrawn; the plan as built stays)* | **the town** — an authored layout of the terrain-authoring blocks (old town + square + town hall, residential belt, industrial edge, ring road, fields, hill) + a river + two bridges, fixed seed, hand-adjusted, saved as a terrain | Terrain Authoring §8, §12, §18f–§18k (the random-terrain pipeline, recipes, block layout, bridges as road); ⛔ no OSM | W · ✅ **AS BUILT `2026-11-13`** — NEW CODE only for an authored TOWN PLAN input (`assets/terrain-plans/city-battle.plan.json` → the same synth ground writer, importer, recipe blocks, Bridge features and `TerrainBake.SaveAs`; [Terrain Authoring §18l](../../DESIGN_Terrain_Authoring.md)): 1 500 m square, 58 blocks / 448 houses, river + two bridges, hill, ring road arc, town hall on the square; loads cold 25.7 s / warm 3.2 s; rails `TownPlanRails`, scenario `features/town-loads` |
| T-2 | **CE-6002** | D-R ✅: water impassable to ground units — ⭐ MEASURE first (Terrain Authoring §10 left it open), fix only if water is walkable | navmesh bake, `TerrainWorld.SurfaceTypeAt`, `RubbleMound` (CE-3438) | W |
| O-1 | **CE-6003** ✅ BUILT `2026-11-13` | **obstacle removal**: an obstacle type carries `Health` (TKB `MaxHealth`); a blast drains it by charge × closeness (⚠ DEVIATION, measured: NOT the person / hull law — it gave a block the same 80 hp for every warhead); at ≤ 0 its Health owner removes it through the ELM teardown ⇒ the existing obstacle bake rebakes sight, fire and navmesh on every node; the blade's seam is `ObstacleDamage.Damage` per tick of contact (CE-7012). As built: [Peek and Fire §9.9](../../DESIGN_Peek_And_Fire.md) | `AreaEffectSystem` (+ `AssessObstacle`), `HealthApplicationSystem`, NEW `ObstacleRemovalSystem` + `ObstacleDamage`, `StaticObstacleBakeSystem`, the obstacle types 8806–8809 | W |
| O-1b | **CE-6004** | the obstacle bake's 0.5 s WALL-CLOCK debounce (R-244) under the deterministic stepped run — measure; a removal must land at the same tick at every thread count | `StaticObstacleBake.cs:52,167`, `stepped-hash.py` — ⏳ with CE-6012 | W |
| O-2 | **CE-6005** ✅ BUILT `2026-11-13` | wreck → obstacle for heavy vehicles (M1, T-72, Bradley — `WreckObstacleDto`), created through the ELM (multi-frame, acked) by the dead vehicle's Health owner, ONCE (an alive→dead crossing); a SEPARATE obstacle (type 8819, 600 hp) on the hulk, which stays. As built: [Peek and Fire §9.10](../../DESIGN_Peek_And_Fire.md) | `Health.Current ≤ 0` (as `WreckSpawnSystem` reads it), the creation request, the obstacle bake, CE-6003's removal | W |
| V-1a | **CE-6006** | the **bulldozer** type: catalogue entry (engineering type, tracked mobility, no weapon), an armour profile (small arms bounce, a few RPG hits kill) | `TkbEntityTypes`, `NedTkbCatalog`, `ArmorModel` | | W · ✅ **AS BUILT `2026-11-13`**: type `Engineer_Bulldozer = 112` (class `Bulldozer` 1.1.*.9.1, tracked, no weapon, crew abstract); armour 50 / 40 / 40 / 35 mm, health 1000 ⇒ small arms and the 12.7 AP are 0 %, the RPG-7 100 % at 400 ⇒ the third kills ([Armour Hit Model](../vehicle-tactics/DESIGN_Armour_Hit_Model.md), "bulldozer's profile") |
| V-1b | **CE-6007** | the bulldozer's **3-D model** (tracked chassis, engine block, cab, articulated blade — NOT armoured-looking), gallery thumbnail — the ONE UI-side item the user allowed | `ShapeKit`, `VisualFamily`, `scripts/thumbnails.py` | | W · ✅ **AS BUILT `2026-11-13`**: a new family `Bulldozer` + `ShapeKits.Bulldozer` (tracks with a high rear sprocket, hood in FRONT, open ROPS cab, blade on push arms with rams, rear ripper; the blade is drawn lowered, not articulated) — [Map 3-D §6al](../../DESIGN_Map_3D_Mode.md); thumbnail in the atlas |
| L-1 | **CE-6008** ✅ BUILT `2026-11-13` — as built §4b | lethality as scenario settings (drone count + battery, accuracy, armour): accuracy = the side skill amplifier by trigger, drone count = content; battery + armour have no scenario path ⇒ CE-6016 | the existing parameters | W |
| M-1 | **CE-6009** ✅ BUILT `2026-11-13` — as built §4a | company / platoon macros (side, kit, entry point); the waves inserted by trigger | Scenario Macros, `InsertMacro` | W |
| B-0 | **CE-6010** ✅ BUILT `2026-11-13` — as built §4c | the scenario skeleton `showcases/city-battle`: the forces of §4 by macro, prepared positions (sandbags, roadblocks), the 5 km fog, recording ON, civilians placed; the scripted top of §3 | everything above | W |
| — | **CE-6012** | the world lane's code meets the parallel checks: CE-6003 / CE-6005's rails and the feature scenarios rerun with the three checks once the lane is merged with `exp/threaded-build`; then CE-6004 | the checks | W |
| V-A | **CE-6011** ✅ BUILT `2026-11-13` — as built §4c | variants: defender layout, roadblock set, AA / MANPADS positions, civilian density (D-16) | `$variant`, variant forms (Scenario Macros §17) | W |
| F-0 | **CE-7001** | ⭐ FIRST in B — fix what the grind stands on: CE-2341 · CE-2304 · PL-1 / CE-2264 · CE-3361 · CE-2232 | the existing behaviours | B |
| C-1 | **CE-7002** | `CompanyCommander`: attack (base of fire / assault / reserve, phase lines, reserve commit) + defend (a block per platoon, fall back); ⚠ `CommandPicture.MaxSubordinates = 6` ⇒ the support element | PlatoonAttack / Defend, EQS, declared reads | B |
| C-1a | **CE-7021** | the company echelon exists: type, template, standing orders, a picture sized for a company | the platoon template + `AssignSopEvent` | B |
| C-1d | **CE-7022** | puppet platoons — the commander's test bench (§12 ②) | — | B |
| C-1b | **CE-7023** | `CompanyAttack` | `PlatoonAttackBehavior` | B |
| C-1c | **CE-7024** | `CompanyDefend` | `PlatoonDefendBehavior` | B |
| C-2 | **CE-7003** | support calls — fires, smoke, bulldozer / engineers at a roadblock, drone tasking | SupportCommand, the observer chain | B |
| C-3 | **CE-7004** 🔶 counterattack BUILT `2026-11-14` (§6a) | local counterattack, asking the battalion for more — ⭐ MUST-HAVE (§3a). ⏳ asking the battalion for more is not built (no battalion commander yet) | — | B |
| C-4 | **CE-7005** | tempo: every company order carries a deadline; escalate at 50 % / 100 % — never "wait" | the HSM's phase timers | B |
| C-5 | **CE-7006** 🔶 BUILT `2026-11-14` (the carriers' half) | the mounted / dismounted handoff. As built: in the convoy's contact (`PlatoonConvoy` CContact) the carrier whose squad got out stays for it — an ARMED one (its known type's weapon suite has a mount, `TemplateArms.KnownArmed`) is assigned `VehicleFightFromCover` there, its gun over the dismounted squad; an unarmed or unknown one holds as before; secure / remount hold all (lane default R-7010). ⏳ Left: stopping SHORT of the objective (the last covered point ≤ 300 m) — today the squads get out where the contact finds them | `SquadDismount`, `VehicleFightFromCover` | B |
| A-1 | **CE-7007** | active defence: shoot and move between a squad's posts; outposts withdraw | `SquadDefend`, the fall-back | B |
| A-1b | **CE-7032** | outposts withdraw along a planned route (today: one `Back` point) | `BringOpBack` | B |
| A-2 | **CE-7008** | suppression wears off, leaders rally — first a rail proving whether a squad can be pinned forever | stress, `SquadRecover` | B |
| A-3 | **CE-7009** 🔶 BUILT `2026-11-14` (opt-in) | casualty evacuation. As built: `EvacuateMetres` on `CasualtyCare` / `ReactToContact` (0 = off): the medic carries the man that far straight back, AWAY from the squad's top contact, instead of to the nearest cover (`SquadRecover`'s destination path; each casualty his own point). ⚠ Lane default R-7009: OPT-IN, so every existing drill stays cover-first — and the squad SOP's reaction passes no params, so the showcase needs a way to set 150 (⏳ for the user: a squad-level default, or the SOP reaction's params) | `CasualtyCare`, the held-figure carry | B |
| G-A | **CE-7010** | the air gate in `SupportCommand` (§3b) | `SupportCommandBehavior`, air defence | B |
| CV-1 | **CE-7011** ✅ BUILT `2026-11-13` (Watch, boldness, the noise-panicked driver) | civilians (§3c): Watch (CE-7030), boldness (CE-7031), and a driver who panics from noise, not only when hit (`TrafficPanic.cs:187-200` covers a hit car). ⭐ CIV-3 fear-spreads is ALREADY BUILT (`CrowdSystem.cs:400-421`, measured 2026-11-13) | `CivilianHsm`, the phone held item | B |
| CV-1a | **CE-7030** | `CivilianMode.Watch`: curious at a distance, some film | `CivilianBehavior`, the phone | B |
| CV-1b | **CE-7031** | the boldness trait (≈ 5 % filmers) | civilian params | B |
| V-2 | **CE-7012** ✅ BUILT `2026-11-13` (§6c) | `ClearObstacle`: drive to the blocker, blade down, push → CE-6003 | vehicle drive behaviours | B |
| R-1 | **CE-7013** | drone team: operator + stock + battery; Bomber and Recon quadcopters; recon joins the observer chain | FPV drone, `DroneImpactSystem`, `PersonObserveFire` | B |
| R-1a | **CE-7026** ✅ BUILT `2026-11-13` | the operator launches. ⚠ DEVIATION (lane default R-7007): no runtime creation — his STOCK is authored drones landed beside him (`DroneOperator { Drone1…4, Location, IntervalSeconds }`); a launch is his ORDER (`AirPlatformStrike` on the area — the FPV's Dive takes off, searches, picks, dives); one in the air at a time, the next 20 s after the last is gone; the FPV type got its four feet (a landed drone had no ground contacts and could never take off) | FPV type + dive brain | B |
| R-1b | **CE-7027** ✅ BUILT `2026-11-14` | the bomber quadcopter (drop). As built: type 408 `Drone_Bomber` (two M67s on a drop rack); `AirAttackProfile.HoverDrop` — hovers 30 m over the point, drops one on the hover release cue, flies back; the operator re-sends it while it has rounds ([FPV Drone §5d](../vehicle-tactics/DESIGN_FPV_Drone.md)) | the impact path | B |
| R-1c | **CE-7028** ✅ BUILT `2026-11-13` | the recon quadcopter → SPOT → FireSupport → the battery. As built: NEW type 407 `Drone_Recon` (the FPV airframe, no warhead, 30-min battery, 1 500 m camera, feet) + NEW `DroneRecon { Location, RadiusM, HeightAgl }` (circles the area at 80 m; battery or an enemy jammer ⇒ down where it is); the operator sends an UNARMED stock drone to watch, an armed one to strike. ⭐ The chain needs no new code: a drone that is a member of its team's squad merges into the squad pool, the staff SPOTs it and asks FireSupport on an indirect-fire element (`StaffComposerSystem.ComposeSupport`) | `StaffComposerSystem`, `PersonObserveFire` | B |
| R-2 | **CE-7014** ✅ BUILT `2026-11-13` | anti-drone reaction + the jammer. As built: the reaction is CE-7029 (men) + `ShelterFromAir` (vehicles); the JAMMER is a TKB descriptor `JammerDto { RadiusM }` (NEW type 113 `Ew_DroneJammer`, the crewed HMMWV with a 400 m bubble); `DroneJamming` rebuilds the bubbles in the brains' serial pre-pass (`BrainPhase.PrepareToolkitFrame`) and the FPV's dive reads it: inside an ENEMY bubble the link is lost (`BatteryOut` = jammed) and it comes down where it is (lane default R-7006) | `ua-drone-shelter` | B |
| R-2a | **CE-7029** ✅ BUILT `2026-11-13` | infantry take cover from drones (vehicles already do). As built: a man's `AirThreat` edge is re-raised on his commander (`SquadSensing.AirThreat`, serial, once per squad a frame; a man aboard skipped — the vehicle shelters); `BasicSquadSop`'s new drone row (above "under a platoon") reacts `SquadShelterFromAir` — every live man is sent `ShelterFromAir`, held until 10 s after the last sensing, then the task resumes; a LONE man's `BasicInfantrySop` row reacts `ShelterFromAir` himself (a man under command never does, R-2062) | `ShelterFromAir`, `SquadSensing` | B |
| S-1 | **CE-2278** *(existing)* | smoke rounds — ⭐ MUST-HAVE on the breach drill's critical path | indirect fire | B |
| S-1b | **CE-7025** | smoke ROUNDS from the mortars (IF-5) and a FireSupport request that names smoke — ⛔ no smoke round exists today | Indirect Fire IF-5, `SupportCommandBehavior` | B |
| S-2 | **CE-7015** | unit morale / rout — ⭐ MUST-HAVE; ⛔ waits for the user's AQ101 E-3 / E-4 decision | stress | B |
| S-3 | **CE-7016** 🔶 the query BUILT `2026-11-14` | AI-chosen landing zone for the air assault. As built: NEW EQS filter `OpenGroundTest` (no solid's box within 25 m, slope < 10° across ±5 m) + NEW template `FindLandingZone` (rings 300–800 m round the objective, open ground only, scored beyond the known threats' reach and near the aircraft) — the handoff's default. ⏳ the air assault ASKING it (its LZ is still authored, `ua-air-assault` `RouteHold` Unload) | `ua-air-assault`, EQS | B |
| B-1 | **CE-7017** | the full battle on CE-6010's skeleton + the dynamism gate (§12 ⑤) | everything | B |
| — | **CE-7033** | the dynamism gate as a tool, read from a recording | the recorder | B |
| — | **CE-7034** | company vs company on a town slice, with deadlines (§12 ④ / ④a) | — | B |
| FI-1 | **CE-7018** | the interest score for filming at this size | Film Director drafter | B |
| P-1 | **CE-7019** | recording ON within the budget at this size (D-17) — measured on the battle, handed to the parallel / performance programme (the recorder's 18.7 ms) | Scale Rig, CE-3427, CE-4395 | B (measure) |
| AD6 | **CE-7020** ⏸ | **later, not priority** (user, 2026-11-13): a radar SAM — a launcher vehicle with its own radar; much is built (radar seeker, chaff, the warning, the radar modality) — the sensor that runs, the vehicle and the engagement are not. When it lands it is the air gate's heaviest threat | Air Operations AD1–AD6, `AirDefenceNodes` | B (later) |

## 12. Testing — never correctness on 1 000 men

| tier | what | time |
|---|---|---|
| ① decision rails | a commander / operator / bulldozer brain fed a hand-made static copy (*"platoon 2 at 40 %, in contact at block C, reserve fresh"*) → assert the orders it emits; no simulation | seconds, every build |
| ② puppet subordinates | platoons as light entities a test script moves (position, strength, contact), no soldiers — the commander over time: phase changes, reserve commit, fall back | seconds of sim |
| ③ per-feature dry twins | one small in-process scenario per new piece (`uc-reinforce-dry` pattern): bulldozer clears a roadblock, drone team vs a jammer, company vs a platoon | minutes, on demand + nightly |
| ④ company vs company | ≈ 250 men on a slice of the town | minutes, nightly |
| ⑤ the full battle | performance (the Scale Rig, **recording ON**) and filming; ⭐ **the dynamism gate**: ≥ X % of men moved > 50 m in every 5 min · contacts per minute ≥ Y · each phase line by its deadline (X, Y set from the first runs) — "not dynamic" is a red row. ⛔ Not a behaviour-correctness gate: a wrong moment is read from its RECORDING, not re-run | the slow lane (T3) |
| ④a stalemate probe | company vs company **with deadlines** on a town slice — where a stalemate shows first | minutes, nightly |
| ✚ every new system | ⭐ **the three machine checks** (§9): its own rails run with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` (an undeclared cross-entity read throws; `=2` lists them all) and `HROT_WORLD_GUARD=1` (a write off its owning thread throws); and `python3 scripts/stepped-hash.py --threads 1,4,8` on the feature's own small scenario — the same world at every thread count. ⚠ A rail that writes another entity asserts the result ONE tick later (R-4212) | seconds |

## 13. Rejected — one line each

- **extend the demo-city showcase** — a scripted timetable of ~50-entity acts on an 800 m map: the opposite of scale and autonomy.
- **script the platoons with mission plans** — 30+ platoons by hand, and it is not the AI showing.
- **OSM-imported town** — the user: auto-import often produces garbage.
- **a tank with a dozer blade** — the user: not needed; the bulldozer is one clear role and a target the defenders go after.
- **destructible sandbags as a goal** — no one in the battle needs them gone; removal is for what blocks vehicles (§5).
- **a fire-team brain** — ≈ 260 more commander brains for little visible difference.
- **aircraft first, against a live air defence** — the user: a suicide mission; the ground kills the AA, then the gate opens (§3b).
- **sequential phases** — long quiet stretches; overlapped instead (§3).
- **3 000 men now** — 410–500 ms brain tick measured (Scale Rig §7.7); after the parallel programme.

## 14. Design docs checked

| doc | applies? |
|---|---|
| `DESIGN_Demo_City_Showcase.md` | ⛔ not as a structure (scripted acts, small scale) · ✅ its gaps G-1…G-7 for the shared features |
| `DESIGN_Urban_Battle_Demo.md` | ✅ the platoon / squad plays reused unchanged |
| `DESIGN_Command_Chain_Example.md` | ✅ the echelon pattern C-1 extends |
| `DESIGN_Scale_Rig.md` | ✅ the budget (R-4192) and the measurement |
| `DESIGN_Parallel_Simulation.md` | ✅ §9's contract |
| `DESIGN_Terrain_Synth.md` | ✅ T-1 extends it (no authored layout today) |
| `DESIGN_Terrain_Import_UTM.md` | ✅ the fact that water has no movement cost (D-R) |
| `DESIGN_Weather_And_Time.md` §8 | ✅ the fog shortens every sensor already |
