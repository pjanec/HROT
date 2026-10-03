using System.Collections.Generic;
using System.Linq;
using CarKinem.Core;
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
/// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2, §5.5 D-4, build steps S1 and S3. The grant is a contract
/// every network implementation honours (R-165); this is how NED honours it.
/// <list type="bullet">
///   <item>⭐ Every descriptor that carries a group member is mapped HERE, role-independently — so every node binds
///     the SAME groups whatever translators its role composed (a Muscle node has no NavigationIntent egress, but it
///     must still know that granting <c>dtNavigationIntent</c> moves the brain).</item>
///   <item>Five of them declared no components before (design §3 F-2): granting or transferring them moved no
///     claim.</item>
///   <item>The group anchors (D-4) carry each group's never-sent members.</item>
///   <item>⭐ S3 — this is the ONE source of each role's descriptors: <see cref="GroupDescriptors"/> is what the
///     creator's grant strategy hands out, and a rail asserts it equals every node's live binding.</item>
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

    private static IReadOnlyDictionary<NodeRole, IReadOnlyList<long>>? _groupDescriptors;

    /// <summary>
    /// ⭐ S3 — per role, the descriptors its group binds to (empty for an empty group). Computed once from
    /// <see cref="Apply"/> over an empty map; the descriptor-map rails assert it equals the live binding on every
    /// node role, so the strategy and the nodes cannot disagree.
    /// </summary>
    public static IReadOnlyDictionary<NodeRole, IReadOnlyList<long>> GroupDescriptors
        => _groupDescriptors ??= ComputeGroupDescriptors();

    private static IReadOnlyDictionary<NodeRole, IReadOnlyList<long>> ComputeGroupDescriptors()
    {
        var map = new DescriptorOwnershipMap();
        Apply(map);
        var result = new Dictionary<NodeRole, IReadOnlyList<long>>();
        foreach (var role in HrotOwnershipGroups.Table.Groups.Keys)
            result[role] = map.DescriptorsOf(role).ToArray();
        return result;
    }

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

        // MuscleGround group descriptors (moved here from NedReplicationModule in S3).
        // dtWorldPos is the entire physical/kinematic authority block (R-170: it moves WHOLE). The
        // GeoSpatialIngressTranslator writes NetworkTransform, but the authoritative components on the Muscle side are
        // SimTransform + SimVelocity plus the CarKinem state CarKinematicsSystem writes. DeferredTakeoverSystem uses
        // this mapping to claim them when a Muscle receives the grant. One call: a second would overwrite the first.
        map.RegisterMapping(
            (long)EDescriptorType.dtWorldPos,
            ComponentType<SimTransform>.ID,
            ComponentType<SimVelocity>.ID,
            ComponentType<VehicleState>.ID,
            ComponentType<VehicleParams>.ID,
            ComponentType<NavState>.ID);
        // dtNavigationStatus → NavigationStatus. The Brain's NavigationStatusIngressTranslator declares no target
        // components, so without this OwnershipIngressSystem on the Brain would not clear NavigationStatus authority
        // when a Muscle takes dtNavigationStatus.
        map.RegisterMapping((long)EDescriptorType.dtNavigationStatus, NavigationContractsComponentIds.NavigationStatus);

        map.BindGroups(HrotOwnershipGroups.Table, Anchors);

        foreach (var violation in map.GroupBindingViolations)
            FdpLog<DescriptorOwnershipMap>.Error(
                "[Node-{0}] Ownership group binding: {1}", localNodeId, violation);
    }
}
