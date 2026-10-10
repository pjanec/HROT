<!--STATUS
state: LIVE
build-state: DESIGN — requirements (§1) and user rulings (§1a) recorded; transport shaped by the user (full NED, D3);
  open: Q6 first host, Q8 ReplayBrowser, and two measurements D3 names (authority bits on offline hosts). Nothing built.
updated: 2026-10-10 (rev 4 — U10: §3.4 the reuse ledger, D12 the viewer role. Rev 3 — U7-U9: the camera is an ordinary spatial entity; the gateway is a hot-installable module in
  Editor/ReplayBrowser instantiating the same NED translators; no dedicated DDS partition. Rev 2: full NED.)
current-answer: §1 requirements · §1a the user's rulings · §3.4 THE REUSE LEDGER · §5 D1a why not in-process · §3 what exists, incl. what NED already carries (§3.3) ·
  §4 the architecture (module / class / sequence diagrams) · §5 decisions · §6 open questions · §7 stages.
stale-below: the HISTORY heading at the end — rev 1's loopback socket + host-side producer, rev 2's ViewCamera descriptor
  and Editor-wide NED mode.
  Do NOT quote them.
known-rot: none.
known-conflict: none. ⚠ This is NOT a Stride replacement — §3.1 (Stride is a simulating node). The user ruled Stride
  stays untouched (§1a).
