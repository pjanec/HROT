using System.Collections.Generic;
using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.Common.Serializers
{
    /// <summary>
    /// Managed Intent DTO component that stores the list of passenger Network IDs
    /// for an embarked unit's vehicle during scenario genesis.
    ///
    /// <para>Written by <c>PassengerBufferTranslator.Inject</c>; consumed and resolved
    /// to a live <see cref="Fdp.Toolkit.Behavior.Components.PassengerBuffer"/> by
    /// <c>GenesisMaterializationSystem</c>.</para>
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(HrotComponentIds.InitialPassengersIntent)]
    public sealed class InitialPassengersIntent
    {
        /// <summary>Network IDs of all passenger entities at scenario load time.</summary>
        public List<long> PassengerNetworkIds { get; set; } = new();
    }

    /// <summary>
    /// Managed Intent DTO component that stores the Network ID of the vehicle entity
    /// for an embarked soldier during scenario genesis.
    ///
    /// <para>Written by <c>IsEmbarkedTagTranslator.Inject</c>; resolved to a live
    /// <see cref="Fdp.Toolkit.Behavior.Components.IsEmbarkedTag"/> by
    /// <c>GenesisMaterializationSystem</c>.</para>
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(HrotComponentIds.InitialVehicleIntent)]
    public sealed class InitialVehicleIntent
    {
        /// <summary>Network ID of the vehicle entity this soldier is embarked in.</summary>
        public long VehicleNetworkId { get; set; }
    }

    /// <summary>
    /// Managed Intent DTO component that stores the Network IDs for the three
    /// hierarchy link entities (parent, first-child, next-sibling) during scenario genesis.
    ///
    /// <para>Written by <c>VisHierarchyNodeTranslator.Inject</c>; resolved to a live
    /// <see cref="Fdp.Toolkit.Vis2D.Components.VisHierarchyNode"/> by
    /// <c>GenesisMaterializationSystem</c>. A value of <c>0</c> means the entity
    /// reference was <see langword="null"/> in the scenario file.</para>
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(HrotComponentIds.InitialHierarchyIntent)]
    public sealed class InitialHierarchyIntent
    {
        /// <summary>Network ID of the parent entity (0 = no parent).</summary>
        public long ParentNetworkId { get; set; }

        /// <summary>Network ID of the first-child entity (0 = no first child).</summary>
        public long FirstChildNetworkId { get; set; }

        /// <summary>Network ID of the next-sibling entity (0 = no next sibling).</summary>
        public long NextSiblingNetworkId { get; set; }
    }

    /// <summary>
    /// Managed Intent DTO component that stores the Network ID of the personal
    /// route entity for a vehicle during scenario genesis.
    ///
    /// <para>Written by <c>PersonalRouteRefTranslator.Inject</c>; resolved to a live
    /// <see cref="Hrot.Map.Common.Components.PersonalRouteRef"/> by
    /// <c>GenesisMaterializationSystem</c>.</para>
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(HrotComponentIds.InitialRouteIntent)]
    public sealed class InitialRouteIntent
    {
        /// <summary>Network ID of the personal route entity (0 = none).</summary>
        public long RouteNetworkId { get; set; }
    }

    /// <summary>
    /// A single entry in <see cref="InitialTargetsIntent"/> carrying the Network ID and
    /// sensor data for one perceived target at scenario load time.
    /// </summary>
    public struct TargetEntry
    {
        /// <summary>Network ID of the perceived target entity.</summary>
        public long  NetworkId;

        /// <summary>Last known X position of the target (world units).</summary>
        public float PosX;

        /// <summary>Last known Y position of the target (world units).</summary>
        public float PosY;

        /// <summary>Threat score assigned to this target by the perception system.</summary>
        public float Score;

        /// <summary>Simulation tick at which this target was last observed.</summary>
        public uint  LastSeenTick;

        /// <summary>Encoded sensor modality that detected this target.</summary>
        public byte  Modality;
    }

    /// <summary>
    /// Managed Intent DTO component that stores the target list (Network IDs + sensor data)
    /// for an entity's perception memory during scenario genesis.
    ///
    /// <para>Written by <c>TargetMemoryTranslator.Inject</c>; resolved to a live
    /// <see cref="Fdp.Toolkit.Perception.Components.TargetMemory"/> by
    /// <c>GenesisMaterializationSystem</c>. Partial materialization is allowed:
    /// targets whose Network ID cannot be resolved are silently dropped.</para>
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(HrotComponentIds.InitialTargetsIntent)]
    public sealed class InitialTargetsIntent
    {
        /// <summary>Target entries stored at scenario load time.</summary>
        public List<TargetEntry> Entries { get; set; } = new();
    }
}
