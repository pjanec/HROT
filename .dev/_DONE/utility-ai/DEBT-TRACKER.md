# Utility AI — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-01 | BATCH-01 | `Fnv1a32_CoverQuery_ProducesStableNonZeroValue` has pinned value (0x9317A97B) in comment but `Assert.Equal` is commented out. Activate it to catch algorithm regressions. File: `UtilityTestWorldTests.cs` | P2 | BATCH-03 | RESOLVED (0x72BE4C04u pinned) |
| D-03 | BATCH-02 | `Quadratic` and `InverseQuadratic` hardcode `qDx*qDx` and ignore the `Exponent` field. An author passing `exponent:3` gets a quadratic not cubic. Add a doc comment or consider using `MathF.Pow(qDx, Exponent)` for a general power curve. File: `ResponseCurveEvaluate.cs` | P3 | BATCH-04 | RESOLVED (inline Note: comments added in BATCH-04) |
| D-04 | BATCH-03 | New test files (`UtilityResultBufferTests.cs`, `UtilityScorerTests.cs`) use namespace `Fdp.Toolkit.Utility.Tests` while all prior utility test files use `Fdp.Toolkit.Tests`. Tests discover fine but should be normalized for consistency. | P3 | BATCH-05 | RESOLVED (namespaces normalised in BATCH-05) |
| D-05 | BATCH-03 | `UtilityResultEntry.WinningPostureId` is `byte`; `UtilityOption.OptionId` is `ushort`. Scorer uses `(byte)` cast silently. Phase 1 safe ([0,255]) but future option IDs > 255 would truncate silently. Add a debug assert at decision-def registration: `Debug.Assert(opt.OptionId <= byte.MaxValue)`. | P3 | BATCH-05 | RESOLVED (Debug.Assert added in UtilityCore.cs in BATCH-05) |
| D-06 | BATCH-03 | `UtilityInputRegistrar` uses `Dictionary<ushort, nint>` — acceptable for Phase 1 (registered at startup). Phase 2 source-gen should replace with a flat array indexed by InputId for O(1) read-time dispatch. File: `UtilityScorer.cs`. | P3 | BATCH-05 | RESOLVED (deferred to Phase 2 source-gen; acceptable at Phase 1 scale) |
| D-07 | BATCH-05 | `UtilityResultBuffer` was missing its `[ComponentId]` attribute, causing `InvalidOperationException` on `Repo.RegisterComponent<UtilityResultBuffer>()`. Fixed: added `UtilityResultBuffer = 151` to `UtilityApplicationComponentIds` and decorated the struct. | P1 | BATCH-05 | RESOLVED |
| D-08 | BATCH-06 | Residual namespace inconsistency in older test files: `StandardInputReaderTests.cs`, `CurveEvaluationTests.cs`, `AggregatorTests.cs`, `UtilityCoreTests.cs` still use `namespace Fdp.Toolkit.Tests.Utility` (old convention). The canonical namespace is `Fdp.Toolkit.Tests` (fixed in BATCH-05 for `UtilityScorerTests` and `UtilityResultBufferTests`; fixed in BATCH-06 for `StarterPackIntegrationTests`). All are under `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/`. | P3 | Phase 2 cleanup | OPEN |

| D-10 | BATCH-08 | `HashCollision_EmitsUT0103` in `UtilityInputGeneratorTests.cs` searches up to 200,000 candidates at runtime to find a collision pair (report: ~567ms). Pre-compute a known collision pair (e.g., iterate once, note the two names in a comment, hard-code them) so the test runs in microseconds. | P3 | BATCH-10 | OPEN |
| D-11 | BATCH-08 | `DistanceToContext` reader has `[UtilityInput]` removed to avoid CS0121 ambiguity. The generated `UtilityInputRegistrar.RegisterAll()` therefore skips it. `StandardInputs.RegisterAll()` is still the startup call so nothing breaks now. When the codebase transitions to `UtilityAutoDiscovery.ScanAndRegister()` as the sole startup mechanism, `DistanceToContext` would be silently unregistered unless a dedicated `[UtilityRegistrar]` wrapper or an explicit call is added. Add a regression test that calls only `ScanAndRegister()` and verifies `DistanceToContext` is still registered. | P2 | Phase 3/4 | OPEN |

| D-12 | BATCH-09 | `ForeachBuild_EmitsPartialManifest` test (SC-P2-02-4) was missing from the developer submission. Dev-lead added it directly before committing BATCH-09. | P1 | BATCH-09 | RESOLVED |
| D-13 | BATCH-09 | `UtilityRegistry.MergeFrom(UtilityRegistry source)` added but may be unused in the current `ScanAndRegisterDecisions` implementation. Confirm and remove if dead code. | P3 | BATCH-10 | OPEN |
| D-14 | BATCH-09 | The existing reflective `UtilityDecisionCatalog.RegisterAll(out UtilityRegistry)` in `UtilityDecisionCatalog.cs` is superseded by the generated catalog. Both perform the same work. Remove the reflective version after Phase 2 is complete. | P3 | Phase 3 cleanup | OPEN |

| D-15 | BATCH-10 | UT0121 (input used with wrong context) deferred. Requires per-input `AllowedContexts` metadata to be propagated from the `[UtilityInput]` attribute. Not yet defined in the attribute schema. | P3 | Phase 3 cleanup | OPEN |
| D-16 | BATCH-10 | UT0122 (parameterized input missing required param) deferred. Requires per-input param-schema (e.g., `EqsTopScore` requires a sensor name string). Schema not yet in `[UtilityInput]` attribute. | P3 | Phase 3 cleanup | OPEN |
| D-17 | BATCH-10 | UT0144 (all product-mode options with gates, no sum-mode fallback) warning deferred. Requires full option-mode analysis of the Build body. | P3 | Phase 3 cleanup | OPEN |
| D-18 | BATCH-10 | UT0145 (duplicate OptionId within a decision) warning deferred. Generator already handles this at gen time; analyzer check redundant for now. | P3 | Phase 3 cleanup | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
