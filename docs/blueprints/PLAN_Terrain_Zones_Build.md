<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and a design disagree, the DESIGN wins.
updated: 2026-09-17
current-answer: §2 is the stage/task table (the dispatchable unit), §3 is the UNDER-SPECIFIED register —
  read §3 BEFORE picking up any task whose id appears in it.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: none.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — THE owning design for everything in stages A–F.
  - docs/blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — the WHY and the
    decision record; read it when a task's rationale is unclear.
  - docs/designs/routes-1/ROUTES1-DESIGN.md — owns routes (§5, §16). Stage G only.
  - docs/designs/mgmt-1/DESIGN.md — §11 owns the PrepareZone/CommitZone 2PC protocol.
  - docs/DESIGN_Distributed_Scenario_Persistence.md — owns the save gate the new entities pass through.
-->

# PLAN — **Terrain, zones and the asset build: the build breakdown**

> ⛔ **This file carries NO design content.** Each task names its **owning chapter**; the implementer
> reads that chapter, not a summary of it. ⭐ Tasks are written so a backend agent can pick one up with
> the design open beside it.

## 0. How to use this

| | |
|---|---|
| ⭐⭐ **before starting ANY task** | check §3 — if the task id is listed there, part of it has **no ground to stand on** and must be resolved with the user first |
| ⭐⭐ **T-1 first** (`R-142`) | every task names the feature suite to run BEFORE writing code. ⛔ Do not open a new rail class where a suite exists |
| ⭐ **ids** | ⛔ the coordinator allocated NONE. The implementing session numbers these into the tracker and states the ids in its report (rule 3/5) |
| ⭐ **stage order** | A → B → C → D/E → F. **G is independent** and may run in parallel by a different lane |

---

## 1. WHAT THIS BUILDS, IN ONE PARAGRAPH

Zones become ordinary authored **entities** (a `TkbType`, an `EditablePolyline`, a `SimTransform`) that
are saved to the scenario like anything else; a **node-local marker** records what each node has loaded
and a **footprint hash** tells it when that went stale; a **shared loader** runs on every host, invoked
two ways (locally during scenario load, and via a **2PC round** at runtime); the **terrain loader** takes
over road-network loading from the retiring zone bundle; and the **UI** shows per-zone state on the map
and in a zones view. ⛔ **Terrain tiles themselves stay FAKED by ruling** — see design §6/§7.

---

## 2. THE STAGES

### Stage A — Preconditions *(nothing below works correctly until these land)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **A1** | Fix `TacticalAreaGizmo` to add the `SimTransform` origin, and its `EmitPickSegments` origin with it | `hill-attack` entity `5525100c` renders **once**, at `origin + Points`; a click on the drawn outline selects it. ⭐ Red-proof first: assert the two gizmos' emitted vertices coincide | design **§2.2**; defect **BP-517** |
| **A2** | Correct `EditablePolyline`'s header: points are RELATIVE to `SimTransform` | the header no longer claims "world-space"; no code change | design **§2.2**; **BP-517** |
| **A3** | Make the navigation consumers re-read `ZoneEnvironmentData` per tick instead of caching the blob in a ctor field | a blob swapped after module construction is observed by `PathfindingSolverSystem`, `NavigationSolverModule` and `EngineBackedNavigationModule`. ⭐ Rail: swap the singleton mid-run, assert a path changes | design **§5.4** (R2) ⚠ **see §3-U1** |
| **A4** | Fix `ClusterOpIntents.cs:133`'s comment — `LoadZoneIntent` is **not** consumed by `ClusterMaster` | comment matches reality (A4 is doc-only; C4 makes it true instead) | design **§4** (dead edge 1) |

### Stage B — The entity model

