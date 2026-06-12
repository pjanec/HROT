# Main Toolbar 2 — FINAL REPORT

**Date:** 2026-06-12 · **Branch:** `blueprint-integ-1` · **Mode:** autonomous (per `multi-batch-NON-interactive-ds.md`
+ user override "no stopping after each batch"). Dev-lead orchestrated via claude-worker-orchestrator (`pro`),
hard-reviewed + independently verified each batch, committed one-per-batch.

## Tasks — all 7 done & lead-verified
| Task | Batch | Commit | Verified |
|------|-------|--------|----------|
| MTB2-T1 generic icon UX (90% inset + hover/toggle) | 30 | `50ef7309` | Fdp.Presentation icon/toolbar 45/45, 0 warn |
| MTB2-T2 Save icon in MainToolbar + `shell/save` cell | 31 | `689199a1` | icon 3/3, guardrail 13/13 (+`ContainsEntry`) |
| MTB2-T3 `DynamicDisplayName` + menu/tooltip plumbing | 32 | `dab5b560` | Fdp menu/toolbar 17/17, NodeEditor.Core 181/181 |
| MTB2-T4 perspective-aware Save + Save Scenario + dynamic label | 33 | `aca37c94` | SaveCommands 12/12, Editor.Tests 183/183 |
| MTB2-T5 unified File menu + "Scenario" display-label | 34 | `57fe0fd8` | PerspectiveLabel 2/2, guardrail 14/14 |
| MTB2-T6 `RecipePickerSource` (per-kind recipes incl Empty) | 35 | `dffc5882` | AiShared.Tests 1069/1069 |
| MTB2-T7 `NewAssetLauncher` + File/New + New button; retire RecipeCreateModal wiring | 36 | `b1f513b4` | Editor.Tests 186/186, guardrail 15/15, Blueprints PRE-1 only |

## What shipped (the four requested items)
1. **New-from-recipe** — `RecipePickerSource` + `NewAssetLauncher`; `shell.newAsset` (Ctrl+N) + File/New Asset… + New
   toolbar button open a **Tree recipe picker** (per-kind recipes incl. "Empty") → create + open. (Interactive
   name/folder popup deferred → DBT-A3.)
2. **Save icon** in the main toolbar next to Open Asset, wired to `shell.save` (Ctrl+S), with a **dynamic tooltip**.
3. **Unified File menu** (New/Open/Save/Save As…/Save All + Save Scenario/Save Scenario As…), Save routed by the
   **active-save-target resolver** (focused document, else scenario via the "Editor" perspective signal), with a
   **dynamic `Save [{kind}: {name}]`** label/tooltip. Perspective shows as **"Scenario"** (display-label; key unchanged).
4. **Toolbar icon UX** — generic `IconWidgets` 90% centered inset + clear hover/toggle fills (benefits every icon).

## Verification discipline
Every batch independently re-built + re-tested by the lead (no regen flag), diffs + assertions read line-by-line.
**Caught (trust-diffs-not-report):** BATCH-31's guardrail asserted only `Height>0` (couldn't prove the Save entry) →
added public `MainToolbarManager.ContainsEntry` + strengthened the test (D-T2-1). All suites green; `Hrot.Blueprints.Tests`
stayed at the PRE-1 baseline (no new failures) across the run.

## Decisions recorded
DESIGN DEC-A1…A7; run-time D-RUN-1, D-T2-1, D-T3-1, D-T6-1, D-T7-1 (see DECISIONS.md).

## Deferred debt (out of approved MTB2 scope — awaiting direction)
- **DBT-A1 (P3):** delete the now-unused `RecipeCreateModal`/`NewFromRecipeService` (wiring retired; classes/tests kept).
- **DBT-A2 (P3):** remove the duplicate **Scenario-menu** Save/Save-As entries (the File menu now covers them).
- **DBT-A3 (P2):** the **interactive New Asset name/folder popup** (currently a functional default-name create). This
  is a UI feature needing its own design — recommend a small design pass before implementing.

## Pending: your runtime/manual test
The headless suites are green; the visual/interactive behaviors want an eyeball in the live editor:
- toolbar icons have margin + visible hover/toggle; Save icon present; New button present;
- Ctrl+S saves the active document in canvas perspectives and the scenario in the "Scenario" perspective; Save
  menu/tooltip reads `Save [kind: name]`; perspective switcher shows "Scenario";
- New Asset (button / File menu / Ctrl+N) opens the recipe Tree picker → creates + opens.

**Workstream status: 7/7 tasks ✅ complete and verified.**

---

## Post-runtime corrective round (2026-06-12, after your manual test)
Your runtime test surfaced 4 bugs + promoted the New-asset dialog to a real requirement. All fixed/delivered
(lead delegated to Zoo per the lead-only correction; the icon visual was done directly before that correction):

| Item | Fix | Commit |
|------|-----|--------|
| BUG-A4 toolbar icon UX | smaller inset (0.72), restored white hover frame, distinct `HeaderActive@0.85` toggle fill (composable) | `2dec34d3` |
| BUG-A1 New Asset crash | open the **catalogued** asset (`FindByAssetId`), not the minted adapter; scenario skipped | `0329272a` (BATCH-37) |
| BUG-A2 scenario Save via icon | scenario-context Save always enabled; named→SaveCurrent / unnamed→SaveAs; label always names the scenario | `3533c059` (BATCH-38) |
| BUG-A3 no save feedback | document save reports `[OK] Saved {Kind}: '{Name}'.`; scenario save sets the status line | `3533c059` (BATCH-38) |
| **MTB2-T8** generic New/Save-As **name+subfolder modal** (DBT-A3 + DBT-2) | `INameFolderDialog` + `KnownSubfolders` (model) → `AssetNameFolderModal` (name + logical folder tree + create-subfolder) wired into New + document/scenario Save-As | `7a3364a3` + `c0e8ba27` (BATCH-39/40) |

## Remaining — user-gated holds (no further work without your go-ahead)
- **DBT-A1** — delete the retired `RecipeCreateModal`/`NewFromRecipeService` classes: **awaits your approval** (only
  after you confirm new-asset creation works).
- **DBT-A2** — the old **Scenario menu** is intentionally **kept** per your directive until the replacement is confirmed.

## Please runtime-test (the headless suites are green; visuals/interaction need your eye)
1. **Icons:** clearly smaller w/ margin; **hover = white frame**; toggled = filled highlight; toggled+hovered shows both.
2. **New Asset** (New button / File→New Asset… / Ctrl+N): recipe Tree picker → pick → **name+folder modal** opens
   (edit name, pick/create a subfolder) → Create → asset opens. No crash for Blueprint/BTree/HSM/Scenario.
3. **Save:** Ctrl+S in a canvas saves the active doc with feedback; in the "Scenario" perspective it saves the
   scenario (unnamed → name+folder modal); Save menu/tooltip reads `Save [kind: name]`.
4. **Save As** (document + scenario): opens the same name+folder modal.

If all good, say the word on **DBT-A1** (delete retired classes) and whether to retire the duplicate Scenario-menu
Save/Save-As (**DBT-A2**).
