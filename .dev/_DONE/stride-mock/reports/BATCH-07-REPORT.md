# BATCH-07 Report: SM-011 Integration Validation Gate

**Batch:** BATCH-07
**Task:** SM-011 — Full Integration Validation Gate
**Status:** COMPLETE (static checks) / PARTIAL (runtime checks deferred)
**Date:** 2026-05-14

---

## Summary

All 5 static checks PASS. All 3 test suites match their baselines exactly (no regressions).
Runtime cluster checks are deferred and require a live cluster environment.

---

## Check 1 — DRY: StrideNodeBootstrapper isolation

**PASS**

Grepped `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs` for `Raylib`, `ImGui`,
and `IMapCameraProvider` in C# code statements and using directives.

All matches found were in XML doc comment lines (`///`):
- Line 47: `/// IMPORTANT: this class must not reference Raylib, ImGui, or`
- Line 48: `/// <c>IMapCameraProvider</c>. It is engine-agnostic by design.`
- Line 92: `/// The Raylib renderer reads from this buffer.`

Zero references in actual C# code (no using directives, no statements, no type references).
Constraint is satisfied: `StrideNodeBootstrapper` is engine-agnostic.

---

## Check 2 — Both entry points use StrideNodeBootstrapper

**PASS**

`StrideMockSubsystem.cs`:
- Line 40: `private StrideNodeBootstrapper? _core;`
- Line 86: `_core = new StrideNodeBootstrapper();`
- Line 87: `_core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, _networkFactory);`

`FakeStrideApp.cs`:
- Line 38: `private StrideNodeBootstrapper? _core;`
- Line 88: `_core = new StrideNodeBootstrapper();`
- Line 89: `_core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, networkFactory);`

Both entry points instantiate `StrideNodeBootstrapper` directly via `new`.

---

## Check 3 — Test suites at baseline (SM-009 + SM-010)

**PASS — No regressions**

| Test Project            | Baseline Pass | Actual Pass | Baseline Fail | Actual Fail | Skipped | Regression? |
|-------------------------|:------------:|:-----------:|:-------------:|:-----------:|:-------:|:-----------:|
| Hrot.SimHost.Tests      |    ~566      |     566     |     ~27       |     27      |    3    |     NO      |
| Hrot.IG.Tests           |    ~319      |     319     |     ~68       |     68      |    0    |     NO      |
| Hrot.StrideMock.Tests   |      41      |      41     |       0       |      0      |    0    |     NO      |

All three suites match baselines exactly. The 27 SimHost failures and 68 IG failures are
pre-existing, not regressions introduced by this workstream.

---

## Check 4 — SharedApplicationBootstrapper concrete subclasses

**PASS**

Grepped for `: SharedApplicationBootstrapper` across all C# source files (excluding
design docs and batch instructions). Production concrete subclasses found:

1. `StrideNodeBootstrapper` — `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs` line 51
2. `SimHostNodeBootstrapper` — `Hrot\Subsystems\Hrot.SimHost\SimHostNodeBootstrapper.cs` line 37
3. `IgNodeBootstrapper` — `Hrot\Subsystems\Hrot.IG\IgNodeBootstrapper.cs` line 46
4. `TestBootstrapper` (test stub) — `Hrot.StrideMock.Tests\SharedApplicationBootstrapperTests.cs` line 56

Exactly the expected 3 production subclasses plus the test stub. No unexpected subclasses.

---

## Check 5 — SharedApplicationBootstrapper phase ordering invariant

**PASS**

Verified in `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`,
`BootstrapNode()` method. Phase ordering confirmed:

- Phase 6a: `RegisterSpawningPipeline(context)` called first within Phase 6
- Phase 6a+: `context.Kernel.RegisterModule(context.NedReplication)` — NedReplication
  registered AFTER `RegisterSpawningPipeline`, BEFORE `RegisterNetworkTranslators`
- Phase 6b: `RegisterNetworkTranslators(context, ...)` — called after NedReplication
- Phase 6c: Time-sync translators wired (base class, not a hook)
- Phase 6d: `RegisterApplicationSystems(context)` — called BEFORE `Kernel.Initialize()`
- Phase 7: `context.Kernel.Initialize()` — confirmed as the LAST statement in the method

All phase ordering invariants from DESIGN.md §4 are satisfied.

---

## Deferred Runtime Checks

The following SM-011 checklist items require a live running cluster and cannot be verified
statically. All are deferred pending a cluster environment:

| Item | Description | Status |
|------|-------------|--------|
| SC_SM006_7 / SC_SM007_7 | `[StrideMock]` tab visible; camera sync on tab switch works | DEFERRED — requires live cluster |
| SC_SM007_6 | Standalone mode boots cleanly | DEFERRED — requires live cluster |
| SC_SM008_1–SC_SM008_7 | FakeStrideApp visual + lifecycle verification | DEFERRED — requires live cluster |
| Replay safety | Load recording, seek backward — no ghost entities | DEFERRED — requires live cluster |
| Recording | `OperatingLive` session produces `node_700.fdp` in staging | DEFERRED — requires live cluster |
| 2PC | `SerializeLocal` and `PrefetchFiles` commands ACKed correctly | DEFERRED — requires live cluster |
| Diagnostics | `CollectDiagnostics` produces valid dump from node 700 | DEFERRED — requires live cluster |
| Time | Orchestrator Pause halts all nodes on same tick | DEFERRED — requires live cluster |

---

## Deviations and Issues

None. All static checks passed with results matching expected values exactly.
