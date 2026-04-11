# Technical Debt Tracker — Modular-2

| ID | Priority | Source | Description | Target Batch | Status |
|----|----------|--------|-------------|--------------|--------|
| DEBT-001 | P2 | BATCH-01 | Pre-existing test failures: 24 Hrot.SimHost.Tests, 7 Hrot.IG.Tests, 4 Hrot.ClusterRunner.Tests (routing guard + ActionDispatch count) | P4/P5 batches | Open |
| DEBT-002 | P3 | BATCH-01 | Stale InternalsVisibleTo for `ModuleHost.Core` in Fdp.Core.csproj | Cleanup | Open |
| DEBT-003 | P3 | BATCH-01 | Developer scripts left in .dev/ root: fix-empty-refs.ps1, remap-component-ids.ps1, etc | Cleanup | Open |
| DEBT-004 | P3 | BATCH-01 | Old source directories (Fdp.Kernel/, FDP.Interfaces/, ModuleHost.Core/) still on disk | Cleanup | Open |
