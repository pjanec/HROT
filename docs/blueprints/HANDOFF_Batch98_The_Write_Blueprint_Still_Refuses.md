<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file — the Batch 98 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. It finishes the half of the write path Batch 96 named and
  Batch 97 did not cover, and builds the Properties dialog the user queued.
-->
# HANDOFF — Batch 98: **the write Blueprint still refuses, and the Properties dialog**

> 📌 **Dispatched at `18dfcbb25`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⚠ **If a later document INVALIDATES an item — STOP AND
> REPORT.** ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push
> `chore: started batch 98 at 18dfcbb25` FIRST.**
> ⭐⭐ **`R-106` applies: a blocked item stops THAT ITEM, never the batch.** ⭐ **Four verdicts per item.**

> ## ⭐⭐⭐ BATCH 97 WAS GOOD WORK — **this is not a correction of it**
> ⭐ All four items done, no blocks, no cascade, the gate table complete, and its `P2` probe pass found
> that its **own first offset rail covered one of two resolution arms** — ⭐⭐ **exactly the class of
> self-check this programme has been missing.** ⛔ **Nothing here reverts any of it.**
>
> ⚠ **But the user's acceptance test still fails on the FIRST click**, for a reason `97` was never asked
> to cover — **`98a`**. ⭐ **That is a scope gap of MINE, not a defect of theirs.**

---

## 1. ⛔⛔⛔ **`98a` — on Blueprint, OK still refuses while PLANNING** *(coordinator-measured `2026-08-20`)*

### 📐 The measurement

```csharp
// PerspectiveWorkspaceRegistrar:826 — the write target
private static IBlackboardManagedAsset? DeclarationOwnerOf(EditorSelectionStore store, VariableRow row)
    => store.ActiveAsset is IBlackboardManagedAsset asset          // ⛔ BlueprintAsset is NOT one
    && store.ActiveAsset.AssetId == row.Origin.AssetId ? asset : null;

// VariableEditCommit:149
if (asset is null) return Outcome.RefusedNoDeclarationOwner;
```

⇒ ⭐⭐⭐ **In the ordinary authoring state — PLANNING — the target is the INITIAL value**
*(`TargetFor(runState)`)*, `CommitInitialValue` asks for the owner, and on **Blueprint** the owner is
**always `null`.** ⛔ **So OK refuses, on every Blueprint variable, every time.**

### ⭐⭐ Why this survived four batches — **and the asymmetry is in the SAME FILE**

| line | |
|---|---|
| **`:836`** `ResolveEntry` | ⭐⭐ **asks the ROW first** — `if (row.ReadDeclaration?.Invoke() is { } carried) return carried;` ⇒ **`95a`'s fix** |
| **`:826`** `DeclarationOwnerOf` | ⛔ **does NOT** — it still type-tests `store.ActiveAsset` |

📌 **`95a` fixed the vocabulary mismatch for READING and left it open for WRITING**, and `BP-355` says
so in as many words: *"Blueprint still resolves to `null` — the write target is typed
`IBlackboardManagedAsset` and `BlueprintAsset` is not one, the same vocabulary mismatch `95a` fixed for
READING."* ⭐ **Batch 96 named it and it was never given to anyone as an item. That is my miss.**

### ⭐⭐⭐ What to build — ⛔ **and the row already carries the answer for READING, not for WRITING**

⚠⚠ **Do not simply mirror `95a`.** ⭐ A row carries its **declaration** *(a value)*; a commit needs
somewhere to **PUT** the new JSON. ⇒ ⭐⭐ **the missing thing is a WRITE-BACK seam, not a lookup.**

