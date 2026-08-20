<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - rewritten for Batches 89 and 90, plus row A9 (Batch 91).
stale-below: nothing. The 2026-08-18 edition's "part D is blocked" framing and its
  "expect (pending)" rows are BOTH GONE - do not quote them.
known-rot: none known. Every "not built" claim names the command that measured it on
  2026-08-19; re-measure rather than trusting the sentence.
note: GUIDE_Track_C_Visual_Check.md parts B (Inspector default value) and F (change
  highlighting) still stand and are not repeated here.
-->

# GUIDE — **the visual check**, post-Batch-91

> ⭐⭐⭐ **THE TWO THINGS THAT BLOCKED THE LAST EDITION ARE FIXED.**
> ✅ **The edit dialog reaches the designer** *(Batch 89 — part `D` is fully runnable)*.
> ✅ **The Details Value column is LIVE on all three hosts** *(Batch 90 — `C7` and `H9` INVERTED)*.
> ⭐ **The suspension is lifted on Blueprint, BTree and HSM** *(`M-21`)*.
>
> ⭐ **Every row ends in a PASS/FAIL you can write down.** ⛔ **Nothing asks you to judge whether
> something "seems right"** — each expectation cites the ruling it comes from.
> ⏱ **Budget: ~45 minutes.** ⭐ **Do the parts in order.**

---

## 0. ⭐⭐⭐ WHAT CHANGED SINCE THE LAST EDITION — **do not report these from the old one**

| row | was | ⭐ now |
|---|---|---|
| ⭐⭐ **part `D`** | *"expect `D2` onward to FAIL — nothing draws the modal"* | ✅ **FULLY RUNNABLE.** Batch 89 put the modal in the frame *(`WindowManager.RegisterFrameOverlay`)*; `BP-327` closed against its ORIGINAL criterion |
| ⭐⭐⭐ **`C7` · `H9`** | *"expect `(pending)` — a live value would be a surprise"* | ✅✅ **INVERTED — a LIVE value is the PASS condition**, on all three hosts *(`BP-334` closed)* |
| **`C2`** | a detour through `InspectorWindow` | ⭐ **the dialog works now** — check both |
| **`F1`** | blocked by `D` | ⭐ **runnable** |
| ⭐ **`D1`** | *"click the `⋮`"* | ⛔ **still no `⋮` button — RIGHT-CLICK.** ⚠ Ruling 5 wants *"a three-dot button AND double-click"*; **the button half is still unbuilt** |
| ⛔ **`E2`–`E7`** | *"pin a variable…"* | ⛔ **still SKIP — watch pinning is not built** |

## 0a. ⛔ NOT BUILT — **a failure here is EXPECTED. Record it, do not diagnose it**

| ⛔ | 📐 measured `2026-08-19` |
|---|---|
| **watch PINNING** | `PinnedVariableRowSource` exists and `AiWatchWindow.Pinned` exposes it — ⭐ **nothing in production ADDS to it** ⇒ `E2`–`E7` unrunnable |
| **the `⋮` three-dot button** | `VariableTableControl.DrawRowMenu` opens on **`BeginPopupContextItem()`** — right-click only |
| **`GroupBy` / fold persistence / the `Type` toggle persisting** | not built *(settled Batch 79)* |

## 0b. ⭐⭐ WHERE `(pending)` IS STILL CORRECT — **read this before reporting one**

⭐⭐⭐ **A live Value column does NOT mean every cell shows a number.** `(pending)` is the right answer
whenever there is nothing to show:

| `(pending)` is CORRECT when | |
|---|---|
| the sim is **not running** | ⭐ the column shows the **initial** value instead *(ruling 3)* |
| **no entity is selected**, or no live session | ⭐ honest emptiness — ⛔ never a zero that looks like a value |
| ⭐⭐ **the run has not written that variable yet** | 📌 **row `C9`, and `BP-338` now enforces it per name, per frame.** ⛔⛔ **A ZERO here is a REGRESSION** — it was the exact defect Batch 90 had to fix for the feature to be a fix |

⚠ **Blueprint's change HIGHLIGHT does not light.** ⭐ Its live values arrive as decoded objects, and
the highlight diffs **bytes** ⇒ **inert by design**, not a bug. ⭐ **BTree and HSM highlight normally.**

## 0c. ⭐⭐ REGRESSION ROWS — **the highest-value findings in this guide**

