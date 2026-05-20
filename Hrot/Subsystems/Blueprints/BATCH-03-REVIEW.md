# BATCH-03-REVIEW.md

## Summary

BATCH-03 has been fully implemented and all acceptance criteria have been met.

**Test results:**
- Previous baseline: 57 passed, 1 skipped
- After BATCH-03: 76 passed, 1 skipped, 0 failed
- New tests added: +19 (8 from TH-008 + 11 from TH-009)

---

## Deliverables Completed

### Debug Protocol Types (`Hrot.Blueprints.Core/Debug/`)

- **`IBlueprintProbeSink.cs`** -- interface with `OnNodeEnter` and generic `OnPinValueChanged<T>`
- **`DebugProbe.cs`** -- static class with `NullProbeSink` (default no-op) and mutable `Sink` property
- **`IBlueprintDebugSession.cs`** -- sealed records `BreakpointHit`, `NodeExecuted`, `PinValueChanged`; interface inheriting `IBlueprintProbeSink` with breakpoint management and step controls

### Foundation Stubs (`FDP/Toolkits/Fdp.Toolkits/Blueprints/`)

New enum/class files:
- `BlackboardTier.cs` -- B1024, B4096, B16384 values
- `BlueprintLatentCursor.cs` -- StructLayout Sequential Size=16 with GraphId
- `BlueprintCompileException.cs` -- exception with Diagnostics property
- `CompilerMode.cs` -- Release, Debug, Trace

Replaced placeholder stubs:
- `BlueprintDefinition.cs`, `BlueprintRegistry.cs`
- `Systems/BlueprintTickSystem.cs`, `Systems/BlueprintMaintenanceSystem.cs`
- `Components/BlueprintBlackboard1024.cs` (ComponentId 205)
- `Components/BlueprintBlackboard4096.cs` (ComponentId 206)
- `Components/BlueprintBlackboard16384.cs` (ComponentId 207)
- `Partitioning/BlueprintBlackboardHeader.cs` (MagicValue = 0x42503132u)
- `Partitioning/BlueprintBlackboardPartitions.cs`
- `Partitioning/BlueprintSlotEntry.cs`

### Hrot.Blueprints.Core additions

- `BlueprintCompiler.cs` -- stub (Phase 3 not yet implemented)
- `InMemoryRoslynCompiler.cs` -- stub (Phase 3 not yet implemented)
- Updated `.csproj` with `ProjectReference` to `Fdp.Toolkits`

### TH-008: CapturingDebugSession

- `Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs` -- test helper implementing both `IBlueprintProbeSink` and `IBlueprintDebugSession`
- `Hrot.Blueprints.Tests/Debug/CapturingDebugSessionTests.cs` -- 8 contract tests (SC1-SC8)

### TH-009: TestData Infrastructure

Valid JSON test assets (9 files):
- `TestAssets/LibraryMath.bp.json`
- `TestAssets/InstanceCounter.bp.json`
- `TestAssets/InstanceCounterV1ModifiedBody.bp.json`
- `TestAssets/InstanceCounterV2WithBonus.bp.json`
- `TestAssets/HealthRegen.bp.json`
- `TestAssets/HasVisibleTarget.bp.json`
- `TestAssets/MoveToAndFire.bp.json`
- `TestAssets/DoorActor.bp.json`
- `TestAssets/DoorSensor.bp.json`

Invalid JSON test assets (4 files):
- `TestAssets/Invalid/ConditionWithRunning.bp.json`
- `TestAssets/Invalid/ConditionWithDelay.bp.json`
- `TestAssets/Invalid/AiPrimitiveParamsTooLarge.bp.json`
- `TestAssets/Invalid/InstanceStateExceedsLargestTier.bp.json`

Snapshot directories (with `.gitkeep`):
- `Snapshots/Schedule/.gitkeep`
- `Snapshots/Emit/.gitkeep`
- `Snapshots/DebugMap/.gitkeep`

Test helper files:
- `TestEventDefinitions.cs` -- `HitEvent` struct with `[EventId(90010)]`
- `TestData.cs` -- `LoadAsset`, `LoadSnapshot`, `ReadOrRegenerateSnapshot`, `ResolveTestAssetsDir`, `ResolveSnapshotsDir`
- `SampleAssetLoadTests.cs` -- 11 tests (9 theory + 2 fact)

---

## Issues Encountered and Resolved

1. **`ISimulationView` namespace** -- Located in `Fdp.ModuleHost.Abstractions`, not `Fdp.Core`. Fixed with correct `using` directive.

2. **`IBlueprintDebugSession` naming conflict** -- Interface cannot have both `event Action<PinValueChanged>? OnPinValueChanged` and inherited generic method `void OnPinValueChanged<T>(...)`. Resolved by renaming the event to `OnPinValueChangedEvent`.

3. **JSON schema discrepancies in instructions** -- The instructions contained incorrect JSON examples (`"$type"` discriminator, nested `callablePeers` objects, `typeRef.clrTypeName`). The actual `BlueprintAsset` deserialization uses `"kind"` (lowercase) as discriminator, `"Type": { "TypeId": "..." }` for type refs, and `CallablePeers` as a plain `List<Guid>`. All JSON test assets were created using the correct schema.

4. **Invalid hex literal** -- Instructions showed `0xBP_1234U` which is not valid C#. Used `0x42503132u` (ASCII for "BP12") instead.

5. **Snapshot directory location** -- Snapshots must be at the test project root `Snapshots/` (not inside `TestAssets/`). The `csproj` `<Content>` item correctly points to `Snapshots\**\*`.

6. **`TestData.ReadOrRegenerateSnapshot`** -- Instructions suggested using `Xunit.Assert.Equal` in the static helper, which is inappropriate. Used `throw new Exception(...)` instead for portability outside test context.

---

## Build Verification

All three projects build clean:
- `Fdp.Toolkits` -- 0 errors, 0 errors
- `Hrot.Blueprints.Core` -- 0 errors, 0 errors
- `Hrot.Blueprints.Tests` -- 0 errors, 0 errors

Final test run: **76 passed, 1 skipped, 0 failed** (net8.0)
