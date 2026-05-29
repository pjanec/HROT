# BATCH-15 REPORT — JM-P2-010: Committed Fixture Envelope Migration Script

**Date:** 2026-05-29  
**Branch:** json-migration  
**Status:** COMPLETE

---

## 1. Files Created / Modified

### New Files (Source)

| Path | Description |
|------|-------------|
| `FDP/Tools/Fdp.Tools.EnvelopeStamper/Fdp.Tools.EnvelopeStamper.csproj` | Tool project file |
| `FDP/Tools/Fdp.Tools.EnvelopeStamper/StamperOptions.cs` | CommandLine options |
| `FDP/Tools/Fdp.Tools.EnvelopeStamper/FixtureStamper.cs` | Core stamping logic |
| `FDP/Tools/Fdp.Tools.EnvelopeStamper/Program.cs` | Entry point |
| `FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/Fdp.Tools.EnvelopeStamper.Tests.csproj` | Test project file |
| `FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/FixtureStamperTests.cs` | 10 tests (T01–T10) |

### Modified Files (Infrastructure)

| Path | Description |
|------|-------------|
| `IOS-IG-SimHost.sln` | Added both new projects |

### Fixture Files Stamped (43 total)

#### Scenarios (3 files)
- `scenarios/hill-attack/scenario.json`
- `scenarios/test-fire/scenario.json`
- `scenarios/test-move/scenario.json`

#### Road Networks (4 files)
- `FDP/Examples/Fdp.Examples.CarKinem/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Core.Tests/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Map.Common.Tests/Assets/sample_road.json`
- `Hrot/Subsystems/Hrot.SimHost/Assets/sample_road.json`

#### Blueprint TestAssets — core (9 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/DoorActor.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/DoorSensor.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/HasVisibleTarget.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/HealthRegen.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounter.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounterV1ModifiedBody.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounterV2WithBonus.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/LibraryMath.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/MoveToAndFire.bp.json`

#### Blueprint TestAssets — with-* (5 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/empty-library.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/instance-blueprint.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-action.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-condition.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-branch.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-callable-peer.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-custom-event.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-delay.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-sequence.bp.json`

#### Blueprint TestAssets — Invalid (8 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/AiPrimitiveParamsTooLarge.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/bad-dispatch.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/ConditionWithDelay.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/ConditionWithRunning.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/empty-name.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/InstanceStateExceedsLargestTier.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/null-asset-id.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/primitive-without-dispatch.bp.json`

#### Blueprint TestAssets — Recipes (5 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/CoverAwarePatrol.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/HealthThresholdReaction.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/MoveAndFireCombo.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadAwareEngagement.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadState.bp.json`

#### Hrot.AI.Behaviors Blueprints (5 files)
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/CoverAwarePatrol.bp.json`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/HealthThresholdReaction.bp.json`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/MoveAndFireCombo.bp.json`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SquadAwareEngagement.bp.json`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SquadState.bp.json`

---

## 2. Stamper Test Results

```
Test suite: Fdp.Tools.EnvelopeStamper.Tests
Total: 10  |  Passed: 10  |  Failed: 0  |  Skipped: 0
Duration: ~4.25 s

  Passed T01_ScenarioFile_GetsStamped
  Passed T02_BlueprintFile_GetsStamped
  Passed T03_RoadNetworkFile_GetsStamped
  Passed T04_AlreadyStampedFile_IsSkipped
  Passed T05_XunitRunnerJson_IsExcluded
  Passed T06_ExtDepsFiles_AreExcluded
  Passed T07_DryRun_DoesNotModifyFiles
  Passed T08_OrchestratorContext_GetsSchemaVersion2
  Passed T09_UnknownFormatFile_IsSkipped
  Passed T10_MetaIsFirstProperty_AndOldHeaderPreserved
