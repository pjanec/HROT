# BATCH-02 Report

**Batch:** BATCH-02  
**Tasks:** FIX2-002, FIX2-009  
**Status:** APPROVED -- all tests green

---

## Test Results

```
Passed!  - Failed: 0, Passed: 882, Skipped: 8, Total: 890, Duration: 40 s
```

- Pre-existing count before this batch: 880
- New tests added: 2 (one per task)
- Final pass count: 882
- Regressions: 0

The 2 failures seen in an earlier run of the suite were timing-sensitive perf tests
(`WhenNode_EqsResult_Under150ns_perTick`) that flicker under load; they passed cleanly
in the decisive run above.

---

## Task FIX2-002 -- Populate DebugMap fields during emit

### Files changed

| File | Change |
|---|---|
| `Hrot.Blueprints.Compiler/.../IrDebugAnnotation.cs` | Added `NodeKind?` and `DisplayName?` properties |
| `Hrot.Blueprints.Compiler/.../DebugProbeInsertion.cs` | Propagates `NodeKind` onto the probe's `Debug` annotation |
| `Hrot.Blueprints.Compiler/.../DebugMapBuilder.cs` | Updated `_openNodes` dict to carry `NodeKind`+`DisplayName`; updated `RecordNodeStart`/`RecordNodeEnd` accordingly |
| `Hrot.Blueprints.Compiler/.../CSharpEmitter.cs` | `Emit()` now calls `SetAssetName`, `SetGeneratedSourcePath`, `AddGraph`, `AddPin`, `AddStateLayoutField`; `EmitNodeStart` passes `NodeKind`/`DisplayName` to `RecordNodeStart` |

### Test

`Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/FIX2_002_DebugMapEmitTests.cs`
-- test `DebugMap_CompiledAsset_HasNonEmptyPinsAndGraphs`.

Goes through the production `BlueprintCompiler.Compile` path; asserts
`AssetName`, `GeneratedSourcePath`, `Graphs`, `Pins`, `StateLayout.Fields`
(name + size + offset) and `NodeKind` on at least one `DebugMapEntry`.

---

## Task FIX2-009 -- Implement CaptureInstanceStateFromDefinition

### Files changed

