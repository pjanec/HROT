using System.Numerics;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests;

/// <summary>
/// C2 — the tile loader is a fake, and <b>announcing it on every round is the deliverable</b>.
///
/// <para>⭐ <c>R-133</c>: a capability reported present that silently no-ops is worse than an absent one.
/// The failure mode this guards is the "announce once at startup" shortcut — after the first round it is
/// indistinguishable from silence, which is exactly what the ruling forbids.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.6, §6, §9.8.
/// </summary>
public sealed class AnnouncingZoneTileLoaderTests
{
    [Fact]
    public void ItAnnouncesOnEVERYRound_NotOnlyTheFirst()
    {
        var loader = new AnnouncingZoneTileLoader();

        loader.Build(Vector2.Zero, new Vector2(10f, 10f), 0xAAAA);
        loader.Build(Vector2.Zero, new Vector2(10f, 10f), 0xBBBB);
        loader.Build(Vector2.Zero, new Vector2(10f, 10f), 0xCCCC);

        // ⭐ The round counter is the checkable proxy for "it spoke each time": the log call sits
        //   unconditionally beside this increment, with no first-time flag to short-circuit it.
        Assert.Equal(3, loader.Rounds);
    }

    [Fact]
    public void ItReportsSUCCESS_BecauseWithStaticTerrainTheZoneLoadIsGenuinelySatisfied()
    {
        // ⛔ NOT optimism. The postcondition is "the terrain covering this zone is resident", and with
        //    static terrain all of it already is ⇒ satisfied immediately. Returning false would mark
        //    every zone Failed on every node and make the map lie in the other direction.
        var loader = new AnnouncingZoneTileLoader();

        Assert.True(loader.Build(new Vector2(-100f, -100f), new Vector2(100f, 100f), 0x1234));
    }

    [Fact]
    public void TheSameCoverageForTwoDifferentZones_IsBuiltBothTimes_BecauseTilesAreNotZoneOwned()
    {
        // ⭐ §9.8 — tile residency is keyed by GEOGRAPHY, not by zone. The stub must not quietly
        //   introduce a per-zone tile cache, or the real implementation inherits zone-owns-its-tiles and
        //   shrinking zone A would evict tiles zone B still needs.
        var loader = new AnnouncingZoneTileLoader();
        var min = new Vector2(0f, 0f);
        var max = new Vector2(50f, 50f);

        Assert.True(loader.Build(min, max, footprintHash: 0x1111));
        Assert.True(loader.Build(min, max, footprintHash: 0x2222));

        Assert.Equal(2, loader.Rounds);
    }
}
