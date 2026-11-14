using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests;

/// <summary>
/// B2 / B3 — the terrain load MARKER and the footprint HASH.
///
/// <para>⭐⭐ <b>These rails prove ABSENCE, which is the whole point of the marker's policy.</b>
/// "It has the attribute" is NOT the rail: an attribute that nothing consults is exactly the
/// silently-no-op capability that is worse than an absent one. So each half is asserted at the gate the
/// engine actually applies — <c>GetSaveableMask</c> for scenario save and <c>GetRecordableMask</c> for
/// the .fdp recording — and the scenario half is additionally proven end-to-end through the real
/// serializer.</para>
///
/// <para>⚠ The two flags are INDEPENDENT (a component may be dropped from one and kept in the other),
/// so both are asserted separately rather than inferred from one another.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2, §9.1, §9.7 ③c.
/// </summary>
public sealed class TerrainAssetLoadStateTests
{
    /// <summary>Control component: no policy ⇒ saveable AND recordable. Without it, an assertion that
    /// the marker is absent could be satisfied by everything being absent.</summary>
    [ComponentId(232)]
    private struct PlainMarkerControl { public float X; }

    // ── B2: the marker is in NEITHER output ──────────────────────────────────────────────

    [Fact]
    public void TerrainAssetLoadState_IsExcludedFromBothScenarioSaveAndRecording()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<TerrainAssetLoadState>();   // [DataPolicy(NoScenario | NoReplay)]
        repo.RegisterComponent<PlainMarkerControl>();      // no policy → both

        int markerId = ComponentTypeRegistry.GetId(typeof(TerrainAssetLoadState));
        int plainId  = ComponentTypeRegistry.GetId(typeof(PlainMarkerControl));

        var saveable   = repo.GetSaveableMask();     // scenario JSON   (NoScenario excluded)
        var recordable = repo.GetRecordableMask();   // .fdp recording  (NoReplay  excluded)

        Assert.False(saveable.IsSet(markerId),
            "TerrainAssetLoadState is [DataPolicy(NoScenario)] ⇒ must never reach a saved scenario: it is "
          + "node state keyed by entity, and nodes legitimately disagree about it.");
        Assert.False(recordable.IsSet(markerId),
            "TerrainAssetLoadState is [DataPolicy(NoReplay)] ⇒ must never reach a recording: it is "
          + "re-derivable by re-running the load, so recording it would replay one node's cache state.");

        // ⭐ Anti-vacuity: the exclusions above are the FLAGS, not a blanket drop of everything.
        Assert.True(saveable.IsSet(plainId));
        Assert.True(recordable.IsSet(plainId));
    }

    [Fact]
    public void ScenarioSave_RoundTrip_OmitsTerrainAssetLoadState()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<TerrainAssetLoadState>();
        repo.RegisterComponent<PlainMarkerControl>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new PlainMarkerControl { X = 1f });
        repo.AddComponent(e, new TerrainAssetLoadState
        {
            Phase      = LoadPhase.Loaded,
            SourceHash = 0xDEADBEEFUL,
        });

        var json = new ScenarioSerializerBuilder("Hrot.Scenario").Build()
                       .Serialize(repo, new ScenarioHeader("Hrot.Scenario"))
                       .ToJsonString();

        Assert.Contains("PlainMarkerControl", json);
        Assert.DoesNotContain("TerrainAssetLoadState", json);
    }

    // ── B3: the footprint hash sees a MOVE and a RESHAPE, and needs no writer's help ──────

    private static List<Vector2> Triangle() => new()
    {
        new Vector2(-53f, -88.5f),
        new Vector2(47f, -88.5f),
        new Vector2(47f, 11.5f),
    };

    [Fact]
    public void Footprint_IsStable_ForTheSameOriginAndPoints()
    {
        var a = ZoneFootprint.Compute(new Vector3(670f, 473.5f, 0f), Triangle());
        var b = ZoneFootprint.Compute(new Vector3(670f, 473.5f, 0f), Triangle());

        Assert.Equal(a, b);
    }

    [Fact]
    public void Footprint_ChangesWhenTheZoneMOVES_EvenThoughPointsAreIdentical()
    {
        // ⭐⭐ THE CASE A COUNTER CANNOT SEE. Points are RELATIVE, so moving a zone leaves them
        //    byte-identical — only the transform moves. A points-only key is blind to this.
        var points = Triangle();

        var before = ZoneFootprint.Compute(new Vector3(670f, 473.5f, 0f), points);
        var after  = ZoneFootprint.Compute(new Vector3(770f, 473.5f, 0f), points);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Footprint_ChangesWhenTheZoneIsRESHAPED()
    {
        var origin = new Vector3(670f, 473.5f, 0f);

        var before = ZoneFootprint.Compute(origin, Triangle());

        var reshaped = Triangle();
        reshaped[2] = new Vector2(47f, 40f);          // drag one vertex
        var after = ZoneFootprint.Compute(origin, reshaped);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Footprint_ChangesWhenAVertexIsADDED()
    {
        var origin = new Vector3(670f, 473.5f, 0f);

        var before = ZoneFootprint.Compute(origin, Triangle());

        var grown = Triangle();
        grown.Add(new Vector2(-53f, 11.5f));
        var after = ZoneFootprint.Compute(origin, grown);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Footprint_TreatsNegativeZeroAsZero()
    {
        // -0f == 0f and denotes the same point, but has a different bit pattern. Hashing raw bits would
        // report a move that never happened.
        var a = ZoneFootprint.Compute(new Vector3(0f, 0f, 0f), Triangle());
        var b = ZoneFootprint.Compute(new Vector3(-0f, -0f, -0f), Triangle());

        Assert.Equal(a, b);
    }

    [Fact]
    public void Footprint_OfAShapelessZone_StillDependsOnItsOrigin()
    {
        // A zone that has lost its points must not collapse onto every other shapeless zone.
        var a = ZoneFootprint.Compute(new Vector3(10f, 0f, 0f), null);
        var b = ZoneFootprint.Compute(new Vector3(20f, 0f, 0f), null);
        var c = ZoneFootprint.Compute(new Vector3(10f, 0f, 0f), new List<Vector2>());

        Assert.NotEqual(a, b);
        Assert.Equal(a, c);   // null and empty are the same footprint: no shape either way
    }
}
