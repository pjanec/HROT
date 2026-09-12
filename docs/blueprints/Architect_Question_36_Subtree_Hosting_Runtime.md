# Architect Question #36 — **what RUNS a hosted subtree, and how is it RESOLVED?**

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
| ① | ⛔⛔ **ONE brain per entity.** `BehaviorState` is `{ int ActiveBehaviorHash; uint InstanceId; byte BrainTier; }` — **one** hash, **one** tier. `BTreeTickSystem:83` and `HsmTickSystem:158` both key off that single `ActiveBehaviorHash` | `Behavior/Components/BehaviorComponents.cs:44` |
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

| | |
|---|---|
| ⭐ **new persisted field** | `StateNodeDto.SubtreeName` — nullable, omitted when empty ⇒ old assets load unchanged, new files a superset *(the `BP-302` shape)* |
| ⭐ **moves** | `hsm-persistence-shape` **only when an asset is re-saved** — ⚠ **not on the checked-in fixtures**, which is the correction `BP-302` established and which three handoffs in a row predicted wrongly |
| ⛔ **does NOT move** | the blueprint golden set · `StructureHash` · any BTree id |
| ⭐ **FastHSM** | **additive only** under `B` — the host's tick drives the child; ⛔ no kernel delegate or ABI change |
| ⚠ **inherited, not fixed here** | a hosted subtree's own actions still resolve DTOs at baked offsets ⇒ **the `E3` hazard is inherited** and must be asserted as a named gap, per the handoff |

---

## 6. Status

⛔ **OPEN.** ⭐ **Batch 77 item 1 STOPPED here rather than picking `Q36-A` unilaterally** — ① is a
one-brain-per-entity invariant, and choosing what breaks it is not an implementation detail.

⭐ **What Batch 77 delivered instead:** the measurement above, and items 2 and 3 in full.
