# Architect question #25 — Macros

> **Status:** awaiting architect answers. Raised 2026-08-06 after the functions audit
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

A **function** compiles to a plain `static` C# method (`Func_X`). Latent execution needs the
`BlueprintLatentCursor` living in the blueprint's `State` struct and a resume-block state machine, and
that machinery exists **only** for the top-level graph. So a function structurally *cannot* contain a
`Delay` or a `WaitForChannel` — the validator is not being conservative, it is describing the emit.

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

> _To be filled in from the architect round._

| Sub-question | Answer | Notes |
|---|---|---|
| **Q25-A** — what is a macro | | |
| **Q25-B** — where expansion happens | | |
| **Q25-C** — scope and sharing | | |
| **Q25-D** — multiple exec pins | | |
| **Q25-E** — guard rails | | |
