# REPORT — Batch 82: **`U-6` — Details hosts the shared table**

> 📌 **Started at `ea36baa`** *(rule 1b marker, pushed before any code)*, on dispatch `0973760ca`
> ff-merged at `444b4b2`. ⭐ **Rule 4:** re-pulled the coordinator branch before the final commit.
> ⭐ **IDs allocated: `BP-316` `BP-317` `BP-318`.** ⭐ **`DEBT-AIB` rows touched: NONE.**
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.
> ⭐⭐ **All three items shipped**, including both cheap side items.

---

## 0. ⭐⭐⭐ Your "MEASURE FIRST" was right — **there is no shared Details host, and it is worse than that**

📐 **Measured, and it is all I found:**

| | |
|---|---|
| windows titled **`"Details"`** in the whole repo | ⛔ **exactly ONE** — `BlueprintDetailsWindow`, registered by `EditorSubsystem:2981` on **Blueprint only** |
| its shape | `sealed`, in `Hrot.Blueprints.Editor`, blueprint-specific **by construction** *(`BlueprintAsset`, `BlueprintNodeDrawerRegistry`, `BlueprintNodeSelection`)* |
| what BTree / HSM have instead | `InspectorWindow` — ⚠ **which itself exists TWICE**, as you measured |

⇒ ⭐⭐ **Ruling 6 wants one panel across three perspectives, and two of the three have no Details panel
at all.** ⛔ **Not "the host is in the wrong assembly" — it does not exist.**

### ⭐⭐⭐ And there is a generic Details host that nobody uses

📐 **`NodeEditor.UI.Panels.DetailsPanel`** is a host-agnostic panel driven by
`IDetailsViewProvider`/`DetailsTarget`. ⭐⭐ **Its target vocabulary already names ruling 2's exact
split:**

```csharp
// NodeEditor.Core/Interfaces/IDetailsViewProvider.cs:76,81
public sealed record Variable(string VariableId) : DetailsTarget;
public sealed record LocalVariable(string FunctionId, string LocalId) : DetailsTarget;
```

⛔⛔ **Its only construction site in the repo is `NodeEditor.Demo/DemoShell.cs:833`.**
⚠ *"Unreferenced is not unintentional"* ⇒ ⛔ **not deleted, and NOT adopted here either**: rewriting
`BlueprintDetailsWindow` onto a different abstraction is far beyond *"unchanged and small."*
📌 **Filed as `BP-317` and pointed at sequencing row 61**, which already owns the cross-host host.

### ⇒ ⭐ I took your split rule

📌 *"Landing it on ONE perspective first, proven, is a legitimate outcome… ruling 6 says the panel is
reused, it does not say all three must land in one batch."*

⭐⭐ **Landed on Blueprint.** ⛔ **But the shared half is genuinely shared** — `VariableDetailsSection`
and both routing interfaces are in **`Hrot.Editor.AiShared`**, so a BTree/HSM details host wires itself
by implementing `IVariableDetailsHost`. ⚠ **Perspectives done: Blueprint. Not done: BTree, HSM** —
because there is nothing there to host it in.

---

## 1. 🛠 Item 1 — **placement** *(`BP-316`)*

⭐ **You were right that this is placement, not construction.** The new type is **thin**:

| | |
|---|---|
| `VariableDetailsSection` *(AiShared)* | owns a `VariableTableModel` + Track C's `VariableTableControl`; `Show(heading, source)` / `Clear()` / `Draw(id)` |
| ⛔ **NOT a `ManagedWindow`** | ⭐ that would have forced a **second** Details window onto Blueprint, which already has one — and Batch 81 has just finished cleaning up what two windows claiming one identity does |
| the rail for ruling 9 | asserts the hosted control **is** `VariableTableControl`, **from `Hrot.Editor.AiShared`** — ⛔ a blueprint copy fails it |

⚠ **Fixed in passing, and it matters:** `SectionVariableRowSource` **required** a byte reader and
hard-coded `HasEverBeenWritten: true` ⇒ every authored row would have rendered `<unreadable>`,
**claiming a decode failure that never happened.** ⭐ Reader now optional,
`HasEverBeenWritten: reader != null` ⇒ `(pending)` — **the rule `BlackboardSectionRowSource` already
followed.** ⛔ **The value's RUN-STATE meaning is still row 58's**, untouched.

---

## 2. 🛠 Item 2 — **ruling 2's routing** *(`BP-316`)*

⭐ **Reconciled, not duplicated.** Track C keys on the section; ruling 2 keys on global-vs-local — and
**the section id carries that distinction**, so one mechanism serves both *(ruling 9)*.

