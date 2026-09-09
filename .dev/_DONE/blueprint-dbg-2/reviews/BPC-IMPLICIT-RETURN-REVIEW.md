# BPC-IMPLICIT-RETURN Review (delegated to Zoo `pro`)
**Status:** ✅ APPROVED   **Date:** 2026-06-10

## Summary
`ReturnNode` is now optional: an implicit return is synthesized at genuine end-of-chain. Explicit Return kept for early-exit and non-default status/value.

## Verification performed (independent — Zoo: trust diffs, hard-review)
- **Compiler diffs:** `Stage5_Schedule.SealFallThrough` synthesizes the dispatch-appropriate implicit return — `IrTerm_ReturnStatus(Success)` for AiPrimitive/Library, `IrTerm_Return(null)` for Instance — exactly mirroring `BuildReturnTerminator`; `_fallThroughTarget`→`Goto` (Sequence chaining) preserved. `Stage2_Validate` BP1601 cleanly relaxed (comment only). Both as prescribed.
- **Extra change (beyond spec) — VERIFIED LEGITIMATE:** `ScheduleBranchNode` now seals **empty branch arms** (`else SealFallThrough(...)`) so they get the implicit return instead of a bare default `IrTerm_FallThrough`. Correct and necessary: an empty arm is an end-of-chain that previously (under BP1601) was masked; with BP1601 relaxed it must implicitly return too. Branch arms don't auto-rejoin, so an empty arm genuinely ends → return is right. Not scope-creep / not a hack.
- **Golden change VERIFIED:** `MoveToAndFire.ir.txt` (an AiPrimitive) Block 0 `fall_through` → `return_status Success` — exactly the intended implicit return; not masking.
- **Test adaptations VERIFIED legitimate (not weakening):** `V_DispatchKindCompatibilityTests` `EmitsBP1601`→`CompilesWithoutBP1601` (assertion flipped to `DoesNotContain` — the feature's purpose); BP1601 added to `KnownNotYetEmittedCodes`; `SequenceSchedulingTests` 2 asserts `FallThrough`→`ReturnStatus`.
- **6 new `BPC_ImplicitReturnTests`** read + run → all pass: Instance void→`Return(null)`; AiPrimitive & Library no-Return→`ReturnStatus(Success)`; Branch early-exit `Failure` + implicit fall-off `Success`; explicit `Failure` not overridden; explicit value Return preserved. Behavioral (exact terminator + status), covers all dispatch kinds + early-exit + non-default + value.
- **Full `Hrot.Blueprints.Tests`:** 1752 passed / 7 failed (documented pre-existing reds) / 8 skipped / 1767 total = 1761 + 6 new. Zero new failures.

## Notes (P3)
- New tests assert at the IR-terminator level (correct for a Stage5 change; emit of `IrTerm_Return`/`ReturnStatus` is already covered by existing explicit-Return emit tests). A compile+run `X==7` would be marginally stronger; optional.

## Verdict
APPROVED — committed. Zoo `pro` handled a moderately-complex, semantics-bearing compiler change correctly (incl. a thoughtful necessary extension), with legitimate golden/test updates.
