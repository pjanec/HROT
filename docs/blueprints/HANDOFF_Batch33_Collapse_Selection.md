# HANDOFF — Batch 33: `BP-74` — collapse a selection into a Function or Macro

> 📌 **Dispatched at `<pending>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-74`, `BP-76`, `BP-77`, `BP-82` are *referenced*.
> **You allocate** every new id and diagnostic code (rule 5).
>
> 📄 **[Q26](Architect_Question_26_Collapse_Selection.md) is the design and every question is SETTLED**
> — A3 · B2 · C · D1 · E1 · F. ⛔ **Do not reopen them**; build to the table.
>
> ⭐ **This is the feature the user asked for.** The macro programme built a destination; this is the
> road to it. **Nobody authors an empty macro and rewires a graph into it by hand.**
>
> ⭐ **Batch 32 was flawless** — nothing found wrong, counts right on arrival, and you generalised
> `BP1667` and the mirror rebuild past what was asked. Same standard here.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** boundary analysis | ⭐ **all of it** — the dedup and cycle cases (§1.3) are where this goes wrong | the plan record + its unit fixtures |
| **2** the two emitters | the Function legality rules | the Macro emitter, mirroring |
| **3** sink + undo | the single-undo-entry shape | the `NodeId`→`Guid` plumbing |
| **4** menu + refusal surface | — | ⭐ **entirely** — ⚠ but read §4's warning |
| **5** the round-trip comparator | ⭐ **yes** — what "equivalent" means is the whole test | the fixtures |

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

## 1. 🔴 The boundary analysis — in `.Compiler` (Q26-D1)

### 1.1 Where it lives, and why that is reachable

⚠ `.Editor` does **not** reference `.Compiler` directly. It does not need to — the chain is
**`.Editor → .Core → .Compiler`** (`Hrot.Blueprints.Core.csproj` carries the `ProjectReference`).
⭐ **Not a theory: `BlueprintClipboard` already calls `GraphFragmentCloner` across exactly this path.**

⇒ Put it beside `GraphFragmentCloner` in `.Compiler/Compiler/Transform/`. ⭐ **Its exact inverse,
`Stage2_5_ExpandMacros`, is already in that assembly** — keep an operation and its inverse together.

📌 Two facts that keep editor concerns out of it: `BlueprintCommandSink.ResolveNodeId` (`:383`) already
maps the editor's `NodeId` → `Guid`, and **`Pin.TypeRef` carries the type**, so building
`ParameterDecl`s needs **no type registry**.

### 1.2 It is a **pure function**: selection → plan **or** refusal

```
Analyse(Graph host, IReadOnlyCollection<Guid> selection, CollapseTarget kind)
    → CollapsePlan            // entries, exits, inputs, outputs
    | CollapseRefusal         // reasons, each naming the offending node ids
```

⚠ **No mutation, no editor types, no `ImGui`.** That is what makes it headlessly testable and is the
reason it is not in `.Editor`.

**The plan is the four boundary sets** — everything else is a degenerate case of this table:

| Crossing | Becomes |
|---|---|
| **exec** into the selection | one `ExecInDecl` ⭐ (**N of them — Batch 32 shipped this**) |
| **exec** out of the selection | one `ExecOutDecl` |
| **data** into the selection | one `Graph.Inputs` entry |
| **data** out of the selection | one `Graph.Outputs` entry |

### 1.3 ⚠⚠ The four cases a naive implementation gets wrong

| # | Case | Right answer |
|---|---|---|
| **a** | ⭐ **One outside producer feeds *two* selected nodes** | **ONE** input, not two. **Dedup by source pin**, and both interior consumers re-tie to the same parameter. Naive code emits duplicate parameters with the same name |
| **b** | **One selected node feeds *three* outside consumers** | **ONE** output. Dedup by source pin again, on the other side |
| **c** | 🔴 **A cyclic boundary** — a selected node feeds an outside node that feeds *back* into the selection | ⛔ **Refuse.** The extracted graph would need its own output before its input. ⚠ **This is the one refusal that is not obvious from the four-set table**, and it is silent corruption if missed |
| **d** | **The selection contains the graph's own `EventEntryNode` or `ReturnNode`** | ⛔ **Refuse** — those are the host graph's boundary, not movable content |

### 1.4 Legality — reuse the rules, do not invent them

