<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §1 what exists (measured), §2 the lean, §4 the hazards, §5 the plan, ⭐ §7 determinism as a switchable mode (R-4207), ⭐ §8 interleaving and quantizing
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — §11h the per-system costs at 1 000 men this targets
  - HANDOFF_Coordinator_To_Backend_Threaded_Kernel_Experiment.md — the OTHER overnight experiment (the kernel on a worker thread); independent of this one
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — run the simulation on many cores, and do less per tick

**For:** a backend session. **From:** coordinator. **Dispatched at `<sha>`**, scope frozen there. ⭐ **An EXPERIMENT** — 🔒 user, `2026-11-12`:
*"Can we do the independently on the overnight backend session, in another experimental branch? If it needs windows machine, we can (8 physical core
machine)"* — and earlier: *"Single thread calculations of thousands of entities is ineffective … Many of those could be ok with writing stuff via command
buffers, allowing parallel runs."* · *"If Determinism mode goes against performance mode … needs to be switchable."* · *"writing every frame and
calculating every frame is what adds time. Interleaving and quantizing might help."*

⛔⛔ **Autonomous — never stop for a question** (R-4156). Every decision is YOURS: take it, record it with its claim table in your status file, move on.
A failed premise stops THAT item only (R-106). ⭐ You may change or overturn anything in the design when the code or a measurement shows a better way —
say so with the evidence. The user judges the result in the morning.

## Where to work

⭐ **A NEW branch `exp/parallel-sim`** from `coordinator` — ⛔ not `backend`, and ⛔ **independent of `exp/threaded-kernel`** (the other overnight
experiment moves the kernel to a worker thread; this one speeds up the tick itself — they add up later, do not merge one into the other). Push the
branch as you go. On `backend` push only `chore: started …` and the status file `STATUS_Backend_Parallel_Simulation_Experiment.md` (with the
experiment branch's sha). ⛔ Nothing merges into a lane without the user's word.

**The machine:** the cloud VM has 4 cores — enough to build, to prove CORRECTNESS and to see a trend. ⭐ **Real speed-ups need real cores: the user's
Windows machine has 8 physical cores** and this session may be run there. On Windows, every run with a window is HIDDEN (`HROT_HIDDEN_WINDOW=1`; ⛔ no
window may appear — the user works on that machine). If you run on the VM, leave a one-command measurement script for the Windows run and say so.

## The situation

The design: [DESIGN_Parallel_Simulation.md](../../DESIGN_Parallel_Simulation.md) — read it whole. In short:

- ✔ The scheduler runs a phase's systems one after another (`SystemScheduler.cs:222-231`).
- ✔ A zero-allocation data-parallel loop EXISTS (`EntityQuery.ForEachParallel`, adaptive batches) and per-thread command buffers EXIST
  (`EntityRepository.View.cs:16-48`) — used by car kinematics, the raycast solver, hull clearance; ⛔ NOT by brains, weapons, stress, callouts, threat,
  squads — the CGF systems that cost the most at 1 000 men (Evaluation Cost §11h: the synchronous sim 38 ms — brains 9.8, weapons 7.3, stress 5.7,
  callouts 3.7, threat 2.0, squads 1.7, ~25 small ≈ 8; PostSimulation 9.5).
- ✔ The hot non-brain systems loop over entities and write only their OWN entity (stress, callouts, weapon dispatcher). The brain writes ~100 sites,
  some cross-entity.
- ⛔ Per-thread playback order is not deterministic ⇒ per-BATCH buffers merged in batch order (§4, §7.1 — costs about one copy of the events).
- ⭐ R-4207: determinism is a MODE (`Deterministic` for stepped mode, Run-to, filming, regression, tests; `Throughput` for continuous live play),
  chosen by what is running. Its real price is the WAITS (stepped mode waiting for background jobs), not the buffers (§7.2).
- §8: interleaving (1 / k of the entities per tick, the ELAPSED dt rule — ⚠ ui measured the brain's cadenced tick getting one frame's dt, HSM timers
  running slow) and quantizing (✔ `GetRefRW` stamps the whole 64 KB chunk even when the value does not change).

## The experiment — in this order, as far as the night goes

| # | what | done when |
|---|---|---|
| **E-1** | ⭐ **the baseline and M-1** — the 1 000-man rig (`scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10`; 3 000 if it runs) in the editor's preview (or the CGF node), per-system ms (`KernelFrameTimings`, `HROT_FRAME_STATS`, `/diagnostics/architecture`); and M-1: per table, writable references handed out, how many left the value unchanged, chunks ended dirty | the baseline table + M-1's table in the status file |
| **E-2** | **P-0: the deterministic per-batch merge** in `ForEachParallel` (per-batch buffers and event lists, merged in batch order) + the **serial-vs-parallel rail harness** (one world, run serial and parallel at 1 / 2 / 4 / 8 threads, compare byte for byte) + the `SimulationMode` switch (R-4207) | the harness green on the existing parallel systems; the merge's own cost measured |
| **E-3** | **P-1: convert the self-writing hot systems** — stress, callouts, weapon dispatcher, threat evaluation, squad coordination, drone impacts, fire processing — each with its serial-vs-parallel rail | each system's ms before / after at 1 / 2 / 4 / 8 threads |
| **E-4** | **P-5 / P-6, where E-1's numbers point**: the shared interleave helper (the elapsed-dt rule first) on the slow-changing systems; check-before-write / quantizing in the tables M-1 names | the saving per system or table, and its stated error vs every-tick |
| **E-5** | *(stretch)* **P-2 / P-3 the brains**: the inventory of their writes (self / cross-entity / bus / shared state), a Debug guard on writes to another entity, then the tier loop in batches | as far as the night goes — the inventory alone is valuable |
| **E-6** | **the verdict** — the 1 000-man tick before / after (and 3 000), on the VM and, if run there, on the 8-core Windows machine; what was hard; the real cost to finish; go / change / stop | a short section at the top of the status file |

## Rules

- ⭐ **Decide, record, continue.** No item waits for an answer.
- **Correctness first:** a converted system ships only with its serial-vs-parallel rail green in `Deterministic` mode; the determinism rails and the
  scenario regression on the branch at the end (a red is a finding).
- **0 B garbage per steady tick** in every parallel body (R-2084).
- **One core budget:** `FdpConfig.MaxDegreeOfParallelism` — leave cores for the main thread (cores − 2 is the lean).
- Run anything slow in the background; never poll it. ⛔ No screenshots or recordings in git.

**Done when:** E-6 is written — however far E-5 got.

---
_coordinator lane, 2026-11-12_
