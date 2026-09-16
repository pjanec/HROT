# HANDOFF — Batch 83: **rows `58` → `59` → `59b`** · ⭐⭐ **LONG UNATTENDED RUN**

> 📌 **Dispatched at `14881c13f`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ **Documents that change after it are FYI ONLY.**
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT. ⛔ Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push `chore: started batch 83 at <sha>` FIRST.**
>
> ⭐⭐⭐ **THIS IS AN OVERNIGHT RUN WITH NOBODY WATCHING.** ⛔ **You cannot ask a question.**
> ⭐ **Every fork below is pre-decided or is an explicit STOP.** §1 is the protocol — **read it first.**

---

## 0. ⭐⭐ Design basis — **per item, and the queue is not mine to reorder**

📌 **`Q32_…_ANSWERS.md` §4 is the MASTER sequencing table** *(`R-22`)*. **Verbatim:**

| row | verbatim | this batch |
|---|---|---|
| **58** | *"the Value column: mode switch, read-only, pretty-printed tooltip (rulings 3-4) + blueprint's `ILiveValueProvider` and `UpdateVariableDefaultValueJson`"* — *needs 57's host* | ⭐ **item 1** |
| **59** | *"the StructEdit dialog — **three-dot button AND double-click** (rulings 5, 10) + the **not-running** write (ruling 7, half)"* — *needs 58's column* | ⭐ **item 2** |
| **59b** | *"the Watch panel: make `HandlePinValueChanged` real · EDITING through the same dialog · show NOTHING before the run (rulings 11, 13)"* | ⭐ **item 3** |

⭐ **57 landed in Batch 82** ⇒ **58's precondition is met.** ⭐ **Each item feeds the next**, which is
exactly why three rows are safe to chain in one long run and why the ORDER is fixed.

### ⚠⚠ Two corrections to the record, made `2026-08-17` — **read these, they change premises**

| ⛔ what older documents say | ⭐ the truth, measured |
|---|---|
| *"`S5` must land before the type picker"* | ✅ **`S5` LANDED — Batch 65 (`BP-255`).** `SelectableTypeIds` ∪ discovered structs, ⭐ **`Assert.Same`-locked** to `BlueprintTypeChoices.TypeIds`. ⛔ **Do not build a second offerable list** |
| *"stage `B′` is BLOCKED on `BP-228`"* | ✅ **`BP-228` closed Batch 47; `B′` done in Batch 65.** ⭐ **`R-61`: stage `D` is the ONLY unification work left** |
| *"the emitters still emit `Variable` and `WorkingState` separately"* | ⛔⛔ **FALSE — the coordinator said this and was wrong.** ⭐ `IrAsset.StateDeclarations` = `WorkingState ∪ Variables` ships *(Batch 56, `BP-244`)*; both struct emitters, `CSharpEmitter` and `FieldLayout` walk it |

---

## 1. ⭐⭐⭐ THE LONG-RUN PROTOCOL — **binding, and it outranks finishing**

| # | rule |
|---|---|
| **1** | ⭐⭐⭐ **ONE COMMIT PER ITEM, and the FULL GATE SET before each commit.** ⛔ **Never batch three items into one commit** — if item 3 goes wrong overnight I must be able to keep 1 and 2 |
| **2** | ⭐⭐ **A red gate STOPS THE RUN.** ⛔ **Do not "fix forward" past a red into the next item.** ⭐ Land what is green, write the report, stop |
| **3** | ⭐⭐⭐ **NEVER leave the tree red or the working copy dirty at the end.** ⭐ **A partial batch that is green is a GOOD outcome.** ⛔ **A complete batch that is red is a bad one** |
| **4** | ⭐⭐ **If a MEASURE-FIRST finds the premise false — STOP that item, keep the earlier ones, report.** ⛔ **Do NOT redesign unattended.** 📌 Batch 82 did exactly this and it was right |
| **5** | ⭐ **Push after every item**, not once at the end — ⛔ an overnight container can die |
| **6** | ⭐⭐ **Time/again budget: if an item has taken more than ~2 hours or 3 failed attempts, STOP IT and move to the report.** ⛔ **Do not start an item you cannot finish and gate** |
| **7** | ⭐ **Report every item you did NOT reach**, with why. ⛔ **Silence reads as "done"** |
| **8** | ⛔⛔ **NO VISUAL CHECK, and no claim that anything "renders correctly."** ⭐ **You cannot see the screen. Assert through rails only** |

