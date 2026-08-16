# DESIGN — variable details & editing, consistent across every asset type

> **Track C design. `2026-08-15`.** ⭐ **This is what gets built.**
> ⛔ **Supersedes** the Track C rows in [`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) §3 and the
> panel sequencing in [`DESIGN_Variable_Details_And_Live_Values.md`](DESIGN_Variable_Details_And_Live_Values.md) §8.
> ⭐ **Additive to** `docs/blueprints/NodeEdit/D7-details-panel.md` — see §8.

![surfaces and dialog](DESIGN_Variable_Details_And_Editing.svg)

---

## 1. ⭐ The model

| surface | shows | filtered by |
|---|---|---|
| **My Blueprint** | every variable, **every asset type that has variables** | nothing — it is the tree |
| **Details** | ⭐⭐ **the TABLE — exactly three columns: `Name` · `Type` · `Value`** | the **section** of the clicked row |
| **Watch** | the same table | the pinned set |

⛔⛔ **THREE COLUMNS, NOTHING ELSE.** No Bytes, no Category, no Role/Scope, no flags. ⭐ **Everything
else lives in the Edit dialog only.** ⇒ **the table is a value monitor, not a property grid** — that is
what lets it stay readable at a glance and what makes it *"resemble an automatic watch panel"*.
📌 **Per-variable size is still reachable — in the dialog; the whole-asset total is the planning-mode
budget indicator (§5).**

⭐⭐ **Selection yields a SECTION, not a variable.** Clicking any row in *Local Variables* routes Details
to the locals-of-this-graph table with that row highlighted; clicking any row in *Variables* routes it to
the asset-scope table. ⇒ **the routing key is `(asset, section)` + a highlight.**

⛔ **The table is never replaced by a single-variable form.** It is the only place where every variable,
its type and its current value are visible together — *"resembles an automatic watch panel"* — and that
is the reason it exists.

---

## 2. ⭐⭐ The editable properties — **measured, not taken from a spec**

⛔ **Fields with no storage are REMOVED from the design.** `D7`'s **Replication** *(Replicated,
RepCondition, RepNotify)* and **Range** *(Min/Max)* have **no member on any carrier** — `VariableDecl`
has nine members, `ParameterDecl` six, `BlackboardVariableEntry` seven, and none of them is these.
⭐ **Building them would produce controls with nowhere to save.**

| property | BP `Variable` | BP `Parameter` | BTree/HSM entry | editable |
|---|---|---|---|---|
| **Name** | ✔ | ✔ | ✔ | ✔ ⚠ **must run the refactor-rename service** |
| **Type** *(`TypeId` · `IsArray` · `GenericArgs` · `Capacity` · `InitialLength`)* | ✔ | ✔ | ✔ *(`FieldType`)* | ✔ ⛔ **planning only — it moves bytes** |
| **Default value** *(`DefaultValueJson`)* | ✔ | ✔ | ✔ | ✔ — ⭐ **this is "the value"** |
| **Tooltip** | ✔ | ✔ | — | ✔ |
| **Comment** | ✔ | ✔ | ✔ | ✔ |
| **Category** | ✔ | ⛔ | ⛔ | ✔ where present |
| **IsEditable** | ✔ | ⛔ | ⛔ | ✔ |
| `IsExposedOnSpawn` | ✔ | ⛔ | ⛔ | ⚠ **persisted but nothing reads it at spawn.** ⛔ **Keep it — do not "clean it up"**; per the `.dev/` rule, unreferenced ≠ unintentional. 📐 File the gap, do not close it |
| `Id` | ✔ | ✔ | — | ⛔ identity — references resolve by it |
| `IsAutoManaged` | — | — | ✔ | ⛔ editor-owned |
| `Role` / `Scope` | *(is `DeclarationKind`)* | — | ✔ | ⛔ **read-only for blueprints** — `Q-k`: *"a move, not a toggle"* |

⇒ ⭐⭐ **Seven editable properties, and the set DIFFERS BY DECLARATION KIND** ⇒ **the dialog is driven by
the kind, not by one fixed form.**

---

## 3. ⭐⭐⭐ One dialog, two scopes

`IComponentEditService.Open(object component, Type componentType, EditScope?, EditContext?)` takes
**any boxed object** — it is not ECS-specific — and `EditScope` already ships `WholeComponent`,
`ForField(path)` and `ForFields(...)`.

| run state | passed in | designer edits |
|---|---|---|
| **planning** *(not started)* | a **properties object** for that declaration kind + `EditScope.WholeComponent` | all seven, as applicable |
| **running / paused** | the variable's **own value** + its CLR type | **value only** |

⭐⭐ **The mode is a PARAMETER, not a second implementation** — same dialog, same `IEditSession`
lifecycle, same OK/Cancel, same validation. ⛔ **This is what keeps it inside ruling 9.**

⚠ **The one genuinely new UI work:** `Type` needs a **picker** editor and `Category` a combo.
StructEdit supports custom editors; they must be registered. ⛔ **`S5` lands first** — the picker needs
**one** offerable list, and today there are two (`SelectableTypeIds` vs `EditorOfferableTypeIds`).

---

## 4. ⭐ Gestures — identical on every surface

| gesture | result |
|---|---|
| **`⋮` → "Edit…"** | opens the dialog — **on the My Blueprint row AND on the table row** |
| **double-click a row** | ⭐ **the same dialog**, everywhere |
| **F2, or `⋮` → "Rename"** | inline rename ⚠ **moved off double-click**, which now belongs to "Edit…" |
| single-click | selects; Details re-filters to that row's section |

⭐ **Rename is not lost:** the dialog carries `Name`, and committing it runs the refactor service exactly
as inline rename does today. ⇒ **one gesture, one meaning, on every surface.**

---

## 4a. ⭐⭐⭐ Change highlighting — the value monitor half

⭐ **A value that changed is drawn RED for one step, then returns to normal** — the Visual Studio
debugger behaviour. ⛔ **Non-planning modes only** *(running · paused · stepping · replay)*.

| | |
|---|---|
| ⭐⭐ **the unit is a TICK, not a UI frame** | ⛔ **Never compare per rendered frame** — at 60 fps against a slower tick that flickers meaninglessly. **Compare against the value at the previous tick boundary**; while paused/stepping, "previous step" *is* the previous tick |
| ⭐⭐ **compare RAW BYTES, not the formatted string** | ⛔ **a formatted value hides change** — a `float` moving in its 7th digit renders identically. **Diff the byte slice** |
| ⭐ **state to keep** | per row: `(lastValueBytes, lastChangedTick)`. **Render red while `currentTick == lastChangedTick`** ⇒ ⭐ **the rule is a pure predicate and therefore HEADLESSLY TESTABLE**, even though the colour is not |
| ⭐ **who owns it** | ⛔ **not the breakpoint snapshots** — `_preTickSnapshot`/`_postTickSnapshot` only exist while the debugger is engaged, and the monitor must work when it is not. ⇒ **the shared row renderer owns a small previous-value cache**, keyed by `(asset, variable)`, covering the whole section so scrolling does not reset it |
| ⭐ **scope** | the cache is **per section**, shared by Details and Watch — one implementation, both hosts |
| ⚠ **a value that changes every tick stays red** | ⭐ **correct and expected** — VS behaves the same. **Do not add damping**; it would hide genuine churn |
| ⚠ **struct rows** | diff the whole slice; **the whole cell** goes red. No per-field highlighting in the table — that is the dialog's job |

### ⚖️ One sub-decision, with a lean

⛔ **An optimistic edit (§6) also changes the displayed value.** If it is painted the same red, *"the sim
changed this"* and *"my edit is pending"* look identical. ⚖️ **Lean: give pending its own marker**
*(e.g. italic or a trailing `*`)* **and reserve red for sim-driven change.** ⭐ **Small, and it keeps the
monitor honest.** 📐 **Flagged, not assumed — say so if you want them the same.**

---

## 5. ⭐ Run state governs everything

| | planning | running / paused | replay |
|---|---|---|---|
| Value column | the **initial** value | the **current** value | current, ⛔ **read-only** |
| ⭐ **change highlight** | ⛔ **none** | ✔ **red for one tick** | ✔ **red for one tick** |
| dialog scope | whole properties object | value only | ⛔ **no dialog** |
| **byte-budget indicator** | ⭐ **shown** | ⛔ **hidden** | hidden |

⭐⭐ **The budget indicator is PLANNING-ONLY chrome, not part of the variable list.** It answers *"will
this layout fit?"* — a question about the layout being authored. Once the exercise runs the layout is
fixed and the number is noise. ⇒ ⛔ **it needs no per-host capability flag — the same run-state switch
the Value column already uses covers it**, and it retires the old objection that the shared control was
*"inappropriate"* to reuse.

⭐ **Editability = run state ∧ row kind.** ⛔ **Read-only-passthrough (🔒) and node-owned
(`IsAutoManaged`) rows never get a writable dialog, in either mode.**

---

## 6. ⭐ The write path

| | |
|---|---|
| **planning** | ⭐⭐ **already ships** — `DefaultValueAuthoring.Hydrate` / `OpenSession` / `CommitAndSerialize` → `IBlackboardManagedAsset.UpdateVariableDefaultValueJson(name, json)`. **Re-host it; do not rebuild it**, and retire the `InspectorWindow` "STATIC PARAMETERS" section it currently lives in so there is one home |
| **running / paused** | ⭐⭐⭐ **OPTIMISTIC DISPLAY** *(user ruling)* — **paint the new value in the cell immediately**, then **stage** through the existing `StageMutation` path, which lands at the tick **N+1** boundary. ⛔ **Do NOT write `_liveRepo` during a pause** — `Blackboard1024` is `[DataPolicy(NoSave)]`, i.e. **snapshotted and recorded**, so a non-simulation write breaks Flight Recorder linearity |
| 🔴 **prerequisite, either way** | **the surgical field write** — `SetComponentFieldRaw(entity, typeId, byteOffset, src, size)` in `Fdp.Core`. ⛔ **Today's staged write is whole-component and lands AFTER the restore, so every other field reverts a tick** — on the shared blackboard that reverts BTree and HSM state |

---

## 7. ⭐ What already ships — reuse, do not rebuild

| | |
|---|---|
| `VariablesPanelControl` | in `AiShared`, descriptor-driven, per-section budgets, **live Value column already built** *(5th column, `"—"` on no match)* ⚠ gated on a name-match against the selected entity |
| `DefaultValueAuthoring` + `UpdateVariableDefaultValueJson` | the whole planning-mode commit path |
| StructEdit — `IComponentEditService` / `IEditSession` / `EditScope` | the dialog engine and its two modes |
| Watch `"(pending)"` | *"nothing before the run"* is **already designed and built** via `!HasEverBeenWritten` |
| ⚠ **the Watch refresh gap is NOT an empty handler** | it needs **Trace** compile mode — ⛔ **Debug emits no `PinValueChanged` at all**, and `QuickReloadService:64` hardcodes `CompilerMode.Debug`. 📐 **Fix the mode, then the handler** |

---

## 8. ⭐ Relationship to `D7` — additive, not a contradiction

`D7-details-panel.md` is authoritative and routes by **target kind**. This design **adds a
`VariableSection` target** and moves `D7`'s `Variable` field list from a docked form into the dialog —
⭐ **the field list is preserved; only its host changes** *(minus the two storage-less groups, §2)*.
⛔ **`D7`'s `Variable`/`LocalVariable` single-target form is superseded by the section table + dialog.**

---

## 9. Rails

| | |
|---|---|
| ⭐⭐ **three columns** | a test asserting the table exposes **exactly** `Name`, `Type`, `Value` — ⛔ **a fourth column fails it** |
| ⭐⭐ **change highlight** | ⭐ **headless**: drive `(lastValueBytes, lastChangedTick)` and assert the predicate — **changed ⇒ true for one tick, false on the next** · **unchanged ⇒ never** · ⛔ **planning ⇒ never** · ⭐ **byte-equal-but-format-equal must still be FALSE, and format-equal-but-byte-different must be TRUE** *(the float-7th-digit case)* |
| ⭐⭐ **one dialog** | a reflection test: exactly **one** call site constructs the variable edit session |
| ⭐ **one commit path per mode** | planning writes JSON only; running stages only. ⛔ **No path writes both** |
| ⭐ **kind-driven fields** | a test per declaration kind asserting the dialog exposes **exactly** its storable set — ⛔ **a field with no backing member fails the test** |
| ⭐ **row-kind refusal** | 🔒 and node-owned rows: the dialog opens read-only or not at all — **proven by trying** |
| ⭐ **run-state matrix** | §5 as a table-driven test, including **replay ⇒ no dialog** |
| ⛔ **visual check required** | the table, the gestures and the budget-indicator switch are surfaces **no headless test can see drawn** |

---

## 10. Sequence

**`S5` (one picker)** → **the surgical field write** → **`C-table`** *(Details hosts the section table)* →
**`C-dialog`** *(one dialog, two scopes, kind-driven)* → **`C-watch`** *(share the renderer; fix the
compile mode)* → **`C-outline`** *(cross-host, per `D6`'s section descriptors)*.

⚠ **Still open, not blocked by this design:** the `InspectorWindow` retirement order, and whether
`W7`'s suppressible-warning design changes what the table shows for a conflicted variable.
