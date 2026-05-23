using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Declares the trace lane structure for the HSM asset type.
/// Consumed by the shared trace panel to build the timeline column headers.
/// </summary>
public sealed class HsmTraceLaneProvider : ITraceLaneProvider
{
    private static readonly IReadOnlyList<TraceLaneDescriptor> _lanes =
    [
        new TraceLaneDescriptor("hsm.states",    "States",    TraceLevel.Lifecycle),
        new TraceLaneDescriptor("hsm.events",    "Events",    TraceLevel.Decisions),
        new TraceLaneDescriptor("hsm.actions",   "Actions",   TraceLevel.Decisions),
        new TraceLaneDescriptor("hsm.guards",    "Guards",    TraceLevel.Decisions),
        new TraceLaneDescriptor("hsm.timers",    "Timers",    TraceLevel.Decisions),
        new TraceLaneDescriptor("hsm.conflicts", "Conflicts", TraceLevel.Errors),
    ];

    public AssetKind Kind => AssetKind.Hsm;
    public IReadOnlyList<TraceLaneDescriptor> Lanes => _lanes;
}