| ⭐ | |
|---|---|
| **①** | ⭐⭐ **MEASURE what actually persists a blueprint declaration's `DefaultValueJson`** — a `BlueprintAsset` has its own vocabulary *(`VariableDecl`/`ParameterDecl`, `Guid Id`)*. ⛔ **Do not widen `IBlackboardManagedAsset`** to swallow it; that interface is the AI blackboard's and `R-108`/`95a` both treat the two vocabularies as genuinely different |
| **②** | ⭐ **the seam should be host-supplied, the same route `writeLive` and `liveValueProvider` take** — ⛔ not a new interface on the row |
| **③** | ⚠ **If a blueprint declaration has NO write-back path at all**, that is a **capability**, not a wire ⇒ ⭐ **report it as one and move to `98b`** *(`R-106`)* — ⛔ do not invent a persistence route |
| ⭐⭐ **the rail** | **construct the production registrar, raise the gesture, press OK, and assert the DECLARATION'S JSON CHANGED** — ⛔ not that a delegate is non-null. 📌 `M-22`. ⭐ And say which layer is faked *(`M-29`)* |

### ⚠ Two limits the user must be told about, and they are CORRECT — ⛔ do not "fix" them

| ⭐ | |
|---|---|
| **live write is `AiPrimitive`-only** | 📐 `ResolveWorkingStateField:960` refuses any other dispatch kind. ⭐ **Right**: an `Instance` blueprint's fields are offset **within a per-blueprint slot** of `BlueprintBlackboard1024/4096/16384` *(`:1435`–`:1451`)*, a different address space from `AiPrimitive`'s flat `Blackboard1024` *(`:1386`)*. 📌 `Q32` §2.1 — guessing that arithmetic corrupts memory |
| **BTree/HSM live write** | `BP-364` — ⭐ a **capability**, and their `writeLive` is `null` **deliberately** |

---

## 2. 🛠 **`98b` — the Properties dialog** *(`BP-359`; the user queued it on `2026-08-19`)*

> ⭐⭐ **User:** *"the 'Properties' context menu now opens the same 'Edit variable' modal as 'Edit'. This
> is wrong."* … *"ok so you please add the properties dialog to next batch once 97 returns."*

⭐⭐⭐ **The answer is SETTLED — 📌 `R-108`. ⛔ Do not re-derive it.**

### 📐 What it opens

📄 **`DESIGN_Variable_Details_And_Editing.md:233`** — *"'Properties…' | **a properties object for that
declaration kind** | `WholeComponent`"*
⇒ ⭐⭐ **the two menu items differ by the OBJECT, ⛔ not by the scope.** Both correctly use
`WholeComponent` since `96b`, which is exactly why they now look identical.
⚠ **The design's own summary line — *"the two menu items ARE the two scopes"* — is FALSE**, and Batch 96
measured it so *(`BP-359`)*.

### ⭐ What it shows — **already enumerated in code**

`VariablePropertySchema.For(kind)`, measured off the carriers rather than taken from a spec:

| carrier | properties |
|---|---|
| **`VariableDecl`** | Name · Type · DefaultValue · Tooltip · Comment · Category · IsEditable · IsExposedOnSpawn |
| **`ParameterDecl`** | Name · Type · DefaultValue · Tooltip · Comment |
| **`BlackboardVariableEntry`** | Name · Type · DefaultValue · Comment |

⛔ **`Role`/`Scope` is NOT a property** — *the SECTION is the classification* *(user, `2026-08-16`)*.
⛔ **Replication and Range are excluded** — **no carrier has a backing member**, and the schema's own
rail fails a property that cannot be stored.

### ⭐ Availability *(the design's matrix)*

| | planning | running / paused | replay |
|---|---|---|---|
| **Properties…** | ✔ **editable** | ⚠ **read-only** — *"you cannot retype a variable mid-run"* | ⛔ read-only |

⭐ **`97b` built `VariableEditGesture.Decide` over `VariableEditPolicy`** ⇒ ⭐⭐ **the greying is already
there; feed it, ⛔ do not write a second matrix** *(ruling 9)*.

### ⚠ Two real costs — ⭐ **and the third one you may have read is STALE**

