# BATCH-17-CONTINUATION: Phase 5 Epilogue (ANC-P5-08c-08d PlayMontageChainNode Custom Drawer - Validation & Wiring)

**Batch Number:** BATCH-17-CONTINUATION  
**Tasks:** ANC-P5-08c, ANC-P5-08d  
**Phase:** Phase 5 - Blueprint authoring primitives (epilogue - editor drawer validation & integration)  
**Estimated Effort:** 6-8 hours  
**Priority:** MEDIUM  
**Dependencies:** BATCH-17 approved (ANC-P5-08a-08b core drawer + UI); BATCH-10 approved (AiPrimitive infrastructure); BATCH-06 approved (validators ANIM001-007)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes the `PlayMontageChainNode` custom editor drawer by adding live in-drawer validation feedback (ANIM005 same-slot enforcement, ANIM012 length ≤8 warnings) and wiring tests to confirm drawer registry integration and asset round-trip serialization.

**MANDATORY WORKFLOW:** Complete tasks 08c → 08d in sequence with passing tests between each:

1. **ANC-P5-08c:** Implement → Write tests → **ALL tests pass** ✅
2. **ANC-P5-08d:** Register drawer in bootstrap → Add wiring tests → **ALL tests pass** ✅

**DO NOT** move to 08d until 08c tests pass. Do not stop and ask for permission to run tests, fix compilation errors, or complete obvious plumbing. Finish implementation and report when both tasks are complete.

### Required Reading (IN ORDER)

1. **BATCH-17 Deliverables** — Review the approved core implementation:
   - `.dev/anim-ctrl/reports/BATCH-17-REPORT.md` (implementation summary)
   - `.dev/anim-ctrl/reviews/BATCH-17-REVIEW.md` (approval findings)
2. **Task Detail:** `.dev/anim-ctrl/TASK-DETAIL.md` — Addendum A section (ANC-P5-08c, 08d)
3. **Design Doc 5:** `.dev/anim-ctrl/DD-5_BlueprintPrimitives_v1_1.md` — §14.5, §3.3 (same-slot requirement), §10 (validators ANIM005/ANIM012)
4. **Design Doc 4:** `.dev/anim-ctrl/DD-4_TKB_AnimationDescriptor_v1_2.md` — §6 (ANIM005 definition), §5 (query API)
5. **Validator Reference:**
   - `.dev/anim-ctrl/reviews/BATCH-06-REVIEW.md` (validator architecture confirmation)
   - `.dev/anim-ctrl/reports/BATCH-08-REPORT.md` (ANIM008-011 / 012 implementation notes)
6. **Exemplar Wiring Tests:**
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/WhenNodeDrawerTests.cs` (exemplar: `DrawerRegistry_Contains_WhenNodeDrawer`)
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` (registration pattern)

### Source Code Locations

**Primary work area (continuation of BATCH-17):**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeSession.cs` — Extend with validation feedback in `Draw()`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs` — Add 4-6 new validation + wiring tests

**Registration update:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` — Extend `CreateNodeDrawerRegistry` signature, add conditional drawer registration

**Node definition (read-only for context):**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationActionNodes.cs` — `PlayMontageChainNode` struct

