using System;
using System.Linq;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.CGF.Configuration;
using Hrot.Editor;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.Scenario;
using Hrot.SimHost;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐ <c>CE-3043</c> — the editor's AI section: reads a unit's live task / SOP / ROE and applies an author's change
/// THROUGH THE ONE GATE — at once while paused, by event while running — at origin Superior. 📄
/// <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6. Runs the production behaviour registry.
/// </summary>
public sealed class EntityAiSectionTests
{
    private sealed class Fixture
    {
        public readonly EntityRepository World = new();
        public readonly BehaviorRegistry Registry = new();
        public readonly EntityAiEditModel Model;
        public readonly Entity Unit;

        public Fixture()
        {
            SimHostComponentRegistry.RegisterAll(World);
            CognitiveComponentRegistry.RegisterAll(World);
            BlueprintTierTable.RegisterAll(World);
            var geo = new WGS84Transform();
            geo.SetOrigin(0.0, 0.0, 0.0);
            World.SetSingletonManaged<IGeographicTransform>(geo);
            CgfBehaviorSetup.LoadFromAiAssembly(Registry);
            Model = new EntityAiEditModel(() => World, () => Registry);
            Unit = World.CreateEntity();
            World.AddComponent(Unit, new BehaviorState());
        }
    }

    [Fact]
    public void CE3043_Paused_AnEditAppliesAtOnce_ThroughTheGate_AtSuperior()
    {
        var f = new Fixture();
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: false));
        Assert.True(f.Model.ApplySop(f.Unit, "BasicInfantrySop", "{}", running: false));
        Assert.True(f.Model.ApplyRoe(f.Unit, RoeFire.HoldFire, RoeReactions.StayOnTask, running: false));

        var now = f.Model.Read(f.Unit)!;
        Assert.Equal("Idle", now.Task);
        Assert.Equal(BehaviorOrigin.Superior, now.TaskOrigin);
        Assert.Equal("BasicInfantrySop", now.Sop);
        Assert.Equal(BehaviorOrigin.Superior, now.SopOrigin);
        Assert.Equal(RoeFire.HoldFire, now.Fire);
        Assert.Equal(RoeReactions.StayOnTask, now.Reactions);
        Assert.Equal(BehaviorOrigin.Superior, now.RoeSetBy);

        Assert.True(f.Model.ClearSop(f.Unit, running: false));
        Assert.True(f.Model.ClearTask(f.Unit, running: false));
        var cleared = f.Model.Read(f.Unit)!;
        Assert.Null(cleared.Task);
        Assert.Null(cleared.Sop);
    }

    [Fact]
    public void CE3043_Paused_TheGateStillDecides_AnOperatorsOrderIsNotReplaced()
    {
        var f = new Fixture();
        f.World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = f.Unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Operator });
        f.World.Bus.SwapBuffers();
        new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(f.Registry).Execute(f.World, 0.016f);

        Assert.False(f.Model.ApplyTask(f.Unit, "Demo_TakeCover", "{}", running: false));
        Assert.Equal("Idle", f.Model.Read(f.Unit)!.Task);
    }

    [Fact]
    public void CE3043_Running_AnEditIsAnEvent_NotADirectWrite()
    {
        var f = new Fixture();
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: true));
        Assert.Null(f.Model.Read(f.Unit)!.Task);                               // nothing changed yet
        f.World.Bus.SwapBuffers();
        var sent = Assert.Single(f.World.Bus.ReadManaged<AssignBehaviorEvent>());
        Assert.Equal("Idle", sent.BehaviorName);
        Assert.Equal(BehaviorOrigin.Superior, sent.Origin);
    }

    [Fact]
    public void CE3043_TheSopRow_OffersOnlyBehavioursKnownToDriveNoChannel()
    {
        var f = new Fixture();
        var choices = f.Model.SopChoices();
        Assert.Contains("BasicInfantrySop", choices);                          // a BTree with a known-empty set
        Assert.DoesNotContain("Idle", choices);                                // hand-written: unknown ⇒ not offered
        foreach (var name in choices)
        {
            Assert.True(f.Registry.TryGetId(name, out int id));
            Assert.True(f.Registry.TryGetDefinition(id, out var d));
            Assert.Empty(d!.WritesChannels!);
        }
        Assert.Contains("Idle", f.Model.TaskChoices());
    }

    [Fact]
    public void CE2084_TheSopSlot_RefusesABehaviourKnownToDriveAChannel()
    {
        var f = new Fixture();
        f.Registry.Register(0x7A01, "SopT_Mover", new BehaviorDefinition
        {
            Name = "SopT_Mover", BrainTier = BehaviorConstants.BrainTierBTree, WritesChannels = new[] { typeof(LocomotionChannel) },
        });
        Assert.False(f.Model.ApplySop(f.Unit, "SopT_Mover", "{}", running: false));
        Assert.Null(f.Model.Read(f.Unit)!.Sop);
    }

    [Fact]
    public void CE3043_TheParamsForm_WritesWhatTheBehaviourReads()
    {
        // the authored params type of a real generated behaviour; the form's JSON must parse back through BehaviorParams
        var f = new Fixture();
        var type = f.Registry.GetRegisteredNames().Select(f.Model.ParamsTypeOf).First(t => t is { IsValueType: true })!;
        object value = BehaviorParamsForm.Hydrate(type, "{}");
        string json = BehaviorParamsForm.ToJson(value, type);
        Assert.Equal(json, BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(type, json), type));
    }

    [Fact]
    public void CE3043_TheScenarioCatalogue_OffersTheAiSection()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        Assert.Contains(EntityAiDetailsViewDescriptor.ViewId, editor.ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id));
    }
}
