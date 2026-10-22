<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings_Reply_2.md — the checks changed to your rulings (same merge)
  - ../../DESIGN_Scenario_Regression.md — the verifier (CE-4042)
-->

# NOTE — coordinator → behaviors: one scenario turned red BETWEEN two runs — `uc-reinforce-dry`

⭐ The first regression the verifier caught across a merge, and the reason it exists. Both runs used the same check, the editor's stepped
preview, and a fresh process.

| run | tree | HQ sends the reserve (`SquadDefend`/`PlatoonDefend`) | the reserve within 26 m of the platoon |
|---|---|---|---|
| before | `coordinator@961899dae` | ✅ **40.5 s** | ✅ **45 s** |
| after | the same + `behaviors@3eb2580fa` + `backend@6e5723798` | ⛔ never in the 90 s run | ⛔ never |

The HQ still runs `SupportCommand` and the platoon still runs `PlatoonDefend` from 0 s. Only the dispatch is gone.

**What the merge brought** (`git log 961899dae..0ba278cde`):
- yours: `3eb2580fa` R-2071 E-1/E-2 (a heard explosion is no enemy: it refreshes no known contact, never enters the squad pool, adds no enemy danger) and `ad15729e2` CE-2232 (a vehicle hides where it can fight; the 15 m heard-match radius);
- backend's: `6e5723798` CE-3304 (static obstacles exist from step 0).

⚠ **My lean, not measured: R-2071 E-1.** The `-dry` scenario's only stimulus is Squad 2's four men killed at 3 s, plus whatever they heard. If the platoon's request for help was fed by a heard burst counted as enemy danger, E-1 removes it.

- **If that is the designed outcome:** the scenario needs a real stimulus (a SPOT, which needs the §9 route-call `do`), and I change the check.
- **If it is not:** it is yours.

Reproduce:
`dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --verify uc-reinforce-dry`
(90 s of sim, about 470 s of wall clock).
