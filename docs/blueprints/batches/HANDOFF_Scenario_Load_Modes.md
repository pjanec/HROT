<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for HN-029 — two host-agnostic scenario-load endpoints
  (scenario/load/live + scenario/load/edit), both cluster-wide via 2PC, replacing the IEditorLogic-hardwired
  /scenario/load; then upgrade conformance's entity-inspector from DECLARED to a real content diff. Carries no
  design: cites MCP § Group U + mgmt-1 §12.
known-conflict: none. Independent of HN-028 (TIME lane, a read-only accessor on a different file).
-->
# HANDOFF — **HN-029: scenario load modes (live + edit), cluster-wide** *(harness lane)*

> 📌 **Dispatched at `73f53e11a`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push the started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J** — 📐 series stands at **`HN-029`** *(this gap)* /
> **`MX-014`**; allocate from there and state them (rules 3/5). ⚠ `HN-029` is the GAP's tracking id; your CODE
> rows are new `HN-` numbers.

> 🔒 **User, `2026-08-24`:** *"scenario/load is wrong abstraction. there are 2 load modes — live and edit …
> better having separate scenario/load/live and scenario/load/edit endpoints. both should be cluster wide.
> editor is not special, also uses 2pc for its single process."*

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`MCP_Integration.md` § Group U](../../MCP_Integration.md)** *(the MCP endpoint design, RESOLVED)* — the two
endpoints, the host-agnostic `TransitionStateIntent` seam, the live/edit/preview table, the sequence diagram, and
the built-vs-gap table. ⛔ **The state machine is NOT yours to design** — it is owned by
📄 **[`docs/designs/mgmt-1/DESIGN.md` §12/§5.5](../../designs/mgmt-1/DESIGN.md)** *(2PC · transition planner)*;
`docs/HROT architecture.md:414` shows the promote-edit-to-live trajectory. 📄 CGF authoring is blessed by
[`UXI-37` ruling 65](../../UX/UX_Feature_Cgf_Brain_Diagnostics.md). ⭐ Report per obligation ③; **fold deviations
into the owning design — `MCP_Integration.md` § Group U** *(and the harness doc §Conformance for the conformance
parts)* *(obligation ⑤)*.

## 1. ⭐⭐ THE PREMISE — measured, do not re-derive *(MCP § Group U)*

⭐ Loading is **already one 2PC mechanism**: `TransitionStateIntent` → `ClusterOpRequest(TransitionState)` →
`ClusterMaster` → `TransitionPlanner` → per-step `FanOutNodeOp(PrepareEdit|PrepareLive)` + `CommitState`. ⭐⭐ The
**editor already uses it** — `EditorApplication.LoadScenarioByName:160-171` publishes
`TransitionStateIntent{OperatingEdit}` into a **one-node** `ClusterMaster`. ⛔ **The editor is not special.**

⛔ **The one real gap:** `POST /scenario/load` is hardwired to `IEditorLogic.LoadScenarioByName`
*(`DebugApiService.cs:824`)* ⇒ `--mode all` answers `NOT_SUPPORTED_HERE(editor.authoring)`.

## 2. ⭐⭐⭐ THE ITEMS

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| 🔴🔴 **①** | ⭐⭐⭐ **`scenario/load/live`** — publish `TransitionStateIntent{TargetState=OperatingLive, ScenarioId, ExerciseId?}` to the **host's** `ClusterMaster`; wait for `OperatingLive` readiness | MCP § Group U | ⛔ **do NOT call `IEditorLogic`** — that is the editor-only trap this replaces. ⭐ **No new handler** — `HrotScenarioLoadHandler`+`ReferenceLiveLoadHandler` exist on SimHost·CGF·editor. ⚠ the endpoint needs the **orchestration bus / a publish seam** the host owns *(same shape as HN-028's master access — wire it at the ClusterRunner host)* |
| 🔴🔴 **②** | ⭐⭐⭐ **`scenario/load/edit`** — same, `TargetState=OperatingEdit` | MCP § Group U | ⭐ editor + SimHost have `HrotEditLoadHandler`; ⚠ **CGF has NO edit-load handler** ⇒ on `--mode all`, edit is **partial** *(SimHost loads, CGF does not)*. ⛔ **Do NOT add the CGF handler in this batch** — it is a CGF-lane follow-up *(ruling 65)*; instead **declare `load/edit` absent-on-CGF in the capability manifest's known-absent baseline** so the gap is honest, not a crash |
| ⭐⭐ **③** | ⭐ **`/scenario/load` → alias for `scenario/load/edit`** *(its current editor behaviour is `OperatingEdit`)* | MCP § Group U | ⭐ keeps `GoldenCaptureFixture`/`McpClient.LoadScenarioAsync` working. ⛔ **do not silently change what existing callers get** — the golden fixture authors in edit; keep it edit. ⚠ or migrate the callers to `load/edit` explicitly and retire the alias — state which you did |
| 🔴🔴 **④** | ⭐⭐⭐ **Readiness contract** — `OperatingLive`/`OperatingEdit` gates, mirroring the existing `OperatingEdit` wait *(`DebugApiService.cs:834`)* | MCP § Group U | ⛔ **handle the reload level-vs-edge race** already documented at `DebugApiService.cs:841-849` — a reload can satisfy a bare state check before the new world exists. ⭐ `--mode all` live must wait for genesis materialization, not just the state bit |
| 🔴🔴 **⑤** | ⭐⭐⭐ **Upgrade conformance: `entity-inspector` DECLARED → REAL CONTENT DIFF** — load the SAME scenario **live in BOTH** editor and `--mode all`, then diff | Q54 · §Conformance | ⭐⭐ this is the payoff — the *"load S in both, then diff"* sequence becomes executable. ⛔ remove `entity-inspector` from the DECLARED/known-absent set once it compares for real |
| 🔴🔴 **⑥** | ⭐⭐⭐ **PROVE the content diff FAILS on demand** — perturb one world's loaded content, confirm the `entity-inspector` diff reddens naming the path, revert | §8-style | ⛔ **mutation table** *(the `N4` standard)*; **rebuild before concluding** *(stale-binary trap)*. ⛔ a content diff never seen red is decoration |
| ⭐ **⑦** | **manifest describes the new endpoints** | Q54 § manifest | ⭐ they are enumerated from the route table **automatically** — ⛔ do not hand-author them. ⚠ confirm `unclassifiedRoutes: []` still holds; classify the two routes if the classifier needs it |

