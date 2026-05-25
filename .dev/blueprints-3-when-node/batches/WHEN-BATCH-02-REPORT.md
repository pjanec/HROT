# WHEN-BATCH-02 Report — Stage 2 Validators for WhenNode, ReadEqsResultNode, SpawnEqsSensorNode

## Tasks Completed
- **WHEN-M1-T3** — `V_WhenNodeRules` validator (BP2001–BP2015)
- **WHEN-M1-T4** — `V_ReadEqsResultNodeRules` validator (BP2020, BP2021)
- **WHEN-M1-T5** — `V_SpawnEqsSensorNodeRules` validator (BP2030, BP2031)

---

## 1. Summary of Files Changed

### Modified

| File | Changes |
|------|---------|
| `Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs` | Renamed BP2001→BP3010, BP2002→BP3011, BP2003→BP3012 in Stage 3 section; updated BP1600 comment; added BP2001–BP2015 (WhenNode), BP2020–BP2021 (ReadEqsResultNode), BP2030–BP2031 (SpawnEqsSensorNode) |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage3_Normalize.cs` | Updated BP2001→BP3010 (orphan node), BP2002→BP3011 (implicit cast) |
| `Hrot.Blueprints.Compiler/Compiler/CompileOptions.cs` | Added `IEqsTemplateCatalog? EqsTemplates = null` optional parameter |
| `Hrot.Blueprints.Compiler/Compiler/Stages/ValidationContext.cs` | Added `IEqsTemplateCatalog? EqsTemplates` property; initialized from `options.EqsTemplates` in constructor |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` | Added `#if NET8_0_OR_GREATER using Fdp.Toolkit.ReplayBrowser.Search; #endif`; registered `V_WhenNodeRules`, `V_ReadEqsResultNodeRules`, `V_SpawnEqsSensorNodeRules` in `Validators` list; added all three validator classes |
| `Hrot.Blueprints.Tests/Compiler/Stage3_NormalizationTests/Stage3_NormalizationTests.cs` | Updated [CoversDiagnosticCode("BP2001")]→"BP3010", [CoversDiagnosticCode("BP2002")]→"BP3011"; updated assertions `DiagnosticCodes.BP2001`→`BP3010`, `DiagnosticCodes.BP2002`→`BP3011`; updated comments |
| `Hrot.Blueprints.Tests/Compiler/Stage2_ValidationTests/V_AllValidatorsCoverageTests.cs` | Updated `KnownNotYetEmittedCodes`: removed "BP2003" (renamed to BP3012), added "BP3012" and "BP2015" (deferred) |

### Created

| File | Purpose |
|------|---------|
| `Hrot.Blueprints.Compiler/Compiler/Catalogs/IEqsTemplateCatalog.cs` | Compile-time catalog interface for EQS template asset IDs |
| `Hrot.Blueprints.Tests/Compiler/WhenNodeValidatorTests.cs` | 21 tests covering BP2001–BP2014 + valid happy path |
| `Hrot.Blueprints.Tests/Compiler/ReadEqsResultValidatorTests.cs` | 4 tests covering BP2020, BP2021 + valid happy path |
| `Hrot.Blueprints.Tests/Compiler/SpawnEqsSensorValidatorTests.cs` | 4 tests covering BP2030, BP2031, no-catalog suppression + valid happy path |

---

## 2. Diagnostic Codes Implemented vs Skipped

