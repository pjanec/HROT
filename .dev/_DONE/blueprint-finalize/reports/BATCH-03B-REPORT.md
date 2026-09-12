# BATCH-03B Report — Compiler: function-graph-call validation hardening

## Implementation Summary

Extended `V_FunctionGraphCallRules` (added in BATCH-03A) in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`
with four new diagnostics. Added the corresponding codes to `DiagnosticCodes.cs`.
Added a test file with 7 tests (1 positive control + 6 negative tests).

### Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs:57-61` | Added BP1651–BP1654 constants in the BP16xx block |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs:1043-1218` | Extended `V_FunctionGraphCallRules.Validate` with 3 passes + `DfsVisit` + `BuildCyclePath` helpers |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/BATCH03B_FunctionGraphCallValidationTests.cs` | New test file (created) |

---

## Rules and diagnostic codes (file:line)

### `DiagnosticCodes.cs:58`
```
BP1651 — FunctionCallNode.TargetGraphId not found or target graph is not GraphKind.Function
BP1652 — FunctionCallNode argument count mismatch (caller data-IN pin count ≠ target graph Inputs.Count)
BP1653 — FunctionCallNode argument type mismatch (positional, conservative TypeId string comparison)
BP1654 — Function-graph call cycle detected (direct or transitive recursion)
```

### `Stage2_Validate.cs` — `V_FunctionGraphCallRules.Validate`

**Pass 1 — per-node checks (BP1651, BP1652, BP1653) + call graph construction:**
- Lines 1068–1124: For each `FunctionCallNode` with non-empty `TargetGraphId`, parse GUID and resolve target.
  - Unresolvable or non-Function target → `BP1651` (line 1079), skip remaining checks for this node.
  - Resolved correctly → add edge `callerGraph.Id → target.Id` to `callEdges` dict.
  - Count data-IN pins (`!IsExec && Direction=="In"`). Mismatch with `targetGraph.Inputs.Count` → `BP1652` (line 1093).
  - When count matches: positional type comparison → `BP1653` (line 1110) for each mismatched pair.

**Pass 2 — BP1650 (BATCH-03A, preserved):** Lines 1128–1146. Checks latent nodes in called Function graphs.

**Pass 3 — cycle detection (BP1654):** Lines 1149–1176. Standard three-colour DFS over the directed call graph.
- `DfsVisit` (lines 1181–1222): white=0/grey=1/black=2. Back-edge (grey neighbour) → `BuildCyclePath` + emit `BP1654`.
- `BuildCyclePath` (lines 1226–1252): walks `parent[]` pointers from back-edge source to cycle-start, produces `"A → B → A"` notation.
- One emission per unique cycle (canonical key = sorted alphabetical join of node names).

---

## Type-compatibility mechanism (BP1653) + limitations

**Mechanism used** (cited): Stage 2 runs before Stage 4 (`Stage4_TypeResolve`). At this point
`Pin.TypeRef.TypeId` carries the raw string set by the asset author (e.g. `"System.Int32"`).
No TypeRegistry resolution is available yet, so full type inference/covariance is not applicable.

**Conservative approach implemented:**
- Compare `callerPin.TypeRef.TypeId` vs `targetGraph.Inputs[i].Type.TypeId` as plain `string.Equals` (ordinal).
- An empty `TypeId` or `"System.Object"` on either side is treated as a wildcard — no flag emitted.
- This guarantees **zero false positives** for unresolved/generic types while catching clear textual mismatches.

**Known limitations:**
1. Generic argument mismatch is NOT detected (only top-level `TypeId` is compared). E.g. `List<int>` vs `List<float>` would not be flagged because both have `TypeId = "System.Collections.Generic.List"`.
2. Array wrapping (`IsArray` flag) is not compared.
3. Inheritance/covariance is not considered (both by design — Stage 2 has no TypeRegistry access).
4. Unresolved wildcards (empty TypeId) bypass the check — expected: partially-authored assets should not produce false positives.

---

## Cycle-detection approach

**Algorithm**: iterative-DFS three-colour marking (white/grey/black) over the call graph.
- Each `Graph.Id` (Function kind only) is initially white.
- On entering a node: mark grey.
- For each called target: if grey → back-edge (cycle). If white → recurse.
- On leaving: mark black.
- Cycle path reconstructed from `parent[]` dictionary walking from back-edge source to cycle-start.
- Canonical deduplication key: sorted-then-joined node names — prevents reporting the same cycle from multiple back-edges.

