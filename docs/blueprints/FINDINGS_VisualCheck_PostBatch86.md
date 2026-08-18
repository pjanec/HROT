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

## 4. ⏳ NOT YET DIAGNOSED — **stated as open, not guessed**

| row | what is known | what is NOT |
|---|---|---|
| **`B3`** *no row highlight* | `VariableTableControl:90/127` calls **`view.HighlightOf(...)`** ⇒ **a highlight mechanism exists.** 📌 §5: the **CHANGE** highlight is *"planning ⇒ none"* **by design**, and the check ran in planning | ⛔ **Whether a SELECTION highlight was ever built is UNMEASURED.** ⚠ **I asserted `B3` without checking** — it may be a guide error too |
| **`B8`** *second node click does not switch back* | `BlueprintDetailsWindow:40` holds **`_lastSubSelection`** — *"used to decide when a NODE click should take the…"* ⇒ **the arm-switching logic EXISTS** | ⛔ **Why it does not re-take on the second click is UNMEASURED** |

---

## 5. ⭐ What this says about the PROCESS — **not about the code**

| ⚠ | |
|---|---|
| ⭐⭐⭐ **A guide row must cite the batch that BUILT the thing, not the design that RULED it** | 📌 Four rows here cited a **ruling** and assumed a build. ⛔ **`D1` cited ruling 5's three-dot button; nothing ever built one** |
| ⭐⭐ **"Row N landed" needs to mean the ROW, not the batch's items** | 📌 `C7`: row 58 named the blueprint live provider; the batch did not build it, did not claim it, and I merged the row | ⭐ **Add to the gate contract: restate the ROW's own acceptance list and mark each item built / not built** |
| ⭐ **The user's failures were CHEAP and the triage was EXPENSIVE** | ⭐⭐ **That is the right ratio** — ⛔ but only because the guide rows were falsifiable. **Keep that.** |
