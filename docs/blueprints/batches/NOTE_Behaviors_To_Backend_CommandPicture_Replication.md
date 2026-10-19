<!--STATUS
state: LIVE — behaviors → backend: one ask (the wire half of CE-2301). Dispatched at the behaviors commit that adds this file.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8b the picture and the log on every node (R-2058), §8a CommandPicture as built
  - docs/DESIGN_Command_Display.md — (ui) D4, the map gizmo and the Reports window that read it
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §13c-1 the precedent: EngagementArea replicated (CE-3261)
-->

# NOTE — behaviors → backend: replicate `CommandPicture` read-only (`CE-2301` ②, R-2058)

🔒 User, `2026-10-19`: *"Replicate the commander's picture read-only. Accepted."* — and, asked whether the commander entity crosses nodes at
all: *"Keep replication, replicate the commander picture as well."* (it does: `EntityInfo.CommanderId` resolves the commander on every
node, `EntityInfoIngressTranslator.cs:216-249`).

## The ask

| | |
|---|---|
| **what** | `Fdp.Toolkit.Squad.Command.CommandPicture` (component 407, `CommandComponents.cs:110`) on every node where the commander entity exists, written ONLY by the node that owns the commander's brain (the `StaffInboxSystem` there folds it) |
| **shape** | three fixed lists (`Elements` ×8 `ReportedElement`, `Subordinates` ×6 `SubordinateRow`, `Requests` ×4 `PendingRequest`), each `int Count` + an `[InlineArray]` — every field a value (network ids, not handles). NoScenario already |
| **group** | the Brain group, like `EngagementArea` (`dtEngagementArea` 151, `CE-3261`) — the same descriptor / translator / ownership-binding road |
| **rate** | it changes only when a report is folded (a few per minute per commander); send on change |
| **readers** | ui's `CommandPictureGizmo` (UI-5) and nothing in a brain: ⚠ a node that does not own the commander must never FOLD into its copy — behaviors gates the fold on authority (`StaffInboxSystem(gateOnAuthority: true)`, part ① of `CE-2301`, behaviors) |

## Acceptance

- the descriptor-map rails (`TheDescriptorMapIsWiredTests`, the ownership-group rail) list it;
- a round trip on two worlds: the owner's picture with two elements and one subordinate row arrives field for field (the
  `BodyPartReplicationTests.CE3261_*` shape);
- a replica's copy is overwritten by the next sample, never by the replica's own inbox.

behaviors builds part ① meanwhile (the log on every node, the fold gated on authority); ui is asked to show "not on this node
yet" while a node has no copy.

---
_behaviors lane, 2026-10-19_