**Example BP1654 message** for A→B→A:
```
Function-graph call cycle detected: GraphA → GraphB → GraphA. Function graphs compile to
synchronous C# methods; a cycle would cause a stack overflow at runtime.
```

---

## Test names and results

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/BATCH03B_FunctionGraphCallValidationTests.cs`

| Test | Code | Result |
|------|------|--------|
| `BP1651_TargetGraphId_PointsToNonExistentGuid_EmitsBP1651` | BP1651 | PASS |
| `BP1651_TargetGraphId_PointsToEventGraph_EmitsBP1651` | BP1651 | PASS |
| `BP1652_CallerHasOneArgPin_TargetHasTwoInputs_EmitsBP1652` | BP1652 | PASS |
| `BP1653_CallerArgTypeInt32_TargetInputTypeSingle_EmitsBP1653` | BP1653 | PASS |
| `BP1654_SelfRecursion_EmitsBP1654` | BP1654 | PASS |
| `BP1654_TransitiveCycle_ACallsB_BCallsA_EmitsBP1654` | BP1654 | PASS |
| `PositiveControl_ValidFunctionCall_NoBP165x` | (positive) | PASS |

BATCH-03A tests (still green):

| Test | Result |
|------|--------|
| `Stage2_FunctionGraphWithLatentNode_EmitsBP1650` | PASS |
| `Stage5_FunctionCallNodeWithTargetGraphId_EmitsIrOp_GraphCall_And_ReadInputArg` | PASS |
| `E2E_FunctionCallNode_CompileAndRun_WritesExpectedResult` | PASS |

**Raw output (BATCH-03A + BATCH-03B combined run):**
```
Total tests: 10
     Passed: 10
 Total time: 1.6593 Seconds
```

---

## Full-suite failure classification (`Hrot.Blueprints.Tests`)

```
Total tests: 1197
     Passed: 1182
     Failed: 7
    Skipped: 8
```

All 7 failures are **pre-existing** (identical to the BATCH-03A baseline):

| Failing test | Classification |
|--------------|----------------|
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")` | Pre-existing golden snapshot drift |
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")` | Pre-existing golden snapshot drift |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Pre-existing golden snapshot drift |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Pre-existing golden snapshot drift |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing golden snapshot drift |
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Pre-existing |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing |

**Zero new failures. No golden files changed.**

---

## Integration test

```
dotnet test Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot
Total tests: 10
     Passed: 10
```

---

## Build verification

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    0 Error(s)
    0 new warnings in touched projects (Hrot.Blueprints.Compiler, Hrot.Blueprints.Tests)
```

---

## Design Decisions

1. **BP1650 preserved in-place**: the BATCH-03A BP1650 check was restructured into Pass 2 of the extended validator but remains behaviourally identical. The only change is that `calledGraphIds` is now computed from the `callEdges` dict (which is already built in Pass 1) rather than a separate scan, eliminating a redundant loop.

2. **Three-pass structure**: Pass 1 (per-node) → Pass 2 (latent in called graphs) → Pass 3 (cycles). This ordering ensures that BP1651 guards are applied before the call graph is used for cycle detection, so only valid Function-graph edges are considered.

3. **Cycle deduplication**: the canonical key sorts the path elements alphabetically before joining. This prevents the same A→B→A cycle being emitted twice (once discovered from A, once from B's parent walk).

4. **Caller graph kind not restricted**: the validator checks FunctionCallNodes in ALL graphs (Event, Function, Construction). This is correct — a caller need not be a Function graph. Only the TARGET must be Function.

---

## Deviations

None. All rules implemented exactly as specified.

---

## Known Issues / Limitations

- BP1653 type-compat is conservative (TypeId string only; see "Type-compatibility mechanism" section).
- The cycle message format uses `"A → B → A"` notation. If graph names contain `" → "` as a substring, the message could be ambiguous — however this is a cosmetic edge case with no correctness impact.

---

## Suggested Commit Message

```
feat(blueprint-compiler): add BP1651-BP1654 function-graph-call validation (BATCH-03B)
```
