<!--STATUS
state: LIVE — coordinator → behaviors: rules S4, the rest (B3–B5 in the measured shape)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — S4 as built (E-1 MemberEvaluation, E-2/E-3 measures) · SR-72 / 72a · SR-75 · SR-111 · SR-112
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-3 / PH-6 read what this adds (`Group`, pass mark) — ⚠ its leans await the user
  - ../../DESIGN_Rules_View_UI.md — RV-4's lanes provider (the score-history lane joins it); RV-8 the summary
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_And_View_Data.md — the parent handoff (Part B); this replaces its B3–B5 rows with the measured shape
-->
# HANDOFF — coordinator → behaviors: **rules S4 — the scores the screens read**

**Dispatched at** `eabedf562` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-03`:** *"What node is going to calculate these score tables, where and how stored? How the scoring relates to objectives and measures?"* → *"Write handoffs to behavior and backend."*

## The measured shape (`2026-11-03`, on `behaviors`) — why most of B3 is already true
The scores ARE the objective tree: every objective / measure / evaluation is an entity whose behaviour runs on the Brain; a composite recomputes its
`Score` from its children by weight each tick (SR-75); each node's `ObjectiveStatus` + `ObjectiveLinks` replicate (`dtObjectiveStatus`) and are recorded.
`MemberEvaluation` (E-1) builds a per-member subtree — `Member standards` (`WeightedMean`) → measures + `NoHarm` — and a squad leader's macro nests another
`MemberEvaluation` ⇒ ⭐ the roll-up up the command chain already exists BY CONSTRUCTION. ⛔ No screen aggregates (RV-8, PH-1): everything below is Brain-side.

## Items
| # | item | spec | done when |
|---|---|---|---|
| ① | **role composites** — `MemberEvaluation` also keeps ONE composite per role (`TacticalDesignation`, else class) under the side root (*"All riflemen"*, *"All MG gunners"*), its children the members' `Member standards` roots; following the chain live like the rest | SR-111 (scores by ROLE) | the summary's by-role rows are plain reads; rail: a reinforcement joins its role's composite |
| ② | **pass mark + `Group`** — a pass mark on a status (`Pass`, 0..1, default per kind) and a measure `Group` (mission · fire · reports …) taken from the evaluation macro, never hard-coded; ⭐ first MEASURE whether either already exists under another name | SR-111, SR-112, PH-6 | GO / NO-GO = `Score ≥ Pass` is a read |
| ③ | **CE-2378** — a `required` measure does nothing (a measure never turns Failed) | the row | ⏳ ⚠ the user has not yet answered the lean (*a required measure below its pass fails its composite*) — build it once answered |
| ④ | **score history (B4)** — an indexed timeline lane of `Score` changes (on a change ≥ a threshold, or a slow sample) joined to RV-4's `ScenarioRulesTimelineLanes` provider — ⛔ no new store; the recording already holds every frame for the AAR | SR-103, SR-111 (trend) | live trend + AAR + recording read ONE history; ⚠ RV-4's provider owner is undecided (lean: ui builds it, you review) — coordinate on the branch |
| ⑤ | **the debrief data (B5)** — confirm every PH-6 field is a read of the tree (per standard: `Value`, `Target`, the squad's mean = its role / squad composite, GO by ②; own root vs squad vs platoon roots) and add what is not | SR-112, PH-6 | the ui debrief draws from reads only |
| ⑥ | per-player GAME score inputs the backend needs (kills, assists from CE-4155 attribution, team kills) exposed as measures or events it can read — coordinate with backend's handoff (HANDOFF_Coordinator_To_Backend_Player_Score.md) | SR-112, S6 §00.12 | backend's per-player score reads them; ⛔ one counting mechanism |

## Gates *(Rule 8)*
Touched classes, new rails red-proved · one live run with a `MemberEvaluation` on a platoon: the role composites and pass marks on the wire (`GET /objectives?tree=1`) · the gate scripts incl. `ned-wire-inventory.py --check` · fold the as-built into `DESIGN_Scenario_Rules.md` §00.10 (S4).

_coordinator lane, 2026-11-03_
