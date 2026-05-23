# BATCH-05 Report — NodeEditor: NodeAttachments Model Foundation

**Status:** APPROVED — all tasks complete, builds clean, tests pass.

---

## Build Results

### NodeEditor.sln
```
Build succeeded in 2.6s
  NodeEditor.Primitives  net8.0  succeeded
  NodeEditor.Core        net8.0  succeeded
  NodeEditor.UI          net8.0  succeeded
  NodeEditor.Core.Tests  net8.0  succeeded
  NodeEditor.Demo        net8.0  succeeded
  NodeEditor.UI.Tests    net8.0  succeeded
0 Error(s), 0 Warning(s)
```

### IOS-IG-SimHost.sln
```
Build succeeded.
0 Warning(s)
0 Error(s)
```

---

## Test Results

**Total tests: 72 / Passed: 72 / Failed: 0**

Baseline before BATCH-05: 63 tests (inferred from 72 - 9 new).
New tests added: 9 (4 in AttachmentIdTests + 5 in AttachmentCommandsTests).

All 72 tests passed in 0.86 seconds.

---

## Files Created

| File | Description |
|------|-------------|
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/AttachmentId.cs` | New — TASK-NEA-01: AttachmentId struct |
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentModel.cs` | New — TASK-NEA-01: IAttachmentModel interface with AttachmentCategory and AttachmentState enums |
| `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Primitives/AttachmentIdTests.cs` | New — 4 tests for AttachmentId |
| `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Commands/AttachmentCommandsTests.cs` | New — 5 tests for attachment command records |

## Files Modified

| File | Change |
|------|--------|
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs` | TASK-NEA-02/03: added 3 default attachment members to IGraphModel; added AttachmentsAdded/AttachmentsRemoved/AttachmentsModified to GraphChangeKind; added AffectedAttachments to GraphChangeNotification |
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Commands/GraphCommand.cs` | TASK-NEA-08: added `using NodeEditor.Core.Interfaces;`; added 5 attachment command records before Batch |
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs` | Fixed one breaking call site: 4-arg GraphChangeNotification -> 5-arg |

---

## Deviations from Spec

None. All changes match the batch instructions exactly.

---

## Developer Insights

**1. How many existing implementations of IGraphModel exist across the codebase (src + tests)?**

Two:
- `FakeGraphModel` in `src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs`
- `StubModel` (private nested class) in `tests/NodeEditor.Core.Tests/View/GraphViewTests.cs`

**2. Were the default interface implementations necessary for any test stubs, or were they all fine with explicit overrides?**

Yes, the default implementations were necessary for both existing implementations. Neither `StubModel` nor `FakeGraphModel` implements the three new attachment members (`Attachments`, `FindAttachment`, `GetAttachmentsForNode`). Without defaults, both would have failed to compile. The defaults return empty collections/null, which is the correct behavior for implementations that do not yet support attachments.

**3. Did any test file other than FakeGraphModel.cs construct a GraphChangeNotification positionally that needed fixing?**

No. `FakeGraphModel.cs` is the only file in the entire codebase that constructs a `GraphChangeNotification` using positional arguments. The `StubModel` in tests only declares the `Changed` event (add/remove stubs) and never constructs the notification record.

**4. How many total attachment-related tests were written?**

9 total:
- 4 in `AttachmentIdTests.cs` (Empty_IsDefault, NewId_GeneratesUniqueId, Equality_SameGuid_Equal, Equality_DifferentGuid_NotEqual)
- 5 in `AttachmentCommandsTests.cs` (AddAttachment_Roundtrip, RemoveAttachments_Roundtrip, SetAttachmentProperty_Roundtrip, ReorderAttachments_Roundtrip, MoveAttachment_Roundtrip)

**5. Is there a NodeEditor.sln-level build target that builds all 4 projects together?**

Yes. `NodeEditor.sln` builds all 6 projects in one pass: the 4 production projects (`NodeEditor.Primitives`, `NodeEditor.Core`, `NodeEditor.UI`, `NodeEditor.Demo`) plus both test projects (`NodeEditor.Core.Tests`, `NodeEditor.UI.Tests`). The solution file serves as the single top-level build entry point for all NodeEditor code.
