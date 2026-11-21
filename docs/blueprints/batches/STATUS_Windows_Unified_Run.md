<!--STATUS
state: LIVE — measured 2026-11-13
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Windows_Unified_Threaded_Run.md (on coordinator) — the order this answers (CE-4537 … CE-4541)
  - STATUS_Windows_Threaded_Rerun.md — the previous run: every BEFORE number here
  - STATUS_Unify_Threaded_Defaults_On.md (exp/threaded-build) — the batch tested (U-1 … U-7)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a.1 the switches, §0a.2 the manual test sheet walked here (not edited: measure only)
-->

# STATUS — Windows: the UNIFIED threaded build, defaults ON (CE-4537 … CE-4541)

**Built:** `exp/threaded-build` @ **`a9955908e`** (`READY FOR WINDOWS at 251e9cc36`), Release, 0 errors; merged into `exp/windows-threaded-measure`.
Same machine as the earlier runs (Ryzen 7 6800H 8C/16T, Windows 11 26200, cs-CZ, AC, Balanced, hidden windows). ⭐ **No `HROT_THREADED_KERNEL` /
`HROT_PARALLEL_SIM` / `HROT_SIM_THREADS` in any run** unless a row says so — the defaults and `%LOCALAPPDATA%\HROT\performance.json` decided.
That file did not exist before this run; it is left at the defaults (`ThreadedEditor true, ParallelSimulation true, WorkerThreads 0`).

## Verdicts

| # | | verdict |
|---|---|---|
| W-1 · `CE-4537` | defaults ON, the Settings switches | ✅ all steps — one gap: the menu's status line is not readable over HTTP |
| W-2 · `CE-4538` | the numbers, via the setting | ✅ editor tick **75 → 69.7 ms** at 8 threads (1.9× the switch off); CGF node **57.5 → 55.4 ms** (8), **64.7 → 53.4** (12) |
| W-3 · `CE-4539` | player + server, separate nodes, `-m all` | ✅ 5 min soak 0 faults, guard 0; the multiplayer cycle 10/10; nodes obey the setting; `-m all` serial |
| W-4 · `CE-4540` | stepped determinism + CE-3445's long series | ⛔ **mode 3 is NOT repeatable on real cores** — two separate effects, see W-4. Switch off: EQUAL |
| W-5 · `CE-4541` | the manual test sheet §0a.2 | ⚠ 5 of 7 rows pass; ⛔ the 3-D follow route is blind in the threaded editor (`GET /view`, `POST /view/follow`); Undo of an HTTP delete is refused in BOTH kernels |

## W-1 · `CE-4537` — defaults ON with no environment variables

Driven over HTTP through the menu's own commands (`POST /editor/commands/settings.performance.<x>/invoke?scope=shell` — the seam the menu uses).

| step | result |
|---|---|
| no `performance.json` before | ✅ none |
| start with nothing set | ✅ `[Performance] threaded node ON · parallel simulation ON · worker threads auto (settings)` + `[STAGED] the node over S, the presentation over A; table mask ALL types; single-threaded: no` |
| untick **Parallel simulation** | ✅ the file `C:\Users\pjane\AppData\Local\HROT\performance.json` written: `{"ThreadedEditor": true, "ParallelSimulation": false, "WorkerThreads": 0}` |
| the status says *restart to apply* | ⚠ **not checkable over HTTP**: `GET /editor/commands/settings.performance.status` answers *"No editor-command dispatcher is reachable"* (the GET has no `?scope=shell`, the invoke does) — the rail `PerformanceStartupTests.TheMenu_WritesTheFile_ThatTheNextStartReads` covers it |
| restart | ✅ `… parallel simulation off · worker threads auto (settings)` |
| untick **Threaded editor**, **Worker threads ▸ 4**, restart | ✅ `threaded node off · parallel simulation off · worker threads 4 (settings)`; no `[STAGED]` line |
| every switch back ON / Auto, restart | ✅ `threaded node ON · parallel simulation ON · worker threads auto (settings)` |

## W-2 · `CE-4538` — the numbers on real cores, via the setting (no env vars)

**The editor** (`threaded-scale-measure.py --threads 1,2,4,6,8,12`; the thread count written to `performance.json` before each start, the start
line confirms it; `HROT_TWO_WORLDS_MASK=ui-no-inspector` as in every earlier run). BEFORE = the rerun (env vars, `d8e47f3c0`).

