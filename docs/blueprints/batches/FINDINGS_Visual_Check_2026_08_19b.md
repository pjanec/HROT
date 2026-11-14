<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: §2–§6, one per reported failure.
stale-below: nothing.
known-rot: none.
known-conflict: §6 contradicts REPORT_Batch94 §9's claim that the pinned row now tracks
  the run. The rail is green and production is frozen — §6 says exactly why.
-->
# ⛔⛔ FINDINGS — the second visual check, `2026-08-19`

> ⭐ **User, verbatim:** *"small progress (after 5 hours from starting the watch panel development) …
> i stopped testing then, enough errors."*

---

## 1. ⭐⭐⭐ THE PATTERN, STATED FIRST — **because it is the actual problem**

📌 **Six batches. Every one green. Every one shipped a defect the first click finds.**

⭐⭐⭐ **The common factor is not carelessness — it is that `R-21`/`R-62` are TRUE:** *no headless rail
can drive the drawing.* ⇒ ⛔ **the entire surface layer is unrailed BY CONSTRUCTION**, and every rail
we write stops one call short of it and then reads as proof.

| batch | the rail asserted | ⛔ what the first click found |
|---|---|---|
| 90 | the live arm is passed | the feed had no entity |
| 94 | a row pinned **from the source** tracks | ⭐ **the row the UI actually pins is a different one** — §6 |
| 95 | the dialog **opens** | ⭐ **it opens EMPTY, refuses on OK, and crashes on Properties** — §3–§5 |

⇒ ⭐⭐ **A rail that constructs its own input is testing the half we did not ship.** 📌 `M-22`'s
correction generalises: ⛔ *"is it connected?"* is not *"does anything flow?"* — ⭐⭐ **and *"does the
mechanism work?"* is not *"does the mechanism the UI actually calls work?"***

---

## 2. ⭐ THE FIVE FAILURES

| # | reported | status |
|---|---|---|
| **①** | the edit dialog opens with **nothing to edit** — name, two separators, OK/Cancel | ✅✅ **ROOT-CAUSED, §3 — the modal never opens the table the drawer requires** |
| **②** | OK says *"this row cannot be written…"* **after** opening | ⚠ **§4 — deliberate *(read-only still opens)*, but shaped as an editor** |
| **③** | **"Properties…" CRASHES the editor** *(native)* | ✅✅ **ROOT-CAUSED, §3 — SAME cause: `TableNextRow()` with no table open** |
| **④** | *"no live writer is installed for this host"* | ✅✅ **ROOT-CAUSED, §6a — one missing argument** |
| **⑤** | the **Watch row stays 0** while Details goes live | ✅✅ **ROOT-CAUSED, §6b — Batch 94 re-froze its own fix** |

---

## 3. ✅✅✅ ① **and** ③ — **ONE CAUSE, ONE CALL SITE.** ⛔ No design question, no runtime dump

> ⭐⭐ **User:** *"isn't it the most basic user input operation? why do you need a `typeof(int)` run,
> can't you see the sources yourself what they do?"* ⭐⭐⭐ **Right on both counts. Read, not measured:**

### ⭐⭐⭐ `3a` — **the drawer requires a table the modal never opens**

```csharp
// ComponentEditDrawer.cs:41  — the contract, in its own doc-comment
/// Must be called inside a two-column BeginTable/EndTable block.

// …:241  DrawLeafNode, first statement
ImGuiApi.TableNextRow();
```

```csharp
// VariableEditModal.Draw — the ENTIRE body, lines 197-202
ImGui.Separator();
drawer.DrawEditNode(session.Document.Root);     // ⛔ no BeginTable, anywhere in the file
ImGui.Separator();
```

⇒ ⭐⭐⭐ **`grep -n "Table" VariableEditModal.cs` returns NOTHING.** The modal draws a separator, calls
a drawer that must be inside a table, and draws another separator.

📌 **That is exactly what the user saw** — *"just 'count' and two horizontal separator lines"*. ⭐ **The
two lines are 197 and 202, with nothing between them.**

### ⭐⭐⭐ `3b` — **why "Edit value…" is empty and "Properties…" CRASHES — the same fact**

| gesture | scope | what the document becomes | result |
|---|---|---|---|
| **"Edit value…"** | `ForField("$.Count")` | ⛔ **nothing matches** ⇒ `ApplyScope` returns an **EMPTY `SelectionRoot`** | ⭐ the `SelectionRoot` case iterates **zero children** ⇒ **no `TableNextRow` is ever reached** ⇒ **nothing drawn, no crash** |
| **"Properties…"** | `WholeComponent` | ⭐ the real node is kept | ⛔⛔ **first `TableNextRow()` with no table open ⇒ NATIVE ABORT** |

⇒ ⭐⭐⭐ **The modal has NEVER successfully drawn anything, for any variable, on any host.** ⛔ **And
"Properties…" would crash on a DTO variable too** — any non-empty document crashes.

⚠⚠ **`R-21`/`R-62` are why no test saw it:** ⭐ **the tests assert the DOCUMENT, never the DRAW.**

### ⭐⭐ `3c` — **the second, independent bug: a variable's NAME is not a path inside its own VALUE**

```csharp
// VariableEditing.ScopeFor:206
EditScope.ForField(EditPath.Parse(ToJsonPath(variablePath)))   // ⇒ "$.Count"
```

