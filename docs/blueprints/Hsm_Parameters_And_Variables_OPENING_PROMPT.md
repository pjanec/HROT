<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: sections 3-5; sections 1-2 are the shared ground, not questions
known-rot: none. REWRITTEN 2026-08-20 after merging Batches 46-98 — the first draft asked
  seven questions of which four were already ruled. Do not restore it from history.
-->
# Opening prompt — HSM parameters and variables: what is ruled, what HSM is missing, what is still open

> **To:** the Blueprint / BTree authoring session (owner of `DESIGN_Parameter_Model.md`, `RULINGS.md`
> and the variable-unification programme).
> **From:** the HSM design session, branch `claude/hsm-visual-editing-9ngei4`.
> **Read first if new to the HSM side:** [Hsm_Integration_Map.md](Hsm_Integration_Map.md) ·
> [Hsm_Issues_Tracker.md](Hsm_Issues_Tracker.md)
>
> ⭐⭐ **REWRITTEN `2026-08-20`.** The first draft asked you to explain your parameter model. Then we
> merged Batches 46→98 and read `DESIGN_Parameter_Model.md`, `EXPLAINER_Where_Parameters_And_State_Live.md`
> and `RULINGS.md`. **Four of the seven questions were already ruled, and two of our stated
> premises were wrong.** This version records what we now believe, corrects our own errors, and asks
> only what your documents do not answer.
>
> ⛔ **Nothing is being built.** This is a design consult.

---

## 1. ⛔ Two things we had wrong — corrected from your docs

Recording these because the first draft would have asked you to confirm them.

| we wrote | the truth | source |
|---|---|---|
| *"a node binds its **whole DTO** to one variable"* | ⛔ **superseded `2026-08-16`.** The 100 B holds **ONE params struct per BEHAVIOUR** (`BehaviorDefinition.ParamsDtoType`, singular); an action **binds a FIELD** of it — `[SharedAiAction(typeof(Dto),"Field")]` | `DESIGN_Parameter_Model.md` §0 |
| *"role is `input`/param vs `state`"* | ⚠ **vocabulary:** the enum is `{ Input, State }` and **there is no "Param" role** — `Input` *is* the parameter role | resolver design §3.2, `R-06` |

⭐ We have propagated both into our own docs.

---

## 2. What we take as settled — please correct anything wrong

| # | Taken as ruled | Source |
|---|---|---|
| 1 | `Role { Input, State }` is **cross-host**; BTree/HSM working state ≡ blueprint working state | `R-06` |
| 2 | `Scope { Node, Behavior, Entity }` applies to `State` only | `DESIGN_Parameter_Model.md` §1 |
| 3 | ⭐⭐ **The thunk key is `MethodFqn@offset`** — stated for the BTree/HSM `Role=Input` case **jointly** | `R-88` |
| 4 | Offsets come from the packer ⇒ the params-case variable name is **editor-only** at runtime | `R-88` |
| 5 | Supply order: **bake defaults, overlay incoming JSON by variable name, runtime wins** — once at assignment, never per tick. **True on both hosts since Batch 70/74** | `R-81`, `BP-292` |
| 6 | **Sections are the classification** — no `Role`/`Scope` control on any host | ruling `2026-08-16` |
| 7 | Params belong to the **occurrence**, not the entity; carry the **params area only** | `DESIGN_Parameter_Model.md` §4 |
| 8 | **HSM multi-occurrence cost ACCEPTED** | ruling `2026-08-16` |
| 9 | ⭐ **"On HSM, absent NEVER means unwanted — it is behind, not scoped out"** | ruling `2026-08-16` |
| 10 | `Blackboard1024` is **one component shared** by BTree/HSM/Blueprint at disjoint offsets | `R-65` |

⇒ **Our earlier "is the offset-in-key convention sound?" question is withdrawn.** `R-88` rules it,
and it is the right answer: the offset rides inside the hashed identity, so **no `StateDef` /
`TransitionDef` ROM change is needed** — which matters, because both structs are full (32 B with one
spare byte; 16 B with none).

---

## 3. 📐 The measurement: HSM has the supply half and not the binding half

Measured on the merged tree, `2026-08-20`:

| | BTree | HSM |
|---|---|---|
| `MethodFqn` references in the bridge emit core | **50** | ⛔ **0** |
| params **supply** (`__parseParams`: bake defaults → overlay JSON) | ✅ `BTreeBridgeEmitCore.EmitParseParamsLocal:1231` | ✅ `HsmBridgeEmitCore:231` *(Batch 74, `BP-292`)* |
| params **binding** (per-binding thunk at `MethodFqn@offset`) | ✅ shipped | ⛔ **absent** |
| stateful working slots | ✅ | ✅ `EmitStatefulWorkingSlotsArray:443` |
| `ExpressionTargetField` carrier | node | ⚠ **transition + global only — `StateNodeDto` has none** |

