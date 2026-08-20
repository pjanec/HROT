# HANDOFF — Batch 79: **WIRE TRACK C** — five built surfaces that no window hosts

> 📌 **Dispatched at `4d153709f`.** Frozen per rule 1.
> ⭐⭐ **Rule 1b: push `chore: started batch 79 at <sha>` before writing any code.**
> ✅ **Batch 78 MERGED at `e8c2c1535`.**
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
>
> ⭐⭐⭐ **RULE 8 IS REWRITTEN — read §5 before you run anything.** ⛔ **I no longer re-run your gates.**
> ⭐ **Your report substitutes for my run**, and §5 is the contract that makes that safe.

---

## 0. ⭐⭐⭐ The finding this batch exists for — **and it is mine**

📐 **Measured while preparing the user's verification checklist:** ⭐⭐ **everything Track C planned WAS
built.** ⛔⛔ **FIVE of seven deliverables are hosted by NOTHING.**

| deliverable | built | reachable | the measurement |
|---|---|---|---|
| `C-sections` | ✅ | ✅ | `BlueprintMyBlueprintWindow:326` constructs the panel |
| Inspector `DEFAULT VALUE — {var}` | ✅ | ✅ | `EditorSubsystem:2135/2153` |
| 🔴 **`C-table`** | ✅ | ⛔ | `VariableTableControl` referenced **only** inside its own folder + `VariableEditGestureBinder` |
| 🔴 **`C-dialog` + launcher** | ✅ | ⛔ | reached only through the gesture binder — which nothing constructs |
| 🔴 **`C-tick`** | ✅ | ⛔ | feeds the table |
| 🔴 **`C-watch`** | ✅ | ⛔ | `AiWatchWindow.DrawClientArea` draws its **own** `Name / Enabled / Hits` table; uses neither `PinnedSource`, the row renderer, nor `VariableValueFormatter` |
| 🔴 **`C-outline`** | ✅ | ⛔ | `BlackboardMyBlueprintModel` is **constructed by nothing** |

⚠ **Fourth instance of one pattern in a week** — the producer picker · `VariableEditLauncher` · and now
the whole table stack. ⭐ **Each shipped complete, tested, and with no caller.**
📄 **[`CHECKLIST_Track_C_Visual_Verification.md`](CHECKLIST_Track_C_Visual_Verification.md) §2 IS THIS BATCH'S ACCEPTANCE LIST.** ⛔ **Do not re-derive the feature set.**

### ⛔⛔ Two user rulings that bound the scope

| | |
|---|---|
| ⭐⭐ **PURELY ADDITIVE** | *"ad `VariablesPanelControl` — **keep for now**, but we need to rethink it later."* ⇒ ⛔ **NOTHING RETIRES this batch.** Two variable surfaces coexisting is **accepted, deliberately** |
| ⭐⭐ **the merge is a SEPARATE design task** | 📄 **[`Architect_Question_38`](Architect_Question_38_One_Details_Panel.md)** — one mode-switching Details panel, ⭐ **absorbing `BP-128`.** ⛔ **Do not start it, and do not fold the table into `BlueprintDetailsWindow`** *(that IS the merge)* |

---

## 1. ⭐⭐⭐ Host the outline on BTree/HSM — ⭐ *the user named this one exactly*

> ⭐ **User:** *"do you plan using myblueprint panel also for HSM and BTrees? … this one should be
> unified across BTree/HSM/blueprints — that means adding it to the list of panels for btree/hsm
> perspective."*

📐 **Ground truth, measured:**

| | |
|---|---|
| ⭐ **the model exists** | `BlackboardMyBlueprintModel(BlackboardHostKind host, Func<IReadOnlyList<BlackboardVariableEntry>> variables)` — sections `bb.inputs` · `bb.workingState` · `bb.assetGlobals`, built **per host** |
| ⭐⭐ **the panel is already host-agnostic** | `MyBlueprintPanel` in **`NodeEditor.UI`**, `IMyBlueprintModel` in **`NodeEditor.Core`** ⇒ ⛔ **nothing about it is blueprint-specific.** It was built for this |
| 🔴 **and the AI perspective registers no such window** | `PerspectiveWorkspaceRegistrar` creates **FindResults · Inspector · RuntimeInspector · TraceTimeline · BlackboardAuthoring · Diagnostics · Breakpoints · Watch** — ⛔ **no My Blueprint panel at all** |
| ⭐ **the template** | `BlueprintMyBlueprintWindow` — ⚠ **blueprint-side**, so the AI side needs its own thin `ManagedWindow` wrapper in `Hrot.Editor.AiShared` |

