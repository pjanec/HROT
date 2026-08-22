<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-22
current-answer: this whole file — the AI-debug API + MCP server is PORTED, WIRED, and VERIFIED
  end-to-end on headless Linux. Two follow-ups remain (DEBT-MCP-001 tests, DEBT-MCP-002 tracer).
known-conflict: none.
-->
# AI-debug API + MCP server — integration status

Porting `origin/feat/ai-debug-api` (@ `d7b2a6e12`) onto the coordinator branch. It's a **port, not a
merge** — the branch has disjoint history from trunk (see `docs/UX/MCP_PORT_PLAN.md`). This file tracks
what landed and what remains.

## What it is

A loopback HTTP control plane for AI-driven testing/automation:
```
agent ──stdio──▶ tools/ai-debug-mcp (Node, 49 tools) ──HTTP──▶ DebugApiHost (localhost) ──▶ DebugApiService ──▶ the editor
```
Endpoints: `load_scenario` · `spawn_entity` · `play`/`pause`/`step` · `enter_preview` · `checkpoint`/
`restore_checkpoint` · `diff_state` · `focus_entity` · `send_entity_command` · behavior traces · live
mutation / fault injection.

## Landed (builds clean)

| piece | where |
|---|---|
| Production DebugApi | `Hrot/Subsystems/Hrot.Editor/DebugApi/` (`DebugApiService` 2140 ln, `DebugApiHost`, `MainThreadJobQueue`, `EditorAiTracerCoordinator`, `DebugApiSafeFloatConverters`) |
| Shared diagnostics helpers | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/{EventSerializationHelper,JsonShapeDescriber}.cs` |
| Node MCP server | `tools/ai-debug-mcp/` (17 files, outside the .sln — a Node toolchain dependency only) |
| Design corpus | `.dev/ai-debug-api/` (52 docs) |
| `EventSerializationHelperTests` | kept compiled (harness-free) |

### Three shared-file reconciliations (ADA-era additions the trunk lacked)

| drift | fix |
|---|---|
| `OrchestrationConstants.DefaultStagingDirectory` (removed on trunk) | → `ResolveStagingRoot()` in `DebugApiService` |
| `IGeographicTransform.Origin` (getter added by ADA) | added as a **default interface member** (0,0,0), overridden in `WGS84Transform` (degrees) — 12 test doubles untouched |
| `FdpEventBus.GetRegisteredManagedEventTypes()` (method added by ADA) | added; trunk's `ManagedEventStream<T>` already implements `IManagedEventStreamInfo`, so the body ports verbatim |

## Remaining

### ✅ Wiring — DONE

`EditorSubsystem` constructs the `DebugApiHost` + `DebugApiService` after the preview controller, and pumps
its `MainThreadJobQueue` once per frame in `Update`. **Enabled by setting `HROT_DEBUG_API_PORT`** to a port;
off (zero cost) otherwise. Two collaborators were adapted: `EditorApplication.CurrentClusterState` was
exposed (getter added over the existing field); `editorTracer` and `rrController` are omitted (see
DEBT-MCP-002 / no `EcsRecordReplayController` in this root), degrading only the behavior-trace and
record/replay endpoints.

### ✅ End-to-end verification — DONE (2026-08-22)

Ran `HROT_DEBUG_API_PORT=8137 xvfb-run … dotnet Hrot.ClusterRunner.dll --mode editor`; the loopback API came
up in ~3s and answered:
```
GET /status    → clusterState:"Idle", simTime:0, isPaused:true, entityCount:0
GET /scenarios → ["hill-attack","test-fire","test-move"]   (also proves the curated-scenario seeding)
GET /sim/state → isPaused:true, inPreview:false
GET /entities  → []
```

### ✅ Behavior-trace tracer + record/replay — WIRED and VERIFIED (2026-08-22)

- **`DEBT-MCP-002` was a NAME COLLISION, not a duplicate implementation.** Trunk's
  `Hrot.Editor.Debug.EditorAiTracerCoordinator` does *time control* (pause/step); the ported
  `Hrot.Editor.DebugApi.EditorAiTracerCoordinator` does *behavior-trace arming* (`ArmEntity`/`DisarmEntity`,
  self-contained on the world). They are different concepts — **both are kept**; the DebugApi one is now
  constructed and passed as `editorTracer`. *(A rename to disambiguate would be nice-to-have polish.)*
- **`EcsRecordReplayController` already exists on trunk** (`Hrot.SimHost/Modules/Orchestration/`) — it was
  never *instantiated* in the editor root, not missing. Now constructed dedicated to the API
  (`new EcsRecordReplayController(_kernel, EditorNodeId, _world)`) and passed as `rrController`.

**End-to-end verification (headless, `FDP_STAGING_ROOT=/tmp/…`):** `load_scenario` → `recording/start`
(entered preview, began writing) → `sim/play` → `recording/stop` wrote a real **48-frame `.fdp`** →
`replay/load` (loaded, `totalFrames:48`) → `replay/status` (active) → `replay/step` (advanced frames);
`trace/observe` responded (tracer live); `entities` returned a live `M2 Bradley IFV` with its components.
⚠ Recording needs a POSIX `FDP_STAGING_ROOT` on Linux — the default `C:\FDP_Temp` is a Windows path.

### Follow-ups

| item | status |
|---|---|
| **`DEBT-MCP-001` — the 15 harness-dependent integration tests** | ⛔ **DEFERRED, excluded from the build**. They call `EditorHarness.BuildDebugApiService(...)`, needing 9 collaborators trunk's diverged `EditorHarness` lacks (all of which now exist in `EditorSubsystem`, so reviving them is a harness-build mirror of the production wiring). ⭐ **Recommendation:** prefer a small NEW set of end-to-end HTTP smoke tests (drive the real API headless, as above) as the primary gate; selectively revive the original unit tests only for tricky semantics (checkpoint/diff, fault injection). |
| **`DEBT-MCP-003` — rename the ported tracer** | optional: `Hrot.Editor.DebugApi.EditorAiTracerCoordinator` → e.g. `BehaviorTraceCoordinator`, to kill the name collision with the time-control tracer. |

## Notes

- The Node server is deliberately outside `IOS-IG-SimHost.sln`; it adds a Node dependency, no C# build coupling.
- Once wired, the API becomes a second consumer of the editor's internals (co-owned surface).
