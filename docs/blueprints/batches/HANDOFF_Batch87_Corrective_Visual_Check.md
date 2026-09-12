<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: this whole file - the Batch 87 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# HANDOFF — Batch 87: **the three defects the visual check found, and row 58's missing half**

> 📌 **Dispatched at `0477bb98e`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 87 at 0477bb98e` FIRST, before any code.**
>
> ⭐⭐⭐ **READ [`FINDINGS_VisualCheck_PostBatch86.md`](FINDINGS_VisualCheck_PostBatch86.md) FIRST — it IS
> this batch's investigation.** ⛔ **Do not re-derive it.** Every root cause below is measured there with
> file:line.

---

## 1. ⭐⭐⭐ WHY THIS BATCH EXISTS

The user ran the Blueprint visual check on Batch 86's tree. **Six failure groups.** Triaged:
**three real defects**, four errors in MY guide, one pass I mislabelled.

⭐⭐ **The headline finding is not any one defect — it is that `BP-327` was MASKING TWO OTHERS.**
⛔ *"The dialog has no OK button"* absorbed *"the Details table has no menu at all"* and *"the panel
never hands back to the node arm."* ⇒ ⚠ **Fixing `BP-327` alone would have produced a dialog nobody
could open.**

⭐ **`BP-327`'s own filed blocker is now GONE:** it says *"not fixable in a batch that may not do visual
work (`R-62`: no visual checks)"* — 📌 **`R-62` LIFTED that for Blueprint.** ⇒ ⭐⭐ **this is the batch it
named.**

---

## 2. 🛠 THE WORK — **five items, IN THIS ORDER**

> ⭐⭐ **The order is the dependency order, not a priority list.** ⛔ Item 2 without item 1 is invisible;
> item 1 without item 2 is a menu that opens nothing.

### ⭐⭐⭐ 2a — Draw the modal *(`BP-327`)*

📄 **Design basis:** `DESIGN_Variable_Details_And_Editing.md` §3–§4 · ruling 5 *("opens a StructEdit-based
editing window, OK / Cancel, initialised to the variable's current value")* · `BP-327` *(filed Batch 84)*.

⭐ **Batch 84 built the ENTIRE headless path** — gesture → session → `Accept()` → the run-state arm → the
world or the declaration. ⛔ **`VariableEditLauncher.Open` returns an `IEditSession` and NO SURFACE DRAWS
IT.** ⇒ ⭐ **this item is the surface, not the mechanism.**

| ⭐ | |
|---|---|
| **OK / Cancel** | ⭐ **Cancel must leave the declaration untouched** — guide `D7` |
| **two scopes, ONE dialog** | 📌 design §3: *"Edit value…"* ⇒ `EditScope.ForField` · *"Properties…"* ⇒ `EditScope.WholeComponent`. ⛔ **Same lifecycle, same OK/Cancel, same validation** |
| ⭐⭐ **refusals GREYED + TOOLTIP** | 📌 `F3` and the user's `2026-08-17` ruling: *"showing explanatory tooltip would be better than allowing user to click the button and then saying that it is not possible — same information value, no false expectations."* ⭐ **`LiveWriteUnavailable` and `RefusedRunning` are the two words that surface must render** |
| ⛔ **do NOT rebuild the write path** | ⭐ it ships; route to it *(ruling 9)* |

### ⭐⭐⭐ 2b — Attach the gesture binder to EVERY table host

📐 **Measured, `PerspectiveWorkspaceRegistrar:306`:** `EditGestures.Attach(Variables.Control)` — **the
standalone Variables window only.** ⛔ **`BlueprintDetailsWindow:83` builds its OWN
`VariableDetailsSection` and nothing attaches to it.** ⇒ **`D2`/`D3`/`D11` fail because there is NO MENU
THERE.**

| ⭐ | |
|---|---|
| ⭐⭐ **enumerate the table hosts — do not grep for two** | 📌 **`R-74`: only the graph enumerates.** ⚠ **Known: three** — the standalone `Variables`, `BlueprintDetailsWindow`'s section, and **`AiWatchWindow`** *(`BP-330`: `_control` is **private with no accessor** — ⭐ **expose it the way `WatchPanelWindow` already does**)*. ⛔ **If the graph finds a fourth, that is a finding — report it** |
| 🔴🔴 **THE RAIL** | ⭐⭐⭐ **Assert on the CONSTRUCTED objects: every table host a perspective builds has `IsEditGestureBound == true`.** 📌 `VariableTableControl:56` already exposes exactly that property. ⛔ **A rail over the registrar's SOURCE cannot catch this** — that is why Batch 83's rails passed |

### ⭐⭐ 2c — Render the selection *(`B3`)*

📐 **The chain is fully wired and ends one call short.** `BlueprintMyBlueprintWindow:357` sets
`SelectedVariablePath` → `VariableDetailsSection:119` applies it → `VariableTableView.IsSelected(row)`
computes it — ⛔⛔ **and `VariableTableControl` NEVER CALLS `IsSelected`. Zero references.**

| ⭐ | |
|---|---|
| ⛔ **keep the two highlights ORTHOGONAL** | 📌 the type's own doc: *"Do NOT express selection through the change highlight… a header would read 'something changed' because the designer clicked."* ⇒ ⭐ **a selected row that ALSO changed this tick shows BOTH** |
| 🔴 **THE RAIL** | ⭐⭐ **Ask the ARTEFACT, not the model.** ⚠ **`IsSelected` already returns true today** — a rail on it passes while nothing renders. 📌 **The `CellText` lesson from Batch 83: assert what the CONTROL would draw** |

### ⭐⭐ 2d — Give the panel back to the node arm *(`B8`)*

📐 **`BlueprintDetailsWindow`:** `ShowVariables` snapshots `_lastSubSelection = ActiveSubSelection`;
`ShowingVariables` returns `Equals(ActiveSubSelection, _lastSubSelection)`.
📌 **The comment states a TIME claim** — *"a node selection that arrived **AFTER** the variable list wins
it back"* — ⛔⛔ **the code tests VALUE INEQUALITY.**

| 📐 two measurements make that fatal | |
|---|---|
| **a variable click never moves `ActiveSubSelection`** | `BlueprintMyBlueprintWindow` has **zero** references to it ⇒ the snapshot records **the node that was already selected** |
| **`BlueprintNodeSelection` is a `sealed record`** | ⇒ **value equality** ⇒ re-clicking the **same** node is `Equals` to the snapshot |

⇒ ⭐⭐⭐ **Re-clicking the same node can NEVER take the panel back.** ⭐ A *different* node works — ⛔ **which
is why every test passed and the designer's actual gesture failed.**

⭐⭐ **Fix: a shared ORDERING TOKEN both arms bump** — ⛔ **not an equality test on a field only one arm
writes.** ⚠ **Rail: click node N → variable → node N AGAIN ⇒ the node arm draws.**

### ⚠ 2e — Blueprint's live-value provider *(row 58's unbuilt half)* — ⭐ **LAST, and STOPPABLE**

📐 **Measured:** `EditorSubsystem` passes a provider for BTree *(`:2178`)* and HSM *(`:2188`)* and ⛔
**none for Blueprint**; ⭐ **zero `ILiveBlackboardValueProvider` implementations exist under
`Hrot.Blueprints.*`** ⇒ guide `C7` shows **`(pending)`**, which is the **designed** rendering for a source
with no byte reader.

📌 **`Q32` §4 row 58 required it:** *"the Value column… **+ blueprint's `ILiveValueProvider`** and
`UpdateVariableDefaultValueJson`."* ⚠⚠ **Batch 83 neither built nor claimed it, and I merged the row —
that is MY miss, recorded in the findings.**

> ⭐⭐⭐ **THIS ITEM MAY GROW BEYOND A CORRECTIVE BATCH.** ⛔ **If it does — STOP AND REPORT after 2a–2d,
> with the tree clean.** ⭐ **Stopping is a good outcome** *(Batch 85 is the model)*. ⚠ **Do NOT half-build
> a provider.**

---

## 3. ⛔ OUT OF SCOPE

| ⛔ | |
|---|---|
| **the `⋮` three-dot button** | ⚠ **Ruling 5 says *"a three-dot button AND double-click"* and only a RIGHT-CLICK menu exists.** ⭐ **Deliberately deferred** — 2a–2d make double-click work, which is the half that blocks the check. ⛔ **Do not add it here** |
| **Watch PINNING** *(`E2`–`E7`)* | 📄 `DESIGN_Variable_Watch_Pinning.md` — its own batch |
| **Task groups `A` / `B` / `C`** | 📄 `PLAN_Remaining_Work.md` rev 31 — ⛔ **none of them, including `R-86`'s renamable/editable ruling** |
| **`Q41`–`Q43`'s builds** | approved as DESIGN; ⛔ not scheduled |
| **BTree / HSM anything** | 📌 `R-21`/`R-60` — still suspended |

---

## 4. ⭐ GATES — **the rule-8 contract, plus the two this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · golden movement as a **diff shape** · every red confirmed **pre-existing vs `0477bb98e`** · clean tree after every suite · both quarantine counts · `tracker-counts.py --check` + `rulings-check.py` + **every id you allocated** |
| ⭐⭐ **8** | 🔴 **The 2b enumeration: the `search_graph` query, its `total`, and the list of table hosts** — ⛔ **not "I found the two I was told about"** |
| ⭐⭐ **9** | ⭐ **For 2c and 2d: state what the rail ASKS.** ⛔ **A rail that asks the model passes today and proves nothing** — 📌 say which artefact it interrogates |

⭐ **Baseline** *(Batch 86, last green)*: AiShared **1397** · Blueprints **3767/3777/10** ·
tracker **open 68 / done 199** · rulings **59/59**.

⚠ **`Fdp.Toolkits.Tests` needs no coordinator run** — 📌 `DEBT-AIB-030`: seven tests, the identity
ROTATES. ⭐ Confirm by `--filter` and say so.

## 5. ⭐⭐ If you must stop

⭐ **Stopping is a good outcome.** ⛔ **The one unacceptable end state is 2a landed without 2b** — a
dialog nobody can open, which is the exact shape this batch exists to remove.
