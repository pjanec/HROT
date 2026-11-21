<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — ⭐ THE design: §6.1 the shared contract, §6.3 YOUR tasks, §8 the review (AR-7, AR-8, AR-13, AR-14, AR-20 are yours)
  - ../audio/DESIGN_FPS_Audio.md — the sound as heard (the mixer, the beds)
-->

# HANDOFF — coordinator → `ui`: the animals' bodies, gaits, flock, rider and sound (CE-4623, tasks AU-1…AU-8)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `d6d76b479`**; ⛔ scope FROZEN there.

🔒 User, `2026-11-16`: *"the UI to implement their visuals, and they all need to know upfront the TKB and this entity type IDs so they can work
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
`git fetch origin coordinator ui backend`; on `ui`, merge `origin/coordinator`; push `chore: started animal visuals at <sha>`. Status:
`STATUS_Ui_Animals_Visuals.md`. ⭐ backend builds the types at the same time and sends `NOTE_Backend_To_Ui_Animals_Contract.md` (the component
layouts) as its first commit — until it lands, build against stand-ins with the names below.

## 1. The shared contract — §6.1 of the design, fixed for BOTH lanes
| you READ (backend declares) | you DECLARE |
|---|---|
| types **700–711** (cattle 700 · sheep 701 · herd / riding / war horse 702 / 703 / 704 · dog 705 · cat 706 · bird flock 707 · bird 708 · hen 709 · pig 710 · goat 711), area **712**; classes `Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird` `Poultry` `Pig` `Goat`; `LocomotionClass.Quadruped` / `.Bird`; the replicated **`AnimalState`** (Mode, Fear band) and **`FlockState`** (Count, Seed, Mode); the horses' seats (703: 0 rider, 1 second rider; 704: 0); the sound types **7800–7895** | ⭐ `VisualFamily.Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird` `Poultry` `Pig` `Goat`, mapped from the CLASS names in `FamilyOfClass`, and their kits; the 2-D icons; the clips / placeholders for 7800–7895 |

## 2. The tasks — the design's §6.3, in order
⭐ AU-1 the MOCKUP first · AU-2 the families and kits, icons, thumbnails · AU-3 the gaits (a per-leg phase, walk → gallop, the jump tuck, wings,
the hen's flutter) · AU-4 the flock in closed form · AU-5 the dead animal (lying, never burning) · AU-6 the rider (pose, first person, firing
while riding, the bolt's HUD cue) · AU-7 the animal-area tool and form · AU-8 the sound as heard (the mixer, the birdsong bed extended to orchards
/ parks). Each "done when" is in §6.3.

## 3. Gates
T-1 first (the 3-D model, body, frame and audio suites); display rails under `xvfb-run -a` (a skip is not a pass); the fidelity side by side
with the mockup (R-4146, in the report, ⛔ not in git); each new rail red-proved; 0 B per steady frame (R-2084).

## 4. The morning report — `STATUS_Ui_Animals_Visuals.md`
per task, the fidelity side-by-side, every lane decision with what would change it, the gate-report contract (rule 8), ids.
