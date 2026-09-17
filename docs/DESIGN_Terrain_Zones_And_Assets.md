<!--STATUS
state: LIVE
build-state: ⛔ **DESIGN — DOWNGRADED FROM READY-TO-BUILD `2026-09-17`.** The diagrams (§2–§4) stand,
  but §8 (HOST HETEROGENEITY) is an unclosed hole the user found after they were drawn: this document
  assumed ONE load model role-filtered across hosts, and there are at least TWO. ⛔ No handoff until §8
  is ruled. ⚠ Marking it READY-TO-BUILD was a coordinator error — the module diagram showed WHO runs the
  loader and never asked whether they run the SAME ONE.
updated: 2026-09-17
current-answer: §2 is the model, §3 the two invocation paths, §4 what is registered and ticked where
  (incl. the two DEAD edges), §5 the WHY, §6 what is real vs faked in slice 1.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: none. This document REPLACES the zone half of docs/designs/packs-3/DESIGN.md
  (§2.B/§2.C/§2.E), which is already marked superseded there.
related-designs:
  - docs/blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — the WHY and the
    decision record (§5 ruling, §6 gaps). THIS doc is the WHAT; that one is why it is shaped so.
  - docs/designs/mgmt-1/DESIGN.md — §11 owns the PrepareZone/CommitZone 2PC protocol and ZoneSpec;
    this doc owns the AUTHORING model and the entity→asset build that §11 never covered.
  - docs/designs/packs-3/DESIGN.md — SUPERSEDED zone half; still owns the ACL/network-DRY work.
  - docs/DESIGN_Distributed_Scenario_Persistence.md — owns which file entities ride in and the
    ownership save gate; zone/road/obstacle entities pass through that gate like any other.
  - docs/designs/navig-2/Navigation_Design_v2_0.md — owns per-layer navmesh bake parameters and the
    INavmeshProvider seam; §6 here must not contradict its bake model.
  - docs/DESIGN_Node_Roles_And_Policies.md — owns which role consumes terrain data (MuscleGround,
    Perception, NavigationSolver); §4 here role-filters on exactly that.
-->

# DESIGN — **Terrain, zones and the asset build**

> **The one rule:** the **ENTITY IS THE DEFINITION**; an asset is **cached, reconstructable data**
> derived from it. There is no artefact that can disagree with the world.

## 1. INVENTORY — measured `2026-09-16`/`17` (graph + grep; `check_index_coverage` not run)

| # | exists already | where |
|---|---|---|
| ① | `EditablePolyline` — points **+ a `Version` counter** documented *"so subscribers can detect stale cached copies"* | `Hrot.Core/Components/Map/EditablePolyline.cs` |
| ② | ⭐⭐ **`RoadNetworkBuilder`** — *"Builder for constructing RoadNetworkBlob from components"*: `AddNode` / `AddSegment` / `Build` | `FDP/.../CarKinem/Road/RoadNetworkBuilder.cs` |
| ③ | `RoadNetworkBlob` (NativeArrays + a broadphase grid), `ZoneEnvironmentData` singleton | `FDP/.../CarKinem/Road/`, `FDP/.../CarKinem/` |
| ④ | the 2PC seam: `PrepareAsync` *(must not mutate ECS)* / `Commit` *(main thread)* / `Abort` | `FDP/.../Orchestration/IClusterStateHandler.cs` |
| ⑤ | the barrier — waits for **all** nodes' `NodeOpCompletedEvent` | `ClusterMaster.cs:1242-1254` |
| ⑥ | `NodeOpType.PrepareZone=7 / CommitZone=8` reserved on the wire in **two** enums | `NodeOpType.cs:15-16`, `OrchestrationMessages.cs:53-54` |
| ⑦ | `ClusterOpType.LoadZone=3` + a real wire arm publishing `LoadZoneIntent` | `ClusterOpMasterTranslator.cs:238-241` |
| ⑧ | shared cross-host registration precedent | `SerializeLocalRegistrar` (`CE-279`) |
| # | **does NOT exist** | |
| ⑨ | any entity→asset compile; any `CmdSwapZone`; any consumer of `LoadZoneIntent`; any zone control in the cluster panel | measured absences |
| ⑩ | any load-state marker component — graph returned only unrelated editor test classes | ⇒ new component, not a missed seam |

