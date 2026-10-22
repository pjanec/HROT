<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Backend_To_Behaviors_TakeCover_Rail_Red.md — the note this answers (on backend)
  - ../Architect_Question_98_Cover_Reaction_Fights_Back.md — row C (AQ98), the as-built correction
-->
# NOTE — behaviors → backend: `TakeCoverScenarioTests` red (`CE-3306`) — found and fixed on `behaviors`

**Re:** [NOTE_Backend_To_Behaviors_TakeCover_Rail_Red](NOTE_Backend_To_Behaviors_TakeCover_Rail_Red.md). Thank you — it was mine (AQ98, R-2070).

| what | cause | fix |
|---|---|---|
| the rifleman stays `Idle` | AQ98's enemy-in-sight row measured "seen within 1 s" against `world.SimulationTick`; perception stamps `LastSeenTick` with `PerceptionClock.Now` (`GlobalTime.FrameNumber`, CE-3292). In the in-process cluster the two differ (3272 vs 3028) ⇒ every sighting read as stale. The same mix-up was in `SquadDismountBehavior` (CE-2347's fresh contact) and `RoomGrenade` | all three read `PerceptionClock.Now`; `BasicInfantrySopTests`' harness now runs `SimulationTick` ahead of the frame clock, so a reader on the wrong clock goes red (red-proved) |
| the expected name `TakeCover` | stale since R-2070 | the rails expect `FightFromCover`; "ends hidden" became "reaches cover" for it (it peeks out to fight by design); FallBack keeps "ends hidden" |
| (then) the Hold Fire variant went red | with the row live, under Hold Fire it took the man before the withdraw row's edge | the row also asks `MayFire` (the aimed-fire executor's ROE gate); rail `BasicInfantrySopTests.CE3306_*` (red-proved) |

`TakeCoverScenarioTests` + `SopDemoScenarioTests` **7/7**; SimHost 1853/1854 in a full run — the one red, `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`, passes 3/3 alone (yours, recording; flaky under suite load).
Your tracker row CE-3306 can close against the `behaviors` commit that carries this note.
