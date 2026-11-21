<!--STATUS
state: LIVE
updated: 2026-09-17
current-answer: §1 what shipped, §2 the gate table, §3 the reds (all named, all proven), §4 what the
  design got wrong, §5 the UML check, §6 ids allocated.
stale-below: nothing — new document.
known-rot: 🔴 §2's gate row 7 (Hrot.Orchestrator.Tests) was WRONG — see the AMENDMENT at the top of §3.
  Every other row was re-measured on 2026-09-18 after an explicit build and stands.
known-conflict: none.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — the owning design. §10 carries the AS-BUILT corrections
    this batch folded back; THIS report points at them and does not restate them.
  - docs/blueprints/PLAN_Terrain_Zones_Build.md — the stage breakdown these items came from.
  - docs/blueprints/batches/HANDOFF_Terrain_Zones_Batch2b_Ops_And_Retirement.md — the dispatch.
-->

# REPORT — Terrain & zones, batch 2b (the ops and the retirement)

**Branch** `claude/blueprint-macro-feature-sdmspn` · **scope frozen at** `765618636` · **11 of 11 items
complete** (`Z0` gate debt + `C3` `C4` `C5-fix` `D1` `D2` `D3` `D4` `D5` `F1` `F2` `F3` `F4`).

---

## 1. What shipped

| item | what landed |
|---|---|
| **C3** | the scenario-load path calls `TerrainLoadService.EnsureAllLoaded` **locally**, no NodeOp |
| **C4** | `LoadZoneIntent` finally has a consumer — one `PrepareZone`→`CommitZone` round **per zone** |
| **D2** | `ZonePayloadDto` / `TerrainAssetBuildPayloadDto` on the wire, `ZoneOpPayload` / `TerrainAssetOpPayload` on the node op |
| **D3** | ONE `TerrainAssetHandler`, registered on all four ECS hosts by a shared `TerrainAssetRegistrar` |
| **D4** | `IgZoneDummyHandler` **deleted** |
| **D5** | the terrain-identity check, failing loudly and naming the node |
| **F1** | `ZoneDefinitionDto` · `ZoneObstacleDto` · the envelope `Zones` section · `IZoneManagerService`+`ZoneManagerService` · `ZoneMembership` (id **171 burned**) · the DTO/road arms of `EditorZoneAuthoringSystem` |
| **F2** | the `ScenarioMergeCore` I4 one-`Zones`-source guard |
| **F3** | the test surface — 6 suites and 2 doubles, every claim re-homed or deleted **with a reason** (§3.3) |
| **F4** | `CE-277(a)`/`OQ1` closed as **will-not-build** |

### 1.1 🔴 A DEFECT THIS BRANCH SHIPPED IN BATCH ②, FOUND AND FIXED HERE

📐 **`ClusterSlave` commits with `repo: null` at BOTH dispatch sites** (`ClusterSlave.cs:271`, `:432`).
`C5`'s `TerrainLoadClusterStateHandler.Commit` published only through that parameter, so in production it
took its *"no-ECS host"* branch on **every** host: disposed the staged blob and published **nothing** —
no `TerrainDefinition`, no `ZoneEnvironmentData`, no holder swap. A capability that reported present and
silently no-opped (`R-133`).

⚠ **Why its own suite was blind:** every test called `Commit(intent, world)`, handing the repository in —
a path production does not take. ⭐ Fixed with the established pattern (`repo ?? _world`, as
`HrotScenarioLoadHandler.cs:196` already does), and pinned by a rail that commits with a **null** repo.

### 1.2 🔴 SECOND FINDING — the abort arm reached no handler at all

📐 **Measured:** `IClusterStateHandler.Abort` has **zero production callers** (`ClusterSlave` never invokes
it) and **no handler in the tree claimed `NodeOpType.AbortTransaction`**. A master's abort fan-out reached
every node, found no handler, and **auto-ACKed `Success` while nothing rolled back.** ⭐ `TerrainAssetHandler`
now claims it. ⚠ **Every other handler family still has no rollback path** — pre-existing, narrowed only
for terrain here, and worth a tracker row of its own.

