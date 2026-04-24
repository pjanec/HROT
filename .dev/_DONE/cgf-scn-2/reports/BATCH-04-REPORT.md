# BATCH-04 Report — Phase 5: Checkpoint Event Preservation

**Batch**: BATCH-04  
**Phase**: 5 — Checkpoint Event Preservation (S501-S504)  
**Status**: COMPLETE

---

## Summary

All Phase 5 tasks have been implemented and tested. The checkpoint pipeline now
correctly captures events from the live simulation bus and persists them in
checkpoint files so that replica nodes receive the events on restore.

---

## Tasks Completed

| Task   | Description                                              | Status |
|--------|----------------------------------------------------------|--------|
| S501   | `FdpEventBus.PopulateCurrentStreams` / `PopulateCurrentManagedStreams` | Done |
| S502   | `RecorderSystem.WriteEvents` `serializeReadBuffer` flag  | Done   |
| S503   | Wire `EventAccumulator` into `ReferenceCheckpointHandler` | Done  |
| S504   | `CheckpointIOWorker` passes `snapshot.Bus, serializeReadBuffer: true` | Done |

---

## Files Changed

### FDP Submodule

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Core/IManagedEventStreamInfo.cs` | Added `IList CurrentEvents { get; }` to interface and concrete default |
| `FDP/Engine/Fdp.Core/ManagedEventStream.cs` | Implemented `CurrentEvents => _front` property |
| `FDP/Engine/Fdp.Core/FdpEventBus.cs` | Added `PopulateCurrentStreams`, `PopulateCurrentManagedStreams` |
| `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs` | Added `serializeReadBuffer` parameter to `RecordDeltaFrame`, `RecordKeyframe`, `WriteEvents` |
| `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs` | `WriteCheckpointFile`: pass `snapshot.Bus, serializeReadBuffer: true` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs` | Added `EventAccumulator` field/ctor param; `Commit()` calls `FlushToReplica` |
| `FDP/Engine/Fdp.Core.Tests/FdpEventBusCurrentStreamsTests.cs` | **NEW** — S501 tests (4 tests) |
| `FDP/Engine/Fdp.Core.Tests/RecorderSystemReadBufferTests.cs` | **NEW** — S502 tests (2 tests) |
| `FDP/Engine/Fdp.Core.Tests/CheckpointIOWorkerTests.cs` | S504-T1 test added |
| `FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` | Added `K4os.Compression.LZ4` test dependency |

### Top-Level Repo

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeContext.cs` | Added `public required EventAccumulator EventAccumulator { get; init; }` |
| `Hrot/Engine/Hrot.Common/Infrastructure/HrotNodeBuilder.cs` | Set `EventAccumulator = eventAccumulator` in `return new HrotNodeContext { ... }` |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` | Added `eventAccumulator` param to `BuildOrchestration`; pass to handler |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Pass `eventAccumulator: _context.EventAccumulator` to `BuildOrchestration` |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CheckpointClusterOpHandlerTests.cs` | Updated two `new ReferenceCheckpointHandler(...)` call sites to add `new EventAccumulator()` |
| `Hrot/Subsystems/Hrot.SimHost.Tests/ReferenceCheckpointEventTests.cs` | **NEW** — S503 tests (3 tests) |

---

## Test Results

| Test Suite | Before | After | New Tests |
|------------|--------|-------|-----------|
| `Fdp.Core.Tests` | 716 passed | 725 passed | +9 (S501×4, S502×2, S504×1, plus S501-T3 variant) |
| `Hrot.SimHost.Tests` | 444 passed | 448 passed | +4 (S503×3, updated handler tests counted) |

Build: `0 errors`, warnings are pre-existing.

---

## Design Notes

- `GetRawBytes()` on `INativeEventStream` already reads from `_readBuffer` (the current/read side). No new method was needed; `serializeReadBuffer=true` simply routes to `GetRawBytes()` instead of `GetPendingBytes()`.
- Managed stream `CurrentEvents => _front` exposes the read-side list without allocation.
- `PopulateCurrentStreams` / `PopulateCurrentManagedStreams` mirror the existing `PopulatePendingStreams` / `PopulatePendingManagedStreams` but check the read buffer.
- `ReferenceCheckpointHandler.Commit()` calls `FlushToReplica(snap.Bus, source.GlobalVersion - 1)` so events captured since the last acknowledged frame are injected into the snapshot's bus before recording.
- `uint.MaxValue` underflow in `source.GlobalVersion - 1` when `GlobalVersion == 0` is safe — it results in nothing being flushed, which is the correct behaviour for a fresh node.
