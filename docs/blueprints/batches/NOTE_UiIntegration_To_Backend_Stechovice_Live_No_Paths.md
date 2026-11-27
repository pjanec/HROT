# Note: `unreal-ig` → `backend`: live on Stechovice no vehicle gets a path — the NavigationSolver's 100 ms budget throws its work away (CE-11076)

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-27 |
| **For** | `backend` (the module host and the navigation solver; F-3 / CE-4503 and CE-11057 ③ are yours) — `terrain` FYI (it was asked to triage CE-11076 first) |
| **Found by** | Unreal's live bridge test ([NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md](NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md)): every HROT-driven vehicle move `FailedBlocked` within ~2 s |
| **Reproduced** | `unreal-ig` `18ef540f5`, Debug, `--mode editor` under Xvfb, `showcases/stechovice-battle` loaded live (realtime factor ≈ 1, ~5 800 entities) |

## Measured

| | |
|---|---|
| the failing move | Riverbank Blue Tank 2 (1142), ordered 40 m on open bank (`POST /entities/1142/order {kind:move}`): drove 15 m, then **speed 0, steering 0, heading frozen**, `FrustrationTicks.SlowSeconds` 0 → 2 s ⇒ `NavigationStatus.Result FailedBlocked`. A second order straight behind it: it never moved at all, `FailedBlocked` 2 s later. `NavState.IsBlocked` 0 (no wall, no door held it); a terrain trace finds only a `forest` area round it, no prism |
| ⭐ the cause | the editor log, ~4 min: **`[ModuleHost][TIMEOUT] Module 'NavigationSolver' timed out after 100ms. Task abandoned … its output was discarded`** ×181, and **`[CIRCUIT-OPEN] Module 'NavigationSolver' is SKIPPED for 10000 ms after 5 consecutive failures`** ×33 (also `Eqs` timed out ×22) ⇒ path answers are discarded or never computed; a move with no path stands still, and the frustration rule (2 s, CE-11057 ③) fails it |
| why the regression passes | `ModuleHostKernel.cs:1013, 1068-1078` (F-3, CE-4503): a run dispatched on a STEPPED frame is waited for to its end, never abandoned — the deterministic regression never meets the 100 ms wall-clock budget; a live run does |

⛔ Not measured: why one solver run takes > 100 ms here (the request count per run, the Stechovice navmesh size, the battle's load), and whether a discarded run's path REQUESTS are lost for good or answered later.

## Asked — your lane's call; leans

| # | lean | why |
|---|---|---|
| ① | ⭐ a timed-out solver run must not LOSE its requests: requeue the unanswered ones (or apply the late result) instead of discarding the output | today a slow frame silently drops paths; the circuit then drops 10 s of them |
| ② | ⭐ the solver bounds its own work per run (answer requests up to a budget, carry the rest to the next run) so it does not hit the host's timeout at all | a big terrain + a battle is exactly when paths are needed |
| ③ | a move still WAITING for its first path is not "slow" for the frustration rule | a planned-but-unanswered move standing still is not blocked |

Acceptance: `stechovice-battle` LIVE — the scenario's *"a Blue tank ordered over the bridge reaches the west bank"* row, and an ordered M1 on the open east
bank reaching a point 40 m away; no `NavigationSolver` circuit-open in the log.
