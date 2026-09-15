# Architect question #26 — Collapse a selection into a Function or Macro

> **Raised by the user 2026-08-11**, on seeing that the macro programme (BP-78…BP-83) delivered macros
> as a *capability* but not the gesture that makes them worth having: *select part of a graph, turn it
> into a function or a macro.*
>
> ⭐ **It is already tracked as [BP-74](Blueprint_Issues_Tracker.md), open and 🔴**, found by the
> functions audit of 2026-08-06. The user re-derived a real gap independently.
>
> 📌 **The gesture is the point of the feature.** Unreal's *Collapse to Function* / *Collapse to Macro*
> is how anyone actually creates one — nobody authors an empty macro and re-wires a graph into it by
> hand. Without collapse, BP-78…BP-83 built a destination with no road to it.

---

## Why this is being asked now

Collapse was **expensive** when BP-74 was filed and is **cheap now**, because the macro programme
happened to build every primitive it needs:

| Needed by collapse | Built | Where |
|---|---|---|
| deep-copy a node subset, fresh ids, remapped links, `LinkedToIds` mirror, **and the maps** | ✅ Batch 30 | `GraphFragmentCloner` (`.Compiler/Compiler/Transform/`) |
| a graph kind to collapse *into* | ✅ Batch 28 | `GraphKind.Macro` |
| a call node deriving its whole shape by projection | ✅ Batch 29 | `MacroCallNode` (one field, F4) |
| ⭐ **the exact inverse operation, working and executed** | ✅ Batches 30-31 | `Stage2_5_ExpandMacros` |
| the latent-in-a-function rule collapse must respect | ✅ Batch 31 | `BP1661` (corrected) |

