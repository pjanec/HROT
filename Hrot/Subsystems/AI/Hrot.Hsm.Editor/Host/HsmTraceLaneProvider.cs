using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Declares the trace lane structure for the HSM asset type.
/// Consumed by the shared trace panel to build the timeline column headers.
/// </summary>
public sealed class HsmTraceLaneProvider : ITimelineLaneProvider
{
    private static readonly IReadOnlyList<TimelineLaneDescriptor> _lanes =
    [
        new TimelineLaneDescriptor("hsm.states",    "States",    TraceLevel.Lifecycle),
        new TimelineLaneDescriptor("hsm.events",    "Events",    TraceLevel.Decisions),
        new TimelineLaneDescriptor("hsm.actions",   "Actions",   TraceLevel.Decisions),
        new TimelineLaneDescriptor("hsm.guards",    "Guards",    TraceLevel.Decisions),
        new TimelineLaneDescriptor("hsm.timers",    "Timers",    TraceLevel.Decisions),
        new TimelineLaneDescriptor("hsm.conflicts", "Conflicts", TraceLevel.Errors),
    ];

    public bool Applies(TimelineLaneContext context) => context.AssetKind == nameof(AssetKind.Hsm);
    public IReadOnlyList<TimelineLaneDescriptor> Lanes => _lanes;
}
