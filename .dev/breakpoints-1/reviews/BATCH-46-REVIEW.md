# BATCH-46 Review — UBP-P9T1 + P9T2 + P9T3

**Date:** 2025  
**Status:** APPROVED  
**Prior test count:** 89  
**New test count:** 97 (+8)

---

## Summary

P9T1-P9T3 implemented cleanly. 97/97 tests passing. Zero compiler warnings.

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` | Added `IsBroken: bool` and `IsWatch: bool` to `Breakpoint` record |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | Added `OnHotReloadCompleted`, `OnHotReloadBegin`, `SaveWatches`, `LoadWatches`, `MarkAsWatch` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Added `_notifier` field, updated constructor, implemented all 5 new methods |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IBreakpointNotifier.cs` | NEW — simple `Notify(string)` interface |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs` | NEW — `Save`/`TryLoad` JSON helpers |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/HotReloadResilienceTests.cs` | NEW — 6 tests (P9T1: 3, P9T2: 3) |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/WatchPersistenceTests.cs` | NEW — 2 tests (P9T3) |

---

## Test Quality Assessment

### P9T1 — HotReloadResilienceTests (3 tests) ✓

- **`HotReload_StructureCompatible_PreservesBreakpoint`**: Uses real `PredicateCompiler`, registers a `PropertyMatchDto(Health.Current<10)`, calls `OnHotReloadCompleted()`, asserts breakpoint still mounted AND `IsBroken == false`. **Directly validates the design "remount on success" requirement.**

- **`HotReload_RemovesTargetedField_MarksBreakpointBroken`**: Uses `ThrowOnSecondCompileCompiler` (first call succeeds to simulate normal AddBreakpoint; second call throws to simulate "field removed"). Calls `OnHotReloadCompleted()`, asserts `IsBroken == true` AND `Condition != null` (DTO retained). **Validates both error path and "retain DTO for repair" design requirement.**

- **`HotReload_NoAccessViolation_DuringActiveBreakpoint`**: 5 breakpoints × 100 reload cycles with `Record.Exception`. Asserts no exception + all `IsBroken == false`. **Fuzz test matching the TASK-DETAIL success condition precisely.**

### P9T2 — HotReloadResilienceTests (3 tests) ✓

- **`HotReloadBegin_DuringPause_ForcesContinueAndFlushesMutations`**: Puts manager in paused state via `OnHit`, stages 3 mutations, calls `OnHotReloadBegin()`. Asserts `IsPaused == false` AND `PendingMutationsCount == 0`. **Key design invariant: stale mutations discarded, not applied to new layout.** Uses manual manager setup (not `ManagerFactory`) to properly pre-populate `preTickSnapshot` so `OnHit` doesn't fail on SyncFrom.

- **`Notification_StepAbandoned_Emitted`**: Uses `RecordingBreakpointNotifier`, pauses, calls `OnHotReloadBegin()`. Asserts `Messages.Count == 1` and message contains "abandoned" (case-insensitive). **Directly tests the design's toast notification requirement.**

- **`HotReloadBegin_WhenNotPaused_DoesNothing`**: Guards against spurious calls — no exception, state unchanged. **Defensive edge case.**

### P9T3 — WatchPersistenceTests (2 tests) ✓

- **`Watches_PersistAcrossRestart_StructureCompatible`**: Creates manager1 with 3 watch-flagged breakpoints via `MarkAsWatch`, saves to temp file, creates manager2, calls `LoadWatches`. Asserts 3 watches restored, none broken. Uses temp file with `finally` cleanup. **Full round-trip test with real `WatchPersistence` JSON serialization.**

- **`Watches_Restore_FailsGracefullyOnDriftedSchema`**: Saves a watch, clears registry (simulate drift), creates manager2 with `AlwaysThrowCompiler`, loads watches. Asserts (a) no exception, (b) watch present, (c) `IsBroken == true`. **Validates the "flag invalid but don't crash" design requirement from TASK-DETAIL.**

---

## Key Implementation Decisions

1. **`OnHotReloadBegin` discards mutations BEFORE `RequestContinue`**: This ensures stale byte-offset mutations from the old layout are never applied to the new layout via ECB drain. Correct per §12.2.

2. **`IBreakpointNotifier` is a new minimal interface** (not referencing `NodeEditor.Core.Action.IEditorIndicators` directly): Keeps `Hrot.Diagnostics.Breakpoints` free of NodeEdit dependency. Wiring to actual toast system happens at composition root.

3. **`LoadWatches` graceful failure via `Add(Breakpoint { Enabled=false, IsBroken=true })`**: When compiler throws during load, uses the `Add(Breakpoint)` overload with `Enabled=false` to skip `TryMountDelegate`, then sets `IsWatch=true` and `IsBroken=true` in the breakpoint record. Watch is present in `AllBreakpoints` (user can see it) but doesn't fire.

4. **`WatchPersistence` uses `JsonSerializer` with `IncludeFields=true`**: Consistent with `BreakpointJsonClipboard`. The `[JsonPolymorphic]` + `[JsonDerivedType]` attributes on `SearchPredicateDto` handle polymorphic DTOs automatically.

---

## APPROVED — proceed to BATCH-47 (INT1-INT3)
