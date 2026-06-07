# DEBT-TRACKER — navig-2

> P2/P3 deferred issues discovered during implementation. P1 issues go directly into the next
> batch (never here). Reference the source (DESIGN/DD chapter, file, batch, or review) for each entry.

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| 1 | P3 | BATCH-02-REVIEW, `NavigationStatusEgressTranslator` | Full entity scan per tick; should be delta-query (exclude unchanged) for large entity counts. | BATCH-05+ |
| 2 | P2 | BATCH-03-REVIEW, `NavigationTestWorldFactory` | Does not register `NavigationCorridorMuscle`; every test class that needs it must register manually. Will cause accumulation as Phase 9 tests land. | ✅ BATCH-04 |
| 3 | P3 | BATCH-03-REVIEW, `PathfindingBatchData` | Ring-buffer slot aliasing: two requests from same entity within same capacity window collide silently. No runtime diagnostic. | BATCH-06+ |
| 4 | P3 | BATCH-03-REVIEW, `PathfindingSolverSystem` §15 | Budget bands (Critical 50% / Normal 35% / Low 15%) not applied; events processed in arrival order. Priority sort deferred per spec. | BATCH-06+ |
| 5 | P3 | BATCH-04-REVIEW, `NavFakeIds` | `FakeBrainPathCacheEntry=255` conflicts with `VehicleColor` (Fdp.Examples.CarKinem). Latent — only fires if both assemblies register their components in the same process. | BATCH-05+ |
