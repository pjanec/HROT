<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - rewritten for Batches 87 and 88, and to correct
  FIVE rows the 2026-08-18 run proved wrong.
stale-below: nothing. The previous revision's part-D framing ("BP-327: no OK button")
  and its "BTree/HSM out of scope" fence are BOTH gone - do not quote them.
known-rot: none known. Every "not built" claim below names the command that measured it
  on 2026-08-19; re-measure rather than trusting the sentence.
note: GUIDE_Track_C_Visual_Check.md parts B (Inspector default value) and F (change
  highlighting) still stand and are not repeated here.
-->

# GUIDE — **the visual check**, post-Batch-88

> ⭐⭐⭐ **THE SUSPENSION IS LIFTED ON ALL THREE HOSTS.** 📌 **`R-21`** held every visual check until
> *"the Details panel is implemented and the emitters and all access infrastructure are unified."*
> ⇒ ✅ **Blueprint** *(Batch 82)* · ✅⭐ **BTree and HSM** — **Batch 88b built `AiDetailsWindow`**.
> ⛔⛔ **The previous revision's fence — *"do not check BTree/HSM"* — IS GONE.** 📌 **`M-21`.**
>
> ⭐ **Every row ends in a PASS/FAIL you can write down.** ⛔ **Nothing asks you to judge whether
> something "seems right"** — each expectation cites the ruling it comes from.
> ⏱ **Budget: ~40 minutes** *(was 30; part H is new)*. ⭐ **Do the parts in order.**

---

## 0. ⛔⛔⛔ READ THIS FIRST — **five rows of the last edition were WRONG**

⚠ **The `2026-08-18` run reported eight failures. FOUR were errors in the guide, not in the product**
*(📄 [`FINDINGS_VisualCheck_PostBatch86.md`](FINDINGS_VisualCheck_PostBatch86.md) §2)*, ⭐ **and a fifth
is new.** ⛔ **All five are corrected below. Do not re-report them from the old edition.**

| was | ⭐ now |
|---|---|
| **`D1`** *"click the `⋮`"* | ⛔ **there is no `⋮` button** — ⭐ **it is a RIGHT-CLICK menu.** ⚠ Ruling 5 wants *"a three-dot button AND double-click"*; **the button half was never built** |
| **`C7`** *"live values while running"* | ⚠ **`88a` made the *Blackboard Variables* window live** — ⛔ **Details is still `(pending)` on ALL THREE hosts** *(`BP-334`)* |
| **`E2`–`E7`** *"pin a variable…"* | ⛔ **watch PINNING is still not built.** ⭐ **Only `E1` is runnable** |
| **`C2`** *"read a declared default"* | ⚠ **depends on part D**, which is still blocked — ⭐ **now has its own route, see `C2`** |
| 🔴 **part D** *"`BP-327`: the dialog has no OK button"* | ⛔⛔ **THAT DIAGNOSIS IS DEAD.** Batch 87 built the modal — ⭐ **but nothing DRAWS it.** See `0a` |

## 0a. ⛔ NOT BUILT — **a failure here is EXPECTED. Record it, do not diagnose it**

| ⛔ | 📐 measured `2026-08-19` |
|---|---|
| 🔴🔴 **THE EDIT DIALOG STILL DOES NOT APPEAR — part D will fail, for a NEW reason** | ⭐⭐ **`VariableEditModal` is built and exposed** *(`PerspectiveWorkspaceRegistrar:328`/`:602`)*, and **its `Draw()` has ZERO callers — production or test.** `grep -rn "EditModal" --include=*.cs` ⇒ **construction · the property · two test asserts. No frame ever calls it.** ⚠⚠ **This is one level up from `BP-327`**: Batch 84 built the write path nothing drew, Batch 87 built the dialog **nothing calls**. ⛔ **Report part D as blocked; do NOT re-diagnose it** |
| ⛔ **watch PINNING** | `PinnedVariableRowSource` exists and `AiWatchWindow.Pinned` exposes it — ⭐ **nothing in production ADDS to it.** ⇒ `E2`–`E7` unrunnable |
| ⛔ **the `⋮` three-dot button** | `VariableTableControl.DrawRowMenu` opens on **`BeginPopupContextItem()`** — right-click only |
| ⚠ **the Value column in DETAILS** | **`BP-334`** — `ILiveBlackboardValueProvider` has **exactly ONE consumer** *(`BlackboardAuthoringWindow:524`)*; the Details table's live arm is `readRaw`, which **no production caller passes** ⇒ ⭐⭐ **`(pending)` in Details is a KNOWN gap on every host** |
| ⚠ **`GroupBy` / fold persistence / the `Type` toggle persisting** | not built *(settled Batch 79)* |

