<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — seek / paused replay / the branch (D5, D18)
  - ../Architect_Question_108_Run_Mode_Picker.md — §3 (AAR → Live confirmation)
-->
# NOTE ui → backend — after a seek in a paused AAR the playhead runs ~0.3 s past the master's position (CE-1444) · the mode switches pass live

## 1. The playhead leads the seek (yours, we think)
📐 Measured in the real headless editor (switch 2's rail, one sim axis): seek to 4.0 s in a paused AAR → the node's `TotalTime` (the shown
world / playhead) reads **4.317 s** (4.27–4.37 across runs) — the branch then starts at **4.001 s** (the master's position; the branched
run's sim once it records). ⇒ the operator sees, and ui's confirmation text says, a moment ~0.3 s later than the one the branch uses.
**Ask:** is the replay meant to settle exactly on the seek target while paused (the look-ahead / barrier window letting the slave run on
past it)? If the playhead should equal the master's position, that fixes the text too; if a lead is by design, tell ui which value names
the branch moment and ui words the text from it. ui's row: CE-1444.

## 2. CE-4104 live, over HTTP (`scripts/repro-mode-switches.sh`, orchestrator + cgf + simhost, `demo-city`) — 0 failed checks
① `POST /replay` → OperatingReplay · ② `/replay/seek` + `POST /cluster/transition {OperatingLive}` → a new exercise, `parentExerciseId` = the
parent · ③ `POST /review {atSeconds}` → OperatingReview · ④ `POST /review/exit` → OperatingLive, one recording.

## 3. Observed on the way — FYI, not chased
- CGF's `/status.simTime` and `/sim/state` read **0 and paused throughout** the live run, even after `POST /sim/play` on the master — is that
  field CGF's local clock that a slave never advances, or a reporting gap?
- One earlier run (not repeated): the branch's inventory row's duration went 2.0 → 47.1 (in the review) → 1.7 (after ⏏) and its parent
  showed "-" for a while.
- No HTTP route reads the live point (L) — the switches script cannot check ④'s "live resumes at L"; the editor rail does.
- `basic-desert` fails to load: *"ScenarioSerializer: scenario DOM missing"* (the script uses `demo-city`).
