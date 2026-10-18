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

# NOTE — behaviors → backend: more fields on `SquadReport` *(BR-3, CB-31…CB-34, CB-37…CB-41)*

**Re-stamped `2026-10-18` (rule 1a, still not in `origin/backend`):** the report vocabulary was settled into the doctrinal set (Building Clearing §5c) — the last three rows below are new.

**Why.** The user asked how the brain avoids acting on old reports. The answer ([Building Clearing §5b](../../designs/group-maneuvers/DESIGN_Building_Clearing.md))
ages a report from when the squad SAW the thing, orders reports per sender, and has squads re-send their status while engaged. ⚠ BR-3
is unstarted (`git merge-base --is-ancestor` checked: not in `origin/backend`), so this note amends it; the frame file is not edited.

| change to BR-3 | ⭐ lean | rail |
|---|---|---|
| **`ObservedAt`** (sim seconds) beside `SentAt` | when the squad saw what it reports; age is measured from it (CB-31) | a report held 3 s arrives with `ObservedAt` 3 s before `SentAt`, on every node |
| **`Seq`** (uint, per sender unit) | the picture keeps the newest per subject and drops a late older one (CB-32) | two reports from one sender, published in order, carry increasing `Seq` on every node |
| kind **`Sitrep`** *(was `Status`)* | combat power and ammo as GREEN / AMBER / RED / BLACK, phase, position (CB-34, CB-37) | a SITREP with every field arrives on every node |
| **`KindId`** (ushort) replaces the byte `Kind` | the report kind registry: standard 1–99 (CONTACT, SPOT, SITREP, LACE, PROGRESS, REQUEST), a CLOSED set (CB-43) — no behaviour-defined kinds | an unknown kind id still crosses the wire and is logged |
| **SPOT fields** | size class (individual · team · squad · platoon), activity, equipment flags (MG · sniper · AT · vehicle), radius (CB-37) | a SPOT round-trips every field |
| **`Subtype`** (byte) + a `RequestId` *(re-stamped: the free payload is DROPPED, CB-43)* | the closed subtype of the kind (PROGRESS: Set · PhaseLine · Foothold · …; REQUEST: Casevac · FireSupport · …); a REQUEST's id that an answering order cites | every kind × subtype round-trips; an unknown subtype still crosses and is logged |

Everything else in BR-3 stands (event topic, egress drains the local event, ingress raises it on every node, sender's own sample
skipped by `OriginNodeId`, replay records it). Please name your tracker row in your report so `CE-2263` (RP-1) can cite it.
