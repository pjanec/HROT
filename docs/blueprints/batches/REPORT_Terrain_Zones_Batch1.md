<!--STATUS
state: LIVE
build-state: REPORT — batch ① FOUNDATIONS (stages A + B + G), 11 items, ALL SHIPPED.
updated: 2026-09-17
current-answer: §1 gate table, §2 ids, §3 U1's measurement, §4 the UML check, §5 what the design got wrong.
stale-below: nothing — new document.
known-conflict: none.
related-designs:
  - ../../DESIGN_Terrain_Zones_And_Assets.md — the owning design; §5.4 and §4 were EDITED by this batch.
  - ../PLAN_Terrain_Zones_Build.md — §5's "② owns U1" was CORRECTED by this batch.
  - ../../designs/routes-1/ROUTES1-DESIGN.md — owns Stage G.
-->

# REPORT — **Terrain & zones, Batch ① FOUNDATIONS** *(stages A + B + G)*

**Dispatched at `b1751c382` · started marker `ae41e760` · branch `claude/blueprint-macro-feature-sdmspn`.**
**11 / 11 items shipped. No item stopped, no item blocked.**

---

## 1. GATE TABLE

⚠ **Honest scope of the baselines.** The RED sets below were measured at the dispatch sha by stashing the
whole batch and re-running the failing classes. The **total counts** at base were NOT re-measured for the
full suites (they are derived from "total now − tests I added"), and that arithmetic is marked *derived*.

| # | gate — verbatim command | pass / fail / skip | `--no-build` | delta vs baseline |
|---|---|---|---|---|
| 1 | `bash scripts/quick-check.sh FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` | **2199 / 0 / 0** | no (first build) | **+27 tests, 0 red** · baseline 2172 *(derived)* |
| 2 | `bash scripts/quick-check.sh Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` | **961 / 3 / 3** | no | **+6 tests**, 3 red **all pre-existing** · baseline 961 *(derived)* |
| 3 | `bash scripts/quick-check.sh Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj` | **421 / 6 / 1** | no | **+3 tests**, 6 red **all pre-existing** · baseline 425 *(derived)* |
| 4 | `bash scripts/quick-check.sh FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` | **1206 / 6 / 9** | no | **+0 tests**, 6 red **all pre-existing** |
| 5 | `python3 scripts/tracker-counts.py --check` | `tracker counts OK — open 103 / done 357 (+1 refuted)` | n/a | table updated to match rows |
| 6 | `python3 scripts/rulings-check.py` | `35/35 rulings verified against their sources` | n/a | unchanged |

**Working tree clean after every suite run** — verified with `git status --short` before and after the
stash/pop baseline cycle; no suite wrote into the tree.

**Quarantine counts:** SimHost **3 skipped**, IG **1 skipped**, Fdp.Core **9 skipped**, Fdp.Toolkits **0
skipped** — all unchanged by this batch; none added, none removed.

**Golden movement:** none. This batch moves no golden/generator output — no generator ran, and the
`Generators 184/184` gate is not in this batch's surface.

### 1a. Every RED, confirmed pre-existing against `b1751c382`

Method: `git stash push -u` → re-run the failing classes at base → `git stash pop`.

| suite | test | evidence it is pre-existing |
|---|---|---|
| SimHost | `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` | **red at base** in the stashed run |
| SimHost | `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` | **red at base** in the stashed run |
| SimHost | `MapPresentationParityRails.EveryTkbSpawningHost_ObtainsTheSharedTranslatorSet("Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs")` | it is a **source scan over a file this batch never touched** (`git status Stride/` clean); the file contains neither `TkbTranslatorSet.Base` nor `EntityCreationPack.Build` (`grep -c` = 0), so it failed at base by construction |
| IG | `EntityDamageTranslatorTests` ×2, `EntityInfoTranslatorTests` ×2 *(6 failures incl. theory cases)* | **red at base**; the failing set is a subset of the base failing set |
| Fdp.Core | `InMemoryMigrationStorageTests` ×3, `AsyncRecorderTests.ErrorPropagation_BackgroundWorkerError_PropagatesOnDispose` *(6 failures)* | **red at base** |

### 1b. 🔴 ONE RED WAS MINE, AND IS FIXED — the most useful thing that happened in this batch

`Hrot.SimHost.Tests` first came back with **6** failures, three of which I had caused:
`TheRegistryMutatorsAreSerialisedTests`, `EditLoadClusterOpHandlerTests` and
`ReplayLoadClusterOpHandlerTests`.

