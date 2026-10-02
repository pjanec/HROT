<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: the whole file — the Batch 98 return
stale-below: nothing
-->
# ⭐⭐⭐ REPORT — **Batch 98: the write Blueprint still refuses**

| | |
|---|---|
| **branch** | `claude/hrot-implementation-j1jvin` |
| **handoff** | [`HANDOFF_Batch98_The_Write_Blueprint_Still_Refuses.md`](HANDOFF_Batch98_The_Write_Blueprint_Still_Refuses.md) · ⛔ §2 **WITHDRAWN** by [`STEER_Batch98b…`](STEER_Batch98b_Properties_Is_A_Custom_Dialog.md) |
| **scope frozen at** | ⭐ **`18dfcbb25`** |
| **base for every RED** | ⭐ **`18dfcbb25`** |
| **started-marker** *(rule 1b)* | `9a4f054` |
| **ids allocated** *(rules 3/5)* | ⭐ **`BP-365` · `BP-366` · `BP-367` · `BP-368` · `BP-369`** — ⛔ no others. **Closed: `BP-360`** |

---

## 0. ⭐⭐⭐ THE THREE VERDICTS — **`R-106`, one row per item**

| item | verdict | |
|---|---|---|
| **`98a`** — OK refuses while PLANNING | ✅ **done** | `BP-365` · and two further defects it uncovered, `BP-366`/`BP-367`, without which it would have been **destructive** |
| **`98b`** — the Properties dialog | ⛔ **not started** *(re-specified mid-batch)* | A StructEdit-based build was begun and **REVERTED IN FULL, before any commit**, on the user's steer. 📌 `R-109`: it must be a **custom** dialog. Filed as **`BP-369`** with the whole design |
| **`98c`** — the outline's dead Watch entry | ✅ **done** | `BP-360` closed |

⭐ **`98b` blocked nothing.** It was last-but-one in the handoff's order and `98c` did not depend on it —
⭐ **`98c` was built after the revert**, which is exactly what `R-106` asks for.

### ⚠ `98b` — **what happened, in order**

1. Built the measurement: `IVariablesSchemaSource` has **no** property write seam; the 8/5/4 property
   sets differ per carrier; 📐 **`StructEdit` registers field editors BY TYPE and has no per-field
   read-only concept at all.**
2. Started a StructEdit-document implementation *(DTOs per kind, a neutral value carrier, a write seam)*.
3. ⛔ **User: *"skip the Properties; it can not be a StructEdit based dialog, it must be custom. Do not
   build it."*** ⇒ reverted **every** uncommitted file and deleted the new one. ⭐ Tree clean at the
   `98a` commit.
4. Merged the coordinator's steer and moved to `98c`.

⭐⭐ **The steer's diagnosis is right and my per-field read-only investigation was the wrong problem** —
`Name` is a rename *(the refactor service)* and `Type` is a retype migration *(`StructureHash` moves)*;
neither is a struct field write. ⭐ Read-only is dialog-level and Batch 96 already built it.

---

## 1. ⭐⭐ `98a` — **OK lands on a Blueprint variable while PLANNING** *(`BP-365`)*

📐 **The defect.** `CommitInitialValue` resolved its target through `DeclarationOwnerOf`, which
type-tests `store.ActiveAsset is IBlackboardManagedAsset` — ⛔ **and `BlueprintAsset` is not one.** ⇒ in
**PLANNING**, the ordinary authoring state, the owner was **always `null`** and OK refused on every
Blueprint variable. ⚠ The asymmetry was in ONE file: `:836` asks the row first *(`95a`)*, `:826` does not.

### ⚠⚠ I DEVIATED FROM THE HANDOFF'S ②, and here is the measurement

> **②** *"the seam should be host-supplied, the same route `writeLive` and `liveValueProvider` take —
> ⛔ not a new interface on the row"*

