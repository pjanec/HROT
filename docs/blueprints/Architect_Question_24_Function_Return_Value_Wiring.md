# Architect question #24 — A Function graph's return value cannot be wired

> **Scope.** Blueprint editor + compiler contract. Tracker item **BP-71** (🔴) —
> *"The Return node's value pin is an output on both projections, so nothing can be wired into it."*
> Detail: `Blueprint_Issues_Detail.md#BP-71`.
> Found while auditing what **BP-24** (graph create + switching, Batch 15) actually unblocked.
> This changes a pin-direction contract that the compiler, the editor and a test all encode
> independently, so per the engine-rules gate it gets an architect pass before any code.

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

- **D1 — formalise single-output.** A Stage 2 error on `Outputs.Count > 1`, and the signature window
  disables **+** at one. Honest about what the compiler does.
- **D2 — support N outputs** (tuple return, or `out` parameters). Real emitter work, and the call
  site needs N return pins instead of one.
- **D3 — leave it.** Today's behaviour: the second output vanishes without a word.

**Claude's lean: D1, decided in this round even though it is not the reported bug.** D3 is a
silent-data-loss shape and the programme has spent fifteen batches removing exactly those. D2 is a
capability decision that deserves its own demand — nothing has asked for it.

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

## Answers — *pending architect round*

> Relay to NotebookLM, record the decisions in this section, **then** build. Do not start on the
> strength of Claude's leans.

| Sub-question | Decision | Reasoning |
|---|---|---|
| Q24-A — which side moves | | |
| Q24-B — legacy direction | | |
| Q24-C — diagnostic | | |
| Q24-D — output count | | |
