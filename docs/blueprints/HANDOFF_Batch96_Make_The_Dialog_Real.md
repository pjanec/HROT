<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 96 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: §4 reverses part of Batch 94's 94c. That is deliberate and measured —
  FINDINGS_Visual_Check_2026_08_19b.md §6b. Batch 94's rail is green and its production
  path is frozen; the rail pins a row the UI never pins.
-->
# HANDOFF — Batch 96: **make the dialog real, and unfreeze the pin**

> 📌 **Dispatched at `a2f93954c`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⚠ **If a later document INVALIDATES an item — STOP AND
> REPORT.** ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push
> `chore: started batch 96 at a2f93954c` FIRST.**
>
> 📄 **Source: [`FINDINGS_Visual_Check_2026_08_19b.md`](FINDINGS_Visual_Check_2026_08_19b.md)** — the
> user's second visual check. ⭐ **Read §1 before the items.**

---

## 1. ⛔⛔⛔ READ THIS FIRST — **why six green batches keep failing the first click**

> ⭐⭐ **User:** *"small progress (after 5 hours…) … i stopped testing then, enough errors."*

📌 **`R-21`/`R-62` are TRUE: no headless rail can drive the drawing.** ⇒ ⛔⛔ **the surface layer is
unrailed BY CONSTRUCTION**, and every rail we write stops one call short of it — **then reads as
proof.**

| ⭐ the rule this batch adds to itself | |
|---|---|
| ⭐⭐⭐ **A rail must take its input from the SAME OBJECT the UI takes it from** | 📌 §4: Batch 94's pin rail pins a row from the **source**; the UI pins a row from the **sampled view**. ⭐ Both real objects, ⛔ different rows |
| ⭐⭐ **When you cannot drive the draw, rail the DECISION the draw makes and state the gap** | ⭐ `VariableWatchGesture` is the good precedent — ⛔ it did not stop the outline entry being dead *(§6)* |
| ⭐ **Say, per rail, which layer is faked** | Batch 95 did this well. ⭐ **Keep it** |

⛔ **Do not open this batch by writing a framework for that.** ⭐ **Apply it per item.**

---

## 2. 🔴🔴 **`96a` — "Properties…" CRASHES THE EDITOR.** ⭐ FIRST, and its acceptance is a REPRO

⭐ `"Properties…"` differs from `"Edit value…"` in exactly one respect:
**`EditScope.WholeComponent`** vs `EditScope.ForField` *(`VariableEditing:205`)*.
⚠ **`WholeComponent` is the arm nothing has ever drawn.**

| ⭐ | |
|---|---|
| **①** | ⭐⭐⭐ **REPRODUCE IT FIRST.** ⛔ **Do not fix a native crash you have not seen.** ⚠ A native ImGui abort is usually an **id/stack imbalance** — a `Begin` without its `End`, a popup id mismatch, a table column outside its table — ⭐ **all of which are in the DRAW path, not in the scope enum** |
| **②** | ⭐ **If you cannot reproduce headlessly, say so and rail the decision** — ⛔ but then the item is NOT done, and say that too |
| **③** | ⛔⛔ **A crash is never fixed by disabling the entry.** ⭐ If the honest outcome is *"Properties is not supported for this row shape"*, that is a **greyed entry with a reason at the GESTURE**, plus the crash still fixed |

⭐ **`R-27` does not gate this** — a crash is not a design question.

---

## 3. 🛠 **`96b` — the dialog opens with NOTHING TO EDIT** *(finding ①/②)*

### ⭐⭐⭐ MEASURE BEFORE YOU BUILD — **this may not be a bug at all**

```csharp
// VariableEditModal.Draw:201  — the whole body is one call
new ComponentEditDrawer(session, pickerCtx: null).DrawEditNode(session.Document.Root);
```

⚠⚠ **The user's `Count` is a plain `int`. Every existing test of this path uses a DTO**
*(`DefaultValueAuthoringTests` → `DavTestActionParams` with `Speed`/`Direction`/`Count`)*.

⭐⭐ **Open a real session over `typeof(int)` and DUMP THE DOCUMENT.** Then answer, in the report:

| ⭐ question | |
|---|---|
| **①** | what `EditNodeKind` is the root, and **how many children**? |
| **②** | does `DrawEditNode` have a path that renders a **scalar ROOT**, or only scalar **leaves under a container**? |
| **③** | ⛔ **is editing a SCALAR variable a capability that was never built?** |

⇒ ⭐⭐⭐ **If ③ is yes, STOP AND REPORT.** ⛔ **Do not invent a scalar editor inside the modal** — that
is a design call, it is mine to make with the user, and 📌 the AI hosts' variables are DTOs while
blueprint variables are scalars, so it decides whether one dialog serves both *(ruling 9)*.
⭐ **If instead the document is fine and the DRAW drops it, fix the draw.**

### ⚠ And the second half — **the dialog must not open and then refuse**

📐 The Details menu gates on `row.CanEverBeWritten` *(`VariableTableControl:283`)* and the commit
refuses on **the same expression** *(`VariableEditCommit:119`)* ⇒ ⛔ **they cannot disagree for one
row.** 📌 **The user opened it from the MY BLUEPRINT panel, not the Details table.**

| ⭐ | |
|---|---|
| **①** | **measure which gesture the outline click runs**, and whether it gates on `CanEverBeWritten` **at all** |
| **②** | ⭐⭐ **then ask the real question: is a hand-authored blueprint `int` genuinely `RowKind != Normal` or `IsStale`?** ⛔ **If it classifies as node-owned, the CLASSIFIER is the defect, not the gate** — and that is a bigger finding than the dialog |
| **③** | ⭐ **refuse at the GESTURE, greyed, with the reason** — 📌 *"same information value, no false expectations"*. ⛔ **Never a dialog that opens and then says no** |

