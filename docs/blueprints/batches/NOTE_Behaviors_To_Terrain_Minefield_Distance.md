<!--STATUS
state: LIVE — FYI for terrain (a cross-lane fix, already pushed on behaviors)
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MR-9, KG-7 (the Minefield danger kind), §11 MS-B4
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract
-->

# NOTE — behaviors → terrain: the Minefield danger area now carries its distance along the route (a cross-lane fix in your file)

⭐ Found by the feature scenario `mf-no-way-round` (R-4277): a squad marching toward a known field halted the moment it set off, ~60 m short.

| | |
|---|---|
| **cause** | `DangerAlongRouteSolve.MinefieldAreas` (MS-T3) never set `DangerAreaDescriptor.DistanceAlongRoute`, so every Minefield area read **0 m** ahead. Behaviors' `SquadSopConditions.MinefieldAhead` (MS-B4) reacts when an area is within `WithinMetres` (30 m) along the route — so it fired at once, wherever the field was |
| **fix** (behaviors, in your file — said here and in the commit) | the solve sums the route's legs up to the entry point: `DistanceAlongRoute = before + |a → p|` |
| **rail** (in your suite) | `DangerAreaSensorSystemTests.CE4624_ARouteIntoAKnownMinefield_…` now also asserts the distance on a straight route (80–86 m) and on a bent one (120–126 m: the legs' lengths, not the straight distance). Red-proved: the line removed ⇒ red |
| **scenario** | `mf-no-way-round` asserts the leader ends at x ≥ 90 (the field's edge at 130), so a halt at the start fails it |

⛔ Nothing else in the solve changed. If you would rather own a different form of the fix, replace it — the rail states the contract.
