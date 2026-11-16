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
| AU-6 rider | built (live drive waits for backend's type 703 + possession); ⭐ review fix: the mounted legs SPLAY over the ±0.35 m barrel to the stirrups (`BlockFigure.MountLeg`) — the first legs went straight down inside the horse's body; the seat height was right (the rail measures it against the back) | `TheMountedRider_*`, `TheRider_IsDrawnInTheSaddle_*` (fires from the saddle), the bolt cue `IsBolted` (0 B) |
| AU-7 area tool + form | built against a TEMPORARY stand-in component (id 511) | `AnimalAreaDetailsViewTests`, `TheAnimalArea_*`; arming the tool needs backend's TKB 712 |
| AU-8 sound as heard | built | `AudioTests.CE4623_*` (4) |

## Binds to backend
| bound | at |
|---|---|
| `AnimalState` (501), `FlockState` (502), `AnimalMode`, `FlockMode`, `AnimalFlags.Bolting`, `LocomotionClass.Quadruped / Bird`, `HerdMember` (503) | merged `origin/backend@3fb384efa`; the stand-in structs deleted, `AnimalReader` reads backend's components |
| classes `Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird` `Poultry` `Pig` `Goat` -> `FamilyOfClass`; types 700-711 from `AnimalTkbCatalog` (the rails draw the REAL types; the size is scaled to the TKB length; the horse's rider seat is backend's 703 seat) | merged `origin/backend@5c797b66e` (AB-1, AB-2); the DIS stand-in `AnimalOfDis` deleted; the 2-D outline reads the family; the CE2138 DIS-names golden gained backend's animal classes |
| still stand-ins | the area component `AnimalAreaDef` (temporary id 511) until backend declares TKB 712's component |
| finding for backend | the 703 / 704 rider seat x 0.15 is over the withers of a nose-to-tail box; honest value about -0.15 (the kit is drawn `OriginAhead` = 0.30 m forward of its box so the rider sits on the saddle) |

## Lane decisions - for the user's review
(see the end of this file; each says what would change it)

| id | decision (taken overnight, by measurement) | what would change it |
|---|---|---|
| L-1 | the nine kits come from ONE parametric builder (`MetreKit` + a `QuadSpec` row per species): each kit's box is MEASURED from its parts, the mockup draws the SAME table | the user wants hand-modelled meshes (a real art pass replaces the kit, the gait roles stay) |
| L-2 | an animal keeps its kit's proportions: scaled UNIFORMLY to backend's TKB length (its width / height are first estimates); the horse's back is set to backend's seat height (1.35 m rider hips) | backend tunes the species table (AR-21) or wants width / height honoured exactly |
| L-3 | animal sounds use the existing `Ambient` sound category (no new `SoundCategory` — it would add a volume row to every audio panel) | the user wants a separate "Animals" volume slider |
| L-4 | the call rhythm (`AnimalSoundCatalog.CallsMade`) is the operator's closed-form timing; backend's AB-8 must share it (one rule, AR-7) — until then they are two copies | backend publishes its timing function: delete mine and call theirs |
| L-5 | rest calls throttle per herd (2.5 s), alarms per herd (1 s), a bark never; at most 3 animal sounds a frame; calls stop for ~6 s after shooting | the sound design wants a chorus |
| L-6 | the birdsong bed is LIFTED (up to +60 %) by perched flocks / birds within 80 m, never replaced: the old always-on bed stays (it already falls silent under fire) | the user wants no birdsong away from trees |
| L-7 | one species per animal area (a mixed pen = several areas) | backend's area component carries several species |
| L-8 | a saddled (riding / war) horse stands head-up — it does not graze with a rider on it | backend sets Guard / Alert itself |
| L-9 | a flock changing mode (perched -> flying) snaps between layouts (no blend): the flock entity itself moves away at once | the take-off looks wrong live (a 0.4 s blend is one line in `DrawFlock`) |
| L-10 | no saddle drawn on the horse kit (a herd horse would wear one) | a "tack" flag on the riding type |
| L-11 | the area form's temporary component id is 511 (506-511 unallocated; ui's block 460-475 is exhausted) | backend declares the real one: delete `AnimalAreaDef`, rebind `AnimalAreaOps` |

## Gate report (rule 8)
| gate | command | result | base |
|---|---|---|---|
| T-1 the animals' own rails | `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter AnimalModelRail\|CE4623` (test project built, `--no-restore` after a restore) | `AnimalModelRail` 18/0/0 (frame tests ran, not skipped), `AudioTests.CE4623_*` 4/0/0 | new |
| area form | `xvfb-run -a dotnet test Hrot.Editor.AiShared.Tests --filter AnimalAreaDetailsViewTests` | 2/0/0 (the frame test ran) | new |
| T2 the whole Presentation suite | `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build` | 2206 pass / 8 fail / 11 skip. Pre-existing at the base sha `5c60f4d75` (clean worktree, isolated): CE1309, CE1086, CE4388. Four frame rails (CE1039, CE4582, P7B, TheBuildingDamagePlan) fail only inside the full run and pass alone, also in my tree. E2b (road vertices 1.1 m off the ground) fails in my tree and was SKIPPED at base - terrain code, no animal code touched, base unverified | |
| T2 AiShared | `dotnet test Hrot.Editor.AiShared.Tests --no-build` | 2355 pass / 4 fail: `NoFileKeepsAPrivateRepoRootWalkUp` and `NoProductionFileSpellsANewContentRoot` and `SaveSlot R8` fail at base too (MotorcycleModelRail and the terrain files are the offenders; mine was fixed); `EngineEditorTheme...NoContext` passes alone | |
| red-proofs | walk's four leg offsets flattened to 0 -> `TheGait_*` FAILS (restored) | | |
| working tree | clean after the runs; only intended files committed | | |
| ids | no tracker rows allocated; component id 511 claimed TEMPORARILY (`AnimalAreaDef`); sound ids 7800-7895 are backend's | | |
