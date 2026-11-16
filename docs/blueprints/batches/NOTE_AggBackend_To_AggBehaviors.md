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
| **F-9** | a spawn-time fill from the TKB composite | ⭐ **built — CE-8016.** `StochasticUnitFillSystem` (Hrot.Core, in `CgfLogicPack`, the owner, once): author `AggregateUnit { Resolution = Stochastic }` + the unit's TKB type (`TkbIdentity`) and it gets `UnitSlotTable` (one slot per man of the flattened composite — role from the designation, full health, his basic load, `CompositeSlot` = leaf + 1) and `UnitHoldings` (vehicles / systems grouped by type), and its echelon if 0. A table you author (Count > 0) is left alone. ⇒ ST-4 needs no generator script. Contract §3f |

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

## `2026-11-16` (round 3, later) — your F-10 / F-11, and one change that moves a row of yours

| # | answer | where |
|---|---|---|
| **F-10** | ✅ **N-3 built** (CE-8018): a stochastic observer sees PHYSICAL soldiers and vehicles, one way (placed, of a side, not hidden, not a part, alive) — the same curve, a man's signature √(1/9), a vehicle's √0.5 | `UnitSensing.cs` `GatherPhysical` · rail `UnitSensingRails.N3_…` |
| **F-11** | ✅ **fixed** (CE-8021): `StochasticUnitFillSystem` gives every stochastic unit its own `SquadCognitiveState` (N-2's pool) when it has none — you can drop the authored `"SquadCognitiveState": {}` from `agg-detect-by-range`, `agg-sees-physical`, `agg-break-contact` | `StochasticUnitFill.cs` · rail `StochasticUnitFillTests.TheSystem_…GivesItAContactPool…` |
| ⚠ **R-8006** *(for the user's review)* | the stochastic NAKED EYE now stops at the personal soldiers' TKB `VisionRange` — the hard limit the physical soldier sees to (NED rifleman: **500 m**; the urban 2002: 150 m). Found by the C-8 ladder: the 400 m-R50 eye fought at 200 / 300 m where the physical squads never saw each other. ⇒ 🔴 **`agg-detect-by-range`'s 700 m pair (eyes only) is no longer contacted** — the physical rifleman would not see it either. ⭐ Lean: move that pair inside 500 m (e.g. 450 m — the curve still predicts a window), or give the observer optics (a sensor holding is not capped). `agg-sees-physical` (250 m) is unaffected | `UnitSensing.cs` `UnitDetection.BeyondEye` · design §23.4 |
| ⭐ **R-8005** *(for the user's review)* | C-3 calibrated by the ladder: a share of the hits lands on the man already hit; a broken man goes to ground (incoming × 0.5) and still fires at 0.6 — your SOP's "broken" reading (`Stress ≥ BreakLevel`) is unchanged, but a broken unit now dies much more slowly and still shoots | `StochasticCombat.cs` · STATUS §5 |
