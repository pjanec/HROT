using Fdp.Testing;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-4148 (Node Features S2) — the silent-default rule for the HUD gate: a production caller that HAS a node (so a feature gate) must PASS it
/// to the map pack — <c>AttachMapLayers(… featureGate:)</c> for the station and <c>MapInteractionContext.FeatureGate</c> for the effect system's hit
/// memory — and each map host declares the <c>hud.*</c> features with the right default (ON for the Player/IG only).
/// The Replay Browser has no node (it replays recordings in sandbox worlds: no feature gate exists there) and is the one named exemption.
/// </summary>
public sealed class HudFeaturesAreWiredOnEveryHostTests
{
    private const string NoNodeHost = "Hrot/Subsystems/Hrot.ReplayBrowser";

    private static IEnumerable<(string Rel, string Text)> HostFiles()
    {
        var root = TestRepo.Root;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "Hrot", "Subsystems"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests")) continue;
            yield return (rel, string.Join('\n', File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//"))));
        }
    }

    private static string HostOf(string rel) => string.Join('/', rel.Split('/').Take(3));

    [Fact]
    public void EveryHostWithANode_PassesTheGateToTheStationAndTheEffectSystem()
    {
        var files = HostFiles().ToList();
        var attach = files.Where(f => f.Text.Contains("MapInteractionPack.AttachMapLayers(") && !HostOf(f.Rel).Equals(NoNodeHost)).ToList();
        Assert.True(attach.Count >= 4, "IG, CGF, Editor, SimHost attach the layers");
        foreach (var f in attach) Assert.True(f.Text.Contains("featureGate:"), $"{f.Rel} attaches the map layers without the node's feature gate");

        var build = files.Where(f => f.Text.Contains("MapInteractionPack.Build(") && !HostOf(f.Rel).Equals(NoNodeHost)).ToList();
        Assert.True(build.Count >= 4);
        foreach (var f in build) Assert.True(f.Text.Contains("FeatureGate = "), $"{f.Rel} builds the map interaction without the node's feature gate");
    }

    [Theory]
    [InlineData("Hrot.IG/IgNodeBootstrapper.cs", "true")]
    [InlineData("Hrot.CGF/CgfNodeBootstrapper.cs", "false")]
    [InlineData("Hrot.Editor/EditorNodeBootstrapper.cs", "false")]
    [InlineData("Hrot.SimHost/SimHostNodeBootstrapper.cs", "false")]
    public void EveryMapHost_DeclaresTheHudFeatures_OnByDefaultForThePlayerOnly(string file, string playerDefault)
    {
        var text = HostFiles().Single(f => f.Rel.EndsWith("/" + file)).Text;
        Assert.Contains($"HudFeatures.Declarations(playerDefault: {playerDefault})", text);
    }
}
