<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — §23.4 (agg-backend as-built), §23.5 / §23.6 (agg-behaviors)
  - NOTE_AggBackend_To_Agg_Contract.md — the contract these findings are about
-->

# NOTE — exp/agg-behaviors → exp/agg-backend: two findings from ST-0 *(CE-4616)*

| # | finding | evidence | what we did | what we ask |
|---|---|---|---|---|
| **F-1** | ⛔ the contract holds no AMMUNITION CAPACITY: `UnitSlot.Ammo` is a count, `UnitHoldings.Ammo` a pool — so the SITREP's ammo colour (a share of the basic load, `StaffComposerSystem.AmmoOf`) cannot be computed for a stochastic unit | `AggregateComponents.cs` (`UnitSlot`, `UnitHolding`) | `AmmoOf` answers **Unknown** for a stochastic unit (nothing invented); a parent's walk adds nothing for it | a capacity — a per-slot `AmmoMax`, or a per-role basic load the TKB composite gives at spawn. ⚠ the layout is fixed (SV-12): decide before the first recording |
| **F-2** | the staff composer walked only ROSTERED units, so a stochastic unit's outbox was never flushed: no SITREP, no PROGRESS Set — and your **refusal's TaskFailed stayed queued, never delivered** (`CE8002`'s rail asserts the queue, not the delivery) | `StaffSystems.cs` (`Sends`, the unit walk) | fixed on our side: the composer also walks roster-less `AggregateUnit` units (`95c5ed46a`); the end-to-end delivery is pinned by `ST0_Acceptance_APhysicalCompany_CommandsAStochasticPlatoon_EndToEnd` (a refused Strike reaches the company as TaskFailed) | consider asserting the DELIVERY in CE8002's own rail once you merge us |

## `2026-11-16` (later) — ST-4 needs a stochastic unit AUTHORED in a scenario

| # | ask | why |
|---|---|---|
| **F-9** | a SPAWN-TIME fill: a unit authored with only `AggregateUnit { Resolution = Stochastic, Echelon }` gets its `UnitSlotTable` (roles from its TKB composite's slots, health full, ammo the basic load — `UnitAmmunition.BasicLoad`) and `UnitHoldings` (the composite's systems) — the "TKB composite resolver" your CE-8013 lists | ST-4's battalion-vs-battalion scenario: hand-authoring 18 units × 48 slots in JSON is unreadable and duplicates the composite. ⏳ Until it lands, ST-4 authors the components in the scenario JSON from a generator script IF the scenario loader round-trips the fixed slot array (measured in ST-4), and switches to your fill when it merges |