related-designs:
  - DESIGN_Stride_Node_Modes.md — owns the Stride story (a 3-D SHELL around a simulating node). This viewer EXTRACTS its §8
    3-D gizmo triage and §9 locomotion blend into shared code and leaves Stride's roles and code paths otherwise untouched.
  - designs/gizmos-1/DESIGN.md — owns "Evaluate Once, Present Anywhere": the dumb-terminal gizmo stream, its DDS topics
    (§6), §10 context-menu bindings and the MenuAction return trip. The viewer is one more dumb terminal.
  - UX/UX_Feature_Selection.md — owns UXI-11, the one selection store, its request/notification protocol and the
    SelectionEgressSystem the viewer listens to (§2.7.17).
  - DESIGN_Terrain_World.md — owns TerrainWorld (local metres, X east / Y north / Z up) — what the 3-D scene is built from.
  - DESIGN_Geo_Origin.md — owns the terrain's geo origin; the viewer needs it because NED positions are lat/lon.
  - DESIGN_Remote_Map_Control.md — owns remote commands into a host and R-134 (the DDS type stops at the translator).
  - DESIGN_Gizmo_Anchor_Identity.md — owns network-id identity of gizmo anchors; via Architect_Question_73 (CE-463),
    "an external viewer is an external view on one node, and selection spreads both ways".
  - DESIGN_Dead_Reckoning.md — owns remote-entity smoothing on every node; the viewer's passive node inherits it.
  - DESIGN_Node_Roles_And_Policies.md — owns roles and persistence (R-140, IG is a passive non-persisting node — the
    viewer node's model).
  - blueprints/Architect_Question_86_Editor_Runs_The_Orchestrator_Core.md — owns the editor as a one-node cluster with an
    orchestrator core and a slave-less MasterSyncController; under D3 the viewer becomes that cluster's second node.
-->

# DESIGN — a Godot 3-D viewer for HROT

## Headline

⭐ **A separate Godot 4.7.1 (.NET) process whose ONLY input is a GATEWAY.** The gateway's lean form (D3) is **a passive
HROT node inside the Godot process** — IG-shaped: NED ingress, dead reckoning, terrain residency — plus a projection of
that node's world into a render model. Godot code reads the render model and nothing else. Later the **SumoSharp**
stream enters the **same gateway** (stage 3). The host menu toggle starts and stops the process.

| | |
|---|---|
| what it renders | terrain (built from `TerrainWorld`), entities (models + mannequin animation; boxes first), 3-D gizmos, fire/detonation effects, selection cubes, context menus |
| what it never does | physics, simulation, scenario load/save, deciding what a menu action means (the host does) |
| what it reuses | NED replication + ingress translators + DR, the gizmo DDS topics, the selection egress, the owner-routed update request, `TerrainWorld`, SumoSharp City3D's split and cloud tests |
| what is new | the gateway's projection, a hot-installable viewer gateway module for the two offline hosts (Editor, ReplayBrowser), the Godot app |

---

## 1. REQUIREMENTS — user, `2026-10-10` (recorded as given; ids are for discussion)

| id | requirement | stage |
|---|---|---|
| **V-01** | A 3-D viewer startable from the **Editor, CGF, SimHost or ReplayBrowser** as a **menu toggle** | 1 |
| **V-02** | Builds its 3-D scene from **our terrain automatically**; may **cache** the built scene on disk if building is slow | 1 |
| **V-03** | **Animated characters** — the open-source Godot **mannequin**; optionally soldier (rifle) / civilian (no rifle) — not mandatory | 1 |
| **V-04** | Ships **open-source 3-D models**: personal car, APC, tank, helicopter, fighter jet | 1 |
| **V-05** | **Free camera** keyboard/mouse controls **as in the Unity editor** | 1 |
| **V-06** | HROT provides **entity state every frame** (position, speed, stance, … whatever is needed) | 1 |
| **V-07** | HROT provides the **gizmo stream**, rendered in 3-D as debug drawing | 1 |
| **V-08** | HROT provides **events — fires, detonations** — rendered as simple particle effects | 1 |
| **V-09** | Godot runs **in its own thread** with **its own 3-D window**, closable **without closing the host** | 1 |
| **V-10** | **Click to select** entities — selection **shared with HROT** — with a **wireframe-cube** indicator | 1 |
| **V-11** | **Entity context menu** in the 3-D window, **from the gizmo data**, same as the HROT UIs | 1 |
| **V-12** | Camera starts at a **tilted top view (~30° below horizontal) framing the whole terrain**, unless an HROT **camera entity** carries a location; **saveable to the scenario** | 1 |
| **V-13** | The **camera entity** is created and **owned by the host that created the Godot instance** | 1 |
| **V-14** | The camera entity **follows Godot's camera** when moved in Godot — through a gateway sending **state-update-request FDP bus events** | 1 |
| **V-15** | If the host moves the camera entity, **Godot follows** | 1 |
| **V-16** | Coordinates: HROT's **internal local frame** (never geodetic), adapted only to **Godot's axis/rotation conventions** | 1 |
| **V-17** | Godot is a **viewer only** — no physics, no simulation | 1 |
| **V-18** | A **gateway** feeding the viewer **either in-process from a host or from the network** (Godot as a separate app on **NED DDS**) | arch 1 · build 2 |
| **V-19** | Later: the **SumoSharp** viewer's features — SUMO road net and cars over the network, rendered **together with HROT data** | 3 |
| **V-20** | **Lightweight**, **developable by Claude in the Linux cloud** (Xvfb for tests) | all |

### 1a. The user's rulings on rev 1 — `2026-10-10`, verbatim

| # | 🔒 user | consequence |
|---|---|---|
| **U1** | *"Stride untouched for now."* | §6 Q1 closed — keep, no Stride work beyond the two extractions D9/D10 already proposed |
| **U2** | *"How to pass data if not same process? Special non-ned dds topic? Or full NED perhaps - good test for NED?"* | ⭐ D3 rewritten: **full NED** is now the lean (§5 D3). The question assumes a separate process ⇒ D1 is treated as accepted |
| **U3** | *"Ad 3: ok. Ad 4: ok. Ad 5: ok. Ad 6: ok."* | camera entity (one per launching host, created on first move, none in ReplayBrowser) · `Viewer3D/` layout · Godot 4.7.1 · Editor first — ⚠ **but under full NED the Editor is the most expensive first host; §6 Q6 asks again** |
| **U4** | *"the gateway should be the only input to godot, the network stream from sumosharp should go through it"* | ⭐ the gateway is the single input; SumoSharp is a gateway input, never a second path into Godot (§4.1) |
| **U5** | *"3d models could be authored from simple boxes as the first iteration (tank and its turret each one box etc) if easy and nothing better — usable for slice 0"* | D10: **box models first**, articulated where it matters (hull + turret), used from S0 |
| **U6** | *"Menu actions irrelevant — controlled by host, sent back to host, not 3d viewer's business so much."* | D8: the viewer shows the menu and returns the chosen id — nothing more. Rev 1's notes on handler/id defects are dropped from this design |
| **U7** | *"Camera can use usual world position component as any other spatial entity."* | D7: **no new component, no new descriptor** — the camera is an ordinary spatial entity of a camera TKB type; its pose replicates through `WorldPos` like any other entity |
| **U8** | *"What is the issue with in process solution?"* | answered in §5 **D1a** |
| **U9** | *"the gateway sitting as a pluggable dynamically enablable module in the editor and replaybrowser will need to instantiate the same translators as the networked hosts. Already networked host will need no special care … No dedicated dds partition required"* | D3 / Q8: the offline hosts get a **hot-installed viewer gateway module** (kernel `InstallModuleAsync`) instantiating the **same NED translators**; networked hosts change nothing; the normal domain |
| **U10** | *"We need to use code as much as possible, unification over duplication. What can be reused as is, including the bootstrap code? What needs to be new?"* | ⭐ §3.4 — the reuse ledger, measured; D12 — the role |

---

## 2. INVENTORY — measured `2026-10-10`, branch `ui`

⚠ Graph via the MCP (`home-user-HROT`); every total corroborated with grep. `check_index_coverage` not run — no total
below is a proof of completeness.

| query | total | what it settled |
|---|---|---|
| `search_graph(name_pattern=".*Stride.*", label="Class")` | **71** | Stride is a simulation node + 3-D shell; no renderer-neutral entity seam (§3.1) |
| `search_graph(name_pattern=".*(Gizmo\|Selection\|ContextMenu\|Camera).*", label="Interface")` | **28** | `IGizmoTransport`, `IGizmoSource`, `IGizmoDrawBuilder`, `ISelectionState`, `IMapCameraProvider` (2-D only) |
| `search_graph(qn_pattern=".*GizmoMap\.Contracts.*")` | **490** | the BCL-only primitive contract incl. `ContextMenuBinding`, `ContextMenuItemDto`, `SpatialAnchor` |
| `search_graph(name_pattern=".*(Camera\|Viewpoint).*", label="Class")` + grep for camera structs | **18 / 0** | ⛔ **no camera entity or component exists** |
| `search_graph(name_pattern=".*Selection(Request\|Changed\|…)\w*")` | **179** | one store: `SelectionRequestSystem`, `EcsSelectionState`, `SelectionChangedNotification`, `SelectionEgressSystem` |
| `search_graph(name_pattern=".*NetworkFactory$")` + grep `new …NetworkFactory` | **53 / 4** | ⛔ the **Editor is hard-wired offline** (`EditorSubsystem.cs:219` → `OfflineNetworkFactory` → `NullReplicationModule`); CGF/SimHost get NED or BDC from `ClusterRunner/Program.cs:227-228`; ReplayBrowser **discards** its factory (`ReplayBrowserSubsystem.cs:140`) |
| `search_graph(name_pattern=".*(Fire\|Detonat\|Shot\|…)…(Event\|Record\|…)$")` | **47** | local bus `WeaponFireNotification`/`DetonationNotification`; NED `WeaponFire`/`MunitionDetonation` |
| `search_code("Godot")` | **3** | passing mentions only |
| SumoSharp `demos/City3D` (read 2026-10-10) | — | Godot 4.7.1 .NET, `CityLib` + `Viewer`, `IReplicationSource` swap, `fetch-godot.sh`, Xvfb screenshot; DDS used only in its remote mode |

---

## 3. WHAT ALREADY EXISTS

### 3.1 ⛔ Stride is not a viewer

📐 `StrideNodeBootstrapper` provides **`MuscleGround | Perception | NavigationSolver`**
(`Hrot/Subsystems/Hrot.NodeComposition/StrideNodeBootstrapper.cs:17-21`); `DESIGN_Stride_Node_Modes.md`: *"Stride is
not a new node type. It is a SHELL — a 3-D window and a physics/animation backend"*. It has **no terrain** (flat 20 km box,
`StrideHrotGame.cs:1009`), **no 3-D context menu**, **no effects**, **no camera entity**. Ruled untouched (U1).