| Code | Status | Notes |
|------|--------|-------|
| BP2001 | Implemented | WhenNode in unsupported dispatch (Library, AiPrimitive, Instance pure-function) |
| BP2002 | Implemented | WhenNode missing required payload for its mode |
| BP2003 | Implemented | WhenNode ValueChanged: empty ComponentTypeId or PropertyPath |
| BP2004 | Implemented | WhenNode ValueChanged Source=PeerBlueprintVariable: peer not in sibling signatures |
| BP2005 | Implemented | WhenNode EventFired: EventTypeId empty or not in engine event catalog |
| BP2006 | Implemented | WhenNode EventFired: TargetFilter=Self without TargetFieldName |
| BP2007 | Implemented | WhenNode EventFired: PayloadCheck with empty PropertyPath or TargetValueText |
| BP2008 | Implemented | WhenNode ConditionMet: null Condition or empty CompoundPredicateDto (#if NET8_0_OR_GREATER for compound check) |
| BP2009 | Implemented | WhenNode ConditionMet: PropertyMatchDto.ComponentType == null (#if NET8_0_OR_GREATER) |
| BP2010 | Implemented | WhenNode EqsResult: SensorVariableName not declared as EqsSensorHandle variable |
| BP2011 | Implemented | WhenNode EqsResult: ScoreCrossed with zero threshold, or BecomesStale with zero/negative MaxAgeSeconds |
| BP2012 | Implemented | WhenNode Edges == WhenEdge.None |
| BP2013 | Implemented | WhenNode EventFired FallingEdge (Warning) |
| BP2014 | Implemented | WhenNode ValueChanged Epsilon != 0 (Warning, best-effort — no field-type lookup) |
| BP2015 | **Skipped** | WhenNode downstream of a Branch: exec pins are not materialized at Stage 2 (added in Stage 3 Normalize). Cannot reliably detect branch-successor nodes without pin data. Added TODO comment in validator. Deferred to Stage 3 or later. |
| BP2020 | Implemented | ReadEqsResultNode in unsupported dispatch |
| BP2021 | Implemented | ReadEqsResultNode sensor variable not declared |
| BP2030 | Implemented | SpawnEqsSensorNode in unsupported dispatch |
| BP2031 | Implemented | SpawnEqsSensorNode template not in catalog (suppressed when EqsTemplates is null) |

**Stage 3 renames:**
- BP2001 → BP3010 (orphan node elimination warning)
- BP2002 → BP3011 (implicit cast insertion warning)
- BP2003 → BP3012 (reserved placeholder, still unused)

---

## 3. Test Results

### New tests added: 29

| Test class | Count | All pass? |
|------------|-------|-----------|
| `WhenNodeValidatorTests` | 21 | Yes |
| `ReadEqsResultValidatorTests` | 4 | Yes |
| `SpawnEqsSensorValidatorTests` | 4 | Yes |

### Targeted test run (compiler + normalization):

```
Passed:  67, Failed: 0, Skipped: 0
```

Includes: WhenNodeValidatorTests (21), ReadEqsResultValidatorTests (4), SpawnEqsSensorValidatorTests (4),
Stage3_NormalizationTests (3+1=4), V_AllValidatorsCoverageTests (2), V_DispatchKindCompatibilityTests,
V_PeerReferencesTests, V_AiPrimitiveIntentTests, V_VariablesAndStateTests.

### Pre-existing failures (unrelated to this batch):

Approximately 98 tests fail in `MoveToAndFireDemoTests`, `InMemoryCompileTests`, and related end-to-end
tests due to JSON deserialization errors (`BlueprintDispatchKind` enum format mismatch in test asset JSON files).
These failures existed before this batch and are not caused by any changes made here.

---

## 4. Notes and Deferred Items

1. **BP2015 deferred**: The "WhenNode downstream of a Branch" check requires detecting nodes reachable
   from BranchNode via exec-flow links. At Stage 2, WhenNode (and most other new node types) have empty
   `Pins` lists because pins are added by Stage 3 Normalize. Without pin data, branch-successor detection
   is unreliable. A TODO comment is placed in `V_WhenNodeRules.Validate()`. This check should be
   implemented after Stage 3 pin materialization or as a post-normalize Stage 3 validator pass.

2. **BP2014 best-effort**: The epsilon warning fires for any non-zero Epsilon value regardless of field type,
   because full field-type resolution (needed to determine if the observed field is actually integer/boolean)
   requires Stage 4 TypeResolve work. This is documented in the validator comment.

3. **BP2009 net8.0 only**: The `HasUnresolvableComponentType` recursive check is guarded by
   `#if NET8_0_OR_GREATER` because `SearchPredicateDto`/`PropertyMatchDto` are only available in
   the net8.0 build of the compiler (Fdp.Toolkits reference is conditional). The null check part
   of BP2008 (Condition == null) works in both TFMs.

4. **EqsTemplates opt-in**: When `CompileOptions.EqsTemplates` is `null`, BP2031 is suppressed
   for non-empty TemplateAssetIds. This allows quick compile invocations without a template catalog.
   BP2031 is always emitted for `Guid.Empty` TemplateAssetId regardless of catalog presence.

---

## 5. Build Status

- `Hrot.Blueprints.Compiler` (netstandard2.0 + net8.0): **Build succeeded, 0 errors, 0 warnings**
- `Hrot.Blueprints.Tests` (net8.0): **Build succeeded, 0 errors** (pre-existing CS0618 warnings from unrelated code)
