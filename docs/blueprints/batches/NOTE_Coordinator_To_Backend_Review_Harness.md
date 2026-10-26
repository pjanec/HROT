<!--STATUS
state: LIVE — coordinator → backend: DAR's live → review → live oracle is the replay harness's review phase; H1b waits for P1; the loop asked of the clock (R-4086, R-4087)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-2 OWNS the harness re-cut after DAR (the review phase H5, H1b on the sim axis, the wire check in H2)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — yours; P3's acceptance and §12e's rails are what H5 / H2 run at scenario scale
  - HANDOFF_Backend_Replay_Review_Build.md — yours; ⛔ NOT amended (scope frozen at cfcb539e5) — this is FYI for P3, never a reason to stop PW…P2
  - NOTE_Coordinator_To_Ui_Review_Timeline_Addendum.md — item 8 (the loop) asks the same as §3 here
-->
# NOTE — coordinator → backend: the review and the replay harness (R-4086)

🔒 **User, `2026-10-26`:** *"yes write the addendum note, and approved"* — on the harness re-cut in [Scenario Regression §10-2](../../DESIGN_Scenario_Regression.md).
⭐ FYI for your batch — **your scope is frozen at `cfcb539e5`**; nothing here changes PW, P0, P1 or P2.

## 1. P3's acceptance runs in the verifier — one oracle, not two

Your design's P3 acceptance — *"live → review → live identical (world + singletons + the next N steps = the straight run — R1's oracle
reused)"* — is the replay harness's NEW phase **H5** (§10-2): `--verify <s> --verify-replay review` runs the scenario live to T, enters a
review at t < T, compares every snapshot with the live run's (`ScenarioGolden.CompareFrom`, carried = the recording's manifest), returns to
live, checks the world equals the one at T, runs on to `runFor` and compares with a straight run — on every feature scenario, in a fresh
process each. ⭐ **The coordinator builds H5 once P3 lands** (it needs `/review` and the review states). Your P3 unit rails stay as your
handoff lists them; ⛔ please do not add a second scenario-scale oracle beside `DirectorRunnerTests` — point P3's report at H5 instead.

What H5 needs from P3, nothing more: the review intent with `atSimSeconds` (R11), `IsOperatingReview` readable from the cluster state, and
the exit's "live, paused, at L" (§3c).

## 2. H1b waits for P1, not for the fixes

CE-4051 / CE-4052 are fixed (thanks) and CE-4053 is ruled (R-3056) — but a stepped run's replay clock advances WALL time (2–19 ms gaps
per step, measured in step 0), so `/sim/step` in a replay cannot land on the live run's sim frames. ⇒ H1b uses `atSimSeconds` and the
Sim-axis step (P1 rows 9, 10, 12). Nothing to do for you beyond P1 as designed.

## 3. ⚠ "Loop range" is a clock behaviour

Your ui note lists *"Loop range"* in the review / replay right-click menu. A loop drives the CLUSTER clock (seek back at the range's end);
done per host, two operators' panels fight. ⇒ ⭐ **ask:** a loop range as a session setting of `ReplayClockProcessManager` (set by an intent
from any host, carried in `ReplaySessionTopic`), or drop the item. Until you decide, ui is told not to build it (the addendum's item 8).

## 4. The wire, at scenario scale

H2 (the seek phase) gains a WIRE check once PW lands: after each seek, the DDS instances alive on a test reader equal the world's owned ids
(your §12e rail, on whole scenarios). Nothing to build for you — say in PW's report which reader / topic set you assert on, so H2 reuses it.

_coordinator lane, 2026-10-26_
