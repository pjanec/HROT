using System;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests;

/// <summary>
/// B5 / B6 — the terrain NAME in the scenario header, and the terrain DEFINITION asset + its singleton.
///
/// <para>⭐ The absence half is asserted at the gate the engine really applies. For the singleton that is
/// <c>ComponentTypeRegistry.IsRecordable</c>, because <c>RecorderSystem.RecordSingletons</c> walks the
/// singleton tables and records every one whose type is recordable — so <c>NoReplay</c> here is
/// load-bearing, not decoration.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ① ①a ②.
/// </summary>
public sealed class TerrainDefinitionTests
{
    // ── B5: the header carries the terrain NAME, independently of TkbName ─────────────────

    [Fact]
    public void Serialize_WithTerrainName_WritesItIntoTheHeader()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build()
            .Serialize(repo, new ScenarioHeader("Hrot.SimHost", TerrainName: "desert/kandahar"));

        Assert.Equal("desert/kandahar", dom["Header"]!["TerrainName"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WithTerrainNameButNoTkb_StillWritesTheHeader()
    {
        // ⛔ THE REGRESSION THIS PINS: the Header node used to exist only when TkbName was set, so a
        //    terrain-only scenario would have had its terrain name silently dropped.
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build()
            .Serialize(repo, new ScenarioHeader("Hrot.SimHost", TerrainName: "kandahar"));

        Assert.NotNull(dom["Header"]);
        Assert.Equal("kandahar", dom["Header"]!["TerrainName"]!.GetValue<string>());
        Assert.Null(dom["Header"]!["TkbName"]);
    }

    [Fact]
    public void Serialize_WithBothNames_CarriesBoth()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build()
            .Serialize(repo, new ScenarioHeader("Hrot.SimHost", TkbName: "Alpha_v1", TerrainName: "kandahar"));

        Assert.Equal("Alpha_v1", dom["Header"]!["TkbName"]!.GetValue<string>());
        Assert.Equal("kandahar", dom["Header"]!["TerrainName"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WithNeitherName_OmitsTheHeaderEntirely()
    {
        // Graceful absence: a scenario with no opinion about terrain is legal and still loads.
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build()
            .Serialize(repo, new ScenarioHeader("Hrot.SimHost"));

        Assert.Null(dom["Header"]);
    }

    // ── B6: the definition asset parses, and the singleton is in NEITHER output ───────────

    private const string OneRoadNetwork = """
        {
          "schemaVersion": 1,
          "name": "kandahar",
          "roadNetworks": [ "roads/main.net.xml" ]
        }
        """;

    [Fact]
    public void Parse_ADefinitionListingOneRoadNetwork_ProducesIt()
    {
        var def = TerrainDefinitionParser.Parse(OneRoadNetwork);

        Assert.Equal(1, def.SchemaVersion);
        Assert.Equal("kandahar", def.Name);
        Assert.Equal(new[] { "roads/main.net.xml" }, def.RoadNetworks);
        Assert.False(def.IsEmpty);
    }

    [Fact]
    public void Parse_ADefinitionDeclaringNoContent_IsValidAndEmpty()
    {
        var def = TerrainDefinitionParser.Parse("""{ "schemaVersion": 1, "name": "bare" }""");

        Assert.True(def.IsEmpty);
        Assert.Equal("bare", def.Name);
    }

    [Theory]
    [InlineData("")]                                              // empty
    [InlineData("not json")]                                      // unparseable
    [InlineData("[1,2,3]")]                                       // not an object
    [InlineData("""{ "name": "no version" }""")]                  // missing versioned root
    [InlineData("""{ "schemaVersion": 99, "name": "future" }""")] // newer than this build
    public void Parse_RejectsAMalformedOrUnsupportedDefinition_LoudlyRatherThanAsEmpty(string json)
    {
        // ⭐ An unreadable terrain must not degrade into "this terrain provides nothing" — the two are
        //   indistinguishable downstream, and loading the scenario's terrain is mandatory.
        Assert.Throws<ArgumentException>(() => TerrainDefinitionParser.Parse(json));
    }

    [Fact]
    public void TerrainDefinitionSingleton_IsInNeitherTheScenarioNorTheRecording()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        // ⚠⚠ THE REGISTRATION IS LOAD-BEARING, NOT CEREMONY — see
        //    SettingTheSingletonWithoutRegistering_SilentlyIgnoresTheDataPolicy below. This is why
        //    HrotSharedComponentRegistry registers the type on every ECS host.
        repo.RegisterManagedComponent<TerrainDefinition>();

        repo.SetSingletonManaged(TerrainDefinitionParser.Parse(OneRoadNetwork));
        Assert.True(repo.HasSingletonManaged<TerrainDefinition>(), "precondition: the singleton is set");

        int id = ManagedComponentType<TerrainDefinition>.ID;

        // ⭐ The RECORDING gate: RecorderSystem.RecordSingletons skips a table whose type is not
        //   recordable. Without [DataPolicy(NoReplay)] the parsed definition would be written out.
        Assert.False(ComponentTypeRegistry.IsRecordable(id),
            "TerrainDefinition is [DataPolicy(NoReplay)] ⇒ RecordSingletons must skip it: it is "
          + "re-derived from the named asset on every load and must never be replayed from a file.");

        // ⭐ The SCENARIO gate: it is not saveable, and the serializer walks entities in any case.
        Assert.False(ComponentTypeRegistry.IsSaveable(id),
            "TerrainDefinition is [DataPolicy(NoScenario)] ⇒ never written into a scenario.");

        var json = new ScenarioSerializerBuilder("Hrot.SimHost").Build()
            .Serialize(repo, new ScenarioHeader("Hrot.SimHost", TerrainName: "kandahar"))
            .ToJsonString();

        // The NAME rides in the header; the parsed CONTENT does not appear anywhere.
        Assert.Contains("kandahar", json);
        Assert.DoesNotContain("TerrainDefinition", json);
        Assert.DoesNotContain("roads/main.net.xml", json);
    }

    /// <summary>
    /// 🔴 ENGINE HAZARD, measured — <c>[DataPolicy]</c> on a managed type is applied by
    /// <c>RegisterManagedComponent</c> and by nothing else. <c>SetSingletonManaged</c> auto-registers
    /// through <c>ManagedComponentType&lt;T&gt;.ID</c>, which does NOT read the attribute, so the type
    /// keeps the registry defaults (recordable = saveable = TRUE).
    ///
    /// <para>⇒ a managed singleton set without a prior registration is <b>recorded into the flight
    /// recorder despite carrying <c>NoReplay</c></b>, silently. This rail exists so the day someone
    /// removes the registration from the shared registry, THIS fails loudly and names the reason,
    /// instead of the policy quietly ceasing to apply.</para>
    ///
    /// ⚠ The assertion below documents CURRENT ENGINE BEHAVIOUR, which is a defect, not the behaviour we
    /// want. If the engine is fixed so auto-registration honours the attribute, this test SHOULD fail —
    /// delete it then, and keep the one above.
    /// </summary>
    [Fact]
    public void SettingTheSingletonWithoutRegistering_SilentlyIgnoresTheDataPolicy()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();

        // NOTE: no RegisterManagedComponent<TerrainDefinition>() here — that is the whole point.
        repo.SetSingletonManaged(TerrainDefinitionParser.Parse(OneRoadNetwork));

        int id = ManagedComponentType<TerrainDefinition>.ID;

        Assert.True(ComponentTypeRegistry.IsRecordable(id),
            "MEASURED 2026-09-17: the attribute is ignored on the auto-registration path, so the type "
          + "defaults to recordable. If this now fails, the engine has been fixed — delete this rail.");
    }

    // ── CE-3126 (R-229): the geo origin is part of the terrain file ────────────────────────────

    [Fact]
    public void CE3126_Parse_ReadsTheOrigin_AbsentIsNull_MalformedThrows()
    {
        var d = TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":{"lat":48.1,"lon":-11.5,"alt":420}}""");
        Assert.Equal(new TerrainGeoOrigin(48.1, -11.5, 420), d.Origin);

        Assert.Equal(new TerrainGeoOrigin(1, 2, 0), TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":{"lat":1,"lon":2}}""").Origin);
        Assert.Null(TerrainDefinitionParser.Parse("""{"schemaVersion":2}""").Origin);   // ⛔ no default — zeros downstream

        Assert.Throws<ArgumentException>(() => TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":{"lat":1}}"""));
        Assert.Throws<ArgumentException>(() => TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":[1,2]}"""));
        Assert.Throws<ArgumentException>(() => TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":{"lat":91,"lon":0}}"""));
    }

    /// <summary>⭐ The shipped terrains carry their origin as data (Berlin — the scenarios on them store Berlin lat/lon).</summary>
    [Theory]
    [InlineData("test-town")]
    [InlineData("bt-range")]
    [InlineData("basic-desert")]
    public void CE3126_EveryShippedTerrain_DeclaresItsOrigin(string terrain)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "Hrot", "Subsystems")))
            dir = System.IO.Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        var path = System.IO.Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", terrain, "terrain.json");
        var d = TerrainDefinitionParser.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(new TerrainGeoOrigin(52.52, 13.405, 0.0), d.Origin);
    }

    /// <summary>
    /// ⭐ A transform starts at 0,0,0 with VALID matrices (before CE-3126 a bare instance mapped everything to the origin), and
    /// switching the origin moves the same local point to a different lat/lon while the round trip stays exact.
    /// </summary>
    [Fact]
    public void CE3126_WGS84Transform_StartsAtZero_AndSwitchesOrigin()
    {
        var t = new Fdp.Modules.Geographic.Transforms.WGS84Transform();
        Assert.Equal((0.0, 0.0, 0.0), t.Origin);
        var local = new System.Numerics.Vector3(489f, 296f, 0f);
        var (lat0, lon0, _) = t.ToGeodetic(local);
        Assert.InRange(lat0, 0.0026, 0.0028);
        Assert.InRange(lon0, 0.0043, 0.0045);

        t.SetOrigin(52.52, 13.405, 0.0);
        var (lat, lon, alt) = t.ToGeodetic(local);
        Assert.InRange(lat, 52.522, 52.523);
        var back = t.ToCartesian(lat, lon, alt);
        Assert.True(System.Numerics.Vector3.Distance(back, local) < 0.05f, $"{back}");
        Assert.Equal(52.52, t.Origin.lat, 9);
    }
}
