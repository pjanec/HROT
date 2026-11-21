<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 96 report.
stale-below: nothing.
known-rot: none.
known-conflict: none. §4 reverses part of Batch 94's 94c, deliberately, exactly as the
  handoff's own STATUS block said it would.
-->
# REPORT — Batch 96: **make the dialog real, and unfreeze the pin**

> 📌 **Handoff header says `Dispatched at a2f93954c`** — ⚠ **but it was AMENDED after that**, by
> `fcd0292` *(rule 1a: my remote head was `9a0c970`, which did not contain `a2f93954c`, so the
> ancestry check correctly said "not started")*. ⛔ **The STATUS block was not re-stamped.**
> ⭐ **I built the CURRENT content and froze my scope at `fcd0292`**, which is also my rule-1b marker
> *(`f7b4148`)*. ⭐ **Nothing landed on the coordinator branch during the run** — re-fetched before this
> commit, `HEAD..origin/claude/blueprint-authoring-status-gm0akp` is **empty**.
> ⭐ **Base for every RED = `fcd0292`.**

| item | |
|---|---|
| ⭐ **`96a`** | ✅ **the modal opens the table the drawer requires** |
| ⭐ **`96b`** | ✅ **a variable's name stops being a path inside its own value** |
| ⭐ **`3b`** | ✅ **OK actually writes · a refusal names its real cause · a view is shaped as a view** |
| ⭐ **`96c`** | ✅ **the Watch pins the camera, not the photograph** |
| ⛔ **`96d`** | 🛑 **STOPPED AND REPORTED** — §5, on the handoff's own condition |
| ⛔ **`96e`** | **NOT STARTED** — its condition *("only if `96a`–`96d` are green")* is not met |

⭐ **IDs allocated (rule 3/5): `BP-353` · `BP-354` · `BP-355` · `BP-356` · `BP-357` · `BP-358` ·
`BP-359` · `BP-360`.** ⛔ No others, and no ledger row this batch.

---

## 1. ⭐⭐⭐ WHAT THE USER WILL SEE — **and the one thing they still will not**

| user's report | |
|---|---|
| ① *"the dialog opens with nothing to edit"* | ✅ **two causes, both fixed** — `96a` *(no table)* **and** `96b` *(an empty scope)*. ⚠ Either alone leaves it empty |
| ③ *"Properties… CRASHES the editor"* | ✅ **same cause as ①** — `TableNextRow()` with no table open |
| ② *"OK says this row cannot be written"* | ✅ **fixed, and it was NOT the classifier** — §3 |
| ⑤ *"the Watch row stays 0"* | ✅ **fixed** — §4 |
| ④ *"no live writer is installed"* | 🛑 **still true, and now measured** — §5 |
| ⛔⛔ **NEW, and it blocks the same click** | 🔴 **a SCALAR variable's edit still goes nowhere** — `BP-356`, §3.3. ⚠ **The user's `Count` is an `int`** |

⇒ ⚠⚠ **Say this plainly before the next visual check:** the dialog will now **open, draw and not
crash** on every host, and for a **DTO** variable an edit will land. ⛔ **For a plain `int` the input
will draw and typing will change nothing** — that is `BP-356`, it is a `StructEdit` root-binding gap,
and it is asserted on purpose rather than papered over.

---

## 2. 🛠 `96a` — **the modal never opened the table the drawer requires**

### 📐 The contract, and the one caller that broke it

```
ComponentEditDrawer.cs:41   /// Must be called inside a two-column BeginTable/EndTable block.
…               :241   DrawLeafNode, first statement:  ImGuiApi.TableNextRow();
VariableEditModal.Draw      Separator → DrawEditNode → Separator      ⛔ no table in the file
```

| gesture | ⇒ |
|---|---|
| *"Edit value…"* | the scope filtered the document to an **empty `SelectionRoot`** *(`96b`)* ⇒ zero children ⇒ `TableNextRow` never reached ⇒ ⭐ **a name and two separator lines** |
| *"Properties…"* | the real node survived ⇒ ⛔⛔ **the first `TableNextRow()` with no table open ABORTED THE EDITOR** |

### ⭐ Which caller I mirrored, and what the rail is

