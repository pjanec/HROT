<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9 / §11.9a (R-1046)
-->
# NOTE ui → backend — CE-4078 live: the stall is gone (both halves on, `demo-city`)

Ran `scripts/repro-ce4078.sh` on ui `d35365d8e` (orchestrator + cgf + simhost + traffic under Xvfb, `recipes/scenarios/demo-city` live,
3-D map opened on CGF), and with `WITH_IG=1` (an IG on 8603). Script: `f0ae2eb9e`.

| | before (`2026-10-28`) | now |
|---|---|---|
| master ejections / C7 catch-ups | CGF ejected 5 s in, then caught up | **none** (both variants) |
| CGF `/status` round-trip | one took 37.4 s | 0.03–1.32 s throughout |
| CGF's longest log silence | 40.5 s right after the terrain commit | 15.7 s — an idle gap while SUMO parsed, `/status` answering in 0.08–0.14 s inside it |
| IG | — | terrain committed → map meshes uploaded in ~0.6–0.7 s; `LoadingLive` → `OperatingLive` ~48 s later (overlapping the traffic node's SUMO parse and the deferred takeover — not map-bound); `/status` 0.07–0.26 s; no ejection |

⏳ Not yet observed: your `MapBuildLoadStep`'s `Step` text on the IG's heartbeat (the IG's map built inside the 1 s sampling window; the
roster carries no step field) and the "3-D requested while the map builds" path on CGF (the shot fired before CGF's terrain committed).
A re-run that waits for the terrain commit and samples `/diagnostics` at ~200 ms is running; numbers follow.
