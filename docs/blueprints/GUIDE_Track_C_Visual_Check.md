<!--STATUS
state: SUPERSEDED
updated: 2026-08-18
superseded-by: GUIDE_Blueprint_Visual_Check.md
current-answer: parts B (Inspector default value) and F (change highlighting) only
stale-below: parts C, D and E describe surfaces Batches 82-84 replaced. Do not run them.
-->
# GUIDE — Track C visual check, **step by step**

> ⭐ **Companion to [`CHECKLIST_Track_C_Visual_Verification.md`](CHECKLIST_Track_C_Visual_Verification.md)** — the checklist is *what*, this is *how*.
> ⭐ **Every step ends in a PASS/FAIL you can write down.** ⛔ **Nothing here asks you to judge whether a
> feature "seems right"** — each expectation is a ruling from the design, cited.
>
> ✅✅ **ALL PARTS ARE RUNNABLE — Batch 80 landed `2026-08-17`.** ⭐ **Run A → F in order.**

---

## 0. ✅ Status before you start — **measured `2026-08-17`, after Batch 80**

| part | surface | ready? |
|---|---|---|
| **A** | **My Blueprint sections** *(Blueprint perspective)* | ✅ **yes** |
| **B** | **Inspector → `DEFAULT VALUE — {var}`** | ✅ **yes** |
| **C** | **My Blueprint outline on BTree / HSM** | ✅ **yes** — ⭐ **unblocked by Batch 80** |
| **D** | **Variables table** | ✅ **yes** — ⭐ **routing works with no host setup** |
| **E** | **Watch → Pinned variables** | ✅ **yes** |
| **F** | **change highlighting** *(red / yellow)* | ✅ **yes** |

### 0a. ⭐ What Batch 80 changed — **read this, it moves two expectations**

⛔ **Before:** `PerspectiveWorkspaceRegistrar` built the outline only `if (hostKind != null)`, and
**`EditorSubsystem` passed `hostKind` to none of its three registrars** ⇒ in the running editor the
outline was never constructed and the table never routed. ⚠ **Every Batch-79 rail was green throughout**
— each built its own registrar and passed the argument production did not.

| ⭐ the fix, in three parts | what it means for YOU |
|---|---|
| ① the two call sites now pass `hostKind` | — |
| ⭐⭐ ② **the host kind is DERIVED from the perspective name** *(`"BTree"` / `"HSM"`, case-insensitive)*; the parameter survives as an override | ⛔ **there is no argument left to forget** |
| ⭐⭐ ③ the outline **follows the selection store**, and a **default section-source resolver** is installed | ⭐ **`C4` and `D3` need no setup** — switching assets and clicking a section just work |

⚠⚠ **And one thing Batch 80 caught that would have made `D3` look right and be wrong:**
📐 **`SectionVariableRowSource` does not filter** — it takes `_schema.Variables` **wholesale** and uses
the section only as a **label**. ⇒ ⛔ **routing through it would have shown the WHOLE blackboard under
EVERY heading.** ⭐ **`BlackboardSectionRowSource` is new for exactly this**, and it shares
`BlackboardMyBlueprintModel.SectionOf` with the outline ⇒ ⭐⭐ **`D3` is now a real check: a variable
cannot sit under one heading in the tree and another in the table.**

---

## ⚠⚠ PROVISIONAL — **two things will change; do NOT record them as pass criteria**

> ⭐⭐⭐ **User, `2026-08-17`:** *"i do not want to visually check something that will need to change."*
> ⭐ **Checked item by item.** ⛔ **The criterion is NOT "will the code change" — it is "will what you
> SEE change."** ⭐⭐ **Almost nothing does. The two that do:**

| ⚠ | what changes | ⭐ so check this instead |
|---|---|---|
| ⛔⛔ **`A2` / `A6` — the SECTION LIST** | 📌 **`R-01`/`R-03`: `Variable` ≡ `WorkingState`, and stage `D` collapses them into ONE section.** ⇒ **the count and names WILL change** | ⭐ **check the MECHANISM** — that sections split by kind at all, that `[+]` creates the right kind, that empty ones stay. ⛔ **Do not record *"four sections"* as a pass** |
| ⚠ **the SET of `Variables` windows** | 📌 **`R-10`/`R-11`: `U-16` RETIRES some of them** — the renames are an **interim you authorised**, not the end state | ⭐ **check they are DISTINGUISHABLE**, ⛔ **do not memorise which exist** |

⭐⭐ **Everything else is END-STATE and worth checking properly** — ⭐ **including `3a`'s row commands:
stage `B` moves that code onto `IVariablesSchemaSource`, ⛔ but rename/delete/duplicate must work on
those rows either way, so what you SEE is final.**

---

## A. My Blueprint — sections *(Blueprint perspective)* ✅

> 📄 Checklist **1.1–1.7** · design §1c *(sections are the classification)*

