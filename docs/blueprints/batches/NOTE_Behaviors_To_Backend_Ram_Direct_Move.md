<!--STATUS
state: LIVE — for backend (navigation)
updated: 2026-11-17
current-answer: the whole note
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — RM-2 (the direct move inside ~50 m), RM-5 (the weak barrier), §5b K-2 (R-2129)
  - ../../DESIGN_Vehicle_Breaching.md — the contact breach the ram should reach
-->

# NOTE — behaviors → backend: a ramming car needs a DIRECT move inside its run-in (CE-2445)

📐 **Measured `2026-11-17` on `scenarios/demos/checkpoint/cp-ram-barrier`** (behaviors, CE-4622 K-8). A `RamTarget` car (a hull with an active
`RamOrder`) drives at a man behind a row of four barrels across the road. About 20 m out the PLANNER routes it round them: it veers to y 287 and stops beside
the barrels at (383, 287). The break gate (`HullBreakGate.MayBreak` answers true for an active `RamOrder`, K-2) is never reached because the hull never touches
the barrier.

| | |
|---|---|
| what the design asks | RM-2: *"inside ~50 m a direct move (no path round a weak barrier)"*; RM-5: the ram breaches a weak barrier through `ContactBreach` |
| why behaviors did not build it | R-2129 — `LocomotionMoveTo`'s flags byte is full, and a path-less move is the mover's, not the brain's |
| ⭐ lean | a hull with an active `RamOrder` inside its run-in (the order's target within ~50 m) drives the STRAIGHT line to its move's destination; the obstacle is met and the breach gate decides (break, push, or too strong) |
| how to verify | `scripts/scenario-regression.sh cp-ram-barrier` — the rows "the car goes through the barrels" and "the Gunner is struck" go green |

⚠ The scenario stays ⚠ in `docs/CAPABILITIES.md` §9 until then; the rest of the checkpoint (`cp-routine-car`, `cp-vbied`, `cp-compliant-bomber`,
`cp-moto-attack`) passes.
