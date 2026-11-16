<!--STATUS
state: LIVE — dispatched as a FOLLOW-UP: start when your minefield batch is reported — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — ⭐ THE design: §2 decisions (CP-*, RM-*), §3 UML, §4 the checkpoint in time, §5 slices K-1…K-8, §5a the fixed ids, §8 the review (folded)
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — IM-3 (the bike-borne bomb uses RamTarget), IM-8 ("two men on one bike" is a watch indicator)
  - ../../DESIGN_Civilians_And_Crowd_Control.md — the escalation ladder (§5k), hostile acts, custody / search, hidden weapons (§5h)
-->

# HANDOFF — coordinator → `behaviors`: the CHECKPOINT, RAMMING and the CAR BOMB (CE-4622, slices K-1…K-8)

**For:** the `behaviors` lane, as a FOLLOW-UP — start when your minefield batch is reported. **From:** coordinator. **Dispatched at `b933ddd7d`**;
⛔ scope FROZEN there — later documents are FYI; one that invalidates a slice ⇒ STOP that slice, do the rest (R-106).

🔒 User, `2026-11-16`: *"Checkpoint and ramming (CE-4622) can be passed to one of the lanes as a followup handoff"* · decisions R-4260, the review
R-4262; R-4277 (a feature works only when its scenarios pass).

## ⭐⭐⭐ UNATTENDED
Never ask, never wait; decide by the claim table and record an `R-2xxx` lane decision with what would change it; a failed premise stops ONE slice
(R-106); a defect is fixed where it lives with a rail in its own suite; cross-lane edits (the vehicle kinematics, damage, the wire) are allowed and
SAID; commit, push and update `STATUS_Behaviors_Checkpoint.md` after each slice.

## 0. Start
Merge `origin/coordinator` (and `origin/terrain` if it carries the obstacle rows you place); restore; push `chore: started checkpoint at <sha>`.
Read the design END TO END (R-129) — §2–§5 are the folded text; §8 explains each change; ⛔ HISTORY is not to be quoted.

## 1. The ids — §5a, fixed
`RamOrder` = component id **126** (127, 128 held for this feature), descriptor **`dtRamOrder` = 196** (197 held). ⛔ Take nothing else outside
your block.

## 2. The slices — §5, in order
- **K-1** `VehicleImpactSystem` (RM-1) — the ground twin of `DroneImpactSystem`, sharing its swept test; a man struck at rising closing speeds falls
  in rising Health bands with a seeded spread; riders / boarding exempt. ⚠ It touches every vehicle: re-check every vehicle scenario's verdict.
- **K-2** `RamOrder` + `RamTarget` + RM-3 / RM-5 — lead pursuit, avoidance off for the target only; the car is a Lethal hostile act; a weak barrier
  breaks (`HullBreakGate` reads `RamOrder`); a car without the order cannot.
- **K-3** the ENGINE zone (CP-6 / CP-7) — MG fire at the engine stops a car in a stated time; a penetrating hit may hurt an occupant; the driver
  incapacitated ends the move.
- **K-4** the site + `CheckpointPlay` + `VehicleApproachWatch` (CP-1 / CP-1a / CP-2) — a routine car stops at the line; a fast car is a threat BY TIME.
- **K-5** the vehicle ladder (CP-3) — rungs by time, skipped when short; a warning shot only at NonLethal+; a disabled car coasts. ⭐ A two-up
  motorcycle has no engine rung (KV-14) and "two men on one bike" raises the watch (IM-8).
- **K-6** the search (CP-4) — out, hands up, searched; the boot's charge found by the vehicle search.
- **K-7 + K-8** the FEATURE-TEST SCENARIOS (R-4277), each a regression test (What / Judge `description`, `checks.json` expect rows, golden):
  `cp-routine-car` (released) · `cp-vbied` (stopped short OR detonating — both possible by distance and timing) · `cp-compliant-bomber` ·
  `cp-moto-attack` (a two-up bike approaches; the pillion fires or throws — the throw is backend's, item 2 of their regression-suite batch: use it
  if it has landed, else the firing variant and list the throw as ⏳) · `cp-ram-barrier`. DONE = `scripts/scenario-regression.sh <name>` passes in
  the editor tier; add each to `docs/CAPABILITIES.md` §9 with its mark.

## 3. Gates and report
T-1 first (vehicle kinematics, breach, damage, civilians / escalation, custody suites); every behaviour step with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1` and `stepped-hash.py --threads 1,4,8` on the checkpoint scenarios (the parallel rule's own gate);
each new rail red-proved by a compiling mutation; 0 B per steady frame (R-2084); invariant output (R-4221). Fold the as-built into the design
(obligation ⑤); the gate-report contract (rule 8); ids from your block.
