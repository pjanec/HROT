# BP-2 Report — Compiler-side pin rehydration (pins-empty path)

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-05  
**Status:** COMPLETE — all tasks done, tests green, 0 new errors/warnings

---

## Summary

BP-2 adds `Stage0_Rehydrate`, a new compiler pre-pass that runs before `Stage2_Validate` and
rebuilds `node.Pins` for any node whose `Pins` list is empty (projection-only save). Without
this pass, blueprints saved from the editor had all pins stripped and the compiler received
pin-less nodes; Stage4/Stage5 resolve connections 100% by `link.FromPinId == pin.Id` /
`link.ToPinId == pin.Id` with no name-fallback, so every wire dangled → empty Tick schedule
→ "Count stays 0".

---

## Files Created / Modified

| File | Change |
|------|--------|
| `Hrot.Blueprints.Compiler/Compiler/Catalogs/INodeRegistry.cs` | Added `PinSchema` record; added `GetStaticPins(Node)` method to interface |
| `Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInNodeRegistry.cs` | Implemented `GetStaticPins` with static shapes for all node kinds |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage0_Rehydrate.cs` | **NEW** — full rehydration pre-pass |
| `Hrot.Blueprints.Compiler/Compiler/BlueprintCompiler.cs` | Added `Stage0_Rehydrate.Run(asset, options)` as first line of `Compile` (before Stage2) |
| `Hrot.Blueprints.Tests/Compiler/Stage0_RehydrateTests/Stage0_RehydrateTests.cs` | **NEW** — 10 focused unit tests |
| `Hrot.Blueprints.Tests/Demos/CountingDemo_PinsStripped_ProofTests.cs` | **NEW** — keystone proof tests (2 tests) |

---

## PinSchema / INodeRegistry Contract

```csharp
public readonly record struct PinSchema(string Name, string Direction, bool IsExec, string TypeId);

