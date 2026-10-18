<!--STATUS
state: LIVE — behaviors → backend: a FRAME (you design the details as step 1, then build).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS this: CB-15 (reports up the chain), §3 SquadReport / ReportKind boxes, §5 module graph. This frame only points at it.
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — §4c W4: the event-topic precedent (BehaviorFaultReport) this follows.
  - docs/DESIGN_Sensors_And_Doctrine.md — G10: the "Reported" modality a later radio model plugs in behind the same topic.
-->

# HANDOFF — behaviors → backend: squad reports on the wire *(BR-3)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; documents that change after
it are FYI only — if one invalidates the item, stop it and say so (R-106).

**What it is.** The user ruled (R-2031, `2026-10-18`): what a squad learns reaches its platoon only as typed reports, and 🔒 *"Reports
need to be networkable, nothing hidden inside single brain only."* Behaviors builds the sender (`ReportOutbox`: the delay, the leader in
the fight) and the receiver (`PlatoonPicture`) — [DESIGN_Building_Clearing](../../designs/group-maneuvers/DESIGN_Building_Clearing.md)
CB-15, `CE-2263`. This is the wire half. ⭐ Build against that design's §3 `SquadReport` box and §5 module graph; fold any deviation
back into it.

**Fences.** ⛔ No report rides inside a brain component or a squad's replicated state — it is an EVENT topic. ⛔ No delay on the wire:
the sender holds a report for its `ReportDelaySeconds` and publishes when due, so the wire is instant and deterministic.

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **BR-3** | **the `SquadReport` topic** | Clearing CB-15, §5; the `BehaviorFaultReport` precedent (`Hrot.Network.NED/BehaviorFaultMessages.cs`, `Translators/BehaviorFaultTranslators.cs`) | an event-shaped topic `"SquadReport"`: `FromUnit` and `ToUnit` (network ids) · `Kind` (byte: Contact, UnderFire, Casualty, RoomClear, RoomBlocked, BuildingClear, CombatIneffective, Detained) · `Subject` (network id: the contact, the casualty, the detainee; 0 when none) · `Room` (int: the room graph's id; −1 when none) · `Position` · `Count` · `SentAt` · `OriginNodeId`. Egress drains a local `SquadReportNotification` on the sender's node; ingress raises the same local event on EVERY node (the sender's own sample skipped by `OriginNodeId`). Installed with the cognitive translator pack | rail: a report raised on one node arrives once on every other node with every field; a report about an entity with no network id is refused at egress and counted; replay records and replays it |

**Ids.** Allocate the tracker row from your block (rule 3) and name it in your report, so behaviors can cite it from `CE-2263`.
