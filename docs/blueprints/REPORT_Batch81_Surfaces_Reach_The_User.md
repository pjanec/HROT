# REPORT — Batch 81: **visual check round 1 — every finding, and the drop item did not need dropping**

> 📌 **Started at `f6a2d07`** *(rule 1b marker, pushed before any code)*, on dispatch `438143ee7` ff-merged at `9b16fea`.
> ⭐ **Rule 4:** re-pulled the coordinator branch before the final commit.
> ⭐ **IDs allocated: `BP-310` `BP-311` `BP-312` `BP-313` `BP-314` `BP-315`.**
> ⭐ **`DEBT-AIB` rows touched: NONE.**
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.
> ⭐⭐ **§3c was NOT split out. All five items shipped.**

---

## 0. ⭐⭐⭐ The headline: **§2 was not the defect you described — it was worse**

You measured a **title collision** and concluded the designer *"has no way to tell which surface they are
looking at."* 📐 **Measured here, the mechanism is an ID collision with silent last-writer-wins:**

| | |
|---|---|
| the Track C table registers as | `ai_variables_{suffix}` ⇒ **`ai_variables_blueprint`** on Blueprint |
| `BlueprintVariablesManagedWindow` has claimed since AIE-048 | **`ai_variables_blueprint`** |
| `WindowManager.RegisterWindow` | **`_windows[window.Id] = window`** |
| registration order | `RegisterWindows` **`:2325`** → `RegisterExtraWindow(_blueprintVariablesWindow)` **`:2989`** |

⇒ ⛔⛔ **The Track C table was EVICTED from the registry on Blueprint** — absent from the Window menu, the
dock and every lookup, **nothing logged.** ⭐ **The designer was not choosing the wrong window. The new one
was not there.**

### ⚠ Why no rail caught it — **the seventh instance of one rule**

📐 Batch 79/80's rails assert on **`registrar.RegisteredWindows`** — the registrar's own list, which the
eviction never touches, because the registrar *did* build and *did* append it. ⇒ ⭐⭐ ***ask the ARTEFACT,
not the thing that produced it.*** The new rails ask the **`WindowManager`**, after the real
`EditorSubsystem` has run.

### ⚠⚠ And a second finding from the probe

🔴 **The TITLE rail alone would not have caught it.** An evicted window contributes no title, so
`NoTwoWindowsOnOnePerspective_ShareATitle` stayed **green** under the revert probe while the other four went
red. ⇒ ⭐ **a duplicate title and a duplicate id are different defects: the first is confusing, the second is
invisible.** Both rails are in, because neither implies the other.

### 📐 One correction to your sweep

You named **three** windows titled `"Variables"`. Measured, **two are registered windows**; the third
(`BlueprintVariablesWindow`) is the **inner control** `BlueprintVariablesManagedWindow` wraps, so it never
reaches the manager. ⭐ **Its `Title` was renamed anyway** — it is what the wrapper draws.

---

## 1. 🛠 The five items

### ① 🔴 **BLOCKING — feed the outline** *(`BP-310`)*

⭐ **Your diagnosis was exactly right**, and the derivation was already sitting there:

| what the panel needs | ⭐ where it already was | needed passing? |
|---|---|---|
| `IEditorHostServices` | `AiCanvasContext.View.Host` | — |
| `IEditorCommands` | `AiCanvasContext.Commands` | — |
| the active document's context | ⭐⭐ **`AiGraphCanvasWindow.ActiveContext` — already `public`** | ⛔ **no** |
| a route from the registrar to it | ⭐⭐⭐ **`RegisterExtraWindow` is already called on each registrar with its own canvas window** | ⛔ **no** |

⇒ ⭐ **`RegisterExtraWindow` installs the resolver itself.** ⛔ **Nothing new for `EditorSubsystem` to pass,
therefore nothing to forget** — the Batch-80 move, one level down. The `2026-08-16` forwarding-rail control
is still there (asserted **on the constructed object**), but it now guards a seam that cannot be skipped.

