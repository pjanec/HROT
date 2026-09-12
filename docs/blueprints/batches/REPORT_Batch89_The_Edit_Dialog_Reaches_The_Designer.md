<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 89 report.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# REPORT — Batch 89: **the edit dialog reaches the designer** *(`BP-327`, reopened and closed)*

> 📌 **Dispatched at `7c2279851`** · **started at `c569e5f`** *(rule 1b marker)* · **all four items LANDED.**
> ⭐ **Ids allocated** *(rule 3/5)*: **`BP-336`** *(done)* · **`BP-337`** *(OPEN — a measurement finding)*.
> **`BP-327` REOPENED and CLOSED against its ORIGINAL criterion.**
> ⭐⭐⭐ **9 guide rows unblocked** — see §6. ⛔ **This batch does not run the visual check.**

---

## 1. ⭐ What landed

| item | verdict |
|---|---|
| ⭐⭐⭐ **`89a`** — draw the modal | ✅ **BUILT** — `WindowManager.RegisterFrameOverlay`, registered by the registrar |
| ⭐⭐ **`89b`** — the shared popup id *(`BP-336`)* | ✅ **BUILT** — contained, exactly as the handoff hoped |
| ⭐ **`89c`** — `BP-327`'s disposition | ✅ **REOPENED + CLOSED** in one row *(§4 — the arithmetic differs from the handoff's, deliberately)* |
| ⭐ **`89d`** — one false doc sentence | ✅ **FIXED** — `AiDetailsWindow`, doc only |

⭐ **Every premise in the handoff was re-measured and every one held.** ⛔ Nothing to STOP over.

---

## 2. 🛠 `89a` — the modal joins the frame *(`BP-327`)*

📄 **Design basis:** `BP-327`'s own exit criterion — *"the right batch draws the modal and runs the
guide's `F2`/`F3`"* · `R-67` · ruling 9.

### 2.1 ⭐ The defect, re-measured

```
grep -rn "EditModal" --include=*.cs .          → 4 hits, NONE of them a Draw() call
```

⭐ the construction *(`:328`)* · the property *(`:602`)* · **two tests asserting it is non-null.**
⇒ ⛔⛔ **Batch 87 built a class that CAN draw and nothing called it.** The gesture opened a session, the
modal held it, and no frame rendered it.

⚠⚠ **`BP-327`'s own sentence still described the editor word for word:** *"the write is COMPLETE and
UNREACHABLE BY A DESIGNER."* 📌 **Third consecutive turn of one pattern** — Batch 84 built a write path
nothing drew; Batch 87 built the drawer nothing calls.

### 2.2 ⭐⭐ WHERE it went, and why the two obvious places are wrong

| ⛔ rejected | 📐 why |
|---|---|
| a window's `DrawClientArea` | **`ManagedWindow.Render` returns early on `!IsOpen`** and again on a perspective mismatch ⇒ ⚠ **the dialog would vanish exactly when the designer closes the panel they opened it from** — a defect indistinguishable from this one |
| a line in `EditorSubsystem` | ⛔ **three registrars ⇒ three lines to forget.** 📌 `R-67` is the whole reason `AiDetails`, `MyBlueprint` and `Variables` are registered **by the registrar** |

⭐⭐⭐ **`WindowManager.RegisterFrameOverlay(Action)`** — a list drawn in the **final per-frame slot**,
after every window and the status bar, beside the file dialog: 📌 the slot whose own comment reads
*"Draw file dialog service last so the modal overlays all other windows."*
⭐ **The registrar registers its own modal** in `RegisterWindows` ⇒ ⛔ **the composition root gained
nothing to forget.**

⭐⭐ **A METHOD GROUP, not a lambda** — `RegisterFrameOverlay(EditModal.Draw)`. ⛔ A closure would make
the delegate opaque and reduce the rail to counting; a method group lets a rail ask **by identity**
whether *this* modal's `Draw` is in the path.

⛔ **The file dialog was NOT moved onto the new hook** *(handoff instruction, and "no rush removals")* —
⭐ noted as a follow-up in the code, beside the loop.

### 2.3 ⭐⭐⭐ THE RAIL — **two halves, and neither is worth anything alone**

