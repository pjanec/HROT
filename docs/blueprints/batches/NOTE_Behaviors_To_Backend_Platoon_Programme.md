<!--STATUS
state: LIVE — behaviors → backend: the order of the open BR frames for the platoon programme, one new requirement on BR-12, BR-1 split.
updated: 2026-10-18
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — THE programme: §6a sensors, sectors and the roof · §13 the lane split
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Geometry_On_The_Muscle.md — BR-12 (WorldSurvey) and the BR-1 / BR-10 amendments this extends
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Report_Ageing.md — BR-3's fields (ObservedAt, Seq, Sitrep, KindId, SPOT fields, Subtype, RequestId, Synthetic)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md — UI-3 takes BR-1's test house
-->

# NOTE — behaviors → backend: the platoon programme — what we need from you, in order *(BR-1 … BR-12, CE-2287)*

**Re-stamp (rule 1a):** none of BR-1 … BR-12 is in `origin/backend` (checked 2026-10-18), so this note amends them; the dispatched frames
are not edited. The user approved the programme on 2026-10-18 (R-2046) and asked for the work to be split across the lanes.

## 1. The order behaviors waits on them

| order | frame | why first | behaviors item that waits |
|---|---|---|---|
| 1 | **BR-12** `WorldSurvey` (BuildingSurvey · VisibilityFan · PointSurvey, solved on the muscle) | the rooms to clear, the window faces, the posts' views, a contact's storey — every play's Plan reads it | the clearing (`CE-2257`…`CE-2259`), the sensors (`CE-2283`), the geometry moves (`CE-2286`) |
| 2 | **BR-3** the report topic + the [report-ageing note](NOTE_Behaviors_To_Backend_Report_Ageing.md)'s fields (incl. `Synthetic`) | every report up the chain rides it | the staff (`CE-2293`) runs on a local loopback until then, the wire after |
| 3 | **BR-8** infantry LMGs + the new roles on the wire | the roles split every squad play | roles (`CE-2281`) |
| 4 | **BR-6 / BR-7** the spread factor, acquisition by exposure | proficiency changes the fight | the tier ladder |
| 5 | **BR-9** sniper rifle, DMR, scope | the counter-sniper demo | `CE-2279` |
| 6 | **BR-2** stun holds every class · **BR-4 / BR-5** Search / Detain / Restrain, `Lean` | the police clearing | `CE-2266` |
| 7 | **CE-2287** backend's own brain-side geometry sites onto the survey | R-2037 | — |
| — | BR-10 `BuildingOf` | ⛔ already replaced by BR-12's PointSurvey (geometry note) | — |
| — | BR-11 trenches | later (DA-8) | — |

## 2. One new requirement on BR-12 — the roof is a room

The roof must be clearable from the stairs (user, 2026-10-18: *"roof reachable by stairs so even roof can be secured during the house
clearing operation"*). On `house-3f` (DA-22, now built by the ui lane as UI-2) the roof is the top storey: a stair house with a door and a
1 m parapet. ⭐ **BuildingSurvey's derived room graph must give the roof storey as ONE room** (open, bounded by the parapet), **the stair
house as a room of its own, and the stair-house door as the portal between them**; the stair is a portal between the storeys as before.

| rail | |
|---|---|
| `house-3f` | the survey lists the ground and upper rooms, the stair house and the roof room; the stair-house door is a portal between the stair house and the roof; `PointSurvey` of a point on the roof answers storey = roof, room = the roof room, outdoors |

## 3. BR-1 is split

BR-1 was *"the room graph + one CQB test house"*. ⛔ The room graph is now BR-12's derived rooms (R-2038 — never a load-time seam the
brains call). ⭐ The **test house template** moves to the ui lane (UI-3, [handoff](HANDOFF_Behaviors_To_Ui_Platoon_Demos.md)), together
with the shared `Recipes/Buildings/` folder and `house-3f`. ⇒ **BR-1 needs no backend work**; its rails become BR-12's (the CQB house
gives its rooms and portals through the survey).

Please name your tracker rows for BR-12 and BR-3 in your report, so `CE-2283` and `CE-2293` can cite them.
