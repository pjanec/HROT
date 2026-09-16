# BATCH-06 Report

**Tasks:** PU-601, PU-602, PU-603 — Unified Save / Save-All + FlushNow + flush-on-close
**Branch:** `blueprint-integ-1`
**Date:** 2026-06-05

---

## Implementation Summary

### PU-601 — `RegenerationScheduler.FlushNow()`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs`

Added `public int FlushNow()` that drains `_pending` immediately, bypassing the debounce guard. Extracted a shared private `Drain()` method called by both `Tick()` (after debounce check) and `FlushNow()` (unconditionally). Re-entrancy safety is preserved: `Drain()` copies the pending set and clears it before invoking `_flushAction`, so any `Schedule()` calls made during a flush action queue to the next drain.

`Tick()` is unchanged except it now calls `Drain()` internally — same observable behavior.

### PU-602 — `AtomicFileWriter` + `SaveAllAiDocumentsCommand`

**New file:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/AtomicFileWriter.cs` (netstandard2.0)

`AtomicFileWriter.Write(path, content)` writes via temp-then-`Move` pattern. Uses `File.Delete` + `File.Move` (not the `File.Move(..., overwrite)` overload, which is .NET 5+ only) for netstandard2.0 compatibility. Target directory is created if absent. Cleans up `.tmp` sidecar on failure.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Documents/SaveAllAiDocumentsCommand.cs`

`static SaveAllAiDocumentsCommand.Execute(manager, saveBlueprintDelegate, saveBTreeDelegate, saveHsmDelegate, report)` iterates `manager.OpenDocuments` and, for each dirty doc:
- Checks `SourceFilePath` — if empty, emits `[WARN] Skipped '...': no source path (awaiting migration/path-at-creation)` via `report`, leaves the doc dirty, never throws.
- Dispatches by `doc.Kind` to the appropriate injected delegate, then calls `doc.MarkClean()`.
- Wraps each per-doc save in a `try/catch` so a single failure does not abort the rest (failure is reported but not rethrown).
- Clean docs (not `IsDirty`) are skipped silently.

**Circular-ref avoidance:** `SaveAllAiDocumentsCommand` lives in `Hrot.Editor.AiShared` and receives all per-kind save logic as injected `SaveDelegate` (a `delegate void(IEditableAsset, string)`) — the same pattern as `AiAssetEmitService`. The BTree/HSM mapper calls (`BehaviorTreeAssetMapper.ToDto`, `HsmAssetMapper.ToDto`, JSON services, `AtomicFileWriter`) are wired in `EditorSubsystem.RegisterWindows` (where those assemblies are already visible) and passed as lambda delegates. Blueprint save similarly calls `SaveActiveBlueprintCommand.Save` from a lambda in `EditorSubsystem`.

**`BeforeDocumentClosed` event:** Added to `AiDocumentManager` (`Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs`). Fires before the document is removed from the list so subscribers can flush; the manager itself remains persistence-agnostic.

### PU-603 — Ctrl+Shift+S + Save-All toolbar + flush-on-close + Shutdown flush

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

- Added `private Action? _saveAllCallback` field + `internal Action? SaveAllCallback` test hook.
- In `RegisterWindows`: built per-kind save delegates (Blueprint via `SaveActiveBlueprintCommand.Save`; BTree via `BehaviorTreeAssetMapper.ToDto → BTreeJsonServices.Serialize → AtomicFileWriter.Write`; HSM via `HsmAssetMapper.ToDto → HsmJsonServices.Serialize → AtomicFileWriter.Write`). The Blueprint delegate resolves `BlueprintAsset` from the document's `ViewState as AiCanvasContext → AssetRef` (the same lookup pattern as the existing Ctrl+S wiring).
- `_saveAllCallback` calls `_regenerationScheduler?.FlushNow()` then `SaveAllAiDocumentsCommand.Execute(...)`.
- Wired `_aiDocumentManager.BeforeDocumentClosed` to save dirty path'd docs on close (same per-kind switch + `MarkClean`; errors caught and printed to Console, never rethrown).
- In `DrawUI`: added "Save All" ImGui window gated on `ImGui.GetCurrentContext() != IntPtr.Zero` with a "Save All" button and Ctrl+Shift+S shortcut (mirrors the existing Ctrl+S + "Save Blueprint" block).
- In `Shutdown`: added `_regenerationScheduler?.FlushNow()` + `_saveAllCallback?.Invoke()` at the top, before any resource teardown.

