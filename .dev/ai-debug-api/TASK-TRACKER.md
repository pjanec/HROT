# Task Tracker — AI Debug & Test API (Editor) + MCP Server

**Reference:** [DESIGN.md](./DESIGN.md) for architecture; [TASK-DETAIL.md](./TASK-DETAIL.md) for per-task specs.
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md).

---

## Executor legend

- **[zoo]** — default executor (the Cline-based agent). Mechanical/reuse-heavy work over existing,
  verified APIs. **Trust the diff, not the report** — hard-review every batch regardless of its summary.
- **[sonnet]** — subtle, cross-cutting, or new-engine-seam work (concurrency, serialization internals,
  polymorphic JSON, run-mode ordering, engine flags). Execute via a sonnet agent; still hard-review.

All tasks: no test-fix loops handed back to the executor — lead reviews diffs and commits.
Tests must use frozen `TestAssets` fixtures + direct deserialize, never the production scan path or
scratch `.bp.json` (see Constraints in detail doc).

**Recommended execution order:** P0 → P1 → P-MCP (loop usable end-to-end) → P2/P3/P4 (leverage tier) → P5–P9.

---

## Verification strategy (autonomous loop)

Every batch ships tests; the loop self-verifies via `dotnet test`, so red can't be hidden.

- **Tier 1 — in-process integration tests (the gate).** Use the existing `EditorHarness`
  (`Hrot.ClusterRunner.Integration.Tests`), which builds a full offline editor world (`Repo`, `Bus`,
  `OrchBus`, `Kernel`, `EntityMap`, `Editor`/`IEditorLogic`, `Preview`/`IPreviewController`, time
  controller) with no DDS/window. Each task's **Success Conditions** become xUnit tests driving the API's
  service layer against the harness — no HTTP/MCP needed. This covers groups B/C/D/E/F/G/H/I/K/L/M/N.
- **Tier 2 — HTTP/MCP end-to-end smoke.** Launch `ClusterRunner -m editor --debug-api --headless`, hit
  endpoints (and via the Node MCP server), assert. Validates transport + lifecycle (P0, P-MCP). The
  offline editor has no DDS participant and headless skips the window, so process start is expected to work
  — proven in ADA-P0-T01.
- **Manual-verify only:** ADA-P9-T01 (focus-camera / gizmo annotations are visual).
- **Test hygiene:** frozen `TestAssets` fixtures + direct deserialize (never the production scan path or
  scratch `.bp.json`); never regenerate snapshots to make a test pass.

