# HANDOFF — Batch 37: `BP-57` — function-local variables, the compiler half

> 📌 **Dispatched at `1b759b48`** · ⚠ **§6 AMENDED and RE-DISPATCHED at `PENDING`** — the user
> confirmed on `2026-08-12` that no implementation run had picked this up yet, so rule 1's *never amend
> after dispatch* does not bite. ⭐ **Only §6 and one line of §8 changed**; §§0–5 and §7 are byte-identical
> to the original dispatch. **Frozen again from this stamp on.**
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-57`, `BP-82`, `BP-224` are *referenced*.
> `BP1664` is a **reserved diagnostic code** this batch finally makes buildable. **You allocate**
> everything new (rule 5).
>
> 📄 **[Q27](Architect_Question_27_Local_Variables.md) is SETTLED** — A1 · B (reframed) · C1 · D · E.
> ⛔ **Do not reopen.** ⚠ **Read Q27-B's reframing before designing anything** — it changes what the
> question even was.
>
> ⭐ **This closes the last thing blocking `BP1664`**, and with it the final macro code.

---

## 0. Scope — the compiler half only, deliberately

⛔ **No authoring UI in this batch.** Declaring a local from the editor is **Batch 38**.

**Why split** (the collapse precedent, Batches 33→34, which worked): the compiler half here is a new
model list, a **new IR op**, a new emit path, a resolution change and **two rails** — one of them
novel. Adding the UI would make this the largest batch of the programme, and the UI is a
mirror-an-existing-command job that gains nothing from being done alongside.

⇒ After this batch a local is declarable **in JSON only** — exactly where collapse's core sat after
Batch 33. **Say so in the row** rather than letting it read as finished.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** model | — | ⭐ **entirely** — reuse `VariableDecl` |
| **2** IR + emit | ⭐ **the op and the declaration site** | the golden IR tests |
| **3** resolution | ⭐ **the shadowing lookup** (§3) — the name fallback is the trap | the tests |
| **4** `BP1664` | — | 🟢 mechanical now |
| **5** ⭐ the macro/host-local rail | ⭐ **all of it** — novel (§5) | the negative fixtures |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | The **tracker + detail docs are yours** for this batch |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

---

## 1. 🟢 The model

```csharp
public sealed class Graph { /* … */ public List<VariableDecl> LocalVariables { get; set; } = new(); }
```

⭐ **Reuse `VariableDecl`** — it already carries `Id`, `Name`, `Type`, `DefaultValueJson`, `Category`,
`Tooltip`. 📐 **Your call:** `IsEditable` and `IsExposedOnSpawn` are **meaningless for a local** (there
is no instance to expose it on). Reuse wholesale and ignore them, or introduce a narrower decl.
⚖️ Reuse buys the type-picker and every existing row view for free in Batch 38; a narrower type is
honest but duplicates most of the shape. **State which and why.**

⚠ **`Stage3_Normalize` rebuilds `Graph`.** Batch 31 replaced both copies with `Graph.WithNodesAndLinks`
**plus a reflection test that fails when any member is not carried across**. ⇒ adding `LocalVariables`
should turn that test **red until you add it**. ⭐ **If it does not, the guard is broken — that is a
finding**, same as Batch 32.

📌 **Empty means today's behaviour**; every existing asset must round-trip byte-identically.

---

## 2. 🔴 IR and emit — ⚠ **a local is NOT a `State` field**

⭐ **Q27-D ruled: locals get their own op**, and A1 is what forces it. `IrOp_Read/WriteVariable` emit
`{sv}.{VarFieldName(idx)}` — **a field access on the `State` struct** (`StatementEmitter:59,63`). A C#
local is not a `State` field, so **the existing op cannot represent one.**

⇒ New ops (name them yourself), emitting **plain locals**:

| | |
|---|---|
| **Declare** | at the top of the emitted method body — `EmitInstanceFunctionMethod` (`InstanceEmitter:270`) is the Function-graph site; find the Library equivalent |
| **Initialise** | ⭐ **Q27-E: reset to `DefaultValueJson` on entry**, so it is genuinely per-invocation |
| **Read/write** | a bare local, no `{sv}.` prefix |

⭐ **The whole point of A1 is that the `State` struct does not grow.** ⚠ **Assert that**: a graph with
locals emits a `State` with **the same fields as before**. A local that quietly became a field is
exactly the Unreal macro-local wart Q27 exists to avoid.

⚠ **Locals must never enter `FindVariableIndex`/`VarFieldName`.** See §6.

---

## 3. 🔴 Resolution — the shadowing lookup, and the trap in it

**Q27-C1: a local wins inside its graph.** ⚠ **The user's argument is why C2 was never available:** a
`Library` function is compiled **once** and called from assets it has never seen, so *"refuse a name
that collides with any consumer's variables"* is not a check that can be written.

⛔⛔ **The trap.** `Stage0_Rehydrate.FindVariableDecl` and `Stage5.FindVariableIndex` are **asset-scoped
with a NAME FALLBACK** (`FindVariableIndex:4518-4521` — after the id loops fail, it matches on name).

⇒ **An id miss silently resolves to the asset variable of the same name.** With shadowing that is not
a nuisance, it is **the wrong storage read, silently** — the persistent instance field instead of the
per-call local.

⇒ ⭐ **Local lookup must be id-first, graph-scope-first, and must NOT fall through to a name match in
another scope.** ⚠ **Test the collision explicitly**: a local and an asset variable sharing a name,
where reading the local must not see the field, and writing the local must not touch it. **A test that
only checks the local's value would pass on the defect** — assert the asset variable is *unchanged*.

---

## 4. 🟢 `BP1664` — finally buildable

**A macro graph declaring a local is an error.** ⭐ **Q27-B reframed why**, and the row should say it:
a macro is **spliced**, so after expansion it does not exist as a graph and its nodes are in the host —
**a macro-local has nothing to be scoped to.** It is not a policy we impose, it is an incoherence we
report.

⭐ **Unreal shipped this and it is broken** — their macro locals land in the host's scope and never
reset per call. **We refuse the construct they regret.** Worth one line in the row.

---

## 5. 🔴🔴 The rail nobody had named — a macro body referencing a **host** local

⭐ **This is new since Q27 was written and it is the interesting item.**

Q27-B: after splicing, a macro's nodes **are host nodes**, so they see **the host's** locals — which is
correct and is what "macros inherit the host's variables" means.

⚠⚠ **But a macro is called from many hosts.** A macro body referencing a local **resolves against
whichever host it is spliced into** ⇒ **the same macro expands cleanly in one graph and references a
non-existent local in another.**

⇒ **It must fail loud, at the call site, naming the macro and the missing local.** ⭐ **`BP1661`'s
attribution lesson one level along**: blame a node the designer placed, not something inside somebody
else's macro.

📐 **Two things are yours to decide, and say which:**

| | |
|---|---|
| **Where it fires** | Stage 2 at the call site (like `BP1661`, before expansion) or during expansion. ⚖️ **Stage 2 is the lean** — the error gate runs before Stage 2.5, and it names an authored node |
| **How a macro references a local at all** | by **name** is the only thing that can work across hosts, since ids are per-graph. ⚠ **That reopens §3's fallback hazard deliberately** — say how you keep it from resolving to an asset variable instead |

⚠ **This may argue for refusing local references inside macro bodies entirely**, at least for now.
⭐ **If you conclude that, say so and file it** — a smaller, honest rule beats a resolution scheme
nobody can predict. **Do not build a cross-host name resolver just because it is possible.**

---

## 6. 📌 The index space — **file it, do NOT fix it here**

> ⚠ **Amended `2026-08-12`, pre-read** — the user confirmed this handoff had not yet been picked up, so
> this section is replaced rather than left wrong. 📄 **Full working:
> [`FINDING_Variable_Index_Space.md`](FINDING_Variable_Index_Space.md).** ⭐ **The instruction is
> unchanged — file it, do not fix it here.** What changed is *what the row should say*, and one
> question below is now **answered**, not asked.

`Stage5.FindVariableIndex` returns an index **within whichever of three lists matched**;
`EmissionContext.VarFieldName` (`:55`) reads that integer as a **priority-ordered union** of
`Variables` then `WorkingState`, with `Parameters` absent entirely. ⭐ **They disagree about what the
integer means.**

### ⛔ It is **not** `BP-224`'s shape. Do not file it as one

| | |
|---|---|
| **`BP-224` was** | a discriminator **wrong from the day it was written**, harmless only because one case had never occurred yet |
| ⭐ **This is** | code that is **correct under an invariant that holds** — ⚠ **and that nothing enforces** |

Two independent structural facts hold it up, both coordinator-measured across
`Hrot.AI.Behaviors/Assets/Blueprints/*.bp.json`:

| | |
|---|---|
| **1. The lists are disjoint by dispatch kind** | **Instance** uses `Variables` only (13 assets); **AiPrimitive** uses `WorkingState`+`Parameters` and **never** `Variables` (23). ⇒ where `WorkingState` is populated, `Variables.Count == 0`, so `VarFieldName`'s first branch **cannot** fire |
| **2. The editor cannot author the other target** | `BlueprintPickerSources:148-152` queries **`_asset.Variables` and nothing else** ⇒ a Get/SetVariable aimed at a `WorkingState` field or a `Parameter` **is not authorable** |
| ⭐ **Someone was already bitten** | `AiPrimitiveLowering:42-66` **appends** `__phase` rather than prepending, commented *"would shift every real field by +1, so `VarFieldName` would emit the WRONG field."* **The workaround is the evidence** |

⇒ **The row is: nothing enforces the disjointness, and `Parameters` has no correct branch at all.**

### ⭐ The question this section used to ask is answered

It asked whether `Parameters` **already** makes it live. ⭐ **It does not** — the picker cannot author
such a node, and there are **zero** `Get`/`SetVariable` nodes in the corpus targeting a `Parameters` id.
⚠ **Independent confirmation is still worth having: if you measure otherwise, your measurement wins over
this note** — say so and raise the severity.

⇒ **File it as its own row** (⭐ **your id**, rule 3). ⛔ **Do not fix it in this batch** — locals never
enter that space (§2), so it is off this path, and folding an index refactor in would muddy
revert-goes-red on both. 📌 **The fix is Batch 38's**, and the finding's lean is *return a
`(storage-kind, index)` pair from `FindVariableIndex`* so the ambiguity becomes unexpressible.

📌 **One unrelated observation worth its own row:** ⚠ **four assets carry a numeric `Dispatch: 1`**
where every other asset has a string. Nothing to do with this; just noticed while measuring.

---

## 7. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (`RESUME_START_HERE.md` §3).
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival five batches running.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3234** / 0 / 10 skipped ⚠ *(total 3244 — `BP-111` filters 7 host-timing tests out)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### Tests

| | |
|---|---|
| ⭐ **Run it** | a Function graph with a local, **through real Roslyn**, called **twice** — the second call sees the **default**, not the first call's value ⚠ (`.Succeeded` never invokes Roslyn). **This is the test that proves "local" means what it says** |
| ⭐ **The `State` does not grow** | same fields as before adding the local |
| ⭐ **Shadowing** | local + asset variable sharing a name ⇒ the local is read/written **and the asset variable is unchanged** |
| **`BP1664`** | a macro declaring a local ⇒ the code, asserting the **code** |
| **§5's rail** | a macro body referencing a host local ⇒ whatever you decided, asserting the **code** and that it **names the call node** |
| **Round-trip** | existing assets byte-identical; `LocalVariables` absent still loads |
| **`Graph` copy guard** | Batch 31's reflection test went red until `LocalVariables` was carried — **say whether it did** |

---

## 8. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) · **your decl
reuse call** (§1) · **your §5 ruling** and where the rail fires · ⭐ **whether your own measurement
agrees that no authorable node targets `Parameters`** (§6) · **whether the `Graph` reflection guard went
red** · anything here **wrong against the code**.

⭐ **You have corrected this coordinator in five consecutive batches.** The last was Batch 35, where my
"reorder is destructive" premise was **wrong in the opposite direction from the real hazard** —
duplicate names, which fell out of a formula I had been quoting for weeks. ⭐ **§3 and §5 here are my
reasoning about resolution order, and they deserve exactly that scepticism.**
