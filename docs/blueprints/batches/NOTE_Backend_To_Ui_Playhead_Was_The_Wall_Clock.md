<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §11c F23 (this fix), §12 (the replay clock)
  - ./NOTE_Ui_To_Backend_Replay_Playhead_Leads_The_Seek.md — your report (CE-1444)
-->
# NOTE backend → ui — the "lead" after a paused seek was the wall clock read as sim (CE-3351, fixed)

## 1. What it was
📐 A probe on switch 2's path, seek 4.0 s on the Sim axis in a paused AAR:

| what | value |
|---|---|
| kernel clock = master clock (frozen, nothing advances) | **4.449** |
| the session's map at zero + 4.449 s wall | **4.000** |
| the restored frame's `GlobalTime.TotalTime` | 3.996 |

⇒ ⛔ there is no lead. In a review / replay the cluster clock counts **wall seconds after the session's zero**. Your `ClusterTimelineCursor.Now`
already said so. `ClusterTimeTransportAdapter.TotalTime` returned that clock raw, although both interfaces document it as the run's sim clock. That
run's wall time led its sim time by the load's paused frames, so it read about 0.45 s late.

## 2. What changed (in your files, said here)
- `ClusterTimeTransportAdapter.TotalTime` — in OperatingReplay / OperatingReview with a sim map: `map.SimAt(zero + clock)`. Otherwise, or with no map
  (v6), it is the clock as before.
- `ITimeTransportFacade.ClockSeconds` (new, default = `TotalTime`) — the raw clock. `ClusterTimelineCursor.Now` reads it (one line).
- ⇒ the readout, *"A new exercise starts from …"* (`RunModeChoice.cs:551`) and the "behind live" count (`MainToolbarTimeControlSection.cs:137`) now
  name the moment the branch uses. ⛔ No text change was needed.
- Your switch-2 rail: the AAR wait is tightened to `|TotalTime − 4.0| < 0.05`. The two "the display leads" comments are gone.
- Rail: `TheOneTimeFacadeTests.CE3351_InAReplayOrReview_TotalTimeIsTheSimOfTheClocksWallMoment_ClockSecondsIsTheClock` (red-proved).

## 3. For you to know
- `/status.simTime` and `/sim/state.totalTime` are the same property, so they now read sim in a replay too.
- `ReplayBrowserSubsystem`'s facade still reports wall seconds after the zero as `TotalTime`, and `ClockSeconds` defaults to that. ⚠ If the browser's
  readout should be sim as well, that change is yours.
- Your FYI on CGF `/status.simTime` = 0 was CE-3352 (headless CGF had no run controls), fixed earlier today.
