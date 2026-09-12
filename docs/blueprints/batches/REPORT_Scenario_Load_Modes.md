<!--STATUS
state: LIVE
build-state: BUILT — all 8 items. ⚠ Item ⑤ landed as DIAGNOSED rather than SAME; see §3 deviation ④.
updated: 2026-08-24
current-answer: §1 per item · §2 what the measurements changed · §3 obligation ③/⑤ (six deviations) ·
  §4 §Gates · §5 the mutation table · §6 the ids · §7 what is still open.
design-basis: 📄 docs/MCP_Integration.md § Group U + its § AS-BUILT (written by this batch) ·
  docs/designs/mgmt-1/DESIGN.md §12/§5.5 (the 2PC state machine — NOT this batch's to design) ·
  docs/DESIGN_Headless_Testability.md §Conformance · docs/blueprints/batches/HANDOFF_Scenario_Load_Modes.md
  (dispatched 396337d74).
known-rot: none. ⚠ EPHEMERAL — the durable record is MCP_Integration.md § Group U AS-BUILT.
-->
# REPORT — **HN-029: scenario load modes (live + edit), cluster-wide**

> ⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ The durable record is
> **[`MCP_Integration.md` § Group U AS-BUILT](../../MCP_Integration.md)** and
> **[`DESIGN_Headless_Testability.md` §Conformance](../../DESIGN_Headless_Testability.md)** — both written
> before this batch closed *(obligation ⑤)*.

🔒 **User, `2026-08-24`:** *"scenario/load is wrong abstraction. there are 2 load modes — live and edit …
both should be cluster wide. editor is not special, also uses 2pc for its single process."*

⭐⭐ **Headline:** `--mode all` loads `hill-attack` **live** and materialises **8 entities**
*(`clusterState: OperatingLive`)*, and the conformance sequence this design has wanted since it was
written — *"load S in BOTH, then diff"* — **now runs**. ⚠ Two of the eight items landed differently from
the handoff's expectation, both because measurement said so; §3 has all six deviations.

## 1. ⭐⭐ PER ITEM

| # | item | state |
|---|---|---|
| ✅ **①** | `scenario/load/live` | **BUILT.** Publishes `TransitionStateIntent{OperatingLive, ScenarioId, fresh ExerciseId}` to the host's own `ClusterMaster`. ⛔ No `IEditorLogic`, no new handler |
| ✅ **②** | `scenario/load/edit` | **BUILT.** ⚠ CGF's missing edit-load handler is **declared**, not fixed *(`HN-039`)* — a CGF-lane follow-up |
| ✅ **③** | `/scenario/load` → alias for `load/edit` | **BUILT as an alias; callers NOT migrated** *(stated, per the handoff's "state which you did")*. ⭐ `GoldenCaptureFixture` and `McpClient.LoadScenarioAsync` are bit-identical; the MCP catalog marks `load_scenario` deprecated |
| ✅ **④** | readiness contract | **BUILT** for both target states, edge-triggered, degrading on a clockless/worldless host. 🔴 And it was the item that exposed the real gap — §3 deviation ② |
| ⚠ **⑤** | `entity-inspector` DECLARED → real content diff | **The DIFF IS REAL AND RUNS.** ⛔ But the declaration is NOT removed: measured, the IG node also lists a node-local entity ⇒ DECLARED → **DIAGNOSED**, not SAME. §3 deviation ④ |
| ✅ **⑥** | prove the content diff fails on demand | **PROVEN** — §5, plus two unplanned reds |
| ✅ **⑦** | the manifest describes the new endpoints | **BUILT.** Enumerated from the route table, `unclassifiedRoutes: []` still holds; ⭐ classified as a NEW capability `scenario.load`, not `editor.authoring` |
| ✅ **⑧** | reconcile the agent-facing MCP surface | **BUILT** — catalog + partials + `get_capabilities` + the server's own handlers. `test:catalog` 521/521, `gen:skill:check` PASSED |

## 2. ⭐⭐⭐ THE PREMISE HELD — **and that is the interesting part**

⭐ The handoff said *"do not re-derive: loading is ALREADY one 2PC mechanism."* 📐 **Confirmed, and stronger
than stated:**

| 📐 measured | |
|---|---|
| ⭐⭐ **every host already has a bus that reaches a `ClusterMaster`** | **directly** on the orchestrator *(`_bus`)* and — ⭐ this is the load-bearing one for the user's ruling — on the **editor's own ONE-NODE master** *(`EditorSubsystem:1702`, `new ClusterMaster(_orchestrationBus, offlineConfig)`)*; via `ClusterOpEgressTranslator` → DDS → `ClusterOpMasterTranslator` on **CGF · SimHost · IG** *(all three through `NodeBootstrapper:194-200`)* and **ExCon** *(`ExConSubsystem:332`)* |
| ⭐⭐⭐ **both load modes already existed as UI** | `ClusterScenarioPanel`'s **"Load into Edit"** *(`OperatingEdit`, `ExerciseId = Guid.Empty`)* and **"Load into Live"** *(`OperatingLive`, fresh `ExerciseId`)*. ⇒ ⭐ the endpoints copy that shape rather than inventing one — the seam law's usual answer *(the thing exists and is under-adopted)* |
| ⭐ **one implementation of the publish lambda** | `SubsystemDebugProvider.TransitionsVia(Func<FdpEventBus?>)`, not four copies in four subsystems *(ruling 9)* |

⛔ **And the seam exposed is a narrow `Action<TransitionStateIntent>`, NOT the bus** — handing out an
`FdpEventBus` would let the debug host inject anything into a node's control plane. 📌 The same reasoning
that made `HN-028` expose one `bool?` instead of a whole controller.

## 3. ⭐⭐⭐ OBLIGATIONS ③ AND ⑤ — **six deviations**

**③ — the design's UML/sequence was checked** *(`MCP_Integration.md` § Group U's `sequenceDiagram`)*. The
built path matches it: `POST` → dispatcher → host's `ClusterMaster` → `TransitionPlanner` → per-node
fan-out → readiness gate. **Six deviations:**

| # | the design said | 📐 measured / built |
|---|---|---|
| **①** | *"the editor is not special"* | ⭐ **True of the MECHANISM**, and `load/live` has no special case at all. ⚠ `load/edit` still prefers `IEditorLogic.LoadScenarioByName` when present, because that driver does an `Idle` round-trip and a **local wipe** *(`NewScenario()` → `WorldResetEvent` + `SoftClear`)* which on a cluster is each node's `HrotEditLoadHandler`'s job. ⇒ same intent, one extra hop with nothing to do elsewhere — ⭐ and every existing caller stays bit-identical |
| **②** | *(implicit)* the endpoint needs a publisher | 🔴🔴 **It also needs the cluster STATE, and that was unwired.** 📐 The first cluster load PUBLISHED correctly — *"TransitionStateIntent accepted"*, *"fan-out complete (nodes=5)"* — and then answered **`NOT_SUPPORTED_HERE(cluster.state)`**. ⛔ The load worked and the reply said unsupported: deviation ⑤'s shape one layer up. ⇒ `ClusterStateAnyNode`, from ExCon's pumped `ClusterUiCache` |
| **③** | *(unstated)* | 🔴🔴 **The scenario was not on the NAS** — prefetch failed on `<staging>/shared/scenarios/hill-attack`. ⭐ Fixed by REUSE: `CuratedScenarios.SeedIntoWorking`, whose own doc already said *"host-agnostic … CGF or any other host can call the same helper"*, was never called by the runner. ⛔ **Gated on `HROT_DEBUG_API_PORT`** — it force-overwrites curated NAMES in the operator's working NAS, and the user is running the cluster runner beside their own work |
| **④** | item ⑤: remove `entity-inspector` from the declared set | ⚠⚠ **NOT removed — and this is a measurement, not a shortfall.** 📐 Same scenario live in both: the IG inspector lists **10 rows to the editor's 9**, carrying a **node-local entity** *(networkId 0, unnamed)*. ⇒ the entry survives with a MEASURED reason instead of a tooling gap, and the scenario content is compared for real by a dedicated rail. ⛔ Deleting the entry would have made the suite red forever on a true host difference |
| **⑤** | *(unstated)* the loaded worlds are comparable by id | 🔴🔴 **THE IDS DIFFER: editor `1000–1007`, cluster `2–9`.** Same seven entities, same names, same order; **different allocator authorities** *(offline allocator vs the centralised `DdsIdAllocatorServer`, `mgmt-1` §5.7)*. ⇒ entities are matched by NAME; the divergence is filed as **`HN-037`** and pinned by a tripwire rail that reddens *and is deleted* if `D6` unifies them |
| **⑥** | *(unstated)* equalise the worlds inside the existing diff | ⛔ **Two rails, not one.** 📐 Loading inside the STRUCTURAL diff made `mission` newly DIFFERENT *(`selectedEntityId` 0 vs 9, `commitButtonEnabled` false vs true)* — ExCon's mission panel carries a **local selection** once there is something to select. ⚠ Local selection is per-host by nature; folding it in would have forced a whole-panel exemption and hidden real regressions inside `mission` |

**⑤ — folded into the owning designs before this batch closed:**

| doc | what changed |
|---|---|
| 📄 **[`MCP_Integration.md` § Group U](../../MCP_Integration.md)** | new **§ AS-BUILT**: the shipped seam, all six deviations, what else shipped, and the two open items. STATUS `current-answer` now points at it first |
| 📄 **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** | §Conformance's *"the worlds cannot be equalised"* row **replaced** by the built state, with the prior text marked SUPERSEDED inline and the two things it did NOT deliver named |

## 4. ⭐⭐⭐ §GATES

| # | gate | verbatim command | `--no-build`? | result · delta vs `396337d74` |
|---|---|---|---|---|
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **0 errors** *(rebuilt before every conclusion — the stale-binary trap)* |
| 1 · 8 | ⭐⭐⭐ **the integration gate** | `bash scripts/run-system-tests.sh` | builds | ⭐⭐ **83 / 83 pass, 0 fail, 0 skip** *(baseline `81/81` after HN-028 ⇒ **+2**, the two new content rails)*. ⚠ One earlier full run failed `DeterminismRails.Two_fresh_processes_agree_on_the_entity_mapping` — see row 4 |
| 8 | ⭐⭐ **`--mode all` loads live, end to end** | `HROT_DEBUG_API_PORT=… xvfb-run dotnet Hrot.ClusterRunner.dll --mode all` + `curl -X POST /scenario/load/live` | n/a | ⭐ `{"loaded":"hill-attack","awaited":true,"target":"OperatingLive","entityCount":8,"sawWorldChange":true,"hadWorldAnchor":true}`; `clusterState: OperatingLive`. ⭐ Works from the **Scenario**, **SimHost** and **ExCon** perspectives |
| 8 | the editor forwarding rail | `dotnet test Hrot.Editor.Tests --filter FullyQualifiedName~DebugApiCompositionTests` | `--no-build` | ⭐ **5 / 5** *(baseline 4 ⇒ +1: `requestTransition:` is now asserted by name)* |
| 8 | ⭐ **the MCP catalog + skill** | `npm run test:catalog` · `npm run gen:skill:check` | n/a | ⭐ **521 / 521 passed** · **`gen:skill:check` PASSED** *(SKILL.md regenerated from `tool-catalog.mjs`, ⛔ never hand-edited)* |
| 2 | out-of-solution / stale bin | — | — | ⭐ every gated project is in `IOS-IG-SimHost.sln`; every `--no-build` run followed a full build of the same tree |
| 3 | golden movement | `git status --short` | — | ⭐ **ZERO goldens moved** *(0 created, 0 modified, 0 deleted)*. ⭐ `git status --short scenarios/` also clean — mutation M3 perturbed only the process's **staged copy**, never the committed curated file |
| 4 | every RED pre-existing, by name | — | — | ⭐ **no reds on the final tree** *(83/83)*. ⚠ `DeterminismRails.Two_fresh_processes_agree_on_the_entity_mapping` failed on ONE earlier full run and then passed **4/4 in isolation** and **83/83 on a full re-run** ⇒ **`HN-023`'s known 1-in-4 flake, recurred once.** ⛔ Still open; this batch neither caused nor fixed it |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean; mutation M3 reverted by **inverse edit** and verified *(`grep -c "MUTATION PROBE"` ⇒ **0**)* |
| 6 | quarantine counts | — | — | ⭐ **0 skips before, 0 after.** ⛔ No new filter |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | — | ⭐ **OK (open 99 / done 333)** ⚠ *(blind to `HN-`/`MX-` rows — known quirk)* · **24/24 verified, 3 staleness WARNs** *(2 pre-existing + `DESIGN_Headless_Testability.md`, edited here; its only citing ruling is `R-131`, untouched)* · **83 designs OK** · **12/12 mermaid blocks parse** |

## 5. ⭐⭐⭐ THE MUTATION TABLE — **item ⑥**

| # | mutation *(reverted by inverse edit)* | what reddened | expected? |
|---|---|---|---|
| **M3** | ⭐⭐⭐ **perturb the CLUSTER's seeded scenario only** — rename `M1 Abrams` in the copy `Program.cs` seeds *(the editor seeds from a different call site, so this is host-asymmetric by construction)* | ⭐⭐ `The_two_hosts_hold_the_same_loaded_world`: *"only in the EDITOR : [M1 Abrams] / only in --mode all : [M1 Abrams MUTATED]"* ⇒ **the content diff names the exact divergent entity** | ✅ yes |

⭐⭐ **And two UNPLANNED reds, which are better evidence than a synthetic one because nobody arranged them:**

| | |
|---|---|
| 🔴 **the content rail's FIRST run failed** | naming the id divergence entity-by-entity *(`1000:Tank Platoon…` vs `2:Tank Platoon…`)* ⇒ that is how `HN-037` was found at all |
| 🔴 **the structural rail failed** when worlds were equalised inside it | *"mission (editor_mission vs excon_mission): `$.activeTaskId` … `$.commitButtonEnabled` … `$.selectedEntityId`: golden=0 actual=9"* ⇒ that is how deviation ⑥ was found |

⭐ Both rebuilt the full solution before any conclusion was drawn.

## 6. ⭐ RULE 5 — the ids allocated

| id | |
|---|---|
| ✅ **`HN-029`** | **CLOSED** — the gap it tracked |
| ✅ **`HN-032`** | the provider seam *(`RequestTransition` · `ClusterState` · `AvailableScenarios` · `TransitionsVia`)* |
| ✅ **`HN-033`** | the readiness read — `ClusterStateAnyNode` |
| ✅ **`HN-034`** | the curated-scenario seed at the runner *(gated on the debug port)* |
| ✅ **`HN-035`** | the two conformance content rails + M3 |
| ✅ **`HN-036`** | the agent-facing MCP surface |
| 🔴 **`HN-037`** | **open** — a `networkId` is not portable across hosts *(editor 1000+, cluster 2+)* |
| 🔴 **`HN-038`** | **open** — `OwnershipUpdate` strict-mode violation during a cluster live load |
| 🔴 **`HN-039`** | **open** — CGF has no edit-load handler *(CGF lane)* |

⚠ **`HN-030`/`HN-031`** were already taken *(the coordinator's catalog-generation backlog row, and
`HN-028`'s code row from the batch before this one)*, so this batch starts at `HN-032`.

## 7. 🔴 WHAT IS STILL OPEN — **stated, not smoothed**

| ⛔ | ⭐ |
|---|---|
| **an EDIT load in `--mode all` is PARTIAL** *(`HN-039`)* | CGF has no edit-load handler. ⇒ the content diff uses **live**, where every host has handlers. ⛔ Not this lane's to add *(`UXI-37` ruling 65)* |
| **`entity-inspector` is still a DECLARED divergence** *(deviation ④)* | ⭐ but for a MEASURED host fact now, and the scenario content underneath it is compared for real |
| **cross-host ids differ** *(`HN-037`)* | ⭐ pinned by a tripwire that reddens the day they are unified |
| **`OwnershipUpdate` strict-mode violation** *(`HN-038`)* | newly observable, non-fatal; the fix is a `RegisterEvent` at a replication bootstrap and **which** one is a replication-lane question |
| ⚠ **`HN-023`'s determinism flake recurred once** | 4/4 in isolation, 83/83 on re-run. ⛔ Not evidence it is gone — and per `R-131` it stays a defect to resolve, not a filter |
