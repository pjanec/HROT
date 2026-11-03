using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.UI.Common.RunMode;
using Xunit;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ CE-4141 / AQ108 D24 (the ui half: the picker's Run setup, ▶'s memory, the indicator menu's text) — the pure model of a run setup (<see cref="RunSetup"/>), what the mode indicator's menu says of the running scenario
/// (<see cref="RunSetupText"/>), and ▶ reusing the scenario's last setup. Red-proof (compiles): <c>GoLive</c> ignoring <c>LastRunSetupOf</c> ⇒ the ▶ rail fails.
/// </summary>
public sealed class RunSetupTests
{
    private static readonly ScenarioParamDecl[] Decls =
    {
        new("count", "int", JsonValue.Create(2), "how many"),
        new("side", "ForceId", JsonValue.Create("Hostile")),
    };

    [Fact]
    public void AClosedExpander_GivesNoArgs_AChangedParamOnlyIt_ANumberVariantItsReservedArg_RandomNone()
    {
        var s = new RunSetup(Decls, usesVariants: true);
        Assert.Null(s.ArgsJson());                                       // the defaults: a start needs no interaction
        Assert.True(s.IsDefault);

        s.Set("count", "5");
        Assert.Equal("""{"count":5}""", s.ArgsJson());                    // only what differs — and still random: no $variant

        s.RandomVariant = false; s.Variant = 3;
        var args = JsonNode.Parse(s.ArgsJson()!)!.AsObject();
        Assert.Equal(3, args[ScenarioVariants.ArgName]!.GetValue<int>());  // a chosen number
        Assert.Equal(5, args["count"]!.GetValue<int>());

        s.RandomVariant = true;
        Assert.DoesNotContain(ScenarioVariants.ArgName, s.ArgsJson()!);   // back to random: none

        s.Set("count", "2");                                              // typing the default back is no override
        Assert.Null(s.ArgsJson());
    }

    [Fact]
    public void AVariantZero_IsAChoice_AsAuthored_NotRandom()
    {
        var s = new RunSetup(Decls, usesVariants: true) { RandomVariant = false, Variant = 0 };
        Assert.Equal("""{"$variant":0}""", s.ArgsJson());
    }

    [Fact]
    public void AScenarioWithoutVariants_NeverPassesAVariant_EvenWithANumberSet()
    {
        var s = new RunSetup(Decls, usesVariants: false) { RandomVariant = false, Variant = 4 };
        Assert.Null(s.ArgsJson());                                        // the variant row is hidden, so it cannot be chosen
        Assert.False(s.UsesVariants);
        s.Set("nope", "1");                                               // an unknown param is ignored
        Assert.Null(s.ArgsJson());
    }

    [Fact]
    public void FromArgs_SeedsTheChoice_AndIgnoresWhatTheScenarioDoesNotDeclare()
    {
        var s = RunSetup.FromArgs(Decls, true, """{"$variant":3,"count":7,"side":"Hostile","gone":1}""");
        Assert.False(s.RandomVariant);
        Assert.Equal(3, s.Variant);
        Assert.Equal("""{"$variant":3,"count":7}""", s.ArgsJson());       // "side" is its default, "gone" is not a param
    }

    [Fact]
    public void TheMenuText_NamesTheActualVariant_AsAuthoredWhenThereIsNone_AndOnlyTheParamsThatDiffer()
    {
        Assert.Equal(new[] { "Variant: as authored" }, RunSetupText.Of(null, Decls));
        Assert.Equal(new[] { "Variant: as authored" }, RunSetupText.Of("""{"count":2,"side":"Hostile"}""", Decls));   // the resident args hold every param: defaults are not worth a line
        Assert.Equal(new[] { "Variant: 3", "count = 7" }, RunSetupText.Of("""{"$variant":3,"count":7,"side":"Hostile"}""", Decls));
        Assert.Equal(new[] { "Variant: 3", "x = 1" }, RunSetupText.Of("""{"$variant":3,"x":1}""", null));                // no declared params known ⇒ every named one
        using var empty = new EntityRepository();
        Assert.Empty(RunSetupText.Of(empty));                                                                             // no scenario loaded ⇒ nothing
    }

    [Fact]
    public void Play_ReusesTheScenariosLastRunSetup_AndAnotherScenarioStartsAtTheDefaults()
    {
        var rig = With(ClusterState.OperatingEdit, initialArmed: RunArmed.Live, withDoor: true);
        Assert.Null(rig.Choice.LastRunSetupOf("hill-attack"));

        rig.Choice.OpenAndGoLive("hill-attack", """{"$variant":3}""");
        Assert.Contains("LoadForLive:hill-attack:{\"$variant\":3}", rig.C.Calls);           // the picker's start carries it
        Assert.Equal("""{"$variant":3}""", rig.Choice.LastRunSetupOf("hill-attack"));

        rig.C.Calls.Clear();
        rig.Choice.Play();                                                                  // ▶ on the open scenario
        Assert.Equal(new[] { "LoadForLive:hill-attack:{\"$variant\":3}" }, rig.C.Calls);   // ... starts with the same setup

        rig.C.LoadedScenarioName = "other";
        rig.C.Calls.Clear();
        rig.Choice.Play();
        Assert.Equal(new[] { "LoadForLive:other" }, rig.C.Calls);                           // never set up: the plain load, the defaults

        rig.Choice.OpenAndGoLive("hill-attack");                                            // a later start with nothing chosen forgets it
        Assert.Null(rig.Choice.LastRunSetupOf("hill-attack"));
    }
}
