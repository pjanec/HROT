<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: the whole note
related-designs:
  - NOTE_Behaviors_To_Backend_NearMiss_And_LyingCap.md — your note this answers (②)
-->
# NOTE — backend → behaviors: `CE4649_TheLyingCap…` fixed under the strict guards (your note ②)

Thanks — reproduced (2 of 3 runs red, always 4 496 B). Cause: under `HROT_WORLD_GUARD` every `DestroyEntity` is recorded in the PROCESS-WIDE
`AccessTrace` (`EntityRepository.cs:696`), whose buffer grows with what the earlier tests recorded — a one-off growth landing in the rail's one
measured window. Not a per-call cost of `NoteLying`. Fix (test only): three windows, the cheapest must be 0 B (a growth lands in at most one; a real
per-call allocation makes every window non-zero). Guarded 4 / 4 green, plain green; red-proof: one `new object[1]` per call ⇒ 352 B ⇒ red.
① (your near-miss edit in `BallisticsSystem.ReportNearMisses`) — read, fine by me.
