<!--STATUS
state: LIVE — OPEN (backend, 2026-10-29): retire the ExCon node completely, unless a good reason to keep it is found
updated: 2026-10-29
build-state: DESIGN — the measurements in §3 come first; leans in §4 await the user
current-answer: §1 the user's account · §3 what ExCon carries and where each would go · §4 sub-questions with leans
stale-below: nothing
known-rot: docs/projects/Hrot/Subsystems/Hrot.ExCon.md (2026-05-23) describes ExCon as a live role — superseded by §1 once decided
related-designs:
  - ../DESIGN_Cluster_View.md — CE-1436: the second ingress + read model exist because of ExCon (V2 keeps its monitoring topics); retiring ExCon removes the reason for V2's difference
  - Architect_Question_91_Player_Nodes.md — the Player node = an IG node (Q91); IG loses the remote-map role ExCon drove
  - ../DESIGN_Node_Roles_And_Policies.md — §5 ExCon issues unowned requests; §5a its private ORBAT fork (UXI-25 / CE-259t)
  - ../DESIGN_Perspective_Unification.md — ExCon is one of the mapped perspectives (7 windows)
  - ../projects/Hrot/Subsystems/Hrot.ExCon.md — the project as documented in May
-->
# Architect Question 109 — retire the ExCon node

## 1. 🔒 Why ExCon exists — the user's account (`2026-10-29`, verbatim)

> *"ExCon was built without a world, exactly to exercise the option not having a world on a lightweight UI only node. also to force
> communication over messages that are intentionally free of internal implementation details of the hosts, so that the UI stays isolated. For
> example, it should be using JSON representations of entity state requests. It should use high level entity creation requests. and it should use
> the IG node as a remotely controlled 2D map. I think the last point is the load bearing and driving reason for the ExCon existence. I think this
> main reason is already gone. Anyway, the IG node is now becoming the player node, losing its remote 2d/3d map role it used to have before."*
> · *"The intention is to retire the ExCon node completely. Unless we find a good reason to keep it."*

| reason ExCon was built | still true? |
|---|---|
| ① prove a world-less, lightweight UI-only node is possible | ⭐ proven — the lesson is kept; the node is not needed to keep it |
| ② force host-neutral messages (JSON entity-state requests, high-level entity creation requests) | ⭐ the CONTRACTS stay (they are used by other hosts and by HTTP/MCP); they do not need ExCon as their only client — ⏳ measure §3 row "contracts" |
| ③ ⭐ **load-bearing: drive the IG node as a remotely controlled 2-D map** | ⛔ **gone** — IG is becoming the Player node (Q91) and loses the remote-map role |

## 2. What retiring it would remove (the cost of keeping it)

The world-less design is what made ExCon special, and each special case is a second copy somewhere: a second cluster ingress + read model
(CE-1436, V2), a foreign scenario-save slice (`ExConScenarioSaveHandler`), no recorder (R-4011), a private ORBAT fork (UXI-25, blocked by
CE-259t), unowned requests (Node Roles §5), the IG-side remote-map machinery (`MapCommandController`, `ContextMenuSystem`, `MiniExConPanel`,
the `MapCommandRequest` / `MapCommandAck` / `ContextActionsUpdate` topics), and a perspective with 7 windows.

## 3. INVENTORY — what ExCon carries, and where each would go (⏳ the "elsewhere?" column is to MEASURE before any lean is final)

📐 Graph: `search_graph name_pattern=".*ExCon.*" label=Class` → 34 production classes; grep: **138** production `.cs` files outside the project
mention `ExCon` (230 with tests). Folders: `Adapters/` `Logic/` `Observer/` `Panels/` `Services/` `Windows/`.

