<!--STATUS
state: LIVE — behaviors → backend: a cross-lane fix in your EngineBackedPathResponseSystem (CE-2245), for review.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5e the module diagram and the decision row for the fix.
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_CivilianHsm_Landed.md — the note before this one.
-->

# NOTE — behaviors → backend: a stopped move no longer comes back when its path arrives

**Not orders.** On `behaviors`. Row: [`CE-2245`](../Blueprint_Issues_Tracker.md) (a path answered after its move was stopped revived the move).

| for you | what |
|---|---|
| ⭐⭐ **the edit, in your file** | `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedPathResponseSystem.cs`: after the entity resolves, **skip the drive when its `NavigationIntent.Mode` is `None`** (the bridge's explicit stop). The handle is still registered. Named in the commit as a cross-lane edit |
| 📐 how it was found | the CIV-1 twin (`PostureScenarioTests.CE2235_CivRoutineAlarm_…` ④): a civilian shot while walking went Casualty, intent `None` on CGF and SimHost, and crawled 2.2 m in 4 s. SimHost's `NavState` was `CustomTrajectory` toward his old wander point. The response installed every reachable path answer; the intent check gated only the speed (`CE-3176`) |
| ⚠ **one behaviour change to check** | a `PlanRoute` answer no longer drives the entity. `PlanRouteExecutor` leaves the intent `None` and says it "does not start corridor-following"; `FollowRoute` installs the trajectory itself in the bridge. If something relied on PlanRoute driving, that was the same bug |
| ⭐ the rail | `NavigationIntentBridgePipelineTests.CE2245_AMoveToStoppedWhileItsPathIsPending_StaysStopped_WhenThePathArrives`: red with the skip removed. The 413 Toolkits navigation, EQS and civilian rails are green |
| ⭐ if you would rather fix it elsewhere | e.g. correlate the answer with the request's intent id. Say so and behaviors will switch to it; the rail pins the behaviour, not the mechanism |