| threads | sim tick ms | UI playing ms | UI paused ms | BrainTick | the group | EQS (background) | cores |
|---|---|---|---|---|---|---|---|
| 1 | 154.9 → **129.7** | 7.8 → 8.5 | 12.8 → 13.1 | 66.8 → 38.7 | 145.4 → 106.4 | 101.4 → 87.9 | 3.26 → 3.31 |
| 2 | 98.9 → **99.8** | 8.6 → 8.5 | 12.7 → 13.0 | 25.2 → 26.7 | 73.4 → 72.5 | 55.5 → 55.5 | 3.78 → 3.81 |
| 4 | 78.3 → **75.4** | 8.9 → 9.1 | 12.9 → 12.9 | 12.3 → 11.3 | 45.0 → 39.0 | 29.4 → 29.6 | 4.32 → 4.38 |
| 6 | 74.2 → **69.1** | 9.1 → 9.3 | 12.9 → 12.9 | 8.8 → 9.0 | 39.4 → 32.9 | 24.2 → 23.3 | 4.66 → 4.85 |
| 8 | 76.0 → **69.7** | 9.2 → 9.7 | 12.8 → 13.0 | 8.7 → 9.3 | 39.5 → 33.6 | 21.0 → 22.1 | 4.98 → 5.39 |
| 12 | 75.4 → **67.5** | 9.8 → 10.3 | 12.9 → 13.6 | 8.5 → 7.1 | 38.0 → 29.4 | 18.2 → 18.1 | 5.56 → 6.01 |
| setting OFF | 144.4 (first run) → **131.7** | 146.3 → 149.4 | 36.5 → 41.9 | 28.0 → 27.8 | 81.4 → 79.0 | 16.0 → 15.2 | 2.36 → 2.52 |

- ⭐ 8 threads vs the setting OFF: tick **1.89×**, UI frame **15×**. Still flat from 6 threads. Working set 2.26–2.42 GB, staging 93 MB.

**What is still serial at 8 threads** (tick 69.7 ms):

| system | ms @1 | ms @8 | × | % tick |
|---|---|---|---|---|
| `TogglableSimulationGroup` | 106.9 | 33.8 | 3.2 | 49 % |
| `BrainTickSystem` | 39.2 | 9.4 | 4.2 | 13.5 % |
| `StressSystem` | 16.5 | 4.0 | 4.1 | 5.8 % |
| ⭐ `WeaponDispatcherSystem` | 6.5 | 3.8 | 1.7 | 5.5 % |
| ⭐ `TogglableInputGroup` | 4.2 | 3.2 | 1.3 | 4.6 % |
| `SquadCoordinationSystem` | 14.2 | 3.0 | 4.7 | 4.3 % (was 5.0, serial-ish — now scales) |
| `TogglablePostSimulationGroup` | 5.8 | 2.6 | 2.3 | 3.7 % |
| `CalloutSystem` | 4.9 | 2.5 | 2.0 | 3.5 % |
| `CgfThreatEvaluationSystem` | 6.5 | 1.6 | 4.0 | 2.3 % |

⇒ the largest serial pieces left: `WeaponDispatcherSystem` (1.7×), the Input group (1.3×), then a tail under 1 ms each.

**Separate node processes** (`scale-rig.py --terrain demo-city --men 1000`, the setting per run; every node printed
`[Performance] threaded node off · parallel simulation ON · worker threads <n> (settings)` — or `off (settings)` for the OFF run):

| run | CGF work ms (fps) | BrainTick | WeaponDisp. | StaffComposer | Egress (all passes) | SimHost work ms | cores CGF / SimHost |
|---|---|---|---|---|---|---|---|
| setting OFF | 102.8 → **101.9** (22.3) | 36.0 | 10.0 | 2.5 | 15.8 | 21.6 | 1.21 / 2.91 |
| 1 | 104.5 → **106.2** (22.0) | 39.5 | 11.3 | 3.0 | 14.8 | 22.4 | 1.22 / 1.68 |
| 4 | 61.8 → **58.0** (37.4) | 11.9 | 4.6 | 1.0 | 14.5 | 24.3 | 2.08 / 1.95 |
| 8 | 57.5 → **55.4** (40.1) | 9.1 | 4.1 | 0.8 | 16.7 | 27.0 | 2.78 / 2.36 |
| 12 | 64.7 → **53.4** (39.2) | 9.1 | 4.1 | 0.9 | 14.9 | 27.8 | 3.08 / 2.68 |

- ⭐ CGF at 8: **1.84× the OFF run**. `StaffComposerSystem` converted (2.6 → 0.8 ms); `WeaponDispatcherSystem` 6.3 → 4.1. 12 threads no longer
  slower than 8.
- ⭐ **Egress (~15 ms) is now the CGF's largest serial cost by far** — its top topic is still `EqsSensorConfigTopic`: **7.2 ms, 30 477 samples**
  per window (positions: 0.8 ms, 17 106). SimHost 22–28 ms whatever the setting.

