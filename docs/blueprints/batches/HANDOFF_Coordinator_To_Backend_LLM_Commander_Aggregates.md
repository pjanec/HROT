<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-24
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §5c the decision contract (your as-built §5c.1), §5b run_play; this slice adds §5d
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — OWNS the stochastic unit: orders by KIND (§23.5, R-8305), the split into squads on contact (X-5, "one level at a time"), the SITREP from slots (ST-0)
  - STATUS_Backend_LLM_Commander.md — continue it
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER over AGGREGATED UNITS (CE-4685)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-24`: *"maybe we could somehow combine it … with the aggregated units. Does that make sense? Or maybe it is already functional
in that way, because they also have commander"* → the coordinator's answer (most of the plumbing is already aggregate-aware, unproven together;
lean: prove one small slice, then sell it) → *"Yes, please, write a handoff note to the backend."*

## ⭐ Why
Aggregation lives at the higher echelons — and that is where an LLM commander adds most: slow, big-picture decisions (main effort, the reserve),
while aggregated units fight as numbers and **deaggregate into soldiers with the game AI's drills on contact**. A thinking enemy at company or
battalion scale for little CPU. The README will claim it only once this slice's scenario passes.

## Priority
Next, after the row in progress. Continue `STATUS_Backend_LLM_Commander.md` (a new §, Aggregates); decisions to `DECISIONS_Backend.md`. On the
cloud VM. ⚠ The stochastic unit and the plays are other lanes' code (the agg lanes, behaviors): CALL them, ⛔ do not change them; a needed change
is a NOTE.

## Measured by the coordinator `2026-11-24` — the premises (verify before building on them)

| the premise | code — how it IS | design |
|---|---|---|
| the seat's orders reach a stochastic subordinate | ✅ the seat orders by KIND (`CommandOrders.Order` Move / Hold / Attack / SupportByFire; `Route` Defend / BreakContact — `CommanderActionSystem.cs:317-346`); the stochastic rows resolve those kinds (`ResolutionRouting.cs:84-88`); `Route` orders a stochastic member by kind (`CommandNodes.cs`, R-8305) | ✅ Stochastic Units §23.5 |
| the plays order stochastic subordinates | ✅ mostly through `Route` (`PlatoonDefend` 3×, `CompanyBehaviors` 3×, `SupportByFire` 1×, `PlatoonAttack` 1×); ⚠ `PlatoonAttack.cs:977` `Assign(…SquadRecover…)` does not — **measure what it does to a stochastic subordinate** | ✅ same |
| reports from a stochastic unit | ✅ the staff composes its SITREP from slots and holdings (`StaffSystems.cs:128-168`, ST-0) | ✅ §23 ST-0 |
| "in the fight" for validation | ✅ `CombatEffective.IsInTheFight` counts a stochastic unit's live slots (`CombatEffective.cs:53`) | ✅ §23.7 |
| `run_play` at company level | ⛔ the four PLATOON plays only | — this slice |
| proven together | ⛔ no scenario | — this slice |

## What it builds

| # | builds | done when (rail, red-proved) |
|---|---|---|
| A-1 | **the seat at any echelon**: the prefix, the snapshot and the tools speak of the commander's own echelon and his subordinates (platoons, companies), not "squads" — from the commander's class, never hard-coded; a stochastic subordinate's row shows its strength from its slots | a rail: a company seat's briefing names its platoons and their strength |
| A-2 | **`run_play` offers what the commander's class fulfils** — a company gets `CompanyAttack` / `CompanyDefend` (their params from the gazetteer: objectives in order, base of fire, reserve); a platoon keeps its four | a company seat runs `CompanyAttack` and its platoons are ordered by kind |
| A-3 | **the split is news, not a surprise**: when a stochastic subordinate hands over and becomes a physical command unit (X-5), the feed says so in one radio line and the seat keeps commanding it (the same entity, or say what changes) | a rail on the split: the seat's next order still lands |
| A-4 | **the scenario `llm-company-aggregated`** on the pattern of `agg-platoon-under-company` / `agg-battalion-fight`: a Blue company commander with a seat over 2–3 STOCHASTIC platoons attacks; Red (physical or stochastic, game AI) defends; contact makes Blue's platoons split into squads | checks: every order reaches a stochastic platoon by kind; at contact a platoon splits and its squads fight; the seat keeps commanding after the split; reports flow from aggregated and split units; the fight ends |

⭐ Keep the decision contract (§5c) and its token budget unchanged; report the request size first / median / last.

## The proof
1. The scripted mind on `llm-company-aggregated` passes its checks.
2. **Haiku at the LOWEST effort** (`model: "haiku"`, `effort: "low"`, the request JSON only) plays it with the contract — report as before: calls,
   decisions applied / refused / repaired / CONTINUE, contingencies fired, tokens, the checks; quote 3 whole decisions, ⭐ at least one made after a
   platoon split.
3. Say plainly what did NOT work with aggregated units, and which lane owns it.

## ⛔ Not in this slice
A battalion seat over companies (the next step if this passes), the laptop (L-3b), continuous mode (L-4), CE-3493.

## Gates (CLAUDE.md rule 8)
Your commander rails (L-1 … L-3c, the duel) + the new ones (each red-proved) + the aggregate scenarios the seat's orders touch (`agg-platoon-under-company`,
`agg-platoon-splits-into-squads` — run, not changed) + `ScenarioChecksTests` + the two runs above. Fold the as-built into the design as §5d
(obligation ⑤). Report the ids you allocated.
