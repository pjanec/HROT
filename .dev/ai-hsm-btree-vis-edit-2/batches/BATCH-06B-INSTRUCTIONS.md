# BATCH-06B — CORRECTIVE for BATCH-06 (showcase Condition binding)

**Task:** TASK-BT-06 corrective. **One objective.** Builds on BATCH-06 (do not redo it; only fix the issue below).

## 🔒 Working agreement (MANDATORY)
Same as prior batches: **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 🐛 Issue found in review
In `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json`, the **Condition** leaf (`VisualId` `30000000-0000-0000-0000-000000000001`) is bound to `Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander` — that is an `[BTreeAction]`, **not** a `[BTreeCondition]`. A Condition node must bind a real condition method. (The structural test passed only because the FQN was non-empty; the binding is semantically wrong.)

## ✅ Fix (exact)
1. **`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json`** — on that Condition node:
   - `Condition.MethodFqn` → `"Hrot.AI.Behaviors.Brains.CgfNodes.Condition_TargetAliveAndVisible"` (a real `[BTreeCondition]`, verified at `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs:466`).
   - `DisplayLabel` → `"TargetAliveAndVisible"`.
   - Fix the `EditorMetadata.Comment` to describe the real condition (not "Always Succeed").
   - `Condition.DelegateShape`: `Condition_TargetAliveAndVisible(ref FireAtTargetParams, ref BehaviorTreeState, ref BTreeContext)` takes a DTO param — set the `DelegateShape` value that matches a 3-param DTO-style condition (`ThreeParamReusable`); if unsure which enum value is correct, read `BTreeActionDelegateShape` and pick the one for the "reusable/expression-target DTO" shape. Leave `ExpressionTargetField` null.
   - The Action leaf keeps `...CgfNodes.Action_Wander` (correct — it IS a `[BTreeAction]`). Do NOT change the Action, Wait, Subtree, Pills, or any other node.
   - The file MUST still round-trip byte-stable.
2. **`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Persistence/BTreeShowcaseAndStarterTests.cs`** — STRENGTHEN the feature-projection test so this error class can't pass again. Add/extend assertions on the projected model:
   - The Condition leaf's `MethodFqn` is non-empty, **differs** from the Action leaf's `MethodFqn`, and **contains `"Condition"`** (case-insensitive).
   - The Action leaf's `MethodFqn` **contains `"Action"`** (case-insensitive).

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. the strengthened assertions).
- [ ] Showcase Condition binds `Condition_TargetAliveAndVisible`; Action still binds `Action_Wander`; file round-trips byte-stable.
- [ ] Report appended/updated at `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-06-REPORT.md` (note the corrective).