⭐ **`B3` · `B8` · `D1b` · `E6b`** were Batch 87's fixes and **no human has seen them since.**
⇒ ⛔ **a failure in any of them is a REGRESSION, and worth more than everything else here.**

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

### ⭐⭐ A9 — **NEW, Batch 91.** The one designer-visible surface of the alias fix

| # | do | expect | ✔ |
|---|---|---|---|
| ⭐⭐⭐ **A9** | On a **BTree** or **HSM** asset with a sub-tree: drag an **Unbound Sub-Tree Requirement** onto a matching **Defined Variable** → the `↳ aliased by:` badge appears. **SAVE · CLOSE the asset · REOPEN it** | ⭐⭐⭐ **the alias and its badge are STILL THERE.** ⛔⛔ **Before Batch 91 they were always gone** — 📌 `BP-339`, and the loss was **silent** | ☐ |
| ⭐ **A9b** | Delete the sub-asset the alias points at, then reopen | ⭐ the alias **prunes** — ⛔ no dangling badge | ☐ |

---

## B. ⭐⭐ Details hosts the TABLE

> 📌 **`Q32` ruling 1:** *"Details hosts the list of vars."* · ⛔ *"The table is never replaced by a
> single-variable form."*

| # | do | expect | ✔ |
|---|---|---|---|
| **B1** | Click a variable row in **My Blueprint** | ⭐⭐⭐ **a TABLE of variables** — ⛔⛔ **NOT a property form** | ☐ |
| **B2** | Look at which rows are in it | ⭐ **every variable of that row's SECTION** | ☐ |
| ⭐⭐ **B3** | **REGRESSION** — find the row you clicked | ⭐ it is **highlighted** inside the table *(Batch 87)* | ☐ |
| **B4** | Read the column headers | ⭐ **`Name` · `Value` · `Type`** — ⛔⛔ **no `Bytes`, `Role` or `Scope`** | ☐ |
| **B5** | Click a **`Local Variables`** row | the **CURRENT graph's** locals; the heading **names the graph** | ☐ |
| **B6** | Switch graph while showing locals | the table **follows** | ☐ |
| **B7** | Click a **Graph** / **Function** / **Macro** node in the outline | ⭐⭐ **the table LETS GO** — ⛔ no stale list beside an unrelated selection | ☐ |
| ⭐⭐ **B8** | **REGRESSION** — click a **canvas node**, then a variable, then **the node again** | ⭐ **the panel switches arms EVERY time, both directions** *(`R-95` — the panel obeys the focused SURFACE)* | ☐ |

---

## C. ⭐⭐⭐ The Value column — **this is the part that changed most**

> 📌 **ruling 3:** *"ONE Value column, meaning switched by run state."* · **ruling 4:** *"Value is
> READ-ONLY in the cell."*

**Sim NOT running:**

| # | do | expect | ✔ |
|---|---|---|---|
| **C1** | Count the value columns | ⭐⭐ **exactly ONE** | ☐ |
| **C2** | Right-click a variable → **Edit value…**, set a default, OK. Then read the cell | ⭐ **the initial value you just set.** ⚠ **Cross-check** the same variable in `InspectorWindow`'s `DEFAULT VALUE — {var}` panel — ⭐ **both must agree** *(one write path)* | ☐ |
| **C3** | Read one with **no** declared default | the type's **zero value** — ⛔ not blank, not `--` | ☐ |
| **C4** | Try to type into a value cell | ⛔ **you cannot** *(ruling 4)* | ☐ |
| **C5** | Hover a **struct** value | ⭐⭐ tooltip **pretty-printed, multi-line, one field per line** | ☐ |
| **C6** | Look at any row's value width | ⭐ **one line, never wrapping** | ☐ |

**Sim RUNNING** *(load a scenario, let it tick)* — ⭐⭐⭐ **the inverted rows:**

