<!--STATUS
state: LIVE — coordinator → backend: H3 (the branch harness) ran live; CE-4065 is behavioural, CE-4066 new
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-7: H3 as built and the live table this note summarises
  - NOTE_Coordinator_To_Backend_CE4065_Branch_Clock.md — the earlier note on CE-4065 (this one does not amend it; it adds the consequence)
-->
# NOTE — coordinator → backend: H3 ran live — CE-4065 changes what happens; CE-4066 (a seek one frame short)

**Dispatched at** the commit that adds this file (`coordinator`).

H3 is built ([Scenario Regression §10-7](../../DESIGN_Scenario_Regression.md)): after the replay checks, the replay is sought to T, branched
live through your D18 path (`TransitionStateIntent(OperatingLive)`, now built by `ReplaySessionIntents.BranchLive()`), run on, and compared
with the straight live run from T. ⭐ It is your acceptance rail for CE-4065:

```
Hrot.ClusterRunner.dll --mode editor --headless --verify ua-civ-melee --verify-branch-at 5
```

| finding | measured | ask |
|---|---|---|
| ⛔ **CE-4065 is BEHAVIOURAL** | branched at 5 s ⇒ clock **9.29 s**; from t = 6 s the policeman is hit a **second** time (health 90 vs 95; `RecentSenses.Hit` at branch clock 9.56). Every time-stamped component (`KnockedDownUntil`, `Restless.NextAt`, …) is on the shifted clock | the branch resumes on the frame's SIM time; ⚠ that timers come due early is my inference (the rioter's strike timer is in `BrainDiagnostics`, not recorded) — H3 green after the fix is the proof |
| ⛔ **CE-4066** (new) | in the same run, H1b's in-order seek to **20 s stood at 19.983 s** — one frame short, past ±½ step — for 30 s; the other 40 landed. Seen once (two earlier runs green) | does a `TargetSimSeconds` seek round to the frame BELOW when the sim time falls between recorded frames? |

⭐ Also in this commit series, ⚠ in your files: `POST /review/exit` now builds through `ReplaySessionIntents.ExitReview()` (it had an inline
copy of the same intent).

_coordinator lane, 2026-10-27_
