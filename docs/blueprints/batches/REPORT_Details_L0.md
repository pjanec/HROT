<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L0 built, measured and deferred.
stale-below: nothing.
known-rot: none.
known-conflict: none. L0.4 is deferred, not blocked — §6.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, layer `L0`** *(the context)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §6 `L0` · **dispatch:** [`HANDOFF_Details_Panel_Migration.md`](HANDOFF_Details_Panel_Migration.md)
> **Scope frozen at** `e2c1348a5` · **started at** `7b81ce00` · **branch** `claude/hrot-implementation-j1jvin`
> ⚠ The started-marker says `7b81ce00`, not `e2c1348a5`: rule 7 says re-sync from the coordinator
> branch, so I merged its head, which contains the dispatch. ⛔ No `L0` item differs between them.
> ⭐ **Re-pulled the coordinator branch before the final commit** *(rule 4)*.
> ⛔ **No diagram in this report** — 📌 the `2026-08-21` rule: *diagrams live in the design, never in the
> batch*. Everything below CITES §1/§2/§2b/§6 instead.

| item | verdict | one line |
|---|---|---|
| **`L0.1`** | ✅ **done** | the store holds a **SET**; `ActiveSubSelection` is the derived single, **all 7 read sites unchanged** |
| **`L0.2`** | ✅ **done** | **three refusals DELETED**, not unified; per-host tie-breaks preserved as ORDER |
| **`L0.3`** | ✅ **done** | `DetailsContext` + builder — §2's six fields verbatim |
| **`L0.4`** | ⛔ **not started** | entity selection still reads the editor-side copies — §6 of this report |

⭐ **IDs I allocated:** **`BP-390`** *(done)* · **`BP-391`** · **`BP-392`** *(open)*.

---

## 1. ⭐⭐ OBLIGATION ③ — **I checked the UML before building, and here is the match**

📌 The handoff §1: *"report — the design carries N classes / M sequences for this layer; what I built
matches / deviates HERE and why."*

📐 **§2's `classDiagram` carries 13 classes; §2b carries 4 `sequenceDiagram`s + 1 `stateDiagram`.**
⭐ `L0` is responsible for **3** of those classes and **1** of the sequences.

| design element | built | match |
|---|---|---|
| `DetailsContext` *(6 fields)* | ✅ `AiShared/Shell/DetailsContext.cs` | ⭐ **exact** — `Focus`, `Selection`, `Entities`, `Asset`, `Perspective`, `Mode` |
| `EditorSelectionStore` *(`ActiveSubSelections` in §2)* | ✅ | ⭐ **exact** |
| §2b's **"a PAN must change nothing"** sequence | ✅ railed | ⭐ **exact** — the rail asserts its green branch literally |
| `PerspectiveWorkspace.BuildContext()` | ⚠ **DEVIATION** | see below |

### ⚠ The one deviation, argued rather than chosen silently

⛔ §2 hangs `BuildContext()` on **`PerspectiveWorkspace`**. 📐 That type is **`L1.1`**, and §6's own
dependency graph runs `L0.3 → L1.1` — ⇒ building it here would mean building `L1.1` inside `L0`.

⭐ **So the assembly logic is a free function `DetailsContextBuilder.Build(...)`, and `L1.1` will host it
on the workspace** — a moved call, ⛔ not a second implementation *(ruling 9)*. ⚠ The diagram stays
true of the finished design; only the ORDER of arrival differs.

---

## 2. ⭐⭐⭐ INVENTORY — **the queries actually run** *(`R-74`)*

⚠⚠ **The MCP `codebase-memory-mcp` tools TIMED OUT at their 60 s cap while the index was building.**
⛔ **I did not fall back to grep** — I used the **CLI**: `codebase-memory-mcp cli <tool> '<json>'`.
📐 Index: **159 180 nodes / 419 070 edges**.

| # | query | total | what it settled |
|---|---|---|---|
| **Q1** | `search_graph name_pattern=".*Selection.*" label="Class"` | **59** | the surface |
| **Q2** | `query_graph MATCH (c)-[:INHERITS]->(i) WHERE i.name="IAssetSubSelection"` | **9** | ⭐ the complete set the SET type must carry |
| **Q3** | `query_graph MATCH (s)-[:USAGE\|WRITES\|CALLS]->(t) WHERE t.name CONTAINS "ActiveSubSelection"` | **31** | every producer/consumer |

### ⛔⛔ `IMPLEMENTS` RETURNS ZERO ROWS — **the edge is `INHERITS`**

⚠ C# interface realisation is modelled as **`INHERITS`**. ⭐ Querying `IMPLEMENTS` returns `total: 0`,
which reads exactly like *"nothing implements this interface"* — ⛔ **a false negative of precisely the
shape the canon warns about.** `get_graph_schema` is what settled it.

### ⭐⭐ 9 implementations, **2 assemblies** — and the obvious file holds only 7

