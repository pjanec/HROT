using Fdp.Testing;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ Player Join §7c (R-4115) / R-4102 — where the gameplay packs are composed, scanned the way <see cref="HudFeaturesAreWiredOnEveryHostTests"/> scans the hosts: CGF composes the server pack, the Player (IG)
/// the client pack, and the SimHost and the editor neither (the military-sim core never carries gameplay). ✅ Red-proof: a composition line moved to (or added on) another host ⇒ red.
/// </summary>
public sealed class GameplayPacksAreComposedOnTheRightHostsTests
{
    private static string Host(string file)
    {
        var root = TestRepo.Root;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "Hrot", "Subsystems"), file, SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests")) continue;
            return string.Join('\n', File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//")));
        }
        throw new FileNotFoundException(file);
    }

    [Fact]
    public void CgfComposesTheServerPack_PlayerTheClientPack_SimHostAndEditorNeither()
    {
        var cgf = Host("CgfNodeBootstrapper.cs"); var player = Host("PlayerNodeBootstrapper.cs");
        var simhost = Host("SimHostNodeBootstrapper.cs"); var editor = Host("EditorNodeBootstrapper.cs");

        Assert.Contains("new Hrot.Gameplay.GameplayServerPack(", cgf);
        Assert.DoesNotContain("GameplayClientPack", cgf);
        Assert.Contains("new Hrot.Gameplay.GameplayClientPack(", player);
        Assert.DoesNotContain("GameplayServerPack", player);
        foreach (var other in new[] { simhost, editor })
        {
            Assert.DoesNotContain("GameplayServerPack", other);
            Assert.DoesNotContain("GameplayClientPack", other);
        }
    }
}
