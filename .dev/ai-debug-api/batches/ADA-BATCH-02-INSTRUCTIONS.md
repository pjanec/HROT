# ADA-BATCH-02: Core Read & Control Endpoints (+ BATCH-01 correctives)

**Batch Number:** ADA-BATCH-02
**Tasks:** Corrective C1, C2 (from BATCH-01 review) + ADA-P1-T01, ADA-P1-T02, ADA-P1-T03, ADA-P1-T04
**Phase:** Phase 1 — Slice 1 Surface (part 1 of 2)
**Estimated Effort:** ~18 hours
**Executor:** sonnet (establishes the data-serialization + marshalling handler patterns the rest of P1 reuses)
**Priority:** HIGH
**Dependencies:** ADA-BATCH-01 (DebugApiHost, MainThreadJobQueue, EventSerializationHelper)

---

## Onboarding & Workflow

This batch turns the foundation into the first real, AI-usable surface: status, entity query/dump, event
history, sim/preview/time control, and scenario load/list/save. It also closes two BATCH-01 gaps.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. **BATCH-01 review (your correctives + guidance):** `.dev/ai-debug-api/reviews/ADA-BATCH-01-REVIEW.md`
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Groups A, B, C, D, E; *Threading & Marshalling Model*;
   *Wait-Gating Semantics*; *Run-Mode Model*.
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P1-T01..T04.

> Do **NOT** use the codebase-memory MCP. Verify facts against code. Do not git commit. Report honestly —
> the lead re-runs `dotnet test` and the headless smoke.

### Existing code to study / reuse
- `DebugApiHost`, `MainThreadJobQueue` (BATCH-01) — `Hrot/Subsystems/Hrot.Editor/DebugApi/`.
- `EditorSubsystem.cs` — the live services to expose: the serializer-injected
  `EntityStateExtractionService` (line ~783), `EditorTimeTransportFacade`/`_previewController`/`_timeController`,
  `_editorLogic`, `_orchestrationBus`, `_world`, `_entityMap`. Pass what's needed into the API service.
- `EditorTimeTransportFacade` — `Hrot/Subsystems/Hrot.Editor/UI/EditorTimeTransportFacade.cs`.
- `DiagnosticEventHistoryService.GetHistory` + `EventSerializationHelper` (BATCH-01).
- `IEditorLogic` (`LoadScenarioByName`, `AvailableScenarios`), `ScenarioFileService.SaveScenario`,
  `ClusterStateUpdateEvent` (load-completion signal).
- `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — the test world (Repo, Bus, OrchBus, Editor,
  Preview, time controller, EntityMap).

---

## Corrective Task-0 (do FIRST)
- **C1 — EventSerializationHelper tests.** Add xUnit tests proving: a `SpawnEntityCommand` with a boxed
  `EntityInfo` in `InitialComponents` serializes with the name readable; a struct event with a fixed-buffer
  / blackboard-like field serializes to a readable DTO (not raw bytes); (entity-ref resolution stays
  deferred per ADA-01-D01 — assert the no-resolver path only). Fix the helper if any fail.
- **C2 — Un-skip the headless smoke.** Convert the BATCH-01 `[Fact(Skip=…)]` process smoke into a real
  test (launch `-m editor --debug-api --headless`, poll `GET /status` for 200, `POST /shutdown` **with a
  Content-Length / body**, assert clean exit). Gate behind an env var/trait if it can't run in plain CI,
  but make it runnable.

---

## Architecture to establish (followed by all later endpoint batches)
1. **Testable service layer.** Extract handler logic into a `DebugApiService` (or similar) constructed from
   the editor's services (repo, extraction service, time facade, preview, editor logic, orch bus, history
   service). `DebugApiHost` maps routes → `DebugApiService` methods via the job queue. **Tests target
   `DebugApiService` against `EditorHarness`** (fast, no HTTP) — this is the Tier-1 gate.
2. **Envelope must not re-serialize domain data with CamelCase.** Domain payloads are produced as
   `System.Text.Json.Nodes.JsonNode` via the DTO path (`EventSerializationHelper`,
   `EntityStateExtractionService`) and embedded in the envelope's `data` as a `JsonNode` so it passes
   through verbatim. Change `ApiResponse.Data` to `JsonNode?` (or serialize so a JsonNode is not
   re-cased). **Never** hand a raw domain object to the host's CamelCase serializer.
3. **Marshalling.** Every world-touching handler runs via `MainThreadJobQueue.RunOnMainThread`. Event
   history + log reads stay off-thread. Refactor `DebugApiHost` routing from if/else to a small route table.

---

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Groups A–E)
- **A/B:** `GET /status` (full payload), `GET /entities`, `GET /entities/{networkId}` (serializer-injected dump).
- **C:** `GET /events?bus=world|orchestration&type=&since=&max=` (payload via `EventSerializationHelper`).
- **D:** `GET /sim/state`, `POST /sim/play|pause|step|timescale`, `POST /preview/enter|exit` (via the facade).
- **E:** `GET /scenarios`, `POST /scenario/load {name, waitForReady?}` (poll `ClusterStateUpdateEvent` for
  `OperatingEdit`; do NOT use `LoadedScenarioName`), `POST /scenario/save {name}`.

## Verification (ship tests; loop to green)
- **Tier-1 (EditorHarness, the gate):** xUnit tests for each endpoint's `DebugApiService` method —
  e.g. after `harness.Editor.LoadScenarioByName(<frozen test scenario>)` + pumping to `OperatingEdit`,
  `ListEntities()` returns N; `DumpEntity(id)` shows a readable blackboard DTO; `GetEvents("world")`
  includes a published event with readable payload; `Step(3)` advances 3 ticks; `LoadScenario(waitForReady)`
  only returns at `OperatingEdit`; `SaveScenario` round-trips.
- **Tier-2 (process smoke, extended):** add `GET /entities` and `GET /sim/state` to the smoke test.
- Commands: `dotnet build IOS-IG-SimHost.sln`; `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests`.

## Constraints (hard)
- Frozen `TestAssets` fixtures + direct deserialize — never the production scan path or scratch `.bp.json`;
  never regenerate snapshots to pass.
- No ImGui/Raylib/world access off the main thread. No ASP.NET Core.
- Reuse the editor's existing serializer-injected `EntityStateExtractionService` (not a raw 2-arg one).

## Deliverables
- Code + tests green via both `dotnet` commands; extended smoke.
- `.dev/ai-debug-api/reports/ADA-BATCH-02-REPORT.md` (DEV-GUIDE format): what built, decisions/deviations,
  FULL `dotnet test` summary, blockers, debt → `.dev/ai-debug-api/DEBT-TRACKER.md`.

> Report honestly; the lead re-runs the tests and the headless smoke and reads the diff.