### 3.2 Reused building blocks

| need | already exists | file | use |
|---|---|---|---|
| 3-D triage of gizmo shapes | `DebugPrimitiveRenderer3D` → `IDebugDrawSink3D` | `Stride/Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs:219-273` | ⭐ **extract** — Stride appears only as 14 math value types |
| locomotion blend | `LocomotionBlend` (pure `System`) | `Stride/Hrot.Stride.Animation/LocomotionBlend.cs` | ⭐ **extract** |
| per-engine model mapping | TKB `Stride.RenderModelDef` — *"a future 3D engine gets its own descriptor"* | `Fdp.Toolkits/Tkb/Domain/StrideRenderModelDefDto.cs:39-43` | add **`Godot.RenderModelDef`** |
| terrain | `TerrainWorld` (prisms, panels w/ openings, slabs/ramps, surfaces, doors); `TerrainResidency` loads it by name on every node | `Fdp.Toolkits/Terrain/TerrainWorld.cs:83`, `Hrot.Core/Services/TerrainResidency.cs:37` | the viewer node loads it like any node; the gateway meshes it |
| a passive node to copy | IG — passive, non-persisting (R-140), NED ingress, DR, 2-D map | `IgNodeBootstrapper.cs` | the viewer node's shape |
| main-menu toggle | `GlobalMenuRegistry.RegisterCheckableItem` | `Fdp.Presentation/ImGui/WindowManager/WindowManager.cs:394` | `View ▸ 3D Viewer` |
| Godot split + cloud tests | `CityLib`/`Viewer`, `fetch-godot.sh`, `--headless` smoke, Xvfb screenshot | `pjanec/SumoSharp demos/City3D` | the same |

### 3.3 ⭐ What NED / DDS already carries — the case for full NED

| the viewer needs | wire form today | file | gap |
|---|---|---|---|
| entity exists / type / side / name | NED `EntityMaster` (TkbType, DisType), `EntityInfo` | `Hrot.Network.NED/GenericDescriptors.cs:92,150` | — |
| pose + velocity | NED `WorldPos` (lat/lon/alt, HPR, velocity) | `SimDescriptors.cs:14` | lat/lon ⇒ needs the terrain's geo origin (loaded with the terrain) |
| health | NED `EntityDamage` | `SimDescriptors.cs:51` | — |
| stance | `hrot/anim/StanceStatus` on real DDS | `Hrot.Animation.Replication/AnimationDdsMessages.cs:79` | not ingested by IG today (CE-2121 "IG ingress open") — the viewer node composes it |
| fire / detonation | NED `WeaponFire`, `MunitionDetonation` | `FireInteractionMessages.cs` | — |
| gizmos | `DebugPrimitivesBatch` (keyed FrameNumber + NodeId) — CGF, SimHost, IG publish | `GizmoMap.Network/Topics/DebugPrimitivesBatch.cs` | the Editor publishes none (offline) |
| menu JSON behind a binding | `StringInternEntry` + `DdsStringInternPublisher` | `GizmoMap.Network/Transport/DdsStringInternPublisher.cs` | ⛔ **no production host constructs the publisher** (only `GizmoMap.Example`) ⇒ menus cannot resolve remotely until wired |
| pick / menu action / ground point back | `GizmoInteractionBatch` (Started, MenuAction + ActionId, WorldPos) | gizmos-1 §5, §10.7 | — |
| the host's selection set | NED `SelectionChangedEvent{MapId, ids}` from the shared `SelectionEgressSystem` | `MapMessages.cs:106`; `Hrot.Presentation/.../SelectionEgressSystem.cs:60` | wired on IG only (`IgApplication.cs:929`) |
| camera moved in Godot → owner | NED `UpdateEntityAttributeRequest` (owner-routed) | `GenericMessages.cs:261-264` | — |
| camera entity state | NED `WorldPos` — an ordinary spatial entity (U7) | `SimDescriptors.cs:14` | — (a camera TKB type only) |
| terrain name | cluster load phase (`NodeTransitionPayloadDto.TerrainName` on `NodeOpCommand`) | `OrchestrationPayloadDtos.cs:146` | the viewer node joins the load phase, as IG does |

⇒ ⭐ **Of eleven needs, nine already cross the wire and two need wiring that exists in code — nothing new.** That is why
D3 leans to full NED rather than a viewer-private protocol.

