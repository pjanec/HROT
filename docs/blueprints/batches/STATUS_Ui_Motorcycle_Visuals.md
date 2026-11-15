<!--STATUS
state: LIVE — OVERNIGHT, UNATTENDED run in progress (updated at every slice's end)
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

## Progress
| item | state |
|---|---|
| U-1 mockup | ✅ `docs/designs/vehicle-tactics/motorcycle-visuals.svg` (+ `scripts/gen-motorcycle-mockup.py`), `c525fe21e` |
| U-2 family + kit + riders + arc | ✅ built against the stand-in (commit `CE-4621 U-2 + U-3`, pushed). Family `Motorcycle`, 2-wheel kit, bank, `SeatPose.Ride` riders, the firing pillion turned to his aim, `SeatArcGizmo` (the arc wedge on both maps for the selected bike), picker glyph `_motorcycle`. ⏳ waits for backend: `["Motorcycle"]` in `FamilyOfClass` (its rail `CE2138_EveryClassAFamilyOrAvatarNames_IsInTheTable` fails on a missing class), the real seat numbers, the thumbnail (needs type 115) |
| U-3 PL-2 pillion first person | ✅ **MEASURED: WIRING, built** — see "U-3 measurement" |
| U-4 PL-1 driving | ✅ the lean (bank) and the eye rail built; ⏳ the LIVE drive check waits for type 115 (a stand-in cannot run in the real editor) — see "U-4" |

## Gates (rule 8 contract)
| gate | command | result |
|---|---|---|
| T-1 the feature's own suite | `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter MotorcycleModelRail` (`HROT_HIDDEN_WINDOW=1`) | **12 passed / 0 failed / 0 skipped** — the real frame rail RAN (not skipped) |
| the seams it touches | same project, `--filter "MotorcycleModelRail\|Gizmo\|Possession"` | **459 / 0 / 0** |
| T1 whole project | `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build` (with `HROT_HIDDEN_WINDOW=1`) | 2183 passed / 7 failed / 10 skipped: **3 PRE-EXISTING at the base `b0e44e530`** (clean worktree built + run: `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` — the FPV's drawn size changed by another lane, expected 0.6 got 0.35 —, `CE4388_TheCachedWireSphere…`); 3 pass without the hidden-window switch (`CE4582_TheCardsHide…`, `P7B_In3D…`, `CE1039_Sessions…` = the hidden-window mode, not the change); `E2b_V_ARandomTerrain_TheRoadsStandOnTheHills` (a terrain road-vertex height) fails in my tree and was SKIPPED in the base worktree — unrelated to bodies, **base unconfirmed**, filed as an observation |
| red-proof (ONE key rail, visual work) | remove the bank from `EntityBodyLayer3D` | `TheMotorcycle_IsDrawn_ByTheRealFrame…` fails: *"a turning bike is drawn banked (differs … by 0 px)"*; restored, green |
| 0 B per frame | `TheMotion_AllocatesNothing…`, `TheCue_OutsideSeatArc…` | 0 B asserted |
| `--no-build` column | the test project was BUILT (not the production one) before every run | yes |
| tree clean after runs | `git status` | clean apart from this batch's files |

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

## Waits for backend (merge `origin/backend` when it has them)
type `TkbEntityTypes.Motorcycle = 115` · class `"Motorcycle"` (`1.1.*.80`) · the seats (Pose `Ride`, X/Z ≈ (0.02, 0.90) and (−0.52, 0.90), the pillion's arc) · M-2 (`StrippedOn`) · MC-2 (off-road factor). Then: `["Motorcycle"] = VisualFamily.Motorcycle` in `FamilyOfClass`, turn `MotorcycleModelRail`'s stand-in into the real type, the thumbnail (`scripts/thumbnails.py --add-image`), the live U-3/U-4 checks.

## Ids
`CE-4621` (coordinator's) updated with ui's state; no new ui ids allocated yet.
