<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — ⭐ THE design (READY-TO-BUILD, R-4283 + R-4284): §3 decisions, §4 UML, §5 numbers, §6 scenarios, §7 ids, §8 slices
  - ../../designs/air-attack/DESIGN_Air_Operations.md — the route (§5), the plane model (§6), the attack behaviour and its evasion (§7e) you extend
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — the radar sensor, emission and warning (RS-2, RS-6, RS-7, RS-10) you reuse and exempt aircraft from
  - ../../designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — the guided round (§3c) you give a motor
  - HANDOFF_Coordinator_To_Ui_Air_Combat_Visuals.md — ui's half (the looks); it keys on the ids you declare
-->

# HANDOFF — coordinator → `air` (a NEW lane): AIR COMBAT — jets, missiles, cannons, missile defence, GCAS, the show (CE-4647)

**For:** a NEW lane on branch **`air`** (create it from `origin/coordinator`). **From:** coordinator. **Dispatched at `369177d6d`**; ⛔ scope FROZEN
there — later documents are FYI; one that invalidates a step ⇒ STOP that step, do the rest (R-106).

🔒 User, `2026-11-16`, verbatim: *"Design the air dogfight now, for jets, with missiles and cannons, as well as evasive maneuvers for air to air missiles
including sam missiles … without following the terrain but avoiding crashing to terrain - the ai autopilot need to know the aircraft limits and pull up
soo enough. some 'too immersed in combat' or other auxiliary induced failures might be added if the combat turns to be too equal and boring (it is a show in
the first place so the attacks need to happen above the terrain not many kilometers away...)"* · *"approved, fold it in and write the handoffs"* — R-4283, R-4284.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait, never end a turn on a question. Every open choice: measure it, decide by the claim table (code + design), build it, record an
`R-10xxx` lane decision *"for the user's review"* with what would change it. A failed premise stops ONE step, never the batch (R-106). A defect is fixed
where it lives, with a rail in that feature's own suite (T-1). Commit, push and update **`docs/blueprints/batches/STATUS_Air_Combat.md`** after every step
(a restart or a compaction resumes from it). Cross-lane edits (motion, combat, catalogue, network, the scenario harness, behaviours) are allowed and SAID in
the commit and the status. ⛔ Never take an id outside your blocks. ⛔ The looks are ui's (AC-15) — you declare ids, the class and placeholder look rows only.

## 0. Start
`git fetch origin coordinator`; create `air` from `origin/coordinator`; `dotnet restore` the test projects you run; push `chore: started air at <sha>`.
Read RULINGS.md (R-4283, R-4284, R-2015, R-3007, R-4258/9), CLAUDE.md, then the design END TO END — ⭐ §3 is the contract, §9 the review that shaped it.
First command: `scripts/cbm.sh list_projects` in the background; every claim about what exists goes through the graph AND grep.

## 1. Your id blocks
tracker / rulings **CE-10000…CE-10999 · R-10000…R-10999** (rows at the END of your own tracker area) · aircraft type **409** · munitions **6060…6065**
· `NavigationMode` / wire **8**. ⭐ Component ids ×5 and descriptor ordinals ×4: **you measure and take them in step S0** (§7 of the design).

## 2. The slices — in order, each DONE when its scenarios pass (`scripts/scenario-regression.sh <name>`, R-4277)

