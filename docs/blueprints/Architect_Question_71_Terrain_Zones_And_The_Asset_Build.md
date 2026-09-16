<!--STATUS
state: LIVE
build-state: DESIGN — decision-shaped; a RECOMMENDED LEAN per sub-question. Resolve JOINTLY with the
  user (no relay yet — see §0.1). ⛔ NOT buildable; no handoff until the leans (or alternatives) are
  approved and the resulting model is folded into the owning designs.
updated: 2026-09-16
current-answer: §3 (the sub-questions + leans). §1 is the INVENTORY, §2 the measured conflict that
  produced this document. Nothing here is a ruling yet.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: docs/designs/packs-3/DESIGN.md §2.B/§2.C contradicts its OWN design conversation
  (.dev/_DONE/packs-3/design_talk.md:555-567) and contradicts docs/designs/mgmt-1/DESIGN.md §11.
  This document exists to resolve that; it does not pretend the conflict is already settled.
related-designs:
  - docs/designs/mgmt-1/DESIGN.md — §11 owns the ZONE as a geographic staged-load unit (ZoneSpec,
    PrepareZone/CommitZone 2PC); it does NOT own authoring or asset production.
  - docs/designs/packs-3/DESIGN.md — §2.B/§2.C/§2.E own the AS-BUILT embedded `Zones` bundle and
    `ZoneManagerService`; this document proposes reverting that half.
  - docs/DESIGN_Distributed_Scenario_Persistence.md — owns WHICH FILE scenario data rides in and the
    ownership save gate; it does not own what a zone IS. Its line 511 needs correcting either way.
  - docs/designs/navig-2/Navigation_Design_v2_0.md — owns per-layer navmesh bake parameters; any
    asset-build design must land on it. It assumes SCENE GEOMETRY as the bake source, not entities.
  - docs/DESIGN_Node_Roles_And_Policies.md — owns which role consumes what; the zone/terrain
    consumers (MuscleGround, Perception, NavigationSolver) are named there.
  - docs/designs/cgf-1/mgmt-DESIGN.md — ⚠ near-duplicate of mgmt-1 carrying the same §11 (R-132 two
    producers). Read mgmt-1; that copy must not be quoted as current.
-->

# Architect Question 71 — **What is a zone, what is a terrain, and how do authored entities become loadable assets?**

> **One sentence:** `packs-3` made a *zone* a content bundle (roads + terrain-db + obstacles);
> `mgmt-1` made it a *geographic window* onto a terrain — and `packs-3`'s own design conversation
> argued for `mgmt-1`'s shape before the opposite was built.

## 0. Why this document exists

Three threads converged: the CE-277 zone follow-on (*"give CGF a zone service"*), the measured
round-trip duplication of zone obstacles, and the question of how operator-authored routes and
obstacles ever become the heavy data the simulation actually consumes. All three dissolve into one
prior question — **what is a zone** — which two designs answer differently.

### 0.1 ⛔ Relay discipline

This document carries **my leans**. Per `CLAUDE.md`, if it is ever relayed to the NotebookLM
architect, ask **before** it lands in a corpus refresh, and phrase the ask as *evidence-only*
(*"name the producers of X"*), never *"which option should we pick?"* — a verdict from something
that has ingested my verdict is worth nothing.

---

## 1. INVENTORY — enumerated, not guessed

Graph queries (codebase-memory, project `home-user-HROT`), each with its `total`:

| query | total | what it settled |
|---|---|---|
| `search_graph(name_pattern=".*Terrain.*")` | 112 | terrain exists as a **height-query provider** (`ITerrainProvider`, `TerrainQuery*System`), **not** as an identified loadable content pack |
| `search_graph(".*(RoadNetwork\|NavMesh\|Navmesh).*")` | 315 | navmesh baking exists (`StrideNavmeshBaker`) but bakes **Stride scene geometry**, never ECS entities, and writes no file |
| `search_graph(".*(EditablePolyline\|RoutePlan\|ZoneMembership\|ZoneObstacle\|ZoneDefinition).*")` | 81 | routes/shapes are **already entities**, already replicated, already saved |
| `search_graph(".*(IClusterStateHandler\|TkbLoadClusterStateHandler).*")` | 50 | the 2PC prepare/commit/abort seam is generic and live |

Corroborating greps (both trees; ⚠ `check_index_coverage` not run, so absences below are strong but
not proof):

