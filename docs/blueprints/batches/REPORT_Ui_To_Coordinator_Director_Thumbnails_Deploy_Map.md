<!--STATUS
state: LIVE
updated: 2026-11-05
current-answer: the whole file (a batch report)
stale-below: none
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Film_Director.md §8.5 — CE-4267 as built
  - docs/DESIGN_Add_Entity_Picker.md §2i — CE-4268 as built
  - docs/DESIGN_Player_Join.md — CE-3370 (S3-d) and the R-3076 key fix
-->
# REPORT — ui → coordinator: director, thumbnails, deploy map *(answers [the handoff](HANDOFF_Coordinator_To_Ui_Director_Thumbnails_Deploy_Map.md), dispatched at `271d9331c`)*

| item | state | commits |
|---|---|---|
| ① CE-4267 the director plays almost no takes | ✅ DONE — 11 of 11 load and run, 0 load failures | `c3c60cd57` · `c67042c88` (cross-lane: FDP recorder) · `35fe05b3b` · `3e42c3ff1` · `f0c92bad1` |
| ② CE-4268 thumbnails in both pickers | ✅ DONE — generator + lookup step ③; whole catalogue NOT run | `f12f487d9` · `af6e845e6` · `58bacbd68` · `805b3a1e9` · `de8694440` |
| ③ CE-3370 spawn area on the Player's 2-D map | ✅ DONE | `575d460ab` |
| FYI R-3076 Tab = scoreboard, J / Shift+J = next / previous man | ✅ doc fix | `b3fb8f2bb` |

## ① CE-4267 — per take, before / after *(`director-check.py live highlights`, the VM, Xvfb)*

| take | before | after |
|---|---|---|
| fire-and-movement · bc-house-clear · rpg-window · drone-shelter · air-strike | fresh editor: *"did not load within 30 s"*, 0 steps · operable editor: *"no unit named X"* (ran on the previous world) | **ok** |
| armed-bomber · civ-police-line (`m2`) · weapon-choice (`tow`, `kill`) · hill-attack · air-assault · airborne-drop | the same | runs its own world; **beat missing** ⇒ take content, appended to CE-1241 |

Five causes (each railed, each rail red-proven by a compiling mutant) — [Film Director §8.5](../../DESIGN_Film_Director.md):
the door's question went to an invisible UI modal and `IsBusy` dropped later loads · the readiness probe accepted the old world ·
a failure was replaced by the next title in the same frame · ⭐ found live: leaving a preview with a held route crashed the editor
(`FdpAutoSerializer` vs `EntityLink`'s readonly fields — **backend's file, fixed here, say so**) · ⭐ found live: each take after a
played one met its own preview still leaving (now a wait).

## ② CE-4268 — as built *([§2i](../../DESIGN_Add_Entity_Picker.md))*

📐 0 of 127 scenarios had a preview; `map.json` had no rules. ⭐ Lean changed in one place: `/director/stills` is Replay Browser only,
so `scripts/thumbnails.py` drives the editor's own routes (`/scenario/load/edit`, `/entities/spawn`, `/film/shot`, `/film/capture`);
PNGs go to the NAS root only (none in git). `ThumbnailMap` step ③ `generated/<TKB Name>.png` fills only a gap. `/film/shot {spec}` now
honours `spec.layers` through the director's one override (the unit cards were in every capture; the user's layer file is never
written — rail). ⏭ Run it over the whole catalogue (127 scenarios, 278 types) under Xvfb when wanted — not done here.

## Gates

| gate | result |
|---|---|
| `Hrot.Editor.Tests` `~Director` (built, then `--no-build`) | 66 passed / 1 skipped / 0 failed |
| `Fdp.Core.Tests` `~FdpAutoSerializer` | 38 / 0 |
| `Hrot.Presentation.Tests` `EntityTypeCatalogTests` | 40 / 0 |
| `Hrot.SimHost.Tests` `GZH011_Tests` | 16 / 0 |
| CE-3370 (worker) | 48 passed, 2 red-proofs |
| `tracker-counts.py --check` | OK |
| reds | none new; ⚠ CE-3370's backend "spawns inside the area" suites were not run, and the Player's replica world holding `SpawnArea` entities is not verified live |

Ids allocated: none (every row came from the handoff).
