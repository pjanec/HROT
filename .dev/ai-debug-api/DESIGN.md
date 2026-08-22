# AI Debug & Test API (Editor) + MCP Server — Design

## Overview

This design adds an **in-process HTTP/JSON debug API** to the Hrot ClusterRunner, exposed
only in the all-in-one `-m editor` topology, and a companion **external Node.js MCP server**
that wraps that API as tools for an AI coding agent. Together they let an AI model drive and
inspect a running simulation — list/inspect ECS entities, read event history, control sim time,
load scenarios, issue entity commands, set data-condition breakpoints, checkpoint/diff world
state, record runs for post-mortem replay, query logs, and read AI-behavior execution traces.

The goal is to give an AI agent first-class testing and diagnostic leverage over the simulation,
both for **autonomous test-fix loops** (the agent launches the runner, drives an experiment,
observes, and tears it down) and for **manual-session assistance** (the agent helps a human who is
already driving the editor).

The capability layer this API exposes is almost entirely **already built** inside `EditorSubsystem`
against a single shared `EntityRepository`; this design is largely *wiring and exposure*, with one
small new engine seam (live AI-trace arming) and a few minor utilities.

---

## Goals / Non-goals

**Goals**
- Expose simulation query + control + command capabilities over a minimal local HTTP/JSON API.
- A thin, self-contained, opt-in component owned by `EditorSubsystem` — not a core part of the runner.
- An external Node.js MCP server that proxies the API and manages the runner process lifecycle.
- Design all capability tiers up front; stage their implementation/testing as separate tasks.

**Non-goals**
- No ASP.NET Core, no DI container, no generic web-hosting model.
- No live event *streaming* / push (SSE/WebSocket/DDS) in this design — event **history** only.
- No deployment on live distributed nodes (SimHost/CGF). Per architecture, live inspection must go
  through ExCon/IG over DDS; exposing this HTTP API on a live node violates the Anti-Corruption Layer.
- No world-source abstraction for remote access. The bounded universe is `-m editor` and
  (future) `-m replaybrowser`, both local in-process repositories.

---

## Topology & Key Decisions

The API targets `-m editor` because it is a guaranteed **single process with one shared ECS world**:

- `EditorSubsystem` constructs one `EntityRepository _world` ([EditorSubsystem.cs:574]) and wires
  *both* brain (CGF/AI: `CgfLogicPack`, `BehaviorRegistry`, blueprint runtime, AI hot-reload) and
  muscle (`SimHostCoreLogicPack`, physics, spawning, perception) into the **same** kernel/world.
- Config validation guarantees editor mode is always standalone, single-process, no DDS peers
  ([HrotRunnerConfiguration.cs:128, :144]). There is **no DDS participant**
  (`OfflineNetworkFactory.Participant => null`, [OfflineNetworkFactory.cs:85]).
- Consequently the distributed brief (DDS gateways, `TransitionStateIntent` over DDS,
  `ITimeControlGateway`, ISubsystem-plugin direct-memory) collapses into **direct in-process calls**.

**Two-layer split:**
1. **ClusterRunner side** — a minimal `HttpListener`-based component owned by `EditorSubsystem`,
   activated only by an explicit flag/config key (off by default). All semantics (threading,
   wait-gating, run-mode rules) live here.
2. **External Node.js MCP server** — a thin proxy that maps MCP tools 1:1 onto HTTP endpoints,
   plus runner process lifecycle (launch / attach / graceful-then-hard shutdown). No business logic.

---

## Architectural Principles

- **Self-contained & opt-in.** The web component lives in its own folder/namespace
  (`Hrot.Editor.DebugApi`), is constructed in `EditorSubsystem.Initialize()` only when configured,
  pumped in `Update()`, disposed in `Shutdown()`. Off by default; not a prime part of the runner.
- **Single-threaded ECS is sacred.** Every operation that touches `_world`, `NetworkEntityMap`, or
  publishes to the bus is **marshalled onto the main loop thread** via a job queue drained inside
  `EditorSubsystem.Update()`. This mirrors the existing console
  `EnqueueConsoleAction`/`DrainConsoleActions` pattern.
- **Reuse, don't rebuild.** Every capability maps to an existing, verified service (see Reuse Map).
  New code is limited to: one engine seam (live trace arming), a CLR→JSON-schema helper for discovery,
  and log filtering.
- **Autonomous, not strictly headless.** The editor process is running (a window may be open); we drive
  it programmatically. We therefore do *not* require zero UI-assembly dependencies — but we still never
  invoke ImGui (or ImGui-state-probing adapters) outside the render frame, on any thread, and we never
  reuse a UI class where a pure service exists.
- **Readable serialization is via the custom DTO machinery, never raw.** Both component *and* event
  JSON must go through the inspector-grade custom-translator path (`DtoDiagnosticMapper` / `ScenarioSerializer`
  translators), not plain `System.Text.Json` on raw objects — otherwise fixed-buffers, `[InlineArray]`
  blackboards, boxed `List<object>` component lists, and `Entity` handles serialize as opaque garbage.
- **API owns safety, MCP stays dumb.** Wait-gating, run-mode exclusivity, and thread marshalling are
  enforced by the API. The MCP server never reasons about sim state — it just relays.
- **Bounded universe.** Editor (and future Replay Browser) only. No remote/live abstraction.

---

## Component Architecture

