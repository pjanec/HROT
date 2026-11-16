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

### ST-3 / ST-4 / mixed scenarios (`2026-11-16`) — observations for you

| # | observation | evidence |
|---|---|---|
| **F-15** | `agg-battalion-fight` (Blue HQ `CompanyAttack` / Red HQ `CompanyDefend`, three 90-man companies each, 600 m apart) ends one-sided: the assault company is wiped out within 7 s of contact (all 90 slots down 328-335 s) while Red's front company loses 5; its stress is at the break level on 60 slots at the first contact. The duel (`agg-platoon-duel`: a platoon against a 10-slot squad) shows the same first-second stress. A balance question for the C-8 band (CE-8009), not a scenario defect | `scenarios/demos/aggregates/agg-battalion-fight`, `agg-platoon-duel`; `scripts/scenario-regression.sh <name>` |
| **F-16** | the personal-weapon map `KillerVictimTkb.PersonalSoldiers` is filled ONLY by `StochasticUnitFillSystem` from a unit it fills (first seen wins); a world whose stochastic units all carry authored `UnitSlotTable`s has an empty map and nobody fires. `agg-out-of-ammo` carries a spare filled squad for it | `StochasticUnitFill.cs:202` |
| **CE-8011** | stepped-hash on `agg-battalion-fight` (`--threads 1,4,8`): STATE EQUAL at 1, 4 and 8 threads (0 type(s) differ, 0 SimTransform rows) — ⚠ the gate steps only 6 chunks of 20 (~2 s of sim): before any contact, so it proves the setup + the first fills and orders, not the fight; a longer window would need the gate's --chunk raised | `python3 scripts/stepped-hash.py --threads 1,4,8 --scenario agg-battalion-fight` |


## `2026-11-16` (ST-5) — the policy landed; two asks

| # | finding | ask |
|---|---|---|
| **F-17** | `ResolutionPolicy` (id 119, World Environment, `SetResolutionPolicy`) is wired: `CgfLogicPack` now passes `Policy = ResolutionPolicies.BandsOf` to your manager (one-line edit at your CE-8013 site). **Detail areas** (SV-15, up to 8 `EntityRef`s: `policy.AreaAt(i)`) are stored but `ResolutionBands` has no field for them | the manager: a unit inside a detail area (a marked area entity) is split / never auto-merged regardless of the bands |
| **F-18** | D-P3 (planned posts) NOT built on our side — `SquadDefend`'s plan is bound to a commander's EQS sensors and live men (design §23.5c R-8330). `UnitSplits.FallbackPosts` stays the only source | nothing now; a design for the asks over a unit entity + a Planning-stage wait would be the next step |

## `2026-11-16` (after merging agg-backend at `a6ba7cf86`) — four scenarios went red; root causes measured, three findings

⭐ Measured, not guessed: a fresh restore + build ruled out a stale binary; each red was then bisected in a scratch worktree (agg-backend commits reverted one at a time on top of `a6ba7cf86`, never committed). Three of the four were **content tied to the old engine numbers** and were re-authored on our side (R-8331 … R-8334, design §23.6c); **none needs an agg-backend change to pass**. The findings below are what the bisect showed about the engine.

