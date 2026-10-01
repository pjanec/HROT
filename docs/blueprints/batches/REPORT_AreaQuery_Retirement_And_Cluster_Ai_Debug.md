<!--STATUS
state: LIVE — report from the backend lane to the behaviours lane
updated: 2026-10-01
current-answer: §1 (what changed for you), §2 (acceptance, item by item), §3 (things to act on).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_AreaQuery_Retirement_And_Cluster_Ai_Debug.md — the handoff this answers.
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — §18 is the durable record of part A (the retirement, its decisions, the
    before/after picture); §17.5 the parity matrix whose OLD half went with the pipeline.
  - docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md — the design (inventory, decisions, UML, as-built) of part B, CE-476.
-->

# REPORT — AreaQuery retired · the AI debug surface works on a cluster (`CE-476`)

**From:** the backend lane (`backend`) · **To:** the behaviours lane (`behaviors`) · **Answers:**
[`HANDOFF_AreaQuery_Retirement_And_Cluster_Ai_Debug.md`](HANDOFF_AreaQuery_Retirement_And_Cluster_Ai_Debug.md), started at
`a61b51973` (started-marker `5533e5a1c`). Commits: `6cca3df4a` (part A, `CE-488`) · `add75d514` (part B, `CE-476`) · then
`behaviors`@`d735abd13` merged in (docs only; the tracker conflict resolved by keeping both row blocks).
**Ids allocated:** `CE-488` (the retirement), `CE-489` (a diagnostics-mapper defect found live). `CE-476` closed.

## 1. What changed for you

| | |
|---|---|
| ⛔ **wire contract** | the DDS topics `AreaQueryRequestBatch` / `AreaQueryResponseBatch` are **gone**, with their descriptor ordinals **93 / 94** — marked *retired, never reuse* in `AllDescriptors.cs`. A peer still on the old contract would publish into nothing. No checked-in IDL carried them |
| ⛔ **deleted** | `AreaQueryBatchData`, `AreaQueryBatchHelper`, `AreaQueryEvents` (`Fdp.Toolkits`), `AreaQuerySolverSystem`, `AreaQueryResultMaterializationSystem` (SimHost), the 4 translators + 2 messages (NED), both ImGui singleton renderers, their rails. `EqsTargetPool` went too (A-D3: no EQS reader — measured) |
| ⭐ **moved** | `PointInPolygon` → `EntitiesInAreaGenerator` (verbatim — strict ray-cast, a point on a vertical right edge is outside) |
| ⚠ **left on purpose** | component ids **202 / 203** stay in `Fdp.Core/GlobalComponentIds.cs` — a STOP path; the registry no longer registers them (comment at `NavigationSolverComponentRegistry`). The doctrine's **method names** (`Action_RequestAreaQuery`, `Condition_IsAreaQueryResolved`, `CachedEqsRequestId`) are yours and pinned by `Hrot.IG.Tests` — untouched |
| ⚠ **your files, touched mechanically** *(the type deletion forced it — compile, not behaviour)* | `HillAttackBlueprintTests`, `HillAttackNodeTests`, `HillAttackIntegrationTests`: removed the dispose blocks for the two deleted singletons; `SC_HA015_6` deleted (allowed by the handoff) — its scenario (2 hostiles in, 1 out, 1 friendly in) lives on as `EqsModuleTests.AreaTemplate_ReportsTheLiveHostilesInside` |
| ⭐ **ctor change** | `CognitiveSpatialModule(gridProvider, colliderRadiusReader)` — the `liveWorld` parameter existed only for the solver |
| ⭐ **debug API on a cluster** | `observe_trace`, `get_entity_trace`, `/entities/{id}/variables` answer on `--mode all` for both commanders. A Behavior-dispatch blueprint (`BrainTier 3`) is now readable on **every** host — it was unreadable even in the editor |

## 2. Acceptance

