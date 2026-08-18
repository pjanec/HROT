<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: this whole file - the triage of the 2026-08-18 visual check.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# FINDINGS — Blueprint visual check, `2026-08-18` *(post-Batch-86)*

> ⭐⭐⭐ **Headline: FOUR of the reported failures are MY GUIDE promising surfaces that were never
> built. ONE is a PASS I mislabelled. ONE is a genuinely new defect, and it is NOT `BP-327`.**
> ⛔ **Ids are NOT allocated here** *(rule 3)* — the implementation session numbers them.
>
> 📌 **Swept before triage** *(`2026-08-18` rule)*: `Q32` §4 rows 56–61 · `DESIGN_Variable_Details_And_Editing.md` §5/§6 ·
> `REPORT_Batch83` · `R-21`/`R-62`/`R-60`.

---

## 1. ⭐⭐⭐ THE NEW DEFECT — **the Details table has NO gesture binder attached**

📐 **Measured, `PerspectiveWorkspaceRegistrar:306`:**

```csharp
EditGestures = new VariableEditGestureBinder(...);
EditGestures.Attach(Variables.Control);      // ⛔ the STANDALONE Variables window's table
```

⛔⛔ **`BlueprintDetailsWindow:83` constructs its OWN `VariableDetailsSection`**, exposes it as
`Variables`, and ⛔ **nothing ever attaches the binder to it.**

⇒ ⭐⭐ **`D2` `D3` `D11` fail in the Details panel because there is NO MENU THERE AT ALL** — ⛔ **not
because the dialog lacks an OK button.** ⚠⚠ **Two defects were being read as one.**

| ⭐ | |
|---|---|
| **this is the TWELFTH instance of the no-caller pattern** | ⚠ and the comment **three lines above the `Attach` call** names the **eleventh**: *"`VariableEditLauncher` and `VariableEditGestureBinder` shipped in Batch 75… constructed ONLY IN TESTS — zero production call sites."* ⇒ ⭐ **Batch 83 fixed the launcher's call site and attached it to ONE of the two tables** |
| ⭐ **the same shape as `BP-330`** | `AiWatchWindow._control` is private ⇒ nothing can attach there either. ⇒ ⭐⭐ **THREE table hosts, ONE attach** |
| ⭐⭐ **the control** | ⛔ **a rail on the registrar's source cannot catch this.** ⭐ **Assert on the CONSTRUCTED object**: *every table host a perspective builds has `IsEditGestureBound == true`* — 📌 `VariableTableControl:56` already exposes exactly that property |

---

## 2. ⚠⚠ FOUR GUIDE ERRORS — **the check asked for things that do not exist**

| row | what the guide promised | 📐 measured |
|---|---|---|
| 🔴 **`D1`** *"click the `⋮`"* | a three-dot button | ⛔ **There is no `⋮`.** `VariableTableControl.DrawRowMenu` opens on **`BeginPopupContextItem()`** — a **RIGHT-CLICK** menu. ⚠⚠ **And that also deviates from ruling 5**, which says *"a three-dot button AND double-click"* ⇒ ⭐ **the button half was never built** |
| 🔴 **`C7`** *"the same column shows the CURRENT value"* | live values while running | ⛔⛔ **Blueprint has NO live-value provider — at all.** `EditorSubsystem` passes one for BTree *(`:2178`)* and HSM *(`:2188`)* and ⛔ **none for Blueprint**; ⭐ **zero `ILiveBlackboardValueProvider` implementations exist under `Hrot.Blueprints.*`**. ⇒ **`(pending)` is the DESIGNED rendering** for a source with no byte reader *(`VariableDetailsSection` doc)* |
| 🔴 **`E2`–`E7`** | pin a variable, compare, stale rows | ⛔ **Watch PINNING is not built** — it is 📄 `DESIGN_Variable_Watch_Pinning.md`, designed `2026-08-18` and **unstarted**. ⭐ **Only `E1` was ever runnable** |
| ⚠ **`C2`** | read a variable with a declared default | ⚠ **Blocked by the D-part** — ⛔ with no way to author a default, an asset may contain none to read. ⭐ **Not an independent failure** |

