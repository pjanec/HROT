<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: "Where it stands", then P-1 … P-5
related-designs:
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — §11h "measured on Windows, preview" carries these numbers; §3 D6 (a soldier cadence is a later POLICY)
  - STATUS_Ui_Windows_Render_Under_Load.md — the first preview numbers (update 115 ms, ~40 ms named) this run attributes
  - STATUS_Ui_Windows_Replay_Speed.md — the same machine and rig, replay instead of preview
-->

# STATUS — where the editor's frame goes while a 1 000-man PREVIEW plays (ui; CE-4433)

Handoff [HANDOFF_Coordinator_To_Ui_Windows_Preview_Frame.md](HANDOFF_Coordinator_To_Ui_Windows_Preview_Frame.md), dispatched at `bc0c93253`; started at `2e883738f`
(merged `backend@8ccb92a32`, `coordinator@ee1c3ca80`). The user's Windows machine, Release, **every run hidden** (`HROT_HIDDEN_WINDOW=1`), **uncapped**
(`--target-fps 0`), panel capture off (`HROT_PANEL_CAPTURE=0`, the user's path). The rig regenerated (`scale-rig.py --terrain scale-1 --men 1000 --vehicles 50
--aircraft 10 --write-only`), opened for editing (28 s), **Play** (preview, continuous clock), 60 s to let the fight develop, then per view 30 s playing and 30 s
paused. Frame times are the hidden render target's (GPU-honest, no vsync). Three runs (base, base2, and the cadence runs) — base2 shown, base agrees within 1 ms.

## Where it stands

| | |
|---|---|
| ⭐ playing vs paused, 3-D mid | **98.8 ms (10.1 fps) vs 32.4 ms (30.9 fps)**; update **79.1 vs 12.3 ms** — the simulation costs **~63–67 ms on the drawing thread** |
| ⭐ the "~75 ms nobody named" | **named**, to within 1 ms (P-2): the synchronous sim module 38 ms (brains 9.8, weapons 7.3, stress 5.7, callouts 3.7, threat 2.0, squads 1.7 + ~25 small ones ≈ 8), the PostSimulation phase 9.5, **the two snapshot copies for the background position search and navigation 8.3**, Input phase 3.6, gizmos 2.1 |
| ⭐ soldiers' brains at a cadence (P-4) | **saves only ~4 ms** (brain tick 9.8 → 5.4 ms at 0.2 s; frame 98.8 → 91.8 ms) — brains are a sixth of the sim's cost |
| the lean (P-5) | **(c) the synchronous simulation on its own thread** is the one that makes the UI independent of the sim; (b) the named non-brain costs are the near-term cuts; (a) interleaving brains is worth ~4 ms |

## How it was measured — the new instrument

⭐ **`KernelFrameTimings`** (`FDP/Engine/Fdp.ModuleHost/KernelFrameTimings.cs`, CE-4433) — the kernel's frame BY PART, opt-in under `HROT_FRAME_STATS=1` (off: one bool
test per part). It names what lies BETWEEN the systems the scheduler already times: each phase, each sync point, each background module's view acquire (the snapshot
copy) and harvest (its command playback), each synchronous module's whole run, the frame-synced wait. 0 B per frame once warm — rail
`KernelFrameTimingsTests.ASteadyFrameOfParts_Allocates0B_AndDrainsTheirTimes`. Printed by the runner's `FrameStats` as `[FrameStats]   kernel (ms · calls · KB per frame)`.
The per-system split is `/diagnostics/architecture` (`recentAvgMs` per execution; every synchronous module runs once per frame).

## P-1 — the drawing thread, playing vs paused (base2; ms)

| | 3-D mid playing | 3-D mid paused | 2-D whole playing | 2-D whole paused |
|---|---|---|---|---|
| frame avg (fps) | **98.8 (10.1)** | 32.4 (30.9) | 132.8 (7.5) ⚠ | 29.3 (34.1) |
| worst 2-s window's p95 · worst frame | 175.7 · 175.7 | 48.4 · 68.9 | 170.9 · 170.9 | 35.2 · 57.7 |
| update · draw · UI | 79.1 · 14.5 · 5.1 | 12.3 · 14.5 · 5.5 | 115.5 · 11.3 · 5.8 | 14.2 · 9.5 · 5.5 |
| the kernel's whole Update | 74.6 | 11.6 | 109.7 | 13.5 |

⚠ 2-D whole in base2 measured later in a heavier fight (110 ms kernel); run `base` measured the same view at **98.0 ms / update 82.6** and every cadence run at 92–94 ms —
the view itself costs no more than 3-D mid (the draw is cheaper: 10–11 vs 14 ms).

### The kernel's frame by part, 3-D mid (ms per frame; `KernelFrameTimings`)

| part | playing | paused | the sim's share |
|---|---|---|---|
| **run EditorSim** (the synchronous sim: systems + Tick + command playback) | **38.7** | 0.9 | **37.8** |
| **phase PostSimulation** | **10.3** | 0.8 | **9.5** |
| **acquire view NavigationSolver** (the snapshot copy) | **4.3** | — | **4.3** |
| **acquire view Eqs** (the snapshot copy) | **4.0** | — | **4.0** |
| run GizmoInteraction (the gizmo systems) | 8.0 | 5.9 | 2.1 |
| phase Input | 5.4 | 1.9 | 3.6 |
| acquire view TrafficStepping | 0.8 | — | 0.8 |
| harvests (Eqs · Navigation · Traffic: their command playback) | 0.8 | — | 0.8 |
| phase Export · BeforeSync · AnimationMuscle | 1.0 · 0.5 · 0.7 | 1.2 · 0.6 · 0.2 | ~0.2 |
| **sum** | **74.6** (= the whole) | 11.6 | **63.0** |

⇒ **playing − paused = 63.0 ms of simulation on the drawing thread, every millisecond attributed.** Outside the kernel the editor's update adds ~4 ms (79.1 − 74.6),
the same playing or paused.

### Inside EditorSim and PostSimulation — the systems ≥ 1 ms (3-D mid, playing; ms per execution)

| system | ms | | system | ms |
|---|---|---|---|---|
| **BrainTickSystem** | **9.75** | | DroneImpactSystem (phase) | 2.02 |
| **WeaponDispatcherSystem** | **7.34** | | CgfThreatEvaluationSystem | 2.01 |
| **StressSystem** | **5.73** | | CarKinematicsSystem (phase) | 1.72 |
| **CalloutSystem** | **3.71** | | SquadCoordinationSystem | 1.72 |
| StatelessGizmoSystem (gizmos) | 5.97 | | FireProcessingSystem (phase) | 1.43 |
| StaffComposerSystem | 1.06 | | MapLayerAssignmentSystem (phase) | 1.13 |
| ~25 systems at 0.35–0.85 ms each | ≈ 12 | | CycloneEgressSystem (phase) | 0.99 |

Off the drawing thread (worker threads, NOT in the frame): `EqsSolverSystem` 58 ms per run at 10 Hz, `PathfindingSolverSystem` 6.4 ms, `LocalGridBuilderSystem` 2.3 ms.

## P-2 — the candidates the handoff named, each measured

| candidate | ms per frame | how often | where |
|---|---|---|---|
| ⭐ **the snapshot copy for a background module** — `OnDemandProvider.AcquireView` → `snapshot.SyncFrom(_liveWorld, _componentMask)` | **8.3** (Navigation 4.3 + Eqs 4.0; ~6.5 ms per copy) + Traffic 0.8 | each 10 Hz module, ~0.6–0.7 dispatches per frame | `FDP/Engine/Fdp.ModuleHost/Providers/OnDemandProvider.cs:67`, called at `ModuleHostKernel.cs` dispatch (`acquireProvider.AcquireView()`) |
| harvesting the background modules + playing back their command buffers | 0.8 | per completed run | `ModuleHostKernel.HarvestEntry` → `PlaybackCommands` |
| playing back the synchronous modules' commands | inside "run EditorSim" (not separable without a finer timer) | every frame | `ModuleHostKernel.PlaybackCommands` |
| the egress translators | ~1.0 (`CycloneEgressSystem`, Export phase 1.0) | every frame | the Export phase |
| the event bus swap + event history capture | < 0.1 + `EventHistoryCaptureSystem` 0.5 (686 KB / frame) | every frame | `ModuleHostKernel.UpdateInternalCore` |
| the synchronous modules not yet named | **EditorSim 38 ms** — weapons 7.3, stress 5.7, callouts 3.7, threat 2.0, squads 1.7, staff 1.1, ~25 small (≈ 12) besides the brains 9.8 | every frame | the CGF logic in EditorSim |

⭐ **The biggest three, by drawing-thread time:** ① the synchronous sim's systems other than brains (**~28 ms**; weapons, stress and callouts the top three at
`WeaponDispatcherSystem`, `StressSystem`, `CalloutSystem`); ② the PostSimulation phase (**9.5 ms**: drone impacts, car kinematics, fire processing, map layers);
③ **the two snapshot copies (8.3 ms)** — `OnDemandProvider.cs:67`. ⚠ The copies cannot simply be shared: an ASYNCHRONOUS module never joins a snapshot convoy
(`ModuleHostKernel.cs:2089`, `Mode != RunMode.Asynchronous`), because each keeps its copy leased for its whole run and the two start on different frames.

## P-3 — what the UI feels

| | playing | paused |
|---|---|---|
| ⭐ **input to draw** (a camera drag is read at the frame's start and shown at its end: 1–2 frames) | **~100–200 ms** (3-D mid); worst frame 176 ms ⇒ up to ~350 ms | **~30–65 ms**; worst frame 69 ms |
| worst frame in 30 s | 140–176 ms (3-D mid) · 132–171 ms (2-D whole) | 47–69 ms |
| opening a panel (Inspector, API round trip incl. the next frame) | 15–102 ms (varies run to run) | 5–32 ms |

⚠ The debug API's own round trip (median 2 ms, p95 21–28 ms, playing or paused) does NOT wait for a frame on this path, so it is not a latency measure; the
input-to-draw figure is derived from the frame times above. ⇒ the "unresponsive" the user saw is **10 fps: every input waits a 100 ms frame, often two.**

## P-4 — the soldier-cadence experiment (`HROT_SOLDIER_CADENCE`, default off, experimental)

⚠ **The dt answer first:** a cadenced brain's tick receives **ONE frame's dt**, not the time since its last tick — `BrainTickSystem.ExecuteCore` passes the frame's
`deltaTime` to `Run` (→ `BTreeRunner.cs:82` `_deltaTime = ctx.DeltaTime`, `HsmRunner.cs:217` `HsmKernel.Update(…, ctx.DeltaTime, …)`). A BTree `Wait` / `Cooldown`
measures from SIM TIME (`_time = repo.SimulationTime`, CE-2112) and stays right; **an HSM timer, or anything that integrates dt, runs slow by the period / frame
ratio** (×~2 at 0.2 s and 10 fps, ×12 at 60 fps). Measured anyway, not fixed.

The switch (`BrainTickSystem.ExperimentalSoldierCadence`): every brain no cadence POLICY owns (soldiers, vehicles — rules keep theirs) becomes `Periodic` at the
period, staggered by `CadenceGate`, and is woken by a `SensorChangedEvent` (the SOP slot's wake).

| 3-D mid, playing | off (base2) | 0.2 s | 0.1 s |
|---|---|---|---|
| frame avg | 98.8 ms (10.1 fps) | **91.8 ms (10.9 fps)** | 98.6 ms (10.1 fps) |
| update · kernel | 79.1 · 74.6 | 72.7 · 68.5 | 78.4 · 73.9 |
| run EditorSim | 38.7 | 34.6 | 37.7 |
| `BrainTickSystem` | 9.75 | **5.39** | 7.17 |
| worst frame | 175.7 | 148.1 | 161.4 |
| 2-D whole frame avg | (98.0 in run `base`) | 92.2 | 94.1 |

⇒ **a 0.2 s cadence saves ~4 ms of brain time (~7 ms of frame); 0.1 s ~2.5 ms.** The brain only halves at a 12× slower cadence: in a fight the senses wake most
soldiers most frames, and the walk over every brain (the tier queries, the store view) is paid whether a slot is due or not. The frame stays ~92 ms either way.

## P-5 — the lean (for the coordinator's architect question)

**Decision (lean): (c) — run the synchronous simulation off the drawing thread, the drawing thread showing a snapshot; and meanwhile (b), cut the named non-brain costs.**
Paused the drawing thread is ~30 ms (30+ fps); playing it carries **63 ms of simulation** of which brains are 10 ms. Only (c) takes the 63 ms out of input latency; (b)
and (a) shave it.

| the lean rests on | code — how it IS | design basis — how it was MEANT |
|---|---|---|
| the sim's cost on the drawing thread is 63 ms, of which brains are ~10 | ✅ measured above (`KernelFrameTimings`, `/diagnostics/architecture`) | ✅ Evaluation Cost §11g named brains as the big term at 1 000 men on a NODE — ⚠ here, in the editor, the non-brain CGF systems are ~3× brains |
| interleaving brains saves ~4 ms | ✅ P-4 | ✅ §3 D6 "a soldier / vehicle LOD later is a POLICY" — the mechanism exists (`CadenceGate`); ⚠ the dt is one frame's (P-4) |
| the brain writes the live world in place, so it cannot run on a snapshot | ✅ `CgfLogicPack.cs:59` `Synchronous`; ~100 `GetComponentRW` sites (handoff) | ⛔ searched `docs/` for a sim-thread / render-thread split for the editor: none found — this is the architect question |
| a snapshot copy costs ~6.5 ms per copy of a module's mask | ✅ `OnDemandProvider.cs:67`, measured 4.0–4.3 ms/frame at ~0.65 copies/frame | ✅ the kernel's providers are the designed way to give a module a frame-consistent view; ⚠ a render snapshot would be the whole drawn world's mask, likely more |
| paused, the drawing thread alone is ~30 ms | ✅ P-1 paused | — |

Rejected (one line each):
- **(a) alone** — 4 ms of 63; and the dt fault would have to be fixed first (HSM timers).
- **(b) alone** — the biggest single cut is the snapshot copies (8.3 ms) and they cannot be convoyed as built (`ModuleHostKernel.cs:2089`); the rest is ~25 systems of 0.4–7 ms each — a long tail, weeks of work for 30–40 %.
- **"the snapshot copy is the 75 ms"** — refuted: 8.3 ms.

Design docs checked: `DESIGN_Evaluation_Cost_And_Cadence.md` §3 / §11g — applies (the cadence mechanism, the brain cost model); §11h added with these numbers ·
`DESIGN_Time_Architecture.md` §17 — does not apply (stepped mode waits for background jobs; Play is continuous, measured here) · `STATUS_Ui_Windows_Render_Under_Load.md`
— its "update 115 ms, ~75 ms unattributed" is now attributed (it measured a heavier moment and `StatelessGizmoSystem` before CE-4389's cull).

## Gates

| gate | result |
|---|---|
| `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build` (built first) | ✅ **258 / 258** (2 new: `KernelFrameTimingsTests`) |
| red-proof: an allocation per `Stop` put back | ✅ `ASteadyFrameOfParts_Allocates0B…` red (`Assert.Equal() Failure`), source restored |
| the runner (Release) on the merged tree | ✅ 0 errors |
| ⚠ `Fdp.Toolkits.Tests` (BrainTickSystem) | not run — the change is a branch behind a switch that defaults off (0); the off path is the one every preview run above took |
| `HROT_SOLDIER_CADENCE` | default off (0); nothing sets it; named experimental in its doc comment |
| no recordings / screenshots / terrain data in git | ✅ the logs stay in the session scratchpad |
