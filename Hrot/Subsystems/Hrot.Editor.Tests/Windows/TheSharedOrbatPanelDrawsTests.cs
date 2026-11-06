using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Moq;
using Raylib_cs;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4213 (docs/DESIGN_Entity_Hierarchy_Views.md §4, §5) — the REAL <see cref="SharedOrbatPanel"/> drawn in a REAL ImGui frame (the <c>UiFrameRail</c> harness, under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row)
/// over a small world holding all three relations: <b>Command</b> (two units under a commander), <b>Structure</b> (a vehicle with a sensor part and an embarked passenger) and <b>Scenario</b> (a macro instance with two members, plus the
/// Saved / Transient groups). It asserts on what the panel EMITTED while drawing (<see cref="SharedOrbatPanel.DrawnRows"/>: each row as the draw loop wrote it, with its depth, group-ness and arrow), on pixels (the frame is not empty, and the
/// views differ), and on a CLICK: a mouse press on the Structure radio button changes the view. Screenshots: <c>HROT_FRAME_SCREENSHOTS</c> (else the temp folder) — look at them; ⛔ never committed.
/// 🔴 Red-proof (compiles): <c>DrawContent</c> handing the provider <c>EntityTreeRelation.Command</c> instead of <c>Relation</c> ⇒ the click rail (and the Structure / Scenario row rails) go red.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheSharedOrbatPanelDrawsTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly Mock<ISpawnController> _spawn = new();
    private readonly Entity _leader, _tank, _sensor, _passenger, _instance;

    public TheSharedOrbatPanelDrawsTests()
    {
        _world.RegisterComponent<EntityInfo>();
        _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
        _world.RegisterComponent<PartMetadata>();
        _world.RegisterComponent<IsEmbarkedTag>();
        _world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
        _world.RegisterComponent<Fdp.Toolkit.Combat.Components.TurretPose>();
        _world.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorTag>();
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<Fdp.Toolkit.Scenario.MacroOrigin>();
        _world.RegisterComponent<Fdp.Toolkit.Scenario.MacroExpansionStatus>();
        _world.RegisterComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>();
        _world.RegisterManagedComponent<Fdp.Toolkit.Scenario.MacroInstance>();
        _world.CreateEntity();   // index 0 is no entity

        // Command: a commander and two units under him
        _leader = Named("Platoon leader");
        foreach (var n in new[] { "Rifleman A", "Rifleman B" })
        {
            var u = Named(n);
            _world.AddComponent(u, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = _leader });
        }
        // Structure: a tank with a sensor part and an embarked passenger
        _tank = Named("Tank");
        _sensor = _world.CreateEntity();
        _world.AddComponent(_sensor, new PartMetadata { ParentEntity = _tank, InstanceId = 3 });
        _world.AddComponent(_sensor, new Fdp.Toolkit.Perception.Components.SensorTag { Kind = Fdp.Toolkit.Perception.Components.SensorModality.Radar });
        _passenger = Named("Passenger");
        _world.AddComponent(_passenger, new IsEmbarkedTag { VehicleEntity = _tank });
        // Scenario: a macro instance with two members; a hand-placed (saved) unit; a live (transient) one
        _instance = Named("Fire team A");
        _world.AddComponent(_instance, new NetworkIdentity(50));
        _world.AddComponent(_instance, new Fdp.Toolkit.Scenario.MacroExpansionStatus { State = Fdp.Toolkit.Scenario.MacroExpansionState.Done });
        _world.SetManagedComponent(_instance, new Fdp.Toolkit.Scenario.MacroInstance { Macro = "test/fire-team" });
        foreach (var n in new[] { "Blue 1", "Blue 2" })
        {
            var m = Named(n);
            _world.AddComponent(m, new Fdp.Toolkit.Scenario.MacroOrigin { Instance = new Fdp.Toolkit.Replication.EntityRef(50), Local = Guid.NewGuid() });
            _world.AddComponent(m, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
        }
        var live = Named("Live addition");
        _world.AddComponent(live, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
    }

    public void Dispose() => _world.Dispose();

    private Entity Named(string name)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
        return e;
    }

    private static string Shot(string name)
    {
        var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        return Path.Combine(dir, name);
    }

    /// <summary>The pixels of a screenshot that are not the dark-grey clear colour (what the panel drew).</summary>
    private static int Drawn(string path)
    {
        var image = Raylib.LoadImage(path);
        try
        {
            int n = 0;
            for (int y = 0; y < image.Height; y += 1)
                for (int x = 0; x < image.Width; x += 1)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(c.R - 80) > 12 || Math.Abs(c.G - 80) > 12 || Math.Abs(c.B - 80) > 12) n++;
                }
            return n;
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static int Differing(string a, string b)
    {
        var ia = Raylib.LoadImage(a); var ib = Raylib.LoadImage(b);
        try
        {
            int n = 0;
            for (int y = 0; y < Math.Min(ia.Height, ib.Height); y++)
                for (int x = 0; x < Math.Min(ia.Width, ib.Width); x++)
                {
                    var p = Raylib.GetImageColor(ia, x, y); var q = Raylib.GetImageColor(ib, x, y);
                    if (p.R != q.R || p.G != q.G || p.B != q.B) n++;
                }
            return n;
        }
        finally { Raylib.UnloadImage(ia); Raylib.UnloadImage(ib); }
    }

    private static void Pos(ImGuiNET.ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableFact]
    public void TheThreeRelations_AreDrawnByTheRealPanel_AndTheSwitchChangesTheView()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var adapter = new ScenarioOrbatAdapter(_world, _world.Bus, _spawn.Object);
        var panel = new SharedOrbatPanel();
        var picked = new List<Entity>();
        adapter.PickOffer = e => { picked.Add(e); return true; };   // a click on a row is "select this entity" — the ONE route the map and the inspector share (CE-1346's pick offer takes it first)
        foreach (var id in new[] { _leader.Index, _tank.Index, _instance.Index, ScenarioOrbatAdapter.GroupSavedId, ScenarioOrbatAdapter.GroupTransientId }) panel.Expand(id);

        using var frame = UiFrameHarness.Begin(640, 420);
        void Step(Action<ImGuiNET.ImGuiIOPtr>? inject, int times = 1)
        {
            for (int i = 0; i < times; i++)
                frame.Step(() =>
                {
                    inject?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new Vector2(420, 380), ImGuiNET.ImGuiCond.Always);
                    ImGui.Begin("ORBAT");
                    panel.DrawContent(adapter, adapter);
                    ImGui.End();
                });
        }
        void ClickRow(string text)
        {
            var r = panel.DrawnRows.Single(x => x.Text == text);
            var c = (r.Min + r.Max) / 2f + new Vector2(20f, 0f);   // clear of the arrow button, on the row's text
            Step(io => Pos(io, c.X, c.Y), 2);
            Step(io => { Pos(io, c.X, c.Y); io.AddMouseButtonEvent(0, true); });
            Step(io => Pos(io, c.X, c.Y));
            Step(io => { Pos(io, c.X, c.Y); io.AddMouseButtonEvent(0, false); });
            Step(null, 2);
        }

        // ── Command: two units under a commander; the tank's parts and passenger are NOT in the chain of command
        Step(null, 4);
        var command = panel.DrawnRows.ToList();
        Assert.Equal(EntityTreeRelation.Command, panel.Relation);
        Assert.Equal(new[] { "Platoon leader", "Rifleman A", "Rifleman B" }, command.Where(r => r.Text.StartsWith("Platoon") || r.Text.StartsWith("Rifleman")).Select(r => r.Text));
        Assert.Equal(new[] { 0, 1, 1 }, command.Where(r => r.Text.StartsWith("Platoon") || r.Text.StartsWith("Rifleman")).Select(r => r.Depth));
        Assert.Equal(true, command.Single(r => r.Text == "Platoon leader").ArrowExpanded);            // the commander carries an open arrow
        Assert.DoesNotContain(command, r => r.Text.Contains("sensor"));
        Assert.All(command, r => Assert.False(r.IsGroup));
        Assert.Equal(new[] { "Command", "Structure", "Scenario" }, panel.RelationSwitch.Select(s => s.Label));
        // ⭐ CE-4256 — ONE line: three icon buttons (not radios, no "Filter" label), then the filter field filling the rest
        var icons = panel.RelationSwitch;
        Assert.All(icons, i => Assert.True(Math.Abs(i.Min.Y - icons[0].Min.Y) < 1f, "the icons share one line"));
        Assert.True(icons[0].Max.X < icons[1].Min.X && icons[1].Max.X < icons[2].Min.X, "side by side");
        var filter = panel.DrawnFilter;
        Assert.True(Math.Abs(filter.Min.Y - icons[0].Min.Y) < 4f, "the filter field is on the icons' line");
        Assert.True(filter.Min.X >= icons[2].Max.X, "after the third icon");
        Assert.True(filter.Max.X - filter.Min.X > 200f, $"and fills the rest of the line ({filter.Max.X - filter.Min.X:0} px)");
        ClickRow("Rifleman A");
        Assert.Equal("Rifleman A", _world.GetComponentRO<EntityInfo>(Assert.Single(picked)).Name.ToString());   // a click selects that unit
        frame.Screenshot(Shot("orbat-command.png"));
        string commandShot = Shot("orbat-command.png");
        Assert.True(Drawn(commandShot) > 3000, "the panel drew into the frame");

        // ── click the Structure radio button: the panel switches, and the rows change
        var structure = panel.RelationSwitch.Single(s => s.Label == "Structure");
        var at = (structure.Min + structure.Max) / 2f;
        Step(io => Pos(io, at.X, at.Y), 2);
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        Step(null, 3);
        Assert.Equal(EntityTreeRelation.Structure, panel.Relation);                                    // the click did it
        var rows = panel.DrawnRows.ToList();
        Assert.Contains(rows, r => r.Text == "Tank" && r.Depth == 0 && r.ArrowExpanded == true);       // the vehicle, expanded
        Assert.Contains(rows, r => r.Text == "Tank · sensor Radar" && r.Depth == 1);                   // its sensor part
        Assert.Contains(rows, r => r.Text == "Passenger" && r.Depth == 1);                             // the embarked passenger rides under it
        Assert.DoesNotContain(rows, r => r.Text == "Rifleman A" && r.Depth == 1);                      // the command chain is not this relation
        picked.Clear();
        ClickRow("Tank · sensor Radar");
        Assert.Equal(_sensor, Assert.Single(picked));                                                  // the part row selects the PART entity, in this relation too
        frame.Screenshot(Shot("orbat-structure.png"));
        Assert.True(Differing(commandShot, Shot("orbat-structure.png")) > 200, "the two views are different pictures");

        // ── Scenario: the macro instance with its members, and the Saved / Transient groups
        var scenario = panel.RelationSwitch.Single(s => s.Label == "Scenario");
        at = (scenario.Min + scenario.Max) / 2f;
        Step(io => Pos(io, at.X, at.Y), 2);
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        Step(null, 3);
        Assert.Equal(EntityTreeRelation.Scenario, panel.Relation);
        rows = panel.DrawnRows.ToList();
        Assert.Contains(rows, r => r.IsGroup && r.Text.StartsWith("Saved ("));                          // group labels, not entities
        Assert.Contains(rows, r => r.IsGroup && r.Text.StartsWith("Transient ("));
        Assert.Contains(rows, r => !r.IsGroup && r.Text == "Fire team A" && r.Depth == 1 && r.ArrowExpanded == true);
        Assert.Contains(rows, r => r.Text == "Blue 1" && r.Depth == 2);                                // the members hang under the instance
        Assert.Contains(rows, r => r.Text == "Blue 2" && r.Depth == 2);
        Assert.Contains(rows, r => r.Text == "Live addition" && r.Depth == 1);                         // a transient root
        Assert.DoesNotContain(rows, r => r.Text == "Tank · sensor Radar");
        picked.Clear();
        ClickRow("Blue 2");
        Assert.Equal("Blue 2", _world.GetComponentRO<EntityInfo>(Assert.Single(picked)).Name.ToString());   // a macro member selects the member
        picked.Clear();
        var group = panel.DrawnRows.Single(r => r.IsGroup && r.Text.StartsWith("Saved ("));
        var gc = (group.Min + group.Max) / 2f + new Vector2(20f, 0f);
        Step(io => Pos(io, gc.X, gc.Y), 2); Step(io => { Pos(io, gc.X, gc.Y); io.AddMouseButtonEvent(0, true); }); Step(io => Pos(io, gc.X, gc.Y)); Step(io => { Pos(io, gc.X, gc.Y); io.AddMouseButtonEvent(0, false); }); Step(null, 2);
        Assert.Empty(picked);                                                                          // a group label selects nothing: it is no entity
        frame.Screenshot(Shot("orbat-scenario.png"));
        Assert.True(Differing(Shot("orbat-structure.png"), Shot("orbat-scenario.png")) > 200);

        // ── CE-4256 — and back: the Command icon switches the view as the radio did
        var cmd = panel.RelationSwitch.Single(s => s.Label == "Command");
        at = (cmd.Min + cmd.Max) / 2f;
        Step(io => Pos(io, at.X, at.Y), 2);
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        Step(null, 3);
        Assert.Equal(EntityTreeRelation.Command, panel.Relation);
        Assert.Contains(panel.DrawnRows, r => r.Text == "Rifleman A" && r.Depth == 1);
    }

    /// <summary>⭐ U-3 (docs/UX/DESIGN_Colour_Icons.md §3, I3) — the relation switch is three coloured glyph toggles through the ONE icon map: Command teal, Structure orange, Scenario violet; the ACTIVE one framed and at full
    /// strength on the neutral #34343E, the others on #1C1C22 at ~60 % in THEIR OWN colours (never the active one coloured and the rest grey). 🔴 Red-proof (compiles): the panel drawing its options through <c>ImGui.Button</c> again ⇒ no neutral background ⇒ red.
    /// Render (never in git): <c>HROT_FRAME_SCREENSHOTS</c> ⇒ <c>colour-icons-orbat.png</c>.</summary>
    [SkippableFact]
    public void U3_TheRelationSwitch_IsThreeColouredGlyphToggles_TheActiveOneFramedAtFullStrength()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var adapter = new ScenarioOrbatAdapter(_world, _world.Bus, _spawn.Object);
        var panel = new SharedOrbatPanel();
        using var frame = UiFrameHarness.Begin(640, 200);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        for (int i = 0; i < 5; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(420, 180), ImGuiNET.ImGuiCond.Always);
                ImGui.Begin("ORBAT##u3");
                panel.DrawContent(adapter, adapter);
                ImGui.End();
            });
        string path = Shot("colour-icons-orbat.png");
        frame.Screenshot(path);
        var icons = panel.RelationSwitch;
        Assert.Equal(3, icons.Count);
        var image = Raylib.LoadImage(path);
        try
        {
            static bool Near(Color c, Vector4 v, int tol) => Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;
            int Count(int i, Func<Color, bool> what)
            {
                int n = 0;
                for (int x = (int)icons[i].Min.X; x < (int)icons[i].Max.X; x++) for (int y = (int)icons[i].Min.Y; y < (int)icons[i].Max.Y; y++) if (what(Raylib.GetImageColor(image, x, y))) n++;
                return n;
            }
            var neutralSel = new Vector4(0x34 / 255f, 0x34 / 255f, 0x3E / 255f, 1f); var neutral = new Vector4(0x1C / 255f, 0x1C / 255f, 0x22 / 255f, 1f);
            var teal = NodeEditor.Core.Widgets.Icons.Get("orbat/command").Color; var orange = NodeEditor.Core.Widgets.Icons.Get("orbat/structure").Color; var violet = NodeEditor.Core.Widgets.Icons.Get("orbat/scenario").Color;
            Assert.True(Count(0, c => Near(c, neutralSel, 5)) > 80, "the active option (Command) sits on the selected neutral #34343E");
            Assert.True(Count(1, c => Near(c, neutral, 5)) > 80 && Count(2, c => Near(c, neutral, 5)) > 80, "the others on #1C1C22");
            Assert.True(Count(0, c => Near(c, teal, 24)) >= 8, "Command's glyph is teal at full strength");
            Assert.True(Count(1, c => Near(c, orange, 24)) == 0 && Count(2, c => Near(c, violet, 24)) == 0, "the unselected options are not at full strength...");
            Assert.True(Count(1, c => Near(c, Vector4.Lerp(neutral, orange, 0.6f), 24)) >= 8, "...Structure keeps its own orange at ~60 %");
            Assert.True(Count(2, c => Near(c, Vector4.Lerp(neutral, violet, 0.6f), 24)) >= 8, "...and Scenario its own violet");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
