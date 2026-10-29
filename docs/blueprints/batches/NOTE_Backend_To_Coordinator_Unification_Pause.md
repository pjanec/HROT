<!--STATUS
state: LIVE — backend → coordinator: backend development is PAUSED until the run-modes duplicates are unified (user, R-3066)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Cluster_View.md — CE-1436 (ui's frame), V1–V5 approved: one cluster-facts ingress + one ClusterView on every node
  - ../RULINGS.md — R-3066 (this pause and its order)
  - NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md — ui's frame (on `ui`, `fcbacaae8`)
-->
# NOTE — backend → coordinator: development paused; unification first (R-3066)

🔒 **User, `2026-10-29`:** *"We need to pause any development until we enforce the unification and sharing."* · *"Approved both."* (the
`ClusterView` design and the order below) · and *"stop running sonnet agents in parallel on different tasks. Keep focus on single task."*
(now in CLAUDE.md, Sonnet delegation).

## What is paused (work kept in its worktree, nothing merged)

| task | state |
|---|---|
| **CE-3339** the two scenario hashes (your `HANDOFF_Coordinator_To_Backend_Scenario_Hashes`) | partly built, stopped |
| **CE-4095** a new scenario from a checkpoint | partly built, stopped |
| **CE-4089** the content move, round 2 (L6 + your CE-4091 review items) | merged with backend, stopped |
| **CE-4099** autosave | designed (AQ108 §4c + A8 from your review), not started — the user deferred it |
| **CE-4092** start with a scenario | not started (waits on CE-4089) |

⚠ `HANDOFF_Coordinator_To_Backend_Op_Status_On_Nodes` (the narrow "add ClusterOpStatus to the slave reader") is **superseded** by ui's frame
and the `ClusterView` design — not built separately.

## The order, one Sonnet task at a time

1. **`ClusterView` (CE-1436)** — [`DESIGN_Cluster_View.md`](../../DESIGN_Cluster_View.md): one ingress for the four fact topics, one read model
   per node (`ClusterUiCache` composes it; `ClusterSlave`'s follow-fields — incl. backend's CE-4090 flags and ui's CE-4096 ones — delegate),
   built in `EcsHostClusterHandlers.Compose`. Closes the CGF / SimHost / IG unsaved-check gap.
2. **The duplicates backend found in its own run-modes work** (tracker `CE-3340`): recorded copies under the reserved `Recipes/` prefix; two
   scenario-version stamps (the director's takes read the stale load-time one); the content seed wired in two hosts instead of the shared bootstrap;
   `assets/behaviors` spelled twice (`BehaviorGroups` — the "analyzer cannot reference it" claim to be VERIFIED); and, found on the way, the 15 s
   unsaved-gate timeout (R-4001: the heartbeat is the only time limit).
3. **Rails that fail on a duplicate** — one cluster read model per node; every host composing the same handler set; no content path outside
   `ContentLayout`.
4. A row for the per-test private `RepoRoot()` helpers (pre-existing, many).

Then the paused tasks resume in the order above (CE-3339 → CE-4089 → CE-4095 → CE-4092; CE-4099 when the user un-defers it).

## ⭐ Update (`2026-10-29`, later)

- **Done and pushed:** CE-1436 `ClusterView` (every ECS node reads the cluster facts; the unsaved check reaches CGF/SimHost/IG — live-cluster rail);
  CE-3340 ② (one reader of "which version ran") and ⑤ (no fixed 15 s gate timeout, R-4001).
- **Running (the only agent):** CE-4089 finish (L6 recipe names) + CE-3340 ① ③ ④.
- **Decided by the user:** R-3067 — retire ExCon after ClusterView, keeping only a generic opt-in wire inspector (AQ109 §5b); R-3068 — the armed
  mode is ONE cluster-wide fact (AQ108 D12a), its default a shared bootstrap parameter sent by the host that starts a load
  (`NOTE_Backend_To_Ui_Armed_Mode_Cluster_Wide.md`); backend half is CE-3341, queued.

_backend lane, 2026-10-29_
