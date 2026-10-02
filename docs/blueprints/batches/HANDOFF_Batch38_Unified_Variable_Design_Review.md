# HANDOFF — Batch 38: ⭐ **DESIGN REVIEW — the unified variable model.** No feature code

> 📌 **Dispatched at `a305e1b0`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** **You allocate everything new** (rule 5).
>
> ⛔⛔ **THIS BATCH WRITES NO FEATURE CODE.** The deliverable is an **assessment document**.
> ⚠ **`BP-57`'s remaining work was Batch 38 and is now
> [Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md) — postponed until this returns.**

---

## 0. What you are being asked to do, and why

Two design documents propose **unifying the blueprint variable model**:

| | |
|---|---|
| 📄 [Variable_Model_Unification.md](Variable_Model_Unification.md) | four bespoke lists → `Role` × `Scope`, in four stages **A → D** |
| 📄 [Variable_Editing_UI.md](Variable_Editing_UI.md) | one navigator + the shared table as the details view |

⭐ **Assess whether they are buildable, and find what they miss.** The user's words:

> *"They need to assess the feasibility of the unified design, find flaws and gaps, also to check the
> stages leading there. Usually such checks reveal weak spots to fill before breaking to actionable
> tasks."*

⚠⚠ **The coordinator wrote both documents and has been corrected by this session in every batch
since 29.** ⭐ **Treat every claim in them as a hypothesis.** §2 lists the ones that are load-bearing.

⛔ **Do not design the replacement.** ⭐ **Do not soften findings to be agreeable** — a review that
returns *"looks feasible"* has cost a batch and bought nothing. **If the design is wrong, say so.**

---

## 0a. ⚡ How to work