| half | where | what it asks |
|---|---|---|
| ⭐ **the SLOT is real** | `Fdp.Presentation.Tests` · `FrameOverlayTests` *(10)* | drives a **real ImGui frame** through `ImGuiTestFixture`: a registered overlay is invoked **once per frame**, **after every window**, **even when every window is closed**, and **across a perspective switch** |
| ⭐ **the REGISTRAR fills it** | `Hrot.Editor.AiShared.Tests` · `TheEditDialogReachesTheFrameTests` *(11)* | asks the **CONSTRUCTED `WindowManager`** after a real `RegisterWindows`: is a delegate whose `Target` is **this registrar's `EditModal`** and whose method is **`Draw`** in `FrameOverlays`? |

⛔ **A slot nobody fills draws nothing; a registration into a slot nobody invokes draws nothing.**
⚠ **The ImGui context is created in the FDP suite and not in AiShared, on purpose** — it is
process-global native state, serialized there by the `"ImGui Sequential"` collection, and the
1457-test AiShared suite has no such guard.

⛔⛔ **`TheEditDialogIsDrawnTests` was NOT extended**, per the handoff. 📌 `R-67`: it constructs the
modal itself, so it proves `Draw()` **works** and can never ask whether anyone **calls** it.
⭐⭐ **And that blindness is now QUANTIFIED, not asserted — see §5 probe `P1`.**

---

## 3. 🛠 `89b` — three modals, three ImGui ids *(`BP-336`)*

📐 **Measured:** `VariableEditModal.Title` is a **`public const string`**, used for **both**
`OpenPopup` and `BeginPopupModal`. ⇒ the moment `89a` lands, **three registrars draw three modals under
ONE id every frame.**

⚠ **Correct today only because `if (!IsOpen) return` fires first for the other two** — ⛔ **an
undocumented guard standing between two popups with the same id.**
📌 **This repo has already paid for popup-id confusion once** — `AssetPickerModal:185-189`: *"the popup
opens under one id while `BeginPopupModal` waits on another, so it never renders."*

⭐ **`PopupId` = `$"{Title}##{suffix}"`**, seeded from the registrar's perspective, the same way every
window takes an `idOverride`. ⭐ **Everything before `##` is what ImGui DISPLAYS**, so the designer
still reads *"Edit variable"* on all three hosts — ⚠ **railed explicitly**, because scoping an id must
not trade an invisible fix for a visible rename. ⛔ **`Title` stays a `const`**: it is referenced by
rails and it genuinely is the display title; what became instance-scoped is the **ID**.

⭐ **Contained, as the handoff hoped** — one property, one constructor parameter *(defaulted, so the
headless single-instance harness is unchanged)*, two call sites inside `Draw`.

---

## 4. ⭐ `89c` — `BP-327`'s disposition, and **the arithmetic differs from the handoff's**

⭐ **I took the handoff's recommendation: REOPEN, do not file a new id.** It is the same defect one
level up with the same exit condition, and a new row would split one story across two.

⚠ **But the count moves differently than the handoff predicted**, and this is worth stating because it
is the kind of thing a `--check` disagreement is made of:

| the handoff expected | 📐 what happened |
|---|---|
| *"`done 204 → 203` before your own close"* | ⭐ **`done` never dipped.** The reopen and the close are **one edit to one row inside one batch**, so the checkbox was never committed in the `[ ]` state |

⭐⭐ **The row now carries both halves in sequence** — the `2026-08-19` measurement that reopened it,
then the close **against the ORIGINAL criterion**: ⛔ not *"the class exists"* but ⭐ ***"a designer can
reach the dialog"***, evidenced by the two rail halves and by probe `P1`.

📐 **Arithmetic:** baseline **66 / 204** → `BP-336` done **(+1)** → `BP-337` open **(+1)** →
**67 / 205.** ✅ `tracker-counts.py --check` passes.

---

## 5. ⭐ GATES — **the rule-8 contract, plus the three this batch owns**

### ⭐ 1 + 2 — per gate, with the `--no-build` column