📐 **Measured:** Blueprint's schema sources are constructed **inside `BlueprintMyBlueprintWindow`, per
outline selection** — the asset-scoped one at `:416`, the graph-scoped one at `:223` — ⛔ **long after
`CreateRegistrar` has returned**, and the graph-scoped one follows the canvas by delegate. ⇒ a seam
supplied at the composition root could answer for the two asset-scoped sections and **not** for Local
Variables. ⭐⭐ **That is the same measurement `95a` made for the READ**, which is why the read arm lives
on the row too.

⇒ ⭐ **The seam is on the ROW**, and `CommitInitialValue` asks it first then falls back to the asset —
**one preference order, the same one `ResolveEntry` already uses**, ⛔ not two mechanisms.

⭐ **The handoff's ① was followed exactly:** `IVariablesSchemaSource` gained the write, ⛔ and
`IBlackboardManagedAsset` was **not widened** — §4's prohibition, and a rail pins the premise so a later
widening goes red.

### ⛔⛔ Two further defects, and `98a` would have been DESTRUCTIVE without them

| | |
|---|---|
| **`BP-366`** | `ResolveVariableSelection:416` built its schema source with **`onChanged: () => { }`** while the same window computed a real `markDirty` from the same asset **~260 lines above**. ⚠ Harmless while the source was read-only ⇒ **that is why it survived**. ⛔ Once `98a` gave it a write, the edit would land in memory and **die on close** |
| **`BP-367`** | `Entries:111` built a **three-argument** `BlackboardVariableEntry`, so `DefaultValueJson` was **never projected** — and the line consuming it calls itself *"Row 58 — the INITIAL arm's source."* ⇒ the dialog opened at the **TYPE's** default. ⛔⛔ After `98a`, an **OK with nothing typed** would overwrite an authored `1` with `0` |

⭐ **Both are railed**, and the round-trip rail asserts an unedited open-and-OK is a **no-op in value terms**.

---

## 2. ⭐⭐ `98c` — **the outline's Watch entry stops being dead** *(`BP-360`)*

📐 `MyBlueprintContextMenu:40` enables on `commands.Get("editor.toggle-variable-watch") is not null`, and
**nothing registered that id.** ⇒ Batch 94's *"ONE command, TWO entry points"* shipped with **one**.

| ⭐ | |
|---|---|
| the toggle | moved out of `AttachWatchGesture`'s closure onto the registrar, so **both entry points call the same one** — ⛔ a second copy drifts on the unpin-first rule |
| the route | `IVariableWatchToggleHost`, installed in the registrar's ONE `RegisterExtraWindow` pass — 📌 `R-67`, the same route the live projection takes |
| ⛔⛔ **the trap avoided** | **registered only when a real toggle exists.** The menu enables on `Get(id) != null`, so an unconditional registration would have **enabled** the entry on a Watch-less perspective and made the click do nothing — re-creating `BP-360` one layer down |
| the refusal | an id naming no variable reports through the window's own indicator *(`BP-223`/`Q26-B2`)*, ⛔ never silently |
| ⭐ **one delegate, not two** | the Details table also takes an `IsWatched` predicate *(to render "Stop watching")*; the outline menu draws a fixed label and has **no dependency on the variable assembly by design** ⇒ taking a predicate here would be **a dependency with no consumer** |

---

## 3. ⭐⭐⭐ **WHOSE OBJECT · WHICH LAYER IS FAKED** — per rail *(📌 `M-22`, `M-29`)*

| rail | takes its input from | ⛔ FAKES |
|---|---|---|
| `TheBlueprintPlanningEditLandsTests` *(7)* | the **real** `EditorSubsystem` composition root, its **real** registrar and binder, the **real** `BlueprintMyBlueprintWindow` row source, a **real** `BlueprintAsset` and a **real** `BlueprintFileAsset` on disk | ⭐ the **DRAW** layer only — `R-21`/`R-62`; the gesture is raised by `OnEditValue` and the typed value is set through the same binding `DrawLeafNode` mutates. ⛔ **Nothing about the declaration, the write or the dirty flag is faked** |
| `TheOutlineWatchEntryIsLiveTests` *(5)* | the **real** registrar, its **real** Watch store, its **real** `RegisterExtraWindow` pass, the **real** outline window; the item id comes from **the outline itself** | ⭐ the **DRAW** layer, and ⚠ **the registrar's CONSTRUCTION** — 📐 a headless `EditorSubsystem` has `registrar.Watch == null` *(the Watch needs a breakpoint manager, set in `Initialize`)*, so the harness builds the services bundle with one. ⛔ 📌 `R-67` — **it therefore cannot see a composition-root defect** |
| `TheEditActuallyLandsTests` *(7, +1 new)* | the **real** `EditorSubsystem` registrar; the assets are `TestManagedAsset`, which carries its own note | ⭐ the **DRAW** layer |

