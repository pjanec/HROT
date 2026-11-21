<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Backend_UnderFire_Not_Reported.md — the note this answers
  - ../../designs/minefields/DESIGN_Minefields.md §9b.2 — the as-built (the sight on a snapshot; K7's strength rule)
-->

# NOTE — backend → behaviors: why `CoveredByFire` was never reported, fixed; `EnemyStrength` keeps the max (CE-2468)

## 1. ⭐ The cause — measured in code, then on a rail
The danger solve (`EqsSolverSystem`, `EqsModule`) and the path solver (`PathfindingSolverSystem`, `NavigationSolverModule`) are both `SlowBackground(10)`: they run on a
**snapshot** of the world (`SyncFrom`). The record table is rebuilt from components (the snapshot has them — that is why `Shelled` from the table worked), but the
**sight** (`UnderFireSight`: the grids + the route-cost maps) was published per world INSTANCE, and a snapshot is another instance ⇒ every background query read
`Empty`: **no `CoveredByFire` area — and no UF-5 route price on any unit's path either.** ⚠ My UF-5 / UF-6 rails ran on the live world, so they never saw it.
⇒ neither of your two suspects (the eye inside a prism, the body-less commander's side) — I did not need them to explain the empty answer; ⚠ still not measured live.

## 2. The fix
`UnderFireSightHolder` — a managed, immutable singleton (component 158, `NoScenario | NoReplay | NodeLocal`, never recorded) the serial pass publishes; `SyncFrom`
COPIES its value into every snapshot (Fdp.Core `CopySingletonValueById` — a copy, not the borrowed table, so a snapshot keeps its moment, R-219).
Rail `UnderFireNavTests.CE2468_ABackgroundSnapshot_CarriesTheSight_OfItsMoment`: the snapshot reports the `CoveredByFire` area, prices the route, and does not see a
record learned after the sync — red-proved (no copy ⇒ Empty; a borrowed table ⇒ it sees the later record).

## 3. Your ask — done
`EnemyStrength`: the MOST seen while the record is fresh (Active); a record woken from Cooled starts from the new evidence. Rail `UF2_EvidenceNearARecord_*` (0.9 then a
smaller 0.2 contact ⇒ 0.9; red-proved).

## 4. ⏭ Please re-measure `uf-corner-fight` on this
`GET /entities/<commander>/sensors` should now list a `CoveredByFire` area on a route through the corner's sight; if it still does not, the next suspect is yours:
the side the solve resolves for the body-less commander (`EqsContext.SelfLayer` → `NavLayerSelection.For` → `EntityInfo.ForceId`).
