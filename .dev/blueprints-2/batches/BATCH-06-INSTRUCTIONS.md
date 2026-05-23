# BATCH-06 — NodeEditor: NEA-04/07/09/11(theme)

## Tasks Covered
- TASK-NEA-04: Attachment layout engine (wrap-and-stack)
- TASK-NEA-07: Selection of attachments
- TASK-NEA-09: Attachment context-menu provider interface
- TASK-NEA-11 (theme part only): Theme additions for attachments

## Spec References
Read these files BEFORE writing any code:
1. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\NodeEditor_Extension_NodeAttachments.md`
   - Section 5.1 (layout dimensions)
   - Section 7 (selection semantics)
   - Section 6.4 (context menu interface)
   - Section 10 (theme additions)
2. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\TASK-DETAIL.md` (Phase 2, tasks NEA-04, NEA-07, NEA-09, NEA-11)

## Repository Root
`d:\Work\IOS-IG-SimHost-FDP-2`

## Project Locations
- Primitives: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/`
- Core: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/`
- Core Tests: `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/`
- Solution: `FDP/ExtDeps/NodeEdit/NodeEditor.sln`
- Full solution (verify at end): `IOS-IG-SimHost.sln`

---

## AGENTS.md Requirements (Non-Negotiable)
- Do NOT use Unicode characters in comments or string literals. Use plain ASCII only.
  - No arrows (-> not ->), no special symbols, no em dashes in code.
- Preserve all existing comments exactly.
- Minimize diffs -- only change lines that must change.
- Build must be 0 errors, 0 warnings.

---

## Step 0 -- Read Existing Code First

Read ALL of these before writing any code:

1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionEntry.cs`
2. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionState.cs`
3. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IDetailsViewProvider.cs`
4. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorTheme.cs`
5. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs`
6. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/DefaultTheme.cs`
7. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/AttachmentId.cs` (created in BATCH-05)
8. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentModel.cs` (created in BATCH-05)

---

## Step 1 -- TASK-NEA-04: Attachment Layout Engine

Create a new file: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentLayout.cs`

This file contains the layout result type and interface for measuring attachments.
Namespace: `NodeEditor.Core.Interfaces`. Uses: `NodeEditor.Primitives`.

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// The computed position of a single attachment pill within the host's stack.
/// All coordinates are relative to the host node's top-left corner.
/// Positive Y is downward (canvas coordinate convention).
/// Attachments appear above the host, so Y values will be negative.
/// </summary>
public readonly record struct AttachmentPlacement(
    AttachmentId Id,
    Vector2 TopLeft,
    Vector2 Size);

/// <summary>Result of laying out all attachments for one host node.</summary>
public sealed class AttachmentLayout
{
    /// <summary>Placements indexed by attachment id.</summary>
    public IReadOnlyDictionary<AttachmentId, AttachmentPlacement> Placements { get; }

    /// <summary>
    /// Total height of the attachment stack above the host, including the gap.
    /// Zero when there are no attachments.
    /// </summary>
    public float TotalHeightAboveHost { get; }

    public AttachmentLayout(
        IReadOnlyDictionary<AttachmentId, AttachmentPlacement> placements,
        float totalHeightAboveHost)
    {
        Placements = placements;
        TotalHeightAboveHost = totalHeightAboveHost;
    }

    /// <summary>An empty layout (no attachments).</summary>
    public static AttachmentLayout Empty { get; } =
        new(new Dictionary<AttachmentId, AttachmentPlacement>(), 0f);
}
```