⭐⭐ **The unrailed draw, stated plainly:** ⛔ **no rail in this batch asserts that anything RENDERS.**
`R-21`/`R-62` stand.

---

## 4. ⭐⭐ REVERT PROBES — **one per production edit, never delegated**

⛔ **Every probe un-applied with the INVERSE EDIT**, never `git checkout --`.

| # | the probe | ⭐ went RED |
|---|---|---|
| **P1** | remove the row-first arm from `CommitInitialValue` | 3 of 7 — lands / typed-value / dirty |
| **P2** | restore `onChanged: () => { }` at the row source | `APlanningEdit_MarksTheDocumentDirty` |
| **P3** | un-project `DefaultValueJson` from `Entries` | `APlanningEdit_LandsInTheDeclaration` *(the destructive case)* |
| **P4** | registrar stops installing the watch toggle | 3 of 5 |
| **P5** | drift the registered command id | 3 of 5, incl. the three-way agreement rail |

⚠ **The `98b` work was reverted wholesale rather than probed** — it never reached a commit.

---

## 5. ⭐⭐⭐ THE GATE TABLE — **the seven-row contract**

⭐ Base for every RED: **`18dfcbb25`**.

| gate | `--no-build`? | result | Δ baseline |
|---|---|---|---|
| solution build | — | **0 errors** · `EXIT=0` | — |
| **AiShared** | ✅ | **1705 / 0 / 0** · `EXIT=0` | **0** |
| **BTree.Editor** | ✅ | **622 / 0 / 0** · `EXIT=0` | **0** |
| **Hsm.Editor** | ✅ | **554 / 0 / 0** · `EXIT=0` | **0** |
| **Blueprints** | ✅ | **3827 / 0 / 10 skip** · `EXIT=0` | **+13** *(3814)*, skips **0** |
| **Hrot.Editor** | ✅ | **201 / 0 / 0** · `EXIT=0` | **0** |
| **Breakpoints** | ✅ | **143 / 0 / 0** · `EXIT=0` | **0** |
| **Generators** | ✅ | **277 / 0 / 0** · `EXIT=0` | **0** |
| **Persistence** | ✅ | **143 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.Core** | ⛔ **NO** *(out of solution)* | **211 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.UI** | ⛔ **NO** | **135 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **Fhsm** | ⛔ **NO** | **300 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **StructEdit** | ⛔ **NO** | ⚠ **191 / 1 / 0** | **0** — ⭐ `BP-363`, **pre-existing and unchanged** |
| **Fdp.Presentation** | ✅ | **146 / 0 / 0** *(`BP-337` filter)* · `EXIT=0` | **0** |
| **Fdp.Toolkits** | ✅ | ⚠ see below | — |
| `tracker-counts.py --check` | — | **OK — open 78 / done 226 (+1 refuted)** · `EXIT=0` | done **+5** |
| `rulings-check.py` | — | **74 / 74 verified** · `EXIT=0` | **+1** *(`R-109`)* |
| `design-digest.py --check` | — | **52 docs OK** · `EXIT=0` | — |

### ⭐ Row 3 — **golden movement as a DIFF SHAPE**

⛔ **ZERO golden files, ZERO asset `.json` files.**
`git diff --name-only 18dfcbb25..HEAD | grep -iE "golden|\.json$|Assets/"` → **nothing**.
⇒ code + tests + docs only.

### ⭐ Row 4 — **every RED confirmed pre-existing**

