<!--STATUS
state: LIVE — OPEN (backend, 2026-10-29): retire the ExCon node completely, unless a good reason to keep it is found
updated: 2026-10-29
build-state: READY-TO-BUILD — A–F APPROVED 2026-10-29 (R-3067), after CE-1436; §5 the DER inspector — its own decision (lean awaits the user)
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

## 3. INVENTORY — what ExCon carries, and where each already is (📐 MEASURED `2026-10-29`)

📐 Graph: `search_graph name_pattern=".*ExCon.*" label=Class` → 34 production classes; `.*Orbat.*(Panel|Window|Adapter|Seam|Source)$` and
`.*Mission(Editor|Plan.*Editor|Authoring|Window|Panel).*` for rows 3; grep for every user of each shared class (production, tests excluded).
**138** production `.cs` files outside the project mention `ExCon` (230 with tests). ⚠ `check_index_coverage` is unavailable through the CLI.

| ExCon capability | ExCon's classes | 📐 measured: already elsewhere? | if retired |
|---|---|---|---|
| exercise control (pause / resume / step / time scale) | ExConLogic (reads the cluster's time via `ClusterTimeObservation`) | ✅ the shared `ShellTimeControlToolbar` (registered by CGF and the editor) and the Orchestrator-owned **Cluster Control** window — whose id is still `"excon_cluster_control"` (`ClusterControlWindow.cs:17`) | nothing to move; rename the window id |
| cluster monitoring, 2PC history | `ClusterUiCache` on ExCon's observer bus | ✅ the Orchestrator's `ClusterControlWindow` / `DiagnosticsWindow` read the master's own `ClusterUiCache` | stays on the master host |
| ORBAT | its own `OrbatPanel` + `ExConOrbatAdapter` | ✅ `SharedOrbatPanel` (Presentation) via `ScenarioOrbatAdapter`, on CGF + editor; plus `EditorOrbatPanel` | ⚠ ExCon's OrbatPanel is the private fork (UXI-25) — deleted, not moved |
| **mission-plan editing** | `ExConMissionWindow` → the SHARED `MissionPanel` (Presentation) + ExCon's own `MissionEditorService` | ✅ **NOT unique**: `MissionPanel` / `MissionPanelWindow` already run on CGF and the editor, committing through `Hrot.UI.Common.Facades.IMissionEditorService` (the editor's Mission panel, `ScenarioMissionView`, `/missions` HTTP) | 🔴 found: ExCon carries a SECOND `IMissionEditorService` (`Hrot.ExCon.Services`) beside the shared facade — a duplicate that goes with it |
| entity placement | `ExConSpawnerWindow` → the SHARED `SpawnerPanel` | ✅ `SpawnerPanel` on CGF + editor | nothing to move |
| entity inspector | `ExConDerEntityInspectorWindow` → `DerEntityInspectorPanel` (Fdp.Presentation, reads the world-less DER mirror) | ⚠ used ONLY by ExCon (`ExConMock`); ECS hosts inspect their world (`EntityInspectorPanel`, the editor's inspector) | the DER inspector has no other user ⇒ retire it with ExCon (measure: any other DER consumer before deleting the DER panel itself) |
| IG context menus | `ContextMenuLogic`, `JsonContextMenuBuilder` → `ContextActionsUpdate` → IG `ContextMenuSystem` | ✅ the shared `MapEntityMenuHandler` runs on CGF, editor, IG (`IgApplication`), SimHost visualization and the shared ORBAT (R-1019) | delete the remote path (ExCon + IG halves + `ContextActionsUpdateTranslator`) |
| remote 2-D map driving of IG | `MapCommandRequest/Ack` via `NedExConEgressWriters` → IG `MapCommandController`, `NedIgNetworkAdapter`; `MiniExConPanel` on IG | — the role is gone (§1 ③) | delete both halves + the map-command topics (wire values reserved, R-42) |
| event log / data monitor, ExCon diagnostics | `InteractionPanel`, `DiagnosticsPanel` | ✅ ddsmonitor; `/diagnostics/architecture`; the Orchestrator's DiagnosticsWindow | delete |
| scenario save / load slice | `ExConScenarioSaveHandler` / `LoadHandler` (`ExCon.Observer` foreign doc) | — | 📐 **0** scenarios / recipes carry an `ExCon.Observer` slice (`grep -rl` over `scenarios/`, `recipes/`, the old recipes) ⇒ nothing to migrate |
| ⭐ the CONTRACTS (§1 ②) — JSON entity-state requests, high-level creation | ExCon's egress writers | ✅ **they survive without ExCon**: `UpdateEntityAttributeRequest` is produced by the HTTP surface (`DebugApiService`) and `NedCommandGateway`, applied by `UpdateEntityAttributeRequestSystem`; `EntityCreationRequest` appears in 36 production files outside ExCon | keep the contracts; retire only the node |

⭐ **Result: no capability in this table is unique to ExCon.** The only things that exist nowhere else are the world-less mirrors of things
the ECS hosts have (the DER inspector, its ORBAT fork, its second mission-service interface, its observer read model) and the remote-map path
whose purpose is gone.

## 4. Sub-questions — ⏳ leans

| # | question | ⭐ lean | what would change it |
|---|---|---|---|
| **A** | retire ExCon completely? | ⭐ **yes** — its load-bearing reason (③) is gone, ① is proven, ② lives in the contracts, and §2 lists the duplication it keeps alive | a capability in §3 with no home after measurement, or a deployment that needs a world-less console (e.g. a thin client) |
| **B** | where does mission-plan editing go? | ⭐ nowhere new — §3 measured it already runs on the shared `MissionPanel` (CGF, editor); delete ExCon's second `IMissionEditorService` | — |
| **C** | what happens to the IG remote-map machinery? | ⭐ delete it with ExCon (`MapCommandController`, `ContextMenuSystem`'s ExCon arm, `MiniExConPanel`, the three map-command topics — a wire change, retired values kept reserved per R-42) | a Player-node use for any of it (Q91) |
| **D** | what replaces the "exercise director's console"? | ⭐ the master host (the Orchestrator's windows + the shared map/ORBAT/time control) — or the editor — no new host | the user wanting a separate operator station ⇒ then an ECS-node role on the shared frame, never a world-less fork |
| **E** | `ClusterView` V2 (monitoring topics observer-only) | ⭐ with ExCon gone, only the master keeps monitoring, and it reads its own in-process bus ⇒ the observer translator retires too; V2's difference disappears | — |
| **F** | order | ⭐ after CE-1436 lands (it moves ExCon onto the shared ingress first, so the retirement is a deletion, not a rewrite) and before CE-3340's rails (so the rails do not pin ExCon's copies) | — |

⏳ **Next step:** the user decides A–F (§3 is measured). Development stays paused (R-3066); the retirement itself would be one task after CE-1436.

## 5. ✅ Approved (`2026-10-29`, R-3067) — and the one thing kept: the DER entity inspector

🔒 User: *"approved, retire ExCon after ClusterView. Keep just the der entity inspector panel, i guess it is generic and might go to global
diagnostic windows, pls check it and evaluate it."*

### 5a. 📐 The DER entity inspector, measured

| question | 📐 measured | file |
|---|---|---|
| is the PANEL generic? | ✅ yes — `DerEntityInspectorPanel` renders any `IDerRepo`: an entity list + a live tree of each entity's raw network DESCRIPTORS as received (`GetAllRawDescriptors`); it lives in `Fdp.Presentation`, not in ExCon | `FDP/Engine/Fdp.Presentation/ImGui/Panels/DerEntityInspectorPanel.cs:38-68` |
| is its DATA generic? | ⛔ **no** — `IDerRepo` is documented as *"Non-ECS entity repository for ExCon Mock"*, and the only thing that FILLS one is ExCon's own ingress (`NedExConIngressTranslators`, created by `INetworkFactory.CreateExConIngressHandlers`) | `FDP/Toolkits/Fdp.Toolkits/DER/IDerRepo.cs` header; `Hrot/Network/Hrot.Network.NED/ExCon/NedExConIngressTranslators.cs:248-346` |
| does any other host have a DER? | ⚠ the editor declares one (`EditorApplication._view`, `:38`) but **nothing writes it** (grep for writes: none) — so `EditorOrbatPanel` (constructed `EditorSubsystem.cs:2865`) reads an always-empty repository, and `EntityPropertyInspector` is never constructed: 🔴 **dead code found**, to delete with ExCon | `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs:38,62` |
| where would it go? | the shared global diagnostics windows — `DiagnosticsWindowsBundle` (Presentation), attached by IG, CGF, SimHost, the Replay Browser and the editor | `Hrot/Engine/Hrot.Presentation/Windows/DiagnosticsWindowsBundle.cs` |
| what does it show that nothing else does? | the entity **as received on the wire** — raw descriptors per entity, live, before any translator turns them into components — including from a non-HROT sender (a DIS gateway). The ECS inspector shows components AFTER translation; ddsmonitor records samples to a file, not a live per-entity view | — |

### 5b. ⏳ Lean

⭐ **Keep it as a global diagnostic — "Wire Entity Inspector" — with its OWN data source, opt-in:** move the DER descriptor ingress out of ExCon into
the shared diagnostics (one passive subscriber to the descriptor topics, filling a `DerRepo`), created **only while the window is open** (a mirror
of every descriptor on every node otherwise costs memory and DDS readers for nothing), and add the window to `DiagnosticsWindowsBundle` so every
host offers it by construction. Rename nothing it does not need: `DerRepo` stays the one non-ECS mirror; its header stops saying "ExCon".

| rejected | why |
|---|---|
| point the panel at the ECS world instead | it would duplicate the existing ECS `EntityInspectorPanel` — and lose the one thing it is for (the wire, before translation) |
| keep a DER mirror always on every node | a second entity repository running all the time (the duplication R-3066 removes), for a debugging view |
| drop it | loses the only live "what did the wire say about entity X" view (the user asked to keep it) |

⏳ What would change the lean: if ddsmonitor gains a live per-entity view, the panel becomes redundant.
