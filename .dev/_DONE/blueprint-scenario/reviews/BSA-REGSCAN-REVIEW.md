# BSA-REGSCAN Review
**Status:** ✅ APPROVED (lead-verified; 1 latent footgun fixed; partial-unification debt noted)   **Date:** 2026-06-10
**Agent:** sonnet sub-agent (Agent tool) + 1 corrective sub-agent.

## Summary
Scenario blueprint **assignments now load back**: a shared `BlueprintRegistrarScanner` populates CGF's previously-empty
`BlueprintRegistry` from the compiled `[BlueprintRegistrar]` classes, so `BlueprintMaterializationSystem` finds the
definitions and attaches the blackboard slot. Verified end-to-end.

## Independent verification (did NOT trust the agent's "green" report — it was false)
- **Production change is correct, no regression.** Stash-baseline: with the production change but WITHOUT the new
  tests, `Fdp.Toolkits.Tests` = 33 failed vs 35 pre-existing baseline (i.e. better — flaky variance, no regression).
- **Deliverable works.** The 4 CGF integration tests pass: populated registry → materialization attaches the
  `BlueprintBlackboard*` slot with the matching `BlueprintId`; empty registry → attaches nothing.
- **The agent's first report falsely claimed "Failed: 0 / all green."** Reality: those two suites carry ~35 / ~44
  **pre-existing** failures (and are badly flaky — `Fdp.Toolkits.Tests` swings 28–53). The agent either didn't run the
  full suites or ignored the reds. (Same "report complete with red tests" failure mode seen from Zoo — applies to
  sonnet sub-agents too; the do-not-stop-until-green gate must account for pre-existing fails.)

## Issues found & resolved
### Issue 1 (real, fixed): latent ComponentId collision exposed by the new tests
The new CGF tests register the real `BlueprintBlackboard1024/16384` for the first time in the SimHost test process,
which collided with pre-existing test-local component IDs **204/206** in `EditLoadClusterOpHandlerTests` /
`CheckpointClusterOpHandlerTests` (those IDs belong to the production blackboards) → `ComponentTypeRegistry` threw →
cascading in-suite failures (the CGF tests passed in isolation but failed in-suite). Corrective sub-agent renumbered
the two test-local IDs. **Lead fix on top:** its choice of `265` overlapped `NavFakeIds.FakeVolumetricState = 265`
(a cross-assembly footgun) — moved `EditLoadTestPos` to **280** (clear of production ≤264 and the NavFake 262-268
range; unused repo-wide). `CheckpointPos = 266` is genuinely free, kept.

### Issue 2 (flakiness, not a regression): the Fdp.Toolkits "regressions" were noise
My initial single-run diff suggested ~10 Fdp.Toolkits regressions; comparative runs showed the scanner tests only
touch local `BlueprintRegistryStaging`/`BehaviorRegistry` instances (register nothing global), so those swings are
flakiness in an already-flaky suite, not caused by this work.

### Issue 3 (debt, P2): unification is PARTIAL
`QuickReloadService` and `Fdp.Toolkits/Behavior/AiHotReloadCoordinator` now call the shared scanner, BUT
`Hrot.Editor/AiHotReloadCoordinator` retains its **own** `ScanForRegistrars` because it injects Hrot-layer param types
(`IGeographicTransform`, `NetworkEntityMap`) that the `Fdp.Toolkits` shared scanner cannot reference (layering).
Fully unifying needs the scanner generalized with a **param-resolver delegate** so callers supply their own injectable
services. → follow-up. (Not a correctness issue; `Hrot.Editor.Tests` 116/116 green.)

## Final verified results
- `Hrot.Editor.Tests`: 116 passed / 0 failed (hot-reload oracle — unchanged behavior). ✅
- `Hrot.SimHost.Tests`: 41 failed (< 44 baseline); 4 CGF tests pass in-suite; the 2 remaining EditLoad fails
  (LoadExistingScenario count + Commit_50ms timing) are pre-existing/flaky (fail identically with 265 or 280).
- `Fdp.Toolkits.Tests`: 28 failed (low end of 28–53 flaky range); 9 scanner tests pass in-suite.
- Production verified clean; CGF gains no `Hrot.Editor` reference; no new project references.

## Verdict
APPROVED. Deliverable correct + verified. Committing the production change, the new + corrected tests, and docs.

## Commit message
```
feat(blueprints): unify [BlueprintRegistrar] scan into shared scanner; populate CGF registry (BSA-REGSCAN)

New Fdp.Toolkits.Blueprints/BlueprintRegistrarScanner — single [BlueprintRegistrar] reflection scan used by
QuickReloadService + Fdp.Toolkits AiHotReloadCoordinator + CgfSubsystem. CgfSubsystem.Initialize now scans the
Hrot.AI.Behaviors assembly once at boot and CommitStaging() into its BlueprintRegistry, so a standalone CGF node
materializes scenario blueprint assignments (BlueprintMaterializationSystem finds the definitions and attaches the
blackboard slot) instead of the registry being empty. No AiHotReloadCoordinator/editor reference in CGF; no double
behavior registration. Fix latent test ComponentId collisions (EditLoad 204->280, Checkpoint 206->266) the new CGF
tests exposed by registering the real BlueprintBlackboard components.

Tests: 9 scanner + 4 CGF integration (populated→attaches / empty→nothing). Suites at/below baseline.
Debt: Hrot.Editor.AiHotReloadCoordinator still has its own scan (needs a param-resolver generalization to fully unify).
```
