<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L3 built, measured, deferred and found.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, layer `L3`** *(migrate the views)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §4 · §6 `L3` · **dispatch:** [`HANDOFF_Details_Panel_Migration.md`](HANDOFF_Details_Panel_Migration.md)
> **started at** `55cc60e1` *(marker `ea30bbe6`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ Re-synced from the coordinator at the start *(rule 7)*; re-pulled before the final commit *(rule 4)*.
> ⛔ **No diagram in this report** — 📌 diagrams live in the design, never in the batch.

⭐ **`R-106` verdicts — §6 `L3`'s table has SEVEN rows:**

| row | verdict | one line |
|---|---|---|
| **Runtime** *(3 panes)* | ✅ **done** | the wrong-axis registry **dissolves** into three predicated views |
| **Graph signature** | ✅ **done** | ⚠ predicate built as the **expressible** reading — `BP-398` ① |
| **Layout / byte budget · Asset settings** | ⚠ **partial** | ships as **ONE** view; §6's split has no seam in the code — `BP-398` ② |
| **Diagnostics** | ⚠ **subsumed** | 📐 `VariablesPanelControl`'s host **IS** that same window |
| **Variables** | ✅ *(already `L1.3`)* | — |
| ⛔ **Node properties** | ⛔ **not started** | §6: ***"do not delegate this one"*** |
| ⛔ **Parameter sync** | ⛔ **not started** | §6: ***"⚠ LAST — after the orchestrator wiring (`R-99`)"*** |
| ⛔ **Utility** | ⛔ **not started** | the same 697-line file as node properties |

⭐ **IDs I allocated:** **`BP-397`** *(the layer)* · **`BP-398`** *(two design premises measurement
contradicted)* · **`BP-399`** *(the four deferred rows, open)*.

---

## 1. ⭐⭐ THE ENUMERATION, FIRST *(`R-74`)*

```
search_graph(".*(RuntimeInspectorPane|BlackboardAuthoringWindow|VariablesPanelControl|
  GraphSignatureWindow|InspectorWindow|BlueprintDetailsWindow).*", label="Class")   → 24
search_graph(".*Selection$", label="Class")                                        → 12
```

⭐⭐ **What the graph told me that reading the design could not:** the three runtime panes live in
**three SUBSYSTEM assemblies** *(`Hrot.Blueprints.Editor`, `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`)*,
not in `AiShared`. ⇒ that decided where each descriptor had to be built and how it could reach the
catalogue. ⚠ It also surfaced a **second** `InspectorWindow` *(70 ln, `Hrot.Blueprints.Editor`)* — ⛔ not
§6's 697-line one; it is `L5`'s retirement target.

---

## 2. ⭐⭐⭐ `L3.1` — **what the Runtime view actually bought**

⛔ **The old shape:** `_panes.Find(p => p.TargetKind == asset.Kind)` — asset kind was **the registry's
axis**. ⇒ exactly one pane per kind was reachable, so ⚠ **a second Blueprint view was unrepresentable**,
and *"this kind, but only while running"* could not be said at all.

⭐ **Now** kind is one clause inside one view's own predicate — 📌 `R-112` verbatim: *"a host says so in
its own predicate."*

⭐⭐⭐ **The rail that proves the ruling bought something:**
`TwoViewsMayClaimTheSameAssetKind_WhichTheOldRegistryForbade`. ⛔ Without it, `L3.1` would read as
moving a lookup from one file to another.

### ⭐⭐ The `Mode != Planning` clause is the design's, and it closes a real defect

📐 **Measured:** the old window drew its pane in **every** mode, and the pane then said *"No live BTree
state."* **from inside**. ⇒ ⛔ that is `R-117`'s blank one level down — **a view claiming the panel in
order to apologise.** ⭐ Now it declines while PLANNING and the shell's grey line answers, in one voice
for every host.

### ⭐⭐⭐ Registration costs the composition root NOTHING *(`R-67`)*

📐 `EditorSubsystem:2864`/`:2870`/`:2884` already call `RegisterPane` — **all three unchanged**.
`RegisterPane` now contributes the descriptor too.

⚠ **Why not the claim chain itself:** panes are registered long after the workspace is built, and
`IDetailsViewSource` is read **once** at registration. ⛔ Making the registry lazily re-read would turn a
snapshot into a per-frame query.

### ⛔⛔ And a silent defect became a throw

📐 `RegisterPane` accepted **two panes for one kind** and `_panes.Find` returned the **first** — the
second simply never drew. ⚠ A wiring bug wearing a working editor. ⭐ The descriptor id carries the
kind, so the registry's duplicate guard now fails **at the wiring** *(the `G4` precedent)*.
⚠ `RuntimeInspectorWindowTests.RegisterPane_MultipleCanBeRegistered` still passes — it registers stubs
through the standalone constructor, which has no catalogue.

---

## 3. ⭐⭐ `L3.2`/`L3.3` — **one body, two hosts**

📐 Both `GraphSignatureWindow` and `BlackboardAuthoringWindow` had `protected override
DrawClientArea()` — **unreachable from a view.**

⭐⭐ **This is the shape `L1.3` relied on and never had to build:** `VariableDetailsSection` is
deliberately window-less — *"the host draws it; it does not own a window… that is what lets one Details
panel per perspective host the same list."*

⇒ ⭐ each gained `public void DrawContent()`, with `DrawClientArea() => DrawContent()`.
⛔ **ROUTING, not duplication** — the body is unchanged and there is still exactly **one** of it.

### ⭐⭐ …and the contribution arm was collapsed BEFORE a third copy appeared

📐 `L1.2` wrote the *"collect this window's views"* loop in `RegisterExtraWindow` **and** mirrored it in
the constructor for `Details`. ⛔ `BlackboardAuthoring` would have been a **third** copy.
⇒ ⭐ one `ContributeDetailsViews(object?)`, used by all three — 📌 ruling 9, and three copies of a
guarded loop is how one of them quietly loses its guard.

---

## 4. ⚠⚠ `BP-398` — **TWO DESIGN PREMISES THAT MEASUREMENT CONTRADICTED**

⭐ Neither was invented around; both are built as the honest reading and named here.

### ① *"Blueprint ∧ a graph row"* — **the input does not exist**

📐 `search_graph(".*Selection$")` → **12** sub-selection types, **none of them a graph**. The selected
graph is `GraphSignatureWindow`'s **own state** *(`_selectedGraphId`, snapped from the canvas by
`ResolveSelectedGraph`, `BP-72`)* — ⛔ not something the store publishes.

