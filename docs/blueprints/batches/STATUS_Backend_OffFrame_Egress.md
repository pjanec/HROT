<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: §1 the B-0 baseline gate table (before any code changed) · §2 the build log B-1…B-9
stale-below: nothing
related-designs:
  - docs/DESIGN_Egress_Change_Detection.md §7 — the design this experiment builds (leans O-A…O-G approved)
  - docs/blueprints/RESUME_Backend_Large_Terrain_Perf.md §0 — the build plan B-0…B-9 and the ground rules
-->

# STATUS — backend: the state egress OFF the main thread (CE-3427 N-1, an experiment)

## 1. B-0 — the baseline, before any code changed (base `eea4a6d54`, cloud VM, 4 cores)

Built each test project (with restore — another lane's merge came in), then `dotnet test --no-build`, `HROT_HIDDEN_WINDOW=1`.

| suite | passed | failed | the reds (all pre-existing: this IS the base) |
|---|---|---|---|
| `Fdp.Network.Cyclone.Tests` | 102 | 0 | — |
| `Hrot.Network.NED.Tests` | 196 | 0 | — |
| `Hrot.SimHost.Tests` | 2 636 (6 skipped) | 9 | `ShippedTreesAreCSharp` ×6, `StrictNetworkSeparation` CE4208, `CgfLogicPack` CE454, `LoadPhaseChain` CE3345 (flaky, as the resume doc says) |
| `Hrot.ClusterRunner.Tests` | 411 | 6 | `CE1436_CgfShapedHost…`, `EverySystemHoldingEntities…`, the two `EverySystemStateFieldIsClassified`, `EveryListedWriter_ReadsTheReplayFlag`, ⚠ and `CE1446_TheKernelRunsItsSystemsInTheSharedBootstrappersOrder` (not in the resume doc's list of 5) |
| `Fdp.Toolkits.Tests` | (running) | | |
| `Hrot.ClusterRunner.Integration.Tests` | (running) | | |

## 2. The build

(filled as each step lands)
