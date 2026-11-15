<!--STATUS
state: ⛔ ON HOLD — the review (design §6) must be ruled first; this file will be re-stamped (rule 1a) — do NOT start
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — ⭐ THE design (inventory §1, decisions RS-1…RS-9 APPROVED R-4258, UML §3, timeline §4, slices + acceptance §5)
  - ../../designs/air-attack/DESIGN_Air_Operations.md — §7d–§7f, what this extends (AirDefenceNodes, the missile warning, the evasion)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §6 F the sensor shape to copy; §8.7 the carriage trap
-->

# HANDOFF — coordinator → `backend`: the radar SAM (CE-4617)

**For:** the `backend` lane (its own id block). **From:** coordinator. **Dispatched at `87fc746f3`**; ⛔ scope FROZEN there — later documents are
FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"Single track with missile and radar together for simplicity, otherwise approved everything to be built by backend."*

## 0. Start
`git fetch origin coordinator backend behaviors`; on `backend`, merge `origin/coordinator` (and `origin/behaviors` — it carries CE-2436's air /
drone work you build beside); restore the test projects you will run; push `chore: started radar SAM at <sha>`. Read the design END TO END, then
Air Ops §7d–§7f and the thermal design §6 F / §8.7 (R-129).

## 1. How you work
| | |
|---|---|
| ⭐⭐ **you build ALL of it** — the sensor, signatures, missile, vehicle type, the behaviour (`RadarSamNodes`), emission control, the warning and the aircraft's reaction stage, the emitter SPOT | ⭐ cross-lane edits into `Hrot.AI.Behaviors` (`AirDefenceNodes`, `AirAttackNodes`, the SOP rows) are allowed — name the lane and the file in each commit |
| ⭐⭐ **decide, never wait** — every default the design names (ranges, signatures, timings, relocation distance) is yours to tune, recorded as an `R-` lane decision with what would change it | ⛔ never "tune until the scenario passes" |
| ⭐⭐ **one air-defence path** — the engagement goes THROUGH `AirDefenceNodes` (a radar track counts as seen), never beside it | ruling 9 |
| ⭐ **the vehicle's model is a placeholder** until the ui lane's U-1 (CE-4618) lands — give the type an asset key the ui lane can fill | |
| ⭐ RS-7's receiver: measure the existing path a side-wide SPOT can take (the reported posts of Assault Under Fire); pick it, never a new one | a failed premise stops that item only |

## 2. The order — the design's §5
B-1 → B-2 → B-3 → B-4 → B-5 → B-6, each "done when" as §5 says. ⚠ B-1: measure the thermal §8.7 trap (an extra sensor child changed behaviour)
on the carrier and report it.

## 3. Gates — every slice
T-1 first (`AirDefenceNodesTests`, `AirDefenceTranslatorsTests`, `AirAttackNodesTests`, the thermal perception rails); every behaviour rail with
`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads
1,4,8` on `sam-site`; 0 B per steady frame; invariant output; `RadarEmitter` unmanaged. Build the affected project in the fix loop; the live runs in
the background, three each.

## 4. The report — `STATUS_Backend_Radar_SAM.md`
① `sam-site` runs (cue, warning, salvo, outcome, relocation, the SPOT) · ② lane decisions with what would change each · ③ per slice built /
stopped · ④ the gate-report contract (rule 8) · ⑤ ids allocated · ⑥ the design folded to the as-built (obligation ⑤).
