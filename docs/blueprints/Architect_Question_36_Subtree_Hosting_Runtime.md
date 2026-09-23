<!--STATUS
state: LIVE
build-state: ⛔ NOT BUILDABLE FROM THIS DOCUMENT. The RULINGS are approved (2026-09-23), but this
  is a decision document and carries NO UML. Per NO-IMPLEMENTATION-WITHOUT-UML, the approved
  shape must be folded into DESIGN_Occurrence_Scoped_Storage.md — which owns §18/§19's hosting
  call and already carries the diagrams — and marked buildable THERE before any batch.
  §2a.3 is the item list, not a dispatch.
updated: 2026-09-23
current-answer: ✅ APPROVED 2026-09-23 — Q36-A = B, Q36-B = A (§6). §2a.3 is the build list.
  §3 and §4 hold the two sub-questions and the leans that were approved, both UNCHANGED.
  §2a is the 2026-09-23 correction: two of §2's premises moved after O4/O7c, and the leans got
  CHEAPER rather than different. ⛔ Read §2a before quoting §2 or §5.
stale-below: §2's measurements are dated 2026-08-17 at tree 9caa61e and two of them have MOVED
  — see §2a. §5's blast radius predates HostedSubtree and OVERSTATES the cost of Q36-A = B.
known-rot: §5 does not know about HostedSubtree.Tick (O4/C1), which supplies most of what
  "the host ticks the child inline" needs.
known-conflict: PLAN_Remaining_Work.md is revision 37 (2026-08-19) and predates O4/O7c; do not
  quote its E5 state.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — owns the hosted child's STORAGE and, since O4 §18/§19,
    the hosting CALL itself (HostedSubtree.Tick). This question owns only which brain runs the
    child and how the child is found.
  - Architect_Question_33_Blueprint_Brain_Tier.md — §1.5.4 rules the SHAPE (hosted via
    SubtreeAssetId, non-blocking, completion via HsmCommandWriter).
  - Architect_Question_34_Blueprint_Occurrence_Identity.md — §7 rules provisioning by KEY,
    never AttachToEntity.
-->

# Architect Question #36 — **what RUNS a hosted subtree, and how is it RESOLVED?**

> ## Storage model — per-occurrence slots in the tier ladder
>
> 📄 **[`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md)** is the authority for
> where behaviour params and AiPrimitive working state live: **per-occurrence slots**, allocated by the
> partition allocator inside the tier components this document specifies. There is no per-entity
> brain-state component — every occurrence (a Blueprint Instance, a BTree stateful node, an HSM region)
> gets its own slot. It is the build-out of
> [`Architect_Question_37`](Architect_Question_37_Unify_On_The_Allocator.md), which the user parked on
> `2026-08-17` and reopened on `2026-09-19`.
>
> ⭐ That design owns the hosted child's **storage**; this question still owns **which brain runs it**
> and **how it is resolved**.


> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable (`2026-08-16` user ruling).
> ⭐ **The document is the deliverable** — it isolates a decision with large blast radius and is
> **resolved jointly with the user.**
>
> 📄 **Context:** `E5` — 📄 [`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) §4B ·
> [`HANDOFF_Batch77_Subtree_Hosting.md`](batches/HANDOFF_Batch77_Subtree_Hosting.md) §1.
> ⭐ **The SHAPE is already ruled** — [`Q33`](Architect_Question_33_Blueprint_Brain_Tier.md) §1.5.4
> *(hosted via `SubtreeAssetId`, not as an action; non-blocking; completion raised through
> `HsmCommandWriter`)* and [`Q34`](Architect_Question_34_Blueprint_Occurrence_Identity.md) §7
> *(provision by KEY, ⛔ never `AttachToEntity`)*.
> ⛔⛔ **What is NOT ruled is the two things `E5` needs FIRST: which brain runs the child, and how the
> child is found at all.**

---

## 1. 🔴 Why this stopped Batch 77 item 1

The handoff's *"Ground truth — measured, do not re-measure"* table covers **storage**
*(`ComputeStatefulSlotKey` + `BlueprintBlackboardPartitions`)* and says the `E3` dependency was stale.
⭐ **Both hold.** ⛔ **But `E5`'s first two steps are "provision **and resolve**", and neither the table
nor `Q33`/`Q34` says what resolve reads or what the child executes on.** Measuring those produced two
blockers, below. ⚠ **Neither is an implementation detail** — each picks a mechanism the other tracks
will inherit.

