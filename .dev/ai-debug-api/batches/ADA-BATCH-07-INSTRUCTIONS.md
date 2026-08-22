# ADA-BATCH-07: Breakpoints — Run-Until-Condition (Group G) + MCP tools

**Batch Number:** ADA-BATCH-07
**Tasks:** ADA-P2-T01 (breakpoint endpoints + `SearchPredicateDto` JSON + hit observation) + Group G MCP tools
**Phase:** Phase 2 — the autonomous-testing leverage feature (run, auto-pause on a condition, inspect)
**Estimated Effort:** ~16 hours
**Executor:** sonnet (polymorphic `SearchPredicateDto` JSON + hit observation across ticks are subtle)
**Priority:** HIGH (first leverage-tier feature)
**Dependencies:** Phase 1 + P-MCP complete. Reuses the editor's already-wired `_bpManager`.

---

## Onboarding & Workflow

Add data/event breakpoints that auto-pause the sim when a condition is met — the key primitive for
autonomous testing ("run until entity X's health < 10, then inspect"). The engine infra exists and is wired
in the editor; this batch exposes it over HTTP + MCP.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-05-REVIEW.md` + `ADA-BATCH-06-REVIEW.md` (gate discipline; the
   full-build-on-interface-change and by-hand audit lessons).
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group G (run-until-condition / breakpoints).
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P2-T01 (authoritative spec + Success Conditions).

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY — the lead re-runs
> `dotnet test --filter DebugApi`, the ENV-gated headless smoke / a real reproduce, AND re-runs the MCP
> `npm run verify`, and reads the full diff. The gate has caught narrowed/false "done" repeatedly. If a
> Success Condition can't be met in the bare harness, log debt and say so — never fake a hit.

### Existing infra to reuse (do NOT reinvent)
- **`IDataBreakpointManager`** (`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/`):
  `AddBreakpoint(SearchPredicateDto condition, Entity? filter=null, int occurrenceThreshold=1,
  string displayName="", Guid? sourceElementId=null) → BreakpointId`; `Remove(BreakpointId)`;
  `AllBreakpoints`; `PausedTick`; events `OnBreakpointHit(Breakpoint, Entity)` / `OnPauseStateChanged(bool)`;
  plus a continue/resume method (find it — likely `RequestContinue`). `BreakpointId` is a stable id type.
- The editor already builds `_bpManager` (`EditorSubsystem.cs:1013`, `DataBreakpointManager`) +
  `DataBreakpointSystem(_bpManager, _world.Bus)` (`:1016`), and exposes it internally as
  `DataBreakpointManager` (`:505`). Pass `_bpManager` into `DebugApiService` (new ctor param, mirror how
  tkbDb/geoTransform were added in BATCH-05).
- **`SearchPredicateDto`** (`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`) — the
  polymorphic predicate hierarchy (PropertyMatch / TransientEvent / Compound / …). Its JSON
  (de)serialization is ALREADY solved and proven — study
  `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/SearchPredicateDtoSerializationTests.cs` and reuse the
  EXACT same `JsonSerializerOptions`/converter/discriminator config. Do not invent a parallel polymorphic
  scheme.

---

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Group G)
- `POST /breakpoints {condition, filterNetworkId?, occurrenceThreshold?, name?}` — deserialize `condition`
  (polymorphic `SearchPredicateDto`), resolve `filterNetworkId` → `Entity` via `NetworkEntityMap` (main
  thread), `AddBreakpoint(...)` → `{ breakpointId }`.
- `GET /breakpoints` → `AllBreakpoints` (id, condition summary, enabled, occurrenceThreshold, hitCount, name).
- `DELETE /breakpoints/{id}` → `Remove`.
- `GET /breakpoints/hits` → `{ isPaused, pausedTick, lastHit:{ breakpointId, networkId } }`. Subscribe to
  `OnBreakpointHit` / `OnPauseStateChanged` (on the main thread) and store the last hit + pause state.

## MCP tools (keep the server in lockstep — Group G, partially closes ADA-06-D01)
Add to `tools/ai-debug-mcp/src/index.mjs`, 1:1 with the new endpoints: `set_breakpoint`, `list_breakpoints`,
`remove_breakpoint`, `get_breakpoint_status`. Update the README tool table and the ADA-06-D01 note (G now
present; H/I/J/K/L still pending). Extend `verify.mjs` with a breakpoint flow (see below).

## Verification (ship tests; loop to green)
- **Tier-1 (EditorHarness):** extend `EditorHarness` to build + expose a `DataBreakpointManager` +
  `DataBreakpointSystem` on its world (mirror the editor wiring) and pass it into `BuildDebugApiService`, so
  the system actually ticks during `PumpFrames`/`PumpUntil`.
  1. POST a `PropertyMatchDto` (a component field compared to a value), then drive the sim (play/pump);
     `GET /breakpoints/hits` shows `isPaused:true` with the triggering `networkId` once the condition is met.
  2. A `TransientEventPredicateDto` breakpoint pauses on the event firing.
  3. `DELETE` removes it; subsequent runs do not pause.
  4. A `CompoundPredicateDto` (AND) round-trips through JSON and compiles.
  > If genuinely driving a hit in the bare harness is not achievable (e.g. the predicate needs a system the
  > harness doesn't run), prove the round-trip + registration + the hit-observation plumbing via a directly
  > injected `OnBreakpointHit` invoke, and log the end-to-end-hit coverage gap as debt — do NOT fake
  > `isPaused`.
- **Tier-2 (MCP `verify.mjs`):** after load + play, `set_breakpoint` → drive → `get_breakpoint_status`
  reflects the pause (or, if a real hit isn't drivable headless, assert set/list/remove round-trip and note
  it). Keep it re-runnable via `npm run verify`.
- `dotnet build IOS-IG-SimHost.sln` (full build — `DebugApiService` ctor change ripples to the harness);
  `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- Polymorphic `SearchPredicateDto` (de)serialization MUST reuse the proven options from the existing
  serialization test — verify a `CompoundPredicateDto` round-trips byte-faithfully.
- Reuse the editor's wired `_bpManager`; do not construct a second manager in the editor path.
- Hit observation (event subscription, `NetworkEntityMap` lookups) on the main thread / marshalled.
- Unknown/blank/invalid `condition` → `400` (not a crash). Unknown `filterNetworkId` → `400`.
- Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code + tests green; extended MCP `verify.mjs`; README updated.
- `.dev/ai-debug-api/reports/ADA-BATCH-07-REPORT.md` (DEV-GUIDE format): built, decisions/deviations, FULL
  `dotnet test` summary, the headless/MCP verify output, blockers, debt → DEBT-TRACKER (incl. any
  end-to-end-hit coverage gap, and update ADA-06-D01 for Group G).
