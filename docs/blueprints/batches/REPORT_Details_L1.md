<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L1 built, measured and found.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, layer `L1`** *(the registry)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §2 · §5 · §6 `L1` · **dispatch:** [`HANDOFF_Details_Panel_Migration.md`](HANDOFF_Details_Panel_Migration.md)
> **started at** `55cc60e1` · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ **Re-synced from the coordinator at the start** *(rule 7)* and **re-pulled before the final commit**
> *(rule 4)*.
> ⛔ **No diagram in this report** — 📌 *diagrams live in the design, never in the batch*; everything
> below CITES §2/§5/§6.

| item | verdict | one line |
|---|---|---|
| **`L1.1`** | ✅ **done** | descriptor · instance · registry — §2's members, unchanged |
| **`L1.2`** | ✅ **done** | registration through the **existing claim chain**, ⛔ no new root argument |
| **`L1.3`** | ✅ **done** | `VariableDetailsSection` **wrapped**, not rewritten |
| **`L1.4`** | ✅ **done** | `ExactlyOne<T>` — `L0.2`'s deleted rule, in **one** predicate |

⭐ **IDs I allocated:** **`BP-393`** *(the layer)* · **`BP-394`** *(a rail of mine that could not fail)*.

---

## 1. ⭐⭐ OBLIGATION ③ — **the UML check, and the one deviation**

📐 §2's `classDiagram` carries **13 classes**; `L1` owns **4** of them.

| design element | built | match |
|---|---|---|
| `DetailsViewDescriptor` *(Id · Title · Rank · AppliesTo · Create)* | ✅ | ⭐ **exact** |
| `IDetailsViewInstance` *(Draw · Dispose)* | ✅ | ⭐ **exact** |
| `DetailsViewRegistry` *(Add · OfferSet · Default)* | ✅ | ⭐ **exact**, with `Default` **nullable** — §2b's `stateDiagram` has an explicit `EmptyOffer` state, so the honest signature admits it |
| `PerspectiveWorkspace *-- DetailsViewRegistry` | ⚠ **DEVIATION** | see below |

### ⚠ The deviation — the registry's home

⛔ §2 composes the registry into **`PerspectiveWorkspace`**. 📐 That type is extracted in **`L6.1`**
*(§6: "extract `PerspectiveWorkspace`, give Scenario one, rename the key with a layout migration")*, ⛔
not in `L1`.

⇒ ⭐ the registry lives on **`PerspectiveWorkspaceRegistrar`**, and `L6.1` carries it across when it
splits §5's *"wiring hub"* half out. 📌 §5 is explicit that the generic half is *"trapped inside the
specific one"* — this is one more thing that travels with it, ⛔ **not a second registry**.
⚠ Same shape as `L0.3`'s builder deviation, and for the same reason: the diagram describes the finished
design, and the layer order decides when each box arrives.

---

## 2. ⭐⭐ WHAT THE RULINGS BOUGHT — **each one is a mechanism, not a comment**

| ruling | ⭐ mechanism |
|---|---|
| **`R-116`** the predicate ships with the view | `AppliesTo` is a **field of the descriptor** ⇒ the registry never learns what a node or a variable row is |
| **`R-112`** `AssetKind` is never a view key | ⭐ railed directly: the SAME registry answers differently for two perspectives, purely through predicates. ⛔ That is the mistake §4 dissolves `RuntimeInspectorWindow` for |
| **`R-120`** a view owns no shared state | descriptor + **factory** ⇒ two callers get two instances. ⛔ No `Instanceable` flag, nothing to arbitrate |
| **`R-98`** default by `Rank` | `OfferSet` ranks; `Default` is the head |
| **`R-117`** a blank panel is a defect | ⭐ **empty is a REAL answer**, and `Default` returns `null` rather than a fallback — ⛔ a view claiming a context it rejected would lie |
| **`R-67`** no new root argument | the claim chain, via `IDetailsViewSource` |

### ⭐ And one guard that is not from a ruling

⛔ **A duplicate `Id` THROWS at registration.** ⚠ Last-wins and first-wins are both *silent*, and the
symptom would be a view that mysteriously never appears. 📌 Same reasoning as `G4`'s duplicate-name
guard: an id collision is a **wiring** bug and must fail at the wiring.

---

## 3. ⭐⭐⭐ `L1.2` — **the claim chain, and the one place it does not reach**

📌 §6 `L1.2`: *"registration through the existing claim chain — ⛔ no new root argument"* *(`R-67`)*.
⭐ A window declares `IDetailsViewSource`; `RegisterExtraWindow` collects it. ⇒ ⛔ `EditorSubsystem`
gains nothing to forget — 📌 and this registrar is the one that has forgotten a service **four times**
*(Batches 79/80/81, then 96d)*.

⚠⚠ **`AiDetails` is built in the CONSTRUCTOR**, not handed in through `RegisterExtraWindow` ⇒ the chain
never sees it. ⭐ So its arm is **mirrored in the constructor**, beside the existing
`_outlineSelection ??= MyBlueprint; _detailsHost ??= AiDetails;` — which is exactly where that file
already mirrors the chain for the same reason. ⭐ Both paths share **one `_viewSources` guard**, so a
window reaching both registers exactly once.

---

## 4. ⭐⭐ `L1.3` — **wrapped, not rewritten, and the `L4` consequence stated now**

⭐ `VariablesDetailsView` **delegates** to `VariableDetailsSection` *(ruling 9 — a second variables table
would drift on exactly the thing that matters: which rows are shown)*.

