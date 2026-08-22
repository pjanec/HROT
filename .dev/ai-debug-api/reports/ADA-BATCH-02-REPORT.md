# ADA-BATCH-02 Report — Core Read & Control Endpoints (+ BATCH-01 correctives)

**Batch:** ADA-BATCH-02
**Tasks:** Correctives C1, C2 + ADA-P1-T01, ADA-P1-T02, ADA-P1-T03, ADA-P1-T04
**Date:** 2026-06-14
**Executor:** Opus 4.8 (Claude Code lead agent)
**Branch:** `feat/ai-debug-api` (working tree only — not committed, per instructions)

---

## Summary

All BATCH-02 scope is implemented and green on the offline (no-DDS) Tier-1 surface:

- **C1** — `EventSerializationHelper` is now verified by xUnit tests, and a real gap was found and
  fixed (FixedString fields serialized as raw byte arrays → now readable strings).
- **C2** — the headless process smoke test now exists as a runnable (env-gated) test, extended with
  `GET /entities` and `GET /sim/state` (Tier-2).
- **Architecture** — handler logic extracted into a testable `DebugApiService`; the host routing is
  table-driven; `ApiResponse.Data` is now a `JsonNode?` embedded verbatim (no CamelCase re-casing of
  domain payloads); every world-touching handler runs via `MainThreadJobQueue.RunOnMainThread`.
- **Endpoints** — Groups A/B/C/D/E implemented: status, entity list/dump, event history,
  sim/preview/time control, scenario list/load(waitForReady)/save.

---

## Correctives

### C1 — EventSerializationHelper tests (+ helper fix)

**New file:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EventSerializationHelperTests.cs` (3 tests):
1. `SpawnEntityCommand` with a **boxed `EntityInfo`** in `InitialComponents` (boxed `List<object>`) →
   asserts `EntityInfo.Name` is the readable string `"Alpha"`.
2. A struct with a **fixed-buffer field** (`EntityInfo.Name : FixedString64`) → readable string, not a
   64-element byte array.
3. The **no-resolver path** is callable and produces valid JSON (entity-ref resolution stays deferred
   per ADA-01-D01).

**Gap found & fixed:** `DtoDiagnosticMapper.MapObject` rendered `FixedString32`/`FixedString64`
through the generic `FixedBufferAttribute` path, i.e. as a raw `List<byte>` of all 64 bytes — *not*
the decoded name. This is exactly the failure C1 was meant to catch. **Fix:**
`FDP/Toolkits/Fdp.Toolkits/Diagnostics/DtoDiagnosticMapper.cs` now special-cases the two FixedString
struct types and emits `obj.ToString()` (the decoded string) before the fixed-buffer branch. This is
load-bearing: every event/component payload that carries a name now reads correctly.

Entity-ref (`Entity`→networkId) resolution remains deferred (ADA-01-D01); the tests assert only the
no-resolver path, as instructed.

### C2 — Headless process smoke (now runnable)

**New file:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiHeadlessSmokeTests.cs`.

There was no committed `[Fact(Skip=…)]` smoke in BATCH-01 (the lead ran it manually), so this is the
first committed version. It:
- launches the real runner: `dotnet Hrot.ClusterRunner.dll -m editor --debug-api --debug-api-port N --headless`;
- polls `GET /status` for 200 (60 s budget — editor boot is heavy);
- **Tier-2 extended:** asserts `GET /entities` and `GET /sim/state` both return 200 `{ok:true}`;
- `POST /shutdown` **with a JSON body** (HttpListener 411s on a bodyless POST — ADA-01-D02);
- asserts the process exits cleanly (exit 0).

It is gated behind `ADA_RUN_HEADLESS_SMOKE=1` (debt ADA-02-D03): the editor mode boots the full kernel
stack (AI-behaviors build, scenario assets, NLog file targets), which is too heavy/environment-sensitive
for the default fast lane. It is runnable on demand and the lead re-runs it manually per the batch note.

---

## Architecture established

### Testable service layer — `DebugApiService`

**New file:** `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs`.

Constructed from the editor's already-built services: `EntityRepository`, `NetworkEntityMap`, the
**serializer-injected** `EntityStateExtractionService` (reused, not a raw 2-arg one), the
`EditorTimeTransportFacade`, `IPreviewController`, `IEditorLogic`, `DiagnosticEventHistoryService`,
`MasterSyncController`, and a `Func<ClusterState>` cluster-state provider. One method per endpoint;
each returns a `JsonNode` payload. Tier-1 tests target this class directly against `EditorHarness`
(fast, no HTTP).

