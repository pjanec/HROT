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

- [ ] **ADA-P0-T01** [sonnet] DebugApiHost: HttpListener, routing, JSON envelope, config flag, `/shutdown` [details](./TASK-DETAIL.md#ada-p0-t01--debugapihost-skeleton)
- [ ] **ADA-P0-T02** [sonnet] MainThreadJobQueue + `EditorSubsystem.Update` drain + background-thread guards [details](./TASK-DETAIL.md#ada-p0-t02--main-thread-job-queue)
- [ ] **ADA-P0-T03** [sonnet] `EventSerializationHelper` (promote `DtoDiagnosticMapper` public + Entity→networkId) [details](./TASK-DETAIL.md#ada-p0-t03--eventserializationhelper)
- [ ] **ADA-P0-T04** [zoo] CLR→JSON-schema helper for discovery [details](./TASK-DETAIL.md#ada-p0-t04--clrjson-schema-helper)

---

## Phase 1 — Slice 1 Surface (T1)

**Goal:** Queries, event history, sim/preview/time control, scenario load/list/save, generic command +
discovery, TKB catalog, world/coordinate info. The first end-to-end usable surface.

- [ ] **ADA-P1-T01** [zoo] Status + entity list/dump (Groups A/B) [details](./TASK-DETAIL.md#ada-p1-t01--status--entity-querydump)
- [ ] **ADA-P1-T02** [zoo] Event history endpoint (Group C) [details](./TASK-DETAIL.md#ada-p1-t02--event-history)
- [ ] **ADA-P1-T03** [zoo] Sim/preview/time control (Group D) [details](./TASK-DETAIL.md#ada-p1-t03--simpreviewtime-control)
- [ ] **ADA-P1-T04** [zoo] Scenario load/list/save (Group E) [details](./TASK-DETAIL.md#ada-p1-t04--scenario-loadlistsave)
- [ ] **ADA-P1-T05** [sonnet] Entity commands + `/commands` discovery + wait-gating (Group F) [details](./TASK-DETAIL.md#ada-p1-t05--entity-commands--discovery)
- [ ] **ADA-P1-T06** [zoo] `/components` + `/scenarios` discovery [details](./TASK-DETAIL.md#ada-p1-t06--componentsscenarios-discovery)
- [ ] **ADA-P1-T07** [zoo] TKB entity-type catalog (Group M) [details](./TASK-DETAIL.md#ada-p1-t07--tkb-entity-type-catalog)
- [ ] **ADA-P1-T08** [zoo] World/coordinate info + geo origin getter + geo↔local convert w/ orientation (Group N) [details](./TASK-DETAIL.md#ada-p1-t08--worldcoordinate-info)

---

## Phase 2 — Run-Until-Condition (T2)

**Goal:** Data/event breakpoints that auto-pause the sim; the autonomous-testing leverage feature.

- [ ] **ADA-P2-T01** [sonnet] Breakpoint endpoints + `SearchPredicateDto` JSON + hit observation (Group G) [details](./TASK-DETAIL.md#ada-p2-t01--breakpoints)

---

## Phase 3 — Checkpoint / Restore + Diff (T3)

**Goal:** Single-slot revertible snapshot (preview) + state diff.

- [ ] **ADA-P3-T01** [sonnet] Checkpoint/restore via `IPreviewController` + run-mode guards (Group H) [details](./TASK-DETAIL.md#ada-p3-t01--checkpointrestore)
- [ ] **ADA-P3-T02** [sonnet] Diff endpoint via `ComponentDiffService` (Group H) [details](./TASK-DETAIL.md#ada-p3-t02--state-diff)

---

## Phase 4 — Recording + Replay (T4)

**Goal:** Preview/live recording (finalize-before-rewind) + isolated headless replay.

- [ ] **ADA-P4-T01** [sonnet] Recording start/stop (preview + live) + run-mode exclusivity (Group I) [details](./TASK-DETAIL.md#ada-p4-t01--recording)
- [ ] **ADA-P4-T02** [sonnet] Isolated replay sandbox + seek/step + query-target swap (Group I) [details](./TASK-DETAIL.md#ada-p4-t02--isolated-replay)

---

## Phase 5 — Logs (T5)

**Goal:** Filtered access to the in-memory log sinks.

- [ ] **ADA-P5-T01** [zoo] Logs query endpoint + filtering (Group J) [details](./TASK-DETAIL.md#ada-p5-t01--logs-query)

---

## Phase 6 — AI Behavior Traces (T6)

**Goal:** Per-entity behavior-tree/HSM/blueprint trace extraction. Contains the one new engine seam.

- [ ] **ADA-P6-T01** [sonnet] Live trace arming seam (`AiTracerCoordinator` override + `TraceBufferLifecycleSystem` + `DebugMap`) [details](./TASK-DETAIL.md#ada-p6-t01--trace-arming-seam)
- [ ] **ADA-P6-T02** [sonnet] Trace extraction endpoints + JSON (Group K) [details](./TASK-DETAIL.md#ada-p6-t02--trace-extraction)

---

## Phase 7 — Entity Query / Filter + Spatial (T7)

**Goal:** Server-side entity filtering so the AI doesn't pull everything.

- [ ] **ADA-P7-T01** [zoo] Entity filter (`?component=`, `?near=x,y,r`) on list endpoint (Group B) [details](./TASK-DETAIL.md#ada-p7-t01--entity-filter--spatial)

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

- [ ] **ADA-PM-T01** [sonnet] MCP server scaffold (stdio, tool registry, fetch proxy, envelope passthrough) [details](./TASK-DETAIL.md#ada-pm-t01--mcp-scaffold)
- [ ] **ADA-PM-T02** [sonnet] Process lifecycle: launch + attach + graceful→hard kill [details](./TASK-DETAIL.md#ada-pm-t02--process-lifecycle)
- [ ] **ADA-PM-T03** [zoo] Tool definitions mirroring all endpoints 1:1 [details](./TASK-DETAIL.md#ada-pm-t03--tool-definitions)
