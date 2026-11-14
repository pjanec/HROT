<!--STATUS
state: LIVE
build-state: ✅ COMPLETE 2026-09-18 — all 8 stages shipped across 4 batches (A+B+G / C1,C2,C5-C7 /
  C3,C4,D,F / C8,E,H). ⛔ NOT a design: every task REFERENCES its owning chapter and restates nothing.
  If this file and a design disagree, the DESIGN wins.
updated: 2026-09-18
current-answer: §2 is the stage/task table, ALL DONE. §3's register is fully closed (9 of 9). §5 records
  the as-built batch split. ⭐⭐ The programme did NOT end with a working end-to-end zone load, and the
  reason is BP-550 — read the closing note at the end of §3 before planning follow-on work.
stale-below: nothing — but see known-rot: three task rows described work that could not be built as
  written, and each now carries its as-built correction inline.
known-rot: THREE of my own task/register rows were measured WRONG by the build and are corrected in
  place, each pointing at the owning design section: E5 + U6 (the "Map2D role" framing is unbuildable —
  the role selects no systems and the editor is outside it by ruling; design §10.7), U9 (BOTH offered
  options are impossible — nothing accepts a path, and a root cannot cross the wire; design §10.8), and
  §6's retirement test-surface list (wrong count AND composition; design §10.6).
known-conflict: none.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — THE owning design for everything in stages A–F.
  - docs/blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — the WHY and the
    decision record; read it when a task's rationale is unclear.
  - docs/designs/routes-1/ROUTES1-DESIGN.md — owns routes (§5, §16). Stage G only.
  - docs/designs/mgmt-1/DESIGN.md — §11 owns the PrepareZone/CommitZone 2PC protocol.
  - docs/DESIGN_Distributed_Scenario_Persistence.md — owns the save gate the new entities pass through.
  - docs/blueprints/Architect_Question_57_Cgf_Authoring_Packaging.md — owns the recipe/create registry
    Stage H wires. Read it before touching H1-H3: it already ruled "no new registry, no new assembly".
  - docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md — owns the New-Asset picker shell Stage H feeds.
-->

# PLAN — **Terrain, zones and the asset build: the build breakdown**

> ⛔ **This file carries NO design content.** Each task names its **owning chapter**; the implementer
> reads that chapter, not a summary of it. ⭐ Tasks are written so a backend agent can pick one up with
> the design open beside it.

## 0. How to use this

| | |
|---|---|
| ⭐⭐ **before starting ANY task** | check §3. ✅ **Nothing here needs the USER any more** *(U2 confirmed `2026-09-17`)* — the three remaining rows are measurements/design calls to make **inside** the task and **state in the report** |
| ⭐⭐ **T-1 first** (`R-142`) | every task names the feature suite to run BEFORE writing code. ⛔ Do not open a new rail class where a suite exists |
| ⭐ **ids** | ⛔ the coordinator allocated NONE. The implementing session numbers these into the tracker and states the ids in its report (rule 3/5) |
| ⭐ **stage order** | A → B → C → D/E → F. **G is independent**; **H depends only on B5** |
| ⭐⭐ **dispatch grouping** | §5 — the four batches these stages are handed off in |

---

## 1. WHAT THIS BUILDS, IN ONE PARAGRAPH

