<!--STATUS
state: LIVE — FYI for terrain (a cross-lane edit in your scenario) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Temperament_And_Stress.md — §7.7 D-11 A (the break waits for his post)
  - ../../designs/group-maneuvers/DESIGN_Assault_Under_Fire.md — §9.1b D-11 B (the base of fire takes posts)
-->
# NOTE behaviors → terrain: D-11 is built — `pc-corridor-fight` re-measured; one authoring change asked (`2026-11-19`, CE-4666 / CE-4672)

| run (stepped editor, behaviors `18d031e5b`) | Red lines in cover (S1 / S2) | Blue S2 (base of fire) bunches / open | Blue S1 / S3 open after 90 s |
|---|---|---|---|
| before D-11 | ❌ 60 s / ❌ 60 s | ❌ / ❌ | ✅ / ✅ |
| D-11, the scenario as committed (`SearchRadius` 10 on Red's squads) | ❌ 83 s / ❌ 60 s | ✅ / ✅ | ✅ / ❌ |
| D-11, Red's `SearchRadius` 25 (local edit only) | ✅ / ✅ | ✅ / ✅ | ❌ / ❌ |

✅ **DONE by behaviors (user, `2026-11-19`: "just change the radius to 25 yourself, terrain is not claiming it"):** Red's three squads' `"SearchRadius": 10` → `25` in your `scenarios/tests/pc-corridor-fight/scenario.json` (cross-lane, said). Your own CE-4673 measure put the nearest cover 13.5–16 m off
the centreline posts, so 10 m can only post them in the open — D-11 A lets a man REACH his post before he breaks, but the post must be cover.
⏳ **Mine, next:** Blue Squads 1 and 3 (the assault elements) wholly in the open at 90 s — being probed; I report before touching the check.
