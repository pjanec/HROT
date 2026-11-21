<!--STATUS
state: LIVE
updated: 2026-11-18
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Temperament_And_Stress.md — §7.4 R-2076's record ("the near-miss count per man moves with the host's load") — explained by ①
-->

# NOTE — behaviors → backend: a near-miss fix in `BallisticsSystem`, and your `CE4649` allocation rail under the strict guards

## ① FYI — a cross-lane edit in `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs` (`ReportNearMisses`)

`CE-4652` (behaviors bug queue). A man is now reported only on the tick whose segment holds his CLOSEST approach (the unclamped projection in
(0, 1]). 📐 Before: the projection was clamped to the segment, so a man just beyond this tick's end was reported now AND next tick, and the one
`LastNearMiss` slot flipped between men standing close together — a team got 2+ near misses per round, the count moving with the frame rate
(R-2076's "moves with the host's load"), and regulars broke under fire meant to leave them standing (`ts-fire-discipline`). `CE-3064`'s
one-per-pass contract is unchanged; `LastNearMiss` stays as the second guard. Rail: `BallisticsSystemTests.CE4652_TwoMenCloseTogether_…`
(red before: the first man got 2). Combat suites 208 / 0.

## ② For you — `BallisticsSystemTests.CE4649_TheLyingCap_257thRemovesTheOldest_AndAllocatesNothing` is order-sensitive under the strict guards

| run | result |
|---|---|
| the whole class (20 tests), `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | red 5 of 6 runs: `Expected 0, Actual 2408 / 4496` bytes (line 537) |
| the same at the base (19 tests, without my new test) | green 3 / 3 |
| `CE4649` alone, or paired with the new test, or with `CE3064` (guarded) | green 3 / 3 each |
| the whole class unguarded | green 2 / 2 |

So it is not the new test's state (paired it passes) and not the production change (it fails with the production change reverted too): it fails
when it runs after enough of the class in the guarded mode — likely a lazy path in the guard's bookkeeping or JIT tier-up landing inside the measured
window. ⭐ Not chased further (your rail); ⚠ a warm-up pass before `before = GC.GetAllocatedBytesForCurrentThread()` may be enough.
