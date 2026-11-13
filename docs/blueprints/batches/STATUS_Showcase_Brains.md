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
| 5 | CE-7025 smoke bombs · CE-7003 support calls (fires + smoke) | ✅ · 🔶 part (bulldozer / drone tasking wait for CE-7012 / CE-7026…) | §6b; CE-2278 (the grenade) not yet |
| 6 | CE-7008 suppression wears off | ✅ | `StressModel.QuietSeconds` |
| 7 | CE-7010 the air gate | ⏭ ignored — the user | — |

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
| `tracker-counts.py --check` · `rulings-check.py` | OK · 601 / 601 |

## 3. The morning report

| lane default taken (R-7000, user may override) | |
|---|---|
| the company has NO standing orders (handoff said `BasicCompanySop`) | its work is its task |
| `CompanyAttack` takes four fixed objectives `Objective1…4` | the intent payload is a flat struct |
| the reserve reinforces before the company falls back; the block it went into counts as held | measured on the bench: with two blocks one failure is "half" |
| R-7001 a smoke request = the `ElementEquipment.Smoke` flag; the gun loads the round its strike names | a request's `Count` is already 1 in production |

| for the user to look at | |
|---|---|
| CE-7035 | the drill fixture vs `HROT_READ_CHECK=1` (83 pre-existing reds) |
