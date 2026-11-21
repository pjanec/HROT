<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §12–§17 (on exp/perf): what the branch carries
  - HANDOFF_Coordinator_To_Ui_Windows_Perf_Measure.md — the Windows measurement of the same branch, running in parallel
-->

# HANDOFF — coordinator → backend: VALIDATE `exp/perf` from every side, and keep working there

**For:** a backend session. **From:** coordinator. **Dispatched at `07e178275`**, scope frozen there.
🔒 User, `2026-11-12`: *"ok to merge both into a single but still experimental branch. If that one is ok from various perspectives, it be merged to
all other lanes. Until then lets keep working in experimental backend branch. I do not want other lanes accidentally import the backend yet."*
⛔⛔ **Autonomous** (R-4156); a failed premise stops THAT item (R-106).

## ⛔ The branch rule

**`exp/perf`** = `exp/parallel-sim-4` (POC 1→4 of the simulation's throughput, stacked) + `exp/managed-reads` (R-4210), merged by the
coordinator (the duplicate managed-read implementation from POC 3 dropped, the audited one kept). ⭐ **Backend's performance work continues ON
`exp/perf`** until the user approves it. ⛔ **Never merge `exp/perf` into `backend`, `ui`, `behaviors` or `coordinator`, and never merge
`backend` into another lane while it carries any of it** — the user decides when it goes to every lane at once.

## Validate — "ok from various perspectives"

| # | perspective | how | done when |
|---|---|---|---|
| **V-1** | **the build and the suites** | the whole solution once; the unit suites of every project the branch touched (`Fdp.Core`, `Fdp.ModuleHost`, `Fdp.Toolkits`, `Hrot.Network.NED`, the AI behaviour projects, `Hrot.Editor`) — `--no-build` after the one build | each suite's pass / fail / skip vs `coordinator` at the merge base, every red named pre-existing (base sha) or new |
| **V-2** | **defaults unchanged where they must be** | every opt-in switch OFF ⇒ the simulation's output is the base's: the determinism rails, a recording made on `coordinator` and on `exp/perf` replay to the same hashes | the rails green; the hashes equal |
| **V-3** | **determinism** | `DeterminismRails`; every serial-vs-parallel rail at 1 / 2 / 4 / 8 threads | green |
| **V-4** | **the scenario regression** | the full regression (background, ~3 h per mode) — serial, and with the Throughput switches on | the pass list both ways vs `coordinator`; each new red root-caused |
| **V-5** | **replication and recording** | the replication suites (Egress §7's gate list), record → replay → seek, the managed-read detector (`HROT_MANAGED_MUTATION_CHECK=1`) over the scenario subset | green; detector silent on recorded types |
| **V-6** | **the editor and the UI** | the editor's feature rails and the frame rails under `xvfb-run -a` | green vs base |
| **V-7** | **allocation** | the 0 B rails; the rig's garbage per frame vs base | no regression |
| **V-8** | ⭐ **the verdict** — merge to all lanes or not, with what must be fixed first | top of `STATUS_Perf_Branch_Validation.md` ON `exp/perf` | |

Fix on `exp/perf` what V-1…V-7 find (small, railed); anything larger is listed for the user.

---
_coordinator lane, 2026-11-12_
