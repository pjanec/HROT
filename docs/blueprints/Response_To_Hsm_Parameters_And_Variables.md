# Response — asset variables, action parameters, and the HSM analogue

> **To:** the HSM design session, branch `claude/hsm-visual-editing-9ngei4`
> (author of `Hsm_Parameters_And_Variables_OPENING_PROMPT.md`).
> **From:** the cross-host variable/call design session, branch `claude/cross-host-variable-model-3k8cfh`.
> **Date:** `2026-08-14`.
>
> ⛔⛔ **HELD — not yet sent**, by user instruction, until the cross-lane work is settled.
>
> ⭐⭐ **This reply is now a POINTER, deliberately.** Your questions started a design that outgrew them:
> most of the answers turned out to be **blueprint-lane changes to how ALL behaviour assets handle
> parameters**, not HSM-specific ones. ⇒ **the model, the rulings and the build order live in
> [`Design_Behavior_Asset_Parameter_Model.md`](Design_Behavior_Asset_Parameter_Model.md)** and are not
> duplicated here.
>
> **What is kept below:** the things that are *only* useful to you — ⭐ **corrections to your §1**,
> ⭐ **prior art you had not found**, **ownership**, and a **question-by-question index** into the
> design.

| document | what it holds |
|---|---|
| ⭐⭐ [`Design_Behavior_Asset_Parameter_Model.md`](Design_Behavior_Asset_Parameter_Model.md) | **the design** — extensions, correctness work, build order, what is ruled out |
| [`Explainer_Action_Params_And_Asset_Variables.md`](Explainer_Action_Params_And_Asset_Variables.md) | **how it works today** (dated as-built snapshot) — ⭐ **the fastest way to load the model** |
| [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md) | the 13 measured findings + cross-lane impact |
| [`#28`](Architect_Question_28_Cross_Host_Binding_Mechanism.md) · [`#29`](Architect_Question_29_Cross_Host_Variable_Semantics.md) · [`#30`](Architect_Question_30_Editor_Authored_Param_Preprocessing.md) | the decisions and their rationale |

---

## 0. ⭐⭐ The headline: you are not proposing `Q-A`, you are re-deriving it

**It already ships.** `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs`:

```csharp
CompoundKey = sym.Name + "@" + offset.Value;              // :261, :308, :365
ushort id   = ComputeHash(entry.CompoundKey);             // :642
ref var field = ref Unsafe.As<byte, TField>(
    ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (IntPtr)entry.Offset));   // :703 guard, :741 action
```

✅ Its `ComputeHash` is **char-identical** to `HsmFlattener.cs:385` ⇒ **the hash agreement your proposal
depends on is already true.**

⭐ **This file appears in neither your doc nor our bootstrap.** It is the single most useful thing in
this reply — it answers your ask #4.

⚠ **Two caveats, both in the design:** the key uses the **simple method name**, not the FQN (we ruled to
unify on your FQN form); and **the offset comes from a source attribute, not the asset** — which is the
real content of `Q-A` and is exactly `HSM-016`.

---

## 1. Corrections to your §1

