<!--STATUS
state: LIVE — in progress
updated: 2026-11-12
current-answer: A-5 the verdict (top, when written) · then A-1…A-4
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §13 (the analysis this batch tests) · §12 (the experiment)
  - HANDOFF_Coordinator_To_Backend_Threaded_Analysis_Instruments.md — the dispatch (at 25bce1e00)
  - STATUS_Threaded_Kernel_Experiment.md — the previous experiment (PublishProbe, ThreadedKernelGate)
-->

# STATUS — the threaded-analysis INSTRUMENTS (evidence, not a solution)

**Where:** branch [`exp/threaded-analysis`](https://github.com/pjanec/HROT/tree/exp/threaded-analysis) — from `exp/threaded-kernel` (`9a2bf95c3`)
with `coordinator` merged (`a5a07fd53`; the one conflict, the design's §12 vs §13, resolved by keeping both). ⛔ Nothing goes to a lane — user,
`2026-11-12`: *"the handoff is for separate branch. It's not for pushing into backend."* (so not even the `chore: started` / status copy the
handoff names). Every instrument is opt-in (an env switch, default OFF) and changes no behaviour.

## A-5 — the verdict

⏳ not yet.

## The items

| # | instrument | state | sha | note |
|---|---|---|---|---|
| A-1 | runtime access tracer (`HROT_ACCESS_TRACE=1`) | ⏳ building (worker) | | |
| A-2 | static reachability (Roslyn) | ⏳ design (Opus) | | |
| A-3 | the treatments' costs (`PublishProbe` extended) | | | needs A-1's masks |
| A-4 | presentation-out-of-the-tick dependencies | | | |
| A-5 | verdict + fold into §13 | | | |
