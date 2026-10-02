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

## 2. 🛠 **`96a` — the modal never opens the table the drawer requires.** ⭐ ONE cause, TWO failures

> ⛔⛔ **The earlier version of this handoff asked for a REPRO and a runtime dump. Both were wrong and
> the user said so:** *"you will never see the native imgui crash, it leaves no traces"* ·
> *"can't you see the sources yourself what they do?"* ⭐⭐⭐ **It was readable. It has been read.**

### 📐 The measurement

```csharp
// ComponentEditDrawer.cs:41 — the contract, in its own doc-comment
/// Must be called inside a two-column BeginTable/EndTable block.
// …:241 DrawLeafNode, first statement:   ImGuiApi.TableNextRow();

// VariableEditModal.Draw:197-202 — the entire body
ImGui.Separator();
drawer.DrawEditNode(session.Document.Root);   // ⛔ no BeginTable — grep "Table" in that file: NOTHING
ImGui.Separator();
```

| gesture | why it behaves as it does |
|---|---|
| **"Edit value…"** | its scope filters the document to an **EMPTY `SelectionRoot`** *(§3)* ⇒ zero children ⇒ **`TableNextRow` never reached** ⇒ ⭐ **nothing drawn, no crash** — 📌 *"two separators and nothing between them"* |
| **"Properties…"** | keeps the real node ⇒ ⛔⛔ **first `TableNextRow()` with no table open ⇒ NATIVE ABORT** |

⇒ ⭐⭐⭐ **The modal has never successfully drawn anything, for any variable, on any host** — ⛔ and
**Properties would crash on a DTO variable too.**

### ⭐ Build

| ⭐ | |
|---|---|
| **①** | ⭐⭐ **wrap the `DrawEditNode` call in the two-column `BeginTable`/`EndTable` the drawer documents.** ⛔ **Do not change the drawer** — it is `Fdp.Presentation` infrastructure with its own callers and its own rails; ⭐ **find an existing caller that does it correctly and MIRROR it** *(`ComponentEditWindow` is the obvious candidate — read it first)* |
| **②** | ⛔ **`EndTable` must be reached on every path**, including the early `RebuildRequired` return inside `DrawEditNode` |
| ⭐⭐ **the rail** | ⛔ **You cannot drive ImGui headlessly** *(`R-21`/`R-62`)*. ⭐ **So rail what CAN be asserted and SAY the gap**: e.g. that the modal's draw path is the same table-wrapping shape as the reference caller. ⚠ **State plainly in the report that the DRAW itself is unrailed** — 📌 `M-29`, and that honesty is worth more than a rail that pretends |

---

## 3. 🛠 **`96b` — a variable's NAME is not a path inside its own VALUE**

### 📐 The measurement

```csharp
// VariableEditing.ScopeFor:206
EditScope.ForField(EditPath.Parse(ToJsonPath(variablePath)))     // ⇒ "$.Count"
```

⭐ The session is opened over **the variable's value** — `Open(instance, typeof(int), scope)` — so the
document root **IS** the value, at `$`. ⛔⛔ **`$.Count` asks for a field named `Count` INSIDE the
`int`.** ⭐ There is none, and for a DTO it would mean *"a field called `Count` inside the DTO"*, which
is a different thing from *"the variable `Count`"*.

⇒ ⭐⭐ **Wrong for every variable, on every host.** ⭐ **"Edit value…" of a whole variable IS the whole
document.**

### ⭐ Build

⭐⭐ **`ScopeFor` stops synthesising a field path from the variable name.** ⚠ **What `ForField` is FOR
is a real sub-path** — a field *inside* a DTO variable, which is a gesture that does not exist yet.
⇒ ⛔ **Do not delete the `ForField` arm; stop feeding it the variable name.**

⚠ **`ToJsonPath` has exactly one other consideration**: it passes an already-`$`-rooted path through.
⭐ **Check whether anything relies on that** before changing it.

---

## 3b. ⚠ **the dialog opens and then refuses on OK**

⭐ Independent of the two above, and still wrong. ⚠ **But it is DELIBERATE** —
`VariableEditing.Open`'s doc-comment: *"`ReadOnly` still OPENS — the design says properties are
read-only mid-run, not absent; refusing to open would hide the values a designer wants to read."*

⇒ ⭐⭐ **The decision is defensible; the PRESENTATION is not.** ⭐ **Open it shaped as a read-only view
— no OK button** — ⛔ never an editor whose OK says no.

⚠ **And measure the other half:** is the user's `Count` genuinely `RowKind != Normal` or `IsStale`?
⭐⭐ **If a hand-authored blueprint `int` classifies as node-owned, the CLASSIFIER is the defect** and
that is a bigger finding than the dialog — ⛔ **report it rather than working around it.**

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
| **changing `ComponentEditDrawer`** | `96a` — it is `Fdp.Presentation` infrastructure with other callers; ⭐ **the modal is the broken caller** |
| **disabling "Properties…" to stop the crash** | `96a` — ⭐ the crash is a missing `BeginTable`, not a bad gesture |
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
| ⭐ **`96a`** | which existing caller you mirrored for the table wrapping, and ⛔ **the explicit statement that the DRAW itself is unrailed** |
| ⭐ **`96b`** | what `ScopeFor` now returns for a whole-variable edit, and what still uses the `ForField` arm |
| ⭐ **`96c`** | which option, and what the inverted rail now asserts |
