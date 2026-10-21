<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.4 (CE-3283, what the ear tells by proficiency) — the change that regressed
  - ../../DESIGN_Civilians_And_Crowd_Control.md — §5d–§5f (fear from heard shots, panic, shelter, contagion) — the behaviour that broke
-->

# NOTE — coordinator → backend: civilians no longer hear gunfire (CE-4039), and no longer shelter in a house (CE-4040)

**Found while scouting the feature films** (PLAN_Feature_Films): the civilian clips show calm civilians walking up to a gunfight.

## Evidence

| where | result |
|---|---|
| editor preview, `ua-civ-shelter` (fear probe) | a rifleman fires 6 rounds ~27 m away; every civilian's `CivilianState.Fear` **0** and `HeardAt` **0** throughout; at 4 s they switch Routine → **Gather** (the calm civilians' ring round a casualty) |
| live check `utility-demo-check.py --launch ua-civ-shelter` (`--mode all`) | ⛔ **FAIL 7** — no panic, nobody inside, no door opened (was ✅ 15/15 on 2026-10-17) |
| in-process twins, `PostureScenarioTests` at the coordinator head | ⛔ `CE2235_CivRoutineAlarm`, `CE2236_CivCower`, `CE2236_CivShelter`, `CE2237_CivContagion` red (4 of 5) |

## Bisect — `CE2235_CivRoutineAlarm`, `97c2b3259` (2026-10-17) .. head

⛔ **First bad commit: `2abbb0c89`** — *"CE-3283/3284/3285 (Q95 A, B1, B3): sound signatures, the ear's level, real carries, bands"*.

| commit | routine-alarm | cower | contagion | shelter |
|---|---|---|---|---|
| `279220eb3` (its parent) | ✅ | ✅ | ✅ | ⛔ (a SECOND, earlier regression — being bisected separately) |
| `2abbb0c89` | ⛔ | ⛔ | ⛔ | ⛔ |
| `2abbb0c89` with `AcousticPerception.IdentificationOf` forced to `0` (an experiment, not a fix) | ✅ | — | — | ⛔ |

⇒ ⭐ **the family-identification path breaks it.** A listener at Regular or above now gets the sound's family in `Flags` bits 13–15;
`SoundFamily.Rifle = 6` sets bits 14–15, so the `short` goes negative. Somewhere between `AcousticPerception.Sound` and
`CivilianFearSystem` (which frightens only when the decoded `SourceClass` is SmallArms / HeavyWeapon / Explosion) the sound is lost or
mis-decoded for those listeners. ⚠ **The exact line is not pinned** — the decode in `SensorMemoryStage.HearAll` looks right on reading;
the fix and its rail are yours.

## Why the gates did not catch it

The commit's gate list was Toolkits perception, Core, SimHost, NED, Presentation. ⭐ **The civilian twins live in
`Hrot.ClusterRunner.Integration.Tests` (`PostureScenarioTests`)** — the suite that would have gone red. Gate-report row 8 (a change to a
cross-cutting stimulus names the integration suite that exercises it) covers exactly this.

## Ask

1. Fix CE-3283 so a Regular+ civilian (and soldier) still hears small arms as violence; keep Q95 A's identification.
2. Rail it in the feature's own suite: the four civilian twins above, plus a perception-level rail that a Regular listener's heard rifle
   shot decodes as `SmallArms` with family `Rifle`.
3. ⚠ Check the SOLDIERS too: anything keyed on a heard sound's class (react to a heard shot, `tt-heard-shot`, HQ-1 heard launches,
   CE-3276 far listeners) may be blind the same way for Regular+ listeners.

## ② The second regression — shelter (CE-4040)

Bisected with `CE2236_CivShelter` over `97c2b3259` .. `279220eb3` (the hearing break's parent, where civilians still hear):

⛔ **First bad commit: `f1f4c89ba`** — *"CE-2304: a walker in a crowded house — a re-sent move continues, a move getting nowhere ends, an
AI walker stays on the mesh, avoidance skips another storey"* (2026-10-19, behaviors' `sq-defend-house` stall). Its parent `be8c29772` passes.

| hypothesis — ⛔ NOT measured | why it fits |
|---|---|
| **N2** — a walker gaining < 0.25 m in 4 s is a frustration episode (replan, else `FailedBlocked`) | a panicking civilian waits at a CLOSED door while `DoorPassageSystem` opens it; if that wait counts as "getting nowhere", the move fails before the door is open — the live check saw **no door opened** |
| **N3** — an AI walker's avoidance-bent step goes through `KeepOnNavmesh` | the door's passage may not be walkable mesh until it is open, so the step to the threshold is clamped |

⭐ The shelter twin (`PostureScenarioTests.CE2236_CivShelter`) is the rail; it was not in that commit's gates (Fdp.Toolkits navigation +
kinematics). ⚠ `bt-doors` (a visitor opening a closed back door) runs through the same door passage — worth one run.

## ③ Added `2026-10-21` by behaviors — the line, measured by reading (CE-4039; not fixed here, it is yours)

⭐ **`EqsSolverSystem.cs:342` reads flag bit 15 as `AccurateLineOfSightTest.FlagPendingRay` on EVERY template.** A rifle's family is
`SoundFamily.Rifle = 6` (`SignaturesDto.cs:110`); `AcousticSensorGenerator.Sound` writes it at `FamilyShift = 13` ⇒ bits 14 **and 15**. So
for a Regular+ listener every heard rifle shot looks "awaiting raycasts": the solve yields (`_AwaitingRaycasts`) before `_memory.Observe`
(`:357`) and never publishes — the listener hears nothing, `CivilianFearSystem` never sees it. ⭐ Mortar (1), Gun (2), HeavyMg (3) never set bit
15 — which is why `EqsModuleTests.CE3283_TheEar_*` (Mortar only) stayed green. Families 4–7 (AaGun, Launcher, Rifle) are all affected, soldiers
included (Ask 3).

| a fix that was drafted and withdrawn (the user: yours) | |
|---|---|
| in the solver: the pending check only when the template RUNS an `AccurateLineOfSightTest` (none of the acoustic template's phases do) | a `HasRayTest(in template)` over its four test arrays |
| the rail, in the ear's suite: `EqsModuleTests` — `HearingRig` given a shooter type and a listener x; an `InfantrySoldier` (rifle) at 200 m; Militia / Regular / Veteran ⇒ one Shot, `SmallArms`, family 0 / Rifle / Rifle | red before the fix for Regular and Veteran (by reading — not run) |

⚠ An alternative is moving the family off bit 15 (the flags are full: bits 0–7 tests, 8–9 kind, 10–12 class, 13–15 family) — the solver
guard is the smaller change.

_behaviors lane, 2026-10-21_
