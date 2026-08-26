<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this file is a REPORT — ephemeral. ⭐ The durable record is
  docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md (§6a = the per-host reconciliation, §9 = the as-built
  delta, both folded per obligation ⑤) and the tracker row CE-051. ⛔ Do not quote this file as design.
-->
# REPORT — **CGF tool / selection / camera / rename** *(Axis-C E3)*

📄 **Design (durable, UML + as-built):** [`docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md`](../../DESIGN_Cgf_Tool_Selection_Camera_Slice.md)
📄 **Handoff:** [`HANDOFF_Cgf_Tool_Selection_Camera.md`](HANDOFF_Cgf_Tool_Selection_Camera.md) · **Dispatched at `4a48e3375`**
⭐ **IDs allocated (rule 5): `CE-051`.** ⚠ No new findings filed — the two defects found are *fixed* by this
batch, not deferred.
⭐ **Started-marker: `71f5b9996`** *(pushed before any code — rule 1b)*. **Base for every Δ below.**

## 0. ⭐ PROCESS

| # | |
|---|---|
| **P1** | ⛔ Still harness-bound to `claude/reset-working-branch-qd1qpv`. ⚠⚠ **Rule 7 needed a real MERGE this time, not a fast-forward** — my branch carried one commit *(the E2 T3 record)* the coordinator lacked. 📐 Checked first: **zero overlapping files** between my side and its 10 commits, so `git merge --no-edit` was conflict-free. ⭐ Stated because every prior slice was a clean ff and this is the first divergence. |

## 1. ⭐⭐⭐ OBLIGATION ③ — the UML checked before building, and §6 taken literally

⭐ The design carried a `classDiagram` (10 boxes) + a `sequenceDiagram` (6 participants), and — unusually —
**a §6 that told me the risk was a two-way reconciliation, not a lift.** ⭐⭐ I measured **both** sides before
writing any shared body, which is what §6 asked, ⭐⭐⭐ **and that is the only reason this batch found
anything.** Two live defects and one design-premise correction came out of the comparison, not out of the
code I wrote.

## 2. 🔴🔴 THE TWO LIVE DEFECTS — neither anticipated by the design

### D1 — **CGF's "Center on entity" sent the camera to the ORIGIN**

📐 Measured, three facts in a chain:
1. `CenterCameraOnEntity` assigned `MapCamera.Target` — i.e. `InnerCamera.Target`.
2. `MapCamera.Update` assigns `InnerCamera.Target = _targetTarget` **every frame**; `EnableSmoothing`
   defaults to **`false`**, so it is an outright overwrite, not a lerp toward it.
3. CGF never called `FocusOn`, so `_targetTarget` was still `Vector2.Zero`.

⇒ ⭐⭐ **the centre was undone on the very next frame and the view snapped to the origin.** The editor's arm
called `FocusOn`, which sets `_targetTarget` — the correct seam, and now the shared one.

⛔⛔ **The rail had to call `camera.Update()` before asserting.** ⚠ A rail that checked `Target` immediately
after centring would have **passed on the broken code** — which is precisely how this survived. That is the
one detail I would most want a reviewer to check.

### D2 — **`SelectEntityCommand` was published and read by NOTHING**

📐 Full-repo sweep: the only three references were `EditorApplication.SelectEntity` *(publish)*,
`PresentationComponentRegistry` *(`RegisterEvent`)*, and the struct itself. ⇒ ⭐⭐ **`IEditorLogic.SelectEntity(long)`
has been a silent no-op on every host** — the panel calls it, the event is registered, nothing consumes it,
and nothing ever reported that.

⚠⚠ **This corrects the design's premise.** §3 ② lists `SelectEntitySystem` beside two systems whose bodies
come out of `EditorSubsystem`'s drain; ⛔ there was no such body to extract. **It is new capability.**
⭐ And the reference count on `SelectEntityCommand` was non-zero — ⇒ this is a textbook instance of *"never
read a reference count as adoption."*

### ⭐⭐ …and CGF was BETTER in one respect, so the survivor is a MERGE

