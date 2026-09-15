# REPORT — Batch 79: **Track C is reachable**

> 📌 **Started at `faef5f2`** *(rule 1b marker, pushed before any code; dispatch `4d15370` already an ancestor)*.
> ⭐ **Rule 4:** re-pulled before the final commit — **no new coordinator commits**.
> ⭐ **IDs allocated: NONE** — every item was an existing `C-*` deliverable; nothing new was filed.
> ⭐ **`DEBT-AIB` rows touched: NONE.**
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged, no new skip.

---

## 0. ⭐ The headline

| | |
|---|---|
| ✅ **all five surfaces hosted** | outline · table · dialog launcher · tick highlight · variable watch |
| ⭐⭐⭐ **item 3's STOP: TWO concepts, not one — and actually THREE** | measured in the **persistence layer**, which already keeps them apart. ⛔ Not merged |
| ⛔⛔ **a sixth thing was unwired and nobody had listed it** | ⭐ **every `VariableValueFormatter` in the repo lived in a TEST** ⇒ hosting the table with a null decoder would have shown `<unreadable>` in every cell |
| ⭐ **item 4: two of the four are NOT BUILT** | ⚠ **and one of them is CLAIMED IN A DOC COMMENT** |

---

## 1. ✅ Item 1 — the outline, on BTree and HSM

| | |
|---|---|
| **what** | `AiMyBlueprintWindow` — a thin `ManagedWindow` constructing `BlackboardMyBlueprintModel` over `MyBlueprintPanel` |
| ⭐ **nothing else changed** | the panel is `NodeEditor.UI` over `IMyBlueprintModel` in `NodeEditor.Core` — ⛔ **it was never blueprint-specific**; the model was simply constructed by nothing |
| ⭐⭐ **the registrar CONSTRUCTS it** | asserted on the **object** *(`reg.MyBlueprint!.Host`)*, ⛔ never on the registrar's source — the `2026-08-16` forwarding-rail rule |
| ⛔ **and NOT on Blueprint** | that perspective already has `BlueprintMyBlueprintWindow`. ⭐ **The negative arm is a rail**: without it, a registrar that hands *every* perspective an outline would pass |
| ⭐ **both host kinds** | sections asserted as an ordered **sequence** in `SortOrder` — `bb.inputs` → `bb.workingState` → `bb.assetGlobals` — for **BTree and HSM** |
| ⚠ **EMPTY not ABSENT** | an empty blackboard still lists all three sections; only the item lists are empty |
| ⚠ **the panel is lazy, deliberately** | the AI perspectives have no `IEditorHostServices` at boot. ⭐ Until `Retarget` supplies them the window **states the reason** — ⛔ not an empty frame that reads as broken |

---

## 2. ✅ Item 2 — the table, per perspective

⭐ **Where: its own `AiVariablesWindow` per perspective.** ⛔ **NOT folded into `BlueprintDetailsWindow`** —
📐 that window is the NODE inspector *(selection store · a session cached per selected node · node
property editors)*, and folding a variable table into it **is `Q38`'s merge**, which the user deferred.

| | |
|---|---|
| ⭐⭐ **the window never sees an asset** | it holds a `VariableTableModel` whose `Source` is swapped ⇒ **section routing is one assignment**, and a heterogeneous set renders unchanged |
| ⭐⭐⭐ **selection ROUTES it** | outline click → section id → registrar resolves a row source → table re-filters. ⭐ **Asserted end to end through the registrar's own wiring**, ⛔ not by calling `ShowSection` directly — the wiring is the thing that was missing |
| ⭐ **and the join is IN THE REGISTRAR** | ⛔ not left to the host: *"a surface the host must remember to attach"* is exactly how five of these became unreachable |
| ⭐⭐ **`VariablesPanelControl` still draws** | ✅ **rail: `BlackboardAuthoring` is still registered.** The additive guarantee, executable |

⚠ **Yes, this adds two windows per AI perspective** — ⭐ accepted, and `Q38` exists to rationalise the
count. ⛔ **No STOP was hit**: the outline→table selection channel did not need inventing, because the
section id is the whole key.

---

## 3. ✅ Item 3 — the Watch. ⭐⭐⭐ **TWO concepts — and the answer is THREE**

### 📐 The measurement, not the inference

| | |
|---|---|
| **a breakpoint watch** | `Breakpoint` + `IsWatch` — a **CONDITION THAT FIRES**: a `SearchPredicateDto`, `Enabled`, `HitCount`, identity a `Guid` |
| **a pinned variable** | `VariableRow` — an **OBSERVED IDENTITY**: `(AssetId, Entity, Section, VariablePath)` with bytes, staleness and a row kind. ⛔ **no condition; it cannot fire** |
| ⭐⭐⭐ **the decisive evidence** | **the persistence layer ALREADY keeps them apart**: `DebugSessionPersistence.Save(nodeBreakpoints, watches, dbmBreakpoints, path)` — `IsWatch` lives in the **third** parameter, and the **second** is `Blueprints.Core.Debug.Watch`, persisted as `WatchEntry { AssetId, GraphId, PinId, DisplayName, ExpectedTypeName }` |
| ⚠⚠ **so there are THREE, not two** | the **blueprint PIN watch** is a third shape again — ⛔ **the handoff's hint that `Watch.IsStale` suggested one entity was an inference, and it does not survive the measurement** |

