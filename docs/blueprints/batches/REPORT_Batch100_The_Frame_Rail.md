<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file — the Batch 100 return.
stale-below: nothing.
known-rot: none.
known-conflict: R-21 / R-62 ("no headless rail can drive ImGui") are superseded by R-124.
  Every earlier report's "the draw is unrailed by construction" was TRUE when written.
-->
# REPORT — Batch 100: **the frame rail, and the five defects it can finally see**

> 📌 **Dispatched at `f4ec0209c`** · scope frozen there · base for every RED: **`f4ec0209c`**.
> ⭐ **Started-marker pushed first** *(rule 1b)*: `cc95efa`.
> ⭐ **Ids allocated by me** *(rule 3)*: **`BP-373`** · **`BP-374`** · **`BP-375`** · **`BP-376`** ·
> **`BP-377`**.

---

## 1. ⭐⭐⭐ THE FOUR VERDICTS *(`R-106`)* — **one per item, none missing**

| item | verdict | |
|---|---|---|
| **`100a`** — the frame rail | ✅ **DONE** | ⭐ **and it reproduced the defect before the fix**, which was its acceptance |
| **`100b`** — the dialog shows the number | ✅ **DONE** | ⚠ the Properties form was **not** red; reported, not claimed — §4 |
| **`100c`** — `[x]` closes it | ✅ **DONE** | ⭐ railed **across frames**, which is the only way to see it |
| **`100d`** — the Properties form is drawn | ✅ **DONE** | ⭐ **plus the class-level IL rail** the handoff asked for |
| **`100e`** — the Watch shows the live value | ✅ **DONE** | ⭐ **9th silent default**; railed connected **and** flowing |
| **`100f`** — the Watch's menu loses "Properties…" | ✅ **DONE** | ⭐ host-declared; ⚠ **two** watch surfaces, not one — §6 |
| gates · probes · tracker · report | ✅ **DONE** | §7–§9 |

⛔ **Nothing blocked, nothing partial, nothing not-started.**

---

## 2. ⭐⭐⭐ `100a` — **THE FRAME RAIL, and it works here**

📌 **`R-124`.** ⛔⛔ **The premise of `R-21`/`R-62` is false on this machine** — ⚠ **and it was TRUE when
those rulings were written**; everything built under them was correct at the time.

| | |
|---|---|
| **where** | `Hrot/Editor/Hrot.Editor.UiFrameRail/` — a **test-support project**, referenced by both test assemblies. ⛔ Not copied into each *(ruling 9)*; ⛔ no production assembly references it |
| **versions** | Raylib-cs **7.0.2** / rlImgui-cs **3.2.0**, ⭐ **pinned to the app's** — a harness laying out with a different ImGui measures the wrong thing |
| **skip, never fail** | `IsAvailable()` probes by **actually opening a window**, ⛔ not by reading `DISPLAY` — ⚠ a `DISPLAY` pointing at a dead server is exactly the case that would crash instead of skip. ⭐ **Verified both ways**: 8 ran under Xvfb, 8 skipped with `DISPLAY` unset, each printing its reason |
| **re-entrancy** | Raylib is not re-entrant ⇒ a `DisableParallelization` collection **and** a process-wide semaphore. ⚠ Belt and braces deliberately: a crashed host truncates a run and makes counts differ between runs — 📌 the `BP-337` / `DEBT-AIB-030` shape |
| ⛔ **not a screenshot differ** | 📌 `R-124`: the assertions are ordinary assertions that happen to run **inside a rendered frame**. ⭐ `Screenshot()` exists for **evidence a human can look at**, never as a gate |

### ⭐⭐ Its acceptance — **it FAILED first**

```
against f4ec0209c: The edit dialog's content width is 259.0 px (floor 320.0).
```

### ⭐⭐⭐ THE MEASURED NUMBERS — **mine, not the coordinator's**

