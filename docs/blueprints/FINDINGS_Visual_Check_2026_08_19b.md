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
| **①** | the edit dialog opens with **nothing to edit** — name, two separators, OK/Cancel | ⚠ **hypothesis, §3 — MEASURE FIRST** |
| **②** | OK says *"this row cannot be written — node-owned, passthrough or stale"* **after** opening | ⚠ **§4 — two predicates disagree** |
| **③** | **"Properties…" CRASHES the editor** *(native)* | 🔴🔴 **§5 — unmeasured, highest severity** |
| **④** | *"no live writer is installed for this host"* | ✅✅ **ROOT-CAUSED, §6a — one missing argument** |
| **⑤** | the **Watch row stays 0** while Details goes live | ✅✅ **ROOT-CAUSED, §6b — Batch 94 re-froze its own fix** |

---

## 3. ⚠ ① — the dialog is EMPTY. **Hypothesis, and it is a DESIGN question**

📐 The body is one call:

```csharp
// VariableEditModal.Draw:201
var drawer = new ComponentEditDrawer(session, pickerCtx: null);
drawer.DrawEditNode(session.Document.Root);
```

⭐ The document comes from `DefaultValueAuthoring.OpenSession` ⇒ `editService.Open(instance,
varEntry.FieldType, scope)`.

⚠⚠ **`Count` is a plain `int`.** ⭐ **Every existing test of this path uses a DTO** — `DavTestActionParams`
with `Speed`/`Direction`/`Count` fields *(`DefaultValueAuthoringTests`)*. ⇒ ⛔ **the suspicion is that
the StructEdit service is a COMPONENT editor: it enumerates a type's FIELDS, and `System.Int32` has
none**, so the document has a container root with **zero children** and the drawer draws nothing.

⇒ ⭐⭐⭐ **If that is right, this is not a bug in the modal — it is a capability that was never built:
editing a SCALAR variable.** ⛔ **Do not patch the drawer until this is measured.**

---

## 4. ⚠ ② — **two writability predicates, and they disagree**

| where | predicate |
|---|---|
| the Details menu enables the entry | `row.CanEverBeWritten` *(`VariableTableControl:283`)* |
| the commit refuses | `if (!row.CanEverBeWritten) return Outcome.RefusedReadOnly;` *(`VariableEditCommit:119`/`:161`)* |

⭐ **Same expression** ⇒ ⛔ **they cannot disagree for one row… unless the dialog was opened from a
surface that does not consult it.** 📌 **The user opened it from the MY BLUEPRINT panel, not the
Details table.**

⇒ ⭐⭐ **Measure which gesture that outline click runs, and whether it gates on `CanEverBeWritten` at
all.** ⚠ **And then the real question:** is `Count` genuinely `RowKind != Normal` or `IsStale`? ⛔ **If a
hand-authored blueprint variable classifies as node-owned, the CLASSIFIER is the defect** — not the
gate.

⭐ **Either way the UX is wrong:** ⛔⛔ **a dialog must not open and then refuse on OK.** 📌 The user's own
rule — *"same information value, no false expectations"* — ⭐ **refuse at the gesture, greyed, with the
reason.**

---

## 5. 🔴🔴 ③ — **"Properties…" crashes the editor.** ⛔ UNMEASURED, and it is the top item

⭐ `"Properties…"` differs from `"Edit value…"` in **one** way: `EditScope.WholeComponent` instead of
`EditScope.ForField` *(`VariableEditing:205`)*.

⛔⛔ **I have NOT reproduced it and will not guess at a native crash.** ⭐ **It needs a real repro**
— ⚠ a native ImGui crash is usually an **id/stack imbalance** *(a `Begin` without its `End`, a popup id
mismatch, or a table column drawn outside its table)*, and `WholeComponent` is the arm nothing has
ever drawn.

⇒ ⭐⭐ **First item of the next batch, and the only one whose acceptance is a REPRO, not a rail.**

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