## W-3 · `CE-4539` — player + server, separate nodes, `-m all`

| step | result |
|---|---|
| ⭐ 5 min soak: `-m server` + `-m player` (hidden, DDS domain 74, `HROT_WORLD_GUARD=1`), s2-respawn loaded live, the player deploys into a placed unit and drives it the whole time (yaw sweeping), the trigger held 2 s in every 10 (428 fire frames), the scenario RELOADED live on the server at 2.5 min | ✅ every step; after the reload the player went back to Choosing and redeployed; the unit moved throughout |
| the server's tick | ✅ ~59.7 ticks/s, sim time = wall time (30.0 s of sim per 30 s, 10 samples); FrameStats median 16.7 ms |
| the player's frame | ✅ FrameStats median **16.7 ms**, max 17.6 (vsync-paced) |
| start lines | ✅ player `threaded node ON · parallel simulation ON · worker threads auto (settings)` + `[STAGED] …`; server `threaded node off · parallel simulation ON …` |
| faults · guard | ✅ 0 in both logs · no guard report written (0 hits) |
| `scripts/multiplayer-cycle.py` (Release, domain 76, guard on) | ✅ **10 / 10** (join, deploy, drive, late joiner refused P1's unit, die → respawn 5.0 s, Esc, quit, rejoin, server alive; step 10 SKIP: `ddsmonitor` not installed here) — 3 players `threaded node ON`, the server `parallel simulation ON` |
| separate orchestrator / CGF / SimHost, no env vars | ✅ every node prints `parallel simulation ON · worker threads <n> (settings)` — the W-2 rig runs |
| `-m all` | ✅ `[Performance] threaded node off · parallel simulation off · worker threads auto (-m all (serial))`; no `[STAGED]`; the windows draw (FrameStats per window: start surface, map view, 2-D terrain, film …; 16.7 ms frames); 0 faults |

(W-3 and W-4's series ran at the same time, as the handoff allowed — the soak's frame numbers are under that load.)

## W-4 · `CE-4540` — stepped determinism

`scripts/stepped-hash.py` (now refuses to compare runs of different step counts — exit 2). Runs compared by me inside each step-count group from the
saved results (`--save`), `EntityGuid` excluded as the gate does.

| run | threads | step counts (sim time) | verdict inside equal step counts | exit |
|---|---|---|---|---|
| `--kernel 0` (switch off; parallel phases from the setting = ON) | 1 · 4 · 12 | 120 · 120 · 120 | ✅ **STATE EQUAL** | 0 |
| `--kernel 3` | 1 · 4 · 12 | 120 · 120 · **121** | 1 vs 4: ⛔ 4 types (`BehaviorState`, `BlueprintBlackboard1024`, `BlueprintBlackboard4096`, `BrainJournal`) | 2 |
| `--kernel 3` retry, `--rows BehaviorState --bytes` | 1 · 4 · 12 · 4 · 4 · 4 | 120 · **121 · 121** · 120 · 120 · 120 | 1 vs each 4 (×3): ⛔ the same 4 types, the **same 10 rows**; 4 vs 12 (both 121): ✅ equal | 2 |
| ⭐ CE-3445's long series, `--kernel 3 --chunk 40` (240 steps, unprobed) | 1 + 4 ×11 | 4 of 12 runs **241** steps, 8 runs 240 | 240-step group (7 runs): 2 of 6 pairs ⛔ 3 types (`BehaviorState`, `BlueprintBlackboard4096`, `BrainJournal`); 241-step group (1-thread + 3 runs): 2 of 3 pairs ⛔ the same 3 types | 2 |

**Two separate effects in mode 3, both measured:**

1. ⛔ **An extra step** — 1 run in 3 to 1 in 6 takes 121 (241) steps where its peers take 120 (240), at any thread count (the 1-thread series run did
   too). The switch-off kernel took 120 every time today (the rerun saw it take 121 every time — the entry into the paused preview is where the
   count is decided; the gate now catches it).
2. ⛔ **A brain divergence inside equal step counts — no longer rare on real cores.** The rows (`--rows BehaviorState --bytes`, 1 thread vs 4):
   the same **10 men** (network ids 1289, 1293, 1324, 1328, 1340, 1342, 1346, 1615, 1618, 1873) in every pair; bytes 0–5, 7, 9–10, 19–21 =
   `ActiveBehaviorHash`, `InstanceId`, `Origin`/`Urgency` and `RunSince`. 1 thread: each **starts a new reaction (urgency 4) at the last step**
   (`RunSince` = 2.0 s, a fresh `InstanceId` 0x97C…0x985 in a row); 4 threads (all three runs identical): each **still runs the previous
   behaviour**, begun at 1.133 s. Positions (`SimTransform`) equal in every pair. ⇒ the same ten men react on a different step; the 4-thread runs
   agree with each other, so it is an ORDER/timing effect, not noise. In the 240-step series the 4-thread runs themselves split 5 : 2 on the same
   three types — so "which step the reaction lands on" varies run to run too.
   - ⚠ Lean, unmeasured: the one-tick delay CE-4509 tolerated for parallel brains (*"one-tick delay tolerated in the test"*) — a reaction raised in
     a parallel phase landing one step later. The previous build (`d8e47f3c0`, the rerun) had 1 / 4 / 12 EQUAL at 120 steps; this build adds
     E-1…E-3 (R-195's SOP wake, the own-entity ADD fixes) and the unify batch.
   - Not done: a saved world per run (the gate has no world-save option; `/scenario/save` saves the authored world, not the stepped state) — the
     per-type hashes and the `BehaviorState` rows of every run are kept instead (`%TEMP%\…\wtm\u\w4\*.json`, scratch).

## W-5 · `CE-4541` — the manual test sheet (§0a.2), walked over HTTP, defaults ON

| # | row | result |
|---|---|---|
| 1 | the start lines | ✅ `[Performance] threaded node ON · parallel simulation ON · worker threads auto (settings)` · `[STAGED] the node over S, the presentation over A …` |
| 2 | load `scale-rig-demo-city-1000-2s`, select a unit, Play, Pause, Step ×5 | ✅ loaded; the selection lands (`GET /view` lists it); the sim runs while playing (sim 19.8 → 25.1 s in 5 s); paused it holds; **each step exactly one frame** (1/60 s ×5) |
| 2 | … the 3-D view following it | ⛔ **`POST /view/follow` refused in the threaded editor**: *"No map canvas draws this world (a headless host)"*; `GET /view` says the same (`camera: null`). With `HROT_THREADED_KERNEL=0` the same call works (`following: 1010`, a camera). Cause, read: `DebugApiService.View.cs:18` / `:74` look the camera up by `_world` (`ViewTelemetry.Of(_world)`, `ViewControl.Of(_world)`) — in mode 3 that is the sim's world S, while the map canvas draws A (a side table keyed by the world, §13.2). `POST /film/shot {follow}` works in mode 3 (it goes another way) ⇒ the UI's own follow is probably fine; the agent-facing route is blind. Not seen through the UI (hidden window) |
| 3 | while playing: move a unit, give an order, delete one | ✅ the move order and a hold order land; the delete is gone on the next read |
| 3 | … Undo | ⚠ **refused in BOTH kernels**: *"'Undo' (edit.undo) is DISABLED right now"* — after a `DELETE /entities/{id}` while playing AND in edit (paused). The HTTP delete does not enter the undo history; ⇒ not a threading difference. Undo after a UI delete is untested (needs the window) |
| 4 | a recording: live run, review it, film a shot | ✅ `/scenario/load/live` → 40 s live → `POST /review {atSimSeconds:10}` reaches `OperatingReview` → seek to 20 s → `POST /film/shot {follow}` + `POST /film/capture` (640×360 PNG, 110 KB, scratch only) → `POST /review/exit` back to `OperatingLive`; 0 faults |
| 5 | server + players | ✅ W-3 (the cycle 10/10, the soak) |
| 6 | the Performance menu | ✅ W-1 |
| 7 | `-m all` | ✅ W-3 |
| — | faults · guard over the sheet's session | ✅ 0 faults; `HROT_WORLD_GUARD=1`: **0 distinct hits** |

## Surprises — one line each

- ⛔ Mode 3 stepped runs diverge in BRAIN state far more often than CE-3445's "1 in 36": 1 vs 4 threads differ in 4 of 4 comparisons at 120 steps, the
  same 10 men starting a reaction one step apart; the rerun's build had them EQUAL. Positions stay equal.
- ⛔ The extra step (121 vs 120) persists in mode 3: 4 of 12 series runs, 2 of 6 retry runs.
- ⛔ `GET /view` / `POST /view/follow` read the sim's world in the threaded editor and report "a headless host" (`DebugApiService.View.cs:18`, `:74`).
- `EqsSensorConfigTopic` is still the most expensive topic on the CGF's wire (7.2 ms, 30 k samples per 15 s) — more than all positions.
- The setting changes the node processes too (by design): a user who sets 1 worker thread in the editor's menu also gets 1 on every node started from
  that account.
- The log's `·` separator is written in the OEM code page (byte 0xFA) — a UTF-8 reader shows `ú` / `�` (cosmetic; R-4221 covers the culture, not
  the encoding).

---
_Windows session, 2026-11-13_