⭐ **The inverse existing is the single most useful fact here** — see [Q26-E](#q26-e--should-collapse--expand-be-a-guaranteed-round-trip).

---

## Ground truth (verified against code 2026-08-11)

### What already exists

| | |
|---|---|
| `GraphCommand.CollapseToFunction(Nodes, FunctionName, Pure, CategoryPath)` | `GraphCommand.cs:93` |
| `GraphCommand.CollapseToMacro(Nodes, MacroName, CategoryPath)` | `:100` |
| `GraphCommand.CollapseToComment` · `ExpandNode(NodeId)` | `:106` · `:112` |
| `GraphFragmentCloner.Clone(nodes, links) → ClonedFragment{Nodes, Links, NodeMap, PinMap}` | `.Compiler/Compiler/Transform/` |
| Macro model + all four pin projections + `MacroCallNode` | Batches 28-29 |

### What does not exist

| | |
|---|---|
| 🔴 **Any `BlueprintCommandSink` case for either collapse** | falls to `default:` — *"unknown commands are silently accepted (forward-compat)"* → **`return new GraphCommandResult(true, null)`** (`:218-220`). ⭐ **Dispatching a collapse today reports SUCCESS and does nothing.** Trap #5 |
| 🔴 **Any menu item** in `CanvasRenderer` | the NodeEdit **Demo** binds Ctrl+E in `DemoShell`, not in shared UI |
| 🔴 **The boundary analysis** — *which crossing links become parameters, which become returns* | ⚠ **this is the actual work.** `FakeCommandSink:401` models the gesture but hardcodes scenario S22's `Base`/`Multiplier`/`Bonus`/`Result` pins — **a scenario prop, not a reference implementation** |

---

## ⭐ The one structural fact that drives every question below

**A selection's boundary is four sets**, and each maps to a different part of the extracted graph:

| Crossing | Direction | Becomes |
|---|---|---|
| **exec** into the selection | in | the macro's **single** exec-in / the function's entry |
| **exec** out of the selection | out | one **`ExecOutDecl`** per distinct exit (macro) |
| **data** into the selection | in | one **`Graph.Inputs`** entry |
| **data** out of the selection | out | one **`Graph.Outputs`** entry |

Everything contentious is a degenerate case of this table.

---

## Q26-A — What happens when the selection has **more than one exec entry**?

D3 gave a macro **exactly one** exec-in. A selection entered from two places therefore has no legal
macro form.

| | Option | ⚖️ |
|---|---|---|
| **A1** | ⭐ **Refuse, and say which nodes are the entries** | ✅ honest, cheap, no silent restructuring · ⚠ the designer must reshape by hand |
| **A2** | Insert a merge/`Sequence` so both entries land on one exec-in | ✅ always succeeds · 🔴 **silently changes semantics** — two callers that were distinct become one path |
| **A3** | Extend D3 to N exec-**in**s | ✅ most general · 🔴 reopens a settled decision and changes the splice rules |

📐 **Claude's lean: A1.** ⭐ Consistent with the programme's whole direction — *refuse loudly rather than
restructure silently*. A2 is exactly the "plausible value instead of reporting" shape that Batches
28-31 spent four rounds removing. **Reuse:** the diagnostic machinery already exists; A1 is a check, not
a feature.

### ⭐ Why N exec-ins works for Unreal and not (yet) for us — the actual mechanism

⚠ **It is not the graph model.** Almost everything is symmetric work we have already done once:

| Piece | Status |
|---|---|
| a declaration list for entries | mirror of `ExecOutDecl` / `Graph.ExecOutputs` (Batch 29) |
| entry node projects **N exec-outs** | exact mirror of `ReturnNode` gaining **N exec-ins** (Batch 29). Today `EventEntryNodePins` emits one, `MakeExec("Out","Out")` |
| splice rule 1 becomes indexed | exact mirror of rule 2 |
| several predecessors converging | ✅ already handled — `ComputeMergePoints` allocates one shared block for in-degree ≥ 2 |

**The whole difficulty is one thing, and it is a property of our *backend*, not our design.**

Data pins here are **pull-based**: `ResolveDataPin` walks *backwards* from the consumer to the producer
and emits **at the point of use**. That splits producers in two:

| Producer | How it resolves | Safe from any path? |
|---|---|---|
| **pure** (`Add`, `Compare`, `GetVariable`) | recomputable — re-emitted at each point of use | ✅ yes |
| **impure** (non-pure `FunctionCall`, `CallPeerBlueprint`) | side-effecting ⇒ runs **once**, pinned where its exec pin sits, materialised as a local and cached in `_statementPinCache` | ⚠ only under the premise below |

✅ **Verified:** `StatementEmitter` emits `var __t{idx} = <expr>;` — **declaration and assignment
together**. There is no hoisted declaration to fall back on, so the local exists *only on the path that
ran it*.

**One exec-in is safe for a structural reason:** everything inside the body is reachable **only**
through that single entry, which sits downstream of the call node — so an impure producer feeding a
data-in dominates the entire body.

**Two exec-ins break exactly that.** Entry #2 can be reached by a path that never ran the producer:

```csharp
goto L_Entry2;
L_Entry1: var __t5 = SomeImpureCall();   // assigned only on this path
          goto L_Body;
L_Entry2: goto L_Body;                   // __t5 never assigned
L_Body:   Log($"{__t5}");                // ⇒ CS0165
```

⇒ **`CS0165` — a hard error in generated code**, naming a synthesized local, pointing at the
**consumer** rather than at the producer on the other path. ⭐ **This is design finding F2 exactly,
mirrored onto the input side.** F2 already rules for ≥ 2 exec-**outs** (*data outputs must be
pure-fed*); N exec-**ins** needs the same rule for data **inputs**.

### ⚠ So why is Unreal fine?

**Because Unreal does not emit C#.** Blueprints compile to bytecode for Unreal's own VM, with a flat,
zero-initialised local frame — **reading a never-assigned local yields the default value, not an
error.** So Unreal silently tolerates the case and hands you a stale or zero value. We hand the graph
to **Roslyn**, which performs definite-assignment analysis and refuses.

⭐ **The design already recorded this exact asymmetry** for the exec-out side: *"Unreal tolerates the
equivalent and yields a stale value; we would break the build instead."* Same trade, other direction.

⇒ **N exec-ins is not hard — our backend is stricter than the one we are copying.** The cost is the
symmetric work above **plus one more Stage 2 purity validator**, not a redesign. ⚠ And it inherits F2's
recorded caveat: a purity rule is **conservative**, rejecting impure producers that genuinely dominate
every entry. The precise check is dominance-based, but dominance exists only at Stage 5 — *after*
expansion — where the diagnostic would name synthesized nodes nobody placed.

📌 **This is why A1 is "not yet" rather than "no".** The blocker is a known-shaped validator, not a
model limitation. **If the architect wants Unreal parity, A3 is affordable** — it is F2 again, and F2
already shipped.

---

## Q26-B — Function or Macro: does the **editor choose**, or the **designer**?

Two legality rules already exist and are not negotiable:

- a selection containing a **latent** node cannot become a **Function** (`BP1650`/`BP1661`)
- a selection with **more than one exec exit** cannot become a **Function** (a Function returns once)

| | Option | ⚖️ |
|---|---|---|
| **B1** | ⭐ **Offer both; grey out the illegal one with the reason in the tooltip** | ✅ teaches the rule at the moment it matters · ⚠ needs the analysis to run before the menu opens |
| **B2** | Offer both; let the illegal choice fail with a diagnostic afterwards | ✅ trivial menu · ⚠ the designer has already named the thing before being told no |
| **B3** | Editor picks silently — Macro when latent or multi-exit, else Function | 🔴 **silent**, and the difference matters downstream |

📐 ⭐ **Claude's lean: B2 — revised 2026-08-11, the user is right and I was wrong.**

A greyed item **does not say why**. The reason has to be discovered by hovering something that looks
disabled, which is exactly the interaction a designer skips. **An error the moment you ask for the
thing actually teaches the rule.** Unreal does B2, and here it is not a wart — it is the better design.

⭐ **This codebase's own history is the strongest argument**, and it points the same way: the tracker
treats *"greyed with no explanation"* as a **defect**, not a UX pattern —
**[BP-76](Blueprint_Issues_Tracker.md)** (*Go to Definition and Expand render **permanently greyed***,
filed 🔴) and **[BP-77](Blueprint_Issues_Tracker.md)** (*a live button with no handler*). ⇒ **shipping a
new permanently-greyed menu item would be filing the next BP-76 ourselves.**

⚠ **Keep one piece of B1 anyway:** the boundary analysis should still be a **pure, side-effect-free
function**, because that is what makes it testable headlessly, independent of any gesture. B2 just
means we call it **on invoke** rather than on menu-open, and render its refusal as a **message naming
the offending nodes** — not a silent disable.

---

## Q26-C — What about **variables and state** referenced inside the selection?

| Kind | Status | Question |
|---|---|---|
| blueprint **Variables** (`Get`/`SetVariable`) | asset-scoped, visible from any graph | ⇒ **nothing to do**? Confirm |
| **WorkingState** reads/writes | asset-scoped | same |
| **function-local** variables | ⚠ **do not exist** — `Graph` has no `LocalVariables` field (**BP-57**) | moot today; will matter later |
| a **macro** declaring locals | forbidden (`BP1664`, reserved, unbuildable until BP-57) | consistent |

📐 **Claude's lean: nothing to do today, and say so explicitly** so a future session does not re-derive
it. ⚠ **But flag it:** when **BP-57** ships, collapse must decide whether a local referenced inside the
selection becomes a **parameter** or is **moved** into the extracted graph. That is a genuine future
decision, not a detail.

---

## Q26-D — Where does the boundary analysis **live**, and what is one undo entry?

BP-74 says *do it as a **host command** so it is one undo entry* (BP-60 precedent). That settles undo.
It does not settle the assembly.

| | Option | ⚖️ |
|---|---|---|
| **D1** | ⭐ Analysis in **`.Compiler`** (next to `GraphFragmentCloner`), the sink calls it | ✅ headlessly testable with no editor host · ✅ sits beside the clone primitive it uses · ✅ ⭐ **the inverse (`Stage2_5_ExpandMacros`) already lives there** — the two halves of one transform in one place |
| **D2** | Analysis in **`.Editor`**, beside the sink | ✅ shorter call path · ⚠ tests need editor scaffolding · ⚠ splits an operation from its inverse across assemblies |

📐 **Claude's lean: D1, strongly.** ⚠ **Precedent:** `BlueprintClipboard`'s clone core had to be **moved
down** to `.Compiler` in Batch 30 for exactly this reason, and BP-69 is on record that duplicating
across this boundary produced two copies that drifted. **Put it on the right side the first time.**

---

## Q26-E — Should **collapse ∘ expand** be a guaranteed round-trip?

⭐ **This is the question the user's "nontrivial test case proving it really works" turns on.**

Collapse and expansion are exact inverses, and **expansion already exists, works, and is proven by
execution** (Batch 31: spliced, compiled through real Roslyn, ticked across frames). So the strongest
available proof is a **property**, not an example:

> take a graph → collapse a selection to a macro → expand it again → the result is **structurally
> equivalent** to the original (same nodes, same links, modulo ids and layout)

| | Option | ⚖️ |
|---|---|---|
| **E1** | ⭐ **Make it a required invariant, test-locked** | ✅ the strongest correctness evidence available, and nearly free given expansion exists · ⚠ constrains collapse (no reordering pins, no "helpful" tidying) |
| **E2** | Nice-to-have; test a few examples instead | ✅ no constraint · ⚠ example tests are what let `BP1661` ship gated on the wrong condition for a whole batch |
| **E3** | Not required — collapse may normalise | 🔴 then nothing pins the boundary analysis at all |

📐 **Claude's lean: E1**, and it is the recommendation I hold most strongly. ⭐ **Batch 31's lesson was
precisely that shape-assertions pass while the feature is wrong** — `BP1661`'s gate was inverted and the
whole suite stayed green, because every fixture encoded the same wrong assumption. **A round-trip
property cannot encode the assumption it is testing.** It is the closest thing to a proof this codebase
can get for a refactoring operation.

⚠ **Honest cost:** "structurally equivalent" needs a definition — node kinds and link topology yes;
node ids, positions and pin ids no. That comparator is real work (a canonical graph hash), and it is
also reusable for any future refactor.

---

## What this unblocks

| | |
|---|---|
| **BP-74** | the row itself |
| **BP-77** | *"Macros +"* gets a second, better entry point — collapse is how macros are really made |
| **BP-76** | `ExpandNode` is the same table read backwards; the menu gating is already its own row |
| **BP-80** | its remaining visual half becomes smaller — collapse *is* the palette-free way to create a macro |

## Not in scope for this question

`CollapseToComment` (cosmetic, no boundary analysis) · `ExpandNode`'s menu gating (**BP-76**) ·
function-local variables (**BP-57**) · cross-asset collapse (Q25-C1 keeps macros asset-local).

---

## ⭐ How Unreal resolves these — researched 2026-08-11

Sources at the foot of this section. ⚠ **Where Unreal and this codebase differ, the difference is
called out explicitly — including one place Unreal is weaker and we should beat it.**

### The shape of Unreal's feature

Unreal ships **three** collapse commands, all always present on the selection context menu:

| Command | Produces | Reusable? |
|---|---|---|
| **Collapse Nodes** | a *collapsed graph* — organisational only. ⚠ **It inherits the limits of the graph containing it**: a collapsed graph inside a Function still cannot hold latent nodes | ❌ no |
| **Collapse to Function** | a real Function | ✅ yes, and callable from other Blueprints |
| **Collapse to Macro** | a real Macro | ✅ yes |

Every one of them has **`Expand Node`** to revert — ⭐ **the inverse gesture is native in Unreal, not an
afterthought.** *"Execute is added by default when collapsing."*

📌 We have no equivalent of **Collapse Nodes** (our third command is `CollapseToComment`, which is
cosmetic). Out of scope here, but worth knowing the gap exists.

### Per question

| Q | Unreal | vs. our lean |
|---|---|---|
| **A** exec entries | ⭐ **Unreal macros support N exec-INs.** A macro's `Inputs` tunnel takes *"any number of execution or data pins"*; the docs' own example has one exec-in `Test` and two exec-outs `Win`/`Lose` | ⚠ **Unreal is more permissive than our D3** (exactly one exec-in) — **because it emits bytecode, not C#.** Mechanism in Q26-A; reframing below |
| **B** who chooses | **Unreal offers all three and fails afterwards with a message** — e.g. *"Macros cannot have latent functions (\"Delay\")"*. It does **not** grey out the illegal choice | ⭐ **Unreal is right and my first lean was wrong** — a greyed item does not say why. Lean revised to **B2**; see Q26-B |
| **C** variables | Functions have local variables; macros do not — same split as ours (`BP-57`/`BP1664`) | ✅ already at parity |
| **E** round-trip | `Expand Node` exists for all three forms, so collapse↔expand is a supported workflow — but Unreal **does not guarantee or verify** structural identity | ⇒ our **E1 is better than Unreal**, and cheap because our expansion is already proven by execution |

### 🔴 Q26-F (new) — the place Unreal is **weaker**, and we should not copy it

**Unreal's macros *can* contain latent nodes** (that is why the standard macro library's loops exist,
and why the exit-and-re-enter pattern is idiomatic). ⚠ **But `Collapse to Macro` refuses a selection
containing one**, with *"Macros cannot have latent functions"*.

