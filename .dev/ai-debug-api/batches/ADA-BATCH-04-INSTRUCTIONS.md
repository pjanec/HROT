# ADA-BATCH-04: Entity Commands + Discovery + Spawn

**Batch Number:** ADA-BATCH-04
**Tasks:** ADA-P1-T05 (commands + `/commands` discovery + spawn), ADA-P1-T06 (`/components` discovery)
**Phase:** Phase 1 — Slice 1 Surface (part 2 of 2)
**Estimated Effort:** ~14 hours
**Executor:** sonnet (generic event (de)serialization + wait-gating are subtle)
**Priority:** HIGH
**Dependencies:** BATCH-02 (DebugApiService, host patterns), BATCH-01 (JsonShapeDescriber, EventSerializationHelper)

---

## Onboarding & Workflow

Adds the command surface (the AI mutates the sim by publishing FDP events), command/component discovery,
and a spawn convenience. Follow the patterns BATCH-02 established.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-02-REVIEW.md` (patterns + path-naming note) and `ADA-BATCH-03-REVIEW.md`.
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group F (entity commands + discovery), Group B (`/components`),
   *Wait-Gating Semantics (command results)*.
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P1-T05, ADA-P1-T06.

> No codebase-memory MCP. No git commit. Report honestly — the lead re-runs `dotnet test` + the headless
> reproduce and reads the diff (it has caught false "done" claims three times now).

### Existing code to study / reuse
- `DebugApiService` / `DebugApiHost` (BATCH-02) — extend, don't fork. JsonNode payloads; `RunOnMainThread`.
- `JsonShapeDescriber.Describe(Type)` (BATCH-01) — for `/commands` + `/components` schemas.
- `EventType.GetAllRegistered()`, `ComponentTypeRegistry.GetAllTypes()` — enumeration.
- `FdpEventBus.Publish<T>` / `PublishManaged<T>` — publish; `EditorTimeTransportFacade` (`IsPaused`,`InPreview`)
  for wait-gating.
- `SpawnEntityCommand` (`Fdp.Toolkit.NetworkSpawning.Events`) — the spawn path; `EditorHarness` test
  `SpawnTestEntity` shows the working publish.
- `MissionControlIntent` + `MissionControlAckEvent` (keyed by `RequestId`) — the example correlated-ack command.

---

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Group F, B)
- `GET /commands` — enumerate publishable FDP event types (`EventType.GetAllRegistered()`) + field schema.
- `GET /components` — `ComponentTypeRegistry.GetAllTypes()` + field schema.
- `POST /entities/command {eventType, payload, wait?}` — resolve `eventType` to the registered CLR event
  type, deserialize `payload` (JSON → that type, managed vs unmanaged), publish on `_world.Bus`.
  **Wait-gating (API owns it):** if `wait==true` AND time is advancing (`InPreview && !IsPaused`), await the
  correlated ack (e.g. `MissionControlAckEvent` by `RequestId`) with a timeout, return `{awaited:true, …}`;
  otherwise publish and return `{awaited:false, reason:"sim not running"}`. Always timeout-bounded.
- `POST /entities/spawn {tkbType, transform?, components?, attributesJson?}` — build + publish a
  `SpawnEntityCommand`; return the spawned `networkId` (and `awaited` per the wait rule, best-effort).

## Verification (ship tests; loop to green)
- **Tier-1 (EditorHarness):**
  - `GET /commands` lists `SpawnEntityCommand`, `MissionControlIntent`, … each with a field schema.
  - `GET /components` lists registered component types with field shapes (Vector/enum mapped correctly).
  - `spawn` → after pumping, `ListEntities` shows the new entity / `entityCount` increases.
  - generic `command` (e.g. a `SpawnEntityCommand` via the generic endpoint) → the event appears in
    `GetEvents("world")`.
  - **wait-gating (deterministic):** with the harness paused (default), a `wait:true` command returns
    `awaited:false, reason:"sim not running"`. (The `awaited:true` correlated-ack happy path may be
    best-effort if the ack-emitting system isn't readily drivable in the bare harness — if so, log debt,
    don't fake it.)
- **Tier-2 (headless smoke, extend):** `GET /commands` returns non-empty; `POST /entities/spawn` then
  `GET /status` shows `entityCount` increased.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- Payloads via JsonNode / DTO path; never the host's CamelCase serializer for domain data.
- All world-touching handlers via `RunOnMainThread`. Wait-gating logic in the API, never assumed by callers.
- Generic command deserialization must handle unregistered/unknown `eventType` → `400` (not a crash).
- Frozen `TestAssets` fixtures; never the production scan path; never regenerate snapshots.
- Path-naming: align endpoint paths with the DESIGN API table and keep them consistent
  (`/entities/command`, `/entities/spawn`, `/commands`, `/components`).

## Deliverables
- Code + tests green; extended smoke.
- `.dev/ai-debug-api/reports/ADA-BATCH-04-REPORT.md` (DEV-GUIDE format): built, decisions/deviations,
  FULL `dotnet test` summary, the headless smoke output (commands non-empty + spawn raises entityCount),
  blockers, debt → DEBT-TRACKER.