Each batch is **gated by these tests run by the lead** before commit (trust the diff + green/red, not the
agent's report).

---

## Phase 0 — Web Host Foundation (T0 + shared helpers)

**Goal:** The opt-in `DebugApiHost`, main-thread marshalling, and the shared serialization/schema helpers
every later group depends on. Prereq for everything.

- [x] **ADA-P0-T01** [sonnet] DebugApiHost: HttpListener, routing, JSON envelope, config flag, `/shutdown` — BATCH-01, lead-verified headless smoke (status+shutdown clean exit 0) [details](./TASK-DETAIL.md#ada-p0-t01--debugapihost-skeleton)
- [x] **ADA-P0-T02** [sonnet] MainThreadJobQueue + `EditorSubsystem.Update` drain + background-thread guards — BATCH-01 [details](./TASK-DETAIL.md#ada-p0-t02--main-thread-job-queue)
- [x] **ADA-P0-T03** [sonnet] `EventSerializationHelper` — BATCH-01 + BATCH-02 C1 (tests added; caught+fixed a real FixedString→raw-bytes bug) [details](./TASK-DETAIL.md#ada-p0-t03--eventserializationhelper)
- [x] **ADA-P0-T04** [zoo] CLR→JSON-schema helper for discovery — BATCH-01 [details](./TASK-DETAIL.md#ada-p0-t04--clrjson-schema-helper)

---

## Phase 1 — Slice 1 Surface (T1)

**Goal:** Queries, event history, sim/preview/time control, scenario load/list/save, generic command +
discovery, TKB catalog, world/coordinate info. The first end-to-end usable surface.

- [x] **ADA-P1-T01** [zoo] Status + entity list/dump (Groups A/B) — BATCH-02, lead-verified [details](./TASK-DETAIL.md#ada-p1-t01--status--entity-querydump)
- [x] **ADA-P1-T02** [zoo] Event history endpoint (Group C) — BATCH-02 [details](./TASK-DETAIL.md#ada-p1-t02--event-history)
- [x] **ADA-P1-T03** [zoo] Sim/preview/time control (Group D) — BATCH-02 (Step exact-N is debt ADA-02-D04) [details](./TASK-DETAIL.md#ada-p1-t03--simpreviewtime-control)
- [x] **ADA-P1-T04** [zoo] Scenario load/list/save (Group E) — BATCH-02 + BATCH-03 P1 corrective. Load now lead-verified in real headless (test-move → OperatingEdit, entityCount 1). Fixes: NAS storage provider + ClusterMaster roster seed + wall-clock poll [details](./TASK-DETAIL.md#ada-p1-t04--scenario-loadlistsave)
- [x] **ADA-P1-T05** [sonnet] Entity commands + `/commands` discovery + wait-gating (Group F) — BATCH-04, lead-verified (32/32 + real headless smoke: commands non-empty, spawn raises entityCount). Debt: ADA-04-D01 (ack-wait happy path, sanctioned), ADA-04-D02 (managed-event discovery → T06b) [details](./TASK-DETAIL.md#ada-p1-t05--entity-commands--discovery)
- [x] **ADA-P1-T06** [zoo] `/components` + `/scenarios` discovery — BATCH-04 (`/components` done; `/scenarios` already shipped as `/scenario/list` in BATCH-02) [details](./TASK-DETAIL.md#ada-p1-t06--componentsscenarios-discovery)
- [ ] **ADA-P1-T06b** [sonnet] Managed-event discovery for `/commands` (ADA-04-D02): surface managed events (`SpawnEntityCommand`, `MissionControlIntent`, …) — bus-level `GetRegisteredManagedEventTypes()` seam or assembly scan for a marker. Fold into next discovery-adjacent batch.
- [x] **ADA-P1-T07** [zoo→sonnet] TKB entity-type catalog (Group M) — BATCH-05, lead-verified (real headless: 15 types, M1 Abrams; descriptors via EventSerializationHelper). Minor debt ADA-05-D01 (disType cosmetic) [details](./TASK-DETAIL.md#ada-p1-t07--tkb-entity-type-catalog)
- [x] **ADA-P1-T08** [zoo→sonnet] World/coordinate info + geo origin getter + geo↔local convert w/ orientation (Group N) — BATCH-05, lead-verified (real headless: Berlin origin 52.52/13.405, 1000×1000 grid, origin→(0,0,0), round-trip OK; full-solution build clean — IGeographicTransform member addition safe across all implementers) [details](./TASK-DETAIL.md#ada-p1-t08--worldcoordinate-info)

---

## Phase 2 — Run-Until-Condition (T2)

**Goal:** Data/event breakpoints that auto-pause the sim; the autonomous-testing leverage feature.

- [x] **ADA-P2-T01** [sonnet] Breakpoint endpoints + `SearchPredicateDto` JSON + hit observation (Group G) — BATCH-07 (+1 fix round), lead-verified: 59/59 tests, full build clean, REAL e2e hit proven manually AND automated (verify.mjs Step 10c, 75/75): always-true PropertyMatch → isPaused:true, lastHit.networkId 1000, hitCount 1. 4 MCP tools (Group G closes part of ADA-06-D01). ADA-07-D01 RESOLVED [details](./TASK-DETAIL.md#ada-p2-t01--breakpoints)

---

## Phase 3 — Checkpoint / Restore + Diff (T3)

**Goal:** Single-slot revertible snapshot (preview) + state diff.

- [x] **ADA-P3-T01** [sonnet] Checkpoint/restore via `IPreviewController` + run-mode guards (Group H) — BATCH-08, lead-verified: REAL headless revert (checkpoint→spawn→step→restore reverts entityCount 2→1). 71/71 tests, full build clean, 95/95 verify, orphan-clean. Single-slot coordinated with /preview/* [details](./TASK-DETAIL.md#ada-p3-t01--checkpointrestore)
- [x] **ADA-P3-T02** [sonnet] Diff endpoint via `ComponentDiffService` (Group H) — BATCH-08 (/diff/capture + /diff/compare, DiffNode tree, births in union). Works for clean entities; NaN-entity serialization crash is pre-existing ADA-08-D02 → BATCH-09 corrective T0 [details](./TASK-DETAIL.md#ada-p3-t02--state-diff)

---

## Phase 4 — Recording + Replay (T4)

**Goal:** Preview/live recording (finalize-before-rewind) + isolated headless replay.

- [x] **ADA-P4-T01** [sonnet] Recording start/stop (preview + live) + run-mode exclusivity (Group I) — BATCH-10, lead-verified: real headless preview recording → 3.2 MB .fdp on disk, finalize-before-rewind, recording↔checkpoint exclusion. Two-phase split avoids a main-thread/TCS deadlock. Live mode deferred (ADA-10-D01) [details](./TASK-DETAIL.md#ada-p4-t01--recording)
- [x] **ADA-P4-T02** [sonnet] Isolated replay sandbox + seek/step + query-target swap (Group I) — BATCH-10, lead-verified: /replay/load (271 frames) + seek + /replay/entities; ISOLATION proven (live entity unchanged during seeks). 79/79 tests, npm verify 149/149 [details](./TASK-DETAIL.md#ada-p4-t02--isolated-replay)

---

## Phase 5 — Logs (T5)

**Goal:** Filtered access to the in-memory log sinks.

- [x] **ADA-P5-T01** [zoo→sonnet] Logs query endpoint + filtering (Group J) — BATCH-11, lead-verified live (/logs well-formed, level/logger/since/max filters). get_logs MCP tool. 91/91, verify 168/0 [details](./TASK-DETAIL.md#ada-p5-t01--logs-query)

---

## Phase 6 — AI Behavior Traces (T6)

**Goal:** Per-entity behavior-tree/HSM/blueprint trace extraction. Contains the one new engine seam.

- [x] **ADA-P6-T01** [sonnet] Live trace arming seam (`AiTracerCoordinator` override + `TraceBufferLifecycleSystem` + `DebugMap`) [details](./TASK-DETAIL.md#ada-p6-t01--trace-arming-seam)
- [x] **ADA-P6-T02** [sonnet] Trace extraction endpoints + JSON (Group K) [details](./TASK-DETAIL.md#ada-p6-t02--trace-extraction)

---

## Phase 7 — Entity Query / Filter + Spatial (T7)

**Goal:** Server-side entity filtering so the AI doesn't pull everything.

- [x] **ADA-P7-T01** [zoo→sonnet] Entity filter (`?component=`, `?near=x,y,r`) on list endpoint (Group B) — BATCH-11, lead-verified live (component→1/0, near radius incl/excl; XZ-plane). list_entities MCP params wired [details](./TASK-DETAIL.md#ada-p7-t01--entity-filter--spatial)

---

## Phase 8 — Live Mutation / Fault Injection (T8)

**Goal:** Discoverable attribute patching + arbitrary component edit.

- [ ] **ADA-P8-T01** [sonnet] Attribute patch via `JsonAttributeCompiler` + local `UpdateEntityAttributeRequestSystem` + `/attributes/schema` (Group L) [details](./TASK-DETAIL.md#ada-p8-t01--attribute-patch)
- [ ] **ADA-P8-T02** [sonnet] StructEdit component-edit escape hatch (Group L) [details](./TASK-DETAIL.md#ada-p8-t02--structedit-component-edit)

---

## Phase 9 — Manual-Session Assistance (T9)

**Goal:** Focus-on-entity + debug annotations for the human-in-the-loop.

- [ ] **ADA-P9-T01** [zoo] Focus-on-entity + gizmo annotations (Group F/manual-assist) [details](./TASK-DETAIL.md#ada-p9-t01--focus--annotations)

---

## Phase MCP — Node.js MCP Server (T-MCP)

**Goal:** External stdio MCP server proxying the API, with runner process lifecycle.

- [x] **ADA-PM-T01** [sonnet] MCP server scaffold (stdio, tool registry, fetch proxy, envelope passthrough) — BATCH-06, lead-verified (re-ran verify 50/50; thin callApi, localhost, envelope verbatim) [details](./TASK-DETAIL.md#ada-pm-t01--mcp-scaffold)
- [x] **ADA-PM-T02** [sonnet] Process lifecycle: launch + attach + graceful→hard kill — BATCH-06, lead-verified (launch→drive→stop end-to-end; independent Get-Process orphan check: 0 before/after, runner exit 0) [details](./TASK-DETAIL.md#ada-pm-t02--process-lifecycle)
- [x] **ADA-PM-T03** [zoo→sonnet] Tool definitions mirroring all endpoints 1:1 — BATCH-06, lead-verified (25 tools, full by-hand tool↔route audit vs DebugApiHost.cs — all paths match). G/H/I/J/K/L tools deferred until their endpoints exist (ADA-06-D01) [details](./TASK-DETAIL.md#ada-pm-t03--tool-definitions)
