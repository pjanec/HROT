<!--STATUS
state: LIVE
updated: 2026-10-31 (Scenario Rules rev 3 folded — the coordinator lane's session of 2026-10-30/31)
current-answer: §0 read first · §1 where the discussion is · §2 next, in order · §3 the ids · §4 done this session
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/PLAN_Training_And_Gameplay_Engine.md — the ranked items (P-1 scenario rules first) and R-4105…R-4107
  - docs/DESIGN_Player_Join.md — the deploy design (rev 2, packs; §7a the audit)
  - docs/DESIGN_Node_Features.md — the feature switches (three tiers)
  - docs/PLAN_Feature_Films.md — §1d the defects filming found
-->

# RESUME — coordinator lane: **training + gameplay engine, filming** *(after a compaction, start here)*

## 0. Read first
- Lane: **coordinator**, branch `coordinator`, push only there, no PRs. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` +
  `Claude-Session: https://claude.ai/code/session_015HdXr83WacDQJB6gNp8WZV`. Merge `ui` / `backend` into `coordinator` only when the user asks.
- The user is on mobile: plain-text questions with a LEAN, GitHub links on `coordinator`, gloss every id. Long work in the background, never polled.
- Build work goes to ONE standing Sonnet worker (brief it with what was measured). Opus designs and reviews.
- 🔒 **The user is still DISCUSSING the design** (*"No building yet, we still discuss the design"* — said for the deploy; then *"keep discussing
  these topics"*). ⛔ Do not start building P-1 / deploy / features without a go.

## 1. Where the discussion is (2026-10-31, latest first)
1. **Architect review → [`PLAN_Training_And_Gameplay_Engine.md`](../PLAN_Training_And_Gameplay_Engine.md).** The user ranked: **P-1 scenario rules = most
   value** (objectives, end conditions, triggers; lean: Blueprints at SCENARIO scope — ⛔ measure first whether a Blueprint can run at scenario scope);
   P-2 game master = the server's default UI, free (R-4107); P-3 side knowledge (map layers that hide what the other side must not see — *"No idea
   how"*); **P-4 performance evaluation** = infrastructure only (gather/aggregate/NED, no screen — R-4105), *"one of the most valuable features"*,
   its own design; **P-5 AI skill per side = an AMPLIFIER** (R-4106; today `CombatProficiency`, one value per unit, presets Untrained…Elite);
   **P-6 fun features** (kill feed, timer, tickets, squad spawn) *"very important"*; P-x2 fog-of-war security and P-x3 client prediction: not now.
2. **[`DESIGN_Node_Features.md`](../DESIGN_Node_Features.md)** (agreed): composition capability · availability token `hrot.feature.<id>` · switch
   (`NodeFeatures.On(id)`, precedence cluster ▸ node `--feature` ▸ composition default; Live / AtLoad). S1 = the structure + `map3d.in-load` migrated.
   Tracker **CE-4150**. ⛔ open: where run settings are written into a recording.
3. **[`DESIGN_Player_Join.md`](../DESIGN_Player_Join.md) rev 2** (architecture approved): name **Deploy**; a **gameplay server pack** on an ECS node
   (`--gameplay`, `-m server`), a **client pack** on the Player host, own gameplay topics; orchestrator untouched; no server ⇒ observer (R-4103);
   D-3 = the feature `gameplay.deploy-server`. §7a = the audit: **CE-4148** (hit marker / kill cam → `hud.*` features), **CE-4149** (gate policy by
   host kind). ⛔ **The user owes rulings on G1b damage arc, G4 Tab-after-death, G5 spread ring, G6 near-miss** — lean: G1b/G4 gameplay features,
   G5/G6 training aids ON everywhere.
4. Rulings this session: **R-4101** IG = the Player's obsolete name · **R-4102** gameplay is packs, never in the orchestrator · **R-4103** no server ⇒
   observer · **R-4104** a core gameplay feature must be switchable · **R-4105** engine = infrastructure, frontends via NED · **R-4106** skill
   amplifier · **R-4107** Zeus = the server's default UI · **R-4108** EVERY feature is granular, pluggable, announced as a capability and switchable — nothing hard-coded (binds every design).

## 1a. IN FLIGHT (2026-10-31, latest)
- ⭐ **P-1 scenario rules + P-4 evaluation + scenario MACROS — design in [`DESIGN_Scenario_Rules.md`](../DESIGN_Scenario_Rules.md)** (read `current-answer`): rev 3/4 = no interpreter, objectives are ENTITIES running small generic behaviours in the TASK slot (SR-21, SR-37, SR-51); evaluation = objectives with a SUBJECT (SR-19); directors = reusable behaviours ordering down the command chain (SR-17/18). §8–§9 = SCENARIO MACROS: scenario-like assets in `macros/`, inserted as an ordinary entity with position (SR-39a, SR-70), params declared in the header (SR-68), bindings in a `ParamBindings` component (SR-57), entities created transient (SR-42′), positions relative to the instance (SR-69); references = exact GUID · `RefKey` · query (SR-63–SR-66); objective tree = `Parent` + `Subject` links (SR-53–SR-56); member thresholds nearest-wins (SR-67); ORBAT policy CE-4152.
- ⭐ **CE-4153 stable scenario GUIDs** — an EXPERIMENT by a Sonnet worker in worktree `/home/user/HROT-ce4153`, local branch `ce-4153-stable-guids` (never pushed): measure readers of reproduced ids → S1–S7 stable GUIDs (+ a replicated `EntityGuid` descriptor) → S8–S10 drop `NetworkIdentity` from files ONLY if safe; NO backward compatibility (user). Design [`DESIGN_Scenario_Entity_Keys.md`](../DESIGN_Scenario_Entity_Keys.md) rev 3 + §00a. ⛔ review its report + diff before anything reaches `coordinator`.
- Queued after it: **CE-4154** (the World Environment saved in `header.world`, SR-71) · **CE-4151** (a TKB default task gets params) · **CE-4152** (ORBAT inclusion policy).
- ⏭ **Next design topic (lean):** the objective KINDS list + their params, `ObjectiveStatus`, the measures (Count / Duration / Latency / Ratio) and per-member TKB defaults, `ExerciseEnded` — then brief S1.

## 2. Next, in order (ask before building)
1. **P-1 scenario rules**: measure Blueprint at scenario scope (search_graph + the Blueprint designs, R-129 order), then write the design.
2. P-5 the skill amplifier (small): one read site (`ProficiencyOf`), a per-side run setting.
3. Node Features S1 → Deploy S1 (deploy D-3 needs the feature).
4. P-4 performance-evaluation design.
5. The user's G1b/G4/G5/G6 rulings ⇒ fold into Player Join §7a + CE-4148.

## 3. The ids
- Tracker (coordinator block CE-4000…4999): allocated this session **CE-4116…CE-4154 and CE-4199**; **next free CE-4155** (skip 4199).
- Rulings: **next free R-4113** (R-4109…R-4112 = scenario control).
- Standing Sonnet worker: agent `ae49170f7c3d5d648` (context ~480 k — start a fresh one if it is gone or too full).

## 4. Done this session (2026-10-30/31)
- Filming: the clip pass made 50 clips (+ F-08 re-framed), issues **CE-4130…4139** (PLAN_Feature_Films §1d); the user got all 51 as a zip.
  ⚠ the MP4s live only in the scratchpad (lost on a container reset). F-02 `demo-city` not filmed — the user's "no entities / no SUMO cars in
  Live" is NOT reproduced here (works on this build); the user's `logs/editor_0.log` is asked for; lean: **CE-4131** (the editor slows with every
  load) explains "it worked the first time".
- Built + merged: CE-4119 (sidecar FrameSim) · CE-4125 (why an anchor is unresolved) · CE-4127 / 4128 (camera sync, film font) · CE-4143
  (a player's fast turret gun fires where he looks, `L` switches lock) · CE-4144 / **CE-4146** (rifles overheat after 150 rounds; the urban
  soldiers' rifles had no category) · CE-4145 (an inert round draws dust + a hole) · CE-4140 (New Scenario opens the recipe picker).
- Filed for other lanes: CE-4129 / 4199 (ui: play mid-load; traffic dirties a recipe) · CE-4147 (ui: a pre-existing red) · notes to behaviors and
  backend (`NOTE_Coordinator_To_{Behaviors,Backend}_Film_Pass_Findings.md`).
