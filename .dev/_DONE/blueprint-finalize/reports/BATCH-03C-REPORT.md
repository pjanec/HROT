# BATCH-03C Report

## Implementation Summary

Completed the editor side of in-blueprint function-graph calls by projecting canonical pins that
bind to exactly what the BATCH-03A compiler consumes (Stage5_Schedule.cs).

### Task 1 — Thread `containingGraph` into projection

**File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`**

Added optional trailing parameter `Graph? containingGraph = null` to `GetCanonicalPins` (line 60).

**Call-site updates (verified these are the only 3 call sites):**

| File | Line | Change |
|---|---|---|
| `BlueprintGraphModel.cs` | 145 | Added `_graph` as `containingGraph` arg |
| `BlueprintCommandSink.cs` | 209 | Added `_graph` as named `containingGraph:` arg |
| `BlueprintNodeCatalog.cs` | 186 | Left as-is (no graph available in catalog context; null → graceful fallback) |

### Task 2 — EventEntryNode arm

Replaced `EventEntryNode => ExecOnly("Out")` with a call to new helper `EventEntryNodePins(containingGraph)`.

**New helper logic:**
- If `containingGraph?.Kind == GraphKind.Function` and `containingGraph.Inputs.Count > 0`:
  emit `MakeExec("Out","Out")` + one `MakeData(inp.Name, "Out", inp.Type?.TypeId ?? "System.Object")` per input.
- Else: `ExecOnly("Out")` (unchanged for Event/AiPrimitive/inputless Function graphs).

**Compiler contract satisfied (Stage5_Schedule.cs ~1157-1189):**
Stage5 reads `!IsExec && Direction=="Out"` pins on `EventEntryNode` and name-matches each to
`Graph.Inputs` (OrdinalIgnoreCase) to emit `IrOp_ReadInputArg(argIndex)`. Projected pins have
`Direction="Out"` and `Name = inp.Name`.

### Task 3 — ReturnNode arm

Replaced `ReturnNode => ExecOnly("In")` with a call to new helper `ReturnNodePins(containingGraph)`.

**New helper logic:**
- If `containingGraph?.Kind == GraphKind.Function` and `containingGraph.Outputs.Count > 0`:
  emit `MakeExec("In","In")` + `MakeData(Outputs[0].Name, "Out", Outputs[0].Type?.TypeId ?? "System.Object")`.
- Else: `ExecOnly("In")`.
- Only first output projected (BATCH-03A is single-output; multi-output deferred).

**Why `Direction="Out"` for the value pin (documented in XML-doc):**
Stage5 `BuildReturnTerminator` (~881-897) reads
`rn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out")` as the return value.
The pin is a *producer* — the node provides the return value on that pin (mirrors GetVariable
convention). Direction="Out" is therefore both compiler-required and semantically correct.

**Compiler contract satisfied (Stage5_Schedule.cs ~881-897):**
`outPin = rn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out")` — the pin has
`Direction="Out"`, `Name = Outputs[0].Name`.

### Task 4 — FunctionCall(TargetGraphId) arm

Added dispatch helper `FunctionCallPinsDispatch(fc, asset, containingGraph)` replacing the direct
`FunctionCallNode fc => FunctionCallPins(fc)` routing in the switch.

**New dispatch logic:**
- If `fc.TargetGraphId` is non-empty, `asset != null`, `Guid.TryParse` succeeds, and
  `asset.Graphs.FirstOrDefault(g => g.Id == guid && g.Kind == GraphKind.Function)` finds a target
  → call new `FunctionGraphCallPins(fc, target)`.
- Otherwise (empty TargetGraphId / unparseable GUID / target not found / not a Function graph)
  → fall through to existing `FunctionCallPins(fc)` (CLR-reflection path, no throw).

**`FunctionGraphCallPins` pin shape:**
- If `!fc.IsPure`: `MakeExec("In","In")`, `MakeExec("Out","Out")`.
- One `MakeData(inp.Name, "In", inp.Type?.TypeId ?? "System.Object")` per `target.Inputs` (declaration order).
- If `target.Outputs.Count > 0`: `MakeData(target.Outputs[0].Name, "Out", target.Outputs[0].Type?.TypeId ?? "System.Object")`.

**Compiler contract satisfied (Stage5_Schedule.cs ~642-679):**
`ResolveAllDataInputs(node, stmts)` consumes all `!IsExec && Direction=="In"` data-IN pins
positionally as call arguments. `gcOutPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out")`
is the return slot.

**CLR path kept intact:** the existing `FunctionCallPins(fc)` is untouched and still used for all
standard CLR-method `FunctionCallNode`s (empty `TargetGraphId`).

## Design Decisions

1. **Named parameter for `containingGraph`** in `BlueprintCommandSink.cs` call site:
   `GetCanonicalPins(node, _catalog.KindRegistry, _asset, containingGraph: _graph)` uses a named
   arg to skip the `channelCommands` parameter. This is cleaner than passing `null` positionally.

2. **Separate dispatch helper vs. inline guard in switch:** Using `FunctionCallPinsDispatch` keeps
   the switch arms readable and makes it trivial to add a pure-TargetGraphId arm later.

3. **`containingGraph` is not used for `FunctionGraphCallPins` target resolution** — the `asset`
   parameter supplies the graph registry. `containingGraph` is threaded to the Entry/Return arms as
   a direct reference. This matches how the compiler resolves things.

## Deviations