CGF preferred `NetworkTransform.LastPosition` and fell back to `SimTransform`; the editor read `SimTransform`
only. ⚠ On a host that does not **own** `SimTransform` the replicated position is the fresher one — 📌 the same
insight that gave the rotate gizmo an `EntityWriteRouter` *(`AX-005b`)*. ⇒ `CenterOnEntitySystem` takes
**CGF's component preference** and **the editor's camera seam**. 📌 The E2 create-core pattern, one increment
later: *the two copies had each learned something the other had not.*

## 3. 🔴 THE LOAD-BEARING AS-BUILT CORRECTION — every dep is a RESOLVER

⭐⭐⭐ **The HN-037 "check the captures" rule earned its place here, and it was not close.**
📐 Measured in `EditorSubsystem`:

| what | line |
|---|---|
| the module is CONSTRUCTED | `:1273` |
| `kernel.Initialize()` → `RegisterSystems` | `:1733` |
| `_camera` created | `:1801` |
| `_spawnAdapter` created | `:1942` |
| `_selectionState` created | `:1945` |
| all three set back to **`null`** | `:4756`–`:4775` |

⇒ ⛔⛔ **capturing instances in `InteractionDeps` would have wired all three systems to permanent nulls —
silently.** No exception, no log, just a tool set that does nothing. ⚠ On CGF it is sharper: those fields are
created in `RegisterWindows`, which **never runs headless**. ⇒ every member is a `Func<>`, resolved per
`Execute`, and a rail pins that a not-yet-built viewport is tolerated rather than throwing.

## 4. ⭐ WHAT MOVED, AND ONE THING THAT DELIBERATELY DID NOT

| ⭐ moved | to | why |
|---|---|---|
| `EditorTool` · `ActivateEditorToolEvent` · `CenterOnEntityCommand` | **`Hrot.Core`** *(`Hrot.Common` / `Hrot.Common.Events`)* | beside `SelectEntityCommand`/`OpenRenameDialogCommand`, already shared. The shared drain switches on the enum and lives in `Hrot.Presentation`, which cannot reference a host |
| ⭐⭐ **`EntityRotatorGizmo`** | **`Hrot.Presentation/ScenarioEditor/Gizmos`** | ⛔ **not in the item list, and without it the Rotate arm could not be shared at all** — `Hrot.Presentation` cannot reference `Hrot.SimHost`. 📐 Measured safe: its usings are all `Fdp.*`, and `Hrot.SimHost` already references `Hrot.Presentation`, so the move is acyclic and its two SimHost callers still resolve it. ⭐ Wrong assembly, not wrong shape — the E2 launcher finding again |
| the drain · the two handlers | **3 new shared systems** in `Hrot.ScenarioEditor.Systems` | `ScenarioEditorModule.RegisterSystems` is populated at last — `PACK2-E002` finished |
| the inline rename modal *(~35 ImGui lines)* | **`EntityRenameModal`** *(AiShared.Browser)* | ⭐ CGF had **no rename affordance at all** before this |

| ⛔ NOT moved | why |
|---|---|
| **`EditorSpawnAdapter`** *(a deviation from item ①)* | 📐 The drain's entire dependency on it is **one parameterless call**, while the adapter pulls in `Hrot.Map.Common`, `Hrot.UI.Common.Facades`, `Hrot.Core.Network` and a creation-request source. ⇒ ⭐ a delegate collapses the drain's duplication *(still one adapter, one drain — ruling 9)* without moving four namespaces for zero behavioural gain. ⚠ **Consequence, stated rather than hidden:** CGF composes no spawn adapter, so its `Spawn` tool now **REPORTS unserviceable** — ruling 49 applied to a tool, and railed |

## 5. ⭐⭐ THE RECONCILIATION IS GATED, NOT JUST DONE

⭐ Per-host detail is in the design's **§6a** *(durable)*. What matters here is that it **cannot regress**:

