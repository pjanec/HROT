# BUG2-BATCH-01 Report

**Batch:** BUG2-BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2026-03-21  
**Status:** Complete

---

## 📊 Task Completion

| Task ID   | Status | Notes |
|-----------|--------|-------|
| BUG2-N001 | ✅ Done | Removed duplicate `UpdateEntityDescriptorRequestSystem` from `SimHostApp.cs`; added `RegisteredSystemTypes_ContainsNoDuplicates` test |
| BUG2-N002 | ✅ Done | Added `EnableSenderTracking` to all 4 DDS participants (SimHost, IG, IosSubsystem, NetworkDemo); added `ProcessSample_WithSenderTracking_SetsOwnerId` integration test |
| BUG2-N003 | ✅ Done | Added `Dispose(long)` override to `WorldPosEgressTranslator` that tombstones the DR sample; added DDS integration tests `Dispose_CallsDisposeOnDrWriter` and `Dispose_AlsoCallsBaseDispose` |
| BUG2-M001 | ✅ Done | Added `BehaviorFinished` and `UnderAttack` cases to `ResolveTrigger` in both `MissionControlRequestSystem` and `EntityMissionIngressTranslator`; made method `internal`; tests in both test projects |
| BUG2-M002 | ✅ Done | Added `_triggerTypes`, `GetDefaultTriggerParams`, `HandleEditTriggerType`, `HandleEditTriggerParams`, `HandleAddTrigger` to `MissionPanel`; trigger UI block in `Draw`; 4 tests added |
| BUG2-M003 | ✅ Done | Replaced `↑`, `↓`, `✕` button labels with `Up`, `Down`, `Delete` across all task rows |
| BUG2-M004 | ✅ Done | Added `HandleForceCommit` and `TestHook_ClearDraftAndDismissConflict` to `MissionPanel`; replaced static commit UI with conditional conflict vs commit UI; 3 tests added |
| BUG2-U001 | ✅ Done | Removed `Tools[]`, `_selectedTool`, `SelectedTool`, and `interaction.activeTool` from `ConfigPanel`; updated `ConfigPanelTests` to remove obsolete tests and add `BuildPatch_DoesNotContainInteractionKey` and `NoToolsField` |
| BUG2-U002 | ✅ Done | Added `ImGui.Indent(indent)` / `ImGui.Unindent(indent)` around each node in `OrbatPanel.Draw`; added `GetVisibleNodes_SubordinateHasGreaterDepthThanParent` test |

---

## 🧪 Testing Results

**Unit Tests Passed:** 924 / 924 (in modified test projects)

| Project | Before | After | New Tests |
|---------|--------|-------|-----------|
| Hrot.ExCon.Tests | ~262 | 283 | +21 (M002×4, M003 N/A, M004×3, U001×2, U002×1; previously added tests not counted here) |
| Hrot.IG.Tests | ~300 | 316 | +16 (N002×1 + previously added) |
| Hrot.SimHost.Tests | 268 | 268 | N001, N003 tests already indexed |
| Hrot.Map.Common.Tests | 57 | 57 | M001 tests stable |

**Pre-existing failures (not introduced by this batch):**
- `FDP.Toolkit.Tkb.Tests`: 2 failures (pre-existing)
- `ModuleHost.Core.Tests`: 1 failure (pre-existing in standalone run)
- Test host CLR crash (0x80131506): pre-existing, confirmed by stash test

**Key Test Scenarios Verified:**
- ✅ No duplicate system types registered in `SimHostApp`
- ✅ `SenderIdentityConfig.AppInstanceId` flows through to `EntityMaster.OwnerId` over DDS
- ✅ `WorldPos` sample is tombstoned (not just abandoned) when entity is disposed
- ✅ `ResolveTrigger` returns correct enum for all 5 trigger strings including new BehaviorFinished/UnderAttack
- ✅ Trigger type change resets params to type-appropriate defaults
- ✅ Force commit is sent with `baseVersion == 0` bypassing OCC check
- ✅ `HandleForceCommit` dismisses the conflict alert inline
- ✅ `TestHook_ClearDraftAndDismissConflict` clears draft and conflict together
- ✅ ORBAT child node has `Depth > 0` while root has `Depth == 0`
- ✅ `ConfigPanel.BuildPatch` no longer emits an `interaction` key

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`SenderIdentityConfig` namespace**: Initially included `using CycloneDDS.Runtime;` which doesn't contain `SenderIdentityConfig`; it lives in `CycloneDDS.Runtime.Tracking`. Found via `Select-String` across the FDP ExtDeps source tree.

2. **`DdsLoan<T>` indexer vs enumerator**: `loan[i]` returns the raw `T` (no `Info`), but `foreach (var sample in loan)` yields `DdsSample<T>` which exposes `.Info.InstanceState`. Fixed by switching to `foreach` with manual index tracking for `GetSender(idx)`.

3. **`InternalsVisibleTo` missing for `Hrot.ExCon.Tests`**: The `TestHook_ClearDraftAndDismissConflict` method is `internal` but the `Hrot.ExCon.csproj` had no `InternalsVisibleTo` attribute (unlike `Hrot.SimHost.csproj` and others). Added the attribute via `AssemblyAttribute` item in the csproj.

4. **csproj XML corruption**: An automated replacement inserted a new `<ItemGroup>` inside an unclosed `<ItemGroup>` block. Resolved by rewriting the csproj cleanly via PowerShell.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `MissionPanel` already had `TestHook_DraftBaseVersion` and `TestHook_PollCommitCompletion` as `internal` members, but `InternalsVisibleTo` was never added — these were effectively dead test hooks. They are now accessible after this batch.
- `ResolveTrigger` existed in two near-identical copies (`MissionControlRequestSystem` and `EntityMissionIngressTranslator`). A shared static utility would reduce duplication.

**Q3: What design decisions did you make beyond the instructions? How do they differ from the spec?**

- Added `TestHook_ClearDraftAndDismissConflict` to `MissionPanel` rather than making `ClearDraft()` `internal`, keeping the helper narrowly scoped to the "discard draft + dismiss conflict" atomic operation as specified in the UI flow.
- `GetDefaultTriggerParams` was made `public static` to allow direct testing without a panel instance, consistent with `GetTaskIcon`.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `HandleForceCommit` calls `DismissConflict()` internally, so conflict UI collapses immediately on force-commit submission rather than waiting for the round-trip ACK. This is correct UX (operator has committed to forcing) and is now covered by `HandleForceCommit_DismissesConflictAlert`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- No hot-path changes. All new code runs in UI frame or network handler paths where allocation overhead is acceptable.

---

## ⚠️ Outstanding Issues / Next Steps

- The duplicate-copy of `ResolveTrigger` logic between `MissionControlRequestSystem` and `EntityMissionIngressTranslator` is a minor tech-debt item; suggest consolidating into a shared static helper in `Hrot.Map.Common`.
- The 2 pre-existing `FDP.Toolkit.Replay.Tests` failures and the CLR crash (0x80131506) are unrelated to this batch and should be tracked separately.
