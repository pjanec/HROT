# Architect Question #35 — **how does an HSM action learn WHICH occurrence it is?**

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable (`2026-08-16` user ruling).
> ⭐ **The document is the deliverable** — it forces a decision with real blast radius into
> decision-shaped options, and is **resolved jointly with the user.**
>
> 📄 **Context:** `E3` — 📄 [`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) §4B ·
> [`Architect_Question_34`](Architect_Question_34_Blueprint_Occurrence_Identity.md) §7.
> ⭐⭐ **`E3` is the one occurrence case that SILENTLY CORRUPTS:** two concurrently-active orthogonal
> regions running the same action **write the same bytes**.

---

## 1. Why this is a question — ⭐ **the escalation, and what it measured**

⭐ **Batch 72 killed my "signature widening" premise**, and **Batch 73 produced the consumer census.**

| | measured |
|---|---|
| ⛔ **the thunk cannot receive an occurrence** | dispatch is `delegate*<void*, void*, HsmCommandWriter*, void>`; every registered id is a **static function pointer chosen at build time** |
| ⛔ **and there is nowhere for a second occurrence's bytes** | the generated thunk resolves its DTO at `bb.BehaviorParameters[0] + <baked offset>` — a fixed offset into the entity's **single 100-byte `BrainBlackboard`** ⇒ ⭐ **one home by construction** |
| 🔴 **the ABI blast radius, if the delegate widens** | **55** `[HsmAction]`/`[HsmGuard]` methods across **25 directories** — incl. **FastHSM's own demos/tests** and **both `FDP/Examples` projects** · **13** kernel call sites · ⭐⭐ **FIVE** emitters producing the fixed shape *(incl. `CSharpEmitter` — **the blueprint side registers HSM thunks too**)* |

⇒ ⭐⭐ **The storage half is already ruled** *(`Q34` §7: the partition allocator under
`ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)` — BTree's shipped algorithm)*.
⛔ **What is NOT ruled is DELIVERY: how the occurrence identity reaches the thunk so it can compute
that key.**

---

## 2. ⭐ Three facts that shape the options *(measured `2026-08-17`)*

| | |
|---|---|
| ⭐⭐ **`contextPtr` is `Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*`** | ⛔ **NOT an `ExtDeps` type** — it is ours: `Self`, `WorldHandle`, `TraceContext`. ⚠ **But the KERNEL treats `context` as an opaque `void*`**, so the kernel cannot fill a field it does not know about |
| ⭐⭐ **`HsmCommandWriter` IS a kernel type** | `Fhsm.Kernel/Data/HsmCommandWriter.cs`, a `ref struct` the kernel constructs and passes to **every action** ⇒ ⭐ **the kernel can fill it, and it already reaches the thunk** |
| ⚠ **guards do NOT get the writer** | `EvaluateGuard` is `delegate*<void*, void*, ushort, bool>` — the third argument is `eventId`. ⭐ **But `VE-DEBT-004`: NO production `[HsmGuard]` exists**, measured |

---

## 3. `Q35-A` — **the delivery mechanism**

| | option | cost | verdict |
|---|---|---|---|
| **A** | **widen the delegate** — add the occurrence as a parameter | 🔴 **ABI break across 55 methods / 25 directories / 5 emitters / 13 kernel sites**, incl. `ExtDeps` and both `FDP/Examples` projects | ⛔ **the honest baseline, and the most expensive thing on the page** |
| ⭐⭐ **B** | **carry it on `HsmCommandWriter`** — the kernel sets `CurrentRegion` / `CurrentStateId` before each dispatch; the thunk reads them and computes its own slot key | ⭐ **two fields on a kernel struct + the kernel's assignment.** ⛔ **No signature changes anywhere** — hand-written thunks keep compiling untouched. ⚠ **Actions only; guards unserved** | ⭐⭐⭐ **RECOMMENDED LEAN** — ⭐ **and "guards unserved" is measurably free: there are none** |
| **C** | **carry it on `HsmKernelBridge`** *(our context struct)* | ⛔ the **kernel** must write into a struct it sees as `void*` ⇒ **a layout convention across the ExtDeps boundary** — the coupling `B` avoids by using a type the kernel owns | ⛔ **rejected** — same delivery, worse boundary |
| **D** | a kernel-set **ambient** *(static / `[ThreadStatic]` current occurrence)* | zero signature change, **and it serves guards** | ⚠ **ambient state in the hot dispatch loop**; ⛔ **breaks the moment ticking is parallelised**, which is the direction of travel |

⭐ **What the thunk actually needs is not "the region" but the BASE of its per-occurrence storage** —
and ⛔ **the kernel cannot compute that** *(it knows nothing of the partition allocator)*. ⇒ **the kernel
supplies IDENTITY; the thunk does the `TryGetSlotOffset` lookup it already performs for stateful BTree
actions.** ⭐ **That is why `B` is sufficient and `A` is overkill.**

---

## 4. `Q35-B` — **what identity, exactly?**

| | option | |
|---|---|---|
| ⭐ **A** | **`(regionSlotIndex, stateId)`** | ⭐⭐ **LEAN** — both are already in scope at the call site (`HsmKernelCore`), and together they name the occurrence the way `nodeVisualId` does for BTree |
| **B** | a pre-hashed `int occurrenceKey` computed by the kernel | ⛔ **puts the key algorithm in `ExtDeps`** — ⚠ **two homes for one algorithm** *(ruling 9)*; the whole point of `Q34` §7 was to reuse `ComputeStatefulSlotKey` |
| **C** | region only | ⛔ **insufficient** — one region re-entering different states hosting the same action would alias |

---

## 5. `Q35-C` — **who pays for the DTO's bytes now?**

⚠ **Today the DTO lives in the 100-byte `BrainBlackboard` at a baked offset.** Under `E3` a
**per-occurrence** DTO must come from the partition allocator instead.

| | question | lean |
|---|---|---|
| ⭐ **the single-occurrence case** | does a plain, non-orthogonal HSM action **also** move to the allocator? | ⭐⭐ **YES — one path, not two.** ⛔ Keeping the baked-offset path *"for the simple case"* is two mechanisms for one concept *(ruling 9)*, and the divergence is exactly what makes the bug invisible today |
| ⚠ **the cost** | every HSM action gains a tier probe + `TryGetSlotOffset` | ⭐ **the same cost stateful BTree actions already pay**, and the thunk already contains that code shape |
| 🔴 **the corpus** | 📐 **`persistence-shape` cannot see this** *(it is thunk emission)* — ⭐ **but the Batch-73 generated-code tier CAN**, and its acceptance test proves it reaches thunk ids | ⭐ **land `E3` under that tier** |

---

## 6. Blast radius under the lean (`B` + `A` + one path)

| | |
|---|---|
| ✅ **no delegate signature changes** | ⇒ **55 attributed methods, both `FDP/Examples` projects and FastHSM's own demos compile untouched** |
| ⚠ **`ExtDeps` still changes** | **two fields on `HsmCommandWriter`** + the kernel assigning them at **13** call sites. ⭐ **Additive, not breaking** |
| ⚠ **five emitters change what they EMIT** | ⛔ **not the shape they emit to** — thunk bodies resolve the DTO from the allocator instead of `bb.BehaviorParameters[0]` |
| ⭐ **`E5` rides the same mechanism** | 📄 `Q34` §7 already rules a hosted subtree provisions **by key** ⇒ ⭐⭐ **one storage route serves `E3` and `E5`** |
| ⛔ **`StructureHash` / `persistence-shape`** | **must not move** — this is runtime storage and thunk text |

---

## 7. Status

| | |
|---|---|
| **raised** | `2026-08-17`, coordinator, from Batch 73's consumer census |
| **state** | 🔴 **OPEN — agenda for a working session with the user.** ⛔ **`E3` does not start until `Q35-A` is answered** |
| ⭐ **not blocked by it** | **`BP-281`** *(HSM has no `ParseParams` counterpart)* · **`E7b`'s runtime half** · **BTree's emit tier** · the `InspectorWindow` retirement ⇒ **Batch 74 proceeds** |
| ⚠ **what a "no" costs** | if none of `B`/`C`/`D` is acceptable and `A` is too expensive, ⛔ **`E3` is deferred and orthogonal regions keep sharing bytes** — ⭐ **that is a legitimate answer**, but it must be a decision, not a drift |