---

## 4. 🛠 **`96c` — the pin froze again** *(finding ⑤)* — ⚠ **this reverses part of `94c`**

### 📐 The measurement

```csharp
// VariableRowSampler.Sample — the tail
byte[] cachedBytes = sample.Bytes;                 // ⭐ a LOCAL, captured at THIS call
return row with { ReadValue = () => cachedBytes, … };
```

⭐ **Correct for Details** *(rebuilt and re-sampled every frame)*. ⛔⛔ **The Watch pins what the table
SHOWS — a rewritten row** — whose arms close over that frame's locals ⇒ ⭐⭐⭐ **a snapshot again**, the
exact defect `94a` removed.

⚠ **Batch 94's rail is green because it pins a row from the SOURCE.** ⛔ **The UI pins from the sampled
VIEW.** 📌 §1's rule, and this is the case that produced it.

### ⭐ The decision — ⛔ **I am NOT ruling it; measure and choose**

| ⭐ option | |
|---|---|
| **(a)** the Watch pins the **SOURCE** row | ⭐ the Watch already has its own sampler, so it would re-sample normally. ⚠ **needs the view to be able to hand back the pre-sample row** |
| **(b)** the rewrite reads **through the cache ENTRY** rather than a captured copy | ⭐ smaller, ⛔ **changes the sampler's contract for every panel** — a rewritten row would then never be a stable snapshot, which some caller may rely on |

⭐⭐ **State which you picked, why, and what you measured.** ⛔ **If both are wrong, STOP AND REPORT.**

### ⭐⭐⭐ The rail — **and it is the whole point of this item**

> ⛔ **Not** *"a row from the source tracks."* ⭐⭐ **Pin the row the TABLE VIEW holds** — the same
> object the gesture passes — **and assert it tracks.** ⚠ **Invert Batch 94's rail rather than adding
> beside it**, so the old shape cannot come back.

---

## 5. 🛠 **`96d` — the live writer is never passed** *(finding ④ — `R-67`, the SIXTH instance)*

📐 `writeLive` is an **optional** ctor parameter of `VariableEditGestureBinder` *(`:80`)* and
📐 **`PerspectiveWorkspaceRegistrar:329` constructs the binder WITHOUT IT** ⇒ ⛔⛔
`Outcome.LiveWriteUnavailable` **guaranteed in production, every host, every row.**

⚠⚠ **The code NAMED the shape while shipping it unwired** — its own doc-comment calls it *"the
silent-default shape"*. ⭐ **The word was earned; the wire was not run.**

| ⭐ | |
|---|---|
| **the source** | ⛔ **do not invent one.** ⭐ **Measure what already writes a live blackboard value** — the Blueprint debug session is the obvious candidate and it already backs the READ *(`BlueprintLiveValueProvider`'s reader factory)*. ⚠ **If no writer exists anywhere, STOP AND REPORT — that is a capability, not a wire** |
| **the shape** | ⭐ **host-supplied delegate, passed at the composition root** — 📌 the same route `liveValueProvider` takes, ⛔ not a new interface |
| ⭐⭐ **the rail** | **construct the production binder and assert a write LANDS** — ⛔ **not** that the argument is non-null. 📌 `M-22` |

---

## 6. 🛠 **`96e` — the outline's "Watch this variable" is dead** *(finding §7)* — ⚠ **only if `96a`–`96d` are green**

📐 `MyBlueprintContextMenu:40` enables on `commands.Get("editor.toggle-variable-watch") is not null`,
and 📐 **nothing registers that command** *(the only other mention is a test asserting the constant)*.
⇒ ⭐ Batch 94's *"ONE command, TWO entry points"* is **half true in production**.

⭐ It refuses honestly rather than dead-ending, so it is a **gap, not a trap** ⇒ ⛔ **last, and droppable.**

---

## 7. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **a scalar editor invented inside the modal** | `96b` — a design call, mine with the user |
| **disabling "Properties…" to stop the crash** | `96a` — a crash is not fixed by hiding its trigger |
| **a new live-write interface** | `96d` — follow `liveValueProvider`'s route |
| **a generic "value arrives" framework** | 📌 one was tried and thrown away `2026-08-16`. ⭐ Per item, explicit |
| **reverting Batch 95** | ⭐ both its items hold; this batch is above them |

---

## 8. ⭐ GATES

⭐ **Baseline** = Batch 95's table, base sha **`a2f93954c`**: AiShared **1545** · BTree.Editor **622** ·
Hsm.Editor **554** · Generators **277** · Persistence **143** · Blueprints **3796/0/10 skip** ·
Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** · NodeEditor.UI **135** ·
Fhsm **300** · Fdp.Presentation **146 filtered** · tracker **74 / 213** · rulings **70/70**.

⭐ **Same seven-row contract; Batch 95's table was good — keep its shape**, including the `--no-build`
column, `EXIT=` unfiltered, and revert-goes-red per item.
⚠ `WhenNodePerfTests.Spawn_ZeroAllocation` is a known flake *(`BP-111`, `HostTimingSensitive`)`* — ⭐ if
it reds, say which run.

### ⭐ Extra, this batch only

| ⭐ | |
|---|---|
| ⭐⭐⭐ **per rail: which object the input came from** | 📌 §1. ⛔ **"a row" is not an answer — say WHOSE row** |
| ⭐ **`96a`** | the repro, or the explicit statement that it could not be reproduced **and the item is not done** |
| ⭐ **`96b`** | the dumped document for `typeof(int)`: root kind + child count |
| ⭐ **`96c`** | which option, and what the inverted rail now asserts |
