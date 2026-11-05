<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-05
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Player_Join.md — THE design (rev 2, approved 2026-10-31): §2 rulings, §4 diagrams, §5 D-1…D-16, §6 slices, §7 measure-first, §7b/§7c the user's later rulings
  - docs/DESIGN_Node_Features.md — the switch `gameplay.deploy-server` and the `hud.*` features live here (S1 BUILT, §8 as-built)
  - docs/DESIGN_Scenario_Rules.md — §10.3 / SR-82: the score IS evaluation measures (D-15); SR-41 / SR-68: the respawn delay is a scenario parameter (D-14)
  - docs/DESIGN_Cluster_Membership_Use_Cases.md — §2.1 UC-17…UC-24: the multiplayer acceptance recipe (CE-4080)
  - docs/PLAN_Training_And_Gameplay_Engine.md — P-6 fun features (squad spawn, respawn waves) this completes
-->

# HANDOFF — coordinator → backend: DEPLOY — a Player joins, spawns and respawns (Player Join S1 → S3)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `574bad50d`**. ⭐ Scope FROZEN
there; later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒
a NOTE to coordinator.

## Why you

🔒 User, `2026-11-05`: *"I need some other lane to continue with planned gameplay features like joining and spawning / respawning"* ·
*"Only behavior lane is occupied now"*. The design is approved and nothing of it is built (graph `search_graph .*(Deploy|PlayerSlot|Gameplay).*` Class = 7, all weapon / asset
*deployment*, none of D-1…D-16; grep: no `DeployProcessor`, no `gameplay.deploy-server` in `Hrot/` or `FDP/`). ⭐ It is mostly ECS + wire + possession — your ground; the Player-side screens are
small and you build them too, as a declared cross-lane edit (one lane end to end: S1 only works when both halves exist). The ui lane
gets the editor bug batch in parallel.

## The order

| # | item | design basis | acceptance |
|---|---|---|---|
| **0** | ⭐ **CE-4086 first — a Player promotes a replica before its first pose arrives** and holds (and draws) it at (0,0,0) for ~40 ticks. Every deploy ends in exactly that promotion | Player Join §6 "before S1 runs live"; tracker CE-4086 (measured live) | the promotion waits for the pose (or uses it); a rail on the promotion path |
| **1** | **Measure first** — Player Join §7's three questions: does `ClusterView` / the Player read `NodeCapabilitiesTopic`; the exact `-m server` subsystem set (§7b: Brain · Muscle · perception · navigation · orchestrator · traffic, one process); how `StationClaims` (`FDP/Toolkits/Fdp.Toolkits/Behavior/Possession/StationClaims.cs`) reaches the slots | §7, §7b | answers folded into §7 as measured |
| **2** | ⭐⭐ **S1 — join:** the three packs (`Hrot.Gameplay`: server · client · wire), the feature `gameplay.deploy-server` **ON by default where the pack is installed** (R-4115, §7c — D-2's `--gameplay` gate is SUPERSEDED), `DeployRequest` / `DeployStatus` / `PlayerSlotTopic` (D-5), `DeployProcessor` for PLACED units, `Playable { Side, SlotName }` + its editor checkbox (D-9), `StationClaims` reading the slot (D-8), `DeployFlow` + `DeployScreen` (D-4, D-7: waiting · observer · side · unit), aircraft in flight (D-12), observer (D-13), **CE-4156** *"Waiting for the server to load a scenario"* | §4 diagrams, §5 D-1…D-13, §6 S1, §7c | §6 S1's line: a military-sim cluster with the feature OFF runs with **no** gameplay topic on the wire (a rail); with it on, a Player deploys into a placed unit |
| **3** | **S2 — spawn and respawn:** roles per side (D-10), spawn areas as a map Area with `Side` + facing and a server-searched free point ≥ 3 m from anyone on ground / navmesh (D-11), death → kill cam → the deploy screen after the delay (D-14). ⭐ The delay is a **scenario parameter** with default 10 s (§7b); the kill cam and HUD aids are `hud.*` Node Features (R-4113) | §5 D-10, D-11, D-14; §7b | §6 S2's line: a role spawns at a free point of its side's area and is possessed; a full role is refused; death returns to the deploy screen after the delay |
| **4** | **S3:** the scoreboard on `Tab` (D-16, a tap keeps the squad switch), kit variants, choosing the area on the map. ⭐ **D-15 as ruled:** the score is NOT a new counter — it is the evaluation measures whose subject is the player's unit (Scenario Rules §10.3, SR-82); the per-player score (S7) and the kill feed are BUILT — reuse them | §5 D-15, D-16; §7b | `Tab` shows the round's per-player score from the existing measures |
| **5** | **Acceptance live: CE-4080** — a server and two Players play one scenario (UC-17…UC-24: late join, two ask for one unit, pause reaches both) | Cluster Membership Use Cases §2.1 | the UC table re-measured, rows that changed updated in that doc |
| **gap** | when blocked: **CE-4250** (saving while a scenario runs in Preview kills the editor — `TargetMemoryTranslator.Extract` names a preview-spawned entity; ui's note to you leans "drop runtime refs") · **CE-4158** (the full `ClusterRunner.Integration.Tests` batch hangs) · **CE-4266** (is `CreationRequestId` redundant — analysis first) | tracker rows | per row |

⚠ Fences: the orchestrator stays untouched (R-4102); every new behaviour is a switchable Node Feature (R-4108); new recorded components are
unmanaged, a managed one an immutable record (the record rail, CE-4046); no allocation / LINQ on any per-frame path (R-2084). Fold the
as-built into Player Join §6 / §7 per slice.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · each slice's new rails, red-proved · the "no gameplay topic
with the feature off" rail · `RecordedManagedComponentsAreImmutableTests` + `EveryComponentTypeDeclaresItsDataPolicyTests` · a live run per
slice over HTTP (RUNBOOK) · every red confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.
⚠ After merging, restore the test projects once.

---
_coordinator lane, 2026-11-05_
