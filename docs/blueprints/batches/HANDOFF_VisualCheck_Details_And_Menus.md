<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the UI lane — findings from the user's visual check of the merged
  L6 baseline. Each item is ALREADY DESIGNED; the UI session does the detailed analysis + build. Cited
  design bases below; diagrams live in the designs.
known-conflict: none.
-->
# HANDOFF — UI lane · **visual-check findings (Details views, float/pin, menus)**

> 📌 **Dispatched at `<DISPATCH_SHA>`.** ⭐ Branch from it *(rule 7)*; **rule 1b: started-marker FIRST.**
> ⭐ Lane: UI / variable *(`claude/hrot-implementation-j1jvin`)* — ids **`BP-`**, tracker `A`–`G`. **Rule 3: your own ids.**
> ⛔ **Scope FROZEN at this sha.** ⭐ **These are the USER's findings from the visual pass on the merged
> baseline** — each is already designed; **you own the detailed analysis** (the coordinator did only a light
> design-basis sweep). Confirm each against its cited design before building (obligation ③); fold any
> deviation back into the design (obligation ⑤).

## The baseline that was checked

⭐ **Good:** the **Scenario perspective now has a Details panel and shows 2 views (Components + Mission)**
for a selected entity — L6 confirmed working by the user. The items below are what's missing/wrong.

## Findings

| # | user finding | ⭐ design basis / likely cause | what to do |
|---|---|---|---|
| **VC-1** | ⛔ **Cannot open a floating window** — neither the contextual **float** (live) nor the **pin** (frozen-to-current-context). Both were designed. | 📄 `DESIGN_Details_Panel_View_Switching.md` §6 **`L4.4`** *(entry points: toolbar affordance + View menu)* · §2b float/pin sequences · §2 `DetailsViewWindow`. 📌 **`BP-403` (open):** the **View-menu half is NOT wired.** ⚠ And the toolbar `float`/`pin` buttons only render at ≥2 offers AND when the `DetailsWindow` has a window-manager — **verify the Scenario Details host actually passes one** *(`PerspectiveWorkspace`/registrar wiring)*, else the buttons never draw even with 2 views. | finish `L4.4`'s entry points; make float + pin reachable from the Scenario perspective |
| **VC-2** | **Move `File ▸ Layout` → a `Settings` main menu.** | ⚠ **User request, not previously designed.** Menu is registered in `LocalWindowController.RegisterLayoutMenu` under path `"File/Layout/…"`. | change the menu path to a top-level `Settings` menu *(e.g. `"Settings/Layout/…"`)*; keep the items |
| **VC-3** | **Missing the item to update the git-stored curated scenario set.** | 📄 `docs/UX/UX_Feature_Curated_Scenarios.md`. ⚠ The item **exists** — `ScenarioMenuCommands` registers `scenario.updateCurated` "Save Curated Scenarios to Git", **`isEnabled` = `CuratedScenarios.CanSaveToGit()`** *(a walk-up-to-repo probe)*. Likely cause: the probe does not find the repo `scenarios/` from the running build's output dir, so the item is **disabled** *(and disabled items may not show)*. | verify the walk-up probe resolves from the run location; ensure a disabled item is still **visible with a reason**, per the layout "Save as default" precedent |
| **VC-4** | **Graph-signature and Runtime views don't appear** in the details panel *(empty graph click → "no node selected")*. | 📄 `DESIGN_Details_Panel_View_Switching.md` §6 `L3` — the descriptors EXIST *(`GraphSignatureDetailsView.cs`, `RuntimeDetailsView.cs`)* but are **predicate-gated**: graph-signature needs a **graph row** selected *(not empty space)*; runtime needs `Mode != Planning` ∧ its asset kind. | confirm the predicates + that these descriptors are registered on the perspectives the user is in; if the intent is broader, adjust the predicate — **argue it against §6 `L3`** |
| **VC-5** | **Node properties / Inspector / entity blueprints / other windows are un-migrated** to the details panel. | 📌 **`BP-399` (open):** *"L3's remaining four rows not started"* — Node properties *(the 697-line `InspectorWindow`)*, Utility, Parameter sync, + the rest. 📄 §6 `L3` table. | build `BP-399` — the L3 remainder — so these become predicated Details views |

## Not this batch / notes

⛔ No time-lane or MCP file. ⚠ **VC-2 is a new preference** *(no design)* — record a one-line design note when you build it. ⭐ **VC-3 is the coordinator's curated-scenarios feature** — it is small; if the fix is only the probe/visibility, do it inline and say so.

## Gates

⭐ Standing contract *(rule 8)*: one row per gate · command · pass/fail/skip · delta · goldens as a diff
shape · `tracker-counts.py --check` · `rulings-check.py` · the **`BP-` ids you allocated** · `R-106`
verdicts. ⭐ **The visual items are `R-27`-gated** — the user re-checks on the next baseline; where you can,
add a headless rail asserting the offer-set/menu-registration model. ⭐ Rule 4/7: re-sync + pull the
coordinator branch around the batch.
