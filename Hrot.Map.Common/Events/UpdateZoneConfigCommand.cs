namespace Hrot.Map.Common.Events
{
    /// <summary>
    /// Managed command requesting a runtime configuration update for an existing zone.
    /// Published via <c>Bus.PublishManaged</c> and consumed by the zone-config
    /// ingress system to reload road-network data or other mutable zone properties.
    ///
    /// <para>This is a managed event (class) because it carries <c>string?</c> data.
    /// No <c>[EventId]</c> attribute is required; managed events are routed by CLR type.</para>
    /// </summary>
    public sealed class UpdateZoneConfigCommand
    {
        /// <summary>Name of the zone whose configuration should be updated.</summary>
        public string  ZoneName        { get; init; } = string.Empty;

        /// <summary>
        /// Optional path to a road-network JSON file.  When <c>null</c> or empty the
        /// zone's existing road network is retained unchanged.
        /// </summary>
        public string? RoadNetworkPath { get; init; }
    }
}
