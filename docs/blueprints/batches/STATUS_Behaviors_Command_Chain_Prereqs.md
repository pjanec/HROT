<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Command_Chain_Prereqs.md — the items this run follows (dispatched at de10fc69a)
  - ../../DESIGN_Orders_Params_Entity_Classes.md — §15 CE-4612 as built
  - ../../designs/aggregate-units/DESIGN_Aggregate_Squad.md — §18 CE-4613 as built
-->

# STATUS — `behaviors`, the command-chain prerequisites

Started at `8952cdb87` (merged `coordinator@a70f497cb`), marker `a1bb312a7`.

## 1. The items

| # | row | state | where |
|---|---|---|---|
| 1 | CE-4612 orders as typed state (closes CE-4609) | ✅ | Orders design §15; R-2088 derived `CurrentOrder` · R-2089 `Fulfils` · R-2090 the order's identity is its `RequestId` (user's question) |
| 2 | CE-4613 `UnitStrength` | ✅ | Aggregate Squad §18; R-2091, R-2092 |
| 3 | CE-4614 `Strength` / `QuietArea` objectives (closes CE-6018 ①) | ✅ (overnight run) | Scenario Rules §10.2; R-2093 |
| 4 | CE-4589 · 5 CE-4610 · 7 CE-7037 · 8 CE-4605 · 9 CE-4606 · 10 CE-4607 · 11 CE-4608 · 12 CE-7038 | ⏳ the overnight run (`STATUS_Behaviors_Command_Chain_Overnight.md`) | — |
| 6 | CE-7035 | ✅ drill half DONE by backend before this run (X-1); ⏳ the civilian fixture half not checked yet | tracker |

## 2. The gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet build Hrot.SimHost.Tests / Fdp.Toolkits.Tests / Hrot.Editor.Tests --no-restore` (Editor after `dotnet restore` — the merge brought a project) | — | 0 errors |
| `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|StressSystemTests\|CompanyBehavior\|SupportCommand\|SquadReport` | ✓ | 279 / 280 — the red `StressSystemTests.CE2272_…` (undeclared `SimTransform` read) PRE-EXISTING (also red at `0a4a4d364`) |
| the same, `HROT_PARALLEL_SIM=0` | ✓ | 280 / 280 |
| `… --filter SopTests\|ShippedTrees\|TacticalIntent\|ScenarioVerb\|Objective` `PAR=0` | ✓ | 128 / 128 |
| the same `PAR=1 + READ_CHECK + WORLD_GUARD` | ✓ | 124 / 128 — the four `BasicInfantrySopTests` (`CE1269`, `CE2332`, `CE3306`, `CE4035`: undeclared `EntityInfo` read in the SOP fixture) PRE-EXISTING: the same four red at the start marker `a1bb312a7` (clean worktree) |
| `dotnet test Fdp.Toolkits.Tests --filter Behavior` · `--filter Squad\|Utility` | ✓ | 626 / 626 · 360 / 360 (1 skipped) |
| `dotnet test Hrot.Core.Tests --filter ParamsDto` | ✓ | 7 / 7 |
| `dotnet test Hrot.Editor.Tests --filter Order` | ✓ | 23 / 24 (5 skipped) — `TheTaskPickerTests.H5_ARifleman_…` (Combat folder 15 vs 14) PRE-EXISTING: the same at `a1bb312a7` |
| red-proofs (compiling mutants, restored + touched) | — | CE-4612: `HoldsFirm` on the two names ⇒ `CE2351(InfantrySquadHold)` red · the paused arm dropped ⇒ `CE4612_TheSquadsOrder…` red · the paused task loses the request id ⇒ red · the reply gate open to minted ids ⇒ `CE4612_ACommandersOrder…` red. CE-4613: the platoon sums squads ⇒ `CE4613_…` red · a dead man counted present ⇒ red. All green after |
| `tracker-counts.py --check` | — | OK |
| `stepped-hash.py` | — | ⏳ not run yet (CE-7037 open) |

⚠ Found on the way: `Guid.Version` is .NET 9+ (the toolkit is net8.0) — a failed build had left stale binaries that printed reds and greens for a while; every gate above is from a clean build after the fix.

## 3. For the user

| decision (lane, for review) | |
|---|---|
| R-2088 | `CurrentOrder` derived from the slot, not a second component |
| R-2089 | a play declares the kind it fulfils (`[Behavior(Fulfils = …)]`); a typo fails the build (`nameof`) / the load |
| R-2090 | ONE id: the `RequestId`; commanders mint UUIDv8 ids (deterministic), only a station's own request gets an outcome reply |
| R-2091 | "in the fight" for a unit of units keeps the subunit rule (R-D4's men rule would call a 36-man platoon effective with 4 men) |
| R-2092 | `FightingMembers`/`StandingMembers` stay member counts (slot logic); strength goes through `UnitStrength` |
