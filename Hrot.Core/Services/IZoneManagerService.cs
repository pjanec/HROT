using System.Collections.Generic;
using Fdp.Kernel;
using Hrot.Map.Common.Scenario;

namespace Hrot.Map.Common.Services;

/// <summary>
/// Application-layer pivot that translates <see cref="ZoneDefinitionDto"/> data
/// into ECS state (road-network singleton + obstacle entities).
/// </summary>
public interface IZoneManagerService
{
    /// <summary>
    /// Loads all zones described in <paramref name="zones"/> into <paramref name="repo"/>.
    /// <list type="bullet">
    ///   <item>For each zone whose <c>RoadNetworkPath</c> is non-null, the road network
    ///         file is loaded and stored in the <c>ZoneEnvironmentData</c> singleton.
    ///         Any previously stored road network blob is disposed first (memory safety).</item>
    ///   <item>For each obstacle in each zone, an ECS entity with
    ///         <c>SimTransform</c> + <c>PhysicsCollider</c> is created.</item>
    /// </list>
    /// </summary>
    void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones);

    /// <summary>
    /// Returns the zones dictionary that was supplied to the most recent
    /// <see cref="LoadZones"/> call, or an empty dictionary if no zones have been loaded.
    /// </summary>
    Dictionary<string, ZoneDefinitionDto> GetActiveZones();
}
