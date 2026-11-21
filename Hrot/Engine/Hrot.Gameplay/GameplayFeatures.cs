using Hrot.Common.Infrastructure;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join D-3 / §7c (R-4115) — the feature <c>gameplay.deploy-server</c>: THIS node serves Deploy requests (and reads the player slots). Declared by
/// <see cref="GameplayServerPack"/> through <c>INodeCapability.Features</c> (declared ⇔ available, F-3); <c>Live</c>, ON by default — the pack is composed by
/// default (R-4115), so the feature is what an operator switches. The node advertises it as <c>hrot.feature.gameplay.deploy-server</c>; "a server exists" = a
/// node advertises it AND reports it ON.
/// </summary>
public static class GameplayFeatures
{
    public const string DeployServerId = "gameplay.deploy-server";

    /// <summary>The wire token a node advertises for the feature (<c>hrot.feature.&lt;id&gt;</c>).</summary>
    public static string DeployServerToken => FeatureRegistry.TokenOf(DeployServerId);

    public static readonly FeatureDeclaration DeployServer = FeatureDeclaration.Switch(DeployServerId,
        "This node serves Player Deploy requests (one server per cluster: the lowest node id with it on)", FeatureChange.Live, _ => true);
}
