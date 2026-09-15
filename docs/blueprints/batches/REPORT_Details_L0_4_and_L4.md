<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L0.4 and L4 built, measured, deferred and found.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, `L0.4` + layer `L4`** *(the World, then float and pin)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §2 · §2b · §6 `L0.4` · §6 `L4` · **dispatch:** [`HANDOFF_Details_Panel_Migration.md`](HANDOFF_Details_Panel_Migration.md)
> **started at** `55cc60e1` *(marker `2c75154f`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ Re-synced from the coordinator at the start *(rule 7)*; re-pulled before the final commit *(rule 4)*.
> ⛔ **No diagram in this report** — diagrams live in the design.

## 0. ⭐⭐ WHY `L0.4` IS IN THIS BATCH

📐 §6's dependency graph puts **`L0.4` on `L4.1`'s line**:

```
L0.4 ─────────────────────────── └─ L4.1 ─ L4.2 ─ L4.3 ─ L4.4 ────────┘
```

⇒ ⭐ `L0.4` was the gate, and `L1`/`L2`/`L3`'s reports each said so. ⛔ Starting `L4` without it would
have built float and pin over a context whose entity field still read an editor-side copy.

| item | verdict | one line |
|---|---|---|
| **`L0.4`** | ✅ **done** | the context reads `SelectionState` from the World; the interim is **deleted** |
| **`L4.1`** | ✅ **done** | `DetailsViewWindow` — one class for both modes |
| **`L4.2`** | ✅ **done** | contextual float — live, stable id, persists |
| **`L4.3`** | ✅ **done** | pin — frozen, volatile, duplicate **focuses** |
| **`L4.4`** | ⚠ **partial** | toolbar affordance built; the **View-menu** half is `BP-403` |

⭐ **IDs I allocated:** **`BP-400`** *(`L0.4`)* · **`BP-401`** *(`L4`)* · **`BP-402`** *(two bad rails)* ·
**`BP-403`** *(`L4.4`'s deferred half)*. ⭐ **`BP-392` is CLOSED** by `BP-400`.

---

## 1. ⭐⭐ OBLIGATION ③ — **the UML check**

📐 §2's `classDiagram` carries **13 classes**. With this batch, **12 of 13 exist**; the 13th is
`PerspectiveWorkspace`, which `L6.1` extracts.

| design element | built | match |
|---|---|---|
| `FrozenContextSource` *(`-DetailsContext snapshot`)* | ✅ | ⭐ **exact** |
| `DetailsViewWindow` *(«float or pin», `DrawClientArea`)* | ✅ | ⭐ **exact** |
| `DetailsViewWindow o-- DetailsViewDescriptor` · `o-- IDetailsContextSource` | ✅ | ⭐ **exact** |
| `DetailsViewWindow *-- "1" IDetailsViewInstance` | ✅ | ⭐ **exact** — and **railed as a multiplicity** |
| `World o-- "0..*" SelectionState` · `..> World : reads entity selection` | ✅ | ⭐ **exact** |

📐 §2b has **four** sequences. `L4` implements the **float** and **pin** ones; both are railed
transition by transition.

---

## 2. ⭐⭐⭐ `L0.4` — **the design intent was read first, and it changed the answer**

📌 `R-129`: *"intents are not in code; they are in design docs."*
📄 **`docs/UX/UX_Feature_Selection.md` §0/§2.1**, read end-to-end **before** the code:

| what it says | ⭐ what it decided here |
|---|---|
| `SelectionState` is *"already correct for multi-select — **one primary, many selected**"* | ⇒ ⭐⭐ **the source returns PRIMARY FIRST**, so a view taking `[0]` gets the entity the ring paints **green** — ⛔ not whichever the archetype walk reached first |
| `ISelectionState`/`DefaultSelectionState`'s `HashSet` are 🔴 **"the defect — a second, parallel store"** | ⇒ ⭐ the World is the truth; the Details context reads it directly |
| §2.1: `ISelectionState` **keeps its shape** as a read-through view | ⇒ ⛔ **I did NOT migrate it** — that is `UXI-11`'s programme, ⭐ and saying so is the scope line |

### ⚠ SCOPE, stated so nobody reads more into this

⛔ This does **not** perform the `ISelectionState` → `EcsSelectionState` migration, and ⛔ does **not**
delete `EntityInspectorPanel`'s `HashSet` — 📄 §6 **`L6.3`** deletes that one **by name**.
⭐ What it does: **the Details context is no longer fed a copy.**

### ⭐⭐ Two measurements that shaped the implementation

| 📐 measured | ⭐ consequence |
|---|---|
| the root's world field is **nullable and read lazily** *(`ClockIsHalted`: `var world = _world; if (world is null) …`)* | ⇒ ⛔ the source resolves the world at **call time**. ⚠ An eager capture would bind `null` for the editor's whole lifetime, **silently** — 📌 the same construction-order shape that made `L3.3`'s first wiring register nothing |
| `QueryBuilder.With<T>` only **sets a bit** *(`QueryBuilder.cs:36`)* | ⇒ ⭐ a world that never registered `SelectionState` matches **nothing** rather than throwing — railed, because the safety is invisible at the call site |

### ⭐⭐⭐ The same-instance clause is a CONTRACT now, not a local trick

📄 §6 `L0.4`: *"return the same list instance when unchanged, or every view rebuilds per frame."*
⭐ It moved from `DetailsContextBuilder`'s `[ThreadStatic]` interim **onto the interface**, and the
comparison is **elementwise**:

⛔⛔ **The obvious cache — key on `Count` — passes every other rail in the file and fails exactly where
it matters:** clicking entity B after entity A leaves the count at **1**. ⚠ Same for a primary that
moved. Both are railed.

---

## 3. ⭐⭐⭐ `L4` — **§2's central claim, made checkable**

📄 §2, verbatim: *"the two window classes differ ONLY in `IDetailsContextSource`."*

⇒ ⛔ **there is no `isPinned` flag and no second class.** A float holds a `LiveContextSource`; a pin
holds a `FrozenContextSource`. ⭐ The rail pair
`ALiveFloat_ReAsksEveryFrame` / `APin_KeepsTheContextItWasPinnedAt` differ **in one constructor
argument** and nothing else — which is the claim, asserted.

| ⭐ | |
|---|---|
| **`IsVolatile` + `ShowInMenu`** follow §2's hosting table | float **persists** and is menu-listed; pin is **excluded from the layout save** and is not |
| ⛔⛔ **no reference captured at open time** | §6 `L4`, verbatim — a float re-asks its source **and** its predicate every frame, railed |
| ⭐ **`R-117`'s SECOND site is live** | a float whose predicate is false **stays open** and **names itself** — ⛔ a bare *"nothing to show"* reads as stuck |
| ⭐⭐ **the pin id reuses the TOOLBAR's key** | §2b's `viewId + assetId + selectionKey`, where the selection key **is** `DetailsViewSelector.KeyOf` *(ruling 9 — ⛔ a second key-builder would drift from what the toolbar remembers)* |
| ⭐ **the float id is STABLE** | ⛔ it must not move with the selection, or a saved layout could never restore it |
| ⭐ **a duplicate FOCUSES** | 📌 `R-100`, for both gestures |

---

## 4. ⚠⚠ `BP-402` — **A PROBE THAT FOUND NOTHING, AND A RAIL THAT HAD GONE VACUOUS**

⭐ **The two most useful findings of the batch, and neither came from reading code.**

### ① The probe that reddened **zero** rails

📐 Replacing `Pin`'s `new FrozenContextSource(frame.Context)` with the shell's own **LIVE** source —
which collapses `R-119` **entirely**, the single most load-bearing ruling in `L4` — reddened
**nothing**.

⛔ **Why:** frozen-ness was asserted only on a `DetailsViewWindow` built **by hand**; ⚠ the window the
**entry point** actually makes was never checked.
⭐ Fixed by `TheShellsPinIsFrozen_WhileTheShellsFloatIsLive`, through the production shell.
📌 `BP-394`'s lesson in the other direction: **a probe that does not redden is a finding about the
rail.**

### ② The rail that still PASSED and tested nothing

📐 `TheEntityListIsStable_WhenTheEntityDidNotChange` wrote `store.SelectedEntity` — which the context
**no longer reads** after `L0.4` — so it compared `Array.Empty` with `Array.Empty` and was **true by
construction**.

⚠⚠ **Nothing reported it.** It was found only because its sibling `AllFiveSourcesReachTheContext`
**failed** and I read the file. ⭐ It now asserts a **non-empty** list through the real seam, with an
`Assert.NotEmpty` guard against going vacuous again.

⇒ ⭐⭐⭐ **The generic lesson, and it is new:** *when a seam is re-pointed, every rail that fed it
through the OLD path must be re-read — **the ones that keep passing are the dangerous half.***

---

## 5. ⭐ REVERT PROBES

| # | probe | result |
|---|---|---|
| **①** | the entity source returns a fresh array every call | ⭐ **2 red** — the same-instance rails |
| **②** | `Pin` uses the LIVE source *(collapses `R-119`)* | ⛔⛔ **0 red** → ⭐ **new rail written** → ⭐ **1 red**. See `BP-402` ① |

⛔ **No `git checkout --`** — both un-applied by the inverse edit.

---

## 6. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L3`'s table**. Base sha **`55cc60e1`**. ⭐ `--no-build` on every suite, over a fresh
solution build. ⚠ Each suite run as **its own command** — 📌 `L3` §7.1's build/test race.

| gate | env | result | Δ vs `L3` |
|---|---|---|---|
| **solution build** | — | ⭐ **0 errors, 0 warnings** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1829 / 0 / 0** | ⭐ **+27 — mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3887 / 0 / 10** | **0** |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | **206 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ⚠ **260 / 2 / 0** | **0** — the `D003_*` pair |
| **tracker** | — | ⭐ **OK — open 84 / done 254 (+1 refuted)** | +4 done, ±0 open |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | — |
| **design digest** | — | ⭐ **OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*.
⚠ The two reds are `DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity`
and `…D003_Predicate_True_AllowsUpdateAndDraw` — ⭐ **the same pair `L1`/`L2`/`L3` reported**,
pre-existing since Batch 103; **no file of theirs is in this diff.**

### ⭐ Quarantine counts

`Hrot.Blueprints.Tests` **10 skipped** *(Xvfb)*, unchanged; every other suite **0**. ⛔ No new skips.

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **14 files: 9 changed, 5 added**, 0 deleted, 0 renamed. ⛔ No
`.approved.` / golden / snapshot file in the diff. ⚠ **One `.csproj` changed** — `Hrot.Core` made an
**explicit** ProjectReference on `AiShared` *(it was already transitive; the build proves it resolved
before the edit)*.

---

## 7. ⭐ LANE CHECK

⭐ Files touched: `AiShared` + tests · `Hrot.Editor` *(one composition-root line)*. ⛔ **Nothing under
`Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel` or the integration tests** *(`R-128`)*.
⭐ ids are **`BP-`**; ⛔ no `TM-`, no `Area H`.
⛔ **Still the coordinator's:** the staged-write / yellow story *(`R-126`, `R-130`)*.

---

## 8. ⭐ WHAT IS OPEN

| | |
|---|---|
| ⛔ **`BP-403`** | `L4.4`'s View-menu half — ⭐ the seam is named *(`ShellEditorCommands.Register`)*; ⚠ wiring it is composition-root work |
| ⛔ **`BP-399`** | `L3`'s four remaining rows — node properties · utility · parameter sync · the Blackboard split |
| ⭐ **`L5` is reachable per item** | §6: retire *"after its replacement is live"*. ⚠ `L4.2` is what makes retirement **lossless** *(§6: folding a standalone window into a toolbar no longer removes a designer's floating placement)* — ⭐ that precondition is now met |
| ⭐ **`L6` unchanged** | `L6.1` extracts `PerspectiveWorkspace` and carries the registry, the context builder and the entity source across |
