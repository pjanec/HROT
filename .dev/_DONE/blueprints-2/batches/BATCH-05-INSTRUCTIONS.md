# BATCH-05 — NodeEditor: NodeAttachments Model Foundation

## Tasks Covered
- TASK-NEA-01: `AttachmentId` and `IAttachmentModel`
- TASK-NEA-02: `IGraphModel` extension for attachments
- TASK-NEA-03: `GraphChangeKind` + `GraphChangeNotification` extensions
- TASK-NEA-08: Attachment `GraphCommand` records

## Spec References
Read these files in full BEFORE writing any code:
1. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\NodeEditor_Extension_NodeAttachments.md` (sections 4.1, 4.2, 4.3, 4.4, 8)
2. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\TASK-DETAIL.md` (Phase 2, tasks NEA-01 through NEA-03, NEA-08)

## Repository Root
`d:\Work\IOS-IG-SimHost-FDP-2`

## Project Locations
- Primitives: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/`
- Core: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/`
- Demo: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/`
- Core Tests: `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/`
- Solution: `FDP/ExtDeps/NodeEdit/NodeEditor.sln`
- Full solution (verify at end): `d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln`

---

## AGENTS.md Requirements (Non-Negotiable)
- Do NOT use Unicode characters in comments or string literals. Use plain ASCII only.
- Preserve all existing comments exactly unless they are wrong.
- Minimize diffs — only change lines that must change.
- Build must be clean (0 errors, 0 warnings) before submitting the report.

---

## Step 0 — Read Existing Code First

Read these files completely before writing any new code:

1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/NodeId.cs` — the pattern to follow for AttachmentId
2. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs` — the interface to extend
3. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Commands/GraphCommand.cs` — the class to extend
4. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs` — has a call site to update

---

## Step 1 — TASK-NEA-01: AttachmentId and IAttachmentModel

### File 1a: Create `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/AttachmentId.cs`

Follow the same pattern as `NodeId.cs` exactly. Namespace: `NodeEditor.Primitives`.

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// Unique identifier for an attachment in a graph. Wraps a <see cref="Guid"/>
/// to provide type safety; never expose raw Guids in the public API.
/// </summary>
public readonly record struct AttachmentId(Guid Value)
{
    /// <summary>The empty (default-constructed) AttachmentId.</summary>
    public static AttachmentId Empty => default;

    /// <summary>Generate a new, random AttachmentId.</summary>
    public static AttachmentId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => $"Attachment({Value:N}[..8])"[..19];
}
```

### File 1b: Create `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IAttachmentModel.cs`

New file. Namespace: `NodeEditor.Core.Interfaces`. Uses: `NodeEditor.Primitives`.

Exact contents:

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of one attachment pinned to a host node.
/// Implemented by the host; the editor never mutates this directly.
/// </summary>
public interface IAttachmentModel
{
    AttachmentId Id { get; }
    NodeId HostNodeId { get; }

    /// <summary>
    /// Stable categorization. Determines header color and default visual.
    /// Host-defined; NodeEditor does not interpret the value.
    /// </summary>
    AttachmentCategory Category { get; }

    /// <summary>
    /// Optional short glyph rendered first in the pill body.
    /// One or two characters; rendered larger than the label.
    /// Null means no glyph.
    /// </summary>
    string? Glyph { get; }

    /// <summary>
    /// Optional one-line label rendered after the glyph.
    /// Truncated with ellipsis if too long.
    /// Null means no label (glyph-only pill).
    /// </summary>
    string? Label { get; }

    /// <summary>Tooltip on hover. Multi-line allowed.</summary>
    string? Tooltip { get; }

    /// <summary>
    /// State flags affecting visual treatment.
    /// Identical semantics to NodeState for the shared bits.
    /// </summary>
    AttachmentState State { get; }

    /// <summary>
    /// Ordering position within the host's attachment stack.
    /// Lower values render to the left; equal values are stable-sorted by Id.
    /// </summary>
    int StackIndex { get; }
}

/// <summary>Stable categorization for an attachment.</summary>
public enum AttachmentCategory
{
    /// <summary>BTree decorator (Inverter, Repeater, etc.).</summary>
    Decorator,
    /// <summary>HSM state flag (deferred-events, has-history, conflict).</summary>
    Flag,
    /// <summary>Blueprint pure-call (future use).</summary>
    Pure,
    /// <summary>Host-defined; uses theme custom color.</summary>
    Custom,
}

/// <summary>State flags for visual treatment of an attachment.</summary>
[Flags]
public enum AttachmentState
{
    Normal           = 0,
    Disabled         = 1 << 0,
    Error            = 1 << 1,
    Warning          = 1 << 2,
    Executing        = 1 << 3,   // debug only
    RecentlyExecuted = 1 << 4,   // debug only
    Selected         = 1 << 5,   // editor-managed, never set by host
}
```

---

## Step 2 — TASK-NEA-02 and NEA-03: IGraphModel Extension + Change Notification

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs`

You need to make THREE changes to this file:

**Change A: Add attachment members to `IGraphModel` with default implementations.**

The new members must be added after the existing `FindLink` method and before the `Changed` event. Use default interface implementations so existing implementations (like `StubModel` in tests and `FakeGraphModel` in the demo) compile unchanged.

Add these three members after `ILinkModel? FindLink(LinkId id);`:

