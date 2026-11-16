<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — ⭐ THE design: §4d is your scope, §5a the jets, §5b the missiles, §7 the ids
  - ../../DESIGN_Map_3D_Mode.md — the families and shape kits (§6n): the jet, the A-10's pylons with rounds left
  - ../../DESIGN_Visual_Effects.md — the effects you extend
  - ../../DESIGN_Entity_Control.md — the pilot station and HUD (§8f) the fighter HUD extends
  - HANDOFF_Coordinator_To_Air_Combat.md — the air lane's half; it declares the ids you key on
-->

# HANDOFF — coordinator → `ui`: AIR COMBAT LOOKS — the MiG, jets with missiles, afterburner, the arena, the fighter HUD (CE-4647, AC-15)

**For:** ui. **From:** coordinator. **Dispatched at `369177d6d`**; ⛔ scope FROZEN there — later documents are FYI (R-106).
🔒 User, `2026-11-16`: *"Or maybe we rather let the visuals to the UI branch as usual."* · *"approved, fold it in and write the handoffs"* — R-4284.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait. Decide by measurement, record `R-1xxx` lane decisions for the user's review. Commit, push and keep a status file after every step.
Your ids from your own blocks (tracker / rulings `CE-1xxx` / `R-1xxx`). ⛔ Never redo what exists: the F-16 body (`ShapeKits.Jet`), the muzzle flash
(`EffectSystems.cs:177`), hidden retractable gear (`EntityBodyLayer3D.cs:1840`), flares.

## 1. Dependency on the `air` lane
You key on what the air lane declares: the class **`TwinEngineFighter`** (DIS `1.2.*.1.<sub>`, the sub from its S0), type **409**, munitions **6060–6065**,
and the replicated **`FighterPilotState`** (`Throttle`, `GLoad`). ⭐ Start with the mockups and the shapes at once (the class NAME is fixed); `git fetch
origin air` and merge it when its S0 / S3 land for the sub value and the component; until then a rail may stamp the class directly.

## 2. The steps
| # | step | done when |
|---|---|---|
| **1** | ⛔ **MOCKUPS FIRST** (R-4143): the MiG twin-fin body beside the F-16; a jet with its missiles on rails (full, after two shots); the fighter HUD; the arena in 2-D and 3-D — house style over `scripts/mockup_kit.py`, committed beside the design, each captioned with its scope (R-4151) | the SVGs are in `docs/designs/air-attack/` and §4d links them |
| **2** | the **MiG body** — two canted fins, two nozzles, intakes under the wings — `VisualFamily` keyed by `TwinEngineFighter` (more specific than `Fighter`, the `AttackAircraft` precedent `VisualFamily.cs:108`) | a frame rail renders it next to its mockup (R-4146) |
| **3** | **stores**: wingtip + underwing rails with the missiles still carried (IR outboard, radar inboard), one gone per shot — the A-10 rounds-left rule (`EntityBodyLayer3D.cs:483, 1534`) | a rail: 4 → 2 after two shots |
| **4** | the four **AAM look rows** (tune the air lane's placeholders); the **trail only while the motor burns** — from the round's age against `MissileDto.BurnSeconds` (no wire); a **launch-off-the-rail** flash and smoke | a rail: smoke during the burn, none while coasting |
| **5** | **afterburner** at full throttle and **wingtip vortex trails** above 6 g, from `FighterPilotState`; **smoke and fire on a dead jet** as it falls | rails per effect, under `xvfb-run -a` (display-only rails skip silently) |
| **6** | the **arena**: an authoring gizmo (centre, radius, floor, ceiling) and its 2-D / 3-D drawing (a faint ring, the floor) | the gizmo edits `AirArena`; drawn on both maps |
| **7** | the **fighter HUD** for a possessed F-16 / MiG (the manual mouse-aim pilot — mode 8 is AI only): gun pipper at the led point, missile in-zone / lock cue, radar warning, g | its frame rail against the mockup |

## 3. Gates
The touched test classes + the frame rails (under `xvfb-run -a`), each new rail red-proved once; proof proportionate to risk (visual work — one key
mutation per rail). Fidelity checked by YOU against the mockups (R-4146): the side-by-side in your status, never a pause.