⚠ **Your "watch for" was right and is gated:** services are per-document, so both inputs are re-read every
frame and a document switch rebuilds the panel — `SwitchingDocuments_RebuildsThePanelOverTheNewDocumentsServices`.

⭐⭐ **The two service sources are kept APART** (`_explicit*` from `Retarget`, `_derived*` from the resolver).
⛔ Merging them is what closed the loop; and a `Retarget(vars, null, null)` would otherwise erase what the
resolver had just supplied. That has its own probe.

### ② **the sweep + rename** *(`BP-311` — see §0)*

⭐ **Layout check, as asked:** `ManagedWindow.WindowInternalName` is **`"{Title}###{Id}"`** ⇒ **ImGui keys
dock identity on the `###Id`, not the Title.** ⇒ ⭐ **renaming titles is layout-safe**; **moving an id costs a
saved dock slot.** ⛔ So the **new** window changed id, not the legacy one — and it had no slot to lose on
Blueprint *precisely because it was never registered there*.

| window | title before | ⭐ title now |
|---|---|---|
| `AiVariablesWindow` *(Track C table)* | `Variables` | **Variable Values** |
| `BlueprintVariablesManagedWindow` + its inner control | `Variables` | **Blueprint Variables** |
| `BlackboardAuthoringWindow` | `Blackboard Variables` | *(unchanged)* |

⭐⭐ **The durable half is not the rail — `RegisterCore` now THROWS** on a duplicate id, turning a silent
eviction into a startup failure for every registrar and every extra window.

### ③a **the row commands** *(`BP-312`)*

⭐ **Your root cause was exact, and there was a second half:** ⛔ `DeleteItem` rebuilt the facade with a
hard-coded `DeclarationKind.Variable`, and `DeclarationList.Remove` searches `decl.Kind`'s bucket — so it
could not have found a Parameter *even once the lookup did*.

⭐ **Your reasoning against a third prefix holds, and I checked the premise you gave:** `Variable`,
`Parameter` and `WorkingState` share one list, one delete rule and unique GUIDs. ⛔ **No differing delete
rule found ⇒ no STOP.**

⭐ **Resolving to the `BlueprintDeclaration` FACADE** is what carries the kind into the mutation — and a
Parameter is backed by `ParameterDecl`, which `AsVariableDecl` reports as null anyway. `Duplicate` **asks
`CarriesEditorPresentation`** rather than testing the kind.

### ③b **one dialog per section** *(`BP-313`)*

⭐ **Note is rewritten in place, not left standing.** ⚠ **The `noun` is load-bearing** — it drives the ImGui
popup id, and `ModalPopupIdTests`' general form covers the two new modals **for free**.

⭐⭐ **What the new rail asserts that the existing production-`Retarget` rail cannot:** that rail only asks
whether the invoke **succeeds**, which both wirings do. ⛔ **The observable difference is the asset** —
quick-add appends on the spot, a dialog appends nothing until confirm.

### ③c ⭐⭐⭐ **THE DROP ITEM — and it did not need dropping** *(`BP-314`)*

📐 **Your cost estimate was high because it assumed the UI half was new. It was not:** `MyBlueprintPanel`
**already had** `BeginDisabled` + tooltip, used for *"Not implemented"*. ⇒ the UI change is a **precedence**,
extracted as a pure `ResolveCreateDisabledReason` so it is checkable without an ImGui context.

⭐ **`CreateDisabledReason` is DEFAULTED** ⇒ all **13 construction sites across 5 files** compile unchanged —
the field arrives as a capability, not a rename.

⭐⭐⭐ **The one genuinely hard part was the one you named:** `_sections` is `static readonly`, and its own
comment cites that staticness as why the flag could not vary. ⛔ **Not mutated — PROJECTED per read**
(`d with { … }`), so section identity and the position-by-position D.6.2 order survive while the reason
follows the canvas. Cached on the reason, because the panel reads `Sections` every frame.

⭐ **General, not macro-specific**, per the round-out preference.

### ④ ⚠ **the Details panel — MEASURED, not fixed** *(`BP-315`)*