## 0b. ⭐⭐ WHAT BATCH 87 FIXED — **these are now the REGRESSION rows**

⭐ **`B3`, `B8`, `E6` and the row menus were the last run's REAL defects.** ⇒ ⛔ **a failure in any of
them is a REGRESSION, and the single most valuable thing this run can find.**

---

## A. My Blueprint — the outline *(Blueprint)*

| # | do | expect | ✔ |
|---|---|---|---|
| **A1** | Blueprint perspective, open any blueprint asset | the **My Blueprint** panel is present, populated | ☐ |
| **A2** | Read the section list | ⭐⭐ **ONE state section** *(Batch 86)* — ⛔⛔ **a `Working State` section HERE is a FINDING.** ⚠ **On BTree/HSM it is CORRECT — see part H** | ☐ |
| **A3** | Find **`Local Variables`** | present, and the **only graph-scoped** section | ☐ |
| **A4** | Switch graph on the canvas | ⭐ **`Local Variables` follows the canvas** | ☐ |
| **A5** | Find a section with no declarations | ⭐⭐ **renders EMPTY with its header — ⛔ does not vanish** | ☐ |
| **A6** | Click any section's **`[+]`** | ⭐ **the same new-variable dialog for EVERY section** *(`R-17`)* | ☐ |
| **A7** | Open a **Macro** graph, click `[+]` on `Local Variables` | ⭐ **refuses OUT LOUD** — ⛔ not a silent no-op | ☐ |
| **A8** | Look for a **Role** or **Scope** dropdown, anywhere | ⛔⛔ **there is none.** The SECTION is the classification | ☐ |

---

## B. ⭐⭐ Details hosts the TABLE

> 📌 **`Q32` ruling 1:** *"Details hosts the list of vars."* · ⛔ *"The table is never replaced by a
> single-variable form."*

| # | do | expect | ✔ |
|---|---|---|---|
| **B1** | Click a variable row in **My Blueprint** | ⭐⭐⭐ **a TABLE of variables** — ⛔⛔ **NOT a property form** | ☐ |
| **B2** | Look at which rows are in it | ⭐ **every variable of that row's SECTION** | ☐ |
| **B3** | ⭐⭐ **REGRESSION ROW** — find the row you clicked | ⭐ it is **highlighted** inside the table. 📌 **Batch 87 fixed this** *(computed, never drawn)* ⇒ ⛔ **a failure is a REGRESSION** | ☐ |
| **B4** | Read the column headers | ⭐ **`Name` · `Value` · `Type`** — ⛔⛔ **no `Bytes`, `Role` or `Scope`** | ☐ |
| **B5** | Click a **`Local Variables`** row | the **CURRENT graph's** locals; the heading **names the graph** | ☐ |
| **B6** | Switch graph while showing locals | the table **follows** | ☐ |
| **B7** | Click a **Graph** / **Function** / **Macro** node in the outline | ⭐⭐ **the table LETS GO** — ⛔ no stale list beside an unrelated selection | ☐ |
| **B8** | ⭐⭐ **REGRESSION ROW** — click a **canvas node**, then a variable, then **the node again** | ⭐ **the panel switches arms EVERY time, both directions.** 📌 **Batch 87 fixed this** *(`R-95` — the panel obeys the focused SURFACE, not the selected payload)* ⇒ ⛔ **a failure is a REGRESSION** | ☐ |

---

## C. ⭐⭐ The Value column

> 📌 **ruling 3:** *"ONE Value column, meaning switched by run state."* · **ruling 4:** *"Value is
> READ-ONLY in the cell."*

**Sim NOT running:**

| # | do | expect | ✔ |
|---|---|---|---|
| **C1** | Count the value columns | ⭐⭐ **exactly ONE** | ☐ |
| **C2** | ⭐ **CORRECTED ROUTE.** Select a node with a bound parameter variable, and read its default in **`InspectorWindow`'s `DEFAULT VALUE — {var}` panel**; then find that same variable in the Details table | ⭐ **the same initial value in both.** ⚠ **The Inspector panel is the ONLY live default editor today** — ⛔ **do not go looking for one in the table**, part D is blocked | ☐ |
| **C3** | Read one with **no** declared default | the type's **zero value** — ⛔ not blank, not `--` | ☐ |
| **C4** | Try to type into a value cell | ⛔ **you cannot** *(ruling 4)* | ☐ |
| **C5** | Hover a **struct** value | ⭐⭐ tooltip **pretty-printed, multi-line, one field per line** | ☐ |
| **C6** | Look at any row's value width | ⭐ **one line, never wrapping** | ☐ |

