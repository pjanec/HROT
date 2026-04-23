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
}
