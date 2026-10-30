<!--STATUS
state: LIVE — ui → coordinator: HANDOFF_Coordinator_To_Ui_Editor_Polish done — CE-4111, CE-4114, CE-4115, CE-4112
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Editor_Polish.md — the handoff this answers
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.16 CE-4111 (measured, designed, built) · §2.6 CE-4112 addendum
  - ../../DESIGN_Cluster_Load_Phase.md — §11.7b CE-4115 as built
  - ../../UX/UX_Feature_Menu_Follows_Focus.md — §MB.4 CE-4114 as built
-->
# NOTE — ui → coordinator: editor polish done (four items)

**Dispatched at** the commit that adds this file (`ui`). Built by ONE warm Sonnet worker in the main checkout, in the handoff's order; each diff
and its rails reviewed here; screenshots looked at.

| id | as built | rails (each red-proved) | look |
|---|---|---|---|
| **CE-4111** | ⭐ measured first: the icon showed the CURRENT mode, the check mark the ARMED one (a recording open correctly does not arm AAR, R-1043) and the current item was greyed ⇒ ONE `RunModeChoice.Shown` read by both; the shown item checked + enabled (choosing it is a no-op); a run's header adds *"· loads go into: {armed}"*; `IsArmed` → `IsShown` by Roslyn (3 files). Design: AQ108 §2.16 | `MainToolbarTimeControlTests.CE4111_*` ×3 | model rails |
| **CE-4114** | `Windows` under `View`; unlisted top-level menus after the rightmost listed non-Help menu (`FirstTrailingMenu` gone); Help = the silk question-mark icon, flush right | `TopLevelMenuOrderTests.CE4114_*`, `TheHelpMenuIsAnIconTests` (xvfb) | [menu bar](../../img/ce4114-menubar.png) |
| **CE-4115** | full-display modal, black veil α 0.65, text at `SubtitleBand.TextSizeFor(h)` centred on 0.382·h, modest Cancel under it (operator only; the player's wait screen the same without) — the model untouched | `TheOneTimeFacadeTests.CE4115_*` ×2 (xvfb) + the CE-1442 / CE-1445 rails green | [splash](../../img/ce4115-splash.png) |
| **CE-4112** | `HROT-wallpaper.png` embedded beside the silk atlas (one resource reader); `StartSurfaceWindow` cover-crops it under a dark band behind the menu (α 0.55, readability over the bright arrow) | `StartGateTests.CE4112_*` ×2, `TheStartScreenShowsTheWallpaperTests` (xvfb) | [Start](../../img/ce4112-start.png) |

⚠ Found and filed: **CE-1455** — `EditorScenarioSessionLoadTests.CE1443_S3_*` (a load under armed Preview goes Live) is red at the commit
before CE-4111 too; not investigated. ⚠ CE-4115's "swallows the click" is proven by hover (Xvfb cannot inject a click), not by a real click.

_ui lane, 2026-10-30_
