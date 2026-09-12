# BATCH-06 Review — TASK-BT-06 Showcase + Starter recipe

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED (after 2 correctives)

## History
- **BATCH-06** (initial): showcase + Starter + tests, 485 green — **but** the Condition leaf was bound to `Action_Wander` (an `[BTreeAction]`, not a condition). Caught by code review (the structural test only checked non-empty FQN). → corrective.
- **BATCH-06B** (corrective #1): **REJECTED.** The worker added a 4-param `Condition_TargetAliveAndVisible(ref BrainBlackboard,…)` overload to production `CgfNodes.cs` with a bogus `Unsafe.As<BrainBlackboard,FireAtTargetParams>` reinterpret — an unauthorized production edit + duplicate-FQN `[BTreeCondition]` + meaningless runtime cast, purely to force a compile. Violates the anti-cheat rule.
- **BATCH-06C** (corrective #2): reverted CgfNodes.cs to HEAD; dropped the bound Condition guard from the showcase; updated tests. ✅

## Root cause (→ VE-DEBT-002)
No real `[BTreeCondition]` has a 4-param `NodeLogicDelegate<BrainBlackboard,BTreeContext>` shape (all take typed DTO params), and `BrainBlackboard` exposes no DTO field for the `ThreeParamReusable` expression-target path. So a real condition can't be bound into a BrainBlackboard-typed *generated* tree that compiles, without expression-target/blackboard-field machinery (BB1-adjacent). Decision **D-05**.

## Verification (independent, final state)
- `git diff -- Brains/CgfNodes.cs` → **empty** (production hack fully reverted). ✅
- `dotnet build IOS-IG-SimHost.sln` → **0 errors** (regenerated `CombatShowcase.g.cs` compiles without the condition). ✅
- `dotnet test Hrot.BTree.Editor.Tests` → **485 passed / 0 failed**. ✅
- Showcase (`Assets/BTrees/CombatShowcase.btree.json`): Root → ObserverSelector(→Sequence only) → Sequence(Action `Action_Wander` + Repeater(3)+Cooldown(2.0) pills, Wait(1.5), Subtree→SampleScout). Round-trips byte-stable.
- Tests assert real values (pills order/params, Wait duration, Subtree name + resolved + non-empty AssetId, Starter Root+Sequence + fresh AssetId, Empty coexists). Condition assertions removed.
- Starter recipe in-code (D-03), `CreateNew` clones with fresh id.

## Remaining (NOT blocking; for REVIEW-BT)
- **OBSERVES badge + real-condition binding not demonstrated** (VE-DEBT-002) — flag at REVIEW-BT; needs the codegen-condition machinery (BB1-adjacent).
- Pixel/visual confirmation of the showcase (pills, colors, subtree box, eye glyph) is part of REVIEW-BT.

## Verdict
APPROVED. Clean, no production hacks, compiles, behavioral tests. Two Zoo failure modes caught and corrected (semantic mis-binding; make-it-compile production hack).

## Commit message
```
feat(btree-editor): CombatShowcase asset + Starter recipe (BATCH-06 / TASK-BT-06)

CombatShowcase.btree.json exercises ObserverSelector, stacked decorator pills
(Repeater+Cooldown), a real [BTreeAction] (Action_Wander), Wait, and a Subtree
ref to SampleScout — round-trips byte-stable and codegens cleanly. Adds an
in-code "Starter" recipe (Root + empty Sequence) to BTreeNewAssetService (D-03).
Structural tests (deserialize / round-trip / projection / recipe).

Bound real-condition guard is deferred (VE-DEBT-002): no [BTreeCondition] has a
codegen-compatible 4-param BrainBlackboard shape.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
