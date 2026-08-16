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

### ⭐⭐⭐ 1a. The control is a GENERIC ROW LIST, fed by sources *(user ruling)*

> ⭐ **"the watch window must allow for selected variables from different assets."**

⛔⛔ **Therefore the table is NOT "the view of one asset".** ⭐ **It renders an
`IReadOnlyList<VariableRow>` and knows nothing about where the rows came from.**

| source | produces | homogeneous? |
|---|---|---|
| **`SectionSource(asset, section)`** — Details | every row of that section | ✔ one asset |
| **`PinnedSource(rowIds)`** — Watch | ⭐ **rows from ARBITRARY assets and entities, mixed** | ⛔ **no** |

⇒ ⭐⭐ **Every row is SELF-DESCRIBING.** The row carries its own identity and its own accessors; the
panel never reaches back to "the asset" because in Watch there is no single asset.

```
VariableRow
  Origin      : (AssetId, Entity, Section, VariablePath)   ← identity; Entity is part of it
  DisplayName : string        ← ⭐ the SOURCE decides qualification (below)
  TypeText    : string
  ClrType     : Type
  ReadValue   : () -> ReadOnlySpan<byte>   ← raw, for both display and change-diff
  AssetTick   : () -> uint    ← ⭐ THIS row's asset tick (§4a)
  RowKind     : Normal | ReadOnlyPassthrough | NodeOwned
  IsStale     : bool          ← asset closed / entity gone; ⭐ Watch already has this concept
```

### ⭐⭐ Name qualification is the SOURCE's job, not the table's

⛔ **In a heterogeneous list `Health` is ambiguous** — two assets can both declare it. ⛔ **But we are
NOT adding a fourth column.** ⇒ ⭐ **the source supplies `DisplayName`:**

| | |
|---|---|
| **Details** | the **short** name — unambiguous within one section |
| **Watch** | a **qualified** name — `Asset.Variable`, plus the entity when the same asset is watched on more than one | ⭐ **full path in the tooltip either way** |

⭐⭐ **One column, two contents, zero special-casing inside the control.**

### ⚠ Consequences that fall out of heterogeneity

| | |
|---|---|
| ⭐ **entity is part of row identity** | the same asset on two entities has **two different values** ⇒ the key is `(AssetId, Entity, VariablePath)`, ⛔ **not `(asset, variable)`** |
| ⭐⭐ **the tick is PER ROW** | in Watch, rows tick at **different rates** — each row diffs against **its own** asset tick (§4a) ⇒ ⛔ **no panel-wide tick** |
| ⭐ **stale rows** | a Watch row outlives its asset or entity. ⭐ **`Watch.IsStale` already exists — reuse it**; a stale row shows its last value, greyed, and its dialog is refused |
| ✅ **"Edit…" still resolves** | the row knows its own asset and entity, so the dialog needs no ambient context |

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

### ⭐⭐⭐ The unit of change — **the asset's own tick** *(user ruling)*

> ⭐ **"a non-frozen CGF behavior tick, i.e. the asset tick/update call."**

⛔ **NOT the rendered frame. NOT the world tick.** ⭐ **The counter advances only when THAT ASSET's
tick/update actually runs, and only when it is not frozen.**

| consequence | why it is right |
|---|---|
| ⭐⭐ **paused on a breakpoint ⇒ the highlight PERSISTS** | behaviours do not tick while frozen *(`dt == 0`)*, so nothing has happened. **It clears when you actually Step** — which is precisely the VS behaviour |
| ⭐ **an asset that is not scheduled keeps its highlight** | ⛔ **correct — the value has not had a chance to change.** A world-tick counter would wrongly clear it |
| ⭐ **the counter is PER ASSET INSTANCE** | ⛔ **not global** — different behaviours tick at different rates and tiers |

