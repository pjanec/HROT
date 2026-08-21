<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file — the Batch 99 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. HANDOFF_Batch98 §2 was withdrawn mid-run (rule 1c) and this
  replaces it properly, built on the steer the user relayed.
-->
# HANDOFF — Batch 99: **the Properties form**

> 📌 **Dispatched at `e90af1936`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push
> `chore: started batch 99 at e90af1936` FIRST.**
> ⭐⭐ **`R-106`: a blocked item stops THAT ITEM, never the batch. Four verdicts per item.**

> ## ⭐⭐⭐ BATCH 98 WAS THE BEST RETURN OF THIS PROGRAMME — **say it plainly**
> ⭐ `98a` landed, `98c` landed, `98b` was **reverted in full before any commit** on the user's steer and
> **blocked nothing** — ⭐⭐ **exactly what `R-106` asks for.**
> ⭐⭐⭐ **And it found two defects that would have made its own fix DESTRUCTIVE** — `BP-366` *(the edit
> would die on close)* and `BP-367` *(an untyped OK would overwrite an authored `1` with `0`)* — ⭐ **both
> harmless while the write refused, which is exactly why they survived.**
> ⭐⭐ **Then Batch 96's own rail caught a wrong-asset hazard `98a` introduced, during the gate run**, and
> the rail change was **argued rather than done quietly.** ⛔ **Nothing here corrects any of it.**

---

## 1. 🛠 **`99a` — the Properties form** *(`BP-369`; `R-109`)*

⭐⭐⭐ **The design is SETTLED.** 📄 **[`STEER_Batch98b_Properties_Is_A_Custom_Dialog.md`](STEER_Batch98b_Properties_Is_A_Custom_Dialog.md)**
and `R-108`/`R-109`. ⛔ **Do not re-derive it, and ⛔ do not re-open the StructEdit question.**

### ⛔ Why it is CUSTOM — **the two-line version**

| field | ⛔ why a struct field write is wrong |
|---|---|
| **`Name`** | it is a **RENAME** ⇒ the **refactor service** |
| **`Type`** | it is a **RETYPE MIGRATION** — `DefaultValueJson` may not convert, offsets move, **`StructureHash` moves** *(`R-24`)* |

