<!--STATUS
state: LIVE — ✅ APPROVED 2026-10-21 (R-2065) — user: "Aq99 Approved. You build it."; build: CE-2336
updated: 2026-10-21 (A revised per type after the user's crowd question)
build-state: BUILDING
current-answer: §0 the decision · §2 the sub-questions with leans
stale-below: nothing
known-rot: none yet
known-conflict: docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §8.1 bc-room-clear ③ "no friend hit" — B below would move it to BC-4/BC-5; not reconciled until answered
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS the room drill (§7.1, §7.1b as built) and the demo acceptance (§8.1) this question revisits
  - FDP/Engine/Fdp.ModuleHost/docs/car-kinem-implementation-design.md — OWNS RVO-Lite (§2), the avoidance A changes the person radius of
  - docs/DESIGN_Building_Interiors.md — OWNS doors in the navmesh (§5c, §5d): the 0.3 m body the mesh is baked for
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — OWNS a man down in the ground model (§5h, R-3027, CE-2321 — backend): C refines it
  - docs/blueprints/Architect_Question_98_Cover_Reaction_Fights_Back.md — the neighbouring open question on the same night's measurements (the cover reaction)
-->

# Architect Question 99 — the clearing demos: how close two men may stand, and what a cleared room may cost

## 0. The decision, in under a page

📐 **Measured `2026-10-20`, the `bc-room-clear` / `bc-house-clear` twins (CE-2257, CE-2259) with a per-man drill + navigation trace:** after
CE-2334 and CB-64 the room drill works end to end (stack 11 s → breach → entry 13 s → **RoomClear 30 s**, both insurgents down). Two things
remain, and both are yours:

1. **A person's avoidance shell is 1.6 m.** The Pedestrian preset's `AvoidanceRadius = 0.8` (`VehicleClass.cs:127`) and RVO-Lite's hard
   shell is twice it (`RVOAvoidance.cs`, `combinedRadius = avoidanceRadius * 2`, with a "strong spring"). No TKB type overrides it (only
   tanks author one). So two men can never stand closer than ~1.6 m: a stack 0.8 m apart, a 1.25 m walkable hallway, a man passing another
   in a doorway — all fight the spring. It is most of what stalled the drill tonight.
2. **Two alert, posted insurgents kill two or three entry men in every run** — 📐 6 runs of the finished drill: RoomClear in 4, both
   insurgents down in 4, friends lost 2, 2, 2, 3, 3, 3 — whatever the drill does: they are aimed at the door when it opens. The demo's
   acceptance ③ says "no friend hit".

**Decision (recommended): A + B + C** — soldiers and security forces get the body the navmesh is baked for (0.3 m) while civilians
keep their 1.6 m (A, per type), and BC-1's ③ becomes "no friend
hit BY A FRIEND" with the casualties reported; "no friend hit at all" stays the target of BC-4 (a grenade before entry) and BC-5; and a man down is stepped over where nothing lets
the others walk round him (C).

**Rejected:**
- *0.3 m for every person* — packs protesters and rioters into one horde (user, 2026-10-21); per type instead.
- *drill-side workarounds for 1.6 m* (wider stacks, men standing off) — a 2 m hallway cannot hold a 1.6 m shell; it moves the jam, not removes it.
- *making insurgents unaware* — hides the drill's real weakness (no suppression of the doorway) instead of naming it.

**Design docs checked:** car-kinem design §2 — applies: it OWNS RVO-Lite and gives no reason for 0.8 m for a person · Building Interiors
§5c — applies: the mesh is baked for a 0.3 m body · Building Clearing §8.1 — applies: it OWNS ③ · §1 doctrine (Battle Drill 6) — applies:
the drill assumes speed, surprise and often a grenade; a prepared defender in the room is BC-4/BC-5's case.

## 1. INVENTORY *(codebase-memory CLI `search_graph` `.*Avoidance.*` / `.*RVO.*` — production: `RVOAvoidance.ApplyAvoidance`,
`CarKinematicsSystem.ApplyCollisionAvoidance`; the only authored `AvoidanceRadius` values are 7 scenarios' tanks (5 m); `check_index_coverage`
not run — CLI only)*