| # | measured | site |
|---|---|---|
| ① | `NodeOpType` has **24** values; `PrepareZone = 7` / `CommitZone = 8` are **reserved on the wire** in *two* enums | `FDP/.../Enums/NodeOpType.cs:15-16`, `Hrot.Network.Orchestration/.../OrchestrationMessages.cs:53-54` |
| ② | the only implementor of either op is a **dummy ACK**; **no publisher exists** | `Hrot.IG/Modules/Orchestration/IgZoneDummyHandler.cs:41-42` |
| ③ | `CmdSwapZone` (the designed swap event) — **zero code hits** | designed only, `mgmt-1` §11.1 |
| ④ | the 2PC barrier is **BUILT and in daily use** — waits for all nodes' `NodeOpCompletedEvent` | `Hrot.Orchestrator/ClusterMaster.cs:1242-1254` (`GenericTransactionTracker`) |
| ⑤ | `PrepareAsync` is contractually *"async prep… **must not mutate ECS state**"*; `Commit` is main-thread; `Abort` rolls back | `FDP/.../IClusterStateHandler.cs:16-51` |
| ⑥ | **`TerrainDatabaseId` is DEAD** — 3 sites total (1 declaration + 2 lines of a DTO round-trip test), **zero production readers** | `Hrot.Core/Scenario/Map/ZoneDefinitionDto.cs:21` |
| ⑦ | terrain identity **already exists and is global** — `SceneId`, documented *"the map/terrain identifier"* | `Hrot.Orchestrator/GlobalContextClusterOpHandler.cs:78,279,291` ⚠ written `string.Empty` at `AssetInventoryProcessManager.cs:215` |
| ⑧ | `RoadNetworkLoader` is **load-only** — no writer anywhere | `FDP/.../CarKinem/Road/RoadNetworkLoader.cs` |
| ⑨ | zone obstacles are created as plain `SimTransform`+`PhysicsCollider`, **untagged** ⇒ the save gate persists them *as entities* while the `Zones` section re-creates them ⇒ **round-trip duplication** | `Hrot.Core/Services/ZoneManagerService.cs:59-68` vs `ScenarioSerializer.cs:557-563` |
| ⑩ | the merge enforces **exactly one `Zones` source**, throwing, citing *"brain-only"* — a rule the wiring does not follow (the **muscle** is the node wired to emit zones) | `ScenarioMergeCore.cs:115-123` vs `Hrot.SimHost/NodeBootstrapper.cs:306,313` |
| ⑪ | the authoring UI treats the road network as **a path you type to an existing asset** — it never produces one | `Hrot.Presentation/Panels/ZoneEditorPanel.cs` |
| ⑫ | **no entity→asset conversion exists**, in code or in either design tree | corpus sweep for bake/generate/convert→navmesh\|terrain\|road: every hit is scene-geometry-bake-at-load or a "future stage" note |

---

## 2. The measured conflict

| | `mgmt-1` §11 | `packs-3` §2.B/§2.C (**as built**) |
|---|---|---|
| a zone is | `ZoneSpec { ZoneId, GeoPoint[] Bounds, DataPath }` — *"a named high-resolution area… defined by a 2D polygon"* | `ZoneDefinitionDto { RoadNetworkPath, TerrainDatabaseId, Obstacles[] }` — a content bundle |
| where it lives | a zone artefact, referenced | **embedded** in the scenario file under `Zones` |
| terrain | global — `BaseTerrain` / `SceneId`, loaded at `LoadingEdit` | a **property of the zone** (dead field ⑥) |

🔴 **`packs-3` contradicts its own design conversation.**
`.dev/_DONE/packs-3/design_talk.md:555` (the user): *"The Zone concept… serves mainly for **EXERCISE
RUNTIME loading of terrain areas**. Maybe instead of directly referencing the static asset in scenario
files, we can reference some kind of predefined **ZONE name** used to locate zone json files… the
scenario loader will load these zones as part of loadingEdit or loadingLive."*
The reply agreed: *"**Rather than adding a new section to the scenario JSON file**, you should utilize
this existing Orchestration pipeline"* → `ScenarioHeader(SubsystemType, SchemaVersion, string? ZoneId)`.
`packs-3/DESIGN.md` §2.B then specified **exactly the embedded section the talk rejected**, with no
stated reason beyond *"one road network per zone — KISS"*.

⇒ this is a **regression from its own record**, not a later evolution. That matters for `R-137`
(*unification may not cost a feature*): reverting here removes a bundle nobody argued for, not a
capability someone chose.

### 2.1 The model this document proposes

