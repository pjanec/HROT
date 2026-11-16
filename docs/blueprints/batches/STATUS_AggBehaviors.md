<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 slices · §2 decisions · §3 gates · §4 ids
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_AggBehaviors.md — the batch (dispatched at 3f33c0ee3)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the design; §23.4 is this lane's ST-0 ⑦ design
-->

# STATUS — `exp/agg-behaviors`, the stochastic unit's behaviours and the chain (CE-4616)

Started at `b5a884ba7` (from `coordinator@d94af8e2f`). Unattended run. agg-backend started at `992d8d0c8` (no contract NOTE yet); agg-ui not started.

## 1. Slices

| slice | state | sha | rails | design folded |
|---|---|---|---|---|
| ST-0 ⑦ design | ✅ §23.4 — inventory (23 sites), R-8300…R-8304, UML | — | — | §23.4 |
| ST-0 ⑦ increment 1 (neutral DTOs Defend / BreakContact, the contract rail, the routed record) | ✅ built — no site flipped (R-8304) | `dbdc6b9de` | `BehaviorParamsValidationTests.R8300_…` (all 7 kinds / 26 plays) · `SquadManeuverTests.R8301_…` · red-proofs: a play DTO field removed ⇒ R8300 red; `Order` recording "" ⇒ R8301 red; `Release` not unnaming ⇒ R8301 + `CE2141_AMemberThatIsAUnit…` red | §23.4 (as-built table; D-1 `Left`/`Right` are `Bearing`) |
| ST-0 ⑦ the flip (sites → `Order(K)`) | ⏳ waits agg-backend's routing table (R-8304) | | | |
| ST-0 ① the four readers' slot-table branch | ⏳ waits agg-backend's `AggregateUnit` / `UnitSlotTable` | | | |
| ST-1 … ST-5 | ⏳ | | | |

## 2. Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-8300…R-8304 | see the design §23.4 | — |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet test Hrot.SimHost.Tests --filter BehaviorParamsValidationTests\|SquadManeuverTests\|CommanderNodesTests\|UnitCommander\|TacticalIntentResolutionSystemTests\|BehaviorParamsSweepTests\|CgfBehaviorSetupTests` | yes (built the test project) | 328 / 0 — plain AND `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| `dotnet test Fdp.Toolkits.Tests --filter Behavior\|SubordinateOrders` | yes | plain 640 / 0 · parallel env 639 / 1 — `BTreeHostsBTreeTests.U0_R5_…` ("Component size 4096 exceeds maximum 1024", deterministic, BTree host, not touched here; ⚠ not re-proved at the base) |
| `dotnet test Hrot.Editor.Tests --filter Order\|ABehaviourAdvertises` | yes | 49 / 1 — `TheTaskPickerTests.H5_ARifleman…` ⛔ PRE-EXISTING: fails identically with this batch's `OrderDtos.cs` reverted (Combat 14→16, Casualties 6→7) |
| `dotnet test Hrot.Editor.AiShared.Tests --filter TheOrdersTabTests` | yes | 4 / 0 |
| `dotnet test Hrot.Presentation.Tests --filter Menu\|Shortcut\|Keymap\|Order\|RuleEditorR5b` | yes | 175 / 0 after the goldens regenerated (A1/A2 `TheMenusAreGoldenTests`: 2 files, 8 lines each, action ids only; K2 letters D / B declared), plain and parallel env |
| `python3 scripts/tracker-counts.py --check` | — | OK |

## 4. Ids allocated

R-8300…R-8304.
