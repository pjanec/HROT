<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - STATUS_Behaviors_Assault_Under_Fire.md — your morning report (§-1 the summary, §0 the runs, §4 the threshold proposal) — this batch closes it
  - ../../designs/group-maneuvers/DESIGN_Assault_Under_Fire.md — CE-2436 (A-9, AU-4's blind fire, AU-6)
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md — CE-4615 §5, the dynamism thresholds
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — CE-4621 M-4 (MC-4a the realistic FPV guidance, the dash runs) — part 2
  - ../../designs/vehicle-tactics/DESIGN_FPV_Drone.md — the FPV's terminal guidance MC-4a changes
-->

# HANDOFF — coordinator → `behaviors`: close the assault test, then the realistic FPV and the dash (CE-2436, CE-4621 M-4) — OVERNIGHT, UNATTENDED

**For:** the `behaviors` lane (ids `CE-2000`…, `R-2000`…). **From:** coordinator. **Dispatched at `7bea87df1`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-16`: *"approved your leans, ignore the mortar lethality, write handoff to behaviors."* (R-4265)

## ⭐⭐⭐ OVERNIGHT, UNATTENDED — read this before anything else
| the rule | |
|---|---|
| ⭐⭐⭐ **NOBODY IS THERE TO ANSWER** — never ask, never wait, never end a turn on a question | every open choice: measure it, decide by the claim table (code + design), build it, record an `R-2xxx` lane decision *"for the user's review"* with what would change it |
| ⭐⭐⭐ **DO NOT STOP UNTIL EVERY ITEM IS BUILT, GATED AND REPORTED** | a slice finishing is not the end — go straight to the next |
| ⭐⭐ **a failed premise stops ONE item, never the batch** (R-106) | write why in the status file, do every item that does not depend on it |
| ⭐⭐ **a defect is fixed where it lives**, a rail in that feature's own suite (T-1), its design folded (obligation ⑤) | a large one filed as a row and worked around |
| ⭐ **commit, push and update the status file at every item's end** | a restart or a compaction resumes from it |
| ⛔ **you may NOT** reverse a user ruling, or re-open the mortar lethality (🔒 user: *"ignore the mortar lethality"*) | file it as a finding instead |

## 0. Start
`git fetch origin coordinator behaviors backend`; on `behaviors`, merge `origin/coordinator` (it carries backend's motorcycle: the conduct gate,
`OrderKind.Dash`, the firing seat, the speed factor); restore the test projects you run; push `chore: started assault close-out + drones at <sha>`.
Status: keep `STATUS_Behaviors_Assault_Under_Fire.md` for part 1; open `STATUS_Behaviors_Realistic_FPV_And_Dash.md` for part 2.

## Part 1 — close the assault test (CE-2436, CE-4615)

| # | item | done when |
|---|---|---|
| 1 | ⭐ **apply R-4265**: *"Objective 1 taken"* = the platoon's PhaseLine report **OR** the 30 s hold — in `utility-demo-check.py`'s `cc-battalion` check and the design's acceptance (Assault Under Fire §7, Battalion Test §5) | the check re-judges #18–#20 as three passes from their recorded logs (no new run needed for that) — and ONE fresh base run confirms |
| 2 | ⭐ **re-time the phase-line deadlines** to the suppression-first attack: from your measured runs (PL 1 reached ≈ 517–606 s) set PL 1 / PL 2 with the same margin the design used before; record the numbers as a lane decision | the deadlines met in the passing runs; the scenario and its design updated |
| 3 | **lock the dynamism thresholds** — your proposal `CC_MOVED_MIN = 0.45`, `CC_CONTACTS_MIN = 90` (first window only), now that three passes exist; CE-4615's acceptance met for all three variants (base, stall, collapse) | locked in the check and the design; the three variants re-run |
| 4 | **CE-2440** — the blueprint blackboard race at 8 threads (1 in 5): root-cause and fix it in the shared code, with a rail that reproduces it | `stepped-hash.py --threads 1,4,8` on `cc-battalion` equal in 5 / 5 |
| 5 | **A-9** — the company's base of fire on a COVERED position within weapon range (Assault Under Fire A-9, not built) | its rail; a base run with it |
| 6 | **AU-4's blind cannon fire** — the IFVs fire on REPORTED posts they cannot see (A-4 as designed) | its rail |
| 7 | CE-2437 / CE-2439 (the read-check reds you filed) — fix if small, else leave filed with the measured cause | — |

## Part 2 — the realistic FPV and the grey-zone dash (CE-4621 M-4) — ⭐ after part 1's items 1–3

backend has built the motorcycle (type 115, the firing seat, `OrderKind.Dash` and the conduct gate, the speed factor, the plays — see
`STATUS_Backend_Motorcycle.md`); ui its look and the player on it. ⭐ This part is the one piece left to you by design (R-4262):

| # | item | done when (Motorcycle §6 M-4) |
|---|---|---|
| 8 | ⭐ **MC-4a — the FPV's terminal guidance becomes realistic**: it aims from its OWN perceived track with an operator delay (~0.3–0.5 s) and stops re-aiming at a COMMIT point (time-to-go ≈ 0.7 s); the expected miss of a break turn ½·a·(commit + delay)² written into the FPV design | ⚠ **every drone scenario changes**: re-run the drone demos (`ua-drone-*`, the drone team in `cc-battalion`, the air-defence and shotgun rails), reconcile each rail to the new guidance with its reason — never loosen a rail to pass |
| 9 | **MC-3…MC-7 the dash** — `GreyZoneDash` on bikes under `OrderKind.Dash`: spacing, the lookout read (declared), the break at time-to-go, the hearing mask (backend built it — verify), dismount at the edge | seeded runs on open ground vs two FPV operators: the riders UNHURT (miss beyond the casualty radius) in a measured share; the conduct gate refuses the halting reactions (journal `ForbiddenByOrder`) |

## Gates — every item
T-1 first (each feature's own suites); every behaviour rail with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail
red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8`; 0 B per steady frame (R-2084); invariant output (R-4221). Build the TEST
project in the fix loop; live runs in the background, three each.

## The morning report
Part 1 in `STATUS_Behaviors_Assault_Under_Fire.md` (the base variant table re-judged, the deadlines, the thresholds locked, CE-2440's cause) ·
part 2 in `STATUS_Behaviors_Realistic_FPV_And_Dash.md` (the drone scenarios before / after, the dash's measured share) · every lane decision with
what would change it · the gate-report contract (rule 8) · ids allocated.
