# ADA-BATCH-10 Report

**Date:** 2026-06-14  
**Branch:** main  
**Status:** COMPLETE — all gates passed

---

## Deliverables

### ADA-P4-T01 — Recording Endpoints

**Files changed:**
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiService.cs` — added phased recording API
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiHost.cs` — recording routes with deadlock-safe phasing
- `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs` — `_rrController` field + ctor wiring
- `Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\EditorHarness.cs` — `_rrController` field + wiring

**Routes added:**
- `POST /recording/start {mode}` — returns `{recording:true, mode, fdpPath}`
- `POST /recording/stop` — returns `{recording:false, fdpPath}`
- `GET /status` updated: `recording` field now reflects actual state

**Hard ordering implemented:** `EnterPreviewMode → PrepareRecordingAsync → workload → FinalizeRecordingAsync → ExitPreviewMode`

**Mutual exclusion:** `BeginRecordingStart` throws `InvalidOperationException` when `_preview.IsInPreviewMode` is true.

**DEBT:** ADA-I-D01 — live mode throws "not supported in editor mode" (mode:preview works).

### ADA-P4-T02 — Isolated Replay Endpoints

**Routes added:**
- `POST /replay/load {fdpPath}` — returns `{loaded:true, fdpPath, totalFrames, currentFrame}`
- `POST /replay/seek {frame}` — returns `{frame}`
- `POST /replay/step {dir}` — dir: "forward"|"backward", returns `{frame}`
- `GET /replay/status` — returns `{replayActive, currentFrame, totalFrames}`
- `GET /replay/entities` — returns array from sandbox (not live world)
- `POST /replay/unload` — returns `{unloaded:true}`

**Isolation guarantee:** `ReplayBrowserContext` owns its own `SandboxRepo`/`SandboxBus`. Seek operations NEVER touch `_world` or `MasterSyncController`.

### Group I MCP Tools

**File changed:** `tools\ai-debug-mcp\src\index.mjs`

8 tools added to `TOOLS` array:
`start_recording`, `stop_recording`, `load_replay`, `seek_replay`, `step_replay`, `get_replay_status`, `list_replay_entities`, `unload_replay`

**File changed:** `tools\ai-debug-mcp\verify.mjs`
- Step 10f: Group I tool registration check (8 tools verified)
- Step 10g: Record→load→seek→inspect round-trip with live-world isolation check
- `stop_preview` guard added before `start_recording` (required: `EditorTimeTransportFacade.Step()` auto-enters preview when not in preview)

---

## Critical Design Decision: Deadlock Avoidance

`EcsRecordReplayController.PrepareRecordingAsync` / `FinalizeRecordingAsync` internally call `ModuleHostKernel.InstallModuleAsync` / `UninstallModuleAsync`, which await a `TaskCompletionSource` (`swapTcs`) fulfilled by the main thread at its next `BeforeSync` boundary (`Kernel.Update()`).

**Calling these from inside `RunOnMainThread` deadlocks**: the main thread is blocked waiting for `GetAwaiter().GetResult()` and can never reach `BeforeSync` to fulfil `swapTcs`.

**Fix:** Two-phase split:

| Phase | Method | Threading | Context |
|-------|--------|-----------|---------|
| Phase 1 | `BeginRecordingStart(mode)` | sync, main thread | via `RunOnMainThread` |
| Phase 2 | `CompleteRecordingStartAsync()` | async, background thread | directly from HTTP thread |

For stop:

| Phase | Method | Threading | Context |
|-------|--------|-----------|---------|
| Phase 1 | `CompleteRecordingStopAsync()` | async, background thread | directly from HTTP thread |
| Phase 2 | `FinishRecordingStop()` | sync, main thread | via `RunOnMainThread` |

Convenience wrappers `StartRecordingAsync` / `StopRecordingAsync` are kept for tests that control their own pump loop — they must NOT be called from inside `RunOnMainThread`.

**Tests:** `DebugApiBatch10Tests` avoids deadlock by running `CompleteRecordingStartAsync` / `CompleteRecordingStopAsync` via `Task.Run` while the test thread pumps frames with `PumpUntil(() => installTask.IsCompleted)`.

---

## Test Results

### dotnet test `--filter "FullyQualifiedName~DebugApi"`

```
Passed!  - Failed: 0, Passed: 79, Skipped: 0, Total: 79, Duration: 13 s
```

**4 new Batch-10 tests (all passing):**
- `PreviewRecording_ProducesFdpFile_AndWorldIsRewound` — 278 ms
- `RecordingStart_WhileCheckpointed_Throws` — 202 ms
- `IsolatedReplay_LoadAndSeek_LiveWorldUnaffected` — 441 ms
- `ReplayStatus_ReflectsCurrentFrame` — 302 ms

### dotnet build `IOS-IG-SimHost.sln`

```
0 Error(s), 11 Warning(s) (pre-existing warnings only)
```

### npm run verify

```
Passed: 149
Failed: 0

VERIFICATION PASSED
```

Headless round-trip confirmed:
- `start_recording` produced `.fdp` at `C:\FDP_Temp\exercises\{guid}\node_0.fdp`
- Module install log: `ModuleHostKernel | [ModuleHost] Module 'Recording_...' unhooked from topology. Draining...`
- `stop_recording` finalized and rewound (log: `UnloadingPreview: live repo rewound to snapshot`)
- `load_replay` returned `totalFrames: 2`
- `list_replay_entities` returned 2 entities from sandbox
- `seek_replay` to frame 0: `currentFrame=0`
- `get_status` during replay showed live world with `entityCount: 2` (UNCHANGED)
- `get_replay_status` showed `replayActive: true`
- `unload_replay` completed, `get_replay_status` showed `replayActive: false`

---

## DEBT

- **ADA-I-D01**: Live mode recording (`mode:live`) throws "not supported in editor mode". Requires cluster setup with `TransitionStateIntent` wiring. Deferred.

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiService.cs` | Added Group I recording/replay methods; phased recording API |
| `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiHost.cs` | Added Group I routes; deadlock-safe phased recording routes |
| `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs` | `_rrController` field + ctor wiring |
| `Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\EditorHarness.cs` | `_rrController` field + `BuildDebugApiService` wiring |
| `Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\DebugApiBatch10Tests.cs` | NEW — 4 Tier-1 tests |
| `tools\ai-debug-mcp\src\index.mjs` | 8 Group I MCP tools added |
| `tools\ai-debug-mcp\verify.mjs` | Step 10f + 10g verification; `stop_preview` guard |
