using CarKinem.Road;
using Fdp.Interfaces;
using Hrot.Map.Common.Services;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ CE-4013 (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ae T2) — HROT's providers for the two UNIVERSAL load parts
/// (<see cref="RoleLoadRequirements.UniversalParts"/>: the knowledge base and the terrain), written ONCE. Every ECS host's load chain
/// starts from these; it used to be the same two lines hand-copied into five roots.
/// </summary>
public static class HrotUniversalLoadParts
{
    /// <summary>The knowledge-base and terrain steps, in that order, and the terrain residency behind the second.</summary>
    /// <param name="tkb">The node's catalogue; null ⇒ the built-in one (a scenario that names a TKB replaces it through the step).</param>
    /// <param name="localStagingRoot">The node's staging root.</param>
    /// <param name="roadNetworkHolder">The holder the residency publishes the terrain's road graph into.</param>
    /// <param name="content">The node's terrain content (<c>HrotNodeContext.TerrainContent</c>). ⛔ No default: a host that has a context
    /// passes its registry (the silent-default rule); only a root with no node context passes null.</param>
    public static (List<ILoadPartProvider> Parts, TerrainResidency Residency) Build(
        ITkbDatabase? tkb, string localStagingRoot, RoadNetworkHolder roadNetworkHolder, TerrainContentRegistry? content)
    {
        var residency = new TerrainResidency(localStagingRoot, roadNetworkHolder, content);
        var parts = new List<ILoadPartProvider>
        {
            new KnowledgeBaseLoadStep(tkb ?? HrotEnvironment.CreateTkb(), localStagingRoot),
            new TerrainLoadStep(residency, localStagingRoot),
        };
        return (parts, residency);
    }
}
