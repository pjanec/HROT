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
| 4 | CE-7005 tempo | ✅ | inside `CompanyAttack` |
| 5 | CE-7025 smoke bombs · CE-7003 support calls (fires + smoke) · CE-2278 smoke grenade | ✅ · 🔶 part (bulldozer / drone tasking wait for CE-7026…) · 🔶 kit + break contact (crossing / breach face left) | §6b; Defend & Attack DA-G as built |
| 6 | CE-7008 suppression wears off · CE-7007 shoot and move · CE-7032 the OP withdraws | ✅ · ✅ · ✅ | `StressModel.QuietSeconds` · `SquadDefend.ShootAndMove` · `PlatoonDefend` OpBack → BreakContact |
| 7 | CE-7010 the air gate | ⏭ ignored — the user | — |
| 10 | CE-7012 `ClearObstacle` (the bulldozer) | ✅ | `Brains/ClearObstacleBehavior.cs`; City Battle §6c |
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
| `tracker-counts.py --check` · `rulings-check.py` | OK · 605 / 605 |

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
| R-7001 a smoke request = the `ElementEquipment.Smoke` flag; the gun loads the round its strike names | a request's `Count` is already 1 in production |

| for the user to look at | |
|---|---|
| CE-7035 | the drill fixture vs `HROT_READ_CHECK=1` (83 pre-existing reds; ⭐ also: `ReactToContact`'s choice reads a man's `WeaponState` undeclared once the men carry real weapons — seen with AssaultRiflemen, CE-2278) |
| CE-2278 kit | the Grenadier and the AssaultRifleman now carry 2 smoke grenades beside their M67s: their grenade mount's ammo total is 6 / 4 (was 4 / 2) — a scenario hash that counts rounds moves |