📐 **Cause:** my new `ZoneEntityPersistenceTests` calls `ComponentTypeRegistry.Clear()`, which wipes a
**process-global** dictionary. That assembly has no `xunit.runner.json`, so collections run in parallel
and the clear deleted registrations other classes had already made — so **the failures landed on classes
this batch never touched**, which is exactly the `DEBT-AIB-030` signature.

⭐ The `QA-008` rail (`TheRegistryMutatorsAreSerialisedTests`) is what caught it, by design, and named the
file. Fix: `[Collection(ComponentTypeRegistryMutatorCollection.Name)]` on the new class. SimHost went
**6 → 3**, and the 3 that remain are the pre-existing ones above.

⚠ **Two process lessons worth keeping:** ① three suites run CONCURRENTLY produced three *extra* reds that
vanished on a serial re-run (`RecorderSystemTests.DualStream_RecordableMaskFilter_NonRecordableBitIsCleared`,
`FastPathBenchmarks`, `ComponentDirtyTrackingTests`) — load flake, and I nearly reported one of them as a
real finding. ② my first failure-extraction regex silently **dropped theory cases** whose names contain
spaces, which hid the `MapPresentationParityRails` red for two rounds.

### 1c. Row 8 — it DOES bind, and here is the suite

The handoff said row 8 does not bind *"unless B4/B5 turns out to touch the scenario merge path."*
🔴 **B5 touched it.** `ScenarioMergeCore` rebuilt the canonical header from `TkbName` alone, so the new
terrain name would have been **silently dropped from every distributed save**.

- **Named suite:** `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioMergeCoreTests.cs` — the merge
  invariants' own suite (`CE-277(c2)`). **Run: green**, +3 rails (carry-with-TkbName, carry-without-TkbName,
  and mismatch-fails-loudly).
- ⛔ **Not** gated on `Hrot.ClusterRunner.Integration.Tests`: the change is inside the pure merge core, and
  that suite carries two of the programme's long-standing quarantined reds (`R-131`), so it would have
  added no signal this batch could act on.

---

## 2. IDS ALLOCATED *(rule 3 — the coordinator allocated none; rule 3a-id: plain, no letter suffixes)*

| id | item | state |
|---|---|---|
| **BP-517** | A1 + A2 — the gizmo double-render and the header | ✅ **ticked done** *(filed by the coordinator)* |
| **BP-518** | G1 — `RoutePlanTranslator` | ✅ **ticked done** *(filed by the coordinator)* |
| **BP-519** | A3 — per-tick road graph + the `U1` measurement + `RoadNetworkHolder` | ✅ done |
| **BP-520** | A4 — `LoadZoneIntent`'s false "Consumed by ClusterMaster" | ✅ done |
| **BP-521** | B1 — `TkbType.TerrainZone = 8804` | ✅ done |
| **BP-522** | B2 — `TerrainAssetLoadState` | ✅ done |
| **BP-523** | B3 — `ZoneFootprint` | ✅ done |
| **BP-524** | B4 — zone entities through the ordinary gate | ✅ done |
| **BP-525** | B5 — terrain name in the header + the merge | ✅ done |
| **BP-526** | B6 — terrain definition asset + singleton | ✅ done |
| **BP-527** | 🔴 **NEW DEFECT — `[DataPolicy]` silently ignored for a managed singleton set without an explicit registration** | ⛔ **OPEN** — engine work, out of this batch's scope |

⭐ Rows placed in **Area F — Runtime & state architecture** (the honest fit for model/policy/persistence
work), not Area C where the coordinator filed BP-517/518. Area placement does not affect
`tracker-counts.py`, which buckets by `RW-` tag.

**Wire ids allocated: exactly ONE — `TkbType.TerrainZone = 8804`.** ⛔ The three `NodeOpType`/`ClusterOpType`
values belong to batch ②, and the `NodeOpType` gaps at 6/17/18/19 were left empty.

---

## 3. `U1` — THE MEASUREMENT, AND WHAT IT DID TO A3

> The plan said: *"whether any navigation module runs on a background thread where `DataPolicy` constrains
> singleton access. If it does, the per-tick singleton read is illegal and a holder object is required."*

### ✅ ANSWER: YES — and the design's preferred fix was **not implementable**, for a sharper reason than `DataPolicy`.

