<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-24
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — §5d your as-built of CE-4685 (the four findings are its "what did not work")
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md — OWNS the staff's reports and relays (§6, §8; CE-2310 HQ-3) — item ① changes it
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the stochastic unit, its kinds (§23.5) and the physical plays (R-8392)
  - ../../DESIGN_Orders_Params_Entity_Classes.md — OWNS the order DTOs — item ④ adds one
  - STATUS_Backend_LLM_Commander.md — continue it
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER over AGGREGATED UNITS, the four findings (CE-4686)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-24`: *"feel free to give the aggregated unit behavior to the backend (all 4)"* — the four findings of your CE-4685 report.
⇒ ⭐ **items ① and ④ touch other lanes' code (behaviors' command chain, the orders design) and you are AUTHORISED to change it** — say it in each
commit and in the report (cross-lane edit), and run that code's own suites.

## Priority
Next, after the row in progress. Continue `STATUS_Backend_LLM_Commander.md` (a new §, Aggregates 2); decisions to `DECISIONS_Backend.md`.

## Read first
Your own §5d / §Aggregates report, then the owning design of each item (above) — R-129: the intent before the change.

## The four items

| # | the finding | ⭐ the fix (lean — another is yours to choose, said in DECISIONS) | done when (rail, red-proved) |
|---|---|---|---|
| ① | **a company hears no SPOT of enemy infantry** — a commander's staff composes SPOT from its OWN contacts and relays only indirect fire / air defence (`StaffSystems.Relay`, CE-2310 HQ-3); a platoon command entity senses nothing ⇒ the company knows the enemy only from the brief, strength colours and splits | a body-less commander (a platoon command unit, an aggregated unit's command) RELAYS its subordinates' SPOTs of ground forces up one level, merged (one element per reported group, freshest kept, the source kept) — never its own invention; ⚠ design the rule with Command Chain §6/§8 first: what HQ-3 kept out and why (report flooding?), then a bounded relay | in `llm-company-aggregated` the company's picture holds Red's squad as reported by a platoon, with its age and source; the company seat's snapshot shows it; ⭐ the battalion scenarios (`cc-battalion`, `agg-battalion-fight`) still pass |
| ② | **the model broke `CompanyAttack` to "save" a platoon** whose Defend is part of the play's choreography | the snapshot shows each subordinate's ROLE in the running play (base of fire · assault · reserve · support) beside its order — from the play's own state, read not changed; the prefix says a play's sub-orders are the play's, not to be undone | a rail: a company seat running `CompanyAttack` reads each platoon's role; the Haiku run does not end the play to undo a role order |
| ③ | **a platoon's SupportByFire stalled ~180 m off the support position** after the split | root-cause it first (claim table): the seat sends SupportByFire as `Attack` + `HoldBack` (R-8303, built for squads); for a platoon the table has a `SupportByFire` kind row (`PlatoonSupportByFire`) — lean: send the KIND `SupportByFire` where the subordinate's class resolves it | the split platoon reaches the support position and fires from it (a scenario row) |
| ④ | **Bound has no neutral order DTO** — a whole unit's Bound carries `PlatoonBound`'s own fields | a neutral Bound DTO (the orders design's pattern, as for Attack / Defend), read by every play that fulfils Bound | a rail: one Bound order reaches a physical and a stochastic subordinate with the same params |

## The proof
`llm-company-aggregated` with the scripted mind (all rows, plus the new ones for ① and ③) and **Haiku at the LOWEST effort** with the contract —
side by side with your CE-4685 run: did the company see the enemy (①), keep the play running (②), and get fire on the position (③)? Quote 3
decisions, one made on a relayed SPOT.

## Gates (CLAUDE.md rule 8)
Your commander rails + the new ones (each red-proved) + ① the command chain's own suites (`SquadManeuverTests`, the staff / report rails) and the
battalion scenarios + ③④ the plays' suites they touch + `ScenarioChecksTests` + the two runs; the parallel env on any system you change. Fold
the as-built into the owning designs — ① into Command Chain §6/§8, ④ into the orders design, ②③ into LLM Commander §5d (obligation ⑤). Report the
ids you allocated.
