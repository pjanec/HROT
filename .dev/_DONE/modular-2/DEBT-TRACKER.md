# Technical Debt Tracker — Modular-2

| ID | Priority | Source | Description | Target Batch | Status |
|----|----------|--------|-------------|--------------|--------|
| DEBT-001 | P2 | BATCH-01 | Pre-existing test failures: 24 Hrot.SimHost.Tests, 7 Hrot.IG.Tests, 4 Hrot.ClusterRunner.Tests (routing guard + ActionDispatch count) | P4/P5 batches | ✅ BATCH-08 |
| DEBT-005 | P2 | BATCH-02 | FDP TimeConfig test/code mismatch: test asserts 1s default, code has 60s default (SyncRefreshIntervalTicks). 3 Fdp.Engine.Tests failures. | Cleanup | ✅ BATCH-08 |
| DEBT-002 | P3 | BATCH-01 | Stale InternalsVisibleTo for `ModuleHost.Core` in Fdp.Core.csproj | Cleanup | Open |
| DEBT-003 | P3 | BATCH-01 | Developer scripts left in .dev/ root: fix-empty-refs.ps1, remap-component-ids.ps1, etc | Cleanup | ? BATCH-16 |
| DEBT-004 | P3 | BATCH-01 | Old source directories (Fdp.Kernel/, FDP.Interfaces/, ModuleHost.Core/) still on disk | Cleanup | ? BATCH-16 verified absent |
| DEBT-006 | P2 | BATCH-08 | Neutral CreateEntityCommand too shallow for IG/SimHost descriptor richness. RESOLVED: ISimHostNetworkAdapter uses simple JSON passthrough. | BATCH-09 | ✅ BATCH-09 |
| DEBT-007 | P3 | BATCH-08 | DDS crash on exit in Hrot.ExCon.Tests. | Cleanup | ? BATCH-16: DdsTestCollection added with DisableParallelization |
| DEBT-008 | P3 | BATCH-08 | NedTranslationHelper.ToUpdateDescriptorRequest: DescriptorJson field does not exist on DDS type (has Payload: EntityDescriptorUnion). Needs JSON-to-EntityDescriptorUnion translation. TODO comment added. | BATCH-17 | Open |
| DEBT-009 | P2 | BATCH-09 | Hrot.SimHost/Network/ still contains original translator files that were cloned to Hrot.Network.NED/SimHost/ but NOT deleted. Pathfinding (BrainPathfindingTranslatorPack, SimPathfindingTranslatorPack, PathfindingTranslators) and perception (BrainPerceptionTranslatorPack, SimPerceptionTranslatorPack, PerceptionTranslators) packs were NOT moved to NED at all. NodeBootstrapper.cs still calls these directly from Hrot.SimHost. Hrot.SimHost.csproj still has NED reference. Remove duplicates, move remaining packs, update NodeBootstrapper to use factory, remove NED ref. | BATCH-10 | ✅ BATCH-10 |
| DEBT-010 | P2 | BATCH-11 | CGF NED decoupling | BATCH-13 | ? BATCH-16: MissionControlExecutionSystem moved to Hrot.Common; NED ref removed from CGF |
| DEBT-011 | P2 | BATCH-11 | IG Task 19 blocked: OrchestratePersonalRouteAsync uses NED CreateEntityRequest with multi-descriptor list not representable in neutral CreateEntityCommand. DrawPersonalRouteCommandTests tracks NED types. | BATCH-17 | Open |

| DEBT-012 | P3 | BATCH-16 | IgApplication: _contextMenuRequestWriter and _mapCommandAckWriter initialized but unused (callbacks use _networkAdapter). Remove unused DDS writers. | BATCH-17 | Open |