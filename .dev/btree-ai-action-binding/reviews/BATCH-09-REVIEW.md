# BATCH-09 Review (S2-G — Slice 2 DEMO GATE)
**Status:** ✅ APPROVED — **SLICE 2 COMPLETE**   **Date:** 2026-06-16

## Summary
T20_MultiStateful asset authored; the normal `Hrot.AI.Behaviors` codegen build now compiles the emitted stateful thunk (DEBT-AIB-026 closed); `T20_MultiStateful_ProofTests` proves the full pipeline end-to-end. Verified by lead build + test runs.

## Verification (lead-run)
- **Clean rebuild `Hrot.AI.Behaviors -t:Rebuild`: 0 errors / 0 warnings** — the decisive DEBT-AIB-026 gate (first real compile of the emitted stateful bridge + topology).
- Byte-identity `Hrot.AiEditor.Persistence.Tests`: **129/0** (struct-name change did not perturb goldens).
- `Hrot.AiEditor.Generators.Tests`: **85/87** — only the 2 known MigrationEquivalence failures; **T10 proof tests pass** (unaffected by the struct rename) and **both T20 proof tests pass**.
- `Fdp.Toolkits.Tests --filter Behavior`: **153/0** (no runtime regression).
- Read T20 proof tests: genuinely end-to-end (real generator→Roslyn-compile→register→**real `BehaviorIngressSystem` provisioning**→tick with `BTreeContext{Self,World}`), real value assertions (A=7, B=5, A≠B from independent partition slots; counter=1 + threshold/limits unperturbed → disjoint memory). Documented per-tick arithmetic checks out.

## Key finding — the compile-gate caught 3 real gaps BATCH-06 missed (vindicates DEBT-AIB-026)
BATCH-06's stateful path was **incompletely wired** and its tests didn't catch it because they exercised only `EmitBridge` (and hand-rolled thunks), bypassing the full generator pipeline. BATCH-09's normal-build compile surfaced + fixed:
1. **`BTreeMethodCompatibilityValidator`** — `ThreeParamReusableStateful` fell through to the `FourParamFull` check → the real generator emitted **BTREE0002** (skip) for the stateful node, so the bridge thunk would never have been generated. Added a dedicated 4-param-shape validation branch (param-0 type-matches the variable; param-1 ref WorkingState; param-2 ref BehaviorTreeState; param-3 ref Ctx).
2. **`BTreeEmitCore` struct-name uniqueness** — multiple managed assets sharing `BlackboardTypeName` (T10/T11/T20 all `BrainBlackboard`) emitted same-named structs → CS0101 in the combined build. Now prefixed with the asset name. Struct is nominal (DEBT-AIB-011) so safe; T10 proof tests confirm no behavioral break.
3. **`BTreeEmitCore` topology-core** — emitted a method-group for stateful nodes (CS1503) instead of the `{MethodFqn}@{offset}@{slotKey}` string blob key that matches the bridge registration. Now emits the correct keyed `Action(...)`.

## Issues Found
None blocking. Minor test-infra notes (carried from the harness, documented in-test): `GC.KeepAlive(Fbt.Compiler.FbtAutoDiscovery)` needed to load the lazy assembly before Roslyn; scan coordinator must get a throwaway registry (its `Dispose` clears the registry).

## Deviations (ratified)
- Two separate `DemoCursorParams` variables (one per node) rather than aliasing — cleaner independence demo.
- Live inspector: proof is headless (partition-slot reads in-test). Live BrainBlackboard/partition renderer inspection deferred (the runtime mechanism is proven; UI is the existing inspector path).

## Verdict
APPROVED. **Slice 2 (S2-1…S2-G) COMPLETE.** DEBT-AIB-026 resolved.

## Commit Message
```
feat(btree-binding): S2-G demo gate — multiple stateful primitives end-to-end (BATCH-09)

Completes S2-G. SLICE 2 COMPLETE.
- T20_MultiStateful.btree.json: same stateful primitive at 2 nodes (distinct FNV-1a slots)
  + stateless IncrementCounter; Sequence[AdvanceCursor_A(3), AdvanceCursor_B(5), IncrementCounter]
- Emitter gaps fixed (closes DEBT-AIB-026, first real compile of the stateful path):
  * BTreeMethodCompatibilityValidator: dedicated ThreeParamReusableStateful 4-param validation
    (was falling through to FourParamFull -> BTREE0002 skip)
  * BTreeEmitCore: asset-prefixed blackboard struct name (CS0101 across managed assets)
  * BTreeEmitCore: emit {MethodFqn}@{offset}@{slotKey} blob key for stateful topology nodes
- T20_MultiStateful_ProofTests: end-to-end (generate->compile->register->BehaviorIngressSystem
  provision->tick), asserts independent partition-slot cursors (A=7,B=5) + mixed stateless coexist
Tests: 2 new proof tests; clean rebuild 0 errors; byte-identity 129/0; generators 85/87 (2 known);
Behavior 153/0.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