---

## Design Decisions

### Delegate injection for circular-ref avoidance

`SaveAllAiDocumentsCommand` is in `Hrot.Editor.AiShared` (which does NOT reference `Hrot.BTree.Editor` / `Hrot.Hsm.Editor` / `Hrot.Blueprints.Editor`). All kind-specific logic is injected as `SaveDelegate` lambdas from `EditorSubsystem` (in `Hrot.Editor`, which references all three editor assemblies). This is the exact pattern of `AiAssetEmitService`.

### `BeforeDocumentClosed` event (not call-site hook)

Chosen over an EditorSubsystem-side call-site wrap because:
1. `AssetBrowserWindow.HandleCloseRow` → `mgr?.Close(doc)` is the actual call site, inside the window assembly, not directly in EditorSubsystem.
2. The event keeps `AiDocumentManager` persistence-agnostic — the manager fires the event and proceeds with close regardless.
3. The subscription is a single line in `RegisterWindows`, easy to follow.

### Blueprint save from `ViewState`, not from `doc.Asset`

`doc.Asset` for Blueprint documents is `BlueprintFileAsset` (the `IEditableAsset` catalog wrapper), not `BlueprintAsset` (the rich model). The rich model is in `doc.ViewState as AiCanvasContext → AssetRef`. The Blueprint save delegate looks up the document from `_aiDocumentManager.OpenDocuments` by `AssetId` to retrieve the `ViewState`. This mirrors `SaveFromActiveDocument` and preserves the unchanged `BlueprintAsset` / `SaveActiveBlueprintCommand.Save` write path.

### Shutdown order

