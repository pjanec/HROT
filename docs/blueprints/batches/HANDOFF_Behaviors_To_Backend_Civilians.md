<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — the design this hands off (§4 CV-1, CV-4, CV-6, CV-9, CV-10; §5 slices)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md — the ui half; U-1 and U-4 build on B-2 and B-1
  - docs/DESIGN_Injury_States.md — rule I1 that B-1's stored EffectState keeps intact
  - docs/DESIGN_Weapon_Layers.md — the grenade throw and weapon categories B-1/B-5 reuse
  - docs/DESIGN_Orders_Params_Entity_Classes.md — §2 EntityClassTable, where B-2 adds the class rows
-->

# HANDOFF — behaviors → backend: the civilian programme's engine half *(B-1…B-5)*

**Dispatched at `24fa3462e`** (behaviors). ⭐ **Your scope is FROZEN at `24fa3462e`. Documents that change after it are FYI
only**; a later document that invalidates an item ⇒ stop that item and report it.

**What it is.** The civilian programme ([DESIGN_Civilians_And_Crowd_Control](../../DESIGN_Civilians_And_Crowd_Control.md),
🔒 R-4033…R-4036) gives civilians one HSM whose modes are routine, panic, shelter, protest and riot, makes a civilian's
side flip on a hostile act, and gives security forces non-lethal weapons. The behaviours are the behaviors lane's
(`CE-2233`…`CE-2239`). **These five items are the engine pieces they stand on.**

**This is a FRAME.** The CV decisions are leans the user has not ruled yet (design §4). ⭐ **Step 1 of each item is your
detailed design**: UML in the owning design, folded as-built. ⛔ **Build an item only after the user rules the CV it
implements.** Ids: allocate from your block (`CE-3xxx`) and state them in your report. B-1…B-5 are labels, not ids.

## Items

| # | what | design basis | key decisions, with my lean | acceptance |
|---|---|---|---|---|
| **B-1** | **non-lethal effects** (CV-6 ⓐ–ⓓ) | design §4 CV-6, §2.1, §2.4; Injury_States I1 | ⓐ `DamageAssessedEvent`/`EntityHitDamage` grow `EffectKind` {Kinetic, Blunt, Irritant, Stun, Push} + magnitude (`DetonationEvents.cs:26-42`). ⓑ a STORED, replicated `EffectState` {Stunned, Irritated, KnockedDown, Until} on the victim; ⛔ never derived from Health (I1). ⓒ a persistent `AreaEffectCloud` entity (Kind, Radius, on `Lifespan`) with a SimHost system that applies the effect each tick to people inside and cuts LOS transmittance. ⭐ Lean: a sibling of `AreaEffectSystem` (`AreaEffectSystem.cs:33-320` is one-moment), not a mode of it. ⓓ `WarheadKind` += Blunt, Irritant, Stun (`WarheadDto.cs:9-19`) | a baton round hits for Blunt + KnockedDown with Health barely moved; a gas canister's cloud makes people inside Irritated for its lifetime, on every node; sight through the cloud is reduced |
| **B-2** | **the new civilian catalogue** (CV-1) | design §4 CV-1, §1 rows "types", "replacement blast radius", "entity classes" | `CivilianTkbCatalog`: Adult (m/f), Child and Elderly variants with NEW type ids (1001/1003 retired, ⛔ not reused). Each has `BrainTier = Hsm`, `DefaultBehaviorHash = CivilianHsm` (the asset name the behaviors lane will register), Health, Injury, BodyZones (HL-12), stand/crouch/prone animation, `PersonCarrier`, footsteps, hearing 100 m + vision 30 m Low band (CV-2), outfits kept (CE-1099). Plus a **security-force person family** (CV-11: shield and baton carriers). ⭐ **`EntityClassTable` gains `Civilian` and `SecurityForce` classes (R-280)**, so the picker, the 3-D model and the behaviour lookup agree. Migrate the 4 scenarios (`ua-threat-ranking`, `ua-attack-approach`, `tt-heard-shot`, `tt-posture`), ~9 tests that use the literal 1001, and map keys 501–505. `CivilianCar` stays | the 4 scenarios load and run on the new ids; no `1001`/`1003` left outside HISTORY; the class-table rail is green with the new rows |
| **B-3** | **a runtime side-flip write path** (CV-4) | design §1 row "side on the wire" (measured `EntityInfoEgressTranslator.cs:51-53, 121`; ingress applies at `EntityInfoIngressTranslator.cs:177-193`) | ⭐ one engine call, e.g. `SetForce(entity, ForceId)`, run by the `EntityInfo` owner (the creator, R-160). It writes `ForceId` AND `SmartEgressUtil.MarkDirty(dtEntityInfo)`. ⛔ A bare `SetComponent` changes only the creator's copy (the AX-015 failure). The behaviors lane's `HostileAct` handler calls it | two-node rail: CGF flips a civilian to Hostile and SimHost's `EntityInfo.ForceId` reads Hostile within one publish; red-proved by dropping the `MarkDirty` |
| **B-4** | **the perf-gate harness** (CV-10) | design §4 CV-10, §1 rows "HSM cost", "perception cost" | measure on CGF, at 100/300/500 civilians: brain tick per entity (`HsmRunner.cs:189` calls `HsmKernel.Update` per entity inside `BrainTickSystem`, unbatched), perception (Sensors §9.4) and movement (`CarKinematicsSystem` `HumanGait`). ⭐ Lean: a repeatable harness the behaviors lane re-runs on `CE-2235`, not a one-off number | a table of ms per frame per subsystem at each count, reproducible by one command |
| **B-5** | **non-lethal and civilian weapons in the TKB** (CV-9 slice 1) | design §4 CV-9, §2.4 | baton/melee (D5 contact damage + knockdown, through B-1), baton round (ballistic, Blunt), tear gas grenade + launcher (Indirect, Irritant cloud), stun grenade (thrown, Stun area); the civilian's stone and Molotov for `ThrowAt` (Blunt; fire cloud as an `AreaEffectCloud` Kind). ⛔ Water cannon and LRAD wait (CIV-6) | each munition, fired through the existing fire path, produces its B-1 effect; a stone thrown by `ThrowAt` lands on the grenade arc |

**Order:** B-3 and B-2 first, because they unblock the behaviors lane's CIV-1 and CIV-4. B-4 runs with B-2. B-1 comes before B-5.

**Not yours:** CIV-0, the safety gate that keeps soldiers off neutral targets (`CE-2233`, behaviors). Don't rely on it while building, and don't build it.

## Gates — report each row (the reviewing lane will not re-run them)

| # | report |
|---|---|
| 1 | one row per gate: verbatim command · pass/fail/skip · delta vs baseline |
| 2 | a `--no-build` column. ⛔ Build the TEST project, never the production one, before `--no-build` |
| 3 | golden movement as a diff shape, if any |
| 4 | every red confirmed pre-existing against the base sha, named |
| 5 | the tree is clean after the runs |
| 6 | both quarantine counts; a new skip is a finding |
| 7 | `tracker-counts.py --check`, `rulings-check.py`, `design-digest.py --check`; every id allocated |
| 8 | B-1 and B-3 are cross-node: name and run the integration suite that exercises replication of the new component / the flip, or state with evidence why it cannot gate |
