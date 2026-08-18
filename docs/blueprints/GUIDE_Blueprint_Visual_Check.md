<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: this whole file
note: covers the surfaces Batches 82 and 83 built. The older GUIDE_Track_C_Visual_Check.md
  parts B (Inspector default value) and F (change highlighting) still stand and are not
  repeated here; its parts C/D/E are superseded by parts B-F below.
-->

# GUIDE — **Blueprint visual check**, post-Batch-83

> ⭐⭐⭐ **THE SUSPENSION IS LIFTED — for Blueprint.** 📌 **`R-21`** *(user, `2026-08-14`)* held every
> visual check until *"the Details panel is implemented and the emitters and all access infrastructure
> are unified."* ⇒ ✅ **Details panel — Batch 82** · ✅ **emitters + access — Batch 56 and stage `C`.**
> ⛔⛔ **STILL SUSPENDED for BTree and HSM** — 📌 **`R-60`: they have no Details window at all**
> *(`BP-317`, sequencing row 61)*. ⚠ **Do not check them and do not record failures there.**
>
> ⭐ **Every row below ends in a PASS/FAIL you can write down.** ⛔ **Nothing asks you to judge whether
> something "seems right"** — each expectation cites the ruling it comes from.
> ⏱ **Budget: ~30 minutes.** ⭐ **Do the parts in order** — each uses what the previous one opened.

---

## 0. ⛔⛔ TWO THINGS THAT WILL CHANGE — **do NOT record them as pass criteria**

| ⚠ | what will change | ⭐ check this instead |
|---|---|---|
| ⛔⛔ **the SECTION LIST** *(`Variables` vs `Working State`)* | 📌 **`R-61`: stage `D` collapses them into ONE.** They are **the same thing** — identical `(Role, Scope)`; only `Dispatch` differs | ⭐ **check the MECHANISM**: that sections exist, that `[+]` creates the right kind, that empty ones stay. ⛔ **Never record *"four sections"* as a pass** |
| ⚠ **the SET of `Variables` windows** | 📌 **`R-10`/`R-11`: `U-16` retires some** *(row 60)*. The current names are an interim | ⭐ **check they are DISTINGUISHABLE.** ⛔ **Do not memorise which exist** |

## 0a. ⛔ NOT BUILT YET — **a failure here is expected, not a finding**

| ⛔ | owner |
|---|---|
| **editing a value while the sim is RUNNING or PAUSED** | **row `59c`** — Batch 84. ⭐ **It must REFUSE, visibly** *(part G)* |
| **a Details panel on BTree / HSM** | **`BP-317`**, row 61 |
| **`GroupBy` / fold persistence, the `Type` toggle persisting** | 🔴 **`2.7`** — not built *(settled Batch 79)* |

---

## A. My Blueprint — the outline ✅ *(Batch 80/82)*

| # | do | expect | ✔ |
|---|---|---|---|
| **A1** | Blueprint perspective, open any blueprint asset | the **My Blueprint** panel is present, populated | ☐ |
| **A2** | Read the section list | sections are split **by kind** — ⛔ not one undifferentiated list | ☐ |
| **A3** | Find **`Local Variables`** | present, and the **only graph-scoped** section | ☐ |
| **A4** | Switch graph on the canvas | ⭐ **`Local Variables` follows the canvas** — contents change | ☐ |
| **A5** | Find a section with no declarations | ⭐⭐ **renders EMPTY with its header — ⛔ does not vanish** | ☐ |
| **A6** | Click any section's **`[+]`** | ⭐ **the same new-variable dialog opens for EVERY section** — 📌 **`R-17`** *(user, `2026-08-17`: "must open new variable dialog same as any other variable section")* | ☐ |
| **A7** | Open a **Macro** graph, click `[+]` on `Local Variables` | ⭐ **refuses OUT LOUD** — ⛔ not a silent no-op | ☐ |
| **A8** | Look for a **Role** or **Scope** dropdown, anywhere | ⛔⛔ **there is none.** 📌 The SECTION is the classification *(`2026-08-16` ruling)* | ☐ |

---

## B. ⭐⭐ Details hosts the TABLE ✅ *(Batch 82 — `U-6`)*

> 📌 **`Q32` ruling 1:** *"Details hosts the list of vars."* · 📌 **design §1:** ⛔ *"The table is never
> replaced by a single-variable form."*

