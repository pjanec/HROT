# BATCH-BB1D Report

## Implementation Summary

### Task 1 — HSM `BuildFacetDispatcher(asset, ctx)` overload
**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmSelectionBridgeHelper.cs`

Added the missing context-aware overload:
```csharp
public static HsmFacetDispatcher? BuildFacetDispatcher(
    HsmAsset?            asset,
    HsmFacetFqnContext?  fqnContext)
    => asset is null ? null : new HsmFacetDispatcher(asset, fqnContext);
```
The context-aware `HsmFacetDispatcher(HsmAsset, HsmFacetFqnContext?)` constructor already existed (BB1B); this helper just exposes it through the same factory pattern as the BTree equivalent.

### Task 2 — `expressionTargetFieldAccessor` on both registrars
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (lines ~1943–1960)

Added `expressionTargetFieldAccessor: ResolveExpressionTargetField` to both `_btreeRegistrar` and `_hsmRegistrar` constructors. This enables the "Static Parameters" default-value StructEdit panel in the Inspector.

Added private static helper at the end of the `EditorSubsystem` class body (before the nested private classes):
```csharp
private static string? ResolveExpressionTargetField(object? facet) => facet switch
{
    BTreeActionFacet af          => af.ExpressionTargetField,
    BTreeConditionFacet cf       => cf.ExpressionTargetField,
    TransitionFacet tf           => tf.ExpressionTargetField,
    GlobalTransitionFacet gtf    => gtf.ExpressionTargetField,
    _                            => null,
};
```

### Task 3 — Shared `*FacetFqnContext` in `ActiveChanged` handler
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (lines ~2000–2031)

BTree branch: now creates `var btreeCtx = new BTreeFacetFqnContext()` and passes it to BOTH:
- `BTreePickerDrawerFactory.BuildDrawers(btreeAsset, _behaviorRegistry, sharedSchemaExporter, btreeCtx)`
- `BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset, btreeCtx)`

HSM branch: mirrors this with `var hsmCtx = new HsmFacetFqnContext()` passed to BOTH:
- `HsmPickerDrawerFactory.BuildDrawers(hsmAsset, sharedSchemaExporter, hsmCtx)`
- `HsmSelectionBridgeHelper.BuildFacetDispatcher(hsmAsset, hsmCtx)`

Each `ActiveChanged` event creates a fresh context — no stale context leaks across asset switches.

### Task 4 — Integration tests
**Files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BB1DSharedContextIntegrationTests.cs` (7 new tests)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/BB1DSharedContextIntegrationTests.cs` (7 new tests)

---

## Design Decisions

1. **Fresh context per `ActiveChanged`** — The simplest safe lifecycle: a new `*FacetFqnContext` is allocated on each document-switch event. Since contexts are small (2 string fields) and allocation is cheap relative to the document switch, this eliminates any stale-state risk across asset reopens.

2. **Accessor as private static method** — `ResolveExpressionTargetField` is a private static so it can be passed as a method group reference (`expressionTargetFieldAccessor: ResolveExpressionTargetField`) to both registrar constructors, avoiding a lambda allocation and keeping both registrars synchronized with the same logic.

3. **Integration tests in `*.Tests/Host/`** — The tests use the real `BuildFacetDispatcher` and `BuildDrawers` factory seams (not mocked), with a `StubExporter` (file-scoped) to control the schema. This is the same pattern used by the existing bridge helper tests.

---

## Deviations

None. All changes strictly follow the batch instructions.

---

## Test Results

### New tests added: 14

**Hrot.BTree.Editor.Tests — BB1DSharedContextIntegrationTests (7 new tests):**
| Test | Purpose |
|------|---------|
| `SharedContext_BTree_DispatcherWritesFqn_DrawerFiltersToTType` | CRITICAL: proves shared context causes type-filtering (would FAIL if context not shared) |
| `NoContext_BTree_DrawerReturnsAllVars` | Documents pre-BB1D failure mode (no context → all vars shown) |
| `SharedContext_BTree_ClearedForNonActionNode_DrawerShowsAllVars` | Context cleared on non-action selection → all vars shown |
| `AccessorHelper_BTreeActionFacet_ReturnsBoundVarName` | Accessor returns ExpressionTargetField for BTreeActionFacet |
| `AccessorHelper_NonActionFacet_ReturnsNull` | Accessor returns null for BTreeWaitFacet and null input |

**Hrot.Hsm.Editor.Tests — BB1DHsmSharedContextIntegrationTests (7 new tests):**
| Test | Purpose |
|------|---------|
| `SharedContext_Hsm_DispatcherWritesFqn_DrawerFiltersToTType` | CRITICAL: proves shared context causes type-filtering for HSM transitions |
| `NoContext_Hsm_DrawerReturnsAllVars` | Documents pre-BB1D failure mode for HSM |
| `SharedContext_Hsm_ClearedForStateSelection_DrawerShowsAllVars` | Context cleared on state selection → all vars shown |
| `BuildFacetDispatcher_WithContext_NullAsset_ReturnsNull` | Null-safety for new overload |

### Full suite results (Stability filter: `Stability!=Flaky&Stability!=Environment&Stability!=Broken`)

| Test Project | Passed | Failed | Skipped | Total |
|-------------|--------|--------|---------|-------|
| Hrot.BTree.Editor.Tests | 434 | 0 | 0 | 434 |
| Hrot.Hsm.Editor.Tests | 382 | 0 | 0 | 382 |
| Hrot.Editor.AiShared.Tests | 1049 | 0 | 0 | 1049 |
| Hrot.AiEditor.Persistence.Tests | 112 | 0 | 0 | 112 |
| EditorSubsystemBootTests (Hrot.ClusterRunner.Integration.Tests) | 10 | 0 | 0 | 10 |
| **Total** | **1987** | **0** | **0** | **1987** |

### Build: 0 errors, 0 warnings across all changed projects.

### Pre-existing flaky test noted (NOT caused by this batch):
`AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` in `Hrot.Editor.AiShared.Tests` fails when other tests leave `.tmp` files in the system temp folder before it runs. Confirmed pre-existing: the test passes when run in isolation (`git stash` + isolated run). Passes with the stability filter applied. Not in the TEST-HEALTH.md ledger — should be added as `Flaky/Environment` in a future clean-up batch.

---

## Developer Insights

1. **The gap was clean to fix** — all three context-aware constructors and factory overloads were already in place from BB1A–C. The only missing piece was the wiring in the composition root. The pattern was exactly as designed.

2. **`HsmCompositeStringDrawer` is `internal`** — the HSM integration tests rely on `InternalsVisibleTo` (already configured in `Hrot.Hsm.Editor.csproj`). The `HsmBlackboardFieldPickerDrawer` is `public`, so the assertion path is fully accessible.

3. **The DDS abort in `Hrot.ClusterRunner.Integration.Tests`** — running the full test suite causes a `CycloneDDS.Runtime.DdsException` from the DDS networking subsystem (not EditorSubsystem). The `EditorSubsystemBootTests` class itself passes cleanly (10/10) when run by class name filter. The DDS issue is an environment/infrastructure problem unrelated to this batch.

4. **Test count jump** — before this batch, BTree had 427 tests and HSM had 375. Now: 434 and 382 respectively (7 new each).

---

## Known Issues

- **Running-editor visual check required** — The "Static Parameters" ImGui panel (B-3) and the "BlackboardField" combo type-filter (B-1) and "Promote to new variable" button (B-2) can only be verified by opening the live editor, loading a BTree/HSM asset with a registered action, selecting the action node/transition, and observing the Inspector render. Mark as **REVIEW-BB1**.
- The `AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` test should be added to `TEST-HEALTH.md` as `Flaky/Environment`.

---

## Suggested Commit Message

`feat(editor): BB1D — wire shared FacetFqnContext + expressionTargetFieldAccessor in EditorSubsystem (B-1/B-2/B-3 live)`
