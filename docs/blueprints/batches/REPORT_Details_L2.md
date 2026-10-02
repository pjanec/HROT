<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L2 built, measured and found.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, layer `L2`** *(the shell)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §2 · §2b · §4 · §6 `L2` · **dispatch:** [`HANDOFF_Details_Panel_Migration.md`](HANDOFF_Details_Panel_Migration.md)
> **started at** `55cc60e1` *(marker `669bc293`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ **Re-synced from the coordinator at the start** *(rule 7)* and **re-pulled before the final commit**
> *(rule 4)*.
> ⛔ **No diagram in this report** — 📌 *diagrams live in the design, never in the batch*; everything
> below CITES §2/§2b/§4/§6.

| item | verdict | one line |
|---|---|---|
| **`L2.1`** | ✅ **done** | `AiDetailsWindow` → **`DetailsWindow`**; it now draws what the REGISTRY chose |
| **`L2.2`** | ✅ **done** | §2b's five transitions, keyed on §2's `(Perspective, AssetId, SHAPE)` |
| **`L2.3`** | ✅ **done** | the grey line, at **both** sites the design names by line number |

⭐ **IDs I allocated:** **`BP-395`** *(the layer)* · **`BP-396`** *(a rail that asserted half a predicate)*.

---

## 1. ⭐⭐ OBLIGATION ③ — **the UML check, and the one deviation**

📐 §2's `classDiagram` carries **13 classes**; `L2` owns **4** more of them.

| design element | built | match |
|---|---|---|
| `DetailsWindow` *(«docked shell», `DrawClientArea`)* | ✅ | ⭐ **exact** — grown from `AiDetailsWindow` per §4 |
| `IDetailsContextSource` *(`Current()`)* | ✅ | ⭐ **exact** |
| `LiveContextSource` *(`Current()`)* | ✅ | ⚠ **DEVIATION** — see below |
| `DetailsWindow *-- "0..1" IDetailsViewInstance` | ✅ | ⭐ **exact**, and **railed as a multiplicity** *(`InstantiatedViewId` is null before the first draw)* |
| `FrozenContextSource` | ⛔ **not built** | ⭐ **`L4.3`'s** — a snapshot type with no window to freeze for would be a guess about `L4` |

📐 §2b's `stateDiagram` has **five edges**. **All five are railed**, one `[Fact]` each, named after the edge.

### ⚠ The deviation — where the context is assembled

⛔ §2 draws `BuildContext()` on **`PerspectiveWorkspace`** and `LiveContextSource o-- PerspectiveWorkspace`.
📐 That type is extracted in **`L6.1`**, not `L2`.

⇒ ⭐ `LiveContextSource` holds a **delegate**, supplied by `PerspectiveWorkspaceRegistrar` — §5's
*"wiring hub"* half, which is **the very thing `L6.1` splits out**. ⇒ `L6.1` replaces **one constructor
argument** and nothing else.
⚠ Same shape as `L0.3`'s builder and `L1`'s registry home, and for the same reason: the diagram
describes the FINISHED design; the layer order decides when each box arrives.

---

## 2. ⭐⭐⭐ `L2.1` — **the rename, and the one thing that must NOT rename**

| ⭐ | |
|---|---|
| **type** | `AiDetailsWindow` → **`DetailsWindow`** *(§6 `L2.1`)*; the registrar's `AiDetails` → **`Details`** |
| ⛔⛔ **the persisted ImGui id** | **UNCHANGED** — `ai_details_btree` / `ai_details_hsm` |

📌 §5, verbatim: a bare key rename *"silently resets layouts"*, because `CurrentPerspective` and every
`OwningPerspective` are **persisted**. ⇒ ⭐ **railed directly** *(`TheRenameKeptThePersistedWindowId`)* —
⚠ cheap, and the failure it guards is invisible until a designer opens the editor and finds their dock
layout gone.

⭐ **The property renamed too**, beyond the design's letter. ⛔ A property named `AiDetails` returning a
`DetailsWindow` is the half-rename that confuses; the change is mechanical *(≈40 test lines)* and
`L6.1` rewrites that surface anyway.

### ⭐⭐ What actually changed under the rename

⛔ The window **no longer decides what to draw.** ⭐ `Frame()` builds the context once, asks the registry
for the offer set and the selector for the choice, and returns all of it as a **value**;
`DrawClientArea` is a **thin renderer** over that. ⇒ ⭐ §6's *"every task's rail asserts on a returned
model"* holds — **the 13 shell rails run with no ImGui context at all.**

⭐⭐ **Both collaborators are CONSTRUCTOR arguments.** 📌 The `2026-08-16` rule — *"a production caller
that HAS a dependency must PASS it"*: the registrar holds the registry, the store and the run-state
source **at the line where it builds this window**. ⛔ An `AttachShell(…)` setter would have been a
tenth silent default waiting to happen.

---

## 3. ⭐⭐ `L2.2` — **the key is a SHAPE, and a lapsed pick is FORGOTTEN**

📄 §2, verbatim: *"node A → node B keeps the view; a variable pick remembers its own."*
⇒ ⭐ the key's third component is the **ordered list of selection TYPE NAMES**, ⛔ not their ids.

⚠ **Order is part of the shape, deliberately** — it is part of the SET's identity too *(`L0.1`'s
elementwise guard)*, so the two agree rather than disagreeing by one being order-blind.

