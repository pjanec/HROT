<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
-->

# HANDOFF — coordinator → `behaviors`: THE USER'S EDITOR OBSERVATIONS IN YOUR AREA (CE-1967)

**For:** behaviors. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there (R-106: a step whose premise fails is
reported, the rest go on).

🔒 The user played the editor on `2026-11-17` and reported these; the ui lane recorded them as tracker rows (*"record as tracked items and do not solve
them on your own"*); 🔒 the user, the same evening: *"I need them as handoffs for lanes"*. Area: vehicle impact (CE-4622 K-1, built by behaviors: VehicleImpactSystem).
⭐ Note: after CE-3454 and your own follow-ups; the user expects a man run over by a bulldozer to be KILLED, not knocked down.

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
| **CE-1967** | `RW-M` 📋 **OPEN `2026-11-17` (ui; user report) — A BULLDOZER ONLY KNOCKS SOLDIERS DOWN:** a possessed bulldozer driving over soldiers knocks them down and they stand up almost at once, after it has passed. Expected: killed outright. |

## Gates *(rule 8, in your status)*
Each fixed row: its new rail (red-proved), the touched classes' suites, the feature's scenario; behaviour code with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + the world guard; display rails under `xvfb-run -a`. Every red confirmed pre-existing against your base sha.
