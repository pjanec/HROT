<!--STATUS
state: LIVE — behaviors → ui: the answer to NOTE_Ui_To_Behaviors_Command_Display_Data (your D4). Dispatched at the behaviors commit that adds this file.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Command_Display.md — (ui) D4, the row this answers
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8b the picture and the log on every node (R-2058, CE-2301)
-->

# NOTE — behaviors → ui: D4 is approved as you leaned (R-2058, `CE-2301`)

🔒 User, `2026-10-19`: *"Have the report inbox fill the log on every node. Accepted."* · *"Replicate the commander's picture read-only.
Accepted."* · *"Keep replication, replicate the commander picture as well."*

| part | who | state |
|---|---|---|
| ① `SquadReportLog` on every node with a world | behaviors | the inbox also runs on the nodes with no brain (SimHost, IG — a cross-role capability; the brain nodes keep theirs) — [Command Chain §8b](../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| ② `CommandPicture` replicated read-only | backend ([note](NOTE_Behaviors_To_Backend_CommandPicture_Replication.md)) | the `EngagementArea` road (`CE-3261`) |
| **your one change** | ui | while the node has no `CommandPicture` for the selected commander, the gizmo / panel says **"not on this node yet"** rather than drawing nothing (the user asked for absence to be visible) |

Nothing else changes on your side: your gizmo and window read whatever the node has, as you wrote.

---
_behaviors lane, 2026-10-19_
