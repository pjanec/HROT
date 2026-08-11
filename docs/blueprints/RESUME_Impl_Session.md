# RESUME — implementation session · **Batch 34 (finish `BP-74`) is next**

> **Written immediately before a context compaction. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-11**, at `3446b6d`.
>
> ⭐ **Batches 29–33 are all delivered, coordinator-verified and merged.** Macros work end to end —
> authored, called, expanded, compiled through real Roslyn, ticked across frames, and debuggable.
> Collapse-a-selection works **headlessly** and its round-trip property is proven.
> ⛔ **Collapse is NOT reachable from the canvas.** That is Batch 34. §2 is the whole story.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch — PUSH HERE** | ⭐ **`claude/hrot-implementation-j1jvin`** |
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`** · at `3446b6d` |
| **HEAD** | fast-forwarded onto `3446b6d`; **Batch 33 merged and verified** |
| **Counts** | **63 open · 94 done** — ⚠ *derive them, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **New finding IDs** | **BP-223+** · diagnostics **BP1669+** (⚠ `BP1664` is *reserved and unbuildable* — see §5) |

⛔ **No PR unless the user explicitly asks.** There has never been one in this programme.
⛔ **Never put a model identifier** in a commit message, code comment, or anything else pushed.

---

## 0 · First actions, in this order

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge --ff-only origin/claude/blueprint-authoring-status-gm0akp     # rule 7 — ALWAYS
export PATH="$HOME/.dotnet:$PATH"                                       # see §4
```

1. ⭐ **Re-sync from the coordinator branch first** (`.claude/CLAUDE.md` **rule 7**) and **again before
   your final commit** (**rule 4**). Skipping this caused three ID collisions in past batches.
2. Read the **Batch 34 handoff** if one exists (`docs/blueprints/HANDOFF_Batch34_*.md`). At the time of
   writing it was **not yet authored** — §2 below is what it will cover.
3. Read **[HANDOFF_Batch33_Collapse_Selection.md](HANDOFF_Batch33_Collapse_Selection.md) §3 and §4** —
   those two items are the unfinished half and the handoff text still stands verbatim.
4. Skim **[RESUME_START_HERE.md](RESUME_START_HERE.md)** §3 (gates) and §5 (verified facts —
   **do not re-derive them**).

---

## 1 · ⛔ Read this before touching collapse — what Batch 33 did *not* do

Quoting the Batch 33 report, unchanged because it is still true:

> **Collapse is not reachable from the canvas.** The sink cases, the single undo entry, and the
> context-menu items are absent. `BlueprintCommandSink` still has **no case** for either command, so a
> dispatched collapse **still falls to `default:` and reports success while doing nothing** — the
> **trap #5** the handoff flagged is unchanged and still sitting there.

This was a **deliberate clean stop at a boundary**, permitted by the handoff and accepted by the
coordinator. It is not a defect to re-litigate; it is Batch 34's scope.

---

## 2 · Batch 34 — finish `BP-74`

| # | Item | Model | Source |
|---|---|---|---|
| 1 | 🟠 **Sink cases** for both collapse commands in `BlueprintCommandSink` | 🟠 Sonnet under review | Batch 33 handoff **§3** |
| 2 | 🔴 **One** undo entry for the whole gesture | 🔴 Opus | §3 |
| 3 | 🟢 **Context menu** via `CommandCatalog` + `_editorCommands?.Invoke(...)` | 🟢 Sonnet | §4 + **§4a** |
| 4 | 🟢 **Refusal on invoke** through `IEditorIndicators.Notify` | 🟢 Sonnet | §4 |
| 5 | 🔴 **BP-221** afterwards, if the batch has room | 🔴 Opus | §5 below |

### The three traps, restated so they are not re-derived

| ⛔ | |
|---|---|
| **`default:` reports success** | `BlueprintCommandSink:218-220` — *"unknown commands are silently accepted (forward-compat)"* → `new GraphCommandResult(true, null)`. ⭐ **Write the test that would have caught this** — a dispatched collapse asserting the graph actually changed |
| **Undo must be ONE entry** | `view.Execute(fwd, inv, "label")` (BP-60 precedent, see *"Add Return"* in `DrawContextMenu`). ⚠ Collapse creates a graph, moves N nodes and re-ties many links. **Test: collapse → undo → host structurally identical AND the created graph gone.** A half-undo leaving an orphan macro graph is the defect to watch |
| **Do NOT grey the menu items out** | Q26-B2: *offer whenever there is a selection; refuse on invoke, naming the offending nodes.* ⚠ `CanvasRenderer`/`CanvasCommands` live in **`NodeEditor.UI`**, shared with the BTree and HSM editors — **`BP-76` is that exact mistake sitting in the file you are about to edit** (`:740-753` matches on `node.Title == "ScaleBy"` and on kind ids that never occur, so both items render permanently greyed). Go through the `CommandCatalog` seam, as `Paste` (`editor.paste`) does, and register handlers **host-side** |

