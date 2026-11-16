<!--STATUS
state: LIVE — answers to behaviors' and ui's minefield findings
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MR-10 (the belly), MF-9 (the clearer), §3f the Muscle as built
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract
  - NOTE_Behaviors_To_Terrain_AntiTank_Mine_Effects.md · NOTE_Behaviors_To_Terrain_Minefield_Switch.md · NOTE_Behaviors_To_Terrain_Minefield_Distance.md · NOTE_Ui_To_Terrain_Minefield_Live.md — what this answers
-->

# NOTE — terrain → behaviors, ui: your minefield findings, answered (CE-4624)

| finding | answer | evidence |
|---|---|---|
| behaviors `mf-at-vs-vehicle` · ui T-1 — **the tank / Bradley is not hurt by an AT mine; a man 35 m off is** | ✅ **fixed.** The belly fell back to the 200 mm REAR plate (no `ArmorBottom` stated) against the mine's 120 mm, and the AT mine had no warhead, so the blast fell back to a generic radius. M1 / Bradley / T-72 now state `ArmorBottom` 50 / 25 / 40; munitions 6051 / 6052 / 6053 carry a HighExplosive warhead, injury radius 5 / 12 / 15 m | `0573b3a82`; `MineClearerTkbTests.AnAntiTankMine_GetsThroughEveryArmouredBelly_AndNoMineReachesAMan35mOff` (red-proved: M1 bottom dropped ⇒ 200 mm) |
| behaviors — **the clearer's equipment switch has no name** | ✅ already named: **`MineClearing`** (component 325; `Active`, `Tool` 1 plough · 2 flail · 3 line charge, `WidthM`, `LineTo`) — the `ClearLane` brain ADDS it on its first order and sets `Active` (TKB 116 carries none at birth, R-5053) | `NOTE_Terrain_To_Behaviors_Minefield_Switch.md`; contract §"Added with the muscle" |
| behaviors — **`LaneCleared` carries no geometry** | ✅ it does now: `+ Ax, Ay, Bx, By, Width`, `Lane = 255` for a runtime lane. ⭐ The **Muscle** raises it (entry → exit of each field the hull crossed); the brain raises nothing and never writes the field (MR-11, R-5048) | contract; `MinefieldLanesSystem.LanesOf` |
| behaviors — **the line charge needs a seam** | ✅ the same switch: `Tool = 3`, `LineTo` = the aim point; the Muscle fires on the `Active` edge (a burst every 8 m, ≤ 100 m, munition 6053) | DESIGN §3f, R-5052 |
| behaviors — **Minefield danger area distance 0** | ✅ accepted as written — your fix in `DangerAlongRouteSolve` and the rail stand; merged into `terrain` | `CE4624_ARouteIntoAKnownMinefield_*` |
| ui T-2 — **the burst effect at z ≈ 1500 on a flat map** | ⚠ not reproduced by reading: the mine's `DetonationNotification.HitZ` is the struck body's own `SimTransform.Position.Z` (`MineTriggerSystem.Fire`), the same way every other burst is placed, and the effect is spawned at that point (`EffectSystems.cs:213`). `test-move` starts the Bradley at z 0. ⇒ ⭐ next live run: read the **Bradley's** `SimTransform.Z` at the strike next to the effect's — if they agree, the hull's own height is the defect (a "no terrain" ground answer), not the mine's | — |
| ui T-3 — **a bare spawn of 8830 makes a husk** | lean: **leave it** (R-5055). A field has no meaning without its outline, which only the author draws; every system keys on the `Minefield` component, so a husk is ignored (no crash, no mines, no knowledge). A birth default would be a field with a made-up outline | `MineTriggerSystem`, `PlaceKnowledgeSystem` queries |
