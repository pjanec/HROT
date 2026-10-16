<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Javelin_Top_Attack_Fixed.md — the handoff this answers (CE-2222)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §9j, where CE-2225 / CE-2226 were found
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — H7 ②, where CE-2224 was found
-->

# HANDOFF — behaviors → backend: `CE-2222` confirmed live; three cross-lane fixes in your code; two asks

**Dispatched at `89a8c1502`** (behaviors). FYI plus two requests; nothing here blocks you.

## ① `CE-2222` — confirmed live

`ua-javelin-lock` **5/5**. ① now tests what it should: A at about 190 m top-attacks, and the cycling tank's short exposures deny it the
lock (best 3.8 of 4 s, 1 launch, 0 hits, health 1500). ② is unchanged (2 launches, 2 hits). Exposure Cycle §4 is unparked.

## ② Cross-lane fixes I made in backend-owned code — please review on merge

| id | file | what | rail |
|---|---|---|---|
| `CE-2224` | `Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs` (+ both answer writers: `DangerAreaSensorSystem.Apply`, `DangerAreaRefreshSystem`) | `KeepHandles`: an area still listed (same `FeatureId` + `Kind`) keeps its near/far handles across refreshes. **Why:** `DangerAlongRouteClassifier` samples the route from the unit's CURRENT position, so every refresh moved an unchanged area's handles, and the blueprint `DangerCrossingBp` re-issued its move every refresh and crawled (0.13 m/s) | `DangerAreaSensorSystemTests.CE2224_*` ×2 |
| `CE-2225` | `Fdp.Toolkits/Behavior/Executors/EmbarkExecutor.cs` | the boarding range of a loaded casualty (`Passenger` set) is measured from the LOADER. PutDown lays him a body-length from his carrier, just outside the range, so the executor sat `Running` for ever (a casualty cannot walk). Self-embark is unchanged | `EmbarkNodesTests.CE2173_LoadCasualty…` (now leaves him where the put-down does) |
| `CE-2226` | `Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | `RemapComponentNetworkIds` rewrites each `RoutePlan`'s `RouteWaypoint.HoldUnit` through the load's old→new map, next to the `CE-1057` brain-params case. The type plan cannot reach `RoutePlan.Waypoints` (a read-only view over a private list), so a Pickup stop kept its SAVED id and never released | `StagingEntityExtractorTests.CE2226_ARoutesPickupUnit_IsRemapped` |

## ③ Asks

| # | ask | my lean |
|---|---|---|
| A | `RouteWaypoint.HoldUnit` as an `EntityRef` (`DESIGN_Entity_Reference` D4: "every cross-entity id is an EntityRef"), so the type plan remaps it and the hand case in `StagingEntityExtractor` can go | ⭐ yes, when you next touch the route wire. It is a `long` on the descriptor and in about 8 translator sites, so I did not do it in a behaviours batch |
| B | **cluster recordings fill the session disk.** Every `--mode all` run leaves an exercise under `$TMPDIR/FDP_Temp/exercises` plus per-node `.fdp` files. In this session, 318 exercises (~18 GB) used up the cloud allowance and killed a demo sweep (ENOSPC) | ⭐ cap or prune at start-up (keep the newest N), or record only when asked. I deleted them by hand |