Zones become ordinary authored **entities** (a `TkbType`, an `EditablePolyline`, a `SimTransform`) that
are saved to the scenario like anything else; a **node-local marker** records what each node has loaded
and a **footprint hash** tells it when that went stale; a **shared loader** runs on every host, invoked
two ways (locally during scenario load, and via a **2PC round** at runtime); the **terrain loader** takes
over road-network loading from the retiring zone bundle; and the **UI** shows per-zone state on the map
and in a zones view; finally the **new-scenario recipe** path is wired so a fresh scenario acquires its
terrain and TKB names by **carrying them from a seed**, instead of inheriting whatever the host last
loaded. ⛔ **Terrain tiles themselves stay FAKED by ruling** — see design §6/§7.

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
| **B1** | Allocate **ONE** new `TkbType`: **`TerrainZone = 8804`** 🔒 *(name + value ruled `2026-09-17`)*, beside `8801/8802/8803` | the value exists in `TkbEntityTypes` as **8804**, is not a reused id, and the choice is recorded. ⚠ `R-42`: it reaches replays and saved scenarios. ⛔ **No static-obstacle kind yet** — ruled `2026-09-17`: static obstacles are handled as dynamic for now | design **§2.1** ✅ **value RULED — §3-U2** |
| **B2** | Add `TerrainAssetLoadState { LoadPhase Phase, ulong SourceHash }` with `[DataPolicy(NoScenario \| NoReplay)]` | the component exists, is registered on every ECS host, and a save/replay round-trip proves it is **absent** from both outputs | design **§2** (new-type table), **§9.1** |
| **B3** | Implement the footprint hash `hash(SimTransform ⊕ Points)` as ONE shared helper | moving a zone changes the hash; reshaping changes it; neither requires any writer to cooperate. ⭐ Rail both cases | design **§9.7 ③c** |
| **B4** | Zone entities save/load through the ordinary gate | a scenario containing a zone round-trips: same footprint, same `TkbType`, and **no** `TerrainAssetLoadState` in the file | design **§2**; `DESIGN_Distributed_Scenario_Persistence` §6 |
| **B5** | Carry the terrain **NAME** (+ optional subfolder path) in the scenario **header, beside `Header.TkbName`** | a scenario round-trips the terrain name and **nothing else** — ⛔ **no asset list, no road-network paths, no per-entity terrain component.** ⚠ A scenario with **no** terrain name still loads (graceful fallback, like `TkbName`) | design **§2.1e ①** ⚠ **resolves §3-U4** |
| **B6** | Define the **terrain definition file** — a JSON asset resolved from that name, listing the terrain's own content (road network(s) first) — and an **ECS singleton** holding the PARSED definition | the schema exists with a versioned root; a definition listing one road network parses into the singleton; a save/replay round-trip proves the singleton is **absent** from both outputs (it is re-derived, never persisted). ⛔ **Authored/shipped as an asset — not editable from the scenario editor** | design **§2.1e ①a ②**; **§2.1a** |