⭐ **That refusal is an Unreal wart, not a rule.** The capability allows it; only the gesture forbids it.
⚠ **And it is precisely backwards for us:** `BP-78` records that **a macro is the only construct that
can factor out a reusable *latent* sequence** — that is the whole reason macros were built here — and
Batch 31 **proved it by execution** (aim → `Delay 0.4` → fire, spliced, compiled through real Roslyn,
ticked across frames). ⇒ **Refusing to collapse a latent selection would throw away our single biggest
advantage over the thing we are copying.**

📐 **Claude's lean: allow it, and make it a headline test** — collapse a latent sequence to a macro,
expand it, run it across frames. ⭐ **This is the "same or better" case: same gesture, strictly better
behaviour, and we already have the machinery to prove it.**

⚠ **The legality rule stays**, and we already have it: a latent macro may not be *called from* a graph
that compiles to a synchronous method — `BP1661`, corrected in Batch 31 to gate on *"is this graph a
`FunctionCall` target"* rather than graph kind. **Collapse should reuse `BP1661`, not invent a rule.**

### ⚠ Reframing Q26-A now that Unreal's answer is known

Unreal's macro model has **N exec-ins**; our **D3** settled on exactly one. So:

- **A3** (*extend D3 to N exec-in*) is the **Unreal-parity** answer — ⛔ but it reopens a settled
  architect decision and changes the splice rules, so it is the architect's call, not ours.
