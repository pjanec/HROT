# PLAN — what is left, with the parameter-as-variable model folded in

> **Coordinator, `2026-08-15`.** ⭐ **Supersedes the task lists in
> [`DESIGN_Variable_Details_And_Live_Values.md`](DESIGN_Variable_Details_And_Live_Values.md) §8 and
> [`PLAN_Cross_Host_Sequencing.md`](PLAN_Cross_Host_Sequencing.md) §6** — those were two lists for one
> programme. ⛔ **This is the single list.**

![remaining work](PLAN_Remaining_Work.svg)

---

## 1. ⭐⭐⭐ The model the plan is now organised around

⭐⭐ **The cross-host design and the Q32 design reached the same model independently** — `Explainer:269`
*"Parameters, working state and asset variables are not three things"* over axes **`Role` × `Scope`**,
which is our one cell in different words:

| cell | what it is | where it lives |
|---|---|---|
| **`Parameter` = (Input, Asset)** | scenario-writable, **packed at offset 0**, `Pack`, 100 B budget | the inline params region |
| ⭐⭐ **`Variable` ∪ `WorkingState` = (State, Asset)** | **ONE cell.** Batch 56 made the emitters agree | `State` (Instance) / `WorkingState` (AiPrimitive) |
| **graph locals = (State, Graph)** | same `DeclarationKind` tag as asset variables | appended after the asset's own storage |

⇒ ⭐⭐⭐ **What this buys the plan: the remaining tasks split by WHICH LAYER they touch — not by host,
and not by which word the old three-list model used.** ⛔ **And the standing constraint over all of it
is ruling 9: never two implementations of one concept.**

⚠ **One sentence in the cross-host design is now false and its `D2` hedge rests on it** —
`Design_Behavior_Asset_Parameter_Model.md:72` calls the kinds *"storage of different dispatch kinds
**that never coexist**."* ⭐ **True of the corpus, false of the model** since `U-12`. **Retire the hedge.**

---

## 2. ✅ Done — merged at `bc79be664`, all eight gates coordinator-run

| batch | item | ⭐ what it actually bought |
|---|---|---|
| **56** | one cell, one emit path | the union the whole plan now walks |
| **58** | `W1` — id collision gate | ⭐ built as an **analyzer**, because a generator cannot see another generator's output |
| **57** | `S1` — AiPrimitive state metadata | ⭐⭐ a **consumer with no producer** for its entire life; 32 assets were invisible to the debugger |
| **59** | `W3` — stub registrations deleted | production now registers **exactly one** HSM id |

---

## 3. ⏳ In flight — dispatched, to run back to back

| batch | items | ⭐ note |
|---|---|---|
| **60** | `W2` + `W4` — runtime layout gate, then the layout it guards | ⭐⭐ **blast radius measured ZERO** (no shipped asset carries a `Vector3`-class variable) ⇒ **cheapest moment this change will ever have.** ⛔ **The corpus cannot witness it — the constructed asset is the only witness** |
| **61** | `BP-247` · `W5` · `W6` · `W7` · `S2` | five items, **per-item STOP conditions**. ⛔ **`W5` was corrected pre-dispatch — the constant fold is not buildable** |

⇒ ⭐ **After these two, PHASE A is complete** and layout/size is closed for every type.

---

## 4. ⏭ Track B — finish struct support *(headless)*

⭐ **Why it comes before Track C: the value column cannot render a struct until `S3` lands.**

| | work | ⭐ why it matters |
|---|---|---|
| **`S3`** | `MarshalFromBytes` — **one generic struct arm** (`PtrToStructure`) + **assembly-qualified** `ResolveType` | ⭐⭐ **closes `BP-01`** — `Vector2/3/4`, `Quaternion`, `FixedString32/64/128`, **seven of the eighteen offerable types**, fall through to `return bytes`. *"Watch shows raw hex"* was never a panel bug |
| **`S4`** | fixed lists: stop dropping `Capacity` in the fallback | ⛔ a declared list **silently degrades to a scalar** |
| **`S5`** | **ONE picker** — `EditorOfferableTypeIds` ∪ `SelectableTypeIds` | ⛔ **a `Parameter` cannot be given a struct through the picker today — and four ship** ⇒ ruling 9, in the UI |
| ⭐ **rail** | pin the marshaller against the **closed 18-type set** with a reflection test | ⚠ **would have caught `BP-01` long ago**; extends `U-8` from *"every offered type compiles"* to *"and can be shown and edited"* |

---

## 5. ⏭ Track C — the panels *(the user-visible half; ⛔ gated on the visual check)*

⚠⚠ **Renamed to `C1…C7`.** ⛔ **The design doc's §8 labels `57`–`61` are ITEM labels and now collide
with real batch numbers 56–61** — that collision would have reached the implementation session.

