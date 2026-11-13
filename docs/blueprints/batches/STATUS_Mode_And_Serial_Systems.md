<!--STATUS
state: LIVE — in progress
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the work M-1 … M-9 (CE-4480 … CE-4488), scope frozen at ffc802ab2
  - NOTE_Backend_To_Coordinator_Parallel_Switches_And_Throughput.md — the findings it answers (D1–D5)
  - STATUS_Overnight_Threaded_Build.md — the overnight build this continues (D-1 … D-43, the gate commands)
  - ../../DESIGN_Parallel_Simulation.md — §10–§11 the pre-score, §18 the per-entity rule
-->

# STATUS — the mode set by what is running, the pre-score retired, the serial systems converted

Branch `exp/threaded-build`. Rulings R-4218 / R-4219 (R-4207, R-4215).

## 1. DONE

| item | what | commit |
|---|---|---|
| M-3 `CE-4482` | `UtilityPrescore` retired: the class, its two `UtilityScorer` hooks and helper, the `BrainTickSystem` call, `HROT_BRAIN_PRESCORE`, its two rails (Q4 / T3 — "pre-score == serial score", now covered by the brain phase's own determinism rails); Parallel Simulation §14 / §15 marked SUPERSEDED | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|
| M-D1 | the handoff points the pre-score's record at Parallel Simulation §10–§11 | ⭐ it is §14 (POC 2) and §15 (POC 3) — §10–§11 never mention it; those are the sections marked superseded | design |
| M-D2 | does any host keep SERIAL brains and want parallel scoring (M-3's precondition)? | ⭐ no — measured in code: the pre-score needs `HROT_PARALLEL_SIM=1`, which itself makes the brains a parallel phase (`BrainTickSystem.Parallel`), so it ran only under the measuring switch `HROT_SERIAL_BRAINS=1`; by design R-4214 ① puts the per-entity brain rule in both modes | `BrainTickSystem.cs`, R-4214 |

## 4. BUGS FIXED

| bug | rail |
|---|---|

## 5. OPEN

| item | why | what it needs |
|---|---|---|

## 6. NUMBERS (before / after)

| what | before | after | how |
|---|---|---|---|

## NEXT (the resume point)
M-1 … M-9 in order; started at the merge of `coordinator` (`ffc802ab2`) into `exp/threaded-build`.