---

## 2. THE MODEL

```mermaid
classDiagram
  class EditablePolyline {
    +List~Vector2~ Points
    +int Version
  }
  class Area {
    +AreaType Type
  }
  class RoadFeature {
    +float LaneWidth
    +int LaneCount
  }
  class TerrainAssetLoadState {
    +LoadPhase Phase
    +int SourceVersion
  }
  class PhysicsCollider {
    +float Radius
  }
  class TerrainLoadService {
    +EnsureLoaded(view, entity) bool
    +EnsureAllLoaded(view) int
  }
  class ZoneTileLoader {
    +Build(bounds, version)
  }
  class RoadNetworkCompiler {
    +Compile(view) RoadNetworkBlob
  }
  class RoadNetworkBuilder {
    +AddNode(pos)
    +AddSegment(...)
    +Build(...) RoadNetworkBlob
  }
  class ZoneEnvironmentData {
    +RoadNetworkBlob RoadNetwork
  }
  EditablePolyline <-- Area : boundary
  EditablePolyline <-- RoadFeature : centerline
  Area --> TerrainAssetLoadState : marked when loaded
  RoadFeature --> TerrainAssetLoadState : marked when loaded
  TerrainLoadService --> ZoneTileLoader
  TerrainLoadService --> RoadNetworkCompiler
  RoadNetworkCompiler --> RoadNetworkBuilder : REUSED
  RoadNetworkCompiler --> ZoneEnvironmentData : publishes blob
  note for PhysicsCollider "OBSTACLE = live by construction.<br/>No asset, no load, no marker (R1)."
  note for RoadNetworkBuilder "EXISTS - FDP CarKinem/Road/RoadNetworkBuilder.cs"
  note for EditablePolyline "EXISTS - its Version is the staleness key (R4)"
  note for ZoneEnvironmentData "EXISTS - the swappable singleton (R2)"
```

*What the picture shows that the prose hid:* **obstacles hang off nothing.** Every other definition
kind flows into a loader and earns a marker; the obstacle box terminates immediately — which is the
whole of ruling R1, visible rather than argued.

| new type | why it is new |
|---|---|
| `Area { AreaType Type }` | a zone is *a tactical drawing of an area that happens to be typed `Zone`*; the type field is what keeps one authoring surface serving zones and tactical areas alike |
| `RoadFeature` | the KIND discriminator the build selects on (`Q71-E2`); `EditablePolyline` alone cannot say "this polyline is a road" |
| `TerrainAssetLoadState { LoadPhase Phase, int SourceVersion }` | ⭐ `[DataPolicy(NoScenario \| NoReplay)]`. **Not** a bare tag — streaming has an in-flight state and loads can fail. ⚠ Named `Terrain…` on purpose: a bare `AssetLoadState` collides with the editor's existing BTree/HSM asset-load-state concepts |

---

## 3. THE TWO INVOCATION PATHS — one implementation

### 3.1 Runtime — operator-driven, via the 2PC round

```mermaid
sequenceDiagram
  participant OP as Operator (cluster panel)
  participant M as ClusterMaster
  participant H as TerrainAssetHandler (every host)
  participant S as TerrainLoadService
  OP->>M: BuildTerrainAsset (kinds) or Reload zones
  M->>H: PrepareTerrainAsset (txId, kinds)
  Note over H: role x kind filter<br/>nothing for me -> ACK at once
  H->>S: build into STAGED buffer (no ECS mutation)
  S-->>H: staged blob + versions
  H-->>M: NodeOpCompleted Ready
  Note over M: barrier - all nodes
  M->>H: CommitTerrainAsset (txId)
  H->>S: publish staged -> ZoneEnvironmentData singleton
  H->>H: stamp TerrainAssetLoadState on each definition
  H-->>M: NodeOpCompleted
```

