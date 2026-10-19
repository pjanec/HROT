<!--STATUS
state: LIVE — backend → behaviors: the answer to NOTE_Behaviors_To_Backend_Interior_Move_Stall (CE-2304).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/navig-2/Navigation_Design_v2_0.md — §3.4a: the as-built of the backend half (the storey gap, the walker kept on the mesh, the no-progress watchdog, a re-sent move continues)
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §8c: the posts and the behaviour that re-sends the move
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Interior_Move_Stall.md — the question
-->

# NOTE — backend → behaviors: the interior stall, measured — four backend causes fixed, one on your side

**Not orders.** On `backend`. Row: [`CE-2304`](../Blueprint_Issues_Tracker.md).

## What a two-node probe showed *(sq-defend-house, baseline binaries, both the brain node and SimHost read every ~13 s)*

| finding | 📐 measured |
|---|---|
| ⭐ **the brain re-sends each post move about twice a second** | Rifleman 2's `NavigationIntent` to **the same point** `(117, 153.9, 3)`: `IntentId` 5 at 12 s, 23–27 at 25 s, 55 at 40 s. Every man's id climbs in step. **Each re-send made SimHost stop the man, drop his path and wait for a fresh one** (`NavState` back to `None`, speed 0), so he walked in bursts (~0.4 m/s) and every stuck count restarted from zero — no watchdog could ever fire |
| a man pushed through a wall | Rifleman 1 starts inside at (120.9, 156); at 12 s he is at (120.4, 151.7), 1.3 m outside the south wall (y 153). An AI walker had no wall check at all (only the operator's, CE-3181) |
| avoidance across storeys | the collider grid is 2-D: a man upstairs gave way to men in the room below |
| moving but getting nowhere | the Leader sat at path progress 0.004 m with his speed flickering; the stuck count (speed < 0.2 m/s for 2 s) kept resetting |

## What backend changed *(your two asks)*

| | |
|---|---|
| **① not through walls** | an AI walker's step that avoidance bends is kept on the walkable navmesh — the same `KeepOnNavmesh` as an operator's (slide along the edge, or stop) |
| **② a stuck move ends** | a walker on a path who gains < 0.25 m in 4 s ends his move `FailedBlocked` (or replans first, if the move allows replans). Your `MoveTo` executor already maps that to `Failure` |
| ⭐ **a re-sent move continues** | a new `IntentId` for **the point the path already leads to** (≤ 0.25 m across, same storey) keeps the path and the stuck count; only speed and arrival radius follow the new intent. So your re-sends no longer stop him |
| storeys | avoidance ignores a man more than 1.5 m above or below |

## ⭐ The one on your side — please look

**Who re-sends the post move twice a second?** Even with the muscle tolerating it now, a re-send every ~0.5 s costs a DDS sample per man per re-send and hides a behaviour that keeps re-entering its move. 📐 The intent ids above are the evidence; the destination never changed. My guess (not measured): `CombatPosture`'s approach re-enters `MoveTo` each time the utility re-picks Advance (`CooldownSeconds` 0 on the posted men's advance params). If a re-send IS meant (to re-point a moved post), it still works: a different point is a new move as before.

⭐ **Re-measured on the fixed binaries (`f1f4c89ba`) — the churn is worse than "the same point twice":** the ids reach ~100 by 40 s,
and most re-sends are a **stop then a start** (`Mode None` with id N, then `PathToPoint` id N+1 — an `OnExit` then an `OnEnter` of
the move executor) or a move to a **different** post point a few seconds later (Rifleman 2: (117, 153.9, 3) → (115.9, 154.55, 3);
Rifleman 6: three different points in 40 s). Neither is a "re-send" the muscle can keep — a stop stops him, a new point re-plans — so
the men still spend the posting phase starting and stopping. ⇒ the fix is on your side: one move per post, re-issued only when the
post itself changes.

**Repro:** `python3 scripts/utility-demo-check.py sq-defend-house --launch`. Probe: `/tmp`-style script that reads `NavigationIntent.IntentId` per man from the `Scenario` perspective every few seconds.
