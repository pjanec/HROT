<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Brain_Cost_1000.md — the frame (CE-4395)
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — behaviors' design; any cadence change is proposed THERE, with a lean
-->

# NOTE — backend → behaviors: backend is profiling and speeding up the BRAIN at 1 000 men (CE-4395)

Started `cec9e162d` (`2026-11-10`). The coordinator handed backend the cost of `BrainTickSystem` on the CGF (20 ms at 1 000 men, 410–500 ms at 3 000 —
something grows superlinearly). What this means for you:

| | |
|---|---|
| ⛔ behaviour does not change | speed-ups only; the scenario regression (`--verify all`) is the guard, run before and after |
| edits stay narrow | first: TIMING SLOTS (`QueryTimings`) inside the brain's machinery to attribute the cost; then the fix at the measured hot spot. Each commit names the files it touches |
| a CADENCE change | proposed in YOUR design (Evaluation Cost & Cadence) with a lean, and announced here first — ⛔ never built silently |
| your vehicle-tactics work | I will avoid the vehicle brain files unless the numbers point there; if they do, I will say so here before editing |

Status: `docs/blueprints/batches/STATUS_Backend_Brain_Cost_1000.md`.

## Update `2026-11-10` — what backend changed in your code, and the one cadence the user approved

| | |
|---|---|
| ✅ exact speed-ups (no behaviour change) | `PartIndex` takes parts added within a frame from a new repository add log instead of rebuilding (62 rebuilds a tick → 1 at 3 000 men); `BehaviorOwnedParts.Release` reads the parts index and destroys in entity order (indices recycle as before). Cost & Cadence **§11g-1** |
| ⭐ **a CADENCE — approved by the user** (*"0.2 seconds is very much acceptable"*) | three decisions declare `CadenceSeconds = 0.2` on their `[UtilityDecision]`: **Combat posture, Attack approach, Threat ranking**. The scorer (`DecisionCadence`) re-scores a unit's decision every 0.2 s on its phase and at once on a `SensorChangedEvent`; between, it returns the stored result and writes the unit buffer and decision log back as that scoring wrote them. Opt-in per decision; nothing else moved. **§11g-2** |
| ⚠ what moves | a posture / approach / top-threat switch can come up to 0.2 s later when nothing woke the man; decision-log stamps move with it |
| ⚠ **a finding for you** — `CadenceGate.Next` | it adds the stagger on EVERY tick (`now + p + phase`), so a Periodic slot and the SOP run every 0.2–0.38 s, not every 0.2 s. I left it as is (changing it moves the SOP's timing) and added `CadenceGate.NextOnGrid` (a fixed phase; interval = the period) for the decision cadence. Your call whether the SOP should move to it |
| ⚠ the generated decision catalog | it rebuilds the attribute from constructor arguments, so it drops `CadenceSeconds` (as it already drops `OptionNames`); there a decision scores every call — as before |
| ✅ B-3 (garbage) | `PersonHands.Holding` now has a struct-visitor form (the `Func` one adapts to it) and `ParameterResolver.WarheadOnly` skips the provenance text: "am I armed" allocates nothing (the brain 35 → 8.5 KB a frame) |
| ⚠ **for you** — order garbage | what is left is `SquadMarch`'s order traffic: ~930 B per `AssignTacticalIntentEvent` (managed + JSON params) re-issued to a follower as the lead walks; 125 marching squads make it every frame. Zero needs an unmanaged order event — your contract; not touched |
