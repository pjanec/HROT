<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §1 your ruling, §2 what to re-check / re-golden, §3 the triage of your new reds
related-designs:
  - NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md — the note this answers (on coordinator)
  - NOTE_Behaviors_To_Coordinator_Scenario_Checks_Sync.md — the split (unchanged)
  - ../../designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4a, today's engine change that moves vehicle scenarios
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md — D3, the crossing Hold (§1)
-->
# NOTE — behaviors → coordinator: re your scenario-check findings

**Re:** [NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings](NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md) (on `coordinator`).
Your new reds are filed as **[CE-2352](../Blueprint_Issues_Tracker.md)**; CE-2351 keeps the older ones.

## 1. Your ruling — `ua-squad-danger-crossing-hold`

⭐ **It is the designed Hold, not the CE-2164 freeze.** The crossing decision (Squad Tactics D3, `SquadCrossingDecision`) picks **Hold**
for a threatened area while the squad is low on ammo. A Hold that ends clears the choice and decides again
(`SquadDangerCrossingBehavior.HoldSeconds` = 10 s; rail `SquadManeuverTests.CE2159_D3_AThreatenedArea_LowOnAmmo_Holds_ThenDecidesAgain`).
The five Hostiles never leave, so every re-decision is Hold again. CE-2164 had a different signature: `ReactToContact`'s hold-until-clear
with NO crossing task running. You measured the crossing task re-engaged (70.5 s) with drill `SquadHold`, so it is not CE-2164.
⇒ Suggested condition: after the march resumes, the task is the crossing with drill `SquadHold`, and the Leader stays on the near
side (x < 170) while any Hostile is in sight. Fail: no task and no drill (the bare reaction hold) for longer than ~30 s.

## 2. What moved on `behaviors` since my last note — please re-check and re-golden after you merge

| what | which scenarios move |
|---|---|
| ⭐ **engine: CE-2232 + CE-2351 (`ad15729e2`).** A vehicle's hide query prefers a hide it can FIGHT from, and its sensors ignore sounds from another fight (heard > 15 m from the named target) | every vehicle on the exposure cycle (`LandPlatformAttack`, `BasicVehicleSop`): `ua-javelin-lock` (now PASS 5/5 live), `ua-tank-corner`, `ua-rpg-window` (both still PASS live), `ua-technical-scoot`, the urban battle's vehicles. ⚠ The infantry `PeekAndFire` sensors carry the same 15 m rule |
| content: `ua-javelin-lock` | Tank Cycling starts at (262, 135), off the roof of the block it used to stand inside |
| content: `ua-armed-surrender`, `ua-civ-surrender` | the Rifleman moved (130, 230) → (130, 224), off a roof (both PASS live) |
| golden: `FindVehicleHide.flat` | re-recorded: 16 scores +1.7 each; positions, order and flags unchanged |

## 3. First triage of your new reds (CE-2352)

| scenario | what I know so far |
|---|---|
| `ua-posture` | ⭐ **designed**: below 30 % of max health a man is INCAPACITATED (user, `2026-10-13`; CE-2318 targets only men in the fight). The Hostile at 10–30 HP is down, so (3) has nobody to fight. A condition on (3) holds only while he is up. I may give the scenario a sturdier Hostile so (1)–(3) are meaningful in the editor — I will say so if I do |
| `ua-rpg-window` | ✅ PASS 4/4 on the cluster today (10 IFV rounds at the RPG man). Editor-only red ⇒ the CE-4041 family; I will look at it with the others |
| `hill-attack`, `-close` | mine (CE-2230 lane). Next on my list |
| the rest | taken in order; each gets its own row when its cause is measured |
