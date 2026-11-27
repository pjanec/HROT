<!--STATUS
state: LIVE — a NOTE (protocol §5): the report on your handoff · updated: 2026-11-27 · current-answer: the whole file
related-designs:
  - HANDOFF_UiIntegration_To_Backend_Navigation_Solver_Live_Budget.md — yours, the handoff this answers
  - ../../designs/navig-2/Navigation_Design_v2_0.md — N6–N8, the rules as built
  - ../../projects/FDP/Core/Fdp.ModuleHost.md — §5, the run budget and why the discard stays
-->
# NOTE — `backend` → `unreal-ig`: the live NavigationSolver budget (CE-11076) — measured, three fixes, the M1 arrives

Answer to [HANDOFF_UiIntegration_To_Backend_Navigation_Solver_Live_Budget.md](HANDOFF_UiIntegration_To_Backend_Navigation_Solver_Live_Budget.md) (scope frozen at
`e269b0918`). Built on `backend` with `unreal-ig@d9658d023` merged (`44a84bfe7`). Ids from backend's block: **CE-3503 · CE-3504 · CE-3505**.

## ④ Measured first (a probe in the solver, live, `stechovice-battle`, `--mode editor`, Debug, 4-core VM)

| | |
|---|---|
| a run | 188 runs in ~3 min: **10.6 requests and 74 ms a run** on average, max 403 ms; **64 timed out**, the circuit opened 8 times |
| ⭐ what is slow | **unreachable** requests — 106 of the 124 over 20 ms; **each one alone 80–270 ms** (the A* runs to its 20 000-iteration cap); 44 % of all solver time. Reachable requests are cheap |
| ⭐ why so many | **42 of 97** unreachable requests were CE-4674's *"re-plan a failed move once the mesh changes"* — the live overlay bake changes the mesh all the time, so every answer came back older than the mesh and the move re-planned at once (one move: 5 re-plans in under a second) |
| were requests lost? | ✅ yes, three ways: the host discards a timed-out run's answers (CE-3066); an open circuit skips 10 s; and the solver DROPPED everything past 256 a run. The mover asked once (`NavigationIntentBridgeSystem`) and waited |

⇒ ② alone could not fix it (one request outlasts any per-run budget), so the fix is at both ends.

## What was built (rails red-proved: each of four mutations went red)

| item | as built | vs. your lean |
|---|---|---|
| ③ **CE-3503** | a point move **waiting for its first path** (no trajectory, held by the bridge, not a crowd agent) is not frustrated; it **re-asks every 3 s** and fails `FailedUnreachable` (logged *"no path answer"*) only after **30 s** (past the 10 s circuit break). With a path, the 2 s speed rule is unchanged | ⭐ as leaned. ⚠ No new result value: it would change the wire enum (`ENavigationResult`) — the log names the cause |
| ① (by CE-3503) | the CALLER keeps the request/reply contract: any lost answer (a discarded run, an open circuit, a dropped sample) is re-asked | ⚠ **deviation**: not a host requeue — the host cannot tell which inputs an abandoned run had answered, and ageing modules (perception, EQS) must not get theirs again. `Fdp.ModuleHost.md` §5 records it |
| **CE-3504** | a failed-unreachable move re-plans on a mesh change **at most once per 5 s** of sim time | new — the storm the measurement found |
| ② **CE-3505** | the host gives each background run its **own wall budget** (`ModuleRunContext.WallBudgetMs` — half the timeout live, **0 on a stepped frame**, R-3035 untouched); the solver answers within it and **carries** the rest to the next run, never drops (the 256 cap is gone); a load drops the carry | ⭐ as leaned. ⏳ OPEN: one unreachable request still costs 80–270 ms, so a run holding one can still overrun — CE-3503 recovers its answer; a cheap "unreachable" (polygon connectivity, or a cost cut-off) is the follow-up |

## Acceptance — live, the same scenario, after the fix

| row | result |
|---|---|
| ⭐ Riverbank Blue Tank 2 (1142) ordered 40 m ahead on the open east bank | ✅ **arrived** — 0.8 m from the point, `NavigationStatus.Result = Arrived`, `ReplanCount 0` (before: drove 15 m, stood, `FailedBlocked` at 2 s) |
| no `NavigationSolver` circuit-open | ✅ **0** in ~3 min (before: 8 in 3 min, 33 in 4 min) |
| a timeout followed by the answers arriving | ⚠ **40 timeouts** in ~3 min remain (one unreachable request at a time — CE-3505's open half); **0 "no path answer"** — no move failed for them |
| *"a Blue tank ordered over the bridge reaches the west bank"* (`checks.json`) judged LIVE | ⏳ not judged here (no live verifier run) — yours to re-run in your bridge test |

## Gates

| gate | result |
|---|---|
| `Fdp.Toolkits.Tests` — `Navigation` + `CarKinem` (`--no-build`, fresh binary) | ✅ 855 / 855 |
| `Fdp.ModuleHost.Tests` — whole (incl. `ResilienceIntegrationTests`, the F-3 stepped rails) | ✅ 269 / 269 after one fix: `KernelFrameTimingsTests` read 12 parts for 2 — the timer is process-global and my new kernel rail overlapped it (red 2 of 3, green at base 3 of 3) ⇒ its collection now runs alone |
| the fixed-step `stechovice-battle` regression | ⏳ not run (backend's gate stops at the suites above; a stepped run answers every request — budget 0 — so its behaviour is unchanged by ②) |

⚠ FYI: `POST /scenario/load/live` on a fresh VM answers *"did not reach a settled OperatingLive within 30s"* while Stechovice's terrain imports
on first use; the load completes on its own a few minutes later.
