using Fdp.Core;
using Hrot.NED.Descriptors;

namespace Hrot.Map.Common.Replication.Utils
{
    /// <summary>
    /// The one mapping between the ECS <see cref="ForceId"/> and the wire <see cref="eForceIdentifier"/>, both ways.
    ///
    /// <para>⚠ The two enums do NOT share numbering — wire <c>UNKNOWN=0, FRIENDLY=1, OPPOSING=2, NEUTRAL=3</c>, ECS
    /// <c>Neutral=0, Friend=1, Hostile=2</c>. Ingress used to CAST the wire value, so a neutral entity reached every
    /// replica as <c>(ForceId)3</c>, a value with no member (measured on <c>ClusterRunner --mode all</c>: the debug API
    /// could not serialize any replica of a neutral entity).</para>
    /// </summary>
    public static class ForceIdMapping
    {
        public static eForceIdentifier ToWire(ForceId forceId) => forceId switch
        {
            ForceId.Friend  => eForceIdentifier.FORCE_FRIENDLY,
            ForceId.Hostile => eForceIdentifier.FORCE_OPPOSING,
            ForceId.Neutral => eForceIdentifier.FORCE_NEUTRAL,
            _               => eForceIdentifier.FORCE_UNKNOWN,
        };

        /// <summary>The ECS has no "unknown" force; an unknown wire force reads as the ECS default, Neutral.</summary>
        public static ForceId FromWire(eForceIdentifier wire) => wire switch
        {
            eForceIdentifier.FORCE_FRIENDLY => ForceId.Friend,
            eForceIdentifier.FORCE_OPPOSING => ForceId.Hostile,
            _                               => ForceId.Neutral,
        };
    }
}
