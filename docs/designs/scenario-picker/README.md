<!--STATUS
state: LIVE
updated: 2026-11-01 (coordinator — first mockup)
current-answer: the three mockup files below; the decisions are AQ108 D24 (run setup), D25 (the wide picker, CE-4141) and D26 (the generic picker's extensions, shared with the thumbnail entity picker CE-4142)
stale-below: nothing
known-rot: none — ⚠ a MOCKUP of an unbuilt UI; names and numbers are illustrative; the decision rows win where they differ
known-conflict: none
related-designs:
  - docs/UX/DESIGN_Scenario_Presets.md — §5b: the preset combo under the thumbnail replaces the Run setup expander (D24)
  - docs/blueprints/Architect_Question_108_Run_Mode_Picker.md — OWNS the picker, the Start perspective, the modes; D24 run setup, D25 the wide picker
  - docs/DESIGN_Scenario_Rules.md — SR-101 run variants and SR-41 scenario params that the run setup chooses
  - docs/designs/rules-view/README.md — the sibling mockups (rules view, summaries, HUD)
-->

# Scenario picker — UI mockup

> ⭐ How a scenario, a recipe or a recording is picked, and how a run is set up (variant + parameters) without having to choose anything.
> Decisions: [`Architect_Question_108_Run_Mode_Picker.md`](../../blueprints/Architect_Question_108_Run_Mode_Picker.md) **D24** and **D25**
> (tracker **CE-4141**). ⛔ Mockups, not specs: the decision rows win.

| file | shows |
|---|---|
| [`mockup/PickerOpen.html`](mockup/PickerOpen.html) | the ONE generic picker, WIDE: search · the tree on the left with the **Recent** section on top (CE-4098) · a side pane with the scenario's **`preview.png`** large, its title, a training / game badge and its **description**, and facts from the header (terrain, player seats + AI, objectives / triggers, variants / params) · *Variant · random* and *Parameters · defaults* as COLLAPSED expanders · *Open* (into editing) and *Open and go live* (starts at once — no second page) |
| [`mockup/RunSetup.html`](mockup/RunSetup.html) | the SAME picker with the run setup EXPANDED — ⭐ one page, no second dialog: *Variant* (random · a number · as authored) and *Parameters* (defaults filled in, changed ones highlighted, *Reset to defaults*) as expanders in the side pane; a List / Thumbnails toggle in the header (the generic layout switch, CE-4142) · *Open and go live* starts at once |
| [`mockup/PickerRecipeAndRecording.html`](mockup/PickerRecipeAndRecording.html) | **New from a recipe** with NO preview.png — the description alone, no placeholder art · **Open recording (AAR)** — grouped by day, the scenario's preview, the run's result, its variant + params, players, objectives, branch parent · *Rerun variant 3 live* and *Open in AAR* |

**How to look at them:** open a file in any browser (self-contained). The same boards are on the claude.ai canvas
[Rules View Mockup](https://claude.ai/artifact/VLfa5GxVGh3Z7nYsGbf65k) under *Scenario picker*; the committed files win.
