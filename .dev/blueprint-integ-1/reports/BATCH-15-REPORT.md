# BATCH-15 Report
**Task:** AIE-053 — SubElementCollision detector + dangling-reference classification (Phase 5 FINAL)

## Implementation Summary

### Task 1: SubElementCollisionDetector + Inspector diagnostic strip

**New file** `Hrot/Editor/Hrot.Editor.AiShared/Validation/SubElementCollisionDetector.cs`:
- `ActionCollision(string ShortName, IReadOnlyList<string> ClaimingFqns)` sealed record.
- `SubElementCollisionDetector.GetCollisions(IActionSchemaExporter)`: groups `schemaExporter.All.Values` by `ExtractShortName(entry.Fqn)` (last-dot-segment), keeps groups with >1 distinct FQN, sorts claimants ascending.
- Handles duplicate FQNs in the dictionary defensively via `.Distinct()`.

**InspectorWindow changes** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`):
- New optional `IActionSchemaExporter? schemaExporter = null` ctor parameter (appended; all existing call sites still compile).
- `DrawCollisionDiagnosticStrip()` private method — only invoked when `_schemaExporter` is non-null AND collisions exist; renders a red `ImGuiChildFlags.Borders` strip with sorted claimant FQNs.
- `DrawCollisionDiagnosticStrip()` called as first statement in `DrawClientArea()`.
- `GetCollisions()` internal headless accessor returning null when exporter is absent — used by headless tests.

**PerspectiveWorkspaceRegistrar changes** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs`):
- New optional `IActionSchemaExporter? schemaExporter = null` ctor parameter (appended after `aggregatorService`).
- Forwarded to `InspectorWindow(schemaExporter: schemaExporter)`.

