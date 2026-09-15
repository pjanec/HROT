using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Validates whether an alias drop would create an unsafe cross-region write
/// conflict, per BB SS9.4.
/// </summary>
public static class BlackboardAliasDropValidator
{
    /// <summary>
    /// Returns true if adding the given alias binding to the given variable
    /// would introduce a cross-region write conflict (BB SS9.4).
    /// The check is HSM-specific: BTree assets have no parallel regions.
    /// </summary>
    public static bool WouldCreateCrossRegionConflict(
        IBlackboardManagedAsset asset,
        string variableName,
        BlackboardAliasBinding newBinding,
        IReadOnlyDictionary<Guid, int>? regionIndexByStateId)
    {
        // Fast exit: no region map means no parallel structure.
        if (regionIndexByStateId == null || regionIndexByStateId.Count == 0) return false;

        // Fast exit: new binding's element is not in any parallel region.
        if (!regionIndexByStateId.TryGetValue(newBinding.RequiringElementId, out int newRegion))
            return false;

        var existing = asset.GetAliasesFor(variableName);
        foreach (var b in existing)
        {
            if (b.RequiringAssetId != newBinding.RequiringAssetId) continue; // different asset
            if (!regionIndexByStateId.TryGetValue(b.RequiringElementId, out int existingRegion))
                continue;
            
            if (existingRegion != newRegion)
            {
                // Conflict! Check suppression. ⭐ W7a: the key is built by BlackboardConflictKey so
                //    this surface and HsmValidator's rule 9 cannot key the same pair differently —
                //    which would silence one and not the other, with nothing failing anywhere.
                var writerPairKey = BlackboardConflictKey.ForWriterPair(
                    newBinding.RequiringElementId, b.RequiringElementId);
                if (asset.IsConflictSuppressed(variableName, writerPairKey))
                    continue;

                return true; 
            }
        }
        return false;
    }
}
