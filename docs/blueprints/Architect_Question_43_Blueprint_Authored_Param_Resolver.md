<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 5 - the recommended answers. Nothing here is built.
stale-below: nothing.
known-rot: none.
known-conflict: none. This is Q41-C3' promoted to its own question, as Q41 said it
  should be; it does not disagree with Q41, it details it.
-->
# ⭐ Architect Question 43 — **a parameter resolver authored AS A BLUEPRINT**

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable *(`2026-08-16` user ruling)*.
> ⭐⭐ **I analyse and RECOMMEND, the user APPROVES.**
>
> 📌 **Origin:** user, `2026-08-18` — *"Could the action resolver be blueprint authorable?"*, then
> *"approved, and write Q43 for the blueprint resolver."*
> ⭐ **This is `Q41-C3′` promoted**, exactly as `Q41` said it should be: *"recommended in principle,
> own design pass — the entry-point shape is the part that needs a design, not a batch."*
>
> ⭐⭐ **Approved and BINDING on this question:** `R-90` *(`Q41` `A`/`B`/`D`)* · `R-91` *(emit the
> resolve hook; PER-VARIABLE)* · `Q41-C1′`–`C3′`. ⛔ **`C2` in the plan — the hook — is this
> question's PREREQUISITE.** Without it a resolver of any kind cannot run on a managed asset.

---

## 1. ⭐⭐ INVENTORY *(`R-74`)*

