<!--STATUS
state: LIVE — the behaviors lane's report on HANDOFF_Coordinator_To_Behaviors_Possession_Release.md (PR1).
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Coordinator_To_Behaviors_Possession_Release.md — the dispatch this answers.
  - docs/DESIGN_Entity_Control.md — §4.3: the new release arm is folded into the possession sequence.
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — §4-F (F1), the decision built.
-->

# REPORT — behaviors → coordinator/backend: PR1, release a possessed unit when its Player's node leaves

**Built on** `behaviors` after merging `backend` at `636a08b48` (the dispatch commit is its ancestor). Tracker id **`CE-2231`**
([row](../Blueprint_Issues_Tracker.md)). Cross-lane: `FDP/Toolkits/Fdp.Toolkits/Behavior/Possession/Possession.cs`.

## What was built

`PossessionSystem.Execute` reads `NodeDeparted` (when the bus has it registered). For every unit this node runs the brain of whose
`PossessedBy.StationNodeId` equals the departed node, it calls the SAME `Release` that Esc uses. That release removes `PossessedBy`,
which replicates as `Held = 0`. It also clears the behaviour at origin Possession, so the ingress resumes the paused task. A unit
held from another node is untouched. There is no timer (R-4001). The arm is folded into [Entity Control §4.3](../../DESIGN_Entity_Control.md)'s
`alt` (Esc / killed / **the station's node departs**).

⚠ **One assumption, stated:** `PossessedBy.StationNodeId` and `NodeDeparted.NodeId` are the same id space. `EntityOrders.StationFor`
sets the station id from `NetworkAuthority.LocalNodeId`, and `ClusterCapabilityIngestSystem` raises the departure with the
descriptor's `NodeId`. Both are cluster node ids, but no test pins that they are equal on a live cluster. The cross-node suite
(`EntityOrdersAcrossNodesTests`) would be the place; it was not run here (T3, ClusterRunner.Integration).

## Gates

| gate | command | result | `--no-build` |
|---|---|---|---|
| T-1 the feature's suite + the new rail | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "FullyQualifiedName~Possession"` | 4 / 0 (3 existing + `CE2231_TheHoldingNodeDeparts_ItsUnitIsReleased_AndItsTaskResumes_AnotherNodesUnitIsKept`) | yes, after building the TEST project |
| red-proof | the arm keyed on `gone.NodeId + 100000` (compiles; never matches) | ✅ RED (1 failed) — reverted | built |
| SimHost suite | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | 1491 / 0 / 6 skipped (after the ui + backend merges, on the final tree) | yes |
| tracker | `python3 scripts/tracker-counts.py --check` | OK | — |

Ids allocated: `CE-2231`. The tree is clean after the runs.
