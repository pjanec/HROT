<!--STATUS
state: LIVE
updated: 2026-11-10 (many-core results)
current-answer: §0 START HERE (the next session runs on a WINDOWS machine with many cores) → §1 set the machine up → §2 make the rig run natively → §3 the MEASUREMENTS to make → §4 the optimisation backlog, decided by §3's numbers. §5 the traps, §6 where everything lives.
stale-below: nothing
related-designs:
  - docs/DESIGN_Scale_Rig.md — ⭐ the rig, the budgets and EVERY measurement so far (§4 baselines, §5 L-4 load + garbage, §6 the world-query timings: §6.1 first numbers, §6.2 the sensor scans, §6.3 the fight, §6.4 what needs a physical machine)
  - docs/designs/navig-2/Navigation_Design_v2_0.md §15 — the bounded A* and CircleSearch (our zero-garbage Dijkstra / A*), §14 the tile cache (one folder per square)
  - docs/DESIGN_Evaluation_Cost_And_Cadence.md §11b — the "whole-world scan per question" levers table (CE-3427's row)
  - docs/DESIGN_Sensors_And_Doctrine.md §5.3–5.4 — the EQS budget (counted work, deterministic order) a parallel solver must keep
  - docs/DESIGN_Terrain_World.md §11 — trees (woods grid, lone trunks, woods in the navmesh, fire, the tracked speed cap)
  - docs/blueprints/batches/STATUS_Backend_Large_Terrain.md — the large-terrain handoff L-1…L-8 (L-6 waits on the coordinator's CE-4371)
  - FDP/Docs/projects/core/ModuleHost.Core.md — how modules are scheduled (SlowBackground = async, own snapshot, 10 Hz)
-->

# RESUME — backend: large-terrain PERFORMANCE (sensors, EQS, paths) on a MANY-CORE machine

## 0. ⭐⭐⭐ START HERE

### ⭐⭐⭐⭐ DONE on the many-core machine (`2026-11-10`, LATEST) — read this first

§1 machine set up (SUMO via `scripts/install-sumo.ps1`, scale-1 regenerated) · §2 the rig runs natively on Windows (`9a4b05053`,
`scripts/cluster_launch.py`, `--runner-dll`, `--pin`) · §3 M-1…M-5 measured (`8769f25fe`) → **[DESIGN_Scale_Rig §7](../DESIGN_Scale_Rig.md)**.

| finding | ⏭ waits on |
|---|---|
| ⭐ the EQS runs **2.9×/s, not 10**: the kernel's live cadence counts `60/Hz` frames from the HARVEST (230 ms idle after a 120 ms run). Opt-in `HROT_SLOW_WALLCLOCK=1` (wall clock from dispatch — the user's stated intent) ⇒ 7.1 runs/s, 2.2× the solves | ✅ **C-1 BUILT** (user: yes) — 6.8 runs/s, 2 790 solves/s ([§7.6](../DESIGN_Scale_Rig.md)) |
| ⭐ the cold bake is **GC-bound**: 322 s workstation GC → 127 s server GC with 4 heaps (peak 2.5 GB) | ✅ **G-1 BUILT** (user: yes) — cold load 119 s, peak 2.74 GB (§7.6) |
| the rig's frame figure added background runs: SimHost frame **36 ms** (not 173); manual systems now named by their module | done |
| P-1 parallel EQS | ✅ **BUILT** — run 116 → 42 ms (1 000 men), 211 → 81 ms (3 000), 1.8× solves at 3 000 ([§7.7](../DESIGN_Scale_Rig.md)); ⏭ next limits N-1 the SimHost frame, N-2 the band shares (raise the budget?) |

🔒 **User, `2026-11-10`:** *"I will continue with this on another session on a windows machine with more cores. So the resumption should
instruct to make the measurements and continue optimizing."* · earlier: *"focus on terrain dependent stuff like the eqs and sensor stuff
timing only with 1000 men moving and fighting … We can run without the recorder. Brain costs can be ignored."* · *"Today's machines have 16+
cores … let me know when [a physical machine] is necessary."*

**Where it stands (backend `00cf6a8ec`, `2026-11-10`):** every whole-world SCAN and every allocating search on the sensor / EQS / path
route is gone (numbers in §3's baseline column). ⭐ **What is left is GENUINE terrain work** — sight lines for the tactical EQS tests
(`CheapLineOfSightTest`, `ThreatExposureTest`), run on ONE background thread. ⇒ the next lever is **parallel solving**, and THAT is the
question this machine answers.

**The order, fixed:**

1. `git fetch` + merge what you build on (`backend` itself; check `coordinator` for CE-4371, which unblocks L-6 — see STATUS file).
   `RELEARN` (CLAUDE.md), read this file's §0–§3, then [DESIGN_Scale_Rig §6](../DESIGN_Scale_Rig.md).
2. **§1** — set the machine up (SDK, Python, the scale-1 terrain is NOT in git — regenerate it).
3. **§2** — make `scripts/scale-rig.py` start / stop / trace nodes NATIVELY on Windows (it is Linux-only today). ⛔ Do not measure before.
4. **§3** — the measurements, in order, written into DESIGN_Scale_Rig as a new §7 ("on a physical many-core machine").
5. **§4** — optimise by those numbers; every fix: rail red-proved, fight re-measured, design updated, commit by path, push `backend`.

## 1. ⭐ The machine

| need | how | ⚠ |
|---|---|---|
| .NET 8 SDK, Python 3, Git | as usual; `dotnet --info` shows the core count via `Environment.ProcessorCount` too | — |
| ⭐ a RELEASE runner | `dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release` | ⛔ every number so far is Release; Debug is ~2–4× slower |
| ⭐ the scale-1 terrain | `python scripts/terrain-scale.py --trees` → `assets/terrain/scale-1/` (gitignored; ≈ 140 MB). It runs `hrot-terrain-synth --preset full --seed 1` first when `artifacts/terrain-synth/full-1/` is missing | ⚠ the synth set needs SUMO's **netconvert** on PATH (`pip install eclipse-sumo` = 1.20.0, as used); without it the set has no `roads.net.xml` and the script stops with that message |
| the navmesh tile cache | first load bakes **106 260 tiles ≈ 8 min** (cold, 4 cores) into `HROT_NAVTILE_CACHE` (default `%LOCALAPPDATA%\Hrot\navtiles`); every later load is warm | ⛔ never measure LOAD on a cold cache; ⭐ the cold bake itself is a useful many-core number (it is a `Parallel.For` over tiles) |
| `dotnet-trace` | `dotnet tool install -g dotnet-trace` | ⚠ its sampled thread time LOST the frames below the EQS solver here (§5) — use the query slots first |

## 2. ⭐ Make the rig run natively on Windows *(first build task)*

📐 **Today:** `scripts/scale-rig.py` imports `scripts/perf-stress.py`, whose node control is `scripts/cluster-node.sh` (bash: `setsid`,
`nohup`, `xvfb-run`, `pgrep`, `/proc/<pid>/environ`) and `trace_nodes()` (reads `/proc`). ⛔ None of that exists on Windows.

⭐ **The precedent to copy (one implementation, do not invent a second):** `scripts/multiplayer-cycle.py` — *"Plain Python 3, standard
library only; runs UNCHANGED on Windows and Linux (subprocess.Popen, terminate/kill — no killpg, no xvfb-run)"*: `start()` launches
`dotnet <Hrot.ClusterRunner.dll> --mode … ` with `HROT_DEBUG_API_PORT` in the env, keeps the `Popen`; `stop()` terminates it; an Xvfb only on
a Linux box with no DISPLAY.

| ⭐ the change | |
|---|---|
| `perf-stress.py`'s `node("start"/"stop-all", …)` → a Python process table (Popen per node, kept by port) on Windows; keep `cluster-node.sh` on Linux, or move both to the Python path | ⭐ the rig, perf-stress and the multiplayer cycle then share ONE launcher — fold it into a small module both import |
| `trace_nodes()` finds the pid by `/proc` | ⭐ use the Popen's own `pid` |
| the rig passes `--headless` and `HROT_HEADLESS_MAX_HZ=240` | keep (an unpaced headless node spins a core, CE-3422) |
| `scratchpad/rig.sh` (sets `HROT_RUNNER_DLL` to the Release dll) | ⭐ a `--runner-dll` argument instead (multiplayer-cycle has one) |
| ⛔ the `pkill -f` self-kill trap does not exist on Windows | ⭐ but never kill by image name (`taskkill /im dotnet.exe`) — it kills the session's own build servers and the test host |

✅ **Done when:** `python scripts/scale-rig.py --terrain scale-1 --men 96 --sides 1 --runner-dll <Release dll>` prints the table on Windows.

## 3. ⭐⭐⭐ The measurements — in this order, each written into DESIGN_Scale_Rig §7

⭐ Always: Release, warm tile cache, **recording OFF** (the rig's default since CE-3427; `--record` to keep it), `--queries 30`,
`--json <file>` kept, and **note the machine** (CPU model, cores, RAM, Windows build). ⭐ Run each twice; quote the second.

| # | run | the command | what it answers | ⭐ compare with (4-core cloud VM, `2026-11-10`) |
|---|---|---|---|---|
| **M-1** | the march | `scale-rig.py --terrain scale-1 --men 1000 --sides 1` | the per-node frame and the query table without a fight; movers moved (CE-3424) | load 34.8 s · one EQS run 23 ms · hearing 14 µs, sight 9 µs a solve |
| **M-2** | ⭐ the FIGHT | `… --men 1000 --sides 2` | THE target load. Per query: `eqs.sensor (all)`, `eqs.test:*`, `nav.PlanPath`, `nav.PathCosts`, `terrain.SegmentBlocked`; per node: work ms/frame, garbage, frames/s | SimHost 195 ms work/frame at 49 fps; `EqsSolverSystem` 126 ms a run; `CheapLineOfSightTest` 440 µs and `ThreatExposureTest` 375 µs a batch; `nav.PlanPath` 183 µs avg / 31 ms worst; `nav.PathCosts` 168 µs; SimHost garbage 0.37–0.43 MB/frame; 10 / 12 movers moved |
| **M-3** | ⭐ the EQS HEADROOM | from M-2's numbers: `EqsSolverSystem` ms per RUN (the profiler's per-execution average) vs the module's 100 ms period (10 Hz), and each sensor's refresh = `eqs.sensor (all)` solves per second ÷ sensors (2 per man) | ⭐⭐ **the parallel decision** (§4 P-1): a run ≤ ~60 ms ⇒ one thread holds 10 Hz with room; ≥ 100 ms ⇒ perception goes stale under load and parallel solving pays | 126 ms a run in the fight on this VM ⇒ already over |
| **M-4** | scale it | M-2 with `--men 2000` (and 3000 if it holds) | how each cost grows with the men — linear is fine, worse is a scan we missed | — |
| **M-5** | the cold bake | delete the tile cache folder, load scale-1 once (`scripts/terrain-load-probe.sh` is bash — or the rig's load time on a cold cache) | the bake's many-core scaling (475 s on 4 cores) | 475 s |
| **M-6** | a trace, only if a cost is unexplained | `--trace <dir>` (after §2) | which METHOD — ⚠ check the answer against the slots (§5) | — |

⭐ Two numbers the budget table needs and only this machine gives (DESIGN_Scale_Rig §6.4): **load ≤ 30 s** and **frame ≤ 10 ms per node** —
⚠ the rig runs the three nodes on ONE machine; say so, and if possible pin each node to its own cores (`start /affinity` or
`Process.ProcessorAffinity`) to approximate one node per machine.

## 4. ⭐ The optimisation backlog — ranked by the last measurement; RE-RANK by §3's

| # | item | measured | the lean | ⚠ constraints |
|---|---|---|---|---|
| **P-1** | ⭐⭐ **parallel EQS solving** (SimHost `EqsSolverSystem`, today one thread in `EqsModule`, `SlowBackground(10)`) | 126 ms a run in the fight (VM) | ⭐ **build it IF M-3 says the run cannot hold 10 Hz.** Shape: plan the tick's set in the schedule's order (counted cost), SOLVE the chosen sensors in parallel into per-sensor result buffers, APPLY (publish, memory stage, pool, twin answers) on the solver thread IN ORDER. Speculative chunks keep the exact start rule: solve the next K in parallel, accept in order while `ShouldStart` holds, discard the rest (their solves must be pure) | 🔒 **deterministic** — the same scenario must solve the same sensors (Sensors & Doctrine §5.3); a solve's SIDE EFFECTS today: the shot-ring cursor (`AcousticPerception.HearRing` → `view.GetCommandBuffer()`, per-thread buffers — commutative, one per sensor), the result pool (`EqsResultPool.NextFreeIndex`), `SensorMemoryStage`, `_shared` twin answers, `_surveys`, raycast submission (`AccurateLineOfSightTest`). ✅ already per-thread: the broadphase (CE-1055), the sight batch (CE-1160), the door / cloud / clock / weather caches, `CircleSearch`. ⚠ audit every test / generator type for shared mutable fields before. ⭐ Design FIRST (UML, in Sensors & Doctrine §5.4) — it is a large-blast-radius change: the lean goes to the user |
| **P-2** | the sight-heavy tests themselves | `CheapLineOfSightTest` 440 µs a batch (candidates × ≤ 4 body points × a 1.8 µs sight line + a fire trace), `ThreatExposureTest` 375 µs (candidates × threats × sight) | ⭐ look for SHARED work before parallel: the two tests in one template may trace the same lines; a per-run memo of (from-cell, to-cell) → visible; order candidates so an early "visible" ends the aim loop | ⛔ a memo must be keyed so it can never answer for a door / smoke state it did not see |
| **P-3** | the A* worst case | 31 ms (avg 183 µs), 20 000-expansion cap rarely hit (0.05 a frame) | measure M-4 first; a hierarchical / tile-level pre-search only if long paths dominate | `CircleSearch.RunAStar` = Detour's rules; its rail compares with the plain `FindPath` |
| **P-4** | the path solver itself | `PathfindingSolverSystem` 23–27 ms a run; ≤ 256 requests a tick, EXCESS DROPPED (`PathfindingBatchData.DefaultCapacity`) | ⭐ count the drops (a `QueryTimings.Add` counter) before anything — a dropped request is a man who waits (CE-3424) | — |
| **P-5** | CE-3424 — men barely move | 10 / 12 moved in the fight after the path fixes (1 / 12 before) | re-measure M-1; close the row if the march moves | the squad re-order rule is behaviors' (`SquadMarchBehavior`) |
| **P-6** | the load (34.8 s on the VM) | navmesh warm 16 s of it (one process) | M-5 / M-1 first — likely within budget on this machine | — |
| ⛔ | NOT this programme | `BrainTickSystem` (behaviors; user: ignore), the recorder (off), `CycloneEgressSystem` (~12 ms — real NICs, §6.4) | — | — |

## 5. ⛔ Traps — each cost time here

| trap | what to do |
|---|---|
| ⭐ `dotnet-trace`'s sampled thread time charged 97 % of the EQS solver to `QueryTimings.End` — it lost the frames below | ⭐ add a `QueryTimings` slot around the suspect (`Begin`/`End`, `Bytes()` for garbage, `Add` for counts) and read `/diagnostics/queries`; that is how all of §6.2 was found |
| a "passed" after a FAILED build | `--no-build` runs the old binary: check the build's error count first (it happened twice here) |
| stale test binaries | build the TEST project, then `--no-build` (CLAUDE.md) |
| DotRecast allocates | its `DtNodePool` allocates 88 B per node per query (measured on the package) — ⛔ do not call `FindPath` / `FindPolysAroundCircle` / sliced search on a hot route; use `CircleSearch` |
| a snapshot's `SyncFrom` does NOT count structural changes or adds | ⭐ caches keyed on `StructuralChanges` / `ComponentsAddedOf` also check the frame (`GlobalVersion`, which a sync DOES copy) — see `DoorStates.Of`'s comments |
| the cluster pauses itself after losing a node | measure inside the rig's window; ⚠ the CGF node was lost once right after a window on the VM (heartbeat under load) — note it if it recurs |
| the cold tile cache | the first scale-1 load bakes ≈ 8 min (VM); ⚠ the old flat cache layout listed 115 000 files per write — fixed (one folder per square), a cache from an older build is simply re-baked |
| rig readiness | a node reports `PlayReady` (not `Ready`) — the rig accepts both |

## 6. Where everything lives

| what | where |
|---|---|
| per-query timings | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/QueryTimings.cs` · `GET /diagnostics/queries[?reset=true]` · the rig's per-node table (ms/frame, calls/frame, avg/max µs, KB/frame, counters) |
| the slots | `terrain.*` (TerrainWorld), `world.Trace.*` (TerrainWorldQuery), `nav.*` (DotRecastNavmeshProvider), `eqs.*` (EqsSolverSystem, EqsFilters), `percept.*` (VisionBroadphase, AcousticPerception), `doors.*`, `clouds.*`, `cover.*` |
| our search | `FDP/Toolkits/Fdp.Toolkits.Navigation.Recast/CircleSearch.cs` (`Run` = the path-cost Dijkstra, `RunAStar` = the A*) |
| the EQS solver | `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` (schedule loop `Execute`, `RunSensor`, `EvaluateSensor`), module `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` |
| the rig | `scripts/scale-rig.py` (+ `scripts/perf-stress.py` machinery, `trace_nodes`) · the terrain `scripts/terrain-scale.py --trees` · `hrot-trees`, `hrot-heights` tools |
| the rails of this programme | `QueryTimingsTests`, `TerrainWorldTests.CE3427_*` (doors), `WeatherFieldTests.CE3427_*` (clock, weather), `RecastNavmeshFactoryTests.CE3427_*` (A*, path costs), `TerrainWoodsTests` |
| the tracker | CE-3427 (this programme), CE-3424 (men not moving), CE-3425 (other frame costs), CE-3426 (felling) — `docs/blueprints/Blueprint_Issues_Tracker.md` |

⭐ Standing rules that apply all the way through: commit by path on green, push `git push origin HEAD:backend`; builds and runs in the
BACKGROUND; recorded components unmanaged; no allocation / LINQ on a hot route (a rail proves 0 B); every fix's numbers into the owning
design; gloss ids and give GitHub links in chat.
