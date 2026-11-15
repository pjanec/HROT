<!--STATUS
state: LIVE — RE-DISPATCHED (rule 1a: the first stamp 87fc746f3 was never started), not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — ⭐ THE design (inventory §1, decisions RS-1…RS-10 as AMENDED by the §6 review, R-4258 + R-4259, UML §3, timeline §4, slices + acceptance §5)
  - ../../designs/air-attack/DESIGN_Air_Operations.md — §7d–§7f, what this extends (AirDefenceNodes, the missile warning, the evasion)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §6 F the sensor shape to copy; §8.7 the carriage trap
-->

# HANDOFF — coordinator → `backend`: the radar SAM (CE-4617)

**For:** the `backend` lane (its own id block). **From:** coordinator. **Dispatched at `d287b3380`** *(re-stamped; supersedes `87fc746f3` — the §6 review and the user's two changes, R-4259)*; ⛔ scope FROZEN there — later documents are
FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"Single track with missile and radar together for simplicity, otherwise approved everything to be built by backend."* ·
*"Our maps size does not matter for aircraft they can fly even outside of a terrain. Radar radiation needs to be published/replicated so other
entities and on machines can detect that."*

## 0. Start
`git fetch origin coordinator backend behaviors`; on `backend`, merge `origin/coordinator` (and `origin/behaviors` — it carries CE-2436's air /
drone work you build beside); restore the test projects you will run; push `chore: started radar SAM at <sha>`. Read the design END TO END, then
Air Ops §7d–§7f and the thermal design §6 F / §8.7 (R-129).

## 1. How you work
| | |
|---|---|
| ⭐⭐ **you build ALL of it** — the sensor, signatures, missile, vehicle type, the behaviour (`RadarSamNodes`), emission control, the warning and the aircraft's reaction stage, the emitter SPOT | ⭐ cross-lane edits into `Hrot.AI.Behaviors` (`AirDefenceNodes`, `AirAttackNodes`, the SOP rows) are allowed — name the lane and the file in each commit |
| ⭐⭐ **decide, never wait** — every default the design names (ranges, signatures, timings, relocation distance) is yours to tune, recorded as an `R-` lane decision with what would change it | ⛔ never "tune until the scenario passes" |
| ⭐⭐⭐ **`RadarEmission` is the ONE owner of "radiating"** (RS-10) — replicated; the sensor, the warning receiver, the direction finding and the visuals all DERIVE from it on their own node; nobody writes another node's entity | the user's ruling |
| ⭐⭐ **aircraft BEYOND the terrain** (RS-2a) — every rail of B-1 / B-2 / B-6 includes one; measure the burst's fragments reaching an off-grid aircraft and fix it where they do not | the user's ruling |
| ⭐⭐ **ONE fire-control sight rule** (RS-4) — radar counts as seen only for a radar-guided mount; `SightNow` unchanged; an MG must not fire at a radar-only contact | the review's top blocker |
| ⭐ **the visuals seam** (RS-9): fix the vehicle's DIS type and class, the launcher on turret 0, placeholder `MunitionVisualDto` / effect rows — the ui lane (CE-4618) builds the shape kit and tunes the looks | ⛔ there is no asset key |
| ⭐ RS-7: the SPOT goes to the opposing side's TOP commander as a `SquadReportNotification` (the path is measured in the design); `SupportCommand` gains the SEAD rule | a failed premise stops that item only |

## 2. The order — the design's §5
B-1 → B-2 → B-3 → B-4 → B-5 → B-6, each "done when" as §5 says. ⚠ B-1: measure the thermal §8.7 trap (an extra sensor child changed behaviour)
on the carrier and report it.

## 3. Gates — every slice
T-1 first (`AirDefenceNodesTests`, `AirDefenceTranslatorsTests`, `AirAttackNodesTests`, the thermal perception rails, `EqsDistributedTests`, the `SupportCommand` / staff suites); every behaviour rail with
`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads
1,4,8` on `sam-site`; 0 B per steady frame; invariant output; `RadarEmitter` unmanaged. Build the affected project in the fix loop; the live runs in
the background, three each.

## 4. The report — `STATUS_Backend_Radar_SAM.md`
① `sam-site` runs (cue, warning, salvo, outcome, relocation, the SPOT) · ② lane decisions with what would change each · ③ per slice built /
stopped · ④ the gate-report contract (rule 8) · ⑤ ids allocated · ⑥ the design folded to the as-built (obligation ⑤).