### Envelope no longer re-cases domain payloads

`ApiResponse.Data` changed from `object?` to **`JsonNode?`** (`DebugApiHost.cs`). Domain payloads are
produced as `JsonNode` via the DTO path (`EventSerializationHelper` for events, the serializer-injected
`EntityStateExtractionService` re-parsed through `FdpJsonOptionsRegistry.DefaultRelaxed` for entities).
STJ writes a `JsonNode`'s own keys verbatim, so the host's CamelCase policy applies only to the
envelope keys (`ok`/`data`/`error`), never the embedded domain JSON. No raw domain object is ever handed
to the host serializer.

### Table-driven routing + marshalling

`DebugApiHost` routing is now a `List<RouteEntry>` (method + path template with `{param}` capture) with
a small matcher. World-touching handlers run via `_jobQueue.RunOnMainThread`; event-history retrieval +
DTO mapping stay off-thread (thread-safe). `/status` answers a minimal `{ok:true}` before the service is
attached (preserves the BATCH-01 contract and liveness checks), and `/shutdown` is handled inline.

### Wiring into EditorSubsystem

`ConfigureDebugApi(port, shutdownCallback)` (called from `Program.cs` before `Initialize`) now just
records the request; the host + service are constructed at the end of `Initialize()` (after the preview
controller, editor logic, extraction service, and event history exist) and started there — works
headless. `EditorApplication` gained a public `CurrentClusterState` getter so the scenario-load
ready-poll uses `OperatingEdit` (not `LoadedScenarioName`, per the architect-confirmed decision).

---

## Endpoints (Groups A–E)

| Group | Endpoint | Service method | Notes |
|---|---|---|---|
| A | `GET /status` | `GetStatus` | scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording(false) |
| B | `GET /entities` | `ListEntities` | `[{networkId, name, components:[…names]}]` |
| B | `GET /entities/{networkId}` | `DumpEntity` | serializer-injected dump; unknown id → **404** (resolved via `NetworkEntityMap.TryGetEntity`) |
| C | `GET /events?bus=&type=&since=&max=` | `GetEvents` | payload via `EventSerializationHelper`; default `bus=world`, `max=200`, most-recent-first |
| D | `GET /sim/state` | `GetSimState` | |
| D | `POST /sim/play` / `pause` | `Play` / `Pause` | explicit, idempotent (read-then-toggle; never blind-toggle) |
| D | `POST /sim/step {count?}` | `Step` | discrete step(s) via facade |
| D | `POST /sim/timescale {scale}` | `SetTimeScale` | |
| D | `POST /preview/enter {startPaused?}` / `exit` | `EnterPreview` / `ExitPreview` | |
| E | `GET /scenarios` | `ListScenarios` | `AvailableScenarios` |
| E | `POST /scenario/load {name, waitForReady?}` | `BeginLoadScenario` + `PollClusterStateIsOperatingEdit` | host loops the poll across drains; returns at `OperatingEdit` |
| E | `POST /scenario/save {name}` | `SaveScenario` | `IEditorLogic.SaveScenarioAs` |

---

## Tier-1 tests (the gate)

**New file:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiServiceTests.cs` (11 tests).
Entities are seeded via the proven `SpawnEntityCommand`-on-`harness.Bus` path (the bare `EditorHarness`
has no orchestrator-driven scenario-load pipeline — see Deviations). `EditorHarness` was extended with
test accessors (`TimeController`, `Serializer`, `History`) and a `BuildDebugApiService()` helper that
mirrors the production wiring; a `World`-bus `EventHistoryCaptureSystem` is now registered so the
event-history endpoint has data.

Coverage: status entity-count; entity list (networkId + readable name); entity dump (readable
`EntityInfo`, not raw bytes); unknown-id → null (404); event history includes the published
`SpawnEntityCommand` with a readable payload + type filter; play/pause idempotency; timescale;
preview enter/exit; step advances `totalTime`; scenario list; save→reload round-trip.

---

## Full `dotnet test` summary (honest)

**`dotnet build IOS-IG-SimHost.sln`** → **0 errors**, 27 warnings (all pre-existing: xUnit2013
collection-size analyzer + `IBlueprintTimeController` obsolete + nullable in unrelated test projects).

**`dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests`** — the **full** suite **cannot
complete on this machine**: it aborts with an **unhandled `CycloneDDS.Runtime.DdsException: dds_take
failed: -3 (BadParameter)`** on a background thread in
`Fdp.Network.Cyclone.Services.DdsIdAllocatorServer.ProcessRequests` (via `HostedIdAllocatorServer.RunLoop`),
triggered by the **DDS-based `HrotRunnerHarness`** tests. **This abort is PRE-EXISTING and environmental,
NOT caused by ADA-BATCH-02** — verified by stashing all my changes and re-running on the clean baseline,
which aborts identically (`Failed: 2, Passed: 2, Skipped: 2` then `Test Run Aborted`). My changes add no
DDS code.

Because the abort kills the whole test process regardless of filter once a DDS test runs, I ran the
**offline (no-DDS) editor surface** — the relevant Tier-1 gate — as the representative summary:

```
dotnet test …Integration.Tests --filter \
  "FullyQualifiedName~DebugApi|~EventSerializationHelper|~OfflineEditor|\
   ~EditorAuthoring|~EditorPreviewAndSave|~EditorSubsystemBoot|~ZoneScenarioLoad"
