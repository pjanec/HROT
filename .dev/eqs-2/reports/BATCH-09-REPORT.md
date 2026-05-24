# BATCH-09 REPORT

## Task
**EQS-022** -- ImGui inspector and gizmo projector

## Files Created

| File | Status |
|------|--------|
| `Hrot/Subsystems/Hrot.IG/Gizmos/EqsGizmoSettings.cs` | NEW |
| `Hrot/Subsystems/Hrot.IG/Gizmos/EqsSensorGizmo.cs` | NEW |
| `Hrot/Subsystems/Hrot.IG/Gizmos/EqsCognitiveBufferRenderer.cs` | NEW |
| `Hrot/Subsystems/Hrot.IG.Tests/Eqs/EqsVisualizersTests.cs` | NEW |

## Files Modified

None.

## Test Results

### New visualizer unit tests (T-VIS1 through T-VIS5)

| Test | Status |
|------|--------|
| T-VIS1 `EqsSensorGizmo_HasGizmoProjectorAttribute_WithCorrectTypes` | PASS |
| T-VIS2 `EqsCognitiveBufferRenderer_HasImGuiRendererAttribute_ForCognitiveBuffer` | PASS |
| T-VIS3 `EqsCognitiveBufferRenderer_GetSummary_ReadyBuffer_ReturnsCorrectString` | PASS |
| T-VIS4 `EqsCognitiveBufferRenderer_GetSummary_NotReady_ReturnsAwaitingString` | PASS |
| T-VIS5 `EqsGizmoSettings_KeyHashes_AreDistinct` | PASS |

**Total new tests: 5/5 passed.**

### Regression -- FDP EQS unit tests
`Fdp.Toolkits.Tests` (filter `~Eqs`): **49/49 passed** (unchanged).

### Regression -- Hrot EQS integration tests
`Hrot.ClusterRunner.Integration.Tests` (filter `~Eqs`): **21/21 passed** (unchanged).

## Build Results

| Target | Result |
|--------|--------|
| `Hrot.IG.csproj` (`--no-restore`) | Build succeeded, 0 warnings, 0 errors |
| `Hrot.IG.Tests.csproj` (`--no-restore`) | Build succeeded, 0 warnings, 0 errors |

## Deviations from Instructions (with justification)

### 1. `buf[i]` indexer replaced with `buf.GetSpanRO()[i]`
**Instruction:** "EqsCognitiveBuffer access pattern: `buf[i]` (indexer exists from BATCH-01)"
**Reality:** No `this[int]` indexer exists on `EqsCognitiveBuffer`. The struct exposes `GetSpanRO()` returning `ReadOnlySpan<EqsResult>` and `GetSpanRW()` for mutation. All existing integration tests use `buffer.GetSpanRO()[i]`.
**Fix:** Used `buffer.GetSpanRO()[i]` in both `EqsSensorGizmo.Draw` and `EqsCognitiveBufferRenderer.RenderValue`.

### 2. `LastUpdateEpoch` field omitted from renderer
**Instruction:** `ImGuiApi.TextUnformatted(string.Format("Refresh Epoch    : {0}", buf.LastUpdateEpoch));`
**Reality:** `EqsCognitiveBuffer` has no `LastUpdateEpoch` field. The struct only has `Count`, `LastUpdateTick`, and `Results`. Compiling `buf.LastUpdateEpoch` would be a CS0117 error.
**Fix:** Omitted the `LastUpdateEpoch` line. Only `LastUpdateTick` is displayed.

### 3. `using Fdp.Toolkit.Replication.Components;` replaced with `using Fdp.Core;`
**Instruction:** Listed `Fdp.Toolkit.Replication.Components  // SimTransform` as a required using.
**Reality:** `SimTransform` is declared in namespace `Fdp.Core` (`Fdp.Core/CoreComponents/SimComponents.cs`), not in `Fdp.Toolkit.Replication.Components`. Using the wrong namespace would cause a compilation error.
**Fix:** Used `using Fdp.Core;` (consistent with `ProjectilePresentationGizmo.cs` pattern).

### 4. `FixedString32` qualified as `Fdp.Core.FixedString32`
**Instruction:** Used bare `new FixedString32(...)`.
**Reality:** `Hrot.IG.csproj` (via `GizmoMap.Contracts`) exposes both `Fdp.Core.FixedString32` and `Fdp.Toolkit.Diagnostics.Gizmos.FixedString32`, making the bare name ambiguous (CS0104). With `TreatWarningsAsErrors` active and the error being an actual error, the build failed.
**Fix:** Used `new Fdp.Core.FixedString32(...)` -- same pattern as `SpatialGridGizmo.cs`.

### 5. Tests T-VIS3 and T-VIS4: `IsReady` initializer replaced with `LastUpdateTick`
**Instruction:** `new EqsCognitiveBuffer { IsReady = true, Count = 3, LastUpdateTick = 1 }` and `{ IsReady = false, Count = 0 }`.
**Reality:** `IsReady` is a computed read-only property (`LastUpdateTick > 0`), not a settable field. Using it in an object initializer is a CS0200 compile error.
**Fix:** Used `{ Count = 3, LastUpdateTick = 1 }` (IsReady == true implicitly) and `{ Count = 0, LastUpdateTick = 0 }` (IsReady == false implicitly).

### 6. T-VIS2: `Assert.True(attrs.Any(...))` replaced with `Assert.Contains(...)`
**Instruction:** Used `Assert.True(attrs.Any(a => a.TargetType == typeof(EqsCognitiveBuffer)))`.
**Reality:** xUnit analyzer xUnit2012 warns against using `Assert.True()` for collection membership checks. The project has `<NoWarn>` entries but not xUnit2012.
**Fix:** Used `Assert.Contains(attrs, a => a.TargetType == typeof(EqsCognitiveBuffer))` which is the idiomatic xUnit form and suppresses the warning.

### 7. No `[Collection("EqsVisualizersTests")]` on test class
**Instruction:** "Add `[Collection("EqsVisualizersTests")]` on the test class if needed".
**Constraint:** "Do NOT add `[Collection("EqsIntegrationTests")]` to unit tests -- these are NOT integration tests".
**Decision:** These are pure reflection/struct unit tests with no shared state or I/O. No collection attribute is needed. Omitted per the spirit of the constraint (unit tests should not carry collection isolation unless required).

## Summary

All 4 files created, all 5 new tests pass, no EQS regressions in 49 unit tests or 21 integration tests. Both build targets succeed with 0 errors and 0 warnings.
