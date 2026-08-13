# HANDOFF — Batch 41: ⭐ **`BP-57`'s last mile — the locals authoring UI**

> 📌 **Dispatched at `PENDING`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic. **You allocate
> everything new** (rule 5).
>
> ⭐⭐ **This closes `BP-57`.** The compiler half landed in 37 and was corrected in 39; a local is
> declarable **in JSON only**. ⛔ **After this, it is authorable.**
>
> ⚠ **It runs BEFORE the `U-` sequence** ([the plan](PLAN_Variable_Unification_Tasks.md)) deliberately:
> it is `BP-57`'s last mile **and it sits on the very surfaces `U-4`…`U-6` then change.** ⇒ ⭐ **§1 is
> written so that the unification ABSORBS this work rather than undoing it.**
>
> 📄 **Supersedes [Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md) §3**, which was drafted before
> the two reviews. ⛔ **Read this, not that.**

---

## 0. Scope

| | |
|---|---|
| **§1** ⭐⭐ | the **locals schema source** — the foundation the rest hangs off, and what makes `U-4`…`U-6` cheap |
| **§2** ⭐ | the **Local Variables section**, following the canvas |
| **§3** 🔴 | the **picker**, and a bug that makes a local-targeting node show a **raw GUID** |
| **§4** 🔴 | **rename and delete** — delete is where `BP-225`'s shape lands |
| **§5** ⭐ | the **node badge** ⚠ **touches `NodeEditor.Core` + `.UI` — two gates move.** Last, and separable |
| **§6** 📌 | one misplaced doc comment |

⚠ **If the batch runs long, stop cleanly before §5.** ⭐ **§1–§4 close `BP-57`**; §5 is a readability
win that costs two extra gates. **Say where you stopped.**

⛔ **NOT in this batch:** the unification (`U-1`…`U-16`) · widening the picker to structs (`BP-228`) or
to `WorkingState`/`Parameters` (`BP-226`) · `BP1650`'s remaining latency copy (`BP-233`, deliberately
left as its own slice).

---

## 0a. ⚡ How to work

**You are on Opus.** 🟢 **Delegate to Sonnet:** §2's section wiring and §6 — both mirror-an-existing-
pattern. ⭐ **Opus keeps §1's shape, §4's delete semantics and §5's ruling.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker + detail docs are yours** for this batch |
| **Revert-goes-red** | every item, **never delegated** |
| **Commit per item** · **no PR** | |

📌 **Housekeeping:** `claude/batch39-locals-preserved` is fully merged and can be deleted. **Say if you did.**

---

## 1. ⭐⭐ The locals schema source — build this FIRST

> ⭐ **The single most important instruction in this handoff.** Everything else is ordinary UI work.

**Implement the locals model as an `IVariablesSchemaSource`** (`Hrot.Editor.AiShared/Blackboard/
VariablesPanelControl.cs`), and have §2's section **project it**.

⚠ **Why, in one line:** `U-6` moves every variable surface onto that interface. ⭐ **A locals source
written to it is absorbed; a bespoke one has to be undone.**

### ⛔ What you must NOT do — and this is a trap the reviews already found

| | |
|---|---|
| ⛔ **Do NOT add a member to `IVariablesSchemaSource`** | ⚠ **That is `U-5`'s `V2` finding**: the capability flag is a **shared-interface** change that moves the **AiShared gate (1213)** and touches `BTreeHsmSchemaSource` + the HSM source. ⭐ **Not this batch.** *Implementing* the interface is free; *changing* it is not |
| ⭐ **DO implement `CountNodesReferencingVariable` honestly** | ⚠ `BlueprintVariableSchemaSource` returns a hardcoded **`0`** (`BP-230`) — ⛔ **trap #5, and §4 depends on this member being real.** **Your new source must not copy that.** Fixing the *existing* one is `U-5`; **yours starts correct** |
| ⛔ **Do NOT implement `UpdateVariableRole`/`UpdateVariableScope`** | ⭐ **`Q-k` ruled `Role`/`Scope` are READ-ONLY for blueprints** — changing either is a *move* with reference consequences, not a toggle. They are **default-bodied** members; leaving them is the contract's intended shape |

### ⚠ And `Role`/`Scope` do not arise here at all

§2's section is **My Blueprint**, not the shared table. ⭐ **The table with its `Role`/`Scope` columns
is `U-6`.** ⇒ **this batch has no reason to touch either, which is why it can precede `U-5`.**

