using System.Collections.Generic;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Scenario;

namespace Hrot.Core.Network;

/// <summary>
/// Abstraction over the staging-based entity extraction pipeline.
///
/// <para>
/// The canonical implementation (<c>StagingEntityExtractor</c> in
/// <c>Hrot.CGF.Orchestration</c>) deserialises the scenario JSON into a transient
/// staging repository, allocates fresh network IDs, and packages every entity into
/// an <see cref="EntityCreationRequest"/> for the genesis pipeline.
/// </para>
/// <para>
/// Exposing this as an interface in <c>Hrot.Core</c> allows
/// <c>Hrot.Presentation.HrotEditLoadHandler</c> to depend on the extraction
/// contract without introducing a circular project reference to <c>Hrot.CGF</c>.
/// </para>
/// </summary>
public interface IScenarioEntityExtractor
{
    /// <summary>
    /// Extracts all root entities from the scenario JSON and returns a list of
    /// <see cref="EntityCreationRequest"/> objects ready for the genesis pipeline.
    /// </summary>
    /// <param name="serializer">Scenario serializer used to hydrate the staging
    ///   repository.</param>
    /// <param name="json">Raw scenario JSON.</param>
    /// <param name="idAllocator">Network-ID allocator used to pre-allocate IDs.</param>
    IReadOnlyList<EntityCreationRequest> Extract(
        ScenarioSerializer serializer,
        string json,
        INetworkIdAllocator idAllocator);

    /// <summary>
    /// ⭐⭐ <c>L4a</c> — the same extraction, with the host's optional BEHAVIOUR REMAPPER.
    ///
    /// <para>⭐ Added so the ONE <c>ScenarioLoadStep</c> can serve every host through this interface. 📐 The
    /// remapper patches network ids embedded in mission-plan parameters; before the step existed, only CGF
    /// passed one, and it could do so only by depending on the CONCRETE extractor — which is a large part
    /// of why CGF needed a handler of its own at all.</para>
    ///
    /// <para>⚠ The default implementation ignores the remapper, so an extractor that has no notion of one
    /// (a test double, a host that does not remap) needs no change and behaves exactly as before.</para>
    /// </summary>
    IReadOnlyList<EntityCreationRequest> Extract(
        ScenarioSerializer serializer,
        string json,
        INetworkIdAllocator idAllocator,
        Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? behaviorRemapper)
        => Extract(serializer, json, idAllocator);
}
