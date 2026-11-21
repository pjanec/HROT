<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
-->

# HANDOFF — coordinator → `terrain`: THE USER'S EDITOR OBSERVATIONS IN YOUR AREA (CE-1963, CE-1964, CE-1968, CE-1966)

**For:** terrain. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there (R-106: a step whose premise fails is
reported, the rest go on).

🔒 The user played the editor on `2026-11-17` and reported these; the ui lane recorded them as tracker rows (*"record as tracked items and do not solve
them on your own"*); 🔒 the user, the same evening: *"I need them as handoffs for lanes"*. Area: the editor's terrain tools and terrain features (Dynamic Terrain, CAPABILITIES §2b; DESIGN_Terrain_Editing / the terrain lane's own designs).
⭐ Note: CE-1966 may be ui's drawing (a feature handle / fallback marker in the 3-D map) — if so, fix it anyway as a said cross-lane edit or hand it to ui with the measured cause.

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
| **CE-1963** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — THE DITCH TOOL DRAWS A LINE BUT NO DITCH APPEARS:** the polyline is drawn on the map, no ditch is carved. Not investigated. |
| **CE-1964** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — THE RAISE / LOWER TERRAIN TOOL DOES NOTHING:** after selecting it, clicking (or any mouse action) on the map changes nothing and gives no hint how to mark an area. Not investigated. |
| **CE-1968** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — WOOD AREA TREES PLANTED ~10 m OFF THE POLYGON:** after drawing a wood area and Finish, the polygon fills green but the trees land offset (about 10 m), partly outside the polygon. Not investigated. Lean: a coordinate origin mismatch (polygon in one frame, the tree scatter in another — e.g. tile origin vs world origin). |
| **CE-1966** | `RW-S` 📋 **OPEN `2026-11-17` (ui; user report) — A PINK CUBE IN THE MIDDLE OF EVERY PLACED TERRAIN FEATURE (3-D):** a placed wall piece (and other terrain features) draws a big pink cube at its centre in the 3-D map, sometimes bigger than the feature. Lean: it is the feature's handle / fallback marker — draw it only when the feature is selected (or not at all). |

## Gates *(rule 8, in your status)*
Each fixed row: its new rail (red-proved), the touched classes' suites, the feature's scenario; behaviour code with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + the world guard; display rails under `xvfb-run -a`. Every red confirmed pre-existing against your base sha.