**Sim RUNNING** *(load a scenario, let it tick)*:

| # | do | expect | ✔ |
|---|---|---|---|
| ⚠ **C7** | ⛔⛔ **CORRECTED — do NOT expect live values in Details.** Watch a variable in the **Details** table while the sim runs | ⭐⭐ **`(pending)`, and that is the KNOWN state — 📌 `BP-334`.** ⛔ **Record it as PASS-as-expected.** ⚠ **A live value here would be a surprise worth reporting** | ☐ |
| ⭐ **C7b** | ⭐ **NEW — this is what `88a` actually delivered.** Open the **Blackboard Variables** window *(not Details)* on a **Blueprint** asset while the sim runs | ⭐⭐ **the Value column shows LIVE values and they change.** ⛔ **A `(pending)` here IS a finding** — that is `88a`'s deliverable | ☐ |
| **C8** | Look for raw hex, anywhere | ⛔⛔ **NEVER raw hex** *(`BP-01`, closed Batch 83)* — a hex string is a **regression** | ☐ |
| **C9** | Find a variable declared but never written | ⭐ **`(pending)`** — ⛔ not `<unreadable>` | ☐ |

> 🟡 **KNOWN, do not re-report.** A struct renders `{"X":1.0,"Y":2.0}` when not running and
> `{X=1.0, Y=2.0}` when running. ⭐ Ruling 3 switches the column's MEANING, not its NOTATION. Cosmetic.

---

## D. ⚠⚠ The edit dialog — **EXPECT D1 TO PASS AND D2 ONWARD TO FAIL**

> ⛔⛔ **The reason CHANGED.** The old edition blamed `BP-327` *("no OK button")*. ⭐ **Batch 87 built
> the modal — and `VariableEditModal.Draw()` has no caller** *(§0a)*, so no dialog can appear.
> ⭐⭐ **Run `D1` and `D1b` to confirm the MENU; record `D2`–`D11` as blocked, not as new findings.**

| # | do | expect | ✔ |
|---|---|---|---|
| ⭐ **D1** | ⛔ **CORRECTED — there is no `⋮`.** **RIGHT-CLICK** a table row | a menu with **"Edit value…"** and **"Properties…"** | ☐ |
| ⭐ **D1b** | ⭐⭐ **REGRESSION ROW — right-click a row in EACH of: Details · the standalone Variables window · the Watch** | ⭐⭐⭐ **the menu appears in ALL of them.** 📌 **Batch 87's fix** *(one attach point, `BoundTables`)* — ⛔ **a table with no menu is a REGRESSION** | ☐ |
| **D2** | **Double-click the VALUE cell** | *(blocked — expect nothing)* | ☐ |
| **D3** | **Double-click the NAME cell** | *(blocked)* | ☐ |
| **D4**–**D8** | ⛔ **skip — all require an open dialog** | *(blocked)* | ☐ |
| **D9** | Press **F2** on a row | ⭐ inline rename — ⚠ **this does NOT go through the modal**, so it may work. **Record which** | ☐ |
| **D10** | Rename via **Properties → Name** | *(blocked)* | ☐ |
| **D11** | Right-click a **My Blueprint** row | ⭐⭐ **the same menu as a table row** *(design §4: "identical on every surface")* | ☐ |

---

## E. ⭐⭐ The Watch panel

| # | do | expect | ✔ |
|---|---|---|---|
| **E1** | Open **Watch** with the sim **NOT running** | ⭐⭐ **it shows NOTHING** — ⛔ not stale rows, not `--` | ☐ |
| ⛔ **E2**–**E7** | ⛔⛔ **SKIP — watch pinning is not built** *(§0a)*. ⭐ **Do not record failures here** | — | — |
| ⭐ **E6b** | ⭐ **the runnable remnant of `E6`:** right-click a row in **each** watch window | ⭐⭐ **both offer the menu** *(`BP-330` closed, Batch 87)*. ⛔ The dialog still will not open — that is `D`, not `E` | ☐ |

