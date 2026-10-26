using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Declares the four trace timeline swim-lanes for BTree assets.
/// Registered at startup so the shared Trace Timeline window can render them.
/// </summary>
public sealed class BTreeTraceLaneProvider : ITimelineLaneProvider
{
    private static readonly IReadOnlyList<TimelineLaneDescriptor> _lanes =
    [
        new TimelineLaneDescriptor("bt.nodes", "NodeStatus", TraceLevel.Lifecycle | TraceLevel.Decisions),
        new TimelineLaneDescriptor("bt.stack",  "Stack",     TraceLevel.Lifecycle),
        new TimelineLaneDescriptor("bt.async",  "Async",     TraceLevel.Async),
        new TimelineLaneDescriptor("bt.errors", "Errors",    TraceLevel.Errors),
    ];

    public bool Applies(TimelineLaneContext context) => context.AssetKind == nameof(AssetKind.BTree);

    public IReadOnlyList<TimelineLaneDescriptor> Lanes => _lanes;
}