| what | before `100b` | after |
|---|---|---|
| ⭐ **the REAL `VariableEditModal`**, popup content width | **259.0 px** | **504.0 px** |
| ⭐ a **REPLICA** of its exact table shape, **value column** | **60.0 px** *(the clamp floor, exactly)* | **305.0 px** |

⚠ **The coordinator's probe reported 60 → 305.** ⭐ That is the **replica's** pair, and it reproduces
independently inside the suite. ⛔ **The real modal's numbers are different because the seam is
different** — its title text and OK/Cancel row widen the auto-resized popup — and the handoff's
instruction to *"report what YOU measure"* is exactly why that matters.

### ⚠ Why the two seams, stated plainly *(📌 `M-29`)*

The number the designer loses is the avail width **inside the value column**, and that column is drawn
by `ComponentEditDrawer` — **`Fdp.Presentation` infrastructure with five other working callers, which
this batch must not touch.** ⇒ the real-modal rail measures the **CAUSE** *(the container)* and the
replica measures the **SYMPTOM** *(the column)*. ⭐ **The replica is labelled a replica in its own doc
comment**, and its job is to keep the DIAGNOSIS true, ⛔ not to gate the fix.

---

## 3. ⛔⛔ TWO MISTAKES THE HARNESS CAUGHT IN ITS OWN AUTHOR — **worth as much as the fixes**

| | |
|---|---|
| ⭐⭐ **the close signal was a no-op** | The first draft signalled `[x]` with `ImGui.CloseCurrentPopup()` **after `Draw()` returned** — ⛔ that call does nothing outside a popup's `Begin`/`End` pair. ⭐ The rail failed with *"[x] did not close the dialog"*, **correctly**. ⚠ **The first thing the frame harness measured was a mistake in a test** |
| ⛔⛔ **rendering the window CRASHED THE TEST HOST** | `ManagedWindow.Render(perspective, new IconAtlas(IntPtr.Zero, …))` dies under a real GL context — the title-bar pin button hands ImGui a **zero texture handle**, harmless with no renderer attached and **fatal with one**. ⚠⚠ A crashed host truncates the run ⇒ 📌 the `BP-337` shape, and shipping a rail that can do that would be **worse than shipping no rail**. ⭐ The rail now drives `DrawClientArea` — **the method the defect was in** — and the window chrome is the faked layer, stated |

---

## 4. ⭐ `100b` — **and one honest negative**

⚠ **The Properties form was NOT red before the fix.** Its text inputs already made auto-resize wide
enough. ⭐ `SetNextWindowSize` is applied there anyway because the shape is identical and the failure is
one short field away — ⛔ **reported as a precaution, not claimed as a repair.**

---

## 5. ⭐⭐ `100c` — **why one frame is not enough**

📌 The handoff: *"⛔ Not just the same frame."* ⭐⭐ **A same-frame assertion PASSES against the broken
code** — the popup really did close, and it was the **following** frame that resurrected it. ⚠ **That
one-frame gap is the entire bug**, and it is invisible to any rail that does not render twice.

⭐ `[x]` now routes to a named `CloseFromWindowChrome()`. ⚠ **The faked layer is exactly one line** — the
`if` inside `Draw` that notices ImGui cleared the flag — because a rail cannot press the button.

---

## 6. ⭐⭐⭐ `100f` — **the measurement that decided the design**

📌 The handoff said *"NOT `if (host is AiWatchWindow)`"*, and gave the reason as a matter of shape.
📐 **There is a stronger reason, and it is measured: there are TWO watch surfaces** — `AiWatchWindow`
**and** Blueprints' `WatchPanelWindow`. ⇒ ⛔ **a type test would have shipped with one of them still
offering the authoring menu.** 📌 `R-74` again: the enumeration finds what the assumption misses.

⭐ `IVariableTableHost.Gestures` has **no default body** *(`U-5`/`BP-230`)*, so all **six** hosts had to
answer — ⚠ **that cost is the feature.** ⭐ "Properties…" is **ABSENT** on the Watch, ⛔ not greyed:
greying says *"not right now"* *(the `F3` convention, for a refusal a designer can undo by pausing)*,
and this surface will never offer it.