⚠ **The instance BORROWS the section** and `Dispose` is deliberately empty: the section is built and
wired by the registrar *(run-state source, edit gestures, live projection — 📌 `R-67`'s four)*, so
disposing it when a float window closes would take the docked one down with it.

⇒ ⚠ **Stated now rather than discovered in `L4`:** because the section is shared, two windows showing
this view would share its scroll and selection — which `R-120` forbids. ⭐ The seam for the fix is the
descriptor's **factory**, which already returns per-window instances; `L4.2` is where it becomes real.

---

## 5. ⚠⚠ `BP-394` — **A RAIL I WROTE THAT COULD NOT FAIL**

⭐ **The most useful thing this batch found, and it was found by a probe.**

| step | |
|---|---|
| 🔴 **the code defect** | `OfferSet` used **`List.Sort`** — introsort, **UNSTABLE** — while the comment beside it claimed *"equal ranks keep registration order"*. ⇒ a default that can flip between runs |
| ⭐ **fixed** | `OrderByDescending`, which is documented stable |
| ⛔⛔ **the RAIL was the second defect** | written with **10** same-rank views, the probe *(restoring `List.Sort`)* **did not redden** |
| 📐 **why** | introsort delegates partitions **below 16 elements to INSERTION SORT** — stable in practice ⇒ **at n=10 the rail could not fail** |
| ⭐ **fixed** | **64** views forces the quicksort path; the probe then reddened |

📌 **The general lesson:** ⛔ a probe that does not redden is **a finding about the rail**, not permission
to move on. ⚠ And here the failing ingredient was **a constant**, not the assertion — which is the kind
of vacuity that survives review.

---

## 6. ⭐ REVERT PROBES — **each un-applied by the inverse edit**

| # | probe | result |
|---|---|---|
| **①** | `OfferSet` back to `List.Sort` | ⛔ **no red at n=10** → ⭐ **rail fixed to n=64** → ⭐ **1 red** |
| **②** | delete the claim-chain registration | ⭐⭐ **5 red of 7** — the production rails; ⚠ the two pure-predicate cases correctly stayed green, which is what makes them separable |

---

## 7. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L0`'s table**. Base sha **`55cc60e1`**. ⚠ Environment stated per row.

| gate | env | result | Δ vs `L0` |
|---|---|---|---|
| **solution build** | — | ⭐ **0 errors** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1752 / 0 / 0** | ⭐ **+16 — mine** *(9 registry + 7 production)* |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3882 / 0 / 10** | **0** |
| `Hrot.BTree.Editor.Tests` | — | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | — | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | — | **206 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | — | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | — | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | — | ⚠ **260 / 2 / 0** | **0** — the `D003_*` pair, pre-existing since Batch 103 |
| **tracker** | — | ⭐ **OK — open 83 / done 246 (+1 refuted)** | +2 done |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | — |
| **design digest** | — | ⭐ **OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*. ⛔ The out-of-solution four were not
touched by `L1` *(no file of theirs is in the diff)*; last measured green in `MIN`'s table.

### ⚠⚠ 7.1 — ONE UNREPRODUCIBLE AiShared FAILURE, reported because I could not name it

📐 **One run reported `1751 / 1 / 0`.** ⛔ **I did not capture the failing test's name** — the run used
`-v q`. ⭐ **Six consecutive re-runs since then are `1752 / 0 / 0`**, including three with `-v n`, which
would have printed the name had it recurred.

⇒ ⚠ **What I can say:** it happened once, immediately after a rebuild, and I cannot reproduce it.
⛔ **What I will not say:** that it is a known flake, or that it is in any particular test — ⭐ I have no
evidence for either, and 📌 naming a suspect I cannot demonstrate is how `M-38`-shaped false canon
starts. ⚠ **If it recurs, capture it with `-v n` first** — that is the missing measurement, not a
re-run.

### ⭐ Quarantine counts

`Hrot.Blueprints.Tests` **10 skipped** *(Xvfb)*, unchanged; every other suite **0**. ⛔ **No new skips.**

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **9 files: 4 changed, 5 added**, 0 deleted. ⛔ No `.approved.` / golden /
snapshot file appears in the diff.

---

## 8. ⭐ LANE CHECK

⭐ Every file touched is **UI/variable lane** — `AiShared` and its tests. ⛔ **Nothing under
`Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel` or the integration tests** *(`R-128`)*.
⭐ ids are **`BP-`**; ⛔ no `TM-`, no `Area H`.

⛔ **Still not touched, and still the coordinator's:** the staged-write / yellow story *(`R-126`,
`R-130`, the new `DESIGN_Staged_Live_Write.md` and `IStagedWrites`)* — 📌 handoff §2.

---

## 9. ⭐ WHAT `L1` UNBLOCKS, AND WHAT IS STILL OPEN

| | |
|---|---|
| ⭐ **`L2` is now unblocked** | §6's graph: `L1.2 → L1.3/L1.4 → L2.1`. The shell can ask a real registry for a real offer set |
| ⭐ **`L4.1` is also unblocked** | §6: *"`L4` needs `L1`, not `L2`"* |
| ⛔ **`L0.4` still not started** | `BP-392` — entity selection still reads the editor-side copies. ⚠ Not blocking `L1` or `L2`; it gates `L4.1` |
| ⚠ **`BP-391` still open** | the HSM mixed node+link gap. ⭐ **`L1.4` is half its answer** — the predicate now exists; ⛔ the grey line that makes it visible is `L2.3` |