| ⭐ | |
|---|---|
| **`Type` picker · `Category` combo** | registered as **StructEdit custom editors** — ⭐ the mechanism exists *(`_fieldEditors`, `ICustomFieldEditor`)* |
| ⭐⭐⭐ **`Name` IS A RENAME** | ✅ safe on **Blueprint** — declarations carry a persisted `Guid Id` and references store `VariableId` *(`M-16`)*. ⛔⛔ **On BTree/HSM the binding stores the NAME STRING and `RenameVariable` does NOT fix up `ExpressionTargetField`** *(`M-15`)* ⇒ **renaming a bound AI variable DANGLES it** *(caught at build as `BTREE0002`, a whole-asset skip)* ⇒ ⭐ **it MUST run the refactor service**, which the design already requires of both routes |
| ⛔ ~~`S5` must land first~~ | ⭐⭐ **STALE — `S5` shipped in Batch 65** *(`BP-255`)*: `BuildSelectableTypeIds` is seeded from `EditorOfferableTypeIds` ∪ `Entity` ∪ discovered structs and `BlueprintTypeChoices.TypeIds` is **that same list**, `Assert.Same`-locked. ⇒ ⭐ **offer `Type` from the start** |

⚠ **If the `Type` picker turns out to need more than registering an editor, ⭐ ship Properties with
`Type` greyed and its reason, and report it** — ⛔ **do not hold the whole dialog for one field**
*(`R-106`)*.

---

## 3. 🛠 **`98c` — the outline's dead Watch entry** *(`BP-360`)* — ⭐ small, and it has waited two batches

📐 `MyBlueprintContextMenu:40` enables on `commands.Get("editor.toggle-variable-watch") is not null`, and
**nothing registers that command** ⇒ Batch 94's *"ONE command, TWO entry points"* is **half true**: the
Details-table entry is wired, the outline entry is drawn and dead.
⭐ It refuses honestly rather than dead-ending ⇒ **a gap, not a trap** — ⛔ **last, and droppable.**

---

## 4. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **widening `IBlackboardManagedAsset` to cover `BlueprintAsset`** | `98a` — two genuinely different vocabularies; `95a` and `R-108` both keep them apart |
| **an `Instance`-blueprint live write** | ⭐ a per-blueprint slot in a tiered component — 📌 `Q32` §2.1, guessing corrupts memory |
| **a BTree/HSM live writer** | `BP-364` — a capability, and the refusal is honest today |
| **a second editability matrix** | `98b` — ⭐ `97b`'s `VariableEditGesture.Decide` already exists |
| **`Role`/`Scope` in the Properties dialog** | user ruling `2026-08-16` — the section is the classification |
| **reverting anything from Batch 97** | ⭐ all four items hold |

---

## 5. ⭐ GATES

⭐ **Baseline** = Batch 97's table, base sha **`18dfcbb25`**: AiShared **1705** · BTree.Editor **622** ·
Hsm.Editor **554** · Blueprints **3814 / 0 / 10 skip** · Hrot.Editor **201** · Breakpoints **143** ·
Generators **277** · Persistence **143** · NodeEditor.Core **211** · NodeEditor.UI **135** · Fhsm **300** ·
StructEdit **191 / 1** *(⚠ `BP-363`, pre-existing)* · Fdp.Presentation **146 filtered** ·
tracker **open 78 / done 221** · rulings **73/73**.

⭐ **Batch 97's table was the best yet — keep its shape**: `--no-build` column, `EXIT=` unfiltered, the
diff-shape golden row, revert-goes-red per item, and the *"whose object · which layer is faked"* table.
⚠ **`StructEdit.Tests` carries one pre-existing RED** *(`BP-363` — `R-104`'s cycle fence missing from
StructEdit's own builder)*; ⭐ **confirm it against `18dfcbb25`, do not fix it here unless `98a`/`98b`
finish early.**

---

## 6. ⭐⭐ WHAT THE USER DOES NEXT

⭐ **They re-run the acceptance test:** open `Count4` → right-click `Count` → **"Edit value…"** → type →
**OK** → ⭐⭐ **the value changes.** ⚠ **In PLANNING that is `98a`.** ⭐ While **paused** on an
`AiPrimitive` blueprint, `97c` already lands it.
⚠ **Expected, not findings:** an `Instance` blueprint refuses a LIVE edit *(correct)* · BTree/HSM refuse
a live edit and say why *(`BP-364`)* · a pin does not survive a scenario reload *(`94g`)*.