| # | measured fact | site |
|---|---|---|
| ① | `NavigationSolverModule.Policy => ExecutionPolicy.SlowBackground(10)` — a background thread | `NavigationSolverModule.cs:26` |
| ② | `SlowBackground` ⇒ `DataStrategy.SoD`; `Validate()` **forbids** `Direct` off the main thread | `ExecutionPolicy.cs:81-84`, `:148-157` |
| ③ | 🔴 **`ISimulationView` has NO singleton API at all** — 9 members, none of them a singleton accessor | `Fdp.Core/Abstractions/ISimulationView.cs` |
| ④ | 🔴 `CarKinematicsSystem` — *the pattern §5.4 told me to copy* — gets singletons by downcasting the view to `EntityRepository` and **throwing** when it is not one | `CarKinematicsSystem.cs:48-51` |
| ⑤ | it is safe there only because `GroundKinematicsModule` is `Synchronous` | ② |

⇒ ⭐⭐⭐ **Copying `CarKinematicsSystem` into the solver would have made it THROW on its own production
path**, not read a stale blob. The blocker is not that `DataPolicy` filters the singleton out of the
snapshot — it is that **there is no way to ask a view for a singleton at all.**

**What A3 shipped instead** — §5.4's own named alternative:
- `RoadNetworkHolder` — an immutable box behind one volatile reference write, so a reader gets the whole
  old graph or the whole new one and never a torn multi-field native struct.
- `PathfindingSolverSystem.Execute` resolves **per tick**: live singleton when the view IS the repo →
  holder → constructor blob. ⇒ every path that *has* a single source still uses it.
- Rails: swap-via-singleton observed, swap-via-holder observed, and the ctor-blob fallback not regressed.

**Two things A3 did NOT solve, stated rather than hidden:**
1. ⚠ **Lifetime.** Publishing a new graph does not make the old blob safe to dispose while a 10 Hz
   background solver may be mid-traversal inside its native arrays. **Batch ② owns this** — the commit
   path must keep the previous blob alive.
2. ⚠ A host composing `NavigationSolverModule` **without** a holder still will not see a reload. The
   loader must pass one.

⭐ **Context that makes this cheap now:** `NavigationSolverModule` has **zero production constructions**
today (only test sites). The background path is not live yet — which is precisely why fixing it before
role-based composition switches it on was worth doing.

---

## 4. THE UML CHECK *(obligation ③)*

**The design carries 1 class diagram (10 classes), 3 sequence diagrams and 1 module-relationship graph.**

| diagram | what I built | verdict |
|---|---|---|
| §2 class diagram — `TerrainAssetLoadState { LoadPhase, SourceHash }`, `TkbIdentity` as THE discriminator, `SimTransform → EditablePolyline` "points are RELATIVE" | built exactly: the marker has those two fields, discrimination rides `TkbIdentity` (`TerrainZone = 8804`), and A1 made the relative rule true in code | ✅ **matches** |
| §2 class diagram — `TerrainLoadService`, `ZoneTileLoader`, `StaticObstacleBaker`, `TerrainLoader` | **not built — correctly**: all four are stage C/D, batch ② | ✅ out of scope |
| §2.1e ⑤d sequence — New Scenario from a recipe | not built — stage H, batch ③ | ✅ out of scope |
| §3.1 / §3.2 sequences — the 2PC round and the local scenario-load path | not built — stage C, batch ② | ✅ out of scope |
| §4 module graph — `ZoneEnvironmentData → CarKinematicsSystem` re-read every tick; `PathfindingSolverSystem` a **red frozen box** | 🔴 **DEVIATION** — the graph's dashed *"MUST become a per-tick read"* edge from `ZED` to `PF` is **not what shipped**, because on the background path that edge cannot exist. Shipped: `RoadNetworkHolder → PathfindingSolverSystem`, with the `ZED` edge live only on Synchronous hosts | ⚠ **deviation, argued and folded back** |
| §4 module graph — *"`NavigationSolverModule` and `EngineBackedNavigationModule` have the same shape"* | 🔴 **the design is WRONG about `EngineBackedNavigationModule`** — see §5 | ⚠ **corrected in the design** |

**Design edits made (obligation ⑤ — prior state marked SUPERSEDED):**
- `docs/DESIGN_Terrain_Zones_And_Assets.md` **§5.4** — the whole "preferred fix / what would flip it"
  block is struck through, marked **⛔⛔ SUPERSEDED `2026-09-17` by the `U1` measurement (`BP-519`)**, and
  replaced with the five-row measurement table, what shipped, and the two deferred hazards.
- `docs/DESIGN_Terrain_Zones_And_Assets.md` **§4 red box 2** — marked ✅ FIXED, plus a
  **⚠⚠ CORRECTION** that *"`EngineBackedNavigationModule` has the same shape"* is measured false.