---

### 3.4 ⭐⭐⭐ THE REUSE LEDGER — what is reused as-is, what moves, what is new *(U10, measured `2026-10-10`)*

**Host side**

| piece | verdict | evidence |
|---|---|---|
| CGF, SimHost — their whole NED stack | ✅ **as-is, nothing added** (U9) | built by `ClusterRunner/Program.cs:210-228` |
| `ModuleHostKernel.InstallModuleAsync` / `UninstallModuleAsync` | ✅ as-is — the toggle installs/removes the gateway module | `ModuleHostKernel.cs:1372,1461`; production use `EcsRecordReplayController.cs:132-220` |
| `NedNetworkFactory` + `CreateReplicationModule()` (entity master, geospatial, damage, fire egress live inside it) | ✅ as-is — the gateway module calls exactly what CGF calls | `CgfSubsystem.cs:832`; `INetworkFactory.cs:27` |
| `CreateGizmoTranslators` + `CreateGizmoPublisherSystem` | ✅ as-is — same calls as CGF | `CgfSubsystem.cs:1424-1436`; `INetworkFactory.cs:208,214` |
| `AnimationReplicationModule` (`StanceStatus` egress/ingress) | ✅ as-is | `Hrot.Animation.Replication/Translators/Descriptors/` |
| `SelectionEgressSystem` | ✅ as-is — wire it (today IG only) | `Hrot.Presentation/.../SelectionEgressSystem.cs:60`; `IgApplication.cs:929` |
| `DdsStringInternPublisher` | ✅ as-is — wire it (no production host does) | `GizmoMap.Network/Transport/DdsStringInternPublisher.cs` |
| the per-node DDS setup (participant + entity map + geo transform + factory) | 🔁 **extract** — it is inline in `Program.cs:210-228`; the gateway module and the viewer process both need it ⇒ one helper, three callers | `ClusterRunner/Program.cs:210-228` |
| `GlobalMenuRegistry.RegisterCheckableItem` | ✅ as-is — `View ▸ 3D Viewer` | `WindowManager.cs:394` |
| `ViewerGatewayModule` | 🆕 **new, composition only** — opens the participant via the extracted helper, installs the pieces above | — |
| the process launcher (spawn/kill Godot, pass domain + node id + the node it mirrors) | 🆕 new — no host launches a child viewer today | agent sweep: only `dotnet build` and shell-open are spawned |

**Viewer process — the passive node**

