# ADDENDUM to Batch 25 — visual-check results, 2026-08-09

> **Merge this into your Batch 25 work.** The user completed sections A–C and F of the step-by-step
> check. **Seven new rows (BP-120…BP-126)**, one of which is the root cause of four separate symptoms
> *and* of an already-open issue.
>
> ⚠ **Priority note:** BP-120 and BP-121 together make a Function Library **unusable end to end today**.
> They are cheap. **Do them before Item 5 (the string nodes).** Item 2 (the matrix) still comes first.

---

## ✅ What the check confirmed working — do not re-litigate

| | |
|---|---|
| **A5** | ⭐ **A freshly created Function Library compiles with 0 errors.** BP-112 confirmed fixed in the real build |
| **B5** | Renaming an output in `Details` projects to the node immediately — **BP-86 confirmed fixed** |
| **B7 · B9** | Undo works across add/remove node, rename, add param, retype |
| **C2** | `FixedString32` **and** `64` present in the picker — BP-87 item 1 confirmed |
| **C3** | ✅ |
| **F2 · F4** | `Status` correctly **hidden** on an Instance function's Return node — **BP-105 working as designed** |
| **BP1657** | ⭐ **Our new diagnostic fired, correctly, on a real user graph.** It also proved the user's `Warning` ruling right — as an `Error` it blocked a build whose only real problem was an unwired node |

---

## 🔴 BP-120 — Graph Signature edits never re-project pins. **One root cause, four symptoms, plus BP-102.**

**This is the most valuable finding in the batch.**

`ReturnNodeDrawer.cs:142-143` routes its edits through the edit service:

```csharp
apply: () => { apply(); _editService.NotifyStructureChanged(_parent); },
undo:  () => { undo();  _editService.NotifyStructureChanged(_parent); });
```

`OnStructureChanged` → `BlueprintDocumentFactory.cs:211` → `graphModel.RebuildAndNotify()` → pins
re-projected. **That is why adding an output from the Return node's `Details` works (B4).**

⭐ **`GraphSignatureWindow` calls only `_dirtyTracker.MarkDirty(assetId)` (`:299`, `:303`).** It never
calls `NotifyStructureChanged` and never records an undoable edit. So an output added there **changes
the model but never re-projects the pins.**

### Every symptom this explains