### 3.2 Scenario load — the same service, called locally, **no NodeOp**

```mermaid
sequenceDiagram
  participant LH as Scenario load handler
  participant S as TerrainLoadService
  participant W as Node world
  LH->>W: deserialize entities (zones, roads, obstacles)
  LH->>S: EnsureAllLoaded(view)
  loop each Area(Zone) and RoadFeature
    S->>W: read marker + EditablePolyline.Version
    alt marker Loaded and version matches
      S-->>S: skip - idempotent
    else missing or stale
      S->>S: build, then stamp marker
    end
  end
  Note over LH,S: already inside the cluster's own<br/>load transaction - a nested 2PC would deadlock
```

*What these show that prose hid:* the **same `TerrainLoadService` is the only implementation**; the
2PC round is an *invocation wrapper* the scenario path deliberately does not use. The idempotency
check is the identical branch on both paths, so "reload" and "load on scenario open" cannot drift.

---

## 4. MODULE RELATIONSHIPS — who registers it, who runs it, what is DEAD

```mermaid
graph TD
  subgraph Reg["Registration - shared, every ECS host"]
    TR["TerrainAssetRegistrar<br/>mirrors SerializeLocalRegistrar"]
    TR --> TAH["TerrainAssetHandler<br/>IClusterStateHandler"]
  end
  subgraph Hosts["Hosts - role filtered"]
    CGF["CGF Brain<br/>ACKs, builds nothing"]
    SIM["SimHost Muscle<br/>builds roads + tiles"]
    IG["IG Map2D<br/>ACKs"]
    ED["Editor all-in-one<br/>builds"]
  end
  TAH --> CGF
  TAH --> SIM
  TAH --> IG
  TAH --> ED
  SIM --> ZED["ZoneEnvironmentData singleton"]
  ZED -->|re-read EVERY TICK| CK["CarKinematicsSystem"]
  ZED -.->|MUST become a per-tick read| PF["PathfindingSolverSystem"]
  LZI["LoadZoneIntent<br/>published by wire translator"]
  LZI -.->|NO CONSUMER| NONE["nothing"]
  PFOLD["PathfindingSolverSystem<br/>readonly ctor blob - FROZEN"]
  style LZI fill:#fdd,stroke:#900
  style NONE fill:#fdd,stroke:#900
  style PFOLD fill:#fdd,stroke:#900
```

⛔ **The two red boxes are MEASURED DEAD/BROKEN EDGES, and they are why this diagram exists:**

1. **`LoadZoneIntent` has no consumer** — the wire translator publishes it (`ClusterOpMasterTranslator.cs:238-241`)
   and nothing reads it, while its own doc comment at `ClusterOpIntents.cs:133` claims *"Consumed by
   `ClusterMaster`"*. The comment is false and must be corrected whichever way this builds.
2. 🔴 **`PathfindingSolverSystem` holds `readonly RoadNetworkBlob _roadNetwork`, assigned once in its
   constructor** (`:32`, `:63`). ⇒ **a commit-time pointer swap reaches `CarKinematicsSystem` and
   silently does nothing for pathfinding.** `NavigationSolverModule` and `EngineBackedNavigationModule`
   have the same shape. **R2 is therefore a precondition, not a cleanup.**

---

## 5. WHY — the rationale the diagrams cannot carry

### 5.1 Why there is no zone artefact
An artefact is a second place the truth can live, and the moment it exists it can disagree with the
world (`R-132`, two producers for one slot). Making the entity the definition and the asset a
**cache** removes the failure mode by construction: a cache that disagrees is simply *stale*, and
staleness is detectable (§5.3) where disagreement is not.

