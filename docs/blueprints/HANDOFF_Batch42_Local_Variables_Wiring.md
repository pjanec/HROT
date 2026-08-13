# HANDOFF — Batch 42: ⭐ **finish `BP-57` — wire what Batch 41 built**

> 📌 **Dispatched at `PENDING`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic.
>
> 📄 **Continues [Batch 41](HANDOFF_Batch41_Local_Variables_Authoring.md)**, which delivered **§1 and §3
> only**. ⭐ **Its §1 and §3 are excellent and are NOT to be redone.** This is §2, §4, §5, §6.
>
> ⭐⭐ **THIS closes `BP-57`.**

---

## 0. ⚠⚠ What Batch 41 actually left — measured, not assumed

⭐ **`BlueprintLocalVariableSchemaSource` is complete and ORPHANED.** `Add` · `Remove` · `Rename` ·
`Move` · `CountNodesReferencingVariable` · `ReferencesTo` all exist and are tested.
⛔ **`grep` finds NOTHING that constructs it outside its own tests.**

⇒ ⭐ **This batch is mostly WIRING, not building.** Three gaps, in order:

| | |
|---|---|
| **§1** ⭐⭐ | **nothing projects the source** — `BlueprintMyBlueprintModel` is untouched ⇒ **there is still nowhere to declare a local** |
| **§2** 🔴 | ⭐ **`RemoveVariables` is a naive `RemoveAll`** — it drops the declaration and **leaves every reference dangling.** ⚠ **The machinery to do better is RIGHT THERE and unused** |
| **§3** 🔴 | ⛔ **no undo anywhere** — every mutation calls `_onChanged()` and **nothing records an undo entry** |
| **§4** ⭐ | the node badge ⚠ **moves two gates** — the clean stop point |
| **§5** 📌 | the doc comment · the tracker |

---

## 0a. ⚡ How to work

**You are on Opus.** 🟢 **Sonnet takes §1's section wiring and §5.** ⭐ **Opus keeps §2 and §3** — they
are where the defect shapes live.

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** ⚠ **and it was not touched last batch — see §5** |
| **Revert-goes-red** | every item, **never delegated** |
| ⚠ **Stop point** | ⭐ **before §4.** §1–§3 close `BP-57`. **Say where you stopped** — Batch 41 did not, and §8 asked |

📌 **Housekeeping:** `claude/batch39-locals-preserved` is fully merged and can be deleted.

---

## 1. ⭐⭐ Project the source — the Local Variables section

**User-ruled, twice. ⛔ Not open.** ⭐ **The source exists; give it a surface.**

| | |
|---|---|
| **A `Local Variables` section** | in `BlueprintMyBlueprintModel`, alongside `Graphs · Functions · Macros · Custom Events · Variables` |
| ⭐ **Filled from the CURRENT GRAPH** | ✅ **the source already does this** — it reads through a **delegate**, not a captured `Graph`. **Feed it `AiCanvasContext.CurrentGraphId`'s graph and it follows the canvas for free** |
| ⭐ **Always present, empty when the graph has none** | ⛔ **a section that appears and disappears reads as a broken feature** |
| **`[+]` where applicable** | see below |

⚠ **The two costs, measured and unchanged:** `_sections` is `static readonly`, and
`Retarget(IEditableAsset?, BlueprintAsset?)` is **asset-only — the model has no current-graph concept.**
✅ **But the wiring exists:** `AiCanvasContext.CurrentGraphId`, already consumed by
`GraphSignatureWindow` — `BP-72`. ⭐ **Follow it; do not invent a second mechanism.**

### ⛔⛔ On "where applicable" — silence is still not an option

⭐ **The source already answers the semantics:** `IsReadOnly` is true for a **`Macro`** graph, and it
**projects read-only rather than vanishing** — deliberately, because *"a surface that disappears
teaches nothing"* (`BP1664`: a macro-local has nothing to be scoped to).

⇒ **The section must render that honestly:** either `[+]` is absent **and the section says why**, or it
is present **and refuses out loud** through the `IEditorIndicators` surface `BP-223` repaired.
📐 **Your call which; ⛔ a silently missing button is not one of them** (`Q26-B2`; `BP-76`/`BP-77`).

**Gate:** switch graphs ⇒ contents change · **present and empty** when the graph has none · a `Macro`
graph ⇒ **your chosen refusal, asserted** · creating a local from `[+]` reaches `AddVariable`.

---

## 2. 🔴 Delete must not orphan its references — **and the machinery is already there**

```csharp
public void RemoveVariables(IReadOnlyList<string> names)
{
    …
    if (g.LocalVariables.RemoveAll(v => set.Contains(v.Name)) > 0)
        _onChanged();                       // ⛔ the references are simply left behind
}
```

⚠ **`ReferencesTo(Guid)` and `CountReferencesTo(Guid)` exist on the same class and are NOT called by
it.** ⭐ **Batch 41 built the honest reference count precisely so the delete could use it, then did not
wire the delete.**

⇒ 📐 **Decide and say which**, then build it:

| | |
|---|---|
| **(a) take the references with it** | delete the declaration **and** the `Get`/`Set` nodes that targeted it — ⭐ **and hand them back for the undo** |
| **(b) refuse while referenced** | out loud, **naming the count and where** — `CountNodesReferencingVariable` already gives the number |

