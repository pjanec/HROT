# ADA-BATCH-11: Logs Query (Group J) + Entity Filter/Spatial (Group B) + MCP tools

**Batch Number:** ADA-BATCH-11
**Tasks:** ADA-P5-T01 (logs query) + ADA-P7-T01 (entity filter + spatial) + MCP tools
**Phase:** Phases 5 + 7 — two simple, cohesive query/filter endpoints
**Estimated Effort:** ~8 hours
**Executor:** sonnet
**Priority:** MEDIUM
**Dependencies:** Phase 1 + P-MCP. Reuses the log sinks + spatial grid already available.

---

## Onboarding & Workflow

Two small read-side features: (J) filtered access to the in-memory log sinks, and (B+) server-side entity
filtering so the AI doesn't pull everything.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-09-REVIEW.md` + `ADA-BATCH-10-REVIEW.md` (the live-reproduce gate).
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group J (logs) + Group B (queries / filter).
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P5-T01, ADA-P7-T01.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY — the lead re-runs
> `dotnet test --filter DebugApi`, `npm run verify`, and a real headless reproduce. Run the full build.

### Existing infra to reuse
- **Logs:** `NLogMessageLogTarget.SharedInstance.GetMessages()` (thread-safe, lock-guarded → off-thread OK)
  and `AiBehaviorLogTarget`. `MessageLogEntry` has timestamp/level/logger/message. No query API on the sinks
  — filter in the endpoint.
- **Entity filter:** `ListEntities()` already extracts all entities (`EntityStateExtractionService`). For
  `?component=` filter by presence of the component name in the dump; for `?near=x,y,r` use the entity's
  position (the `SimTransform.Position` already in the dump, or the spatial grid) and a radius test.
- The MCP `list_entities` tool ALREADY declares `component` and `near` params (added in BATCH-06) and passes
  them as query string — so P7 is mostly the server-side filter logic; just confirm the wiring end-to-end.

---

## Endpoints
### Group J — Logs (ADA-P5-T01)
- `GET /logs?level=&logger=&since=&max=` → filter over `NLogMessageLogTarget.SharedInstance.GetMessages()`
  (+ `AiBehaviorLogTarget`); return `[{ timestamp, level, logger, message }]`. Off-thread (no `RunOnMainThread`
  needed — sinks are lock-guarded). `level` = minimum level or exact (decide + document); `since` = frame or
  timestamp (match what `MessageLogEntry` exposes); `max` bounds the count (default e.g. 200).

### Group B — Entity filter (ADA-P7-T01)
- Extend `GET /entities` with optional `?component=Foo` (only entities having that component) and
  `?near=x,y,r` (only entities within radius `r` of `(x,y)` using the position component). Both filters
  composable. No filter → current behavior (all entities).

## MCP tools
- Add `get_logs` (1:1 with `/logs`). The `list_entities` tool already has `component`/`near` — verify it
  passes them through to the query string correctly (fix if not). Update README tool table + ADA-06-D01 note
  (Group J now present). Extend `verify.mjs` with a logs query + a filtered entities query.

## Verification (ship tests; loop to green)
- **Tier-1 (EditorHarness):**
  - Logs: after emitting a known log line, `GET /logs?level=Info` includes it; `?logger=` and `?since=`
    narrow correctly; `?max=` bounds the count.
  - Filter: `?component=BrainBlackboard` returns only entities with that component; `?near=x,y,r` returns only
    entities within radius `r` (spawn/move entities to known positions to assert).
- **Tier-2 (live headless / MCP `verify.mjs`):** `get_logs` returns non-empty after load; `list_entities`
  with `component=` narrows the result vs unfiltered. Re-runnable; no orphans.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- Logs read off-thread (lock-guarded sinks); entity extraction marshalled (as today). Filtering in the
  endpoint (sinks/extraction have no query API). NaN-safe serialization (BATCH-09) already covers dumps.
- Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code + green tests; extended MCP `verify.mjs`; README updated.
- `.dev/ai-debug-api/reports/ADA-BATCH-11-REPORT.md` (DEV-GUIDE format): built, decisions (level/since
  semantics), FULL `dotnet test` summary, headless/MCP reproduce output (logs non-empty + filtered entities),
  blockers, debt → DEBT-TRACKER (update ADA-06-D01 for Group J).