### 🛠 What was wired

⭐ **The breakpoint list is untouched.** The window gained a **second, labelled** section —
*"Pinned variables"* — fed by `PinnedVariableRowSource` through `VariableTableControl`.
⭐ `Type` is hidden there *(`VariableTableColumns.Watch`)* and shown in Details — the one toggle, per
surface. ⭐ Pinned rows read bytes through the row's own `ReadValue`, ⛔ **not through the window's old
64-byte carrier**, so a 136-byte struct pins and renders.
⭐ **Rails:** two assets × two entities keep independent identities · a **stale row survives and refuses
its dialog** *(kept, not dropped — a shrinking list would be silent)*.

⛔ **Unifying the three is a design question**, not a wiring one, and was not attempted.

---

## 4. ⛔⛔ The sixth unwired thing — **nobody had listed it**

📐 **Every `VariableValueFormatter` in the repo lived in a TEST**, each with its own inline lambda; the
production count was **zero**. ⇒ ⛔⛔ **hosting the table with a `null` decoder would have rendered
`<unreadable>` in every cell** — *a panel that looks wired and shows nothing*, the exact shape this
batch exists to remove, reintroduced by the fix.

⭐ **`RawValueDecoder`** is the production one: primitives · **enums by NAME** · blittable structs;
⚠ **undersized input is a FAILURE, not a partial read** *(a plausible-looking wrong value is worse than
`<unreadable>`)*; ⛔ never throws. ⭐ **bool is special-cased** — `Marshal.SizeOf(bool)` is 4 while the
blackboard packs it as 1 via the `[MarshalAs(I1)]` the DTO emitter injects.
⭐ **Rail: the registrar's own formatter decodes real bytes** *(`2.5f` → `"2.5"`)*, plus the refusal arm.

---

## 5. ✅ Item 4 — the four ⚠ verify items, settled FROM CODE

| # | verdict | measured |
|---|---|---|
| **2.7** | 🔴 **NOT BUILT** | `VariableTableModel.GroupBy` is a plain settable property — ⚠⚠ **and its own doc comment CLAIMS *"Persisted per panel in the editor layout"*, which nothing implements.** Fold is ImGui's `CollapsingHeader` ⇒ persisted in `imgui.ini` by window+label, ⛔ not by the editor layout. `ShowType` is **ctor-time** with **no toggle UI anywhere** |
| **2.26** | ⭐ **BUILT this batch — minus Rename** | right-click the **name cell**: *Edit value…* / *Properties…*, both disabled on a stale or node-owned row. ⛔ **Rename absent BY DESIGN**: a `VariableRow` is an observation with no asset handle, schema source or undo recorder ⇒ **rename belongs to the OUTLINE**, which holds the asset — the blueprint side does exactly that via `RegisterMyBlueprintItemCommands`. ⭐ A greyed entry would restate the "built but inert" shape |
| **2.40** | ⭐ **values editable BUILT** · 🔴 **budget NOT BUILT** | `VariableEditing.cs:151` — `(_, Planning) ⇒ Editable`. The only budget UI is the **old** `BlackboardAuthoringWindow`'s |
| **2.41** | ⭐ **live values BUILT** · 🔴 **budget-hides NOT BUILT** | rows re-read every frame and `VariableChangeMonitor.Observe` returns `None` while planning. ⛔ **There is NO run-state input to the budget display at all** — zero occurrences of `RunState`/`IsRunning` in that window, so its budget draws **mid-run too** |

⛔ **Nothing bigger was built as a drive-by.** ⭐ The checklist rows now carry these verdicts, so the
visual session **confirms rather than discovers**.

---

## 6. Gates — all seven reports

### 1 + 2 · one row per gate, with the `--no-build` column

