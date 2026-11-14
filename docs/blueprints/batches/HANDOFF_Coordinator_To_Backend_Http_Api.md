<!--STATUS
state: LIVE — dispatched, not started; START AFTER the determinism residue batch (HANDOFF_Coordinator_To_Backend_Determinism_Residue.md) is reported
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../DESIGN_Debug_Api_Http_Only.md — ⭐ THE design of part A (R-4223)
  - HANDOFF_Coordinator_To_Backend_Debug_Api_Http_Only.md — part A's item list H-1…H-7 (CE-4523…CE-4529), run AS WRITTEN except where this file says otherwise
  - ../../RUNBOOK_Cluster_Debugging_Over_Http.md — how a process is STARTED (and with which switches) — B-5 adds the performance switches there
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a.1 the performance switches and their precedence (R-4224)
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — L-5 (the Performance status line over HTTP), answered by B-5
-->

# HANDOFF — coordinator → `backend`: the HTTP API — HTTP-only and self-describing, and its open failures

**For:** the `backend` lane, **after** the determinism residue batch. **From:** coordinator. **Dispatched at `eb43032c3`** *(re-stamped under rule 1a, unstarted: B-6 added)*; ⛔ **scope FROZEN
there** — later documents are FYI; one that invalidates an item ⇒ STOP that item and report it (R-106). ⭐ Autonomous, no questions.
🔒 User, `2026-11-14`: *"yes write it"* · *"agreed yes add the central registry and ammend the handoff"* (B-6) · *"how the hhtp api allows running the simulation with those settings changed? (it needs to for various
tests)"*. ⭐ The ui-filed rows CE-1464 / CE-1472 / CE-1474 / CE-1950 are taken by backend here — **cross-lane, said**.

## 0. Start

On `backend`, `git merge origin/coordinator`; restore the test projects you will run (the third stale-binary trap); push
`chore: started http api at <sha>`. Status file `STATUS_Http_Api.md`.

## A. HTTP-only, self-describing (R-4223) — CE-4523…CE-4529, NO LONGER DEFERRED

Run [the HTTP-only handoff](HANDOFF_Coordinator_To_Backend_Debug_Api_Http_Only.md)'s H-1…H-7 as written, with ONE change: ⛔ **not on a new
`exp/debug-http` branch** — on `backend` (the parallel programme is merged; experiment branches are retired, CLAUDE.md). Its deferral condition
is met (`e4af862f6`, and `exp/next-conversions` is an ancestor of `coordinator`).

## B. The open failures — after A's H-1 (they touch the same route table)

| # | row | the failure (measured by ui) | decision |
|---|---|---|---|
| **B-1** | **CE-1474** | `--mode server` answers `POST /sim/play` with 501 and `/status` has no `simTime` | ⭐ the fused server takes the SAME transport the editor / cluster use (one implementation, not a server-only path); `/status.simTime` from the one clock; rail on `--mode server` |
| **B-2** | **CE-1950** | a replay that FAILS to load leaves the cluster in `OperatingReplay`, and `POST /replay {waitForReady:true}` answers `ok` | ⭐ the row's lean: a failed 2PC round leaves the cluster where it was and the route answers the failure with the node's reason (the master already logs *"completed with failures"*); rail: a recording with an old schema ⇒ an error answer, state unchanged |
| **B-3** | **CE-1472** | the Player's `GET /diagnostics/render` is stale | ⭐ the Player's map feeds the SAME `RenderTelemetry` the editor's overlay feeds (shared, not a copy) |
| **B-4** | **CE-1464** | an HTTP-possessed unit never fires on a WINDOWED editor: the editor's own station sends a frame per rendered frame with a higher `seq`, and "newest seq wins" drops the HTTP frames | ⭐ **measure first**, then the row's lean: a station sends nothing for a unit it did not possess through its own UI. If the fix lands in ui's station code, build it and say so; ⛔ do not widen to the sight path (`lookFrom` / `lookDir`, CE-4143) |
| **B-5** | **CE-4585** *(new, replaces L-5)* | **the performance switches over HTTP** — see §C | ⭐ §C's three parts |
| **B-6** | **CE-4586** *(new, amended in `2026-11-14`, rule 1a)* | **an agent cannot learn which environment variables exist** — 141 `HROT_*` + 12 `FDP_*`, read raw at 96 production sites, listed nowhere | ⭐ build [`DESIGN_Debug_Api_Http_Only.md` §6](../../DESIGN_Debug_Api_Http_Only.md): ONE `EnvVars` table in `Fdp.Core` (name, category Switch / Diagnostic / Test / Tool, type, default, purpose, design section); every read goes through it (mechanical, no behaviour change); the rail `EveryEnvReadIsRegisteredTests` (raw read or undeclared name ⇒ red; allow-list = projects that cannot reference `Fdp.Core`); `/help` + `--help-api` render it by category; `/performance` shows what this process received. ⚠ Do B-6 AFTER A's H-3 (`/help` exists) and with B-5. Experiment leftovers: declared, and LISTED in the report for the user — ⛔ not deleted |

