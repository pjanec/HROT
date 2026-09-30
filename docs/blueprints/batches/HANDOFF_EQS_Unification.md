<!--STATUS
state: LIVE — DRAFT FOR THE BACKEND LANE (not dispatched; the user relays it)
updated: 2026-09-30
current-answer: the whole file — a FRAME handoff (goal, measured state, fences, decisions with leans, acceptance). The
  backend session designs the detail (inventory, UML, seams) in docs/ as step 1.
stale-below: nothing.
known-rot: none.
known-conflict: Architect_Question_6_Access_Shapes_And_Vocabulary.md Q6-D ("do NOT force the batch query into the
  SpawnEqsSensor template path") — overtaken by the user's 2026-09-30 ruling below; Q78 §6 records the revision.
related-designs:
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns EQS 1.3 (sensor, templates, generators, solver). §1 says it
    "upgrades the engine's current minimalistic AreaQuerySolverSystem"; §5.5 already lists an EntitiesInArea generator.
  - docs/designs/hill-attack/DESIGN.md — Phase 1 owns the AreaQuery system and the doctrine that uses it (the invariant).
  - docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md — §5.3 is the measurement this frame rests
    on; the blueprint hill attack is the consumer waiting for this.
  - docs/projects/FDP/Toolkits/Fdp.Toolkits.Spatial.Eqs.md — :742 already calls the AreaQuery types "legacy".
-->

# HANDOFF — unify the two EQS systems: AreaQuery folds into EQS 1.3

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). **Status:** draft — ⛔ not
dispatched. Stamp `Dispatched at <sha>` when it is; from then the scope is frozen at that sha.

## 1. Goal — the user's words

> 🔒 *"i would like to unify the EQS systems, likely by integrating area query into EQS 1.3 stuff (as it seems newer) so
> i can use the new system in the blueprints as planned."* — 2026-09-30

⇒ ONE environment-query system: EQS 1.3. AreaQuery's one capability (entities of a force inside an area polygon)
becomes an EQS generator. Its callers move over. Its pipeline is then retired — ⛔ **not before parity is proven.**

## 2. What exists — measured `2026-09-30` *(a state doc — re-verify before building)*

| | **AreaQuery** (live) | **EQS 1.3 / `EqsSensor`** |
|---|---|---|
| request → result | `AreaQueryRequestEvent` → `AreaQuerySolverSystem` (`CognitiveSpatialModule`) → `AreaQueryResultEvent` → `AreaQueryResultMaterializationSystem` → `AreaQueryBatchData` (64-slot ring) + `EqsTargetPool` | a persistent child-entity `EqsSensor` + template → `EqsSolverSystem` (`EqsModule`) → `EqsCognitiveBuffer` |
| shared? | ⛔ separate solvers (`EqsModule.cs:17-19`); only the namespace and `EqsTargetPool` are shared | |
| answers | force-filtered point-in-polygon (`AreaQuerySolverSystem.cs:152,156`) | radius / cover / navmesh generators; ⛔ **no `EntitiesInArea`**, though design §5.5 lists it |
| production callers | the C# hill attack: `HillAttackCommanderNodes.cs` — `Action_RequestAreaQuery` :189, `Condition_IsAreaQueryResolved` :238, `Action_DispatchWaveWithTargets` :288, `Condition_IsWaveCompleted` :439, `Deactivate_RequestAreaQuery` :527; plus `TargetPoolOps`, `AreaQueryBatchOps` (blueprint helpers) | `HideInCoverBehavior` only — whose BTree is *"registered as behaviours nowhere"* |
| 🔴 production state | ✅ proven by `hill-attack-close` | ⛔ **INERT — [`CE-465`](../Blueprint_Issues_Tracker.md):** nothing outside tests installs `IEqsTemplateRegistry`, so `EqsSolverSystem.cs:143-158` returns the empty stub for every sensor. Only 1 of the design's 8 starter templates exists (`FindCoverFromTarget`) |
| network | `Hrot.Network.NED/SimHost/AreaQueryTranslators.cs` | sensor replication (`EqsDistributedTests`) |
| tests | 12 files | 81 files — all with a test-installed registry |

## 3. The items

