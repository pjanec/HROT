# Task Detail — AI Debug & Test API (Editor) + MCP Server

**Reference:** [DESIGN.md](./DESIGN.md) for architecture, the API table (groups A–N), the Reuse Map, and the
New Work list. Each task below cites the design section it implements.

**Global constraints (apply to every task):**

- The whole feature lives in a self-contained component owned by `EditorSubsystem`, off unless the
  `--debug-api` flag / `DebugApi.Enabled` config is set (see [Component Architecture](./DESIGN.md#component-architecture)).
- Every world-touching operation runs on the main thread via the job queue (ADA-P0-T02). Never touch
  `_world`, `NetworkEntityMap`, ImGui, Raylib, or `MapCamera`/`CullingState` from a background thread
  (see [Threading & Marshalling Model](./DESIGN.md#threading--marshalling-model)).
- Readable serialization always goes through the custom DTO machinery (ADA-P0-T03), never raw STJ on
  domain objects.
- **Tests** must use frozen `TestAssets` fixtures + direct deserialize — never the production scenario
  scan path or scratch `.bp.json`. Do not regenerate snapshots to make tests pass.
- **Zoo executor caveat:** review the produced diff directly; do not trust the batch report. Hard-review
  every batch.

---

## Phase 0 — Web Host Foundation

---

### ADA-P0-T01 — DebugApiHost skeleton

**Design Reference:** [Component Architecture](./DESIGN.md#component-architecture), [Architectural Principles](./DESIGN.md#architectural-principles). **Executor:** sonnet.

**Scope:**
- New project/folder `Hrot.Editor.DebugApi` (or a folder under `Hrot.Editor`), namespace `Hrot.Editor.DebugApi`.
- `DebugApiHost` class: owns a `System.Net.HttpListener` bound to `localhost:<port>`, a route table
  (method + path template → handler), and JSON request/response (de)serialization with a standard
  envelope `{ ok, data?, error?, awaited? }`.
- Config: add `--debug-api` (bool) and `--debug-api-port` (int, default e.g. 8080) to
  `HrotRunnerConfiguration`; surface as `DebugApi.Enabled` / `DebugApi.Port`.
- `EditorSubsystem.Initialize` constructs `DebugApiHost` **only when enabled**; `Shutdown` disposes it.
- `POST /shutdown` handler: signals the main loop to exit cleanly (set a flag the runner loop honors to
  break and run `orchestrator.Shutdown()`), returns `{ ok:true }` before exit.
- `GET /status` returns a minimal `{ ok:true }` placeholder (full payload in ADA-P1-T01).

**NOT included:** the job queue (ADA-P0-T02), any capability endpoints.

**Constraints:**
- No ASP.NET Core / generic host / DI container. `HttpListener` only.
- Listener bound to loopback by default.
- Host must be a no-op (never constructed) when the flag is absent.

**Success Conditions:**
1. With `--debug-api --debug-api-port 8099`, `GET http://localhost:8099/status` returns `200` `{ ok:true }`.
2. Without the flag, no listener is opened (port closed) and editor startup is unchanged.
3. `POST /shutdown` causes the runner process to exit cleanly (subsystems disposed, exit code 0).
4. Editor still launches and runs headless (`--headless`) with the API enabled.

---

### ADA-P0-T02 — Main-thread job queue

**Design Reference:** [Threading & Marshalling Model](./DESIGN.md#threading--marshalling-model). **Executor:** sonnet.

**Scope:**
- `MainThreadJobQueue`: thread-safe enqueue of jobs `Func<object?>` (or typed), each paired with a
  `TaskCompletionSource`. HTTP handler threads enqueue + await the TCS.
- Drain in `EditorSubsystem.Update()` **after the kernel tick, before draw** — run each job on the main
  thread, fulfil/fault its TCS.
- Helper `Task<T> RunOnMainThread<T>(Func<T>)` used by handlers.
- Mirror the existing `EnqueueConsoleAction`/`DrainConsoleActions` pattern (`SubsystemOrchestrator`).

**NOT included:** wait-gating semantics for command acks (ADA-P1-T05).

**Constraints:**
- A job that throws faults its TCS (handler returns `500` with the message), never crashes the loop.
- Off-thread-safe reads (event history, logs, static catalogs) must NOT be forced through the queue.
- No ImGui/Raylib access inside jobs (jobs run during Update, outside the ImGui frame).

**Success Conditions:**
1. A handler enqueuing a job that reads `_world.MaxEntityIndex` returns the value, proving main-thread execution.
2. Concurrent requests (e.g. 20 parallel) all complete without ECS corruption or deadlock.
3. A job that throws yields a `500` with the exception message; the sim keeps ticking.
4. Drain happens post-tick (a job reading entity state sees the just-completed tick's values).

---

### ADA-P0-T03 — EventSerializationHelper

**Design Reference:** [New Work #4](./DESIGN.md#new-work-everything-else-is-wiring), [Group C](./DESIGN.md#group-c--event-history). **Executor:** sonnet.

**Scope:**
- Promote `DtoDiagnosticMapper` from `internal` → `public` (Fdp.Toolkit.Diagnostics).
- New `public static class EventSerializationHelper` in `Fdp.Toolkit.Diagnostics`:
  `string SerializeToJson(object? value, IGuidResolver? resolver = null)` →
  `DtoDiagnosticMapper.MapObject(value, value?.GetType() ?? typeof(object), visited)` →
  `JsonSerializer.Serialize(mapped, FdpJsonOptionsRegistry.Indented)`.
- Resolve `Entity`-handle fields to networkId via a `DiagnosticGuidResolver`/`NetworkEntityMap` when a
  resolver is supplied (entity-ref resolution is main-thread-only).

**NOT included:** the event-history endpoint (ADA-P1-T02) — this is the reusable helper only.

**Constraints:**
- Must handle fixed-buffers, `[InlineArray]`, and boxed `List<object>` component lists (e.g.
  `SpawnEntityCommand.InitialComponents`) — verify with those exact types.
- Pure DTO mapping (no resolver) must be callable off-thread; resolver-based calls assume main thread.
- Do NOT reuse `EventBrowserPanel.BuildCopyJson` (UI assembly).

**Success Conditions:**
1. `SerializeToJson(new SpawnEntityCommand{ InitialComponents = [EntityInfo{Name="x"}] })` produces JSON
   where the boxed `EntityInfo.Name` is present and readable (not opaque).
2. A struct event containing a fixed-buffer/blackboard-like field serializes to a readable DTO, not raw bytes.
3. With a resolver, an event field of type `Entity` serializes as its networkId (or null if unmapped).
4. `Fdp.Toolkit.Diagnostics` exposes `DtoDiagnosticMapper` publicly; existing internal callers still compile.

---

### ADA-P0-T04 — CLR→JSON-schema helper

**Design Reference:** [New Work #2](./DESIGN.md#new-work-everything-else-is-wiring), [Group F](./DESIGN.md#group-f--entity-commands-generic--discovery). **Executor:** zoo.

**Scope:**
- `public static class JsonShapeDescriber`: given a CLR `Type`, walk `FdpAutoSerializer.GetSortedMembers`
  and emit `[{ name, type }]` (mapping CLR primitives/string/enum/Vector/Quaternion/List<T>/nested to
  JSON type names).
- Used by discovery endpoints (`/commands`, `/components`).

**NOT included:** the endpoints themselves (ADA-P1-T05/T06).

**Constraints:** reflection-only, no instance needed; handle nested/array/enum types gracefully.

**Success Conditions:**
1. `Describe(typeof(SpawnEntityCommand))` lists `NetworkId:long`, `TkbType:long`, etc. with correct JSON types.
2. An enum field is described as `string` (enum-name) per `StrictStringEnumConverter` convention.
3. Unknown/complex nested types are described as nested objects, not crashed.

---

## Phase 1 — Slice 1 Surface

---

### ADA-P1-T01 — Status + entity query/dump

**Design Reference:** [Group A/B](./DESIGN.md#group-a--lifecycle--status). **Executor:** zoo.

**Scope:**
- `GET /status` → `{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }`
  (read from `_editorLogic`, `EditorTimeTransportFacade`, `_world`).
- `GET /entities` → `[{ networkId, name, components:[…type names] }]` from `EntityStateExtractionService`.
- `GET /entities/{networkId}` → full component dump (serializer-injected `EntityStateExtractionService`).
- All marshalled to main thread.

**NOT included:** filters (ADA-P7-T01).

**Constraints:** reuse the editor's existing serializer-injected `EntityStateExtractionService` instance
([EditorSubsystem.cs:783]); do not new up a raw (2-arg) one. Resolve `{networkId}` via `NetworkEntityMap.TryGetEntity`.

**Success Conditions:**
1. After loading a frozen test scenario with N entities, `GET /entities` returns N entries each with a `networkId`.
2. `GET /entities/{id}` returns a components dict with at least the standard components; a blackboard
   component renders as a readable DTO (proving the serializer path), not raw bytes.
3. Unknown `{networkId}` → `404` with envelope error.

---

### ADA-P1-T02 — Event history

**Design Reference:** [Group C](./DESIGN.md#group-c--event-history). **Executor:** zoo.

**Scope:**
- `GET /events?bus=world|orchestration&type=&since=&max=` → list of
  `{ frame, provider, type, isManaged, summary, payload }`.
- History retrieval via `DiagnosticEventHistoryService.GetHistory(providerFilter)` (off-thread).
- `payload` serialized via `EventSerializationHelper` (ADA-P0-T03).
- `bus` maps to provider filter (`World`/`Orchestration`); default `world`.

**Constraints:** retrieval + pure DTO mapping off-thread; if payload includes entity-ref resolution,
that path is marshalled (see Threading). Default `max` bounded (e.g. 200).

**Success Conditions:**
1. After ticking with a known event published, `GET /events?bus=world` includes it with a readable `payload`.
2. `?type=SpawnEntityCommand` filters to that type only; `?bus=orchestration` reads the orchestration bus.
3. An event with a complex payload serializes readably (not raw), confirming the helper is used.

---

### ADA-P1-T03 — Sim/preview/time control

**Design Reference:** [Group D](./DESIGN.md#group-d--sim--preview--time-control-via-editortimetransportfacade), [Run-Mode Model](./DESIGN.md#run-mode-model). **Executor:** zoo.

**Scope:**
- Consume the existing `EditorTimeTransportFacade` (the toolbar's facade).
- `GET /sim/state` → `{ isPaused, inPreview, totalTime, timeScale }`.
- `POST /sim/play`, `/sim/pause` (explicit wrappers over `TogglePlayPause` after reading state),
  `/sim/step {count?}` (`Step`), `/sim/timescale {scale}` (`SetTimeScale`).
- `POST /preview/enter {startPaused?}`, `/preview/exit`.

**Constraints:** all marshalled. `play`/`pause` must be idempotent (read state, toggle only if needed) —
never blind-toggle.

**Success Conditions:**
1. `POST /sim/step` with `count:3` from a paused preview advances exactly 3 ticks (`totalTime`/frame delta).
2. `play` then `GET /sim/state` shows `inPreview:true, isPaused:false`; `pause` flips `isPaused:true`.
3. `set_time_scale 2.0` reflected in `GET /sim/state.timeScale`.

---

### ADA-P1-T04 — Scenario load/list/save

**Design Reference:** [Group E](./DESIGN.md#group-e--scenario-load), [Appendix](./DESIGN.md#appendix-architect-confirmed-decisions-verified-against-code). **Executor:** zoo.

**Scope:**
- `GET /scenarios` → `EditorApplication.AvailableScenarios`.
- `POST /scenario/load {name, waitForReady?}` → `IEditorLogic.LoadScenarioByName`; when `waitForReady`,
  the job polls `_orchestrationBus.ReadManaged<ClusterStateUpdateEvent>()` across frames and returns only
  when `CurrentState == ClusterState.OperatingEdit`.
- `POST /scenario/save {name}` → `ScenarioFileService.SaveScenario`.

**Constraints:** **Do not** use `LoadedScenarioName` as the completion signal (set at frame 0). The
`waitForReady` poll must span multiple ticks via the job/queue mechanism with a timeout.

**Success Conditions:**
1. `GET /scenarios` lists the frozen test scenarios.
2. `POST /scenario/load {name, waitForReady:true}` returns `200` only after `OperatingEdit`; immediately
   after, `GET /entities` is non-empty (genesis complete).
3. `POST /scenario/save` writes a file that re-loads to an equivalent entity set.

---

### ADA-P1-T05 — Entity commands + discovery

**Design Reference:** [Group F](./DESIGN.md#group-f--entity-commands-generic--discovery), [Wait-Gating Semantics](./DESIGN.md#wait-gating-semantics-command-results). **Executor:** sonnet.

**Scope:**
- `GET /commands` → enumerate publishable FDP event types (`EventType.GetAllRegistered()`) + field schema
  (ADA-P0-T04).
- `POST /entities/command {eventType, payload, wait?}` → deserialize to the named event type, publish via
  `Publish<T>`/`PublishManaged<T>`. **Wait-gating:** if `wait` and time is advancing (`InPreview && !Paused`),
  await the correlated ack (e.g. `MissionControlAckEvent` by `RequestId`) with timeout; otherwise return
  `{ awaited:false, reason:"sim not running" }`.
- `POST /entities/spawn {tkbType, transform?, components?, attributesJson?}` → build + publish `SpawnEntityCommand`.

**NOT included:** focus (ADA-P9-T01).

**Constraints:** wait logic lives here (API owns it), always timeout-bounded; degrade to `awaited:false`.
Deserialization must map JSON → the registered event CLR type (managed vs struct). Marshalled.

**Success Conditions:**
1. `GET /commands` lists `SpawnEntityCommand`, `MissionControlIntent`, etc. with field schemas.
2. `POST /entities/spawn` with a valid `tkbType` results in a new entity (visible in `GET /entities` after a step).
3. With sim paused, a `wait:true` command returns immediately `awaited:false reason:"sim not running"`.
4. With sim running, a command that has a correlated ack returns `awaited:true` with the ack (or times out gracefully).

---

### ADA-P1-T06 — /components + /scenarios discovery

**Design Reference:** [Group B](./DESIGN.md#group-b--queries). **Executor:** zoo.

**Scope:**
- `GET /components` → `ComponentTypeRegistry.GetAllTypes()` + field schema (ADA-P0-T04).
- (`/scenarios` already in ADA-P1-T04; cross-link only.)

**Success Conditions:**
1. `GET /components` lists registered component types with field shapes.
2. A component with Vector/enum fields shows correct JSON types.

---

### ADA-P1-T07 — TKB entity-type catalog

**Design Reference:** [Group M](./DESIGN.md#group-m--entity-type--tkb-catalog-scenario-authoring). **Executor:** zoo.

**Scope:**
- Retain a reference to the editor's `tkbDb` (currently a local in `Initialize`) and pass to `DebugApiHost`.
- `GET /tkb/types?category=` → `TkbDatabase.GetAll()` (or `GetEntitiesByCategory`) →
  `[{ tkbType, name, categoryPath, disType }]`.
- `GET /tkb/types/{tkbType}` → `MandatoryComponents`, `ChildBlueprints`, `DisType`, descriptor bag
  (`GetAllDescriptors()`) serialized via `EventSerializationHelper`.

**Constraints:** read-only static catalog → off-thread-safe. Reuse the dynamic projection rather than the
hardcoded `TkbCatalogEntry[]`.

**Success Conditions:**
1. `GET /tkb/types` lists the registered templates (e.g. the Urban Combat set) with ids + names.
2. `GET /tkb/types/{id}` returns mandatory components + readable descriptor DTOs without spawning.
3. A `tkbType` from the catalog is accepted by `POST /entities/spawn` (round-trip with ADA-P1-T05).

---

### ADA-P1-T08 — World/coordinate info

**Design Reference:** [Group N](./DESIGN.md#group-n--world--coordinate-info-scenario-authoring), [New Work #6](./DESIGN.md#new-work-everything-else-is-wiring). **Executor:** zoo.

**Scope:**
- Add an `Origin` (lat/lon/alt) getter to `IGeographicTransform`/`WGS84Transform` (or capture the origin
  the editor passes at `CreateGeoTransform`).
- `GET /world/info` → `{ geo:{origin}, spatialGrid:{cellSize,originX,originY,width,height,extent}, terrain:null, navmesh:null }`
  (grid from `SpatialHashGrid` via `CognitiveSpatialModule`).
- `POST /world/geo-to-local {lat,lon,alt,headingDeg?}` → `{x,y,z, rotation?}` (`WGS84Transform.ToCartesian`
  + `SimTransformBridgeSystem.HeadingDegToRotation`).
- `POST /world/local-to-geo {x,y,z,rotation?}` → `{lat,lon,alt, headingDeg?}` (`ToGeodetic` +
  `RotationToHeadingDeg`).

**Constraints:** conversions are stateless → off-thread-safe. Report `terrain`/`navmesh` as `null` in
editor (do not fabricate). Note the known `RotationToHeadingDeg` degenerate-pitch bug (out of scope to fix).

**Success Conditions:**
1. `GET /world/info` returns the Berlin origin and the 1000×1000 grid extent.
2. `geo-to-local` of the origin lat/lon returns ≈(0,0,0); round-trip `geo→local→geo` is within tolerance.
3. `headingDeg:90` → a rotation whose `RotationToHeadingDeg` ≈ 90 (East); North=0.

---

## Phase 2 — Run-Until-Condition

---

### ADA-P2-T01 — Breakpoints

**Design Reference:** [Group G](./DESIGN.md#group-g--run-until-condition-breakpoints). **Executor:** sonnet.

**Scope:**
- `POST /breakpoints {condition, filterNetworkId?, occurrenceThreshold?, name?}` →
  `IDataBreakpointManager.AddBreakpoint(...)` → `{ breakpointId }`.
- `GET /breakpoints` → `AllBreakpoints`; `DELETE /breakpoints/{id}` → `Remove`.
- `GET /breakpoints/hits` → `{ isPaused, pausedTick, lastHit:{ id, networkId } }` (subscribe to
  `OnBreakpointHit`/`OnPauseStateChanged`, store last).
- `condition` is a `SearchPredicateDto` deserialized from JSON (polymorphic).

**Constraints:** polymorphic JSON (de)serialization of the `SearchPredicateDto` hierarchy (discriminator
field). Reuse the editor's wired `_bpManager`. Hit observation via events captured on the main thread.

**Success Conditions:**
1. POST a `PropertyMatchDto` (e.g. a component field < N), then `play`; `GET /breakpoints/hits` shows
   `isPaused:true` with the triggering `networkId` once the condition is met.
2. A `TransientEventPredicateDto` breakpoint pauses on the event firing.
3. `DELETE` removes it; subsequent runs do not pause.
4. A `CompoundPredicateDto` (AND) round-trips through JSON and compiles.

---

## Phase 3 — Checkpoint / Restore + Diff

---

### ADA-P3-T01 — Checkpoint/restore

**Design Reference:** [Group H](./DESIGN.md#group-h--checkpoint--restore--diff-preview-run-only). **Executor:** sonnet.

**Scope:**
- `POST /checkpoint` → `IPreviewController.EnterPreviewMode(startPaused:true)` (single-slot RAM snapshot).
- `POST /checkpoint/restore` → `IPreviewController.ExitPreviewMode()`.
- Guards: reject when a **live run** is active (mutually exclusive); reflect state in `/status`.

**Constraints:** use the `IPreviewController` facade only — never `PreviewClusterOpHandler` directly.
Single slot (one checkpoint at a time). Keyed multi-checkpoints are explicitly out of scope.

**Success Conditions:**
1. `checkpoint` → mutate an entity (move it) → `restore` → the entity returns to its checkpointed state.
2. `checkpoint` while a live run is active returns `409`/error.
3. `/status.inPreview` reflects checkpoint state.

---

### ADA-P3-T02 — State diff

**Design Reference:** [Group H](./DESIGN.md#group-h--checkpoint--restore--diff-preview-run-only). **Executor:** sonnet.

**Scope:**
- `POST /diff {entities?}` → serialize entity state (via the serializer path) before and after a caller
  workload, run `ComponentDiffService.ComputeTreeDiff(before, after, epsilon)`, return the `DiffNode` tree.
- Support diffing against the most recent checkpoint snapshot when present.

**Constraints:** diff does not require a checkpoint (can diff two serialized snapshots). Marshalled.

**Success Conditions:**
1. Capture state, move an entity, diff → the result tree shows the changed position component only.
2. An unchanged entity yields no diff nodes (within epsilon).
3. Entity birth/death between snapshots is represented in the tree.

---

## Phase 4 — Recording + Replay

---

### ADA-P4-T01 — Recording

**Design Reference:** [Group I](./DESIGN.md#group-i--recording--replay), [Run-Mode Model](./DESIGN.md#run-mode-model). **Executor:** sonnet.

**Scope:**
- `POST /recording/start {mode:preview|live}`:
  - preview: `EnterPreviewMode()` → `EcsRecordReplayController.PrepareRecordingAsync(exerciseId, dir)`.
  - live: publish `TransitionStateIntent{OperatingLive}` (records automatically via `ReferenceLiveLoadHandler`).
- `POST /recording/stop` → `FinalizeRecordingAsync()`; **for preview, finalize BEFORE the exit rewind**,
  then `ExitPreviewMode()`. Returns `{ fdpPath }`.
- ExerciseId/dir from `ClusterStateUpdateEvent.ExerciseId` + `OrchestrationConstants.DefaultStagingDirectory`.

**Constraints:** enforce the ordering `EnterPreviewMode → PrepareRecordingAsync → … → FinalizeRecordingAsync
→ ExitPreviewMode`. Recording during preview must NOT rewind while recording. Mutually exclusive with
checkpoint operations.

**Success Conditions:**
1. Preview recording produces a `.fdp` on disk; after stop, the world is rewound (revertible).
2. The first recorded frame is a keyframe (file loads standalone — verified in ADA-P4-T02).
3. Live recording produces a `.fdp` and a `RecordingLedgerEntry` (ledgered).
4. Requesting a checkpoint during a live recording is rejected.

---

### ADA-P4-T02 — Isolated replay

**Design Reference:** [Group I](./DESIGN.md#group-i--recording--replay) (replay isolation note). **Executor:** sonnet.

**Scope:**
- `POST /replay/load {fdpPath}` → stand up an **isolated** `ReplayBrowserContext` (its own `SandboxRepo`/`SandboxBus`).
- `POST /replay/seek {frame}` / `POST /replay/step {dir}` → `PlaybackController` over the sandbox.
- In replay mode, point the query services (entity dump, event history) at the **sandbox** repo/history.

**Constraints:** seeks must **never** touch `_world` (would desync `MasterSyncController`). The replay
context is fully disconnected from the live kernel tick.

**Success Conditions:**
1. Load a `.fdp` produced by ADA-P4-T01 → `GET /entities` (replay-scoped) returns the recorded frame's entities.
2. `seek`/`step` change the replay-scoped entity state; the live `_world` is unaffected (verify a live
   entity unchanged during replay seeking).
3. Loading a preview-originated `.fdp` directly by path works (no ledger dependency).

---

## Phase 5 — Logs

---

### ADA-P5-T01 — Logs query

**Design Reference:** [Group J](./DESIGN.md#group-j--logs). **Executor:** zoo.

**Scope:**
- `GET /logs?level=&logger=&since=&max=` → filter over `NLogMessageLogTarget.SharedInstance.GetMessages()`
  (+ `AiBehaviorLogTarget`); return `[{ timestamp, level, logger, message }]`.

**Constraints:** off-thread (sinks are lock-guarded). Filtering done in the endpoint (sinks have no query API).

**Success Conditions:**
1. After emitting a known log line, `GET /logs?level=Info` includes it.
2. `?logger=` and `?since=` filters narrow the result correctly; `?max=` bounds the count.

---

## Phase 6 — AI Behavior Traces

---

### ADA-P6-T01 — Trace arming seam

**Design Reference:** [New Work #1](./DESIGN.md#new-work-everything-else-is-wiring), [Group K](./DESIGN.md#group-k--ai-behavior-traces). **Executor:** sonnet.

**Scope:**
- Implement/confirm the editor's `AiTracerCoordinator` override so `BeginObservingAsset`/`EndObservingAsset`
  actually set `DebugState.Flags` and `TraceBufferLifecycleSystem` allocates the
  `BTreeTraceWorkingMemory1024`/`HsmTraceWorkingMemory1024` components on matching entities.
- Register a blueprint `DebugMap` for field decoding; handle blueprints via `DebugProbe.Sink` (per asset type).
- `POST /trace/observe {networkId|assetId, on}` arms/disarms.

**Constraints:** this is the one genuine engine seam — verify buffer allocation actually happens (not the
base no-op). Live-only (replay traces need no arming).

**Success Conditions:**
1. After `POST /trace/observe {on:true}` for a running entity and a tick, the entity has a populated
   trace working-memory component (was empty before).
2. Disarming stops allocation/population for that asset.
3. Blueprint-driven entities route through `DebugProbe.Sink` and produce trace data.

---

### ADA-P6-T02 — Trace extraction

**Design Reference:** [Group K](./DESIGN.md#group-k--ai-behavior-traces). **Executor:** sonnet.

**Scope:**
- `GET /entities/{networkId}/trace` → `BTreeDebugSession.GetCurrentStateSnapshot/GetRecentNodeHistory`,
  `HsmDebugSession` equivalents, `BlueprintDebugSession.CaptureLiveState(entity, assetId)`.
- Serialize via `EventSerializationHelper` (readable DTOs).

**Constraints:** requires arming (ADA-P6-T01). Marshalled.

**Success Conditions:**
1. For an armed, running BTree entity, the endpoint returns the active node path + recent node history.
2. For an HSM entity, active leaf state + recent transitions.
3. For a blueprint entity, the live state snapshot (no pause required).

---

## Phase 7 — Entity Query / Filter + Spatial

---

### ADA-P7-T01 — Entity filter + spatial

**Design Reference:** [Group B](./DESIGN.md#group-b--queries). **Executor:** zoo.

**Scope:**
- Extend `GET /entities` with `?component=Foo` (has-component filter) and `?near=x,y,r` (spatial radius,
  using the spatial grid / position component).

**Success Conditions:**
1. `?component=BrainBlackboard` returns only entities with that component.
2. `?near=500,500,50` returns only entities within radius 50 of (500,500).

---

## Phase 8 — Live Mutation / Fault Injection

---

### ADA-P8-T01 — Attribute patch (primary)

**Design Reference:** [Group L](./DESIGN.md#group-l--live-mutation--fault-injection). **Executor:** sonnet.

**Scope:**
- `GET /attributes/schema` → `JsonAttributeCompiler.ExportSchema()` / `RegisteredPaths`.
- `POST /entities/{networkId}/attribute {patchJson}` → apply via `JsonAttributeCompiler.Compile(patchJson,
  compiler.CreatePatchContext(repo, entity))`. To match the command-event model, publish
  `UpdateEntityAttributeCommand` and wire a local `UpdateEntityAttributeRequestSystem` (offline ctor) in the
  editor; or call the compiler directly on the job queue.

**Constraints:** authority-aware; unregistered keys safely ignored. Marshalled. Global registry (not
per-TKB-type) — discovery exposes the global schema (optionally intersect with entity components).

**Success Conditions:**
1. `GET /attributes/schema` lists patchable paths (Name, Affiliation, GeoPosition.*, Heading).
2. `POST …/attribute {"Name":"Alpha"}` changes the entity's `EntityInfo.Name` (visible in `GET /entities/{id}`).
3. `{"Heading":90}` updates the entity rotation; an unregistered key is ignored without error.

---

### ADA-P8-T02 — StructEdit component edit (escape hatch)

**Design Reference:** [Group L](./DESIGN.md#group-l--live-mutation--fault-injection). **Executor:** sonnet.

**Scope:**
- `POST /entities/{networkId}/component {componentType, patch}` → `IComponentEditService.Open(component,
  type)` → apply patch to `EditDocument` → `Commit()` (runs `IComponentValidator`) → write boxed value back.

**Constraints:** never patch `NativeChunkTable` memory directly. Validation via the type's validator.
Marshalled.

**Success Conditions:**
1. Editing an arbitrary component field (outside the compiler's registered paths) succeeds and is visible.
2. An invalid value is rejected by the validator (returns `400`, component unchanged).

---

## Phase 9 — Manual-Session Assistance

---

### ADA-P9-T01 — Focus + annotations

**Design Reference:** [Group F](./DESIGN.md#group-f--entity-commands-generic--discovery) (focus), Tier-3 manual-assist. **Executor:** zoo.

**Scope:**
- `POST /entities/{networkId}/focus` → publish `CenterOnEntityCommand`.
- `POST /annotations {…}` → draw debug markers via the gizmo `DebugPrimitiveBuffer` (highlight entities/points).

**Constraints:** publish-only (marshalled); gizmo drawing happens in the render loop, not the API thread.

**Success Conditions:**
1. `POST …/focus` centers the editor camera on the entity (manual verification / camera-target change).
2. `POST /annotations` adds a marker visible on the map (manual verification).

---

## Phase MCP — Node.js MCP Server

---

### ADA-PM-T01 — MCP scaffold

**Design Reference:** [MCP Server (Node.js)](./DESIGN.md#mcp-server-nodejs). **Executor:** sonnet.

**Scope:**
- Node.js project (`@modelcontextprotocol/sdk` + native `fetch`, Node 18+), stdio transport.
- Tool registry + a generic `callApi(method, path, body)` helper that maps the `{ok,data,error,awaited}`
  envelope to MCP tool output verbatim (including `awaited:false`).
- Config: base URL (attach) or launch parameters.

**Constraints:** thin proxy only — no business logic. Envelope passed through verbatim.

**Success Conditions:**
1. The server starts over stdio and lists its tools.
2. `get_status` tool returns the API's `/status` payload through the envelope.
3. An API error surfaces as a structured MCP tool error with the API message.

---

### ADA-PM-T02 — Process lifecycle

**Design Reference:** [MCP Server (Node.js)](./DESIGN.md#mcp-server-nodejs). **Executor:** sonnet.

**Scope:**
- **launch:** spawn `ClusterRunner -m editor --debug-api --debug-api-port N [--headless]`, poll
  `GET /status` until ready, own the child process.
- **attach:** connect to a configured URL of an already-running instance.
- **kill:** `start_simulation`/`stop_simulation` tools; shutdown = `POST /shutdown` (or SIGTERM) → wait with
  timeout → `SIGKILL` if still alive. Tear down child on server exit.

**Constraints:** graceful-then-hard kill with timeout. Don't leak child processes on crash.

**Success Conditions:**
1. `start_simulation` launches the runner and returns once `/status` is reachable.
2. `stop_simulation` shuts it down gracefully; a hung process is `SIGKILL`ed after the timeout.
3. Killing the MCP server also terminates a launched child.

---

### ADA-PM-T03 — Tool definitions

**Design Reference:** [API Surface](./DESIGN.md#api-surface-shared-http--mcp-spec) (all groups). **Executor:** zoo.

**Scope:**
- Define one MCP tool per HTTP endpoint (groups A–N), input schemas mirroring request bodies, each calling
  `callApi` (ADA-PM-T01). Strictly 1:1; no composites.

**Constraints:** names/inputs match the API table exactly. No composite tools in this slice.

**Success Conditions:**
1. Every endpoint in the design's API table has a corresponding MCP tool.
2. A representative end-to-end flow works through MCP: `start_simulation → load_scenario → list_entities →
   get_entity → set_breakpoint → play → get_breakpoint_status → stop_simulation`.