| claim | code | design |
|---|---|---|
| a person's shell is 1.6 m | ✅ `VehicleClass.cs:127` (0.8) · `RVOAvoidance.cs` (×2) | ⛔ searched car-kinem §2: no reason given for a person |
| the mesh's body is 0.3 m | ✅ `RecastNavmeshBaker.cs:70` | ✅ Building Interiors §5c |
| 0.3 m changes no person rail but one premise | ✅ measured: `Fdp.Toolkits.Tests` CarKinem/Navigation/Formation 646/647, `Hrot.SimHost.Tests` crowds/squads/civilians 210/210 | — |
| the one that moves is a PREMISE, not a regression | ✅ `CE2304_AWalkerPushedByTheMenBesideHim_StaysInsideTheWall`: "the men beside him pushed him north — else this proves nothing"; at 0.3 m men 0.6 m beside him no longer push — its file of men moves to 0.45 m | — |
| the entry men die to aimed defenders | ✅ 6 runs of the finished drill: 2, 2, 2, 3, 3, 3 lost; RoomClear in 4 | ✅ §1 Battle Drill 6: suppression/grenade before entry is BC-4 |

## 2. The sub-questions

| # | question | ⭐ lean | blast radius |
|---|---|---|---|
| **A** | how close may two men stand? | ⭐ **per person TYPE, not one preset** *(revised 2026-10-21 — 🔒 user: "could we avoid applying the tight spacing for people crowds? Rioting and protesting people taking distance? I am afraid 0.3m would make them one tight hord")*: the TKB kinematics DTO gains an optional `AvoidanceRadius` (guarded `> 0`, like every field beside it, `VehicleKinematicsTkbTranslator.BuildVehicleParams`), set **0.3** on soldier and security-force person types; **civilians keep the preset's 0.8** (1.6 m apart — what the crowd demos were measured on). ⭐ A police line (`HoldTheLine`, security types) stands shoulder to shoulder, as it should. ⏭ A crowd whose spacing follows its MOOD is the crowd's designed `Density` (Civilians §2.1, not built) — a later knob, not this one. ~~Pedestrian preset 0.8 → 0.3 for everyone~~ — rejected: it packs crowds into one horde. 📐 Measured with 0.3 for all (local, not committed): room stack 11 s → 5 s; suites green bar one rail premise | soldiers and security only; a pair of different types is asymmetric (each keeps ITS radius: a civilian steps 1.6 m from a soldier, the soldier only 0.6 m from him) — fine; ⚠ cross-lane (backend: the DTO and translator; BdcTkbBuilder per type) |
| **C** | a man DOWN in a doorway? | ⭐ walk round him where there is room (R-3027 as it stands), **step over him where there is not** — a doorway, a corridor: he is stepped over when no way round fits. ⛔ Not decided here: a one-night step-over (skip him as a neighbour) was built and REVERTED, because R-3027 / CE-2321 (backend) keeps him a neighbour, and the user's words there were about him not moving, not about others walking round | 📐 one body 0.9 m inside room A's door walled it off for every man behind; FDP CarKinem, backend's lane |
| **B** | what may a cleared room cost (BC-1 ③)? | ⭐ "**no friend hit by a friend**" + the casualties in the report; "no friend hit" moves to BC-4 (a grenade before a Suspected room) and BC-5 (contact at the doorway) | the §8.1 table; the twin's ③ assert |

**What would change the lean:** A — a measured need for 1.6 m somewhere (a crowd that must not touch); B — if BC-1 must show a bloodless
entry on its own, then BC-4's grenade comes into BC-1 instead.
