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

## `2026-11-16` (later still) — found authoring the nine feature-test scenarios (ST-0 … ST-2, R-4277)

⭐ F-9 landed (CE-8016) and works: every scenario authors a unit with only `AggregateUnit { Resolution: Stochastic }` and the fill gives it 30 / 10 slots (platoon / squad) and its holdings. Two of the nine are NOT green for an engine reason:

| # | finding | evidence | scenario / failing row | what we ask |
|---|---|---|---|---|
| **F-10** | ⏳ **N-3 is not built**: `StochasticUnitSensorSystem` takes only STOCHASTIC units as targets (`_all` is filled from `Effective == Stochastic`), so a stochastic observer never detects a PHYSICAL squad — no contact, no SPOT up the chain. The design records it as deferred (§23.4 "⏳ N-3 … stochastic targets only") | `UnitSensing.cs` `Execute` pre-pass; `agg-sees-physical`'s dump: the observer's `UnitDetections.Count` stays 0 with a physical squad 250 m away in the open | `agg-sees-physical` — 🔴 RED, rows *"the stochastic observer holds a reported contact on the physical squad"* and *"the platoon commander hears a SPOT"* (the third row, *the physical squad never contacts the stochastic unit*, holds) | build N-3 (a physical entity — a soldier or a body-less physical squad commander's members — as a sensing target, one way); the scenario then turns green without a change |
| **F-11** | nothing PROVISIONS `SquadCognitiveState` on a memberless stochastic unit: `UnitSensing.Report` returns when the observer has none, so N-2's contact pool is never written in a scenario run (`SquadStateProvisioning.EnsureForCommander` adds it only to a unit that has subordinates). Your ST-2 rails add it by hand | `UnitSensing.cs` `Report` (`HasComponent<SquadCognitiveState>` guard); a stochastic squad's dump has `UnitDetections` and no `SquadCognitiveState` | `agg-detect-by-range`, `agg-sees-physical`, `agg-break-contact` author `"SquadCognitiveState": {}` on their observers as the workaround | provision it in `StochasticUnitFillSystem` (the fill already touches the unit once) — then the three drop the authored component |

⭐ **Not engine defects, for the record:** a scenario's `Order` verb checks the order against the PHYSICAL class table before it publishes — a rifle squad has no Strike, so `agg-refused-order` orders Strike to a stochastic ARTILLERY BATTERY (type 304: a physical battery runs `ArtilleryUnitStrike`; the stochastic table refuses it); `agg-march-mounted` uses a mounted PLATOON with authored wheeled holdings because no TKB composite carries a company's vehicles.

| # | observation (no action required unless you see it) | evidence |
|---|---|---|
| **F-13** | a captured golden of `agg-march-mounted` differed ONCE in five fresh-process runs: the unit's position was one follower step off from t = 15 s — the Move play / follower started a frame later (the other six goldened scenarios repeat). Not reproduced in three more runs | `/tmp`-only run logs; hashes of `AggregateUnit`, `SimTransform`, `FireAllocation`, `SopState` differed at the first differing snapshot. The scenario therefore carries no golden |

