<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file. It REPLACES §2 (`98b`) of HANDOFF_Batch98.
stale-below: nothing.
known-rot: none.
known-conflict: HANDOFF_Batch98 §2 says to build Properties through StructEdit. That is
  WITHDRAWN by this note. §1 (`98a`) and §3 (`98c`) are UNCHANGED.
-->
# ⛔⛔ STEER — **`98b`: Properties is a CUSTOM dialog, not a StructEdit document**

> ⭐⭐⭐ **User, `2026-08-20`:** *"we have no per-field read only flag in Struct editor — wait why do we
> need StructEdit readonly fields? it does not make sense. StructEdit edits always whole structure, not
> just part of it, and this is what we want for the watch value editing. The Properties dialog can
> hardly be a StructEdit dialog, it must be custom."*
>
> ⭐⭐ **Correct on both halves. ⛔ `HANDOFF_Batch98` §2 is WITHDRAWN and replaced by this note.**
> ⭐ **`98a` and `98c` are UNCHANGED — keep going on those** *(`R-106`)*.

---

## 1. ⭐⭐⭐ THE PER-FIELD READ-ONLY FLAG IS NOT NEEDED — **you were solving the wrong problem**

⭐ **For "Edit value…" there is nothing to gate per field:** the session is opened over the variable's
**own value**, so the document **IS** the thing being edited — 📌 exactly what `96b` established when it
stopped synthesising `$.<name>`. ⇒ ⭐⭐ **read-only is a DIALOG-LEVEL state, not a field attribute.**

📐 **And it is already built** — Batch 96 `3b`: a row that can never be written **opens shaped as a
read-only VIEW with no OK**, and `97b`'s `VariableEditGesture.Decide` greys the gesture the policy
denies. ⇒ ⛔ **nothing per-field is required anywhere in the value path.**

⚠⚠ **Where the need came from:** trying to render the **PROPERTIES set** through StructEdit. That set
differs per carrier — **8 / 5 / 4** properties *(`VariablePropertySchema.For`)* — so one DTO would need
per-field hiding. ⭐⭐ **That is a TYPE problem, not a FLAG problem** — and the deeper reason follows.

---

## 2. ⛔⛔⛔ WHY PROPERTIES CANNOT BE A STRUCT EDIT — **two of its fields are OPERATIONS, not writes**

⭐ A StructEdit commit means *"here is the new struct, apply it."* ⛔ **Two Properties fields do not mean
that:**

| field | ⛔ why a field write is wrong |
|---|---|
| ⭐⭐⭐ **`Name`** | **it is a RENAME.** ✅ Safe on Blueprint *(declarations carry a persisted `Guid Id`, references store `VariableId` — `M-16`)*. ⛔⛔ **On BTree/HSM the binding stores the NAME STRING and `RenameVariable` does NOT fix up `ExpressionTargetField`** *(`M-15`)* ⇒ **renaming a bound AI variable DANGLES it**, caught at build as `BTREE0002`, **a whole-asset skip**. ⇒ ⭐ **it MUST run the refactor service** — 📄 the design already requires that of both routes |
| ⭐⭐⭐ **`Type`** | **it is a RETYPE MIGRATION.** The stored `DefaultValueJson` may not convert; the field's size and offset move; ⚠ **`StructureHash` moves with the field list** *(`R-24`)*. ⛔ A struct commit expresses none of that |

⇒ ⭐⭐ **A struct-editor commit would have to be DIFFED against the old declaration and dispatched
per-field to side effects.** ⛔ **That is a custom controller with a StructEdit costume on.**

### ⭐ And the rail that argued for one dialog has lost its premise

📄 `DESIGN_Variable_Details_And_Editing.md:480` — *"two dialogs, one implementation: 'Edit value…' and
'Properties…' differ **only** by the `EditScope` argument."*
⛔ **Batch 96 measured that false** *(`BP-359`)*: both open the **value** document, and the design's own
`:233` says Properties takes *"a properties object for that declaration kind"* — ⭐ **a different
OBJECT.** ⇒ **the "one implementation" argument rested on a sameness that does not exist.**

