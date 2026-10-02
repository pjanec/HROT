# AN5 -- Immutable Action Selection — Report

Branch: `blueprint-integ-1`  
Design authority: ACTION-NODE-DESIGN.md §D-B

---

## Goal

Remove the editable Combo from `ChannelCommandNodeDrawer` (the "chameleon" hazard).  
Render `ChannelType`/`ActionId` as **read-only labels**; action selection is now create-time-only
via the AN4 per-action palette.

---

## Drawer change — `ChannelCommandNodeDrawer.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ChannelCommandNodeDrawer.cs`

### What changed

| Before | After |
|--------|-------|
| `Draw()` built a Combo from catalog entries + called `ApplySelection()` on user pick | `Draw()` renders `ImGui.LabelText("Channel", …)` + `ImGui.LabelText("Action", …)` — no mutation |
| `SelectActionForTest(int)` internal hook mutated `ChannelType`/`ActionId` | Removed entirely |
| `ApplySelection(entry)` + `MarkChanged()` private helpers | Removed entirely |
| `IsDirty { get; private set; }` writeable, set to `true` on mutation | `IsDirty => false` — constant, no mutation path |
| Unconfigured node: `(no action selected — param pins hidden)` warning | `ImGui.TextDisabled("(unconfigured — drop from the per-action palette)")` hint |

### `editService` param decision — DROPPED

`_editService` was only used by `MarkChanged()` → `ApplySelection()` → removed.
The `editService` parameter was dropped from both:
- `ChannelCommandNodeDrawer(IChannelCommandCatalog catalog)` — no second param
- `ChannelCommandNodeSession(ChannelCommandNode node, IChannelCommandCatalog catalog)` — no edit service

This is the **cleaner** option (no unused field) and does not ripple: the `BlueprintEditorBootstrap`
registration at line 46 was updated to pass only `channelCatalog`.
The top-level `CreateNodeDrawerRegistry` signature is **unchanged** (still accepts `editService` —
used by WhenNodeDrawer, FunctionCallNodeDrawer, PlayMontageChainNodeDrawer).

---

## Bootstrap change — `BlueprintEditorBootstrap.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`

Line 46 (approximate): registration updated from
```csharp
new ChannelCommandNodeDrawer(channelCatalog, editService)
```
to
```csharp
new ChannelCommandNodeDrawer(channelCatalog)
```
Comment updated to reference AN5/D-B.

---

## Test updates — `ChannelCommandNodeDrawerTests.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/ChannelCommandNodeDrawerTests.cs`

### Removed (mutation hooks, now invalid)
- `CC-03 Session_SelectActionForTest_SetsChannelTypeAndActionId`
- `CC-03 Session_SelectActionForTest_MarksDirty`
- `CC-03 Session_SelectActionForTest_CallsMarkDirtyOnEditService`
- `CC-07 Session_SelectActionForTest_OutOfRange_IsNoOp`
- `SpyEditService` inner class (only used by the above mutation tests)

### Updated
- All `new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService())` calls → `new ChannelCommandNodeDrawer(MakeCatalog())`
- `CC-04 Session_SelectMoveTo_NodePinSchema_ProjectsMoveToParams` → rewritten as `Session_ConfiguredMoveTo_NodePinSchema_ProjectsMoveToParams`: pre-configures the node with `ChannelType`/`ActionId` at construction (simulating the AN4 palette bake), then checks pin projection and confirms session stays clean
- `CC-05 Session_ResetDirty_ClearsDirtyFlag` → rewritten as `Session_ResetDirty_IsNoOp_RemainsClean` (IsDirty is always false)
- `CreateTestDrawerRegistry` helper: `SpyEditService` → `NullEditService`

### New (AN5 read-only contract)
- `CC-03 Session_IsDirty_IsAlwaysFalse` — confirms `IsDirty` is `false` before and after `ResetDirty()`
- `CC-03 Session_HasNoSelectActionForTestMutationHook` — reflection check: `SelectActionForTest` does not exist on the session type

### BF-TA-01 (drawer resolution) — unchanged
`BlueprintDetailsWindowTests.BlueprintDetails_ChannelCommandNode_ResolvesChannelCommandDrawer`
still passes: the drawer resolves a non-null `ChannelCommandNodeSession`; only `Draw()` is now read-only.

---

## Build results

| Project | Result |
|---------|--------|
| `Hrot.Blueprints.Editor` | 0 errors, 0 warnings |
| `Hrot.Blueprints.Tests` | 0 errors, 8 pre-existing warnings |
| `Hrot.Editor.AiShared.Tests` | 0 errors |
| `Hrot.Editor` | 0 errors |

## Test results

| Suite | Result |
|-------|--------|
| `ChannelCommandNodeDrawer*` + `BlueprintDetailsWindow*` (18 tests) | 18/18 passed |
| `Hrot.Blueprints.Tests` full suite | 1539 passed, 4 failed (pre-existing only), 8 skipped |
| `Hrot.Editor.AiShared.Tests` | 832/832 passed |

### Pre-existing failures (unchanged set)
- `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
- `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
- `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` (CRLF flake)
- `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` (CRLF flake)

**0 new failures.**

---

## Deviations

None. The task spec's "keep it minimal" choice was honored: `editService` dropped from the drawer and session
(no unused field), bootstrap registration updated (1 line), top-level `CreateNodeDrawerRegistry` signature
unchanged (no ripple to callers). Visual read-only render is REVIEW-V1 gate as specified.