| | |
|---|---|
| ⭐⭐ **compare RAW BYTES, not the formatted string** | ⛔ **a formatted value hides change** — a `float` moving in its 7th digit renders identically. **Diff the byte slice** |
| ⭐ **state to keep** | per row: `(lastValueBytes, lastChangedAssetTick)`. **Highlight while `currentAssetTick == lastChangedAssetTick`** ⇒ ⭐ **a pure predicate, therefore HEADLESSLY TESTABLE** even though the colour is not |
| ⭐ **who owns it** | ⛔ **not the breakpoint snapshots** — `_preTickSnapshot`/`_postTickSnapshot` exist only while the debugger is engaged, and the monitor must work when it is not. ⇒ **the shared row renderer owns the cache**, keyed by ⭐ **`(AssetId, Entity, VariablePath)`** *(§1a — entity is part of identity)*, covering the whole list so scrolling does not reset it |
| ⭐⭐ **the tick is PER ROW** | ⛔ **there is no panel-wide tick** — a Watch list mixes assets that tick at different rates ⇒ **each row diffs against `row.AssetTick()`** |
| ⚠ **a value that changes every tick stays highlighted** | ⭐ **correct** — VS behaves the same. ⛔ **No damping**; it would hide genuine churn |
| ⚠ **struct rows** | diff the whole slice; **the whole cell** highlights. No per-field highlighting in the table — that is the dialog's job |

### ⭐ Colours — two distinct states

| state | colour | meaning |
|---|---|---|
| **changed** | 🔴 **red**, one asset tick | ⭐ **the SIM changed it** |
| **pending** | 🟡 **yellow**, until it lands | ⭐ **YOUR optimistic edit has not been applied yet** *(§6)* — clears when the staged write lands at the N+1 boundary |
| unchanged | normal | |

⛔ **Never the same colour** — otherwise *"the sim changed this"* and *"my edit has not landed"* are
indistinguishable, which is the one thing a monitor must not do.

### 📐 Carriers — **what exists, and what must be verified**

| | |
|---|---|
| ✅ **the Watch row already carries the state** | `IBlueprintDebugSession.Watch` has **`LastValueBytes`** *(raw!)*, **`LastUpdateTick`**, `UpdateCount` and `HasEverBeenWritten`, and `WriteValue<T>(value, self, tick)` already threads a tick in ⇒ ⭐ **on the Watch side the predicate is nearly free** |
| 🔴 **but the buffer is 64 bytes** | `Watch._valueBuffer = new byte[64]` and `WriteValue` **throws above 64** ⇒ ⛔ **`MemberSlotList` (96), `WaveState` (104), `HillAttackSharedState` (136) cannot go through it.** 📐 **The shared row renderer must not inherit that limit** |
| ⚠ **`OnNewTick()` exists on the debug session** | 📐 **VERIFY whether it fires per WORLD tick or per ASSET tick** — ⛔ **the ruling needs the asset's own tick, and I have not confirmed which this is** |
| ⚠ **BTree/HSM need the equivalent** | `BTreeFacets.TickCount` exists **but `BTreeFacetMapper:170/191` sets it to `0`** ⇒ 📐 **trap #5 shape — verify it is populated before relying on it** |

---

## 5. ⭐ Run state governs everything

| | planning | running / paused | replay |
|---|---|---|---|
| Value column | the **initial** value | the **current** value | current, ⛔ **read-only** |
| ⭐ **change highlight** | ⛔ **none** | ✔ 🔴 **red, one ASSET tick** · 🟡 **yellow while pending** | ✔ 🔴 **red, one asset tick** *(no pending — no edits)* |
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
| ⭐⭐ **three columns** | a test asserting the table exposes **exactly** `Name`, `Type`, `Value` — ⛔ **a fourth column fails it**, including for a heterogeneous list |
| ⭐⭐⭐ **heterogeneous source** | ⭐ **feed the control rows from TWO DIFFERENT assets AND the same asset on two entities**, and assert: distinct identities · **independent** highlight state · qualified `DisplayName` from the source · a stale row renders and refuses its dialog. ⛔ **This is the test that stops the control from quietly assuming one asset** |
| ⭐⭐ **change highlight** | ⭐ **headless**: drive `(lastValueBytes, lastChangedAssetTick)` and assert the predicate — **changed ⇒ true for one ASSET tick, false on the next** · **unchanged ⇒ never** · ⛔ **planning ⇒ never** · ⭐ **format-equal but byte-different must be TRUE** *(the float-7th-digit case)* · ⭐⭐ **frozen: N world frames with NO asset tick ⇒ STILL TRUE** *(the ruling's whole point)* · ⭐ **pending and changed must be DISTINGUISHABLE states, not one flag** |
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
