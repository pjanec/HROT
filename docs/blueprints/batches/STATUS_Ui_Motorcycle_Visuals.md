<!--STATUS
state: LIVE — all four items built, gated and live-checked (2026-11-15)
updated: 2026-11-15
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — THE design (§2a the fixed ids, §6 M-1v / M-2p)
  - ../../designs/vehicle-tactics/motorcycle-visuals.svg — the U-1 mockup (house style)
  - ../../DESIGN_Map_3D_Mode.md — the as-built model (§6am)
  - HANDOFF_Coordinator_To_Ui_Motorcycle_Visuals.md — the dispatch (frozen at 464ca44f9)
  - HANDOFF_Coordinator_To_Backend_Motorcycle.md — backend's side (M-1 type 115 / M-2 the firing seat)
-->

# STATUS — ui: the motorcycle's look and the player on it (CE-4621 M-1v, M-2p)

Started on `b0e44e530` (coordinator merged). Backend (`a2e399011`) had only its started-marker at dispatch: type 115, class `Motorcycle` and the seats do NOT exist on `origin/backend` yet ⇒ built against a STAND-IN type (id 9115, DIS `1.1.0.80.1`, seat 0 Driver, seat 1 pillion with an arc), the look pinned; re-bind when backend lands.

## Progress — ✅ ALL FOUR ITEMS BUILT, GATED AND LIVE-CHECKED *(backend's M-1 / M-2 landed mid-run; merged `7effc694c`, bound)*
| item | state |
|---|---|
| U-1 mockup | ✅ `docs/designs/vehicle-tactics/motorcycle-visuals.svg` (+ `scripts/gen-motorcycle-mockup.py`), `c525fe21e` |
| U-2 family + kit + riders + arc + glyph + thumbnail | ✅ bound to the REAL type 115 / class `Motorcycle` / seats; kit fitted to backend's numbers (rider hips x 0.1 z 0.75, pillion x −0.45 z 0.85, body 2.1 × 0.8 × 1.2); `["Motorcycle"]` in `FamilyOfClass`; thumbnail in the atlas (page 1, slot 0/2; 79 pictures); picker glyph `_motorcycle` |
| U-3 PL-2 pillion in first person | ✅ **MEASURED: WIRING** (below), built, **live-checked** |
| U-4 PL-1 driving | ✅ the lean built; **live-checked**: drove at 28 m/s, turned, the bike and its rider leaned |

## Live checks (real editor under `xvfb-run`, editor mode, HTTP per the runbook; scenario `test-move`, type 115 + riflemen spawned)
| check | result |
|---|---|
| **U-4 drive** | possessed man 1006, *Drive* press (`use` / `useVerb 0` / `useTarget` = the bike) ⇒ he sat in seat 0 at (x + 0.1, z 0.75) = backend's rider seat; the bike then possessed (the UI's handover step) and driven with control frames: **28 m/s**; a left turn (steer 1) ⇒ heading turned, angular z 0.29; the scene capture shows the bike banked into the turn, the rider leaning (`live-scene-turn.png`). ⏳ *slowed off-road*: `test-move` has no terrain classes (flat, no surface grid) — backend's MC-2 is covered by its own rails, not checked live here |
| **U-3 board** | men 1008 / 1010, *Board* (`useVerb 1` — the primary is *Drive* while the driver seat is free) ⇒ seat **1**, at the pillion's place (z 0.85) |
| **U-3 the HUD cue** | camera looking +x, bike heading +x ⇒ the crosshair is AHEAD, outside the pillion's 30°…330° arc: the HUD shows **`OUT OF ARC - this seat cannot fire there, fire held`** under the crosshair (`live-pillion-hud2.png`, X screenshot) |
| **U-3 fire inside / outside the arc** | trigger held + `aim` AHEAD ⇒ `ActionStatus.Weapon.Reason = OutOfArc`, ammo **210 → 210**; trigger + `aim` REAR-LEFT (az ≈ 134°) ⇒ ammo **210 → 194**. Drawn: the pillion turned back, the rifle pointing rear-left (`live-pillion-fires.png`, scene capture) |

