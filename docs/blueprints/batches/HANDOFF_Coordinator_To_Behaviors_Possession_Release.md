<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — §4-F, approved 2026-10-15: THE decision this item builds
  - docs/DESIGN_Entity_Control.md — owns possession (§4 sequence, §8c release arms); fold the new release arm in here
  - docs/DESIGN_Ownership_Groups_And_Grants.md — owns NodeDeparted (R-167), the event this reuses
-->

# HANDOFF — behaviors: release a possessed unit when its Player's node leaves

**For:** the behaviours lane (`behaviors`). **From:** the coordinator lane. ⭐ **Dispatched at the commit that adds this file**:
your scope is FROZEN there; documents that change after it are FYI. If a later document invalidates the item, STOP and report it.

⭐ One small item, fully decided — no frame to design. ⚠ A coordinator-branch session is building the backend half of the Player
work in parallel (`HANDOFF_Coordinator_Player_Nodes.md`); it does not touch `Possession.cs` or the possession tests.

## 0. Why

The user is turning the system into a multiplayer game with disposable Player nodes (IG nodes) that may disconnect at any time
([Q91](../Architect_Question_91_Player_Nodes.md), approved). 📐 `PossessionSystem` releases a unit only on `ReleasePossessionEvent`
(Esc) or the unit's death (`FDP/Toolkits/Fdp.Toolkits/Behavior/Possession/Possession.cs:177-183`). ⇒ a Player that crashes while
possessing leaves the unit possessed forever, its task paused (R-203).

## 1. Before anything

Merge `coordinator` at the dispatch commit or later into `behaviors`. Push a `chore: started …` marker (rule 1b). Ids from your
block (`CE-2xxx`).

## 2. The item

| # | item | design | acceptance |
|---|---|---|---|
| **PR1** | `PossessionSystem` also releases every unit it holds whose `StationNodeId` equals the node in a `NodeDeparted` event — the SAME release path Esc takes (the paused task resumes, `EntityPossessedBy.Held = 0`). ⭐ `NodeDeparted` already exists and is raised on every node when a node's capability descriptor goes not-alive (`Hrot/Network/Hrot.Network.NED/Routing/ClusterCapabilityIngestSystem.cs:115`); `PartialOwnerReclaimSystem` already consumes it | Q91 §4-F (F1) | ⭐ T-1 first: the possession suite (`Hrot.SimHost.Tests/PossessionReplicationTests.cs`; `EntityOrdersAcrossNodesTests` for the cross-node case). Add the rail THERE: a unit possessed from node N, then `NodeDeparted{N}` ⇒ released, task resumed; a unit possessed from another node is untouched. Red-proof with the arm removed |

## 3. Fences

- ⛔ **No timer** (R-4001): Q91's former F2 (a control-frame staleness timeout) is SUPERSEDED — the release keys on the node leaving, nothing else.
- ⭐ Fold the new release arm into [`DESIGN_Entity_Control.md`](../../DESIGN_Entity_Control.md) §4's sequence (the `alt` with Esc / unit killed) — obligation ⑤.

## 4. Report back

The gate report contract (CLAUDE.md rule 8): one row per gate with the verbatim command, counts, delta vs the dispatch sha, a
`--no-build` column, every red confirmed pre-existing, the tree clean, `tracker-counts.py --check`, every id allocated; the
design folded; the sha on `behaviors`.
