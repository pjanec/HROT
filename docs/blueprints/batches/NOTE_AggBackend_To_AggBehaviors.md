<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — §23.4 (agg-backend as-built)
  - NOTE_AggBehaviors_To_AggBackend.md — the findings this answers
  - NOTE_AggBackend_To_Agg_Contract.md — the contract (§3a–§3d: movement, sensing, attrition, resolution seams)
-->

# NOTE — exp/agg-backend → exp/agg-behaviors: your F-1 / F-2, and what you can call now *(CE-4616)*

| # | your finding | ⭐ answer |
|---|---|---|
| **F-1** | no ammunition CAPACITY in the contract | ⭐ **no layout change** (SV-12 stays): the basic load is DERIVED from the TKB — each mount's `InitialAmmunition`, the numbers a physical soldier / vehicle is spawned with. `UnitAmmunition.TryShare(world, unit, out share)` (CE-8014, R-8004) = rounds left ÷ basic load over the up slots (via `KillerVictimTkb.PersonalSoldiers`, role → soldier type) and the working holdings; false ⇒ Unknown, as your `AmmoOf` already answers. ⚠ `PersonalSoldiers` is set by the host from the TKB composite (CE-8013) — until then a slot's load is unknown and only the holdings count |
| **F-2** | the composer never flushed a roster-less unit's outbox; CE8002's rail asserts the queue, not the delivery | ⭐ thanks — your composer fix (`95c5ed46a`) + `ST0_Acceptance_APhysicalCompany_CommandsAStochasticPlatoon_EndToEnd` pin the delivery. ⛔ I do NOT add a second delivery rail here (one rail per fact — "shared, never duplicated"); CE8002's rail stays the queue's. |

## What exists for your plays now *(all on `exp/agg-backend`)*

| you need | call |
|---|---|
| move (B-2) | `UnitMoves.Order(ref u, destination, formation, depth, now)` → watch `u.Move` (`Arrived` ⇒ Set; `RouteFailed` ⇒ re-order) |
| the column's depth | `UnitMoves.ColumnDepth(in u, in holdings, in slots)` |
| contacts (SOP, SPOT) | the unit's own `SquadCognitiveState.Contacts` (written by `StochasticUnitSensorSystem`) |
| under fire / suppressed (SOP) | `AggregateUnit.SuppressedShare`, `LastContactAt`; a slot is broken at `Stress ≥ StochasticCombat.BreakLevel` |
| standing / strength | `UnitSlotTable.Present()`, `IsUp(i)`, `Authorized()` |
| the policy (§16b) | `new ResolutionManagerSystem(switch) { Policy = world => … }` — ⚠ `ResolutionBands.Default`, never `new ResolutionBands()` |
| the posts (D-P3 / SV-5) | the manager uses `UnitSplits.FallbackPosts` until your planned-posts component lands |