| File | Change |
|---|---|
| `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | Full implementation of `CaptureInstanceStateFromDefinition`; added `ReadInstanceState` helper |

### Test

`Hrot.Blueprints.Tests/Debug/FIX2_009_InstanceStateInspectionTests.cs`
-- test `StateInspection_Instance_ReturnsNonEmptyFields`.

Compiles a blueprint with a `Health: float` variable through the production
compiler, builds a real `BlueprintBlackboard1024` struct via
`BlueprintBlackboardPartitions.Initialize` + `TryAttach`, writes the
expected float value, sets a breakpoint, fires `OnNodeEnter` to pause the
session, then asserts `GetCurrentStateSnapshot().FieldValues["Health"] == 42.5f`.

---

## Developer Insights

### 1. Obstacles populating StateLayout.Fields

The only data needed -- `IrField.Offset`, `IrField.Size`, `IrField.Name`,
`IrTypeRef.FullName` -- is fully available after Stage 6 (FieldLayout),
which runs before the emitter (Stage 7). No additional threading was
required; `asset.Variables` is already the post-layout list available at
emit time.

The tricky part was the **type string format**. `DebugMapBuilder` stores
`StateLayoutField.Type` as a string, and `BlueprintDebugSession.ResolveType`
expects CLR full names (`"System.Single"`, `"System.Int32"`, etc.).
`StatementEmitter.TypeRefToCSharp` maps those same names to C# keywords
(`"float"`, `"int"`). Using `TypeRefToCSharp` for the `StateLayoutField`
type caused `ResolveType` to fall through to `Type.GetType("float")` which
returns `null`, silently dropping every field.

**Fix:** use `field.Type.FullName` (the CLR name) directly for
`StateLayoutField.Type`, not `TypeRefToCSharp`.

### 2. Design decisions for wiring the partition allocator

`CaptureInstanceStateFromDefinition` already receives `ISimulationView _view`
(a field of `BlueprintDebugSession`). The partition allocator is not a
separate object -- `BlueprintBlackboardPartitions` is a static utility that
operates on a raw `byte*` obtained from the component bytes.

The approach:
1. Call `_view.HasComponent<BlueprintBlackboardNNN>` to find which tier the
   entity is on.
2. Call `_view.GetComponentRO<BlueprintBlackboardNNN>` to get a
   `ref readonly` to the component.
3. `MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in bb, 1))` yields
   a `ReadOnlySpan<byte>` over the component's memory.
4. Pin it with `fixed` and call `BlueprintBlackboardPartitions.TryGetSlotOffset`.

`BlueprintDefinition` does **not** have a `BlueprintId` property; the `bpId`
is already computed earlier in `CaptureStateSnapshot` via
`BlueprintIdHash.Compute(assetId)` and is passed directly as a parameter.
This avoids re-computing it and keeps `CaptureInstanceStateFromDefinition`
independent of the `BlueprintDefinition` record.

### 3. Dead-code gaps found

All five builder methods (`SetAssetName`, `SetGeneratedSourcePath`, `AddGraph`,
`AddPin`, `AddStateLayoutField`) had zero callers before this batch.
`DebugMapBuilder` also has a `Record(nodeId, graphId, startLine, endLine)`
method that is also uncalled (its role is superseded by the
`RecordNodeStart`/`RecordNodeEnd` pair).

`IrDebugAnnotation` had `Synthesized`, `NodeId`, `PinId`, `GraphId` but
no `NodeKind` or `DisplayName`, so `DebugMapEntry.NodeKind` was always
empty even though the field existed in the record.

### 4. Edge cases discovered

- **Blueprint with no state variables (Library/AiPrimitive):** the
  `if (asset.Dispatch == AssetDispatch.Instance)` guard means `AddStateLayoutField`
  is never called; `StateLayout.Fields` stays empty, and `ReadInstanceState`
  exits immediately after the null-check. Safe.

- **Blueprint with pins of unknown CLR type:** `ResolveType` falls through to
  `Type.GetType(typeFullName)`, which returns `null` for types whose assemblies
  are not loaded in the debug session process. The field is silently skipped.
  This is by design (the existing `if (fieldType == null) continue` guard).

- **`TryAttach` with `structureHash = 0`:** permitted by the API; the slot is
  allocated with hash 0 and `TryGetSlotOffset` finds it by `blueprintId`
  regardless of hash. The hash is only used for stale-breakpoint detection.

- **Type string mismatch (`"float"` vs `"System.Single"`):** described in
  section 1 above; fixed by using `field.Type.FullName` for `StateLayoutField`.

---

## Suggested commit message

```
fix: populate DebugMap metadata and implement instance state inspection

FIX2-002: CSharpEmitter.Emit() now calls SetAssetName, SetGeneratedSourcePath,
AddGraph, AddPin, and AddStateLayoutField at emit time. IrDebugAnnotation gains
NodeKind/DisplayName; DebugProbeInsertion propagates NodeKind onto probe
annotations; DebugMapBuilder.RecordNodeEnd stores them on DebugMapEntry.
StateLayoutField.Type uses IrField.Type.FullName (CLR names) so ResolveType
in the debug session can match them correctly.

FIX2-009: CaptureInstanceStateFromDefinition reads BlueprintBlackboard bytes
via ISimulationView.GetComponentRO, pins them with `fixed`, calls
BlueprintBlackboardPartitions.TryGetSlotOffset to find the slot, then projects
each StateLayout field into the FieldValues dict using MarshalFromBytes.

Tests: +2 (FIX2_002_DebugMapEmitTests, FIX2_009_InstanceStateInspectionTests)
Suite: 882 passed, 0 failed, 8 skipped (was 880 passing)
```
