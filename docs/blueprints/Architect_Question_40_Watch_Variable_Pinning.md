<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 3 (the recommendations), as amended by section 0 - RESOLVED 2026-08-18
stale-below: in section 3, read Q40-B/C/F through section 0's amendments; section 5's
  "BTree/HSM out of scope" is OVERTURNED by section 6.
note: user-requested design, 2026-08-18. Not relayed to any architect; resolved jointly
  with the user per the 2026-08-17 ruling.
-->

# ARCHITECT QUESTION 40 — **pinning a VARIABLE to the Watch panel**

> ⭐⭐ **User request, `2026-08-18`, verbatim:** *"lets pls design the watch var pinning. a simple new
> entry in mybleprint context menu on variable record is fine. must work for any variable type
> (including locals). watch evaluates every brain tick."*
>
> ⛔ **No architect will answer this** *(`2026-08-16` ruling)*. ⭐⭐ **I analyse and recommend; the user
> approves.** ⭐ **Every sub-question below carries a RECOMMENDED ANSWER** — reply *"approved"*, or name
> the one you want changed.

---

## 0. ✅✅ RESOLVED — **user, `2026-08-18`** *(this section AMENDS §3; read it first)*

> ⭐⭐ **Verbatim:** *"pull for variables - every brain tick. not sure how pin based watches work -
> maybe they need push (pin is not a variable to be polled). non running graph locals - stale grayed.
> adding/removing variable watch must be possible whenever sim in planning or running but paused. of
> course watch must work for also for hsm and btrees. otherwise ok"*

| | outcome |
|---|---|
| **`Q40-A`** identity | ✅ **approved as recommended** |
| **`Q40-B`** feed | ✅ **poll for variables** — ⭐⭐ **AMENDED: pins KEEP their push** *(below)* |
| **`Q40-C`** where | ⚠⚠ **CHANGED — my recommendation was wrong for cross-host** *(below)* |
| **`Q40-D`** stale locals | ✅ **stale, greyed** — ⭐ the user picked the marked option, not raw |
| **`Q40-E`** entity | ✅ **approved as recommended** |
| **`Q40-F`** gesture | ⚠ **NARROWED — `Planning` and `Paused` only** *(below)* |
| **`Q40-G`** canvas stub | ✅ **approved as recommended** |
| **cross-host** | ⛔⛔ **§5's *"BTree/HSM out of scope"* is OVERTURNED** — see §6 |

### ⭐⭐⭐ 0a. `Q40-B` amended — **TWO FEEDS, ONE ROW TYPE**

> ⭐⭐ **User:** *"pin is not a variable to be polled."* ⭐ **Correct, and it is the sharper statement.**

| feed | for | why |
|---|---|---|
| ⭐ **PUSH** — `OnPinValueChanged` *(ships)* | **pins** | ⛔ **a pin is a TRANSIENT value at an edge, not a stored field.** There is no address to poll — the value exists only as it flows |
| ⭐ **POLL, every brain tick** | **variables** *(incl. locals)* | ⭐ **a variable IS a stored field with a byte offset** ⇒ polling is the natural read, and `R-49` forbids emitting a push per variable |

⛔⛔ **This is NOT a ruling-9 violation, and the design already has the precedent:** 📌 §1a gives the
table **`SectionSource`** *(one asset)* and **`PinnedSource`** *(arbitrary assets)* — ⭐ **two SOURCES
feeding one control.** ⇒ **push and poll are two sources of `VariableRow`, not two implementations of
one concept.** ⭐ **They converge at the row, and everything downstream is already shared** *(Batch 83)*.

### ⚠⚠ 0b. `Q40-C` CHANGED — **the poll must NOT live in `BlueprintDebugSession`**

⛔ **My §3 recommendation `C2` put the poll in `BlueprintDebugSession`'s tick hook.** ⚠ **The user's
cross-host requirement makes that wrong** — it would make the shared Watch panel depend on the
blueprint session, which is exactly the split `U-6` spent a batch removing.

⭐⭐⭐ **And the right seam ALREADY EXISTS, cut deliberately for this:**
📌 **`BlueprintAssetTickSource.cs:12-21`, verbatim:** *"Batch 68 cut the seam and left it open on
purpose: `VariableRow.AssetTick` is a per-row [delegate] … (BTree, HSM and blueprint rows).
⛔ **Teaching it about `BlueprintAssetTick` would make the [shared layer] blueprint-specific.**"*

⇒ ⭐⭐ **`C2′` — the poll is driven PER ROW by the row's own `AssetTick`**, in `Hrot.Editor.AiShared`.
⭐ **Each host supplies its tick source and its byte reader; blueprint's already ships.**
⭐ **This also honours §1a's *"in Watch, rows tick at different rates ⇒ no panel-wide tick."***