---

## F. ⭐ Refusals — **the part most likely to be wrong**

> 📌 **User ruling, `2026-08-17`, verbatim:** *"disabling/graying a `[+]` … but showing explanatory
> tooltip would be better than allowing user to click the button and then saying that it is not
> possible — same information value, no false expectations."*

| # | do | expect | ✔ |
|---|---|---|---|
| **F1** | Sim **RUNNING/PAUSED**, open **Properties…** | *(blocked by D — record as blocked)* | ☐ |
| **F2** | Sim **RUNNING/PAUSED**, try **Edit value…** | ⭐ **the menu item is GREYED or it refuses.** ⚠ **Record HOW** — ⛔ the refusal is reachable even though the dialog is not | ☐ |
| **F3** | For every refusal | ⭐⭐ **greyed + a tooltip saying WHY** — ⛔⛔ **a click that dead-ends is a FINDING** | ☐ |
| **F4** | In **replay** | ⛔ everything read-only | ☐ |

---

## H. ⭐⭐⭐ NEW — **the Details panel on BTree and HSM** *(Batch 88b, `BP-317`)*

> ⭐ **This part has never been run.** ⛔ **The old edition forbade it.** 📌 `M-21`.
> ⭐⭐ **The window is `AiDetailsWindow`**, titled **`Details`**, ids `ai_details_btree` /
> `ai_details_hsm`, **one per perspective**.

| # | do | expect | ✔ |
|---|---|---|---|
| **H1** | Switch to the **BTree** perspective, open a `.btree` asset | ⭐⭐ **a window titled `Details` exists** — ⛔ its absence is the whole batch failing | ☐ |
| **H2** | Repeat on **HSM** | ⭐ **the same** | ☐ |
| **H3** | Read the **My Blueprint** section list here | ⭐⭐ **`Inputs` · `Working State` · `Asset Globals`.** ⛔⛔ **A `Working State` section here is CORRECT** — ⚠ **the opposite of `A2`**, because `R-01`'s collapse is a *blueprint* declaration-kind ruling, not a blackboard-role one | ☐ |
| **H4** | Click a variable row in the outline | ⭐⭐ **Details shows the TABLE of that section** — ⛔ not a form, exactly as `B1` | ☐ |
| **H5** | Find the row you clicked | ⭐ **highlighted** *(same mechanism as `B3`)* | ☐ |
| **H6** | Click a section, then something with no variables | ⭐ **the panel CLEARS** — ⛔ no stale list *(`B7`)* | ☐ |
| **H7** | With nothing selected | ⭐ **`"No variable selected."`** — ⛔ **not a blank window** | ☐ |
| **H8** | Right-click a row | ⭐ **the menu appears** *(the AI hosts go through the same one attach point)* | ☐ |
| ⚠ **H9** | Sim running — read the Value column | ⭐ **`(pending)` — EXPECTED**, 📌 `BP-334`, same as `C7` | ☐ |
| ⛔ **H10** | Click a **canvas node** here | ⚠ **Details does NOT switch to a node view** — ⭐ **by design**: this window has **one arm**; the AI node surface is `InspectorWindow`, which stays. ⛔ **Not a finding** | ☐ |

---

## G. ⭐ How to report

⭐ **Per row: the id, PASS or FAIL, and — if FAIL — what you saw.** ⛔ **Do not diagnose**; a screenshot
or one sentence is enough. ⭐⭐ **"PASS-as-expected" is a valid result** for the known-gap rows.

| ⭐ especially worth writing down | why |
|---|---|
| ⭐⭐⭐ **`B3` · `B8` · `D1b` · `E6b` failing** | **Batch 87 fixed all four** ⇒ a failure is a **REGRESSION**, the highest-value finding available |
| ⭐⭐⭐ **anything rendering as HEX** | `BP-01` was closed in Batch 83 |
| ⭐⭐ **any click that dead-ends** without saying why | `F3` |
| ⭐⭐ **Details showing a FORM instead of a TABLE** | inverts `U-6`'s whole design |
| ⭐⭐ **part H failing at `H1`/`H2`** | Batch 88b's entire deliverable |
| ⭐ **a LIVE value in Details** *(`C7`/`H9`)* | ⚠ **the opposite of a defect** — it would mean `BP-334` is wrong |
| ⛔ **`D2`–`D8`, `E2`–`E7`** | ⭐ **known-blocked. Record once, do not investigate** |