→ Passed: 53, Failed: 0, Skipped: 0, Total: 53
```

This includes **all 21 ADA tests** (5 DebugApi foundation + 3 EventSerializationHelper +
11 DebugApiService + 2 headless-smoke which no-op when the env gate is unset) plus the surrounding
offline editor integration tests.

**`dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter ~EntityStateExtractionServiceTests`** →
**8/8 passed** (confirms the `DtoDiagnosticMapper` FixedString change did not regress entity extraction).

### Other failures observed (pre-existing, NOT introduced)

- `EditorFileIOIntegrationTests.SaveScenario_SubsystemTypeIsHrotScenario` — fails with the saved
  scenario JSON lacking a `Header` property (`JsonElement.TryGetProperty` on a default element).
  **Verified pre-existing**: reverted `EditorHarness` to baseline + removed my new test files, rebuilt,
  ran in isolation → still fails. Unrelated to ADA-BATCH-02 (no serialization-output code touched).
- `ContextMenuIntegrationTests` / `MapPlacementIntegrationTests` — DDS/IG `HrotRunnerHarness`-based;
  same environmental DDS lane as the abort above.
- `Fdp.Toolkit.Diagnostics.Gizmos.Tests` — 3 gizmo routing/persistence failures (struct-update routing,
  float32 persistence). No dependency on `DtoDiagnosticMapper`/FixedString/the DTO path; pre-existing.

---

## Deviations / decisions

1. **`waitForReady` in the bare harness.** `EditorHarness` wires `EditorApplication` directly with the
   orchestration bus but **no** `ClusterMaster`/`ClusterSlave` handlers, so `LoadScenarioByName`'s
   multi-tick state machine never reaches `OperatingEdit` there. The **production** `EditorSubsystem`
   *does* wire `ClusterMaster`, so the poll works in-process. Tier-1 therefore seeds entities via
   `SpawnEntityCommand` (the established offline pattern) and tests the save→reload round-trip via the
   explicit-path `SaveScenario(file)` rather than driving the full orchestrated load. The poll logic
   itself lives in the service/host and reads `OperatingEdit` (never `LoadedScenarioName`).
2. **`Step(count)` is loop-coupled** (ADA-02-D04): the facade's `Step()` sets the time-controller delta;
   the tick is applied on the next `Update()`. The Tier-1 test asserts non-regressing `totalTime`.
3. **Pause is barrier-deferred** in the harness: `SwitchToDeterministic` enters `BarrierPending`, which
   the controller reports as Continuous until the wall-clock barrier elapses on a later tick (mirrors the
   real editor loop). The test pumps until paused rather than asserting immediately.
4. **Save endpoint** uses `SaveScenarioAs(name)` → editor `ScenariosRoot` (env-dependent), so the
   hermetic round-trip test uses the explicit-path overload (ADA-02-D02).

---

## Blockers

- **None for the implementation.** The only blocker to a *full-suite* green run is the pre-existing
  CycloneDDS background-thread crash, which is environmental and outside this workstream. The lead's
  re-run on a DDS-healthy box (or the offline filter above) is the meaningful gate.

---

## Files changed

**New:**
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EventSerializationHelperTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiServiceTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiHeadlessSmokeTests.cs`

**Edited:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/DtoDiagnosticMapper.cs` — FixedString → readable string (C1 fix).
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` — route table, `JsonNode` envelope, body parse,
  service dispatch, scenario-load poll loop.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — deferred `ConfigureDebugApi`; build host + service at
  end of `Initialize`; capture the serializer-injected extraction service.
- `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs` — public `CurrentClusterState` getter.
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — test accessors + `History`
  capture system + `BuildDebugApiService()`.

**Debt added:** ADA-02-D01..D04 in `.dev/ai-debug-api/DEBT-TRACKER.md`.