| # | do | expect | ✔ |
|---|---|---|---|
| **B1** | Click a variable row in **My Blueprint** | ⭐⭐⭐ **the Details panel shows a TABLE of variables** — ⛔⛔ **NOT a property form for the one you clicked** | ☐ |
| **B2** | Look at which rows are in it | ⭐ **every variable of that row's SECTION** — 📌 *"selection yields a SECTION, not a variable"* | ☐ |
| **B3** | Find the row you clicked | ⭐ it is **highlighted** inside the table | ☐ |
| **B4** | Read the column headers | ⭐ **`Name` · `Value` · `Type`** — ⛔⛔ **no `Bytes`, no `Role`, no `Scope`** *(design §1: "Bytes, Role and Scope go")* | ☐ |
| **B5** | Click a **`Local Variables`** row | ⭐ the table shows **the CURRENT graph's locals**, and the heading **names the graph** | ☐ |
| **B6** | Switch graph on the canvas while showing locals | ⭐ the table **follows** | ☐ |
| **B7** | Click a **Graph** / **Function** / **Macro** node in the outline | ⭐⭐ **the table LETS GO** — ⛔ **it must not leave a stale variable list beside an unrelated selection** | ☐ |
| **B8** | Click a **node on the canvas**, then a variable, then the node again | ⭐ **last selection wins BOTH ways** — the panel switches arms each time | ☐ |

---

## C. ⭐⭐ The Value column ✅ *(Batch 83 — row 58)*

> 📌 **ruling 3:** *"ONE Value column, meaning switched by run state — **initial** when not running,
> **current** when running or paused."* · 📌 **ruling 4:** *"Value is READ-ONLY in the cell."*

**With the sim NOT running:**

| # | do | expect | ✔ |
|---|---|---|---|
| **C1** | Count the value columns | ⭐⭐ **exactly ONE** — ⛔ not an *Initial* column beside a *Current* one | ☐ |
| **C2** | Read a variable that has a declared default | its **initial value** | ☐ |
| **C3** | Read one with **no** declared default | ⭐ the type's **zero value** *(`0`, `false`, …)* — ⛔ **not blank, not `--`** | ☐ |
| **C4** | Try to type into a value cell | ⛔ **you cannot.** The cell is read-only *(ruling 4)* | ☐ |
| **C5** | Hover a **struct** value | ⭐⭐ tooltip is **pretty-printed, multi-line, one field per line** | ☐ |
| **C6** | Look at any row's value width | ⭐ **one line, never wrapping, never growing the row** *(design §4b)* | ☐ |

**With the sim RUNNING** *(load a scenario, let it tick)*:

| # | do | expect | ✔ |
|---|---|---|---|
| **C7** | Watch the same variable | ⭐⭐ **the SAME column now shows the CURRENT value**, and it changes as the sim runs | ☐ |
| **C8** | Look for raw hex, anywhere | ⛔⛔ **NEVER raw hex** — 📌 **`BP-01`, closed Batch 83**. A hex string is a **regression**, report it loudly | ☐ |
| **C9** | Find a variable declared but never written by the run | ⭐ **`(pending)`** — ⛔ **not `<unreadable>`**, which claims a decode failure that did not happen | ☐ |

> 🟡 **KNOWN, already filed — do not re-report.** A **struct** renders in two notations: `{"X":1.0,"Y":2.0}`
> when not running, `{X=1.0, Y=2.0, …}` when running. ⭐ **Ruling 3 switches the column's MEANING, not
> its NOTATION.** ⚠ **Cosmetic; queued for the next batch.**

---

## D. ⭐⭐ The edit dialog ✅ *(Batch 83 — row 59)*

> 📌 **ruling 5:** a three-dot button *"opens a StructEdit-based editing window, OK / Cancel,
> initialised to the variable's current value."* · 📌 **design §3–§4:** ⭐ **ONE dialog, TWO scopes, and
> the USER picks which.**

**Sim NOT running:**

