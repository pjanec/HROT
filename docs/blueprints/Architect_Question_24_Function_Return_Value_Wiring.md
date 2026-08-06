# Architect question #24 — A Function graph's return value cannot be wired

> **Scope.** Blueprint editor + compiler contract. Tracker item **BP-71** (🔴) —
> *"The Return node's value pin is an output on both projections, so nothing can be wired into it."*
> Detail: `Blueprint_Issues_Detail.md#BP-71`.
> Found while auditing what **BP-24** (graph create + switching, Batch 15) actually unblocked.
> This changes a pin-direction contract that the compiler, the editor and a test all encode
> independently, so per the engine-rules gate it gets an architect pass before any code.
>
> **Status: A/B/C decided by the user 2026-08-06 (A1 + B1 + C3) — BP-71 is unblocked and buildable.
> Q24-D (one output or many) is still open**; it does not block the A/B/C work, which is
> single-output-only either way.

**Symptom.** BP-24 made Function graphs creatable and reachable. `GraphSignatureWindow` can give one
an **Output**. `FunctionCallNodeDrawer` can target it and `FunctionGraphCallPins` projects a typed
return pin at the call site. Every piece of *calling* a function that returns a value works —
and the function itself can never produce one, because the designer cannot wire anything into the
`Return` node.

---

## Ground truth (verified against code 2026-08-06)

### The contradiction, in three files

| Where | What it says |
|---|---|
| `Host/NodePinSchema.cs:328` `ReturnNodePins` | value pin is `MakeData(output.Name, "Out", typeId)` |
| `Stage0_Rehydrate.EnrichReturnPins` | same — `MakePin(output.Name, "Out", …)` |
| `Stage5_Schedule.BuildReturnTerminator:1896` | finds `!IsExec && Direction == "Out"`, then calls `ResolveDataPin(rn.Id, outPin.Id)` — which searches for a link with **`ToNodeId == rn.Id`** |

So the pin is *declared* an output and *consumed* as an input.

### Why that is unwirable on the canvas

| Step | Code |
|---|---|
| Direction maps straight through to NodeEdit | `BlueprintPinModel.cs:86` — `pin.Direction == "In" ? Input : Output` |
| Same-direction links are rejected | `BlueprintLinkValidator.Validate` — *"Cannot connect pins of the same direction (output → input required)"* |

A designer drags from `Abs.result` (Output) to `Return.result` (Output) and the link is refused.
There is no alternative gesture: the pin has no inline default-value editor either, because
`BlueprintPinModel` synthesises `Default` only for `Direction == "In"` data pins.

### `Return` is the *only* node in the compiler with this shape

`ResolveAllDataInputs` — the universal convention — is
`node.Pins.Where(p => !p.IsExec && p.Direction == "In")` (`Stage5_Schedule.cs:3233`).
Of the ~20 `ResolveDataPin` call sites, **`BuildReturnTerminator:1898` is the sole one that passes a
`Direction == "Out"` pin.** This is a one-off anomaly, not a house style.

### The contract is deliberate, and locked by a test

Both the compiler test and the editor test state it in prose:

- `BATCH03A_FunctionGraphCallTests.cs:139` — *"'Out' direction: this is the return-value slot on the
  ReturnNode. Stage5.BuildReturnTerminator looks for `Direction=="Out"` here, then calls
  `ResolveDataPin(rn.Id, outPin.Id)` which follows a link arriving at `ToNodeId=addReturnId`."*
- `NodePinSchemaEnrichmentTests.cs:750` — *"The value pin MUST have `Direction=="Out"` (compiler
  contract …)."*

So the data model was chosen on purpose and the **canvas consequence was simply never considered**.
Nothing is "broken" in the compiler's own terms; the two halves have never been used together.

### What the failure looks like today

Only for **Instance**-dispatch assets — `BuildReturnTerminator` returns `IrTerm_ReturnStatus(rn.Status)`
early for `Library` / `AiPrimitive` and ignores the data pin entirely.

1. `ResolveDataPin` finds no link ⇒ **BP4001 *warning*** and a dummy `IrValue`.
2. Temps are declared only at their assigning statement (`var __t{idx} = …`), and a dummy has none.
3. `TerminatorEmitter` writes `return __t7;` ⇒ **CS0103 from Roslyn, with no BP diagnostic
   explaining it.** Same unattributed-Roslyn-error shape as **BP-69**.

### Blast radius is near-zero — the path is entirely unexercised

Scanned all **92** `*.bp.json` in the repo:

| Finding | Count |
|---|---|
| Graphs declaring `Outputs` | **2** (`SquadState.bp.json` `GetThreatLevel`, in two copies) |
| `Return` nodes with **authored** pins | **0** — every one is `"Pins": []`, reprojected on load |
| Function graphs that actually **wire** a return value | **0** |

`GetThreatLevel` declares output `ThreatLevel`, contains `EventEntry` + `GetVariable` + `Return`, and
has **`"Links": []`** — it does not wire its own return either. So no on-disk asset pins the current
direction, and a change only touches the test builders and the two tests quoted above.

> ⚠ Note for whoever builds this: `BlueprintCommandSink.ApplyPinIds` orders baked pins
> **inputs-then-outputs** to match the canvas's `entry.Inputs`/`entry.Outputs` walk. Flipping the
> direction moves the value pin to the front of a freshly-placed `Return` node's pin list. That is
> self-consistent (both sides regenerate from the same projection) but it *is* the kind of ordinal
> coupling that BP-65 turned into a silent bug.

---

## Q24-A — Which side moves?

- **A1 — flip the projection to `Direction == "In"`; widen `BuildReturnTerminator` to accept either.**
  `Return` becomes an ordinary data consumer and every generic mechanism applies for free: the link
  validator passes, `ResolveAllDataInputs`-shaped reasoning holds, and the pin gains an **inline
  default-value editor** (so `return 0;` needs no wired literal). *Reuse:* everything.
  *Build:* two projection lines, one predicate widening, update the two contract tests.
  *Risk:* any hand-authored JSON carrying `"Out"` — mitigated by accepting both (Q24-B).
- **A2 — keep `"Out"`; special-case the canvas.** Render the pin on the left and exempt `ReturnNode`
  in `BlueprintLinkValidator`. *Reuse:* the compiler untouched. *Cost:* a per-node-kind exception in
  a rule that is currently universal, and the pin still gets no default editor. Every future reader
  of `BlueprintLinkValidator` has to learn the exception.
- **A3 — leave `ReturnNode` exec-only; add a `SetReturnValue` node** that writes the graph output.
  *Reuse:* the ordinary data-in convention, no contract change. *Cost:* a new node kind, a new
  palette entry, a new Stage5 case, and two ways to end a function — plus "which one wins" rules.
- **A4 — do nothing; declare functions value-less** and route results through variables.
  *Cost:* `Graph.Outputs`, `FunctionGraphCallPins`, `IrOp_GraphCall`'s return type and
  `InstanceEmitter.EmitInstanceFunctionMethod` all become dead weight; the signature window keeps
  offering an Output that cannot work.

**Claude's lean: A1.** It is the smallest diff, it deletes a special case rather than adding one, and
it matches Unreal — whose Return Node is precisely an *input*-collecting node, one input pin per
declared output. A2 buys nothing the compiler needs and costs a permanent exception in the one rule
that is currently exception-free. A3 is defensible but pays a new node kind for a two-line problem.

## Q24-B — What about assets that already carry the old direction?

- **B1 — accept both directions in `BuildReturnTerminator`, permanently.** One `||`. Nothing to
  migrate, nothing can break.
- **B2 — accept only `"In"`, migrate the fixtures.** Cleanest contract; 0 shipped assets to migrate,
  but any external/hand-authored JSON breaks silently (the pin is simply not found ⇒ void return).
- **B3 — accept both, and emit a BP warning on the legacy `"Out"` shape** so the old form is
  visibly deprecated rather than quietly tolerated.

**Claude's lean: B1 now, B3 only if the legacy form is expected to persist.** With zero on-disk
instances the migration cost is nil either way, so the deciding factor is which failure is worse: a
silently-void return (B2) or a permanently dual contract (B1). B1's `||` is one token and it makes
B2's silent failure impossible.

## Q24-C — Should an unwired return value be a diagnostic?

Today it is a **BP4001 warning** followed by a **CS0103** nobody can trace back.

- **C1 — Stage 2 error.** New `BPxxxx`: *"Function graph 'F' declares output 'X' but its Return node
  has no value wired."* Fails the build at the authoring layer, naming the graph and the node.
- **C2 — keep it a warning, but emit `default(T)`** instead of a dangling temp, so the C# always
  compiles.
- **C3 — both** — error at Stage 2, `default(T)` as the belt-and-braces emit path.

**Claude's lean: C3.** C1 alone is the right *authoring* answer; C2 alone repeats the BP-16 mistake
(a silent wrong value). Together, the designer gets a named error and the emitter can never produce
an unattributable Roslyn failure. ⚠ Every new `BPxxxx` needs a `[CoversDiagnosticCode]` test or
`V_AllValidatorsCoverageTests` fails the build.

