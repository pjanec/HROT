<!--STATUS
state: LIVE
updated: 2026-10-31 (first version — coordinator; the architect review of 2026-10-31 and the user's rulings on it)
current-answer: §1 the rulings · §2 the items, ranked by the user · §3 what to design next
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Player_Join.md — the deploy (side, unit / role, spawn, observe); its S3 score feeds item P-4
  - docs/DESIGN_Node_Features.md — the feature switches every item here is delivered as (presets, side knowledge, fun features)
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — the Player host; interest management deferred there (item P-x2)
  - docs/DESIGN_Entity_Control.md — possession, the FPS station; §8h's prediction covers only the use prompt (item P-x3)
  - docs/DESIGN_Director_Timeline_Editor.md — the recording's timeline index (shots, hits, blasts, state lanes) item P-4 aggregates from
-->

# PLAN — one engine for **military training AND gameplay** *(what is missing, ranked by the user)*

> 🔒 **User, `2026-10-31`:** *"please suggest if we are missing something critical or if there is something that can be easily added to improve
> the flexibility and also the fun factor from the gameplay, because I want the engine to support both."*

## 1. Rulings (✅ user, `2026-10-31`)

| id | ruling | the user's words |
|---|---|---|
| **R-4105** | **The engine is the INFRASTRUCTURE; a frontend is built on top.** A screen (a debrief, a scoreboard) need not be part of the engine — every fact it needs must be GATHERED, AGGREGATED and reachable over the NED protocol | 🔒 *"the engine is oriented towards the infrastructure and backend capabilities, while the frontend is something that is usually developed on top of that, so it doesn't need to be strict part of the engine as is. It just needs to be accessible, all the information needed for that via the NED protocol."* |
| **R-4106** | **AI skill is set per SIDE as an AMPLIFIER, never a leveller** — it raises or lowers every unit's own level and keeps their differences; set in the scenario, changeable during the run (from the game-master mode) | 🔒 *"there needs to be some amplifier that increases the level or decreases the level, but doesn't level all to equal."* |
| **R-4107** | **The game-master ("Zeus") mode is the server's default UI** — what the CGF / SimHost operator UI already is; the server must be ABLE to start headless — by default it is a cluster node with the FULL UI (the shared bootstrap), which IS the Zeus mode; headless or a reduced UI is a choice (✅ clarified `2026-10-31`: *"It doesn't mean it always start headless. Otherwise the Zeus mode wouldn't be possible with the server's UI."*) | 🔒 *"isn't this Game Master Zeus mode? Practically the one we have now by default on CGF and SimHost nodes. I assume this should be the default mode for the server node."* |
| **R-4108** | **EVERY feature is GRANULAR and PLUGGABLE** — nothing hard-coded: each behaviour is a feature declared by its capability, announced as a capability token when composed, and switched on or off (the three tiers of `DESIGN_Node_Features.md`). It applies to every item below and to every new design | 🔒 *"all the features, they should be granular, there should be pluggable. Nothing is hard-coded, everything is a feature that can be switched on or off and announced as a capability. The same approach as we drafted just a while ago."* |

## 2. The items — ranked by the user

⭐ **R-4108:** every item ships as Node Features — e.g. `scenario.rules`, `ui.side-knowledge` (+ `ui.side-knowledge.last-known`), `ai.skill-amplifier`, `eval.performance`, `hud.kill-feed`, `game.round-timer`, `game.tickets`, `game.squad-spawn` — each declared by the capability that implements it, advertised, and switchable per node and per cluster.

| # | item | the user | lean / next step |
|---|---|---|---|
| **P-1** | **scenario rules** — objectives, end conditions, triggers at SCENARIO scope (training: *"secure the bridge by 10:00"*, phase lines, evaluation; game: capture points, time limit, tickets, win / lose) | ⭐⭐⭐ *"The point number one seems to have the most value for me."* | ⭐ design FIRST: a `scenario.rules` feature on the server, authored with the EXISTING Blueprint visual scripting; events (unit enters area, unit killed, timer, objective state), actions (message, spawn wave, end the exercise, award points); areas reuse the spawn-area pattern; every outcome lands on the timeline. ⛔ open: can a Blueprint run at SCENARIO scope (not per entity)? — the first measurement |
| **P-2** | **the game-master mode** (Arma's Zeus: spawn, order, possess any unit, see everything, live) | *"this comes for free for the server node"* — R-4107 | ⭐ no new design: a feature set of the server's UI (Node Features); verify headless start (`HrotRunnerConfiguration.Headless` exists) and that a headless server + a remote game-master UI work |
| **P-3** | **knowledge shared across a team** — a side sees only what the side knows; map layers hide what the other side should not see | *"quite interesting, and that could be part of the features … No idea how."* | ✅ **clarified `2026-10-31`:** the **2-D map only** is limited by what a side knows (*"the 3D map is simply drawing the simulated truth, while the 2D map can be used as an information source"*); the feature is **OFF by default** (the default is Zeus: everything shown); a unit lost from sight leaves a **last-known marker on the 2-D map that fades with time**; optional, separate: knowledge INDICATORS drawn over the 3-D truth (for observers). ⭐ features `map2d.side-knowledge`, `map2d.side-knowledge.last-known`, `map3d.knowledge-indicators` — the 2-D layers draw a unit of another side only when the viewer's side KNOWS it (the perception / contact memory that exists: `KnownCombatantList`, sensor contacts); shared markers and pings per side / squad ride the player slot. Design after P-1 |
| **P-4** | **player performance evaluation** — gather, evaluate, aggregate per player / unit / side (shots, hits, kills, fratricide, exposure, time to objective, orders obeyed …) and make it reachable over NED; the debrief SCREEN is a frontend (R-4105) | ⭐⭐ *"one of the most valuable features of such an engine"* · *"will probably need some separate design"* | ⭐ its own design: the data sources are the recording's timeline index (already lanes of shots / hits / blasts / state changes) and the live bus; P-1's objectives give the "did the mission succeed"; the result is a topic / an HTTP+NED query, not a window |
| **P-5** | **AI skill per side** — set in the scenario, changeable live from the game-master mode, an amplifier (R-4106) | ⭐⭐ *"This is extremely important for being settable"* | ✅ **clarified `2026-10-31`:** a per-side **slider that adds tiers**; a unit already at the top stays at the top (*"the units are kept at their maximum even if the slider tells to increment"*). ⭐ a per-side `SkillAmplifier` in TIERS (e.g. −2 … +2) applied where `CombatProficiency` is READ (`ProficiencyOf.Effective(unit) = clamp(own ⊕ side amplifier)`), so every unit keeps its own level; scenario-authored, a run setting to change live. Today: one value per unit, five presets Untrained / Militia / Regular / Veteran / Elite (`CombatProficiencySettings`, `Fdp.Toolkits/Behavior/Components/CombatProficiency.cs`) |
| **P-6** | **fun features** — kill feed, round timer, tickets, squad spawn, respawn waves | ⭐⭐ *"seems very important for the gameplay fun factor"* | ⭐ each a feature (switchable, R-4104); most need P-1 (timer, tickets, win) or the deploy S2/S3 (respawn, squad spawn, score) |
| **P-7** | **scenario feature presets** — a scenario names its mixture (*Realistic training* · *Arcade*) | (proposed with Node Features) | ⭐ a named set of Node Features' cluster overrides stored in the scenario |
| P-x2 | fog of war as SECURITY (not sending what a side must not see) | *"not so important at this moment"* | ⏭ later |
| P-x3 | client-side prediction of a player's own unit | *"not so important at this moment"* | ⏭ later — measure the LAN latency in UC-17 first |
| P-x4 | voice / radio nets | — | ⏭ later |

## 3. What to design next, in order

1. **P-1 scenario rules** — first: measure whether a Blueprint can run at scenario scope; then the design (events, actions, objectives, areas, where it runs, how it is switched).
2. **P-5 the skill amplifier** — small, high value; one read site (`ProficiencyOf`) and one run setting.
3. **P-4 performance evaluation** — its own design (R-4105: data + aggregation + NED access, no screen).
4. **P-3 side knowledge** — with P-6 and the deploy S2/S3.