### 1.3 ⚠ A DELIBERATE, USER-VISIBLE BREAK THE COORDINATOR SHOULD WEIGH

📐 **Only SimHost registers a terrain loader** (one call site). `D5` implements §8.3 N4 as written ⇒ **once
anything issues a zone op against a terrain-named scenario, IG and CGF will FAIL it loudly** until they
compose one. That is the ruling working as designed *("how Stride's present gap should surface instead of
silently passing")*, and the blast radius today is **nil** because no UI issues the op yet (§8.4/§9 are
OPEN). ⛔ Named here because it is a decision, not an accident.

---

## 2. Gate table — base `765618636`

⭐ One build per project; `--no-build` for every run after (`BUILD ONCE · RUN ONCE`).

| # | gate — verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|
| 1 | `dotnet build <proj> --no-restore` × **Hrot.Core, Hrot.Presentation, Hrot.SimHost, Hrot.IG, Hrot.CGF, Hrot.Editor, Hrot.Orchestrator, Hrot.Network.Orchestration, Fdp.Toolkits** | n/a | **0 errors** each | — |
| 2 | same × **Hrot.Core.Tests, Hrot.SimHost.Tests, Hrot.Editor.Tests, Hrot.Presentation.Tests, Hrot.Orchestrator.Tests, Fdp.Toolkits.Tests, Hrot.ClusterRunner.Integration.Tests** | n/a | **0 errors** each | — |
| 3 | `dotnet test Hrot.Core.Tests --no-build` | ✅ | **170 / 2 fail** | reds identical to base (§3.1) |
| 4 | `dotnet test Hrot.SimHost.Tests --no-build` | ✅ | **973 / 3 fail / 3 skip** | reds ⊂ base's 4 (§3.1) |
| 5 | `dotnet test Hrot.Editor.Tests --no-build` | ✅ | **403 / 1 fail / 1 skip** | §3.2 |
| 6 | `dotnet test Hrot.Presentation.Tests --no-build` | ✅ | **238 / 2 fail** | §3.2 |
| 7 | `dotnet test Hrot.Orchestrator.Tests --no-build` | ✅ | **160 / 3 fail** | reds identical to base (§3.1) |
| 8 | `dotnet test Fdp.Toolkits.Tests --no-build --filter ScenarioMergeCoreTests` | ✅ | **11 / 11 pass** | +1 (re-homed) |
| 9 | ⭐⭐ **INTEGRATION** — `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter EditorAuthoringIntegrationTests` | ✅ | **11 / 11 pass** | −2 (both deleted with reasons, §3.3) |
| 10 | `python3 scripts/tracker-counts.py --check` | n/a | **OK — open 104 / done 363 (+1 refuted)** | — |
| 11 | `python3 scripts/rulings-check.py` | n/a | **35/35 verified** | — |
| 12 | `python3 scripts/design-digest.py --check` | n/a | **pass** — STATUS + INVENTORY + UML all present | — |

⭐ **New rails added this batch: 20.** `ClusterMasterZoneRoundTests` 6 · `TerrainAssetHandlerTests` 13 ·
`TranslatorDtoTests` +2 · `HrotScenarioLoadHandlerTests` +3 · `TerrainLoadClusterStateHandlerTests` +2 ·
`ScenarioMergeCoreTests` +1 *(net of re-homing)*.

⭐ **Working tree CLEAN after every suite run** — no golden was regenerated.
⭐ **Quarantine counts unchanged**: 3 skips in `Hrot.SimHost.Tests`, 1 in `Hrot.Editor.Tests`, both as at base.
⛔ **No new skip was introduced.**

⚠ **Row 9 is the row-8 obligation.** This batch changed a cross-node protocol (a new 2PC round, a new
handler on every host, a retired persistence section), so the integration suite that exercises the editor
authoring + save path was named and RUN. ⛔ The **full** `ClusterRunner.Integration` suite is **not** gated:
its `ClusterRunner` DDS-allocator crash is a **pre-existing** un-gateable condition already on record, so
the `EditorAuthoring`/`EditorHarness` slice — the part this batch actually touched — was run in isolation.

