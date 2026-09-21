<!--STATUS
state: LIVE
updated: 2026-09-21
current-answer: section 5 for the DECISIONS (APPROVED IN FULL by the user 2026-08-18: A2',
  B2, C1, D, E, F). Section 8 for WHAT IS BUILT - read it before quoting section 1's
  INVENTORY or section 6's sequencing, both of which section 8 corrects.
stale-below: section 1's INVENTORY is INCOMPLETE - it never found
  Behavior_Parameter_Resolver_Detailed_Design.md, which already owns this feature as gap G2
  and decomposes it into R1-R5. Section 6's step 2 ("the C# resolver picker first") did NOT
  happen and was not a hard dependency. Section 8 supersedes both.
known-rot: 2026-09-21 - C1'/C2' resolve PER VARIABLE, and a site reaches a variable via
  ExpressionTargetField - which HSM STATES did not have. E3b-0 has since given the state's
  four action slots a target field, so that gap is CLOSED; see
  DESIGN_Occurrence_Scoped_Storage.md 28.6.
known-conflict: Behavior_Parameter_Resolver_Detailed_Design.md 8.3 rules a reuse strategy
  that AVOIDS struct-typed graph inputs/outputs ("R3 - Hard (architectural)"), which
  CONTRADICTS Q43-B2. MEASURED 2026-09-21: R3 already works - a Library graph input typed
  `global::Ns.Struct` compiles and marshals today. Q43-B2 wins; 8.3's avoidance is stale.
  This is Q41-C3' promoted to its own question, as Q41 said it should be.
related-designs:
  - Behavior_Parameter_Resolver_Detailed_Design.md - owns this feature as gap G2 and
    decomposes it into R1-R5/E1-E6; R1+R2 (the LibraryFunctionDelegate seam) shipped
    2026-07-14 and this question's inventory missed them.
  - DESIGN_Parameter_Model.md - owns the three data shapes and the bake/overlay/resolve/write
    order this refines (3.1, 4.7).
  - DESIGN_Occurrence_Scoped_Storage.md - owns WHERE a resolved DTO lands (28, 28.7); this
    question owns WHO computes it.
  - Architect_Question_41... - owns the per-VARIABLE resolver selection (C1'/C2') this details.
-->
# ⭐ Architect Question 43 — **a parameter resolver authored AS A BLUEPRINT**

