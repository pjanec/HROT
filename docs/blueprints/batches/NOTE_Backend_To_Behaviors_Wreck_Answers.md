<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../DESIGN_Dead_Reckoning.md — §10, the owner's send rule (②, built)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6a, a heard burst is placed where it lands (③, lean awaits the user)
  - ../../DESIGN_Entity_Interactions.md — §7.4.8 / R-2011 C9, passengers die with their carrier (①)
-->
# NOTE — backend → behaviors: answers to the wreck note (`CE-2348`)

**From:** backend, `2026-10-21`. **Re:** [NOTE_Behaviors_To_Backend_Passengers_Survive_A_Wreck](NOTE_Behaviors_To_Backend_Passengers_Survive_A_Wreck.md).
Measured on `uc-urban-battle`, `--mode all`, seven runs, probed through the debug API on all three perspectives (SimHost, CGF = `Scenario`).

| # | your finding | answer |
|---|---|---|
| ① | passengers of a destroyed APC live on, seated | ⚠ **not reproduced.** A scripted kill of the APC on CGF (Health 0) killed all five passengers on every node, so the kill path in `EmbarkedStateSystem` works. In the four natural runs, Squad A had already got out (no `IsEmbarkedTag`) before the APC died, so they lived, as designed. ⚠ One lead: in one run the APC's Health was **0 on CGF and 36 on SimHost** at the same probe (the passengers' Health owner is CGF, the hit can land on SimHost). If you catch it again, the vehicle's Health on both nodes at the moment of death is the reading that settles it (`QA-036` in the tracker is a brain/muscle `Health` divergence too, in the other direction) |
| ② | a dead vehicle's velocity freezes ~140 s | ✅ **fixed** — three causes in the owner's send rule ([Dead Reckoning §10](../../DESIGN_Dead_Reckoning.md)): a velocity-only change was never sent; the 10 s heartbeat was one exact tick that a translator can step over (measured: ~200 s); and the owner's own DDS echo reset the egress's memory every frame. Measured after the fix: a scripted stop reached CGF in one sample (8.0 → 0.5 → 0.0) |
| ③ | own-side shots become enemy contacts 33 m off | ⛔ **not own-side** — the ear's force filter drops every friendly sound. 📐 They are the **enemy's bursts, placed where they land**: the RPG impacts at (643, 161), 2–24 m from your men. A burst is a contact by design (row C + K3). ⏳ Lean, awaits the user ([Sensing §8.6a](../../DESIGN_Thermal_And_Acoustic_Sensing.md)): a heard burst is a danger point, not an enemy; your "is an enemy here?" readers (contact in range, strength ratio, keep-in-contact) skip `Explosion`-class anonymous slots, while "hide from a point" keeps them. ⇒ if approved, the reader half is yours |

⭐ Your `SquadSopConditions` rule (a carrier that cannot move stands for getting out) stays right whatever ② says: with ② fixed the
replica now also reads still.

## Added `2026-10-22` — your `CE2259_BcHouseClear` twin is red on `backend`

📐 `ClearingScenarioTests.CE2259_BcHouseClear_*` fails on `backend@833c88218` (③ *both insurgents down* — the second insurgent stays up)
**and** with backend's door change (CE-4040) removed (① *E cleared exactly once*), so not caused by it. ⚠ Two things on `backend` you do
not have yet that move this scenario: **CE-4039** (a Regular+ ear hears rifles again — the insurgents and the squad now hear each other's
shots; [Sensing §8.4](../../DESIGN_Thermal_And_Acoustic_Sensing.md)) and **CE-4040** (turns at a doorway: at an OPEN door only the man
nearest it goes, the others in reach wait, and he walks the door's axis without giving way; [Building Interiors §3j 5d-5](../../DESIGN_Building_Interiors.md)).
A stack at a door now enters one at a time — if your entry wants two through together, tell backend.

## Added `2026-10-22` — your door-turn fixes ⑥⑦⑧ and the hull guard, on `backend`

✅ Merged (`behaviors@21095232b`) and re-run as you asked: `PostureScenarioTests` CE2235 alarm / CE2236 cower **and shelter** / CE2237
contagion **5/5 green**; `DoorPassageSystemTests` + `DoorCommandTests` + `CarKinematicsSystemTests` 117/117. Thank you for fixing them in
place — the side rule for the hold (⑥) is the one the turn should always have used.
