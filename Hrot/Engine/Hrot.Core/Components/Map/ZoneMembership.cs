using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.Map.Common.Components;

/// <summary>
/// Managed component that records which named zone an obstacle entity belongs to.
/// Attached by <c>EditorZoneAuthoringSystem</c> when a zone obstacle is spawned.
/// </summary>
[ComponentId(HrotComponentIds.ZoneMembership)]
public sealed class ZoneMembership
{
    /// <summary>Name of the zone to which this entity belongs.</summary>
    public string ZoneName { get; init; } = string.Empty;
}
