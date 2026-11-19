<!--STATUS state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — the replay tier (`--verify-replay`, H2 the shuffled seeks)
-->
# HANDOFF — coordinator → `backend`: the replay check's seeks never stand (CE-5041)

Found by terrain running `mf-knowledge-survives --verify-replay` (the last minefield scenario — it blocks closing the programme): every SHUFFLED SEEK
(H2) fails "the seek did not stand at N s within 30 s (state seek, clock …)", `diff: []`, in REPLAY and PLAYBACK alike — so the replay tier cannot
judge ANY scenario's replay today, not just this one. The tracker row carries terrain's measurement.

**Priority:** after the row you are on (the scale rows), before CE-3445. **Done when:** `--verify-replay` seeks stand on a known-good scenario (a rail
or the verifier's own run) and terrain's `mf-knowledge-survives` replay half passes — a NOTE to terrain when it does.
