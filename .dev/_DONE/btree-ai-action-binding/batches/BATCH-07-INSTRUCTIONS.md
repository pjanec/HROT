# BATCH-07: Slice 2 — Cross-region validator forbids concurrent stateful Subtree
**Tasks:** S2-4   **Phase:** Slice 2 editor validation   **Est:** ~8h
**Dependencies:** independent of S2-1/S2-2 (editor-only). BATCH-06 committed.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/btree-ai-action-binding/SLICE2-DESIGN.md` §10 Flaw 3 (the mandated fix) + §6.2.
3. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S2-4 — the two named tests + exact assertions. Implement those exactly; do not invent acceptance criteria.
4. Codebase-memory MCP first.

## What the hazard is (verbatim from the design)
`FNV-1a(BehaviorAssetId, NodeVisualId)` is unique within one asset's execution — but if an HSM runs the **same Subtree** concurrently in **two orthogonal parallel regions**, the stateful nodes inside compute the **same** synthetic keys for both → both project WorkingState over the same partition slot → race-write corruption. The editor must **hard-error** on this.

## Key current-code facts (verified by dev-lead — exact paths/lines)
- **Parallel regions are an HSM concept.** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`: `StateNode` has `bool IsParallel`, `List<RegionNode> RegionNodes`, `int RegionIndex`, `List<StateNode> Children`, `Parent`. `RegionNode` has `byte RegionIndex`. A child state's `RegionIndex` says which region it's in. `HsmAsset.GetParallelRegionMap()` (~lines 287-296) returns `Dictionary<Guid stableId,int regionIndex>` for states whose parent `IsParallel`.
- **Existing cross-region validator** lives in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs` (~lines 253-287): it iterates `asset.AllStates` where `s.IsParallel && s.RegionNodes.Count >= 2`, maps region→writers, and emits `HsmDiagnostic(HsmDiagnosticCode.CrossRegionBlackboardConflict, HsmDiagnosticSeverity.Warning, msg, new[]{composite.StableId})` when two regions write the same variable. **This is the method to extend** with the stateful-Subtree check.
- **Diagnostic types**: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnostic.cs` — `record HsmDiagnostic(HsmDiagnosticCode Code, HsmDiagnosticSeverity Severity, string Message, IReadOnlyList<Guid> TargetStableIds)`; `enum HsmDiagnosticSeverity { Info, Warning, Error }`. `HsmDiagnosticCode.cs` — add a new code (see below).
- **Subtree node reference**: in HSM, find how a state references a sub-behavior/subtree (search `HsmAsset.cs` + the BTree model `Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` `BTreeSubtreePayload { Guid SubtreeAssetId; string SubtreeName; bool IsResolved; }`). For HSM the equivalent is the state's referenced sub-behavior/sub-tree id — locate the exact field on `StateNode` (search for "Subtree"/"BehaviorRef"/"SubBehavior"/"AssetId" on the HSM state model).
- **There is NO editor-level "stateful" flag today** — you must introduce the statefulness signal (see Task design).
- **Existing test harness to mirror**: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs` — `MakeParallelAsset()` builds a parallel composite with two region children; tests call `new HsmValidator().Validate(asset, bb)` and assert on the returned diagnostics (`Assert.Single(diagnostics, d => d.Code == ...)`, severity).

## Task: hard-error on the same stateful Subtree in ≥2 parallel regions (S2-4)
**Files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnosticCode.cs` (UPDATE — add `ConcurrentStatefulSubtree`)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs` (UPDATE — new check in the parallel-composite loop)
- the statefulness resolver (NEW small type or a delegate param — see below)
- the HSM state model only if you must read the subtree reference field (read-only access).

**Design:**
1. **Statefulness signal.** A Subtree reference is "stateful" iff its referenced asset contains ≥1 stateful node. Since the editor has no such flag, introduce a **resolver injected into validation**: `Func<Guid /*referencedAssetId*/, bool> isStatefulSubtree`. Production wires it to "referenced asset has any `ThreeParamReusableStateful` action (or, for blueprint/HSM, any WorkingState)"; **find the existing validation entry point and thread the resolver through it** (add an optional parameter defaulting to `_ => false` so existing callers/tests compile unchanged). Tests supply a stub. Do NOT hard-code statefulness by asset name.
2. **The check.** In the parallel-composite loop in `HsmValidator`: for each composite with ≥2 regions, collect every Subtree reference reachable under each region (the state subtree referenced by states in that region), grouped by `referencedAssetId` → set of distinct `RegionIndex`. For any `referencedAssetId` where `isStatefulSubtree(id)` is true AND it appears in **≥2 distinct regions** of the same composite → emit a **hard error**:
   `new HsmDiagnostic(HsmDiagnosticCode.ConcurrentStatefulSubtree, HsmDiagnosticSeverity.Error, msg, new[]{ composite.StableId })` where msg names the subtree and the two region indices.
3. A **stateless** Subtree (resolver returns false) in multiple regions → **no diagnostic** (allowed).
4. Do not change the existing `CrossRegionBlackboardConflict` (variable-write) behavior.

**Tests required** (`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/` — mirror `HsmValidatorBlackboardConflictTests.MakeParallelAsset`):
- `SameStatefulSubtree_InTwoParallelRegions_HardErrors` — build a parallel composite; place a reference to the **same** subtree asset id in a state in region 0 and a state in region 1; pass an `isStatefulSubtree` stub returning true for that id; assert exactly one `HsmDiagnostic` with `Code == ConcurrentStatefulSubtree` and `Severity == Error`, and `TargetStableIds` contains the composite's StableId.
- `StatelessSubtree_InParallelRegions_Allowed` — same topology but the stub returns false → assert **no** `ConcurrentStatefulSubtree` diagnostic is produced.
- (If feasible) `SameStatefulSubtree_SameRegion_NoError` — the same stateful subtree twice in ONE region → no error (only cross-region concurrency is the hazard).

## Global rules
- Build `Hrot.Hsm.Editor` + `Hrot.Hsm.Editor.Tests` 0 errors. Run the FULL `Hrot.Hsm.Editor.Tests` suite green (0 net-new failures). If a pre-existing unrelated failure exists, note it — do not fix unrelated things.
- Editor-only change; no codegen, no runtime, no byte-identity impact. Do NOT touch the persistence/generator assemblies.
- Never weaken a test to pass. Fail loud. Fix root causes to completion; do not stop for permission. Only stop on a breaking design flaw (write it at the top of the report).

## Success Criteria
- [ ] `ConcurrentStatefulSubtree` diagnostic code added; `HsmValidator` emits it (Error) for the same stateful subtree across ≥2 regions; stateless case clean.
- [ ] Statefulness resolver threaded through validation (default no-op; tests stub it).
- [ ] All three (or ≥ the two required) tests pass; full `Hrot.Hsm.Editor.Tests` green.
- [ ] Report at `.dev/_DONE/btree-ai-action-binding/reports/BATCH-07-REPORT.md`.

## Report Requirements
Answer: where the validation entry point is and how you threaded the resolver; how you enumerate subtree references per region; the exact new diagnostic message; how production should wire `isStatefulSubtree` (note it as a follow-up if you only stub it in tests — that wiring may be its own debt); any deviation; weak points; suggested commit message. Do NOT ask comprehension questions.
