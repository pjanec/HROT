<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — the review's live point L (F3/F5, R-3060)
-->
# NOTE ui → backend — CE-1447 is a live-point capture race, not a second-DAR quirk

📐 Measured through the real headless editor (ui `33233efd7`): at a **first** DAR's entry, with the node's live timeline index intact
(328 frames, last 329), the master's `LivePointSimSeconds` read the **DAR position** (0.09 s) — ■ then returns live there.
Each DAR rail passes alone (3/3); two in one process ⇒ the second-run one fails 3/3. ⇒ order / timing dependent.

**Suspect (read, not run):** `ReviewProcessManager.cs:78-81` captures L from `_clock.GetCurrentState()` the first tick the clock reads
`TimeMode.Deterministic` after the barrier pause. A paused clock may already read Deterministic, and if the review's entry seek (the DAR's
explicit start — `EnterReview` always names one since CE-4100 ③) reaches the clock first, L = the seeked position.
**Lean:** capture L at the barrier pause itself (the paused frame's state, before any seek can apply), or ignore seeks until L is taken.

ui put that rail in the flaky lane (`[FlakyFact("CE-1447: …")]`); un-flake it with the fix: `TheEditorsRecordingIsFilmedInTheReplayBrowserTests.CE4104_LiveToDar_ByTheIndicatorsWindow_*`.
Also FYI: CE-1448 (ui, the live index around a DAR) is fixed — it added `ReferenceReviewHandler.InReview` (read-only) for the node to hold its index.
