<!--STATUS
state: LIVE — a STEER (pre-empts, protocol §2 ④) · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Street_Fight.md — the street-fight rows (CE-4666…CE-4669); this steer raises their priority
  - ../PLAN_Bug_Priorities.md — CE-4665 is in the P1 family (a move that silently never finishes)
  - PROTOCOL_Autonomous_Lanes.md §6a — scenarios per row are proportionate
-->
# STEER — coordinator → `behaviors`: TWO CRITICAL ITEMS, TOP OF YOUR QUEUE

🔒 User, `2026-11-18`: *"infantry stop moving after a reload and the streetfight are quite critical ones so pls yes, handoff to behaviors please, with
higher priority"*.

## Priority — this PRE-EMPTS your queue
Push the row you are on at its next safe point (WIP to `behaviors-wip-<topic>` if unverified, protocol §1), then take these, in this order, BEFORE
everything else in your queue:

| order | row | what | |
|---|---|---|---|
| 1 | **CE-4665** | infantry stop moving after a scenario reload — the crowd's agent slots are never released (128 max; added at `JoinCrowd`, removed only at `OffMeshLinkDetectionSystem.cs:146`; stale agents matched by a reused `entity.Index` after SoftClear) | ⚠ CROSS-LANE (navigation, FDP toolkit — backend's area): allowed and SAID (commit + status + a NOTE to backend). The tracker row carries the measured cause, the proposed fix (`IDtCrowdProvider.UnregisterAll()` on a world change; release a dead / destroyed unit's slot; warn on a full crowd) and the two rails |
| 2 | **CE-4668** · **CE-4667** · **CE-4666** · **CE-4669** | the street fight fought in the open | as [`HANDOFF_Coordinator_To_Behaviors_Street_Fight.md`](HANDOFF_Coordinator_To_Behaviors_Street_Fight.md) (order and first questions there) |

## CE-4665 — done when
- the two rails pass, each red-proved: a new world + N new entities under the same handles ⇒ each a NEW agent at its own start; 128 registered,
  destroyed, 128 more ⇒ all succeed;
- live, ONE editor process: load a scenario with ≥ 16 infantry four times in a row (`/scenario/load/live`) — every infantry unit moves on the fourth
  load as on the first (the report's measurement: 16 / 16 first, 4 / 16 after three loads);
- a long run with casualties does not exhaust the crowd (the dead-unit release);
- §6a: the movement scenarios that use infantry crowds — a handful, not the family; the wide set once at the end, in the background.