| piece | verdict | evidence |
|---|---|---|
| `SharedApplicationBootstrapper` (7-phase order; registers `NedReplicationModule` in 6a+ and time-sync in 6c itself) | ✅ **as-is** | `Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs:48-589` |
| `HrotNodeBuilder` `.WithRole/.WithNetworkFactory/.WithReplication` | ✅ as-is (IG's `BuildContext` is these four calls) | `IgNodeBootstrapper.cs:150-158` |
| `NedReplicationModule`'s **receive-only arm** — ghost creation, entity-state ingress, `DeadReckoningSyncSystem`, `driveFromNetwork` | ✅ as-is **for a `Map2D` node**; ⚠ a new role must be admitted (`NedReplicationModule.cs:243-254` throws for a role with none of Muscle/Map2D/Brain) — D12 | `NedReplicationModule.cs:46-47,243-254` |
| `NodeBootstrapper.BuildOrchestration(role, …)` — the role-driven `ClusterSlave` builder SimHost and Stride share | ✅ reuse — ⚠ its path for a receive-only role is **not yet measured** (it branches on Brain/Muscle) | `Hrot.SimHost/NodeBootstrapper.cs`; callers `SimHostNodeBootstrapper.cs:439`, `StrideNodeBootstrapper.cs:389` |
| `LoadPhaseChain` + `KnowledgeBaseLoadStep` + `TerrainLoadStep` + `TerrainResidency` (TKB, terrain, geo origin) | ✅ as-is | `IgNodeBootstrapper.cs` BuildOrchestration; R-182 (terrain is universal) |
| `HrotEnvironment.CreateTkb` + `HrotSharedComponentRegistry.RegisterAll` | ✅ as-is | `IgNodeBootstrapper.cs:198-205` |
| `EntityCreationPack` (to request the camera entity, owner = the host) | ✅ as-is | `IgNodeBootstrapper.cs:500-540`; `DESIGN_Entity_Authoring_Surface.md` §4 |
| `NodeCompositionPlan` + `CoreInfrastructureCapabilities.UnitHierarchy` | ✅ as-is | `IgNodeBootstrapper.cs:259-300` |
| `CreateGizmoTranslators` (gizmo ingress into the viewer node) | ✅ as-is | `INetworkFactory.cs:208` |
| `ViewerNodeBootstrapper : SharedApplicationBootstrapper` | 🆕 **new but thin** — the base requires one subclass per node type (3 exist); every hook body is a call into a row above | `SharedApplicationBootstrapper` phase list |
| a viewer role | ⚠ **decision D12** | `NodeRole.cs` (None, Brain, MuscleGround, Map2D, Perception, NavigationSolver) |

**Viewer process — rendering**

| piece | verdict | evidence |
|---|---|---|
| `DebugPrimitiveRenderer3D` 3-D triage | 🔁 **move** to a shared net8 assembly (Stride keeps using it) — 14 Stride math types → `System.Numerics` | `Stride/Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs` |
| `LocomotionBlend` | 🔁 **move** to the same assembly — it is pure `System` | `Stride/Hrot.Stride.Animation/LocomotionBlend.cs` |
| `TerrainWorldMesh.Build` (ground grid + prisms + slabs, Z-up soup) | ✅ as-is for **S0**; per-kind meshes with materials are new in S2 | `Fdp.Toolkits/Terrain/TerrainWorldMesh.cs:31` |
| `RenderProjection` + `RenderModel`, the HROT→Godot transform | 🆕 new (Core, unit-tested) | — |
| Godot glue — scene, free camera, picking, popup, selection cube, box models, particles, terrain cache | 🆕 new | — |
| `Godot.RenderModelDef` TKB descriptor + a camera TKB type | 🆕 new **data**, following `Stride.RenderModelDef`'s own instruction | `StrideRenderModelDefDto.cs:39-43` |

⭐ **Findings this ledger surfaced** *(recorded here, not fixed by this design)*:
- **IG hand-rolls its `ClusterSlave` handler list** (`IgNodeBootstrapper.cs:324-480`) while SimHost and Stride share
  `NodeBootstrapper.BuildOrchestration` — the drift class `Architect_Question_62` names. The viewer uses the shared one;
  migrating IG is a separate item.
- **U9 deliberately revises "the editor is networkless"** (`Architect_Question_26_Entity_Action_Model.md` constraint 2;
  `CE-516`, `Program.cs:199-204`) — only while the viewer is open, and the Editor stays the only owner of its entities.
- **Per-host translator sets stay per host** (`Architect_Question_63` §9, canon): the gateway module adds no "network
  bundle"; it composes factory methods the way each host's bootstrapper already does.

---

## 4. THE ARCHITECTURE (lean: full NED)

### 4.1 Modules — who runs what, and the dead edges

```mermaid
graph TD
  subgraph NETH["networked host (CGF / SimHost) - nothing new"]
    W1[(ECS world)]
    E1["NED translators<br/>(existing, ingress + egress)"]
    W1 <--> E1
  end
  subgraph OFFH["offline host (Editor / ReplayBrowser)"]
    W2[(ECS world)]
    GM["ViewerGatewayModule<br/>hot-installed on toggle<br/>= the same NED translators"]
    M["menu: View > 3D Viewer"]
    W2 <--> GM
    M -- "InstallModuleAsync /<br/>UninstallModuleAsync" --> GM
  end
  DDS[(NED DDS - normal domain)]
  E1 <--> DDS
  GM <--> DDS
  M -- "spawns / kills" --> GP
  subgraph GP["Godot process"]
    subgraph GW["GATEWAY = the only input"]
      VN["passive viewer node<br/>NED ingress + DR + terrain"]
      PJ["RenderProjection<br/>world -> render model"]
      SU["SumoSharp ingest (stage 3)"]
      VN --> PJ
      SU -.-> PJ
    end
    G["Godot glue<br/>nodes, meshes, camera, input, popups"]
    PJ --> G
    G -- "picks, menu ids, camera moves" --> VN
  end
  DDS <--> VN
  SUMO[(SumoSharp DDS)] -.-> SU
```

*What the picture shows that prose hid:* the **networked hosts change nothing** (U9) — they already speak NED; the two
**offline hosts get one module**, installed only while the viewer is open, that instantiates the **same translators**;
Godot code touches only the projection. Dotted = stage 3.

### 4.2 Classes — existing vs proposed

```mermaid
classDiagram
  class NedReplicationModule { <<exists>> ingress + egress }
  class EgressTranslators { <<exists>> EntityMaster GeoSpatial Damage Fire }
  class SelectionEgressSystem { <<exists>> Hrot.Presentation }
  class DdsStringInternPublisher { <<exists>> unwired in production }
  class UpdateEntityAttributeRequest { <<exists>> owner-routed }
  class DeadReckoningSyncSystem { <<exists>> every node }
  class TerrainResidency { <<exists>> load by name }
  class ModuleHostKernel { <<exists>> InstallModuleAsync }
  class Gizmo3DTriage { <<extracted>> from DebugPrimitiveRenderer3D }
  class LocomotionBlend { <<extracted>> from Hrot.Stride.Animation }
  class ViewerGatewayModule { <<new>> IEcsModule, offline hosts }
  class ViewerNodeBootstrapper { <<new>> passive, IG-shaped }
  class SharedApplicationBootstrapper { <<exists>> 7-phase base }
  class NodeBootstrapper { <<exists>> BuildOrchestration(role) }
  class HrotNodeBuilder { <<exists>> WithRole WithReplication }
  class RenderProjection {
    <<new>> Hrot.Viewer.Core
    RenderModel Project(world)
  }
  class RenderModel {
    <<new>> pure C#
    EntityView[] Entities
    long[] Selection
    Gizmo3D[] Gizmos
    MenuBinding[] Menus
    EffectEvent[] Effects
    EntityView? Camera
  }
  class GodotGlue { <<new>> Hrot.Viewer.Godot }
  ModuleHostKernel ..> ViewerGatewayModule : installs on toggle
  ViewerGatewayModule --> EgressTranslators : instantiates
  ViewerGatewayModule --> SelectionEgressSystem
  ViewerGatewayModule --> DdsStringInternPublisher
  SharedApplicationBootstrapper <|-- ViewerNodeBootstrapper
  ViewerNodeBootstrapper ..> NodeBootstrapper : orchestration
  ViewerNodeBootstrapper ..> HrotNodeBuilder : context
  ViewerNodeBootstrapper --> NedReplicationModule
  ViewerNodeBootstrapper --> DeadReckoningSyncSystem
  ViewerNodeBootstrapper --> TerrainResidency
  RenderProjection ..> Gizmo3DTriage : uses
  RenderProjection ..> LocomotionBlend : uses
  RenderProjection ..> RenderModel : builds
  GodotGlue ..> RenderModel : reads only
  GodotGlue ..> UpdateEntityAttributeRequest : camera moves
```

*What it shows:* the new runtime pieces are **one module that composes existing translators**, one bootstrapper (a copy
of IG's shape), a projection and the Godot glue. ⛔ No new component, descriptor or topic — the camera is an ordinary
spatial entity (U7).

### 4.3 Sequences

**A frame, a select, a menu action**

```mermaid
sequenceDiagram
  participant H as Host (NED translators)
  participant D as DDS
  participant V as Viewer node (gateway)
  participant P as RenderProjection
  participant G as Godot glue
  H->>D: EntityMaster / WorldPos / StanceStatus / fire events
  H->>D: DebugPrimitivesBatch + StringInternEntry (its NodeId)
  H->>D: SelectionChangedEvent (its node)
  D->>V: ingress translators -> local world, DR
  V->>P: world after tick
  P->>G: RenderModel (local frame -> Godot frame)
  G->>D: GizmoInteractionBatch Started (click on entity)
  D->>H: host's ingress -> selection request
  G->>D: GizmoInteractionBatch MenuAction (id)
  D->>H: host handles it (not the viewer's business)
```

**The camera entity, both ways (V-12..V-15) — an ordinary spatial entity**

```mermaid
sequenceDiagram
  participant H as Host world (owner)
  participant D as DDS
  participant G as Godot camera
  alt no camera entity in the world
    G->>G: frame terrain bounds at -30 deg, facing north
    G->>D: CreateEntityRequest (camera TKB type, owner = host) on first move
    D->>H: host creates and owns it (V-13)
  else camera entity replicated
    D->>G: WorldPos of the camera entity
    G->>G: adopt unless it is its own echo
  end
  G->>D: UpdateEntityAttributeRequest (pose) while flying
  D->>H: owner applies to its spatial component
  H->>D: host moves it (panel, script) -> WorldPos
  D->>G: follow
```

---

## 5. DECISIONS

### D1 · Process model — ⭐ **a separate Godot process** (accepted implicitly by U2; questioned by U8 — D1a)

A process keeps what V-09 protects — the host never blocks, Godot owns its window, closing it leaves the host running.

### D1a · Why not in-process — the answer to U8

⭐ **No architectural blocker — a tooling blocker today, and two real weaknesses.** `RenderProjection` takes a world, so
an in-process variant would reuse it unchanged; nothing here designs in-process out.

| issue | measured / sourced | weight |
|---|---|---|
| **No supported way to start Godot inside an existing .NET 8 process.** Godot's C# normally starts its **own** .NET runtime; inside the host a runtime already runs, so Godot must be driven *as a library* from it. Official LibGodot (4.6) ships C/C++ embedding; **.NET hosting is a stated goal**, not shipped | GodotCon Amsterdam 2026 LibGodot talk; `libgodot_project` README | ⛔ **blocker today** |
| the one package that does it (`2dog.engine`) **targets .NET 10**; HROT is .NET 8 | NuGet `2dog.engine` (4.7.2.x) | ⛔ blocker unless HROT moves to .NET 10 or the package is rebuilt — unverified whether it can be |
| **toggling off and on** (V-01) needs engine restart in one process — upstream "restart support" patches were still being merged | GodotCon 2026 talk | ⚠ risk for the menu toggle |
| **crash isolation** — a Godot or GPU-driver crash in-process takes the editor down with unsaved work | — | ⚠ real, permanent |
| **two engines' windows and GL contexts in one process** (Raylib/GLFW on the host loop + Godot on its own thread) — feasible on Windows/Linux in principle, untried | host: `RaylibPresentationShell.cs:34`; Stride ran its two windows on ONE thread | ⚠ risk, not a blocker |
| what in-process would GAIN | the offline hosts would need no translators and no DDS | ⭐ real — but U9's module removes most of that cost |

⇒ **Revisit when an official .NET 8 LibGodot path exists.** Until then the process model is the only one that builds.

### D2 · The gateway — ⭐ **the only input to Godot** (U4)

Godot code depends on `RenderModel` alone. Inputs to the gateway: the viewer node's world (stage 1–2), SumoSharp's
replication (stage 3). ⇒ no Godot type crosses into a host, and no second data path ever reaches Godot.

### D3 · The transport — ⭐ **full NED; the gateway hosts a passive HROT node; offline hosts hot-install a gateway module** (U9)

| the lean rests on | how it IS | how it was MEANT |
|---|---|---|
| everything the viewer needs already crosses NED/DDS | ✅ §3.3 — 9 of 11 on the wire, 2 need existing code wired, 0 new | ✅ gizmos-1 §6 (gizmo topics are DDS by design); UXI-11 §2.7.17 (selection egress) |
| a module can be switched on at runtime | ✅ `ModuleHostKernel.InstallModuleAsync` / `UninstallModuleAsync` (`ModuleHostKernel.cs:1372,1461`), used in production for the recording/replay modules (`EcsRecordReplayController.cs:132-220`) | ✅ `designs/replay-and-modules/DESIGN.md` (hot-plugged modules) |
| an external viewer of one node is an already-ruled concept | ✅ CE-463 (`PickStreamId = targetNodeId`) | ✅ Q73 §8 |
| a passive node is a known shape | ✅ IG (`IgNodeBootstrapper`) | ✅ R-140 |
| decoding + smoothing must not be written twice | ✅ NED ingress translators + `DeadReckoningSyncSystem` | ✅ `DESIGN_Dead_Reckoning.md` (DR on every node) |
| stage 2 (V-18) becomes free | the stage-1 path IS the network path | ✅ V-18 |

**The module (offline hosts only):** `ViewerGatewayModule : IEcsModule`, installed when `View ▸ 3D Viewer` is checked and
uninstalled when unchecked or when the viewer process exits. It opens a DDS participant on the **normal domain** (U9: no
dedicated partition) and instantiates the **same** egress translators the networked hosts register (entity master,
geospatial, damage, fire, stance, gizmo batch + string intern, selection egress) plus the ingress for what comes back
(`GizmoInteractionBatch`, `UpdateEntityAttributeRequest`, `CreateEntityRequest`). ⭐ It **composes**; it implements no
translator.

**What still has to be measured, before it is built:**
- ⚠ **authority on the offline hosts.** Egress publishes only entities the node has authority over
  (`GeoSpatialEgressTranslator.cs:135-136`). The Editor owns everything as a one-node cluster (Q86) — ⛔ *whether its
  entities actually carry authority bits under `NullReplicationModule`* is unverified. ReplayBrowser's replay worlds are
  reconstructed — ⛔ their authority bits are unverified too. If either is empty, egress publishes nothing.
- ⚠ **sharing the normal domain with a live cluster.** An Editor or ReplayBrowser publishing on the same domain as a
  running cluster would put two copies of the same network ids on the wire. Fine while they are not run side by side;
  ⛔ not designed for here.
- the FDP + NED weight inside the Godot process — S0.

**Rejected:**
- **Viewer-private DDS topics** — a second egress of the state NED already publishes (ruling 9); stage 2 still needs NED.
- **A loopback socket** (rev 1) — the same duplication, plus no `ddsmonitor`, no second viewer, no remote machine.
- **Switching the Editor's whole replication module to NED at boot** (rev 2's Q9 lean) — heavier, and always-on;
  the hot-installed module is on only while the viewer is.
- **A plain DDS subscriber gateway without an FDP world** — re-implements translators, lat/lon conversion and DR.

### D4 · Godot app structure — ⭐ **City3D's split**

`Hrot.Viewer.Core` (pure net8, no Godot types, unit-tested: the viewer node bootstrap, `RenderProjection`, the coordinate
transform, terrain meshing, extracted triage + blend) + `Hrot.Viewer.Godot` (thin glue). Godot owns nodes,
`MultiMeshInstance3D`, `AnimationTree`, `PopupMenu`, the free camera, picking.

### D5 · Coordinates (V-16) — ⭐ **one transform, in Core**

HROT: X east, Y north, Z up, right-handed, yaw 0 = east, +90° = north (`SimComponents.cs:16`). Godot: Y up, right-handed,
camera forward −Z.

| | HROT → Godot |
|---|---|
| position / velocity | `(x, y, z) → (x, z, −y)` |
| rotation quaternion | `(x, y, z, w) → (x, z, −y, w)` — the axis map has det = +1, **no handedness flip** |
| yaw | HROT yaw θ about +Z **=** Godot rotation θ about +Y |

⭐ The same map SumoSharp uses for SUMO → Godot (SUMO is also x-east / y-north), so stage 3 shares it. NED's lat/lon is
converted to local metres by the node's `WGS84Transform` (origin from the terrain) **before** this transform.

### D6 · Terrain scene (V-02) — ⭐ **the viewer node loads the terrain by name like any node; the gateway meshes it per kind and caches by content hash**

Cache: `<LocalAppData>/Hrot/viewer-terrain/<name>-<hash>.res` (folder rule precedent: `NavTileCache`).
**Rejected:** streaming terrain geometry — a second representation and a new topic.

### D7 · Camera entity (V-12..V-15) — ⭐ **an ordinary spatial entity of a camera TKB type** (U7); one per launching host; created on first camera move; none in ReplayBrowser (U3)

Pose lives in the usual spatial component and replicates through `WorldPos` like any entity; it is saved with the
scenario like any entity, and — as a side effect of being ordinary — the 2-D map can draw it. Godot → owner:
`UpdateEntityAttributeRequest`; owner → Godot: `WorldPos`. ⚠ Echo: while the user flies, Godot ignores incoming poses
older than its last sent one (precedent: selection's `"Remote."` echo suppression). Field of view stays a viewer setting.

### D8 · Selection, picking, context menu (V-10, V-11) — ⭐ **reuse the gizmo interaction path** (U6)

Click → Godot ray vs entity bounds → `GizmoInteractionBatch` Started for that `netId` → the host's own ingress makes it a
selection request (CE-463 shape). The selection set returns as the host's `SelectionChangedEvent`; Godot draws the
wireframe cube. Right-click → the `ContextMenuBinding` for that `netId` → JSON from `StringInternEntry` → Godot
`PopupMenu` → `MenuAction(netId, id)` back. ⭐ What the id means is the host's business.

### D9 · Gizmos in 3-D (V-07) — ⭐ **extract the triage, shared by Stride and Godot**, with per-shape skip counters (Stride §8)

### D10 · Models and animation (V-03, V-04) — ⭐ **box models first (U5), then real assets**

`Godot.RenderModelDef` names a model; S0–S2 use **procedural boxes**: a tank = hull box + turret box (+ barrel), APC = hull
box, car = body + cabin, helicopter = fuselage + rotor disc (spins with speed), jet = fuselage + wing slab, person =
mannequin if trivially available, else a capsule. Real assets later, behind the same descriptor, with a licence table
(CC0 / MIT / CC-BY). Locomotion from the extracted `LocomotionBlend` + `StanceStatus`. ⛔ Not `IAnimationBackend` — that
is the simulation's animation contract.

### D11 · Effects (V-08) — ⭐ **NED `WeaponFire` / `MunitionDetonation` → `EffectEvent` → Godot one-shot particles**

### D12 · The viewer node's role — ⭐ **lean: a new `View3D` flag, admitted to NED's receive-only arm beside `Map2D`**

NED composes a node by role (`NedReplicationModule.cs:243-254`): `Map2D` gets exactly what a viewer needs — ghost creation,
entity-state ingress, dead reckoning, `driveFromNetwork` — and a role with none of Muscle/Map2D/Brain **throws**. The
change is to read `Map2D | View3D` where the code means *"receive-only presentation"*, plus the role→load-part and
asset-capability tables. ⭐ No new arm, no copied code.
**Rejected:** declaring `Map2D` — zero code, but a 3-D viewer would carry the 2-D map's name into every role-derived
decision (the user's R-S3: *"the ability to support 2d map should be named as such"*) · `NodeRole.None` — skips
replication entirely (`HrotNodeBuilder.cs:113`).
⚠ S0 may start on `Map2D` to prove the node before the flag lands — named as temporary if it does.

---

## 6. OPEN QUESTIONS

| # | question | ⭐ lean | state |
|---|---|---|---|
| Q1 | Stride | untouched | ✅ **ruled** (U1) |
| Q2 | separate process | yes — D1a says why not in-process | ✅ accepted (U2); U8 answered in D1a |
| Q3 | camera entity shape | ordinary spatial entity · one per launching host · on first move · none in ReplayBrowser | ✅ **ruled** (U3, U7) |
| Q4 | code layout | `Viewer3D/`; Godot project outside `HROT.sln` | ✅ **ruled** (U3) |
| Q5 | Godot version | 4.7.1 .NET | ✅ **ruled** (U3) |
| **Q6** | first host | U3 approved "Editor". Under U9 the Editor needs only the gateway module, so it is reachable early. ⭐ **Lean: S0 against a `--mode all` cluster (zero host work, proves the viewer node), then the Editor through the module in S1** | **open** |
| Q7 | transport | full NED + hot-installed module on offline hosts, normal domain | ✅ **shaped by the user** (U2, U9) |
| **Q8** | ReplayBrowser | ⭐ the same module as the Editor — ⚠ conditional on its replay worlds carrying authority bits (D3, to measure) | **open** |

---

## 7. STAGES

| slice | delivers | proves |
|---|---|---|
| **S0** spike | fetch Godot in the cloud; the extracted DDS-setup helper; `ViewerNodeBootstrapper` boots inside the Godot process and joins a `--mode all` cluster; box entities move; Xvfb screenshot | D1, D3's viewer half, V-20 — and the three §3.4 unknowns: **the assembly weight in Godot**, **`NodeBootstrapper.BuildOrchestration` on a receive-only role**, **a late-joining slave accepted by the master** |
| **S1** Editor host | `ViewerGatewayModule` hot-installed by `View ▸ 3D Viewer` (measure the Editor's authority bits first); the toggle spawns/kills the process; free camera; default camera | V-01 (Editor), V-05, V-09, V-16, V-17 |
| **S2** scene | terrain meshes + cache; `Godot.RenderModelDef` with box models; stance/locomotion | V-02, V-03, V-04 (boxes), V-06 |
| **S3** interaction | extracted gizmo triage; `StringInternEntry` publisher wired; selection egress wired; picking; menu popup | V-07, V-10, V-11 |
| **S4** camera entity | camera TKB type; create on first move; both-way pose; scenario save | V-12..V-15 |
| **S5** effects | fire / detonation particles | V-08 |
| **S6** other hosts | CGF + SimHost: the toggle only (they already speak NED); ReplayBrowser: the module, per Q8 | V-01 complete |
| **stage 2** | standalone viewer on any NED cluster — the same app, no host launching it | V-18 |
| **stage 3** | SumoSharp ingest in the gateway; road-net mesh; a shared origin with the HROT terrain | V-19 |
| *assets* | real models replacing boxes, licence table | V-03, V-04 |

⭐ Each slice gated in the cloud by Core unit tests, a `godot --headless` smoke and an Xvfb screenshot (City3D's
`run-smoke.sh` / `screenshot.sh`). DDS cross-process works in this cloud (`ddsmonitor` captured a multi-process cluster,
`docs/RUNBOOK_Cluster_Debugging_Over_Http.md` §5a). ⚠ The aesthetic check stays a human one.

---

## ⛔ HISTORY — rev 1 and rev 2 (2026-10-10), superseded the same day. Do NOT quote.

- **Transport:** rev 1 leaned to a **loopback TCP socket** carrying a viewer-private `ViewerFrame` contract, produced by a
  host-side `ViewerProducerSystem` + `ViewerRequestSystem`. Superseded by D3 after U2 and the §3.3 measurement: that
  contract would have been a second egress of state NED already publishes.
- **First host:** rev 1 leaned "Editor first"; under full NED the Editor is offline-only, so Q6 is re-asked.
- **Menu actions:** rev 1 listed two pre-existing defects on the menu-action return path; dropped per U6 — the action's
  meaning is the host's business, not this design's.
- **Camera (rev 2):** a new `ViewCamera` component + NED descriptor. Superseded by U7 — an ordinary spatial entity.
- **Editor NED mode (rev 2 Q9):** switch the Editor's whole replication module to NED at boot on a private domain.
  Superseded by U9 — a hot-installed gateway module on the normal domain, on only while the viewer is.
