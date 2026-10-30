<!--STATUS
state: LIVE — OPEN (backend, 2026-10-29): retire the ExCon node completely, unless a good reason to keep it is found
updated: 2026-10-29
build-state: READY-TO-BUILD — A–F APPROVED 2026-10-29 (R-3067), after CE-1436; §5b the generic wire inspector is part of the same task
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

### 5b. ✅ Decided (`2026-10-29`, user: *"yes, record it to be done with the ExCon retirement"*) — ONE generic, automated wire inspector

📐 **Measured first:** today the DER is filled by hand-written per-descriptor bridging handlers — `NedEntityInfoBridgingHandler`,
`NedEntityMissionBridgingHandler`, `NedMapOverlayBridgingHandler` (`Hrot/Network/Hrot.Network.NED/ExCon/NedExConIngressTranslators.cs:245-380`) —
**3 descriptors of 125 wire types**. ⛔ In that form it is not worth keeping. ⭐ But every piece of an automated version exists:

| piece | where it already is |
|---|---|
| the list of every wire type, generated (nothing hand-maintained) | `CycloneDDS.IdlMap.g.cs` — one `[assembly: DdsIdlMapping(type, …)]` per type, emitted at build |
| which types are per-entity | the protocol's rule: **83 of 125** are keyed by the entity id (`EntityId` first; multi-instance: parent id + instance id) — `docs/reference/NED_DDS_Protocol.md` keyed-topic table; the same rule `scripts/ned-wire-inventory.py` applies |
| a store for raw descriptors per entity | `DerRepo` / `IDerEntity.SetDescriptor` (boxed) |
| a renderer for ANY boxed descriptor | `DerEntityInspectorPanel` → `ImGuiPropertyTree.Render` (public fields, no per-type code) |