### 5.2 Why obstacles are excluded (R1)
Measured: `RaycastSolverSystem.cs:145-147` reads `PhysicsCollider` straight off broadphase candidates
and `LosRequestBatchingSystem` queries by its component id ⇒ **an obstacle occludes LOS the instant
the entity exists.** There is nothing to cache, so a marker on an obstacle would always read `Loaded`
and mean nothing. ⛔ A vacuous state field is worse than none — it invites code to branch on it.
⭐ If a baked static-occlusion structure is ever wanted, it is an **optimisation** that re-enters
through this same design, not a gap being left open.

### 5.3 Why the marker carries a version, not just a flag (R4)
Idempotency without staleness is indistinguishable from *never reloading*: redraw a loaded zone's
boundary and a bare flag still says `Loaded`, so the cache silently serves the old shape forever.
⭐ **The key already exists** — `EditablePolyline.Version` is documented *"incremented … each time a
committed edit is applied, so subscribers can detect stale cached copies."* It was built for exactly
this, so the marker stores the version it was built from and a mismatch means rebuild.

### 5.4 Why the swap seam is a precondition (R2)
See §4's second red box. ⭐ **Preferred fix — reuse, not a new abstraction:** make the
`ZoneEnvironmentData` **singleton the single source** and have the navigation systems re-read it per
tick exactly as `CarKinematicsSystem` already does, rather than introducing a holder/provider object.
One source, no new seam, and it matches the "one source, read it every time" pattern (`R-126`).
⚠ **What would flip it:** if a navigation module runs on a background thread where singleton access is
constrained by `DataPolicy`, a holder becomes necessary. **Check that before building.**

### 5.5 Why roads are real and tiles are faked
`RoadNetworkBuilder` already turns nodes + segments into a `RoadNetworkBlob`, and a road polyline is
a node-and-segment list — so the road compile is **mostly reuse and genuinely small**. Terrain tiles
(navmesh, heightmap, streaming, geographic cache keys) are none of those things, and the user ruled
them postponed. ⇒ the slice is honest about which half is which rather than faking both.

### 5.6 Why a fake must announce itself
`R-133`: *a capability reported present that silently no-ops is worse than an absent one.* The tile
loader therefore logs its stub-ness on every round **and** the capability manifest must not advertise
a real terrain capability. This is a build constraint, not a caveat.

---

## 6. SLICE 1 — what is real, what is faked

| piece | slice 1 |
|---|---|
| `Area`+`AreaType`, `RoadFeature`, `TerrainAssetLoadState` | ⭐ **REAL** |
| authoring zones / roads / obstacles as entities; saved by the ordinary gate | ⭐ **REAL** |
| road compile (entities → `RoadNetworkBlob` via `RoadNetworkBuilder`) → singleton | ⭐ **REAL** (§5.5) |
| R2 swap seam (navigation reads the singleton per tick) | ⭐ **REAL — precondition** |
| idempotency + version staleness | ⭐ **REAL** |
| both invocation paths, the 2PC round, shared registration, panel controls | ⭐ **REAL** |
| **terrain tile generation / streaming / geographic cache** | ⛔ **FAKED** — stub that logs, marks loaded |
| obstacles in the load model | ⛔ **EXCLUDED** (R1) |

**Enum values to allocate:** `ClusterOpType.BuildTerrainAsset = 17` (next free; 2 is a documented
reserved gap). `NodeOpType.PrepareTerrainAsset = 29`, `CommitTerrainAsset = 30` — ⚠ **do not reuse the
undocumented gaps at 6/17/18/19**; both enums are wire contracts in two places.

