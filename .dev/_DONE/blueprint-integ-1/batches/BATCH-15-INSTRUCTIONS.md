# BATCH-15: SubElementCollision detector + dangling-reference classification (FINAL — completes Phase 5)
**Tasks:** AIE-053   **Phase:** 5   **Est:** ~7h
**Dependencies:** AIE-051 (ReferenceCatalog/RefactorService wired — BATCH-14).
This is the LAST task. It is **partly net-new** (the collision detector and the classification do not exist yet), not pure wiring.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-053; `.dev/blueprint-integ-1/design-talk.md` lines ~1533–1680 (Steps 7.1 SubElementCollision + 7.2 Dangling Reference Classification — **contains the exact code skeleton; follow it**).
3. `.dev/blueprint-integ-1/reviews/BATCH-14-REVIEW.md` (note DEBT-011: Blueprint contributor is header-only).

Use **codebase-memory MCP** first; not `search_code`. **Do NOT change CycloneDDS versions** (GizmoMap.Contracts stays 0.2.2); do not touch Hrot.IG/DDS. Headless tests must not call ImGui without a context.

## Ground truth (verified — build on these)
- **`IActionSchemaExporter`** (`Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`): `IReadOnlyDictionary<string, ActionSchemaEntry> All { get; }`; `ActionSchemaEntry.Fqn` is the FQN string. The composition root already constructs `new ActionSchemaExporter()` (BATCH-14, for the aggregator) — **reuse that same instance**.
- **`InspectorWindow`** (`Hrot.Editor.AiShared/Windows/InspectorWindow.cs`): current ctor is `(EditorSelectionStore store, IRefactorService refactorService, FindResultsWindow findResults, Func<Guid,IBlackboardManagedAsset?>? subAssetResolver=null, string? idOverride=null, string? owningPerspective=null, IFacetDispatcher? facetDispatcher=null)`. It is **constructed inside `PerspectiveWorkspaceRegistrar`** (not directly in EditorSubsystem) — so thread the exporter through the registrar exactly like the aggregator was threaded in BATCH-14.
- **`RefactorService.PreviewDelete`/`ApplyDelete`** (`Hrot.Editor.AiShared/Refactor/RefactorService.cs:137`/`:162`): `PreviewDelete` collects `danglingRefs` from `_referenceCatalog.FindReferences(element.Key)` and returns `DeletePreview(assetId, danglingRefs, issues)`. `ApplyDelete` currently only refuses on Error-severity issues. `DeleteOptions(bool AllowDanglingReferences=false)` already exists.
- **`DeletePreview`** (`Refactor/IRefactorService.cs:38`): `record (Guid AssetId, IReadOnlyList<AssetReferenceInfo> DanglingReferences, IReadOnlyList<RefactorIssue> Issues)`. **Only one construction site** (`PreviewDelete`) — extend it backward-compatibly.
- **`SubElementKind`** (`References/SubElementKind.cs`): `ActionFqn, ConditionFqn, GuardFqn, EventName, AssetReference, BlackboardField, BlackboardVariable, UtilityInput`. `AssetReferenceInfo` carries `TargetKind` (a `SubElementKind`) + `HostKind` (an `AssetKind`).

## Tasks (in order)