**rails:** ⭐ **the registrar CONSTRUCTS it** — asserted on the constructed object, not on the
registrar's source *(⛔ the vacuous-rail lesson, and the `2026-08-16` forwarding-rail rule)* · **a BTree
asset and an HSM asset each yield their expected section list in `SortOrder`** · ⭐ **creating in a
section produces a declaration of that kind** · **an empty section renders EMPTY, not absent.**

---

## 2. ⭐⭐ Host the table — ⭐ *and where is a decision I am making, not leaving open*

| | |
|---|---|
| ⭐⭐ **a dedicated variables-table window PER PERSPECTIVE** | ⭐ **the AI perspective and the blueprint perspective each get one**, fed by `SectionSource` |
| ⛔ **NOT folded into `BlueprintDetailsWindow`** | 📐 **measured: that window is the NODE inspector** — it takes `AiSelectionStore`, caches a session per selected **node**, and draws node property editors. ⭐⭐ **Folding a variable table into it is exactly `Q38`'s merge**, and `Q38` is explicitly deferred |
| ⭐ **selection routes it** | 📄 design §1c: *"selection yields a SECTION, not a variable… the routing key is `(asset, section)` + a highlight."* ⇒ **clicking a row in the outline re-filters the table to that section** |
| ⚠ **yes, this adds two windows** | ⭐ **and `Q38` exists to rationalise the count later.** ⛔ **Additive now beats pre-empting a design task the user has deferred** |

🔴 **STOP** if routing needs a selection channel that does not exist between the outline and the table —
⭐ **say what is missing rather than inventing a second selection store.**

**rails:** ⭐ **both windows are constructed by their registrar** *(asserted on the object)* ·
⭐ **selecting a section in the outline re-filters the table** · **the table renders heterogeneous rows**
*(several assets/entities)* · ⛔ **`VariablesPanelControl` still constructs and draws** — that is the
additive guarantee.

---

## 3. ⭐ Wire the Watch — ⚠ *and first determine whether it is one concept or two*

📐 **`AiWatchWindow.DrawClientArea` draws `Name / Enabled / Hits` over `_manager.AllBreakpoints.Where(bp
=> bp.IsWatch)`.** ⭐ **Track C's Watch is `PinnedSource` over `VariableRow`.**

🔴🔴 **STOP — answer this before wiring:** ⭐⭐ **are a "breakpoint marked as watch" and a "pinned
variable" the SAME entity, or two?**

| if… | then |
|---|---|
| ⭐ **the same** | **feed the existing window from `PinnedSource`** and delete nothing else — the window keeps its identity, its body changes |
| ⚠ **two concepts** | ⛔ **do NOT merge them silently.** ⭐ **Say so and wire the variable watch as its own feed**, with the breakpoint list untouched |

⭐ **The design's own hints:** `Watch.IsStale` and `!HasEverBeenWritten` are cited in
📄 `DESIGN_Variable_Details_And_Editing.md` §4b as **already shipped** — ⚠ **which suggests one entity,
but that is an inference, not a measurement.** ⭐ **Measure it.**

**rails:** ⭐ a pinned set spanning **two assets and two entities** renders with correct grouping and
**independent** highlight state · ⭐ **a stale row renders GREYED and refuses its dialog** ·
⭐ **a 136-byte struct pins and renders** *(the old 64-byte buffer limit is gone)* · **`Type` column
hidden by default in Watch, shown in Details.**

---

## 4. ⭐⭐ Resolve the four **⚠ verify** items — ⛔ *from CODE, not from the eye*

> ⭐ **The checklist marks these "verify" rather than claiming them, because I could not confirm them
> from code.** ⛔⛔ **The user will run the visual check next** — ⭐ **they should be CONFIRMING, not
> DISCOVERING.**

