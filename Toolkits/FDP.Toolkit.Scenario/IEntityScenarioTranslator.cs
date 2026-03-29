using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Custom translator that handles N ECS component types → M scenario DOM entries (N:M mapping).
    /// Registered with <see cref="ScenarioSerializerBuilder"/> before calling
    /// <see cref="ScenarioSerializerBuilder.Build"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="ScenarioSerializer"/> runs every registered translator before the
    /// fallback <c>FdpAutoSerializer</c>.  After <see cref="Extract"/> returns, the bits
    /// reported by <see cref="GetConsumedComponentsMask"/> are cleared from the entity's
    /// remaining serialization mask so the auto-serializer does not emit duplicate entries.
    /// </remarks>
    public interface IEntityScenarioTranslator
    {
        /// <summary>
        /// Returns a bitmask identifying every ECS component type ID that this translator
        /// will consume during <see cref="Extract"/>/<see cref="Inject"/>.
        /// The serializer clears these bits from the per-entity mask after
        /// <see cref="Extract"/> runs so the auto-serializer fallback skips them entirely.
        /// </summary>
        BitMask256 GetConsumedComponentsMask();

        /// <summary>
        /// Returns <see langword="true"/> when this translator can handle the given entity.
        /// Checked before <see cref="Extract"/> is called.
        /// </summary>
        bool CanTranslate(EntityRepository repo, Entity entity);

        /// <summary>
        /// Extracts scenario data from the entity and returns it as a dictionary keyed by
        /// the desired scenario component name (one or more entries per call).
        /// <para>
        /// Volatile <see cref="Entity"/> cross-references must be converted to stable GUID
        /// strings via <paramref name="guidResolver"/>.
        /// </para>
        /// </summary>
        Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver);

        /// <summary>
        /// Reconstitutes ECS components on <paramref name="entity"/> from the
        /// <paramref name="scenarioData"/> entries that were produced by <see cref="Extract"/>.
        /// <para>
        /// GUID strings must be resolved back to live <see cref="Entity"/> handles via
        /// <paramref name="guidResolver"/>.
        /// </para>
        /// </summary>
        void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver);
    }
}