| # | do | expect | ✔ |
|---|---|---|---|
| **D1** | Click the **`⋮`** right of a value | a menu with ⭐ **"Edit value…"** and ⭐ **"Properties…"** | ☐ |
| **D2** | **Double-click the VALUE cell** | the **value** dialog — ⭐ same as "Edit value…" | ☐ |
| **D3** | **Double-click the NAME cell** | the **Properties** dialog — ⭐ the full attribute set | ☐ |
| **D4** | In Properties, find **Type** | ⭐⭐ **a type PICKER**, and it offers **structs** as well as primitives *(`S5` made it one list)* | ☐ |
| **D5** | Change a **Type**, press OK, reopen | ⭐ **it stuck** | ☐ |
| **D6** | Change a **value**, press OK, reopen | ⭐ **it stuck** — this wrote `DefaultValueJson` | ☐ |
| **D7** | Change something, press **Cancel**, reopen | ⛔ **nothing changed** | ☐ |
| **D8** | Look in Properties for **Role** / **Scope** | ⛔⛔ **absent.** Not a property on any host | ☐ |
| **D9** | Press **F2** on a row | ⭐ inline rename, and ⭐ **the rename propagates** *(the refactor service ran)* | ☐ |
| **D10** | Rename via **Properties → Name** instead | ⭐ **same result** — ⛔ two routes, one mechanism | ☐ |
| **D11** | Do **D1–D3** from a **My Blueprint** row instead of a table row | ⭐⭐ **identical gestures, identical dialogs** *(design §4: "identical on every surface")* | ☐ |

---

## E. ⭐⭐ The Watch panel ✅ *(Batch 83 — row 59b, `BP-01` closed)*

| # | do | expect | ✔ |
|---|---|---|---|
| **E1** | Open **Watch** with the sim **NOT running** | ⭐⭐ **it shows NOTHING** *(row 59b: "show nothing before the run")* — ⛔ not stale rows, not `--` | ☐ |
| **E2** | Start the sim, pin a variable | the row appears with a **decoded value** | ☐ |
| **E3** | Compare that row against the same variable in **Details** | ⭐⭐ **same value, same formatting** — ⛔ they read one formatter now | ☐ |
| **E4** | Look for raw hex | ⛔⛔ **`BP-01`. None** | ☐ |
| **E5** | Pin variables from **two different assets** | ⭐ both appear, mixed, each with its own identity | ☐ |
| **E6** | Use `⋮` / double-click on a Watch row | ⭐⭐ **the SAME dialog as Details** *(ruling 11 — "SHARE it")* | ☐ |
| **E7** | Close an asset that has a pinned row | ⭐ the row goes **stale**: last value, **greyed** | ☐ |

---

## F. ⭐ Refusals — **the part most likely to be wrong**

> 📌 **User ruling, `2026-08-17`, verbatim:** *"disabling/graying a `[+]` … but showing explanatory
> tooltip would be better than allowing user to click the button and then saying that it is not
> possible — **same information value, no false expectations**."*

| # | do | expect | ✔ |
|---|---|---|---|
| **F1** | With the sim **RUNNING or PAUSED**, open **Properties…** | ⭐ **read-only** — ⛔ you cannot retype a variable mid-run | ☐ |
| **F2** | With the sim **RUNNING or PAUSED**, try **Edit value…** | ⛔ **it REFUSES** — ⭐ **that is row `59c`, Batch 84, not a defect.** ⚠ **Record HOW it refuses** | ☐ |
| **F3** | For every refusal above | ⭐⭐ **greyed + a tooltip that says WHY** — ⛔⛔ **a click that dead-ends is a FINDING** | ☐ |
| **F4** | In **replay** | ⛔ everything read-only | ☐ |

---

## G. ⭐ How to report

⭐ **Per row: the id, PASS or FAIL, and — if FAIL — what you saw.** ⛔ **Do not diagnose**; a screenshot
or one sentence is enough.

| ⭐ especially worth writing down | why |
|---|---|
| ⭐⭐⭐ **anything that renders as HEX** | `BP-01` was closed in Batch 83 — a hex string is a **regression**, not an old bug |
| ⭐⭐ **any click that dead-ends** without saying why | `F3` — the user ruling above |
| ⭐⭐ **the Details panel showing a FORM instead of a TABLE** | ⛔ that inverts `U-6`'s whole design |
| ⭐ **a stale list left beside an unrelated selection** | `B7` |
| ⛔ **BTree / HSM anything** | ⭐ **out of scope — `R-60`.** Not a finding |
