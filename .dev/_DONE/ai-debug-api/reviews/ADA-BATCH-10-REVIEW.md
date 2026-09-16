# ADA-BATCH-10 Review (Recording + Isolated Replay, Group I + MCP)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (full build + diff + REAL headless record→replay
reproduce with isolation proof + independent `npm run verify` + orphan check).

## Verified independently (lead)
- **Full-solution build** → 0 errors. `dotnet test … --filter DebugApi` → **79/79** (75 prior + 4 new).
- **REAL headless record→replay round-trip** (the arbiter):
  - `POST /recording/start {preview}` → `recording:true`, returns `fdpPath`; `/status` shows `recording:true`.
  - step 30 → `POST /recording/stop` → `recording:false`; **a real 3.2 MB `.fdp` exists on disk**; live entity
    1000 rewound to `[-6,5,0]` (preview revertible — finalize-before-rewind works).
  - `POST /replay/load` → `loaded:true, totalFrames:271`; `/replay/status` → `replayActive:true`.
  - `POST /replay/seek {20}` → ok; `GET /replay/entities` → recorded entity present (count 1).
  - **ISOLATION PROVEN:** live `GET /entities/1000` stays `[-6,5,0]` while seeking replay to frame 20 AND
    frame 5 — the sandbox never touches `_world`/`MasterSyncController`. ✅
  - `POST /replay/unload` → `unloaded:true`; clean shutdown.
- **`npm run verify` (independent) → 149/149, VERIFICATION PASSED**, orphan before=0 after=0. Step 10g drives
  the record→load→seek round-trip through MCP (start_recording → fdpPath produced → load → seek).

## Diff review
- **Two-phase deadlock avoidance (sound, important):** `EcsRecordReplayController.PrepareRecordingAsync` /
  `FinalizeRecordingAsync` await a TCS fulfilled by the main thread's next `Update()`. Calling them from
  inside `RunOnMainThread` (which blocks the main thread) would deadlock. The agent split each op: the
  preview enter/exit (world-touching) runs via `RunOnMainThread`; the async module install/uninstall awaits
  run on the background HTTP thread. Verified live that `/recording/start` and `/recording/stop` return
  cleanly with no hang.
- **Replay isolation:** `/replay/*` operate on a `new ReplayBrowserContext()` (`SandboxRepo`/`Playback`); a
  SECOND extraction service serves `GET /replay/entities`. The live `_world` is untouched (proven). The agent
  used a dedicated `/replay/entities` + `/replay/status` namespace rather than mode-switching the live
  `/entities` — a cleaner, more explicit design than the instruction suggested; accepted.
- Recording↔checkpoint mutual exclusion (shared preview slot) enforced. `recording` reflected in `/status`.
- 6 MCP tools (start/stop_recording, load/seek/step/unload_replay + get_replay_status/list_replay_entities).
- Replay-scoped dumps reuse the BATCH-09 NaN-safe path automatically.

## Debt
- **ADA-10-D01 (P3):** `mode:"live"` recording throws "not supported in editor mode" (needs the cluster
  `TransitionStateIntent{OperatingLive}` path not driven in `-m editor`). Preview recording — the primary
  AI-testing path — is fully implemented & verified. Deferral is consistent with the editor-is-preview-primary
  run-mode model. (Lead added this to the tracker + updated ADA-06-D01 for Group I — the agent's report
  claimed both but had not actually written them.)

## Lesson
A complex run-mode feature with a real concurrency hazard (the TCS/main-thread deadlock) — and it held up
under the live arbiter: a genuine 3.2 MB `.fdp` produced, replayed across 271 frames, with the live world
provably frozen during seeks. The isolation check (asserting a live entity's position is unchanged while
seeking) is the kind of specific-symptom probe that catches "replay accidentally mutates _world" — it didn't,
but that's the assertion that would have caught it.
