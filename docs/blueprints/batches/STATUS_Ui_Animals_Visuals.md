<!--STATUS
state: LIVE - overnight, unattended (CE-4623, AU-1..AU-8)
updated: 2026-11-16
current-answer: the task table, then "Binds to backend", then the lane decisions
related-designs:
  - ../../designs/animals/DESIGN_Animals.md - the design (§6.1 contract, §6.3 tasks, §8 review); as-built in ../../DESIGN_Map_3D_Mode.md §6al.3
  - HANDOFF_Coordinator_To_Ui_Animals_Visuals.md - the dispatch
  - NOTE_Backend_To_Ui_Animals_Contract.md - backend's component ids and layouts (bound, below)
-->

# STATUS - ui: animal visuals (CE-4623, AU-1..AU-8)

| task | state | evidence |
|---|---|---|
| AU-1 mockup | built | `docs/designs/animals/animals-visuals.svg` (script `scripts/gen-animals-mockup.py`) |
| AU-2 families, kits, icons | built (stand-in family keying; thumbnails wait for backend's types) | `AnimalModelRail` (kits, DIS to family, 2-D outlines); picker glyph sources `assets/icons/src/_cattle ...` + PNGs |
| AU-3 gaits | built | `AnimalModelRail.TheGait_*`, `TheTracker_*` (0 B); red-proof: the walk's offsets flattened fails `TheGait_*` |
| AU-4 flock | built | `AnimalModelRail.TheFlock_*` + the frame rail (60 birds, 40 after the count falls, the same picture after a seek) |
| AU-5 dead animal | built | frame rail: `DeadAnimalsDrawn`, no wreck colour; `Burns()` excludes animals |
| AU-6 rider | built (live drive waits for backend's type 703 + possession) | `TheMountedRider_*`, `TheRider_IsDrawnInTheSaddle_*` (fires from the saddle), the bolt cue `IsBolted` (0 B) |
| AU-7 area tool + form | built against a TEMPORARY stand-in component (id 511) | `AnimalAreaDetailsViewTests`, `TheAnimalArea_*`; arming the tool needs backend's TKB 712 |
| AU-8 sound as heard | built | `AudioTests.CE4623_*` (4) |

## Binds to backend
| bound | at |
|---|---|
| `AnimalState` (501), `FlockState` (502), `AnimalMode`, `FlockMode`, `AnimalFlags.Bolting`, `LocomotionClass.Quadruped / Bird`, `HerdMember` (503) | merged `origin/backend@3fb384efa`; the stand-in structs deleted, `AnimalReader` reads backend's components |
| still stand-ins | the family keying by DIS (`VisualFamilies.AnimalOfDis`) until the classes exist; the area component `AnimalAreaDef` (temporary id 511) until backend declares TKB 712's component; type 703's seats in the rider rail |

## Lane decisions - for the user's review
(see the end of this file; each says what would change it)
