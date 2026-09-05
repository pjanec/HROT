# Architect Question #37 — **should ALL parameter storage move to the allocator?**

> ⛔⛔ **PARKED `2026-08-17` by the user** — ⭐ *"i would defer this idea for now and return to single
> level behaviors for a while in order to finish the planned work with variable unification and related
> ui changes. but i would certainly keep this open and return to it a bit later."*
> ⭐⭐ **The measurements below are the deliverable.** ⛔ **Do not re-measure them when this reopens.**

---

## 1. The question, in the user's words

> ⭐ *"why not using the allocator always and leave the brainblackboard for parameters? is there any
> good reason not to unify everything to allocatable blackboard components? would it harm the hardcoded
> behaviors or something?"*

⭐⭐ **It arose from a real defect**, not from tidiness: with an HSM host ticking a BTree child *(`Q36-A`
= `B`, approved)*, **both pack their variables from offset `0` into the same 100-byte
`BrainBlackboard.BehaviorParameters`** and nothing keeps them apart.

---

## 2. 📐 What was measured — `2026-08-17`

| # | measured | verdict |
|---|---|---|
| ① | `BTreeBlackboardPackHelper.Pack:131` — **`int offset = 0` PER ASSET.** Every asset lays its variables out from `0` | ⛔ host and child **overlap by construction** |
| ② | thunks bake `ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)<offset>)` | ⛔ **the base is hardcoded in EMITTED text** |
| ③ | `BehaviorIngressSystem:57` shadow-copies the whole 128-byte blackboard and runs **the ONE active behaviour's** `ParseParams` | ⛔ **a child's params would never be written at all** |
| ④ | `MaxBehaviorParamByteSize = 100` | ⚠ a **shared** budget, not per-behaviour |
| ⑤ | `BrainBTreeState` holds **one** `BehaviorTreeState` for the entity | ⚠ **same shape** — a hosted BTree child needs its own |

### ⭐⭐⭐ And the two objections that DO NOT exist

| | measured |
|---|---|
| ✅ **hardcoded behaviours would NOT be harmed** | 📐 **every direct `bb.BehaviorParameters[0]` reference in the repo is inside an EMITTER.** Hand-written node methods take `ref dto`; hand-written resolvers take a destination `byte*`. ⭐ **Both are already base-agnostic** — 📄 `DESIGN_Parameter_Model.md` §4.2 says so and the code matches ⇒ **the change is "emitters emit a different base expression"** |
| ✅ **replay / snapshot is unaffected** | `BrainBlackboard` **and** all three `BlueprintBlackboard{1024,4096,16384}` are `[DataPolicy(NoSave)]` — **snapshotted AND recorded alike** |

### ⚠ The two costs that are real

| | |
|---|---|
| 🔴 **a 1 KB floor per AI entity** | the smallest tier is **1024 B** *(96 of it header + slot table)* against today's **128 B** `BrainBlackboard` ⇒ ⭐ **~8× for the simple case**, and an **archetype change for every AI entity** *(today `EnsureTierComponent` adds a tier on demand)*. ⚠ **Whether it matters depends on the AI entity count, which was NOT measured** |
| ⚠ **indirection moves from SOME to ALL** | today: one field access on a component already in hand. Under the allocator: tier probe → `GetComponentRW` → `fixed` → `TryGetSlotOffset` *(linear scan)*. ⭐ **Generated STATEFUL thunks already do exactly this**, so it is proven — ⛔ **but it goes from "the stateful ones pay it" to "every action, every tick"** |

⭐ **And `BrainBlackboard` does not disappear** — the tail stays *(`ExpectedThreatLevel` at offset 120,
the interrupt registers written by `CognitiveInterruptSystem`/`CognitiveCleanupSystem`)*. ⭐⭐ **The
component becomes *cognitive tail only*, which is clearer than today's "params plus an unrelated tail at
fixed offsets"** — and it is exactly the separation 📄 `DESIGN_Parameter_Model.md` §4.3 already asserts
*("carry the params AREA only, never the component")*.

---

## 3. `Q37-A` — **the options**

| | option | verdict |
|---|---|---|
| **A** | **unify unconditionally** — every behaviour's params come from the allocator | simplest, one mechanism everywhere; ⛔ **pays the 1 KB floor on every AI entity** |
| ⭐ **B** | **unify, and add a SMALLER tier** — e.g. `BlueprintBlackboard256`, `MaxSlots ≈ 2` | ⭐⭐ **the recommended lean** — the unification, with the small case priced properly. ⭐ **Also collapses the "am I a root or a child?" branch** that would otherwise be baked into every emitted thunk |
| **C** | **root keeps the inline area; hosted children use the allocator** | cheapest today — ⛔ **the two-mechanism answer**, and that divergence is what hid the defect. ⚠ **`Q35-C` already ruled against the same shape** |

---

## 4. What this subsumes when it reopens

| | |
|---|---|
| ⭐ **`Q36-C`** *(never written)* | *"where does a hosted child's params base come from"* — ⛔ **stops being a separate question**: if params always come from the allocator, there is one answer |
| ⭐⭐ **`E3`'s scope** | `E3` is *"resolve the base instead of baking it"*. ⭐ **Under `A`/`B` that IS the change**, with no root/child branch ⇒ ⚠ **`E3` built before this decision would be partly rework** |
| ⚠ **the 100-byte cap** | dissolves for children — their region comes from a 928 / 3936 / 16368-byte tier |

---

## 5. Status

| | |
|---|---|
| **raised** | `2026-08-17`, **by the user**, from the host/child params collision |
| **state** | ⛔⛔ **PARKED — deliberately, with the measurements banked.** ⭐ **Reopen before building `E3` or `E5`**; both are downstream of it |
| ⭐ **what proceeds meanwhile** | **single-level behaviours**: the variable unification's remaining UI work, the Track C visual check, and the single-level defects — 📄 `PLAN_Remaining_Work.md` |
