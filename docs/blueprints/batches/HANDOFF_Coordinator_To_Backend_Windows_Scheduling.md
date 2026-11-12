<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §19 Windows scheduling (power throttling, timer resolution, priorities, hybrid cores, console QuickEdit)
  - ../../DESIGN_Threaded_Work_Inventory.md — §5a the task as the user decided it; §7.6 the measurement that makes it urgent
  - STATUS_Ui_Windows_Throttling.md (on exp/perf) — CE-4453: windowless nodes run at about HALF speed
-->

# HANDOFF — coordinator → backend: every HROT process opts out of Windows power throttling (and the other scheduling settings)

**For:** the next free backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen.
🔒 User, `2026-11-12`: *"i do not want headless mode running in console to be any slower"* · *"add both"* · *"Yes"* (do it now, on its own branch).
⭐ Measured (CE-4453, Windows): Windows puts a node with no visible window into EcoQoS by default — clock ~2.6 GHz vs ~3.8–4.1 GHz; hidden CGF
**228 → 121 ms**, SimHost **101 → 44 ms** with the `powercfg` opt-out; minimised = hidden; a visible console is not throttled. ⇒ a ~1.9× win for
every headless node, independent of threading. ⛔⛔ **Autonomous** (R-4156).

## Where to work

A NEW branch **`exp/windows-scheduling`** from **`coordinator`** — production code, small, merge-ready on its own (it does NOT depend on
`exp/integration`). Status file `docs/blueprints/batches/STATUS_Windows_Scheduling.md` on it. ⛔ No lane merge without the user's word.

## The items — ONE shared start-up helper every host calls (shared and unified; ⛔ no per-host copies)

| # | item | done when |
|---|---|---|
| **W-1** | find the ONE place every HROT process passes at start-up (the shared bootstrapper / the runner's `Program`) — graph + grep; name it | the helper's home justified in the status file |
| **W-2** | ⭐ opt the PROCESS out of power throttling: `SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, {ControlMask = EXECUTION_SPEED \| IGNORE_TIMER_RESOLUTION, StateMask = 0})`; the same per thread (`SetThreadInformation(ThreadPowerThrottling)`) for the simulation thread and the worker pool's threads (`FdpWorkerPool`) | a rail with the Windows call faked behind an interface (the call made with exactly these masks); a no-op on Linux; an env switch to disable it (`HROT_ECOQOS=1` keeps Windows' default) for comparison |
| **W-3** | `timeBeginPeriod(1)` while a simulation runs, `timeEndPeriod(1)` at shutdown | faked-call rail |
| **W-4** | thread priorities: the sim thread AboveNormal; the pool's FRAME lane Normal; its background lane: Normal for the off-frame egress scan (it is cluster latency — critical review F-15), BelowNormal for EQS windows and the recorder | rail on the configured priorities |
| **W-5** | console hosts clear `ENABLE_QUICK_EDIT_MODE` (`GetConsoleMode` / `SetConsoleMode`) — a click in a console window otherwise freezes every write to it, and a node logging a line per step with it | faked-call rail |
| **W-6** | hybrid CPUs: the pool's frame lane sized to the PERFORMANCE cores (`GetSystemCpuSetInformation`, `EfficiencyClass`); the background lane may use efficiency cores. ⚠ The user's machine is AMD (no E-cores) — unit-test the sizing logic on faked CPU-set data | rail on the sizing with a faked hybrid layout |
| **W-7** | the Windows measurement re-run WITH THE CODE instead of `powercfg`: leave a one-command script (`scripts/windows-scheduling-measure.py`) running CE-4453's matrix (hidden / minimised / visible console, `HROT_ECOQOS=1` vs default) — the Windows session runs it | the script; ⭐ the Windows run itself is a separate step the user hands to the Windows lane |

## Rules
- 0 B per steady frame (R-2084); every call at start-up or thread creation only.
- The changed projects' OWN rails; no broad suites. Run builds in the background; never poll. ⛔ No screenshots in git.

**Done when:** W-1…W-6 built with their rails, W-7's script left, the status file says what each setting does on Linux (nothing).

---
_coordinator lane, 2026-11-12_