📐 **Not a wiring gap.** `BlueprintDetailsWindow` reads `ActiveSubSelection as BlueprintNodeSelection` — ⭐ it
is a **node** inspector and has never had a variable arm.

| measured | |
|---|---|
| `MyBlueprintPanel.SelectionChanged` | ⛔ **ZERO subscribers anywhere in the repo** |
| `BlueprintMyBlueprintWindow`'s `navigateToItem` | ⛔ **explicitly ignores variables** — *"Variables stay non-navigating — nowhere sensible to go"* |

⇒ ⭐ **the panel is correct about its own contract; the capability is absent** — and a Details panel that
switches between a node arm and a variable arm **is `Q38`'s *"one mode-switching Details panel"***, deferred.

⚠ **You were right that two surfaces are involved:** the guide's part D expects *"Double-click a NAME cell →
the properties dialog"*, which is the **Track C table's** row dialog, a different window entirely.

---

## 2. 🔴 Revert-goes-red — **per item, never delegated**

| probe | red |
|---|---|
| ① remove the `RegisterExtraWindow` derivation | **8 / 12** |
| ① restore `Retarget`'s clear-on-null | **1** *(the loop's own guard)* |
| ② restore `ai_variables_{suffix}`, guard off | **4 / 5**, ⛔ **silently** — the eviction reproduced |
| ③a restore the kind-scoped lookup | **9** |
| ③b re-wire the quick-add at the production site | **2** |
| ③c model stops projecting · panel ignores the reason | **3 + 2** |

⛔ Every one un-applied by the **inverse edit**, never `git checkout --`.

---

## 3. Gates — **all seven reports**

### 1 + 2 · one row per gate, with the `--no-build` column

