using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Editor.UiFrameRail;
using Hrot.Editor.Windows;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-5006 W2 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-1 / TE-17 / TE-19) — the editor's two tool strips (Scenario tools, Terrain) that replaced the Editor Toolbar: their entries and what each member runs, the armed-tool-is-lit rule
/// (Tool Model step 5 / UXR-84, ported from the old toolbar), the Terrain strip's key to <see cref="TerrainLock"/>, and a real frame of both orientations.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorToolStripsTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private string? _armed;
    private readonly List<(int Action, object? View, Entity Target)> _ran = new();

    public void Dispose() => _world.Dispose();

    private ScenarioToolsWindow Scenario()
    {
        var reg = new GlobalActionRegistry();
        foreach (var id in new[] { GlobalActionIds.AddEntityFriendly, GlobalActionIds.AddEntityHostile, GlobalActionIds.AddEntityNeutral, GlobalActionIds.AddMapGraphic, GlobalActionIds.Measure })
        {
            int captured = id;
            reg.Register(id, (v, t) => _ran.Add((captured, v, t)));
        }
        return new ScenarioToolsWindow(() => reg, () => _world, () => _armed);
    }

    private (TerrainToolsWindow Window, Mock<ISpawnController> Spawn) Terrain(EntityRepository? world = null)
    {
        var spawn = new Mock<ISpawnController>();
        var w = new TerrainToolsWindow(() => world ?? _world, () => spawn.Object, () => _armed, TerrainPickCatalog.FeaturePicks(),
            new[] { ("street-clutter", "Street clutter", 1.5f), ("undergrowth", "Undergrowth", 2f) });
        return (w, spawn);
    }

    // ── Scenario tools ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The strip has ONE family, Add Entity (Friendly · Hostile · Neutral · Map graphic), and Measure; each member runs ITS existing handler (203…206, 200) from the host registry with the editor's world and no target. 🔴 Red-proof: map Hostile to 203 ⇒ red.</summary>
    [Fact]
    public void TheScenarioStrip_HasAddEntityAndMeasure_AndEachMemberRunsItsOwnHandler()
    {
        var w = Scenario();
        Assert.Equal(new[] { "##ts_add_entity", "##ts_measure" }, w.StripEntries.Select(e => e.Id).ToArray());
        var add = w.StripEntries[0];
        Assert.Equal(new[] { "Friendly", "Hostile", "Neutral", "Map graphic" }, add.Family!.Select(m => m.Label).ToArray());
        Assert.True(w.IsOpen);   // open by default
        for (int i = 0; i < 4; i++) add.Pick(i);
        w.StripEntries[1].OnClick!();
        Assert.Equal(new[] { 203, 204, 205, 206, 200 }, _ran.Select(r => r.Action).ToArray());
        Assert.All(_ran, r => { Assert.Same(_world, r.View); Assert.True(r.Target.IsNull); });
        Assert.Equal(3, add.Selected);   // the picked member is now the face
    }

    /// <summary>🔒 The armed-tool rule, ported from the old toolbar: Add Entity is lit while the Spawn tool is the armed one, Measure while Measure is; with Select (or nothing) armed neither is. 🔴 Red-proof: IsLit always false ⇒ red.</summary>
    [Fact]
    public void TheArmedToolIsLit_OnTheScenarioStrip()
    {
        var w = Scenario();
        _armed = ScenarioToolIds.Spawn;   Assert.Equal((true, false), (w.StripEntries[0].IsLit!(), w.StripEntries[1].IsLit!()));
        _armed = ScenarioToolIds.Measure; Assert.Equal((false, true), (w.StripEntries[0].IsLit!(), w.StripEntries[1].IsLit!()));
        _armed = ScenarioToolIds.Select;  Assert.Equal((false, false), (w.StripEntries[0].IsLit!(), w.StripEntries[1].IsLit!()));
        _armed = null;                    Assert.Equal((false, false), (w.StripEntries[0].IsLit!(), w.StripEntries[1].IsLit!()));
    }

    // ── Terrain strip ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The Terrain strip's families and their members, in order; it is CLOSED by default. 🔴 Red-proof: drop Hedge from the Walls filter ⇒ red.</summary>
    [Fact]
    public void TheTerrainStrip_HasTheSevenFamilies_AndIsClosedByDefault()
    {
        var (w, _) = Terrain();
        Assert.False(w.IsOpen);
        string[] Names(string id) => w.StripEntries.Single(e => e.Id == "##ts_t_" + id).Family!.Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "wood", "trees", "walls", "obstacles", "earthworks", "water", "scatter" }, w.StripEntries.Select(e => e.Id["##ts_t_".Length..]).ToArray());
        Assert.Equal(new[] { "Wood", "Scrub" }, Names("wood"));
        Assert.Equal(new[] { "Tree", "Tree row", "Tree group" }, Names("trees"));   // ⭐ CE-5008 (TE-3): One · Row · Group
        Assert.Equal(new[] { "Wall", "Fence", "Hedge" }, Names("walls"));
        Assert.Equal(new[] { "Car", "Sandbag wall", "Concrete block", "Crate", "Row of obstacles…" }, Names("obstacles"));   // ⭐ CE-5008 (TE-5): the row tool is the fifth member
        Assert.Contains("Level", Names("earthworks")); Assert.DoesNotContain("Crater", Names("earthworks"));   // the disabled pick is not offered
        Assert.Equal(new[] { "Water", "Bridge" }, Names("water"));
        Assert.Equal(new[] { "Street clutter", "Undergrowth" }, Names("scatter"));
        Assert.All(w.StripEntries.SelectMany(e => e.Family!), m => Assert.False(string.IsNullOrEmpty(m.Glyph.Glyph)));
    }

    /// <summary>What a member arms: a line feature is DRAWN (the area tool, its spec in the style JSON), a tree is one click, an obstacle is placed as an ENTITY of its TKB type (R-4264), a scatter member names its recipe. 🔴 Red-proof: obstacles armed as features ⇒ red.</summary>
    [Fact]
    public void TheTerrainMembers_ArmTheRightTool()
    {
        var (w, spawn) = Terrain();
        ToolStripEntry E(string id) => w.StripEntries.Single(e => e.Id == "##ts_t_" + id);
        E("walls").Pick(0);
        spawn.Verify(s => s.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, It.Is<string>(j => j.Contains("feature:2:"))), Times.Once);
        E("trees").Pick(0);
        spawn.Verify(s => s.StartPlacementMode(TkbEntityTypes.TerrainFeature, It.IsAny<string?>()), Times.Once);
        E("obstacles").Pick(1);
        spawn.Verify(s => s.StartPlacementMode(TkbEntityTypes.Obstacle_SandbagWall, null), Times.Once);
        E("trees").Pick(1);   // ⭐ CE-5008 — the tree Row is DRAWN (a line), the Group too
        spawn.Verify(s => s.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, It.Is<string>(j => j.Contains("feature:14:tree:Line"))), Times.Once);
        E("trees").Pick(2);
        spawn.Verify(s => s.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, It.Is<string>(j => j.Contains("feature:15:tree:Area"))), Times.Once);
        E("obstacles").Pick(4);   // the obstacle ROW: a line tool whose spec is the row's, never a terrain feature
        spawn.Verify(s => s.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, It.Is<string>(j => j.Contains(ObstacleRowOptions.Spec))), Times.Once);
        E("scatter").Pick(1);
        spawn.Verify(s => s.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, It.Is<string>(j => j.Contains("undergrowth"))), Times.Once);
        Assert.Equal(1, E("scatter").Selected);
    }

    /// <summary>Lit = the family this strip armed last AND a tool still armed. 🔴 Red-proof: ignore the active tool ⇒ stays lit after the tool ends ⇒ red.</summary>
    [Fact]
    public void ATerrainFamily_IsLit_WhileItsToolIsArmed()
    {
        var (w, _) = Terrain();
        var walls = w.StripEntries.Single(e => e.Id == "##ts_t_walls"); var water = w.StripEntries.Single(e => e.Id == "##ts_t_water");
        _armed = ScenarioToolIds.Spawn;
        Assert.False(walls.IsLit!());                     // nothing armed by this strip yet
        walls.Pick(0);
        Assert.Equal((true, false), (walls.IsLit!(), water.IsLit!()));
        _armed = null;
        Assert.False(walls.IsLit!());                     // the tool ended
        _armed = ScenarioToolIds.Select;
        Assert.False(walls.IsLit!());
    }

    /// <summary>
    /// 🔑 THE KEY — ONE per-frame rule: placed terrain is selectable on the editor's CURRENT world only while the Terrain strip is open AND its perspective is the active one. Open + Scenario ⇒ unlocked; another perspective ⇒ locked; back ⇒ unlocked;
    /// closed ⇒ locked; a reloaded world is followed. 🔴 Red-proof: drop the <c>BelongsTo</c> term ⇒ the other-perspective assert fails; always <c>false</c> ⇒ the open asserts.
    /// </summary>
    [Fact]
    public void TheTerrainLock_FollowsOpenAndThePerspective_OnTheCurrentWorld()
    {
        var first = new EntityRepository(); var second = new EntityRepository();
        try
        {
            EntityRepository current = first;
            var w = new TerrainToolsWindow(() => current, () => null, () => _armed, TerrainPickCatalog.FeaturePicks(), Array.Empty<(string, string, float)>());
            w.UpdateLock("Scenario");
            Assert.False(TerrainLock.IsOpen(first));                 // closed by default
            w.IsOpen = true;
            w.UpdateLock("Scenario"); Assert.True(TerrainLock.IsOpen(first));
            w.UpdateLock("Review");   Assert.False(TerrainLock.IsOpen(first));   // open, but its perspective is not the active one
            w.UpdateLock("Scenario"); Assert.True(TerrainLock.IsOpen(first));
            current = second;                                        // a reload swaps the world
            w.UpdateLock("Scenario"); Assert.True(TerrainLock.IsOpen(second));
            w.IsOpen = false;
            w.UpdateLock("Scenario"); Assert.False(TerrainLock.IsOpen(second));
        }
        finally { first.Dispose(); second.Dispose(); }
    }

    /// <summary>A narrow tool strip draws no pin in its title bar (it would cover the title); the title stays "Terrain".</summary>
    [Fact]
    public void AToolStrip_HasNoTitleBarPin_AndKeepsItsTitle()
    {
        var (w, _) = Terrain();
        Assert.Equal("Terrain", w.Title);
        Assert.False((bool)typeof(ToolStripWindow).GetProperty("ShowPinButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!);
    }

    /// <summary>A real frame of both strips: the Scenario strip wide ⇒ a ROW, the Terrain strip tall ⇒ a COLUMN (and a screenshot when <c>HROT_RAIL_SHOTS</c> names a folder). 🔴 Red-proof: flip the orientation test in <c>ToolStripLayout.Compute</c> ⇒ red.</summary>
    [SkippableFact]
    public void TheStrips_DrawInARowWhenWide_AndAColumnWhenTall()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var scenario = Scenario(); var (terrain, _) = Terrain(); terrain.IsOpen = true;
        terrain.StripEntries.Single(e => e.Id == "##ts_t_walls").Pick(0); _armed = ScenarioToolIds.Spawn;   // Walls lit, as in panel A
        using var frame = UiFrameHarness.Begin();
        frame.StepN(3, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(20, 20), ImGuiNET.ImGuiCond.Always); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(320, 70), ImGuiNET.ImGuiCond.Always);
            scenario.Render("Scenario", null!);
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(20, 120), ImGuiNET.ImGuiCond.Always); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(70, 360), ImGuiNET.ImGuiCond.Always);
            terrain.Render("Scenario", null!);
        });
        Assert.Equal(ToolStripOrientation.Row, scenario.LastOrientation);
        Assert.Equal(ToolStripOrientation.Column, terrain.LastOrientation);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "tool-strips.png"));
    }
}
