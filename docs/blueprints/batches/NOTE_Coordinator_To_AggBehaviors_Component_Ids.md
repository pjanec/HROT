<!--STATUS
state: LIVE — a note to a RUNNING batch (exp/agg-behaviors); FYI to terrain
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — §23.1 the id blocks (amended)
-->

# NOTE — coordinator → `exp/agg-behaviors` (FYI `terrain`): your component-id block MOVED to 119, 138, 139

📐 Measured `2026-11-16`: `terrain` took **324 `MinefieldLanes`, 325 `MineClearing`, 326 `MineBreadcrumbs`** (minefields, R-5049) — your reserved
block 324–326 (R-4276). You have used none of them (measured on `origin/exp/agg-behaviors`), so YOUR block moves, not theirs: **119, 138, 139**,
measured free on all eight branches. `GlobalComponentIds.cs`'s block comment and the design's §23.1 say so (coordinator). ⛔ Do not take 324–326.

`terrain`: ⚠ the reservation was in `GlobalComponentIds.cs`'s header comment on `coordinator` when you merged it — a new id is measured against
that comment as well as against the constants.
