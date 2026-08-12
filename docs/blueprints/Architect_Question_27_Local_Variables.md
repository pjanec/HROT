# Architect question #27 — Function-local variables (`BP-57`)

> **Raised 2026-08-11**, the natural next item now that the macro programme is complete.
> ⭐ **`BP-57` is also the last thing blocking `BP1664`**, the one unallocated macro diagnostic.
>
> ⚠ **The row is one line** — *"Per-function local variables absent from the data model itself"* — and
> there is **no design**. Hence this question rather than a handoff.

---

## Ground truth (verified against code 2026-08-11)

### What a "variable" is today

| | |
|---|---|
| `BlueprintAsset.Variables : List<VariableDecl>` | `{ Id, Name, Type, DefaultValueJson, IsEditable, IsExposedOnSpawn, Category, Tooltip, Comment }` |
| ⭐ **They become FIELDS on the `State` struct** | `StatementEmitter:59,63` emits `{sv}.{VarFieldName(idx)}` — **persistent per instance**, surviving every tick |
| `Graph` has **no** `LocalVariables` | verified — `Id · Name · Kind · Inputs · Outputs · ExecInputs · ExecOutputs · Nodes · Links · Comments · EditorMetadata` |
| Lookup is **asset-scoped** | `Stage0_Rehydrate.FindVariableDecl(variableId, asset)` · `Stage5.FindVariableIndex(variableId)` |

⇒ **A local is not a small addition to this.** Everything above is *persistent instance storage*; a
local is *per-invocation*. **They are different storage classes, not different scopes of one thing.**

### 🔴 A latent defect you will land on top of — `BP-224`'s shape again

> ⚠⚠ **CORRECTED 2026-08-11 — see [FINDING_Variable_Index_Space.md](FINDING_Variable_Index_Space.md).**
> The mechanism below is real, but *"latent defect, `BP-224`'s shape"* is **the wrong
> characterization**. It cannot fire, for two **structural** reasons rather than coincidence:
> `Variables` and `WorkingState` are the storage of **different dispatch kinds** and never coexist, and
> the Get/SetVariable picker offers **only `Variables`**. ⇒ It is an **unenforced invariant**, not a
> latent defect, and the fix is to make the invariant unexpressible rather than to rewrite the index
> space. **Read the finding before acting on this section.**

`Stage5.FindVariableIndex` (`:4498`) searches **three** lists and returns the index **within whichever
list matched**:

```csharp
for (int i = 0; i < variables.Count;  i++) if (variables[i].Id  == guid) return i;
for (int i = 0; i < workState.Count;  i++) if (workState[i].Id  == guid) return i;
for (int i = 0; i < parameters.Count; i++) if (parameters[i].Id == guid) return i;
```

`EmissionContext.VarFieldName` (`:55`) consumes that integer as a **priority-ordered union**:

```csharp
if (index < Asset.Variables.Count)    return Asset.Variables[index].Name;
if (index < Asset.WorkingState.Count) return Asset.WorkingState[index].Name;
return $"__var_{index}";                      // ⚠ Parameters are not here at all
```

⭐ **The two disagree about what the integer means.** A `WorkingState` entry found at index 2 resolves
to **`Variables[2]`** whenever `Variables.Count > 2` — a different field entirely.

⚠ **Coordinator-measured: no shipped asset has BOTH `Variables` and `WorkingState` non-empty**, so it is
**latent, not live** — ⭐ **exactly `BP-224`'s shape, which the implementation session named: *a
discriminator that is correct only because one of its cases never occurs*.** `AiPrimitiveLowering:56`
already shifts "every real field by +1" to keep this resolution working, which is the smell.

