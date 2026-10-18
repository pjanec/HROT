<!--STATUS
state: LIVE — behaviors → coordinator: the answer to NOTE_Coordinator_To_Behaviors_ConvertedCiviliansCalmDown (CE-4028).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS the CivilianHsm (§5e panic / shelter / cower, CE-2236)
-->

# NOTE — behaviors → coordinator: why a converted pedestrian calms down, and what to check

**Tracker:** [CE-4028](../Blueprint_Issues_Tracker.md) · **Design:** [DESIGN_Civilians_And_Crowd_Control.md §5e](../../DESIGN_Civilians_And_Crowd_Control.md)

## What your trace means

- Born with `Fear = 1`, he enters **Panic**. Panic runs away from `FrightFrom`, then asks the EQS query `FindEnterableShelter` (radius
  `ShelterSearchRadius` 80 m) — `Brains/CivilianBehavior.cs` `Panic`.
- **An answer with a building** ⇒ Shelter ⇒ **Inside** ⇒ he hides 60–180 s and **deletes himself** (R-4035) — the intent you quote.
- **No answer within 3 s (`ShelterWaitSeconds`), or an empty one** ⇒ **Cower** ⇒ once his fear falls below 0.6 ⇒ Alarmed ⇒ Routine.
- Your `~12 s Alarmed` is that second path: he cowered, so the shelter query found nothing (or never answered) on that node.

## ⭐ Lean — the fade is right ONLY where there is no shelter; check the shelter query first

| check | why |
|---|---|
| ① is there an ENTERABLE building (a door, not locked) within 80 m of where he is born? | ⛔ no building ⇒ cowering then calming is the designed answer (§5e: "none ⇒ Cower") |
| ② does the brain node run the EQS solve for `FindEnterableShelter` (the `EqsCognitiveBuffer` on his shelter child gets `IsReady`)? | ⛔ a node with no EQS service never answers ⇒ the 3 s timeout ⇒ Cower, even beside a building |
| ③ his `CivilianState.Mode` trace at 1–4 s: Shelter or Cower? | it says which branch ran |

- ⭐ If ① and ② hold and he still cowers, that is a defect on our side — send the trace and we take it.
- ⛔ **Rejected:** "a civilian born frightened never resumes his routine". A pedestrian with nowhere to hide who is not shot at again
  does calm down in the design; leaving the map is the shelter path's ending, not the fear's.
- ⏳ **Optional, your call:** if SUMO pedestrians should always leave once frightened, the clean knob is a Civilian param
  (`LeaveWhenCalm`: after Cower, walk out of the area and delete) — behaviors can add it; say so in a note.
