<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Seats_And_Hats.md — §3.3 H3, R-4164 (the hat is derived)
  - ../../UX/DESIGN_Input_And_Commands.md — K6 / Q6 (the shortcuts window, overrides)
-->

# NOTE — coordinator → ui: your two QUESTION notes, answered (R-4176)

🔒 User, `2026-11-08`: *"Yes, approve the ui leans"*. Answers to
[NOTE_Ui_To_Coordinator_Hat_Menu_And_Observer_Camera](NOTE_Ui_To_Coordinator_Hat_Menu_And_Observer_Camera.md) and
[NOTE_Ui_To_Coordinator_Shortcuts_Menu_Home_And_Overrides](NOTE_Ui_To_Coordinator_Shortcuts_Menu_Home_And_Overrides.md):

| # | point | ✅ decided |
|---|---|---|
| 1 | the hat menu's clicks | **keep as built** (`20576f7dd`): an informational menu — the hat follows what you do (R-4164) |
| 2 | the Observer seat's camera (H6) | **keep as built** (`c301b0a08`): the Observe hat's free / Follow camera, limited by the seat |
| 3 | where *Keyboard shortcuts… Ctrl+K* lives | ⭐ **Edit where a host has an Edit menu; Help ▸ Keyboard shortcuts… elsewhere** — drop the Edit menu you added to hosts that had none (Player, Replay Browser, SimHost …); update the three shell menu-bar rails back. Fold it into your next push |
| 4 | per-user binding overrides + rebind | **parked** — when built: one JSON (`actionId → binding`) in the user folder beside `UserRecentPicks`, applied over the declared defaults at start-up, conflicts refused (Input design Q6); the window gains *Rebind…* per row. ⛔ not scheduled |

FYI — backend's next batch ([HANDOFF_Coordinator_To_Backend_Recording_Storage](HANDOFF_Coordinator_To_Backend_Recording_Storage.md)) builds the recording
storage model (CE-4330); its status bar segment, dialogs and picker chips come to you as a follow-on once backend's replicated jobs land — the
mockup is `docs/UX/recording-storage-visual.svg`. Nothing for you now.

---
_coordinator lane, 2026-11-08_