---

## 2. 📐 What was measured — `2026-08-17`, on the tree at `9caa61e`

| # | measured | file |
|---|---|---|
| ① | ⛔⛔ **ONE brain per entity.** `BehaviorState` is `{ int ActiveBehaviorHash; uint InstanceId; byte BrainTier; }` — **one** hash, **one** tier. `BrainTickSystem`'s two arms both key off that single `ActiveBehaviorHash`, and `BrainTier` is what selects between them | `Behavior/Components/BehaviorComponents.cs:44` |
| ② | ⛔ **an HSM child is resolvable by asset id; a BTree child is NOT** | — |
| ②a | HSM registers under `DeterministicIdFromGuid(dto.AssetId)` | `HsmBridgeEmitCore.cs:131,152` |
| ②b | BTree registers under `BehaviorHash.FromName(name)` | `BTreeBridgeEmitCore.cs:446` |
| ②c | ⭐⭐ **and `BTreeBridgeEmitCore.cs:349` computes `int behaviorId = DeterministicIdFromGuid(dto.AssetId);` and NEVER USES IT** — dead, and it is **exactly** the value that would have made the two agree | `BTreeBridgeEmitCore.cs:349` |
| ③ | `BehaviorRegistry` indexes by **name** (`_nameToId`) and by **int id** (`_definitions`). ⛔ **No asset-id index of any kind** | `Behavior/BehaviorRegistry.cs:175-176` |
| ④ | ⭐⭐ **production never resolves by asset id — it resolves by NAME.** `BehaviorIngressSystem:65` is `TryGetId(evt.BehaviorName, …)` ⇒ the derivation asymmetry in ② is **invisible today** | `Behavior/Systems/BehaviorIngressSystem.cs:65` |
| ⑤ | ⭐⭐⭐ **the shipped BTree subtree mechanism ALSO resolves by name.** `BehaviorTreeBlob.SubtreeAssetIds` is a **`string[]`** *(of names — the field name misleads)*, and `BTreeEmitCore:836` emits `p.SubtreeName` as the reference | `Fbt.Kernel/BehaviorTreeBlob.cs:64`, `BTreeEmitCore.cs:836` |
| ⑥ | ⛔ **and the HSM side persists only half that pair.** `BTreeSubtreePayload` carries **`SubtreeAssetId` + `SubtreeName` + `IsResolved`**; `StateNode`/`StateNodeDto` carry **`SubtreeAssetId` alone** | `BehaviorTreeAsset.cs:90-97` vs `HsmAssetDto.cs` |

> ⚠ **⑥ is mine.** Batch 75 persisted `StateNode.SubtreeAssetId` and did not carry the name across.
> ⭐ At the time nothing read it, so nothing said which half was the *resolving* half — ⑤ is what says
> it, and ⑤ was not measured until now.

---

## 2a. ⚠⚠ CORRECTIONS — **two of §2's premises MOVED, and the leans got CHEAPER** *(`2026-09-23`)*

> 🔒 Re-measured while answering *"what is missing for `Q36`?"*. ⭐⭐ **Neither lean changes.**
> ⛔ But §5's blast radius was costed before `O4`, and one line of §2 was read too generously by a
> later summary — including by me.

### 2a.1 ⭐⭐⭐ `O4`/`C1` SHIPPED THE HOSTING PRIMITIVE THIS QUESTION PREDATES

📄 `DESIGN_Occurrence_Scoped_Storage.md` §18/§19 · `FDP/Toolkits/Fdp.Toolkits/Behavior/HostedSubtree.cs`.

⚠ **When `Q36-A` = `B` was costed, "the host ticks the child inline" still implied designing the
hosted-child STATE MODEL from scratch:** where the child's cursor lives — ⛔ it cannot share the
host's, one 64-byte `BehaviorTreeState` carries one `RunningNodeIndex` — how it is addressed, when it
resets, and what happens when the host abandons a still-`Running` child.

⭐ **`HostedSubtree` is exactly that, already built and railed**, and its own doc-comment calls it
*"one body, used by the generated orchestrator AND by hand-written hosts"*:

