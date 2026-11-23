# Handoff: ui → terrain — two whole-world scans that stall a large terrain (Stechovice)

| | |
|---|---|
| **From** | `ui`, 2026-11-23 |
| **For** | the `terrain` lane |
| **Dispatched at** | `ui` (see the commit that adds this file) |
| **User, verbatim** | *"In any case, the terrain lane would be the right one for solving the issues."* |
| **Scope** | two engine fixes below; ids are yours to allocate (placeholders here) |

## 1. What was measured

The Stechovice showcase (`scenarios/showcases/stechovice-battle/`, on the first-use-imported Stechovice terrain, CE-11037) advances
~0.3 sim s per 30 wall s in Preview's run-to on a 4-core VM. `GET /diagnostics/architecture` (60 s of run-to, step 0.1):
`EqsSolverSystem` avg **7.6 s** per execution (max 42 s), `PathfindingSolverSystem` avg **6.2 s** (max 35 s); everything else is milliseconds.
`/diagnostics/queries` shows the timed queries cost only ms per second, so the time is outside them. An EventPipe sample (SampleProfiler,
40 s, 230 k samples) put it in two functions:

| # | function | samples (inclusive) | what it does |
|---|---|---|---|
| 1 | `RoadGraphRouter.Route` (`FDP/Toolkits/Fdp.Toolkits/Navigation/RoadGraphRouter.cs:110-160`), from `PathfindingSolverSystem.SolveGround` → `RoutePlanner.Plan` | 19 159 | Dijkstra with a **linear scan of every node to pick the next** (`:137`) and a **scan of every segment per settled node** (`:141`) ⇒ O(N·(N+S)) per route. Stechovice's road graph: **19 257 nodes, 19 691 segments** (`Stechovice.roads.json`). The turbo step waits on it (`ModuleHostKernel.WaitForRun`) |
| 2 | `VehicleFootprint.Solid` (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/VehicleFootprint.cs:71-80`), from `CoverPointsGenerator.Sized` | 11 613 | tests every cover sample against **every prism** (`foreach world.Prisms`, 333 k on Stechovice) |

Not the cause: GC (pause 0.4 %), line of sight (`terrain.SegmentBlocked` ~9 µs), the other systems.

## 2. Where the road graph comes from (for the record)

Not SUMO's router: HROT's own. The importer derives the vehicles' graph from the SUMO net (`NetRoadNetwork.Read`, `TerrainImporter.cs:84-86`)
into `<name>.roads.json`; `TerrainResidency` loads it (`RoadNetworkLoader.LoadFromJson`, `TerrainResidency.cs:309`) as
`ZoneEnvironmentData.RoadNetwork`. The terrain defines it; scenarios do not.

## 3. The fixes we suggest (yours to decide)

1. **Router**: adjacency built once per `RoadNetworkBlob` generation (node → outgoing segment list, CSR arrays, reused) + a binary-heap open
   list; same costs (`_segFactor`, direction rules, `UnderFireSight`), same tie-breaking so routes do not change. Rails: on seeded random
   graphs the route and its metres equal the current implementation's; a 20 k-node graph routes under a budget; 0 B steady (CLAUDE.md HOT PATH).
   `NearestAccess` (`:58-95`) is also a scan of every segment × 17 samples — consider a segment grid if it shows up next.
2. **Cover**: `Solid` → `world.InsideFootprint(p.X, p.Y, z, z + height)` when `height > 0`, else `world.InsideFootprint(p.X, p.Y)` —
   the indexed query (`TerrainWorld.cs:618-637`) has the same height-band rule; its own comment records the same scan costing 52 s on the
   scale terrain (CE-4366), so this caller was missed. Rail: equal answers to the scan on seeded random points.

## 4. How to check

Re-run the same measurement on Stechovice: Preview, `POST /sim/run-to {"seconds":60,"step":0.1}`, then `GET /diagnostics/architecture`
(both solvers in ms, not s). `ui` will then tune and finish the showcase on it.