| clicked | resolves to |
|---|---|
| **Variables** / **Inputs** / **Working State** | that section's declarations, via `BlueprintVariableSchemaSource` |
| **Local Variables** | ⭐ the **current graph's** locals, via `BlueprintLocalVariableSchemaSource` — heading names the graph, and it **follows the canvas by delegate** |
| Graphs / Functions / Macros / Custom Events | ⛔ **nothing** — the panel lets go rather than leaving a stale list |

### ⚠ One stated deviation from ruling 2's wording, with its basis

📌 Ruling 2 says a global click yields *"the list of **globals / working state**"* — **one merged
list.** ⛔ **Not merged here.** 📌 **`Q39` settles that `Variable` ≡ `WorkingState` and rules the merge
is stage `D`**, *"the only risky stage"*, with its own batch and a JSON migration.
⇒ ⭐ **merging in the ROUTER would do stage `D`'s job in the UI layer and have to be undone**; routing
per section **collapses by construction** the day the sections do.

### ⭐⭐⭐ The wiring is DERIVED — the fourth batch of this seam, and the third derivation

⛔ Batches 79, 80 and 81 each lost a surface to *"someone must remember to wire it."*
📐 **The composition root already hands the registrar BOTH windows** through `RegisterExtraWindow`
*(`:2969` outline, `:2981` details)* ⇒ ⭐ **the registrar connects them**, in either order, over the
**interfaces** — so a BTree/HSM host is wired the day it exists.

⭐ **This closes `BP-315`'s measurement.** Batch 81 measured `MyBlueprintPanel.SelectionChanged` as
having **zero subscribers repo-wide**, and `navigateToItem` skipping variables — *"nowhere sensible to
go."* **There is now somewhere to go.**

⭐ **Last selection wins in BOTH directions:** a variable click takes the panel from the node arm, a
**later** node click takes it back. ⛔ Merely asking *"is a node selected?"* would let a stale node
selection outrank a fresh variable click — that arm has its own rail.

---

## 3. 🛠 Item 3 — **both document repairs** *(`BP-318`)*

| | repaired | ⭐ measured before writing |
|---|---|---|
| **a** | **`BP1031` described as LIVE** in `DESIGN_Parameter_Model.md`, `Blueprints_Overview.md`, `EXPLAINER_Where_Parameters_And_State_Live.md` *(2 claims + a heading)* | ⛔ **retired at `Stage2_Validate.cs:168`** *(Batch 70, `BP-278`)*, **no production code raises it**, `V_DispatchKindCompatibilityTests` asserts `DoesNotContain` |
| **b** | **`DESIGN_Variable_Details_And_Editing.md` still ORDERED** the `InspectorWindow` STATIC PARAMETERS retirement | ⛔ **WITHDRAWN** *(`BP-295`)* — the premise was measured **inverted**; it is the only LIVE default-value surface |

⭐⭐ **Both `known-rot:` STATUS lines are CLEARED**, because the rot is gone rather than annotated.
⭐ **The EXPLAINER's `BP1031` reasoning is KEPT as the record of why it went** — marked so it no longer
reads as a live rail. ⛔ **No back-catalogue sweep.**

---

## 4. 🔴 Revert-goes-red — **per item, never delegated**

| probe | red |
|---|---|
| the registrar stops connecting outline→details | **3 / 18** |
| the local arm stops being distinguished | **2** |
| `HasEverBeenWritten` back to unconditional `true` | **1** |

⛔ Each un-applied by the **inverse edit**, never `git checkout --`.
⚠ **The first probe attempt did not compile** — deleting the two fields tripped `CS0649`
*(warnings-as-errors)*; the probe was reshaped to keep the assignments and skip only the connection.

---

## 5. Gates — **all seven reports**

### 1 + 2 · one row per gate, with the `--no-build` column