### Stage C — The loader and the two invocation paths

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** | `TerrainLoadService` with `EnsureLoaded(view, entity)` / `EnsureAllLoaded(view)`, idempotent, hash-checked | calling twice with no edit does nothing the second time; calling after a move/reshape rebuilds; both provable without a cluster | design **§3**, **§9.7** |
| **C2** | `ZoneTileLoader` as an **announcing fake** | it logs its stub-ness on **every** round, and the capability manifest advertises **no** real terrain capability | design **§5.6**, **§6** ⚠ **see §3-U3** |
| **C3** | Scenario-load invocation: the load handlers call `EnsureAllLoaded` **locally**, not via a NodeOp | loading a scenario with N zones leaves N markers `Loaded` on every participating node, with **no** nested 2PC | design **§3.2** |
| **C4** | Give `LoadZoneIntent` a consumer that starts a `PrepareZone`/`CommitZone` round | the dangling publish in `ClusterOpMasterTranslator` reaches a handler; one op **per zone** (not a multi-zone payload) | design **§9.2 U4**, **§9.3**; `mgmt-1` **§11.1** |
| **C5** | Introduce the **terrain loader** as an `IClusterStateHandler` **mirroring `TkbLoadClusterStateHandler` field for field** — name from the locally staged header, artifact from local staging, intercepts `PrepareLive`/`PrepareEdit`, runs **before** the scenario handler, **differential cache keyed on `(terrain name, file timestamp)`**, registered **UNCONDITIONALLY on every ECS host** | loading a scenario whose named terrain **definition** lists a road network yields a populated `ZoneEnvironmentData` **without** any `Zones` section, **on a pure MuscleGround node too**; re-loading the same scenario with an unchanged file does **no** re-ingestion (the cache key proves idempotency). ⛔⛔ **The rail that matters: a node with NO authoring deps still loads terrain** — `NodeBootstrapper.cs:316-318`, see §2.1e ④ | design **§2.1e ②a ③ ④**, **§2.1d** ⚠ **resolves §3-U8** |
| **C6** | ⭐⭐ **BLOB LIFETIME — inherited from batch ①'s `U1`.** The commit/swap path must keep the PREVIOUS `RoadNetworkBlob` alive until no background reader can still be inside it | publishing a new graph while the 10 Hz `SlowBackground` solver is mid-traversal does **not** free the arrays it is walking. ⭐ Rail: swap under a reader and assert no use-after-free / no torn read. ⛔ **`RoadNetworkHolder`'s volatile write makes the REFERENCE atomic; it says nothing about the old blob's memory** — batch ① said so explicitly and deferred it here | design **§5.4** *(as-built, "two things this did NOT solve" ①)* |
| **C7** | ⭐⭐ **PASS THE HOLDER — the other half of `U1`.** Whatever composes `NavigationSolverModule` must hand it a `RoadNetworkHolder`, and the loader must publish into it | a host composing the solver **observes a reload**; ⛔ a host that composes it without a holder is a **startup failure or a loud warning**, never a silent never-reloads. ⚠ `NavigationSolverModule` has **zero production constructions** today (test sites only) — ⭐ that is why this is cheap now and expensive after role-based composition switches it on | design **§5.4** *(as-built, ②)*; **§2.1e ④** |
| **C8** | 🔒 **RULED `2026-09-17` (user): IG and CGF COMPOSE A REAL TERRAIN LOADER** — not a scoped-down identity check | `TerrainLoadClusterStateHandler` is registered on **IG and CGF** as it already is on SimHost *(one call site each, mirroring `NodeBootstrapper`)*, and a zone op against a terrain-named scenario **succeeds** on all three rather than failing `D5`'s identity check. ⭐ Rail per host. ⛔ **Ships BEFORE or WITH Stage E** — `E2`'s context menu is what first issues the op, and `BP-537`'s blast radius stops being nil at that moment | design **§8.3** (N4), **§2.1e ④**, **§10.5**; defect **BP-537** |

### Stage D — The cluster ops

