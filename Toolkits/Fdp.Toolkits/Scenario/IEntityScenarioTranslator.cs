using System.Collections.Generic;
using Fdp.Kernel;

namespace Fdp.Toolkit.Scenario
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

        /// <summary>
        /// Returns the DOM key names that this translator <em>produces</em> during
        /// <see cref="Extract"/> (i.e. the keys it writes into the entity's scenario node).
        /// <para>
        /// The serializer adds these names to the "translator-handled" set so the
        /// auto-serializer does not attempt to process them as ECS component type names —
        /// they are custom compound keys (e.g. <c>"OrdnanceDef"</c>) that only this
        /// translator understands.  Failing to declare output keys will cause a strict
        /// <see cref="System.InvalidOperationException"/> at deserialize time for any
        /// unrecognised key not excluded by some other mechanism.
        /// </para>
        /// </summary>
        /// <returns>
        /// Zero or more DOM key strings.  Return an empty collection (not <see langword="null"/>)
        /// when the translator's output keys happen to coincide with ECS type names and are
        /// therefore already covered by the consumed-component-mask exclusion.
        /// </returns>
        IEnumerable<string> GetOutputDomKeys() => System.Array.Empty<string>();
    }
}