| it supplies | |
|---|---|
| the child's **own** `BehaviorTreeState`, in its own occurrence slot, resolved **by key at runtime** | ⛔ a missing slot is a HARD failure, not a silent one *(§19.6 ⑤)* |
| **`D4` half one** — reset on completion, DEEPER than the interpreter's own cleanup *(`StackPointer`, `NodeIndexStack`, `LocalRegisters`, `InstanceFlags`)* | |
| **`D4` half two** — the `F14` case, host abandons a still-`Running` child, via the hosting node's **deactivator** | |
| ⭐ **zero `ExtDeps` change** — `BehaviorTreeState` untouched, no kernel delegate, no ABI move | |

⇒ 🔒 **`B`'s cost drops from *"design and build the hosted-child state model"* to *"call an existing
helper with a key."*** ⚠ The BTree host already does exactly this — `BTreeOrchestratorEmitCore:151`
and `:182` emit `HostedSubtree.Tick(…, ref ctx, slotKey)`. ⭐ What remains for the HSM host is
**plumbing, not mechanism**: a slot key and an emitter.

### 2a.2 ⛔⛔ THE HSM ORCHESTRATOR IS THE **ALIAS** ARM — **the state-hosting arm has NO EMITTER AT ALL**

🔴 **A summary of this question read "the HSM orchestrator hosts a subtree". It does not, and the
distinction decides what is missing.**

📐 **Measured:** `HsmOrchestratorEmitCore.Emit` collects from **`dto.Aliases`** — blackboard
variable aliases — and **returns `null` when there are none**, which its own comment says is *"what
keeps the whole corpus byte-identical (no shipped asset has an alias)"*.

| ⇒ consequence | |
|---|---|
| ⛔ **`StateNodeDto.SubtreeAssetId` is read by NOTHING that emits a tick** | the state-hosting arm — the one this question is about — has no emitter |
| ⚠ **what IS emitted is the alias arm, and it carries the pre-`O4` defect** | `:98` emits `GetInterpreter().Tick(ref subBb, ref state, ref ctx)` — the caller's `BehaviorTreeState`, exactly the shape `HostedSubtree.Tick` replaced on the BTree side |
| ✅ **latent, not biting** | zero shipped `.hsm.json` declares an alias ⇒ the emitter returns `null` for every one of them |

### 2a.2a 🔴🔴 MEASURED `2026-09-23` — **THE EMITTED `[HsmAction]` HAS THE WRONG SIGNATURE ENTIRELY**

🔒 The previous revision of this section left one question open: the emitted `[HsmAction]` takes
`ref BehaviorTreeState state`, and an HSM has no tree state of its own — *"does the missing work mean
bake a slot key, or change the action signature?"* ⭐ **Measured, and the answer is neither.**

📐 **The HSM action ABI is a THUNK, and a `[HsmAction]` method must BE that thunk:**

| | |
|---|---|
| the dispatcher invokes | `((delegate*<void*, void*, HsmCommandWriter*, void>)actionPtr)(instance, context, writer)` — `HsmActionDispatcher.cs:20` |
| the generator registers | `{ id, (IntPtr)(delegate*<void*, void*, HsmCommandWriter*, void>)&{FullName} }` — `HsmActionGenerator.cs:405` |
| ⇒ so a decorated method must itself be | `static unsafe void M(void* instancePtr, void* contextPtr, HsmCommandWriter* writer)` — which is exactly the shape the generator emits for its OWN SharedAi thunks at `:599` |

⛔⛔ **What `HsmOrchestratorEmitCore:91-95` emits is the FastBTree ACTION shape instead:**

```csharp
[HsmAction(Name = "Orchestrate_X")]
public static NodeStatus Orchestrate_X_Tick(
    ref Bb master, ref BehaviorTreeState state, ref BTreeContext ctx, int paramIndex)
```

⇒ 🔴 **`&` of that cannot convert to the HSM thunk pointer type. The emitted orchestrator would
not compile.** ⭐ So `ref BehaviorTreeState` comes from **nowhere** — it is not an unsourced parameter,
it is *the wrong signature for the attribute it carries*.

