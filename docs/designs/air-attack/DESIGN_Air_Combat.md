<!--STATUS
state: LIVE
updated: 2026-11-16
build-state: DESIGN — decisions AC-1…AC-14 carry leans and are NOT approved; ⛔ no handoff until the user approves them and calls the design complete (then the independent review)
current-answer: §0 the ask · §1 inventory · §2 the proven pattern · §3 decisions · §4 UML · §5 the numbers · §6 scenarios · §7 ids · §8 slices
stale-below: none
known-rot: none
known-conflict: Air Operations §5 (the route: a height above the ground on every 20 m sample) — AC-1 replaces it for cruise legs; §5 keeps it for low flight
related-designs:
  - DESIGN_Air_Operations.md — OWNS the aircraft route (§5), the flight models (§6), the attack behaviour and its missile evasion (§7e, unified here by AC-9), the air targets (§7c: the lead, the hull hit)
  - DESIGN_Radar_SAM.md — OWNS the radar sensor (RS-2), the published radar emission and the radar warning (RS-10, RS-6), the salvo fields (RS-4) a fighter's radar and radar missile reuse
  - ../vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS guidance, seekers, the proximity fuze and decoys (§3c); AC-6 adds the motor and the g limit to its round
  - ../weapons/DRAFT_Weapon_Malfunctions_And_Physical_Devices.md — OWNS weapon malfunctions (CE-4644): the gun jam is THEIRS, not a lapse here (AC-12)
  - ../fun/BACKLOG_Fun_Ideas.md — F-25 / CE-4643 (WWI air combat) reuses the pilot and the behaviour of this design with biplane numbers
-->

# DESIGN — air combat: jets, missiles, cannons, missile defence, ground safety, the show

🔒 **User, `2026-11-16`:** *"Design the air dogfight now, for jets, with missiles and cannons, as well as evasive maneuvers for air to air missiles
including sam missiles which shouldnt be difficult becuse the missile navigation is simple and likely easy to dodge. With feature testing scenarios
and without following the terrain but avoiding crashing to terrain - the ai autopilot need to know the aircraft limits and pull up soo enough. some
'too immersed in combat' or other auxiliary induced failures might be added if the combat turns to be too equal and boring (it is a show in the
first place so the attacks need to happen above the terrain not many kilometers away...)"* · earlier the same day: *"the aircraft now follow the
terrain height instead of keeping absolute height most of the time."*

## 0. The ask, in five parts

| # | part | where |
|---|---|---|
| A | jets fight each other: missiles (IR and radar) and cannons | AC-2…AC-8 |
| B | they dodge missiles, air-to-air AND surface-to-air | AC-6, AC-9 |
| C | no terrain following; the autopilot knows its limits and never flies into the ground | AC-1, AC-4 |
| D | the fight stays over the map, close enough to watch | AC-10, AC-11 |
| E | a stalemate is broken by human-like lapses | AC-12 |

## 1. INVENTORY *(measured `2026-11-16`)*

Queries: `search_graph name_pattern=".*(Guided|Missile|Seeker|Countermeasure|Decoy|FixedWing|AirMotion|AirAttack|AirRoute|Dogfight|Intercept|Fighter|Gcas|TargetLead).*" label=Class` ⇒ 26 production classes,
**none for air-to-air, a fighter pilot or ground collision**; `label=Interface` ⇒ 0 relevant. Grep of `docs/` + `.dev/` (dogfight · air-to-air ·
GCAS · ground collision · AIM-9 · AIM-120 · interceptor) ⇒ only F-25 / CE-4643 (WWI, fun backlog). ⚠ Index coverage cannot be checked through
the CLI.

