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

📐 **Claude's lean: B1.** ⚠ **Note the cost honestly:** B1 means the boundary analysis must be a **pure,
side-effect-free function** that the menu can call speculatively. That is a design constraint worth
accepting anyway — it is what makes the analysis testable headlessly, independent of any gesture.

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

## Answers

> ⏳ **Awaiting the architect.** Record them here, then build.

| Q | Answer |
|---|---|
| **A** — multiple exec entries | |
| **B** — who chooses Function vs Macro | |
| **C** — variables and state | |
| **D** — where the analysis lives | |
| **E** — round-trip invariant | |

📌 **If the architect has no opinion on D**, treat it as self-researched against code in the
Q23/Q24/Q25 pattern and take **D1** — the assembly argument is a code fact (the inverse lives in
`.Compiler`), not an engine-rules question.
