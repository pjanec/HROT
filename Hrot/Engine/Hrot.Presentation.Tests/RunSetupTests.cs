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
    public void TheSwitchToLive_ReusesTheScenariosLastRunSetup_AndAnotherScenarioStartsAtTheDefaults()
    {
        var rig = With(ClusterState.OperatingEdit, initialArmed: RunArmed.Preview, withDoor: true);
        Assert.Null(rig.Choice.LastRunSetupOf("hill-attack"));

        rig.Choice.OpenAndGoLive("hill-attack", """{"$variant":3}""");
        Assert.Contains("LoadForLive:hill-attack:{\"$variant\":3}", rig.C.Calls);           // the picker's start carries it
        Assert.Equal("""{"$variant":3}""", rig.Choice.LastRunSetupOf("hill-attack"));

        rig.C.Calls.Clear();
        rig.Choice.Choose(RunArmed.Live);                                                   // ⭐ CE-3405: the indicator's switch on the open scenario
        Assert.Equal(new[] { "LoadForLive:hill-attack:{\"$variant\":3}" }, rig.C.Calls);   // ... starts with the same setup

        rig.C.LoadedScenarioName = "other";
        rig.C.Calls.Clear();
        rig.Choice.Choose(RunArmed.Live);
        Assert.Equal(new[] { "LoadForLive:other" }, rig.C.Calls);                           // never set up: the plain load, the defaults

        rig.Choice.OpenAndGoLive("hill-attack");                                            // a later start with nothing chosen forgets it
        Assert.Null(rig.Choice.LastRunSetupOf("hill-attack"));
    }

    // ════════ ⭐ CE-3393 (presets §5c, D8, D11) — the split ▶: ▾ arms a preset, ▶ plays it ════════

    private static readonly ScenarioPreset Easy = new("Easy", PresetVariant.AsAuthored, new JsonObject { ["count"] = 5 });
    private static readonly ScenarioPreset Night = new("Night attack", new PresetVariant(PresetVariantKind.Number, 3));

    private static Rig PresetRig(RunArmed armed)
    {
        var rig = With(ClusterState.OperatingEdit, initialArmed: armed, withDoor: true);
        rig.Choice.PresetsOf = _ => new[] { new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored), Easy, Night };
        return rig;
    }

    [Fact]
    public void TheSwitchToLive_LoadsTheArmedLivePreset_AndArmingNeverChangesTheMode()
    {
        var rig = PresetRig(RunArmed.Live);
        Assert.Equal(ScenarioPreset.DefaultName, rig.Choice.ArmedPreset!.Name);
        rig.Choice.ArmPreset("Easy");                                                        // in Edit the ▾ belongs to what ▶ plays: the Preview (CE-3405)
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);                                       // ▾ arms a preset, never the mode (AQ108 R1)
        Assert.Equal("Easy", rig.Choice.ArmedPreset!.Name);
        Assert.Equal("Easy", rig.Choice.ArmedPresetOf("hill-attack", RunArmed.Preview).Name);
        Assert.Equal(ScenarioPreset.DefaultName, rig.Choice.ArmedPresetOf("hill-attack", RunArmed.Live).Name);
        rig.Choice.ArmedPresets.Arm("hill-attack", RunArmed.Live, "Easy");                   // (the Live preset remembered from the briefing's ▾)
        rig.Choice.Choose(RunArmed.Live);
        Assert.Contains("LoadForLive:hill-attack:{\"count\":5}", rig.C.Calls);
    }

    private static TransitionStateIntent[] PreviewIntents(Rig rig)
    {
        rig.Bus.SwapBuffers();
        return rig.Bus.ReadManaged<TransitionStateIntent>().Where(i => i.TargetState == ClusterState.OperatingPreview).ToArray();
    }

    /// <summary>
    /// ⭐ CE-4317 (R-4168, presets §9) — ▶ with an armed preset the world does not hold is the Preview IN PLACE: the dry-run intent carries the preset's args (<c>DryRunIntents.Enter(startPaused, presetArgs)</c>, applied on the Preview's
    /// copy of the edit world — unsaved edits kept); NOTHING reopens the scenario. 🔴 Red-proof (compiles): <c>PreviewArgs</c> answering <c>reopen = true</c> for an authored world ⇒ OpenForEdit is called and the intent is gone ⇒ red.
    /// </summary>
    [Theory]
    [InlineData("Night attack", "{\"$variant\":3}")]
    [InlineData("Easy", "{\"count\":5}")]
    public void Preview_WithAnArmedPreset_IsAppliedInPlace_CarriesTheArgs_NeverReopens(string preset, string expectedArgs)
    {
        var rig = PresetRig(RunArmed.Preview);
        rig.Choice.ArmPreset(preset);
        rig.Choice.Play();
        Assert.DoesNotContain(rig.C.Calls, c => c.StartsWith("OpenForEdit"));
        var sent = Assert.Single(PreviewIntents(rig));
        Assert.Equal(expectedArgs, sent.ScenarioArgsJson);
        Assert.Equal("Continuous", sent.TimeMode);
    }

    /// <summary>⭐ CE-4317 — the default Preview is unchanged: byte-identical to the intent with no setup (no args), as authored. Red-proof: sending <c>{}</c> for Default ⇒ red.</summary>
    [Fact]
    public void Preview_OfTheDefaultPreset_SendsNoArgs_ByteIdenticalToThePlainDryRun()
    {
        var rig = PresetRig(RunArmed.Preview);
        rig.Choice.Play();
        var sent = Assert.Single(PreviewIntents(rig));
        var plain = Hrot.UI.Common.Facades.DryRunIntents.Enter(false);
        Assert.Null(sent.ScenarioArgsJson);
        Assert.Equal(plain.ScenarioArgsJson, sent.ScenarioArgsJson);
        Assert.Equal(plain.TimeMode, sent.TimeMode);
        Assert.Equal(plain.TargetState, sent.TargetState);
        Assert.DoesNotContain(rig.C.Calls, c => c.StartsWith("OpenForEdit"));
    }

    /// <summary>⭐ CE-4317 — Step (▶ paused) and Run to … start the SAME Preview: the armed preset rides them too.</summary>
    [Fact]
    public void StepAndRunTo_PreviewTheArmedPresetInPlaceToo()
    {
        var rig = PresetRig(RunArmed.Preview);
        rig.Choice.ArmPreset("Easy");
        Assert.True(rig.Choice.EnterPreviewPaused());
        var sent = Assert.Single(PreviewIntents(rig));
        Assert.Equal("{\"count\":5}", sent.ScenarioArgsJson);
        Assert.Equal("Deterministic", sent.TimeMode);
    }

    /// <summary>
    /// ⭐ CE-4317 — the ONE case in place cannot serve (presets §9 "only if a part cannot be applied in place"): the world already holds ANOTHER non-default preset (its variant is baked in; an in-place apply needs the authored world under it) —
    /// ▶ reopens with the armed one (CE-3393), and the dry run starts once editing is back; Default armed over such a world reopens as authored.
    /// </summary>
    [Fact]
    public void Preview_WhenTheWorldHoldsAnotherPreset_ReopensWithTheArmedOne_ThenTheDryRunStartsWhenEditingIsBack()
    {
        var rig = PresetRig(RunArmed.Preview);
        rig.C.LoadedArgsJson = """{"count":5}""";                                          // the world was opened with Easy
        rig.Choice.ArmPreset("Night attack");
        rig.Choice.Play();
        Assert.Contains("OpenForEdit:hill-attack:{\"$variant\":3}", rig.C.Calls);       // reopened with the preset — no dry run yet
        Assert.Empty(PreviewIntents(rig));

        rig.C.LoadedArgsJson = """{"$variant":3}""";
        Hear(rig, ClusterState.Idle);
        Hear(rig, ClusterState.OperatingEdit);                                              // the reopen landed (Hear's frame publishes the dry run and swaps it readable)
        Assert.Contains(rig.Bus.ReadManaged<TransitionStateIntent>().ToArray(), i => i.TargetState == ClusterState.OperatingPreview);

        var back = PresetRig(RunArmed.Preview);
        back.C.LoadedArgsJson = """{"count":5}""";                                         // Easy held, Default armed ⇒ as authored needs the load
        back.Choice.Play();
        Assert.Contains("OpenForEdit:hill-attack", back.C.Calls);
        Assert.Empty(PreviewIntents(back));
    }

    [Fact]
    public void Preview_WithThePresetAlreadyLoaded_IsTheDryRunAtOnce()
    {
        var rig = PresetRig(RunArmed.Preview);
        rig.Choice.ArmPreset("Easy");
        rig.C.LoadedArgsJson = """{"count":5}""";
        rig.Choice.Play();
        Assert.DoesNotContain(rig.C.Calls, c => c.StartsWith("OpenForEdit"));
        rig.Bus.SwapBuffers();
        Assert.Contains(rig.Bus.ReadManaged<TransitionStateIntent>().ToArray(), i => i.TargetState == ClusterState.OperatingPreview);
    }

    [Fact]
    public void ThePresetsLoadedTest_MatchesTheOverridesAndTheVariant()
    {
        Assert.True(PresetText.IsLoaded(new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored), null));
        Assert.True(PresetText.IsLoaded(Easy, """{"count":5}"""));
        Assert.False(PresetText.IsLoaded(Easy, """{"count":6}"""));
        Assert.True(PresetText.IsLoaded(Night, """{"$variant":3}"""));
        Assert.False(PresetText.IsLoaded(Night, """{"$variant":4}"""));
        Assert.True(PresetText.IsLoaded(new ScenarioPreset("R", PresetVariant.RandomEachRun), """{"$variant":812}"""));
        Assert.False(PresetText.IsLoaded(new ScenarioPreset("R", PresetVariant.RandomEachRun), null));
    }

    [Fact]
    public void TheArmedPreset_IsRemembered_PerScenarioAndMode_InTheUserFile()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ap-" + System.Guid.NewGuid().ToString("N"), ArmedPresets.FileName);
        var a = ArmedPresets.Open(file);
        a.Arm("hill-attack", RunArmed.Preview, "Night attack");
        a.Arm("hill-attack", RunArmed.Live, "Easy");
        var b = ArmedPresets.Open(file);                                                    // a later session
        Assert.Equal("Night attack", b.Armed("hill-attack", RunArmed.Preview));
        Assert.Equal("Easy", b.Armed("hill-attack", RunArmed.Live));
        Assert.Equal(ScenarioPreset.DefaultName, b.Armed("other", RunArmed.Live));
        b.Arm("hill-attack", RunArmed.Live, ScenarioPreset.DefaultName);                     // Default = forget
        Assert.Equal(ScenarioPreset.DefaultName, ArmedPresets.Open(file).Armed("hill-attack", RunArmed.Live));
        Assert.Null(ArmedPresets.Open(null).PersistPath);
    }

    // ════════ ⭐ CE-4317 (R-4168, L-10) — the mode menu's Run setup ▸ Preview · Live ════════

    private static Hrot.UI.Common.Panels.MainToolbarTimeControlSection.ModeMenuItem? RunSetupOf(Hrot.UI.Common.Panels.MainToolbarTimeControlSection s)
        => s.ModeMenu()?.Items.FirstOrDefault(i => i.Label == Hrot.UI.Common.Panels.MainToolbarTimeControlSection.RunSetupLabel);

    /// <summary>
    /// ⭐ CE-4317 — the Edit menu offers <i>Run setup</i> (before Close): a Preview group and a Live group, each with the scenario's presets (the ▾'s wording) and ✓ on the one ARMED for that mode, then <i>Edit presets…</i>.
    /// Choosing ARMS it for that mode only — never the mode (R1), never the other mode's preset. 🔴 Red-proof (compiles): the Live group arming <c>RunArmed.Preview</c> ⇒ the Live ✓ assert fails.
    /// </summary>
    [Fact]
    public void TheEditMenu_OffersRunSetup_PerMode_AndChoosingArmsThatModeOnly()
    {
        var rig = PresetRig(RunArmed.Preview);
        var section = new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(rig.F, choice: rig.Choice);
        var items = section.ModeMenu()!.Items;
        var setup = items.Single(i => i.Label == Hrot.UI.Common.Panels.MainToolbarTimeControlSection.RunSetupLabel);
        Assert.Equal("Close", items[^1].Label);                                              // Edit ✓ · Live · AAR · ── · Run setup ▸ · ── · Close
        Assert.Equal(setup, items[^3]);
        var groups = setup.Children!.Where(c => c.Children is not null).ToArray();
        Assert.Equal(new[] { "Preview", "Live" }, groups.Select(g => g.Label));
        Assert.Equal(3, groups[0].Children!.Count);                                          // Default · Easy · Night attack
        Assert.Equal("Edit presets…", setup.Children!.Last().Label);
        Assert.Equal(new[] { true, false, false }, groups[1].Children!.Select(r => r.Selected));   // Default armed in both

        groups[1].Children![1].Invoke!();                                                    // Live ▸ Easy
        Assert.Equal("Easy", rig.Choice.ArmedPresetOf("hill-attack", RunArmed.Live).Name);
        Assert.Equal(ScenarioPreset.DefaultName, rig.Choice.ArmedPresetOf("hill-attack", RunArmed.Preview).Name);
        Assert.Equal(RunArmed.Preview, rig.Choice.Armed);                                    // never the mode

        groups = RunSetupOf(section)!.Children!.Where(c => c.Children is not null).ToArray();   // rebuilt: ✓ moved in Live only
        Assert.Equal(new[] { false, true, false }, groups[1].Children!.Select(r => r.Selected));
        Assert.Equal(new[] { true, false, false }, groups[0].Children!.Select(r => r.Selected));

        groups[0].Children![2].Invoke!();                                                    // Preview ▸ Night attack — what ▶ will play
        Assert.Equal("Night attack", rig.Choice.ArmedPreset!.Name);
        rig.Choice.Play();
        Assert.Equal("{\"$variant\":3}", Assert.Single(PreviewIntents(rig)).ScenarioArgsJson);
    }

    /// <summary>⭐ CE-4317 — in the Live briefing the entry offers Live's presets alone; choosing one reloads Live with it (nothing has run), as the ▾ does. In a RUN (and in Preview) there is no Run setup entry.</summary>
    [Fact]
    public void TheLiveBriefing_OffersRunSetupForLive_AndChoosingReloadsWithTheArgs_NotInARun()
    {
        var live = With(ClusterState.OperatingLive, initialArmed: RunArmed.Live, withDoor: true);
        live.Choice.PresetsOf = _ => new[] { new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored), Easy, Night };
        var section = new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(live.F, choice: live.Choice);
        Assert.True(live.Choice.LiveBriefing);
        var setup = RunSetupOf(section)!;
        Assert.NotNull(setup);
        Assert.DoesNotContain(setup.Children!, c => c.Children is not null);                  // no Preview group: Live's list directly
        setup.Children!.First(c => c.Label.EndsWith("Easy")).Invoke!();
        Assert.Contains("LoadForLive:hill-attack:{\"count\":5}", live.C.Calls);

        var running = With(ClusterState.OperatingLive, initialArmed: RunArmed.Live, running: true, clock: 12);
        running.Choice.PresetsOf = live.Choice.PresetsOf;
        Assert.Null(RunSetupOf(new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(running.F, choice: running.Choice)));
        var preview = With(ClusterState.OperatingPreview, running: true, clock: 3);
        Assert.Null(RunSetupOf(new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(preview.F, choice: preview.Choice)));
    }
}