---

## 2. 🔴 ITEM 1 — row `58`, **the Value column**

### ⭐ Design basis *(verbatim)*

| ruling | |
|---|---|
| **3** | ⭐⭐ *"ONE Value column, meaning switched by run state — **initial** when not running, **current** when running or paused, across live / replay / preview"* · ⛔ *"the coordinator argued two columns and is **overruled**"* |
| **4** | *"Value is **READ-ONLY in the cell**. Tooltip shows it **full size and pretty-printed** (structs)"* |
| **§4b** *(`DESIGN_Variable_Details_And_Editing.md`)* | ⭐ *"One line, never wrapping, never growing the row"* — primitive **inline formatted**; struct ⭐ **`{X=1.0, Y=2.0, …}` elided**, tooltip pretty-printed one field per line; fixed list **`{Count=3: 1, 2, 3}`**; ⭐ **stale row = last value, greyed** |

### 🛠 What to build

1. ⭐ **The mode switch** — one place that answers *"initial or current?"*, driven by run state.
   ⛔ **Not a bool per call site.** ⭐ **`IEngineDebugTimeController.IsPausedByDebugger` and the debug
   session registry already exist** — ⛔ **do not coin a second notion of "running."**
2. ⭐ **Blueprint's `ILiveValueProvider`** — the running arm. ⚠ **MEASURE whether one exists already**
   *(the interface name is named by the sequencing row, so it may be a name, not a thing)*.
   ⛔ **If it does not exist, build the smallest one that serves the row's `ReadValue`** — 📌 the row
   already carries `ReadValue : () -> ReadOnlySpan<byte>`, so **the provider feeds the row, not the panel.**
3. ⭐ **`UpdateVariableDefaultValueJson`** — the planning arm's READ side for this item
   *(the WRITE lands in item 2)*. ⭐ **`DefaultLiteral` already owns JSON→literal conversion** *(`BP-247`)*
   — ⛔ **do not write a second converter.**
4. ⭐ **Rendering** per §4b, decoded by **`RawValueDecoder`** *(exists, Track C)*.
   ⛔ **NEVER raw hex** — 📌 `BP-01`'s original symptom; `S3` gave the decoder its struct arm.

### ⭐⭐ The honesty rule this item must not break

📌 **Batch 82 fixed `SectionVariableRowSource` to report `HasEverBeenWritten: reader != null`** ⇒ an
authored-but-never-run row shows **`(pending)`**, ⛔ **not `<unreadable>`, which would claim a decode
failure that never happened.** ⭐ **Item 1 must preserve that distinction** — *"no value yet"* and
*"could not decode"* are **different cells**, and a rail must say so.

### ⛔ Out of this item
⛔ **No write path at all** *(item 2)* · ⛔ **no three-dot button** *(item 2)* · ⛔ **no Watch changes**
*(item 3)*.

---

## 3. 🔴 ITEM 2 — row `59`, **the StructEdit dialog + the NOT-RUNNING write**

### ⭐ Design basis *(verbatim)*

| ruling | |
|---|---|
| **5** | *"A **three-dot button** right of the value opens a **StructEdit-based editing window**, OK / Cancel, initialised to the variable's current value"* — ⭐ *"promoted from vectors only to **everything**"* |
| **10** | *"**Reuse** the existing StructEdit generic value-editing dialog"* |
| **7** | ⭐ *"Write target follows run state: running ⇒ writes the **live blackboard**; not running ⇒ writes the **initial value in JSON**"* — ⛔ **only the NOT-RUNNING half is in scope** |
| **§3–§4** *(design doc)* | ⭐⭐ **ONE dialog, TWO SCOPES, and the USER picks:** **"Edit value…"** ⇒ `ForField` · **"Properties…"** ⇒ `WholeComponent`. ⭐ **Gestures: `⋮` menu on BOTH the My Blueprint row and the table row · double-click the VALUE cell ⇒ value · double-click the NAME cell/row ⇒ properties · F2 ⇒ rename** |

