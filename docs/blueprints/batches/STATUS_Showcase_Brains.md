<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: §1 the items · §2 the gates · §3 the morning report
related-designs:
  - HANDOFF_Ui_To_Showcase_Brains.md — the frames this run follows
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — §6a the company as built, §11 the rows
-->

# STATUS — `showcase-brains`, the overnight run (CE-7000)

Branch `showcase-brains` from `exp/threaded-build` (≥ `a9955908e`) + `ui` (docs) + `showcase-world`, started at `8a12755b5`.
🔒 User, `2026-11-13`: *"do the HANDOFF_Ui_To_Showcase_Brains.md overnight autonomously … ask no questions, decide yoursels, fix issues as you see
them, do not act against the parallelism rules"* · *"and skip fixing 7001"* · *"ignore 7010"*.

## 1. The items

| # | rows | state | where |
|---|---|---|---|
| 1 | CE-7001 F-0 | ⏭ skipped — the user | — |
| 2 | CE-7021 company echelon · CE-7022 puppet bench | ✅ | TKB 306; `SquadManeuverTests.Company.cs` |
| 3 | CE-7023 `CompanyAttack` · CE-7024 `CompanyDefend` | ✅ | `Brains/CompanyBehaviors.cs`; `PlatoonDefend` Ready reports Set up |
| 4 | CE-7005 tempo · CE-7004 counterattack | ✅ · 🔶 (the counterattack; asking the battalion for more not built — no battalion commander) | inside `CompanyAttack` · `CompanyDefend` `KCounter` (R-7008) |
| 5 | CE-7025 smoke bombs · CE-7003 support calls (fires + smoke) · CE-2278 smoke grenade | ✅ · 🔶 part (bulldozer / drone tasking wait for CE-7026…) · 🔶 kit + break contact (crossing / breach face left) | §6b; Defend & Attack DA-G as built |
| 6 | CE-7008 suppression wears off · CE-7007 shoot and move · CE-7032 the OP withdraws | ✅ · ✅ · ✅ | `StressModel.QuietSeconds` · `SquadDefend.ShootAndMove` · `PlatoonDefend` OpBack → BreakContact |
| 7 | CE-7010 the air gate | ⏭ ignored — the user | — |
| 10b | CE-7006 mounted / dismounted | 🔶 the carriers' half: an armed carrier fights from cover over its dismounted squad (R-7010); stopping short of the objective left | `PlatoonConvoy` KeepVehicle; `TemplateArms` |
| 12 | CE-7009 casualty evacuation | 🔶 built, OPT-IN (`EvacuateMetres`; R-7009) — the showcase's way to set 150 m is for the user | |
| 11 | CE-7016 the landing zone | 🔶 the query (`OpenGroundTest` + `FindLandingZone`); the air assault asking it left | |
| 10 | CE-7012 `ClearObstacle` (the bulldozer) | ✅ | `Brains/ClearObstacleBehavior.cs`; City Battle §6c |
| 8 | CE-7014 the jammer · CE-7029 infantry take cover from drones | ✅ · ✅ | `JammerDto` + type 113 `Ew_DroneJammer`; `DroneJamming` (serial pre-pass index) read by the FPV's dive (FPV Drone §5a) · |
| 8b | CE-7026 the operator launches | ✅ (R-7007: an order to his authored stock; the FPV got feet) | `DroneOperator`; FPV Drone §5b |
| 8e | CE-7027 the bomber quadcopter | ✅ | type 408 `Drone_Bomber`; `AirAttackProfile.HoverDrop` + the hover release cue; the operator re-sends it; FPV Drone §5d |
| 8c | CE-7028 the recon drone joins the observer chain | ✅ | type 407 `Drone_Recon`, `DroneRecon` orbit; the chain by squad membership (no new code); FPV Drone §5c |
| 8d | CE-7029 infantry take cover from drones | ✅ | `SquadSensing.AirThreat` → `BasicSquadSop` drone row → `SquadShelterFromAir`; a lone man's `BasicInfantrySop` row |
| 9 | CE-7030 Watch · CE-7031 boldness · CE-7011 | ✅ · ✅ · ✅ (the noise-panicked driver: `TrafficPanicSystem.Deafened`, R-7005) | `CivilianBehavior` Watch state; civilians design §5f CIV-3 marked BUILT |

