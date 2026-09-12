# HANDOFF — Batch 82: **`U-6` — Details hosts the shared table**

> 📌 **Dispatched at `0973760ca`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ **Documents that change after it are FYI ONLY.**
> ⚠ **If a later document INVALIDATES an item here — STOP AND REPORT IT. ⛔ Do NOT adapt, do NOT
> revert.** 📌 **Batch 81 lost 20 minutes to exactly that**, and it was the coordinator's fault.
> ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push `chore: started batch 82 at <sha>` first.**

---

## 0. ⭐⭐⭐ Why THIS, now — **the design basis, per the new rule**

⭐⭐ **Two independent roadmaps converge on the same next item.** ⛔ **That is the whole argument:**

| roadmap | position |
|---|---|
| 📄 **`Variable_Model_Unification.md` §4** *(the LIVE order — ⚠ the `A→B→C→D` table below its banner is **SUPERSEDED**)* | `0` → `C` ✅ → `A` ✅ → ⭐ **`B`** ← **here** → `B′` *(blocked on `BP-228`)* → `D1`–`D4` |
| 📄 **`Q32_…_ANSWERS.md` §4** *(the MASTER sequencing table)* | `56` ✅ → ⭐ **`57` = `U-6`** ← **here** → `58` → `59` → `59b` → `59c` → `60` → `61` |

⇒ ⭐ **They are the same item.** 📌 **`Q32` §4 row 57, verbatim:**
> *"`U-6` — Details hosts the **shared** control + ruling 2's selection routing | ⛔ **the shared
> control, never a blueprint copy** (ruling 9)"*

### ⭐⭐⭐ And it is the UNBLOCK CONDITION for the visual-check suspension

📌 **`Q32_…_ANSWERS.md` header, user ruling `2026-08-14`, verbatim:**
> *"⛔⛔ **AND A SEQUENCING RULING: NO VISUAL CHECKS** until the **Details panel is implemented** and
> the emitters and all access infrastructure are unified."*

⚠⚠ **That suspension has been in force the whole time.** ⛔ **The coordinator ran a visual check on
`2026-08-17` without having read it** — ⭐ **the user re-derived the ruling unaided.**
⇒ ⭐⭐ **This batch is the first half of lifting it.**

---

## 1. ⭐⭐ What `U-6` IS — **and what it is NOT**

📌 **`Q32` question doc §"Does this ride `U-6` or follow it?", verbatim:**
> *"⚖️ **Lean: `U-6` first, unchanged and small** — **move the existing table into Details and prove it
> renders.** **Then** values as a second slice. ⛔ **Bundling them means a red panel could be either
> change.**"*

⭐⭐⭐ **The control ALREADY EXISTS and is ALREADY SHARED.** ⛔ **Build nothing new that Track C built:**

| ✅ exists | where |
|---|---|
| the generic row list | `VariableTableControl` — renders `IReadOnlyList<VariableRow>`, **knows nothing about its source** |
| the section source | `BlackboardSectionRowSource` *(filters by `SectionOf`)* · `PinnedVariableRowSource` |
| the outline + its `SectionSelected` event | `AiMyBlueprintWindow` · `BlueprintMyBlueprintModel` |
| the value decoder | `RawValueDecoder` |

⇒ ⭐ **This batch is PLACEMENT and ROUTING, not construction.**

---

## 2. 🔴 Item 1 — **Details hosts the table** *(the placement half)*

### ⭐⭐ Design basis
📌 **`Q32` ruling 1:** *"**Details hosts the list of vars**, as designed | `U-6`, unchanged"*
📌 **`Q32` ruling 6:** *"⭐⭐ **The same Details panel is REUSED for every asset type** — HSM, BTree,
Blueprint ⇒ **this is a cross-host deliverable, not a blueprint one**"*
📌 **ruling 9** *(the acceptance criterion)*: *"no keeping two implementations for the same concept."*

### ⚠⚠ MEASURE FIRST — **there may be no shared "Details" host to put it in**

📐 **What I measured** *(and it is all I measured — ⛔ do not treat this as the design)*:

| window | assembly |
|---|---|
| `BlueprintDetailsWindow` | `Hrot.Blueprints.Editor` |
| `InspectorWindow` | ⚠ **exists TWICE** — `Hrot.Blueprints.Editor` **and** `Hrot.Editor.AiShared` |

⇒ ⭐⭐ **Ruling 6 wants ONE panel across three perspectives, and I did not find one.**
⚠ **So `U-6` may require a shared Details HOST that does not exist** — ⛔ **which is bigger than
*"unchanged and small"* implies.**

> ⭐⭐⭐ **THE SPLIT RULE — stated so you need not ask.** ⛔ **If creating the shared host is large,
> STOP AND REPORT with the measurement.** ⭐ **Landing it on ONE perspective first, proven, is a
> legitimate outcome** — 📌 ruling 6 says the panel is reused, ⛔ **it does not say all three must land
> in one batch.** ⚠ **Report which perspectives you did.**

