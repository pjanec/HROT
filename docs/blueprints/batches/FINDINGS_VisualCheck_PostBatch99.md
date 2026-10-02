<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-08-20
current-answer: this whole file — five defects from the 2026-08-20 visual check, each
  root-caused to a file and line by the coordinator BEFORE any batch was written.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# FINDINGS — visual check after Batch 99 *(`2026-08-20`)*

> ⭐⭐⭐ **User:** *"it is progressing but so slowly that i am starting to think what we are doing
> wrong… What makes these simple tasks SO unbelievably difficult? Is it somehow too complex and
> confusing and needs refactor?"*
>
> ⭐⭐ **The five defects are root-caused below. §6 answers the question, and the answer is not "we were
> careless" — it is structural, measurable, and it says the current method has hit its ceiling.**

---

## 1. ⛔⛔ The edit dialog shows `[+][-]` and **no number**

📐 **The user's hypothesis was right, and it is NOT a StructEdit bug** — ⭐ the modal's table setup is
**byte-identical** to the working reference:

| | `ComponentEditWindow:144`–`:149` *(works)* | `VariableEditModal:296`–`:302` |
|---|---|---|
| flags | `Borders │ RowBg │ Resizable │ SizingFixedFit` | ⭐ **identical** |
| col 0 | `TableSetupColumn("Property", WidthFixed, 180f)` | ⭐ **identical** |
| col 1 | `TableSetupColumn("Value", WidthStretch)` | ⭐ **identical** |

### ⭐⭐⭐ The difference is the CONTAINER, not the table

```
ComponentEditWindow  = a ManagedWindow with a real, resizable width
VariableEditModal    = BeginPopupModal(..., ImGuiWindowFlags.AlwaysAutoResize)   ⛔ and NO SetNextWindowSize
```

📐 `ComponentEditDrawer:253` — `float inputWidth = GetContentRegionAvail().X;` … `if (inputWidth < 60f) inputWidth = 60f;`
📐 `:396` — `ImGuiApi.InputInt("##v", ref v)` — ⚠ **`InputInt` draws a text field PLUS `[-]` and `[+]` step buttons**, and
`SetNextItemWidth` sizes **the whole group**.

⇒ ⛔⛔ **A `WidthStretch` column inside an `AlwaysAutoResize` popup is CIRCULAR**: the popup sizes to its
content, and the content asks the popup how much room it has. It resolves small, clamps to **60 px**,
the two step buttons consume it, and **the number has nowhere left to draw.**
⇒ ⭐ **exactly the reported symptom: the buttons are visible, the value is not.**

⭐ **Fix:** give the popup an explicit width *(`SetNextWindowSize` with a sensible default, or drop
`AlwaysAutoResize` for the table region)*. ⛔ **Do not touch `ComponentEditDrawer`** — five other callers,
all working.

⚠ **Honesty:** this is a strong inference from the sources, ⛔ **not a pixel measurement** — ImGui cannot
be exercised headlessly *(`R-21`/`R-62`)*. §6 is about exactly that gap.

---

## 2. ⛔ `[x]` does not close the Edit dialog

📐 `VariableEditModal.Draw`:

```csharp
if (!IsOpen) { _open = false; return; }
if (!_open) { ImGui.OpenPopup(PopupId); _open = true; }      // ⛔ REOPENS IT
if (!ImGui.BeginPopupModal(PopupId, ref _open, ...)) return;
```

⇒ ⭐⭐ ImGui sets `_open = false` when `[x]` is clicked. ⛔ **`IsOpen` is `_binder.ActiveSession != null`,
which the `[x]` never touches** ⇒ next frame the second line **reopens the popup.**
⭐ **`[x]` must do what `Cancel()` does** — close the session, not just clear a flag.

⚠ **`VariablePropertiesModal:168` has the same bug in a worse form** — `bool open = true;` is a **local**,
passed by `ref`, and **never read again**.

---

## 3. ⛔⛔⛔ The Properties dialog never appears — **`_propertiesModal.Draw()` HAS NO CALLER**

📐 `BlueprintDetailsWindow` — `:50` declares it · `:125` constructs it · `:66` **opens** it · `:53`
exposes it · ⛔ **and no line calls `Draw()`.** The only `.Draw()` in the file is `:291 session.Draw()`.

⇒ ⭐⭐ the gesture fires, `Open()` returns `true`, `IsOpen` is `true`, **and nothing is ever rendered.**