```csharp
    /// <summary>All attachments currently in this graph.</summary>
    IReadOnlyCollection<IAttachmentModel> Attachments
        => Array.Empty<IAttachmentModel>();

    /// <summary>Find an attachment by id, or null if not present.</summary>
    IAttachmentModel? FindAttachment(AttachmentId id) => null;

    /// <summary>
    /// Returns all attachments whose host is the given node, ordered by StackIndex ascending.
    /// Returns an empty list if the node has no attachments or does not exist.
    /// </summary>
    IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId)
        => Array.Empty<IAttachmentModel>();
```

**Change B: Add three new values to `GraphChangeKind` enum.**

The current enum ends with:
```csharp
    VariablesChanged,
    Wholesale,
```

Change it to:
```csharp
    VariablesChanged,
    AttachmentsAdded,
    AttachmentsRemoved,
    AttachmentsModified,
    Wholesale,
```

**Change C: Update `GraphChangeNotification` record.**

Current record:
```csharp
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    string? Reason);
```

New record (insert `AffectedAttachments` between `AffectedLinks` and `Reason`):
```csharp
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    IReadOnlySet<AttachmentId>? AffectedAttachments,
    string? Reason);
```

This is a breaking positional change. You MUST also fix the one call site in the Demo project (see Step 3).

IMPORTANT: Add `using NodeEditor.Primitives;` at the top of IGraphModel.cs if not already present. It already exists — do not add a duplicate.

---

## Step 3 — Fix the One Breaking Call Site

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs`

Find the line (approximately line 90):
```csharp
        => Changed?.Invoke(new GraphChangeNotification(kind, null, null, null));
```

Change it to (pass null for the new `AffectedAttachments` parameter):
```csharp
        => Changed?.Invoke(new GraphChangeNotification(kind, null, null, null, null));
```

That is the only call site in the src/ tree that passes positional arguments. The test stubs use event-only (no construction), so they need no changes.

---

## Step 4 — TASK-NEA-08: Attachment GraphCommand Records

### Modify `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Commands/GraphCommand.cs`

Add the five new attachment command records just before the `Batch` record (keep `Batch` as the last record — it's the "group" command and should stay last for readability). Insert after the `ExpandNode` record:

```csharp
    /// <summary>Add an attachment to a host node.</summary>
    public sealed record AddAttachment(
        AttachmentId NewId,
        NodeId HostNodeId,
        AttachmentCategory Category,
        string? Glyph,
        string? Label,
        string? Tooltip,
        int StackIndex,
        IReadOnlyDictionary<string, object?>? HostProperties) : GraphCommand;

    /// <summary>Remove one or more attachments by id.</summary>
    public sealed record RemoveAttachments(
        IReadOnlyList<AttachmentId> AttachmentIds) : GraphCommand;

    /// <summary>Set a host-defined property on an attachment.</summary>
    public sealed record SetAttachmentProperty(
        AttachmentId Id,
        string Key,
        object? Value) : GraphCommand;

    /// <summary>Reorder the attachments of a single host node.</summary>
    public sealed record ReorderAttachments(
        NodeId HostNodeId,
        IReadOnlyList<AttachmentId> NewOrder) : GraphCommand;

    /// <summary>Move an attachment to a different host node.</summary>
    public sealed record MoveAttachment(
        AttachmentId Id,
        NodeId NewHostNodeId,
        int NewStackIndex) : GraphCommand;
```

Add the required using at the top of the file if not already present:
```csharp
using NodeEditor.Core.Interfaces;
```

(Check the existing usings — if `NodeEditor.Core.Interfaces` is already there, do not add a duplicate.)

---

## Step 5 — Tests

### File 5a: Create `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Primitives/AttachmentIdTests.cs`

Minimum 4 tests:
1. `Empty_IsDefault` — `AttachmentId.Empty == default`
2. `NewId_GeneratesUniqueId` — two `NewId()` calls return different values
3. `Equality_SameGuid_Equal` — two `AttachmentId` with same Guid are equal
4. `Equality_DifferentGuid_NotEqual` — two `AttachmentId` with different Guid are not equal

### File 5b: Create `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Commands/AttachmentCommandsTests.cs`

One test per command record, verifying construction and property access:
1. `AddAttachment_Roundtrip` — construct `AddAttachment`, verify all properties
2. `RemoveAttachments_Roundtrip` — construct `RemoveAttachments`, verify `AttachmentIds`
3. `SetAttachmentProperty_Roundtrip` — verify `Id`, `Key`, `Value`
4. `ReorderAttachments_Roundtrip` — verify `HostNodeId`, `NewOrder`
5. `MoveAttachment_Roundtrip` — verify `Id`, `NewHostNodeId`, `NewStackIndex`

All tests must use `using NodeEditor.Core.Commands;`, `using NodeEditor.Core.Interfaces;`, `using NodeEditor.Primitives;`.

---

## Step 6 — Build and Test

After all changes, run:

```
dotnet build FDP/ExtDeps/NodeEdit/NodeEditor.sln
```

Fix any errors. Then run:

```
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj
```

Then run the full solution build:

```
dotnet build IOS-IG-SimHost.sln
```

All must produce 0 errors.

---

## Step 7 — Submit Report

Write the report to:
`d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\reports\BATCH-05-REPORT.md`

Include:
- Build result (0 errors, 0 warnings)
- Test count: existing + new
- Files created/modified
- Any deviations from spec
- Answers to Developer Insights questions:
  1. How many existing implementations of IGraphModel exist across the codebase (src + tests)?
  2. Were the default interface implementations necessary for any test stubs, or were they all fine with explicit overrides?
  3. Did any test file other than FakeGraphModel.cs construct a `GraphChangeNotification` positionally that needed fixing?
  4. How many total attachment-related tests were written?
  5. Is there a `NodeEditor.sln`-level build target that builds all 4 projects together?