| Report | What actually happened |
|---|---|
| **F3** — *"added output to Graph Signature, shown in Return node detail panel, but NOT shown as pin on Return node"* | The pin was never projected |
| **B6** — *"added bool output, added boolean literal, could not wire them"* | ⭐ **Not a type-system bug. There was no pin to wire to.** `bool`→`bool` was never the problem |
| **B8** — *"can't wire, can't test"* | Same |
| **C4** — `BP3010` orphan + `BP1657` | The Return node was unreachable *and* unwirable, so it could not be connected to anything |
| ⭐ **[BP-102](Blueprint_Issues_Detail.md#bp-102)** — *Graph Signature edits do not undo* | **Same root cause.** Already open since Batch 20 |

⇒ **Fix: route `GraphSignatureWindow`'s edits through `EditService`** — `recordUndoable` **and**
`NotifyStructureChanged` — exactly as `ReturnNodeDrawer` does. **One fix closes BP-102 and BP-120.**

⚠ **The JSON confirms it:** the user's asset carries `Outputs: [{ "Name": "Para2", "TypeId": "bool" }]`
with the Return node at `"Pins": []` and `"Links": []`. Saved assets are projection-only so `Pins: []`
is normal — but the **live** editor had no pin either, which is the defect.

**Delegation:** 🔴 Opus — the undo-record shape must match `ReturnNodeDrawer`'s so one gesture stays one
undo step. 🟢 Sonnet — the tests once the shape is fixed.

---

## 🔴 BP-121 — a new Function graph has **no Return node**

> B1: *"newly created function contains just output node, no return node."*
> F1: *"graph opened, just Event node present, no Return."* — and the user's JSON shows exactly one
> `EventEntry` node.

In Unreal, creating a function gives you **entry + return, already wired.** Here the author must find
`Return` in the palette, place it, and wire it — and if they miss the wire they get `BP3010` +
`BP1657`, which is precisely what happened in C4.

⇒ **Seed a new Function graph with an `EventEntry` **and** a `Return`, exec-wired.** BP-103 already
established the seeding mechanism for a new Library asset (`BlueprintNewAssetService`); this is the
same idea one level down, for a new *graph*.

⚠ **Together with BP-120 this is why the whole section failed.** Neither is deep; both are in the way of
everything.

**Delegation:** 🟢 Sonnet — the seeding mirrors BP-103.

---

## 🟠 BP-122 — a graph cannot be renamed. Anywhere.

> A4: *"No way to rename 'NewFunction' — not even in Graph Signature."*

Every graph is stuck with its creation name. F2 rename exists for My Blueprint *items* (BP-101 is still
open for some of these) but a graph's own name has no editing affordance at all.

**Delegation:** 🟢 Sonnet, once a home for it is chosen — ⚠ **which BP-123 decides.** Sequence it after.

---

## 🔴 BP-123 — 📐 fold `Graph Signature` into `Details`. **The user is right, and it is Unreal parity.**

> *"i do not understand why we are setting inputs and outputs in graph signature. Way more intuitive
> would be to set Detail on Event node (inputs) and Details on Return node (outputs). … The whole Graph
> Signature seems redundant and would be much better if integrated into details."*
>
> *"there should be one context-sensitive Detail for anything"* · *"Details panel says 'no node
> selected' when clicking an empty place in the graph — maybe we could reuse the Details panel for
> showing the Graph Signature."*

⭐ **This is exactly how Unreal works.** Select the function **entry** node → its Details shows the
Inputs. Select the **Return** node → its Details shows the Outputs. Click empty canvas → Details shows
the **graph/asset** properties. There is no separate signature window.

⭐ **And it dissolves BP-120 structurally rather than fixing it:** if outputs are only ever edited on the
Return node's Details, the projection path that *works* becomes the only path. A second editor for the
same state is what created the divergence.

| | |
|---|---|
| **Inputs** | on the `EventEntry` node's `Details` — the user confirmed this already works: *"Added input param there, the pin appeared in the Event node"* |
| **Outputs** | on the `Return` node's `Details` — already exists (BP-89) and already projects correctly |
| **Empty-canvas click** | show graph + asset properties instead of *"no node selected"* — ⭐ **this is where graph rename (BP-122) belongs** |
| **`Graph Signature` window** | retire once both halves are covered |

📐 **This is a design item, not a ticket.** ⚠ **Do not start building it in Batch 25** — write the design
note, and the coordinator will run it past the architect. **BP-120 is the tactical fix and ships now;
BP-123 is the structural one.**

---

## 🟠 BP-124 — literals cover only a few types, and there is no conversion node

> B4: *"Selected uint data type. Added literal node, no uint supported, just 'Integer'."*
> C4: *"for ushort no ushort literal node exists."*

BP-87 registered the types and the widening coercions, so `ushort → int` **wiring** works. But there is
**no way to author a `ushort` constant**, and no explicit conversion node to place when the implicit
coercion does not apply.

⚠ **Note the interaction with BP-120:** some of what the user read as "cannot wire" was the missing pin.
**Re-test this after BP-120 lands** before scoping it — the literal gap is real, but its size is not yet
known.

---

## 🟠 BP-125 — `BP3010` "orphan node eliminated" is an **Error**

> C4: `CSC : error BP3010: Orphan node '49eca277…' in graph 'NewFunction' was eliminated.`

A disconnected node is **normal during authoring**, and in Unreal it is simply ignored. Failing the
whole solution build for one is disproportionate — especially when, as here, the node was disconnected
*because of another bug*.

⇒ **Warning, not Error.** Same reasoning the user applied to `BP1657`, and the same conclusion.

---

## 🟠 BP-126 — the Return node's `Status` combo is a fixed value

> B2: *"i still see a combo with fixed values for Success, Error, In progress — meaningless … the status
> must be an input data pin of the return node, not a fixed value to select in a combo."*

BP-105 already hides `Status` for Instance and for Library-with-outputs — and F2/F4 confirm that works.
What remains is the **zero-output Library** case, and the user's point is sharper than visibility: **a
status chosen at author time from a combo is a constant, so it cannot express a runtime outcome.** If a
library function can fail, the status must be a **data-in pin**.

⚠ **Test-locked** — `BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn` and the
AiPrimitive contract depend on the current shape. 📐 **Design note first; do not just change it.**

---

## Suggested order inside Batch 25

| | |
|---|---|
| 1 | `BP1657` → Warning *(already item 1)* |
| 2 | ⭐ **The matrix** *(already item 2 — unchanged, still the headline)* |
| 3 | 🆕 **BP-120** + **BP-121** — cheap, and a Function Library is unusable without them |
| 4 | **BP-125** → Warning — trivial, same call as BP1657 |
| 5 | BP-118, `FixedString128`, the string nodes *(items 3–5)* |
| 6 | 📐 **BP-123** design note only. **BP-122** waits on it. **BP-124** re-scoped after BP-120 |

⚠ **BP-123 and BP-126 are design items — write notes, do not build.** The coordinator runs them past the
architect.
