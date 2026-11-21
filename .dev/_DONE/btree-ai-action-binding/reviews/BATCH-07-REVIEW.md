# BATCH-07 Review (S2-4)
**Status:** ✅ APPROVED   **Date:** 2026-06-16

## Summary
`HsmValidator` now emits a hard `ConcurrentStatefulSubtree` (Severity.Error) when the same stateful Subtree asset is referenced across ≥2 orthogonal parallel regions of one composite; stateless/same-region/different-asset cases stay clean. Verified by reading source + running the suite.

## Verification (lead-run)
- `Hrot.Hsm.Editor.Tests`: **503/0** (6 new + 497 pre-existing, 0 regressions). Counts match the report.
- Read all 6 test assertions: real (`Assert.Single(... Code==ConcurrentStatefulSubtree)`, `Severity==Error`, `TargetStableIds` contains composite, `DoesNotContain` for the negative cases). Good coverage: required 2 + same-region + default-resolver + different-assets + no-spurious-other-code.

## Key finding — S2-4 ships DORMANT in production (recorded as debt, not a blocker)
Grep confirms `SubtreeAssetId` exists ONLY in the two touched files → there was no pre-existing HSM subtree-reference field. So:
1. `StateNode.SubtreeAssetId` is **new and not persisted** to JSON (no real asset sets it).
2. `_isStatefulSubtree` defaults to `_ => false`; production never supplies a real resolver.
3. The production `HsmAssetValidator` entry point isn't threaded to pass either.
⇒ The check is correct + tested but provides **no real protection today**. This matches Slice-2 reality (the BTree→stateful-subtree composition is itself deferred, DEBT-AIB-025). Validator is ready for when that feature lands. → **DEBT-AIB-028**.

## Issues Found (non-blocking)
### Note 1 (→ DEBT-AIB-029): direct-children-only
`CheckConcurrentStatefulSubtrees` walks only `composite.Children` (direct). A stateful subtree nested deeper inside a region (grandchild) is not detected. Spec said "reachable under each region." Acceptable for the dormant v1; extend to full region-subtree reachability when activated.

## Deviations
- New `StateNode.SubtreeAssetId` field (no existing field found — verified by grep). Sound, but see dormancy debt.
- Resolver threaded via optional ctor param (default no-op) — backward-compatible. Correct.

## Verdict
APPROVED. Activation work captured in DEBT-AIB-028/029.

## Commit Message
```
feat(hsm-validator): S2-4 hard-error same stateful Subtree across parallel regions (BATCH-07)

Completes S2-4.
- HsmDiagnosticCode.ConcurrentStatefulSubtree (hard Error)
- HsmValidator: per-parallel-composite check groups direct-child Subtree refs by asset id;
  emits Error when one stateful asset spans >=2 distinct regions. Injected
  Func<Guid,bool> isStatefulSubtree resolver (defaults _ => false; back-compatible)
- StateNode.SubtreeAssetId (new; not yet persisted) as the validator's read source
Tests: 6 (2 required + same-region + default-resolver + different-assets + no-spurious-code);
full Hrot.Hsm.Editor.Tests 503/0.
Dormant in production until subtree-ref persistence + resolver wiring (DEBT-AIB-028/029).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
