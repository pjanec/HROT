using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.Utility.Editor.Tracing;

public sealed class UtilityTraceLaneProvider : ITimelineLaneProvider
{
    public bool Applies(TimelineLaneContext context) => context.AssetKind == nameof(AssetKind.Utility);

    public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new TimelineLaneDescriptor[]
    {
        new("utility_scoring",  "Decision Scoring",       TraceLevel.Decisions),
        new("utility_values",   "Consideration Values",   TraceLevel.Values),
    };
}
