<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — VA-9 (approved R-4296): the discharger is backend's mount; "pop smoke" is a behaviour
  - HANDOFF_Coordinator_To_Backend_Vehicle_Appearance_2.md — backend's VA-S5 (the mount), which this row waits for
-->
# HANDOFF — coordinator → `behaviors`: "POP SMOKE" — a vehicle screens itself when threatened (CE-4670 VA-S5, the behaviour half)

🔒 User, `2026-11-18`: *"yes, write the handoffs"*.

## Priority
⛔ BLOCKED until backend's NOTE says the `SmokeDischarger` mount works (VA-S5). Then: after the critical steer (CE-4665 and the street fight) and
your decided rows (D-4…D-8) — before your remaining second-tier fight rows.

| row | builds | done when |
|---|---|---|
| **CE-4670 pop smoke** | a vehicle with a `SmokeDischarger` fires it — once, with a cooldown — when it is THREATENED: an ATGM / RPG launched at it or a laser / radar lock (the threat reports that exist), or a hit it survived; then it reverses or moves to cover (the existing hide / break-contact behaviours) — find what exists first (graph + grep: threat, missile warning, break contact) and extend it, never a parallel reaction | ⭐ a scenario: a tank under an ATGM shot pops smoke and backs out of the line of sight; the shooter's next shot loses it (the cloud blocks sight and the thermal sensor) |

Parallel rules apply (`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + the world guard on the behaviour's own rails); §6a scenarios per row.