```
┌─────────────────────────────────────────────┐        ┌──────────────────────────┐
│ ClusterRunner process  (-m editor)           │        │ Node.js MCP server (ext) │
│                                               │        │                          │
│  EditorSubsystem                              │ HTTP   │  - stdio MCP transport   │
│   ├─ _world (EntityRepository)   ◄────────────┼───────►│  - tool → fetch(endpoint)│
│   ├─ _timeController, _previewController       │ JSON   │  - launch/attach runner  │
│   ├─ _bpManager, rrController, debug sessions  │        │  - graceful→hard kill    │
│   └─ DebugApiHost (HttpListener)              │        └──────────────────────────┘
│        ├─ background HTTP threads              │                  ▲
│        └─ MainThreadJobQueue ──drained in──┐   │                  │ MCP (stdio)
│                                            ▼   │                  │
│        EditorSubsystem.Update(): … kernel tick … then DrainApiJobs()
└─────────────────────────────────────────────┘            AI coding agent
```

- **`DebugApiHost`** owns the `HttpListener`, route table, JSON (de)serialization, and the
  `MainThreadJobQueue`. Constructed with handles to the editor's services
  (`_world`, `_entityMap`, the `EntityStateExtractionService`, `_timeController`,
  `_previewController`, `_editorLogic`, `_orchestrationBus`, `_bpManager`, `rrController`,
  the debug sessions, and the log sinks).
- **Activation:** a new config flag (e.g. `--debug-api` / `DebugApi.Enabled` + `DebugApi.Port`,
  localhost-bound by default). When absent, `DebugApiHost` is never constructed.

---

## Threading & Marshalling Model

- HTTP requests arrive on `HttpListener` background threads.
- Each handler that touches simulation state builds a **job** (a delegate returning a JSON-serializable
  result), enqueues it on a thread-safe `MainThreadJobQueue`, and blocks on a `TaskCompletionSource`.
- `EditorSubsystem.Update()` drains the queue **after the kernel tick, before draw**, so jobs observe
  a quiescent, post-tick world. Each job runs on the main thread and fulfils its `TaskCompletionSource`.
- The HTTP thread unblocks and serializes the result.

**Background-thread hard rules** (verified with architect; violations crash the process):
- **Never call ImGui** from a background thread — Dear ImGui's native context is thread-local; calls
  (or adapters that probe ImGui state: `ImGuiInputSource`, `ImGuiClipboard`) throw an uncatchable
  `AccessViolationException`.
- **Never read Raylib / `MapCamera` / viewport / `CullingState`** off-thread (the render loop mutates them).
- **Safe off-thread:** `DiagnosticEventHistoryService.GetHistory()` (copy-under-lock) and the
  log sinks (`NLogMessageLogTarget.GetMessages()`, lock-guarded). **Entity extraction must run on the
  main thread (marshalled) or against a paused/snapshot repo** — never live off-thread.

Practical consequence: **log reads and event-history *retrieval* bypass the queue** (already thread-safe);
**everything else is marshalled**. Nuance for events: `DtoDiagnosticMapper` over the *captured* event
copies is pure and off-thread-safe, but resolving an event's `Entity`-handle fields to networkIds touches
`NetworkEntityMap` (not thread-safe) — so payload serialization that performs entity-ref resolution is
marshalled to the main thread. Event JSON without entity-ref resolution can stay off-thread.

---

## Run-Mode Model

Two mutually-exclusive ways to advance the sim in the editor, surfaced as distinct API verbs:

| | **Preview run** | **Live run** |
|---|---|---|
| Trigger | `EnterPreviewMode()` (the toolbar "play") → `OperatingPreview` | `TransitionStateIntent{OperatingLive}` → `ReferenceLiveLoadHandler` |
| Revertible | ✅ RAM snapshot on enter, rewind on exit | ❌ monotonic; rewind would corrupt the recording |
| Recording | ✅ **optional** (forward segment, finalize-before-rewind) | ✅ automatic `.fdp` |
| Replay-Browser GUI ledger | ❌ (load `.fdp` directly by path) | ✅ `RecordingLedgerEntry` written |
| Best for | "try → observe → revert" experiments; checkpoint/restore | committed recorded runs |

Recording is mechanically valid only on a **monotonically advancing** world. The forward portion of a
preview run qualifies; the *exit rewind* (`_liveRepo.SyncFrom(_snap)`) is the only incompatibility, so
recorded-preview sequences the recorder finalize **before** the rewind (see Recording section).

The API rejects: checkpoint/restore requests during a live run, and naive "record" requests that
don't follow the finalize-before-rewind ordering.

---

## Wait-Gating Semantics (command results)

Most FDP commands are fire-and-forget; a few have a correlated ack (e.g. `MissionControlIntent` →
`MissionControlAckEvent` keyed by `RequestId`). For commands that request a result-wait:

- The API first checks **whether time is advancing**: `InPreview && !Paused`
  (`Paused == _timeController.GetMode() == TimeMode.Deterministic`), read via the same
  `EditorTimeTransportFacade` the toolbar uses.
- If time is **not** advancing → publish and return immediately with `{ "awaited": false,
  "reason": "sim not running" }`. (Blocking would hang — no ticks means no ack.)
- If time **is** advancing → wait for the correlated ack, **always** with a timeout; degrade to
  `awaited:false` on expiry (the sim can pause mid-wait, or the command may have no ack at all).

