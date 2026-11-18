<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS PlatoonDefend / PlatoonAttack, the post's cover search, the base of fire
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS SupportByFire / Suppress and the squad drills
  - ../../OVERVIEW_Behaviour_Building_Blocks.md — §8a hurt / down / dead (what a behaviour can rely on)
  - PROTOCOL_Autonomous_Lanes.md — HOW (incl. §6a: scenarios per row are proportionate)
-->
# HANDOFF — coordinator → `behaviors`: THE STREET FIGHT FOUGHT IN THE OPEN (CE-4666…CE-4669)

🔒 User, `2026-11-18`: *"the guys are fighting like 200 years ago - standing in the middle of the street in groups, firing at the other group, not
trying to take cover, not changing positions, not looking realistically."* The measured report (another lane's live read of `pc-defend-house` moved onto
real-1, `coordinator@2e274c5fd`, ~8 min in) is in the four tracker rows — the rows are the truth.

## Priority
**Next, after the row you are on** — before your remaining second-tier fight rows (CE-3463 / CE-3464 / CE-3465 / CE-3444). It is the same family and
the most visible one: these are the core of every fight. ⚠ CE-3444 (a unit thrashes between cover points) and CE-3464 (a pinned man walks instead of
crawling to cover) may share a cause with CE-4666 — say so in your status if you find it.

| order | row | the question to answer first |
|---|---|---|
| 1 | **CE-4668** dead men still run behaviours | small and certain: a unit at hp 0 must run nothing (OVERVIEW §8a) |
| 2 | **CE-4667** `SupportByFire` / `Suppress` never exits | out of ammo ⇒ reload / resupply / stop; no live target ⇒ stop — what does the design say the exit is? |
| 3 | **CE-4666** bunched in the open, no cover | ⭐ FIRST separate the scenario from the behaviour: rerun with Red's `SearchRadius` 20–25 (posts on the centreline, walls 9–18 m away) — "no cover found" vs "found but not used"; then Blue's base of fire starting IN the street. Answer the report's question (is "stay at the point, in the open" the intended fallback?) from the design, and the ROE-unset question (`FireUnset / ReactionsUnset / ForceUnset` — intended default for demo units?) — a choice no design makes ⇒ `DECISIONS_Behaviors.md` |
| 4 | **CE-4669** squads idle with no behaviour; Locomotion "moving" while still | is the readout stale, or is the move really stuck? (it appears in all four rows) |

## The scenario (R-4277)
If the real-1 street version of `pc-defend-house` is not in the repo, build it as a feature-test scenario (e.g. `pc-street-fight`) with expect rows
for each defect: the base of fire within N m of cover and not standing; defenders off the centreline; nobody suppressing with no ammo or no live
target; no behaviour on a dead unit. ⭐ Per §6a: this scenario + the feature's own set per row, the wide set once at the end in the background.