| rail | asserts |
|---|---|
| `NoCompositionRootAssignsTheCameraTargetDirectly` | neither host assigns `Camera.Target` — D1 cannot come back |
| `NoCompositionRootConstructsAToolGizmoItself` | neither host constructs any of the four tool gizmos |
| `TheEditorsDrainMethodIsGone` + `CgfRegistersTheSharedModule` | the positive half — the module IS registered on both, so a rename cannot fake a pass |

⭐⭐ **Both are SOURCE SCANS, and that is structurally necessary, not lazy.** The parallels were a **local
function** and an **inline lambda** that called the same shared primitives and referenced nothing new. ⛔ A
reference count sees nothing; reflection sees nothing; the call graph sees nothing. ⚠ Exactly how the two
paths drifted far enough for D1 to go unnoticed.

## 6. ⭐ GATES *(rule 8 — the report substitutes for a coordinator re-run)*

**Base for every "pre-existing" claim: the started-marker `71f5b9996`.** Build once, then `--no-build`.

| gate | command | `--no-build` | result | Δ vs `71f5b9996` |
|---|---|---|---|---|
| build `Hrot.Core` · `Hrot.Presentation` | `dotnet build … --no-restore` | n/a | ✅ 0 errors | — |
| build `Hrot.SimHost` · `Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` | `dotnet build … --no-restore` | n/a | ✅ 0 errors each | — |
| build `Hrot.Editor.Tests` · `AiShared.Tests` · `SimHost.Tests` · `SystemTests` | `dotnet build … --no-restore` | n/a | ✅ 0 errors each | — |
| **NEW rails** `TheViewportInteractionIsSharedTests` | `--filter …` | ✅ | ✅ **14 / 0** | — |
| **T1** `Hrot.Editor.Tests` | `dotnet test … --no-build` | ✅ | ✅ **291 / 0 / 1 skip** | **0** |
| **T1 stability** ×5 total runs | same | ✅ | 4 green, 1 red = the known `CE-050` flake | **0 attributable** |
| **T2** `Hrot.Editor.AiShared.Tests` | `dotnet test … --no-build` | ✅ | ✅ **2027 / 0 / 1 skip** | **0** |
| **T2** `Hrot.Blueprints.Tests` | `dotnet test … --no-build` | ✅ | ✅ **3965 / 0 / 18 skip** | **0** |
| **T2** `Hrot.SimHost.Tests` | `dotnet test … --no-build` | ✅ | ⚠ **768 / 1 / 3 skip** — see below | **0 attributable** |
| **RED PROOF** *(inverse edit)* | `FocusOn` → `Camera.Target =`, **and** the selection write commented out | ✅ | ✅ **2 reds** *(`CentringSurvivesTheNextCameraUpdate`, `SelectEntityCommandWritesThePrimarySelection`)*, restored **byte-identical** | — |
| `mermaid-check.mjs` | on the design | n/a | ✅ **2/2 blocks parse** | — |
| `design-digest.py --check` | as written | n/a | ✅ 92 docs: STATUS + INVENTORY + UML present | — |
| `rulings-check.py` | as written | n/a | ⚠ **25/25 quotes verify**, 2 inherited staleness WARNs | — |
| `tracker-counts.py --check` | as written | n/a | ✅ *"open 102 / done 346 (+1 refuted)"* | **0 — see caveat** |
| **T3** conformance / system suite | `bash scripts/run-system-tests.sh` | ⛔ built once by the script | ⏳ **BACKGROUNDED — result in §8** | — |

### ⚠ The two reds, attributed

| red | attribution |
|---|---|
| `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` *(Editor, 1 of 5 runs)* | ⭐ **already filed as `CE-050`** last batch — green in isolation, red under full-suite pressure, matching the backend lane's documented root cause. ⛔ My diff touches no ALC/hot-reload file |
| `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` *(SimHost)* | ⭐ **the backend lane's own `W3` row**, listed in `HANDOFF_Test_Suite_Reliability.md` §3 as *"real — investigate"*. ⛔ Stable, not a flake, and not in this batch's neighbourhood |

⚠⚠ **Stated plainly, as last batch:** I did **not** build a base worktree for either. The attribution rests
on isolation behaviour, zero diff overlap, and each red being named in another lane's live handoff.