⭐ **Built as:** *"a Blueprint document that has at least one graph row to show."*
⛔ **A 13th `IAssetSubSelection` with no writer was NOT invented** — that is the dead-seam shape
`CLAUDE.md` names *(`ITkbHotReloadEvents`)*.

⚠ **A second measurement forced the predicate to be an INSTANCE method:**
`context.Asset is BlueprintAsset` **does not compile** — `CS8121`: the shell's `IEditableAsset` and
`BlueprintAsset` are **different hierarchies**. ⇒ the context answers the **kind**; the window answers
**which**.

### ② *"Layout / byte budget"* · *"Asset settings"* · *"Diagnostics"* — **three rows, one body**

📐 `BlackboardAuthoringWindow.DrawClientArea` is a single flow — comparison toolbar → state banner →
`VariablesPanelControl` → the `SUB-TREE ALLOCATIONS` header — with **no seam** between them. ⭐ And
`VariablesPanelControl`'s host **IS that window** *(`:509`)*, so the Diagnostics row names the same
object.

⇒ ⭐ ships as **ONE** view. ⛔ Splitting one body into three is a **DECOMPOSITION**, not the delegation
§6 calls `L3` — and inventing that split inside an implementation batch is design work in the wrong lane.

---

## 5. ⚠⚠ AN `L2` RAIL WENT RED — **and it was encoding its neighbours' absence**

📐 `ADocumentWithNoVariableSelected_SaysWhy_RatherThanDrawingABlank` asserted `EmptyOffer` for an open
document. ⇒ ⛔ **`L3.3`'s Blackboard view claims any open document**, so the panel is now filled.

⭐⭐ **The `EmptyOffer` assertion was true only because no other view existed yet** — it encoded
*"nothing else is built"*, not the claim the rail is **for**.

| ⭐ resolved by re-expressing, ⛔ not relaxing | |
|---|---|
| the two REAL claims are asserted directly | the variables view **declines** with an empty section · the panel is **never blank** |
| ⭐ **a NEW rail keeps `L2.3`'s empty-offer branch covered** | `WithNoViewsRegistered_AnOpenDocumentStillGetsTheGreyLine` — ⛔ otherwise that branch would silently go unrailed |

📌 **The mirror of `BP-396`**: a rail written before its neighbours exist can encode their absence by
accident. ⚠ **Three batches running, three different ways a rail has been wrong** — and each time the
fix was to state the claim more precisely, never to weaken it.