> # ✅✅✅ APPROVED IN FULL — user, `2026-08-18`
> ⭐⭐⭐ **`A2′`** *(the reserved `GraphKind.Construction` slot — ⛔ `A2`'s new dispatch kind is
> **WITHDRAWN**, the user's question found it)* · **`B2`** · **`C1`** · **`D`** · **`E`** · **`F`**,
> all as recommended. ⛔ **Canon, not a proposal** — see `R-94`. ⚠ **Nothing is built.**
>
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
| ⭐⭐⭐ ⑥ | `GraphKind` members, **and who consumes each** | **4 / 3** | `Function` · `Event` · **`Construction`** · `Macro`. 📐 **`Construction` maps `GraphKind → IrGraphKind` at `Stage5_Schedule:4837` and is named in `Stage2_Validate:422`** — ⛔⛔ **and NO emitter selects it**: `AiPrimitiveEmitter`, `InstanceEmitter`, `LibraryEmitter`, `CSharpEmitter` filter on `Function`/`Event`/`AiPrimitiveMain`; **zero** hits in `Fdp.Toolkits/Blueprints` or `Hrot.Blueprints.Core` |
| ⑦ | design intent for ⑥ *(`.dev/` + `docs/` sweep)* | **9 files** | ⭐⭐ **`Q23` states it deliberately:** *"Construction graphs are not offered in the create menu — nothing in the runtime consumes `GraphKind.Construction` yet."* ⇒ ⭐ **a RESERVED SLOT, not a vestige** |

⇒ ⭐⭐⭐ **THREE of the four hard parts already exist:** the **hook** *(`R-91`)*, the **struct-writing
vocabulary** *(④)*, and ⭐⭐ **the ENTRY-POINT KIND itself** *(⑥ — reserved, unconsumed, and named for
exactly this)*. ⛔ **What is missing is one EMITTER ARM and a picker filter.**

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

### `Q43-A` — What IS a resolver blueprint? ⭐⭐ **REVISED `2026-08-18` — the slot already exists**

> ⛔⛔ **MY FIRST ANSWER WAS WRONG, and the user's question found it — for the second time in two
> questions.** 📌 **User:** *"do we really need a new dispatch kind? for blueprints we were using some
> graph that runs on creation, isn't that similar?"*
>
> 📐 **Measured:** ⭐⭐⭐ **`GraphKind { Function, Event, Construction, Macro }` — `Construction` ALREADY
> EXISTS**, maps through `Stage5_Schedule:4837` into **`IrGraphKind.Construction`**, and is named in
> `Stage2_Validate:422`'s entry rule.
> ⛔ **And NO emitter selects it** — `AiPrimitiveEmitter`, `InstanceEmitter`, `LibraryEmitter`,
> `CSharpEmitter` all filter on `Function` / `Event` / `AiPrimitiveMain`; **zero** occurrences in
> `Fdp.Toolkits/Blueprints` or `Hrot.Blueprints.Core`.
>
> ⭐⭐ **And the design record already SAYS SO, deliberately** — 📄 **`Q23`:** *"Construction graphs are
> not offered in the create menu — **nothing in the runtime consumes `GraphKind.Construction` yet**."*
> ⇒ ⭐⭐⭐ **It is a RESERVED SLOT waiting for a consumer, not a vestige** *(`CLAUDE.md`: unreferenced is
> not unintentional — the `.dev/`+docs sweep is what found this)*.
>
> ⇒ ⛔ **A resolver does not need a new dispatch kind. It needs the consumer `Construction` was
> reserved for.**

| | option | verdict |
|---|---|---|
| ⛔ **A1** | a new **`AiPrimitiveHosting`** member | ⛔ **Reject** — all five members are *"a slot that TICKS"* |
| ⛔ **A2** *(withdrawn)* | a new **`BlueprintDispatchKind.ParamResolver`** | ⛔⛔ **WITHDRAWN** — ⚠ it would add a member to an enum with **in-degree 75**, **mirrored twice** *(`DEBT-013`)*, **inside `StructureHash`** — ⭐ **to express something the model already expresses** |
| ⭐⭐⭐ **A2′** | ⭐⭐ **The resolver IS a `GraphKind.Construction` graph** — *"the graph that runs at setup"* — with a signature of `(current DTO) → (DTO)` | ⭐⭐⭐ **RECOMMENDED** |
| ⛔ **A3** | `Library` + an unchecked convention | ⛔ **Reject** — a convention nothing checks is the shape this programme keeps filing |

| ⭐ why `A2′` wins on every axis | |
|---|---|
| **cost** | ⛔ **ZERO enum changes.** ⭐ No `StructureHash` impact, no mirrored-enum problem, no 75 call sites |
| **semantics** | ⭐⭐ *"runs once at setup, before the thing ticks"* — ⭐ **that is what a resolver IS**, and what `Construction` was named for |
| **safety** | ⭐⭐ every emitter already filters to `Function`/`Event`/`AiPrimitiveMain` ⇒ ⭐ **a new Construction emitter CANNOT change any existing asset** |
| **signature** | ⭐ graph `Inputs`/`Outputs` already exist and their CRUD is wired *(`Q23`)* ⇒ ⭐⭐ **`Q43-D`'s "take the DTO in, give it back" IS the graph signature** — no new concept |
| ⚠ **what it still needs** | ⭐ **an emitter arm for `IrGraphKind.Construction`**, and ⭐ the picker filter. ⛔ **That is the whole delta** |

#### ⚠ The one thing `A2′` must not do — **squat on the slot**

`Construction` is plausibly *also* wanted for its original sense: **an Instance asset's construction
script**. ⛔ **Do not define `Construction` as "the resolver graph".**
⇒ ⭐⭐ **Define it as "runs once at setup"**, and let **`Dispatch × GraphKind`** say what that means —
📌 **`V_DispatchKindCompatibility` is already exactly that validator.** ⭐ A `Construction` graph on a
resolver asset resolves params; on an `Instance` asset it would configure the instance. ⭐ **Same emit
shape, different call site** — ⛔ **not two features.**

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

---

## 8. ⭐⭐⭐ AS-BUILT `2026-09-21` — **the resolver compiles, registers and runs**

> ⭐⭐ **Obligation ⑤:** the build deviated from this document in three measured ways. They are
> recorded here, and the prior state is marked, rather than left only in a batch report.

### 8.1 ⛔⛔ What §1's INVENTORY MISSED — **the owning design already existed**

📌 §1 ran six graph queries and concluded *"three of the four hard parts already exist."* ⛔ It never
found 📄 [`Behavior_Parameter_Resolver_Detailed_Design.md`](Behavior_Parameter_Resolver_Detailed_Design.md),
which owns this feature as **gap `G2`** and decomposes it in §8.1 into `R1`–`R5`.

| 📐 measured `2026-09-21` | |
|---|---|
| ⭐⭐⭐ **`R1` + `R2` SHIPPED `2026-07-14`** | `LibraryFunctionDelegate` + `BlueprintDefinition.Functions`. Its own XML doc says it verbatim: *"the runtime seam through which a blueprint-authored parameter resolver is dispatched."* |
| ⛔⛔ **and had ZERO production consumers** for two months | only `LibraryFunction_InvokeTests` and `LibraryFunctionsDemo_ProofTests` read it |
| ⇒ ⭐ **the delta was smaller than §5 `A2′` said** | not *"an emitter arm plus a picker filter"* — ⭐ **an emitter arm, a table, a validator.** The marshalling, the delegate and the registrar loop were already there |

🔒 **The transferable lesson, and it is `CLAUDE.md`'s seam law again:** *"we need a shared X"* almost
always means **X already exists and is under-adopted.** ⚠ The `INVENTORY` rule was FOLLOWED — six
queries, all answered — ⛔ **but it queried the CODE graph only.** The design corpus was not swept for
this question's own topic, and the one document that owned it was one `search_code` away.

### 8.2 ⛔ `§8.3`'s "avoid R3" is STALE — **struct-typed graph pins already work**

📄 `Behavior_Parameter_Resolver_Detailed_Design.md` §8.1 rates **`R3` — struct/DTO-typed graph
inputs/outputs — *"Hard (architectural)"***, and its **§8.3 reuse strategy exists to AVOID it**: *"the
blueprint function stays pure scalar-in / scalar-out."* ⛔ **That contradicts `Q43-B2`.**

📐 **Measured by experiment, not by reading:** a `Library` asset whose graph input is typed
`global::Hrot.AI.Behaviors.Brains.CgfNodes.MoveToLocationParams` **compiles today**, emits
`public static …MoveToLocationParams Resolve(…MoveToLocationParams Dto)`, and the
`LibraryFunctionDelegate` adapter marshals it with `MemoryMarshal.Read<T>` / `Unsafe.SizeOf<T>`.

⭐ **Why it works although `StaticTypeRegistry` is a fixed scalar list:** the `global::` acceptance path
takes any FQN as an unmanaged value type, and its **guessed 4-byte size is never used here** — a Library
asset has no state layout (`StateSize = 0`) and the adapter sizes with the **real CLR** `Unsafe.SizeOf`.
⇒ ⭐⭐ **`Q43-B2` stands; §8.3's decomposition is no longer needed** *(the reciprocal note is in that
document)*.

