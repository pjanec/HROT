using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class RecipeMetadataAdapterTests
{
    [Fact]
    public void ToShared_MapsAllFields()
    {
        var compiler = new RecipeMetadata
        {
            DisplayName    = "Test Recipe",
            Category       = "AI",
            Description    = "A test recipe description",
            Difficulty     = "Intermediate",
            ConceptsTaught = new List<string> { "Concept1", "Concept2", "Concept3" },
        };

        var shared = compiler.ToShared();

        Assert.NotNull(shared);
        Assert.Equal(compiler.DisplayName, shared.DisplayName);
        Assert.Equal(compiler.Category, shared.Category);
        Assert.Equal(compiler.Description, shared.Description);
        Assert.Equal(compiler.Difficulty, shared.Difficulty);
        Assert.Equal(compiler.ConceptsTaught.Count, shared.ConceptsTaught.Count);
        Assert.Equal(compiler.ConceptsTaught[0], shared.ConceptsTaught[0]);
        Assert.Equal(compiler.ConceptsTaught[1], shared.ConceptsTaught[1]);
        Assert.Equal(compiler.ConceptsTaught[2], shared.ConceptsTaught[2]);
    }

    [Fact]
    public void ToShared_NullInput_ReturnsNull()
    {
        Core.Assets.RecipeMetadata? compiler = null;
        var shared = compiler.ToShared();
        Assert.Null(shared);
    }

    [Fact]
    public void ToShared_DefaultValues_Preserved()
    {
        var compiler = new RecipeMetadata(); // all defaults

        var shared = compiler.ToShared();

        Assert.NotNull(shared);
        Assert.Equal("", shared.DisplayName);
        Assert.Equal("", shared.Category);
        Assert.Equal("", shared.Description);
        Assert.Equal("Beginner", shared.Difficulty);
        Assert.Empty(shared.ConceptsTaught);
    }

    [Fact]
    public void ToShared_ModifyingCopy_DoesNotAffectOriginal()
    {
        var compiler = new RecipeMetadata
        {
            DisplayName    = "Original",
            ConceptsTaught = new List<string> { "A" },
        };

        var shared = compiler.ToShared()!;
        shared.DisplayName = "Modified";
        shared.ConceptsTaught.Add("B");

        // Original is unaffected
        Assert.Equal("Original", compiler.DisplayName);
        Assert.Single(compiler.ConceptsTaught);
        Assert.Equal("A", compiler.ConceptsTaught[0]);
    }
}