⭐ **`ComponentEditWindow.DrawClientArea` steps 2–5** — the one the handoff named. ⭐ **Rebuild BEFORE
the table** *(inside it, `DrawEditNode`'s own `RebuildRequired` early return would draw an empty table
for ever)*; `EndTable` inside the `if`, so it is reached on every path that opened one.
⛔ **`ComponentEditDrawer` is untouched.**

⭐⭐ **The rail is the FAMILY**, `EveryDrawerCallSiteOpensItsTableTests`: 📐 the graph **plus** a grep
cross-check enumerate **SIX** production call sites — `ComponentEditWindow:152` · `ComponentReflector:406`
· `ReplaySearchPanel:155` · `InspectorWindow:303` · `InspectorWindow:404` · `VariableEditModal:200` —
and ⭐ **five were already correct.** ⚠ **The graph alone missed the two `InspectorWindow` sites**
*(cross-assembly)*; the grep found them. The rail sweeps the repository so a **seventh** caller fails
until it is listed.

> ⛔⛔ **THE DRAW ITSELF IS UNRAILED, and I am saying so plainly.** 📌 `R-21`/`R-62`: no headless rail
> can drive ImGui. This asserts the **shape of the call site in the sources**, not that a row appears.
> ⭐ It is the strongest thing available at the layer six green batches kept shipping defects into;
> ⛔ **it is not proof the dialog renders.**

---

## 3. 🛠 `96b` + `3b` — **the scope, the write, and the shape**

### 3.1 ⭐⭐⭐ `96b` — a variable's NAME is not a path inside its own VALUE

📐 `ScopeFor` built `ForField("$.Count")` from the variable's name; `OpenSession` opens over **the
VALUE**, so the root **is** the value at `$` ⇒ `"$.Count"` asked for a field named `Count` *inside the
`int`*. ⇒ ⛔ **wrong for every variable on every host.**

| ⭐ what `ScopeFor` returns now | |
|---|---|
| **a whole-variable edit** *(the only production call)* | `EditScope.WholeComponent`, for **both** actions |
| **a real sub-path** *(a field INSIDE a DTO variable)* | `EditScope.ForField` — ⭐ **the arm is KEPT**; ⛔ no production caller passes one yet, and that gesture does not exist |

⚠⚠ **Batch 75 fixed the SPACE and not the PREMISE** — `"Count"` → `"$.Count"` — and the rooted path was
just as empty.

> ⭐⭐⭐ **AND THE ONE RAIL THAT ASSERTED THE RESULTING DOCUMENT USED THE ONE FIXTURE IN WHICH THE BUG IS
> INVISIBLE.** `VariableEditGestureBinderTests` declares a variable named `Count` whose type is
> `DemoVar { int Count; float Speed; }`, so `"$.Count"` **did** match a node — **the DTO's own `Count`
> member** — and the rail read the wrong node as success. ⇒ **that is why four batches passed over it.**

⭐ **THREE rails inverted, none deleted**, each with the diagnosis inline. ⭐ The new rail counts the
nodes the drawer would visit, through the production launcher, **including that coincidence**.

### 3.2 ⭐⭐⭐ `3b` — the handoff's question had a THIRD answer

> ⭐ Handoff: *"is the user's `Count` genuinely `RowKind != Normal` or `IsStale`? **If a hand-authored
> blueprint `int` classifies as node-owned, the CLASSIFIER is the defect.**"*

📐 **Neither. The classifier is correct.** `VariableEditGestureBinder`'s **`assetOf` parameter had ZERO
production call sites** *(only two tests ever passed one)* ⇒ `CommitInitialValue` hit
`if (asset is null) return Outcome.RefusedReadOnly;` ⇒ ⛔ **every OK refused, on every host, for every
row** — and told the designer *"node-owned, a passthrough, or stale"* about an ordinary variable.
📌 **`R-67`, the SEVENTH instance, two lines from the sixth.**