- **A1** (*refuse, and name the entry nodes*) is **more restrictive than Unreal**, but it is
  **forward-compatible**: nothing about refusing today prevents A3 later, and the refusal message can
  say so.
- **A2** (*silently insert a merge*) stays wrong under any reading.

📐 **Revised lean: A1 now, A3 as the stated parity target.** ⚠ **Note honestly that this is the one
question where I am recommending we ship *less* than Unreal** — on the grounds that a settled decision
should be reopened deliberately rather than as a side effect of building a gesture.

### Sources

- [Macros in Unreal Engine — official docs](https://dev.epicgames.com/documentation/unreal-engine/macros-in-unreal-engine?lang=en-US) (tunnel nodes, *"any number of execution or data pins"*, `Win`/`Lose` example)
- [Collapsing Graphs in Unreal Engine — official docs](https://dev.epicgames.com/documentation/en-us/unreal-engine/collapsing-graphs-in-unreal-engine) (the three commands, `Expand Node`, *"Execute is added by default"*)
- [Blueprint Macro Library — official docs](https://dev.epicgames.com/documentation/en-us/unreal-engine/blueprint-macro-library-in-unreal-engine)
- [Delay and other latent actions inside a Macro — Epic forums](https://forums.unrealengine.com/t/delay-and-other-latent-actions-inside-a-macro/9024) (latent-in-macro status over time; the *"Macros cannot have latent functions"* collapse refusal)
- [Managing complexity in Blueprints — Epic](https://www.unrealengine.com/blog/managing-complexity-in-blueprints) (a collapsed graph inherits its container's limits; functions may not hold latent actions)

⚠ **Confidence:** the official-docs rows are solid. The *collapse-refuses-latent* behaviour comes from
forum/answers threads spanning 2014→2025 with shifting engine behaviour, so **treat it as "Unreal has
historically refused this" rather than a precise statement about 5.6**. It does not change the
recommendation — we should allow it either way.

---

## ✅ Answers — **SETTLED 2026-08-11**

⚠ **Provenance, stated precisely:** **A, B and F are user rulings**, given directly in conversation.
**C and E** are the user accepting Claude's leans. **D** was delegated to Claude to investigate and is
answered against code below, in the Q23/Q24/Q25 self-research pattern. **NotebookLM was not consulted
on this round** — say so if it is ever cited as an architect ruling.

| Q | Answer | Source |
|---|---|---|
| **A** | ⭐ **A3 — N exec-ins. Unreal parity.** ⚠ **This supersedes [Q25](Architect_Question_25_Macros.md)-D3** (*"exactly one exec-in"*) | user |
| **B** | **B2 — offer all forms; refuse on invoke with a message naming the offending nodes.** ⛔ No greyed-out menu items | user |
| **C** | Nothing to do today; **re-decide when BP-57 lands** | lean accepted |
| **D** | ⭐ **D1 — the analysis lives in `.Compiler`.** Investigated below | Claude |
| **E** | **E1 — collapse ∘ expand is a required, test-locked structural invariant** | lean accepted |
| **F** | ⭐ **ALLOW latent nodes in a collapsed selection.** Unreal's refusal makes no sense — its macros *may* hold latent nodes, so the gesture forbids what the capability permits | user |

### A3 — what it actually costs, and the one thing it drags in

⛔ **A3 reverses a settled architect decision (Q25-D3), deliberately.** Anyone reading Q25 must land
here. The mechanism section above is the justification: the model work is symmetric to Batch 29/30, and
the real blocker is C#'s definite-assignment analysis, which Unreal's bytecode VM does not have.

⚠ **A3 is not free: it drags in the mirror of F2.** With ≥ 2 exec-**ins**, a data **input** fed by an
**impure** producer is definitely-assigned only on the entering path ⇒ `CS0165`. ⇒ **a new Stage 2
rule is required: when a macro declares ≥ 2 exec-ins, every data input must be fed by a pure producer
chain.** Same shape as `BP1663`, pointed the other way; ⚠ inherits the same recorded caveat (purity is
conservative and rejects impure producers that genuinely dominate every entry — dominance exists only
at Stage 5, after expansion, where the diagnostic would name synthesized nodes).

📌 `BP1664` and `BP1666` are the unused codes in the reserved macro block. **The implementation session
allocates** (rule 3).

### D1 — investigated, and the answer is not what the directory names suggest

⚠ **`.Editor` does *not* reference `.Compiler` directly**, which initially looks like it rules D1 out.
It does not. The real chain, verified from the `.csproj` files:

```
Hrot.Blueprints.Editor  →  Hrot.Blueprints.Core  →  Hrot.Blueprints.Compiler
```

`Hrot.Blueprints.Core.csproj` carries `<ProjectReference Include="..\Hrot.Blueprints.Compiler\…" />`,
and `.Editor` references `.Core`. ⭐ **So `.Compiler` is reachable from the sink transitively — and this
is not a theory: `BlueprintClipboard` (in `.Editor`) already calls `GraphFragmentCloner` (in
`.Compiler`) exactly this way, shipped in Batch 30.** A working precedent beats an inference.

Three further checks, all clean:

| | |
|---|---|
| `.Compiler` has **no** NodeEdit or editor reference | only `Fdp.Toolkits`, `Hrot.Common`, and NuGet ⇒ the analysis stays **headlessly testable** |
| the sink already maps the editor's `NodeId` → `Guid` | `BlueprintCommandSink.ResolveNodeId` (`:383`) — the boundary is one existing helper |
| crossing pins carry their own type | `Pin.TypeRef` (`GraphTypes.cs:125`) ⇒ building `ParameterDecl`s needs **no type registry**, so no editor-side resolution leaks in |

⇒ **D1 with no caveats.** ⭐ And it keeps an operation and its exact inverse in one assembly:
`Stage2_5_ExpandMacros` is already there.

⚠ **One oddity noticed while investigating, not a blocker:** `Hrot.Blueprints.Core.csproj` `<Compile
Include>`s some `.Compiler` **source files** by path (`Compiler/Roslyn/**`, `Stage8_RoslynFinalize.cs`)
rather than consuming them from the referenced assembly. Do not add to that arrangement; put new files
in `.Compiler` and let the project reference carry them.

📌 **If the architect has no opinion on D**, treat it as self-researched against code in the
Q23/Q24/Q25 pattern and take **D1** — the assembly argument is a code fact (the inverse lives in
`.Compiler`), not an engine-rules question.