| scenario | moved by | mechanism (measured) |
|---|---|---|
| `agg-detect-by-range` | `5d8a30150` (CE-8009 R-8006) | the 700 m eye contact is past the naked eye's reach (`UnitDetection.BeyondEye`, 150 m); reverting the commit restores it at 7.25 s |
| `agg-battalion-fight` | `5d8a30150` (CE-8009 R-8005) | broken men still fire (0.6) and hits focus: the assault costs Red companies 1 and 2 ≥ 10 men each (Amber); reverting the commit restores the all-Green result |
| `agg-out-of-ammo` | `6d9634b56` (CE-8021) | the order-less Red target now HAS a contact pool, so our StochasticUnitSop sends it back (BreakContact to (1000, 800)); it withdraws at 1.24 m/s, the attacking squad follows at 0.95 m/s, and from 5.25 s phase A writes no line (`FireAllocation.Count` 0, the pair still reported — the range opening past the rifle's `KvEntry` reach is the remaining term of `Split`, not read directly) with 4 rounds a man left. Reverting CE-8021 alone (with the other two reverted) restores the spend. ✅ correct behaviour — the scenario's target now defends |
| `agg-sees-physical` | `673a44469` built N-3 — ✅ it works | two blockers on THIS scenario, F-19 and F-20 below; with optics at 700 m on flat berlin the observer reports the physical squad at 9.25 s and the platoon hears SPOT at 12.25 s |

| # | finding | evidence | ask |
|---|---|---|---|
| **F-19** | ⛔ a stochastic unit's `SimTransform` keeps its AUTHORED height (z 0) — nothing sets it on the ground — while physical bodies stand on the terrain. N-3's line starts at the unit's z + 2 m (`UnitSensing.cs` `SenseOne`, `from = mine[a] + (0, 0, EyeHeight)`), so on any ground above z ≈ 1 m the line runs into the earth: on `basic-desert` a PINNED stochastic squad never sees a physical squad 120 m away (`UnitDetections.Count` 0 at 5 s) | `GET /perception/los?observer=<unit>&target=<man>` on basic-desert: unit eye (300, 140, 1.7), man at z 0.19 — *"blocked by terrain: transmittance 0"*, crossing `ground / earth` at 31.6 m on every body point. The same scenario on `berlin` (flat): reported at 1.25 s, SPOT at 4.25 s | put a stochastic unit on the ground (the fill / the follower sets z from the terrain height), or sample N-1 / N-3 from the ground height under the footprint |
| **F-20** | ⚠ the naked eye's reach (R-8006: 150 m for a rifle squad's men) is far inside the manager's split band (SV-9: 400 m + 200 m lead; forced 300 m, C-7) ⇒ an UNPINNED eye-only stochastic observer is split before any physical body enters its sight: **N-3 by eye is unreachable in production** (only optics / thermal beyond 600 m, or a `PinStochastic`) | `agg-sees-physical` at 250 m: Planning at 0.25 s, Physical at 1.25 s, no detection ever (probe rows); at 700 m with optics: no split, reported at 9.25 s | a design statement: is that intended (then §15 / §16b should say so), or should a stochastic unit sense the physical enemy that TRIGGERS its split (the SPOT up the chain before the handover)? |
| **F-21** | ⚠ the naked eye's reach is ONE number per process: `StochasticUnitSensorSystem.ResolveEyeReach` takes the max `VisionRange` over `KillerVictimTkb.PersonalSoldiers` — a static map filled by the FIRST unit the spawn fill fills (`StochasticUnitFill.cs:206`, first seen wins). A composite holding an anti-tank man brings his launcher sight's range (`UrbanCombatTkbCatalog.cs:417`, `Max(SoldierVisionRange, sightRange)`) into EVERY observer's eye, whatever its own men | inferred from runs, not proved per composite: `agg-platoon-duel` (a platoon filled first) still fights at 300 m after R-8006, while `agg-detect-by-range` / `agg-out-of-ammo` (a rifle squad filled first) cap at 150 m. ⚠ the naked eye should be the observer's own men's (per unit, its own composite) and never a weapon sight | per-unit reach from the observer's own composite (the fill knows it), eye only |

⭐ Also observed (not yours alone, no action asked): in a live editor, a second scenario load at an EARLIER sim time than the previous run's left the stochastic sensing and fire phases idle (`_lastStep` / `_last` are per-system-instance and compare against the old clock — no `UnitDetections` / `FireAllocation` added by t = 5 s). The regression suite runs each scenario in a fresh process and is unaffected; the reload leak is the known CE-3294 class.

## `2026-11-16` (ST-5 scenarios) — the resolution manager in four feature-test scenarios; two findings

⭐ The process works end to end in the editor tier: `agg-split-on-contact`, `agg-squad-holds-house`, `agg-merge-back`, `agg-forced-handover` (design §23.6d). C-7 skips Planning (never seen at a step); a split's 30 children carry exactly `uuid5(<unit>, child:<composite slot>)`; a merge keeps every loss (3 dead stay dead, 2 hurt come back at 12.5); the instructor's `PinPhysical` re-splits with the SAME keys.

| # | finding | evidence | ask |
|---|---|---|---|
| **F-22** | ⚠ a split's children carry NO name: `BusResolutionSwitch.Materialise` writes `EntityInfo { ForceId = force }` only (`UnitResolution.cs:348`) — so the scenario checks (named units), the golden (it hashes NAMED units only) and every UI that lists units by name cannot tell them apart; the regression suite now reaches them by network id (R-8335, a grammar extension on our side) | `GET /entities` after a split: thirty rows `{"networkId": 1007…1036, "name": ""}` with `AggregateChild` | a name per child — e.g. `"<unit name>/<slot>"` (X-3's key, readable), or the composite leaf's own label — so a recording, the review and a check can name a man |
| **F-23** | ⛔ the merge distance is INSIDE the split trigger with the default bands: trigger `d < PhysicalBand + LeadMetres` = 600 m (`UnitResolution.cs:227`), merge-eligible `d > PhysicalBand + MergeMargin` = 500 m (`:275`). An enemy standing anywhere between 500 and 600 m makes the unit split, merge after the quiet time, split again — forever | `zz`-probe (not committed): a physical squad standing 550 m off a stochastic platoon on berlin, default policy — Physical at 2.25 s, Stochastic 62.25 s, Physical 65.25 s, Stochastic 125.25 s, …, Stochastic 188.25 s (every ~63 s). Also seen in the first `agg-merge-back` draft (merged at 119 s, re-split at 122 s while the patrol walked through 500–600 m) | the merge test against band + lead + margin (the split's own trigger + the hysteresis), as the Planning cancel already does (`withdrawn`, `:229`) — ⭐ our scenarios set `mergeMargin: 250` by policy meanwhile (R-8336) |

⭐ Also measured (ours, not yours): a `ScenarioTime` trigger with `fromSeconds: 0` is armed at 0.27 s and never fires — the policy trigger of `agg-merge-back` uses 1 s.
