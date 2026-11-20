<!--STATUS
state: LIVE — a finding handed to backend (CE-5042) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - NOTE_Terrain_To_Behaviors_Taking_CE5040.md — the minefield chain this sits beside (terrain's CE-5040 also edited NavigationExecutionSystem: the frustration threshold, not the arrival)
  - ../../designs/minefields/DESIGN_Minefields.md — mf-no-way-round is one of the minefield programme's scenarios (CE-4624 closes on them)
-->
# NOTE terrain → backend: `mf-no-way-round` went red at your CE-167 commit `d3240ca9d` (CE-5042)

📄 Row: [CE-5042](../Blueprint_Issues_Tracker.md).

| measured `2026-11-19` (`git bisect run` on `mf-no-way-round`, 303 commits, judged on "the squad reacts MinefieldAhead") | |
|---|---|
| last good | `3bfab1dc3` (and `54709228c`, the scenario's own 10 / 10 commit) |
| ⛔ **first bad** | **`d3240ca9d` — "CE-167: a point move issued where the body already stands arrives, never fails as blocked"** (`NavigationExecutionSystem`: a PathToPoint / DirectPoint move whose body is under `FrustrationSpeedThreshold` within `ArrivalRadius` of the resolved point ⇒ `Arrived`) |
| the symptom | an UNcommanded squad (`SquadMarch` → (145, 80), inside a field known at start) now creeps: its route's distance to the field falls from 133 m to only ~38 m in 120 s and never reaches the SOP's 30 m ⇒ no `MinefieldAhead`; on `54709228c` it halted at 111 s |

⚠ **The mechanism is my inference, not measured:** every fresh move starts at standstill (speed 0 < 0.2), so a marching man whose next formation slot lies within
his move's `ArrivalRadius` is now `Arrived` on the first tick, without ever accelerating ⇒ the formation advances only as fast as the slots outrun the radius. If so,
the CE-167 rule wants "the drive never STARTED" (no progress since the move was issued), not "the body is slow right now".

⭐ **Acceptance:** `scripts/scenario-regression.sh mf-no-way-round` passes again with `CE167_*` still green. ⛔ Terrain has NOT touched the arrival rule.
