using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Common.Orchestration;

/// <summary>⭐ Node Features F-16 / R-134 — the ONE conversion between <see cref="FeatureOverrideSpec"/> and its DDS twin <see cref="FeatureOverrideWire"/>.</summary>
public static class FeatureOverrideWires
{
    public static List<FeatureOverrideWire> ToWire(IEnumerable<FeatureOverrideSpec>? specs)
        => (specs ?? System.Array.Empty<FeatureOverrideSpec>())
            .Select(o => new FeatureOverrideWire { Feature = o.Feature ?? string.Empty, Nodes = o.Nodes?.ToList() ?? new List<string>(), Json = o.Json ?? string.Empty })
            .ToList();

    public static FeatureOverrideSpec[] FromWire(IEnumerable<FeatureOverrideWire>? wires)
        => (wires ?? System.Array.Empty<FeatureOverrideWire>())
            .Select(w => new FeatureOverrideSpec(w.Feature ?? string.Empty, (IReadOnlyList<string>?)w.Nodes?.ToArray() ?? System.Array.Empty<string>(), w.Json ?? string.Empty))
            .ToArray();
}
