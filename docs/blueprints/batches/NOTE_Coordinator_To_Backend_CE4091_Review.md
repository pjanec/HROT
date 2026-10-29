<!--STATUS
state: LIVE — coordinator → backend: CE-4091 reviewed and accepted; two things CE-4089 must carry along
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Content_Layout.md — L2 (recipes listed only by New), L6 (a recipe is named by its root; the old `Recipes/<seed>` under the scenarios root goes)
  - ../Architect_Question_108_Run_Mode_Picker.md — D7 (the recording keeps its scenario; open the copy as a new unsaved scenario)
-->
# NOTE — coordinator → backend: CE-4091 accepted; carry `RecordedScenarioNames` into CE-4089

✅ **CE-4091 accepted** (`4ccae65e6`): whole-folder copy, stamped at go-live from the SAVED file, a branch inherits its parent's copy, and opening
the copy writes neither the original nor the recording — each with a rail. Nothing to redo.

## ⭐ Two things for CE-4089 (the content move) — not a separate task

| # | what | why |
|---|---|---|
| 1 | `RecordedScenarioNames` (`FDP/Toolkits/Fdp.Toolkits/Orchestration/RecordedScenarioNames.cs`) is a NEW user of the old reserved `Recipes/` prefix under the scenarios root. When CE-4089 moves recipes to the top-level `recipes/` (L6), route this name through the same one authority (`AssetRoots`) so it moves with it — ⛔ not a hard-coded `"Recipes"` left behind | R-4097 L5/L6: one path authority, the old `Recipes/<seed>` goes |
| 2 | ⚠ a staged recorded copy must NEVER be listed by *New from a recipe* — it is not a recipe (L2: New lists recipes only). Today it sits under `Recipes/Recorded/<id>`, i.e. inside the folder New lists. Measure whether the New dialog / start surface lists it; either way add a rail ("a staged recorded copy is not offered by New"), and in CE-4089 give the copies a home that is neither `recipes/` nor `scenarios/` (e.g. the NAS working area for opened copies) | a user would otherwise see one GUID "recipe" per recording they ever opened |

_coordinator lane, 2026-10-28_
