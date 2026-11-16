<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — ⭐ THE design: §6.1 the shared contract, §6.2 YOUR tasks, §4 / §8 the decisions (the §8 AR rows supersede the §4 rows they touch), §4a the ids, §5 the UML
-->

# HANDOFF — coordinator → `backend`: the animals, the simulation and every behaviour (CE-4623, tasks AB-1…AB-10)

**For:** the `backend` lane. **From:** coordinator. **Dispatched at `d6d76b479`**; ⛔ scope FROZEN there — later documents are FYI; one that
invalidates a task ⇒ STOP that task, do the rest (R-106).

🔒 User, `2026-11-16`: *"issue a handoff for the backend lane to implement all the animal stuff. Movements, the behavior to implement their
behaviors, and the UI to implement their visuals, and they all need to know upfront the TKB and this entity type IDs so they can work
independently"* · decisions R-4266…R-4269.

## ⭐⭐⭐ OVERNIGHT, UNATTENDED — read this before anything else
| the rule | |
|---|---|
| ⭐⭐⭐ **NOBODY IS THERE TO ANSWER** — never ask, never wait, never end a turn on a question | every open choice: measure it, decide by the claim table (code + design), build it, record an `R-` lane decision *"for the user's review"* with what would change it |
| ⭐⭐⭐ **DO NOT STOP UNTIL EVERY TASK IS BUILT, GATED AND REPORTED** | a task finishing is not the end — go straight to the next |
| ⭐⭐ **a failed premise stops ONE task, never the batch** (R-106) | write why in the status file, do every task that does not depend on it |
| ⭐⭐ **a defect is fixed where it lives**, a rail in that feature's own suite (T-1), its design folded (obligation ⑤) | a large one filed as a row and worked around |
| ⭐⭐ **the other lane may not have landed** — build against a STAND-IN with the fixed names and ids of §6.1, keep going, re-bind when its work merges; `git fetch` the other lane at each task's end and merge it when it has what you need | |
| ⭐ **commit, push and update the status file at every task's end** | a restart or a compaction resumes from it |
| ⛔ **you may NOT** reverse a user ruling, change an id or a name fixed in §6.1, or build the other lane's half | file it as a finding instead |

## 0. Start
`git fetch origin coordinator backend ui behaviors`; on `backend`, merge `origin/coordinator`; restore the test projects you run; push
`chore: started animals at <sha>`. Status: `STATUS_Backend_Animals.md`. Read the design END TO END first (R-129).

## 1. The shared contract — §6.1 of the design, fixed for BOTH lanes
⭐ You DECLARE: the TKB types **700–712**, the DIS types, the species classes (`Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird` `Poultry` `Pig`
`Goat`) and the parents (`LandAnimal` = `3.1.*.200-209`, `AirAnimal` = `3.2.*.205`), `LocomotionClass.Quadruped` and `.Bird`, the replicated
**`AnimalState`**, **`FlockState`**, **`HerdMember`**, the horses' seats (703: 0 rider firing while riding, 1 second rider; 704: 0), the sound types
**7800–7895** (`7800 + species·8 + line`) and the area **712**. ⛔ NOT the 3-D families (`VisualFamily.*`) — ui declares those.
⭐ **Your FIRST commit after the start marker**: a note `NOTE_Backend_To_Ui_Animals_Contract.md` giving ui the component ids and field layouts of
`AnimalState` / `FlockState` / `HerdMember`, the `LocomotionClass` values and the HUD reason for a bolt — so ui can bind early.

## 2. The tasks — the design's §6.2, in order
AB-1 the taxonomy · AB-2 movement (Quadruped, Bird, the ~40 Pedestrian sites audited, SUMO rows, the nav layer) · AB-3 fear (ONE shared fear, the
flight zone, civilians unchanged) · AB-4 the brain, herds, the grazing / perching layer, night · AB-5 the species plays (dog, cats, birds, hens,
pigs, goats, the riding / war horse) · AB-6 jump links · AB-7 the panic breach · AB-8 sounds (sim side) · AB-9 the animal area · AB-10 the demo
`farm-under-fire`. Each "done when" is in §6.2.
⭐ Cross-lane edits are allowed and said (`Hrot.AI.Behaviors` for the brains and plays; the terrain lane's files for the grazing layer and the
jump links; `Hrot.Traffic` for the SUMO rows).

## 3. Gates — every task
T-1 first (each feature's own suites — civilians, crowds, perception, the fear, kinematics, traffic, breach, possession); every behaviour rail
with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py
--threads 1,4,8`; 0 B per steady frame (R-2084); invariant output (R-4221); new components unmanaged and fixed before their first recording.
⭐ AB-2 and AB-3 touch shared seams — re-run the civilian, crowd and vehicle scenarios and report them unchanged.

## 4. The morning report — `STATUS_Backend_Animals.md`
per task built / stopped and the design folded · every lane decision with what would change it · the gate-report contract (rule 8) · ids allocated.