Create a new file: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Spatial/AttachmentLayoutEngine.cs`

Namespace: `NodeEditor.Core.Spatial`. Uses: `NodeEditor.Core.Interfaces`, `NodeEditor.Primitives`, `System.Numerics`.

The engine computes the wrap-and-stack layout. The spec dimensions at zoom 1.0 are:
- Pill height: 20 px
- Min width: 24 px
- Horizontal padding: 6 px (each side)
- Inter-attachment horizontal gap: 4 px
- Inter-row vertical gap: 3 px
- Gap above host header: 6 px

Pill width = measured content width + padding * 2. Minimum pill width is 24 px.

Layout algorithm:
1. Sort attachments by StackIndex ascending (ties broken by attachment Id value).
2. Place attachments left-to-right in a row. When adding the next pill would exceed hostWidth, wrap to a new row above.
3. The bottom-most row sits at Y = -(pillHeight + gapAboveHost) relative to the host top-left.
   Each additional row sits gapAboveHost + pillHeight + interRowGap px further up.
4. Within each row, pills are laid out left-to-right starting at X = 0, with interGap between them.

The public static method takes `IReadOnlyList<IAttachmentModel>`, `float hostWidth`, and a `Func<IAttachmentModel, float>` contentWidthMeasurer (so the engine does not depend on ImGui for measurement — the caller supplies sizes).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Computes the layout of attachment pills above a host node.
/// Pure math -- no rendering dependency. The caller supplies measured content widths.
/// </summary>
public static class AttachmentLayoutEngine
{
    // Spec constants (zoom 1.0 values; callers scale by zoom before passing hostWidth).
    public const float PillHeight          = 20f;
    public const float PillMinWidth        = 24f;
    public const float PillPaddingH        = 6f;   // each side
    public const float InterAttachmentGap  = 4f;
    public const float InterRowGap         = 3f;
    public const float GapAboveHost        = 6f;

    /// <summary>
    /// Compute the layout for a set of attachments on a single host node.
    /// </summary>
    /// <param name="attachments">All attachments for the host, in any order.</param>
    /// <param name="hostWidth">Width of the host node at current zoom.</param>
    /// <param name="measureContentWidth">
    /// Returns the content width (glyph + gap + label, already at current zoom)
    /// for a single attachment. Must return a value greater than or equal to zero.
    /// </param>
    /// <returns>Computed layout. Returns <see cref="AttachmentLayout.Empty"/> when the list is empty.</returns>
    public static AttachmentLayout Compute(
        IReadOnlyList<IAttachmentModel> attachments,
        float hostWidth,
        Func<IAttachmentModel, float> measureContentWidth)
    {
        if (attachments.Count == 0)
            return AttachmentLayout.Empty;

        // Sort by StackIndex, then by attachment Id value for stable ordering on ties.
        var sorted = attachments
            .OrderBy(a => a.StackIndex)
            .ThenBy(a => a.Id.Value)
            .ToList();

        // Compute pill widths.
        var widths = new float[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            float content = measureContentWidth(sorted[i]);
            widths[i] = Math.Max(PillMinWidth, content + PillPaddingH * 2f);
        }

        // Build rows (each row is a list of indices into sorted[]).
        var rows = new List<List<int>>();
        var currentRow = new List<int>();
        float rowUsed = 0f;

        for (int i = 0; i < sorted.Count; i++)
        {
            float needed = (currentRow.Count == 0)
                ? widths[i]
                : rowUsed + InterAttachmentGap + widths[i];

            if (currentRow.Count > 0 && needed > hostWidth)
            {
                rows.Add(currentRow);
                currentRow = new List<int>();
                rowUsed = 0f;
            }

            if (currentRow.Count > 0) rowUsed += InterAttachmentGap;
            currentRow.Add(i);
            rowUsed += widths[i];
        }
        if (currentRow.Count > 0)
            rows.Add(currentRow);

        // Rows are built bottom-up. Row 0 in the list is the bottom-most row
        // (closest to the host header). Compute placements.
        var placements = new Dictionary<AttachmentId, AttachmentPlacement>(sorted.Count);

        // Bottom of bottom row is at Y = -(GapAboveHost + PillHeight).
        // Top of bottom row is at Y = -(GapAboveHost + PillHeight).
        // Y increases downward; attachments are above the host (negative Y).
        // Row top-Y for row index r (0 = bottom-most):
        //   rowTopY(0) = -(GapAboveHost + PillHeight)
        //   rowTopY(r) = rowTopY(0) - r * (PillHeight + InterRowGap)

        float bottomRowTopY = -(GapAboveHost + PillHeight);

        for (int r = 0; r < rows.Count; r++)
        {
            float rowTopY = bottomRowTopY - r * (PillHeight + InterRowGap);
            float x = 0f;

            foreach (int idx in rows[r])
            {
                var attachment = sorted[idx];
                var topLeft = new Vector2(x, rowTopY);
                var size    = new Vector2(widths[idx], PillHeight);
                placements[attachment.Id] = new AttachmentPlacement(attachment.Id, topLeft, size);
                x += widths[idx] + InterAttachmentGap;
            }
        }

        // TotalHeightAboveHost = GapAboveHost + rows.Count * PillHeight + (rows.Count - 1) * InterRowGap.
        float totalHeight = GapAboveHost
            + rows.Count * PillHeight
            + (rows.Count - 1) * InterRowGap;

        return new AttachmentLayout(placements, totalHeight);
    }
}
```