This logic lives entirely in the API. The MCP server never decides whether to wait. This enables the
discrete-stepping flow: pause → POST command (returns, not awaited) → POST step×N → GET entity/events.

---

## API Surface (shared HTTP ↔ MCP spec)

Each capability defines the HTTP endpoint and the MCP tool together; tools are strictly 1:1 with
endpoints. All bodies are JSON. `networkId` is the long network entity id (resolved via
`NetworkEntityMap.TryGetEntity`). Responses include a standard envelope
`{ ok, data?, error?, awaited? }`.

### Group A — Lifecycle & Status
| HTTP | MCP tool | Request → Response |
|---|---|---|
| `GET /status` | `get_status` | → `{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }` |
| `POST /shutdown` | `stop_simulation` | → `{ ok }` then process exits cleanly (signals main loop) |

(`start_simulation` is MCP-side only — it spawns the runner; see MCP section.)

### Group B — Queries
| HTTP | MCP tool | Request → Response |
|---|---|---|
| `GET /entities` | `list_entities` | optional `?component=Foo&near=x,y,r` → `[{ networkId, name, components:[…names] }]` |
| `GET /entities/{networkId}` | `get_entity` | → full component dump (`EntityStateDumpDto` via serializer-injected `EntityStateExtractionService`) |
| `GET /components` | `list_component_types` | → `[{ name, fields:[{name,type}] }]` (discovery) |
| `GET /scenarios` | `list_scenarios` | → `[relativePath]` (`EditorApplication.AvailableScenarios`) |

### Group C — Event History
| HTTP | MCP tool | Request → Response |
|---|---|---|
| `GET /events?bus=world\|orchestration&type=&since=&max=` | `get_event_history` | → `[{ frame, provider, type, isManaged, summary, payload }]`. Payload JSON via the **custom DTO mapper** (`DtoDiagnosticMapper.MapObject` → `JsonSerializer` with `FdpJsonOptionsRegistry`), the same machinery the inspector uses — NOT plain STJ on the raw event and NOT the UI `EventBrowserPanel`. Default `bus=world`. History retrieval + pure DTO mapping are off-thread; `Entity`-ref→networkId resolution (if requested) is marshalled (see Threading). |

### Group D — Sim / Preview / Time Control (via `EditorTimeTransportFacade`)
| HTTP | MCP tool | Notes |
|---|---|---|
| `GET /sim/state` | `get_sim_state` | `{ isPaused, inPreview, totalTime, timeScale }` |
| `POST /sim/play` | `play` | explicit: enter preview / resume if needed |
| `POST /sim/pause` | `pause` | explicit pause |
| `POST /sim/step {count?}` | `step` | discrete single-step(s) (`Step(1/60)` each) |
| `POST /sim/timescale {scale}` | `set_time_scale` | |
| `POST /preview/enter {startPaused?}` | `enter_preview` | |
| `POST /preview/exit` | `stop_preview` | |

Explicit `play`/`pause` wrap the facade's `TogglePlayPause` after reading state, so the AI gives
explicit intent and never blind-toggles.

### Group E — Scenario Load
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `POST /scenario/load {name, waitForReady?}` | `load_scenario` | calls `IEditorLogic.LoadScenarioByName`; if `waitForReady`, the job polls `_orchestrationBus.ReadManaged<ClusterStateUpdateEvent>()` across frames and returns `200` only when `CurrentState == OperatingEdit` (multi-tick genesis pipeline complete). `LoadedScenarioName` is **not** a completion signal. |
| `POST /scenario/save {name}` | `save_scenario` | `ScenarioFileService.SaveScenario` — persists the authored world (completes the discover→spawn→save authoring loop) |

### Group F — Entity Commands (generic + discovery)
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `GET /commands` | `list_commands` | enumerates publishable FDP event types (`EventType.GetAllRegistered()`) + field schemas (discovery) |
| `POST /entities/command {eventType, payload, wait?}` | `send_entity_command` | deserialize → `Publish<T>`/`PublishManaged<T>` on `_world.Bus`; wait-gated correlated ack if applicable |
| `POST /entities/spawn {tkbType, transform?, components?, attributesJson?}` | `spawn_entity` | convenience over `SpawnEntityCommand` |
| `POST /entities/{networkId}/focus` | `focus_entity` | publishes `CenterOnEntityCommand` (manual-assist) |

### Group G — Run-Until-Condition (breakpoints)
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `POST /breakpoints {condition: SearchPredicateDto, filterNetworkId?, occurrenceThreshold?, name?}` | `set_breakpoint` | `IDataBreakpointManager.AddBreakpoint(...)` → `{ breakpointId }` |
| `GET /breakpoints` | `list_breakpoints` | `AllBreakpoints` |
| `DELETE /breakpoints/{id}` | `remove_breakpoint` | `Remove(id)` |
| `GET /breakpoints/hits` | `get_breakpoint_status` | `{ isPaused, pausedTick, lastHit:{ id, networkId } }` (from `OnBreakpointHit`/`OnPauseStateChanged`) |

`condition` is a polymorphic `SearchPredicateDto` (PropertyMatch / TransientEvent / Compound /
SpatialBounding / Lifecycle / …) — the **same DSL** the Replay Browser search uses. On hit, the
manager rewinds and `RequestPause()`s; the AI then runs `play` and inspects when paused.