| # | task | success condition | owning chapter |
|---|---|---|---|
| **B1** | Allocate **ONE** new `TkbType`: the **zone** kind, beside `8801/8802/8803` | the value exists in `TkbEntityTypes`, is not a reused id, and the choice is recorded. ⚠ `R-42`: it reaches replays and saved scenarios. ⛔ **No static-obstacle kind yet** — ruled `2026-09-17`: static obstacles are handled as dynamic for now | design **§2.1** ⚠ **see §3-U2** |
| **B2** | Add `TerrainAssetLoadState { LoadPhase Phase, ulong SourceHash }` with `[DataPolicy(NoScenario \| NoReplay)]` | the component exists, is registered on every ECS host, and a save/replay round-trip proves it is **absent** from both outputs | design **§2** (new-type table), **§9.1** |
| **B3** | Implement the footprint hash `hash(SimTransform ⊕ Points)` as ONE shared helper | moving a zone changes the hash; reshaping changes it; neither requires any writer to cooperate. ⭐ Rail both cases | design **§9.7 ③c** |
| **B4** | Zone entities save/load through the ordinary gate | a scenario containing a zone round-trips: same footprint, same `TkbType`, and **no** `TerrainAssetLoadState` in the file | design **§2**; `DESIGN_Distributed_Scenario_Persistence` §6 |
| **B5** | Introduce the **terrain entity**: ONE ordinary entity per scenario carrying the terrain identity + its asset references (road networks first) | a scenario round-trips the terrain entity through the **ordinary save gate** — ⛔ no new persistence plumbing. The loader reads terrain identity from it, not from `Header`. ⚠ State and enforce the duplicate rule (what happens if two exist) | design **§2.1d** ⚠ **resolves §3-U4** |

### Stage C — The loader and the two invocation paths

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** | `TerrainLoadService` with `EnsureLoaded(view, entity)` / `EnsureAllLoaded(view)`, idempotent, hash-checked | calling twice with no edit does nothing the second time; calling after a move/reshape rebuilds; both provable without a cluster | design **§3**, **§9.7** |
| **C2** | `ZoneTileLoader` as an **announcing fake** | it logs its stub-ness on **every** round, and the capability manifest advertises **no** real terrain capability | design **§5.6**, **§6** ⚠ **see §3-U3** |
| **C3** | Scenario-load invocation: the load handlers call `EnsureAllLoaded` **locally**, not via a NodeOp | loading a scenario with N zones leaves N markers `Loaded` on every participating node, with **no** nested 2PC | design **§3.2** |
| **C4** | Give `LoadZoneIntent` a consumer that starts a `PrepareZone`/`CommitZone` round | the dangling publish in `ClusterOpMasterTranslator` reaches a handler; one op **per zone** (not a multi-zone payload) | design **§9.2 U4**, **§9.3**; `mgmt-1` **§11.1** |
| **C5** | Introduce the **terrain loader**, even as a near-stub: it reads the terrain entity (B5) and takes over road-network loading from the retiring zone bundle | loading a scenario whose terrain entity references a road network yields a populated `ZoneEnvironmentData` **without** any `Zones` section. ⭐ This is what makes Stage F safe | design **§2.1d** ⚠ **resolves §3-U8** |

### Stage D — The cluster ops

| # | task | success condition | owning chapter |
|---|---|---|---|
| **D1** | Allocate `ClusterOpType.BuildTerrainAsset` and the two `NodeOpType` values, in **both** wire enums | values allocated without reusing the undocumented `NodeOpType` gaps at 6/17/18/19; both enums agree | design **§6** (enum note) ⚠ **see §3-U2** |
| **D2** | Purpose-built payload DTOs for the zone op and the build op | the zone op no longer reuses `ArchivePayloadDto` stuffing `ExerciseId` into `ZoneId` | design **§8.1 ⑯** |
| **D3** | ONE `TerrainAssetHandler` implementing `IClusterStateHandler`, registered on **every** ECS host via a shared registrar mirroring `SerializeLocalRegistrar` | the same class is registered by all hosts; **the ACK is unconditional**; a host with no loader composed ACKs without doing work | design **§8.3** (N6), **§3.1** |
| **D4** | Retire `IgZoneDummyHandler` | deleted; IG still ACKs both ops via the shared handler and never stalls a round | design **§8.3** |
| **D5** | Terrain-identity check: a host verifies it holds the scenario's `SceneId` and **fails loudly** if not | a mismatch produces a **failed** op result naming the node — ⛔ never a silent pass | design **§8.3** (N4) ⚠ **see §3-U4** |