## 3. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐⭐ **publish seam in `--mode all`** | the endpoint must reach the host's `ClusterMaster`/orchestration bus to publish the intent. In the editor `EditorApplication` owns it; in `--mode all` wire it at the ClusterRunner host *(the same composition-root wiring as the lifted API)*. ⛔ do NOT reach into `OrchestratorSubsystem` internals *(TIME/orchestrator lane)* — publish an INTENT onto the bus, which is host-agnostic by design |
| ⚠ **live vs edit vs preview** | ⛔ **edit ≠ preview.** `load/edit` = `OperatingEdit`/`HrotEditLoadHandler` *(authoring)*; preview = `PreviewClusterOpHandler` snapshot/rewind *(NOT a file load)* — do not conflate |
| ⚠ **CGF edit gap is DECLARED, not fixed** | item ② — the manifest baseline carries it; ⛔ a follow-up batch (CGF lane) adds the handler |
| ⚠ **conformance mode choice** | for the content diff, **live in both** is the fully-built path *(all live handlers exist)*; edit-in-`--mode all` is partial until the CGF handler lands |

## 4. ⛔ LANE & SCOPE

⭐ **Yours (harness/editor lane, Area J):** `Hrot.Editor/DebugApi/*` *(the two endpoints, the alias, readiness)* ·
the ClusterRunner-host publish-seam wiring *(`Program.cs`)* · `Hrot.SystemTests/Conformance/*` +
`McpClient`/fixtures · the capability manifest classification.

⛔⛔ **NOT yours — STOP-and-report:**
- ⛔ **`OrchestratorSubsystem.cs` / `ClusterMaster` / `Fdp.Toolkits/Time` internals** — you PUBLISH an intent, you do not edit the master *(that keeps this host-agnostic; it is also the HN-028 boundary)*.
- ⛔ **the CGF edit-load handler** — CGF-lane follow-up *(ruling 65)*, not this batch.
- ⛔ variable/blackboard/`AiShared` panels *(UI-lane freeze)*.

⚠ **HN-028 may run in parallel** *(TIME lane, a read-only accessor on `OrchestratorSubsystem`)* — no file overlap.
⭐ **Rule 4: pull the coordinator branch before your final commit.**

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs `73f53e11a`** ·
`--no-build` column · every RED pre-existing **by name** · golden movement as a diff shape · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the ids you allocated**.

⭐⭐ **Row 8 — integration:** `bash scripts/run-system-tests.sh` *(baseline `80/80`)* + **item ⑥'s content-diff
mutation table** + a run that **loads live in `--mode all` and in editor and diffs `entity-inspector`**. 📐 name
whether the DDS-allocator crash recurs *(pre-existing if so, with the base sha)*.

## 6. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`MCP_Integration.md` § Group U](../../MCP_Integration.md)**
*(the endpoints' final shape, the alias decision, the readiness contract)* and **`DESIGN_Headless_Testability.md`
§Conformance** *(the `HN-029` gap note → resolved; `entity-inspector` now a content diff)*. ⛔ Design content in the
design; the report points at it. ⭐ Close `HN-029` and state the new ids in the tracker; leave the **CGF edit-load
handler** as a named open follow-up.
