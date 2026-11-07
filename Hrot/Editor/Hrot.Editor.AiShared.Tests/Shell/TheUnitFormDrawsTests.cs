using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Forms;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ U-23 / D3 (docs/UX/DESIGN_Details_Forms.md §3) — the <b>Unit</b> form in a real frame: name, side, strength read from the world, the expert row under
/// <i>Details for experts</i>; ⭐ §2.4 — ONE editable rule: a recording (the run state <c>Replay</c>) is read-only. The bundle registers the declared forms.
/// 🔴 Red-proof (compiles): the Unit form's Side reading the wrong index map ⇒ <c>Side = Red</c> ⇒ red. Renders: <c>HROT_RAIL_SHOTS</c> ⇒ <c>unit-form.png</c>.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheUnitFormDrawsTests
{
    [SkippableFact]
    public void U23_TheUnitForm_ReadsTheWorld_AndARecordingIsReadOnly()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Health>(); world.RegisterComponent<NetworkIdentity>();
        var e = world.CreateEntity();
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(e, new Health { Current = 80, Max = 100 });
        world.AddComponent(e, new NetworkIdentity(42));

        HrotForms.EnsureRegistered();
        Assert.Contains(FormCatalogue.All, f => f.Id == HrotForms.Unit.Id);   // the bundle's one registration
        Assert.True(HrotForms.Unit.OffersFor(world, e));
        Assert.True(FormDetailsView.Editable(VariableRunState.Planning, world));
        Assert.False(FormDetailsView.Editable(VariableRunState.Replay, world));   // §2.4: a recording is read-only, one rule

        var view = new FormDetailsView(HrotForms.Unit, () => world);
        view.View.ExpertOpen = true;
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 300));
            ImGui.Begin("Details##unit-form");
            view.View.Draw(HrotForms.Unit, world, new[] { e }, editable: true);
            ImGui.End();
        });
        Assert.Equal(new[] { "Name = Alpha 1-1", "Side = Blue", "Strength = 80", "Entity id = 42" }, view.View.Rows);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "unit-form.png"));
    }

    /// <summary>⭐ U-23 / D5 — the <b>Shape</b> form is offered for a spawn area OR a weather region (an any-of), and a spawn area's rows read the world.
    /// 🔴 Red-proof (compiles): <c>FormDecl.OffersFor</c> ignoring <c>Offers</c> ⇒ not offered (no Requires) ⇒ red.</summary>
    [Fact]
    public void U23_TheShapeForm_IsOfferedForASpawnArea_AndReadsIt()
    {
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Hrot.Common.SpawnArea>(); world.RegisterComponent<Fdp.Toolkit.Weather.WeatherConditions>();
        var spawn = world.CreateEntity();
        world.AddComponent(spawn, new Hrot.Common.SpawnArea { Side = ForceId.Hostile, FacingDeg = 90 });
        var unit = world.CreateEntity();
        world.AddComponent(unit, new EntityInfo { Name = new FixedString64("Alpha") });
        HrotForms.EnsureRegistered();
        Assert.True(HrotForms.Shape.OffersFor(world, spawn));
        Assert.False(HrotForms.Shape.OffersFor(world, unit));
        Assert.Equal(1.0, HrotForms.Shape.Fields[0].ReadNumber!(world, spawn));   // Red
        Assert.Equal(90.0, HrotForms.Shape.Fields[1].ReadNumber!(world, spawn));
        Assert.Equal("90 °", FormView.Format(HrotForms.Shape.Fields[1], 90, default));
        Assert.All(HrotForms.Shape.Fields, f => Assert.False(f.Attribute != null && f.Definition == null));   // every field names a vocabulary attribute
    }

    /// <summary>⭐ U-23 / D5 — the <b>Scenario</b> form is the WORLD form: its target is the World Environment (the clock + the global weather); the start reads as a
    /// date, the global weather through the SAME rows as a region's. 🔴 Red-proof (compiles): <c>WorldTarget</c> returning <c>Entity.Null</c> ⇒ red.</summary>
    [Fact]
    public void U23_TheScenarioForm_TargetsTheWorldEnvironment()
    {
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>(); world.RegisterComponent<Fdp.Toolkit.Weather.WeatherConditions>();
        var env = world.CreateEntity();
        world.AddComponent(env, new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc).Ticks, DisplayUtcOffsetMinutes = 120 });
        world.AddComponent(env, new Fdp.Toolkit.Weather.WeatherConditions { GroundTemperatureC = 21, VisibilityM = 8000 });
        HrotForms.EnsureRegistered();
        Assert.Equal(env, HrotForms.Scenario.WorldTarget!(world));
        var f = HrotForms.Scenario.Fields;
        Assert.Equal("2026-06-01 06:00 UTC", f[0].ReadText!(world, env).ToString());
        Assert.Equal(120.0, f[1].ReadNumber!(world, env));
        Assert.Contains(f, x => x.Label == "Temperature" && x.ReadNumber!(world, env) == 21);
        Assert.Contains(HrotForms.Shape.Fields, x => x.Label == "Temperature");   // the ONE weather row set, on both forms
    }
}