### 8.3 ⭐ The ONE thing that actually blocked `A2′` — **`BP5001`**

📐 A `Library` asset whose only graph was `Construction` failed with **`BP5001`** — *"declares no
Function graphs and no Macro graphs, so it exposes nothing to call"* — and emitted **nothing**. ⛔ Not
the type system, not the emitter, not `StructureHash`: **one lowering rule.** ⭐ The widening is purely
additive — it can only turn a hard error into a successful compile — so no asset that compiled before
moved.

### 8.4 ⭐⭐ What was built

| piece | where |
|---|---|
| the **`Construction` emitter arm** — the consumer the kind was reserved for | `LibraryEmitter.EmitClass` *(the same emit as a Function graph, deliberately)* |
| **`BlueprintDefinition.Resolvers`** — a separate INDEX, the same `LibraryFunctionDelegate` | `Fdp.Toolkits/Blueprints/BlueprintDefinition.cs` |
| the registrar table, gated on `Count > 0` so no existing golden moves | `CSharpEmitter.EmitLibraryRegistration` |
| **`V_ResolverPurity`** — `BP1675` purity · `BP1676` Library-only · `BP1677` the `(DTO in → same DTO out)` signature | `Compiler/Stages/V_ResolverPurity.cs` |
| **the golden asset** — `ParamResolverDemo`, corpus 43 → 44 | `Hrot.AI.Behaviors/Assets/Blueprints/ParamResolverDemo.bp.json` |
| **the completeness rail** — every concrete `Node` subclass is classified pure or side-effecting | `V_ResolverPurityTests.EveryNodeKind_IsClassifiedAsPureOrSideEffecting` |
| **the end-to-end rail** — the real corpus asset compiles, loads, registers and REFINES a DTO | `BlueprintAuthoredResolver_InvokeTests` |

⭐⭐ **Why `Resolvers` is its own table and not a `Functions` entry:** the graph KIND is the only thing
that separates a resolver from a helper, and `Q43-A3` rejected *"a Library function plus an unchecked
convention."* ⛔ One delegate type (ruling 9 — one invocation mechanism), two indexes.

### 8.5 ⚠ Two honest gaps in `C1`'s enforcement, and one deviation from §6

| ⚠ | |
|---|---|
| **`FunctionCallNode` is ALLOWED** and the validator cannot see inside a CLR callee | ⭐ refusing it would kill the motivating case — geo-authored params need `IGeographicTransform.ToCartesian`, and §8.2 `E3` names the CLR escape hatch as how a graph reaches one. ⇒ the callee's purity belongs to whoever registered it |
| **`MacroCallNode` is DENIED** | ⛔ macros expand at Stage 5, AFTER this Stage-2 check, so an allowed macro could smuggle in any denied node |
| ⛔ **§6's step 2 — the C# resolver picker — was SKIPPED** | 🔒 the UI lane is held by the user *(`2026-09-21`)* until the `ui` branch is integrated. ⭐ It was a de-risking step, not a hard dependency: resolvers register in CODE for now |
| ⛔ **NO BINDING YET** | ⚠ nothing yet says *"behaviour X's params are refined by resolver Y.Z"*. `Resolvers` is a producer whose consumer is the next slice — ⭐ and that is exactly the `Functions`-shaped orphan this document should not repeat, so it is named here rather than left implicit |