### Stage E — The UI

| # | task | success condition | owning chapter |
|---|---|---|---|
| **E1** | Zone gizmo renders load state: `LineStyle.Solid` = loaded, `Dashed` = stale, colour for failed | a zone drawn, then reshaped, visibly changes stroke without a reload; the gizmo reads the **local** marker | design **§9.2 U1**, **§9.1** |
| **E2** | Context-menu item "Load zone" on a zone entity, via `SharedContextMenuPopulator.PopulateEntityMenu` | the item appears for the zone `TkbType`, is **always enabled** (never gated on local freshness), and publishes the **cluster** op | design **§9.2 U2**, **§9.6**, **§9.7 ③b** |
| **E3** | A **zones view** contributed to the existing `DetailsWindow`, offered when map background is the context | one row per zone with name + state + per-row Load, and a "Load all stale" header action; progress sourced from `_pendingTransactions`, ⛔ **not** `HasInFlightTransaction` | design **§9.5**, **§9.4** ⚠ **see §3-U5** |
| **E4** | Cluster-panel control to trigger a terrain-asset build | the button publishes `BuildTerrainAsset`; per-node outcome is visible (OK / Failed), never one global OK | design **§9.2 U3**, **§8.3** (N5) |

| **E5** | Move area/tactical-drawing authoring out of IG-only into the shared **Map2D role** feature set, so zones can be drawn wherever that role runs | the same authoring path (`CMD_START_AUTHORING` → point-sequence → commit, and the edit tool) is reachable on **every host carrying the Map2D role**, editor included — ⛔ not an IG-private code path | design **§2.1**; `DESIGN_Node_Roles_And_Policies` (Map2D role) ⚠ **resolves §3-U6** |

### Stage F — Retirement *(⚠ `HN-037`: the TEST surface is the work, not the deletion)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **F1** | Retire `ZoneDefinitionDto`, the embedded `Zones` scenario section, `ZoneMembership`, and `ZoneManagerService`'s DTO half | the types are gone; no scenario writes a `Zones` section; an **old** file with one loads with zones ignored rather than failing | design **§6**; `DESIGN_Distributed_Scenario_Persistence` **§6a** |
| **F2** | Remove the `ScenarioMergeCore` I4 one-`Zones`-source guard | the guard and its "brain-only" message are gone with the section it policed | design **§6**; persistence design **§4b** |
| **F3** | Re-home or delete the **five** suites that assert the retiring behaviour: `ZoneManagerServiceTests`, `ZoneScenarioLoadIntegrationTests`, `ZoneEditorPanelTests`, `ScenarioFileServiceZoneTests`, and the two `SpyZoneManagerService` doubles | every claim is either **re-homed** to the new path or **deleted with a stated reason**; ⛔ no claim silently disappears | design **§6** |
| **F4** | Close `CE-277(a)` ("give CGF a real `IZoneManagerService`") as **will-not-build** | the tracker row records why: the brain has no zone consumer, and the class it would instantiate is what F1 retires | persistence design **§6a** |

### Stage G — Routes *(INDEPENDENT of A–F; different owning design)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **G1** | Add a `RoutePlanTranslator` mirroring `EditablePolylineTranslator` | a scenario with a shared route round-trips **with its waypoints**; a vehicle's `PersonalRouteRef` resolves to a route that is **not empty**. ⚠ must go through `Mutate()` (so `Version` stays correct) and must round-trip `ExtensionJson` | `ROUTES1-DESIGN` **§16**, **§4**; defect **BP-518** |

---

## 3. THE UNDER-SPECIFIED REGISTER — **5 of 8 RESOLVED `2026-09-17`**