⇒ **HSM is exactly one layer behind: it can be *given* parameters, but nothing can *bind* an action
to them.** Combined with our tracker rows `HSM-013` (AiPrimitive registers under a GUID hash the blob
never looks up) and `HSM-015` (the generated HSM thunk reads `Params` out of live instance memory),
**an HSM action or guard with parameters cannot work today by any route.**

---

## 4. The questions your documents do not answer

Each has our lean, so you have something concrete to reject.

### Q-1 — Per-slot binding on a four-slot state *(this is `DEBT-BF-04`)*

A BTree node has **one** action. An HSM **state has four** (`OnEntry`/`OnExit`/`Activity`/`Timer`)
and a transition has **two** (guard, effect). `StateNodeDto` carries **no** `ExpressionTargetField`
at all — verified on the merged tree.

Under the *"one params struct per behaviour, action binds a field"* model this may be **simpler than
we first thought**: each of the six slots binds a **field** of the one behaviour struct, so no new
per-slot allocation is needed — only a per-slot *field name*.

**Our lean:** six field-name bindings, one per slot, mirroring `[SharedAiAction(typeof(Dto),"Field")]`.
**Q-1a.** Is that the intended extension? **Q-1b.** `DEBT-BF-04` was flagged as *"needs an architect
design decision, not an autonomous guess"* — does the `2026-08-16` field-binding ruling already
discharge it, or does it still need its own round?

### Q-2 — Where a guard's parameters come from

An HSM guard is `bool(void* instance, void* context, ushort eventId)` — **no blackboard argument**,
and `instance` is the HSM instance, not a blackboard. So a bound guard must fetch `BrainBlackboard`
from the entity through `HsmKernelBridge`.

**Q-2a.** Acceptable per evaluation, given guards are evaluated **speculatively** across candidate
transitions inside an RTC loop bounded at 100 iterations? **Q-2b.** Or add the pointer to
`HsmKernelBridge`, which we already build once per entity per tick? *(Our lean: Q-2b.)*

### Q-3 — Guard side-effect safety

`R-88` makes the params case editor-only precisely because the binding is by offset. But an HSM guard
**must be side-effect free** — `SelectTransition` evaluates candidates and discards losers. A guard
holding a mutable `ref` into shared param bytes is a footgun BTree conditions may not have.

**Should a bound HSM guard get a read-only projection?** And if so, does that break field-type
equality with an action that binds the same field? *(No lean — we do not know your intent here.)*

### Q-4 — `Scope=Node` across state re-entry

Your `Node` scope is keyed `FNV-1a(BehaviorAssetId, NodeVisualId)`. HSM has a lifecycle BTree lacks:
**a state can be exited and re-entered**, and **history can restore it**.

**Q-4a.** Is the HSM `Node`-scope unit the **state**, or the **state-slot**? *(Lean: state-slot,
keyed `FNV-1a(AssetId, StableId, SlotKind)` — `OnEntry` and `Activity` are different call sites.)*
**Q-4b.** Should `Node`-scoped state be **reset on entry**, **preserved across re-entry**, or
**author-controlled**? *(Lean: preserved — `OnEntry` is the natural place to reset. But this is a
real semantic choice.)*

### Q-5 — The 100-byte budget under genuine concurrency

Ruling 8 accepts *"HSM multi-occurrence cost"*. We want to check it covers this: a BTree runs **one
leaf at a time**; an HSM **parallel composite has several active leaves simultaneously**, each with
live `Activity` params in the same 100 B.

**Does the packer's budget model already account for simultaneously-live bindings, or does it assume
one-at-a-time?** If the latter, HSM reaches the heavy tier much sooner than BTree.

### Q-6 — Ownership

`HSM-013` and `HSM-015` live in `AiPrimitiveEmitter` / `CSharpEmitter` — **your compiler**, surfacing
as HSM authoring failures. `HSM-017` (rename dangles the binding, with **no** HSM build diagnostic —
BTree has `BTREE0002`, HSM has only the `HSM0001` parse-error code) is the HSM half of your `M-15`.
**Whose lane?** We are content to hold them as *recorded, not owned*.

---

## 5. What would be most useful back

1. **Corrections to §2** — anything we have taken as ruled that is not.
2. **Q-1**, which unblocks the most: it decides whether `DEBT-BF-04` is discharged or still needs an
   architect round.
3. **Q-3**, the one place we have no lean at all.
4. **A view on sequencing.** `HSM-013`/`HSM-015` are the fix that makes bound HSM actions work; is
   that a batch you would take, or one we should hand off?

⛔ **Out of scope here** — HSM-local and not needing you: event authoring (`HSM-009`), the
initial-state model (`HSM-003`), history modelling (`HSM-010`), timers (`HSM-012`), region
persistence (`HSM-004`/`005`).

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Created. |
| 2026-08-20 | ⭐ **Rewritten** after merging Batches 46–98: four of seven questions were already ruled (`R-88`, `R-06`, `R-81`, ruling 8); two of our own premises were wrong and are corrected in §1; added the §3 measurement showing HSM has the supply half and not the binding half. |