## 2. The gates

| command (switches named) | result |
|---|---|
| `dotnet build Hrot/Subsystems/Hrot.SimHost.Tests --no-restore` | 0 errors |
| `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test …SimHost.Tests --no-build --filter SquadManeuverTests.CE70` | 5 / 5 |
| the same with `HROT_PARALLEL_SIM=0` | 5 / 5 |
| T-1, the drills' own suite: `HROT_PARALLEL_SIM=1 …--filter SquadManeuverTests` | 233 / 233 (the same with `=0`; `+ HROT_WORLD_GUARD=1` 233 / 233) |
| `+ HROT_READ_CHECK=1` on the whole drill suite | ⚠ 83 red — ALL in squad code this run did not touch (e.g. `SquadGates.cs:47` reads `WeaponState` undeclared): the drill fixture never registers the brain phase's declared reads ⇒ **CE-7035** filed. The company rails are green under it |
| red-proofs (each a compiling mutant, restored after) | `SupportSet` ⇒ true: `CE7023_CompanyAttack…` red · `Tempo` off: `CE7005_…` red · `Reinforce` finds no reserve: `CE7024_…` red |
| `dotnet test Hrot.Core.Tests --no-build` | 366 / 367 — the red (`HrotEnvironmentTests.CreateGeoTransform_HasNoDefaultOrigin…`, `NoGeoFrameException`) is PRE-EXISTING on `exp/threaded-build`: no file it touches differs from the base (CE-4396, `ea3ae421c`) |
| `NedTkbBuilderCombatTests` | 56 / 56 (incl. `CE7021_TheInfantryCompany_IsThreePlatoons`) |
| smoke (item 5): `…--filter SquadManeuverTests.CE7|CE2176|CE2312` + `Fdp.Toolkits.Tests --filter AmmoChoiceTests|AimAndFireExecutorTests` | 21 / 21 · 152 / 152; red-proofs: the battery's `Round` dropped ⇒ 2 red · the HQ ignoring the flag ⇒ red · `LoadIndex` never switching ⇒ red |
| clean build after smoke: drills `PAR=1` / `=0` (+ `HROT_WORLD_GUARD=1`) · `Fdp.Toolkits.Tests` Combat · `Hrot.Core.Tests` | 236 / 236 · 236 / 236 · 446 / 446 · 366 / 367 (the pre-existing geo red) |
| CE-7008: `…--filter SquadManeuverTests|StressSystemTests`, `PAR=1` / `=0` | 263 / 263 · 263 / 263; red-proof: the quiet term dropped ⇒ `CE7008_…` red |
| CE-7007: drills + stress `PAR=1` / `=0` (+ `HROT_WORLD_GUARD=1`) on the clean build | 264 / 264 · 264 / 264; red-proof: `ShootAndMove` not called ⇒ `CE7007_…` red |
| CE-7032: drills + stress `PAR=1` / `=0` | 264 / 264 · 264 / 264; red-proof: no withdrawal ⇒ `CE2276_E2_…OP…` red |
| CE-7030 / 7031: `CivilianHsmTests` `PAR=0` | 29 / 29; red-proof: `Curious` never true ⇒ both red. ⚠ `PAR=1`: every civilian rail (old ones too) throws in the FIXTURE — it skips the serial `CivilianFearSystem` that adds `CivilianState` in production ⇒ added to CE-7035 |
| CE-7012: `ClearObstacleBehaviorTests` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) · drills + stress + shelter + clear `PAR=1` / `=0` (+ `HROT_WORLD_GUARD=1`) | 2 / 2 · 2 / 2 · 272 / 272 · 272 / 272; red-proof: push by `Current` not `Max` ⇒ both red |
| CE-2278: `AimAndFireExecutorTests.CE2278_*` + `AmmoChoiceTests` + `WL4_W12e*` + `PersonHandsTests` · `SquadManeuverTests.CE2278_*` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) | 23 / 23 · 2 / 2 · 2 / 2; red-proofs: the executor not re-mounting a smoke order ⇒ red · the smoke arm off ⇒ red. Clean build: `Fdp.Toolkits.Tests` Combat 543 / 543 · drills + stress + clear + shelter 274 / 274 both ways · `Hrot.Core.Tests` 366 / 367 (the pre-existing geo red) |
| CE-7011 driver: `Hrot.Traffic.Tests` (after `dotnet restore` — the test project's assets predated a merged reference: the third stale-binary trap) | 85 / 85; red-proof: the noise arm off ⇒ `CE7011_…` red |
| CE-7029: `SquadCoordinationSystemTests` 21 / 21 · `SquadManeuverTests.CE7029_*`, `BasicInfantrySopTests.CE7029_*` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`); clean build: drills 275 / 275 `PAR=1` · 328 / 328 `PAR=0`; alone, `PAR=1` and `=0`: `BasicInfantrySopTests` 18 / 18 · `BasicVehicleSopTests` 4 / 4 · `ShippedTreesAreCSharpTests` 31 / 31 | red-proofs: the system not raising it ⇒ red · the squad row's sense changed ⇒ red · the lone row on the wrong sense ⇒ red. ⚠ Mixed in ONE `PAR=1` run with the drills, the SOP classes give 10–23 ROTATING reds — PRE-EXISTING (the base before CE-7029 in a clean worktree: 23, 20) ⇒ **CE-7036**; six shipped-tree snapshots red on the base (`0926ff85c` re-filed groups) re-recorded (group line only) |
| CE-7014: `AirAttackNodesTests` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) 22 / 22; clean build: `Fdp.Toolkits.Tests` Motion + Combat + Squad 798 / 798 · drills + drones 297 / 297 both ways · `Hrot.Core.Tests` 366 / 367 (the pre-existing geo red) | red-proof: the jammed check off ⇒ the enemy-jammer case red |
| CE-7026: `DroneOperatorBehaviorTests` + `AirAttackNodesTests` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) 24 / 24 · `NedTkbBuilderCombatTests.CE7026_CE7014_*`; clean build: drills + drones + operator 299 / 299 both ways · Fdp Motion 70 / 70 · `Hrot.Core.Tests` 367 / 368 (the pre-existing geo red) | red-proofs: the in-the-air wait dropped ⇒ red · one FPV foot removed ⇒ red |
| CE-7028: `AirAttackNodesTests` + `DroneOperatorBehaviorTests` `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) 26 / 26 · `NedTkbBuilderCombatTests.CE7028_*`; clean build: drills + drones + operator 301 / 301 both ways · `Hrot.Core.Tests` 368 / 369 (the pre-existing geo red) | red-proofs: the orbit steered at the point ⇒ red · the operator's mount test off ⇒ red. ⚠ found on the way: the first cut sent an UNKNOWN-type drone to watch (no template ⇒ "no mount") — narrowed to a known type with no mount |
| CE-7004: `SquadManeuverTests.CE7004_*` + the company rails `PAR=1` / `=0` (+ `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) 7 / 7; clean build: drills + drones + operator 303 / 303 both ways | red-proof: the counter guard always false ⇒ the strong-reserve case red |
| CE-7016 (the query): `LandingZoneTests` 3 / 3; clean build: `Fdp.Toolkits.Tests` Eqs + Spatial 174 / 174 | red-proof: the clearance to the box centre ⇒ red |
| CE-7006: `SquadManeuverTests.CE7006_*` + the convoy, drone-operator and fight-from-cover rails `PAR=1` / `=0` (+ `HROT_WORLD_GUARD=1`) 47 / 47 | red-proof: the armed check dropped ⇒ red |
| CE-7009: `SquadManeuverTests.CE7009_*` + the four `CE2152_CasualtyCare_*` `PAR=1` / `=0` (+ `HROT_WORLD_GUARD=1`) 5 / 5; ⚠ under `HROT_READ_CHECK=1` all five (the old four too) throw on the squad gates' `WeaponState` read — CE-7035; clean build: drills + drones + operator 304 / 304 both ways | red-proof: the evacuation arm off ⇒ red |
| `tracker-counts.py --check` · `rulings-check.py` | OK · 609 / 609 |

## 3. The morning report

| lane default taken (R-7000, user may override) | |
|---|---|
| the company has NO standing orders (handoff said `BasicCompanySop`) | its work is its task |
| `CompanyAttack` takes four fixed objectives `Objective1…4` | the intent payload is a flat struct |
| the reserve reinforces before the company falls back; the block it went into counts as held | measured on the bench: with two blocks one failure is "half" |
| R-7003 civilians watch / bold are opt-in params | no existing civilian scenario changes |
| R-7002 shoot and move opt-in below the company, on (25 s) in `CompanyDefend` | no existing defence changes |
| R-7004 the bulldozer clears ANY obstacle in 10 s of blade contact (`PushSeconds`, a task param) | the handoff's frame; no TKB field yet |
| R-7005 a SUMO car's driver abandons it within 25 m of a shot or a burst (≤ 2 cars a frame, each once) | closer than the walkers' 50 m: a driver in a running car hears less |
| R-7006 a jammer's 400 m bubble (type 113) downs every drone of another force inside it (an FPV falls where it is) | the battery's existing path; its own side flies through |
| R-7007 a drone launch is the operator's ORDER to his authored stock (no runtime creation) | creation would need a new pending component + system |
| R-7008 a LOST block (gone / gave it up) is counterattacked by a ≥ 70 % reserve, once; a FAILING one (Black / falling back) is reinforced | the handoff's trigger and the existing one each keep theirs |
| R-7009 casualty evacuation is opt-in (`EvacuateMetres`, 0 = off) | every existing drill stays cover-first |
| R-7001 a smoke request = the `ElementEquipment.Smoke` flag; the gun loads the round its strike names | a request's `Count` is already 1 in production |

| for the user to look at | |
|---|---|
| CE-7035 | the drill fixture vs `HROT_READ_CHECK=1` (83 pre-existing reds; ⭐ also: `ReactToContact`'s choice reads a man's `WeaponState` undeclared once the men carry real weapons — seen with AssaultRiflemen, CE-2278) |
| CE-7009 casevac on | the squad SOP's reaction passes no params, so how should the showcase turn on 150 m evacuation — a squad-level default, or params on the SOP's reaction? (built opt-in, R-7009) |
| CE-7036 | under `HROT_PARALLEL_SIM=1` the SOP suites and the drill suite cannot share a run (rotating reds; pre-existing) — gate them separately until the collection fix |
| CE-2278 kit | the Grenadier and the AssaultRifleman now carry 2 smoke grenades beside their M67s: their grenade mount's ammo total is 6 / 4 (was 4 / 2) — a scenario hash that counts rounds moves |

## 4. Morale — `HANDOFF_Coordinator_To_Showcase_Brains_Morale.md` (dispatched at `d9353eb5d`)

| row | state | where | design folded |
|---|---|---|---|
| CE-4584 a burst within 2 × injury radius is a near miss | ✅ | ONE helper `HitModel.NearMiss` (event + UnderFire stamp) — bullets (`BallisticsSystem`) and bursts (`AreaEffectSystem`, people only) both call it | Temperament & Stress §7.6 |
| CE-7015 the rout | ✅ | `CombatEffective.IsStanding` / `StandingMembers` (fighting AND not broken) — `PlatoonDefend.SlotFailing` both arms + baseline, `PlatoonAttack.AssaultBlack` (relieve AT-9 and abort AT-10); `CombatEffective` itself unchanged; `BrainReads` declares `Stress` | Defend & Attack DA-67 / AT-10; AQ101 §R |
| CE-4583 the Shelled danger area | ✅ | `ShelledAreas` (3 bursts in 60 s within 15 m ⇒ a 25 m area, cools after 120 s) fed by `ShelledAreaSystem` (after `AreaEffectSystem`); a `GasFootprint` of `Kind = Shelled` ⇒ the route classifier and the danger sensor need no new path; `DangerAreaKinds.Shelled` is bit 0 (the DDS IDL byte is signed); `SquadMarch` avoids it | Squad Tactics §7.7 |

| gate | result |
|---|---|
| T-1 feature suites: `Fdp.Toolkits.Tests` AreaEffect + DangerAlongRoute + HitModel · SimHost `SquadManeuverTests|StressSystemTests` · `SimHostCoreLogicPackTests` | 144 / 0 · 273 / 0 · 5 / 0 |
| new rails, `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`: SimHost drills + stress · `Fdp.Toolkits.Tests` danger / area | 272 / 1 · 94 / 0 — the 1 is `StressSystemTests.CE2272_…` (`SimTransform` read undeclared), PRE-EXISTING at `0a4a4d364` (clean worktree) |
| full `Fdp.Toolkits.Tests` | 5003 / 5 — the five S0 rails (`S0IdTypesRailTests` ×3, `S0WalkerTests`, `S0ComponentTypesRailTests`), PRE-EXISTING at `0a4a4d364` |
| full `Hrot.SimHost.Tests` | 2747 / 7 — `OneBootstrap…`, `CE1431_…`, `CE4208_…`, `UtmEverywhere…`, `CE3345_…` (order-dependent) PRE-EXISTING at `0a4a4d364`; `DemoMissionPlan_…` flaky (green alone); `SimHostCoreLogicPack_EmptyWorld_…` green alone |
| red-proofs (compiling mutants, restored and touched) | the burst near-miss call removed ⇒ CE4584 red · `SlotFailing` on the fighting count ⇒ CE7015 defend red · `AssaultBlack` on the colour alone ⇒ CE7015 attack red · the 60 s window dropped ⇒ CE4583 red · the classifier ignoring the kind ⇒ CE4583 red |
| stepped hash `HROT_PARALLEL_SIM=1 python3 scripts/stepped-hash.py --threads 1,4,8 --dll <Debug runner>` | `morale-barrage-green` (105 entities, simTime 2): **STATE EQUAL** — 0 types, 0 `SimTransform` rows differ at 4 and 8 threads (⚠ the 120 steps end before any barrage; the new systems are covered at 1 / 4 / 8 only by their own rails under `HROT_PARALLEL_SIM=1`). `city-battle` (6628 entities): ⛔ **not compared** — the runs ended at simTime 7.917 / 7.817 / 7.733 for the same 120 steps ⇒ **CE-7037**, not run on the base |
| live, T3: `python3 scripts/utility-demo-check.py --launch morale-barrage-green` / `-veteran` | ✅ both PASS. GREEN (Militia, nerve 0.15): first near miss 27.1 s, first man broken 46.9 s, **falls back 46.9 s** (1 broken, 0 down, 16 bursts, closest 6.8 m, peak line stress 0.66; sim to 53.8 s). VETERAN (Veteran, nerve 0.9): first near miss 26.9 s, one man broken at 58.0 s, **holds for 241 s** (0 down, 20 bursts, closest 8.5 m, peak stress 0.76). Times are sim seconds after the line was engaged; frame counts were not read |
| ids | CE-4584, CE-4583, CE-7015 (handoff's); CE-7037, CE-7038 (findings); no new ruling |

| the live demo — what it took (all in `scripts/gen-morale-scenarios.py` / `run_morale_barrage`) | |
|---|---|
| the spotter (`SpotForFire`) | ⛔ dropped: at (290,160) house H3 hid the line; in the open he saw it but the ENGAGED line shot him (hit 31 s, broke, dead ~130 s) and the fire stopped with him (one round in 240 s) |
| the battery's strike authored at t = 0 | ⛔ refused — "gun 0 has no indirect weapon … no gun reaches the point" (the crews have not manned the tubes). The check orders the `Strike` once they have (`ua-indirect-fire`'s precedent) |
| the `Location` | local metres `[x, y, z]` (CE-2433) — the check's `geo_of()` is stale (landed at (52.3, 14.0)); a strike on (120.8, 156, 0), Squad 1 upstairs in OBJ, ran 60 s and fired nothing ⇒ **CE-7038** |
| the aim point | 10 m short of Squad 2 (open ground): inside the 11.8 m near miss, outside the 5.9 m injury radius — ON the squad, a man of EITHER variant went down hurt (collapsed counts as broken) by ~33 s and both fell back |
| `UnderFire` | not on the editor's Scenario view ⇒ the check judges the near miss from the bursts and reads `Stress` |
