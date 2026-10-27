<!--STATUS
state: LIVE — backend → coordinator (cc ui): CE-4062, CE-4064 and CE-1365 fixed; what each answer was
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12v (CE-4064), §12u (W5, the reader discard at every replay exit)
  - ../../DESIGN_People_Display.md — U10 / CE-1171 row (CE-4062)
  - NOTE_Coordinator_To_Backend_Films_P0_Measured.md — the note this answers
  - NOTE_Ui_To_Backend_Films_P2a_And_The_T3_Flake.md — ui's note (CE-1365)
-->
# NOTE — backend → coordinator (cc ui): films P0 follow-ups

**Dispatched at** the commit that adds this file (`backend`).

| row | answer | fix | ⚠ |
|---|---|---|---|
| **CE-4062** the blow never replays | your three questions: ✅ `StrikeNotification` IS recorded (every event stream not `NoReplay`) · ✅ replayed onto the RB world's bus with each played or stepped frame · ✅ `Striker` is the recorded world's own entity. ⛔ The cause: `EffectSpawnSystem` returns at its replay gate BEFORE it read the strikes (`StrikeTracker` said *"empty during a replay"*) | `TrackStrikes` runs before the gate — presentation memory only, it spawns nothing (a cross-lane edit in `Hrot.Presentation`, ui: FYI) | a SEEK skips events, so landing mid-swing by a seek shows no swing; step or play across it |
| **CE-4064** the RB step counts paused frames | — | on the Sim axis the step AND play go to the next frame whose sim time moved, in one seek ([DAR §12v](../../DESIGN_Replay_Time_Axis_And_Review.md)); Wall unchanged | the fast single-step HTTP calls being dropped is not addressed |
| **CE-1365** (ui's T3 flake) | the race was in PRODUCTION: `CuratedScenarios` overwrote in place in a root every editor and test host shares | identical bytes are left alone, anything else is a temp file renamed over the target | ui: `CE1364_T3` should now hold in a parallel run — tell me if it still flakes |

⭐ Re-measure ask (your method): `ua-civ-melee` at 4.4 s in the Replay Browser — played at 1× or stepped across the blow, the Rioter should swing.

_backend lane, 2026-10-27_
