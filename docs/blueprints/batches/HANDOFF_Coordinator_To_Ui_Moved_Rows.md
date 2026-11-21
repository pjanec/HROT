<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios.md — why these rows moved
  - ../PLAN_Bug_Priorities.md — second tier: the threaded editor, determinism, tests and tools
-->
# HANDOFF — coordinator → `ui`: seven rows moved from backend

🔒 User, `2026-11-18`: *"terrain and ui seems idle; anything for them? … the other lanes might take some of its tasks?"*. Protocol as before, plus the
new §6a (scenarios per row are proportionate, R-4294). The tracker row is the truth for each; your CE-1147 decision is with the coordinator.

| order | row | what | why ui |
|---|---|---|---|
| 1 | **CE-3468** | in the live editor with the parallel / threaded kernel (the default) a `Points` / `Best` scoring root sees no scored children (the archery contest's score) | the threaded editor — you fixed its map faults (CE-1979) |
| 2 | **CE-2438** | the editor's paused-preview + run-to run does not repeat on `pc-defend-house` | an editor run mode |
| 3 | **CE-1473** + **CE-3451** | `Hrot.ClusterRunner.Tests` does not build; six of its rails drifted meanwhile | you found CE-1473 at your gate |
| 4 | **CE-1475** | `SumoNetPlacementTests` F5 / E1 fail on a Czech Windows (decimal commas — R-4221: fix the OUTPUT, never a tolerant parser) | you found it |
| 5 | **CE-1046** + **CE-1038** | `LiveFromReplayTests.AfterBranch_SimGroupsReEnabled` fails under load; the SimHost suite runs a different number of tests per run (a hang) | test reliability; a proven flake gets `[FlakyFact]` with its row, never a skip |