| ⭐ built | |
|---|---|
| **`assetOf` wired** | from the store the registrar already holds, ⭐⭐ **keyed on the ROW's asset id** — ⚠ the Watch mixes rows from arbitrary assets, so *"whatever is open"* would land an edit **in the wrong asset**, silently, with an `Ok` |
| **`RefusedNoDeclarationOwner`** | its own outcome and its own sentence — 📌 *"same information value, no false expectations"*; ⛔ a refusal that misnames its cause sends the designer to fix the wrong thing |
| **the read-only VIEW** | a row that can **never** be written opens with **no OK** and the reason in the body. ⚠ **A free-running refusal keeps its greyed OK and tooltip** — that one is ACTIONABLE *(pause and it works)*, and the two must stay different |

⚠ **Blueprint still resolves to `null`**: the write target is typed `IBlackboardManagedAsset` and
`BlueprintAsset` is not one — 📌 the same vocabulary mismatch `95a` fixed for READING, still open for
WRITING. ⭐ It now **says so** instead of blaming the row.

### 3.3 ⛔⛔ `BP-356` — **and a scalar's edit STILL goes nowhere**

📐 `ReflectionEditDocumentBuilder.CreateLeafBinding` opens `if (fi == null && pi == null) return null;`
— **a binding needs a MEMBER**, and a document's ROOT has none.

| variable | root | ⇒ |
|---|---|---|
| **DTO** | `Struct`, children bound | ✅ editing works *(asserted)* |
| ⛔ **scalar** | **IS** the leaf, `Binding == null` | ⛔⛔ `DrawLeafNode` ends `node.Binding?.SetBoxed(value)` — **a null-conditional that discards the typing**; `Commit()` can only return the seed |

⚠⚠ **That is the user's exact case.** ⭐ **Asserted on purpose** by `AScalarVariablesEditGoesNowhere` —
⚠ **flip it when fixed, do not delete it.** ⛔ **Not fixed here:** the fix is a root binding in
**`StructEdit`** *(`FDP/ExtDeps`, its own suite)* whose blast radius is every scalar-rooted edit session
in the editor. ⭐ **A cheaper alternative worth weighing first**, entirely inside `Hrot.Editor.AiShared`:
open a scalar through a one-field wrapper, which also reads better *(a labelled `Value:` row)*.

---

## 4. 🛠 `96c` — **the Watch pins the camera, not the photograph**

### ⭐ The decision: **(a)**, and (b) was measured and rejected

⛔ **(b)** *(the rewrite reading through the cache ENTRY)*: the pinned row's arms would point at the
**Details panel's** cell, so the Watch would track whatever that panel last sampled — and the Watch's
own sampler would then cache **a read of another panel's cache**. 📌 That is exactly the coupling
`R-103` *("rows know nothing about each other")* forbids, and which `VariableRowSampler`'s own
doc-comment already rules out.

⭐ **(a)**: `VariableTableModel` keeps this frame's source rows beside the sampled ones ·
`VariableTableView.SourceOf` un-samples by `Origin.Key` *(failing **OPEN** for rows it never sampled)* ·
`VariableTableControl` raises the toggle with the **source** row.

### ⭐⭐⭐ What the inverted rail asserts — **and whose object it takes**

> 📌 The handoff's §1 rule, earned here: *"a rail must take its input from the SAME OBJECT the UI takes
> it from."*

⭐ `APinnedVariableFollowsTheRunAcrossBehaviourFrames` now **builds the Details view, takes the row IT
holds, and pins through `VariableTableControl`'s own toggle seam** — ⛔ it no longer pins
`details.GetRows()[0]`. ⭐⭐ **`RaiseWatchToggleForTest` takes the VIEW**, exactly as `DrawRowMenu` does;
🔴 the old signature took a bare row, **which is precisely what let the rail diverge from the product.**

---

## 5. 🛑 `96d` — **STOPPED AND REPORTED**, on the handoff's own condition

> ⭐ Handoff §5: *"⛔ do not invent one. ⭐ Measure what already writes a live blackboard value… ⚠ **If no
> writer exists anywhere, STOP AND REPORT — that is a capability, not a wire**."*

📐 **Measured, per host:**

