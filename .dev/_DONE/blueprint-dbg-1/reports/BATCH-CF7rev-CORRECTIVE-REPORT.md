# BATCH-CF7rev-CORRECTIVE-REPORT — Fix auto-instrumentation in production code

**Date:** 2026-06-09
**Batch:** BATCH-CF7rev-CORRECTIVE
**Priority:** P0 — Feature broken in production
**Status:** ✅ DONE — All objectives met

---

## Summary

The auto-instrumentation callback in `EditorSubsystem.cs` was using plain `JsonSerializer.Deserialize<BlueprintAsset>(json, new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true })` — missing `JsonStringEnumConverter`, `AllowTrailingCommas`, and `ReadCommentHandling.Skip`. This caused string enums (e.g., `"Kind": "Function"`) to fail deserialization silently (try-catch swallowed by `Console.WriteLine` which is invisible in the editor).

**Root cause confirmed:** The diagnostic test `PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices` proves that the plain `JsonSerializer` path throws a `JsonException` on `Count4.bp.json`, while `BlueprintJsonServices.Deserialize` succeeds.

---

## What Was Fixed

### Task 0: EditorSubsystem callback asset loading ✅

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

| Change | Description |
|--------|-------------|
| Added `using Hrot.Blueprints.Core;` | Required for `BlueprintJsonServices` (line 101) |
| Replaced `JsonSerializer.Deserialize<BlueprintAsset>(json, options)` | Now uses `BlueprintJsonServices.Deserialize(json)` — the single source of truth that includes `JsonStringEnumConverter`, `AllowTrailingCommas`, `ReadCommentHandling.Skip` |
| Replaced `Console.WriteLine(...)` | Now uses `bpLog.LogWarning/LogError/LogInfo` via `MessageLogOutputConsole` wrapping `_hotReloadSource` — messages appear in the editor's Message Log UI |
| Added success log | `bpLog.LogInfo($"Auto-instrumentation: {asset.Name} compiled in {mode} mode.")` after `TriggerAsync` |
| Added null-deserialization guard | Logs a warning if `BlueprintJsonServices.Deserialize` returns null |

### Task 1: Fix false-positive end-to-end test ✅

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs`

| Change | Description |
|--------|-------------|
| Added clarifying comment to `SetBreakpoint_TriggersAutoInstrument_ThenPauses` | Notes the test uses a synthetic callback (not the production QuickReloadService path) |
| Added `PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices` | Diagnostic test proving the broken path fails on Count4.bp.json |
| Added `CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset` | Tests the REAL production asset-loading path: `BlueprintJsonServices.Deserialize` + compilation in Debug mode |
| Added `using System.Text.Json;` | Required for `JsonSerializerOptions` in the diagnostic test |

### Task 2: Diagnostic verification ✅

The diagnostic test `PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices` confirmed:

- **Broken path** (plain `JsonSerializer.Deserialize<BlueprintAsset>` without `JsonStringEnumConverter`): **throws `JsonException`** on `Count4.bp.json`
- **Fixed path** (`BlueprintJsonServices.Deserialize`): **succeeds**, producing a valid asset with correct graph/node counts

This is the definitive proof that the root cause is deserialization failure.

---

## Test Results

### CF7rev tests (all pass): 10/10 ✅

```
CF7rev_InstrumentationTests.SetBreakpoint_NoDebugMap_InvokesCallback_WithDebugMode       ✅
CF7rev_InstrumentationTests.SetBreakpoint_HasDebugMap_DoesNotInvokeCallback              ✅
CF7rev_InstrumentationTests.AddWatch_NoDebugMap_InvokesCallback_WithTraceMode            ✅
CF7rev_InstrumentationTests.RegisterDebugMap_ReResolves_TentativeProbeNodeId             ✅
CF7rev_InstrumentationTests.RegisterDebugMap_ReResolves_WhenProbeNodeIdAlreadyCorrect    ✅
CF7rev_EndToEndTests.SetBreakpoint_TriggersAutoInstrument_ThenPauses                     ✅
CF7rev_EndToEndTests.BreakpointSetBeforeCompile_BecomesActive_AfterMapRegisters          ✅
CF7rev_EndToEndTests.ModeSelection_DebugForBreakpoints_TraceForWatches                   ✅
CF7rev_EndToEndTests.PlainJsonDeserialization_FailsOrProducesDifferentAsset...           ✅  (NEW)
CF7rev_EndToEndTests.CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilable  ✅  (NEW)
```

### Full Blueprints suite

- **Passed:** 1683
- **Failed:** 7 (all pre-existing, unrelated: MoveToAndFire snapshot mismatch, WhenNode benchmark alloc, etc.)
- **Skipped:** 8

### Build

```
Build succeeded — 0 Errors, 9 Warnings (all pre-existing)
```

---

## Success Criteria Verification

| Criterion | Status |
|-----------|--------|
| `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors | ✅ |
| All CF7rev tests pass (8 original + new tests) | ✅ 10/10 |
| Blueprints full suite: 7 pre-existing, 0 new | ✅ No new failures |
| EditorSubsystem callback uses `BlueprintJsonServices.Deserialize` | ✅ |
| Logging uses `_hotReloadSource` (visible in editor), not `Console.WriteLine` | ✅ |
| New test `PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices` confirms root cause | ✅ Throws `JsonException` |
| New test `CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset` passes | ✅ |
| Report submitted | ✅ This document |

---

## Files Changed

1. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — Fixed callback deserialization + logging
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs` — Added diagnostic + production-path tests, clarified synthetic callback

---

## Root Cause Details

**Count4.bp.json** contains string enum values like:
```json
"Kind": "Function",
"Direction": "Input",
"ValueType": "Float"
```

The plain `JsonSerializerOptions` with only `IncludeFields` and `PropertyNameCaseInsensitive` cannot deserialize these — `System.Text.Json` throws a `JsonException` because it expects integer enum values by default.

`BlueprintJsonServices._options` includes `options.Converters.Add(new JsonStringEnumConverter())` which correctly handles string-to-enum conversion. It also adds `AllowTrailingCommas = true` and `ReadCommentHandling = JsonCommentHandling.Skip` as safety measures.

The error was silently swallowed because:
1. The callback's `catch (Exception ex)` caught the `JsonException`
2. `Console.WriteLine` output is invisible in the Unity-like editor — it goes to the system console, not the editor's Message Log
3. The asset remained uncompiled, so breakpoints never fired — exactly the behavior the user observed ("set bp → sim does NOT pause")
