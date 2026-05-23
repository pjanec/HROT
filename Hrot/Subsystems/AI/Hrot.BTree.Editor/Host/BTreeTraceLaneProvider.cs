using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Declares the four trace timeline swim-lanes for BTree assets.
/// Registered at startup so the shared Trace Timeline window can render them.
/// </summary>
public sealed class BTreeTraceLaneProvider : ITraceLaneProvider
{
    private static readonly IReadOnlyList<TraceLaneDescriptor> _lanes =
    [
        new TraceLaneDescriptor("bt.nodes", "NodeStatus", TraceLevel.Lifecycle | TraceLevel.Decisions),
        new TraceLaneDescriptor("bt.stack",  "Stack",     TraceLevel.Lifecycle),
        new TraceLaneDescriptor("bt.async",  "Async",     TraceLevel.Async),
        new TraceLaneDescriptor("bt.errors", "Errors",    TraceLevel.Errors),
    ];

    public AssetKind Kind => AssetKind.BTree;

    public IReadOnlyList<TraceLaneDescriptor> Lanes => _lanes;
}
