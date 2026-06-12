# BATCH-BB1C Review
**Status:** ✅ APPROVED   **Date:** 2026-06-12

## Summary
Corrective Task 0 (B-3 completion), B-4, and B-5 all done and well-tested. B-1…B-5 are now complete. Verified
green independently: Persistence 112, BTree 429, HSM 378, AiShared 1049 — 0 failed, 0 new.

## Resolved (BB1B follow-ups)
- **Issue 1 (live wiring): FIXED.** `PerspectiveWorkspaceRegistrar` now takes + forwards
  `expressionTargetFieldAccessor` to `InspectorWindow`; tests assert forwarding via
  `Inspector.HasExpressionTargetFieldAccessor`.
- **Issue 2 (edit-service test): FIXED.** `DefaultValueAuthoring` helper extracted; `DefaultValueAuthoringTests`
  drive the **real** StructEdit `ComponentEditService` over a DTO with an enum field (hydrate → `SetBoxed` →
  commit → serialize → rehydrate round-trip). Root cause of a struct-serialization miss found + fixed
  (`JsonSerializerOptions.IncludeFields = true`, shared across hydrate/serialize/Inspector).

## Verified correctness
- **Auto-delete is shared-var-safe (the key risk):** both `BTreeCommandSink.ApplyRemoveNodes` and
  `HsmCommandSink.ApplyRemoveLinks` delete the bound var only when `IsAutoManaged==true`; tests
  `DeleteActionNode_SharedVar_DoesNotRemoveVar` / `RemoveTransitionLink_SharedVar_DoesNotRemoveVar` prove shared
  vars survive. Tests drive the real `sink.Apply(...)` path.
- **Alias-drop exclusion (§3.7):** `VariablesPanelControl.IsAliasDropAccepted` is a real static predicate;
  tested true for matching shared var, false for auto-managed regardless of type.
- **VM population + unused-diagnostic:** `BuildViewModel` populates `IsAutoManaged`; auto var with a live node
  ref is not flagged unused.

## Minor (not blocking)
- The "panel section split" tests (`NodeOwnedVariableTests` ~L82-113) filter rows in the test itself rather than
  invoking the production `DrawSection` split (which is ImGui-bound). The genuinely-logical parts (VM population,
  alias predicate, lifecycle) ARE tested against production code; the dimmed-group rendering is a visual item for
  REVIEW-BB1. Acceptable.

## Verdict
APPROVED. B-1…B-5 complete and committed. Remaining BB1 items: REVIEW-BB1 (running-editor visual gate — user)
and DEBT-BF-04 (HSM state-action per-slot picker — needs a design call; states host 4 action slots).

## Commit Message
```
feat(blackboard/inspector): B-3 live-wire + node-owned var presentation/lifecycle + tooltip (BATCH-BB1C)

Completes B-3, B-4, B-5 (BB1 core complete).
CT0 (B-3): PerspectiveWorkspaceRegistrar wires expressionTargetFieldAccessor → InspectorWindow;
  DefaultValueAuthoring helper extracted (Hydrate/OpenSession/CommitAndSerialize, IncludeFields for
  struct DTOs); 14 tests incl. real StructEdit edit-service enum round-trip + accessor + registrar.
B-4: VariableViewModel.IsAutoManaged + BuildViewModel population; VariablesPanelControl node-owned
  "Node-Owned Allocations" dimmed group + IsAliasDropAccepted predicate (auto vars excluded);
  BTreeCommandSink/HsmCommandSink auto-delete the node-owned var (IsAutoManaged-gated, shared-safe)
  + mark dirty (re-pack). 20 tests.
B-5: DefaultValueAuthoring.StaticVsDynamicTooltip const + assertions.
Suites green: Persistence 112, BTree 429, HSM 378, AiShared 1049; 0 failed, 0 new.
```