⚠⚠ **Why nothing caught it, and the second half is the worse one:** `Emit` returns `null` for every
shipped asset *(no aliases)*, **and 📐 ZERO tests compile the emitted text** — the two that exercise
`HsmOrchestratorEmitCore` assert on **strings**. ⇒ the emission has never been compiled by anything.
🔒 **A text-asserting golden cannot tell you the code it pins is not valid C#.**

⭐⭐ **This makes `Q36-A` = `B` CHEAPER AGAIN, not harder.** A correct HSM thunk receives
`contextPtr` → `HsmKernelBridge*` → world + self — which is **precisely what
`HostedSubtree.Tick` needs** to resolve the child's tree-state slot by key. ⇒ ⛔ **no attribute
change, no ABI change, no new delegate**: the hosting action is written in the shape the generator
already uses at `:599`, and resolves the child's cursor inside the thunk.

⇒ ⭐ **§2a.3 item 4 is therefore NOT "bake a slot key into the action signature"** — it is *"emit the
hosting action as a real thunk and let `HostedSubtree.Tick` resolve the slot"*.

### 2a.3 ⭐ WHAT IS ACTUALLY MISSING, UNDER THE LEANS

| # | missing | measured at |
|---|---|---|
| **1** | **`StateNodeDto.SubtreeName`** + both mapper directions — this is `Q36-B` = `A`, and it is UNBUILT | `HsmAssetDto.cs:98` and `HsmAsset.cs:870` carry `SubtreeAssetId` **alone**; BTree's proven triple is `BehaviorTreeAsset.cs:93-96` |
| **2** | **an emitter for the state-hosting arm** | §2a.2 |
| **3** | **route it through `HostedSubtree.Tick`**, never the pre-`O4` shape | `BTreeOrchestratorEmitCore:151`/`:182` is the model |
| **4** | **emit the hosting action as a REAL THUNK** `(void*, void*, HsmCommandWriter*)` and resolve the child's slot inside it — ✅ **unblocked by §2a.2a**; the key comes from the bridge, not from the signature | `HsmActionGenerator.cs:599` is the shape; `BTreeOrchestratorEmitCore:151` the call |
| **5** | **assert the inherited `E3` hazard** — §5 already says NAME it, do not fix it. Zero instances | §5 |

⚠ **① and ②–③ of §2 are RE-MEASURED AND STILL TRUE** *(`2026-09-23`)*: `BehaviorState` is still
`{ ActiveBehaviorHash, InstanceId, BrainTier }` and `BrainTickSystem:169/171` still branches
exclusively on the tier; `BehaviorRegistry` still has **no asset-id index**. ⭐ `O7c` merged the two
tick systems into one `BrainTickSystem`, which changes the file but **not** the invariant.

---

## 3. `Q36-A` — **which brain runs the hosted child?**

⭐ **This is the load-bearing one.** ⛔ **There is no second brain slot on an entity** (①), so a state
that hosts a subtree has nowhere to put it.

| | option | cost | verdict |
|---|---|---|---|
| **A** | **a second `BehaviorState`-shaped component for the hosted child** *(`HostedBehaviorState`)* | one new component + one new tick system + ordering vs the host's tick | ⚠ honest, but ⛔ **a second brain mechanism** — ruling 9's shape |
| **B** | ⭐ **the HOST ticks the child inline** — the hosting state's entry resolves the child definition and the host's own tick drives it, child state living in the partition `Q34` §7 already rules | ⭐ **no new component, no new system, no ordering question**; the child never becomes "the entity's behaviour" | ⭐⭐ **the lean** — it is the only option that keeps *one* brain per entity, which is what ① actually encodes |
| **C** | **swap `ActiveBehaviorHash` to the child on entry and back on exit** | free | ⛔⛔ **RULED OUT by `Q33` §1.5.4** — *"a hosted subtree does NOT block its state's transitions"*; a swapped brain means the host is not running |
| **D** | **defer `E5` until the brain-slot shape unifies** *(`Q33` §1.5.5 correction 1 flags exactly this convergence)* | the queue stalls | ⚠ **worth naming**, because `B` is a commitment: it makes "hosted child" a thing the HSM tick owns, and that is hard to walk back |

⭐ **Under `B`, `Q34` §7 stays satisfied by construction** — the child is provisioned by KEY into a
partition, never attached to the entity, so it never competes for the single brain slot.