| host | live READ | live WRITE (bytes) | ⭐ name → offset, for writing |
|---|---|---|---|
| **Blueprint** | `BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot` | ⭐⭐ **`IBlueprintDebugSession.TryWriteWorkingStateField` — REAL production code** *(Batch 84 row `59c`; stages via `IDataBreakpointManager.StageFieldMutation`, refuses unless frozen)* ⛔ **ZERO production callers** | ⛔⛔ **PRIVATE inside `BlueprintDebugSession`** *(`layoutFields` / `def.StateFields`, used by the READ)* ⇒ a `WriteLiveValue(row, bytes)` **cannot be built from outside** |
| **BTree / HSM** | `LiveBlackboardValueProvider.GetLiveBytes` | ⛔ **none, anywhere** | ⚠ `TryResolve` gives `(Type, ByteOffset)` — ⛔ **within `BehaviorParameters`, NOT within the component** |

⇒ ⛔⛔ **The remaining arithmetic is exactly what `Q32` §2.1 forbids guessing:** *"an out-of-range
offset is MEMORY CORRUPTION, not a wrong value."*

| ⭐ what a `96d` batch should build | |
|---|---|
| **Blueprint — ONE SEAM** | a public `TryWriteWorkingStateField(entity, assetId, fieldName, bytes)` on `IBlueprintDebugSession`, implemented with the walk the READ already does. ⭐ Then `writeLive` is a wire |
| ⛔ **BTree/HSM — a CAPABILITY** | there is no live write path at all, and the component-relative offset does not exist as a seam |

⭐ **The designer's message is already honest** — `LiveWriteUnavailable`: *"no live writer is installed
for this host."* ⇒ ⭐ **filed as `BP-358`.**

⛔ **`96e` NOT STARTED** — its own condition is *"only if `96a`–`96d` are green"*, and `96d` is a
measured stop. 📄 Filed complete as **`BP-360`** so a later batch starts from the measurement.

---

## 6. ⭐⭐ REVERT-GOES-RED — per item, never delegated

| probe | un-applied | result |
|---|---|---|
| **P1 · `96a`** | the `BeginTable`/`EndTable` wrapping in the modal | 🔴 **2 of 5** — the family rail **and** the mirror rail |
| **P2 · `96b`** | `ScopeFor(action)` → `ScopeFor(action, row.Origin.VariablePath)` | 🔴 **3 of 5**. ⚠ `Properties` stayed green *(it never used the path)* and the **commit** rail stayed green — ⭐ **because `Commit()` returns the whole buffer regardless of scope**, which is why a commit-based rail could never have caught this |
| **P3 · `3b`** | `assetOf: null` at the registrar | 🔴 **2 of 5** — both host arms of *"OK writes the declared default"* |
| **P4 · `96c`** | the control raises the sampled row instead of `view.SourceOf(row)` | 🔴 **`APinnedVariableFollowsTheRunAcrossBehaviourFrames`** — ⭐ **the rail that was green for the whole defect** |

⭐ **All four un-applied with the INVERSE EDIT** *(⛔ never `git checkout --`)*, each re-confirmed green.
⭐ **Working tree clean after every suite run.**

---

## 7. ⭐⭐ GATES — the seven-row contract

| # | gate | result | Δ vs `fcd0292` | `--no-build`? |
|---|---|---|---|---|
| 1 | AiShared | **1560 / 0 / 0** | **+15** | ✅ |
| 2 | BTree.Editor | **622 / 0 / 0** | 0 | ✅ |
| 3 | Hsm.Editor | **554 / 0 / 0** | 0 | ✅ |
| 4 | AiEditor.Generators | **277 / 0 / 0** | 0 | ✅ |
| 5 | AiEditor.Persistence | **143 / 0 / 0** | 0 | ✅ |
| 6 | Blueprints | **3801 / 0 / 10 skip** | **+5** | ✅ |
| 7 | Hrot.Editor | **201 / 0 / 0** | 0 | ✅ |
| 8 | Breakpoints | **143 / 0 / 0** | 0 | ✅ |
| 9 | NodeEditor.Core | **211 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 10 | NodeEditor.UI | **135 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 11 | Fhsm | **300 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 12 | `Fdp.Presentation` *(`BP-337`, `~…WindowManager`)* | **146 / 0 / 0** | 0 | ✅ |
| 13 | `Fdp.Toolkits --filter CognitiveRuntimeModuleTests` | **1 / 0 / 0** | 0 | ⛔ **`--filter` ONLY** *(`DEBT-AIB-030`)* |
| 14 | Blueprints `--filter Benchmarks` | **8 / 0 / 1 skip** | 0 | ✅ ⭐ green on both runs this batch |
| ⭐ **15** | **`StructEdit`** *(run because `96b`/`BP-356` reason about it)* | ⚠ **191 / 1 / 0** | 0 | ⛔ **NO — out of solution** |