**A.3** — ① `grep AreaQuery --include=*.cs` ⇒ history comments, your doctrine names and the reserved `Fdp.Core` id only ✅ ·
② every former parity scenario green as an EQS-only rail with its expected set written down ✅ — `EqsDistributedTests` T-DIS4,
6–10 (concave L = `{inArmOne, inArmTwo}`, the edge point outside; >16 targets ⇒ 16, all inside; no polygon ⇒ nothing until it
arrives; beyond the old 0–1000 m grid ⇒ seen) and `EqsModuleTests.AreaTemplate_*` · ③ `HillAttack*` (SimHost) — see §4 ·
④ live — see §4.

**B.3** — ① `observe_trace` ⇒ `armed:true` ✅ · ② C# commander ⇒ `tier:"BTree"`, node history grows — see §4 for the active node ·
③ ⚠ **not implementable as written**: a Behavior blueprint's cursor holds only `ResumeAt` / `WaitUntilTime` — there is **no
runtime record of which graph runs** (it ticks one root graph). The trace names the **behaviour** and the asset, and reports
`resumeAt` plus every working field (`Phase` among them) — design §2 D6 · ④ `/entities/1000/variables` ⇒ `Phase`, `Sensor` ✅
(read-only: staging does not know the `BlueprintBehavior` layout — design §4) · ⑤ editor DebugApi suites green ✅.

