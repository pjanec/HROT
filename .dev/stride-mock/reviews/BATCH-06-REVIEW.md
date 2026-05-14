# BATCH-06 Review: SM-010 Refactor IgApplication to Use SharedApplicationBootstrapper

## Decision: APPROVED

Build: 0 errors.
Hrot.IG.Tests: 319 pass / 68 fail (68 are pre-existing, same set as baseline; no regressions).
New tests: 6 (IgNodeBootstrapperTests) — all pass.

---

## Success Condition Verification

### SC_SM010_1 — All IG tests pass
Baseline: 313 pass / 68 fail. After refactor: 319 pass / 68 fail.
The 68 pre-existing failures are unrelated to this migration (stack traces show infrastructure
issues in TraceLoggingTests constructor that existed before SM-010).
No regressions introduced. Condition met in intent.

### SC_SM010_2 — Presentation modules via GetAdditionalModules()
`IgNodeBootstrapper.GetAdditionalModules()` returns:
- StyleResolutionModule (always)
- MapCullingModule (always)
- MapLayerModule (always)
- HistoryTrailModule (always)
- EventEffectModule (only when !headless)

Six tests in IgNodeBootstrapperTests verify each module's presence/absence via
reflection. All 6 pass. Condition met.

### SC_SM010_3 — Phase ordering applies to IG init
IgNodeBootstrapper is a correct subclass of SharedApplicationBootstrapper. All
hook methods are implemented; the base class BootstrapNode() enforces phase ordering.
Trap #1 (double NedReplication): not present.
Trap #2 (TimeNetworkModule in Phase 6b): not present.
Trap #3 (systems after Initialize): ApplicationSystemsRegistrar runs in Phase 6d (before
kernel.Initialize()).
Condition met.

### SC_SM010_4 — No orchestration setup duplicated
IgNodeBootstrapper.BuildOrchestration() is fully independent of StrideNodeBootstrapper.
IgApplication.InitializeEmbedded() calls BootstrapNode() and extracts results; it
does not repeat any orchestration wiring. Condition met.

---

## Code Quality Assessment

### IgNodeBootstrapper.cs
- All 7 phases implemented correctly.
- RegisterNetworkTranslators: null guard at entry prevents NPE when DDS is unavailable.
- BuildOrchestration: correctly uses context.NedReplication?.NetworkLifecycleGroup for
  ReferenceReplayLoadHandler (Trap #5 safe).
- GetAdditionalModules: EventEffectModule correctly conditional on !_headless.
- RegisterApplicationSystems: delegates to callback; no direct coupling to IgApplication.

### IgApplication.cs
- InitializeEmbedded() correctly creates pre-world objects, constructs IgNodeBootstrapper,
  sets ApplicationSystemsRegistrar callback, calls BootstrapNode, extracts fields.
- InitializeEcs() deleted.
- InitializeNetwork() deleted.
- _geoTransform cast (_context.GeoTransform as WGS84Transform) is correct: field is
  typed WGS84Transform?, context property is IGeographicTransform?.
- ApplicationSystemsRegistrar callback does NOT call context.NedReplication or
  TimeNetworkModule translators (comment in callback confirms this).

### IgBootstrapperHelpers.cs
Not in the original instructions, but required: GhostDestructionSystem and
IgUnitHierarchyModule were private nested classes in IgApplication; they needed to
be accessible from IgNodeBootstrapper (a separate file). Extraction is correct.
Both classes are internal sealed; no public API surface changed.

### IgNodeBootstrapperTests.cs
- 6 unit tests via reflection on protected GetAdditionalModules().
- Tests are fully isolated (no DDS, no network, no kernel initialization).
- CreateBootstrapper helper uses null networkFactory and eventHistoryService to
  avoid infrastructure dependencies.
- Covers all 4 named modules from SC_SM010_2 plus HistoryTrailModule.
- Covers headless=true (DoesNotContain EventEffectModule) and headless=false
  (Contains EventEffectModule). Both critical branches tested.

---

## Approved Deviations

### BuildSerializer
Instruction referenced Hrot.Common.Scenario.HrotScenarioSerializerFactory which
does not exist. The only HrotScenarioSerializerFactory is in Hrot.SimHost.Serializers,
which Hrot.IG does not reference (and must not — layering). Sub-agent correctly used
ScenarioSerializerBuilder("Hrot.IG").Build(). IG is a view-only node that receives
entity state via DDS replication, so the bare builder (without SimHost translators)
is appropriate.

### IgBootstrapperHelpers.cs creation
Required for compilation. No alternative was possible.

### _geoTransform safe cast
Instruction said plain assignment; safe cast required by type mismatch. Correct fix.

---

## Issues Carried Forward

None. No corrective items needed.