**Retirement, with its test surface** (`HN-037`: measure tests, not just production): `ZoneDefinitionDto`,
the embedded `Zones` section, `ZoneMembership`, `ZoneManagerService`'s DTO half, the `ScenarioMergeCore`
I4 guard, `ZoneEditorPanel` → repointed at entity authoring; and the five suites that assert the
retiring behaviour — `ZoneManagerServiceTests`, `ZoneScenarioLoadIntegrationTests`, `ZoneEditorPanelTests`,
`ScenarioFileServiceZoneTests`, plus the two `SpyZoneManagerService` doubles.

## 8. ⛔ OPEN — **HOST HETEROGENEITY: there is more than one load model**

> 🔒 **User, `2026-09-17`:** *"What all nodes implement navigation and perception and whatever affected
> by reloading the tiled data. Some hosts might not support dynamic loading of these stuff — like maybe
> stride simhost — they should say what they support in their capability flags… their zone load
> implementation will be different (all preloaded with terrain load and unchangeable and not tile
> streamed, tied to the terrain id...), what the ui should look like and do etc."*

### 8.1 Measured `2026-09-17`

| # | fact | site |
|---|---|---|
| ⑪ | **4 production navmesh providers**: `DotRecastNavmeshProvider` (Stride), `EngineBackedNavmeshProvider`, `FakeNavmeshProvider`, `StubNavmeshProvider` (EQS) | `search_graph(".*NavmeshProvider.*", Class)` = 12 incl. tests |
| ⑫ | ⭐ **Stride's navmesh is baked from STRIDE SCENE GEOMETRY at scene load**, at two call sites (node shell `:1116`, editor `:1271`) | `StrideHrotGame.cs:1834` `BakeNavmesh` |
| ⑬ | ⇒ **its source is the scene, not our entities or tiles** — tile streaming has nothing to give it. This is the user's *"one navmesh, preloaded, tied to the terrain id"* host, measured | ⑫ |
| ⑭ | 🔴 **NO host consumes terrain tiles today.** The only terrain-derived data with live consumers is the **road blob** | ⑪+⑬ |
| ⑮ | ✅ **CORRECTION TO §4/R2 — the navmesh IS swappable.** It is a *singleton-managed* provider (`SetSingletonManaged<INavmeshProvider>`) read per use (`VehicleNavigationIntentSystem.cs:190-191`) ⇒ **R2's frozen-ctor problem is ROAD-SPECIFIC, not general** | `StrideHrotGame.cs:1870` |
| ⑯ | perception is **already role-gated by composition** — `.Capability(NodeRole.Perception, new SimHostCapabilities.PerceptionSpatial(...))` | `SimHostNodeBootstrapper.cs:307` |
| ⑰ | ⭐⭐ **the cross-node capability mechanism already exists** — namespaced tokens (`CapabilityTokens.ReliableInit`, `fdp.role.*`) on the durable `NodeCapabilities` descriptor, ingested by `ClusterMaster` into `NodeHealthProfile`, with the role mask **DERIVED** from the token subset | `AQ-70 §Q70-B/C`; `IgNodeBootstrapper.cs:340`, `ClusterMaster.cs:491` |

### 8.2 The questions this opens

| # | question | ⭐ lean |
|---|---|---|
### 8.3 ✅ RULED `2026-09-17` — **no capability is announced at all**

> 🔒 **User:** *"no host should be fully static, in a sense that it can never load another terrain. The
> ability to load terrain which is defined in the scenario is **mandatory**. Maybe right now some hosts
> like stride do not support it but this is more a **bug and unimplemented feature** than something we
> can live with… So just the dynamic zone loading/tile streaming is what is not supported there…
> With static terrain the zone load is **always satisfied immediately**. So the user does not need to
> know the zone load was made in static mode, it was simply satisfied immediately (OK)."*

⭐⭐⭐ **Why this is stronger than a simplification:** with static terrain the zone-load POSTCONDITION —
*"the terrain data covering this zone is resident"* — **is genuinely TRUE**, because all of it already
is. Static is not a degraded mode, it is a **trivially complete** one. ⇒ there is nothing to advertise
because nothing is missing.