## Q24-D — One output, or many?

`Graph.Outputs` is a `List<ParameterDecl>`, but **only `[0]` is ever read** — 5 sites
(`InstanceEmitter:272`, `LibraryEmitter:33`, `CSharpEmitter:240`, `Stage0_Rehydrate:284/808`,
`Stage5:1480/3010`). `GraphSignatureWindow` happily lets a designer add a second one, which is then
**silently ignored**.

> The user's goal is **Unreal parity** — Unreal's Return Node carries one input pin per declared
> output. So the question is not *whether* N outputs are wanted but *when*, and what they cost.
> Costed against code 2026-08-06.

### What multi-output would actually cost

**The one invariant not to break.** `IrStatement` has exactly one `IrValue? ResultValue`
(`Ir/IrStatement.cs:5`). Making it a list would touch every statement consumer plus the
one-`PinId`-per-statement debug annotation, probe insertion and breakpoint mapping. **Avoid.**
Everything below keeps the one-result invariant and is therefore additive.

| Piece | Cost | Why |
|---|---|---|
| **Library dispatch** | **≈free** | `EmitLibraryFunctionAdapter` already writes results into an `outputs` **byte span** via `MemoryMarshal.Write` (`CSharpEmitter:267`), and already walks **N inputs** with an `__off` cursor (`:252-258`). N outputs is that same loop mirrored. ⚠ Write them **sequentially with `__off` advance**, not as one packed struct — the reader side walks sequentially, and struct padding would not match. |
| **Per-pin value resolution** | **already exists** | `_statementPinCache` maps pin→value precisely so a statement-produced value is not recomputed (`Stage5:1526` — *"re-invoking would re-run the side effect"*). One `IrOp_GraphCall` statement + one field-read statement per consumed pin, each cached. Probes/watch stay per-pin correct for free. |
| **Debug map** | **already assumes N** | `CSharpEmitter.Emit:69` loops `foreach (var field in graph.Outputs)` and registers *every* one. Only the emit/return sites hardcode `[0]`. |
| **Editor projections** | small | `EnrichReturnPins`, `ReturnNodePins`, `FunctionGraphCallPins` loop instead of taking `[0]`. All three **already loop over `Inputs`** — mirror it. |
| **Signature window** | **nothing** | already N-row CRUD (Add/Remove/Rename/Retype/Move). |
| **Instance dispatch — the carrier** | **the only real design work** | `IrOp_GraphCall.ReturnType` is a single `IrTypeRef` and the emitted C# is a plain method. N needs a carrier: a `ValueTuple` or a synthesized struct. **Precedent exists** — `StatementEmitter.TypeRefToCSharp:1591` already passes through `_ when t.FullName.StartsWith("_")`, commented *"local generated type (synthesized struct)"*. So composing a return type and threading it through `IrTypeRef.FullName` is an established move, not a new one. |
| **Return terminator** | small, and avoidable | `BuildReturnTerminator` collects N pins. It can stay **single-value** by synthesizing a carrier-construction statement just before the return (`var __t9 = (a, b); return __t9;`) — **zero terminator changes**. |

**Estimate: ~250–450 lines across compiler + editor, plus tests. `RW-M`, not `RW-H`.** No new
subsystem, no invariant broken, and strictly additive: with `Outputs.Count <= 1` the emitter keeps
producing today's bare `float`/`void`, so no golden IR, no shipped asset and no existing test moves.

**So: not luxurious.** The machinery this needs mostly exists, in the right shape, for other reasons.

### The argument for not doing it *in this item*

Demand is currently **zero**: of 92 assets, 2 declare an output and **neither wires it** — there is
no function anywhere returning even *one* value yet. Meanwhile BP-71's fix is two lines. Bundling N
turns a two-line unblock into a multi-day slice, and the two-line version is what makes Function
graphs useful at all.

### Options

- **D1 — formalise single-output permanently.** Stage 2 error on `Outputs.Count > 1`; signature
  window caps at one. Honest, but forecloses Unreal parity.
- **D2 — build N now**, inside BP-71.
- **D3 — leave it.** Today: the second output vanishes without a word.
- **D1′ — single-output *now*, N as a named follow-up.** BP-71 fixes the direction and adds a
  Stage 2 diagnostic on `Outputs.Count > 1` whose message says **"not supported yet — see BP-73"**,
  not "illegal". Kills the silent discard (the actual defect today) without foreclosing anything.

