# Architect Question #39 — **the `Variable` / `WorkingState` unification is UNFINISHED INFRASTRUCTURE**

> ⭐⭐⭐ **User, `2026-08-17`, verbatim:** *"variables **ARE** working state, there is **NO distinction**
> and if there is it is very likely wrong old not yet updated implementations… it should **not be just
> UI unification, it must be infrastructure unification as well**. So **there is no merging needed, it
> needs to be already merged in the infrastructure**. Same for BTree/HSM."*
>
> ⭐⭐⭐ **THE USER IS RIGHT, AND THE DESIGN SAYS IT IN THOSE WORDS.** ⛔⛔ **This question was
> originally written as *"should the outline show one section?"* — ⚠ **that was a UI question about an
> INFRASTRUCTURE gap, and it was the wrong question.** ⭐ **Rewritten `2026-08-17`.**

---

## 1. ⭐⭐⭐ The design already ruled it — **verbatim, and it is unambiguous**

> 📌 **`Variable_Model_Unification.md` §2:**
> *"⇒ ⭐ **`WorkingState` and `Variables` occupy the SAME cell.** Both are private, persistent,
> per-entity storage carrying the suspension bookkeeping (`Cursor` / `__phase`) plus the designer's
> fields. **Two names, one concept**, held apart only by a diagnostic."*

📐 **The mapping table, from that document:**

| today | Role | Scope | dispatch |
|---|---|---|---|
| `BlueprintAsset.Parameters` | **`Input`** | `Asset` | AiPrimitive |
| `BlueprintAsset.WorkingState` | ⭐ **`State`** | ⭐ **`Asset`** | AiPrimitive |
| `BlueprintAsset.Variables` | ⭐ **`State`** | ⭐ **`Asset`** | Instance |

⇒ ⭐⭐ **Identical `(Role, Scope)`.** ⛔ **The ONLY thing separating them is which `Dispatch` the asset
uses** — and the design says the tag therefore **buys nothing**:

> 📌 *"for that cell the tag carries **no information `Dispatch` did not already carry**."*
> 📌 **And `BP1024`** *("AiPrimitive uses parameters and workingState, not variables")* — *"⭐
> **disappears** — it only ever said **'call it `WorkingState`'**."*

⇒ ⭐⭐⭐ **There is nothing to decide about whether they are the same. That is settled and has been.**

---

## 2. 🔴🔴 So why does the UI still show two? — **because the unification STOPPED HALF-WAY**

⭐ **It was planned as four stages.** 📐 **Measured status `2026-08-17`:**

| stage | what | status |
|---|---|---|
| **A** | `Variables` becomes a third `IVariablesSchemaSource`; kill `bool isParams` | ✅ **DONE** *(`U-4`/`U-5`)* |
| **C** | `FindVariableIndex` → `(kind, index)`; `VarFieldName` switches on it | ✅ **DONE** *(`U-3` closed `BP-226`; `VariableRef(VariableKind, int)`)* |
| 🔴 **B** | **My Blueprint's Variables section projects THAT source instead of its own** | ⛔⛔ **NOT DONE** |
| 🔴 **D** | ⭐⭐⭐ **ONE declaration list with `Role`/`Scope`** | ⛔⛔ **NOT DONE** — *"the only risky stage"* |

### ⭐⭐ `B` is exactly what the user is looking at

> 📌 **`Variable_Model_Unification.md`, verbatim:** *"⛔ **`Variables` does NOT** [plug into
> `IVariablesSchemaSource`] — it has a **separate path** via `BlueprintMyBlueprintModel` →
> `MyBlueprintItem`. ⭐ **That is the parallel implementation to remove.**"*

⇒ ⛔ **`Parameters` and `WorkingState` flow through the shared source; `Variables` does not.** ⭐⭐ **Two
code paths for one concept — ruling 9's target, stated years before the visual check found it.**

### ⭐⭐⭐ `D` is the infrastructure unification the user is asking for

⚠⚠ **And `U-9` — the piece that DID ship — was built INVERSE of the plan:**

> 📌 **`BOOTSTRAP_Cross_Host_Variable_Model.md`:** *"`BlueprintDeclaration` + `BlueprintAsset.Declarations`
> — one tagged sequence over `Parameters` ∪ `WorkingState` ∪ `Variables`. ⚠ **Built INVERSE of the plan:
> the tagged type is the VIEW, the three lists are still the STORAGE.**"*

⇒ ⭐⭐⭐ **THAT is the whole answer.** ⛔ **The unification is a VIEW over three storage lists.** ⇒ **the
UI shows three sections because the STORAGE is still three lists**, and every surface that reads
storage rather than the view sees three concepts.

---

## 3. ⭐⭐ Cross-host — ⛔ **my earlier answer was WRONG**

⚠⚠ **`Q39-D` previously said *"NO — the shared name is a coincidence."* ⛔ **That is incorrect.**

📐 **`Role` is genuinely shared and ALREADY SHIPS** — `Variable_Model_Unification.md` §"`Role` is not a
new abstraction":