> ⭐⭐⭐ **`C7` is the one that matters beyond the guide.** 📌 **`Q32` §4 row 58 reads:** *"the Value
> column… **+ blueprint's `ILiveValueProvider`** and `UpdateVariableDefaultValueJson`."*
> ⛔ **Batch 83's report never claims it** — ⭐ **I merged row 58 as complete when half of it was not
> built, and did not notice because the report did not claim it either.** ⚠ **The gate contract asks
> for what a batch DID; it does not ask what the row REQUIRED.**

---

## 3. ✅ `A2` IS A PASS — **I mislabelled it**

📐 `BlueprintMyBlueprintModel.GetItems` now switches on **`SectionVariables`** and
**`SectionParameters`** only — ⛔ **there is no `SectionWorkingState` arm.** ⭐ **Batch 86 retired the
section deliberately** *(handoff item 4c)*.

⇒ ⭐⭐ ***"No Working State in My Blueprint" IS THE INTENDED STATE.***
⚠⚠ **ONE THING STILL TO CONFIRM WITH THE USER:** ⭐ **do the declarations that used to sit under
*Working State* now appear under *Variables*?** ⛔ **If they vanished entirely, THAT is a finding** —
and it is the only way this row could still be a defect.

---

## 4. 🔴🔴 `B3` — **MEASURED. The selection is COMPUTED and NEVER DRAWN**

> ⚠ **User, `2026-08-18`:** *"Ad B3 - why dont you measure it?"* ⭐ **Fair — it was one grep.**

📐 **The whole chain is wired, and it ends one call short:**

| ✅ | `BlueprintMyBlueprintWindow:357/383` sets **`SelectedVariablePath: item.DisplayName`** |
|---|---|
| ✅ | `VariableDetailsSection:119` applies it — **`_model.SelectedVariablePath = selection.SelectedVariablePath`** |
| ✅ | `VariableTableView.IsSelected(row)` computes it, ⭐ **deliberately kept ORTHOGONAL to the change highlight** *(*"do NOT express selection through the change highlight… a header would read 'something changed' because the designer clicked"*)* |
| 🔴🔴 | ⛔⛔ **`VariableTableControl` NEVER CALLS `IsSelected` — zero references.** ⭐ **The renderer never asks.** |

⇒ ⭐⭐ **`B3` is a REAL defect, and a one-line-shaped one.** ⚠⚠ **The THIRTEENTH instance of the
pattern — and an INVERTED one:** ⛔ usually nothing constructs the thing; ⭐ **here everything
constructs and routes it, and the last consumer does not read it.**
📌 **So the existing rail shape does not catch it** — *"assert on the constructed object"* passes here.
⇒ ⭐ **the check has to be *"the control's rendered row state reflects `IsSelected`"***, i.e. ask the
**artefact**, not the model — 📌 the same lesson Batch 83 learned about `CellText`.

### ⏳ `B8` — still not diagnosed

