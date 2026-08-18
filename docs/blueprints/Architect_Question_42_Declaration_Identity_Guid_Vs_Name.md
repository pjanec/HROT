<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 5 - the recommended answers. Nothing here is built.
stale-below: nothing.
known-rot: none.
known-conflict: none. Section 3's carve-outs are the same split R-88 records; this
  document decides what to do about them, it does not disagree with them.
-->
# ⭐ Architect Question 42 — **declaration identity: `Guid` inside, free name outside?**

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable *(`2026-08-16` user ruling)*.
> ⭐⭐ **Resolved JOINTLY with the user: I analyse and RECOMMEND, the user APPROVES.**
>
> 📌 **Origin:** user, `2026-08-18` — *"cant variables be named internally as guids, and with user
> facing UI only freely changeable names?"*, following the ruling *"possibility to rename is
> mandatory"* *(`R-86`)*.
>
> ⭐⭐⭐ **This is NOT a new design.** 📐 **Blueprints already do it.** ⇒ the question is whether the AI
> hosts **converge on it**, and what the carve-outs are.

---

## 1. ⭐⭐ INVENTORY *(`R-74` — the enumeration, before the design)*

| # | query | total | what it found |
|---|---|---|---|
| ① | `search_graph(name_pattern=".*(VariableDecl\|ParameterDecl\|BlackboardVariableEntry\|RenameVariable)$")` | **35** | **4 production `RenameVariable`** *(`BehaviorTreeAsset`, `HsmAsset`, `BlueprintVariableSchemaSource`, `BlueprintLocalVariableSchemaSource`)* + 2 interfaces + 1 demo; the rest are **test stubs** |
| ② | in-degree of the identity types | — | ⭐⭐ **`BlackboardVariableEntry` = 114** · `VariableDecl` = 99 · `ParameterDecl` = 35. ⚠ **That 114 is the blast-radius number for adding an `Id`** |
| ③ | `grep '"VariableId"' *.bp.json` | **present** | blueprint `GetVariable`/`SetVariable` nodes persist **`"VariableId": "<guid>"`** |
| ④ | `Stage0_Rehydrate.FindVariableDecl` · `NodePinSchema.FindVariableDecl` | **2** | both do `Guid.TryParse(vid)` → `Declarations.Of(Variable).FirstOrDefault(d => d.Id == id)` ⇒ ⭐ **the blueprint resolves by `Id`, at BOTH the editor and the compiler stage** |
| ⑤ | `Declarations.cs` | **4 types** | `VariableDecl` · `ParameterDecl` · `EventDispatcherDecl` · `CustomEventDecl` — **every one carries `Guid Id` + `string Name`**, and `Id` **is persisted** |
| ⑥ | `BlackboardVariableEntry.cs` | **1 type** | `(Name, FieldType, Comment, IsAutoManaged, DefaultValueJson, Role, Scope)` — ⛔ **no id** |

---

## 2. 🔴 THE ASYMMETRY *(`M-16`)*

| | identity | a reference stores | rename |
|---|---|---|---|
| ⭐ **Blueprint** | `Guid Id` + `Name`, **persisted** | **`VariableId`** *(guid)* | ✅ **no reference fixup** |
| ⛔ **BTree / HSM** | `Name` only | **the name string** *(`ExpressionTargetField`, `WorkingStateTargetField`)* | ⛔ **dangles the binding — `M-15`** |

⇒ ⭐⭐ **Two identity models for one concept.** 📌 **Ruling 9 — *"no keeping two implementations for the
same concept"* — is the acceptance criterion of this whole programme**, and this is an instance of it
that predates the variable-model work rather than one it created.

---

## 3. ⭐⭐⭐ THE CARVE-OUTS — **what a `Guid` CANNOT replace** *(`R-88`)*

⛔⛔ **A `Guid` removes the INCIDENTAL name dependencies. Three are ESSENTIAL — the name is a contract
with something OUTSIDE the asset:**

| | why the name must stay |
|---|---|
| 🔴 **`Scope=Entity` shared slots** | two **different assets** meet on `FNV(variableId)`. A guid would have to be shared between them ⇒ a shared declaration, i.e. the manifest *(`R-38`)*. ⛔ **Guids in a hand-authored manifest is worse than names** |
| 🔴 **scenario / commander `JsonParams`** | a human writes `{"speed": 4}`. ⛔ `{"a3f9-…": 4}` is not authorable |
| 🔴 **the emitted C# field name** | the variable **becomes a generated struct field**, and hand-written `FourParamFull` actions address it as `bb.MyVariable` |

⭐⭐ **And blueprints themselves prove the limit:** they have the `Guid` **and**
`StructureHashComputation` still appends **`f.Name`** ⇒ a blueprint rename still moves the hash
*(`R-24`)*, because the debug/access path is **name-keyed** *(`StateFields: name → offset`)*.

> ⭐⭐⭐ **So the outcome is not "guids everywhere." It is: `Guid` = INTERNAL identity, `Name` = EXTERNAL
> contract.** ⛔ **That is not a compromise — it is the correct end state**, and it is what makes rename
> free exactly where nothing outside the asset is watching.

---

## 4. ⭐ What binds any answer

