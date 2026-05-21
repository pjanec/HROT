# BATCH-11 Completion Report

**Tasks:** TASK-CP-003 — Stage 6: Lower (Dispatch-Aware Transformations)

---

## 1. Files Created or Modified

### Modified — IR operation definitions
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrOperation.cs`  
  Added 9 new operations after `IrOp_CheckCursorVersion` (which already existed):
  - `IrOp_WriteWorkingStatePhase(int PhaseValue)` — AiPrimitive: writes `__phase` byte to working state.
  - `IrOp_ReadWorkingStatePhase` — AiPrimitive: reads `__phase` byte from working state.
  - `IrOp_WriteWorkingStateWaitUntilTime(IrValue Value)` — AiPrimitive: writes delay deadline to working state.
  - `IrOp_ReadWorkingStateWaitUntilTime` — AiPrimitive: reads delay deadline from working state.
  - `IrOp_WriteCursorResumeAt(int ResumeAtValue)` — Instance: writes resume label to cursor.
  - `IrOp_ReadCursorResumeAt` — Instance: reads resume label from cursor.
  - `IrOp_WriteCursorInstanceVersion` — Instance: captures current instance version into cursor.
  - `IrOp_WriteCursorWaitUntilTime(IrValue Seconds)` — Instance: records delay deadline in cursor.
  - `IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType)` — reads a named field from a component ref.

### Modified — Diagnostics
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/DiagnosticCodes.cs`  
  Added named aliases:
  - `BP5001_LibraryHasNoFunctions = "BP5001"` — Library blueprint contains no function graphs.
  - `BP9001_InternalLibraryLatent = "BP9001"` — Library blueprint contains a latent operation (internal invariant failure).

### Modified — Compiler orchestration
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`  
  After Stage 5, now calls `Stage6_Lower.Run(ir, options.Mode, sink)`. After Stage 6, throws `NotImplementedException("Stage 7 not yet implemented (CP-004)")`.

### Created — New lowering support files
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/SynthesizedGuids.cs`  
  Deterministic GUID derivation using SHA256 for synthesized IR fields and blocks.  
  Methods: `PhaseField(Guid assetId)`, `WaitUntilTimeField(Guid assetId)`, `DispatchBlock(Guid graphId)`, `PhaseBlock(Guid graphId, int phase)`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/FieldLayout.cs`  
  `ComputeFieldLayouts(IrAsset asset)` — assigns `Offset` and `Size` to all `IrField` records.  
  Layout base offsets: Parameters at 0, WorkingState at 8 (after StructureHash header), Variables at 16 (after BlueprintLatentCursor). Alignment: `SizeBytes switch { 1=>1, 2=>2, <=4=>4, _=>8 }`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/StructureHashComputation.cs`  
  `Compute(IrAsset asset)` — FNV-1a 64-bit hash over `Dispatch;{fields}` canonical string.  
  Fields appended as `Name|TypeFqn|Offset|Size;` for Parameters, WorkingState, and Variables. Uses `FnvHasher.Hash64`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/DebugProbeInsertion.cs`  
  `Apply(IrAsset asset, CompilerMode mode)` — no-op in Release mode.  
  Debug/Trace: inserts `IrOp_DebugProbe_NodeEnter` at the start of each block whose first statement has a non-null `Debug.NodeId`. Trace mode additionally appends `IrOp_DebugProbe_PinValue` after each value-producing statement with a non-null `Debug.PinId`.

### Modified (implemented) — AiPrimitive lowering stubs
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/AiPrimitiveLowering.cs`  
  Ensures `__phase` byte field (and optional `__waitUntilTime` float field for LatentDelay) are prepended/appended to WorkingState. Delegates per-graph lowering to `WaitLowering_AiPrimitive.Apply`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/WaitLowering_AiPrimitive.cs`  
  Full phase-byte state machine lowering. For N suspend points:
  - Modifies each suspend block: removes wait-op and resume-point const; writes phase number; changes `IrTerm_Suspend` → `IrTerm_ReturnStatus(Running)`.
  - Synthesizes dispatch block: reads `__phase`, branches phase==0 → phase-0 initial block, else chain.
  - Synthesizes per-phase check blocks: for channel/event wait — `IrOp_GetComponentRO` + `IrOp_FieldRead("Status")` + status comparison branches; for delay — time comparison branch.
  - Synthesizes return-running, not-running, and failure blocks per phase.
  - New graph entry = dispatch block.

### Modified (implemented) — Instance lowering stubs
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/InstanceLowering.cs`  
  Delegates per-graph lowering to `WaitLowering_Instance.Apply` for graphs containing latent ops.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/WaitLowering_Instance.cs`  
  Full cursor-based state machine lowering. For N suspend points:
  - Modifies each suspend block: removes wait-op and resume-point const; writes `WriteCursorResumeAt(k+1)`, `WriteCursorInstanceVersion`; changes `IrTerm_Suspend` → `IrTerm_Return(null)`.
  - Synthesizes dispatch block: reads `Cursor.ResumeAt`, branches ResumeAt==0 → initial block, else chain.
  - Synthesizes per-resume check blocks: starts with `IrOp_CheckCursorVersion`; for channel/event — `IrOp_GetComponentRO` + `IrOp_FieldRead("Status")` + status comparison; for delay — time comparison.
  - Synthesizes return-void, not-running, and failure blocks per resume label.
  - New graph entry = dispatch block.

### Modified (implemented) — Library lowering stub
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/LibraryLowering.cs`  
  Scans all blocks for latent ops; emits `BP9001_InternalLibraryLatent` if found. Checks for at least one function graph; emits `BP5001_LibraryHasNoFunctions` if none. Returns asset unchanged.

