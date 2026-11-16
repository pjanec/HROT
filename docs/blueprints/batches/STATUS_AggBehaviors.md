<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 slices · §2 decisions · §3 gates · §4 ids
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_AggBehaviors.md — the batch (dispatched at 3f33c0ee3)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the design; §23.5 is this lane's ST-0 ⑦ design
-->

# STATUS — `exp/agg-behaviors`, the stochastic unit's behaviours and the chain (CE-4616)

Started at `b5a884ba7` (from `coordinator@d94af8e2f`). Unattended run. agg-backend started at `992d8d0c8` (no contract NOTE yet); agg-ui not started.

## 1. Slices

| slice | state | sha | rails | design folded |
|---|---|---|---|---|
| ST-0 ⑦ design | ✅ §23.5 — inventory (23 sites), R-8300…R-8304, UML | — | — | §23.5 |
| ST-0 ⑦ increment 1 (neutral DTOs Defend / BreakContact, the contract rail, the routed record) | ✅ built — no site flipped (R-8304) | `dbdc6b9de` | `BehaviorParamsValidationTests.R8300_…` (all 7 kinds / 26 plays) · `SquadManeuverTests.R8301_…` · red-proofs: a play DTO field removed ⇒ R8300 red; `Order` recording "" ⇒ R8301 red; `Release` not unnaming ⇒ R8301 + `CE2141_AMemberThatIsAUnit…` red | §23.5 (as-built table; D-1 `Left`/`Right` are `Bearing`) |
| ST-0 ⑦ the flip (sites → `Route`, R-8305) + ST-0 ③ `StochasticUnitDefend` (R-8306) | ✅ built (agg-backend merged at `d767196c9`) | see §3 | `ST0_Acceptance_…` (SquadManeuverTests.Aggregate.cs) · the physical plays' suites green · red-proofs: `Route` never routing ⇒ acceptance red; the staff's stochastic walk dropped ⇒ red; the Prepared time dropped ⇒ red | §23.5 (D-2) |
| ST-0 ① the four readers' slot-table branch (`AggregateUnits`) | ✅ built | see §3 | `SquadManeuverTests.ST0_*` ×3 · `ScenarioRulesTests.ST0_TheCensus_…` · red-proofs: each reader's branch / the roster-sum arm / the census add / the StrengthOf guard / an allocation in the slot walk ⇒ the matching rail red | §23.6 |
| ST-0 (the whole slice) | ✅ done — except ⛔ the SITREP ammo colour (finding F-1) | | the acceptance rail, plain and `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | |
| ST-1 (`StochasticUnitMove`, Defend's approach, R-8307/8308) | ✅ built | see git | `ST1_*` ×6 (acceptance: arrival within ±10 % of `PredictSeconds`) | §23.5 |
| ST-2 (`StochasticUnitBreakContact`, `StochasticUnitSop`, `StochasticUnitHold`, the ingress seam; R-8309…8311) | ✅ built | see git | `ST2_*` ×5 (acceptance: under fire it breaks contact) | §23.5 |
| ST-3 … ST-5 | ⏳ | | | |

## 2. Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-8300…R-8311 | see the design §23.5 (R-8307…R-8311: ST-1 / ST-2) | — |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet test Hrot.SimHost.Tests --filter BehaviorParamsValidationTests\|SquadManeuverTests\|CommanderNodesTests\|UnitCommander\|TacticalIntentResolutionSystemTests\|BehaviorParamsSweepTests\|CgfBehaviorSetupTests` | yes (built the test project) | 328 / 0 — plain AND `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| `dotnet test Fdp.Toolkits.Tests --filter Behavior\|SubordinateOrders` | yes | plain 640 / 0 · parallel env 639 / 1 — `BTreeHostsBTreeTests.U0_R5_…` ("Component size 4096 exceeds maximum 1024", deterministic, BTree host, not touched here; ⚠ not re-proved at the base) |
| `dotnet test Hrot.Editor.Tests --filter Order\|ABehaviourAdvertises` | yes | 49 / 1 — `TheTaskPickerTests.H5_ARifleman…` ⛔ PRE-EXISTING: fails identically with this batch's `OrderDtos.cs` reverted (Combat 14→16, Casualties 6→7) |
| `dotnet test Hrot.Editor.AiShared.Tests --filter TheOrdersTabTests` | yes | 4 / 0 |
| `dotnet test Hrot.Presentation.Tests --filter Menu\|Shortcut\|Keymap\|Order\|RuleEditorR5b` | yes | 175 / 0 after the goldens regenerated (A1/A2 `TheMenusAreGoldenTests`: 2 files, 8 lines each, action ids only; K2 letters D / B declared), plain and parallel env |
| `python3 scripts/tracker-counts.py --check` | — | OK |
| **ST-0 ① / ⑦ flip / ③** — `dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|ScenarioRulesTests\|Behavior\|Commander\|TacticalIntent\|CgfBehaviorSetup\|Aggregate\|Staff\|Objective\|Census\|Order\|R830\|ST0_` | yes (SimHost.Tests built) | plain 626 / 1 (`LoadPhaseChainTests.CE3345`, flaky — green alone); parallel env 623 / 4 (the four pre-existing, F-4) — every `ST0_*`, `R830*` and the physical plays' suites green in both |
| `dotnet test Fdp.Toolkits.Tests` (whole) | yes | plain 5168 / 7 · parallel 5162 / 13 — all pre-existing (F-4); the S0 seconds rail no longer lists `DefendOrder` (allow-listed with its plays' DTOs) |
| `dotnet test Hrot.Core.Tests` routing / fit / order / params | yes | 33 / 0 |
| `dotnet test Hrot.Editor.Tests` order / behaviour / picker | yes | 97 / 2 — `H5_…` pre-existing, `CE4350…` golden stale (F-3) |
| `dotnet test Hrot.Presentation.Tests` menu / order / task | yes | 166 / 0 |

## 4. Ids allocated

R-8300…R-8311 (R-8307 arrival radius / line depth; R-8308 retries; R-8309 break point; R-8310 the SOP rows; R-8311 the SOP seam; R-8305: `CommandOrders.Route` / `KeyFor`; R-8306: the Prepared times, Hasty 0 / Prepared 600 s / Fortified 3600 s).

## 5. Findings

| # | finding | for |
|---|---|---|
| F-1 | ⛔ `UnitSlot` has no ammo capacity (`Ammo` is a count; `UnitHoldings.Ammo` a pool) ⇒ a stochastic unit's SITREP ammo colour is UNKNOWN (`AmmoOf` adds nothing for it) | agg-backend: a capacity field or a per-echelon basic-load constant |
| F-2 | the staff composer served only rostered units: a roster-less stochastic unit never flushed its outbox (no SITREP / Set / the refusal's TaskFailed). Fixed here (`StaffComposerSystem._stochastic`) — agg-backend's `CE8002` rail asserted the QUEUE, never the delivery | agg-backend (aware; no action unless it prefers an empty `UnitRoster` on every stochastic unit) |
| F-3 | `TheOfferedBehavioursFitTheEntityTests.CE4350…` golden is STALE from other lanes (26 `ClearObstacle` rows, 21 new TKB types, …) before this batch; a regenerate would also take `StochasticUnitDefend` (7 rows). Not regenerated here | whoever regenerates it |
| F-4 | pre-existing reds, not touched: `TheTaskPickerTests.H5_…`, the `S0*RailTests` (Terrain / Orchestration / ReplayBrowser members), `BTreeHostsBTreeTests.U0_R5_…` + `MissionDirector…` + gizmo alloc rails (Toolkits); under the parallel env `HoldTheLine…CE2241`, `TacticsTrees…CE3170`, `StationWeapon…CE2238` ×2; `LoadPhaseChainTests.CE3345` flaky (green alone) | their lanes |
| F-5 | ST-2: the SOP is substituted when the template SOP is ASSIGNED; a unit that becomes stochastic LATER (a merge, ST-5) keeps its physical SOP until something re-assigns it | ST-5 (the merge must publish the table's SOP) |
| F-6 | `March` / `Dash` resolve to `StochasticUnitMove` but carry `SquadMarch`-shaped JSON, which the strict `MoveOrder` parse refuses (R-8303 increment 2: a neutral March DTO) | agg-behaviors increment 2 |
| F-7 | the staff also reports Set / TaskFailed when a commander's TASK ends, so a Move play's own Set duplicates it (same kind, newest wins) — harmless; the play reports explicitly so it holds without a staff | — |
