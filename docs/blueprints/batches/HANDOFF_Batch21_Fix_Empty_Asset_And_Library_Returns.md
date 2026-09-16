# HANDOFF — Batch 21: BP-103 (empty asset crashes + breaks the build) · BP-104 · BP-105

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> ⚠ **All three items come from the user driving the editor after Batch 20.** Batch 20's own gates were
> green and its code reviewed clean — these are the defects only a human at the UI could find.

---

## 0. ⚡ Model delegation — read this first

You are on **Opus**. **Delegate to Sonnet sub-agents anything that does not need Opus-level reasoning.**
Tokens are the binding constraint; this is a requirement.

| Work | Model | Why |
|---|---|---|
| **BP-103** seeding a starter graph into the blank templates | 🟢 **Sonnet** | `CreateFunctionGraph` already exists and already seeds an entry node — this is wiring it into a table row |
| **BP-103** tests (create → open → no throw, per template) | 🟢 **Sonnet** | Contract is stated below |
| **BP-104** writing the missing Roslyn test | 🟢 **Sonnet** | Mirror `MultiOutput_PassesRoslyn_EndToEnd`, set Library dispatch |
| **BP-105** rendering only the applicable half of the Return panel | 🟢 **Sonnet** | One conditional in an existing drawer |
| **Which graph an `Instance` blank template should seed** | 🔴 **Opus** | Shipped assets disagree — see §2 |
| **BP-104: whether `Library` belongs in that branch at all** | 🔴 **Opus** | Compiler semantics; a wrong call here is a silent wrong-value bug |
| **Diff review · gate runs · revert-goes-red** | 🔴 **Opus** | Never delegate verification |

⚠ **Delegation does not transfer the verification duty.**

---

## 1. Context

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch** | `claude/blueprint-macro-feature-sdmspn` — **push here**; rebase/merge from the coordinator branch first |
| **Coordinator branch** | `claude/blueprint-authoring-status-6sr5ld` — docs/tracker; do **not** push here |
| **Truth** | [Tracker](Blueprint_Issues_Tracker.md) · [Detail](Blueprint_Issues_Detail.md) |

**Shared-file protocol:** `Blueprint_Issues_Tracker.md` and `Blueprint_Issues_Detail.md` are **yours**
for this batch — mark rows, add `DONE` notes, reconcile counts three ways. The coordinator holds off.

⚠ **`BP-92` has been REOPENED.** Its dispatch choice is correct and reviewed clean; it shipped a door
into a pre-existing hole. It ticks again when a new Library opens without throwing.

**Standing rules:** verify claims against code (the audit register has been wrong ten times) · fix,
don't disable · record findings in the detail doc · **revert your fix and confirm the test goes red**.

---

## 2. 🔴 BP-103 — a blank-template blueprint has zero graphs

