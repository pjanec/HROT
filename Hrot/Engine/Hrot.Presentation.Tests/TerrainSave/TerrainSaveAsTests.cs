using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.TerrainOsm;
using Hrot.UI.Common.TerrainSave;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §13 W-3/W-4; mockup <c>terrain-authoring-4-save.svg</c>) — the <i>Save terrain as</i> dialog: its logic (the name, the summary that follows it, the two choices, the overwrite confirmation naming the
/// terrain's users, a failure as a message) and its REAL frame (Xvfb; a skip is not a pass): the rows are where the mockup puts them, the glyphs are the named ones in their colour. ✅ Red-proofs (compiling mutations, one command): <c>Save()</c> not asking
/// before an overwrite ⇒ the W-4 rail red; the frame's Save button at the wrong y ⇒ the layout rail red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainSaveAsTests
{
    private sealed class Fake : ITerrainSaveService
    {
        public string? SourceScenarioName { get; set; } = "riverside-3";
        public string? SourceTerrainName => "base-t";
        public readonly HashSet<string> Existing = new();
        public readonly List<string> Previews = new(), Saves = new();
        public bool FailSave;
        public TerrainSavePreview Preview(string name)
        {
            Previews.Add(name);
            return new TerrainSavePreview(23, 684, 14, 220, 6.1, 3, 12, 2, 3, 20000, 120.0);
        }
        public bool Exists(string name) => Existing.Contains(name);
        public IReadOnlyList<string> UsersOf(string name) => new[] { "riverside-3-mission", "night-crossing", "bridge-defence" };
        public TerrainSaveOutcome Save(string name, bool prebuild, string? scenario)
        {
            if (FailSave) throw new IOException("the disk is full");
            Saves.Add($"{name}|{prebuild}|{scenario}");
            return new TerrainSaveOutcome("assets/terrain/" + name, scenario);
        }
    }

    [Fact]
    public void U5_TheNameDecidesTheSummary_TheChoicesGoToTheSave_AndABadNameCannotBeSaved()
    {
        var fake = new Fake();
        var m = new TerrainSaveAsModel(fake);
        Assert.Equal("riverside-3", m.Name);
        Assert.Equal("riverside-3-mission", m.ScenarioName);
        m.Refresh(); m.Refresh();
        Assert.Single(fake.Previews);                          // the summary is read again only when the name changed
        Assert.Equal(23, m.Preview!.Blocks);
        Assert.Equal("~2 min; loads fast after", m.MeshEstimate);
        Assert.True(m.CanSave);

        m.Name = "bad name!";
        Assert.False(m.NameOk); Assert.False(m.CanSave); Assert.Equal("use letters, digits, - and _", m.NameProblem);
        m.Refresh();
        Assert.Null(m.Preview);
        Assert.Single(fake.Previews);                          // a bad name asks the engine nothing

        m.Name = "riverside-4"; m.PrebuildMesh = false; m.CreateScenario = true; m.ScenarioName = "riverside-4-mission";
        Assert.True(m.Save());
        Assert.Equal("riverside-4|False|riverside-4-mission", Assert.Single(fake.Saves));
        Assert.Equal(TerrainSaveStep.Done, m.Step);
        Assert.Equal("riverside-4-mission", m.Outcome!.ScenarioName);

        var none = new TerrainSaveAsModel(fake) { CreateScenario = false, Name = "plain" };
        Assert.True(none.Save());
        Assert.Equal("plain|True|", fake.Saves[1]);              // no scenario asked for ⇒ none passed
    }

    [Fact]
    public void U5_SavingOverAnExistingTerrain_AsksFirst_NamesTheUsers_ReplaceWritesAndCancelDoesNot()
    {
        var fake = new Fake(); fake.Existing.Add("riverside-3");
        var m = new TerrainSaveAsModel(fake);
        Assert.True(m.Overwrites);
        Assert.False(m.Save());                                   // asks: nothing written yet
        Assert.Equal(TerrainSaveStep.ConfirmOverwrite, m.Step);
        Assert.Empty(fake.Saves);
        Assert.Equal(new[] { "riverside-3-mission", "night-crossing", "bridge-defence" }, m.Users);

        m.CancelReplace();
        Assert.Equal(TerrainSaveStep.Form, m.Step);
        Assert.Empty(fake.Saves);

        Assert.False(m.Save());
        Assert.True(m.ConfirmReplace());
        Assert.Single(fake.Saves);
        Assert.Equal(TerrainSaveStep.Done, m.Step);
    }

    [Fact]
    public void U5_AFailedSaveIsAMessageOnTheModel_NotAnExceptionInTheFrame_AndTheDialogStaysOpen()
    {
        var fake = new Fake { FailSave = true };
        var m = new TerrainSaveAsModel(fake);
        Assert.False(m.Save());
        Assert.Equal("the disk is full", m.Error);
        Assert.Equal(TerrainSaveStep.Form, m.Step);
        Assert.Null(m.Outcome);
    }

    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    [SkippableFact]
    public void U5_TheDialogsFrame_HasTheMockupsRows_AlignedChecksAndButtons_AndTheNamedGlyphsInTheirColours()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(900, 640);
        var window = new TerrainSaveAsWindow(() => new Fake());
        window.Open();
        for (int i = 0; i < 8; i++) frame.Step(window.Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u5-save-terrain-as.png")); }

        Assert.True(window.IsOpen);
        var r = window.Rects;
        // the two checkboxes and their fields share x; the name field and the scenario field are the mockup's widths and share their rows' alignment
        Assert.Equal(r["mesh"].Min.X, r["scenario"].Min.X, 1f);
        Assert.True(r["scenario.name"].Min.X > r["scenario"].Min.X + 120f);
        Assert.True(r["name"].Max.X - r["name"].Min.X > 250f);
        // the footer: Save terrain and Cancel on ONE row, one height, Save left of Cancel
        Assert.Equal(r["btn:save"].Min.Y, r["btn:cancel"].Min.Y, 1f);
        Assert.Equal(r["btn:save"].Max.Y - r["btn:save"].Min.Y, r["btn:cancel"].Max.Y - r["btn:cancel"].Min.Y, 1f);
        Assert.True(r["btn:save"].Max.X < r["btn:cancel"].Min.X);
        // the section order, top to bottom
        Assert.True(r["sep:Baked into the terrain"].Min.Y < r["sep:Stays in the source only"].Min.Y && r["sep:Stays in the source only"].Min.Y < r["sep:Then"].Min.Y);
        // the rows: four baked lines one under the other at one x, the glyph names AND colours of the mockup (R-4139)
        Assert.Equal(r["glyph:blocks"].Min.X, r["glyph:bridges"].Min.X, 0.5f);
        Assert.True(r["glyph:blocks"].Min.Y < r["glyph:woods"].Min.Y && r["glyph:woods"].Min.Y < r["glyph:walls"].Min.Y && r["glyph:walls"].Min.Y < r["glyph:bridges"].Min.Y);
        Assert.Equal(OsmWizardStyle.ORANGE, window.Glyphs["blocks"].Colour);
        Assert.Equal(OsmWizardStyle.GREEN, window.Glyphs["woods"].Colour);
        Assert.Equal(OsmWizardStyle.BLUE, window.Glyphs["bridges"].Colour);
        Assert.Equal(OsmWizardStyle.BLUE, window.Glyphs["units"].Colour);
        Assert.Equal(OsmWizardStyle.RED, window.Glyphs["damage"].Colour);
        Assert.Equal(OsmWizardStyle.OK, window.Glyphs["name.valid"].Colour);
        // the Save button is the mockup's filled blue, drawn
        var shot = Raylib.LoadImageFromScreen();
        try { Assert.True(Count(shot, c => Math.Abs(c.R - 0x33) < 24 && Math.Abs(c.G - 0x69) < 24 && Math.Abs(c.B - 0xAD) < 24) > 200, "the Save terrain button is filled blue"); }
        finally { Raylib.UnloadImage(shot); }
    }
}
