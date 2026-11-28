using Fdp.Testing;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ Player Join §7c (R-4115) / R-4102 — where the gameplay packs are composed, scanned the way <see cref="HudFeaturesAreWiredOnEveryHostTests"/> scans the hosts: CGF composes the server pack (over DDS, with an election), the Player (IG)
/// the client pack, the SimHost neither (the military-sim core never carries gameplay) and ⭐ CE-11027 S12-4 the EDITOR the server pack too — LOCAL (the in-process hub), with NO election (it serves alone) and no client pack. ✅ Red-proof: a composition line moved to (or added on) another host ⇒ red.
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
    public void CgfComposesTheServerPack_PlayerTheClientPack_SimHostNeither_EditorTheLocalServerPackWithNoElection()
    {
        var cgf = Host("CgfNodeBootstrapper.cs"); var player = Host("PlayerNodeBootstrapper.cs");
        var simhost = Host("SimHostNodeBootstrapper.cs"); var editor = Host("EditorNodeBootstrapper.cs");

        Assert.Contains("new Hrot.Gameplay.GameplayServerPack(", cgf);
        Assert.DoesNotContain("local: true", cgf);                       // the cluster's server speaks DDS
        Assert.DoesNotContain("GameplayClientPack", cgf);
        Assert.Contains("new Hrot.Gameplay.GameplayClientPack(", player);
        Assert.DoesNotContain("GameplayServerPack", player);
        Assert.DoesNotContain("GameplayServerPack", simhost);
        Assert.DoesNotContain("GameplayClientPack", simhost);

        // ⭐ S12-4 (Q114 B): the editor serves — alone (no election: networkless, so no second server can exist) and over the local hub, with its own creation pack
        var line = editor.Split((char)10).Single(l => l.Contains("new Hrot.Gameplay.GameplayServerPack(")).Split("//")[0];
        Assert.Contains("local: true", line);
        Assert.Contains("creation: () => EntityCreation", line);
        Assert.DoesNotContain("election", line);
        Assert.DoesNotContain("GameplayClientPack", editor);
    }
}
