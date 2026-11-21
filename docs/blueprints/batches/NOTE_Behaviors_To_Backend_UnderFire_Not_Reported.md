<!--STATUS
state: LIVE — for backend
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md §9b, §9b.1 — UF-6 (the danger solve reads the unit's side; Direct → CoveredByFire) and K7 (the merge)
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.9 — R4 (reads EnemyStrength), R6 (the CoveredByFireAhead row that waits for the area)
-->

# NOTE — behaviors → `backend`: a Direct `UnderFire` record exists, but the route danger solve never reports `CoveredByFire` (CE-2468 UF-6)

`behaviors`, `2026-11-21`, measured on `behaviors` at `2ea9fe7fc` (your `a58bc1c29` merged) + CE-2466, the deterministic `uf-corner-fight` run.

## 1. ⭐ The record is there (UF-2 works)
`GET /entities?component=UnderFireKnowledge` at 100 / 140 / 200 s: ONE record, network id 1522 — `SidePlus1 2` (Friend), `Cause Direct`, `Point (639.2, 382.9, 0)`
(Red's west corner), `RangeM 150`, `State 0`, `LearnedAt 97.07 → 111.98 → 182.28` (refreshed by later contacts), `EnemyStrength 7 → 7 → 3`.

## 2. ⛔ The danger answer never carries it (UF-6)
`GET /entities/1000/sensors` (the Blue Squad commander's route sensor, `DangerRouteWatch`), every 4 s from 140 to 200 s: `ready`, `kinds "Shelled,
StreetCrossing, Intersection, Minefield, CoveredByFire"`, **`count 0, areas []`** — while the squad stands and then bounds at x 623–638, y 283–330,
45–100 m straight down the street from the record's point (the corner's men saw and shot them there at 97 s and 184 s). The route runs ToPoint
(650, 430), through the corner itself. ⇒ ⭐ the CoveredByFire pass returns nothing for this route.

⚠ **Not measured** (you can see it faster): whether `UnderFireSight` covers those cells (authored terrain, eye 1.6 m, target 1.0 m — the corner is a
building corner: does the eye stand inside the building's prism?), and which side the solve resolves for a BODY-LESS commander's sensor.

## 3. ⚠ The merge lowers `EnemyStrength`
K7's "newest wins" also overwrites `EnemyStrength`: 7 at the break, 3 after a later, smaller contact. Squad Tactics R4 compares against the force
REMEMBERED — ⭐ ask: keep the MAX `EnemyStrength` while the record is fresh (it ages out anyway).

## 4. What it costs today
R6 (`CoveredByFireAhead`) never fires, so a resumed bound walks straight back into the corner's field of fire — measured: second contact at 184 s,
third at 287 s, the squad destroyed at 290–410 s (it never reaches the corners, which is why U2's never-back-in rows pass).
