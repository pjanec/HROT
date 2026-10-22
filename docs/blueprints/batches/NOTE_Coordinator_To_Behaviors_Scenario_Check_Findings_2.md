<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md — the first list (still open; this one adds, it does not amend)
  - ../../DESIGN_Scenario_Regression.md — the verifier (CE-4042)
-->

# NOTE — coordinator → behaviors: the full re-run after your merge (one more red)

The full run after merging your CE-2351 work (103 scenarios, each a FRESH editor process, stepped once per frame, goldens re-recorded):
**see the counts in [DESIGN_Scenario_Regression §7](../../DESIGN_Scenario_Regression.md)**. ⭐ Your three changed scenarios: `ua-weather-sensing` and
`ua-civ-provocation` PASS; `ua-casualty-care-heavy` is below. Everything else that is red is already in the
[first list](NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md) or your CE-2351.

## ⛔ `ua-casualty-care-heavy` — the Medic is ordered to carry, and never carries

I first fixed its checks to your `SquadHold` change: the squad's care is its DRILL (`SquadDrillTrace.Kind` 11), carried is the embark cue
(`IsEmbarkedTag.Mode` is not in the dump). Then, measured in the editor's stepped run:

| Judge point | |
|---|---|
| (1)(2) for 30 s no care, no carry, nobody carries him | ✅ |
| (3) the hostiles at 0 HP (31 s) ⇒ the squad goes at once | ✅ the care drill 31.25 s · the Medic `CarryCasualty` 36.25 s |
| (4) the drag, put down alive | ⛔ **he is never carried** (no `IsEmbarkedTag` on Rifleman 3 in 130 s), and **Rifleman 3 dies at 101.75 s** — he is below 25 HP already at 2.25 s (still under fire after the check's drop) |

⚠ Your sweep reports it PASS live on the cluster ⇒ the editor-vs-cluster family again (CE-4041). Reproduce:
`dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --verify ua-casualty-care-heavy`
