# BATCH-BB1B Review
**Status:** ⚠️ APPROVED WITH FOLLOW-UP   **Date:** 2026-06-12

## Summary
Corrective Task 0 (B-2 binding) is complete and well-tested via the real `ApplyFacet` path. B-3's data layer
(`DefaultValueJson` on entry/DTO/mapper + `UpdateVariableDefaultValueJson`) is complete + round-trip/back-compat
tested. The B-3 authoring panel is implemented in `InspectorWindow` but is **not live-wired** and its
StructEdit→JSON path is **untested**. Independently verified green: Persistence 112, BTree 424, HSM 373,
AiShared 1025 — 0 failed, 0 new.

## Resolved
- **B-2 binding (was BB1A Issue 1): FIXED.** `Promote()` + `value=newName` flows through `BTreeFacetMapper.ApplyFacet`
  / HSM apply to persist `ExpressionTargetField`. `PromoteBindTests`/`HsmPromoteBindTests` drive the **real**
  apply path and assert `node.Action.ExpressionTargetField` on the model + a model→DTO→model round-trip. B-2 done.

## Issues Found

### Issue 1: B-3 authoring panel not wired in the composition root (P1 → Corrective Task 0 of BB1C)
**File:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs:167` (constructs
`InspectorWindow`) — NOT modified this batch; the new `expressionTargetFieldAccessor` ctor param defaults to
`null`. So in the running editor the "STATIC PARAMETERS" panel never renders (accessor null). The report's claim
"injected via delegate from composition root" is inaccurate — the composition root was not touched.
**Fix (BB1C CT0):** wire `expressionTargetFieldAccessor` in `PerspectiveWorkspaceRegistrar` to read the current
BTree/HSM facet's `ExpressionTargetField` (reflection over the facet, mirroring the dispatcher). Add a headless
test that the wired accessor returns the bound var name for an Action/transition facet.

### Issue 2: B-3 StructEdit→JSON authoring path untested (P1 → Corrective Task 0 of BB1C)
**File:** `InspectorWindow.cs:326-399` — the hydrate-from-`DefaultValueJson` → `_facetEditService.Open` →
edit → `Commit` → `Serialize` → `UpdateVariableDefaultValueJson` logic is embedded in the ImGui draw method, so
nothing exercises it. The batch explicitly required a test driving the **real StructEdit edit-service** over a DTO
(incl. an **enum** field) → serialize → assert `DefaultValueJson`, and hydrate-back.
**Fix (BB1C CT0):** extract the hydrate/serialize logic into a headless-testable helper (e.g.
`DefaultValueAuthoringSession`) and add a test: open the edit service over a DTO with an enum field, edit it,
commit→serialize, assert the JSON carries the value (enum by name per ENUM-NAME), and re-hydrate round-trips.

## Test Quality
Strong where present: `PromoteBindTests` and `DefaultValueJsonRoundTripTests` use real apply paths, real mappers,
real `JsonSerializer`, and back-compat legacy JSON. Gap is coverage, not correctness: the B-3 edit-service
authoring path (Issue 2) has no test.

## Verdict
APPROVED for commit (green, B-2 fully complete, B-3 data layer complete; no regressions). B-3 authoring panel
wiring + edit-service test → BB1C Corrective Task 0.

## Commit Message
```
feat(blackboard/inspector): Promote→bind (B-2 done) + DefaultValueJson data layer + StructEdit panel (BATCH-BB1B)

Corrective Task 0 — completes B-2: Promote now binds ExpressionTargetField via ApplyFacet
(CurrentNodeVisualId/CurrentVisualId threaded through the facet contexts; DrawInput promote
branch sets value=newName). 9 new headless tests assert create+bind+round-trip (BTree+HSM).

B-3 (data layer + panel; live wiring deferred to BB1C):
- BlackboardVariableEntry gains optional DefaultValueJson; IBlackboardManagedAsset.
  UpdateVariableDefaultValueJson seam; BehaviorTreeAsset/HsmAsset implement it; both mappers
  round-trip it; DTOs [JsonIgnore WhenWritingNull] for byte-stability.
- InspectorWindow "Static Parameters" StructEdit panel (hydrate from DefaultValueJson, edit,
  serialize back) — accessor not yet wired in PerspectiveWorkspaceRegistrar.
- 16 new persistence round-trip/back-compat tests.
Suites green: Persistence 112, BTree 424, HSM 373, AiShared 1025; 0 failed, 0 new.
```
