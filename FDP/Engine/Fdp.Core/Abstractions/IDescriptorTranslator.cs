using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Indicates which network phases a translator participates in.
    /// </summary>
    [System.Flags]
    public enum TranslatorDirection : byte
    {
        None          = 0,
        Ingress       = 1 << 0,
        Egress        = 1 << 1,
        Bidirectional = Ingress | Egress,
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Which traffic class a translator carries — the one genuinely new artefact of
    /// <c>DQ30-C</c>.</b>
    ///
    /// <para>📄 <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §C ·
    /// <c>docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md</c> §4.</para>
    ///
    /// <para>⛔⛔ <b>Why a per-translator category and not a per-system switch.</b> While a debugger
    /// holds a node's world frozen, world-state ingress must stop — brain state at tick T read
    /// against replicated state at T+k is the exact confusion a debugger exists to prevent — but
    /// control-plane ingress must keep polling, because <b>this node's own resume arrives through
    /// it</b>. 📐 Measured: a single ingress system can hold both classes at once (the auxiliary
    /// pack carries combat and mission-control traffic), so gating whole systems cannot express the
    /// split. Gating the control plane by accident is <c>DQ30-A</c>'s deadlock.</para>
    ///
    /// <para>🔒 <b><see cref="WorldState"/> is 0, i.e. the default, and that is the fail-safe
    /// direction.</b> A control-plane translator left unmarked fails LOUDLY and immediately —
    /// *"resume does not work"* — whereas the opposite default would leak live world data into a
    /// frozen snapshot SILENTLY.</para>
    /// </summary>
    public enum TranslatorClass : byte
    {
        /// <summary>Entity/component replication, descriptor, mission and intent ingress. Stops with the sim.</summary>
        WorldState   = 0,

        /// <summary>Time-mode, lockstep, time-sync and orchestration traffic. ⭐ Keeps polling while frozen.</summary>
        ControlPlane = 1,
    }

    /// <summary>
    /// Translates between network descriptors and ECS components.
    /// </summary>
    public interface IDescriptorTranslator : INetworkTranslator
    {
        /// <summary>
        /// Unique identifier for this descriptor type.
        /// </summary>
        long DescriptorOrdinal { get; }

        /// <summary>
        /// The ECS component type IDs that this translator reads or writes authority-gated data for.
        ///
        /// <para>
        /// This is the <em>Single Source of Truth</em> for the descriptor → component mapping.
        /// <c>NedReplicationModule</c> iterates all registered translators on startup and populates
        /// the <c>DescriptorOwnershipMap</c> from these IDs so that <c>OwnershipIngressSystem</c>
        /// can call <c>SetAuthority(entity, exactComponentId, bool)</c> without resorting to
        /// a try/catch on mismatched ordinal-to-component-ID casts.
        /// </para>
        ///
        /// <para>
        /// Translators that operate on managed components, or that are ingress-only with no
        /// direct authority gate, may return the default empty list.
        /// </para>
        /// </summary>
        IReadOnlyList<int> TargetComponentIds => System.Array.Empty<int>();

        /// <summary>
        /// Applies descriptor data to an entity (used during ghost promotion).
        /// </summary>
        void ApplyToEntity(Entity entity, object data, EntityRepository repo);

        void Dispose(long networkEntityId);
    }
}
