<!--STATUS
state: LIVE — behaviors → backend: an amendment to the unstarted BR-3 frame (the SquadReport topic).
updated: 2026-10-18
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — §5b OWNS this: a report's life and ageing (CB-31…CB-35)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Building_Clearing_Reports.md — the BR-3 frame this amends (not edited)
-->

# NOTE — behaviors → backend: two more fields and one more kind on `SquadReport` *(BR-3, CB-31…CB-34)*

**Why.** The user asked how the brain avoids acting on old reports. The answer ([Building Clearing §5b](../../designs/group-maneuvers/DESIGN_Building_Clearing.md))
ages a report from when the squad SAW the thing, orders reports per sender, and has squads re-send their status while engaged. ⚠ BR-3
is unstarted (`git merge-base --is-ancestor` checked: not in `origin/backend`), so this note amends it; the frame file is not edited.

| change to BR-3 | ⭐ lean | rail |
|---|---|---|
| **`ObservedAt`** (sim seconds) beside `SentAt` | when the squad saw what it reports; age is measured from it (CB-31) | a report held 3 s arrives with `ObservedAt` 3 s before `SentAt`, on every node |
| **`Seq`** (uint, per sender unit) | the picture keeps the newest per subject and drops a late older one (CB-32) | two reports from one sender, published in order, carry increasing `Seq` on every node |
| kind **`Status`** | men in the fight in `Count`; a small `Flags` byte: combat effective · ammo low · phase bits (CB-34) | a `Status` with every field arrives on every node |

Everything else in BR-3 stands (event topic, egress drains the local event, ingress raises it on every node, sender's own sample
skipped by `OriginNodeId`, replay records it). Please name your tracker row in your report so `CE-2263` (RP-1) can cite it.