| was | now |
|---|---|
| **N1** role-or-capability | ⛔ **COLLAPSED** — no capability exists to classify |
| **N2** advertise a token pair | ⛔ **COLLAPSED.** ⭐ The cleanest way to satisfy `R-133` *(never declare a capability that no-ops)* is to declare none |
| **N3** three outcomes | ⭐ **TWO: satisfied / failed.** A host that cannot load the scenario's terrain at all is **BROKEN, not static** — it FAILS loudly |
| **N5** show the mode | ⭐ per-node **OK / Failed** only; no mode to display |
| **N6** capability filter to avoid stalling | ⛔ **COLLAPSED — there is no matrix.** 🔒 *"Host not taking active part should always ack to avoid blocking, why a matrix is needed?"* ⇒ **the ACK is UNCONDITIONAL**; the only input to whether WORK happens is the request's `kinds` × **the loaders this host actually composed**. ⭐ Role filtering **already happened at composition** (`SimHostNodeBootstrapper.cs:307` `.Capability(NodeRole.Perception, …)`) — re-applying it at op time would be a second mechanism for one decision (ruling 9) |
| **N4** terrain-identity binding | ⭐⭐ **SURVIVES AND STRENGTHENS.** Since loading the scenario's terrain is MANDATORY, a host must verify it holds the scenario's `SceneId` and **fail loudly** if not — that is how Stride's present gap should surface instead of silently passing |

⭐ **Free consequence — `IgZoneDummyHandler` RETIRES.** It exists only to dummy-ACK `PrepareZone`/
`CommitZone` so IG does not stall the round; the shared handler now does that by construction on every
host, with no bespoke class.
⭐ **Kept, but DEMOTED to a node DIAGNOSTIC (not a wire capability):** whether a host built tiles or had
nothing to do. Not for the operator, not in the protocol — for the day tiles are real and someone asks
*"why did node X do nothing?"*

### 8.4 ⛔ STILL OPEN — the zone-loading UI

See §9. That is the only thing now standing between this document and `READY-TO-BUILD`.

## 9. ⛔ OPEN — the zone-loading UI *(leans for approval, `2026-09-17`)*

### 9.1 ✅ **THE LOCAL MARKER IS SUFFICIENT — and the component is NEVER replicated** *(user, `2026-09-17`)*

> 🔒 **User:** *"isnt showing the local one a simple and sufficient option? Would we need to publish
> share the loading state component, isnt it always local? Can these disagree across nodes?"*

⛔⛔ **A PRIOR DRAFT OF THIS SECTION WAS WRONG AND IS RETRACTED.** It argued that an operator station
must render a cluster rollup because *"ExCon/IG never build tiles, so their local marker would read
'not loaded' forever and the map would lie."* 🔴 **That premise contradicts §8.3.** Under
satisfied-immediately, a host with nothing to do **STAMPS THE MARKER `Loaded`** — it does not leave it
unset. ⇒ the local marker is **correct on every node, including operator stations**, and rendering it
is simple, honest and sufficient.

| the question | ⭐ the answer |
|---|---|
| publish/share the component? | ⛔ **No.** It is **node state keyed by entity**, not entity state — *"has THIS node got the data resident"*. Replicating it would assert one value for a fact that is legitimately per-node |
| is it always local? | ⭐ **Yes, by nature.** And `R-136` is satisfied without argument: it is **not durable state** *(re-derivable by re-running the load, and deliberately `NoScenario \| NoReplay`)*, so it needs neither a TKB home nor a published descriptor |
| can nodes disagree? | ⭐ **Yes, and they SHOULD.** Transiently while one is still building; persistently when one **FAILED**. ⛔ Disagreement is not corruption here — it is the truth |
| then how is another node's FAILURE seen? | ⭐ through the **op result** (§8.3's satisfied/failed), aggregated by the tracker and surfaced in the panel — ⛔ **not** by replicating a component |

