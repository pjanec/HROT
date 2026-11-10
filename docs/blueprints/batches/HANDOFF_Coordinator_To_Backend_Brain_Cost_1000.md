<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
related-designs:
  - ../../DESIGN_Scale_Rig.md — §5.4 (BrainTickSystem in the over-budget list), §7 (the fight runs where it was "ignored"), N-3 (3 000 men)
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — ⭐ behaviors' design of WHAT a brain evaluates and HOW OFTEN (D1–D8, R-2083; T1–T10 built): read it before changing any cadence
  - ../../DESIGN_Sensors_And_Doctrine.md — what the brain is fed (sensors, memory), and P-1's parallel EQS (your own §5.8)
  - ../../OVERVIEW_Behaviour_Building_Blocks.md — the map of the brain's machinery (decision slots, utility, BTree / HSM / Blueprint)
  - ../../PLAN_Large_Terrain.md — §8 the budgets (R-4192)
-->

# HANDOFF — coordinator → backend: the BRAIN's cost at 1 000 men (CE-4395)

**For:** backend. **From:** coordinator. **Dispatched at `<stamp>`**. ⭐ Scope FROZEN there. Merge `coordinator` first, push
`chore: started HANDOFF_Coordinator_To_Backend_Brain_Cost_1000 at <sha>`, and keep a status file. ⭐ You design the details: INVENTORY, UML and the
leans, in the owning design (Evaluation Cost & Cadence, or a new Scale Rig section, whichever fits). This handoff is the frame.

🔒 User, `2026-11-10`: *"The backend seems to be free. Maybe it could take the cost of 1000 man brain."*
⚠ This **supersedes** the scale-rig scope line *"Brain costs can be ignored"* (Scale Rig §6, same day). The brain is now in scope.

## Measured so far (yours, from the rig)

| run | `BrainTickSystem` (CGF) | where |
|---|---|---|
| demo-city: 96 → 296 → 1 000 men | 1.2 → 3.4 → 20.0 ms | Scale Rig §5.1 |
| scale-1, 1 000 men + 50 vehicles + 10 aircraft | 14.6 ms | §5.4 (CE-3425) |
| scale-1, 1 000 men fighting, 16 cores | 18.6 – 24 ms | §7 |
| ⛔ 3 000 men | **410 – 500 ms** | N-3 |

⭐ **3× the men costs ~20× the time.** Something in the brain grows faster than linearly: something per pair, or a scan of everyone done for
everyone. That is the first thing to find. Budget: R-4192, the sim frame ≤ 10 ms per node at 1 000 men + 50 vehicles + 10 aircraft. The brain's node
is the CGF.

## The work

| # | step | done when |
|---|---|---|
| B-1 | **attribute the 20 ms**: per brain kind (infantry, vehicle, squad leader, platoon), per machinery (BTree tick, HSM, utility decisions, the blueprint VM, memory / sensor reads, the decision gate), per call site, at 1 000 and 3 000 men, fighting. Use the scheduler profiler and the `QueryTimings` slots you built in CE-3427 (add slots where the brain has none) | a table in the owning design naming what grows superlinearly, with `file:line` |
| B-2 | **fix the biggest, by the numbers**: an algorithm (a per-pair scan → the shared grid or the squad's shared contacts, which exist: OVERVIEW §groups), a cadence (behaviors' D1–D8 already let a slot run at its own rate: use them, do not add a second scheduler), or parallelism (the P-1 pattern) | ⭐ 1 000 men fighting: the brain under the frame budget on the Windows machine; 3 000 men measured and reported |
| B-3 | **no garbage** in the brain's steady frame (R-4192: 0 B) | the rig's per-system garbage column for the brain at 0 |

## Fences

- ⛔⛔ **Behaviour must not change.** This is behaviors' code (BTree / HSM / Blueprint and the behaviour infrastructure).
  - Speed it up; do not alter what a unit decides.
  - The guard is the scenario regression (`--verify all`, DESIGN_Scenario_Regression). Every scenario that passes its conditions on your base
    must still pass.
  - A golden that moves needs a diff shape that says why (a cadence moves timings; say so and re-record only those).
  - ⚠ Expect the stamps on decision logs to move if a cadence changes.
- ⭐ **A cadence change is a DESIGN change in behaviors' Evaluation Cost & Cadence.** Propose it there with a lean, and tell behaviors with a note.
  Ask the user if it changes how units behave.
- ⭐ **Tell behaviors you are in their code.** Write a NOTE at the start and keep edits narrow; they are working on vehicle tactics now.
- R-2037: the brain never reads world geometry. Do not "speed it up" by giving it a local copy of something it must ask the muscle for.
- Report with the gate contract (per gate: command, counts, delta, `--no-build`; reds proved pre-existing against the base sha).