| # | do | expect | ✔ |
|---|---|---|---|
| ⭐⭐⭐ **C7** | ✅ **INVERTED.** Select an entity, watch a variable in the **Details** table | ⭐⭐⭐ **a LIVE value that CHANGES as the sim runs.** ⛔⛔ **`(pending)` on a variable the run IS writing is now a FINDING** *(`BP-334` closed, Batch 90)* | ☐ |
| ⭐ **C7b** | Compare the same variable in the standalone **Blackboard Variables** window | ⭐ **the same value, the same formatting** — ⛔ two notations for one value is a finding | ☐ |
| ⭐⭐ **C7c** | Find a variable the run has **not** written | ⭐⭐ **`(pending)`** — ⛔⛔ **a ZERO here is a REGRESSION** *(`BP-338`)* | ☐ |
| ⭐ **C7d** | Deselect the entity / stop the sim | ⭐ back to **initial values** or `(pending)` — ⛔ **never a stale live value left behind** | ☐ |
| **C8** | Look for raw hex, anywhere | ⛔⛔ **NEVER raw hex** *(`BP-01`)* — a hex string is a **regression** | ☐ |
| **C9** | Look for `<unreadable>` | ⚠ **only** where a value genuinely cannot be decoded — ⛔ **not where `(pending)` belongs** | ☐ |
| ⚠ **C10** | On **BTree/HSM**, watch a changing value | ⭐ **the change HIGHLIGHT lights.** ⚠ **On Blueprint it does NOT — that is by design** *(objects carry no bytes to diff)*, ⛔ not a finding | ☐ |

---

## D. ⭐⭐⭐ The edit dialog — ✅ **NOW RUNNABLE** *(Batch 89)*

> ⭐⭐ **The modal is drawn in the FINAL frame slot**, beside the file dialog. ⇒ ⚠ **two consequences
> that are DESIGNED, not defects:** it **stays up across a perspective switch**, and it does **not**
> close when the panel you opened it from is closed. ⛔ **Closing with its host window was the defect
> Batch 89 fixed.**
>
> 📌 **ruling 5:** a three-dot button *"opens a StructEdit-based editing window, OK / Cancel,
> initialised to the variable's current value."* · **design §3–§4:** ⭐ **ONE dialog, TWO scopes.**

**Sim NOT running:**

