<!--STATUS
state: LIVE — a claim (protocol §5): terrain is building CE-5040 in behaviors' code · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - NOTE_Terrain_To_Behaviors_Commanded_Squad_Ignores_Minefield.md — the finding this takes over
  - ../../designs/minefields/DESIGN_Minefields.md — KG-7 / MR-19, the chain it unblocks
-->
# NOTE terrain → behaviors: terrain is TAKING CE-5040 — please do not build it

🔒 **User, `2026-11-19`:** *"we cant wait for behaviors, can you do that part yourself and just leave behaviors a note what are you doing so they don't collide"*

| | |
|---|---|
| **what** | [CE-5040](../Blueprint_Issues_Tracker.md): a squad under an HQ never reacts `MinefieldAhead`. Terrain root-causes and fixes it, in behaviors' code where the cause is (expected: the SOP / command path of a commanded squad — `BasicSquadSop`, `SquadDismountBehavior`, the order path, `BrainTickSystem`'s SOP slot) |
| **started at** | terrain@`59ab31874` (merged `behaviors@683a2fa25`, `coordinator`) |
| **⛔ please** | don't start CE-5040; avoid restructuring the SOP rows `c4624000-…-0b40` / `MinefieldAhead` or the commanded-squad reaction gate until terrain's commit lands (I'll add its sha here) |
| **how it lands** | on `terrain`, a cross-lane edit named in the commit message, with a rail in your suites + `mf-clear-lane-and-cross` passing; ⭐ the design fold-back goes in the owning design |
| **if you've already started** | tell the user — terrain stops and keeps the scenario side only |

✅ **Status: the fix landed — terrain `4dd1b818e`.**

| | |
|---|---|
| **the cause** | `BasicSquadSop`'s `UnderCommand` row (CE-2358, *"the platoon reacts for the squad"* — true ⇒ the row wins, nothing ordered) stood ABOVE the `MinefieldAhead` row ⇒ a squad under a live HQ never reached it. ⚠ MS-B5 sends the Engineers request UP the chain, so the asking squad ALWAYS has a commander: the two rulings were never reconciled |
| **the fix** | the minefield row moved ABOVE `UnderCommand`, beside the carrier rows that already "stay the squad's own"; `UnderCommand`'s comment names it |
| **the rail** | `SquadManeuverTests.CE5040_BasicSquadSop_UnderALivePlatoon_AMinefieldAhead_StillHalts_AndCallsTheEngineers` — red-proved (row back below ⇒ red); MS-B4 + CE-2358 rails green (4/4) |
| **measured** | `mf-clear-lane-and-cross`: S3 `MinefieldAhead` at 159 s, S4 `ReconMinefield` at 162 s, no second mine |
| ⭐ **not changed** | `ReconMinefield`'s pace (MS-B2: ~75 s per 10 m cell, by design) — the scenario now places its engineers 40 m short and runs 2000 s |

⚠ **FYI, not caused by this:** `mf-no-way-round` (no commander) fails its `MinefieldAhead` row on today's tree — the squad stalls ~40 m along its route
short of the field at 120 s (it halted at 111 s on `2026-11-18`). Being bisected by terrain now.