- `docs/blueprints/PLAN_Terrain_Zones_Build.md` **§5** — *"② owns `U1`"* corrected to **① owns `U1`**.

---

## 5. ⭐⭐ WHAT THE DESIGN GOT WRONG *(the most valuable section)*

| # | claim | measured truth |
|---|---|---|
| **1** | §5.4: *"have the navigation systems re-read it per tick exactly as `CarKinematicsSystem` already does, rather than introducing a holder"* | 🔴 **Not implementable.** `ISimulationView` exposes no singleton API, and the model it names as the template gets singletons by downcasting-and-throwing. The escape clause was the real answer, not the fallback. |
| **2** | §4: *"`NavigationSolverModule` and `EngineBackedNavigationModule` have the same shape"* | 🔴 **False for the second.** `EngineBackedNavigationModule._roadNetwork` is assigned in the ctor and **read by nothing** — `Tick` is empty and its providers are navmesh/volumetric/crowd. There was no stale read to fix because there is no read. ⛔ Do not "fix" that field. |
| **3** | PLAN §5: *"② owns `U1`"* | 🔴 **Wrong batch.** `U1` blocks `A3` (§3.2), and `A3` is Stage A — batch ①. Corrected in the plan. |
| **4** | B2/B6 success condition: *"a save/replay round-trip proves it is absent"* | ⚠ **Under-specified in a way that matters.** For a **managed singleton** the attribute alone proves nothing — see #5. The rails now assert at the gates the engine actually applies (`GetSaveableMask`, `GetRecordableMask`, `ComponentTypeRegistry.IsRecordable`) plus an end-to-end serialize. |
| **5** | implied everywhere: *"`[DataPolicy]` on the type is what excludes it"* | 🔴🔴 **FALSE for managed singletons, and it cost a red test to discover.** The attribute is read only by `RegisterComponent`/`RegisterManagedComponent`. `SetSingletonManaged` auto-registers via `ManagedComponentType<T>.ID`, which never reads it ⇒ the type keeps `recordable = saveable = TRUE`, and `RecorderSystem.RecordSingletons` **writes it into recordings anyway**. ⇒ **`BP-527`**, and the reason `HrotSharedComponentRegistry` now carries a comment saying that registration line is load-bearing. |
| **6** | `RULINGS.md` **`R-44`**: *"`MAX_COMPONENT_TYPES = 256`"* | 🔴 **The code says 512** (`FdpConfig.cs:37`), and ids already run to 264. ⭐ This is precisely the **§M disease** the ledger warns about: `rulings-check.py` verifies the quote still exists *in the doc*, and the doc did not change — **the code did**. The row is GREEN AND FALSE. ⛔ I did **not** edit `RULINGS.md` (cross-cutting canon, and the ledger is not this lane's file) — flagging it for the coordinator. |

**Also worth the coordinator's attention, not filed as batch work:**
- `EditablePolyline.Version` is still the never-incremented counter of `BP-516`; A2 added a 🔴 header
  warning telling readers not to rely on it and pointing at the footprint hash instead.
- `HasInFlightTransaction` (design §9.4) remains unfixed and out of scope, as the design says — still
  unfiled as a tracker row.

---

## 6. WHAT SHIPPED — files

**Production:** `TacticalAreaGizmo` · `EditablePolyline` (doc) · `ClusterOpIntents` (doc) ·
`TkbEntityTypes` · `GlobalComponentIds` (+2) · `PathfindingSolverSystem` · `NavigationSolverModule` ·
`EngineBackedNavigationModule` (doc) · **new** `RoadNetworkHolder` · **new** `Fdp.Toolkits/Terrain/`
(`TerrainAssetLoadState`, `ZoneFootprint`, `TerrainDefinition`, `TerrainDefinitionParser`) ·
`HrotSharedComponentRegistry` · `ScenarioHeader` · `ScenarioSerializer` · `ScenarioMergeCore` ·
`ScenarioHeaderDto` · **new** `RoutePlanTranslator` · `HrotScenarioSerializerFactory`.

**Rails (+36):** `PresentationGizmoTests` +3 · `PathfindingSolverSystemTests` +3 ·
`TerrainAssetLoadStateTests` +8 *(new)* · `TerrainDefinitionTests` +13 *(new)* · `ScenarioMergeCoreTests` +3 ·
`RoutePlanTranslatorTests` +4 *(new)* · `ZoneEntityPersistenceTests` +2 *(new)*.