| # | gate | command | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|---|
| 1 | **AiShared** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/…csproj` | ⛔ built | **1457 / 0 / 0** | **+11** *(1446 → 1457)* |
| 2 | **Blueprints** | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/…csproj` | ⛔ built | **3767 / 0 / 10 skip** *(3777)* | **0** |
| 3 | **BTree.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/…csproj` | ⛔ built | **615 / 0 / 0** | **0** |
| 4 | **Hsm.Editor** | `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/…csproj` | ⛔ built | **551 / 0 / 0** | **0** |
| 5 | **Hrot.Editor** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/…csproj` | ⛔ built | **201 / 0 / 0** | **0** |
| 6 | **Breakpoints** | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/…csproj` | ⛔ built | **143 / 0 / 0** | **0** |
| 7 | ⚠ **NodeEditor.Core** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/…csproj` | ⛔⛔ **BUILT — never `--no-build`** | **211 / 0 / 0** | **0** |
| 8 | ⚠ **NodeEditor.UI** | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/…csproj` | ⛔⛔ **BUILT** | **135 / 0 / 0** | **0** |
| 9 | ⚠ **Fhsm.Tests** | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/…csproj` | ⛔⛔ **BUILT** | **300 / 0 / 0** | **0** |
| ⭐ **10** | ⚠⚠ **Fdp.Presentation.Tests** *(NEW to this batch — `89a`'s other rail half lives here)* | `dotnet test …/Fdp.Presentation.Tests/…csproj --filter "FullyQualifiedName~Fdp.Presentation.Tests.WindowManager"` | ⛔ built | **146 / 0 / 0** | **+10** *(136 → 146)* |

⛔⛔ **Row 10 is FILTERED, and that is a finding, not a convenience — see `BP-337` and §5-gate-11.**
⚠ **`Fdp.Toolkits.Tests` not run** — 📌 `DEBT-AIB-030`. ⛔ Nothing in this batch's diff touches it.

### ⭐⭐⭐ 3 — golden movement, as a DIFF SHAPE

| | |
|---|---|
| ⭐⭐⭐ **ZERO goldens moved.** | ⛔ **No asset, no emit golden, no `persistence-shape.txt`, no hash fixture is in the diff.** |
| **the whole diff** | **6 files**: 2 new *(both test files)*, 4 modified. **Production: 4** — `WindowManager.cs` · `PerspectiveWorkspaceRegistrar.cs` · `VariableEditModal.cs` · `AiDetailsWindow.cs` *(doc only)* |
| **shape** | ⭐ **purely additive in production**: one new method + one new property + one loop in `WindowManager`; one registration line in the registrar; one property + one defaulted ctor parameter in the modal. ⛔ **Zero removed behaviour lines** — the only deletion is `89d`'s false doc sentence |
| ⚠ **existing test assertions moved** | ⭐ **NONE.** ⛔ **0 test methods deleted, 0 edited.** ⚠ Notable given Batches 86–88 each moved counts: nothing here changes a window count, because an overlay is **not a window** |

### ⭐⭐ 4 — every RED confirmed pre-existing vs the base `7c2279851`

⭐ **The nine baseline suites and the filtered row 10 are all green at `HEAD`.**

⛔⛔ **One genuine pre-existing failure, confirmed in a CLEAN WORKTREE at `7c2279851`:** the **full**
`Fdp.Presentation.Tests` suite **crashes its test host** — `Fdp.Toolkit.Vis2D.Tests.Gizmos.DebugPrimitiveRenderer2D*`
throws `NullReferenceException` at `DebugPrimitiveRenderer2D.cs:28`, then *"Test host process crashed"*.

| | at base `7c2279851` | at `HEAD` |
|---|---|---|
| full suite | **11 failed / 34 passed / 45 total, ABORTED** | **4 failed / 14 passed / 18 total, ABORTED** |

⚠⚠ **The differing counts are the point, not a discrepancy:** the abort **truncates the run**, so the
totals depend on ordering. ⇒ ⛔ **neither a red nor a green from the whole suite is evidence** — the
same shape as `DEBT-AIB-030`, from a different cause. ⭐ **Filed as `BP-337`**, and it explains why this
suite has been absent from every batch's baseline table without anyone writing down why.

### ⭐ 5 — the working tree is CLEAN after every suite run

✅ `git status --short` after the full set showed **only the batch's own 6 files.**

### ⭐ 6 — quarantine counts

| | before | after |
|---|---|---|
| **Blueprints skipped** | 10 | **10** |
| **every other suite skipped** | 0 | **0** |

⭐ **No new skip.**

### ⭐ 7 — tracker, rulings, ids

```
python3 scripts/tracker-counts.py --check   → tracker counts OK — open 67 / done 205 (+1 refuted)
python3 scripts/rulings-check.py            → 65/65 rulings verified against their sources
python3 scripts/design-digest.py --check    → all 49 recently-changed design documents carry a STATUS header
```

⭐ **Ids allocated:** **`BP-336`** · **`BP-337`**. **`BP-327`** reopened and re-closed *(§4)*.
⛔ **No `Architect_Question_N` created** *(rule 3a)*.

### ⭐⭐ 8 — THE ENUMERATION: everything drawn per-frame OUTSIDE a `ManagedWindow`

```
grep -rn "EditModal" --include=*.cs .                                   → total 4 (none a Draw call)
search_graph(label="Class", name_pattern="SetFileDialogService|RegisterFrameOverlay|
                                          ImGuiFileDialogService|StatusBarManager|MainToolbarManager")
                                                                        → total 5
read WindowManager.Render (:456-553)                                    → the authoritative list
```

⭐ **`WindowManager.Render` is the only per-frame driver** *(one production caller —
`Hrot.ClusterRunner/Program.cs:353`)*. Everything it draws outside the window loop:

| # | drawn per frame, not a `ManagedWindow` | how it got there |
|---|---|---|
| 1 | the **main menu bar** *(global menu · perspective menu · host menus · gizmo menus)* | hard-coded |
| 2 | **`MainToolbarManager.RenderInline`** | hard-coded, but **registration-driven inside** |
| 3 | ⚠ **the "About HROT" modal** | ⛔ **hard-coded, `WindowManager`-owned** |
| 4 | ⚠ **the "Settings" modal** | ⛔ **hard-coded, `WindowManager`-owned** |
| 5 | **`_statusBar.Render`** | hard-coded, **registration-driven inside** *(`RegisterSection`)* |
| 6 | ⭐⭐ **the file dialog** — `(_fileDialogService as ImGuiFileDialogService)?.Draw()` | **`SetFileDialogService`** — a **ONE-SLOT** registration |
| 7 | ⭐ **the frame overlays** *(this batch)* | **`RegisterFrameOverlay`** — a list |

⇒ ⭐⭐⭐ **The handoff asked: "if a second modal is already in this position, say so — it changes whether
the hook is new or a duplicate." ⚠ TWO already are (#3, #4), and a third thing (#6) already occupies
the exact slot.** ⭐ **But none of them is a duplicate of this hook:** #3/#4 are `WindowManager`'s own
modals with no registration seam at all, and **#6 is a SINGLE-SLOT special case of exactly what
`RegisterFrameOverlay` generalises.**

⇒ ⭐⭐ **The honest statement: the hook is not new ground, it is the general form of `SetFileDialogService`.**
⛔ **Folding the file dialog onto it was explicitly out of scope** *(handoff; "no rush removals")* — ⭐
noted as a follow-up in the code. ⚠ **A future batch that does it should also consider #3/#4**, which
are the only remaining hard-wired modals.

### ⭐⭐ 9 — WHAT EACH RAIL ASKS

| rail family | ⭐ what it ASKS | ⛔ what it does NOT |
|---|---|---|
| **`FrameOverlayTests`** *(10, FDP)* | ⭐⭐ **a REAL ImGui frame**: invocation count per frame · order **after** windows · survival when **every window is closed** · survival **across a perspective switch** · idempotence by delegate equality · registration order · null throws · re-entrant registration | ⛔ **that anyone registers anything** |
| **`TheEditDialogReachesTheFrameTests`** *(11, AiShared)* | ⭐⭐ **the CONSTRUCTED `WindowManager`** after a real `RegisterWindows` — **by delegate identity** *(`Target` is this registrar's `EditModal`, method is `Draw`)*; three registrars ⇒ **three distinct** modals; `89b`'s ids distinct **and still displaying one title** | ⛔ **that a frame invokes the slot**; ⛔ **that ImGui renders anything** *(`R-21`/`R-62`: no visual checks)* |
| ⭐ **the negative controls** *(5)* | before `RegisterWindows` the list is **empty** · no edit service ⇒ **nothing registered** · an **unregistered** overlay is never drawn · an unscoped modal keeps the **bare** title | ⭐ **without these the positives could pass vacuously** |

⛔⛔ **`TheEditDialogIsDrawnTests` was NOT extended** — 📌 it is `R-67`'s own example and it stayed
green through the entire life of the defect it is named for.

### ⭐⭐⭐ 10 — REVERT-GOES-RED, three probes, **never delegated**

| probe | what was un-applied | reds |
|---|---|---|
| ⭐⭐⭐ **P1** | the registrar's `RegisterFrameOverlay(EditModal.Draw)` line | **4**, and ⭐⭐ **ONLY the 4 new AiShared rails — all 1453 others stayed green**, `TheEditDialogIsDrawnTests` and both `EditModal != null` asserts included |
| **P2** | `Render` stops invoking the overlay list *(kept the loop, dropped the call)* | **7 / 10** — ⭐ the 3 pure-registration rails correctly stay green |
| **P3** | `PopupId` reverts to `Title` | **1** — ⭐ `89b`'s rail alone |

⭐⭐⭐ **`P1` is the answer to the handoff's question** — *"if something else reddens too, say which;
that would mean the old tests were less blind than measured."* ⇒ ⛔ **Nothing else reddened.** The
existing suite is exactly as blind as `R-67` predicts, and now that is a measurement rather than a
claim.

⛔ **Every probe was un-applied with the INVERSE EDIT** — ⛔ never `git checkout --`.
⚠ **P2 was run under a filter**, because the unfiltered suite aborts *(`BP-337`)*.

---

## 6. ⭐⭐⭐ WHAT THIS UNLOCKS — **stated in the report, as asked**

⭐ **9 rows of 📄 [`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md) part `D`** —
`D2` · `D3` · `D4`–`D8` · `D10` — plus **`F1`**, and it makes **`C2`**'s default-authoring route real
instead of a detour through `InspectorWindow`.

⇒ ⭐⭐ **The user runs the visual check next.** ⛔ **This batch does not run it — headless** *(`R-21`/`R-62`)*.

⚠ **Two things the check should expect, so a known gap is not read as a new defect:**

1. ⛔ **The Value column reads `(pending)` on all three hosts** — 📌 **`BP-334`**, Batch 88's finding.
   ⭐ Part `D` is about the **dialog**, which is now reachable; the **column** is a separate seam.
2. ⭐ **The dialog is a MODAL in the final frame slot** ⇒ it deliberately **stays up across a
   perspective switch** and does **not** close when the panel it was opened from is closed. ⚠ That is
   the designed behaviour, not a leak — ⛔ closing it with its host window is the defect this batch fixed.

---

## 7. ⭐ Carried

| | |
|---|---|
| ⛔⛔ **`BP-334`** | the two live-value seams — ⭐ a **ruling-9 decision** *(lean: give `IVariableRowSource` a formatted-value arm)*, ⛔ not a wiring item |
| ⚠ **`BP-337`** *(new)* | `Fdp.Presentation.Tests` crashes its host ⇒ **an unrunnable suite is an ungated one.** ⭐ Worth a batch; ⛔ it is a Vis2D defect, out of this scope fence |
| ⭐ **follow-up, noted in code** | fold the file dialog *(and possibly the About/Settings modals)* onto `RegisterFrameOverlay` — ⛔ **not a side effect of this batch** |
| ⭐ **`BP-325`** · **row 60 / `U-16`** · **row 61** | untouched |
| ⛔ **PARKED** | `E3` · `E5` · `E7a` · `Q36` · `Q37` · everything in `Q38`–`Q44` *(`R-27`)* |
| ⭐ **`DEBT-AIB` partitions touched** | ⚠ **none.** No `DEBT-AIB` row moved |