### Modified (implemented) — Stage 6 orchestrator stub
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage6_Lower.cs`  
  `Run(IrAsset asset, CompilerMode mode, DiagnosticSink sink)`:
  1. Dispatch-specific lowering (AiPrimitive/Instance/Library) — runs first to synthesize fields.
  2. `FieldLayout.ComputeFieldLayouts` — assigns Offset/Size after synthesized fields are present.
  3. `StructureHashComputation.Compute` — finalizes hash over canonical layout.
  4. `DebugProbeInsertion.Apply` — inserts debug probes last (targets final block structure).

### Created — New tests
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Stage6Tests.cs`  
  7 new tests covering SC1-SC7:
  - SC1: `Stage6_AiPrimitive_WaitForChannel_ProducesDispatchBlock` — asserts dispatch block label, `IrOp_ReadWorkingStatePhase`, phase-0 `WriteWorkingStatePhase(1)` + `ReturnStatus(Running)`, channel-check block `GetComponentRO` + `FieldRead("Status")`, `__phase` in WorkingState.
  - SC2: `Stage6_Instance_WaitForChannel_ProducesCursorDispatch` — asserts cursor dispatch block label, `IrOp_ReadCursorResumeAt`, initial block `WriteCursorResumeAt(1)` + `WriteCursorInstanceVersion` + `IrTerm_Return(null)`, resume-check block `CheckCursorVersion` + `GetComponentRO` + `FieldRead("Status")`.
  - SC3: `Stage6_StructureHash_ChangesWhenFieldNameChanges` — field name change → different hash.
  - SC4: `Stage6_StructureHash_ChangesWhenFieldTypeChanges` — field type change → different hash.
  - SC5: `Stage6_StructureHash_StableWhenOnlyGraphBodyChanges` — same fields, different graphs → same hash.
  - SC6: `Stage6_Library_NoFunctionGraphs_EmitsBP5001` — `IrAsset` with Library dispatch and no graphs emits BP5001.
  - SC7: `Stage6_DebugProbe_InsertsNodeEnterInDebugMode` — block with non-null NodeId gets `IrOp_DebugProbe_NodeEnter` prepended in Debug mode; original statement shifts to index 1.

---

## 2. Deviations from Instructions

### Stage6_Lower execution order
The BATCH-11-INSTRUCTIONS Step 7 pseudocode shows `FieldLayout` and `StructureHash` computed
**before** the dispatch-specific lowering switch. The implementation reverses this: dispatch
lowering runs first, then `FieldLayout`, then `StructureHash`.

