<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
-->

# HANDOFF — coordinator → `convoy`: THE USER'S EDITOR OBSERVATIONS IN YOUR AREA (CE-1959, CE-1972)

**For:** convoy. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there (R-106: a step whose premise fails is
reported, the rest go on).

🔒 The user played the editor on `2026-11-17` and reported these; the ui lane recorded them as tracker rows (*"record as tracked items and do not solve
them on your own"*); 🔒 the user, the same evening: *"I need them as handoffs for lanes"*. Area: the convoy and the SUMO traffic handover (DESIGN_Road_Convoy, AQ93).
⭐ Note: CE-1959: the user saw hits on SUMO cars from a POSSESSED armoured vehicle work before — find what changed; CE-1972: likely the convoy scenario that stops on red.

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
| **CE-1959** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — CONVOY AMBUSH: SUMO CARS IGNORE HITS FROM A POSSESSED ARMOURED VEHICLE:** firing the vehicle's weapon at SUMO traffic shows hits, but the car neither stops nor does its driver bail out, and the car is not handed over to our own model. The user saw it work when firing as a soldier. Not investigated. Lean: check whether vehicle-weapon hits reach the traffic damage / takeover path at all (a weapon category the traffic hit handler does not take). |
| **CE-1972** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — THE CONVOY LEADER WEAVES BETWEEN LANES:** in the convoy scenario (likely the stop-on-red one) the leading HROT armoured vehicle swings from one lane to the other and back, and the two followers copy it. Not the SUMO car. Not investigated. Lean: the route / lane-follow steering of the lead vehicle oscillates (a gain or a lookahead too short). |

## Gates *(rule 8, in your status)*
Each fixed row: its new rail (red-proved), the touched classes' suites, the feature's scenario; behaviour code with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + the world guard; display rails under `xvfb-run -a`. Every red confirmed pre-existing against your base sha.
