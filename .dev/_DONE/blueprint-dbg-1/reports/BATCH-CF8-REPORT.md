# BATCH-CF8 REPORT: Persist & Restore Debug Session

**Date:** 2026-06-09  
**Batch:** BATCH-CF8  
**Status:** COMPLETE ✅  
**Gate:** Build 0 errors | 7 pre-existing failures, 0 new | 8/8 CF8 tests pass

---

## Summary

Implemented debug session persistence: node breakpoints + data breakpoints (with JIT-compiled conditions as DTOs) + watches are saved to `.debug/bpsession.json` (repo root, already gitignored by CF-7-rev). On editor restart, session is restored and CF-7-rev auto-instruments affected assets — breakpoints become active without manual Compile. Stale entries (nodes deleted after save) are retained but disabled per BPF-003.

---

## Files Changed

### New Files
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DebugSessionPersistence.cs` — DTOs (DebugSessionFile, NodeBreakpointEntry, DataBreakpointEntry, WatchEntry) + Save/TryLoad methods
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF8_SessionPersistenceTests.cs` — 8 tests

### Modified Files
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs` — Marked [Obsolete], renamed internal `WatchEntry` → `WatchPersistenceEntry` to avoid conflict with new public `WatchEntry` DTO
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — Suppressed CS0618 obsolete warnings on legacy SaveWatches/LoadWatches calls
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — Added `enabled` parameter overload on `SetBreakpoint`, added `RestoreNodeBreakpoints()` and `RestoreWatches()`, updated `ReResolveBreakpointsForAsset` for stale handling
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — Added save/restore wiring: debounced save (500ms) on breakpoint/session changes, immediate save on Shutdown, restore after CF-7-rev callback wiring

---

## Task Completion

### Task 1: DebugSessionPersistence DTO + save/load ✅
- Created public DTOs: `DebugSessionFile`, `NodeBreakpointEntry`, `DataBreakpointEntry`, `WatchEntry`
- `Save()` accepts node breakpoints + watches + DBM breakpoints; writes indented JSON
- `TryLoad()` returns null on missing/malformed files (never throws)
- Filters out `ExternalHitTagPredicateDto`-only DBM breakpoints from save (they're node BPs, recreated on restore)
- Existing `WatchPersistence` marked `[Obsolete]`, internal class renamed to `WatchPersistenceEntry`

### Task 2: BlueprintDebugSession restore methods ✅
- Added `enabled` parameter overload: `SetBreakpoint(assetId, graphId, nodeId, enabled)` — interface impl delegates to it with `enabled: true`
- `RestoreNodeBreakpoints(IReadOnlyList<NodeBreakpointEntry>)` — calls SetBreakpoint per entry; triggers CF-7-rev instrumentation
- `RestoreWatches(IReadOnlyList<WatchEntry>)` — calls AddWatch per entry; skips unresolvable types
- Disabled breakpoints: still trigger instrumentation (for DebugMap), still show marker, but NOT forwarded to DBM (no pause)

### Task 3: EditorSubsystem save/restore wiring ✅
- `ScheduleDebugSessionSave()` — debounced at 500ms via `CancellationTokenSource` + `Task.Delay`
- Subscribed to `OnBreakpointListChanged` and `OnSessionStateChanged` for auto-save
- `SaveDebugSession()` called on Shutdown (after cancelling pending debounce)
- `RestoreDebugSession()` called after CF-7-rev callback is wired (so restore triggers instrumentation)
- Save path: `<repo-root>/.debug/bpsession.json` (resolved by walking up from BaseDirectory)

### Task 4: Enabled flag + stale handling ✅
- `SetBreakpoint(enabled: false)` creates breakpoint record with `Enabled = false`, skips DBM forwarding
- `ReResolveBreakpointsForAsset` marks stale ONLY when a previously-resolved breakpoint (old ProbeNodeId != authored NodeId) loses its mapping — not for breakpoints that were always fallback

### Task 5: SetIsWatch / MarkAsWatch ✅
- Already existed as `MarkAsWatch(id, bool)` on `IDataBreakpointManager` and `DataBreakpointManager` — no changes needed

### Task 8: Tests ✅
All 8 tests in `CF8_SessionPersistenceTests.cs` pass:
1. **RoundTrip_NodeBreakpointsOnly** — 2 breakpoints (1 enabled, 1 disabled) round-trip correctly
2. **RoundTrip_DataBreakpoint_WithCondition** — BlueprintVariablePredicateDto with NumericPredicateDto round-trips
3. **RoundTrip_Watches** — Watch entry with Type.GetType round-trip
4. **Save_FiltersOut_ExternalHitTagPredicateDto** — ExternalHitTag breakpoints excluded from DataBreakpoints
5. **SaveFile_IsValidJson_MatchesSchema** — Full session JSON validates structure
6. **TryLoad_ReturnsNull_ForMissingFile** — No throw on missing file
7. **TryLoad_ReturnsNull_ForMalformedFile** — No throw on garbage JSON
8. **Restore_TriggersCF7rev_Instrumentation** — RestoreNodeBreakpoints fires CF-7-rev callback with Debug mode

---

## Build & Test Results

```
Build: 0 errors, 0 warnings (from changed files)
Hrot.Blueprints.Tests: 1691 passed, 7 failed, 8 skipped, 1706 total
CF8 tests: 8 passed, 0 failed
```

### 7 Pre-existing Failures (unchanged)
1. AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource (MoveToAndFire)
2. AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource (HasVisibleTarget)
3. Stage8Tests.Stage8_PdbContainsEmbeddedSource
4. Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb
5. AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes
6. MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot
7. WhenNodePerfTests.WhenNode_ZeroAllocOnHotPath

---

## Architecture Decisions Documented

- **File location:** `<repo-root>/.debug/bpsession.json` (gitignored since CF-7-rev)
- **Authored node ids** are the durable key — not probe ids (they're re-translated via BreakpointTargets on restore)
- **Conditions saved as DTOs** — compiled delegates are never serialized; recompiled via PredicateCompiler on load
- **ExternalHitTagPredicateDto filtering** — DBM breakpoints created by SetBreakpoint forwarding are excluded from save (they're node BPs, recreated on restore)
- **Entity FilterEntity** — not persisted (runtime-only references)
- **Stale breakpoints** — retained but disabled (IsStale) when node deleted after save; only breakpoints that were previously resolved to block-probe ids are marked stale

---

## Success Criteria Checklist

- [x] Build 0 errors
- [x] `Hrot.Blueprints.Tests` → 7 pre-existing, 0 new
- [x] All 8 CF8 tests pass
- [x] `DebugSessionPersistence` generalizes `WatchPersistence` (save ALL breakpoints, not just watches)
- [x] Save triggers on breakpoint/watch change (debounced) + on close
- [x] Restore triggers CF-7-rev instrumentation for each restored asset
- [x] `ExternalHitTagPredicateDto`-only DBM breakpoints excluded from save
- [x] Disabled breakpoint restore: breakpoint entered as disabled, not forwarded to DBM
- [x] Missing node on restore → stale (via `ReResolveBreakpointsForAsset` update)
- [x] `MarkAsWatch` already present on DBM (no addition needed)