### ⚠ 0c. `Q40-F` NARROWED — **the watch set is mutable only when nothing is racing it**

> ⭐⭐ **User:** *"adding/removing variable watch must be possible whenever sim in planning or running
> but paused."*

| sim state | add / remove a watch |
|---|---|
| **Planning** *(cluster `Idle` / `*Edit`)* | ✅ **allowed** — pinning ahead of a run is the normal workflow |
| **Paused / stepping** | ✅ **allowed** |
| ⛔ **free-running** | ⛔⛔ **FORBIDDEN** — ⭐ the poll list is read by the tick; mutating it while the sim runs is a race |
| **Replay** | ⛔ **forbidden** *(same reason; state it rather than leaving it to fall out)* |

⭐⭐ **And it is GREYED WITH A TOOLTIP, never a click that dead-ends** — 📌 the user's `2026-08-17`
ruling: *"same information value, no false expectations."*
⭐ **The state comes from `R-69`'s cluster state** — ⛔ **not a fourth notion of "running."**

---

## 1. 🔴 Why this needs a design at all — **the watch model cannot express a variable**

📐 **Measured `2026-08-18`:**

| | |
|---|---|
| the record | `Watch(WatchId, Guid assetId, Guid graphId, **Guid pinId**, string displayName, Type expectedType)` — ⛔ **pin-keyed** |
| the lookup | `_watchesByPinString` — ⛔ **a dictionary from pin string** |
| ⭐⭐ **how a value ARRIVES** | `OnPinValueChanged<T>(Entity self, string pinId, T value)` — ⛔⛔ **PUSHED by emitted code at a pin.** ⭐ **Nothing polls** |

⇒ ⭐⭐⭐ **A variable is not a pin, and nothing pushes for it.** ⛔ **This is not "add a menu item" — the
feed does not exist.**

📌 **And `R-68`: nothing can add a watch TODAY either** — the canvas menu's `"Watch this Value"` sits
inside `ImGui.BeginDisabled()` *(`CanvasRenderer.cs:684`)* and `CommandCatalog.ToggleWatch` has **no
invoker**. ⇒ ⭐ **we are not extending a working feature; we are building the first one.**

---

## 2. ⭐⭐ The measurement that makes this SMALL — **locals are not special**

📐 **`GraphLocalSlots` are laid out in the SAME emitted struct as everything else** — both
`AiPrimitiveEmitter:109` and `InstanceEmitter:156` walk them, and `CSharpEmitter:476` folds them into
the same layout scan.

⇒ ⭐⭐⭐ **A graph local has a BYTE OFFSET exactly like an asset variable** ⇒ 📌 **`R-35`'s membership
rule** *("in the variable model IFF it has a byte offset in a struct THIS ASSET emits")* **already
admits them** ⇒ ⛔ **no second mechanism is needed for the user's *"including locals"*.**

⭐ **And the surgical READ already ships:** `BlueprintDebugSession:1308-1312` —
`int start = 8 + field.OffsetBytes; … bytes.Slice(start, field.SizeBytes)`.

---

## 3. ⭐⭐⭐ The sub-questions, each with a RECOMMENDATION

### `Q40-A` — **what identifies a watched variable?**

| | option |
|---|---|
| **A1** | extend `Watch` with an optional variable origin beside `pinId` |
| ⭐ **A2** | **key the pinned set by `VariableRow.Origin`** — `(AssetId, Entity, Section, VariablePath)`, which **already exists** |
| **A3** | a parallel `VariableWatch` type |

