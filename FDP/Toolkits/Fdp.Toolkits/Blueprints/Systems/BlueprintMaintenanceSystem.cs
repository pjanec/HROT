using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Handles Blueprint tier upgrades (BeforeSync phase).
/// When an entity holds both the smaller and larger tier components simultaneously,
/// this system copies state from the smaller to the larger and removes the smaller.
/// Per Runtime DD §7 + InlinePatches Q-12.3.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class BlueprintMaintenanceSystem : IEcsModuleSystem, IProfiledSystem
{
    // ⭐ O3a / B3: one query per ADJACENT tier pair, built from BlueprintTierTable.AdjacentPairs
    //   and index-aligned with it. ⛔ Was two named fields and two ~18-line copied methods; a 4th
    //   tier made it three of each.
    private EntityQuery[]? _upgradeQueries;

    public string ProfileName => "BlueprintMaintenanceSystem";

    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;

        var pairs = BlueprintTierTable.AdjacentPairs;
        if (_upgradeQueries is null)
        {
            _upgradeQueries = new EntityQuery[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                var (from, to) = pairs[i];
                // ⚠ "Holds BOTH tiers" is the promotion signal, exactly as the two hand-written
                //   queries expressed it: something added the larger component and left the smaller.
                _upgradeQueries[i] = to.Constrain(from.Constrain(repo.Query())).Build();
            }
        }

        // ⭐ Smallest pair first — the order the two named calls ran in, and it is load-bearing:
        //   a 1024→4096 promotion in this same pass can make the entity eligible for 4096→16384,
        //   and running ascending lets that cascade complete within one frame, as it did before.
        for (int i = 0; i < pairs.Count; i++)
            UpgradePair(repo, pairs[i].From, pairs[i].To, _upgradeQueries[i]);
    }

    /// <summary>
    /// ⛔⛔ <c>CopyToLargerTier</c> is where <c>H1</c> lives — it copies the header's
    /// <c>Reserved</c>, which since <c>A3</c> carries the per-slot <c>Kind</c> nibble array. Rail
    /// <c>A3_R2</c> pins it, and this method must keep going THROUGH that helper.
    /// </summary>
    private unsafe void UpgradePair(
        EntityRepository repo, BlueprintTierSpec from, BlueprintTierSpec to, EntityQuery query)
    {
        foreach (var entity in query)
            BlueprintTierTable.Promote(repo, from: from, to: to, entity: entity);
    }
}
