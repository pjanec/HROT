<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-05
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — the shared Timeline (CE-4262…4265 touch it)
  - docs/designs/replay-browser-2/DESIGN.md — the Replay Browser (CE-4260, CE-4261)
  - docs/DESIGN_Rules_View_UI.md — the rules view (CE-4265)
-->

# HANDOFF — coordinator → ui: the editor bug and UX batch (user reports, 2026-11-01…11-04)

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `574bad50d`**. ⭐ Scope FROZEN there;
later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒ a NOTE
to coordinator.

## Why

🔒 User, `2026-11-05`: *"pls check recent bug reports there are many"*. These are the open rows filed since 11-01 on your surfaces —
almost all the user's own reports, each in the [tracker](../Blueprint_Issues_Tracker.md) with its measurement and the user's words. The
backend lane takes Deploy (joining / spawning) in parallel; ⚠ your Timeline plan (CE-1301…1306) and the earlier replay UI-state handoff
(CE-4047, CE-4049) are unchanged — order them against this batch as you judge, **the four 🔴 first**.

## The items

| # | row | what | note |
|---|---|---|---|
| **1** | 🔴 **CE-4258** | Run Setup's param table: *reset* on an overridden row **crashes the editor**; two entity-link rows clash in ImGui ids | crash first |
| **2** | 🔴 **CE-4260** | the Replay Browser's *Save current as default* overwrites the EDITOR's layout, and the browser does not reopen with its own | the window shell |
| **3** | 🔴 **CE-4264** | dragging the playhead stops when the mouse moves fast — the drag updates only while the mouse is over the one-row ruler | capture the drag |
| **4** | **CE-4265** | rules view: panning the timeline left draws the span bars over the lane labels | clip |
| **5** | **CE-4262** | the director's five tracks always show at the top of the editor's timeline | show them where the director is in use |
| **6** | **CE-4263** | clicking a timeline event should show the EVENT in Details (today: its subject entity) | ⚖️ small design question in the row — answer it in the Timeline design |
| **7** | **CE-4261** | the Replay Browser gets the main toolbar (its own contents: the replay transport), *File ▸ Load .fdp…*, *File ▸ JSON…* | |
| **8** | **CE-4257** | Details ▸ Summary names the entity: TKB type by name + number, DIS type (seven numbers), culture-invariant | |
| **9** | **CE-4256** | ORBAT: no *Filter* label; three icon toggles on the filter line replace the *Command / Structure / Scenario* radios | |
| **10** | **CE-4255 · CE-4253** | one *File ▸ New…* submenu (New Macro… included); no *Scenario* main menu — its items move to *File* and the scenario parameters as the row says | menus |
| **11** | **CE-4242 · CE-4147** | two reds: the loading-screen veil rail (deterministic under xvfb) and `EditorScenarioSessionLoadTests.CE1443_S3…` (predates CE-4140) | R-131: fix or justify |
| **12** | **CE-4219** | fold the remaining undo / condition duplicates into the shared ones (R-174) | |
| **13** | **CE-4254** | ONE generic field-declaration editor — ⚖️ **DESIGN FIRST** (a `docs/` design with UML), no build in this batch | |

⛔ **NOT in this batch — held by the user:** CE-4251 (■ in a live run ends the exercise…) and CE-4252 (the run-mode indicator before ▶):
*"Record it, please, as a tracked item. Do not send to UI now."*

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · a rail per fixed bug, red-proved (a crash rail for CE-4258) ·
display rails under `xvfb-run -a` for any drawing change (`…FrameRail`; a skip is not a pass) · every red confirmed pre-existing against
the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-05_