> ⭐⭐ **RECOMMEND `A2`.** 📌 Batch 83 already gave Details and Watch **one row type and one formatter**;
> the design's own words are *"entity is part of row identity… the key is `(AssetId, Entity,
> VariablePath)`, ⛔ not `(asset, variable)`."* ⇒ **the identity question is already answered — reuse it.**
> ⛔ **`A3` is ruling 9's prohibition** *(a second implementation of one concept)*.

### `Q40-B` — **how does a value arrive each brain tick?**

| | option |
|---|---|
| ⛔ **B1** | **emit a push per watched variable**, like pins |
| ⭐ **B2** | **POLL at the tick — read the blackboard by offset** |

> ⭐⭐⭐ **RECOMMEND `B2`, and `B1` is FORBIDDEN.** 📌 **`R-49`: *"NEVER GENERATE PER-VARIABLE CODE."***
> ⚠ **`B1` also cannot work retroactively** — pinning would require a recompile, which is absurd for a
> debugging gesture. ⭐ **`B2` reuses the surgical read that ships** *(§2)*.

### `Q40-C` — **where does the poll live?**

| | option |
|---|---|
| ⛔ **C1** | the Watch panel polls when it draws | ⛔ **frame-rate, not tick-rate** — misses changes between draws, and the user said *"every brain tick"* |
| ⭐ **C2** | **`BlueprintDebugSession` polls in its per-tick hook** and writes `LastValueBytes` |
| **C3** | a new per-tick system |

> ⭐⭐ **RECOMMEND `C2`.** ⭐ **It is where `OnPinValueChanged` already writes**, so a variable watch and a
> pin watch become the same row downstream ⇒ ⛔ **the panel needs NO change** *(Batch 83 already made it
> render `VariableRow`s through `WatchRowBridge`)*. ⭐ **`C3` is a new tick consumer for no gain.**

### `Q40-D` — **a local whose graph is not currently executing: what does it show?**

| | option |
|---|---|
| **D1** | its slot's raw contents *(a leftover from the last invocation)* |
| ⭐ **D2** | the same, **but marked** — the slot is live storage, the VALUE is stale |
| **D3** | hide the row until the graph runs |

> ⭐ **RECOMMEND `D2`.** ⛔ **`D1` shows a leftover as if it were current** — the exact class of silent
> lie this programme keeps finding. ⛔ **`D3` makes rows appear and disappear** — 📌 the design already
> rejected that shape for sections: *"a section that appears and disappears reads as a broken feature."*
> ⭐ **`Watch.IsStale` ALREADY EXISTS and already renders greyed** *(design §1a)* ⇒ **reuse it, do not
> coin a marker.**
> ⚠ **This is the sub-question I am least certain of** — if you would rather see the raw slot with no
> marker at all *(`D1`)*, say so; it is cheaper and defensible.

### `Q40-E` — **which entity?**

> ⭐⭐ **RECOMMEND: the entity is captured at PIN TIME from the row's own `Origin`**, defaulting to the
> session's current `SetEntityFilter`. 📌 **The design already ruled this** — *"the same asset on two
> entities has two different values."* ⇒ ⛔ **a watch that follows the filter would silently change
> subject when you change the filter.** ⭐ **`Q40-A`'s key already carries `Entity`; nothing new.**

### `Q40-F` — **the gesture**

> ⭐⭐ **RECOMMEND: a toggle — `"Watch this variable"` / `"Stop watching"` — on the My Blueprint row
> context menu** *(as the user specified)*, ⭐ **AND on the Details table row.**
> 📌 **Design §4: *"Two items, on BOTH the My Blueprint row and the table row — identical everywhere."***
> ⇒ ⛔ **a gesture that exists on one surface only re-creates the split `U-6` just removed.**
>
> ⭐ **Available while PLANNING too** — the row appears in Watch immediately and ⭐ **shows nothing until
> the run**, which is exactly what row `59b` specifies. ⛔ **Do not grey the menu item when the sim is
> down**: pinning ahead of a run is the normal workflow, not an error.

### `Q40-G` — **the disabled canvas stub**

> ⭐ **RECOMMEND: WIRE IT, do not delete it.** 📌 **`R-13`: say which it is** — ⭐ this is **neither
> duplicate code nor dead**; it is a **stub for the unbuilt feature this document designs.** ⇒ once
> `ToggleWatch` has a real implementation, `"Watch this Value"` leaves `BeginDisabled()` and invokes
> `CommandCatalog.ToggleWatch` — ⭐ **one command, two entry points** *(pin and variable)*.

---

## 4. ⭐ Blast radius — **why this is smaller than it looks**

| | |
|---|---|
| ⭐⭐ **no emitter change** | `B2` polls; `R-49` holds |
| ⭐⭐ **no panel change** | Batch 83 already renders `VariableRow`s from a bridge |
| ⭐ **no new identity** | `Q40-A` reuses `VariableRow.Origin` |
| ⭐ **no locals special case** | §2 — they are ordinary offsets |
| ⚠ **the real work** | a **poll list** on the debug session, the per-tick read, and the toggle command |
| ⚠ **the real risk** | ⛔ **polling N watched fields every brain tick.** ⭐ **Bound it**: the read is a slice of a component already in hand, but **state the cost** and cap or measure it rather than assuming |

---

## 5. ⛔ NOT decided here

| ⛔ | |
|---|---|
| **watching across ASSETS in one panel** | ⭐ the design already requires it *(`"the watch window must allow for selected variables from different assets"`)* and `Q40-A`'s key supports it — ⚠ **but the poll would then span sessions.** 📌 **Out of the first slice; state it as the next question** |
| ⛔ ~~**BTree / HSM**~~ | ⛔⛔ **OVERTURNED by the user, `2026-08-18`** — see **§6** |
| **editing from the Watch row** | ✅ already built *(Batch 83, ruling 11)* — ⛔ nothing to decide |

---

## 6. ⭐⭐⭐ CROSS-HOST — **required, and it has ONE hard dependency**

> ⭐⭐ **User:** *"of course watch must work for also for hsm and btrees."*

### ⭐ What is ALREADY host-neutral — **more than expected**

| ✅ | |
|---|---|
| the **row identity** | `VariableRow.Origin` carries `AssetId` — ⛔ nothing blueprint-specific |
| the **tick seam** | ⭐⭐ `VariableRow.AssetTick`, **cut open for this in Batch 68** *(§0b)* |
| the **table + formatter + dialog** | `Hrot.Editor.AiShared` *(Batches 82–83)* |
| ⭐⭐ **the SURFACE to hang the gesture on** | **`AiVariablesWindow` is registered on ALL THREE perspectives** — ⛔ **so the gesture does NOT wait for row 61's Details host** |

⇒ ⭐ **`R-60` blocks the DETAILS panel on BTree/HSM. It does NOT block the watch gesture** — the shared
variables table is already there.

### ⭐⭐⭐ 6a. **THE USER WAS RIGHT — and I over-scoped this** *(`2026-08-18`)*

> ⭐⭐ **User:** *"variables share all the implementation so why would that be different, when available
> for blueprints it must work more or less for free for hsm and btrees."*

📐 **Measured, and the answer is YES — most of it is already shared, including the store:**

| ✅ already shared | evidence |
|---|---|
| ⭐⭐⭐ **the pinned-variable STORE** | **`AiWatchWindow._pinned` is a `PinnedVariableRowSource`**, with a public `Pinned` accessor — ⭐ **the store exists** |
| ⭐⭐ **the window itself** | **`AiWatchWindow` lives in `Hrot.Editor.AiShared`** and is built by the **shared** `PerspectiveWorkspaceRegistrar:337` ⇒ **all three perspectives** |
| ⭐⭐ **its CONTENT across perspectives** | fed by **`_bpManager`, passed to all three registrars** *(`:2128` `:2152` `:2164`)* ⇒ 📌 **the user's *"shared no matter what perspective"* is ALREADY TRUE** |
| ⭐ **breakpoints** | `AiBreakpointsWindow`, same registrar, same shared manager |
| ⭐⭐ **the READ is not session-bound** | `BlueprintDebugSession:1301-1320` needs an **`ISimulationView`**, the **shared `Blackboard1024`**, a `StructureHash` guard and a field layout — ⛔ **nothing blueprint-specific except the BASE OFFSET and where the layout comes from.** ⭐ **Both are DATA, not machinery** |

⇒ ⭐⭐ **What is actually missing is small:** **(a)** the gesture that calls `Pinned.Add(...)`, **(b)** the
per-tick poll that gives those rows a value, **(c)** the per-host base offset.
⛔ **`R-70` blocks BREAKPOINTS / pause / step on BTree/HSM — it does NOT block the watch poll.**
⚠ **My §6 slicing said otherwise and was wrong.**

### 🔴🔴 6b. **AND THERE ARE TWO WATCH WINDOWS** — ⭐ ruling 9's target

| window | assembly | registered by | fed by |
|---|---|---|---|
| ⭐ **`AiWatchWindow`** | **`Hrot.Editor.AiShared`** | the **shared** registrar — **all three perspectives** | `_bpManager` + `PinnedVariableRowSource` |
| ⚠ **`WatchPanelWindow`** | ⛔ **`Hrot.Blueprints.Editor`** | `BlueprintWindowRegistrar:53` — **blueprint only** | `_session` |

⚠⚠ **`AiWatchWindow`'s own empty-state text already promises the missing gesture:**
> *"No pinned variables. **Pin one from the Variables table.**"* · *"No watch entries. **Right-click a
> breakpoint → Mark as Watch.**"*

📌 **That is what the user reported seeing** *(`2026-08-17`: "Watch window shows two sections, but 'No
watch entries', 'No pinned variables'")* ⇒ ⭐⭐ **the user has been looking at `AiWatchWindow`.**
⚠⚠ **Batch 83's `BP-01` fix landed on `WatchPanelWindow`** — ⭐ a real fix to a real defect, ⛔ **but not
necessarily the window in front of the user.**

⇒ 📌 **`R-13` — name which it is:** ⭐⭐ **duplicate SURFACE *and* duplicate CODE** *(both render watch
rows)* ⇒ **ruling 9 applies.** ⛔ **Not a rush removal**: ⭐ **the gesture targets `AiWatchWindow`**, and
`WatchPanelWindow`'s retirement is a question for row **60 / `U-16`**, with its own evidence.

### 🔴 6c. The dependency that IS real — **the AI debug sessions** *(narrower than I said)*

📐 **Measured `2026-08-18`:**

| | |
|---|---|
| `HsmDebugSession` · `BTreeDebugSession` | ⭐ **exist**, complete, both `: AiDebugSessionBase` |
| production construction sites | ⛔⛔ **ZERO — tests only** |
| the composition root's own words | 📌 `EditorSubsystem:2183`: *"BTree/HSM debug sessions are **not yet attached/working** — intentionally null until wired."* |

⇒ ⛔⛔ **THE THIRTEENTH INSTANCE of the pattern**, and it is the whole cross-host gap: ⭐ **a variable
watch on BTree/HSM has nothing to observe until those sessions are wired.**

### ⚖️ Recommended slicing — ⭐ **build host-neutral from day one, light up in two steps**

| slice | what | why |
|---|---|---|
| ⭐ **1** | **the mechanism, in `AiShared`** — the gesture calls **`AiWatchWindow.Pinned.Add`**, the poll is driven per row by `AssetTick`, the base offset is **host DATA**. **Proven on Blueprint**, whose tick source and layout ship | ⛔ **the acceptance criterion is NO blueprint-specific code in `AiShared`** — ⭐ **not "it works on blueprint"** |
| ⭐ **2** | **each host supplies its tick source + base offset** *(data, not machinery)* | ⭐⭐ **this is the *"more or less for free"* the user expects — and if it is NOT nearly free, slice 1 leaked host knowledge** |
| ⚠ **3** *(separate)* | **wire `HsmDebugSession` / `BTreeDebugSession`** | ⛔ **needed for BREAKPOINTS / pause / step on BTree/HSM, and for `Q40-H`'s entity discovery** — ⭐ **not for the watch poll itself** |

⚠⚠ **The per-host BASE OFFSET is the subtle half.** 📌 `R-65`: `Blackboard1024` is **ONE component
shared by BTree, HSM and Blueprint at DISJOINT offsets**, and the blueprint read path hard-codes
`8 + field.OffsetBytes`. ⇒ ⭐⭐ **the base belongs to the HOST, and must be owned in exactly one place**
— 📌 **the same *"whoever computes the offset must own that `+8` in ONE place, not two"* that Batch 84
item 2 is already being held to.** ⭐ **Solve it once, for both.**

---

## 7. 🆕 `Q40-H` — **what does a pin made while PLANNING bind to?** *(NEW, needs your nod)*

⚠ **This falls out of approving *"add a watch while planning"*, and I did not see it before.**
📌 **`Q40-E` captures the entity at pin time** — ⛔ **but while planning there IS no entity.**

| | option | |
|---|---|---|
| **H1** | pin `(AssetId, VariablePath)` only; at run start, **expand to one row per live entity** | ⭐ matches *"watch this variable"* as a designer means it · ⚠ N rows appear from one gesture |
| **H2** | pin with entity = **the session's entity filter**, resolved at run start | ⭐ one row · ⛔ **silently arbitrary** when several entities run the asset |
| **H3** | forbid pinning while planning | ⛔⛔ **contradicts the approved `Q40-F`** |

> ⚖️ **RECOMMEND `H1`.** ⭐ **A designer pinning `Health` before a run means "show me Health", not "show
> me Health on some entity I cannot name yet."** ⭐ **`Q40-A`'s key already carries `Entity`, so the
> expansion produces ordinary rows** — ⛔ nothing new downstream.
> ⚠ **`H1` needs `GetActiveEntities(assetId)`** — 📌 **which is on the SHARED `IAiTraceObserver`**, not a
> blueprint interface. ⭐ **But its implementers are the debug sessions** ⇒ **on BTree/HSM this is where
> `R-70` actually bites** *(slice 3)*, ⛔ **not the poll.**
> ⚠ **If you prefer one row over N, say so** — `H2` is cheaper and I can defend it; it is only the
> *silence* about which entity I object to, and that could be fixed with a visible label instead.

## 8. ⛔ Still not decided
⚠ **Watching variables from DIFFERENT ASSETS in one panel** — ⭐ the key supports it and the store is
already shared, ⚠ **but the poll would span debug sessions.** ⭐ **Out of slice 1.**