| gate | command | `--no-build`? | result | Δ baseline |
|---|---|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | — | ✅ **0 err / 69 warn** | = |
| ⭐ **AiShared** | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | yes | ✅ **1303 / 1303** | **+14** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | yes | ✅ **3681 / 3691, 10 skipped** | = |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | yes | ✅ **615** | = |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | yes | ✅ **551** | = |
| Generators | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj --no-build` | yes | ✅ **270** | = |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | yes | ✅ **134** | = |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | yes | ✅ **136** | = |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | yes | ✅ **56 / 68, 12 skipped** | = |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | yes | ✅ **29** | = |
| ⚠ **Toolkits** | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | yes | 🔴 **3 of 5 runs red, 1 test each** | see ④ |
| ⭐ **NodeEditor.Core** | `dotnet test …/NodeEditor.Core.Tests.csproj` | ⛔ **NO** | ✅ **208** | = |
| ⭐ **NodeEditor.UI** | `dotnet test …/NodeEditor.UI.Tests.csproj` | ⛔ **NO** | ✅ **131** | = |
| ⭐ **Fhsm.Tests** | `dotnet test …/Fhsm.Tests.csproj` | ⛔ **NO** | ✅ **300, 0 skipped** | = |
| tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **open 64 / done 180 (+1 refuted)** | = |

### 3 · ⭐⭐⭐ Golden movement — **as a diff shape**

```
$ git diff --stat faef5f2..HEAD -- '*Snapshot*' '*golden*' '*Golden*' '*.cs.txt'
(no output)
```

⇒ ⭐⭐ **ZERO baseline files in the diff.** The whole change is **10 files, +876 / −25**:

| file | ± | what |
|---|---|---|
| `TrackCWiringTests.cs` | **+369** | new — the rails |
| `AiMyBlueprintWindow.cs` | **+133** | new |
| `AiVariablesWindow.cs` | **+111** | new |
| `AiWatchWindow.cs` | +87 / −… | the variables half, added beside the breakpoint list |
| `PerspectiveWorkspaceRegistrar.cs` | +74 | construction + registration + the routing join |
| `RawValueDecoder.cs` | **+62** | new |
| `VariableTableControl.cs` | **+31** | the row menu — ⭐ **purely additive, no existing line changed** |
| `AiWatchBreakpointsWindowTests.cs` · `PerspectiveWorkspaceRegistrarTests.cs` | +26 / −… | ⚠ **the only edits to EXISTING assertions** — window counts, §④ |
| `CHECKLIST_…md` | +8 / −4 | the four verdicts |

⭐ **`persistence-shape`, the 43 `Emit/*.cs.txt` and `StructureHash` are untouched** — ⛔ **and could not
have moved**: no emitter is in the diff at all.

### 4 · every RED confirmed pre-existing — **base `faef5f2`**

| red | verdict |
|---|---|
| `GizmoRegistryTests.SC_GZ004_2` · `StatelessGizmoRegistryTests.SC_GZ022_2` | ⭐⭐ **`DEBT-AIB-030`, and the identity ROTATED WITHIN THIS BATCH** — run 1 `SC_GZ004_2`, runs 2–3 `SC_GZ022_2`, and two other runs fully green. ⭐ **Confirmed mine by `--filter`: `~Gizmos` `187 / 187`, the two named tests `2 / 2`.** 📐 **`git diff --name-only faef5f2..HEAD -- FDP/` is EMPTY** ⇒ ⛔ **the diff cannot reach `Fdp.Toolkits` at all** |

⚠ **The three pre-existing window-count assertions that moved were NOT reds** — they were correct
before and are correct after; see ⑤.

### 5 · working tree after every suite run

`git status --short` ⇒ **empty**. ⛔ **No test regenerated a golden.**

### 6 · quarantine

**12** scenario · **0** FastHSM — ⭐ **unchanged.** ⛔ No new skip.

### 7 · ids + marker

**IDs allocated: none.** **Started at `faef5f2`.**

---

## ⑤ The three assertions I changed, and why

| test | was → is | why it is not a weakening |
|---|---|---|
| `Perspective_NoManager_…` | 6 → **7** | the Variables table joined the **core** set |
| `Perspective_WithManager_RegistersEightWindows` | 8 → **9** | ⭐ **the NAME is kept** so the `AIE-034` scenario stays traceable; only the count moved |
| `WatchAndBreakpointsWindowIds_AreDistinct…` · `ThreeRegistrars_…DistinctIdSets` | 24 → **27**, 18 → **21** | ⭐⭐ **the property under test is DISTINCTNESS, and it still holds** — which is precisely what would break if a new window forgot its perspective suffix |

---

## 7. 🔴 Revert-goes-red — **on the three things that were actually missing**

| probe | reddens |
|---|---|
| suppress `RegisterCore(wm, Variables)` + `MyBlueprint` | the registered-windows rail |
| suppress the `SectionSelected` join | the routing rail |
| drop `formatter:` from the `AiWatchWindow` ctor | ⭐ the **silent-default** rail |

📐 **5 of 14 red under the probe · 14 / 14 after.** ⛔ Un-applied by the inverse edit, never by
`git checkout --`.

---

## 8. Carried forward

⭐⭐ **The VISUAL CHECK is now unblocked** — checklist §1 + §2, with four rows that were "verify" now
answered. · 🔴 **`2.7`'s persistence and `2.40`/`2.41`'s budget indicator are NOT BUILT** and want a
decision *(build, or delete the claims)* · ⭐ **`Q38`** — one mode-switching Details panel, absorbing
`BP-128` · `BP-309` *(filed, not adopted)* · the six `STILL REAL` `DEBT-AIB` rows · the producer
picker's runtime · the 12 quarantined scenario tests.
⛔⛔ **Everything multi-level stays parked** — `E3` · `E5` · `E7a` · `Q36` · `Q37`.
