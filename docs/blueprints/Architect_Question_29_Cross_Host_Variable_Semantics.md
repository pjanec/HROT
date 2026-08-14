# Architect question #29 — cross-host variable semantics (slots · scope · guards · events)

> **Raised 2026-08-14**, the SEMANTIC half of
> [#28](Architect_Question_28_Cross_Host_Binding_Mechanism.md). Ground truth in
> [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md).
>
> ⭐⭐ **The question behind all four:** the blueprint side spent nine batches proving that *a
> declaration which does not say which kind it is, is a defect* (`BP-226`). ⛔ **Aimed across hosts:
> if BTree, HSM and Blueprint each carry their own scope/uniqueness rules, that is three copies of an
> ordering that must agree.** ⚠ **But the hosts genuinely differ** — HSM has concurrent orthogonal
> regions and re-entry with history; BTree runs one leaf at a time. 📐 **So: where does the shared
> model STOP, and is the boundary a *capability* or a *kind*?**

---

## Ground truth

### ⭐⭐ The shared model already exists — at the editor layer only

| layer | BTree | HSM | Blueprint |
|---|---|---|---|
| **editor** | ⭐ `BlackboardVariableEntry` (`Role` × `Scope`) — `Hrot.Editor.AiShared` | ⭐ **same record** | ⭐ **same record** |
| **persisted asset** | `BlackboardVariableDto` (`Role` × `Scope`) | `HsmAssetDto` + same DTO | ⛔ **`ParameterDecl`/`VariableDecl` under `DeclarationKind`** |
| **compiler IR** | packed offsets | ⛔ **absent** | `VariableRef(VariableKind, int)` |

⇒ ⭐ **It is not three-way. It is 2 + 1, and the seam is the LAYER, not the host.**

- `Role` = `Input · State`; `Scope` = `Node · Behavior · Entity` (`BlackboardVariableEnums.cs`).
- `DeclarationKind` = `Parameter · WorkingState · Variable`; `VariableKind` adds `Unresolved = 0`.
  ⚠ **Different member orders** — ✅ but bridged by a **total, name-to-name** mapping
  (`DeclarationRefs.cs:23–36`), not an ordinal cast. Verified both directions.
- ⭐ **Graph locals are tagged `DeclarationKind.Variable`** — the *same* tag as asset variables
  (`Stage4:23`, `Stage5:122`). ⇒ **`DeclarationKind` does not carry the local/asset distinction.**
  Resolution is **by `Guid`, never by name** (`Q27-C1` shadowing).

### What a state actually carries today

`StateNode` has **four action-name strings** — `OnEntryAction · OnExitAction · ActivityAction ·
TimerAction` — and ⛔ **no `ExpressionTargetField`** (`DEBT-BF-04` confirmed).
`TransitionNode`/`GlobalTransitionNode` **do** carry one.

⚠ **And the four slots are already unequal in the ROM path:** `HsmFlattener.cs:172–173` allows an
explicit `EntryActionId`/`ExitActionId` override; ⛔ **`:174–175` (Activity, Timer) have none**, and
`actionTable[name]` is a **raw indexer** — `KeyNotFoundException` on an uncollected name.

### Guards are speculative and hold a MUTABLE ref

`HsmActionGenerator.EmitSharedAiGuardThunk` (`:690`) does
`repo.GetComponentRW<BrainBlackboard>(bridge->Self)` and hands the method `ref TField`.
⇒ ⛔ **a guard can write the shared variable today, and `SelectTransition` evaluates guards
speculatively and discards the losers.** Existing signatures are `ref` (`SequentialCondition(ref float
field, …)`), so a read-only projection is **source-breaking** for them.

### The budget is per-DTO, not per-live-set

`BP1200` sizes **one asset's** `Parameter` set ≤ 100; `BehaviorRegistry.cs:200` and
`BehaviorParameterSizeAnalyzer.cs:64` each size **one DTO** ≤ 100 — and the analyzer **re-declares the
constant locally** (`:26`). ⛔ **Nothing sums across simultaneously-live bindings.**
⭐ `BTreeBlackboardPackHelper.Pack` **already returns `totalBytes`**, and **skips `Role == State`
entirely** (`:140`) — working state is not in the inline region at all.

### Events share the type and none of the rails

`EventDispatcherDecl.Parameters` and `CustomEventDecl.Parameters` are `List<ParameterDecl>` — **the
same `ParameterDecl`** as `BlueprintAsset.Parameters`. ⛔ **But they sit outside `Declarations`**, so
they inherit **none** of: cross-kind uniqueness (`MakeUniqueName` over `Declarations`), `Role`/`Scope`,
the Stage-2 type rail (`:397–413`), or the size rails.

---

## Q29-A — What carries a binding on an HSM state? *(their `Q-B`, `DEBT-BF-04`)*

| | Option | ⚖️ |
|---|---|---|
| **A1** | Six persisted fields — `OnEntryParam`, `OnExitParam`, `ActivityParam`, `TimerParam`, `GuardParam`, `EffectParam` | ✅ faithful to the authored surface; obvious in the inspector · 🔴 **six fields and six picker sites**, and it hard-codes "four slots" into persistence |
| **A2** | One variable per **state**, shared by all four slots | ✅ cheapest · 🔴 **forces all four actions to take the same DTO type** — almost never true |
| **A3** | ⭐⭐ **A binding is its own entity**, keyed `(StableId, SlotKind)` | ✅ one carrier, one picker, one uniqueness rule; **`SlotKind` is an enum that can grow** · ⚠ a new persisted collection rather than fields on `StateNode` |

📐 **Claude's lean: A3 for storage, presented as A1.** ⭐ **The ground truth argues for it harder than
the HSM session realised:** the four slots are **already unequal** in `HsmFlattener` — Entry/Exit have
an explicit-id override, Activity/Timer do not. **A1 persists that asymmetry into the data model; A3
dissolves it**, because every slot becomes the same kind of thing.

---

## Q29-B — Is write-back just aliasing, and may a guard write? *(their `Q-D`)*

**D1 (is write-back aliasing?)** — ✅ **yes, and it is not really a question**: the thunk hands the
method a `ref` into the blackboard bytes. There is no copy-back step because there is no copy.

The real decision is **D2**:

| | Option | ⚖️ |
|---|---|---|
| **B1** | No enforcement — status quo | ✅ zero work · 🔴 **a guard can corrupt shared state during speculative evaluation, silently** |
| **B2** | ⭐ **Read-only projection for guards** — `in`/`ref readonly` at the thunk boundary, and `GetComponent` rather than `GetComponentRW` | ✅ the footgun becomes unexpressible · ⚠ **source-breaking** for existing `[SharedAiCondition]` signatures (`ref float field`) — a small, mechanical migration |
| **B3** | Validator rule only — refuse a guard method that writes its DTO | ✅ non-breaking · 🔴 **needs dataflow analysis to be sound**; a rule that cannot see through a helper call is a rule that lies |

⭐ **Their sub-question — "does read-only break DTO-type equality with the action sharing the
variable?"** ⇒ **No.** The DTO *type* is unchanged; only the **ref-kind** differs. Aliasing is
unaffected.

📐 **Claude's lean: B2.** ⭐ **B3 is the option to avoid** — it is the "one bool cannot say which half
failed" shape: a sound version needs analysis we do not have, and an unsound version is worse than
nothing. ⚠ **The migration is the honest cost**, and it is countable: three shipped `[SharedAi*]`
condition sites plus test fixtures.

**Their `Q-D3` (two orthogonal regions aliasing one variable):** the validator already has
`CheckConcurrentSharedScopeKeys` and `CrossRegionBlackboardConflict`. 📐 **Claude's lean: hard-error on
concurrent *writers*, permit concurrent *readers*** — which B2 makes decidable, because a guard is
then statically a reader.

---

## Q29-C — What is `Node` scope under re-entry? *(their `Q-E1`/`Q-E3`)*

| | Option | ⚖️ |
|---|---|---|
| **C1** | Per **state**, reset on entry | ✅ matches an intuition of "fresh each visit" · 🔴 **two slots on one state would share storage**, reintroducing A2's type problem |
| **C2** | ⭐ Per **state-slot**, keyed `FNV(AssetId, StableId, SlotKind)`, **preserved** across re-entry | ✅ **identical in shape to BTree's `FNV(BehaviorAssetId, NodeVisualId)`** — the same model, one term wider · ⚠ "preserved" must be *stated*, because re-entry makes it visible where BTree never did |
| **C3** | Author-controlled per variable (a `ResetOnEntry` flag) | ✅ maximally expressive · 🔴 **new persisted vocabulary** for a need nobody has demonstrated |

📐 **Claude's lean: C2.** ⭐ **The argument is that `preserved` is not a new decision — it is the
existing one.** BTree `Node`-scope state already persists across the node being re-reached; HSM
re-entry merely *makes that visible*. **C1 would be the new semantics, not C2.** ⇒ **and `OnEntry` is
the natural, explicit place for an author to reset** — which is exactly the HSM session's own lean.
⚠ **`Q-E2` (`Behavior`/`Entity` scope carry over unchanged): agreed, no question raised.**

---

## Q29-D — Are event parameters `Parameter`s? *(and the budget, their `Q-F`)*

| | Option | ⚖️ |
|---|---|---|
| **D1** | ⭐⭐ **Event params become `Declarations` entries** — they already **are** `ParameterDecl` | ✅ inherits uniqueness, `Role`/`Scope`, the type rail and the size rails **for free** · ⚠ they are per-event, not per-asset — `Declarations` would need an owner discriminator |
| **D2** | Stay separate, get their own parallel rails | ✅ no change to `Declarations` · 🔴 **a second copy of four rails that must agree with the first** — `BP-226`'s shape |
| **D3** | Stay separate, no rails — status quo | ✅ free · 🔴 an event payload can today name a nonexistent type and nothing checks it |

📐 **Claude's lean: D1**, with the caveat stated: `Declarations` is currently asset-scoped and an event
parameter is event-scoped, so D1 is **not free** — it needs the same owner discriminator that graph
locals needed. ⭐ **But that is one mechanism serving two needs, which is the argument for it.**

**On the budget (their `Q-F`) — confirmed: it assumes one-at-a-time.** Options:

| | | ⚖️ |
|---|---|---|
| **budget-1** | Keep per-asset/per-DTO | 🔴 an HSM parallel composite can exhaust 100 B with nothing noticing |
| **budget-2** | ⭐ Sum over **all** bindings in an asset | ✅ **headless, needs no region analysis**, `Pack` already returns `totalBytes` · ⚠ conservative — refuses some layouts that would fit |
| **budget-3** | Sum over **simultaneously-live** bindings from the region structure | ✅ exact · 🔴 needs a liveness analysis over orthogonal regions and history |

📐 **Claude's lean: budget-2 now, budget-3 only if a real asset is refused by it.** ⭐ **And regardless
of choice: `BehaviorParameterSizeAnalyzer.cs:26` must stop re-declaring `MaxBehaviorParamByteSize`
locally** — that is a third copy of a constant that must agree.

---

## Answers — ⭐⭐ ARCHITECT RULING, `2026-08-14`

> ⭐ **Provenance:** the NotebookLM architect is **unavailable**; the user designated this session as
> architect of record. ⇒ **rulings, not leans.** ⚠ **Claude-authored** — weaker provenance than the
> NotebookLM rounds, so **each ruling names the measurement it rests on** and can be overturned on
> evidence. **Measurements `M1`–`M4` are tabulated in the Answers section of
> [#28](Architect_Question_28_Cross_Host_Binding_Mechanism.md).**

### The rulings

**A → A3, and the reason is the one Claude found rather than the one the HSM session gave.**
⭐ Their argument for B3-underneath-B1 was *slot-key uniqueness*. **The stronger argument is that the
four slots are not peers today** (`HsmFlattener` gives two of them an override and two of them a raw
indexer throw), **and A1 would freeze that inequality into persistence.** A3 makes the asymmetry
impossible to express.
⚠ **But answer the question A3 raises and they did not:** ⭐ **is `SlotKind` open or closed?** If a
state later gains a fifth slot, A1 needs a persisted-schema change and A3 needs an enum member.
⇒ **that difference is the actual justification for A3, and it should be written down as the reason** —
otherwise the next session re-litigates it as "six fields is simpler", which, for four slots, it is.

**B → B2. ⭐⭐ And `M3` removes the only argument against it: the migration is essentially FREE.**

⛔ **The draft costed this at *"~3 shipped sites plus test fixtures"*. Measured: `[SharedAiCondition]`
and `[SharedAiHeavyCondition]` have ZERO production usages** — 4 usages exist, **all test fixtures**
(`SharedAiAttributeTests`, `SharedAiTestFixtures`, `ActionSchemaExporterTests`).
⇒ ⭐⭐ **The read-only projection can be made the rule before anyone depends on the mutable one.**
⚠ **This is the cheapest ruling in either document, and it closes a live footgun** — guard thunks call
`GetComponentRW` and hand out a mutable `ref` **today**, during speculative evaluation.

⭐ **State the invariant one level up:** the rule is not *"guards are read-only"*, it is
⭐⭐ **a speculative evaluation may not be observable.** Guards are today's instance; a future
scoring/utility hook would be another. **State it that way, implement it for guards.**

⛔ **B3 rejected** — a validator rule must be re-written for every new callee shape and cannot see
through a helper call, whereas the ref-kind is checked by the C# compiler for free.
⭐ **Use the type system where the type system already works.**

**On `Q-D3` (concurrent regions):** **hard-error on concurrent writers, permit concurrent readers** —
⚠ **decidable ONLY because of B2.** ⇒ **B2 is a prerequisite, not a peer.**

**C → C2, and Claude's framing is the ruling: `preserved` is the status quo, not a choice.**
⭐ Add the part Claude left implicit: **the reason BTree never had to answer this is that a BTree node
has no "exit".** HSM re-entry does not introduce new *storage* semantics; it introduces the **first
observation point** for semantics that were always there. ⇒ **Do not model it as an HSM feature.**
⛔⛔ **CONFIRMED by `M4` — it IS cross-compilation-visible, so `Q29-C` must ride with `Q28-A`.**
The stateful key is `{MethodFqn}@{offset}@{slotKey}`, baked as a `const` in the emitted thunk, and the
source states it *"must stay in lockstep with the topology blob key in `BTreeEmitCore.EmitAction`"*
(`BTreeBridgeEmitCore.cs:610–622`). ⇒ **widening it to carry `SlotKind` is not a separate change — it
is part of the `A2` re-bake.**

⭐ **And the mechanism already generalises**, which is the good news `M4` also produced:
`BTreeBridgeEmitCore.cs:611` — *"Behavior-scoped co-bound nodes bake the same key … so they dispatch
to one thunk over one shared slot."* ⇒ ⭐⭐ **scope is ALREADY encoded in the key.** The HSM analogue
adds one term in the same position; **it is not a new idea, it is the existing one with a wider tuple.**

**D → D1, but NOT in this programme, and the budget is the part to build now.**
⭐ D1 is right and the reasoning is right — event params *are* `ParameterDecl`, and D2 is a second copy
of four rails. ⛔ **But the bootstrap explicitly scoped event authoring out, and `HSM-009` is open on
the other side.** ⇒ **Record D1 as the ruling and do not schedule it**: the owner discriminator it
needs is the same one graph locals needed, so **it should follow whatever shape `U-12`'s store flip
settles on, not precede it.**
**On the budget: budget-2, and it is the item worth doing immediately** — ⭐ it is headless, `Pack`
already computes the number, and it closes a hole that is live *today* for BTree parallel composites,
not only hypothetically for HSM regions. ⚠ **Claude under-sold this:** the gap is not HSM-specific.
⭐ **And fold the duplicate `MaxBehaviorParamByteSize` into it** — a constant with three copies is the
same defect class as everything else in these two documents.

### 📌 What the ruling changes

| | |
|---|---|
| ⭐⭐ **B2 got cheap** | `M3`: **zero production usages.** It went from *"a migration to weigh"* to *"do it now, before anything depends on the mutable ref"* |
| ⭐ **B2 is a prerequisite, not a peer** | the cross-region concurrency rule is undecidable without it |
| ⭐ **budget-2 is promoted** | it fixes a **live BTree** hole, not a speculative HSM one |
| ⛔ **C2 confirmed as part of the `Q28-A` re-bake** | `M4`: slot keys are inside the registry key and baked as consts |
| ⛔ **D1 ruled but deliberately unscheduled** | it waits on `U-12`'s storage shape |

### ⚠⚠ The one ruling here I hold with least confidence

⭐ **`Q29-A` (slot carrier).** A1-vs-A3 turns on whether `SlotKind` ever grows past the six slots that
exist today (4 state + 2 transition). ⛔ **I have no evidence either way** — no HSM roadmap says
whether a seventh slot is plausible. **A3 costs a new persisted collection to buy extensibility nobody
has asked for**; A1 is genuinely simpler for a fixed six.
⇒ ⭐ **I rule A3 on the `HsmFlattener` asymmetry argument alone** (four slots that are already not
peers), **not on extensibility.** ⚠ **If a later architect has roadmap knowledge that six is final,
A1 is the better answer and this ruling should be overturned.**