Files (⛔ not in git): `/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/live/{live-scene-turn.png,live-pillion-hud2.png,live-pillion-fires.png}` and `…/scratchpad/moto/{mockup.png,side-by-side.png,shots/moto-*.png}`.

## Gates (rule 8 contract)
| gate | command | result |
|---|---|---|
| T-1 the feature's own suite | `HROT_HIDDEN_WINDOW=1 xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter MotorcycleModelRail` | **13 passed / 0 failed / 0 skipped** (the real-frame rail RAN) |
| picker / catalog / class table / atlas | `--filter "CE1017\|S1_TheIcon\|CE2138\|EntityTypeCatalog\|Atlas"` plus the rail | 89 / 89 *(goldens carry type 115, the class 80 oracle, `_motorcycle`, atlas count 79)* |
| T1 whole project | `HROT_HIDDEN_WINDOW=1 xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build` | 2185 passed / **8 failed** / 10 skipped. ⭐ **3 PRE-EXISTING at base `b0e44e530`** (clean worktree, built and run): `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` (the FPV's drawn size — another lane's), `CE4388_TheCachedWireSphere…`. 4 are the HIDDEN-WINDOW mode / window-sharing rails (they pass or change without `HROT_HIDDEN_WINDOW`): `CE1039_Sessions…`, `CE4582_TheCardsHide…`, `P7B_In3D…`, `TheBuildingDamagePlanTests…`. `E2b_V_ARandomTerrain_TheRoadsStandOnTheHills` (a road vertex 1.13 m off the ground, random terrain): at the base worktree it SKIPPED ⇒ **base unconfirmed**; not a body rail — terrain lane's |
| red-proof (ONE key rail — visual work) | remove the bank from `EntityBodyLayer3D` | `TheMotorcycle_IsDrawn_ByTheRealFrame…` fails (*differs by 0 px*); restored, green |
| 0 B per frame | `TheMotion_AllocatesNothing…`, `TheCue_OutsideSeatArc…` | 0 B asserted (the cue rail warms 5 000 calls first: the tiered JIT allocates once) |
| restore after the merge | `dotnet build Hrot.Presentation.Tests` WITH restore, then `--no-restore` / `--no-build` | done; the TEST project was built before every run |
| tree clean | `git status` after the runs | clean apart from this batch's files |

## Fidelity (R-4146) — the real frame beside the mockup
`/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/moto/side-by-side.png` (mockup on top; real frames below: riding two up, the pillion firing to the rear-left, a banked left turn) — ⛔ not in git. Matches: two wheels, tank, long seat, driver on the bars, pillion upright behind, the pillion's torso turned toward the aim with the rifle shouldered, the arc wedge from his seat. Deviations (the mockup updated to the as-built is NOT needed — they are drawing detail): the rifle is the soldier block-figure's (a flat box), the driver's legs are straighter than the mockup's; the wedge in the real gizmo is a dashed arc + two edge lines (no fill).

## U-3 measurement *(PL-2: can a possessed man in a vehicle's firing seat fire in first person?)*
| asked | measured | where |
|---|---|---|
| the camera at the seat's eye | **already built**: `SeatSwitch.TryEye` = the seat's place on the vehicle's pose + 0.72 m (CE-1210 MS-5); a man in a seat rides the vehicle's chase camera, `O` = his eyes. Rail added (`TheEye_OfAPossessedRider…`) | `SeatSwitch.cs:94-104`, `PossessionStation.cs` `_eye = SeatSwitch.TryEye(...)` |
| free look | **already built** (a seated man: WASD hold him, the mouse looks); `ClampToGun` clamps only a STATION's arc, so a pillion's look is free — what we want | `PossessionStation.cs:1445-1450, :1858` |
| the cue outside the arc | **built**: under the crosshair `PossessionStation.OutsideSeatArc` → *"OUT OF ARC - this seat cannot fire there, fire held"* BEFORE the shot (0 B, rail); the executor's own refusal (`ActionStatus.OutOfArc`, the WEAPON line) already shows after a shot (CE-2317) | `PossessionStation.cs` (`OutsideSeatArc`, `InArc`) |
| the fire path itself | **does not exist yet, and it is BACKEND's**: boarding STRIPS `CanMove \| CanShoot` from every seat but a weapon system's Gunner seat, so the dispatcher refuses an embarked man's `FireAtPoint` before any executor runs. PF-1 (backend M-2: `StrippedOn` keeps `CanShoot` on a firing seat + the generalised `InStationArc` for the refusal reason) is exactly the missing piece. **No ui design is needed**: the LMB path fires `Hud.Resolve(w, unit, mount)` = the unit's own mount, unchanged | `Embarkation.cs:50, :517-525, StrippedOn` |
⇒ U-3 is WIRING (ui side DONE). ⏳ the LIVE check (board, fire, see the cue) needs backend's M-2 + type 115.