---

## 7. ⭐⭐ GATES — **Batch 99's table shape, plus this batch's three extra rows**

⭐ Base for every RED: **`f4ec0209c`**. Every command unfiltered unless a row says otherwise.

| gate | `--no-build`? | result | Δ baseline |
|---|---|---|---|
| solution build | — | **0 errors** · `EXIT=0` | — |
| **AiShared** | ✅ | **1706 / 0 / 0** · `EXIT=0` | **0** |
| **Blueprints** *(under Xvfb)* | ✅ | **3870 / 0 / 10 skip** · `EXIT=0` | **+18** *(3852)*, skips **0** |
| **BTree.Editor** | ✅ | **622 / 0 / 0** | **0** |
| **Hsm.Editor** | ✅ | **554 / 0 / 0** | **0** |
| **Hrot.Editor** | ✅ | **201 / 0 / 0** | **0** |
| **Breakpoints** | ✅ | **143 / 0 / 0** | **0** |
| **Generators** | ✅ | **277 / 0 / 0** | **0** |
| **Persistence** | ✅ | **143 / 0 / 0** | **0** |
| ⛔ **NodeEditor.Core** | ⛔ **NO** *(out of solution ⇒ `--no-build` reports a STALE BIN)* | **211 / 0 / 0** | **0** |
| ⛔ **NodeEditor.UI** | ⛔ **NO** | **135 / 0 / 0** | **0** |
| ⛔ **Fhsm** | ⛔ **NO** | **300 / 0 / 0** | **0** |
| ⛔ **StructEdit** | ⛔ **NO** | ⚠ **191 / 1 / 0** | **0** — `BP-363`, pre-existing |
| **Fdp.Presentation** | ✅ | **146 / 0 / 0** *(`BP-337` filter)* | **0** |
| **Fdp.Toolkits** | ✅ | ⚠ **1963 / 1** — see row 4 | — |
| `tracker-counts.py --check` | — | **OK — open 77 / done 235 (+1 refuted)** | done **+5** |
| `rulings-check.py` | — | **92 / 92 verified** | **0** |
| `design-digest.py --check` | — | **OK** *(incl. `R-123`'s class+sequence diagram check)* | — |

### ⭐⭐⭐ EXTRA ROW 1 — **frame-rail counts: RAN / SKIPPED**

| environment | ran | skipped | |
|---|---|---|---|
| ⭐ **under `xvfb-run`** | **8** | **0** | ⭐ the number that matters — ⛔ *"all skipped"* would be a FINDING |
| ⚠ with `DISPLAY` unset | **0** | **8** | ⭐ each printing *"no DISPLAY — run under `xvfb-run …`"* |

⛔ **None of Blueprints' 10 skips is a frame rail** — all ten are the pre-existing quarantine.

### ⭐⭐ EXTRA ROW 2 — **the measured widths** · §2 above. **259.0 → 504.0** *(real modal)* ·
**60.0 → 305.0** *(replica's value column)*.

### ⭐ EXTRA ROW 3 — **the screenshot**

📄 **[`img/b100-edit-dialog-fixed.png`](img/b100-edit-dialog-fixed.png)** — the value `11` rendering
with room to spare, beside its `−`/`+` step buttons. ⛔ **Evidence, not a gate.**

### ⭐ Row 3 — **golden movement as a DIFF SHAPE**

⛔ **ZERO golden files, ZERO asset `.json`.** One **new PNG** under `docs/blueprints/img/` *(evidence)*.
⇒ code + tests + docs + one image.

### ⭐ Row 4 — **every RED confirmed pre-existing**

| RED | evidence |
|---|---|
| `StructEdit…Build_CircularReference_CircularFieldIsUnsupported` | `BP-363` — **191 / 1, identical**. Nothing in this diff touches StructEdit |
| ⚠ `Fdp.Toolkits.Tests` | 📌 **`DEBT-AIB-030` REPRODUCED LIVE AGAIN**: two runs this batch failed **different tests** — `Gizmos.Tests.GizmoRegistryTests.SC_GZ004_2` then `Squad.Tests.DangerAreaProviderTests…`. ⭐ **Isolated by namespace as the debt requires ⇒ 187 / 0.** ⇒ cross-test interference, ⛔ not a regression |

### ⭐ Row 5 — **working tree CLEAN after every suite run.** ⛔ No golden regenerated by a test.

### ⭐ Row 6 — **quarantine counts:** Blueprints **10 → 10**; every other suite **0**. ⛔ No new skip.

### ⭐ The **+18** on Blueprints, itemised

**8** frame rails · **1** `EveryModalAWindowOwnsIsDrawnTests` · **9** `TheWatchIsWiredLikeADetailsHostTests`.

---

## 8. ⭐⭐ REVERT PROBES — **one per item, never delegated**

⛔ **Never `git checkout --`** — every probe un-applied with the **inverse edit**.

| # | probe | red |
|---|---|---|
| **P1** | the modals lose `SetNextWindowSize` *(the state at `f4ec0209c`)* | **1** — the real-modal width rail, at **259.0 px** |
| **P2** | `BlueprintDetailsWindow` does not draw its modal | **1** — the IL rail, naming the field: *"owns `VariablePropertiesModal` (`_propertiesModal`) and never calls its `Draw()`"* |
| **P3** | the registrar does not pass the run-state source | **3** — the three perspectives |
| **P4** | the host's gesture answer is not carried to its table | **3** — the three perspectives, ⭐ **and the anti-vacuity rail stayed GREEN**, which is what makes those three meaningful |

⚠ **`100c` has no separate probe and that is deliberate**: the `[x]` rail's own first draft *was* the
probe — it failed against a signal that did nothing, and the fix to the rail is what made it real.

---

## 9. ⭐ WHOSE OBJECT · WHICH LAYER IS FAKED *(📌 `M-29`)*

| rail | input comes from | ⛔ what is faked |
|---|---|---|
| `TheEditDialogHasRoomForTheNumberTests` | ⭐ the **production** `VariableEditModal` over a real StructEdit session | ⛔ **nothing but the mouse.** ImGui really lays it out |
| `TheValueColumnIsCircularWithoutAnExplicitWidthTests` | ⚠ **a REPLICA** of the modal's container + table | ⛔ **the modal itself** — it exists to keep the diagnosis true, ⛔ not to gate the fix |
| `TheCloseBoxActuallyClosesTests` | the production modal, real frames | ⛔ **one line** — the `if` in `Draw` that calls `CloseFromWindowChrome` |
| `ThePropertiesFormIsVisibleWhenOpenedTests` | the production window + its production modal | ⛔ **the window CHROME** — `Render` crashes with a zero-handle atlas, so `DrawClientArea` is driven directly |
| `EveryModalAWindowOwnsIsDrawnTests` | ⭐ compiled **IL** | ⛔ **reachability** — a call after an early `return` passes |
| `TheWatchIsWiredLikeADetailsHostTests` | ⭐ `CreateRegistrar`, production's own factory | ⛔ the registrar's **construction arguments** *(catalog, refactor stub, breakpoint manager)* — ⚠ `Watch` is `null` without a manager, so a bare `EditorSubsystem` would assert about a null |

---

## 10. ⭐ WHAT WAS **NOT** BUILT

⛔ A golden-image / screenshot-diff suite · input simulation · any change to `ComponentEditDrawer` · a
second editability matrix · Properties as StructEdit · a per-field read-only flag · an
`Instance`-blueprint live write · a BTree/HSM live writer · anything from
`DESIGN_Details_Panel_View_Switching.md` · **any revert of Batches 94–99**.

⚠ **One thing the batch's definition of done still cannot certify from a rail**: that the `[x]` GLYPH is
hit-testable where the designer aims, and that the outline click → Details → menu chain works with a
real mouse. ⭐ Everything else in the user's sentence is now asserted in a rendered frame.