### Group H — Checkpoint / Restore + Diff (preview-run only)
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `POST /checkpoint` | `checkpoint` | single-slot RAM snapshot via `IPreviewController.EnterPreviewMode(startPaused:true)` (the editor's safe facade — **never** `PreviewClusterOpHandler` directly) |
| `POST /checkpoint/restore` | `restore_checkpoint` | rewind via `IPreviewController.ExitPreviewMode()` |
| `POST /diff {entities?}` | `diff_state` | `ComponentDiffService.ComputeTreeDiff(before, after, epsilon)` → `DiffNode` tree (serialize entity state before/after the workload, diff the JSON) |

> **Single-slot.** The only blessed snapshot mechanism is the preview slot, accessed via
> `IPreviewController` — so checkpoint/restore is one level (it *is* preview enter/exit) and is
> mutually exclusive with a live run. **Keyed multi-checkpoints** (try N sequences from one state,
> retained simultaneously) are deferred to future engine work — a proper snapshot service, not a
> bypass of `PreviewClusterOpHandler`. Note `diff` does **not** require a checkpoint: it can serialize
> entity state before and after a workload and diff the two JSON trees.

### Group I — Recording + Replay
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `POST /recording/start {mode: preview\|live}` | `start_recording` | preview: `EnterPreviewMode → PrepareRecordingAsync`; live: `TransitionStateIntent{OperatingLive}` |
| `POST /recording/stop` | `stop_recording` | `FinalizeRecordingAsync` (preview: **before** the exit rewind) → `{ fdpPath }` |
| `POST /replay/load {fdpPath}` | `load_replay` | stands up an **isolated** `ReplayBrowserContext` (its own `SandboxRepo` + `SandboxBus`); bypasses ledger |
| `POST /replay/seek {frame}` / `step {dir}` | `replay_seek` / `replay_step` | `PlaybackController` over the sandbox repo |

ExerciseId/dir come from `ClusterStateUpdateEvent.ExerciseId` + `OrchestrationConstants.DefaultStagingDirectory` — never minted.

> **Replay isolation (mandatory).** Replay runs in a `ReplayBrowserContext` whose `SandboxRepo` is a
> *separate* `EntityRepository` ([ReplayBrowserContext.cs:30]), disconnected from the live `_world`,
> `_world.Bus`, and the kernel tick. Seeking restores historical keyframes **into the sandbox only**.
> In replay mode the query services (entity dump, event history, diff, traces) are instantiated against
> the **sandbox repo + sandbox history service**, never `_world`. A seek must *never* touch `_world` —
> doing so would overwrite live state and desync `MasterSyncController`. This is the "world-source"
> parameterization (live `_world` vs sandbox repo) — both local, within the bounded universe.

### Group J — Logs
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `GET /logs?level=&logger=&since=&max=` | `get_logs` | filter over `NLogMessageLogTarget.GetMessages()` / `AiBehaviorLogTarget`; **off-thread (thread-safe)** |

### Group K — AI Behavior Traces
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `POST /trace/observe {networkId, on}` | `observe_entity` | arms/disarms tracing for an entity (**new seam**, live only; not needed in replay) |
| `GET /entities/{networkId}/trace` | `get_behavior_trace` | `BTreeDebugSession.GetCurrentStateSnapshot/GetRecentNodeHistory`, `HsmDebugSession` ditto, `BlueprintDebugSession.CaptureLiveState` → JSON via existing translators |

### Group L — Live Mutation / Fault Injection
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `GET /attributes/schema` | `get_patchable_attributes` | `JsonAttributeCompiler.ExportSchema()` / `RegisteredPaths` — discoverable patchable JSON paths + value types (e.g. `Name`, `Affiliation`, `GeoPosition.Latitude/Longitude/Altitude`, `Heading`) |
| `POST /entities/{networkId}/attribute {patchJson}` | `patch_attribute` | **primary path** — `UpdateEntityAttributeCommand`-style attribute patch compiled by `JsonAttributeCompiler` (authority-aware, discoverable) |
| `POST /entities/{networkId}/component {componentType, patch}` | `edit_component` | **escape hatch** — arbitrary component-field edit via StructEdit `IComponentEditService` (validated), for fields outside the registered paths |

> **Two complementary mutation paths (both marshalled to main thread):**
>
> 1. **Attribute patch — `JsonAttributeCompiler` (primary, discoverable).** The sophisticated JSON→ECS
>    compiler (`Fdp.Toolkits/Replication/Patching/JsonAttributeCompiler.cs`): `Compile(patchJson,
>    compiler.CreatePatchContext(repo, entity))`. Zero-alloc, **authority-aware** (`CanWrite<T>()`),
>    routing-table based; unregistered keys are safely ignored. Its `RegisteredPaths`/`ExportSchema()`
>    powers `GET /attributes/schema` so the AI can discover legal patch keys + types. This is the
>    `UpdateEntityAttributeCommand` mechanism — to stay consistent with the command-event model, the
>    API publishes `UpdateEntityAttributeCommand` and a **locally-wired `UpdateEntityAttributeRequestSystem`**
>    (its interface ctor exists for offline use; not currently registered in the editor — small new wiring)
>    applies it via the compiler. (Direct `compiler.Compile(...)` on the job queue is the simpler
>    alternative if we prefer to skip the bus round-trip.)
>    *Limitation:* `RegisteredPaths` is a single global registry, not per-TKB-type; v1 discovery exposes
>    the global schema (optionally intersected with the entity's live components). Per-type narrowing is
>    a future enhancement.
>
> 2. **Arbitrary component edit — StructEdit (escape hatch).** For component fields the compiler does not
>    register, edit via `IComponentEditService.Open(component, type)` → patch the `EditDocument` →
>    `session.Commit()` (runs the type's `IComponentValidator`, boxes the result) → write back. Same
>    machinery the Entity Inspector uses (`ComponentEditServiceBuilder`, EditorSubsystem.cs:973). Never
>    patch `NativeChunkTable` memory directly.

### Group M — Entity-Type / TKB Catalog (scenario authoring)
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `GET /tkb/types?category=` | `list_entity_types` | `TkbDatabase.GetAll()` → `[{ tkbType, name, categoryPath, disType }]` (optional `GetEntitiesByCategory`) |
| `GET /tkb/types/{tkbType}` | `get_entity_type` | full descriptor: `MandatoryComponents`, `ChildBlueprints`, `DisType`, and the descriptor bag (`GetAllDescriptors()` → VehicleParameters / BehaviorProfile / Sensor / CombatPlatform DTOs) — introspected **without spawning** |

> Read-only static catalog (built at init), so these reads are **off-thread-safe** like event history.
> Descriptor DTOs serialize through the same `EventSerializationHelper`/`DtoDiagnosticMapper` path as
> events/components (readable, not raw). This is what lets the AI author scenarios: discover types →
> inspect what each materializes → `spawn_entity` (Group F, `SpawnEntityCommand.TkbType`) →
> `save_scenario` (Group E). `NetworkSpawningSystem` validates the id via `TkbDatabase.TryGetByType`
> and runs the translators to materialize components.

### Group N — World / Coordinate Info (scenario authoring)
| HTTP | MCP tool | Behaviour |
|---|---|---|
| `GET /world/info` | `get_world_info` | `{ geo:{origin:{lat,lon,alt}}, spatialGrid:{cellSize, originX, originY, width, height, extent:{minX,maxX,minY,maxY}}, terrain:null, navmesh:null }` |
| `POST /world/geo-to-local {lat,lon,alt, headingDeg?}` | `geo_to_local` | `IGeographicTransform.ToCartesian` → `{x,y,z}`; optional `headingDeg` → `rotation` (quaternion) via `SimTransformBridgeSystem.HeadingDegToRotation` |
| `POST /world/local-to-geo {x,y,z, rotation?}` | `local_to_geo` | `IGeographicTransform.ToGeodetic` → `{lat,lon,alt}`; optional `rotation` → `headingDeg` via `SimTransformBridgeSystem.RotationToHeadingDeg` |

> **Orientation conversion** uses `SimTransformBridgeSystem.HeadingDegToRotation` / `RotationToHeadingDeg`
> (`Fdp.Toolkits/Geographic`) — the same helpers the NED/BDC/IG geo translators use. ENU frame:
> heading is degrees clockwise from North (North=0°, East=90°); `SimTransform.Rotation` is a `Quaternion`.
> These are stateless → off-thread-safe. **Known pre-existing bug** (test-health tracker):
> `RotationToHeadingDeg` mishandles a degenerate pitch-down (90°) rotation (returns 90 instead of 0) — not
> a blocker for planar authoring, but flag it; fix if vertical orientations matter.

> **In the editor the placement envelope is the spatial grid.** `SpatialHashGrid` (Width/Height/CellSize/
> OriginX/Y; default 1000×1000 m @ 5 m from origin 0,0) is live via `CognitiveSpatialModule` and is the
> authoritative "where can I place entities" region for authoring. **Terrain/navmesh bounds are IG/SimHost-
> only** (no bounds API even there) and report `null` in editor — be explicit so the AI doesn't assume a
> larger world. Conversion methods are public and stateless (off-thread-safe); the grid extent is static
> config (safe to read); **only the geo origin needs new exposure** (see New Work).

| Capability | Existing type / entry point | File:line | Headless |
|---|---|---|---|
| Entity dump (readable blackboards) | `EntityStateExtractionService(repo, map, serializer)` | EditorSubsystem.cs:783 | ✅ |
| Time/preview control | `EditorTimeTransportFacade` (toolbar's own facade) | Hrot.Editor/UI/EditorTimeTransportFacade.cs | ✅ |
| Event history | `DiagnosticEventHistoryService.GetHistory()` (copy-under-lock) | Fdp.Core/Diagnostics | ✅ off-thread |
| Event→JSON (readable) | `DtoDiagnosticMapper.MapObject` (custom DTO/fixed-buffer/InlineArray machinery, currently `internal`) → `JsonSerializer` w/ `FdpJsonOptionsRegistry`. Needs new public `EventSerializationHelper` (see New Work) — **not** plain STJ, **not** the UI panel | DtoDiagnosticMapper (Fdp.Toolkit.Diagnostics) | ⚠ partial-new |
| Scenario load / list | `IEditorLogic.LoadScenarioByName` / `AvailableScenarios`; ready = `ClusterStateUpdateEvent.CurrentState==OperatingEdit` | EditorApplication.cs:156/58/78 | ✅ |
| Entity commands | `Publish<T>`/`PublishManaged<T>`; `SpawnEntityCommand`, `CenterOnEntityCommand`, `MissionControlIntent`(+Ack), `UpdateEntityAttributeCommand` | FdpEventBus.cs:34/69 | ✅ |
| Attribute patch (primary, discoverable) | `JsonAttributeCompiler.Compile` / `CreatePatchContext` / `ExportSchema` / `RegisteredPaths`; applied via `UpdateEntityAttributeRequestSystem` (offline ctor) | Fdp.Toolkits/Replication/Patching/JsonAttributeCompiler.cs | ✅ (main-thread; local system needs wiring) |
| Arbitrary component edit (escape hatch) | StructEdit `IComponentEditService.Open → EditDocument → Commit (IComponentValidator)` | ComponentEditServiceBuilder, EditorSubsystem.cs:973 | ✅ (main-thread) |
| Run-until-condition | `IDataBreakpointManager.AddBreakpoint(SearchPredicateDto,…)` → rewind + `RequestPause()` | Hrot.Diagnostics.Breakpoints; wired EditorSubsystem.cs:977 | ✅ |
| Predicate DSL | `SearchPredicateDto` (shared w/ replay search) | Fdp.Toolkits/ReplayBrowser/Search | ✅ |
| State diff | `ComponentDiffService.ComputeTreeDiff` | Fdp.Toolkits/ReplayBrowser/Diff | ✅ |
| Checkpoint/restore | `IPreviewController.EnterPreviewMode/ExitPreviewMode` (editor facade; **not** `PreviewClusterOpHandler` directly) — single slot | `_previewController`, EditorSubsystem.cs:433-461 | ✅ |
| Replay (isolated) | `ReplayBrowserContext` (own `SandboxRepo`, [:30]) — disconnected from live `_world`/bus/tick | Fdp.Toolkits/ReplayBrowser | ✅ |
| Recording | `EcsRecordReplayController.PrepareRecordingAsync/FinalizeRecordingAsync`; live trigger `ReferenceLiveLoadHandler.cs:94` | EditorSubsystem.cs:816 | ✅ |
| Replay seek/load (over the isolated sandbox above) | `ReplayBrowserContext.LoadRecording/SeekToFrame`, `PlaybackController` | Fdp.Toolkits/ReplayBrowser | ✅ |
| Auto-keyframe (self-contained mid-session recording) | `RecorderTickSystem` ctor `_framesSinceKeyframe=KeyframeInterval-1` | RecorderTickSystem.cs:38 | ✅ |
| Logs | `NLogMessageLogTarget.SharedInstance.GetMessages()`, `AiBehaviorLogTarget` | Fdp.Core/Logging | ✅ off-thread |
| AI traces (read) | `BTree/HsmDebugSession.Update/GetCurrentStateSnapshot`, `BlueprintDebugSession.CaptureLiveState` | EditorSubsystem.cs:660-662 | ✅ (read) |
| Entity lookup | `NetworkEntityMap.TryGetEntity/TryGetNetworkId` (main-thread only) | Fdp.Toolkit.Replication.Services | main-thread |
| Discovery enum | `EventType.GetAllRegistered()`, `ComponentTypeRegistry.GetAllTypes()`, `FdpAutoSerializer.GetSortedMembers` | Fdp.Core | ✅ |
| Entity-type / TKB catalog | `TkbDatabase.GetAll()/GetByType/GetEntitiesByCategory`, `TkbTemplate.GetAllDescriptors()` (introspect w/o spawn) | Fdp.Toolkits/Tkb/TkbDatabase.cs, Fdp.Core/Abstractions/TkbTemplate.cs | ✅ off-thread (static catalog) |
| Scenario save | `ScenarioFileService.SaveScenario` | Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs | ✅ (main-thread) |
| Geo convert (geo↔local position) | `IGeographicTransform.ToCartesian/ToGeodetic` (`WGS84Transform` ENU, Berlin origin default) | Fdp.Toolkits/Geographic/Transforms/WGS84Transform.cs | ✅ off-thread (stateless) |
| Orientation convert (heading↔rotation) | `SimTransformBridgeSystem.HeadingDegToRotation/RotationToHeadingDeg` (ENU, N=0°/E=90°; ⚠ known pitch-down degenerate bug) | Fdp.Toolkits/Geographic, SimTransformBridgeSystem | ✅ off-thread (stateless) |
| Geo origin | `WGS84Transform._originLat/Lon/Alt` (private — **needs getter**, see New Work) | HrotEnvironment.cs:25-29 (hardcoded) | ⚠ needs exposure |
| Placement envelope (editor) | `SpatialHashGrid` Width/Height/CellSize/OriginX/Y (via `CognitiveSpatialModule`) | Fdp.Toolkits/CarKinem/Spatial/SpatialHashGrid.cs | ✅ (static extent) |
| Marshalling precedent | `EnqueueConsoleAction`/`DrainConsoleActions` | SubsystemOrchestrator | ✅ |

---

## New Work (everything else is wiring)

1. **Live AI-trace arming seam.** Before extraction, call `AiTracerCoordinator.BeginObservingAsset`
   for the target asset so `TraceBufferLifecycleSystem` allocates the
   `BTreeTraceWorkingMemory1024`/`HsmTraceWorkingMemory1024` components (sets `DebugState.Flags`);
   without it `GetCurrentStateSnapshot` returns empty buffers. `BeginObservingAssetImpl` is a virtual
   no-op in the base, so confirm/implement the editor override that actually sets the flags, and register
   a blueprint `DebugMap` for field decoding. Note blueprints trace via `DebugProbe.Sink`, not
   `AiTracerCoordinator` — handle per asset type. **Live tracing only — replay traces need no arming**
   (trace components are `[DataPolicy(NoSave)]`, flight-recorded and restored on seek).
2. **CLR→JSON-schema helper** for `/commands` and `/components` (enumeration exists; only field-shape
   emission is missing — walk `GetSortedMembers(Type)`, map CLR types to JSON primitives).
3. **Log filtering** (level/logger/since/max) over the sinks' `GetMessages()` snapshot.
4. **`EventSerializationHelper`** (the essential one) — a new public helper in `Fdp.Toolkit.Diagnostics`
   that serializes an arbitrary event object (struct or managed) to inspector-grade readable JSON via
   `DtoDiagnosticMapper.MapObject(obj, obj.GetType(), visited)` → `JsonSerializer` with `FdpJsonOptionsRegistry`.
   Requires promoting `DtoDiagnosticMapper` from `internal` → `public`. Handles fixed-buffers, `[InlineArray]`,
   and boxed `List<object>` component lists. **Must also resolve `Entity`-handle fields to networkId** (via
   a `DiagnosticGuidResolver`/`NetworkEntityMap`, like the component path) — that resolution step is
   main-thread-only, so payload serialization that resolves entity refs runs on the job queue. Plain STJ
   and `EventBrowserPanel.BuildCopyJson` are both rejected (raw, no translator pass).
5. **Local attribute-patch wiring.** Register a local `UpdateEntityAttributeRequestSystem` in the editor
   (its interface-based ctor exists for offline use; `OfflineNetworkFactory` registers none today) so
   published `UpdateEntityAttributeCommand`s are applied via `JsonAttributeCompiler`. (Alternatively, the
   API calls `JsonAttributeCompiler.Compile` directly on the job queue.) Plus expose `ExportSchema()` for
   the `/attributes/schema` discovery endpoint.
6. **Geo-origin exposure.** `WGS84Transform` stores the origin privately with no getter. Add an
   `Origin` (lat/lon/alt) getter to `IGeographicTransform`/`WGS84Transform`, or capture the origin the
   editor passes at `CreateGeoTransform()` time and hand it to `DebugApiHost`. (Conversion methods are
   already public; only the origin read is missing.)
7. **`DebugApiHost`** (the `HttpListener`, routing, `MainThreadJobQueue`, JSON envelope) + config flag
   + `/shutdown` main-loop exit signal.

**Deferred (future engine work, not in these tasks):** *keyed multi-checkpoints* — the only blessed
snapshot today is the single preview slot via `IPreviewController`; retaining multiple named snapshots
simultaneously needs a dedicated snapshot service (must not bypass `PreviewClusterOpHandler`).

---

## MCP Server (Node.js)

- **Transport:** stdio (standard for a locally-launched MCP server driven by a coding agent).
- **Deps:** `@modelcontextprotocol/sdk` + native `fetch` (Node 18+). No business logic.
- **Tool mapping:** strictly 1:1 with HTTP endpoints (see API table). Tool input schemas mirror request
  bodies; outputs pass through the `{ok,data,error,awaited}` envelope **verbatim** (including
  `awaited:false`), so the model sees "published, not awaited (sim not running)" rather than a hang.
- **Process lifecycle:**
  - *launch* — spawn `ClusterRunner -m editor --debug-api --port N [--headless]`, poll `GET /status`
    until ready, own the child, tear down on server exit and on `stop_simulation`.
  - *attach* — connect to an already-running instance at a configured URL (handy mid manual session).
  - **Kill = graceful-then-hard:** `POST /shutdown` (or SIGTERM) → wait with timeout → `SIGKILL` the
    child if still alive.
- **Errors/timeouts:** HTTP errors surfaced as structured MCP tool errors with the API's message.
- This self-contained launch → drive → stop is what makes the server usable inside **test-fix
  integration loops**.

---

## Future Directions (record now, design later)

- **MCP control of the Replay Browser** (`-m replaybrowser`) for AI-driven **post-mortem analysis** of
  recorded `.fdp` files. Strong reuse: `ReplayBrowserContext` materializes a real `EntityRepository`
  per frame, plus `FederatedReplayManager` (multi-node sync), `SearchPredicateDto` search, causality
  jumps, and `ComponentDiffService` — all headless. Because a replay frame **is** a live
  `EntityRepository`, the entire read/query layer (entity dump, event history, predicate search, diff,
  AI-behavior traces) is the **same implementation pointed at a different repo** — direct code sharing,
  not just interface abstraction. Only the control plane forks (live control vs frame seek; replay world
  is read-only). Trace components survive record→seek, so behavior traces in replay need no arming seam.
  Note: this is *within* the bounded universe (both local in-process repos); it is **not** the
  remote/live abstraction the architecture forbids.
- **Live event streaming** (SSE/WebSocket, possibly DDS) — superseded for now by event history; revisit
  if push semantics become necessary.
- **Optional ledger entry for preview recordings** so they also appear in the Replay Browser GUI
  "Available Exercises" dropdown (currently only `OperatingLive` runs are ledgered).

---

## Out of Scope

- Live event streaming/push.
- Deployment on live distributed SimHost/CGF nodes (ACL violation; use ExCon/IG over DDS).
- Remote/live world-source abstraction.
- Any modification of `PreviewClusterOpHandler` to entangle it with recording (keep it generic).

---

## Task Decomposition & Staging

Designed together; built/tested in stages. Each is a separate, largely non-overlapping task.

- **T0 — Web host foundation.** `DebugApiHost` (HttpListener + routing + JSON envelope),
  `MainThreadJobQueue` drained in `EditorSubsystem.Update()`, config flag, `/shutdown`. *(Prereq for all.)*
- **T1 — Slice 1 surface.** Groups A/B/C/D/E/F/M/N: status, entity list/dump, event history, sim+preview+time
  control, scenario load/list/**save**, generic command + discovery (`/commands`, `/components`, `/scenarios`),
  the **entity-type / TKB catalog** (`/tkb/types`), and **world/coordinate info** (`/world/info`, geo↔local
  convert) for scenario authoring. (TKB descriptor JSON shares the `EventSerializationHelper` DTO path; needs
  retained `_tkbDb` + geo-transform + spatial-grid references passed to `DebugApiHost`, and a geo-origin getter.)
- **T2 — Run-until-condition.** Group G (breakpoints; reuse, `SearchPredicateDto` JSON).
- **T3 — Checkpoint/restore + diff.** Group H (single-slot snapshot via `IPreviewController` + `ComponentDiffService`; keyed multi-checkpoint deferred).
- **T4 — Recording + replay.** Group I (preview + live recording, finalize-before-rewind; light seek/diff).
- **T5 — Logs.** Group J (filter over thread-safe sinks).
- **T6 — AI behavior traces.** Group K (the live-trace arming seam + extraction).
- **T7 — Entity query/filter + spatial.** Group B filters.
- **T8 — Live mutation / fault injection.** Group L: attribute patch via `JsonAttributeCompiler` +
  `/attributes/schema` discovery + local `UpdateEntityAttributeRequestSystem` wiring; StructEdit escape hatch.
- **T9 — Manual-assist.** focus-on-entity, gizmo annotations.
- **T-MCP — Node.js MCP server.** All tools (launch + attach, graceful→hard kill), mirroring the API.

Recommended order: T0 → T1 → T-MCP (so the loop is usable end-to-end early) → T2/T3/T4 (the
autonomous-testing leverage tier) → T5–T9.

---

## Appendix: Architect-Confirmed Decisions (verified against code)

- Scenario-load completion signal = `ClusterStateUpdateEvent.CurrentState == OperatingEdit` on the
  orchestration bus; `LoadedScenarioName` is set frame 0 and is **not** a completion signal.
- Recording is valid only on a monotonically advancing world; `ReferenceLiveLoadHandler` records on
  `OperatingLive`. **Recorded preview** is feasible by finalizing the recorder **before** the exit
  rewind; first recorded frame auto-keyframes (`RecorderTickSystem.cs:38`) → self-contained `.fdp`.
- Preview recordings are valid on disk but not ledgered → load via `ReplayBrowserContext.LoadRecording(path)`.
- Background `HttpListener` is blessed (mirrors `ConsoleCommandService`); ImGui/Raylib must never be
  touched off-thread; event-history and log reads are safe off-thread; entity extraction must be marshalled.
- No live/remote world-source abstraction; editor + replaybrowser is the bounded universe.

### Design-Review Resolutions (architect, verified against code)

1. **Event→JSON must use the custom DTO machinery, not raw serialization.** Plain
   `System.Text.Json`/`FdpJsonOptionsRegistry` on a raw event object mis-serializes custom-translated types
   (fixed-buffers, `[InlineArray]` blackboards, boxed `List<object>` component lists, `Entity` handles) —
   exactly the failure `EventBrowserPanel.BuildCopyJson` exhibits. The fix is a new public
   `EventSerializationHelper` wrapping `DtoDiagnosticMapper.MapObject` (promote it `internal→public`) plus
   `Entity`→networkId resolution — the same translator-grade path the Entity Inspector uses for components.
   (Correction to an earlier draft that proposed serializing raw events directly: not viable.)
2. **Checkpoint/restore** uses the `IPreviewController` facade, never `PreviewClusterOpHandler` directly.
   This makes it single-slot (= preview enter/exit); keyed multi-checkpoints are deferred.
3. **Live mutation = two complementary paths.** Primary: attribute patching via the authority-aware
   `JsonAttributeCompiler` (the `UpdateEntityAttributeCommand` mechanism), which is **discoverable** via
   `ExportSchema()`/`RegisteredPaths` and applied locally by a (newly-wired) `UpdateEntityAttributeRequestSystem`.
   Escape hatch: arbitrary component-field editing via StructEdit `IComponentEditService` (validated).
   *(Correction: the architect dismissed `UpdateEntityAttributeCommand` as DDS-only and pushed StructEdit
   exclusively — it is in fact locally applicable, authority-aware, and the discoverable primary path.
   StructEdit is the secondary path for fields outside the registered attribute paths.)* Never patch
   `NativeChunkTable` memory directly.
4. **Replay** runs in an isolated `ReplayBrowserContext` (`SandboxRepo`/`SandboxBus`), fully disconnected
   from the live `_world`, bus, and kernel tick; seeks restore into the sandbox only. Query services are
   pointed at the sandbox repo in replay mode.
5. **AI-trace arming** is mandatory before extraction: `AiTracerCoordinator.BeginObservingAsset` →
   `TraceBufferLifecycleSystem` allocates the trace buffers (sets `DebugState.Flags`); blueprints use
   `DebugProbe.Sink`. Confirm/implement the editor's `BeginObservingAssetImpl` override.
