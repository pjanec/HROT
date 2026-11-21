<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
-->

# HANDOFF — coordinator → `ui`: THE USER'S EDITOR OBSERVATIONS IN YOUR AREA (CE-1960, CE-1961, CE-1971, CE-1962, CE-1965, CE-1969, CE-1970)

**For:** ui. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there (R-106: a step whose premise fails is
reported, the rest go on).

🔒 The user played the editor on `2026-11-17` and reported these; the ui lane recorded them as tracker rows (*"record as tracked items and do not solve
them on your own"*); 🔒 the user, the same evening: *"I need them as handoffs for lanes"*. Area: the editor, the possession station, the 3-D map and the air-combat looks.
⭐ Note: CE-1960 / CE-1961 / CE-1971 belong to the air combat you are finishing (CE-4647); the others after it.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait. For each row: **① REPRODUCE it first** — in the editor (headless over the HTTP debug API, or the feature's own scenario) — and
write the reproduction into the row; ⛔ a row you cannot reproduce is reported with what you tried, not "fixed". **② root-cause it** (graph AND grep;
the owning design first, R-129). **③ fix it where it lives**, with a rail in THAT feature's own suite (T-1) that is red without the fix.
**④ the feature's scenario** passes (`scripts/scenario-regression.sh`) — add an expect row when the scenario could not see the defect.
Fold anything learned into the owning design. Tracker / ruling ids from your own block; mark each row DONE with its rail and commit.
Merge `origin/coordinator` first; commit, push and keep your status file after every row.

## The rows (verbatim from the tracker)

| row | the observation |
|---|---|
| **CE-1960** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report, air combat CE-4647) — A POSSESSED JET'S CANNON FIRES BACKWARDS:** the aiming cross sits ahead of the nose, but the tracers leave from the cockpit towards the tail. Not investigated. Lean: the gun mount / muzzle frame of the jet suites (S3) uses the wrong forward axis, or the possession fire path takes the aim vector reversed for aircraft. |
| **CE-1961** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report, air combat CE-4647) — A POSSESSED JET CANNOT PITCH:** left/right turns and banks properly, but it cannot climb or dive — it stays level, which removes air combat play. Not investigated. Lean: the possession control maps only yaw/bank onto the flight model; pitch / altitude command is not wired. |
| **CE-1971** | `RW-S` 📋 **OPEN `2026-11-17` (ui; user report, air combat CE-4647) — AIRCRAFT LABELS VANISH AT DISTANCE IN 3-D:** the 3-D label distance cut-off suits ground units but hides small, far, high aircraft — then nothing shows where they are. Wanted: a longer (or no) cut-off for air entities. |
| **CE-1962** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — DRIVE AND FIRE WHEN THE GUNNER SEAT IS EMPTY:** possessing a vehicle with a gunner seat and no gunner puts you in the driver seat with no way to fire; switching to the gunner seat fires but the vehicle stops (nobody steers). Wanted: with no real gunner, the driver also controls the weapon. |
| **CE-1965** | `RW-S` 📋 **OPEN `2026-11-17` (ui; user report) — THE TOOLBAR SPLIT BUTTON:** the editor toolbar's split button is drawn differently from the main menu's split button for the armed mode, and every click opens its menu. Wanted: a real split button — the arrow opens the menu, the choice is remembered, and a click on the icon runs the last chosen item. |
| **CE-1969** | `RW-S` 📋 **OPEN `2026-11-17` (ui; user report) — RIGHT-CLICK OFFERS FINISH / CANCEL WITH NOTHING TO FINISH:** after Finish from the right-click menu the tool runs (fine), but the next right-click shows Finish / Cancel again although no point was placed. Expected: Finish only when the tool has points to finish. |
| **CE-1970** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — STUCK IN THE 3-D MAP:** in some state Ctrl+M and View ▸ 2D/3D map both did nothing; no way back to the 2-D map. How the state was reached is unknown. Lean: the toggle is refused by a guard (an active tool or a camera mode such as possession) without any message. |

## Gates *(rule 8, in your status)*
Each fixed row: its new rail (red-proved), the touched classes' suites, the feature's scenario; behaviour code with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + the world guard; display rails under `xvfb-run -a`. Every red confirmed pre-existing against your base sha.
