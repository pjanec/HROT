using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class PasteResponseModalTests
{
    private static readonly string WellFormedText =
        "----- HUMAN SUMMARY -----\n" +
        "The combat behavior has been refined.\n" +
        "\n" +
        "----- STRUCTURED CHANGES (JSON) -----\n" +
        "{\n" +
        "  \"summary\": \"Burst-fire behavior added.\",\n" +
        "  \"changes\": [\n" +
        "    {\n" +
        "      \"kind\": \"node_added\",\n" +
        "      \"elementId\": \"b4711d22-1d22-4c5e-9a3b-2c4d5e6f7a8b\",\n" +
        "      \"elementDescription\": \"Repeater decorator\",\n" +
        "      \"field\": null,\n" +
        "      \"oldValue\": null,\n" +
        "      \"newValue\": \"Repeater(count: 3)\",\n" +
        "      \"severity\": \"behavior\",\n" +
        "      \"description\": \"AimAndFire now repeats 3 times.\"\n" +
        "    }\n" +
        "  ]\n" +
        "}";

    // ---- Apply with well-formed text ------------------------------------------

    [Fact]
    public void Apply_WellFormedText_PopulatesRegistry_ReturnsTrue()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = WellFormedText };

        var result = state.Apply(assetId, registry);

        Assert.True(result);
        Assert.NotNull(registry.GetSession(assetId));
        Assert.True(state.SessionWasApplied);
    }

    // ---- Apply twice replaces session ------------------------------------------

    [Fact]
    public void Apply_Twice_SecondSessionReplacesPrevious()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();

        var firstText = WellFormedText;
        var secondText = WellFormedText.Replace("Burst-fire behavior added.", "Different summary.");

        var state = new PasteResponseModalState { PastedText = firstText };
        state.Apply(assetId, registry);

        state.PastedText = secondText;
        state.Apply(assetId, registry);

        var session = registry.GetSession(assetId);
        Assert.NotNull(session);
        Assert.Contains("Different summary.", session!.Response.TopLevelSummary);
    }

    // ---- Apply with bad text --------------------------------------------------

    [Fact]
    public void Apply_UnrecoverableTruncation_ReturnsFalse_ParseErrorSet()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var badText = "{ \"summary\": \"abc\", \"changes\": [{ \"kind\":";

        var state = new PasteResponseModalState { PastedText = badText };
        var result = state.Apply(assetId, registry);

        Assert.False(result);
        Assert.NotNull(state.ParseError);
        Assert.Null(registry.GetSession(assetId));
    }

    // ---- ParseError stays null on success ------------------------------------

    [Fact]
    public void Apply_WellFormedText_ParseErrorRemainsNull()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = WellFormedText };

        state.Apply(assetId, registry);

        Assert.Null(state.ParseError);
    }

    // ---- Reset ---------------------------------------------------------------

    [Fact]
    public void Reset_ClearsAllState()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = WellFormedText };
        state.Apply(assetId, registry);

        state.Reset();

        Assert.Equal("", state.PastedText);
        Assert.Null(state.ParseError);
        Assert.False(state.SessionWasApplied);
    }

    // ---- D-12: zero-change response policy -----------------------------------

    [Fact]
    public void ZeroChanges_WithTruncationWarning_Rejected()
    {
        // A response with 0 changes and a truncation warning should be rejected.
        const string truncatedText = "{ \"summary\": \"abc\", \"changes\": [{ \"kind\":";
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = truncatedText };

        var result = state.Apply(assetId, registry);

        Assert.False(result);
        Assert.NotNull(state.ParseError);
        Assert.Contains("truncated", state.ParseError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(registry.GetSession(assetId));
    }

    [Fact]
    public void ZeroChanges_WithInformationalWarning_Accepted()
    {
        // A valid "nothing changed" response with an informational warning should be accepted.
        const string noChangesText =
            "----- HUMAN SUMMARY -----\n" +
            "No changes detected.\n" +
            "\n" +
            "----- STRUCTURED CHANGES (JSON) -----\n" +
            "{\n" +
            "  \"summary\": \"No structural changes detected.\",\n" +
            "  \"changes\": [],\n" +
            "  \"warnings\": []\n" +
            "}";
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = noChangesText };

        var result = state.Apply(assetId, registry);

        Assert.True(result);
        Assert.Null(state.ParseError);
        Assert.NotNull(registry.GetSession(assetId));
    }

    [Fact]
    public void ZeroChanges_NoWarnings_Accepted()
    {
        // A response with 0 changes and 0 warnings should be accepted (valid empty diff).
        const string noChangesText =
            "----- HUMAN SUMMARY -----\n" +
            "No changes detected.\n" +
            "\n" +
            "----- STRUCTURED CHANGES (JSON) -----\n" +
            "{\n" +
            "  \"summary\": \"No changes.\",\n" +
            "  \"changes\": []\n" +
            "}";
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new PasteResponseModalState { PastedText = noChangesText };

        var result = state.Apply(assetId, registry);

        Assert.True(result);
        Assert.Null(state.ParseError);
        Assert.NotNull(registry.GetSession(assetId));
    }
}