⭐ **The UX ruling and the architecture agree**: because the item is always offered and refuses on
invoke, the shared component needs **no knowledge of blueprint legality** — enablement reduces to
*"is anything selected"*, which is kind-agnostic and legitimate in shared UI.

### What already exists and must be *used*, not rebuilt

| Piece | Where |
|---|---|
| `CollapseAnalysis.Analyse(host, selection, target, macrosById)` → `CollapsePlan` **or** a refusal | `.Compiler/Compiler/Transform/CollapseSelection.cs` |
| `CollapseEmitter` → `CollapseEdit(Extracted, CallNode, RewrittenHost)` | `.Compiler/Compiler/Transform/CollapseEmitter.cs` |
| Refusal reasons | `BoundaryNodeSelected` · `CyclicBoundary` · `FunctionLatent` · `FunctionMultipleExits` · `FunctionMultipleEntries` · `EmptySelection` |
| Notification surface | `IEditorIndicators.Notify(EditorNotification)` → `ToastQueue.Enqueue`, wired at `BlueprintDocumentFactory:346`. ⛔ **Do not invent a second path** |

---

## 3 · What Batches 29–33 built — the map

⭐ **All merged.** Do not rebuild any of it; grep before you assume something is missing.

| Batch | Landed |
|---|---|
| **29** | `BP-80` macro surface — `ExecOutDecl`, `Graph.ExecOutputs`, `MacroCallNode`, **all four projection halves**, `BP1668` · warning triage (`BP-217`/`BP-218`/`BP-219`) · `BP-131` `Return.Success` |
| **30** | ⭐ `Stage2_5_ExpandMacros` + `GraphFragmentCloner` + four Stage-2 rails (`BP1660`–`BP1663`), `BP1665`/`BP1667`, `[JsonIgnore] Node.OriginNodeId` |
| **31** | ⭐ the macro payoff **executed** (latent body in a macro, real Roslyn, ticked across frames) · `BP-83` debug provenance (`DebugMapEntry.OriginNodeId/OriginGraphId`, schema `1.0`→`1.1`) · `BP-220` · `BP-111` |
| **32** | **Q26-A3 N exec-ins** — `ExecInDecl`, `Graph.ExecInputs`, indexed splice rule 1, `BP1666` |
| **33** | ⭐ collapse's **headless core** + the **round-trip property** (`CanonicalGraphShape`) · `MacroLatency` shared predicate · `BP-221`/`BP-222` opened |

### The files that matter

```
Hrot.Blueprints.Compiler/Compiler/
  Stages/Stage2_5_ExpandMacros.cs    fixpoint (MaxRounds=16 → BP1665), five splice rules
  Stages/V_MacroCallRules.cs         BP1660 BP1661 BP1663 BP1666 BP1662
  Transform/GraphFragmentCloner.cs   ⭐ NodeMap+PinMap are the deliverable, not the nodes
  Transform/CollapseSelection.cs     boundary analysis — pure function, selection → plan | refusal
  Transform/CollapseEmitter.cs       the two emitters (Macro / Function)
  Transform/CanonicalGraphShape.cs   Describe / AreEquivalent — id-free structural equality
  Transform/MacroLatency.cs          the ONE latent predicate (BP1661 + collapse share it)
```

### Design decisions you must not re-derive

| ⭐ | |
|---|---|
| **Q26-A3 supersedes Q25-D3** | a macro has **N exec-ins**, not one |
| **Structural purity** | a producer is impure **iff it carries exec pins** (`BP1663`/`BP1666`) |
| **Denormalised mirror** | `Pin.LinkedToIds` mirrors the link list — **rebuild it wholesale**, never patch per-rewire |
| **Four projection halves** | editor `NodePinSchema` ⇄ compiler `Stage0_Rehydrate`, for Entry **and** Return |
| **Dedup keys differ per set** | entries/exits by *interior pin* · inputs by *outside producer pin* · outputs by *interior producer pin*. Getting this wrong silently merges boundary pins |
| **`.Succeeded` never invokes Roslyn** | only the real generator path proves a blueprint compiles |