| # | do | expect | ✔ |
|---|---|---|---|
| **A1** | Open the editor, switch to the **Blueprint** perspective, open any blueprint asset | the **My Blueprint** panel is present | ☐ |
| **A2** | Read the section list top to bottom | ⭐ **Variables is SPLIT per kind** — ⛔ not one undifferentiated list. Order matches `SortOrder` | ☐ |
| **A3** | Find **`Local Variables`** | ⭐ present, and it is the **only graph-scoped** section | ☐ |
| **A4** | Switch to a different graph on the canvas | ⭐ **`Local Variables` follows the canvas** — its contents change | ☐ |
| **A5** | Find a section with no declarations | ⭐⭐ **it renders EMPTY, with its header — ⛔ it does NOT vanish** *(“a section that appears and disappears reads as a broken feature”)* | ☐ |
| **A6** | Click a section's **`[+]`** | a declaration **of that section's kind** is created — ⛔ not a generic “variable” | ☐ |
| **A7** | Open a **Macro** graph, click `[+]` on `Local Variables` | ⭐ it **refuses OUT LOUD** *(an indicator)* — ⛔ it does not silently do nothing | ☐ |
| **A8** | Look anywhere in the panel for a **Role** or **Scope** dropdown | ⛔⛔ **there is none, on any asset type.** The section IS the classification | ☐ |

---

## B. Inspector — the default-value panel ✅

> 📄 Checklist **1.8–1.12** · Batch 74

| # | do | expect | ✔ |
|---|---|---|---|
| **B1** | **BTree** or **HSM** perspective; select a node whose facet has an **`ExpressionTargetField`** *(a transition/action that writes a blackboard variable)* | a section appears in the **Inspector** | ☐ |
| **B2** | Read its title | ⭐ **`DEFAULT VALUE — {variable}`** — ⛔ **not** “STATIC PARAMETERS” | ☐ |
| **B3** | Read the subtitle | it names **`ExpressionTargetField`** | ☐ |
| **B4** | Hover the section | ⭐ tooltip: *“applied once at behavior assignment; bind a variable for live/dynamic values”* | ☐ |
| **B5** | Edit a field, commit, **close and reopen the asset** | ⭐ the value **persisted** *(it went to `DefaultValueJson`)* | ☐ |
| **B6** | Select a node with **no** `ExpressionTargetField` | the section is **absent** — it is contextual | ☐ |

---

## C. BTree / HSM outline ✅

> 📄 Checklist **2.36–2.39** · ⭐ **unblocked by Batch 80** — §0a

| # | do | expect | ✔ |
|---|---|---|---|
| **C1** | BTree perspective, open `CombatShowcase.btree.json` | a **My Blueprint** panel is present | ☐ |
| **C2** | Read the sections | ⭐ **`Inputs` → `Working State` → `Asset Globals`**, in that order | ☐ |
| **C3** | Repeat on HSM with `HsmShowcase.hsm.json` | the same three sections, **for the HSM asset** | ☐ |
| **C4** | Open an asset with an empty blackboard | ⭐ **all three sections still listed**, contents empty | ☐ |
| **C5** | `[+]` in a section | a declaration **of that kind** | ☐ |
| **C6** | Blueprint perspective | ⛔ **exactly ONE outline** — the blueprint's own. ⭐ **No second panel appeared** | ☐ |

---

## D. The Variables table ✅

> 📄 Checklist **2.1–2.9**, **2.10–2.17**, **2.24–2.30**
>
> ⚠⚠ **`D4`–`D8` need the SIM RUNNING.** ⭐ **At authoring time there is no entity**, so every row
> correctly reads **`(pending)`** — ⛔ **that is not a failure**, and deliberately **not**
> `<unreadable>`: *"a decode failure that never happened would send a designer hunting a bug in their
> type"* *(Batch 80)*. ⇒ **Do `D1`–`D3` and `D9`–`D14` cold; start the sim for `D4`–`D8`.**

