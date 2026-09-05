# HANDOFF — Batch 34: make collapse reachable, and close `BP-221`

> 📌 **Dispatched at `6087fc80`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-74`, `BP-76`, `BP-77`, `BP-221`, `BP-222` are
> *referenced* existing rows. **You allocate** anything new (rule 5).
>
> 📄 Design: **[Q26](Architect_Question_26_Collapse_Selection.md)**, all settled. ⛔ Do not reopen.
>
> ⭐ **Batch 33 finished the headless core and stopped cleanly at a stated boundary.** This batch is the
> other half: **make it reachable from the canvas**, then fix the 🔴 it walked into.
>
> ⭐⭐ **Almost all of this is headless.** `ClipboardCommandTests` / `GraphSwitchingTests` already invoke
> registered editor commands directly (`commands.Invoke(CommandCatalog.Paste)`), so **the whole command
> path is provable without the UI.** ⚠ Only the ImGui menu item itself needs eyes — two clicks, once.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** sink + undo | ⭐ **the inverse** (§1.2) — get it wrong and undo half-restores | the sink cases, the plumbing |
| **2** host commands | — | ⭐ **entirely** — mirror the `Paste` registration |
| **3** menu | — | ⭐ **entirely** — ⚠ but read §3's warning |
| **4** `BP-221` | the emitter fix and its blast radius | the regression test |
| **5** `BP-222` | ⚠ **reproduce first** (§5) | — |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | The **tracker + detail docs are yours** for this batch |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

---

## 1. 🔴 The sink, and **one** undo entry

### 1.1 The cases

`BlueprintCommandSink` has **no case** for `CollapseToFunction` / `CollapseToMacro`, so both reach
`default:` — *"unknown commands are silently accepted (forward-compat)"* → `GraphCommandResult(true, null)`
(`:218-220`). ⭐ **A collapse dispatched today reports SUCCESS and does nothing.**

Each case: map `NodeId` → `Guid`, run **Batch 33's `CollapseAnalysis`**, then apply the plan **or**
surface the refusal (§3). 📌 `BlueprintDocumentFactory.SelectedNodeIds(view)` already returns
`IReadOnlyCollection<Guid>` — ⭐ **exactly the analysis's parameter type.** No new plumbing.

⛔ **Do not re-derive any boundary logic in the sink.** Batch 33 put it in `.Compiler` deliberately; the
sink's job is *map ids → call → apply or report*.

### 1.2 ⚠⚠ The inverse — the part to get right

**Collapse must be ONE undo entry.** A designer pressing Ctrl+Z once expects the whole gesture back.
Two mechanisms exist and both are already handled by the sink:

| | |
|---|---|
| `GraphCommand.Batch(Label, Commands)` | handled at `:192` → `ApplyBatch` (`:1371`) |
| `BlueprintEditCommand(string Label, Action Mutate)` | handled at `:215` → `ApplyBlueprintEdit` (`:1044`) |

⭐ **`BlueprintEditCommand` is the right transport**, because collapse **creates a graph** and
`GraphCommand`'s vocabulary is node/link-only — there is no *AddGraph*. Pair a forward and an inverse
and hand both to `view.Execute(fwd, inv, "Collapse to Macro")`, exactly as *"Add Return"* does.

📐 **The inverse itself is your call. My lean, with the reasoning:**

> **Snapshot the host graph before mutating; the inverse restores the snapshot and drops the created
> graph.**

⚖️ Coarse, but **exactly correct**, and correctness is what matters for a restructure this broad. A
per-edit inverse would have to re-create nodes and links with their **original ids** — and collapse
mints fresh ones through `GraphFragmentCloner`, so a naive inverse silently returns a graph that
*looks* right and has different identity.

⛔⛔ **The tempting wrong answer: "undo = expand the call node back."** It is not the inverse. Expansion
mints **fresh ids** too, so undo→redo→undo would drift identity every cycle, and every pin GUID —
`SHA-256("pin:{nodeId}:{name}:{direction}")` — changes with it. **Breakpoints, the debug map and any
saved reference would follow a node that no longer exists.**

⚠ **Test undo explicitly, and assert on identity, not just shape:** collapse → undo → the host graph is
**canonically equal to the original** (⭐ Batch 33's `CanonicalGraphShape` is already the tool) **and the
created graph is gone.** ⭐ A half-undo leaving an orphan macro graph behind is the defect to hunt.

---

## 2. 🟢 Host commands — mirror `Paste` exactly

Add two ids to `CommandCatalog` (`NodeEditor.Core`), then register handlers in
`BlueprintDocumentFactory` beside `Cut`/`Paste`/`Duplicate` (`:950-980`):

```csharp
reg.Add(CommandCatalog.CollapseToMacro, "Collapse to Macro", "Edit",
    ctx => CollapseFrom(view, currentGraph(), SelectedNodeIds(view), CollapseTarget.Macro, markDirty),
    isEnabled: () => view.Selection.Nodes.Any(),      // ⭐ selection ONLY — see §3
    description: "Moves the selected nodes into a new macro and calls it.",
    defaultKey: /* your call */);