**Claude's recommendation: D1′.** D3 is a silent-data-loss shape and this programme has spent fifteen
batches removing those. D1 contradicts the stated Unreal-parity goal. D2 is the right *capability*
but the wrong *sequencing* — it blocks a two-line correctness fix behind a design round for a carrier
type, for a feature no asset can yet exercise. D1′ gets the fix out this week and leaves N a costed,
scheduled item rather than an open question. ⚠ Whichever is chosen, the `Outputs.Count > 1`
diagnostic needs a `[CoversDiagnosticCode]` test or `V_AllValidatorsCoverageTests` fails the build.

**If N is wanted in its own slice, the one question to settle first:** `ValueTuple` vs a synthesized
`_FuncOut_{Name}` struct for the Instance carrier. Tuple gives named elements free; a synth struct
matches the existing `_`-prefixed convention and is easier to name in diagnostics and the watch panel.
The Library path uses neither — it stays sequential span writes.

---

## What this unblocks

| Item | How |
|---|---|
| **BP-24's headline** | a created Function graph can finally do the one thing a function is for |
| `FunctionCallNode` graph-call mode | today its return pin can only ever carry a dummy |
| **BP-57** (per-function locals) | a function with no return value has much less need of locals |
| `SquadState.GetThreatLevel` | the one shipped asset that declares an output could be completed |

## Not in scope

Multiple return values beyond the Q24-D ruling · `out`/`ref` parameters · early-return with a value
from a nested branch (works already once the pin is wirable — `BuildReturnTerminator` runs per
`ReturnNode`) · the `Library`/`AiPrimitive` status-return path, which is a separate contract and is
not affected.

---

## Answers — **ALL FOUR DECIDED 2026-08-06 by the user**

> ⚠ **Provenance:** all four were decided by the **user directly**, not by the NotebookLM architect —
> the same delegation precedent as Q23's self-researched round. A/B/C: *"for a b c lets use your
> lean"*. D was reframed with a real cost estimate after the user noted Unreal supports multiple
> outputs, then decided: *"i would definitely like D with 'proper N', as costed, scheduled item.
> Until implemented fully, i agree with the 'not supported yet — see BP-73'."*
>
> ⚠ Per the working agreement, an approval is **not** a verification (Q22's approved D2 was the one
> step that could not work). Re-check each decision against the code before building it.

| Sub-question | Decision | Reasoning |
|---|---|---|
| **Q24-A — which side moves** | ✅ **A1 — flip both projections to `Direction == "In"`; widen `BuildReturnTerminator` to accept either.** | Removes a special case instead of adding one: `Return` is the compiler's only pin declared `"Out"` and consumed as an input (1 of ~20 `ResolveDataPin` sites, against the universal `ResolveAllDataInputs`). Matches Unreal, whose Return Node is an input-collecting node. Free bonus: the pin gains an inline default-value editor, since `BlueprintPinModel` synthesises `Default` only for `"In"` data pins — so `return 0;` no longer needs a wired literal. |
| **Q24-B — legacy direction** | ✅ **B1 — accept both directions in `BuildReturnTerminator`, permanently.** | One `\|\|`. Zero on-disk instances to migrate either way (0 of 92 assets author Return pins), so the deciding factor is which failure is worse — and B1 makes B2's silently-void return impossible. |
| **Q24-C — diagnostic** | ✅ **C3 — Stage 2 error *and* emit `default(T)`.** | Today: BP4001 *warning* → an undeclared dummy temp → `return __t7;` → **CS0103 with no BP attribution** (BP-69's shape). C1 alone is the right authoring answer; C2 alone repeats BP-16 (silent wrong value). Together the designer gets a named error and the emitter can never produce an untraceable Roslyn failure. ⚠ Needs a `[CoversDiagnosticCode]` test. |
| **Q24-D — output count** | ✅ **D1′ — single-output now; proper N-output is WANTED and is now a costed, scheduled item: [BP-73](Blueprint_Issues_Detail.md#BP-73).** The `Outputs.Count > 1` diagnostic must read **"not supported yet — see BP-73"**, never "illegal" or "unsupported". | N-output is **`RW-M`, ~250–450 lines, and additive** — the Library ABI is already span-based and N-shaped, `_statementPinCache` already does per-pin values, the debug map already loops all outputs, and `TypeRefToCSharp` already passes synthesized types through. It is **not** luxurious, and Unreal parity is the stated goal. But demand is zero today (no asset returns even one value), and bundling it would block a two-line correctness fix behind a carrier-type design round. So: ship the fix, keep the door open, build N as its own slice. |