**Query API (read-only for context):**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Queries/IAnimationTkbQueries.cs` — `GetMontage(class, id)` / `GetPlayableMontages(class)`

### Report Submission

Write report to:
`.dev/anim-ctrl/reports/BATCH-17-CONTINUATION-REPORT.md`

If blocked by architectural contradiction, document in:
`.dev/anim-ctrl/questions/BATCH-17-CONTINUATION-QUESTIONS.md`

---

## Context

**Why now?** BATCH-17 successfully delivered the core drawer skeleton and dynamic chain UI (08a-08b). This continuation adds two finishing touches: (1) live validation feedback so designers see ANIM005/ANIM012 violations while editing, and (2) wiring tests that confirm the drawer integrates with the registry and survives asset serialization round-trips.

**Scope:** Continuation of Phase 5 epilogue. After 08c-08d, the `PlayMontageChainNode` drawer is feature-complete: full ergonomics, live validation, and wiring verified.

**Related Tasks:**
- [ANC-P5-08a](../TASK-DETAIL.md#anc-p5-08---playmontagechainnode-custom-drawer-editor) (08a) - Drawer + session skeleton (BATCH-17, complete)
- [ANC-P5-08b](../TASK-DETAIL.md#anc-p5-08---playmontagechainnode-custom-drawer-editor) (08b) - Dynamic chain UI + ChainCount (BATCH-17, complete)
- [ANC-P5-08c](../TASK-DETAIL.md#addendum-a--anc-p5-08-implementation-plan-playmontagechainnode-custom-drawer) (08c) - Validation feedback (this batch)
- [ANC-P5-08d](../TASK-DETAIL.md#addendum-a--anc-p5-08-implementation-plan-playmontagechainnode-custom-drawer) (08d) - Wiring tests (this batch)
- DD-5 §14.5 (overall drawer spec)
- DD-4 §6 (ANIM005 same-slot rule), DD-5 §10 (ANIM012 length rule)

---

## 📐 Task Specification

### ANC-P5-08c — In-drawer Validation Feedback (ANIM005 / ANIM012)

**Refs:** DD-4 §6 (ANIM005), DD-5 §3.3/§10 (ANIM012), BATCH-06 (validator architecture).

Extend `PlayMontageChainNodeSession.Draw()` to surface live validation checks for the two key rules designers need to know about when building chains:

#### ANIM005 Enforcement: Same-Slot Requirement

**Rule:** All entries in a montage chain must reference montages in the same animation slot. (DD-4 §6 ANIM005 definition: chain entries cannot span multiple slots.)

**Implementation:**
- When rendering the chain UI, resolve each entry's `MontageId` to its montage metadata using `GetMontage(currentClass, montageId)`
- Extract the `.Slot` from the montage
- Compare all live entries' slots; if any differ, flag a VIOLATION
- Display error message: `"❌ ANIM005 Violation: Chain entries must use the same animation slot. Found slots: [Slot1, Slot2, ...]"`
- **Non-blocking:** Designer sees the error but can continue editing (compile will enforce at save time)

#### ANIM012 Enforcement: Length ≤ 8

**Rule:** Montage chain entries cannot exceed 8 (the array bound). (DD-5 §10 ANIM012 definition: `Count ≤ 8`.)

**Implementation:**
- When rendering, check if `ChainCount > 8` (edge case for loaded assets over-length)
- If so, display warning: `"⚠️ ANIM012 Warning: Chain length ({Count}) exceeds maximum of 8. Loaded asset may have been edited externally."`
- Add button to auto-truncate: "Truncate to 8" (calls `RemoveChainEntry` until `ChainCount == 8`, sets IsDirty)
- **Non-blocking:** Same as ANIM005

#### Test Coverage for 08c

**Unit tests** (headless, no ImGui):
- `ValidationFeedback_ANIM005_MultipleSlotViolation_IsReported` — Load a chain with entries in different slots, verify session reports the violation message
- `ValidationFeedback_ANIM012_OverLength_IsReported` — Manually construct session state with `ChainCount > 8`, verify warning message
- `ValidationFeedback_Truncate_Button_RemovesToMaxCapacity` — Call the truncate helper, verify `ChainCount` becomes 8 after `IsDirty` set
- `ValidationFeedback_NoViolation_WhenAllSame_NoErrorDisplayed` — Load valid chain (all same slot, ≤8), verify no violation messages

**Success Criteria:**
- ✅ ANIM005 violation detected and reported by session (not yet visual rendering, just the message logic)
- ✅ ANIM012 over-length detected and reported
- ✅ 4 unit tests all passing, covering valid + invalid scenarios
- ✅ Build clean, no new warnings
- ✅ Existing BATCH-17 tests still pass (no regression)

---

### ANC-P5-08d — Wiring Tests + Registration Update

**Refs:** DD-5 §14.5, exemplar `WhenNodeDrawerTests`, `BlueprintEditorBootstrap`.

Complete the editor integration: (1) extend bootstrap to register the drawer with optional query dependency, (2) add wiring test that confirms the registry resolves the drawer and it has a real caller.

#### Registration: `BlueprintEditorBootstrap.CreateNodeDrawerRegistry`

**Current pattern** (from exemplar):
```csharp
public static BlueprintNodeDrawerRegistry CreateNodeDrawerRegistry(
    IEngineEventCatalog catalog,
    IPredicateCompiler predicateCompiler,
    IEditService editService)
{
    var registry = new BlueprintNodeDrawerRegistry();
    registry.Register(typeof(WhenNode), new WhenNodeDrawer(catalog, predicateCompiler, editService));
    return registry;
}
```

**Update required:**
- Extend the signature to accept `IAnimationTkbQueries? animationQueries` (optional, defaults to null)
- Add conditional registration:
  ```csharp
  if (animationQueries != null)
  {
      registry.Register(typeof(AiPrimitiveNode), new PlayMontageChainNodeDrawer(animationQueries, editService, ...));
  }
  ```
  *(Determine exact registration key — either `AiPrimitiveNode` type or a dispatch object; confirm against route A dispatch decision from BATCH-17-REPORT.)*
- Graceful degrade if queries unavailable (drawer simply not registered; editor still boots without animation-specific UI)

#### Wiring Test: DrawerRegistry Integration

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs` (extend existing test class)

