using Hrot.Editor;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.Tests;

public sealed class ScenarioNewAssetTests
{
    [Fact]
    public void Create_Empty_NewWorld_ThenSaveAs()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        var result = svc.CreateNew(null, "MyScenario", "Combat");

        // Verify NewScenario was called, then SaveScenarioAs with the full name.
        Assert.Equal(1, session.NewScenarioCallCount);
        var saveCall = Assert.Single(session.SaveScenarioAsCalls);
        Assert.Equal("Combat/MyScenario", saveCall);

        // Returned asset has correct metadata.
        Assert.Equal(AssetKind.Scenario, result.Kind);
        Assert.Equal("MyScenario", result.Name);
        Assert.NotEqual(Guid.Empty, result.AssetId);
    }

    [Fact]
    public void Create_Empty_NoRelPath_SavesWithNameOnly()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        svc.CreateNew(null, "FlatScenario", "");

        Assert.Equal(1, session.NewScenarioCallCount);
        var saveCall2 = Assert.Single(session.SaveScenarioAsCalls);
        Assert.Equal("FlatScenario", saveCall2);
    }

    [Fact]
    public void Create_FromSeed_LoadsSeedThenSaveAs()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "SeedScenario" });

        var seedRecipe = svc.AvailableRecipes().First(r => r.Name == "SeedScenario");
        var result = svc.CreateNew(seedRecipe, "NewName", "Sub");

        // Verify LoadScenarioByName was called with the seed name, then SaveScenarioAs.
        var loadCall = Assert.Single(session.LoadScenarioByNameCalls);
        Assert.Equal("SeedScenario", loadCall);
        var saveCall3 = Assert.Single(session.SaveScenarioAsCalls);
        Assert.Equal("Sub/NewName", saveCall3);

        Assert.Equal("NewName", result.Name);
        Assert.Equal(AssetKind.Scenario, result.Kind);
    }

    [Fact]
    public void AvailableRecipes_IncludesEmptyAndSeeds()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "SeedA", "SeedB" });

        var recipes = svc.AvailableRecipes();
        Assert.Equal(3, recipes.Count);

        Assert.Contains(recipes, r => r.Name == "Empty");
        Assert.Contains(recipes, r => r.Name == "SeedA");
        Assert.Contains(recipes, r => r.Name == "SeedB");

        // All recipes have Scenario kind.
        Assert.All(recipes, r => Assert.Equal(AssetKind.Scenario, r.Kind));
    }

    [Fact]
    public void CreateNew_NullName_Throws()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "", ""));
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "  ", ""));
    }

    [Fact]
    public void Kind_IsScenario()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        Assert.Equal(AssetKind.Scenario, svc.Kind);
    }
}

/// <summary>
/// Fake implementation of <see cref="IScenarioCreationSession"/> for testing.
/// </summary>
public sealed class FakeScenarioCreationSession : IScenarioCreationSession
{
    public int NewScenarioCallCount { get; private set; }
    public readonly List<string> SaveScenarioAsCalls = new();
    public readonly List<string> LoadScenarioByNameCalls = new();

    public void NewScenario()
    {
        NewScenarioCallCount++;
    }

    public void SaveScenarioAs(string scenarioName)
    {
        SaveScenarioAsCalls.Add(scenarioName);
    }

    public void LoadScenarioByName(string scenarioName)
    {
        LoadScenarioByNameCalls.Add(scenarioName);
    }
}