---

## 4 · ⚠ Environment — this container, every time

| | |
|---|---|
| ⭐ **`dotnet` is not on `PATH`** | fresh cloud VM. `export PATH="$HOME/.dotnet:$PATH"` **in every Bash call**. If missing entirely: `bash scripts/cloud-bootstrap.sh` |
| ⭐ **The two NodeEdit gates take NO `--no-build`** | those projects are **not in `IOS-IG-SimHost.sln`** ⇒ under `--no-build` the runner prints **no output at all**, which reads as *"nothing to report"*. Trap #5 in the gate script itself |
| ⭐ **Warning counts need `-t:Rebuild`** | an incremental build once showed 69→30 and it was pure artefact. Also `sort -u` — **MSBuild prints every warning twice** |
| **Solution** | `IOS-IG-SimHost.sln` (⚠ *not* `Hrot.sln`) |
| **`ClusterRunner.Integration.Tests`** | ⚠ **46/150 red on a clean tree in this container** (`Fatal error. Failed to create RW mapping for RX memory`). Pre-existing, **not a gate** — establish the baseline before blaming your diff |
| **`BP-111`** | 7 host-timing tests are filtered out of the default run (`Category=HostTimingSensitive`) ⇒ **3178 of 3188** |

### The eight gates — baseline at `a8deb89`, coordinator-run

| | |
|---|---|
| Solution build | **0 errors · 69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3178** / 0 failed / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |
| `python3 scripts/tracker-counts.py --check` | clean — **63 open · 94 done** |

Commands: **[RESUME_START_HERE.md](RESUME_START_HERE.md) §3.** Copy them; do not retype from memory.

---

## 5 · Open findings you already own

| ID | |
|---|---|
| **BP-74** | ⏭ **open, partial** — the headless core landed; sink + undo + menu are Batch 34 (§2) |
| **BP-221** | 🔴 `AiPrimitiveEmitter` has **no `Func_*` helper loop** while `StatementEmitter` emits the call ⇒ `CS0103`. Found by collapse-to-Function. **Pre-existing**, not caused by collapse |
| **BP-222** | `CS0815` — a zero-output function call. ⭐ **Deliberately filed unattributed**; do not guess a cause into the row |
| **BP-80** | ⏭ row stays **open** for the two *visual* gestures (palette drag, `BP-77`'s *"Macros +"*) — the only part needing the user's eyes |
| **BP-82** | ⏭ two library rails |
| **BP1664** | ⛔ **RESERVED AND UNBUILDABLE — do not attempt it.** `Graph` has no `LocalVariables` field (**BP-57**), so a macro cannot declare a local |
| **BP-76 / BP-77** | ⏭ explicitly **out of scope** for collapse — it gives macro creation a second, better entry point but closes neither row |

---

## 6 · Process lessons — paid for, do not re-learn

| ⚠ | |
|---|---|
| ⭐ **Revert-goes-red is never delegated** | Opus writes every revert patch and runs it. It is the only evidence a test actually tests something |
| ⭐ **Guard every revert with `timeout`** | a revert patch that inserted `k = 0;` inside a `for` loop produced an infinite loop and burned a 10-minute timeout. `timeout 300` on the build, `timeout 400` on the test run. Keep a `.bak` and restore from it |
| **Avoid constant conditions in revert patches** | `CS0162` *"unreachable code detected"* is an **error** here. Use a non-constant equivalent — `producer.Pins.Count > 100000`, an inverted comparison, a swapped emitter branch |
| ⭐ **Gate commits on the tree, not on an agent's report** | a Sonnet subagent once stalled ~19 minutes having written **no files** while reporting progress. Diff before you believe |
| **Sub-agents share ONE working tree** | builds must be **sequential**. Two parallel `dotnet build`s corrupt each other's obj/ |
| ⭐ **Establish the baseline BEFORE the change** | the ungated consumers are red on a clean tree; without a before-measurement every one of them looks like your regression |
| **Rules 4 + 7 together** | rule 7 catches what landed **before** your run, rule 4 what landed **during** it. Three ID collisions came from having neither |
| **Rule 5** | ⭐ **state every id and diagnostic code you allocated** in your report, so a collision is caught at merge, not three batches later |
| **Rule 6** | the tracker and detail docs are **yours** for the batch's duration; the coordinator records findings in prose, not rows |
| **Report what you did NOT do, first and loudly** | Batch 33's partial delivery was accepted because the boundary was stated in the commit subject, the body **and** the tracker row |
