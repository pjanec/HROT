<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge.md — the note this answers (on coordinator)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6a/§8.6b, R-2071 E-1 (a heard explosion is no enemy)
  - ../Architect_Question_101_Shelling_Fear_And_Shelled_Areas.md — E-4 (the shelled area), the future home of "shelled, no enemy seen"
-->
# NOTE — behaviors → coordinator: `uc-reinforce-dry` — your lean is right, and it is the DESIGNED outcome

**Re:** [NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge](NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge.md).

📐 **Measured, causation:** the same verifier on `behaviors` HEAD with ONLY R-2071 E-1's pool skip removed
(`SquadPerceptionMergeSystem`, the `IsBurst` line) ⇒ **PASS**, reserve sent **40.5 s**, at the platoon **45 s** — your "before" numbers exactly.
With E-1 in ⇒ red.

| why | |
|---|---|
| the platoon asks for reinforcement only with an enemy ELEMENT within 300 m in its picture (`StaffSystems.ComposeSupport`, `ReinforceContactMetres`) and its strength Red/Black | unchanged |
| in the `-dry` editor run the only element near was made by the **Spotter's two mortar rounds landing on the platoon**: heard bursts entered the squad pool as anonymous contacts ⇒ a SPOT at the burst ⇒ "enemy near" | the mortar's launch is heard ~1040 m off — beyond 300 m |
| R-2071 E-1 (user ruling): **a heard explosion is no enemy position** — it never enters the squad pool | ⇒ no element, no request, no dispatch |

⇒ ⭐ **Your option 1:** the check needs a real stimulus — the faked SPOT of a six-man element at (8,110) from Squad 1 that the python check
POSTs. Until `do` can make a SPOT (§9 route-call), please mark the dispatch rows as needing it rather than red.

⚠ Whether a platoon that is only SHELLED (no enemy seen) should ask for help is a fair question, but its home is R-2071 E-4, the shelled
area ([AQ101](../Architect_Question_101_Shelling_Fear_And_Shelled_Areas.md) D, awaiting the user) — not an explosion counted as an enemy.

Also on `behaviors` today (re-check after you merge): `uc-urban-battle` **PASS 5/5** live (CE-2353: one squad "enemy" — a neutral in view no
longer holds a reaction or puts a remounted squad out; CE-2354: a vehicle whose squad went on foot carries on).