| ExCon capability | its classes | already elsewhere? (to measure) | if retired |
|---|---|---|---|
| exercise control: pause / resume / step / time scale | ExConLogic, cluster-control window | the shared toolbar time control on every host (CE-4097 mode picker, `/sim/*`) | nothing to move |
| cluster monitoring: node status, 2PC history, diagnostics | `ClusterUiCache` (observer bus), DiagnosticsPanel, ExConDiagnosticsWindow | the Orchestrator's `ClusterControlWindow` / `DiagnosticsWindow` read the master's own `ClusterUiCache` | stays on the master host |
| ORBAT with mission-plan editing (the richest one) | OrbatPanel, ExConOrbatAdapter, MissionEditorService, ExConMissionWindow | the shared ORBAT seam (UXI-04) — but WITHOUT mission editing | ⚠ the one capability with no twin: mission-plan editing moves onto the shared ORBAT (UXI-25's job, now a move, not a retirement of a fork) |
| entity placement (spawner) | ExConSpawnerWindow, ExConSpawnShim | the shared Add Entity / placement on the map hosts | nothing to move (measure) |
| entity inspector (DER) | ExConDerEntityInspectorWindow | the shared inspector | nothing to move (measure) |
| IG context-menu computation | ContextMenuLogic, JsonContextMenuBuilder, IG `ContextMenuSystem` | the shared map entity menu (R-1019, `MapEntityMenuHandler`) | delete with the remote-map path |
| remote 2-D map driving of IG | IgMiniExConPanel / MiniExConPanel, `MapCommandController`, the map-command topics | — (the role is gone) | delete |
| request transactions + timeouts | RequestTransactionManager, PendingRequest | the shared request path (HTTP, MCP) — ⚠ check its timeouts against R-4001 | delete |
| data monitor (DDS events/s) | ExConDataMonitorWindow | ddsmonitor, `/diagnostics/architecture` | delete (measure) |
| scenario save / load slice | ExConScenarioSaveHandler / LoadHandler (`ExCon.Observer` foreign doc) | — | the foreign slice disappears; old scenarios carrying `foreign/node_<excon>.json` load without it (measure) |
| ⏳ **contracts** — JSON entity-state requests, high-level entity creation | (used by ExCon) | ⏳ which other clients use them (HTTP routes, MCP, other hosts)? | keep the contracts; retire only the node |

## 4. Sub-questions — ⏳ leans

| # | question | ⭐ lean | what would change it |
|---|---|---|---|
| **A** | retire ExCon completely? | ⭐ **yes** — its load-bearing reason (③) is gone, ① is proven, ② lives in the contracts, and §2 lists the duplication it keeps alive | a capability in §3 with no home after measurement, or a deployment that needs a world-less console (e.g. a thin client) |
| **B** | where does mission-plan editing go? | ⭐ onto the shared ORBAT, on every host that shows it (the editor and the master host at least) | the ORBAT unification (UXI-04) not ready ⇒ do it first |
| **C** | what happens to the IG remote-map machinery? | ⭐ delete it with ExCon (`MapCommandController`, `ContextMenuSystem`'s ExCon arm, `MiniExConPanel`, the three map-command topics — a wire change, retired values kept reserved per R-42) | a Player-node use for any of it (Q91) |
| **D** | what replaces the "exercise director's console"? | ⭐ the master host (the Orchestrator's windows + the shared map/ORBAT/time control) — or the editor — no new host | the user wanting a separate operator station ⇒ then an ECS-node role on the shared frame, never a world-less fork |
| **E** | `ClusterView` V2 (monitoring topics observer-only) | ⭐ with ExCon gone, only the master keeps monitoring, and it reads its own in-process bus ⇒ the observer translator retires too; V2's difference disappears | — |
| **F** | order | ⭐ after CE-1436 lands (it moves ExCon onto the shared ingress first, so the retirement is a deletion, not a rewrite) and before CE-3340's rails (so the rails do not pin ExCon's copies) | — |

⏳ **Next step (design only — development is paused, R-3066):** measure the "elsewhere?" column of §3 (graph + grep per row), then put the leans to the user.