None. All changes are within the spec.

**CallPeerBlueprint arg-pin work explicitly left for a later batch** — confirmed the existing
`CallPeerBlueprintPins()` helper and its TODO comment were not modified.

## Test Results

### New tests added to `NodePinSchemaEnrichmentTests.cs`

| Test | Result |
|---|---|
| `EventEntryNode_FunctionGraph_TwoInputs_ProjectsExecOutPlusTwoDataOut` | PASS |
| `EventEntryNode_EventGraph_ExecOnly` | PASS |
| `EventEntryNode_FunctionGraph_NoInputs_ExecOnly` | PASS |
| `EventEntryNode_NullGraph_ExecOnly` | PASS |
| `ReturnNode_FunctionGraph_OneOutput_ProjectsExecInPlusDataOut` | PASS |
| `ReturnNode_FunctionGraph_NoOutputs_ExecOnly` | PASS |
| `ReturnNode_NullGraph_ExecOnly` | PASS |
| `FunctionCall_TargetGraphId_ImpureFunction_ProjectsExecAndDataPins` | PASS |
| `FunctionCall_TargetGraphId_PureFunction_NoExecPins` | PASS |
| `FunctionCall_TargetGraphId_UnknownGuid_FallsBackToCLRPath_NoThrow` | PASS |
| `FunctionCall_TargetGraphId_EventGraph_NotFunction_FallsBackToCLRPath` | PASS |
| `CompilerSelectors_EntryReturnFunctionCall_AllProjectedPinsSatisfySelectors` | PASS |

**NodePinSchemaEnrichmentTests suite:** 19/19 passed (8 pre-existing + 11 new).

### Full `Hrot.Blueprints.Tests` run

```
Failed!  - Failed: 7, Passed: 1182, Skipped: 8, Total: 1197, Duration: ~28 s
```

**7 failures are the pre-existing baseline (confirmed subset, zero new failures):**

| Test | Classification |
|---|---|
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` | Pre-existing golden snapshot mismatch |
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` | Pre-existing golden snapshot mismatch |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Pre-existing golden snapshot mismatch |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Pre-existing snapshot mismatch |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing snapshot mismatch |
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Pre-existing failure |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing allocation regression |

No new failures. No goldens changed.

### Integration test

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 1 s
```

10/10 pass.

### Build

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
0 Error(s)
```

No new warnings in touched projects (`Hrot.Blueprints.Editor`, `Hrot.Blueprints.Tests`). Pre-existing
warnings in other projects (xUnit2013, CS0618, CS8601, MSB3026) unchanged.

## Call-site Updates (exact file:line)

| File | Line before | Change |
|---|---|---|
| `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | 59 | Added `Graph? containingGraph = null` parameter |
| `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | ~100 (switch) | `EventEntryNode => EventEntryNodePins(containingGraph)` |
| `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | ~101 (switch) | `ReturnNode => ReturnNodePins(containingGraph)` |
| `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | ~104 (switch) | `FunctionCallNode fc => FunctionCallPinsDispatch(fc, asset, containingGraph)` |
| `Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs` | 145 | Added `, _graph` to `GetCanonicalPins` call |
| `Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs` | 209 | Added `, containingGraph: _graph` to `GetCanonicalPins` call |

## Pin Shapes Per Arm

### EventEntryNode in Function graph (Graph.Inputs = [{Name="A",TypeId="System.Int32"}, {Name="B",TypeId="System.Single"}])
```
IsExec=true  Name="Out" Direction="Out"            ← exec-Out
IsExec=false Name="A"   Direction="Out" TypeId=System.Int32   ← data-Out
IsExec=false Name="B"   Direction="Out" TypeId=System.Single  ← data-Out
```
Compiler selector satisfied: `!IsExec && Direction=="Out"` (Stage5~1162).

### ReturnNode in Function graph (Graph.Outputs = [{Name="Result",TypeId="System.Int32"}])
```
IsExec=true  Name="In"     Direction="In"           ← exec-In
IsExec=false Name="Result" Direction="Out" TypeId=System.Int32  ← data-Out (NOT "In")
```
Compiler selector satisfied: `!IsExec && Direction=="Out"` (Stage5~891 BuildReturnTerminator).

### FunctionCallNode (TargetGraphId, Impure, Inputs=[{X,Int32}], Outputs=[{Score,Single}])
```
IsExec=true  Name="In"    Direction="In"             ← exec-In
IsExec=true  Name="Out"   Direction="Out"            ← exec-Out
IsExec=false Name="X"     Direction="In"  TypeId=System.Int32   ← data-In arg
IsExec=false Name="Score" Direction="Out" TypeId=System.Single  ← data-Out return slot
```
Compiler selectors satisfied:
- `ResolveAllDataInputs`: all `!IsExec && Direction=="In"` consumed positionally (Stage5~661).
- `gcOutPin`: first `!IsExec && Direction=="Out"` as return slot (Stage5~662).

## Known Issues

None. All spec items implemented and verified.

## Confirmation: CallPeerBlueprint Left for Later

The `CallPeerBlueprintPins()` helper (NodePinSchema.cs) and its `TODO(BATCH-03)` comment were not
modified. Arg-pin projection for `CallPeerBlueprintNode` remains deferred to a separate batch per
the spec.

## Suggested Commit Message

```
feat(blueprint-editor): project canonical Entry/Return/FunctionCall pins for Function graphs (BATCH-03C)
```
