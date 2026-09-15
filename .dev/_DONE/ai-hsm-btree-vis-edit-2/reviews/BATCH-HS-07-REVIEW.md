# BATCH-HS-07 Review — TASK-HS-07 showcase + Starter recipe

**Reviewer:** Dev Lead · **Date:** 2026-06-13 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read diffs, built the consuming project, re-ran suite)
- **Showcase `HsmShowcase.hsm.json`:** composite+initial child, parallel with 3 regions (each `InitialChildStableId` set), history pseudo-state inside a composite, final state (no children/outgoing), 3 events, 3 transitions + 1 global, `StubIdle` bound on `Idle.OnEntry`/`Activity` + one transition `ActionFunction`. **Every transition `GuardFunction` is null** (VE-DEBT-004). Validator: 0 Errors / 0 Warnings; round-trip byte-stable.
- **Starter recipe:** in-code `MakeStarterDto` (root composite + one Initial Simple state) added to `AvailableRecipes()` alongside Empty; validates 0 Errors. Mirrors BTree.
- **Out-of-scope but legitimate — generator fix (`HsmBridgeEmitCore.cs`):** the emitter produced `(delegate*<…>)static (…) => {}` — **a lambda cannot be cast to a function pointer in C#** (only a method group via `&`). So `HsmJsonGenerator` emitted **uncompilable** C# for ANY HSM with a bound action/guard. The showcase (first asset to bind `StubIdle`) is the first to trip it. Fix = static local function + `&__hsActionStub` (the correct idiom); same applied to the guard path. This is a real **build-break-prevention** fix, NOT a hack (no suppression/exclusion/weakening). The worker should per protocol have flagged-and-stopped rather than fixing out-of-scope, but the fix is correct and necessary.
- **DECISIVE end-to-end check:** built `Hrot.AI.Behaviors` (which runs `HsmJsonGenerator` over the showcase) → **0 errors**. Proves (a) the showcase projects to valid C#, (b) the emit-core fix compiles, (c) without it the showcase would have broken the build.
- **No cheating:** no fake `[HsmGuard]`/actions, no validator weakening. `SampleGuard.hsm.json` untouched.
- **Tests (22, behavioral):** deserialize, byte-stable round-trip (double+triple), 0-Error validation, shape (parallel≥2 regions, history, final, ≥2 events, ≥1 global, StubIdle bindings, **all guards null**, event refs), Starter recipe (one initial state, round-trip, 0 Errors).
- **Re-run:** `Hrot.Hsm.Editor.Tests` **454/0** (22 new, 0 pre-existing failures).

## Issues
- Out-of-scope file (`HsmBridgeEmitCore.cs`) — accepted as a necessary correct fix; committed separately with its own message. Logged the latent-build-break nature in the tracker.

## Verdict
APPROVED. Showcase + Starter recipe land; the HSM JSON→bridge codegen no longer emits uncompilable C# for bound actions/guards (build-break closed). Guard-binding demo deferred (VE-DEBT-004). Pixel/layout polish → REVIEW-HS.
