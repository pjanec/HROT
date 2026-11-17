<!--STATUS
state: LIVE — in progress
updated: 2026-11-17
current-answer: the row table
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — CE-1958 (an animal and its pen's wall; the home circle)
-->
# STATUS — backend: THE USER'S EDITOR OBSERVATIONS (CE-1958)

Handoff: [`HANDOFF_Coordinator_To_Backend_User_Observations.md`](HANDOFF_Coordinator_To_Backend_User_Observations.md). Started at `a401d7d22`
(`origin/coordinator` merged into `backend`).

| row | state | reproduction | root cause | fix + rail |
|---|---|---|---|---|
| **CE-1958** pigs stuck in the fence | 🚧 fixing | ⏳ live `farm-under-fire` probe | ① the kinematics traced an ANIMAL at its centre only (`HullClearance.HasHull` leaves `Quadruped` out — meant for "no backing out", it also dropped the body's corners) ⇒ a pig walked at a wall stops with its centre on the wall line, half its body each side ② an animal's HOME circle was the outline's FARTHEST-VERTEX radius (11.3 m for the 16 m square pen, walls 8 m off) and a herd member's spot is the herd's spot + up to 11 m of spread, never kept in the home ⇒ grazing spots past the pen's wall ③ an animal has no reverse and turns only while it walks ⇒ once facing the wall it never turned away (the walk never ends, so no new spot is chosen) | ⏳ |
