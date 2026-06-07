# BATCH-14 Report — JM-P2-009: Bootstrap Wiring (GATE)

**Batch:** BATCH-14
**Task:** JM-P2-009 — Bootstrap wiring (role-driven NodeBootstrapper + editor + CLI)
**Date:** 2026-05-29
**Status:** COMPLETE

---

## Summary

All deliverables for JM-P2-009 implemented. `MigrationServices` is now wired into every
host composition root (SimHost, CGF, IG, Editor, ClusterRunner). M-2 (per-host scoped
registration) is enforced via `HrotMigrationBootstrap` role-specific factory methods.

---

## Files Changed

### New Files

| File | Description |
|------|-------------|
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/BehaviorTreeMigrationModule.cs` | Skeleton passthrough module for BehaviorTree format (v1) |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/HrotMigrationBootstrap.cs` | Role-specific MigrationServices factory (M-2 enforcement) — 5 profiles |
| `Hrot/Subsystems/Hrot.SimHost.Tests/NodeBootstrapperMigrationTests.cs` | 9 unit tests (T01–T07) for M-2 registration enforcement |

### Modified Files

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs` | Updated BehaviorTree comment (was wrong: said "No BehaviorTreeMigrationModule is created") |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` | Added `using Fdp.Core.Serialization.Migrations;`, `using Hrot.Common.Scenario.Migrations;`, `MigrationServices` property, `RegisterMigrationServices(NodeRole, string?)` method |
| `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs` | Added `using Fdp.Core.Serialization.Migrations;`, `MigrationServices` property, wired `RegisterMigrationServices` in `BuildOrchestration` |
| `Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs` | Added `using Fdp.Core.Serialization.Migrations;`, `using Hrot.Common.Scenario.Migrations;`, `MigrationServices` property, wired `HrotMigrationBootstrap.BuildIg()` in `BuildOrchestration` |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Added `using Fdp.Core.Serialization.Migrations;`, `using Hrot.Common.Scenario.Migrations;`, updated `CreateFileService()` to pass `migrationServices`, added `CreateMigrationServices()` method |
| `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Added `"migrate"` to `validNames` set; updated `--mode` HelpText to include `migrate` |
| `Hrot/Runner/Hrot.ClusterRunner/Program.cs` | Added `using Hrot.Common.Scenario.Migrations;`, added `--mode migrate` stub handler |

---

## Test Results

### New Migration Tests (NodeBootstrapperMigrationTests)

**Filter:** `NodeBootstrapperMigration`
**Result:** Passed! — Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 537 ms

| Test | Status |
|------|--------|
| T01-a: `RegisterMigrationServices_MuscleGroundRole_RegistersScenarioTkbRoadNetworkOrchestratorContext` | PASS |
| T01-b: `RegisterMigrationServices_MuscleGroundRole_DoesNotRegisterBlueprintOrMapInteractionConfig` | PASS |
| T02: `RegisterMigrationServices_BrainRole_RegistersSameAsSimHost` | PASS |
| T03-a: `BuildIg_RegistersScenarioTkbOrchestratorContextMapInteractionConfig` | PASS |
| T03-b: `BuildIg_DoesNotRegisterBlueprintOrRoadNetwork` | PASS |
| T04: `BuildIg_Pipeline_ThrowsMigrationException_ForBlueprintDocType` | PASS |
| T05: `BuildEditor_RegistersAllCustomerFacingAndPassthroughFormats` | PASS |
| T06: `BuildClusterRunnerCi_RegistersSimHostPlusTestScriptAndNodeConfig` | PASS |
| T07: `RegisterMigrationServices_SetsPropertyOnBootstrapper` | PASS |

### Full SimHost.Tests Suite

**Result:** Failed: 38, Passed: 573, Skipped: 3, Total: 614
**Note:** The 38 failures are pre-existing test failures in unrelated test classes
(`MissionPlanTranslatorTests`, `SimHostCoreLogicPackTests`, `AreaQueryTranslatorTests`,
`FullBranchPipelineTests`, etc.). None are related to the migration bootstrap changes.

### IgNodeBootstrapper Tests

**Filter:** `IgNodeBootstrapper`
**Result:** Passed! — Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 627 ms

---

## Build Results

All directly-affected projects build clean:

| Project | Result |
|---------|--------|
| `Hrot.Common` | Build succeeded |
| `Hrot.SimHost` | Build succeeded |
| `Hrot.IG` | Build succeeded |
| `Hrot.Editor` | Build succeeded |
| `Hrot.ClusterRunner` | Build succeeded |
| `Hrot.SimHost.Tests` | Build succeeded |
| `Hrot.IG.Tests` | Build succeeded |

Full solution build (`IOS-IG-SimHost.sln`) reports `Build FAILED` due to pre-existing
errors in `Hrot.Blueprints.Tests` (missing `Hrot.Editor` assembly reference and
`IAnimationTkbQueries` type). These errors are unrelated to BATCH-14 changes.

---

## Deviations from Instructions

None significant. One minor point:

1. **`HrotDocumentTypes.BehaviorTree` comment updated**: The instructions said to create
   `BehaviorTreeMigrationModule.cs`. The existing `HrotDocumentTypes.cs` had a comment
   stating "No `BehaviorTreeMigrationModule` is created (C-1)." Per AGENTS.md rule
   ("Preserve all existing comments exactly unless they are wrong, not matching the
   intentions or code"), this comment was updated to reflect the new reality.

---

## Architecture Verification

- M-2 enforced: each host profile registers only the formats it loads:
  - SimHost/CGF: Scenario + TKB + RoadNetwork + OrchestratorContext (passthrough v2)
  - IG: Scenario + TKB + OrchestratorContext + MapInteractionConfig (no Blueprint)
  - Editor: all formats (Scenario, Blueprint, BehaviorTree, TKB, RoadNetwork, all passthroughs)
  - ClusterRunner --mode ci: SimHost profile + TestScript + NodeConfiguration
  - ClusterRunner --mode migrate: same as Editor
- `writerIdentifier` is role-driven: Brain -> "Hrot.CGF", other SimHost -> "Hrot.SimHost"
- T04 confirms fail-loud M-2: IG pipeline throws `MigrationException` for `"Hrot.Blueprints"`
