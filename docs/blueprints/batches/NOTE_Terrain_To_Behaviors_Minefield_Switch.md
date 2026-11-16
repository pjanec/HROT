<!--STATUS
state: LIVE — for behaviors (MS-B3)
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Terrain_Minefield_Switch.md — the question this answers
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract (its "Added since" table)
  - ../../designs/minefields/DESIGN_Minefields.md — MF-9, MR-11, MR-18
-->

# NOTE — terrain → behaviors: the clearer's switch and the lane (answers NOTE_Behaviors_To_Terrain_Minefield_Switch)

Pushed on `terrain` (`e99beab11`). ⭐ Your lean, taken, with one change.

| # | answer |
|---|---|
| 1 | ⭐ **`Fdp.Toolkit.Terrain.MineClearing`** (component 325, unmanaged, recorded, Brain group, `dtMineClearing` 195): `byte Active` · `MineClearingTool Tool` (1 Plough, 2 Flail, 3 LineCharge) · `float WidthM` · `Vector3 LineTo`. The clearer's OWN brain writes it (a per-entity write). Re-point `ClearerEquipment.SetActive(world, clearer, on)` to set `Active` (and `Tool` / `WidthM` from the order — 4.5 m for plough / flail). TKB 116 carries it, inactive (MS-T4, building). |
| 1b | ⭐ the LINE CHARGE uses the SAME marker: `Tool = LineCharge`, `LineTo` = where it lands (≤ ~100 m), `WidthM` ~8, `Active = 1` — the Muscle fires once on the activation EDGE (detonations every ~8 m, the swath cleared). So `FireLineCharge(world, clearer, toward)` is three field writes, no new seam. |
| 2 | ⚠ **changed**: a runtime lane is NOT appended to the field (`SetLane`): the field is authored and owned by the scenario's owner — the swath clear runs where the clearer moves (MR-11). It is a RECORD of the deciding node: **`MinefieldLanes`** (component 324, `dtMinefieldLanes` 194); `LaneCleared` now carries the segment (`Ax, Ay, Bx, By, Width`, `Lane = 255`). Read lanes with `MinefieldLanesSystem.LanesOf(view, fieldGuid, into)` — e.g. to find the lane to cross single file. ⭐ The brain raises nothing; the Muscle raises `LaneCleared` when the clearer leaves the field or switches off. |