⇒ ⭐⭐ **The division of labour:** the **map** renders the local marker *(what this node has)*; the
**panel** renders op outcomes *(what every node reported)*. Two surfaces, two honest questions, no
replication and no rollup plumbing.

### 9.2 Per question

| # | question | ⭐ lean |
|---|---|---|
| **U1** | seeing a zone is stale after an edit | ⭐ **outline STYLE carries state, text carries detail** — dashed/solid on the area outline is readable at a glance across many zones without reading, and does not rely on colour alone; the gizmo text is for the one zone being inspected. ⚠ **UNMEASURED:** whether the overlay renderer can parameterise stroke style per entity — check before committing |
| **U2** | invoking a load | ⭐ **`SharedContextMenuPopulator.PopulateEntityMenu`** — the exact existing seam: it already adds *"Edit Shape"* for `EditablePolyline` and *"Edit Route"* for `RoutePlan`. Add *"Load zone"* when the entity carries `Area{Type=Zone}`. Shared ⇒ every host using the shared menu gets it |
| **U3** | forcing all changed zones | 🔒 **RULED (user, `2026-09-17`): the zone editor becomes a VIEW on the existing DETAILS SHELL**, offered when empty map space is selected. ⭐⭐ **PURE REUSE — see §9.5.** ⛔⛔ **A PRIOR DRAFT OF THIS ROW CLAIMED THIS WAS "NEW INFRASTRUCTURE… the largest single item in this design." THAT WAS FALSE** — it came from a grep scoped to one folder and two name patterns. `DetailsWindow` already is *"THE DETAILS SHELL: one window, N views, chosen by a predicate"*, `WindowScope.PerspectiveBound`, with a view registry and contributed `*DetailsView` classes |
| **U4** | multi-zone at once | ⭐⭐ **the user's lean, and it is already supported: ONE OP PER ZONE.** 📐 Measured: `FanOutSerializeLocal` registers `_pendingTransactions[requestId]` (a **keyed dictionary**, `Expected = nodeIds.Count`) and never touches `_activeTransaction` ⇒ **concurrent rounds already work on this path in production.** ⛔ Do NOT widen the op to carry N zones |

### 9.3 Why one-op-per-zone beats a multi-zone payload

⭐ **Independent failure** — a single bad zone fails its own round, not the batch. ⭐ **Independent
progress** — U3's per-row state falls out of the tracker instead of needing a sub-protocol inside one
transaction. ⭐ **Natural retry granularity.** ⭐ And the node side stays free to **serialise the builds
at will** *(tile building is heavy I/O+CPU; N parallel builds would thrash)* — which is a LOCAL policy,
invisible to the protocol.
⚠ **The one question it raises:** *"load all stale"* on a 50-zone scenario opens 50 trackers at once.
Cheap (dictionary entries) but unbounded — ⭐ lean: cap in-flight at the **requester**, not in the
master, and leave the protocol alone.

### 9.5 ⭐⭐ THE ZONES VIEW — a contribution to the EXISTING details shell

📐 **Measured `2026-09-17` — the shell already exists and is actively used:**
`Hrot.Editor.AiShared/Windows/DetailsWindow.cs` — *"`L2.1` — THE DETAILS SHELL: one window, N views,
chosen by a predicate"*, `WindowScope.PerspectiveBound` with an `owningPerspective`, a
`DetailsViewRegistry`, an `IDetailsContextSource` and `IDetailsViewInstance` contributions
*(`BlackboardDetailsView`, `HsmEventsDetailsView`, `BlueprintNodeDetailsView`, …)*. Its own header notes
it **was** `AiDetailsWindow` and that the old name is false: *"this is the shell for EVERY perspective."*
📄 Owning design: **[`docs/blueprints/DESIGN_Details_Panel_View_Switching.md`](blueprints/DESIGN_Details_Panel_View_Switching.md)**.

