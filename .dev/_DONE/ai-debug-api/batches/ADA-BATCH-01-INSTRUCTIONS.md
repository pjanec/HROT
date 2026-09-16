# ADA-BATCH-01: Web Host Foundation

**Batch Number:** ADA-BATCH-01
**Tasks:** ADA-P0-T01, ADA-P0-T02, ADA-P0-T03, ADA-P0-T04
**Phase:** Phase 0 — Web Host Foundation
**Estimated Effort:** ~18 hours (greenfield)
**Executor:** sonnet (subtle concurrency + serialization internals; T04 is mechanical)
**Priority:** HIGH — prerequisite for every later phase
**Dependencies:** none

---

## Onboarding & Workflow

This batch builds the opt-in in-process HTTP host, the main-thread marshalling queue, and the two
shared serialization/schema helpers every later group depends on. No capability endpoints yet beyond a
minimal `/status` and `/shutdown`.

### Required reading (IN ORDER)
1. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md` (coding standards, build/test, batch report format).
2. **Design:** `.dev/_DONE/ai-debug-api/DESIGN.md` — *Component Architecture*, *Threading & Marshalling Model*,
   *New Work* items #2 and #4.
3. **Task detail:** `.dev/_DONE/ai-debug-api/TASK-DETAIL.md` — ADA-P0-T01..T04 (Scope / Constraints / Success Conditions).
4. **Verification strategy:** `.dev/_DONE/ai-debug-api/TASK-TRACKER.md` — *Verification strategy* section.

> Do **NOT** use the codebase-memory MCP. Use the IDE's own search/grep/read. Verify every fact against
> the code before writing it.

### Existing code to study
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — `Initialize` / `Update` / `Shutdown`; the services
  to hand to the host (`_world`, `_editorLogic`, time controller, etc.). This is where the host is owned.
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — the window loop vs `orchestrator.Run()` headless loop;
  `ConsoleCommandService` + `orchestrator.EnqueueConsoleAction` / `DrainConsoleActions` (the marshalling
  precedent to mirror).
- `Hrot/Runner/Hrot.ClusterRunner/SubsystemOrchestrator.cs` — `DrainConsoleActions` + frame order.
- `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` — add the new flags.
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/` — `DtoDiagnosticMapper` (promote `internal`→`public`),
  `EntityStateExtractionService` (the DTO machinery reference).
- `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` — `Indented` / `DefaultRelaxed`.
- `FDP/Engine/Fdp.Core/FlightRecorder/FdpAutoSerializer.cs` — `GetSortedMembers(Type)` (for the schema helper).
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` + `HarnessSmokeTests.cs` — the test
  harness you will write integration tests against.

### New files (indicative; follow existing namespace/layout conventions)
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` — HttpListener, route table, JSON envelope.
- `Hrot/Subsystems/Hrot.Editor/DebugApi/MainThreadJobQueue.cs` — thread-safe queue + `RunOnMainThread<T>`.
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiConfig.cs` (or extend `HrotRunnerConfiguration`).
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EventSerializationHelper.cs`.
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/JsonShapeDescriber.cs`.
- Edits: `EditorSubsystem.cs` (construct/pump/dispose host), `HrotRunnerConfiguration.cs` (`--debug-api`,
  `--debug-api-port`), `Program.cs` (clean `/shutdown` exit signal), `DtoDiagnosticMapper.cs` (visibility).

### Build & test
```
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests
```

---

## Task scope (authoritative spec in TASK-DETAIL.md)

- **ADA-P0-T01 — DebugApiHost skeleton.** HttpListener (loopback), routing, `{ok,data,error,awaited}`
  envelope, `--debug-api`/`--debug-api-port` flags, opt-in construction in `EditorSubsystem`, `/status`
  placeholder, `POST /shutdown` clean-exit signal.
- **ADA-P0-T02 — MainThreadJobQueue.** Thread-safe enqueue + `TaskCompletionSource`; drained in
  `EditorSubsystem.Update()` **after kernel tick, before draw**; `RunOnMainThread<T>` helper; faults isolate.
- **ADA-P0-T03 — EventSerializationHelper.** Promote `DtoDiagnosticMapper` to `public`; add
  `EventSerializationHelper.SerializeToJson(object, IGuidResolver?)` (DTO mapper → `FdpJsonOptionsRegistry`);
  optional `Entity`→networkId resolution. Must handle fixed-buffers, `[InlineArray]`, boxed `List<object>`.
- **ADA-P0-T04 — JsonShapeDescriber.** `Describe(Type)` → `[{name,type}]` via `GetSortedMembers`.

## Verification (this batch ships its own tests; loop until green)

- **Tier-1 (xUnit, EditorHarness):**
  - `MainThreadJobQueue`: enqueue from a non-main thread, assert it runs on the harness's main pump and
    returns the value; a throwing job faults its task without killing the pump; 20 concurrent jobs complete.
  - `EventSerializationHelper`: serialize a `SpawnEntityCommand` with a boxed `EntityInfo` component →
    assert the name is present/readable; serialize a struct with a fixed-buffer field → readable DTO; with
    a resolver, an `Entity` field → networkId.
  - `JsonShapeDescriber`: `Describe(typeof(SpawnEntityCommand))` lists the expected fields/types; enum→string.
  - `DtoDiagnosticMapper` is publicly accessible from `Fdp.Toolkit.Diagnostics`; existing callers compile.
- **Tier-2 (process smoke):** a test (or a documented manual step) launches
  `dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- -m editor --debug-api --debug-api-port 8099 --headless`,
  polls `GET http://localhost:8099/status` for `200 {ok:true}`, then `POST /shutdown` and asserts clean exit.
  **If the process cannot start headless in your environment, STOP and report it as a blocker** (do not
  stub or fake it) — this proves the whole transport tier.

## Constraints (hard)
- No ASP.NET Core / DI / generic host. `HttpListener` only. Loopback by default. Host never constructed
  when the flag is absent.
- No ImGui / Raylib / `MapCamera` / `_world` / `NetworkEntityMap` access from background threads.
- Tests use frozen `TestAssets` fixtures + direct deserialize; never the production scan path or scratch
  `.bp.json`; never regenerate snapshots to pass.

## Deliverables
- Code + tests above, all green via the two `dotnet` commands.
- A batch report at `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-01-REPORT.md` (per DEV-GUIDE format): what was
  built, decisions, any deviations, test results (paste the `dotnet test` summary), and any debt → add rows
  to `.dev/_DONE/ai-debug-api/DEBT-TRACKER.md`.

> **Report honestly.** Do not claim green you didn't run. The lead will re-run `dotnet test` and read the
> diff, not just the report.