| where | which |
|---|---|
| `AiShared/Selection/SubSelectionRecords.cs` | `BlueprintNodeSelection` · `BTreeNodeSelection` · `BTreePillSelection` · `HsmStateSelection` · `HsmTransitionSelection` · `HsmRegionSelection` · `UtilityConsiderationSelection` |
| ⭐ **`Hrot.Hsm.Editor/Inspector/HsmSubSelections.cs`** | **`HsmEventSelection` · `HsmGlobalTransitionSelection`** |

⛔ A sweep of the obvious file finds **7 of 9**. ⚠ And `VariableOutlineSelection` — which *sounds* like
one — **does not inherit the interface**; name-similarity would have wrongly included it.

### ⭐⭐⭐ BOTH DIRECTIONS OF THE CLAUDE.md CAVEAT FIRED — neither tool alone was sufficient

| | |
|---|---|
| ⭐ **the graph found what grep would not have** | **`HsmGlobalsStrip.OnChipClicked`** writes `ActiveSubSelection` directly — ⛔ **a FOURTH production writer the design does not name** *(it names three bridges)*, and nothing about it suggests grepping for it |
| ⭐ **grep found what the graph missed** | the **readers** — `InspectorWindow` ×3 and `BlueprintDetailsWindow` ×4. The `USAGE` query surfaced writers and tests, ⛔ not these property reads |

⇒ ⛔ **an exhaustive claim from either alone would have been false.** 📌 That is the measured caveat in
`CLAUDE.md`, reproduced here on a different symbol.

---

## 3. ⭐⭐ `L0.1` — **the set, and why the derived single keeps `Count == 1`**

⭐ `EditorSelectionStore` now stores `IReadOnlyList<IAssetSubSelection>`; `ActiveSubSelections` is the
truth and **`ActiveSubSelection` derives** from it.

⛔⛔ **The `Count == 1` in the derivation is NOT `R-118`'s deleted rule sneaking back.** 📐 The deleted
rule **discarded the set**; this one derives from a set that is **fully preserved**. ⭐ And it is what
makes §6 `L0.1`'s promise — *"every existing reader unchanged"* — literally true for all **7** read
sites: each asks *"is the selection THIS one node?"*, and answering `list[0]` for a two-node marquee
would silently show node 1 of 2. 📌 The rule's real home is `L1.4`'s predicate, exactly where §6 puts it.

⭐⭐ **The no-change guard is ELEMENTWISE and KEEPS THE STORED INSTANCE.** ⚠ The bridges rebuild their
list every frame ⇒ reference equality would fire every frame; and §6 `L0.4` requires *"the same list
instance when unchanged, or every view rebuilds per frame."*

---

## 4. ⭐⭐⭐ `L0.2` — **the highest-risk task: three refusals DELETED, not unified**

📌 §6: *"three refusals **deleted**, ⛔ not unified"* — 📌 and `R-118`'s history is that unifying them
was **my own lean, which the user overturned**.

| host | deleted | ⭐ tie-break preserved as ORDER |
|---|---|---|
| **Blueprint** `:57` | `if (selection.Count != 1) return null;` | — |
| **BTree** `:61` | ″ | ⭐ **attachments FIRST** — a one-pill selection still resolves to `BTreePillSelection` |
| **HSM** `:79` | `Count == 0`, the *"more than one node ⇒ null"* arm, and the *"only selected element overall"* arm | ⭐ **nodes before links** — the old *"state wins"* |

⭐ Each bridge gained `MapSelections`; `MapSelection` became `all.Count == 1 ? all[0] : null`.
⭐ **An unresolvable id is DROPPED, not fatal** — one stale canvas id no longer discards the designer's
real selections.

---

## 5. ⚠⚠ THREE RAILS RE-EXPRESSED — **each premise was a rule `R-118` deletes**

⛔ None was silently edited; each says so in its own doc comment.

| rail | was | is |
|---|---|---|
| `MapSelection_MixedNodeAndLink_PrefersStateNode` | *"the state node is preferred"* | **`MapSelections_MixedNodeAndLink_ReportsBoth_StateFirst`** — both reported, state first, derived single null |
| ⭐⭐ `MapSelection_MultipleNodesSelected_ReturnsNull` | ⛔ **selected one real node + one `Guid.NewGuid()`** | **SPLIT IN TWO** — see below |
| *(new)* | — | `MapSelections_AStaleId_IsDropped_AndTheRestSurvive` *(HSM)* |

### ⭐⭐ The split is itself a finding

📐 The old Blueprint rail's name claimed *"multiple nodes"* but its body paired **one real node with one
stale id** ⇒ ⛔ it conflated **two of the three facts** the old `null` flattened. ⭐ It is now:
- **`MapSelections_TwoRealNodes_ReportsBoth_AndTheDerivedSingleIsNull`** — the multi-select claim the
  name promised and never tested;
- **`MapSelections_AStaleId_IsDropped_AndTheRealNodeSurvives`** — the drop rule.

