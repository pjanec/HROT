# ADA-BATCH-14 Report — T06b: Managed-Event Discovery + P9: Focus/Annotations + MCP Tools

**Date:** 2026-06-15
**Branch:** feat/ai-debug-api
**Status:** DONE — all tests green, MCP verify green (203/0), 0-error build

---

## Summary

ADA-BATCH-14 is the final tracked batch of the AI Debug & Test API workstream. It closes
ADA-04-D02 (managed events not discoverable via `/commands`) and adds the two manual-session
assist endpoints (focus camera + debug annotations).

---

## Decisions

### T06b — Managed-event enumeration approach

**Decision: bus-level `GetRegisteredManagedEventTypes()` seam** (not assembly scan).

Rationale:
- `FdpEventBus._managedStreams` (ConcurrentDictionary<int, object>) already holds every registered
  managed stream. Values implement `IManagedEventStreamInfo` with `EventType` returning `typeof(T)`.
- Adding a `GetRegisteredManagedEventTypes()` method on `FdpEventBus` is one line of iteration —
  zero reflection, zero convention scanning, thread-safe (ConcurrentDictionary snapshot).
- Assembly scanning would be brittle (no reliable marker convention) and would return types never
  registered, polluting the output with dead event types.

**Completeness caveat** (documented in `FdpEventBus.GetRegisteredManagedEventTypes()` XML doc,
`ListCommands()`, `DEBT-TRACKER.md`, `README.md`, and this report):
Managed events only appear in `GET /commands` AFTER their stream is created via `RegisterManaged<T>()`
or the first `PublishManaged<T>()` call. Types that exist in the codebase but have never been
published will be absent. On a live editor session, `SpawnEntityCommand` and `MissionControlIntent`
appear because the editor registers them on boot. In a headless session, only types that have been
exercised will appear.

### P9 — Focus endpoint

`POST /entities/{networkId}/focus` publishes `CenterOnEntityCommand` (unmanaged struct, `[EventId(8104)]`)
directly via `_world.Bus.Publish<CenterOnEntityCommand>(cmd)`. This is the correct low-level bus call
(not `PublishManaged`) because `CenterOnEntityCommand` is an unmanaged struct.

### P9 — Annotations endpoint

`POST /annotations` writes to a `DebugPrimitiveBuffer?` field on `DebugApiService`. The buffer is:
- **Production wiring** (`EditorSubsystem.cs`): passed as the existing `_gizmoBuffer` (created during
  editor boot, at offset 1092 in `EditorSubsystem`).
- **Test wiring** (`EditorHarness.BuildDebugApiService`): optional parameter; Tier-1 tests construct
  and pass their own `DebugPrimitiveBuffer`.
- **Null check**: if the buffer is not wired, `AddAnnotation()` returns a clear error (not a crash).

Supported annotation types: `sphere`, `anchor`, `line`. Color is optional hex string.

---

## Changes

### FDP\Engine\Fdp.Core\FdpEventBus.cs
- Added `GetRegisteredManagedEventTypes()`: iterates `_managedStreams.Values`, casts each to
  `IManagedEventStreamInfo`, returns `IReadOnlyList<Type>`. Thread-safe (ConcurrentDictionary snapshot).

### Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiService.cs
- Added `using Fdp.Toolkit.Diagnostics.Gizmos` and `using Hrot.Editor.Commands`.
- Added `_primitiveBuffer` field (Group M).
- Added `primitiveBuffer` optional parameter to constructor.
- `ListCommands()`: now merges unmanaged events (tagged `managed:false`) with managed events from
  `_world.Bus.GetRegisteredManagedEventTypes()` (tagged `managed:true`). Deduplicates by name.
- `SendCommand()`: also searches managed event types if unmanaged lookup fails.
- Added `FocusEntity(long networkId)`: publishes `CenterOnEntityCommand`, returns `{ focused:true }`.
- Added `AddAnnotation(JsonNode? body)`: dispatches to `DrawSphere`, `DrawSpatialAnchor`, or `DrawLine`
  on `_primitiveBuffer`. Returns `{ added, primitiveIndex, bufferCount }` or error.