> ⭐⭐ These were places where the design **could not say what success looks like**, so a task written
> against them would have been unfalsifiable. ⭐ Five are now ruled; the rest are listed with what remains.

### 3.1 ✅ RESOLVED by user ruling `2026-09-17`

| # | was | ✅ the ruling |
|---|---|---|
| **U3** | what a TILE is has no definition | 🔒 *"tiles are implementation detail, used as example of possible implementation, not a concept to implement now, **fake is ok**"* ⇒ ⭐ **C2's success condition is the ANNOUNCEMENT, and that is complete** — not a placeholder for a better one |
| **U4** | terrain identity has nothing to compare against | 🔒 terrain becomes **an entity** carrying its identity and its asset references ⇒ ⭐ **new task B5.** ⚠ **Lean recorded: an ORDINARY entity, not an ECS singleton** — 📐 the scenario serializer writes `$meta` + `Header` + `Entities` and **no singletons**, so an entity persists through the existing gate for free while a singleton would need new plumbing |
| **U6** | zone creation is IG-only | 🔒 *"area authoring should be part of unified **Map2d role** features, as well as authoring the tactical drawings, **nothing of it should be IG host only**"* ⇒ ⭐ **new task E5** |
| **U7** | the static-obstacle bake has nothing to call | 🔒 *"static can be handled as dynamic for now… optimizations for static ones can come later"* ⇒ ⭐ **B1 allocates NO static kind**, and no bake is built. ⛔ Do not allocate a permanent wire id (`R-42`) for behaviour that is not being implemented |
| **U8** | retiring the zone bundle strands the road network | 🔒 *"yes we need to introduce a terrain loader (even if not doing anything useful now)"* ⇒ ⭐ **new task C5**, and it is what makes Stage F safe |

### 3.2 ⚠ STILL OPEN

| # | blocks | what is missing | what would settle it |
|---|---|---|---|
| **U1** | **A3** | the fix direction rests on an **unmeasured** fact: whether any navigation module runs on a background thread where `DataPolicy` constrains singleton access. If it does, the per-tick singleton read is illegal and a holder object is required instead | ⭐ **an implementer measurement, not a user decision** — design §5.4 already names it as *"what would flip it"* |
| **U2** | **B1**, **D1** | ⭐⭐ **SHRUNK — this is a NAMING call only.** 📐 Measured `2026-09-17`: the `NodeOpType` gaps at **6/17/18/19 are absent from the AUTHORITATIVE NED enum too** (`OrchestrationMessages.cs`), so they are historical holes, **not reservations** — and the FDP copy is a mirror whose *"integer values must remain identical to the NED counterpart (verified by unit tests)"*. ⇒ allocating new values is safe | ⭐ **proposed, awaiting one word:** `TacGraphic_Zone = 8804`; `NodeOpType.PrepareTerrainAsset = 29` / `CommitTerrainAsset = 30` *(clearly-new values rather than filling a hole, so no future reader has to wonder whether the hole meant something)*; `ClusterOpType.BuildTerrainAsset = 17` |
| **U5** | **E3** | the details shell has no *"map background selected"* CONTEXT. `PopulateEmptyMapMenu` proves empty space is a click target for a **menu**, but selection-as-context is new | ⭐ **an implementer design call inside `IDetailsContextSource`** — small, and E3 cannot be asserted until it exists |

⇒ ⭐ **U1 and U5 are implementer measurements/calls, not user decisions.** ⛔ **U2 is the only one still
needing the user, and it is one word.**

## 4. What this plan deliberately does NOT contain

⛔ The tile format, the cache key, eviction, and what a commit swaps *(design §7 — postponed by ruling)*.
⛔ The road-network-as-entity extension *(design §2.1d — built only when a selection requirement appears)*.
⛔ The route model *(owned by `ROUTES1-DESIGN` §5; only its persistence gap is here, as G1)*.
⛔ Fixing `HasInFlightTransaction` *(design §9.4 — a pre-existing cluster-panel defect, out of scope)*.
