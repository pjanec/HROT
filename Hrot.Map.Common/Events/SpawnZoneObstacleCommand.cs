using System.Numerics;

namespace Hrot.Map.Common.Events
{
    /// <summary>
    /// Managed command requesting the creation of a new zone obstacle entity.
    /// Published via <c>Bus.PublishManaged</c> and consumed by the zone-obstacle
    /// ingress system to spawn the corresponding ECS entity.
    ///
    /// <para>This is a managed event (class) because it carries <c>string</c> data.
    /// No <c>[EventId]</c> attribute is required; managed events are routed by CLR type.</para>
    /// </summary>
    public sealed class SpawnZoneObstacleCommand
    {
        /// <summary>Unique name identifying the obstacle zone within the scenario.</summary>
        public string  ZoneName { get; init; } = string.Empty;

        /// <summary>World-space centre position of the obstacle zone (XY plane).</summary>
        public Vector2 Position { get; init; }

        /// <summary>Radius of the obstacle zone in metres.</summary>
        public float   Radius   { get; init; }
    }
}