## C. The performance switches and HTTP — measured `2026-11-14`, the decision for B-5

| claim | code (how it is) | design |
|---|---|---|
| the switches apply ONCE, at process start | `PerformanceStartup.Apply` (`Configuration/PerformanceStartup.cs:33`), called once from `Program.cs:109` | ✅ R-4224, Threaded Presentation §0a.1 |
| precedence: an explicit `HROT_PARALLEL_SIM` / `HROT_THREADED_KERNEL` / `HROT_SIM_THREADS` > `-m all` serial > the user file > ON | `PerformanceStartup.cs:6-12, 42-55` | ✅ same |
| a live toggle would give a MIXED state | `BrainTickSystem.Parallel` and `SquadCoordinationSystem.Parallel` read the switch ONCE into a static at type load (`BrainTickSystem.cs:290`, `SquadCoordinationSystem.cs:107`); other systems read it per tick; the threaded node's two worlds are composed at start | ⛔ searched, no design for a live switch |
| tests already choose the switches — at START | `EditorProcess.cs:172` sets them per process (*"explicit, so a rail does not depend on the machine's settings file"*); `stepped-hash.py:40` sets `HROT_THREADED_KERNEL`; in-process rails set the statics | ✅ |
| HTTP can SEE nothing of it | the running mode is printed once to stdout; no route returns it; the menu's status line is a shell command (`settings.performance.status`) | — (L-5) |
| ⚠ HTTP can CHANGE the user's saved file | the menu toggles are shell commands (`PerformanceMenu.cs:46-62`); `POST /editor/commands/settings.performance.parallel/invoke?scope=shell` saves to the REAL user folder (`UserFiles` → `HROT`), so every later process on that machine starts with it | ⛔ a test doing this silently reconfigures the user's machine |

**Decision (B-5):**

1. ⭐ **`GET /performance`** on every process: what it RUNS with (threaded node, parallel phases, worker threads) and, per switch, WHERE it came
   from (`env` / `-m all` / `file` / `default`), plus the saved choice for the next start — the one source is `PerformanceStartup.Running` +
   `SavedForNextStart()`. Also in `/status`. Documented in the route docs, so `/help` carries it (part A).
2. ⭐ **a test runs with the switches it needs by STARTING the process with them** — the env vars, as the harness already does. `/help` and the
   runbook say so in one line each (*"start with `HROT_PARALLEL_SIM=0` …; `GET /performance` confirms"*). ⛔ No live toggle (the mixed state above).
3. ⛔ **`settings.performance.*` invoked over HTTP is refused** with that hint — the menu stays for a person; an HTTP client may not rewrite the
   user's own settings file. (If a scripted change of the SAVED choice is ever wanted, it is a separate, explicit route — not built now.)

Rails: `/performance` reports `env` for a process started with `HROT_PARALLEL_SIM=0` and `default` without it; the HTTP invoke is refused and
the file is untouched (a temp `PathOverride`).

## D. Gates and report

T-1 first: the route docs' rail (`EveryRouteIsDocumentedTests`), `DebugApiOrdersTests`, the replay route suite, `PerformanceStartupTests`, the
system-test harness (`DESIGN_MCP_System_Test_Harness.md`, unchanged and green — A's acceptance 5). New rails red-proved; R-4221 (invariant
output) on `/help` and `/performance`. As-built into `DESIGN_Debug_Api_Http_Only.md` (A, B-5, B-6 §6) and the runbook (B-5 ②). Gate-report contract
(CLAUDE.md rule 8); ids used.