```mermaid
graph TD
  subgraph Global["Global - orchestrator context"]
    T["Terrain / SceneId<br/>the content pack"]
  end
  subgraph Zones["Geographic windows onto the terrain"]
    Z1["ZoneSpec A<br/>Bounds + DataPath"]
    Z2["ZoneSpec B<br/>Bounds + DataPath"]
  end
  subgraph Content["World content - authored as ENTITIES"]
    R["RoutePlan / EditablePolyline"]
    O["Obstacle: SimTransform + PhysicsCollider"]
  end
  subgraph Built["Built assets - the conversion output"]
    RA["Road graph asset"]
    OA["LOS obstacle asset"]
  end
  T --> Z1
  T --> Z2
  R -->|PrepareTerrainAsset| RA
  O -->|PrepareTerrainAsset| OA
  RA -.->|windowed by| Z1
  OA -.->|windowed by| Z1
  X["RETIRED: ZoneDefinitionDto<br/>embedded Zones section<br/>ZoneMembership"]
  style X fill:#fdd,stroke:#900
  style Built fill:#efe
```

*What the picture shows that the prose hid:* content and zones are **orthogonal** — an authored road
crosses zone boundaries freely, and a zone windows whatever content falls inside it. The retired box
is exactly the place where `packs-3` fused the two axes.

---

## 3. THE SUB-QUESTIONS — each with a recommended lean

### Q71-A — Is a zone a content bundle, or a geographic window onto a terrain?

⭐ **LEAN: geographic window (`mgmt-1`). Revert `packs-3`'s bundle.**
A zone is `{ ZoneId, Bounds, DataPath }`; roads and obstacles are world content that crosses zone
boundaries and is not owned by any zone.
**Blast radius:** retires `ZoneDefinitionDto`, the embedded `Zones` section, `ZoneMembership`, and the
`ScenarioMergeCore` one-`Zones`-source rule (⑩). **Reuse-vs-build:** pure reuse — `ZoneSpec` is
already specified in `mgmt-1` §11.2.
**What would change the lean:** a consumer that genuinely needs per-zone road/obstacle *scoping*
rather than spatial windowing. None found (⑫).

### Q71-B — Where does terrain identity live?

⭐ **LEAN: it already lives in the orchestrator's global context — `SceneId` (⑦), *"the map/terrain
identifier"*.** No ECS component, no new field. A zone's identity is then the pair
**(terrain, zoneId)** — the terrain-db id is part of *which zone this is*, never a property carried
inside it, which is why ⑥ was dead.
**Build:** populate `SceneId` (today written `string.Empty` at `AssetInventoryProcessManager.cs:215`).
**What would change the lean:** if a single exercise must host two terrains at once — then terrain
becomes per-zone after all. Believed false; worth one sentence of confirmation.

### Q71-C — How does a scenario reference the zones it needs?

⭐ **LEAN: a foreign key in the scenario header**, exactly as the `packs-3` talk concluded —
`ScenarioHeader(SubsystemType, SchemaVersion, ZoneIds)` — resolved to zone artefacts by name.
Loading them reuses the **same executive code** as the runtime zone load, invoked during
`LoadingEdit`/`LoadingLive` rather than via `PrepareZone`.
**Alternative considered:** keep zones wholly out of the scenario and drive them only from the
orchestrator context. Rejected — the scenario is what knows *where the action is*.

### Q71-D — How are roads and obstacles represented for authoring?

⭐ **LEAN: as ordinary entities, reusing what exists.** `RoutePlan` and `EditablePolyline` are already
entity components — replicated (`EditablePolylineTranslator`), operator-editable (context menu), and
already persisted (`scenarios/hill-attack/scenario.json` carries an `EditablePolyline`). Obstacles
keep `SimTransform`+`PhysicsCollider` and **lose** `ZoneMembership`.
**Cost to state plainly:** obstacles become individually persisted entities, so a scenario grows by
the obstacle count — the same behaviour as every other entity, and it removes duplication ⑨.
**Reuse-vs-build:** ~all reuse; the only new thing is deciding whether "this polyline is a road"
needs a marker component or is inferred from an existing one. ⛔ **Open inside this sub-question.**

### Q71-E — The asset build: `PrepareTerrainAsset` / `CommitTerrainAsset`

> 🔒 **User's proposal, `2026-09-16`:** *"The entity→asset conversion is basically an asset build and
> as such it would probably need similar cross cluster operation `PrepareTerrainAsset` /
> `CommitTerrainAsset` (cross cluster as we do not know what node handles it — anyone can do its part,
> in distributed manner). Prepare converts the entities into assets and loads the stuff (roads,
> obstacles…) into some separate memory area, Commit then switches pointers to apply (forgetting the
> previous no longer valid ones). Asset preparation might be limited to just some entity kinds like
> routes or obstacles… so the asset kinds involved should be part of `PrepareTerrainAsset`
> parameters."*

⭐ **LEAN: adopt it as stated — a new op pair mirroring `PrepareZone`/`CommitZone`, carrying an
asset-KIND mask.** It fits the existing contract exactly: `PrepareAsync` is already *"must not mutate
ECS"* (⑤), which is precisely "convert into a separate memory area"; `Commit` is main-thread, which is
precisely "switch pointers"; `Abort` already frees staged work. The barrier (④) is built and proven.