`FlushNow()` is called first (drains the debounced `.cs` regeneration queue), then `_saveAllCallback` (saves JSON for dirty path'd docs). This ensures the regen scheduler does not write `.cs` for assets that were already JSON-saved. Both calls are before any `Dispose()` / null-out.

---

## Deviations

None. The implementation matches the spec exactly.

---

## Test Results

### PU-601 — `FlushNowTests` (5 new tests in `Hrot.Editor.AiShared.Tests`)

| Test | Result |
|------|--------|
| `FlushNow_WithScheduledAsset_FlushesDespiteNoClock` | PASS |
| `FlushNow_EmptyQueue_ReturnsZero` | PASS |
| `FlushNow_ClearsQueue_SubsequentFlushNowReturnsZero` | PASS |
| `FlushNow_ReentrancySafe_ScheduleDuringFlush_QueuesToNextTick` | PASS |
| `Tick_DebounceUnaffected_AfterFlushNow_RescheduleAndTickStillWork` | PASS |

All existing `RegenerationSchedulerTests` (5 tests) remain green — debounce behavior unchanged.

### PU-602 — `AtomicFileWriterTests` (4 new) + `SaveAllAiDocumentsCommandTests` (6 new)

| Test | Result |
|------|--------|
| `Write_CreatesFile_WithCorrectContent` | PASS |
| `Write_OverwritesExistingFile` | PASS |
| `Write_CreatesDirectoryIfAbsent` | PASS |
| `Write_NullPath_Throws` | PASS |
| `Execute_NullManager_IsNoOp` | PASS |
| `Execute_CleanDocs_NotWritten` | PASS |
| `Execute_NoPath_SkippedWithWarnReport_DocStillDirty` | PASS |
| `Execute_DirtyBTree_WriteJsonToFile_MarkClean` | PASS |
| `Execute_DirtyHsm_WriteJsonToFile_MarkClean` | PASS |
| `Execute_MixedDocs_PathDSaved_NoPathSkipped_CleanNotWritten` | PASS |

### PU-603 — `FlushOnCloseTests` (5 new)

| Test | Result |
|------|--------|
| `BeforeDocumentClosed_FiredBeforeDocRemoved_WithDirtyDoc` | PASS |
| `BeforeDocumentClosed_SaveDirtyDoc_ViaSpyDelegate` | PASS |
| `BeforeDocumentClosed_CleanDoc_NothingWritten` | PASS |
| `BeforeDocumentClosed_HsmDirtyDoc_SavesHsmJson` | PASS |
| `BeforeDocumentClosed_SaveAllCallback_InvokedByExecute` | PASS |

### Verification gates (per batch spec)

| Gate | Result |
|------|--------|
| `dotnet build IOS-IG-SimHost.sln` | **0 errors / 0 new warnings** in touched projects |
| `Hrot.Editor.AiShared.Tests` (all) | **789 passed / 0 failed** |
| `Hrot.AiEditor.Persistence.Tests` (persistence 88) | **88 passed / 0 failed** |
| `Hrot.AiEditor.Generators.Tests` (generators 37) | **37 passed / 0 failed** |
| `Hrot.BTree.Editor.Tests` | **392 passed / 0 failed** |
| `Hrot.Hsm.Editor.Tests` | **341 passed / 0 failed** |
| `SaveActiveBlueprintCommandTests` | **8 passed / 0 failed** (no regression) |
| `RegenerationSchedulerTests` | **5 passed / 0 failed** (no regression) |
| `EditorSubsystemBoot` | **10/10 passed** |
| `Hrot.Blueprints.Tests` | **7 pre-existing failures (DEBT-006/014 baseline), 0 new** |

### Pre-existing failures in `Hrot.Blueprints.Tests` (confirmed baseline, not introduced by this batch)

All 7 match exactly the baseline recorded before any BATCH-06 changes:
1. `Library_EmitMatchesGoldenSource`
2. `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`
3. `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`
4. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
5. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
6. `LibraryMath_GeneratedSource_Snapshot`
7. `MoveToAndFire_GeneratedSource_Snapshot`

Blueprint's `SaveActiveBlueprintCommand.Save` write path (`File.WriteAllText`) is **unchanged**. All 8 `SaveActiveBlueprintCommandTests` pass.

---

## Developer Insights

### `File.Move` overload compatibility

`File.Move(src, dst, overwrite: true)` is .NET 5+, not available in netstandard2.0. The `AtomicFileWriter` uses `File.Delete(path) + File.Move(tmp, path)` which has a brief non-atomic window on NTFS, but is safe in practice because the temp file is on the same volume and any interruption between Delete and Move leaves the tmp file in place (not a silent data loss). The design note is documented in the source.

### Blueprint `ViewState` lookup in the save delegate

The Blueprint save delegate must look up `doc.ViewState as AiCanvasContext → AssetRef` to get the `BlueprintAsset`, because `doc.Asset` is `BlueprintFileAsset` (catalog wrapper, not the rich model). This is the same lookup pattern as the existing Ctrl+S wiring. If `ViewState` is null (e.g. Blueprint not yet opened in the canvas), the delegate is a no-op — the doc stays dirty and the AssetBrowser close handler skips it. This is acceptable because a Blueprint with no `ViewState` has never been opened for editing and cannot have unsaved canvas changes.

### `_saveAllCallback` double-FlushNow on Shutdown

`Shutdown()` calls `_regenerationScheduler?.FlushNow()` and then `_saveAllCallback?.Invoke()`. The `_saveAllCallback` also calls `_regenerationScheduler?.FlushNow()` internally — resulting in two FlushNow calls. The second is a no-op (queue empty). This is harmless and ensures Shutdown always flushes even if `_saveAllCallback` is null.

### Debounced `flushAction` `.cs` routing is UNCHANGED — PU-401 deferral

The `RegenerationScheduler.flushAction` in `EditorSubsystem.RegisterWindows` still routes:
- Blueprint → `_blueprintQuickReloadTrigger?.Invoke(asset)` (in-process compile, no `.bp.json` write)
- BTree/HSM → `emitService.Emit(asset)` (writes `.cs` file → file-watcher → MSBuild → edit-to-live)

**This routing is intentionally unchanged.** Today's BTree/HSM assets are assembly-loaded with `SourceFilePath=""`, so Save-All skips them with a warning. The `.cs`→`.json` route switch is deferred to **PU-401** (migration batch), which will run after real assets exist as `.json` files. Flipping the route now would break BTree/HSM edit-to-live for existing un-migrated `.cs` assets.

The BTree/HSM JSON save path added in PU-602 is purely additive: it is exercised only via explicit Save-All on synthesized path'd docs (in tests), and will be live for real assets after PU-401 migration.

---

## Known Issues

None. All success conditions met.

---

## Suggested Commit Message

```
feat(persistence): unified Save-All + FlushNow + flush-on-close (BATCH-06, PU-601/602/603)
```