**Tests to add:**

1. `DrawerRegistry_Contains_PlayMontageChainNodeDrawer`
   - Create a full `BlueprintNodeDrawerRegistry` via `CreateNodeDrawerRegistry(..., animationQueries: nonNull, ...)`
   - Verify registry has an entry for the chain node
   - Assert it resolves to a `PlayMontageChainNodeDrawer` instance

2. `DrawerRegistry_WithoutQueries_NoPlayMontageChainNodeDrawer`
   - Call `CreateNodeDrawerRegistry(..., animationQueries: null, ...)`
   - Assert that chain node is NOT in the registry (graceful degrade)
   - Editor still boots, no error

3. `NodeDrawerRegistry_AllThreeDrawers_HaveProductionCaller`
   - Mirror the exemplar wiring test from `WhenNodeDrawerTests`
   - Verify that `PlayMontageChainNodeDrawer`, `WhenNodeDrawer`, and any other drawer in the registry have callers in production code
   - This is a compile-time check: search for `CreateNodeDrawerRegistry` calls in the codebase and verify they match the registry contents

4. `AssetRoundTrip_DrawerOpen_NoCorruption`
   - Create a test asset with a `PlayMontageChainNode` that has 3 entries
   - Serialize to JSON
   - Deserialize back
   - Open the drawer (create session)
   - Verify chain state matches original (all 3 entries intact, `ChainCount == 3`)
   - Close drawer, serialize again
   - Assert JSON before/after is identical (no spurious mutations from drawer interaction)

**Success Criteria:**
- ✅ Registry integration test passes (drawer is resolvable)
- ✅ Graceful degrade test passes (null queries → no registration, no error)
- ✅ Production caller test passes (drawer registration is reachable)
- ✅ Asset round-trip test passes (no mutation after open/close/serialize)
- ✅ 4 wiring tests all green
- ✅ Full editor test suite green (no regression)

---

## ✅ Quality Standards

**Test Quality (BATCH-17 baseline maintained):**
- Validation feedback tests verify actual error messages, not just object existence
- Wiring tests check registry resolution and production reachability (not smoke tests)
- Round-trip test confirms JSON stability (serialization correctness)
- All tests are independent and can run in any order

**Code Quality:**
- No unused fields or ambiguous comments
- Dispatch keying decision from BATCH-17 confirmed and preserved
- Graceful degrade when queries unavailable (no hard error)
- Storage-agnostic write-back pattern maintained (Pattern A/B from BATCH-17)

**Build & Regression:**
- Solution builds clean (0 new errors)
- BATCH-17 tests still pass (no breakage)
- Existing validator tests unaffected
- No warnings introduced

---

## 📝 Completion Checklist (For Developer)

Before submitting report, verify:

- [ ] ANC-P5-08c implemented: Validation feedback for ANIM005 (same-slot) and ANIM012 (length ≤8)
- [ ] ANC-P5-08c tests: 4 unit tests, all passing
- [ ] ANC-P5-08d implemented: Drawer registered in `CreateNodeDrawerRegistry` with optional queries
- [ ] ANC-P5-08d tests: 4 wiring tests, all passing
- [ ] Full suite: 8 new tests, no regressions
- [ ] Build: `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4` → 0 new errors
- [ ] BATCH-17 tests: `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs` → all green
- [ ] Git ready: 2 commits planned (08c complete, 08d complete) or 1 combined if appropriate
- [ ] Report written: `.dev/anim-ctrl/reports/BATCH-17-CONTINUATION-REPORT.md` with findings + test results

---

## 🔗 Reference Links

- **Task Detail:** [Addendum A — ANC-P5-08 Implementation Plan](../TASK-DETAIL.md#addendum-a--anc-p5-08-implementation-plan-playmontagechainnode-custom-drawer)
- **DD-5:** [DD-5_BlueprintPrimitives_v1_1.md](../DD-5_BlueprintPrimitives_v1_1.md) — §14.5, §3.3, §10
- **DD-4:** [DD-4_TKB_AnimationDescriptor_v1_2.md](../DD-4_TKB_AnimationDescriptor_v1_2.md) — §6 (ANIM005)
- **Exemplar Drawer:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/WhenNodeDrawer.cs`
- **Exemplar Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/WhenNodeDrawerTests.cs`
- **Bootstrap:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`

---

**Ready to begin? Start with ANC-P5-08c validation feedback — read DD-5 §14.5 and DD-4 §6 first, then implement the feedback logic in the session's `Draw()` method.**
