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
    /// Translates between network descriptors and ECS components.
    /// </summary>
    public interface IDescriptorTranslator
    {
        /// <summary>
        /// Unique identifier for this descriptor type.
        /// </summary>
        long DescriptorOrdinal { get; }
        
        /// <summary>
        /// DDS topic name.
        /// </summary>
        string TopicName { get; }

        /// <summary>
        /// Number of valid ingress samples consumed by this translator.
        /// Egress-only translators should return 0.
        /// </summary>
        long ReceivedSampleCount { get; }

        /// <summary>
        /// Number of samples published by this translator.
        /// Ingress-only translators should return 0.
        /// </summary>
        long SentSampleCount { get; }

        /// <summary>
        /// Declares which network phases this translator participates in.
        /// Must be explicitly implemented by every translator — no default is provided.
        /// </summary>
        TranslatorDirection Direction { get; }

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
        /// Processes incoming network data and updates ECS entities.
        /// </summary>
        void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
        
        /// <summary>
        /// Scans ECS entities and publishes updates to the network.
        /// </summary>
        void ScanAndPublish(ISimulationView view);
        
        /// <summary>
        /// Applies descriptor data to an entity (used during ghost promotion).
        /// </summary>
        void ApplyToEntity(Entity entity, object data, EntityRepository repo);

        void Dispose(long networkEntityId);
    }
}
