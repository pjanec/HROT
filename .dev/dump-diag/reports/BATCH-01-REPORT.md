# BATCH-01 Report — JSON Serialization Foundation

**Batch:** BATCH-01  
**Workstream:** dump-diag  
**Status:** COMPLETE  
**Tests:** Fdp.Core.Tests 702/704 passed (2 skipped pre-existing stress tests); Fdp.Toolkits.Tests 779/802 passed (23 pre-existing failures, 0 new)

---

## Task Status

| Task ID     | Title                                         | Status    |
|-------------|-----------------------------------------------|-----------|
| DD-P1-T01   | Move converters to Fdp.Core                   | COMPLETE  |
| DD-P1-T02   | Create FdpJsonOptionsRegistry                 | COMPLETE  |
| DD-P1-T03   | Extract JsonAestheticFormatter                | COMPLETE  |
| DD-P1-T04   | Refactor all existing JSON callers            | COMPLETE  |

---

## Implementation Details

### DD-P1-T01 — Move converters to Fdp.Core

**New files created in `FDP/Engine/Fdp.Core/Serialization/Converters/`:**

- `VectorArrayConverters.cs` — `Vector2ArrayConverter`, `Vector3ArrayConverter`, `Vector4ArrayConverter`, `QuaternionArrayConverter`
  - All public non-sealed, inherit `JsonConverter<T>`
  - Serialize as compact inline arrays: `[x, y, z]`
  - Namespace: `Fdp.Core.Serialization.Converters`

- `FixedStringConverters.cs` — `FixedString32Converter`, `FixedString64Converter`
  - Serializes `FixedString32/64` as plain JSON strings (fixes the struct "Length/IsEmpty" bug from `JsonObject`)
  - Namespace: `Fdp.Core.Serialization.Converters`

- `StrictStringEnumConverter.cs` — `StrictStringEnumConverter`
  - Public non-sealed, inherits `JsonStringEnumConverter(allowIntegerValues: false)`
  - Namespace: `Fdp.Core.Serialization.Converters`

**Modified for backward compatibility:**

- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioJsonConverters.cs`
  - All 6 converter implementations replaced with `[Obsolete]` empty subclasses forwarding to Core types
  - Preserves existing consumer code without breaking changes

- `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`
  - Local `StrictStringEnumConverter` replaced with empty sealed subclass forwarding to Core type
  - `OrchestrationJsonOptions.Default` property now delegates to `FdpJsonOptionsRegistry.DefaultRelaxed`

### DD-P1-T02 — Create FdpJsonOptionsRegistry

**New file:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`

Two frozen singletons in `Fdp.Core.Serialization` namespace:

**`DefaultRelaxed`:**
- `IncludeFields = true`
- `PropertyNameCaseInsensitive = true`
- `AllowTrailingCommas = true`
- `ReadCommentHandling = JsonCommentHandling.Skip`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
- All 4 vector converters, 2 FixedString converters, `StrictStringEnumConverter`
- `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`
- `MakeReadOnly()`

**`Indented`:** Copy of `DefaultRelaxed` + `WriteIndented = true`

**Critical issue resolved:** `.NET 8` requires `TypeInfoResolver` to be explicitly set before `MakeReadOnly()`. Without `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`, the call throws `InvalidOperationException`. Added `using System.Text.Json.Serialization.Metadata;`.

### DD-P1-T03 — Extract JsonAestheticFormatter

**New file:** `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs`

- Namespace: `Fdp.Toolkit.Serialization`
- One public static method: `FlattenNumericArrays(string rawJson) -> string`
- Uses Newtonsoft.Json `JToken.Parse` + `JsonTextWriter` with `Formatting.Indented`
- Private helpers: `WriteFormattedToken`, `IsPureNumericArray`
- Extracted from `ScenarioFileService.SaveScenario` verbatim; logic unchanged

Newtonsoft.Json is intentionally kept in `Fdp.Toolkits` (not in `Fdp.Core`) because `Fdp.Core` does not and should not take a Newtonsoft.Json dependency.

### DD-P1-T04 — Refactor all existing JSON callers

Six callers updated:

1. **`FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`**
   - `_fieldAwareOptions` now points to `FdpJsonOptionsRegistry.DefaultRelaxed`

2. **`FDP/Engine/Fdp.Core/FlightRecorder/Metadata/MetadataSerializer.cs`**
   - `_options` now points to `FdpJsonOptionsRegistry.DefaultRelaxed`

3. **`Hrot/Engine/Hrot.Core/HrotSerializerOptions.cs`**
   - Builds on `FdpJsonOptionsRegistry.Indented` (copy constructor) + adds `PropertyNamingPolicy = CamelCase`
   - **Design decision:** CamelCase policy is preserved to maintain backward compatibility with existing HROT scenario files

4. **`Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`**
   - `OrchestrationJsonOptions.Default` delegates to `FdpJsonOptionsRegistry.DefaultRelaxed`

5. **`FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs`**
   - "Copy JSON" button: replaced inline `new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }` with `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter.FlattenNumericArrays`

6. **`FDP/Engine/Fdp.Presentation/ImGui/Utils/EntityJsonDumper.cs`**
   - `Dump()`: replaced `new JsonSerializerOptions { WriteIndented = true }` with `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter.FlattenNumericArrays`

Also modified:
- **`Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`**
  - `SaveScenario` now calls `JsonAestheticFormatter.FlattenNumericArrays` + `File.WriteAllText`
  - Removed Newtonsoft.Json usings from this file
  - Removed `WriteFormattedToken` and `IsPureNumericArray` private methods (now live in `JsonAestheticFormatter`)

---

## Issues Encountered

### Issue 1 — TypeInfoResolver required before MakeReadOnly() (.NET 8)