**Justification:** The AiPrimitive lowering synthesizes new `IrField` entries (`__phase`,
`__waitUntilTime`) into `WorkingState` before `FieldLayout` is called. If `FieldLayout` ran
first, those synthesized fields would receive no `Offset`/`Size` assignments. The design doc
§9.6 comment `// Offset/Size assigned by FieldLayout` implies FieldLayout is a post-synthesis
pass. The functional behavior is correct: `StructureHash` sees the final layout including all
synthesized fields.

### Additional IR ops beyond the explicit list
The instructions listed a subset of new ops. The implementation also added
`IrOp_ReadWorkingStatePhase`, `IrOp_ReadCursorResumeAt`, `IrOp_WriteWorkingStateWaitUntilTime`,
and `IrOp_ReadWorkingStateWaitUntilTime`, which are the natural read-counterparts needed by the
dispatch block logic (e.g., the dispatch block reads `__phase` to branch). Without the read ops,
the dispatch block cannot emit correct IR.

---

## 3. Answers to Output Questions

**Q1: Were the new IR ops added to `IrOperation.cs`? List what was added.**

Yes. 9 new operations were added (all in `IrOperation.cs`):
1. `IrOp_WriteWorkingStatePhase(int PhaseValue)`
2. `IrOp_ReadWorkingStatePhase`
3. `IrOp_WriteWorkingStateWaitUntilTime(IrValue Value)`
4. `IrOp_ReadWorkingStateWaitUntilTime`
5. `IrOp_WriteCursorResumeAt(int ResumeAtValue)`
6. `IrOp_ReadCursorResumeAt`
7. `IrOp_WriteCursorInstanceVersion`
8. `IrOp_WriteCursorWaitUntilTime(IrValue Seconds)`
9. `IrOp_FieldRead(IrValue Source, string FieldName, IrTypeRef ResultType)`

`IrOp_CheckCursorVersion`, `IrOp_DebugProbe_NodeEnter`, and `IrOp_DebugProbe_PinValue` already
existed in the file before BATCH-11.

**Q2: How is the AiPrimitive phase dispatch block structured in the IR?**

The dispatch block uses an `IrTerm_Branch` chain. Specifically:

- The dispatch block reads `IrOp_ReadWorkingStatePhase` → value `phaseV`, then computes
  `IrOp_Const("0", ByteType)` → `constZero`, then `IrOp_PureCall("op_Eq_Byte", [phaseV, constZero])` → `isZero`.
- Its terminator is `IrTerm_Branch(isZero, phase0InitialBlock, elseTarget)`.
- For a single wait (N=1): `elseTarget` is directly the phase-1 check block.
- For N>1 waits: `elseTarget` is a synthesized chain block that reads `__phase` again,
  compares to `k`, and branches to the k-th check block or the next chain block.
  Each chain block follows the same pattern: `IrTerm_Branch(isK, checkBlockK, nextChainOrLastCheck)`.

In all cases, `IrTerm_Branch` is the only branch primitive used. There is no switch terminator in
the IR; multi-wait dispatch is encoded as a linear chain of two-way branches.

**Q3: Were the TASK-DETAIL.md SC1 and SC2 verified by dedicated tests?**

Yes. Both are covered by dedicated `[Fact]` methods in `Stage6Tests.cs`:
- `Stage6_AiPrimitive_WaitForChannel_ProducesDispatchBlock` (SC1)
- `Stage6_Instance_WaitForChannel_ProducesCursorDispatch` (SC2)

Both run the full Stage 5 → Stage 6 pipeline using `BlueprintAssetBuilder` and assert on the
structural properties of the lowered IR (dispatch block labels, op types, terminator types,
and presence of synthesized fields).

---

## 4. Build and Test Results

### Full solution build
```
dotnet build IOS-IG-SimHost.sln
Build succeeded.  0 Error(s)
```

### Test run
```
dotnet test Hrot.Blueprints.Tests.csproj --no-build
Passed!  - Failed: 0, Passed: 175, Skipped: 3, Total: 178
```

Baseline preserved: the original 168 passing tests + 3 skipped are unchanged.  
7 new Stage6 tests all pass (SC1-SC7).
