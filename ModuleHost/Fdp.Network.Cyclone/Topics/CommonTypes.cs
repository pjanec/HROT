using System;
using CycloneDDS.Schema;

namespace ModuleHost.Network.Cyclone.Topics
{
    /// <summary>
    /// Unique identifier for a network application instance.
    /// Combines domain and instance to form a globally unique ID.
    /// </summary>
    [DdsStruct]
    public partial struct NetworkAppId : IEquatable<NetworkAppId>
    {
        [DdsId(0)] public int AppDomainId;
        [DdsId(1)] public int AppInstanceId;

        public bool Equals(NetworkAppId other) => 
            AppDomainId == other.AppDomainId && AppInstanceId == other.AppInstanceId;

        public override bool Equals(object? obj) => 
            obj is NetworkAppId other && Equals(other);

        public override int GetHashCode() => 
            HashCode.Combine(AppDomainId, AppInstanceId);

        public static bool operator ==(NetworkAppId left, NetworkAppId right) => 
            left.Equals(right);

        public static bool operator !=(NetworkAppId left, NetworkAppId right) => 
            !left.Equals(right);

        public override string ToString() => 
            $"{AppDomainId}:{AppInstanceId}";
    }

    /// <summary>
    /// Network affiliation for entity identification.
    /// Maps to DIS affiliation concepts.
    /// </summary>
    public enum NetworkAffiliation : int
    {
        Neutral = 0,
        Friend_Blue = 1,
        Hostile_Red = 2,
        Unknown = 3
    }

    /// <summary>
    /// Network lifecycle state for entity synchronization.
    /// Corresponds to entity lifecycle phases.
    /// </summary>
    public enum NetworkLifecycleState : int
    {
        Ghost = 0,
        Constructing = 1,
        Active = 2,
        TearDown = 3
    }
}