⚖️ **(b) is the lean** — a delete that silently removes the designer's nodes is a bigger surprise than
one that refuses. ⚠ **But (a) with a good undo is defensible; what is NOT defensible is today's
behaviour**, which leaves the asset uncompilable (`BP1670` then refuses it at Stage 2).

⭐ **Whichever you choose, `BP-225`'s lesson binds:** *an undo that restored only the declaration
recreates the dangling state.* **The references are part of the transaction.**

📌 **Rename needs nothing** — it is already correct and already reasoned: a local resolves **by id**
(`FindLocalIndex` is id-only), so a rename cannot re-target anything. ⭐ **Prove it with a test if one
does not exist; do not "fix" it.**

📌 **`MoveVariable` carries a finding worth promoting to the tracker:** reordering locals is **not
cosmetic for a suspending graph** — declaration order feeds `FieldLayout` ⇒ `StructureHash` ⇒ **the
blackboard is re-initialised on next run.** ⭐ **That is correct behaviour, but a designer dragging a
row will not expect it. Decide whether it warrants a warning, and record the reasoning either way.**

---

## 3. 🔴 Undo — every mutation currently bypasses it

⛔ **`AddVariable`, `RemoveVariables`, `RenameVariable`, `MoveVariable` all mutate the model and call
`_onChanged()`. None records an undo entry.** ⇒ **every locals gesture is unundoable today.**

⭐ **One undo entry per gesture**, and for delete **the entry covers the declaration AND whatever §2
decided about the references** — `BP-225`'s shape, and `BP-74`'s *"one undo entry that restores
identity"*.

⚠ **Use the mechanism the canvas already has** (`view.Undo`, context-provided per graph — see
`BlueprintGraphSwitcher`). ⛔ **Do not invent a second undo path.**

**Gate:** each gesture undoes to the exact prior state · ⭐ **delete-then-undo restores the declaration
AND the references** · redo re-applies · **one entry per gesture, not one per keystroke** (`BP-204`).

---

## 4. ⭐ The node badge — ⚠ **moves two gates.** Stop before this if the batch is long

**User-ruled: a badge.** ⛔ **NOT colour** — colour already means **type**, and overloading it puts two
meanings on one channel.

⚠ **Why it is still needed even after Batch 41's `(local)` picker suffix:** the suffix disambiguates at
**pick time**; on the **canvas** a local `Scratch` and an asset `Scratch` still render pixel-identical
while reading different storage.

| | measured |
|---|---|
| ✅ `MyBlueprintItem` | **already has `BadgeText` + `IconKey`** ⇒ the panel side is free |
| ⛔ `INodeModel` | **no badge** ⇒ a new member on **`NodeEditor.Core`** *and* rendering in **`NodeEditor.UI`** |
| ⛔ **`Subtitle` is not a shortcut** | **`BP-17` owns it** — a badge there collides with every renamed node |
| ⚠ **Two gates move** | **NodeEdit Core 208** · **UI 131** — ⭐ **and they take NO `--no-build`** |

📐 **Yours: the badge's shape**, and whether the panel item mirrors it. ⚖️ **Both surfaces is the lean.**

---

## 5. 📌 Two small things, and one of them is bookkeeping

| | |
|---|---|
| **The doc comment** | `GraphTypes.cs:64-82` — `BP-220`'s block explaining `WithNodesAndLinks` is attached to **`LocalVariables`** ⇒ two consecutive `<summary>` blocks and an undocumented method. **One line** |
| ⚠ **The tracker** | ⛔ **Batch 41 did not touch it.** `BP-57`'s row records **neither** the schema source nor the picker/title work. ⭐ **Record both, plus this batch** — and **tick `BP-57` if §1–§3 land** |

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution **`IOS-IG-SimHost.sln`**.
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — **§4 moves both.**
⭐ **`python3 scripts/tracker-counts.py --check`** — clean **ten** batches running.

**Baseline — coordinator-run, post-Batch-41:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** *(⚠ an incremental build under-reports — record honestly)* |
| Blueprints | **3289 total / 3279 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1213** | ⛔ **must NOT move** — `U-5`'s `V2` is not this batch |
| BTree **612** · Breakpoints **130** · Generators **193** | 0 failed |
| NodeEdit Core **208** · UI **131** | ⚠ **§4 moves these** |

⛔ **Say plainly what is NOT covered by a headless test.** ⭐ **The visual check has not run for SEVEN
batches**, and §1 and §4 are exactly what it would catch.

---

## 7. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** · ⭐ **confirmation
`Hrot.Editor.AiShared` was NOT modified** · **your `[+]` choice** (§1) · **your delete ruling** (§2) ·
**your reorder/`StructureHash` ruling** (§2) · **the badge's shape and whether `INodeModel` changed**
(§4) · ⭐ **WHERE YOU STOPPED** · ⭐ **whether `BP-57` is now ticked** · anything here **wrong against
the code**.

⭐ **Batch 41's §1 reasoned past the handoff three times** — counting by id not name, counting across
the asset not the graph, and `IsUnused` following the real count. ⚠ **§2 and §3 here are the other half
of that same reasoning: the count exists so the delete can use it, and the gesture is worthless if it
cannot be undone.**