```

---

## 3. Existing Test Suite Results

### Fdp.Core.Tests
```
Passed! — Failed: 0, Passed: 1141, Skipped: 2, Total: 1143
```
No regressions. All migration-related tests pass with stamped fixtures.

### Hrot.Common.Tests
```
Passed! — Failed: 0, Passed: 11, Skipped: 0, Total: 11
```
No regressions.

### Hrot.Blueprints.Tests
```
Failed! — Failed: 1, Passed: 800, Skipped: 8, Total: 809
```
The single failure is `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`.
**This is a confirmed pre-existing failure** — it fails identically on the baseline commit (before
any fixture stamping). Verified by `git stash` + re-run. Not caused by this batch.
The 800 passing tests (including all fixture-loading and compilation tests) pass with the
newly-stamped blueprint fixtures.

---

## 4. Before/After Content Samples (First 10 Lines)

### `scenarios/hill-attack/scenario.json`

**Before:**
```json
{
  "header": {
    "subsystemType": "Hrot.Scenario",
    "schemaVersion": "1.0"
  },
  "entities": {
    "98e75951-8493-4db9-9197-785b9ba470bc": {
      "MissionPlan": {
        "PlanData": {
          "activeTaskId": "00000000-0000-0000-0000-000000000000",
```

**After:**
```json
{
  "$meta": {
    "docType": "Hrot.Scenario",
    "schemaVersion": 1
  },
  "header": {
    "subsystemType": "Hrot.Scenario",
    "schemaVersion": "1.0"
  },
  "entities": {
```

---

### `Hrot/Subsystems/Hrot.SimHost/Assets/sample_road.json`

**Before:**
```json
{
  "nodes": [
    { "id": 0, "position": { "x": 100, "y": 100 } },
    { "id": 1, "position": { "x": 100, "y": 50 } },
    { "id": 2, "position": { "x": 100, "y": 150 } },
    { "id": 3, "position": { "x": 150, "y": 100 } },
    { "id": 4, "position": { "x": 50, "y": 100 } }
  ],
  "segments": [
    {
```

**After:**
```json
{
  "$meta": {
    "docType": "Fdp.RoadNetwork",
    "schemaVersion": 1
  },
  "nodes": [
    {
      "id": 0,
      "position": {
        "x": 100,
```

---

### `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-action.bp.json`

**Before:**
```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "11111111-0000-0000-0000-000000000002",
  "Name": "SimpleAction",
  "Dispatch": "AiPrimitive",
```

**After:**
```json
{
  "$meta": {
    "docType": "Hrot.Blueprints",
    "schemaVersion": 1
  },
  "Header": {
    "SubsystemType": "Hrot.Blueprints",
    "SchemaVersion": "1.0"
  },
```

---

## 5. Full Stamper Run Output (Summary)

Command run:
```
dotnet run --project FDP/Tools/Fdp.Tools.EnvelopeStamper/Fdp.Tools.EnvelopeStamper.csproj --no-build -- --root D:\WORK\IOS-IG-SimHost-FDP
```

Result:
```
Done. Stamped=43, AlreadyStamped=0, Skipped=2085, Errors=0
```

Second run (idempotency check, dry-run):
```
Done. Stamped=0, AlreadyStamped=43, Skipped=2085, Errors=0
```

All 2085 skipped files were correctly excluded (obj/, bin/, ExtDeps/, .deps.json,
.runtimeconfig.json, xunit.runner.json, Fdp.Core.Tests/Serialization/Migrations, etc.).
Zero errors.

---

## 6. Fixture Files Modified by the Stamper

43 fixture files were modified. Full list as returned by `git diff --name-only`:

```
FDP/Examples/Fdp.Examples.CarKinem/Assets/sample_road.json
Hrot/Engine/Hrot.Core.Tests/Assets/sample_road.json
Hrot/Engine/Hrot.Map.Common.Tests/Assets/sample_road.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/DoorActor.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/DoorSensor.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/HasVisibleTarget.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/HealthRegen.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounter.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounterV1ModifiedBody.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/InstanceCounterV2WithBonus.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/AiPrimitiveParamsTooLarge.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/ConditionWithDelay.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/ConditionWithRunning.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/InstanceStateExceedsLargestTier.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/bad-dispatch.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/empty-name.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/null-asset-id.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Invalid/primitive-without-dispatch.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/LibraryMath.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/MoveToAndFire.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/CoverAwarePatrol.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/HealthThresholdReaction.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/MoveAndFireCombo.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadAwareEngagement.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadState.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/empty-library.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/instance-blueprint.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-action.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-condition.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-branch.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-callable-peer.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-custom-event.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-delay.bp.json
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-sequence.bp.json
Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/CoverAwarePatrol.bp.json
Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/HealthThresholdReaction.bp.json
Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/MoveAndFireCombo.bp.json
Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SquadAwareEngagement.bp.json
Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SquadState.bp.json
Hrot/Subsystems/Hrot.SimHost/Assets/sample_road.json
scenarios/hill-attack/scenario.json
scenarios/test-fire/scenario.json
scenarios/test-move/scenario.json
```

(IOS-IG-SimHost.sln also modified — projects added.)

---

## 7. Verification: Files NOT Stamped (Exclusions Working)

| File | Reason not stamped |
|------|--------------------|
| `config.json` | No header/Header/nodes+segments — unknown format, skipped |
| `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestFixtures/Envelopes/missing_meta.json` | Excluded by `Fdp.Core.Tests/Serialization/Migrations` path rule |
| `xunit.runner.json` (any location) | Excluded by filename rule |
| All `*.deps.json`, `*.runtimeconfig.json` | Excluded by suffix rules |
| All files under `ExtDeps/` | Excluded by `ExtDeps` path rule |
| All files under `bin/`, `obj/` | Excluded by build output rules |

---

## 8. Implementation Notes

- `Hrot.Common` is NOT referenced from the tool project. DocType strings are inlined as
  `private const string` in `FixtureStamper.cs` per the implementation notes in the batch spec.
- `JsonEnvelope.HasEnvelope(dom)` is used for idempotency check (not `HasMeta` which does
  not exist on the API).
- All files written back using `Utf8JsonWriter` with `Indented = true`.
- Old `header`/`Header` blocks are preserved unchanged.
- `OrchestratorContext` gets `schemaVersion=2` per C-4.
- Dry-run mode counts files as `Stamped` (would-stamp) without writing.