```

⚠ **`isEnabled` checks the selection and NOTHING else.** ⛔ Not latency, not exec-entry count, not
graph kind. **Legality is decided on invoke and reported** (§3).

---

## 3. 🟢 The menu — ⚠⚠ **the trap is in the file you are editing**

Add the items to `CanvasRenderer.DrawContextMenu`'s `HoverKind.None` branch (`:553+`), beside
*"Add Comment"* — the existing selection-aware precedent — and dispatch with
`_editorCommands?.Invoke(...)`, exactly as **`Paste`** does.

⛔⛔ **`CanvasRenderer` lives in `NodeEditor.UI`, which the BTree and HSM editors share.**
**`BP-76` is that mistake, in this same file** (`:740-753`):

```csharp
bool canNavigate = node != null && (node.Kind.Id == "Function.Call" || node.Kind.Id == "Macro.Call" || …);
bool canExpand   = node != null && (… || node.Title == "ScaleBy");   // ⚠ a demo node's TITLE
```

Blueprint kind ids are `"FunctionCall"`/`"CallCustomEvent"`, so nothing matches and both items render
**permanently greyed** — a shared component guessing at one host's vocabulary.

⭐ **Q26-B2 is what keeps you out of that hole.** Because the item is **always offered** and refuses
**on invoke**, `NodeEditor.UI` needs **no knowledge of blueprint legality at all**. ⇒ **Never grey a
collapse item for illegality.** A greyed item does not say why; an error at the moment of asking
teaches the rule — and the tracker files greyed-with-no-explanation as a **defect** (`BP-76`, `BP-77`).

📌 **Refusal surface:** `IEditorIndicators.Notify(EditorNotification)` → `ToastQueue.Enqueue`
(`NodeEditor.Core/Action/`), wired at `BlueprintDocumentFactory:346`. **Confirm its shape and use it**;
do not invent a second notification path. ⭐ **The message must name the offending nodes** — Batch 33's
`CollapseRefusal` already carries them.

📌 **Still out of scope:** `BP-77`'s *"Macros +"* button and `BP-76`'s own gating. ⚠ **But you are now
one line from BP-76** — if fixing its kind-id gate falls out naturally, say so and do it as its own
commit; otherwise leave it.

---

## 4. 🔴 `BP-221` — an AiPrimitive never emits its `Func_*` helpers

✅ **Coordinator-verified independently.** `InstanceEmitter:80-86` emits a helper per non-tick Function
graph:

```csharp
var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
    ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);
foreach (var fg in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function && g != tickGraph))
    EmitInstanceFunctionMethod(e, asset, fg);
```

`AiPrimitiveEmitter:157` picks a tick graph **the same way** — and has **no equivalent loop.** The call
site is emitted regardless ⇒ **`CS0103` against a method that does not exist.**

⚠ **Pre-existing and reachable by ordinary hand-authoring.** Collapse merely walked into it, exactly as
Batch 31's payoff test walked into `BP1661`. ⇒ ⭐ **Write the regression test as a hand-authored
AiPrimitive asset with a second Function graph and a `FunctionCall`** — *not* as a collapse test.
**Prove it independent of the feature that found it**, or the fix looks like a collapse detail.

⚠ **Check the blast radius before mirroring the loop:** the two emitters differ in more than this
(hosting, `Parameters`, `WorkingState`). **Report what else diverges** even if you do not fix it.

---

## 5. ⚠ `BP-222` — **reproduce before fixing**

*A zero-output Function-graph call assigns a void helper (`CS0815`).* ⭐ **You filed it deliberately
unattributed, and that instinct was right** — it may be the `BP-104` family or the call-site emitter.

⇒ **Reproduce it from a hand-authored graph first**, then attribute, then fix. ⛔ **Do not fix the half
collapse happened to reach.** ⭐ Fixing the symptom you stumbled into is how a root cause gets
mis-attributed and re-opened three batches later. **If reproduction shows it is not what the row says,
say so** — a corrected row is worth more than a fix.

📌 **If BP-221 and BP-222 together unblock it, add the Function-path compile proof** Batch 33 could not:
collapse to a Function → **through real Roslyn** → run → assert a value. ⚠ Only if they genuinely
unblock; do not force it.

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (`RESUME_START_HERE.md` §3) — ⭐ **and this batch
edits `NodeEditor.Core` + `NodeEditor.UI`, so they are load-bearing.**
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival for two batches running now.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3178** / 0 / 10 skipped ⚠ *(total 3188 — `BP-111` filters 7 host-timing tests out)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

⚠ **A `CommandCatalog` addition is a shared-assembly change** — the BTree and HSM editors compile
against it. A green solution build is the check that you added rather than renamed.

### Tests

| | |
|---|---|
| ⭐ **The command path, headless** | `commands.Invoke(CommandCatalog.CollapseToMacro)` on a prepared selection ⇒ the host graph collapsed and the macro created. ⭐ `ClipboardCommandTests` is the precedent |
| ⭐ **Undo** | one entry; host **canonically equal** to the original **and** the created graph gone |
| **Redo** | collapse → undo → redo ⇒ canonically equal to the post-collapse state |
| **Refusal reaches the user** | an illegal selection ⇒ **no mutation** *and* a notification naming the offending nodes. ⚠ **Assert the notification**, not just the absence of change |
| **Not greyed** | the command is **enabled** for an illegal-but-non-empty selection — ⭐ the test that locks Q26-B2 against a future "helpful" `isEnabled` |
| **`BP-221`** | hand-authored AiPrimitive + second Function graph + `FunctionCall` ⇒ compiles **through real Roslyn** ⚠ (`.Succeeded` never invokes Roslyn). Revert-goes-red |

⚠ **The one visual check** — the menu items appear on right-click with a selection, and clicking one
collapses. **Two clicks, once.** Everything else above is headless; **say which you actually did.**

---

## 7. Reporting

Per-suite numbers · the **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) · **your inverse
design** (§1.2) and whether undo asserts identity · **what `BP-222` reproduced as** · **what else
diverges between the two emitters** (§4) · ⭐ **whether you did the visual check** · anything here
**wrong against the code**.

⭐ **Your last two batches found defects the coordinator's review had passed.** If something above does
not match the tree, say so plainly.