### ⚠⚠ MEASURE FIRST — **ruling 10 names TWO StructEdit stacks and the design says MEASURE, not assume**

📌 **`Q32_…_ANSWERS.md` ruling 10, verbatim:**
> *"⛔ **Are these two implementations of one concept (ruling 9's target), or two different jobs that
> look alike?** ⚖️ **Coordinator lean: build the dialog on the FDP-level `IComponentEditService`** — it
> is the one already shared beyond blueprints"*

| candidate | where |
|---|---|
| ⭐ **`IComponentEditService`** *(StructEdit.Core)* — driven by `StructInspectorProjector.cs`; already ships `WholeComponent` / `ForField(path)` / `ForFields(...)` | shared beyond blueprints |
| ⚠ **the blueprint-local one** — `Hrot.Blueprints.Editor/Inspector/` : `IStructEditDrawer<T>` · `DrawerRegistry` · `PrimitiveDrawers`, consumed by `InspectorWindow` **and** `BlueprintDetailsWindow` | blueprint only |

> ⭐⭐⭐ **THE PRE-DECIDED FORK — so you need not ask:**
> ✅ **Build on `IComponentEditService`** *(the lean, and ruling 9 favours the shared one)* **IF** it can
> be driven from the editor with a boxed value + CLR type and no ECS entity.
> ⛔ **IF it structurally requires an ECS component/entity** — **STOP ITEM 2, keep item 1, report the
> measurement.** ⭐ **Do NOT fall back to the blueprint-local stack silently**: that choice cements the
> duplicate ruling 9 exists to remove, and it is a design call, not an implementation one.

### 🛠 What to build *(once the fork resolves ✅)*

1. ⭐ **The two menu items** = the two scopes. ⛔ **ONE dialog implementation** — same `IEditSession`
   lifecycle, same OK/Cancel, differing **only by the `EditScope` argument** *(ruling 9)*.
2. ⭐ **Availability by run state**, per the design's table: **Properties…** fully editable in planning,
   ⚠ **read-only when running/paused** *("you cannot retype a variable mid-run")*, ⛔ replay read-only.
3. ⭐ **The NOT-RUNNING write only** ⇒ `DefaultValueJson`, through `DefaultLiteral`'s existing contract.
   ⚠ **`0` means "leave it zero-initialised", for EVERY type** — 📌 `BP-247`'s hard-won uniform rule;
   ⛔ **do not special-case it back.**
