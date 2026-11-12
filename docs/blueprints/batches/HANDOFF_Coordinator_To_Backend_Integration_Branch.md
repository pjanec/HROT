<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — ⭐ §7 the critical review (P-1 the common base; §7.4 the unmeasured assumptions) and §7.1a the user's decisions R-4214 / R-4215
  - HANDOFF_Coordinator_To_Ui_Windows_Egress_Anomaly.md — the Windows half
-->

# HANDOFF — coordinator → backend (VM): build the ONE integration branch, then measure memory and same-tick contact

**For:** the free backend VM session (it finished CE-4454). **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen.
🔒 User, `2026-11-12`: *"yes, approved, queue them"* — the integration branch (critical review P-1) and the measurements §7.4 names as never
planned. ⛔⛔ **Autonomous** (R-4156); ⛔ NO broad suites or scenario regressions (user: test the changed features).

## Part 1 — `exp/integration`: the one base the overnight build starts from

| # | item | done when |
|---|---|---|
| **I-1** | a NEW branch **`exp/integration`** from **`exp/perf`** (merge-ready, CE-4454 P-3), then merge in order: **`exp/threaded-poc-4`**, **`exp/read-check`**, **`exp/measure-2`** (its CURRENT head — another session is still finishing it; re-merge it when its K-5 verdict lands), and **`coordinator`**. Known conflicts (critical review P-1): `EntityRepository.cs`, `AccessTrace.cs`, `DESIGN_Threaded_Simulation_Presentation.md` — resolve by keeping both features; ONE read path (the audited `ThreadedReads.PureReads`), ONE pool (`FdpWorkerPool`) | the full solution builds once, 0 errors; then `dotnet restore` + build of each touched test project and its OWN rails: `TwoWorldsTests`, `PureReadsTests`, `ParallelEntitiesTests`, `ParallelSyncTests`, the read-check rail, the census rail (`EntityCensusTests`), `DeterminismRails`; every red named pre-existing (base sha) or a merge defect, fixed |
| **I-2** | push `exp/integration`; the status file `STATUS_Integration_Branch.md` on it names every merge, every conflict and how it was resolved | ⛔ never push to the merged branches or any lane |

## Part 2 — measurements on `exp/integration` (critical review §7.4)

| # | measurement | done when |
|---|---|---|
| **N-1** | ⭐ **MEMORY per node**: the 1 000-man rig (and 3 000 if it runs), each node's working set and managed heap, broken down where possible: the world S, the presentation world A (threaded editor `HROT_THREADED_KERNEL=3`), the replica R (off-frame egress on), the last-tick copy (`LastTickProbe`), SoD background snapshots, the undo `WorldSnapshotRing`, recorder keyframes, pooled snapshots — against the ≤ 4 GB per node budget (`DESIGN_Scale_Rig.md`); and the same in `--mode all` (several nodes in one process) | a table per node and per mode; what dominates; whether 5 000 men fits |
| **N-2** | **same-tick cover and contact**: a contact-onset scenario (two sides closing; the heaviest combat scenario in `scenarios/`), counting per tick how many units COMMIT to a cover point and how many see a NEW contact on the same tick — and how many pairs commit to the same / overlapping point on one tick | the distribution; it sizes the cover recovery (F-9) and the callout de-duplication (F-10) |

Report in `STATUS_Integration_Branch.md` (Part 1) and `STATUS_Memory_And_Contact.md` (Part 2), both on `exp/integration`. Run anything slow in the
background; never poll. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-12_
