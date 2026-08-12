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

## Answers

> ⏳ **Awaiting the architect.** Record here, then build.

| Q | Answer |
|---|---|
| **A** — what a local *is* in emitted C# | |
| **B** — which graph kinds may declare them | |
| **C** — shadowing | |
| **D** — ⭐ fix the index space first? | |
| **E** — initialisation | |