4. ⭐ **Rename stays on F2 and in the menu**, and **`Properties…` carries `Name`** ⇒ ⛔ **both routes must
   run the refactor-rename service.** *(A second rename path is ruling 9's prohibition.)*
5. ⭐ **The type picker is `BlueprintTypeChoices.TypeIds`** — ⛔ **`S5` already made it one list.**

### ⛔⛔ HARD STOPS in this item

| ⛔ | why |
|---|---|
| **the RUNNING write** | 📌 **row `59c`** — it needs the **ECB surgical field write** first *(ruling 14)*. ⭐ *"the whole-component route is not merely unsafe, it **exceeds `MaxComponentSize`** and cannot work"* |
| **`Role` / `Scope` controls** | ⛔⛔ **NOT A PROPERTY AT ALL** — ⭐ **the SECTION is the classification** *(`2026-08-16` ruling)*. Not in the dialog, not a column, not on any host |
| **merging Variables with Working State** | ⭐ **`R-59`: that is stage `D`**, own batch, JSON migration. ⛔ **Not a convenience fix here** |

---

## 4. 🔴 ITEM 3 — row `59b`, **the Watch panel becomes real**

### ⭐ Design basis
📌 **Row `59b` verbatim:** *"make `HandlePinValueChanged` real · **EDITING through the same dialog** ·
**show NOTHING before the run**"* · 📌 **ruling 11:** ⭐⭐ *"the runtime value change is the same mechanism
the Watch panel should provide — **SHARE it**."*

### 🛠 What to build

1. ⭐⭐ **`HandlePinValueChanged` real** — ⛔ **through item 2's dialog, not a second editor.**
   📌 **Ruling 11 is explicitly a SHARING instruction**; a Watch-local editor fails ruling 9.
2. ⭐ **"Show NOTHING before the run"** — ⚠ **this is a REFUSAL, and it must be VISIBLE.**
   📌 The user's `2026-08-17` ruling: ⭐ *"disabling/graying with an explanatory tooltip is better than
   allowing the click and then saying it is not possible — **same information value, no false
   expectations**."* ⇒ ⭐⭐ **grey it and explain; ⛔ never a click that dead-ends.**
3. ⭐ **Watch already has the concepts it needs** — `IsStale`, the pinned set, and per-row `AssetTick`.
   ⛔ **Do not add a panel-wide tick** — 📌 *"in Watch, rows tick at different rates."*

### ⚠ The one thing to leave alone
⛔ **Ruling 12's immediacy gate** *("visible in BOTH panels within one frame while frozen")* is
**`59c`'s acceptance criterion**, not this item's — it runs through the RUNNING write. ⭐ **Note in your
report whether the shared handler makes it reachable.**

---

## 5. ⛔ EXPLICITLY OUT OF SCOPE — **each with its owner**

| ⛔ not here | owner |
|---|---|
| the **ECB surgical field write** + the running write | **`59c`** — `Fdp.Core`, additive, bounds-checked, ⭐ **its own red-first batch** |
| **retiring** any Variables window | **`60` = `U-16`** — ⛔ gated on Details being *proven*, and ⚠ **`R-60`: BTree/HSM have no Details window at all** |
| the **shared cross-host outline** | **`61`** |
| **stage `D1`–`D4`** *(one declaration list)* | ⛔⛔ **its own batch.** 🔴🔴 **`R-24`: `D2` must preserve field order or EVERY DEPLOYED BLACKBOARD IS WIPED** |
| a **BTree/HSM Details host** | **`BP-317`**, pointed at row `61` |

---

## 6. ⭐ Gates — **the rule 8 contract, all seven rows, PER ITEM**

⭐⭐ **Your report substitutes for my run.** ⛔ **A missing row sends me to the terminal.**
⚠ **On a long run, report the gate table ONCE PER ITEM COMMIT** — ⛔ a single end-of-run table cannot
tell me which item moved which number.

| # | report |
|---|---|
| **1** | verbatim command · pass/fail/skip · **Δ vs baseline** |
| **2** | ⭐⭐ **the `--no-build` column.** ⛔ **`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` take NO `--no-build`** |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE** |
| **4** | ⭐ **every RED confirmed pre-existing against the base sha**, named |
| **5** | ⭐ **working tree CLEAN after every suite run** |
| **6** | ⭐ **both quarantine counts** — ⛔ **a new skip is a finding, not a fix** |
| **7** | ⭐ **`tracker-counts.py --check`** · ⭐ **`rulings-check.py`** · **every id you allocated** |

⭐ **Baseline** *(Batch 82)*: build **0/69** · AiShared **1330** · Blueprints **3727/3737/10** ·
BTree.Editor **615** · Hsm.Editor **551** · Generators **270** · Breakpoints **134** · Persistence
**136** · Scenarios **56/68 (12 skipped)** · UrbanCombat **29** · Toolkits **1964** · NodeEditor.Core
**211** · NodeEditor.UI **135** · FastHSM **300** · tracker **open 66 / done 187** · rulings **40/40**.
⛔ **`Fdp.Toolkits.Tests` = `DEBT-AIB-030`** — identity rotates between runs; confirm by `--filter`.

⭐⭐ **`StructureHash` must not move in ANY item.** ⛔ **All three items are editor-side.** 📌 If a
golden or `persistence-shape.txt` moves, **that is a STOP**, not a regeneration.

---

## 7. ⭐⭐ FYI — **not your work, and it does not change any item above**

⭐ **`R-62`: the visual-check suspension's condition is now MET for Blueprint** — *Details panel
implemented* ✅ *(82)* **and** *emitters/access unified* ✅ *(56 + stage `C`)*. ⛔ **Still not met for
BTree/HSM** *(`R-60`)*. ⇒ ⭐ **the user may run a visual check on Blueprint after this batch** — which is
why item 3's *"grey it and explain"* matters more than usual: **it is about to be looked at.**