⇒ ⭐ strictly more coverage than before, and the two cases can no longer be confused.

---

## 6. ⛔ `L0.4` — **NOT STARTED, and why that is a choice rather than a miss** *(`R-106`)*

⚠ **Not blocked**: §6's dependency graph puts `L0.4` on its own line — it gates `L4.1`, ⛔ **not
`L1.1`** ⇒ `L1` can proceed without it.

⭐⭐ **`DetailsContext.Entities` is WIRED, not empty.** `DetailsContextBuilder.EntitiesOf` reads the
store's single `SelectedEntity` — **the same source every existing panel already reads** — and is
isolated to **one method**, so `L0.4` is a one-method change. ⛔ **A silent default would have been an
empty list**; this is an honest interim with its replacement named in its own doc comment.

📄 **`R-129` sweep done:** beyond the Details design, **`docs/UX/UX_Feature_Selection.md` §0–§2.1** owns
this. ⭐ It names the copies *(`DefaultSelectionState`'s `HashSet`, `SimHostInspectorAdapter`)* and adds
a constraint the Details design does not state: ⚠ **`ISelectionState` KEEPS ITS SHAPE as a read-through
view** *(`EcsSelectionState`)* rather than being deleted outright — *"every shared panel keeps
compiling."* ⇒ ⭐ `L0.4` deletes the **storage**, not the interface.

---

## 7. ⭐ REVERT PROBES — **each reddened, each un-applied by the inverse edit**

| # | probe | result |
|---|---|---|
| **①** | the store's guard becomes reference-equality only | ⭐ **1 red** — the pan rail **alone**, ⛔ nothing else |
| **②** | restore `if (selection.Count != 1) …` in the HSM bridge | ⭐ **2 red** |
| **③** | the builder COPIES the selection list | ⭐⭐ **2 red** — the same-instance and pan-equality rails |

⭐ Probe ① is the one worth noting: it reddened **exactly one** rail, which is what makes that rail a
measurement of the elementwise guard rather than of the change event in general.

---

## 8. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`MIN`'s table**. Base sha **`7b81ce00`**. ⚠ Environment stated per row.

| gate | env | result | Δ |
|---|---|---|---|
| **solution build** | — | ⭐ **0 errors** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1736 / 0 / 0** | ⭐ **+13 — mine** *(7 set + 6 context)* |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3882 / 0 / 10** | ⭐ **+1 — mine** *(1 rail split into 2)* |
| `Hrot.Hsm.Editor.Tests` | — | **555 / 0 / 0** | ⭐ **+1 — mine** *(1 re-expressed + 1 new)* |
| `Hrot.BTree.Editor.Tests` | — | **622 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | — | **206 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | — | **151 / 0 / 0** | **0** |
| `Hrot.AiEditor.Generators.Tests` | — | **277 / 0 / 0** | **0** |
| `Hrot.AiEditor.Persistence.Tests` | — | **143 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | — | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | — | ⚠ **260 / 2 / 0** | **0** — the `D003_*` pair, **pre-existing**; §8.1 |
| **tracker** | — | ⭐ **OK — open 83 / done 244 (+1 refuted)** | +2 open / +1 done |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | ⚠ the ledger was slimmed 409→184 lines by the coordinator; 92 → 22 probes is **theirs**, not a loss here |
| **design digest** | — | ⭐ **58 docs OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*.
⛔ **`--no-build` for every in-solution project**; the out-of-solution four were not touched by `L0`
*(no file of theirs is in the diff)* and were last measured green in `MIN`'s table.

### ⚠ 8.1 — the two `ClusterRunner` reds

`DataDrivenGizmoPredicateTests.D003_*` ×2 — ⭐ the same pair Batch 103 reproduced in a worktree at an
ancestor commit, unchanged through `MIN`. ⛔ `L0` touches no gizmo code.

### ⭐ Quarantine counts

`Hrot.Blueprints.Tests` **10 skipped** *(Xvfb)*, unchanged; every other suite **0**. ⛔ **No new skips.**

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **9 files: 7 changed, 2 added**, 0 deleted. ⛔ No `.approved.` / golden /
snapshot file appears in the diff.

---

## 9. ⭐ LANE CHECK

⭐ Every file touched is **UI/variable lane** — `AiShared`, `Blueprints.Editor`, `BTree.Editor`,
`Hsm.Editor` and their tests. ⛔ **Nothing under `Fdp.Toolkits/Time/`, `Hrot.Orchestrator`,
`ModuleHostKernel` or the integration tests** *(`R-128`)*. ⭐ ids are **`BP-`**; ⛔ no `TM-`, no `Area H`.

⛔ **`R-130`/`R-126` noted and NOT acted on:** the ledger now records that `MIN`'s direct
`WriteFieldNow` is *"at odds with the design"* and that yellow means **staged**. 📌 The handoff §2 puts
the whole staged-write / watch story with the **coordinator** ⇒ ⭐ flagged here, ⛔ untouched.