⭐ A struct commit means *"here is the new struct, apply it"* ⇒ ⛔ it would have to be **diffed** against
the old declaration and **dispatched per field** — a custom controller in a StructEdit costume.
⭐ **Read-only is DIALOG-LEVEL and already built** *(Batch 96 `3b`, `97b`'s `Decide`)* ⇒ ⛔ **no
per-field flag anywhere.**

### ⭐⭐⭐ Build it by FACTORING `VariableCreateModal` — **most of it exists**

📐 **Measured:** `Hrot.Blueprints.Editor/Windows/VariableCreateModal.cs` already draws
**`Name`** *(`InputText`, `:134`–`:136`)* with **duplicate-name validation** *(`IsDuplicateVariableName`,
`:196`)* and **`Type`** *(`BeginCombo` over **`BlueprintTypeSystem.SelectableTypeIds`**, `:138`–`:152`,
including the discovered-struct arm)*.

⭐⭐ **Ruling 9, at the right level: CREATE and EDIT-PROPERTIES are the SAME FORM.** ⇒ **factor that
body into one reusable form and drive it from both gestures** — ⛔ **not two dialogs that each know how
to draw a type combo.**

### ⭐ The set — **the SCHEMA is the filter**

`VariablePropertySchema.For(kind)` decides which controls appear:

| carrier | properties |
|---|---|
| **`VariableDecl`** | Name · Type · DefaultValue · Tooltip · Comment · Category · IsEditable · IsExposedOnSpawn |
| **`ParameterDecl`** | Name · Type · DefaultValue · Tooltip · Comment |
| **`BlackboardVariableEntry`** | Name · Type · DefaultValue · Comment |

⛔ **`Role`/`Scope` is NOT in the dialog** — *the SECTION is the classification* *(user, `2026-08-16`)*.
⛔ **Replication and Range are excluded** — **no carrier has a backing member**, and the schema's own
rail fails a property that cannot be stored.

### ⭐ Availability

| | planning | running / paused | replay |
|---|---|---|---|
| **Properties…** | ✔ **editable** | ⚠ **read-only** — *"you cannot retype a variable mid-run"* | ⛔ read-only |

⭐ **`97b`'s `VariableEditGesture.Decide` over `VariableEditPolicy` already exists — FEED IT.**
⛔ **Do not write a second matrix** *(ruling 9)*.

### ⛔⛔ The two fields that are OPERATIONS — **handle them, or disable them honestly**

| ⭐ | |
|---|---|
| **`Name`** | ⛔ **never a direct write.** ⭐ **Route through the refactor service** — 📄 the design requires it of both routes *(F2/menu rename and Properties)*. ⚠⚠ **On BTree/HSM this is not optional:** the binding stores the **NAME STRING** and `RenameVariable` does **not** fix up `ExpressionTargetField` *(`M-15`)* ⇒ **a bound AI variable's rename DANGLES it**, caught at build as `BTREE0002`, **a whole-asset skip**. ⭐ On Blueprint the persisted `Guid Id` makes it safe *(`M-16`)* |
| **`Type`** | ⚠ **if a retype cannot be made SAFE in this batch — ship it DISABLED with its reason and REPORT it** *(`R-106`)*. ⛔⛔ **Do NOT silently write the new type and leave `DefaultValueJson` unconvertible.** ⭐ **Shipping six of eight fields is a win; shipping a silent corruption is not** |

### ⭐⭐ The rails

| ⭐ | |
|---|---|
| **the schema drives the form** | assert `VariablePropertySchema.For(kind)` decides the offered controls, for **all three** carriers |
| ⭐⭐⭐ **`Name` calls the refactor service** | ⛔ **not** *"a string changed"* — assert the **service is invoked** |
| **availability** | the form is read-only in running/paused/replay, ⭐ driven by `VariableEditPolicy` — ⛔ not a second matrix |
| ⚠ **whose object · which layer is faked** | 📌 `M-29`. ⭐ **The DRAW is unrailed** *(`R-21`/`R-62`)* — say so, as Batches 96–98 all did |

---

## 2. ⚠ **`99b` — `BP-367`'s sibling: is the INITIAL arm right everywhere else?** *(only after `99a`)*

📐 **`98a` found that `BlueprintVariablesWindow.Entries` never projected `DefaultValueJson`** — ⭐ and
the line consuming it calls itself *"Row 58 — the INITIAL arm's source."*

⇒ ⭐⭐ **ENUMERATE the other projections** *(📌 `R-74` — the graph, ⛔ not a grep alone)*: every place a
`BlackboardVariableEntry` or a `VariableViewModel` is constructed from a declaration, and check that
**`DefaultValueJson` survives the projection.**
⚠ **`BP-367` was invisible because the write refused.** ⭐ **The write no longer refuses** ⇒ any other
lossy projection is now **live**.

⭐ **A count of "one, and it is fixed" is a fine answer** — ⛔ **an absent enumeration is not.**

---

## 3. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **Properties as a StructEdit document** | `R-109` |
| **a per-field read-only flag** | `R-109` — read-only is dialog-level and built |
| **a second editability matrix** | ⭐ `97b`'s `Decide` exists |
| **`Role`/`Scope` in the dialog** | user ruling `2026-08-16` |
| **a silent retype** | `99a` — ⭐ disable it honestly instead |
| **an `Instance`-blueprint live write** · **a BTree/HSM live writer** | `BP-364` · 📌 `Q32` §2.1 |
| **reverting anything from Batch 98** | ⭐ all of it holds |

---

## 4. ⭐ GATES

⭐ **Baseline** = Batch 98's table, base sha **`e90af1936`**: AiShared **1705** · BTree.Editor **622** ·
Hsm.Editor **554** · Blueprints **3827 / 0 / 10 skip** · Hrot.Editor **201** · Breakpoints **143** ·
Generators **277** · Persistence **143** · NodeEditor.Core **211** · NodeEditor.UI **135** · Fhsm **300** ·
StructEdit **191 / 1** *(⚠ `BP-363`, pre-existing)* · Fdp.Presentation **146 filtered** ·
tracker **open 78 / done 226** · rulings **74/74**.

⭐ **Keep Batch 98's table shape** — `--no-build` column, `EXIT=` unfiltered, the diff-shape golden row,
revert-goes-red per item, and the *"whose object · which layer is faked"* table.
⚠ **`99a` touches `Hrot.Blueprints.Editor` and `Hrot.Editor.AiShared`** — ⭐ if you factor
`VariableCreateModal`, **say what moved and what its old callers now call.**

---

## 5. ⭐⭐ WHAT THE USER IS DOING IN PARALLEL

⭐ **Re-running the acceptance test on `98a`:** open `Count4` → right-click `Count` → **"Edit value…"** →
type → **OK** → **the value changes**, in **PLANNING**.
⚠ **If that still fails, it is a `98a` finding and it OUTRANKS this batch** — ⭐ **expect a steer.**
