<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the item table
stale-below: none
related-designs:
  - docs/DESIGN_Debug_Api_Http_Only.md — THE design of part A (§2–§5) and B-6 (§6); as-built folded there
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — how a process is started; B-5 adds the performance switches
-->

# STATUS — the HTTP API (HANDOFF_Coordinator_To_Backend_Http_Api.md)

Dispatched at `eb43032c3`, scope frozen there. Branch `backend`, started at `247525ec3` (merged `coordinator@28b430a8f`).

| item | row | state | result |
|---|---|---|---|
| H-1 | CE-4523 | ✅ | the source-scan rail over 224 routes; 1 wrong place fixed (invoke `scope` → body), 12 undocumented keys documented, 5 param docs moved from the GET to the POST variable route; red-proved |
| H-2 | CE-4524 | ✅ | prose embedded in `Hrot.Editor/DebugApi/Guide/`, HTTP-worded (rail) |
| H-3 | CE-4525 | ✅ | `GET /help` (markdown, availability marks) + `--help-api`; rails red-proved; live 2 637 / 2 636 lines |
| H-4 | CE-4526 | ✅ | server + `.mcp.json` entry + catalog rail removed; `NotATool` dropped, `Tool` kept as the id |
| H-5 | CE-4527 | ✅ | `.claude/skills/ai-debug-http` — a pointer; followed live |
| H-6 | CE-4528 | ✅ | HISTORY notes in 31 docs; no new design-digest failures |
| H-7 | CE-4529 | ✅ | design §7 as-built |
| B-1 | CE-1474 | ✅ | the dispatcher routes to the ONE world-holding provider when no perspective is claimed; live `simTime` 0 → 2.04 after play (headless and windowed) |
| B-2 | CE-1950 | ✅ | `LoadingReplay` is a load round (fails at once → Idle); the publishing routes' `waitForReady` follow the round outcome (409 + reason) |
| B-3 | CE-1472 | ✅ | the Player's canvas updates whenever it owns the map; live 1 frozen frame → 51 frames at 25 fps; no rail (display + server + player) |
| B-4 | CE-1464 | ✅ | measured first (Xvfb ~25 fps: rate-bound race, HTTP sometimes won); `HttpControlledUnits` — the station follows but sends no frames for a unit possessed over HTTP |
| B-5 | CE-4585 | ✅ | `GET /performance` + `/status.performance`; `settings.performance.*` refused (403, file untouched); runbook §1.5 |
| B-6 | CE-4586 | ✅ | `Fdp.Core.EnvVars`: 156 names (Switch 56 · Diagnostic 41 · Test 56 · Tool 3); 106 production reads migrated in 62 files; `/help` §8, `--help-api`, `/performance.environment` |

## Gates (part A)

| gate | result |
|---|---|
| `Hrot.Editor.Tests` `AgentGuide\|EveryRoute\|DebugApi\|Capabilit` | 225 / 225 |
| red-proofs | invoke `scope` back on the query ⇒ the params rail red · `/help` dropping `GET /status` ⇒ `Help_NamesEveryDocumentedRoute` red |
| live | `--help-api` exit 0, 2 636 lines; editor `GET /help` `text/markdown`, 2 637 lines, 1 route marked not-here; `GET /status` ok |
| `design-digest.py --check` | identical to the base (pre-existing failures only) |
| grep `ai-debug-mcp\|tool-catalog` outside `.dev/`, batches and HISTORY notes | 2 HISTORY mentions in code comments (the rail's and the csproj's "moved from") |

## Gates (part B)

| gate | result |
|---|---|
| `dotnet build HROT.sln --no-restore` (after B-6) | 0 errors |
| `Hrot.Orchestrator.Tests` (whole) | 482 / 482 |
| `Hrot.Editor.Tests` `DebugApi\|EveryRoute\|AgentGuide\|DebugApiOrders\|Replay` | 220 / 220 |
| `Hrot.ClusterRunner.Tests` `PerformanceStartup` | 14 / 14 |
| `Hrot.ClusterRunner.Tests` (whole — it builds again, CE-1473) | 427 / 433 — ⚠ 6 reds, none in this batch's diff: drifted while the project did not build ⇒ filed **CE-3451** |
| `Hrot.Presentation.Tests` `PossessionStationTests` | 74 / 74 |
| `Fdp.Core.Tests` `EveryEnvReadIsRegistered` | 5 / 5 |
| `Hrot.SystemTests` `TheFusedServer_ReportsSimTime_AndPlayRunsTheClock` (boots `--mode server`) | 1 / 1 (9 s) |
| red-proofs | B-1 dispatcher world-holder loop · B-2 `LoadingReplay` dropped / `RoundSettled` ignoring Failed · B-4 the send gate · B-5 env source + refusal · B-6 a raw read in `WorldGuard.cs` — each ⇒ its rail red, restored ⇒ green |
| live | B-1 server headless + windowed; B-3 Player + server; B-4 windowed editor, possessed M1 |

**Experiment leftovers, LISTED for the user (not deleted — "no rush removals"):** `HROT_CONTENT_VERSION` (content-version bump counter, parallel-sim POC) · `HROT_PUBLISH_PROBE` (+`_EVERY`, `_OUT`, `_MASKS`) · `HROT_REPLICA_PROBE` · `HROT_CONTACT_PROBE` (+`_DETAIL`, `_OUT`) · `HROT_POC3_WALK` (+`_OUT`) · `HROT_TWO_WORLDS_MASKS` · `HROT_TWO_WORLDS_MASK`.

**Ids used:** CE-4523…CE-4529, CE-1474, CE-1950, CE-1472, CE-1464, CE-4585, CE-4586 (dispatched); **CE-3451** allocated (backend block). Cross-lane edits, said: ui's Player (`PlayerApplication.cs`, B-3) and station (`PossessionStation.cs`, B-4).
