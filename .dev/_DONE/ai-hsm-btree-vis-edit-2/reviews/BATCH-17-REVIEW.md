# BATCH-17 Review — TASK-BT-17 Generator symbol-check (CRITICAL guarantee) — implemented by sonnet

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent — soundness-focused)
- **Validator (`BTreeMethodCompatibilityValidator`) is SOUND — no false-pass.** Read in full: every "can't confirm compatible" path returns a non-null reason → asset invalid → skip + BTREE0002: unresolvable blackboard/context/`NodeStatus`/`BehaviorTreeState` symbol; unresolved method; non-static/non-public; wrong return; wrong arity; wrong ref-kinds; param types via `SymbolEqualityComparer`; param3 ≠ `System.Int32`; `ThreeParamReusable` (safe-skip, `// TODO VE-DEBT-002`). Reachable-leaf walk mirrors the emitter's entry selection + visited-set (cycle-safe). Matches the real `NodeLogicDelegate<TBB,TCtx>` (cited `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeLogicDelegate.cs`).
- **Generator wiring:** `rawFiles.Combine(context.CompilationProvider)` → `RegisterSourceOutput` → `Validate(dto, compilation)` before emit; incompatible → `BTREE0002` Warning + skip (same path as BT-12/14).
- **No false-reject of real assets:** full `dotnet build IOS-IG-SimHost.sln` → **0 errors, NO BTREE0002** (CombatShowcase's `Action_Wander` + SampleScout still emit).
- **6 new tests** (incompatible DTO-param action + condition, unresolved method, compatible-emits, sibling-isolation, wrong-arity/return). Independent re-run: `Generators.Tests` **52 passed / 2 failed** (the 2 = pre-existing MigrationEquivalence, verified pre-existing in BATCH-09), `Persistence.Tests` **123/0**, `BTree.Editor.Tests` **505/0**.

## Issues
None. (Incrementality: combining with full `CompilationProvider` re-runs generation on any code change — acceptable for the small asset set; logged as VE-DEBT-003.)

## Verdict
APPROVED. **The build can no longer be broken by ANY incompatible binding** (palette, Inspector, or hand-edited JSON) — the editor always launches. Completes the fault-tolerant-codegen guarantee (BT-12 unbound + BT-14 cycle + BT-17 incompatible-binding).

## Commit message
```
fix(btree-editor)!: generator symbol-check — incompatible bound method → diagnostic, never build break (BATCH-17 / TASK-BT-17)

A leaf bound to a method that can't bind to the tree's blackboard/context (DTO-
param method, etc.) emitted uncompilable .Action(Method,…) → broke the whole
Hrot.AI.Behaviors build (reachable via the Inspector picker). Add
BTreeMethodCompatibilityValidator: the generator (now combined with
CompilationProvider) resolves each reachable bound Action/Condition method and
validates it against NodeLogicDelegate<TBB,TCtx>; incompatible/unresolved →
asset skipped + BTREE0002 (never an Error). Sound (no false-pass: unresolved =
invalid). Real valid assets unaffected (no BTREE0002). +6 generator tests.

Implemented by sonnet; lead-verified (validator soundness + wiring + full build).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
