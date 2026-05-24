# BATCH-16 Report

**Tasks:** GZ041 (Phase A), GZ042 (Phase B)  
**Status:** Complete

---

## Files Created

| File | Description |
|------|-------------|
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Fdp.Diagnostics.Contracts.csproj` | New project — references only Fdp.Core |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/Rgba32.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/CoordinateSpace.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/SizeMode.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PickToken.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PipelineTarget.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitiveShape.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/ScreenAnchor.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/StringInternMap.cs` | Moved from Fdp.Toolkits |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj` | Standalone test project |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` | SC-GZ041-3 test |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/Fdp.Diagnostics.Network.csproj` | New project — references Contracts + CycloneDDS |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/DebugPrimitivesBatch.cs` | Moved from Fdp.Toolkits/Network |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoUiState.cs` | Moved from Fdp.Toolkits/Network |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/StringInternBatch.cs` | Moved from Fdp.Toolkits/Network |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/IDdsReader.cs` | Moved from Fdp.Toolkits/Network |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/IDdsWriter.cs` | Moved from Fdp.Toolkits/Network |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs` | Moved from Hrot.Network.NED — namespace changed |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionEventKind.cs` | Moved from Hrot.Network.NED — namespace changed |

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` | Added refs to Fdp.Diagnostics.Contracts and Fdp.Diagnostics.Network |
| `FDP/FDP.sln` | Added Contracts, Contracts.Tests, Network projects |
| `Hrot/Network/Hrot.Network.NED/Hrot.Network.NED.csproj` | Added ref to Fdp.Diagnostics.Network |
| `FDP/ExtDeps/FastCycloneDds/tools/CycloneDDS.CodeGen/CycloneDDS.targets` | Added `CycloneDdsDisableCodeGen` opt-out condition to CycloneDDSCodeGen and IncludeCycloneDDSGeneratedFiles targets |
| `.dev/gizmos-1/TASK-TRACKER.md` | Marked GZ041 and GZ042 as done |

## Files Deleted

| File | Reason |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/Rgba32.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/CoordinateSpace.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/SizeMode.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/PickToken.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/PipelineTarget.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitive.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitiveShape.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/ScreenAnchor.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IDebugDrawBuilder.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs` | Moved to Contracts |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/DebugPrimitivesBatch.cs` | Moved to Network |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/GizmoUiState.cs` | Moved to Network |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/StringInternBatch.cs` | Moved to Network |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsReader.cs` | Moved to Network |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsWriter.cs` | Moved to Network |
| `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionBatch.cs` | Moved to Fdp.Diagnostics.Network with namespace change |
| `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEventKind.cs` | Moved to Fdp.Diagnostics.Network with namespace change |

---

## Build Result

`dotnet build IOS-IG-SimHost.sln --no-incremental`: **0 Error(s), Build succeeded**

---

## Test Results

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| Fdp.Diagnostics.Contracts.Tests | 1 | 0 | SC-GZ041-3 passes standalone |
| Hrot.Network.NED.Tests | 81 | 0 | All GZ037/GZ038 tests pass |
| Fdp.Toolkits.Tests | 934 | 26 | Pre-existing failures |
| Hrot.IG.Tests | 465 | 5 | Pre-existing (instructions said ~4; extra is SC_GZ015_2_MarshalSizeOf unrelated to this batch) |
| Fdp.Presentation.Tests | 298 | 3 | Pre-existing failures |
| Hrot.SimHost.Tests | 560 | 20 | Pre-existing failures |

---

## Git Commits

- **FDP submodule:** `622797e` — "GZ041/GZ042: Fdp.Diagnostics.Contracts and Fdp.Diagnostics.Network assemblies"
- **Root repo:** `b8e3294` — "GZ041/GZ042: Update Hrot references for new Diagnostics assemblies"

---

## Deviations

1. **`Fdp.Diagnostics.Contracts.csproj` InternalsVisibleTo extended:** Added `Fdp.Presentation.Tests` and `Hrot.IG.Tests` beyond the two listed in instructions. Required because `DebugPrimitiveBuffer.Append` is `internal` and those test assemblies call it directly. Without this, the solution would fail to compile with 2 CS1929 errors in `Fdp.Presentation.Tests`.

2. **`CycloneDDS.targets` modified (minimal):** Added `Condition="'$(CycloneDdsDisableCodeGen)' != 'true'"` to `CycloneDDSCodeGen` and `IncludeCycloneDDSGeneratedFiles` targets to support the opt-out property referenced in the instructions. No existing behavior changed (property defaults to empty/false).

3. **`Fdp.Diagnostics.Network` uses `CycloneDdsDisableCodeGen=true`:** As anticipated by the instructions. The `DebugPrimitive` struct (in `Fdp.Diagnostics.Contracts`) lacks `[DdsStruct]` and cannot be attributed from outside its assembly without adding CycloneDDS.Schema to Contracts (which would violate the "no CycloneDDS" constraint). Disabling codegen is the accepted workaround per the instructions.

4. **NED test files unchanged:** Both `GizmoInteractionTranslatorTests.cs` and `GizmoIngressTranslatorTests.cs` still have `using Hrot.Network.NED.Gizmos;` — this is correct because `GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`, and `DebugPrimitivesIngressTranslator` remain in that namespace. The `GizmoInteractionBatch`/`GizmoInteractionEventKind` types now resolve via `using Fdp.Toolkit.Diagnostics.Gizmos.Network;` which those files already had.

---

## CycloneDDS Codegen Status

- **Fdp.Diagnostics.Network:** Codegen **disabled** (`CycloneDdsDisableCodeGen=true`). No `obj/Generated` folder. Types compile as plain partial structs. This is intentional — see deviation #3 above.
- **Hrot.Network.NED:** Codegen **enabled** and runs normally. `obj/Generated` contains all standard NED DDS generated files. `GizmoInteractionBatch` is no longer in NED source files so its `.g.cs` is no longer generated here — it will be generated in `Fdp.Diagnostics.Network` once `[DdsStruct]` on `DebugPrimitive` is addressed (future task).