⚠ **The ONE red, confirmed PRE-EXISTING against `fcd0292`.**
`StructEdit.Tests.Reflection.DocumentBuilderTests.Build_CircularReference_CircularFieldIsUnsupported`
— a `System.Text.Json` cycle exception. 📐 **Verified by building a worktree at the base sha and running
that single test: RED there too.** ⭐ This batch touches **no** `StructEdit` source *(the diff is 19
files, all `Hrot.Editor.AiShared`, `Hrot.Blueprints.Tests` and their tests)*. ⚠ It is **not** in Batch
95's baseline because that suite has never been gated — ⭐ **I ran it deliberately** since `BP-356`
makes a claim about that very builder.

⭐ **Quarantine: Blueprints 10 skip, everything else 0. ⛔ NO NEW SKIP.**
⭐ **No golden movement** — no emitter and no asset is touched; `git status` clean after every suite.

### ⭐ 7b — the scripts, **UNFILTERED**, with `EXIT`

```
$ python3 scripts/tracker-counts.py --check
tracker counts OK — open 78 / done 217 (+1 refuted)
EXIT=0                     ⭐ the summary table was corrected in the same commit as the rows

$ python3 scripts/rulings-check.py
70/70 rulings verified against their sources
EXIT=0                     ⭐ no ledger row added this batch; no staleness warning
```

---

## 8. ⭐ PER RAIL — **whose object the input came from** *(handoff §1)*

| rail | ⭐ the input's OWNER | ⛔ faked |
|---|---|---|
| `EveryDrawerCallSiteOpensItsTableTests` | **the repository's own sources**, swept | ⛔ nothing — ⚠ **but it is a SOURCE-SHAPE rail; the draw is unrailed** |
| `TheDialogHasSomethingToDrawTests` | **the production `VariableEditLauncher`**, its real edit service and real document | ⛔ nothing |
| `TheEditDialogIsDrawnTests` *(read-only shaping)* | **the production `VariableEditModal` + binder**, asked for the decisions `Draw` branches on | ⚠ the DRAW — 📌 `R-21`/`R-62` |
| `TheEditActuallyLandsTests` | **the real `EditorSubsystem`'s registrar, binder, launcher and selection store**; the row from the production `BlackboardSectionRowSource` | ⚠ the ASSET is `TestManagedAsset` — `HsmAsset`'s ctor is internal and `BehaviorTreeAsset`'s needs an `Fbt` blob; every path here is typed on the INTERFACE |
| `TheWatchGoesLiveTests` *(inverted)* | ⭐⭐ **the row the DETAILS VIEW holds**, pinned through `VariableTableControl`'s own toggle seam — 📌 the same object the UI uses | ⛔ nothing at that seam; ⚠ the live map is a test dictionary |

---

## 9. ⭐ WHAT THE NEXT PASS SHOULD KNOW

| | |
|---|---|
| ⛔⛔ **`BP-356` will be hit on the first click** | a plain `int` draws an input and typing changes nothing. ⭐ **Tell the user before the check** so it is not reported as a fourth mystery |
| ⭐ **`BP-359`** | *"Properties…"* opens the VALUE, not the DECLARATION — it has never edited properties, and the two menu items now differ by nothing |
| ⭐ **`BP-358`** | the live writer: Blueprint is **one seam** away, BTree/HSM need a **capability** |
| ⭐ **`BP-360`** | the outline's Watch entry — measured and filed, not started |
| ⚠ **Carried, untouched** | `BP-342` · `BP-345` · `BP-346` · `BP-348` · `BP-352` · `BP-337` *(half-fixed)* · `94g` |