---

## 3. 🔴 Item 2 — **ruling 2's selection routing** *(the navigation half)*

📌 **`Q32` ruling 2, verbatim** — ⭐ *"new, and it is the panel's whole navigation model"*:
> *"⭐ **Selection routes:** click a **global** in My Blueprint ⇒ the list of **globals / working
> state**. Click a **local** ⇒ the locals of the **currently selected graph**."*

⚠ **Track C's routing is SECTION-keyed** *(`SectionSelected` → `ShowSection`)*. ⭐ **Ruling 2 is
GLOBAL-vs-LOCAL keyed**, and the local arm is **graph-scoped**. ⇒ ⭐⭐ **Close, but not the same —
reconcile them rather than adding a second routing mechanism** *(ruling 9)*.

⭐ **The graph-scoped feed already exists:** `BlueprintLocalVariableSchemaSource`, and the outline's
locals section already follows the canvas via `AiCanvasContext.CurrentGraphId`.

---

## 4. ⛔ EXPLICITLY OUT OF SCOPE — **each with the batch that owns it**

| ⛔ not here | owner |
|---|---|
| the **Value column** / run-state meaning switch | **`58`** — 📌 *"Then values as a second slice"* |
| the **StructEdit dialog** *(three-dot + double-click)* | **`59`** |
| **any write path** | **`59c`**, and ⚠ **it needs the surgical ECB field write first** |
| **retiring** any Variables window | **`60` = `U-16`** — 📌 gated: *"⛔ **only after Details is proven**, or there is no editing surface at all"* |
| the **shared cross-host OUTLINE** | **`61`** — 📌 *"⛔ **Not folded into `U-6`**"* |
| the **visual check** | ⛔ still suspended — this batch is **half** the unblock condition |

### ⚠ The standalone `AiVariablesWindow` — ⭐ **KEEP IT, and say so in your report**

⛔ **Do NOT retire it as "now redundant."** 📌 **`U-16` is gated on Details being PROVEN**, and 📌 the
user's `2026-08-17` ruling is *"keep for now."* ⭐ **Coexistence is deliberate until `60`.**
⚠ **But do note in your report whether it became redundant** — ⭐ **that is `U-16`'s evidence.**

---

## 5. ⭐ Gates — **the rule 8 contract, all seven rows**

⭐⭐ **Your report substitutes for my run.** ⛔ **A missing row is the one thing that sends me to the
terminal.**

| # | report |
|---|---|
| **1** | one row per gate: **verbatim command · pass/fail/skip · Δ vs baseline** |
| **2** | ⭐⭐ **a `--no-build` COLUMN.** ⛔ **`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` take NO `--no-build`** |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE** |
| **4** | ⭐ **every RED confirmed pre-existing against the base sha**, named |
| **5** | ⭐ **working tree CLEAN after every suite run** |
| **6** | ⭐ **both quarantine counts** — ⛔ **a new skip is a finding** |
| **7** | ⭐ **`tracker-counts.py --check`** · ⭐ **`rulings-check.py`** · **every id you allocated** |

⭐ **Baseline** *(Batch 81)*: build **0/69** · AiShared **1330** · Blueprints **3719/3709/10** ·
BTree.Editor **615** · Hsm.Editor **551** · Generators **270** · Breakpoints **134** · Persistence
**136** · Scenarios **56/68 (12 skipped)** · UrbanCombat **29** · Toolkits **1964** · NodeEditor.Core
**211** · NodeEditor.UI **135** · FastHSM **300** · tracker **open 65 / done 185** · rulings **35/35**.
⛔ **`Fdp.Toolkits.Tests` = `DEBT-AIB-030`** — identity rotates; confirm by `--filter`.

---

## 6. ⭐ Two cheap side items — ⛔ **only if they stay cheap**

> ⭐⭐ **These are DOCUMENT repairs, from the `2026-08-17` corpus sweep.** ⚠ **They misled the
> coordinator today; left alone they will mislead the next session.** ⛔ **Drop either if it grows.**

| | repair | evidence |
|---|---|---|
| **a** | ⛔ **`BP1031` is RETIRED — but FOUR design docs still describe it as LIVE**, including `DESIGN_Parameter_Model.md`, ⚠ **the one `RULINGS.md` sends readers to** | `Blueprint_Issues_Tracker` `BP-278` |
| **b** | ⛔ **`DESIGN_Variable_Details_And_Editing.md` still ORDERS the STATIC PARAMETERS retirement that was WITHDRAWN** *(premise measured inverted; it is the only LIVE surface)* | `BP-295` · `HANDOFF_Batch74` §3 |

⭐ **Add a `STATUS` block to any design document you touch** *(format: `.claude/CLAUDE.md`)* — ⛔ **do
not sweep the back catalogue.**
