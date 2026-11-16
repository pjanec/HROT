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
| ST-0 ⑦ increment 1 (neutral DTOs Defend / BreakContact, the contract rail, the routed record) | 🔨 building | | | |
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

## 4. Ids allocated

R-8300…R-8304.