**You are on Opus.** ⭐ **This is a reading-and-measuring batch, and it is Opus work** — the value is
in judgement, not volume. 🟢 **Delegate to Sonnet only the mechanical sweeps** (§3's consumer census,
§2's re-measurements); ⭐ **Opus keeps every verdict.**

| | |
|---|---|
| **Throwaway probes are expected** | ⭐ the coordinator's claims in §2 were produced by temporary xunit files that were then **deleted**. Do the same |
| ⛔ **Nothing temporary is committed** | no probe files, no scratch assets in the corpus |
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** for this batch — file real defects you find as rows |
| **No PR** | |

⚠ **Gates:** you change no product code, so the eight gates should be **untouched**. ⭐ **Run the build
and the Blueprints suite ONCE at the start** to confirm you are measuring against a green tree, and
**once at the end** to prove you left nothing behind. **Report both.**

**Baseline — coordinator-RUN, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3243** / 0 / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | 0 failed |

---

## 1. The deliverable

**One document: `docs/blueprints/REVIEW_Unified_Variable_Design.md`.** Structure it however serves the
findings, but it must answer:

| | |
|---|---|
| **1** | ⭐ **Is the `Role` × `Scope` model right?** Does every existing declaration fit exactly one cell — and is there anything that fits **none**, or **two**? |
| **2** | ⭐⭐ **Are the four stages independently shippable and revertible, in that order?** §4 is where the coordinator is least confident |
| **3** | ⭐ **What is missing entirely?** §3 names the sweeps that were **not** done |
| **4** | **Which §2 claims survive your own measurement** — and which do not |
| **5** | ⭐ **What must be decided before this can be broken into tasks**, and what can be decided later |
| **6** | 📐 **Your verdict**: build it as staged · build it with named changes · ⛔ **or don't** |

⚠ **Rank by severity and say which findings are blockers.** A list of twenty equal-weight
observations is not a review.

---

## 2. ⚠⚠ Coordinator claims that are load-bearing — **re-measure them**

Each was measured by the coordinator with a throwaway probe. ⭐ **Where your measurement disagrees,
yours wins and the design changes.**

| # | claim | how it was measured |
|---|---|---|
| **C1** | ⭐ **A struct-typed blueprint variable ALREADY COMPILES** by **fully-qualified** name — correct `Marshal.OffsetOf` / `Unsafe.SizeOf` in the field descriptor — while `StaticTypeRegistry.TryResolve` returns **False** for that same type. Short name ⇒ `BP1500` | temporary xunit compile probe |
| **C2** | `DetailsTarget` already carries `Variable(VariableId)` **and `LocalVariable(FunctionId, LocalId)`**, and ⛔ **no Blueprint type implements `IDetailsViewProvider` at all** | grep |
| **C3** | `BP1024`/`BP1031`/`BP1011` **enforce** the Variables/WorkingState disjointness at **Stage 2** — ⚠ and lowering runs at Stage 6, **after** them | reading `Stage2_Validate` |
| **C4** | `IsExposedOnSpawn`/`IsEditable` have **no compiler-side reader** — inert | grep |
| **C5** | The shared table **already renders `Role` and `Scope` columns**, and `IVariablesSchemaSource` already exposes `CountNodesReferencingVariable` | reading `VariablesPanelControl` |
| **C6** | Blueprints are already versioned documents — `DocumentMeta(Blueprint, 1)` — and `ScenarioMigrationModule` shows the pattern, ⚠ **including migrator PAIRS** | grep |
| **C7** | 🔴 `BlueprintCompiler` **shallow-copies**: `Variables`/`WorkingState`/`Parameters` are **the caller's list instances**, and Stage 2.5 splices into **shared `Graph` objects** | reading `BlueprintCompiler:35-52` |
| **C8** | `Parameters` is **absent from `VarFieldName` entirely**, and `WorkingState`+`Parameters` coexist legally ⇒ the one pair no rail separates | `BP-226` |

⭐ **C1 and C7 are the two that change the plan if wrong.** ⚠ **C7 in particular:** if the compiler
really shares those lists, **anything that writes to them during compilation escapes into the caller's
asset** — and that is a prerequisite for stage D, possibly a live defect today. **Establish whether
any production path hands `Compile` a live editor document.** ⛔ **The coordinator could not.**

---

## 3. ⛔ What the design documents did NOT do — the sweeps

⭐ **This is where the review earns the batch.** The coordinator reasoned about the model and the
editor and **never enumerated the consumers.**

| | |
|---|---|
| **3.1** ⭐⭐ | **A full census of every reader of `asset.Variables` / `WorkingState` / `Parameters`** — compiler, both emitters, lowering, editor, debug/inspector, comparison, generators, runtime. ⚠ **Stage D breaks every one of them.** *How many are there? Which are mechanical, which are not?* |
| **3.2** ⭐ | **The three parallel ORDER lists** — `VariableOrder`, `WorkingStateOrder`, `ParameterOrder`. ⛔ **The design does not mention them once.** Do they merge? Does order survive the migration? Is a persisted order even meaningful once one list is tagged? |
| **3.3** ⭐ | **The debug / inspector surface.** `BlueprintFieldDescriptor` carries name/type/offset/size per variable; a runtime inspector reads it. **What does a `Role`/`Scope` tag do to it — and to the debug map's schema version?** |
| **3.4** | **Comparison fixtures and the export path** (`BlueprintComparisonSanitizer`, `Comparison/Fixtures`). Does a model change churn them, and are they asserted byte-wise? |
| **3.5** | **The `Dispatch: 1` numeric assets** (`BP-227`, four of them). ⚠ **Do they survive a migration that rewrites the same file?** |
| **3.6** | **Round-trip.** A `Role`/`Scope` field on every decl changes **every** asset's JSON. What is the byte-identity guarantee today, and what does it become? |
| **3.7** 📐 | ⭐ **Is a stage 0 missing?** C7's shallow copy looks like a prerequisite for anything that writes asset-level lists during compilation. **If so, say so — it re-orders the plan** |

---

## 4. ⭐⭐ The stages — the least-confident part of the design

```
A  Variables becomes a third IVariablesSchemaSource; kill the `bool isParams`   editor only
B  Details hosts the shared table; My Blueprint routes selection into it        editor only
B′ unify the type-choice record so structs are offerable                        editor only
C  FindVariableIndex → (kind, index); VarFieldName switches — closes BP-226     compiler only
D  one declaration list with Role/Scope; rails restated                         model + migration
```

**The claimed ordering principle:** *everything that does not touch the asset format lands first, so
the migration is the last and only variable.*

📐 **Test that claim. Specifically:**

| | |
|---|---|
| **4.1** | ⭐ **Is A really editor-only?** `BlueprintVariableSchemaSource` takes `bool isParams`; making it three-way means a third `Variables` source. **Does anything else consume that constructor?** |
| **4.2** | ⭐⭐ **Is C really independent of D?** `FindVariableIndex` returning `(kind, index)` **while the model still has three lists** — does the kind come from anywhere real, or is C only meaningful after D? ⚠ **If C depends on D, the ordering claim is wrong and `BP-226` stays open longer than advertised** |
| **4.3** | **Is B′ separable from B?** |
| **4.4** | ⭐ **Is D one stage or several?** One list + a tag + a migration + three rails restated + every consumer in 3.1 — **that may be a programme, not a batch.** If it splits, propose the split |
| **4.5** | **Revert-goes-red per stage** — can each be reverted independently once the next has landed? |

---

## 5. 📐 Design questions the review should pressure-test

⚠ These were **answered by the coordinator acting as architect** (delegated, NotebookLM not consulted)
in the two documents' §6/§7. ⭐ **They are rulings, not open questions — but a ruling built on a wrong
premise is worth catching now.**

| | ruling | the premise to test |
|---|---|---|
| `Graph.Inputs` **out** | passed, not stored | does anything treat a graph input as storage? |
| `Scope` = **two values** | the blackboard's `Node/Behavior/Entity` is a different axis | is it? |
| Instance gets **no `Input` channel** | `IsExposedOnSpawn` is inert (**C4**) | is there a spawn path that would want it? |
| **one** table, `Scope` a column | `DetailsTarget` is already scope-aware (**C2**) | |
| struct picker = **list union**, FQNs only | **C1** | ⭐ **and can `BP87_TypePickerTests`' lock be extended to assert end-to-end compilation rather than `TryResolve`?** |

---

## 6. Reporting

⭐ **Your assessment document is the deliverable** — link it in your report.

Also: **gates at start and end** · ⭐ **every `C#` claim you re-measured, with the verdict** · ⭐ **every
id you allocated** (rule 5) · **your 3.7 and 4.2 answers**, which are the two that re-order the plan ·
⭐ **your §1.6 verdict, stated plainly** · **anything in the two design documents that is wrong against
the code.**

⚠ ⭐ **Say what you could NOT establish.** A review that reports only what it proved, and stays silent
on what it could not reach, is the same defect as a `default:` arm that returns a plausible value.
**The coordinator failed to establish whether any production path hands `Compile` a live asset (C7) —
that gap is stated rather than hidden, and yours should be too.**
