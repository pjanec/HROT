# ADA-BATCH-02 Review

**Verdict:** ACCEPTED (good parts) **with one P1 corrective** → BATCH-03 Task-0.
**Reviewer:** dev lead (diff + independent `dotnet test` + headless process smoke).

## Verified independently
- `dotnet test … --filter DebugApi` → **20/20 passed** (re-run by lead). Build: 0 errors.
- Diff review: `DebugApiService` (clean, one method/endpoint, JsonNode payloads via DTO path),
  `DebugApiHost` (route table, `ApiResponse.Data` as `JsonNode` embedded verbatim, marshalled handlers,
  events off-thread). Tests are **real and meaningful** (readable name survives, 404, payload contains
  decoded string, idempotent play/pause), not stubs.
- **C1 (resolved):** EventSerializationHelper tests added — and **caught a real bug**: `FixedString32/64`
  was emitting a raw 64-byte array; fixed to emit the decoded string (`DtoDiagnosticMapper`). This is the
  exact gap the corrective existed to find. ✅
- **Headless process smoke (run by lead):** `GET /status` (full payload), `GET /scenarios` (real list:
  NewScenario, test-move, "hill attack 2", …), `GET /sim/state`, `POST /shutdown` (clean exit 0) — all ✅.

## P1 CORRECTIVE → BATCH-03 Task-0
- **Scenario load via API does not complete in headless `-m editor`.** Reproduced by lead:
  `POST /scenario/load {name:"test-move", waitForReady:true}` → **504** "did not reach OperatingEdit within
  600 ticks"; `entityCount` stayed **0**; `/entities` empty. The agent's claim that this "works in
  production EditorSubsystem" was NOT verified and is contradicted by the headless run.
  - **Investigate:** is it (a) the poll budget too short / frames too fast for the async genesis pipeline
    (`LoadScenarioByName` loads from disk async; 600 fast headless frames may elapse first), (b) the wrong
    trigger (`LoadScenarioByName` insufficient in headless), or (c) the wrong completion signal source
    (`_clusterState()` never observes `OperatingEdit` in editor)?
  - **Fix** the load endpoint so a scenario actually materializes entities, and **add an integration test
    that loads a real scenario and asserts `entityCount > 0`** — via the real `EditorSubsystem` path (or a
    harness extended with the orchestrator), NOT the bare `EditorHarness`. Scenario load is fundamental to
    the whole testing premise — it must work end-to-end.
  - Until fixed, name-based `SaveScenario` is also only verified saving an *empty* world (the smoke saved
    entityCount=0), so its meaningful round-trip is unverified too.

## Accepted-as-debt (logged in DEBT-TRACKER)
- ADA-02-D01 (waitForReady poll coarseness — folds into the corrective above),
  ADA-02-D02 (SaveScenarioAs env-dependent root; hermetic name-based round-trip uncovered),
  ADA-02-D03 (headless smoke env-gated), ADA-02-D04 (Step exact-N is loop-coupled; test asserts
  non-regression only).

## Notes / guidance for BATCH-03
- Endpoint path naming is slightly inconsistent (`/scenarios` list vs `/scenario/load|save` singular) —
  harmless, but pick one convention and keep the MCP tool names aligned with the design table.
- Follow the established `DebugApiService` + JsonNode-payload + `RunOnMainThread` patterns for all new endpoints.
