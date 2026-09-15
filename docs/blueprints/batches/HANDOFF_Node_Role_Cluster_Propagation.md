<!--STATUS
state: LIVE
build-state: HANDOFF — dispatched to the UI lane (H - ui, `claude/reset-working-branch-qd1qpv`). First
  buildable slice of the cross-node construction barrier: propagate node role masks across the cluster.
updated: 2026-09-15
current-answer: this is the dispatch. The owning DESIGN is DESIGN_Cross_Node_Construction_Barrier.md
  §5 (P1–P3) + §0 (INVENTORY, the exact sites). Build against it; do not redraw its diagrams here.
related-designs:
  - ../../DESIGN_Cross_Node_Construction_Barrier.md — §0 INVENTORY (sites) · §5 P1–P3 (this slice's spec).
  - ../../DESIGN_Role_Affinity_Ownership.md — NodeRole is the role vocabulary; roles are labels, engine holds no semantics.
-->
# HANDOFF — cluster orchestrator: propagate node role masks *(P1–P3)*

> 📌 **Scope is FROZEN at the coordinator HEAD named in the dispatch message.** Documents that change
> after it are FYI only. ⭐ Rule 7: re-sync from `claude/blueprint-authoring-status-6sr5ld` FIRST. ⭐ Rule 1b:
> push an empty `chore: started <slice> at <sha>` marker before writing code. ⛔ No PR. ⛔ Continue `CE-` ids
> (plain increments, rule 3a-id) — you allocate them.

## 1. 🎯 GOAL
Make the cluster orchestrator **properly handle and transfer node roles**: every node's `NodeRole` **mask**
travels the cluster and is queryable — the prerequisite for reliable-init peer membership. Today the role is
**dropped on the wire and re-guessed lossily downstream** — fix that end to end.

## 2. 📐 THE THREE GAPS — measured `2026-09-15` (do not re-measure to confirm; verify then fix)
| # | gap | fix |
|---|---|---|
| **P1** | `NodeHeartbeatEvent` = `{ NodeId, LocalStateId, WallTicksUtc, SubsystemName }` — **no role mask**; role is re-derived by the lossy `NedNetworkFactory.MapSubsystemNameToRole` *(3 names → ONE role, else `None`)*. Producers: `ClusterSlave.cs:153` · `OrchestrationObserverTranslator.cs:86` · `NedOrchestrationTranslator.cs:58` | **carry the node's `NodeRole` mask on the heartbeat** (the node knows its own declared mask at boot). ⭐ **RETIRE `MapSubsystemNameToRole`** — read the mask off the heartbeat (seam law: the source has it, stop reconstructing it from a display string). ⚠ the heartbeat is also a DDS topic *(`NodeOpSlaveTranslator` writes it)* — carry the mask on the wire too |
| **P2** | the orchestrator roster `NodeHealthProfile` stores `SubsystemName` only, **no role**; `SimpleClusterStateCache.NodeCapability.Role` exists but is fed by the lossy switch | **store the role mask** on `NodeHealthProfile` (orchestrator `NodeRoster`) AND keep `NodeCapability.Role` populated from the real mask |
| **P3** | no "present nodes whose mask ∩ R ≠ ∅" query — `SimpleClusterStateCache` exposes only `GetLeastLoadedNode`; `NodeRoster` exposes `ActiveNodes` (no role) | **add a membership accessor** — `NodeRoster.NodesWithRole(NodeRole)` and/or a cache equivalent |

⛔ **This slice is ONLY role propagation** — the peer barrier / gateway / participants come later (design
pieces A–C). Stop cleanly at the P1–P3 boundary.

## 3. ⭐ PROCESS (frame-delegation)
1. ⭐ **Design detail first:** fold the P1–P3 as-built into `DESIGN_Cross_Node_Construction_Barrier.md` §5
   *(and correct §0 if a site moved)* — the classes touched + the heartbeat sequence, marking the prior
   state superseded. It is a small slice; a short as-built note + updated §5 suffices, no new diagram unless
   the wire shape changes.
2. ⭐ Build the **affected projects only** *(Fdp.Core / Fdp.Toolkits.Orchestration / Hrot.Orchestrator /
   Hrot.Network.NED)* — build once, then `--no-build`. Run builds/E2E in the BACKGROUND.
3. ⭐ **T-1 first:** find + run the feature's own suite before adding rails *(orchestration/roster/cluster-state
   tests)*; add rails INTO those suites, red-proofed.

## 4. ✅ ACCEPTANCE
- A node's **declared role mask** *(possibly multi-role, `NodeRole` is `[Flags]`)* is visible in the
  orchestrator roster and resolvable by role via the new query — proven on `--mode all` *(brain=CGF,
  muscle=SimHost, map2d=IG each show their true mask)*.
- `MapSubsystemNameToRole` is **gone** *(or reduced to a pure fallback for a missing field, argued in the report)*.
- Existing orchestration/cluster suites green; new rails red-proofed; a `--role`-style multi-mask node
  *(e.g. `MuscleGround|Perception`)* is preserved end to end, not collapsed to one role.

## 5. ⭐⭐ REPORT BACK TO **H - coord** (the gate contract — this substitutes for my re-run)
One message when done or blocked, carrying: **the `CE-` ids allocated · the design §5 edit you folded · one
row per gate (verbatim command · pass/fail/skip · `--no-build` · delta vs base) · every RED confirmed
pre-existing against the base sha · working tree clean · the diff shape *(files, additive vs destructive)* ·
`rulings-check.py` + `design-digest.py --check` verdicts.** ⛔ If a premise here is false, STOP that item and
report — do not adapt silently *(scope frozen)*.
