using Hrot.Editor.AiShared.Recipes;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Recipes;

public sealed class RecipeMetadataTests
{
    [Fact]
    public void SharedType_HasAllFields()
    {
        var meta = new RecipeMetadata
        {
            DisplayName    = "My Recipe",
            Category       = "AI",
            Description    = "A test recipe",
            Difficulty     = "Advanced",
            ConceptsTaught = new List<string> { "ConceptA", "ConceptB" },
        };

        Assert.Equal("My Recipe", meta.DisplayName);
        Assert.Equal("AI", meta.Category);
        Assert.Equal("A test recipe", meta.Description);
        Assert.Equal("Advanced", meta.Difficulty);
        Assert.Equal(2, meta.ConceptsTaught.Count);
        Assert.Contains("ConceptA", meta.ConceptsTaught);
        Assert.Contains("ConceptB", meta.ConceptsTaught);
    }

    [Fact]
    public void Defaults_Difficulty_IsBeginner()
    {
        var meta = new RecipeMetadata();
        Assert.Equal("Beginner", meta.Difficulty);
    }

    [Fact]
    public void Defaults_ConceptsTaught_IsEmptyNotNull()
    {
        var meta = new RecipeMetadata();
        Assert.NotNull(meta.ConceptsTaught);
        Assert.Empty(meta.ConceptsTaught);
    }

    [Fact]
    public void Defaults_DisplayName_IsEmpty()
    {
        var meta = new RecipeMetadata();
        Assert.Equal("", meta.DisplayName);
    }

    [Fact]
    public void SetAllFields_ThenReadBack_AllMatch()
    {
        var concepts = new List<string> { "Loops", "Variables", "Events" };
        var meta = new RecipeMetadata
        {
            DisplayName    = "Beginner Tutorial",
            Category       = "Tutorials",
            Description    = "Learn the basics",
            Difficulty     = "Beginner",
            ConceptsTaught = concepts,
        };

        Assert.Equal("Beginner Tutorial", meta.DisplayName);
        Assert.Equal("Tutorials", meta.Category);
        Assert.Equal("Learn the basics", meta.Description);
        Assert.Equal("Beginner", meta.Difficulty);
        Assert.Same(concepts, meta.ConceptsTaught);
        Assert.Equal(3, meta.ConceptsTaught.Count);
    }
}