**EditorSubsystem wiring** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`):
- Extracted `sharedSchemaExporter = new ActionSchemaExporter()` before constructing `BlackboardAggregatorService` (which now receives `sharedSchemaExporter` instead of an inline `new`).
- All three registrars (`_btreeRegistrar`, `_hsmRegistrar`, `_blueprintRegistrar`) receive `schemaExporter: sharedSchemaExporter`.

### Task 2: Dangling-reference classification + ApplyDelete refusal

**IRefactorService.cs additions**:
- `ReferenceCriticality` enum: `AutoResolvable` / `Critical`.
- `ClassifiedDanglingReference(AssetReferenceInfo Reference, ReferenceCriticality Criticality)` sealed record.
- `DeletePreview` extended with:
  - `IReadOnlyList<ClassifiedDanglingReference> ClassifiedReferences { get; init; }` — defaults to `Array.Empty<>()` so all existing positional constructors compile unchanged.
  - `IReadOnlyList<AssetReferenceInfo> CriticalReferences` — computed property filtering `ClassifiedReferences` for convenience.
  - Backward-compatible: `DanglingReferences` unchanged; new members are init-settable with safe defaults.

**RefactorService.cs changes**:
- `PreviewDelete`: after collecting `danglingRefs`, classifies each with `ClassifyReference(r)` and populates `ClassifiedReferences` via `with { ClassifiedReferences = classified }`.
- `ApplyDelete`: refuses with `RefactorResult(false, …, reason)` when `ClassifiedReferences` contains Critical refs AND the preview contains a Warning issue (i.e. `AllowDanglingReferences` was `false`). Does not delete the file.
- `ClassifyReference(AssetReferenceInfo) → ReferenceCriticality` private static method with explicit `SubElementKind` switch expression.

## Design Decisions

### SubElementKind → Criticality mapping

| SubElementKind      | Criticality       | Justification |
|---------------------|-------------------|---------------|
| `ActionFqn`         | **Critical**      | Resolved at C# compile-time via the generated action registrar; removing the declaring type breaks the build. |
| `ConditionFqn`      | **Critical**      | Same pattern as ActionFqn — generated registrar hardcodes the declaring type. |
| `GuardFqn`          | **Critical**      | Same pattern — HSM guard registrar binds the type at compile-time. |
| `AssetReference`    | **Critical**      | The caller's typed field references the deleted asset's exported type; deletion breaks C# compilation. |
| `BlackboardField`   | **Critical**      | The blackboard DTO struct field type originates from the deleted asset; removing the type breaks struct compilation. |
| `EventName`         | **AutoResolvable**| Event dispatch is name-based at runtime; a missing event simply goes unhandled — no compilation dependency. |
| `BlackboardVariable`| **AutoResolvable**| Variable binding is name-based and resolved at runtime; missing master variables degrade gracefully. |
| `UtilityInput`      | **AutoResolvable**| Utility input selectors use string names; a missing entry fails softly at runtime. |

### ApplyDelete refusal heuristic
The `ApplyDelete` method does not receive the original `DeleteOptions`. The pattern used: a `Warning` issue is added to `DeletePreview.Issues` exactly when `AllowDanglingReferences == false` and there are dangling refs. So the refusal guard checks `ClassifiedReferences` has Critical entries **AND** `Issues` contains a Warning — which faithfully maps `AllowDanglingReferences: false + Critical refs → refuse`.

### Exporter threading
The `ActionSchemaExporter` instance used by `BlackboardAggregatorService` (constructed once in `EditorSubsystem.RegisterWindows`) is reused as `sharedSchemaExporter` and forwarded to all three perspective registrars, which forward it into their `InspectorWindow`. No second `ActionSchemaExporter` is created.

### DeletePreview backward-compatibility
The positional record constructor `DeletePreview(Guid, IReadOnlyList<AssetReferenceInfo>, IReadOnlyList<RefactorIssue>)` was not changed. New members use `init;` with empty-array defaults, so stubs and tests that use the positional form compile and work unchanged.

## Deviations

None. Implementation follows the design-talk skeleton and batch instructions exactly.

## Test Results

### Hrot.Editor.AiShared.Tests
**737 / 0** (baseline 718, +19 new tests)

New tests:
- `CollisionDetectorTests` (6 tests, `Validation/CollisionDetectorTests.cs`):
  - `CollisionDetector_FlagsDuplicateShortNames` — asserts short name "DoThing", 2 sorted claimants.
  - `CollisionDetector_NoCollision_WhenShortNamesUnique` — asserts empty list.
  - `CollisionDetector_SameFqnTwice_NotACollision` — single dict entry, asserts empty.
  - `CollisionDetector_ThreeClaimants_SortedAscending` — three FQNs sharing "Fire", asserts sorted order.
  - `CollisionDetector_FqnWithNoDot_ShortNameIsFqnItself` — different no-dot FQNs, no collision.
  - `CollisionDetector_MultipleCollisions_ReturnsOnePerShortName` — 2 collision groups, asserts both.
- `Batch15RefactorTests` (13 tests, `Refactor/Batch15RefactorTests.cs`):
  - `PreviewDelete_ClassifiesCriticalVsAutoResolvable` — theory across all 8 SubElementKinds.
  - `PreviewDelete_MixedKinds_SplitCriticalAndAuto` — 2 Critical + 2 Auto, asserts split + CriticalReferences.
  - `ApplyDelete_RefusesCritical_WhenDisallowed_DoesNotDeleteFile` — verifies file NOT deleted + reason contains "critical".
  - `ApplyDelete_AllowsWhenAccepted_DeletesFile` — AllowDanglingReferences=true proceeds.
  - `PreviewDelete_AutoResolvableOnly_DoesNotBlock` — EventName ref → ApplyDelete succeeds.
  - `PreviewDelete_NoRefs_ClassifiedReferences_IsEmpty` — empty catalog.

### Other suites (no new failures)
| Suite | Result |
|-------|--------|
| `Hrot.BTree.Editor.Tests` | **380 / 0** |
| `Hrot.Hsm.Editor.Tests` | **330 / 0** |
| `Hrot.Blueprints.Tests` | **1026 / 11** — 10 pre-existing DEBT-006; 11th is `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` (locale decimal separator "0,8" vs "0.8", confirmed pre-existing by git stash check) |
| `EditorSubsystemBoot` filter | **10 / 0** |

### Full solution build
`dotnet build IOS-IG-SimHost.sln` → **Build succeeded. 0 Warning(s). 0 Error(s).** (GizmoMap.Contracts 0.2.2 untouched; Hrot.IG/DDS untouched.)

## Developer Insights

- The design-talk skeleton used `bool` as the third arg to `BeginChild` which is the old ImGuiNET API; the codebase uses `ImGuiChildFlags` — fixed to `ImGuiChildFlags.Borders`.
- The `ApplyDelete` refusal decision (using the Warning issue as a proxy for `AllowDanglingReferences: false`) avoids changing the `IRefactorService` interface or adding a DeleteOptions field to `DeletePreview`. It is slightly indirect but defensible given the existing design contract.
- `ConditionSummaryAttachmentTests` failure is a pre-existing locale issue (decimal comma in European locale), unrelated to BATCH-15. Logged here for Lead awareness but not counted as a new failure.

## Known Issues

- **DEBT-011 remains open:** `BlueprintReferenceContributor` returns no edge references — cross-asset peer-call (CallPeerBlueprintNode) tracking was deferred in BATCH-14 and not addressed here. The classification infrastructure is in place; the moment real Blueprint edge references are contributed, they will be correctly classified (currently `AssetReference` → Critical).

## Suggested Commit Message

```
feat(editor): SubElement collision detector + dangling-ref classification (BATCH-15, AIE-053)

SubElementCollisionDetector scans IActionSchemaExporter for short-name collisions; InspectorWindow
shows a red diagnostic strip (headless-safe) when collisions exist; sharedSchemaExporter forwarded
to all three PerspectiveWorkspaceRegistrar instances.

RefactorService.PreviewDelete now classifies each dangling reference (ActionFqn/ConditionFqn/
GuardFqn/AssetReference/BlackboardField → Critical; EventName/BlackboardVariable/UtilityInput →
AutoResolvable); ApplyDelete refuses with a clear error when Critical refs exist and
AllowDanglingReferences=false. DeletePreview extended backward-compatibly.

Build: 0 errors / 0 warnings. Tests: AiShared 737/0 (+19), BTree 380/0, Hsm 330/0,
Blueprints 1026/11 (10 DEBT-006 + 1 pre-existing locale), EditorSubsystemBoot 10/0.
Phase 5 complete. DEBT-011 remains open (BlueprintReferenceContributor edge refs).
```