| gate | command | `--no-build`? | result | Δ |
|---|---|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | — | ✅ **0 err / 69 warn** | = |
| ⭐ **AiShared** | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | yes | ✅ **1330** | **+12** |
| ⭐ **Blueprints** | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | yes | ✅ **3709 / 3719, 10 skipped** | **+28** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | yes | ✅ **615** | = |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | yes | ✅ **551** | = |
| Generators | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj --no-build` | yes | ✅ **270** | = |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | yes | ✅ **134** | = |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | yes | ✅ **136** | = |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | yes | ✅ **56 / 68, 12 skipped** | = |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | yes | ✅ **29** | = |
| ⚠ **Toolkits** | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | yes | 🔴 **1 red run 1, green runs 2–3** | ④ |
| ⭐ **NodeEditor.Core** | `dotnet test …/NodeEditor.Core.Tests.csproj` | ⛔ **NO** | ✅ **211** | **+3** |
| ⭐ **NodeEditor.UI** | `dotnet test …/NodeEditor.UI.Tests.csproj` | ⛔ **NO** | ✅ **135** | **+4** |
| ⭐ **Fhsm.Tests** | `dotnet test …/Fhsm.Tests.csproj` | ⛔ **NO** | ✅ **300, 0 skipped** | = |
| tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **open 65 / done 185 (+1 refuted)** | +1 / +5 |

⭐⭐ **The two NodeEdit gates are the cost §3c said to pay deliberately — paid, and both moved.**

### 3 · ⭐⭐⭐ Golden movement — **as a diff shape**

```
$ git diff --stat f6a2d07..HEAD -- '*Snapshot*' '*Golden*' '*golden*' '*.cs.txt' '*persistence-shape*'
(no output)
```

⇒ ⭐ **ZERO baselines.** **20 files, +1766 / −93** *(of which the report is +246 and the tracker ±20)*.
⛔ **No emitter, DTO, asset or compiler file is in the diff at all**, so `persistence-shape`, the 43
`Emit/*.cs.txt` and `StructureHash` **could not** have moved. ⭐ **10 of the 18 code/test files are TESTS**,
and **5 of those are new**.

| file | ± | |
|---|---|---|
| `TheOutlineGetsItsServicesTests.cs` | **+442** | new — ① on the production path |
| `WindowIdentityIsDistinctTests.cs` | **+179** | new — ② on the `WindowManager`, not the registrar |
| `MyBlueprintItemCommandTests.cs` | +131 | ③a — one per kind × per gesture |
| `LocalVariableSectionTests.cs` · `DeclarationSectionsTests.cs` | +90 / +68 | ③c / ③b |
| `MyBlueprintSectionDescriptorTests.cs` · `SectionCreateDisabledReasonTests.cs` | **+74 / +56** | new — the two NodeEdit gates |
| `BlueprintDocumentFactory.cs` | +183 / −45 | ③a + ③b — the one create path |
| `AiMyBlueprintWindow.cs` | +120 / −12 | ① — the two service sources |
| `BlueprintMyBlueprintModel.cs` | +64 / −7 | ③c — the projection |
| `PerspectiveWorkspaceRegistrar.cs` | +49 / −8 | ① derivation + ② the duplicate-id guard |
| `MyBlueprintPanel.cs` · `IMyBlueprintModel.cs` | +33 / +19 | ③c — NodeEdit |
| `BlueprintMyBlueprintWindow.cs` | +31 / −4 | ③b — the two modals |
| `AiVariablesWindow.cs` · `BlueprintVariablesManagedWindow.cs` · `BlueprintVariablesWindow.cs` | +3 / −3 | ② — the three titles, one line each |
| `BlueprintMyBlueprintModelTests.cs` | +48 / −12 | ⑤ — **the only edit to an EXISTING assertion** |

### 4 · every RED confirmed pre-existing — base `f6a2d07`

| red | verdict |
|---|---|
| `Fdp.Toolkits.Tests` — 1 test, run 1 only | ⭐⭐ **`DEBT-AIB-030`.** Runs 2–3 fully green *(`1964 / 1964`)*, `--filter Gizmos` **`187 / 187`**. 📐 **`git diff --name-only f6a2d07..HEAD -- FDP/Toolkits/` is EMPTY** ⇒ ⛔ **the diff cannot reach that assembly** |

### 5 · working tree after every suite run

`git status --short` ⇒ **clean** *(only the intended edits before commit)*. ⛔ No golden regenerated.

### 6 · quarantine — **12 scenario · 0 FastHSM**, unchanged. ⛔ No new skip.

### 7 · **ids: `BP-310`–`BP-315`** · **started at `f6a2d07`**

---

## ⑤ The one existing assertion I changed, and why

| test | change | why it is not a weakening |
|---|---|---|
| `MyBlueprintModel_Sections_SameInstanceAlways` → `…_AreTheSameSetAcrossARetarget` | `Assert.Same` on the container → **identical ids, identical order, identical count** | ⭐ **Its own comment states the invariant**: *"a section is scoped in its CONTENTS, not in its existence."* `Assert.Same` was the cheap proxy while the list was static; ⛔ with the reason projected it pins the **implementation**, not the property. ⭐ **The direct statement is strictly MORE than `Assert.Same` said**, and a second rail keeps the allocation guarantee where it still holds *(two reads, nothing changed ⇒ same list)* |

🔴 **Found by the gate, not by reading** — it was green when I ran Blueprints after ③b and red after ③c.

---

## 4. Carried forward

⭐⭐ **The visual check is unblocked further:** parts **C, D and F** were blocked on item ①, and the Blueprint
perspective's table is now reachable at all. · 🔴 **`BP-315`** — the Details panel's variable arm **is `Q38`**
· 🔴 **`2.7`'s persistence and `2.40`/`2.41`'s budget indicator are NOT BUILT** *(settled Batch 79)* ·
`BP-309` *(filed, not adopted)* · the six `STILL REAL` `DEBT-AIB` rows · the producer picker's runtime · the
12 quarantined scenario tests. ⛔⛔ **Everything multi-level stays parked** — `E3` · `E5` · `E7a` · `Q36` · `Q37`.

📌 **Queued behind this, per your §7:** Batch 83's *"the Watch has no entry points"* and an asset with an
`ExpressionTargetField` so guide part **B** becomes testable. ⭐ **§3c did not become Batch 82**, so that slot
is free.
