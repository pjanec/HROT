# ADA-BATCH-10: Recording + Isolated Replay (Group I) + MCP tools

**Batch Number:** ADA-BATCH-10
**Tasks:** ADA-P4-T01 (recording) + ADA-P4-T02 (isolated replay) + Group I MCP tools
**Phase:** Phase 4 — flight recording + post-mortem replay (batched so a record→replay round-trip is the arbiter)
**Estimated Effort:** ~20 hours
**Executor:** sonnet (run-mode ordering + finalize-before-rewind + replay isolation are subtle)
**Priority:** HIGH (leverage tier)
**Dependencies:** Phase 1 + P-MCP + BATCH-07/08/09. Reuses the wired `EcsRecordReplayController` +
`ReplayBrowserContext`.

---

## Onboarding & Workflow

Two coupled capabilities: (I1) record the sim to a `.fdp` (preview = revertible, or live = ledgered); (I2)
load that `.fdp` into an ISOLATED replay sandbox and seek/step it without touching the live world. Batched
together so the live arbiter is a real **record → load → seek → inspect** round-trip.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-08-REVIEW.md` + `ADA-BATCH-09-REVIEW.md` (run-mode/preview-slot
   coordination; the "prove the real thing on the live process, check the specific symptom" discipline).
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group I (recording + replay) + the **Run-Mode Model** section
   (preview vs live; finalize-BEFORE-rewind; replay isolation).
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P4-T01, ADA-P4-T02 (authoritative + Success).

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY — the lead re-runs
> `dotnet test --filter DebugApi`, a REAL headless record→replay reproduce, AND `npm run verify`, and reads
> the full diff. Green unit tests are NOT enough — the `.fdp` must actually be produced AND replayed, and the
> live world must be proven UNAFFECTED during replay seeking. Run the FULL `dotnet build IOS-IG-SimHost.sln`.

### Existing infra to reuse (do NOT reinvent — confirmed APIs)
- **`EcsRecordReplayController`** (`Hrot.SimHost/Modules/Orchestration/`) — the editor builds it as a LOCAL
  `rrController` at `EditorSubsystem.cs:852`. **Retain it as a field and pass it into `DebugApiService`**
  (mirror how `_bpManager`/`diffService` were added). Methods: `PrepareRecordingAsync(Guid exerciseId, string
  storageDirectory)`, `FinalizeRecordingAsync(long maxNetworkId = 0)`, `IsReplayActive`, `ActiveRecordingModule`.
- **`ReplayBrowserContext`** (`Fdp.Toolkits/ReplayBrowser/`) — fully isolated: `SandboxRepo` (own
  `EntityRepository`), `SandboxBus`, `HistoryService`, `Playback`, `CurrentFrame`; `LoadRecording(string
  fdpPath)`, `SeekToFrame(int)`, `StepForward()`/`StepBackward()`. Construct a `new ReplayBrowserContext()`
  for `/replay/load`; it bypasses the GUI ledger (loads by path directly).
- **ExerciseId / dir:** `ClusterStateUpdateEvent.ExerciseId` (the editor already reads cluster state for
  scenario-load completion) + `OrchestrationConstants.DefaultStagingDirectory`. Do NOT mint your own id.
- **Run-mode state** in `/status` (`inPreview`, `recording`) — reuse/extend.
- For replay-scoped queries, build a SECOND `EntityStateExtractionService` over `SandboxRepo` (the extraction
  service is repo-parameterized) and route `/entities`/`/entities/{id}`/`/events` to it WHILE replay is active.

---

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Group I)
### Recording (T01)
- `POST /recording/start {mode:"preview"|"live"}`:
  - **preview:** `IPreviewController.EnterPreviewMode(startPaused:true)` → `rrController.PrepareRecordingAsync(
    exerciseId, OrchestrationConstants.DefaultStagingDirectory)`.
  - **live:** publish `TransitionStateIntent{OperatingLive}` (records automatically via the live load handler).
- `POST /recording/stop` → `rrController.FinalizeRecordingAsync()`. **For preview: finalize BEFORE the exit
  rewind**, THEN `ExitPreviewMode()`. Return `{ fdpPath }` (the produced file).
- **Ordering (hard):** `EnterPreviewMode → PrepareRecordingAsync → (run workload) → FinalizeRecordingAsync →
  ExitPreviewMode`. Recording must NOT rewind while recording. **Mutually exclusive with checkpoint**
  (`/checkpoint` and `/recording/start` both use the preview slot — reject the second with a clear error).
  Reflect `recording:true` in `/status`.

### Replay (T02)
- `POST /replay/load {fdpPath}` → `new ReplayBrowserContext()` + `LoadRecording(fdpPath)`. Hold it as the
  active replay context; route queries to its `SandboxRepo` while active.
- `POST /replay/seek {frame}` → `SeekToFrame(frame)`; `POST /replay/step {dir:"forward"|"back"}` →
  `StepForward()`/`StepBackward()`.
- `POST /replay/unload` (or similar) → dispose the context, route queries back to `_world`.
- **Isolation (hard):** seeks/steps operate ONLY on `SandboxRepo`/`Playback` — they must NEVER touch `_world`
  or the live `MasterSyncController` (would desync the live kernel). The replay context is fully disconnected.

## MCP tools (Group I — keep server in lockstep, advances ADA-06-D01)
Add 1:1 tools (`start_recording`, `stop_recording`, `load_replay`, `seek_replay`, `step_replay`,
`unload_replay`). Update README tool table + ADA-06-D01 (I now present; J/K/L pending). Extend `verify.mjs`.

## Verification (prove the REAL round-trip)
- **Tier-1 (EditorHarness):** extend the harness to expose `rrController` (and build the replay context).
  1. Start preview recording → run a few frames (move/spawn) → stop → a `.fdp` exists on disk; after stop the
     world is rewound (preview revertible). 
  2. `/replay/load` that `.fdp` → replay-scoped `ListEntities` returns the recorded frame's entities.
  3. `seek`/`step` change replay-scoped state; assert a LIVE `_world` entity is UNCHANGED during replay seeking.
  4. Requesting `/checkpoint` during an active recording (and vice-versa) is rejected.
- **Tier-2 (live headless / MCP `verify.mjs`):** load test-move → `start_recording{preview}` → `step` a few →
  `stop_recording` (capture `fdpPath`) → `load_replay{fdpPath}` → `seek_replay{frame}` → replay-scoped
  `list_entities` non-empty → `get_status` shows live world intact → `unload_replay`. Re-runnable; NO orphans.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- Preview recording: finalize BEFORE rewind; never rewind mid-recording. Mutually exclusive with checkpoint.
- ExerciseId from `ClusterStateUpdateEvent` + `OrchestrationConstants.DefaultStagingDirectory` (don't mint own).
- Replay seeks NEVER touch `_world`/`MasterSyncController`; query the SandboxRepo while replay active.
- Reuse `EcsRecordReplayController` + `ReplayBrowserContext`; don't fork. Marshalling for any `_world` access.
- Frozen `TestAssets`; never the production scan path; never regenerate snapshots. NaN-safe serialization
  (BATCH-09) already covers replay-scoped dumps since they reuse the same extraction/DumpToJsonNode path.

## Deliverables
- Code + green Tier-1 + green live record→replay reproduce + green `npm run verify`; README updated.
- `.dev/ai-debug-api/reports/ADA-BATCH-10-REPORT.md` (DEV-GUIDE format): built, decisions/deviations (run-mode
  ordering, replay isolation), FULL `dotnet test` summary, the REAL reproduce output (fdpPath produced +
  replay entities + live-world-unaffected proof), blockers, debt → DEBT-TRACKER (update ADA-06-D01 for Group I).
