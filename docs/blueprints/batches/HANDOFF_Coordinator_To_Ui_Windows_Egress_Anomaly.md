<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §7.4: "the off-frame egress alone makes the CGF slower" — unexplained; §7.2 F-15 (egress priority, a machine-wide core budget)
  - ../../DESIGN_Parallel_Simulation.md — §11.1 the anomaly as first measured (252 → 337–339 ms); §19 Windows scheduling
  - HANDOFF_Coordinator_To_Ui_Windows_Throttling_Measure.md — run that one FIRST; this follows in the same session
-->

# HANDOFF — coordinator → the Windows session: why does off-frame egress ALONE make the CGF slower?

**For:** the ui session on the user's Windows machine. **From:** coordinator. **Dispatched at `d6f7d0fb2`**. ⭐ AFTER the throttling measurement,
same session, never at the same time. 🔒 User, `2026-11-12`: *"yes, approved, queue them"*. ⛔ Every process HIDDEN. ⛔ **Measure only.**

## The anomaly (Parallel Simulation §11.1, reproduced twice)

On `exp/perf`, a SERIAL CGF frame with only off-frame egress + the parallel replica sync on (`FDP_EGRESS_OFFFRAME=1 HROT_PARALLEL_SYNC=1`)
went **252 → 337–339 ms** — BrainTick +36, WeaponDispatcher +12, ingress +12 ms, systems egress does not touch. With everything else on too it
disappears (163 ms). Suspects: CPU contention on the pinned CPUs (the scan and the replica copy steal the serial systems' cores — your scaling
run found unpinned nodes contend heavily), the egress scan at BelowNormal vs the frame, or cache / memory-bandwidth effects of the replica copy.

## The branch — exactly **`exp/perf`**, Release, `HROT_QUERY_TIMINGS=0`

| # | run (the 1 000-man rig) | answers |
|---|---|---|
| E-1 | serial, pinned (`--pin`) — the baseline · then `FDP_EGRESS_OFFFRAME=1` alone · then `HROT_PARALLEL_SYNC=1` alone · then both | which of the two causes it |
| E-2 | both on, with CGF on 6 cores of its own (`--cgf-cpus`, as in your scaling run S-3) | is it contention with the other nodes |
| E-3 | both on, `FDP_EGRESS_WORKERS=1` (the scan on one worker) and the pool budget lowered by one (`HROT_SIM_THREADS`) | is it the scan's worker count stealing cores |
| E-4 | per run: the systems' ms (`all_ms`), CGF threads, and if you can, CPU usage per thread (`dotnet-counters` / Process Explorer, hidden) | where the extra time goes |

## Report

`docs/blueprints/batches/STATUS_Ui_Windows_Egress_Anomaly.md`, pushed onto **`exp/perf`** only: the commands verbatim, the table, the noise
(one column repeated), and the verdict — the cause, or what is ruled out. ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