### ⚠ Two standing caveats

1. ⚠ **`rulings-check`'s 2 WARNs are INHERITED** — `.claude/CLAUDE.md` *(the coordinator's `dabd35715`)* and
   `DataBreakpointManager.cs` *(the backend lane's `0c1121c69`)*, both arriving through the rule-7 merge.
   ⛔ My diff touches neither. **Every quote still verifies.**
2. ⛔ **`tracker-counts.py` still does not count `CE-` rows** *(its filter is `\*\*\[?BP-\d+`)* — *"counts OK"*
   means the BP tally is consistent, ⛔ not that `CE-051` was verified. Third batch running; ⭐ worth the
   coordinator's one-line fix if the `CE-` series is meant to be tallied.

## 7. ⭐ WHAT I DID NOT DO

| ⛔ | why |
|---|---|
| **`IMapPickService`** | design §2/§8 — Axis-B, a different concept *(transient click-to-resolve)* with different backing. Untouched |
| **View / inspector / property-edit** | **E4** per §8 |
| **No new tool vocabulary** | §8. The `EditorTool` set is unchanged, and ⛔ no `ITool`/`ToolManager` registry was invented — design §1 measured that none exists and E3 shares orchestration, not a framework |
| **CGF `Spawn`** | it reports unserviceable. Composing a spawn adapter on CGF is real new composition and outside E3's four behaviours |
| **`CE-047` / `CE-048` / `CE-050`** | untouched, still open |

## 8. ⏳ T3 — the system/conformance suite

⭐ Backgrounded per the handoff §1. Result appended here when it lands; otherwise it is in the next session's
first message.

> **Expectation, stated in advance so the result can contradict it:** ⭐ **no panel-model change at all.**
> This batch adds no window, menu item or toolbar entry — it re-homes behaviour behind the same events. ⇒ the
> conformance verdicts should be **byte-identical** to E2's run. ⚠ **The real risk is different in kind from
> the last two batches:** the editor now runs three tool systems through the kernel instead of a direct call
> from `Update()`, so a **frame-ordering** difference is conceivable — a tool activation published and drained
> one frame later than before. ⛔ If anything reddens, that ordering is where I would look first, ⭐ and the
> `ModeStartupRails` are the rails that would see it.

## 9. ⭐ DECISION LOG *(decide-and-log autonomy)*

| # | decision | basis |
|---|---|---|
| 1 | `EditorTool`/events → `Hrot.Core` with namespace `Hrot.Common`, updating ~10 call sites | ⛔ keeping a `Hrot.Editor` namespace inside a shared assembly is a lie that is worse in shared code than the 10 mechanical edits |
| 2 | **Move `EntityRotatorGizmo`** *(not in the item list)* | without it the Rotate arm cannot be shared; measured acyclic, usings already all `Fdp.*` |
| 3 | **`EditorSpawnAdapter` NOT lifted**; a delegate instead | one parameterless call vs four namespaces |
| 4 | Every `InteractionDeps` member is a **resolver** | §3 — captured instances would be permanently null |
| 5 | The rename modal keeps its **own command drain** | it needs ImGui, so it cannot be an ECS system; `Drain`/`Commit` stay ImGui-free for rails |
| 6 | `CenterOnEntitySystem` = CGF's component preference + the editor's camera seam | §2 — each copy had learned something the other had not |
| 7 | CGF's "Rotate" keeps its **select-first** line | the context menu acts on the clicked entity, the toolbar on the selection — a caller concern, so the shared body needs no host branch |
| 8 | `AlsoSelect` hook rather than pushing a panel type into shared code | CGF's inspector follow-through is a host concern |
| 9 | Unserviceable tools **report** with a reason | ruling 49 / `VC-3` — *"nothing happened"* is indistinguishable from *"not implemented"* |
| 10 | Guard the de-dup with **source scans** | the parallels were a local function and a lambda — invisible to reflection, the call graph and reference counts |
| 11 | Rail the camera fix **after `camera.Update()`** | ⛔ the naive assertion passes on the broken code — this is the whole reason D1 lasted |
