# BATCH-12 REPORT — Fault-tolerant codegen: invalid asset → diagnostic, not build break

**Task:** TASK-BT-12 (Fix A)
**Date:** 2026-06-12
**Status:** ✅ Complete

## Summary

Made BTree codegen **fault-tolerant per asset**. An asset that cannot emit valid topology (unbound Action/Condition leaf) is now **skipped** and reported as a **non-build-breaking Warning** (BTREE0002) — the assembly still builds, other assets are unaffected.

## Changes

### 1. `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`

**`EmitAction` (lines 439-443):** Changed the unbound-action path from emitting an uncompilable `.Action(visualId: …)` call to throwing `InvalidOperationException`:
```csharp
if (p == null || string.IsNullOrEmpty(p.MethodFqn))
    throw new InvalidOperationException(
        $"Action node {node.VisualId:D} is unbound (no method) — bind a method in the editor.");
```

**`EmitCondition` (lines 465-469):** Symmetric for Condition:
```csharp
if (p == null || string.IsNullOrEmpty(p.MethodFqn))
    throw new InvalidOperationException(
        $"Condition node {node.VisualId:D} is unbound (no method) — bind a method in the editor.");
```

- Guard covers both `null` payload AND empty `MethodFqn` (a payload object with no method string).
- Only reachable nodes trigger the throw (the walk from entry → children → leaves). Disconnected unbound nodes are never emitted and do not throw.
- `EmitWait`/`EmitSubtree` unchanged — they already emit valid calls for null payloads.

### 2. `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs`

- Added `CodegenWarningId = "BTREE0002"` constant.
- Added `MakeCodegenWarningDiagnostic()` method — creates a Roslyn `DiagnosticDescriptor` with:
  - id: `BTREE0002`
  - title: `"BTree asset skipped (codegen validation)"`
  - messageFormat: `"Skipped '{0}': {1}. Fix the asset in the editor."`
  - category: `"BTreeJsonGenerator"`
  - defaultSeverity: `Warning`
- Changed `EmitTopologyCore` catch block to use `MakeCodegenWarningDiagnostic` (Warning instead of Error).
- Changed bridge codegen catch block to use `MakeCodegenWarningDiagnostic` (Warning instead of Error).
- Deserialize-failure path left as BTREE0001 Error (unchanged).

### 3. Tests

#### Persistence tests (`Hrot.AiEditor.Persistence.Tests/Emit/BTreeEmitCoreValidationTests.cs`) — 5 new tests

| Test | Result |
|------|--------|
| `EmitTopologyCore_ReachableUnboundAction_ThrowsInvalidOperationException` | ✅ Pass |
| `EmitTopologyCore_ReachableUnboundCondition_ThrowsInvalidOperationException` | ✅ Pass |
| `EmitTopologyCore_ReachableAction_EmptyMethodFqn_ThrowsInvalidOperationException` | ✅ Pass |
| `EmitTopologyCore_DisconnectedUnboundAction_DoesNotThrow` | ✅ Pass |
| `EmitTopologyCore_ReachableBoundAction_DoesNotThrow` | ✅ Pass |

#### Generator tests (`Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs`) — 6 new tests

| Test | Result |
|------|--------|
| `Generator_UnboundActionAsset_DoesNotEmitSource_AndReportsWarning` | ✅ Pass |
| `Generator_UnboundActionAsset_OutputCompilation_HasNoErrors` | ✅ Pass |
| `Generator_UnboundConditionAsset_DoesNotEmitSource_AndReportsWarning` | ✅ Pass |
| `Generator_ValidAsset_EmitsTopologyAndBridge_NoWarning` | ✅ Pass |
| `Generator_UnboundActionAsset_DoesNotSuppressSiblingValidAsset` | ✅ Pass |
| (Integration test: valid + unbound sibling — valid still emits, only Warning for unbound) | ✅ Pass |

## Test results

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| `Hrot.AiEditor.Persistence.Tests` | **118** | 0 | All pass |
| `Hrot.AiEditor.Generators.Tests` | **44** | 2 | 2 pre-existing `MigrationEquivalenceTests` failures ($meta serialization — unrelated to this batch) |
| `Hrot.BTree.Editor.Tests` | **493** | 0 | All pass |

All new tests pass. No regressions in any of the three named test projects.

## Solution build

```
dotnet build IOS-IG-SimHost.sln → 0 Error(s), 22 Warning(s)
```

- All 22 warnings are pre-existing (xUnit2013, CS0618 obsolete, CS8602 nullable, NU1903 vulnerability).
- **No BTREE0002 warnings fire** for committed assets (all are valid).
- `Hrot.AI.Behaviors` builds cleanly.

## ⚠️ Critical finding: TreatWarningsAsErrors

**`Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj` line 4:**
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

This means **if a BTREE0002 Warning is emitted for a real unbound asset in the `Hrot.AI.Behaviors` project, it WILL be escalated back to an Error** and the build will still break. The `Directory.Build.props` at the repo root does NOT have TWAE.

The generator-level test proves the diagnostic is correctly a Warning — but the project-level TWAE overrides this. The fix is correct at the generator layer; the project TWAE requires a separate decision (e.g., `<WarningsNotAsErrors>BTREE0002</WarningsNotAsErrors>` to carve out just this diagnostic).

**Per the working agreement, TWAE was NOT globally disabled.** This is flagged for the architect/team to decide.

## Diffs

### `BTreeEmitCore.cs`
- `EmitAction`: null/empty guard → `throw InvalidOperationException` (was: emit uncompilable call)
- `EmitCondition`: symmetric

### `BTreeJsonGenerator.cs`
- Added `CodegenWarningId = "BTREE0002"`
- Added `MakeCodegenWarningDiagnostic()` (Warning severity)
- Emit bridge catch → Warning (was: Error)
- Deserialize path unchanged (still Error)

### New files
- `Hrot.AiEditor.Persistence.Tests/Emit/BTreeEmitCoreValidationTests.cs` — 5 emit-core validation tests

### Modified test files
- `Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs` — +6 BATCH-12 tests, +2 JSON helpers
