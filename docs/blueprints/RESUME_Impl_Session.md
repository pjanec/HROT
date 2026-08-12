# RESUME — implementation session · **Batch 37 delivered — `BP-57`'s compiler half**

> **Written immediately before a context compaction. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-11**, at `5e347f6`.
>
> ⭐ **Batches 29–34 are delivered; 29–33 are coordinator-verified and merged, 34 is pushed.**
> Macros work end to end — authored, called, expanded, compiled through real Roslyn, ticked across
> frames, and debuggable. ⭐ **`BP-74` is CLOSED**: collapse is reachable from the canvas, is one
> undo entry, refuses on invoke with a message naming the offending nodes, and both targets are
> proven by execution — the Macro path by the round-trip property plus a latent run, the Function
> path (which has **no inverse**) by compiling through real Roslyn and asserting the value.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch — PUSH HERE** | ⭐ **`claude/hrot-implementation-j1jvin`** |
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`** · at `5e347f6` |
| **HEAD** | Batch 37 pushed on top of `cf26c24`; **awaiting coordinator verification** |
| **Counts** | **57 open · 105 done** — ⚠ *derive them, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **New finding IDs** | **BP-228+** (`BP-223` B34; `BP-224`/`BP-225` B35; `BP-226`/`BP-227` B37) · diagnostics **BP1670+** · diagnostics **BP1669+** (⚠ `BP1664` is *reserved and unbuildable* — see §5) |

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

## 1 · ✅ `BP-74` is closed — what Batch 34 added

| | |
|---|---|
| **Sink** | `BlueprintCommandSink` cases for `GraphCommand.CollapseToFunction`/`CollapseToMacro`. ⭐ A refusal is a **failed** result carrying the reason — the `default:` arm's silent `(true, null)` is gone. The regression test asserts on the **graph**, because asserting `Success` alone would have been green on the defect |
| **Undo** | **One** entry, via a `BlueprintEditCommand` forward/inverse pair built once in `BlueprintCollapse.Prepare`. ⭐ Restores **identity**, not shape: the emitter clones rather than moves, so the inverse puts the original node objects back with their original pin GUIDs |
| **Menu** | `CanvasRenderer` items dispatched through `CommandCatalog` + `_editorCommands`. ⛔ **Never greyed for illegality** (Q26-B2); hidden outright when the host registered no handler, so shared UI never renders a dead blueprint item |
| **`BP-221`** | Was **two** holes: the missing `Func_*` loop **and** an Instance-shaped context-arg list passed into an AiPrimitive `TickCore`. Five `CS0103`s, not one |
| **`BP-222`** | The call-site emitter assigning a `void` helper. Same class as BP-221 — declaration and call site deciding independently; now one shared `LibraryEmitter.HelperReturnType` |
| **`BP-223`** ⭐ | **New.** `IEditorIndicators.Notify` enqueued into a `ToastQueue` nothing drained — every notification the editor raised since BP-24 was discarded. The handoff's claim that the surface was "already wired" was wrong against the code. Fixed with `NotificationOverlay` on the canvas `AfterDraw` hook |

⚠ **Still open elsewhere:** `BP-77`'s *"Macros +"* button and `BP-76`'s own kind-id gating. Collapse
gives macro creation a second entry point but closes neither row.

---

## 2 · What is next

⛔ **No handoff for Batch 35 exists yet.** Re-sync from the coordinator branch and read whatever
`docs/blueprints/HANDOFF_Batch35_*.md` says. If you are picking work up without one, the open rows
with the most leverage are in §5.

---

## 3 · What Batches 29–37 built — the map

⭐ **All merged.** Do not rebuild any of it; grep before you assume something is missing.

| Batch | Landed |
|---|---|
| **29** | `BP-80` macro surface — `ExecOutDecl`, `Graph.ExecOutputs`, `MacroCallNode`, **all four projection halves**, `BP1668` · warning triage (`BP-217`/`BP-218`/`BP-219`) · `BP-131` `Return.Success` |
| **30** | ⭐ `Stage2_5_ExpandMacros` + `GraphFragmentCloner` + four Stage-2 rails (`BP1660`–`BP1663`), `BP1665`/`BP1667`, `[JsonIgnore] Node.OriginNodeId` |
| **31** | ⭐ the macro payoff **executed** (latent body in a macro, real Roslyn, ticked across frames) · `BP-83` debug provenance (`DebugMapEntry.OriginNodeId/OriginGraphId`, schema `1.0`→`1.1`) · `BP-220` · `BP-111` |
| **32** | **Q26-A3 N exec-ins** — `ExecInDecl`, `Graph.ExecInputs`, indexed splice rule 1, `BP1666` |
| **33** | ⭐ collapse's **headless core** + the **round-trip property** (`CanonicalGraphShape`) · `MacroLatency` shared predicate · `BP-221`/`BP-222` opened |
| **37** | ⭐ **`BP-57` compiler half** — locals as plain C# locals, per-graph index space, id-only resolution · `BP1664` finally built, `BP1669` allocated · `BP-226`/`BP-227` filed. ⛔ **No authoring UI — Batch 38** |
| **36** | ⭐ **`BP-76`/`BP-82` closed — the macro programme is done.** `Expand Node` (the greyed gate was hiding a corrupting path), the splice extracted to a public `MacroExpander`, `Go to Definition`, a macro library no longer "exposes nothing" |
| **35** | ⭐ **`BP-75`/`BP-77`/`BP-80` closed** — a macro is authorable by hand: create, list, declare N entries/exits, drag from the palette · `BP-224`/`BP-225` found and fixed |
| **34** | ⭐ **`BP-74` closed** — sink cases, one undo entry, the menu · `BP-221`/`BP-222` fixed ⇒ the **Function-path Roslyn proof** Batch 33 could not write · `BP-223` found and fixed |

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
| ~~**BP-74**~~ | ✅ closed Batch 34 |
| ~~**BP-221** / **BP-222**~~ | ✅ fixed Batch 34 |
| ~~**BP-223**~~ | ✅ found and fixed Batch 34 |
| **BP-80** | ⏭ row stays **open** for the two *visual* gestures (palette drag, `BP-77`'s *"Macros +"*) — the only part needing the user's eyes |
| **BP-82** | ⏭ two library rails |
| **BP1664** | ⛔ **RESERVED AND UNBUILDABLE — do not attempt it.** `Graph` has no `LocalVariables` field (**BP-57**), so a macro cannot declare a local |
| **BP-76 / BP-77** | ⏭ explicitly **out of scope** for collapse — it gives macro creation a second, better entry point but closes neither row |

---

## 6 · Process lessons — paid for, do not re-learn

| ⚠ | |
|---|---|
| ⭐ **Revert-goes-red is never delegated** | Opus writes every revert patch and runs it. It is the only evidence a test actually tests something |
| ⭐⭐ **After restoring from `.bak`, `touch` the file** | `mv $F.bak $F` restores the **old mtime**, so MSBuild's up-to-date check skips the recompile and the **reverted binary survives**. Batch 34 lost half an hour to two tests that "failed in the full run and passed in isolation" — they were running against a DLL still carrying the last revert. ⚠ Compilation is per-project all-or-nothing, so an unrelated edit in the same project masks this and it only bites on the **last** revert of a series |
| ⭐ **Guard every revert with `timeout`** | a revert patch that inserted `k = 0;` inside a `for` loop produced an infinite loop and burned a 10-minute timeout. `timeout 300` on the build, `timeout 400` on the test run. Keep a `.bak` and restore from it |
| **Avoid constant conditions in revert patches** | `CS0162` *"unreachable code detected"* is an **error** here. Use a non-constant equivalent — `producer.Pins.Count > 100000`, an inverted comparison, a swapped emitter branch |
| ⭐ **Gate commits on the tree, not on an agent's report** | a Sonnet subagent once stalled ~19 minutes having written **no files** while reporting progress. Diff before you believe |
| **Sub-agents share ONE working tree** | builds must be **sequential**. Two parallel `dotnet build`s corrupt each other's obj/ |
| ⭐ **Establish the baseline BEFORE the change** | the ungated consumers are red on a clean tree; without a before-measurement every one of them looks like your regression |
| **Rules 4 + 7 together** | rule 7 catches what landed **before** your run, rule 4 what landed **during** it. Three ID collisions came from having neither |
| **Rule 5** | ⭐ **state every id and diagnostic code you allocated** in your report, so a collision is caught at merge, not three batches later |
| **Rule 6** | the tracker and detail docs are **yours** for the batch's duration; the coordinator records findings in prose, not rows |
| **Report what you did NOT do, first and loudly** | Batch 33's partial delivery was accepted because the boundary was stated in the commit subject, the body **and** the tracker row |
