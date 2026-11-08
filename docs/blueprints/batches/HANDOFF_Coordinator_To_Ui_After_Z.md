<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-08
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Bookmarks_And_Notes.md — E7 (U-10)
  - ../../DESIGN_Recording_Storage.md — §8 the mockup, the ui half of CE-4330 (U-11)
  - ../../UX/DESIGN_Colour_Icons.md — §2, §I1 (U-2)
  - ../../UX/DESIGN_UI_Conventions.md — §2 trees, §4 headers (U-4, U-7)
  - ../../UX/DESIGN_Map_Interaction.md — the armed tool visible (U-8)
-->

# HANDOFF — coordinator → ui: after batch Z

**For:** ui. **From:** coordinator. **Dispatched at `99699401a`**. ⭐ Scope FROZEN there. Merge `coordinator`, `backend` and `behaviors` first, push
`chore: started HANDOFF_Coordinator_To_Ui_After_Z at <sha>`, then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file
(`STATUS_Ui_After_Z.md`) after every item, fetch and merge `backend` after every item. ⛔⛔ Anything needing the user is skipped (R-4156).
⭐ **The fidelity rule (R-4146) on every UI item: render the REAL host's frame** (its own menus and data, never a test stand-in) under Xvfb, compare
with the mockup / the user's report, report the side-by-side.

🔒 User, `2026-11-08`: *"check what next to give them"* · *"pls do not send every little thing separately, just record as tracked items"* (R-4178).

| # | id | what | basis | done when |
|---|---|---|---|---|
| **U-1** | — | **the Start screen on the REAL editor** (the user's Windows screenshot): icons keyed on the menu's COMMAND, not its label; the disabled reason in the tooltip; widths from measured text; row buttons draw glyph then words; the heading wraps | [NOTE_Coordinator_To_Ui_Start_Screen_Real_Menu](NOTE_Coordinator_To_Ui_Start_Screen_Real_Menu.md) (its acceptance) | the real editor's Start screen matches storyboard ① / ⑧, also at a UI scale above 1 |
| **U-2** | CE-4333 | **ONE icon-button chrome** — `IconButtonChrome` into `NodeEditor.Core.Widgets`; `GlyphWidgets`, `IconWidgets` and the mode indicator draw through it | [NOTE_Coordinator_To_Ui_One_Icon_Button_Chrome](NOTE_Coordinator_To_Ui_One_Icon_Button_Chrome.md) | its acceptance; Colour Icons §I1 folded |
| **U-3** | CE-4337 | *Keyboard shortcuts…* → **View ▸ Settings** on every host (R-4178 supersedes your R-4176 #3 build) | R-4178 | the menu-bar rails |
| **U-4** | CE-4334 | the ORBAT tree's row icons (CHEVRON · ICON · LABEL) | UI Conventions §2 | a frame rail: every row has its icon |
| **U-5** | CE-4335 | Mission summary's *End exercise* greyed (with the reason) outside a Live run | AQ108 §0 | a rail per mode |
| **U-6** | CE-4336 | Data breakpoints: *Add* opens the condition first; *Show condition* works on any row | `DataBreakpointManagerPanel.cs:231, :259` | rails for both |
| **U-7** | CE-4338 | the Brain tab's expanders: the icon of what each section is about | Colour Icons; UI Conventions §4 | a frame rail per section |
| **U-8** | CE-4339 | 3-D map placement (Paste and the tool): a direction line from the centre toward the drag while dragging | Map interaction | a frame rail mid-drag |
| **U-9** | CE-4340 | 3-D map hover: the pick volume sized from the entity's bounds (the label showed ~15 m away) — measure first | — | a rail: no hover beyond the entity's bounds + a margin |
| **U-10** | CE-4325 | ⭐ **E7 bookmarks and notes, whole** — B / the ⚑ on the timeline corner, the popup, the Marks window, `notes.json` with the recordings, the master's one list + one managed topic + one CRUD message, the write-back on leaving AAR; the **S / W axis toggle moves to the timeline corner** | [DESIGN_Bookmarks_And_Notes](../../UX/DESIGN_Bookmarks_And_Notes.md) (R-4177), mockup `recording-marks-visual.svg` | §2's rules as rails; the mark's size measured on the largest scenario |
| **U-11** | CE-4330 (ui half) | the recording-storage UI over backend's facts **as they land**: the status bar segment and the Transfers popover (fact ① is on every ClusterView — backend's NOTE), then the picker chips and the dialogs when facts ② ③ land; skip what is not there yet | [DESIGN_Recording_Storage](../../DESIGN_Recording_Storage.md) §7.3, §8 | the mockup's six status states and the popover rendered from real jobs |
| **U-12** | CE-4129 · CE-4280 · CE-4273 | small: ▶ refused (with the reason) while a load runs and the empty world says so · the HUD feature texts' old key names · the Car's thumbnail capture | each row | each row |

⛔ **Not here:** the Review perspective (behaviors) · recording storage's model (backend) · CE-4341 outer-band height (backend).

## Gates

The CLAUDE.md gate contract, rows 1–8; display rails under `xvfb-run -a` (a skip is not a pass). Row 8: U-10 adds a topic and a master writer ⇒
the orchestrator suites and `ClusterRunner.Integration.Tests`; U-2 touches every glyph button ⇒ the Presentation frame rails.

---
_coordinator lane, 2026-11-08_