⇒ ⭐ **The zones view is a registered `DetailsView`, not a panel.** The only genuinely new seam is a
**details CONTEXT for "the map background is selected"** — `IDetailsContextSource` must be able to
report it, so the registry's predicate can offer the zones view. That is a small addition to an
existing interface, not new infrastructure.

| what the view SHOWS — one row per `Area{Type=Zone}` entity | source |
|---|---|
| zone name | the Area entity |
| **state**: `Loaded` · `Loading` · `Failed` · **`Stale`** | ⭐ the **LOCAL** `TerrainAssetLoadState` (§9.1) — `Stale` is `marker.SourceVersion != EditablePolyline.Version` (R4) |
| last cluster outcome, incl. **which node failed** | the op result (§8.3), ⛔ never a replicated component |
| header: counts (`n zones · m stale · k failed`) | derived |

| what it SUPPORTS | |
|---|---|
| per-row **Load** | publishes the cluster op for that ONE zone (§9.2 U4: one op per zone) |
| per-row **select / zoom-to** | selects the zone entity; the map focuses it |
| header **Load all stale** | fans out one op per stale zone, capped at the requester |
| ⛔ **NOT** road-path or obstacle-radius editing | that was the retiring `ZoneEditorPanel`'s job; those are now ordinary entity authoring on the map |

### 9.6 🔒 RULED — **the zone-load action is ALWAYS cluster-wide**

> 🔒 **User, `2026-09-17`:** *"the zone load menu should always trigger cluster wide load."*

⇒ the context-menu item (U2), the per-row action and *"Load all stale"* **all publish the cluster op**;
⛔ **there is no local-only zone load, on any host.** ⭐ On the editor this still goes through the
orchestrator, because the editor **is** a single-node cluster — exactly the principle `CE-275` already
established for saving *("no direct write in the editor… same code everywhere")*. ⭐ One path, so the
editor cannot drift from the cluster.

### 9.4 🔴 FINDING — **`HasInFlightTransaction` is very nearly always FALSE** *(measured `2026-09-17`)*

> 🔒 **User:** *"The `_activeTransaction` concept feels weird, shouldnt it be 'any transaction is in
> progress?'"* — ⭐ **it should, and today it is not.**

📐 `_activeTransaction` is assigned at `ClusterMaster.cs:791` and **cleared at `:869 in the same
method**, commented *"ClusterMaster uses sync fan-out; clear immediately"*. `ClusterScenarioPanel.cs:288`
already concedes it: *"HasInFlightTransaction is reset to false immediately after the fan-out."*
⇒ ⛔ **the public `HasInFlightTransaction` — whose documented job is to disable command buttons while a
2PC round is pending — answers `false` while rounds are genuinely pending.** The real in-flight set is
**`_pendingTransactions`**, which stays populated until the ACKs complete.

| ⭐ consequence for this design | |
|---|---|
| ⛔ **do NOT source any zone-loading progress indicator from `HasInFlightTransaction`** | it would read "idle" throughout every load |
| ⭐ the honest signal is **`_pendingTransactions`** *(keyed, one tracker per zone under U4)* — which is also exactly what U3's per-row progress wants | |
| ⚠ **the pre-existing defect is OUT OF SCOPE here but should be filed** | the buttons this was meant to gate are not being gated; that is a cluster-panel bug, not a terrain one |

⚠ And `_activeTransaction` remains a **different, single-slot path** used by the cluster **state
machine**. ⛔ Zone ops must not be routed through it.

## 7. POSTPONED — deliberately not designed here

What a tile **is**, how it streams, its geographic cache key and eviction, and what a commit swap
replaces once tiles are real. 🔒 Ruled postponed by the user (`AQ-71` §5). ⭐ The fake is shaped so
that filling it in touches `ZoneTileLoader` and nothing else.