⚠ **Ruling 9 is NOT being weakened** — ⭐ **it is being satisfied at the right level**, see §3.

---

## 3. ⭐⭐⭐ WHAT TO BUILD INSTEAD — **mirror `VariableCreateModal`, which already draws most of it**

📐 **Measured `2026-08-20`** — `Hrot.Blueprints.Editor/Windows/VariableCreateModal.cs` already has:

| ⭐ it already draws | line |
|---|---|
| **`Name`** — `ImGui.InputText`, with **duplicate-name validation** *(`IsDuplicateVariableName`)* and an empty-name guard | `:134`–`:136`, `:196`–`:200` |
| ⭐⭐⭐ **`Type`** — `ImGui.BeginCombo` over **`BlueprintTypeSystem.SelectableTypeIds`**, short-named, with the discovered-struct arm | `:138`–`:152` |
| a container combo *(Single / List)* | `:159` |

⇒ ⭐⭐ **The hard field — the type picker — EXISTS and is wired to the one offerable set** *(`S5`,
Batch 65)*. ⛔ **Do not build a StructEdit custom field editor for it.**

⭐⭐⭐ **Ruling 9, correctly applied: CREATE and EDIT-PROPERTIES are the SAME FORM.** ⇒ **factor
`VariableCreateModal`'s body into a reusable properties form, and let both the create gesture and
"Properties…" drive it.** ⛔ **Not two dialogs that both know how to draw a type combo.**

| ⭐ | |
|---|---|
| **the set** | ⭐ **`VariablePropertySchema.For(kind)` decides which controls appear** — 8 / 5 / 4. ⛔ **Not a per-field flag: the SCHEMA is the filter, and it is already measured off the carriers** |
| **read-only** | ⭐ **dialog-level, from `VariableEditPolicy`** — planning ⇒ editable · running/paused ⇒ **read-only** *("you cannot retype a variable mid-run")* · replay ⇒ read-only. ⭐ `97b`'s `Decide` already greys the gesture |
| ⭐⭐ **`Name`** | ⛔ **never a direct write** — route through the **refactor service**, which the design requires of both routes |
| ⭐⭐ **`Type`** | ⚠ **if a retype cannot be made safe in this batch, ship it DISABLED with its reason and REPORT it** *(`R-106`)* — ⛔ **do not hold the dialog**, and ⛔ **do not silently write the new type and leave `DefaultValueJson` unconvertible** |
| ⛔ **not in the dialog** | **`Role`/`Scope`** — *the SECTION is the classification* *(user, `2026-08-16`)* · **Replication**, **Range** — **no carrier has a backing member** |

⭐ **What stays StructEdit: "Edit value…", unchanged.** `ScalarEditBox<T>`, the wrapper, the commit
arms, the live writer — ⛔ **none of that moves.** ⭐ The user's own words: *"StructEdit edits always the
whole structure, and this is what we want for the watch value editing."*

---

## 4. ⭐ THE RAIL

⭐ **The schema decides the controls** — assert `VariablePropertySchema.For(kind)` drives which fields
the form offers, for all **three** carriers.
⭐⭐ **`Name` goes through the refactor service** — ⛔ assert it is CALLED, not that a string changed.
⚠ **Say which layer is faked** *(`M-29`)* — ⛔ **the draw is unrailed** *(`R-21`/`R-62`)*, as always.

---

## 5. ⚠ IF THIS IS BIGGER THAN THE REST OF THE BATCH

⭐ **`R-106`: finish `98a` and `98c` first.** ⛔ **Do not let `98b` swallow the run.**
⭐ **`98a` is the user's acceptance test** — *open `Count4`, right-click `Count`, "Edit value…", type,
OK, the value changes* — ⭐⭐ **that is worth more than the Properties dialog.**