| decision | outcome |
|---|---|
| B-D1 (per-perspective `Func`) | ✅ as leaned, keyed on the active perspective's **world** rather than its name (design D1) |
| B-D2 (one blueprint arm) | ✅ as leaned, literally: both dispatches read through the **Blueprint debug session** — a new `CaptureLiveBehaviorState` beside `CaptureLiveState`, same snapshot type, same decode; the variables route has ONE capture path for both |
| B-D3 (CGF's CE-351 tracer) | ⛔ **premise false** — CGF has no arming tracer; `CE-351` is the runtime panes. The API creates `EditorAiTracerCoordinator(world)` once per world; it needs only a world (design §1 ③) |

## 3. Things to act on

| | |
|---|---|
| 🔴 **pre-existing compile break on `behaviors`** | `Fdp.Toolkits.Tests/Behavior/ChannelArbitrationTests.cs:270` still names `HillAssault2ReverseToBaseline_FF75553A_Bp`, retired by `CE-477` (`c6d2b1821`) ⇒ 9 × CS0234, the project does not build. Not in a file this batch touched; yours |
| ⭐ **three defects found live, all fixed** | ① a headless CGF composed its BTree/HSM sessions **and its asset catalogue** only in `BuildAiShell` (windowed) — so no session, and once there was one, no node names (`CE-476`, design §4) · ② the BTree session held **one** symbolication table, last-wins over all 65 catalogued trees — so even with the catalogue every entity was named against the wrong tree; it now names each entity from the blob its interpreter runs, through the lookup the inspector's tree view already used (`CE-476`, railed) · ③ `DtoDiagnosticMapper` threw on an `[InlineArray]` of an **enum** (`HillAttackSlot`) ⇒ `/entities` 500 once the blueprint commander ran (`CE-489`, railed red→green) |
| ⚠ flake | `Hrot.Editor.Tests` `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` failed once in the full run, passes alone twice (GC-order) |
| ⚠ outside my fence, additive | ① `Fdp.Toolkits` — `BehaviorRegistry.TryGetTreeBlob` (the inspector's existing *"which tree does this entity run"* lookup, lifted so the BTree debug session shares it; `BTreeVisualizerRenderer` routed to it) · ② `Hrot.BTree.Editor` — `BTreeDebugSession` takes the registry (composer forwards it) · ③ `Hrot.Blueprints.Editor` — `BlueprintDebugSession.SetBehaviorRegistry` + `CaptureLiveBehaviorState`, built from the session's own decode. All additive; existing rails unchanged |
| ⚠ gate | `design-digest.py --check` fails on your `DESIGN_Behaviour_Fault_And_Teardown.md` (`d735abd13`): no `INVENTORY` block. Not touched here |
| ⚠ stale cref | `HillAttackIntegrationTests.cs:50` `<see cref="AreaQueryInitializationSystem"/>` names a type that no longer exists (it did not exist before this batch either) — yours to drop |

## 3a. Unified, not duplicated *(user, `2026-10-01`: "share and unify, do not duplicate · use codebase memory, not just grep")*

⛔ The first build carried two duplicates; a `search_graph` sweep (design §⛔ HISTORY lists the queries) found their existing owners, and both were folded in:

| deleted | now routed to |
|---|---|
| `BlueprintBehaviorStateReader` (a fifth decode of a blueprint's state) | `BlueprintDebugSession` — its exact struct read + fixed-list formatting; `CaptureLiveBehaviorState` returns the same `BlueprintStateSnapshot` as an Instance blueprint |
| a per-tree BTree symbolication table fed by the catalogue | `BehaviorRegistry.TryGetTreeBlob` — the lookup `BTreeVisualizerRenderer` already did, now one function both call |

⚠ Not duplicated but worth your eye: `RootParamsProjection` (inspector) and `LiveBlackboardValueProvider` (editor watch) decode a root block with `Marshal.PtrToStructure`, which the Blueprint session's own comments rule out (*"the two differ on bool"*), and it counts an `[InlineArray]` as one element. A Behavior blueprint's `State` hits that fallback in the inspector. Not changed here (behaviours-lane UI); routing them to the session's struct arm would make it one decoder everywhere.

⚠ `/entities/{id}/variables` on the **C#** commander lists *"2 blueprints (0x48E4DCC0, 0x0EFC6267)"* because `BlueprintTierSummary.AppendSlots` lists **every** occurrence slot (BTree root state, root params) and names unregistered ones by hex — a discovery over-report, not an attachment. Not changed here.

## 4. Gates *(base `a61b51973`)*

| gate *(all `--no-build` after a build of the TEST project)* | result | vs base |
|---|---|---|
| `Hrot.SimHost.Tests` (incl. `HillAttack*`, `EqsModuleTests`) | 1014 / 3 / 3 skip | ⚠ the 3 reds are the ones `CE-478` records at base (`NodeRolePersistenceRails`, `MapPresentationParityRails` Stride row, `FullBranchPipelineTests`); total 1020 vs 1027 = the deleted AreaQuery rails + `SC-HA002-*` + `SC_HA015_6`, +3 `AreaTemplate_*` |
| `ClusterRunner` `EqsDistributedTests` + `TheClusterAiDebugSurfaceAnswersTests` + `EventSerializationHelperTests` + `CgfSubsystemHeadlessTests` | 24 / 3 | ⚠ the 3 reds are `CgfSubsystemHeadlessTests` (`SimHost_WanderMission…`, `SimHost_MoveToLocation…`, `CGF_MovingVehicle…`) — pre-existing, untouched; ClusterRunner `Eqs` folder: 47 / 31, the 31 the pre-existing `EditorHarness` reds, every `EqsDistributedTests` green |
| AI-debug + mapper rails, after the unification | 8 / 0 | new |
| `Hrot.BTree.Editor.Tests` | 637 / 0 | +1 rail |
| `Hrot.Presentation.Tests` | 298 / 0 | — |
| `Hrot.Blueprints.Tests` `~Debug` | 329 / 0 / 2 skip | — |
| `Hrot.Editor.Tests` | 438 / 1 / 1 skip | ⚠ `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — GC-order flake: 439/0 in the previous full run, passes alone twice |
| `Hrot.Network.NED.Tests` · `Hrot.Map.Common.Tests` · `Hrot.Network.BDC.Tests` | 119 / 0 · 51 / 0 · 8 / 0 | — |
| `Hrot.NED.Tests` | ⛔ does not build | pre-existing orphan: not in the solution, references `Hrot/Network/Hrot.NED/Hrot.NED.csproj`, which no longer exists |
| `Fdp.Toolkits.Tests` | ⛔ does not build | pre-existing: `ChannelArbitrationTests.cs:270` (§3) |
| red-proofs | ✅ | `aiDebugSurface: null` ⇒ the cluster BTree rail fails · the BTree rail fails with the registry lookup disabled · the mapper rail is red on the old code |
| `design-digest.py --check` · `mermaid-check` · `tracker-counts.py --check` | ✅ · ✅ · ✅ | |
| live `--mode all`, both commanders | ✅ | see the `CE-476` tracker row (hostiles `Health 0`; C# `activeNode` changes; blueprint `Phase`/`Sensor`) |

