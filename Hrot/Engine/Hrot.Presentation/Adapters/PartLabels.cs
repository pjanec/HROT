using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// ⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views §4, AQ79 §0.10: <i>"the descriptor type is the part's kind"</i>) — what a nameless PART is, read from the
/// component its creator gave it, never from its <c>PartMetadata.InstanceId</c> (a per-descriptor index that says nothing about the kind).
/// The creators: a weapon mount (<c>WeaponMountInfo</c>), a turret (<c>TurretPose</c>), a perception sensor (<c>SensorTag</c> — its modality), an EQS sensor
/// (<c>EqsSensor</c>), a personal route (a part of the route tkb type). Anything else: <c>part #&lt;InstanceId&gt;</c>.
/// ⛔ The weapon's NAME is not shown: the mount carries only a weapon GUID and the catalogue is not reachable from here cheaply — the mount index is.
/// </summary>
public static class PartLabels
{
    /// <summary>The part's kind label (no parent name): <c>weapon mount 1</c>, <c>turret 0</c>, <c>sensor Radar</c>, <c>EQS sensor</c>, <c>personal route</c>, <c>part #7</c>.</summary>
    public static string Describe(ISimulationView view, Entity part)
    {
        if (Has<WeaponMountInfo>(view, part)) return $"weapon mount {view.GetComponentRO<WeaponMountInfo>(part).MountIndex}";
        if (Has<TurretPose>(view, part))
        {
            int instance = Has<PartMetadata>(view, part) ? view.GetComponentRO<PartMetadata>(part).InstanceId : -1;
            int index = TurretParts.IndexOf(instance);
            return index >= 0 ? $"turret {index}" : "turret";
        }
        if (Has<SensorTag>(view, part)) return $"sensor {view.GetComponentRO<SensorTag>(part).Kind}";
        if (Has<EqsSensor>(view, part)) return "EQS sensor";
        if (Has<TkbIdentity>(view, part) && view.GetComponentRO<TkbIdentity>(part).TkbType == (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.TacGraphic_Route)
            return "personal route";
        return Has<PartMetadata>(view, part) ? $"part #{view.GetComponentRO<PartMetadata>(part).InstanceId}" : "part";
    }

    /// <summary>The row text: <c>Parent · kind</c> (just the kind when the parent has no name).</summary>
    public static string Describe(ISimulationView view, Entity part, string? parentName)
        => string.IsNullOrEmpty(parentName) ? Describe(view, part) : $"{parentName} · {Describe(view, part)}";

    private static bool Has<T>(ISimulationView view, Entity e) where T : unmanaged
        => (view is not EntityRepository r || r.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);
}
