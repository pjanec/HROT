using Hrot.Map.Common;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ Player Join S2-a — what a placed DEPLOY ROLE (8815) is BORN with, in ONE place (the sibling of <see cref="MacroBirth"/>): a <c>DeployRole</c> for the Friend side with a rifleman-less default the author edits in Details
/// (side, person TKB type, name, max count). Called by <c>ScenarioSpawnAdapter.ArmPlacement</c> — the point commit every host passes.
/// </summary>
public static class GameplayBirth
{
    /// <summary>Adds the default <see cref="Hrot.Common.DeployRole"/> when <paramref name="tkbType"/> is a Deploy Role; never twice.</summary>
    public static void BornWith(long tkbType, List<object> components)
    {
        if (tkbType != TkbEntityTypes.DeployRole || components.Exists(c => c is Hrot.Common.DeployRole)) return;
        components.Add(new Hrot.Common.DeployRole { Side = Fdp.Core.ForceId.Friend, PersonTkbType = 0, Name = new Fdp.Core.FixedString32("Role"), MaxCount = 0 });
    }
}
