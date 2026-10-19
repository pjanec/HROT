<!--STATUS
state: LIVE — ui → behaviors: one question with a lean (D4 of DESIGN_Command_Display); ui builds everything that does not depend on it.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Command_Display.md — (ui) OWNS how the sectors, the picture and the report log are drawn; §6 D4 is this question
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — (behaviors) OWNS CommandPicture, StaffInboxSystem, SquadReportLog
-->

# NOTE — ui → behaviors: the command picture and the report log never reach a node with no brain *(UI-5, UI-6)*

**Measured (`2026-10-19`, at `ui@8cfcb646f`):**

| | |
|---|---|
| `CommandPicture` | registered only by `Hrot.SimHost/CognitiveComponentRegistry.cs:34`; no translator / descriptor ⇒ never on the wire. Your design says so (*"read on the brain node"*, CC-12 / CC-16) |
| `SquadReportLog` | filled by `StaffInboxSystem` (`StaffSystems.cs:228`) only AFTER `if (!IsComponentTypeRegistered<CommandPicture>()) return;` — although `SquadReportNotification` reaches every node's bus (Command Chain §5, *"ingress on every node"*) |
| ⇒ | the editor / CGF / ExCon map can show the sectors (`EngagementArea` is replicated — ui draws them now) but **not** the selected commander's picture (UI-5) nor the Reports window's log (UI-6); both work on SimHost's map today |

**ui's lean** (your call — [DESIGN_Command_Display §6 D4](../../DESIGN_Command_Display.md)):

1. **The log:** append to `SquadReportLog` BEFORE the `CommandPicture` guard, and register the inbox wherever the notification is
   registered (verdict `not-owned-here` on a node that owns no commander) — one writer, every node, no new class.
2. **The picture:** replicate `CommandPicture` read-only in the Brain group (fixed-size, NoScenario already; changes only when a report
   arrives).

Rejected: a display copy folded on every node (the brain-only `OrderGiven` reset would make copies disagree) · the window asking the
brain node over HTTP (the map is not an HTTP client).

ui does not wait: the gizmo and the window read whatever the node has, so they light up on every node the day either lands.

---
_ui lane, 2026-10-19_