`BlueprintDetailsWindow:40` holds **`_lastSubSelection`** *("used to decide when a NODE click should
take the…")* ⇒ **the arm-switching logic exists.** ⛔ **Why it does not re-take on the second click is
UNMEASURED** — ⭐ stated as open rather than guessed.

---

## 4a. ✅ `A2` — **the exact check, because my first question was unanswerable**

> ⚠⚠ **User, `2026-08-18`:** *"as there is no more Working State section, i have no idea what variables
> come from what section, they are all in variables. You need to be more exact what to look for."*
> ⭐⭐⭐ **Correct — I asked a question the UI cannot answer.** ⛔ **The tag is gone from the UI ON
> PURPOSE; the only place the old split survives is GIT.**

📐 **Batch 86 retagged 12 assets / 34 declarations.** ⭐ **Use ONE asset with a clean split:**

**`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/Blueprints/HillAssault2_CalculateSegments.bp.json`**
— **14 declarations, and the split is unambiguous:**

| section | expect **exactly** these |
|---|---|
| ⭐ **Parameters** *(5)* | `StartX` · `StartY` · `EndX` · `EndY` · `TankSpacing` |
| ⭐⭐ **Variables** *(9 — ALL formerly `WorkingState`)* | `TotalSlots` · `BurnedSlotsMask` · `WaveUsedSlotsMask` · `BaselineReservedMask` · `ActiveAttackerCount` · `CurrentWave` · `CachedEqsRequestId` · `CachedTargetGroupHandle` · `EqsRequestTime` |

| verdict | |
|---|---|
| ✅ **PASS** | all **9** appear under **Variables**, all **5** under **Parameters**, ⛔ **no third section** |
| 🔴 **FINDING** | **any of the 9 missing** ⇒ the collapse dropped declarations. ⚠ **That would contradict Batch 86's gate 8** *(43/43 `StructureHash` byte-identical)*, so it is worth reporting loudly |

### 📐 `Variables (0)` — **the whole chain MEASURED, `2026-08-18`, and every layer is GREEN**

⭐ **Reported:** *"Variables (0) - no variables shown there"* — on **an unnamed asset** *(`A1` says "any
blueprint asset")*. ⭐⭐ **Measured, layer by layer, rather than asked about:**

| layer | verdict |
|---|---|
| **the outline model** | ✅ `BlueprintMyBlueprintModelTests` — **11/11 pass** |
| **persistence + the real corpus** | ✅ `CorpusCanonicalisationTests` + `GoldenCorpusTests` — **140/140 pass** ⇒ the on-disk `Kind: "Variable"` declarations DO reach the store |
| **the section wiring** | ✅ descriptor `("variables", "Variables", …)` and the `GetItems` switch **agree**; `SectionParameters`' display name is **`"Inputs"`** ⇒ ⭐ **the user's *"Parameters shown as Input"* is EXPECTED** |
| **the live binding** | ✅ `EditorSubsystem:2296` calls `Retarget(blueprintAsset: ctx?.AssetRef as BlueprintAsset)`, and ✅ `BlueprintDocumentFactory:379` sets **`AssetRef = bpAsset`** |
| **the null path** | ⚠ `GetItems` returns **empty for EVERY section** when `_asset == null`, headers intact — ⭐ **that would ALSO have emptied Graphs / Functions / Events**, which was not reported |

⇒ ⭐⭐⭐ **NO DEFECT IS SUPPORTED BY THE MEASUREMENT.** ⛔ **The one variable left is WHICH ASSET WAS
OPEN** — ⭐ **an asset with only `Parameter` declarations correctly shows `Variables (0)`**, and the
reported *"Inputs"* section says this one has inputs.
⇒ ⚠ **Re-run `A2` against `HillAssault2_CalculateSegments` specifically.** ⛔ **This is not a deferral —
every layer reachable without the running editor has been eliminated.**

---

## 5. ⭐ What this says about the PROCESS — **not about the code**

| ⚠ | |
|---|---|
| ⭐⭐⭐ **A guide row must cite the batch that BUILT the thing, not the design that RULED it** | 📌 Four rows here cited a **ruling** and assumed a build. ⛔ **`D1` cited ruling 5's three-dot button; nothing ever built one** |
| ⭐⭐ **"Row N landed" needs to mean the ROW, not the batch's items** | 📌 `C7`: row 58 named the blueprint live provider; the batch did not build it, did not claim it, and I merged the row | ⭐ **Add to the gate contract: restate the ROW's own acceptance list and mark each item built / not built** |
| ⭐ **The user's failures were CHEAP and the triage was EXPENSIVE** | ⭐⭐ **That is the right ratio** — ⛔ but only because the guide rows were falsifiable. **Keep that.** |
