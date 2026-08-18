<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 3 (the recommendations) - awaiting user approval
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
| **BTree / HSM** | ⛔ **`R-60`: no Details window there yet.** ⭐ The same `Origin` key will serve when row 61 lands |
| **editing from the Watch row** | ✅ already built *(Batch 83, ruling 11)* — ⛔ nothing to decide |
