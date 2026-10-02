# BATCH-BB1A Review
**Status:** ⚠️ APPROVED WITH FOLLOW-UP   **Date:** 2026-06-12

## Summary
B-1 (type-filtered picker, BTree + HSM) is complete and correct. B-2's data model (`IsAutoManaged` persisted +
back-compat) and Promote variable-creation are complete; **the `ExpressionTargetField` binding write-back is
missing** — Promote creates the variable but does not bind it, and the `PromoteRequested` flag is wired to no
consumer. Tests are high quality (real drawer path, real mapper round-trip, real `JsonSerializer` back-compat).
Independently verified green: Persistence 98/0, BTree 420/0, HSM 368/0.

## Issues Found

### Issue 1: B-2 Promote does not bind `ExpressionTargetField` (P1 → Corrective Task 0 of BB1B)
**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs:187-208` (and HSM mirror).
**Problem:** Spec §3.3/§6 B-2 requires Promote to "create the variable AND bind the node's
`ExpressionTargetField` to it." Current `Promote()` only creates the variable and returns the name; nothing sets
the node/facet's `ExpressionTargetField`. The `PromoteRequested`/`TriggerPromote` flag is observed by no consumer,
so in the running editor clicking "Promote to new variable" neither creates nor binds anything yet.
**Fix (BB1B Corrective Task 0):** complete the create→bind gesture and wire it live. Thread the owning node's
VisualId/StableId into the facet context (alongside the FQN) so Promote can be driven from `DrawInput`; on
promote, set the `ExpressionTargetField` value (StructEdit write-back) so the binding persists via `ApplyFacet`.
Add a headless test asserting the facet/model `ExpressionTargetField` equals the promoted `_auto_` name.

### Issue 2: HSM `ExpressionTargetField` added to transitions/global-transitions only, NOT states (P2)
**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacets.cs` (TransitionFacet/GlobalTransitionFacet).
**Problem:** REVIEW-BB1's stated surface is "select a BTree action / **HSM state** → type-filtered picker." The
batch covered HSM transitions instead of states. The coder's rationale is sound — a state has four independent
action slots (OnEntry/OnExit/Activity/Timer), so a single `ExpressionTargetField` on the state is ambiguous — but
the dominant HSM action surface is therefore not yet pickable.
**Fix:** tracked as DEBT-BF-04; resolve in BB1B — per-action-slot binding on HSM state facets (one
`ExpressionTargetField`-equivalent per action slot), or an explicit design decision with the architect if the
per-slot model needs confirmation.

## Test Quality
Adequate-to-strong. Filtering tests drive the real `BlackboardFieldPickerDrawer.GetItems()` (not just the static
helper); round-trip tests use real `ToDto/FromDto` + real `JsonSerializer`; back-compat deserializes hand-written
legacy JSON lacking the property. Gap: no test asserts the Promote→binding (because the behavior isn't
implemented — see Issue 1).

## Verdict
APPROVED for commit (solid, fully-green increment; no regressions). B-1 done. B-2 partial: variable
creation + persistence done; **binding deferred to BB1B Corrective Task 0**. HSM state-action coverage →
DEBT-BF-04, resolved in BB1B.

## Commit Message
```
feat(ai-editor): B-1 type-filtered blackboard picker + B-2 IsAutoManaged/Promote-create (BTree & HSM) (BATCH-BB1A)

Completes B-1; partially completes B-2 (variable creation + persistence; binding deferred to BB1B).
- BTree/HSM blackboard-field pickers filter variables by the selected action's DtoType via a shared
  facet-FQN context (BTreeFacetFqnContext / HsmFacetFqnContext) threaded through the facet mappers.
- New HSM blackboard-field picker drawer + attribute; ExpressionTargetField added to HSM transition/
  global-transition facets, model, DTO, and mapper (both directions).
- IsAutoManaged added to BlackboardVariableEntry + BlackboardVariableDto/HsmBlackboardVariableDto
  ([JsonIgnore WhenWritingDefault] → byte-stable); carried through both mappers.
- Promote() creates an _auto_{id} node-owned variable of the action's DtoType (IsAutoManaged=true).
Tests: 36 new (BTree drawer 12, HSM drawer 11, persistence round-trip/back-compat 13). Suites green:
Persistence 98, BTree 420, HSM 368; 0 failed, 0 new.
```
