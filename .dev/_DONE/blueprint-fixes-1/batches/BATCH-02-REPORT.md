# BATCH-02 Report

## Status: COMPLETE

All tasks implemented and all tests passing.

---

## Baseline

- Tests before batch: 823 passing, 8 skipped
- Tests after batch: 852 passing, 8 skipped (+29 new tests)

---

## Task Summary

### BPF-002 + BPF-021: Extend Compiler Debug Map (Task 1)

**Files changed:**
- `Hrot.Blueprints.Compiler/Compiler/Emit/DebugMapBuilder.cs` — Added new record types `DebugGraphInfo`, `DebugPinInfo`, `StateLayoutField`, `DebugStateLayout`; extended `DebugMap` with `AssetName`, `GeneratedSourcePath`, `Graphs`, `Pins`, `StateLayout`; extended `DebugMapBuilder` with builder methods for all new fields.
- `Hrot.Blueprints.Compiler/Compiler/Emit/DebugMapSerializer.cs` — Added DTO types for new fields (`GraphDto`, `PinDto`, `StateLayoutDto`, `StateLayoutFieldDto`); updated `Serialize`/`Deserialize` to round-trip all new fields.
- `Hrot.Blueprints.Core/DebugMapIndex.cs` — Fixed `AssetName` to use `map.AssetName` (no longer falls back to Guid string when name is non-empty); added `GeneratedSourcePath` and `StateLayout` properties; added `_pinsByGuid` and `_graphsByGuid` dicts; added `TryGetPinById`, `TryGetGraphById`, `AllPins`, `AllGraphs` members.

**Tests added:** `DebugMapExtensionTests` (17 test methods covering AssetName round-trip, graph/pin/stateLayout serialization, and DebugMapIndex lookup methods).

---

### BPF-001: GetCurrentStateSnapshot (Task 2)

**Files changed:**
- `Hrot.Blueprints.Core/IBlueprintDebugSession.cs` — Expanded `BlueprintStateSnapshot` from `(Entity, Guid)` to `(Entity Self, Guid AssetId, string AssetName, BlueprintDispatchKind Dispatch, IReadOnlyDictionary<string, object> FieldValues, BlueprintLatentCursor? Cursor)`; added `using Fdp.Toolkit.Blueprints`.
- `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — Implemented `GetCurrentStateSnapshot()` → `CaptureStateSnapshot()` → `CaptureAiPrimitiveState()` that reads `Blackboard1024` fields using `MemoryMarshal.AsBytes` + `StateLayout` fields or `BlueprintDefinition.StateFields`; added `ResolveType()` helper.

**Tests added:** `StateSnapshotTests` (4 test methods checking AssetName from debug map, Library dispatch kind, null when not paused, fallback to Guid string).

---

### BPF-003: Breakpoint Hash Safety + Per-Frame Dedup (Task 3)

**Files changed:**
- `Hrot.Blueprints.Core/IBlueprintDebugSession.cs` — Added `AssetStructureHashAtSetTime { get; init; }` and `IsStale { get; init; }` to `Breakpoint`; added `void OnNewTick()` to interface.
- `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — Added `_firedBreakpointsThisTick` HashSet; `SetBreakpoint` captures current structure hash; `RegisterDebugMap` marks stale instead of clearing; `OnNodeEnter` checks `IsStale` and hash before pausing, uses per-frame dedup; `Continue()` clears dedup set; `OnNewTick()` also clears dedup set.
- `Hrot.Blueprints.Tests/Debug/BreakpointTests.cs` — Updated `StructureHashMismatch_ClearsBreakpoints` to assert stale-not-cleared behavior.
- `Hrot.Blueprints.Tests/Debug/HotReloadInteractionTests.cs` — Updated `RegisterDebugMap_NewHash_ClearsBreakpointsForThatAsset` to assert stale-not-cleared behavior.

**Tests added:** `BreakpointHashSafetyTests` (7 test methods covering hash capture, zero hash fallback, stale-not-clear on hash change, stale BP doesn't pause, per-frame dedup, OnBreakpointListChanged fired on hash change).

---

### BPF-004: Peer-Call Probe Signature (Task 4)

**Files changed:**
- `Hrot.Blueprints.Core/IBlueprintProbeSink.cs` — Changed `OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)` to `OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName)` and `OnPeerCallExit(Entity entity)` to `OnPeerCallExit(Entity self, string peerAssetIdString, string methodName)`.
- `Hrot.Blueprints.Core/DebugProbe.cs` — Updated static helpers and `NullProbeSink` to match.
- `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — `OnPeerCallEnter` now parses `Guid.TryParse(peerAssetIdString)` for accurate asset keying (falls back to `Guid.Empty`).
- `Hrot.Blueprints.Tests/CapturingDebugSession.cs` — Updated signatures.
- `Hrot.Blueprints.Tests/Editor/MockDebugSession.cs` — Updated signatures.
- `Hrot.Blueprints.Tests/Debug/StepTests.cs` — Updated `OnPeerCallExit` call to 3-arg signature.

**Tests added:** `PeerCallProbeTests` (3 test methods checking active-entity tracking with valid Guid string, removal on exit, and Guid.Empty fallback for invalid string).

---

### BPF-005: StepOut Tick Boundary + Entity Death (Task 5)

**Files changed:**
- `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — Added `_stepFromTick` field; all step methods capture `_view.Tick`; `StepOut` at depth 0 re-pauses when `_view.Tick > _stepFromTick`; `OnNodeEnter` checks `_view.IsAlive(_stepFromEntity)` and abandons step when entity is dead.
- `Hrot.Blueprints.Tests/Debug/StepTests.cs` — Changed `StubSimulationView.IsAlive` from `throw NotImplementedException()` to `return true` (required for entity-death check compatibility).

**Tests added:** `StepOutEdgeCaseTests` (2 test methods checking StepOut at depth 0 re-pauses on next tick, and entity-death abandonment).

---

## Design Decisions

- **BPF-003 stale vs. clear**: Breakpoints are now marked `IsStale = true` instead of cleared when the asset structure hash changes. This preserves the user's breakpoint list and allows them to re-enable after re-registering a matching map. Two pre-existing tests that asserted the old clear behavior were updated.
- **Per-frame dedup on Continue()**: `Continue()` clears `_firedBreakpointsThisTick` to allow hit-count accumulation across resumed sessions (as tested by `Breakpoint_HitCount_IncreasesOnEachHit`). `OnNewTick()` also clears it at simulation tick boundaries.
- **AiPrimitive state reading**: Uses `MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in bb, 1))` for safe zero-copy byte view of `Blackboard1024`. Verifies structure hash before reading fields.

## Test Count

| Phase | Passing | Skipped |
|---|---|---|
| Baseline | 823 | 8 |
| After BATCH-02 | 852 | 8 |
| Delta | +29 | 0 |
