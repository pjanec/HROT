<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §0 the Start screen (D31 Recent) and the live storyboard ①, ⑧
  - NOTE_Backend_To_Ui_Start_Screen_Fidelity_Done.md — backend's fidelity pass (96f4bd367) this corrects
-->

# NOTE — coordinator → ui: the Start screen on the REAL editor does not match the storyboard — fix it next (before Z-7 continues)

🔒 User, `2026-11-08`, with a Windows screenshot of the editor's Start screen: *"it look like the lane building it never saw it"*. Measured — it never
did: backend's fidelity pass (`96f4bd367`) was checked on a SYNTHETIC menu (`StartGateTests.cs:193-268`: items labelled `"Open Scenario"`, `"Open
Recording"`), while the editor's File menu says *Load Scenario…, New Scenario…, Load Checkpoint…, Open Exercise Recording…, Close, Exit, About*.
(The start-up crash on the same screen is fixed: `36d3e5877`.)

| what the user sees | cause (measured) |
|---|---|
| no icons on the actions except *About* | `StartSurfaceWindow.ActionLook` (`:231-240`) matches LABELS (`"New Scenario"`, `"Open Scenario"`, `"Open Recording"`) that the real menu never uses |
| *Load Checkpoint… (unavai…* and *Open Exercise Recording…* run past their buttons | the column is a fixed `ActionWidth = 220`; the label is the menu's own text, the disabled reason included (`:66` builds `StartButton(a.Label(), a.Enabled(), a.Invoke)` — no `Why`) |
| the row buttons' icons sit ON the words (*●ive*, *✎dit*) | `DrawStartButton` (`:261-275`) pushes the label right with `ButtonTextAlign 0.72` inside a fixed `RowButtonW = 66` — with the user's font metrics the words have no room, so the glyph overprints them |
| the Recent heading is cut (*AAR of the lates…*) | one long line in a fixed `RecentColumnWidth = 470` |

**Fix (the shape, yours to build):**
- key the look (icon, short words) on the menu item's **command id** (K5: every menu item runs a declared command), never on its label;
- the reason a disabled action is greyed goes to the **tooltip** (`StartButton.Why`), not into the label;
- widths come from **measured text** (`CalcTextSize` + glyph + padding), not constants; the row buttons draw glyph, then words at glyph width + a gap;
- the heading wraps, or shortens to *Recent* with the rest as its tooltip.

**Acceptance:** render the **real editor host's** Start screen (its own File menu, not a test menu) under Xvfb with the rail's shot hook, and compare
it with the storyboard ① / ⑧; measured rails: every action has its icon, no label overflows its button, each row button's words start right of its
glyph. Put the side-by-side in the report (R-4146). ⚠ Check it at a UI scale above 1 too — the screenshot's font is larger than the rails'.

---
_coordinator lane, 2026-11-08_
