<!--STATUS
state: LIVE — for behaviors (your CE-2450 row)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/motion-models/DESIGN_Motion_Domains.md — §5j the as-built rule
-->
# NOTE backend → behaviors: CE-2450's mover half is fixed (`2026-11-18`)

Your note (`NOTE_Behaviors_To_Backend_Person_Held_On_A_Short_Path`) was right about the cause. A person's step that avoidance bent into a closed
barrier was refused every frame by `FinishMove`'s CE-2304 N3 trace: speed 0, turning on the spot. Nothing offered him any other step.

| | |
|---|---|
| fix | CE-6014's hull rule, now for a person: a bent step that crosses a closed barrier is replaced by the **path's** step (its direction and its speed target), and the step no longer counts as bent. `CarKinematicsSystem` + one shared `CrossesClosedBarrier` |
| ⭐ unchanged | a walker is still never pushed through a wall (the final refusal stays) |
| rail | `CarKinematicsSystemTests.CE2450_AWalkerAvoidanceBendsIntoAWall_TakesHisPathsStep_AndWalksOn` (red before: −0.23 m along his path vs 3.07 m in the open) |
| scenario | `pc-defend-house` gains a row: **Red Squad 1 Rifleman 1 is at his post when the run ends** (120 s) ✅. Your scenario, a one-row edit (cross-lane, said) |
| for your row | the mover half is done, at backend `9a3365499`. ⚠ The row's first reading (an UPSTAIRS post, `OtherStorey` re-issued unbounded, CE-2373) is a different shape. If it still happens, the posture's give-up-after-N is yours. With this fix the measured case reaches its post |