📄 [detail](Blueprint_Issues_Detail.md#bp-103) · **do this first; it blocks re-testing everything else**

**Reproduced by the user.** Creating a Function Library threw:

```
Blueprint asset 'FuncLib1' has no graphs
```

`MakeEmptyBlueprint` synthesises an asset with **no `Graphs`**, nothing adds one, and
`BlueprintDocumentFactory:138` throws when `ResolveInitialGraph:1605` returns null.

⚠ **Pre-existing, not a Batch-20 regression** — verified against `8fbe18dd`: the old `Empty` template
had the identical hole. Do not treat this as "undo BP-92".

⚠⚠ **The worse half:** the file is written *before* it is opened, so the crash leaves a persistent
asset. An empty `Library` then fails compile with **BP5001**, and the blueprint compile is an MSBuild
step of `Hrot.AI.Behaviors` — **so `dotnet build` of the solution fails until the file is deleted.**
The editor currently offers a create path that bricks the build. That is the severity here, not the
dialog box.

### Fix — seed a starter graph per blank template

One more field on the `BlankTemplateRow` table BP-92 introduced. ✅ **Reuse
`BlueprintDocumentFactory.CreateFunctionGraph`** (`:1682`) — it already builds a `GraphKind.Function`
graph seeded with an `EventEntryNode`.

| Template | Seed |
|---|---|
| `Function Library` (Library) | a Function graph — e.g. `NewFunction`. **Settled**: BP5001 requires ≥1 Function graph, so this is forced |
| `Empty` (Instance) | 🔴 **Opus call — see below** |

### 🔴 The Instance seed is a real decision, not a default

Shipped Instance assets **disagree**, and the difference is load-bearing:

| Asset | Graph |
|---|---|
| `CountingDemo`, `CollectionWriteDemo`, `EditorTypesDemo` | `Tick` / **Function** |
| `CoverAwarePatrol`, `HealthThresholdReaction`, `SquadAwareEngagement` | `Tick` / **Event** |

And `InstanceEmitter.cs:81` selects the tick graph as `Kind == Function && Name == "Tick"`, falling
back to *the first Function graph*. **A `Tick`/Event graph does not match that lookup** — those assets
must reach the runtime by another path.

⇒ **Determine which form a new Instance blueprint should get, from the emitter's behaviour, not from
which is more common.** Whichever you pick, a new `Empty` asset must **compile cleanly on creation** —
that is the acceptance test, and it is the thing nobody checked for the old template either.

### Done when

- [ ] Creating **either** template opens without throwing.
- [ ] Creating either template and compiling immediately succeeds — **no BP5001, no build break**.
- [ ] A test per template covers create → open → compile. ⚠ The absence of this test is why a
      years-old crash shipped; the test matters as much as the fix.
- [ ] `git status` is clean after the test run — no stray `.bp.json` left in the assets tree.

---

## 3. 🔴 BP-104 — SUSPECTED: a Library function's outputs are ignored

📄 [detail](Blueprint_Issues_Detail.md#bp-104)

⚠ **Reproduce before fixing.** This is read from source and **not confirmed** — the coordinator could
not run the editor.

`Stage5.BuildReturnTerminator:1907-1913` takes the `AiPrimitive` branch for **`Library` too**, returning
`IrTerm_ReturnStatus` and never reading `graph.Outputs` — while `LibraryEmitter.CSharpReturnType`
declares the method's return type **from those same Outputs**. A Library function with one `float`
output would emit `return NodeStatus.Success;` from a method declared `static float`.

**Step 1 — reproduce.** After BP-103, add an output to a function in a Function Library and compile.

**Step 2 — the missing test, regardless of outcome.** Trap #9 exactly:

| Test | Library dispatch? | Runs Roslyn? |
|---|---|---|
| `LibraryAdapter_WritesOutputsSequentially_NotAsABlittedTuple` | ✅ 2 outputs | ❌ `CompileOk` asserts `Succeeded`, which **does not run the C# compiler** |
| `MultiOutput_PassesRoslyn_EndToEnd` | ❌ Instance | ✅ |

**No test compiles Library-with-outputs through Roslyn.** Add one via
`BlueprintTestFixture.CompileAndLoad` — that is the only assertion that proves the emit is valid C#.

**Step 3 — 🔴 Opus: if confirmed, decide what `Library` should do.** Either it belongs in the
`AiPrimitive` branch (and declared outputs on a Library function must then be a **Stage 2 error**, not
silently dropped), or it does not (and it should take the value-return path like `Instance`).
⚠ `LibraryMath.bp.json` has **zero** outputs, so no shipped asset constrains the answer — which is
exactly why this survived. **State your reasoning; do not pick the smaller diff.**

---

## 4. BP-105 — the Return panel shows a control that does nothing

📄 [detail](Blueprint_Issues_Detail.md#bp-105)

> User: *"Status offers an editable combo; but what is the purpose of it?"*

`BuildReturnTerminator` uses **exactly one** of the two, by dispatch:

| Dispatch | Uses | Ignores |
|---|---|---|
| `Instance` | declared **Outputs** | **`Status`** |
| `AiPrimitive` · `Library` | **`Status`** | **Outputs** |

⇒ On an Instance function graph, the `Status` combo Batch 20 added is **live and inert** — trap #5,
introduced while fixing a different member of the same family.

**Scope for this batch — the UI half only.** Render only the applicable section and label it; the
drawer already resolves the containing graph, so the asset's dispatch is in reach. Keep it honest: if
`Status` is inert here, do not draw it.

⚠ **Out of scope:** making `Status` a data-**in** pin. The user is right that a baked compile-time
status cannot express `Running`, which is a runtime decision — but that changes the AiPrimitive
contract and needs an architect view. **Do not build it. Do not silently leave the inert combo either.**

---

## 5. Gates

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

**Baseline, measured by the coordinator on the merged Batch-20 tree:**
build **0 errors** · Blueprints **2889 passed / 0 failed / 10 skipped** · AiShared **1213 / 0** ·
BTree **612 / 0**. Known flakes: `PdbEmbeddedSourceTests`,
`WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick` — re-run the single filter before calling
either a regression. Classify with `git stash` → re-run → `git stash pop`.

---

## 6. Reporting back

⚠ **Batch 20's report omitted all three of these.** They are required:

1. **The gate numbers you actually ran** — not "gates green".
2. **Which tests you reverted the fix against, and that they went red.**
3. **What you delegated to Sonnet and what you kept** — so the split can be tuned.

Plus, as always: anything that contradicts this handoff (corrections are worth more than compliance),
and anything you deliberately did not do.

⚠ **Register what you leave behind as a tracker row, not as a note inside a `DONE` block.** Batch 20
declared the Graph-Signature undo gap honestly but buried it, so it appeared in no count and no
priority list; the coordinator had to lift it out as `BP-102`.

**Definition of done:** gates green vs the baseline above · tracker rows `[x]` with `DONE` notes ·
counts reconciled **three ways** · pushed to `claude/blueprint-macro-feature-sdmspn`.

⚠ **Do not create a pull request.**
