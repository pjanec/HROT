# BF-02: SetVariable/GetVariable with `var:`-prefixed VariableId — Fix Report

**Date:** 2026-06-10
**Type:** Bug fix

---

## The change

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`
**Method:** `GraphScheduler.FindVariableIndex(string variableId)` (~line 1535)

Added prefix-stripping of `"var:"` (case-insensitive) before `Guid.TryParse`, mirroring the existing idiom in `Stage0_Rehydrate.ResolveVariableTypeId` (lines 487-490):

```csharp
var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
    ? variableId.Substring(4)
    : variableId;
```

Then uses `idStr` for both:
- `Guid.TryParse(idStr, out var guid)` — the GUID lookup
- The name-fallback comparisons — `variables[i].Name == idStr` etc.

Previously, the method called `Guid.TryParse` on the raw `variableId`, so `"var:a0000006-…"` failed to parse as a GUID, both the GUID-search and the name-fallback failing, and `FindVariableIndex` returned **-1** → `EmissionContext.VarFieldName(-1)` produced `__var_-1`.

No other files were touched. The fix is minimal: 4 lines added, `variableId` → `idStr` substitution in the name-fallback loops.

## The test

**File (new):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5VarPrefixResolutionTests.cs`

Two test cases:

1. **`SetVariable_VarPrefixedId_EmitsRealFieldName_NotVarMinusOne`** — Primary regression test. Builds an Instance blueprint with one declared variable (`Count`, `System.Int32`), an EventEntry → SetVariable (VariableId = `"var:<Count's GUID>"`, value literal `7`) → Return graph. Compiles through Stage2–Stage7, then asserts:
   - No compile errors
   - Generated source does NOT contain `__var_-1` (regression guard)
   - Generated source DOES contain `s.Count` (correct field emission)

2. **`GetVariable_VarPrefixedId_EmitsRealFieldName_NotVarMinusOne`** — Parallel case. A GetVariableNode with a `"var:<Guid>"` VariableId reads a declared variable. Same assertions on the generated source.

Both tests compile through the full pipeline (Stage2 → Stage7), verifying the fix works end-to-end.

## Test results

```
$ dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests

Failed!  - Failed:     7, Passed:  1737, Skipped:     8, Total:  1752, Duration: 34 s - Hrot.Blueprints.Tests.dll (net8.0)
```

The 7 failures are all documented pre-existing reds:

| Test | Status |
|------|--------|
| `AiPrimitive_EmitMatchesGoldenSource` (×2) | Pre-existing |
| `Stage8_PdbContainsEmbeddedSource` | Pre-existing |
| `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` | Pre-existing |
| `TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing |
| `MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing |
| `WhenNode_ZeroAllocOnHotPath` | Pre-existing |

**Zero new failures. Zero regressions.** Both new BF-02 tests pass.

## Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` | Strip `"var:"` prefix in `FindVariableIndex` before `Guid.TryParse` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5VarPrefixResolutionTests.cs` | New: 2 tests for var:-prefixed SetVariable/GetVariable compilation |
