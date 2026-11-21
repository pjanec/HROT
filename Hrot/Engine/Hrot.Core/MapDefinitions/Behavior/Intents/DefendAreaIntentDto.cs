using Fdp.Core;
namespace Hrot.Map.Definitions.Behavior.Intents
{
    /// <summary>
    /// Intent DTO for the "DefendArea" tactical intent.
    /// Decorated with <see cref="BehaviorAttribute"/> (on the behaviour) so it is
    /// auto-discovered by <c>BehaviorSchemaDiscovery.AutoRegister</c> and
    /// <c>BehaviorCatalog</c>.
    ///
    /// <para>
    /// <b>BehaviorId:</b> "DefendArea" — this string must match the
    /// <c>TargetIntentId</c> of the <c>DefendAreaMapper</c> (TASK-TI011).
    /// </para>
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    [Fdp.Toolkit.Replication.RetiredJsonKeys("CenterLat", "CenterLon")]   // CE-4153 S10: S0 item 5 folded them into `Center`
    public sealed class DefendAreaIntentDto
    {
        public const string BehaviorId = BehaviorNames.DefendArea;

        /// <summary>⭐ S0 item 5 (SR-135) — the area's centre: ONE authored place (local metres, or an entity's position). Replaces the lat/lon pair.</summary>
        public Fdp.Toolkit.Replication.PointLink Center;

        /// <summary>Radius in meters.</summary>
        public float RadiusMeters;
    }
}