| # | query | total | what it found |
|---|---|---|---|
| ① | `search_graph(name_pattern=".*(BlueprintDispatch\|DispatchKind\|AiPrimitiveHosting).*")` | **21** | production: **`BlueprintDispatchKind` ×2** *(Compiler + Toolkits — `DEBT-013`'s deliberate mirror)* · **`AiPrimitiveHosting`** · **`V_DispatchKindCompatibility`** *(98 lines — the validator that already encodes which shapes are legal where)*; rest are tests + `.dev/` history |
| ② | `BlueprintDispatchKind` | **3 members** | `Library` · `AiPrimitive` · `Instance` — ⚠ **in-degree 75**, and 📌 `StructureHash_DifferentAcrossDispatchKinds` proves **dispatch is IN the hash** |
| ③ | `AiPrimitiveHosting` / `AiPrimitiveIntent` | **5 / 2** | `BTreeAction` · `BTreeCondition` · `HsmAction` · `HsmGuard` · `BlueprintCall` — ⭐⭐ **every member is "a slot that TICKS"** · intents are `Action`/`Condition` only |
| ④ | struct-building IR ops | **2, BUILT** | ⭐⭐⭐ **`IrOp_MakeStruct`** *(construct from per-field values)* and **`IrOp_SetMembers`** *(copy-with-changes)* — **Q#14 Option B**, with live arms in `StatementEmitter:238/258` |
| ⑤ | the target signature | **1** | `ResolveParams<TDto>(ref TDto dto, EntityRepository world, Entity self, IHostVariableAccess? host)` — 📌 `BehaviorParams.cs:18` |

⇒ ⭐⭐ **Two of the three hard parts already exist:** the **hook** *(`R-91`)* and the **struct-writing
vocabulary** *(④)*. ⛔ **What is missing is the ENTRY-POINT SHAPE**, which is what this question decides.

---

## 2. ⭐ Why this is worth doing at all

📌 **`R-37`:** resolvers fill params **once at activation** — ⭐ **that is the intended default**, and
`Q41-B`'s per-tick reader node is the exception, not the rule.
⛔ **But today the resolver tier is programmer-only**: **5 resolvers**, all registered in
`CgfCuratedBehaviorRegistrar`, and ⛔ **no editor surface at all**.

⇒ ⭐⭐⭐ **A designer who wants *"my destination comes from the world when this behaviour starts"* has
no route that does not involve a C# change.** ⭐ **This gives them one, in the tool they already use.**

---

## 3. ⭐ What binds any answer

| id | binds |
|---|---|
| **`R-91`** | ⭐ the hook is **per-VARIABLE**, and the order is **bake → overlay → resolve → write** |
| **`R-37`** | ⛔ **once at activation.** ⛔ **Not a second supply mechanism** |
| **`R-84`** | ⛔ live host↔child binding stays out |
| **`R-81`** | ⭐ the resolver **REFINES** what bake+overlay produced — ⛔ it does not replace the order |
| **ruling 9** | ⭐⭐ **one mechanism, two authoring routes.** ⛔ A parallel "blueprint params path" fails this |
| **`R-65`** | ⚠ the blackboard is shared by three hosts — ⛔ a resolver must write **only its own DTO** |

---

## 4. 🔴 THE CRUX — **a resolver runs inside the ingress SHADOW PARSE**

📐 `BehaviorIngressSystem`: shadow-copy the blackboard → parse into the **shadow** → commit only on
success. ⇒ ⭐⭐ **`ParseParams` throwing is not a bug, it is the mechanism** — *"a parse failure leaves
the entity 100% on its old behaviour."*

⇒ ⭐⭐⭐ **A resolver blueprint inherits that guarantee FOR FREE — but only if it writes nothing except
`ref dto`.** ⛔ **Any side effect (spawn, shared-state write, event dispatch) escapes the shadow and
survives a failed parse**, which breaks the one property the whole path is built on.

---

## 5. ⭐⭐⭐ THE SUB-QUESTIONS — **each with a recommended answer**

### `Q43-A` — What IS a resolver blueprint: a new hosting, or a new dispatch?

| | option | verdict |
|---|---|---|
| **A1** | a new **`AiPrimitiveHosting`** member *(`ParamResolver`)* | ⛔ **Reject.** 📐 **All five existing members are "a slot that TICKS."** A resolver is **called during ingress**, hosted nowhere ⇒ ⛔ it would make `Hostings` mean two different things |
| ⭐⭐⭐ **A2** | a new **`BlueprintDispatchKind.ParamResolver`** | ⭐⭐⭐ **RECOMMENDED** |
| **A3** | reuse `Library` + a convention | ⛔ **Reject** — a convention nothing checks is the shape this programme keeps filing |

⭐ **Why A2:** the emit shape **genuinely differs** — ⛔ no `WorkingState`, ⛔ no tick, ⭐ a `ref TDto`
in/out — and **`V_DispatchKindCompatibility` is already the place that says which shapes are legal
where**, so this is an arm on an existing validator rather than a new concept.
⚠ **Cost, stated:** `BlueprintDispatchKind` has **in-degree 75**, exists in **two mirrored copies**
*(`DEBT-013`)*, and **dispatch is in `StructureHash`** — ⛔ **a new member must be appended, never
inserted.**

### `Q43-B` — What does it WRITE: its own generated struct, or a foreign DTO?

| | option | verdict |
|---|---|---|
| **B1** | only its **own** generated `Params` type *(as composed AiPrimitive does)* | ⚠ **Cheap and it MISSES THE MOTIVATING CASE** — `Q41` started from a **hand-written** `MoveToParams` |
| ⭐⭐⭐ **B2** | **any struct type it names**, filled via `IrOp_MakeStruct` / `IrOp_SetMembers` | ⭐⭐⭐ **RECOMMENDED** |

⭐⭐ **Why B2:** 📐 **both ops are already BUILT with live emit arms** *(inventory ④)* — this is
**routing an existing vocabulary at a new entry point, not new IR.** ⭐ **And `B1` falls out for free**:
a blueprint-generated `Params` is just another struct type.
⚠ **Constraint:** the resolver's declared output type **must equal the target variable's type**, and
that is what the picker filters on — ⭐ **the same type-filter the parameter picker already uses.**

### `Q43-C` — How is purity enforced?

| | option | verdict |
|---|---|---|
| ⭐⭐⭐ **C1** | ⭐ **A validator arm — `V_ResolverPurity`** — beside `V_DispatchKindCompatibility`: ⛔ **declares no variables** · ⛔ **no side-effecting op** · ⭐ **writes only the output DTO** | ⭐⭐⭐ **RECOMMENDED** |
| **C2** | document it and trust the author | ⛔⛔ **Reject** — §4 says a side effect **survives a failed parse.** ⚠ That is silent corruption, not a lint |
| **C3** | whitelist the legal node vocabulary | ⚠ **Safer and it ROTS** — ⛔ every new pure node is blocked by default until someone remembers the list |

⭐⭐ **Recommended shape: a DENY-LIST of side-effecting op kinds, plus a RAIL that every op kind is
either on the list or explicitly marked pure.** ⇒ ⭐ **a new op cannot be silently forgotten** — the
rail fails until someone classifies it. 📌 That is the checkable form this programme's rules keep
converging on.

### `Q43-D` — Does the resolver SEE the value bake+overlay produced?

⭐⭐⭐ **RECOMMENDED: YES — the graph takes the current DTO as an input and returns the modified one.**
📌 `ResolveParams` is `ref TDto`, and it runs **after** deserialize ⇒ ⭐ **the resolver REFINES**
*(`R-81`)*. ⛔ **A resolver that only produced a value would silently discard the scenario's override**,
which is exactly the defect `BP-275` fixed on the generated path. ⚠ **No sub-options** — this follows
from the canon and is stated so it is not re-decided.

### `Q43-E` — Where is a resolver chosen, and from what list?

⭐⭐⭐ **RECOMMENDED: ONE type-filtered picker, per variable, listing BOTH sources** — registered C#
resolvers *(`Q41-C2′`)* **and** blueprint assets with `Dispatch = ParamResolver` whose output type
matches. 📌 **Ruling 9: a resolver is a resolver** ⇒ ⛔ **two pickers would be two mechanisms in the UI
for one concept in the model.**

### `Q43-F` — What happens when a resolver blueprint faults?

⭐⭐⭐ **RECOMMENDED: it THROWS, and nothing catches it.** ⭐ The ingress already turns that into *"the
entity stays 100% on its old behaviour"* — ⛔ **do NOT add a try/catch that yields a default DTO**,
which would convert a loud failure into a silent all-zero params region. 📌 **The same reasoning
`BehaviorParams.FromJson` states for not swallowing.**

---

## 6. ⚠ SEQUENCING — **this is not first**

| order | | |
|---|---|---|
| **1** | ⭐⭐⭐ **the resolve HOOK** *(plan `C2` / `R-91`)* | ⛔ **prerequisite — nothing here runs without it** |
| **2** | ⭐ **the C# resolver picker** *(plan `C3`)* | ⭐ proves the per-variable selection UI on a mechanism that already exists |
| **3** | ⭐ **this question's build** | ⚠ **only once 1 and 2 are real** |

⛔⛔ **`R-26`'s implementation FREEZE holds** — ⭐ one session, all hosts.
⚠ **And `R-22`: `Q32` §4 owns the variable-model order** — ⛔ this does not jump it.

## 7. ⛔ OUT OF SCOPE

| ⛔ | |
|---|---|
| **per-tick parameter binding** | ⛔ ruled out — `R-37`, `R-84`. ⭐ `Q41-B`'s reader node is the per-tick answer |
| **letting a resolver WRITE the host blackboard** | ⛔ `Q41-A1`, approved: publish/subscribe only |
| **`E7a`** *(populating `IHostVariableAccess`)* | ⭐ **orthogonal — a resolver blueprint receives `host` for free once `E7a` lands**; ⛔ this question does not build it |
| **`Q42`'s guid migration** | independent |