## U-4 PL-1 driving
*Drive* is the existing use verb for any vehicle with a Driver seat (`PossessionStation.InDriverSeat`, CE-1158): nothing bike-specific to build; the bike inherits manual driving through the existing first-person driving. Built: the lean in a turn (a drawing: `MotionPose.Bank`, riders lean with it, rail + frame rail red-proved). ⏳ the live check (drive, slowed off-road) needs type 115 (+ backend's MC-2 slow-down). Known limit (LD-6): the first-person eye stays at the UNBANKED seat while the bike leans (the horizon never rolls; up to ~0.5 m sideways at the 35° cap).

## Lane decisions *(for the user's review; each says what would change it)*
| # | decision | what would change it |
|---|---|---|
| LD-1 | `SeatPose.Ride` (=4, appended to the FDP enum) — legs astride, feet on the pegs; the driver leans to the bars, a pillion sits upright. Backend's seats should name `Pose = Ride` (the stand-in does) | backend preferring `Sit` ⇒ a one-line TKB change, the figure falls back to the plain bench |
| LD-2 | the pillion's drawn twist is limited to 160° (`EntityBodyLayer3D.MaxSeatTwist`); the seat's arc, not the drawing, decides what he may fire at | none (the drawing only) |
| LD-3 | a seat's arc is READ ccw from `TraverseMinDeg` to `TraverseMaxDeg` (so both sides + rear = `70 … 290` or `70 … -70`; a station's `-120 … 120` is still the front) — the HUD cue (`PossessionStation.InArc`) and, when it lands, backend's `InStationArc` generalisation must agree | backend reading it differently ⇒ change `InArc` (one function, one rail) |
| LD-4 | the bike's bank into a turn is `atan(v·ω/g)` capped at 35°, eased 0.15 s — a DRAWING only (the sim never leans) | — |
| LD-5 | the pillion is drawn firing when the weapon brain's posture is `Aimed` AND the seat states an arc and is no station (a FIRING seat, PF-1) | backend publishing a different "firing" fact |
| LD-6 | the first-person eye on a leaning bike stays at the unbanked seat (`SeatSwitch.TryEye` is static, the bank lives in the draw layer) | a user finding the view odd ⇒ publish the bank on the layer and read it in `TryEye` |
| LD-7 | the fire-arc wedge is a `SeatArcGizmo` (family Channels: the selected and pinned units, like the action status), amber dashed arc + edges, range 30 m, from the TKB's seats — not a recorded component | the user wanting it always on ⇒ a layer switch |
| LD-8 | the stand-in type is 9115 (a test-only id; never shipped) | — |

## What waits for backend
Nothing blocks ui. Cross-lane edits I made in backend's `NedTkbCatalog.Motorcycle` (said): both seats `Pose = Ride` (was `Sit`), `IconName = "_motorcycle"` (was `"car"`, which has no art). Still backend's: the rough-ride spread (their L-8), the dash / drive-by / swarm plays (M-4…M-6).

## Ids
`CE-4621` (coordinator's) updated with ui's state; no new ui ids allocated. ⭐ for the coordinator: `E2b_V_ARandomTerrain_TheRoadsStandOnTheHills` is red in the ui tree (terrain's).