| # | item | lean |
|---|---|---|
| **①** | **Make EQS 1.3 live** — install the template registry in production from the `[EqsTemplate]` registrar (`CE-465`) | it is the precondition for everything else; `EqsModule.cs:21` claims hot reload picks templates up, but `AiHotReloadCoordinator` contains no EQS code — measure and fix |
| **②** | **`EntitiesInArea` generator** (design §5.5) + a template reproducing AreaQuery exactly: entities of force *F* inside the polygon of an area entity | ⭐ **parity by construction** — reuse `AreaQuerySolverSystem`'s polygon + force test code, do not re-derive it |
| **③** | **parity rail:** the same world, same area, same force ⇒ identical entity sets from both systems | the rail that licenses ④ |
| **④** | **retire AreaQuery** — events, solver, materialisation, `AreaQueryBatchData`, translators, `AreaQueryBatchHelper` | ⛔ only after ③ is green **and** the callers have moved (⑤). "No rush removals" applies |
| **⑤** | **move the callers** — the C# hill attack's 5 methods and the blueprint helpers | ⚠ **cross-lane** (see §5) |

## 4. Decisions for the session — each with a lean

| | question | lean | what would change it |
|---|---|---|---|
| **D1** | keep an AreaQuery-shaped *facade* (request/poll/free over a one-shot sensor), or move callers to the sensor model? | **move callers; no facade.** A facade is a second API for one system — the thing this unification removes | a caller whose lifecycle truly is fire-and-forget and cannot own a child sensor |
| **D2** | where does the result pool live? | EQS's own buffer; `EqsTargetPool` goes with AreaQuery unless EQS already reads it | the session's inventory |
| **D3** | order | ① → ② → ③ → ⑤ → ④, each its own commit | — |
| **D4** | network | the polygon test runs where the solver runs today; the session must show the `--mode all` cluster still delivers results to the commander's node | a measured cross-node gap |

## 5. Fences

| ✅ backend lane owns | ⛔ not yours — STOP and report |
|---|---|
| `Fdp.Toolkits/Spatial/Eqs/**`, `Hrot.SimHost/Systems/*Eqs*`, `*AreaQuery*`, `EqsModule`, `CognitiveSpatialModule`, the translators, the template registry | `Hrot/Subsystems/Hrot.AI.Behaviors/**` — the hill-attack doctrine and the blueprint helpers belong to the **behaviours lane**. ⭐ Deliver ①–③ and a written migration recipe for ⑤; the behaviours lane moves its callers; **then** ④ |

⚠ **Touch-point:** `AreaQueryBatchOps` (behaviours lane) was made generic on `2026-09-30` (force is an argument, plus
`TargetAt`) as the stop-gap until this lands. It is the second caller ⑤ must move.

## 6. Step 1 — the session designs the detail *(the FRAME rule)*

Investigate → write the design in `docs/` (extend `EQS_Design_v1.3_final.md` or a sibling), with an `INVENTORY`
(`search_graph` for every `*AreaQuery*`, `*Eqs*`, `*TargetPool*` type and every registration site), a
`classDiagram`, a `sequenceDiagram` and a **module diagram showing who registers and who ticks each system on each
host** (SimHost, Stride, editor, ClusterRunner) → build → fold the as-built back. ⭐ Add a STATUS block to
`EQS_Design_v1.3_final.md` — it has none. ⭐ Allocate your own ids.

## 7. Acceptance

- ⭐⭐⭐ **The invariant:** `hill-attack-close` still works exactly as before — both targets destroyed, the platoon back on
  its baseline — in `SimHost` (`HillAttackIntegrationTests`) **and** in a live `ClusterRunner --mode all` run
  (`docs/RUNBOOK_Cluster_Debugging_Over_Http.md`).
- EQS returns non-empty results in production (a rail that fails if the registry is missing — `CE-465`'s red-proof).
- The parity rail (③), red-proofed.
- The EQS suites stay green, run by their own filters (`ClusterRunner.Integration.Tests` `Eqs/*` — gate row 8).
- Nothing named `AreaQuery*` remains, and `docs/projects/FDP/Toolkits/Fdp.Toolkits.Spatial.Eqs.md` says so.