| gate | command | `--no-build`? | result | Δ |
|---|---|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | — | ✅ **0 err / 69 warn** | = |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | yes | ✅ **1330** | = |
| ⭐ **Blueprints** | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | yes | ✅ **3727 / 3737, 10 skipped** | **+18** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | yes | ✅ **615** | = |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | yes | ✅ **551** | = |
| Generators | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj --no-build` | yes | ✅ **270** | = |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | yes | ✅ **134** | = |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | yes | ✅ **136** | = |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | yes | ✅ **56 / 68, 12 skipped** | = |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | yes | ✅ **29** | = |
| ⚠ **Toolkits** | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | yes | 🔴 **3 → 2 → 0 across three runs** | ④ |
| ⭐ **NodeEditor.Core** | `dotnet test …/NodeEditor.Core.Tests.csproj` | ⛔ **NO** | ✅ **211** | = |
| ⭐ **NodeEditor.UI** | `dotnet test …/NodeEditor.UI.Tests.csproj` | ⛔ **NO** | ✅ **135** | = |
| ⭐ **Fhsm.Tests** | `dotnet test …/Fhsm.Tests.csproj` | ⛔ **NO** | ✅ **300, 0 skipped** | = |
| tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **open 66 / done 187 (+1 refuted)** | +1 / +2 |
| ⭐ **rulings** | `python3 scripts/rulings-check.py` | — | ✅ **35 / 35** | = |

### 3 · ⭐⭐⭐ Golden movement — **as a diff shape**

```
$ git diff --stat ea36baa..HEAD -- '*Snapshot*' '*Golden*' '*golden*' '*.cs.txt' '*persistence-shape*'
(no output)
```

⇒ ⭐ **ZERO baselines.** **13 files, +787 / −20** — ⭐ of which **+394 is the new rail file** and
**+134 the two new shared types**. ⛔ **No emitter, DTO, asset or compiler file is in the diff at
all**, so `persistence-shape`, the 43 `Emit/*.cs.txt` and `StructureHash` **could not** have moved.

| file | ± | |
|---|---|---|
| `DetailsHostsTheVariablesTests.cs` | **+394** | new — 18 rails, incl. the end-to-end production path |
| `VariableDetailsSection.cs` · `VariableOutlineRouting.cs` | **+94 / +48** | new — the shared drawable and the two interfaces |
| `BlueprintMyBlueprintWindow.cs` | +90 / −4 | ruling 2's resolver + `PublishSelection` |
| `BlueprintDetailsWindow.cs` | +77 / −4 | hosts the list; `ShowingVariables` precedence |
| `PerspectiveWorkspaceRegistrar.cs` | +36 | the derived connection |
| `VariableRowSources.cs` | +17 / −6 | the `(pending)` honesty fix |
| `BlueprintMyBlueprintModel.cs` | +7 | read-only `Asset` accessor |
| 5 docs + tracker | +51 / −6 | `BP-318` and the three new rows |

### 4 · every RED confirmed pre-existing — base `ea36baa`

| red | verdict |
|---|---|
| `Fdp.Toolkits.Tests` — **3, then 2, then 0** | ⭐⭐ **`DEBT-AIB-030`**, and the rotation is unusually visible this time. Run 3 fully green *(`1964 / 1964`)*, `--filter Gizmos` **`187 / 187`**. 📐 **`git diff --name-only ea36baa..HEAD -- FDP/` is EMPTY** ⇒ ⛔ **this batch's diff cannot reach that assembly** |

### 5 · working tree after every suite run

`git status --short` ⇒ **clean** *(only the intended edits before commit)*. ⛔ No golden regenerated.

### 6 · quarantine — **12 scenario · 0 FastHSM**, unchanged. ⛔ No new skip.

### 7 · **ids: `BP-316` `BP-317` `BP-318`** · **started at `ea36baa`**

---

## 6. ⚠ The standalone `AiVariablesWindow` — **KEPT, and here is `U-16`'s evidence**

📌 You asked whether it became redundant. ⭐ **Partly, and only on Blueprint:**

| | |
|---|---|
| ⭐ **what Details now does that it does** | renders the same control over the same rows, **routed by an outline click** |
| ⛔ **what it still does that Details does not** | it is registered on **all three** perspectives, and ⭐ **BTree/HSM have no Details window** *(`BP-317`)* ⇒ **retiring it there would leave no table at all** |
| ⚠ **and on Blueprint** | its section routing is inert *(Blueprint's outline is `BlueprintMyBlueprintWindow`, which routes to Details now)*, so there it IS close to redundant |

⇒ ⭐ **KEPT**, per `U-16`'s gate *("only after Details is proven, or there is no editing surface at
all")* and the user's `2026-08-17` *"keep for now."* ⭐⭐ **The honest reading for row 60: it is
retireable on Blueprint once Details is proven, and NOT retireable on BTree/HSM until `BP-317`.**

---

## 7. Carried forward

⭐⭐ **This is HALF the visual-check unblock condition** *(the other half is the emitter/access
unification)* — ⛔ **the suspension still stands.** · 🔴 **`BP-317`** — BTree/HSM have no Details host,
and NodeEdit's unused generic one belongs with row 61 · **`BP-315` is closed by `BP-316`** ·
🔴 `2.7` and `2.40`/`2.41` still NOT BUILT · `BP-309` *(filed, not adopted)* · the six `STILL REAL`
`DEBT-AIB` rows · the producer picker's runtime · the 12 quarantined scenario tests.
⛔⛔ **Everything multi-level stays parked** — `E3` · `E5` · `E7a` · `Q36` · `Q37`.

📌 **Next in the sequencing table:** `58` *(the Value column / run-state meaning)*, then `59` *(the
StructEdit dialog)*.
