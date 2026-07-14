namespace Hrot.Map.Definitions.Behavior.Intents
{
    /// <summary>
    /// Intent DTO for the "DefendArea" tactical intent.
    /// Decorated with <see cref="BehaviorContractAttribute"/> so it is
    /// auto-discovered by <c>BehaviorSchemaDiscovery.AutoRegister</c> and
    /// <c>BehaviorCatalog</c>.
    ///
    /// <para>
    /// <b>BehaviorId:</b> "DefendArea" — this string must match the
    /// <c>TargetIntentId</c> of the <c>DefendAreaMapper</c> (TASK-TI011).
    /// </para>
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class DefendAreaIntentDto
    {
        public const string BehaviorId = "DefendArea";

        /// <summary>Latitude of the area center.</summary>
        public double CenterLat { get; set; }

        /// <summary>Longitude of the area center.</summary>
        public double CenterLon { get; set; }

        /// <summary>Radius in meters.</summary>
        public float RadiusMeters { get; set; }
    }
}