| # | task | success condition | owning chapter |
|---|---|---|---|
| **D1** | Allocate **`ClusterOpType.BuildTerrainAsset = 18`** 🔴 *(was `17` — SUPERSEDED, it collides with the live `SaveScenario = 17`)* and `NodeOpType.PrepareTerrainAsset = 29` / `CommitTerrainAsset = 30`, in **both** wire enums | ✅ **the `NodeOpType` half SHIPPED in batch ② part 1** (both enums, 28 was the previous high). ⚠ **Remaining: the `ClusterOpType` value at 18**, the `NodeOpType` gaps at 6/17/18/19 still **left empty**, and the NED/FDP enums agreeing (the mirror's own unit test proves it) | design **§6** (enum note) ✅ **values RULED — §3-U2** |
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

| **E5** | ✅ **SHIPPED — but NOT as written.** 🔴 **The task was SELF-CONTRADICTING and the build said so:** *"the shared **Map2D role** feature set … editor included"* — 📐 `NodeRole.Map2D` is a component-**OWNERSHIP** policy (`HrotRoleComponentSets.cs:193`) that selects no systems and registers no tools, and **the editor deliberately does not carry it** (`EditorCapabilitiesTests.cs:232` asserts the user ruling *"CGF ∪ SimHost, and NOT ImageGenerator"*) ⇒ **a role gate would have excluded the one host `U6` names.** ⭐ Built as `AreaAuthoringArm`, a shared class holding **no host knowledge**, which is what `U6` actually asked for | the one authoring path is reachable from any host that composes the shared adapter or calls the arm — ⛔ not IG-private, and ⛔ **not role-gated** | design **§10.7** *(AS-BUILT; supersedes the §2.1 framing)* ✅ **closed §3-U6** |

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

### Stage H — New-scenario recipes *(how the terrain + TKB names are ACQUIRED)*

> ⭐⭐⭐ **Depends on B5** *(the header carries the terrain name)*, and on nothing else in A–G. ⛔ **No new
> asset kind, no new registry, no picker work** — `Q57` already ruled that and the shell already ships on
> both hosts. This stage is **three wirings + one deletion**, closing the three measured gaps in
> design §2.1e ⑤c.

| # | task | success condition | owning chapter |
|---|---|---|---|
| **H1** | **Pass the seeds** — `EditorSubsystem`'s `ScenarioNewAssetService` construction moves to the **2-arg** ctor, enumerating `AssetRoots.ScenariosRecipesRoot` *(mirroring `BlueprintEditorBootstrap.DiscoverRecipes()`)* | with two scenarios in `Recipes/Scenarios/`, the New-Asset picker lists **two** Scenario recipes where it lists **one** today. ⭐ Rail: `AvailableRecipes().Count` reflects the directory, and re-reads it **live** *(`AvailableRecipes` is called per-open, not snapshotted)* | design **§2.1e ⑤a ⑤c G1**; `Q57` §"AS BUILT" |
| **H2** | **Resolve seeds against the RECIPES root** — the `FromSeed` branch must not go through a load whose contract is *"from the **scenarios** root"* | creating from a seed that exists **only** in `Recipes/Scenarios/` succeeds; ⛔ a seed name that collides with a scenario of the same name in the scenarios root resolves to the **recipe**. ⭐ Rail both | design **§2.1e ⑤c G2**; `AssetRoots` §16 |
| **H3** | **Scenario offers NO blank template** — `IsBlankTemplate => false`, and `"Empty"` leaves `AvailableRecipes()` | the picker offers no way to mint a scenario with **no terrain and no TKB**; `POST /assets {"kind":"Scenario"}` with no recipe name **refuses** and names the recipes that would have worked. ⚠ `RecipeByName.Resolve` already documents a kind with no blank template as **legitimate** — ⛔ no change needed there | design **§2.1e ⑤c G3** |
| **H4** | **Ship at least one seed** under `Recipes/Scenarios/` whose header carries both a `TkbName` and a terrain name | creating from it yields a scenario whose header round-trips **both** names, with **nothing** re-authoring them. ⭐⭐ **This is the rail that proves the whole stage** — §2.1e ⑤d's point is that the names are *carried*, not chosen | design **§2.1e ⑤b ⑤d**, **§2.1e ①** |

⛔⛔ **Explicitly NOT in this stage** *(each would re-open a ruled question)*: a dedicated recipe asset
format declaring `{terrain, tkb}` *(`Q57`: no new registry/vocabulary; a seed scenario is a superset)* ·
terrain/TKB fields on `NewAssetDialog` *(it is deliberately kind-agnostic)* · an editor-side write to
`ITkbDatabase.ActiveTkbName` *(it has exactly ONE writer, and the cluster re-derives it from the staged
header anyway — design §2.1e ②a)*.

---

## 3. THE UNDER-SPECIFIED REGISTER — **6 of 9 RESOLVED `2026-09-17`**

> ⭐⭐ These were places where the design **could not say what success looks like**, so a task written
> against them would have been unfalsifiable. ⭐ Six are now ruled; the rest are listed with what remains.

### 3.1 ✅ RESOLVED by user ruling `2026-09-17`

| # | was | ✅ the ruling |
|---|---|---|
| **U3** | what a TILE is has no definition | 🔒 *"tiles are implementation detail, used as example of possible implementation, not a concept to implement now, **fake is ok**"* ⇒ ⭐ **C2's success condition is the ANNOUNCEMENT, and that is complete** — not a placeholder for a better one |
| **U4** | terrain identity has nothing to compare against | 🔒 *"Terrain is **not an ordinary entity** as others, it is **by design a singleton concept** and a special one already being handled in a special way (or should be — loading various assets etc). Scenario persistence for such special singleton is not a problem."* ⇒ ⭐ **new tasks B5 + B6.** ⛔⛔ **My 'ordinary entity' lean is RETRACTED.** ⚠ And this does NOT reopen the retired `Zones` section: that was a **content bundle duplicating entities**; terrain is a **global fact**, in the same class as `$meta` and `Header.TkbName`, which §6a already keeps as globals.<br>⭐⭐ **NARROWED `2026-09-17`** 🔒 *"Terrain is an asset referenced **by name** (with optional subfolder path) from a scenario, **nothing more needed in scenario** … terrain asset needs **its own definition file (json)** processed by the loader."* ⇒ ⛔ **the scenario global is a NAME, not an identity+asset-reference block** — a second draft of B5 that carried asset references is **RETRACTED**; the asset list moved to **B6**'s definition file |
| **U6** | zone creation is IG-only | 🔒 *"area authoring should be part of unified **Map2d role** features, as well as authoring the tactical drawings, **nothing of it should be IG host only**"* ⇒ ⭐ **new task E5**.<br>✅ **CLOSED `2026-09-18` — and the ROLE half of the wording was unbuildable.** 📐 `Map2D` selects no systems; the editor is outside it by ruling. ⭐ The user's actual requirement — *"nothing of it should be IG host only"* — is a **duplicate-mechanism** finding *(the path existed twice: ~35 lines editor, ~135 lines IG)*, and collapsing it into `AreaAuthoringArm` satisfies it. ⚠ **The move exposed a LIVE DEFECT:** both bodies hard-coded `TacGraphic_Area` and read the incoming `tkbType` only to compare against `TacGraphic_Route` ⇒ a `CMD_START_AUTHORING` for `TerrainZone` silently authored an `8803` — **a shape appeared, so nothing looked broken, and no stage-`E` zone surface would ever have matched it.** 📄 `BP-545`, design §10.7 |
| **U7** | the static-obstacle bake has nothing to call | 🔒 *"static can be handled as dynamic for now… optimizations for static ones can come later"* ⇒ ⭐ **B1 allocates NO static kind**, and no bake is built. ⛔ Do not allocate a permanent wire id (`R-42`) for behaviour that is not being implemented |
| **U8** | retiring the zone bundle strands the road network | 🔒 *"yes we need to introduce a terrain loader (even if not doing anything useful now)"* ⇒ ⭐ **new task C5**, and it is what makes Stage F safe |
| **U2** | the wire ids had no allocation | ✅ **CONFIRMED by the user `2026-09-17`** — these are the values, and `R-42` makes them permanent: **`TkbType.TerrainZone = 8804`** 🔒 *(deliberately NOT `TacGraphic_*`: a zone is a load directive that happens to be drawn, not a tactical graphic)* · **`NodeOpType.PrepareTerrainAsset = 29`** · **`NodeOpType.CommitTerrainAsset = 30`** · **`ClusterOpType.BuildTerrainAsset = 18`** 🔴 *(CORRECTED `2026-09-17` from `17`, which collides with the live `SaveScenario = 17` — `OrchestrationMessages.cs:42`; found by batch ②, verified at the coordinator)*. 📐 The measurement behind it: the `NodeOpType` gaps at **6/17/18/19 are absent from the AUTHORITATIVE NED enum too** (`OrchestrationMessages.cs`) ⇒ historical holes, **not reservations**; ⛔ **do not fill them** — clearly-new values so no future reader wonders what the hole meant. ⚠ The FDP copy is a mirror whose *"integer values must remain identical to the NED counterpart (verified by unit tests)"* ⇒ **D1 must land in BOTH enums** |

### 3.2 ⚠ STILL OPEN

| # | blocks | what is missing | what would settle it |
|---|---|---|---|
| **U1** | **A3** | the fix direction rests on an **unmeasured** fact: whether any navigation module runs on a background thread where `DataPolicy` constrains singleton access. If it does, the per-tick singleton read is illegal and a holder object is required instead | ⭐ **an implementer measurement, not a user decision** — design §5.4 already names it as *"what would flip it"* |
| **U5** | **E3** | the details shell has no *"map background selected"* CONTEXT. `PopulateEmptyMapMenu` proves empty space is a click target for a **menu**, but selection-as-context is new | ⭐ **an implementer design call inside `IDetailsContextSource`** — small, and E3 cannot be asserted until it exists |
| **U9** | **H2** | ✅ **CLOSED `2026-09-18` — 🔴 and BOTH of the options I offered were IMPOSSIBLE.** 📐 *"return full paths the existing load accepts"*: **nothing anywhere accepts a path** — `OpenForEdit` (`EditorScenarioSession.cs:147`) takes a NAME and publishes a `TransitionStateIntent`. 📐 *"widen the seam with a root-aware load"*: resolution is **per node**, against that node's NAS scenarios root, so a root would cross the wire to nodes where `Recipes/Scenarios` — a local output path — **does not exist**. ⇒ ⭐ **the third shape costs nothing**: seeds are staged into a reserved `Recipes/` subfolder and loaded as the relative name `Recipes/<seed>`; **no seam changed.** ⚠ `AssetRoots`' own header had already flagged the reason — *"Scenario has **no** Assets root — Scenarios are orchestrator/NAS-backed."* | 📄 `BP-549`, design **§10.8** |

⇒ ✅⭐⭐⭐ **ALL NINE ROWS ARE CLOSED `2026-09-18`.** U1, U5 and U9 were implementer measurements made
inside their tasks, and **two of the three falsified the row that posed them** *(U9's both options; U5's
"selection-as-context is new" — it existed end to end and wanted **one enum member**)*.

### 3.3 ⛔⛔⛔ THE CLOSING NOTE — **the programme did NOT end with a working zone load, and the reason is NEW**

📐 **Measured by batch ③ and verified at the coordinator:** `TkbLoadClusterStateHandler` **reads**
`{requestedTkb}.zip` from its local staging root *(`:79`)* — and **nothing anywhere writes one.**
⇒ ⭐⭐⭐ **the loaders this programme built resolve artifacts that NOTHING DISTRIBUTES.** 📄 **`BP-550`.**

⚠⚠ **Why this was invisible until now, stated precisely:** **no scenario in the repository had ever named
a TKB or a terrain.** `H4` shipped the first seed that does ⇒ it **turned a latent gap into a measurable
one**. ⛔ That is not a defect of stage `H`; it is stage `H` doing its job.

| ⭐ what this means for follow-on work | |
|---|---|
| ⭐⭐⭐ **the next programme is ARTIFACT STAGING** — a distribution path for named TKB and terrain assets | ⛔ no stage of THIS plan owned it, and none should have: the gap only exists once something names an artifact |
| ⭐⭐ **`T3` becomes meaningful for this feature for the first time** once it lands | 📌 until then an E2E create-from-seed **throws by design** *(§8.3 `N4`)*, so a green there would have required building the staging mechanism anyway |
| ⚠ **two `U` rows remain scope-deferred, not open questions** | `U3`'s tile lifetime *(tiles are FAKED by ruling — nothing to evict yet)* and `U7`'s static-obstacle bake *(postponed; `B1` deliberately burned no wire id, `R-42`)*. ⭐ Both become real the day tiles are |

## 4. What this plan deliberately does NOT contain

⛔ The tile format, the cache key, eviction, and what a commit swaps *(design §7 — postponed by ruling)*.
⛔ The road-network-as-entity extension *(design §2.1d — built only when a selection requirement appears)*.
⛔ The route model *(owned by `ROUTES1-DESIGN` §5; only its persistence gap is here, as G1)*.
⛔ Fixing `HasInFlightTransaction` *(design §9.4 — a pre-existing cluster-panel defect, out of scope)*.
⛔ Any new recipe/picker machinery *(`Q57` ruled it already exists; Stage H is wiring and content only)*.

---

## 5. ⭐⭐⭐ DISPATCH GROUPING — **four batches, not eight stages and not one lump**

> ⭐ **The grouping rule:** a batch is the smallest set that leaves the tree **coherent** when it lands.
> ⛔ A boundary that leaves two producers alive for one job, or a retirement without its replacement, is
> the wrong boundary however convenient its size *(`R-132`)*.

| batch | stages | tasks | ⭐ why THIS boundary |
|---|---|---|---|
| **① FOUNDATIONS** | **A + B + G** | 11 | ⭐⭐ Everything is **locally verifiable — no cluster, no loader.** A is a precondition for all of B *(the gizmo double-render would make every later visual check lie)*; B is pure model; **G is independent** and rides along because this batch is the lightest. ⭐ Lands the four permanent wire ids *(`R-42`)* early, so nothing downstream guesses them |
| **② THE LOADER AND THE OPS** | **C + D + F** | 16 | ⭐⭐⭐ **The load-bearing batch, and F is why it is one batch.** The terrain loader (C5) is what makes retiring the zone bundle (F) safe — 🔒 the user's own ruling behind `§3-U8`. ⛔ Splitting them leaves the tree either **two producers for the road network** *(`R-132`)* or **a retirement with nothing loading roads.** ⚠ Largest batch; ⭐ `HN-037` applies — the **test** surface of F is the work, not the deletion. ⚠⚠ **GREW to 16 on `2026-09-17`**: batch ①'s `U1` measurement deferred **two** hazards here — `C6` blob lifetime and `C7` passing the holder |
| **③ THE SURFACES** | **C8 + E + H** | 10 | ⭐ Both are surface work over a model that batch ② has made real: the map/zones UI, and the new-scenario recipe path. ⛔ Neither can be asserted before ② — a "not loaded" badge is meaningless without a loader. ⚠⚠ **`C8` JOINED `2026-09-17`**: `E2` is the first thing that issues a zone op, so the IG/CGF loaders (`BP-537`) must land **before or with** it — ⛔ shipping `E` alone would turn a nil-blast-radius break into a live one |
| **④ *(only if ② overruns)*** | **F alone** | 4 | ⚠ **The one legal split of ②**, and only after C5 is green and reported. ⛔ Never the reverse |

⚠⚠ **AS-BUILT `2026-09-17` — batch ② SPLIT, and not where §5 predicted.** 📐 It ran as **②a** *(`C1` `C2` `C5` `C6` `C7` + half of `D1` — the loader, the fake and blob lifetime)* and **②b** *(the other 11)*. ⛔ The predicted split was *"F alone, last"*; ⭐ the real one fell at a **wire-value collision** — `BuildTerrainAsset = 17` clashed with the live `SaveScenario = 17`, which `R-42` makes unrecoverable, so the item STOPPED for a coordinator ruling *(`R-106`)*. ⇒ ⭐⭐ **the lesson for the grouping rule: a batch also splits where a PERMANENT WIRE VALUE needs a decision, not only where the tree would be left incoherent.** ⚠ The C-before-F ordering held throughout — `F` never started before `C5` was green, which is the constraint §5 actually exists to protect.

⭐⭐ **Reporting:** each batch returns the **gate-report contract** *(`CLAUDE.md` §"THE GATE REPORT
CONTRACT", rows 1–8)*. ⛔ Row 8 binds batches ② and ③ specifically — they are cross-node changes, so the
report **names the integration suite** that would break if the invariant broke, and reports **running**
it, or states with base-sha evidence why it cannot gate.
⭐ Every batch also states **which `U` rows it closed and how** *(⚠ **CORRECTED `2026-09-17`: ① owns `U1`,
not ②** — `U1` blocks `A3` (§3.2), and `A3` is Stage A, which batch ① carries. Closed in batch ①: the flip
condition is MET and §5.4's prescribed fix was not implementable — see `BP-519`. ③ owns `U5` and `U9`)*, and
**every id it allocated** *(rule 5)* — ⛔ the coordinator allocated none.
