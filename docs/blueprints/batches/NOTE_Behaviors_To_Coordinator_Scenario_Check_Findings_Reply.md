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
| ⭐ **engine: R-2071 E-1 (CE-2348 ③, user ruling).** A heard explosion is no enemy: it refreshes no known contact, never enters the squad pool, counts no enemy danger, and is no fight target; it still alerts (E-2) and TakeCover still hides from it | any scenario with incoming fire: `uc-urban-battle`, `uc-convoy-hit(-ifv)`, the mortar / RPG demos. Re-verify after the merge |
| golden: `FindVehicleHide.flat` | re-recorded: 16 scores +1.7 each; positions, order and flags unchanged |

## 3. First triage of your new reds (CE-2352)

| scenario | what I know so far |
|---|---|
| `ua-posture` | ⭐ **designed**: below 30 % of max health a man is INCAPACITATED (user, `2026-10-13`; CE-2318 targets only men in the fight). The Hostile at 10–30 HP is down, so (3) has nobody to fight. A condition on (3) holds only while he is up. I may give the scenario a sturdier Hostile so (1)–(3) are meaningful in the editor — I will say so if I do |
| `ua-rpg-window` | ✅ PASS 4/4 on the cluster today (10 IFV rounds at the RPG man). 📐 **Editor, probed**: a RACE, not a fault. Two RPG hits kill an IFV; IFV 1 (hit 4.8 s) drives to cover and hides until ~15 s, the RPG's second rocket (15.6 s) kills it ~1 s before its first round (in a real-time editor run it fired at 16.9 s and hit him); IFV 2 is still in its hide cycle when it is hit. A 100 s window changes nothing. On the cluster the IFVs win it (10 rounds). ⇒ the designed hide-first cycle against a fast RPG; if the demo must show return fire every run, the scenario needs sturdier IFVs or a slower RPG — your call on the condition, mine on the content if you want it |
| `ua-javelin-lock` | ✅ in the editor at HEAD all five of your conditions PASS; only the golden differs (it predates CE-2232) — please re-record it after the merge |
| `hill-attack`, `-close` | 📐 **cluster vs editor**: on the cluster (`--mode all`) the commander dispatches wave 1 at 30 s and its tanks fire (3 rounds in 150 s). In the editor the waves dispatched but no tank reached its firing point: ✅ **FIXED (CE-2352)** — the exposure clock treated the targets as seeing the tank on flat ground, though their eyes reach 100 m; now in the editor the tanks fire at 64.9 s and an M1 is destroyed (your ① ② PASS). ⚠ **Your check (3) can never pass:** the commander's baseline is at **x 523–532** (its params, read live), not x < 480 (the start column) — please change it to x < 540 or to "back within 15 m of the baseline" |
| the rest | taken in order; each gets its own row when its cause is measured |

## 4. Not mine, seen in my gate

`GhostProtocolTests.Execute_RespectsTimeBudget` (Fdp.Toolkits) fails deterministically since CE-4044 (`fd59c8e50`, merged with `coordinator`): ghost promotion is now budgeted by COUNT, and the test still asserts a 2 ms TIME budget. Backend's to update.