⛔⛔ **THIS IS THE THIRD OCCURRENCE OF `BP-327`** *(Batch 87: "the modal draws"; Batch 89: "`Draw` had no
caller")*. ⭐⭐ **Batch 99's rails asserted `IsOpen` and the commit path — both true, both useless**, because
a rail that cannot reach the draw cannot see that nobody draws.

---

## 4. ⛔⛔ The Watch row reads `0` for ever — **the SILENT-DEFAULT PATTERN, 9th instance**

📐 `AiWatchWindow:68`–`:69` builds `new VariableTableControl(formatter)` +
`new VariableTableModel(_pinned, VariableTableColumns.Watch)` — ⛔ **and is never given a run-state
source.** Every `SetRunStateSource` call site is a **details** host:

```
PerspectiveWorkspaceRegistrar:718   host.SetRunStateSource(_runState)   // the DETAILS host
PerspectiveWorkspaceRegistrar:325   Variables.SetRunStateSource(_runState)
BlueprintDetailsWindow:163 · AiDetailsWindow:104 · AiVariablesWindow:105
```

⇒ the Watch's model sits at the default **`Planning`** ⇒ 📌 `VariableValue.ModeFor(Planning)` selects the
**INITIAL** arm *(`Q32` ruling 3)* ⇒ ⛔⛔ **it renders `DefaultValueJson` — `0` — and the initial value
never changes**, which is precisely *"0 instead of 11, and it stayed 0 for ever."*

⭐⭐⭐ **The row is NOT the problem, and the user's instinct was exactly right: it IS the same row.**
📐 `SectionVariableRowSource:81` and `BlackboardSectionRowSource:106` both pass
`AssetTick: () => BehaviorFrame.Current`, and `PinnedVariableRowSource.Pin` stores the row **object**
unchanged. ⇒ ⭐ **the pinned row is a live camera; the Watch is just looking at the wrong arm of it.**

⛔⛔ **And it is the canonical silent default:** 📌 `CLAUDE.md` — *"a production caller that HAS a
dependency must PASS it."* ⭐ **`PerspectiveWorkspaceRegistrar` holds `_runState`, hands it to the details
host at `:718`, and holds the `Watch` — and does not.**

---

## 5. ⚠ "Properties…" appears in the Watch context menu

📐 `RegisterExtraWindow:635` — `if (window is IVariableTableHost tableHost) AttachEditGestures(tableHost);`
⇒ ⭐ **every** table host gets **every** gesture. ⛔ The Watch is a value list; 📌 the user: *"no one is
interested in the other properties than the value in the Watch window."*
⭐ **Fix:** the gesture set becomes part of what a host declares, like `VariableTableColumns` already is
*(`VariableTableColumns.Watch` exists — ⭐ the precedent is already there)*.

---

## 6. ⭐⭐⭐ WHY THIS IS SLOW — **the honest answer**

### ⛔⛔ ① Every one of these five defects is in the region NO RAIL CAN REACH

| defect | what it is |
|---|---|
| §1 | a popup **width** |
| §2 | an ignored `ref` flag |
| §3 | a **missing method call** |
| §4 | a service **not passed to a constructor** |
| §5 | a gesture attached to the wrong host |

📌 **`R-21`/`R-62`: no headless rail can drive ImGui** ⇒ ⭐⭐⭐ **a batch can report `3852 / 0` and the
feature can be dead**, because nothing green touches the drawing. ⛔ **We have been adding rails in the
one region where rails are cheap and defects are not.**

### ⛔ ② One LAYER per batch — the dialog has failed at five different layers in five batches

`94` no gesture binder → `95` wrong document → `96` no `BeginTable` → `97`/`98` the write refused →
`99` the form is not drawn, and the input has no width.
⭐ **Each batch could only see the then-topmost layer.** ⛔ That is not carelessness; it is the visibility
limit of headless work — ⭐⭐ **and the remedy is to stop shipping a layer at a time.**

### ⛔⛔ ③ The recurring shape is **"CONSTRUCTED, NOT CONNECTED"**

⭐ `BP-327` *(a modal nobody draws)* is on its **third** occurrence. ⭐ The **silent default** is on its
**ninth**. ⛔ **Both are composition-root defects, and we keep fixing INSTANCES instead of the CLASS.**

### ⭐⭐ So: is it too complex and does it need a refactor? — **YES, and the refactor is already designed**

⛔ **The variable-row stack is not the complex part.** ⭐⭐ **The COMPOSITION ROOT is:**
`EditorSubsystem` is ~4 000 lines and `PerspectiveWorkspaceRegistrar` takes **21 constructor
parameters**. ⇒ **every one of these five bugs is a wiring bug in that region.**

⭐⭐⭐ **`PerspectiveWorkspace` — the extraction `R-121` already specifies — is exactly that refactor.**
📄 [`DESIGN_Details_Panel_View_Switching.md`](DESIGN_Details_Panel_View_Switching.md) §5.
⇒ ⭐ **it stops being a Details-panel prerequisite and becomes the fix for the defect class.**

---

## 7. ⭐⭐ WHAT CHANGES — **proposed, for approval**

| # | ⭐ change | why |
|---|---|---|
| **①** | ⭐⭐⭐ **ONE VERTICAL-SLICE batch, not layers.** Its acceptance is the user's own sentence, end to end, and every item is a step **of that one sentence** | ⛔ five layer-batches produced a feature that still does not work |
| **②** | ⭐⭐⭐ **A GENERIC composition-root rail** — for every window the registrar registers, assert **on the constructed object** that every dependency the registrar HOLDS and the window CAN accept was passed | ⭐ kills the silent-default class *(9)* in one rail instead of one instance at a time |
| **③** | ⭐⭐ **A "modal is drawn" rail** — every modal field owned by a registered window must be reachable from that window's draw | ⭐ kills `BP-327`'s class *(3 occurrences)* |
| **④** | ⚠ **MEASURE whether ImGui can render offscreen here** — ⭐ **4 of these 5 would have been caught by one rendered frame.** ⛔ Not proposed blind: the feasibility is measured first, and reported | ⭐⭐ this is the only change that attacks ① in §6 |

⛔ **Nothing dispatches until ④ is measured** — ⭐ because if it is possible, the batch shape changes.
