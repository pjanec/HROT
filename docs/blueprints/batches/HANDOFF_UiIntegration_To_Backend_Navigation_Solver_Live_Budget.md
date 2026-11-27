# HANDOFF — `unreal-ig` → `backend`: live, a slow NavigationSolver run loses its paths — vehicles stand and fail (CE-11076)

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-27 |
| **For** | `backend` (the module host, the navigation solver, the frustration rule — F-3 / CE-4503, CE-3066 and CE-11057 ③ are yours) |
| **Dispatched at** | `unreal-ig` @ `e269b0918` + the commit adding this file |
| **User, verbatim** | *"yes, triage CE-11076"* · *"Can you write a handoff to the backend?"* |
| **Tracked** | [CE-11076](../Blueprint_Issues_Tracker.md) (the umbrella; allocate your own ids for the pieces, from your block) |
| **Evidence** | [NOTE_UiIntegration_To_Backend_Stechovice_Live_No_Paths.md](NOTE_UiIntegration_To_Backend_Stechovice_Live_No_Paths.md) (the live repro) · Unreal's [NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md](NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md) |
| **Scope** | ⭐ frozen at the dispatch sha; documents that change after it are FYI only — a later document that invalidates an item ⇒ STOP that item and report it |

## 1. The problem, measured (live, `showcases/stechovice-battle`, `--mode editor`, realtime factor ≈ 1, ~5 800 entities)

| | |
|---|---|
| symptom | every HROT-driven vehicle move ends `FailedBlocked` ~2 s after it stops: Riverbank Blue Tank 2 (1142) ordered 40 m on open bank drove 15 m, then speed 0, steering 0, heading frozen; a second order — it never moved. `NavState.IsBlocked` 0; nothing solid round it |
| ⭐ cause | `[ModuleHost][TIMEOUT] Module 'NavigationSolver' timed out after 100ms. Task abandoned … its output was discarded` **×181** and `[CIRCUIT-OPEN] … SKIPPED for 10000 ms after 5 consecutive failures` **×33** in ~4 min (`Eqs` ×22) ⇒ no path answers; a move with no path stands; the 2 s rule fails it |
| why no rail or regression saw it | a STEPPED frame waits for the run (`ModuleHostKernel.cs:1013, 1068-1078`, F-3 / CE-4503, R-3035) — the deterministic regression never meets the 100 ms wall-clock budget |

⛔ Not measured: the cost of one solver run here (requests per run, navmesh size, the battle's load) and whether a discarded run's REQUESTS are lost for good.

## 2. Design basis — what was meant

| doc | says | applies |
|---|---|---|
| `docs/projects/FDP/Core/Fdp.ModuleHost.md` §5 (CE-3066) | a timed-out run is kept as a zombie, never overlapped; once it ends its command buffers are **discarded** ("its answers are for a snapshot several frames old") | ⭐ this is the discard that loses the paths — right for a module whose answers age, ⚠ not for a REQUEST/REPLY service whose callers wait on the reply |
| `DESIGN_Threaded_Simulation_Presentation.md` R-3035 (F-3) | a stepped run never abandons a module on the wall clock | why the regression passes; ⛔ do not change |
| `docs/designs/navig-2/Navigation_Design_v2_0.md` N2 / N5 | the 0.2 m/s / 2 s speed rule (N5, CE-11057 ③) and the no-progress rule (N2) define frustration | ⚠ a move that has NO PATH YET is not described — the open question of item ③ |

## 3. The frame — goal, fences, items with leans

**Goal:** live, on a big terrain under load, a vehicle's move gets its path (late if need be) and drives; a slow solver run never silently drops a request.

**Fences:** ⛔ stepped runs unchanged (R-3035: never abandon on the wall clock). ⛔ The frame never waits on a slow module live (CE-3066). ⛔ No change to the
module host's discard for modules whose answers age (perception, EQS) without a design argument. ⭐ Parallel and hot-path rules (CLAUDE.md): no new
per-frame allocation in the solver or the executor.

| # | item | ⭐ lean | rejected (one line) |
|---|---|---|---|
| ① | a timed-out solver run must not LOSE its path requests | ⭐ the unanswered requests of an abandoned run are requeued for the next run (the request/reply contract), not discarded with its buffers | apply the late result — answers for a world several frames old; raising the 100 ms budget — moves the cliff, does not remove it |
| ② | the solver never reaches the host's timeout | ⭐ it bounds its own work per run (answer requests up to a budget, carry the rest) | a bigger thread pool — the same cliff at a bigger map |
| ③ | a move waiting for its FIRST path is not frustrated | ⭐ the frustration rule starts counting only once the move has a path (a planned-but-unanswered move standing still is not blocked); a long wait is reported (a path timeout), not `FailedBlocked` | keeping today's rule — a slow solver turns into "blocked" with no cause in the status |
| ④ | measure first | ⭐ the solver run's cost on `stechovice-battle` live (requests per run, ms per request) — states which of ① / ② carries the fix | — |

⚠ The design question in ③ is a Navigation v2 change — fold the answer into N2 / N5. Item ① touches the module host's discard rule — fold it into
`Fdp.ModuleHost.md` §5 (a request/reply module is not an ageing answer).

## 4. Acceptance (R-4277)

| | |
|---|---|
| ⭐ `stechovice-battle` LIVE | the scenario's own row *"a Blue tank ordered over the bridge reaches the west bank"* (`checks.json`), judged on a live run; an M1 ordered 40 m on the open east bank arrives |
| the log | no `NavigationSolver` circuit-open; a timeout, if any, followed by the requeued requests being answered |
| rails | an abandoned solver run's requests are answered by the next run (a fake slow run); a move waiting for its first path is not `FailedBlocked` at 2 s; stepped runs unchanged (the existing F-3 rails); 0 B steady in the executor |
| gates | the gate-report contract (CLAUDE.md rule 8): navigation + CarKinem + ModuleHost suites, the integration suite that exercises the module host's async path (`ResilienceIntegrationTests`), and the fixed-step `stechovice-battle` regression unchanged |