| Target | Rule |
|---|---|
| **Macro** | ⭐ **latent nodes ALLOWED** (Q26-F). ⚠ Unreal refuses this; its refusal forbids by gesture what its own capability permits, and `BP-78` records that factoring out a reusable *latent* sequence is the one thing macros can do that nothing else can |
| **Function** | ⛔ **no latent node**, and ⛔ **at most one exec exit** (a Function returns once) |

⚠ **The latent rule already exists and is already correct** — `BP1661`, fixed in Batch 31 to gate on
*"is this graph a `FunctionCall` target"* rather than graph kind. ⛔ **Do not write a second
latent-detection rule**; reuse `FindTransitivelyLatentNode`.

---

## 2. 🟠 The two emitters

Shared analysis, two outputs:

| | Creates | Call node |
|---|---|---|
| **Macro** | a `GraphKind.Macro` graph — `ExecInputs` from entries, `ExecOutputs` from exits, `Inputs`/`Outputs` from data crossings | `MacroCallNode` (**one field**, `TargetGraphId` — F4) |
| **Function** | a `GraphKind.Function` graph | `FunctionCallNode` |

⭐ **Reuse `GraphFragmentCloner`** to lift the selected nodes: it returns `NodeMap`/`PinMap`, which is
exactly what re-tying the boundary needs. ⚠ **Rebuild `LinkedToIds` wholesale afterwards** — Batch 32
established `RebuildLinkedToIds` as the pattern; do not patch per rewire.

⚠ **Both pin projections move together** — `NodePinSchema` **and** `Stage0_Rehydrate`. Every batch that
moved one and not the other produced a silent shape mismatch.

---

## 3. 🟠 The sink — and it is **one** undo entry

`BlueprintCommandSink` has **no case** for either command today, so both fall to `default:` —
*"unknown commands are silently accepted (forward-compat)"* → `return new GraphCommandResult(true, null)`
(`:218-220`). ⭐ **Dispatching a collapse right now reports SUCCESS and does nothing.** Trap #5, sitting
in the tree.

**One undo entry (BP-60 precedent).** The canvas records forward/inverse pairs via
`view.Execute(fwd, inv, "label")` — see `DrawContextMenu`'s *"Add Return"*. ⚠ Collapse mutates a lot
(new graph, N nodes moved, many links re-tied) and **must still be a single entry** — a designer
pressing Ctrl+Z once expects the whole gesture back.

⚠ **Test undo explicitly**: collapse → undo → the host graph is structurally identical to before, **and
the created graph is gone.** ⭐ A half-undo that leaves an orphan macro graph behind is the defect to
watch for.

---

## 4. 🟢 Menu + the refusal surface — ⚠ **do NOT copy the neighbouring idiom**

Add the items to `CanvasRenderer.DrawContextMenu`'s `HoverKind.None` branch (`:553+`), beside
*"Add Comment"*, which is the existing **selection-aware** precedent
(`CanvasCommands.AddCommentAroundSelection`, gated on `view.Selection.Nodes.Any()`).

⛔⛔ **Here is the trap.** The surrounding code uses `ImGui.MenuItem(label, shortcut, selected, enabled)`
and **greys items out** — `Paste` and `Add Comment` both do. ⭐ **Q26-B2 forbids that for collapse.**

> **Offer the items whenever there is a selection. Do NOT grey them out for illegality.
> Refuse on invoke, with a message naming the offending nodes.**

⚠ **The reason is in this repo's own history**, not taste: the tracker files greyed-with-no-explanation
as a **defect** — **`BP-76`** (*Go to Definition and Expand render **permanently greyed***) and
**`BP-77`** (*a live button with no handler*). **Shipping a permanently-greyed collapse item would be
filing the next BP-76 ourselves.** A greyed item does not say why; an error at the moment of asking
teaches the rule.

📌 **The message surface exists:** `IEditorIndicators.Notify(EditorNotification)` →
`ToastQueue.Enqueue` (`NodeEditor.Core/Action/`), already wired in `BlueprintDocumentFactory:346`.
**Confirm its exact shape and use it**; do not invent a second notification path.

### ⭐⭐ 4a. `CanvasRenderer` is SHARED UI — go through the host-command seam

⚠ `CanvasRenderer` and `CanvasCommands` live in **`NodeEditor.UI`**, which the **BTree and HSM editors
also use**. ⛔ **Blueprint concepts must not be hardcoded there.**

⚠ **`BP-76` is exactly that mistake, sitting in the file you are about to edit** (`:740-753`):

