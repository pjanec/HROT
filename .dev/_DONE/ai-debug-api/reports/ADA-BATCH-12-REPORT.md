# ADA-BATCH-12 Report

**Date:** 2026-06-15
**Branch:** main
**Status:** COMPLETE — all gates passed

---

## Deliverables

### ADA-P6-T01 — Live Trace Arming Seam (Group K — arm)
### ADA-P6-T02 — Trace Extraction Endpoints (Group K — extract)

**Files created:**
- `Hrot/Subsystems/Hrot.Editor/DebugApi/EditorAiTracerCoordinator.cs` — new subclass; arms/disarms per-entity trace buffers by publishing `PatchDebugStateCommand` to the world bus
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiBatch12Tests.cs` — 5 new Tier-1 integration tests

**Files modified:**
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — instantiates `EditorAiTracerCoordinator` (replaces `new AiTracerCoordinator()`); wires `editorTracer`, `btreeSession`, `hsmSession` into `DebugApiService`
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` — Group K fields (4), 4 new optional constructor params, `ObserveTrace()` and `GetEntityTrace()` methods
- `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` — routes `POST /trace/observe` and `GET /entities/{networkId}/trace`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — `BehaviorDiagnosticsModule` registered; `EditorTracer`/`BTreeSession`/`HsmSession` public properties and init wiring; `BuildDebugApiService()` extended with new params
- `tools/ai-debug-mcp/src/index.mjs` — `observe_trace` and `get_entity_trace` tools added (44 tools total)
- `tools/ai-debug-mcp/verify.mjs` — both tools in `requiredTools`, Step 13 exercises full arm→step→trace→disarm flow

---

## Design Decisions / Deviations

### Arming is entity-centric, not assetId-centric

The `AiTracerCoordinator.BeginObservingAssetImpl` takes a `Guid assetId`, but `BehaviorState.ActiveBehaviorHash` is an `int` — no Guid→int mapping exists at runtime. The Debug API `POST /trace/observe` takes a `networkId` which maps directly to an `Entity`. Therefore:

- `EditorAiTracerCoordinator` exposes `ArmEntity(Entity)` / `DisarmEntity(Entity)` methods called directly from `ObserveTrace(networkId, on)` after resolving the entity from `NetworkEntityMap`.
- The base `BeginObservingAssetImpl` / `EndObservingAssetImpl` remain no-ops; arming goes through the entity-centric path.
- This is simpler and correct for the Debug API use case. Asset-centric arming (arm all entities running a given asset) is deferred as DEBT.

### BehaviorDiagnosticsModule added to EditorHarness

`TraceBufferLifecycleSystem` and `DebugStatePatchSystem` were not previously registered in the test harness (they were only in production via `CgfLogicPack`'s inner modules). Added `Kernel.RegisterModule(new BehaviorDiagnosticsModule())` to `EditorHarness` constructor — this is the correct standalone module for these systems.

### GetEntityTrace calls Update() on-demand

`BTreeDebugSession.Update(repo, entity)` is called on-demand in `GetEntityTrace()` rather than each frame. This is correct for the Debug API: the endpoint is a pull, not a push, and calling Update on-demand reads the latest trace state from the ECS. The frame loop in the UI panel separately calls Update for the debug panel display — these two calls are independent (ring buffer is read-position tracked per session).

### Blueprint trace limited

`BlueprintDebugSession.CaptureLiveState` requires a `Guid assetId`. For the API, this is resolved from the `BlueprintBound` managed component on the entity. If the entity lacks this component (e.g., non-blueprint entity), `hasSnapshot=false` is returned. Blueprint arming is not needed (uses `DebugProbe.Sink`, not trace buffers). Logged as DEBT-BF-K01.

---

## The Buffer Absent→Present Proof (crux)

From `DebugApiBatch12Tests.ObserveTrace_ArmsEntity_AllocatesTraceBuffer`:

1. Entity spawned with `BrainTier=BrainTierBTree`.
2. **BEFORE arming**: `HasComponent<BTreeTraceWorkingMemory1024>(entity)` → **false**.
3. `ObserveTrace(networkId, on: true)` → publishes `PatchDebugStateCommand{Behavior:{EnableTraceBuffer:true}}`.
4. `PumpFrames(3)` → `DebugStatePatchSystem` (Input phase) sets the flag; `TraceBufferLifecycleSystem` (BeforeSync) adds the component.
5. **AFTER arming**: `HasComponent<BTreeTraceWorkingMemory1024>(entity)` → **true**. ✓

This proves the seam works: the base `AiTracerCoordinator` no-op is replaced by a concrete implementation that drives buffer allocation through the ECS pipeline.

From the live verify.mjs Step 13c (entity 1000 = M2 Bradley, test-move scenario):
- `tier=BTree`, `traceArmed=true`, `activeNode="20000000-0000-0000-0000-000000000001"`, `nodeHistory` populated with entries.
- This confirms the end-to-end flow: arm → ticks → trace data extracted.

---

## Build Results

`dotnet build IOS-IG-SimHost.sln` → **0 errors**, 2 pre-existing NU1903 warnings (MessagePack CVE, unrelated).

---

## Test Results

`dotnet test --filter "FullyQualifiedName~DebugApi"` → **97/97 passed** (5 new Batch-12 tests).

New tests:
- `ObserveTrace_ArmsEntity_AllocatesTraceBuffer` (crux) ✓
- `ObserveTrace_DisarmsEntity_RemovesTraceBuffer` ✓
- `GetEntityTrace_NoBehaviorState_ReturnsTierNone` ✓
- `GetEntityTrace_UnknownEntity_ReturnsError` ✓
- `ObserveTrace_UnknownEntity_ReturnsError` ✓

---

## verify.mjs Results

`cd tools/ai-debug-mcp && npm run verify` → **178/178 assertions — VERIFICATION PASSED**

Step 13 live output (entity 1000):
- `13a`: `observe_trace{networkId:1000, on:true}` → `{armed:true, networkId:1000}` ✓
- `13b`: `step{count:5}` ✓
- `13c`: `get_entity_trace{networkId:1000}` → `{tier:"BTree", traceArmed:true, activeNode:"20000000-0000-0000-0000-000000000001", nodeHistory:[...]}` ✓
- `13d`: `observe_trace{networkId:1000, on:false}` → `{armed:false, networkId:1000}` ✓

---

## Debt Log

| ID | Description |
|----|-------------|
| DEBT-BF-K01 | Blueprint trace: `CaptureLiveState` requires `BlueprintBound` component on entity. Entities without it return `hasSnapshot=false`. Full blueprint trace (with `DebugProbe.Sink` registration) deferred. |
| DEBT-BF-K02 | Asset-centric arming (`observe_trace{assetId}` — arm ALL entities running a given behavior asset) not implemented. Requires Guid→int hash mapping which does not exist at runtime. Deferred. |
| DEBT-BF-K03 | HSM entities not covered in Tier-1 tests (no HSM entity in frozen TestAssets fixture). HSM code path exists in `GetEntityTrace` but not test-verified at Tier-1. Covered at Tier-2 (live) if HSM entity is available in a loaded scenario. |

**ADA-06-D01 update:** Group K tools (`observe_trace`, `get_entity_trace`) are now implemented and verified. ADA-06-D01 may be closed for Group K.