📐 The session is opened over the **variable's value** — `Open(instance, typeof(int), scope)` — so the
document root **IS** the `int`, at `$`. ⛔⛔ **`$.Count` asks for a FIELD NAMED `Count` INSIDE the
int.** ⭐ There is none, and there never could be.

⇒ ⭐⭐ **Wrong for EVERY variable, DTO or scalar** — for a DTO it would mean *"a field called `Count`
inside the DTO"*, which is a different thing from *"the variable `Count`"*.
⇒ ⭐ **"Edit value…" of a whole variable IS the whole document.**

### ⛔ WHAT I GOT WRONG, stated plainly

⭐ I filed this as *"editing a scalar may be a capability that was never built"* and asked for a runtime
dump. ⛔⛔ **Both were wrong.** 📐 `DetermineKind(typeof(int))` returns **`EditNodeKind.Scalar`**, which
`DrawEditNode` routes to `DrawLeafNode`, which draws a real input — ⭐ **the machinery handles scalars
fine.** ⚠ **It was readable in the sources and I escalated it to a design question instead of reading
them.** 📌 **`M-30` is WITHDRAWN.**

---

## 4. ⚠ ② — the dialog opens and THEN refuses on OK

⭐ Independent of §3, and still a defect: 📌 the user's own rule is *"same information value, no false
expectations"* ⇒ ⛔⛔ **a dialog must never open and then refuse.**

⚠ **But note what `VariableEditing.Open`'s own doc-comment says:**
> *"`ReadOnly` still OPENS — the design says properties are read-only mid-run, not absent; refusing to
> open would hide the values a designer wants to read."*

⇒ ⭐⭐ **That is a deliberate design decision and it is DEFENSIBLE — for a READ-ONLY VIEW.** ⛔ **What is
not defensible is presenting it as an editor with an OK button that then says no.** ⇒ ⭐ **open it
titled and shaped as read-only, with no OK**, ⛔ **or refuse at the gesture.**

⚠ **Still to measure:** whether the user's `Count` is genuinely `RowKind != Normal` / `IsStale` — ⭐ **if
a hand-authored blueprint `int` classifies as node-owned, the CLASSIFIER is the defect** and that is
bigger than the dialog.

---

## 6. ✅✅ THE TWO THAT ARE ROOT-CAUSED

### ⭐⭐ `6a` — ④ **the live writer is never passed.** 📌 `R-67`, the SIXTH instance

```csharp
// VariableEditCommit.Commit:167
case Target.LiveBlackboard:
    if (writeLive is null) return Outcome.LiveWriteUnavailable;
```

📐 **`writeLive` is an OPTIONAL constructor parameter of `VariableEditGestureBinder`** *(`:80`)*, and
📐 **`PerspectiveWorkspaceRegistrar:329` constructs the binder WITHOUT IT.**

⇒ ⛔⛔ **`LiveWriteUnavailable` is guaranteed in production, on every host, for every row.**

⚠⚠ **And the code NAMED the shape while shipping it unwired** — its own doc-comment:
> *"Null is NOT silently treated as 'refuse': it returns `LiveWriteUnavailable`, because the run state
> SAID yes and the mechanism did not arrive — 📌 that is the silent-default shape and it earns its own
> word."*

⭐ **The word was earned. The wire was not run.**

### ⭐⭐⭐ `6b` — ⑤ **Batch 94 fixed the arms in `94a` and RE-FROZE them in `94c`**

📐 `VariableRowSampler.Sample` ends:

```csharp
byte[]  cachedBytes  = sample.Bytes;      // ⭐ a LOCAL, captured at THIS call
object? cachedObject = sample.Object;
return row with {
    ReadValue       = () => cachedBytes,
    ReadValueObject = row.ReadValueObject is null ? null : () => cachedObject,
    …};
```

⭐ **Correct for Details**, which rebuilds and re-samples every frame ⇒ a fresh rewritten row each time.
⛔⛔ **But the WATCH pins what the table is showing — a REWRITTEN row** — and its arms close over
**that frame's** locals. ⇒ ⭐⭐⭐ **the pin is a snapshot again**, which is precisely the defect
`94a` existed to remove.

| ⚠ why the rail is green | |
|---|---|
| `ARowPinnedFromTheDetailsSource…TracksTheRun` | ⭐ **pins a row taken from the SOURCE.** ⛔ **The UI pins a row taken from the sampled VIEW.** ⇒ the rail exercises a path the product does not use |

⇒ ⭐ **The fix is a decision, not a patch:** either the Watch pins the **source** row *(and does its own
sampling — it already has its own sampler)*, or the sampler's rewrite reads **through the cache entry**
rather than a captured copy. ⛔ **Do not guess — the second is smaller but changes the sampler's
contract for every panel.**

---

## 7. ⭐ ALSO FOUND, while answering the user's *"how do I pin?"*

⛔ **The My Blueprint OUTLINE's *"Watch this variable"* is permanently greyed** — *"Not implemented
(editor.toggle-variable-watch)"*. 📐 `MyBlueprintContextMenu:40` enables it on
`commands.Get("editor.toggle-variable-watch") is not null`, and 📐 **nothing in the repo registers that
command** *(the only other mention is a test asserting the constant)*.

⇒ ⭐ **Batch 94's *"ONE command, TWO entry points"* is HALF TRUE in production**: the Details-table
entry is wired *(`PerspectiveWorkspaceRegistrar:767`)*; the outline entry is drawn and dead.
⭐ **It refuses honestly rather than dead-ending**, so it is a gap, ⛔ not a trap.