⇒ ⭐ **Locals would be a FOURTH source in that index space.** See [Q27-D](#q27-d).

---

## ⭐ How Unreal resolves it — including a wart worth not copying

| | |
|---|---|
| **Function locals** | exist, and **reset to their default on entry** — genuinely per-invocation |
| 🔴 **Macro locals** | exist **and are broken by design.** Because a macro is *spliced into the calling graph*, a macro "local" lands in the host's scope and **does not reset per call**. Community reports are consistent: they *look* like function locals and do not behave like them |
| **"Persistent local"** | a separate concept, and **only for Booleans and Integers** |

⭐ **This vindicates `BP1664`.** Our rule *"a macro may not declare a local"* refuses precisely the
construct Unreal shipped and regrets — and **for the same mechanical reason**: our macros are spliced
too, so a macro-local has nowhere per-invocation to live. ⇒ **Recommend keeping `BP1664` as a rule, not
as a placeholder** (see [Q27-B](#q27-b)).

---

## Q27-A — What **is** a local, in the emitted C#?

| | Option | ⚖️ |
|---|---|---|
| **A1** | ⭐ **A C# local in the emitted method** — `var __local_Foo = default;` at entry | ✅ genuinely per-invocation, zero `State` growth, matches Unreal's *function* semantics · ⚠ **cannot survive a suspension** (see the constraint below) |
| **A2** | Another `State` field, scoped by convention | ✅ trivial; reuses every existing path · 🔴 **not a local** — it persists across calls and across instances' ticks, which is the bug Unreal's macro locals have |
| **A3** | A C# local where possible, a `State` field where the graph can suspend | ✅ correct in both cases · ⚠ **the storage class becomes a derived property**, and a designer cannot see which they got |

⚠⚠ **The constraint that decides this:** a suspension is an early `return` (`WaitLowering_Instance`),
so **a C# local does not survive it**. Function graphs **cannot** suspend (`BP1650`), so A1 is safe
*there*. Event/tick graphs **can**.

📐 **Claude's lean: A1, scoped to graph kinds that cannot suspend** — i.e. answer A1 *and* Q27-B
together, rather than A3's silent per-graph switch. ⭐ **A2 is the option to avoid**: it reproduces
Unreal's macro-local wart by construction.

---

## Q27-B — Which graph kinds may declare locals?

| Kind | Can suspend? | |
|---|---|---|
| **Function** | ❌ never (`BP1650`) | ✅ the obvious yes |
| **Macro** | ✅ (that is its purpose — `BP-78`) | ⛔ `BP1664` forbids. ⭐ **Unreal shipped this and it is broken** |
| **Event / tick** | ✅ | 📐 **the open one** — Unreal calls these "the Uber Graph" and locals there are exactly what misbehaves |
| **Construction** | ❌ | 📐 probably yes, but nobody has asked |

📐 **Claude's lean: Function (and probably Construction) only, for now.** ⭐ **`BP1664` becomes a real
rule with a real reason** rather than a reserved code. ⚠ **If the architect wants Event-graph locals,
that forces A3** and the storage class stops being predictable from the graph kind — say so explicitly
rather than discovering it in the emitter.

---

## Q27-C — Shadowing

A local named `Health` in a graph whose asset also declares `Health`.

| | Option | ⚖️ |
|---|---|---|
| **C1** | ⭐ **Local wins inside its graph** (C#/Unreal behaviour) | ✅ least surprising to anyone with either background · ⚠ `Get Health` means different things in two graphs of one asset |
| **C2** | **Refuse the duplicate name** | ✅ no ambiguity ever · ⚠ stricter than both references |
| **C3** | Allow, and require the node to say which | ✅ explicit · ⚠ a new pin/field on every Get/Set |

📐 **Claude's lean: C1** — but ⚠ **note the machinery**: `FindVariableDecl`/`FindVariableIndex` are
**asset-scoped with a name fallback**. ⭐ **The name fallback is what makes shadowing dangerous** — an
id miss silently resolves to the asset variable of the same name. ⇒ **whatever is chosen, the local
lookup must be id-first and must not fall through to a name match on a different scope.**

---

## Q27-D — ⚠ Fix the index space **first**, or add a fourth source to it?

| | Option | ⚖️ |
|---|---|---|
| **D1** | ⭐ **Fix `FindVariableIndex`/`VarFieldName` to agree — one storage-kind + index pair — THEN add locals** | ✅ the latent defect stops being latent · ✅ locals land on a sound base · ⚠ a preparatory batch with no visible feature |
| **D2** | Add locals as a fourth list and keep the current scheme | 🔴 **makes the latent case reachable** — `BP-224` all over again, and this time we would be the ones making it live |
| **D3** | Give locals a separate op (`IrOp_Read/WriteLocal`) that never enters the union | ✅ no contact with the defect at all · ⚠ leaves the existing defect latent, and doubles the read/write op family |

📐 **Claude's lean: D1, then locals.** ⭐ **`BP-224` is the precedent and it is recent**: a
discriminator that was wrong from the day it was written, harmless only because one case never
occurred — and the moment collapse made macros real, it became a live user-visible defect. **We are
about to occupy the empty case.** ⚖️ D3 is the pragmatic fallback if D1 is judged too broad.

---

## Q27-E — Initialisation

Unreal resets a function local to its default **on entry**. `VariableDecl` already carries
`DefaultValueJson`.

📐 **Claude's lean: reset on entry, reusing `DefaultValueJson`** — no new model, and it is the one
semantic Unreal's macro locals fail to provide. ⚠ Confirm the reset is per *invocation*, not per
instance, once A is settled.

---

## What this unblocks

`BP-57` itself · ⭐ **`BP1664`** — the last unallocated macro diagnostic · and the recurring
authoring complaint that a graph needs a scratch value with nowhere to put it that is not permanent
instance state.

## Not in scope

Persistent locals (Unreal's Bool/Int-only concept) · local **structs**/collections beyond whatever
`VariableDecl` already supports · the `Parameters` gap in `VarFieldName` **unless D1 is chosen**, in
which case it falls out.

---

## ✅ Answers — **SETTLED 2026-08-11**

⚠ **Provenance:** **A, C, E** are user rulings. **B** was **reframed by the user** and is better for it.
**D** was **delegated to Claude to rule on**. **NotebookLM was not consulted** — do not cite this as an
architect ruling.

| Q | Answer |
|---|---|
| **A** | ⭐ **A1 — a C# local in the emitted method.** Per-invocation, no `State` growth |
| **B** | ⭐ **Reframed — see below.** *"Which graphs can suspend"* is **not a graph-kind property**, and **macros do not have locals because they have no scope to own them** |
| **C** | **C1 — the local wins inside its graph** |
| **D** | ⭐ **Claude's ruling: locals get their OWN op. The index-space defect is filed separately and does NOT block this** — see below |
| **E** | **Reset to `DefaultValueJson` on entry** |

### ⭐ B, reframed — the user's correction, and it is the better model

> *"What graphs can suspend? Don't macros always inherit the vars from their host, not having any local
> ones themselves?"*

✅ **Both halves verified against code.**

**On suspension — it is not a kind property.** `InstanceLowering:16-21` applies `WaitLowering_Instance`
to **any `IrGraph` that contains a latent op**:

```csharp
bool hasLatent = graph.Blocks.SelectMany(b => b.Statements)
    .Any(s => s.Operation is IrOp_LatentDelay or IrOp_WaitForChannel
                           or IrOp_WaitForEvent or IrOp_InlineActionCall);
```

⇒ ⭐ **A Function graph cannot suspend not because it is a Function, but because `BP1650`/`BP1661`
keep latent nodes out of it.** The suspension property is *"contains a latent op"*, and the rails are
what make it false for functions. **State the rule that way** — a kind-based test would be a
`BP-224`-shaped discriminator, correct only by coincidence.

**On macros — the user is right, and it dissolves the question.** A macro is **spliced**: after
expansion it does not exist as a graph, its nodes are in the host. ⇒ ⭐ **A macro-local has nothing to
be scoped to.** `BP1664` is therefore **not a policy we impose but an incoherence we report** — and
⭐ **that is precisely why Unreal's macro locals are broken**: they allowed the incoherent construct,
and it landed in the host's scope and stopped resetting.

⇒ **A macro's nodes see the HOST's locals**, automatically, because after splicing they *are* host
nodes. **A macro does get locals — the host's.**

⚠⚠ **And that produces a hazard nobody has named yet, which the build must handle:** a macro body
referencing a local **resolves against whichever host it is spliced into**. The same macro can expand
cleanly in one graph and reference a non-existent local in another. ⇒ **the reference must fail loud at
the call site, naming the macro and the missing local** — `BP1661`'s attribution lesson, one level
along. 📌 **This is new since the questions were written; treat it as part of the answer.**

⇒ **Permitted:** `Function`, and `Construction` (neither can suspend). **Not `Macro`** — no scope.
**Event/tick graphs: deferred**, because A1 cannot survive a suspension and nothing yet needs them.

### ⭐ D — Claude ruling, as asked

**Locals get their own IR op (`IrOp_ReadLocal`/`IrOp_WriteLocal` or equivalent), and the existing
index-space defect is filed as its own row and NOT fixed in the locals batch.**

**Why this is forced rather than chosen:** ⭐ **A1 decides it.** `IrOp_Read/WriteVariable` emit
`{sv}.{VarFieldName(idx)}` — **a field access on the `State` struct**. A C# local is not a `State`
field, so **the existing op cannot represent it at all.** Locals therefore never enter that index
space, and the question *"do we add a fourth source"* does not arise.

**Why not fix the union first anyway (my earlier D1 lean):**

| | |
|---|---|
| ⭐ **It is no longer on this path** | A1 routes locals around it entirely. Fixing it first would be a preparatory batch for a hazard this feature does not touch |
| ⚠ **Mixing them makes both harder to verify** | the locals batch would carry an unrelated index-space refactor, and a revert-goes-red on either would be muddied |
| 🔴 **But it is real and must not be lost** | `FindVariableIndex` returns a per-list index; `VarFieldName` reads a priority union; they agree only because **no shipped asset has both `Variables` and `WorkingState`** populated. ⭐ **`BP-224`'s exact shape** — and `BP-224` sat harmless for months until collapse made its empty case occur |

⇒ **File it now, fix it on its own.** ⚠ **And say in that row what would make it live**: an asset with
both lists populated — which `Parameters` (absent from `VarFieldName` entirely) may already achieve.
**Check that before assuming it is still latent.**

### 📌 On C1 — the user's argument is stronger than the doc's

> *"Can't imagine how to restrict shadowing in a generic shared function."*

⭐ **Right, and it makes C2 not merely unattractive but unenforceable.** A `Library` function is
compiled **once** and called from assets it has never seen; refusing a name that collides with *any*
consumer's variables is not a check that can be written. ⇒ **C1 by necessity, not only by preference.**

⚠ **The lookup hazard stands and is the build's problem:** `FindVariableDecl`/`FindVariableIndex` are
asset-scoped **with a name fallback**, so an id miss silently resolves to the asset variable of the
same name. ⇒ **local lookup must be id-first and must never fall through to a name match in another
scope.**