**Gate:** the source projects the current graph's locals · `CountNodesReferencingVariable` returns
**0, 1 and 3** correctly, the 3 spread across two graphs · **it compiles without any change to
`Hrot.Editor.AiShared`** (⭐ **assert by the AiShared gate staying at 1213**).

---

## 2. ⭐ The Local Variables section — following the canvas

**User-ruled**, twice. ⛔ **Not open.**

| | |
|---|---|
| **A new `Local Variables` section** | in `BlueprintMyBlueprintModel`, alongside `Graphs · Functions · Macros · Custom Events · Variables` |
| ⭐ **Filled from the CURRENT GRAPH** | *"which graph"* is answered by **which graph is open** — a flat scope column cannot say it, which is the user's own objection |
| ⭐ **Always present, empty when the graph has none** | ⛔ **A section that appears and disappears reads as a broken feature** |
| **`[+]` where applicable** | see below |

### ⚠ What this costs — measured

| | |
|---|---|
| ⛔ `_sections` is `static readonly` | a sixth entry is trivial; a **context-sensitive** one is the work |
| ⛔ `Retarget(IEditableAsset?, BlueprintAsset?)` | **asset only — the model has no current-graph concept at all** |
| ✅ ⭐ **the wiring exists** | `AiCanvasContext.CurrentGraphId` (from `BlueprintGraphSwitcher`), **already consumed by `GraphSignatureWindow`** — that is `BP-72`, whose lesson was *a panel editing the graph you are not looking at is a defect*. ⭐ **Follow it; do not invent a second mechanism** |

### ⛔⛔ On "where applicable" — silence is not an option

`MyBlueprintSectionDescriptor.CanCreateItems` is a **static bool**, and the excluded case is a
**`Macro` graph** (`BP1664` — a macro is spliced, so a macro-local has nothing to be scoped to).

⭐ **The designer must learn WHY**, per the standing ruling that decided `Q26-B2` — *"grey out does not
educate the user"* — and `BP-76`/`BP-77`, both filed **because** something was greyed with no
explanation. ⇒ either `[+]` is absent **and the empty section says why**, or it is present **and
refuses out loud** through the `IEditorIndicators` surface `BP-223` repaired. 📐 **Your call which;
⛔ a silently missing button is not one of them.**

---

## 3. 🔴 The picker — and the raw-GUID bug

### 3.1 The blocker

`BlueprintPickerSources:148-152`:
```csharp
if (string.IsNullOrEmpty(text)) return _asset.Variables;
return _asset.Variables.Where(v => v.Name.Contains(text, ...)).ToList();
```
⇒ ⭐ **Even a JSON-declared local cannot be aimed at from the editor.** Offer the current graph's
locals **as well as** the asset's variables.

⚠ **Distinguish them in the list.** `Q27-C1` permits shadowing, so a local `Scratch` and an asset
`Scratch` are **two identical-looking rows that read different storage**.

⛔⛔ **Do NOT widen it further.** `WorkingState`/`Parameters` are `BP-226`'s space; struct FQNs are
`BP-228`'s (*any dotted string compiles* — `a.b` emits `global::a.b`). ⭐ **One line in your report
confirming you widened it to locals and nothing else.**

### 3.2 🔴 The bug that exists regardless of the picker

`BlueprintNodeModel.ResolveVariableName` (`:425-448`) resolves a Get/SetVariable's title through
**`Variables` then `WorkingState`** and nothing else ⇒ ⚠ **a local-targeting node displays a RAW
GUID.** Add the `LocalVariables` branch.

📌 **Keep the existing fallback shape** — an unresolvable id is returned as-is *"so a dangling
reference stays visible on the node rather than reading as a valid"* reference.

---

## 4. 🔴 Rename and delete

| | |
|---|---|
| **Rename** | ⭐ **Safe — prove it with a test, not a comment.** A local resolves by **id** (`FindLocalIndex` is id-only), so a rename cannot re-target anything. ⚠ **This is the OPPOSITE of `BP-225`'s pins**, where identity is the *name*. **Do not carry that fear across** |
| **Delete** | 🔴 **Leaves every `Get`/`SetVariable` dangling.** ⭐ **`BP1670` now catches it as a Stage-2 diagnostic** (Batch 39) — so this is no longer silent, ⛔ **but shipping a gesture that reliably breaks the asset is still wrong.** ⇒ **use §1's real reference count**: take the references with it, or refuse while referenced, and **hand them back for the undo** — `BP-225`: an undo restoring only the declaration recreates the dangling state |
| **Duplicate names** | 📐 Two locals sharing a name in one graph. ⚠ **`BP-225` refused this for exec declarations because two decls collapsed onto ONE pin id. Here ids are distinct** ⇒ not corrupting, only confusing. ⭐ **Different problem, different answer — decide and say which** |
| 📌 **`BP-231`** | `RemoveVariable`/`RenameVariable` do not maintain the order lists. **Benign today; if your delete path touches them, fix it and say so** |

