# ADA-BATCH-01 Review

**Verdict:** ACCEPTED (with two corrective items carried to BATCH-02).
**Reviewer:** dev lead (diff + independent test/smoke run, not the agent's report).

## Verified independently
- `dotnet test …Integration.Tests --filter DebugApiFoundationTests` → **8/8 passed, 0 failed** (re-run by lead; build green, 0 new warnings).
- **Headless process smoke (run by lead, since the agent skipped it):**
  `dotnet Hrot.ClusterRunner.dll -m editor --debug-api --debug-api-port 8099 --headless`
  - `GET /status` → `{"ok":true,…}` — the headless editor starts and serves HTTP. **#1 de-risk proven.**
  - `POST /shutdown` (with Content-Length) → `{"ok":true}` and process exits **cleanly (exit 0)** via
    `orchestrator.Stop()` → `_running=false` → `Run()` loop exits.

## Code review (diff)
- `MainThreadJobQueue` — marshalling correct (ConcurrentQueue + TCS, faults isolated, `RunOnMainThread<T>` re-throws inner). ✅
- `DebugApiHost` — loopback bind, async accept loop, responds before invoking shutdown. ✅
- `EditorSubsystem` — `DrainAll()` at the correct post-`_kernel.Update()` quiescent point; host disposed before kernel teardown. ✅
- `HrotRunnerConfiguration` / `Program.cs` — flags + opt-in wiring (`orchestrator.Stop` as shutdown callback). ✅
- `EventSerializationHelper` / `JsonShapeDescriber` — code plausible and clean. ⚠ see Corrective C1.
- `FdpAutoSerializer.GetSortedMembers` internal→public — fine. `DtoDiagnosticMapper` was already public (no-op).

## Corrective items → BATCH-02 Task-0 (P1)
- **C1 — EventSerializationHelper is unverified.** ADA-P0-T03 success conditions (boxed `List<object>`
  e.g. `SpawnEntityCommand.InitialComponents`, fixed-buffer/blackboard field, `Entity`→networkId with a
  resolver) were NOT tested. Add these xUnit tests; fix the helper if they fail. Load-bearing — every
  event/trace/TKB payload flows through it.
- **C2 — Un-skip the headless smoke.** Convert the `[Fact(Skip=…)]` process smoke into a real
  (or CI-gated) test now that the lead has proven it works, so it can't silently regress.

## Notes / guidance for BATCH-02
- **Envelope must not re-serialize domain data with CamelCase STJ.** `DebugApiHost` serializes the whole
  `ApiResponse` (incl. `data`) with its own `JsonNamingPolicy.CamelCase` options. Domain payloads
  (entities, events, TKB descriptors) must be produced via the DTO path
  (`EventSerializationHelper`/`EntityStateExtractionService`) and embedded as raw `JsonNode`/`JsonElement`
  — never re-serialized through the host's CamelCase options.
- **Refactor routing to a table** when endpoints multiply (currently hardcoded if/else for 2 routes).
- Use `MainThreadJobQueue.RunOnMainThread` for every world-touching handler.

## Debt logged
- ADA-01-D01 (carried) — entity-ref resolution deferred in `EventSerializationHelper`.
- ADA-01-D02 — POST endpoints require `Content-Length` (HttpListener 411 on bodyless POST); MCP/clients
  must send it (fetch does automatically); document for manual `curl` use.
- ADA-01-D03 — `/shutdown` breaks only the headless `Run()` loop, not the windowed Raylib loop; acceptable
  (API is headless-primary), revisit if windowed remote-shutdown is ever needed.