---

## Step 2 -- TASK-NEA-07: Selection of Attachments

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionEntry.cs`

Three changes:
A. Add `AttachmentId Attachment { get; }` property.
B. Add `Attachment` parameter to the private constructor and initialize it.
C. Update all existing factory methods to pass `AttachmentId.Empty` for the new parameter.
D. Add `OfAttachment(AttachmentId id)` factory method.
E. Add `Attachment` to `SelectionEntryKind` enum.

The `SelectionEntry` struct uses `readonly record struct` so equality is auto-generated from all properties.
Add `using NodeEditor.Primitives;` -- it already exists; do not add a duplicate.

The new private constructor signature:
```csharp
private SelectionEntry(SelectionEntryKind k, NodeId n, LinkId l, CommentId c, RerouteRef r, AttachmentId a)
```

The new factory method:
```csharp
public static SelectionEntry OfAttachment(AttachmentId id) =>
    new(SelectionEntryKind.Attachment, NodeId.Empty, LinkId.Empty, CommentId.Empty, default, id);
```

Update enum at end of file:
```csharp
public enum SelectionEntryKind { Node, Link, Comment, Reroute, Attachment }
```

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/SelectionState.cs`

Add an `Attachments` computed property after `Reroutes`:

```csharp
    public IEnumerable<AttachmentId> Attachments =>
        _items.Where(e => e.Kind == SelectionEntryKind.Attachment).Select(e => e.Attachment);
```

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IDetailsViewProvider.cs`

Add two new cases to `DetailsTarget`. The `DetailsTarget` abstract record currently ends with:
```csharp
    public sealed record Function(string FunctionId) : DetailsTarget;
```

Add after it (still within the `DetailsTarget` abstract record block, before the closing brace):
```csharp
    /// <summary>A single selected attachment.</summary>
    public sealed record SingleAttachment(AttachmentId Id) : DetailsTarget;

    /// <summary>Multiple selected attachments.</summary>
    public sealed record MultipleAttachments(IReadOnlyList<AttachmentId> Ids) : DetailsTarget;
```

Add `using NodeEditor.Primitives;` at the top of `IDetailsViewProvider.cs` if not already present. It already has `using NodeEditor.Primitives;` -- check before adding.

---

## Step 3 -- TASK-NEA-09: Context Menu Provider

Create a new file: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentContextMenuProvider.cs`

Namespace: `NodeEditor.Core.Interfaces`. Uses: `NodeEditor.Primitives`.

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// One item in an attachment context menu.
/// Label is the display text; Execute is the action to invoke on click.
/// </summary>
public sealed record ContextMenuItem(string Label, Action Execute, bool Enabled = true);