```csharp
public enum BlackboardVariableRole { Input = 0, State = 1 }     // ⭐ BTree/HSM — and the unified model
public enum WorkingStateScope      { Node = 0, Behavior = 1, Entity = 2 }
```

⇒ ⭐⭐ **The user's impression is CORRECT: BTree/HSM working state and blueprint working state are the
same concept** — both are `Role = State`.

### ⚠ But `Scope` is TWO DIFFERENT THINGS wearing one word — **this is the real nuance**

| | blueprint `Scope` | AI `WorkingStateScope` |
|---|---|---|
| values | `Asset` · `Graph` | `Node` · `Behavior` · `Entity` |
| ⭐ **means** | **VISIBILITY** — who can see it | ⛔ **BLACKBOARD SLOT SHARING** — who shares storage |

📌 **`Q-b` already ruled:** *"Does `Scope` need more than two values? ⛔ **No. `Asset` and `Graph`, and
stop there.** The BTree side's `WorkingStateScope` is about **blackboard slot sharing**."*

⇒ ⭐ **Unify on `Role`. ⛔ DO NOT naively unify `Scope`** — ⚠ **that is the one place where "same name"
really is a coincidence**, and it is the opposite of what I said before.

---

## 4. ⚠⚠ Two things that fit NO cell — **`D` must handle them or it breaks**

| | |
|---|---|
| 📌 **synthesized fields** — `__phase`, `__waitUntilTime`, `_when_*_prev` | ⭐ injected into `WorkingState` **during lowering**, after Stage 2's gate. They ARE `(State, Asset)` but were **never declared** ⇒ ⛔⛔ **under `D` they need a `Synthesized` marker or THEY SURFACE IN THE AUTHORING UI** |
| 🟠 **shared state** (`GetShared`/`SetShared`) | entity-scoped, **name-keyed, resolved at RUNTIME, declared nowhere in the asset** — 📐 **61 references across 8 shipped assets.** ⭐ *"To a designer it **is** a variable."* ⚠ **A deliberate exclusion — but the design says the decision must be taken EXPLICITLY** |

---

## 5. ⭐⭐⭐ RECOMMENDATION — **for approval**

| | ⭐ recommendation |
|---|---|
| **`Q39-A`** *(is there a distinction?)* | ⛔⛔ **NO — and this is not a new decision.** ⭐ *"Two names, one concept."* **Close it as ALREADY RULED** |
| ⭐⭐⭐ **`Q39-B`** *(what actually gets built?)* | ⭐⭐ **STAGE `B` then STAGE `D`** — ⛔ **not a UI merge.** `B` removes the parallel path *(editor only, trivial revert)*; `D` collapses three storage lists into one tagged list. ⭐ **The UI then shows one section BY CONSTRUCTION, with nothing to merge** |
| **`Q39-C`** *(sequencing)* | ⭐ **`B` FIRST and separately** — editor-only, reversible, and it alone removes the duplicate the user is looking at. ⚠ **`D` carries a JSON migration and is *"the only risky stage"*** ⇒ **its own batch, red-first, gated on `StructureHash`** |
| **`Q39-D`** *(cross-host)* | ⭐⭐ **YES on `Role` — it already ships as `BlackboardVariableRole`.** ⛔⛔ **NO on `Scope`** — blueprint `{Asset, Graph}` is VISIBILITY, AI `{Node, Behavior, Entity}` is SLOT SHARING *(`Q-b`)* |
| **`Q39-E`** *(migration)* | ⚠ **`D` needs one, and it is REVERSIBLE** — 📌 *"the pair is a bijection… the down-migrator `Q-d` demands is writable."* ⭐ **`StructureHash` is IDENTICAL if the partition reproduces order within each group** |

### ⛔ What this changes in flight

| | |
|---|---|
| ⛔⛔ **Batch 81 §3b** *(every section's `[+]` opens the same dialog)* | ⭐⭐ **PULL IT.** It builds a create-dialog **per section** for sections that stage `D` collapses — ⚠ **hardening the split** |
| ⚠ **Batch 81 §3a** *(rename/delete no-ops)* | ⭐ **KEEP** — the kind-scoped `FindVariable` bug is real under any model, and ⭐ **stage `B` would not fix it** |
| 📌 **the plan's open point** *"AiPrimitive-only sections show on every blueprint"* | ⭐ **dissolves under `D`** |

---

## 6. Status

| | |
|---|---|
| **raised** | `2026-08-17` by the user *(visual check `A2`)*; ⭐ **rewritten the same day** after the user rejected the UI framing |
| ⭐ **settled, do not re-litigate** | **`Variable` ≡ `WorkingState`** *(`Q32` ruling 8 · `Variable_Model_Unification` §2)* · **`Role` is cross-host** · **`Inputs` is genuinely different** |
| ⭐ **the actual work** | **stage `B`** *(editor, small)* → **stage `D`** *(model + migration, risky)* |
| ⚠ **my two corrections** | ⛔ **the UI framing was wrong** — this is infrastructure · ⛔ **`Q39-D`'s "coincidence" was wrong** — `Role` is shared, only `Scope` differs |
