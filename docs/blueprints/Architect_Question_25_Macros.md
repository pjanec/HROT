# Architect question #25 — Macros

> **Status: ANSWERED 2026-08-07** — self-researched round (see [Answers](#answers) for provenance).
> All five sub-questions decided: **A1 · B1 · C1-now · D3 (N ≥ 0 exec-out) · E + 2 added rails**.
> Implementation split into `BP-79`…`BP-83`. Raised 2026-08-06 after the functions audit
> ([tracker](Blueprint_Issues_Tracker.md)) found macros are **half-scaffolded**, not absent as the
> audit register had claimed.
> **Related:** [Q24](Architect_Question_24_Function_Return_Value_Wiring.md) (function return values —
> all four sub-questions decided, BP-71 + BP-73 shipped).
> **Tracked as:** `BP-78` (this design) → implementation items to be split after the answers land.

---

## Why this is being asked now

Functions became genuinely usable in batches 15–18 (create → author → wire params → wire return →
N outputs → call). The audit that followed asked *"what is still missing"* and turned up macros —
which the register had recorded as **"absent from the entire codebase; new capability"**.

**That claim was wrong**, and wrong in the way this repo keeps being wrong: the search covered
`Hrot/` and not `FDP/`. This is the **fifth and sixth** overturned "nothing exists" claim.

## Ground truth (verified against code 2026-08-06)

### What already exists

| Piece | Where | State |
|---|---|---|
| `editor.create-macro` | `NodeEditor.Core/CommandCatalog.cs:54` | id declared |
| `editor.collapse-to-macro` | `CommandCatalog.cs:58` | id declared |
| `GraphCommand.CollapseToMacro` | `NodeEditor.Core/Commands/GraphCommand.cs:100` | **real command record** |
| `Macro.Call` node kind | `NodeEditor.UI/Canvas/CanvasRenderer.cs:742,753` | referenced by the *Go to Definition* and *Expand Node* menu gates |
| **Macros section in My Blueprint** | `BlueprintMyBlueprintModel.cs:59` | **rendered, with a live "+" button** bound to `editor.create-macro` |
| Demo implementation | `NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs:453` | see the caveat below |

⚠ **The Demo implementation is a scenario prop, not a reference implementation.** `ApplyCollapseToFunction`
(`:401`, the macro twin is `:453`) hardcodes the S22 scenario's pin signature — `Base`, `Multiplier`,
`Bonus`, `Result` — as literal `AddPin` calls. It models the *gesture* (delete the selection, add a
call node at the selection centroid, add an entry to My Blueprint) but **not** the part that is
actually hard: deciding which links crossing the selection boundary become parameters and which
become returns. Do not cite it as prior art for the semantics.

### What does not exist

- **No `GraphKind.Macro`.** The enum is `{ Function, Event, Construction }`
  (`Assets/GraphTypes.cs:24`). There is no macro in the data model, the compiler, or the schema.
- **No expansion pass** anywhere in Stage 0–8.
- **Nothing registers `editor.create-macro`.** The My Blueprint **"+" button is live and does
  nothing** — the BP-60 shape, user-visible today. Tracked separately as `BP-77`; it must be
  resolved (implement or remove) regardless of how this question is answered.

---

## ⭐ The reason macros are worth building *in this codebase*

Not "Unreal has them". This one:

> **`BP1650` — a function graph invoked by `FunctionCall` must not contain latent nodes; latent
> execution is only supported in the top-level Tick/event graphs.**
> (`Stage2_Validate.cs:2150-2166`)

A **function** compiles to a plain `static` C# method (`Func_X`). So a function structurally *cannot*
contain a `Delay` or a `WaitForChannel` — the validator is not being conservative, it is describing
the emit.

> ⚠ **Correction (2026-08-07, from the answers round).** This section originally said the latent
> machinery "exists **only** for the top-level graph". **That is false.**
> `InstanceLowering.cs:16-21` applies `WaitLowering_Instance` to **every** graph in the asset that
> contains a latent op, and `Func_X` does receive `ref State s` (`InstanceEmitter.cs:283-291`), so the
> cursor is reachable from a function body. The real reason BP1650 must exist is narrower and harder:
>
> 1. `State` holds exactly **one** `Cursor` field (`InstanceEmitter.cs:109`) — one resume slot per
>    instance, not per call frame.
> 2. Suspension is expressed as an **early `return`** (`WaitLowering_Instance.cs:109`).
>
> So a suspending `Func_X` would clobber the caller's single shared cursor *and* return to a caller
> that has no way to learn it suspended. Supporting latent-in-a-function means a cursor **stack** plus
> suspend propagation through every call site — a large runtime change.
>
> The justification is unaffected in substance and stronger in force: macros avoid the problem by
> construction rather than by paying for it.

A **macro inlines into its call site**, so a latent node inside it lands in the top-level graph where
the cursor already lives.

⇒ **Macros are currently the only possible way to factor out a reusable *latent* sequence.** For
behaviour authoring — *aim → wait 0.4s → fire*, *approach → wait for arrival → report* — that is the
common case, and today it must be copy-pasted at every call site. Functions cannot ever serve it.

The secondary Unreal-parity benefit — **multiple exec inputs and outputs** — is also only expressible
by inlining: a C# method has one entry and one return.

---

## Q25-A — What *is* a macro here?

- **A1 — Unreal-faithful: a macro is an inlined exec-subgraph.** Own `GraphKind.Macro`, its own
  Input/Output boundary nodes, may declare **multiple exec in/out pins**, may contain latent nodes,
  **no local state**, **no recursion**. Expanded away before scheduling; the runtime never sees it.
- **A2 — "function that inlines".** Same authoring surface as a Function graph (one exec in, one exec
  out, N data in/out), but expanded rather than called. Buys latent-in-a-reusable-graph and nothing
  else.
- **A3 — pure-data macro only.** No exec pins at all; a reusable data expression subgraph.
  Cheapest by far, and sidesteps every exec-flow question.

**Claude's lean: A1**, but see Q25-D — A1's multi-exec pins are what make it worth a new `GraphKind`
rather than a flag on `Graph`. A2 is a strictly smaller A1 and can ship first if the answer to D is
"one exec pair for now".
**Reuse vs build:** A1 and A2 reuse the entire Function authoring surface (signature window, My
Blueprint section, canvas, `ReturnNode`); A1 adds boundary-node pin projection. A3 reuses the most but
does not solve the latent case, which is the whole justification above.

## Q25-B — Where does expansion happen?

- **B1 — a new stage between Validate and Normalize** (`Stage2.5_ExpandMacros`). Clean seam: Stage 2
  has already checked the asset, and everything downstream (Normalize/TypeResolve/Schedule/Lower)
  sees an asset with no macros in it and needs **zero** changes.
- **B2 — inside `Stage3_Normalize`.** No new stage in the pipeline; risks entangling expansion with
  normalisation rules that assume a settled node set.
- **B3 — at Stage 0 (rehydrate).** Earliest, but then Stage 2 validates *expanded* graphs and every
  diagnostic points at synthesized nodes the designer never placed — a debuggability disaster.

**Claude's lean: B1.** The decisive argument is diagnostics, not tidiness: with B1, Stage 2 errors
still name the node **as the designer placed it**, inside the macro. Every stage after expansion must
carry an origin annotation regardless (`IrDebugAnnotation.OriginNodeId` already exists and is
consulted by `CSharpEmitter.EmitNodeStart` — that is the hook).
**Reuse vs build:** B1 is a new file and one call site; the pipeline is already an explicit ordered
list of stages. No downstream stage changes in any option.

## Q25-C — Scope and sharing

- **C1 — asset-local only.** A macro lives in the blueprint that declares it (`GraphKind.Macro` in
  `Graphs`). Simplest; mirrors how custom events and function graphs work today.
- **C2 — macro libraries (Unreal's model).** A separate asset kind holding only macros, referenced by
  other blueprints. Matches `CallablePeers`/`BlueprintPeerSource`, which already solve cross-asset
  discovery for functions (BP-66).
- **C3 — C1 now, C2 as a follow-up.**

**Claude's lean: C3.** C2's discovery half is genuinely mostly built (peer source, signature parser,
`DeclaredCallablePeers`), but macro expansion across assets forces a decision about **when** the
referenced asset is read — the compiler currently takes sibling *signatures*, not sibling *bodies*,
and inlining needs the body. That is a real new dependency in the build graph and deserves its own
round rather than riding along.
**Reuse vs build:** C1 is pure reuse. C2 reuses discovery but adds a body-level cross-asset
dependency (and cache invalidation: editing a macro must rebuild every consumer).

## Q25-D — Multiple exec pins?

The question that decides whether this is a new kind or a flag.

- **D1 — yes, N exec in / N exec out.** Full Unreal parity. The classic payoff is a `Gate`/`Sequence`
  style macro, or one branch of a shared decision returning through a different exec pin.
- **D2 — one exec in, one exec out.** Latent-in-a-reusable-graph still works. Much smaller: the call
  node's pin projection is the existing `FunctionGraphCallPins` shape.
- **D3 — N exec out, one exec in.** The common Unreal case (`ForEachLoop`: Loop Body + Completed)
  without the multi-entry complication.

**Claude's lean: D3.** Multi-*entry* is rare in practice and is the piece that complicates expansion
most (the inlined subgraph needs one landing point per entry, and the "which entry did we come from"
question reintroduces state). Multi-*exit* is common, cheap, and is just N continuation edges from the
expansion site. ⚠ D1 can be reached later from D3 without an asset migration; D2 → D3 is also
additive. So the sequencing risk is low whichever is picked.
**Reuse vs build:** D2 reuses `FunctionGraphCallPins` wholesale. D3 needs a boundary-node projection
that emits N exec-outs — new, but small and mirrors `EventEntryNodePins`' existing "one pin per
declared thing" loop.

## Q25-E — Guard rails

Not really optional; listed so the answers can adjust them rather than discover them.

| Rule | Why | Cost |
|---|---|---|
| **No recursion** (direct or mutual) | inlining a cycle does not terminate | cycle check over the macro call graph; a Stage 2 error |
| **Expansion depth cap** | a macro calling a macro 20 deep is a build-time bomb | counter in the expansion pass |
| **No local state / variables** | a macro has no instance to hang state on; it is textual | Stage 2 error (moot until BP-57 lands locals for functions) |
| **Origin annotation on every expanded node** | otherwise breakpoints, the watch panel and every diagnostic point at nodes the designer never placed | reuses `IrDebugAnnotation.OriginNodeId` |

⚠ **The origin-annotation rule is the one that will bite if it is skipped.** This programme has spent
eighteen batches making failures attributable; an expansion pass that loses provenance would
reintroduce exactly the "error with no explicable source" shape that BP-69, BP-71 and BP-73 each ended
in.

---

## What this unblocks

| Item | How |
|---|---|
| Reusable **latent** sequences | the only mechanism that can express one (see the ⭐ section) |
| `BP-77` | the dead "Macros +" button gets a real handler instead of being removed |
| `BP-74` (collapse-to-function) | collapse-to-**macro** is the same gesture; both land together |
| Multi-exec-out reusable subgraphs | not expressible by any current construct |

## Not in scope for this question

Function **local variables** (`BP-57`, already tracked) · collapse-to-function (`BP-74`, a functions
item that needs no macro decision) · the `Go to Definition` gate bug (`BP-76`) · macro **libraries** if
Q25-C is answered C1 or C3.

---

## Answers

> **Provenance: self-researched round, 2026-08-07.** Decided by Claude against code, **not** run
> through the NotebookLM architect. Same footing as **Q23** and **Q24**, both of which were settled
> this way and recorded as such. If the architect later disagrees on any row, the answer is the
> architect's — nothing below is load-bearing enough to be expensive to revisit except **Q25-B**,
> which fixes the pipeline seam.

### Verification log

Five claims were put up for checking before answering; the round was explicitly *not* allowed to
rubber-stamp the leans, and a sixth and seventh surfaced while checking.
Outcome: **two claims false, one materially understated, four confirmed.** Both falsehoods and the
understatement moved the design — none of them was cosmetic, and every one made the feature *cheaper*
or its failure mode *louder*.

| # | Claim under test | Verdict | Evidence |
|---|---|---|---|
| 1 | ⭐ An inlined latent node really works in the top-level graph | ✅ **confirmed** | `WaitLowering_Instance.Apply` keys off `graph.Blocks.Where(b => b.Terminator is IrTerm_Suspend)` and handles **N** suspend points via a chained dispatch (`n = suspendBlocks.Count`; chain blocks for `n > 1`). Nothing in it cares where the node was authored. An inlined latent node is just another suspend block. |
| 2 | The latent machinery exists only for the top-level graph | ❌ **FALSE** | `InstanceLowering.cs:16-21` lowers **every** graph with a latent op. See the ⭐ correction above — the real blocker is the single shared `Cursor` (`InstanceEmitter.cs:109`) plus suspend-as-`return` (`WaitLowering_Instance.cs:109`). |
| 3 | The pipeline is an explicit ordered stage list; inserting a stage needs no downstream change | ✅ **confirmed** | `BlueprintCompiler.Compile` lines 54-77 are a literal statement sequence. `Stage3_Normalize.Run` already returns a **new** asset, so an expansion stage that rewrites the node set has a precedent to copy. |
| 4 | `IrDebugAnnotation.OriginNodeId` is genuinely consulted | ✅ **confirmed** | `CSharpEmitter.cs:45,53`: `debug?.NodeId ?? debug?.OriginNodeId`, feeding `RecordNodeStart`/`RecordNodeEnd`. It is a **fallback**, not the primary — which matters, see Q25-E. |
| 5 | The compiler sees sibling *signatures*, not sibling *bodies* | ✅ **confirmed, and it is decisive for C** | `BlueprintSignature` carries `ExportedFunctions` as name + param **types** only — no `Nodes`, no `Links`. Cross-asset inlining needs bodies. |
| 6 | Stage5's single-exec-successor limit constrains macro exec-out count | ⚠ **materially understated — it does not constrain it at all** | With expansion before Stage5, the call node is **gone** by scheduling time; N exec-outs are N ordinary host-graph links. `GetSingleExecSuccessor` (`:3628`) and BP1412 (`:3624`) never see a macro. Separately, `ComputeMergePoints` (`:4269`) already handles exec in-degree ≥ 2 generically. **Multi-exec is far cheaper than the question assumed.** |
| 7 | *(raised during the round)* The expansion pass must write its own node cloning | ❌ **FALSE — it is already built** | `BlueprintClipboard.Rehydrate:129-184` (shipped by **BP-23a**) already does the JSON deep-copy, fresh **node and pin** GUIDs, link remap, and the denormalised `Pin.LinkedToIds` mirror. Deltas: boundary links must be **rewired** where `Rehydrate:171-174` drops them, and it sits in `.Editor` while Stage 2.5 sits in `.Compiler` (dependency runs Editor → Compiler, so move the remap **down**). ⚠ Caught only because a link-validator false positive forced a second look at BP-23a — the seventh "nothing exists" overturn in this programme. |

### Decisions

| Sub-question | Answer | Notes |
|---|---|---|
| **Q25-A** — what is a macro | **A1** — Unreal-faithful inlined exec-subgraph, own `GraphKind.Macro` | Lean confirmed, but on a *different* argument than the one offered. The question said A1 earns its own `GraphKind` because of multi-exec pins. The real reason is that the **alternative is unsafe**: `Stage5_Schedule.cs:4311-4314` maps `GraphKind → IrGraphKind` with a `_ => IrGraphKind.Function` catch-all, and `InstanceEmitter.cs:81-82` picks *"the first `Function` graph"* as the Tick graph when none is named `Tick`. A macro modelled as a flag on a `Function` graph could therefore **silently become the tick graph**. A distinct enum member makes every existing `== GraphKind.Function` filter (40 sites in the compiler) fail correctly and by default. `GraphKind` serialises as a **string** (`BlueprintJsonServices.cs:26` registers `JsonStringEnumConverter`), so appending a member is additive on disk. |
| **Q25-B** — where expansion happens | **B1** — new `Stage2_5_ExpandMacros` between Validate and Normalize | Lean confirmed, with a second argument the question did not have: `BlueprintCompiler.Validate()` (`:108-123`) runs **Stage 2 alone** as the editor's live validator. Under B1 the editor validates macros exactly **as authored**; under B3 it would validate expanded graphs and red-underline nodes the designer never placed. ⚠ **Hard requirement:** the `_ => IrGraphKind.Function` catch-all at `Stage5:4314` must become an explicit diagnostic. As it stands, an expansion-pass bug that leaves a macro behind is a **silent miscompile into a function** — Trap #5, and it would re-create the BP1650 breakage with no error. |
| **Q25-C** — scope and sharing | **C1 now** (asset-local), C2 deferred to its own round — i.e. the **C3** sequencing | Lean confirmed and the gate is now concrete rather than suspected: signatures carry no bodies (verification #5), so cross-asset macros require the compiler to read sibling **asset files**, not sibling signature files. That is a new build-graph dependency plus cache invalidation (editing a macro must rebuild every consumer). Not a rider on this question. |
| **Q25-D** — multiple exec pins | **D3** — one exec-in, **N ≥ 0** exec-out | Lean confirmed, cost revised **down** (verification #6). Extended from the question's D3 per the round-out rule: N ≥ 0 rather than N ≥ 1 makes a zero-exec macro fall out for free, which *is* option **A3** (pure-data macro) as a subset of A1 rather than an alternative to it. Multi-**entry** (D1) stays out: `Stage5:204-208` states the merge machinery is deliberately **not** applied at Sequence-branch or When-arm roots, so a body entered from two places is not uniformly safe. D3 → D1 remains additive with no asset migration. |
| **Q25-E** — guard rails | All four kept; **two added**, one dropped as already-handled | See the table below. |

### Q25-E — final guard rails

| Rule | Status | Detail |
|---|---|---|
| **No recursion** (direct or mutual) | keep — **reuse, don't build** | BP1654 (`Stage2_Validate.cs:2173+`) is already a three-colour DFS over the FunctionCall graph. The macro check is the same algorithm over macro-call edges. |
| **Expansion depth cap** | keep | Counter in the expansion pass. |
| **No macro-local state** | keep | Moot until BP-57 lands locals for functions, but must be a Stage 2 error from day one, not a later addition. |
| **Origin annotation on every expanded node** | keep, **sharpened** | `OriginNodeId` is a *fallback* to `NodeId` (`CSharpEmitter.cs:45`). `DebugMapBuilder.RecordNodeStart` (`:99`) ignores a re-open while a node id is already open, and `RecordNodeEnd` closes it — so one designer node expanded at two call sites yields **two** `DebugMapEntry` rows, same `NodeId`, different line ranges. Line→node stays 1:1; **node→line becomes one-to-many**. A breakpoint on a macro-internal node must therefore arm at **every** expansion site. Concrete and testable, which the original phrasing was not. |
| ➕ **Reject `GraphKind.Macro` past Stage 2.5** | **added** | Replace `Stage5:4314`'s `_ => IrGraphKind.Function` catch-all with a diagnostic. Without it the whole feature's failure mode is silent. This is the single most important item in the table. |
| ➕ **Macro graphs excluded from tick-graph selection** | **added** | `InstanceEmitter.cs:81-82`'s `?? FirstOrDefault(Kind == Function)` fallback. Satisfied for free by A1's separate enum member; needs an explicit test so a later refactor cannot quietly undo it. |
| ~~Resume-point renumbering across hot reload~~ | **dropped — already handled** | Considered: `IrOp_WriteCursorResumeAt(k + 1)` numbers resume points by **block-list position**, so adding a macro call renumbers every downstream resume point. But a structure change is a **hard** reload, and `LatentCursorReloadTests` documents *"hard reload resets cursor to ResumeAt=0"*. No new guard rail needed. |