| | work |
|---|---|
| **`C1`** | Details hosts the shared variable list + selection routing *(globals ⇄ locals-of-current-graph)* |
| **`C2`** | the **one Value column** whose meaning switches on run state + blueprint's `ILiveValueProvider` and `UpdateVariableDefaultValueJson` |
| **`C3`** | **StructEdit dialog** (three-dot **and** double-click) · the **not-running** write ⇒ JSON default |
| **`C4`** | **Watch panel**: real refresh · editing · **nothing before the run** ⛔ *(none of the three is true today; `HandlePinValueChanged` is an empty body)* |
| **`C5`** | tier-2 + tier-3 write halves (`MarshalToBytes` · `TrySetFieldRaw`) · ⭐ **write BOTH copies** (snapshot **and** live) |
| **`C6`** | retire `BlueprintVariablesWindow` (`U-16`) + ⛔ **delete the dead `IStructEditDrawer`/`DrawerRegistry` chain** |
| **`C7`** | the **shared outline** across HSM / BTree / Blueprint — ⛔ **only after Details works for blueprints** |

### ⚠⚠ Two prerequisites that are NOT panel work — do them before `C5`

| | |
|---|---|
| 🔴 **the surgical ECB field-write** | ⛔ every ECB write is **whole-component**, and `Blackboard1024` is **shared across BTree/HSM/Blueprint at disjoint offsets** ⇒ a whole-component write **clobbers other subsystems**. ⭐ **The read side already slices `8 + OffsetBytes`; the write is its mirror.** 📌 `Fdp.Core`, engine-wide, one command |
| 🔴🔴 **the paused snapshot-vs-live pass** | `DataBreakpointManager:123` — `ActiveView => _isPaused ? _preTickSnapshot : _liveRepo`, and `:470-473` **rewinds `_liveRepo` to start-of-tick** ⇒ a write queued while frozen **would not appear at all**, and the rewind can **discard** a write near a pause boundary. ⚠ **Cited from two code sites, not run — a strong signal, not a proven mechanism.** ⭐ **It is a `Hrot.Diagnostics.Breakpoints` design question, not a panel one** |

---

## 6. ⏭ Track D — the cross-host extensions *(headless, independent of B and C)*

⭐ **The natural filler whenever the visual check blocks Track C.**

| | work | state |
|---|---|---|
| **`W13`** | retire the standalone stride path — one projection formula repo-wide | ✅ **unblocked** |
| **`W9`** | FQN key unification + `SlotKind` — **one re-bake, one verification** | ✅ **UNBLOCKED — `D1` ruled OPEN**, so `#29`-A's tagged carrier stands |
| **`W8`** | reserved input variable + generated deserialize, **both** emitters | ⚠ **needs `D2`** · closes `DEBT-AIB-021` ⇒ **a managed asset parametrized from scenario JSON end to end, today impossible** |
| **`W10`** | initializer picker — ⭐ **reuse `IBehaviorActionCatalog`**, do not build a picker | needs `W8` |
| **`W11`** | asset-driven HSM thunk emission (`HSM-016`) | needs `W9` |
| **`W12`** | authored `Construction` initializer + world-singleton vocabulary | ⛔⛔ **largest new surface, UNBUDGETED — scope it before starting** |

---

## 7. ⛔ Open — and not one of these is a code question

| | |
|---|---|
| **`D2`** | which `DeclarationKind` is the reserved input variable ⇒ ⚖️ **measured lean `Variable`** *(`Pack` skips it, and offset 0 IS the packed region)*; **Batch 56 dissolved the per-kind half.** ⭐ **Wants a nod, not research** |
| **`D3`** | the orchestrator emitters are **proven dead** — ⭐ **delete or wire? disposition only** |
| ⛔ **the visual check** | **still suspended by your ruling.** ⚠ **Track C cannot be finished without it** — its whole deliverable is surfaces no headless test can see drawn |
| **the held HSM reply** | drafted, not sent. ⚠ **Under the freeze it buys design progress, not code** |

### 📌 Filed, not fixed

| | |
|---|---|
| **`BP-241`** | `--canonicalise` split out of Batch 55 — a doc-type-agnostic tool needs a per-doc-type repair seam |
| 🔴 **`BP-242`** | `GeneratedBlueprintSchemaCatalog` — a **second, independent `*.bp.json` parser**. ⛔ **Its v2 blindness is fixed; the underlying behaviour — returning a silently WRONG answer instead of an error — is not** |

---

## 8. ⭐ The order, in one line

**60 → 61 → Track B → Track C**, with **Track D inserted wherever C is blocked.**
⛔ **The only hard dependency is `S3` before `C2`** — everything else is preference.
