# BATCH-06 Report -- NEA-04/07/09/11(theme)

## Build Results

### NodeEditor.sln
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### IOS-IG-SimHost.sln
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Results

```
Passed!  - Failed: 0, Passed: 82, Skipped: 0, Total: 82
```

- Existing tests: 72
- New tests this batch: 10
- Total: 82

## Files Created

1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentLayout.cs`
   - `AttachmentPlacement` readonly record struct
   - `AttachmentLayout` sealed class with `Placements`, `TotalHeightAboveHost`, and static `Empty`

2. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Spatial/AttachmentLayoutEngine.cs`
   - `AttachmentLayoutEngine` static class with `Compute()` and 6 public constants
   - Wrap-and-stack layout algorithm, pure math, no rendering dependency

3. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentContextMenuProvider.cs`
   - `ContextMenuItem` sealed record
   - `IAttachmentContextMenuProvider` interface

4. `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Spatial/AttachmentLayoutTests.cs`
   - 6 tests covering layout engine

5. `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/AttachmentSelectionTests.cs`
   - 4 tests covering selection of attachments

## Files Modified

1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionEntry.cs`
   - Added `AttachmentId Attachment { get; }` property
   - Added `a` parameter to private constructor
   - Updated all 4 existing factory methods to pass `AttachmentId.Empty`
   - Added `OfAttachment(AttachmentId id)` factory method
   - Added `Attachment` to `SelectionEntryKind` enum

2. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionState.cs`
   - Added `Attachments` computed property

3. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IDetailsViewProvider.cs`
   - Added `SingleAttachment(AttachmentId Id)` case to `DetailsTarget`
   - Added `MultipleAttachments(IReadOnlyList<AttachmentId> Ids)` case to `DetailsTarget`

4. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs`
   - Added `IAttachmentContextMenuProvider? AttachmentContextMenu => null;` default interface member

5. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorTheme.cs`
   - Added 4 attachment pill color default interface members (Decorator, Flag, Pure, Custom)
   - Added 4 attachment geometry default interface members (Height, CornerRadius, GapAboveHost, InterGap)

6. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/DefaultTheme.cs`
   - Added 8 explicit `{ get; init; }` properties matching interface defaults

7. `.dev/blueprints-2/TASK-TRACKER.md`
   - Marked TASK-NEA-04, NEA-07, NEA-09, NEA-11 as `[x]`

## Deviations from Spec

None. All items implemented exactly as specified.

One build fix applied: `Action` was ambiguous between `System.Action` and the
`NodeEditor.Core.Action` namespace. Used `System.Action` qualified name in
`ContextMenuItem` record. The instructions noted "No extra using needed when
implicit usings are enabled" but did not account for the same-assembly namespace
conflict; using the fully qualified `System.Action` is equivalent and correct.

## Developer Insights

### 1. Did adding `AttachmentId` to `SelectionEntry`'s private constructor break any existing test?

No. The existing factory methods (`OfNode`, `OfLink`, `OfComment`, `OfReroute`) were all
updated to pass `AttachmentId.Empty` as the new argument. Since the `readonly record struct`
equality is auto-generated from all properties, and all existing test entries that did not
set an attachment will have `Attachment == AttachmentId.Empty`, they remain equal to each
other. Zero existing tests broke.

### 2. Did `IEditorTheme` default interface members compile without issue on all C# 8+ targets?

Yes. The project targets `net8.0` which fully supports default interface members (a C# 8
feature). No compilation issues; all 8 new properties in `IEditorTheme` compiled cleanly.

### 3. How many rows does the layout engine produce for 5 attachments of width 30 each on a host of width 100?

Each pill width = 30 + 6*2 = 42 px.
Row attempt: 42, then 42+4+42=88 (fits), then 88+4+42=134 > 100 (wrap).
- Row 0 (bottom): attachments 0 and 1 (total = 88 px)
- Row 1: attachments 2 and 3 (total = 88 px)
- Row 2 (top): attachment 4 (total = 42 px)

Answer: **3 rows**.
TotalHeightAboveHost = 6 + 3*20 + 2*3 = 6 + 60 + 6 = 72 px.

### 4. Does `DefaultTheme` need explicit overrides of the interface defaults, or do the defaults suffice for `DefaultTheme`'s contract?

The interface defaults suffice for any type that simply implements `IEditorTheme` without
overriding them. However, `DefaultTheme` is a `sealed class` used directly by host code as
a configurable object (with `{ get; init; }` properties). Without explicit overrides the
properties would be interface-level default methods and NOT settable via `init`, making
`new DefaultTheme { AttachmentHeight = 22f }` impossible. The explicit `{ get; init; }`
overrides are therefore required to preserve `DefaultTheme`'s "configurable via init"
contract.

### 5. Total new test count for this batch

**10 new tests** (6 layout engine + 4 selection).
