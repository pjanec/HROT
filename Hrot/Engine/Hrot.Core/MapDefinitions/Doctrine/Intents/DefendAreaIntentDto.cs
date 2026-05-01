namespace Hrot.Map.Definitions.Doctrine.Intents
{
    /// <summary>
    /// Intent DTO for the "DefendArea" tactical intent.
    /// Decorated with <see cref="DoctrineContractAttribute"/> so it is
    /// auto-discovered by <c>DoctrineSchemaDiscovery.AutoRegister</c> and
    /// <c>DoctrineCatalog</c>.
    ///
    /// <para>
    /// <b>BehaviorId:</b> "DefendArea" — this string must match the
    /// <c>TargetIntentId</c> of the <c>DefendAreaMapper</c> (TASK-TI011).
    /// </para>
    /// </summary>
    [DoctrineContract(DoctrineIds.DefendArea_Intent, BehaviorId, DoctrineCategory.AllMilitary)]
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