- Added `ParseColor(string?, Rgba32)` helper.

### Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiHost.cs
- Added Group M routes:
  - `POST /entities/{networkId}/focus` → `RunMain(s => s.FocusEntity(id))`
  - `POST /annotations` → `RunMain(s => s.AddAnnotation(ctx.Body))`

### Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs
- Added `primitiveBuffer: _gizmoBuffer` to the `DebugApiService` constructor call so the production
  wiring provides the real gizmo buffer.

### Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\EditorHarness.cs
- Added optional `primitiveBuffer` parameter to `BuildDebugApiService()`.

### Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\DebugApiBatch14Tests.cs (new file)
- 8 Tier-1 tests: 2 for T06b (managed discovery), 1 for FocusEntity, 4 for AddAnnotation variants,
  1 error case.

### tools\ai-debug-mcp\src\index.mjs
- Added `focus_entity` and `add_annotation` tools (Group M). Total: 49 tools.

### tools\ai-debug-mcp\verify.mjs
- Added Step 14 (ADA-BATCH-14): 4 sub-steps:
  - 14a: list_commands includes `managed:true` entries and `managed:false` entries.
  - 14b: focus_entity → `focused:true`, event history check (best-effort).
  - 14c: add_annotation sphere → `added:true`.
  - 14d: add_annotation line → `added:true`.
- Renumbered old Step 14 (stop_simulation) to Step 15, orphan check to Step 16.

### .dev/ai-debug-api/DEBT-TRACKER.md
- ADA-04-D02: OPEN → **RESOLVED** (ADA-BATCH-14).

### tools/ai-debug-mcp/README.md
- Added `focus_entity` and `add_annotation` to tool table. Updated count to 49. Added notes on
  managed-event completeness caveat, focus headless-verify note, and annotation render manual-verify note.

---

## Build

`dotnet build IOS-IG-SimHost.sln` → **0 errors, 28 warnings** (all pre-existing, none from this batch).

---

## Test Results

`dotnet test --filter "FullyQualifiedName~DebugApi"` →

```
Test Run Successful.
Total tests: 115
     Passed: 115
 Total time: 21 seconds
```

8 new tests in `DebugApiBatch14Tests`. All 115 DebugApi tests pass.

---

## MCP Verify

`cd tools/ai-debug-mcp && npm run verify`

```
=== Summary ===
  Passed: 203
  Failed: 0

VERIFICATION PASSED
```

Up from 193 (Batch 13) to 203. Exit code 0. No orphan processes.

### Step 14 results (new steps)

14a: `list_commands` includes `managed:true` entries (SpawnEntityCommand visible) AND `managed:false` entries. ✓
14b: `focus_entity {1000}` → `focused:true`. ✓ CenterOnEntityCommand in event history: not present in headless (sim not advancing — expected).
14c: `add_annotation {sphere}` → `added:true`, `bufferCount:675`. ✓
14d: `add_annotation {line}` → `added:true`. ✓

---

## Manual-Verify Required (cannot verify headless)

The following require a windowed editor session and CANNOT be verified in headless mode:

1. **Camera centering** (P9 SC#1): After `POST /entities/1000/focus`, the map canvas should pan and
   zoom to entity 1000. Headless confirms the `CenterOnEntityCommand` publish; the camera move happens
   in the render loop (Raylib canvas). Not automatable.

2. **Gizmo marker visible on map** (P9 SC#2): After `POST /annotations {type:"sphere",...}`, a yellow
   sphere should appear on the map at the specified world coordinates. Headless confirms the buffer
   write (`bufferCount` increases); the gizmo render requires `DataDrivenGizmoSystem` in a windowed
   session. Not automatable.

---

## Debt

ADA-04-D02 is **CLOSED** by this batch.

All remaining open items in DEBT-TRACKER.md are P3 carryover from earlier batches. No new debt
introduced by ADA-BATCH-14.