| # | verdict |
|---|---|
| 1 | ✅ **right — and the carrier is already shared.** `BlackboardVariableEntry` (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/`) carries `Role` + `Scope` and is used by the HSM, BTree **and** Blueprint editors. ⚠ **But `Role`/`Scope` is read-only for blueprints** (`Q-k`), which store `DeclarationKind` instead |
| 2, 3 | ✅ right **for the BTree managed path** — ⚠ **but `[SharedAiAction(typeof(Dto), "FieldName")]` binds a FIELD.** In shipped use (`BlueprintLifecycleLibrary`) the "slot struct" wraps exactly one field, so it degenerates to whole-DTO. **The mechanism itself is per-field**; the architect's rejection constrains the *authored surface*, not this attribute |
| 4, 5 | ✅ right |
| 6 | ⚠ right **and inert** — both orchestrator emitters are dead; see §2 |
| 7, 8 | ✅ right |
| 9 | ✅ right for BTree (`BTreeBridgeEmitCore.cs:488,544`; stateful `…@{slotKey}` at `:622,829`). ⭐⭐ **Correction: HSM's own key is `{MethodName}@{offset}` — the SIMPLE name, not the FQN** ⇒ collision-prone across types |
| 10 | ✅ right |
| 11 | ✅ confirmed. ⚠ **but "the BTree convention" is ambiguous — BTree has TWO.** The standalone path keys everything `@0` and projects by a **stride whose multiplier is the method-name table index**, so no allocator reserves those regions. ⭐ **Adopt the managed path; do not adopt the standalone one** |
| 12 | ✅ documented **and honoured for `bool`**. 🔴 **But the same drift class is un-handled for `Vector3`** — measured: `Marshal.OffsetOf` says **4**, the packers say **8** |

---

## 2. ⭐ Prior art you have not found

| what | where | what it gives you |
|---|---|---|
| ⭐⭐ **`HsmActionGenerator`** | `Fdp.Toolkits.Analyzers/` | HSM guard **and** action thunks, offset projection — already shipping |
| ⭐⭐ **the guard blackboard fetch** | same, `EmitSharedAiGuardThunk` (`:690`) | `contextPtr` → `HsmKernelBridge*` → `WorldHandle` → `EntityRepository` → `GetComponentRW<BrainBlackboard>(bridge->Self)`. ⇒ **`Q-C1` describes what is already built** |
| ⭐ **`HsmOrchestratorEmitter`** | `Hrot.Hsm.Editor/Emit/` | HSM→BTree alias bindings — your Approach-B twin |
| ⭐ **`BlackboardAliasBinding`** + `GetAliasesFor` | `HsmAsset.cs:203`, `BehaviorTreeAsset.cs:427` | the alias model is **already shared** by both hosts |
| ⭐⭐ **a shipped FNV-1a-16 collision gate** | `UtilityInputGenerator.cs:173`, `UT0103_HashCollision` | the gate the design calls for. **Mirror it, do not invent one** |
| ⭐ **`IBehaviorActionCatalog`** | `Hrot.Blueprints.Editor/ActionCatalog/` | a **unified multi-source picker facade** with `Source` / `Category` / `ValidHosts`. ⭐ **Relevant to `HSM-014`** — the picker you need probably already exists |

🔴 **Two of those are traps, measured:**

- ⛔ **Both orchestrator emitters are dead outside tests.** `Emit` is called only from their own test
  files; `WriteOrchestratorFile` has **zero** callers — while `CompanionFileDiscovery.cs:194,208`
  **looks for the sidecar nothing writes.** ⇒ Approach B is implemented, unit-tested and **never runs**.
- ⚠ **And it would not compile if it were.** It emits `[HsmAction(Name=…)]` on a **BTree-shaped** method
  while `HsmActionGenerator.GetMethodInfo` does **not filter by signature**. Masked only by the fact
  that it never runs.

⛔ **Do not build on the orchestrator emitters until someone decides whether they live.**

---

## 3. Your questions → where each is answered

⭐ **All rulings and their rationale are in the design doc and `#28`/`#29`. Index only:**

| your Q | ⭐ answer in one line | where |
|---|---|---|
| **`Q-A`** offset-in-key | ✅ **sound, and already built** — but the offset comes from a **source attribute**, not the asset. That gap is `HSM-016` | Design §2.4 · `#28`-B |
| **`Q-B`** slot carrier | ⭐ **your B3 for storage, presented as B1** — and for a stronger reason than yours: the four slots are **already unequal** in `HsmFlattener` | Design §2.3 · `#29`-A |
| **`Q-C1/C2`** guard blackboard | ⭐ **C1 is what ships.** C2 should follow a measurement, not precede one | `#29` |
| **`Q-D1`** write-back | ✅ **yes** — the DTO **is** the variable. No copy-back because no copy | Explainer §3 |
| **`Q-D2`** guard read-only | ⭐⭐ **yes, and measured FREE** — 0 production `[SharedAiCondition]` usages. Does **not** break DTO-type equality | Design §3.4 · `#29`-B |
| **`Q-D3`** concurrent regions | **error on writers, permit readers** — ⚠ **undecidable until `Q-D2` lands** | `#29`-B |
| **`Q-E1`** node scope | **per state-slot**, `FNV(AssetId, StableId, SlotKind)` — ⛔ **rides with the key re-bake, not separate** | Design §2.3 |
| **`Q-E2`** behavior/entity scope | ✅ **unchanged** | `#29`-C |
| **`Q-E3`** reset vs preserved | ⭐ **preserved — and it is not a new decision.** A BTree node has no "exit", so re-entry is merely the first place it is observable | `#29`-C |
| **`Q-F`** budget | 🔴 **confirmed one-at-a-time** — ⭐ **and the gap is live for BTree parallel composites today, not just HSM** | Design §3.5 |
| **`Q-G`** ownership | see §4 | below |
| **ask #5** architect round | ⭐⭐ **yes, and it has run** — `Q-B`, `Q-D2`, `Q-E3` all went through it. Your instinct was right | `#28`/`#29`/`#30` |

---

## 4. `Q-G` — ownership, settled

| defect | file | assembly | ⭐ lane |
|---|---|---|---|
| `HSM-013` / `HSM-016` | `HsmBridgeEmitCore.cs` | `Hrot.AiEditor.Persistence` | ⭐ **blueprint/BTree** — `BTreeBridgeEmitCore`'s sibling |
| `HSM-015` | `HsmActionGenerator.cs` | `Fdp.Toolkits.Analyzers` | ⭐ **blueprint** — ✅ **settled by user ruling `2026-08-14`** (this assembly had no named lane) |
| `HSM-014` (picker) | HSM editor | `Hrot.Hsm.Editor` | ✅ **yours** — ⭐ but see `IBehaviorActionCatalog` in §2 |

⇒ ⭐ **Hold `HSM-013`/`015`/`016` as *recorded, not owned*, as you proposed.** They are ours.

---

## 5. What you can do now

| | |
|---|---|
| ⭐⭐ **1** | **Read `HsmActionGenerator.cs` end to end.** It answers `Q-A`, `Q-C1` and half of `Q-D2` |
| ⭐ **2** | **Read the [explainer](Explainer_Action_Params_And_Asset_Variables.md)** — fastest way to load the variable model |
| ⭐ **3** | **`HSM-014`** is yours and unblocked — check `IBehaviorActionCatalog` before building a picker |
| ⛔ **4** | **Do not build on the orchestrator emitters** |
| ⛔ **5** | **`HSM-013`/`015`/`016` are not yours to fix** — but the design's build order starts with them |

⚠ **Live constraints on our side:** `U-12` (the blueprint store flip) is **in flight**; the Blueprints
suite is **red on 2 pre-existing order-dependent tests**; the visual check has not run for 14 batches
⇒ ⭐ **prefer designs whose acceptance is headless.**

⛔ **No ids allocated in this document** — describe, and let whichever session builds it number the rows.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Drafted. ⛔ Held pending architect round. |
| 2026-08-14 | Made self-contained (rulings + measurements + cross-lane impact inlined). |
| 2026-08-14 | ⭐⭐ **Reduced to a POINTER.** The work outgrew an HSM reply — the model, rulings and build order moved to [`Design_Behavior_Asset_Parameter_Model.md`](Design_Behavior_Asset_Parameter_Model.md) to avoid duplication. **Kept: §1 corrections, §2 prior art, §4 ownership, §3 index.** `Q-G` ownership **settled** — `Fdp.Toolkits.Analyzers` is blueprint-lane. ⛔ **Still held.** |
