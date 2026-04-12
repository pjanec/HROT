# Technical Debt Tracker — Modular-2

| ID | Priority | Source | Description | Target Batch | Status |
|----|----------|--------|-------------|--------------|--------|
| DEBT-001 | P2 | BATCH-01 | Pre-existing test failures: 24 Hrot.SimHost.Tests, 7 Hrot.IG.Tests, 4 Hrot.ClusterRunner.Tests (routing guard + ActionDispatch count) | P4/P5 batches | ✅ BATCH-08 |
| DEBT-005 | P2 | BATCH-02 | FDP TimeConfig test/code mismatch: test asserts 1s default, code has 60s default (SyncRefreshIntervalTicks). 3 Fdp.Engine.Tests failures. | Cleanup | ✅ BATCH-08 |
| DEBT-002 | P3 | BATCH-01 | Stale InternalsVisibleTo for `ModuleHost.Core` in Fdp.Core.csproj | Cleanup | Open |
| DEBT-003 | P3 | BATCH-01 | Developer scripts left in .dev/ root: fix-empty-refs.ps1, remap-component-ids.ps1, etc | Cleanup | Open |
| DEBT-004 | P3 | BATCH-01 | Old source directories (Fdp.Kernel/, FDP.Interfaces/, ModuleHost.Core/) still on disk | Cleanup | Open |
| DEBT-006 | P2 | BATCH-08 | Neutral CreateEntityCommand too shallow for IG/SimHost descriptor richness. IG's route entity creation needs MapRoute + EntityInfo + WorldPos descriptors. SimHost's CreateEntityRequestSystem processes full EntityDescriptorUnion list. ICreateEntityRequestSource interface returns NED types. Blocks TASK-P4-002 and TASK-P4-003. Fix: extend CreateEntityCommand with neutral descriptor types in Hrot.Core. | BATCH-09 | Open |
| DEBT-007 | P3 | BATCH-08 | DDS crash on exit in Hrot.ExCon.Tests: CycloneDDS native AccessViolationException during process shutdown when DdsWriterAdapterTests runs alongside other tests. All test logic passes; crash is from native teardown. Possible fix: xunit assembly fixture for DDS shutdown or isolate DDS tests to standalone project. | Cleanup | Open |
| DEBT-008 | P3 | BATCH-08 | NedTranslationHelper.ToUpdateDescriptorRequest stub incomplete: only fills EntityId and BaseVersion, ignores DescriptorJson. IG's SendGeoSpatialUpdate maps a WorldPos descriptor that will be silently dropped. Needs full WorldPos translation. | BATCH-09 | Open |