| id | binds |
|---|---|
| **`R-86`** | ✅ **user ruling: renaming is MANDATORY**; `IsAutoManaged` is a lifecycle, not ownership |
| **`R-88`** | ⭐ the name is load-bearing in **four** cases and editor-only otherwise |
| **`M-15`** | ⛔ `RenameVariable` rewrites **no** bindings today, for **any** variable |
| **`R-24`** | ⛔ anything that moves `StructureHash` hard-resets live entity state |
| **`R-23`** | ⭐ **the DOWN-MIGRATOR is the revert** once a new file version is written |
| **`R-26`** | ⛔ **implementation freeze** — ONE session builds this, across all hosts |

---

## 5. ⭐⭐⭐ THE SUB-QUESTIONS — **each with a recommended answer**

### `Q42-A` — Do the AI hosts adopt a `Guid` declaration identity?

| | option | verdict |
|---|---|---|
| **A1** | ⭐ **Yes — `BlackboardVariableEntry` gains `Guid Id`, converging on the blueprint model** | ⭐⭐⭐ **RECOMMENDED** |
| **A2** | keep names, and make `RenameVariable` rewrite every binding | ⚠ **cheaper now, and it does not compose** — ⛔ **every FUTURE binding field is a new place that must remember to participate.** 📌 This programme's own history *(three registrars forgetting one shared service, three times)* says that is the bug that recurs |
| **A3** | a third scheme *(e.g. a per-asset int handle)* | ⛔⛔ **Reject** — a third identity model for one concept is the defect being fixed |

⭐ **Why A1:** it makes rename **structurally** free rather than maintained, and it closes a ruling-9
asymmetry rather than papering over it. ⚠ **Cost is real and stated in §6.**

### `Q42-B` — What exactly becomes id-keyed in this stage?

⭐⭐⭐ **RECOMMENDED: the NODE BINDINGS only** — `ExpressionTargetField`, `WorkingStateTargetField`
*(BTree Action/Condition · HSM Transition/GlobalTransition)*, plus the alias table and the refactor
catalog key.
⛔ **NOT** the three carve-outs in §3. ⛔ **NOT** `StructureHash` *(see `Q42-E`)*.
⚠ **Keep the field NAME on the wire too** — an id-only binding is unreadable in a diff, and every
persisted blueprint reference already carries the guid **alongside** a readable name.

### `Q42-C` — Do the three carve-outs stay name-keyed?

⭐⭐⭐ **RECOMMENDED: YES, all three** — and **say so in the rename UI**. 📌 §3 is the message text: a
rename that touches a `Scope=Entity` variable, a scenario-overridden one, or *(on blueprints)* a hashed
declaration is **a contract change, not a fixup**, and gets a confirm naming which one.
⛔ **Never a silent rename there.** ⭐ **The promoted-params case gets no prompt at all** — nothing
outside the asset is watching it.

### `Q42-D` — Must names stay UNIQUE per asset once identity is a guid?

⭐⭐⭐ **RECOMMENDED: YES, unchanged.** ⚠ **The tempting answer is "no — the guid is the identity now",
and it is wrong**: the name is still the **generated C# struct field name** *(§3)*, so two variables
sharing one name is a compile error, not a UI nicety. ⭐ **Uniqueness is now enforced for a REASON that
can be stated**, rather than as a side effect of the name being the key.

### `Q42-E` — Does `StructureHash` stop including the name?

⭐⭐ **RECOMMENDED: NOT IN THIS STAGE — and do not fold it in.** 📐 The name is in the hash because the
**access path is name-keyed** *(`StateFields: name → offset`)*, so dropping it is a change to live-state
compatibility, ⛔ not a rename convenience. ⚠ **It deserves its own measurement**; ⭐ **flag it, keep the
confirm from `Q42-C` in the meantime.**

### `Q42-F` — When does `IsRenamable` flip to `true`?

⭐⭐⭐ **RECOMMENDED: only after `Q42-B` lands.** ⛔⛔ **Flipping the flag first ships the silent break
(`M-15`) to every designer at once.** ⭐ **The flag was a guard around the gap** *(`R-86`)*; it retires
when the gap does, not when the ruling is written.

---

## 6. ⚠ MIGRATION SHAPE — **stated so the cost is not discovered mid-batch**

| | |
|---|---|
| **file version** | `.btree.json` / `.hsm.json` bump; ⭐ **up-migrator assigns an `Id` per variable and rewrites bindings; down-migrator restores name-keyed bindings** *(`R-23` — the down-migrator IS the revert)* |
| ⭐⭐ **names do NOT change during migration** | ⇒ ⭐⭐⭐ **zero `StructureHash` movement, zero emit-golden movement.** ⚠ **The migration is additive; only `.btree.json`/`.hsm.json` text moves** |
| ⚠ **blast radius** | 📐 **`BlackboardVariableEntry` has in-degree 114.** ⭐ Adding an optional `Id` with a default keeps every construction site compiling; ⛔ **the risk is the sites that COMPARE by name** — those must be enumerated, not grepped |
| ⭐ **rails** | an asset whose bindings still name a variable **must fail the migration loudly**, ⛔ never fall back to name resolution — 📌 a silent fallback is how the two models would come to coexist permanently |

## 7. ⛔ OUT OF SCOPE

| ⛔ | |
|---|---|
| **the readable SEED name** *(`MoveTo_Advance_params`)* | ⭐ independent and still wanted — it is why most variables never need renaming |
| **`Q41`'s reader node** | unrelated |
| **retiring `InspectorWindow`'s default-value panel** | ⭐ becomes possible once `R-86` + rows 58/59 land; ⛔ not this |
| **HSM/BTree Details WINDOW** | 📌 sequencing row 61 owns it |
