# HANDOFF — Batch 43: ⭐⭐ **ONE ITEM — the Local Variables section.** Nothing else

> 📌 **Dispatched at `71d5ca84`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic.
>
> ⭐⭐ **This is the LAST thing between `BP-57` and closed.** Everything behind the surface is built:
> the schema source, the honest reference count, the refusal, the undo. ⛔ **There is still nowhere in
> the editor to declare a local.**

---

## 0. ⛔⛔ Read this first — it has been asked for twice and skipped twice

| batch | asked | delivered |
|---|---|---|
| **41** | source · **SECTION** · picker · delete · badge · nit | source · picker |
| **42** | **SECTION** · delete · undo · badge · nit | delete · undo |

⚠ **It was listed FIRST in both handoffs.** ⭐ **The common factor is the coordinator's, not yours:
in both I wrote *"🟢 Sonnet takes the section wiring"* — and it is the one item that never landed.**

⇒ ⭐⭐ **This batch is ONE item, on Opus, delegated to nobody.**
⛔ **No badge. No doc comment. No unification. Nothing else at all** — except the tracker (§4), which
has also now been skipped twice.

⚠ **If you believe the section should not be built, or cannot be as specified — say so and stop.**
⭐ **That is a legitimate outcome and a useful one. Silently doing the other items is not.**

---

## 1. What exists already — you are wiring, not designing

⭐ **`BlueprintLocalVariableSchemaSource` is complete**, and its constructor already takes everything
the section needs:

```csharp
BlueprintLocalVariableSchemaSource(
    BlueprintAsset          asset,
    Func<Graph?>            currentGraph,   // ⭐ a DELEGATE — feed it and it follows the canvas
    Action                  onChanged,
    Action<string, Func<bool>> record,      // ⭐ BlueprintDocumentFactory.LocalVariableUndoRecorder
    Action<string>?         refuse)         // ⭐ how a refusal reaches the designer
```

| already built | |
|---|---|
| `Variables` projection · `AddVariable` · `RemoveVariables` *(refuses while referenced)* · `RenameVariable` · `MoveVariable` | ✅ |
| `CountNodesReferencingVariable` — **real**, not the hardcoded `0` | ✅ |
| `IsReadOnly` — **true for a `Macro` graph**, projecting read-only rather than vanishing | ✅ |
| the undo recorder, host-side | ✅ `BlueprintDocumentFactory.LocalVariableUndoRecorder` |

⇒ ⛔ **Do not re-derive any of it. Do not modify it unless the section proves it wrong** — and if it
does, **say so explicitly** rather than quietly changing it.

---

## 2. The five edits — with the call sites

### 2.1 The descriptor

`BlueprintMyBlueprintModel` — `_sections` is a `static readonly` list of five. **Add a sixth:**

```csharp
public const string SectionLocalVariables = "localvariables";
new(SectionLocalVariables, "Local Variables", 5, null, true, true, "editor.create-local-variable"),
```

📐 **Sort order is yours** — after `Variables` reads naturally; **say which and why.**

### 2.2 The items — mirror `BuildVariableItems`

`GetItems(sectionId)` is a `switch`. Add the case, and build items **exactly like
`BuildVariableItems`** (`:207-231`): `ItemId: $"local:{v.Id}"` · accent from
`GetVariableAccentColor(v.Type?.TypeId)` · `IsRenamable: true` · `IsDeletable: true`.

⚠ **Read from the CURRENT GRAPH, not the asset.**

### 2.3 ⭐⭐ The current-graph problem — the one real piece of work

⛔ **`Retarget(IEditableAsset?, BlueprintAsset?)` is asset-only. The model has no idea which graph is
open.** That is the whole difficulty, and it is why this item is not the triviality it looks like.

✅ **The mechanism exists and its sibling already uses it.** `BlueprintGraphSwitcher:90-91`:

```csharp
_model.Retarget(graph);      // ← the canvas node model, on every graph switch
_sink.Retarget(graph);
```

⇒ 📐 **Give the My Blueprint model the same news.** Two shapes, **your call, say which:**