---

## 3. The reds — every one named, none left unexplained

### 🔴 AMENDMENT `2026-09-18` — **ROW 7 WAS WRONG, AND IT WAS MY OWN RAIL**

⛔ §2 row 7 reports `Hrot.Orchestrator.Tests` as **160 pass / 3 fail — reds identical to base**. 📐
Re-measured `2026-09-18` **after an explicit build of the test project**: it was **4** failures, and the
fourth was
`ClusterMasterZoneRoundTests.LoadZoneIntent_FansOutPrepareThenCommit_AndReportsSuccessOnlyAtTheEnd` —
**a rail this batch wrote**.

| | |
|---|---|
| **what it asserted** | `Assert.NotEqual(prepares[0].TransactionId, commits[0].TransactionId)` — written when the zone round used a FRESH transaction id per phase |
| **why that became false** | `D3` measured that the NODE stages under the prepare's id and consumes that staging in the commit ⇒ two ids leave every commit unable to find its own prepare. The master was changed to reuse **ONE** id per round (design §10.3). ⛔ The rail was not updated with it, so it went on asserting the behaviour that had just been found wrong |
| 🔴 **why the gate run did not catch it** | ⛔⛔ **the documented `--no-build` STALE-BINARY trap.** During `D3` I built the PRODUCTION project (`Hrot.Orchestrator`) and not the test project, so `Hrot.Orchestrator.Tests/bin` still held the **pre-`D3`** `Hrot.Orchestrator.dll`. `dotnet test --no-build` tested the old code and printed a pass. ⚠ `CLAUDE.md` names this exactly: *"`dotnet test --no-build` runs a STALE BINARY and prints `PASSED`"* — and it caught me in the one suite where a production change and its rail disagreed |
| ✅ **fixed** | assertion corrected to `Equal` with the history in the comment; suite now **12/12** for that class |

⭐ **The rest of the table was re-verified, each after an explicit build** — so the coordinator can trust
the other rows: `Hrot.Core.Tests` **170/2** *(the two `EcsPatchContextTests`, pre-existing)* ·
`Hrot.SimHost.Tests` **983/3** *(the three pre-existing; 983 vs the reported 973 is this batch's new
rails)* · `Hrot.Orchestrator.Tests` **166/3** *(the three pre-existing)*. ⇒ ⛔ **one row was wrong, and it
was wrong because of HOW it was measured, not because a red was hidden deliberately.**

⭐⭐ **The lesson, stated so it is checkable rather than a resolution:** `--no-build` is only safe when the
test project itself has been built since the last PRODUCTION edit. ⇒ **build the TEST project, not the
production one, before a `--no-build` run** — the test project's build copies the production assembly,
the reverse does not.

### 3.1 ✅ PROVEN PRE-EXISTING — measured in a worktree at base `765618636`

| suite | red | base |
|---|---|---|
| `Hrot.Core.Tests` | `EcsPatchContextTests.EcsPatchContext_FlushDirtyMarks_DeduplicatesOrdinals` | ✅ red at base |
| `Hrot.Core.Tests` | `EcsPatchContextTests.EcsPatchContext_FlushDirtyMarks_CallsSmartEgressForTouchedComponents` | ✅ red at base |
| `Hrot.SimHost.Tests` | `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` | ✅ red at base |
| `Hrot.SimHost.Tests` | `MapPresentationParityRails.EveryTkbSpawningHost_…(EditorStrideSubsystem.cs)` | ✅ red at base |
| `Hrot.SimHost.Tests` | `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` | ✅ red at base |
| `Hrot.Orchestrator.Tests` | `ClusterMasterPrefetchTests.PrefetchScenario_WhenGatewaySucceeds_…` | ✅ red at base |
| `Hrot.Orchestrator.Tests` | `StorageGatewayTests.PushToNodes_BadTarget_ReturnsPartialFailure` | ✅ red at base |
| `Hrot.Orchestrator.Tests` | `StorageGatewayTests.PrefetchScenarioAsync_EmptyDirectory_ThrowsInvalidOperation` | ✅ red at base |