```csharp
bool canNavigate = node != null && (node.Kind.Id == "Function.Call" ||
                                    node.Kind.Id == "Macro.Call"    || …);
bool canExpand   = node != null && (… || node.Title == "ScaleBy");   // ⚠ a demo node TITLE
```

Blueprint kind ids are `"FunctionCall"`/`"CallCustomEvent"`, so **nothing ever matches and both items
render permanently greyed** — a shared component guessing at one host's vocabulary.

⇒ **Use the seam that already works: `CommandCatalog` + `_editorCommands?.Invoke(...)`,** exactly as
**`Paste`** does (`editor.paste`). Add collapse ids to `CommandCatalog`, invoke them from the menu, and
**register the handlers host-side** in the Blueprint editor.

⭐ **And here is why B2 makes this clean rather than merely acceptable:** because the item is *always
offered* and refuses **on invoke**, the shared component needs **no knowledge of blueprint legality at
all**. Enablement reduces to *"is anything selected"* — kind-agnostic, and legitimate in shared UI.
⇒ **The UX ruling and the architecture agree.** Greying out would have forced blueprint rules back
into `NodeEditor.UI` and produced BP-76 a second time.

📌 **Out of scope:** `BP-77`'s *"Macros +"* button and `BP-76`'s menu gating stay open — collapse gives
macro creation a **second, better** entry point but does not close either row.

---

## 5. 🔴 The round-trip invariant (Q26-E1) — **the proof the user asked for**

> **collapse a selection to a macro → expand it again → structurally equivalent to the original**

⭐ **This is nearly free and it is the strongest evidence available**, because expansion already exists
and is **proven by execution** (Batch 31: spliced, compiled through real Roslyn, ticked across frames).

⚠ **"Equivalent" needs a definition, and that definition is the test.** 📐 **Your call, state it:**

| Compare | Ignore |
|---|---|
| node **kinds** and their multiset | node **ids** (fresh by construction) |
| link **topology** (which pin-role connects to which) | **pin ids**, editor **positions** |
| declared inputs/outputs by **name + type** | declaration **order**, unless you argue it is load-bearing |

⇒ **A canonical graph hash.** ⚠ Build it as a **reusable comparator**, not a one-off assert — `BP-76`'s
`ExpandNode` will want the same thing.

⭐ **Why a property and not examples:** Batch 31 is the argument. `BP1661` shipped gated on an inverted
condition with the **entire suite green**, because every fixture encoded the same wrong assumption.
**A round-trip property cannot encode the assumption it is testing.**

⚠ **Scope honestly:** the invariant binds the **Macro** path only — `Stage2_5_ExpandMacros` is its
inverse, and **there is no function-inlining pass**. The Function path gets the weaker proof below.
**Say so in the row rather than implying both are covered.**

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (`RESUME_START_HERE.md` §3).
⭐ **Run `python3 scripts/tracker-counts.py --check`** before your final commit — Batch 32 was the first
batch to arrive with the counts already right; keep that.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3161** / 0 / 10 skipped ⚠ *(total 3171 — `BP-111` filters 7 host-timing tests out)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

⚠ **This batch touches `NodeEditor.UI`** (the menu) — those two gates stop being incidental.

### Tests

| Layer | |
|---|---|
| ⭐ **Round-trip** | collapse → expand → canonical-equal. **Several shapes**: 1-in/1-out · **2 entries** · 2 exits · shared input (case a) · fan-out output (case b) |
| **Refusals** | cyclic boundary (c) · boundary node selected (d) · Function + latent · Function + 2 exits — each asserting **the reason and the node ids named** |
| ⭐ **Latent to Macro** | Q26-F: a selection containing `Delay` collapses, then **compiles through real Roslyn and ticks across frames** ⚠ (`.Succeeded` never invokes Roslyn) |
| **Undo** | one entry; host restored **and** the created graph gone |
| **Function path** | collapse → compile through Roslyn → **run and assert a value** (no inverse exists, so this is its proof) |
| **Sink** | the commands no longer fall through `default:` — ⭐ a test that would have caught the silent-success arm |

---

## 7. Reporting

Per-suite numbers · the **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) · **your
definition of structural equivalence** (§5) · ⭐ **whether the latent collapse actually ran, and the
value you asserted** · anything here **wrong against the code**.

⭐ **You have corrected the coordinator in most batches and been right every time.** If something above
does not match the tree, say so plainly — it is the most valuable line in your report.
