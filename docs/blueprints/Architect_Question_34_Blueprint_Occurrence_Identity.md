# Architect Question #34 — **blueprint occurrence identity: where do the bytes come from?**

> ⛔⛔ **NOT RELAYED.** The NotebookLM architect is generally unavailable (`2026-08-16` user ruling).
> ⭐ **This document is the deliverable** — it forces a decision with real blast radius into
> decision-shaped options. **Resolved JOINTLY with the user**, recorded here.
>
> 📄 **Context:** [`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md) §4 (multi-occurrence) ·
> §4.1 — *"Blueprint: own slot per **asset**; ⛔ **same asset twice collapses** (identity =
> `blueprintId`)"*. ⭐ **The user accepted the multi-occurrence cost (`2026-08-16`).**
> ⚠ **This question is the one thing that design does NOT answer: the slot table has no spare bytes.**

---

## 1. The measurement — ⭐ **there is no room, and the header cannot help**

| | measured `2026-08-17` |
|---|---|
| `BlueprintSlotEntry` | `int BlueprintId(4) · uint InstanceVersion(4) · ushort PayloadOffset(2) · ushort PayloadSize(2) · uint StructureHash(4)` = ⭐ **exactly 16, no padding, no spare bit** |
| `SlotEntrySize` | `= 16`, a **public const** used by all three tier components and by `Migrate` |
| tiers | `1024`/`4096`/`16384` ⇒ **MaxSlots 4 / 8 / 16**, slot tables **64 / 128 / 256**, payload **928 / 3936 / 16096** |
| ⛔ `BlueprintBlackboardHeader.Reserved` (8 B, unused) | ⛔ **wrong granularity** — the header is **per entity-tier**; one entity hosts many slots |
| ⛔ `InstanceVersion` | ⛔ **taken** — the latent-cursor staleness token *(bumped on hard reload, compared against `BlueprintLatentCursor.InstanceVersion`)* |
| `StructureHash` in the entry | already *"truncated from ulong to fit the 16-byte slot-entry budget"* ⇒ ⭐ **the budget has been binding once already** |

⚠ **This is NOT the Batch-69 case.** There we refused to grow the entry for a **tick counter no
simulation code reads**. ⭐ **An occurrence key is read on every slot lookup** — the trade is different,
and it must be decided on its own merits rather than by that precedent.

---

## 2. `Q34-A` — **where does the discriminator live?**

| | option | cost, measured | verdict |
|---|---|---|---|
| ⭐ **A** | **widen `BlueprintSlotEntry` to 20 B**: add `uint OccurrenceKey` *(`0` = the default/only occurrence)* | slot tables **80 / 160 / 320** ⇒ payload **912 / 3904 / 16032** — ⭐ **loses 16 / 32 / 64 bytes per entity**, ~**1.7 % / 0.8 % / 0.4 %**. Touches `SlotEntrySize`, `Initialize`, `Migrate`, the three tier `const`s and their doc comments | ⭐⭐ **RECOMMENDED LEAN** — the cost is measurable and small; the field is read on the hot lookup, so it earns its bytes |
| **B** | **compose into the existing field**: store `FNV-1a(blueprintId, instanceKey)` in `BlueprintId` | ⛔ **`BlueprintTickSystem` and `DetachFromEntity` both need the REAL `blueprintId`** to reach `registry.TryGetById` ⇒ needs a parallel side map, i.e. **a second lookup structure** | ⛔ **rejected on measurement** — it trades 4 bytes for a per-entity map |
| **C** | **status quo** — identity stays `blueprintId`, one occurrence per asset per entity | zero | ⛔ **that IS the gap** the user accepted the cost of closing |
| ⛔ **D** | reuse `InstanceVersion` | zero | ⛔⛔ **two meanings on one field — the trap this programme keeps finding** |

---

## 3. `Q34-B` — **who mints the key?**

⭐ **BTree is the template throughout** *(design §4.1)*: `FNV-1a(assetGuid, nodeVisualId)` — an
**authoring-stable** id, so the same node gets the same region every run. ⚠ **A blueprint attached by a
runtime event has no node id.**

| | option | |
|---|---|---|
| ⭐ **A** | **caller-supplied `InstanceKey` on the attach event** *(`0` = default)*; detach and lookup take the same key | ⭐⭐ **LEAN — deterministic and replayable.** The caller that decides *"attach a second copy"* is the only one that can name it |
| **B** | auto-increment per `(entity, blueprintId)` | ⛔ **order-dependent** ⇒ a replay that reorders two attaches swaps their state |
| **C** | hash of the attaching **host occurrence** *(when nested)* | ⭐ **not exclusive with A** — it is what a HOST would pass as `InstanceKey`. ⇒ **fold into A as a convention, not a mechanism** |

---

## 4. `Q34-C` — **what does the 3-arg `TryGetSlotOffset(memory, blueprintId, out offset)` mean afterwards?**

⚠ It is the **hot path**, and it is called from `BlueprintTickSystem` and `HasInitializedSlot`.

| | option | |
|---|---|---|
| ⭐ **A** | it keeps meaning **"the occurrence with key `0`"**, and a 4-arg overload takes the key | ⭐⭐ **LEAN** — every existing call site stays correct **by construction**, and the migration is additive *(the same shape `TryGetSlotOffset` already used when the `StructureHash` overload was added)* |
| **B** | it returns the **first** match regardless of key | ⛔ **silently picks an arbitrary occurrence** — the failure mode is a wrong-but-plausible read, the worst kind this programme has hit |
| **C** | delete it; force every caller to pass a key | ⚠ honest but noisy; ⭐ **A already forces the question at each site that needs it** |

---

## 5. Blast radius — ⭐ **what this decision does NOT touch**

| | |
|---|---|
| ✅ **`StructureHash`** | the slot entry is **runtime storage**, not compiled asset shape ⇒ ⛔ **a widening must NOT move `StructureHash` or `persistence-shape.txt`.** If it does, something else changed |
| ✅ **the scenario format** | `BlueprintAssignmentDto` is already a **per-assignment list** ⇒ a second assignment of one asset is a **format-compatible** addition *(`Q33-D`'s note)* |
| ⚠ **the recorded frame** | tier components are `[DataPolicy(NoSave)]` = **snapshotted AND recorded** ⇒ **the recorded component grows by the slot-table delta.** Small, but say it out loud |
| ⚠ **Track C row identity** | `(AssetId, Entity, VariablePath)` gains a **fourth** component. ⭐ Already noted as a carry-forward in the plan; **do not build for it until this lands** |

---

## 6. ✅ RESOLVED — `2026-08-17`, with the user

> ⭐⭐⭐ **User: "approved"** on all three leans, then — asked whether the cost is *"just a few bytes"* —
> ⭐⭐ **"lets do it now."**

| | answer |
|---|---|
| **`Q34-A`** | ✅ **A — widen `BlueprintSlotEntry` to 20 B** with `uint OccurrenceKey` *(`0` = default)* |
| **`Q34-B`** | ✅ **A — caller-supplied `InstanceKey` on the attach event** |
| **`Q34-C`** | ✅ **A — the 3-arg `TryGetSlotOffset` keeps meaning "key `0`"**; a 4-arg overload takes the key |
| **when** | ⭐ **NOW** — scheduled, no longer "later" |

---

## 7. ⭐⭐⭐ The refinement the user's question forced — **three cases, TWO mechanisms**

> ⭐ **User: *"couldn't blueprint multi-occurrence happen if two HSM regions run in parallel? or two
> blueprint nodes?"*** ⇒ 📐 **I measured, and the demand is real but it does NOT all land here.**

### 🔴 The measurement that reframes it — **the slot table's `BlueprintId` field is ALREADY polymorphic**

⛔ **It is not always a blueprint id.** `BlueprintSharedState` and `StatefulBTreeActionBinder` put
`ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)` in **that same field**:

| scope | key |
|---|---|
| ⭐⭐ **`Node`** | `FNV(assetId ++ nodeVisualId)` — ⭐ **the occurrence is ALREADY in the key** |
| `Behavior` | `FNV(assetId ++ variableId)` |
| `Entity` | `FNV(variableId)` |

⇒ ⭐⭐ **"compose the discriminator into the existing field" is not hypothetical — it is SHIPPED**, for
the slots that need it. ⚠ **§2's rejection of option `B` stands only for the ATTACH path**, which must
recover the real id to reach `registry.TryGetById`. **Both uses coexist in one table, correctly.**

### ⭐ The three cases

| case | today | owner |
|---|---|---|
| **two BTree nodes hosting one blueprint** | ✅ **already fine, twice over** — the host owns layout *("BTree owns layout, blueprint provides `TickCore`")*, and stateful slots key at `Scope.Node` | ⛔ nothing to do |
| 🔴 **two HSM regions running one action** | ⛔⛔ **COLLAPSES SILENTLY** — `hash(method @ fieldOffset)` has no region or state in it ⇒ both write the same bytes | ⭐ **`E3`** — ⛔ **not this question**; it is a KEY change, not a slot-table change |
| ⚠ **one Instance attached twice** | ⭐ **REFUSED, not collapsed** — `AttachToEntity` finds the slot and returns `AlreadyAttached` *(idempotent no-op)* | ⭐⭐ **THIS question** |

⇒ ⭐⭐⭐ **The dangerous case is `E3`'s, and it does not need these four bytes.** `Q34` buys the
**capability** *("attach the same asset twice deliberately")*, not a correctness fix — ⚠ **say that
plainly, because "multi-occurrence" names both and only one of them is a live defect.**

### ⭐⭐ A constraint this puts on `E5` — **written down here because nowhere else has it**

⛔ **`E5` (a state hosting a sub-blueprint via `SubtreeAssetId`) is unbuilt, and where a hosted
subtree's storage comes from has never been decided.** Two regions hosting the same asset is exactly
the user's scenario.

| route `E5` could take | consequence |
|---|---|
| ⭐⭐ **provision through `ComputeStatefulSlotKey` with the region/state in the key** *(BTree's pattern)* | ✅ **solved by construction** — reuses a shipped key algorithm; the hosted occurrence is not visible to the registry, and has no reason to be |
| ⛔ provision through `AttachToEntity` | inherits this whole question, plus the registry lookup it does not need |

⇒ ⭐ **RULED: `E5` takes the key route.** ⚠ **If a batch finds that it cannot, that is a STOP** — it
would mean a hosted subtree needs registry identity, which nothing has argued for.

---

## 8. Status

| | |
|---|---|
| **raised** | `2026-08-17`, coordinator, from the Batch-70 scoping measurement |
| **state** | ✅ **RESOLVED and SCHEDULED** — Batch 72 |
| **did not block** | ⭐ the Instance params seam *(Batch 70)* — it changes the **payload**, not the **slot entry** |
