# WHEN-BATCH-01 Report — Schema Foundation + EqsSensorHandle Registration

## M0-T1 Confirmation

All five EQS-side API points confirmed against the current codebase (post-BATCH-16 commit `aa5733e0`):

1. **`EqsCognitiveBuffer.LastUpdateTimeSeconds` (float)** — present at `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` in `EqsCognitiveBuffer` struct, added in EQS-033.
2. **`EqsSensorHandle` wrapper struct in `FDP.Eqs`** — present at `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs`, added in EQS-037.
3. **`view.IsAlive(Entity)` exists** — confirmed as the standard liveness API via codebase grep across ECS usages.
4. **`EqsResult` field naming** — `EntityId` (long), `PositionX` / `PositionY` (float) confirmed in `EqsComponents.cs`.
5. **`IEntityCommandBuffer.CreateEntity()` and `AddComponent<T>(Entity, T)`** — confirmed present and used in `EqsLifecycleNodes.cs` (BATCH-16 additions).

All five API points are in-tree and stable. The Blueprint schema types introduced in this batch reference them correctly.

---

## Summary of Files Changed

| File | Change |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj` | Changed `TargetFramework` from `netstandard2.0` to `TargetFrameworks>netstandard2.0;net8.0`; added conditional `ProjectReference` to `Fdp.Toolkits` (net8.0 TFM only); added `CycloneDdsDisableCodeGen=true` to prevent DDS IDL generation for compiler types |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` | Added `#if NET8_0_OR_GREATER` guard on `using Fdp.Toolkit.ReplayBrowser.Search;`; added 3 `[JsonDerivedType]` attributes to `Node` base (`WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode`); appended all new schema types: `WhenNode`, `WhenMode`, `WhenEdge`, `ValueChangedPayload`, `ValueChangedSource`, `EventFiredPayload`, `EventTargetFilter`, `PayloadCondition`, `ComparisonOperator`, `ConditionMetPayload` (with `#if NET8_0_OR_GREATER` guard on `SearchPredicateDto?`), `EqsResultPayload`, `EqsTrigger`, `ReadEqsResultNode`, `SpawnEqsSensorNode` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs` | Added `"FDP.Eqs.EqsSensorHandle"` entry: `IsUnmanaged=true`, `SizeBytes=8` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/SchemaReflectionTests.cs` | Added `using Hrot.Blueprints.Core.Compiler.Catalogs;`; renamed `ConcreteNodeSubtypeCount_Is19` → `ConcreteNodeSubtypeCount_Is22` and updated assertion `19→22`; added 3 `[InlineData]` entries for `WhenNode`/`ReadEqsResultNode`/`SpawnEqsSensorNode`; added `EqsSensorHandle_IsPermittedVariableType` test |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AssetJsonRoundTripTests.cs` | Added `WhenNode_AllModes_RoundTrip`, `ReadEqsResultNode_RoundTrip`, `SpawnEqsSensorNode_RoundTrip` |

---

## Test Results

| Metric | Baseline (pre-batch) | After batch |
|---|---|---|
| Passed | 392 | 399 |
| Failed | 98 | 98 |
| Skipped | 7 | 7 |
| Total | 497 | 504 |

The 98 pre-existing failures are unrelated to this batch (they are `BlueprintDispatchKind` string-enum deserialization tests that fail due to missing `JsonStringEnumConverter` in `BlueprintJsonServices` — a known pre-existing issue). No regressions introduced.

**New tests added (all passing):**
- `SchemaReflectionTests.ConcreteNodeSubtypeCount_Is22`
- `SchemaReflectionTests.DiscriminatorRoundTrip_EachNodeKind` — 3 new `[InlineData]` cases (`When`, `ReadEqsResult`, `SpawnEqsSensor`)
- `SchemaReflectionTests.EqsSensorHandle_IsPermittedVariableType`
- `AssetJsonRoundTripTests.WhenNode_AllModes_RoundTrip`
- `AssetJsonRoundTripTests.ReadEqsResultNode_RoundTrip`
- `AssetJsonRoundTripTests.SpawnEqsSensorNode_RoundTrip`

---

## Deviations from Instructions

### 1. Compiler project multi-targeted instead of single-TFM project reference

**Instruction:** "If `Hrot.Blueprints.Compiler.csproj` does not already reference `Fdp.Toolkits`, add one."

**Issue:** `Hrot.Blueprints.Compiler` targets `netstandard2.0` for use by the Roslyn source generator (`Hrot.Blueprints.Generators`, which must be `netstandard2.0` and has `EnforceExtendedAnalyzerRules=true`). `Fdp.Toolkits` targets `net8.0` only. A direct `<ProjectReference>` from `netstandard2.0` to `net8.0` produces a hard `NU1201` error.

**Resolution:** Changed `TargetFramework` to `TargetFrameworks>netstandard2.0;net8.0` and added the project reference conditionally via `Condition="'$(TargetFramework)' == 'net8.0'"`. The Generators project continues to consume the `netstandard2.0` build (no changes required there). The Core project and Tests project (both `net8.0`) now consume the `net8.0` build of the Compiler.

### 2. `ConditionMetPayload.Condition` type-guarded by `#if`

**Instruction:** Field must be `SearchPredicateDto? Condition`.

**Issue:** The `SearchPredicateDto` type is unavailable in the `netstandard2.0` build of the Compiler (no Fdp.Toolkits reference there).

**Resolution:** Field uses `#if NET8_0_OR_GREATER`:
- `net8.0` build: `SearchPredicateDto? Condition` — fully typed, round-trips correctly.
- `netstandard2.0` build (Generators only): `object? Condition` — erased type, acceptable because the Generators do not perform JSON round-trips on ConditionMetPayload at compile-generation time.

The Tests and Core projects consume the `net8.0` build, so all round-trip tests use the typed `SearchPredicateDto?` path.

### 3. `CycloneDdsDisableCodeGen=true` added to Compiler project

The `buildTransitive` targets from `cyclonedds.net` propagate to the Compiler project via the Fdp.Toolkits dependency chain. The code generator tried to emit IDL for the new Blueprint enums and produced a collision on the `None` value. Since the Compiler project does not define DDS topics, `CycloneDdsDisableCodeGen=true` was added to suppress generation.

---

## Success Criteria Status

| Criterion | Status |
|---|---|
| `StaticTypeRegistry` resolves `"FDP.Eqs.EqsSensorHandle"` to unmanaged 8-byte type | PASS |
| `EqsSensorHandle_IsPermittedVariableType` passes | PASS |
| `ConcreteNodeSubtypeCount_Is22` passes | PASS |
| `DiscriminatorRoundTrip_EachNodeKind` includes `"When"`, `"ReadEqsResult"`, `"SpawnEqsSensor"` — all pass | PASS |
| Round-trip tests `WhenNode_AllModes_RoundTrip`, `ReadEqsResultNode_RoundTrip`, `SpawnEqsSensorNode_RoundTrip` pass | PASS |
| Solution builds (all projects) | PASS |