/// <summary>
/// Host-supplied provider that returns context menu items for a given attachment.
/// Registered via <see cref="IEditorHostServices.AttachmentContextMenu"/>.
/// If no provider is registered, right-clicking an attachment falls through to the
/// canvas empty-area context menu.
/// </summary>
public interface IAttachmentContextMenuProvider
{
    IReadOnlyList<ContextMenuItem> GetItemsFor(AttachmentId id);
}
```

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs`

Add a new optional property `AttachmentContextMenu` to `IEditorHostServices`. Use a default interface member returning null so existing implementations do not need to change.

Add after the existing `IEditorTheme Theme { get; }` property:

```csharp
    /// <summary>
    /// Optional provider for attachment right-click context menus.
    /// Returns null when no provider is registered.
    /// </summary>
    IAttachmentContextMenuProvider? AttachmentContextMenu => null;
```

---

## Step 4 -- TASK-NEA-11 (Theme Part): Theme Additions

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorTheme.cs`

Add 8 new properties as default interface members. Default values must exactly match the spec:
- AttachmentDecoratorColor: #8E44AD (purple)
- AttachmentFlagColor: #16A085 (teal)
- AttachmentPureColor: #27AE60 (green)
- AttachmentCustomColor: #7F8C8D (gray)
- AttachmentHeight: 20f
- AttachmentCornerRadius: 8f
- AttachmentGapAboveHost: 6f
- AttachmentInterGap: 4f

Add a `using System.Numerics;` -- it already has it; do not add a duplicate.

Add at the end of the interface body (before the closing brace):

```csharp
    // ---- Attachment pill colors (default values match spec section 5.3) ----

    /// <summary>Background color for Decorator category pills. Default: #8E44AD.</summary>
    Vector4 AttachmentDecoratorColor => new(0x8E / 255f, 0x44 / 255f, 0xAD / 255f, 1f);

    /// <summary>Background color for Flag category pills. Default: #16A085.</summary>
    Vector4 AttachmentFlagColor      => new(0x16 / 255f, 0xA0 / 255f, 0x85 / 255f, 1f);

    /// <summary>Background color for Pure category pills. Default: #27AE60.</summary>
    Vector4 AttachmentPureColor      => new(0x27 / 255f, 0xAE / 255f, 0x60 / 255f, 1f);

    /// <summary>Background color for Custom category pills. Default: #7F8C8D.</summary>
    Vector4 AttachmentCustomColor    => new(0x7F / 255f, 0x8C / 255f, 0x8D / 255f, 1f);

    // ---- Attachment pill geometry ----

    /// <summary>Pill height in canvas units at zoom 1.0. Default: 20.</summary>
    float AttachmentHeight        => 20f;

    /// <summary>Pill corner radius at zoom 1.0. Default: 8.</summary>
    float AttachmentCornerRadius  => 8f;

    /// <summary>Vertical gap between pill bottom and host header. Default: 6.</summary>
    float AttachmentGapAboveHost  => 6f;

    /// <summary>Horizontal gap between adjacent pills. Default: 4.</summary>
    float AttachmentInterGap      => 4f;
```

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/DefaultTheme.cs`

Add 8 new explicit `{ get; init; }` properties with the same defaults as the interface (so `DefaultTheme` overrides the default interface members with concrete values).

Add at the end of the `DefaultTheme` class (before the closing brace of the class, after the `GetFontForSize` method):

```csharp
    // ---- Attachment pill colors ----
    public Vector4 AttachmentDecoratorColor { get; init; } = new(0x8E / 255f, 0x44 / 255f, 0xAD / 255f, 1f);
    public Vector4 AttachmentFlagColor      { get; init; } = new(0x16 / 255f, 0xA0 / 255f, 0x85 / 255f, 1f);
    public Vector4 AttachmentPureColor      { get; init; } = new(0x27 / 255f, 0xAE / 255f, 0x60 / 255f, 1f);
    public Vector4 AttachmentCustomColor    { get; init; } = new(0x7F / 255f, 0x8C / 255f, 0x8D / 255f, 1f);

    // ---- Attachment pill geometry ----
    public float AttachmentHeight       { get; init; } = 20f;
    public float AttachmentCornerRadius { get; init; } = 8f;
    public float AttachmentGapAboveHost { get; init; } = 6f;
    public float AttachmentInterGap     { get; init; } = 4f;
```