| exists | what it gives | file |
|---|---|---|
| `FixedWingModel` | AI: follow an `AirPath`, fly to a point (2-D yaw turn at max bank + a clamped climb), straight and level; the mouse-aim manual pilot; the dead fall | `Fdp.Toolkits/Motion/FixedWingModel.cs:34-190` |
| `AirRoute.Compile` | ⚠ re-samples height above the ground every 20 m ⇒ **the terrain following the user saw** | `Motion/AirRoute.cs:97-104` |
| `MotionFrame.Ground(x, y)` | the terrain height on the motion node | `Motion/IMotionModel.cs:24` |
| `NavigationMode` | 0…7 (`Traffic` = 7) — ⭐ 8 is free | `Navigation/NavigationComponents.cs:20-60` |
| `GuidedRoundSystem` | lead-pursuit homing (`TargetLead.Point`), **constant speed**, one turn rate (30°/s default), seeker cone, proximity fuze, decoy seduction | `Combat/Systems/GuidedRoundSystem.cs:183-283` |
| `TargetLead` | the ONE predictor (rounds, missiles, gun runs) | `Combat/TargetLead.cs:14-45` |
| `Countermeasures.Release` · `CountermeasureSystem` | one flare / chaff per dispenser interval; seduction in the cone | `Combat/Countermeasures.cs:83`, `Systems/CountermeasureSystem.cs:23` |
| `MissileWarning` (+ sensing) | bearing, seeker, time to impact on the target's brain | `Combat/Components/AirDefenceComponents.cs:50` |
| `RadarEmission` · `RadarWarning` · radar sensor | a radar that radiates, a target warned "searched / tracked"; aircraft-only candidates | `Perception/Radar/RadarComponents.cs:36,81`; Radar SAM RS-2, RS-10 |
| `AirAttackNodes` evade stage | warning ⇒ stop firing, 4 decoys, beam the missile (2-D MoveTo), come round — ⚠ inside the ATTACK behaviour only | `Hrot.AI.Behaviors/Brains/AirAttackBehaviors.cs` §7e |
| gun pass | `ForwardFire` — a fixed gun fired at the led point inside its window | `AirAttackBehaviors.cs:1093` |
| `FireProcessingSystem.LedAtAircraft` · hull hit · fall | a round at an aircraft is led; it meets the hull; a dead aircraft falls and crashes | Air Ops §7c-i (AT1–AT3) |
| F-16C (401) | ⚠ a body, signatures, RCS 3 m² — **no flight, no weapons, no brain** | `NedTkbCatalog.cs:1286,1325,1333` |
| A-10C (405) | the one flying plane: cruise 150 / stall 60 / top 200 m/s, 60° bank, GAU-8 | `NedTkbCatalog.cs:1620` |
| air-to-air missiles · fighter cannon · a second fighter type | ⛔ none (munitions: GAU-8 6023/6024, Stinger 6030, Igla 6031, radar SAM 6050) | `MunitionTkbCatalog.cs` |

## 2. The proven pattern, and what we take

**How combat flight simulations do it:** a **layered AI pilot** — a tactical layer picks the fight (engage, defend, separate), a manoeuvre layer
picks the basic fighter manoeuvre (lead / pure / lag pursuit, break, extend, high and low yo-yo, defensive spiral), and an **autopilot** flies
it within the aircraft's limits (g, corner speed, energy). Ground safety is **Auto-GCAS** (F-16, in service since 2014): it predicts the
recovery path at the aircraft's g and takes the stick at the last safe moment. Missile defence is **drag** (turn away until the missile runs out
of energy), **beam / notch** (put it on the 3 or 9 o'clock — kills a radar seeker's Doppler) and the **last-ditch break** (max g across its path
inside ~2 s, when it cannot follow), with flares / chaff.

| piece | adopt? | the reason here |
|---|---|---|
| tactical layer on the brain, the manoeuvre flown on the motion node | ✅ adopt | today the brain re-issues a 2-D point every ~0.5 s (`AirAttackNodes`, `SteerPeriod`); a manoeuvre needs every-frame 3-D steering at the aircraft's own position, which lives on the motion node |
| an energy model (g limit, corner speed, thrust, drag) | ✅ adapt — 4 numbers, not aerodynamics | a dogfight IS energy; without it a turn costs nothing and nobody ever wins |
| Auto-GCAS | ✅ adopt | the user asked exactly for it; one prediction, all AI flight modes |
| proportional navigation for missiles | ⛔ reject | the lead pursuit at a turn limit already hits (CE-2187 rails); the user WANTS dodgeable missiles; adding a motor and a g limit (AC-6) makes the drag and the break work for the right reason |
| beyond-visual-range doctrine (shots at 40 km) | ⛔ reject (show rule AC-11) | the user: *"not many kilometers away"* |