```mermaid
sequenceDiagram
  participant OP as Operator
  participant M as ClusterMaster
  participant N as Every node handler
  participant ECS as Node ECS + assets
  OP->>M: build assets (kinds)
  M->>N: PrepareTerrainAsset (txId, kinds)
  Note over N: role x kind filter<br/>not mine -> ACK immediately
  N->>ECS: read authored entities (no mutation)
  N->>N: convert into STAGED asset buffer
  N-->>M: NodeOpCompleted (Ready)
  Note over M: barrier - waits for ALL
  M->>N: CommitTerrainAsset (txId)
  N->>ECS: swap active pointers, free previous
  N-->>M: NodeOpCompleted
  Note over M,N: any Prepare fails -> AbortTransaction<br/>staged freed, no ECS mutation
```

*What the picture shows that the prose hid:* the **kind × role filter** is what makes "anyone does its
part" safe — a node that consumes no road graph ACKs without building one, the same shape
`IgZoneDummyHandler` already uses, so no node can stall the round.

**Three things inside this that are genuinely open:**

| | question | ⭐ lean |
|---|---|---|
| **E1** | does **each node convert locally**, or does **one node build and distribute** the artefact? | ⭐ **convert locally.** The authored entities are replicated, so every node already has the input; and the existing staging/NAS push path is a heavier hammer. ⚠ **Hazard to rail:** conversion must be **deterministic**, or two nodes get different road graphs and diverge with nothing to detect it — the `R-136` failure shape. A conformance rail comparing a hash of the built asset across nodes is the cheap control |
| **E2** | what are the **kinds**, and are they a flags mask? | ⭐ flags mask (`Roads \| Obstacles \| …`), extensible, so one op serves every future asset. Follows the round-out preference |
| **E3** | what **triggers** a build — explicit operator action, or automatically on edit commit? | ⭐ **explicit.** It is heavy; auto-building on every vertex drag would thrash. An explicit "build" affordance also makes the staged/committed distinction visible to the operator |

### Q71-F — What happens to the existing `Zones` section?

⭐ **LEAN: retire it, and do not write a migration shim.** Measured: **no scenario in the repo carries
a `Zones` section at all**, so there is no corpus to migrate. `Deserialize` already skips unknown
sections (§6b format recognition), so an old file with one degrades to "zones ignored" rather than
failing.
**⚠ Separate, unmeasured flag found while investigating:** `scenarios/hill-attack/scenario.json` uses
lowercase `entities`/`header` while `ScenarioMergeCore` indexes `dom["Entities"]`/`dom["Zones"]`
(case-sensitive `JsonNode`). Likely old-format assets vs the new writer — **needs its own check**, and
it is not this document's question.

### Q71-G — The spatial coupling: a rebuilt asset that overlaps a loaded zone

> The user's framing: a built asset *"is likely not related to zones much. Maybe just when the assets
> falls into the already loaded zone(s) — then their asset… might need reloading."*

⭐ **LEAN: defer, and do not design it until A–E are settled.** It is a *consequence* of the split,
not a mechanism of its own: once zones window world content, `CommitTerrainAsset` simply needs to
re-window whatever zones are currently loaded. Whether that is a fresh `PrepareZone` round or an
internal re-slice is a detail that A–E decide.

---

## 4. Sequencing — if the leans are approved

| # | step | why here |
|---|---|---|
| 1 | fold the **terrain/zone split** (A+B) into `mgmt-1` §11 and mark `packs-3` §2.B/§2.C/§2.E **SUPERSEDED** in place; add the reciprocal `related-designs` links | the documents must stop disagreeing before anything is built against either |
| 2 | correct `DESIGN_Distributed_Scenario_Persistence.md` §6a (zones are **not** brain-owned) + its line 511, and the `ScenarioMergeCore` I4 message | ⑩ — a throw citing a rule the wiring never followed |
| 3 | retire the embedded `Zones` bundle (F) + `ZoneMembership`; obstacles become plain entities (D) | closes the round-trip duplication ⑨ |
| 4 | the asset build (E) — **its own design doc with the UML**, not a batch | new mechanism, new wire ops |
| 5 | zone reference by id (C) + `PrepareZone`/`CommitZone` made real | the delivery half, already specified in `mgmt-1` §11 |
| ⛔ | G | only after 1–5 |

⚠ **`CE-277(a)` — *"give CGF a real `IZoneManagerService`"* — should be CLOSED, not built**: it
would add a zone service to the one role with no zone consumer, and the class it would instantiate is
what step 3 retires.