| slice | build (design §) | done when |
|---|---|---|
| **S0 measure** | ① **ids**: a FULL scan of every `[ComponentId(...)]` and every id constant (all forms: `= N;`, `= N,`, enums, tests) on every lane branch (`coordinator behaviors backend ui terrain convoy exp/agg-*`) ⇒ take 5 free component ids as YOUR block, declared in `GlobalComponentIds.cs`' header like R-4276 (⛔ 163 reserved, 262–279 `NavFakeIds`, 476–495 full, tests hold 487/488/492/493); the same for 4 `DescriptorOrdinal`s (⛔ 201–206 time, 220–239 agg; read `NOTE_Coordinator_To_Convoy_Ordinals.md`); the MiG's DIS subcategory; the air-to-air weapon-category value. ② `Ground(x, y)` beyond the map edge. ③ the vision range at which a unit sees a jet. ④ whether `RadarPerception` (RS-2) runs on an AIRCRAFT emitter. ⇒ each answer with `file:line` into the design's §1 INVENTORY, in the same commit | the ids are declared, the four answers are in the design |
| **S1 the autopilot** | AC-1 (a leg's altitude, early climb) · AC-3 (`Steer3D`: own attitude, no Euler readback, no ±25° clamp in mode 8; bank = acos(1/n); `AccelMps2` is the thrust) · AC-4 (GCAS in EVERY AI fixed-wing mode by simulating the recovery through `Steer3D`; floor rule; turn away from a ridge) · the F-16's flight numbers (§5a) · `FighterPilotState` (muscle group) | `ac-cruise-altitude`, `ac-gcas-ridge`, `ac-gcas-inverted`, `ac-gcas-steep-ridge`, `ac-map-edge` · the GCAS rails |
| **S2 missiles that can be beaten** | AC-6 (boost / coast, g ∝ speed², 0.4 s lag, `Defeated`, current speed apart from `SpeedMps`, `MaxG` 0 ⇒ CE-2222's turn rate; the radar notch) on the AAMs AND the SAMs (§5b) · AC-9 / AC-9a (`MissileDefenceState` component, `MissileDefence.Run` with its two outputs, the live threat scan, the `LastDitch` reflex; `AirAttackNodes`' evade AND radar-escape stages folded in; only a launch starts it) · the grammar items (`Defeated`, the shot's range, `far`) | the 6 S2 scenarios · ⭐ the Monte-Carlo rail (100 seeded shots per profile, the hit-rate band you state, a late reaction measurably worse) |
| **S3 the fighters** | AC-2 (mode 8 in the engine AND wire enums and both `MapMode`s + a round-trip rail; `AirCombatSteer` brain group) · AC-5 · AC-7 (catalogue §5a for both types; the MiG's `TwinEngineFighter` class; placeholder look rows for 6060–6065) · AC-8 (the employment zone, the accelerating gun lead ≤ 500 m, the friendly check, the new weapon category) · AC-13 / AC-13a (an Attack on an aircraft ⇒ the air-combat profile of `AirAttack`; CAP = Hold + the fighter SOP row; deconfliction; after a kill; winchester / disengage; its own radar On; aircraft emitters exempt from RS-6 / RS-7; jets that touch both crash) | the 5 S3 scenarios |
| **S4 the show** | AC-10 (`AirArena`, soft edge, merge) · AC-11 · AC-12 (skill sets reaction / g / quality; lapses past the stalemate, per-entity `SimRng`) · `AirArenaSystem` | the 3 S4 scenarios |

## 3. The gates *(the report carries them per CLAUDE.md rule 8)*
- ⭐ T-1 first: the features' own suites — `FixedWingModelTests`, `RotorcraftModelTests`, `GuidedRoundSystemTests`, `AirAttackNodesTests`, `AirDefenceNodesTests`,
  the radar SAM suites — and the built scenarios that move: `ua-air-attack`, `ua-air-defence`, `sam-site`, `ua-attack-evades` if it exists. A golden that moves
  is RE-RECORDED with a diff you read and describe (§4 of Scenario Regression); `sam-site`'s chaff band re-measured and stated.
- Every behaviour slice runs with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` and the world guard; `stepped-hash.py --threads 1,4,8` on `ac-dogfight-2v2`
  (parallel safety — never the verdict). The brain's reads of `FighterPilotState` / `AirArena` declared in `BrainReads.Declare`.
- ⛔ No allocation and no LINQ in `Fight`, `Steer3D`, `Gcas`, `ScanInbound`, the zone maths: a rail — 0 bytes in a steady frame.
- Wire: a `--mode all` cluster run of `ac-dogfight-1v1` (the jets must FIGHT, not fly straight — RV-6's failure is invisible in the editor).
- `scripts/protocol-doc-check.py` OK — the new descriptors and the mode go into `docs/reference/NED_DDS_Protocol.md`.

## 4. When you finish
The design's §3–§8 folded to the AS-BUILT (deviations argued, prior state SUPERSEDED); `docs/CAPABILITIES.md` marks air combat by its scenarios (✅ / ⚠ / ⏳);
a `docs/USAGES.md` row if a usage became real; `STATUS_Air_Combat.md` lists every scenario with its verdict, every id taken, every `R-10xxx` decision.