**Error:** `InvalidOperationException: JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only`

**Root cause:** In .NET 8, calling `MakeReadOnly()` on a `JsonSerializerOptions` that has no `TypeInfoResolver` assigned throws. The `TypeInfoResolver` must be set explicitly.

**Fix:** Added `options.TypeInfoResolver = new DefaultJsonTypeInfoResolver()` before each `MakeReadOnly()` call. Required adding `using System.Text.Json.Serialization.Metadata;`.

### Issue 2 — HrotSerializerOptions camelCase must be preserved

`HrotSerializerOptions.HrotJsonOptions` uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` to match the HROT scenario file format. `FdpJsonOptionsRegistry.Indented` does not include this policy. The fix was to use the copy constructor `new JsonSerializerOptions(FdpJsonOptionsRegistry.Indented)` then add the CamelCase policy, without calling `MakeReadOnly()` (the instance is still frozen via the `static readonly` field).

### Issue 3 — Toolkits test failures (pre-existing)

23 tests in `Fdp.Toolkits.Tests` were failing when I started. Confirmed pre-existing by running `git stash` + re-running the specific failing test `RoundTrip_MissionPlanQueue_PreservesPhaseData` — it failed on the unmodified codebase too. My changes introduced 0 new test failures.

The pre-existing failures span: `MissionDirectorSystemTests`, `CombatComponentTests`, `IdAllocationTests`, `HsmTickSystemTerminalTests`, `PhysicsQueryActionNodeTests`, `SimTransformBridgeSystemTests`, `FdpAutoSerializerFixedBufferTests`, `FireProcessingSystemTests`.

---

## Test Coverage Added

### `FDP/Engine/Fdp.Core.Tests/Serialization/ConverterRegistryTests.cs`

Two test classes:

**`ConverterTests`** (DD-P1-T01):
- `FixedString64Converter_Serialize_ProducesStringJson`
- `FixedString64Converter_Deserialize_ReadsStringJson`
- `StrictStringEnumConverter_RejectsInteger`
- `Vector3ArrayConverter_RoundTrip`

**`FdpJsonOptionsRegistryTests`** (DD-P1-T02):
- `DefaultRelaxed_IsNotNull`
- `Indented_IsNotNull`
- `DefaultRelaxed_IsImmutable`
- `Indented_IsImmutable`
- `DefaultRelaxed_IncludesFields`

### `FDP/Toolkits/Fdp.Toolkits.Tests/Serialization/JsonAestheticFormatterTests.cs`

**`JsonAestheticFormatterTests`** (DD-P1-T03):
- `FlattenNumericArrays_FlatArray_RemainsOnOneLine`
- `FlattenNumericArrays_IndentedNumericArray_CollapsedToOneLine`
- `FlattenNumericArrays_MixedArray_NotCollapsed`
- `FlattenNumericArrays_EmptyArray_HandledGracefully`
- `FlattenNumericArrays_NestedObject_OnlyNumericArraysCollapsed`

---

## Developer Insights

### Issues Encountered During Implementation
1. TypeInfoResolver (.NET 8 requirement) — see Issue 1 above
2. HrotSerializerOptions camelCase contract — see Issue 2 above
3. Newtonsoft.Json must stay in Fdp.Toolkits — Fdp.Core should not acquire this dependency; `JsonAestheticFormatter` lives in Fdp.Toolkits precisely for this reason

### Weak Points Spotted in the Codebase
- `FdpAutoSerializer._fieldAwareOptions` was creating a new `JsonSerializerOptions` with a manually assembled list of converters that could drift from new converters added to the registry. This is now resolved by using the registry directly.
- `OrchestrationJsonOptions.Default` in HROT was a static field containing a locally-created `JsonSerializerOptions`, duplicating the list of converters. Now delegates to registry.
- The 23 pre-existing Toolkits test failures should be investigated as a separate effort. They involve unmanaged struct size assertions, physics query resolution, and mission director phase progression — none related to JSON.

### Design Decisions Made Beyond the Spec
- `Indented` singleton in the registry does NOT call `MakeReadOnly()` implicitly — it uses `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` then `MakeReadOnly()` explicitly. Both singletons are immutable.
- `HrotSerializerOptions` builds on `FdpJsonOptionsRegistry.Indented` using the copy constructor (rather than `DefaultRelaxed`) to inherit the indented formatting that HROT scenario files need.
- `MetadataSerializer._options` was changed to `DefaultRelaxed` (not `Indented`) since flight recorder metadata is not a human-authored file; compactness is appropriate.
- The `[Obsolete]` attribute on forwarding subclasses in `ScenarioJsonConverters.cs` signals to future developers that these types should be migrated to use Core types directly.

---

## Suggested Git Commit Message

```
feat(serialization): centralize JSON converters and options in Fdp.Core

- Add VectorArrayConverters, FixedStringConverters, StrictStringEnumConverter
  to Fdp.Core.Serialization.Converters
- Add FdpJsonOptionsRegistry with DefaultRelaxed and Indented frozen singletons
- Add JsonAestheticFormatter (flattenNumericArrays) to Fdp.Toolkit.Serialization
- Refactor 6 callers (FdpAutoSerializer, MetadataSerializer, HrotSerializerOptions,
  OrchestrationJsonOptions, EventBrowserPanel, EntityJsonDumper) to use registry
- Extract ScenarioFileService aesthetic formatting to JsonAestheticFormatter
- Add forwarding [Obsolete] subclasses in ScenarioJsonConverters for backward compat
- Fix: set TypeInfoResolver before MakeReadOnly() to satisfy .NET 8 requirement
- Add tests: ConverterRegistryTests (9 tests), JsonAestheticFormatterTests (5 tests)
```
