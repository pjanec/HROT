<!--STATUS
state: LIVE
updated: 2026-11-02
current-answer: §1 (what was built, per item) · §2 (gates) · §3 (findings and open debt)
stale-below: nothing
related-designs:
  - docs/DESIGN_Scenario_Macros.md — OWNS the design; §5.1.3, §15.1, §15.2, §15.3 carry this batch's as-built
  - docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md — the dispatch (scope frozen at d358a8728)
-->

# Report — backend → coordinator: Scenario Macros S3a

Dispatch: [`HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md`](HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md), scope frozen at
`d358a8728`. Branch `backend`. Every item's as-built is folded into [`DESIGN_Scenario_Macros.md`](../../DESIGN_Scenario_Macros.md) (§5.1.3 · §15.1 ·
§15.2 · §15.3); this report only points there.

## 1. What was built

| # | item | commit | design |
|---|---|---|---|
| ① | the ONE param binder: `BindableNode`, `IParamSource`, `ParamBinder`, `ParamBindings` (469) — committed first and announced to behaviors | `3683f1487` (+ note `2563f21d2`) | §5.1.3 |
| ② | the macro FILE: `macros/<path>/scenario.json`, header `Kind`/`Params`/`Anchor`/`Placement.MaxShift`/`Relocatable`; sample `macros/test/fire-team` | `6fc54c7e7` | §15 |
| ③ | `MacroInstance` (470, immutable record), `MacroExpansionStatus` (471, unmanaged), `MacroOrigin` (472, unmanaged), wire twins (dt 158/159/170), TKB `ScenarioMacro` 8813 | `29c2ea623` | §15 |
| ④ | the EXPANDER on the creation path: files → ordinary child requests (flatten, bind, resolve, relocate, GUID `macro:<local>`, transient), `MacroExpansionSystem`, the load waits on `MacroExpansionIntent` (473) | `02659da06` | §15.1 |
| ⑤ | PLACEMENT as a solver query: `PlacementRequestBatch` → `PlacementSolverSystem` → `PlacementResponseBatch`, wire dt 171/172; one batch per expansion, any `Failed` slot fails it all, no answer in 300 frames ⇒ the same solver code on this node (E-6) | `92ed7a881` + wiring in `96078e74a` + live fixes `832bc466a` | §15.2 |
| ⑥ | VALIDATOR in the prepare (SR-149/161/162/156) · `@Key` duplicates first-match with a warning (SR-188) · reconcile by GUID on an args/anchor edit, destroyed stays destroyed (SR-150/172) · cascade on instance removal (SR-170) · `MacroExpandedWith` (474) | `96078e74a` | §15.2 |
| ⑦ | the macros package to EVERY node (SR-80/148) · scenario args from `POST /scenario/load/live {args}` and `--param name=value` to every node's load, applied by the ONE params routine (`ScenarioParams`, the expander uses it too), kept as `RecordingMetadata.ScenarioArgs` (SR-41/176) | `d8991c55b`, `33321d750` | §15.3 |

Ids allocated: components 469–474; descriptors 158, 159, 170, 171, 172; TKB 8813. No tracker row allocated.

## 2. Gates

| gate | command | result | vs base |
|---|---|---|---|
| toolkits (touched classes) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "ScenarioParams\|MacroValidator\|ParamBinder\|Orchestration\|Placement\|PathfindingSolver\|NavigationSolver\|MacroTypes"` | 161/0 and 42/0 | new rails green |
| SimHost (touched classes) | `--filter "ScenarioLoadStep\|NodeOpSlaveTranslator\|MacroExpander\|LoadPhase\|EntityCreationPack\|StagingEntityExtractor\|StrictNetworkSeparation\|NodeRolePersistence"` | 62/0 and 123/0 | ⚠ 2 reds in the wider `LoadPhase` filter: `EveryEcsHostComposesTheLoadPhaseChainRails` ×2 on `CgfSubsystem.cs` — **red before this batch**: the file has no `FromRoles` at `3683f1487^` either, and this batch never touched it |
| orchestrator (whole) | `dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests --no-build` | 416/0 | — |
| runner (filtered) | `--filter "RunMode\|Startup\|Configuration"` | 81/0 | — |
| NED (whole) | `dotnet test Hrot/Network/Hrot.Network.NED.Tests --no-build` | 176/1 | the 1 red `AllTopicStructsHaveDdsTopicAttribute` (`FeatureOverrideWire`) is red at base |
| `DistributedScenarioLoadTests` alone | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter DistributedScenarioLoadTests` | ✅ 1/0 — §2a | — |
| live `--mode all` | `scenarios/test-macro-two-teams --load live --param side=Friend` | ✅ — §2b | — |
| docs | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | OK · OK (3 "source changed" warnings, pre-existing) · OK · 14/14 | — |

Red-proofs (each a compiling mutation, restored): item ④ children not transient ⇒ red · item ⑤ solver ignoring `MaxShift` ⇒ red ·
item ⑥/⑤ a `Failed` slot ignored ⇒ `Placement_OneFailedSlot_…` red · item ⑦ `ScenarioParams` not applying bindings ⇒ 2 of 4 red.

### 2a. `DistributedScenarioLoadTests`

✅ `DistributedLoad_TranslatesNetworkIds_AndSpawnsEntitiesWithRemappedMissionPlan` passed in **13 s** (a real 3-node boot + load; the summary line's `< 1 ms` is the runner's misreport — the per-test logger gives 13 s).

### 2b. The live run

Two instances of `test/fire-team` (one with identity rotation = east, one turned 90°), an observer whose side is the scenario param:
3 entities extracted → both instances `Done` with no fallback reason → 8 transient men, each pointing at its own instance, per-instance
args `Friend` / `Hostile`, the turned team rotated; the observer is `Friend` (the `--param`). Placement: CGF sent 2, SimHost received 2, answered 2.

## 3. Findings, deviations, open debt

| | |
|---|---|
| ⚠ deviation E-3 | the extractor is NOT moved to `Hrot.Common`: IG may not wire one (`NodeRolePersistenceRails`), so the expander takes the existing `IScenarioEntityExtractor` seam — §15.1 |
| ⚠ found while building ⑤ | the navigation solver ticks only when sim time advances, but a scenario loads PAUSED ⇒ placement now runs every frame on the main thread — §15.2 |
| 🔴 found LIVE, fixed | the placement translators sat in pool-gated packs CGF composes without a pool ⇒ CGF never asked; and both topics were keep-last-1 ⇒ two requests in one frame lost one. Neither is visible to a unit rail — §15.2 |
| ⏭ open | `@Key` args resolve only by GUID at runtime (the live world does not register `RefKey`) · two instances in one frame are placed against the same world (may overlap each other) · a reconcile re-applies only `SimTransform`/`EntityInfo` and does not re-place · after a restore `MacroExpandedWith` is absent ⇒ no reconcile until the next expansion · an edit load takes no scenario args · a child's layer comes from its file's DIS type (0 ⇒ infantry) |