**The build (part of the ExCon retirement task):**
1. ⛔ **Gate first:** prove a `DdsReader<T>` can be built reflectively for every keyed generated type (ddsmonitor reads any topic from the same DLLs, which suggests yes — unverified here). If it cannot, STOP: the inspector is dropped instead.
2. ONE `WireEntityMirror` (shared diagnostics, not ExCon): enumerates the mapped types, keeps the entity-keyed ones, opens a typed reader per type by reflection, and puts each sample into a `DerRepo` under its entity id (creating the entity on its first descriptor). ⛔ No per-descriptor code — a new descriptor appears with zero changes.
3. **Opt-in:** the mirror and its ~83 readers exist ONLY while the window is open (created on open, disposed on close).
4. The window — "Wire Entity Inspector" — joins `DiagnosticsWindowsBundle`, so every host that attaches the global diagnostics offers it by construction.
5. Deleted with ExCon: the three bridging handlers, `CreateExConIngressHandlers`, the editor's never-written DER (`EditorApplication._view`) and its dead readers (`EditorOrbatPanel`, `EntityPropertyInspector`); `IDerRepo`'s header stops saying "ExCon".
6. Rails: every entity-keyed mapped type is mirrored (count matches the protocol's keyed set); a sample of an unseen type appears without code; nothing is read while the window is closed.

| rejected | why |
|---|---|
| keep the hand-written bridging handlers | per-descriptor code that covers 3 of 125 types — the opposite of unified |
| point the panel at the ECS world | duplicates the ECS `EntityInspectorPanel` and loses the wire view (before translation) |
| a DER mirror always on | a second entity repository on every node, all the time, for a debugging view |

## 6. The build plan — slices (backend, `2026-10-29`; after CE-1446 B1–B3 per the one-worker rule)

📐 **Measured for the plan:** one production `.csproj` references `Hrot.ExCon` (`Hrot.ClusterRunner`); `HROT.sln` carries `Hrot.ExCon` +
`Hrot.ExCon.Tests`; **22** test files name `excon` in a harness node list (`HrotRunnerHarness("simhost,ig,excon,cgf", …)`) and use it for nothing
else; **11** test files drive ExCon itself (`harness.ExCon.Logic`, `TestHook_ClusterSlave`, `new ExConSubsystem`). The DER/inspector items are §5a/§5b.

| slice | what | proves it |
|---|---|---|
| **X1** the tests off ExCon | `HrotRunnerHarness` loses the ExCon node (its `excon` token REJECTED, so no list keeps it silently); the 22 node lists drop it; each ExCon-DRIVING test is re-homed or deleted per the rule below; `SimTimeSyncIntegrationTests` drops the ExCon column; `CheckpointClusterTests` drops its debug provider; `PlayerJoinsARunningScenarioTests` reads the cluster level from CGF's `ClusterView` | the touched classes green |
| **X2** the node goes | delete `Hrot.ExCon`, `Hrot.ExCon.Tests`, the ClusterRunner `excon` mode/registration and its `ClusterRunner.Tests` classes (`ExConSubsystemTests`, `ExConSubsystemClusterTests`, `ExConHandlerRegistrationTests`, the ExCon arms of `HexagonalBoundaryTests` / `SubsystemOrchestratorTests`), the sln entries; the Cluster Control window id `excon_cluster_control` renamed (§3 row 1); `ClusterConformanceRails`' ExCon panel names | the full solution builds; ClusterRunner.Tests green |
| **X3** IG's remote-map half | `MapCommandController`, `ContextMenuSystem`'s ExCon arm, `MiniExConPanel`, `ContextActionsUpdateTranslator`, `NedExConEgressWriters`; the `MapCommandRequest` / `MapCommandAck` / `ContextActionsUpdate` topics retired with their wire values RESERVED (R-42) | IG.Tests green; the wire inventory script agrees |
| **X4** the world-less mirrors | the observer-only cluster translator (ClusterView V2's difference, §4 E); ExCon's second `IMissionEditorService`; the editor's never-written DER (`EditorApplication._view`, `EditorOrbatPanel`, `EntityPropertyInspector`); `CreateExConIngressHandlers` + the three bridging handlers | the touched classes green |
| **X5** the generic wire inspector | §5b steps 1–6 — ⛔ step 1's gate first (a reflective `DdsReader<T>` for every keyed generated type); if it fails, the inspector is dropped and §5b says so | §5b step 6's rails |

⭐ **The re-home rule for an ExCon-driving test:** name the capability it proves. Shared surface (mission commit through `IMissionEditorService`,
entity selection, placement through `SpawnerPanel`) ⇒ drive the same flow from CGF / the editor, in the feature's own suite (T-1), never a new
class. ExCon-only capability (the remote IG map: `MapPlacementIntegrationTests`' "ExCon starts placement mode → IG activates CreationTool") ⇒
deleted with the capability, the deletion named in the report. ⛔ No test keeps an ExCon stand-in.

### 6.1 ✅ AS-BUILT `2026-10-30` (backend) — slice X1, the tests off ExCon

`HrotRunnerHarness` has no ExCon node; a node list naming `excon` throws `ArgumentException` citing R-3067. 16 test files dropped the token; the rest per the table.

| test | capability it proved | outcome |
|---|---|---|
| `PlayerJoinsARunningScenarioTests` | the running cluster's PlayReady, seen by another node | re-homed: reads CGF's `NodeForTest.ClusterSlave.View` |
| `SimTimeSyncIntegrationTests` | sim-time agreement | ExCon column dropped (Orchestrator / SimHost / IG remain) |
| `CheckpointClusterTests` (run-not-recorded) | every node learns it | ExCon debug provider dropped from the node loop |
| `AllSubsystemsClusterTransitionTests` (2) | a slave's local state follows the master | ExCon removed from the node set; the slave under test is the IG's |
| `ClusterOpE2eScriptTests`, `UrbanCombatFileLifecycleTests`, `TimeControlIntegrationTests` | — | only a dead `using Hrot.ExCon` removed |
| `AreaAuthoringIntegrationTests` | area drawn on the IG → create request → ack → overlay → polyline on SimHost + IG | kept; the tool is activated on the IG by `TestHook_ParseCommandAndActivateAreaTool` (as `AclBackdoorEliminationTests`), the ExCon-only "config sent by ExCon" half deleted. ⚠ X3 deletes `MapCommandController`: this activation must be re-pointed then |
| `MissionControlIntegrationTests` | `MissionControlRequest` on DDS applied by the Brain, jump moves the plan, ack returns | kept; ExCon's request-transaction tracking (ExCon-only) deleted |
| `MapPlacementIntegrationTests` (2) | ExCon starts placement mode → IG activates `CreationTool` → click → spawn; DER mirror receives it | **deleted** (remote IG map, ExCon-only); the create-request → SimHost/IG distribution is covered by the gateway spawn suites |
| `SelectionAndMissionIntegrationTests` · selection | IG selection published to ExCon's read model | **deleted** (ExCon was the only DDS consumer of `SelectionChangedEvent`) |
| `SelectionAndMissionIntegrationTests` · mission commit | commit through an `IMissionEditorService` resolves with the ack | ✅ **re-homed** to `MissionControlIntegrationTests.Cgf_CommitMissionAsync_ResolvesWithAck_NotTimeout`, driven through `harness.Cgf.CreateDebugProvider().MissionEditor` (the shared service). It first stopped on a production gap: `CgfSubsystem` built `ScenarioMissionService` only in the `!_headless` block, so a headless CGF's debug provider (the HTTP/MCP mission seam) had none. Fixed: built once outside the gate (`CgfSubsystem.cs` ~:1013); `PollAcks()` already ran every frame; the panel wiring stays window-only. Red-proved by putting the construction back behind `!_headless` |
| `DistributedScenarioSaveTests` (2) *(not on the plan's list)* | the save routes ExCon's `ExCon.Observer` slice to `foreign/` | foreign assertions removed (no remaining node writes a foreign slice); canonical merge and the saved-file hash/stamp stay |

### 6.2 ✅ AS-BUILT `2026-10-30` (backend) — slice X2, the node goes

Deleted `Hrot/Subsystems/Hrot.ExCon` and `Hrot.ExCon.Tests` (+ their `HROT.sln` entries, the `Hrot.ClusterRunner` project reference, the `InternalsVisibleTo("Hrot.ExCon.Tests")` on `Hrot.Presentation`). The runner finds its subsystems by reflection, so there was no registration to delete; the `excon` mode and its legacy `ios` alias (and `ios` as a `--wait-for` peer) are now an **Invalid mode** (thrown, never silently dropped) — rail `RunnerConfigurationTests.ParseMode_ExCon_IsRejectedAsInvalid`, red-proved. Also: `Program.cs` (`["ExCon"]` perspective map, `EXCON => 200` node-id offset), `launchSettings.json`, `run_All.sh`, `scripts/composition-inventory.py`.

| file | what was done |
|---|---|
| `ClusterRunner.Tests`: `ExConSubsystemTests`, `ExConSubsystemClusterTests`, `ExConHandlerRegistrationTests` | deleted (they tested ExCon itself) |
| `HexagonalBoundaryTests` | T001 keeps the Orchestrator arm; T004 (`PauseIntent_ReachesSlaveTranslatorTick`, ExCon's own pipeline) deleted with its spy translator/factory |
| `SubsystemOrchestratorTests` | the TitleBarColor-distinct rail uses CGF as the third concrete (was ExCon); `MockSubsystem("ExCon")` name-only mocks kept |
| `RunModeTests`, `RunnerConfigurationTests`, `RunnerIntegrationTests`, `TheLayoutIsOneUnitTests` | `excon` expectations dropped (`all` = 5 nodes); `ios` / `editor,excon` become one rejection theory; wait-for `ios` rejected |
| `Orchestrator/ClusterControlWindow` | id `excon_cluster_control` → `cluster_control`; `layout/default/imgui.ini`, `fdp_windows.json`, both `registered-windows.json` goldens updated to match (the `excon_*` windows leave the `all` golden; `ig_mini_excon` stays for X3) |
| `SystemTests`: `ClusterConformanceRails` | removed the four `excon-*` cluster-only kinds **and `ExConObserver`** (also an ExCon-registered kind), the `"ExCon"` routable perspective, the ExCon honest-FALSE cells; `ig-mini-excon` stays (X3) |
| `SystemTests`: `ModeStartupRails` | `all` banner without `excon`; the `excon` mode row dropped |
| `SimHost.Tests`: `OneBootstrapImplementationRails`, `TheDebugProvidersDoNotUnderReportTests` | the ExCon host row / debt edge / provider path removed |

⚠ Left for X3/X4, named: `ScenarioPanelWindows.ExConReports` (a now-unused id), the `Create*ExCon*` members of `INetworkFactory` and their test doubles, `MiniExConIntegrationTests`, `tools/ai-debug-mcp` skill text that still mentions the ExCon perspective.
