using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Common;
using DescriptorOwnershipMap = Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap;
using EDescriptorType        = Hrot.NED.Descriptors.EDescriptorType;

namespace Hrot.Network.Replication;

/// <summary>
/// ⭐⭐ <b>NED's binding of the network-agnostic ownership groups to its descriptors.</b>
/// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2, §5.5 D-4, build step S1. The grant is a contract every
/// network implementation honours (R-165); this is how NED honours it.
/// <list type="bullet">
///   <item>⭐ Every descriptor that carries a group member is mapped HERE, role-independently — so every node binds
///     the SAME groups whatever translators its role composed (a Muscle node has no NavigationIntent egress, but it
///     must still know that granting <c>dtNavigationIntent</c> moves the brain).</item>
///   <item>Five of them declared no components before (design §3 F-2): granting or transferring them moved no
///     claim.</item>
///   <item>The group anchors (D-4) carry each group's never-sent members.</item>
/// </list>
/// </summary>
public static class NedOwnershipGroupBinding
{
    /// <summary>D-4 — per role, the descriptor that carries the group's never-sent members.</summary>
    public static IReadOnlyDictionary<NodeRole, long> Anchors { get; } = new Dictionary<NodeRole, long>
    {
        [NodeRole.Brain]        = (long)EDescriptorType.dtNavigationIntent,
        [NodeRole.MuscleGround] = (long)EDescriptorType.dtWorldPos,
        [NodeRole.Perception]   = (long)EDescriptorType.dtEqsResult,
    };

    /// <summary>
    /// Registers the group descriptors and binds the groups. Call after every translator and explicit mapping is
    /// registered. Violations are logged as errors; the descriptor-map rails assert there are none.
    /// </summary>
    public static void Apply(DescriptorOwnershipMap map, long localNodeId = 0)
    {
        // Brain group descriptors.
        map.RegisterMapping((long)EDescriptorType.dtNavigationIntent, NavigationContractsComponentIds.NavigationIntent);
        map.RegisterMapping((long)EDescriptorType.dtEntityMission,
            GlobalComponentIds.MissionPlanQueue, BehaviorApplicationComponentIds.ActiveMissionPlan);
        map.RegisterMapping((long)EDescriptorType.dtSensorConfig,
            ComponentType<Fdp.Toolkit.Perception.Components.PerceptionReceptor>.ID);
        map.RegisterMapping((long)EDescriptorType.dtEqsSensorConfig, GlobalComponentIds.EqsSensor);
        map.RegisterMapping((long)EDescriptorType.dtEntityDamage, GlobalComponentIds.CombatHealth);

        // Perception group descriptor.
        map.RegisterMapping((long)EDescriptorType.dtEqsResult, GlobalComponentIds.EqsCognitiveBuffer);

        // MuscleGround: dtWorldPos (whole, R-170) and dtNavigationStatus are registered by the module itself.

        map.BindGroups(HrotOwnershipGroups.Table, Anchors);

        foreach (var violation in map.GroupBindingViolations)
            FdpLog<DescriptorOwnershipMap>.Error(
                "[Node-{0}] Ownership group binding: {1}", localNodeId, violation);
    }
}