### ⭐ Two edges that look alike and are not

| | |
|---|---|
| ⭐ **a pick that stops APPLYING** | **forgotten.** ⛔ Keeping it would make the panel jump back to a view last seen three selections ago, the moment it happened to apply again — ⚠ indistinguishable from a bug |
| ⭐ **an EMPTY OFFER in between** | **remembered.** ⚠ Deselecting is transient *(a marquee, a click on blank canvas)* — ⛔ dropping the pick there punishes the designer for deselecting |

⭐ **Both are railed**, because the difference is a decision and not an accident.

### ⚠ One judgement call, stated

⭐ **The toolbar draws only at ≥2 offers.** With one view the row is a permanently-pressed button that
decides nothing, and the view's own heading already names it. ⛔ Nothing is lost — the SELECTOR still
runs, so the switch appears the moment a second view claims the context. ⚠ Trivially reversible.

---

## 4. ⭐⭐⭐ `L2.3` — **two sentences, and the predicate half that makes them reachable**

### ⭐ Two strings, not one — `R-118`'s lesson applied to prose

📌 `R-118` deleted a `null` that meant three things at once. ⛔ Collapsing *"no document is open"* and
*"nothing applies to this selection"* rebuilds exactly that mistake in the UI layer: **the first is
fixed by OPENING something, the second by SELECTING something else**, and a designer told the wrong
one looks in the wrong place.

📐 **Measured at the second site the design names:** `RuntimeInspectorWindow:54` and `:67` **both said
`"No active session."`** — and ⚠ **neither is about a session.** `:54` fires with no document open;
`:67` with a document open and no pane claiming its kind.

### ⭐⭐ The predicate half — **the defect this closes**

📐 **Measured:** `VariableDetailsSection.Draw` is `if (!HasContent) return;`
⇒ ⛔ a variables view that claimed the panel with an **empty section** renders a **BLANK** — precisely
the defect `R-117` names.

⭐ **Fixed in the DESCRIPTOR** — `AppliesTo: ctx => Applies(ctx) && section.HasContent` *(📌 `R-116`:
the predicate ships with the view, and the view knows it has nothing to show)*. ⛔ The alternative —
the shell special-casing variables — would put knowledge of what a variable **is** inside a type that
must not have it.

⇒ ⭐ **This is also `BP-391`'s other half**: the HSM mixed node+link selection now reaches a real grey
line instead of a blank.

---

## 5. ⚠⚠ `BP-396` — **TWO RAILS THAT WENT RED AGAINST CORRECT CODE, in opposite directions**

⭐ **The most useful thing this batch found, and both were found by running, not by reading.**

| # | what happened | ⭐ what it means |
|---|---|---|
| **①** | `L1`'s `WithTheOutlineFocused_TheVariablesViewIsOffered` **went red** when `L2.3` added the `HasContent` conjunct | 📐 §6 `L3` gives this view the predicate **"outline focus ∧ variable rows"** — ⭐ `L1` asserted the **first conjunct only**, honestly, because `L1` had not built the second. ⇒ ⛔ **a rail written before its second half exists is not wrong, it is INCOMPLETE — and invisible until the half arrives** |
| **②** | my own new `TheToolbarRemembersThePickTests` **went red twice** | 📐 the helper minted a fresh `FakeAsset` **per call**, so two contexts differing only by selected node also differed by `AssetId` ⇒ *"node A → node B"* looked like **two documents** |

### ⭐ How each was resolved — ⛔ neither by relaxing an assertion

- **①** the assertions are **unchanged**; the SETUP gained the row the design always required, and the
  rail was **SPLIT IN TWO** so the two conjuncts **fail separately** — ⛔ one rail covering both cannot
  say which half broke.
- **②** one shared document, plus a **new** rail `ADifferentAsset_HasItsOwnMemory` stating the asset
  half of the key **positively** — ⭐ the trap is now asserted rather than remembered.

📌 **The mirror of `BP-394`:** that one was a rail that could not FAIL; these are rails that could not
PASS. ⚠ **In both directions, a red is a claim about the RAIL until the rail has been checked.**

---

## 6. ⭐ REVERT PROBES — **five, each un-applied by the inverse edit**

⛔ **No `git checkout --`** — every probe was reversed by the opposite edit.