### Task 1: SubElementCollisionDetector + Inspector diagnostic strip (AIE-053 part 1)
**New file** `Hrot/Editor/Hrot.Editor.AiShared/Validation/SubElementCollisionDetector.cs` — implement exactly per the design-talk skeleton: `public sealed record ActionCollision(string ShortName, IReadOnlyList<string> ClaimingFqns)` + `public static IReadOnlyList<ActionCollision> GetCollisions(IActionSchemaExporter)` grouping `schemaExporter.All.Values` by short name (last `.`-segment of `Fqn`), keeping groups with >1 distinct FQN, claimants sorted.
**Wire into `InspectorWindow`:** add an **optional** `IActionSchemaExporter? schemaExporter = null` ctor param (keep optional so existing call sites/tests still compile); add `DrawCollisionDiagnosticStrip()` rendering the red strip at the top of `DrawClientArea()` **only when** the exporter is non-null and collisions exist. Thread the exporter through `PerspectiveWorkspaceRegistrar` (new optional param, forwarded to the `InspectorWindow` ctor) and pass the shared `ActionSchemaExporter` from `EditorSubsystem` to all three registrars. No "auto-fix" button (per spec — user fixes in IDE; strip vanishes on next reflect).
**Tests (`Hrot.Editor.AiShared.Tests`):** `CollisionDetector_FlagsDuplicateShortNames` (two entries `A.Ns1.DoThing` + `B.Ns2.DoThing` → one `ActionCollision("DoThing", [both FQNs sorted])`); `CollisionDetector_NoCollision_WhenShortNamesUnique` (empty); `CollisionDetector_SameFqnTwice_NotACollision` if applicable. Use a fake `IActionSchemaExporter`. Headless — do NOT instantiate ImGui; test the detector (and, if you expose a headless accessor on InspectorWindow, that too) directly.

### Task 2: Dangling-reference classification + ApplyDelete refusal (AIE-053 part 2)
Classify each dangling reference in `PreviewDelete` as **Critical** (removal breaks compilation — e.g. an exported-type/typed-field usage) vs **Auto-resolvable** (soft/name-based — e.g. a BTree subtree call or peer-call that the runtime tolerates by failing the node). Add a `ReferenceCriticality` enum and surface the classification on `DeletePreview` **backward-compatibly** (e.g. keep `DanglingReferences`, add a parallel `IReadOnlyList<ClassifiedDanglingReference>` or a `CriticalReferences` list; update the single `PreviewDelete` construction site + any callers/tests you find). Define a defensible `SubElementKind`→criticality mapping (justify it in the report; e.g. typed `AssetReference` exported-type usage → Critical; subtree/peer/name refs → Auto-resolvable). In `ApplyDelete`: **if any Critical reference exists and `options.AllowDanglingReferences` is false, refuse** with a clear `RefactorResult(false, …, "<reason>")` — do not delete the file.
**Tests:** `PreviewDelete_ClassifiesCriticalVsAutoResolvable` (construct refs of each kind via the catalog; assert the split); `ApplyDelete_RefusesCritical_WhenDisallowed` (Critical present + `AllowDanglingReferences:false` → `Success==false`, file NOT deleted, reason mentions critical); `ApplyDelete_AllowsWhenAccepted` (`AllowDanglingReferences:true` → proceeds); `PreviewDelete_AutoResolvableOnly_DoesNotBlock`. Assert real classification + real refusal/file-state, not non-null. Existing `RefactorServiceTests`/`RefactorEndToEndTests` must still pass.

## Success Criteria
- [ ] AIE-053 per success conditions; **Phase 5 complete → entire integration done.**
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 warnings (GizmoMap.Contracts on 0.2.2).
- [ ] Green: `Hrot.Editor.AiShared.Tests`, `Hrot.Blueprints.Tests` (no new failures beyond DEBT-006's 10), `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter.
- [ ] No leftover TODO/debug; docs.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-15-REPORT.md`.

## Execution rules
- Tasks in sequence (detector+strip → classification). Run the suites yourself; fix root causes; never fake a pass; assert real values (collision short-name + sorted claimants, the critical/auto split, the refusal + file-state), not non-null.
- Keep `InspectorWindow`/`PerspectiveWorkspaceRegistrar`/`DeletePreview` changes **additive and backward-compatible** — don't break existing call sites or tests. Follow the design-talk skeleton for the detector + strip.
- Reuse the existing `ActionSchemaExporter` instance and `ReferenceCatalog`/`RefactorService` — don't reimplement.

## Report Requirements
In `reports/BATCH-15-REPORT.md`: the `SubElementKind`→criticality mapping you chose + justification; how the exporter is threaded to the Inspector; how you extended `DeletePreview` backward-compatibly; actual test counts; full-solution build 0 errors/0 warnings + no new Blueprints failures; whether DEBT-011 (Blueprint per-node refs) is touched or remains open; suggested commit message. No comprehension questions.
