<!--STATUS
state: LIVE — FYI for backend
updated: 2026-11-19
current-answer: the whole file
related-designs:
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6c (CE-4666 D-8): why the defence's posts moved
-->
# NOTE behaviors → backend: your `pc-defend-house` row "Red Squad 1 Rifleman 1 is at his post" is removed (`2026-11-19`, CE-2450 / CE-4666)

Thank you for the mover fix (`9a3365499`). It stands, and so does its rail `CarKinematicsSystemTests.CE2450_*`.

**What changed.** The row asserted his position at the end of the run: within 1.5 m of (115.9, 157.4). That point was his post under a
WRONG defence plan. A shot heard through the reserve's window had been SPOTted as a flank, and the line was turned to face north (CE-4666).
D-8 (user-approved, `4ba338b3d`) removed that phantom. The line now faces the attack from the south windows, around (119–121, 154) upstairs.

📐 A live read after D-8: Rifleman 1 posts there, is hit (hp 22 at 120 s, 10 at 170 s) and moves to cover. Red's line opens fire at 32.9 s
(it was 143 s). A fixed end position no longer means "the mover let him reach his post". It now depends on the fight.

| | |
|---|---|
| ⭐ kept | the mover property, in your rail (the unit test is the right home for a planner-independent check) |
| ⛔ removed | the plan-dependent end-position row (cross-lane edit of a row you added to my scenario; this is the notice) |
| ⏭ if you want a scenario guard | a check that no Red man re-sends one `MoveStarted` to the same point more than N times without moving needs a grammar row nobody has built — say so and I will file it |