---

## 4. `Q36-B` — **what does resolve READ?**

⭐ **Given ⑤, the answer the codebase already ships is NAME.** The question is what the HSM state
persists so that name is available.

| | option | cost | verdict |
|---|---|---|---|
| **A** | ⭐ **mirror the BTree payload: add `SubtreeName` beside `SubtreeAssetId`** *(and keep the Guid as the stable identity)* | one nullable DTO field + both mapper directions; **moves `hsm-persistence-shape`** | ⭐⭐ **the lean** — ⭐ **one mechanism with the shipped BTree subtree path** (ruling 9), and the pair is already the proven shape: the **name resolves**, the **Guid survives a rename** |
| **B** | **add an asset-id index to `BehaviorRegistry`** and resolve by Guid | additive to the registry; both hosts gain it | ⚠ **cleaner in the abstract** and it fixes ② for good — ⛔ **but it makes HSM subtrees resolve by a different key than BTree subtrees**, which is two mechanisms for one concept |
| **C** | **unify the id derivation** — make BTree register under `DeterministicIdFromGuid(assetId)`, the dead value at ②c | 🔴 **every registered BTree id changes**; `BehaviorState.ActiveBehaviorHash` is persisted in replay/ingress paths | ⛔ **largest blast radius on the page**, and ④ says nothing currently *needs* it |

⚠ **Whatever is chosen, ②c should not stay as it is** — a computed-and-discarded id with a comment
calling it *"the deterministic behavior ID"* reads as the mechanism when it is not one. ⭐ **Either use
it (`C`) or delete it with the reason recorded** *(and per the `.dev/` rule, check the design corpus
before deleting)*.

---

## 5. Blast radius under the lean (`Q36-A` = `B`, `Q36-B` = `A`)

> ⚠⚠ **COSTED `2026-08-17`, BEFORE `O4`. §2a.1 makes `B` CHEAPER than this table implies** — the
> hosted-child state model it assumes must be designed is now shipped as `HostedSubtree.Tick`.

| | |
|---|---|
| ⭐ **new persisted field** | `StateNodeDto.SubtreeName` — nullable, omitted when empty ⇒ old assets load unchanged, new files a superset *(the `BP-302` shape)* |
| ⭐ **moves** | `hsm-persistence-shape` **only when an asset is re-saved** — ⚠ **not on the checked-in fixtures**, which is the correction `BP-302` established and which three handoffs in a row predicted wrongly |
| ⛔ **does NOT move** | the blueprint golden set · `StructureHash` · any BTree id |
| ⭐ **FastHSM** | **additive only** under `B` — the host's tick drives the child; ⛔ no kernel delegate or ABI change |
| ⚠ **inherited, not fixed here** | a hosted subtree's own actions still resolve DTOs at baked offsets ⇒ **the `E3` hazard is inherited** and must be asserted as a named gap, per the handoff |

---

## 6. Status

✅✅ **APPROVED `2026-09-23` — `Q36-A` = `B`, `Q36-B` = `A`.** 🔒 **User, verbatim: *"Q36
approved."*** ⇒ §2a.3 is the build list; §2a.2a is the measurement that unblocks its item 4.

⚠ **What approval does and does not settle:** it rules WHICH BRAIN runs the child *(the host, inline)*
and WHAT RESOLVE READS *(the name, beside the Guid)*. ⛔ It does **not** license the `E3` hazard away —
§5 still says a hosted subtree's own actions resolve DTOs at baked offsets and that must be asserted
as a named gap.

### ⛔ HISTORY — the pre-approval status

⛔ **OPEN — and what it was waiting on was a RULING, not more measurement.** ⭐ **Batch 77 item 1
STOPPED here rather than picking `Q36-A` unilaterally** — ① is a one-brain-per-entity invariant, and
choosing what breaks it is not an implementation detail.

⚠ **As of `2026-09-23` both sub-questions carry a lean, the blast radius is costed, and §2a records
what moved. 🔒 There is no architect to relay to** *(`2026-08-16` user ruling)* — ⭐ so the only
missing input is the user's approval, after which §2a.3 is the build list.

⭐ **What Batch 77 delivered instead:** the measurement above, and items 2 and 3 in full.