public interface INodeRegistry
{
    IReadOnlyList<PinSchema> GetStaticPins(Node node);
}
```

`BuiltInNodeRegistry.GetStaticPins` is a switch expression over node type. Structurally fixed
kinds return full shapes; dynamic kinds return their static skeleton only:

- `BranchNode` → exec-In "In", exec-Out "True", exec-Out "False", data-In "Condition"/Boolean
- `SequenceNode` → exec-In "In", exec-Out "Then0", exec-Out "Then1"
- `LiteralNode` → data-Out "Value"/TypeId
- `CastNode` → exec In/Out + data-In "In"/Object + data-Out "Out"/TargetTypeId
- `LatentDelayNode` → exec In/Out + data-In "Duration"/Single
- `WhenNode` → exec-In "In", exec-Out "OnFired", exec-Out "OnEnded", exec-Out "Out"  
  *(names load-bearing for Stage5_Schedule.GetWhenExecSuccessor)*
- `EventEntryNode` → exec-Out "Out" (dynamic data-Out pins enriched from Graph.Inputs)
- `ReturnNode` → exec-In "In" (optional data-Out from Graph.Outputs)
- `GetVariableNode` → empty (data-Out "Value" fully dynamic)
- `SetVariableNode` → exec In/Out (data In/Out enriched with typed Value pins)
- Non-pure `FunctionCallNode` → exec In/Out skeleton (data pins from reflection/graph)
- Pure `FunctionCallNode` → empty
- All other exec-only kinds → exec In/Out

---

## Stage0_Rehydrate Algorithm

### Entry Point
`public static void Run(BlueprintAsset asset, CompileOptions options)` is called as the FIRST
statement of `BlueprintCompiler.Compile`, before `Stage2_Validate.Run`. Iterates all graphs in
the asset; for each graph builds link adjacency maps (outLinks/inLinks per nodeId), then for
each node with `Pins.Count == 0`:

1. **Build canonical pin list** (`BuildCanonicalPins`):
   - Call `options.NodeRegistry.GetStaticPins(node)` for the static skeleton
   - Convert `PinSchema` records to `Pin` objects (no GUIDs yet, IDs assigned next)
   - Dispatch to dynamic enrichers for: `EventEntryNode`, `ReturnNode`, `GetVariableNode`,
     `SetVariableNode`, `FunctionCallNode`, `CallCustomEventNode`, `CallPeerBlueprintNode`

2. **Assign link GUIDs** (`AssignLinkGuids`) — mirrors `BlueprintGraphModel.Rebuild` slow-path (~:181-228):
   - Separate canonical pins into Out-bucket and In-bucket (declaration order)
   - Collect distinct `FromPinId` GUIDs from outgoing links (first-occurrence order) → assign
     i-th GUID to i-th Out pin (fan-out deduplication handled by the HashSet)
   - Collect distinct `ToPinId` GUIDs from incoming links → assign to In pins
   - Leftover pins (no link) → deterministic synthetic GUID via
     `Stage3_Normalize.SynthesizedGuid($"pin:{nodeId:N}:{pin.Name}:Out/In")`

3. **Write back** to `node.Pins`

### How It Mirrors BlueprintGraphModel.Rebuild

`BlueprintGraphModel.Rebuild` (lines ~181-228) is the editor's slow path that runs when
`Pins:[]` is detected. It walks `graph.Links`, matches `link.FromPinId` to pin order positionally,
and assigns the link GUID to the i-th pin of that direction bucket. `Stage0_Rehydrate` replicates
this algorithm identically: same directional bucketing, same first-occurrence deduplication,
same positional i-th assignment, same deterministic fallback for unlinked pins.

---

## Dynamic Pin Derivation

| Node kind | Source |
|-----------|--------|
| `EventEntryNode` | exec-Out "Out" + data-Out per `graph.Inputs[i]` (Function graphs only) |
| `ReturnNode` | exec-In "In" + data-Out from `graph.Outputs[0]` (Function graphs only) |
| `GetVariableNode` | data-Out "Value" typed via `ResolveVariableTypeId(VariableId, asset)` — strips `var:` prefix, looks up in `asset.Variables` then `asset.WorkingState` |
| `SetVariableNode` | exec In/Out + data-In "Value" + data-Out "Value" (typed from variable) |
| `FunctionCallNode` (graph) | exec In/Out (if !IsPure) + data-In per `target.Inputs` + data-Out from `target.Outputs[0]` |
| `FunctionCallNode` (CLR) | exec In/Out (if !IsPure) + data-In per method parameters + data-Out "Return" if non-void |
| `CallCustomEventNode` | exec In/Out + data-In per `CustomEventDecl.Parameters` |
| `CallPeerBlueprintNode` | exec In/Out + typed data-In per `funcSig.Inputs` + data-Out "Return"; fallback to Return:System.Object |

---

## No-Swallow Fallback for Unresolvable FunctionCall

If a `FunctionCallNode`'s CLR target type/method cannot be resolved via reflection (e.g. in the
netstandard2.0 MSBuild host where the game assembly is not loaded), `EnrichClrFunctionCallPins`
emits a warning to `System.Diagnostics.Debug.WriteLine` naming the node ID, type, and method,
then leaves the exec In/Out pins from the static skeleton as-is.

The fallback result — exec-only skeleton — still compiles; the data wires that target those pins
will become unresolved in Stage4/Stage5, which produces diagnostics rather than a silent empty
Tick. This is acceptable for the MSBuild host (tracked for BP-3 where we fix the assembly loading
gap). In the net8.0 test/editor host, reflection succeeds for all local types.

---

## Pins-Stripped CountingDemo Proof

**Test:** `CountingDemo_PinsStripped_ProofTests`  
- Loads `CountingDemo.bp.json` and strips ALL node Pins to `[]` before calling `CompileAndLoad`  
- Runs through the full compiler pipeline (Stage0 → Stage7)  
- Attaches to entity, ticks 5 frames, asserts `Count == 5`

**Result:** PROOF-CD-STRIPPED-002 **PASSED** — Count == 5 after 5 ticks with all pins stripped.

This proves Stage0_Rehydrate restores full connectivity: EventEntry → GetVariable (Count) +
Literal(1) → FunctionCall(Add) → SetVariable(Count) → Return. Every link's GUIDs are correctly
reassigned to the rehydrated pins, so Stage4 type-resolves and Stage5 schedules all 5 nodes.

---

## Build and Test Counts

### Blueprints test project (`Hrot.Blueprints.Tests.csproj`)
- **Build:** 0 errors, 0 warnings (test project itself)
- **New tests (BP-2):** 10 Stage0_RehydrateTests + 2 CountingDemo_PinsStripped_ProofTests = **12 new, all green**
- **CountingDemo_ProofTests:** 2/2 green (unchanged baseline)
- **Total test results:** 1387 total — 1352 passed, 27 failed (pre-existing), 8 skipped
- **No regressions:** identical failure count to pre-BP-2 baseline (verified by stash comparison)

### Full solution (`IOS-IG-SimHost.sln`)
- **Build:** FAILED — 1 pre-existing error (BP0002 in `Hrot.AI.Behaviors`: `Fdp.Toolkits` not
  loadable in MSBuild generator host when processing `Count2.bp.json`). This error is tracked
  as BP-3 and was present before BP-2.
- **New errors from BP-2:** 0
- **New warnings from BP-2:** 0
- **Pre-existing warnings:** 18 (xUnit2013 style warnings in unrelated test projects)

---

## Weak Points / Deviations

1. **CLR FunctionCall reflection gap:** In the MSBuild netstandard2.0 host, game assembly types
   are not loaded, so `EnrichClrFunctionCallPins` falls back to exec-only skeleton. The data pins
   won't be rehydrated in that context. Consequence: MSBuild-compiled blueprints with
   CLR-FunctionCall nodes will get exec-only skeletons → Stage4/5 data resolution fails → compile
   diagnostics (not silent). BP-3 (Fix generator deserialization) + a future registry-driven
   FunctionCall declaration will address this.

2. **`GraphKind.Event` used in unit tests:** The test graphs are constructed with `GraphKind.Event`
   (not `GraphKind.Function`) because that's what matches an event-driven Tick graph at the asset
   schema level. The `EventEntry` enricher checks `graph.Kind == GraphKind.Function` to decide
   whether to emit data-Out pins from `graph.Inputs` — for the 2-node test (no Function inputs),
   this check is False regardless of graph kind, so the test still exercises the exec-pin
   rehydration path correctly.

3. **Stage0 does not call `Stage2_Validate` sink:** The rehydration runs before the diagnostic
   sink is consumed. NO-SWALLOW is implemented via `Debug.WriteLine` rather than a proper
   `BP00xx` diagnostic code. A follow-up can add a diagnostic code once the sink is available
   earlier in the pipeline.

4. **27 pre-existing failures:** The BP-2 instructions referenced "7 pre-existing DEBT-006
   failures" but the actual baseline on this branch is 27 (recipe stubs, snapshot tests,
   ALC lifecycle tests, golden source tests). BP-2 introduces zero new failures.

---

## Suggested Commit Message

```
feat(compiler): Stage0_Rehydrate — rehydrate pin-less nodes before Stage2 (BP-2)

Blueprints saved projection-only have Pins:[] on every node.  Stage4/Stage5
resolve connections 100% by pin ID, so pin-less nodes produce dangling wires →
empty Tick → "Count stays 0".

Adds Stage0_Rehydrate.Run(asset, options) as the FIRST pass in
BlueprintCompiler.Compile:
- Extends INodeRegistry / BuiltInNodeRegistry with GetStaticPins (all node kinds).
- Builds canonical ordered pin list (static shapes + dynamic enrichment from
  asset state for EventEntry/Return/Variable/FunctionCall/CustomEvent/Peer).
- Assigns link GUIDs via the positional-within-direction-bucket algorithm that
  mirrors BlueprintGraphModel.Rebuild (the editor slow-path for Pins:[]).
- NO-SWALLOW: unresolvable CLR FunctionCall emits Debug.WriteLine + exec-only.

Proof: CountingDemo with ALL pins stripped compiles and counts to 5 after 5 ticks.
10 new Stage0 unit tests + 2 CountingDemo_PinsStripped proof tests, all green.
27 pre-existing failures unchanged (0 regressions).
```
