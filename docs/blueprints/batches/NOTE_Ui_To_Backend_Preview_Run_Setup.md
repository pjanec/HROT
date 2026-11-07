<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — LC-11 (L-10 / CE-4317): the Run setup on the mode menu applies to Preview AND Live
-->

# NOTE — ui → backend: a Preview that takes a Run setup (L-10 / CE-4317) — REQUEST

**What ui measured.** Live takes a run setup: ▶ Live → `IRunScenarioControl.LoadForLive(scenario, argsJson)` → the load's `ScenarioArgsJson`
(params + `$variant`, applied by `ScenarioLoadStep` on every node). ⛔ A **Preview** does not load: ▶ Preview → `ITimeTransportFacade.EnterDryRun(startPaused)`
runs a COPY of the edit world, which was loaded with the authored values. ⇒ a variant / changed params cannot reach a Preview without a backend path.

**REQUEST:** a dry run that takes the run's setup — `EnterDryRun(startPaused, argsJson)` — applying the params and the variant to the dry-run copy on
every node (the same `ScenarioParams` / `ScenarioVariants` the load applies), and restoring the authored world on Stop as today.
⭐ Lean: re-run the load's apply step on the copy (one implementation — the load's), never a second params applier; ⛔ never re-open the scenario
for edit with args (that would bake the setup into the authored world and a Save would keep it).

**ui side, ready once it exists:** the mode menu's *Run setup…* (the picker's `RunSetup` editor, one per scenario, `RunModeChoice.LastRunSetupOf`)
passed by ▶ to BOTH Live (already) and Preview. Until then L-10 is ⏳ in ui's status.

---
_ui lane, 2026-11-07_
