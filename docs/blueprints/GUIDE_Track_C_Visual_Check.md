# GUIDE — Track C visual check, **step by step**

> ⭐ **Companion to [`CHECKLIST_Track_C_Visual_Verification.md`](CHECKLIST_Track_C_Visual_Verification.md)** — the checklist is *what*, this is *how*.
> ⭐ **Every step ends in a PASS/FAIL you can write down.** ⛔ **Nothing here asks you to judge whether a
> feature "seems right"** — each expectation is a ruling from the design, cited.
>
> 🔴🔴 **READ §0 FIRST — one surface is still not constructed in production**, so part **C** is blocked
> until a two-line fix lands. ⭐ **Everything else is ready now.**

---

## 0. 🔴 Status before you start — **measured `2026-08-17`, after Batch 79**

| part | surface | ready? |
|---|---|---|
| **A** | **My Blueprint sections** *(Blueprint perspective)* | ✅ **yes** |
| **B** | **Inspector → `DEFAULT VALUE — {var}`** | ✅ **yes** |
| **C** | 🔴 **My Blueprint outline on BTree / HSM** | ⛔⛔ **BLOCKED** — §0a |
| **D** | **Variables table** | ⚠ **window exists; its section routing is wired inside the same blocked branch** — §0a |
| **E** | **Watch → Pinned variables** | ✅ **yes** |
| **F** | **change highlighting** *(red / yellow)* | ⚠ **after D** |

### 0a. ⛔⛔ Why C and D are blocked — **the fifth instance of one pattern**

📐 **`PerspectiveWorkspaceRegistrar` takes `BlackboardHostKind? hostKind = null`, and builds the outline
— and the outline→table routing — only `if (hostKind != null)`.**
🔴 **`EditorSubsystem` constructs all three registrars and passes `hostKind` to none of them.** ⭐ The
only caller that passes it is `TrackCWiringTests`.

⇒ ⛔ **In the running editor `MyBlueprint` is `null`, so the outline is never registered and the
Variables window is never routed.** ⚠ **The registrar is ready; the composition root does not activate
it.**

⭐⭐ **This is exactly the `2026-08-16` rule in `.claude/CLAUDE.md`** — *"a production caller that HAS a
dependency must PASS it"* — ⚠ **and `EditorSubsystem` has it: it is constructing the registrars named
`"BTree"` and `"HSM"` two lines apart.**
⇒ **Fix = pass `hostKind` at the two call sites, plus a rail asserting the PRODUCTION composition root.**
📌 **That is Batch 80, and it is small.**

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

## C. 🔴 BTree / HSM outline — ⛔ **BLOCKED, do not run yet**

> 📄 Checklist **2.36–2.39** · §0a above

| # | do | expect | ✔ |
|---|---|---|---|
| **C1** | BTree perspective, open `CombatShowcase.btree.json` | a **My Blueprint** panel is present | ☐ |
| **C2** | Read the sections | ⭐ **`Inputs` → `Working State` → `Asset Globals`**, in that order | ☐ |
| **C3** | Repeat on HSM with `HsmShowcase.hsm.json` | the same three sections, **for the HSM asset** | ☐ |
| **C4** | Open an asset with an empty blackboard | ⭐ **all three sections still listed**, contents empty | ☐ |
| **C5** | `[+]` in a section | a declaration **of that kind** | ☐ |
| **C6** | Blueprint perspective | ⛔ **exactly ONE outline** — the blueprint's own. ⭐ **No second panel appeared** | ☐ |

---

## D. The Variables table ⚠ *(window exists; routing blocked with C)*

> 📄 Checklist **2.1–2.9**, **2.10–2.17**, **2.24–2.30**

| # | do | expect | ✔ |
|---|---|---|---|
| **D1** | Find the **Variables** window in the perspective's dock | present on **all three** perspectives | ☐ |
| **D2** | Count the columns | ⭐⭐ **`Name` · `Value` · `Type`** — ⛔ **`Bytes`, `Role`, `Scope` are GONE** *(seven → three)* | ☐ |
| **D3** | Click a section row in the outline | ⭐ the table **re-filters to that section**, row highlighted *(⚠ blocked with C)* | ☐ |
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

## F. Change highlighting ⚠ *(after D)*

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
