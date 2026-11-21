using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ <c>CE-4623</c> (animals AU-7; docs/designs/animals/animals-visuals.svg panel F) — the Animal area's Details form: offered for exactly one animal area, titled <i>Animal area</i>; Species / Count / Seed / Enclosed in one label column, every edit ONE
/// undo step; the dice re-rolls the seed; Enclosed defaults follow the species (pigs and goats on). The frame test SKIPS without a display (<c>xvfb-run -a dotnet test … --filter AnimalAreaDetailsViewTests</c>).
/// </summary>
public sealed class AnimalAreaDetailsViewTests
{
    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static (EntityRepository World, Entity Area) World(AnimalSpecies species = AnimalSpecies.Cattle)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<AnimalArea>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
        w.AddComponent(e, AnimalAreaOps.Default(species));
        return (w, e);
    }

    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    [Fact]
    public void TheTab_IsOfferedForOneAnimalArea_TitledAnimalArea_RequiringItsComponent()
    {
        var (w, e) = World();
        var d = AnimalAreaDetailsViewDescriptor.For(() => w);
        Assert.Equal("Animal area", d.Title);
        Assert.Contains(typeof(AnimalArea), d.Requires);
        Assert.True(d.AppliesTo(Context(e)));
    }

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    private static void Click(UiFrameSession frame, Action draw, Vector2 at)
    {
        frame.Step(() => { Pos(at.X, at.Y); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); draw(); });
        frame.Step(() => { Pos(5, 5); draw(); });
        frame.Step(draw);
    }

    [SkippableFact]
    public void TheForm_DrawsItsFourRows_InOneColumn_TheDiceRerollsInOneStep_TheEnclosedBoxTogglesInOneStep()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (w, e) = World(AnimalSpecies.Pig);
        var rec = new Recorder(); EditSteps.Install(w, rec);
        var view = new AnimalAreaDetailsView(() => w);
        using var frame = UiFrameHarness.Begin(420, 300);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(400, 240));
            ImGui.Begin("Details — Animal area##aa");
            view.Draw(Context(e), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "animal-area-details.png")); }
        var rects = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        foreach (var k in new[] { "species", "count", "seed", "dice", "enclosed" }) Assert.True(rects.ContainsKey(k), $"the form drew '{k}'");
        Assert.True(Math.Abs(rects["species"].Min.X - rects["count"].Min.X) < 4f, "species and count start one value column");
        Assert.True(rects["species"].Min.Y < rects["count"].Min.Y && rects["count"].Min.Y < rects["seed"].Min.Y && rects["seed"].Min.Y < rects["enclosed"].Min.Y, "in the mockup's order");
        // a pig's area is born Enclosed
        Assert.Equal(1, w.GetComponentRO<AnimalArea>(e).Enclosed);
        // the dice re-rolls the seed in ONE step
        rec.Log.Clear();
        uint before = w.GetComponentRO<AnimalArea>(e).Seed;
        Click(frame, Draw, (rects["dice"].Min + rects["dice"].Max) * 0.5f);
        Assert.NotEqual(before, w.GetComponentRO<AnimalArea>(e).Seed);
        Assert.Equal(new[] { "begin Re-roll animal area", "end" }, rec.Log);
        // the Enclosed box: one step, and 🔴 red-proof: a form that never writes it leaves the byte as it was
        rec.Log.Clear();
        Click(frame, Draw, (rects["enclosed"].Min + rects["enclosed"].Max) * 0.5f);
        Assert.Equal(0, w.GetComponentRO<AnimalArea>(e).Enclosed);
        Assert.Equal(new[] { "begin Open animal area", "end" }, rec.Log);
    }
}