| | |
|---|---|
| **(a)** | a `Func<Graph?>` supplied at construction — ⭐ **matches the schema source's own shape** |
| **(b)** | a `Retarget(Graph)` overload called from `BlueprintGraphSwitcher` beside the two that are already there |

⚠ **Whichever you pick, `Changed` must fire on a graph switch**, or the panel shows the previous
graph's locals until something else pokes it. ⭐ **`BP-72`'s lesson is exactly this: a panel showing
the graph you are not looking at is a defect.**

### 2.4 `[+]` → the create path

`editor.create-local-variable` must reach `AddVariable`. ⭐ **Mirror `editor.create-variable`**, which
`BlueprintDocumentFactory:308` registers and `BlueprintMyBlueprintWindow:91` opens as a modal.

📐 **A modal or an inline row is your call** — ⚠ **but `BP-12c`'s lesson binds: a section that declares
a create command nothing registers is an INERT BUTTON.** ⭐ **Assert the command is registered.**

### 2.5 ⛔ The `Macro` case — silence is not an option

⭐ **The source already rules it:** `IsReadOnly` is true for a `Macro` graph, and it **projects
read-only rather than vanishing** (`BP1664`: a macro-local has nothing to be scoped to).

⇒ **Render that honestly:** `[+]` absent **and the section says why**, or present **and refusing out
loud** through the `refuse` delegate the source already accepts. 📐 **Your call; ⛔ a silently missing
button is not one of them** (`Q26-B2`; `BP-76`/`BP-77`).

---

## 3. Gates

| | |
|---|---|
| ⭐ **Present and empty** | a graph with no locals ⇒ the section **exists**, with zero items |
| ⭐⭐ **Follows the canvas** | switch graphs ⇒ `GetItems` returns the **new** graph's locals **and `Changed` fired** |
| ⭐ **Create reaches the model** | invoking `editor.create-local-variable` adds a local to the **current** graph — ⭐ **and the command is registered, not just declared** |
| **Rename / delete route to the source** | including **delete refusing while referenced**, which Batch 42 built |
| ⭐ **`Macro` graph** | your chosen refusal, **asserted** |
| 🔴 **Revert-goes-red** | remove the section descriptor ⇒ the present-and-empty and follows-the-canvas tests fail |

**Baseline — coordinator-run, post-Batch-42:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** *(⚠ an incremental build under-reports — record honestly)* |
| Blueprints | **3298 total / 3288 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1213** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |

⭐ **`python3 scripts/tracker-counts.py --check`** — clean eleven batches running.

⛔ **Say plainly what is NOT covered headlessly.** ⚠ **The visual check has not run for EIGHT batches**,
and this section is precisely what it would catch — *"present and empty"* and *"follows the canvas"*
are exactly the things a headless test can pass while the panel shows nothing.

---

## 4. 📌 The tracker — skipped twice, not optional this time

⛔ **`BP-57`'s row records NONE of Batches 41, 42 or this one.** ⭐ **Record all three**, and
⭐⭐ **tick `BP-57` if the section lands** — the compiler half, the source, the picker, the title fix,
the delete refusal, the undo and the section are then all in.

📌 **`MoveVariable` carries a finding worth its own row if you agree it is one:** reordering locals is
**not cosmetic for a suspending graph** — declaration order feeds `FieldLayout` ⇒ `StructureHash` ⇒
**the blackboard re-initialises on next run.** ⭐ **Correct behaviour; not what a designer dragging a
row expects.**

---

## 5. Reporting

Per-suite numbers · `tracker-counts.py --check` · revert-goes-red · ⭐ **every id you allocated** ·
**your 2.3 shape choice** · **your 2.5 refusal choice** · ⭐⭐ **whether `BP-57` is now TICKED** ·
⭐ **and if you did not build the section, WHY — plainly, as the first line of your report.**

⚠ ⭐ **Batch 42's delete ruling found that the repo had already decided the same question for asset
variables, and matched it.** ⭐ **That instinct is exactly right here too: `BuildVariableItems` and
`editor.create-variable` are the precedent. Follow them rather than inventing a parallel shape.**
