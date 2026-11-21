using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐ Node Features F-17 — <c>map3d.in-load</c> (R-1046 / CE-3354 ③), the first feature: this node's LOAD also waits for its built 3-D map, which then
/// builds eagerly from the terrain commit. AtLoad. ON by default for the Player (the IG, <see cref="NodeRole.Map2D"/>), OFF for every other host that supplies a
/// map (CGF, SimHost, the editor), where an operator names the nodes. Declared by each host that supplies a map status (<c>SharedApplicationBootstrapper.HostFeatures</c>).
/// </summary>
public static class Map3dInLoadFeature
{
    public const string Id = MapInLoadOverrides.FeatureId;

    public static FeatureDeclaration Declaration(bool onByDefault)
        => FeatureDeclaration.Switch(Id, "The load waits for this node's built 3-D map (it builds eagerly)", FeatureChange.AtLoad, _ => onByDefault);

    /// <summary>True when the node has this feature and it is on now (frozen by the current load).</summary>
    public static bool IsOn(IFeatureGate? gate)
        => gate is NodeFeatures f && f.Registry.Available(Id) && f.On(Id);

    /// <summary>The boot-time answer (defaults ▸ <c>--feature</c> of this process), for the one decision taken before the load freezes anything: the map's build policy.</summary>
    public static bool OnAtBoot(NodeRole role, bool onByDefault)
    {
        var f = new NodeFeatures(new FeatureRegistry(new[] { Declaration(onByDefault) }), role);
        foreach (var (id, json) in NodeFeatureOverrides.FromCommandLine)
            if (id == Id) f.SetNodeOverride(id, json);
        return f.On(Id);
    }
}
