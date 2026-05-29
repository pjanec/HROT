using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Utility.Editor.Tracing;

public sealed class UtilityTraceLaneProvider : ITraceLaneProvider
{
    public AssetKind Kind => AssetKind.Utility;

    public IReadOnlyList<TraceLaneDescriptor> Lanes { get; } = new TraceLaneDescriptor[]
    {
        new("utility_scoring",  "Decision Scoring",       TraceLevel.Decisions),
        new("utility_values",   "Consideration Values",   TraceLevel.Values),
    };
}