---

## Step 5 -- Tests

### File 5a: Create `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Spatial/AttachmentLayoutTests.cs`

Minimum 6 tests:
1. `SingleAttachment_SingleRow` -- one attachment with a given width: verify it's placed at (0, -(GapAboveHost+PillHeight)), total height = GapAboveHost + PillHeight
2. `TwoAttachments_FitInOneRow` -- two attachments that fit side-by-side in hostWidth: verify positions and total height = one row
3. `TwoAttachments_WrapToSecondRow` -- two attachments that do NOT fit side-by-side: second wraps to row above; total height = 2 rows (2*PillHeight + InterRowGap + GapAboveHost)
4. `SortsByStackIndex` -- two attachments with StackIndex 1 and 0: verify attachment with index 0 comes first (leftmost)
5. `EmptyList_ReturnsEmpty` -- zero attachments: returns AttachmentLayout.Empty, TotalHeightAboveHost == 0
6. `MinWidth_Applied` -- attachment with zero content width: pill width is clamped to PillMinWidth (24 px)

Use constants from `AttachmentLayoutEngine`:
```
using NodeEditor.Core.Spatial;
const float H = AttachmentLayoutEngine.PillHeight;       // 20
const float G = AttachmentLayoutEngine.GapAboveHost;     // 6
const float Ir = AttachmentLayoutEngine.InterRowGap;     // 3
const float Ig = AttachmentLayoutEngine.InterAttachmentGap; // 4
const float P = AttachmentLayoutEngine.PillPaddingH;     // 6 (each side)
const float M = AttachmentLayoutEngine.PillMinWidth;     // 24
```

Use a simple stub `IAttachmentModel` implementation inside the test class.

### File 5b: Create `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/AttachmentSelectionTests.cs`

Minimum 4 tests:
1. `OfAttachment_KindIsAttachment` -- `SelectionEntry.OfAttachment(id).Kind == SelectionEntryKind.Attachment`
2. `OfAttachment_AttachmentPropertySet` -- verify `.Attachment == id`
3. `SelectionState_Attachments_FiltersCorrectly` -- add a node and an attachment to selection; `.Attachments` yields only the attachment id
4. `Toggle_Attachment` -- toggle an attachment entry on/off via `SelectionState.Toggle`

---

## Step 6 -- Build and Test

```
dotnet build FDP/ExtDeps/NodeEdit/NodeEditor.sln
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj
dotnet build IOS-IG-SimHost.sln
```

All must produce 0 errors, 0 warnings.

---

## Step 7 -- Update TASK-TRACKER.md

Mark these tasks as `[x]` in `.dev/blueprints-2/TASK-TRACKER.md`:
- TASK-NEA-04
- TASK-NEA-07
- TASK-NEA-09
- TASK-NEA-11

---

## Step 8 -- Submit Report

Write the report to:
`d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\reports\BATCH-06-REPORT.md`

Include:
- Build results (both solutions)
- Test count: existing + new
- Files created/modified
- Any deviations from spec
- Answers to Developer Insights:
  1. Did adding `AttachmentId` to `SelectionEntry`'s private constructor break any existing test?
  2. Did `IEditorTheme` default interface members compile without issue on all C# 8+ targets?
  3. How many rows does the layout engine produce for 5 attachments of width 30 each on a host of width 100?
  4. Does `DefaultTheme` need explicit overrides of the interface defaults, or do the defaults suffice for `DefaultTheme`'s contract?
  5. Total new test count for this batch.