| RED | evidence |
|---|---|
| `StructEdit.Tests…Build_CircularReference_CircularFieldIsUnsupported` | ⭐ **`BP-363`**, confirmed in a clean worktree last batch and **identical here: 1 failed / 191 passed.** ⛔ Not this batch's; nothing in this diff touches StructEdit |
| ⚠ **`Fdp.Toolkits.Tests`** | 📌 **`DEBT-AIB-030` REPRODUCED LIVE THIS BATCH.** Three full runs: **1964 green**, then **1 failed**, then **2 failed with DIFFERENT names** *(`GizmoRegistryTests.SC_GZ004_2` · `StatelessGizmoRegistryTests.SC_GZ022_2`)*. ⭐ **Isolated by namespace as the debt requires: `--filter "…Gizmos.Tests"` ⇒ 187 / 0.** ⇒ cross-test interference, ⛔ not a regression |

### ⭐ Row 5 — **the working tree is CLEAN after every suite run** · `git status --short` → empty.

### ⭐ Row 6 — **quarantine counts:** Blueprints skips **10 → 10**; every other suite **0**. ⛔ **No new skip.**

### ⭐ The **+13** on Blueprints, itemised

**7** `TheBlueprintPlanningEditLandsTests` · **5** `TheOutlineWatchEntryIsLiveTests` ·
**1** `APinnedRowWhoseAssetIsNoLongerOpen_Refuses`.

---

## 6. ⛔⛔ THE ONE THING THAT WENT RED AND WAS **MINE**

⭐⭐ **Batch 96's own rail caught a hazard `98a` introduced, during the gate run** — ⭐ and it is worth
reporting as loudly as the fixes.

📐 `BlackboardSectionRowSource` resolves its asset **per call** *(right for BUILDING rows — the active
document changes under it)*. ⇒ the row's new write-back would have written **a row pinned from asset `A`
into whatever document is open now**. ⚠ The Watch mixes rows from arbitrary assets, so this is the exact
wrong-asset failure Batch 96 guarded.

⭐ **Fixed** with the SAME asset-identity guard Batch 96 put in `DeclarationOwnerOf`. **`BP-368`**.

⚠⚠ **And I CHANGED Batch 96's rail, which needs justifying rather than doing quietly.**
`ARowFromAnotherAssetDoesNotWriteIntoTheOpenOne` asserted `RefusedNoDeclarationOwner` and *"neither asset
was written"*. 📐 **That refusal was a LIMITATION, not the safety property** — the owner could only ever
be `store.ActiveAsset`, so a stray row had nowhere to go at all. ⭐ Now that the row carries its own
write-back, a stray row **can** resolve its own declaration and writing there is **correct**.
⇒ ⭐⭐ **the rail now asserts the property — the OPEN document is untouched — and
`APinnedRowWhoseAssetIsNoLongerOpen_Refuses` covers the hazard the old wording was really guarding**, in
the production shape. ⛔ **Widened, not loosened**, and both directions are asserted.

---

## 7. ⭐ What Batch 99 inherits

| | |
|---|---|
| ⭐⭐⭐ **`BP-369`** *(new, `RW-H`)* | **the Properties dialog, as a CUSTOM form** — 📌 `R-109`, and the steer's §3 is the design: factor `VariableCreateModal`, `VariablePropertySchema.For(kind)` as the filter, `Name` through the refactor service, `Type` disabled with its reason if a retype cannot be made safe |
| ⭐ **`BP-364`** | BTree/HSM live writing — a capability, still |
| ⭐ **`BP-363`** | StructEdit's missing static cycle fence *(`R-104`)* |

⭐ **The user's acceptance test now passes on Blueprint in PLANNING** — open `Count4`, right-click
`Count`, *"Edit value…"*, type, **OK**, the value changes **and the document goes dirty**.
⚠ **Expected, not findings:** an `Instance` blueprint refuses a LIVE edit *(correct)* · BTree/HSM refuse
a live edit and say why *(`BP-364`)* · a pin does not survive a scenario reload *(`94g`)*.
