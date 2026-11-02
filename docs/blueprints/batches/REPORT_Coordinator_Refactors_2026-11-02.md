# Report - coordinator refactors, 2026-11-02

| Item | Commit | Gate command (all `--no-build`, after `dotnet build <tests.csproj> --no-restore`) | Result | Notes |
|---|---|---|---|---|
| CE-4211 one scenario file-name constant | f1832c018 | `Fdp.Toolkits.Tests --filter FullyQualifiedName~ContentLayoutTests`; built `Hrot.Orchestrator`, `Hrot.Editor.Tests` | 13/13 pass; builds 0 errors | `ContentLayout.ScenarioFileName`; the 5 literals now use it, the two existing consts alias it. `Hrot.Orchestrator.Tests` does NOT compile: `ThePanelAsksTheOneRunSurfacesTests.cs(26)` CS0234 `Hrot.Editor` - a file this item did not touch (last changed in `7484b25d7`); pre-existing, not investigated further. |
| CE-3358 delete IG capabilities path | 9ea051197 | NED.Tests `TheTopicNamesAreDerivedFromTypesTests`+`TheDescriptorTopicsDoNotAutodisposeTests`; IG.Tests `GizmosRemoteVisualizationTests`; SimHost.Tests `StrictNetworkSeparationTests`; Toolkits.Tests `GZH015` | 12/12, 4/4, 8/8, 4/4 | Deleted publisher, `IGCapabilitiesAnnounce`, `PipelineTargetWire`(+tests), the announce tests, doc rows. `GizmoCapabilitiesTracker` KEPT: `GizmoNetworkTransportModule.Tracker` + 8 GZH tests use it (nothing in production feeds it now - comment says so). `scripts/ned-wire-inventory.py --check` OK. |
| CE-3357 fixture GUIDs | dfd674607 | `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~DistributedScenarioLoadTests` | 1/1 pass | Duration printed `< 1 ms` for a cluster test - suspicious, not re-verified. Also fixed `DebugApiServiceTests` (saves a raw spawn) and `PlayerJoinsARunningScenarioTests` (stages a raw spawn); `DebugApiBatch09Tests` and `EditorFileIOIntegrationTests` never save raw-spawned entities - unchanged. |
| S9' (CE-4153) | - | - | NOT STARTED | Stopped, see below. |
| S8 | - | - | NOT STARTED | Depends on S9'. |
| Node Features S1 | - | - | NOT STARTED | Conditional on budget. |

## Why S9' was stopped
- `HoldUnit` -> `EntityLink` has no component-level home: grep over non-test code shows `EntityLink` used only in behaviour params DTOs (`Hrot.Core/MapDefinitions/Behavior/*`), orders and the migration tool. Nothing resolves an `EntityLink` stored in a COMPONENT at load.
- Sites a component link touches: `RoutePlan.cs:49`, `RoutePlanTranslator.cs:86/135` (JSON), `StagingEntityExtractor.cs:455-485` (numeric remap), `AirHolds.cs:87-89` (reader), NED wire `MapDescriptors.cs:136` + `DescriptorMapper.cs:146`, `MapRouteIngress/EgressTranslator`, `CreateEntityRequestDescriptorBuilder.cs:193`, editor gizmo VM (`RouteWaypointGizmoTests`).
- Open design call (lean: keep the wire `int`, make the component field `EntityLink`, have the extractor resolve GUID -> new id into `.Ref` using the map it already builds, brain reads `.Ref`). Needs the user's nod because it amends SR-140 in a second place (wire twin and editor VM keep int).
- Parts (b)-(e) and S8 not started. Budget.