| # | do | expect | ✔ |
|---|---|---|---|
| **D1** | Find the **Variables** window in the perspective's dock | present on **all three** perspectives | ☐ |
| **D2** | Count the columns | ⭐⭐ **`Name` · `Value` · `Type`** — ⛔ **`Bytes`, `Role`, `Scope` are GONE** *(seven → three)* | ☐ |
| **D3** | Click a section row in the outline | ⭐⭐ the table **re-filters to that section** — ⛔ **check it shows ONLY that section's variables**, not the whole blackboard *(§0a)* | ☐ |
| **D4** | Look at a **primitive** row | inline and formatted — `80`, `12.5`, `true` | ☐ |
| **D5** | Look at a **struct** row | ⭐ one-line elided summary `{X=1.0, Y=2.0, …}` — ⛔ **never raw hex** | ☐ |
| **D6** | Hover that struct cell | ⭐ **pretty-printed tooltip, one field per line** | ☐ |
| **D7** | Look at a **fixed list** row | `{Count=3: 1, 2, 3}`, elided | ☐ |
| **D8** | Find any undecodable row | ⭐ it says **`<unreadable>`** in words, with the reason in the tooltip | ☐ |
| **D9** | Resize the window narrow | ⭐ values stay **one line, elided** — ⛔ rows never grow or wrap | ☐ |
| **D10** | **Double-click a VALUE cell** | the **value** dialog opens, **scoped to that field** | ☐ |
| **D11** | **Double-click a NAME cell** | the **properties** dialog opens — the whole object | ☐ |
| **D12** | **Right-click a name cell** | ⭐ *Edit value…* and *Properties…* — ⛔ **no Rename; that is BY DESIGN**, rename lives in the outline | ☐ |
| **D13** | Press **F2** on an outline row | inline rename works, and references still update | ☐ |
| **D14** | Confirm the old surface still exists | ⭐⭐ **Blackboard Authoring still draws its own variables list.** ⛔ **Two surfaces coexisting is DELIBERATE** *(your ruling; the merge is `Q38`)* | ☐ |

---

## E. Watch — pinned variables ✅

> 📄 Checklist **2.31–2.35** · ⭐ **Batch 79 measured that a watch is THREE concepts** — the window keeps
> its breakpoint list and gained a **second, labelled** section

| # | do | expect | ✔ |
|---|---|---|---|
| **E1** | Open **Watch** | ⭐ **two sections**: the breakpoint list *(Name / Enabled / Hits)* **and** *“Pinned variables”* | ☐ |
| **E2** | Pin variables from **two different assets on two different entities** | all render together, grouped by **Asset then Entity** | ☐ |
| **E3** | Look at the columns | ⭐ **`Type` is HIDDEN here** *(monitoring)* — ⛔ but shown in the Variables table *(authoring)* | ☐ |
| **E4** | Pin a large struct *(≥136 bytes, e.g. `HillAttackSharedState`)* | ⭐ it renders — **the old 64-byte limit is gone** | ☐ |
| **E5** | Before the sim writes it once | ⭐ **`(pending)`** — ⛔ not a zero, not blank | ☐ |
| **E6** | Delete/close the asset a pinned row belongs to | ⭐⭐ the row **survives, GREYED**, showing its last value — ⛔ it does not vanish | ☐ |
| **E7** | Double-click that greyed row | ⭐ it **refuses** its dialog | ☐ |

---

## F. Change highlighting ✅ *(run after D)*

> 📄 Checklist **2.18–2.23** · design §4a — ⭐⭐ **the unit is a non-frozen ASSET tick, not a frame**

| # | do | expect | ✔ |
|---|---|---|---|
| **F1** | Run the sim; watch a variable the AI writes | 🔴 **red for one tick**, then clears | ☐ |
| **F2** | Edit a value yourself | 🟡 **yellow**, and ⭐ **visibly distinct from red** | ☐ |
| **F3** | ⭐⭐ **PAUSE with a row red** | ⭐⭐⭐ **the red PERSISTS** — ⛔ it must NOT clear while paused *(this is the whole reason the unit is an asset tick)* | ☐ |
| **F4** | Press **Step** once | the highlight advances **one** tick | ☐ |
| **F5** | Run one asset on **two entities**, change it on one | ⭐ only **that** row reddens — they are independent | ☐ |
| **F6** | Collapse a group containing a red row | ⭐⭐ **the collapsed header shows red** — you can fold everything and still see where activity is | ☐ |
| **F7** | Look at **BTree / HSM** rows | ⚠ **inert — never highlighted.** ⭐ **Expected, not a defect**: no per-asset tick counter on those hosts yet | ☐ |

---

## G. ⛔ DO NOT look for these — **measured NOT BUILT**

> ⭐ **Settled from code in Batch 79 so you do not hunt for them.**

| | |
|---|---|
| ⛔ **`GroupBy` / fold / `Type`-toggle persistence** | **NOT BUILT.** `GroupBy` is a plain property, fold is ImGui's own `imgui.ini` state, and `ShowType` is **ctor-time with no toggle UI at all**. ⚠⚠ **Its doc comment claims otherwise — the comment is wrong** |
| ⛔ **a `Type` column toggle** | there is no control; the column is fixed per surface *(shown in Details, hidden in Watch)* |
| ⛔ **the budget indicator reacting to run state** | **NOT BUILT** — the only budget UI is Blackboard Authoring's, and it has **no run-state input at all**, so it draws mid-run too |
| ⛔ **Rename in the table's row menu** | **absent by design** — a row is an observation with no asset handle. Rename is in the **outline** |

---

## H. Recording the result

⭐ **For each failed step, note: the step id · what you saw · which asset and perspective.**
⭐⭐ **A step that cannot be reached is a FAIL, not a skip** — ⛔ *"built but unreachable" is the exact
defect this programme has now hit five times.*
📌 **Send me the failures and I will triage them into a batch.**
