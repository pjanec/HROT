using System.Text.Json.Nodes;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Orchestration;
using Hrot.Tools.ContentMigrate;
using Xunit;

namespace Hrot.Tools.ContentMigrate.Tests;

/// <summary>
/// ⭐ S0 item 9 (<c>docs/DESIGN_Scenario_Rules.md</c> §9.8.11; CE-4153 S10 "no backward compatibility") — the content migration moves each
/// retired params form to its S0 form by the ENGINE's type plan, a second run moves nothing, and the repository's authored content is
/// migrated AND loads through the scenario load's own reader (which now refuses an old form).
/// </summary>
public sealed class ContentMigrationTests
{
    private static readonly string Repo = ContentLayout.TryFindSourceTree(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("the source tree (HROT.sln) was not found above the test output");

    private static readonly ParamsMigrator Migrator = ParamsMigrator.ForRepository(Repo);

    // every terrain in assets/terrain declares this origin
    private static readonly WGS84Transform Berlin = new(52.52, 13.405, 0);

    private static JsonObject Migrate(string behavior, string json)
    {
        var migrated = Migrator.MigrateParams(behavior, json, () => Berlin, out _);
        Assert.NotNull(migrated);
        Assert.Null(Migrator.MigrateParams(behavior, migrated, () => Berlin, out _));   // ⭐ idempotent: a second run moves nothing
        Assert.Null(Migrator.LoadRefusal(behavior, migrated));                          // ⭐ and the result loads
        return JsonNode.Parse(migrated!)!.AsObject();
    }

    private static string Local(double lat, double lon)
    {
        var v = Berlin.ToCartesian(lat, lon, 0.0);
        return new JsonArray(JsonValue.Create(Math.Round(v.X, 2)), JsonValue.Create(Math.Round(v.Y, 2)), JsonValue.Create(0)).ToJsonString();
    }

    [Fact]
    public void MoveToLocation_TargetLatLon_BecomesTheLocalTarget_ThroughTheTerrainOrigin()
    {
        var o = Migrate("MoveToLocation", "{\"targetLat\":52.5227,\"targetLon\":13.4122,\"speed\":5,\"arrivalRadius\":5}");
        Assert.Equal(Local(52.5227, 13.4122), o["target"]!.ToJsonString());
        Assert.False(o.ContainsKey("targetLat"));
        Assert.False(o.ContainsKey("targetLon"));
        Assert.Equal(5, (int)o["speed"]!);
        Assert.NotNull(Migrator.LoadRefusal("MoveToLocation", "{\"targetLat\":52.5,\"targetLon\":13.4}"));   // ⛔ the old form does NOT load
    }

    [Fact]
    public void ANestedMoveToLocation_InABlueprintsParams_MovesItsLocalXY_IntoTarget()
    {
        // Demo_MissionPlan's contract nests two MoveToLocation params (scenarios/mission-demo-bp)
        var o = Migrate("Demo_MissionPlan", "{\"Move\":{\"x\":600,\"y\":427,\"speed\":5},\"Home\":{\"x\":446.3,\"y\":420.9,\"speed\":5}}");
        Assert.Equal("[600,427,0]", o["Move"]!["target"]!.ToJsonString());
        Assert.Equal("[446.3,420.9,0]", o["Home"]!["target"]!.ToJsonString());
        Assert.False(o["Move"]!.AsObject().ContainsKey("x"));
    }

    [Fact]
    public void APlaceWrittenAsLatLon_BecomesLocalMetres_AnImpossibleLatLonIsTheOldSwappedLocalFallback_AndXYZIsUntouched()
    {
        Assert.Equal(Local(52.5227, 13.4122), Migrate("AirPlatformStrike", "{\"Location\":[52.5227,13.4122]}")["Location"]!.ToJsonString());
        // [300, 600] is no latitude: the editor's no-transform fallback wrote it as [y, x] — OrderGeo.ToLocal read it as x = 600, y = 300
        Assert.Equal("[600,300,0]", Migrate("AirPlatformStrike", "{\"Location\":[300,600]}")["Location"]!.ToJsonString());
        Assert.Null(Migrator.MigrateParams("AirPlatformStrike", "{\"Location\":[1,2,3]}", () => Berlin, out _));
    }

    [Fact]
    public void TheRepositorysAuthoredContent_IsMigrated_AndEveryParamsBlobLoads()
    {
        var problems = new List<string>();
        foreach (var file in ParamsMigrator.JsonFiles(ParamsMigrator.ContentRoots(Repo)))
        {
            var r = Migrator.MigrateFile(file, write: false);
            string rel = Path.GetRelativePath(Repo, file);
            problems.AddRange(r.Changes.Select(c => $"{rel}: not migrated — {c}"));
            problems.AddRange(r.Refusals.Select(c => $"{rel}: does not load — {c}"));
        }
        Assert.True(problems.Count == 0, "run hrot-content-migrate:\n" + string.Join("\n", problems));
    }
}
