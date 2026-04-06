# Technical Debt Tracker — EyesAndMuscle Workstream

This document tracks P2 and P3 technical debt, refactoring opportunities, and deferred minor issues discovered during development and reviews.

- **P1 (critical):** Fixed immediately as Corrective Task 0 in the very next batch.
- **P2 (important):** Added here; scheduled explicitly in a near-future batch.
- **P3 (low priority):** Added here; resolved opportunistically or in a dedicated cleanup batch.

When resolved → mark ✅. Do not delete rows.

| Status | Priority | Category | Source Batch | Description | Target Fix |
|---|---|---|---|---|---|
|   | P2 | Architecture | EAM-BATCH-01 | `HrotNodeBuilder.WithRole` accepts `Hrot.SimHost.NodeRole` param (unused by builder). Prevents future extraction to shared project. Consider dropping `role` param or moving `NodeRole` to `Hrot.Common`. | BATCH-03 |
|   | P2 | DRY | EAM-BATCH-01 | `SimHostApp.EnsureIdAllocatorRouting` private method still exists — circular dependency prevents calling `DdsIdAllocatorHelper` from `Hrot.SimHost`. Move `DdsIdAllocatorHelper` to `Hrot.Common` (or inline in builder, delete from SimHostApp) during EAM-M001 migration. | BATCH-03 |
|   | P2 | Correctness | EAM-BATCH-01 | `NedReplicationModule.RegisterSystems` does NOT register `NetworkLifecycleSystemGroup(ghostCreationSystem)`. Required for replay lifecycle gating during Phase 4 SimHostApp migration. Add before EAM-M001 is executed. | BATCH-03 (Corrective Task 0) |
