using System.Numerics;
using Fdp.Kernel;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Shadow of the last-published (or last-received) position and orientation.
    ///
    /// <para>
    /// On the <b>egress</b> (SimHost) side, <c>GeoSpatialEgressTranslator</c> compares the live
    /// <c>SimTransform</c> against this component every tick to decide whether to broadcast a
    /// <c>GeoSpatial</c> DDS message.  Only when the entity moves or rotates beyond the configured
    /// threshold — or when the heartbeat interval fires — is a packet sent.  This avoids the heap
    /// overhead of <see cref="EgressPublicationState"/> for high-frequency physics data.
    /// </para>
    ///
    /// <para>
    /// On the <b>ingress</b> (IG) side, <c>GeoSpatialIngressTranslator</c> writes the decoded
    /// network position and orientation into this component so that <c>SimTransform</c> can be
    /// interpolated toward the latest received state.
    /// </para>
    ///
    /// <para><b>Component ID = <see cref="GlobalComponentIds.NetworkTransform"/> (52).</b>
    /// It replaces the former <c>NetworkPosition</c> (also ID 52) and folds in rotation,
    /// saving one component-ID slot over maintaining separate position and rotation components.
    /// </para>
    /// </summary>
    [DataPolicy(DataPolicy.NoRecord)]
    [ComponentId(GlobalComponentIds.NetworkTransform)]
    public struct NetworkTransform
    {
        /// <summary>Last position that was sent to (or received from) the network.</summary>
        public Vector3 LastPosition;

        /// <summary>Last orientation that was sent to (or received from) the network.</summary>
        public Quaternion LastRotation;
    }
}