| # | probe | result |
|---|---|---|
| **①** | drop `&& section.HasContent` from the descriptor | ⭐ **4 red** — the shell's two empty-state rails on both perspectives |
| **②** | `RuntimeInspectorWindow`'s two arms return the same sentence | ⭐ **1 red**, exactly its own rail |
| **③** | delete `_pickByKey.Remove(key)` *(a lapsed pick is parked, not forgotten)* | ⭐ **1 red** |
| **④** | drop the selection SHAPE from the context key | ⭐ **2 red** — the shape rail and the order rail |
| **⑤** | `LiveContextSource` caches its first context | ⭐ **1 red** — `TheContextIsReReadEveryFrame` |

⭐ **Every probe reddened its own rail and nothing else** — ⛔ no probe was silent.

---

## 7. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L1`'s table**. Base sha **`55cc60e1`**. ⚠ Environment stated per row.
⭐ **`--no-build` on every suite**, over a fresh **solution** build.

| gate | env | result | Δ vs `L1` |
|---|---|---|---|
| **solution build** *(`IOS-IG-SimHost.sln`)* | — | ⭐ **0 errors, 0 warnings** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1788 / 0 / 0** | ⭐ **+36 — all mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3882 / 0 / 10** | **0** |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | **206 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ⚠ **260 / 2 / 0** | **0** — see 7.1 |
| **tracker** | — | ⭐ **OK — open 83 / done 248 (+1 refuted)** | +2 done |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | — |
| **design digest** | — | ⭐ **OK** *(STATUS · INVENTORY · UML all pass)* | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*. ⛔ The out-of-solution four
*(`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests`, …)* were not touched by `L2` — **no file of theirs
is in the diff**; last measured green in `MIN`'s table.

### ⚠ 7.1 — the `ClusterRunner` reds, named and confirmed pre-existing

📐 **The two are `DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity`
and `…D003_Predicate_True_AllowsUpdateAndDraw`** — ⭐ **the same pair `L1` reported**, pre-existing
since Batch 103, and no file of theirs is in this diff.

⚠ **One run of that suite reported `259 / 3`.** ⛔ **That run did not print names** *(the grep matched
only the summary)*. ⭐ **Three subsequent runs, all with names, reported `260 / 2` and named exactly the
`D003` pair.** ⇒ ⚠ **What I can say:** a third failure occurred once and did not recur.
⛔ **What I will not say:** which test it was — 📌 the same discipline as `L1` §7.1, and naming a
suspect I cannot demonstrate is how false canon starts.

### ⭐ Quarantine counts

`Hrot.Blueprints.Tests` **10 skipped** *(Xvfb)*, unchanged; every other suite **0**.
⛔ **No new skips.**

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **14 files: 7 changed, 6 added, 1 RENAMED** *(`AiDetailsWindow.cs` →
`DetailsWindow.cs`, tracked as `R` by git)*, **0 deleted**. ⛔ No `.approved.` / golden / snapshot file
appears in the diff, and the tree was clean after every suite run.

---

## 8. ⭐ LANE CHECK

⭐ Every file touched is **UI/variable lane** — `AiShared`, its tests, and one line of
`Hrot.Smoke.Tests` *(a comment naming the renamed property)*. ⛔ **Nothing under `Fdp.Toolkits/Time/`,
`Hrot.Orchestrator`, `ModuleHostKernel` or the integration tests** *(`R-128`)*.
⭐ ids are **`BP-`**; ⛔ no `TM-`, no `Area H`.

⛔ **Still not touched, and still the coordinator's:** the staged-write / yellow story *(`R-126`,
`R-130`, `DESIGN_Staged_Live_Write.md`, `IStagedWrites`)* — 📌 handoff §2.

---

## 9. ⭐ WHAT `L2` UNBLOCKS, AND WHAT IS STILL OPEN

| | |
|---|---|
| ⭐⭐ **`L3` is now unblocked** | §6's graph: `L2.3 → L3.*`, which **fans out completely**. Every migrated view is a descriptor plus a predicate; ⛔ nothing more of the shell is needed |
| ⭐ **`L4.1` was already unblocked by `L1`** | ⭐ and `L2` built **`IDetailsContextSource`**, which is `L4`'s whole mechanism — ⛔ `FrozenContextSource` is still `L4.3`'s to write |
| ⭐ **`BP-391` is CLOSED in effect** | the HSM mixed node+link selection now reaches the grey line rather than a blank. ⚠ **Left open as a row** until a visual check confirms it, ⛔ since it was reported as a user-visible gap |
| ⛔ **`L0.4` still not started** | `BP-392` — entity selection still reads the editor-side copies. ⚠ Not blocking `L3`; it gates `L4.1` |
| ⚠ **`L5` is NOT unblocked by `L2` alone** | §6: retire *"per item, after its replacement is live"* ⇒ each retirement waits on its own `L3` view |
