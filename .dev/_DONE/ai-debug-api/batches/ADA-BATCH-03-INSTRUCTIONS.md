# ADA-BATCH-03: Fix scenario load (headless) — P1 corrective

**Batch Number:** ADA-BATCH-03
**Tasks:** P1 corrective for ADA-P1-T04 (scenario load completion)
**Phase:** Phase 1 corrective
**Estimated Effort:** ~8–12 hours (investigation-heavy; correctness-critical)
**Executor:** sonnet
**Priority:** P1 — scenario load is fundamental to the whole testing premise
**Dependencies:** BATCH-02 (the endpoints + DebugApiService)

> Focused single-objective batch on purpose: make scenario load actually work end-to-end in headless,
> with a real integration test. Do NOT add other endpoints in this batch.

---

## The problem (reproduced by the dev lead)

`POST /scenario/load {name, waitForReady:true}` does **not** complete in headless `-m editor`:
- Returns **504** "Scenario '…' did not reach OperatingEdit within 600 ticks".
- `GET /status` afterwards shows `entityCount: 0`; `GET /entities` is empty.
- Meanwhile `GET /status`, `GET /scenarios` (returns a real list incl. `test-move`, `hill attack 2`),
  `GET /sim/state`, `POST /shutdown` all work.

Reproduce exactly (the lead's command):
```
DLL=Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll
dotnet "$DLL" -m editor --debug-api --debug-api-port 8099 --headless &
curl -s --retry 50 --retry-delay 2 --retry-connrefused http://localhost:8099/status
curl -s -X POST -H "Content-Type: application/json" -d '{"name":"test-move","waitForReady":true}' http://localhost:8099/scenario/load
curl -s http://localhost:8099/status | grep -o '"entityCount":[0-9]*'   # observed: 0
curl -s -X POST -d '' http://localhost:8099/shutdown
```

## Investigate (verify against code; do NOT assume)
Determine which of these is the cause (or another):
1. **Poll budget / timing:** `LoadScenarioByName` kicks an async, multi-tick genesis pipeline that loads
   from disk; the host's 600-poll loop runs one poll per `Update` drain and headless frames are fast, so
   the budget may elapse before the async load + genesis complete. (If so: wait on a real completion
   signal / wall-clock, not a fixed fast-frame count.)
2. **Wrong trigger:** is `IEditorLogic.LoadScenarioByName` the complete trigger in headless, or does the
   editor's normal (GUI) load path do more (e.g. via `ClusterScenarioPanel` / a process manager that
   isn't ticked headless)? Check what actually drives `HrotEditLoadHandler` to materialize entities and
   whether all required process managers / `ClusterMaster.Tick` / `ClusterSlave.Tick` run in
   `EditorSubsystem.Update` headless.
3. **Wrong completion signal:** how is the `Func<ClusterState>` (`_clusterState`) wired into
   `DebugApiService` from `EditorSubsystem`? Does it ever observe `OperatingEdit`? Trace where editor
   cluster state is published (`ClusterStateUpdateEvent` on `_orchestrationBus`) and consumed.
4. **Time mode:** the editor starts paused (Deterministic, dt=0). Does the scenario genesis / entity
   creation pipeline require dt>0 or a pump it isn't getting? (`CreateEntityRequestSystem`,
   `GenesisMaterializationSystem`, `NetworkSpawningSystem` ticking.)

Relevant code: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (load handler wiring ~lines 809–940,
the `_orchestrationBus`/`_clusterMaster`/`_clusterSlave`, and how `ConfigureDebugApi`/`DebugApiService`
gets its `_clusterState`), `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs`
(`LoadScenarioByName`), `HrotEditLoadHandler`, `ClusterStateUpdateEvent`.

> Do NOT use codebase-memory MCP. Do NOT git commit. Report honestly.

## Fix + verify (the gate)
- Make `POST /scenario/load {name, waitForReady:true}` reliably reach a true "ready" state and
  **materialize the scenario's entities**.
- **Integration test (mandatory):** load a real scenario (e.g. `test-move`) and assert `entityCount > 0`
  after ready — via the **real `EditorSubsystem`** path (or an `EditorHarness` extended with the editor's
  orchestrator/cluster wiring so the load pipeline actually runs). The bare harness alone is insufficient
  (it has no `ClusterMaster`) — do NOT fake this with a spawn.
- Re-run the lead's headless reproduce command and confirm: load returns `200 {loaded, awaited:true}` and
  `entityCount > 0`.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter "FullyQualifiedName~DebugApi"`.

> **If the root cause is a deep editor-orchestration limitation you cannot resolve cleanly, STOP and
> write a blocker report with your findings** — do not paper over it (e.g. don't just bump the poll count
> blindly without proving entities load).

## Deliverables
- The fix + the mandatory integration test, green.
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-03-REPORT.md`: root cause (with evidence), the fix, the test, the
  headless reproduce output showing `entityCount > 0`, any residual debt.
