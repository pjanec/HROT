# BATCH-23 Report: ED-004 Debug Windows + ED-005 Reload Services

**Batch:** BATCH-23
**Tasks:** TASK-ED-004, TASK-ED-005
**Status:** APPROVED
**Date:** 2026-05-22

---

## Summary

All deliverables from BATCH-23 implemented, built, and tested successfully.

---

## Files Created

### Editor/Debug/ (ED-004)

| File | Description |
|------|-------------|
| `Hrot.Blueprints.Editor/Debug/ReloadLogEntry.cs` | Record with Timestamp, Source, Succeeded, Message, DurationMs |
| `Hrot.Blueprints.Editor/Debug/HotReloadLogModel.cs` | Ring-buffer model, MaxEntries=1000, Queue-based eviction |
| `Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs` | Title returns "Debug [PAUSED]" or "Debug" based on session.IsPaused |
| `Hrot.Blueprints.Editor/Debug/WatchPanelWindow.cs` | OnActivated subscribes OnPinValueChangedEvent; OnDeactivated unsubscribes |
| `Hrot.Blueprints.Editor/Debug/CallstackWindow.cs` | Skeleton with IBlueprintDebugSession + EditorSelectionStore deps |
| `Hrot.Blueprints.Editor/Debug/HotReloadLogWindow.cs` | OnReloadCompleted/OnReloadFailed routes entries to HotReloadLogModel |

### Editor/Reload/ (ED-005)

| File | Description |
|------|-------------|
| `Hrot.Blueprints.Editor/Reload/QuickReloadResult.cs` | Record(bool Succeeded, string? ErrorMessage, long DurationMs) |
| `Hrot.Blueprints.Editor/Reload/FullRebuildResult.cs` | Record(bool Succeeded, int ExitCode, long DurationMs) |
| `Hrot.Blueprints.Editor/Reload/QuickReloadService.cs` | Slice 1 stub: logs intent, returns failed result |
| `Hrot.Blueprints.Editor/Reload/FullRebuildService.cs` | Spawns dotnet build via Process.Start, streams stdout, sets PendingDrainAfterBuild=true on success |

### Tests/Editor/

| File | Description |
|------|-------------|
| `Hrot.Blueprints.Tests/Editor/MockDebugSession.cs` | Full IBlueprintDebugSession + IBlueprintProbeSink no-op implementation; IsPaused settable; PinValueChangedSubscriberCount exposed |
| `Hrot.Blueprints.Tests/Editor/HotReloadLogModelTests.cs` | 5 tests for ring buffer and window routing |
| `Hrot.Blueprints.Tests/Editor/DebugWindowsTests.cs` | 2 tests for pause title and event subscription |

---

## Notable Implementation Decisions

1. **OnPinValueChangedEvent**: The interface event is `OnPinValueChangedEvent` (not `OnPinValueChanged`) to avoid C# conflict with the generic `IBlueprintProbeSink.OnPinValueChanged<T>` method. WatchPanelWindow and MockDebugSession use the correct name.

2. **MockDebugSession**: Implements `OnPinValueChangedEvent` as an explicit interface event (matching CapturingDebugSession pattern) and exposes `PinValueChangedSubscriberCount` for test assertions.

3. **BlueprintSignature namespace**: `BlueprintSignature` is in `Hrot.Blueprints.Core.Compiler`, not `Fdp.Toolkit.Blueprints`. QuickReloadService uses `Hrot.Blueprints.Core.Compiler`.

4. **gitignore workaround**: The `.gitignore` has `[Dd]ebug/` pattern. The `Debug/` subfolder files required `git add -f` to stage them.

5. **QuickReloadService**: `LastSignaturesUsedForTesting` uses `IReadOnlyList<BlueprintSignature>?` (not `string`) since `BlueprintSignature` exists in the codebase.

---

## Test Results

| Metric | Before | After |
|--------|--------|-------|
| Total  | 449    | 456   |
| Passed | 444    | 451   |
| Failed | 0      | 0     |
| Skipped| 5      | 5     |

New tests added: 7 (SC1-SC5 in HotReloadLogModelTests + SC1-SC2 in DebugWindowsTests)

---

## Success Criteria

| SC | Check | Result |
|----|-------|--------|
| SC1 | HotReloadLogModel: add entry increases count | PASS |
| SC2 | HotReloadLogModel: evicts oldest beyond 1000 | PASS |
| SC3 | HotReloadLogModel: clear resets count | PASS |
| SC4 | HotReloadLogWindow.OnReloadCompleted adds success entry | PASS |
| SC5 | HotReloadLogWindow.OnReloadFailed adds failed entry | PASS |
| SC6 | DebugPanelWindow.Title contains [PAUSED] when IsPaused | PASS |
| SC7 | WatchPanelWindow subscribes on Activated, unsubscribes on Deactivated | PASS |
| Build | dotnet build Hrot.Blueprints.Editor zero errors | PASS |
| Tests | 0 failures full suite | PASS |

---

## Commit

```
da56f2f5 feat(blueprints): BATCH-23 ED-004 debug windows + ED-005 reload services stubs
```