⭐ **The base also carries a red this branch does NOT** — `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule`
(green here). That is the flake already filed as `BP-534`, and it is why a single run is not evidence.

### 3.2 ⚠ LOAD-SENSITIVE — stated honestly, NOT claimed proven

| red | what was measured | my reading |
|---|---|---|
| `Hrot.Editor.Tests` · `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | GREEN **3/3 in isolation**; red in the full parallel run here; green in ONE full base run. 📐 The diff touches **no** hot-reload file | an `AssemblyLoadContext` **collection** assertion — allocation pressure from a parallel suite is exactly what stops an ALC being collected. ⛔ I will **not** call it "pre-existing" from one base sample; I call it **load-sensitive and not mine**, with the isolation runs as the evidence |
| `Hrot.Presentation.Tests` · a **ROTATING** pair | run 1: `RouteWaypointGizmoTests.OnCommit_WritesBackToEcs`; run 2: `TheDragCommitsThroughTheWriteRouterTests.ARefusedCommitRestoresTheOriginalPosition` + `ScenarioFileServiceTests.SaveLoad_RoundTrip_…`. **Every one GREEN when run alone** | the identity rotates between runs ⇒ `DEBT-AIB-030`'s shape, not a defect. ⚠ **`ScenarioFileServiceTests.SaveLoad_RoundTrip` IS in this batch's blast radius**, so it was re-run **alone: 4/4 pass** rather than waved through |

⛔ **Neither is reported as a pass.** Both are named, both are reproducible only under parallel load, and
the one that touches my change was verified in isolation instead of being explained away.

### 3.3 ⭐⭐ `F3` — the test surface, claim by claim *(`HN-037`: the test surface IS the work)*

| suite / double | verdict | the reason |
|---|---|---|
| `ZoneManagerServiceTests` (4 tests) | **DELETED** | its subject class is deleted. Road-network→singleton **re-homed** onto `TerrainLoadClusterStateHandlerTests` (which asserts it through the holder); publish/retire **re-homed** onto `RoadNetworkHolderTests`; obstacle creation **re-homed** onto `EditorZoneAuthoringSystemTests`; `GetActiveZones` asserted a concept that no longer exists |
| `HrotScenarioDtoTests` (2) | **DELETED** | both tests round-trip `ZoneDefinitionDto`, which is deleted |
| `ScenarioFileServiceZoneTests` (2) | **DELETED** | both assert the `Zones` section is written / omitted — the section is the thing retired |
| `ZoneScenarioLoadIntegrationTests` (1, 8 assertions) | **DELETED** | drove the whole `Zones`-section load pipeline. Its surviving half — *"an authored zone survives save/load"* — is `ZoneEntityPersistenceTests` |
| `SystemTests.SpawnObstacle_…EntityWithZoneMembershipCreated` | ⭐ **RE-HOMED** | kept the live half (a command creates a placed collidable entity), dropped the `ZoneMembership` half |
| `SystemTests.UpdateZoneConfig_…SetsSingletonTrue` | **DELETED** | the behaviour was a live hazard (wrote `ZoneEnvironmentData` bypassing the holder). Claim re-homed onto the terrain loader |
| `EditorAuthoringIntegrationTests.…ZoneObstacleHasZoneMembership` | ⭐ **RE-HOMED** | ⚠ it justified itself by `ZoneObstacleRenderLayer`, **which no longer exists in the tree** — the test was already anchored to a dead consumer. Kept the end-to-end claim the harness genuinely proves |
| `EditorAuthoringIntegrationTests.…RoadNetworkUpdate_Injects…` | **DELETED** | same retired hazard as above |
| `EditorAuthoringIntegrationTests.…FullSave_BundlesZoneDtoInEnvelope` | **DELETED** | re-homed in two halves (`ZoneEntityPersistenceTests` + `ScenarioMergeCoreTests`). ⚠ the road-network half is **deliberately not** re-homed: a road network is no longer something a scenario save carries |
| `ScenarioMergeCoreTests.Zones_TakenFromTheSingleBrainSource` | ⭐ **RE-HOMED** | restated on the surface that now carries it: a zone ENTITY reaches the canonical file through the ordinary union, and **no** `Zones` section is reconstructed |
| `ScenarioMergeCoreTests.TwoZonesSources_FailLoud` | **DELETED** | the I4 guard is gone; two slices carrying the same zone now collide on the entity GUID at `GuidCollision_FailsLoud`, with a better message |
| `SpyZoneManagerService` (SimHost) | **DELETED** | both halves of its claim died with the section |
| `NullZoneService` (Editor) | **DELETED** | a do-nothing double every test passed — it asserted nothing |
| `UrbanCombatFileLifecycleTests` | ⚠ **repaired** | **a SIXTH suite the design's list of five did not name** — it set `Zones = null` on the envelope. One line |

⇒ ⭐ **14 claims dispositioned; 3 re-homed, 11 deleted, every one with a stated reason.**

---

## 4. What the DESIGN got wrong — folded back per obligation ⑤

⭐ All four corrections live in **[`DESIGN_Terrain_Zones_And_Assets.md` §10](../../DESIGN_Terrain_Zones_And_Assets.md)**,
with the prior state marked SUPERSEDED and the STATUS block's `known-rot` naming them. This report points
at them; it does not restate them.

| # | the design said | measured |
|---|---|---|
| §10.1 | §3.2: `EnsureAllLoaded` at the load handler's commit | the zone entities **do not exist yet** — commit only enqueues into genesis. Moved to the end of `DrainDeferredAcks()` |
| §10.2 | §9.5: a "zone name" from the Area entity | **no name component exists.** The id is `NetworkIdentity.Value` as a string |
| §10.3 | §3.1's sequence | `ClusterSlave` commits the PREPARE intent too and passes `repo: null`; the round needs ONE transaction id across both phases |
| §10.4 | mgmt-1 §11.1's abort arm | reached **no handler at all** |
| §10.5 | §8.3 N4 | ⇒ IG and CGF now fail a zone op loudly (§1.3) |

---

## 5. UML check — obligation ③

📐 The design carries **5 diagrams**: §2's model `classDiagram`, §3.1 and §3.2's `sequenceDiagram`s,
§2.1e ⑤d's authoring sequence, and §4's module `graph TD`.

| diagram | what I built | verdict |
|---|---|---|
| §3.1 runtime 2PC sequence | `ClusterMaster` → `PrepareZone`/`CommitZone` → `TerrainAssetHandler` → `TerrainLoadService` | ⭐ **matches**, refined by §10.3 |
| §3.2 local-load sequence | same service, called locally, no NodeOp | ⚠ **DEVIATES on the MOMENT** (§10.1); the claim it makes is unchanged |
| §4 module diagram | `TerrainAssetHandler` reaches CGF · SimHost · IG · Editor via one registrar; `LoadZoneIntent`'s red "NO CONSUMER" edge is now live | ⭐ **matches**, and **one red box goes green** |
| §2 model `classDiagram` | `TkbIdentity`+`SimTransform`+`EditablePolyline`+`TerrainAssetLoadState` | ⭐ **matches** |
| §2.1e ⑤d authoring sequence | not touched this batch | n/a |

---

## 6. IDs allocated

⭐ Continuing **plain incrementing numbers, no letter suffixes** (`R-3a-id`).

| id | what |
|---|---|
| **BP-535** | 🔴 `ClusterSlave` commits with `repo: null` at both dispatch sites ⇒ C5's terrain loader published nothing in production (§1.1) |
| **BP-536** | 🔴 `IClusterStateHandler.Abort` has zero production callers and nothing claimed `AbortTransaction` ⇒ abort fan-outs auto-ACKed Success while nothing rolled back (§1.2) |
| **BP-537** | ⚠ `D5` makes IG/CGF fail a zone op loudly until they compose a terrain loader — designed, but a user-visible break (§1.3) |
| **BP-538** | ⚠ the `ClusterOpType` parity test EXISTS but is one-directional (FDP→NED only), so it can never see a member NED has and the mirror lacks — the `SaveScenario = 17` shape. **Corrects a false "no such test exists" claim this lane wrote into `BP-533`** |