---

## 5. ⭐ The node badge — **last, and it moves two gates**

**User-ruled: a badge.** ⛔ **NOT colour** — colour already means **type**
(`BlueprintTypeSystem.GetAccentColorForTypeId`), and overloading it puts two meanings on one channel.

⚠ **Why it is needed:** `Q27-C1` permits shadowing ⇒ a local `Scratch` and an asset `Scratch` render
**pixel-identical while reading different storage.** Unreal has this ambiguity; we were asked to do
better.

| | measured |
|---|---|
| ✅ `MyBlueprintItem` | **already has `BadgeText` and `IconKey`** ⇒ the **panel** side is free |
| ⛔ `INodeModel` | `Title` · `Subtitle` · `Category` · `StatusTooltip` — **no badge.** ⇒ a new member on **`NodeEditor.Core`** *and* rendering in **`NodeEditor.UI`**'s `CanvasRenderer` |
| ⛔ **`Subtitle` is NOT a shortcut** | **`BP-17` owns it**: a node with a custom title puts the generated title there. **A badge there collides with every renamed node** |
| ⚠ **Two gates move** | **NodeEdit Core 208** · **UI 131** — ⭐ **and they take NO `--no-build`** (§7) |

📐 **Yours: the badge's shape** (glyph, short text, tooltip) and whether the panel item mirrors it.
⚖️ **Both surfaces is the lean** — the panel supports it for free, and two views disagreeing is its own
defect.

---

## 6. 📌 One misplaced doc comment

`GraphTypes.cs:64-82` — the **`BP-220` block explaining `WithNodesAndLinks` and the reflection guard**
is attached to **`LocalVariables`**, because Batch 37 inserted the field between the comment and its
method. ⇒ `LocalVariables` carries **two consecutive `<summary>` blocks** and `WithNodesAndLinks` is
undocumented. Silent (doc generation off). **One-line fix, no row.**

---

## 7. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — and **§5 moves both**, so run them properly.
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival **nine** batches running.

**Baseline — coordinator-run, post-Batch-40:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** *(⚠ an incremental build under-reports — Batch 38 recorded this honestly; do the same)* |
| Blueprints | **3269 total / 3259 passed / 0 failed / 10 skipped** |
| AiShared **1213** ⭐ *(§1: this must NOT move)* · BTree **612** · Breakpoints **130** | 0 failed |
| Generators **193** · NodeEdit Core **208** · UI **131** ⚠ *(§5 moves the last two)* | 0 failed |

### Tests

| | |
|---|---|
| ⭐⭐ **§1** | the source projects the current graph's locals · **the reference count is real at 0, 1 and 3** · ⭐ **AiShared still 1213** |
| ⭐ **§2** | switch graphs ⇒ the section's contents change · **present and empty** when the graph has none · a `Macro` graph ⇒ **your chosen refusal, asserted** |
| ⭐ **§3** | a `Get` targeting a local resolves through the picker path and compiles to the local · ⭐ **its node title is the NAME, not a GUID** · a local `Scratch` beside an asset `Scratch` is **distinguishable** |
| ⭐ **§4** | delete a referenced local ⇒ your ruling, **and undo restores the declaration AND the references** · rename ⇒ references still resolve · ⭐ **round-trip: an asset with locals survives save/load** |
| **§5** | the badge is present on a local-targeting node and absent on an asset-variable one ⚠ *(model-level; rendering is the visual check)* |

⛔ **Say plainly what is NOT covered by a headless test.** ⭐ **The visual check has not run for six
batches**, and §2/§3/§5 are exactly what it would catch.

---

## 8. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) ·
⭐ **confirmation that `Hrot.Editor.AiShared` was NOT modified** (§1) · **your `[+]`-where-applicable
choice and how the designer learns why** (§2) · **confirmation you widened the picker to locals and
nothing else** (§3) · **your duplicate-name ruling** (§4) · **the badge's shape, and whether
`INodeModel` changed** (§5) · **where you stopped** · ⭐ **whether `BP-57` can now be ticked** ·
anything here **wrong against the code**.

⭐ **You have corrected this coordinator in every batch since 29, most recently by killing a gate I had
called the strongest in the plan.** ⚠ **§1's "absorbed not undone" claim and §4's delete semantics are
my reasoning, not measurement. Treat them accordingly.**