---

## 6. ⭐ REVERT PROBES — **four, each un-applied by the inverse edit**

| # | probe | result |
|---|---|---|
| **①** | `RegisterPane` stops contributing the descriptor | ⭐ **8 red** — every runtime rail incl. the production ones |
| **②** | drop the `Mode` clause from the runtime predicate | ⭐ reddens the predicate + default rails |
| **③** | `ContributeDetailsViews(BlackboardAuthoring)` fed null | ⭐ **5 red** |
| **④** | `GraphSignatureWindow.DetailsViews` yields nothing | ⭐ **1 red**, exactly its own rail |

⛔ **No `git checkout --`.** ⭐ Every probe reddened its own rails and nothing else.

### ⚠ …and the probes were not the only thing that caught me

📐 **My first wiring of `L3.3` called `ContributeDetailsViews(BlackboardAuthoring)` BEFORE the field was
assigned** *(the registrar builds `RuntimeInspector` at `:271`, `BlackboardAuthoring` at `:288`)* ⇒ it
contributed `null` and registered nothing. ⭐ **The production-built rail caught it on the first run** —
📌 exactly what `R-67` says a hand-built rail could not have done.

---

## 7. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L2`'s table**. Base sha **`55cc60e1`**. ⭐ `--no-build` on every suite, over a fresh
solution build.

| gate | env | result | Δ vs `L2` |
|---|---|---|---|
| **solution build** *(`IOS-IG-SimHost.sln`)* | — | ⭐ **0 errors, 0 warnings** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1802 / 0 / 0** | ⭐ **+14 — mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3887 / 0 / 10** | ⭐ **+5 — mine** *(`L3.2`'s rail)* |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | **206 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ⚠ **260 / 2 / 0** | **0** — the `D003_*` pair, named below |
| **tracker** | — | ⭐ **OK — open 84 / done 250 (+1 refuted)** | +2 done, +1 open |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | — |
| **design digest** | — | ⭐ **OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*.
⚠ The two reds are `DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity`
and `…D003_Predicate_True_AllowsUpdateAndDraw` — ⭐ **the same pair `L1` and `L2` reported**,
pre-existing since Batch 103; **no file of theirs is in this diff.**

### ⚠⚠ 7.1 — A MEASUREMENT DISCIPLINE NOTE, because I nearly reported a false number

📐 One `dotnet build … && dotnet test --no-build …` **in a single shell command** reported
**`701 / 0 / 0`** for `AiShared` — ⚠ a **build/test race**, not a result. ⭐ Re-run as its own command:
**`1802 / 0 / 0`**, twice. ⛔ **Never report a count from a run that shared its command with a build**
— 📌 the same family as `quick-check.sh` refusing to test a failed build.

### ⭐ Quarantine counts

`Hrot.Blueprints.Tests` **10 skipped** *(Xvfb)*, unchanged; every other suite **0**. ⛔ **No new skips.**

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **11 files: 7 changed, 4 added**, 0 deleted, 0 renamed. ⛔ No
`.approved.` / golden / snapshot file appears in the diff.

---

## 8. ⭐ LANE CHECK

⭐ Files touched: `AiShared` + its tests · `Hrot.Blueprints.Editor` + its tests. ⛔ **Nothing under
`Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel` or the integration tests** *(`R-128`)*.
⭐ ids are **`BP-`**; ⛔ no `TM-`, no `Area H`.
⛔ **Still the coordinator's, still untouched:** the staged-write / yellow story *(`R-126`, `R-130`,
`DESIGN_Staged_Live_Write.md`, `IStagedWrites`)*.

---

## 9. ⭐ WHAT IS OPEN

| | |
|---|---|
| ⛔ **`BP-399`** | `L3`'s four remaining rows — ⭐ each deferred on the design's OWN words, ⛔ none blocked by `L3.1`–`L3.3` *(§6: `L3.*` fans out completely)* |
| ⛔ **`L0.4` / `BP-392`** | entity selection still reads the editor-side copies; gates `L4.1` |
| ⭐ **`L4` is unblocked** | §6: *"`L4` needs `L1`, not `L2`"* — and `L2.1` built `IDetailsContextSource`, `L4`'s whole mechanism. ⛔ `FrozenContextSource` is `L4.3`'s |
| ⚠ **`L5` waits per item** | §6: retire *"after its replacement is live"* — ⭐ `RuntimeInspectorWindow` is now **routed**, which is `Q-iii`'s first half; its removal is `L5`'s |
