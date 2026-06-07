# BATCH-20 Review

**Status: APPROVED**

---

## Scope Check

All 7 assigned tasks are implemented and verified:

| Task | Description | Result |
|------|-------------|--------|
| D-029 | Update `SaveScenario` test to `$meta.docType` | PASS |
| D-030 | Update `LoadScenario` throw test to `MigrationException` | PASS |
| D-031 | Remove `CycloneDDS.Schema` assertion from dependency test | PASS |
| D-025 | Fix `Phase2ConventionTests` hardcoded schema version | PASS |
| D-022 | Fix 31 EX_T + InlineArray test failures | PASS |
| JM-P5-002 | `test-data/scenario-corpus/BASELINES.md` created | PASS |
| JM-P5-003 | `.dev/json-migration/PR-CHECKLIST.md` created | PASS |

---

## Verification

### Tests run

```
dotnet test "Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj" --no-build
  --> Passed: 114, Failed: 0

dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" --no-build
     --filter "EX_T|Build_ComponentWithEntityInInlineArray|Defaults_MatchDesign"
  --> Passed: 31, Failed: 0
```

Full solution build (`IOS-IG-SimHost.sln -c Debug --no-restore`) succeeded. Only pre-existing
`Hrot.Blueprints.Tests` CS0234/CS0246 errors remain (known, do not fix).

### Pre-existing failures in full `Fdp.Toolkits.Tests` run

38 failures remain in the full toolkit suite. All confirmed pre-existing via `git stash` baseline check (they fail identically against the BATCH-19 commit). These are unrelated to this batch:
`GizmoSettingsPersistenceTests`, `RecordingSearchServiceTests`, `CoverGeneratorAndLosTests`,
`S3_TwoLayersRoutingTests`, `StatelessGizmoRegistryTests`, `DataDrivenGizmoSystemRoutingTests`,
`CombatComponentTests`, `SimTransformBridgeSystemTests`, `AimAndFireExecutorTests`, navigation tests.

---

## Design Alignment

All changes are strictly aligned with the design. Notable points:

- **D-022 root cause was misidentified in batch instructions** (documented as parallelism; actual cause was 3-layered). The developer correctly diagnosed all three root causes independently and implemented targeted fixes without over-engineering.
- **`JsonExportOptions.FormatMode` default correction** (Incremental → AbsoluteState) is validated by the design spec test `Defaults_MatchDesignSpec` which was already present and failing. This is a regression from BATCH-12 — the test existed as a guard and was simply overlooked.
- **Changelog baseline guards** are correct minimal additions. The original code's behaviour (emitting entries for first appearance and destruction) was wrong per the changelog semantics documented in the design.

---

## Test Quality

Tests assert values and behaviour (not just compilation):

- D-029/D-030: `Assert.Equal("Scenario", docType)`, `Assert.Throws<MigrationException>()` — value checks.
- D-025: Range check `schemaVersion >= 1 && schemaVersion <= CurrentVersion` — forwards-compatible logic.
- D-022: EX_T tests assert specific JSON structure (key presence, value equality, frame count, entity count). InlineArray throw test explicitly overrides `DataPolicy.Default` to ensure `Build()` throws as expected.
- `DisableTestParallelization` added as hygiene; not the root fix. Correctly documented in the report.

---

## Early Failure Discipline

- `FdpAutoSerializer.Build()` still throws `InvalidOperationException` loudly when an unsupported inline array type is found (unchanged).
- `ExportChangelogToJson` baseline guards use `continue` (skip silently) — this is correct because "no mutation occurred" is not an error condition.
- No silent exception swallowing introduced.

---

## Debt Tracker Updates

New debt items added:

| ID | Description | Priority |
|----|-------------|----------|
| D-032 | `AutoRegisterAllComponentTypes` scans test assemblies — test-only component types require manual `[DataPolicy(NoSnapshot)]` marking. Consider assembly-level filtering. | P3 |
| D-033 | `EntityJsonConverter` used in AbsoluteState event path but not in Changelog event path — inconsistent entity ref formatting. | P3 |

Resolved:
- D-022 ✅ D-025 ✅ D-029 ✅ D-030 ✅ D-031 ✅

---

## Suggested Git Commit Message

```
fix: BATCH-20 -- D-022/D-025/D-029/D-030/D-031 test fixes + JM-P5-002/003 docs

D-029: Update SaveScenario test to use dollar-sign-meta.docType and Entities (uppercase)
D-030: Update LoadScenario bad-JSON fixture to dollar-sign-meta format; expect MigrationException
D-031: Remove CycloneDDS.Schema DoesNotContain assertion from EditorDependencyTests
D-025: Fix Phase2ConventionTests schema version check -- range instead of exact match
D-022: Fix 31 EX_T + InlineArray test failures (triple root cause)
  - EntityInlineComp: add DataPolicy(NoSnapshot|NoSave|NoRecord) so AutoRegisterAllComponentTypes
    does not register it as snapshotable; update throw test to override with DataPolicy.Default
  - EX_T02-T24: add FormatMode=AbsoluteState to all tests expecting absolute-state JSON object
  - ExportChangelogToJson: add skip guard for first entity appearance and entity destruction frames
  - JsonExportOptions: fix default FormatMode from Incremental to AbsoluteState (design spec)
  - AssemblyInfo.cs: disable test parallelisation for hygiene
JM-P5-002: Add test-data/scenario-corpus/BASELINES.md (baseline refresh guide)
JM-P5-003: Add .dev/json-migration/PR-CHECKLIST.md (per-migrator PR checklist)
```

*(Already committed as `0db152a1`.)*