## 3. Decisions — ⭐ leans, NOT approved

| # | decision — ⭐ lean | why | rejected — one line |
|---|---|---|---|
| **AC-1** | **cruise is a fixed altitude per leg**: a route leg / point move flies at `max(ground under the leg) + the ordered height`, constant along the leg (one climb or descent at the leg's start); terrain following stays ONLY for low flight (ordered height < 100 m: attack run-ins, helicopters, drones) | the user's report; real aircraft hold an altitude; clearance is kept by taking the highest ground | keep per-sample AGL (the bobbing) · absolute Z from the waypoint (the draw tool writes Z 0, CE-2172) |
| **AC-2** | ONE new motion mode, `NavigationMode.AirCombat = 8`, with the brain's choice in ONE new replicated component `AirCombatSteer` (manoeuvre, target, threat, throttle, show limits); the **motion node flies it every frame** (`FixedWingModel.Fight`) | the manoeuvre is geometry against the target's live position; the motion node holds both | the brain re-issuing `MoveTo` points (laggy, 2-D, no g) · a new motion model (one model per locomotion class, `MotionModelTable`) |
| **AC-3** | the autopilot steers a **3-D velocity vector**: it rolls to put the lift onto the desired direction and pulls at most `MaxG` (and the speed-limited g below corner speed); speed changes by thrust − drag(g) − g·sin(climb); never below stall | one rule gives every manoeuvre — a pursuit, a break, a loop, a climb are just different desired vectors | per-manoeuvre scripted paths (dozens of special cases) |
| **AC-4** | **GCAS in every AI fixed-wing mode** (route, point, air combat): each frame predict the recovery (roll level + pull `MaxG` from the current climb angle, after a 0.3 s reaction) over the terrain ahead (the highest ground under the next `recovery time + 2 s` of track); if it would pass below `FloorAgl` (default 150 m; 60 m on an attack run) it takes over until climbing and above the floor; a counter on `FighterPilotState` | the user: *"knows the aircraft limits and pulls up soon enough"*; the prediction uses the SAME g as AC-3 | a minimum-altitude clamp (an aircraft diving at 250 m/s cannot stop in one frame) · GCAS for the manual pilot (R-3007: a hand-flown plane may hit a hill) |
| **AC-5** | the **manoeuvre set** (the brain picks, the autopilot flies): `LeadPursuit` (guns: nose on the led point), `PurePursuit` (missile lock), `LagPursuit` (close without overshooting), `Break` (max g away from the threat's nose), `Extend` (unload, full throttle, away), `Climb` (trade speed for height), `Beam` (threat at 90°, descending), `Drag` (threat at the tail), `LastDitch` (max g across the missile's path), `ReturnToArena`, `Merge` (head-on toward the target, offset 300 m) | the standard set; each is a desired vector + throttle (AC-3) | a free-form planner (untestable) |
| **AC-6** | **missiles get energy and a g limit** — `MissileDto` + `BurnSeconds`, `MaxG`, `DragPerS` (speed loss per second after burn-out, more while turning), `MinSpeedMps` (below it the seeker is lost — "defeated"); turn rate = MaxG·g / speed. 0 everywhere ⇒ today's constant-speed round (ATGMs unchanged). The SAMs (Stinger, Igla, radar SAM) get numbers too | the user: dodging must work; in reality a missile is defeated by running it out of energy or out-turning it near the end; both need these two numbers | a scripted miss chance (no geometry, nothing to watch) · proportional navigation (§2) |
| **AC-7** | **the jets**: the F-16C (401) gets flight, weapons, a radar and a brain; a new **MiG-29 class (409)** for the other side; both cruise 220 m/s, corner 150 m/s, 9 g; flares + chaff; a gun (M61 20 mm / GSh-30-1 30 mm), 2 IR + 2 radar missiles | the opponents a show needs; F-16 already has a body and signatures | a mirrored F-16 for both sides (same silhouette, the viewer cannot tell sides) |
| **AC-8** | **the weapons**: IR missile (AIM-9X / R-73 class: 0.3–8 km, 90° off-boresight cone, flare rejection 0.6) · radar missile (AIM-120 / R-77 class: 1–15 km real, clamped by AC-11, radar seeker, chaff only in the end-game — RS-3) · the gun (≤ 800 m, the led point, `ForwardFire`). Shot rule: one missile at a time per target, wait for the outcome; guns when inside 800 m and the nose within 2° of the lead | numbers from public figures, rounded; the shot rule keeps a fight long enough to watch | salvos of 4 (the fight ends in one pass) |
| **AC-9** | **ONE missile defence for every aircraft**, extracted from `AirAttackNodes`' evade stage into `MissileDefence` (behaviors): warned (missile warning, or a radar TRACKED warning) ⇒ countermeasures by seeker (E2/E3 kept) ⇒ `Beam` (radar) or `Drag` (IR, far) ⇒ `LastDitch` when time to impact < 2.5 s ⇒ resume. Air combat, attack runs AND transit call it (closes the RS-6 / §7e "a move does not react" row) | *"one implementation per concept"*; the SAM and the AAM are the same problem to the aircraft | a second evasion inside the fighter behaviour (two copies drift) |
| **AC-10** | **the arena**: an authored `AirArena` entity (centre, radius, floor, ceiling) the fighters of a scenario fight inside; outside the radius a pilot flies `ReturnToArena` before anything except `LastDitch`; the start is a `Merge` through the centre | the user: *"the attacks need to happen above the terrain"*; an arena is the show's stage | clamping positions (unphysical) · no arena (they drift 20 km away, measured habit of extend / chase) |
| **AC-11** | **show rules on the arena**: `MaxShotRangeM` (default 6 km) — no missile beyond it; `MinFightAltitude` / ceiling | keeps every shot within view | real BVR ranges (the fight is over before it is visible) |
| **AC-12** | **lapses** — each pilot has a `Skill` (Rookie / Veteran / Ace); the arena counts **stalemate seconds** (no hit by anyone in it); past `StalemateSeconds` (default 90 s) each pilot draws a lapse per second with a chance rising with the stalemate and falling with skill (per-entity `SimRng`, deterministic): **Fixation** (on a gun track it holds the pull-out until GCAS at a third of the floor — may hit the ground), **LateWarning** (reacts to a missile 2 s late), **EnergyBleed** (stays in a slow turn under corner speed 10 s), **GLoc** (after > 8 g for 5 s, no control for 3 s, wings level). ⛔ the gun jam is a MALFUNCTION — CE-4644's, not a lapse | the user: *"'too immersed in combat' or other auxiliary induced failures … if the combat turns to be too equal"*; skill makes it unequal by design, lapses break the rest | a director that writes another pilot's state (breaks the per-entity rule) · random deaths (nothing to watch) |
| **AC-13** | **the behaviour** `AirCombat` (one library behaviour, stateful): pick the target (nearest hostile aircraft seen or radar-tracked inside the arena), pick the manoeuvre each 0.5 s from the geometry (aspect, range, energy, threat), pick the weapon (AC-8), call `MissileDefence` (AC-9), apply lapses (AC-12). Orders: **CAP** (patrol a point, engage what enters) and **Intercept** (a named target); a fighter's SOP reacts to an air contact with `AirCombat` | the brain decides, the motion node flies (AC-2); one behaviour, the existing order and SOP machinery | one behaviour per manoeuvre (the transitions ARE the fight) |
| **AC-14** | **who builds** — one new `air` lane (CE / R block 10000–10999) builds all of it, cross-lane into motion, combat, catalogue, behaviours and visuals, said in each commit; ⚠ if no air lane: backend (AC-1…AC-4, AC-6, AC-7 catalogue), behaviors (AC-5 picking, AC-8…AC-13), ui (AC-7 looks, missile trails, afterburner) | one owner keeps the pilot and the behaviour in step (they share the manoeuvre enum) | three lanes on one loop (three handoffs, three merges for one feature) |

## 4. UML

### 4a. Classes

```mermaid
classDiagram
  class FixedWingModel { <<existing, grows>> +Autonomous() · +Fight(frame, steer, params) ★ · +Gcas(frame, params, floor) ★ · -Steer3D(frame, desired, g) ★ }
  class AirRoute { <<existing, grows>> +Compile() · ★ a leg's height = max ground + ordered height (AC-1) }
  class AirCombatSteer { <<★ new, replicated, brain writes>> Manoeuvre · Target · Threat · Throttle · FloorAgl · Arena }
  class FighterPilotState { <<★ new, motion node writes, recorded>> GLoad · Energy · GcasActive · GcasCount · MinAgl · LastDitchAt }
  class AirCombatState { <<★ new, brain, recorded>> Target · Manoeuvre · Skill · Lapse · LapseUntil · Kills · Shots }
  class AirArena { <<★ new, authored>> Centre · Radius · Floor · Ceiling · MaxShotRangeM · StalemateSeconds · LastHitAt }
  class FlightParamsDto { <<existing, grows>> +MaxG · +CornerSpeedMps · +ThrustMps2 · +DragK }
  class MissileDto { <<existing, grows>> +BurnSeconds · +MaxG · +DragPerS · +MinSpeedMps }
  class GuidedRound { <<existing, grows>> +BurnUntil · +MaxG · +DragPerS · +MinSpeed }
  class GuidedRoundSystem { <<existing, grows>> speed profile · turn = MaxG·g / v · slow ⇒ Lost }
  class AirCombatNodes { <<★ new behaviour>> +AirCombat() · -PickTarget() · -PickManoeuvre() · -PickWeapon() · -Lapse() }
  class MissileDefence { <<★ extracted from AirAttackNodes evade>> +Warned(self) · +Run(ref state) Manoeuvre }
  class AirAttackNodes { <<existing>> evade stage ⇒ calls MissileDefence }
  class AirArenaSystem { <<★ new, the arena's one writer>> LastHitAt from the hits inside it }
  class TargetLead { <<existing>> +Point() }
  class Countermeasures { <<existing>> +Release() }
  class RadarWarning { <<existing>> Level Tracked }
  class MissileWarning { <<existing>> TimeToImpactS · Seeker }
  AirCombatNodes --> AirCombatSteer : writes (own entity)
  AirCombatNodes --> AirCombatState : writes (own entity)
  AirCombatNodes ..> MissileDefence
  AirAttackNodes ..> MissileDefence
  MissileDefence ..> MissileWarning
  MissileDefence ..> RadarWarning
  MissileDefence ..> Countermeasures
  AirCombatNodes ..> AirArena : reads
  AirArenaSystem --> AirArena : writes
  FixedWingModel ..> AirCombatSteer : reads
  FixedWingModel --> FighterPilotState : writes (own entity)
  FixedWingModel ..> TargetLead
  FixedWingModel ..> FlightParamsDto
  FixedWingModel ..> AirRoute
  GuidedRoundSystem ..> MissileDto
  GuidedRoundSystem --> GuidedRound
```

*What the picture shows that prose hid: there is no new motion model and no second evasion — the fighter is ONE new mode of the existing
plane model plus ONE extracted defence that the attack behaviour already needed; the brain and the motion node share exactly one replicated
component, `AirCombatSteer`.*

### 4b. Who runs it, and who calls it each frame

```mermaid
graph TD
  subgraph CGF["Brain node (CGF / editor)"]
    SOP["fighter SOP row: air contact ⇒ React AirCombat"] --> AC["AirCombatNodes.AirCombat (per entity, parallel brain phase)"]
    AC --> MD["MissileDefence.Run"]
    AA["AirAttackNodes (existing)"] --> MD
    AR["AirArenaSystem (serial, the arena's owner)"]
  end
  subgraph SIM["Motion node (SimHost / editor)"]
    AMS["AirMotionSystem.Execute — PostSimulation, every frame"] --> FW["FixedWingModel.Autonomous"]
    FW --> FIGHT["Fight (mode 8)"]
    FW --> ROUTE["FollowRoute / ToPoint"]
    FIGHT --> GCAS["Gcas"]
    ROUTE --> GCAS
    GRS["GuidedRoundSystem.Execute — PostSimulation, every frame"]
  end
  AC -- "AirCombatSteer (replicated) + NavigationIntent mode 8" --> FIGHT
  GRS -- "MissileWarningEvent" --> MD
```

*What the picture shows that prose hid: GCAS sits UNDER both the combat mode and the route modes, so the transit and the attack runs get it too;
and the arena has ONE writer on the brain node — no pilot ever writes another pilot or the arena.*

### 4c. A dogfight tick, and a missile defeated

```mermaid
sequenceDiagram
  participant B as AirCombat (brain, 2 Hz decisions)
  participant S as AirCombatSteer (replicated)
  participant F as FixedWingModel.Fight (motion node, every frame)
  participant G as GuidedRoundSystem
  participant D as MissileDefence
  B->>B: target = nearest hostile seen / tracked in the arena
  B->>B: geometry ⇒ Merge, then LagPursuit / LeadPursuit
  B->>S: Manoeuvre, Target, Throttle
  loop every frame
    F->>F: desired vector from the manoeuvre (TargetLead for LeadPursuit)
    F->>F: roll the lift onto it, pull ≤ MaxG, speed by thrust − drag − climb
    F->>F: Gcas: predicted recovery below the floor ⇒ take over
  end
  B->>B: nose within 2° of the lead, ≤ 800 m ⇒ gun burst (ForwardFire)
  G->>D: missile warning (IR, 6 s to impact)
  D->>D: flares (4, one per interval) · Drag
  D->>S: Manoeuvre Drag, Threat = the missile's bearing
  G->>G: burn-out, drag in the turn, speed < MinSpeed ⇒ Lost
  D->>D: time to impact < 2.5 s would have meant LastDitch
  D->>B: warning cleared ⇒ resume
```

## 5. The numbers *(public figures, rounded; the builder records each as a lane decision)*

| | F-16C (401) | MiG-29 class (409) | IR missile (AIM-9X / R-73) | radar missile (AIM-120 / R-77) | radar SAM (6050) · Stinger · Igla |
|---|---|---|---|---|---|
| speed | cruise 220, corner 150, top 400 m/s | same | boost to 850 m/s, burn 5 s | 1100 m/s, burn 8 s | 800 · 650 · 570 m/s, burn 4 · 2 · 2 s |
| g | 9 | 9 | 40 (⇒ 26°/s at 850 m/s, 70°/s at 300) | 35 | 30 · 20 · 18 |
| after burn-out | — | — | −40 m/s per s, +more turning | −30 m/s per s | −40 · −60 · −60 m/s per s |
| defeated below | — | — | 300 m/s | 350 m/s | 300 · 250 · 250 m/s |
| other | 2 IR + 2 radar missiles, M61 20 mm 500 rounds, 30 flares + 30 chaff | GSh-30-1 30 mm 150 rounds, same missiles' counterparts | cone 90°, flare rejection 0.6 | cone 60°, chaff end-game (RS-3) | as built |

📐 **Why a dodge works, in two numbers:** at 850 m/s and 40 g the IR missile turns 26°/s — a radius of 1.9 km; an F-16 at corner speed and 9 g
turns 34°/s — 0.5 km. ⇒ a hard turn across its path in the last 2 s puts the aircraft outside what it can follow, and a drag (turn away) makes
it chase until it falls under 300 m/s. ⇒ the outcome depends on WHEN the pilot reacts — which is exactly what lapses (AC-12) and skill change.

## 6. Feature-test scenarios *(R-4277 — each: `description` What / Judge, `checks.json` expect rows, a golden)*

| scenario | what happens | expect rows (machine) |
|---|---|---|
| **`ac-cruise-altitude`** | an F-16 flies a 3-leg route at 300 m over hills | `field Viper SimTransform.Position.Z` within ±15 m of each leg's altitude after the climb · `never field FighterPilotState.GcasCount > 0` |
| **`ac-gcas-ridge`** | an F-16 ordered to 50 m toward a 200 m ridge, and one in a 30° dive at 2000 m | `field FighterPilotState.GcasCount >= 1` (both) · `never health Viper == 0` · `field FighterPilotState.MinAgl >= 30` at end |
| **`ac-guns-drone`** | an F-16 on a recon drone (407) flying straight | `shot by Viper at Drone hit` before 60 s · `health Drone == 0` before 90 s |
| **`ac-ir-defeated`** | a MiG fires one IR missile at an F-16 (flares, Veteran) from 3 km behind | `field Viper Countermeasures` ≥ 2 flares released · `field FighterPilotState.LastDitchAt > 0` or the missile `Lost` · `never health Viper < 100%` |
| **`ac-ir-hits-late`** | the same shot, the F-16 with `LateWarning` forced | `health Viper < max` before 15 s — ⭐ proves the defence is what saves it |
| **`ac-sam-dodge`** | an F-16 crossing a radar SAM site (114) and a Stinger team at 3 km | the SAM fires ≥ 1 (`shot by *Sam`) · the F-16 flies `Beam` (`field AirCombatState.Manoeuvre == Beam` or the defence's state) · ≥ 1 SAM defeated · the F-16 alive at 120 s (seeded) |
| **`ac-attack-evades`** | the existing A-10 gun run under a Stinger — the §7e evasion through `MissileDefence` | the §7e rail's cues, unchanged (4 flares, beam, comes round) |
| **`ac-dogfight-1v1`** | F-16 vs MiG, both Veteran, an arena of 4 km over the map | `near Viper Fulcrum 1500` before 60 s (the merge) · `never` either beyond arena radius + 1 km · `shot` (gun or missile) before 120 s · one `health == 0` before 300 s |
| **`ac-dogfight-2v2`** | the sky show: two pairs, one Ace, one Rookie a side | ≥ 4 missile shots · ≥ 1 gun hit · ≥ 4 flare releases · ≥ 2 kills before 600 s · `never` a shot from beyond `MaxShotRangeM` |
| **`ac-stalemate-lapse`** | two Aces, `StalemateSeconds` 30 | `field AirCombatState.Lapse != None` (any) before 120 s · a kill or a crash before 240 s |

⚠ A crash and a kill both read `Health 0`; the rows tell them apart by `FighterPilotState.GcasCount` / a `shot … hit` before it.

## 7. Ids *(measured free on coordinator, behaviors, backend, ui, terrain, convoy, exp/agg-* `2026-11-16`; ⛔ permanent once built)*

| what | id |
|---|---|
| aircraft type MiG-29 class | **409** (`TkbEntityTypes`, max in use 408) |
| munitions: AIM-9X class, AIM-120 class, R-73 class, R-77 class, M61 20 mm HEI, GSh-30-1 30 mm HEI | **6060…6065** (in use up to 6053) |
| components: `AirCombatSteer`, `FighterPilotState`, `AirCombatState`, `AirArena` | **163, 277, 278, 279** (holes; 281–289 taken by agg) |
| DDS ordinals: `AirCombatSteer`, `AirArena` (+ 2 spare) | **204–207** (convoy holds 198–203) |
| `NavigationMode.AirCombat` | **8** |
| CE / R block (if the air lane is approved) | **10000–10999** |

## 8. Slices *(the order; each ends with its scenarios passing)*

| slice | items | scenarios |
|---|---|---|
| **S1 the autopilot** | AC-1 (leg altitude), AC-3 (3-D steer + energy), AC-4 (GCAS in every AI mode), F-16 flight numbers | `ac-cruise-altitude`, `ac-gcas-ridge` |
| **S2 missiles that can be beaten** | AC-6 (motor, g, drag, defeat) on every SAM and AAM; AC-9 `MissileDefence` extracted, transit reacts | `ac-ir-defeated`, `ac-ir-hits-late`, `ac-sam-dodge`, `ac-attack-evades` |
| **S3 the fighters** | AC-2 mode 8 + `AirCombatSteer`, AC-5 manoeuvres, AC-7 jets, AC-8 weapons, AC-13 behaviour + CAP / Intercept | `ac-guns-drone`, `ac-dogfight-1v1` |
| **S4 the show** | AC-10 arena, AC-11 show rules, AC-12 skill + lapses; the looks (afterburner, contrails at altitude, missile smoke) | `ac-dogfight-2v2`, `ac-stalemate-lapse` |

Machine checks on every behaviour slice: `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` (the pilot reads the target's and the missile's position —
declared tick-start reads), the world guard, and `stepped-hash.py --threads 1,4,8` on `ac-dogfight-2v2` (parallel safety, not the verdict).

⚠ **Measured by the builder first** (unknown today): the terrain height `Ground(x, y)` beyond the map's edge (RS-2a says aircraft fly there — GCAS
must not read a cliff at the edge); the vision range at which a pilot sees a jet (else the radar cues the fight); whether `RadarPerception`
runs on a non-vehicle emitter (the fighter's radar).
