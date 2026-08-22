# ADA-BATCH-04 Report

**Batch:** ADA-BATCH-04 — Entity Commands + Discovery + Spawn
**Tasks:** ADA-P1-T05 (commands + `/commands` discovery + spawn), ADA-P1-T06 (`/components` discovery)
**Date:** 2026-06-14
**Executor:** sonnet
**Branch:** `feat/ai-debug-api`

---

## Built

`dotnet build IOS-IG-SimHost.sln` → **0 errors, 27 warnings (all pre-existing)**.

---

## Implementation Summary

### New code

**`DebugApiService.cs`** — four new methods added to the existing service:

- `ListCommands()` — enumerates `EventType.GetAllRegistered()` (unmanaged events with `[EventId]`), emits `[{ name, fields:[{name,type}] }]` via `JsonShapeDescriber.Describe(t)`.
- `ListComponents()` — enumerates `ComponentTypeRegistry.GetAllTypes()`, same schema output.
- `SendCommand(eventTypeName, payload, wait)` — resolves type by name from `EventType.GetAllRegistered()`; deserializes payload via `System.Text.Json` to the CLR type; publishes via `PublishEventObject` (reflection dispatch to `Publish<T>` for unmanaged types or `PublishManaged<T>` for managed); implements wait-gating: if `wait==true && !(InPreview && !IsPaused)` → immediate `{awaited:false, reason:"sim not running"}`. Returns `(JsonNode? result, string? error)` tuple so the host maps `error → 400`.
- `SpawnEntity(tkbType, transform?, components?, attributesJson?)` — builds a `SpawnEntityCommand` with `NetworkId=0` (auto-allocate), publishes via `Bus.PublishManaged`, returns `{spawned, tkbType, awaited, reason}`.
- Private `PublishEventObject(Type, object?)` helper — reflection-based dispatch to generic `Publish<T>` / `PublishManaged<T>`.

**`DebugApiHost.cs`** — four new routes added to `BuildRoutes()`:

- `GET /commands` → `RunMain(s => s.ListCommands())`
- `GET /components` → `RunMain(s => s.ListComponents())`
- `POST /entities/command {eventType, payload, wait?}` → marshalled `SendCommand()`; error → 400
- `POST /entities/spawn {tkbType, transform?, components?, attributesJson?}` → marshalled `SpawnEntity()`

**`DebugApiBatch04Tests.cs`** — 11 new Tier-1 tests (EditorHarness):

- `ListCommands_ReturnsNonEmpty_WithFieldSchemas`
- `ListCommands_IncludesMissionControlAckEvent_WithFieldSchema`
- `ListCommands_IncludesCenterOnEntityCommand`
- `ListComponents_ReturnsNonEmpty_WithFieldSchemas`
- `ListComponents_IncludesEntityInfo`
- `SendCommand_UnknownEventType_Returns400Error`
- `SendCommand_ValidUnmanagedCommand_AppearsInEventHistory`
- `SendCommand_WaitTrue_SimPaused_ReturnsAwaitedFalse_SimNotRunning`
- `SpawnEntity_ValidTkbType_IncreasesEntityCount`
- `SpawnEntity_Paused_ReturnsAwaitedFalse`
- `ListEntities_AfterSpawn_ShowsNewEntity`

**`DebugApiHeadlessSmokeTests.cs`** — extended with BATCH-04 checks:

- `GET /commands` non-empty
- `GET /components` responds OK
- `POST /scenario/load {name:"test-move", waitForReady:true}` then `entityCount > 0`
- `POST /entities/spawn {tkbType:1001}` then poll for entityCount increase

---

## Decisions / Deviations

### D1 — `/commands` covers unmanaged events only

`EventType.GetAllRegistered()` is constrained to `where T : unmanaged` (via `EventType<T>`). `SpawnEntityCommand` is a managed struct (contains `List<object>?`) and is not registered there. The `/commands` endpoint correctly enumerates only unmanaged struct events with `[EventId]` attributes. Managed events (SpawnEntityCommand, MissionControlIntent, etc.) are NOT enumerated by `/commands` but CAN be targeted via `/entities/spawn` (SpawnEntityCommand) and `/entities/command` only if their type has `[EventId]`.

The design spec says "EventType.GetAllRegistered()" which aligns with this behavior. The generic command endpoint's coverage is therefore limited to unmanaged events. A note is added to DEBT-TRACKER.

### D2 — Wait-gating ack-wait deferred

The `awaited:true` correlated-ack path (await `MissionControlAckEvent` by `RequestId`) requires cross-tick continuation. The synchronous job-queue pattern doesn't support multi-tick blocking without a redesign. The endpoint correctly returns `awaited:false, reason:"ack-wait not yet supported; event published"` when time is advancing. This is logged as debt ADA-04-D01.

### D3 — Headless smoke uses TkbType=1001 (CivilianPedestrian)

The real editor registers UrbanCombat types 1001–2003 (not TkbType=1 which is the test harness stub). The smoke updated to use TkbType=1001.

---

## Dotnet Test Summary (full)

```
dotnet test --filter "FullyQualifiedName~DebugApi"

Passed!  - Failed: 0, Passed: 32, Skipped: 0, Total: 32, Duration: ~5s
```

Breakdown:
- DebugApiFoundationTests: 9 passed (unchanged from BATCH-01)
- DebugApiServiceTests: 11 passed (unchanged from BATCH-02)
- DebugApiScenarioLoadTests: 1 passed (unchanged from BATCH-03)
- **DebugApiBatch04Tests: 11 passed (new)**
- DebugApiHeadlessSmokeTests: 1 passed (opted-out without ADA_RUN_HEADLESS_SMOKE=1)

---

## Headless Smoke Output

Run with `ADA_RUN_HEADLESS_SMOKE=1`:

```
ADA_RUN_HEADLESS_SMOKE=1 dotnet test --filter "FullyQualifiedName~HeadlessSmoke"

Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: ~4.7s
```

Smoke verified:
- `GET /status` → 200 OK (editor up)
- `GET /entities` → 200 OK
- `GET /sim/state` → 200 OK
- `GET /commands` → 200 OK, data array non-empty ✅
- `GET /components` → 200 OK ✅
- `POST /scenario/load {name:"test-move", waitForReady:true}` → 200, entityCount=1 after load ✅
- `POST /entities/spawn {tkbType:1001}` → 200, entityCount increased ✅
- `POST /shutdown` → 200, process exited 0

---

## Blockers

None.

---

## Debt

| ID | Description | Priority |
|----|-------------|----------|
| ADA-04-D01 | `/entities/command` wait-gating: `awaited:true` correlated-ack path (poll bus across ticks for `MissionControlAckEvent` by `RequestId`) not implemented. Returns `awaited:false, reason:"ack-wait not yet supported; event published"` when sim is running. Requires multi-tick continuation mechanism. | P3 |
| ADA-04-D02 | `/commands` enumerates only unmanaged `[EventId]` events. Managed events (SpawnEntityCommand, MissionControlIntent) not discoverable via `/commands`. Managed events can only be sent via dedicated endpoints (`/entities/spawn`). A managed event registry or reflection scan would be needed to cover them generically. | P3 |
