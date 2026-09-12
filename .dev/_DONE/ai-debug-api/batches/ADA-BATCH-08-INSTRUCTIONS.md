# ADA-BATCH-08: Checkpoint / Restore + State Diff (Group H) + MCP tools

**Batch Number:** ADA-BATCH-08
**Tasks:** ADA-P3-T01 (checkpoint/restore) + ADA-P3-T02 (state diff) + Group H MCP tools
**Phase:** Phase 3 — experiment-and-revert from an exact state + token-efficient state comparison
**Estimated Effort:** ~16 hours
**Executor:** sonnet (run-mode guards + diff tree + snapshot-slot coordination are subtle)
**Priority:** HIGH (leverage tier)
**Dependencies:** Phase 1 + P-MCP + BATCH-07. Reuses `IPreviewController` (already wired) +
`ComponentDiffService` + the entity serializer path.

---

## Onboarding & Workflow

Two related capabilities: (H1) a single-slot revertible checkpoint so the AI can snapshot, experiment, and
roll back to the exact prior state; (H2) a state diff so it can compare before/after cheaply (token-efficient
— a tree of only what changed, not a full dump).

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/_DONE/ai-debug-api/reviews/ADA-BATCH-06-REVIEW.md` + `ADA-BATCH-07-REVIEW.md` (gate discipline; full-build
   on ctor/interface change; the "prove the real thing on the live process" lesson).
3. **Design:** `.dev/_DONE/ai-debug-api/DESIGN.md` — Group H (checkpoint/restore/diff, preview-run only) + the
   Run-Mode Model section.
4. **Task detail:** `.dev/_DONE/ai-debug-api/TASK-DETAIL.md` — ADA-P3-T01, ADA-P3-T02 (authoritative + Success).

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY — the lead re-runs
> `dotnet test --filter DebugApi`, a REAL headless reproduce (checkpoint→move→restore→entity reverts;
> capture→move→diff shows only the moved component), AND `npm run verify`, and reads the full diff. Round-trip
> + green unit tests are NOT enough for these — the revert and the diff must be proven on the live process.

### Existing infra to reuse (do NOT reinvent)
- **`IPreviewController`** — already wired into `DebugApiService` (BATCH-02 added `/preview/enter` +
  `/preview/exit`). Methods: `EnterPreviewMode(startPaused:true)` (single-slot RAM snapshot),
  `ExitPreviewMode()` (rewind to snapshot). USE THIS FACADE ONLY — never `PreviewClusterOpHandler` directly.
- **`ComponentDiffService.ComputeTreeDiff(before, after, epsilon) → DiffNode`** tree
  (`Fdp.Toolkits.ReplayBrowser/Diff`, pure/UI-free). The serialized entity-state objects are the before/after.
- The entity serializer path already in `DebugApiService` (`EntityStateExtractionService` / the same path
  `DumpEntity`/`ListEntities` use) — produce the before/after snapshots with it. Marshal entity extraction
  to the main thread.
- Run-mode state is observable via `/status` (`inPreview`, `recording`) — reuse the existing status fields.

---

## CRITICAL interplay (read before designing the API)
Checkpoint and the existing `/preview/enter`+`/preview/exit` share the **same single preview slot** — both
go through `IPreviewController.EnterPreviewMode`/`ExitPreviewMode`. There is exactly ONE slot. So:
- `POST /checkpoint` must coordinate with preview state: if already in preview (entered via `/preview/enter`
  OR a prior `/checkpoint`), define clear behavior — either reject with a clear error or treat as "already
  checkpointed". Do NOT double-enter (that would corrupt or no-op the snapshot).
- `/status.inPreview` must reflect the checkpoint accurately (a checkpoint IS a preview-mode entry).
- Reject `POST /checkpoint` when a **live run** is active (mutually exclusive) → `409`.
- Document the chosen semantics in the report so the overlap with `/preview/*` is explicit, not accidental.

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Group H)
- `POST /checkpoint` → `EnterPreviewMode(startPaused:true)`; `409` if a live run is active. Return current state.
- `POST /checkpoint/restore` → `ExitPreviewMode()` (rewind). Return current state.
- `POST /diff` — capture/compare. Recommended shape (pick the cleanest, document it): a capture step that
  serializes the requested entities' state server-side and returns a `baselineId`, and a compare step that
  serializes again and runs `ComputeTreeDiff(baseline, current, epsilon)` → `DiffNode` tree JSON. Also support
  diffing current state against the most-recent checkpoint snapshot when one exists. `entities?` optionally
  scopes which entities to capture/diff (default all).

## MCP tools (Group H — keep server in lockstep, advances ADA-06-D01)
Add 1:1 tools to `tools/ai-debug-mcp/src/index.mjs` (`checkpoint`, `restore_checkpoint`, and the diff
tool(s) matching your `/diff` shape). Update the README tool table + the ADA-06-D01 note (H now present;
I/J/K/L still pending). Extend `verify.mjs` with a checkpoint+diff flow (see below).

## Verification (ship tests; loop to green — prove the REAL behavior)
- **Tier-1 (EditorHarness):**
  1. `checkpoint` → move an entity (publish a transform change / use spawn+move) → pump → `restore` → the
     entity's `SimTransform` returns to the checkpointed value (assert position equality within epsilon).
  2. `checkpoint` while a live run is active → `409`/error. `/status.inPreview` reflects checkpoint state.
  3. diff: capture → move an entity → diff → the tree shows ONLY the changed component (e.g. `SimTransform`);
     an unchanged entity yields no diff nodes (within epsilon); entity birth/death between snapshots appears
     in the tree.
- **Tier-2 (MCP `verify.mjs`, ENV-gated headless smoke as needed):** after load + play(paused), `checkpoint`
  → move (e.g. spawn an entity or send a command) → `restore` → assert revert; and a `capture → change →
  diff` asserting a non-empty change tree. Re-runnable via `npm run verify`. NO orphan processes.
- `dotnet build IOS-IG-SimHost.sln` (full build — `DebugApiService` ctor may change; harness ripples);
  `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- `IPreviewController` facade ONLY; never `PreviewClusterOpHandler`. Single slot; keyed multi-checkpoints OUT
  of scope (deferred). Coordinate with `/preview/*` (see interplay).
- Diff tree via `ComponentDiffService.ComputeTreeDiff`; don't hand-roll a diff. Serialize via the existing
  serializer path; entity extraction marshalled to the main thread.
- Live run active → checkpoint rejected (`409`). Reflect run-mode in `/status`.
- Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code + green tests; extended MCP `verify.mjs`; README updated.
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-08-REPORT.md` (DEV-GUIDE format): built, decisions/deviations (incl.
  the explicit `/checkpoint` ↔ `/preview/*` slot semantics), FULL `dotnet test` summary, the REAL
  headless/MCP reproduce output (revert proven + diff tree shown), blockers, debt → DEBT-TRACKER (update
  ADA-06-D01 for Group H).
