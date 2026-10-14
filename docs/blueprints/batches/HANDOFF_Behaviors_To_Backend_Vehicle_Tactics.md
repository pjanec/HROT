<!--STATUS
state: LIVE — DISPATCHED at 4b9c5495c (2026-10-14; the commit that adds this file sits on it); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — the PROGRAMME (CE-2165): items, decisions D-A…D-L (all approved), order, demos
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — items B3–B5 (M1–M3, M6 shape)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — items B6–B8 (M8, M4, M5, D-L)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — item B2 (V5 reverse, §5), B9 (M9 technicals, §6); the behaviour items this lane builds on top
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — item B10 (CE-2168, MD-A…MD-H approved)
  - docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md — item B11 (the drone's Muscle half)
-->

# HANDOFF — what the behaviours lane needs from backend: the combat engine, reverse driving and the Air motion domain

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at `4b9c5495c`**: your scope is
FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do every
item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14`: *"a tank in an urban area which is supposed to hide behind a building … just go out of the cover for a short while
for firing or watching and then move back again … RPG or anti-tank weapons … the tank will recognize that kind of enemy as a threat and
will attack him with priority"* · *"Rpg should be realistic, the tactics is not just for tanks … missile systems like law or javelin …
small copter drone attacks"* · *"Tank should be given antitank piercing rounds as well … vehicles equipped with heavy machine gun (like
popular white toyota pickup) and missile weapon system on it"* · *"We will need aircraft and boats and amphibious vehicles so we should
design for that early, no ad hoc exceptions … Motion needs to stay on muscle node"* · *"Drone will need to be controllable also from FPS
possess mode"* · *"yes write the backend handoff."*

Every decision is already approved (D-A…D-L, MD-A…MD-H). The designs are **READY-TO-BUILD with class, sequence and module diagrams** —
build against them, and per CLAUDE.md obligation ③ report *"the design carries N classes and M sequences; what I built matches / deviates
HERE and why"*; fold any deviation back into the design (obligation ⑤).

## 1. Before anything

1. Read `docs/blueprints/RULINGS.md`, then the programme's §4a index and each design's §1 (measured ground) for the items you take.
2. **Ids:** CE-2165 / CE-2166 / CE-2167 / CE-2168 are this lane's rows (behaviors block) and stay ours; record your progress in them by
   appending a dated line. Any NEW finding gets a row from **your** block (`CE-3xxx`).
3. Merge `behaviors` at `4b9c5495c` or later (the designs live there). Push a `chore: started …` marker right after the merge (rule 1b).

## 2. The items — in this order

| # | item | design | acceptance (the rails named there, in the features' own suites) | blocks on our side |
|---|---|---|---|---|
| **B1** ⭐ FIRST, ALONE | **CE-2166 — a per-shot cooldown drains twice a frame** (`WeaponDispatcherSystem.cs:55,66-72` + `AimAndFireExecutor.cs:154-156`, `FireAtPointExecutor.cs:78-80`): drain in ONE place (the dispatcher) | Missiles §1 ⚠ | a 10 s cooldown fires every 10 s; re-timed cooldown rails listed in your report (`AimAndFireExecutorTests`, `WeaponInteractionDispatcherTests`, hill attack) | every weapon demo's timing |
| **B2** | **CE-2167 / V5 — AI reverse**: `BicycleModel` clamps to `[−MaxSpeedRev, MaxSpeedFwd]`; reversing counts trajectory progress; PersonalCar / Truck reverse speeds; `LocomotionMoveTo.Issue(…, reverseAllowed)`; CE-3027 / CE-3029 (corners) | Exposure Cycle §5 | `V5_AnAiTank_ReversesToAPointBehindIt_FacingUnchanged`; re-measure the hill attack's `ReverseToBaseline` LIVE and report what it did before and after | V4 (the exposure node) |
| **B3** | **M1 — 3-D arrival**: `DetonationNotification.Travel`, `Facing.Top` (≥ 45° descent, D-A), straight-down never Front, `FacingOf` 3-D, a vehicle height band in `RaycastSolverSystem` | Hit Model §2–§3 | §5's M1 rails; `P2_AVehicleKeepsTheCircle` replaced on purpose | V1 (danger to me) |
| **B4** | **M2 — armour per face × penetrator**: `ArmorTop` (0 ⇒ rear), `…Ce` columns (0 ⇒ kinetic), `Penetrator { Kinetic, ShapedCharge, Tandem }`, `ArmourFor(platform, facing, penetrator)` | Hit Model §2 | §5's M2 rails; the three CE3071 rails unchanged | V1 |
| **B5** | **M3 — the penetrator on the round**: `CombatTkb.PenetratorOf`; the RPG / TOW munition TEMPLATES get their own `WarheadDto` (names kept; LR1 re-pinned on purpose) | Hit Model §2 | `M3_TheRpgAndTowRounds_CarryAShapedChargeWarhead` | — |
| **B6** | **M8 — ammo loadout + choice**: `AmmoLoadDto` / `AmmoLoadout`, `AmmoChoice` (the one `ArmorModel`, W-6), the switch costs one reload, `WeaponFireIntent.AmmoGuid` + `WeaponFireRequest.AmmoGuid` (a wire field — R-169), `FireProcessingSystem` fires the loaded round, the effectiveness input reads the best round and the resolved penetration | Missiles §2 | §7's M8 rails | `ua-tank-ammo` |
| **B7** | **M4 — guided rounds**: `MissileDto` (`Combat.Missile`), `GuidedRound`, `GuidedRoundSystem` (Muscle, before Ballistics), per-round lifetime and speed, min range, the lock on the turret path; Command = the LAUNCHER's sight line (`TurretPose`, kept on target by the Track hold), FireAndForget = the round's own seeker; **D-L** `FireOnMove` per mount | Missiles §3–§4 | §7's M4 / D-L rails; `WL5_W12c_AnAtgm_FiresOneRoundPerFlight` stays green | `ua-javelin-lock`, technicals |
| **B8** | **M5 / M6 data**: munitions 6010–6019 (Missiles §5 table), tank loadouts + coax MG, the RPG man (4 rockets + a rifle), an AT-team type (Javelin / NLAW), every combat vehicle with explicit `MaxHealth` + top + CE armour, vehicle eye heights | Missiles §5, Hit Model §2, programme §2 | TKB rails (`NedTkbBuilderCombatTests`, `UrbanCombatCatalogRails`); `ua-weapon-choice` re-checked | all demos |
| **B9** | **M9 — technicals**: TKB 104 (HMG) / 105 (ATGM), DIS `SmallWheeledUtilityVehicle`, pintle turrets, D-K (vehicle-level gunner) | Exposure Cycle §6 | a TKB rail per type; the HMG elevates to +70° | `ua-technical-scoot` |
| **B10** | **Motion domains v1 (CE-2168, MD-A…MD-H)**: `Gen.Locomotion` + the 7 classes (Air / Naval stop collapsing to PersonalCar); `IMotionModel { Autonomous, Manual }` + the class table; **role `MuscleAir`** with its kinematic ownership group; `AirMotionModule` (held by the SimHost by default, by the editor's Muscle too); `ManualDrive.Vertical`; the station's control scheme chosen by locomotion class; FixedWing / Surface / Amphibious = table rows that fail loudly. Ground stays as it is in v1 (its two-model refactor is a later slice) | Motion Domains §3–§4 | an air entity is owned by MuscleAir and moved only there; possession of an air entity drives it; ground rails unchanged | the drone |
| **B11** | **The drone's Muscle half (M7)**: `RotorcraftModel` (autonomous + manual), `FlightParamsDto` / `FlightParams` / `FlightState`, `DroneImpactSystem`, TKB type 403, the rotor sound class 7 + audible while airborne, the `VisionBroadphase` overhead fix, the bridge skipping a non-ground mover | FPV Drone §2–§4 | §5's M7 Muscle rails | `FpvAttack`, `ua-drone-shelter` |

**What this lane builds meanwhile and after (FYI, not yours):** V1 `ThreatDanger.ToMe`, V3 the vehicle EQS (incl. the TKB handed to the
tests at construction, `BeyondThreatRangeTest`, `OverheadCoverTest`, three templates), V4 `VehicleFightFromCover` + `LandPlatformAttack`,
V6, V8 `ShelterFromAir` + `BasicVehicleSop` + the `AirThreat` sense, `FpvAttack` + `DroneTargetDecision`, the six demos. ⚠ V3 touches
`EqsModule` / `EntityRepository.Sync`'s neighbourhood and `ThreatEvaluationSystem` — we will name every cross-lane edit.

## 3. Fences

- ⛔ No combat number moves outside B8's data, B4's top / CE columns and B3's face — say which rails re-pinned and why.
- ⛔ R-212 A1: the AI's estimate and the shot use ONE armour model; no second effectiveness formula anywhere.
- ⛔ R-261: the behaviour never names a round, a mount or a guidance mode; the weapon brain decides.
- ⛔ Motion stays on the Muscle (user); no flyer on `LinearKinematicsSystem`; no new `KinematicsMode` branch inside CarKinematics for a new
  domain (MD-B rejected that).
- ⛔ Stride is out of scope (user: *"We can ignore stride completely"*).
- ⭐ R-169: the only wire change expected is `WeaponFireRequest.AmmoGuid` (+ `ManualDrive.Vertical` in its descriptor); report any other.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · a `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · the tree clean after each suite · `tracker-counts.py --check` · every id you allocated ·
**and the INTEGRATION suite for the cross-node items**: B6 (the ammo on the wire), B7 (guidance on the Muscle), B10 (a new ownership role),
B11 — e.g. the `Hrot.ClusterRunner.Integration.Tests` weapon / possession scenarios, or why it cannot gate. Per item: the design section,
match / deviation, and the design fold-back. Then tell this lane which items are on `backend` at which sha.