| # | do | expect | ✔ |
|---|---|---|---|
| ⭐ **D1** | ⛔ **there is no `⋮`.** **RIGHT-CLICK** a table row | a menu with **"Edit value…"** and **"Properties…"** | ☐ |
| ⭐⭐ **D1b** | **REGRESSION** — right-click a row in **each** of: Details · the standalone Variables window · the Watch | ⭐⭐ **the menu appears in ALL of them** *(Batch 87's one attach point)* | ☐ |
| ⭐⭐⭐ **D2** | **Double-click the VALUE cell** | ⭐⭐ **the dialog OPENS**, with **OK** and **Cancel**, initialised to the current value | ☐ |
| **D3** | **Double-click the NAME cell** | the **Properties** dialog — ⭐ the full attribute set | ☐ |
| **D4** | In Properties, find **Type** | ⭐⭐ **a type PICKER**, offering **structs** as well as primitives *(`S5`)* | ☐ |
| **D5** | Change a **Type**, OK, reopen | ⭐ **it stuck** | ☐ |
| **D6** | Change a **value**, OK, reopen | ⭐ **it stuck** — this wrote `DefaultValueJson` | ☐ |
| **D7** | Change something, press **Cancel**, reopen | ⛔ **nothing changed** | ☐ |
| **D8** | Look in Properties for **Role** / **Scope** | ⛔⛔ **absent** | ☐ |
| **D9** | Press **F2** on a row | ⭐ inline rename, and ⭐ **the rename propagates** | ☐ |
| **D10** | Rename via **Properties → Name** instead | ⭐ **same result** — ⛔ two routes, one mechanism | ☐ |
| **D11** | Do **D1**–**D3** from a **My Blueprint** row | ⭐⭐ **identical gestures, identical dialogs** *(design §4)* | ☐ |
| ⭐ **D12** | Open the dialog, then **switch perspective** | ⭐ **it stays up** — ⛔ **by design, not a leak** | ☐ |
| ⭐ **D13** | Open the dialog on **BTree**, then on **HSM** | ⭐ **each host's dialog is its own** *(`BP-336`: per-instance ids)* — ⛔ they must not fight | ☐ |

---

## E. ⭐⭐ The Watch panel

| # | do | expect | ✔ |
|---|---|---|---|
| **E1** | Open **Watch** with the sim **NOT running** | ⭐⭐ **it shows NOTHING** — ⛔ not stale rows, not `--` | ☐ |
| ⛔ **E2**–**E7** | ⛔⛔ **SKIP — watch pinning is not built** *(§0a)*. ⭐ **Do not record failures here** | — | — |
| ⭐ **E6b** | **REGRESSION** — right-click a row in **each** watch window | ⭐⭐ **both offer the menu**, ⭐ **and the dialog now OPENS** *(`BP-330` + Batch 89)* | ☐ |

---

## F. ⭐ Refusals — **the part most likely to be wrong**

> 📌 **User ruling, `2026-08-17`, verbatim:** *"disabling/graying a `[+]` … but showing explanatory
> tooltip would be better than allowing user to click the button and then saying that it is not
> possible — same information value, no false expectations."*

| # | do | expect | ✔ |
|---|---|---|---|
| ⭐ **F1** | Sim **RUNNING/PAUSED**, open **Properties…** | ⭐ **read-only** — ⛔ you cannot retype a variable mid-run | ☐ |
| **F2** | Sim **RUNNING/PAUSED**, try **Edit value…** | ⭐ **Paused ⇒ writes the live blackboard · Running/Replay ⇒ REFUSES.** ⚠ **Record HOW it refuses** | ☐ |
| **F3** | For every refusal | ⭐⭐ **greyed + a tooltip saying WHY** — ⛔⛔ **a click that dead-ends is a FINDING** | ☐ |
| **F4** | In **replay** | ⛔ everything read-only | ☐ |

---

## H. ⭐⭐ The Details panel on BTree and HSM *(Batch 88b)*

> ⭐⭐ **`AiDetailsWindow`**, titled **`Details`**, ids `ai_details_btree` / `ai_details_hsm`.

| # | do | expect | ✔ |
|---|---|---|---|
| **H1** | **BTree** perspective, open a `.btree` asset | ⭐⭐ **a window titled `Details` exists** | ☐ |
| **H2** | Repeat on **HSM** | ⭐ **the same** | ☐ |
| **H3** | Read the **My Blueprint** section list here | ⭐⭐ **`Inputs` · `Working State` · `Asset Globals`.** ⛔⛔ **A `Working State` section here is CORRECT** — ⚠ **the opposite of `A2`** | ☐ |
| **H4** | Click a variable row in the outline | ⭐⭐ **Details shows the TABLE of that section** — ⛔ not a form | ☐ |
| **H5** | Find the row you clicked | ⭐ **highlighted** | ☐ |
| **H6** | Click a section, then something with no variables | ⭐ **the panel CLEARS** | ☐ |
| **H7** | With nothing selected | ⭐ **`"No variable selected."`** — ⛔ not a blank window | ☐ |
| **H8** | Right-click a row | ⭐ **the menu appears, and the dialog opens** | ☐ |
| ⭐⭐⭐ **H9** | ✅ **INVERTED.** Sim running, entity selected — read the Value column | ⭐⭐⭐ **LIVE values that change** — ⛔ **`(pending)` on a written variable is now a FINDING** | ☐ |
| ⭐ **H10** | Watch a changing value here | ⭐ **the change highlight LIGHTS** *(BTree/HSM supply bytes)* | ☐ |
| ⛔ **H11** | Click a **canvas node** here | ⚠ **Details does NOT switch to a node view** — ⭐ **by design**: one arm; the AI node surface is `InspectorWindow`. ⛔ **Not a finding** | ☐ |

---

## G. ⭐ How to report

⭐ **Per row: the id, PASS or FAIL, and — if FAIL — what you saw.** ⛔ **Do not diagnose**; a screenshot
or one sentence is enough.

| ⭐ especially worth writing down | why |
|---|---|
| ⭐⭐⭐ **`B3` · `B8` · `D1b` · `E6b` failing** | **Batch 87 fixed all four** ⇒ a failure is a **REGRESSION** |
| ⭐⭐⭐ **`(pending)` on a variable the run IS writing** *(`C7`/`H9`)* | ⛔ **Batch 90's whole deliverable** |
| ⭐⭐⭐ **a ZERO where `(pending)` belongs** *(`C7c`)* | ⛔ **`BP-338` — the regression that nearly shipped with the feature** |
| ⭐⭐⭐ **anything rendering as HEX** | `BP-01` |
| ⭐⭐ **the dialog not opening** *(`D2`)* | ⛔ Batch 89's whole deliverable |
| ⭐⭐ **any click that dead-ends** without saying why | `F3` |
| ⭐⭐ **Details showing a FORM instead of a TABLE** | inverts `U-6`'s design |
| ⛔ **`E2`–`E7`** | ⭐ **known-blocked. Record once, do not investigate** |
| ⛔ **Blueprint's highlight not lighting** *(`C10`)* | ⭐ **by design** — objects carry no bytes to diff |
