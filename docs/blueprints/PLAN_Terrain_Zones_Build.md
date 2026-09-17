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
| **B1** | Allocate the new `TkbType` values: a **zone** kind and a **static-obstacle** kind, beside `8801/8802/8803` | the values exist in `TkbEntityTypes`, are **not** reused ids, and the choice is recorded. ⚠ `R-42`: they reach replays and saved scenarios | design **§2.1**, **§2.1c** ⚠ **see §3-U2** |
| **B2** | Add `TerrainAssetLoadState { LoadPhase Phase, ulong SourceHash }` with `[DataPolicy(NoScenario \| NoReplay)]` | the component exists, is registered on every ECS host, and a save/replay round-trip proves it is **absent** from both outputs | design **§2** (new-type table), **§9.1** |
| **B3** | Implement the footprint hash `hash(SimTransform ⊕ Points)` as ONE shared helper | moving a zone changes the hash; reshaping changes it; neither requires any writer to cooperate. ⭐ Rail both cases | design **§9.7 ③c** |
| **B4** | Zone entities save/load through the ordinary gate | a scenario containing a zone round-trips: same footprint, same `TkbType`, and **no** `TerrainAssetLoadState` in the file | design **§2**; `DESIGN_Distributed_Scenario_Persistence` §6 |

### Stage C — The loader and the two invocation paths

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** | `TerrainLoadService` with `EnsureLoaded(view, entity)` / `EnsureAllLoaded(view)`, idempotent, hash-checked | calling twice with no edit does nothing the second time; calling after a move/reshape rebuilds; both provable without a cluster | design **§3**, **§9.7** |
| **C2** | `ZoneTileLoader` as an **announcing fake** | it logs its stub-ness on **every** round, and the capability manifest advertises **no** real terrain capability | design **§5.6**, **§6** ⚠ **see §3-U3** |
| **C3** | Scenario-load invocation: the load handlers call `EnsureAllLoaded` **locally**, not via a NodeOp | loading a scenario with N zones leaves N markers `Loaded` on every participating node, with **no** nested 2PC | design **§3.2** |
| **C4** | Give `LoadZoneIntent` a consumer that starts a `PrepareZone`/`CommitZone` round | the dangling publish in `ClusterOpMasterTranslator` reaches a handler; one op **per zone** (not a multi-zone payload) | design **§9.2 U4**, **§9.3**; `mgmt-1` **§11.1** |

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

## 3. ⛔ UNDER-SPECIFIED — **where a success condition has no ground**

> ⭐⭐ These are **not** "hard tasks". They are places where the design **cannot yet say what success looks
> like**, so a task written against them would be unfalsifiable. ⛔ Resolve with the user before building.

| # | blocks | what is missing | what would settle it |
|---|---|---|---|
| **U1** | **A3** | ⚠ the fix direction depends on an **unmeasured** fact: whether any navigation module runs on a background thread where `DataPolicy` constrains singleton access. If it does, the per-tick singleton read is illegal and a holder object is required instead | measure the module's threading/`DataPolicy` context. §5.4 names this explicitly as *"what would flip it"* |
| **U2** | **B1**, **D1** | **numbering and naming are the user's call** — the `TkbType` values and the two `NodeOpType` values. `R-42` makes them permanent | a user decision. ⛔ Do not invent ids that reach replays and saved scenarios |
| **U3** | **C2** | 🔴 **what a TILE IS has no definition** — not its format, its geographic key, its granularity, its eviction, nor what a commit "swaps". ⇒ *"the tile loaded correctly"* **cannot be asserted**; only *"the fake announced itself"* can. This is POSTPONED BY RULING, not an oversight | design **§7**. A success condition beyond the announcement requires the postponed design |
| **U4** | **D5** | 🔴 **the terrain-identity check has nothing to compare against on some hosts.** `SceneId` is written `string.Empty` (`AssetInventoryProcessManager.cs:215`), and Stride bakes its navmesh from **scene geometry** with no terrain id attached ⇒ "does this host hold the scenario's terrain?" is currently unanswerable there | decide what identifies a loaded terrain per host, and populate `SceneId`. ⚠ Until then D5 can only be built for hosts that *have* an id |
| **U5** | **E3** | ⚠ the details shell has **no "map background selected" context** today. `PopulateEmptyMapMenu` proves empty space is a click target for a **menu**, but selection-as-context is new | specify how `IDetailsContextSource` reports that context. ⭐ Small, but it is a new seam and unspecified |
| **U6** | **E1/E2** *(zone AUTHORING)* | 🔴 **how an operator CREATES a zone is not specified.** Area authoring exists (`CMD_START_AUTHORING` → `PointSequenceTool` → commit, plus `ActivateAreaEditingTool`) but is **IG-only**; the zones view lives in the editor's details shell. ⇒ no task can assert "the operator drew a zone" on the editor | decide whether zone creation is IG-only, or whether the area tool is shared to the editor |
| **U7** | **Stage C/D generally** | ⚠ **the static-obstacle bake has no implementation to call.** §2.1c rules that buildings get baked into navmesh + physics; no bake-from-entities exists (`StrideNavmeshBaker` bakes **scene geometry**) ⇒ the build op is designed for content that cannot yet be produced | this is deliberate (§2.1c: *"the op pair is designed for buildings even though slice 1 builds none"*) — ⭐ **record it, do not build it**. Only becomes a task when a building kind exists |
| **U8** | **F1** | ⚠ the road network currently loads via `ZoneManagerService.LoadZones` from the zone's `RoadNetworkPath`. §2.1d rules the **terrain loader** takes that job — but **no terrain loader exists yet** | either build the terrain loader as part of F1, or accept that retiring the zone bundle **removes the road network's only load trigger** until it exists. ⛔ Do not do F1 without deciding this |

⚠⚠ **U8 is the one that can break a working feature.** Everything else is a gap in the new work; U8 is a
**regression risk in the old**.

---

## 4. What this plan deliberately does NOT contain

⛔ The tile format, the cache key, eviction, and what a commit swaps *(design §7 — postponed by ruling)*.
⛔ The road-network-as-entity extension *(design §2.1d — built only when a selection requirement appears)*.
⛔ The route model *(owned by `ROUTES1-DESIGN` §5; only its persistence gap is here, as G1)*.
⛔ Fixing `HasInFlightTransaction` *(design §9.4 — a pre-existing cluster-panel defect, out of scope)*.