| checklist # | claim to settle |
|---|---|
| **2.7** | `GroupBy`, per-group fold state and the `Type` toggle **persist per panel** in the editor layout |
| **2.26** | the **`⋮` menu** carries *"Edit value…"*, *"Properties…"* **and Rename** |
| **2.40** | **planning mode**: values editable **and the budget indicator visible** |
| **2.41** | **running mode**: the Value column goes live **and the budget indicator hides** |

⭐ **For each: BUILT / NOT BUILT / built-but-unwired**, with the `file:line`. ⛔ **Build only what is
cheap and clearly in Track C's design** — ⭐ **anything bigger is a finding for Batch 80**, not a
drive-by.

---

## 5. ⭐⭐⭐ Gates — **THE NEW CONTRACT.** ⛔ *I do not re-run them*

> ⭐⭐⭐ **User ruling `2026-08-17`:** *"you seem to run the same gates as the implementation session has
> already done before reporting to you, this is an enormous waste of time; pls rather ask the
> implementation session to report same detail you want to see from running your gates."*
> ⇒ ⭐ **Rule 8 is rewritten in `.claude/CLAUDE.md`.** ⛔ **A missing row is now the one thing that sends
> me back to the terminal.**

**Baseline at `e8c2c1535`:** build **0 / 69** · FastHSM **300 / 300** · Blueprints **3691 / 3681 / 0 / 10** ·
AiShared **1289** · BTree.Editor **615** · Breakpoints **134** · Generators **270** · Hsm.Editor **551** ·
AiEditor.Persistence **136** · Examples.Scenarios **56 / 68 (12 skipped)** · Examples.UrbanCombat **29** ·
Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 64 / done 180**.

### ⭐ Report ALL SEVEN — ⛔ **items 2–5 are the ones that replace my re-run**

| # | report |
|---|---|
| **1** | **one row per gate: verbatim command · pass/fail/skip · delta vs baseline** |
| ⭐⭐ **2** | **a `--no-build` COLUMN.** ⛔ **`NodeEditor.Core`, `NodeEditor.UI` and `Fhsm.Tests` MUST BUILD** — they are out of solution and `--no-build` reports a **stale bin** |
| ⭐⭐⭐ **3** | **golden movement as a DIFF SHAPE, not a yes/no** — *"N files, purely additive, zero removed lines, what changed per file"*. ⛔ **"unchanged" alone is not enough** |
| ⭐⭐ **4** | **every RED confirmed PRE-EXISTING against the base commit, named, with the base sha** |
| ⭐ **5** | **the working tree is CLEAN after every suite run** *(⛔ else a test regenerated a golden)* |
| **6** | **both quarantine counts** — **12** scenario · **0** FastHSM. ⛔ **a new skip is a finding** |
| **7** | **`tracker-counts.py --check`** · **every id allocated** · **the started-marker sha** |

| | |
|---|---|
| ⭐⭐ **this batch is EDITOR-ONLY** | ⇒ **AiShared · Blueprints · NodeEdit ×2** are the suites it reaches. ⛔ **The blueprint golden set and `StructureHash` must not move at all** — ⭐ **if they do, you touched emission and that is the finding** |
| ⛔ **`Fdp.Toolkits.Tests`** | **I no longer look at it** — `DEBT-AIB-030`, **seven tests, identity rotates.** ⭐ **Confirm any red by `--filter`/namespace yourself and say so** |

---

## 6. Reporting

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **the registrar CONSTRUCTS the window, asserted on the OBJECT** · both host kinds.
⭐⭐ **item 2** — **where you hosted the table and why** · ⭐ **that selection routes it** · ⛔ **that
`VariablesPanelControl` still draws**.
⭐⭐⭐ **item 3** — ⭐ **one concept or two?** *(the measurement, not the inference)* · what you wired.
⭐⭐ **item 4** — **four verdicts with `file:line`** · what you built vs deferred.
**Always:** the started-marker sha · every id allocated · the `DEBT-AIB` rows touched.

⭐⭐⭐ **Thirteen batches, and the last six each corrected a premise of mine.** ⭐ **This batch exists
because of one I never even stated: that a component with passing tests is a component someone can
use.** ⛔ **Keep stopping when a premise fails — and now that I am not re-running your gates, the report
is the only place a gap can surface.**
